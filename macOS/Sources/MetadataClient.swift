import Foundation

/// Metadata and cover art lookup. No API keys, no accounts.
///
/// iTunes Search — best album / year / genre / track number, 1000px+ artwork
/// Deezer        — solid fallback, carries ISRC and BPM
/// MusicBrainz   — canonical release data, artwork via Cover Art Archive
final class MetadataClient {
    private static let userAgent =
        "TrackForge/1.0 (https://github.com/barongartner/TrackForge)"

    var country = "CA"

    private let session: URLSession
    private let artCache = ArtCache()
    private let musicBrainzGate = RateGate(minimumInterval: 1.1)

    init() {
        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = 20
        configuration.httpAdditionalHeaders = ["User-Agent": Self.userAgent]
        session = URLSession(configuration: configuration)
    }

    // MARK: - Cleaning

    private static let parenNoise =
        #"\((?:official|lyric|audio|video|music|hd|4k|visuali[sz]er|full)[^)]*\)"#
    private static let bracketNoise =
        #"\[(?:official|lyric|audio|video|music|hd|4k|visuali[sz]er|full)[^\]]*\]"#
    private static let phraseNoise =
        #"\b(official (music )?video|official audio|lyric video|lyrics|audio only|hq|hd|4k|free download)\b"#

    /// Strips the YouTube furniture that wrecks a search.
    static func clean(artist: String?, title: String?) -> (artist: String, title: String) {
        var t = title ?? ""
        for pattern in [parenNoise, bracketNoise, phraseNoise] {
            t = t.replacingOccurrences(
                of: pattern, with: "", options: [.regularExpression, .caseInsensitive])
        }
        t = t.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: " -–—|"))

        let a = (artist ?? "").replacingOccurrences(
            of: #"\s*-\s*Topic$"#, with: "",
            options: [.regularExpression, .caseInsensitive]).trimmed

        return (a, t)
    }

    /// "Artist - Title (Official Video)" → ("Artist", "Title"). Best effort.
    static func splitVideoTitle(_ raw: String?) -> (artist: String, title: String) {
        let value = raw ?? ""
        for separator in [" - ", " – ", " — ", " -- ", ": "] {
            guard let range = value.range(of: separator) else { continue }
            let left = String(value[value.startIndex..<range.lowerBound]).trimmed
            let right = String(value[range.upperBound...]).trimmed
            if left.count > 1 && left.count < 60 {
                return clean(artist: left, title: right)
            }
        }
        return clean(artist: "", title: value)
    }

    // MARK: - Sources

    private func json(_ urlString: String) async -> Any? {
        guard let url = URL(string: urlString) else { return nil }
        do {
            let (data, response) = try await session.data(from: url)
            if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                return nil
            }
            return try JSONSerialization.jsonObject(with: data)
        } catch {
            return nil   // a network or parse failure is an empty result, not a crash
        }
    }

    private static func escape(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""
    }

    private static func string(_ d: [String: Any], _ key: String) -> String {
        if let s = d[key] as? String { return s }
        return ""
    }

    private static func number(_ d: [String: Any], _ key: String) -> String {
        guard let n = d[key] as? NSNumber else { return "" }
        return n.stringValue
    }

    private static func take4(_ s: String) -> String {
        s.count >= 4 ? String(s.prefix(4)) : s
    }

    func iTunes(artist: String, title: String, limit: Int = 8) async -> [MatchCandidate] {
        let (a, t) = Self.clean(artist: artist, title: title)
        let term = "\(a) \(t)".trimmed
        guard !term.isEmpty else { return [] }

        guard let root = await json(
            "https://itunes.apple.com/search?term=\(Self.escape(term))"
            + "&entity=song&limit=\(limit)&country=\(country)") as? [String: Any],
              let results = root["results"] as? [[String: Any]]
        else { return [] }

        return results.map { r in
            let c = MatchCandidate()
            let art = Self.string(r, "artworkUrl100")
            c.source = "iTunes"
            c.title = Self.string(r, "trackName")
            c.artist = Self.string(r, "artistName")
            c.album = Self.string(r, "collectionName")
            let collectionArtist = Self.string(r, "collectionArtistName")
            c.albumArtist = collectionArtist.isEmpty ? c.artist : collectionArtist
            c.year = Self.take4(Self.string(r, "releaseDate"))
            c.genre = Self.string(r, "primaryGenreName")
            c.trackNumber = Self.number(r, "trackNumber")
            c.trackCount = Self.number(r, "trackCount")
            c.discNumber = Self.number(r, "discNumber")
            c.durationSeconds = (Int(Self.number(r, "trackTimeMillis")) ?? 0) / 1000
            c.artURL = art.replacingOccurrences(
                of: #"/\d+x\d+bb\.jpg$"#, with: "/1000x1000bb.jpg",
                options: .regularExpression)
            c.artThumbURL = art
            return c
        }
    }

    func deezer(artist: String, title: String, limit: Int = 6) async -> [MatchCandidate] {
        let (a, t) = Self.clean(artist: artist, title: title)
        let term = "\(a) \(t)".trimmed
        guard !term.isEmpty else { return [] }

        guard let root = await json(
            "https://api.deezer.com/search?q=\(Self.escape(term))&limit=\(limit)") as? [String: Any],
              let results = root["data"] as? [[String: Any]]
        else { return [] }

        return results.map { r in
            let c = MatchCandidate()
            let album = r["album"] as? [String: Any] ?? [:]
            let performer = (r["artist"] as? [String: Any]).map { Self.string($0, "name") } ?? ""

            c.source = "Deezer"
            c.title = Self.string(r, "title")
            c.artist = performer
            c.albumArtist = performer
            c.album = Self.string(album, "title")
            c.year = Self.take4(Self.string(r, "release_date"))
            c.trackNumber = Self.number(r, "track_position")
            c.discNumber = Self.number(r, "disk_number")
            c.isrc = Self.string(r, "isrc")
            if let bpm = (r["bpm"] as? NSNumber)?.doubleValue, bpm > 0 {
                c.bpm = String(Int(bpm.rounded()))
            }
            c.durationSeconds = Int(Self.number(r, "duration")) ?? 0
            let xl = Self.string(album, "cover_xl")
            c.artURL = xl.isEmpty ? Self.string(album, "cover_big") : xl
            c.artThumbURL = Self.string(album, "cover_small")
            let albumID = Self.number(album, "id")
            c.albumID = albumID.isEmpty ? nil : albumID
            return c
        }
    }

    /// Deezer's track search omits genre and label; the album endpoint has them.
    func enrichFromDeezerAlbum(_ c: MatchCandidate) async {
        guard let albumID = c.albumID, !albumID.isBlank else { return }
        guard let root = await json("https://api.deezer.com/album/\(albumID)") as? [String: Any]
        else { return }

        if c.publisher.isBlank { c.publisher = Self.string(root, "label") }
        if c.year.isBlank { c.year = Self.take4(Self.string(root, "release_date")) }
        if c.trackCount.isBlank { c.trackCount = Self.number(root, "nb_tracks") }
        if c.genre.isBlank,
           let genres = root["genres"] as? [String: Any],
           let data = genres["data"] as? [[String: Any]],
           let first = data.first {
            c.genre = Self.string(first, "name")
        }
    }

    func musicBrainz(artist: String, title: String, limit: Int = 5) async -> [MatchCandidate] {
        let (a, t) = Self.clean(artist: artist, title: title)
        guard !t.isEmpty else { return [] }

        // MusicBrainz asks for no more than one request per second. Honouring that
        // keeps a bulk enrich over 200 tracks polite instead of getting us blocked.
        await musicBrainzGate.wait()

        let query = "recording:\"\(t)\"" + (a.isEmpty ? "" : " AND artist:\"\(a)\"")
        guard let root = await json(
            "https://musicbrainz.org/ws/2/recording/?query=\(Self.escape(query))"
            + "&fmt=json&limit=\(limit)") as? [String: Any],
              let recordings = root["recordings"] as? [[String: Any]]
        else { return [] }

        return recordings.map { r in
            let c = MatchCandidate()
            let releases = r["releases"] as? [[String: Any]] ?? []
            let release = releases.first ?? [:]
            let releaseID = Self.string(release, "id")

            var credit = ""
            for entry in (r["artist-credit"] as? [[String: Any]] ?? []) {
                credit += Self.string(entry, "name") + Self.string(entry, "joinphrase")
            }

            c.source = "MusicBrainz"
            c.title = Self.string(r, "title")
            c.artist = credit
            c.albumArtist = credit
            c.album = Self.string(release, "title")
            let releaseDate = Self.string(release, "date")
            c.year = Self.take4(releaseDate.isEmpty
                ? Self.string(r, "first-release-date") : releaseDate)
            c.isrc = (r["isrcs"] as? [String])?.first ?? ""
            c.durationSeconds = (Int(Self.number(r, "length")) ?? 0) / 1000
            if !releaseID.isEmpty {
                c.artURL = "https://coverartarchive.org/release/\(releaseID)/front-1200"
                c.artThumbURL = "https://coverartarchive.org/release/\(releaseID)/front-250"
            }
            return c
        }
    }

    // MARK: - Combined

    /// Every source, merged, deduped and scored. Best match first.
    func lookup(
        artist: String, title: String,
        durationSeconds: Double = 0, deep: Bool = false
    ) async -> [MatchCandidate] {
        async let itunesTask = iTunes(artist: artist, title: title)
        async let deezerTask = deezer(artist: artist, title: title)

        var all = await itunesTask + deezerTask

        if deep || all.count < 3 {
            all += await musicBrainz(artist: artist, title: title)
        }

        var seen = Set<String>()
        var unique: [MatchCandidate] = []
        for c in all {
            let key = "\(Self.normalise(c.title))|\(Self.normalise(c.album))|\(c.source)"
            guard seen.insert(key).inserted else { continue }
            c.score = (Self.scoreMatch(c, artist: artist, title: title,
                                       duration: durationSeconds) * 10).rounded() / 10
            unique.append(c)
        }

        unique.sort { $0.score > $1.score }

        // Fill in genre/label for the best few Deezer hits, which the search omits.
        for c in unique.prefix(4) where c.source == "Deezer" && c.genre.isEmpty {
            await enrichFromDeezerAlbum(c)
        }

        return unique
    }

    /// Folds the candidate list into one result that has as many fields filled as
    /// possible. No single source carries everything — iTunes has track numbers
    /// and genre, Deezer has ISRC and year, MusicBrainz has ISRC but rarely a
    /// genre — so applying only the top match leaves gaps the user then has to
    /// hunt for by hand.
    ///
    /// The best match is the base. Every still-empty field is filled from the next
    /// candidate that has it, but only from candidates close enough in score to be
    /// the same recording — otherwise a weak match for a different song donates
    /// its album and quietly corrupts the tags.
    static func merge(_ candidates: [MatchCandidate]) -> MatchCandidate? {
        guard let best = candidates.first else { return nil }

        let merged = MatchCandidate()
        merged.source = best.source
        merged.score = best.score
        merged.title = best.title
        merged.artist = best.artist
        merged.album = best.album
        merged.albumArtist = best.albumArtist
        merged.year = best.year
        merged.genre = best.genre
        merged.trackNumber = best.trackNumber
        merged.trackCount = best.trackCount
        merged.discNumber = best.discNumber
        merged.isrc = best.isrc
        merged.publisher = best.publisher
        merged.bpm = best.bpm
        merged.durationSeconds = best.durationSeconds
        merged.artURL = best.artURL
        merged.artThumbURL = best.artThumbURL
        merged.albumID = best.albumID

        // Only trust donors that are plainly the same recording.
        let floor = max(45, best.score - 25)
        var contributors: [String] = []

        let fields: [(ReferenceWritableKeyPath<MatchCandidate, String>)] = [
            \.album, \.albumArtist, \.year, \.genre, \.trackNumber, \.trackCount,
            \.discNumber, \.isrc, \.publisher, \.bpm, \.artURL, \.artThumbURL,
        ]

        for c in candidates.dropFirst() {
            // Sorted descending, so nothing after this qualifies either.
            if c.score < floor { break }

            var used = false
            for field in fields {
                let incoming = c[keyPath: field]
                guard !incoming.isBlank, merged[keyPath: field].isBlank else { continue }
                merged[keyPath: field] = incoming
                used = true
            }
            if used, !contributors.contains(c.source) { contributors.append(c.source) }
        }

        merged.mergedFrom = contributors
        return merged
    }

    private static func normalise(_ s: String?) -> String {
        (s ?? "").lowercased().filter { $0.isLetter || $0.isNumber }
    }

    private static let compilationWords =
        ["greatest hits", "best of", "compilation", "now that", "karaoke",
         "tribute", "made popular"]

    /// How well does a candidate match what we actually asked for?
    private static func scoreMatch(
        _ c: MatchCandidate, artist: String, title: String, duration: Double
    ) -> Double {
        let ct = normalise(c.title), ca = normalise(c.artist)
        let wt = normalise(title), wa = normalise(artist)
        var score: Double = 0

        if !wt.isEmpty && !ct.isEmpty {
            if ct == wt {
                score += 50
            } else if ct.contains(wt) || wt.contains(ct) {
                score += 32
            } else {
                let wanted = Set(wt)
                let shared = wanted.filter { ct.contains($0) }.count
                score += 10.0 * Double(shared) / Double(max(wanted.count, 1))
            }
        }

        if !wa.isEmpty && !ca.isEmpty {
            if ca == wa { score += 30 }
            else if ca.contains(wa) || wa.contains(ca) { score += 20 }
        }

        if duration > 0 && c.durationSeconds > 0 {
            let diff = abs(duration - Double(c.durationSeconds))
            switch diff {
            case ..<2.001: score += 20
            case ..<5.001: score += 12
            case ..<12.001: score += 4
            default: score -= 15
            }
        }

        if !c.artURL.isEmpty { score += 5 }
        if !c.year.isEmpty { score += 3 }
        if c.source == "iTunes" { score += 4 }

        let album = c.album.lowercased()
        if compilationWords.contains(where: album.contains) { score -= 12 }
        if c.artist.localizedCaseInsensitiveContains("karaoke") { score -= 40 }

        return score
    }

    // MARK: - Cover art

    struct ArtOption: Identifiable {
        let id = UUID()
        let url: String
        let thumbURL: String
        let source: String
        let label: String
    }

    func findArt(artist: String, album: String, limit: Int = 8) async -> [ArtOption] {
        let term = "\(artist) \(album)".trimmed
        guard !term.isEmpty else { return [] }

        var options: [ArtOption] = []
        var seen = Set<String>()

        if let root = await json(
            "https://itunes.apple.com/search?term=\(Self.escape(term))"
            + "&entity=album&limit=\(limit)&country=\(country)") as? [String: Any],
           let results = root["results"] as? [[String: Any]] {
            for r in results {
                let thumb = Self.string(r, "artworkUrl100")
                let full = thumb.replacingOccurrences(
                    of: #"/\d+x\d+bb\.jpg$"#, with: "/1500x1500bb.jpg",
                    options: .regularExpression)
                guard !full.isEmpty, seen.insert(full).inserted else { continue }
                options.append(ArtOption(
                    url: full, thumbURL: thumb, source: "iTunes",
                    label: "\(Self.string(r, "collectionName")) "
                         + "(\(Self.take4(Self.string(r, "releaseDate"))))"))
            }
        }

        if let root = await json(
            "https://api.deezer.com/search/album?q=\(Self.escape(term))&limit=\(limit)")
            as? [String: Any],
           let results = root["data"] as? [[String: Any]] {
            for r in results {
                let full = Self.string(r, "cover_xl")
                guard !full.isEmpty, seen.insert(full).inserted else { continue }
                options.append(ArtOption(
                    url: full, thumbURL: Self.string(r, "cover_small"),
                    source: "Deezer", label: Self.string(r, "title")))
            }
        }

        return options
    }

    func downloadArt(_ urlString: String?) async -> Data? {
        guard let urlString, !urlString.isBlank, let url = URL(string: urlString) else { return nil }
        if let cached = await artCache.get(urlString) { return cached }

        do {
            let (data, response) = try await session.data(from: url)
            if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                return nil
            }
            guard data.count >= 512 else { return nil }
            await artCache.put(urlString, data)
            return data
        } catch {
            return nil
        }
    }
}

/// Artwork is cached in memory by URL, capped so a long session cannot grow
/// without bound.
private actor ArtCache {
    private var storage: [String: Data] = [:]
    private let limit = 300

    func get(_ key: String) -> Data? { storage[key] }

    func put(_ key: String, _ value: Data) {
        guard storage.count < limit else { return }
        storage[key] = value
    }
}

/// Serialises calls and holds them apart by a minimum interval.
private actor RateGate {
    private let minimumInterval: TimeInterval
    private var lastCall: Date = .distantPast

    init(minimumInterval: TimeInterval) { self.minimumInterval = minimumInterval }

    func wait() async {
        let elapsed = Date().timeIntervalSince(lastCall)
        if elapsed < minimumInterval {
            let remaining = minimumInterval - elapsed
            try? await Task.sleep(nanoseconds: UInt64(remaining * 1_000_000_000))
        }
        lastCall = Date()
    }
}
