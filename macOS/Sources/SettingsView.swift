import AppKit
import SwiftUI

/// One card, two columns, accent eyebrows marking each section.
struct SettingsView: View {
    @EnvironmentObject private var forge: ForgeService

    @Binding var toolStatus: RootView.ToolStatus
    let onInstall: () -> Void

    @State private var saved = ""

    var body: some View {
        ScrollView {
            CardPanel {
                HStack(alignment: .top, spacing: 40) {
                    leftColumn
                    rightColumn
                    Spacer(minLength: 0)
                }
                .padding(14)
            }
            .fixedSize(horizontal: false, vertical: true)
        }
        .padding(Theme.pad)
    }

    // MARK: - Left

    private var leftColumn: some View {
        VStack(alignment: .leading, spacing: 0) {
            Eyebrow("Paths").padding(.bottom, 8)
            pathRow("Library", $forge.config.libraryFolder)
            pathRow("Save to", $forge.config.outputFolder)

            Eyebrow("Audio").padding(.top, 16).padding(.bottom, 8)
            FlatPicker(caption: "Format", selection: $forge.config.format,
                       options: ["mp3", "flac", "opus", "m4a"])
                .padding(.bottom, 6)
            FlatPicker(caption: "Bitrate", selection: $forge.config.bitrate,
                       options: ["320", "256", "192", "128"])

            Eyebrow("Naming").padding(.top, 16).padding(.bottom, 8)
            HStack(spacing: 0) {
                Text("Pattern")
                    .font(Theme.body).foregroundColor(Theme.textDim)
                    .frame(width: 82, alignment: .leading)
                FlatTextField(placeholder: "{track} {title}",
                              text: $forge.config.filenamePattern, monospaced: true)
                    .frame(width: 240)
            }
            Text("{track} {tracknum} {title} {artist} {albumartist} {album} {year}")
                .font(Theme.numericSmall)
                .foregroundColor(Theme.textFaint)
                .padding(.leading, 82)
                .padding(.top, 6)
            Text(preview)
                .font(Theme.numericSmall)
                .foregroundColor(Theme.textDim)
                .padding(.leading, 82)
                .padding(.top, 3)

            HStack(spacing: 10) {
                Button("Save settings") {
                    forge.saveConfig()
                    saved = "Saved. Rescan the library if you changed the folder."
                }
                .flatButton(primary: true)

                Text(saved).font(Theme.secondary).foregroundColor(Theme.good)
            }
            .padding(.top, 18)
        }
        .frame(width: 400, alignment: .leading)
    }

    /// Live preview computed through the real NameFormatter token rules, so a
    /// pattern that will not work is obvious before anything is downloaded.
    private var preview: String {
        let sample = Track()
        sample.title = "vicinity of obscenity"
        sample.artist = "system of a down"
        sample.albumArtist = "system of a down"
        sample.album = "steal this album!"
        sample.year = "2002"
        sample.trackNumber = "9"
        return NameFormatter.buildFileName(
            sample, pattern: forge.config.filenamePattern, extension: ".mp3")
    }

    private func pathRow(_ caption: String, _ binding: Binding<String>) -> some View {
        HStack(spacing: 0) {
            Text(caption)
                .font(Theme.body).foregroundColor(Theme.textDim)
                .frame(width: 82, alignment: .leading)
            FlatTextField(placeholder: "", text: binding, monospaced: true)
                .frame(width: 210)
            Button("…") { chooseFolder(into: binding) }
                .flatButton()
                .padding(.leading, 6)
        }
        .padding(.bottom, 6)
    }

    private func chooseFolder(into binding: Binding<String>) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.directoryURL = URL(fileURLWithPath: binding.wrappedValue)
        if panel.runModal() == .OK, let url = panel.url {
            binding.wrappedValue = url.path
        }
    }

    // MARK: - Right

    private var rightColumn: some View {
        VStack(alignment: .leading, spacing: 0) {
            Eyebrow("Behaviour").padding(.bottom, 8)
            FlatCheckbox(title: "Detect BPM and key on download",
                         isOn: $forge.config.analyzeBpmAndKey)
            FlatCheckbox(title: "Pick cover art automatically", isOn: $forge.config.autoArt)
            FlatCheckbox(title: "Force Title Case", isOn: $forge.config.forceTitleCase)
            FlatCheckbox(title: "Store the source URL in the file",
                         isOn: $forge.config.writeSourceURL)
            FlatCheckbox(title: "Read BPM from djay's library",
                         isOn: $forge.config.importDjayData)

            Eyebrow("Lookup").padding(.top, 16).padding(.bottom, 8)
            FlatPicker(caption: "iTunes store", selection: $forge.config.itunesCountry,
                       options: ["CA", "US", "GB", "AU", "DE", "FR", "JP"], width: 72)
                .padding(.bottom, 6)
            FlatPicker(caption: "Cookies", selection: cookieBinding,
                       options: ["none", "safari", "chrome", "edge", "firefox", "brave", "vivaldi"])

            Eyebrow("Tools").padding(.top, 16).padding(.bottom, 8)
            toolBlock

            Button("Install / update tools", action: onInstall)
                .flatButton(primary: !isReady)
                .padding(.top, 10)
        }
        .frame(width: 340, alignment: .leading)
    }

    /// The config stores "" for off; the picker wants a value it can show.
    private var cookieBinding: Binding<String> {
        Binding(
            get: { forge.config.cookiesFromBrowser.isEmpty ? "none" : forge.config.cookiesFromBrowser },
            set: { forge.config.cookiesFromBrowser = $0 == "none" ? "" : $0 })
    }

    private var isReady: Bool {
        if case .ready = toolStatus { return true }
        return false
    }

    private var toolBlock: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(ytDlpLine)
            Text(ffmpegLine)
            Text("folder  \(ToolInstaller.toolsDirectory.path)")
                .foregroundColor(Theme.textFaint)
        }
        .font(Theme.numericSmall)
        .foregroundColor(isReady ? Theme.textDim : Theme.bad)
        .lineLimit(1)
        .truncationMode(.middle)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(8)
        .background(Theme.chromePanel)
        .overlay(Rectangle().strokeBorder(Theme.chromeBorder, lineWidth: 1))
    }

    private var ytDlpLine: String {
        if case .ready(let ytDlp, _) = toolStatus { return "yt-dlp  \(ytDlp)" }
        return "yt-dlp  not found"
    }

    private var ffmpegLine: String {
        if case .ready = toolStatus { return "ffmpeg  ok" }
        return "ffmpeg  not found"
    }
}
