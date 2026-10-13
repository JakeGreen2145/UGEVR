param([switch]$Restore,[string]$SteamRoot = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$settingsPath = Join-Path $SteamRoot 'config\steamvr.vrsettings'
$vrPathReg = Join-Path $SteamRoot 'steamapps\common\SteamVR\bin\win64\vrpathreg.exe'
$driverPath = Join-Path $repo 'diagnostics\test-hmd-driver\driver'
if (Get-Process vrserver -ErrorAction SilentlyContinue) { throw 'Close SteamVR before switching diagnostic drivers.' }
if ($Restore) {
    & $vrPathReg removedriver $driverPath
    & (Join-Path $PSScriptRoot 'Use-SimulatedHeadset.ps1') -Restore -SteamRoot $SteamRoot
    exit
}
$pathsFile = Join-Path $env:LOCALAPPDATA 'openvr\openvrpaths.vrpath'
New-Item -ItemType Directory -Force -Path (Join-Path $repo 'evidence') | Out-Null
if (-not (Test-Path -LiteralPath "$repo\evidence\openvrpaths-original.json")) { Copy-Item -LiteralPath $pathsFile -Destination "$repo\evidence\openvrpaths-original.json" }
& $vrPathReg adddriver $driverPath
if ($LASTEXITCODE -ne 0) { throw 'Driver registration failed' }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.steamvr | Add-Member -NotePropertyName forcedDriver -NotePropertyValue 'unityvrtest' -Force
$settings.driver_null.enable = $false
$settings | Add-Member -NotePropertyName driver_unityvrtest -NotePropertyValue ([pscustomobject]@{enable=$true}) -Force
$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Write-Output 'Registered diagnostic HMD with runtime-controlled simulated 6DOF poses.'
