# Phase L5: 演出系（mte_bundle + StageLight / StageLaser / Psyllium） 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE のライブ演出 3 レイヤー（ステージライト・レーザー・ペンライト）を SE に移植し、mte_bundle（DLL 埋め込みアセットバンドル）経由の描画実体を含めてタイムライン制御できるようにする。

**Architecture:** L1〜L4 の移植定型 + 新規要素 2 点: (1) `mte_bundle`（136,570 bytes、自作 MIT）を SE リポジトリへコピーし csproj に `EmbeddedResource`（LogicalName=mte_bundle）で埋め込み、TimelineBundleManager（170 行、`GetManifestResourceStream("mte_bundle")` → `AssetBundle.LoadFromMemory`）を移植。(2) UnityProject スクリプト群（MonoBehaviour: Psyllium 系 7 + StageLight 系 2 + StageLaser 系 2）を `Timeline/UnityScripts/` へコピーして Compile Include（MTE も同スクリプトを DLL に直接コンパイルしており、bundle はマテリアル・テクスチャ等のアセットのみ）。EasingFunctions / MoveEasingType / BGGround は SE 移植済みのため除外。

**Tech Stack:** C# / net35 / 非 SDK csproj / EmbeddedResource

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L5

## Global Constraints

- ビルド: MSBuild 直接実行（debug.bat 禁止）、完了条件 = ビルド成功 + dotnet test 全 PASS（L1〜L4 と同一）
- 移植定型: DCM スタブ / focusedComboBox / DrawComboBox 削除+コメント / OutputDCM 無しレイヤーへ空スタブ追加 / 未使用 using 除去
- 名前空間衝突（StudioLightManager と StageLightManager 等）は既存どおり COM3D2.MotionTimelineEditor.Plugin 名前空間内で共存（クラス名が異なるため衝突しない）
- mte_bundle はビルド済みバイナリを流用（UnityProject からの再ビルドは README 参照に留める。ロードマップ方針どおり）

### Task 1: mte_bundle 埋め込み + TimelineBundleManager + UnityProject スクリプト群

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/mte_bundle`（バイナリコピー）
- Create: `Timeline/UnityScripts/`: Psyllium.cs, PsylliumArea.cs, PsylliumConfig.cs, PsylliumController.cs, PsylliumHand.cs, PsylliumPattern.cs, PsylliumRandomValues.cs, StageLaser.cs, StageLaserController.cs, StageLight.cs, StageLightController.cs（UnityProject/Assets/Scripts から）
- Create: `Timeline/Manager/TimelineBundleManager.cs`
- Modify: csproj（EmbeddedResource LogicalName=mte_bundle + Compile Include ×12）
- Modify: `Timeline/TimelineIntegration.cs`（TimelineBundleManager をマネージャリストへ）

- スコープ注記: TimelineBundleManager の周辺利用のうち lockIcon/unlockIcon（ウィンドウ位置ロック UI）・song.ogg（BGM サンプル）・icon.png（起動アイコン）は **L5 スコープ外**（SE に対応機能が無い/独自実装済み）。bundleManager は StageLight/Laser/Psyllium のマテリアル・テクスチャ読み込み用途のみ移植する
- 依存注記: PsylliumManager は PsylliumConfig.cs 内の PsylliumPatternConfig / PsylliumTransformConfig / PsylliumBarConfig / PsylliumHandConfig に依存（Task 1 でコピーされるため順序は満たされる）
- [ ] Step 1: コピー + csproj + 定型適合（NoiseTextureGenerator は EditorWindow 継承のエディタ専用のため対象外）
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): mte_bundle 埋め込みと演出系 MonoBehaviour 群を移植`

### Task 2: StageLightManager + StageLightTimelineLayer

**Files:**
- Create: `Timeline/Manager/StageLightManager.cs`（291 行）、`Timeline/TimelineLayer/StageLightTimelineLayer.cs`（971 行）
- Create: `Timeline/TransformData/TransformDataStageLight.cs` / `TransformDataStageLightController.cs`
- Modify: TimelineIntegration（RegisterLayer + RegisterTransform ×2: StageLight / StageLightController + マネージャリスト）、csproj、必要なら TimelineLayerBase/ManagerBase にプロパティ追加

- [ ] Step 1〜3: 定型どおり移植 → ビルド/テスト → コミット `feat(timeline): StageLightTimelineLayer を移植 (ステージライト)`

### Task 3: StageLaserManager + StageLaserTimelineLayer

**Files:** 上記と同型（Manager 274 行 / Layer 998 行 / TransformData ×2: StageLaser / StageLaserController。**TimelineIntegration の RegisterLayer + RegisterTransform ×2 + マネージャリスト追加を忘れないこと**）

- [ ] Step 1〜3: 定型どおり → コミット `feat(timeline): StageLaserTimelineLayer を移植 (ステージレーザー)`

### Task 4: PsylliumManager + PsylliumTimelineLayer

**Files:** Manager 415 行 / Layer 1794 行 / TransformData ×6: PsylliumArea / PsylliumBar / PsylliumController / PsylliumHand / PsylliumPattern / PsylliumTransform（**TimelineIntegration の RegisterLayer + RegisterTransform ×6 + マネージャリスト追加を忘れないこと**）

- [ ] Step 1〜3: 定型どおり → コミット `feat(timeline): PsylliumTimelineLayer を移植 (ペンライト)`

### Task 5: フィクスチャ + 実機スモーク + ロードマップ更新

**Files:**
- Create: `Tests/Fixtures/l5-stage-effects.xml`（3 レイヤーの最小 XML）
- Modify: ロードマップ

- [ ] Step 1: フィクスチャ作成、dotnet test 全 PASS
- [ ] Step 2: **実機スモーク（重要）**: devbridge で新 DLL をバイト配列ロードし、TimelineBundleManager 相当の `GetManifestResourceStream("mte_bundle")` → `AssetBundle.LoadFromMemory` → `LoadMaterial` が COM3D2.5 (Unity 2022) で成功するか確認。bundle は旧 Unity でビルドされているため、**ロード失敗時は本フェーズ最大のリスクが顕在化したことになる**。失敗した場合はエラー内容を記録し、ロードマップに「UnityProject の 2022 対応再ビルドが必要」と注記して本フェーズをコード移植完了（アセット保留）として締める
- [ ] Step 3: コミット `docs(timeline): Phase L5 完了をロードマップに反映`

## リスク

| リスク | 対応 |
|---|---|
| mte_bundle が Unity 2022 (COM3D2.5) でロードできない（旧 Unity ビルドの AssetBundle 互換性） | Task 5 の実機スモークで必ず検証。MTE 本体が 2.5 対応済みなら同 bundle が動く実績があるはず（MTE は 2.0/2.5 両対応を謳う）→ 動く可能性が高い |
| シェーダー（bundle 内マテリアル）の URP 非互換 | ロード成否とは別問題。表示検証はゲーム再起動後の通し確認に委ね、ロードマップに注記 |
| PsylliumTimelineLayer (1794 行) の適合漏れ | コンパイルエラー駆動 + code-review の原本 diff 検証でカバー |
