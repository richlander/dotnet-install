#!/bin/sh
# Install script for dotnet-install.
# Usage: curl --proto '=https' --tlsv1.2 -sSf https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh
#
# Uses an existing dotnet-install (if available) or installs it as a
# temporary global tool, then builds from the local source tree into
# ~/.dotnet/bin/. Runs `dotnet-install setup` to configure your shell PATH.

set -eu

main() {
    need_cmd dotnet

    echo "=== Installing dotnet-install ==="

    _used_global_tool=no

    # Use existing dotnet-install if available; otherwise install the global tool temporarily
    if ! command -v dotnet-install > /dev/null 2>&1; then
        echo "Installing dotnet-install global tool..."
        dotnet tool install -g dotnet-install
        _used_global_tool=yes
    fi

    # Install from the local source tree (current branch, as-is)
    dotnet-install src/dotnet-install

    # Remove the temporary global tool if we installed it
    if [ "$_used_global_tool" = "yes" ]; then
        echo "Removing temporary global tool..."
        dotnet tool uninstall -g dotnet-install
    fi

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
