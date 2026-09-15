# タイムラインツール

タイムラインXMLの変換・検証と、シーン別の編集資料をここに集約する。作業前に [AGENTS.md](AGENTS.md) を確認する。

## 現行ツール

| 目的 | ファイル | 手順 |
| --- | --- | --- |
| レーザーの色・明るさをビーム材質へ同期 | [sync_beam_materials.py](scripts/sync_beam_materials.py) | [材質同期](references/beam-material-sync.md) |
| ビームのY軸回転・レーザー原点XZの同期 | [sync_beam_pose.py](scripts/sync_beam_pose.py) | 同じ同期手順の「原点XZとモデルのY軸回転」 |
| 補間キー生成の共通処理 | [beam_curves.py](scripts/beam_curves.py) | 上記2スクリプトから利用 |
| XML・モデル登録の読み取り専用検証 | [inspect_timeline.py](scripts/inspect_timeline.py) | `--help` |
| レーザーオフセットの編集 | [set_laser_offset.py](scripts/set_laser_offset.py) | 引数・対象を実装で確認する補助ツール |

リポジトリルートからの読み取り専用実行例:

```powershell
python tools/timeline/scripts/sync_beam_materials.py '対象.xml' --config tools/timeline/references/beam-sync-kasou.json
python tools/timeline/scripts/sync_beam_pose.py '対象.xml' --config tools/timeline/references/beam-sync-kasou.json
python tools/timeline/scripts/inspect_timeline.py '対象.xml'
python -m unittest discover -s tools/timeline/scripts -p 'test_*.py'
```

`beam-sync-kasou.json` は「仮装狂騒曲 篠澤広」の床レーザー4系統用。反映前に設定と検証結果を確認する。スキル側の同名対応表とは設定値が異なる場合がある。

## シーンと作業履歴

- [仮装狂騒曲 篠澤広](scenes/kasou-hiro/README.md): 床ライトの現行スクリプト、設定、動画比較記録、参照画像、ステージライトの再現手順。
- [archive](archive/README.md): 旧 `beam-XXX-work` 一式と撤回した床ライト設定。現行ツールとして実行しない。
- [work](work/README.md): 生成XML・検証結果・一時作業の置き場。通常のソース検索とGit管理から除外する。

2026-09-09にルート直置きと旧作業フォルダーを整理した。旧ファイルは削除せず、用途ごとの場所へ移動している。
