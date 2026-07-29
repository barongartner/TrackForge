import AppKit
import CoreGraphics
import Foundation

// Renders TrackForge.icns from the same shapes the app draws.
//
// The mark is the wave placeholder from GrabCard's artwork slot, in the one accent
// colour the rest of the UI is allowed to use. Nothing invented — the icon is the
// app's own vocabulary at 1024px.
//
//   ./Tools/makeicon.sh

let background = CGColor(red: 0x15 / 255, green: 0x18 / 255, blue: 0x1d / 255, alpha: 1)
let border     = CGColor(red: 0x2e / 255, green: 0x34 / 255, blue: 0x3d / 255, alpha: 1)
let accent     = CGColor(red: 0xc8 / 255, green: 0x44 / 255, blue: 0x2f / 255, alpha: 1)
let dim        = CGColor(red: 0x78 / 255, green: 0x2a / 255, blue: 0x1e / 255, alpha: 1)

/// Relative bar heights, and which of them get the full accent. The tallest three
/// carry it; the rest sit back, so the mark reads as a waveform rather than a
/// barcode.
let bars: [(height: CGFloat, strong: Bool)] = [
    (0.30, false), (0.62, true), (0.44, false), (0.88, true),
    (0.52, false), (0.72, true), (0.36, false),
]

func render(size: Int) -> CGImage? {
    let s = CGFloat(size)
    guard let context = CGContext(
        data: nil, width: size, height: size,
        bitsPerComponent: 8, bytesPerRow: 0,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    else { return nil }

    context.setAllowsAntialiasing(true)
    context.interpolationQuality = .high

    // macOS icons sit inside the canvas with a margin; the rounded square is the
    // shape every other app on the Dock uses.
    let inset = s * 0.085
    let rect = CGRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
    let radius = rect.width * 0.225

    let body = CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius,
                      transform: nil)
    context.addPath(body)
    context.setFillColor(background)
    context.fillPath()

    context.addPath(body)
    context.setStrokeColor(border)
    context.setLineWidth(max(1, s * 0.006))
    context.strokePath()

    // The wave mark, centred, at the same proportions the app draws it.
    let barWidth = rect.width * 0.072
    let spacing = rect.width * 0.038
    let totalWidth = CGFloat(bars.count) * barWidth + CGFloat(bars.count - 1) * spacing
    var x = rect.midX - totalWidth / 2
    let maxHeight = rect.height * 0.56
    let barRadius = barWidth * 0.5

    for bar in bars {
        let height = maxHeight * bar.height
        let barRect = CGRect(x: x, y: rect.midY - height / 2, width: barWidth, height: height)
        context.addPath(CGPath(roundedRect: barRect, cornerWidth: barRadius,
                               cornerHeight: barRadius, transform: nil))
        context.setFillColor(bar.strong ? accent : dim)
        context.fillPath()
        x += barWidth + spacing
    }

    return context.makeImage()
}

// MARK: - Write the iconset

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1
                 ? CommandLine.arguments[1] : "TrackForge.icns")
let iconset = FileManager.default.temporaryDirectory
    .appendingPathComponent("TrackForge-\(UUID().uuidString).iconset")
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

// The sizes iconutil expects, each at 1x and 2x.
let sizes = [16, 32, 128, 256, 512]

for size in sizes {
    for scale in [1, 2] {
        let pixels = size * scale
        guard let image = render(size: pixels) else {
            FileHandle.standardError.write(Data("could not render \(pixels)px\n".utf8))
            exit(1)
        }
        let name = scale == 1 ? "icon_\(size)x\(size).png" : "icon_\(size)x\(size)@2x.png"
        let bitmap = NSBitmapImageRep(cgImage: image)
        bitmap.size = NSSize(width: pixels, height: pixels)
        guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
        try png.write(to: iconset.appendingPathComponent(name))
    }
}

let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", output.path]
try iconutil.run()
iconutil.waitUntilExit()

try? FileManager.default.removeItem(at: iconset)

guard iconutil.terminationStatus == 0 else {
    FileHandle.standardError.write(Data("iconutil failed\n".utf8))
    exit(1)
}
print("wrote \(output.path)")
