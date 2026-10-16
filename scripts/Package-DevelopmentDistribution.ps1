#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$GameManaged=$env:UGEVR_GAME_MANAGED,
    [string]$ReferenceBuild='unspecified',
    [string]$Version,
    [string]$BepInExArchive,
    [string]$CompilerRoot,[string]$SdkRoot,[string]$SdkVersion
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$package=[IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
$archive=$package+'.zip'
if(Test-Path -LiteralPath $package) { throw 'Destination exists. Choose a fresh directory; packaging never overwrites or deletes a distribution.' }
if(Test-Path -LiteralPath $archive) { throw 'Archive exists. Choose a fresh destination.' }
if($package -eq ([IO.Path]::GetPathRoot($package)).TrimEnd([IO.Path]::DirectorySeparatorChar)) { throw 'Destination cannot be a drive root.' }
if(-not $GameManaged -or -not (Test-Path -LiteralPath (Join-Path $GameManaged 'UnityEngine.CoreModule.dll'))) { throw 'Pass the chosen Mono game Managed directory.' }
if($ReferenceBuild -match '[\\/]' -or $ReferenceBuild -match '[A-Za-z]:') { throw 'ReferenceBuild must be a public build label, not a path.' }
$controllerProject=Join-Path $repo 'controller\UGEVR.Controller\UGEVR.Controller.csproj'
if(-not (Test-Path -LiteralPath $controllerProject)) { throw 'The session controller must be integrated before packaging.' }
if(-not $BepInExArchive) { $BepInExArchive=Join-Path $repo 'downloads\BepInEx_win_x64_5.4.23.5.zip' }
$loaderHash='82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
if(-not (Test-Path -LiteralPath $BepInExArchive) -or (Get-FileHash -LiteralPath $BepInExArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $loaderHash) { throw 'Supply the official pinned BepInEx 5.4.23.5 x64 archive; missing or mismatched archive.' }
$revision=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
if(-not $Version) { $Version='0.1.0-dev.'+$revision.Substring(0,12) }
if($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,79}$') { throw 'Version must be a simple package version.' }
$dirty=[bool](& git -C $repo status --porcelain --untracked-files=normal)
$sourceFiles=@('src/*.cs','src/*.cpp','src/*.h','src/*.csproj','controller/UGEVR.Controller/*.cs','controller/UGEVR.Controller/*.csproj','scripts/Build-Prototype.ps1','scripts/Resolve-BuildTools.ps1','scripts/Deploy-Session.ps1','scripts/UGEVR.ps1','scripts/Package-DevelopmentDistribution.ps1','vendor/openvr.h','vendor/openvr_api.cs','vendor/openvr_api.lib','vendor/openvr_api.dll')
$sourceEntries=@($sourceFiles | ForEach-Object { Get-ChildItem -Path (Join-Path $repo $_) -File } | Sort-Object FullName -Unique | ForEach-Object {
    [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($repo,$_.FullName).Replace('\','/'); Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$stage=Join-Path $repo ('build\distribution-'+[guid]::NewGuid().ToString('N'))
$loader=Join-Path $stage 'loader'
$published=Join-Path $stage 'controller'
$null=New-Item -ItemType Directory -Path $stage
Expand-Archive -LiteralPath $BepInExArchive -DestinationPath $loader
foreach($dependency in @('BepInEx/core/BepInEx.dll')) {
    $prepared=Join-Path (Join-Path $repo 'vendor/BepInEx') $dependency
    if(-not (Test-Path -LiteralPath $prepared) -or (Get-FileHash -LiteralPath $prepared).Hash -ne (Get-FileHash -LiteralPath (Join-Path $loader $dependency)).Hash) { throw 'Prepare the pinned BepInEx dependency before building.' }
}
$tools=& (Join-Path $PSScriptRoot 'Resolve-BuildTools.ps1') -CompilerRoot $CompilerRoot -SdkRoot $SdkRoot -SdkVersion $SdkVersion
& (Join-Path $PSScriptRoot 'Build-Prototype.ps1') -GameManaged $GameManaged -CompilerRoot $CompilerRoot -SdkRoot $SdkRoot -SdkVersion $SdkVersion -DistributionBuild
if(-not (Test-Path -LiteralPath (Join-Path $repo 'src/bin/Release/net35/UnityVRInjector.dll'))) { throw 'Managed build output missing.' }
& dotnet publish $controllerProject -c Release -r win-x64 --self-contained false -o $published -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$repo=/_/ugevr"
if($LASTEXITCODE -ne 0) { throw 'Controller publish failed.' }
if((& git -C $repo rev-parse HEAD).Trim() -ne $revision) { throw 'Source revision changed during the build. Retry from a stable checkout.' }
foreach($source in $sourceEntries) {
    if((Get-FileHash -LiteralPath (Join-Path $repo $source.Path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.Hash) { throw 'A build input changed during compilation. Retry from a stable checkout.' }
}
$dirty=$dirty -or [bool](& git -C $repo status --porcelain --untracked-files=normal)
$null=New-Item -ItemType Directory -Path $package
$payload=Join-Path $package 'payload'
$null=New-Item -ItemType Directory -Path $payload,(Join-Path $package 'controller'),(Join-Path $package 'scripts'),(Join-Path $package 'docs'),(Join-Path $package 'vendor')
# Copy the official release, never arbitrary local loader plugins/configuration.
foreach($item in Get-ChildItem -LiteralPath $loader -Force) { Copy-Item -LiteralPath $item.FullName -Destination $payload -Recurse }
$plugins=Join-Path $payload 'BepInEx/plugins/UnityVRInjector'
$config=Join-Path $payload 'BepInEx/config'
$null=New-Item -ItemType Directory -Path $plugins,$config
Copy-Item -LiteralPath (Join-Path $repo 'src/bin/Release/net35/UnityVRInjector.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $repo 'build/UnityVRRenderBridge.dll'),(Join-Path $repo 'vendor/openvr_api.dll') -Destination $payload
[IO.File]::WriteAllText((Join-Path $config 'BepInEx.cfg'),"[Chainloader]`nHideManagerGameObject = true`n",[Text.UTF8Encoding]::new($false))
foreach($item in Get-ChildItem -LiteralPath $published -File) {
    if($item.Extension -in '.exe','.dll','.json') { Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $package 'controller') }
}
Copy-Item -LiteralPath (Join-Path $repo 'scripts/Deploy-Session.ps1'),(Join-Path $repo 'scripts/UGEVR.ps1') -Destination (Join-Path $package 'scripts')
foreach($name in @('development-distribution.md','injector-session-protocol.md','native-frame-contract.md','session-deployment.md','session-controller.md','profiles.md')) {
    $source=Join-Path $repo ('docs/'+$name)
    if(Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $package 'docs') }
}
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $repo 'vendor/BepInEx-LICENSE'),(Join-Path $repo 'vendor/Valve-OpenVR-LICENSE'),(Join-Path $repo 'vendor/OpenVR-commit.txt') -Destination (Join-Path $package 'vendor')
$dotnetRoot=Split-Path (Get-Command dotnet -CommandType Application).Source -Parent
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $package 'vendor/DotNetHost-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $package 'vendor/DotNetHost-ThirdPartyNotices.txt')
$instructions=@(
    'UGEVR development distribution','',
    'Status: experimental S1. Physical gameplay is unvalidated.',
    'Requirements: Windows x64, .NET 10 runtime, PowerShell 7 on PATH, compatible',
    'Mono Unity game, Visual C++ runtime, D3D11 and a configured OpenVR runtime.','',
    'Use controller/UGEVR.Controller.exe. Pass payload/ as --distribution-root and',
    'scripts/Deploy-Session.ps1 as --deployment-script. The controller, docs and',
    'metadata are not deployed to the game. See docs/development-distribution.md.','',
    'There is one UnityVRInjector.dll and one native bridge in this distribution.',
    'Game assemblies, PDBs, local evidence and Frame Testbench are excluded.',
    'Do not treat this development package as a passed physical compatibility gate.'
) -join "`n"
[IO.File]::WriteAllText((Join-Path $package 'README.txt'),$instructions+"`n",[Text.UTF8Encoding]::new($false))
function Scalar($value) {
    if($value -is [bool]) { return $value.ToString().ToLowerInvariant() }
    if($value -is [int] -or $value -is [long]) { return [string]$value }
    return ConvertTo-Json -InputObject ([string]$value) -Compress
}
$lines=[Collections.Generic.List[string]]::new()
$headers=[ordered]@{
    schema_version=1; package_version=$Version; source_repository='https://github.com/JakeGreen2145/UGEVR';
    source_revision=$revision; source_dirty=$dirty; created_utc=[DateTime]::UtcNow.ToString('o');
    stage='S1'; support_state='experimental'; physical_gameplay='unvalidated'; game_payload='payload';
    injector_version=([regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'src/Injector.cs')),'BepInPlugin\s*\(\s*\x22[^\x22]+\x22\s*,\s*\x22[^\x22]+\x22\s*,\s*\x22([^\x22]+)\x22').Groups[1].Value); native_contract_version=1; controller_schema_version=1;
    managed_target='net35'; reference_build=$ReferenceBuild; controller_target='net10.0-win-x64-framework-dependent';
    dotnet_sdk=(& dotnet --version).Trim(); msvc_compiler_version=(Get-Item -LiteralPath $tools.Compiler).VersionInfo.FileVersion;
    windows_sdk_version=($tools.Include -split ';' | Where-Object { $_ -match '\\Include\\([^\\]+)\\ucrt$' } | ForEach-Object { $Matches[1] });
    bepinex_version='5.4.23.5'; bepinex_archive_sha256=$loaderHash;
    bepinex_archive_url='https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip';
    openvr_revision=(Get-Content -LiteralPath (Join-Path $repo 'vendor/OpenVR-commit.txt')).Trim();
    ugevr_license='not-declared'; manifest_self_hash='excluded'
}
foreach($entry in $headers.GetEnumerator()) { $lines.Add($entry.Key+': '+(Scalar $entry.Value)) }
$lines.Add('source_files:')
foreach($source in $sourceEntries) {
    $lines.Add('  - path: '+(Scalar $source.Path)); $lines.Add('    sha256: '+(Scalar $source.Hash))
}
$lines.Add('files:')
foreach($file in Get-ChildItem -LiteralPath $package -Recurse -File -Force | Sort-Object FullName) {
    $relative=[IO.Path]::GetRelativePath($package,$file.FullName).Replace('\','/')
    $lines.Add('  - path: '+(Scalar $relative)); $lines.Add('    sha256: '+(Scalar (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()))
}
[IO.File]::WriteAllText((Join-Path $package 'distribution.yaml'),($lines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
[IO.Compression.ZipFile]::CreateFromDirectory($package,$archive)
[pscustomobject]@{ Version=$Version; SourceRevision=$revision; SourceDirty=$dirty; Directory=$package; Archive=$archive; ArchiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() }
