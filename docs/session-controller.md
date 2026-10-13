# Session controller

The standalone Windows controller requires the .NET 10 runtime and PowerShell 7 on PATH. It has no Unity or Frame Testbench dependency. The frontend and external test tools can call the same CLI. Frame Testbench remains in its own repository.

Build with `dotnet build controller/UGEVR.Controller -c Release`. Invoke `scripts/UGEVR.ps1` or run the compiled executable directly. An extracted distribution places the controller under `controller/` and deployment scripts under `scripts/`. Only its `payload/` directory belongs in `--distribution-root`.

```powershell
scripts/UGEVR.ps1 launch --exe 'X:\Games\Example\Example.exe' --game-root 'X:\Games\Example' --distribution-root 'C:\UGEVR\payload' --deployment-script 'C:\UGEVR\scripts\Deploy-Session.ps1' --session-dir 'C:\UGEVR\sessions\example-001' --game-arg '-force-d3d11' --timeout-seconds 30
scripts/UGEVR.ps1 status --session-dir 'C:\UGEVR\sessions\example-001'
scripts/UGEVR.ps1 configure --session-dir 'C:\UGEVR\sessions\example-001' --enabled true --recenter yaw
scripts/UGEVR.ps1 disable --session-dir 'C:\UGEVR\sessions\example-001'
scripts/UGEVR.ps1 stop --session-dir 'C:\UGEVR\sessions\example-001'
```

Each launch requires an empty session directory outside the game and distribution roots. A hidden supervisor holds a lease on the game directory through deployment, process lifetime and restoration. Launch returns when the actual injector reports engine and runtime readiness. The supervisor continues after the CLI exits. An explicit `--prepared true` development mode skips deployment and requires an already prepared executable. It cannot be mixed with deployment options.

The first version supports direct executable startup. Provider launch, late attachment, discovery and a graphical frontend remain unavailable. `--steam-app-id 753640` sets `SteamAppId` and `SteamGameId` in the launched child only. This can supply application identity to a Steam-dependent direct launch. It does not implement Steam provider launch.

Repeat `--game-arg VALUE` to pass exact child arguments. Repeat `--replace-path RELATIVE_PATH` to select exact deployment conflicts. The controller passes selections as a PowerShell array without interpreting their contents as code. Unselected conflicts fail before game launch. Deployment receipts and backups remain in the session directory for recovery after controller or machine failure. Automatic restart recovery is not implemented; inspect the receipt and use the deployment script's explicit Restore operation after verifying the game has exited.

The controller records the chosen executable and SHA-256, run GUID, PID and process creation ticks. It verifies PID and creation time on every status/command operation. An exited process invalidates readiness even if its last heartbeat file remains. The actual injector must produce a matching run and PID and a heartbeat no older than ten seconds. Process existence alone never sets engine, runtime or stream readiness. This version always reports stream readiness as unavailable; an external test tool must collect peer, returned-image and input evidence separately.

Disable requests the injector's own restoration/drain path and requires a revision acknowledgment. It does not unload the DLL. Recenter accepts `true`, `position`, `yaw`, `height` or `false`. A recenter request requires enabled rendering; the producer keeps it pending until it can apply it. The controller cannot substitute an acknowledgment for engine or GPU safety proof.

Stop requests disable first, then requests `CloseMainWindow` on the exact owned process. A console fixture or a game without a closeable window may remain running. The command reports pending when its deadline expires. `--force true` explicitly permits termination of that process. It does not kill child process trees, original unrelated processes or SteamVR. Stop waits for the supervisor's deployment cleanup, so game exit alone is not successful cleanup. Cleanup failures return a failure and preserve receipts. Monitoring failure stays visible while a live game runs and still permits an explicit owned stop.

Ctrl+C cancels the current CLI operation. Cancelling launch records a graceful stop request without forcing termination. `--cancel-after-seconds N` provides the same cancellation path with a bounded timer for automation. `--timeout-seconds N` accepts 1 through 300. Deployment operations have a separate 60-second limit. Process stdout and stderr are retained locally up to 65,536 characters each, and remaining output is drained without retention.

## External protocol

Every successful status or pending result is a flat YAML scalar mapping with `schema_version: "1"`. Fields include `run_id`, `executable`, `executable_sha256`, `process_id`, `process_start_utc_ticks`, `state`, `process_alive`, `engine_ready`, `runtime_ready`, `rendering_active`, `injector_status`, `cleanup_complete` and `deployment_restored`. The injector adds `unity_version`, `runtime`, `submitted_pairs`, `acknowledged_revision` and `injector_error`. Identity fields remain in stopped session records. Early argument errors return an `error` scalar without creating a session; later deployment failures retain the session identity.

The producer inherits `UGEVR_SESSION_ID` and `UGEVR_SESSION_DIRECTORY`. It atomically writes `injector-status.yaml` with the schema, run and PID, Unity version, readiness flags, runtime name, counters and `last_error`. It refreshes this file at least once per second even when disabled. Controller readers accept the protocol's plain scalars and single or JSON-style double quoted strings. The parser permits a bounded, flat mapping rather than arbitrary YAML aliases, tags, sequences or objects.

`command.txt` uses flat `key=value` settings: `run_id`, a positive increasing `revision`, `enabled`, and optional `recenter`. A file lease serializes commands for the session. Acknowledgment means the producer applied that exact revision. Timeouts and unsupported actions stay pending. The CLI never infers application from writing a command file. Command and metadata writers replace files atomically with bounded retries for transient Windows file sharing conflicts.

Exit codes are 0 for success, 2 for invalid arguments or file access errors, 3 for session or cleanup failure, 4 for timeout or a pending operation, and 130 for cancellation. Pending results include `operation_result` and, for configuration, `requested_revision`. Structured failures can retain `state: "failed"` with a run identity; clients should inspect the session before requesting cleanup.

## Checks and evidence limits

Run the public CLI checks against controlled children:

```powershell
dotnet run --project controller/checks/UGEVR.Controller.Checks -c Release -- controller/UGEVR.Controller/bin/Release/net10.0/UGEVR.Controller.dll scripts/Deploy-Session.ps1
```

The helper links the actual `InjectorSession` source used by the managed injector. It tests identity, freshness, environment propagation, command acknowledgments, cancellation, ownership, monitoring failure and graceful versus explicit forced stop. Deployment checks call the real deployment script against temporary directories. They cover multiple selected replacements, restoration, cleanup pending after natural exit and cleanup failure. The helper never launches Unity, SteamVR or a headset. These checks validate the control contract; physical gameplay and streaming gates remain separate.
