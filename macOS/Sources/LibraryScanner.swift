import Foundation

/// Walks the library folder and reads tags off every audio file it finds.
final class LibraryScanner {
    private static let skipFolders: Set<String> =
        ["djay", "Backups", ".trackforge", ".Trash", "Automatically Add to Music.localized"]

    /// Apple's own library formats are directory bundles. Descending into them
    /// turns up thousands of managed copies the user never asked us to touch.
    private static let skipExtensions: Set<String> =
        ["musiclibrary", "itlp", "band", "logicx", "photoslibrary", "app"]

    private(set) var tracks: [Track] = []
    private(set) var lastScan: Date?
    private(set) var scannedRoot = ""

    func scan(
        root: String, importDjay: Bool,
        onProgress: ((String) -> Void)? = nil
    ) async throws -> [Track] {
        let found = try await Task.detached(priority: .userInitiated) { () throws -> [Track] in
            let djay = importDjay ? DjayImporter.load(libraryFolder: root) : [:]

            var list: [Track] = []
            guard FileManager.default.fileExists(atPath: root) else { return list }

            var count = 0
            for file in Self.enumerateAudio(root: root) {
                try Task.checkCancellation()

                let track = TagService.read(path: file)
                track.relativePath = Self.relativePath(of: file, from: root)
                if let bpm = djay[(file as NSString).lastPathComponent] { track.djayBpm = bpm }
                list.append(track)

                count += 1
                if count % 25 == 0 {
                    let snapshot = count
                    if let onProgress {
                        await MainActor.run { onProgress("Scanned \(snapshot) files…") }
                    }
                }
            }

            list.sort(by: Self.compareForDisplay)
            return list
        }.value

        tracks = found
        lastScan = Date()
        scannedRoot = root
        return found
    }

    private static func enumerateAudio(root: String) -> [String] {
        var results: [String] = []
        var stack = [root]

        while let directory = stack.popLast() {
            let contents = (try? FileManager.default.contentsOfDirectory(
                atPath: directory)) ?? []

            for name in contents {
                let full = (directory as NSString).appendingPathComponent(name)

                var isDirectory: ObjCBool = false
                guard FileManager.default.fileExists(atPath: full, isDirectory: &isDirectory)
                else { continue }

                if isDirectory.boolValue {
                    if skipFolders.contains(name) { continue }
                    if skipExtensions.contains((name as NSString).pathExtension.lowercased()) {
                        continue
                    }
                    stack.append(full)
                } else if TagService.isAudio(full), !name.hasPrefix(".") {
                    results.append(full)
                }
            }
        }
        return results
    }

    private static func relativePath(of file: String, from root: String) -> String {
        let normalisedRoot = root.hasSuffix("/") ? root : root + "/"
        return file.hasPrefix(normalisedRoot)
            ? String(file.dropFirst(normalisedRoot.count))
            : (file as NSString).lastPathComponent
    }

    /// Album artist, then album, then track number — the way a library reads.
    private static func compareForDisplay(_ a: Track, _ b: Track) -> Bool {
        let artistOrder = sortArtist(a).localizedCaseInsensitiveCompare(sortArtist(b))
        if artistOrder != .orderedSame { return artistOrder == .orderedAscending }

        let albumOrder = a.album.localizedCaseInsensitiveCompare(b.album)
        if albumOrder != .orderedSame { return albumOrder == .orderedAscending }

        return trackNumber(a) < trackNumber(b)
    }

    private static func sortArtist(_ t: Track) -> String {
        if !t.albumArtist.isBlank { return t.albumArtist }
        return t.artist.isBlank ? "~" : t.artist
    }

    private static func trackNumber(_ t: Track) -> Int {
        Int(t.trackNumber.split(separator: "/").first.map(String.init) ?? "") ?? 0
    }
}
