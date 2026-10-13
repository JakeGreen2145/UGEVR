#include <atomic>
#include <cstdint>
#include <d3d11.h>
#include <cstdio>
#include <string>
#include <mutex>
#include "../vendor/openvr.h"

// Called by GL.IssuePluginEvent after both Camera.Render command streams.
// No D3D immediate-context operations occur on the managed/main thread.
static void* textures[2] = {};
static std::atomic<uint64_t> submitted{0};
static std::atomic<int> leftError{-1}, rightError{-1};
static std::atomic<int> renderStage{0};
extern "C" __declspec(dllexport) int GetRenderStage() { return renderStage.load(); }
static std::atomic<int> mirrorStatus{-1};
static std::mutex mirrorMutex;
static std::string mirrorPath;
extern "C" __declspec(dllexport) void RequestMirrorCapture(const char* path) {
    std::lock_guard<std::mutex> lock(mirrorMutex); mirrorPath = path;
}
extern "C" __declspec(dllexport) int GetMirrorStatus() { return mirrorStatus.load(); }
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
static void CaptureMirror(vr::IVRCompositor* compositor) {
    std::string path;
    { std::lock_guard<std::mutex> lock(mirrorMutex); path.swap(mirrorPath); }
    if (path.empty()) return;
    ID3D11Device* device = nullptr;
    auto submittedTexture=reinterpret_cast<ID3D11Texture2D*>(textures[0]);
    submittedTexture->GetDevice(&device);
    SaveTexture(device, submittedTexture, path+"-submitted.bmp");
    ID3D11ShaderResourceView* view = nullptr;
    auto error = compositor->GetMirrorTextureD3D11(vr::Eye_Left, device, (void**)&view);
    if (error != vr::VRCompositorError_None || !view) { mirrorStatus = (int)error; device->Release(); return; }
    ID3D11Resource* resource = nullptr; view->GetResource(&resource);
    ID3D11Texture2D* source = nullptr; resource->QueryInterface(__uuidof(ID3D11Texture2D), (void**)&source);
    resource->Release();
    if (!source) mirrorStatus = -3;
    else { mirrorStatus=SaveTexture(device,source,path); source->Release(); }
    compositor->ReleaseMirrorTextureD3D11(view); device->Release();
}
extern "C" __declspec(dllexport) void SetEyeTextures(void* left, void* right) {
    textures[0] = left; textures[1] = right;
}
static void __stdcall RenderEvent(int eventId) {
    if (!textures[0] || !textures[1]) return;
    auto compositor = vr::VRCompositor();
    if (!compositor) { leftError = rightError = -2; return; }
    if (eventId == 0) {
        renderStage = 10;
        vr::TrackedDevicePose_t renderPoses[vr::k_unMaxTrackedDeviceCount]{};
        vr::TrackedDevicePose_t gamePoses[vr::k_unMaxTrackedDeviceCount]{};
        compositor->WaitGetPoses(renderPoses, vr::k_unMaxTrackedDeviceCount, gamePoses, vr::k_unMaxTrackedDeviceCount);
        renderStage = 11;
        return;
    }
    if (eventId == 2) { CaptureMirror(compositor); return; }
    if (eventId != 1) return;
    renderStage = 1;
    ID3D11Device* gameDevice = nullptr;
    reinterpret_cast<ID3D11Texture2D*>(textures[0])->GetDevice(&gameDevice);
    ID3D11DeviceContext* gameContext = nullptr; gameDevice->GetImmediateContext(&gameContext);
    gameContext->Flush(); gameContext->Release(); gameDevice->Release();
    renderStage = 2;
    // Unity's D3D11 render textures use bottom-up UVs relative to OpenVR.
    vr::VRTextureBounds_t bounds{0.f, 1.f, 1.f, 0.f};
    vr::Texture_t left{textures[0], vr::TextureType_DirectX, vr::ColorSpace_Auto};
    vr::Texture_t right{textures[1], vr::TextureType_DirectX, vr::ColorSpace_Auto};
    leftError = compositor->Submit(vr::Eye_Left, &left, &bounds);
    renderStage = 3;
    rightError = compositor->Submit(vr::Eye_Right, &right, &bounds);
    renderStage = 4;
    if (leftError == 0 && rightError == 0) ++submitted;
    // Unity has not executed desktop Present yet. WaitGetPoses on the next
    // frame performs the implicit handoff. Calling PostPresentHandoff here
    // violates its after-Present contract and can block frame completion.
    gameDevice = nullptr;
    reinterpret_cast<ID3D11Texture2D*>(textures[0])->GetDevice(&gameDevice);
    gameDevice->GetImmediateContext(&gameContext);
    gameContext->Flush(); gameContext->Release(); gameDevice->Release();
    renderStage = 5;
}
extern "C" __declspec(dllexport) void* GetRenderEventFunc() { return (void*)&RenderEvent; }
extern "C" __declspec(dllexport) uint64_t GetSubmittedFrames() { return submitted.load(); }
extern "C" __declspec(dllexport) int GetSubmitError(int eye) { return eye == 0 ? leftError.load() : rightError.load(); }
