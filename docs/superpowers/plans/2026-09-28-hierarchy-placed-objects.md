# Hierarchy 配置物ビュー Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 既存の Hierarchy ウィンドウに「配置物」と「GameObject」の表示切り替えを足す。配置物ビューはメイド / モデル / 背景 / PNG / ライト / サブカメラをカテゴリ別の木で出し、メイドにアタッチしたモデルは付け先メイドの子に置く。行の選択は GameObject ビューと同じく SelectionManager と連動する。

**Architecture:**
- 木の組み立ては Unity に依存しない純ロジックの `PlacedObjectTree` に集める。入力は `PlacedObjectSource` (カテゴリ・ID・ラベル・親 ID・選択対象) の平坦な並びで、カテゴリ分け・アタッチの入れ子・背景モデルの入れ子・重複と循環の処理・変化検知 (`SameSources`) をすべてここで行う。テストは GameObject を null にしたまま書ける
- ゲームの実体から `PlacedObjectSource` を集めるのは `PlacedObjectCollector` (Unity 依存)。アタッチの判定はタイムラインのキー値ではなく実際の親子関係 (モデルのルートの祖先にある `Maid`) で行う。キー値 (`StudioModelStat.attachMaidSlotNo`) はタイムライン有効時しか同期されず、シーンモードで使えないため
- `HierarchyWindow` は既存の 0.5 秒間隔のポーリングで、表示中のビューの一覧だけを集める。配置物ビューは集めた並びを前回と `SameSources` で比べ、変わったときだけ木を組み直す。行 UI は既存の `GUITreeView<T>` をもう 1 つ持って使う (MTEUtils は変更しない)

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「9. Hierarchy の配置物ビュー」。参考: `docs/superpowers/plans/2026-08-16-guitreeview-extraction.md` (GUITreeView の設計)

## 仕様

仕様書の確定事項 (そのまま):

- 既存の Hierarchy ウィンドウに「配置物」と「GameObject」の表示切り替えを足す
- 配置物ビューのカテゴリ: メイド / モデル / 背景 (背景モデルの木) / PNG / ライト / サブカメラ
- メイドにアタッチしたモデルは、付け先メイドの子として表示する
- 行クリックで選択、ダブルクリックで SceneView のフォーカス (GameObject ビューと同じ)。SceneView などで選んだものは一覧で強調・展開する
- 今回は表示と選択の連動まで。表示の切り替え・右クリックの複製や削除・ドラッグでの付け替えは後から足す
- 一覧は変化を検知したときだけ組み直す (毎フレーム組み直さない)
- 保存形式に影響なし

本計画で決めた細部 (plan-review で確認してほしい点):

- **切り替え UI**: 検索欄の上にタブ `配置物` / `GameObject`。選んだビューは Config (`hierarchyViewMode`) に保存し、既定は `配置物`。既存ユーザーも次回から配置物ビューで開く
- **カテゴリの並び**: 上の順で固定。中身が 0 件のカテゴリは見出しごと出さない。見出しは `メイド (2)` のように直下の件数を付け、初期状態で展開しておく
- **各カテゴリの中身と表示名**
  - メイド: `CharacterMgr.GetMaid(i)` のうちボディ読込済みのもの。表示名 `maid.status.fullNameJpStyle`
  - モデル: `ModelProviderHost.GetModels()` (シーンモード・タイムラインの両方で使える提供モデル一覧)。表示名は提供側の管理名 (`ExternalModelEntry.displayName`)
  - 背景: `BGModelManager.modelInfoList` を背景ウィンドウの「追加」タブと同じ `BGModelTree.Build` で木にしたもの。複製 (group > 0) は背景ウィンドウと同じく出さない
  - PNG: `PngPlacementManager.pngObjects` の各ルート。表示名はルートの名前
  - ライト: 追加ライト (`StudioLightManager.lights`) のみ。**メインライトは出さない**。LightWindow の方針 (メインライトは LightMain 経由でしか正しく編集できないため SelectionManager に載せない) に合わせる
  - サブカメラ: `SubCameraManager.subCameras` のカメラ。表示名 `SubCameraData.displayName`
- **アタッチ**: モデルのルートの親をたどって `Maid` が見つかれば、そのメイドの子に置く。付け先メイドが一覧に無い (ボディ未読込など) ときはモデルカテゴリの直下に置く。すべてのモデルがアタッチ中ならモデルカテゴリの見出しは出ない
- **行の見た目**: 選択中はアクセント色。対象が非アクティブなら GameObject ビューと同じく ` (無効)` を付ける。見出し行はクリックしても何も選ばない
- **選択の強調・展開**: 選択物そのものが一覧に無いとき (GameObject ビューでメッシュの子を選んだ等) は、Transform の祖先で最も近い配置物の行を強調・展開する。選択した直後に行がまだ無い (配置した直後で次のポーリング前) ときは、組み直しの後に展開する
- **変化検知**: 0.5 秒ごとに集め直し、カテゴリ・ID・親 ID・ラベル・対象参照の並びが前回と 1 つでも違えば組み直す。同じなら木も行も触らない
- **背景モデル一覧の同期**: 列挙はタイムライン有効時の `BGModelManager.LateUpdate` でしか同期されない。タイムラインが無効なときだけ、背景ウィンドウと同じく `SyncToCurrentBg()` を呼んでから読む。タイムライン有効時に呼ぶと、背景切替を先に消費して LateUpdate 側の `SetupModels` (キーに沿った複製の生成) を飛ばしてしまうため

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること。COM3D2 (2.0) は .NET 3.5 / Unity 5.6 なので `GetComponentInParent(bool includeInactive)` など新しい Unity API は使わない
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`GetInstanceID`, `new GameObject`, `Quaternion.Euler`, `GUI.*` 等) はテストから呼べない。`PlacedObjectTree` は `target` (GameObject) を代入・参照比較 (`ReferenceEquals`) するだけにし、Unity の `==` や メンバーアクセスをしないこと
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)
- `MTEUtils/` はサブモジュール。本計画では変更しない (`GUITreeView` / `GUIView` の公開 API だけを使う)
- `StudioLightManager` は SE (`COM3D2.SceneEditor.Plugin`) と MTE (`COM3D2.MotionTimelineEditor.Plugin`) に同名がある。本計画で使うのは SE 側 (`Manager/StudioLightManager.cs`)。MTE 側の型は `MTEP.` の別名で書く
- 実機検証は通常シーン (撮影モード非対応)。プラグイン DLL の差し替えはゲーム再起動が要る (`com3d25-devbridge:restart-verify` スキル)

## Review Focus

1. モデルをメイドにアタッチ → 次のポーリングでモデルがメイドの子へ移り、アタッチ解除で モデルカテゴリへ戻る。付け先メイドを外したときも行が消えない — Task 1 のテスト「付け先メイドが一覧に無いモデルはモデルカテゴリ直下に出す」、Task 3 の実機確認 2
2. SceneView でモデル・PNG・背景モデル・メイドをクリック → Hierarchy (配置物) で該当行が強調され、閉じていたカテゴリ・付け先メイドが展開されてスクロールされる。GameObject ビューで子メッシュを選んだときは最も近い配置物の行が強調される — Task 1 のテスト「祖先IDはルート側から並ぶ」、Task 3 の実機確認 3・4
3. 一覧が変わらない間は木を組み直さない (毎フレーム・毎ポーリングで Build しない) — Task 1 の `SameSources` のテスト群、Task 3 の実機確認 6 (`PlacedObjectTree.Build` の呼び出し回数)
4. 同じ GameObject を 2 経路で提供された・親 ID が循環した・親が自分自身、といった入力でも例外にならず全件が一度ずつ出る (GUITreeView は ID で展開状態を持つため重複 ID は開閉が連動する) — Task 1 のテスト「同じIDの重複は最初の1件だけ出す」「親子が循環しても全件が出る」「自分自身を親にしてもカテゴリ直下に出る」
5. 配置物を削除した直後 (次のポーリング前) にその行をクリックしても例外にならず、行はその場で消える — Task 3 の `isAlive` / `onSelected` の null 判定、実機確認 5
6. タイムライン有効中に背景を切り替えても背景モデルのキー (複製) が正しく再生される (Hierarchy が `SyncToCurrentBg` を先取りしない) — Task 2 の実機確認 3

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/PlacedObjectTree.cs` | カテゴリ列挙・入力 DTO・ノード・木の組み立て・変化検知・祖先 ID (純ロジック) |
| Create `source/COM3D2.SceneEditor.Plugin/PlacedObjectCollector.cs` | ゲーム・各マネージャーから `PlacedObjectSource` を集める |
| Modify `source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs` | ビュー切り替えタブ、配置物ツリー、選択の強調・展開 |
| Modify `source/COM3D2.SceneEditor.Plugin/Config.cs` | `hierarchyViewMode` |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規 2 ファイル |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/PlacedObjectTreeTests.cs` | 木の組み立てと変化検知のテスト |
| Modify `docs-site/guide/scene-view.md` | Hierarchy 節の説明 |

---

### Task 1: 配置物の木を組み立てる純ロジック

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/PlacedObjectTree.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (`<Compile Include="BGModelTree.cs" />` の直後)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/PlacedObjectTreeTests.cs`

**Interfaces:**
- Produces:
  - `enum PlacedObjectCategory { Maid, Model, Background, Png, Light, SubCamera }` (定義順が表示順)
  - `class PlacedObjectSource { PlacedObjectCategory category; int id; string label; int parentId = PlacedObjectTree.NoParent; GameObject target; }`
  - `class PlacedObjectNode { int id; string label; GameObject target; bool isCategory; List<PlacedObjectNode> children; PlacedObjectNode parent { get; } }`
  - `PlacedObjectTree.NoParent = 0`
  - `PlacedObjectTree.Build(IList<PlacedObjectSource> sources) : PlacedObjectTree` (null 可)
  - `PlacedObjectTree.roots : List<PlacedObjectNode>` (カテゴリ見出しの並び)
  - `PlacedObjectTree.Contains(int id) : bool`
  - `PlacedObjectTree.GetAncestorIds(int id) : List<int>` (ルート側から。無ければ空)
  - `PlacedObjectTree.GetCategoryId(PlacedObjectCategory) : int` / `GetCategoryName(PlacedObjectCategory) : string`
  - `PlacedObjectTree.SameSources(IList<PlacedObjectSource> a, IList<PlacedObjectSource> b) : bool`

- [ ] **Step 1: 失敗するテストを書く**

`PlacedObjectTreeTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>Hierarchy 配置物ビューの木の組み立てと変化検知を固定する</summary>
    public class PlacedObjectTreeTests
    {
        private static PlacedObjectSource Src(
            PlacedObjectCategory category, int id, string label, int parentId = PlacedObjectTree.NoParent)
        {
            return new PlacedObjectSource
            {
                category = category,
                id = id,
                label = label,
                parentId = parentId,
            };
        }

        private static PlacedObjectNode FindCategory(PlacedObjectTree tree, PlacedObjectCategory category)
        {
            var id = PlacedObjectTree.GetCategoryId(category);
            return tree.roots.FirstOrDefault(n => n.id == id);
        }

        private static List<PlacedObjectNode> AllNodes(IEnumerable<PlacedObjectNode> nodes)
        {
            var result = new List<PlacedObjectNode>();
            foreach (var node in nodes)
            {
                result.Add(node);
                result.AddRange(AllNodes(node.children));
            }
            return result;
        }

        [Fact]
        public void 組み立て_空の一覧はカテゴリを出さない()
        {
            Assert.Empty(PlacedObjectTree.Build(new List<PlacedObjectSource>()).roots);
            Assert.Empty(PlacedObjectTree.Build(null).roots);
        }

        [Fact]
        public void 組み立て_カテゴリは定義順で並び空のカテゴリは省く()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Light, 50, "Light1"),
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Png, 40, "PNG1"),
            });

            Assert.Equal(
                new[]
                {
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Maid),
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Png),
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Light),
                },
                tree.roots.Select(n => n.id).ToArray());
            Assert.All(tree.roots, n => Assert.True(n.isCategory));
            Assert.All(tree.roots, n => Assert.Null(n.target));
        }

        [Fact]
        public void 組み立て_カテゴリ見出しに直下の件数を出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Maid, 11, "メイドB"),
            });

            Assert.Equal("メイド (2)", FindCategory(tree, PlacedObjectCategory.Maid).label);
        }

        [Fact]
        public void 組み立て_カテゴリ内は入力順を保つ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Png, 42, "c"),
                Src(PlacedObjectCategory.Png, 40, "a"),
                Src(PlacedObjectCategory.Png, 41, "b"),
            });

            Assert.Equal(new[] { "c", "a", "b" },
                FindCategory(tree, PlacedObjectCategory.Png).children.Select(n => n.label).ToArray());
        }

        [Fact]
        public void アタッチ_モデルは付け先メイドの子になる()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
            });

            var maid = FindCategory(tree, PlacedObjectCategory.Maid).children.Single();
            Assert.Equal(20, maid.children.Single().id);
            Assert.Same(maid, maid.children[0].parent);
            // モデルがすべてアタッチ中ならモデルの見出しは出ない
            Assert.Null(FindCategory(tree, PlacedObjectCategory.Model));
        }

        [Fact]
        public void アタッチ_付け先メイドが一覧に無いモデルはモデルカテゴリ直下に出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 99),
            });

            Assert.Equal(20, FindCategory(tree, PlacedObjectCategory.Model).children.Single().id);
        }

        [Fact]
        public void アタッチ_親が入力の後ろにあっても付く()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
            });

            Assert.Equal(20, FindCategory(tree, PlacedObjectCategory.Maid).children.Single().children.Single().id);
        }

        [Fact]
        public void 背景_親IDの入れ子を保つ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Background, 30, "Stage"),
                Src(PlacedObjectCategory.Background, 31, "Floor", parentId: 30),
                Src(PlacedObjectCategory.Background, 32, "Tile", parentId: 31),
                Src(PlacedObjectCategory.Background, 33, "Wall", parentId: 30),
            });

            var stage = FindCategory(tree, PlacedObjectCategory.Background).children.Single();
            Assert.Equal(new[] { 31, 33 }, stage.children.Select(n => n.id).ToArray());
            Assert.Equal(32, stage.children[0].children.Single().id);
            Assert.Equal("背景 (1)", FindCategory(tree, PlacedObjectCategory.Background).label);
        }

        [Fact]
        public void 異常入力_同じIDの重複は最初の1件だけ出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "先"),
                Src(PlacedObjectCategory.Png, 20, "後"),
            });

            var all = AllNodes(tree.roots).Where(n => !n.isCategory).ToList();
            Assert.Single(all);
            Assert.Equal("先", all[0].label);
            Assert.Null(FindCategory(tree, PlacedObjectCategory.Png));
        }

        [Fact]
        public void 異常入力_親子が循環しても全件が出る()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Background, 30, "A", parentId: 31),
                Src(PlacedObjectCategory.Background, 31, "B", parentId: 30),
            });

            var ids = AllNodes(tree.roots).Where(n => !n.isCategory).Select(n => n.id).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 30, 31 }, ids);
        }

        [Fact]
        public void 異常入力_自分自身を親にしてもカテゴリ直下に出る()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Png, 40, "PNG1", parentId: 40),
            });

            Assert.Equal(40, FindCategory(tree, PlacedObjectCategory.Png).children.Single().id);
        }

        [Fact]
        public void 異常入力_ラベルがnullでも空文字として扱う()
        {
            var tree = PlacedObjectTree.Build(new[] { Src(PlacedObjectCategory.Png, 40, null) });

            Assert.Equal("", FindCategory(tree, PlacedObjectCategory.Png).children.Single().label);
        }

        [Fact]
        public void 祖先_祖先IDはルート側から並ぶ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
            });

            Assert.True(tree.Contains(20));
            Assert.Equal(
                new[] { PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Maid), 10 },
                tree.GetAncestorIds(20).ToArray());
        }

        [Fact]
        public void 祖先_一覧に無いIDは空でContainsはfalse()
        {
            var tree = PlacedObjectTree.Build(new[] { Src(PlacedObjectCategory.Maid, 10, "メイドA") });

            Assert.False(tree.Contains(999));
            Assert.Empty(tree.GetAncestorIds(999));
        }

        [Fact]
        public void カテゴリID_カテゴリごとに異なり0ではない()
        {
            var ids = new[]
            {
                PlacedObjectCategory.Maid, PlacedObjectCategory.Model, PlacedObjectCategory.Background,
                PlacedObjectCategory.Png, PlacedObjectCategory.Light, PlacedObjectCategory.SubCamera,
            }.Select(PlacedObjectTree.GetCategoryId).ToArray();

            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.DoesNotContain(PlacedObjectTree.NoParent, ids);
        }

        [Fact]
        public void 変化検知_同じ内容なら変化なし()
        {
            var a = new List<PlacedObjectSource> { Src(PlacedObjectCategory.Maid, 10, "メイドA") };
            var b = new List<PlacedObjectSource> { Src(PlacedObjectCategory.Maid, 10, "メイドA") };

            Assert.True(PlacedObjectTree.SameSources(a, b));
            Assert.True(PlacedObjectTree.SameSources(null, new List<PlacedObjectSource>()));
        }

        [Fact]
        public void 変化検知_ラベルの変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子") },
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子2") }));
        }

        [Fact]
        public void 変化検知_親の変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子") },
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10) }));
        }

        [Fact]
        public void 変化検知_並びと件数の変化を拾う()
        {
            var x = Src(PlacedObjectCategory.Png, 40, "a");
            var y = Src(PlacedObjectCategory.Png, 41, "b");

            Assert.False(PlacedObjectTree.SameSources(new[] { x, y }, new[] { y, x }));
            Assert.False(PlacedObjectTree.SameSources(new[] { x, y }, new[] { x }));
        }

        [Fact]
        public void 変化検知_カテゴリの変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "a") },
                new[] { Src(PlacedObjectCategory.Png, 20, "a") }));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter PlacedObjectTreeTests`
Expected: コンパイルエラー (`PlacedObjectTree` が無い)

- [ ] **Step 3: 実装する**

`PlacedObjectTree.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>Hierarchy の配置物ビューのカテゴリ。定義順がそのまま表示順</summary>
    public enum PlacedObjectCategory
    {
        Maid,
        Model,
        Background,
        Png,
        Light,
        SubCamera,
    }

    /// <summary>
    /// 配置物 1 件の入力。PlacedObjectCollector がゲームの実体から作る。
    /// 木の組み立てと変化検知は target に触れずこの値だけで行うため、テストでは target を null にできる
    /// </summary>
    public class PlacedObjectSource
    {
        public PlacedObjectCategory category;
        /// <summary>対象 GameObject の GetInstanceID。行の ID (展開状態・スクロール予約) にも使う</summary>
        public int id;
        public string label;
        /// <summary>親にする配置物の id。NoParent または一覧に無い id ならカテゴリ直下に置く</summary>
        public int parentId = PlacedObjectTree.NoParent;
        public GameObject target;
    }

    /// <summary>配置物ビューの 1 行。カテゴリ見出しも同じ型で表す</summary>
    public class PlacedObjectNode
    {
        public readonly int id;
        public readonly string label;
        /// <summary>行を選んだときに選択する GameObject。カテゴリ見出しは null</summary>
        public readonly GameObject target;
        public readonly bool isCategory;
        public readonly List<PlacedObjectNode> children = new List<PlacedObjectNode>();
        public PlacedObjectNode parent { get; private set; }

        public PlacedObjectNode(int id, string label, GameObject target, bool isCategory)
        {
            this.id = id;
            this.label = label;
            this.target = target;
            this.isCategory = isCategory;
        }

        public void AddChild(PlacedObjectNode child)
        {
            child.parent = this;
            children.Add(child);
        }
    }

    /// <summary>
    /// 配置物の平坦な一覧から、カテゴリ見出しを根とする木を組み立てる。
    /// アタッチしたモデル (親 = メイド) や背景モデルの入れ子は parentId で表し、カテゴリをまたいで付けられる。
    /// Unity の実体に触れないので、組み立て・変化検知・祖先の引き当てをテストで固定できる
    /// </summary>
    public class PlacedObjectTree
    {
        public const int NoParent = 0;

        // カテゴリ見出しの ID。GetInstanceID は正負どちらも取るが int.MinValue 付近は実際には使われないため、
        // ここに寄せて配置物の ID との衝突を避ける
        private const int CategoryIdBase = int.MinValue;

        private static readonly int CategoryCount = Enum.GetValues(typeof(PlacedObjectCategory)).Length;

        private readonly List<PlacedObjectNode> _roots = new List<PlacedObjectNode>();
        private readonly Dictionary<int, PlacedObjectNode> _nodeMap = new Dictionary<int, PlacedObjectNode>();

        /// <summary>カテゴリ見出しの並び (中身のあるカテゴリだけ)</summary>
        public List<PlacedObjectNode> roots => _roots;

        private PlacedObjectTree()
        {
        }

        public static int GetCategoryId(PlacedObjectCategory category)
        {
            return CategoryIdBase + (int)category;
        }

        public static string GetCategoryName(PlacedObjectCategory category)
        {
            switch (category)
            {
                case PlacedObjectCategory.Maid: return "メイド";
                case PlacedObjectCategory.Model: return "モデル";
                case PlacedObjectCategory.Background: return "背景";
                case PlacedObjectCategory.Png: return "PNG";
                case PlacedObjectCategory.Light: return "ライト";
                case PlacedObjectCategory.SubCamera: return "サブカメラ";
                default: return category.ToString();
            }
        }

        public static PlacedObjectTree Build(IList<PlacedObjectSource> sources)
        {
            var tree = new PlacedObjectTree();
            if (sources == null)
            {
                return tree;
            }

            // 同じ ID が二度来たら (同じ GameObject を複数経路で提供された等) 最初の 1 件だけ採る。
            // GUITreeView は ID で展開状態を持つため、重複を残すと片方の開閉がもう片方にも効いてしまう
            var accepted = new List<PlacedObjectSource>(sources.Count);
            foreach (var source in sources)
            {
                if (source == null || tree._nodeMap.ContainsKey(source.id))
                {
                    continue;
                }
                tree._nodeMap[source.id] = new PlacedObjectNode(source.id, source.label ?? "", source.target, false);
                accepted.Add(source);
            }

            // 親へ付ける。親が一覧に無い・自分自身・付けると循環するものはカテゴリ直下へ置き、行を失わない
            var categoryItems = new List<PlacedObjectNode>[CategoryCount];
            for (var i = 0; i < CategoryCount; i++)
            {
                categoryItems[i] = new List<PlacedObjectNode>();
            }

            foreach (var source in accepted)
            {
                var node = tree._nodeMap[source.id];
                PlacedObjectNode parent;
                if (source.parentId != NoParent &&
                    tree._nodeMap.TryGetValue(source.parentId, out parent) &&
                    !IsSelfOrAncestor(node, parent))
                {
                    parent.AddChild(node);
                }
                else
                {
                    categoryItems[(int)source.category].Add(node);
                }
            }

            for (var i = 0; i < CategoryCount; i++)
            {
                var items = categoryItems[i];
                if (items.Count == 0)
                {
                    continue;
                }

                var category = (PlacedObjectCategory)i;
                var header = new PlacedObjectNode(
                    GetCategoryId(category), GetCategoryName(category) + " (" + items.Count + ")", null, true);
                foreach (var item in items)
                {
                    header.AddChild(item);
                }
                tree._roots.Add(header);
                tree._nodeMap[header.id] = header;
            }

            return tree;
        }

        /// <summary>candidate から親をたどって node に行き着くか (= candidate の子にすると循環する)</summary>
        private static bool IsSelfOrAncestor(PlacedObjectNode node, PlacedObjectNode candidate)
        {
            for (var p = candidate; p != null; p = p.parent)
            {
                if (p == node)
                {
                    return true;
                }
            }
            return false;
        }

        public bool Contains(int id)
        {
            return _nodeMap.ContainsKey(id);
        }

        /// <summary>id の行を出すために展開する祖先の ID をルート側から並べる。一覧に無ければ空</summary>
        public List<int> GetAncestorIds(int id)
        {
            var result = new List<int>();
            PlacedObjectNode node;
            if (!_nodeMap.TryGetValue(id, out node))
            {
                return result;
            }

            for (var p = node.parent; p != null; p = p.parent)
            {
                result.Add(p.id);
            }
            result.Reverse();
            return result;
        }

        /// <summary>
        /// 前回集めた一覧と同じか。1 項目でも違えば組み直す。
        /// target は Unity の == (ネイティブ呼び出し) を避けて参照で比べる。
        /// 破棄された GameObject は id が変わらないが、行は GUITreeView の isAlive で消える
        /// </summary>
        public static bool SameSources(IList<PlacedObjectSource> a, IList<PlacedObjectSource> b)
        {
            var countA = a != null ? a.Count : 0;
            var countB = b != null ? b.Count : 0;
            if (countA != countB)
            {
                return false;
            }

            for (var i = 0; i < countA; i++)
            {
                var x = a[i];
                var y = b[i];
                if (ReferenceEquals(x, y))
                {
                    continue;
                }
                if (x == null || y == null ||
                    x.category != y.category ||
                    x.id != y.id ||
                    x.parentId != y.parentId ||
                    !string.Equals(x.label, y.label, StringComparison.Ordinal) ||
                    !ReferenceEquals(x.target, y.target))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
```

csproj の `<Compile Include="BGModelTree.cs" />` の直後に `<Compile Include="PlacedObjectTree.cs" />` を足す。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter PlacedObjectTreeTests` が PASS。続けて全テスト (`dotnet test source/COM3D2.SceneEditor.Plugin.Tests`) も PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/PlacedObjectTree.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/PlacedObjectTreeTests.cs
git commit -m "feat(hierarchy): 配置物の一覧からカテゴリ別の木を組み立てる"
```

### Task 2: ゲームの実体から配置物を集める

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/PlacedObjectCollector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (`PlacedObjectTree.cs` の直後)

**Interfaces:**
- Consumes: `PlacedObjectSource`, `PlacedObjectCategory`, `PlacedObjectTree.NoParent` (Task 1)、`ModelProviderHost.GetModels()`、`BGModelTree.Build(IList<MTEP.BGModelInfo>)`、`MTEP.BGModelManager.instance.modelInfoList / SyncToCurrentBg()`、`MTEP.TimelineManager.instance.IsValidData()`、`PngPlacementManager.instance.pngObjects`、`StudioLightManager.instance.lights` (SE 側)、`MTEP.SubCameraManager.instance.subCameras`
- Produces: `PlacedObjectCollector.Collect(List<PlacedObjectSource> results)` (results へ追記する。呼び出し側で Clear する)

- [ ] **Step 1: 参照先の名前を確かめる**

次を読んで、下のコードの名前・アクセス修飾子と一致することを確かめる (違えば実物に合わせる):
- `ModelProviderHost.cs:9-14,76` (`ExternalModelEntry.obj / displayName`)
- `BGModelTree.cs:7-25` (`BGModelNode.info / children`)
- `Timeline/Manager/BGModelManager.cs:8-15,51,151` (`BGModelInfo.gameObject / displayName`、`modelInfoList`、`SyncToCurrentBg`)
- `Timeline/Manager/TimelineManager.cs:167,2254` (`instance`、`IsValidData`)
- `Manager/PngPlacementManager.cs:23-64,152` (`PngObjectData.rootObject`、`pngObjects`)
- `Manager/StudioLightManager.cs:33` (`lights : List<Light>`)
- `Timeline/Manager/SubCameraManager.cs:73-78,188` (`SubCameraData.camera / displayName`、`subCameras`)

- [ ] **Step 2: 実装する**

`PlacedObjectCollector.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Hierarchy の配置物ビューに出す物をゲームと各マネージャーから集め、
    /// Unity に依存しない PlacedObjectSource の並びへ写す。木の組み立ては PlacedObjectTree が行う。
    /// 集める順がそのままカテゴリ内の並び順になる
    /// </summary>
    public static class PlacedObjectCollector
    {
        public static void Collect(List<PlacedObjectSource> results)
        {
            CollectMaids(results);
            CollectModels(results);
            CollectBackgroundModels(results);
            CollectPngs(results);
            CollectLights(results);
            CollectSubCameras(results);
        }

        private static void Add(
            List<PlacedObjectSource> results, PlacedObjectCategory category,
            GameObject go, string label, int parentId)
        {
            results.Add(new PlacedObjectSource
            {
                category = category,
                id = go.GetInstanceID(),
                label = string.IsNullOrEmpty(label) ? go.name : label,
                parentId = parentId,
                target = go,
            });
        }

        private static void CollectMaids(List<PlacedObjectSource> results)
        {
            var gameMain = GameMain.Instance;
            var characterMgr = gameMain != null ? gameMain.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }

            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                // 呼び出し途中 (ボディ未読込) のメイドはまだ操作できないので出さない
                if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
                {
                    continue;
                }
                Add(results, PlacedObjectCategory.Maid, maid.gameObject,
                    maid.status.fullNameJpStyle, PlacedObjectTree.NoParent);
            }
        }

        /// <summary>
        /// 提供モデル。アタッチ中のモデルは提供側がメイドのボーンの子へ付け替えているので、
        /// 実際の親子関係から付け先メイドを求める。タイムラインのキー値
        /// (StudioModelStat.attachMaidSlotNo) はタイムライン有効時しか同期されず、シーンモードで使えないため
        /// </summary>
        private static void CollectModels(List<PlacedObjectSource> results)
        {
            foreach (var entry in ModelProviderHost.GetModels())
            {
                var ownerMaid = FindOwnerMaid(entry.obj.transform.parent);
                Add(results, PlacedObjectCategory.Model, entry.obj, entry.displayName,
                    ownerMaid != null ? ownerMaid.gameObject.GetInstanceID() : PlacedObjectTree.NoParent);
            }
        }

        /// <summary>
        /// transform から親をたどって最初に見つかったメイド。
        /// GetComponentInParent は 2.0 の Unity では非アクティブな親を飛ばし、
        /// includeInactive 引数も無いため自前でたどる
        /// </summary>
        private static Maid FindOwnerMaid(Transform transform)
        {
            for (var t = transform; t != null; t = t.parent)
            {
                var maid = t.GetComponent<Maid>();
                if (maid != null)
                {
                    return maid;
                }
            }
            return null;
        }

        /// <summary>背景モデルの木。背景ウィンドウの「追加」タブと同じ木 (複製は出さない)</summary>
        private static void CollectBackgroundModels(List<PlacedObjectSource> results)
        {
            var bgModelManager = MTEP.BGModelManager.instance;

            // 列挙はタイムライン有効時の LateUpdate でしか同期されないため、無効時だけ背景ウィンドウと同じく
            // ここで同期する (同期済みなら no-op)。有効時に呼ぶと背景切替をこちらが先に消費し、
            // LateUpdate 側の SetupModels (キーに沿った複製の生成) が走らなくなる
            if (!MTEP.TimelineManager.instance.IsValidData())
            {
                bgModelManager.SyncToCurrentBg();
            }

            foreach (var node in BGModelTree.Build(bgModelManager.modelInfoList))
            {
                AddBackgroundNode(results, node, PlacedObjectTree.NoParent);
            }
        }

        private static void AddBackgroundNode(List<PlacedObjectSource> results, BGModelNode node, int parentId)
        {
            var go = node.info.gameObject;
            Add(results, PlacedObjectCategory.Background, go, node.info.displayName, parentId);

            var id = go.GetInstanceID();
            foreach (var child in node.children)
            {
                AddBackgroundNode(results, child, id);
            }
        }

        private static void CollectPngs(List<PlacedObjectSource> results)
        {
            foreach (var png in PngPlacementManager.instance.pngObjects)
            {
                if (png != null && png.rootObject != null)
                {
                    Add(results, PlacedObjectCategory.Png, png.rootObject, png.name, PlacedObjectTree.NoParent);
                }
            }
        }

        /// <summary>
        /// 追加ライトだけを出す。メインライトは LightMain 経由でしか正しく編集できず、
        /// LightWindow も SelectionManager に載せない方針なので揃える
        /// </summary>
        private static void CollectLights(List<PlacedObjectSource> results)
        {
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null)
                {
                    Add(results, PlacedObjectCategory.Light, light.gameObject,
                        light.gameObject.name, PlacedObjectTree.NoParent);
                }
            }
        }

        private static void CollectSubCameras(List<PlacedObjectSource> results)
        {
            foreach (var subCamera in MTEP.SubCameraManager.instance.subCameras)
            {
                if (subCamera != null && subCamera.camera != null)
                {
                    Add(results, PlacedObjectCategory.SubCamera, subCamera.camera.gameObject,
                        subCamera.displayName, PlacedObjectTree.NoParent);
                }
            }
        }
    }
}
```

(`BGModelTree.Build` は `gameObject == null` の情報を捨てるので、`AddBackgroundNode` の `go` は null にならない。`ModelProviderHost.GetModels` も null の GameObject を捨てる)

csproj の `<Compile Include="PlacedObjectTree.cs" />` の直後に `<Compile Include="PlacedObjectCollector.cs" />` を足す。

- [ ] **Step 3: 両構成をビルドし、全テストを通す**

- [ ] **Step 4: 実機確認 (Review Focus 6 の前半)**

ゲームを再起動して DLL を反映し (`com3d25-devbridge:restart-verify`)、通常シーンでエディタを有効にする。メイド 2 人・提供モデル 2 つ (うち 1 つをメイドの頭にアタッチ)・PNG 1 枚・追加ライト 1 つを置き、`eval_csharp` で集めた並びを出す:

```csharp
var list = new System.Collections.Generic.List<COM3D2.SceneEditor.Plugin.PlacedObjectSource>();
COM3D2.SceneEditor.Plugin.PlacedObjectCollector.Collect(list);
return string.Join("\n", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(list,
    s => s.category + " " + s.id + " parent=" + s.parentId + " " + s.label)));
```

確認すること:
1. 各カテゴリの件数と表示名が画面の配置と一致する (背景は背景モデルの数だけ出る)
2. アタッチしたモデルの `parent` がそのメイドの行の `id` と一致し、アタッチしていないモデルは `parent=0`
3. 背景を別の背景に切り替えて再度 `Collect` → 背景の行が新しい背景のモデルに入れ替わる。続けてタイムラインを読み込んだ状態で、背景モデルの複製キーを持つタイムラインの背景を切り替え、複製が従来どおり出ること (Hierarchy が同期を先取りしていないこと)

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/PlacedObjectCollector.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(hierarchy): メイド・モデル・背景・PNG・ライト・サブカメラを配置物として集める"
```

### Task 3: Hierarchy に配置物ビューを足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:95-99` (Hierarchy の節)
- Modify: `source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs` (全体。行番号は現状の 15-283)

**Interfaces:**
- Consumes: `PlacedObjectTree.Build / roots / Contains / GetAncestorIds / GetCategoryId / SameSources`, `PlacedObjectNode`, `PlacedObjectCollector.Collect` (Task 1・2)、`GUITreeView<T>` の公開 API (`SetRoots / SetDirty / Clear / Expand / Reveal / CancelReveal / HandleKeyboard / Draw / searchText`)、`GUIView.DrawTabs(IList<string>, int, float, float, float)`
- Produces: `enum HierarchyViewMode { PlacedObjects, GameObjects }`、`Config.hierarchyViewMode`

- [ ] **Step 1: Config に表示モードを足す**

`HierarchyWindow.cs` の namespace 直下 (クラスの前) に enum を置く:

```csharp
    /// <summary>Hierarchy の表示切り替え。Config に名前で保存される</summary>
    public enum HierarchyViewMode
    {
        PlacedObjects,
        GameObjects,
    }
```

`Config.cs` の `hierarchyVisible` の下に足す:

```csharp
        // Hierarchy の表示。配置物 (メイド・モデル等のカテゴリ別) か、シーンの GameObject 階層か
        public HierarchyViewMode hierarchyViewMode = HierarchyViewMode.PlacedObjects;
```

- [ ] **Step 2: フィールドと配置物ツリーの設定を足す**

定数・フィールドを足す (既存の `_treeView` の下):

```csharp
        private const float ModeTabWidth = 80f;
        private static readonly string[] ViewModeLabels = { "配置物", "GameObject" };

        private readonly GUITreeView<PlacedObjectNode> _placedTreeView = new GUITreeView<PlacedObjectNode>();
        private readonly List<PlacedObjectNode> _placedRoots = new List<PlacedObjectNode>();
        // 前回集めた配置物と、次回の収集に使い回すバッファ。比べて違うときだけ木を組み直す
        private List<PlacedObjectSource> _placedSources = new List<PlacedObjectSource>();
        private List<PlacedObjectSource> _collectBuffer = new List<PlacedObjectSource>();
        private PlacedObjectTree _placedTree = PlacedObjectTree.Build(null);
        // 選択中のオブジェクトに対応する行の ID (選択物そのもの、無ければ最も近い配置物の祖先)。無ければ 0
        private int _selectedPlacedId = 0;
        // 選択したが対応する行がまだ無い (配置直後で次の収集前)。組み直した後に展開する
        private bool _placedRevealPending = false;

        private static bool isPlacedMode => config.hierarchyViewMode == HierarchyViewMode.PlacedObjects;
```

コンストラクタで `SetupPlacedTreeView();` も呼び、メソッドを足す:

```csharp
        /// <summary>配置物ツリーのたどり方と行の見た目。選択の扱いは GameObject ビューと同じ</summary>
        private void SetupPlacedTreeView()
        {
            _placedTreeView.rowHeight = RowHeight;

            _placedTreeView.getId = node => node.id;
            _placedTreeView.getName = node => node.label;
            // 見出しは常に出す。配置物は次の収集を待たず、破棄された時点で行から外す
            _placedTreeView.isAlive = node => node.isCategory || node.target != null;
            _placedTreeView.getChildCount = node => node.children.Count;
            _placedTreeView.getChild = (node, i) => node.children[i];

            _placedTreeView.getLabel = node =>
                node.isCategory || node.target.activeInHierarchy ? node.label : node.label + " (無効)";
            _placedTreeView.getLabelColor = node => IsPlacedSelected(node) ? ACCENT_COLOR : Color.white;
            _placedTreeView.isSelected = IsPlacedSelected;
            _placedTreeView.onSelected = node =>
            {
                // 見出しは選ぶ物が無い。破棄済み (isAlive が次の描画で弾く前) も選ばない
                if (node.isCategory || node.target == null)
                {
                    return;
                }
                selectionManager.Select(node.target);
                OnRowClicked(node.target);
            };

            _placedTreeView.SetRoots(_placedRoots);
            ExpandCategories();
        }

        private bool IsPlacedSelected(PlacedObjectNode node)
        {
            return !node.isCategory && node.id == _selectedPlacedId;
        }

        /// <summary>カテゴリ見出しは最初から開いておく (ID は固定なので木を組む前でも登録できる)</summary>
        private void ExpandCategories()
        {
            foreach (PlacedObjectCategory category in Enum.GetValues(typeof(PlacedObjectCategory)))
            {
                _placedTreeView.Expand(PlacedObjectTree.GetCategoryId(category));
            }
        }
```

(`using System;` を足す)

- [ ] **Step 3: 収集と組み直しを足す**

```csharp
        /// <summary>
        /// 配置物を集め直し、前回と違うときだけ木を組み直す。
        /// 変わらない間は木も行も触らないので、ポーリングしても行の組み直しは起きない
        /// </summary>
        private void RefreshPlacedObjects()
        {
            _lastRefreshTime = Time.realtimeSinceStartup;

            _collectBuffer.Clear();
            PlacedObjectCollector.Collect(_collectBuffer);
            if (PlacedObjectTree.SameSources(_placedSources, _collectBuffer))
            {
                return;
            }

            // 今回の一覧を控え、前回の一覧を次回の収集バッファに回す
            var previous = _placedSources;
            _placedSources = _collectBuffer;
            _collectBuffer = previous;

            _placedTree = PlacedObjectTree.Build(_placedSources);
            _placedRoots.Clear();
            _placedRoots.AddRange(_placedTree.roots);
            // _placedRoots は同じリストの中身を入れ替えているため、参照比較では検出されない
            _placedTreeView.SetDirty();

            // 新しく出た行が選択中の物かもしれないので対応する行を引き直す。
            // 選択時に行が無かった場合だけ、ここで展開・スクロールする
            SyncPlacedSelection(selectionManager.selectedObject, _placedRevealPending);
        }

        /// <summary>
        /// 選択中のオブジェクトに対応する行を求め、reveal なら祖先を開いて画面内へ送る。
        /// 選択物そのものが一覧に無いときは Transform の祖先で最も近い配置物の行を使う
        /// (GameObject ビューでメッシュの子を選んだ場合など)
        /// </summary>
        private void SyncPlacedSelection(GameObject go, bool reveal)
        {
            _selectedPlacedId = FindPlacedId(go);
            _placedRevealPending = go != null && _selectedPlacedId == 0;

            if (!reveal)
            {
                return;
            }
            if (_selectedPlacedId == 0)
            {
                _placedTreeView.CancelReveal();
                return;
            }

            foreach (var ancestorId in _placedTree.GetAncestorIds(_selectedPlacedId))
            {
                _placedTreeView.Expand(ancestorId);
            }
            _placedTreeView.Reveal(_selectedPlacedId);
        }

        private int FindPlacedId(GameObject go)
        {
            for (var t = go != null ? go.transform : null; t != null; t = t.parent)
            {
                var id = t.gameObject.GetInstanceID();
                if (_placedTree.Contains(id))
                {
                    return id;
                }
            }
            return 0;
        }

        /// <summary>表示中のビューの一覧だけを取り直す (非表示のビューは切り替えたときに取る)</summary>
        private void Refresh()
        {
            if (isPlacedMode)
            {
                RefreshPlacedObjects();
            }
            else
            {
                RefreshRoots();
            }
        }
```

(`PlacedObjectTree.Contains` はカテゴリ見出しの ID も含むが、見出しの ID は `int.MinValue` 付近で GameObject の ID と重ならない)

- [ ] **Step 4: 既存の更新経路を表示モードで分ける**

1. `OnShowChanged` と `Update` の `RefreshRoots()` を `Refresh()` に置き換える。`DrawContent` の「更新」ボタンも `Refresh()` にする
2. `OnChangedSceneLevel` に配置物側の破棄を足す:

```csharp
            _placedSources.Clear();
            _placedTree = PlacedObjectTree.Build(null);
            _placedRoots.Clear();
            _selectedPlacedId = 0;
            _placedRevealPending = false;
            _placedTreeView.Clear();
            // Clear は展開状態も捨てるので見出しを開き直す
            ExpandCategories();
```

3. `OnSelectionChanged` を両ビュー向けにする。GameObject ビュー側の処理は `RevealInGameObjectTree(GameObject go)` へそのまま移し (null 時の `CancelReveal` を含む)、次のようにする:

```csharp
        private void OnSelectionChanged(GameObject go)
        {
            RevealInGameObjectTree(go);

            // 配置直後の物を選んだ場合に行が間に合うよう、表示中なら先に集め直す (変化が無ければ組み直さない)
            if (isShowWnd && isPlacedMode)
            {
                RefreshPlacedObjects();
            }
            SyncPlacedSelection(go, true);
        }
```

- [ ] **Step 5: タブと描画を足す**

`DrawContent` を置き換える:

```csharp
        /// <summary>
        /// 行まわりは GUITreeView に委譲し、ここでは表示切り替えタブ・検索欄・更新ボタンの配置と
        /// 矢印キー操作の有効化だけを行う。検索語はビューごとに持つ
        /// </summary>
        protected override void DrawContent()
        {
            if (isPlacedMode)
            {
                _placedTreeView.HandleKeyboard();
            }
            else
            {
                _treeView.HandleKeyboard();
            }

            _view.Init(ToLocalRect(contentRect));

            DrawViewModeTabs();

            // 検索欄 + 手動更新ボタン
            _view.BeginHorizontal();
            {
                var searchWidth = _view.viewRect.width - SearchButtonWidth - Spacing;
                var searchText = isPlacedMode ? _placedTreeView.searchText : _treeView.searchText;
                // 表示を切り替えても入力中の文字が残らないよう、表示ごとに別のコントロールにする
                _view.DrawTextField("", 0f, searchText, searchWidth, RowHeight, value =>
                {
                    if (isPlacedMode)
                    {
                        _placedTreeView.searchText = value;
                    }
                    else
                    {
                        _treeView.searchText = value;
                    }
                }, false, isPlacedMode ? "HierarchyPlacedSearch" : "HierarchyObjectSearch");

                if (_view.DrawButton("更新", SearchButtonWidth, RowHeight))
                {
                    Refresh();
                }
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            // 残りの領域すべてをリストに使う
            var listRect = _view.GetDrawRect(-1, -1);
            if (isPlacedMode)
            {
                _placedTreeView.Draw(_view, listRect);
            }
            else
            {
                _treeView.Draw(_view, listRect);
            }
        }

        /// <summary>配置物 / GameObject の切り替えタブ。切り替えた先の一覧はすぐ取り直して選択行へ送る</summary>
        private void DrawViewModeTabs()
        {
            var current = (int)config.hierarchyViewMode;
            var next = _view.DrawTabs(ViewModeLabels, current, ModeTabWidth, RowHeight);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「5px + margin」になるため、通常の行間に詰める
            _view.currentPos.y -= 5 + _view.margin;

            if (next == current)
            {
                return;
            }

            config.hierarchyViewMode = (HierarchyViewMode)next;
            config.dirty = true;

            // 非表示だったビューは取り直していないので、ここで取り直す
            Refresh();
            var selected = selectionManager.selectedObject;
            if (isPlacedMode)
            {
                SyncPlacedSelection(selected, true);
            }
            else
            {
                RevealInGameObjectTree(selected);
            }
        }
```

`DrawTextField` の入力中キャッシュが切り替え前の文字列を残す場合 (切り替えても検索欄の表示が変わらない) は、`GUIView.DrawTextField` の実装を読み、キャッシュを持たない描画経路 (または呼び出しごとに別 ID) にする。

クラスの summary を「シーンの配置物 (カテゴリ別) または GameObject ツリーを表示するウィンドウ。…」に直し、配置物ビューは変化を検知したときだけ組み直すことを 1 文足す。

- [ ] **Step 6: 両構成をビルドし、全テストを通す**

- [ ] **Step 7: 実機確認 (Review Focus 1〜5)**

ゲームを再起動して DLL を反映し、通常シーンで Hierarchy を開く (`screenshot` で確認)。

1. 既定で「配置物」タブが選ばれ、メイド / モデル / 背景 / PNG / ライト / サブカメラのうち中身のあるカテゴリだけが開いた状態で並ぶ。「GameObject」タブで従来の階層表示に戻り、ウィンドウを閉じて開き直しても選んだタブが保たれる
2. 提供モデルをメイドにアタッチ → 0.5 秒以内にメイドの子へ移る。アタッチを外すとモデルカテゴリへ戻る。メイドを外したとき、アタッチしていたモデルがモデルカテゴリに出るか、モデルごと消えるかを確かめる (メイド削除でモデルが先に外されるかはコードで未確認)。どちらでも一覧が壊れず、消えた行が残らないこと
3. SceneView でモデル・PNG・背景モデル・メイドをそれぞれクリック → 配置物ビューで該当行がアクセント色になり、閉じていたカテゴリ・付け先メイドが開いて行が画面内に来る。配置物ビューの行をクリックすると Inspector がその物に切り替わり、ダブルクリックで SceneView がフォーカスする。見出し行のクリックでは選択が変わらない
4. GameObject タブでメイドのボーン (子) を選んでから配置物タブへ切り替える → そのメイドの行が強調される
5. PNG を追加した直後に Inspector で選択 → 次のポーリングで PNG の行が現れ、強調・展開される。PNG を削除した直後にその行をクリックしても例外が出ない (`tail_log`) で行が消える
6. `profile_add` で `COM3D2.SceneEditor.Plugin.PlacedObjectTree.Build` を計測し、何も変えずに 10 秒置いて呼び出し回数が 0 (または変化の回数だけ) であること。`PlacedObjectCollector.Collect` は 0.5 秒に 1 回程度、Hierarchy を閉じると 0 回。終わったら `profile_remove`
7. 検索欄に「帽子」などを入れると配置物ビューが一致行だけのフラット表示になり、タブを切り替えると各ビューの検索語が保たれる

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs
git commit -m "feat(hierarchy): 配置物ビューと GameObject ビューの切り替えを追加する"
```

### Task 4: ドキュメント

**Files:**
- Modify: `docs-site/guide/scene-view.md` (`## Hierarchy` 節、現状 30-44 行)

- [ ] **Step 1: Hierarchy 節を書き直す**

冒頭の「シーン内の GameObject をツリー表示します。」を、上部のタブで「配置物」と「GameObject」を切り替えられる説明に直し、配置物ビューについて次を足す:

- カテゴリ: メイド / モデル / 背景 / PNG / ライト / サブカメラ (中身の無いカテゴリは出ない)
- メイドにアタッチしたモデルは付け先メイドの下に出る
- ライトは追加ライトのみ (メインライトはライトウィンドウで編集する)
- 背景は背景モデルの木 (複製は背景ウィンドウの「管理」タブで扱う)
- SceneView などで選んだ物は該当行が強調され、必要なら親を開いてスクロールする

操作表に「行のダブルクリック | SceneView のカメラをそのオブジェクトへフォーカス」を足す (現状の表に無いので両ビュー共通として足す)。既存の warning (ボーン階層の描画負荷) は GameObject ビューの注記として残す。

- [ ] **Step 2: コミット**

```bash
git add docs-site/guide/scene-view.md
git commit -m "docs(hierarchy): 配置物ビューの説明を追加する"
```

## レビュー却下メモ

- なし

## レビュー却下メモ

- `FindOwnerMaid` を `GetComponentInParent<Maid>()` に寄せる — 却下。2.0 の Unity では非アクティブな親を飛ばすため自前でたどる理由が計画内 (FindOwnerMaid の summary) に書いてある
- カテゴリ見出し ID (`int.MinValue + 番号`) と `GetInstanceID()` の衝突 — 却下。実質起きない前提で、起きても描画は壊れない
