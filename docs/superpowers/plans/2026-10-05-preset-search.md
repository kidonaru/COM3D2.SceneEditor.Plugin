# シーンプリセットの検索バー Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** シーンプリセットウィンドウに検索行を足し、プリセットフォルダ全体と SceneCapture 配下からプリセットを名前の部分一致で探して、タイルで適用できるようにする。

**Architecture:**
- 検索の純粋ロジック（全ファイルの収集と照合、ルートからの相対パス）を静的クラス `ScenePresetSearch` に切り出し、ゲーム外テストで固定する
- `PresetWindow` は `PngPlacementWindow` の検索パターン（`_searchText` + `TempTileViewContent _searchRoot`）を移植する。`TempTileViewContent` は子の `parent` を書き換えないので、元のツリーを壊さずに平坦な一覧を作れ、`parent` を辿って相対パスも出せる
- SceneCapture のファイル項目には、ツリー構築時（`ScenePresetManager.MarkSceneCaptureItems`）にタグ「SC」を付ける。SceneCapture フォルダ自体はリスト表示なのでタグは検索結果のタイルにだけ見える
- 検索結果は「`rootItem` の参照」と「検索文字列」が前回の作成時から変わったときだけ作り直す（保存・削除・更新・自動ロードはいずれも `Reload` で `rootItem` を新しい参照に差し替える）

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-05-user-feedback-batch-design.md` の「A. シーンプリセットの検索バー」

## Global Constraints

- 照合は名前の部分一致、`StringComparison.OrdinalIgnoreCase`。ひらがな/カタカナの同一視はしない
- 検索範囲は表示中のフォルダではなく `ScenePresetManager.rootItem` 全体。SceneCapture 配下も含める
- 検索結果は常にタイル表示（SceneCapture フォルダ表示中でも）
- SceneCapture 項目のタグは文字列 `"SC"`
- 一致なしの文言: 「該当するプリセットはありません」
- 検索中は「<」と「保存」を無効化。削除は通常プリセットだけ（SceneCapture は今と同じく `canDelete = false`）
- マウスオーバー表示はルートからの相対パス（区切りは `/`、ルート名「ScenePreset」は含めない。SceneCapture は `SceneCapture/...` で始まる）
- 検索文字列が変わったらスクロール位置を先頭へ戻す
- XML（タイムライン・シーンプリセット）の版は上げない。保存形式への影響は無い
- コードのコメント・ログは日本語。両構成（COM3D2 / COM3D25）でビルドする

## Review Focus

1. **検索中に保存・削除・更新が起きる**: `Reload` で `rootItem` が差し替わり、古い `_searchRoot` の項目はサムネが破棄済み（`ClearThumbnails`）。次のフレームで作り直されて、消えた項目が残らないこと → Task 2 で `rootItem` 参照の比較により作り直す。テストは Task 1 の `Collect` が新しいツリーから集め直すことで担保し、実機確認で削除後の件数を見る
2. **前後の空白だけの検索文字列**: 空白は普通の文字として照合する（PngPlacementWindow と同じ）。空文字のときだけ検索解除 → Task 1 のテスト `空文字は何も返さない` で固定
3. **SceneCapture 配下のフォルダ表示中に検索を始める/やめる**: 検索中はタイル、解除するとリスト表示に戻る（`currentDirItem` を書き換えないため）→ Task 2 の分岐で担保、実機確認で screenshot
4. **同名プリセットが別フォルダにある**: 両方とも結果に出て、相対パスで区別できる → Task 1 のテスト `同名でも別フォルダなら両方返す` と `相対パス_*`
5. **読み込み中（isLoading）に検索結果をクリック**: 既存どおり `_view.SetEnabled(false)` が検索行・結果にも効く（検索行を try 内で描く）→ Task 2 の配置で担保

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/ScenePresetSearch.cs` | 新規 | 検索の純粋ロジック（収集・照合・相対パス） |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | `<Compile Include="ScenePresetSearch.cs" />` を足す |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 変更 | `ScenePresetItem` に SC タグ定数、`MarkSceneCaptureItems` でタグ付け |
| `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs` | 変更 | 検索行・検索結果タイル・ボタン無効化 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetSearchTests.cs` | 新規 | `ScenePresetSearch` のテスト |

テストプロジェクトはビルド済みのプラグイン DLL を参照するため、ソースの追加は csproj への登録だけで足りる（テスト csproj は SDK 形式で自動収集）。

---

### Task 1: 検索ロジック `ScenePresetSearch`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ScenePresetSearch.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="ScenePresetNaming.cs" />` の次の行）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetSearchTests.cs`

**Interfaces:**
- Produces:
  - `public static class ScenePresetSearch`
  - `public static void Collect(ITileViewContent root, string text, List<ITileViewContent> result)` — `result` を消さずに追記する。`text` が null/空なら何もしない。並びは `GetAllFiles` の順（ファイルが先、フォルダが後の深さ優先）
  - `public static bool IsMatch(string name, string text)`
  - `public static string GetDisplayPath(ITileViewContent item, ITileViewContent root)` — `item` から `parent` を `root` の手前まで辿り、名前を `/` で連結する。`root` に届かない（`parent` が null になる）場合は辿れた分だけを返す

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetSearchTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセット検索の収集・照合・相対パスを固定する</summary>
    public class ScenePresetSearchTests
    {
        private static TileViewContentBase Dir(string name)
        {
            return new TileViewContentBase
            {
                name = name,
                isDir = true,
                children = new List<ITileViewContent>(),
            };
        }

        private static TileViewContentBase File(string name)
        {
            return new TileViewContentBase { name = name };
        }

        /// <summary>
        /// root
        ///   Pose01, Kiss
        ///   [Sub] Pose02, kiss
        ///   [SceneCapture] Scene01, [Deep] Pose03
        /// </summary>
        private static TileViewContentBase CreateTree()
        {
            var root = Dir("ScenePreset");
            root.AddChild(File("Pose01"));
            root.AddChild(File("Kiss"));

            var sub = Dir("Sub");
            root.AddChild(sub);
            sub.AddChild(File("Pose02"));
            sub.AddChild(File("kiss"));

            var sc = Dir("SceneCapture");
            root.AddChild(sc);
            sc.AddChild(File("Scene01"));
            var deep = Dir("Deep");
            sc.AddChild(deep);
            deep.AddChild(File("Pose03"));

            return root;
        }

        private static List<string> CollectNames(ITileViewContent root, string text)
        {
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, text, result);
            return result.Select(item => item.name).ToList();
        }

        [Fact]
        public void 全階層から部分一致で集める()
        {
            Assert.Equal(new[] { "Pose01", "Pose02", "Pose03" }, CollectNames(CreateTree(), "pose"));
        }

        [Fact]
        public void 大文字小文字を区別しない()
        {
            Assert.Equal(new[] { "Kiss", "kiss" }, CollectNames(CreateTree(), "KISS"));
        }

        [Fact]
        public void 同名でも別フォルダなら両方返す()
        {
            var root = CreateTree();
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "kiss", result);
            Assert.Equal(2, result.Count);
            Assert.NotEqual(
                ScenePresetSearch.GetDisplayPath(result[0], root),
                ScenePresetSearch.GetDisplayPath(result[1], root));
        }

        [Fact]
        public void フォルダ名には一致させない()
        {
            Assert.Empty(CollectNames(CreateTree(), "Deep"));
        }

        [Fact]
        public void 空文字は何も返さない()
        {
            Assert.Empty(CollectNames(CreateTree(), ""));
            Assert.Empty(CollectNames(CreateTree(), null));
        }

        [Fact]
        public void 空白も文字として照合する()
        {
            Assert.Empty(CollectNames(CreateTree(), " "));
        }

        [Fact]
        public void かなは同一視しない()
        {
            var root = Dir("ScenePreset");
            root.AddChild(File("キス"));
            Assert.Empty(CollectNames(root, "きす"));
            Assert.Equal(new[] { "キス" }, CollectNames(root, "キ"));
        }

        [Fact]
        public void 結果へ追記し元のツリーの親を変えない()
        {
            var root = CreateTree();
            var searchRoot = new TempTileViewContent
            {
                isDir = true,
                children = new List<ITileViewContent>(),
            };
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "Pose03", result);
            foreach (var item in result)
            {
                searchRoot.AddChild(item);
            }

            Assert.Single(searchRoot.children);
            Assert.Equal("Deep", searchRoot.children[0].parent.name);
        }

        [Fact]
        public void 相対パス_直下はファイル名だけ()
        {
            var root = CreateTree();
            Assert.Equal("Pose01", ScenePresetSearch.GetDisplayPath(root.children[0], root));
        }

        [Fact]
        public void 相対パス_SceneCapture配下はフォルダ名から始まる()
        {
            var root = CreateTree();
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "Pose03", result);
            Assert.Equal("SceneCapture/Deep/Pose03", ScenePresetSearch.GetDisplayPath(result[0], root));
        }

        [Fact]
        public void 相対パス_ルートに届かなければ辿れた分だけ返す()
        {
            var orphanDir = Dir("Orphan");
            var file = File("Lost");
            orphanDir.AddChild(file);
            Assert.Equal("Orphan/Lost", ScenePresetSearch.GetDisplayPath(file, Dir("Other")));
        }

        [Fact]
        public void 照合_IsMatch()
        {
            Assert.True(ScenePresetSearch.IsMatch("Pose01", "SE0"));
            Assert.False(ScenePresetSearch.IsMatch("Pose01", "x"));
            Assert.False(ScenePresetSearch.IsMatch("Pose01", ""));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run（Git Bash、spec の「ビルド＆テスト」の手順）:
```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetSearchTests
```
Expected: テストのコンパイルエラー（`ScenePresetSearch` が無い）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/ScenePresetSearch.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット一覧の名前検索。表示中のフォルダではなくツリー全体を対象にする
    /// (SceneCapture 仮想フォルダ配下も含む)
    /// </summary>
    public static class ScenePresetSearch
    {
        /// <summary>
        /// root 配下の全ファイル項目から名前が部分一致するものを result へ追記する。
        /// フォルダ名には一致させない
        /// </summary>
        public static void Collect(ITileViewContent root, string text, List<ITileViewContent> result)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var files = new List<ITileViewContent>();
            root.GetAllFiles(files);

            foreach (var file in files)
            {
                if (IsMatch(file.name, text))
                {
                    result.Add(file);
                }
            }
        }

        /// <summary>他の画面とそろえ、大文字小文字だけを同一視する (かなの同一視はしない)</summary>
        public static bool IsMatch(string name, string text)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text))
            {
                return false;
            }
            return name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// root からの相対パス (例: SceneCapture/Deep/Pose03)。
        /// 検索結果では同名のプリセットが別フォルダから並ぶため、マウスオーバーでこれを出して区別する
        /// </summary>
        public static string GetDisplayPath(ITileViewContent item, ITileViewContent root)
        {
            var names = new List<string>();
            for (var current = item; current != null && current != root; current = current.parent)
            {
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
```

csproj に登録（`<Compile Include="ScenePresetNaming.cs" />` の次の行）:

```xml
    <Compile Include="ScenePresetSearch.cs" />
```

（`string.Join(string, string[])` は .NET 3.5 にも IEnumerable 版が無いため `ToArray()` を付ける）

- [ ] **Step 4: テストが通ることを確認する**

Run: Step 2 と同じコマンド
Expected: `ScenePresetSearchTests` の全テストが PASS

- [ ] **Step 5: 進捗を記録する**

コミットはループの段 4（commit スキル）でまとめて行う。ここでは executing-plans の ledger に Task 1 完了を記録する。

---

### Task 2: SceneCapture 項目の SC タグと `PresetWindow` の検索 UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`ScenePresetItem` クラス :16-33、`MarkSceneCaptureItems` :284-305）
- Modify: `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `ScenePresetSearch.Collect(ITileViewContent, string, List<ITileViewContent>)`、`ScenePresetSearch.GetDisplayPath(ITileViewContent, ITileViewContent)`
- Produces（実機確認がリフレクションで触る名前。変えないこと）:
  - `PresetWindow._searchText`（`string`、private フィールド）
  - `PresetWindow._searchRoot`（`TempTileViewContent`、private readonly フィールド）
  - `PresetWindow.EnsureSearchList()`（private メソッド。検索結果を必要なら作り直す）
  - `ScenePresetItem.SCENE_CAPTURE_TAG`（`public const string = "SC"`）

UI のロジックはゲーム外テストの対象外（IMGUI に依存）。確認はビルドと段 3 の実機確認で行う。

- [ ] **Step 1: `ScenePresetItem` にタグ定数を足す**

`ScenePresetManager.cs` の `ScenePresetItem` クラス、`isReadonlyDir` の宣言の後に追加:

```csharp
        /// <summary>検索結果のタイルで SceneCapture 由来の項目に付けるタグ</summary>
        public const string SCENE_CAPTURE_TAG = "SC";

        /// <summary>SC タグの背景色。PNG 配置の「写真」タグと見分けられる色にする</summary>
        public static readonly Color SCENE_CAPTURE_TAG_COLOR = new Color(0.6f, 0.4f, 0.1f);
```

- [ ] **Step 2: `MarkSceneCaptureItems` でファイル項目にタグを付ける**

`else` 節（読み込み専用の設定）を次のように変える:

```csharp
                else
                {
                    // 読み込み専用: 削除ボタンと自動ロード指定を出さない
                    child.canDelete = false;
                    child.canFavorite = false;
                    // サムネが無く見分けにくいため、検索結果のタイルで印を出す
                    // (SceneCapture フォルダ内はリスト表示なのでタグは見えない)
                    child.tag = ScenePresetItem.SCENE_CAPTURE_TAG;
                    child.tagColor = ScenePresetItem.SCENE_CAPTURE_TAG_COLOR;
                }
```

- [ ] **Step 3: `PresetWindow` に検索状態を足す**

`using` に `System.Collections.Generic` を足す。`_view` の宣言の後に追加:

```csharp
        /// <summary>検索行のラベル幅</summary>
        private static readonly float SEARCH_LABEL_WIDTH = 40;

        /// <summary>
        /// 検索中の表示用。子の parent を書き換えない TempTileViewContent を使い、
        /// 元の階層構造を壊さずに平坦な一覧を作る (parent を辿って相対パスも出せる)
        /// </summary>
        private readonly TempTileViewContent _searchRoot = new TempTileViewContent
        {
            name = "検索結果",
            isDir = true,
            children = new List<ITileViewContent>(),
        };

        private string _searchText = "";
        private bool isSearching => !string.IsNullOrEmpty(_searchText);

        // 検索結果を作ったときのツリーと文字列。保存・削除・更新・自動ロードは
        // Reload で rootItem を新しい参照へ差し替えるため、参照の比較で作り直しを検知できる
        private ScenePresetItem _searchBuiltRoot = null;
        private string _searchBuiltText = null;
```

- [ ] **Step 4: 描画の流れに検索行と作り直しを入れる**

`DrawContent` の try 節を次のように変える（検索行も読み込み中は無効化されるよう try の中で描く）:

```csharp
            try
            {
                DrawSearchRow();
                // ツール行の件数ラベルが前回の結果を出さないよう、行を描く前に作り直す
                if (isSearching)
                {
                    EnsureSearchList();
                }
                DrawToolRow(currentDirItem);
                DrawLoadOptionRow();

                _view.DrawHorizontalLine(Color.gray);
                _view.AddSpace(5);

                if (isSearching)
                {
                    DrawSearchTiles();
                }
                else
                {
                    DrawPresetTiles(currentDirItem);
                }
            }
```

`DrawLoadingOverlay` の後に次のメソッドを追加:

```csharp
        private void DrawSearchRow()
        {
            _view.DrawTextField("検索", SEARCH_LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value =>
                {
                    if (value != _searchText)
                    {
                        // 表示中のフォルダは変えない。検索を消したとき元のフォルダへ戻れるようにする
                        _searchText = value;
                        // 前の結果のスクロール位置を引き継ぐと、少ない結果で空白だけが見えることがある
                        _view.scrollPosition = Vector2.zero;
                    }
                });
        }

        /// <summary>検索結果を必要なときだけ作り直す (ツリーの差し替えか検索文字列の変更)</summary>
        private void EnsureSearchList()
        {
            var rootItem = ScenePresetManager.rootItem;
            if (rootItem == _searchBuiltRoot && _searchText == _searchBuiltText)
            {
                return;
            }

            _searchBuiltRoot = rootItem;
            _searchBuiltText = _searchText;

            _searchRoot.RemoveAllChildren();
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(rootItem, _searchText, result);
            foreach (var item in result)
            {
                _searchRoot.AddChild(item);
            }
        }
```

- [ ] **Step 5: ツール行の「<」と「保存」を検索中は無効にする**

`DrawToolRow` の 2 つのボタンを次のように変える:

```csharp
                // ルートでは戻り先が無く、検索中は一覧が階層を表していないため無効化する
                if (_view.DrawButton("<", 20, ROW_HEIGHT, !isSearching && currentDirItem.parent != null))
                {
                    ScenePresetManager.currentDirItem = currentDirItem.parent as ScenePresetItem;
                }

                // SceneCapture 仮想フォルダは読み込み専用のため保存させない。
                // 検索中は保存先のフォルダが一覧から読み取れないため保存させない
                if (_view.DrawButton("保存", 50, ROW_HEIGHT, !isSearching && !currentDirItem.isReadonlyDir))
                {
                    SavePresetWithConfirm();
                }
```

ツール行末尾のフォルダ名ラベルは、検索中は「検索結果 (N 件)」にする:

```csharp
                _view.DrawLabel(isSearching
                    ? _searchRoot.name + " (" + _searchRoot.children.Count + " 件)"
                    : currentDirItem.name, -1, ROW_HEIGHT);
```

（`EnsureSearchList` はこの行より前に呼ぶため、件数は常に今の結果と一致する）

- [ ] **Step 6: 検索結果タイルを描く**

`DrawPresetTiles` の後に追加し、マウスオーバー行の描画を共通化する:

```csharp
        /// <summary>
        /// 検索結果のタイル。SceneCapture 項目もサムネ無しのタイル (名前 + SC タグ) で並べる。
        /// マウスオーバーでは同名を見分けられるようルートからの相対パスを出す
        /// </summary>
        private void DrawSearchTiles()
        {
            if (_searchRoot.children.Count == 0)
            {
                _view.DrawLabel("該当するプリセットはありません", -1, ROW_HEIGHT);
                return;
            }

            ScenePresetItem selectedItem = null;
            ScenePresetItem mouseOverItem = null;

            _view.DrawTileView(
                _searchRoot,
                -1,
                GetTileViewHeight(),
                TILE_WIDTH,
                TILE_HEIGHT,
                item =>
                {
                    selectedItem = item as ScenePresetItem;
                },
                item =>
                {
                    mouseOverItem = item as ScenePresetItem;
                },
                item =>
                {
                    // SceneCapture 項目は canDelete = false のため x ボタン自体が出ない
                    DeletePresetWithConfirm(item as ScenePresetItem);
                });

            OpenItem(selectedItem);

            // 結果を作ったときのツリーを基準にする。同じフレームで Reload が rootItem を
            // 差し替えても、結果の項目と基準のツリーがずれない
            DrawMouseOverRow(mouseOverItem != null
                ? ScenePresetSearch.GetDisplayPath(mouseOverItem, _searchBuiltRoot)
                : null);
        }

        /// <summary>下部にマウスオーバー中の名前表示行を確保した残りの高さ</summary>
        private float GetTileViewHeight()
        {
            return _view.viewRect.height - _view.currentPos.y
                - ROW_HEIGHT - GUIView.defaultMargin;
        }

        /// <summary>タイル一覧の下の、マウスオーバー中の項目名を出す行</summary>
        private void DrawMouseOverRow(string text)
        {
            _view.DrawBox(-1, ROW_HEIGHT);

            if (text != null)
            {
                _view.DrawLabel(text, -1, ROW_HEIGHT);
            }
        }
```

`DrawPresetTiles` の高さ計算と末尾を、上の 2 メソッドを使う形に変える:

```csharp
            var tileViewHeight = GetTileViewHeight();
```

```csharp
            OpenItem(selectedItem);

            DrawMouseOverRow(mouseOverItem?.name);
```

（元の `// 下部にマウスオーバー中の名前表示行を確保し、残りをタイルビューへ充てる` コメントは `GetTileViewHeight` の summary に移ったので削る）

- [ ] **Step 7: ビルドとテスト**

Run: Task 1 Step 2 のコマンドから `--filter` を外したもの（全テスト）
Expected: 両構成のビルドが警告の増加なく成功し、全テストが PASS

- [ ] **Step 8: 進捗を記録する**

executing-plans の ledger に Task 2 完了を記録する。

---

### Task 3: ドキュメント

**Files:**
- Modify: `docs/` 配下のシーンプリセットの説明（`grep -rn "シーンプリセット" docs --include=*.md -l` で該当ページを探す。ユーザー向けの機能説明ページがあれば、検索の項を足す）

- [ ] **Step 1: 該当ページを探す**

Run: `grep -rln "シーンプリセット" /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/docs --include=*.md | grep -v superpowers`

- [ ] **Step 2: 検索の説明を足す**

見つかったユーザー向けページのシーンプリセットの節に、次の内容を既存の文体に合わせて追記する:

- 一覧の上の検索欄に入力すると、全フォルダ（SceneCapture を含む）のプリセットを名前の部分一致で探せる。大文字小文字は区別しない
- 結果はタイルで並び、SceneCapture のプリセットには「SC」の印が付く。マウスを乗せると、フォルダを含めた場所が下に出る
- 検索中は「<」と「保存」が使えない。検索欄を空にすると元のフォルダの表示に戻る

該当ページが無ければ何もしない（CLAUDE.md の互換の節は XML に影響が無いので追記不要）。

---

## 実機確認（ループの段 3 で行う。計画の実装範囲外）

spec の「A. 実機確認」に従う。リフレクションで触る名前は Task 2 の Produces を参照:
- `PresetWindow.instance` の `_searchText` を設定 → 翌フレーム以降に `_searchRoot.children` の件数と名前を読む。期待値はプリセットフォルダ（サイドカー `<名前>.<キー>.xml` を除く）と SceneCapture の実ファイルを部分一致で数えたもの
- `_searchRoot.children` の SC 項目の `tag == "SC"` を確認し、1 件を `ScenePresetManager.LoadPreset(item)` で開く → `tail_log` に例外が無いこと
- 検索中の「<」「保存」の無効化は `screenshot` で確認
- 検索中の削除による作り直し: プリセットフォルダに検証用の XML を 1 件複製し（名前に検索語を含める）、`ScenePresetManager.Reload()` → 検索結果に入ることを確認 → その項目を `ScenePresetManager.DeletePreset(item)` で消す → 翌フレーム以降に `_searchRoot.children` から消え、`_searchBuiltRoot` が新しい `rootItem` と同じ参照になっていること。複製した XML（とサムネ）が残っていれば消す
- 終わったら `_searchText` を `""` に戻す

---

## レビュー却下メモ

- 検索行をツール行の上に置く位置の確認 — 仕様どおり（`DrawSearchRow` → `DrawToolRow`）のため変更不要
- 検索結果の並び（ファイル先・フォルダ後の深さ優先、SC が末尾）— 仕様に反せず自然なため変更不要
- `Collect` が毎回 `GetAllFiles` する負荷 — 作り直しは参照か文字列の変化時だけなので問題なし（指摘側も問題なしと判断）
