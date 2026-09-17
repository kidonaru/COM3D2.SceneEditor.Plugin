# レイヤー削除時の後始末を「復元」と「初期化」に分ける 設計メモ

`2026-09-13-layer-remove-restore-design.md` の続き。実装後の実機確認で方針を変えた分の記録。

## 背景

先行実装では、全レイヤーを「誕生時の断面へ値を戻す」で統一した。実機確認で次が分かった。

- 復元の適用経路そのものは正常（断面がある状態で削除すると値は戻る。ステージライトの `positionMin` が `(3,1,2)` → `(-5,10,0)` に戻ることを devbridge で確認済み）
- ただしモデル・ステージライト・サイリウムのように**実体をレイヤーより後から足せる**レイヤーは、誕生時の断面に 1 本も入らないため何も戻らなかった

対象数の増減を毎フレーム見て積み増す汎用フック（コミット `e9977d7`）でこれは塞いだが、ユーザー判断として**実体を持つレイヤーは値を戻すのではなく実体ごと初期化する**方針に変更する。

## 決定事項

後始末を直交する 2 つに分ける。`TimelineManager` は常に `layer.ResetOnRemove()` を呼び、そのあと宣言が復元対象なら断面を戻す。

- **実体の後始末** — レイヤー自身の `ResetOnRemove()`。どのマネージャを触るかはレイヤーが一番よく知っているため、レイヤーに置く
- **値の復元** — `TimelineLayerDescAttribute.CanRestoreOnRemove`（既存のまま）。誕生時の断面を控える対象かどうかも兼ねる

新しい列挙は作らない。2 つが直交していれば `ライト` のような「追加分は消す・残る実体は値を戻す」も自然に表せる。

| レイヤー | `ResetOnRemove` | `CanRestoreOnRemove` |
|---|---|---|
| `ステージライト` / `ステージレーザー` / `サイリウム` / `モデル` / `PNG配置` / `テキスト` / `サブカメラ` | 実体を破棄する | false |
| `ライト` | 追加ライトだけ破棄する | **true**（メインライトの値が戻る） |
| `モデルボーン` / `モデルシェイプ` / `モデルマテリアル` | 既定値へ戻す | false |
| メイド系 / `カメラ` / `背景` / `背景色` / `背景モデル` / `背景モデルマテリアル` / `ポストエフェクト` | なし | true |
| `メイドアニメ` / `メイドボイス` / `効果音` / `メイド衣装` | なし | false |

適用する契機は **レイヤー削除・アンロード・別タイムラインの読込・新規作成**（`RemoveLayer` と `ResetTimelineState`）。undo/redo の `UpdateTimeline` とシーン切替の `OnChangedSceneLevel` は対象外（現行と同じ）。

## 実体の破棄方法

各マネージャは「タイムライン側のデータリスト」から実体を再構成する `Setup*` を持つ。リストを空にして `Setup*` を呼ぶと実体が消える。

| レイヤー | データ | 破棄の呼び出し |
|---|---|---|
| `ステージライト` | `timeline.stageLightCountList` | `stageLightManager.SetupLights(空)` |
| `ステージレーザー` | `timeline.stageLaserCountList` | `stageLaserManager.SetupLasers(空)` |
| `サイリウム` | `timeline.psylliums` | `psylliumManager.Setup(空)` |
| `モデル` | `timeline.models` | `modelManager.SetupModels(空)` |
| `ライト` | `timeline.lights` | `lightManager.SetupLights(先頭 1 件だけ残したリスト)` |
| `PNG配置` | `timeline.pngObjects` | `pngObjectManager.Setup(空)` |
| `テキスト` | `timeline.textCount` | `textManager.ReleaseTexts()` |
| `サブカメラ` | なし | `subCameraManager.SetCameraCount(MinSubCameraCount)` |

### ライトをメインライトだけ残す理由

`StudioLightManager` は `index 0 = メインライト` を不変条件にしていて（`BuildLightList`）、メインライトはゲーム側の恒久オブジェクトなので削除も再生成もされない。`SetupLights` は `index >= 渡したリスト数` のライトを消すので、**先頭 1 件だけ残したリストを渡せば追加ライトだけが消える**。空リストを渡すとメインライトまで `RemoveLight` に回るため渡さない。

メインライトはこの経路では消せないぶん、キーで書き換えた色・位置が残ってしまう。そこで `ライト` だけは `CanRestoreOnRemove = true` のままにして、追加ライトを消したあとに断面復元でメインライトの値を戻す。

### サブカメラが最小数までしか減らない理由

`SubCameraManager` は `MinSubCameraCount` を下回れない（`SetCameraCount` が `Mathf.Clamp` する）。最小数まで減らすところまでを「初期化」とする。

## 従属レイヤーの既定値

実体をロードした時点の値がコード上に残っているので、それを書き戻す。

| レイヤー | 既定値の出どころ |
|---|---|
| `モデルボーン` | `ModelBoneController.initialPosition` / `initialRotation` / `initialScale`（ボーン読込時に控えている） |
| `モデルシェイプ` | `ModelBlendShape.weight = 0`（`BlendShapeController.Init` が weight 0 で作る）→ `FixBlendValues()` |
| `モデルマテリアル` | `ModelMaterial.Reset()`（`GetInitialColor` / `GetInitialValue` へ戻す既存メソッド） |

## 背景モデルを実体破棄の対象にしない理由

`BGModelManager.AddModel` も実体を後から足せるが、足すのは**背景シーンに元からある子オブジェクトの登録**（group > 0 のときだけ複製を作る）である。プラグインが一から作った実体ではないので、消すと背景そのものを壊す。`背景モデル` / `背景モデルマテリアル` は値のみレイヤーのままにする。

## 断面復元を残す理由

値のみレイヤーには実体の破棄という選択肢が無い（メイドやカメラを消すわけにいかない）ため、現行の断面復元をそのまま使う。`メイド表情` / `メイドマテリアル` / `メイドシェイプ` は対象がチェックで増えるので、`e9977d7` の積み増しも引き続き必要。

## 非対象

- 実体を破棄したあとの undo で実体が戻るかは `RequestHistory` 任せ（タイムラインデータごと戻る想定）。実機で確認する
- `LiveEffectSnapshot` による「値も含めた完全復元」は使わない。初期化で十分という判断
