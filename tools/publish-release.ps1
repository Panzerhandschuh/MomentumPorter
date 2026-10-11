# Builds the release archive to share: MomentumPorter-UI.exe and MomentumPorter-CLI.exe as single-file,
# framework-dependent (.NET 10 runtime required) win-x64 executables, with bspPack and BSPConvert's folders next to
# them, 7-zipped as artifacts/MomentumPorter-win-x64-<commit>.7z. Run from anywhere; needs the .NET 10 SDK and 7-Zip.
param(
    [string] $Output = $null,
    [string] $SevenZip = "$env:ProgramFiles\7-Zip\7z.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "artifacts" }
$folder = Join-Path $Output "MomentumPorter"
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }

# Single-file keeps the managed dependencies inside each executable, so the folder is just the two programs and the
# tools' folders. Native libraries (Skia, HarfBuzz, ANGLE, sourcepp) are bundled too and extracted on first run.
$publishArgs = @(
    "-c", "Release", "-r", "win-x64", "--self-contained", "false",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none", "-p:DebugSymbols=false",
    "-o", $folder, "--nologo", "-v", "q"
)
foreach ($project in "src\MomentumPorter.UI", "src\MomentumPorter.Cli") {
    & dotnet publish (Join-Path $root $project) @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $folder

$commit = (& git -C $root rev-parse --short HEAD).Trim()
$archive = Join-Path $Output "MomentumPorter-win-x64-$commit.7z"
if (Test-Path $archive) { Remove-Item $archive -Force }
if (-not (Test-Path $SevenZip)) { throw "7-Zip wasn't found at $SevenZip; pass -SevenZip" }
Push-Location $Output
try {
    & $SevenZip a -t7z -mx=7 $archive MomentumPorter | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z failed" }
}
finally {
    Pop-Location
}

Write-Host "Wrote $archive"
Get-ChildItem $folder | Format-Table Name, Length -AutoSize
