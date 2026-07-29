# TrackForge for macOS

The Mac build of TrackForge. Same app, rewritten as a native SwiftUI application —
no .NET, no Electron, no Xcode project, no package dependencies.

```
./build.sh          compile TrackForge.app
open TrackForge.app run it
./Tests/run.sh      run the self-test harness
```

Requires macOS 13 or later. Builds with the Swift toolchain that ships with Xcode
or the Command Line Tools; nothing else to install.

---

## What's the same

Everything the user touches. The four pages, the grab pipeline, the scoring, the
merge rules, the Title Case convention, the filename patterns, the 45-point match
threshold, the palette — all ported across from the Windows build rather than
reinvented, so a library maintained on either machine looks identical.

## What's different, and why

**Tags are read and written by hand.** Windows leans on TagLib#. There is no Swift
equivalent worth a dependency, and shelling out to ffmpeg for tag writes gives no
control over which frames land — ffmpeg quietly demotes anything it doesn't
recognise to a `TXXX` frame, which loses `TKEY` and `TSRC`. `ID3.swift` reads
v2.2/v2.3/v2.4 and writes v2.3, the version Rekordbox, Serato and djay all agree
on, and drops any ID3v1 trailer on the way out. Non-MP3 containers are read
through AudioToolbox and written through ffmpeg, which is already a hard
dependency.

**MP3 duration and bitrate are parsed directly.** A scan opens every file in the
folder, and building an `AVAsset` per file costs milliseconds each — enough to
turn a ten-thousand-track scan into a coffee break. One MPEG frame header plus the
Xing/VBRI header when there is one is exact for CBR and close enough for VBR.

**The FFT runs on Accelerate.** Same algorithm as the Windows build — spectral
flux autocorrelation for tempo, Krumhansl-Kessler chroma correlation for key — but
vDSP does the transform. A four-minute track lands in about three seconds instead
of eight, and most of that is still ffmpeg decoding.

**The tools come from different places.** yt-dlp ships an official standalone
universal binary for macOS. ffmpeg comes from `eugeneware/ffmpeg-static`, picked
per architecture: evermeet.cx ships a newer build but Intel-only, which on Apple
silicon would quietly depend on Rosetta being installed. Both are downloaded into
`~/Library/Application Support/TrackForge/tools`, made executable, and given an
ad-hoc signature — Apple silicon refuses to execute an unsigned arm64 binary
outright.

**An installed ffmpeg wins over a downloaded one.** `PATH` for an app launched
from Finder does not include Homebrew, so `/opt/homebrew/bin`, `/usr/local/bin`
and `/opt/local/bin` are searched explicitly. Order is: an explicit path in
Settings, then TrackForge's own tools folder, then those directories, then `PATH`.

**Windows Media Player's mess is gone.** `RemoveBlankFolderArt` and
`RemoveBlankMediaPlayerCache` existed to clean up the black `Folder.jpg`
placeholders WMP leaves behind. Nothing on macOS does that. **Repair tags** stays,
because rewriting as clean v2.3 with no v1 trailer is still what fixes a genre
showing up as a bare number in DJ software.

**djay is looked for in Mac locations.** `~/Music/djay`, the library folder
itself, and the sandboxed App Store containers under `~/Library/Containers`.
Reading another app's container needs Full Disk Access; without it the open simply
fails and the scan carries on with no djay data, which is a perfectly fine
outcome.

**Defaults point at `~/Music`** rather than `F:\Music`.

---

## Layout

```
Sources/
  App.swift             @main, the window, the top bar, page switching
  Theme.swift           the palette and metrics
  Controls.swift        the six controls that carry the look

  Track.swift           the model
  MatchCandidate.swift  one result from an online source
  AppConfig.swift       settings, as JSON in Application Support
  JobQueue.swift        bounded-concurrency background work
  ForgeService.swift    owns config, the queue and every service

  ID3.swift             hand-rolled ID3v2 reader and writer
  TagService.swift      tags + artwork, over ID3 and AudioToolbox
  AudioProperties.swift duration and bitrate
  AudioAnalyzer.swift   BPM and key, on Accelerate
  MetadataClient.swift  iTunes, Deezer, MusicBrainz, and the scoring
  YtDlp.swift           drives yt-dlp and ffmpeg
  ToolInstaller.swift   fetches those two on first run
  LibraryScanner.swift  walks the folder
  DjayImporter.swift    scrapes djay's BPM out of its SQLite file
  NameFormatter.swift   Title Case and filename patterns
  ProcessRunner.swift   async process plumbing

  GrabView.swift        LibraryView.swift    FindView.swift
  SettingsView.swift    JobsView.swift       TagEditorView.swift
  ArtPickerView.swift   EnrichOptionsView.swift  ToolSetupView.swift

Tests/
  main.swift            75 checks over the tag layer, naming and matching
  run.sh
```

## Notes for anyone changing this

Everything a job touches runs off the main actor. `ForgeService.config` is
`@Published` and therefore main-actor-only, so the downloader reads a
`ConfigSnapshot` instead — a lock-guarded copy that the service mirrors on every
edit. Reaching into the published value from a background task traps at runtime,
and the trap surfaces as a `SIGILL` deep inside SwiftUI's attribute graph with
nothing useful in the backtrace. It is not worth rediscovering.

`JobQueue` runs its work in `Task.detached`. A plain `Task` inside a `@MainActor`
class inherits that isolation, which would put the tag writes and the FFT on the
thread that draws the window.

Both pipes of every child process are drained concurrently. Reading stdout to the
end first deadlocks the moment the child writes more than the pipe buffer to
stderr — it blocks on the write, so stdout never closes, so the app waits forever.
That bug cost a day on the Windows build and is exactly as real here.
