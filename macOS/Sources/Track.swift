import Foundation

/// One audio file, plus everything we know or want to know about it.
///
/// A reference type on purpose, exactly as on Windows: the tag editor mutates it
/// in place, the enrich job mutates it in place, and `TagService.write` persists
/// whatever is on it. `clone()` gives you a snapshot for when you need one — the
/// grab pipeline takes one so later edits to the card don't affect an in-flight
/// download.
///
/// Marked `@unchecked Sendable` on the same terms: a Track is handed to exactly
/// one job at a time, and the UI only reads it again once that job reports done.
final class Track: Identifiable, Hashable, @unchecked Sendable {
    let id = UUID()

    var path: String = ""
    var relativePath: String = ""

    var title = ""
    var artist = ""
    var albumArtist = ""
    var album = ""
    var genre = ""
    var year = ""
    var trackNumber = ""
    var trackCount = ""
    var discNumber = ""
    var bpm = ""
    var musicalKey = ""
    var camelot = ""
    var isrc = ""
    var publisher = ""
    var composer = ""
    var comment = ""
    var sourceURL = ""
    var rating = 0

    var hasArt = false
    var durationSeconds: Double = 0
    var bitrate = 0
    var sizeBytes: Int64 = 0

    /// BPM that the djay app already worked out, if we could read it.
    var djayBpm: Double?

    var pendingArt: Data?
    var pendingArtURL: String?

    init() {}

    var fileName: String { (path as NSString).lastPathComponent }

    var durationText: String {
        guard durationSeconds > 0 else { return "" }
        let total = Int(durationSeconds.rounded())
        let s = total % 60, m = (total / 60) % 60, h = total / 3600
        return h > 0
            ? String(format: "%d:%02d:%02d", h, m, s)
            : String(format: "%d:%02d", m, s)
    }

    var displayBpm: String {
        if !bpm.trimmed.isEmpty { return bpm }
        if let d = djayBpm { return String(Int(d.rounded())) }
        return ""
    }

    /// Tag fields this file is missing, for the Library "Missing" column.
    func missingFields() -> [String] {
        var missing: [String] = []
        if title.trimmed.isEmpty { missing.append("title") }
        if artist.trimmed.isEmpty { missing.append("artist") }
        if album.trimmed.isEmpty { missing.append("album") }
        if year.trimmed.isEmpty { missing.append("year") }
        if genre.trimmed.isEmpty { missing.append("genre") }
        if trackNumber.trimmed.isEmpty || trackNumber == "0" { missing.append("track") }
        if displayBpm.isEmpty { missing.append("bpm") }
        if !hasArt { missing.append("art") }
        return missing
    }

    var missingText: String { missingFields().joined(separator: ", ") }
    var isComplete: Bool { missingFields().isEmpty }

    func clone() -> Track {
        let c = Track()
        c.path = path; c.relativePath = relativePath
        c.title = title; c.artist = artist; c.albumArtist = albumArtist; c.album = album
        c.genre = genre; c.year = year; c.trackNumber = trackNumber; c.trackCount = trackCount
        c.discNumber = discNumber; c.bpm = bpm; c.musicalKey = musicalKey; c.camelot = camelot
        c.isrc = isrc; c.publisher = publisher; c.composer = composer; c.comment = comment
        c.sourceURL = sourceURL; c.rating = rating
        c.hasArt = hasArt; c.durationSeconds = durationSeconds
        c.bitrate = bitrate; c.sizeBytes = sizeBytes; c.djayBpm = djayBpm
        c.pendingArt = pendingArt; c.pendingArtURL = pendingArtURL
        return c
    }

    /// Copies every tag field across, leaving identity and file stats alone.
    func copyTags(from other: Track) {
        title = other.title; artist = other.artist; album = other.album
        albumArtist = other.albumArtist; genre = other.genre; year = other.year
        trackNumber = other.trackNumber; discNumber = other.discNumber
        bpm = other.bpm; musicalKey = other.musicalKey; camelot = other.camelot
        isrc = other.isrc; publisher = other.publisher; composer = other.composer
        comment = other.comment; hasArt = other.hasArt; path = other.path
    }

    var searchBlob: String {
        [title, artist, albumArtist, album, genre, year, fileName]
            .joined(separator: " ")
            .lowercased()
    }

    static func == (a: Track, b: Track) -> Bool { a.id == b.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

extension StringProtocol {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
    var isBlank: Bool { trimmed.isEmpty }
}
