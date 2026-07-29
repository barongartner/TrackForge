import SwiftUI

/// Picks which fields a bulk "fill tags from online" run is allowed to touch.
struct EnrichOptionsView: View {
    let trackCount: Int
    let onFinish: (ForgeService.EnrichOptions?) -> Void

    // Title and artist are off by default: they are usually right already, and a
    // wrong match rewriting them is the one mistake that is hard to spot later.
    @State private var fields: [FieldToggle] = [
        FieldToggle(key: "album", label: "Album", isOn: true),
        FieldToggle(key: "albumartist", label: "Album artist", isOn: true),
        FieldToggle(key: "year", label: "Year", isOn: true),
        FieldToggle(key: "genre", label: "Genre", isOn: true),
        FieldToggle(key: "track", label: "Track number", isOn: true),
        FieldToggle(key: "disc", label: "Disc number", isOn: true),
        FieldToggle(key: "title", label: "Title", isOn: false),
        FieldToggle(key: "artist", label: "Artist", isOn: false),
        FieldToggle(key: "isrc", label: "ISRC", isOn: true),
        FieldToggle(key: "publisher", label: "Publisher", isOn: false),
    ]

    @State private var overwrite = false
    @State private var fetchArt = true
    @State private var analyzeAudio = false
    @State private var renameFiles = false

    struct FieldToggle: Identifiable {
        let key: String
        let label: String
        var isOn: Bool
        var id: String { key }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("\(trackCount) track(s) selected. iTunes, Deezer and MusicBrainz get "
                 + "searched, and the highest-scoring match wins.")
                .font(Theme.body)
                .foregroundColor(Theme.textDim)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.bottom, 18)

            Eyebrow("Fields to fill").padding(.bottom, 8)

            LazyVGrid(columns: [GridItem(.fixed(200), alignment: .leading),
                                GridItem(.fixed(200), alignment: .leading)],
                      alignment: .leading, spacing: 4) {
                ForEach($fields) { $field in
                    FlatCheckbox(title: field.label, isOn: $field.isOn)
                }
            }

            Eyebrow("Options").padding(.top, 20).padding(.bottom, 8)
            FlatCheckbox(title: "Overwrite fields that already have a value", isOn: $overwrite)
            FlatCheckbox(title: "Download and embed cover art where it's missing", isOn: $fetchArt)
            FlatCheckbox(title: "Analyse BPM and key from the audio (slower)", isOn: $analyzeAudio)
            FlatCheckbox(title: "Rename files to match the naming pattern", isOn: $renameFiles)

            Text("Tags are written straight to the files. There is no undo.")
                .font(Theme.secondary)
                .foregroundColor(Theme.warn)
                .padding(.top, 16)

            HStack(spacing: 8) {
                Spacer()
                Button("Cancel") { onFinish(nil) }
                    .flatButton()
                    .keyboardShortcut(.cancelAction)
                Button("Run") {
                    onFinish(ForgeService.EnrichOptions(
                        overwrite: overwrite,
                        fetchArt: fetchArt,
                        analyzeAudio: analyzeAudio,
                        renameFiles: renameFiles,
                        fields: fields.filter(\.isOn).map(\.key)))
                }
                .flatButton(primary: true)
                .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 20)
        }
        .padding(24)
        .frame(width: 500)
        .background(Theme.background)
    }
}
