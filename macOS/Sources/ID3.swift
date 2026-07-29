import Foundation

/// A hand-rolled ID3v2 reader and writer.
///
/// The Windows build leans on TagLib#. There is no equivalent Swift package worth
/// taking a dependency on, and shelling out to ffmpeg for tag writes gives no
/// control over which frames land — ffmpeg silently demotes anything it does not
/// recognise to a TXXX frame, which loses TKEY and TSRC. So we do it ourselves.
///
/// Reads v2.2, v2.3 and v2.4. Writes v2.3, because that is the version every DJ
/// tool and media player agrees on, and strips ID3v1 on the way out — it cannot
/// hold most of what we write and only gives a player something stale to fall
/// back to.
enum ID3 {

    // MARK: - Model

    struct Picture {
        var mimeType: String
        var pictureType: UInt8
        var description: String
        var data: Data
    }

    struct Tag {
        var title = ""
        var artists: [String] = []
        var albumArtists: [String] = []
        var album = ""
        var genre = ""
        var year = ""
        var trackNumber = ""
        var trackCount = ""
        var discNumber = ""
        var bpm = ""
        var musicalKey = ""
        var isrc = ""
        var publisher = ""
        var composer = ""
        var comment = ""
        var camelot = ""
        var sourceURL = ""
        var rating = 0
        var pictures: [Picture] = []
    }

    // MARK: - Reading

    /// Reads the tag from an MP3. Returns an empty tag when there is none.
    /// `pictureData` false skips decoding APIC payloads, which makes a library
    /// scan of thousands of files dramatically cheaper — we only need to know
    /// whether art exists, not what it looks like.
    static func read(path: String, pictureData: Bool = true) -> Tag? {
        guard let handle = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? handle.close() }

        guard let header = try? handle.read(upToCount: 10), header.count == 10,
              header[0] == 0x49, header[1] == 0x44, header[2] == 0x33 // "ID3"
        else { return Tag() }

        let major = header[3]
        let flags = header[5]
        let size = Int(syncsafe(slice(header, 6, 4)))
        guard size > 0, size < 64 * 1024 * 1024 else { return Tag() }

        guard var body = try? handle.read(upToCount: size), body.count > 0 else { return Tag() }

        // Whole-tag unsynchronisation: every 0xFF 0x00 pair collapses back to 0xFF.
        if flags & 0x80 != 0 { body = deunsynchronise(body) }

        // Extended header — we have no use for it, so step over it.
        var cursor = 0
        if flags & 0x40 != 0, body.count >= 4 {
            let extSize = major >= 4
                ? Int(syncsafe(slice(body, 0, 4)))
                : Int(bigEndian(slice(body, 0, 4))) + 4
            cursor = min(extSize, body.count)
        }

        var tag = Tag()
        let idLength = major == 2 ? 3 : 4
        let sizeLength = major == 2 ? 3 : 4
        let flagLength = major == 2 ? 0 : 2

        while cursor + idLength + sizeLength + flagLength <= body.count {
            let idBytes = slice(body, cursor, idLength)
            // Padding: a run of zero bytes where a frame ID should be.
            if idBytes.allSatisfy({ $0 == 0 }) { break }

            guard let frameID = String(bytes: idBytes, encoding: .isoLatin1) else { break }

            let rawSize = slice(body, cursor + idLength, sizeLength)
            // v2.4 frame sizes are syncsafe; v2.2 and v2.3 are plain big-endian.
            var frameSize = major >= 4 ? Int(syncsafe(rawSize)) : Int(bigEndian(rawSize))

            // Some taggers write v2.4 frames with v2.3 plain sizes. If the syncsafe
            // reading runs past the end of the tag, fall back to the plain one.
            if major >= 4, frameSize > body.count - cursor - 10 {
                let plain = Int(bigEndian(rawSize))
                if plain <= body.count - cursor - 10 { frameSize = plain }
            }

            let dataStart = cursor + idLength + sizeLength + flagLength
            guard frameSize > 0, dataStart + frameSize <= body.count else { break }

            let payload = Data(body[body.startIndex + dataStart ..< body.startIndex + dataStart + frameSize])
            apply(frameID: frameID, payload: payload, major: major,
                  into: &tag, wantPictureData: pictureData)

            cursor = dataStart + frameSize
        }

        return tag
    }

    private static func apply(
        frameID: String, payload: Data, major: UInt8,
        into tag: inout Tag, wantPictureData: Bool
    ) {
        // v2.2 used three-character IDs. Normalise to the v2.3 names so there is
        // one switch rather than two.
        let id = v22Alias[frameID] ?? frameID

        switch id {
        case "TIT2": tag.title = decodeText(payload).first ?? ""
        case "TPE1": tag.artists = decodeText(payload)
        case "TPE2": tag.albumArtists = decodeText(payload)
        case "TALB": tag.album = decodeText(payload).first ?? ""
        case "TCON": tag.genre = normaliseGenre(decodeText(payload).first ?? "")
        case "TCOM": tag.composer = decodeText(payload).first ?? ""
        case "TPUB": tag.publisher = decodeText(payload).first ?? ""
        case "TBPM": tag.bpm = digitsOnly(decodeText(payload).first ?? "")
        case "TKEY": tag.musicalKey = decodeText(payload).first ?? ""
        case "TSRC": tag.isrc = decodeText(payload).first ?? ""

        case "TYER", "TDRC", "TDRL", "TDAT":
            // TDRC carries a full ISO timestamp in v2.4; we only ever want the year.
            let value = decodeText(payload).first ?? ""
            let year = String(value.prefix(4))
            if tag.year.isEmpty, year.count == 4, Int(year) != nil { tag.year = year }

        case "TRCK":
            let parts = (decodeText(payload).first ?? "").split(separator: "/")
            tag.trackNumber = digitsOnly(parts.first.map(String.init) ?? "")
            if parts.count > 1 { tag.trackCount = digitsOnly(String(parts[1])) }

        case "TPOS":
            let parts = (decodeText(payload).first ?? "").split(separator: "/")
            tag.discNumber = digitsOnly(parts.first.map(String.init) ?? "")

        case "TXXX":
            let (description, value) = decodeUserText(payload)
            if description.uppercased() == "CAMELOT" { tag.camelot = value }

        case "COMM":
            tag.comment = decodeComment(payload)

        case "WOAS", "WOAF", "WORS":
            if tag.sourceURL.isEmpty {
                tag.sourceURL = String(bytes: payload, encoding: .isoLatin1)?
                    .trimmingCharacters(in: CharacterSet(charactersIn: "\0")) ?? ""
            }

        case "POPM":
            tag.rating = decodePopularimeter(payload)

        case "APIC":
            if let picture = decodePicture(payload, major: major, wantData: wantPictureData) {
                tag.pictures.append(picture)
            }

        default:
            break
        }
    }

    private static let v22Alias: [String: String] = [
        "TT2": "TIT2", "TP1": "TPE1", "TP2": "TPE2", "TAL": "TALB",
        "TCO": "TCON", "TCM": "TCOM", "TPB": "TPUB", "TBP": "TBPM",
        "TKE": "TKEY", "TRC": "TSRC", "TYE": "TYER", "TRK": "TRCK",
        "TPA": "TPOS", "TXX": "TXXX", "COM": "COMM", "PIC": "APIC",
        "POP": "POPM", "WAS": "WOAS",
    ]

    // MARK: - Text decoding

    /// A text frame is an encoding byte followed by the text. Multi-value frames
    /// separate values with a null, which is how "Artist A / Artist B" round-trips.
    private static func decodeText(_ payload: Data) -> [String] {
        guard let first = payload.first else { return [] }
        let body = payload.dropFirst()
        guard let text = decodeString(Data(body), encoding: first) else { return [] }

        return text
            .components(separatedBy: CharacterSet(charactersIn: "\0"))
            .map { $0.trimmed }
            .filter { !$0.isEmpty }
    }

    private static func decodeString(_ data: Data, encoding: UInt8) -> String? {
        switch encoding {
        case 0: return String(bytes: data, encoding: .isoLatin1)
        case 1:
            // UTF-16 with a byte-order mark.
            if data.count >= 2, data[data.startIndex] == 0xFF, data[data.startIndex + 1] == 0xFE {
                return String(bytes: data.dropFirst(2), encoding: .utf16LittleEndian)
            }
            if data.count >= 2, data[data.startIndex] == 0xFE, data[data.startIndex + 1] == 0xFF {
                return String(bytes: data.dropFirst(2), encoding: .utf16BigEndian)
            }
            return String(bytes: data, encoding: .utf16LittleEndian)
        case 2: return String(bytes: data, encoding: .utf16BigEndian)
        case 3: return String(bytes: data, encoding: .utf8)
        default: return String(bytes: data, encoding: .isoLatin1)
        }
    }

    /// TXXX: encoding, description, null, value.
    private static func decodeUserText(_ payload: Data) -> (String, String) {
        guard let encoding = payload.first else { return ("", "") }
        let body = Data(payload.dropFirst())
        guard let split = splitOnTerminator(body, encoding: encoding) else { return ("", "") }
        return (decodeString(split.0, encoding: encoding) ?? "",
                decodeString(split.1, encoding: encoding)?
                    .trimmingCharacters(in: CharacterSet(charactersIn: "\0")) ?? "")
    }

    /// COMM: encoding, three-byte language, short description, null, text.
    private static func decodeComment(_ payload: Data) -> String {
        guard payload.count > 4, let encoding = payload.first else { return "" }
        let body = Data(payload.dropFirst(4))   // encoding byte + language
        guard let split = splitOnTerminator(body, encoding: encoding) else { return "" }
        return decodeString(split.1, encoding: encoding)?
            .trimmingCharacters(in: CharacterSet(charactersIn: "\0")) ?? ""
    }

    /// Splits at the first string terminator — one null byte for the single-byte
    /// encodings, two for the UTF-16 ones.
    private static func splitOnTerminator(_ data: Data, encoding: UInt8) -> (Data, Data)? {
        let wide = (encoding == 1 || encoding == 2)
        let bytes = [UInt8](data)

        if wide {
            var i = 0
            while i + 1 < bytes.count {
                if bytes[i] == 0 && bytes[i + 1] == 0 {
                    return (Data(bytes[0..<i]), Data(bytes[(i + 2)...]))
                }
                i += 2
            }
        } else if let i = bytes.firstIndex(of: 0) {
            return (Data(bytes[0..<i]), Data(bytes[(i + 1)...]))
        }
        return (data, Data())
    }

    private static func decodePicture(_ payload: Data, major: UInt8, wantData: Bool) -> Picture? {
        guard let encoding = payload.first else { return nil }
        var bytes = [UInt8](payload.dropFirst())

        var mime = ""
        if major == 2 {
            // v2.2 PIC uses a fixed three-character format code, not a MIME type.
            guard bytes.count > 3 else { return nil }
            let code = String(bytes: bytes[0..<3], encoding: .isoLatin1)?.uppercased() ?? ""
            mime = code == "PNG" ? "image/png" : "image/jpeg"
            bytes = Array(bytes[3...])
        } else {
            guard let nul = bytes.firstIndex(of: 0) else { return nil }
            mime = String(bytes: bytes[0..<nul], encoding: .isoLatin1) ?? "image/jpeg"
            bytes = Array(bytes[(nul + 1)...])
        }

        guard let pictureType = bytes.first else { return nil }
        bytes = Array(bytes.dropFirst())

        guard let split = splitOnTerminator(Data(bytes), encoding: encoding) else { return nil }
        let description = decodeString(split.0, encoding: encoding) ?? ""

        return Picture(
            mimeType: mime.isEmpty ? "image/jpeg" : mime,
            pictureType: pictureType,
            description: description,
            data: wantData ? split.1 : Data())
    }

    /// POPM stores 0–255. The five-star buckets match what Windows Media Player
    /// and every tool that copied it actually write.
    private static func decodePopularimeter(_ payload: Data) -> Int {
        let bytes = [UInt8](payload)
        guard let nul = bytes.firstIndex(of: 0), nul + 1 < bytes.count else { return 0 }
        switch bytes[nul + 1] {
        case 0: return 0
        case 1: return 1
        case 2...64: return 2
        case 65...128: return 3
        case 129...196: return 4
        default: return 5
        }
    }

    // MARK: - Writing

    private static let popmScale: [UInt8] = [0, 1, 64, 128, 196, 255]

    /// Rewrites the file's tag as ID3v2.3 and drops any ID3v1 trailer.
    /// Only populated fields are written; blanks leave the existing value alone,
    /// which is why the caller merges into a freshly-read tag first.
    static func write(_ tag: Tag, to path: String) throws {
        let url = URL(fileURLWithPath: path)
        let audio = try audioPayload(of: url)

        var frames = Data()
        func text(_ id: String, _ value: String) {
            guard !value.isBlank else { return }
            frames.append(frame(id, textPayload(value)))
        }

        text("TIT2", tag.title)
        text("TPE1", tag.artists.joined(separator: "/"))
        text("TPE2", tag.albumArtists.joined(separator: "/"))
        text("TALB", tag.album)
        // Plain text, never the "(17)" numeric reference — that is what made
        // genres show up as bare numbers in other players.
        text("TCON", tag.genre)
        text("TCOM", tag.composer)
        text("TPUB", tag.publisher)
        text("TYER", tag.year)
        text("TBPM", tag.bpm)
        text("TKEY", tag.musicalKey)
        text("TSRC", tag.isrc)

        if !tag.trackNumber.isBlank {
            let value = tag.trackCount.isBlank
                ? tag.trackNumber
                : "\(tag.trackNumber)/\(tag.trackCount)"
            text("TRCK", value)
        }
        text("TPOS", tag.discNumber)

        if !tag.comment.isBlank {
            frames.append(frame("COMM", commentPayload(tag.comment)))
        }
        if !tag.camelot.isBlank {
            frames.append(frame("TXXX", userTextPayload(description: "CAMELOT", value: tag.camelot)))
        }
        if !tag.sourceURL.isBlank {
            var payload = Data()
            payload.append(contentsOf: Array(tag.sourceURL.utf8))
            frames.append(frame("WOAS", payload))
        }
        if tag.rating > 0 {
            var payload = Data()
            payload.append(contentsOf: Array("TrackForge".utf8))
            payload.append(0)
            payload.append(popmScale[min(max(tag.rating, 0), 5)])
            payload.append(contentsOf: [0, 0, 0, 0])   // play counter
            frames.append(frame("POPM", payload))
        }
        for picture in tag.pictures {
            frames.append(frame("APIC", picturePayload(picture)))
        }

        // A little padding so a later edit that grows by a few bytes does not
        // force the whole audio stream to shift.
        let padding = Data(repeating: 0, count: 1024)

        var header = Data()
        header.append(contentsOf: [0x49, 0x44, 0x33])   // "ID3"
        header.append(contentsOf: [3, 0])               // v2.3.0
        header.append(0)                                // no flags
        header.append(syncsafeBytes(UInt32(frames.count + padding.count)))

        var output = header
        output.append(frames)
        output.append(padding)
        output.append(audio)

        // Write beside the original and swap, so a crash mid-write cannot leave a
        // half-tagged file where the music used to be.
        let temporary = url.deletingLastPathComponent()
            .appendingPathComponent(".trackforge-\(UUID().uuidString).tmp")
        try output.write(to: temporary, options: .atomic)

        do {
            _ = try FileManager.default.replaceItemAt(url, withItemAt: temporary)
        } catch {
            try? FileManager.default.removeItem(at: temporary)
            throw error
        }
    }

    /// The file with any leading ID3v2 tag and trailing ID3v1 tag removed.
    private static func audioPayload(of url: URL) throws -> Data {
        let data = try Data(contentsOf: url)
        var start = 0
        var end = data.count

        if data.count >= 10, data[0] == 0x49, data[1] == 0x44, data[2] == 0x33 {
            let size = Int(syncsafe(slice(data, 6, 4)))
            let footer = (data[5] & 0x10) != 0 ? 10 : 0   // v2.4 footer
            start = min(10 + size + footer, data.count)
        }

        if end - start >= 128 {
            let tagStart = end - 128
            if data[tagStart] == 0x54, data[tagStart + 1] == 0x41, data[tagStart + 2] == 0x47 {
                end = tagStart   // "TAG"
            }
        }

        guard start < end else { return Data() }
        return Data(data[start..<end])
    }

    private static func frame(_ id: String, _ payload: Data) -> Data {
        var out = Data()
        out.append(contentsOf: Array(id.utf8).prefix(4))
        out.append(bigEndianBytes(UInt32(payload.count)))   // v2.3: plain size
        out.append(contentsOf: [0, 0])                      // no frame flags
        out.append(payload)
        return out
    }

    /// ISO-8859-1 when the text fits in it, UTF-16 otherwise. Latin-1 keeps the
    /// frame small and is the widest-supported encoding; UTF-16 is the only other
    /// one v2.3 defines, so there is no UTF-8 option to reach for here.
    private static func encodedText(_ value: String) -> (UInt8, Data) {
        if let latin1 = value.data(using: .isoLatin1) {
            return (0, latin1)
        }
        var data = Data([0xFF, 0xFE])   // UTF-16LE byte-order mark
        data.append(value.data(using: .utf16LittleEndian) ?? Data())
        return (1, data)
    }

    private static func textPayload(_ value: String) -> Data {
        let (encoding, bytes) = encodedText(value)
        var payload = Data([encoding])
        payload.append(bytes)
        return payload
    }

    private static func userTextPayload(description: String, value: String) -> Data {
        let (encoding, descriptionBytes) = encodedText(description)
        var payload = Data([encoding])
        payload.append(descriptionBytes)
        payload.append(contentsOf: encoding == 0 ? [0] : [0, 0])
        payload.append(encodedAs(value, encoding: encoding))
        return payload
    }

    private static func commentPayload(_ value: String) -> Data {
        let (encoding, _) = encodedText(value)
        var payload = Data([encoding])
        payload.append(contentsOf: Array("eng".utf8))
        payload.append(contentsOf: encoding == 0 ? [0] : [0, 0])   // empty description
        payload.append(encodedAs(value, encoding: encoding))
        return payload
    }

    private static func encodedAs(_ value: String, encoding: UInt8) -> Data {
        if encoding == 0 { return value.data(using: .isoLatin1) ?? Data() }
        var data = Data([0xFF, 0xFE])
        data.append(value.data(using: .utf16LittleEndian) ?? Data())
        return data
    }

    private static func picturePayload(_ picture: Picture) -> Data {
        var payload = Data([0])   // ISO-8859-1 description
        payload.append(contentsOf: Array(picture.mimeType.utf8))
        payload.append(0)
        payload.append(picture.pictureType)
        payload.append(contentsOf: Array(picture.description.utf8))
        payload.append(0)
        payload.append(picture.data)
        return payload
    }

    // MARK: - Byte helpers

    /// Syncsafe integers store seven bits per byte so the tag can never contain a
    /// byte sequence an MPEG decoder would mistake for a frame sync.
    private static func syncsafe<C: Collection>(_ bytes: C) -> UInt32 where C.Element == UInt8 {
        bytes.reduce(UInt32(0)) { ($0 << 7) | (UInt32($1) & 0x7F) }
    }

    private static func bigEndian<C: Collection>(_ bytes: C) -> UInt32 where C.Element == UInt8 {
        bytes.reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
    }

    private static func syncsafeBytes(_ value: UInt32) -> Data {
        Data([
            UInt8((value >> 21) & 0x7F),
            UInt8((value >> 14) & 0x7F),
            UInt8((value >> 7) & 0x7F),
            UInt8(value & 0x7F),
        ])
    }

    private static func bigEndianBytes(_ value: UInt32) -> Data {
        Data([
            UInt8((value >> 24) & 0xFF),
            UInt8((value >> 16) & 0xFF),
            UInt8((value >> 8) & 0xFF),
            UInt8(value & 0xFF),
        ])
    }

    private static func slice(_ data: Data, _ offset: Int, _ count: Int) -> [UInt8] {
        let start = data.startIndex + offset
        let end = min(start + count, data.endIndex)
        guard start < end else { return [] }
        return [UInt8](data[start..<end])
    }

    private static func deunsynchronise(_ data: Data) -> Data {
        var out = Data(capacity: data.count)
        var previous: UInt8 = 0
        for byte in data {
            if previous == 0xFF && byte == 0x00 { previous = 0; continue }
            out.append(byte)
            previous = byte
        }
        return out
    }

    private static func digitsOnly(_ s: String) -> String {
        let digits = s.prefix(while: { $0.isNumber })
        return digits.isEmpty ? "" : String(digits)
    }

    /// "(17)" and "(17)Rock" are legal ID3v1-style genre references. Resolve them
    /// so the Library page shows "Rock" rather than a number.
    private static func normaliseGenre(_ raw: String) -> String {
        let value = raw.trimmed
        guard !value.isEmpty else { return "" }

        if value.hasPrefix("("), let close = value.firstIndex(of: ")") {
            let number = String(value[value.index(after: value.startIndex)..<close])
            let rest = String(value[value.index(after: close)...]).trimmed
            if !rest.isEmpty { return rest }
            if let index = Int(number), index >= 0, index < genres.count { return genres[index] }
        }
        if let index = Int(value), index >= 0, index < genres.count { return genres[index] }
        return value
    }

    /// The ID3v1 genre table, needed only to resolve numeric references on read.
    private static let genres = [
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge",
        "Hip-Hop", "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B",
        "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska",
        "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient",
        "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical",
        "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise",
        "Alternative Rock", "Bass", "Soul", "Punk", "Space", "Meditative",
        "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave",
        "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream",
        "Southern Rock", "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap",
        "Pop/Funk", "Jungle", "Native US", "Cabaret", "New Wave", "Psychedelic",
        "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal", "Acid Punk",
        "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock",
    ]
}
