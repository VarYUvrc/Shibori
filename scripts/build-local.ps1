param(
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Shibori\Shibori.csproj"
$output = Join-Path $root "artifacts\latest"

if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet restore (Join-Path $root "Shibori.sln") --runtime win-x64
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $output

$exe = Join-Path $output "Shibori.exe"
if (-not (Test-Path $exe)) { throw "ビルド成果物が見つかりません: $exe" }
Write-Host "最新ビルド: $exe"
if (-not $NoLaunch) { Start-Process -FilePath $exe }
