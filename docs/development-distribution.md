# Development distribution

[S1: package the integrated development distribution with provenance](https://github.com/JakeGreen2145/UGEVR/issues/31) creates one experimental S1 distribution. Its physical gameplay status is unvalidated. Packaging success does not establish that a game works on a headset.

## Build once and package

Prepare the official BepInEx 5.4.23.5 Windows x64 release with `scripts/Fetch-Dependencies.ps1`. Packaging verifies the archive against its published release digest, `82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4`. The prepared BepInEx reference DLL must match that archive. The committed OpenVR revision remains pinned in `vendor/OpenVR-commit.txt`.

Run this from PowerShell 7 with the chosen Mono game's Managed directory. Supply a public build label rather than a private path for `ReferenceBuild`.

```powershell
./scripts/Package-DevelopmentDistribution.ps1 `
    -Destination ./dist/UGEVR-development-new `
    -GameManaged $env:UGEVR_GAME_MANAGED `
    -ReferenceBuild 'Firewatch Unity 2017.4.40f1 Mono'
```

The destination directory and its sibling ZIP must be absent. The operation builds the managed injector once against that reference set, builds the native bridge, and publishes the controller for Windows x64. It never recursively deletes or overwrites an existing package. A failed partial destination is retained for inspection; choose another destination for a retry. Temporary build staging directories are retained under `build/`.

The managed DLL and controller omit debugging paths and PDBs. Existing `Build-Prototype.ps1` behavior is retained; its added `-DistributionBuild` option selects those packaging build flags. `Package-Prototype.ps1` remains the older payload-only prototype command. Use the new command for a distribution containing the session controller and provenance.

The first baseline requires this one `UnityVRInjector.dll` and one native bridge to be used unchanged in both Firewatch and Outer Wilds. Separate per-game managed rebuilds do not satisfy that requirement.

## Layout and requirements

| Directory or file | Purpose |
| --- | --- |
| `payload/` | Game deployment files only: pinned loader, BepInEx core, configured `HideManagerGameObject = true`, one injector DLL, native bridge and OpenVR runtime DLL |
| `controller/` | Framework-dependent Windows x64 session controller |
| `scripts/Deploy-Session.ps1` | Independent reversible deployment operation |
| `distribution.yaml` | Version, source revision and dirty flag, build/dependency identities, source-file hashes and shipped-file hashes |
| `docs/`, `README.txt`, `THIRD_PARTY_NOTICES.md`, `vendor/` | Operating contracts, provenance and preserved BepInEx/OpenVR licenses and installed .NET host notices |

Normal operation needs Windows x64, the .NET 10 x64 runtime, PowerShell 7 accessible as `pwsh.exe`, and the Visual C++ runtime required by the native `/MD` build. Game injection currently needs a compatible Windows x64 Mono Unity build, D3D11 and a configured OpenVR runtime. The controller is framework-dependent; the package does not contain a .NET runtime. The game uses its own Mono runtime, not the controller's .NET runtime.

No Frame Testbench executable, dependency or code is included. It remains an external testing integration. Game assemblies, game executables/assets, generated interop, development captures, runtime settings, personal paths and PDBs are excluded. The package preserves dependency licenses and reports the current absence of a declared UGEVR project license rather than assigning one.

## Extracted session operation

The following examples use generic paths. Choose a fresh session directory outside the game directory. Run `status`, `disable`, `configure` and `stop` against that directory afterward.

```powershell
./controller/UGEVR.Controller.exe launch `
    --exe 'C:\Games\Example\Example.exe' `
    --game-root 'C:\Games\Example' `
    --session-dir 'C:\UGEVR\sessions\example-new' `
    --distribution-root "$PWD/payload" `
    --deployment-script "$PWD/scripts/Deploy-Session.ps1" `
    --game-arg '-force-d3d11'

./controller/UGEVR.Controller.exe status --session-dir 'C:\UGEVR\sessions\example-new'
./controller/UGEVR.Controller.exe disable --session-dir 'C:\UGEVR\sessions\example-new'
./controller/UGEVR.Controller.exe stop --session-dir 'C:\UGEVR\sessions\example-new'
```

Pass `payload/` to the deployment operation, not the package root. The latter also contains tools and documentation. Deployment resolves conflicts only when explicitly selected, records ownership and restores unchanged owned files after the selected process exits. A deliberate stop does not silently terminate unrelated processes. Follow the controller and deployment contracts for incomplete cleanup and explicit owned-process termination.

## Manifest and checks

The YAML manifest has a versioned, emitted subset. `source_revision` identifies the Git commit and `source_dirty` reports modified/untracked source at packaging time. `source_files` records relative build-input names and hashes, so a development build is not falsely described as a clean commit. `files` hashes every shipped file except `distribution.yaml` itself. The manifest states that exclusion; the packaging result supplies the sibling ZIP hash. Hashes identify files, not a trusted signature.

Run:

```powershell
./scripts/Test-DevelopmentDistribution.ps1 -Archive ./dist/UGEVR-development-new.zip
```

The verification extracts to a fresh test directory, checks declared hashes and file omissions, starts the extracted controller CLI, and exercises deployment/restoration through the extracted script with a controlled child executable. The child represents a live injector handshake and does not launch Unity, SteamVR, a game or a headset. It verifies that no developer-only source path is needed by the extracted CLI or deployment operation.

The checks keep functional package behavior separate from the physical gate. Native/compositor/GPU behavior, actual game lifetime, headset stream readiness and 3 to 5 minutes of physical gameplay remain required evidence.
