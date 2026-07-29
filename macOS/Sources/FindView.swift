import AppKit
import SwiftUI

/// Takes track names — typed in or sent over from the Library — and finds them on
/// YouTube, so anything missing from disk can go straight into Grab.
struct FindView: View {
    @EnvironmentObject private var forge: ForgeService

    @State private var queries = ""
    @State private var rows: [FindRow] = []
    @State private var selection = Set<FindRow.ID>()
    @State private var note = ""
    @State private var noteColor = Theme.textFaint
    @State private var searching = false

    var body: some View {
        VStack(spacing: 8) {
            intake
            results
        }
        .padding(Theme.pad)
        .onReceive(NotificationCenter.default.publisher(for: .sendToFind)) { notification in
            guard let lines = notification.object as? [String] else { return }
            let unique = Array(NSOrderedSet(array: lines.filter { !$0.isBlank })) as? [String] ?? []
            queries = unique.joined(separator: "\n")
            note = "\(unique.count) loaded from library."
            noteColor = Theme.textFaint
        }
    }

    private var intake: some View {
        CardPanel {
            VStack(alignment: .leading, spacing: Theme.gap) {
                FlatTextEditor(
                    placeholder: "Artist - Title, one per line. Or send tracks here from Library.",
                    text: $queries)
                    .frame(height: 48)

                HStack(spacing: Theme.gap) {
                    Button(searching ? "…" : "Search") { Task { await search() } }
                        .flatButton(primary: true)
                        .disabled(searching)

                    Button("Send best") { send(rows.filter(\.isBest)) }
                        .flatButton()
                        .disabled(rows.isEmpty)

                    Button("Clear") { rows.removeAll(); note = "" }
                        .flatButton()
                        .disabled(rows.isEmpty)

                    Text(note)
                        .font(Theme.secondary)
                        .foregroundColor(noteColor)
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

    private var results: some View {
        Table(rows, selection: $selection) {
            // Only the best hit for a query carries the query text, so a run of
            // three results reads as one group rather than three repeats.
            TableColumn("Searched for") { row in
                Text(row.isBest ? row.query : "").foregroundColor(Theme.text)
            }
            .width(min: 120, ideal: 190)

            TableColumn("YouTube title") { row in
                Text(row.entry.rawTitle)
                    .foregroundColor(row.isBest ? Theme.text : Theme.textDim)
            }
            .width(min: 160, ideal: 290)

            TableColumn("Channel") { row in
                Text(row.entry.uploader).foregroundColor(Theme.textDim)
            }
            .width(min: 90, ideal: 150)

            TableColumn("Len") { row in
                Text(row.entry.durationText).font(Theme.numeric).foregroundColor(Theme.textDim)
            }
            .width(56)

            TableColumn("Views") { row in
                Text(row.entry.viewCount > 0
                     ? row.entry.viewCount.formatted(.number) : "")
                    .font(Theme.numeric)
                    .foregroundColor(Theme.textDim)
            }
            .width(84)

            TableColumn("Link") { row in
                Text(row.entry.url).font(Theme.numericSmall).foregroundColor(Theme.textFaint)
            }
            .width(min: 140, ideal: 210)
        }
        .font(Theme.body)
        .tableStyle(.inset(alternatesRowBackgrounds: true))
        .contextMenu(forSelectionType: FindRow.ID.self) { ids in
            Button("Send to Grab") { send(rows(with: ids)) }
            Button("Copy link") { copy(rows(with: ids)) }
            Button("Open in browser") { open(rows(with: ids)) }
        } primaryAction: { ids in
            send(rows(with: ids))
        }
        .overlay {
            if rows.isEmpty {
                EmptyHint(lines: [
                    "Nothing searched yet.",
                    "Type track names above, or select tracks in Library and hit Find on YouTube.",
                ])
            }
        }
    }

    // MARK: - Actions

    private func search() async {
        let list = queries
            .split(whereSeparator: \.isNewline)
            .map { $0.trimmed }
            .filter { !$0.isEmpty }
        let unique = Array(NSOrderedSet(array: list)) as? [String] ?? []

        guard !unique.isEmpty else {
            note = "Nothing to search for."
            noteColor = Theme.warn
            return
        }

        searching = true
        rows.removeAll()
        defer { searching = false }

        for (i, query) in unique.enumerated() {
            note = "\(i + 1)/\(unique.count)  \(query)"
            noteColor = Theme.textMuted

            let hits = await forge.downloader.search(query, limit: 3)
            for (index, hit) in hits.enumerated() {
                rows.append(FindRow(query: query, entry: hit, isBest: index == 0))
            }
        }

        let found = Set(rows.map(\.query)).count
        note = "Results for \(found)/\(unique.count). Double-click a row to send it to Grab."
        noteColor = Theme.textFaint
    }

    private func rows(with ids: Set<FindRow.ID>) -> [FindRow] {
        rows.filter { ids.contains($0.id) }
    }

    private func send(_ rows: [FindRow]) {
        let urls = rows.map(\.entry.url).filter { !$0.isEmpty }
        guard !urls.isEmpty else { return }
        NotificationCenter.default.post(name: .sendToGrab, object: urls)
    }

    private func copy(_ rows: [FindRow]) {
        let urls = rows.map(\.entry.url).filter { !$0.isEmpty }
        guard !urls.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(urls.joined(separator: "\n"), forType: .string)
    }

    private func open(_ rows: [FindRow]) {
        for row in rows.prefix(5) {
            guard let url = URL(string: row.entry.url) else { continue }
            NSWorkspace.shared.open(url)
        }
    }
}

struct FindRow: Identifiable {
    let id = UUID()
    let query: String
    let entry: VideoEntry
    let isBest: Bool
}
