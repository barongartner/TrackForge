import AppKit
import AudioToolbox
import ImageIO
import UniformTypeIdentifiers

/// Reads and writes tags + embedded artwork.
///
/// MP3 goes through our own ID3 reader and writer, which is what the whole
/// library is made of and what every download produces. Other containers are read
/// through AVFoundation and written through ffmpeg, which is already a hard
/// dependency — no point hand-rolling an MP4 atom rewriter for the rare FLAC.
enum TagService {

    static let audioExtensions: Set<String> =
        ["mp3", "flac", "m4a", "opus", "ogg", "wav", "aac", "wma", "aiff", "aif"]

    static func isAudio(_ path: String) -> Bool {
        audioExtensions.contains((path as NSString).pathExtension.lowercased())
    }

    static func isMP3(_ path: String) -> Bool {
        (path as NSString).pathExtension.lowercased() == "mp3"
    }

    // MARK: - Reading

    /// An unreadable or exotic file comes back as a partly-filled Track rather
    /// than an error — one bad file must never abort a library scan.
    static func read(path: String, includeArtwork: Bool = false) -> Track {
        let track = Track()
        track.path = path

        let attributes = try? FileManager.default.attributesOfItem(atPath: path)
        track.sizeBytes = (attributes?[.size] as? NSNumber)?.int64Value ?? 0

        let properties = AudioProbe.read(path: path)
        track.durationSeconds = properties.durationSeconds
        track.bitrate = properties.bitrate

        if isMP3(path) {
            guard let tag = ID3.read(path: path, pictureData: includeArtwork) else { return track }
            track.title = tag.title
            track.artist = tag.artists.joined(separator: "; ")
            track.albumArtist = tag.albumArtists.joined(separator: "; ")
            track.album = tag.album
            track.genre = tag.genre
            track.year = tag.year
            track.trackNumber = tag.trackNumber
            track.trackCount = tag.trackCount
            track.discNumber = tag.discNumber
            track.bpm = tag.bpm
            track.musicalKey = tag.musicalKey
            track.camelot = tag.camelot
            track.isrc = tag.isrc
            track.publisher = tag.publisher
            track.composer = tag.composer
            track.comment = tag.comment
            track.sourceURL = tag.sourceURL
            track.rating = tag.rating
            track.hasArt = !tag.pictures.isEmpty
        } else {
            readWithAudioToolbox(into: track)
        }

        return track
    }

    /// AudioToolbox rather than AVAsset. Every synchronous AVAsset metadata
    /// accessor is deprecated, and the async replacements would force a library
    /// scan to await one file at a time — the info dictionary gives us the same
    /// common tags in one call with no such cost.
    private static func readWithAudioToolbox(into track: Track) {
        var fileID: AudioFileID?
        let url = URL(fileURLWithPath: track.path) as CFURL
        guard AudioFileOpenURL(url, .readPermission, 0, &fileID) == noErr,
              let fileID
        else { return }
        defer { AudioFileClose(fileID) }

        // AudioFileGetProperty hands back a +1 reference for these two properties,
        // so it has to go through Unmanaged and be taken as retained.
        var size = UInt32(MemoryLayout<Unmanaged<CFDictionary>?>.size)
        var raw: Unmanaged<CFDictionary>?
        guard AudioFileGetProperty(
            fileID, kAudioFilePropertyInfoDictionary, &size, &raw) == noErr,
              let info = raw?.takeRetainedValue() as? [String: String]
        else { return }

        func value(_ key: String) -> String { info[key]?.trimmed ?? "" }

        track.title = value(kAFInfoDictionary_Title)
        track.artist = value(kAFInfoDictionary_Artist)
        track.album = value(kAFInfoDictionary_Album)
        track.genre = value(kAFInfoDictionary_Genre)
        track.composer = value(kAFInfoDictionary_Composer)
        track.comment = value(kAFInfoDictionary_Comments)
        track.year = String(value(kAFInfoDictionary_Year).prefix(4))
        track.bpm = value(kAFInfoDictionary_Tempo)

        // The info dictionary writes "3/12" for a track in a twelve-track album.
        let trackNumber = value(kAFInfoDictionary_TrackNumber)
            .split(separator: "/").map(String.init)
        track.trackNumber = trackNumber.first ?? ""
        if trackNumber.count > 1 { track.trackCount = trackNumber[1] }

        var artworkSize: UInt32 = 0
        var writable: UInt32 = 0
        if AudioFileGetPropertyInfo(
            fileID, kAudioFilePropertyAlbumArtwork, &artworkSize, &writable) == noErr,
           artworkSize > 0 {
            track.hasArt = true
        }
    }

    static func readArt(path: String) -> Data? {
        if isMP3(path) {
            guard let tag = ID3.read(path: path, pictureData: true) else { return nil }
            // Prefer the front cover; fall back to whatever picture is there.
            let front = tag.pictures.first(where: { $0.pictureType == 3 })
            return (front ?? tag.pictures.first)?.data
        }

        var fileID: AudioFileID?
        let url = URL(fileURLWithPath: path) as CFURL
        guard AudioFileOpenURL(url, .readPermission, 0, &fileID) == noErr, let fileID
        else { return nil }
        defer { AudioFileClose(fileID) }

        var size = UInt32(MemoryLayout<Unmanaged<CFData>?>.size)
        var raw: Unmanaged<CFData>?
        guard AudioFileGetProperty(
            fileID, kAudioFilePropertyAlbumArtwork, &size, &raw) == noErr,
              let data = raw?.takeRetainedValue() as Data?, !data.isEmpty
        else { return nil }
        return data
    }

    // MARK: - Writing

    /// Writes every populated field. Blank fields are left untouched — the
    /// existing tag is read first and only overwritten where we have something.
    static func write(_ t: Track, art: Data? = nil) throws {
        guard isMP3(t.path) else {
            try writeWithFfmpeg(t, art: art)
            return
        }

        // Start from what is already on the file so a blank field on the Track
        // does not silently erase a value we simply had no opinion about.
        var tag = ID3.read(path: t.path, pictureData: true) ?? ID3.Tag()

        func set(_ target: inout String, _ value: String) {
            if !value.isBlank { target = value }
        }

        set(&tag.title, t.title)
        if !t.artist.isBlank { tag.artists = splitArtists(t.artist) }
        if !t.albumArtist.isBlank { tag.albumArtists = splitArtists(t.albumArtist) }
        set(&tag.album, t.album)
        set(&tag.genre, t.genre)
        set(&tag.composer, t.composer)
        set(&tag.publisher, t.publisher)
        set(&tag.comment, t.comment)
        set(&tag.musicalKey, t.musicalKey)
        set(&tag.isrc, t.isrc)
        set(&tag.camelot, t.camelot)
        set(&tag.sourceURL, t.sourceURL)

        if let year = Int(t.year), year > 0 { tag.year = String(year) }
        if let track = Int(t.trackNumber.split(separator: "/").first.map(String.init) ?? ""),
           track > 0 { tag.trackNumber = String(track) }
        if let total = Int(t.trackCount), total > 0 { tag.trackCount = String(total) }
        if let disc = Int(t.discNumber), disc > 0 { tag.discNumber = String(disc) }
        if let bpm = Double(t.bpm), bpm > 0 { tag.bpm = String(Int(bpm.rounded())) }
        if t.rating > 0 { tag.rating = t.rating }

        if let art, !art.isEmpty {
            tag.pictures = [ID3.Picture(
                mimeType: "image/jpeg",
                pictureType: 3,                 // front cover
                description: "Cover",
                data: normaliseArt(art))]
            t.hasArt = true
        }

        try ID3.write(tag, to: t.path)
    }

    private static func splitArtists(_ s: String) -> [String] {
        s.components(separatedBy: CharacterSet(charactersIn: ";/"))
            .map { $0.trimmed }
            .filter { !$0.isEmpty }
    }

    /// Non-MP3 containers go through ffmpeg. `-c copy` means the audio stream is
    /// moved across untouched, so this is a remux, not a re-encode.
    private static func writeWithFfmpeg(_ t: Track, art: Data?) throws {
        guard let ffmpeg = ProcessRunner.which("ffmpeg") ?? ProcessRunner.which(ToolInstaller.ffmpegPath)
        else {
            throw TagError.message(
                "Writing tags to a \((t.path as NSString).pathExtension.uppercased()) file needs "
                + "ffmpeg. Install it from Settings.")
        }

        let source = URL(fileURLWithPath: t.path)
        let output = source.deletingLastPathComponent()
            .appendingPathComponent(".trackforge-\(UUID().uuidString).\(source.pathExtension)")

        var artFile: URL?
        var arguments = ["-y", "-loglevel", "error", "-i", t.path]

        if let art, !art.isEmpty {
            let file = FileManager.default.temporaryDirectory
                .appendingPathComponent("trackforge-art-\(UUID().uuidString).jpg")
            try? normaliseArt(art).write(to: file)
            artFile = file
            arguments += ["-i", file.path, "-map", "0:a", "-map", "1:v",
                          "-disposition:v", "attached_pic"]
        } else {
            arguments += ["-map", "0"]
        }
        arguments += ["-c", "copy"]

        let metadata: [(String, String)] = [
            ("title", t.title), ("artist", t.artist), ("album", t.album),
            ("album_artist", t.albumArtist), ("genre", t.genre), ("date", t.year),
            ("track", t.trackNumber), ("disc", t.discNumber), ("TBPM", t.bpm),
            ("composer", t.composer), ("publisher", t.publisher),
            ("comment", t.comment), ("TKEY", t.musicalKey), ("TSRC", t.isrc),
        ]
        for (key, value) in metadata where !value.isBlank {
            arguments += ["-metadata", "\(key)=\(value)"]
        }
        arguments.append(output.path)

        defer { if let artFile { try? FileManager.default.removeItem(at: artFile) } }

        let result = try runSync(ffmpeg, arguments)
        guard result.exitCode == 0, FileManager.default.fileExists(atPath: output.path) else {
            try? FileManager.default.removeItem(at: output)
            throw TagError.message(
                ProcessRunner.lastError(result.stderr) ?? "ffmpeg could not write those tags.")
        }

        _ = try FileManager.default.replaceItemAt(source, withItemAt: output)
        if let art, !art.isEmpty { t.hasArt = true }
    }

    /// A blocking process run, for the synchronous write path.
    private static func runSync(_ exe: String, _ arguments: [String]) throws -> ProcessResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: exe)
        process.arguments = arguments

        let out = Pipe(), err = Pipe()
        process.standardOutput = out
        process.standardError = err
        try process.run()

        let outData = (try? out.fileHandleForReading.readToEnd()) ?? Data()
        let errData = (try? err.fileHandleForReading.readToEnd()) ?? Data()
        process.waitUntilExit()

        return ProcessResult(
            exitCode: process.terminationStatus,
            stdout: String(decoding: outData, as: UTF8.self),
            stderr: String(decoding: errData, as: UTF8.self))
    }

    // MARK: - Artwork

    /// Square-crop from the centre, cap at 1000px, re-encode JPEG so every cover
    /// in the library matches. Anything that will not decode is passed through
    /// untouched rather than dropped.
    static func normaliseArt(_ data: Data, maxSize: Int = 1000) -> Data {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
        else { return data }

        let side = min(image.width, image.height)
        let target = min(side, maxSize)
        let cropX = (image.width - side) / 2
        let cropY = (image.height - side) / 2

        guard let square = image.cropping(
            to: CGRect(x: cropX, y: cropY, width: side, height: side))
        else { return data }

        guard let context = CGContext(
            data: nil, width: target, height: target,
            bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)
        else { return data }

        context.interpolationQuality = .high
        context.draw(square, in: CGRect(x: 0, y: 0, width: target, height: target))

        guard let scaled = context.makeImage() else { return data }

        let output = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(
            output, UTType.jpeg.identifier as CFString, 1, nil)
        else { return data }

        CGImageDestinationAddImage(destination, scaled, [
            kCGImageDestinationLossyCompressionQuality: 0.92,
        ] as CFDictionary)

        guard CGImageDestinationFinalize(destination) else { return data }
        return output as Data
    }

    static func image(from data: Data?) -> NSImage? {
        guard let data, !data.isEmpty else { return nil }
        return NSImage(data: data)
    }

    static func dimensions(of data: Data) -> (Int, Int)? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int
        else { return nil }
        return (width, height)
    }
}

enum TagError: LocalizedError {
    case message(String)
    var errorDescription: String? {
        if case .message(let text) = self { return text }
        return nil
    }
}
