using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Valve.VR;

if (args.Length == 1 && args[0] == "--fake-hung-sdk") {
    Console.WriteLine("{\"type\":\"sample\",\"fake\":true}"); Console.Out.Flush();
    Console.Error.WriteLine("fake SDK shutdown entered"); Console.Error.Flush();
    Console.Write("{\"unfinishedRecord\":"); Console.Out.Flush();
    System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
    return;
}
if (args.Length == 1 && args[0] == "--fake-hung-startup") {
    System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
    return;
}
if (args.Length == 1 && args[0] == "--fake-drained-sdk") {
    for (int i = 0; i < 2000; i++) { Console.WriteLine(JsonSerializer.Serialize(new { type = "sample", padding = new string('x', 128) })); Console.Error.WriteLine(new string('y', 128)); }
    Console.WriteLine("{\"type\":\"end\",\"shutdownConfirmed\":true}");
    return;
}
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
Check(right.Axis0Type.Value == (int)EVRControllerAxisType.k_eControllerAxis_Joystick && right.Axis0Type.Error == "TrackedProp_Success", "raw axis zero joystick property");
Check(right.Axis1Type.Value == 3 && right.Axis2Type.Value == 0 && right.Axis3Type.Value == 1 && right.Axis4Type.Value == 0, "all five declared axis type properties");
Check(source.IntProperties.Count == 10 && source.IntProperties.GetRange(0, 5).ToArray().AsSpan().SequenceEqual(new[] {
    ETrackedDeviceProperty.Prop_Axis0Type_Int32, ETrackedDeviceProperty.Prop_Axis1Type_Int32,
    ETrackedDeviceProperty.Prop_Axis2Type_Int32, ETrackedDeviceProperty.Prop_Axis3Type_Int32,
    ETrackedDeviceProperty.Prop_Axis4Type_Int32 }), "one bounded property read per axis per controller");
var unavailable = observed.Controllers[1];
Check(!unavailable.StateAvailable && unavailable.Packet == null && unavailable.Pressed == null && unavailable.Touched == null && unavailable.Axes == null, "failed state does not fabricate neutral");
Check(unavailable.Axis0Type.Value == 2, "property type does not fabricate unavailable axis values");
Check(observed.ProcessIdentity == "not_verified_pid_only" && observed.InputReceipt == "unknown", "no game identity or input receipt claim");
string json = JsonSerializer.Serialize(observed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
Check(json.Contains("\"pressed\":\"18446744073709551615\""), "JSON mask remains string");
Check(json.Contains("\"axis0Type\":{\"value\":2,\"error\":\"TrackedProp_Success\"}"), "JSON raw axis type and property error");
var changed = new FakeReadback(123, 456);
var changedSnapshot = ControllerProbe.Capture(changed, 123);
Check(changedSnapshot.Status == "scene_focus_changed" && changedSnapshot.Controllers.Length == 0 && changed.ReadCalls > 0, "changed focus discards samples");
var unavailableInput = new FakeReadback(123, 123) { Input = false, NonFinite = true };
var noInputSnapshot = ControllerProbe.Capture(unavailableInput, 123);
Check(noInputSnapshot.InputAvailable == false, "input availability false is observable");
Check(noInputSnapshot.Controllers[0].Axes[0][0] == null && JsonSerializer.Serialize(noInputSnapshot).Length > 0, "nonfinite axis remains unknown serializable");
var missingIdentity = ControllerProbe.Capture(new FakeReadback(123, 123) { IdentityError = true }, 123);
Check(missingIdentity.Controllers[0].Serial.Value == null && missingIdentity.Controllers[0].Serial.Error == "TrackedProp_UnknownProperty", "identity failure remains explicit");
var unavailableType = ControllerProbe.Capture(new FakeReadback(123, 123) { AxisTypeError = true }, 123).Controllers[0];
Check(unavailableType.Axis0Type.Value == null && unavailableType.Axis0Type.Error == "TrackedProp_UnknownProperty" && unavailableType.Axes[0][0] == .35f, "property failure does not guess joystick from nonzero axis state");
Check(unavailableType.Axis4Type.Value == null && unavailableType.Axis4Type.Error == "TrackedProp_UnknownProperty", "property failure preserved for all axes");
var futureType = ControllerProbe.Capture(new FakeReadback(123, 123) { UnknownAxisType = true }, 123).Controllers[0];
Check(futureType.Axis0Type.Value == 987654321 && futureType.Axis0Type.Error == "TrackedProp_Success", "unknown future raw axis type preserved");
Check(futureType.Axis4Type.Value == -17, "signed raw enum value preserved without coercion");
Check(mismatch.IntProperties.Count == 0 && missing.IntProperties.Count == 0, "focus rejection performs zero property reads");

var windowOptions = ProbeOptions.Parse(new[] { "--controller-window", "--expected-scene-pid", "123", "--expected-process-start-utc-ticks", "639275456966499101" });
Check(windowOptions.ControllerWindow && !windowOptions.Controllers && windowOptions.ExpectedProcessStartUtcTicks == 639275456966499101L, "window exact process ticks");
foreach (var invalid in new[] {
    new[] { "--controller-window", "--expected-scene-pid", "123" },
    new[] { "--expected-process-start-utc-ticks", "1" },
    new[] { "--controllers", "--controller-window", "--expected-scene-pid", "1", "--expected-process-start-utc-ticks", "1" },
    new[] { "--controllers", "--expected-scene-pid", "1", "--expected-process-start-utc-ticks", "1" },
    new[] { "--controller-window", "--controller-window", "--expected-scene-pid", "1", "--expected-process-start-utc-ticks", "1" } }) Reject(invalid);
foreach (string badTicks in new[] { "0", "-1", "+1", " 1", "1e2", "3155378976000000000", "9223372036854775808", "١", "" })
    Reject(new[] { "--controller-window", "--expected-scene-pid", "1", "--expected-process-start-utc-ticks", badTicks });
var wrongProcessSource = new FakeReadback(123, 123);
var wrongProcess = ControllerWindow.Capture(wrongProcessSource, new FakeIdentity(8, 8), new FakeClock(), 123, 7, 0);
Check(wrongProcess.Status == "process_identity_mismatch" && wrongProcessSource.ReadCalls == 0 && wrongProcess.Snapshot == null, "PID reuse rejected before SDK reads");
var recycled = ControllerWindow.Capture(new FakeReadback(123, 123), new FakeIdentity(7, 8), new FakeClock(), 123, 7, 1);
Check(recycled.Status == "process_identity_changed" && recycled.Snapshot.Controllers.Length == 0, "PID reuse during capture discards controllers");
var exited = ControllerWindow.Capture(new FakeReadback(123, 123), new FakeIdentity(7, null), new FakeClock(), 123, 7, 1);
Check(exited.Status == "process_identity_changed" && exited.ProcessStartAfter == null, "process exit after read remains unknown");
var windowSample = ControllerWindow.Capture(new FakeReadback(123, 123), new FakeIdentity(7, 7), new FakeClock(), 123, 7, 3);
Check(windowSample.Status == "observed" && windowSample.Sequence == 3 && windowSample.ProcessIdentity == "verified_pid_and_start_ticks", "bounded identity and focus accepted");
Check(windowSample.InputAvailableBefore == true && windowSample.InputAvailableAfter == true && windowSample.InputReceipt == "unknown" && !windowSample.GateEligible, "both availability boundaries no gameplay claim");
Check(windowSample.Snapshot.Controllers[0].Pressed == "18446744073709551615", "window preserves exact masks and identity");
var contextChanged = ControllerWindow.Capture(new FakeReadback(123, 456) { FocusChangeAfterReads = 2 }, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 4);
Check(contextChanged.Status == "scene_focus_changed" && contextChanged.Snapshot.Controllers.Length == 0, "window changed scene excluded");
var finalContextChanged = ControllerWindow.Capture(new FakeReadback(123, 456) { FocusChangeAfterReads = 3 }, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 4);
Check(finalContextChanged.FocusProcessBefore == 123 && finalContextChanged.FocusProcessAfter == 456 && finalContextChanged.Snapshot.Controllers.Length == 0, "last focus boundary after availability is preserved");
var initiallyUnfocused = new FakeReadback(456, 456);
var unfocusedWindow = ControllerWindow.Capture(initiallyUnfocused, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0);
Check(unfocusedWindow.Status == "scene_focus_mismatch" && initiallyUnfocused.ReadCalls == 0 && unfocusedWindow.InputAvailableBefore == null, "fresh initial focus rejects before reads");
var falseAvailable = ControllerWindow.Capture(new FakeReadback(123, 123) { Input = false }, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0);
Check(falseAvailable.InputAvailableBefore == false && falseAvailable.InputAvailableAfter == false && falseAvailable.Status == "observed", "availability does not fabricate delivery");
var clock = new FakeClock(); var samples = new List<WindowSample>();
int samplesCount = ControllerWindow.Run(new FakeReadback(123, 123), new FakeIdentity(7, 7), clock, 123, 7, samples.Add);
Check(samplesCount == 300 && samples.Count == 300 && clock.ElapsedSeconds >= 15, "fixed 15 seconds maximum 300 samples");
Check(samples[^1].Sequence == 299 && samples[^1].ElapsedSeconds < 15 && clock.MinSleep >= .049999, "20Hz no catchup burst");
Check(!windowOptions.HeadPose && !json.Contains("headPose"), "head opt-in absent by default including JSON");
var headArgs = new[] { "--controller-window", "--expected-scene-pid", "123", "--expected-process-start-utc-ticks", "7", "--head-pose" };
Check(ProbeOptions.Parse(headArgs).HeadPose, "head option accepted with bounded controller window");
Reject(new[] { "--head-pose" });
Reject(new[] { "--controllers", "--expected-scene-pid", "123", "--head-pose" });
Reject(new[] { "--controller-window", "--expected-scene-pid", "123", "--expected-process-start-utc-ticks", "7", "--head-pose", "--head-pose" });
var headSource = new FakeReadback(123, 123);
var headSample = ControllerWindow.Capture(headSource, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0, true);
Check(headSample.Snapshot.HeadPose is { DeviceIndex: 0, DeviceClass: "HMD", Connected: true, PoseValid: true, TrackingResult: "Running_OK", Status: "observed" }, "same-client raw HMD identity and validity");
Check(headSample.Snapshot.HeadPose.Transform.Length == 12 && headSample.Snapshot.HeadPose.Transform[7] == 1.6f && headSource.PoseCalls == 1, "head uses one existing Standing pose query");
Check(headSource.Sizes.Count == 2 && headSource.IntProperties.Count == 10, "head adds no controller state or property queries");
Check(JsonSerializer.Serialize(headSample, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).Contains("\"headPose\":{\"deviceIndex\":0"), "optional head schema emitted");
var noHead = ControllerProbe.Capture(new FakeReadback(123, 123) { HeadClass = ETrackedDeviceClass.GenericTracker }, 123, true).HeadPose;
Check(noHead.Status == "device_class_mismatch" && noHead.Transform == null && noHead.PoseValid == null, "wrong head class is explicit unknown");
var invalidHead = ControllerProbe.Capture(new FakeReadback(123, 123) { HeadValid = false }, 123, true).HeadPose;
Check(invalidHead.Status == "pose_unavailable" && invalidHead.PoseValid == false, "invalid raw head does not claim receipt");
var disconnectedHead = ControllerProbe.Capture(new FakeReadback(123, 123) { HeadConnected = false }, 123, true).HeadPose;
Check(disconnectedHead.Status == "pose_unavailable" && disconnectedHead.Connected == false, "disconnected raw head remains explicit");
var nonfiniteHead = ControllerProbe.Capture(new FakeReadback(123, 123) { HeadNonFinite = true }, 123, true).HeadPose;
Check(nonfiniteHead.Status == "nonfinite_pose" && nonfiniteHead.Transform[7] == null && JsonSerializer.Serialize(nonfiniteHead).Length > 0, "nonfinite raw head serializes null without fabricated pose");
var missingHeadPose = ControllerProbe.Capture(new FakeReadback(123, 123) { MissingPoses = true, NoControllers = true }, 123, true).HeadPose;
Check(missingHeadPose.Status == "pose_unavailable" && missingHeadPose.Transform == null, "missing raw pose array is explicit unknown");
var headMismatchSource = new FakeReadback(456, 456);
Check(ControllerWindow.Capture(headMismatchSource, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0, true).Snapshot == null && headMismatchSource.PoseCalls == 0, "initial focus rejects head before pose query");
Check(ControllerWindow.Capture(new FakeReadback(123, 456), new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0, true).Snapshot.HeadPose == null, "inner focus change discards head");
Check(ControllerWindow.Capture(new FakeReadback(123, 456) { FocusChangeAfterReads = 3 }, new FakeIdentity(7, 7), new FakeClock(), 123, 7, 0, true).Snapshot.HeadPose == null, "final focus change discards head");
Check(ControllerWindow.Capture(new FakeReadback(123, 123), new FakeIdentity(7, 8), new FakeClock(), 123, 7, 0, true).Snapshot.HeadPose == null, "process reuse discards head");
var headClock = new FakeClock(); var headSamples = new List<WindowSample>();
Check(ControllerWindow.Run(new FakeReadback(123, 123), new FakeIdentity(7, 7), headClock, 123, 7, headSamples.Add, true) == 300 && headSamples.TrueForAll(s => s.Snapshot.HeadPose != null), "head option preserves bounded window limit");
var slowClock = new FakeClock(); int slowCount = ControllerWindow.Run(new FakeReadback(123, 123), new FakeIdentity(7, 7), slowClock, 123, 7, s => slowClock.Advance(.4));
Check(slowCount < 40 && slowClock.MinSleep >= .049999, "slow reads reduce count without bursts");
var crossedClock = new FakeClock(); var crossedSamples = new List<WindowSample>();
int crossedCount = ControllerWindow.Run(new FakeReadback(123, 123) { OnPoses = () => crossedClock.Advance(16) }, new FakeIdentity(7, 7), crossedClock, 123, 7, crossedSamples.Add);
Check(crossedCount == 0 && crossedSamples.Count == 0, "SDK read crossing fixed window is excluded");
using (var process = System.Diagnostics.Process.GetCurrentProcess())
    Check(new ProcessIdentity().StartUtcTicks((uint)process.Id) == process.StartTime.ToUniversalTime().Ticks, "real process start identity read without OpenVR");
Check(new ProcessIdentity().StartUtcTicks(uint.MaxValue) == null, "unrepresentable process unknown");
System.Diagnostics.ProcessStartInfo Child(string mode) {
    var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    if (System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    info.ArgumentList.Add(mode); return info;
}
var output = new System.IO.StringWriter(); var errors = new System.IO.StringWriter();
int drainedExit = await WindowSupervisor.Run(Child("--fake-drained-sdk"), output, errors, TimeSpan.FromSeconds(10));
Check(drainedExit == 0 && output.ToString().Contains("\"shutdownConfirmed\":true") && output.ToString().Length > 256000 && errors.ToString().Length > 256000, "large stdout stderr drained through SDK completion");
output = new System.IO.StringWriter(); errors = new System.IO.StringWriter(); var deadlineClock = System.Diagnostics.Stopwatch.StartNew();
int hungExit = await WindowSupervisor.Run(Child("--fake-hung-sdk"), output, errors, TimeSpan.FromMilliseconds(500));
Check(hungExit == 124 && deadlineClock.Elapsed.TotalSeconds < 3, "whole child deadline kills hung SDK");
Check(output.ToString().Contains("\"fake\":true") && output.ToString().Contains("\"shutdownConfirmed\":false") && !output.ToString().Contains("\"shutdownConfirmed\":true"), "prior samples retained but hung shutdown never confirmed");
Check(!output.ToString().Contains("unfinishedRecord"), "partial JSON record from killed child excluded");
Check(errors.ToString().Contains("fake SDK shutdown entered") && WindowSupervisor.WholeDeadlineSeconds == 20, "shutdown and production whole deadline");
output = new System.IO.StringWriter(); errors = new System.IO.StringWriter();
int startupExit = await WindowSupervisor.Run(Child("--fake-hung-startup"), output, errors, TimeSpan.FromMilliseconds(500));
Check(startupExit == 124 && !output.ToString().Contains("\"type\":\"sample\"") && output.ToString().Contains("\"shutdownConfirmed\":false"), "hung initialization bounded without synthetic samples");
Console.WriteLine($"{count} source-only controller probe checks passed; OpenVR was not initialized.");

sealed class FakeIdentity(long? before, long? after) : IProcessIdentity {
    int calls;
    public long? StartUtcTicks(uint pid) => calls++ == 0 ? before : after;
}
sealed class FakeClock : IWindowClock {
    public double ElapsedSeconds { get; private set; }
    public DateTime UtcNow => new DateTime(638000000000000000, DateTimeKind.Utc).AddSeconds(ElapsedSeconds);
    public double MinSleep = double.MaxValue;
    public void Sleep(double seconds) { if (seconds > 0) MinSleep = Math.Min(MinSleep, seconds); Advance(seconds); }
    public void Advance(double seconds) { ElapsedSeconds += seconds; }
}
sealed class FakeReadback(uint? before, uint? after) : IControllerReadback
{
    int focusReads;
    public int FocusChangeAfterReads = 1;
    public Action OnPoses;
    public int ReadCalls;
    public int PoseCalls;
    public ETrackedDeviceClass HeadClass = ETrackedDeviceClass.HMD;
    public bool HeadValid = true, HeadConnected = true, HeadNonFinite, MissingPoses, NoControllers;
    public bool Input = true;
    public bool NonFinite;
    public bool IdentityError;
    public bool AxisTypeError;
    public bool UnknownAxisType;
    public List<uint> Indices = new();
    public List<uint> Sizes = new();
    public List<ETrackedDeviceProperty> IntProperties = new();
    public uint? SceneFocusProcess() => focusReads++ < FocusChangeAfterReads ? before : after;
    public bool IsInputAvailable() { ReadCalls++; return Input; }
    public TrackedDevicePose_t[] Poses() {
        OnPoses?.Invoke();
        ReadCalls++;
        PoseCalls++;
        if (MissingPoses) return null;
        var poses = new TrackedDevicePose_t[ControllerProbe.DeviceLimit];
        poses[0] = new TrackedDevicePose_t { bPoseIsValid = HeadValid, bDeviceIsConnected = HeadConnected,
            eTrackingResult = ETrackingResult.Running_OK, mDeviceToAbsoluteTracking = new HmdMatrix34_t { m0 = 1, m5 = 1, m10 = 1, m7 = HeadNonFinite ? float.PositiveInfinity : 1.6f } };
        poses[2] = new TrackedDevicePose_t { bPoseIsValid = true, bDeviceIsConnected = true,
            eTrackingResult = ETrackingResult.Running_OK, mDeviceToAbsoluteTracking = new HmdMatrix34_t { m3 = 0.13f } };
        return poses;
    }
    public ETrackedDeviceClass DeviceClass(uint index) { ReadCalls++; Indices.Add(index); return index == 0 ? HeadClass : !NoControllers && (index == 2 || index == 63) ? ETrackedDeviceClass.Controller : ETrackedDeviceClass.GenericTracker; }
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
    public Int32Property IntProperty(uint index, ETrackedDeviceProperty property) {
        IntProperties.Add(property);
        if (AxisTypeError) return new(null, "TrackedProp_UnknownProperty");
        int value = property switch {
            ETrackedDeviceProperty.Prop_Axis0Type_Int32 => UnknownAxisType ? 987654321 : 2,
            ETrackedDeviceProperty.Prop_Axis1Type_Int32 => 3,
            ETrackedDeviceProperty.Prop_Axis2Type_Int32 => 0,
            ETrackedDeviceProperty.Prop_Axis3Type_Int32 => 1,
            ETrackedDeviceProperty.Prop_Axis4Type_Int32 => UnknownAxisType ? -17 : 0,
            _ => throw new Exception("Unexpected property")
        };
        return new(value, "TrackedProp_Success");
    }
}
