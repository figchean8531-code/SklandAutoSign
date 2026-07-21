param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot "SklandAutoSign.csproj"
$OutputDir = Join-Path $ProjectRoot "dist"
$ExePath = Join-Path $OutputDir "SklandAutoSign.exe"
$ChecksumPath = Join-Path $OutputDir "SHA256SUMS.txt"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK is not installed or dotnet is not available in PATH."
}

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

& dotnet publish $ProjectFile `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path $ExePath)) {
    throw "Build completed but expected executable was not found: $ExePath"
}

$Hash = (Get-FileHash -Algorithm SHA256 $ExePath).Hash.ToUpperInvariant()
"$Hash  SklandAutoSign.exe" | Set-Content -Path $ChecksumPath -Encoding ascii

Write-Host "Build completed:"
Write-Host "  $ExePath"
Write-Host "  $ChecksumPath"
Write-Host "SHA256: $Hash"
