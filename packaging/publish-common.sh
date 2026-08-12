#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
if [ -n "${VERSION:-}" ]; then
    VERSION_FILE_RESOLVED_VERSION=""
else
    VERSION_FILE_RESOLVED_VERSION="$(cat "$ROOT_DIR/dist/version.txt" 2>/dev/null || true)"
fi
VERSION="${VERSION:-${VERSION_FILE_RESOLVED_VERSION:-1.0.0}}"
PUBLISH_BASE="${PUBLISH_BASE:-$ROOT_DIR/dist/publish}"

publish_runtime() {
    local runtime="$1"
    local output_dir="$2"

    rm -rf "$output_dir"
    mkdir -p "$output_dir"

    dotnet publish "$ROOT_DIR/WifiSender.csproj" \
        -c "$CONFIGURATION" \
        -r "$runtime" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:PublishTrimmed=false \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o "$output_dir"
}
