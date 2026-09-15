# 133のステージライト円錐を太くする

## 背景

ステージライトの描画がビルボードから視線積分の体積円錐に置き換わった（`2a5f7a6`、`691aaea`）。円錐の直径は旧ビルボードの幅と同じ `2·tan(spotAngle/2)·range` だが、シェーダが `coreRadius` から外側へ向けて減衰するため、既定の coreRadius 0.2 では半分の明るさになる位置が直径の約 60% になり、同じ spotAngle でも細く見える。

## 変更

`scripts/thicken-stage-light.py` で StageLightTimelineLayer の全キーを変更した。

| 項目 | 変更 |
| --- | --- |
| spotAngle（StageLight 添字 12、Controller 添字 21） | 1.3 倍。上限 179 |
| coreRadius（StageLight 添字 18、Controller 添字 27） | `max(既存値, 0.5)`。グループ 0 の 0.68〜0.87 は保持 |
| その他の値・構造 | 変更なし（再読して全要素を照合） |

対象 Transform: StageLight 989 件、StageLightController 45 件。

倍率と下限は引数で変えられる。効果を強めたい場合は `--angle-scale` を上げるか `--core-radius-min` を上げる。

```powershell
python tools/timeline/scenes/kasou-hiro/scripts/thicken-stage-light.py 対象.xml --angle-scale 1.3 --core-radius-min 0.5 --sha256 <原本SHA256> --apply
```

## バックアップとハッシュ

原本と同じフォルダー:
`仮装狂騒曲 篠澤広133.xml.stagelight-thicken-backup-20260911-232929-472971`

- 元SHA-256: `822c10cdba91bfa26eb1758502e919ca65d2626d59790b22915510b127b753eb`
- 反映後SHA-256: `4d882e10ed7ed459c4e6881502bf9caf9fdf12bb0f164510435ddae5bc02cf1a`

## 検証

- スクリプト内で再読し、対象外の要素・値・構造に差分がないことを確認済み
- ゲーム内での見た目確認は未実施。倍率は見た目を見て再調整する前提
