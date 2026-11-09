import AppKit
import Foundation

// Usage: swift render.swift <input.txt> <output.png> "<window title>"
// Draws the text of a captured console run on a dark terminal-style background. This is a rendering of the real
// output, not a capture of a window.
let arguments = CommandLine.arguments
guard arguments.count >= 3, let raw = try? String(contentsOfFile: arguments[1], encoding: .utf8) else {
    FileHandle.standardError.write("usage: render.swift input.txt output.png [title]\n".data(using: .utf8)!)
    exit(2)
}
let title = arguments.count > 3 ? arguments[3] : "Terminal"
let lines = raw.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n")
let font = NSFont(name: "Menlo-Regular", size: 13)!
let boldFont = NSFont(name: "Menlo-Bold", size: 13)!
let lineHeight: CGFloat = 18
let margin: CGFloat = 18
let titleBar: CGFloat = 34
let scale: CGFloat = 2

func color(for line: String) -> NSColor {
    let trimmed = line.trimmingCharacters(in: .whitespaces)
    if line.contains("[listener]") { return NSColor(calibratedRed: 0.55, green: 0.85, blue: 0.55, alpha: 1) }
    if line.contains("[pending]") { return NSColor(calibratedRed: 0.95, green: 0.80, blue: 0.40, alpha: 1) }
    if trimmed.hasPrefix("Failed ") || trimmed.hasPrefix("Could not") { return NSColor(calibratedRed: 0.95, green: 0.50, blue: 0.50, alpha: 1) }
    if trimmed.hasPrefix("Synced ") || trimmed.hasPrefix("Saved ") || trimmed.hasPrefix("Deleted ") { return NSColor(calibratedRed: 0.55, green: 0.80, blue: 0.95, alpha: 1) }
    if line.hasPrefix("===") { return NSColor.white }
    return NSColor(calibratedWhite: 0.85, alpha: 1)
}

var widest: CGFloat = 0
for line in lines {
    let size = (line as NSString).size(withAttributes: [.font: font])
    widest = max(widest, size.width)
}
let titleWidthNeeded = (title as NSString).size(withAttributes: [.font: boldFont]).width + 2 * 90
let width = max(widest + margin * 2, titleWidthNeeded, 640)
let height = titleBar + margin * 2 + CGFloat(lines.count) * lineHeight

guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: Int(width * scale), pixelsHigh: Int(height * scale),
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { exit(1) }
bitmap.size = NSSize(width: width, height: height)

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSColor(calibratedWhite: 0.11, alpha: 1).setFill()
NSRect(x: 0, y: 0, width: width, height: height).fill()
NSColor(calibratedWhite: 0.20, alpha: 1).setFill()
NSRect(x: 0, y: height - titleBar, width: width, height: titleBar).fill()
for (index, dot) in [NSColor.systemRed, NSColor.systemYellow, NSColor.systemGreen].enumerated() {
    dot.setFill()
    NSBezierPath(ovalIn: NSRect(x: 14 + CGFloat(index) * 20, y: height - titleBar + 11, width: 12, height: 12)).fill()
}
let titleAttributes: [NSAttributedString.Key: Any] = [.font: boldFont, .foregroundColor: NSColor(calibratedWhite: 0.8, alpha: 1)]
let titleWidth = (title as NSString).size(withAttributes: titleAttributes).width
(title as NSString).draw(at: NSPoint(x: (width - titleWidth) / 2, y: height - titleBar + 9), withAttributes: titleAttributes)

for (index, line) in lines.enumerated() {
    let y = height - titleBar - margin - CGFloat(index + 1) * lineHeight + 4
    (line as NSString).draw(at: NSPoint(x: margin, y: y), withAttributes: [.font: font, .foregroundColor: color(for: line)])
}
NSGraphicsContext.restoreGraphicsState()

guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
try! png.write(to: URL(fileURLWithPath: arguments[2]))
print("wrote \(arguments[2]) \(Int(width * scale))x\(Int(height * scale))")
