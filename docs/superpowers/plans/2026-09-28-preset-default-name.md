# シーンプリセット名の日付既定値 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** シーンプリセット保存ポップアップの既定名を、プリセット未選択時は現在日時にし、名前欄の横に「日付」ボタンを置いていつでも日時の名前へ戻せるようにする。

**Architecture:** 日時からプリセット名を作る純関数を `ScenePresetNaming` (新規 static クラス) に置き、`SavePresetPopupWindow.Show` の既定値と「日付」ボタンの両方から使う。`ScenePresetManager` は静的初期化で一覧を組み立てるためテストから触れないので、名前生成はそこから切り離す。

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48)

**Spec:** 本計画の「仕様」節 (機能要望の調査結果に対するユーザー回答: 「プリセット未選択のときだけ日付にするで ok。追加で日付既定値に変更ボタンも追加」)

## 仕様

- 保存ポップアップを開いたとき、読み込み / 保存済みのプリセットがある (`ScenePresetManager.currentPresetName` が空でない) なら従来どおりその名前、未選択 (空) なら現在日時の名前を既定値にする
- 日時の書式は `yyyyMMdd_HHmmss` (例: `20260928_153012`)。プリセット名はドット禁止・ファイル名禁則文字禁止 (`ScenePresetManager.ValidatePresetName`) なので、スクリーンショット名と同系統の数字とアンダースコアだけにする
- 名前欄の右に「日付」ボタンを置く。押すと名前を押した時点の日時の名前に置き換え、検証エラー表示を消す。入力欄のフォーカスは外す (IMGUI の TextField がフォーカス中の編集バッファで値を上書きしないように)
- 日付の後ろに任意の文字を足す操作は、名前欄を直接編集して行う (専用 UI は持たない)
- 保存形式・設定ファイルは変更しない

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1` を付ける。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカード指定ではない)

## Review Focus

1. 生成した既定名が `ValidatePresetName` を通る (ドット・禁則文字を含まない) — Task 1 のテスト
2. 読み込み済みプリセットがあるときは従来どおりその名前が既定になる (上書き保存の操作性を壊さない) — Task 2 の実機確認
3. 名前欄にフォーカスがある状態で「日付」を押しても、欄の表示が日時へ変わる — Task 2 の実機確認
4. 同じ秒に 2 回開いて保存すると同名になり、既存の上書き確認ダイアログが出る (新たな対策は不要) — Task 2 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/ScenePresetNaming.cs` | 日時 → プリセット名 |
| Modify `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs` | 既定値の切替・「日付」ボタン |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | Compile 追加 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNamingTests.cs` | 書式のテスト |
| Modify `docs-site/guide/` のプリセット保存の説明箇所 (Task 2 Step 1 で特定) | 既定名と「日付」ボタンの説明 |

---

### Task 1: 日時からプリセット名を作る

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ScenePresetNaming.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNamingTests.cs`

**Interfaces:**
- Produces: `ScenePresetNaming.CreateDateName(DateTime time) : string`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System;
using System.IO;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>プリセット保存ポップアップの日時の既定名を固定する</summary>
    public class ScenePresetNamingTests
    {
        [Fact]
        public void 日時名_年月日と時分秒をアンダースコアでつなぐ()
        {
            var name = ScenePresetNaming.CreateDateName(new DateTime(2026, 9, 8, 7, 5, 3));
            Assert.Equal("20260908_070503", name);
        }

        [Fact]
        public void 日時名_プリセット名の禁則を含まない()
        {
            var name = ScenePresetNaming.CreateDateName(new DateTime(2026, 12, 31, 23, 59, 59));
            Assert.DoesNotContain(".", name);
            Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetNamingTests`
Expected: コンパイルエラー (`ScenePresetNaming` が無い)

- [ ] **Step 3: 実装する**

`ScenePresetNaming.cs`:

```csharp
using System;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセットの既定名。
    /// ScenePresetManager は静的初期化で一覧を組み立てるため、テストできるよう切り離している
    /// </summary>
    public static class ScenePresetNaming
    {
        /// <summary>
        /// 日時の名前。プリセット名はドットとファイル名の禁則文字を使えないため、
        /// 数字とアンダースコアだけで組む
        /// </summary>
        public static string CreateDateName(DateTime time)
        {
            return time.ToString("yyyyMMdd_HHmmss");
        }
    }
}
```

csproj の `<Compile Include="SavePresetPopupWindow.cs" />` の直後に `<Compile Include="ScenePresetNaming.cs" />` を足す (行が無ければ同じ ItemGroup の末尾)。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

Global Constraints の MSBuild を COM3D2 → COM3D25 の順に実行し、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetNamingTests` が PASS することを確認する。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetNaming.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNamingTests.cs
git commit -m "feat(preset): プリセット名の日時の既定名を作る関数を追加する"
```

### Task 2: 保存ポップアップの既定名と「日付」ボタン

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs:43-44,84-85,190-206`
- Modify: docs-site のプリセット保存の説明

**Interfaces:**
- Consumes: `ScenePresetNaming.CreateDateName(DateTime)`

- [ ] **Step 1: 既定名を切り替える**

`Show()` の 84-85 行を置き換える:

```csharp
            // 上書き保存が主な操作なので読み込み中のプリセット名を既定にする。
            // 未選択なら名前を考えずにすぐ保存できるよう日時にする
            var currentName = ScenePresetManager.currentPresetName;
            window._presetName = string.IsNullOrEmpty(currentName)
                ? ScenePresetNaming.CreateDateName(DateTime.Now)
                : currentName;
```

`_presetName` のコメント (43 行) を「表示のたびに読み込み中プリセット名 (未選択なら日時) で初期化する」に直す。

- [ ] **Step 2: 「日付」ボタンを足す**

定数を追加する (BUTTON_WIDTH の下):

```csharp
        /// <summary>名前欄の右の「日付」ボタンの幅</summary>
        private static readonly int DATE_BUTTON_WIDTH = 40;
```

`DrawWindow` の名前行 (190-206 行) を置き換える:

```csharp
            _view.BeginHorizontal();
            {
                _view.DrawLabel("名前", 40, ROW_HEIGHT);
                _view.DrawTextField(new GUIView.TextFieldOption
                {
                    value = _presetName,
                    width = contentWidth - 40 - DATE_BUTTON_WIDTH - GUIView.defaultMargin * 2,
                    hiddenButton = true,
                    // 入力し直したら前回の検証エラー表示を消す
                    onChanged = value =>
                    {
                        _presetName = value;
                        _errorMessage = null;
                    },
                });

                if (_view.DrawButton("日付", DATE_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    _presetName = ScenePresetNaming.CreateDateName(DateTime.Now);
                    _errorMessage = null;
                    // フォーカス中の TextField は編集バッファを優先して表示するため、外して値を反映させる
                    GUIUtility.keyboardControl = 0;
                }
            }
            _view.EndLayout();
```

- [ ] **Step 3: ドキュメントを直す**

`rg -n "プリセット" docs-site/guide` で保存ポップアップの説明箇所を探し、「未選択なら日時 (`yyyyMMdd_HHmmss`) が既定名になる」「名前欄の右の `日付` ボタンで日時の名前に戻せる。後ろに文字を足して使える」を追記する。該当箇所が無ければ `docs-site/guide/getting-started.md` のプリセットの節に足す。

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 5: 実機確認 (ゲーム起動中なら devbridge、停止中は restart-verify スキル)**

通常シーン (デイリー画面でエディタ有効) で確認する:
1. ゲーム起動直後に保存ポップアップを開く → 名前が日時
2. 既存プリセットを読み込んでから開く → そのプリセット名
3. 名前欄をクリックしてフォーカスしたまま「日付」 → 欄が日時に変わる
4. 日時の後ろに `_test` を足して保存 → 一覧に出る

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs docs-site
git commit -m "feat(preset): 未選択時の保存名を日時にし、日付ボタンを追加する"
```

## レビュー却下メモ

- `GUIUtility.keyboardControl = 0` がモーダル外のフォーカスへ影響しないかの確認 — 未確認のまま見送り。`GUI.ModalWindow` 表示中は背後のウィンドウを操作できず、フォーカスを外して困る対象が無い
