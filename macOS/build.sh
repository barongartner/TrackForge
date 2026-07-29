#!/bin/bash
#
# Build TrackForge.app from the Swift sources — no Xcode project needed.
#   ./build.sh           universal binary (Intel + Apple silicon)
#   ./build.sh --native  this machine's architecture only, for a faster loop
#   open TrackForge.app  run it
#
set -euo pipefail
cd "$(dirname "$0")"

APP="TrackForge.app"
BIN="TrackForge"
BUILD=".build"
DEPLOYMENT="13.0"

if [ "${1:-}" = "--native" ]; then
    ARCHS=("$(uname -m)")
else
    ARCHS=(x86_64 arm64)
fi

rm -rf "$APP" "$BUILD"
mkdir -p "$BUILD"

SLICES=()
for arch in "${ARCHS[@]}"; do
    echo "Compiling ${arch}…"
    swiftc -O -swift-version 5 -target "${arch}-apple-macosx${DEPLOYMENT}" \
        -framework SwiftUI -framework AppKit -framework AVFoundation \
        -framework Accelerate -framework AudioToolbox \
        -framework UniformTypeIdentifiers \
        Sources/*.swift -o "$BUILD/$BIN-$arch"
    SLICES+=("$BUILD/$BIN-$arch")
done

if [ "${#SLICES[@]}" -gt 1 ]; then
    echo "Merging into a universal binary…"
    lipo -create "${SLICES[@]}" -output "$BUILD/$BIN"
else
    mv "${SLICES[0]}" "$BUILD/$BIN"
fi

echo "Assembling $APP…"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BUILD/$BIN" "$APP/Contents/MacOS/$BIN"
cp Info.plist "$APP/Contents/Info.plist"
[ -f TrackForge.icns ] && cp TrackForge.icns "$APP/Contents/Resources/TrackForge.icns" || true

# Ad-hoc signature. A Developer ID would let this be notarised, which needs a paid
# Apple Developer membership; without one, ad-hoc is the strongest option and
# users get one right-click → Open on first launch. See README.
codesign --force --deep --sign - --timestamp=none "$APP"

echo "Done → $APP  ($(lipo -archs "$APP/Contents/MacOS/$BIN"))"
