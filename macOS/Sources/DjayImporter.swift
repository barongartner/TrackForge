import Foundation
import SQLite3

/// Pulls the BPM that Algoriddim djay already analysed out of its MediaLibrary.db,
/// so tracks you've DJ'd with don't get re-analysed from scratch.
///
/// djay stores records as its own "TSAF" binary blobs, which we don't parse
/// properly — we just scrape the printable strings for the file:// URL and pair it
/// with the BPM from the secondary index table. Deliberately best-effort: any
/// failure here just means no djay data, which is fine.
///
/// Its `keySignatureIndex` column is *not* imported. It is an undocumented
/// internal enumeration and guessing at the mapping would put wrong keys in your
/// files.
enum DjayImporter {

    /// Filename → BPM.
    static func load(libraryFolder: String) -> [String: Double] {
        var result: [String: Double] = [:]

        for database in candidateDatabases(libraryFolder: libraryFolder) {
            guard FileManager.default.fileExists(atPath: database) else { continue }
            // Locked, moved, or a format we don't know: skip it.
            scrape(database, into: &result)
        }
        return result
    }

    private static func candidateDatabases(libraryFolder: String) -> [String] {
        let home = NSHomeDirectory()
        let leaf = "djay Media Library/MediaLibrary.db"

        var paths = [
            "\(libraryFolder)/djay/\(leaf)",
            "\(home)/Music/djay/\(leaf)",
            "\(home)/Library/Application Support/djay/\(leaf)",
        ]

        // The App Store builds are sandboxed, so their library lives inside a
        // container. Reading another app's container needs Full Disk Access — if
        // we don't have it the open simply fails and we carry on without.
        for bundle in [
            "com.algoriddim.djay-pro-mac",
            "com.algoriddim.djay-pro-mac2",
            "com.algoriddim.djay-mac",
            "com.algoriddim.djay-pro-mac-3",
        ] {
            paths.append(
                "\(home)/Library/Containers/\(bundle)/Data/Library/Application Support/djay/\(leaf)")
            paths.append("\(home)/Library/Containers/\(bundle)/Data/Music/djay/\(leaf)")
        }
        return paths
    }

    private static func scrape(_ databasePath: String, into result: inout [String: Double]) {
        // Copy first: djay keeps the live database locked with a WAL open.
        let temporary = FileManager.default.temporaryDirectory
            .appendingPathComponent("trackforge-djay-\(UUID().uuidString).db")
        defer { try? FileManager.default.removeItem(at: temporary) }

        do {
            try FileManager.default.copyItem(
                at: URL(fileURLWithPath: databasePath), to: temporary)
        } catch {
            return
        }

        var handle: OpaquePointer?
        guard sqlite3_open_v2(temporary.path, &handle, SQLITE_OPEN_READONLY, nil) == SQLITE_OK
        else {
            sqlite3_close(handle)
            return
        }
        defer { sqlite3_close(handle) }

        var uuidByRow: [Int64: String] = [:]
        var pathByUUID: [String: String] = [:]
        var bpmByRow: [Int64: Double] = [:]

        query(handle, """
            SELECT rowid, key, data FROM database2 WHERE collection IN \
            ('mediaItemAnalyzedData','localMediaItemLocations')
            """) { statement in
            let rowID = sqlite3_column_int64(statement, 0)
            guard let keyText = sqlite3_column_text(statement, 1) else { return }
            let key = String(cString: keyText)
            uuidByRow[rowID] = key

            guard let blob = sqlite3_column_blob(statement, 2) else { return }
            let length = Int(sqlite3_column_bytes(statement, 2))
            guard length > 0 else { return }

            let data = Data(bytes: blob, count: length)
            if let path = firstFileURL(in: data) { pathByUUID[key] = path }
        }

        query(handle, "SELECT rowid, bpm, manualBPM FROM secondaryIndex_mediaItemAnalyzedDataIndex") {
            statement in
            let rowID = sqlite3_column_int64(statement, 0)
            let auto = sqlite3_column_type(statement, 1) == SQLITE_NULL
                ? nil : sqlite3_column_double(statement, 1)
            let manual = sqlite3_column_type(statement, 2) == SQLITE_NULL
                ? nil : sqlite3_column_double(statement, 2)
            if let bpm = manual ?? auto, bpm > 0 { bpmByRow[rowID] = bpm }
        }

        for (rowID, bpm) in bpmByRow {
            guard let uuid = uuidByRow[rowID], let path = pathByUUID[uuid] else { continue }
            result[(path as NSString).lastPathComponent] = (bpm * 10).rounded() / 10
        }
    }

    private static func query(
        _ handle: OpaquePointer?, _ sql: String, each: (OpaquePointer?) -> Void
    ) {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK else {
            sqlite3_finalize(statement)
            return
        }
        defer { sqlite3_finalize(statement) }

        while sqlite3_step(statement) == SQLITE_ROW { each(statement) }
    }

    /// Scans a TSAF blob for the first printable run that looks like a file URL.
    private static func firstFileURL(in data: Data) -> String? {
        let bytes = [UInt8](data)
        var run: [UInt8] = []

        for byte in bytes {
            if byte >= 0x20 && byte <= 0x7e {
                run.append(byte)
                continue
            }
            if let path = fileURLPath(run) { return path }
            run.removeAll(keepingCapacity: true)
        }
        return fileURLPath(run)
    }

    private static func fileURLPath(_ run: [UInt8]) -> String? {
        guard run.count >= 4,
              let text = String(bytes: run, encoding: .ascii),
              text.lowercased().hasPrefix("file:///")
        else { return nil }

        let encoded = String(text.dropFirst(7))   // keep the leading slash
        return encoded.removingPercentEncoding ?? encoded
    }
}
