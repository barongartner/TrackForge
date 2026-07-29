#!/bin/bash
#
# Regenerates TrackForge.icns. Only needed if the mark changes — the .icns is
# committed, so a normal build does not run this.
set -euo pipefail
cd "$(dirname "$0")/.."

BUILD=".build-tools"
mkdir -p "$BUILD"

swiftc -O -swift-version 5 -framework AppKit \
    Tools/MakeIcon/main.swift -o "$BUILD/makeicon"

"$BUILD/makeicon" TrackForge.icns
