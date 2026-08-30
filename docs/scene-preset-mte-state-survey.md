# MTE 由来状態のシーンプリセット保存 調査資料

作成: 2026-08-30。MTE (MotionTimelineEditor) から移植した機能（テキスト・サブカメラ・ポストエフェクト等）を SceneEditor のシーンプリセットで保存・復元できるようにするための現状調査。

**決定済み方針 (2026-08-30, ユーザー判断)**: **タイムライン未読込のシーンでもテキスト等を復元できるようにする**。要素数と実体生成を `TimelineData` から切り離す改修を行う。なお 2026-08-23 の「シーンプリセット連携はスコープ外」決定は本方針で撤回。

パスは `source/COM3D2.SceneEditor.Plugin/` 配下を `<root>` と略記。行番号は 2026-08-30 時点。

## 1. シーンプリセットの保存/読込アーキテクチャ

- スキーマ: `<root>/ScenePresetData.cs` — DTO 群（`XmlAttribute`/`XmlElement`）。ルート `ScenePresetData` (:677-797)、現行 `CurrentVersion = 28` (:745)。:678-744 にバージョン履歴コメントを集約。
- 本体: `<root>/Manager/ScenePresetManager.cs`
  - `SavePreset` :478 / `LoadPreset` :528 / `Capture` :698（保存対象の集約点）
  - 適用は pending キュー方式で非同期（`UpdatePendingApplies` :1607、メイドロード完了待ち）
- フォーマット: `XmlSerializer` XML + サムネ PNG。外部プロバイダのペイロードはサイドカーファイル（`<プリセット名>.<プロバイダid>.<ext>`）に外出しし、本体には `ScenePresetExternal { id, file }` のみ（`ScenePresetData.cs:658-674`）。プリセット名にドット禁止 (:452-471)。
- 保存オプション: `<root>/SavePresetPopupWindow.cs:11-19` `ScenePresetSaveOptions`（saveCamera / saveMaids / saveBackground / enabledProviderIds）。

### 拡張ポイント（新しい保存対象の追加手段）

1. **本体スキーマ拡張**（同一アセンブリ内の状態向け・従来の方法）: `ScenePresetData` にフィールド追加 → `CurrentVersion` を上げてコメント追記 → `Capture` と apply/pending 系に処理追加。旧形式は「null / 未指定なら触らない」規約（v11〜v28 全て同じ流儀）。
2. **プロバイダ登録**（外部プラグイン向け）: `<root>/Manager/ScenePresetProviderRegistry.cs:46-57`。短名 `ScenePresetProviderAttribute` 一致で発見し、static メンバ `PresetProviderId` / `PresetProviderDisplayName` + Capture/Apply の Xml か Binary ペアを要求。

**→ MTE 移植部は同一アセンブリ内なので手段 1（スキーマ拡張、v29〜）が筋。**

## 2. テキスト機能の現状

- `<root>/Timeline/Manager/TimelineTextManager.cs`
  - `struct FreeTextSet { GameObject obj; Text text; RectTransform rect; }` :13-23 — **参照 3 つだけで値は持たない**。実体値の唯一の所有者は Unity コンポーネント（`UnityEngine.UI.Text` / `RectTransform`）。
  - `InitTexts()` :99-134 — **`timeline == null` なら即 return。配列長は `timeline.textCount`** (:101, :108)。
  - 専用 Canvas/Camera を `(0,-10000,0)` に生成（`CreateCanvasAndCamera` :221）。
- `<root>/Timeline/TimelineLayer/TextTimelineLayer.cs`
  - `LateUpdate` :62-79 — 配列長と `timeline.textCount` の差分で `InitTexts()` 再実行。**非編集モード中は `ApplyPlayData()` でキーフレーム値を毎フレーム書き戻す** (:76-78)。
- 永続化経路はキーフレーム（`TransformDataText` → TimelineXml）のみ。`textCount` は `TimelineData.cs:436` / `TimelineXml.cs:243`。
- UI: `<root>/TextWindow.cs`（timeline == null で使用不可 :113-118、編集モード中のみ操作可 :152-158）+ `<root>/TextRowDrawer.cs`（TextItemInspector と共用）。

**timeline == null で使えない理由**: ①`textCount` が `TimelineData` のフィールド ②値の復元経路が `TextTimelineLayer.ApplyPlayData` しかない。

## 3. サブカメラの現状

- `<root>/Timeline/Manager/SubCameraManager.cs`
  - `SubCameraData` :99-198 — `position`/`rotation`/`visible` は Camera 実体の getter/setter で影の状態なし。
  - **台数は timeline 外**: `_subCameras.Count` が真実（TimelineXml に subCameraCount は無い）。`OnPluginDisable` で `_restoreCameraCount` に退避 → `OnLoad` で復元 (:247-261)。**timeline 非依存の要素数保持の既存参考実装**。
- `<root>/Timeline/TimelineLayer/SubCameraTimelineLayer.cs` :34-56 — `Init` で `SetupCameras()`、**`Dispose` で `DestroyAllCameras()`（レイヤーを外すとカメラごと消える）**。
- 姿勢・viewport の永続化はキーフレーム（`TransformDataSubCamera`）経由のみ。
- UI: `<root>/CameraWindow.cs:596-670`（サブカメラタブ）+ `<root>/SubCameraRowDrawer.cs`。

## 4. ポストエフェクト等（TimelineSettingWindow の「要素数」残り）

`<root>/TimelineSettingWindow.cs:313-327` `DrawElementCountSection` に残るのは:

| 項目 | フィールド | 定義 |
|---|---|---|
| パラフィン数 | `timeline.paraffinCount` | `TimelineData.cs:440` / `TimelineXml.cs:255` |
| 距離フォグ数 | `timeline.distanceFogCount` | `TimelineData.cs:441` / `TimelineXml.cs:258` |
| リムライト数 | `timeline.rimlightCount` | `TimelineData.cs:442` / `TimelineXml.cs:261` |

- 実体は `<root>/Timeline/Manager/PostEffectManager.cs` 経由で Unity 側 `PostEffectController` / 各 `*EffectSettings`（マネージャは影の状態を持たず get/set のみ）。`InitPostEffects()` :127-182 が `timeline.*Count` に合わせて Add/Remove。呼び出しは `PostEffectTimelineLayer.cs:70-91`（Update 内で差分検出）。
- ポストエフェクトの編集 UI は専用ウィンドウ未整備（`PostEffectItemInspector` + `PostEffectRowDrawer` のみ）。
- その他の MTE 由来 count/state: `usePostEffectExtraColor`/`usePostEffectExtraBlend` (`TimelineData.cs:438-439`)、`stageLaserCountList`/`stageLightCountList` (:420-421)、`fingerBlendEnabled` (:437)、アスペクト/レターボックス (:433-435)、動画 (:444-460)、BGM/BPM (:428-431)。

## 5. タイムライン非依存化の障害一覧

1. **timeline 直参照**: `ManagerBase.timeline`（`<root>/Timeline/Manager/ManagerBase.cs:15`）経由で `TimelineTextManager.InitTexts` / `PostEffectManager.InitPostEffects` / `TextTimelineLayer.LateUpdate` / `PostEffectTimelineLayer.Update` が `timeline.*Count` を直読み。→ 要素数の所有者を SE 側へ移すか切り離す必要。
2. **レイヤーの書き戻し**: 非編集モード中は `ApplyPlayData()` がキーフレーム値で毎フレーム上書き（`TextTimelineLayer.cs:76-78`、`PostEffectTimelineLayer.cs:87-90`）。プリセット復元直後に上書きされる。→ 復元は「レイヤー未追加なら素直に適用 / レイヤーありなら調停」等の分岐が要る。
3. **ライフサイクル結合**: `SubCameraTimelineLayer.Dispose` → `DestroyAllCameras()`、テキストも `ReleaseTexts()`（`TimelineTextManager.cs:136`）。タイムライン切替で復元済み状態が失われる。
4. **スナップショット型の不在**: `FreeTextSet`/`SubCameraData`/PostEffect は実体参照 or getter/setter のみ。**保存用 POCO（ScenePresetText / ScenePresetSubCamera / ScenePresetPostEffect 等）の新設が必要**（`TransformDataText` は MotionValue 配列ベースでそのまま流用は難しい）。
5. **要素数の所有者が不統一**: サブカメラ台数は timeline 外（`_restoreCameraCount`）、textCount / paraffinCount 等は timeline 内。書き戻し先が要素ごとに違う。
6. **適用タイミング**: プリセット適用は pending キュー（非同期）。MTE 側はマネージャ生成・レイヤー Init 待ちが要るため、同じ待ち枠に載せるか別の遅延が必要。

## 6. 既存の前例

**視線（v26〜v28）が直接の前例**:
- `ScenePresetData.cs:511-534` `ScenePresetLook.timelineTargetType` 等。「タイムライン未読込で保存したら null、適用時は触らない」規約 (:513-515)。
- capture `ScenePresetManager.cs:1262-1272`（MaidCache 直読み、null なら記録しない）/ apply :1785-1810（セッターが `UpdateLookAtTarget` を呼ぶため代入順重要）。
- 設計経緯: `docs/se-mte-state-duplication-survey.md` の B-6 / A-1c。

他の MTE 直参照前例: `ScenePresetManager.cs:896` StudioModelManager、:870/:945 ModelMaterialController、:1672 LateUpdate 強制更新、:2093-2151 maidCache のブレンドシェイプ/マテリアル。

`ModelPlacerProviderRegistry` は外部プラグインへのモデル配置委譲用で、プリセット連携の前例としては視線の方が近い。

## 7. 実装方針の当たり（未計画・次ステップ）

方針「タイムライン未読込でも復元可能」を前提に:

1. 要素数と実体生成を `TimelineData` から切り離す（所有者を SE 側マネージャへ移す。サブカメラの `_restoreCameraCount` 方式が参考）。timeline 読込時は TimelineXml の count と同期する調停が必要。
2. 値スナップショット用 POCO を新設し、`ScenePresetData` v29 としてスキーマ追加（Capture / pending apply 拡張、「未記録なら触らない」規約踏襲）。
3. レイヤー書き戻し・ライフサイクル結合（Dispose での実体破棄）との調停を設計する。
4. `SavePresetPopupWindow` の保存カテゴリへの載せ方を決める。

実装計画: `docs/superpowers/plans/2026-08-30-scene-preset-mte-effects.md` (v29、実装済み)
