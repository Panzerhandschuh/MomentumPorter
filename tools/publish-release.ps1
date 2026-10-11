# Builds the release archives to share, one per platform: MomentumPorter-UI and MomentumPorter-CLI as single-file,
# framework-dependent (.NET 10 runtime required) executables, with bspPack and BSPConvert's folders next to them,
# zipped as artifacts/MomentumPorter-<runtime>-<version>.zip. Run from anywhere on Windows; needs the .NET 10 SDK.
# -Version (1.2.0) is the release's version, which the porter's updater compares with the latest GitHub release's tag
# (v1.2.0), so tag the release with it and attach the archives. Without it the build is a development build (0.0.0)
# that doesn't check for updates, and the archives are named after the commit. Only the Windows build updates itself.
param(
    [string] $Version = $null,
    [string] $Output = $null,
    [string[]] $Runtime = @("win-x64", "linux-x64")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "artifacts" }
if ($Version) {
    $Version = $Version.TrimStart("v")
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "-Version should look like 1.2.0" }
}
$label = if ($Version) { "v$Version" } else { (& git -C $root rev-parse --short HEAD).Trim() }

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Zips $folder as a top-level MomentumPorter folder. Zips made on Windows don't carry Unix permissions, so the Linux
# programs are marked executable here (rwxr-xr-x), or they'd need a chmod after unzipping.
function Write-Zip([string] $folder, [string] $archive, [string[]] $executables) {
    if (Test-Path $archive) { Remove-Item $archive -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $folder -Recurse -File) {
            $relative = $file.FullName.Substring($folder.Length + 1).Replace("\", "/")
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, "MomentumPorter/$relative", [System.IO.Compression.CompressionLevel]::Optimal)
            $mode = if ($executables -contains $relative) { 0x81ED } else { 0x81A4 }
            $entry.ExternalAttributes = $mode -shl 16
        }
    }
    finally {
        $zip.Dispose()
    }
}

foreach ($rid in $Runtime) {
    $windows = $rid.StartsWith("win")
    $folder = Join-Path $Output "MomentumPorter-$rid"
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }

    # Single-file keeps the managed dependencies inside each executable, so the folder is just the two programs and
    # the tools' folders. Native libraries (Skia, HarfBuzz, ANGLE, sourcepp) are bundled too and extracted on first run.
    $publishArgs = @(
        "-c", "Release", "-r", $rid, "--self-contained", "false",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=none", "-p:DebugSymbols=false",
        "-o", $folder, "--nologo", "-v", "q"
    )
    if ($Version) { $publishArgs += "-p:Version=$Version" }
    foreach ($project in "src\MomentumPorter.UI", "src\MomentumPorter.Cli") {
        & dotnet publish (Join-Path $root $project) @publishArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project ($rid)" }
    }

    # Every build gets both bspPack builds (tools/bspPack); keep this platform's
    $otherBspPack = Join-Path $folder $(if ($windows) { "bspPack\bspPack" } else { "bspPack\bspPack.exe" })
    if (Test-Path $otherBspPack) { Remove-Item $otherBspPack -Force }
    # BSPConvert's Windows DevIL and HLLib DLLs, which it doesn't load. Windows builds bundle them into the executable;
    # elsewhere they aren't native libraries, so they'd be left loose in Dependencies.
    if (-not $windows -and (Test-Path (Join-Path $folder "Dependencies"))) { Remove-Item (Join-Path $folder "Dependencies") -Recurse -Force }

    Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $folder

    $archive = Join-Path $Output "MomentumPorter-$rid-$label.zip"
    Write-Zip $folder $archive @("MomentumPorter-UI", "MomentumPorter-CLI", "bspPack/bspPack")
    Write-Host "Wrote $archive"
    Get-ChildItem $folder | Format-Table Name, Length -AutoSize
}
