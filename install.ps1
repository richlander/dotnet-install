# Install script for dotnet-install (Windows).
# Usage: irm https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.ps1 | iex
#
# Downloads a pre-built Native AOT binary from GitHub Releases,
# places it in ~/.dotnet/bin/, and runs `dotnet-install setup`
# to configure your PATH.
#
# Environment variables:
#   DOTNET_INSTALL_FEED  Override the download base URL
#   DOTNET_INSTALL_DIR   Override the install directory

$ErrorActionPreference = "Stop"

$feed = if ($env:DOTNET_INSTALL_FEED) {
    $env:DOTNET_INSTALL_FEED
} else {
    "https://github.com/richlander/dotnet-install/releases/download"
}

$installDir = if ($env:DOTNET_INSTALL_DIR) {
    $env:DOTNET_INSTALL_DIR
} else {
    Join-Path $HOME ".dotnet" "bin"
}

$version = "0.2.2"
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$rid = if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { "win-arm64" } else { "win-x64" }
$url = "$feed/v$version/dotnet-install-$rid.zip"

function New-TempFolder {
    $t = Join-Path ([System.IO.Path]::GetTempPath()) `
        "dotnet-install-$([System.IO.Path]::GetRandomFileName())"
    New-Item -ItemType Directory -Path $t | Out-Null
    return $t
}

Write-Host "dotnet-install: downloading v$version ($rid)"

$dir = New-TempFolder
$archive = Join-Path $dir "dotnet-install.zip"
$extracted = Join-Path $dir "extracted"

try {
    Invoke-WebRequest -Uri $url -OutFile $archive
    Expand-Archive $archive -DestinationPath $extracted

    $bin = Join-Path $extracted "dotnet-install.exe"
    if (-not (Test-Path $bin)) {
        throw "dotnet-install.exe not found in archive"
    }

    # Place binary in install directory
    New-Item -ItemType Directory -Path $installDir `
        -Force | Out-Null
    $dest = Join-Path $installDir "dotnet-install.exe"
    Copy-Item $bin $dest -Force

    Write-Host "dotnet-install: installed to $dest"

    # Run setup to configure PATH
    & $dest setup
}
finally {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}
