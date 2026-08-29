# タイムライン レイヤー複数表示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインウィンドウでレイヤーをインスタンス単位で複数選択し、選択レイヤーをカテゴリ行（折りたたみ可）付きで縦に積んで表示する。

**Architecture:** `TimelineWindow` に「表示レイヤー集合 + 折りたたみ集合 + 行リスト」を持つ純粋クラス `TimelineLayerRowState`（ジェネリック、テスト可能）を導入し、`DrawBoneMenu` / `DrawTimeline` を行リスト駆動に書き換える。レイヤー選択コンボはメニューバー流儀のチェック付きコンボ（クリックで閉じない `GUIMultiSelectComboBox`）へ置き換える。編集操作は従来どおり `currentLayer` のみ。

**Tech Stack:** C# (Unity IMGUI / GUIView)、MSBuild 2 構成ビルド (COM3D2=.NET 3.5 / COM3D25=.NET 4.7.1)、xUnit (net48)

**Spec:** `docs/superpowers/specs/2026-08-29-timeline-multi-layer-display-design.md`

## Global Constraints

- **COM3D2 構成は .NET 3.5**。入力 5 個以上の `Func<>`/`Action<>`、`Tuple`、`CallerMemberName` は使用禁止。ビルド確認は必ず 2 構成とも行う
- **MTEUtils（`source/COM3D2.SceneEditor.Plugin/MTEUtils/`）は submodule のため変更禁止**。拡張は SE 側の派生クラスで行う
- コードのコメントとエラーログメッセージは日本語で書く
- 新規 .cs は `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` に `<Compile Include>` を追加する（旧形式 csproj。テストプロジェクトは SDK 形式なので不要）
- `deploy.bat` / `debug.bat` は実行しない。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2"`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`（プラグインの COM3D25 Debug ビルドが先に必要）
- 表示/折りたたみ状態はセッション内のみ。XML・Config へ保存しない
- UI の実機確認はゲーム再起動が必要なため本計画のスコープ外（最終タスクでチェックリスト化のみ）

---

### Task 1: TimelineLayerRowState（行モデル + 表示状態の純粋クラス）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（Compile Include 追加。`Timeline\TimelineLayerInfo.cs` の行の近くに `<Compile Include="Timeline\TimelineLayerRowState.cs" />`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs`

**Interfaces:**
- Consumes: なし（自己完結のジェネリッククラス。Unity 依存なし）
- Produces:
  - `struct LayerRow<TLayer, TItem>`: フィールド `TLayer layer` / `TItem menuItem`、プロパティ `bool isHeader`（`menuItem == null`）
  - `class TimelineLayerRowState<TLayer, TItem> where TLayer : class where TItem : class`:
    - `bool IsVisible(TLayer layer, TLayer currentLayer)` — currentLayer は常に true
    - `void ToggleVisible(TLayer layer, TLayer currentLayer)` — currentLayer の OFF は無視
    - `bool IsCollapsed(TLayer layer)` / `void ToggleCollapsed(TLayer layer)`
    - `void Reset()` — 両集合をクリア
    - `void Prune(IList<TLayer> aliveLayers)` — 生存リストに無い参照を両集合から除去
    - `void BuildRows(IList<TLayer> layers, TLayer currentLayer, Action<TLayer, List<TItem>> collectItems, List<LayerRow<TLayer, TItem>> result)` — 表示レイヤーごとに「カテゴリ行 + (展開中なら) アイテム行」を layers の並び順で result へ組み立てる（result は先頭でクリア）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRowStateTests
    {
        // TLayer/TItem は参照型なら何でもよいのでテストでは string を使う
        private readonly TimelineLayerRowState<string, string> _state
            = new TimelineLayerRowState<string, string>();

        private static void CollectItems(string layer, List<string> result)
        {
            result.Add(layer + ":item0");
            result.Add(layer + ":item1");
        }

        [Fact]
        public void アクティブレイヤーは未トグルでも表示扱い()
        {
            Assert.True(_state.IsVisible("A", "A"));
            Assert.False(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void トグルで表示のオンオフが切り替わる()
        {
            _state.ToggleVisible("B", "A");
            Assert.True(_state.IsVisible("B", "A"));
            _state.ToggleVisible("B", "A");
            Assert.False(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void アクティブレイヤーの非表示トグルは無視される()
        {
            _state.ToggleVisible("A", "A");
            Assert.True(_state.IsVisible("A", "A"));
        }

        [Fact]
        public void 折りたたみトグルが切り替わる()
        {
            Assert.False(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.True(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.False(_state.IsCollapsed("A"));
        }

        [Fact]
        public void Pruneで死んだレイヤーが集合から消える()
        {
            _state.ToggleVisible("B", "A");
            _state.ToggleCollapsed("B");
            _state.Prune(new List<string> { "A" });
            Assert.False(_state.IsVisible("B", "A"));
            Assert.False(_state.IsCollapsed("B"));
        }

        [Fact]
        public void BuildRowsは表示レイヤーごとにカテゴリ行とアイテム行を積む()
        {
            _state.ToggleVisible("B", "A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A", "B", "C" }, "A", CollectItems, rows);

            // A(ヘッダ+2行) + B(ヘッダ+2行)。C は非表示
            Assert.Equal(6, rows.Count);
            Assert.True(rows[0].isHeader);
            Assert.Equal("A", rows[0].layer);
            Assert.Equal("A:item0", rows[1].menuItem);
            Assert.Equal("A:item1", rows[2].menuItem);
            Assert.True(rows[3].isHeader);
            Assert.Equal("B", rows[3].layer);
        }

        [Fact]
        public void 折りたたみ中はカテゴリ行だけ残る()
        {
            _state.ToggleCollapsed("A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);

            Assert.Single(rows);
            Assert.True(rows[0].isHeader);
        }

        [Fact]
        public void BuildRowsは呼ぶたびに結果をクリアして詰め直す()
        {
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            Assert.Equal(3, rows.Count);
        }
    }
}
```

- [ ] **Step 2: テストが失敗する（コンパイルエラーになる）ことを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineLayerRowStateTests`
Expected: FAIL（`TimelineLayerRowState` が存在せずコンパイルエラー）。
注意: テストはプラグイン DLL 参照方式のため、先に COM3D25 構成をビルドしておくこと（Global Constraints のコマンド）。この時点では本体未変更なので既存ビルドで良い。

- [ ] **Step 3: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインの 1 行分。menuItem == null ならレイヤーカテゴリ行
    /// </summary>
    public struct LayerRow<TLayer, TItem>
        where TLayer : class
        where TItem : class
    {
        public TLayer layer;
        public TItem menuItem;

        public bool isHeader => menuItem == null;
    }

    /// <summary>
    /// タイムラインの複数レイヤー表示状態 (表示集合・折りたたみ集合) と行リスト構築。
    /// セッション内のみのビュー状態で、永続化しない。
    /// Unity 非依存のジェネリックにしてユニットテスト可能にしている
    /// </summary>
    public class TimelineLayerRowState<TLayer, TItem>
        where TLayer : class
        where TItem : class
    {
        private readonly HashSet<TLayer> _visibleLayers = new HashSet<TLayer>();
        private readonly HashSet<TLayer> _collapsedLayers = new HashSet<TLayer>();
        private readonly List<TItem> _itemBuffer = new List<TItem>(128);

        /// <summary>アクティブレイヤーは常に表示対象</summary>
        public bool IsVisible(TLayer layer, TLayer currentLayer)
        {
            return layer == currentLayer || _visibleLayers.Contains(layer);
        }

        public void ToggleVisible(TLayer layer, TLayer currentLayer)
        {
            // アクティブレイヤーは非表示にできない
            if (layer == currentLayer)
            {
                _visibleLayers.Add(layer);
                return;
            }

            if (!_visibleLayers.Remove(layer))
            {
                _visibleLayers.Add(layer);
            }
        }

        public bool IsCollapsed(TLayer layer)
        {
            return _collapsedLayers.Contains(layer);
        }

        public void ToggleCollapsed(TLayer layer)
        {
            if (!_collapsedLayers.Remove(layer))
            {
                _collapsedLayers.Add(layer);
            }
        }

        public void Reset()
        {
            _visibleLayers.Clear();
            _collapsedLayers.Clear();
        }

        /// <summary>タイムライン再構築等で消えたレイヤー参照を掃除する</summary>
        public void Prune(IList<TLayer> aliveLayers)
        {
            _visibleLayers.RemoveWhere(layer => !aliveLayers.Contains(layer));
            _collapsedLayers.RemoveWhere(layer => !aliveLayers.Contains(layer));
        }

        /// <summary>
        /// 表示レイヤーごとに「カテゴリ行 + (展開中なら) アイテム行」を
        /// layers の並び順で result へ組み立てる
        /// </summary>
        public void BuildRows(
            IList<TLayer> layers,
            TLayer currentLayer,
            Action<TLayer, List<TItem>> collectItems,
            List<LayerRow<TLayer, TItem>> result)
        {
            result.Clear();

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (!IsVisible(layer, currentLayer))
                {
                    continue;
                }

                result.Add(new LayerRow<TLayer, TItem> { layer = layer });

                if (IsCollapsed(layer))
                {
                    continue;
                }

                _itemBuffer.Clear();
                collectItems(layer, _itemBuffer);
                foreach (var item in _itemBuffer)
                {
                    result.Add(new LayerRow<TLayer, TItem> { layer = layer, menuItem = item });
                }
            }
        }
    }
}
```

csproj に Compile Include を追加する（`Timeline\TimelineLayerInfo.cs` の行の直後）:

```xml
    <Compile Include="Timeline\TimelineLayerRowState.cs" />
```

- [ ] **Step 4: ビルドしてテストが通ることを確認**

Run:
1. `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
2. `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineLayerRowStateTests`

Expected: 8 件 PASS

- [ ] **Step 5: COM3D2 (.NET 3.5) 構成もビルド確認**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2"`
Expected: ビルド成功（`HashSet<T>.RemoveWhere` は .NET 3.5 の System.Core にあり利用可）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs
git commit -m "feat(timeline): レイヤー複数表示の行モデルと表示状態クラスを追加する"
```

---

### Task 2: GUIMultiSelectComboBox（クリックで閉じないチェック付きコンボ）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/GUIMultiSelectComboBox.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（Compile Include 追加）

**Interfaces:**
- Consumes: `COM3D2.MotionTimelineEditor.GUIComboBox<T>`（MTEUtils。`DrawPopupContent` は override 可能な public abstract 実装）、`GUIView.DrawListView<T>(List<T>, Func<T,int,string>, Func<T,int,bool>, float, float, int, float, GUIStyle)`
- Produces: `class GUIMultiSelectComboBox<T> : GUIComboBox<T>`
  - `Func<T, int, bool> getChecked` — 項目のチェック状態
  - `Action<T, int> onToggle` — 項目クリック時のトグル処理
  - `DrawPopupContent` は常に false を返す（= ポップアップを閉じない。外側クリックで閉じる挙動は `ComboBoxPopupWindow.Update` の既存機構が担う）

- [ ] **Step 1: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/GUIMultiSelectComboBox.cs`:

```csharp
using System;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック付き複数選択コンボ。メニューバーのポップアップと同じ流儀で、
    /// 項目クリックでは閉じずにチェックをトグルする (閉じるのは外側クリック)。
    /// MTEUtils は submodule のため本体を変えず SE 側の派生で拡張する
    /// </summary>
    public class GUIMultiSelectComboBox<T> : GUIComboBox<T>
    {
        public Func<T, int, bool> getChecked;
        public Action<T, int> onToggle;

        public override bool DrawPopupContent(GUIView view)
        {
            var selectedIndex = view.DrawListView(
                items,
                GetCheckedName,
                getEnabled,
                view.viewRect.width,
                view.viewRect.height,
                currentIndex,
                buttonSize.y);

            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                onToggle?.Invoke(items[selectedIndex], selectedIndex);
            }

            // 連続で切り替えられるようポップアップは閉じない
            return false;
        }

        private string GetCheckedName(T item, int index)
        {
            var isOn = getChecked != null && getChecked(item, index);
            return (isOn ? "✓ " : "　 ") + getName(item, index);
        }
    }
}
```

csproj に Compile Include を追加する（ルート直下の .cs 群、例えば `TimelineWindow.cs` の行の近く）:

```xml
    <Compile Include="GUIMultiSelectComboBox.cs" />
```

- [ ] **Step 2: 2 構成ビルド確認**

Run（両方）:
- `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2"`

Expected: 両方ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/GUIMultiSelectComboBox.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): チェック付き複数選択コンボを追加する"
```

---

### Task 3: BoneMenuManager にレイヤー指定版 GetVisibleItems を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs`

**Interfaces:**
- Consumes: `ITimelineLayer.allMenuItems`、`IBoneMenuItem.isVisibleMenu` / `children`
- Produces: `void GetVisibleItems(ITimelineLayer layer, List<IBoneMenuItem> result)` — 指定レイヤーの可視メニュー項目を result へ**追記**する（クリアしない。呼び出し側バッファへの積み増し用）。既存の引数無し `GetVisibleItems()` は currentLayer 対象のまま変更しない

- [ ] **Step 1: 実装を書く**

`BoneMenuManager.cs` の `GetVisibleItems()` の直後にレイヤー指定版を追加する:

```csharp
        /// <summary>
        /// 指定レイヤーの可視メニュー項目を result へ追記する。
        /// 複数レイヤー表示の行リスト構築用 (isEasyEdit の分岐は呼び出し側が行う)
        /// </summary>
        public void GetVisibleItems(ITimelineLayer layer, List<IBoneMenuItem> result)
        {
            foreach (var setMenuItem in layer.allMenuItems)
            {
                if (setMenuItem.isVisibleMenu)
                {
                    result.Add(setMenuItem);
                }

                if (setMenuItem.children == null)
                {
                    continue;
                }

                foreach (var menuItem in setMenuItem.children)
                {
                    if (menuItem.isVisibleMenu)
                    {
                        result.Add(menuItem);
                    }
                }
            }
        }
```

- [ ] **Step 2: 2 構成ビルド確認**

Run: Global Constraints の MSBuild コマンド 2 本
Expected: 両方ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs
git commit -m "feat(timeline): BoneMenuManagerにレイヤー指定の可視項目取得を追加する"
```

---

### Task 4: TimelineWindow の状態配線・表示名ヘルパー・レイヤーコントロール置換

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `TimelineLayerRowState<,>` / `LayerRow<,>`、Task 2 の `GUIMultiSelectComboBox<T>`、Task 3 の `GetVisibleItems(layer, result)`、`TimelineManager.layers` / `SetCurrentLayer(ITimelineLayer)` / `GetLayerInfo(Type)` / `GetLayer(Type, int)` / `layerInfoList`
- Produces（Task 5/6 が使う TimelineWindow のプライベートメンバー）:
  - `TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem> _rowState`
  - `List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>> _rows`（毎フレーム DrawBody で再構築済み）
  - `string GetLayerDisplayName(MTEP.ITimelineLayer layer)`

- [ ] **Step 1: フィールドと表示名ヘルパーを追加する**

`_layerComboBox` フィールド（`/// <summary>使用中レイヤーの選択コンボ...` のブロック全体、約 78〜88 行）を削除し、以下に置き換える:

```csharp
        /// <summary>表示レイヤーの複数選択コンボ。ボーンメニュー上部に置く</summary>
        private readonly GUIMultiSelectComboBox<MTEP.ITimelineLayer> _displayLayerComboBox
            = new GUIMultiSelectComboBox<MTEP.ITimelineLayer>
        {
            contentSize = new Vector2(200, 300),
            // menuWidth (100〜300px) に収めるため前後送りの矢印は省略する
            showArrow = false,
        };

        /// <summary>表示レイヤー集合と折りたたみ集合 (セッション内のみ保持)</summary>
        private readonly TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem> _rowState
            = new TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>();

        /// <summary>今フレームの表示行 (カテゴリ行 + ボーンメニュー行)。DrawBody で再構築する</summary>
        private readonly List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>> _rows
            = new List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>>(256);

        /// <summary>タイムライン差し替え検知用。別インスタンスになったら表示状態をリセットする</summary>
        private MTEP.TimelineData _lastTimeline = null;

        /// <summary>レイヤー数の変化検知用。Prune を毎フレーム走らせないためのガード</summary>
        private int _lastLayerCount = -1;

        /// <summary>追加コンボ用の「現在のメイドで未使用の型」一覧バッファ (毎フレーム詰め直す)</summary>
        private readonly List<MTEP.TimelineLayerInfo> _addableLayerInfoList
            = new List<MTEP.TimelineLayerInfo>(32);
```

コンストラクタ `private TimelineWindow()` の末尾にデリゲート設定を追加する
（フィールド初期化子ではインスタンスメンバーを参照できないためここで設定する）:

```csharp
            _displayLayerComboBox.getName = (layer, _) => GetLayerDisplayName(layer);
            _displayLayerComboBox.getChecked = (layer, _) => _rowState.IsVisible(layer, currentLayer);
            _displayLayerComboBox.onToggle = (layer, _) => _rowState.ToggleVisible(layer, currentLayer);
```

クラス内（`DrawLayerControls` の手前あたり）に表示名ヘルパーを追加する:

```csharp
        /// <summary>
        /// レイヤーインスタンスの表示名。スロット付きレイヤーはメイド名を併記して
        /// 同型レイヤーのインスタンスを区別できるようにする
        /// </summary>
        private string GetLayerDisplayName(MTEP.ITimelineLayer layer)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            var name = info != null ? info.displayName : layer.layerName;
            if (layer.hasSlotNo)
            {
                var maidCache = layer.maidCache;
                var maidName = maidCache != null && !string.IsNullOrEmpty(maidCache.fullName)
                    ? maidCache.fullName
                    : "メイド" + (layer.slotNo + 1);
                name += " (" + maidName + ")";
            }
            return name;
        }
```

- [ ] **Step 2: DrawBody に行リスト構築を配線する**

`DrawBody` の `bool guiEnabled = ...` の直後（`if (texTimelineBG == null && editEnabled)` の手前）に追加する:

```csharp
            // タイムラインが差し替わったら表示状態を初期化 (アクティブのみ表示・全展開)
            if (timeline != _lastTimeline)
            {
                _lastTimeline = timeline;
                _rowState.Reset();
            }

            if (editEnabled)
            {
                // レイヤーの追加・削除は必ず数の変化を伴うため、Prune は数が変わったときだけで足りる
                // (同一フレームでの入れ替えで数が同じ場合、残った死に参照は layers に無いので描画されず無害)
                if (timelineManager.layers.Count != _lastLayerCount)
                {
                    _lastLayerCount = timelineManager.layers.Count;
                    _rowState.Prune(timelineManager.layers);
                }
                BuildRows();
            }
```

`DrawBody` の直後にメソッドを追加する:

```csharp
        /// <summary>表示行リストを組み立てる。簡易表示時は従来どおり単一レイヤーでカテゴリ行なし</summary>
        private void BuildRows()
        {
            if (timelineConfig.isEasyEdit)
            {
                _rows.Clear();
                foreach (var item in boneMenuManager.GetVisibleItems())
                {
                    _rows.Add(new LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>
                    {
                        layer = currentLayer,
                        menuItem = item,
                    });
                }
                return;
            }

            _rowState.BuildRows(timelineManager.layers, currentLayer, CollectVisibleItems, _rows);
        }

        private void CollectVisibleItems(MTEP.ITimelineLayer layer, List<MTEP.IBoneMenuItem> result)
        {
            boneMenuManager.GetVisibleItems(layer, result);
        }
```

- [ ] **Step 3: DrawLayerControls をチェック付きコンボに置き換える**

`DrawLayerControls` の `_layerComboBox` 使用部（`_layerComboBox.buttonSize = ...` から `_layerComboBox.DrawButton(view);` まで）を以下へ置き換える。`-` ボタンは変更しない:

```csharp
            _displayLayerComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
            _displayLayerComboBox.items = timelineManager.layers;
            // DrawListView のアクセント色でアクティブレイヤーを示す
            _displayLayerComboBox.currentIndex = timelineManager.layers.IndexOf(timelineManager.currentLayer);
            _displayLayerComboBox.defaultName = GetLayerComboLabel();
            _displayLayerComboBox.DrawButton(view);
```

`+`（追加コンボ）の items を「タイムライン全体で未使用の型」から「**現在のメイドで未使用の型**」へ変更する。
旧選択コンボは型選択時に `ChangeActiveLayer(layerType, maidSlotNo)` で現在メイドのインスタンスを
自動生成できたが、新コンボは既存インスタンスの表示トグル専用のため、この生成導線を `+` 側へ移す
（「型は他メイドで使用中だが現在のメイドには無い」ケースの新規作成手段を残す）。
`_addLayerComboBox.items = timelineManager.unusingLayerInfoList;` の行を以下へ置き換える:

```csharp
            // 現在のメイドでまだ使っていない型を列挙する (スロット無しレイヤーは存在チェックのみ)。
            // 旧レイヤーコンボが担っていた「型選択で現在メイドのインスタンスを自動生成する」導線の代替
            _addableLayerInfoList.Clear();
            foreach (var info in timelineManager.layerInfoList)
            {
                if (timelineManager.GetLayer(info.layerType, maidManager.maidSlotNo) == null)
                {
                    _addableLayerInfoList.Add(info);
                }
            }
            _addLayerComboBox.items = _addableLayerInfoList;
```

`_addLayerComboBox` の `onSelected`（`ChangeActiveLayer` 呼び出し）は変更しない。

`DrawLayerControls` の直後にボタン面ラベルのヘルパーを追加する:

```csharp
        /// <summary>コンボのボタン面ラベル。アクティブレイヤー名 + 他に表示中があれば「他N」</summary>
        private string GetLayerComboLabel()
        {
            var visibleCount = 0;
            foreach (var layer in timelineManager.layers)
            {
                if (_rowState.IsVisible(layer, currentLayer))
                {
                    visibleCount++;
                }
            }

            var name = GetLayerDisplayName(currentLayer);
            return visibleCount > 1 ? name + " 他" + (visibleCount - 1) : name;
        }
```

- [ ] **Step 4: 2 構成ビルド確認**

Run: Global Constraints の MSBuild コマンド 2 本
Expected: 両方ビルド成功（この時点で描画はまだ旧ロジックのままでよい。`_rows` は未参照警告にならない — readonly フィールドで DrawBody から更新しているため）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): レイヤー複数選択コンボと表示状態の配線を追加する"
```

---

### Task 5: DrawBoneMenu を行リスト駆動にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（`DrawBoneMenu` メソッド）

**Interfaces:**
- Consumes: Task 4 の `_rows` / `_rowState` / `GetLayerDisplayName`、`TimelineManager.SetCurrentLayer(ITimelineLayer)`
- Produces: なし（描画のみ）

- [ ] **Step 1: DrawBoneMenu の行ループを書き換える**

`var menuItems = boneMenuManager.GetVisibleItems();` を削除し、`contentHeight` と行ループを `_rows` 基準へ変更する。行ループ全体（`for (int i = 0; i < menuItems.Count; i++)` の中身）を以下へ置き換える:

```csharp
            var contentHeight = _rows.Count * frameHeight;
```

```csharp
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var isActiveLayerRow = row.layer == timelineManager.currentLayer;

                // レイヤーカテゴリ行: 折りたたみトグル + レイヤー名 (クリックでアクティブ化)
                if (row.isHeader)
                {
                    var headerColor = isActiveLayerRow ? tc.timelineMenuSelectTextColor : Color.white;
                    var headerLayer = row.layer;

                    view.currentPos.x = 0;
                    view.DrawLabel(
                        _rowState.IsCollapsed(headerLayer) ? "＋" : "ー",
                        20,
                        20,
                        headerColor,
                        null,
                        () =>
                        {
                            _rowState.ToggleCollapsed(headerLayer);
                        }
                    );

                    view.currentPos.x = 20;
                    view.DrawLabel(
                        "■ " + GetLayerDisplayName(headerLayer),
                        menuWidth - 20,
                        20,
                        headerColor
                    );

                    view.InvokeActionOnEvent(
                        menuWidth - 40,
                        20,
                        EventType.MouseDown,
                        (pos) =>
                        {
                            if (headerLayer != timelineManager.currentLayer)
                            {
                                timelineManager.SetCurrentLayer(headerLayer);
                            }
                        });

                    continue;
                }

                var menuItem = row.menuItem;

                var diplayName = menuItem.displayName;
                // 選択ハイライトはアクティブレイヤーの行にだけ意味を持つ
                var isSelected = isActiveLayerRow && menuItem.isSelectedMenu;

                view.currentPos.x = 0;

                if (menuItem.isSetMenu)
                {
                    view.DrawLabel(
                        menuItem.isOpenMenu ? "ー" : "＋",
                        20,
                        20,
                        isSelected ? tc.timelineMenuSelectTextColor : Color.white,
                        null,
                        () =>
                        {
                            menuItem.isOpenMenu = !menuItem.isOpenMenu;
                        }
                    );
                }

                view.currentPos.x = 20;

                view.DrawLabel(
                    diplayName,
                    menuWidth - 20,
                    20,
                    isSelected ? tc.timelineMenuSelectTextColor : Color.white
                );

                view.InvokeActionOnEvent(
                    menuWidth - 40,
                    20,
                    EventType.MouseDown,
                    (pos) =>
                    {
                        // 非アクティブレイヤーの行はまずアクティブ化してから選択する
                        if (row.layer != timelineManager.currentLayer)
                        {
                            timelineManager.SetCurrentLayer(row.layer);
                        }
                        menuItem.SelectMenu(isMultiSelect);
                    });

                // A/D ボタンはアクティブレイヤーの行のみ (編集はアクティブレイヤーに束縛)
                if (studioHackManager.isPoseEditing && isActiveLayerRow)
                {
                    view.InvokeActionOnMouse(
                        menuWidth - 20,
                        20,
                        _ =>
                        {
                            view.currentPos.x = menuWidth - 20;

                            var frame = currentLayer.GetFrame(timelineManager.currentFrameNo);
                            if (menuItem.IsFullBones(frame))
                            {
                                if (view.DrawButton("D", 20, 20))
                                {
                                    menuItem.RemoveKey();
                                }
                            }
                            else
                            {
                                if (view.DrawButton("A", 20, 20))
                                {
                                    menuItem.AddKey();
                                }
                            }
                        });
                }
            }
```

注意: `isSetMenu` の開閉トグル（ー/＋）は既存のボーンセット行の機能で、カテゴリ行の折りたたみとは別物。両方とも残すこと。

- [ ] **Step 2: 2 構成ビルド確認**

Run: Global Constraints の MSBuild コマンド 2 本
Expected: 両方ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): ボーンメニューを複数レイヤーの行リスト駆動にする"
```

---

### Task 6: DrawTimeline を行リスト駆動にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（`DrawTimeline` メソッド）

**Interfaces:**
- Consumes: Task 4 の `_rows`、`TimelineManager.SetCurrentLayer(ITimelineLayer)`
- Produces: なし（描画のみ）

- [ ] **Step 1: コンテンツ高さ・背景・選択メニュー背景を行リスト基準にする**

`var menuItems = boneMenuManager.GetVisibleItems();` を削除する。

`var contentHeight = menuItems.Count * frameHeight;` を:

```csharp
            var contentHeight = _rows.Count * frameHeight;
```

背景表示ループの `for (var i = 0; i < menuItems.Count; i += 2)` を `for (var i = 0; i < _rows.Count; i += 2)` に変更する（中身は変更なし）。

「選択中のメニュー背景表示」ループ（`for (var i = 0; i < menuItems.Count; i++)` 〜）を以下へ置き換える:

```csharp
            for (var i = 0; i < _rows.Count; i++)
            {
                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var row = _rows[i];

                // カテゴリ行はドープシート側ではキーを持たない帯として塗る
                if (row.isHeader)
                {
                    view.DrawTexture(
                        texWhite,
                        viewWidth,
                        frameHeight,
                        timelineLabelBgColor);
                    continue;
                }

                // 選択ハイライトはアクティブレイヤーの行のみ
                if (row.layer == timelineManager.currentLayer && row.menuItem.isSelectedMenu)
                {
                    view.DrawTexture(
                        texWhite,
                        viewWidth,
                        frameHeight,
                        tc.timelineMenuSelectBgColor);
                }
            }
```

- [ ] **Step 2: キーフレーム描画を表示レイヤーごとのブロックループにする**

「キーフレーム表示」ブロック（`var frames = currentLayer.keyFrames;` から、フレームドラッグ開始・描画を含む `foreach (var frame in frames)` ループ全体まで）を以下へ置き換える:

```csharp
            // キーフレーム表示。行リストを同一レイヤーの連続ブロックごとに走査する
            var adjustY = (frameHeight - frameWidth) / 2;
            var blockStart = 0;
            while (blockStart < _rows.Count)
            {
                var blockLayer = _rows[blockStart].layer;
                var blockEnd = blockStart;
                while (blockEnd < _rows.Count && _rows[blockEnd].layer == blockLayer)
                {
                    blockEnd++;
                }

                // 折りたたみ中 (カテゴリ行のみでアイテム行なし) は keyFrames 走査ごとスキップする
                if (blockEnd - blockStart == 1 && _rows[blockStart].isHeader)
                {
                    blockStart = blockEnd;
                    continue;
                }

                var isActiveLayer = blockLayer == timelineManager.currentLayer;

                foreach (var frame in blockLayer.keyFrames)
                {
                    var frameNo = frame.frameNo;

                    view.currentPos.x = frameNo * frameWidth;
                    if (view.currentPos.x < scrollPosition.x ||
                        view.currentPos.x > scrollPosition.x + viewWidth)
                    {
                        continue;
                    }

                    for (var i = blockStart; i < blockEnd; i++)
                    {
                        var row = _rows[i];
                        if (row.isHeader)
                        {
                            continue;
                        }

                        var menuItem = row.menuItem;

                        view.currentPos.y = i * frameHeight + adjustY;
                        if (view.currentPos.y < scrollPosition.y ||
                            view.currentPos.y > scrollPosition.y + viewHeight - 20)
                        {
                            continue;
                        }

                        if (!menuItem.HasVisibleBone(frame))
                        {
                            continue;
                        }

                        // 選択状態はアクティブレイヤーにしか存在しない
                        bool isSelected = isActiveLayer && menuItem.IsSelectedFrame(frame);

                        var keyFrameRect = new Rect(
                                view.currentPos.x,
                                view.currentPos.y,
                                frameWidth,
                                frameWidth);

                        // エリア選択範囲内のキーフレームを選択 (アクティブレイヤーのみ)
                        if (isActiveLayer && areaDragInfo.isDragging)
                        {
                            if (areaDragRect.Overlaps(keyFrameRect))
                            {
                                if (!isSelected)
                                {
                                    menuItem.SelectFrame(frame, true);
                                }
                            }
                            else
                            {
                                if (isSelected && !isMultiSelect)
                                {
                                    menuItem.SelectFrame(frame, true);
                                }
                            }
                        }

                        // フレームのドラッグ開始。非アクティブレイヤーはまずアクティブ化してから選択する
                        if (!areaDragInfo.isDragging && !frameDragInfo.isDragging)
                        {
                            view.InvokeActionOnDragStart(
                                keyFrameRect,
                                frameDragInfo,
                                view.currentPos,
                                newPos =>
                                {
                                    if (row.layer != timelineManager.currentLayer)
                                    {
                                        timelineManager.SetCurrentLayer(row.layer);
                                    }
                                    menuItem.SelectFrame(frame, isMultiSelect);
                                    frameDragBoneData = timelineManager.selectedBones
                                        .Where(bone => bone.frameNo == frameNo)
                                        .FirstOrDefault();

                                    // 消費しないと GUI.DragWindow が拾ってウィンドウごと動いてしまう
                                    Event.current.Use();
                                }
                            );
                        }

                        var keyFrameColor = isSelected ? Color.red : Color.white;

                        if (!menuItem.IsFullBones(frame))
                        {
                            keyFrameColor *= Color.gray;
                        }

                        view.DrawTexture(
                            texKeyFrame,
                            frameWidth,
                            frameWidth,
                            keyFrameColor);
                    }
                }

                blockStart = blockEnd;
            }
```

注意:
- 旧コードの `var adjustY = ...` は置き換え後のブロック先頭に含めたので二重定義しないこと
- `frameDragInfo` によるドラッグ中処理・エリア選択のドラッグ開始/ドラッグ中処理（ループの後ろの既存コード）は変更しない

- [ ] **Step 3: 2 構成ビルド確認**

Run: Global Constraints の MSBuild コマンド 2 本
Expected: 両方ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): ドープシートを複数レイヤーの行リスト駆動にする"
```

---

### Task 7: 総合検証

**Files:**
- Test: 全テストスイート

**Interfaces:**
- Consumes: Task 1〜6 の成果物
- Produces: なし

- [ ] **Step 1: 全テスト実行**

Run:
1. `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
2. `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`

Expected: 既存テスト + Task 1 の 8 件がすべて PASS

- [ ] **Step 2: COM3D2 構成の最終ビルド確認**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2"`
Expected: ビルド成功

- [ ] **Step 3: 実機確認チェックリストを残す**

ゲーム再起動が必要なため実機確認は本計画外。次回起動時の確認項目（結果はユーザーへ報告）:

1. レイヤーコンボがインスタンス一覧（メイド名併記）で開き、クリックで ✓ がトグルし閉じないこと。外側クリックで閉じること
2. 複数レイヤー表示時、各レイヤーのカテゴリ行が出て「ー/＋」で折りたためること
3. カテゴリ行のレイヤー名クリックでアクティブが切り替わり、強調色が移ること
4. 非アクティブレイヤーのキーをクリックするとそのレイヤーがアクティブ化され、キーが選択されること
5. 矩形選択・キードラッグ・A/D ボタンがアクティブレイヤーの行にだけ効くこと
6. 簡易表示 (isEasyEdit) で従来どおり単一表示になること
7. タイムライン読み直しで表示状態が初期化されること（アクティブのみ表示）
8. レイヤー削除後にカテゴリ行が消え、エラーが出ないこと
9. `+` コンボに「他メイドでは使用中だが現在のメイドには無い型」が並び、選択で現在メイドのインスタンスが生成・アクティブ化されること
10. キー複数選択（isMultiSelect）中に非アクティブレイヤーの行/キーをクリックすると、選択が全解除されてクリック先の 1 件だけが選択されること（SetCurrentLayer の UnselectAll による仕様どおりの挙動）
