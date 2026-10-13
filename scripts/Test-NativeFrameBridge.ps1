param([string]$CompilerRoot,[string]$SdkRoot,[string]$SdkVersion)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$tools=& (Join-Path $PSScriptRoot 'Resolve-BuildTools.ps1') -CompilerRoot $CompilerRoot -SdkRoot $SdkRoot -SdkVersion $SdkVersion
$oldInclude=$env:INCLUDE
$oldLib=$env:LIB
Push-Location $repo
try {
    $env:INCLUDE=$tools.Include
    $env:LIB=$tools.Lib
    $output=Join-Path $repo 'build\native-contract'
    New-Item -ItemType Directory -Force $output | Out-Null
    # Build the shipping bridge as well as the runtime-fault variant.
    & $tools.Compiler /nologo /LD /EHsc /std:c++17 /MD /O2 'src\RenderBridge.cpp' 'vendor\openvr_api.lib' "/Fe$output\UnityVRRenderBridge.dll" "/Fo$output\RenderBridge.obj" /link /MACHINE:X64
    if($LASTEXITCODE -ne 0) { throw 'Shipping native bridge compile failed.' }
    & $tools.Compiler /nologo /LD /EHsc /std:c++17 /MD /O2 /DUGEVR_NATIVE_CONTRACT_TEST 'src\RenderBridge.cpp' 'vendor\openvr_api.lib' "/Fe$output\FrameBridgeFaults.dll" "/Fo$output\FrameBridgeFaults.obj" /link /MACHINE:X64
    if($LASTEXITCODE -ne 0) { throw 'Fault-injection native bridge compile failed.' }
    & $tools.Compiler /nologo /EHsc /std:c++17 /MD /O2 'tests\native\FrameBridgeContract.cpp' 'd3d11.lib' "/Fe$output\FrameBridgeContract.exe" "/Fo$output\FrameBridgeContract.obj" /link /MACHINE:X64
    if($LASTEXITCODE -ne 0) { throw 'External ABI contract check compile failed.' }
    Copy-Item -LiteralPath 'vendor\openvr_api.dll' -Destination $output -Force
    & (Join-Path $output 'FrameBridgeContract.exe') (Join-Path $output 'FrameBridgeFaults.dll')
    if($LASTEXITCODE -ne 0) { throw 'Native frame contract checks failed.' }
    Get-FileHash -LiteralPath (Join-Path $output 'UnityVRRenderBridge.dll') -Algorithm SHA256
} finally {
    $env:INCLUDE=$oldInclude
    $env:LIB=$oldLib
    Pop-Location
}
