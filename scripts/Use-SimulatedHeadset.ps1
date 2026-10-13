param([switch]$Restore,[string]$SteamRoot = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath)
$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $SteamRoot 'config\steamvr.vrsettings'
$backupPath = Join-Path $PSScriptRoot '..\evidence\steamvr-settings-original.json'
if ($Restore) {
    Copy-Item -LiteralPath $backupPath -Destination $settingsPath -Force
    Write-Output 'Restored original SteamVR settings. Restart SteamVR to apply.'
    exit
}
if (Get-Process vrserver -ErrorAction SilentlyContinue) { throw 'Close SteamVR before switching to simulated headset.' }
New-Item -ItemType Directory -Force -Path (Split-Path $backupPath -Parent) | Out-Null
if (-not (Test-Path -LiteralPath $backupPath)) { Copy-Item -LiteralPath $settingsPath -Destination $backupPath }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.steamvr | Add-Member -NotePropertyName forcedDriver -NotePropertyValue 'null' -Force
$settings.steamvr | Add-Member -NotePropertyName requireHmd -NotePropertyValue $false -Force
$settings.steamvr | Add-Member -NotePropertyName activateMultipleDrivers -NotePropertyValue $false -Force
$settings | Add-Member -NotePropertyName driver_null -NotePropertyValue ([pscustomobject]@{enable=$true;serialNumber='UnityVR-POC-SIMULATED';modelNumber='SIMULATED HEADSET';windowX=100;windowY=100;windowWidth=1280;windowHeight=720;renderWidth=1024;renderHeight=1024;displayFrequency=90.0}) -Force
$settings | Add-Member -NotePropertyName power -NotePropertyValue ([pscustomobject]@{pauseCompositorOnStandby=$false;turnOffScreensTimeout=3600.0}) -Force
$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Write-Output 'SteamVR simulated headset configured. Original settings backed up in evidence.'
