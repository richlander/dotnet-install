#!/bin/sh
# Install script for dotnet-install.
# Usage: curl --proto '=https' --tlsv1.2 -sSf https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh
#
# Installs dotnet-install as a temporary global tool, uses it to install
# itself to ~/.dotnet/bin/ from source, then removes the global tool.
# Runs `dotnet-install setup` to configure your shell PATH.

set -eu

main() {
    need_cmd dotnet
    need_cmd git

    echo "=== Installing dotnet-install ==="

    # Install dotnet-install as a temporary global tool
    if ! dotnet tool list -g 2>/dev/null | grep -q dotnet-install; then
        echo "Installing dotnet-install global tool..."
        dotnet tool install -g dotnet-install
    fi

    # Use the global tool to install itself from source to ~/.dotnet/bin/
    dotnet-install --github richlander/dotnet-install

    # Remove the global tool — ~/.dotnet/bin/ copy is the real one now
    echo "Removing temporary global tool..."
    dotnet tool uninstall -g dotnet-install

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
        "$HOME/.dotnet/bin/dotnet-install" setup < /dev/tty
    else
        "$HOME/.dotnet/bin/dotnet-install" setup
    fi
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
