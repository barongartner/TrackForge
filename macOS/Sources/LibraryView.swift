import SwiftUI

/// Everything already on disk: what's tagged, what isn't, and fixing it.
struct LibraryView: View {
    @EnvironmentObject private var forge: ForgeService

    @State private var search = ""
    @State private var filter: LibraryFilter = .all
    @State private var selection = Set<Track.ID>()
    @State private var sortOrder = [KeyPathComparator(\Track.sortTitle)]
    @State private var status = ""
    @State private var statusColor = Theme.textMuted
    @State private var scanning = false

    @State private var editing: Track?
    @State private var enrichTargets: [Track] = []
    @State private var showEnrichOptions = false
    @State private var confirmRepair = false

    private var shown: [Track] {
        var result = forge.tracks.filter(filter.matches)

        let query = search.trimmed.lowercased()
        if !query.isEmpty {
            let terms = query.split(separator: " ").map(String.init)
            result = result.filter { track in
                let blob = track.searchBlob
                return terms.allSatisfy(blob.contains)
            }
        }
        return result.sorted(using: sortOrder)
    }

    private var selectedTracks: [Track] {
        shown.filter { selection.contains($0.id) }
    }

    var body: some View {
        VStack(spacing: 8) {
            toolbar
            table
        }
        .padding(Theme.pad)
        .onReceive(NotificationCenter.default.publisher(for: .rescanLibrary)) { _ in
            Task { await rescan() }
        }
        .sheet(item: $editing) { track in
            TagEditorView(track: track)
        }
        .sheet(isPresented: $showEnrichOptions) {
            EnrichOptionsView(trackCount: enrichTargets.count) { options in
                showEnrichOptions = false
                guard let options else { return }
                forge.enqueueEnrich(enrichTargets, options: options)
                setStatus("Filling tags on \(enrichTargets.count) — see Jobs", Theme.textMuted)
            }
        }
        .alert("Repair tags", isPresented: $confirmRepair) {
            Button("Cancel", role: .cancel) { }
            Button("Repair") {
                let targets = repairTargets
                forge.enqueueRetag(targets)
                setStatus("Repairing \(targets.count) — see Jobs", Theme.textMuted)
            }
        } message: {
            Text("""
                Rewrite tags on \(repairTargets.count) file(s) as ID3v2.3?

                This fixes genres showing as numbers and cover art that DJ software \
                refuses to read. Nothing is downloaded and no values change — only the \
                tag format is rewritten.
                """)
        }
    }

    // MARK: - Toolbar

    private var toolbar: some View {
        CardPanel {
            VStack(spacing: Theme.gap) {
                HStack(spacing: 8) {
                    FlatTextField(placeholder: "Search", text: $search)
                        .frame(width: 210)

                    ForEach(LibraryFilter.allCases, id: \.self) { candidate in
                        Button(candidate.label) { filter = candidate }
                            .flatButton(primary: filter == candidate, chip: true)
                    }

                    Spacer(minLength: 8)

                    Text(countLine)
                        .font(Theme.numericSmall)
                        .foregroundColor(Theme.textCount)
                        .lineLimit(1)

                    Button(scanning ? "…" : "Rescan") { Task { await rescan() } }
                        .flatButton()
                        .disabled(scanning)
                }

                HStack(spacing: Theme.gap) {
                    Text(status)
                        .font(Theme.secondary)
                        .foregroundColor(statusColor)
                        .lineLimit(1)
                        .truncationMode(.middle)

                    Spacer(minLength: 8)

                    Button(fillAllTitle) { enrich(shown) }
                        .flatButton(primary: true)
                        .disabled(shown.isEmpty)

                    Button(selection.isEmpty ? "Fill selected" : "Fill \(selection.count) selected") {
                        enrich(selectedTracks)
                    }
                    .flatButton(compact: true)
                    .disabled(selection.isEmpty)

                    Button(selection.isEmpty ? "Repair tags" : "Repair \(selection.count)") {
                        confirmRepair = true
                    }
                    .flatButton(compact: true)
                    .disabled(repairTargets.isEmpty)

                    Button("BPM + key") {
                        forge.enqueueAnalyze(selectedTracks)
                        setStatus("Analysing \(selection.count) — see Jobs", Theme.textMuted)
                    }
                    .flatButton(compact: true)
                    .disabled(selection.isEmpty)

                    Button("Find on YouTube") {
                        NotificationCenter.default.post(
                            name: .sendToFind, object: selectedTracks.map { track in
                                "\(track.artist) - \(track.title)"
                                    .trimmingCharacters(in: CharacterSet(charactersIn: " -"))
                            })
                    }
                    .flatButton(compact: true)
                    .disabled(selection.isEmpty)
                }
            }
            .padding(Theme.pad)
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    private var countLine: String {
        let incomplete = forge.tracks.filter { !$0.isComplete }.count
        return "\(shown.count) shown / \(forge.tracks.count) total / \(incomplete) need work"
    }

    private var fillAllTitle: String {
        shown.count == forge.tracks.count ? "Fill every track" : "Fill all \(shown.count) shown"
    }

    private var repairTargets: [Track] {
        selection.isEmpty ? shown : selectedTracks
    }

    // MARK: - Table

    private var table: some View {
        Table(shown, selection: $selection, sortOrder: $sortOrder) {
            TableColumn("Title", value: \.sortTitle) { track in
                Text(track.title.isBlank
                     ? (track.fileName as NSString).deletingPathExtension
                     : track.title)
                    .foregroundColor(Theme.text)
            }
            .width(min: 140, ideal: 230)

            TableColumn("Artist", value: \.artist) { track in
                Text(dash(track.artist)).foregroundColor(Theme.textStrong)
            }
            .width(min: 90, ideal: 155)

            TableColumn("Album", value: \.album) { track in
                Text(dash(track.album))
                    .foregroundColor(track.album.isBlank ? Theme.textFainter : Theme.text)
            }
            .width(min: 90, ideal: 175)

            TableColumn("Year", value: \.yearValue) { track in
                Text(dash(track.year)).font(Theme.numeric).foregroundColor(Theme.textDim)
            }
            .width(52)

            TableColumn("Genre", value: \.genre) { track in
                Text(dash(track.genre)).foregroundColor(Theme.text)
            }
            .width(min: 70, ideal: 110)

            TableColumn("#", value: \.trackNumberValue) { track in
                Text(paddedTrack(track.trackNumber)).font(Theme.numeric)
                    .foregroundColor(Theme.text)
            }
            .width(38)

            TableColumn("BPM", value: \.bpmValue) { track in
                // A BPM that came from djay rather than this file's own tags is
                // dimmed, so it is obvious nothing has been written yet.
                Text(dash(track.displayBpm))
                    .font(Theme.numeric)
                    .foregroundColor(track.bpm.isBlank && track.djayBpm != nil
                                     ? Theme.textFaint : Theme.text)
            }
            .width(52)

            TableColumn("Key", value: \.camelot) { track in
                Text(track.camelot.isEmpty
                     ? dash(track.musicalKey)
                     : "\(track.musicalKey) \(track.camelot)")
                    .font(Theme.numeric)
                    .foregroundColor(Theme.text)
            }
            .width(68)

            TableColumn("Len", value: \.durationSeconds) { track in
                Text(dash(track.durationText)).font(Theme.numeric)
                    .foregroundColor(Theme.textDim)
            }
            .width(52)

            TableColumn("Missing", value: \.missingCount) { track in
                Text(track.isComplete ? "complete" : track.missingText)
                    .foregroundColor(track.isComplete ? Theme.good : Theme.warn)
            }
            .width(min: 100, ideal: 160)
        }
        .font(Theme.body)
        .tableStyle(.inset(alternatesRowBackgrounds: true))
        .contextMenu(forSelectionType: Track.ID.self) { ids in
            Button("Edit tags") { editing = track(with: ids.first) }
                .disabled(ids.count != 1)
            Button("Show in Finder") { revealInFinder(ids) }
            Divider()
            Button("Fill tags…") { enrich(tracks(with: ids)) }
            Button("Analyse BPM + key") { forge.enqueueAnalyze(tracks(with: ids)) }
        } primaryAction: { ids in
            // Double-click on exactly one row opens the editor.
            if ids.count == 1 { editing = track(with: ids.first) }
        }
        .overlay {
            if forge.tracks.isEmpty && !scanning {
                EmptyHint(lines: [
                    "No tracks scanned yet.",
                    "Point Settings › Library at your music folder, then hit Rescan.",
                ])
            }
        }
    }

    private func dash(_ s: String) -> String { s.isBlank ? "—" : s }

    /// Zero-padded so the column reads as a column, not ragged text.
    private func paddedTrack(_ raw: String) -> String {
        let first = (raw.split(separator: "/").first.map(String.init) ?? "").trimmed
        guard let n = Int(first), n > 0 else { return "—" }
        return String(format: "%02d", n)
    }

    private func track(with id: Track.ID?) -> Track? {
        guard let id else { return nil }
        return forge.tracks.first { $0.id == id }
    }

    private func tracks(with ids: Set<Track.ID>) -> [Track] {
        forge.tracks.filter { ids.contains($0.id) }
    }

    private func revealInFinder(_ ids: Set<Track.ID>) {
        let urls = tracks(with: ids).map { URL(fileURLWithPath: $0.path) }
        guard !urls.isEmpty else { return }
        NSWorkspace.shared.activateFileViewerSelecting(urls)
    }

    // MARK: - Actions

    private func enrich(_ tracks: [Track]) {
        guard !tracks.isEmpty else {
            setStatus("Nothing shown to fill.", Theme.warn)
            return
        }
        enrichTargets = tracks
        showEnrichOptions = true
    }

    private func rescan() async {
        scanning = true
        setStatus("Scanning " + forge.config.libraryFolder, Theme.textMuted)
        defer { scanning = false }

        do {
            try await forge.rescanLibrary { message in
                status = message
            }
            if forge.tracks.isEmpty {
                setStatus(
                    FileManager.default.fileExists(atPath: forge.config.libraryFolder)
                        ? "No audio files there. Check the path in Settings."
                        : "Folder not found: " + forge.config.libraryFolder,
                    Theme.warn)
            } else {
                setStatus("Scanned " + forge.config.libraryFolder, Theme.textMuted)
            }
        } catch {
            setStatus("Scan failed: " + error.localizedDescription, Theme.bad)
        }
    }

    private func setStatus(_ text: String, _ color: Color) {
        status = text
        statusColor = color
    }
}

// MARK: - Filters

enum LibraryFilter: CaseIterable, Hashable {
    case all, noArt, noYear, noGenre, noAlbum, noBpm, incomplete

    var label: String {
        switch self {
        case .all: return "All"
        case .noArt: return "No art"
        case .noYear: return "No year"
        case .noGenre: return "No genre"
        case .noAlbum: return "No album"
        case .noBpm: return "No BPM"
        case .incomplete: return "Incomplete"
        }
    }

    func matches(_ t: Track) -> Bool {
        switch self {
        case .all: return true
        case .noArt: return !t.hasArt
        case .noYear: return t.year.isBlank
        case .noGenre: return t.genre.isBlank
        case .noAlbum: return t.album.isBlank
        case .noBpm: return t.displayBpm.isEmpty
        case .incomplete: return !t.isComplete
        }
    }
}

// MARK: - Sort keys

/// Table sorting needs comparable key paths. Year, track and BPM have to sort as
/// numbers — as strings, "9" lands after "10" and the column looks broken.
extension Track {
    var sortTitle: String { title.isBlank ? fileName : title }
    var yearValue: Int { Int(year) ?? 0 }
    var trackNumberValue: Int {
        Int(trackNumber.split(separator: "/").first.map(String.init) ?? "") ?? 0
    }
    var bpmValue: Double { Double(displayBpm) ?? 0 }
    var missingCount: Int { missingFields().count }
}
