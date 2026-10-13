#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive,[string[]]$ForbiddenPath=@())
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixtureBase=[IO.Path]::GetFullPath((Join-Path $repo '.local-tools/distribution-checks'))
$fixture=Join-Path $fixtureBase ([guid]::NewGuid().ToString('N'))
$extracted=Join-Path $fixture 'extracted'
$null=New-Item -ItemType Directory -Force -Path $fixture
$success=$false
function Assert([bool]$value,[string]$message) { if(-not $value) { throw $message } }
function Manifest([string]$path) {
    $result=@{ files=[Collections.Generic.List[hashtable]]::new(); source_files=[Collections.Generic.List[hashtable]]::new() }
    $section=''; $item=$null
    foreach($line in [IO.File]::ReadAllLines($path)) {
        if($line -match '^(source_files|files):$') { $section=$Matches[1]; continue }
        if(-not $section -and $line -match '^([a-z_][a-z_0-9]*): (.+)$') {
            Assert (-not $result.ContainsKey($Matches[1])) 'Duplicate manifest header.'
            $result[$Matches[1]]=ConvertFrom-Json -InputObject $Matches[2] -NoEnumerate
        } elseif($section -and $line -match '^  - path: (.+)$') {
            $item=@{ path=ConvertFrom-Json -InputObject $Matches[1] -NoEnumerate }; $result[$section].Add($item)
        } elseif($section -and $item -and $line -match '^    sha256: (.+)$') {
            Assert (-not $item.ContainsKey('sha256')) 'Duplicate hash field.'
            $item.sha256=ConvertFrom-Json -InputObject $Matches[1] -NoEnumerate
        } else { throw 'Unsupported manifest syntax.' }
    }
    Assert ($result.schema_version -eq '1') 'Unsupported manifest version.'
    Assert ($result.source_revision -match '^[0-9a-f]{40}$' -and $result.source_dirty -is [bool]) 'Missing source provenance.'
    Assert ($result.physical_gameplay -eq 'unvalidated' -and $result.support_state -eq 'experimental') 'Package falsely advertises validation.'
    Assert ($result.manifest_self_hash -eq 'excluded' -and $result.game_payload -eq 'payload') 'Unexpected manifest/hash layout.'
    foreach($section in @('files','source_files')) {
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($item in $result[$section]) {
            Assert ($item.path -is [string] -and $item.sha256 -match '^[0-9a-f]{64}$') 'Invalid manifest file entry.'
            Assert ($item.path -notmatch '(^|/)\.\.(/|$)' -and $item.path -notmatch '[:\\]' -and -not [IO.Path]::IsPathRooted($item.path)) 'Unsafe manifest path.'
            Assert ($seen.Add($item.path)) 'Duplicate manifest file.'
        }
    }
    return $result
}
function VerifyFiles([string]$root,$manifest) {
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $manifest.files) {
        $path=Join-Path $root $item.path
        Assert (Test-Path -LiteralPath $path -PathType Leaf) ('Missing package file: '+$item.path)
        Assert ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $item.sha256) ('Hash mismatch: '+$item.path)
        $null=$seen.Add($item.path)
    }
    foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
        Assert ($relative -eq 'distribution.yaml' -or $seen.Contains($relative)) ('Unmanifested package file: '+$relative)
    }
}
try {
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Archive),$extracted)
    $manifest=Manifest (Join-Path $extracted 'distribution.yaml')
    VerifyFiles $extracted $manifest
    $files=@(Get-ChildItem -LiteralPath $extracted -Recurse -File -Force)
    foreach($file in $files) {
        $relative=[IO.Path]::GetRelativePath($extracted,$file.FullName).Replace('\','/')
        Assert ($relative -notmatch '(^|/)(Managed|evidence|research|\.local-tools|frame-testbench|bin|obj)(/|$)' -and $relative -notmatch '\.(pdb|assets|unity|log|bmp|png|jpg|mp4)$' -and $file.Name -notmatch '^UnityEngine.*\.dll$') ('Forbidden package content: '+$relative)
        # Read both encodings to detect private build/reference paths in binaries.
        $bytes=[IO.File]::ReadAllBytes($file.FullName)
        $ascii=[Text.Encoding]::UTF8.GetString($bytes); $unicode=[Text.Encoding]::Unicode.GetString($bytes)
        foreach($private in @($repo,$env:USERPROFILE)+$ForbiddenPath) {
            if(-not $private) { continue }
            foreach($variant in @($private,$private.Replace('\','/'),$private.Replace('\','\\'))) {
                Assert (-not $ascii.Contains($variant,[StringComparison]::OrdinalIgnoreCase) -and -not $unicode.Contains($variant,[StringComparison]::OrdinalIgnoreCase)) ('Private path in package file: '+$relative)
            }
        }
    }
    Assert (@($files | Where-Object Name -eq 'UnityVRInjector.dll').Count -eq 1) 'Distribution must contain one managed injector.'
    Assert (@($files | Where-Object Name -eq 'UnityVRRenderBridge.dll').Count -eq 1) 'Distribution must contain one native bridge.'
    $config=[IO.File]::ReadAllText((Join-Path $extracted 'payload/BepInEx/config/BepInEx.cfg'))
    Assert ($config -match '(?m)^HideManagerGameObject = true$') 'HideManagerGameObject default missing.'
    foreach($required in @('controller/UGEVR.Controller.exe','controller/UGEVR.Controller.dll','controller/UGEVR.Controller.runtimeconfig.json','controller/UGEVR.Controller.deps.json','scripts/Deploy-Session.ps1','scripts/UGEVR.ps1','payload/winhttp.dll','payload/.doorstop_version','vendor/BepInEx-LICENSE','vendor/Valve-OpenVR-LICENSE','vendor/DotNetHost-LICENSE.txt','vendor/DotNetHost-ThirdPartyNotices.txt')) { Assert (Test-Path -LiteralPath (Join-Path $extracted $required)) ('Required package file missing: '+$required) }
    $runtime=Get-Content -Raw -LiteralPath (Join-Path $extracted 'controller/UGEVR.Controller.runtimeconfig.json') | ConvertFrom-Json
    Assert ($runtime.runtimeOptions.framework.name -eq 'Microsoft.NETCore.App' -and $runtime.runtimeOptions.framework.version -match '^10\.') 'Unexpected controller runtime requirement.'
    $help=& (Join-Path $extracted 'controller/UGEVR.Controller.exe') help
    Assert ($LASTEXITCODE -eq 0 -and ($help -join "`n") -match 'UGEVR launch') 'Extracted apphost help failed.'

    $wrapperHelp=& (Join-Path $extracted 'scripts/UGEVR.ps1') help
    Assert ($LASTEXITCODE -eq 0 -and ($wrapperHelp -join "
") -match 'UGEVR launch') 'Extracted PowerShell wrapper help failed.'
    $deploy=Join-Path $extracted 'scripts/Deploy-Session.ps1'
    $game=Join-Path $fixture 'game'; $session=Join-Path $fixture 'session'
    $null=New-Item -ItemType Directory -Path $game
    [IO.File]::WriteAllText((Join-Path $game 'save.fixture'),'preserved')
    $null=& $deploy -Operation Deploy -GameRoot $game -DistributionRoot (Join-Path $extracted 'payload') -SessionDirectory $session
    foreach($payloadFile in Get-ChildItem -LiteralPath (Join-Path $extracted 'payload') -Recurse -File -Force) {
        $relative=[IO.Path]::GetRelativePath((Join-Path $extracted 'payload'),$payloadFile.FullName)
        Assert ((Get-FileHash -LiteralPath (Join-Path $game $relative)).Hash -eq (Get-FileHash -LiteralPath $payloadFile.FullName).Hash) 'Payload deployment bytes differ.'
    }
    Assert (-not (Test-Path -LiteralPath (Join-Path $game 'controller'))) 'Controller was deployed into the game.'
    $null=& $deploy -Operation Restore -GameRoot $game -SessionDirectory $session
    Assert (@(Get-ChildItem -LiteralPath $game -Force).Count -eq 1 -and [IO.File]::ReadAllText((Join-Path $game 'save.fixture')) -eq 'preserved') 'Extracted deployment did not restore the fixture.'

    # Check hash rejection through the same reader after a controlled mutation.
    $tampered=Join-Path $extracted 'README.txt'; $original=[IO.File]::ReadAllBytes($tampered)
    [IO.File]::AppendAllText($tampered,"`ncontrolled mutation")
    $rejected=$false; try { VerifyFiles $extracted $manifest } catch { $rejected=$true }
    Assert $rejected 'Tampered package was accepted.'
    [IO.File]::WriteAllBytes($tampered,$original); VerifyFiles $extracted $manifest

    # This source-side fixture is not shipped; the extracted runtime operations
    # receive only their packaged DLL/script paths and controlled child identity.
    & dotnet run --project (Join-Path $repo 'controller/checks/UGEVR.Controller.Checks/UGEVR.Controller.Checks.csproj') -c Release -- (Join-Path $extracted 'controller/UGEVR.Controller.dll') $deploy
    Assert ($LASTEXITCODE -eq 0) 'Extracted controller public CLI fixture checks failed.'
    $success=$true
    Write-Output ('PASS development distribution: '+$manifest.files.Count+' hashes, omissions/private paths, apphost/.NET10 metadata, payload deployment/restoration, tamper rejection and extracted controller fixture CLI.')
    Write-Output ('Archive SHA256: '+(Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant())
} finally {
    if($success) {
        $resolved=[IO.Path]::GetFullPath($fixture)
        if(-not $resolved.StartsWith($fixtureBase+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe packaging fixture cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else { Write-Output ('Retained failed packaging fixture for inspection: '+$fixture) }
}
