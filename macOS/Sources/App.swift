import SwiftUI

@main
struct TrackForgeApp: App {
    @StateObject private var forge = ForgeService()

    var body: some Scene {
        WindowGroup("TrackForge") {
            RootView()
                .environmentObject(forge)
                .frame(minWidth: 940, minHeight: 580)
                .preferredColorScheme(.dark)
        }
        .defaultSize(width: 1180, height: 760)
        .windowToolbarStyle(.unifiedCompact)
        .commands {
            CommandGroup(replacing: .newItem) { }
            TrackForgeCommands()
        }
    }
}

/// The page switcher and the two things worth a shortcut, published through the
/// menu bar so they show up where a Mac user looks for them.
struct TrackForgeCommands: Commands {
    var body: some Commands {
        CommandMenu("Go") {
            ForEach(Array(Page.allCases.enumerated()), id: \.element) { index, page in
                Button(page.title) { NotificationCenter.default.post(name: .showPage, object: index) }
                    .keyboardShortcut(KeyEquivalent(Character("\(index + 1)")), modifiers: .command)
            }
            Divider()
            Button("Rescan Library") { NotificationCenter.default.post(name: .rescanLibrary, object: nil) }
                .keyboardShortcut("r", modifiers: .command)
            Button("Jobs") { NotificationCenter.default.post(name: .toggleJobs, object: nil) }
                .keyboardShortcut("j", modifiers: .command)
        }
    }
}

extension Notification.Name {
    static let showPage = Notification.Name("TrackForge.showPage")
    static let rescanLibrary = Notification.Name("TrackForge.rescanLibrary")
    static let toggleJobs = Notification.Name("TrackForge.toggleJobs")
    static let sendToFind = Notification.Name("TrackForge.sendToFind")
    static let sendToGrab = Notification.Name("TrackForge.sendToGrab")
}

enum Page: Int, CaseIterable, Hashable {
    case grab, library, find, settings

    var title: String {
        switch self {
        case .grab: return "Grab"
        case .library: return "Library"
        case .find: return "Find"
        case .settings: return "Settings"
        }
    }
}

struct RootView: View {
    @EnvironmentObject private var forge: ForgeService

    @State private var page: Page = .grab
    @State private var showJobs = false
    @State private var toolStatus: ToolStatus = .checking
    @State private var showToolSetup = false
    @State private var missingTools: [String] = []
    @State private var didStart = false

    enum ToolStatus: Equatable {
        case checking
        case ready(ytDlp: String, ffmpeg: String)
        case missing([String])

        var text: String {
            switch self {
            case .checking: return "checking"
            case .ready: return "tools ready"
            case .missing(let names): return "missing " + names.joined(separator: " + ")
            }
        }

        var color: Color {
            switch self {
            case .checking: return Theme.textFaint
            case .ready: return Theme.good
            case .missing: return Theme.bad
            }
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            Divider().overlay(Theme.chromeBorder)

            HStack(spacing: 0) {
                pageContent
                    .frame(maxWidth: .infinity, maxHeight: .infinity)

                if showJobs {
                    Divider().overlay(Theme.chromeBorder)
                    JobsView(isVisible: $showJobs)
                        .frame(width: Theme.jobsDockWidth)
                        .transition(.move(edge: .trailing))
                }
            }
        }
        .background(Theme.background)
        .sheet(isPresented: $showToolSetup) {
            ToolSetupView(missing: missingTools) { installedSomething in
                showToolSetup = false
                if installedSomething { Task { await refreshToolStatus() } }
            }
        }
        .task {
            guard !didStart else { return }
            didStart = true
            await startup()
        }
        .onReceive(NotificationCenter.default.publisher(for: .showPage)) { note in
            if let index = note.object as? Int, let target = Page(rawValue: index) {
                page = target
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: .toggleJobs)) { _ in
            withAnimation(.easeOut(duration: 0.12)) { showJobs.toggle() }
        }
        .onReceive(NotificationCenter.default.publisher(for: .sendToFind)) { _ in
            page = .find
        }
        .onReceive(NotificationCenter.default.publisher(for: .sendToGrab)) { _ in
            page = .grab
        }
    }

    // MARK: - Chrome

    private var topBar: some View {
        HStack(spacing: 0) {
            ForEach(Page.allCases, id: \.self) { candidate in
                NavButton(title: candidate.title, active: page == candidate) {
                    page = candidate
                }
            }

            Spacer()

            StatusDot(color: toolStatus.color)
            Text(toolStatus.text)
                .font(Theme.secondary)
                .foregroundColor(toolStatus == .checking ? Theme.textDim : toolStatus.color)
                .padding(.leading, 5)
                .padding(.trailing, 12)
                .help(toolStatusHelp)

            Button(forge.jobs.activeCount > 0 ? "Jobs \(forge.jobs.activeCount)" : "Jobs") {
                withAnimation(.easeOut(duration: 0.12)) { showJobs.toggle() }
            }
            .flatButton(primary: forge.jobs.activeCount > 0)
            .padding(.trailing, Theme.pad)
        }
        .padding(.leading, Theme.pad)
        .frame(height: Theme.topBarHeight)
        .background(Theme.chromePanel)
    }

    private var toolStatusHelp: String {
        switch toolStatus {
        case .checking: return "Looking for yt-dlp and ffmpeg."
        case .ready(let ytDlp, let ffmpeg): return "\(ytDlp)\n\(ffmpeg)"
        case .missing: return "Open Settings to install the missing tools."
        }
    }

    @ViewBuilder
    private var pageContent: some View {
        switch page {
        case .grab: GrabView(toolsReady: isReady)
        case .library: LibraryView()
        case .find: FindView()
        case .settings: SettingsView(toolStatus: $toolStatus, onInstall: { presentToolSetup(force: true) })
        }
    }

    private var isReady: Bool {
        if case .ready = toolStatus { return true }
        return false
    }

    // MARK: - Startup

    private func startup() async {
        await refreshToolStatus(offerInstall: true)
        NotificationCenter.default.post(name: .rescanLibrary, object: nil)
    }

    private func refreshToolStatus(offerInstall: Bool = false) async {
        let (ytDlp, ffmpeg) = await forge.downloader.checkTools()

        if let ytDlp, let ffmpeg {
            toolStatus = .ready(ytDlp: ytDlp, ffmpeg: String(ffmpeg.prefix(60)))
            return
        }

        var missing: [String] = []
        if ytDlp == nil { missing.append("yt-dlp") }
        if ffmpeg == nil { missing.append("ffmpeg") }
        toolStatus = .missing(missing)

        if offerInstall {
            missingTools = missing
            showToolSetup = true
        }
    }

    private func presentToolSetup(force: Bool) {
        missingTools = ["yt-dlp", "ffmpeg"]
        showToolSetup = true
    }
}
