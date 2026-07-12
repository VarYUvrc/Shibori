param(
    [string]$Configuration = "Release",
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Shibori.csproj"

dotnet build $project --configuration $Configuration
$exe = Join-Path $root "bin\$Configuration\net8.0-windows\Shibori.exe"
if (-not (Test-Path $exe)) { throw "実行ファイルが見つかりません: $exe" }

if ($SelfTest) { & $exe --self-test } else { & $exe --diagnose }
$log = Join-Path $env:LOCALAPPDATA "Shibori\shibori.log"
Write-Host "診断ログ: $log"
Get-Content -Path $log -Tail 100
