# Per-launch deployment

UGEVR's session controller can use `scripts/Deploy-Session.ps1` to deploy a prepared distribution and restore its owned changes after the selected game exits. The script requires PowerShell 7. It has no Frame Testbench dependency.

```powershell
./scripts/Deploy-Session.ps1 -Operation Inspect -GameRoot C:\Games\Example `
    -DistributionRoot C:\UGEVR\distribution -SessionDirectory C:\UGEVR\sessions\example

./scripts/Deploy-Session.ps1 -Operation Deploy -GameRoot C:\Games\Example `
    -DistributionRoot C:\UGEVR\distribution -SessionDirectory C:\UGEVR\sessions\example

./scripts/Deploy-Session.ps1 -Operation Restore -GameRoot C:\Games\Example `
    -SessionDirectory C:\UGEVR\sessions\example
```

Each operation returns a small YAML summary. Failure is a nonzero script exit with an error. `Inspect` does not mutate the target. `Deploy` requires a new session receipt. `Restore` is repeatable, reads the durable receipt and verifies that its game root matches the explicit argument. The controller must hold a per-game lease for the entire deployment/session lifetime and restore only after its identified game process exits. Optional `ExpectedGameProcessId` rejects a live selected process; this check alone is not a complete process-identity contract.

An existing hash-identical file is reused and never removed during restoration. Existing loader reuse also requires matching loader configuration and BepInEx core. Unknown or mismatched loader/configuration/files fail preflight. To select a remedy explicitly, pass exact relative paths with `ReplaceRelativePath`. The operation backs up those files and restores the original bytes. Selecting one path does not authorize every conflict. This initial fingerprint rule is conservative and does not recognize every otherwise compatible mod loader.

The script creates a YAML deployment receipt before mutating the game. It records original/deployed SHA-256 hashes, owned paths, backup paths, reserved temporary paths and created directories. Writes use a temporary file and replacement; backups and source copies are verified. If deployment fails, it attempts rollback and reports incomplete cleanup separately. A new controller invocation can use the same receipt after a crash.

During restore, each target is compared with its recorded original/deployed hash. Externally edited files and tampered backups are preserved with a conflict result. Only owned files and empty directories recorded by this deployment are removed. Path validation rejects escape, rooted paths, alternate streams and reparse points. Receipts use a restricted, versioned YAML subset emitted by this script; it does not parse arbitrary user-authored YAML. Keep receipts/backups outside the game and distribution directories.

Run `scripts/Test-SessionDeployment.ps1` for nine public-operation scenarios. The checks use temporary fixture directories and exercise fresh deployment/restoration, conflict selection, compatible loader reuse, external edits, backup tampering, locked-file rollback, process-exit guards/restart recovery, path confinement and junction rejection. They do not launch a Unity game or prove physical VR. This is the deployment foundation of [S1: implement reversible per-launch deployment receipts](https://github.com/JakeGreen2145/UGEVR/issues/26).
