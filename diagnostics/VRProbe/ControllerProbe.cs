using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

internal sealed record ProbeOptions(bool Controllers, uint? ExpectedScenePid)
{
    public static ProbeOptions Parse(string[] args)
    {
        bool controllers = false;
        uint? expectedPid = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--controllers" && !controllers) controllers = true;
            else if (args[i] == "--expected-scene-pid" && expectedPid == null && ++i < args.Length)
            {
                string value = args[i];
                foreach (char c in value)
                    if (c < '0' || c > '9') throw new ArgumentException("Expected scene PID must be positive uint decimal.");
                if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint pid) || pid == 0)
                    throw new ArgumentException("Expected scene PID must be positive uint decimal.");
                expectedPid = pid;
            }
            else throw new ArgumentException("Unknown, duplicate or incomplete probe option.");
        }
        if (controllers != expectedPid.HasValue)
            throw new ArgumentException("Controller mode requires both --controllers and --expected-scene-pid N.");
        return new ProbeOptions(controllers, expectedPid);
    }
}

internal sealed record StringProperty(string Value, string Error);
internal interface IControllerReadback
{
    uint? SceneFocusProcess();
    bool IsInputAvailable();
    TrackedDevicePose_t[] Poses();
    ETrackedDeviceClass DeviceClass(uint index);
    ETrackedControllerRole Role(uint index);
    bool ControllerState(uint index, ref VRControllerState_t state, uint size);
    StringProperty Property(uint index, ETrackedDeviceProperty property);
}

internal sealed class OpenVrControllerReadback(CVRSystem system, CVRCompositor compositor) : IControllerReadback
{
    public uint? SceneFocusProcess() => compositor?.GetCurrentSceneFocusProcess();
    public bool IsInputAvailable() => system.IsInputAvailable();
    public TrackedDevicePose_t[] Poses()
    {
        var poses = new TrackedDevicePose_t[ControllerProbe.DeviceLimit];
        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
        return poses;
    }
    public ETrackedDeviceClass DeviceClass(uint index) => system.GetTrackedDeviceClass(index);
    public ETrackedControllerRole Role(uint index) => system.GetControllerRoleForTrackedDeviceIndex(index);
    public bool ControllerState(uint index, ref VRControllerState_t state, uint size) => system.GetControllerState(index, ref state, size);
    public StringProperty Property(uint index, ETrackedDeviceProperty property)
    {
        const uint capacity = 256;
        var text = new StringBuilder((int)capacity);
        var error = ETrackedPropertyError.TrackedProp_Success;
        uint required = system.GetStringTrackedDeviceProperty(index, property, text, capacity, ref error);
        // Do not retry an oversized identity with an unbounded allocation.
        if (error != ETrackedPropertyError.TrackedProp_Success || required > capacity)
            return new StringProperty(null, error == ETrackedPropertyError.TrackedProp_Success ? "buffer_limit" : error.ToString());
        return new StringProperty(text.ToString(), error.ToString());
    }
}

internal sealed record ControllerSample(uint DeviceIndex, string DeviceClass, string Role,
    StringProperty Model, StringProperty Serial, bool StateAvailable, uint? Packet,
    string Pressed, string Touched, float?[][] Axes, bool Connected, bool PoseValid,
    string TrackingResult, float?[] Transform);
internal sealed record ControllerSnapshot(DateTime Timestamp, string Status, uint ExpectedScenePid,
    uint? FocusProcessBefore, uint? FocusProcessAfter, bool? InputAvailable, uint ControllerStateSize,
    int DeviceLimit, string ProcessIdentity, string InputReceipt, ControllerSample[] Controllers);

internal static class ControllerProbe
{
    public static readonly int DeviceLimit = Math.Min(64, (int)OpenVR.k_unMaxTrackedDeviceCount);
    public static readonly uint StateSize = checked((uint)Marshal.SizeOf<VRControllerState_t>());

    public static ControllerSnapshot Capture(IControllerReadback source, uint expectedPid)
    {
        if (expectedPid == 0) throw new ArgumentOutOfRangeException(nameof(expectedPid));
        uint? before = source.SceneFocusProcess();
        ControllerSnapshot Result(string status, uint? after, bool? input, ControllerSample[] samples) =>
            new(DateTime.UtcNow, status, expectedPid, before, after, input, StateSize, DeviceLimit,
                "not_verified_pid_only", "unknown", samples);
        if (before != expectedPid) return Result("scene_focus_mismatch", before, null, Array.Empty<ControllerSample>());

        bool inputAvailable = source.IsInputAvailable();
        var poses = source.Poses();
        var controllers = new List<ControllerSample>();
        for (uint index = 0; index < DeviceLimit; index++)
        {
            if (source.DeviceClass(index) != ETrackedDeviceClass.Controller) continue;
            var state = new VRControllerState_t();
            bool available = source.ControllerState(index, ref state, StateSize);
            var pose = poses[index];
            controllers.Add(new ControllerSample(index, ETrackedDeviceClass.Controller.ToString(), source.Role(index).ToString(),
                source.Property(index, ETrackedDeviceProperty.Prop_ModelNumber_String),
                source.Property(index, ETrackedDeviceProperty.Prop_SerialNumber_String), available,
                available ? state.unPacketNum : null,
                available ? state.ulButtonPressed.ToString(CultureInfo.InvariantCulture) : null,
                available ? state.ulButtonTouched.ToString(CultureInfo.InvariantCulture) : null,
                available ? new[] { Axis(state.rAxis0), Axis(state.rAxis1), Axis(state.rAxis2), Axis(state.rAxis3), Axis(state.rAxis4) } : null,
                pose.bDeviceIsConnected, pose.bPoseIsValid, pose.eTrackingResult.ToString(), Matrix(pose.mDeviceToAbsoluteTracking)));
        }
        uint? after = source.SceneFocusProcess();
        // Discard potentially mixed-consumer samples if scene focus changed during the read.
        if (after != expectedPid) return Result("scene_focus_changed", after, inputAvailable, Array.Empty<ControllerSample>());
        return Result("observed", after, inputAvailable, controllers.ToArray());
    }
    static float? Finite(float value) => float.IsFinite(value) ? value : null;
    static float?[] Axis(VRControllerAxis_t value) => new[] { Finite(value.x), Finite(value.y) };
    static float?[] Matrix(HmdMatrix34_t value) => new[] { Finite(value.m0), Finite(value.m1), Finite(value.m2), Finite(value.m3), Finite(value.m4), Finite(value.m5), Finite(value.m6), Finite(value.m7), Finite(value.m8), Finite(value.m9), Finite(value.m10), Finite(value.m11) };
}
