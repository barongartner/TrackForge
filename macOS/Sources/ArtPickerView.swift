import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// Grid of cover art options. Click to select, double-click to take it.
struct ArtPickerView: View {
    @EnvironmentObject private var forge: ForgeService

    let options: [MetadataClient.ArtOption]
    /// Full-resolution bytes and the URL they came from, or nil on cancel.
    let onFinish: (Data?, String?) -> Void

    @State private var thumbnails: [UUID: NSImage] = [:]
    @State private var selected: MetadataClient.ArtOption?
    @State private var selectedBytes: Data?
    @State private var hint = "Pick a cover. It gets square-cropped and re-encoded at 1000px."

    private let columns = [GridItem(.adaptive(minimum: 150, maximum: 168), spacing: 12)]

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                LazyVGrid(columns: columns, spacing: 12) {
                    ForEach(options) { option in
                        tile(option)
                    }
                }
                .padding(14)
            }

            Divider().overlay(Theme.border)

            HStack(spacing: 8) {
                Text(hint)
                    .font(Theme.secondary)
                    .foregroundColor(Theme.textDim)
                    .lineLimit(1)
                    .truncationMode(.middle)

                Spacer(minLength: 8)

                Button("From file…") { browseLocal() }.flatButton()
                Button("Cancel") { onFinish(nil, nil) }
                    .flatButton()
                    .keyboardShortcut(.cancelAction)
                Button("Use this") { onFinish(selectedBytes, selected?.url) }
                    .flatButton(primary: true)
                    .disabled(selectedBytes == nil)
                    .keyboardShortcut(.defaultAction)
            }
            .padding(14)
            .background(Theme.surface)
        }
        .frame(width: 760, height: 560)
        .background(Theme.background)
        .task { await loadThumbnails() }
    }

    private func tile(_ option: MetadataClient.ArtOption) -> some View {
        VStack(spacing: 6) {
            WaveMark(image: thumbnails[option.id], size: 150)
            Text("\(option.label)  (\(option.source))")
                .font(Theme.secondary)
                .foregroundColor(Theme.textDim)
                .lineLimit(2)
                .multilineTextAlignment(.center)
                .frame(height: 28, alignment: .top)
        }
        .padding(9)
        .background(selected?.id == option.id ? Theme.accentDim : Theme.surface)
        .contentShape(Rectangle())
        .onTapGesture(count: 2) {
            Task {
                await choose(option)
                if selectedBytes != nil { onFinish(selectedBytes, option.url) }
            }
        }
        .onTapGesture { Task { await choose(option) } }
    }

    private func loadThumbnails() async {
        for option in options {
            let url = option.thumbURL.isBlank ? option.url : option.thumbURL
            guard let bytes = await forge.metadata.downloadArt(url) else { continue }
            thumbnails[option.id] = TagService.image(from: bytes)
        }
    }

    private func choose(_ option: MetadataClient.ArtOption) async {
        selected = option
        hint = "Downloading full resolution…"

        // The 1500px iTunes URL is synthesised, so it occasionally 404s where the
        // thumbnail does not. Falling back beats handing back nothing.
        var candidate = await forge.metadata.downloadArt(option.url)
        if candidate == nil { candidate = await forge.metadata.downloadArt(option.thumbURL) }

        guard let full = candidate else {
            hint = "That cover could not be downloaded. Try another."
            selectedBytes = nil
            return
        }

        selectedBytes = full
        if let (width, height) = TagService.dimensions(of: full) {
            hint = "Selected  \(width) × \(height)  from \(option.source)"
        } else {
            hint = "Selected."
        }
    }

    private func browseLocal() {
        let panel = NSOpenPanel()
        panel.title = "Choose a cover image"
        panel.allowedContentTypes = [.jpeg, .png, .webP, .bmp, .tiff, .heic]
        panel.allowsMultipleSelection = false

        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            onFinish(try Data(contentsOf: url), nil)
        } catch {
            hint = "Could not read that image: \(error.localizedDescription)"
        }
    }
}
