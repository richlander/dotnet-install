#!/bin/sh
# Install script for dotnet-install.
# Usage: curl --proto '=https' --tlsv1.2 -sSf https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh
#
# Downloads a pre-built Native AOT binary from GitHub Releases,
# places it in ~/.dotnet/bin/, and runs `dotnet-install setup`
# to configure your shell PATH.
#
# No .NET SDK required.
#
# Environment variables:
#   DOTNET_INSTALL_FEED  Override the download base URL
#   DOTNET_INSTALL_DIR   Override the install directory

set -u

FEED="${DOTNET_INSTALL_FEED:-https://github.com/richlander/dotnet-install/releases/download}"
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet/bin}"

main() {
    downloader --check
    need_cmd uname
    need_cmd tar
    need_cmd mktemp
    need_cmd chmod
    need_cmd mkdir
    need_cmd rm

    get_rid || return 1
    local _rid="$RETVAL"
    assert_nz "$_rid" "rid"

    local _version
    _version="$(get_latest_version)" || return 1
    assert_nz "$_version" "version"

    local _url="${FEED}/v${_version}/dotnet-install-${_rid}.tar.gz"

    local _dir
    _dir="$(ensure mktemp -d)"
    local _archive="${_dir}/dotnet-install.tar.gz"

    say "downloading dotnet-install ${_version} (${_rid})"

    ensure mkdir -p "$_dir"
    ensure downloader "$_url" "$_archive" "$_rid"
    ensure tar -xzf "$_archive" -C "$_dir"

    local _bin="${_dir}/dotnet-install"
    ensure chmod u+x "$_bin"

    if [ ! -x "$_bin" ]; then
        err "cannot execute $_bin (may be noexec mount)"
    fi

    # Place binary in install directory
    ensure mkdir -p "$INSTALL_DIR"
    rm -f "$INSTALL_DIR/dotnet-install"
    ensure cp "$_bin" "$INSTALL_DIR/dotnet-install"
    ensure chmod +x "$INSTALL_DIR/dotnet-install"

    say "installed to ${INSTALL_DIR}/dotnet-install"

    # Clean up archive
    rm -rf "$_dir"

    # Run setup to configure shell PATH.
    # SetupCommand handles non-interactive mode automatically
    # (auto-writes rc file when stdin is redirected).
    "$INSTALL_DIR/dotnet-install" setup
}

get_latest_version() {
    local _url="https://github.com/richlander/dotnet-install/releases/latest"
    local _redirect

    if command -v curl > /dev/null 2>&1; then
        _redirect="$(curl --proto '=https' --tlsv1.2 -sI -o /dev/null -w '%{url_effective}' -L "$_url")"
    elif command -v wget > /dev/null 2>&1; then
        _redirect="$(wget --spider --max-redirect=5 -S "$_url" 2>&1 | grep -i 'Location:' | tail -1 | awk '{print $2}' | tr -d '\r')"
    else
        err "need curl or wget to resolve latest version"
    fi

    # Extract version from redirect URL: .../tag/v0.4.2 → 0.4.2
    local _ver
    _ver="$(echo "$_redirect" | sed 's|.*/v||')"
    if [ -z "$_ver" ]; then
        err "could not determine latest version"
    fi

    printf '%s' "$_ver"
}

get_rid() {
    local _os _arch
    _os="$(uname -s)"
    _arch="$(uname -m)"

    case "$_os" in
        Linux)
            _os="linux"
            ;;
        Darwin)
            _os="osx"
            ;;
        *)
            err "unsupported OS: $_os"
            ;;
    esac

    case "$_arch" in
        aarch64 | arm64)
            _arch="arm64"
            ;;
        x86_64 | x86-64 | x64 | amd64)
            _arch="x64"
            ;;
        *)
            err "unsupported architecture: $_arch"
            ;;
    esac

    RETVAL="${_os}-${_arch}"
}

say() {
    printf 'dotnet-install: %s\n' "$1" 1>&2
}

err() {
    say "error: $1"
    exit 1
}

need_cmd() {
    if ! command -v "$1" > /dev/null 2>&1; then
        err "need '$1' (command not found)"
    fi
}

assert_nz() {
    if [ -z "$1" ]; then err "assert_nz $2"; fi
}

ensure() {
    if ! "$@"; then err "command failed: $*"; fi
}

downloader() {
    local _dld
    if command -v curl > /dev/null 2>&1; then
        _dld=curl
    elif command -v wget > /dev/null 2>&1; then
        _dld=wget
    else
        _dld='curl or wget'
    fi

    if [ "$1" = --check ]; then
        need_cmd "$_dld"
    elif [ "$_dld" = curl ]; then
        curl --proto '=https' --tlsv1.2 \
            --silent --show-error --fail --location \
            --retry 3 \
            "$1" --output "$2" || {
            err "download failed for platform '$3'; binary may not be available for this platform"
        }
    elif [ "$_dld" = wget ]; then
        wget --https-only --secure-protocol=TLSv1_2 \
            "$1" -O "$2" || {
            err "download failed for platform '$3'; binary may not be available for this platform"
        }
    fi
}

main "$@" || exit 1
