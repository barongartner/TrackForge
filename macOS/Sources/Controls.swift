import SwiftUI

// The six controls that carry the whole look. Square corners, 1px borders, one
// accent — the same rules the Windows build's owner-drawn controls followed.

// MARK: - Buttons

struct FlatButtonStyle: ButtonStyle {
    var primary = false
    var chip = false
    var compact = false

    @Environment(\.isEnabled) private var isEnabled
    @State private var hovering = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(compact ? Theme.secondary : (primary ? Theme.emphasis : Theme.body))
            .foregroundColor(foreground)
            .padding(.horizontal, chip ? 9 : 12)
            .frame(height: compact ? 20 : (primary ? Theme.primaryButtonHeight : Theme.buttonHeight))
            .background(background(pressed: configuration.isPressed))
            .overlay(Rectangle().strokeBorder(border, lineWidth: 1))
            .contentShape(Rectangle())
            .onHover { hovering = $0 }
            .opacity(isEnabled ? 1 : 0.4)
    }

    private var foreground: Color {
        primary ? Theme.text : (hovering && isEnabled ? Theme.text : Theme.textDim)
    }

    private func background(pressed: Bool) -> Color {
        if primary {
            if pressed { return Theme.accentDim }
            return hovering && isEnabled ? Theme.accentHover : Theme.accent
        }
        if pressed { return Theme.surfaceHigh }
        return hovering && isEnabled ? Theme.surfaceHigh : Theme.surfaceAlt
    }

    private var border: Color {
        primary ? .clear : Theme.border
    }
}

extension View {
    func flatButton(primary: Bool = false, chip: Bool = false, compact: Bool = false) -> some View {
        buttonStyle(FlatButtonStyle(primary: primary, chip: chip, compact: compact))
    }
}

/// A nav tab. Active state is the accent underline and nothing else.
struct NavButton: View {
    let title: String
    let active: Bool
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(Theme.emphasis)
                .foregroundColor(active ? Theme.text : (hovering ? Theme.textStrong : Theme.textDim))
                .padding(.horizontal, 15)
                .frame(height: Theme.topBarHeight)
                .overlay(alignment: .bottom) {
                    Rectangle()
                        .fill(active ? Theme.accent : .clear)
                        .frame(height: 2)
                }
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

// MARK: - Surfaces

/// A content card: flat surface, one hairline border, square corners.
struct CardPanel<Content: View>: View {
    var background: Color = Theme.surface
    var borderColor: Color = Theme.border
    @ViewBuilder var content: Content

    var body: some View {
        content
            .background(background)
            .overlay(Rectangle().strokeBorder(borderColor, lineWidth: 1))
    }
}

// MARK: - Text fields

struct FlatTextField: View {
    let placeholder: String
    @Binding var text: String
    var monospaced = false
    var onSubmit: (() -> Void)?

    var body: some View {
        TextField("", text: $text, prompt:
            Text(placeholder).foregroundColor(Theme.textFaint))
            .textFieldStyle(.plain)
            .font(monospaced ? Theme.numeric : Theme.body)
            .foregroundColor(Theme.text)
            .padding(.horizontal, 7)
            .frame(height: Theme.fieldHeight)
            .background(Theme.surfaceAlt)
            .overlay(Rectangle().strokeBorder(Theme.border, lineWidth: 1))
            .onSubmit { onSubmit?() }
    }
}

struct FlatTextEditor: View {
    let placeholder: String
    @Binding var text: String

    var body: some View {
        ZStack(alignment: .topLeading) {
            Theme.surfaceAlt
            if text.isEmpty {
                Text(placeholder)
                    .font(Theme.secondary)
                    .foregroundColor(Theme.textFaint)
                    .padding(.horizontal, 9)
                    .padding(.top, 7)
                    .allowsHitTesting(false)
            }
            TextEditor(text: $text)
                .font(Theme.numeric)
                .foregroundColor(Theme.text)
                .scrollContentBackground(.hidden)
                .background(Color.clear)
                .padding(.horizontal, 4)
                .padding(.vertical, 3)
        }
        .overlay(Rectangle().strokeBorder(Theme.border, lineWidth: 1))
    }
}

// MARK: - Progress

struct FlatProgress: View {
    /// 0–100, to match how the jobs report themselves.
    var value: Double
    var barColor: Color = Theme.accent
    var height: CGFloat = 3

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Rectangle().fill(Theme.surfaceHigh)
                Rectangle()
                    .fill(barColor)
                    .frame(width: geometry.size.width * min(max(value, 0), 100) / 100)
            }
        }
        .frame(height: height)
    }
}

// MARK: - Artwork

/// Cover art, or a wave mark standing in for one. Never an empty grey box —
/// a placeholder that looks like a missing image reads as a bug.
struct WaveMark: View {
    var image: NSImage?
    var size: CGFloat

    private static let bars: [CGFloat] = [0.30, 0.62, 0.44, 0.88, 0.52, 0.72, 0.36]

    var body: some View {
        ZStack {
            Theme.surfaceAlt
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            } else {
                HStack(alignment: .center, spacing: max(2, size * 0.035)) {
                    ForEach(Array(Self.bars.enumerated()), id: \.offset) { _, height in
                        Rectangle()
                            .fill(Theme.waveBar)
                            .frame(width: max(2, size * 0.045), height: size * 0.55 * height)
                    }
                }
            }
        }
        .frame(width: size, height: size)
        .clipped()
        .overlay(Rectangle().strokeBorder(Theme.border, lineWidth: 1))
    }
}

// MARK: - Small pieces

struct Pill: View {
    let text: String
    var color: Color = Theme.textDim

    var body: some View {
        Text(text)
            .font(Theme.secondary)
            .foregroundColor(color)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Theme.surfaceHigh)
    }
}

struct StatusDot: View {
    var color: Color

    var body: some View {
        Circle().fill(color).frame(width: 6, height: 6)
    }
}

struct FlatCheckbox: View {
    let title: String
    @Binding var isOn: Bool

    var body: some View {
        Toggle(isOn: $isOn) {
            Text(title).font(Theme.body).foregroundColor(Theme.text)
        }
        .toggleStyle(.checkbox)
    }
}

/// A labelled dropdown, laid out on the same 82pt label column as Settings.
struct FlatPicker: View {
    let caption: String
    @Binding var selection: String
    let options: [String]
    var width: CGFloat = 92

    var body: some View {
        HStack(spacing: 0) {
            Text(caption)
                .font(Theme.body)
                .foregroundColor(Theme.textDim)
                .frame(width: 82, alignment: .leading)
            Picker("", selection: $selection) {
                ForEach(options, id: \.self) { Text($0).font(Theme.body) }
            }
            .labelsHidden()
            .frame(width: width)
        }
    }
}

/// The empty state a page shows before it has anything to list.
struct EmptyHint: View {
    let lines: [String]

    var body: some View {
        VStack(spacing: 8) {
            ForEach(Array(lines.enumerated()), id: \.offset) { index, line in
                Text(line)
                    .font(index == 0 ? Theme.body : Theme.secondary)
                    .foregroundColor(Theme.textFaint)
                    .multilineTextAlignment(.center)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
