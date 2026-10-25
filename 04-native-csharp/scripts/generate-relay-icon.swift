import AppKit
import Foundation

// Offline vector rendering: no browser, font dependency, microphone or network.
let nativeRoot = URL(fileURLWithPath: CommandLine.arguments[1])
let assetRoot = nativeRoot.appendingPathComponent("UUVoiceClipboard.Native/Assets")
try FileManager.default.createDirectory(at: assetRoot, withIntermediateDirectories: true)
let sizes = [16, 20, 24, 32, 48, 64, 128, 256]
var frames: [Data] = []
for size in sizes {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: size * 4, bitsPerPixel: 32)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    let transform = NSAffineTransform()
    transform.scale(by: CGFloat(size) / 32)
    transform.concat()
    let plate = NSBezierPath(roundedRect: NSRect(x: 0.5, y: 0.5, width: 31, height: 31), xRadius: 7, yRadius: 7)
    NSColor(srgbRed: 10/255, green: 12/255, blue: 13/255, alpha: 1).setFill()
    plate.fill()
    NSColor(srgbRed: 65/255, green: 73/255, blue: 77/255, alpha: 1).setStroke()
    plate.lineWidth = 0.7
    plate.stroke()
    NSColor(srgbRed: 151/255, green: 245/255, blue: 57/255, alpha: 1).setStroke()
    for left: CGFloat in [7, 18] {
        let u = NSBezierPath()
        u.lineCapStyle = .round
        u.lineJoinStyle = .round
        u.lineWidth = 2.7
        u.move(to: NSPoint(x: left, y: 22))
        u.line(to: NSPoint(x: left, y: 12))
        u.curve(to: NSPoint(x: left + 7, y: 12),
            controlPoint1: NSPoint(x: left, y: 7.7), controlPoint2: NSPoint(x: left + 7, y: 7.7))
        u.line(to: NSPoint(x: left + 7, y: 22))
        u.stroke()
    }
    NSGraphicsContext.restoreGraphicsState()
    let png = bitmap.representation(using: .png, properties: [:])!
    frames.append(png)
    if size == 256 { try png.write(to: assetRoot.appendingPathComponent("Relay-preview.png")) }
}
func little(_ value: UInt32, count: Int) -> Data {
    Data((0..<count).map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) })
}
var ico = Data([0, 0, 1, 0])
ico.append(little(UInt32(frames.count), count: 2))
var offset = 6 + frames.count * 16
for (index, png) in frames.enumerated() {
    let size = sizes[index]
    ico.append(contentsOf: [UInt8(size == 256 ? 0 : size), UInt8(size == 256 ? 0 : size), 0, 0])
    ico.append(little(1, count: 2))
    ico.append(little(32, count: 2))
    ico.append(little(UInt32(png.count), count: 4))
    ico.append(little(UInt32(offset), count: 4))
    offset += png.count
}
for frame in frames { ico.append(frame) }
try ico.write(to: assetRoot.appendingPathComponent("Relay.ico"))
print("Rendered Relay.ico: \(frames.count) PNG frames, \(ico.count) bytes")
