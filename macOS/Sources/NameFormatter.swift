import Foundation

/// Title Case rules and filename building, matching the library convention.
enum NameFormatter {
    // Deliberately small: the library capitalises "Of" and "On", so only true
    // connectors stay lowercase mid-title.
    private static let lowerWords: Set<String> =
        ["a", "an", "the", "and", "or", "nor", "but", "vs", "feat", "ft"]

    /// `/` and `:` are what macOS actually forbids, but the Windows set is kept so
    /// a library shared between the two platforms names files identically.
    private static let illegal = CharacterSet(charactersIn: "<>:\"/\\|?*")
        .union(CharacterSet(charactersIn: UnicodeScalar(0)...UnicodeScalar(31)))

    static func titleCase(_ input: String?) -> String {
        guard let input, !input.isBlank else { return input ?? "" }

        let words = input.replacingOccurrences(of: "_", with: " ")
            .trimmed
            .split(separator: " ", omittingEmptySubsequences: true)
            .map(String.init)

        var out: [String] = []
        for (i, w) in words.enumerated() {
            let bare = w.filter { $0.isLetter }

            // Leave acronyms (B.Y.O.B., ADD) and deliberate inner caps
            // (DDevil, iTunes) alone.
            let acronym = !bare.isEmpty && bare == bare.uppercased()
            let innerCaps = bare.count > 1 && String(bare.dropFirst()) != bare.dropFirst().lowercased()
            if acronym || innerCaps { out.append(w); continue }

            let lower = w.lowercased()
            let middle = i > 0 && i < words.count - 1
            let stripped = lower.trimmingCharacters(in: CharacterSet(charactersIn: ".,()[]\"'"))

            if middle && lowerWords.contains(stripped) {
                out.append(lower)
            } else {
                // Capitalise the first LETTER, not the first character: a word
                // like "(deluxe" starts with a bracket, and upper-casing that
                // changes nothing, which is how "(deluxe Edition)" slipped through.
                if let idx = lower.firstIndex(where: { $0.isLetter }) {
                    out.append(lower[..<idx]
                        + lower[idx].uppercased()
                        + lower[lower.index(after: idx)...])
                } else {
                    out.append(lower)
                }
            }
        }
        return out.joined(separator: " ")
    }

    static func safeFileName(_ s: String?) -> String {
        var cleaned = (s ?? "")
            .components(separatedBy: illegal)
            .joined()
            .trimmed
        while cleaned.hasSuffix(".") { cleaned = String(cleaned.dropLast()) }
        cleaned = cleaned.split(separator: " ", omittingEmptySubsequences: true)
            .joined(separator: " ")
            .trimmed
        return cleaned.isEmpty ? "Untitled" : cleaned
    }

    /// Build a filename from a pattern. Tokens: {track} {tracknum} {title}
    /// {artist} {albumartist} {album} {year}.
    static func buildFileName(_ t: Track, pattern: String, extension ext: String) -> String {
        let rawTrack = (t.trackNumber.split(separator: "/").first.map(String.init) ?? "").trimmed
        let trackNo = Int(rawTrack) ?? 0
        let numeric = trackNo > 0

        let map: [(String, String)] = [
            ("{track}", numeric ? String(format: "%02d", trackNo) : ""),
            ("{tracknum}", numeric ? String(trackNo) : ""),
            ("{title}", titleCase(t.title)),
            ("{artist}", titleCase(t.artist)),
            ("{albumartist}", titleCase(t.albumArtist.isBlank ? t.artist : t.albumArtist)),
            ("{album}", titleCase(t.album)),
            ("{year}", t.year),
        ]

        var name = pattern
        for (token, value) in map {
            name = name.replacingOccurrences(
                of: token, with: value, options: .caseInsensitive)
        }

        name = name.split(separator: " ", omittingEmptySubsequences: true)
            .joined(separator: " ")
            .trimmingCharacters(in: CharacterSet(charactersIn: " -_"))
        if name.isEmpty { name = titleCase(t.title) }

        var extension_ = ext
        if !extension_.hasPrefix(".") { extension_ = "." + extension_ }
        return safeFileName(name) + extension_
    }

    /// Adds " (2)", " (3)"… until the path is free.
    static func uniquePath(_ desired: String) -> String {
        let fm = FileManager.default
        guard fm.fileExists(atPath: desired) else { return desired }

        let url = URL(fileURLWithPath: desired)
        let dir = url.deletingLastPathComponent()
        let stem = url.deletingPathExtension().lastPathComponent
        let ext = url.pathExtension

        for n in 2..<1000 {
            let candidate = dir
                .appendingPathComponent("\(stem) (\(n))")
                .appendingPathExtension(ext)
            if !fm.fileExists(atPath: candidate.path) { return candidate.path }
        }
        return dir
            .appendingPathComponent("\(stem) (\(UUID().uuidString))")
            .appendingPathExtension(ext).path
    }
}
