#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $repo '.local-tools/deployment-tests'))
$testRoot = Join-Path $fixtureRoot ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Force -Path $testRoot
$deploy = Join-Path $PSScriptRoot 'Deploy-Session.ps1'
$passed = 0
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function NewCase([string]$Name) {
    $caseRoot = Join-Path $testRoot $Name
    $game = Join-Path $caseRoot 'game'; $distribution = Join-Path $caseRoot 'distribution'; $session = Join-Path $caseRoot 'session'
    $null = New-Item -ItemType Directory -Force -Path $game,$distribution
    return @{ GameRoot=$game; DistributionRoot=$distribution; SessionDirectory=$session }
}
function Put([string]$Root,[string]$Relative,[string]$Value) {
    $path=Join-Path $Root $Relative
    $null=New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent)
    [IO.File]::WriteAllText($path,$Value)
}
function Text([string]$Root,[string]$Relative) { return [IO.File]::ReadAllText((Join-Path $Root $Relative)) }
function MustFail([scriptblock]$Action,[string]$Message) {
    $failed=$false
    try { $null=& $Action } catch { $failed=$true }
    Assert $failed $Message
}
try {
    $case=NewCase 'fresh'
    Put $case.GameRoot 'save.txt' 'untouched'
    Put $case.DistributionRoot 'BepInEx/plugins/UGEVR.dll' 'plugin'
    $result=& $deploy -Operation Deploy @case
    Assert ($result -contains 'state: "active"') 'Fresh deployment did not become active.'
    Assert ((Text $case.GameRoot 'BepInEx/plugins/UGEVR.dll') -eq 'plugin') 'Plugin was not deployed.'
    $null=& $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    Assert (-not (Test-Path -LiteralPath (Join-Path $case.GameRoot 'BepInEx'))) 'Owned empty directories remain.'
    Assert ((Text $case.GameRoot 'save.txt') -eq 'untouched') 'Unrelated game file changed.'
    $passed++

    $case=NewCase 'conflict'
    Put $case.GameRoot 'settings.txt' 'original'
    Put $case.DistributionRoot 'settings.txt' 'replacement'
    MustFail { & $deploy -Operation Deploy @case } 'Unselected replacement was allowed.'
    Assert ((Text $case.GameRoot 'settings.txt') -eq 'original') 'Rejected preflight changed target.'
    Assert (-not (Test-Path -LiteralPath $case.SessionDirectory)) 'Rejected preflight created a receipt.'
    $null=& $deploy -Operation Deploy @case -ReplaceRelativePath 'settings.txt'
    $null=& $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    Assert ((Text $case.GameRoot 'settings.txt') -eq 'original') 'Explicit replacement did not restore original bytes.'
    $passed++

    $case=NewCase 'loader'
    foreach($relative in @('winhttp.dll','doorstop_config.ini','BepInEx/core/BepInEx.dll')) {
        Put $case.GameRoot $relative 'compatible'
        Put $case.DistributionRoot $relative 'compatible'
    }
    Put $case.DistributionRoot 'BepInEx/plugins/UGEVR.dll' 'new plugin'
    $null=& $deploy -Operation Deploy @case
    $null=& $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    Assert ((Text $case.GameRoot 'winhttp.dll') -eq 'compatible') 'Reused loader was removed.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $case.GameRoot 'BepInEx/plugins'))) 'Owned plugin directory remains.'
    $case.SessionDirectory=Join-Path (Split-Path $case.SessionDirectory -Parent) 'other-session'
    Put $case.GameRoot 'winhttp.dll' 'unknown loader'
    MustFail { & $deploy -Operation Deploy @case } 'Unknown loader was reused.'
    $null=& $deploy -Operation Deploy @case -ReplaceRelativePath 'winhttp.dll'
    $null=& $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    Assert ((Text $case.GameRoot 'winhttp.dll') -eq 'unknown loader') 'Selected loader remedy did not restore original.'
    $passed++

    $case=NewCase 'external-edit'
    Put $case.DistributionRoot 'plugin.dll' 'deployed'
    $null=& $deploy -Operation Deploy @case
    Put $case.GameRoot 'plugin.dll' 'external edit'
    MustFail { & $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory } 'External edit was overwritten.'
    Assert ((Text $case.GameRoot 'plugin.dll') -eq 'external edit') 'External edit was not preserved.'
    Assert ((Text $case.SessionDirectory 'deployment-receipt.yaml') -match 'state: "conflict"') 'Partial cleanup was not recorded.'
    $passed++

    $case=NewCase 'backup-edit'
    Put $case.GameRoot 'plugin.dll' 'original'
    Put $case.DistributionRoot 'plugin.dll' 'deployed'
    $null=& $deploy -Operation Deploy @case -ReplaceRelativePath 'plugin.dll'
    Put $case.SessionDirectory 'backups/plugin.dll' 'tampered backup'
    MustFail { & $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory } 'Tampered backup was used.'
    Assert ((Text $case.GameRoot 'plugin.dll') -eq 'deployed') 'Target was overwritten by tampered backup.'
    $passed++

    $case=NewCase 'rollback'
    Put $case.DistributionRoot 'a-new.dll' 'new'
    Put $case.DistributionRoot 'z-locked.dll' 'replacement'
    Put $case.GameRoot 'z-locked.dll' 'original'
    $locked=[IO.File]::Open((Join-Path $case.GameRoot 'z-locked.dll'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { MustFail { & $deploy -Operation Deploy @case -ReplaceRelativePath 'z-locked.dll' } 'Locked target did not fail deployment.' }
    finally { $locked.Dispose() }
    Assert (-not (Test-Path -LiteralPath (Join-Path $case.GameRoot 'a-new.dll'))) 'Transaction left a newly deployed file.'
    Assert ((Text $case.GameRoot 'z-locked.dll') -eq 'original') 'Rollback lost original bytes.'
    Assert ((Text $case.SessionDirectory 'deployment-receipt.yaml') -match 'state: "restored"') 'Rollback result missing.'
    $passed++

    $case=NewCase 'restart-recovery'
    Put $case.DistributionRoot 'plugin.dll' 'deployed'
    $null=& $deploy -Operation Deploy @case
    MustFail { & $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory -ExpectedGameProcessId $PID } 'Restoration accepted a live selected process.'
    $restoreOutput=& (Get-Command pwsh).Source -NoProfile -File $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    Assert ($LASTEXITCODE -eq 0) 'Another process could not recover a durable receipt.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $case.GameRoot 'plugin.dll'))) 'Restart recovery left owned file.'
    $null=& $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory
    $passed++

    $case=NewCase 'confinement'
    Put $case.DistributionRoot 'plugin.dll' 'deployed'
    MustFail { & $deploy -Operation Inspect @case -ReplaceRelativePath '../outside.txt' } 'Escaped replacement path was accepted.'
    $null=& $deploy -Operation Deploy @case
    $wrongRoot=Join-Path (Split-Path $case.GameRoot -Parent) 'wrong'
    $null=New-Item -ItemType Directory -Path $wrongRoot
    MustFail { & $deploy -Operation Restore -GameRoot $wrongRoot -SessionDirectory $case.SessionDirectory } 'Mismatched receipt root was accepted.'
    $receiptPath=Join-Path $case.SessionDirectory 'deployment-receipt.yaml'
    $receipt=[IO.File]::ReadAllText($receiptPath).Replace('relative_path: "plugin.dll"','relative_path: "../outside.txt"')
    [IO.File]::WriteAllText($receiptPath,$receipt)
    MustFail { & $deploy -Operation Restore -GameRoot $case.GameRoot -SessionDirectory $case.SessionDirectory } 'Tampered ownership path was accepted.'
    Assert ((Text $case.GameRoot 'plugin.dll') -eq 'deployed') 'Invalid receipt changed owned files.'
    $passed++

    $case=NewCase 'reparse'
    Put $case.DistributionRoot 'nested/plugin.dll' 'deployed'
    $elsewhere=Join-Path (Split-Path $case.GameRoot -Parent) 'elsewhere'
    $null=New-Item -ItemType Directory -Path $elsewhere
    $null=New-Item -ItemType Junction -Path (Join-Path $case.GameRoot 'nested') -Target $elsewhere
    MustFail { & $deploy -Operation Deploy @case } 'Deployment crossed a junction.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $elsewhere 'plugin.dll'))) 'Junction target was mutated.'
    Remove-Item -LiteralPath (Join-Path $case.GameRoot 'nested') -Force
    $passed++
    Write-Output "PASS: $passed deployment scenarios through the public operation boundary."
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($fixtureRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
