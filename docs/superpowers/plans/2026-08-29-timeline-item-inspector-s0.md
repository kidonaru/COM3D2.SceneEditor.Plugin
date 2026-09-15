# タイムライン項目 ⇔ Inspector 連携 Phase S0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (このリポジトリでは subagent-driven-development は使用禁止)

**Goal:** タイムラインのボーンメニューで選択した項目(表情モーフ・ボーン)の現在値編集UIを Inspector に表示し、Inspector 側のボーン選択をタイムラインのメニュー選択へ逆同期する基盤 + パイロット 2 レイヤー(Morph / Motion)を実装する。

**Architecture:** レイヤー型 → `ITimelineItemInspector` の登録制(`TimelineItemInspectorRegistry`)、InspectorWindow への表示分岐追加(キーフレーム選択の次位)、双方向同期の `TimelineSelectionBridge`(MTEP.ManagerBase 派生、`TimelineIntegration._managers` へ追加)。描画は MaidFaceWindow / InspectorWindow から行描画を静的ヘルパー(`FaceMorphRowDrawer` / `BoneSliderRowDrawer`)へ抽出して共有する。レイヤー本体(MTE 逐語コピー)には手を入れない。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI 風 GUIView)、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** `docs/superpowers/specs/timeline-item-inspector-roadmap.md`(Phase S0 節)

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree 禁止。作業はメイン作業ディレクトリの `feature/timeline-window` ブランチ
- ビルド確認(COM3D25 構成):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- ビルド確認(COM3D2 構成。対象フレームワークが違うため必ず両方通す):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- `debug.bat` はゲームフォルダへ DLL をコピーするため使わない。`deploy.bat` / `release.bat` / `deploy.ps1` は絶対に実行しない
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`(既存テストを退行させない)。テストは COM3D25 構成のビルド出力を参照するため、テスト実行前に必ず COM3D25 構成でビルドしておくこと
- 新規ファイルは `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ追加する(SDK スタイルではないので自動では拾われない)。テストプロジェクトは SDK スタイルなので追加不要
- テスト名前空間 `COM3D2.SceneEditor.Plugin.Tests` 内では親名前空間の SE 側 `PluginUtils` がファイルスコープ using エイリアスより優先される → 既存テストと同じく `MTEP = COM3D2.MotionTimelineEditor.Plugin` 等の別名エイリアスを使う
- コミットフッター:
  ```
  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Y79LYcCQZJFUbFvMWLaaKr
  ```

## データモデルの前提知識(全タスク共通)

- メニュー項目は `MTEP.IBoneMenuItem`(`Timeline/BoneMenu/IBoneMenuItem.cs`)。`name` / `displayName` / `isSelectedMenu` / `isSetMenu` / `children` を持つ。選択中項目一覧は `MTEP.BoneMenuManager.Instance.GetSelectedItems()`(セット行は「全子選択」時に自身も含まれ、子も個別に含まれる)
- `MTEP.BoneMenuManager.Instance.UnselectAll()` は**現在レイヤーの**全項目を非選択にする(`allMenuItems` は `currentLayer.allMenuItems` 
 のエイリアス)
- 簡易表示(`MTEP.ConfigManager.instance.config.isEasyEdit`)では `GetSelectedItems()` が疑似項目 `EasyMenuItem` を常に返すため、項目 Inspector の対象外とする
- `MTEP.ITimelineLayer` は `maid` / `maidCache` / `allMenuItems` を公開している(`Timeline/TimelineLayer/ITimelineLayer.cs`)
- MorphTimelineLayer のメニュー項目 `name` はモーフ名(例: `eyeclose`)、MotionTimelineLayer は標準ボーン名(例: `Bip01 Head`、`MaidBoneMenuItem`)と拡張ボーン名(`ExtendBoneMenuItem`)
- SE 側表情モーフ定義は `FaceMorphDef`(`MaidManipulation/MaidFaceMorphController.cs:17`、`name`/`displayName`/`isToggle`)。`name` はタイムライン側モーフ名と同じキー体系(`eyeclose` 等)
- SE 側ボーンスライダー定義は `BoneSliderDef`(`MaidManipulation/MaidBoneSliderController.cs:14`、`boneName`/`displayName`/`axes`)。`MaidBoneSliderController.FindDef(boneName)` で引ける(該当なしは null)
- `SelectionManager`(`Manager/SelectionManager.cs`): `selectedObject` / `selectedBoneMaid` / `selectedBoneDef` / `hasBoneSelection` / `hasIKSelection`。`Select(go)` は**ボーン/IK 選択を解除してから**同値早期 return する(= `Select(selectedObject)` で「ボーン/IK 選択だけ降格」できる)。ボーン選択変更はイベントを発火しない(`onSelectionChanged` は `selectedObject` の変化時のみ)ため、逆方向同期はポーリングで検知する
- `MaidBoneMenuItem.isSelectedMenu` の setter は `studioHack.SetBoneRotateVisible` への書き込み経路を持つが、**このリポジトリの `SceneEditorHack` は `HasBoneRotateVisible` をオーバーライドしていない(既定 false)ため現状この経路は発火せず、単純なフラグとして動く**。将来別の StudioHackBase 実装が追加された場合のみ再考する
- `TimelineSelectionBridge` は `TimelineIntegration` の `_managers` に載るため、`Update()` は `UpdateGuards()`(hack 有効 + MTE 側メイド解決済み + タイムラインデータ有効)を通過したフレームでしか呼ばれない。**タイムライン未読込・メイド未解決時に双方向同期が動かないのは意図した制約**(タイムライン機能自体が動いていないため)

## Task 1: ITimelineItemInspector + Registry(葉項目展開の純粋ロジック含む)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ITimelineItemInspector.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspectorRegistry.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineItemInspectorRegistryTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(`Timeline\BoneMenu\...` 群の近く、335 行付近に Compile Include 追加)

**Interfaces:**
- Produces: `ITimelineItemInspector { void DrawItems(GUIView, MTEP.ITimelineLayer, IList<MTEP.IBoneMenuItem>); string FindItemName(MTEP.ITimelineLayer); }`
- Produces: `TimelineItemInspectorRegistry.Register(Type, ITimelineItemInspector)` / `Find(Type)` / `Find(MTEP.ITimelineLayer)` / `CollectLeafItems(IList<MTEP.IBoneMenuItem>, List<MTEP.IBoneMenuItem>)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineItemInspectorRegistryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineItemInspectorRegistryTests
    {
        /// <summary>テスト用の最小メニュー項目。描画系メソッドは使わない</summary>
        private class FakeMenuItem : MTEP.IBoneMenuItem
        {
            public string name { get; set; }
            public string displayName { get; set; }
            public bool isSelectedMenu { get; set; }
            public bool isVisibleMenu { get; set; }
            public bool isOpenMenu { get; set; }
            public bool isSetMenu => children != null;
            public MTEP.IBoneMenuItem parent { get; set; }
            public List<MTEP.IBoneMenuItem> children { get; set; }

            public void SelectMenu(bool isMultiSelect) { }
            public bool HasVisibleBone(MTEP.FrameData frame) => false;
            public bool IsFullBones(MTEP.FrameData frame) => false;
            public bool IsTargetBone(MTEP.BoneData bone) => false;
            public bool IsSelectedFrame(MTEP.FrameData frame) => false;
            public void SelectFrame(MTEP.FrameData frame, bool isMultiSelect) { }
            public void AddKey() { }
            public void RemoveKey() { }
        }

        private class FakeInspector : ITimelineItemInspector
        {
            public void DrawItems(
                COM3D2.MotionTimelineEditor.GUIView view,
                MTEP.ITimelineLayer layer,
                IList<MTEP.IBoneMenuItem> items) { }
            public string FindItemName(MTEP.ITimelineLayer layer) => null;
        }

        [Fact]
        public void Register_と_Find_で登録したプロバイダを型で引ける()
        {
            var inspector = new FakeInspector();
            TimelineItemInspectorRegistry.Register(typeof(string), inspector);
            Assert.Same(inspector, TimelineItemInspectorRegistry.Find(typeof(string)));
            Assert.Null(TimelineItemInspectorRegistry.Find(typeof(int)));
        }

        [Fact]
        public void Register_同じ型の再登録は置き換える()
        {
            var first = new FakeInspector();
            var second = new FakeInspector();
            TimelineItemInspectorRegistry.Register(typeof(double), first);
            TimelineItemInspectorRegistry.Register(typeof(double), second);
            Assert.Same(second, TimelineItemInspectorRegistry.Find(typeof(double)));
        }

        [Fact]
        public void CollectLeafItems_セット行は子へ展開し重複を除いて葉だけ返す()
        {
            var childA = new FakeMenuItem { name = "a" };
            var childB = new FakeMenuItem { name = "b" };
            var set = new FakeMenuItem
            {
                name = "set",
                children = new List<MTEP.IBoneMenuItem> { childA, childB },
            };
            var single = new FakeMenuItem { name = "c" };

            // GetSelectedItems はセットと子が両方含まれる形で返すので、その形を入力にする
            var source = new List<MTEP.IBoneMenuItem> { set, childA, childB, single };
            var result = new List<MTEP.IBoneMenuItem>();
            TimelineItemInspectorRegistry.CollectLeafItems(source, result);

            Assert.Equal(new MTEP.IBoneMenuItem[] { childA, childB, single }, result);
        }

        [Fact]
        public void CollectLeafItems_呼び出しごとに結果リストをクリアする()
        {
            var item = new FakeMenuItem { name = "a" };
            var result = new List<MTEP.IBoneMenuItem> { new FakeMenuItem { name = "stale" } };
            TimelineItemInspectorRegistry.CollectLeafItems(
                new List<MTEP.IBoneMenuItem> { item }, result);
            Assert.Equal(new MTEP.IBoneMenuItem[] { item }, result);
        }
    }
}
```

- [ ] **Step 2: テストが失敗する(コンパイルエラーになる)ことを確認**

先に COM3D25 構成をビルドしてから実行する:

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: `ITimelineItemInspector` / `TimelineItemInspectorRegistry` 未定義でテストプロジェクトがコンパイル失敗

- [ ] **Step 3: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ITimelineItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのボーンメニュー項目選択に対応する Inspector 表示のプロバイダ。
    /// レイヤー型ごとに TimelineItemInspectorRegistry へ登録する
    /// (spec: docs/superpowers/specs/timeline-item-inspector-roadmap.md)
    /// </summary>
    public interface ITimelineItemInspector
    {
        /// <summary>選択中メニュー項目(セット行は子へ展開済み)の現在値編集UIを描く</summary>
        void DrawItems(GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items);

        /// <summary>
        /// SelectionManager の現在の選択に対応するメニュー項目名を返す(逆方向同期用)。
        /// 対応する項目が無ければ null
        /// </summary>
        string FindItemName(MTEP.ITimelineLayer layer);
    }
}
```

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspectorRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>レイヤー型 → ITimelineItemInspector の登録辞書</summary>
    public static class TimelineItemInspectorRegistry
    {
        private static readonly Dictionary<Type, ITimelineItemInspector> _map =
            new Dictionary<Type, ITimelineItemInspector>();

        /// <summary>登録する。同じ型の再登録は置き換え</summary>
        public static void Register(Type layerType, ITimelineItemInspector inspector)
        {
            _map[layerType] = inspector;
        }

        public static ITimelineItemInspector Find(Type layerType)
        {
            ITimelineItemInspector inspector;
            return _map.TryGetValue(layerType, out inspector) ? inspector : null;
        }

        public static ITimelineItemInspector Find(MTEP.ITimelineLayer layer)
        {
            return layer != null ? Find(layer.GetType()) : null;
        }

        /// <summary>
        /// 選択中メニュー項目からセット行を子へ展開し、重複を除いた葉項目だけを result へ集める。
        /// BoneMenuManager.GetSelectedItems はセット行(全子選択時)と子項目の両方を含むため、
        /// 表示前にこの正規化を通す
        /// </summary>
        public static void CollectLeafItems(
            IList<MTEP.IBoneMenuItem> source, List<MTEP.IBoneMenuItem> result)
        {
            result.Clear();
            foreach (var item in source)
            {
                if (item.isSetMenu)
                {
                    if (item.children == null)
                    {
                        continue;
                    }
                    foreach (var child in item.children)
                    {
                        if (!result.Contains(child))
                        {
                            result.Add(child);
                        }
                    }
                }
                else if (!result.Contains(item))
                {
                    result.Add(item);
                }
            }
        }
    }
}
```

csproj の `<Compile Include="Timeline\BoneMenu\BoneMenuItem.cs" />`(335 行付近)の前に追加:

```xml
    <Compile Include="Timeline\ItemInspector\ITimelineItemInspector.cs" />
    <Compile Include="Timeline\ItemInspector\TimelineItemInspectorRegistry.cs" />
```

- [ ] **Step 4: ビルドしてテストが通ることを確認**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: 新規 4 テストを含め全件 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TimelineItemInspectorRegistryTests.cs
git commit -m "feat(timeline): タイムライン項目Inspectorのプロバイダ登録基盤を追加"
```

## Task 2: TimelineItemInspector ファサード + InspectorWindow 分岐

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:154-162`(KeyFrameInspector 分岐の直後に else-if を追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 1 の `TimelineItemInspectorRegistry.Find(MTEP.ITimelineLayer)` / `CollectLeafItems`
- Produces: `TimelineItemInspector.ShouldDraw()` / `TimelineItemInspector.Draw(GUIView view)`(static。KeyFrameInspector と同じ呼び出し流儀)

- [ ] **Step 1: TimelineItemInspector を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのメニュー項目選択を Inspector に表示するファサード。
    /// 表示の優先順位は InspectorWindow 側の分岐が決める
    /// (キーフレーム選択 = KeyFrameInspector が本クラスより優先)
    /// </summary>
    public static class TimelineItemInspector
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.BoneMenuManager boneMenuManager => MTEP.BoneMenuManager.Instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static readonly List<MTEP.IBoneMenuItem> _leafItems =
            new List<MTEP.IBoneMenuItem>(64);

        /// <summary>メニュー項目選択中で、現在レイヤーのプロバイダが居るか</summary>
        public static bool ShouldDraw()
        {
            if (timelineManager.timeline == null || timelineManager.currentLayer == null)
            {
                return false;
            }
            // 簡易表示の GetSelectedItems は疑似項目 EasyMenuItem を常に返すため対象外
            if (timelineConfig.isEasyEdit)
            {
                return false;
            }
            if (TimelineItemInspectorRegistry.Find(timelineManager.currentLayer) == null)
            {
                return false;
            }
            return boneMenuManager.GetSelectedItems().Count > 0;
        }

        public static void Draw(GUIView view)
        {
            var layer = timelineManager.currentLayer;
            var inspector = TimelineItemInspectorRegistry.Find(layer);
            if (inspector == null)
            {
                return;
            }

            TimelineItemInspectorRegistry.CollectLeafItems(
                boneMenuManager.GetSelectedItems(), _leafItems);
            inspector.DrawItems(view, layer, _leafItems);
        }
    }
}
```

csproj へ追加(Task 1 で追加した行の隣):

```xml
    <Compile Include="Timeline\ItemInspector\TimelineItemInspector.cs" />
```

- [ ] **Step 2: InspectorWindow に分岐を追加する**

`InspectorWindow.cs` の KeyFrameInspector 分岐(154-162 行)の直後、`else if (go == null)` の前に挿入:

```csharp
            else if (TimelineItemInspector.ShouldDraw())
            {
                // タイムラインのメニュー項目選択(キーフレーム未選択時)は現在値の編集UIを出す
                _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
                DrawGizmoHeader(_view);
                TimelineItemInspector.Draw(_view);
                _view.EndScrollView();
            }
```

- [ ] **Step 3: 両構成でビルドが通ることを確認**

Global Constraints の MSBuild 2 コマンドを実行。Expected: どちらも Build succeeded

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): Inspectorにタイムライン項目選択の表示分岐を追加"
```

## Task 3: TimelineSelectionBridge(双方向同期)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineSelectionBridge.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:25-52`(`_managers` 配列へ追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 1 の `TimelineItemInspectorRegistry.Find` と `ITimelineItemInspector.FindItemName`
- Produces: `TimelineSelectionBridge.instance`(`MTEP.ManagerBase` 派生。`Update()` で同期)

- [ ] **Step 1: TimelineSelectionBridge を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineSelectionBridge.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのメニュー項目選択と SelectionManager の双方向同期。
    /// - 順方向: メニュー選択の変化を検知し、Inspector のボーン/IK 選択を降格して
    ///   項目表示(TimelineItemInspector)が見えるようにする
    /// - 逆方向: SelectionManager の変化をポーリングで検知し、プロバイダの逆引きで
    ///   該当メニュー項目を選択状態にする
    /// ボーン選択の変化はイベントが無いためポーリングで検知する
    /// </summary>
    public class TimelineSelectionBridge : MTEP.ManagerBase
    {
        private static TimelineSelectionBridge _instance = null;
        public static TimelineSelectionBridge instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineSelectionBridge();
                }
                return _instance;
            }
        }

        private TimelineSelectionBridge()
        {
        }

        // 逆方向検知用の前回値
        private GameObject _lastSelectedObject = null;
        private BoneSliderDef _lastBoneDef = null;

        // 順方向検知用のメニュー選択スナップショット (現在レイヤーの選択集合)
        private MTEP.ITimelineLayer _lastLayer = null;
        private readonly List<MTEP.IBoneMenuItem> _lastSelectedItems =
            new List<MTEP.IBoneMenuItem>(64);

        // 逆方向同期でメニューを書き換えた直後は順方向の降格を 1 回抑止する
        private bool _suppressForwardOnce = false;

        public override void Update()
        {
            if (timelineManager.timeline == null || timelineManager.currentLayer == null)
            {
                return;
            }

            UpdateReverseSync();
            UpdateForwardSync();
        }

        /// <summary>SelectionManager の変化 → メニュー選択</summary>
        private void UpdateReverseSync()
        {
            var selection = SelectionManager.instance;
            var selectedObject = selection.selectedObject;
            var boneDef = selection.selectedBoneDef;

            if (selectedObject == _lastSelectedObject && boneDef == _lastBoneDef)
            {
                return;
            }
            _lastSelectedObject = selectedObject;
            _lastBoneDef = boneDef;

            var layer = timelineManager.currentLayer;
            var provider = TimelineItemInspectorRegistry.Find(layer);
            if (provider == null)
            {
                return;
            }

            var itemName = provider.FindItemName(layer);
            if (itemName == null)
            {
                return;
            }

            var item = FindMenuItem(layer, itemName);
            if (item == null || item.isSelectedMenu)
            {
                return;
            }

            boneMenuManager.UnselectAll();
            // MaidBoneMenuItem のボーン回転表示連動は SceneEditorHack では発火しない
            // (HasBoneRotateVisible が既定 false のため単純フラグとして動く)
            item.isSelectedMenu = true;
            _suppressForwardOnce = true;
        }

        /// <summary>メニュー選択の変化 → Inspector のボーン/IK 選択を降格</summary>
        private void UpdateForwardSync()
        {
            var layer = timelineManager.currentLayer;
            var selectedItems = boneMenuManager.GetSelectedItems();

            if (!MenuSelectionChanged(layer, selectedItems))
            {
                return;
            }

            _lastLayer = layer;
            _lastSelectedItems.Clear();
            _lastSelectedItems.AddRange(selectedItems);

            var suppress = _suppressForwardOnce;
            _suppressForwardOnce = false;
            if (suppress || selectedItems.Count == 0)
            {
                return;
            }

            var selection = SelectionManager.instance;
            if (selection.hasBoneSelection || selection.hasIKSelection)
            {
                // 同一オブジェクトの再選択でボーン/IK 選択だけを解除する
                // (SelectionManager.Select は同値早期 return の前にボーン/IK を解除する)
                selection.Select(selection.selectedObject);
            }
        }

        private bool MenuSelectionChanged(
            MTEP.ITimelineLayer layer, List<MTEP.IBoneMenuItem> selectedItems)
        {
            if (layer != _lastLayer || selectedItems.Count != _lastSelectedItems.Count)
            {
                return true;
            }
            for (var i = 0; i < selectedItems.Count; i++)
            {
                if (!ReferenceEquals(selectedItems[i], _lastSelectedItems[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>現在レイヤーのメニュー項目(セットの子を含む)を名前で探す</summary>
        private static MTEP.IBoneMenuItem FindMenuItem(
            MTEP.ITimelineLayer layer, string itemName)
        {
            foreach (var item in layer.allMenuItems)
            {
                if (!item.isSetMenu && item.name == itemName)
                {
                    return item;
                }
                if (item.children == null)
                {
                    continue;
                }
                foreach (var child in item.children)
                {
                    if (child.name == itemName)
                    {
                        return child;
                    }
                }
            }
            return null;
        }
    }
}
```

注意: `ManagerBase`(`Timeline/Manager/ManagerBase.cs`)は `timelineManager` / `boneMenuManager` 等の protected プロパティを提供している。実装時に存在しないプロパティがあれば `MTEP.TimelineManager.instance` / `MTEP.BoneMenuManager.Instance` を直接参照する形に読み替えること。

- [ ] **Step 2: TimelineIntegration の _managers へ追加する**

`Timeline/TimelineIntegration.cs` の `_managers` 配列末尾(`MTEP.TimelineTemplateManager.instance,` の後)へ追加:

```csharp
                MTEP.TimelineTemplateManager.instance,
                // メニュー項目選択と Inspector の双方向同期 (SE 固有)
                TimelineSelectionBridge.instance,
```

csproj へ追加(`Timeline\TimelineIntegration.cs` の近く):

```xml
    <Compile Include="Timeline\TimelineSelectionBridge.cs" />
```

- [ ] **Step 3: 両構成でビルドが通ることを確認**

Expected: どちらも Build succeeded

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineSelectionBridge.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): メニュー項目選択とInspector選択の双方向同期ブリッジを追加"
```

## Task 4: 表情モーフ行描画の抽出(FaceMorphRowDrawer)+ FindDef/IsAvailable

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/FaceMorphRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs`(`FindDef` / `IsAvailable` を追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:259-336`(`DrawMorphList` のループ本体を差し替え)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidFaceMorphControllerTests.cs`(新規)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `MaidFaceMorphController.FindDef(string name)` → `FaceMorphDef`(該当なし null)
- Produces: `MaidFaceMorphController.IsAvailable(Maid maid, FaceMorphDef def)` → bool
- Produces: `FaceMorphRowDrawer.Draw(GUIView view, Maid target, FaceMorphDef def, float labelWidth, float rowHeight)`

- [ ] **Step 1: FindDef の失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidFaceMorphControllerTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidFaceMorphControllerTests
    {
        [Fact]
        public void FindDef_全カテゴリを横断してモーフ名で定義を引ける()
        {
            var eyeclose = MaidFaceMorphController.FindDef("eyeclose");
            Assert.NotNull(eyeclose);
            Assert.Equal("目閉じ", eyeclose.displayName);

            // オプションカテゴリ (トグル系) も引ける
            var hoho = MaidFaceMorphController.FindDef("hoho");
            Assert.NotNull(hoho);
            Assert.True(hoho.isToggle);
        }

        [Fact]
        public void FindDef_未知の名前はnull()
        {
            Assert.Null(MaidFaceMorphController.FindDef("unknown_morph"));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

COM3D25 構成でビルド後 `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: `FindDef` 未定義でコンパイル失敗

- [ ] **Step 3: MaidFaceMorphController へ FindDef / IsAvailable を追加する**

`MaidManipulation/MaidFaceMorphController.cs` の `GetAvailableMorphs`(139 行付近)の前に追加:

```csharp
        /// <summary>モーフ名から定義を全カテゴリ横断で引く。該当なしは null</summary>
        public static FaceMorphDef FindDef(string name)
        {
            foreach (var defs in MorphDefs.Values)
            {
                foreach (var def in defs)
                {
                    if (def.name == name)
                    {
                        return def;
                    }
                }
            }
            return null;
        }

        /// <summary>対象メイドの顔にこのモーフが存在するか</summary>
        public static bool IsAvailable(Maid maid, FaceMorphDef def)
        {
            var morph = GetFaceMorph(maid);
            return morph != null && ResolveMorphIndex(morph, def.name) >= 0;
        }
```

- [ ] **Step 4: ビルドしてテストが通ることを確認**

Expected: 新規 2 テスト含め全件 PASS

- [ ] **Step 5: FaceMorphRowDrawer を作り、MaidFaceWindow を差し替える**

`source/COM3D2.SceneEditor.Plugin/FaceMorphRowDrawer.cs`(`MaidFaceWindow.DrawMorphList` のループ本体 273-332 行を移設。ロジックは一切変えない):

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表情モーフ 1 つ分の行描画 (変更追跡チェック + スライダー/トグル)。
    /// MaidFaceWindow と TimelineItemInspector (Inspector の項目表示) で共有する。
    /// 履歴登録・まばたき停止・追跡ストア更新までここで面倒を見る
    /// </summary>
    public static class FaceMorphRowDrawer
    {
        public static void Draw(
            GUIView view, Maid target, FaceMorphDef def, float labelWidth, float rowHeight)
        {
            var value = MaidFaceMorphController.GetMorphValue(target, def);
            // 表示判定用。まだ 1 つも編集していないメイドのストアを作らないよう FindStore を使う
            // (編集操作側のコールバックは GetStore で遅延生成する)
            var faceStore = FaceEditManager.instance.FindStore(target);
            var isModified = faceStore != null && faceStore.IsModified(def.name);

            // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
            // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
            Action<bool> onCheckChanged = newChecked =>
            {
                if (newChecked)
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                        "表情変更マーク: " + def.displayName);
                    FaceEditManager.instance.GetStore(target).Mark(def.name);
                }
                else
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                        "表情変更解除: " + def.displayName);
                    MaidFaceMorphController.SetMabataki(target, false);
                    MaidFaceMorphController.SetMorphValue(target, def, 0f);
                    FaceEditManager.instance.GetStore(target).Unmark(def.name);
                }
            };

            if (def.isToggle)
            {
                view.DrawTrackedToggle(isModified, onCheckChanged,
                    def.displayName, value >= 0.5f, 130, rowHeight, newValue =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情: " + def.displayName);
                        // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                        MaidFaceMorphController.SetMabataki(target, false);
                        MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                        FaceEditManager.instance.GetStore(target).Mark(def.name);
                    });
            }
            else
            {
                view.DrawTrackedSliderValue(isModified, onCheckChanged, rowHeight,
                    new GUIView.SliderOption
                    {
                        label = def.displayName,
                        labelWidth = labelWidth - GUIView.TrackedCheckWidth,
                        width = -1,
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = value,
                        onChanged = newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情: " + def.displayName);
                            // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, newValue);
                            FaceEditManager.instance.GetStore(target).Mark(def.name);
                        },
                    });
            }
        }
    }
}
```

`MaidFaceWindow.DrawMorphList`(259-336 行)のループを差し替え:

```csharp
        private void DrawMorphList(GUIView view, Maid target)
        {
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(target, currentMorphCategory))
            {
                FaceMorphRowDrawer.Draw(view, target, def, LABEL_WIDTH, ROW_HEIGHT);
            }

            view.EndScrollView();
        }
```

注意: 元コードの `faceStore` はループ外で 1 回だけ取得していたが、Drawer では行ごとに `FindStore`(辞書引き 1 回)になる。行数は高々数十のため許容する。`MaidFaceWindow.cs` 側で不要になった using があれば削除する。

csproj へ追加:

```xml
    <Compile Include="FaceMorphRowDrawer.cs" />
```

- [ ] **Step 6: 両構成ビルド + テストが通ることを確認**

Expected: Build succeeded ×2、テスト全件 PASS(表情ウィンドウの挙動は不変のリファクタリング)

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/FaceMorphRowDrawer.cs source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/MaidFaceMorphControllerTests.cs
git commit -m "refactor(face): 表情モーフ行描画をFaceMorphRowDrawerへ抽出しFindDefを追加"
```

## Task 5: MorphItemInspector(表情レイヤーのプロバイダ)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MorphItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:200-260`(`Initialize` 内で登録)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 1 の `ITimelineItemInspector`、Task 4 の `FaceMorphRowDrawer.Draw` / `MaidFaceMorphController.FindDef` / `IsAvailable`

- [ ] **Step 1: MorphItemInspector を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MorphItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表情レイヤー (MorphTimelineLayer) のメニュー項目 → 表情モーフ編集UI</summary>
    public class MorphItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>MaidWindowBase.LABEL_WIDTH (70) と同値 (表情ウィンドウと見た目を揃える。protected のため参照できず値を持つ)</summary>
        private const float LabelWidth = 70f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            foreach (var item in items)
            {
                var def = MaidFaceMorphController.FindDef(item.name);
                if (def == null)
                {
                    // タイムライン専用モーフ (SE の表情ウィンドウに定義が無い) は編集UIを持たない
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }
                if (!MaidFaceMorphController.IsAvailable(maid, def))
                {
                    view.DrawLabel(def.displayName + " (このメイドには存在しません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }
                FaceMorphRowDrawer.Draw(view, maid, def, LabelWidth, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // モーフに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
```

注意: `LABEL_WIDTH` は `MaidFaceWindow` 自身ではなく基底 `MaidWindowBase.cs:20` の `protected static readonly int LABEL_WIDTH = 70;`。継承関係にない本クラスからは参照できないため同値 70 を持つ(値を変えないこと)。`DrawLabel` の `textColor` 引数名は既存呼び出し(InspectorWindow.cs:359 等)に合わせる。

- [ ] **Step 2: TimelineIntegration.Initialize で登録する**

`Timeline/TimelineIntegration.cs` の `Initialize` 内、`RegisterLayer` 群の後に追加(MorphTimelineLayer の RegisterLayer がある位置の近く):

```csharp
            // メニュー項目選択 → Inspector 表示のプロバイダ (spec: timeline-item-inspector-roadmap.md)
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.MorphTimelineLayer), new MorphItemInspector());
```

csproj へ追加:

```xml
    <Compile Include="Timeline\ItemInspector\MorphItemInspector.cs" />
```

- [ ] **Step 3: 両構成でビルドが通ることを確認**

Expected: どちらも Build succeeded

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MorphItemInspector.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): 表情レイヤーの項目選択をInspectorの表情スライダーに接続"
```

## Task 6: BoneSliderRowDrawer 抽出 + MotionItemInspector(逆方向同期あり)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/BoneSliderRowDrawer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MotionItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:402-432`(`DrawBoneContent` の軸スライダーループを差し替え)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`(登録追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 1 の `ITimelineItemInspector`、`MaidBoneSliderController.FindDef(string)` / `GetOffset` / `SetOffsetAxis`(既存)
- Produces: `BoneSliderRowDrawer.Draw(GUIView view, Maid maid, BoneSliderDef def, float labelWidth)`

- [ ] **Step 1: BoneSliderRowDrawer を作り、InspectorWindow を差し替える**

`source/COM3D2.SceneEditor.Plugin/BoneSliderRowDrawer.cs`(`InspectorWindow.DrawBoneContent` の 402-432 行を移設。ロジックは一切変えない):

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポーズボーン 1 本分の軸オフセットスライダー行。
    /// InspectorWindow (ボーン選択時) と TimelineItemInspector (メニュー項目選択時) で共有する。
    /// 履歴登録・モーション停止までここで面倒を見る
    /// </summary>
    public static class BoneSliderRowDrawer
    {
        public static void Draw(GUIView view, Maid maid, BoneSliderDef def, float labelWidth)
        {
            // 再生中は基準ポーズが定まらないため値を読まず、操作された瞬間に停止して書き込む
            var offset = MaidMotionState.IsPlaying(maid)
                ? Vector3.zero
                : MaidBoneSliderController.GetOffset(maid, def);

            for (var i = 0; i < def.axes.Length; i++)
            {
                var axisIndex = i;
                var axis = def.axes[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = axis.label,
                    labelWidth = labelWidth,
                    width = -1,
                    min = axis.min,
                    max = axis.max,
                    step = 0.1f,
                    defaultValue = 0f,
                    value = offset[axisIndex],
                    onChanged = value =>
                    {
                        MaidMotionState.StopMotion(maid);
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ボーン回転: " + def.displayName,
                            new[] { MaidBoneSliderController.GetBone(maid, def.boneName) });
                        MaidBoneSliderController.SetOffsetAxis(
                            maid, def, axisIndex, value);
                    },
                });
            }
        }
    }
}
```

`InspectorWindow.DrawBoneContent` の 402-432 行(offset 取得〜for ループ)を差し替え:

```csharp
            BoneSliderRowDrawer.Draw(_view, maid, selectedDef, LabelWidth);
```

csproj へ追加:

```xml
    <Compile Include="BoneSliderRowDrawer.cs" />
```

- [ ] **Step 2: 両構成ビルド + テストで退行が無いことを確認**

Expected: Build succeeded ×2、テスト全件 PASS(ボーン選択時の Inspector 挙動は不変のリファクタリング)

- [ ] **Step 3: リファクタリングをコミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BoneSliderRowDrawer.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(inspector): ボーン軸スライダー行をBoneSliderRowDrawerへ抽出"
```

- [ ] **Step 4: MotionItemInspector を実装して登録する**

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MotionItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドアニメレイヤー (MotionTimelineLayer) のメニュー項目 → ボーン編集UI。
    /// 逆方向: Inspector のボーン選択 → 該当ボーン行のメニュー選択
    /// </summary>
    public class MotionItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (見た目を揃える)</summary>
        private const float LabelWidth = 50f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            // 退避中は表示に戻す際に上書きされるため操作させない (InspectorWindow と同じ理由)
            if (!MaidManipulateManager.instance.IsVisible(maid))
            {
                view.DrawLabel("非表示中はポーズを操作できません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            foreach (var item in items)
            {
                var def = MaidBoneSliderController.FindDef(item.name);
                if (def == null)
                {
                    // 拡張ボーン等、ポーズスライダー定義が無いボーンは S0 では対象外
                    view.DrawLabel(item.displayName + " (スライダー未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのボーンの行か分かるよう見出しを出す
                view.DrawLabel(def.displayName, -1, RowHeight);
                BoneSliderRowDrawer.Draw(view, maid, def, LabelWidth);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selection = SelectionManager.instance;
            if (!selection.hasBoneSelection || selection.selectedBoneMaid != layer.maid)
            {
                return null;
            }
            // MaidBoneMenuItem.name は標準ボーン名なので boneName がそのまま逆引きキーになる
            return selection.selectedBoneDef.boneName;
        }
    }
}
```

注意: InspectorWindow.cs:357 等の `maidManager.IsVisible(maid)` の実体は `MaidWindowBase.cs:22` の `protected static MaidManipulateManager maidManager => MaidManipulateManager.instance;`。SE 側に `MaidManager` というクラスは存在しない(MTEP 側の同名クラスは別物で `IsVisible` を持たない)ため、本クラスからは `MaidManipulateManager.instance.IsVisible(maid)` を直接呼ぶ。

`Timeline/TimelineIntegration.cs` の Task 5 で追加した登録の隣へ:

```csharp
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.MotionTimelineLayer), new MotionItemInspector());
```

csproj へ追加:

```xml
    <Compile Include="Timeline\ItemInspector\MotionItemInspector.cs" />
```

- [ ] **Step 5: 両構成ビルド + テストが通ることを確認**

Expected: Build succeeded ×2、テスト全件 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MotionItemInspector.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): モーションレイヤーのボーン選択をInspectorと双方向接続"
```

## Task 7: 仕上げ(最終確認 + ロードマップ更新)

**Files:**
- Modify: `docs/superpowers/specs/timeline-item-inspector-roadmap.md`(S0 の進捗と実機確認チェックリストを追記)

- [ ] **Step 1: 最終確認(両構成ビルド + 全テスト)**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: Build succeeded ×2、テスト全件 PASS

- [ ] **Step 2: ロードマップの S0 節へ実機確認チェックリストを追記**

`docs/superpowers/specs/timeline-item-inspector-roadmap.md` の Phase S0 節末尾に追記:

```markdown
#### S0 実装済み・実機確認項目(次回ゲーム起動時)

- [ ] 表情レイヤーで「目閉じ」行を選択 → Inspector に目閉じスライダーが出て編集できる
- [ ] セット行(例: 目)を選択 → 配下モーフがまとめて表示される
- [ ] キーフレームを選択 → KeyFrameInspector が優先表示され、解除で項目表示に戻る
- [ ] モーションレイヤーでボーン行を選択 → Inspector にボーンスライダーが出る
- [ ] タイムライン未読込・メイド未解決時は双方向同期が動かない(意図した制約。Inspector 選択がタイムラインに反応しなくても正常)
- [ ] Inspector /ビューポートでボーンを選択 → タイムラインの該当行が選択される(ループしない)
- [ ] メニュー行クリックでボーン/IK 選択が降格して項目表示に切り替わる
- [ ] 簡易表示 (isEasyEdit) では項目表示が出ない
- [ ] レイヤー切り替え・タイムライン閉鎖で NPE が出ない
- [ ] 表情ウィンドウ・Inspector ボーン選択の従来挙動に退行が無い(抽出リファクタリングの確認)
```

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/timeline-item-inspector-roadmap.md
git commit -m "docs(timeline): S0の実機確認チェックリストをロードマップへ追記"
```

## レビュー却下メモ

- `MaidBoneSliderController.FindDef` の未対応ボーン割合が未計測で「スライダー未対応」表示が多く出る可能性(確信度: 低) — 却下。拡張ボーン等の未対応表示は S0 の設計どおりで、対応拡充は S1 以降の判断。未確認のまま見送り

## 完了後

- code-review スキルでレビューを受ける(リポジトリ標準フロー)
- 実機確認はゲーム起動中 DLL がロックされるため、次回ゲーム起動時に Task 7 のチェックリストで実施
- Phase S1 以降は本計画と同じ型(行描画抽出 → プロバイダ追加 → 登録)で別計画を起こす
