#!/bin/sh
# Install dotnet-install via dotnet tool (SDK workflow).
# Usage: ./install-tool.sh
#
# Installs dotnet-install as a .NET global tool, then graduates it to
# a standalone binary in ~/.dotnet/bin/ with full NuGet pedigree.
# The dotnet tool scaffolding is removed automatically.
#
# Requires the .NET SDK.
#
# Environment variables:
#   DOTNET_INSTALL_DIR   Override the install directory

set -eu

main() {
    need_cmd dotnet

    # Install as dotnet global tool (bootstrap)
    say "installing dotnet-install via dotnet tool..."
    dotnet tool install -g dotnet-install

    # Graduate to standalone binary and remove dotnet tool
    say "graduating to standalone install..."
    dotnet-install doctor
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
