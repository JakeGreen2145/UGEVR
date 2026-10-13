#include <atomic>
#include <cstdint>
#include <d3d11.h>
#include <cstdio>
#include <string>
#include <mutex>
#include <vector>
#include <cmath>
#include "../vendor/openvr.h"
#include "NativeFrameContract.h"

#define EXPORT extern "C" __declspec(dllexport)
static std::mutex stateMutex, mirrorMutex;
static ID3D11Texture2D* textures[2]{};
static std::vector<ID3D11Texture2D*> retired;
static std::string mirrorPath;
static std::atomic<int> renderStage{0}, mirrorStatus{-1};
static NativeFrameDiagnostics diag=[] { NativeFrameDiagnostics value{}; value.size=sizeof(value); value.version=1; value.status=BridgeNotReady; value.poseWaitError=BridgeNotReady; value.leftSubmitError=value.rightSubmitError=BridgeNotReady; return value; }();
static uint64_t waitedGeneration{}, waitedFrame{}, waitedRevision{};
static uint64_t lastWaitGeneration{}, lastWaitFrame{};
static bool requestedValid{}, poseReady{};
static DWORD renderThread{};

#ifdef UGEVR_NATIVE_CONTRACT_TEST
// The test DLL uses the real exported bridge and WARP textures. Only the runtime
// boundary is replaced, so no SteamVR process or global configuration is touched.
static int testWaitError{}, testLeftError{}, testRightError{};
static bool testRuntimeAvailable=true, testPoseValid=true;
static std::atomic<uint64_t> testWaitCalls{0}, testSubmitCalls{0};
static HANDLE testWaitEntered=CreateEvent(nullptr,TRUE,FALSE,nullptr);
static HANDLE testWaitRelease=CreateEvent(nullptr,TRUE,TRUE,nullptr);
EXPORT void TestBlockNextWait() { ResetEvent(testWaitEntered); ResetEvent(testWaitRelease); }
EXPORT int TestWaitForBlockedWait(int timeout) { return WaitForSingleObject(testWaitEntered,timeout)==WAIT_OBJECT_0; }
EXPORT void TestUnblockWait() { SetEvent(testWaitRelease); }
EXPORT void TestConfigureRuntime(int available, int waitError, int validPose, int left, int right) {
    std::lock_guard<std::mutex> lock(stateMutex);
    testRuntimeAvailable=available!=0; testWaitError=waitError; testPoseValid=validPose!=0;
    testLeftError=left; testRightError=right;
}
EXPORT uint64_t TestGetRuntimeCalls(int submit) { return submit ? testSubmitCalls.load() : testWaitCalls.load(); }
#endif

struct Runtime {
    vr::IVRCompositor* compositor{};
    bool Available() {
#ifdef UGEVR_NATIVE_CONTRACT_TEST
        return testRuntimeAvailable;
#else
        compositor=vr::VRCompositor(); return compositor!=nullptr;
#endif
    }
    int Wait(vr::TrackedDevicePose_t* poses) {
#ifdef UGEVR_NATIVE_CONTRACT_TEST
        ++testWaitCalls;
        SetEvent(testWaitEntered); WaitForSingleObject(testWaitRelease,5000);
        poses[0].bPoseIsValid=testPoseValid; poses[0].bDeviceIsConnected=true;
        for(int i=0;i<3;++i) poses[0].mDeviceToAbsoluteTracking.m[i][i]=1;
        return testWaitError;
#else
        vr::TrackedDevicePose_t gamePoses[vr::k_unMaxTrackedDeviceCount]{};
        return compositor->WaitGetPoses(poses,vr::k_unMaxTrackedDeviceCount,gamePoses,vr::k_unMaxTrackedDeviceCount);
#endif
    }
    int Submit(vr::EVREye eye, void* texture) {
#ifdef UGEVR_NATIVE_CONTRACT_TEST
        ++testSubmitCalls; return eye==vr::Eye_Left ? testLeftError : testRightError;
#else
        // Unity render targets use bottom-up UVs relative to OpenVR.
        vr::VRTextureBounds_t bounds{0.f,1.f,1.f,0.f};
        vr::Texture_t value{texture,vr::TextureType_DirectX,vr::ColorSpace_Auto};
        return compositor->Submit(eye,&value,&bounds);
#endif
    }
};

static void Reject(int status) { diag.status=status; ++diag.rejectedEvents; }
static void ReleaseRetired() {
    for(auto* texture:retired) texture->Release();
    retired.clear();
}
static bool ValidTextures(ID3D11Device** output) {
    if(!textures[0] || !textures[1] || textures[0]==textures[1]) return false;
    ID3D11Device* left=nullptr; ID3D11Device* right=nullptr;
    textures[0]->GetDevice(&left); textures[1]->GetDevice(&right);
    bool valid=left && right && left==right;
    D3D11_TEXTURE2D_DESC desc[2]{};
    for(int i=0;i<2;++i) {
        textures[i]->GetDesc(&desc[i]);
        valid=valid && desc[i].Width && desc[i].Height && desc[i].ArraySize==1 &&
            desc[i].MipLevels==1 && desc[i].SampleDesc.Count==1 &&
            (desc[i].BindFlags & D3D11_BIND_RENDER_TARGET)!=0;
        auto format=desc[i].Format;
        valid=valid && (format==DXGI_FORMAT_R8G8B8A8_UNORM || format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB ||
            format==DXGI_FORMAT_R8G8B8A8_TYPELESS || format==DXGI_FORMAT_B8G8R8A8_UNORM ||
            format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB || format==DXGI_FORMAT_B8G8R8A8_TYPELESS);
    }
    valid=valid && desc[0].Width==desc[1].Width && desc[0].Height==desc[1].Height && desc[0].Format==desc[1].Format;
    if(right) right->Release();
    if(!valid) { if(left) left->Release(); return false; }
    *output=left; return true;
}
EXPORT void SetEyeTextures(void* left, void* right) {
    std::lock_guard<std::mutex> lock(stateMutex);
    auto* a=static_cast<ID3D11Texture2D*>(left); auto* b=static_cast<ID3D11Texture2D*>(right);
    if(a==textures[0] && b==textures[1]) return;
    // The caller must pass live COM pointers. AddRef is safe here; immediate
    // context operations and all bridge-owned Releases occur in RenderEvent.
    if(a) a->AddRef(); if(b) b->AddRef();
    for(auto* texture:textures) if(texture) retired.push_back(texture);
    textures[0]=a; textures[1]=b; ++diag.textureRevision; poseReady=false;
}
EXPORT void SetFrameState(uint64_t generation, uint64_t frame) {
    std::lock_guard<std::mutex> lock(stateMutex);
    requestedValid=generation && frame && generation>diag.completedDrainGeneration && generation>=diag.generation &&
        (generation!=diag.generation || frame>=diag.frame);
    if(!requestedValid) { poseReady=false; Reject(BridgeStaleFrame); return; }
    if(generation!=diag.generation || frame!=diag.frame) poseReady=false;
    diag.generation=generation; diag.frame=frame;
}
EXPORT int GetNativeFrameDiagnostics(NativeFrameDiagnostics* output, uint32_t size) {
    if(!output || size<sizeof(NativeFrameDiagnostics)) return 0;
    std::lock_guard<std::mutex> lock(stateMutex); *output=diag; return 1;
}
#define GETTER(name, field, type) EXPORT type name() { std::lock_guard<std::mutex> lock(stateMutex); return diag.field; }
GETTER(GetPoseWaitError,poseWaitError,int)
GETTER(GetBridgeStatus,status,int)
GETTER(GetCompletedPoseFrame,completedPoseFrame,uint64_t)
GETTER(GetCompletedPoseGeneration,completedPoseGeneration,uint64_t)
GETTER(GetCompletedSubmitFrame,completedSubmitFrame,uint64_t)
GETTER(GetCompletedSubmitGeneration,completedSubmitGeneration,uint64_t)
GETTER(GetCompletedDrainGeneration,completedDrainGeneration,uint64_t)
GETTER(GetSubmittedFrames,submittedPairs,uint64_t)
EXPORT int GetSubmitError(int eye) { std::lock_guard<std::mutex> lock(stateMutex); return eye==0 ? diag.leftSubmitError : diag.rightSubmitError; }
EXPORT int GetRenderStage() { return renderStage.load(); }
EXPORT int GetMirrorStatus() { return mirrorStatus.load(); }
EXPORT void RequestMirrorCapture(const char* path) {
    std::lock_guard<std::mutex> lock(mirrorMutex); mirrorPath=path ? path : "";
}
static int SaveTexture(ID3D11Device* device, ID3D11Texture2D* source, const std::string& path) {
    int status = -1;
    D3D11_TEXTURE2D_DESC desc; source->GetDesc(&desc);
    FILE* metadata=nullptr; fopen_s(&metadata,(path+".txt").c_str(),"w");
    if(metadata) { fprintf(metadata,"width=%u height=%u format=%u arraySize=%u mipLevels=%u sampleCount=%u\n",desc.Width,desc.Height,desc.Format,desc.ArraySize,desc.MipLevels,desc.SampleDesc.Count); fclose(metadata); }
    desc.Usage = D3D11_USAGE_STAGING; desc.BindFlags = 0; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ; desc.MiscFlags = 0;
    ID3D11Texture2D* staging = nullptr;
    HRESULT result = device->CreateTexture2D(&desc, nullptr, &staging);
    ID3D11DeviceContext* context = nullptr; device->GetImmediateContext(&context);
    if (SUCCEEDED(result)) {
        context->CopyResource(staging, source);
        D3D11_MAPPED_SUBRESOURCE mapped;
        result = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(result)) {
            FILE* file = nullptr; fopen_s(&file, path.c_str(), "wb");
            if (file) {
                BITMAPFILEHEADER fh{}; BITMAPINFOHEADER ih{};
                fh.bfType = 0x4d42; fh.bfOffBits = sizeof(fh) + sizeof(ih); fh.bfSize = fh.bfOffBits + desc.Width * desc.Height * 4;
                ih.biSize = sizeof(ih); ih.biWidth = desc.Width; ih.biHeight = -(LONG)desc.Height; ih.biPlanes = 1; ih.biBitCount = 32; ih.biCompression = BI_RGB;
                fwrite(&fh, sizeof(fh), 1, file); fwrite(&ih, sizeof(ih), 1, file);
                bool rgba = desc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM || desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
                bool bgra = desc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS || desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM || desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
                if (rgba || bgra) {
                    for (UINT y = 0; y < desc.Height; ++y) {
                        const unsigned char* row = (const unsigned char*)mapped.pData + y * mapped.RowPitch;
                        for (UINT x = 0; x < desc.Width; ++x) {
                            unsigned char pixel[4] = {row[x*4 + (rgba ? 2 : 0)],row[x*4+1],row[x*4 + (rgba ? 0 : 2)],255};
                            fwrite(pixel, 4, 1, file);
                        }
                    }
                    status = 0;
                } else status = -(int)desc.Format - 1000;
                fclose(file);
            } else status = -4;
            context->Unmap(staging, 0);
        } else status = -5;
        staging->Release();
    } else status = -6;
    context->Release();
    return status;
}

static void CaptureMirror(Runtime& runtime) {
    std::string path;
    { std::lock_guard<std::mutex> lock(mirrorMutex); path.swap(mirrorPath); }
    if(path.empty()) return;
    if(!textures[0] || !textures[1] || diag.status!=BridgeOk || !diag.submittedPairs ||
        diag.completedSubmitGeneration!=diag.generation || diag.completedSubmitFrame!=diag.frame ||
        waitedRevision!=diag.textureRevision || diag.leftSubmitError || diag.rightSubmitError) {
        mirrorStatus=BridgeNotReady; return;
    }
#ifdef UGEVR_NATIVE_CONTRACT_TEST
    mirrorStatus=BridgeRuntimeUnavailable;
#else
    ID3D11Device* device=nullptr; textures[0]->GetDevice(&device);
    if(!device) { mirrorStatus=BridgeInvalidTextures; return; }
    int saved=SaveTexture(device,textures[0],path+"-submitted.bmp");
    if(saved!=0) { mirrorStatus=saved; device->Release(); return; }
    ID3D11ShaderResourceView* view=nullptr;
    auto error=runtime.compositor->GetMirrorTextureD3D11(vr::Eye_Left,device,(void**)&view);
    if(error!=vr::VRCompositorError_None || !view) { mirrorStatus=error ? (int)error : BridgeNotReady; device->Release(); return; }
    ID3D11Resource* resource=nullptr; view->GetResource(&resource);
    ID3D11Texture2D* source=nullptr;
    if(resource) { resource->QueryInterface(__uuidof(ID3D11Texture2D),(void**)&source); resource->Release(); }
    if(!source) mirrorStatus=BridgeInvalidTextures;
    else { mirrorStatus=SaveTexture(device,source,path); source->Release(); }
    runtime.compositor->ReleaseMirrorTextureD3D11(view); device->Release();
#endif
}
static void __stdcall RenderEvent(int eventId) {
    std::unique_lock<std::mutex> lock(stateMutex);
    if(eventId<0 || eventId>3) { Reject(BridgeUnknownEvent); return; }
    const DWORD thread=GetCurrentThreadId();
    if(renderThread && renderThread!=thread) { Reject(BridgeWrongRenderThread); return; }
    renderThread=thread;
    ReleaseRetired();
    if(eventId==3) {
        for(auto*& texture:textures) { if(texture) texture->Release(); texture=nullptr; }
        ++diag.textureRevision; poseReady=false; requestedValid=false;
        diag.completedDrainGeneration=diag.generation; diag.status=BridgeDrained;
        { std::lock_guard<std::mutex> mirrorLock(mirrorMutex); mirrorPath.clear(); }
        renderStage=30; return;
    }
    struct Completion {
        int event; uint64_t generation, frame;
        ~Completion() {
            if(event==0) { diag.completedPoseGeneration=generation; diag.completedPoseFrame=frame; }
            if(event==1) { diag.completedSubmitGeneration=generation; diag.completedSubmitFrame=frame; }
        }
    } completion{eventId,diag.generation,diag.frame};
    if(eventId==1) diag.leftSubmitError=diag.rightSubmitError=BridgeNotReady;
    if(eventId==0) {
        // Legacy callers retain their original signatures but must set frame
        // identity through the additive API to receive verified transport.
        diag.poseWaitError=BridgeNotReady;
    }
    if(!requestedValid) { Reject(BridgeStaleFrame); return; }
    Runtime runtime;
    if(!runtime.Available()) {
        if(eventId==0) diag.poseWaitError=BridgeRuntimeUnavailable;
        Reject(BridgeRuntimeUnavailable); poseReady=false; return;
    }
    if(eventId==0) {
        poseReady=false;
        if(lastWaitGeneration==diag.generation && lastWaitFrame>=diag.frame) { Reject(BridgeDuplicateFrame); return; }
        ID3D11Device* device=nullptr;
        if(!ValidTextures(&device)) { Reject(BridgeInvalidTextures); return; }
        auto removed=device->GetDeviceRemovedReason(); device->Release();
        if(FAILED(removed)) { Reject(BridgeDeviceRemoved); return; }
        const uint64_t generation=diag.generation, frame=diag.frame, revision=diag.textureRevision;
        lastWaitGeneration=generation; lastWaitFrame=frame;
        vr::TrackedDevicePose_t poses[vr::k_unMaxTrackedDeviceCount]{};
        renderStage=10;
        // Do not hold the producer mutex during the runtime pacing call. Updates
        // invalidate this result, and COM refs remain owned until a render event.
        lock.unlock(); int error=runtime.Wait(poses); lock.lock();
        diag.poseWaitError=error;
        diag.completedPoseGeneration=generation; diag.completedPoseFrame=frame;
        renderStage=11;
        if(generation!=diag.generation || frame!=diag.frame || revision!=diag.textureRevision || !requestedValid) { Reject(BridgeStaleFrame); return; }
        if(error!=vr::VRCompositorError_None) { Reject(error); return; }
        bool valid=poses[0].bPoseIsValid && poses[0].bDeviceIsConnected;
        for(const auto& row:poses[0].mDeviceToAbsoluteTracking.m) for(float value:row) valid=valid && std::isfinite(value);
        if(!valid) { Reject(BridgeInvalidPose); return; }
        waitedGeneration=generation; waitedFrame=frame; waitedRevision=revision;
        poseReady=true; diag.status=BridgeOk; return;
    }
    if(eventId==2) { CaptureMirror(runtime); return; }
    if(!poseReady || waitedGeneration!=diag.generation || waitedFrame!=diag.frame || waitedRevision!=diag.textureRevision) { Reject(BridgeStaleFrame); return; }
    // Consume even a partially failed pair. Retrying one eye under this pose
    // could produce an AlreadySubmitted result or a temporally mixed pair.
    poseReady=false;
    ID3D11Device* device=nullptr;
    if(!ValidTextures(&device)) { Reject(BridgeInvalidTextures); return; }
    if(FAILED(device->GetDeviceRemovedReason())) { device->Release(); Reject(BridgeDeviceRemoved); return; }
    ID3D11DeviceContext* context=nullptr; device->GetImmediateContext(&context);
    if(!context) { device->Release(); Reject(BridgeInvalidTextures); return; }
    renderStage=1;
    // Flush dispatches queued commands. It is not a GPU completion fence.
    // Unity issued both Camera.Render command streams before this callback.
    const uint64_t generation=diag.generation, frame=diag.frame, revision=diag.textureRevision;
    auto* left=textures[0]; auto* right=textures[1];
    // Setters retain replaced references in retired until this render callback
    // returns. Releasing the mutex keeps status/stop polling responsive even if
    // a foreign runtime Submit blocks. There is no safe forced DLL unload.
    lock.unlock();
    context->Flush(); renderStage=2;
    int leftResult=runtime.Submit(vr::Eye_Left,left); renderStage=3;
    int rightResult=runtime.Submit(vr::Eye_Right,right); renderStage=4;
    // Desktop Present has not run. The next WaitGetPoses does implicit handoff;
    // PostPresentHandoff must not be called before Present.
    context->Flush(); context->Release(); device->Release(); renderStage=5;
    lock.lock();
    diag.leftSubmitError=leftResult; diag.rightSubmitError=rightResult;
    diag.status=leftResult ? leftResult : rightResult;
    if(diag.status==BridgeOk) ++diag.submittedPairs;
    else ++diag.rejectedEvents;
    if(generation!=diag.generation || frame!=diag.frame || revision!=diag.textureRevision || !requestedValid) Reject(BridgeStaleFrame);
}
EXPORT void* GetRenderEventFunc() { return (void*)&RenderEvent; }
