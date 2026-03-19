#!/bin/sh
# Install dotnet-install from local source (developer workflow).
# Usage: ./install-source.sh
#
# Builds dotnet-install from the local source tree via `dotnet run` and
# installs it into ~/.dotnet/bin/. Runs `dotnet-install setup` to
# configure your shell PATH.
#
# Requires the .NET SDK.

set -eu

main() {
    need_cmd dotnet

    echo "=== Installing dotnet-install ==="

    # Install from the local source tree using dotnet run (no global tool needed)
    dotnet run --project src/dotnet-install -- src/dotnet-install

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
