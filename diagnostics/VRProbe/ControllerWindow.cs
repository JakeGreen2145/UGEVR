using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal interface IWindowClock
{
    double ElapsedSeconds { get; }
    DateTime UtcNow { get; }
    void Sleep(double seconds);
}
internal sealed class WindowClock : IWindowClock
{
    readonly Stopwatch watch = Stopwatch.StartNew();
    public double ElapsedSeconds => watch.Elapsed.TotalSeconds;
    public DateTime UtcNow => DateTime.UtcNow;
    public void Sleep(double seconds) => Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, seconds)));
}
internal interface IProcessIdentity
{
    long? StartUtcTicks(uint pid);
}
internal sealed class ProcessIdentity : IProcessIdentity
{
    public long? StartUtcTicks(uint pid)
    {
        try
        {
            if (pid > int.MaxValue) return null;
            using var process = Process.GetProcessById((int)pid);
            if (process.HasExited) return null;
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is NotSupportedException)
        { return null; }
    }
}
internal sealed record WindowSample(string Type, int Sequence, DateTime Timestamp, double ElapsedSeconds,
    uint ExpectedScenePid, long ExpectedProcessStartUtcTicks, long? ProcessStartBefore, long? ProcessStartAfter,
    uint? FocusProcessBefore, uint? FocusProcessAfter, bool? InputAvailableBefore, bool? InputAvailableAfter, ControllerSnapshot Snapshot,
    string Status, string ProcessIdentity, string InputReceipt, bool GateEligible);

internal static class ControllerWindow
{
    public const double DurationSeconds = 15;
    public const int MaximumSamples = 300;
    public static WindowSample Capture(IControllerReadback source, IProcessIdentity identity, IWindowClock clock,
        uint pid, long start, int sequence)
    {
        long? before = identity.StartUtcTicks(pid);
        ControllerSnapshot snapshot = null;
        bool? inputBefore = null, inputAfter = null;
        uint? focusBefore = null, focusAfter = null;
        long? after = before;
        string status = "process_identity_mismatch";
        if (before == start)
        {
            focusBefore = source.SceneFocusProcess();
            if (focusBefore != pid)
            {
                after = identity.StartUtcTicks(pid);
                return new WindowSample("sample", sequence, clock.UtcNow, clock.ElapsedSeconds, pid, start, before, after,
                    focusBefore, focusBefore, null, null, null, "scene_focus_mismatch", "not_verified", "unknown", false);
            }
            inputBefore = source.IsInputAvailable();
            snapshot = ControllerProbe.Capture(source, pid);
            inputAfter = source.IsInputAvailable();
            focusAfter = source.SceneFocusProcess();
            after = identity.StartUtcTicks(pid);
            status = after != start ? "process_identity_changed" :
                snapshot.Status != "observed" ? snapshot.Status : focusAfter != pid ? "scene_focus_changed" : "observed";
            if (status != "observed") snapshot = snapshot with { Controllers = Array.Empty<ControllerSample>(), Status = status };
        }
        return new WindowSample("sample", sequence, clock.UtcNow, clock.ElapsedSeconds, pid, start, before, after,
            focusBefore, focusAfter, inputBefore, inputAfter, snapshot, status, status == "observed" ? "verified_pid_and_start_ticks" : "not_verified",
            "unknown", false);
    }
    public static int Run(IControllerReadback source, IProcessIdentity identity, IWindowClock clock,
        uint pid, long start, Action<WindowSample> emit)
    {
        double began = clock.ElapsedSeconds;
        int sequence = 0;
        while (sequence < MaximumSamples && clock.ElapsedSeconds - began < DurationSeconds)
        {
            var sample = Capture(source, identity, clock, pid, start, sequence);
            // An SDK call may cross the boundary. Its completed read is excluded rather than extending the window.
            if (clock.ElapsedSeconds - began >= DurationSeconds) break;
            emit(sample);
            sequence++;
            // Space reads by at least 50 ms. Slow SDK calls reduce sample count without catch-up bursts.
            double next = clock.ElapsedSeconds + 0.05;
            clock.Sleep(Math.Min(next - clock.ElapsedSeconds, Math.Max(0, DurationSeconds - (clock.ElapsedSeconds - began))));
        }
        return sequence;
    }
}

internal static class WindowSupervisor
{
    public const int WholeDeadlineSeconds = 20;
    public static ProcessStartInfo WorkerStart(string[] args)
    {
        string path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--controller-window-worker");
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task<int> Run(ProcessStartInfo info, TextWriter output, TextWriter errors, TimeSpan deadline)
    {
        output = TextWriter.Synchronized(output);
        errors = TextWriter.Synchronized(errors);
        var elapsed = Stopwatch.StartNew();
        using var child = new Process { StartInfo = info };
        child.Start();
        // Drain both streams continuously, including records emitted before a hung SDK shutdown.
        Task stdout = Pump(child.StandardOutput, output, true);
        Task stderr = Pump(child.StandardError, errors, false);
        Task finished = Task.WhenAll(child.WaitForExitAsync(), stdout, stderr);
        if (await Task.WhenAny(finished, Task.Delay(deadline - (elapsed.Elapsed < deadline ? elapsed.Elapsed : deadline))) != finished)
        {
            try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { }
            // Only this supervisor's direct child is terminated; no runtime or game process is selected.
            await output.WriteLineAsync(JsonSerializer.Serialize(new { type = "timeout", shutdownConfirmed = false, inputReceipt = "unknown", gateEligible = false }));
            await output.FlushAsync();
            return 124;
        }
        await finished;
        return child.ExitCode;
    }
    static async Task Pump(StreamReader reader, TextWriter writer, bool jsonLines)
    {
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            // A killed worker may leave an unfinished JSON record. Preserve completed records only.
            if (jsonLines)
            {
                try { using var document = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
            }
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }
}
