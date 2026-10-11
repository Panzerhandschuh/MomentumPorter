# Builds the release archive to share: MomentumPorter-UI.exe and MomentumPorter-CLI.exe as single-file,
# framework-dependent (.NET 10 runtime required) win-x64 executables, with bspPack and BSPConvert's folders next to
# them, 7-zipped as artifacts/MomentumPorter-win-x64-<version>.7z. Run from anywhere; needs the .NET 10 SDK and 7-Zip.
# -Version (1.2.0) is the release's version, which the porter's updater compares with the latest GitHub release's tag
# (v1.2.0), so tag the release with it and attach the archive. Without it the build is a development build (0.0.0)
# that doesn't check for updates, and the archive is named after the commit.
param(
    [string] $Version = $null,
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
if ($Version) {
    $Version = $Version.TrimStart("v")
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "-Version should look like 1.2.0" }
    $publishArgs += "-p:Version=$Version"
}
foreach ($project in "src\MomentumPorter.UI", "src\MomentumPorter.Cli") {
    & dotnet publish (Join-Path $root $project) @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $folder

$label = if ($Version) { "v$Version" } else { (& git -C $root rev-parse --short HEAD).Trim() }
$archive = Join-Path $Output "MomentumPorter-win-x64-$label.7z"
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
