import AVFoundation
import Foundation

struct AudioProperties {
    var durationSeconds: Double = 0
    var bitrate: Int = 0
}

/// Duration and bitrate.
///
/// MP3 is parsed by hand rather than handed to AVFoundation. A library scan opens
/// every file in the folder, and building an AVAsset per file costs milliseconds
/// each — enough to turn a ten-thousand-track scan into a coffee break. Reading
/// one MPEG frame header, plus the Xing/VBRI header when there is one, is exact
/// for CBR and close enough for VBR, and costs a single small read.
enum AudioProbe {

    static func read(path: String) -> AudioProperties {
        if path.lowercased().hasSuffix(".mp3"), let mp3 = readMP3(path: path) {
            return mp3
        }
        return readWithAVFoundation(path: path)
    }

    // MARK: - MP3

    private static func readMP3(path: String) -> AudioProperties? {
        guard let handle = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? handle.close() }

        let attributes = try? FileManager.default.attributesOfItem(atPath: path)
        let totalBytes = (attributes?[.size] as? NSNumber)?.int64Value ?? 0
        guard totalBytes > 0 else { return nil }

        // Step over an ID3v2 tag if there is one.
        var audioStart: UInt64 = 0
        if let header = try? handle.read(upToCount: 10), header.count == 10,
           header[0] == 0x49, header[1] == 0x44, header[2] == 0x33 {
            let size = header[6..<10].reduce(UInt32(0)) { ($0 << 7) | (UInt32($1) & 0x7F) }
            let footer: UInt32 = (header[5] & 0x10) != 0 ? 10 : 0
            audioStart = UInt64(10 + size + footer)
        }

        // An ID3v1 trailer is not audio either.
        var audioBytes = Int64(totalBytes) - Int64(audioStart)
        if audioBytes > 128 {
            try? handle.seek(toOffset: UInt64(totalBytes - 128))
            if let tail = try? handle.read(upToCount: 3), tail.count == 3,
               tail[0] == 0x54, tail[1] == 0x41, tail[2] == 0x47 {
                audioBytes -= 128
            }
        }
        guard audioBytes > 0 else { return nil }

        // Find the first frame sync. Some files carry junk between the tag and the
        // first frame, so scan a window rather than trusting the offset exactly.
        try? handle.seek(toOffset: audioStart)
        guard let window = try? handle.read(upToCount: 64 * 1024), window.count > 4 else { return nil }
        let bytes = [UInt8](window)

        var offset = 0
        var frame: FrameHeader?
        while offset + 4 <= bytes.count {
            if bytes[offset] == 0xFF, (bytes[offset + 1] & 0xE0) == 0xE0,
               let parsed = FrameHeader(bytes, offset) {
                frame = parsed
                break
            }
            offset += 1
        }
        guard let frame else { return nil }

        // A Xing/Info or VBRI header gives the exact frame count, which is the only
        // way to get a VBR file's duration right without decoding the whole thing.
        if let count = vbrFrameCount(bytes, frameStart: offset, frame: frame) {
            let duration = Double(count) * Double(frame.samplesPerFrame) / Double(frame.sampleRate)
            guard duration > 0 else { return nil }
            return AudioProperties(
                durationSeconds: duration,
                bitrate: Int((Double(audioBytes) * 8 / duration / 1000).rounded()))
        }

        // Otherwise assume constant bitrate, which is what the header advertises.
        guard frame.bitrate > 0 else { return nil }
        let duration = Double(audioBytes) * 8 / Double(frame.bitrate * 1000)
        return AudioProperties(durationSeconds: duration, bitrate: frame.bitrate)
    }

    private struct FrameHeader {
        var bitrate: Int          // kbps
        var sampleRate: Int
        var samplesPerFrame: Int
        var isMPEG1: Bool
        var isMono: Bool

        private static let bitratesV1L3 =
            [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
        private static let bitratesV2L3 =
            [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0]
        private static let sampleRates: [Int: [Int]] = [
            3: [44100, 48000, 32000],   // MPEG 1
            2: [22050, 24000, 16000],   // MPEG 2
            0: [11025, 12000, 8000],    // MPEG 2.5
        ]

        init?(_ bytes: [UInt8], _ i: Int) {
            guard i + 4 <= bytes.count else { return nil }

            let versionID = Int((bytes[i + 1] >> 3) & 0x03)
            let layer = Int((bytes[i + 1] >> 1) & 0x03)
            let bitrateIndex = Int((bytes[i + 2] >> 4) & 0x0F)
            let sampleIndex = Int((bytes[i + 2] >> 2) & 0x03)
            let channelMode = Int((bytes[i + 3] >> 6) & 0x03)

            // Only Layer III (0b01) is worth handling — nothing writes MP2 any more.
            guard versionID != 1, layer == 1, sampleIndex != 3,
                  bitrateIndex > 0, bitrateIndex < 15,
                  let rates = Self.sampleRates[versionID]
            else { return nil }

            isMPEG1 = versionID == 3
            isMono = channelMode == 3
            sampleRate = rates[sampleIndex]
            bitrate = isMPEG1
                ? Self.bitratesV1L3[bitrateIndex]
                : Self.bitratesV2L3[bitrateIndex]
            samplesPerFrame = isMPEG1 ? 1152 : 576
        }
    }

    /// Xing and Info live after the frame's side information; VBRI sits at a fixed
    /// offset from the frame start. Both carry a frame count in the same place.
    private static func vbrFrameCount(_ bytes: [UInt8], frameStart: Int, frame: FrameHeader) -> Int? {
        func tag(at offset: Int) -> String? {
            guard offset + 4 <= bytes.count else { return nil }
            return String(bytes: bytes[offset..<(offset + 4)], encoding: .isoLatin1)
        }
        func uint32(at offset: Int) -> Int? {
            guard offset + 4 <= bytes.count else { return nil }
            return bytes[offset..<(offset + 4)].reduce(0) { ($0 << 8) | Int($1) }
        }

        let sideInfo = frame.isMPEG1
            ? (frame.isMono ? 17 : 32)
            : (frame.isMono ? 9 : 17)
        let xingOffset = frameStart + 4 + sideInfo

        if let name = tag(at: xingOffset), name == "Xing" || name == "Info" {
            guard let flags = uint32(at: xingOffset + 4) else { return nil }
            guard flags & 0x01 != 0 else { return nil }   // frame count present
            return uint32(at: xingOffset + 8)
        }

        if let name = tag(at: frameStart + 36), name == "VBRI" {
            return uint32(at: frameStart + 14 + 36)
        }
        return nil
    }

    // MARK: - Everything else

    /// AVAudioFile rather than AVAsset: the asset accessors that return duration
    /// and data rate synchronously are all deprecated, and their async
    /// replacements would make a whole library scan await one file at a time.
    private static func readWithAVFoundation(path: String) -> AudioProperties {
        let url = URL(fileURLWithPath: path)
        guard let file = try? AVAudioFile(forReading: url) else { return AudioProperties() }

        let sampleRate = file.fileFormat.sampleRate
        guard sampleRate > 0, file.length > 0 else { return AudioProperties() }

        let duration = Double(file.length) / sampleRate
        let attributes = try? FileManager.default.attributesOfItem(atPath: path)
        let size = (attributes?[.size] as? NSNumber)?.doubleValue ?? 0

        return AudioProperties(
            durationSeconds: duration,
            bitrate: Int((size * 8 / duration / 1000).rounded()))
    }
}
