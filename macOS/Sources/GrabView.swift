import AppKit
import SwiftUI

/// Paste links, review what came back, grab it.
struct GrabView: View {
    @EnvironmentObject private var forge: ForgeService
    @StateObject private var model = GrabPageModel()

    let toolsReady: Bool

    var body: some View {
        VStack(spacing: 8) {
            intake
            cards
        }
        .padding(Theme.pad)
        .onAppear { model.attach(forge) }
        .onReceive(NotificationCenter.default.publisher(for: .sendToGrab)) { note in
            if let urls = note.object as? [String] { model.addURLs(urls) }
        }
    }

    // MARK: - Intake

    private var intake: some View {
        CardPanel {
            VStack(alignment: .leading, spacing: Theme.gap) {
                FlatTextEditor(
                    placeholder: "Paste YouTube links, one per line. Playlists expand.",
                    text: $model.urlText)
                    .frame(height: 52)

                HStack(spacing: Theme.gap) {
                    Button(model.busy == .download ? "…" : "Download") {
                        Task { await model.fetch(autoGrab: true, toolsReady: toolsReady) }
                    }
                    .flatButton(primary: true)
                    .disabled(model.busy != nil)

                    Button(model.busy == .review ? "…" : "Review first") {
                        Task { await model.fetch(autoGrab: false, toolsReady: toolsReady) }
                    }
                    .flatButton()
                    .disabled(model.busy != nil)

                    Button(model.busy == .lookup ? "…" : "Look up all") {
                        Task { await model.lookupAll() }
                    }
                    .flatButton()
                    .disabled(model.cards.isEmpty || model.busy != nil)

                    Button("Grab all") { model.grabAll() }
                        .flatButton()
                        .disabled(model.cards.isEmpty)

                    Button("Clear") { model.cards.removeAll() }
                        .flatButton()
                        .disabled(model.cards.isEmpty)

                    Text(model.note)
                        .font(Theme.secondary)
                        .foregroundColor(model.noteColor)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .padding(.leading, 4)

                    Spacer(minLength: 0)
                }
            }
            .padding(Theme.pad)
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    // MARK: - Cards

    @ViewBuilder
    private var cards: some View {
        if model.cards.isEmpty {
            EmptyHint(lines: [
                "Paste a YouTube link above and hit Download.",
                "It finds the tags, grabs the audio and files it for you.",
                "Use Review first if you want to check the tags before it downloads.",
            ])
        } else {
            ScrollView {
                LazyVStack(spacing: Theme.gap) {
                    ForEach(model.cards) { card in
                        GrabCardView(card: card) { model.remove(card) }
                    }
                }
                .padding(.bottom, Theme.pad)
            }
        }
    }
}

// MARK: - Page model

@MainActor
final class GrabPageModel: ObservableObject {
    @Published var urlText = ""
    @Published var cards: [GrabCardModel] = []
    @Published var note = ""
    @Published var noteColor = Theme.textFaint
    @Published var busy: Busy?

    enum Busy { case download, review, lookup }

    private var forge: ForgeService!

    func attach(_ forge: ForgeService) {
        guard self.forge == nil else { return }
        self.forge = forge
    }

    func addURLs(_ urls: [String]) {
        var existing = Set(urlText.split(whereSeparator: \.isNewline).map { $0.trimmed })
        let fresh = urls.map { $0.trimmed }.filter { !$0.isEmpty && existing.insert($0).inserted }
        guard !fresh.isEmpty else { return }

        let head = urlText.trimmed
        urlText = (head.isEmpty ? "" : head + "\n") + fresh.joined(separator: "\n")
    }

    func remove(_ card: GrabCardModel) {
        cards.removeAll { $0 === card }
    }

    func fetch(autoGrab: Bool, toolsReady: Bool) async {
        guard toolsReady else {
            note = "yt-dlp and ffmpeg are both needed — open Settings and use Install tools."
            noteColor = Theme.bad
            return
        }

        // Two links that normalise to the same video are one link.
        var seen = Set<String>()
        let urls = urlText
            .split(whereSeparator: \.isNewline)
            .map { $0.trimmed }
            .filter { !$0.isEmpty }
            .filter { seen.insert(YtDlp.normalizeForProbe($0).url).inserted }

        guard !urls.isEmpty else {
            note = "Paste at least one link."
            noteColor = Theme.warn
            return
        }

        busy = autoGrab ? .download : .review
        noteColor = Theme.textMuted
        defer { busy = nil }

        var fresh: [GrabCardModel] = []
        var failed = 0

        for url in urls {
            note = "Reading " + url
            do {
                let (entries, playlist) = try await forge.downloader.probe(url)
                for entry in entries {
                    let card = GrabCardModel(forge: forge, entry: entry)
                    cards.append(card)
                    fresh.append(card)
                }
                if let playlist { note = "Playlist: \(playlist) (\(entries.count))" }
            } catch {
                failed += 1
                note = error.localizedDescription
                noteColor = Theme.bad
            }
        }

        if failed == 0 { urlText = "" }

        if autoGrab, !fresh.isEmpty {
            for (i, card) in fresh.enumerated() {
                note = "Tagging \(i + 1) of \(fresh.count)…"
                noteColor = Theme.textMuted
                await card.lookup()
                card.grab()
            }
            note = "\(fresh.count) downloading. Watch the Jobs panel or the bars below."
            noteColor = Theme.textFaint
        } else if failed == 0 {
            note = "\(fresh.count) looked up. Check the tags, then grab."
            noteColor = Theme.textFaint
        }
    }

    func lookupAll() async {
        busy = .lookup
        defer { busy = nil }
        for card in cards { await card.lookup() }
    }

    func grabAll() {
        for card in cards where !card.isGrabbed { card.grab() }
    }
}

// MARK: - Card model

@MainActor
final class GrabCardModel: ObservableObject, Identifiable {
    let id = UUID()
    let entry: VideoEntry
    let meta = Track()

    @Published var title = ""
    @Published var artist = ""
    @Published var album = ""
    @Published var albumArtist = ""
    @Published var genre = ""
    @Published var year = ""
    @Published var trackNumber = ""
    @Published var discNumber = ""
    @Published var bpm = ""
    @Published var musicalKey = ""

    @Published var matchLabels: [String] = ["No lookup yet"]
    @Published var selectedMatch = 0
    @Published var status = ""
    @Published var statusColor = Theme.textMuted
    @Published var artImage: NSImage?
    @Published var jobID: Int?
    @Published var lookingUp = false
    @Published var pickingArt = false
    @Published var artOptions: [MetadataClient.ArtOption] = []
    @Published var showArtPicker = false

    private(set) var candidates: [MatchCandidate] = []
    private var merged: MatchCandidate?
    private var artBytes: Data?
    private var artURL: String?
    private var artIsPlaceholder = true

    private let forge: ForgeService

    init(forge: ForgeService, entry: VideoEntry) {
        self.forge = forge
        self.entry = entry

        let (guessedArtist, guessedTitle) = entry.guess()
        title = guessedArtist.isEmpty ? entry.rawTitle : guessedTitle
        artist = guessedArtist
        album = entry.ytAlbum
        year = entry.ytYear
        meta.durationSeconds = Double(entry.durationSeconds)

        Task { await loadThumbnail() }
    }

    var sourceLine: String {
        [entry.rawTitle, entry.uploader, entry.durationText]
            .filter { !$0.isBlank }
            .joined(separator: "  ·  ")
    }

    var isGrabbed: Bool {
        guard let jobID, let job = forge.jobs.job(jobID) else { return false }
        return job.state == .done
    }

    // MARK: Field plumbing

    /// The Windows build had to guard against a text-changed storm wiping the
    /// values a lookup had just written. SwiftUI bindings make that impossible —
    /// the fields are the source of truth and only get read on the way out.
    private func pullFields() {
        meta.title = title.trimmed
        meta.artist = artist.trimmed
        meta.album = album.trimmed
        meta.albumArtist = albumArtist.trimmed
        meta.genre = genre.trimmed
        meta.year = year.trimmed
        meta.trackNumber = trackNumber.trimmed
        meta.discNumber = discNumber.trimmed
        meta.bpm = bpm.trimmed
        meta.musicalKey = musicalKey.trimmed
    }

    private func pushFields() {
        title = meta.title
        artist = meta.artist
        album = meta.album
        albumArtist = meta.albumArtist
        genre = meta.genre
        year = meta.year
        trackNumber = meta.trackNumber
        discNumber = meta.discNumber
        bpm = meta.bpm
        musicalKey = meta.musicalKey
    }

    // MARK: Lookup

    private func loadThumbnail() async {
        guard !entry.thumbnailURL.isBlank else { return }
        guard let bytes = await forge.metadata.downloadArt(entry.thumbnailURL) else { return }
        // A real cover, once found, outranks the video thumbnail.
        guard artIsPlaceholder else { return }
        artImage = TagService.image(from: bytes)
        artBytes = bytes
    }

    func lookup() async {
        lookingUp = true
        defer { lookingUp = false }

        setStatus("Looking up…", Theme.textMuted)
        pullFields()

        candidates = await forge.metadata.lookup(
            artist: meta.artist, title: meta.title,
            durationSeconds: Double(entry.durationSeconds), deep: true)

        guard !candidates.isEmpty else {
            matchLabels = ["No matches found"]
            selectedMatch = 0
            merged = nil
            setStatus("Nothing found online. Type the tags in by hand.", Theme.warn)
            return
        }

        merged = MetadataClient.merge(candidates)
        matchLabels = [merged.map { "Best of all sources (\($0.sourceLabel))" } ?? "Best match"]
            + candidates.map(\.display)
        selectedMatch = 0
        await applySelectedMatch()
    }

    func applySelectedMatch() async {
        let chosen: MatchCandidate?
        if selectedMatch == 0 {
            chosen = merged
        } else {
            let index = selectedMatch - 1
            chosen = index < candidates.count ? candidates[index] : nil
        }
        guard let chosen else { return }

        pullFields()
        chosen.apply(to: meta, overwrite: true, titleCase: forge.config.forceTitleCase)
        // The merge only ever fills gaps, so applying it after a specific pick
        // tops up the fields that pick had nothing for.
        if let merged, merged !== chosen {
            merged.apply(to: meta, overwrite: false, titleCase: forge.config.forceTitleCase)
        }
        pushFields()

        let filled = [meta.title, meta.artist, meta.album, meta.albumArtist,
                      meta.year, meta.genre, meta.trackNumber, meta.discNumber]
            .filter { !$0.isBlank }.count
        let duplicate = forge.alreadyHave(artist: meta.artist, title: meta.title)

        setStatus(
            String(format: "%@ (%.0f) — %d/8 fields%@",
                   chosen.sourceLabel, chosen.score, filled,
                   duplicate ? " — already in your library" : " filled"),
            duplicate ? Theme.warn : Theme.good)

        let url = chosen.artURL.isEmpty ? (merged?.artURL ?? "") : chosen.artURL
        if forge.config.autoArt, !url.isEmpty,
           let bytes = await forge.metadata.downloadArt(url) {
            setArt(bytes, url: url)
        }
    }

    func findArt() async {
        pullFields()
        pickingArt = true
        defer { pickingArt = false }

        setStatus("Searching for cover art…", Theme.textMuted)
        let artistTerm = meta.albumArtist.isEmpty ? meta.artist : meta.albumArtist
        let albumTerm = meta.album.isEmpty ? meta.title : meta.album
        artOptions = await forge.metadata.findArt(artist: artistTerm, album: albumTerm)

        guard !artOptions.isEmpty else {
            setStatus("No cover art found for that album.", Theme.warn)
            return
        }
        showArtPicker = true
    }

    func setArt(_ bytes: Data, url: String?) {
        artBytes = bytes
        artURL = url
        artIsPlaceholder = false
        artImage = TagService.image(from: bytes)
    }

    func setStatus(_ text: String, _ color: Color) {
        status = text
        statusColor = color
    }

    // MARK: Grab

    func grab() {
        pullFields()
        guard !meta.title.isBlank else {
            setStatus("Give it a title first.", Theme.bad)
            return
        }

        jobID = forge.enqueueGrab(ForgeService.GrabRequest(
            url: entry.url,
            meta: meta.clone(),
            artURL: artURL,
            artBytes: artIsPlaceholder ? nil : artBytes,
            outputFolder: nil))
    }
}

// MARK: - Card view

struct GrabCardView: View {
    @EnvironmentObject private var forge: ForgeService
    @ObservedObject var card: GrabCardModel
    let onRemove: () -> Void

    private var job: Job? { card.jobID.flatMap { forge.jobs.job($0) } }

    var body: some View {
        CardPanel(borderColor: borderColor) {
            VStack(spacing: 0) {
                HStack(alignment: .top, spacing: 8) {
                    artColumn
                    fieldColumn
                    actionColumn
                }
                .padding(Theme.pad)

                FlatProgress(value: job?.progress ?? 0, barColor: progressColor)
                    .opacity(job == nil ? 0 : 1)
            }
        }
        .sheet(isPresented: $card.showArtPicker) {
            ArtPickerView(options: card.artOptions) { bytes, url in
                card.showArtPicker = false
                if let bytes {
                    card.setArt(bytes, url: url)
                    card.setStatus("Cover art set.", Theme.good)
                }
            }
        }
    }

    private var borderColor: Color {
        switch job?.state {
        case .done: return Theme.doneBorder
        case .failed, .cancelled: return Theme.bad
        default: return Theme.border
        }
    }

    private var progressColor: Color {
        switch job?.state {
        case .done: return Theme.good
        case .failed, .cancelled: return Theme.bad
        default: return Theme.accent
        }
    }

    // MARK: Columns

    private var artColumn: some View {
        VStack(spacing: 4) {
            WaveMark(image: card.artImage, size: Theme.grabArtSize)
                .onTapGesture { Task { await card.findArt() } }

            Button(card.pickingArt ? "…" : "Change art") {
                Task { await card.findArt() }
            }
            .flatButton(compact: true)
            .frame(width: Theme.grabArtSize)
            .disabled(card.pickingArt)
        }
    }

    private var fieldColumn: some View {
        VStack(alignment: .leading, spacing: Theme.gap) {
            Text(card.sourceLine)
                .font(Theme.secondary)
                .foregroundColor(Theme.textFainter)
                .lineLimit(1)
                .truncationMode(.tail)

            // Row 1 — the fields that decide the filename.
            HStack(spacing: Theme.gap) {
                FlatTextField(placeholder: "Title", text: $card.title)
                    .frame(maxWidth: .infinity).layoutPriority(2)
                FlatTextField(placeholder: "Artist", text: $card.artist)
                    .frame(maxWidth: .infinity).layoutPriority(1.35)
                FlatTextField(placeholder: "Album", text: $card.album)
                    .frame(maxWidth: .infinity).layoutPriority(1.35)
            }

            // Row 2 — everything else, numerics on fixed widths so they line up.
            HStack(spacing: Theme.gap) {
                FlatTextField(placeholder: "Album artist", text: $card.albumArtist)
                    .frame(maxWidth: .infinity).layoutPriority(1.5)
                FlatTextField(placeholder: "Genre", text: $card.genre)
                    .frame(maxWidth: .infinity).layoutPriority(1.1)
                FlatTextField(placeholder: "Year", text: $card.year, monospaced: true)
                    .frame(width: 52)
                FlatTextField(placeholder: "#", text: $card.trackNumber, monospaced: true)
                    .frame(width: 40)
                FlatTextField(placeholder: "Disc", text: $card.discNumber, monospaced: true)
                    .frame(width: 44)
                FlatTextField(placeholder: "BPM", text: $card.bpm, monospaced: true)
                    .frame(width: 48)
                FlatTextField(placeholder: "Key", text: $card.musicalKey, monospaced: true)
                    .frame(width: 54)
            }

            // Row 3 — which match is applied, and how that went.
            HStack(spacing: Theme.gap) {
                Picker("", selection: $card.selectedMatch) {
                    ForEach(Array(card.matchLabels.enumerated()), id: \.offset) { index, label in
                        Text(label).font(Theme.body).tag(index)
                    }
                }
                .labelsHidden()
                .frame(maxWidth: 320)
                .disabled(card.candidates.isEmpty)
                .onChange(of: card.selectedMatch) { _ in
                    Task { await card.applySelectedMatch() }
                }

                Button(card.lookingUp ? "…" : "Look up") {
                    Task { await card.lookup() }
                }
                .flatButton(compact: true)
                .disabled(card.lookingUp)

                Text(card.status)
                    .font(Theme.secondary)
                    .foregroundColor(card.statusColor)
                    .lineLimit(1)
                    .truncationMode(.tail)

                Spacer(minLength: 0)
            }
        }
    }

    private var actionColumn: some View {
        VStack(spacing: 4) {
            Button(grabTitle) { card.grab() }
                .flatButton(primary: true)
                .frame(width: 76)
                .disabled(job != nil && job?.state != .failed && job?.state != .cancelled)

            Button("Remove", action: onRemove)
                .flatButton(compact: true)
                .frame(width: 76)
        }
    }

    private var grabTitle: String {
        switch job?.state {
        case .done: return "Done"
        case .failed, .cancelled: return "Retry"
        case .some: return "…"
        case nil: return "Grab"
        }
    }
}
