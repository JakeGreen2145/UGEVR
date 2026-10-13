// Diagnostic simulated HMD. This does not represent physical headset tracking.
// Interface implementation follows Valve's simplehmd sample; no controllers.
#include "openvr_driver.h"
#include "pose_path.h"
#include <Windows.h>
#include <cmath>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>

namespace {
constexpr char kSerial[] = "unityvr-diagnostic-simulated-hmd-001";
constexpr double kPi = 3.14159265358979323846;
struct InputPose { double x=0, y=1.7, z=0, yaw=0, pitch=0, roll=0; bool motion=false; };
vr::HmdQuaternion_t Multiply(const vr::HmdQuaternion_t& a, const vr::HmdQuaternion_t& b) {
    return {a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z,
        a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,
        a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
        a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w};
}
vr::HmdQuaternion_t Rotation(double yaw, double pitch, double roll) {
    const double factor = kPi/360.0;
    const vr::HmdQuaternion_t qy{std::cos(yaw*factor),0,std::sin(yaw*factor),0};
    const vr::HmdQuaternion_t qx{std::cos(pitch*factor),std::sin(pitch*factor),0,0};
    const vr::HmdQuaternion_t qz{std::cos(roll*factor),0,0,std::sin(roll*factor)};
    return Multiply(Multiply(qy,qx),qz);
}
class SimulatedHmd final : public vr::ITrackedDeviceServerDriver, public vr::IVRDisplayComponent {
    uint32_t index_=vr::k_unTrackedDeviceIndexInvalid;
    InputPose input_;
    vr::DriverPose_t pose_{};
    ULONGLONG start_=0;
    std::string last_line_;
public:
    vr::EVRInitError Activate(uint32_t index) override {
        index_=index; start_=GetTickCount64();
        const auto container=vr::VRProperties()->TrackedDeviceToPropertyContainer(index_);
        auto props=vr::VRProperties();
        props->SetStringProperty(container,vr::Prop_TrackingSystemName_String,"unityvrtest");
        props->SetStringProperty(container,vr::Prop_ModelNumber_String,"DIAGNOSTIC SIMULATED HMD");
        props->SetStringProperty(container,vr::Prop_ManufacturerName_String,"Task diagnostic simulation");
        props->SetStringProperty(container,vr::Prop_SerialNumber_String,kSerial);
        props->SetBoolProperty(container,vr::Prop_WillDriftInYaw_Bool,false);
        props->SetBoolProperty(container,vr::Prop_DeviceIsWireless_Bool,false);
        props->SetBoolProperty(container,vr::Prop_ContainsProximitySensor_Bool,false);
        props->SetBoolProperty(container,vr::Prop_HasDisplayComponent_Bool,true);
        props->SetBoolProperty(container,vr::Prop_IsOnDesktop_Bool,false);
        props->SetBoolProperty(container,vr::Prop_DisplayDebugMode_Bool,true);
        props->SetFloatProperty(container,vr::Prop_UserIpdMeters_Float,0.064f);
        props->SetFloatProperty(container,vr::Prop_DisplayFrequency_Float,90.0f);
        props->SetFloatProperty(container,vr::Prop_UserHeadToEyeDepthMeters_Float,0.0f);
        props->SetFloatProperty(container,vr::Prop_SecondsFromVsyncToPhotons_Float,0.0f);
        props->SetUint64Property(container,vr::Prop_CurrentUniverseId_Uint64,42);
        vr::HmdMatrix34_t left{},right{};
        for(int i=0;i<3;++i) { left.m[i][i]=1; right.m[i][i]=1; }
        left.m[0][3]=-0.032f; right.m[0][3]=0.032f;
        vr::VRServerDriverHost()->SetDisplayEyeToHead(index_,left,right);
        vr::VRDriverLog()->Log("DIAGNOSTIC SIMULATED HMD activated. Pose file: " TEST_HMD_POSE_PATH);
        RunFrame();
        return vr::VRInitError_None;
    }
    void Deactivate() override { index_=vr::k_unTrackedDeviceIndexInvalid; }
    void EnterStandby() override {}
    void* GetComponent(const char* name) override {
        return name && std::strcmp(name,vr::IVRDisplayComponent_Version)==0 ? static_cast<vr::IVRDisplayComponent*>(this):nullptr;
    }
    void DebugRequest(const char*,char* response,uint32_t size) override { if(response && size) response[0]=0; }
    vr::DriverPose_t GetPose() override { return pose_; }
    void RunFrame() {
        if(index_==vr::k_unTrackedDeviceIndexInvalid) return;
        std::ifstream file(TEST_HMD_POSE_PATH);
        std::string line;
        if(std::getline(file,line) && line!=last_line_) {
            std::istringstream stream(line);
            InputPose candidate;
            if(line.rfind("motion ",0)==0) { candidate.motion=true; stream.str(line.substr(7)); }
            if(stream>>candidate.x>>candidate.y>>candidate.z>>candidate.yaw>>candidate.pitch>>candidate.roll) {
                if(std::isfinite(candidate.x)&&std::isfinite(candidate.y)&&std::isfinite(candidate.z)&&
                   std::isfinite(candidate.yaw)&&std::isfinite(candidate.pitch)&&std::isfinite(candidate.roll)) {
                    input_=candidate; last_line_=line;
                    const std::string log="Simulated runtime pose input: "+line;
                    vr::VRDriverLog()->Log(log.c_str());
                }
            }
        }
        InputPose value=input_;
        if(value.motion) {
            const double t=(GetTickCount64()-start_)*0.001;
            value.x+=0.20*std::sin(t); value.y+=0.10*std::sin(t*0.7); value.z+=0.15*std::sin(t*0.5);
            value.yaw+=20*std::sin(t*0.6); value.pitch+=10*std::sin(t*0.4); value.roll+=5*std::sin(t*0.3);
        }
        pose_={};
        pose_.qWorldFromDriverRotation.w=1; pose_.qDriverFromHeadRotation.w=1;
        pose_.qRotation=Rotation(value.yaw,value.pitch,value.roll);
        pose_.vecPosition[0]=value.x; pose_.vecPosition[1]=value.y; pose_.vecPosition[2]=value.z;
        pose_.poseIsValid=true; pose_.deviceIsConnected=true;
        pose_.result=vr::TrackingResult_Running_OK;
        pose_.shouldApplyHeadModel=false;
        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(index_,pose_,sizeof(pose_));
    }
    void GetWindowBounds(int32_t* x,int32_t* y,uint32_t* w,uint32_t* h) override { *x=0; *y=0; *w=2048; *h=1024; }
    bool IsDisplayOnDesktop() override { return true; }
    bool IsDisplayRealDisplay() override { return false; }
    void GetRecommendedRenderTargetSize(uint32_t* w,uint32_t* h) override { *w=1024; *h=1024; }
    void GetEyeOutputViewport(vr::EVREye eye,uint32_t* x,uint32_t* y,uint32_t* w,uint32_t* h) override {
        *x=eye==vr::Eye_Left ? 0:1024; *y=0; *w=1024; *h=1024;
    }
    void GetProjectionRaw(vr::EVREye,float* l,float* r,float* t,float* b) override { *l=-1; *r=1; *t=-1; *b=1; }
    vr::DistortionCoordinates_t ComputeDistortion(vr::EVREye,float u,float v) override {
        vr::DistortionCoordinates_t d{};
        d.rfRed[0]=d.rfGreen[0]=d.rfBlue[0]=u; d.rfRed[1]=d.rfGreen[1]=d.rfBlue[1]=v;
        return d;
    }
    bool ComputeInverseDistortion(vr::HmdVector2_t* result,vr::EVREye,uint32_t,float u,float v) override {
        result->v[0]=u; result->v[1]=v; return true;
    }
};
class Provider final : public vr::IServerTrackedDeviceProvider {
    SimulatedHmd hmd_;
public:
    vr::EVRInitError Init(vr::IVRDriverContext* context) override {
        VR_INIT_SERVER_DRIVER_CONTEXT(context);
        vr::VRDriverLog()->Log("Loading diagnostic simulated HMD driver, no physical hardware.");
        return vr::VRServerDriverHost()->TrackedDeviceAdded(kSerial,vr::TrackedDeviceClass_HMD,&hmd_) ?
            vr::VRInitError_None:vr::VRInitError_Driver_Unknown;
    }
    void Cleanup() override { hmd_.Deactivate(); VR_CLEANUP_SERVER_DRIVER_CONTEXT(); }
    const char* const* GetInterfaceVersions() override { return vr::k_InterfaceVersions; }
    void RunFrame() override { hmd_.RunFrame(); }
    bool ShouldBlockStandbyMode() override { return false; }
    void EnterStandby() override {}
    void LeaveStandby() override {}
};
Provider provider;
}
extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* name,int* error) {
    if(name && std::strcmp(name,vr::IServerTrackedDeviceProvider_Version)==0) {
        if(error) *error=vr::VRInitError_None;
        return &provider;
    }
    if(error) *error=vr::VRInitError_Init_InterfaceNotFound;
    return nullptr;
}
