# 仮装狂騒曲 篠澤広

## ビームライト

共通の [材質同期手順](../../references/beam-material-sync.md) と [対応表](../../references/beam-sync-kasou.json) を使う。変換処理は `../../scripts/` が正式なソース。

## 床ライト

| 内容 | ファイル |
| --- | --- |
| 355〜498Fの長いフェードへの修正 | [correct-floor-long-fade.py](scripts/correct-floor-long-fade.py) |
| 同修正の計画・元ファイルのハッシュ | [floor-light-long-fade-plan.json](plans/floor-light-long-fade-plan.json) |
| 355〜498Fの動画再確認・修正記録 | [floor-light-orange-355-498.md](docs/floor-light-orange-355-498.md) |
| 前の区間のオレンジ発光パターン | [記録](docs/floor-light-orange-pattern.md)、[設定](plans/floor-light-orange-pattern.json) |
| 動画との位置・回転比較 | [floor-light-video-matching.md](docs/floor-light-video-matching.md) |
| 比較画像 | [images/](images/) |

長いフェード修正の引数は `対象XML 編集前バックアップXML 計画JSON [--apply]`。計画に保存された原本・バックアップのハッシュと一致する状態を対象にする、シーン固有の修正スクリプト。過去の計画を現在のXMLへそのまま再適用しない。

```powershell
python tools/timeline/scenes/kasou-hiro/scripts/correct-floor-long-fade.py --help
```

355〜498F向けの「4フレーム点灯・4フレーム消灯・4フレーム休止」は撤回済み。[旧スクリプトと設定](../../archive/floor-355-498-withdrawn/) は経緯の参照用として保管している。

生成物・試作は `tools/timeline/work/kasou-hiro/<task-id>/` に置く。
