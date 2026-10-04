<#
.SYNOPSIS
    AgentDeskApp の自己完結型 (self-contained) Release ビルドを作成し、配布用zipにまとめるスクリプト。

.DESCRIPTION
    dotnet publish を Release / win-x64 / 自己完結型 / シングルファイル出力オプション付きで実行し、
    .NET 10 未導入のWindows PCでもそのまま実行できる配布物を生成する。
    生成された publish フォルダを Compress-Archive で zip 化し、release フォルダに出力する。

.PARAMETER Version
    配布zipのファイル名に付与するバージョン番号。既定値は "0.9.0"。

.EXAMPLE
    ./scripts/build_release_zip.ps1
    ./scripts/build_release_zip.ps1 -Version "1.1.0"
#>
param(
    [string]$Version = "0.9.0"
)

# エラー発生時は即座に処理を停止する
$ErrorActionPreference = "Stop"

# スクリプトの配置場所からリポジトリルートを算出する
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

$csprojPath = Join-Path $repoRoot "src/AgentDeskApp/AgentDeskApp.csproj"
$publishDir = Join-Path $repoRoot "artifacts/publish/win-x64"
$releaseDir = Join-Path $repoRoot "release"
$zipFileName = "AgentDeskApp-v$Version-win-x64.zip"
$zipPath = Join-Path $releaseDir $zipFileName

Write-Host "=== AgentDeskApp Release ビルドを開始します ===" -ForegroundColor Cyan
Write-Host "プロジェクト: $csprojPath"
Write-Host "publish出力先: $publishDir"
Write-Host "zip出力先    : $zipPath"

if (-not (Test-Path $csprojPath)) {
    Write-Error "プロジェクトファイルが見つかりません: $csprojPath"
    exit 1
}

# 既存のpublish出力があれば作り直すためクリーンアップする
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

# 自己完結型・シングルファイルでのpublishを実行する
dotnet publish $csprojPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish に失敗しました (終了コード: $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# 既存の同名zipがあれば上書きするため削除する
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "publish結果をzip化しています..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

if (-not (Test-Path $zipPath)) {
    Write-Error "zipファイルの生成に失敗しました: $zipPath"
    exit 1
}

Write-Host "=== 完了しました ===" -ForegroundColor Green
Write-Host "配布用zip: $zipPath"
