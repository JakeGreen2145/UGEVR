param([string]$CompilerRoot,[string]$SdkRoot,[string]$SdkVersion)
$ErrorActionPreference = 'Stop'
$driverTaskRoot = $PSScriptRoot
$buildTools = & (Join-Path $PSScriptRoot '..\..\scripts\Resolve-BuildTools.ps1') -CompilerRoot $CompilerRoot -SdkRoot $SdkRoot -SdkVersion $SdkVersion
$driverPosePath = (Join-Path $driverTaskRoot 'pose.txt').Replace('\','/')
('#pragma once' + "`n" + '#define TEST_HMD_POSE_PATH "' + $driverPosePath + '"' + "`n") | Set-Content -Encoding ascii (Join-Path $driverTaskRoot 'source/pose_path.h')
$oldInclude=$env:INCLUDE
$oldLib=$env:LIB
$env:INCLUDE=$buildTools.Include
$env:LIB=$buildTools.Lib
$driverOutput = Join-Path $driverTaskRoot 'driver/bin/win64/driver_unityvrtest.dll'
New-Item -ItemType Directory -Force -Path (Split-Path $driverOutput -Parent) | Out-Null
try {
    & $buildTools.Compiler /nologo /std:c++17 /EHsc /MD /O2 /W4 /LD /DWIN32 /D_WINDOWS "/I$driverTaskRoot/vendor" "/I$driverTaskRoot/source" (Join-Path $driverTaskRoot 'source/test_hmd.cpp') "/Fo$driverTaskRoot/driver/bin/win64/test_hmd.obj" /link "/OUT:$driverOutput" "/IMPLIB:$driverTaskRoot/driver/bin/win64/driver_unityvrtest.lib"
    if ($LASTEXITCODE -ne 0) { throw "Diagnostic HMD driver build failed with exit $LASTEXITCODE" }
} finally { $env:INCLUDE=$oldInclude; $env:LIB=$oldLib }
Get-Item -LiteralPath $driverOutput | Select-Object FullName,Length,LastWriteTime
