using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace UGEVR.Controller;

internal static class Program
{
    private static readonly CancellationTokenSource Cancellation = new();
    public static async Task<int> Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cancellation.Cancel(); };
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("UGEVR launch --exe PATH --session-dir PATH --game-root PATH --distribution-root PAYLOAD --deployment-script PATH [--game-arg VALUE] [--steam-app-id ID] [--timeout-seconds 30]\nDevelopment: replace deployment arguments with --prepared true.\nUGEVR status|disable|stop --session-dir PATH [--timeout-seconds 10]\nUGEVR configure --session-dir PATH --enabled true|false [--recenter true|position|yaw|height|false]\nUGEVR stop --session-dir PATH --force true terminates only this session's verified process.\nRepeated --replace-path RELATIVE_PATH explicitly selects deployment conflicts.\n--cancel-after-seconds N uses the Ctrl+C cancellation path.\nLate attachment and provider launch are unavailable.");
                Console.WriteLine("UGEVR profile validate|resolve --game-id steam:ID|assigned:GUID [--storage-root PATH] [--session-config PATH] [--build-sha256 HASH]\nUGEVR profile save --game-id ID --scope global|personal --expected-revision N --setting Tracking.UnitsPerMeter|Tracking.RetainGamePitchAndRoll --value VALUE\nUGEVR profile reset --game-id ID --scope global|personal --expected-revision N [--setting KEY]\nProfile commands are offline; they do not apply settings to a game.");
                return 0;
            }
            if (args[0] == "profile") return ProfileCli.Run(args.Skip(1).ToArray());
            var o = new Options(args.Skip(1).ToArray());
            if (o.Has("cancel-after-seconds")) Cancellation.CancelAfter(TimeSpan.FromSeconds(o.Integer("cancel-after-seconds", 30, 1, 300)));
            return args[0] switch
            {
                "launch" => await Launch(o), "supervise" => await Supervise(o), "status" => Status(o),
                "configure" => await Command(o, o.Boolean("enabled"), o.Has("recenter") ? o.Required("recenter") : null),
                "disable" => await Command(o, false, null), "stop" => await Stop(o),
                "attach" => throw new ArgumentException("Late attachment is unavailable. Use startup launch."),
                _ => throw new ArgumentException("Unknown operation. Run help.")
            };
        }
        catch (OperationCanceledException) { FlatYaml.Print(new() { ["error"] = "Operation cancelled; session ownership remains recorded." }); return 130; }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or FormatException or JsonException or OverflowException)
        { FlatYaml.Print(new() { ["error"] = e.Message }); return 2; }
    }
    private static string DirectoryPath(Options o) => Path.GetFullPath(o.Required("session-dir"));
    private static string SessionPath(string directory) => Path.Combine(directory, "session.yaml");
    private static TimeSpan Timeout(Options o) => TimeSpan.FromSeconds(o.Integer("timeout-seconds", 30, 1, 300));
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static async Task<int> Launch(Options o)
    {
        o.Allow("exe", "session-dir", "game-arg", "timeout-seconds", "game-root", "distribution-root", "deployment-script", "prepared", "replace-path", "cancel-after-seconds", "steam-app-id");
        var directory = DirectoryPath(o); var executable = Path.GetFullPath(o.Required("exe"));
        if (!File.Exists(executable)) throw new ArgumentException("Executable does not exist.");
        _ = Timeout(o);
        if (o.Has("steam-app-id") && (!uint.TryParse(o.Required("steam-app-id"), NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) || steamId == 0)) throw new ArgumentException("--steam-app-id must be a positive decimal Steam application ID.");
        bool prepared = o.Boolean("prepared", false);
        if (prepared && (o.Has("deployment-script") || o.Has("game-root") || o.Has("distribution-root") || o.Has("replace-path"))) throw new ArgumentException("Prepared mode cannot include deployment options.");
        if (!prepared)
        {
            foreach (var key in new[] { "game-root", "distribution-root", "deployment-script" }) _ = o.Required(key);
            if (!File.Exists(Path.GetFullPath(o.Required("deployment-script")))) throw new ArgumentException("Deployment script does not exist.");
            var root = Path.GetFullPath(o.Required("game-root")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Directory.Exists(root)) throw new ArgumentException("Game root must exist.");
            if (!executable.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Executable must be inside the selected game root.");
            RejectReparsePoints(executable);
        }
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new ArgumentException("Use an empty, unique session directory. Existing sessions are never overwritten.");
        Directory.CreateDirectory(directory);
        // CreateNew gives simultaneous launch callers one owner of this directory.
        using (new FileStream(Path.Combine(directory, "launch-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        var run = Guid.NewGuid().ToString("D");
        FlatYaml.Write(SessionPath(directory), new()
        {
            ["schema_version"] = "1", ["run_id"] = run, ["state"] = "preparing", ["executable"] = executable,
            ["process_id"] = "0", ["process_start_utc_ticks"] = "0", ["owned_process"] = "true", ["last_error"] = "",
            ["prepared_mode"] = prepared ? "true" : "false", ["deployment_restored"] = prepared ? "not_applicable" : "false",
            ["executable_sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant(),
            ["cleanup_complete"] = "false"
        });
        var start = SelfStart(); start.ArgumentList.Add("supervise");
        foreach (var argument in o.Arguments) start.ArgumentList.Add(argument);
        using var supervisor = Process.Start(start) ?? throw new InvalidOperationException("Cannot start supervisor.");
        var deadline = DateTime.UtcNow + Timeout(o);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                Cancellation.Token.ThrowIfCancellationRequested();
                var session = ReadSession(directory); var view = View(directory);
                if (view.GetValueOrDefault("failure_kind") == "initialization")
                { view["operation_result"] = "initialization_failed"; FlatYaml.Print(view); return 3; }
                if (session["state"] is "failed" or "stopped") { FlatYaml.Print(view); return 3; }
                if (view["runtime_ready"] == "true" && view["engine_ready"] == "true") { FlatYaml.Print(view); return 0; }
                await Task.Delay(100, Cancellation.Token);
            }
            var pending = View(directory); pending["operation_result"] = "readiness_timeout";
            FlatYaml.Print(pending); return 4;
        }
        catch (OperationCanceledException)
        {
            FlatYaml.Write(Path.Combine(directory, "stop-request.yaml"), new() { ["run_id"] = run, ["force"] = "false" });
            throw;
        }
    }
    private static ProcessStartInfo SelfStart()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate controller runtime.");
        // ShellExecute launches the hidden supervisor without inheriting the CLI caller's pipe handles.
        // A redirected CreateProcess child can otherwise keep stdout open after the launch command exits.
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
        return start;
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Selected executable cannot cross a reparse point: " + part);
    }

    private static async Task<int> Supervise(Options o)
    {
        var directory = DirectoryPath(o); var session = ReadSession(directory);
        session["supervisor_process_id"] = Number(Environment.ProcessId);
        session["supervisor_start_utc_ticks"] = Number(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);
        FlatYaml.Write(SessionPath(directory), session);
        Process? game = null;
        FileStream? gameLease = null;
        bool leaseHeld = false;
        try
        {
            var leaseRoot = o.Boolean("prepared", false) ? Path.GetDirectoryName(session["executable"])! : Path.GetFullPath(o.Required("game-root"));
            var leaseKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(leaseRoot.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));
            var lockDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UGEVR", "locks");
            Directory.CreateDirectory(lockDirectory);
            try { gameLease = new FileStream(Path.Combine(lockDirectory, leaseKey + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); leaseHeld = true; }
            catch (IOException) { throw new IOException("Another UGEVR session owns this game directory."); }
            if (!o.Boolean("prepared", false)) await Deployment(o, directory, "Deploy", 0);
            var start = new ProcessStartInfo(session["executable"]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(session["executable"])! };
            foreach (var argument in o.All("game-arg")) start.ArgumentList.Add(argument);
            start.Environment["UGEVR_SESSION_ID"] = session["run_id"];
            start.Environment["UGEVR_SESSION_DIRECTORY"] = directory;
            if (o.Has("steam-app-id")) { start.Environment["SteamAppId"] = o.Required("steam-app-id"); start.Environment["SteamGameId"] = o.Required("steam-app-id"); }
            game = Process.Start(start) ?? throw new InvalidOperationException("Game process did not start.");
            var stdout = Drain(game.StandardOutput, Path.Combine(directory, "process-stdout.log"));
            var stderr = Drain(game.StandardError, Path.Combine(directory, "process-stderr.log"));
            session["process_id"] = Number(game.Id);
            session["process_start_utc_ticks"] = Number(game.StartTime.ToUniversalTime().Ticks);
            session["state"] = "waiting_for_injector"; FlatYaml.Write(SessionPath(directory), session);
            bool gracefulRequested = false;
            while (!game.HasExited)
            {
                var stopPath = Path.Combine(directory, "stop-request.yaml");
                if (File.Exists(stopPath))
                {
                    var stop = FlatYaml.Read(stopPath);
                    if (stop.GetValueOrDefault("run_id") == session["run_id"])
                    {
                        session["state"] = "stopping";
                        if (stop.GetValueOrDefault("force") == "true") game.Kill(entireProcessTree: false);
                        else if (!gracefulRequested) { game.CloseMainWindow(); gracefulRequested = true; }
                    }
                }
                if (session["state"] != "stopping")
                {
                    var view = View(directory);
                    if (view.GetValueOrDefault("failure_kind") == "initialization")
                    {
                        session["state"] = "failed"; session["failure_kind"] = "initialization";
                        session["initialization_error"] = view["initialization_error"]; session["last_error"] = view["initialization_error"];
                    }
                    else session["state"] = view["runtime_ready"] == "true" && view["engine_ready"] == "true" ? "active" : "waiting_for_injector";
                }
                FlatYaml.Write(SessionPath(directory), session);
                await Task.Delay(200);
            }
            await Task.WhenAll(stdout, stderr);
            session["exit_code"] = Number(game.ExitCode); session["state"] = "stopped";
        }
        catch (Exception e)
        {
            session["state"] = "failed"; session["last_error"] = e.Message;
            FlatYaml.Write(SessionPath(directory), session);
            // Leave running game untouched if monitoring fails. Restore is unsafe until its recorded process exits.
            while (game is not null && !game.HasExited)
            {
                try
                {
                    var stopPath = Path.Combine(directory, "stop-request.yaml");
                    if (File.Exists(stopPath))
                    {
                        var stop = FlatYaml.Read(stopPath);
                        if (stop.GetValueOrDefault("run_id") == session["run_id"])
                        {
                            if (stop.GetValueOrDefault("force") == "true") game.Kill(entireProcessTree: false);
                            else if (stop.GetValueOrDefault("force") == "false") game.CloseMainWindow();
                        }
                    }
                }
                catch (Exception stopError) when (stopError is IOException or FormatException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                await Task.Delay(200);
            }
        }
        finally
        {
            if (leaseHeld && !o.Boolean("prepared", false) && File.Exists(Path.Combine(directory, "deployment-receipt.yaml")))
            {
                try { await Deployment(o, directory, "Restore", game?.Id ?? 0); session["deployment_restored"] = "true"; }
                catch (Exception e) { session["deployment_restored"] = "false"; session["last_error"] += "; restore failed: " + e.Message; session["state"] = "failed"; }
            }
            session["cleanup_complete"] = "true";
            game?.Dispose(); FlatYaml.Write(SessionPath(directory), session);
            gameLease?.Dispose();
        }
        return session["state"] == "failed" ? 3 : 0;
    }
    private static async Task Deployment(Options o, string directory, string operation, int expectedPid)
    {
        var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var command = "$ErrorActionPreference='Stop'; & " + Literal(Path.GetFullPath(o.Required("deployment-script"))) + " -Operation " + Literal(operation) + " -SessionDirectory " + Literal(directory) + " -GameRoot " + Literal(Path.GetFullPath(o.Required("game-root")));
        if (operation == "Deploy")
        {
            command += " -DistributionRoot " + Literal(Path.GetFullPath(o.Required("distribution-root")));
            var replacements = o.All("replace-path").ToArray();
            if (replacements.Length > 0) command += " -ReplaceRelativePath @(" + string.Join(",", replacements.Select(Literal)) + ")";
        }
        if (expectedPid != 0) command += " -ExpectedGameProcessId " + Number(expectedPid);
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start deployment.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: false); throw new IOException("Deployment exceeded 60 seconds; inspect receipt before retrying."); }
        await output; var detail = await error;
        if (detail.Length > 8192) detail = detail[..8192] + " [truncated]";
        if (process.ExitCode != 0) throw new IOException($"Deployment {operation} failed: {detail.Trim()}");
    }

    private static async Task Drain(StreamReader source, string path)
    {
        using var destination = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        var buffer = new char[4096]; int retained = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer);
            if (read == 0) break;
            int count = Math.Min(read, Math.Max(0, 65536 - retained));
            if (count > 0) { await destination.WriteAsync(buffer.AsMemory(0, count)); await destination.FlushAsync(); retained += count; }
        }
    }

    private static Dictionary<string, string> ReadSession(string directory)
    {
        var session = FlatYaml.Read(SessionPath(directory));
        if (session.GetValueOrDefault("schema_version") != "1" || !Guid.TryParse(session.GetValueOrDefault("run_id"), out _)) throw new FormatException("Invalid session identity or schema.");
        foreach (var key in new[] { "state", "executable", "process_id", "process_start_utc_ticks", "owned_process", "last_error", "cleanup_complete" }) if (!session.ContainsKey(key)) throw new FormatException("Missing session field: " + key);
        return session;
    }
    private static bool IsAlive(Dictionary<string, string> session)
    {
        if (!int.TryParse(session.GetValueOrDefault("process_id"), out var pid) || pid <= 0 || !long.TryParse(session.GetValueOrDefault("process_start_utc_ticks"), out var ticks)) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks; }
        catch (ArgumentException) { return false; }
    }
    private static Dictionary<string, string> View(string directory)
    {
        var session = ReadSession(directory);
        var view = new Dictionary<string, string>(session)
        {
            ["process_alive"] = IsAlive(session) ? "true" : "false", ["engine_ready"] = "false", ["runtime_ready"] = "false",
            ["rendering_active"] = "false", ["stream_ready"] = "false", ["stream_status"] = "unavailable", ["injector_status"] = "missing"
        };
        var path = Path.Combine(directory, "injector-status.yaml");
        if (!File.Exists(path)) return view;
        try
        {
            var producer = FlatYaml.Read(path);
            if (producer.GetValueOrDefault("schema_version") != "1" || producer.GetValueOrDefault("run_id") != session["run_id"] || producer.GetValueOrDefault("process_id") != session["process_id"])
            { view["injector_status"] = "identity_mismatch"; return view; }
            if (view["process_alive"] != "true" || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromSeconds(10))
            { view["injector_status"] = "stale"; return view; }
            foreach (var boolean in new[] { "engine_ready", "runtime_ready", "rendering_active" })
                if (producer.GetValueOrDefault(boolean) is not ("true" or "false")) throw new FormatException("Invalid injector boolean: " + boolean);
            if (!long.TryParse(producer.GetValueOrDefault("acknowledged_revision"), out var ack) || ack < 0 || !ulong.TryParse(producer.GetValueOrDefault("submitted_pairs"), out _)) throw new FormatException("Invalid injector counter.");
            if (producer.GetValueOrDefault("runtime") != "openvr") throw new FormatException("Unsupported runtime in this protocol version.");
            foreach (var field in new[] { "unity_version", "engine_ready", "runtime", "runtime_ready", "rendering_active", "submitted_pairs", "acknowledged_revision" }) view[field] = producer.GetValueOrDefault(field, "");
            view["injector_error"] = producer.GetValueOrDefault("last_error", ""); view["injector_status"] = "fresh";
            var initializationError = producer.GetValueOrDefault("initialization_error", "");
            view["initialization_error"] = initializationError;
            if (initializationError.Length > 0)
            {
                view["state"] = "failed"; view["failure_kind"] = "initialization";
                view["last_error"] = initializationError;
                view["runtime_ready"] = "false"; view["rendering_active"] = "false";
            }
        }
        catch (Exception e) when (e is IOException or FormatException or JsonException)
        { view["injector_status"] = "invalid"; view["injector_error"] = e.Message; }
        return view;
    }
    private static int Status(Options o) { o.Allow("session-dir"); FlatYaml.Print(View(DirectoryPath(o))); return 0; }

    private static async Task<int> Command(Options o, bool enabled, string? recenter, bool emit = true)
    {
        o.Allow("session-dir", "enabled", "recenter", "timeout-seconds", "cancel-after-seconds");
        if (recenter is not (null or "true" or "false" or "position" or "yaw" or "height")) throw new ArgumentException("Unsupported recenter operation.");
        if (!enabled && recenter is not (null or "false")) throw new ArgumentException("Recenter requires enabled rendering.");
        var directory = DirectoryPath(o); var session = ReadSession(directory);
        if (!IsAlive(session)) throw new InvalidOperationException("The owned process is no longer alive.");
        FileStream? lease = null; var deadline = DateTime.UtcNow + Timeout(o);
        while (lease is null && DateTime.UtcNow < deadline)
        {
            try { lease = new FileStream(Path.Combine(directory, "command.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, Cancellation.Token); }
        }
        if (lease is null) throw new IOException("Another command is pending; command lock deadline expired.");
        using (lease)
        {
            var path = Path.Combine(directory, "command.txt"); long revision = 1;
            if (File.Exists(path))
            {
                var prior = File.ReadAllLines(path).FirstOrDefault(x => x.StartsWith("revision=", StringComparison.Ordinal));
                if (prior is null || !long.TryParse(prior[9..], out var previous) || previous < 0 || previous == long.MaxValue) throw new FormatException("Invalid prior command revision.");
                revision = previous + 1;
            }
            FlatYaml.AtomicText(path, $"run_id={session["run_id"]}\nrevision={revision}\nenabled={enabled.ToString().ToLowerInvariant()}\n" + (recenter is not null ? "recenter=" + recenter + "\n" : ""));
            while (DateTime.UtcNow < deadline)
            {
                var view = View(directory);
                if (view["injector_status"] == "fresh" && view.GetValueOrDefault("acknowledged_revision") == Number(revision))
                { view["operation_result"] = "acknowledged"; if (emit) FlatYaml.Print(view); return 0; }
                if (view["process_alive"] != "true") break;
                await Task.Delay(100, Cancellation.Token);
            }
            var pending = View(directory); pending["operation_result"] = "pending"; pending["requested_revision"] = Number(revision); if (emit) FlatYaml.Print(pending); return 4;
        }
    }
    private static async Task<int> Stop(Options o)
    {
        o.Allow("session-dir", "timeout-seconds", "force", "cancel-after-seconds");
        var directory = DirectoryPath(o); var session = ReadSession(directory); var timeout = Timeout(o); bool force = o.Boolean("force", false);
        if (session.GetValueOrDefault("owned_process") != "true") throw new InvalidOperationException("This session does not own the process.");
        string disableResult = "not_requested";
        string? disableError = null;
        Dictionary<string, string> StopView()
        {
            var view = View(directory);
            view["disable_result"] = disableResult;
            if (disableError is not null) view["disable_error"] = disableError;
            return view;
        }
        if (IsAlive(session))
        {
            try
            {
                var result = await Command(new Options(new[] { "--session-dir", directory, "--timeout-seconds", "1" }), false, null, emit: false);
                disableResult = result == 0 ? "acknowledged" : "pending";
            }
            catch (InvalidOperationException) when (!IsAlive(ReadSession(directory))) { disableResult = "process_exited"; }
            catch (IOException error)
            {
                // Another configuration command may retain its lease while
                // tracking is unavailable. It cannot veto an explicit stop.
                // The supervisor still owns and verifies the actual process.
                disableResult = "unavailable"; disableError = error.Message;
            }
        }
        FlatYaml.Write(Path.Combine(directory, "stop-request.yaml"), new() { ["run_id"] = session["run_id"], ["force"] = force ? "true" : "false" });
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = ReadSession(directory);
            if (!IsAlive(current) && current.GetValueOrDefault("cleanup_complete") == "true" && current["state"] is "stopped" or "failed") { FlatYaml.Print(StopView()); return current["state"] == "failed" ? 3 : 0; }
            await Task.Delay(100, Cancellation.Token);
        }
        var pending = StopView(); pending["operation_result"] = "stop_pending"; FlatYaml.Print(pending); return 4;
    }
}
