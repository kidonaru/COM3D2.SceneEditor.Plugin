# Phase L7: PngPlacement 連携 + 仕上げ 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PngPlacementTimelineLayer を SE ネイティブの PngPlacementManager に接続するアダプタ版として実装し、MTE 互換総点検と docs 更新でロードマップを完遂する。

**Architecture（調査結果に基づく決定）:** MTE_PngPlacement は外部プラグイン PngPlacement.dll へのハード参照 + リフレクション Field 群で実装されているが、**SE は PNG 配置を自前実装済み**（`Manager/PngPlacementManager.cs`、468 行、PngObjectData: rootObject/quadObject/billboard/brightness/color/renderQueue/visible）。そのため L7 は LightTimelineLayer（SE StudioLightManager 接続）と同型の **アダプタ版** とする:

- `TransformDataPngObject`（463 行、valueCount=31）は **逐語移植**（MTE XML 互換の要。全 31 値をラウンドトリップ保存）
- `PngPlacementTimelineLayer`（482 行）は MTE 版をベースに、wrapper 呼び出しを SE PngPlacementManager API（AddPng/RemovePng/SetBillboard/SetColor/SetRenderQueue/SetVisible + transform 直接操作）へ置換
- 適用時にマップする値: Position/Euler/Scale（transform）、ColorRGBA+Brightness（SetColor）、Visible（SetVisible）。**SE に対応機能が無い値（Inversion/StopRotation/FixCamera/Attach/APng 系等）は適用しない**（XML には保持。コメントで明示）
- タイムライン読込時のオブジェクト生成は `Timeline/Manager/PngObjectTimelineManager.cs`（新規）が担う。**plan-review で確定した設計制約**:
  1. **データ層スキーマは変更しない**: TimelinePngObjectData は `imageName/group/primitive/squareUV/shaderDisplay/renderQueue`（MTE 互換スキーマ）。source/relativePath は持たない
  2. **画像解決**: `imageName` を SE の既知ソース（SOURCE_CONFIG=`UserData/PngPlacement` → SOURCE_PHOTO の順）から拡張子無視のファイル名一致で探索し、見つかれば `AddPng(source, relativePath)` で生成。**見つからない場合は警告ログを出して実体生成をスキップ**（キーフレームは XML に保持されたまま）。docs に「MTE プロジェクトの画像は UserData/PngPlacement へ配置が必要」と明記する
  3. **命名対応表**: タイムライン側の識別子（`imageName + groupSuffix`）と SE AddPng の実体名（ファイル名+連番）は一致しないため、PngObjectTimelineManager が **name → PngObjectData の対応表を自前で保持**し、`GetPngObject(name)` はこの対応表で解決する（SE の実体名には依存しない）
  4. **Scale 変換**: MTE の scalex/scalemag/scalez 合成式を ApplyPngObject から移植し、アスペクト補正は SE の quadObject に委譲されているため root の Y は X と同値にする（実装時に MTE の式と突合し、差異があればコメントで記録）
  5. **生成時パラメータ**（primitive/squareUV/shaderDisplay）は SE の固定 Quad + 標準シェーダーで代替し適用しない（XML 保持。コメント + docs 明示）
  6. billboard は 31 値に含まれない（per-frame 値でない）ため適用対象外である旨をコメントに明示

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L7

## Global Constraints

- ビルド: MSBuild 直接実行・GameVersion=COM3D25（debug.bat 禁止）、完了条件 = ビルド成功 + dotnet test 全 PASS
- PngPlacement.dll へのハード参照は追加しない（SE 自前実装接続のため不要）

### Task 1: TransformDataPngObject + PngObjectTimelineManager + PngPlacementTimelineLayer

**Files:**
- Create: `Timeline/TransformData/TransformDataPngObject.cs`（逐語。namespace を COM3D2.MotionTimelineEditor.Plugin へ変更）
- Create: `Timeline/Manager/PngObjectTimelineManager.cs`（MTE PngPlacementManager の SE 適合版: pngObjects/pngObjectNames 一覧、GetPngObject(name)、Setup(List<TimelinePngObjectData>)、OnLoad/Reset）
- Create: `Timeline/TimelineLayer/PngPlacementTimelineLayer.cs`（MTE 版ベースのアダプタ。DCM スタブ / focusedComboBox / DrawComboBox 定型も適用）
- Modify: TimelineIntegration（RegisterLayer + RegisterTransform(PngObject) + マネージャリスト）、csproj
- Modify: TimelineData の TimelinePngObjectData に FromModel 相当が必要ならデータ層へ追補（L3 の TimelineBGModelData.FromModel と同型）

- [ ] Step 1: TransformDataPngObject 逐語移植（namespace のみ変更、変更点コメント）
- [ ] Step 2: PngObjectTimelineManager 実装（SE PngPlacementManager.instance へ委譲。名前は SE 側と衝突しないよう PngObjectTimelineManager とし、命名変更理由をコメント）
- [ ] Step 3: レイヤーのアダプタ実装（未対応値の不適用をコメントで明示）
- [ ] Step 4: ビルド + dotnet test PASS → コミット `feat(timeline): PngPlacementTimelineLayer を移植 (SE ネイティブ PNG 配置接続)`

### Task 2: フィクスチャ + MTE 互換総点検（機械検証パート）

- Create: `Tests/Fixtures/l7-pngobject.xml`（PngObject 31 値 + PngObjects 要素の最小 XML）
- [ ] Step 1: dotnet test 全 PASS
- [ ] Step 2: **MTE 互換総点検**: TimelineIntegration の RegisterLayer 済みレイヤー名集合と、全フィクスチャ + ローカルの実プロジェクト XML（**リポジトリにはコミットしない**。テストはローカル環境にファイルがある場合のみ実行するオプトイン形式）の className 集合を突合するテストを追加。**DCM 系 3 レイヤー（Morph/Se/Text）はスコープ外の既知除外として assert から外す**
- [ ] Step 3: コミット `test(timeline): L7 フィクスチャと MTE 互換の登録網羅テストを追加`

### Task 3: docs 更新 + ロードマップ完遂

- Modify: `docs-site` のユーザーガイド（「未対応レイヤーは破棄」注意書きの更新。該当ページを grep で特定）
- Modify: ロードマップ（L7 完了 + 全フェーズ完遂サマリ + 実機通し確認の残タスク一覧）
- [ ] Step 1: docs-site の該当記述を更新
- [ ] Step 2: コミット `docs(timeline): Phase L7 完了、全レイヤー移植ロードマップを完遂`

## リスク

| リスク | 対応 |
|---|---|
| MTE 31 値と SE PNG 機能のギャップ（Attach/APng 等） | 適用対象外として明示（XML 保持で互換維持）。SE 側 PNG 機能拡張は将来課題としてロードマップに記録 |
| MTE レイヤーの wrapper 依存コードの置換漏れ | コンパイルエラー駆動 + code-review で検証 |
| 実機通し確認（全レイヤー再生）は本セッションでは不可（要ゲーム再起動） | ロードマップに「次回起動時の確認チェックリスト」を残す |
