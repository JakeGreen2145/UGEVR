param([string]$GameManaged = $env:UGEVR_GAME_MANAGED,[string]$CompilerRoot,[string]$SdkRoot,[string]$SdkVersion,[switch]$DistributionBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $GameManaged -or -not (Test-Path -LiteralPath (Join-Path $GameManaged 'UnityEngine.CoreModule.dll'))) {
    throw 'Pass -GameManaged pointing to the compatible Unity game''s Managed directory.'
}
$buildTools = & (Join-Path $PSScriptRoot 'Resolve-BuildTools.ps1') -CompilerRoot $CompilerRoot -SdkRoot $SdkRoot -SdkVersion $SdkVersion
$oldInclude = $env:INCLUDE
$oldLib = $env:LIB
Push-Location $repo
try {
    $buildArgs = @('build','src\UnityVRInjector.csproj','-c','Release')
    if ($GameManaged) { $buildArgs += "-p:GameManaged=$GameManaged" }
    if ($DistributionBuild) {
        # Do not embed the developer's PDB/source path in a shipped DLL.
        $buildArgs += '-p:DebugType=None','-p:DebugSymbols=false',"-p:PathMap=$repo=/_/ugevr"
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    $env:INCLUDE = $buildTools.Include
    $env:LIB = $buildTools.Lib
    New-Item -ItemType Directory -Force 'build' | Out-Null
    & $buildTools.Compiler /nologo /LD /EHsc /std:c++17 /MD /O2 'src\RenderBridge.cpp' 'vendor\openvr_api.lib' /Febuild\UnityVRRenderBridge.dll /Fobuild\RenderBridge.obj /link /MACHINE:X64
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
} finally { $env:INCLUDE=$oldInclude; $env:LIB=$oldLib; Pop-Location }
