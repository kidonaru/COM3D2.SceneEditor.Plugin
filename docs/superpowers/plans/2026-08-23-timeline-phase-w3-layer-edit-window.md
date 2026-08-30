# Phase W3: レイヤー編集ウィンドウ（DrawWindow 接続）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: 親ワークスペースの標準フローに従い superpowers:executing-plans でタスク単位に実装する（subagent-driven-development は使わない）。ステップはチェックボックス（`- [ ]`）で追跡する。

**Goal:** 移植済みでありながら未接続だった各タイムラインレイヤーの編集 UI（`ITimelineLayer.DrawWindow`）を、汎用のホストウィンドウ 1 枚から描画できるようにし、全 28 レイヤーのキーフレーム編集を GUI で行える状態にする。

**Architecture:** 新規 `TimelineLayerWindow`（`EditorSubWindow` 派生）を追加し、ヘッダーでレイヤーと操作対象メイドを選び、本体で `timelineManager.currentLayer.DrawWindow(view)` を呼ぶだけの薄いホストにする。各レイヤーの `DrawWindow` は既に SE 向けに調整済み（コンボのポップアップはホスト側の `ComboBoxPopupWindow` が描く前提でコメント済み。`Timeline/TimelineLayer/PsylliumTimelineLayer.cs:829`）で、内部で自前のスクロールビューを張るため、ホスト側ではスクロールビューで包まない。

**Tech Stack:** C# 7.3 / .NET 3.5 相当（UnityInjector プラグイン）、IMGUI（MTEUtils の `GUIView` / `GUIComboBox`）

**Spec:** `docs/superpowers/specs/timeline-editing-roadmap.md`（Phase W3 / W4。本計画で方針を転換する）

## 方針転換の経緯（2026-08-23）

ロードマップは当初「MTE の TimelineLayer サブウィンドウは接続せず、レイヤー固有の編集は SE 各ウィンドウへ吸収する」と決めていた。しかし Phase W3 の着手時に調査したところ、次が判明した:

- SE 側の **全 28 レイヤーに `DrawWindow` の実装が移植済み**（`Timeline/TimelineLayer/*.cs`）
- ところが **呼び出し箇所が 1 つも無い**（`grep "\.DrawWindow("` が 0 件）ため、コードが丸ごと死んでいる
- 実装は SE 向けに調整済み（コンボのポップアップ処理をホスト側へ委ねるコメントが入っている）

そのため、書き直し（Psyllium 1794 行を含む 3700 行超の再実装）ではなく、ホストウィンドウを 1 枚用意して既存実装へ接続する方針へ切り替える（ユーザー判断、2026-08-23）。SE ネイティブなウィンドウへの置き換えは、需要の高い領域から段階的に判断する。

## Global Constraints

- ブランチは `feature/timeline-window` のまま。worktree は使わない
- csproj は非 SDK 形式の手動管理。新規 `.cs` は必ず `<Compile Include="..." />` を追加する
- コメント・ラベル・エラーメッセージは日本語
- ビルドは MSBuild を直接叩く（`debug.bat` は実機へ DLL をコピーするため使わない）:
  `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- テストは `dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj`
- **ホスト側でスクロールビューを張らない**。各レイヤーの `DrawWindow` が内部で `BeginScrollView` を呼ぶものが多く（24 ファイル）、`GUIView` はネストしたスクロールビューに対応しない（`MTEUtils/GUIView.cs:300-304`）
- レイヤー側のコード（`Timeline/TimelineLayer/*.cs`）は本 Phase では変更しない。描画が破綻するレイヤーが出た場合も、まず記録に留めて別タスクで直す
- **内部スクロールを持たないレイヤーは、ウィンドウが低いと下部の項目に届かない**。`DrawWindow` から到達する範囲で `BeginScrollView` を一切呼ばないのは次の 10 系統:
  `BGColor` / `BG` / `Camera` / `Morph` / `Move` / `PngPlacement` / `SubCamera` / `Undress` / `Voice` / `PostEffect`（DepthOfField・GTToneMap 派生を含む）。
  特に `SubCameraTimelineLayer.DrawWindow` は 16〜18 行（およそ 320〜380px）を描く。
  ホスト側でスクロールを張ると内部スクロールを持つレイヤーが二重スクロールになるため、
  本 Phase では既定サイズと最小サイズを大きめに取って回避し、実機確認で不足を確認する

## File Structure

| ファイル | 責務 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs`（新規） | レイヤー編集 UI のホスト。レイヤー / 操作対象メイドの選択と `DrawWindow` の呼び出し |
| `source/COM3D2.SceneEditor.Plugin/Config.cs`（変更） | `timelineLayer*` の配置・表示状態フィールド追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`（変更） | ウィンドウ登録 |
| `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`（変更） | Window メニューへの項目追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（変更） | 「編集」ボタン追加 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（変更） | Compile Include 追加 |
| `docs/superpowers/specs/timeline-editing-roadmap.md`（変更） | 方針転換と Phase W3 の記録、W4 の扱いの見直し |

---

### Task 1: TimelineLayerWindow の実装と登録

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `EditorSubWindow`、`GUIView`、`GUIComboBox<T>`、`ComboBoxPopupWindow.instance.ProcessFocus(GUIView, EditorSubWindow)`、`timelineManager.currentLayer`（`MTEP.ITimelineLayer`）/ `usingLayerInfoList` / `GetLayerInfo(Type)` / `ChangeActiveLayer(Type, int)`、`maidManager.maidCaches` / `ChangeMaid(Maid)` / `maidSlotNo`、`ITimelineLayer.DrawWindow(GUIView)` / `hasSlotNo` / `slotNo` / `layerType`
- Produces: `TimelineLayerWindow.instance`、`TimelineLayerWindow.WINDOW_ID = 8903387`

- [ ] **Step 1: Config に配置フィールドを追加する**

`Config.cs` の `timelineLoadVisible` の直後に追記:

```csharp
        // レイヤー編集ウィンドウ
        public int timelineLayerPosX = -1;
        public int timelineLayerPosY = -1;
        public int timelineLayerWidth = 480;
        public int timelineLayerHeight = 560;
        public bool timelineLayerVisible = false;
```

- [ ] **Step 2: ホストウィンドウを実装する**

`source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs` を新規作成:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤー固有の編集 UI (ITimelineLayer.DrawWindow) を描くホストウィンドウ。
    /// 中身は各レイヤーの実装に委ねる薄いホストで、
    /// ここではレイヤーと操作対象メイドの選択だけを持つ
    /// </summary>
    public class TimelineLayerWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903387;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "レイヤー編集";
        // 内部スクロールを持たないレイヤー (SubCamera 等) の項目が隠れないよう、
        // 既定の下限 (200x160) より大きく取る
        protected override int minWidth => 400;
        protected override int minHeight => 400;

        private static readonly int ROW_HEIGHT = 20;

        private readonly GUIView _view = new GUIView();

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;

        private readonly GUIComboBox<MTEP.TimelineLayerInfo> _layerComboBox
            = new GUIComboBox<MTEP.TimelineLayerInfo>
        {
            getName = (layerInfo, index) => layerInfo.displayName,
            onSelected = (layerInfo, index) =>
            {
                timelineManager.ChangeActiveLayer(layerInfo.layerType, maidManager.maidSlotNo);
            },
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            onSelected = (maidCache, index) =>
            {
                maidManager.ChangeMaid(maidCache.maid);
            },
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private static TimelineLayerWindow _instance = null;
        public static TimelineLayerWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineLayerWindow();
                }
                return _instance;
            }
        }

        private TimelineLayerWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineLayerPosX;
            y = config.timelineLayerPosY;
            width = config.timelineLayerWidth;
            height = config.timelineLayerHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineLayerPosX = x;
            config.timelineLayerPosY = y;
            config.timelineLayerWidth = width;
            config.timelineLayerHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineLayerVisible;
            set => config.timelineLayerVisible = value;
        }

        protected override void DrawContent()
        {
            DrawBody();

            // 各レイヤーの DrawWindow はコンボのポップアップ描画をホストに委ねているため、
            // 早期 return しても飛ばさないよう本体とは分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null || currentLayer == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            DrawHeader();

            // レイヤーの DrawWindow は内部で自前のスクロールビューを張るため、
            // ここでは包まない (ネストしたスクロールは GUIView が非対応)
            currentLayer.DrawWindow(_view);
        }

        /// <summary>編集対象のレイヤーと操作対象メイドの選択</summary>
        private void DrawHeader()
        {
            _view.BeginHorizontal();
            {
                _view.margin = 0;

                _layerComboBox.currentItem = timelineManager.GetLayerInfo(currentLayer.layerType);
                _layerComboBox.items = timelineManager.usingLayerInfoList;
                _layerComboBox.DrawButton("レイヤー", _view);

                if (currentLayer.hasSlotNo)
                {
                    _view.AddSpace(10);
                    _view.DrawLabel("操作対象", 60, ROW_HEIGHT);

                    _maidComboBox.currentIndex = currentLayer.slotNo;
                    _maidComboBox.items = maidManager.maidCaches;
                    _maidComboBox.DrawButton(_view);
                }

                _view.margin = GUIView.defaultMargin;
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
        }
    }
}
```

- [ ] **Step 3: 登録する**

`Manager/WindowManager.cs` の `AddWindow(TimelineLoadWindow.instance);` の直後:

```csharp
            AddWindow(TimelineLayerWindow.instance);
```

`MenuBarWindow.cs` の `CreateWindowItem("タイムラインロード", TimelineLoadWindow.instance),` の直後:

```csharp
                        CreateWindowItem("レイヤー編集", TimelineLayerWindow.instance),
```

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="TimelineLoadWindow.cs" />` の直前:

```xml
    <Compile Include="TimelineLayerWindow.cs" />
```

- [ ] **Step 4: TimelineWindow へ導線を追加する**

`TimelineWindow.cs` の `DrawControlPanel` にある「一覧」ボタンの直後:

```csharp
                if (view.DrawButton("編集", 50, 20))
                {
                    WindowManager.ToggleWindowVisible(TimelineLayerWindow.instance);
                }
```

- [ ] **Step 5: 参照している API を実装に合わせて確認する**

Run:
```
grep -n "usingLayerInfoList\|GetLayerInfo\|ChangeActiveLayer" source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
grep -n "hasSlotNo\|slotNo\|DrawWindow" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs
grep -n "maidCaches\|ChangeMaid\|maidSlotNo" source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaidManager.cs
```
Expected: いずれも存在する（`TimelineWindow.DrawControlPanel` が同じ組み合わせで使っている）。差異があれば `TimelineWindow.cs` の呼び出しに合わせる。

- [ ] **Step 6: ビルドしてテストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj
```
Expected: ビルド成功、全テスト PASS

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin
git commit -m "feat(timeline): レイヤー編集ウィンドウを追加して DrawWindow を接続"
```

---

### Task 2: 実機確認とドキュメント更新

**Files:**
- Modify: `docs/superpowers/specs/timeline-editing-roadmap.md`

- [ ] **Step 1: 実機で確認する**

ゲームを再起動し、レイヤー編集ウィンドウを開いて次を確認する。Phase W1 / W2 の未確認項目も併せて消化する:

- レイヤーコンボで切り替えると、そのレイヤーの編集 UI が出る
- 演出系（Psyllium / StageLight / StageLaser）のタブ切り替え・パラメータ編集が動く
- マテリアル / シェイプキー / ポストエフェクト / Se / Voice / Text / BGModel / SubCamera の各レイヤーで編集 UI が出る
- コンボボックスのポップアップが正しい位置に出て、選択が反映される
- レイヤー内のスクロールが効く（ホスト側と二重スクロールになっていない）
- 内部スクロールを持たないレイヤー（特に SubCamera / Camera / PostEffect）で、既定ウィンドウサイズのまま下部の項目まで操作できる
- 操作対象メイドの切り替えが効く（`hasSlotNo` のレイヤー）

破綻するレイヤーがあれば、レイヤー名と症状をロードマップへ記録する（本 Phase では直さない）。

- [ ] **Step 2: ロードマップを更新する**

`docs/superpowers/specs/timeline-editing-roadmap.md` を次のように書き換える:

1. 「1. 現状 → 完了していること」の TimelineLayer 行を「接続しない」から「汎用ホスト（`TimelineLayerWindow`）で接続（2026-08-23 方針転換）」へ更新する
2. 「B. レイヤー個別編集の受け皿が無い領域」の表に、DrawWindow 接続で編集可能になった旨を追記する（受け皿なしだった演出系・マテリアル・シェイプキー・ポストエフェクト・Se・Voice・Text・BGColor・BGModel・SubCamera）
3. Phase W3 / W4 の位置づけを「SE ネイティブなウィンドウへの置き換え（任意）」へ格下げし、需要の高い領域から段階的に判断すると記す
4. Step 1 で見つかった破綻レイヤーがあれば課題として列挙する

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/timeline-editing-roadmap.md
git commit -m "docs(timeline): レイヤー編集 UI の方針転換と Phase W3 の完了を記録"
```

---

## 実装後の必須ステップ

- `code-review` スキルでコードレビューを行い、指摘を取り込んでからユーザーへ提示する
