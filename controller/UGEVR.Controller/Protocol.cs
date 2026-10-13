using System.Text.Json;

namespace UGEVR.Controller;

internal sealed class Options
{
    public string[] Arguments { get; }
    private readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
    public Options(string[] args)
    {
        Arguments = args;
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length) throw new ArgumentException("Options require --name value pairs.");
            var key = args[i][2..];
            if (!values.TryGetValue(key, out var list)) values[key] = list = new();
            if (list.Count > 0 && key is not ("game-arg" or "replace-path")) throw new ArgumentException("Duplicate option: " + key);
            list.Add(args[i + 1]);
        }
    }
    public bool Has(string key) => values.ContainsKey(key);
    public IEnumerable<string> All(string key) => values.TryGetValue(key, out var list) ? list : [];
    public string Required(string key) => values.TryGetValue(key, out var list) && !string.IsNullOrWhiteSpace(list[0]) ? list[0] : throw new ArgumentException("Missing --" + key);
    public bool Boolean(string key, bool? fallback = null) => Has(key) ? bool.TryParse(Required(key), out var value) ? value : throw new ArgumentException("Invalid boolean for --" + key) : fallback ?? throw new ArgumentException("Missing --" + key);
    public int Integer(string key, int fallback, int min, int max) => !Has(key) ? fallback : int.TryParse(Required(key), out var value) && value >= min && value <= max ? value : throw new ArgumentException($"--{key} must be {min} through {max}.");
    public void Allow(params string[] keys) { foreach (var key in values.Keys) if (!keys.Contains(key)) throw new ArgumentException("Unknown option: " + key); }
}

internal static class FlatYaml
{
    // The version 1 protocol permits only a flat scalar mapping, not general YAML.
    public static Dictionary<string, string> Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new FormatException("Protocol file exceeds 64 KiB.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        foreach (var line in reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var split = line.IndexOf(':');
            if (split <= 0 || char.IsWhiteSpace(line[0])) throw new FormatException("Expected flat YAML mapping.");
            var key = line[..split]; var scalar = line[(split + 1)..].Trim();
            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || scalar.Length == 0) throw new FormatException("Invalid protocol key or scalar.");
            string value;
            if (scalar.StartsWith('"')) value = JsonSerializer.Deserialize<string>(scalar) ?? "";
            else if (scalar.StartsWith('\''))
            {
                if (scalar.Length < 2 || !scalar.EndsWith('\'')) throw new FormatException("Invalid single quoted YAML string.");
                value = scalar[1..^1].Replace("''", "'");
            }
            else
            {
                if (scalar.IndexOfAny(['{', '}', '[', ']', '#', '&', '*', '!', '|', '>']) >= 0) throw new FormatException("Unsupported YAML scalar syntax.");
                value = scalar;
            }
            if (!result.TryAdd(key, value)) throw new FormatException("Duplicate protocol key: " + key);
        }
        return result;
    }
    private static string Encode(Dictionary<string, string> values) => string.Concat(values.Select(pair => pair.Key + ": " + JsonSerializer.Serialize(pair.Value) + "\n"));
    public static void Print(Dictionary<string, string> values) => Console.Write(Encode(values));
    public static void Write(string path, Dictionary<string, string> values) => AtomicText(path, Encode(values));
    public static void AtomicText(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, path, overwrite: true); break; }
                catch (Exception error) when (attempt < 20 && error is IOException or UnauthorizedAccessException) { Thread.Sleep(10); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
