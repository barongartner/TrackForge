import Foundation

/// One entry returned by a probe or a search.
struct VideoEntry: Identifiable {
    var id: String { videoID.isEmpty ? url : videoID }

    var videoID = ""
    var url = ""
    var rawTitle = ""
    var uploader = ""
    var durationSeconds = 0
    var viewCount: Int64 = 0
    var thumbnailURL = ""

    // YouTube Music entries carry real tags; use them when they exist.
    var ytTrack = ""
    var ytArtist = ""
    var ytAlbum = ""
    var ytYear = ""

    var durationText: String {
        guard durationSeconds > 0 else { return "" }
        let s = durationSeconds % 60, m = (durationSeconds / 60) % 60, h = durationSeconds / 3600
        return h > 0
            ? String(format: "%d:%02d:%02d", h, m, s)
            : String(format: "%d:%02d", m, s)
    }

    /// Best guess at artist and title, before we look anything up.
    func guess() -> (artist: String, title: String) {
        if !ytArtist.isBlank && !ytTrack.isBlank { return (ytArtist, ytTrack) }

        var (a, t) = MetadataClient.splitVideoTitle(rawTitle)
        if a.isBlank {
            a = uploader.replacingOccurrences(
                of: #"\s*-\s*Topic$"#, with: "",
                options: [.regularExpression, .caseInsensitive])
        }
        if t.isBlank { t = rawTitle }
        return (a, t)
    }
}

/// A thread-safe copy of the settings the downloader needs.
///
/// Probing, searching and downloading all run off the main actor, so they cannot
/// reach ForgeService's `@Published` config directly — the main actor is where it
/// lives, and reaching in from a background task traps.
final class ConfigSnapshot: @unchecked Sendable {
    private var value: AppConfig
    private let lock = NSLock()

    init(_ value: AppConfig) { self.value = value }

    var current: AppConfig {
        lock.lock()
        defer { lock.unlock() }
        return value
    }

    func update(_ newValue: AppConfig) {
        lock.lock()
        value = newValue
        lock.unlock()
    }
}

/// Drives the yt-dlp and ffmpeg executables.
final class YtDlp: @unchecked Sendable {
    private let snapshot: ConfigSnapshot

    init(snapshot: ConfigSnapshot) { self.snapshot = snapshot }

    private func config() -> AppConfig { snapshot.current }

    /// An explicit setting wins, then our own tools folder, then Homebrew and
    /// PATH. The bundled copy taking priority over PATH means a stale system
    /// yt-dlp cannot break downloads once we have installed a current one.
    private static func resolve(configured: String, bundled: String, onPath: String) -> String {
        if !configured.isBlank, FileManager.default.isExecutableFile(atPath: configured) {
            return configured
        }
        if FileManager.default.isExecutableFile(atPath: bundled) { return bundled }
        return ProcessRunner.which(onPath) ?? onPath
    }

    var ytDlpPath: String {
        Self.resolve(configured: config().ytDlpPath,
                     bundled: ToolInstaller.ytDlpPath, onPath: "yt-dlp")
    }

    var ffmpegPath: String {
        Self.resolve(configured: config().ffmpegPath,
                     bundled: ToolInstaller.ffmpegPath, onPath: "ffmpeg")
    }

    private static let probeTimeout: TimeInterval = 120

    // MARK: - Link normalising

    /// Works out what a link actually means.
    ///
    /// A "watch" link carrying a radio mix (list=RD…, or start_radio=1) is what you
    /// get from YouTube's autoplay sidebar. The mix is an endless generated stream,
    /// so expanding it queues hundreds of unrelated tracks. What the user wanted was
    /// the one song they were listening to, so those collapse to the single video.
    ///
    /// Real playlists — /playlist?list=…, or a watch link carrying a genuine
    /// PL/UU/OL/LL list — still expand into every track.
    static func normalizeForProbe(_ raw: String) -> (url: String, singleVideo: Bool) {
        let trimmed = raw.trimmed
        guard !trimmed.isEmpty, let components = URLComponents(string: trimmed) else {
            return (trimmed, false)
        }

        let host = (components.host ?? "").replacingOccurrences(
            of: "www.", with: "", options: .caseInsensitive)
        let isYouTube = host.hasSuffix("youtube.com") || host == "youtu.be"
        guard isYouTube else { return (trimmed, false) }

        let query = Dictionary(
            (components.queryItems ?? []).map { ($0.name.lowercased(), $0.value ?? "") },
            uniquingKeysWith: { first, _ in first })

        var videoID = query["v"] ?? ""
        if host == "youtu.be" && videoID.isEmpty {
            videoID = components.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        }

        // A bare /playlist link is unambiguous: the user wants the whole thing.
        if components.path.lowercased().hasPrefix("/playlist") { return (trimmed, false) }

        let list = query["list"] ?? ""
        let isRadio = query["start_radio"] == "1" || list.lowercased().hasPrefix("rd")

        if isRadio && !videoID.isEmpty {
            return ("https://www.youtube.com/watch?v=\(videoID)", true)
        }

        // A watch link with a genuine playlist keeps the playlist.
        return (trimmed, false)
    }

    // MARK: - Checks

    func checkTools() async -> (ytDlp: String?, ffmpeg: String?) {
        async let yt = Self.firstLine(ytDlpPath, ["--version"])
        async let ff = Self.firstLine(ffmpegPath, ["-version"])
        return await (yt, ff)
    }

    private static func firstLine(_ executable: String, _ arguments: [String]) async -> String? {
        guard let result = try? await ProcessRunner.run(executable, arguments, timeout: 20),
              result.exitCode == 0
        else { return nil }

        let text = result.stdout.isEmpty ? result.stderr : result.stdout
        return text.split(whereSeparator: \.isNewline).first.map { $0.trimmed }
    }

    // MARK: - Probe

    /// Reads metadata for a link without downloading. Playlists expand.
    func probe(_ url: String) async throws -> (entries: [VideoEntry], playlistTitle: String?) {
        let (probeURL, singleVideo) = Self.normalizeForProbe(url)

        var arguments = ["-J", "--no-warnings", "--flat-playlist", "--ignore-config"]
        if singleVideo { arguments.append("--no-playlist") }
        arguments.append(probeURL)

        let result = try await ProcessRunner.run(
            ytDlpPath, arguments, timeout: Self.probeTimeout)

        guard result.exitCode == 0, !result.stdout.isBlank else {
            throw ToolError.message(
                ProcessRunner.lastError(result.stderr) ?? "yt-dlp could not read that link.")
        }

        guard let data = result.stdout.data(using: .utf8),
              let root = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { throw ToolError.message("yt-dlp returned something we could not read.") }

        if root["_type"] as? String == "playlist", let entries = root["entries"] as? [[String: Any]] {
            return (entries.map { Self.parseEntry($0, fallbackURL: url) },
                    root["title"] as? String)
        }
        return ([Self.parseEntry(root, fallbackURL: url)], nil)
    }

    /// Finds a track on YouTube by name. A failed search is just an empty list.
    func search(_ query: String, limit: Int = 5) async -> [VideoEntry] {
        guard !query.isBlank else { return [] }

        let arguments = ["-J", "--no-warnings", "--flat-playlist", "--ignore-config",
                         "ytsearch\(limit):\(query)"]
        guard let result = try? await ProcessRunner.run(ytDlpPath, arguments, timeout: 90),
              result.exitCode == 0,
              let data = result.stdout.data(using: .utf8),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let entries = root["entries"] as? [[String: Any]]
        else { return [] }

        return entries.map { Self.parseEntry($0, fallbackURL: "") }
    }

    private static func parseEntry(_ e: [String: Any], fallbackURL: String) -> VideoEntry {
        func string(_ key: String) -> String {
            if let s = e[key] as? String { return s }
            if let n = e[key] as? NSNumber { return n.stringValue }
            return ""
        }
        func number(_ key: String) -> Double { (e[key] as? NSNumber)?.doubleValue ?? 0 }

        var entry = VideoEntry()
        entry.videoID = string("id")
        entry.rawTitle = string("title")
        entry.uploader = string("uploader").isEmpty ? string("channel") : string("uploader")
        entry.durationSeconds = Int(number("duration"))
        entry.viewCount = Int64(number("view_count"))
        entry.ytTrack = string("track")
        entry.ytArtist = string("artist")
        entry.ytAlbum = string("album")
        entry.ytYear = string("release_year")
        entry.thumbnailURL = string("thumbnail")

        let webpage = string("webpage_url")
        entry.url = !webpage.isEmpty ? webpage
            : !entry.videoID.isEmpty ? "https://www.youtube.com/watch?v=\(entry.videoID)"
            : fallbackURL

        if entry.thumbnailURL.isEmpty, !entry.videoID.isEmpty {
            entry.thumbnailURL = "https://i.ytimg.com/vi/\(entry.videoID)/mqdefault.jpg"
        }
        return entry
    }

    // MARK: - Download

    /// Downloads bestaudio and transcodes it. Returns the path to a file inside a
    /// temp folder — the caller moves it into place and deletes the folder.
    func download(
        _ url: String,
        onProgress: @escaping (Double) -> Void
    ) async throws -> (file: String, temporaryDirectory: String) {
        let cfg = config()
        let temporaryDirectory = FileManager.default.temporaryDirectory
            .appendingPathComponent("TrackForge", isDirectory: true)
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(
            at: temporaryDirectory, withIntermediateDirectories: true)

        var arguments = [
            "-f", "bestaudio/best",
            "-x", "--audio-format", cfg.format,
            "--audio-quality", cfg.format == "mp3" ? cfg.bitrate + "K" : "0",
            "--no-playlist", "--no-warnings", "--newline", "--ignore-config",
            "--no-embed-metadata", "--no-embed-thumbnail",
            "-o", temporaryDirectory.appendingPathComponent("track.%(ext)s").path,
        ]

        let ffmpegDirectory = (ffmpegPath as NSString).deletingLastPathComponent
        if !ffmpegDirectory.isEmpty {
            arguments += ["--ffmpeg-location", ffmpegDirectory]
        }
        if !cfg.cookiesFromBrowser.isBlank {
            arguments += ["--cookies-from-browser", cfg.cookiesFromBrowser]
        }
        arguments.append(url)

        do {
            let result = try await ProcessRunner.runStreamingLines(ytDlpPath, arguments) { line in
                if let percent = Self.parseProgress(line) {
                    onProgress(percent)
                } else if line.contains("ExtractAudio") {
                    onProgress(99)
                }
            }

            let files = ((try? FileManager.default.contentsOfDirectory(
                at: temporaryDirectory, includingPropertiesForKeys: [.fileSizeKey])) ?? [])
                .filter { $0.pathExtension != "part" }

            guard result.exitCode == 0, !files.isEmpty else {
                safeDelete(temporaryDirectory.path)
                throw ToolError.message(
                    ProcessRunner.lastError(result.stderr) ?? "Download failed.")
            }

            // Prefer the transcoded file over any leftover source stream.
            let wanted = cfg.format.lowercased()
            let chosen = files.sorted { a, b in
                let aWanted = a.pathExtension.lowercased() == wanted
                let bWanted = b.pathExtension.lowercased() == wanted
                if aWanted != bWanted { return aWanted }
                return fileSize(a) > fileSize(b)
            }.first!

            return (chosen.path, temporaryDirectory.path)
        } catch {
            safeDelete(temporaryDirectory.path)
            throw error
        }
    }

    private static func parseProgress(_ line: String) -> Double? {
        // "[download]  42.3% of ~5.01MiB at …"
        guard line.hasPrefix("[download]"), let percentIndex = line.firstIndex(of: "%")
        else { return nil }

        let head = line[line.startIndex..<percentIndex]
        let digits = head.reversed().prefix { $0.isNumber || $0 == "." }
        return Double(String(digits.reversed()))
    }

    private func fileSize(_ url: URL) -> Int64 {
        (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init) ?? 0
    }

    func safeDelete(_ directory: String) {
        try? FileManager.default.removeItem(atPath: directory)
    }
}
