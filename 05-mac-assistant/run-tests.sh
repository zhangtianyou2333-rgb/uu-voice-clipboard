#!/bin/zsh
set -euo pipefail

script_root="${0:A:h}"
cd "$script_root"
mkdir -p .build/checks
swiftc Sources/UUVoiceClipboardMacAssistant/DirectedPaster.swift \
    Sources/UUVoiceClipboardMacAssistant/MenuPaster.swift \
    Sources/UUVoiceClipboardMacAssistant/BridgeDiagnostics.swift \
    Sources/UUVoiceClipboardMacAssistant/FocusReturnPolicy.swift \
    Tests/DirectedPasterTests/main.swift \
    -o .build/checks/DirectedPasterTests
.build/checks/DirectedPasterTests
