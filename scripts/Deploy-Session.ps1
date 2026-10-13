#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Deploy','Restore','Inspect')][string]$Operation,
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$SessionDirectory,
    [string]$DistributionRoot,
    [string[]]$ReplaceRelativePath = @(),
    [int]$ExpectedGameProcessId = 0
)
$ErrorActionPreference = 'Stop'

function FullDirectory([string]$Value) {
    return [IO.Path]::GetFullPath($Value).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
function IsInside([string]$Path, [string]$Root) {
    return $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function CheckedPath([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')) {
        throw "Invalid relative deployment path: $Relative"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not (IsInside $path $Root)) { throw "Deployment path escapes its root: $Relative" }
    $part = $path
    while ($part.Length -ge $Root.Length) {
        if (Test-Path -LiteralPath $part) {
            if ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Deployment cannot cross a reparse point: $part"
            }
        }
        if ($part -eq $Root) { break }
        $part = Split-Path $part -Parent
    }
    return $path
}
function FileHash([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Scalar($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [int] -or $Value -is [long]) { return [string]$Value }
    return ConvertTo-Json -InputObject ([string]$Value) -Compress
}
function AtomicText([string]$Path, [string]$Text) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, $Text, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
function WriteReceipt($Receipt, [string]$Path) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($key in @('schema_version','deployment_id','game_root','distribution_root','state')) {
        $lines.Add($key + ': ' + (Scalar $Receipt[$key]))
    }
    $lines.Add('files:')
    foreach ($file in $Receipt.files) {
        $lines.Add('  - relative_path: ' + (Scalar $file.relative_path))
        foreach ($key in @('deployed_sha256','original_sha256','backup_path','temporary_path','owned','restore_state')) {
            $lines.Add('    ' + $key + ': ' + (Scalar $file[$key]))
        }
    }
    $lines.Add('directories:')
    foreach ($directory in $Receipt.directories) { $lines.Add('  - ' + (Scalar $directory)) }
    AtomicText $Path (($lines -join "`n") + "`n")
}
function ReadReceipt([string]$Path) {
    # Read only the versioned YAML subset emitted here, not arbitrary YAML.
    if ((Get-Item -LiteralPath $Path).Length -gt 8MB) { throw 'Deployment receipt exceeds size limit.' }
    $receipt = @{ files = [Collections.Generic.List[hashtable]]::new(); directories = [Collections.Generic.List[string]]::new() }
    $section = ''; $file = $null
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -match '^(files|directories):$') { $section = $Matches[1]; continue }
        if ($line -match '^([a-z_][a-z_0-9]*): (.+)$') {
            if ($section -or $receipt.ContainsKey($Matches[1])) { throw 'Invalid receipt header.' }
            $receipt[$Matches[1]] = ConvertFrom-Json -InputObject $Matches[2] -NoEnumerate
        } elseif ($section -eq 'files' -and $line -match '^  - relative_path: (.+)$') {
            $file = @{ relative_path = ConvertFrom-Json -InputObject $Matches[1] -NoEnumerate }
            $receipt.files.Add($file)
        } elseif ($section -eq 'files' -and $file -and $line -match '^    ([a-z_][a-z_0-9]*): (.+)$') {
            if ($file.ContainsKey($Matches[1])) { throw 'Duplicate receipt field.' }
            $file[$Matches[1]] = ConvertFrom-Json -InputObject $Matches[2] -NoEnumerate
        } elseif ($section -eq 'directories' -and $line -match '^  - (.+)$') {
            $receipt.directories.Add([string](ConvertFrom-Json -InputObject $Matches[1] -NoEnumerate))
        } elseif ($line.Trim()) { throw ('Unsupported receipt syntax: ' + $line) }
    }
    if ($receipt.schema_version -ne 1 -or $receipt.deployment_id -notmatch '^[0-9a-f]{32}$') { throw 'Unsupported deployment receipt.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $receipt.files) {
        foreach ($key in @('relative_path','deployed_sha256','original_sha256','backup_path','temporary_path','owned','restore_state')) {
            if (-not $file.ContainsKey($key)) { throw "Missing receipt field: $key" }
        }
        if ($file.deployed_sha256 -notmatch '^[0-9a-f]{64}$' -or ($null -ne $file.original_sha256 -and $file.original_sha256 -notmatch '^[0-9a-f]{64}$') -or $file.owned -isnot [bool]) { throw 'Invalid receipt file identity.' }
        if (-not $seen.Add($file.relative_path) -or $file.backup_path -ne ('backups/'+$file.relative_path) -or $file.temporary_path -ne ($file.relative_path+'.ugevr-'+$receipt.deployment_id+'.tmp')) { throw 'Invalid receipt ownership paths.' }
    }
    return $receipt
}
function Summary($Receipt, [string]$ReceiptPath) {
    foreach ($key in @('schema_version','deployment_id','state','game_root')) { $key + ': ' + (Scalar $Receipt[$key]) }
    'receipt_path: ' + (Scalar $ReceiptPath)
    'owned_files: ' + @($Receipt.files | Where-Object owned).Count
    'reused_files: ' + @($Receipt.files | Where-Object { -not $_.owned }).Count
}
function RestoreReceipt($Receipt, [string]$ReceiptPath, [string]$Root, [string]$SessionRoot) {
    $Receipt.state = 'restoring'; WriteReceipt $Receipt $ReceiptPath
    $conflicts = [Collections.Generic.List[string]]::new()
    foreach ($file in $Receipt.files) {
        if (-not $file.owned) { continue }
        $target = CheckedPath $Root $file.relative_path
        $backup = CheckedPath $SessionRoot $file.backup_path
        $temporary = CheckedPath $Root $file.temporary_path
        $current = FileHash $target
        if ($current -eq $file.original_sha256) {
            $file.restore_state = 'restored'
        } elseif ($current -eq $file.deployed_sha256) {
            if ($null -ne $file.original_sha256) {
                if ((FileHash $backup) -ne $file.original_sha256) {
                    $conflicts.Add($file.relative_path); $file.restore_state = 'backup_conflict'; continue
                }
                Copy-Item -LiteralPath $backup -Destination $temporary -Force
                [IO.File]::Move($temporary, $target, $true)
            } else {
                Remove-Item -LiteralPath $target -Force
            }
            $file.restore_state = 'restored'
        } else {
            $conflicts.Add($file.relative_path); $file.restore_state = 'external_change'
        }
        # A reserved temporary file is only removed if its content still belongs to this deployment.
        if (Test-Path -LiteralPath $temporary) {
            $temporaryHash = FileHash $temporary
            if ($temporaryHash -eq $file.deployed_sha256 -or ($null -ne $file.original_sha256 -and $temporaryHash -eq $file.original_sha256)) {
                Remove-Item -LiteralPath $temporary -Force
            } else { $conflicts.Add($file.temporary_path) }
        }
        WriteReceipt $Receipt $ReceiptPath
    }
    foreach ($relative in @($Receipt.directories | Sort-Object { $_.Length } -Descending)) {
        $directory = CheckedPath $Root $relative
        if ((Test-Path -LiteralPath $directory -PathType Container) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $directory -Force
        }
    }
    $Receipt.state = if ($conflicts.Count) { 'conflict' } else { 'restored' }
    WriteReceipt $Receipt $ReceiptPath
    if ($conflicts.Count) { throw ('Restoration preserved changed files: ' + ($conflicts -join ', ')) }
}

$root = FullDirectory $GameRoot
$sessionRoot = FullDirectory $SessionDirectory
if ($root -eq ([IO.Path]::GetPathRoot($root)).TrimEnd([IO.Path]::DirectorySeparatorChar)) { throw 'A drive root is not a game directory.' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Game root must exist.' }
$null = CheckedPath $root '.ugevr-path-check'
if ($sessionRoot -eq $root -or (IsInside $sessionRoot $root)) { throw 'Session receipts must be outside the game root.' }
if ($ExpectedGameProcessId -gt 0 -and (Get-Process -Id $ExpectedGameProcessId -ErrorAction SilentlyContinue)) { throw 'The selected game process is still running.' }
$receiptPath = Join-Path $sessionRoot 'deployment-receipt.yaml'
$null = CheckedPath $sessionRoot 'deployment-receipt.yaml'
if ($Operation -eq 'Restore') {
    $receipt = ReadReceipt $receiptPath
    if ((FullDirectory $receipt.game_root) -ne $root) { throw 'Receipt game root differs from the selected game root.' }
    RestoreReceipt $receipt $receiptPath $root $sessionRoot
    Summary $receipt $receiptPath
    return
}
if (-not $DistributionRoot) { throw 'Pass a prepared distribution directory.' }
$distribution = FullDirectory $DistributionRoot
if (-not (Test-Path -LiteralPath $distribution -PathType Container)) { throw 'Distribution root must exist.' }
if ($distribution -eq $root -or (IsInside $distribution $root) -or $sessionRoot -eq $distribution -or (IsInside $sessionRoot $distribution)) { throw 'Distribution, game and session roots must be separate.' }
$null = CheckedPath $distribution '.ugevr-path-check'
$receipt = @{ schema_version=1; deployment_id=[guid]::NewGuid().ToString('N'); game_root=$root; distribution_root=$distribution; state='prepared'; files=[Collections.Generic.List[hashtable]]::new(); directories=[Collections.Generic.List[string]]::new() }
$selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in $ReplaceRelativePath) { $full = CheckedPath $root $relative; $null = $selected.Add([IO.Path]::GetRelativePath($root,$full)) }
$conflicts = [Collections.Generic.List[string]]::new()
foreach ($sourceFile in @(Get-ChildItem -LiteralPath $distribution -Recurse -File -Force | Sort-Object FullName)) {
    $relative = [IO.Path]::GetRelativePath($distribution,$sourceFile.FullName)
    $source = CheckedPath $distribution $relative
    $target = CheckedPath $root $relative
    if (Test-Path -LiteralPath $target -PathType Container) { throw "Target is a directory: $relative" }
    $deployedHash = FileHash $source; $originalHash = FileHash $target
    if ($null -ne $originalHash -and $originalHash -ne $deployedHash -and -not $selected.Contains($relative)) { $conflicts.Add($relative) }
    $receipt.files.Add(@{ relative_path=$relative; deployed_sha256=$deployedHash; original_sha256=$originalHash; backup_path=('backups/'+$relative); temporary_path=($relative+'.ugevr-'+$receipt.deployment_id+'.tmp'); owned=($deployedHash -ne $originalHash); restore_state='pending' })
}
if (-not $receipt.files.Count) { throw 'Distribution contains no files.' }
# An existing loader is reusable only when its configuration and core match the selected distribution.
$loader = CheckedPath $root 'winhttp.dll'
if (Test-Path -LiteralPath $loader) {
    foreach ($relative in @('winhttp.dll','doorstop_config.ini','BepInEx/core/BepInEx.dll')) {
        $source = CheckedPath $distribution $relative; $target = CheckedPath $root $relative
        if ((FileHash $source) -eq $null) { throw "Distribution cannot supply the existing loader component: $relative" }
        if ((FileHash $source) -ne (FileHash $target)) {
            if (-not $selected.Contains([IO.Path]::GetRelativePath($root,$target))) { $conflicts.Add($relative) }
        }
    }
}
if ($conflicts.Count) { throw ('Explicit conflict selection required: ' + (($conflicts | Sort-Object -Unique) -join ', ')) }
if ($Operation -eq 'Inspect') { Summary $receipt $receiptPath; return }
if (Test-Path -LiteralPath $receiptPath) { throw 'Session already has a deployment receipt. Restore it before deploying again.' }
$null = New-Item -ItemType Directory -Force -Path $sessionRoot
$receipt.state='deploying'; WriteReceipt $receipt $receiptPath
try {
    foreach ($file in $receipt.files) {
        if (-not $file.owned) { $file.restore_state='reused'; continue }
        $source = CheckedPath $distribution $file.relative_path
        $target = CheckedPath $root $file.relative_path
        $backup = CheckedPath $sessionRoot $file.backup_path
        $temporary = CheckedPath $root $file.temporary_path
        if ((FileHash $target) -ne $file.original_sha256 -or (FileHash $source) -ne $file.deployed_sha256) { throw 'Files changed after preflight.' }
        if ($null -ne $file.original_sha256) {
            $null=New-Item -ItemType Directory -Force -Path (Split-Path $backup -Parent)
            Copy-Item -LiteralPath $target -Destination $backup
            if ((FileHash $backup) -ne $file.original_sha256) { throw 'Backup verification failed.' }
        }
        $missing = [Collections.Generic.List[string]]::new()
        $directory=Split-Path $target -Parent
        while ($directory -ne $root -and -not (Test-Path -LiteralPath $directory)) {
            $missing.Add([IO.Path]::GetRelativePath($root,$directory)); $directory=Split-Path $directory -Parent
        }
        foreach ($relative in $missing) { if (-not $receipt.directories.Contains($relative)) { $receipt.directories.Add($relative) } }
        WriteReceipt $receipt $receiptPath
        $null=New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent)
        Copy-Item -LiteralPath $source -Destination $temporary
        if ((FileHash $temporary) -ne $file.deployed_sha256) { throw 'Deployment copy verification failed.' }
        [IO.File]::Move($temporary,$target,$true)
        $file.restore_state='deployed'; WriteReceipt $receipt $receiptPath
    }
    $receipt.state='active'; WriteReceipt $receipt $receiptPath
} catch {
    $deploymentError=$_.Exception.Message
    try { RestoreReceipt $receipt $receiptPath $root $sessionRoot }
    catch { throw ('Deployment failed: '+$deploymentError+'. Cleanup also needs attention: '+$_.Exception.Message) }
    throw ('Deployment failed and owned files were restored: '+$deploymentError)
}
Summary $receipt $receiptPath
