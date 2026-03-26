#!/bin/sh
# Install dotnet-install via dotnet tool (SDK workflow).
# Usage: ./install-tool.sh
#
# Installs dotnet-install as a .NET global tool and configures
# your shell PATH. Run `dotnet-install doctor --fix` later to
# graduate to a standalone binary in ~/.dotnet/bin/.
#
# Requires the .NET SDK.
#
# Environment variables:
#   DOTNET_TOOL_BIN      Override the install directory

set -eu

main() {
    need_cmd dotnet

    # Install as dotnet global tool (bootstrap)
    say "installing dotnet-install via dotnet tool..."
    dotnet tool install -g dotnet-install

    # Configure shell PATH only — graduation to standalone binary
    # happens later via `dotnet-install doctor --fix`
    say "configuring PATH..."
    dotnet-install doctor --fix --path
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
