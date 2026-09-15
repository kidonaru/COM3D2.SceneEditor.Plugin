# Phase L4: サブカメラ 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の SubCameraManager + SubCameraTimelineLayer を SE に移植し、サブカメラ演出（ピクチャインピクチャ）をタイムライン制御できるようにする。

**Architecture:** L1〜L3 で確立した移植定型の反復。SubCameraManager (432 行) は MaidFollowSubCamera (MonoBehaviour) を含む自己完結の実装で、MTE CameraManager / LetterBoxView / mte_bundle には依存しない（plan-review の独立検証でも確認済み）。

**PIP 表示先の決定（ロードマップの計画時決定事項）:** SE の GameViewManager はウィンドウモード時にメインカメラを RenderTexture へリダイレクトする（GameViewManager.cs:102）。サブカメラを MTE のまま viewport rect 直接描画にすると、ウィンドウモードで PIP が GameViewWindow の外（実画面）に描かれてしまう。そのため SE 適合として **SubCameraManager の毎フレーム更新でサブカメラの targetTexture をメインカメラの targetTexture にミラーする**（ウィンドウモード = RT / 最大化・非ウィンドウ = null が自動追従。camera.rect は targetTexture 内の正規化 viewport として機能するため PIP は GameViewWindow 内に正しく合成される）。

**Tech Stack:** C# / net35 / 非 SDK csproj

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L4

## Global Constraints

- ビルド: MSBuild 直接実行（debug.bat 禁止）、完了条件はビルド成功 + dotnet test 全 PASS（L1〜L3 と同一）
- 移植定型: DCM スタブ / focusedComboBox / DrawComboBox 削除+コメント / 未使用 using 除去 / OutputDCM 未定義レイヤーには空スタブ追加

### Task 1: SubCameraManager + SubCameraTimelineLayer 移植

**Files:**
- Create: `Timeline/Manager/SubCameraManager.cs`（432 行。MaidFollowSubCamera 含む）
- Create: `Timeline/TimelineLayer/SubCameraTimelineLayer.cs`（507 行）
- Create: `Timeline/TransformData/TransformDataSubCamera.cs`
- Modify: `Timeline/TimelineIntegration.cs`（RegisterLayer + RegisterTransform(SubCamera) + マネージャリスト追加）
- Modify: `Timeline/TimelineLayer/TimelineLayerBase.cs` / `Timeline/Manager/ManagerBase.cs`（subCameraManager プロパティが MTE 側にあれば同形式で追加）
- Modify: csproj

- [ ] Step 1: 逐語コピー + 定型適合。SE に無い依存はコンパイルエラー駆動で最小適合
- [ ] Step 1b: SE 適合を追加: (a) サブカメラの targetTexture をメインカメラへミラー（SubCameraManager の更新処理に SE 追加コメント付きで実装）、(b) TimelineLayerDesc の priority が ModelTimelineLayer(21) と重複するため空き番号へ変更（SE 差分コメントを残す）
- [ ] Step 2: ビルド + dotnet test PASS
- [ ] Step 3: コミット `feat(timeline): SubCameraManager と SubCameraTimelineLayer を移植 (サブカメラ)`

### Task 2: フィクスチャ + 実機スモーク + ロードマップ更新

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l4-subcamera.xml`（Type: SubCamera の最小 XML）
- Modify: `docs/superpowers/specs/timeline-layers-roadmap.md`

- [ ] Step 1: フィクスチャ作成、dotnet test 全 PASS
- [ ] Step 2: devbridge が応答すればサブカメラ生成（Camera + viewport rect）をスモーク。応答しなければスキップして注記
- [ ] Step 3: コミット `docs(timeline): Phase L4 完了をロードマップに反映`

## リスク

- Unity 2022 (COM3D2.5) での Camera 複製・viewport 挙動差 → コンパイル + 実機スモークで検証。RenderTexture 非使用のため URP 干渉リスクは低い
- メイド追従 (MaidFollowSubCamera) のスロット番号解決が SE の MaidManager と整合するか → コンパイルエラー駆動で確認
