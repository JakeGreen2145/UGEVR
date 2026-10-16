# Offline game profiles

The controller's `profile` commands validate, resolve and edit declarative profiles without starting Unity, SteamVR or Frame Testbench. This first module supports `Tracking.UnitsPerMeter` as a finite positive single-precision number and `Tracking.RetainGamePitchAndRoll` as a Boolean. Their built-in defaults are `1` and `false`. The typed descriptors, validation and serializers are shared by these commands.

These commands do not apply settings to a running or launching game. Every result reports `runtime_applied: "false"`. Startup deployment and revisioned live application remain separate work. Diagnostic activation flags, bindings, selectors, executable plugins/scripts and portable packages are unsupported here; unknown settings fail visibly.

## Resolve and edit

```powershell
scripts/UGEVR.ps1 profile resolve --game-id steam:383870
scripts/UGEVR.ps1 profile save --game-id steam:383870 --scope personal --expected-revision 0 --setting Tracking.UnitsPerMeter --value 1.25
scripts/UGEVR.ps1 profile validate --game-id steam:383870
scripts/UGEVR.ps1 profile reset --game-id steam:383870 --scope personal --expected-revision 1 --setting Tracking.UnitsPerMeter
```

The default central store is `%LOCALAPPDATA%/UGEVR/profiles`. `--storage-root PATH` selects a separate explicit store. Game IDs are canonical `steam:APP_ID` or `assigned:GUID`, with a positive decimal application ID or a nonzero lowercase GUID. A display name, executable basename or absolute machine path cannot identify a game. The game directory uses the SHA-256 of that explicit ID, keeping same-named games separate and excluding path syntax from directory selection.

Resolution applies built-in defaults, global defaults, the shared game profile, personal game overrides, then a selected `--session-config PATH`. Each value reports its winning scope. Session data is read without persistence. An explicitly selected missing session file fails. The read result includes global/personal revisions and the shared-profile directory. Read-only commands do not create the store.

Only `global` and `personal` are editable scopes. Save changes one setting; reset removes one setting or, when `--setting` is omitted, all overrides in that scope. Removing an override reveals the next lower value. Shared profiles remain separate and read-only through this CLI. Reset does not reverse game calls or change another scope.

Writers require the current `--expected-revision`, take a per-file exclusive lease, validate the whole prior layer, stage a complete UTF-8 replacement and atomically replace the selected file. A successful edit increments that scope's revision. Stale revisions, invalid data and replacement failures preserve the prior config. Failed replacement removes its temporary staging file. This is an offline persistence contract; it is not a game-applied revision acknowledgment or a guarantee against machine power loss or hostile filesystem races.

## File formats and identity

Settings use INI-style section/key syntax in `config.txt`, preserving the current UGEVR names. A generated file looks like:

```ini
# UGEVR profile revision: 1
[Tracking]
RetainGamePitchAndRoll=false
UnitsPerMeter=1.25
```

The revision comment is optional for authored files and defaults to zero. Duplicate settings, duplicate sections/revision headers, malformed values, unknown sections/settings and files exceeding 64 KiB refuse. Inline comments inside values are unsupported. UEVR's familiar INI syntax is retained; Unreal-specific key names and semantics have no implicit conversion or drop-in compatibility.

Global values live at `global/config.txt`; personal values live at `games/<identity-hash>/personal/config.txt`. An authored shared profile uses `games/<identity-hash>/shared/config.txt` and new YAML metadata in `shared/profile.yaml`:

```yaml
schema_version: "1"
profile_id: "f683aa26-63e1-42ee-bc27-d6c3b41bc61d"
game_id: "steam:383870"
build_sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
```

The fingerprint above is an illustrative value. `build_sha256` is optional. `profile validate|resolve --build-sha256 ACTUAL_HASH` compares an explicitly supplied executable fingerprint with a declared constraint. A mismatch requires revalidation and refuses resolution. A match reports `fingerprint_match_not_runtime_validation`; it proves neither tested compatibility nor the identity of a running game. A missing fingerprint reports `unverified`. The caller is responsible for supplying the intended build hash; this offline command does not inspect a game process.

Metadata is a bounded flat YAML scalar mapping. Future schema versions, unknown fields, malformed profile/build IDs, duplicate keys and a different game identity refuse. A shared directory without metadata also refuses. Reparse points in inspected or edited profile paths refuse. User-selected session paths are explicit read inputs; they never change the destination of a saved global/personal layer.

## Automated validation

```powershell
dotnet build controller/UGEVR.Controller -c Release
python tests/test_profiles_cli.py
```

The checks invoke the actual CLI in temporary stores. They cover all five layers, per-value provenance, same-named game identities, round-trip, reset, concurrent snapshots, stale edits, 64-bit revisions, schema/identity/build mismatches, invalid and unknown settings, duplicate/oversize data and prohibited write scopes. A Windows file handle deliberately denies replacement to verify preservation of the old profile and staging cleanup. Another fixture denies file creation only in its temporary settings directory to exercise staging failure, then restores that directory's exact original ACL. No VR SDK or proprietary game assembly is used.
