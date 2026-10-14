#include <windows.h>
#include <d3d11.h>
#include <atomic>
#include <thread>
#include <stdexcept>
#include <cstdio>
#include "../../src/NativeFrameContract.h"

static void Check(bool valid,const char* message) { if(!valid) throw std::runtime_error(message); }
template<class T> T Load(HMODULE dll,const char* name) { auto value=GetProcAddress(dll,name); Check(value!=nullptr,name); return reinterpret_cast<T>(value); }
static std::atomic<int> destroyed{0};
class LifetimeMarker final:public IUnknown {
    std::atomic<ULONG> count{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** value) override { if(id==__uuidof(IUnknown)) { *value=this; AddRef(); return S_OK; } *value=nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++count; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value=--count; if(!value) { ++destroyed; delete this; } return value; }
};
static ID3D11Texture2D* Texture(ID3D11Device* device,UINT size=16) {
    D3D11_TEXTURE2D_DESC desc{}; desc.Width=desc.Height=size; desc.MipLevels=desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count=1; desc.BindFlags=D3D11_BIND_RENDER_TARGET;
    ID3D11Texture2D* result=nullptr; Check(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&result)),"create WARP texture");
    auto* marker=new LifetimeMarker;
    const GUID id={0x6587dcaa,0x5c9a,0x4e66,{0x95,0xe6,0x82,0x67,0x31,0x94,0xb2,0x88}};
    Check(SUCCEEDED(result->SetPrivateDataInterface(id,marker)),"set lifetime marker"); marker->Release(); return result;
}
int main(int argc,char** argv) {
    try {
        Check(argc==2,"pass test DLL path"); HMODULE dll=LoadLibraryA(argv[1]); Check(dll!=nullptr,"load test DLL");
        auto event=static_cast<void (__stdcall*)(int)>(Load<void* (*)()>(dll,"GetRenderEventFunc")());
        auto set=Load<void (*)(void*,void*)>(dll,"SetEyeTextures");
        auto frame=Load<void (*)(uint64_t,uint64_t)>(dll,"SetFrameState");
        auto configure=Load<void (*)(int,int,int,int,int)>(dll,"TestConfigureRuntime");
        auto calls=Load<uint64_t (*)(int)>(dll,"TestGetRuntimeCalls");
        auto status=Load<int (*)()>(dll,"GetBridgeStatus"); auto waitError=Load<int (*)()>(dll,"GetPoseWaitError");
        auto diagnostics=Load<int (*)(NativeFrameDiagnostics*,uint32_t)>(dll,"GetNativeFrameDiagnostics");
        auto block=Load<void (*)()>(dll,"TestBlockNextWait");
        auto blocked=Load<int (*)(int)>(dll,"TestWaitForBlockedWait"); auto unblock=Load<void (*)()>(dll,"TestUnblockWait");
        NativeFrameDiagnostics diag{};
        Check(!diagnostics(&diag,sizeof(diag)-1),"reject undersized diagnostics");
        frame(1,1); event(3); diagnostics(&diag,sizeof(diag)); Check(diag.completedDrainGeneration==1,"empty drain acknowledged");
        frame(1,1); event(0); Check(status()==BridgeStaleFrame,"drained generation cannot be resurrected");
        frame(0,1); event(0); Check(status()==BridgeStaleFrame,"zero generation rejected");
        frame(2,0); event(0); Check(status()==BridgeStaleFrame,"zero frame rejected");
        frame(2,1); event(0); Check(status()==BridgeInvalidTextures && calls(0)==0,"null textures rejected before runtime wait");
        ID3D11Device* device=nullptr; ID3D11DeviceContext* context=nullptr;
        Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)),"create D3D11 WARP device");
        auto* left=Texture(device); auto* right=Texture(device); set(left,right);
        frame(2,2); event(1); Check(calls(1)==0 && status()==BridgeStaleFrame,"submit without wait rejected");
        configure(1,101,1,0,0); frame(2,3); event(0); event(1);
        Check(waitError()==101 && calls(1)==0,"pose wait error prevents submit");
        configure(1,0,0,0,0); frame(2,4); event(0); Check(status()==BridgeInvalidPose,"invalid pose rejected"); event(1); Check(calls(1)==0,"invalid pose never submitted");
        configure(1,0,1,0,0); frame(2,5); event(0); event(1); Check(calls(1)==2,"one eye pair submitted");
        diagnostics(&diag,sizeof(diag)); Check(diag.submittedPairs==1 && diag.completedSubmitFrame==5 && diag.version==1,"submission diagnostics");
        event(1); Check(calls(1)==2,"duplicate submit rejected"); event(0); Check(status()==BridgeDuplicateFrame,"duplicate wait rejected");
        frame(2,6); event(0); frame(2,7); event(1); Check(calls(1)==2,"frame changed after wait rejected");
        frame(1,99); event(0); Check(status()==BridgeStaleFrame,"older generation rejected");
        configure(0,0,1,0,0); frame(3,1); event(0); Check(waitError()==BridgeRuntimeUnavailable,"missing runtime reported");
        configure(1,0,1,0,104); frame(3,2); event(0); event(1); diagnostics(&diag,sizeof(diag));
        Check(calls(1)==4 && diag.submittedPairs==1 && diag.rightSubmitError==104,"partial submit not counted as pair"); event(1); Check(calls(1)==4,"partial pair cannot be retried");
        configure(1,0,1,0,0); frame(3,3); event(0); auto* replacement=Texture(device); set(left,replacement);
        right->Release(); left->Release(); replacement->Release(); Check(destroyed==0,"queued and active references survive caller release");
        event(1); Check(calls(1)==4,"replaced textures invalidate pose association"); Check(destroyed==1,"retired texture released by render callback");
        event(3); context->ClearState(); context->Flush(); Check(destroyed==3,"drain releases active and retired textures on render callback");
        auto* mismatchLeft=Texture(device); auto* mismatchRight=Texture(device,8); set(mismatchLeft,mismatchRight); frame(4,1); event(0); Check(status()==BridgeInvalidTextures,"mismatched dimensions rejected");
        mismatchLeft->Release(); mismatchRight->Release(); event(3);
        ID3D11Device* otherDevice=nullptr; ID3D11DeviceContext* otherContext=nullptr;
        Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&otherDevice,nullptr,&otherContext)),"create second WARP device");
        left=Texture(device); right=Texture(otherDevice); set(left,right); frame(5,1); event(0); Check(status()==BridgeInvalidTextures,"different devices rejected");
        left->Release(); right->Release(); event(3); otherContext->Release(); otherDevice->Release();
        left=Texture(device); set(left,left); frame(6,1); event(0); Check(status()==BridgeInvalidTextures,"same texture for both eyes rejected"); left->Release(); event(3);
        left=Texture(device); right=Texture(device); set(left,right); frame(7,1); block();
        std::atomic<bool> waitEntered{false};
        std::thread update([&] { waitEntered=blocked(2000)!=0; frame(8,1); set(nullptr,nullptr); unblock(); });
        event(0); update.join(); Check(waitEntered,"wait entered"); Check(status()==BridgeStaleFrame,"update during wait invalidates completed result");
        diagnostics(&diag,sizeof(diag)); Check(diag.completedPoseGeneration==7 && diag.completedPoseFrame==1,"completed wait keeps original identity");
        left->Release(); right->Release(); event(3); diagnostics(&diag,sizeof(diag)); Check(diag.completedDrainGeneration==8,"updated generation drain acknowledged");
        uint64_t before=calls(1); event(1); Check(calls(1)==before,"queued submit after drain rejected"); event(3);
        // Startup/standby recovery keeps the same runtime, generation and owned
        // textures. Only a fresh, acknowledged valid pose can reach submission.
        left=Texture(device); right=Texture(device); set(left,right);
        configure(1,0,0,0,0); frame(9,1); event(0); diagnostics(&diag,sizeof(diag));
        Check(diag.status==BridgeInvalidPose && diag.poseWaitError==0 && diag.completedPoseGeneration==9 && diag.completedPoseFrame==1,"recoverable invalid pose has an exact successful wait acknowledgment");
        event(1); Check(calls(1)==before,"first invalid recovery sample cannot submit");
        frame(9,2); event(0); diagnostics(&diag,sizeof(diag));
        Check(diag.status==BridgeInvalidPose && diag.completedPoseFrame==2,"fresh invalid pose acknowledgment advances without resetting runtime or textures");
        event(1); Check(calls(1)==before,"repeated invalid recovery sample cannot submit");
        const uint64_t pairsBeforeRecovery=diag.submittedPairs;
        configure(1,0,1,0,0); frame(9,3); event(0); diagnostics(&diag,sizeof(diag));
        Check(diag.status==BridgeOk && diag.poseWaitError==0 && diag.completedPoseFrame==3,"fresh valid pose reacquired within the same generation");
        event(1); diagnostics(&diag,sizeof(diag));
        Check(calls(1)==before+2 && diag.submittedPairs==pairsBeforeRecovery+1 && diag.completedSubmitGeneration==9 && diag.completedSubmitFrame==3,"only the valid reacquired sample submits one complete pair");
        left->Release(); right->Release(); event(3);
        std::thread wrongThread([&] { event(0); }); wrongThread.join(); Check(status()==BridgeWrongRenderThread,"wrong render thread rejected");
        context->ClearState(); context->Flush(); context->Release(); device->Release();
        FreeLibrary(dll); std::puts("PASS native external ABI: pose errors, invalid-to-valid reacquisition, invalid/null/replaced textures, generation/frame association, duplicate/partial pairs, concurrent update, COM ownership, empty/repeated drain, render thread, versioned diagnostics"); return 0;
    } catch(const std::exception& error) { std::fprintf(stderr,"FAIL: %s\n",error.what()); return 1; }
}
