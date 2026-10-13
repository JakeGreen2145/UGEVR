param([Parameter(Mandatory=$true)][string]$GameExe,[string]$SteamAppId)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $GameExe).Path
$root = Split-Path $exe -Parent
if ($SteamAppId) {
    $env:SteamAppId = $SteamAppId
    $env:SteamGameId = $SteamAppId
}
$logPath = Join-Path $root 'player-test.log'
$arguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+$logPath+'"'))
$process = Start-Process -FilePath $exe -WorkingDirectory $root -ArgumentList $arguments -PassThru
Write-Output "Launched $exe as PID $($process.Id)"
