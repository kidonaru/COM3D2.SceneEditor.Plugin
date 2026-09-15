# Phase L6: ポストエフェクト 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の PostEffectTimelineLayer（本体 + DepthOfField / DistanceFog / GTToneMap / Paraffin / Rimlight の 5 分割 partial）を SE に移植し、ポストエフェクトをタイムライン制御できるようにする。

**計画時決定（ロードマップの分岐）:** エフェクト実体は **MTE 実装持ち込み** とする。理由: MTE の PostEffect は UnityScripts/PostEffect（Model / Settings / PostEffectController、計 11 ファイル）+ mte_bundle 内シェーダーで自己完結しており、L5 で bundle ロードは実機検証済み。PostEffects.Plugin へのソフト依存（リフレクション連携）は不要になり、未導入環境の分岐も発生しない。

**Architecture:** L1〜L5 の移植定型の反復。TransformType は DepthOfField / DistanceFog / GTToneMap / Paraffin / Rimlight の 5 種（SE enum 定義済み）。

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L6

## Global Constraints

- ビルド: MSBuild 直接実行（debug.bat 禁止）、**必ず `GameVersion=COM3D25` 構成で確認**（`MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 ...`。L0 以降の全ビルドがこの構成で実施済み）、完了条件 = ビルド成功 + dotnet test 全 PASS
- 移植定型: DCM スタブ / focusedComboBox / DrawComboBox 削除+コメント / OutputDCM 無しは空スタブ追加
- レビューで確認済みの前提: 非修飾 `DepthOfFieldScatter` は現行 COM3D25 ビルドで解決済み（StudioHackBase が既に使用）。UnityScripts の `#if COM3D2` 分岐は SE の DefineConstants に COM3D2 が常に含まれるため bundle 経由パス（true 側）になる — 意図どおり
- 確認事項: `studioHack.depthOfField` は `GetComponent`（null あり得る）。PostEffectManager の DoF 経路が null 安全か Task 1 で確認し、必要なら SE 適合で null ガードを足す

### Task 1: PostEffect 基盤（UnityScripts/PostEffect + PostEffectUtils + PostEffectManager）

**Files:**
- Create: `Timeline/UnityScripts/PostEffect/`（10 ファイル: ColorParaffinEffectModel/Settings, DistanceFogEffectModel/Settings, GTToneMapController, PostEffectContext, PostEffectController, PostEffectModelBase, PostEffectSettingsBase, RimlightEffectModel/Settings。実施時に MTE 側一覧と突合すること）
- Create: `Timeline/PostEffectUtils.cs`、`Timeline/Manager/PostEffectManager.cs`（322 行）
- Modify: csproj、TimelineIntegration（マネージャリスト）、TimelineLayerBase/ManagerBase（postEffectManager プロパティが MTE にあれば追加）

- [ ] Step 1: コピー + コンパイルエラー駆動適合（DepthOfField はゲーム標準コンポーネント依存の可能性 → エラー時に実体を確認して適合）
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): PostEffect 基盤を移植 (Manager + エフェクト実体)`

### Task 2: PostEffectTimelineLayer（本体 + 5 分割 partial）+ TransformData 5 種

**Files:**
- Create: `Timeline/TimelineLayer/PostEffectTimelineLayer.cs` + `_DepthOfField/_DistanceFog/_GTToneMap/_Parrifin/_Rimlight.cs`（partial、MTE のファイル名維持）
- Create: `Timeline/TransformData/TransformData{DepthOfField,DistanceFog,GTToneMap,Paraffin,Rimlight}.cs`
- Modify: TimelineIntegration（RegisterLayer ×1 + RegisterTransform ×5）、csproj

- [ ] Step 1: 定型適合（partial 分割ファイルの DrawWindow 系にも focusedComboBox 適合を忘れない）
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): PostEffectTimelineLayer を移植 (5 エフェクト)`

### Task 3: フィクスチャ + 実機スモーク + ロードマップ更新

- Create: `Tests/Fixtures/l6-posteffect.xml`（5 TransformType 全部入り最小 XML）
- [ ] Step 1: dotnet test 全 PASS
- [ ] Step 2: devbridge が応答すれば PostEffectController 型ロードと bundle からの MTE シェーダー Material 生成（L5 で MTE/GTToneMap は取得済みのため軽く）をスモーク
- [ ] Step 3: ロードマップ更新コミット `docs(timeline): Phase L6 完了をロードマップに反映`

## リスク

- DepthOfField がゲーム側 (UnityStandardAssets 等) のコンポーネント依存の場合の 2.5 差分 → コンパイルエラー駆動 + devbridge 型確認
- URP でのポストエフェクト実描画（OnRenderImage 系が URP で動くか）→ 実表示検証はゲーム再起動後の通し確認に委ね、ロードマップに注記（COM3D2.5 は Built-in RP の可能性が高く、MTE 自体が 2.5 対応を謳うためリスク低）
