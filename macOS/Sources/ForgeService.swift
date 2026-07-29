import Foundation

/// The application core. Owns config, the job queue and every service, and
/// exposes the operations the UI actually needs.
@MainActor
final class ForgeService: ObservableObject {
    /// Every edit is mirrored into the snapshot, so a Settings change takes effect
    /// on the next download without anything being rebuilt.
    @Published var config: AppConfig {
        didSet { configSnapshot.update(config) }
    }

    @Published private(set) var tracks: [Track] = []
    @Published private(set) var libraryVersion = 0

    let jobs = JobQueue()
    let library = LibraryScanner()
    let metadata = MetadataClient()
    let downloader: YtDlp

    private let configSnapshot: ConfigSnapshot

    init() {
        let loaded = AppConfig.load()
        configSnapshot = ConfigSnapshot(loaded)
        config = loaded
        downloader = YtDlp(snapshot: configSnapshot)

        metadata.country = loaded.itunesCountry
        jobs.maxConcurrent = loaded.maxConcurrentJobs
    }

    func saveConfig() {
        config.save()
        metadata.country = config.itunesCountry
        jobs.maxConcurrent = config.maxConcurrentJobs
    }

    // MARK: - Library

    func rescanLibrary(onProgress: ((String) -> Void)? = nil) async throws {
        _ = try await library.scan(
            root: config.libraryFolder,
            importDjay: config.importDjayData,
            onProgress: onProgress)
        refreshTracks()
    }

    func refreshTracks() {
        tracks = library.tracks
        libraryVersion &+= 1
    }

    /// Bumps the version so every list rebuilds its rows from the same Track
    /// objects, which the jobs mutate in place.
    func libraryChanged() {
        libraryVersion &+= 1
    }

    /// True if a track with this artist + title is already in the library.
    func alreadyHave(artist: String, title: String) -> Bool {
        func normalise(_ s: String) -> String {
            s.lowercased().filter { $0.isLetter || $0.isNumber }
        }
        let a = normalise(artist)
        let t = normalise(title)
        guard !t.isEmpty else { return false }

        return tracks.contains { existing in
            guard normalise(existing.title) == t else { return false }
            if a.isEmpty { return true }
            let other = normalise(existing.artist)
            return other.contains(a) || a.contains(other)
        }
    }

    // MARK: - Download

    struct GrabRequest {
        var url: String
        var meta: Track
        var artURL: String?
        var artBytes: Data?
        var outputFolder: String?
    }

    /// Download, tag, name and file a single track.
    @discardableResult
    func enqueueGrab(_ request: GrabRequest) -> Int {
        var label = "\(request.meta.artist) - \(request.meta.title)"
            .trimmingCharacters(in: CharacterSet(charactersIn: " -"))
        if label.isEmpty { label = request.url }

        let cfg = config
        let downloader = downloader
        let metadata = metadata

        return jobs.enqueue(kind: "grab", label: label) { report in
            var temporaryDirectory: String?
            defer { if let temporaryDirectory { downloader.safeDelete(temporaryDirectory) } }

            report(2, "Fetching audio")
            let (file, directory) = try await downloader.download(request.url) { percent in
                report(2 + percent * 0.55, String(format: "Downloading %.0f%%", percent))
            }
            temporaryDirectory = directory

            try Task.checkCancellation()

            let meta = request.meta.clone()
            meta.path = file

            if cfg.forceTitleCase {
                meta.title = NameFormatter.titleCase(meta.title)
                meta.artist = NameFormatter.titleCase(meta.artist)
                meta.album = NameFormatter.titleCase(meta.album)
                meta.albumArtist = NameFormatter.titleCase(meta.albumArtist)
            }

            if cfg.analyzeBpmAndKey && meta.bpm.isBlank {
                report(64, "Analysing tempo and key")
                let analysis = await AudioAnalyzer.analyze(
                    path: file, ffmpegPath: downloader.ffmpegPath)
                if let bpm = analysis.bpm, bpm > 0 { meta.bpm = String(Int(bpm.rounded())) }
                if let key = analysis.key, !key.isBlank, meta.musicalKey.isBlank {
                    meta.musicalKey = key
                    meta.camelot = analysis.camelot ?? ""
                }
            }

            try Task.checkCancellation()

            var art = request.artBytes
            if art == nil, let artURL = request.artURL, !artURL.isBlank {
                report(74, "Fetching cover art")
                art = await metadata.downloadArt(artURL)
            }

            if cfg.writeSourceURL { meta.sourceURL = request.url }

            report(84, "Writing tags")
            try TagService.write(meta, art: art)

            let outputFolder = (request.outputFolder?.isBlank == false)
                ? request.outputFolder! : cfg.outputFolder
            try FileManager.default.createDirectory(
                atPath: outputFolder, withIntermediateDirectories: true)

            let fileName = NameFormatter.buildFileName(
                meta, pattern: cfg.filenamePattern,
                extension: (file as NSString).pathExtension)
            let destination = NameFormatter.uniquePath(
                (outputFolder as NSString).appendingPathComponent(fileName))

            try FileManager.default.moveItem(atPath: file, toPath: destination)
            meta.path = destination
            meta.hasArt = !(art?.isEmpty ?? true)

            report(100, "Saved  " + (destination as NSString).lastPathComponent)
            await MainActor.run { self.libraryChanged() }
        }
    }

    // MARK: - Enrich

    struct EnrichOptions {
        var overwrite = false
        var fetchArt = true
        var analyzeAudio = false
        var renameFiles = false
        var fields: [String] = []
    }

    /// Fill in missing tags on library files from online sources.
    @discardableResult
    func enqueueEnrich(_ tracks: [Track], options: EnrichOptions) -> Int {
        let cfg = config
        let metadata = metadata
        let ffmpeg = downloader.ffmpegPath

        return jobs.enqueue(
            kind: "enrich", label: "Fill tags on \(tracks.count) track(s)"
        ) { report in
            var updated = 0, skipped = 0

            for (i, track) in tracks.enumerated() {
                try Task.checkCancellation()
                report(Double(i) / Double(tracks.count) * 100,
                       "\(i + 1)/\(tracks.count)  \(track.title)")

                var changed = false
                var art: Data?

                let candidates = await metadata.lookup(
                    artist: track.artist, title: track.title,
                    durationSeconds: track.durationSeconds)

                // Merge across sources so one pass fills everything available,
                // rather than leaving gaps only the next source down could cover.
                let best = MetadataClient.merge(candidates)

                // Below 45 the match is usually a different song with a similar
                // name, and writing it would be worse than leaving the file alone.
                if let best, best.score >= 45 {
                    best.apply(to: track, overwrite: options.overwrite,
                               titleCase: cfg.forceTitleCase, only: options.fields)
                    changed = true

                    if options.fetchArt, !track.hasArt, !best.artURL.isEmpty {
                        art = await metadata.downloadArt(best.artURL)
                    }
                }

                if options.analyzeAudio, track.displayBpm.isEmpty {
                    let analysis = await AudioAnalyzer.analyze(
                        path: track.path, ffmpegPath: ffmpeg)
                    if let bpm = analysis.bpm, bpm > 0 {
                        track.bpm = String(Int(bpm.rounded()))
                        changed = true
                    }
                    if let key = analysis.key, !key.isBlank, track.musicalKey.isBlank {
                        track.musicalKey = key
                        track.camelot = analysis.camelot ?? ""
                        changed = true
                    }
                }

                guard changed || art != nil else { skipped += 1; continue }

                do {
                    try TagService.write(track, art: art)
                    if options.renameFiles {
                        await MainActor.run { _ = self.renameToPattern(track) }
                    }
                    updated += 1
                } catch {
                    skipped += 1
                }
            }

            report(100, "Updated \(updated), skipped \(skipped)")
            await MainActor.run { self.libraryChanged() }
        }
    }

    /// Rewrites tags already on disk as clean ID3v2.3 with no ID3v1 trailer.
    /// Fetches nothing — every value is read from the file and written straight
    /// back, so it is purely a format repair. Worth having on a Mac because
    /// Rekordbox, Serato and djay all read v2.3 more reliably than v2.4, and a
    /// stale ID3v1 tag is what makes a genre show up as a bare number.
    @discardableResult
    func enqueueRetag(_ tracks: [Track]) -> Int {
        let cfg = config

        return jobs.enqueue(
            kind: "retag", label: "Repair tags on \(tracks.count) track(s)"
        ) { report in
            var repaired = 0, failed = 0
            var failedNames: [String] = []

            for (i, track) in tracks.enumerated() {
                try Task.checkCancellation()
                report(Double(i) / Double(tracks.count) * 100,
                       "\(i + 1)/\(tracks.count)  \(track.fileName)")

                do {
                    let art = TagService.readArt(path: track.path)
                    if cfg.forceTitleCase {
                        track.title = NameFormatter.titleCase(track.title)
                        track.artist = NameFormatter.titleCase(track.artist)
                        track.album = NameFormatter.titleCase(track.album)
                        track.albumArtist = NameFormatter.titleCase(track.albumArtist)
                    }
                    try TagService.write(track, art: art)
                    repaired += 1
                } catch {
                    failed += 1
                    if failedNames.count < 5 { failedNames.append(track.fileName) }
                }
            }

            var message = "Repaired \(repaired)"
            if failed > 0 {
                message += ", \(failed) could not be written (\(failedNames.joined(separator: ", "))"
                    + (failed > failedNames.count ? ", …" : "") + ")"
            }
            report(100, message)
            await MainActor.run { self.libraryChanged() }
        }
    }

    /// Analyse BPM and key for library files, no network involved.
    @discardableResult
    func enqueueAnalyze(_ tracks: [Track], write: Bool = true) -> Int {
        let ffmpeg = downloader.ffmpegPath

        return jobs.enqueue(
            kind: "analyze", label: "Analyse \(tracks.count) track(s)"
        ) { report in
            var done = 0

            for (i, track) in tracks.enumerated() {
                try Task.checkCancellation()
                report(Double(i) / Double(tracks.count) * 100,
                       "\(i + 1)/\(tracks.count)  \(track.title)")

                let analysis = await AudioAnalyzer.analyze(
                    path: track.path, ffmpegPath: ffmpeg)
                guard let bpm = analysis.bpm else { continue }

                track.bpm = String(Int(bpm.rounded()))
                if let key = analysis.key, !key.isBlank {
                    track.musicalKey = key
                    track.camelot = analysis.camelot ?? ""
                }

                if write {
                    do { try TagService.write(track); done += 1 } catch { }
                }
            }

            report(100, "Analysed \(done) of \(tracks.count)")
            await MainActor.run { self.libraryChanged() }
        }
    }

    /// Renames a file to match the configured pattern. Updates track.path.
    @discardableResult
    func renameToPattern(_ track: Track) -> Bool {
        let directory = (track.path as NSString).deletingLastPathComponent
        guard !directory.isEmpty else { return false }

        let wanted = NameFormatter.buildFileName(
            track, pattern: config.filenamePattern,
            extension: (track.path as NSString).pathExtension)
        var target = (directory as NSString).appendingPathComponent(wanted)

        if target.compare(track.path, options: .caseInsensitive) == .orderedSame { return false }

        target = NameFormatter.uniquePath(target)
        do {
            try FileManager.default.moveItem(atPath: track.path, toPath: target)
            track.path = target
            return true
        } catch {
            return false
        }
    }
}
