$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'dist\UnityVRInjector-poc'
if (Test-Path -LiteralPath $package) { throw 'Package directory already exists; use a new destination or review existing files.' }
New-Item -ItemType Directory -Force -Path $package | Out-Null
Copy-Item -Path "$repo\vendor\BepInEx\*" -Destination $package -Recurse -Force
New-Item -ItemType Directory -Force -Path "$package\BepInEx\plugins\UnityVRInjector","$package\BepInEx\config" | Out-Null
Copy-Item -LiteralPath "$repo\src\bin\Release\net35\UnityVRInjector.dll" -Destination "$package\BepInEx\plugins\UnityVRInjector"
Copy-Item -LiteralPath "$repo\build\UnityVRRenderBridge.dll","$repo\vendor\openvr_api.dll","$repo\README.md","$repo\THIRD_PARTY_NOTICES.md","$repo\vendor\Valve-OpenVR-LICENSE","$repo\vendor\BepInEx-LICENSE" -Destination $package
"[Chainloader]`r`nHideManagerGameObject = true`r`n" | Set-Content -LiteralPath "$package\BepInEx\config\BepInEx.cfg" -Encoding utf8
Compress-Archive -Path "$package\*" -DestinationPath "$repo\dist\UnityVRInjector-poc.zip"
Write-Output "Packaged $repo\dist\UnityVRInjector-poc.zip"
