using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UGEVR.Controller;

internal sealed class InspectionBudget
{
    public const int MaximumFiles = 256, MaximumDirectoryEntries = 1024, MaximumOutput = 524288;
    public const long MaximumFileBytes = 134217728, MaximumTotalBytes = 536870912;
    readonly Stopwatch clock = Stopwatch.StartNew();
    public int Files;
    public long Bytes;
    public long Remaining => MaximumTotalBytes - Bytes;
    public void Check(long bytes = 0)
    {
        if (clock.Elapsed.TotalSeconds > 10) throw new InspectionRefusal("time_budget_exceeded");
        if (bytes < 0 || bytes > MaximumTotalBytes - Bytes) throw new InspectionRefusal("total_read_budget_exceeded");
        Bytes += bytes;
    }
}
internal sealed class InspectionRefusal(string code) : Exception(code) { public string Code { get; } = code; }
internal sealed record ManagedFileFacts(string AssemblyName, string AssemblyVersion, string ModuleMvid,
    bool IlOnly, bool Requires32Bit, bool Prefers32Bit, string[] PipelineReferences, string[] VersionAttributes);
internal sealed record PeFileFacts(string Machine, string MachineCode, string Format, string ImageKind,
    string[] RetainedExportNames, string[] FixedProductVersions, ManagedFileFacts? Managed);
internal sealed record ArtifactFacts(string Path, string Role, string State, long? Length = null,
    string? Sha256 = null, PeFileFacts? Pe = null, int? Il2CppMetadataVersion = null, string? ReasonCode = null);
internal sealed record EvidenceVersion(string Artifact, string Source, string Value, string? Family, string? ReleaseTag);

// Reads data from one opened file. It never loads an assembly or native module.
internal sealed class PlayerFileReader(FileStream file, InspectionBudget budget)
{
    static readonly HashSet<string> ExportMarkers = new(StringComparer.Ordinal)
    { "UnityMain", "mono_get_root_domain", "mono_runtime_invoke", "mono_thread_attach", "mono_jit_init_version", "il2cpp_domain_get", "il2cpp_runtime_invoke", "il2cpp_thread_attach" };
    internal static bool PipelineName(string name) => name is "Unity.RenderPipelines.Universal.Runtime" or "Unity.RenderPipelines.HighDefinition.Runtime" or "Unity.RenderPipelines.Core.Runtime";
    byte[] Read(long offset, int count)
    {
        budget.Check(count);
        if (offset < 0 || count < 0 || offset > file.Length - count) throw new InspectionRefusal("truncated_or_invalid_offset");
        byte[] bytes = new byte[count];file.Position = offset;file.ReadExactly(bytes);return bytes;
    }
    static ushort U16(byte[] b, int i) => (ushort)(b[i] | b[i + 1] << 8);
    static uint U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);
    public int Il2CppHeader()
    {
        var header = Read(0, 8);
        if (U32(header, 0) != 0xfab11baf) throw new InspectionRefusal("il2cpp_metadata_magic_mismatch");
        int version = checked((int)U32(header, 4));
        if (version < 1 || version > 100) throw new InspectionRefusal("il2cpp_metadata_version_out_of_range");
        return version; // Header evidence, not validation of every metadata table.
    }
    public PeFileFacts Pe()
    {
        var dos = Read(0, 64);
        if (U16(dos, 0) != 0x5a4d) throw new InspectionRefusal("not_pe");
        uint start = U32(dos, 60);
        if (start < 64 || start > 1048576) throw new InspectionRefusal("pe_header_offset_out_of_range");
        var coff = Read(start, 24);
        if (U32(coff, 0) != 0x4550) throw new InspectionRefusal("pe_signature_mismatch");
        int sections = U16(coff, 6), optional = U16(coff, 20);
        if (sections < 1 || sections > 96 || optional < 96 || optional > 4096) throw new InspectionRefusal("pe_header_shape_refused");
        _ = Read(start + 24, checked(optional + sections * 40));
        file.Position = 0;
        using var pe = new PEReader(file, PEStreamOptions.LeaveOpen);
        var headers = pe.PEHeaders;
        if (headers.PEHeader == null) throw new InspectionRefusal("missing_pe_optional_header");
        ushort machine = U16(coff, 4);
        string architecture = machine switch { 0x8664 => "x64", 0x14c => "x86", 0xaa64 => "arm64", 0x1c4 => "arm", _ => "other_or_unknown" };
        string format = headers.PEHeader.Magic switch { PEMagic.PE32 => "PE32", PEMagic.PE32Plus => "PE32+", _ => "unknown" };
        if ((machine == 0x8664 && format != "PE32+") || (machine == 0x14c && format != "PE32")) throw new InspectionRefusal("machine_optional_header_conflict");
        string kind = (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0 ? "dll" :
            (headers.CoffHeader.Characteristics & Characteristics.ExecutableImage) != 0 ? "executable_image" : "other_image";
        var exports = Exports(headers);
        var versions = ProductVersions(headers);
        return new(architecture, "0x" + machine.ToString("x4", CultureInfo.InvariantCulture), format, kind, exports, versions, Managed(pe));
    }
    long Offset(PEHeaders h, uint rva, int length)
    {
        if (length < 0) throw new InspectionRefusal("invalid_rva");
        foreach (var section in h.SectionHeaders)
        {
            long delta = (long)rva - section.VirtualAddress;
            if (delta >= 0 && delta <= (long)section.SizeOfRawData - length)
            {
                long result = checked((long)section.PointerToRawData + delta);
                if (result < 0 || result > file.Length - length) break;
                return result;
            }
        }
        throw new InspectionRefusal("rva_outside_backed_section");
    }
    byte[] Rva(PEHeaders h, uint rva, int count) => Read(Offset(h, rva, count), count);
    string ExportName(PEHeaders h, uint rva)
    {
        long offset = Offset(h, rva, 1);
        int available = (int)Math.Min(256, file.Length - offset);
        foreach (var section in h.SectionHeaders)
            if (offset >= section.PointerToRawData && offset < (long)section.PointerToRawData + section.SizeOfRawData)
                available = Math.Min(available, checked((int)((long)section.PointerToRawData + section.SizeOfRawData - offset)));
        var bytes = Read(offset, available);int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0 || bytes.Take(end).Any(b => b > 127)) throw new InspectionRefusal("export_name_bound_or_encoding");
        return Encoding.ASCII.GetString(bytes, 0, end);
    }
    string[] Exports(PEHeaders h)
    {
        var directory = h.PEHeader!.ExportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 && directory.Size == 0) return [];
        if (directory.Size < 40) throw new InspectionRefusal("export_directory_truncated");
        var table = Rva(h, checked((uint)directory.RelativeVirtualAddress), 40);
        uint count = U32(table, 24), functionCount = U32(table, 20);
        if (count > 16384 || functionCount > 65536) throw new InspectionRefusal("export_name_count_exceeded");
        if(count == 0)return [];
        var names = Rva(h, U32(table, 32), checked((int)count * 4));
        var ordinals = Rva(h, U32(table, 36), checked((int)count * 2));
        var functions = Rva(h, U32(table, 28), checked((int)functionCount * 4));
        var retained = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; ++i)
        {
            budget.Check();string name = ExportName(h, U32(names, i * 4));
            int ordinal=U16(ordinals,i*2);
            if(ordinal>=functionCount)throw new InspectionRefusal("export_ordinal_out_of_range");
            uint address=U32(functions,ordinal*4);
            if(address==0)throw new InspectionRefusal("export_function_address_missing");
            // Exported data may legitimately occupy a zero-filled virtual section.
            // The table address must belong to the image; retained API markers also
            // require a file-backed address, without claiming callable code.
            bool inImage = h.SectionHeaders.Any(section => (long)address >= section.VirtualAddress &&
                (long)address - section.VirtualAddress < Math.Max(section.VirtualSize, section.SizeOfRawData) &&
                address < (uint)h.PEHeader!.SizeOfImage);
            if (!inImage) throw new InspectionRefusal("export_address_outside_image");
            if (ExportMarkers.Contains(name)) { _ = Offset(h,address,1);retained.Add(name); }
        }
        return retained.ToArray(); // Named table entries are not callable-implementation proof.
    }
    string[] ProductVersions(PEHeaders h)
    {
        var resource = h.PEHeader!.ResourceTableDirectory;
        if (resource.RelativeVirtualAddress == 0 && resource.Size == 0) return [];
        if (resource.RelativeVirtualAddress <= 0 || resource.Size < 16) throw new InspectionRefusal("resource_directory_invalid");
        byte[] Relative(uint offset, int size)
        {
            if ((long)offset + size > resource.Size) throw new InspectionRefusal("resource_offset_out_of_range");
            return Rva(h, checked((uint)resource.RelativeVirtualAddress + offset), size);
        }
        List<(uint Name, uint Next)> Directory(uint at)
        {
            var header = Relative(at, 16);int count = U16(header, 12) + U16(header, 14);
            if (count > 64) throw new InspectionRefusal("resource_directory_entry_bound");
            var entries = Relative(checked(at + 16), count * 8);var result = new List<(uint, uint)>();
            for (int i = 0; i < count; ++i) result.Add((U32(entries, i * 8), U32(entries, i * 8 + 4)));
            return result;
        }
        uint Child(uint value)
        {if ((value & 0x80000000) == 0) throw new InspectionRefusal("resource_directory_shape");return value & 0x7fffffff;}
        var versionEntries = Directory(0).Where(e => e.Name == 16).ToArray();
        if (versionEntries.Length > 1) throw new InspectionRefusal("duplicate_version_resource_type");
        var versions = new SortedSet<string>(StringComparer.Ordinal);int blobs = 0;
        foreach (var type in versionEntries) foreach (var name in Directory(Child(type.Next))) foreach (var language in Directory(Child(name.Next)))
        {
            if (++blobs > 4 || (language.Next & 0x80000000) != 0) throw new InspectionRefusal("version_resource_count_or_shape");
            var entry = Relative(language.Next, 16);uint size = U32(entry, 4);
            if (size < 6 || size > 65536) throw new InspectionRefusal("version_resource_size_bound");
            var bytes = Rva(h, U32(entry, 0), checked((int)size));
            int block = U16(bytes, 0), valueLength = U16(bytes, 2), cursor = 6;
            if (block > bytes.Length || block < 6) throw new InspectionRefusal("version_block_invalid");
            while (cursor + 2 <= block && U16(bytes, cursor) != 0) cursor += 2;
            if (cursor + 2 > block || Encoding.Unicode.GetString(bytes, 6, cursor - 6) != "VS_VERSION_INFO") throw new InspectionRefusal("version_key_invalid");
            cursor = (cursor + 2 + 3) & ~3;
            if (U16(bytes,4)!=0 || valueLength < 52 || cursor > block - 52 || valueLength>block-cursor || U32(bytes, cursor) != 0xfeef04bd) throw new InspectionRefusal("fixed_version_info_invalid");
            uint ms = U32(bytes, cursor + 16), ls = U32(bytes, cursor + 20);
            versions.Add(string.Join('.', new[] { ms >> 16, ms & 65535, ls >> 16, ls & 65535 }.Select(v => v.ToString(CultureInfo.InvariantCulture))));
        }
        return versions.ToArray();
    }
    ManagedFileFacts? Managed(PEReader pe)
    {
        if (!pe.HasMetadata) return null;
        if (pe.PEHeaders.CorHeader == null || pe.PEHeaders.CorHeader.MetadataDirectory.Size > 16777216) throw new InspectionRefusal("managed_metadata_size_bound");
        budget.Check(pe.PEHeaders.CorHeader.MetadataDirectory.Size);
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly || reader.GetHeapSize(HeapIndex.String) > 1048576 || reader.AssemblyReferences.Count > 512) throw new InspectionRefusal("managed_metadata_shape_or_count_bound");
        string Text(StringHandle handle)
        {string s = reader.GetString(handle);if (s.Length > 256 || s.Any(char.IsControl)) throw new InspectionRefusal("metadata_string_bound_or_encoding");return s;}
        var assembly = reader.GetAssemblyDefinition();string identity = Text(assembly.Name);
        var references = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.AssemblyReferences)
        {budget.Check();var reference = reader.GetAssemblyReference(handle);string name = Text(reference.Name);if (PipelineName(name)) references.Add(name + ", Version=" + reference.Version);}
        var versions = new SortedSet<string>(StringComparer.Ordinal);int attributes = 0;
        foreach (var handle in assembly.GetCustomAttributes())
        {
            if (++attributes > 512) throw new InspectionRefusal("managed_attribute_count_bound");
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (Text(type.Namespace) != "System.Reflection" || Text(type.Name) is not ("AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute")) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.Length > 1024 || blob.ReadUInt16() != 1) throw new InspectionRefusal("managed_version_attribute_invalid");
            string? value = blob.ReadSerializedString();
            if (value == null || value.Length > 256 || value.Any(char.IsControl)) throw new InspectionRefusal("managed_version_string_invalid");
            versions.Add(value);
        }
        var flags = pe.PEHeaders.CorHeader.Flags;
        return new(identity, assembly.Version.ToString(), reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D"),
            flags.HasFlag(CorFlags.ILOnly), flags.HasFlag(CorFlags.Requires32Bit), flags.HasFlag(CorFlags.Prefers32Bit), references.ToArray(), versions.ToArray());
    }
}

internal sealed class PlayerInspection
{
    readonly InspectionBudget budget = new();
    readonly List<ArtifactFacts> artifacts = [];
    readonly List<string> issues = [];
    readonly string executable, root;
    bool discoveryComplete = true;
    public PlayerInspection(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length > 4096) throw new InspectionRefusal("absolute_game_executable_required");
        executable = Path.GetFullPath(path);root = Path.GetDirectoryName(executable) ?? throw new InspectionRefusal("installation_root_missing");
    }
    static void PathCheck(string path)
    {
        var parents = new List<string>();for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p)) parents.Add(p);
        for (int i = parents.Count - 1; i >= 0; --i)
            if ((File.GetAttributes(parents[i]) & FileAttributes.ReparsePoint) != 0) throw new InspectionRefusal("reparse_path_refused");
    }
    string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    ArtifactFacts Inspect(string relative, string role, string format = "pe")
    {
        if (budget.Files == InspectionBudget.MaximumFiles) throw new InspectionRefusal("artifact_count_exceeded");
        ++budget.Files;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal)) throw new InspectionRefusal("discovery_path_escape");
        long? length = null;string? hash = null;
        try
        {
            budget.Check();PathCheck(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) return Add(new(Relative(path), role, "malformed", ReasonCode: "expected_regular_file"));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            length = stream.Length;
            if (length > InspectionBudget.MaximumFileBytes) return Add(new(Relative(path), role, "refused", length, ReasonCode: "artifact_size_exceeded"));
            var before = new FileInfo(path);long originalLength = before.Length;DateTime originalTime = before.LastWriteTimeUtc;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer = new byte[65536];
            while (stream.Position < stream.Length)
            {
                budget.Check();int count=(int)Math.Min(buffer.Length,Math.Min(stream.Length-stream.Position,budget.Remaining));
                if(count==0)throw new InspectionRefusal("total_read_budget_exceeded");
                int read=stream.Read(buffer,0,count);if(read==0)throw new EndOfStreamException();
                budget.Check(read);sha.AppendData(buffer.AsSpan(0,read));
            }
            hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            var parser = new PlayerFileReader(stream, budget);PeFileFacts? pe = null;int? metadata = null;
            if (format == "pe") pe = parser.Pe();else if (format == "il2cpp") metadata = parser.Il2CppHeader();
            var after = new FileInfo(path);
            if (stream.Length != originalLength || after.Length != originalLength || after.LastWriteTimeUtc != originalTime) throw new InspectionRefusal("file_changed_during_inspection");
            return Add(new(Relative(path), role, "observed", length, hash, pe, metadata));
        }
        catch (FileNotFoundException) {return Add(new(Relative(path), role, "absent"));}
        catch (DirectoryNotFoundException) {return Add(new(Relative(path), role, "absent"));}
        catch (UnauthorizedAccessException) {return Add(new(Relative(path), role, "inaccessible", length, hash, ReasonCode: "access_denied"));}
        catch (InspectionRefusal error) when (error.Code is not ("time_budget_exceeded" or "total_read_budget_exceeded")) {return Add(new(Relative(path), role, "refused", length, hash, ReasonCode: error.Code));}
        catch (Exception error) when (error is BadImageFormatException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException) {return Add(new(Relative(path), role, "malformed", length, hash, ReasonCode: "malformed_pe_or_metadata"));}
        catch (IOException) {return Add(new(Relative(path), role, "inaccessible", length, hash, ReasonCode: "io_failure"));}
    }
    ArtifactFacts Add(ArtifactFacts value) {artifacts.Add(value);return value;}
    string[] Entries(string directory)
    {
        try
        {
            budget.Check();PathCheck(directory);var entries = new List<string>();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                budget.Check();if (entries.Count == InspectionBudget.MaximumDirectoryEntries) throw new InspectionRefusal("directory_entry_count_exceeded");entries.Add(path);
            }
            return entries.Order(StringComparer.Ordinal).ToArray();
        }
        catch (FileNotFoundException) {return [];}
        catch (DirectoryNotFoundException) {return [];}
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InspectionRefusal)
        {discoveryComplete = false;issues.Add(error is InspectionRefusal r ? r.Code : error is UnauthorizedAccessException ? "directory_access_denied" : "directory_io_failure");return [];}
    }
    public (object Report, int Exit) Run()
    {
        ArtifactFacts? selected = null;string[] dataCandidates = [];string? data = null;
        try
        {
            selected = Inspect(Path.GetFileName(executable), "selected_executable");
            if (selected.State == "observed" && selected.Pe != null)
            {
                var entries = Entries(root);
                dataCandidates = entries.Where(p => Path.GetFileName(p).EndsWith("_Data", StringComparison.OrdinalIgnoreCase)).Select(Relative).ToArray();
                string expected = Path.GetFileNameWithoutExtension(executable) + "_Data";
                var matching = dataCandidates.Where(p => string.Equals(p, expected, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 1) data = matching[0];else if (matching.Length > 1) {discoveryComplete = false;issues.Add("ambiguous_matching_data_directories");}
                else issues.Add("matching_data_directory_not_observed");
                Inspect("UnityPlayer.dll", "unity_player_module");
                Inspect("GameAssembly.dll", "il2cpp_native_candidate");
                foreach (string path in new[] { "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", "MonoBleedingEdge/EmbedRuntime/mono-2.0-sgen.dll", "Mono/EmbedRuntime/mono.dll", "Mono/mono.dll" }) Inspect(path, "mono_runtime_candidate");
                foreach (string path in new[] { "winhttp.dll", "version.dll", "dxgi.dll", "d3d11.dll", "winmm.dll", "UnityVRRenderBridge.dll", "BepInEx/core/BepInEx.dll", "BepInEx/core/BepInEx.Preloader.dll", "BepInEx/plugins/UnityVRInjector/UnityVRInjector.dll", "MelonLoader/MelonLoader.dll" }) Inspect(path, "loader_or_deployment_inventory");
                foreach (string path in new[] { "doorstop_config.ini", "BepInEx/config/dev.codex.unityvrinjector.cfg" }) Inspect(path, "loader_or_deployment_inventory", "bytes");
                if (data != null)
                {
                    Inspect(data + "/il2cpp_data/Metadata/global-metadata.dat", "il2cpp_metadata_candidate", "il2cpp");
                    foreach (string path in new[] { "/Mono/mono.dll", "/Mono/EmbedRuntime/mono.dll", "/MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", "/MonoBleedingEdge/EmbedRuntime/mono-2.0-sgen.dll" })
                        Inspect(data + path, "mono_runtime_candidate");
                    foreach (string path in Entries(Path.Combine(root, data, "Managed")))
                        if (string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase)) Inspect(Relative(path), "managed_inventory");
                }
            }
        }
        catch (InspectionRefusal error) {discoveryComplete = false;issues.Add(error.Code);}
        bool engine = artifacts.Any(a => a.State == "observed" && (a.Pe?.Managed?.AssemblyName is "UnityEngine" or "UnityEngine.CoreModule" || a.Role == "unity_player_module" && a.Pe?.RetainedExportNames.Contains("UnityMain") == true));
        bool mono = artifacts.Any(a => a.Role == "mono_runtime_candidate" && a.State == "observed" && a.Pe != null && a.Pe.RetainedExportNames.Contains("mono_get_root_domain") && a.Pe.RetainedExportNames.Contains("mono_runtime_invoke"));
        bool il2cpp = artifacts.Any(a => a.Role == "il2cpp_native_candidate" && a.State == "observed" && a.Pe != null && a.Pe.RetainedExportNames.Contains("il2cpp_domain_get") && a.Pe.RetainedExportNames.Contains("il2cpp_runtime_invoke")) && artifacts.Any(a => a.Il2CppMetadataVersion.HasValue);
        var versions = artifacts.Where(a => a.State == "observed" && (a.Role == "unity_player_module" && a.Pe?.RetainedExportNames.Contains("UnityMain")==true || a.Pe?.Managed?.AssemblyName is "UnityEngine" or "UnityEngine.CoreModule"))
            .SelectMany(a => (a.Pe?.FixedProductVersions ?? []).Select(v => VersionEvidence(a.Path, "pe_fixed_product_version", v)).Concat((a.Pe?.Managed?.VersionAttributes ?? []).Select(v => VersionEvidence(a.Path, "managed_version_attribute", v)))).ToArray();
        var families = versions.Select(v => v.Family).Where(v => v != null).Distinct(StringComparer.Ordinal).ToArray();
        bool versionConflict=families.Length>1 || versions.Where(v=>v.ReleaseTag!=null).Select(v=>v.Family+":"+v.ReleaseTag).Distinct(StringComparer.Ordinal).Count()>1;
        bool architectureConflict=selected?.Pe!=null && artifacts.Any(a=>a.State=="observed" && a.Pe!=null && a.Pe.Managed==null && (a.Role is "unity_player_module" or "mono_runtime_candidate" or "il2cpp_native_candidate") && a.Pe.MachineCode!=selected.Pe.MachineCode);
        var pipeline = artifacts.Where(a => a.State == "observed" && a.Pe?.Managed != null).Select(a => new { Artifact = a.Path, Identity = a.Pe!.Managed!.AssemblyName, a.Pe.Managed.AssemblyVersion, References = a.Pe.Managed.PipelineReferences })
            .Where(a => PlayerFileReader.PipelineName(a.Identity) || a.References.Length != 0).ToArray();
        var loaders = artifacts.Where(a => a.Role == "loader_or_deployment_inventory" && a.State != "absent").Select(a => new { a.Path, a.State, a.Sha256, ManagedIdentity = a.Pe?.Managed?.AssemblyName }).ToArray();
        bool conflict = loaders.Any(a => a.ManagedIdentity?.StartsWith("BepInEx", StringComparison.Ordinal) == true) && loaders.Any(a => a.ManagedIdentity == "MelonLoader");
        bool mainOk = selected?.State == "observed" && selected.Pe != null;
        bool complete = discoveryComplete && artifacts.All(a => a.State is "observed" or "absent");
        var report = new
        {
            SchemaVersion = 1, Command = "inspect", State = mainOk && discoveryComplete ? "completed" : "refused", InstallationRoot = root,
            RuntimeSupportEstablished = false, TestedCompatibilityEstablished = false, WritesPerformed = false,
            SelectedExecutable = selected, DiscoveryComplete = discoveryComplete, EvidenceComplete = complete,
            Scope = new { DataDirectory = data, AdjacentDataCandidates = dataCandidates, RecursiveSearch = false, MaximumArtifacts = InspectionBudget.MaximumFiles, MaximumDirectoryEntries = InspectionBudget.MaximumDirectoryEntries, MaximumArtifactBytes = InspectionBudget.MaximumFileBytes, MaximumTotalReadBytes = InspectionBudget.MaximumTotalBytes, MaximumOutputBytes = InspectionBudget.MaximumOutput, ElapsedBudgetSeconds = 10, FilesAttempted = budget.Files, BytesCharged = budget.Bytes },
            Unity = new { Interpretation = !discoveryComplete ? "unknown_incomplete_inventory" : engine ? "unity_content_candidate" : "unconfirmed", RuntimeUnityVersion="unobserved", VersionState = versionConflict ? "conflicting_version_evidence" : families.Length == 1 ? "version_family_evidence" : "unknown", VersionFamily = !versionConflict&&families.Length == 1 ? families[0] : null, VersionEvidence = versions },
            Backend = new { Interpretation = !discoveryComplete ? "unknown_incomplete_inventory" : architectureConflict ? "ambiguous_architecture_evidence" : mono && il2cpp ? "ambiguous_mono_and_il2cpp_content" : mono ? "mono_content_candidate" : il2cpp ? "il2cpp_content_candidate" : "unknown", ArchitectureConflict=architectureConflict, MonoExportEvidence = mono, Il2CppExportAndHeaderEvidence = il2cpp, CallableImplementations = "unobserved" },
            Pipeline = new { ActivePipeline = "unknown", RetainedArtifacts = pipeline, AbsenceProvesBuiltIn = false },
            Loader = new { Interpretation = conflict ? "potential_loader_conflict" : loaders.Length != 0 ? "present_unverified" : "no_known_artifacts_observed", Compatibility = "unknown", Files = loaders },
            ActiveGraphicsApi = "unknown", VrRuntimeState = "unobserved", Artifacts = artifacts, Issues = issues.Distinct().Order(StringComparer.Ordinal).ToArray(),
            StartupRoute=selected?.Pe?.Managed?.IlOnly==true?"managed_launcher_requires_separate_validation":selected?.Pe?.Machine!="x64"?"not_current_x64_startup_route":"unvalidated_x64_file_candidate",
            NextSteps = new[] { "Use file evidence to select a separately validated adapter; no supported-player verdict is made.", "Resolve inaccessible/refused/conflicting observations before relying on a complete inventory.", "Validate exact engine/backend/pipeline, graphics, loader ownership and recurring callbacks in a separately authorized runtime probe." },
            Limits = new[] { "Artifact and export/metadata presence does not prove active use, safe loader reuse, retained callable code or compatibility.", "Missing inspected files do not prove an implementation was stripped. Native product versions are build evidence, not an inferred Unity release tag.", "Hash and parsing share one opened handle; length/time checks detect some changes, not hostile filesystem races. The elapsed budget is checked between I/O calls and cannot interrupt a blocked OS read." }
        };
        return (report, mainOk && discoveryComplete ? 0 : 2);
    }
    static EvidenceVersion VersionEvidence(string artifact,string source,string text)
    {
        var match=Regex.Match(text,@"^([0-9]+\.[0-9]+\.[0-9]+)([abfp][0-9]+)?(?:\.[0-9]+)?(?:\+[^\r\n]*)?$",RegexOptions.CultureInvariant|RegexOptions.NonBacktracking);
        bool usable = match.Success && match.Groups[1].Value != "0.0.0";
        return new(artifact,source,text,usable?match.Groups[1].Value:null,usable&&match.Groups[2].Success?match.Groups[2].Value:null);
    }
}

internal static class PlayerInspectionCli
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    public static int Run(string[] args)
    {
        object report;int exit;string format="yaml";
        try {var options = new Options(args);options.Allow("game-exe","format");format=options.Has("format")?options.Required("format"):"yaml";if(format is not ("yaml" or "json"))throw new InspectionRefusal("unsupported_output_format");(report, exit) = new PlayerInspection(options.Required("game-exe")).Run();}
        catch (Exception error) when (error is ArgumentException or InspectionRefusal or IOException or UnauthorizedAccessException)
        {report = new { SchemaVersion = 1, Command = "inspect", State = "refused", ReasonCode = error is InspectionRefusal r ? r.Code : "invalid_inspection_arguments_or_path", RuntimeSupportEstablished = false, TestedCompatibilityEstablished = false, WritesPerformed = false };exit = 2;}
        string Encode(object value)
        {
            string json=JsonSerializer.Serialize(value,Json);if(format=="json")return json;
            using var document=JsonDocument.Parse(json);var output=new StringBuilder();Yaml(document.RootElement,output,0);return output.ToString().TrimEnd('\n');
        }
        string text = Encode(report);
        if (Encoding.UTF8.GetByteCount(text) > InspectionBudget.MaximumOutput)
        {text = Encode(new { SchemaVersion = 1, Command = "inspect", State = "refused", ReasonCode = "output_budget_exceeded", RuntimeSupportEstablished = false, TestedCompatibilityEstablished = false, WritesPerformed = false });exit = 2;}
        Console.WriteLine(text);return exit;
    }
    static void Yaml(JsonElement element,StringBuilder output,int indent)
    {
        string pad=new(' ',indent);
        bool Scalar(JsonElement value)=>value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) || value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()==0 || value.ValueKind==JsonValueKind.Object&&!value.EnumerateObject().Any();
        if(element.ValueKind==JsonValueKind.Object)
            foreach(var property in element.EnumerateObject())
            {
                output.Append(pad).Append(property.Name).Append(':');
                if(Scalar(property.Value))output.Append(' ').Append(property.Value.GetRawText()).Append('\n');
                else {output.Append('\n');Yaml(property.Value,output,indent+2);}
            }
        else if(element.ValueKind==JsonValueKind.Array)
            foreach(var item in element.EnumerateArray())
            {
                output.Append(pad).Append('-');
                if(Scalar(item))output.Append(' ').Append(item.GetRawText()).Append('\n');
                else {output.Append('\n');Yaml(item,output,indent+2);}
            }
    }
}
