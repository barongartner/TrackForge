# Architecture

This document describes the Windows build. The macOS build is a separate native
rewrite that mirrors the same pipelines and rules — see
[macOS/README.md](../macOS/README.md) for what it does differently and why.

TrackForge is a WinForms app on .NET 8, split into a `Core` layer that knows nothing
about the UI and a `UI` layer that does all the drawing.

```
                       ┌──────────────────────────────┐
                       │          MainForm            │
                       │  nav, startup, job plumbing  │
                       └──────────────┬───────────────┘
                                      │
        ┌───────────────┬─────────────┼─────────────┬──────────────┐
        │               │             │             │              │
   ┌────▼────┐    ┌─────▼─────┐  ┌────▼────┐  ┌─────▼──────┐  ┌────▼─────┐
   │GrabPage │    │LibraryPage│  │FindPage │  │SettingsPage│  │JobsPanel │
   └────┬────┘    └─────┬─────┘  └────┬────┘  └─────┬──────┘  └────┬─────┘
        │               │             │             │              │
        └───────────────┴─────────────┴─────────────┴──────────────┘
                                      │
                       ┌──────────────▼───────────────┐
                       │        ForgeService          │
                       │  owns config + every service │
                       └──────────────┬───────────────┘
                                      │
     ┌────────────┬──────────┬────────┼────────┬────────────┬────────────┐
     │            │          │        │        │            │            │
┌────▼────┐ ┌─────▼────┐ ┌───▼───┐ ┌──▼───┐ ┌──▼──────┐ ┌───▼─────┐ ┌────▼─────┐
│JobQueue │ │Library   │ │YtDlp  │ │Audio │ │Metadata │ │TagServ- │ │DjayImp-  │
│         │ │Scanner   │ │       │ │Analy-│ │Client   │ │ice      │ │orter     │
│         │ │          │ │       │ │zer   │ │         │ │         │ │          │
└─────────┘ └──────────┘ └───┬───┘ └──┬───┘ └────┬────┘ └────┬────┘ └────┬─────┘
                             │        │          │           │           │
                          yt-dlp   ffmpeg    iTunes /     TagLib#     djay's
                          ffmpeg             Deezer /                 SQLite DB
                                             MusicBrainz
```

## The layers

### Core

No `using System.Windows.Forms` anywhere in here except where `Track` needs
`System.Drawing` for image handling. Everything is testable in isolation and
everything async takes a `CancellationToken`.

**`ForgeService`** is the single entry point the UI talks to. It owns the config, the
job queue and one instance of each service. It exposes the three operations that
actually matter — `EnqueueGrab`, `EnqueueEnrich`, `EnqueueAnalyze` — plus
`RescanLibraryAsync`. Everything it enqueues returns a `Job` the caller can watch.

**`JobQueue`** runs a fixed pool of background threads pulling from a
`BlockingCollection`. Every state change raises `JobChanged`, which fires on whichever
worker thread produced it — the UI is responsible for marshalling back to the form.
`MainForm.OnJobChanged` does that once, centrally, and swallows the
`ObjectDisposedException` you get if a job finishes during shutdown.

**`Track`** is the model. It's mutable and passed around freely: the tag editor mutates
it in place, the enrich job mutates it in place, and `TagService.Write` persists
whatever is on it. `Clone()` exists for when you need a snapshot (the grab pipeline
takes one so later edits to the card don't affect an in-flight download).

### UI

Hand-laid-out WinForms with owner-drawn controls. No designer files — every control is
positioned in code, which is more verbose but survives merges and diffs cleanly.

`Theme` holds the palette. `Controls.cs` has the six custom controls that make WinForms
look like it isn't from 2005: `FlatButton`, `NavButton`, `CardPanel`, `FlatTextBox`,
`FlatProgress`, `Pill`, and `DarkListView`.

`DarkListView` owner-draws its own header and rows, and calls `SetWindowTheme` with
`DarkMode_Explorer` so the scrollbars match.

## The three pipelines

### Grab: link → tagged file

```
paste URL
   │
   ├─ YtDlp.ProbeAsync ──────────── yt-dlp -J --flat-playlist
   │     └─ VideoEntry.Guess()      artist/title from YT Music tags,
   │                                falling back to splitting the video title
   │
   ├─ MetadataClient.LookupAsync ── iTunes + Deezer in parallel,
   │     └─ ScoreMatch()            MusicBrainz if thin, then scored and sorted
   │
   ├─ [user reviews and edits the card]
   │
   └─ ForgeService.EnqueueGrab ──── background job:
         ├─ YtDlp.DownloadAsync          bestaudio → ffmpeg → mp3 320k   (0-57%)
         ├─ AudioAnalyzer.AnalyzeAsync   tempo + key                     (64%)
         ├─ MetadataClient.DownloadArt   cover                           (74%)
         ├─ TagService.Write             ID3v2.4 + APIC                  (84%)
         └─ File.Move                    into place under the name pattern
```

The download happens into a GUID temp folder under `%TEMP%\TrackForge\`, which is
deleted in a `finally` whether the job succeeds, fails or is cancelled.

### Enrich: fix what's already on disk

For each selected track: look it up, take the best match if it scores 45 or better,
apply only the fields the user ticked, optionally fetch art, optionally analyse the
audio, then write. A track where nothing changed is skipped rather than rewritten.

The 45 threshold matters. Below it the match is usually a different song with a similar
name, and writing it would be worse than leaving the file alone.

### Find: name → YouTube link

`yt-dlp ytsearch3:<query>` per line, three results each, first one flagged as best.
Results can be sent to Grab, which is the loop that closes: library → find → grab →
library.

## External data

### The djay database

Algoriddim djay keeps a SQLite file at
`<library>\djay\djay Media Library\MediaLibrary.db`. Records are stored as proprietary
`TSAF` binary blobs which we deliberately do not try to parse. Instead
`DjayImporter` scrapes printable ASCII runs out of the blobs to find the `file:///`
URL, pairs each record's `rowid` with the BPM in
`secondaryIndex_mediaItemAnalyzedDataIndex`, and builds a filename → BPM map.

The live database is locked with an open WAL, so it's copied to `%TEMP%` first and
opened read-only. Every failure path is swallowed: no djay data is a perfectly fine
outcome.

Its `keySignatureIndex` column is *not* imported. It's an undocumented internal
enumeration and guessing at the mapping would put wrong keys in your files.

### Metadata APIs

All three are keyless and rate-limit themselves reasonably, except MusicBrainz, which
asks for one request per second. `MetadataClient` enforces that with a
`SemaphoreSlim` and a timestamp, so a bulk enrich over 200 tracks stays polite instead
of getting the app blocked.

Artwork is cached in-memory by URL, capped at 300 entries.

## Audio analysis

`AudioAnalyzer` shells out to ffmpeg for decoding (`-f f32le` straight to stdout, mono,
22.05 kHz, first 7 minutes) and does everything else in managed code.

**FFT** is an iterative radix-2 Cooley-Tukey over 2048-sample Hann-windowed frames with
a 512 hop. That's ~43 frames/second.

**Tempo**: spectral flux (positive magnitude deltas summed across bins) gives an onset
envelope. Autocorrelating it and weighting by
`exp(-0.5 · (log₂(tempo/120) / 0.9)²)` biases toward plausible dance tempos without
hard-clamping. Parabolic interpolation around the winning lag gives sub-frame
precision. The result is folded into 60-200 BPM.

**Key**: FFT bins between 55 Hz and 2200 Hz are folded to pitch classes by rounding
their MIDI note number, summed across every frame into a 12-bin chromagram, then
Pearson-correlated against the 24 rotations of the Krumhansl-Kessler major and minor
profiles. The winner becomes a note name and a Camelot code.

The whole thing runs at roughly real-time ÷ 30 on a modern CPU — a four-minute track
analyses in about eight seconds, dominated by ffmpeg decode.

## Error handling

The rule is that no background failure should ever take down the app.

- Every job wraps its work in try/catch; a failure sets `JobState.Failed` and stores
  the message and full trace on the job.
- `TagService.Read` returns a partially-populated `Track` for an unreadable file rather
  than throwing.
- `DjayImporter` swallows everything.
- Metadata calls return empty lists on any network or parse failure.
- `Program.Main` installs handlers for `UnhandledException` and `ThreadException`,
  which append to `%APPDATA%\TrackForge\crash.log` and show the path in a dialog.

The only place errors are surfaced loudly is where the user explicitly asked for
something and it didn't happen: a failed tag write in the editor gets a message box.

## Packaging and the tool bootstrap

`installer/build.ps1` publishes the app self-contained and single-file, then hands it
to WiX 5 to produce `TrackForge-<version>-x64.msi`. Self-contained means the installed
app has no .NET prerequisite — the price is a ~65 MB executable and a ~124 MB package.

WiX is pinned to 5.0.2 deliberately. WiX 6 and 7 refuse to run without accepting the
Open Source Maintenance Fee EULA, which is a paid licensing decision.

**yt-dlp and ffmpeg are not in the installer.** `ToolInstaller` downloads them into
`%LOCALAPPDATA%\TrackForge\tools` on first run. Three reasons:

- yt-dlp goes stale within weeks of a YouTube change; a bundled copy would ship broken.
  Fetching on demand also gives the user a working update button.
- ffmpeg builds are GPL, and redistributing them carries source-offer obligations.
- `%LOCALAPPDATA%` is writable without admin rights, so updating tools never needs UAC.

`YtDlp.Resolve` picks an explicit config path first, then the bundled copy, then `PATH`.
Bundled beating `PATH` matters: a stale system-wide yt-dlp shouldn't be able to break
downloads once TrackForge has fetched a current one.

## Adding things

**A new metadata source** — add a method to `MetadataClient` returning
`List<MatchCandidate>`, call it from `LookupAsync`, and make sure `ScoreMatch` treats
it fairly. The dedupe key is title + album + source, so a new source's results won't
collide with an existing one's.

**A new tag field** — add the property to `Track`, read it in `TagService.Read`, write
it in `TagService.Write`, add it to `MatchCandidate.ApplyTo` if a source can supply it,
and add a field to `TagEditorDialog.AddField` plus the push/pull methods.

**A new page** — build a `Panel` subclass and call `AddPage` in `MainForm.BuildPages`.
Nav button sizing and page switching are handled for you.
