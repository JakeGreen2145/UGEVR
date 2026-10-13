param([Parameter(Mandatory=$true)][string]$GameRoot,[string]$OutputName='firewatch-6dof',[string]$SessionDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$gameEvidence = Join-Path $(if ($SessionDirectory) { $SessionDirectory } else { $GameRoot }) 'UnityVR-evidence'
if (-not (Test-Path -LiteralPath $gameEvidence -PathType Container)) { throw 'Enable Diagnostics.CaptureImages in the plugin and pass -SessionDirectory when using the controller.' }
$posePath = Join-Path $repo 'diagnostics\test-hmd-driver\pose.txt'
$probePath = Join-Path $repo 'diagnostics\VRProbe\bin\Release\net10.0\VRProbe.exe'
$cases = [ordered]@{x='0.25 1.7 0 0 0 0';y='0 1.9 0 0 0 0';z='0 1.7 0.25 0 0 0';yaw='0 1.7 0 30 0 0';pitch='0 1.7 0 0 20 0';roll='0 1.7 0 0 0 15';reset='0 1.7 0 0 0 0'}
foreach ($case in $cases.GetEnumerator()) {
    $outDir = Join-Path $repo "evidence\$OutputName\$($case.Key)"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $before = @(Get-ChildItem -LiteralPath $gameEvidence -Filter 'capture-*.json' | Select-Object -ExpandProperty Name)
    $writeDeadline = (Get-Date).AddSeconds(5)
    do {
        try { $case.Value | Set-Content -LiteralPath $posePath -Encoding ascii; $written=$true }
        catch { $written=$false; if ((Get-Date) -ge $writeDeadline) { throw }; Start-Sleep -Milliseconds 50 }
    } while (-not $written)
    Start-Sleep -Milliseconds 400
    $runtimeJson = & $probePath
    $runtimeJson | Set-Content -LiteralPath (Join-Path $outDir 'runtime.json') -Encoding utf8
    $runtime = $runtimeJson | ConvertFrom-Json
    if ($runtime.serial -ne 'unityvr-diagnostic-simulated-hmd-001' -or -not $runtime.valid) { throw 'Expected simulated runtime HMD with valid tracking' }
    New-Item -ItemType File -Force -Path (Join-Path $gameEvidence 'capture-request') | Out-Null
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $capture = Get-ChildItem -LiteralPath $gameEvidence -Filter 'capture-*.json' | Where-Object { $_.Name -notin $before } | Select-Object -First 1
    } while (-not $capture -and (Get-Date) -lt $deadline)
    if (-not $capture) { throw "Timed out capturing $($case.Key)" }
    $record = Get-Content -LiteralPath $capture.FullName -Raw | ConvertFrom-Json
    Start-Sleep -Milliseconds 500
    Get-ChildItem -LiteralPath $gameEvidence -Filter "*-$($record.frame).*" | Copy-Item -Destination $outDir -Force
    $record.frame | Set-Content -LiteralPath (Join-Path $outDir 'current-frame.txt') -Encoding ascii
    Write-Output "Captured $($case.Key) at frame $($record.frame), head=$($record.headPosition), restored=$($record.gameCameraRestored)"
}
