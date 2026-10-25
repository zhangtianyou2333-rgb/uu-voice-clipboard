#!/bin/zsh
set -euo pipefail

script_root="${0:A:h}"
cd "$script_root"

version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Resources/Info.plist)"
app="dist/v$version/UU Mac Paste Assistant.app"
package="dist/UU-Mac-Paste-Assistant-V$version-macOS.zip"
if [[ -e "$app" || -e "$package" ]]; then
    print -u2 -r -- "该版本产物已存在，为避免覆盖旧包已停止：$package"
    exit 1
fi

swift build -c release --arch arm64 \
    -Xswiftc -debug-prefix-map -Xswiftc "$script_root=." \
    -Xswiftc -file-prefix-map -Xswiftc "$script_root=."
swift build -c release --arch x86_64 \
    -Xswiftc -debug-prefix-map -Xswiftc "$script_root=." \
    -Xswiftc -file-prefix-map -Xswiftc "$script_root=."
mkdir -p .build/universal-release
lipo -create \
    .build/arm64-apple-macosx/release/UUVoiceClipboardMacAssistant \
    .build/x86_64-apple-macosx/release/UUVoiceClipboardMacAssistant \
    -output .build/universal-release/UUVoiceClipboardMacAssistant

staging="$(mktemp -d "$script_root/.build/mac-package.XXXXXX")"
trap 'rm -r -- "$staging"' EXIT
staged_app="$staging/UU Mac Paste Assistant.app"
staged_package="$staging/UU-Mac-Paste-Assistant-V$version-macOS.zip"
mkdir -p "$staged_app/Contents/MacOS" "$staged_app/Contents/Resources"
cp .build/universal-release/UUVoiceClipboardMacAssistant "$staged_app/Contents/MacOS/UUVoiceClipboardMacAssistant"
cp Resources/Info.plist "$staged_app/Contents/Info.plist"
chmod +x "$staged_app/Contents/MacOS/UUVoiceClipboardMacAssistant"
# Remove debug-map symbols carrying local object-file paths before signing.
strip -S "$staged_app/Contents/MacOS/UUVoiceClipboardMacAssistant"
codesign --force --deep --sign - "$staged_app" >/dev/null
codesign --verify --deep --strict "$staged_app"

ditto -c -k --sequesterRsrc --keepParent "$staged_app" "$staged_package"
zip -q "$staged_package" README-FIRST-MAC.md Start-Mac-Assistant.command
unzip -tq "$staged_package"
mkdir -p "dist/v$version"
mv "$staged_app" "$app"
mv "$staged_package" "$package"
print -r -- "已生成：$package"
