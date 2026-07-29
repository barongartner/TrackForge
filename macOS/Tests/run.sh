#!/bin/bash
#
# Compiles the non-UI half of TrackForge together with main.swift and runs it.
#   ./Tests/run.sh
#
# The UI sources are left out because App.swift owns @main, which would collide
# with the test harness's own top-level code.
set -euo pipefail
cd "$(dirname "$0")/.."

ARCH="$(uname -m)"
BUILD=".build-tests"
mkdir -p "$BUILD"

CORE=(
    Sources/Track.swift
    Sources/MatchCandidate.swift
    Sources/AppConfig.swift
    Sources/NameFormatter.swift
    Sources/ProcessRunner.swift
    Sources/ID3.swift
    Sources/AudioProperties.swift
    Sources/TagService.swift
    Sources/ToolInstaller.swift
    Sources/YtDlp.swift
    Sources/MetadataClient.swift
    Sources/AudioAnalyzer.swift
    Sources/DjayImporter.swift
)

swiftc -swift-version 5 -target "${ARCH}-apple-macosx13.0" \
    -framework AppKit -framework AVFoundation -framework Accelerate \
    -framework AudioToolbox -framework UniformTypeIdentifiers \
    "${CORE[@]}" Tests/main.swift -o "$BUILD/selftest"

exec "$BUILD/selftest"
