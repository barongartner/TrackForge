#!/bin/bash
#
# Builds TrackForge.app universal and wraps it in a release dmg.
#   ./package.sh   →  dist/TrackForge-<version>.dmg
#
set -euo pipefail
cd "$(dirname "$0")"

APP="TrackForge.app"
DIST="dist"
STAGE=".build-dmg"

VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Info.plist)"
DMG="$DIST/TrackForge-$VERSION.dmg"

./build.sh

echo "Staging…"
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE" "$DIST"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

echo "Building $DMG…"
hdiutil create \
    -volname "TrackForge $VERSION" \
    -srcfolder "$STAGE" \
    -fs HFS+ \
    -format UDZO \
    -ov \
    "$DMG" >/dev/null

rm -rf "$STAGE"

# Ad-hoc sign the disk image too, so its contents cannot be swapped after the
# fact without the signature breaking.
codesign --force --sign - "$DMG"

echo
echo "Verifying…"
codesign --verify --deep --strict --verbose=1 "$APP" 2>&1 | sed 's/^/  /'
echo "  archs: $(lipo -archs "$APP/Contents/MacOS/TrackForge")"
echo "  size:  $(du -h "$DMG" | cut -f1)"
echo
echo "Done → $DMG"
echo
echo "Not notarised: that needs a Developer ID certificate, which needs a paid"
echo "Apple Developer membership. First launch on another Mac is right-click → Open."
