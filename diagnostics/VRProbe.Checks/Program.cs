using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Valve.VR;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
void Reject(string[] args) { bool rejected = false; try { ProbeOptions.Parse(args); } catch (ArgumentException) { rejected = true; } Check(rejected, string.Join(" ", args)); }
Check(!ProbeOptions.Parse(Array.Empty<string>()).Controllers, "default remains HMD mode");
Check(ProbeOptions.Parse(new[] { "--controllers", "--expected-scene-pid", "123" }).ExpectedScenePid == 123, "controller PID");
Check(ProbeOptions.Parse(new[] { "--expected-scene-pid", "4294967295", "--controllers" }).ExpectedScenePid == uint.MaxValue, "full uint PID");
foreach (var invalidArgs in new[] {
    new[] { "--controllers" }, new[] { "--expected-scene-pid", "1" },
    new[] { "--controllers", "--expected-scene-pid" },
    new[] { "--controllers", "--controllers", "--expected-scene-pid", "1" },
    new[] { "--controllers", "--expected-scene-pid", "1", "--expected-scene-pid", "2" },
    new[] { "--unknown" } }) Reject(invalidArgs);
foreach (var pid in new[] { "0", "-1", "+1", "1.0", "1e2", " 1", "1 ", "4294967296", "", "١" })
    Reject(new[] { "--controllers", "--expected-scene-pid", pid });

Check(ControllerProbe.StateSize == Marshal.SizeOf<VRControllerState_t>(), "native state Marshal.SizeOf");
if (OperatingSystem.IsWindows()) {
    Check(ControllerProbe.StateSize == 64, "Windows SDK struct size");
    Check(Marshal.OffsetOf<VRControllerState_t>(nameof(VRControllerState_t.ulButtonPressed)).ToInt32() == 8, "Windows mask alignment");
}
var mismatch = new FakeReadback(456, 456);
var rejectedSnapshot = ControllerProbe.Capture(mismatch, 123);
Check(rejectedSnapshot.Status == "scene_focus_mismatch" && mismatch.ReadCalls == 0, "focus guard before reads");
Check(rejectedSnapshot.InputAvailable == null && rejectedSnapshot.Controllers.Length == 0, "mismatch retains unknown");
var missing = new FakeReadback(null, null);
Check(ControllerProbe.Capture(missing, 123).Status == "scene_focus_mismatch" && missing.ReadCalls == 0, "missing compositor guard");

var source = new FakeReadback(123, 123);
var observed = ControllerProbe.Capture(source, 123);
Check(observed.Status == "observed" && observed.Controllers.Length == 2, "controller class filter");
Check(source.Indices.Count == ControllerProbe.DeviceLimit && source.Indices[^1] < 64, "bounded enumeration");
Check(source.Sizes.Count == 2 && source.Sizes.TrueForAll(size => size == ControllerProbe.StateSize), "SDK struct size forwarded");
var right = observed.Controllers[0];
Check(right.DeviceIndex == 2 && right.Role == "RightHand" && right.Packet == 99, "index role packet");
Check(right.Pressed == "18446744073709551615" && right.Touched == "9223372036854775808", "exact uint64 strings");
Check(right.Axes.Length == 5 && right.Axes[0][0] == 0.35f && right.Axes[4][1] == -0.25f, "all five axes");
Check(right.Connected && right.PoseValid && right.Transform.Length == 12 && right.Transform[3] == 0.13f, "pose and transform");
Check(right.Model.Value == "model" && right.Serial.Value == "serial", "device identity");
var unavailable = observed.Controllers[1];
Check(!unavailable.StateAvailable && unavailable.Packet == null && unavailable.Pressed == null && unavailable.Touched == null && unavailable.Axes == null, "failed state does not fabricate neutral");
Check(observed.ProcessIdentity == "not_verified_pid_only" && observed.InputReceipt == "unknown", "no game identity or input receipt claim");
string json = JsonSerializer.Serialize(observed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
Check(json.Contains("\"pressed\":\"18446744073709551615\""), "JSON mask remains string");
var changed = new FakeReadback(123, 456);
var changedSnapshot = ControllerProbe.Capture(changed, 123);
Check(changedSnapshot.Status == "scene_focus_changed" && changedSnapshot.Controllers.Length == 0 && changed.ReadCalls > 0, "changed focus discards samples");
var unavailableInput = new FakeReadback(123, 123) { Input = false, NonFinite = true };
var noInputSnapshot = ControllerProbe.Capture(unavailableInput, 123);
Check(noInputSnapshot.InputAvailable == false, "input availability false is observable");
Check(noInputSnapshot.Controllers[0].Axes[0][0] == null && JsonSerializer.Serialize(noInputSnapshot).Length > 0, "nonfinite axis remains unknown serializable");
var missingIdentity = ControllerProbe.Capture(new FakeReadback(123, 123) { IdentityError = true }, 123);
Check(missingIdentity.Controllers[0].Serial.Value == null && missingIdentity.Controllers[0].Serial.Error == "TrackedProp_UnknownProperty", "identity failure remains explicit");
Console.WriteLine($"{count} source-only controller probe checks passed; OpenVR was not initialized.");

sealed class FakeReadback(uint? before, uint? after) : IControllerReadback
{
    int focusReads;
    public int ReadCalls;
    public bool Input = true;
    public bool NonFinite;
    public bool IdentityError;
    public List<uint> Indices = new();
    public List<uint> Sizes = new();
    public uint? SceneFocusProcess() => focusReads++ == 0 ? before : after;
    public bool IsInputAvailable() { ReadCalls++; return Input; }
    public TrackedDevicePose_t[] Poses() {
        ReadCalls++;
        var poses = new TrackedDevicePose_t[ControllerProbe.DeviceLimit];
        poses[2] = new TrackedDevicePose_t { bPoseIsValid = true, bDeviceIsConnected = true,
            eTrackingResult = ETrackingResult.Running_OK, mDeviceToAbsoluteTracking = new HmdMatrix34_t { m3 = 0.13f } };
        return poses;
    }
    public ETrackedDeviceClass DeviceClass(uint index) { ReadCalls++; Indices.Add(index); return index == 2 || index == 63 ? ETrackedDeviceClass.Controller : ETrackedDeviceClass.GenericTracker; }
    public ETrackedControllerRole Role(uint index) => index == 2 ? ETrackedControllerRole.RightHand : ETrackedControllerRole.LeftHand;
    public bool ControllerState(uint index, ref VRControllerState_t state, uint size) {
        ReadCalls++; Sizes.Add(size);
        state = new VRControllerState_t { unPacketNum = 99, ulButtonPressed = ulong.MaxValue, ulButtonTouched = 1UL << 63,
            rAxis0 = new VRControllerAxis_t { x = NonFinite ? float.NaN : 0.35f, y = -0.65f }, rAxis4 = new VRControllerAxis_t { y = -0.25f } };
        return index == 2;
    }
    public StringProperty Property(uint index, ETrackedDeviceProperty property) => IdentityError
        ? new(null, "TrackedProp_UnknownProperty")
        : new(property == ETrackedDeviceProperty.Prop_ModelNumber_String ? "model" : "serial", "TrackedProp_Success");
}
