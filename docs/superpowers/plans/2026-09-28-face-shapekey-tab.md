# 表情ウィンドウのシェイプキータブ・強制上書きの既定 ON Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 表情ウィンドウに顔 (Face スロット) のシェイプキーを編集する「シェイプキー」タブを足す (#6)。あわせて、新しく呼び出したメイドの表情の強制上書きを既定 ON にする (#12)。

**Architecture:**
- シェイプキーウィンドウのメイドタブが持つ「スロット / タグのキャッシュ・検索欄・行の列挙」を `MaidShapeKeyListView` へ切り出し、シェイプキーウィンドウと表情ウィンドウの両方で使う。行の描画は既存の `MaidShapeKeyRowDrawer` のままなので、履歴 (`HistoryScope.ShapeKey`)・変更追跡 (`MaidShapeKeyEditManager`)・タイムライン記録 (`ShapeKeyTimelineLayer`) は既存のシェイプキーの仕組みにそのまま乗る
- 表情タブと同じ値を 2 つのレイヤーで奪い合わないよう、表情モーフ名 (CRC 顔のサフィックス付きを含む) を `FaceShapeKeyFilter` で一覧から外す。判定は純関数でテストする
- 強制上書きの既定 ON は、新規呼出のロード完了 (`MaidManipulateManager.UpdateLoadingMaids`) でだけ行う。呼び直しの分岐はここを通らないのでユーザーの設定が残り、シーンプリセットの保留適用はこの後に走るのでプリセットの設定が勝つ

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「6. 表情ウィンドウのシェイプキータブ」「12. 表情の強制上書きを既定 ON」

## 仕様

### 6. 表情ウィンドウのシェイプキータブ (仕様書より)

- 表情ウィンドウに「シェイプキー」タブを足し、顔 (Face スロット) のシェイプキーを編集できるようにする。中身はシェイプキーウィンドウのメイドタブと同じ操作 (タグ一覧・検索・スライダー)
- スロットは `face` 固定。行の描画は既存の `MaidShapeKeyRowDrawer` を使い、履歴・タイムライン記録も既存のシェイプキーの仕組みに乗る
- スロット・タグ・検索の処理は、シェイプキーウィンドウから共有クラスへ切り出して両方で使う
- このタブを開いている間、タイムラインの対象レイヤーはシェイプキーレイヤーにする (他のタブは従来どおり表情レイヤー)
- 表情モーフ (eyeclose 等、目・眉・口タブで扱う名前) は一覧から除外する。同じ値を表情レイヤーとシェイプキーレイヤーで奪い合わないため
- 強制上書きが OFF の間は、ゲームが毎フレーム顔の値を消すので編集が効かない。スライダーを触ったら強制上書きを ON にする (表情のスライダーと同じ挙動)
- 保存形式は変わらない

### 6. 本計画で決めた細部

- タブの並びは `目` / `眉` / `口` / `オプション` / `視線` / `シェイプキー` / `プリセット`。タブの選択は保存しない (既存タブと同じ)
- 顔スロットは `maid.body0.Face` から取る (スロット名 `face` を文字列で持たない)。スロット選択コンボは出さない
- 検索欄の右に「更新」ボタンを置く (シェイプキーウィンドウのスロット行の「更新」と同じ役目。顔の差し替えは検知できないため)
- 除外する名前 = 表情レイヤーが記録するモーフ (`FaceMorphUtils.saveMorphNames`) ∪ 表情ウィンドウのタブが扱うモーフ (`MaidFaceMorphController` の定義。`nosefook` を含む) と、それぞれの CRC 顔のサフィックス付きの名前 (`MaidFaceMorphController.GetCrcMorphName` で作る。例: `eyeclose1_normal` `eyeclose2_tare`)。`itome_*` などタブで扱わない名前は除外しない
- 強制上書きを ON にするのは、スライダーの操作と変更追跡チェックの切り替え (どちらも値を書く操作) の直前。すでにユーザー設定が ON (`GetMabataki == false`) なら何もしない
- ON への切り替えは表情の履歴 (`HistoryScope.Face`「強制上書き切替」) として別に積む。1 回目の Undo でシェイプキーの値、2 回目で強制上書きが戻る
- タイムラインの表情レイヤーが強制上書きを上書き中 (キーが OFF) のときは、表情のスライダーと同じくユーザー設定だけが ON になり、そのフレームでは効かない (既存の挙動に揃える)
- タイムラインで `メイドシェイプ` レイヤーを選んでも、表情ウィンドウはシェイプキータブへ切り替えない (シェイプキーウィンドウが受け持つ)。`メイド表情` レイヤーを選んだときは、視線タブと同じくシェイプキータブからも `目` タブへ戻す
- ヘッダーの「リセット」はシェイプキータブでは出さない (対象カテゴリを持たないため)

### 12. 表情の強制上書きを既定 ON (仕様書より)

- メイドを呼び出したときに強制上書きを ON にする。すでに配置済みのメイドを呼び直す場合はユーザーの設定を保つ
- ON にするとまばたきが止まる。変更履歴に書く
- シーンプリセット・表情プリセットの既定値 (旧データの読み込み時) は変えない。旧プリセットの見た目が変わるため
- タイムラインは既に既定 ON なので変えない

### 12. 本計画で決めた細部

- ON にする時点は新規呼出のロード完了時 (退避から戻して表示する直前)。呼出直後はまだボディが無く、顔のブレンドを固められないため
- ON にするとゲームは表情ブレンドを作り直さなくなる (`Maid.Update` の `boMabataki` 分岐)。そのままだとロード完了時の `FaceAnime("通常", 1f)` のフェードが進まず表情が決まらないので、フェードを畳んで今の表情タグのブレンドを直接書き込み、まばたき量 (`TMorph.EyeMabataki`) を 0 に戻してから ON にする
- 設定項目は足さない (既定値の変更のみ)
- 変更履歴 (`CHANGELOG.md`) はリリース時に release-prep スキルが git log から書くため、本計画では直接編集しない。挙動変更をコミット本文に明記して拾えるようにする

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`Quaternion.Euler`, `GUI.*`, `SystemInfo.*` 等) はテストから呼べない。`TMorph.crcFaceTypesStr` のような静的配列の読み取りは既存テスト (`MaidFaceMorphControllerTests`) で動いている
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)。テストプロジェクトは SDK 形式なので追加不要
- `MTEUtils/` はサブモジュール。本計画では変更しない
- 実機検証は通常シーン (撮影モード非対応)。ゲーム起動中なら devbridge の `eval_csharp` / `screenshot` で確認する
- タイムライン XML・シーンプリセット・表情プリセットの保存形式は変えない

## Review Focus

1. 表情ウィンドウのシェイプキータブに、目・眉・口・オプションタブで扱うモーフが CRC 顔のサフィックス付きも含めて出ない (出ると表情レイヤーとシェイプキーレイヤーが同じ値を奪い合う) — Task 1 のテスト「CRC 顔のサフィックス付きも除外する」「タブの全モーフを除外する」
2. 強制上書き OFF のメイドでシェイプキーのスライダーを動かすと、強制上書きが ON になって値がその場に残る。Undo 1 回目でシェイプキー、2 回目で強制上書きが戻る — Task 3 の実機確認
3. タイムライン読込中、シェイプキータブではゲートがシェイプキーレイヤーを対象にし (未登録なら追加ボタン)、編集した値は `メイドシェイプ` レイヤーへ記録される。他のタブは従来どおり `メイド表情` — Task 3 の実機確認
4. 新規に呼び出したメイドが強制上書き ON で、無表情や目の半開きで固まらず「通常」の表情で出る。呼出済みのメイドを呼出ウィンドウで選び直しても強制上書きは変わらない — Task 4 の実機確認
5. 旧データのシーンプリセット (`mabataki` 既定 true) を読み込んで呼び出したメイドは、プリセットどおり強制上書き OFF (まばたきあり) になる — Task 4 の実機確認
6. 共有化の後もシェイプキーウィンドウの挙動が変わらない (スロット切替でタグが入れ替わる、「更新」でキャッシュが捨てられる、検索文字列がメイド / モデルタブで共有される) — Task 2 のテスト「検索は大文字小文字を区別しない」と実機確認
7. CRC 顔で表情モーフ (`eyeclose1_normal` 等) をシェイプキーウィンドウでチェックしてシーンプリセットを保存しても、`shapeKeys` に入らない (Task 1 Step 3b で除外を共有判定へ寄せた) — Task 3 の実機確認に含める
8. 既知の制約: シェイプキーウィンドウのメイドタブは従来どおり顔スロットの全シェイプキーを出す (挙動を変えないため除外しない)。そこから表情モーフを編集すると表情レイヤーと値を奪い合いうるのは従来どおりで、新しいタブだけが除外を保証する。ドキュメントに一言書く — Task 5

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/FaceShapeKeyFilter.cs` | 表情モーフ名 (CRC サフィックス付き含む) の判定 |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs` | `GetAllMorphNames`、呼出時の強制上書き ON、ブレンドセット名の解決 |
| Create `source/COM3D2.SceneEditor.Plugin/MaidShapeKeyListView.cs` | スロット / タグのキャッシュ・検索欄・行の列挙 (共有) |
| Modify `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs` | 共有クラスへ寄せる |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidShapeKeyRowDrawer.cs` | 編集前コールバック `onBeforeEdit` |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs` | シェイプキータブ |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` | 新規呼出のロード完了時に強制上書き ON |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 任意シェイプキー保存の表情モーフ除外を共有判定へ |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規 2 ファイル |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/FaceShapeKeyFilterTests.cs` | 除外判定のテスト |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/MaidShapeKeyListViewTests.cs` | 検索・タグ一覧の組み立てのテスト |
| Modify `docs-site/guide/maid-editing.md`、`docs-site/timeline/layers-maid.md` | 説明 |

---

### Task 1: 表情モーフ名の除外判定

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/FaceShapeKeyFilter.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs` (`FindDef` の直後、:209 付近)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (:364 `<Compile Include="FaceMorphRowDrawer.cs" />` の直後)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/FaceShapeKeyFilterTests.cs`

**Interfaces:**
- Produces:
  - `MaidFaceMorphController.GetAllMorphNames() : IEnumerable<string>` (全カテゴリの素のモーフ名)
  - `FaceShapeKeyFilter.IsFaceMorphName(string shapeKeyName) : bool` (null / 空は false)

- [ ] **Step 1: 失敗するテストを書く**

`FaceShapeKeyFilterTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 表情ウィンドウのシェイプキータブから外す名前を固定する。
    /// 表情タブと同じ値をシェイプキーレイヤーでも触れると、2 つのレイヤーが毎フレーム奪い合う
    /// </summary>
    public class FaceShapeKeyFilterTests
    {
        [Theory]
        [InlineData("eyeclose")]
        [InlineData("eyeclose5")]
        [InlineData("mayuha")]
        [InlineData("moutha")]
        [InlineData("hitomis")]
        [InlineData("hoho")]
        [InlineData("toothoff")]
        public void 表情タブのモーフは除外する(string name)
        {
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Fact]
        public void 表情レイヤーに無くタブにだけあるモーフも除外する()
        {
            // nosefook はタイムラインの保存対象外だが、オプションタブで編集できる
            Assert.DoesNotContain("nosefook", MTEP.FaceMorphUtils.saveMorphNames);
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName("nosefook"));
        }

        [Theory]
        [InlineData("eyeclose1_normal")]
        [InlineData("eyeclose1_tare")]
        [InlineData("eyeclose2_tsuri")]
        [InlineData("eyeclose8_normal")]
        public void CRC顔のサフィックス付きも除外する(string name)
        {
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Theory]
        [InlineData("itome_normal")]
        [InlineData("custom_shapekey")]
        [InlineData("eyeclose_custom")]
        public void タブで扱わない名前は残す(string name)
        {
            Assert.False(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void 空の名前は除外しない(string name)
        {
            Assert.False(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Fact]
        public void タブの全モーフを除外する()
        {
            foreach (var name in MaidFaceMorphController.GetAllMorphNames())
            {
                Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name), name);
            }
            foreach (var name in MTEP.FaceMorphUtils.saveMorphNames)
            {
                Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name), name);
            }
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FaceShapeKeyFilterTests`
Expected: コンパイルエラー (`FaceShapeKeyFilter` / `GetAllMorphNames` が無い)

- [ ] **Step 3: 実装する**

`MaidFaceMorphController.cs` の `FindDef` の直後に足す:

```csharp
        /// <summary>全カテゴリのモーフ名 (素の名前。CRC 顔のサフィックスは付けない)</summary>
        public static IEnumerable<string> GetAllMorphNames()
        {
            foreach (var defs in MorphDefs.Values)
            {
                foreach (var def in defs)
                {
                    yield return def.name;
                }
            }
        }
```

`FaceShapeKeyFilter.cs`:

```csharp
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表情ウィンドウのシェイプキータブから外す名前の判定。
    /// 目・眉・口・オプションタブ (表情レイヤー) が扱うモーフをシェイプキーレイヤーでも
    /// 触れると、同じブレンド値を 2 つのレイヤーが毎フレーム奪い合うため一覧に出さない。
    /// CRC 顔では eyeclose 系の実体がサフィックス付きの名前 (eyeclose1_normal 等) なので、
    /// ResolveMorphIndex が解決しうる名前をすべて含める
    /// </summary>
    public static class FaceShapeKeyFilter
    {
        private static HashSet<string> _faceMorphNames = null;

        /// <summary>表情タブが扱うモーフ名か。null / 空は false</summary>
        public static bool IsFaceMorphName(string shapeKeyName)
        {
            if (string.IsNullOrEmpty(shapeKeyName))
            {
                return false;
            }

            if (_faceMorphNames == null)
            {
                _faceMorphNames = BuildFaceMorphNames();
            }
            return _faceMorphNames.Contains(shapeKeyName);
        }

        /// <summary>
        /// 表情レイヤーの記録対象とタブの定義の和集合に、CRC 顔の全目型のサフィックス付きを足す。
        /// タイムラインの記録対象に無くタブにだけあるモーフ (nosefook) もあるため和集合にする
        /// </summary>
        private static HashSet<string> BuildFaceMorphNames()
        {
            var baseNames = new HashSet<string>(MTEP.FaceMorphUtils.saveMorphNames);
            baseNames.UnionWith(MaidFaceMorphController.GetAllMorphNames());

            var names = new HashSet<string>(baseNames);
            foreach (var name in baseNames)
            {
                for (var i = 0; i < TMorph.crcFaceTypesStr.Length; i++)
                {
                    names.Add(MaidFaceMorphController.GetCrcMorphName(name, i));
                }
            }
            return names;
        }
    }
}
```

csproj の `<Compile Include="FaceMorphRowDrawer.cs" />` の直後に `<Compile Include="FaceShapeKeyFilter.cs" />` を足す。

- [ ] **Step 3b: シーンプリセットの除外も同じ判定に寄せる**

`Manager/ScenePresetManager.cs` の `CaptureShapeKeysAndMaterials` (2211 行付近) は、任意シェイプキーの保存から表情モーフを除くため、`GetAvailableMorphs` の素の名前だけで `faceMorphNames` を作っている。CRC 顔ではサフィックス付きの名前 (`eyeclose1_normal` 等) が除外されない。`faceMorphNames` の組み立てを削除し、判定を共有の関数に置き換える:

```csharp
                    foreach (var tag in slot.morph.GetTags())
                    {
                        // 表情モーフは同じ TMorph を共有していて適用順で競合するため除外する
                        // (判定は表情ウィンドウのシェイプキータブと共有。CRC 顔のサフィックス付きも含む)
                        if (FaceShapeKeyFilter.IsFaceMorphName(tag) || !seenTags.Add(tag))
                        {
                            continue;
                        }
```

`faceMorphNames` の変数がほかで使われていないことを `rg -n "faceMorphNames" source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` で確かめる。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FaceShapeKeyFilterTests|MaidFaceMorphControllerTests"` が PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/FaceShapeKeyFilter.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/FaceShapeKeyFilterTests.cs
git commit -m "feat(face): 表情タブが扱うモーフ名の判定を追加する"
```

### Task 2: シェイプキー一覧を共有クラスへ切り出す

挙動は変えないリファクタリング。シェイプキーウィンドウのメイドタブの見た目・操作が変わらないことを確かめてからコミットする。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidShapeKeyListView.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs:48-57,169-297,325-339`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (:325 `<Compile Include="MaidShapeKeyRowDrawer.cs" />` の直前)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/MaidShapeKeyListViewTests.cs`

**Interfaces:**
- Produces:
  - `new MaidShapeKeyListView(Func<string, bool> tagFilter = null)` (tagFilter が false を返す名前は一覧に出さない)
  - `MaidShapeKeyListView.searchText : string` (フィールド、既定 `""`)
  - `MaidShapeKeyListView.GetSlotNames(Maid target) : List<string>` (シェイプキーを持つスロットのカテゴリ名。メイドが変わったときだけ作り直す)
  - `MaidShapeKeyListView.GetTags(Maid target, string slotName) : List<string>` (フィルタ・並べ替え済み。メイドかスロットが変わったときだけ作り直す。スロットが無ければ空)
  - `MaidShapeKeyListView.ClearCache() : void`
  - `MaidShapeKeyListView.IsSearchMatched(string name, string searchText) : bool` (static)
  - `MaidShapeKeyListView.BuildTagList(IEnumerable<string> tags, Func<string, bool> tagFilter) : List<string>` (static)
  - `MaidShapeKeyListView.DrawSearchField(GUIView view, float labelWidth, float rowHeight, float trailingWidth = 0f, Action drawTrailing = null) : void`
  - `MaidShapeKeyListView.DrawRows(GUIView view, Maid target, MTEP.MaidCache maidCache, List<string> tags, float rowHeight, Action onBeforeEdit = null) : void` (`onBeforeEdit` は Task 3 で `MaidShapeKeyRowDrawer.Draw` へ渡す。本タスクでは引数だけ用意し、そのまま渡す)

- [ ] **Step 1: 失敗するテストを書く**

`MaidShapeKeyListViewTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェイプキー一覧の検索と並びを固定する (シェイプキーウィンドウと表情ウィンドウで共有)</summary>
    public class MaidShapeKeyListViewTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void 検索_未入力なら素通し(string searchText)
        {
            Assert.True(MaidShapeKeyListView.IsSearchMatched("anything", searchText));
        }

        [Fact]
        public void 検索は大文字小文字を区別しない()
        {
            Assert.True(MaidShapeKeyListView.IsSearchMatched("MuneUp", "muneup"));
            Assert.True(MaidShapeKeyListView.IsSearchMatched("muneup", "UP"));
        }

        [Fact]
        public void 検索_含まなければ外す()
        {
            Assert.False(MaidShapeKeyListView.IsSearchMatched("mune", "hoho"));
        }

        [Fact]
        public void タグ一覧_フィルタなしは全部を並べ替える()
        {
            var result = MaidShapeKeyListView.BuildTagList(new[] { "b", "c", "a" }, null);
            Assert.Equal(new List<string> { "a", "b", "c" }, result);
        }

        [Fact]
        public void タグ一覧_フィルタがfalseの名前は外す()
        {
            var result = MaidShapeKeyListView.BuildTagList(
                new[] { "eyeclose", "custom2", "custom1" }, name => name != "eyeclose");
            Assert.Equal(new List<string> { "custom1", "custom2" }, result);
        }

        [Fact]
        public void タグ一覧_元の列は書き換えない()
        {
            var source = new List<string> { "b", "a" };
            MaidShapeKeyListView.BuildTagList(source, null);
            Assert.Equal(new List<string> { "b", "a" }, source);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter MaidShapeKeyListViewTests`
Expected: コンパイルエラー (`MaidShapeKeyListView` が無い)

- [ ] **Step 3: 共有クラスを実装する**

`MaidShapeKeyListView.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのシェイプキー一覧 (スロット / タグのキャッシュ・検索欄・行の列挙)。
    /// シェイプキーウィンドウのメイドタブと表情ウィンドウのシェイプキータブで共有する。
    /// 全スロットの morph 走査と GetTags() はどちらも毎フレーム回すには重いため、
    /// 対象が変わったときだけ作り直す。着替えはここから検知できないので、
    /// 呼び出し側の「更新」ボタンで ClearCache する
    /// </summary>
    public class MaidShapeKeyListView
    {
        /// <summary>検索欄の下限幅。右に要素を置いてウィンドウを縮めても潰れないようにする</summary>
        private const float MinSearchFieldWidth = 40f;

        private readonly Func<string, bool> _tagFilter;

        private readonly List<string> _slotNames = new List<string>();
        private Maid _slotNamesMaid = null;

        private List<string> _tags = new List<string>();
        private Maid _tagsMaid = null;
        private string _tagsSlotName = null;

        /// <summary>シェイプキー名の絞り込み。シェイプキーウィンドウではモデルタブとも共用する</summary>
        public string searchText = "";

        /// <param name="tagFilter">一覧に出すシェイプキーか。null なら全部出す</param>
        public MaidShapeKeyListView(Func<string, bool> tagFilter = null)
        {
            _tagFilter = tagFilter;
        }

        /// <summary>シェイプキーを持つスロットのカテゴリ名。メイドが変わったときだけ作り直す</summary>
        public List<string> GetSlotNames(Maid target)
        {
            if (_slotNamesMaid == target)
            {
                return _slotNames;
            }
            _slotNamesMaid = target;
            _slotNames.Clear();

            // COM3D2.5 の goSlot は直接列挙できないため、インデックス走査で両バージョンに対応する
            var slotCount = Mathf.Min((int) TBody.SlotID.end, target.body0.goSlot.Count);
            for (var i = 0; i < slotCount; i++)
            {
                var slot = target.body0.GetSlot(i);
                if (slot != null && slot.morph != null && slot.morph.hash.Count > 0)
                {
                    _slotNames.Add(slot.Category);
                }
            }
            return _slotNames;
        }

        /// <summary>
        /// スロットのタグ一覧 (フィルタ・並べ替え済み)。GetTags() は毎回リストを作るため、
        /// メイドかスロットが変わったときだけ作り直す (同名スロットでもメイドが違えばタグは別物)
        /// </summary>
        public List<string> GetTags(Maid target, string slotName)
        {
            if (_tagsMaid == target && _tagsSlotName == slotName)
            {
                return _tags;
            }
            _tagsMaid = target;
            _tagsSlotName = slotName;

            var slot = target.body0.GetSlot(slotName);
            var morph = slot != null ? slot.morph : null;
            _tags = morph != null
                ? BuildTagList(morph.GetTags(), _tagFilter)
                : new List<string>();
            return _tags;
        }

        /// <summary>スロット / タグ一覧のキャッシュを捨てる。「更新」ボタンから呼ぶ</summary>
        public void ClearCache()
        {
            _slotNamesMaid = null;
            _tagsMaid = null;
            _tagsSlotName = null;
        }

        /// <summary>検索欄の絞り込み判定。未入力なら素通し</summary>
        public static bool IsSearchMatched(string name, string searchText)
        {
            return string.IsNullOrEmpty(searchText)
                || name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>フィルタを通した名前を並べ替えた新しいリスト。元の列は書き換えない</summary>
        public static List<string> BuildTagList(IEnumerable<string> tags, Func<string, bool> tagFilter)
        {
            var result = new List<string>();
            foreach (var tag in tags)
            {
                if (tagFilter == null || tagFilter(tag))
                {
                    result.Add(tag);
                }
            }
            result.Sort();
            return result;
        }

        /// <summary>シェイプキー名の検索欄。スロット / 対象の行と列を揃える</summary>
        /// <param name="trailingWidth">検索欄の後ろに置くコントロールのために空ける幅 (間隔込み)</param>
        /// <param name="drawTrailing">検索欄の後ろに置くコントロールの描画</param>
        public void DrawSearchField(
            GUIView view, float labelWidth, float rowHeight,
            float trailingWidth = 0f, Action drawTrailing = null)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("検索", labelWidth, rowHeight, style: GUIView.gsLabelRight);

                var fieldWidth = drawTrailing == null
                    ? -1f
                    : Mathf.Max(
                        view.viewRect.width - view.padding.x * 2 - labelWidth - view.margin - trailingWidth,
                        MinSearchFieldWidth);
                view.DrawTextField(searchText, fieldWidth, rowHeight, value => searchText = value);

                if (drawTrailing != null)
                {
                    drawTrailing();
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 検索に合う編集可能なシェイプキーをスクロール内に並べる。
        /// 行の描画・履歴・変更追跡は MaidShapeKeyRowDrawer に任せる
        /// </summary>
        /// <param name="onBeforeEdit">値を書く直前に呼ぶ (表情ウィンドウの強制上書き ON)。null なら何もしない</param>
        public void DrawRows(
            GUIView view, Maid target, MTEP.MaidCache maidCache, List<string> tags,
            float rowHeight, Action onBeforeEdit = null)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var matchedCount = 0;

            view.BeginScrollView();
            {
                foreach (var tag in tags)
                {
                    if (!IsSearchMatched(tag, searchText))
                    {
                        continue;
                    }

                    var blendShape = maidCache.GetBlendShape(tag);
                    if (!MaidShapeKeyRowDrawer.IsEditable(blendShape))
                    {
                        continue;
                    }

                    matchedCount++;

                    MaidShapeKeyRowDrawer.Draw(
                        view, target, maidCache, tag, blendShape, rowHeight,
                        onBeforeEdit: onBeforeEdit);
                }

                if (matchedCount == 0)
                {
                    view.DrawLabel("該当するシェイプキーがありません", -1, rowHeight);
                }
            }
            view.EndScrollView();
        }
    }
}
```

このステップの時点では `MaidShapeKeyRowDrawer.Draw` に `onBeforeEdit` 引数がまだ無いので、Task 3 Step 1 の `MaidShapeKeyRowDrawer` の変更 (引数の追加だけ) を先にここで入れる:

```csharp
        public static void Draw(
            GUIView view,
            Maid target,
            MTEP.MaidCache maidCache,
            string shapeKeyName,
            MTEP.MaidBlendShape blendShape,
            float rowHeight,
            Action<GUIView> drawTrailing = null,
            Action onBeforeEdit = null)
        {
            var weight = blendShape.weight;
            // 値を書き込む直前に呼ぶ。同じシェイプキーへの連続変更はマウス解放まで 1 件に集約される
            Action recordEdit = () =>
            {
                // 呼び出し側の前処理 (表情ウィンドウの強制上書き ON) は別の履歴として積むため、
                // シェイプキーの確定待ちを作るより前に済ませる
                if (onBeforeEdit != null)
                {
                    onBeforeEdit();
                }
                HistoryManager.instance.BeforeEdit(
                    target, HistoryScope.ShapeKey, "シェイプキー: " + shapeKeyName,
                    shapeKeyName, () => MaidShapeKeySnapshot.Capture(target, maidCache, shapeKeyName));
            };
```

(以降の本体は変えない。クラスの summary の「ShapeKeyEditWindow と TimelineItemInspector (メニュー項目選択時) で共有する」を「MaidShapeKeyListView (シェイプキーウィンドウ・表情ウィンドウ) と TimelineItemInspector (メニュー項目選択時) で共有する」に直す。)

csproj の `<Compile Include="MaidShapeKeyRowDrawer.cs" />` の直前に `<Compile Include="MaidShapeKeyListView.cs" />` を足す。

- [ ] **Step 4: ShapeKeyEditWindow を共有クラスへ寄せる**

1. フィールド `_searchText`・`_slotNames`・`_slotNamesMaid`・`_tags`・`_tagsSlotName` とその上のキャッシュのコメント (:48-57) を消し、代わりに置く:

```csharp
        /// <summary>メイドタブのスロット / タグ一覧と検索欄。検索文字列はモデルタブとも共用する</summary>
        private readonly MaidShapeKeyListView _maidShapeKeyList = new MaidShapeKeyListView();
```

2. `DrawMaidShapeKeys` (:169-234) を置き換える:

```csharp
        /// <summary>スロット選択 → そのスロットの全シェイプキーをスライダー表示する</summary>
        private void DrawMaidShapeKeys(Maid target, MTEP.MaidCache maidCache)
        {
            var slotNames = _maidShapeKeyList.GetSlotNames(target);
            if (slotNames.Count == 0)
            {
                view.DrawLabel("シェイプキーを持つスロットがありません", -1, ROW_HEIGHT);
                return;
            }

            _slotNameComboBox.items = slotNames;
            DrawLabeledComboBox("スロット", _slotNameComboBox, UpdateButtonWidth + view.margin, () =>
            {
                // 着替えはウィンドウ側から検知できないため明示更新
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    _maidShapeKeyList.ClearCache();
                    maidCache.ClearBlendShapeCache();
                }
            });

            var slotName = _slotNameComboBox.currentItem;
            if (string.IsNullOrEmpty(slotName) || !target.body0.IsSlotNo(slotName))
            {
                return;
            }

            var tags = _maidShapeKeyList.GetTags(target, slotName);

            _maidShapeKeyList.DrawSearchField(view, LABEL_WIDTH, ROW_HEIGHT);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            _maidShapeKeyList.DrawRows(view, target, maidCache, tags, ROW_HEIGHT);
        }
```

3. `DrawSearchField` / `IsSearchMatched` / `ClearSlotCache` / `UpdateSlotNames` / `UpdateTags` (:236-297) を消す。

4. モデルタブ `DrawModelContent` の `DrawSearchField();` を `_maidShapeKeyList.DrawSearchField(view, LABEL_WIDTH, ROW_HEIGHT);` に、`if (!IsSearchMatched(shapeKeyName))` を `if (!MaidShapeKeyListView.IsSearchMatched(shapeKeyName, _maidShapeKeyList.searchText))` に置き換える。

5. `rg -n "_searchText|_slotNames|_tags|UpdateSlotNames|UpdateTags|ClearSlotCache" source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs` が 0 件であることを確かめる。

- [ ] **Step 5: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 6: 実機確認 (Review Focus 6)**

シェイプキーウィンドウのメイドタブで次を確かめる (`screenshot`)。
1. スロットを `body` → `head` に切り替えるとタグ一覧が入れ替わる
2. 検索欄に文字を入れると絞り込まれ、モデルタブへ切り替えても同じ文字が入っている
3. 「更新」を押してもエラーにならず一覧が出直す (`tail_log` で例外が無いこと)
4. スライダーを動かすと値が変わり、Undo で戻る

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidShapeKeyListView.cs source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs source/COM3D2.SceneEditor.Plugin/MaidShapeKeyRowDrawer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/MaidShapeKeyListViewTests.cs
git commit -m "refactor(shapekey): メイドのシェイプキー一覧を共有クラスへ切り出す"
```

### Task 3: 表情ウィンドウのシェイプキータブ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:21-34,124-141,158-191`

**Interfaces:**
- Consumes: `FaceShapeKeyFilter.IsFaceMorphName`、`MaidShapeKeyListView` (Task 2 の全メンバー)、`MaidShapeKeyRowDrawer.Draw(..., Action onBeforeEdit = null)` (Task 2 で追加済み)、既存の `MaidFaceMorphController.GetMabataki` / `SetForceOverride`、`TimelineLayerGate.Begin(GUIView, Type, Maid, float) : TimelineLayerGateState`
- Produces: なし (UI のみ)

- [ ] **Step 1: タブとフィールドを足す**

`FaceTab` に `シェイプキー` を `視線` と `プリセット` の間へ足し、summary を直す:

```csharp
        /// <summary>
        /// ウィンドウ内の内部タブ。先頭 4 つは FaceMorphCategory と同順で 1:1 対応。
        /// 並びは保存しないので途中へ足してよい
        /// </summary>
        private enum FaceTab
        {
            目,
            眉,
            口,
            オプション,
            視線,
            シェイプキー,
            プリセット,
        }
```

`_tab` の下に足す:

```csharp
        /// <summary>シェイプキータブの「更新」ボタンの幅</summary>
        private const int SHAPE_KEY_UPDATE_BUTTON_WIDTH = 50;

        /// <summary>
        /// 顔スロットのシェイプキー一覧。目・眉・口・オプションタブが扱うモーフは
        /// 表情レイヤーと値を奪い合うため出さない
        /// </summary>
        private readonly MaidShapeKeyListView _shapeKeyList =
            new MaidShapeKeyListView(name => !FaceShapeKeyFilter.IsFaceMorphName(name));
```

- [ ] **Step 2: タイムラインのレイヤー選択への追従を直す**

`TryFocusTimelineLayer` の `MorphTimelineLayer` 分岐を置き換える:

```csharp
            if (layerType == typeof(MTEP.MorphTimelineLayer))
            {
                // 表情レイヤーへ記録するタブは複数あるので、別レイヤーのタブ (視線・シェイプキー) から戻すときだけ先頭へ移す
                if (_tab == FaceTab.視線 || _tab == FaceTab.シェイプキー)
                {
                    _tab = FaceTab.目;
                }
                return true;
            }
            // ShapeKeyTimelineLayer はシェイプキーウィンドウが受け持つ。
            // こちらも応じると、髪や衣装のシェイプキーを選んだだけで表情ウィンドウまで前面に出てくる
            return false;
```

(既存の末尾の `return false;` は上の `return false;` に置き換わる。)

- [ ] **Step 3: タブの描き分けとゲートのレイヤーを直す**

`DrawMaidContent` の `_tab = DrawInnerTabs(_tab, 70);` 以降を置き換える:

```csharp
            _tab = DrawInnerTabs(_tab, 70);

            var gateState = TimelineLayerGate.Begin(view, GetTimelineLayerType(_tab), target, ROW_HEIGHT);

            if (_tab == FaceTab.プリセット)
            {
                DrawPresetContent(view, target);
            }
            else if (_tab == FaceTab.視線)
            {
                DrawLookContent(view, target);
            }
            else if (_tab == FaceTab.シェイプキー)
            {
                DrawShapeKeyContent(view, target, gateState);
            }
            else
            {
                DrawMorphList(view, target);
            }
        }

        /// <summary>
        /// タブの値が記録されるタイムラインレイヤー。視線タブは瞳レイヤー、シェイプキータブは
        /// シェイプキーレイヤー、それ以外 (表情・プリセット) は表情レイヤー。
        /// プリセット適用もモーフ値を直接書くのでスライダーと同じ扱い
        /// </summary>
        private static Type GetTimelineLayerType(FaceTab tab)
        {
            switch (tab)
            {
                case FaceTab.視線: return typeof(MTEP.EyesTimelineLayer);
                case FaceTab.シェイプキー: return typeof(MTEP.ShapeKeyTimelineLayer);
                default: return typeof(MTEP.MorphTimelineLayer);
            }
        }
```

`isMorphTab` を、対象カテゴリを持つタブの列挙に直す (シェイプキータブでリセットを出さないため):

```csharp
        /// <summary>モーフ一覧を描くタブか。視線・シェイプキー・プリセットは対象カテゴリを持たない</summary>
        private bool isMorphTab =>
            _tab == FaceTab.目 || _tab == FaceTab.眉 || _tab == FaceTab.口 || _tab == FaceTab.オプション;
```

- [ ] **Step 4: シェイプキータブの中身を描く**

`DrawMorphList` の直後に足す:

```csharp
        /// <summary>
        /// シェイプキータブ。顔スロットのシェイプキーをシェイプキーウィンドウと同じ行で並べる。
        /// 値・履歴・変更追跡はシェイプキーの仕組み (MaidShapeKeyRowDrawer) に乗る
        /// </summary>
        private void DrawShapeKeyContent(GUIView view, Maid target, TimelineLayerGateState gateState)
        {
            var face = target.body0 != null ? target.body0.Face : null;
            if (face == null || face.morph == null)
            {
                view.DrawLabel("顔が読み込まれていません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (maidCache == null)
            {
                // ゲートが同じ状況を通知済みなら重ねて出さない
                if (gateState != TimelineLayerGateState.MaidNotFound)
                {
                    view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, textColor: Color.yellow);
                }
                return;
            }

            var tags = _shapeKeyList.GetTags(target, face.Category);

            _shapeKeyList.DrawSearchField(view, LABEL_WIDTH, ROW_HEIGHT,
                SHAPE_KEY_UPDATE_BUTTON_WIDTH + view.margin, () =>
                {
                    // 顔の差し替えはウィンドウ側から検知できないため明示更新
                    if (view.DrawButton("更新", SHAPE_KEY_UPDATE_BUTTON_WIDTH, ROW_HEIGHT))
                    {
                        _shapeKeyList.ClearCache();
                        maidCache.ClearBlendShapeCache();
                    }
                });

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            _shapeKeyList.DrawRows(view, target, maidCache, tags, ROW_HEIGHT,
                () => EnableForceOverrideForEdit(target));
        }

        /// <summary>
        /// シェイプキーを書く直前に強制上書きを ON にする。OFF の間はゲームが毎フレーム
        /// 顔のブレンド値を作り直すため編集が効かない (表情スライダーの SetMabataki(false) と同じ理由)。
        /// 切り替えは表情の履歴として別に積み、Undo でシェイプキーの変更とは別に戻せるようにする。
        /// タイムラインが上書き中はユーザー設定側だけが変わる (表情スライダーと同じ)
        /// </summary>
        private static void EnableForceOverrideForEdit(Maid target)
        {
            // GetMabataki はユーザー設定 (上書き中は退避値)。すでに ON なら履歴も積まない
            if (!MaidFaceMorphController.GetMabataki(target))
            {
                return;
            }

            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face, "強制上書き切替");
            MaidFaceMorphController.SetForceOverride(target, true);
        }
```

`TimelineLayerGateState` は `COM3D2.SceneEditor.Plugin` 名前空間 (`TimelineLayerGateText.cs`) なので using の追加は要らない。

- [ ] **Step 5: 両構成をビルドし、全テストを通す**

- [ ] **Step 6: 実機確認 (Review Focus 1・2・3)**

通常シーンでメイドを呼び出し、表情ウィンドウを開く。
1. タブが `目 / 眉 / 口 / オプション / 視線 / シェイプキー / プリセット` の順で並び、ウィンドウ幅 300 で折り返して崩れない (`screenshot`)
2. シェイプキータブに `eyeclose` 系・`mouth` 系・`hoho` などが出ない。`eval_csharp` で顔の全タグから除外されたものを列挙し、目・眉・口・オプションのモーフだけであることを確かめる:
   ```csharp
   var maid = GameMain.Instance.CharacterMgr.GetMaid(0);
   var tags = maid.body0.Face.morph.GetTags();
   string.Join(",", tags.Where(t => COM3D2.SceneEditor.Plugin.FaceShapeKeyFilter.IsFaceMorphName(t)).ToArray())
   ```
3. ヘッダーの `強制上書き` を OFF にしてから、シェイプキーのスライダーを動かす → `強制上書き` が ON に変わり、値がその場に残る (次のフレームで戻らない)
4. Undo 1 回でシェイプキーの値が戻り、もう 1 回で `強制上書き` が OFF に戻る (履歴ウィンドウに「強制上書き切替」と「シェイプキー: 〜」の 2 件)
5. タイムラインを読み込み、`メイドシェイプ` レイヤーが無い状態でシェイプキータブを開く → 追加ボタンが出て項目が無効。追加後にスライダーを動かすと `メイドシェイプ` レイヤーへキーが入る。`目` タブへ戻るとゲートは `メイド表情` を対象にする
6. タイムラインで `メイド表情` レイヤーを選ぶとシェイプキータブから `目` タブへ移り、`メイドシェイプ` レイヤーを選んでも表情ウィンドウは前面に出ない
7. 検索欄と「更新」ボタンが 1 行に収まり、ウィンドウを最小幅まで縮めても「更新」がはみ出さない

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs
git commit -m "feat(face): 表情ウィンドウに顔のシェイプキータブを追加する"
```

### Task 4: 呼び出したメイドの強制上書きを既定 ON にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs:473-507` (`ApplyPhotoFacePreset`)、`HasBlendSet` の直後 (:658 付近)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:501-509` (`UpdateLoadingMaids`)

**Interfaces:**
- Consumes: 既存の `MaidFaceMorphController.GetFaceMorph` / `SetForceOverride`
- Produces: `MaidFaceMorphController.EnableForceOverrideForCalledMaid(Maid maid) : void`

- [ ] **Step 1: 2.0 の API を確かめる**

`ilspycmd -t TMorph "W:/COM3D2/COM3D2x64_Data/Managed/Assembly-CSharp.dll" | grep -n "EyeMabataki\|public void MulBlendValues\|dicBlendSet"` で、2.0 にも `public float EyeMabataki` と `MulBlendValues(string, float)` があることを確かめる。無ければ `EyeMabataki` の行を `#if COM3D25` で囲む (両構成のビルドで判断する)。

- [ ] **Step 2: ブレンドセット名の解決を共通化し、呼出時の ON を足す**

`HasBlendSet` の直後に足す:

```csharp
        /// <summary>
        /// 表情タグをブレンドセット名へ解決する。新ボディ顔の別名 (〓通常) を優先し、
        /// どちらも無ければ null (Maid.FaceAnime と同じ解決順)
        /// </summary>
        private static string ResolveBlendSetName(TMorph morph, string faceName)
        {
            var aliasName = faceName + "〓通常";
            if (morph.dicBlendSet.ContainsKey(aliasName))
            {
                return aliasName;
            }
            return morph.dicBlendSet.ContainsKey(faceName) ? faceName : null;
        }

        /// <summary>
        /// 新しく呼び出したメイドの強制上書きを ON にする。
        /// ON の間ゲームは表情ブレンドを作り直さない (Maid.Update の boMabataki 分岐) ため、
        /// ロード完了時の FaceAnime("通常", 1f) のフェードが進まず表情が決まらない。
        /// フェードを畳み、今の表情タグのブレンドを直接書き込んで固めてから ON にする。
        /// まばたきの途中で止めると目が半開きのまま残るので、まばたき量も 0 に戻す
        /// </summary>
        public static void EnableForceOverrideForCalledMaid(Maid maid)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            var faceName = maid.ActiveFace;
            if (!string.IsNullOrEmpty(faceName))
            {
                // t=0 の FaceAnime はフェードを畳んで FaceName を揃えるだけで、ブレンドは塗らない
                maid.FaceAnime(faceName, 0f, 0);

                var blendSetName = ResolveBlendSetName(morph, faceName);
                if (blendSetName != null)
                {
                    morph.MulBlendValues(blendSetName, 1f);
                }
            }

            morph.EyeMabataki = 0f;
            morph.FixBlendValues_Face();

            SetForceOverride(maid, true);
        }
```

`ApplyPhotoFacePreset` の名前解決を置き換える (:492-502):

```csharp
            // FaceAnime(t=0) は FaceName を設定するだけで、実際のブレンド反映は
            // boMabataki 有効時の毎フレーム処理 (Maid.Update) でしか走らない。
            // まばたきを止めた直後は誰も反映しないため、ブレンドセットを直接書き込む
            var settingName = ResolveBlendSetName(morph, data.setting_name);
            if (settingName == null)
            {
                MTEUtils.LogWarning("表情プリセットが見つかりません: {0}", data.setting_name);
                return;
            }
```

- [ ] **Step 3: ロード完了時に呼ぶ**

`UpdateLoadingMaids` の立ちモーションの `if (string.IsNullOrEmpty(maid.body0.LastAnimeFN)) { ... }` ブロックの直後に足す:

```csharp
                // 新しく呼び出したメイドは強制上書き ON で出す。呼出済みメイドの呼び直しは
                // CallMaid の早期 return でここを通らないため、ユーザーの設定が残る。
                // シーンプリセットの保留適用 (UpdatePendingApplies) はこの後に走るので、
                // プリセットに保存された設定 (旧データは OFF) が最終値になる
                MaidFaceMorphController.EnableForceOverrideForCalledMaid(maid);
```

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

- [ ] **Step 5: 実機確認 (Review Focus 4・5)**

1. 呼出ウィンドウで新しくメイドを呼び出す → 表情ウィンドウの `強制上書き` が ON。`eval_csharp` で `maid.boMabataki` が false、`maid.body0.Face.morph.EyeMabataki` が 0 であることを確かめ、`screenshot` で「通常」の表情 (目が開いている・無表情に崩れていない) で出ていることを見る。数秒待ってもまばたきしない
2. `強制上書き` を OFF にしてから、同じメイドを呼出ウィンドウで選び直す → OFF のまま
3. メイドを解除してから呼び直す → ON で出る (新規呼出の扱い)
4. 旧データのシーンプリセット (`mabataki` を持たない、または true のもの) を読み込んでメイドを呼び出す → プリセットどおり `強制上書き` OFF でまばたきする
5. タイムラインを読み込んだ状態で新しく呼び出しても、表情レイヤーの強制上書きキーの値が優先される (キー OFF ならまばたきする)

- [ ] **Step 6: コミット**

変更履歴はリリース時に release-prep が git log から書くため、挙動変更をコミット本文に明記する。

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs
git commit -m "feat(face): 呼び出したメイドの表情の強制上書きを既定で ON にする" -m "挙動変更: 新しく呼び出したメイドは表情の強制上書きが ON になり、まばたきが止まる。呼出済みメイドの呼び直しとシーンプリセット・表情プリセットの読み込みは従来どおり保存された設定に従う。変更履歴に記載すること"
```

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/guide/maid-editing.md:15` (ウィンドウ表の表情の行) と `## 視線` 節の前
- Modify: `docs-site/timeline/layers-maid.md:83-88` (`## メイドシェイプ`)

- [ ] **Step 1: 表情ウィンドウの説明を直す**

`maid-editing.md` のウィンドウ表の表情の行を次にする:

```markdown
| 表情 | 表情・視線・顔のシェイプキーの編集とプリセット（`目` / `眉` / `口` / `オプション` / `視線` / `シェイプキー` / `プリセット` のタブで切替） |
```

`## 視線` の直前に節を足す:

```markdown
## 表情の強制上書き

表情ウィンドウ上部の `強制上書き` が ON の間は、ゲーム側のまばたきと表情の自動更新が止まり、編集した表情がそのまま残ります。
新しく呼び出したメイドは ON の状態で出てきます（まばたきしません）。
まばたきさせたいときは OFF にしてください。呼出済みのメイドを選び直したときと、シーンプリセット・表情プリセットを読み込んだときは保存された設定に従います。

## 顔のシェイプキー

表情ウィンドウの `シェイプキー` タブで、顔のシェイプキーを編集できます。
操作はシェイプキーウィンドウのメイドタブと同じです（検索・チェック・スライダー）。

- `目` / `眉` / `口` / `オプション` タブで扱うモーフは一覧に出ません（表情レイヤーと値がぶつかるため）
- 値は `メイドシェイプ` レイヤーに記録されます。このタブを開いている間はレイヤーゲートも `メイドシェイプ` を対象にします
- `強制上書き` が OFF のときにスライダーを動かすと、自動で ON になります（OFF のままだとゲームが毎フレーム顔の値を戻すため）
- 顔を差し替えた後は `更新` を押すと一覧が作り直されます
```

- [ ] **Step 2: メイドシェイプレイヤーの説明に追記する**

`layers-maid.md` の `## メイドシェイプ` の箇条書きの 1 行目を次にする:

```markdown
- シェイプキーウィンドウか、表情ウィンドウの `シェイプキー` タブ（顔のみ）でチェックした項目だけが対象です。チェックすると 0 フレーム目にキーが打たれ、外すと全フレームから消えます
```

- [ ] **Step 3: 表記の整合を確かめる**

`rg -n "強制上書き|シェイプキー" docs-site/guide docs-site/timeline` で、既存の記述 (タイムラインの強制上書きキーが既定 ON、など) と食い違う文が無いことを確かめる。

- [ ] **Step 4: コミット**

```bash
git add docs-site/guide/maid-editing.md docs-site/timeline/layers-maid.md
git commit -m "docs(face): 顔のシェイプキータブと強制上書きの既定 ON を説明する"
```

## レビュー却下メモ

- Task 2 の `onBeforeEdit` 引数の追加を Task 3 へ移す — 見送り。機械的な引数追加で、Task 2 のビルドとテストで挙動が変わらないことは確かめられる
