#requires -Version 7.0
param([Parameter(ValueFromRemainingArguments)][string[]]$ControllerArguments)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packaged = Join-Path $root 'controller/UGEVR.Controller.dll'
$development = Join-Path $root 'controller/UGEVR.Controller/bin/Release/net10.0/UGEVR.Controller.dll'
if (Test-Path -LiteralPath $packaged -PathType Leaf) { $controller = $packaged }
elseif (Test-Path -LiteralPath $development -PathType Leaf) { $controller = $development }
else { throw 'Build controller/UGEVR.Controller in Release or use an extracted development distribution.' }
& dotnet $controller @ControllerArguments
exit $LASTEXITCODE
