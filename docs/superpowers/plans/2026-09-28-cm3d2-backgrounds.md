# CM3D2 の背景 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ゲームの互換モード (CM3D2 連携) が有効なとき、CM3D2 側の `phot_bg_list.nei` の背景を背景ウィンドウの一覧へ「CM3D2:」付きカテゴリで足し、prefab 名で適用・保存できるようにする。

**Architecture:**
- CM3D2 の背景は `PhotoBGData` に入れない (コンストラクタが private で、id が 2.5 の id と衝突するため)。プラグイン側の `Cm3d2BgEntry` の一覧として別に持つ
- 行 → 一覧の変換 (列の写し・カテゴリ接頭辞・有効 ID / 必要パックの絞り込み・2.5 との prefab 名の重複除去) は純関数の `Cm3d2BgList.Build` に集め、テストで固定する。ファイルの読み込み (`GameUty.FileSystemOld` / `CsvParser` / `CsvCommonIdManager`) とキャッシュは `BackgroundUtils` が持つ
- 適用は `BgMgr.ChangeBg(prefabName)`。ゲームは互換モードで CM3D2 の `.asset_bg` を `GameUty.BgFiles` に登録済みなので、この同一経路で読める。保存 (シーンプリセット・履歴) は既存の「`GetBgId` で id を引けなければ `bgPrefabName`」の経路にそのまま乗り、タイムラインは元々 prefab 名 (`BgMgr.GetBGName()`) で記録する。どちらも保存側のコード変更は要らない

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「14. CM3D2 の背景」

## 仕様

(spec #14 の転記と、調査で確定した詳細)

- ゲームの互換モードが有効 (`GameUty.IsEnabledCompatibilityMode` が true かつ `GameUty.FileSystemOld` が非 null) なら、CM3D2 側の `phot_bg_list.nei` を読んで背景一覧に足す。CM3D2 が未導入の環境では何も足さない (ログも出さない)
- 列は `ID / カテゴリー / BG表示名 / 作成プレファブ名 / 必要パック` (ゲームの `PhotoBGData.Create` と同じ)。ゲームと同じく次の行は出さない:
  - ID が数値でない行、prefab 名が空の行
  - CM3D2 の `phot_bg_enabled_list` (互換側のファイルシステム + パスリスト) に ID が無い行。ただし有効 ID リストが空 (読めない・存在しない) なら絞り込まない
  - 必要パックが空でなく `PluginData.IsEnabled` が false の行
- カテゴリ名は `"CM3D2:" + カテゴリー` (例: `CM3D2:夜伽`)。カテゴリコンボでは 2.5 のカテゴリ (マイルーム含む) の後ろに並ぶ。「すべて」では 2.5 の背景の後ろに並ぶ
- 2.5 の一覧と prefab 名が重なるものは出さない。**大文字小文字は区別しない** (`BgMgr.CreateAssetBundle` は小文字化して引くため同じ背景になる。実機で `SMRoom2`⇔`smroom2`、`Train`⇔`train`、`ClassRoom`⇔`classroom` 等 6 件が該当し、完全一致だと 2 件しか除けない)。CM3D2 の一覧内での重複も先の行だけ残す
- 表示名が空なら prefab 名を表示名にする
- 保存は prefab 名で行う。CM3D2 の ID は `bgId` に入れない (2.5 の ID と衝突する)
- タイムラインの背景 Inspector の見出しは、CM3D2 の背景も表示名で出す (`PhotoBGManager.GetDisplayName` の補完)
- 一覧は初回に 1 度だけ読みキャッシュする (互換モードの有無・CM3D2 の一覧は起動中に変わらない)。2.5 の一覧 (`PhotoBGData.data`) が未構築のうちは読まずに空を返し、キャッシュしない
- 読み込みで例外が出ても 2.5 の背景一覧は使えること (ログを残して CM3D2 分を空にする)
- 2.0 (COM3D2) ビルド: `GameUty.FileSystemOld` / `GameUty.IsEnabledCompatibilityMode` / `GameUty.PathListOld` / `wf.CsvCommonIdManager.ReadEnabledIdList(FileSystemType.Old, ...)` / `AFileSystemBase.IsExistentFile` / `PluginData.IsEnabled` / `GameUty.BgFiles` の旧ファイルシステム登録 (`UpdateFileSystemPathOld`) はすべて 2.0 の Assembly-CSharp にも同じ形で存在する (ilspycmd で確認済み)。`#if COM3D25` もリフレクションも不要。2.0 の実機検証は行わない
- 参考実装: MeidoPhotoStudio `BackgroundRepository.cs:127-151` (互換モード判定 → `FileSystemOld` から `phot_bg_list.nei` を `CsvParser` で読む。MPS は有効 ID・必要パック・重複の絞り込みをしていないが、本計画ではゲームの `PhotoBGData.Create` に合わせて行う)

### 実機で確認済みの値 (COM3D2.5、互換モード有効)

- `phot_bg_list.nei` 55 行、有効 ID 55、必要パックで落ちる行 0、prefab 空 0
- 2.5 と大文字小文字無視で重複 6 件 → 一覧に足すのは 49 件、カテゴリは `夜伽` / `一般` の 2 つ
- 2.5 のカテゴリは `昼` / `夜` / `マイルーム`

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること。`Vector2Int` は 2.0 の Unity に無いので使わない
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`Quaternion.Euler`, `GUI.*`, `SystemInfo.*` 等) はテストから呼べない。`Mathf` / `Rect` / `Vector2` の算術は可
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)。リネームは `git mv` と csproj の書き換えの両方
- `MTEUtils/` はサブモジュール。本計画では変更しない (`GUIView` の公開 API だけを使う)
- 実機検証は通常シーン (撮影モード非対応)
- Unity API (`GUI.DrawTextureWithTexCoords` の alphaBlend 引数) は実装前に Context7 で確認する

(本計画固有)
- ゲーム側の型 (`GameUty` / `CsvParser` / `CsvCommonIdManager` / `PluginData` / `PhotoBGData`) はテスト対象の `Cm3d2BgList` から参照しない。テストから呼ぶとゲーム未初期化で落ちる
- `wf` 名前空間は `using` せず `global::wf.CsvCommonIdManager` と完全修飾する (既存の名前と衝突させないため)

## Review Focus

1. 2.5 と大文字小文字だけ違う prefab 名 (`SMRoom2`⇔`smroom2` 等) が一覧に二重に出ない — Task 1 のテスト「2.5 と大文字小文字違いの prefab 名は出さない」、Task 3 の実機確認 (49 件)
2. CM3D2 の背景をシーンプリセットに保存したとき `bgId` が空で `bgPrefabName` に prefab 名が入り、読み直すと同じ背景が出る (CM3D2 の ID 200 等が 2.5 の背景に化けない) — Task 3 の実機確認 (保存 XML を開いて確認)
3. CM3D2 の背景を選んだ状態で Undo/Redo・タイムラインの背景キー再生をしても同じ背景に戻る (`BgMgr.GetBGName()` が prefab 名を返し `ChangeBgByName` で復元される) — Task 3 の実機確認
4. `CM3D2:夜伽` を選んだままウィンドウを閉じて開き直しても、カテゴリが「すべて」に戻らず一覧が空にもならない (`OnShowChanged` のカテゴリ存在判定が CM3D2 のカテゴリも含む) — Task 3 の実機確認
5. 有効 ID リストが読めない・空の環境でも CM3D2 の背景が全部消えない (空 = 絞り込まない) — Task 1 のテスト「有効 ID リストが空なら絞り込まない」

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/Cm3d2BgList.cs` | `Cm3d2BgEntry` と、行 → 一覧の変換・検索・カテゴリ列挙 (純関数) |
| Modify `source/COM3D2.SceneEditor.Plugin/BackgroundUtils.cs` | CM3D2 の一覧の読み込み・キャッシュ・prefab 名検索 |
| Modify `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs` | カテゴリと一覧へ CM3D2 の背景を足す |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/PhotoBGManager.cs` | 表示名の補完 |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | `Cm3d2BgList.cs` の追加 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/Cm3d2BgListTests.cs` | 変換のテスト |
| Modify `docs-site/guide/staging.md` | 背景ウィンドウの説明 |

変更しないもの: `ScenePresetData.cs` / `Manager/History/BackgroundSnapshot.cs` (保存は既存の `bgPrefabName` 経路)、`Timeline/Hack/SceneEditorHack.cs` / `BackgroundUtils.ChangeBgByName` (タイムラインは prefab 名で `BgMgr.ChangeBg`)。

---

### Task 1: 行から CM3D2 の背景一覧を組み立てる純関数

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Cm3d2BgList.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (`<Compile Include="BackgroundUtils.cs" />` の直前)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Cm3d2BgListTests.cs`

**Interfaces:**
- Produces:
  - `class Cm3d2BgEntry { readonly string category; readonly string name; readonly string prefabName; Cm3d2BgEntry(string category, string name, string prefabName) }` (`category` は接頭辞付き)
  - `Cm3d2BgList.ListFileName = "phot_bg_list.nei"` / `Cm3d2BgList.EnabledListName = "phot_bg_enabled_list"` / `Cm3d2BgList.CategoryPrefix = "CM3D2:"`
  - `Cm3d2BgList.ColumnId = 0` / `ColumnCategory = 1` / `ColumnName = 2` / `ColumnPrefabName = 3` / `ColumnRequiredPack = 4` / `ColumnCount = 5`
  - `Cm3d2BgList.ToCategoryName(string category) : string`
  - `Cm3d2BgList.Build(IEnumerable<string[]> rows, ICollection<int> enabledIds, Predicate<string> isPackEnabled, IEnumerable<string> existingPrefabNames) : List<Cm3d2BgEntry>`
  - `Cm3d2BgList.Find(IList<Cm3d2BgEntry> entries, string prefabName) : Cm3d2BgEntry` (大文字小文字無視、無ければ null)
  - `Cm3d2BgList.GetCategories(IList<Cm3d2BgEntry> entries) : List<string>` (出現順・重複なし)

- [ ] **Step 1: 失敗するテストを書く**

`Cm3d2BgListTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// CM3D2 の phot_bg_list.nei の行から背景一覧を作る規則を固定する。
    /// ゲームの PhotoBGData.Create とほぼ同じ絞り込みに加え、2.5 の一覧との重複を除く。
    /// ゲームは有効 ID リストが読めないと全行を弾くが、ここでは空なら絞り込まない (本家と異なる)
    /// </summary>
    public class Cm3d2BgListTests
    {
        private static readonly string[] NoExisting = new string[0];

        private static bool AllPacks(string pack)
        {
            return true;
        }

        private static string[] Row(string id, string category, string name, string prefab, string pack = "")
        {
            return new[] { id, category, name, prefab, pack };
        }

        [Fact]
        public void 行_列を項目へ写しカテゴリにCM3D2を付ける()
        {
            var rows = new List<string[]> { Row("200", "夜伽", "サロン:昼", "Salon_Day") };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("CM3D2:夜伽", entries[0].category);
            Assert.Equal("サロン:昼", entries[0].name);
            Assert.Equal("Salon_Day", entries[0].prefabName);
        }

        [Fact]
        public void 行_表示名が空ならprefab名を表示名にする()
        {
            var rows = new List<string[]> { Row("1", "一般", "", "Pool") };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Equal("Pool", entries[0].name);
        }

        [Fact]
        public void 行_prefab名が空の行は出さない()
        {
            var rows = new List<string[]> { Row("1", "一般", "名前だけ", "") };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 行_IDが数値でない行は出さない()
        {
            var rows = new List<string[]> { Row("abc", "一般", "A", "PrefabA"), Row("", "一般", "B", "PrefabB") };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 行_列が足りない行も落ちずに必要パックなしとして扱う()
        {
            var rows = new List<string[]> { new[] { "1", "一般", "A", "PrefabA" } };

            var entries = Cm3d2BgList.Build(rows, null, pack => false, NoExisting);

            Assert.Single(entries);
        }

        [Fact]
        public void 行_nullの行と空の入力は無視する()
        {
            Assert.Empty(Cm3d2BgList.Build(null, null, AllPacks, NoExisting));
            Assert.Empty(Cm3d2BgList.Build(new List<string[]> { null }, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 重複_2点5と同じprefab名は出さない()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "サロン", "Salon"),
                Row("2", "一般", "プール", "Pool"),
            };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, new[] { "Salon", "" , null });

            Assert.Single(entries);
            Assert.Equal("Pool", entries[0].prefabName);
        }

        [Fact]
        public void 重複_2点5と大文字小文字違いのprefab名は出さない()
        {
            // BgMgr.CreateAssetBundle は小文字化して引くため同じ背景になる (実機: SMRoom2 ⇔ smroom2)
            var rows = new List<string[]>
            {
                Row("1", "夜伽", "地下室", "SMRoom2"),
                Row("2", "一般", "電車", "Train"),
            };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, new[] { "smroom2", "train" }));
        }

        [Fact]
        public void 重複_CM3D2内で重なるprefab名は先の行だけ残す()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "先", "Pool"),
                Row("2", "夜伽", "後", "pool"),
            };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("先", entries[0].name);
        }

        [Fact]
        public void 有効ID_リストに無い行は出さない()
        {
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA"), Row("2", "一般", "B", "PrefabB") };

            var entries = Cm3d2BgList.Build(rows, new HashSet<int> { 2 }, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("PrefabB", entries[0].prefabName);
        }

        [Fact]
        public void 有効ID_リストが空なら絞り込まない()
        {
            // 有効 ID リストが読めない環境で CM3D2 の背景が全部消えないようにする
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA") };

            Assert.Single(Cm3d2BgList.Build(rows, new HashSet<int>(), AllPacks, NoExisting));
        }

        [Fact]
        public void 必要パック_未導入の行は出さない()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "A", "PrefabA", "pack_a"),
                Row("2", "一般", "B", "PrefabB", "pack_b"),
            };

            var entries = Cm3d2BgList.Build(rows, null, pack => pack == "pack_b", NoExisting);

            Assert.Single(entries);
            Assert.Equal("PrefabB", entries[0].prefabName);
        }

        [Fact]
        public void 必要パック_空なら判定しない()
        {
            var called = false;
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA", "") };

            var entries = Cm3d2BgList.Build(rows, null, pack => { called = true; return false; }, NoExisting);

            Assert.Single(entries);
            Assert.False(called);
        }

        [Fact]
        public void 検索_prefab名は大文字小文字を無視して引く()
        {
            var entries = new List<Cm3d2BgEntry> { new Cm3d2BgEntry("CM3D2:一般", "プール", "Pool") };

            Assert.Same(entries[0], Cm3d2BgList.Find(entries, "pool"));
            Assert.Null(Cm3d2BgList.Find(entries, "Salon"));
            Assert.Null(Cm3d2BgList.Find(entries, null));
            Assert.Null(Cm3d2BgList.Find(null, "Pool"));
        }

        [Fact]
        public void カテゴリ_出現順で重複なく並べる()
        {
            var entries = new List<Cm3d2BgEntry>
            {
                new Cm3d2BgEntry("CM3D2:夜伽", "A", "PrefabA"),
                new Cm3d2BgEntry("CM3D2:一般", "B", "PrefabB"),
                new Cm3d2BgEntry("CM3D2:夜伽", "C", "PrefabC"),
            };

            Assert.Equal(new[] { "CM3D2:夜伽", "CM3D2:一般" }, Cm3d2BgList.GetCategories(entries));
        }

        [Fact]
        public void カテゴリ_接頭辞は2点5のカテゴリと衝突しない()
        {
            Assert.Equal("CM3D2:昼", Cm3d2BgList.ToCategoryName("昼"));
            Assert.Equal("CM3D2:", Cm3d2BgList.ToCategoryName(null));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter Cm3d2BgListTests`
Expected: コンパイルエラー (`Cm3d2BgList` / `Cm3d2BgEntry` が無い)

- [ ] **Step 3: 実装する**

`Cm3d2BgList.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// CM3D2 (ゲームの互換モード) の背景 1 件。
    /// PhotoBGData はコンストラクタが private で id も 2.5 と衝突するため、別の型で持つ
    /// </summary>
    public class Cm3d2BgEntry
    {
        /// <summary>「CM3D2:」を付けたカテゴリ名</summary>
        public readonly string category;
        public readonly string name;
        /// <summary>BgMgr.ChangeBg に渡す prefab 名。保存もこの名前で行う</summary>
        public readonly string prefabName;

        public Cm3d2BgEntry(string category, string name, string prefabName)
        {
            this.category = category;
            this.name = name;
            this.prefabName = prefabName;
        }
    }

    /// <summary>
    /// CM3D2 の phot_bg_list.nei の行から背景一覧を組み立てる。
    /// ファイルの読み込みは BackgroundUtils が行い、ここはゲームの型に触れない (テストから呼ぶため)
    /// </summary>
    public static class Cm3d2BgList
    {
        public const string ListFileName = "phot_bg_list.nei";
        public const string EnabledListName = "phot_bg_enabled_list";
        /// <summary>2.5 の背景とカテゴリを見分けるための接頭辞</summary>
        public const string CategoryPrefix = "CM3D2:";

        // phot_bg_list.nei の列 (ID / カテゴリー / BG表示名 / 作成プレファブ名 / 必要パック)
        public const int ColumnId = 0;
        public const int ColumnCategory = 1;
        public const int ColumnName = 2;
        public const int ColumnPrefabName = 3;
        public const int ColumnRequiredPack = 4;
        public const int ColumnCount = 5;

        public static string ToCategoryName(string category)
        {
            return CategoryPrefix + (category ?? "");
        }

        /// <summary>
        /// 行を背景一覧へ変換する。絞り込みはゲームの PhotoBGData.Create に合わせ、
        /// 加えて 2.5 の一覧 (existingPrefabNames) と prefab 名が重なる行を除く。
        /// enabledIds が null か空なら ID では絞り込まない
        /// </summary>
        public static List<Cm3d2BgEntry> Build(
            IEnumerable<string[]> rows,
            ICollection<int> enabledIds,
            Predicate<string> isPackEnabled,
            IEnumerable<string> existingPrefabNames)
        {
            var entries = new List<Cm3d2BgEntry>();
            if (rows == null)
            {
                return entries;
            }

            // BgMgr.CreateAssetBundle は prefab 名を小文字化して引くため、大文字小文字違いは同じ背景
            var seenPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingPrefabNames != null)
            {
                foreach (var prefabName in existingPrefabNames)
                {
                    if (!string.IsNullOrEmpty(prefabName))
                    {
                        seenPrefabNames.Add(prefabName);
                    }
                }
            }

            var filterById = enabledIds != null && enabledIds.Count > 0;

            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                int id;
                if (!int.TryParse(GetCell(row, ColumnId).Trim(), out id))
                {
                    continue;
                }
                if (filterById && !enabledIds.Contains(id))
                {
                    continue;
                }

                var prefab = GetCell(row, ColumnPrefabName);
                if (string.IsNullOrEmpty(prefab))
                {
                    continue;
                }

                var pack = GetCell(row, ColumnRequiredPack);
                if (!string.IsNullOrEmpty(pack) && isPackEnabled != null && !isPackEnabled(pack))
                {
                    continue;
                }

                if (!seenPrefabNames.Add(prefab))
                {
                    continue;
                }

                var name = GetCell(row, ColumnName);
                entries.Add(new Cm3d2BgEntry(
                    ToCategoryName(GetCell(row, ColumnCategory)),
                    string.IsNullOrEmpty(name) ? prefab : name,
                    prefab));
            }
            return entries;
        }

        /// <summary>prefab 名 (BgMgr.GetBGName() の値) から引く。大文字小文字は区別しない</summary>
        public static Cm3d2BgEntry Find(IList<Cm3d2BgEntry> entries, string prefabName)
        {
            if (entries == null || string.IsNullOrEmpty(prefabName))
            {
                return null;
            }
            foreach (var entry in entries)
            {
                if (string.Equals(entry.prefabName, prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>カテゴリ名を出現順に重複なく返す</summary>
        public static List<string> GetCategories(IList<Cm3d2BgEntry> entries)
        {
            var categories = new List<string>();
            if (entries == null)
            {
                return categories;
            }
            foreach (var entry in entries)
            {
                if (!categories.Contains(entry.category))
                {
                    categories.Add(entry.category);
                }
            }
            return categories;
        }

        private static string GetCell(string[] row, int column)
        {
            return column < row.Length ? (row[column] ?? "") : "";
        }
    }
}
```

csproj の `<Compile Include="BackgroundUtils.cs" />` の直前に `<Compile Include="Cm3d2BgList.cs" />` を足す。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter Cm3d2BgListTests` が PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Cm3d2BgList.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/Cm3d2BgListTests.cs
git commit -m "feat(background): CM3D2 の背景一覧を組み立てる純関数を追加する"
```

### Task 2: CM3D2 の背景一覧を読み込む

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundUtils.cs:81-111` (`EnsureBgDataLoaded` / `ReloadBgData` の下に追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/PhotoBGManager.cs:104-113`

**Interfaces:**
- Consumes: `Cm3d2BgList.Build` / `Cm3d2BgList.Find` / `Cm3d2BgList.ListFileName` / `Cm3d2BgList.EnabledListName` / `Cm3d2BgList.ColumnCount` / `Cm3d2BgList.ColumnId` / `Cm3d2BgEntry` (Task 1)
- Produces:
  - `BackgroundUtils.cm3d2Backgrounds : IList<Cm3d2BgEntry>` (互換モード無効・未構築なら空。null にならない)
  - `BackgroundUtils.FindCm3d2Background(string prefabName) : Cm3d2BgEntry`

- [ ] **Step 1: ゲーム API を確認する**

`W:\COM3D2_5\work\Assembly-CSharp` と 2.0 (`ilspycmd -t <型> "W:/COM3D2/COM3D2x64_Data/Managed/Assembly-CSharp.dll"`) の両方で次のシグネチャを確かめる (調査時点で両方に存在):
- `GameUty.IsEnabledCompatibilityMode` (static bool) / `GameUty.FileSystemOld` (AFileSystemBase)
- `wf.CsvCommonIdManager.ReadEnabledIdList(FileSystemType, bool readPathList, string fileName, ref HashSet<int>)`
- `AFileSystemBase.IsExistentFile(string)` / `FileOpen(string)`、`CsvParser.Open(AFileBase)` / `max_cell_y` / `IsCellToExistData` / `GetCellAsString`
- `PluginData.IsEnabled(string)` (オーバーロードがあるので Predicate にはメソッドグループでなくラムダで渡す)

- [ ] **Step 2: BackgroundUtils に読み込みとキャッシュを足す**

`BackgroundUtils.cs` の `using System;` の下に `using System.Collections.Generic;` を足し、`ReloadBgData` の下に追加する:

```csharp
        private static readonly Cm3d2BgEntry[] EmptyCm3d2Backgrounds = new Cm3d2BgEntry[0];

        /// <summary>CM3D2 の背景一覧のキャッシュ。互換モードの有無と CM3D2 側の一覧は起動中に変わらないため 1 度だけ読む</summary>
        private static List<Cm3d2BgEntry> _cm3d2Backgrounds = null;

        /// <summary>
        /// ゲームの互換モードで読める CM3D2 の背景一覧。2.5 の一覧と prefab 名が重なるものは除いてある。
        /// 重複を除くのに 2.5 の一覧を使うため、それが未構築のうちは空を返してキャッシュしない
        /// </summary>
        public static IList<Cm3d2BgEntry> cm3d2Backgrounds
        {
            get
            {
                if (_cm3d2Backgrounds == null)
                {
                    // 背景ウィンドウを開く前にプリセットやタイムラインで CM3D2 の背景が復元されても、
                    // Inspector の表示名を引けるよう 2.5 の一覧を先に作っておく (GetBgIdByCategoryName と同じ)
                    EnsureBgDataLoaded();
                    if (PhotoBGData.data == null)
                    {
                        return EmptyCm3d2Backgrounds;
                    }
                    _cm3d2Backgrounds = LoadCm3d2Backgrounds();
                }
                return _cm3d2Backgrounds;
            }
        }

        /// <summary>prefab 名 (BgMgr.GetBGName() の値) から CM3D2 の背景を引く。無ければ null</summary>
        public static Cm3d2BgEntry FindCm3d2Background(string prefabName)
        {
            return Cm3d2BgList.Find(cm3d2Backgrounds, prefabName);
        }

        private static List<Cm3d2BgEntry> LoadCm3d2Backgrounds()
        {
            var fileSystem = GameUty.FileSystemOld;
            // CM3D2 未導入 (互換モード無効) なら何も足さない
            if (!GameUty.IsEnabledCompatibilityMode || fileSystem == null)
            {
                return new List<Cm3d2BgEntry>();
            }

            try
            {
                var existingPrefabNames = new List<string>();
                foreach (var bgData in PhotoBGData.data)
                {
                    existingPrefabNames.Add(bgData.create_prefab_name);
                }

                var entries = Cm3d2BgList.Build(
                    ReadCm3d2BgRows(fileSystem),
                    ReadCm3d2EnabledIds(),
                    pack => PluginData.IsEnabled(pack),
                    existingPrefabNames);
                MTEUtils.Log("CM3D2 の背景を {0} 件読み込みました", entries.Count);
                return entries;
            }
            catch (Exception e)
            {
                // 読めなくても 2.5 の背景は使えるようにする。失敗も空としてキャッシュし毎回は読み直さない
                MTEUtils.LogException(e);
                return new List<Cm3d2BgEntry>();
            }
        }

        /// <summary>CM3D2 側の phot_bg_list.nei を行ごとの文字列配列で読む。ファイルが無ければ空</summary>
        private static List<string[]> ReadCm3d2BgRows(AFileSystemBase fileSystem)
        {
            var rows = new List<string[]>();
            if (!fileSystem.IsExistentFile(Cm3d2BgList.ListFileName))
            {
                return rows;
            }

            using (var file = fileSystem.FileOpen(Cm3d2BgList.ListFileName))
            using (var csv = new CsvParser())
            {
                if (!csv.Open(file))
                {
                    MTEUtils.LogError("CM3D2 の {0} を開けませんでした", Cm3d2BgList.ListFileName);
                    return rows;
                }

                // 1 行目は見出し
                for (var y = 1; y < csv.max_cell_y; y++)
                {
                    if (!csv.IsCellToExistData(Cm3d2BgList.ColumnId, y))
                    {
                        continue;
                    }
                    var cells = new string[Cm3d2BgList.ColumnCount];
                    for (var x = 0; x < cells.Length; x++)
                    {
                        cells[x] = csv.IsCellToExistData(x, y) ? csv.GetCellAsString(x, y) : "";
                    }
                    rows.Add(cells);
                }
            }
            return rows;
        }

        /// <summary>
        /// CM3D2 側の有効 ID (phot_bg_enabled_list と、その DLC 別ファイル)。
        /// 読めなければ空を返し、Cm3d2BgList.Build は空を「絞り込まない」と扱う
        /// </summary>
        private static HashSet<int> ReadCm3d2EnabledIds()
        {
            var ids = new HashSet<int>();
            try
            {
                global::wf.CsvCommonIdManager.ReadEnabledIdList(
                    global::wf.CsvCommonIdManager.FileSystemType.Old, true,
                    Cm3d2BgList.EnabledListName, ref ids);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                ids.Clear();
            }
            return ids;
        }
```

(`MTEUtils.Log(string format, params object[] args)` は書式付き。読み込みは初回 1 度だけなのでログは 1 行で済む)

- [ ] **Step 3: タイムラインの表示名を補う**

`Timeline/PhotoBGManager.cs` の `GetDisplayName`:

```csharp
        public string GetDisplayName(string bgName)
        {
            var data = GetPhotoBGData(bgName);
            if (data != null)
            {
                return data.name;
            }

            // CM3D2 の背景は PhotoBGData に無いため、背景一覧の表示名で補う
            var cm3d2Bg = SceneEditor.Plugin.BackgroundUtils.FindCm3d2Background(bgName);
            if (cm3d2Bg != null)
            {
                return cm3d2Bg.name;
            }

            return bgName;
        }
```

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。2.0 構成でのビルドエラー (API 差) が出たら Step 1 に戻って差分を確かめ、該当箇所だけ `#if COM3D25` で囲う (調査時点では不要)。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BackgroundUtils.cs source/COM3D2.SceneEditor.Plugin/Timeline/PhotoBGManager.cs
git commit -m "feat(background): 互換モードで CM3D2 の背景一覧を読み込む"
```

### Task 3: 背景ウィンドウに CM3D2 の背景を並べる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs:73-74,192-205,521-583`

**Interfaces:**
- Consumes: `BackgroundUtils.cm3d2Backgrounds` / `Cm3d2BgList.GetCategories` / `Cm3d2BgEntry` (Task 1・2)

- [ ] **Step 1: カテゴリ一覧の組み立てを 1 か所にまとめる**

`_categories` フィールドの下に追加する:

```csharp
        /// <summary>
        /// カテゴリ一覧 (先頭は ALL_CATEGORY)。CM3D2 の背景のカテゴリは 2.5 のカテゴリの後ろに並べる
        /// </summary>
        private List<string> GetCategories()
        {
            if (_categories == null)
            {
                _categories = new List<string> { ALL_CATEGORY };
                if (PhotoBGData.category_list != null)
                {
                    _categories.AddRange(PhotoBGData.category_list.Keys);
                }
                _categories.AddRange(Cm3d2BgList.GetCategories(BackgroundUtils.cm3d2Backgrounds));
            }
            return _categories;
        }
```

`OnShowChanged` を置き換える (CM3D2 のカテゴリを選んだまま開き直しても「すべて」に戻さない):

```csharp
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                BackgroundUtils.ReloadBgData();
                _categories = null;
                // 作り直しで消えたカテゴリを選択したままだと一覧が空になる
                if (!GetCategories().Contains(_category))
                {
                    _category = ALL_CATEGORY;
                }
            }
        }
```

`DrawFilterRows` のカテゴリ構築を置き換える:

```csharp
                var categories = GetCategories();
                _categoryComboBox.items = categories;
                _categoryComboBox.currentIndex = Mathf.Max(0, categories.IndexOf(_category));
                _categoryComboBox.onSelected = (name, _) => _category = name;
                _categoryComboBox.DrawButton(_view);
```

- [ ] **Step 2: 一覧に CM3D2 の背景を足す**

`DrawBgList` のループを置き換え、補助メソッドを足す:

```csharp
            foreach (var bgData in PhotoBGData.data)
            {
                if (!IsListed(bgData.category, bgData.name))
                {
                    continue;
                }
                if (DrawBgButton(bgData.name, BackgroundUtils.IsCurrentBg(bgData, currentBgName)))
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Background,
                        "背景変更: " + bgData.name);
                    bgData.Apply();
                    // 配置直後から Inspector で位置・回転を編集できるようにする
                    SelectBg(bgMgr);
                }
            }

            // CM3D2 の背景は PhotoBGData に入れられないため後ろに並べる。
            // prefab 名で適用するので、保存 (プリセット・履歴・タイムライン) も prefab 名になる
            foreach (var entry in BackgroundUtils.cm3d2Backgrounds)
            {
                if (!IsListed(entry.category, entry.name))
                {
                    continue;
                }
                var isCurrent = string.Equals(entry.prefabName, currentBgName, StringComparison.OrdinalIgnoreCase);
                if (DrawBgButton(entry.name, isCurrent))
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Background,
                        "背景変更: " + entry.name);
                    bgMgr.ChangeBg(entry.prefabName);
                    SelectBg(bgMgr);
                }
            }
```

```csharp
        /// <summary>カテゴリと検索文字列の絞り込みに通るか</summary>
        private bool IsListed(string category, string name)
        {
            if (_category != ALL_CATEGORY && category != _category)
            {
                return false;
            }
            return string.IsNullOrEmpty(_searchText) ||
                name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 背景ボタン。現在の背景はシアン表示。適用すべきとき (未適用の背景が押された) だけ true。
        /// ChangeBg は同一背景でも再生成して位置・回転をリセットするため、適用済みの背景の再クリックは無視する
        /// </summary>
        private bool DrawBgButton(string name, bool isCurrent)
        {
            return _view.DrawButton(name, -1, ROW_HEIGHT, true, isCurrent ? Color.cyan : Color.white)
                && !isCurrent;
        }
```

クラス冒頭の summary「背景一覧はフォトモードの PhotoBGData、適用は BgMgr.ChangeBg の同一経路を使う」の後ろに「互換モードでは CM3D2 の背景 (BackgroundUtils.cm3d2Backgrounds) も並べる」を足す。

- [ ] **Step 3: 両構成をビルドし、全テストを通す**

- [ ] **Step 4: 実機確認 (Review Focus 1〜4)**

`com3d25-devbridge:restart-verify` スキルで DLL を反映し、通常シーンでエディタを有効にして確認する:
1. 背景ウィンドウのカテゴリに `昼` / `夜` / `マイルーム` の後ろに `CM3D2:夜伽` / `CM3D2:一般` が出る。`eval_csharp` で `COM3D2.SceneEditor.Plugin.BackgroundUtils.cm3d2Backgrounds.Count` が 49 (実行環境の CM3D2 導入状況で変わる。ログの「CM3D2 の背景を N 件」と一致すること)。`地下室` / `電車` / `教室` が 2.5 側とで二重に出ていない
2. `CM3D2:夜伽` の `サロン:昼` を押す → 背景が出てシアン表示、Inspector で背景が選択される。もう一度押しても位置がリセットされない
3. シーンプリセットを保存 → XML の `<background>` 要素 (`ScenePresetData.background`) で `bgId` 属性が無く `bgPrefabName="Salon_Day"`。別の背景にしてから読み込み直すと `サロン:昼` に戻る
4. Undo → 元の背景、Redo → `サロン:昼`
5. タイムラインで背景キーを打ち、別の 2.5 背景のキーと交互に再生して切り替わる。背景 Inspector の見出しが `サロン:昼` (prefab 名ではない)
6. `CM3D2:夜伽` を選んだままウィンドウを閉じて開き直す → カテゴリが `CM3D2:夜伽` のまま、一覧が出る
7. 検索欄に `サロン` → 2.5 と CM3D2 の両方の該当が出る

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs
git commit -m "feat(background): 背景ウィンドウに CM3D2 の背景を並べる"
```

### Task 4: ドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md:56-66` (背景 / ライト / サウンド / PNG 配置 の節)

- [ ] **Step 1: 背景ウィンドウの説明を足す**

表の下 (ライトウィンドウの段落の前) に追加する:

```markdown
背景ウィンドウの一覧には、ゲームの CM3D2 連携 (互換モード) が有効なとき CM3D2 の背景も並びます。
カテゴリ名の先頭に `CM3D2:` が付き、COM3D2 と同じ背景は出ません。
CM3D2 の背景はシーンプリセット・タイムラインに背景名 (prefab 名) で保存されます。
```

- [ ] **Step 2: コミット**

```bash
git add docs-site/guide/staging.md
git commit -m "docs(background): CM3D2 の背景一覧の説明を追加する"
```

## レビュー却下メモ

- なし (指摘 2 件を取り込み: 表示名のために一覧取得時に 2.5 の一覧を先に作る、有効 ID リストが空のときの本家との違いをコメントに明記)
