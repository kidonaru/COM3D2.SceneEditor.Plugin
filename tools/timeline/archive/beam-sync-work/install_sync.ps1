param(
    [Parameter(Mandatory=$true)][string]$TargetSkill,
    [Parameter(Mandatory=$true)][string]$ExpectedSkillHash
)
$ErrorActionPreference = 'Stop'
$targetRoot = (Resolve-Path -LiteralPath $TargetSkill).Path
$skillPath = Join-Path $targetRoot 'SKILL.md'
if ((Get-FileHash -LiteralPath $skillPath -Algorithm SHA256).Hash -ne $ExpectedSkillHash) {
    throw '既存スキルが更新されたためインストールを中止します'
}
$entries = @(
    @{ Source='sync_beam_materials.py'; Target='scripts\sync_beam_materials.py' },
    @{ Source='test_sync_beam_materials.py'; Target='scripts\test_sync_beam_materials.py' },
    @{ Source='beam-material-sync.md'; Target='references\beam-material-sync.md' },
    @{ Source='beam-sync-kasou.json'; Target='references\beam-sync-kasou.json' }
)
foreach ($entry in $entries) {
    $destination = Join-Path $targetRoot $entry.Target
    if (Test-Path -LiteralPath $destination) { throw ('インストール先に既存ファイルがあります: ' + $destination) }
}
$backupPath = $skillPath + '.bak_beam_sync_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fffffff')
Copy-Item -LiteralPath $skillPath -Destination $backupPath
foreach ($entry in $entries) {
    $sourcePath = Join-Path $PSScriptRoot $entry.Source
    $destination = Join-Path $targetRoot $entry.Target
    Copy-Item -LiteralPath $sourcePath -Destination $destination
    if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
        throw ('保存後のハッシュが一致しません: ' + $destination)
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SKILL.md') -Destination $skillPath
if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'SKILL.md')).Hash -ne (Get-FileHash -LiteralPath $skillPath).Hash) {
    throw 'スキル説明の保存後ハッシュが一致しません'
}
Write-Output ('スキル更新完了: ' + $skillPath)
Write-Output ('スキルのバックアップ: ' + $backupPath)
