import Foundation

/// Fetches yt-dlp and ffmpeg into TrackForge's own tools folder so the app works
/// on a clean Mac without the user knowing what Homebrew is.
///
/// These are downloaded rather than shipped inside the app on purpose: ffmpeg
/// builds are GPL and redistributing them carries source-offer obligations, and
/// yt-dlp goes stale within weeks of a YouTube change. Fetching from upstream
/// sidesteps both, and gives the user a working update button.
enum ToolInstaller {

    /// The official standalone build — a universal binary with Python baked in,
    /// so there is nothing to install alongside it.
    static let ytDlpURL =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos"

    /// Per-architecture static ffmpeg. evermeet.cx ships a newer build but only
    /// for Intel; on Apple silicon that would quietly depend on Rosetta being
    /// installed, which it increasingly is not.
    static var ffmpegURL: String {
        let arch = isAppleSilicon ? "arm64" : "x64"
        return "https://github.com/eugeneware/ffmpeg-static/releases/latest/download/ffmpeg-darwin-\(arch)"
    }

    static var isAppleSilicon: Bool {
        var info = utsname()
        uname(&info)
        let machine = withUnsafeBytes(of: &info.machine) { raw -> String in
            String(cString: raw.baseAddress!.assumingMemoryBound(to: CChar.self))
        }
        return machine.hasPrefix("arm")
    }

    /// ~/Library/Application Support/TrackForge/tools — writable without admin rights.
    static let toolsDirectory = AppConfig.configDirectory
        .appendingPathComponent("tools", isDirectory: true)

    static var ytDlpPath: String { toolsDirectory.appendingPathComponent("yt-dlp").path }
    static var ffmpegPath: String { toolsDirectory.appendingPathComponent("ffmpeg").path }

    static var hasYtDlp: Bool { FileManager.default.isExecutableFile(atPath: ytDlpPath) }
    static var hasFfmpeg: Bool { FileManager.default.isExecutableFile(atPath: ffmpegPath) }

    struct Progress {
        var percent: Double
        var message: String
    }

    static func installYtDlp(onProgress: @escaping (Progress) -> Void) async throws {
        try await install(from: ytDlpURL, to: ytDlpPath, label: "yt-dlp", onProgress: onProgress)
    }

    static func installFfmpeg(onProgress: @escaping (Progress) -> Void) async throws {
        try await install(from: ffmpegURL, to: ffmpegPath, label: "ffmpeg", onProgress: onProgress)
    }

    /// Pulls the newest yt-dlp over the top of the existing one.
    static func updateYtDlp(onProgress: @escaping (Progress) -> Void) async throws {
        try await installYtDlp(onProgress: onProgress)
    }

    private static func install(
        from urlString: String, to destination: String, label: String,
        onProgress: @escaping (Progress) -> Void
    ) async throws {
        guard let url = URL(string: urlString) else {
            throw ToolError.message("That download address is not valid.")
        }

        try FileManager.default.createDirectory(
            at: toolsDirectory, withIntermediateDirectories: true)

        onProgress(Progress(percent: 0, message: "Downloading \(label)"))

        var request = URLRequest(url: url)
        request.setValue("TrackForge/1.0", forHTTPHeaderField: "User-Agent")
        request.timeoutInterval = 600

        let (temporary, response) = try await URLSession.shared.download(for: request)
        defer { try? FileManager.default.removeItem(at: temporary) }

        if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
            throw ToolError.message("\(label) download failed (HTTP \(http.statusCode)).")
        }

        let size = (try? FileManager.default
            .attributesOfItem(atPath: temporary.path)[.size] as? NSNumber)??.int64Value ?? 0
        guard size > 1_000_000 else {
            throw ToolError.message("The \(label) download came back empty.")
        }

        onProgress(Progress(percent: 94, message: "Installing \(label)"))

        let target = URL(fileURLWithPath: destination)
        try? FileManager.default.removeItem(at: target)
        try FileManager.default.moveItem(at: temporary, to: target)

        try FileManager.default.setAttributes(
            [.posixPermissions: 0o755], ofItemAtPath: destination)

        // A file written by URLSession carries no quarantine flag, but clear it
        // anyway in case one is inherited, and give the binary an ad-hoc signature.
        // Apple silicon refuses to execute an unsigned arm64 binary outright.
        await shell("/usr/bin/xattr", ["-d", "com.apple.quarantine", destination])
        await shell("/usr/bin/codesign", ["--force", "--sign", "-", destination])

        onProgress(Progress(percent: 100, message: "\(label) installed"))
    }

    /// Best effort — neither of these failing is a reason to abandon the install.
    @discardableResult
    private static func shell(_ executable: String, _ arguments: [String]) async -> Bool {
        guard FileManager.default.isExecutableFile(atPath: executable) else { return false }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
            process.waitUntilExit()
            return process.terminationStatus == 0
        } catch {
            return false
        }
    }
}

enum ToolError: LocalizedError {
    case message(String)
    var errorDescription: String? {
        if case .message(let text) = self { return text }
        return nil
    }
}
