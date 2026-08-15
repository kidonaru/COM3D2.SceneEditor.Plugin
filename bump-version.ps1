<#
.SYNOPSIS
    プラグインのバージョンを更新する。bump-version.bat から呼ばれる。

.DESCRIPTION
    以下を一括で書き換える。
      - source\COM3D2.SceneEditor.Plugin\PluginInfo.cs の PluginVersion
      - CHANGELOG.md の見出し（見出しのみ。内容は手で書く）
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('major', 'minor', 'patch', 'build')]
    [string]$Part
)

$ErrorActionPreference = 'Stop'

$repoDir = $PSScriptRoot
$pluginInfoPath = Join-Path $repoDir 'source\COM3D2.SceneEditor.Plugin\PluginInfo.cs'
$changelogPath = Join-Path $repoDir 'CHANGELOG.md'

# ファイルは UTF-8 (BOM なし) / CRLF なので、読み書きで維持する
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Read-TextFile([string]$path) {
    if (-not (Test-Path $path)) { throw "ファイルが見つかりません: $path" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}
function Write-TextFile([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

# ============ 現在のバージョンを取得 ============
$pluginInfo = Read-TextFile $pluginInfoPath
$versionPattern = 'PluginVersion\s*=\s*"(\d+)\.(\d+)\.(\d+)\.(\d+)"'
$versionMatch = [regex]::Match($pluginInfo, $versionPattern)
if (-not $versionMatch.Success) {
    throw "PluginInfo.cs から PluginVersion を読み取れませんでした: $pluginInfoPath"
}

$major = [int]$versionMatch.Groups[1].Value
$minor = [int]$versionMatch.Groups[2].Value
$patch = [int]$versionMatch.Groups[3].Value
$build = [int]$versionMatch.Groups[4].Value
$oldVersion = "$major.$minor.$patch.$build"

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0; $build = 0 }
    'minor' { $minor++; $patch = 0; $build = 0 }
    'patch' { $patch++; $build = 0 }
    'build' { $build++ }
}
$newVersion = "$major.$minor.$patch.$build"

$date = Get-Date -Format 'yyyy/MM/dd'

Write-Host "バージョン: $oldVersion -> $newVersion ($date)"

# ============ 書き換え内容を組み立てる ============
# 片方だけ書き換わった中途半端な状態にならないよう、両方の検証が通ってから書き込む

# 置換は先頭 1 件だけに限定したいので、件数を指定できる Regex インスタンスの Replace を使う
# (-replace 演算子や [regex]::Replace の静的版には件数指定が無い)
$newPluginInfo = (New-Object regex $versionPattern).Replace($pluginInfo, "PluginVersion = `"$newVersion`"", 1)

$changelog = Read-TextFile $changelogPath

$titlePattern = '(?m)^# 変更履歴[ \t]*\r?$'
$titleMatch = [regex]::Match($changelog, $titlePattern)
if (-not $titleMatch.Success) {
    throw "CHANGELOG.md に見出し (# 変更履歴) が見つかりませんでした"
}

$entryPattern = '(?m)^## \d{4}/\d{2}/\d{2} v([\d.]+)[ \t]*\r?$'
$entryRegex = New-Object regex $entryPattern

$duplicated = $entryRegex.Matches($changelog) | Where-Object { $_.Groups[1].Value -eq $newVersion }
if ($duplicated) {
    throw "CHANGELOG.md に v$newVersion のエントリが既にあります。二重実行の可能性があるため中止します"
}

# 既存の先頭エントリの上に挿入する。エントリがまだ無い場合は見出し行の次の行に置く
$entryMatch = $entryRegex.Match($changelog, $titleMatch.Index)
$blankLineBeforeEntry = ''
if ($entryMatch.Success) {
    $insertIndex = $entryMatch.Index
}
else {
    $afterTitle = $changelog.IndexOf("`n", $titleMatch.Index)
    if ($afterTitle -lt 0) { throw "CHANGELOG.md の見出しの後に改行がありません" }
    $insertIndex = $afterTitle + 1
    # 見出しとの間に空行を入れる
    $blankLineBeforeEntry = "`r`n"
}
$entry = "$blankLineBeforeEntry## $date v$newVersion`r`n`r`n- TODO: 変更内容を記載`r`n`r`n"
$changelog = $changelog.Insert($insertIndex, $entry)

# ============ 書き込み ============
# CHANGELOG の書き込みに失敗したら PluginInfo.cs を元に戻す (バージョンだけ進んだ状態を残さない)
Write-TextFile $pluginInfoPath $newPluginInfo
try {
    Write-TextFile $changelogPath $changelog
}
catch {
    Write-TextFile $pluginInfoPath $pluginInfo
    throw
}
Write-Host "  更新: source\COM3D2.SceneEditor.Plugin\PluginInfo.cs"
Write-Host "  更新: CHANGELOG.md"

Write-Host ''
Write-Host "CHANGELOG.md に『TODO: 変更内容を記載』を挿入しました。内容を書いてから deploy.bat を実行してください"
