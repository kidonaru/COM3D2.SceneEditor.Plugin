# Phase L1: 軽量レイヤー群移植（7 レイヤー） 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の軽量 7 レイヤー（BG / BGColor / Undress / Dress / Move / Animation / Voice）を SE タイムラインへ移植し、タイムラインで再生・キーフレーム編集できるようにする。

**Architecture:** 既存 5 レイヤーの移植と同じパターン: MTE ソース（`W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin\source\COM3D2.MotionTimelineEditor.Plugin\`）から TimelineLayer + TransformData を逐語コピーし、SE に無い依存だけ適合する。SE 側は TransformType enum 全メンバー・RegisterLayer / RegisterTransform 基盤・SceneEditorHack・DressUtils が既に存在するため、適合点は少ない。レイヤー UI（DrawWindow）は方針どおり接続しない（メソッドはコンパイルのため残置してよい。既存ポートと同じ）。

**Tech Stack:** C# / net35 / 非 SDK csproj（Compile Include 手動追加）

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L1

## Global Constraints

- ビルドは MSBuild 直接実行（`debug.bat` 禁止）:
  `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- 各タスク完了条件: 上記ビルドが警告なしで通る + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS
- csproj への `<Compile Include>` 追加を忘れない（手動管理）
- 名前空間は MTE と同じ `COM3D2.MotionTimelineEditor.Plugin` を維持（既存ポートと同じ）
- コミットは Conventional Commits 形式・日本語
- MTE 側ソースは読み取りのみ。変更しない

## 共通の移植手順（各レイヤータスクで繰り返す）

1. MTE `TimelineLayer/<Layer>.cs` → SE `Timeline/TimelineLayer/<Layer>.cs` へコピー
2. MTE `TransformData/<Data>.cs` → SE `Timeline/TransformData/<Data>.cs` へコピー
3. コンパイルエラーになる SE 非互換箇所のみ修正（既存ポート済みレイヤーの同名箇所を参照実装とする）
4. `Timeline/TimelineIntegration.cs` の `Initialize` に登録追加（既存行と同形式）:
   ```csharp
   timelineManager.RegisterLayer(
       typeof(MTEP.<Layer>), MTEP.<Layer>.Create);
   timelineManager.RegisterTransform(
       MTEP.TransformType.<Type>,
       MTEP.TimelineManager.CreateTransform<MTEP.<Data>>);
   ```
5. csproj に Compile Include 追加（`Timeline\TimelineLayer\` / `Timeline\TransformData\` の既存行の並びに挿入）
6. MSBuild → dotnet test → コミット

---

### Task 1: MoveTimelineLayer（最小依存・パターン確立）

**Files:**
- Create: `Timeline/TimelineLayer/MoveTimelineLayer.cs`（MTE 289 行）
- Create: `Timeline/TransformData/TransformDataMove.cs`
- Modify: `Timeline/TimelineIntegration.cs`, `COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: 以後のタスクの参照実装。TransformType.Move の登録

- [ ] **Step 1: 共通手順 1〜5 を実施**（依存は studioHack のみ → SE の StudioHackManager 経由で既存ポートと同一。適合不要の見込み）
- [ ] **Step 2: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 3: コミット** `feat(timeline): MoveTimelineLayer を移植 (メイド全体移動)`

### Task 2: UndressTimelineLayer + DressTimelineLayer

**Files:**
- Create: `Timeline/TimelineLayer/UndressTimelineLayer.cs`（276 行）/ `DressTimelineLayer.cs`（267 行）
- Create: `Timeline/TransformData/TransformDataUndress.cs` / `TransformDataDress.cs`
- Modify: `Timeline/TimelineIntegration.cs`, csproj

- [ ] **Step 1: 共通手順を 2 レイヤー分実施**（DressUtils は SE `Timeline/DressUtils.cs` に移植済み。MTE 側 DressUtils と API 差分があればコンパイルエラー箇所のみ SE 側 API に合わせる）
- [ ] **Step 2: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 3: コミット** `feat(timeline): Undress/DressTimelineLayer を移植 (脱衣・衣装切替)`

### Task 3: BGTimelineLayer + PhotoBGManager

**Files:**
- Create: `Timeline/PhotoBGManager.cs`（MTE 115 行。BG 一覧管理）
- Create: `Timeline/TimelineLayer/BGTimelineLayer.cs`（295 行）
- Create: `Timeline/TransformData/TransformDataBG.cs`
- Modify: `Timeline/TimelineLayer/TimelineLayerBase.cs`（`photoBGManager` プロパティ追加。MTE `TimelineLayer/TimelineLayerBase.cs:200` と同形式）
- Modify: `Timeline/Manager/ManagerBase.cs`（同上。MTE `Manager/ManagerBase.cs:44` 参照）
- Modify: `Timeline/TimelineIntegration.cs`, csproj

- [ ] **Step 1: PhotoBGManager を移植**（PhotoBGData 一覧化。SE BackgroundUtils と重複しても MTE 互換優先でそのまま持ち込む。SE ウィンドウ統合は後続 Phase の課題としない = レイヤー再生専用）
- [ ] **Step 2: BGTimelineLayer を共通手順で移植**（bgMgr は `GameMain.Instance.BgMgr` 直参照のため適合不要）
- [ ] **Step 3: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 4: コミット** `feat(timeline): BGTimelineLayer を移植 (背景切替)`

### Task 4: BGColorTimelineLayer

**Files:**
- Create: `Timeline/TimelineLayer/BGColorTimelineLayer.cs`（359 行）
- Create: `Timeline/TransformData/TransformDataBGColor.cs` / `TransformDataBGGroundColor.cs`
- Modify: `Timeline/TimelineIntegration.cs`, csproj

- [ ] **Step 1: 共通手順を実施**（RegisterTransform は BGColor と BGGroundColor の 2 件）
- [ ] **Step 2: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 3: コミット** `feat(timeline): BGColorTimelineLayer を移植 (背景色・地面色)`

### Task 5: AnimationTimelineLayer

**Files:**
- Create: `Timeline/TimelineLayer/AnimationTimelineLayer.cs`（377 行）
- Create: `Timeline/TransformData/TransformDataAnimation.cs`
- Modify: `Timeline/TimelineIntegration.cs`, csproj

- [ ] **Step 1: 共通手順を実施**（ゲーム標準 .anm 再生。maid.CrossFade 系 API。COM3D2.5 でも同 API が Assembly-CSharp に存在することをコンパイルで確認）
- [ ] **Step 2: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 3: コミット** `feat(timeline): AnimationTimelineLayer を移植 (モーションファイル再生)`

### Task 6: VoiceTimelineLayer

**Files:**
- Create: `Timeline/TimelineLayer/VoiceTimelineLayer.cs`（292 行）
- Create: `Timeline/TransformData/TransformDataVoice.cs`
- Modify: `Timeline/TimelineIntegration.cs`, csproj

- [ ] **Step 1: 共通手順を実施**（maid.AudioMan 系。GameMain 直参照）
- [ ] **Step 2: MSBuild + dotnet test で PASS 確認**
- [ ] **Step 3: コミット** `feat(timeline): VoiceTimelineLayer を移植 (ボイス再生)`

### Task 7: ゴールデンフィクスチャ追加 + 実機スモーク + ロードマップ更新

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l1-layers.xml`（L1 全 7 レイヤーを含む最小 XML を手書き。各レイヤー 1〜2 フレーム。version は現行値）
- Modify: `docs/superpowers/specs/timeline-layers-roadmap.md`（L1 完了マーク）

- [ ] **Step 1: フィクスチャ XML を作成し `dotnet test` で往復 PASS を確認**（TimelineLayerXml の className に各レイヤー名を設定。要素構成は既存フィクスチャと TransformXml 定義を参照）
- [ ] **Step 2: 実機スモーク（ゲーム起動中のみ）**: devbridge `ping` が通る場合、`eval_csharp` で各レイヤーの Create → AddLayer 相当を軽く確認。ゲーム未起動ならスキップし、**ロードマップの L1 完了マークに「実機検証は次回ゲーム起動時に実施」の注記を残す**
- [ ] **Step 3: ロードマップに L1 完了を反映しコミット** `docs(timeline): Phase L1 完了をロードマップに反映`（PhotoBGManager と SE BackgroundWindow/BackgroundUtils の BG 一覧管理が重複するため、統合要否の検討を将来課題として一言残す）

## 主要リスク

- MTE 側レイヤーが MTE 本体の Config / WindowManager 等 UI 系に触れている場合はコンパイルエラーで顕在化する → 既存ポート済みレイヤーの対応箇所を参照して同様に適合（DrawWindow 系は残置可）
- DressUtils / MaidCache の SE 側 API 差分 → コンパイルエラー駆動で最小修正
- AnimationTimelineLayer の .anm ロード経路が 2.5 で挙動差の可能性 → コンパイル確認まで。実挙動は Task 7 の実機スモークと将来の実機検証に委ねる
