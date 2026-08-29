# A分類レイヤーの編集UI委譲化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** レイヤー編集ウィンドウで個別ウィンドウと機能が被っている 8 レイヤーの編集 UI を削除し、PngPlacementTimelineLayer と同じ「案内ラベル＋個別ウィンドウ委譲」方式に置き換える。

**Architecture:** 各 `TimelineLayer.DrawWindow` から重複編集 UI を削り、委譲先ウィンドウへの案内ラベルに置き換える。レイヤー固有機能（モデル管理 UI、強制上書きトグル、ブレンド有効トグル)は残す。Task 3〜5 では操作/管理のタブ切り替え自体を廃止し、**案内ラベル＋モデル管理 UI の常時表示**へ一本化する（意図的な設計。`_tabType` は永続化されない一時 static フィールドのため削除しても保存互換性の問題はない）。キーフレーム書き込みは現在のメイド/モデルのライブ状態を読むため、編集経路が個別ウィンドウへ移っても影響しない（調査済み）。

**Tech Stack:** C# (.NET Framework / Unity IMGUI 風の GUIView)。ユニットテスト対象外の UI コードのため、検証は両 GameVersion のビルド成功とグレップによる参照残り確認。

**Spec:** `docs/layer-window-duplication-survey.md`（A 分類の突き合わせ調査）

## Global Constraints

- ビルドは **COM3D2 / COM3D25 の両方**を必ず通すこと（対象フレームワークが異なる）。`debug.bat` はゲームフォルダへ DLL をコピーするため使わず、MSBuild を直接叩く:
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
  ```
- コメント・ラベル文言は日本語。案内ラベルは PngPlacementTimelineLayer.cs:201-206 の既存パターンに合わせる:
  ```csharp
  public override void DrawWindow(GUIView view)
  {
      // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
      // <対象> の編集 UI は SE の <ウィンドウ> に委譲する (レイヤー UI 非接続方針)
      view.DrawLabel("<対象>の編集は<ウィンドウ名>ウィンドウで行ってください", -1, 20);
  }
  ```
- **BGModelMaterialTimelineLayer は対象外**。MaterialEditWindow の背景タブは「現在の背景の Renderer」を対象とし、レイヤーは「BGModelManager の配置モデル」を対象とするため集合が食い違う（MaterialEditWindow.cs:285-286）。委譲すると配置背景モデルのマテリアル編集手段が失われる。
- 各タスクで UI メソッドを削除したら、`Grep` で削除メソッド名の参照が残っていないことを確認してからビルドする。未使用になったフィールド（コンボボックス、TabType 等）も併せて削除する。

---

### Task 1: ShapeKeyTimelineLayer（メイドシェイプ）の全面委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs:150-297`

**Interfaces:**
- Consumes: なし
- Produces: なし（UI 削除のみ。キー対象の選別は既に `MaidShapeKeyEditManager` のストア経由で ShapeKeyEditWindow が担う）

- [ ] **Step 1: DrawWindow を委譲ラベルに置き換える**

`DrawWindow`（:150）の本体を以下に置き換える（メイド未配置ガードは不要になる）:

```csharp
public override void DrawWindow(GUIView view)
{
    // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
    // シェイプキーの編集・キー対象選別は SE のシェイプキー編集ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("シェイプキーの編集はシェイプキーウィンドウで行ってください", -1, 20);
}
```

- [ ] **Step 2: 不要コードを削除する**

`DrawBlendShapesAdd`（:179）、`DrawBlendShapesEdit`（:249）、`TabType` enum と `_tabType`、`_slotNameComboBox`・`_slotNames`・`_maid` などこの 2 メソッドだけが使っていたフィールドを削除する。

- [ ] **Step 3: 参照残りを確認する**

Grep: `DrawBlendShapesAdd|DrawBlendShapesEdit` → ヒット 0 件であること。

- [ ] **Step 4: 両バージョンをビルドする**

Global Constraints の MSBuild 2 コマンドを実行し、両方成功すること。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs
git commit -m "refactor(timeline): メイドシェイプレイヤーの編集UIをシェイプキーウィンドウへ委譲する"
```

---

### Task 2: MaidMaterialTimelineLayer（メイドマテリアル）の全面委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs:192-`

**Interfaces:** Task 1 と同様、UI 削除のみ。

- [ ] **Step 1: DrawWindow を委譲ラベルに置き換える**

```csharp
public override void DrawWindow(GUIView view)
{
    // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
    // マテリアルの編集・追跡チェックは SE のマテリアル編集ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("マテリアルの編集はマテリアルウィンドウで行ってください", -1, 20);
}
```

- [ ] **Step 2: `DrawMaterial`（:198）とスロット/マテリアル選択コンボ等の専用フィールドを削除する**
- [ ] **Step 3: Grep で `DrawMaterial` の参照残り 0 件を確認する（同名メソッドが他レイヤーにもあるためファイル内検索）**
- [ ] **Step 4: 両バージョンをビルドする**
- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs
git commit -m "refactor(timeline): メイドマテリアルレイヤーの編集UIをマテリアルウィンドウへ委譲する"
```

---

### Task 3: ModelMaterialTimelineLayer（モデルマテリアル）の操作タブ委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs:210-240` ほか

**Interfaces:**
- Consumes: 基底 `ModelTimelineLayerBase.DrawModelManage(view)`（管理タブは残す）

- [ ] **Step 1: タブを廃止し、ラベル＋管理 UI に置き換える**

`TabType` enum・`_tabType`（:210-218）を削除し、`DrawWindow`（:225）を以下へ:

```csharp
public override void DrawWindow(GUIView view)
{
    // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
    // マテリアルの編集・追跡チェックは SE のマテリアル編集ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("マテリアルの編集はマテリアルウィンドウで行ってください", -1, 20);
    view.DrawHorizontalLine(Color.gray);
    DrawModelManage(view);
}
```

- [ ] **Step 2: `DrawMaterial`（:242）と `_modelComboBox`・`_materialComboBox` 等の専用フィールドを削除する**
- [ ] **Step 3: ファイル内 Grep で参照残り 0 件を確認する**
- [ ] **Step 4: 両バージョンをビルドする**
- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs
git commit -m "refactor(timeline): モデルマテリアルレイヤーの操作タブをマテリアルウィンドウへ委譲する"
```

---

### Task 4: ModelShapeKeyTimelineLayer（モデルシェイプ）の操作タブ委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs:228-`

Task 3 と同構造。`TabType`/`_tabType` を削除し、`DrawWindow`（:228）を:

```csharp
public override void DrawWindow(GUIView view)
{
    // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
    // ブレンドシェイプの編集・キー対象選別は SE のシェイプキー編集ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("シェイプキーの編集はシェイプキーウィンドウで行ってください", -1, 20);
    view.DrawHorizontalLine(Color.gray);
    DrawModelManage(view);
}
```

- [ ] **Step 1: 上記置き換えを行い、`DrawBlendShapes`（:245）と専用フィールドを削除する**
- [ ] **Step 2: ファイル内 Grep で参照残り 0 件を確認する**
- [ ] **Step 3: 両バージョンをビルドする**
- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs
git commit -m "refactor(timeline): モデルシェイプレイヤーの操作タブをシェイプキーウィンドウへ委譲する"
```

---

### Task 5: ModelBoneTimelineLayer（モデルボーン）の操作タブ委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelBoneTimelineLayer.cs:249-`

Task 3 と同構造。`TabType`/`_tabType` を削除し、`DrawWindow`（:249）を:

```csharp
public override void DrawWindow(GUIView view)
{
    // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
    // ボーンの編集・追跡選別は SE のボーン編集ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("ボーンの編集はボーンウィンドウで行ってください", -1, 20);
    view.DrawHorizontalLine(Color.gray);
    DrawModelManage(view);
}
```

- [ ] **Step 1: 上記置き換えを行い、`DrawBone`（:266）と専用フィールド（モデル選択コンボ・操作種類コンボ等）を削除する**
- [ ] **Step 2: ファイル内 Grep で参照残り 0 件を確認する**
- [ ] **Step 3: 両バージョンをビルドする**
- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelBoneTimelineLayer.cs
git commit -m "refactor(timeline): モデルボーンレイヤーの操作タブをボーンウィンドウへ委譲する"
```

---

### Task 6: MorphTimelineLayer（メイド表情）のモーフ編集 UI 委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs:220-316`

**注意:** `_isForceUpdate` は再生適用（:149）と編集書き込み（:190）でも使うレイヤー固有機能なのでトグルは残す。削除するのは編集スライダー群のみ。

- [ ] **Step 1: DrawWindow を「強制上書きトグル＋委譲ラベル」へ置き換える**

`TabType` enum（:220-226）・`_tabType` を削除し、`DrawWindow`（:230）を:

```csharp
public override void DrawWindow(GUIView view)
{
    if (maid == null)
    {
        view.DrawLabel("メイドを配置してください", -1, 20);
        return;
    }

    view.DrawToggle("強制上書き", _isForceUpdate, 150, 20, newValue =>
    {
        _isForceUpdate = newValue;
    });

    view.DrawHorizontalLine(Color.gray);

    // 表情モーフの編集・追跡チェックは SE の表情ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("表情の編集は表情ウィンドウで行ってください", -1, 20);
}
```

- [ ] **Step 2: `DrawMorph`（:253）・`DrawMorphSlider`（:291）・`DrawMorphToggle`（:307）を削除する**

`DrawMorph` 末尾の `faceManager.SetMorphValue(maid, _applyMorphMap)`（:285-288）は描画時プレビューであり、編集経路が表情ウィンドウへ移るため一緒に削除してよい（再生時適用は :117 が担う）。

**削除禁止:** レイヤー内プライベートの `GetMorphValue`（:147）/`SetMorphValue`（:179）は削除しない。`GetMorphValue` はキー生成（`CreateTransformData` :211）で使用中。`SetMorphValue` が削除後に未参照となった場合のみ、その 1 メソッドに限り削除してよい（Grep で確認してから）。

- [ ] **Step 3: ファイル内 Grep で削除メソッドの参照残り 0 件を確認する**
- [ ] **Step 4: 両バージョンをビルドする**
- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs
git commit -m "refactor(timeline): メイド表情レイヤーのモーフ編集UIを表情ウィンドウへ委譲する"
```

---

### Task 7: MotionTimelineLayer（メイドアニメ）の手指/足指タブ委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:854-914, 1153-1247`

**注意:** `timeline.fingerBlendEnabled` はキー適用を制御するレイヤー固有設定なのでトグルは残す。編集タブ（編集/追加）は今回のスコープ外で触らない。

- [ ] **Step 1: TabType の 手指/足指 を単一の 指 タブへ統合する**

`TabType` enum の `手指`・`足指` を `指` 1 つに置き換え、`DrawWindow`（:854）の該当 case を:

```csharp
case TabType.指:
    view.DrawHorizontalLine(Color.gray);

    view.DrawToggle("ブレンド有効", timeline.fingerBlendEnabled, -1, 20, newValue =>
    {
        timeline.fingerBlendEnabled = newValue;
    });

    // 指ブレンドの編集 UI は SE の指ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("指の編集は指ウィンドウで行ってください", -1, 20);
    break;
```

- [ ] **Step 2: `DrawFingerBlend`（:1153）と UI 専用の `FingerSlotNames`・`LegSlotNames` を削除する**

**削除禁止:** `GetBaseFinger` はキーフレーム書き込み経路（:512-544 の `CreateBone`/`UpdateBone`、:1145/:1150 の `GetArmFingerBlend`/`GetLegFingerBlend`）からも呼ばれるため削除しない。`FingerBlendBoneNames`（別配列、キー生成用）も削除しない。`FingerBrendNames` は `DrawFingerBlend` 削除後に未参照となった場合のみ削除する（Grep で確認してから）。
- [ ] **Step 3: ファイル内 Grep で参照残り 0 件を確認する**
- [ ] **Step 4: 両バージョンをビルドする**
- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs
git commit -m "refactor(timeline): メイドアニメレイヤーの指ブレンド編集UIを指ウィンドウへ委譲する"
```

---

### Task 8: UndressTimelineLayer（メイド脱衣）の全面委譲

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/UndressTimelineLayer.cs:108-186`

**根拠:** キー書き込みは `maidCache.IsSlotVisible`（= TBody のマスク/めくれのライブ状態、DressUtils.cs:304-314）を読むため、脱衣ウィンドウ（`MaidUndressController` が `body0.SetMask` / めくれを直接操作）での変更もそのままキー化される。追跡機構の追加は不要。

- [ ] **Step 1: DrawWindow を委譲ラベルに置き換える**

`TabType` enum（:108-114）・`_tabType` を削除し、`DrawWindow`（:118）を:

```csharp
public override void DrawWindow(GUIView view)
{
    // 脱衣の編集 UI は SE の脱衣ウィンドウに委譲する (レイヤー UI 非接続方針)
    view.DrawLabel("脱衣の編集は脱衣ウィンドウで行ってください", -1, 20);
}
```

- [ ] **Step 2: `DrawUndress`（:134）・`DrawUndressByCategory`（:155）・`DrawUndressToggle`（:178）を削除する**
- [ ] **Step 3: ファイル内 Grep で参照残り 0 件を確認する**
- [ ] **Step 4: 両バージョンをビルドする**
- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/UndressTimelineLayer.cs
git commit -m "refactor(timeline): メイド脱衣レイヤーの編集UIを脱衣ウィンドウへ委譲する"
```

---

### Task 9: 調査ドキュメントの追従とホスト側の確認

**Files:**
- Modify: `docs/layer-window-duplication-survey.md`（A 分類を「対応済み」へ更新、BGModelMaterial の除外理由を明記）
- 確認のみ: `source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs`

- [ ] **Step 1: TimelineLayerWindow 側の対応漏れがないか確認する**

`hasSlotNo`（メイド選択コンボ表示）や最小ウィンドウ高の分岐が削除したレイヤー名を参照していないか Grep（`TimelineLayerWindow.cs` 内で各レイヤークラス名を検索）。参照があれば整合させる。

- [ ] **Step 2: `docs/layer-window-duplication-survey.md` の A 分類表へ「対応済み（委譲ラベル化）」の注記と BGModelMaterial 除外理由を追記する**
- [ ] **Step 3: 最終ビルド（両バージョン）を実行し成功を確認する**
- [ ] **Step 4: Commit**

```bash
git add docs/layer-window-duplication-survey.md
git commit -m "docs: レイヤー編集UI委譲化の対応状況を調査ドキュメントへ反映する"
```

---

## レビュー却下メモ

（plan-reviewer の指摘 4 件はすべて取り込み。却下なし。TabType 永続化懸念（確信度: 低）は「_tabType は永続化されない static フィールド」と確認して Architecture 節に明記する形で解消した）
