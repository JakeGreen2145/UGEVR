# Installed-player inspection

`inspect` reads the explicitly selected Windows executable and known adjacent Unity artifacts. It creates no session, executes no target code, initializes no VR SDK and does not require Frame Testbench. It writes no cache, configuration or repair. Existing launch and configure behavior does not depend on this report.

```powershell
scripts/UGEVR.ps1 inspect --game-exe 'X:\Games\Example\Example.exe'
scripts/UGEVR.ps1 inspect --game-exe 'X:\Games\Example\Example.exe' --format json
```

The default is nested YAML 1.2 with JSON-quoted strings. `--format json` serializes the same report. Both use integer `schema_version: 1`. Output is local and includes the installation root and relative artifact paths. Redirect it to a file explicitly when retention is needed.

## Evidence and interpretation

`selected_executable` records SHA-256, size, PE machine, optional-header format and image kind. Managed images also expose metadata assembly identity, version, MVID and COR architecture flags. These are file observations. They do not establish the architecture of a future running process. A managed launcher requires separate validation; a non-x64 image is outside the current x64 startup route.

Every artifact has a role and a state: `observed`, `absent`, `inaccessible`, `malformed` or `refused`. Failures have stable reason codes. An unreadable file is not reported absent. Hashing and parsing share one opened read-only handle with `FileShare.Read`. Length and modification-time checks detect some concurrent changes. This does not prevent hostile filesystem replacement races or prove that the installation was observed atomically.

The engine interpretation is a candidate only when managed metadata identifies `UnityEngine` or `UnityEngine.CoreModule`, or an adjacent `UnityPlayer.dll` has a structurally valid `UnityMain` export entry. Version evidence retains its artifact, source and original value. Sources are the fixed product version from a PE version resource and file/informational attributes in an identified engine assembly. Numeric product versions do not imply Unity release tags. Placeholder `0.0.0` versions retain their evidence but do not select a version family. Disagreeing families or release tags remain conflicting. This command does not scan arbitrary binary strings or parse Unity serialized assets, so exact engine versions can remain unknown.

Mono evidence requires named `mono_get_root_domain` and `mono_runtime_invoke` export entries in an inspected runtime candidate. IL2CPP evidence requires `il2cpp_domain_get` and `il2cpp_runtime_invoke` export entries plus the metadata magic and bounded header version. A filename alone supplies neither conclusion. The export entry must have a valid ordinal and image address, and retained API markers require file-backed addresses. Exported data may use virtual zero-fill. Export presence never proves a callable implementation or active backend. The metadata header check does not validate all IL2CPP tables. Simultaneous Mono and IL2CPP evidence and native architecture disagreement remain ambiguous.

Pipeline output retains identified Core/URP/HDRP runtime assemblies and references to those assembly identities. It does not inspect every retained type or establish the active render pipeline. Missing package evidence does not prove Built-in. Graphics API, runtime Unity version, VR state, callable implementations and adapter compatibility remain unobserved. `runtime_support_established`, `tested_compatibility_established` and `writes_performed` are always false.

Known loader files are inventoried and hashed. Managed BepInEx and MelonLoader identities together produce `potential_loader_conflict`. Presence never authorizes reuse or repair. Native proxy filenames do not identify their implementation. The command chooses no conflict remedy.

## Discovery and limits

Discovery uses the executable's parent and only its exact stem plus `_Data`, compared case-insensitively. Other adjacent `_Data` names are reported without selecting them. No drive search, process attachment or recursive tree traversal occurs. Reparse points in selected or discovered paths are refused before descending into them.

The fixed root candidates are `UnityPlayer.dll`, `GameAssembly.dll`, the Mono runtime names under `MonoBleedingEdge/EmbedRuntime`, `Mono/EmbedRuntime` and `Mono`, and these loader locations:

- `winhttp.dll`, `version.dll`, `dxgi.dll`, `d3d11.dll`, `winmm.dll`, `UnityVRRenderBridge.dll` and `doorstop_config.ini`.
- `BepInEx/core/BepInEx.dll`, `BepInEx/core/BepInEx.Preloader.dll`, `BepInEx/plugins/UnityVRInjector/UnityVRInjector.dll` and `BepInEx/config/dev.codex.unityvrinjector.cfg`.
- `MelonLoader/MelonLoader.dll`.

Within the selected Data directory, discovery checks `il2cpp_data/Metadata/global-metadata.dat`, Mono runtime names under `Mono`, `Mono/EmbedRuntime` and `MonoBleedingEdge/EmbedRuntime`, then top-level `Managed/*.dll`. It does not inspect plugins or serialized scene/resource files. Inventory completeness refers only to this documented set.

The report exposes these fixed bounds:

| Work | Limit |
| --- | --- |
| Artifact attempts, including absent fixed paths | 256 |
| Entries in each enumerated directory | 1,024 |
| File length | 128 MiB |
| Total charged hash and parser reads | 512 MiB |
| Encoded report | 512 KiB |
| Elapsed work checked between I/O calls | 10 seconds |
| PE sections / export names / version blobs | 96 / 16,384 / 4 |
| Managed metadata / string heap | 16 MiB / 1 MiB |

PE offsets, section backing, export ordinals, resource hierarchy, version blocks and metadata shapes are checked before interpretation. An OS read that blocks cannot be interrupted by the elapsed check. Artifact-count or directory-enumeration truncation sets `discovery_complete: false`; conclusions become incomplete-inventory unknowns. The command never treats the retained subset as the whole installation. Output overflow returns a small structured refusal rather than a truncated document.

Exit 0 means the selected executable parsed and bounded discovery finished. Optional malformed or inaccessible artifacts can still make `evidence_complete: false`, with their exact states retained. Exit 2 means invalid arguments, invalid selected executable, failed discovery or a global bound. Neither exit code means that injection works. Resolve inaccessible/refused/conflicting observations and perform separately authorized runtime validation before selecting an adapter.

## Automated checks

Build the normal controller and exercise the public CLI:

```powershell
dotnet build controller/UGEVR.Controller -c Release
python tests/test_inspect_cli.py
```

The fixtures generate data-only PE images and managed assemblies in temporary directories. They cover machine variants, invalid headers/offsets/ordinals/resources, size/count limits, backend decoys and conflicts, engine version disagreement, pipeline and loader evidence, YAML/JSON equivalence, read-only hashes and actual NTFS access-denied paths. Windows junction and ACL checks restore their temporary filesystem state. PyYAML is optional for the development format-equivalence check; shipping has no added dependency. Set `UGEVR_TEST_PWSH` to the PowerShell 7 executable for ACL fixtures when it is not at the bundled runtime path.