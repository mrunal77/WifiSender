$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $Root "WifiSender.csproj"
$AppName = "WifiSender"
$Configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$Runtime = if ($env:RUNTIME) { $env:RUNTIME } else { "win-x64" }
$Version = if ($env:VERSION) { $env:VERSION } else {
    $VersionFile = Join-Path $Root "dist/version.txt"
    if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { "1.0.0" }
}
$PublishDir = Join-Path $Root "dist/publish/$Runtime"
$OutputDir = Join-Path $Root "dist"
$PublishedExe = Join-Path $PublishDir "$AppName.exe"
$OutputExe = Join-Path $OutputDir "$AppName-$Version-$Runtime.exe"

Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $OutputExe -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PublishDir, $OutputDir | Out-Null

dotnet publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $PublishDir

Copy-Item $PublishedExe $OutputExe -Force
Write-Host "Created $OutputExe"
