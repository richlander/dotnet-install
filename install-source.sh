#!/bin/sh
# Install dotnet-install from local source (developer workflow).
# Usage: ./install-source.sh
#
# Publishes a Native AOT binary from the local source tree and places
# it in ~/.dotnet/bin/. Runs `dotnet-install setup` to configure your
# shell PATH.
#
# Requires the .NET SDK.

set -eu

INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet/bin}"

main() {
    need_cmd dotnet
    need_cmd uname

    get_rid || return 1
    local _rid="$RETVAL"

    echo "=== Installing dotnet-install from source ==="

    # Publish Native AOT binary
    dotnet publish src/dotnet-install -c Release -r "$_rid" --nologo -v:q

    # Copy binary to install directory
    local _pub="artifacts/publish/dotnet-install/release_${_rid}"
    local _bin="${_pub}/dotnet-install"

    if [ ! -f "$_bin" ]; then
        err "binary not found at $_bin"
    fi

    mkdir -p "$INSTALL_DIR"
    cp "$_bin" "$INSTALL_DIR/dotnet-install"
    chmod +x "$INSTALL_DIR/dotnet-install"

    echo "  Installed to ${INSTALL_DIR}/dotnet-install"

    # Run setup to configure shell PATH.
    # Connect /dev/tty for interactive prompts when piped.
    local _need_tty=yes
    for arg in "$@"; do
        case "$arg" in
            -y) _need_tty=no ;;
        esac
    done

    if [ "$_need_tty" = "yes" ] && [ ! -t 0 ]; then
        if [ ! -t 1 ]; then
            err "unable to run interactively; use -y to accept defaults"
        fi
        "$INSTALL_DIR/dotnet-install" setup < /dev/tty
    else
        "$INSTALL_DIR/dotnet-install" setup
    fi
}

get_rid() {
    local _os _arch
    _os="$(uname -s)"
    _arch="$(uname -m)"

    case "$_os" in
        Linux)  _os="linux" ;;
        Darwin) _os="osx" ;;
        *)      err "unsupported OS: $_os" ;;
    esac

    case "$_arch" in
        aarch64 | arm64)                _arch="arm64" ;;
        x86_64 | x86-64 | x64 | amd64) _arch="x64" ;;
        *)                              err "unsupported architecture: $_arch" ;;
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

main "$@" || exit 1
