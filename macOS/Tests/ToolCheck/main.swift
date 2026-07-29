import Foundation

// Exercises the parts that need yt-dlp and ffmpeg actually present:
// the installer, the tool resolution order, ffmpeg decoding, the Accelerate FFT,
// and — as an independent cross-check — whether ffmpeg can read back the tags our
// own ID3 writer produced.
//
//   ./Tests/toolcheck.sh
//
// No network fetch of any song: the audio it analyses is generated locally.

var failures = 0
var checks = 0

func check(_ label: String, _ condition: Bool, _ detail: String = "") {
    checks += 1
    print(condition ? "  ok   \(label)" : "  FAIL \(label)\(detail.isEmpty ? "" : "  — \(detail)")")
    if !condition { failures += 1 }
}

func section(_ name: String) { print("\n\(name)") }

let scratch = URL(fileURLWithPath: NSTemporaryDirectory())
    .appendingPathComponent("trackforge-toolcheck-\(UUID().uuidString)")
try! FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)

let group = DispatchGroup()
group.enter()

Task {
    defer { group.leave() }

    // MARK: Install

    section("ToolInstaller")
    print("  tools folder: \(ToolInstaller.toolsDirectory.path)")
    print("  ffmpeg build: \(ToolInstaller.ffmpegURL)")

    if ToolInstaller.hasYtDlp {
        print("  yt-dlp already installed, skipping download")
    } else {
        do {
            try await ToolInstaller.installYtDlp { print("    \($0.message)") }
        } catch {
            check("yt-dlp installs", false, error.localizedDescription)
        }
    }
    check("yt-dlp is executable", ToolInstaller.hasYtDlp)

    if ToolInstaller.hasFfmpeg {
        print("  ffmpeg already installed, skipping download")
    } else {
        do {
            try await ToolInstaller.installFfmpeg { print("    \($0.message)") }
        } catch {
            check("ffmpeg installs", false, error.localizedDescription)
        }
    }
    check("ffmpeg is executable", ToolInstaller.hasFfmpeg)

    // MARK: Resolution and launch

    section("Tool resolution and launch")
    let snapshot = ConfigSnapshot(AppConfig())
    let ytdlp = YtDlp(snapshot: snapshot)

    check("resolves the bundled yt-dlp", ytdlp.ytDlpPath == ToolInstaller.ytDlpPath,
          "got \(ytdlp.ytDlpPath)")
    check("resolves the bundled ffmpeg", ytdlp.ffmpegPath == ToolInstaller.ffmpegPath,
          "got \(ytdlp.ffmpegPath)")

    let (ytVersion, ffVersion) = await ytdlp.checkTools()
    check("yt-dlp runs", ytVersion != nil, "no version came back")
    check("ffmpeg runs", ffVersion != nil, "no version came back")
    if let ytVersion { print("    yt-dlp \(ytVersion)") }
    if let ffVersion { print("    \(ffVersion.prefix(70))") }

    let ffmpeg = ytdlp.ffmpegPath

    // MARK: Encode a known signal

    section("ffmpeg encodes MP3")
    // An impulse every 0.5s is exactly 120 BPM, and the decaying envelope gives
    // the onset detector something sharp to lock onto.
    let clickPath = scratch.appendingPathComponent("click120.mp3").path
    let clickResult = try? await ProcessRunner.run(ffmpeg, [
        "-v", "error", "-y",
        "-f", "lavfi",
        "-i", "aevalsrc='0.8*sin(2*PI*1000*t)*exp(-40*mod(t,0.5))':d=40:s=44100",
        "-c:a", "libmp3lame", "-b:a", "320k", clickPath,
    ])
    check("libmp3lame is present in this build",
          clickResult?.exitCode == 0,
          clickResult.map { ProcessRunner.lastError($0.stderr) ?? "" } ?? "did not run")
    check("wrote an mp3", FileManager.default.fileExists(atPath: clickPath))

    // A C major triad — C4, E4, G4 — held long enough for the chromagram to settle.
    let chordPath = scratch.appendingPathComponent("cmajor.mp3").path
    _ = try? await ProcessRunner.run(ffmpeg, [
        "-v", "error", "-y",
        "-f", "lavfi",
        "-i", "aevalsrc='0.3*(sin(2*PI*261.63*t)+sin(2*PI*329.63*t)+sin(2*PI*392.00*t))':d=25:s=44100",
        "-c:a", "libmp3lame", "-b:a", "320k", chordPath,
    ])

    // MARK: Our own MP3 parser against a real file

    section("AudioProbe on a real MP3")
    let properties = AudioProbe.read(path: clickPath)
    check("duration is about 40s", abs(properties.durationSeconds - 40) < 1.5,
          String(format: "got %.2fs", properties.durationSeconds))
    check("bitrate reads as 320", abs(properties.bitrate - 320) < 12,
          "got \(properties.bitrate)")

    // MARK: The analyser

    section("AudioAnalyzer")
    let tempo = await AudioAnalyzer.analyze(path: clickPath, ffmpegPath: ffmpeg)
    if let bpm = tempo.bpm {
        print(String(format: "    detected %.1f BPM", bpm))
        check("finds 120 BPM on a 120 BPM click track", abs(bpm - 120) < 2.5,
              String(format: "got %.1f", bpm))
    } else {
        check("finds a tempo at all", false, "returned nil")
    }

    let key = await AudioAnalyzer.analyze(path: chordPath, ffmpegPath: ffmpeg)
    print("    detected key \(key.key ?? "nil") (\(key.camelot ?? "nil"))")
    check("finds C on a C major triad", key.key == "C", "got \(key.key ?? "nil")")
    check("maps C major to Camelot 8B", key.camelot == "8B", "got \(key.camelot ?? "nil")")

    // MARK: ID3 writer, verified by something that is not our reader

    section("ffmpeg reads back tags our ID3 writer produced")
    let tagged = scratch.appendingPathComponent("tagged.mp3").path
    try? FileManager.default.copyItem(atPath: clickPath, toPath: tagged)

    let track = Track()
    track.path = tagged
    track.title = "Vicinity Of Obscenity"
    track.artist = "System Of a Down"
    track.album = "Steal This Album!"
    track.albumArtist = "System Of a Down"
    track.genre = "Alternative Metal"
    track.year = "2002"
    track.trackNumber = "9"
    track.bpm = "142"
    track.musicalKey = "F#m"

    // A tiny real JPEG, so the APIC frame carries something a decoder accepts.
    let artPath = scratch.appendingPathComponent("art.jpg").path
    _ = try? await ProcessRunner.run(ffmpeg, [
        "-v", "error", "-y", "-f", "lavfi",
        "-i", "color=c=red:s=600x600:d=1", "-frames:v", "1", artPath,
    ])
    let art = FileManager.default.contents(atPath: artPath)

    do {
        try TagService.write(track, art: art)
        check("TagService.write succeeded", true)
    } catch {
        check("TagService.write succeeded", false, error.localizedDescription)
    }

    // ffmpeg prints the metadata it parsed to stderr. If it can read these, so
    // can every player and DJ tool that matters.
    let probe = try? await ProcessRunner.run(ffmpeg, ["-hide_banner", "-i", tagged])
    let output = probe?.stderr ?? ""

    check("ffmpeg sees the title", output.contains("Vicinity Of Obscenity"))
    check("ffmpeg sees the artist", output.contains("System Of a Down"))
    check("ffmpeg sees the album", output.contains("Steal This Album!"))
    check("ffmpeg sees the genre", output.contains("Alternative Metal"))
    check("ffmpeg sees the year", output.contains("2002"))
    check("ffmpeg sees the BPM", output.contains("142"))
    check("ffmpeg sees the embedded cover", output.contains("mjpeg")
          || output.lowercased().contains("attached pic"))
    check("the audio stream still decodes", output.contains("Audio: mp3"))

    // And it still plays: a full decode with no errors proves we did not corrupt
    // the stream by rewriting the tag in front of it.
    let decode = try? await ProcessRunner.run(ffmpeg, [
        "-v", "error", "-i", tagged, "-f", "null", "-",
    ])
    check("decodes end to end with no errors",
          decode?.exitCode == 0 && (decode?.stderr.isBlank ?? false),
          decode?.stderr.prefix(160).description ?? "did not run")

    // MARK: Round trip through our own reader too

    section("Our reader agrees")
    let reread = TagService.read(path: tagged)
    check("title", reread.title == track.title, "got \(reread.title)")
    check("bpm", reread.bpm == "142", "got \(reread.bpm)")
    check("key", reread.musicalKey == "F#m", "got \(reread.musicalKey)")
    check("art detected", reread.hasArt)
    check("duration survives the rewrite", abs(reread.durationSeconds - 40) < 1.5,
          String(format: "got %.2fs", reread.durationSeconds))

    print("\n\(checks - failures)/\(checks) checks passed")
}

group.wait()
try? FileManager.default.removeItem(at: scratch)
exit(failures > 0 ? 1 : 0)
