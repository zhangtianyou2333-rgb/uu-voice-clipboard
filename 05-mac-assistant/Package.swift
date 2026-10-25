// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "UUVoiceClipboardMacAssistant",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "UUVoiceClipboardMacAssistant", targets: ["UUVoiceClipboardMacAssistant"])
    ],
    targets: [
        .executableTarget(name: "UUVoiceClipboardMacAssistant")
    ]
)
