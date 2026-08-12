#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=publish-common.sh
source "$SCRIPT_DIR/publish-common.sh"

APP_NAME="WifiSender"
RUNTIME="linux-x64"
PUBLISH_DIR="$PUBLISH_BASE/$RUNTIME"
APPIMAGE_DIR="$ROOT_DIR/dist/appimage/AppDir"
TOOLS_DIR="$ROOT_DIR/dist/tools"
OUTPUT="$ROOT_DIR/dist/$APP_NAME-$VERSION-linux-x64.AppImage"

publish_runtime "$RUNTIME" "$PUBLISH_DIR"

rm -rf "$APPIMAGE_DIR" "$OUTPUT"
mkdir -p \
    "$APPIMAGE_DIR/usr/bin" \
    "$APPIMAGE_DIR/usr/share/icons/hicolor/256x256/apps"

cp "$PUBLISH_DIR/$APP_NAME" "$APPIMAGE_DIR/usr/bin/$APP_NAME"
chmod +x "$APPIMAGE_DIR/usr/bin/$APP_NAME"

cp "$ROOT_DIR/Assets/appicon.png" "$APPIMAGE_DIR/wifisender.png"
cp "$ROOT_DIR/Assets/appicon.png" "$APPIMAGE_DIR/usr/share/icons/hicolor/256x256/apps/wifisender.png"

cat > "$APPIMAGE_DIR/$APP_NAME.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Cross-platform file transfer over WiFi or LAN
Exec=usr/bin/$APP_NAME
Icon=wifisender
Terminal=false
Categories=Network;FileTransfer;
EOF

cat > "$APPIMAGE_DIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/WifiSender" "$@"
EOF
chmod +x "$APPIMAGE_DIR/AppRun"

APPIMAGETOOL="${APPIMAGETOOL:-}"
if [ -z "$APPIMAGETOOL" ]; then
    APPIMAGETOOL="$TOOLS_DIR/appimagetool-x86_64.AppImage"
    mkdir -p "$TOOLS_DIR"

    if [ ! -x "$APPIMAGETOOL" ]; then
        if command -v curl >/dev/null 2>&1; then
            curl -L --fail -o "$APPIMAGETOOL" "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
        elif command -v wget >/dev/null 2>&1; then
            wget -O "$APPIMAGETOOL" "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
        else
            echo "Install appimagetool or set APPIMAGETOOL to the appimagetool executable." >&2
            exit 1
        fi
        chmod +x "$APPIMAGETOOL"
    fi
fi

(
    cd "$APPIMAGE_DIR"
    ARCH=x86_64 "$APPIMAGETOOL" . "$OUTPUT"
)

echo "Created $OUTPUT"
