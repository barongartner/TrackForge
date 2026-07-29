import AppKit
import SwiftUI

/// Full tag editor for one library file, with online lookup and art picking.
struct TagEditorView: View {
    @EnvironmentObject private var forge: ForgeService
    @Environment(\.dismiss) private var dismiss

    let track: Track

    @State private var title = ""
    @State private var artist = ""
    @State private var album = ""
    @State private var albumArtist = ""
    @State private var composer = ""
    @State private var comment = ""
    @State private var year = ""
    @State private var genre = ""
    @State private var trackNumber = ""
    @State private var discNumber = ""
    @State private var bpm = ""
    @State private var musicalKey = ""
    @State private var isrc = ""
    @State private var publisher = ""

    @State private var artImage: NSImage?
    @State private var newArt: Data?
    @State private var candidates: [MatchCandidate] = []
    @State private var matchLabels = ["No lookup yet"]
    @State private var selectedMatch = 0
    @State private var status = ""
    @State private var statusColor = Theme.textDim
    @State private var renameToPattern = false
    @State private var busy = false
    @State private var artOptions: [MetadataClient.ArtOption] = []
    @State private var showArtPicker = false
    @State private var writeError: String?
    @State private var loaded = false

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: 24) {
                sidebar
                fields
            }
            .padding(18)

            Divider().overlay(Theme.border)
            footer
        }
        .frame(width: 880, height: 620)
        .background(Theme.background)
        .task {
            guard !loaded else { return }
            loaded = true
            pushFields()
            artImage = TagService.image(from: TagService.readArt(path: track.path))
        }
        .sheet(isPresented: $showArtPicker) {
            ArtPickerView(options: artOptions) { bytes, _ in
                showArtPicker = false
                guard let bytes else { return }
                newArt = bytes
                artImage = TagService.image(from: bytes)
                setStatus("Cover art ready. Save to write it in.", Theme.good)
            }
        }
        .alert("Could not write tags", isPresented: .constant(writeError != nil)) {
            Button("OK") { writeError = nil }
        } message: {
            Text(writeError ?? "")
        }
    }

    // MARK: - Sidebar

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 8) {
            WaveMark(image: artImage, size: 210)

            Button(busy ? "…" : "Find cover art") { Task { await findArt() } }
                .flatButton()
                .frame(width: 210)
                .disabled(busy)

            Button(busy ? "…" : "Analyse BPM + key") { Task { await analyze() } }
                .flatButton()
                .frame(width: 210)
                .disabled(busy)

            VStack(alignment: .leading, spacing: 2) {
                Text(track.durationText)
                Text("\(track.bitrate) kbps")
                Text(String(format: "%.1f MB", Double(track.sizeBytes) / 1_048_576))
                Text(track.fileName).padding(.top, 8)
            }
            .font(Theme.secondary)
            .foregroundColor(Theme.textFaint)
            .lineLimit(2)
            .truncationMode(.middle)
            .frame(width: 210, alignment: .leading)
            .padding(.top, 8)

            Spacer(minLength: 0)
        }
    }

    // MARK: - Fields

    private var fields: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 16) {
                VStack(alignment: .leading, spacing: 12) {
                    field("Title", $title, width: 290)
                    field("Artist", $artist, width: 290)
                    field("Album", $album, width: 290)
                    field("Album artist", $albumArtist, width: 290)
                    field("Composer", $composer, width: 290)
                    field("Comment", $comment, width: 290)
                }

                VStack(alignment: .leading, spacing: 12) {
                    HStack(spacing: 16) {
                        field("Year", $year, width: 78, monospaced: true)
                        field("Genre", $genre, width: 190)
                    }
                    HStack(spacing: 16) {
                        field("Track", $trackNumber, width: 78, monospaced: true)
                        field("Disc", $discNumber, width: 78, monospaced: true)
                    }
                    HStack(spacing: 16) {
                        field("BPM", $bpm, width: 78, monospaced: true)
                        field("Key", $musicalKey, width: 94, monospaced: true)
                    }
                    field("ISRC", $isrc, width: 190, monospaced: true)
                    field("Publisher", $publisher, width: 190)
                }
            }

            HStack(spacing: 10) {
                Picker("", selection: $selectedMatch) {
                    ForEach(Array(matchLabels.enumerated()), id: \.offset) { index, label in
                        Text(label).font(Theme.body).tag(index)
                    }
                }
                .labelsHidden()
                .frame(width: 380)
                .disabled(candidates.isEmpty)
                .onChange(of: selectedMatch) { _ in applySelectedMatch() }

                Button(busy ? "…" : "Look up online") { Task { await lookup() } }
                    .flatButton()
                    .disabled(busy)
            }
            .padding(.top, 6)

            Text(status)
                .font(Theme.secondary)
                .foregroundColor(statusColor)
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)

            Spacer(minLength: 0)
        }
    }

    private func field(
        _ caption: String, _ binding: Binding<String>,
        width: CGFloat, monospaced: Bool = false
    ) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(caption).font(Theme.secondary).foregroundColor(Theme.textFaint)
            FlatTextField(placeholder: "", text: binding, monospaced: monospaced)
                .frame(width: width)
        }
    }

    // MARK: - Footer

    private var footer: some View {
        HStack(spacing: 8) {
            FlatCheckbox(title: "Rename the file to match the pattern", isOn: $renameToPattern)
            Spacer()
            Button("Cancel") { dismiss() }
                .flatButton()
                .keyboardShortcut(.cancelAction)
            Button("Save tags") { save() }
                .flatButton(primary: true)
                .keyboardShortcut(.defaultAction)
        }
        .padding(18)
        .background(Theme.surface)
    }

    // MARK: - Plumbing

    private func pushFields() {
        title = track.title
        artist = track.artist
        album = track.album
        albumArtist = track.albumArtist
        composer = track.composer
        comment = track.comment
        year = track.year
        genre = track.genre
        trackNumber = track.trackNumber
        discNumber = track.discNumber
        // Show djay's BPM if the file has none — saving then writes it in.
        bpm = track.displayBpm
        musicalKey = track.musicalKey
        isrc = track.isrc
        publisher = track.publisher
    }

    private func pullFields() {
        track.title = title.trimmed
        track.artist = artist.trimmed
        track.album = album.trimmed
        track.albumArtist = albumArtist.trimmed
        track.composer = composer.trimmed
        track.comment = comment.trimmed
        track.year = year.trimmed
        track.genre = genre.trimmed
        track.trackNumber = trackNumber.trimmed
        track.discNumber = discNumber.trimmed
        track.bpm = bpm.trimmed
        track.musicalKey = musicalKey.trimmed
        track.isrc = isrc.trimmed
        track.publisher = publisher.trimmed
    }

    private func setStatus(_ text: String, _ color: Color) {
        status = text
        statusColor = color
    }

    // MARK: - Actions

    private func lookup() async {
        busy = true
        defer { busy = false }

        pullFields()
        setStatus("Looking up…", Theme.textDim)

        candidates = await forge.metadata.lookup(
            artist: track.artist, title: track.title,
            durationSeconds: track.durationSeconds, deep: true)

        guard !candidates.isEmpty else {
            matchLabels = ["No matches found"]
            selectedMatch = 0
            setStatus("Nothing found online.", Theme.warn)
            return
        }

        matchLabels = candidates.map(\.display)
        selectedMatch = 0
        applySelectedMatch()
    }

    private func applySelectedMatch() {
        guard selectedMatch >= 0, selectedMatch < candidates.count else { return }
        let chosen = candidates[selectedMatch]

        pullFields()
        chosen.apply(to: track, overwrite: true, titleCase: forge.config.forceTitleCase)
        pushFields()

        setStatus(String(format:
            "Applied %@ match (%.0f). Nothing is written until you save.",
            chosen.source, chosen.score), Theme.good)

        if forge.config.autoArt, !chosen.artURL.isEmpty, !track.hasArt {
            Task {
                guard let bytes = await forge.metadata.downloadArt(chosen.artURL) else { return }
                newArt = bytes
                artImage = TagService.image(from: bytes)
            }
        }
    }

    private func findArt() async {
        busy = true
        defer { busy = false }

        pullFields()
        setStatus("Searching for cover art…", Theme.textDim)

        let artistTerm = track.albumArtist.isEmpty ? track.artist : track.albumArtist
        let albumTerm = track.album.isEmpty ? track.title : track.album
        artOptions = await forge.metadata.findArt(artist: artistTerm, album: albumTerm)

        guard !artOptions.isEmpty else {
            setStatus("No cover art found.", Theme.warn)
            return
        }
        showArtPicker = true
    }

    private func analyze() async {
        busy = true
        defer { busy = false }

        setStatus("Analysing audio…", Theme.textDim)
        let analysis = await AudioAnalyzer.analyze(
            path: track.path, ffmpegPath: forge.downloader.ffmpegPath)

        guard let detected = analysis.bpm else {
            setStatus("Could not analyse that file. Is ffmpeg installed?", Theme.warn)
            return
        }

        bpm = String(Int(detected.rounded()))
        if let key = analysis.key { musicalKey = key }
        track.camelot = analysis.camelot ?? ""

        setStatus(String(format: "%.0f BPM, key %@ (%@)",
                         detected, analysis.key ?? "?", analysis.camelot ?? "?"), Theme.good)
    }

    private func save() {
        pullFields()
        do {
            try TagService.write(track, art: newArt)
            if renameToPattern { _ = forge.renameToPattern(track) }

            // Read the file back so the library row shows what actually landed on
            // disk, not what we hoped would.
            let fresh = TagService.read(path: track.path)
            track.copyTags(from: fresh)

            forge.libraryChanged()
            dismiss()
        } catch {
            writeError = error.localizedDescription
        }
    }
}
