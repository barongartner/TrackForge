# TrackForge

A native Windows app for managing a local music library and turning YouTube links
into properly tagged MP3s — cover art, album, year, genre, track number, BPM and
musical key all filled in automatically.

Built because every "YouTube to MP3" tool either dumps an untagged file called
`video.mp3` into your downloads folder, or wants a subscription.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What it does

**Grab** — paste one link, twenty links, or a whole playlist. TrackForge probes each
one, guesses the artist and title, searches iTunes, Deezer and MusicBrainz for the
real metadata, downloads the audio at 320 kbps, analyses the tempo and key, embeds a
1000×1000 cover, and files it under your naming convention. You review everything
before a single byte is written.

**Library** — scans your music folder and shows you exactly what's wrong with it:
which files have no cover, no year, no genre, no BPM. Filter to the broken ones,
select them all, and fill the gaps from online sources in one pass.

**Find Online** — select tracks in your library and search YouTube for them, or type
in a list of songs you want. Results feed straight back into Grab.

**Settings** — paths, format, bitrate, filename pattern, and which automatic
behaviours you want.

---

## Screens

| Page | What it's for |
|---|---|
| Grab | Paste links → review the tags → download |
| Library | See what's tagged, what isn't, and fix it in bulk |
| Find Online | Turn track names into YouTube links |
| Settings | Paths, audio format, naming pattern, behaviour |

---

## Install

Download **`TrackForge-1.0.0-x64.msi`** from the
[latest release](https://github.com/barongartner/TrackForge/releases/latest) and run it.

That's the whole thing. The installer needs nothing on your machine first:

- **No .NET install.** The app ships self-contained.
- **No yt-dlp or ffmpeg install.** TrackForge downloads them itself on first run —
  about 40 MB, into `%LOCALAPPDATA%\TrackForge\tools`, no admin rights needed. You get
  a small dialog with a progress bar the first time you open it. **Settings → Install /
  update tools** re-runs it any time, which is also how you keep yt-dlp current.

The only requirement is Windows 10 or 11, 64-bit.

If you'd rather manage the tools yourself, TrackForge uses whatever is on your `PATH`
when its own copies aren't present, and `config.json` can point at specific executables.

### Build from source

```bash
git clone https://github.com/barongartner/TrackForge.git
cd TrackForge
dotnet build -c Release
```

The executable lands in `src/TrackForge/bin/Release/net8.0-windows/TrackForge.exe`.

To rebuild the installer (needs [WiX 5](https://wixtoolset.org/)):

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
wix extension add -g WixToolset.Util.wixext/5.0.2

.\installer\build.ps1
```

WiX is pinned to 5 on purpose — versions 6 and 7 require accepting a paid Open Source
Maintenance Fee EULA.

---

## How the tagging works

### Metadata sources

Three sources are searched in parallel and every result is scored against what you
asked for. The highest scorer wins, but you can pick any of them from a dropdown.

| Source | Strong at | Needs a key |
|---|---|---|
| iTunes Search | Album, year, genre, track number, 1000px+ artwork | No |
| Deezer | ISRC, BPM, good fallback coverage | No |
| MusicBrainz | Canonical releases, Cover Art Archive | No |

Scoring rewards an exact title match (50), exact artist match (30) and a duration
within two seconds (20). It penalises compilations, "greatest hits" packages and
karaoke versions, because those are almost never what you actually wanted.

**Results are merged, not just ranked.** No single source carries every field — Deezer
has the year and ISRC but often no track number, iTunes has the track number and genre,
MusicBrainz has the ISRC but rarely a genre. Applying only the winner would leave gaps
you'd have to fill by hand, so the best match becomes the base and every blank field is
filled from the next-best source that has it. Only candidates scoring close to the
winner can contribute, so a weak match for a different song can't donate its album name.

One lookup, everything filled. Measured on a real track: 7/9 fields from the top source
alone, 9/9 after merging.

### Fields written

Standard ID3v2.4 frames, so everything else reads them:

| Frame | Field | Frame | Field |
|---|---|---|---|
| `TIT2` | Title | `TBPM` | BPM |
| `TPE1` | Artist | `TKEY` | Musical key |
| `TPE2` | Album artist | `TSRC` | ISRC |
| `TALB` | Album | `TPUB` | Publisher |
| `TCON` | Genre | `TCOM` | Composer |
| `TDRC` | Year | `COMM` | Comment |
| `TRCK` | Track number | `WOAS` | Source URL |
| `TPOS` | Disc number | `APIC` | Cover art |
| `POPM` | Rating | `TXXX:CAMELOT` | Camelot key code |

Cover art is square-cropped from the centre, resized to 1000×1000 and re-encoded as
JPEG at quality 92, so your library doesn't end up with a mix of 300px thumbnails and
3000px scans.

### BPM and key detection

No external audio library. ffmpeg decodes to mono 22.05 kHz float, then:

- **Tempo** — spectral flux onset envelope, autocorrelated, weighted by a log-normal
  prior centred on 120 BPM so it doesn't lock onto half or double time. Parabolic
  interpolation around the peak gives sub-frame precision.
- **Key** — chromagram folded from the FFT magnitudes, correlated against the
  Krumhansl-Kessler major and minor profiles. Reported as both a note name and a
  Camelot code for harmonic mixing.

**How accurate is it?** Measured against tracks Algoriddim djay had already analysed,
using djay as ground truth: **3 exact, 1 octave error, 1 genuine miss out of 5**. That
is what a from-scratch detector gets you — good enough to sort a library by tempo, not
good enough to beatmatch on blind. Octave errors (87 vs 174) are normal and mostly
harmless. Run `--selftest` to see the numbers on your own files.

If you already use djay, TrackForge reads the BPM it worked out from djay's own
library database, and shows those in a dimmer colour so you know where they came from.

### Naming

Default pattern is `{track} {title}`, which produces `09 Vicinity Of Obscenity.mp3`.

| Token | Result |
|---|---|
| `{track}` | `09` (zero-padded) |
| `{tracknum}` | `9` |
| `{title}` `{artist}` `{albumartist}` `{album}` `{year}` | as tagged |

Title Case capitalises every word except true connectors (`a`, `an`, `the`, `and`,
`or`, `but`, `vs`, `feat`). Acronyms and deliberate inner caps are left alone, so
`B.Y.O.B.` and `DDevil` survive intact.

---

## Usage

### Grabbing tracks

1. Paste links into the box on the **Grab** page, one per line. Playlist URLs expand
   into every track.
2. **Fetch metadata** — reads each link without downloading anything.
3. **Look up all** — searches the online sources and fills in the tag fields.
4. Check the cards. Edit anything. **Change art** if you want a different cover.
5. **Grab** on a single card, or **Grab all**.

A card warns you if the track already exists in your library.

### Fixing an existing library

1. **Library** → **Rescan**.
2. Click a filter chip: **No art**, **No year**, **No genre**, **No BPM**, or
   **Incomplete**.
3. `Ctrl+A` to select everything shown.
4. **Fill tags from online** — choose which fields it's allowed to touch and whether
   it may overwrite values that already exist.

Tags are written straight to the files. There is no undo, so leave *Overwrite* off
unless you mean it.

### Keyboard

| Key | Action |
|---|---|
| `Ctrl+1` … `Ctrl+4` | Switch pages |
| `Ctrl+J` | Jobs panel |
| `F5` | Rescan library |
| `Ctrl+A` | Select all (in the library list) |
| `Enter` / double-click | Edit tags |
| `Ctrl+Enter` | Fetch (in the Grab box) |

---

## Diagnostics

```bash
TrackForge.exe --selftest           # library, naming, analyser, lookup, merge
TrackForge.exe --selftest --online  # also exercises YouTube search
TrackForge.exe --uitest             # window-handle leak check over 240 page switches
TrackForge.exe --install-tools      # fetch yt-dlp and ffmpeg headlessly
```

Checks the tools are present, verifies the naming rules, scans the library, scores the
BPM detector against djay's own analysis, and runs a live metadata lookup. Writes to
`%APPDATA%\TrackForge\selftest.log`.

Crashes are logged to `%APPDATA%\TrackForge\crash.log`.

---

## Configuration

`%APPDATA%\TrackForge\config.json`

```json
{
  "LibraryFolder": "F:\\Music",
  "OutputFolder": "F:\\Music",
  "Format": "mp3",
  "Bitrate": "320",
  "FilenamePattern": "{track} {title}",
  "AnalyzeBpmAndKey": true,
  "AutoArt": true,
  "ForceTitleCase": true,
  "WriteSourceUrl": true,
  "ImportDjayData": true,
  "CookiesFromBrowser": "",
  "ItunesCountry": "CA"
}
```

`CookiesFromBrowser` (`opera`, `chrome`, `edge`, `firefox`, `brave`) is only needed if
a link asks you to sign in.

---

## Project layout

```
src/TrackForge/
  Core/
    AppConfig.cs       Settings, loaded from and saved to %APPDATA%
    Track.cs           The model - one audio file and everything about it
    TagService.cs      ID3 read/write and artwork normalisation (TagLib#)
    NameFormatter.cs   Title Case rules and filename building
    LibraryScanner.cs  Walks the library folder
    DjayImporter.cs    Reads BPM out of djay's own database
    AudioAnalyzer.cs   FFT, tempo detection, key detection
    MetadataClient.cs  iTunes, Deezer, MusicBrainz, Cover Art Archive
    MatchCandidate.cs  A scored result from one of those sources
    YtDlp.cs           Drives yt-dlp and ffmpeg
    JobQueue.cs        Background workers with progress reporting
    ToolInstaller.cs   Downloads yt-dlp and ffmpeg on first run
    ForgeService.cs    Ties it all together
    SelfTest.cs        --selftest diagnostics
  UI/
    Theme.cs           Palette and fonts
    Controls.cs        Flat button, nav button, card, text box, progress, pill
    MainForm.cs        Window, navigation, startup
    GrabPage.cs        Paste links, review, download
    GrabCard.cs        One pending download
    LibraryPage.cs     The library table and bulk actions
    FindPage.cs        YouTube search
    SettingsPage.cs    Settings
    TagEditorDialog.cs Full tag editor for one file
    ArtPickerDialog.cs Cover art chooser
    EnrichOptionsDialog.cs  Which fields a bulk fill may touch
    ToolSetupDialog.cs First-run yt-dlp / ffmpeg download
    JobsPanel.cs       The jobs dock

installer/
  TrackForge.wxs     WiX 5 package definition
  build.ps1          Publish self-contained, then build the MSI
  License.rtf        Shown on the installer's licence page
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together.

---

## Known limitations

- **BPM detection is decent, not perfect.** Octave errors happen. Verify before you
  rely on it for anything beatmatched.
- **Key detection is whole-track.** A song that changes key gets whichever one
  dominates.
- **djay's key data isn't imported.** Its `keySignatureIndex` is an internal
  enumeration that isn't documented; BPM is imported, key is detected ourselves.
- **No undo on tag writes.** Bulk operations write straight to disk.
- **yt-dlp breaks when YouTube changes things.** When downloads start failing, hit
  **Settings → Install / update tools** to pull the current build.
- **The MSI is unsigned.** SmartScreen will warn on first run — More info → Run anyway.

---

## Legal

TrackForge is a local file manager and a front-end for yt-dlp. It does not host,
distribute or circumvent DRM on anything. What you download and whether you have the
right to do it is on you — check the terms of service of any site you point it at, and
your local copyright law. Use it for material you own, material that's licensed for
download, or material in the public domain.

---

## Licence

MIT. See [LICENSE](LICENSE).

Built with [TagLib#](https://github.com/mono/taglib-sharp) for tag handling and
[yt-dlp](https://github.com/yt-dlp/yt-dlp) + [ffmpeg](https://ffmpeg.org/) for media.
Metadata from [iTunes Search](https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/),
[Deezer](https://developers.deezer.com/api), [MusicBrainz](https://musicbrainz.org/)
and the [Cover Art Archive](https://coverartarchive.org/).
