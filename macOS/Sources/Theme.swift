import SwiftUI

/// Flat dark palette, carried over from the Windows build.
/// No gradients, no glows, no emoji, square corners.
///
/// Accent discipline: `accent` appears only on the active tab underline, primary
/// buttons, the 2px inset bar on a selected or best-match row, running progress
/// bars, and the uppercase section eyebrows in Settings. Nowhere else.
enum Theme {
    // ---- colour -----------------------------------------------------------

    static let background  = hex(0x0d0f12)
    static let surface     = hex(0x15181d)
    static let surfaceAlt  = hex(0x1b1f25)
    static let surfaceHigh = hex(0x23282f)
    static let border      = hex(0x2e343d)

    /// Divider between table rows.
    static let rowDivider = hex(0x191d23)
    /// Odd table rows, and non-best rows in Find.
    static let rowOdd = hex(0x171b21)

    /// Top bar, jobs dock, tool block — chrome rather than content.
    static let chromePanel  = hex(0x12151a)
    static let chromeBorder = hex(0x22272f)

    static let text       = hex(0xe8ebef)
    static let textStrong = hex(0xc1c8d1)
    static let textDim    = hex(0x929ca8)
    static let textMuted  = hex(0x8a94a1)
    static let textFaint  = hex(0x626c78)
    static let textFainter = hex(0x5b6572)
    static let textCount  = hex(0x7d8794)

    static let accent      = hex(0xc8442f)
    static let accentHover = hex(0xde543d)
    static let accentDim   = hex(0x782a1e)

    static let good = hex(0x4ead7a)
    static let warn = hex(0xd19c3e)
    static let bad  = hex(0xc65454)
    static let selection = hex(0x262e38)

    /// Border of a grab card once its download finished.
    static let doneBorder = hex(0x2f4a3b)
    /// Wave-mark bars in the artwork placeholder.
    static let waveBar = hex(0x3a4250)

    // ---- type -------------------------------------------------------------
    // The system UI face throughout; a monospaced face for every numeric and
    // path, which is what makes the table columns line up.

    static let body      = Font.system(size: 12)
    static let emphasis  = Font.system(size: 12, weight: .semibold)
    static let secondary = Font.system(size: 11)
    static let eyebrow   = Font.system(size: 10, weight: .semibold)
    static let inspectorTitle = Font.system(size: 14, weight: .semibold)

    static let numeric      = Font.system(size: 12, design: .monospaced)
    static let numericSmall = Font.system(size: 11, design: .monospaced)

    // ---- metrics ----------------------------------------------------------

    static let gap: CGFloat = 6
    static let pad: CGFloat = 10
    static let fieldHeight: CGFloat = 24
    static let rowHeight: CGFloat = 26
    static let headerHeight: CGFloat = 24
    static let buttonHeight: CGFloat = 24
    static let primaryButtonHeight: CGFloat = 26
    static let topBarHeight: CGFloat = 38
    static let jobsDockWidth: CGFloat = 320
    static let grabArtSize: CGFloat = 88

    static func hex(_ value: UInt32) -> Color {
        Color(
            .sRGB,
            red:   Double((value >> 16) & 0xff) / 255,
            green: Double((value >> 8) & 0xff) / 255,
            blue:  Double(value & 0xff) / 255,
            opacity: 1
        )
    }
}

/// Uppercase section label: 10px 600, letter-spaced, accent.
struct Eyebrow: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text.uppercased())
            .font(Theme.eyebrow)
            .tracking(1.6)
            .foregroundColor(Theme.accent)
    }
}
