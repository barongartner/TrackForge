import Foundation

/// User settings, persisted to ~/Library/Application Support/TrackForge/config.json.
struct AppConfig: Codable {
    var libraryFolder: String = AppConfig.defaultMusicFolder
    var outputFolder: String = AppConfig.defaultMusicFolder
    var format = "mp3"
    var bitrate = "320"
    var filenamePattern = "{track} {title}"
    var analyzeBpmAndKey = true
    var autoArt = true
    var forceTitleCase = true
    var writeSourceURL = true
    var importDjayData = true
    var skipDuplicates = true
    var cookiesFromBrowser = ""
    var ytDlpPath = ""
    var ffmpegPath = ""
    var maxConcurrentJobs = 2
    var itunesCountry = "CA"

    static let defaultMusicFolder =
        FileManager.default.urls(for: .musicDirectory, in: .userDomainMask).first?.path
        ?? NSHomeDirectory() + "/Music"

    static let configDirectory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory() + "/Library/Application Support")
        return base.appendingPathComponent("TrackForge", isDirectory: true)
    }()

    static let configPath = configDirectory.appendingPathComponent("config.json")

    static func load() -> AppConfig {
        guard let data = try? Data(contentsOf: configPath),
              let cfg = try? JSONDecoder().decode(AppConfig.self, from: data)
        else { return AppConfig() }
        return cfg
    }

    func save() {
        // A settings write failure should never kill the app.
        do {
            try FileManager.default.createDirectory(
                at: Self.configDirectory, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(self).write(to: Self.configPath, options: .atomic)
        } catch {
            NSLog("TrackForge: could not save config — \(error.localizedDescription)")
        }
    }
}
