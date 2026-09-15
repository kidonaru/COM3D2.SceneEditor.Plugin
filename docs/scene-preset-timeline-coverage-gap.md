# タイムライン対応済み / シーンプリセット未対応の機能 調査資料

作成: 2026-09-15。タイムライン（`TimelineLayer`）では扱えるがシーンプリセット（`ScenePresetData` / `ScenePresetManager`）では保存・復元できない機能を洗い出した資料。

パスは `source/COM3D2.SceneEditor.Plugin/` 配下を `<root>` と略記。行番号は 2026-09-15 時点。

関連: `scene-preset-mte-state-survey.md`（MTE 由来状態のプリセット保存、2026-08-30）。本資料はその続きで、テキスト・サブカメラ・動画・BGM の対応完了後に残った差分を扱う。

## 1. 突き合わせの方法

- タイムライン側: `<root>/Timeline/TimelineLayer/*.cs` の `[TimelineLayerDesc(...)]` 全 30 レイヤー。
- プリセット側: `<root>/ScenePresetData.cs`（ルート `ScenePresetData` :902、`CurrentVersion = 34` :975）と、保存対象の集約点 `<root>/Manager/ScenePresetManager.cs` の `Capture` :707。
- 外部プラグイン提供分は `ScenePresetProviderRegistry`（`<root>/Manager/ScenePresetProviderRegistry.cs`）経由で `externals` に載るため、本リポジトリ内に実装が無くても対応済みとして扱う。

## 2. 未対応一覧

| 機能 | レイヤー（ソート順） | プリセット側の状況 |
|---|---|---|
| メイド衣装 | `DressTimelineLayer`（15） | 未対応 |
| メイドアニメブレンド | `AnimationTimelineLayer`（1） | 未対応 |

### 2-1. ライブ演出 3 種（ステージライト / ステージレーザー / サイリウム）

**対応済み (v34, 2026-09-15)**。`ScenePresetEffects.liveEffect` に Undo 用の `LiveEffectState` をそのまま載せ、`LiveEffectSnapshot.CaptureLiveEffect` / `ApplyLiveEffect` を `MteEffectsSnapshot` から呼ぶ。「演出」カテゴリに属する。あわせてサイリウムのメッシュ配置 (`placements`) を DTO に追加した（Undo でも保持されるようになった）。

### 2-2. 背景モデル

**対応済み (v34, 2026-09-15)**。`ScenePresetData.bgModels` (`ScenePresetBgModels` に包んだ `ScenePresetBgModel`: sourceName / group / visible / ローカル transform) を「背景」カテゴリで保存し、`BgModelSnapshot` が `BGModelManager.SyncToCurrentBg()` → `SetupModels` → 値の書き戻しの順で復元する。適用は `FinishApply` の `ApplyModelAppearances` 内、`ApplyBgMaterials` の直前。保存時と違う背景へ適用した場合、見つからないモデルは警告のみで飛ばす。

### 2-3. メイド衣装

`ScenePresetMaid` が持つ衣装関連は `undress`（`ScenePresetData.cs:651`、脱衣スロットと衣装種別のみ）だけで、**どの menu を装備しているかは保存しない**。プリセットは「呼び出し済みメイドの見た目は既にできている」前提のスキーマになっている。

`DressTimelineLayer`（`<root>/Timeline/TimelineLayer/DressTimelineLayer.cs`）は `MaidPartUtils.equippableMaidPartTypes` の各部位に対してキーを持てるので、装備そのものの差し替えができる。

### 2-4. メイドアニメブレンド

`AnimationTimelineLayer`（`<root>/Timeline/TimelineLayer/AnimationTimelineLayer.cs`）はアニメレイヤー 2〜8（`MinLayerIndex` / `MaxLayerIndex` :21-22）への多重アニメ再生を扱う。プリセットは `ScenePresetMaid.motion` / `poseAnmFile` / `poseAnmBinary`（`ScenePresetData.cs:619-630`）の単一モーションだけで、ブレンドレイヤーの概念が無い。

## 3. 対象外と判断したもの

| 機能 | レイヤー | 理由 |
|---|---|---|
| メイドボイス | `VoiceTimelineLayer`（14） | 「そのフレームでボイスを再生する」イベントであって、静的断面であるプリセットに載る状態ではない |
| 地面（BGGround） | （レイヤーではない） | `BGGroundManager` は `TimelineData` 側のタイムライン設定に紐づく。レイヤー単位の機能差分とは別枠 |

### 判断保留: 効果音 (SE)

`SeTimelineLayer`（51）は `TimelineSeManager.PlaySe` をキーで叩く**再生イベント**であり、ボイスと同じ扱いが自然。一方 `TimelineSeManager` は `currentSeName` / `currentInterval` / `currentIsLoop`（`<root>/Timeline/Manager/TimelineSeManager.cs:80-82`）を持ち、ループ再生中の SE は「シーンの状態」とも言える。プリセットに載せるかは仕様判断が必要。

## 4. 対応済みの確認（参考）

カメラ / サブカメラ / 背景 / 背景色 / ライト / PNG 配置 / テキスト / 動画 / BGM、メイドの移動・表情・シェイプ・マテリアル・瞳（視線）・重力・脱衣・ボーン編集・IK・指、モデル系（ボーン・シェイプ・マテリアル。モデル本体は `ModelProviderHost` + 外部プロバイダが復元）、ライブ演出（ステージライト・レーザー・サイリウム）、背景モデル（表示・transform・複製）。

**ポストエフェクト**（`PostEffectTimelineLayer*`、52）は実体が外部の `PostEffects.Plugin` 側（`<root>/Timeline/Manager/PostEffectsBridge.cs` → `PostEffectsClient`）にあり、プリセット保存も同プラグインの `ScenePresetProvider` 実装が `externals` 経由で担当する。本リポジトリ内に保存コードが無いのは正常。

## 5. 着手コストの目安

1. **メイド衣装 / アニメブレンド** — 大。メイドの見た目・モーション適用の非同期経路（`UpdatePendingApplies`）に手を入れる必要があり、既存の適用順序と衝突しやすい。
