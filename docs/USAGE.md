# Usage guide

## First run

TrackForge opens on the **Grab** page and immediately starts scanning your library in
the background. Check the pill in the top right:

- `yt-dlp 2026.07.04` in green — everything's there.
- `missing: yt-dlp, ffmpeg` in red — install them, then restart. See
  [the README](../README.md#requirements).

Go to **Settings** first and point **Library folder** at your music. Everything else
has a sensible default.

---

## Grabbing tracks

### One track

1. Paste the link into the box.
2. **Fetch metadata**. A card appears with the artist and title guessed from the video.
3. **Look up** on the card. The dropdown fills with matches from iTunes, Deezer and
   MusicBrainz, best first, and the fields populate from the top one.
4. If the top match is wrong, pick another from the dropdown — every field and the
   cover art update together.
5. **Grab**.

### A playlist

Paste the playlist URL. **Fetch metadata** expands it into one card per track. Then
**Look up all** followed by **Grab all** — but scroll through the cards first, because
lookups on a mixed playlist get some tracks wrong.

Cards you don't want: **Remove**.

### Reading the cards

The status line under the fields tells you what happened:

| Message | Meaning |
|---|---|
| `Matched iTunes (97)` in green | Strong match, tags are probably right |
| `... you already have this in your library` in amber | Duplicate — check before grabbing |
| `Nothing found online` in amber | Type the tags in by hand |
| `Lookup failed: ...` in red | Network problem, try again |

The score in brackets is out of roughly 110. Anything above 80 is reliable, 45-80 is
worth checking, below 45 is probably a different song.

### Cover art

**Change art** searches iTunes and Deezer for album covers and shows you a grid.
Click one to select it, double-click to select and close. **From file...** if you have
your own.

Whatever you pick gets square-cropped from the centre and re-encoded at 1000×1000.

---

## Fixing an existing library

### Seeing what's broken

**Library** → the **Missing** column lists what each file lacks. The filter chips
narrow it down:

| Chip | Shows |
|---|---|
| No art | Files with no embedded cover |
| No year | No release year |
| No genre | No genre |
| No album | No album |
| No BPM | No tempo, and none imported from djay |
| Incomplete | Missing anything at all |

The counter under the search box reads `x shown | y total | z need work`.

### Bulk fixing

1. Pick a filter chip.
2. `Ctrl+A`.
3. **Fill tags from online**.
4. Choose the fields it may touch. By default it fills album, album artist, year,
   genre, track number, disc number and ISRC, and leaves title and artist alone —
   those are usually already right, and a bad match would wreck them.
5. **Overwrite fields that already have a value** is off by default. Leave it off
   unless you're deliberately re-tagging.
6. **Run**. Watch the Jobs panel.

Anything that doesn't get a match scoring 45+ is left completely untouched.

### One file at a time

Double-click any row for the full editor: every field, the cover, an **Analyse BPM +
key** button, and **Look up online**. Nothing is written until **Save tags**.

**Rename the file to match the pattern** renames on save.

### BPM and key

**Analyse BPM + key** on selected tracks runs the detector and writes `TBPM` and
`TKEY`. It's CPU-bound and takes roughly eight seconds a track, so a few hundred files
is a coffee break.

BPM shown in a dimmer grey came from djay's database rather than the file's own tags.
Analysing writes a real tag and it turns normal.

---

## Find Online

Two ways in:

- Select tracks in the Library and hit **Find on YouTube** — they're loaded as
  `Artist - Title` queries.
- Type or paste a list yourself, one per line.

**Search YouTube** gives three results per query. The first is flagged with an accent
bar down the left and is usually right.

| Action | How |
|---|---|
| Send one to Grab | Double-click, or right-click → Send to Grab |
| Send every best match | **Send best to Grab** |
| Copy the link | Right-click → Copy link |
| Open in browser | Right-click → Open in browser |

---

## Settings

| Setting | Notes |
|---|---|
| Library folder | What gets scanned |
| Save downloads to | Where grabbed files land. Can be the same folder |
| Format | `mp3` for compatibility, `flac` if the source is worth it |
| MP3 bitrate | 320 unless you're short on space |
| Filename pattern | See below |
| Detect BPM and key | Adds ~8s per download |
| Pick cover art automatically | Uses the best match's artwork after a lookup |
| Force Title Case | Normalises title, artist and album |
| Store the source URL | Writes the link into `WOAS` so you know where it came from |
| Read BPM from djay | Imports tempo djay already worked out |
| iTunes store | Affects which releases and genres come back |
| Cookies from browser | Only for links that need a sign-in |

### Filename patterns

The live preview under the box shows what you'd get.

| Pattern | Result |
|---|---|
| `{track} {title}` | `09 Vicinity Of Obscenity.mp3` |
| `{artist} - {title}` | `System Of A Down - Vicinity Of Obscenity.mp3` |
| `{albumartist} - {album} - {track} {title}` | `System Of A Down - Steal This Album! - 09 Vicinity Of Obscenity.mp3` |
| `{year} - {artist} - {title}` | `2002 - System Of A Down - Vicinity Of Obscenity.mp3` |

Illegal filename characters are stripped. Collisions get ` (2)`, ` (3)` appended
rather than overwriting anything.

---

## Jobs

`Ctrl+J`, or the button in the top right, opens the dock. Each job shows a label, a
status line and a progress bar — green when done, red when failed, grey when
cancelled. **Stop** cancels one that's still running. **Clear done** tidies up.

Two jobs run at once by default. Change `MaxConcurrentJobs` in `config.json` if you
want more.

---

## When something goes wrong

| Symptom | Fix |
|---|---|
| `missing: yt-dlp` | `pip install -U yt-dlp`, restart |
| `missing: ffmpeg` | `winget install Gyan.FFmpeg`, restart |
| Download fails on every link | yt-dlp is stale: `pip install -U yt-dlp` |
| One link fails, others work | Age-restricted or private. Set **Cookies from browser** |
| Library shows 0 files | Wrong path in Settings, or the folder has no audio |
| No BPM after analysing | ffmpeg can't decode that file. Check the Jobs panel |
| Lookups return nothing | Check the artist and title fields aren't full of `(Official Video)` |
| App won't start | `%APPDATA%\TrackForge\crash.log` |

Run `TrackForge.exe --selftest` from a terminal to check every subsystem at once.
