param([string]$CompilerRoot,[string]$SdkRoot,[string]$SdkVersion)
$ErrorActionPreference = 'Stop'
if (-not $CompilerRoot) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ build tools or pass -CompilerRoot.' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'No Visual Studio C++ x64 toolchain was found.' }
    $CompilerRoot = (Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).FullName
}
if (-not $SdkRoot) {
    $SdkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
}
if (-not $SdkVersion) {
    $SdkVersion = (Get-ChildItem -LiteralPath (Join-Path $SdkRoot 'Include') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ucrt') } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).Name
}
$compiler = Join-Path $CompilerRoot 'bin\Hostx64\x64\cl.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw "Missing x64 compiler: $compiler" }
if (-not (Test-Path -LiteralPath (Join-Path $SdkRoot "Lib\$SdkVersion\um\x64"))) { throw 'The selected Windows SDK has no x64 libraries.' }
[pscustomobject]@{
    Compiler=$compiler
    Include="$CompilerRoot\include;$SdkRoot\Include\$SdkVersion\ucrt;$SdkRoot\Include\$SdkVersion\shared;$SdkRoot\Include\$SdkVersion\um"
    Lib="$CompilerRoot\lib\x64;$SdkRoot\Lib\$SdkVersion\ucrt\x64;$SdkRoot\Lib\$SdkVersion\um\x64"
}
