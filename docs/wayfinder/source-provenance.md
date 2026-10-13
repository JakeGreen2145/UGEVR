# UEVR audit sources

Audit date: 2026-10-13. The target uses public default-branch source snapshots. These snapshots may contain changes beyond stable release 1.05, published 2024-11-16. Feature presence in source is evidence of a mechanism, not evidence that every supported game or headset works.

| Source | Pinned commit | Role |
| --- | --- | --- |
| [UEVR backend](https://github.com/praydog/UEVR/tree/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d) | `4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d` | Runtime, rendering, input, object hooks, configuration, extensions |
| [UEVR frontend](https://github.com/praydog/uevr-frontend/tree/dd6d372813097668a67e107c543f513d3170dc7a) | `dd6d372813097668a67e107c543f513d3170dc7a` | Process selection, injection, profile operations, frontend settings |
| [UEVR documentation](https://github.com/praydog/uevr-docs/tree/fc19e66b7173f259f00d1feb5cf687b3f0a5131a) | `fc19e66b7173f259f00d1feb5cf687b3f0a5131a` | Documented behavior and public APIs |
| [UGEVR proof of concept](https://github.com/JakeGreen2145/UGEVR/tree/f37758cd23213f78f6d3f3f35d66e6a26ee42c6a) | `f37758cd23213f78f6d3f3f35d66e6a26ee42c6a` | Existing implementation and simulated-headset validation baseline |

## Which UESDK?

UEVR's [submodule declaration](https://github.com/praydog/UEVR/blob/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d/.gitmodules#L10-L12) points to `praydog/UESDK`. Its current Git tree pins `14478dfae61e8385e0e62116718925c4099b1d18`. Direct HTTPS access did not expose that repository, and the public mirrors checked did not contain that commit. We cannot audit that exact SDK revision from the available public source.

A public [RenderDoc SDK mirror](https://github.com/elliotttate/UESDK-RenderDoc) preserves historical upstream commits. The UEVR [June 16 revision](https://github.com/praydog/UEVR/tree/1b1693e820413a8afb1fdfd8f9da43a19870042e) pins `491f973a78d6ffff557b4a9f41d45da42ce21580`. That exact Git object exists in the mirror and is an ancestor of its `418729025bad57b094bf6b2e2b55734454c97de6` head. Historical SDK claims can cite [that exact commit](https://github.com/elliotttate/UESDK-RenderDoc/tree/491f973a78d6ffff557b4a9f41d45da42ce21580). This does not establish that the latest SDK behaves identically.

[ilyida/Universal-UE-SDK](https://github.com/ilyida/Universal-UE-SDK/tree/937ad6e41dedfd84447ad7bb8e332cbfdd7e889f) is a separate project. Its [README](https://github.com/ilyida/Universal-UE-SDK/blob/937ad6e41dedfd84447ad7bb8e332cbfdd7e889f/README.md) credits Dumper-7; its [entry header](https://github.com/ilyida/Universal-UE-SDK/blob/937ad6e41dedfd84447ad7bb8e332cbfdd7e889f/include/uesdk.hpp) exposes `SDK::Init()` and a different layout. UEVR calls lowercase `sdk` types and uses headers such as `sdk/Math.hpp` and `sdk/UObject.hpp`. The similar name does not make it UEVR's dependency or a drop-in replacement.

## Evidence rules

- Source-confirmed means the public implementation contains the described control and behavior path. Conditional paths and SDK calls still need the stated caveats.
- Documented means official documentation describes the behavior. It is weaker evidence when no corresponding implementation path was inspected.
- Runtime-validated requires an observed run with recorded game, build, headset/runtime, scene, and acceptance check. No UEVR runtime validation was performed in this audit.
- UGEVR's existing runs establish simulated HMD tracking and eye submission in two title scenes. They establish neither physical comfort nor full gameplay compatibility.
- Unity-equivalent targets and subsystem boundaries are proposals. They need Unity-specific research and prototypes before implementation decisions are settled.
