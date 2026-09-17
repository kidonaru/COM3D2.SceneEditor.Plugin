# レイヤー削除時のシーン状態復元 設計

作成日: 2026-09-13
関連: `docs/timeline-release-debt-review.md` 項目 14、`docs/superpowers/specs/2026-09-13-timeline-release-debt-medium-design.md` §14

## 背景

タイムラインレイヤーは生存中、毎 `LateUpdate` で SE 側の実体（ライト・モデル Transform・マテリアル・背景色など）を上書きし続ける。現状の `TimelineManager.RemoveLayer`（`Timeline/Manager/TimelineManager.cs:1701`）はイベント購読解除とレイヤー除去だけを行い、シーン側の値には触らない。

結果として、**レイヤーを削除するとそのとき表示していたフレームの値がシーンに焼き付いたまま残る**。利用者から見ると「レイヤーを消したのに見た目が戻らない」状態になる。

項目 14 で決めた「レイヤー削除はシーン上の**実体**を壊さない」は維持したまま、**値だけを削除前の基準へ戻す**のが本設計の目的。

## 決定事項

| 論点 | 決定 |
|---|---|
| 復元の基準 | **レイヤーが生まれた時点**のシーン状態。タイムライン読込時に存在したレイヤーは読込直前、後から追加したレイヤーは追加直前 |
| 対象外レイヤー | `MotionTimelineLayer`（そもそも削除不可）、`VoiceTimelineLayer` / `SeTimelineLayer`（適用が再生トリガになる）、`DressTimelineLayer`（適用が `SetProp` → `AllProcPropSeqStart` の非同期処理を起こす） |
| 実体（個数） | 復元対象外。ライト・モデル・サブカメラ・テキストの台数はレイヤーのキーデータに含まれず、SE 側マネージャまたは `TimelineData` の設定が持つ |
| redo（Ctrl+Y での再削除） | 今回は対象外 |

## 方式

全レイヤーは `ITimelineLayer`（`Timeline/TimelineLayer/ITimelineLayer.cs`）に次を public で持つ。レイヤー個別のコードを足さずに退避・復元を組める。

| メソッド | 役割 |
|---|---|
| `UpdateFrame(FrameData)` | **シーンの現在値を読んで** FrameData に書き出す |
| `CreateFrame(int)` / `CreateFrame(FrameXml)` | フレーム生成 |
| `FromXml(TimelineLayerXml)` | キーフレーム列を丸ごと差し替える |
| `CreateAndApplyAnm()` | キーから anm を組み直してシーンへ適用する |

`UpdateFrame` が読む範囲とレイヤーが書き換えうる範囲は一致している（`LightTimelineLayer.cs:243` は全ライト、`ModelTimelineLayer.cs:170` は全モデル、追跡系の `ModelBone` / `ModelShapeKey` / 各マテリアルは `allBoneNames` = 追跡対象のみ）。`UpdateFrame` の `force` 引数を見るのは `DressTimelineLayer.cs:191` だけで、対象外レイヤーなので既定値でよい。

### 退避（誕生時）

レイヤーの `Init()` 直後・**まだ適用前**の一点で 1 キー分の断面を取り、`FrameXml` として保持する。

```
var frame = layer.CreateFrame(0);
layer.UpdateFrame(frame);
store.Set(layer.layerType, layer.slotNo, frame.ToXml());
```

退避を仕掛ける箇所:

| 経路 | 位置 |
|---|---|
| `TimelineManager.LoadTimeline` | `:426 _timeline.LayerInit()` の直後、`:432 CreateAndApplyAnmAll()` の前 |
| `TimelineManager.CreateNewTimeline` | `:367 _timeline.LayerInit()` と `:369 CreateAndApplyAnmAll()` の間 |
| `TimelineManager.ChangeActiveLayer` | `:1680 newLayer.Init()` と `:1681 newLayer.CreateAndApplyAnm()` の間 |

`UpdateTimeline`（`:488`、undo/redo）は退避しない。この経路が走る時点でシーンはすでにタイムラインに駆動されており、そこで取った断面は基準として誤りになるため。

### 退避（追跡系レイヤーの追加分）

`ModelBoneTimelineLayer` / `ModelShapeKeyTimelineLayer` / `ModelMaterialTimelineLayer` / `MaidMaterialTimelineLayer` / `MorphTimelineLayer` の 5 つは `TimelineLayerBaseTracking` を使い、`allBoneNames` が `trackedBoneNames`（= チェック済み ∪ 既存キーフレーム記載、`TimelineLayerBaseTracking.cs:50-72`）になる。**書き換える範囲がレイヤーの生存中に増えていく**ため、誕生時の断面だけでは足りない。

- レイヤー追加直後は `trackedBoneNames` が空のことがあり、誕生時の断面が 0 ボーンになる
- あとからチェックした項目は断面に含まれず、削除しても削除時点の値のまま凍結される

そこで**断面をボーン単位で積み増す**。項目が新たに追跡対象へ入る瞬間、その項目はまだこのレイヤーに駆動されていないので、そのときの値が正しい基準になる。

積み増しの位置は `TimelineLayerBaseTracking.AddTrackedFirstFrameKeys`（`:168`）。ここは「新たに対象へ入った項目に 0F キーを打つ」処理で、すでに `UpdateFrame(force: true)` でライブ値を読んだ `tmpFrame` を持っている。キーを打つ前にこのフレームを退避へ渡す。

積み増しは**ボーン名ごとに先勝ち**にする。すでに断面にある項目は誕生時の値を保ち、渡された `boneNames` のうち未記録のものだけ足す。これにより:

- 誕生時に取れなかった項目も、駆動が始まる直前の値で基準を持てる
- チェックを外して付け直しても、最初に記録した基準が残る
- undo/redo で生えたレイヤー（誕生時の断面が無い）でも、新規にチェックした項目だけは正しい基準を持てる

レイヤーを削除して同じ種類を追加し直したときは、誕生時の退避が**置き換え**で走るため基準が取り直される。

### 復元

`RemoveLayer` で `layer.Dispose()` の**前**に行う。`Dispose` は `_keyFrames` と `_dummyLastFrame` を捨てる（`TimelineLayerBase.cs:231-235`）ので、後に回すと `isInitialized` が false になり `GetAnmBinary` が null を返す。

```
var xml = new TimelineLayerXml();
xml.keyFrames.Add(baselineFrameXml);
layer.FromXml(xml);
layer.CreateAndApplyAnm();
```

`FromXml`（`TimelineLayerBase.cs:1374-1383`）は `keyFrames` しか読まない。`className` / `slotNo` は無視されるので設定しない。

キーが 1 本でも `_dummyLastFrame` が末尾行として積まれる（`TimelineLayerBase.cs:457`）ため `BuildPlayData`（`:375`）は区間を 1 本作り、現在フレームがどこでも定数値として適用される。`CreateAndApplyAnm` は内部で `ApplyAnm` → `ApplyPlayData` まで到達するので、適用呼び出しはこれ 1 つでよい。

### 退避データの持ち方

`(layerType, slotNo)` をキーにした辞書を `TimelineManager` が持つ。

- **レイヤーインスタンスには持たせない**。`UpdateTimeline`（`:488-513`）が `ClearTimeline()` → `FromXml` → `LayerInit` を通すため、undo/redo のたびにレイヤーは別インスタンスになる
- `ResetTimelineState`（`:324`）でクリアする。読込・新規作成・アンロードのたびに基準は作り直す
- 削除時にエントリは消さない。undo でレイヤーが復活し再び削除されたとき、同じ基準へ戻せるようにする
- undo/redo で生えたレイヤーに退避が無い場合は復元をスキップする（`MTEUtils.LogDebug` に留める）

## 既知の制限（仕様として受け入れる）

- **redo での再削除は復元しない**。`UpdateTimeline` は `RemoveLayer` を通らない。undo 側は「レイヤー復活 → 再適用」で自然に上書きされるため非対称は片側のみ
- **サブカメラの基準がわずかにずれうる**。`SubCameraTimelineLayer.Init`（`:34`）が `subCameraManager.SetupCameras()` を呼ぶため、「Init 後の断面」は読込直前と厳密には一致しない可能性がある
- **カメラレイヤーはカメラ同期 OFF だと適用されない**。`CameraTimelineLayer` の適用経路が同期トグルに従うため、復元も同じ条件に従う
- **追跡系レイヤーで途中チェックを外した項目も復元される**。`BuildTimelineBonesMap`（`TimelineLayerBase.cs:428`）は `allBoneNames` で絞らずキーフレームの内容だけを見るため、断面に残っている項目はすべて書き戻る。レイヤーが一度でも駆動した項目を戻す挙動なので意図どおり
- 復元対象外の 4 レイヤーは従来どおり「削除しても何も起きない」

## テスト方針

ユニットテスト（`source/COM3D2.SceneEditor.Plugin.Tests`、xunit / net48）で検証できるのは Unity 実体に触れない純粋ロジックのみ。

- 復元可否の判定（対象外レイヤー型の一覧）
- 退避ストアの入出力（キー分離・上書き・クリア）

実際の適用は実機（MCP `com3d25-devbridge`）で確認する。
