# Phase L3: 背景モデル・マテリアル系（3 レイヤー） 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の BGModelManager 基盤と BGModel / BGModelMaterial / MaidMaterial の 3 レイヤーを SE に移植し、背景構成オブジェクトとメイドマテリアルをタイムライン制御できるようにする。

**Architecture:** L1/L2 で確立した移植定型の反復。BGModelManager (479 行) + BGModelStat を逐語移植し、レイヤー 3 種を定型適合（DCM スタブ / focusedComboBox / DrawComboBox 削除 / 未使用 using 除去 / DrawComboBox 省略コメント）で移植する。MaidMaterialTimelineLayer は L2 で移植済みの `TransformDataModelMaterial`（TransformType.ModelMaterial）を再利用するため新規 TransformData 不要。BGModel 用は `TransformDataBGModel.cs` を追加。

**Tech Stack:** C# / net35 / 非 SDK csproj

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L3

## 調査結果（前提）

- TimelineData.bgModels (TimelineBGModelData) はデータ層に既存
- MTE `TimelineLayerBase:192` / `ManagerBase:29` に `bgModelManager` プロパティ（SE 側へ同形式で追加）
- MaidMaterialTimelineLayer は `GetOrCreateTransformData<TransformDataModelMaterial>` / TransformType.ModelMaterial を使用（新規登録不要）
- SE TransformType enum に BGModel は定義済み
- MaidMaterial と ModelMaterial の TransformType.ModelMaterial 共用は MTE 原設計どおり（TransformType はレイヤー単位で解釈されるため衝突しない）。**新規 TransformType を追加しないこと**
- BGModelManager は `GameMain.Instance.BgMgr.BgObject` の変化検知で背景モデル一覧を再構築する。SE (SceneEdit) でも BgMgr は BackgroundWindow で使用実績があるが、BgObject 経路は Task 3 の devbridge スモークで必ず確認する（ゲーム起動中のため実施可能）

## Global Constraints

- ビルド: MSBuild 直接実行（debug.bat 禁止）: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- 各タスク完了条件: ビルド成功 + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` 全 PASS
- 移植定型は L1/L2 と同一（コミット履歴のパターンに従う）

### Task 1: BGModelManager 基盤 + BGModelTimelineLayer

**Files:**
- Create: `Timeline/BGModelStat.cs`（MTE 同名）、`Timeline/Manager/BGModelManager.cs`（479 行）
- Create: `Timeline/TimelineLayer/BGModelTimelineLayerBase.cs`（BGModel / BGModelMaterial 共通基底。Task 2 も継承する）
- Create: `Timeline/TimelineLayer/BGModelTimelineLayer.cs`（274 行）、`Timeline/TransformData/TransformDataBGModel.cs`
- Modify: `Timeline/TimelineLayer/TimelineLayerBase.cs` / `Timeline/Manager/ManagerBase.cs`（bgModelManager プロパティ追加）
- Modify: `Timeline/TimelineIntegration.cs`（RegisterLayer + RegisterTransform(BGModel) + マネージャリストへ BGModelManager 追加）
- Modify: csproj

- [ ] Step 1: 逐語コピー + 定型適合。BGModelManager の MTE 固有依存はコンパイルエラー駆動で最小適合
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): BGModelManager と BGModelTimelineLayer を移植 (背景構成オブジェクト)`

### Task 2: BGModelMaterialTimelineLayer + MaidMaterialTimelineLayer

**Files:**
- Create: `Timeline/TimelineLayer/BGModelMaterialTimelineLayer.cs`（298 行）/ `MaidMaterialTimelineLayer.cs`（282 行）
- Modify: `Timeline/TimelineIntegration.cs`（RegisterLayer ×2。TransformData は既存登録を再利用）
- Modify: csproj

- [ ] Step 1: 逐語コピー + 定型適合
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): BGModelMaterial/MaidMaterialTimelineLayer を移植`

### Task 3: フィクスチャ + 実機スモーク + ロードマップ更新

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l3-bgmodel-layers.xml`（3 レイヤー + BGModels 要素の最小 XML。Type は BGModel / ModelMaterial）
- Modify: `docs/superpowers/specs/timeline-layers-roadmap.md`

- [ ] Step 1: フィクスチャ作成、dotnet test 全 PASS
- [ ] Step 2: ゲーム起動中なら devbridge で BGModelManager の背景オブジェクト列挙経路をスモーク。未起動ならスキップして注記
- [ ] Step 3: ロードマップ更新をコミット `docs(timeline): Phase L3 完了をロードマップに反映`

## リスク

- BGModelManager が背景 GameObject 走査に使うゲーム API の 2.5 差分 → コンパイルエラー駆動 + devbridge 確認
- MaidMaterialTimelineLayer のメイドマテリアル走査（TBody/Renderer）の CRC ボディ差分 → ボーン構成非依存（roadmap 前提）のため低リスク。実機通し確認はゲーム再起動後
