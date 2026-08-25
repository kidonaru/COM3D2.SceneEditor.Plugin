# 表情モーフ変更追跡(チェックボックス)機能 設計書

作成日: 2026-08-26
対象ブランチ: feature/timeline-window

## 目的

表情エディタ(MaidFaceWindow)の各モーフ項目に「値を変更しているか」を示すチェックボックスを追加し、

1. スライダー/トグル操作時に自動でチェックが付く
2. チェックを外すと値が 0(デフォルト)に戻り追跡解除される
3. シーンプリセット・表情プリセットには**チェック済み項目のみ**保存される
4. タイムラインのメイド表情レイヤー(MorphTimelineLayer)のボーンメニュー表示・キーフレーム書き込みが**チェック済み項目(∪ 既存キーフレーム記載モーフ)**に絞られる

を実現する。対象は表情モーフのみ(指・シェイプキー・ボーン編集は対象外。仕組みは後で横展開できる形にする)。

## 背景(現状)

- 表情値は maid の `TMorph` にのみ保存され、変更追跡は存在しない
- シーンプリセット保存は「値 ≠ 0」フィルタ(`ScenePresetManager.Capture` 付近、`MaidFacePresetManager.Capture` にも同ルールが重複)
- プリセット適用(`ApplyFace`)は未記載モーフを全て 0 にリセットする
- `MorphTimelineLayer` は全モーフをメニュー表示し、`UpdateFrame` で全モーフをキーフレームに書き込む
- 編集追跡の既存前例として `BoneEditStore`(編集時記録・リセットで復元)がある
- 表情エディタ側(`MaidFaceMorphController.MorphDefs`)とタイムライン側(`FaceMorphUtils`)はモーフ名テーブルが独立しており、共通に使えるキーは生の morph 名文字列のみ

## 設計

### 1. FaceEditStore(新規)

場所: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs`
`MaidManipulateManager` に他コントローラ(lookController 等)と同様に登録する。

- 実体: `Dictionary<Maid, HashSet<string>>`(キーは生 morph 名)
- API:
  - `bool IsModified(Maid maid, string name)`
  - `void MarkModified(Maid maid, string name)`
  - `void Unmark(Maid maid, string name)`
  - `void SetModifiedNames(Maid maid, IEnumerable<string> names)` — プリセットロード時の一括復元
  - `IEnumerable<string> GetModifiedNames(Maid maid)`
  - `void Clear(Maid maid)` / メイド消滅時のクリーンアップ
- マークは **UI 操作(MaidFaceWindow の onChanged)とプリセット適用時のみ**行う。
  `MaidFaceMorphController.SetMorphValue` には仕込まない(タイムライン再生が毎フレーム全モーフを書き込むため)。
- Undo/Redo: `FaceSnapshot` にチェック集合(morph 名集合)を追加し、`HistoryScope.Face` の履歴で復元する。

### 2. 表情ウィンドウ UI(MaidFaceWindow)

- `DrawMorphList` の各行(スライダー行・トグル行)の label 左に幅 20px のチェックボックスを追加。
  `GUIView.DrawToggle(bool, 20, 20, onChanged)` を行頭に置き、ラベル幅を 20px 詰める。
- スライダー/トグル変更の onChanged 内で `MarkModified` を呼ぶ(既存の `HistoryManager.BeforeEdit` → `SetMabataki(false)` → `SetMorphValue` フローに追加)。
- チェック手動 OFF 時: `BeforeEdit` で履歴記録 → 値を 0 に設定 → `Unmark`。
- カテゴリ「リセット」(`ResetCategory`)実行時は当該カテゴリのモーフを全て `Unmark`。
- タブごとの一括チェック解除ボタンは追加しない(リセットと同義のため)。

### 3. シーンプリセット / 表情プリセット

- 保存フィルタを `value != 0f` から「`IsModified` == true」に変更(`ScenePresetManager` の表情 Capture、`MaidFacePresetManager.Capture` の両方)。
  値 0 でもチェック済みなら保存する(明示的に 0 にした意図の保持)。
- `ScenePresetMorph` / `FacePresetMorph` の構造は変更しない(保存されている = チェック済み、の対応で表現できるため)。
- `ScenePresetData` のスキーマバージョンを上げ、バージョン履歴コメントに追記する。
  旧バージョンプリセットは記載モーフ(= 非 0 で保存されたもの)をチェック済みとして復元するため、後方互換は自然に成立する。
- 適用(`ApplyFace` / `MaidFacePresetManager.Apply`): 既存の「未記載モーフを 0 にする」挙動は維持しつつ、適用後に `SetModifiedNames` で保存されていた morph 名集合を反映する。

### 4. タイムライン(MorphTimelineLayer / ボーンメニュー)

- 表示・書き込み対象集合 = 「チェック済みモーフ ∪ 既存キーフレームに含まれるモーフ」。
  和集合にするのは、既存アニメーションのモーフが編集不能になるのを防ぐため。
- `allBoneNames` をこの集合を返すよう変更(現状は `FaceMorphUtils.saveMorphNames` 固定)。
- `InitMenuItems` も同集合でメニューを構築する。チェック状態の変化時にメニュー再構築をトリガーする
  (表情ウィンドウでのチェック変更 → 現在レイヤーが MorphTimelineLayer ならメニュー再構築通知)。
- `UpdateFrame` のキーフレーム書き込みも同集合に絞る。
  → 新規キーフレームはチェック済みモーフのみ保持し、未チェックモーフは補間対象外となる(仕様として合意済み)。
- チェック 0 件かつ既存キーフレームなしの場合、ボーンメニューは空になる(許容、合意済み)。
- 対象は `MorphTimelineLayer`(メイド表情)のみ。`EyesTimelineLayer` / `ShapeKeyTimelineLayer` 等は対象外。

## テスト・ビルド

- `FaceEditStore` の単体テスト(マーク/解除/一括復元/Clear/メイド別独立性)を Tests プロジェクトに追加。
- COM3D2 / COM3D2.5 の両構成でビルド確認(`debug.bat` は実機コピーを伴うため、必要に応じ MSBuild 直叩き)。

## 影響ファイル一覧(想定)

| ファイル | 変更内容 |
|---|---|
| `MaidManipulation/FaceEditStore.cs` | 新規 |
| `MaidManipulation/MaidManipulateManager.cs` | FaceEditStore 登録 |
| `MaidFaceWindow.cs` | チェックボックス列追加・マーク/解除処理 |
| `Manager/History/FaceSnapshot.cs` | チェック集合の履歴対応 |
| `Manager/ScenePresetManager.cs` | 保存フィルタ変更・適用時チェック復元 |
| `ScenePresetData.cs` | スキーマバージョン更新(コメント) |
| `MaidManipulation/MaidFacePresetManager.cs` | 同上 |
| `Timeline/TimelineLayer/MorphTimelineLayer.cs` | allBoneNames / InitMenuItems / UpdateFrame の絞り込み |
| `Timeline/BoneMenu/*`(必要時) | メニュー再構築トリガー |
| Tests | FaceEditStore テスト追加 |
