using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UGEVR.Controller;

// Pure declarative values. No engine, process, extension or runtime activation.
public enum ProfileValueType { Single, Boolean }
public sealed record ProfileSetting(string Name, ProfileValueType Type, string DefaultValue, Func<string, string> Validate);
public sealed record ProfileValue(string Value, string Source);
public sealed record ProfileLayer(long Revision, IReadOnlyDictionary<string, string> Values);
public sealed record ProfileResolution(IReadOnlyDictionary<string, ProfileValue> Values,
    IReadOnlyDictionary<string, string> Metadata, long GlobalRevision, long PersonalRevision);

public static class ProfileModel
{
    public static readonly IReadOnlyDictionary<string, ProfileSetting> Settings =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, ProfileSetting>(new Dictionary<string, ProfileSetting>(StringComparer.Ordinal)
        {
            ["Tracking.UnitsPerMeter"] = new("Tracking.UnitsPerMeter", ProfileValueType.Single, "1", value =>
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number) && number > 0
                    ? number.ToString("R", CultureInfo.InvariantCulture) : throw new FormatException("Tracking.UnitsPerMeter must be finite and positive.")),
            ["Tracking.RetainGamePitchAndRoll"] = new("Tracking.RetainGamePitchAndRoll", ProfileValueType.Boolean, "false", value =>
                bool.TryParse(value, out var flag) ? flag ? "true" : "false" : throw new FormatException("Tracking.RetainGamePitchAndRoll must be true or false."))
        });

    public static string Validate(string key, string value)
    {
        if (value.Any(char.IsControl)) throw new FormatException("Profile values cannot contain control characters.");
        return Settings.TryGetValue(key, out var setting) ? setting.Validate(value) : throw new FormatException("Unsupported profile setting: " + key);
    }

    public static ProfileLayer Parse(string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > 65536) throw new FormatException("Profile settings exceed 64 KiB.");
        if (content.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new FormatException("Profile text contains unsupported control characters.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? section = null; var sections = new HashSet<string>(StringComparer.Ordinal); long revision = 0; bool revisionSeen = false;
        foreach (var raw in content.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Trim();
            const string revisionPrefix = "# UGEVR profile revision: ";
            if (line.StartsWith(revisionPrefix, StringComparison.Ordinal))
            {
                if (revisionSeen || !long.TryParse(line[revisionPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 0)
                    throw new FormatException("Invalid or duplicate profile revision.");
                revisionSeen = true; continue;
            }
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                if (section != "Tracking" || !sections.Add(section)) throw new FormatException("Unsupported or duplicate profile section.");
                continue;
            }
            var equal = line.IndexOf('=');
            if (section == null || equal <= 0) throw new FormatException("Expected [Tracking] and key=value settings.");
            var key = section + "." + line[..equal].Trim();
            if (!values.TryAdd(key, Validate(key, line[(equal + 1)..].Trim()))) throw new FormatException("Duplicate profile setting: " + key);
        }
        return new(revision, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values));
    }

    public static string Serialize(ProfileLayer layer)
    {
        if (layer.Revision < 0) throw new FormatException("Invalid profile revision.");
        var text = new StringBuilder("# UGEVR profile revision: " + layer.Revision.ToString(CultureInfo.InvariantCulture) + "\n");
        if (layer.Values.Count != 0) text.Append("[Tracking]\n");
        foreach (var pair in layer.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var value = Validate(pair.Key, pair.Value);
            text.Append(pair.Key["Tracking.".Length..]).Append('=').Append(value).Append('\n');
        }
        return text.ToString();
    }

    public static IReadOnlyDictionary<string, ProfileValue> Resolve(params (string Source, ProfileLayer Layer)[] layers)
    {
        var result = Settings.ToDictionary(pair => pair.Key, pair => new ProfileValue(pair.Value.DefaultValue, "built_in"), StringComparer.Ordinal);
        foreach (var (source, layer) in layers)
            foreach (var pair in layer.Values) result[pair.Key] = new(Validate(pair.Key, pair.Value), source);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ProfileValue>(result);
    }
}

public sealed class ProfileStore
{
    public string Root { get; }
    public string GameId { get; }
    public string GameDirectory { get; }
    public ProfileStore(string root, string gameId)
    {
        if (!ValidGameId(gameId)) throw new ArgumentException("Use steam:POSITIVE_APP_ID or assigned:LOWERCASE_GUID for --game-id.");
        Root = Path.GetFullPath(root); GameId = gameId;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId))).ToLowerInvariant();
        GameDirectory = Path.Combine(Root, "games", digest); CheckPath(GameDirectory);
    }
    static bool ValidGameId(string id) => id.StartsWith("steam:", StringComparison.Ordinal)
        ? uint.TryParse(id[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var app) && app > 0 && app.ToString(CultureInfo.InvariantCulture) == id[6..]
        : id.StartsWith("assigned:", StringComparison.Ordinal) && Guid.TryParseExact(id[9..], "D", out var guid) && guid != Guid.Empty && guid.ToString("D") == id[9..];
    public static void CheckPath(string path)
    {
        bool leaf = true;
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Profile path contains a reparse point.");
                if (!leaf && (attributes & FileAttributes.Directory) == 0) throw new IOException("Profile path contains a non-directory ancestor.");
            }
            leaf = false;
        }
    }
    static ProfileLayer Empty() => new(0, new Dictionary<string, string>());
    public static ProfileLayer Read(string file)
    {
        CheckPath(file);
        if (Directory.Exists(file)) throw new IOException("Profile settings path is a directory.");
        if (!File.Exists(file)) return Empty();
        return ProfileModel.Parse(ReadText(file));
    }
    static string ReadText(string file)
    {
        CheckPath(file);
        using var stream = OpenRead(file);
        if (stream.Length > 65536) throw new FormatException("Profile settings exceed 64 KiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        return reader.ReadToEnd().TrimStart('\uFEFF');
    }
    static FileStream OpenRead(string file)
    {
        for (int attempt = 0; ; attempt++)
        {
            // Atomic replacement can publish a new file while this handle keeps
            // the old complete snapshot. In-place writes remain excluded.
            try { return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (IOException error) when (attempt < 20 && (error.HResult & 0xffff) is 32 or 33) { Thread.Sleep(5); }
        }
    }
    public string ScopeFile(string scope) => scope switch
    {
        "global" => Path.Combine(Root, "global", "config.txt"),
        "personal" => Path.Combine(GameDirectory, "personal", "config.txt"),
        _ => throw new ArgumentException("Editable profile scope must be global or personal.")
    };
    public Dictionary<string, string> SharedMetadata(string? requestedBuild)
    {
        var directory = Path.Combine(GameDirectory, "shared"); var metadata = Path.Combine(directory, "profile.yaml");
        CheckPath(metadata);
        if (!File.Exists(metadata))
        {
            if (Directory.Exists(directory)) throw new FormatException("Shared profile directory needs profile.yaml identity metadata.");
            return new() { ["shared_profile"] = "absent", ["build_evidence"] = "unverified" };
        }
        var data = FlatYaml.Parse(ReadText(metadata));
        if (data.Keys.Any(key => key is not ("schema_version" or "profile_id" or "game_id" or "build_sha256"))) throw new FormatException("Unsupported profile metadata field.");
        if (data.GetValueOrDefault("schema_version") != "1") throw new FormatException("Unsupported profile schema version.");
        if (!Guid.TryParseExact(data.GetValueOrDefault("profile_id"), "D", out var profile) || profile == Guid.Empty || profile.ToString("D") != data.GetValueOrDefault("profile_id")) throw new FormatException("Invalid profile ID.");
        if (data.GetValueOrDefault("game_id") != GameId) throw new FormatException("Shared profile game identity mismatch.");
        var build = data.GetValueOrDefault("build_sha256");
        if (build != null && !ValidHash(build)) throw new FormatException("Invalid build fingerprint.");
        if (requestedBuild != null && !ValidHash(requestedBuild)) throw new ArgumentException("--build-sha256 must contain 64 lowercase hexadecimal characters.");
        if (build != null && requestedBuild != null && build != requestedBuild) throw new FormatException("Shared profile build changed; revalidation is required.");
        return new() { ["shared_profile"] = profile.ToString("D"), ["build_evidence"] = build == null || requestedBuild == null ? "unverified" : "fingerprint_match_not_runtime_validation" };
    }
    public static bool ValidHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public ProfileResolution Resolve(string? sessionFile, string? build)
    {
        var metadata = SharedMetadata(build);
        if (sessionFile != null && !File.Exists(sessionFile)) throw new FileNotFoundException("Selected session profile does not exist.");
        var global = Read(ScopeFile("global"));
        var shared = Read(Path.Combine(GameDirectory, "shared", "config.txt"));
        var personal = Read(ScopeFile("personal"));
        var session = sessionFile == null ? Empty() : Read(Path.GetFullPath(sessionFile));
        var values = ProfileModel.Resolve(("global", global), ("shared", shared), ("personal", personal), ("session", session));
        return new(values, metadata, global.Revision, personal.Revision);
    }
    public long Edit(string scope, long expectedRevision, string? key, string? value, bool reset)
    {
        if (expectedRevision < 0) throw new ArgumentException("Expected revision cannot be negative.");
        if (key != null) _ = ProfileModel.Validate(key, value ?? ProfileModel.Settings.GetValueOrDefault(key)?.DefaultValue ?? "");
        if (!reset && (key == null || value == null)) throw new ArgumentException("Saving a setting requires --setting and --value.");
        var file = ScopeFile(scope); CheckPath(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!); CheckPath(file);
        CheckPath(file + ".lock");
        using var lease = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        CheckPath(file); var old = Read(file);
        if (old.Revision != expectedRevision) throw new InvalidOperationException("Stale profile edit; read the current scope revision first.");
        var values = new Dictionary<string, string>(old.Values, StringComparer.Ordinal);
        if (reset) { if (key == null) values.Clear(); else values.Remove(key); }
        else values[key!] = ProfileModel.Validate(key!, value!);
        long next = checked(old.Revision + 1);
        FlatYaml.AtomicText(file, ProfileModel.Serialize(new(next, values)));
        return next;
    }
}

internal static class ProfileCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Use profile validate|resolve|save|reset.");
        var action = args[0]; var o = new Options(args.Skip(1).ToArray());
        o.Allow("storage-root", "game-id", "build-sha256", "session-config", "scope", "setting", "value", "expected-revision");
        var root = o.Has("storage-root") ? o.Required("storage-root") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UGEVR", "profiles");
        var store = new ProfileStore(root, o.Required("game-id"));
        string? build = o.Has("build-sha256") ? o.Required("build-sha256") : null;
        if (build != null && !ProfileStore.ValidHash(build)) throw new ArgumentException("--build-sha256 must contain 64 lowercase hexadecimal characters.");
        var output = new Dictionary<string, string> { ["schema_version"] = "1", ["game_id"] = store.GameId, ["operation"] = action, ["runtime_applied"] = "false" };
        if (action is "validate" or "resolve")
        {
            if (o.Has("scope") || o.Has("setting") || o.Has("value") || o.Has("expected-revision")) throw new ArgumentException("Read-only profile commands cannot edit a scope.");
            var resolved = store.Resolve(o.Has("session-config") ? o.Required("session-config") : null, build);
            foreach (var pair in resolved.Metadata) output[pair.Key] = pair.Value;
            output["shared_profile_directory"] = Path.Combine(store.GameDirectory, "shared");
            foreach (var pair in resolved.Values)
            {
                string key = pair.Key.Replace('.', '_'); output[key] = pair.Value.Value; output[key + "_source"] = pair.Value.Source;
            }
            output["global_revision"] = resolved.GlobalRevision.ToString(CultureInfo.InvariantCulture);
            output["personal_revision"] = resolved.PersonalRevision.ToString(CultureInfo.InvariantCulture);
        }
        else if (action is "save" or "reset")
        {
            if (o.Has("session-config") || o.Has("build-sha256")) throw new ArgumentException("Editing does not accept session data or change shared build constraints.");
            if (action == "reset" && o.Has("value")) throw new ArgumentException("Reset does not accept --value.");
            if (!long.TryParse(o.Required("expected-revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var expectedRevision) || expectedRevision < 0)
                throw new ArgumentException("--expected-revision must be a nonnegative 64-bit integer.");
            long revision = store.Edit(o.Required("scope"), expectedRevision, o.Has("setting") ? o.Required("setting") : null, o.Has("value") ? o.Required("value") : null, action == "reset");
            output["scope"] = o.Required("scope"); output["revision"] = revision.ToString(CultureInfo.InvariantCulture);
        }
        else throw new ArgumentException("Unknown profile operation.");
        FlatYaml.Print(output); return 0;
    }
}
