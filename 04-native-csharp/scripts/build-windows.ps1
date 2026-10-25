$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$nativeRoot = Split-Path -Parent $scriptRoot
$project = Join-Path $nativeRoot 'UUVoiceClipboard.Native\UUVoiceClipboard.Native.csproj'
$projectRoot = Split-Path -Parent $nativeRoot
$publish = Join-Path $nativeRoot 'publish\v1.12.0'
$deliverables = Join-Path $projectRoot '02-deliverables'
$package = Join-Path $deliverables 'UU语音中转-会员预览版-V1.12.0-win-x64.zip'

if ((Test-Path $package) -or (Test-Path $publish)) {
    throw '当前版本产物已经存在。请使用新版本号，不要覆盖已交付的版本。'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '未找到 dotnet。请安装 .NET 7 SDK 或使用已发布的自包含程序。'
}

New-Item -ItemType Directory -Force -Path $publish | Out-Null
New-Item -ItemType Directory -Force -Path $deliverables | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publish

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish 失败，未创建发布包。'
}

$readme = Join-Path $nativeRoot 'README-FIRST-NATIVE.md'
$notices = Join-Path $nativeRoot 'THIRD-PARTY-NOTICES.txt'
$starter = Join-Path $nativeRoot 'Start-Native.vbs'
$exe = Join-Path $publish 'UUVoiceClipboard.Native.exe'
$webLoader = Join-Path $publish 'WebView2Loader.dll'
if (-not (Test-Path $exe)) {
    throw "发布结果中没有找到 $exe"
}
if (-not (Test-Path $webLoader)) {
    throw '发布结果缺少 WebView2Loader.dll，不创建不完整的发布包。'
}

$licenses = Join-Path $projectRoot 'LICENSES'
if (-not (Test-Path $licenses)) {
    throw '缺少 LICENSES 目录，不创建发布包。'
}
Compress-Archive -Path $exe, $webLoader, $readme, $notices, $starter, $licenses -DestinationPath $package -CompressionLevel Optimal
Write-Host "已生成：$package"
