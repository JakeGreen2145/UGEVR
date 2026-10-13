param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = (Resolve-Path -LiteralPath $GameRoot).Path
if (Test-Path -LiteralPath (Join-Path $root 'winhttp.dll')) { throw 'Existing loader detected. Install into an unmodded test copy.' }
Copy-Item -Path "$repo\vendor\BepInEx\*" -Destination $root -Recurse -Force
New-Item -ItemType Directory -Force -Path "$root\BepInEx\plugins\UnityVRInjector" | Out-Null
New-Item -ItemType Directory -Force -Path "$root\BepInEx\config" | Out-Null
# Prevent the manager from entering the game's ordinary object lifecycle.
# This fixed the observed Awake-only plugin failure in Outer Wilds.
"[Chainloader]`r`nHideManagerGameObject = true`r`n" | Set-Content -LiteralPath "$root\BepInEx\config\BepInEx.cfg" -Encoding utf8
Copy-Item -LiteralPath "$repo\src\bin\Release\net35\UnityVRInjector.dll" -Destination "$root\BepInEx\plugins\UnityVRInjector"
Copy-Item -LiteralPath "$repo\build\UnityVRRenderBridge.dll","$repo\vendor\openvr_api.dll" -Destination $root
Write-Output "Installed prototype into $root"
