import Foundation

/// One possible metadata match returned by an online source.
final class MatchCandidate: Identifiable, @unchecked Sendable {
    let id = UUID()

    var source = ""
    var title = ""
    var artist = ""
    var album = ""
    var albumArtist = ""
    var year = ""
    var genre = ""
    var trackNumber = ""
    var trackCount = ""
    var discNumber = ""
    var isrc = ""
    var publisher = ""
    var bpm = ""
    var durationSeconds = 0
    var artURL = ""
    var artThumbURL = ""
    var albumID: String?
    var score: Double = 0

    /// Extra sources that donated fields when this is a merged result.
    var mergedFrom: [String] = []

    init() {}

    var sourceLabel: String {
        mergedFrom.isEmpty ? source : "\(source) + \(mergedFrom.joined(separator: " + "))"
    }

    var display: String {
        var s = "\(artist) - \(title)"
        if !album.isBlank { s += "  [\(album)" }
        if !year.isBlank { s += " \(year)" }
        if !album.isBlank { s += "]" }
        s += String(format: "  (%@ %.0f)", source, score)
        return s
    }

    /// Copy the populated fields onto a track.
    func apply(to t: Track, overwrite: Bool, titleCase: Bool, only: [String]? = nil) {
        let allow = only.map { Set($0.map { $0.lowercased() }) }

        func want(_ field: String, _ current: String, _ incoming: String) -> Bool {
            guard !incoming.isBlank else { return false }
            if let allow, !allow.contains(field) { return false }
            return overwrite || current.isBlank
        }
        func cased(_ s: String) -> String { titleCase ? NameFormatter.titleCase(s) : s }

        if want("title", t.title, title) { t.title = cased(title) }
        if want("artist", t.artist, artist) { t.artist = cased(artist) }
        if want("albumartist", t.albumArtist, albumArtist) { t.albumArtist = cased(albumArtist) }
        if want("album", t.album, album) { t.album = cased(album) }
        if want("year", t.year, year) { t.year = year }
        if want("genre", t.genre, genre) { t.genre = genre }
        if want("track", t.trackNumber, trackNumber) { t.trackNumber = trackNumber }
        if want("trackcount", t.trackCount, trackCount) { t.trackCount = trackCount }
        if want("disc", t.discNumber, discNumber) { t.discNumber = discNumber }
        if want("isrc", t.isrc, isrc) { t.isrc = isrc }
        if want("publisher", t.publisher, publisher) { t.publisher = publisher }
        if want("bpm", t.bpm, bpm) { t.bpm = bpm }
    }
}
