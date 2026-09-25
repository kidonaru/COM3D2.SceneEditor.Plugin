# メイド表示トグルの退避方式統一 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン操作ウィンドウの「メイド表示」と Inspector ヘッダーのアクティブトグルを、メイドに対しては `MaidManipulateManager.SetVisible`（遠方退避）で切り替えるよう統一し、GitHub issue #2 の「非表示にすると未ロード扱い / アイコンがグレーのまま / 操作不能」を解消する。

**Architecture:** 現状はタイムライン側が `maid.Visible`（`m_visible` + `SetActive`）、Inspector 側が `GameObject.SetActive` を直接叩き、どちらも GameObject を非アクティブにする。これにより `MTEUtils.GetReadyMaidList`（`maid.Visible` で絞る）と `SceneEditorHack.allMaids`（`isActiveAndEnabled` で絞る）からメイドが落ち、各ウィンドウの一覧から消えたりタイムラインの slotNo が詰まったりする。SE 本来の退避方式は GameObject をアクティブのまま位置だけ飛ばすので、両フィルタに残り、既存の `HiddenMaidGuard` などの退避前提ガードも効く。ユーザー操作の窓口を `MaidManipulateManager.SetVisibleByUser` に集約し、Undo は既存の `HistoryScope.Placement`（`PlacementSnapshot`）に表示状態を追加して扱う。

**Tech Stack:** C# (.NET Framework 3.5 / 4.x の 2 構成), Unity IMGUI, 既存の HistoryManager

**Spec:** GitHub issue #2（kidonaru/COM3D2.SceneEditor.Plugin）の不具合 1・3・6 項目。ユーザー決定: 「退避方式へ統一」

## Global Constraints

- コードのコメント・ログは日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること（`dual-gameversion-build`）
- `debug.bat` はゲームフォルダへ DLL をコピーするため、ビルド確認は MSBuild 直叩き
- 一覧フィルタ（`GetReadyMaidList` / `allMaids`）自体は変えない（ゲーム側が `Visible=false` にしたメイドの扱いは本計画の対象外）

## Review Focus

1. 非表示 → Undo → Redo で、位置と表示状態が食い違わない（表示に戻ったのに退避座標のまま、など）
2. 非表示中にタイムラインを再生・シークしても画面に引き戻されない（`MoveTimelineLayer.ApplyPosition` の退避分岐が効くこと）
3. 旧方式で `maid.Visible=false` にされたまま残っているメイドを、トグル ON で確実に表示へ戻せる
4. 非表示中もタイムライン操作の「操作対象」コンボと各メイドウィンドウの一覧に残り、選択できる
5. メイド以外（ライト・モデル等）の Inspector アクティブトグルは従来どおり `SetActive` + Object スコープの履歴

これらは Unity 実体が必要で単体テスト不可（`tests-no-unity-native-calls`）。Task 5 の実機検証で確認する。

---

### Task 1: PlacementSnapshot に表示状態を持たせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PlacementSnapshot.cs`

**Interfaces:**
- Produces: `PlacementSnapshot` が各メイドの `visible` を記録・復元する（`HistoryScope.Placement` の BeforeEdit でそのまま使える）

- [ ] **Step 1: `MaidPlacement` に `visible` を追加し Capture で記録する**

```csharp
private class MaidPlacement
{
    public Maid maid;
    public Vector3 position;
    public Vector3 rotation;
    public bool visible;
}
```

`Capture` の Add に `visible = manager.IsVisible(maid),` を追加。

- [ ] **Step 2: Apply を表示状態込みに書き換える**

```csharp
public void Apply(Maid maid)
{
    var manager = MaidManipulateManager.instance;

    foreach (var placement in _placements)
    {
        var target = placement.maid;
        if (target == null)
        {
            continue;
        }

        // 表示へ戻すなら先に戻す。退避中のまま実座標を書くと戻り先が退避座標で潰れる
        if (placement.visible && !manager.IsVisible(target))
        {
            manager.SetVisible(target, true);
        }

        if (!manager.IsVisible(target))
        {
            // 退避中に実座標を動かすと画面に出てしまうため、戻り先だけ書き換える
            manager.SetRestorePosition(target, placement.position);
            continue;
        }

        target.SetPos(placement.position);
        target.SetRot(placement.rotation);

        // 瞬間移動で揺れ物が取り残されないよう物理をリセットする
        if (target.body0 != null && target.body0.isLoadedBody)
        {
            target.body0.WarpInit();
        }

        // 非表示へ戻すのは位置を合わせてから。退避前の位置が戻り先として控えられる
        if (!placement.visible)
        {
            manager.SetVisible(target, false);
        }
    }
}
```

- [ ] **Step 3: Approximately で visible も比較する**

条件を `a.maid != b.maid || a.position != b.position || a.rotation != b.rotation || a.visible != b.visible` にする。

- [ ] **Step 4: クラスコメントに「表示状態も記録する」旨を追記**

### Task 2: ユーザー操作用の表示切替窓口

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:73-83`

**Interfaces:**
- Consumes: Task 1 の PlacementSnapshot
- Produces: `public void SetVisibleByUser(Maid maid, bool visible)`

- [ ] **Step 1: SetVisible の下に追加**

```csharp
/// <summary>
/// UI からの表示切替。履歴 (配置スコープ) に積んでから退避方式で切り替える。
/// 旧版のトグルは Maid.Visible で GameObject ごと消していたため、
/// その状態で残っているメイドも表示へ戻せるようにゲーム側のフラグも立て直す
/// </summary>
public void SetVisibleByUser(Maid maid, bool visible)
{
    // ロード中は呼出処理が退避で隠しており、完了時に配置位置へ戻す。
    // ここで解くと戻り先が退避座標のまま確定して取り残される
    if (maid == null || IsLoading(maid))
    {
        return;
    }

    HistoryManager.instance.BeforeEdit(null, HistoryScope.Placement,
        (visible ? "表示: " : "非表示: ") + maid.status.fullNameJpStyle);

    // 旧タイムライン側トグル (m_visible=false) と旧 Inspector トグル (SetActive のみ) の
    // どちらで消えたメイドも戻す。Visible の setter は SetActive(true) も兼ねる
    if (visible && (!maid.Visible || !maid.gameObject.activeSelf))
    {
        maid.Visible = true;
    }
    SetVisible(maid, visible);
}
```

### Task 3: タイムライン操作ウィンドウの「メイド表示」

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs`（`DrawToggles` の「メイド表示」）

- [ ] **Step 1: 状態の読み書きを差し替える**

```csharp
var seMaidManager = MaidManipulateManager.instance;
var targetMaid = maidManager.maid;
DrawIconToggle(view, ToolbarIcons.Kind.Maid, "メイド表示",
    seMaidManager.IsVisible(targetMaid) && targetMaid.Visible && targetMaid.gameObject.activeSelf, true, newValue =>
{
    seMaidManager.SetVisibleByUser(targetMaid, newValue);
});
```

`&& targetMaid.Visible` は旧方式で消えたままのメイドを「非表示」と表示してトグル ON で戻せるようにするため。

### Task 4: Inspector ヘッダーのアクティブトグル

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs`（`DrawHeader`）

- [ ] **Step 1: メイド本体のときだけ退避方式へ振り分ける**

```csharp
/// <summary>
/// アクティブトグル + オブジェクト名 + 右端のフォーカスボタンの 1 行。
/// メイド本体は SetActive で消すと Maid.Update が止まりタイムラインの一覧からも外れるため、
/// 表示/非表示を退避方式 (MaidManipulateManager.SetVisibleByUser) で切り替える
/// </summary>
private void DrawHeader(GUIView view, GameObject go)
{
    var maid = go.GetComponent<Maid>();
    if (maid != null)
    {
        var manager = MaidManipulateManager.instance;
        InspectorHeaderRowDrawer.Draw(view, go.activeSelf && maid.Visible && manager.IsVisible(maid),
            go.name, RowHeight, value => manager.SetVisibleByUser(maid, value), go);
        return;
    }

    InspectorHeaderRowDrawer.Draw(view, go.activeSelf, go.name, RowHeight, value =>
    {
        ObjectTransformRowDrawer.RecordEdit(go);
        go.SetActive(value);
    }, go);
}
```

トグル表示の `go.activeSelf && maid.Visible` は、旧方式で消えたままのメイドを OFF として出し、ON 操作で Task 2 の復帰処理を通すため。

- [ ] **Step 2: 退避中メイドの Transform 行をガードする**

`DrawContent` の既定描画（`DrawHeader(_view, go);` の直後）で、退避中メイドなら Transform 行を描かず警告を出す。実座標は退避先なので、ここで書くと再表示時に戻り先で上書きされて編集が消える。

```csharp
DrawHeader(_view, go);

if (maid == null || !HiddenMaidGuard.DrawWarningIfHidden(
        _view, maid, "非表示中は位置を操作できません", RowHeight))
{
    _objectTransformRowDrawer.Draw(
        _view, go, LabelWidth, ScaleLabelWidth, RowHeight);
}
```

### Task 5: ビルド・実機検証・ドキュメント

**Files:**
- Modify: `docs-site/timeline/control.md:80`（`メイド表示` の説明）

- [ ] **Step 1: 両構成ビルド**（`msbuild-from-bash` の手順。COM3D2 → COM3D25）
- [ ] **Step 2: 実機検証**（`com3d25-restart-verify`。メイド 2 人以上）
  - タイムライン操作で非表示 → 表情/IK/指/Inspector のメイド一覧に残る、操作対象コンボに残り選べる
  - Inspector で表示へ戻す → タイムライン操作のアイコンが ON になる（逆順も）
  - 非表示 → Ctrl+Z で表示・元位置、Ctrl+Y で再び非表示
  - 非表示のままタイムライン再生 → 画面に戻らない
  - ライトの Inspector トグルが従来どおり動く
  - 非表示中のメイドを Inspector で選ぶと Transform 行の代わりに警告が出る
  - 呼出直後（ロード完了前）にトグルを操作しても配置位置に正しく出る（ロード中は無視される）
  - 2 人を非表示にしてシーンプリセット保存 → 読込で、両トグルの表示が一致する
  - 仕様: 旧方式由来の非表示を ON にした操作を Undo しても、非表示には戻らない（新方式へ一方向移行）
- [ ] **Step 3: docs-site の説明を「メイドの表示 / 非表示（非表示中はポーズを編集できません）」へ更新**
- [ ] **Step 4: code-review → commit**

## レビュー却下メモ

- 旧方式で非表示化済みのメイドがリロード後に UI から再選択できない（🔴） — `Maid.Visible` / `SetActive` は実行時状態で保存されず、本修正の DLL 反映にはゲーム再起動が要るため、旧トグル由来の状態は持ち越されない。ゲームや他プラグインが隠したメイドは本計画の対象外（Global Constraints）
