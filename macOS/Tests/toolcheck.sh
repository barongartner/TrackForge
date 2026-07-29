#!/bin/bash
#
# Compiles and runs the checks that need yt-dlp and ffmpeg present. Installs them
# on first run, into TrackForge's own tools folder.
#   ./Tests/toolcheck.sh
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

swiftc -O -swift-version 5 -target "${ARCH}-apple-macosx13.0" \
    -framework AppKit -framework AVFoundation -framework Accelerate \
    -framework AudioToolbox -framework UniformTypeIdentifiers \
    "${CORE[@]}" Tests/ToolCheck/main.swift -o "$BUILD/toolcheck"

exec "$BUILD/toolcheck"
