$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $repo 'downloads\BepInEx_win_x64_5.4.23.5.zip'
New-Item -ItemType Directory -Force -Path (Split-Path $archive -Parent) | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' -OutFile $archive
}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $repo 'vendor\BepInEx') -Force
Write-Output 'Prepared BepInEx 5.4.23.5. OpenVR SDK files are vendored in the repository.'
