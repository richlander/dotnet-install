#!/bin/sh
# Install dotnet-install from its published NuGet package.
# Usage: ./install-package.sh
#
# Builds the tool from source via `dotnet run` to bootstrap, then uses
# it to install the released NuGet package. Runs `dotnet-install setup`
# to configure your shell PATH.

set -eu

main() {
    need_cmd dotnet

    echo "=== Installing dotnet-install from NuGet ==="

    # Use dotnet run to install the published NuGet package
    dotnet run --project src/dotnet-install -- --package dotnet-install

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
