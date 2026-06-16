#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=publish-common.sh
source "$SCRIPT_DIR/publish-common.sh"

APP_NAME="WifiSender"
BUNDLE_ID="com.wifisender.app"
RUNTIME="${RUNTIME:-osx-x64}"
PUBLISH_DIR="$PUBLISH_BASE/$RUNTIME"
APP_BUNDLE="$ROOT_DIR/dist/$APP_NAME.app"
DMG_ROOT="$ROOT_DIR/dist/dmg-root"
OUTPUT="$ROOT_DIR/dist/$APP_NAME-$VERSION-$RUNTIME.dmg"

if [ "$(uname)" != "Darwin" ]; then
    echo "macOS DMG packaging must run on macOS because hdiutil is required." >&2
    exit 1
fi

publish_runtime "$RUNTIME" "$PUBLISH_DIR"

rm -rf "$APP_BUNDLE" "$DMG_ROOT" "$OUTPUT"
mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources" "$DMG_ROOT"

cp "$PUBLISH_DIR/$APP_NAME" "$APP_BUNDLE/Contents/MacOS/$APP_NAME"
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"
cp "$ROOT_DIR/Assets/appicon.png" "$APP_BUNDLE/Contents/Resources/appicon.png"

cat > "$APP_BUNDLE/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIconFile</key>
    <string>appicon.png</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>CFBundleSupportedPlatforms</key>
    <array>
        <string>MacOSX</string>
    </array>
</dict>
</plist>
EOF

cp -R "$APP_BUNDLE" "$DMG_ROOT/"
ln -s /Applications "$DMG_ROOT/Applications"
hdiutil create -volname "$APP_NAME $VERSION" -srcfolder "$DMG_ROOT" -ov -format UDZO "$OUTPUT"

echo "Created $OUTPUT"
