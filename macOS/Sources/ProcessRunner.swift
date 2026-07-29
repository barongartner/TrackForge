import Foundation

struct ProcessResult {
    var exitCode: Int32
    var stdout: String
    var stderr: String
}

enum ProcessError: LocalizedError {
    case couldNotStart(String, String)
    case timedOut(String, TimeInterval)

    var errorDescription: String? {
        switch self {
        case .couldNotStart(let exe, let why):
            return "Could not start \(exe). \(why)"
        case .timedOut(let exe, let seconds):
            return "\(exe) did not respond within \(Int(seconds)) seconds. "
                 + "Try Settings › Install / update tools."
        }
    }
}

/// Runs the external tools.
///
/// Both pipes are always drained concurrently. Reading stdout to the end first
/// deadlocks the moment the child writes more than the pipe buffer to stderr: it
/// blocks on the write, so stdout never closes, so we wait forever. That bug cost
/// a day on the Windows build; it is exactly as real here.
enum ProcessRunner {

    /// Directories a GUI app has to search by hand. Launched from Finder, an app
    /// inherits a bare PATH — Homebrew is not on it, so `ffmpeg` alone finds
    /// nothing even when the user has had it installed for years.
    static let extraSearchPaths = [
        "/opt/homebrew/bin",     // Apple silicon Homebrew
        "/usr/local/bin",        // Intel Homebrew, and most manual installs
        "/opt/local/bin",        // MacPorts
        "/usr/bin",
        "/bin",
    ]

    /// Turns a bare tool name into an absolute path, or returns nil.
    static func which(_ name: String) -> String? {
        if name.contains("/") {
            return FileManager.default.isExecutableFile(atPath: name) ? name : nil
        }

        var dirs = extraSearchPaths
        if let path = ProcessInfo.processInfo.environment["PATH"] {
            dirs += path.split(separator: ":").map(String.init)
        }

        for dir in dirs {
            let candidate = (dir as NSString).appendingPathComponent(name)
            if FileManager.default.isExecutableFile(atPath: candidate) { return candidate }
        }
        return nil
    }

    /// Runs to completion and hands back both streams.
    @discardableResult
    static func run(
        _ executable: String,
        _ arguments: [String],
        timeout: TimeInterval? = nil
    ) async throws -> ProcessResult {
        guard let exe = which(executable) else {
            throw ProcessError.couldNotStart(executable, "It was not found on this Mac.")
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: exe)
        process.arguments = arguments

        let out = Pipe(), err = Pipe()
        process.standardOutput = out
        process.standardError = err

        try process.run()

        // Both readers start before either is awaited, so neither pipe can fill.
        async let stdoutData = readToEnd(out)
        async let stderrData = readToEnd(err)

        if let timeout {
            let deadline = Task {
                try await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
                if process.isRunning { process.terminate() }
            }
            defer { deadline.cancel() }

            let o = await stdoutData, e = await stderrData
            process.waitUntilExit()
            if deadline.isCancelled == false, process.terminationReason == .uncaughtSignal {
                throw ProcessError.timedOut(executable, timeout)
            }
            return ProcessResult(
                exitCode: process.terminationStatus,
                stdout: String(decoding: o, as: UTF8.self),
                stderr: String(decoding: e, as: UTF8.self))
        }

        let o = await stdoutData, e = await stderrData
        process.waitUntilExit()
        return ProcessResult(
            exitCode: process.terminationStatus,
            stdout: String(decoding: o, as: UTF8.self),
            stderr: String(decoding: e, as: UTF8.self))
    }

    /// Runs and streams stdout line by line, so progress can be reported live.
    /// stderr is still drained in the background — see the note above.
    @discardableResult
    static func runStreamingLines(
        _ executable: String,
        _ arguments: [String],
        onLine: @escaping (String) -> Void
    ) async throws -> ProcessResult {
        guard let exe = which(executable) else {
            throw ProcessError.couldNotStart(executable, "It was not found on this Mac.")
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: exe)
        process.arguments = arguments

        let out = Pipe(), err = Pipe()
        process.standardOutput = out
        process.standardError = err

        try process.run()

        async let stderrData = readToEnd(err)

        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            DispatchQueue.global(qos: .userInitiated).async {
                var buffer = Data()
                while true {
                    let chunk = out.fileHandleForReading.availableData
                    if chunk.isEmpty { break }
                    buffer.append(chunk)

                    while let nl = buffer.firstIndex(where: { $0 == 0x0a || $0 == 0x0d }) {
                        let line = String(decoding: buffer[buffer.startIndex..<nl], as: UTF8.self)
                        buffer.removeSubrange(buffer.startIndex...nl)
                        if !line.isEmpty { onLine(line) }
                    }
                }
                if !buffer.isEmpty {
                    onLine(String(decoding: buffer, as: UTF8.self))
                }
                continuation.resume()
            }
        }

        let e = await stderrData
        process.waitUntilExit()
        return ProcessResult(
            exitCode: process.terminationStatus,
            stdout: "",
            stderr: String(decoding: e, as: UTF8.self))
    }

    /// Runs and returns raw stdout bytes — used to pull decoded PCM out of ffmpeg.
    static func runCapturingData(
        _ executable: String,
        _ arguments: [String]
    ) async throws -> Data {
        guard let exe = which(executable) else {
            throw ProcessError.couldNotStart(executable, "It was not found on this Mac.")
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: exe)
        process.arguments = arguments

        let out = Pipe(), err = Pipe()
        process.standardOutput = out
        process.standardError = err

        try process.run()

        async let stdoutData = readToEnd(out)
        async let stderrData = readToEnd(err)

        let data = await stdoutData
        _ = await stderrData
        process.waitUntilExit()
        return data
    }

    private static func readToEnd(_ pipe: Pipe) async -> Data {
        await withCheckedContinuation { (continuation: CheckedContinuation<Data, Never>) in
            DispatchQueue.global(qos: .userInitiated).async {
                let data = (try? pipe.fileHandleForReading.readToEnd()) ?? Data()
                continuation.resume(returning: data)
            }
        }
    }

    /// The last line that looks like an error, for surfacing to the user.
    static func lastError(_ stderr: String) -> String? {
        let lines = stderr.split(whereSeparator: \.isNewline)
            .map { $0.trimmed }
            .filter { !$0.isEmpty }
        let message = lines.last(where: { $0.localizedCaseInsensitiveContains("ERROR") })
            ?? lines.last
        guard let message else { return nil }
        return message.count > 300 ? String(message.prefix(300)) : message
    }
}
