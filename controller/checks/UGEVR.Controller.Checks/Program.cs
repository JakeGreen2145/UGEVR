using System.Diagnostics;
using System.Text.Json;

if (args.FirstOrDefault() == "child")
{
    var directory = Environment.GetEnvironmentVariable("UGEVR_SESSION_DIRECTORY")!;
    var run = Environment.GetEnvironmentVariable("UGEVR_SESSION_ID")!;
    var mode = args.ElementAtOrDefault(1) ?? "good";
    File.WriteAllText(Path.Combine(directory, "child-environment.txt"), (Environment.GetEnvironmentVariable("SteamAppId") ?? "") + "\n" + (Environment.GetEnvironmentVariable("SteamGameId") ?? ""));
    var limit = DateTime.UtcNow.AddSeconds(mode == "natural" ? 3 : 90);
    var producer = new Ugevr.InjectorSession(mode == "wrong" ? Guid.Empty.ToString() : run, directory, mode == "wrongpid" ? Environment.ProcessId + 1 : Environment.ProcessId);
    bool enabled = true;
    while (DateTime.UtcNow < limit)
    {
        var command = producer.Poll(DateTime.UtcNow);
        if (command is not null && mode != "noack")
        {
            enabled = command.Enabled;
            producer.Acknowledge(command);
        }
        if (mode != "missing")
        {
            producer.Publish(DateTime.UtcNow, "controlled-child", true, true, enabled, 8, true);
            if (mode == "stale") File.SetLastWriteTimeUtc(Path.Combine(directory, "injector-status.yaml"), DateTime.UtcNow.AddSeconds(-20));
        }
        await Task.Delay(50);
    }
    return 0;
}

var controller = Path.GetFullPath(args.FirstOrDefault() ?? throw new ArgumentException("Pass controller DLL path."));
var root = Path.Combine(Path.GetTempPath(), "UGEVR-controller-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var owned = new List<string>(); int checks = 0;
async Task<(int Code, string Output)> Cli(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(controller); foreach (var arg in arguments) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch (OperationCanceledException) { process.Kill(); throw new Exception("CLI exceeded deadline: " + string.Join(' ', arguments)); }
    if ((await error).Length != 0) throw new Exception("Unexpected CLI stderr.");
    return (process.ExitCode, await output);
}
void Require(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
bool Field(string output, string key, string value) => output.Split('\n').Contains(key + ": " + JsonSerializer.Serialize(value));
async Task<(string Directory, int Code, string Output)> Launch(string mode, params string[] extra)
{
    var directory = Path.Combine(root, mode + "-" + Guid.NewGuid().ToString("N")); owned.Add(directory);
    var list = new List<string> { "launch", "--exe", Environment.ProcessPath!, "--session-dir", directory, "--prepared", "true", "--game-arg", "child", "--game-arg", mode, "--timeout-seconds", "2" };
    list.AddRange(extra); var result = await Cli(list.ToArray()); return (directory, result.Code, result.Output);
}
try
{
    var good = await Launch("good", "--steam-app-id", "753640");
    Require(good.Code == 0 && Field(good.Output, "engine_ready", "true") && Field(good.Output, "runtime_ready", "true"), "launch requires correct live handshake");
    Require(Field(good.Output, "stream_ready", "false"), "runtime readiness does not imply stream readiness");
    Require(File.ReadAllText(Path.Combine(good.Directory, "child-environment.txt")) == "753640\n753640", "Steam application identity is scoped to the child environment");
    var borrowed = Path.Combine(root, "borrowed"); Directory.CreateDirectory(borrowed);
    File.WriteAllText(Path.Combine(borrowed, "session.yaml"), File.ReadAllText(Path.Combine(good.Directory, "session.yaml")).Replace("owned_process: \"true\"", "owned_process: \"false\""));
    var rejectedStop = await Cli("stop", "--session-dir", borrowed, "--force", "true");
    Require(rejectedStop.Code == 2 && Field((await Cli("status", "--session-dir", good.Directory)).Output, "process_alive", "true"), "stop refuses a borrowed process without touching its owning session");
    var refused = await Cli("launch", "--exe", Environment.ProcessPath!, "--session-dir", good.Directory, "--prepared", "true");
    Require(refused.Code == 2, "existing sessions cannot be overwritten");
    var simultaneous = await Launch("good");
    Require(simultaneous.Code == 3 && simultaneous.Output.Contains("Another UGEVR session owns"), "second session cannot share a leased game directory");
    var configure = await Cli("configure", "--session-dir", good.Directory, "--enabled", "false", "--timeout-seconds", "2");
    Require(configure.Code == 0 && Field(configure.Output, "operation_result", "acknowledged") && Field(configure.Output, "rendering_active", "false"), "configuration waits for producer acknowledgment");
    var enable = await Cli("configure", "--session-dir", good.Directory, "--enabled", "true", "--recenter", "true", "--timeout-seconds", "2");
    Require(enable.Code == 0 && Field(enable.Output, "acknowledged_revision", "2"), "commands advance revisions and preserve recenter request");
    var graceful = await Cli("stop", "--session-dir", good.Directory, "--timeout-seconds", "1");
    Require(graceful.Code == 4 && Field(graceful.Output, "process_alive", "true"), "graceful stop never silently kills a console child");
    var force = await Cli("stop", "--session-dir", good.Directory, "--force", "true", "--timeout-seconds", "3");
    Require(force.Code == 0 && Field(force.Output, "process_alive", "false"), "explicit force terminates the owned child");
    var afterExit = await Cli("status", "--session-dir", good.Directory);
    Require(Field(afterExit.Output, "injector_status", "stale") && Field(afterExit.Output, "runtime_ready", "false"), "exited process invalidates producer readiness");
    var brokenMonitor = await Launch("good");
    File.WriteAllText(Path.Combine(brokenMonitor.Directory, "stop-request.yaml"), "invalid-stop-request\n");
    var monitorDeadline = DateTime.UtcNow.AddSeconds(3);
    (int Code, string Output) monitor = default;
    do { monitor = await Cli("status", "--session-dir", brokenMonitor.Directory); if (Field(monitor.Output, "state", "failed")) break; await Task.Delay(50); } while (DateTime.UtcNow < monitorDeadline);
    Require(Field(monitor.Output, "state", "failed") && Field(monitor.Output, "process_alive", "true"), "monitor failure is visible while the child stays alive");
    var recoverStop = await Cli("stop", "--session-dir", brokenMonitor.Directory, "--force", "true", "--timeout-seconds", "3");
    Require(recoverStop.Code == 3 && Field(recoverStop.Output, "process_alive", "false") && Field(recoverStop.Output, "cleanup_complete", "true"), "explicit owned stop remains available after monitor failure");
    foreach (var mode in new[] { "wrong", "wrongpid", "missing", "stale" })
    {
        var failure = await Launch(mode);
        var expected = mode is "wrong" or "wrongpid" ? "identity_mismatch" : mode == "missing" ? "missing" : "stale";
        Require(failure.Code == 4 && Field(failure.Output, "injector_status", expected) && Field(failure.Output, "runtime_ready", "false"), mode + " handshake cannot pass launch gate");
        await Cli("stop", "--session-dir", failure.Directory, "--force", "true", "--timeout-seconds", "3");
    }
    var noack = await Launch("noack"); Require(noack.Code == 0, "initial readiness is independent of command acknowledgment");
    var pending = await Cli("disable", "--session-dir", noack.Directory, "--timeout-seconds", "1");
    Require(pending.Code == 4 && Field(pending.Output, "operation_result", "pending"), "unacknowledged disable remains pending");
    await Cli("stop", "--session-dir", noack.Directory, "--force", "true", "--timeout-seconds", "3");
    var cancelled = await Launch("missing", "--cancel-after-seconds", "1");
    Require(cancelled.Code == 130 && File.Exists(Path.Combine(cancelled.Directory, "stop-request.yaml")), "cancelled launch records a graceful stop request");
    await Cli("stop", "--session-dir", cancelled.Directory, "--force", "true", "--timeout-seconds", "3");
    var invalid = await Cli("configure", "--session-dir", good.Directory, "--enabled", "perhaps"); Require(invalid.Code == 2, "invalid configuration is rejected");
    var unsupported = await Cli("attach", "--session-dir", good.Directory); Require(unsupported.Code == 2 && unsupported.Output.Contains("unavailable"), "unsupported attachment is explicit");
    if (args.Length > 1)
    {
        var deployment = Path.GetFullPath(args[1]);
        var gameRoot = Path.Combine(root, "synthetic-game"); Directory.CreateDirectory(gameRoot);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(Environment.ProcessPath!)!)) File.Copy(file, Path.Combine(gameRoot, Path.GetFileName(file)));
        var gameExe = Path.Combine(gameRoot, Path.GetFileName(Environment.ProcessPath!));
        var payload = Path.Combine(root, "payload"); Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "injected-a.txt"), "new-a"); File.WriteAllText(Path.Combine(payload, "injected-b.txt"), "new-b");
        File.WriteAllText(Path.Combine(gameRoot, "injected-a.txt"), "original-a"); File.WriteAllText(Path.Combine(gameRoot, "injected-b.txt"), "original-b");
        async Task<(string Directory, int Code, string Output)> Deployed(string script, string mode)
        {
            var dir = Path.Combine(root, "deployed-" + Guid.NewGuid().ToString("N")); owned.Add(dir);
            var result = await Cli("launch", "--exe", gameExe, "--game-root", gameRoot, "--distribution-root", payload, "--deployment-script", script,
                "--session-dir", dir, "--game-arg", "child", "--game-arg", mode, "--replace-path", "injected-a.txt", "--replace-path", "injected-b.txt", "--timeout-seconds", "8");
            return (dir, result.Code, result.Output);
        }
        var deployed = await Deployed(deployment, "good");
        Require(deployed.Code == 0 && File.ReadAllText(Path.Combine(gameRoot, "injected-a.txt")) == "new-a" && File.ReadAllText(Path.Combine(gameRoot, "injected-b.txt")) == "new-b", "real deployment accepts multiple explicit replacements before child launch");
        var deployedStop = await Cli("stop", "--session-dir", deployed.Directory, "--force", "true", "--timeout-seconds", "8");
        Require(deployedStop.Code == 0 && Field(deployedStop.Output, "deployment_restored", "true") && File.ReadAllText(Path.Combine(gameRoot, "injected-a.txt")) == "original-a" && File.ReadAllText(Path.Combine(gameRoot, "injected-b.txt")) == "original-b", "stop waits for real deployment restoration after exact child exit");
        string ScriptLiteral(string text) => "'" + text.Replace("'", "''") + "'";
        var delayedScript = Path.Combine(root, "delayed-deployment.ps1");
        var parameters = "param($Operation,$GameRoot,$SessionDirectory,$DistributionRoot,[string[]]$ReplaceRelativePath,$ExpectedGameProcessId)\n";
        File.WriteAllText(delayedScript, parameters + "if ($Operation -eq 'Restore') { Start-Sleep -Seconds 3 }\n& " + ScriptLiteral(deployment) + " @PSBoundParameters\n");
        var natural = await Deployed(delayedScript, "natural"); Require(natural.Code == 0, "natural-exit child launches under deployment");
        var exitDeadline = DateTime.UtcNow.AddSeconds(6);
        string naturalStatus;
        do { naturalStatus = (await Cli("status", "--session-dir", natural.Directory)).Output; if (Field(naturalStatus, "process_alive", "false")) break; await Task.Delay(50); } while (DateTime.UtcNow < exitDeadline);
        var cleanupPending = await Cli("stop", "--session-dir", natural.Directory, "--timeout-seconds", "1");
        Require(cleanupPending.Code == 4 && Field(cleanupPending.Output, "cleanup_complete", "false"), "dead process does not imply completed deployment cleanup");
        var cleanupDone = await Cli("stop", "--session-dir", natural.Directory, "--timeout-seconds", "6");
        Require(cleanupDone.Code == 0 && Field(cleanupDone.Output, "deployment_restored", "true"), "stop observes delayed natural-exit restoration");
        var failureScript = Path.Combine(root, "failing-deployment.ps1");
        File.WriteAllText(failureScript, parameters + "& " + ScriptLiteral(deployment) + " @PSBoundParameters\nif ($Operation -eq 'Restore') { throw 'controlled cleanup failure after restoration' }\n");
        var failing = await Deployed(failureScript, "natural"); Require(failing.Code == 0, "cleanup-failure case reaches launch gate");
        var failedStop = await Cli("stop", "--session-dir", failing.Directory, "--timeout-seconds", "8");
        Require(failedStop.Code == 3 && Field(failedStop.Output, "state", "failed") && Field(failedStop.Output, "deployment_restored", "false"), "cleanup failure is returned after natural process exit");
    }
    Console.WriteLine($"Completed {checks} public CLI checks. No Unity or VR runtime was launched."); return 0;
}
finally
{
    foreach (var directory in owned)
    {
        if (!File.Exists(Path.Combine(directory, "session.yaml"))) continue;
        try { await Cli("stop", "--session-dir", directory, "--force", "true", "--timeout-seconds", "3"); }
        catch (Exception e) { Console.Error.WriteLine("Owned child cleanup failed: " + e.Message); }
    }
    bool alive = false;
    foreach (var directory in owned.Where(Directory.Exists))
    {
        if (!File.Exists(Path.Combine(directory, "session.yaml"))) continue;
        var status = await Cli("status", "--session-dir", directory);
        if (Field(status.Output, "process_alive", "true")) alive = true;
    }
    if (!alive) Directory.Delete(root, true); else Console.Error.WriteLine("Retained session records: " + root);
}
