# タイムライン リリース前 負債対応（優先度 低）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** リリース前に残った低優先度の負債のうち、データ消失につながる確認不足（19）・設定の二系統（20）・ショートカットのウィンドウ依存（21）をコードで解消し、残り（22〜25）は現状維持として仕様書の記載を確定する。

**Architecture:** タイムライン XML には一切触れない。変更は「UI の確認ダイアログを 2 か所足す」「タイムライン用キー割り当てを `Timeline.xml` から `SceneEditor.xml` へ移す」「キー処理をウィンドウの `Update` からプラグイン本体の `Update` へ移す」の 3 つ。キーの移設は移行処理を作らないため、旧 `Timeline.xml` のカスタムキーは既定へ戻る（リリース前なので許容と決定済み）。

**Tech Stack:** C#（COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成）、xunit（net48）、MSBuild、VitePress（`docs-site/`）

**Spec:** `docs/superpowers/specs/2026-09-14-timeline-release-debt-low-design.md`
（元となった負債一覧は `docs/timeline-release-debt-review.md` の優先度 低 19〜25）

## Global Constraints

- **XML 形式と version は変えない。** `TimelineData.CurrentVersion` は 34 のまま。タイムライン XML には触れない
- **`Timeline.xml` / `SceneEditor.xml` の移行処理は作らない。** リリース前なのでカスタムキーが既定へ戻ることは許容する。`Config.ConvertVersion` に新しい分岐を足さない
- **コメントとログメッセージは日本語で書く。** 既存コードの記述密度に合わせる
- **ビルドは 2 構成とも通すこと。** COM3D2 構成は **.NET 3.5**（入力 5 個以上の `Func<>`/`Action<>` 不可、`Tuple` 一部不可）。片方だけ通して「ビルド成功」と報告しない
- **`debug.bat` / `deploy.bat` / `deploy.ps1` は実行しない。** `debug.bat` はゲームフォルダへ DLL をコピーする。ビルドは MSBuild を直接叩く
- **`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` は `<Compile Include=...>` の明示列挙。** 本計画では Task 3 で 1 ファイル追加するので csproj にも行を足す。テストプロジェクトは SDK スタイルなので追加不要
- **git worktree は使わない。** メインの作業ディレクトリで作業する
- ビルド・テストは常に次の 1 本で回す（Git Bash）。COM3D2 構成のビルドは `bin/Debug/COM3D25/` を消すため、必ず COM3D2 → COM3D25 → `dotnet test` の順にする

  ```bash
  cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
  export MSYS2_ARG_CONV_EXCL="*"
  MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && dotnet test source/COM3D2.SceneEditor.Plugin.Tests
  ```

## 設計書からの逸脱（実装前に把握すること）

- 設計書は「`isKeyInputEnabled` を SE 本体の `Config` へ移設する」と書いているが、**SE 本体の `GetKey` / `GetKeyDown` の中に取り込まない**。取り込むと連番画像出力中に本体側のキー（プラグイン表示切替・ギズモ・Undo）まで止まり、現状の挙動が変わってしまう。名前を `isTimelineKeyInputEnabled` にして、タイムライン系キーの読み手（`TimelineKeyInput` と `TimelineWindow.isMultiSelect`）が自分で見る形にする
- 連番画像出力の確認は**ダイアログを増やさず、既存の確認文を出力先の有無で差し替える**。出力開始前に 2 回確認させない

## File Structure

| ファイル | 役割 | 変更 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Config.cs` | SE 本体の設定（`SceneEditor.xml`）。キー割り当ての唯一の持ち主になる | `KeyBindType` に 14 種追加、既定値追加、`GetKeyDownRepeat` / `isTimelineKeyInputEnabled` 追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` | タイムライン設定（`Timeline.xml`）。キーを手放す | `KeyBindType` / `keyBinds` / `keyBindsXml` / `GetKey*` / `isKeyInputEnabled` / `disablePoseHistory` を削除 |
| `source/COM3D2.SceneEditor.Plugin/TimelineKeyInput.cs` | **新規**。タイムライン操作のキー入力。ウィンドウ表示状態に依らず動く | 新規作成 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs` | プラグイン本体の `Update` | `TimelineKeyInput.Update()` の呼び出し追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs` | タイムライン操作ウィンドウ | `Update` / `UpdateKeyInput` / `SeekFrameWithScroll` を撤去、セーブ上書き確認、`GetKeyName` の参照先変更 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs` | グリッド | `isMultiSelect` の参照先変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | タイムライン本体 | `isKeyInputEnabled` の参照先変更、`OutputImage` の確認文言 |
| `source/COM3D2.SceneEditor.Plugin.Tests/KeyBindConfigTests.cs` | **新規**。キー割り当ての既定と旧設定の読み飛ばしを固定 | 新規作成 |
| `docs-site/timeline/shortcuts.md` / `files.md` / `compatibility.md` | 仕様書 | 記述を実態へ合わせる |

---

### Task 1: セーブの上書きと連番画像出力の削除に確認を出す（項目 19）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs`（`OnSaveClicked`、445 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（`OutputImage`、1036 行付近）

**Interfaces:**
- Consumes: `MTEUtils.ShowConfirmDialog(string message, Action onYes, Action onNo = null)`、`MTEP.PluginUtils.GetImageOutputDirPath(string anmName)`、`timeline.timelinePath`
- Produces: なし（後続タスクはこの変更に依存しない）

**テストについて:** ダイアログと `Directory` 操作は Unity / ゲーム本体に依存し、テストプロジェクト（xunit / net48）から実行できない。このタスクはビルドと実機確認で検証する。TDD は Task 2 で行う。

- [ ] **Step 1: セーブの上書き確認を入れる**

`TimelineControlWindow.OnSaveClicked` を次の内容へ置き換える。`ShowConfirmDialog` はコールバック方式なので、保存とその後の一覧更新をまとめてコールバックへ入れること（`SaveTimeline()` の中に確認を置くと `Reload()` が保存前に走る）。

```csharp
        private void OnSaveClicked()
        {
            if (!timelineManager.IsValidData())
            {
                MTEUtils.ShowDialog(timelineManager.errorMessage);
                return;
            }

            // 同名の別タイムラインを気付かず潰さないよう、上書きになるときだけ確認する
            if (System.IO.File.Exists(timeline.timelinePath))
            {
                MTEUtils.ShowConfirmDialog(
                    $"タイムライン「{anmName}」は既にあります\n上書きしますか?",
                    SaveTimelineAndReload);
                return;
            }

            SaveTimelineAndReload();
        }

        private static void SaveTimelineAndReload()
        {
            timelineManager.SaveTimeline();

            // 保存したタイムラインをサムネ付きで一覧へ出す
            TimelineLoadManager.Reload();
        }
```

- [ ] **Step 2: 連番画像出力の確認文へ削除警告を足す**

`TimelineManager.OutputImage` を次の内容へ置き換える。出力先が既に存在するときだけ文言を差し替え、ダイアログの数は増やさない。

```csharp
        public void OutputImage()
        {
            var message = "連番画像出力を開始しますか？\n出力中は[Esc]キーで停止できます";

            // 出力先は OutputImageInternal が中身ごと削除して作り直すため、事前に知らせる
            if (timeline != null)
            {
                var outputDir = PluginUtils.GetImageOutputDirPath(timeline.anmName);
                if (Directory.Exists(outputDir))
                {
                    message = "連番画像出力を開始しますか？\n出力中は[Esc]キーで停止できます\n\n"
                        + "既存の出力先を中身ごと削除します\n" + outputDir;
                }
            }

            MTEUtils.ShowConfirmDialog(message, () =>
            {
                MTEUtils.Log("連番画像出力を開始しました");
                GameMain.Instance.StartCoroutine(OutputImageInternal());
            });
        }
```

- [ ] **Step 3: ビルドとテストを実行する**

Global Constraints のコマンド 1 本を実行する。
Expected: 2 構成ともビルド成功、`dotnet test` が全 PASS（このタスクではテストを増やしていないので件数は変わらない）

- [ ] **Step 4: 実機で確認する**

ゲームを起動して次を確認する。

1. 新しいアニメ名で `セーブ` → 確認が出ずに保存され、`タイムライン「名前」を保存しました` が出る
2. もう一度同じ名前で `セーブ` → `上書きしますか?` が出る。`いいえ` でファイルの更新日時が変わらないこと、`はい` で保存されることを確認する
3. `ファイル` → `連番画像出力` を 1 回実行して出力先を作り、もう一度実行 → 確認文に `既存の出力先を中身ごと削除します` とパスが出る

- [ ] **Step 5: 仕様書を直す**

`docs-site/timeline/files.md` の「保存」節（27 行付近）へ、既存ファイルがあるときは上書き確認が出る旨を足す。

```markdown
- 同名のタイムラインが既にある場合は `上書きしますか?` の確認が出ます。新規保存では出ません
```

同ファイルの「連番画像出力」節（85 行付近）の確認文の説明を、出力先がある場合は削除警告が出る旨へ書き換える。

```markdown
`ファイル` → `連番画像出力` で、各フレームを PNG として書き出します。`連番画像出力を開始しますか？ 出力中は[Esc]キーで停止できます` の確認が出ます。出力先フォルダが既にある場合は、中身ごと削除する旨が確認文に追加されます。
```

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs \
        docs-site/timeline/files.md
git commit -m "feat(timeline): セーブの上書きと連番画像出力の削除に確認を出す"
```

---

### Task 2: タイムラインのキー割り当てを SceneEditor.xml へ統合する（項目 20）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`KeyBindType` は 10 行付近、`keyBinds` は 519 行付近、`GetKey*` は 578 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs`（`KeyBindType` は 8 行付近、`disablePoseHistory` は 83 行付近、`keyBinds` / `keyBindsXml` は 136 行付近、`GetKey*` は 316 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs`（`UpdateKeyInput` 内の 13 か所 = 115 / 119 / 123 / 127 / 131 / 135 / 143 / 151 / 162 / 166 / 170 / 174 / 178 行、`DrawStatusMessage` の 493 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:175`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:1132,1198`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/KeyBindConfigTests.cs`（新規）

**Interfaces:**
- Produces:
  - `COM3D2.SceneEditor.Plugin.KeyBindType` に 14 種を追加: `AddKeyFrame` / `AddKeyFrameAll` / `RemoveKeyFrame` / `Play` / `Copy` / `Paste` / `FlipPaste` / `PoseCopy` / `PosePaste` / `PrevFrame` / `NextFrame` / `PrevKeyFrame` / `NextKeyFrame` / `MultiSelect`
  - `bool Config.GetKeyDownRepeat(KeyBindType keyBindType)`
  - `bool Config.isTimelineKeyInputEnabled`（`[XmlIgnore]`、既定 true）
  - `MTEP.Config` から `KeyBindType` / `keyBinds` / `GetKey` / `GetKeyDown` / `GetKeyDownRepeat` / `GetKeyUp` / `GetKeyName` / `isKeyInputEnabled` / `disablePoseHistory` が**消える**
- Consumes: `KeyBind`（`MTEUtils`）の `GetKey()` / `GetKeyDown()` / `GetKeyDownRepeat(float, float)` / `GetKeyUp()` / `ToString()`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/KeyBindConfigTests.cs` を新規作成する。

```csharp
using System;
using System.IO;
using System.Xml.Serialization;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// キー割り当ては SceneEditor.xml (SE 本体の Config) が唯一の持ち主。
    /// 既定の取りこぼしは実行時の KeyNotFoundException になるためここで固定する
    /// </summary>
    public class KeyBindConfigTests
    {
        [Fact]
        public void 全てのKeyBindTypeに既定の割り当てがある()
        {
            var config = new Config();

            foreach (KeyBindType type in Enum.GetValues(typeof(KeyBindType)))
            {
                Assert.True(
                    config.keyBinds.ContainsKey(type),
                    "既定の割り当てがありません: " + type);
            }
        }

        [Fact]
        public void タイムライン操作のキーがSceneEditor側にある()
        {
            var config = new Config();

            Assert.Equal("Return", config.GetKeyName(KeyBindType.AddKeyFrame));
            Assert.Equal("Shift+Return", config.GetKeyName(KeyBindType.AddKeyFrameAll));
            Assert.Equal("Backspace", config.GetKeyName(KeyBindType.RemoveKeyFrame));
            Assert.Equal("Space", config.GetKeyName(KeyBindType.Play));
            Assert.Equal("Shift", config.GetKeyName(KeyBindType.MultiSelect));
        }

        [Fact]
        public void 旧TimelineXmlのkeyBind要素は読み飛ばして他の項目を復元する()
        {
            // クラスから削除済みの項目。既存ユーザーの Timeline.xml には残っている
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<keyBind><key>AddKeyFrame</key><value>Return</value></keyBind>"
                + "<keyBind><key>EditMode</key><value>F1</value></keyBind>"
                + "<disablePoseHistory>false</disablePoseHistory>"
                + "<voiceMaxLength>12.5</voiceMaxLength>"
                + "</Config>";

            var serializer = new XmlSerializer(typeof(MTEP.Config));
            using (var reader = new StringReader(xml))
            {
                var config = (MTEP.Config) serializer.Deserialize(reader);

                Assert.Equal(12.5f, config.voiceMaxLength);
            }
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter KeyBindConfigTests
```
Expected: FAIL（`KeyBindType.AddKeyFrame` が存在せずコンパイルエラー）

- [ ] **Step 3: SE 本体の KeyBindType へタイムライン系を足す**

`source/COM3D2.SceneEditor.Plugin/Config.cs` の `KeyBindType` を次へ置き換える。

```csharp
    public enum KeyBindType
    {
        PluginToggle,
        GizmoMove,
        GizmoRotate,
        GizmoScale,
        Undo,
        Redo,
        EditModeToggle,
        WindowsHiddenToggle,
        // ここから下はタイムライン操作 (旧 Timeline.xml から統合)
        AddKeyFrame,
        AddKeyFrameAll,
        RemoveKeyFrame,
        Play,
        Copy,
        Paste,
        FlipPaste,
        PoseCopy,
        PosePaste,
        PrevFrame,
        NextFrame,
        PrevKeyFrame,
        NextKeyFrame,
        MultiSelect,
    }
```

- [ ] **Step 4: 既定の割り当てを足す**

`Config.cs` の `keyBinds` 初期化（519 行付近、`{ KeyBindType.WindowsHiddenToggle, new KeyBind("Tab") },` の直後）へ次を足す。キーは旧 `Timeline.xml` の既定そのまま。

```csharp
            { KeyBindType.AddKeyFrame, new KeyBind("Return") },
            { KeyBindType.AddKeyFrameAll, new KeyBind("Shift+Return") },
            { KeyBindType.RemoveKeyFrame, new KeyBind("Backspace") },
            { KeyBindType.Play, new KeyBind("Space") },
            { KeyBindType.Copy, new KeyBind("Ctrl+C") },
            { KeyBindType.Paste, new KeyBind("Ctrl+V") },
            { KeyBindType.FlipPaste, new KeyBind("Ctrl+Shift+V") },
            { KeyBindType.PoseCopy, new KeyBind("Ctrl+Alt+C") },
            { KeyBindType.PosePaste, new KeyBind("Ctrl+Alt+V") },
            { KeyBindType.PrevFrame, new KeyBind("A") },
            { KeyBindType.NextFrame, new KeyBind("D") },
            { KeyBindType.PrevKeyFrame, new KeyBind("Ctrl+A") },
            { KeyBindType.NextKeyFrame, new KeyBind("Ctrl+D") },
            { KeyBindType.MultiSelect, new KeyBind("Shift") },
```

- [ ] **Step 5: GetKeyDownRepeat と入力抑止フラグを足す**

`Config.cs` の `GetKeyDown`（583 行付近）の直後へ次を足す。

```csharp
        public bool GetKeyDownRepeat(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyDownRepeat(keyRepeatTimeFirst, keyRepeatTime);
        }
```

同じクラスの `dirty` フィールド（577 行付近）の隣へ次を足す。本体側のキー（プラグイン表示切替・ギズモ・Undo）まで止めないよう、`GetKey*` の中では見ないこと。

```csharp
        /// <summary>連番画像出力中など、タイムライン操作のキーだけ止めたいときに false にする</summary>
        [XmlIgnore]
        public bool isTimelineKeyInputEnabled = true;
```

- [ ] **Step 6: テストを実行して通ることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter KeyBindConfigTests
```
Expected: 3 件とも PASS

- [ ] **Step 7: タイムライン設定からキー関連を削除する**

`source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` から次を削除する。

- `enum KeyBindType`（8〜31 行付近）全体
- `disablePoseHistory` フィールドと直上のコメント（83 行付近）。**これは項目 20 の決定（キー割り当ての統合）の範囲外の追加クリーンアップ**。「MTE 互換のため残す。SceneEditor では効果なし」と自ら書いてある死にフィールドで読み手はゼロだが、決定に含まれていないためコミットメッセージ本文にその旨を書き、実装後にユーザーへ一言報告する。外すよう言われたらこの行だけ戻す
- `keyBinds` の初期化（136〜160 行付近）
- `struct KeyBindPair` と `keyBindsXml` プロパティ（161〜194 行付近）
- `isKeyInputEnabled` フィールド（317〜318 行付近）
- `GetKey` / `GetKeyDown` / `GetKeyDownRepeat` / `GetKeyUp` / `GetKeyName`（319 行〜クラス末尾付近）

`System.Collections.Generic` や `KeyBind` の using が他で使われていなければ併せて整理する。

- [ ] **Step 8: 呼び出し側を SE 本体の Config へ向ける**

`TimelineControlWindow.cs` の `UpdateKeyInput` 内の `tc.GetKeyDown(MTEP.KeyBindType.X)` / `tc.GetKeyDownRepeat(...)` を、すべて `config.GetKeyDown(KeyBindType.X)` / `config.GetKeyDownRepeat(KeyBindType.X)` へ書き換える（13 か所 = 115 / 119 / 123 / 127 / 131 / 135 / 143 / 151 / 162 / 166 / 170 / 174 / 178 行。`DrawStatusMessage` の `GetKeyName` は別枠で下に書く）。`var tc = timelineConfig;` の行は削除する。冒頭の

```csharp
            if (GUIUtility.keyboardControl != 0)
```

の直前へ、旧 `isKeyInputEnabled` に相当する抑止を足す。

```csharp
            if (!config.isTimelineKeyInputEnabled)
            {
                return;
            }
```

`DrawStatusMessage`（493 行付近）の

```csharp
                var keyName = timelineConfig.GetKeyName(MTEP.KeyBindType.AddKeyFrame);
```

を次へ変える。

```csharp
                var keyName = config.GetKeyName(KeyBindType.AddKeyFrame);
```

`TimelineWindow.cs:175` を次へ変える。

```csharp
        private bool isMultiSelect =>
            config.isTimelineKeyInputEnabled && config.GetKey(KeyBindType.MultiSelect);
```

`TimelineWindow` に `config`（SE 本体の `Config`）のアクセサが無い場合は、既存の `timelineConfig` の隣へ足す。

```csharp
        private static Config config => ConfigManager.instance.config;
```

`Timeline/Manager/TimelineManager.cs` の 1132 行と 1198 行の `config.isKeyInputEnabled = false;` / `= true;` を、SE 本体の設定へ向ける。

```csharp
            SceneEditor.Plugin.ConfigManager.instance.config.isTimelineKeyInputEnabled = false;
```

```csharp
                SceneEditor.Plugin.ConfigManager.instance.config.isTimelineKeyInputEnabled = true;
```

- [ ] **Step 9: 残骸が無いことを確認する**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
grep -rn --include=*.cs 'MTEP.KeyBindType\|timelineConfig.GetKey\|isKeyInputEnabled\|disablePoseHistory' source/
```
Expected: 出力が空（テストコードの文字列リテラルを除く）

- [ ] **Step 10: ビルドとテストを実行する**

Global Constraints のコマンド 1 本を実行する。
Expected: 2 構成ともビルド成功、`dotnet test` 全 PASS

- [ ] **Step 11: 実機で確認する**

1. 既存の `Timeline.xml`（`keyBind` 要素入り）を置いたままゲームを起動し、エラーログが出ないこと。起動後に `Timeline.xml` から `keyBind` 要素が消えること
2. `Enter` でキーフレーム登録、`Space` で再生 / 停止、`A` / `D` でフレーム移動、`Shift` 押しながらのグリッド複数選択が効くこと
3. `SceneEditor.xml` の `keyBind` に `AddKeyFrame` などの要素が書き出され、値を書き換えて再起動すると反映されること
4. 連番画像出力の実行中に `Enter` や `A` / `D` が効かず、`Esc` で停止できること

- [ ] **Step 12: 仕様書を直す**

`docs-site/timeline/shortcuts.md` の「キーの変更」節を次へ置き換える。

```markdown
## キーの変更

タイムライン用のキーは `Sybaris\UnityInjector\Config\SceneEditor\SceneEditor.xml` の `keyBind` 要素で変更します。GUI はありません。本体側のキー（編集モード、Undo など）と同じファイルです（[設定リファレンス](/guide/configuration#キー設定)）。
```

同ファイル冒頭の 1 文（`タイムライン用のキーは ... 表示されている間だけ有効です`）は Task 3 で直すため、このタスクでは触らない。

`docs-site/` に `disablePoseHistory` や `Timeline.xml` のキー設定の記載が残っていないか確認し、残っていれば直す。

```bash
grep -rn 'disablePoseHistory\|Timeline.xml' docs-site/
```

- [ ] **Step 13: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Config.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs \
        source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs \
        source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/KeyBindConfigTests.cs \
        docs-site/timeline/
git commit -m "refactor(timeline): キー割り当てを SceneEditor.xml へ統合する

Timeline.xml のキー割り当てを廃止し SceneEditor.xml へ一本化する。
旧設定の keyBind 要素は読み飛ばすため、カスタムキーは既定へ戻る。
併せて読み手の無い disablePoseHistory を削除した (決定範囲外の追加クリーンアップ)。"
```

---

### Task 3: ショートカットをウィンドウ非表示でも効くようにする（項目 21）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineKeyInput.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="TimelineControlWindow.cs" />` の隣、374 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs`（`Update()` の 165 行付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs`（`Update` / `UpdateKeyInput` / `SeekFrameWithScroll` を削除）

**Interfaces:**
- Consumes: Task 2 で `Config` へ移した `GetKeyDown` / `GetKeyDownRepeat` / `isTimelineKeyInputEnabled`
- Produces: `static void TimelineKeyInput.Update()`

- [ ] **Step 1: キー処理を専用クラスへ移す**

`source/COM3D2.SceneEditor.Plugin/TimelineKeyInput.cs` を新規作成する。中身は `TimelineControlWindow.UpdateKeyInput` と `SeekFrameWithScroll` をそのまま移したもので、ウィンドウの表示判定だけが無い。

```csharp
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作のキー入力。
    /// ウィンドウの表示状態には依らず、プラグイン表示中は常に効く
    /// </summary>
    public static class TimelineKeyInput
    {
        private static Config config => ConfigManager.instance.config;
        private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;

        /// <summary>テキスト入力中は誤発動を防ぐため無視する</summary>
        public static void Update()
        {
            if (!config.isTimelineKeyInputEnabled)
            {
                return;
            }

            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (studioHack == null || maidManager.maid == null ||
                !timelineManager.IsValidData())
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.AddKeyFrame))
            {
                timelineManager.AddKeyFrameDiff();
            }
            if (config.GetKeyDown(KeyBindType.AddKeyFrameAll))
            {
                currentLayer.AddKeyFrameAll();
            }
            if (config.GetKeyDown(KeyBindType.RemoveKeyFrame))
            {
                timelineManager.RemoveSelectedFrame();
            }
            if (config.GetKeyDownRepeat(KeyBindType.PrevFrame))
            {
                SeekFrameWithScroll(timelineManager.currentFrameNo - 1);
            }
            if (config.GetKeyDownRepeat(KeyBindType.NextFrame))
            {
                SeekFrameWithScroll(timelineManager.currentFrameNo + 1);
            }
            if (config.GetKeyDownRepeat(KeyBindType.PrevKeyFrame))
            {
                var prevFrame = timelineManager.GetPrevFrame(timelineManager.currentFrameNo);
                if (prevFrame != null)
                {
                    SeekFrameWithScroll(prevFrame.frameNo);
                }
            }
            if (config.GetKeyDownRepeat(KeyBindType.NextKeyFrame))
            {
                var nextFrame = timelineManager.GetNextFrame(timelineManager.currentFrameNo);
                if (nextFrame != null)
                {
                    SeekFrameWithScroll(nextFrame.frameNo);
                }
            }
            if (config.GetKeyDown(KeyBindType.Play))
            {
                if (currentLayer.isAnmPlaying)
                {
                    timelineManager.Pause();
                }
                else
                {
                    timelineManager.Play();
                }
            }
            if (config.GetKeyDown(KeyBindType.Copy))
            {
                timelineManager.CopyFramesToClipboard();
            }
            if (config.GetKeyDown(KeyBindType.Paste))
            {
                timelineManager.PasteFramesFromClipboard(false);
            }
            if (config.GetKeyDown(KeyBindType.FlipPaste))
            {
                timelineManager.PasteFramesFromClipboard(true);
            }
            if (config.GetKeyDown(KeyBindType.PoseCopy))
            {
                timelineManager.CopyPoseToClipboard();
            }
            if (config.GetKeyDown(KeyBindType.PosePaste))
            {
                timelineManager.PastePoseFromClipboard();
            }
        }

        /// <summary>フレーム移動し、タイムラインの表示位置も追従させる</summary>
        private static void SeekFrameWithScroll(int frameNo)
        {
            timelineManager.SeekCurrentFrame(frameNo);
            TimelineWindow.instance.FixScrollPosition();
        }
    }
}
```

- [ ] **Step 2: csproj へファイルを登録する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="TimelineControlWindow.cs" />` の次の行へ足す。

```xml
    <Compile Include="TimelineKeyInput.cs" />
```

- [ ] **Step 3: ウィンドウ側のキー処理を消す**

`TimelineControlWindow.cs` から次を削除する。

- `public override void Update()`（83〜93 行付近）
- `private void UpdateKeyInput()` とその直上の `<summary>` コメント（95〜181 行付近）
- `private static void SeekFrameWithScroll(int frameNo)`（184〜189 行付近。`TimelineKeyInput` へ移した）

- [ ] **Step 4: プラグイン本体から呼ぶ**

`COM3D2.SceneEditor.Plugin.cs` の `Update()` にある `if (isEnable)` ブロックへ 1 行足す。

```csharp
                if (isEnable)
                {
                    UpdateGizmoToolKey();
                    UpdateHistoryKey();
                    UpdateEditModeKey();
                    UpdateWindowsHiddenKey();
                    TimelineKeyInput.Update();
                    managerRegistry.Update();
                }
```

- [ ] **Step 5: 残骸が無いことを確認する**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
grep -n 'UpdateKeyInput\|SeekFrameWithScroll' source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs
```
Expected: 出力が空

- [ ] **Step 6: ビルドとテストを実行する**

Global Constraints のコマンド 1 本を実行する。
Expected: 2 構成ともビルド成功、`dotnet test` 全 PASS

- [ ] **Step 7: 実機で確認する**

1. `タイムライン操作` ウィンドウを閉じた状態で `Enter` / `Space` / `A` / `D` / `Ctrl+C` / `Ctrl+V` が効くこと
2. テキスト入力欄にフォーカスがある間はキーが発動しないこと（アニメ名の欄に `a` や `d` を打てる）
3. タイムライン未読み込みの状態でキーを押しても例外が出ないこと（`tail_log` で確認）
4. `Tab` でウィンドウを一括非表示にした状態でもタイムラインのキーが効くこと

- [ ] **Step 8: 仕様書を直す**

`docs-site/timeline/shortcuts.md` の冒頭 1 文を置き換える。

```markdown
タイムライン用のキーは、プラグインを表示している間はウィンドウの表示状態に関わらず有効です。テキスト入力中と、連番画像出力の実行中は無効になります。
```

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineKeyInput.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs \
        source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs \
        docs-site/timeline/shortcuts.md
git commit -m "fix(timeline): ショートカットをウィンドウ非表示でも効くようにする"
```

---

### Task 4: 現状維持にした制限を仕様書へ明記する（項目 19 の残り・22〜25）

**Files:**
- Modify: `docs-site/timeline/compatibility.md`（「その他の制限」節、33 行付近）
- Modify: `docs/timeline-release-debt-review.md`（優先度 低の状態列）

**Interfaces:** なし（文書のみ）

**前提:** 22（グリッドのズームは設定ファイルのみ）・23（削除 / リネームが無い、既定名 `テスト`）・24（`個別設定を初期化` で `最終フレーム` が戻らない）・25（`移動範囲` / `ボイス最大秒数` が値域を決める）は `docs-site/timeline/settings.md` と `files.md` に記載済み。ここでは記載漏れの補完だけを行う。

- [ ] **Step 1: 記載済みの内容を確認する**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
grep -rn '最終フレーム` は戻りません\|frameWidth\|移動範囲\|ボイス最大秒数\|削除やリネームの専用機能' docs-site/timeline/
```
Expected: `settings.md`（`最終フレーム`・`frameWidth`・`移動範囲`・`ボイス最大秒数`）、`window.md`（`frameWidth`）、`files.md`（削除 / リネーム）に計 5〜6 件出る。件数そのものではなく、**4 項目（22 / 23 / 24 / 25）それぞれに 1 件以上の記載があるか**を見る。記載が無い項目があれば Step 2 の一覧へ足す

- [ ] **Step 2: 確認なしで実行される操作を明記する**

`docs-site/timeline/compatibility.md` の「その他の制限」節の末尾へ次を足す。

```markdown
- 次の操作は確認なしで実行されます: `新規作成`、タイムラインのロード（どちらも未保存の変更が失われます）、`タイムライン設定` → `トラック` のトラック削除、テンプレートとカテゴリの削除。`セーブ` の上書き、`アンロード`、`連番画像出力`、`個別設定を初期化` は確認が出ます
- タイムラインの削除・リネームの機能はありません。ファイルを直接操作してください（[ファイル操作](/timeline/files)）
```

- [ ] **Step 3: 仕様書のビルドを確認する**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/docs-site"
npm run docs:build
```
Expected: リンク切れなしでビルド成功

- [ ] **Step 4: 負債一覧の状態を更新する**

`docs/timeline-release-debt-review.md` の優先度 低の表で、19 を `対応済（セーブ上書き・連番画像出力のみ）`、20 を `対応済（SceneEditor.xml へ統合）`、21 を `対応済`、22〜25 を `現状維持（仕様書に明記）` へ書き換える。冒頭の現況サマリと「進め方」も、低の残件が無くなった旨へ直す。

- [ ] **Step 5: コミット**

```bash
git add docs-site/timeline/compatibility.md docs/timeline-release-debt-review.md
git commit -m "docs(timeline): 現状維持にした制限を明記し負債一覧を更新する"
```

---

## 完了条件

- [ ] 2 構成（COM3D2 / COM3D25）ともビルドが通る
- [ ] `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全件 PASS（`KeyBindConfigTests` の 3 件を含む）
- [ ] 実機確認（Task 1 / 2 / 3）が済んでいる
- [ ] `grep -rn --include=*.cs 'MTEP.KeyBindType\|isKeyInputEnabled\|disablePoseHistory' source/` が空
- [ ] `Timeline.xml` に `keyBind` 要素が書き出されず、`SceneEditor.xml` に 22 種のキーが並ぶ
- [ ] `docs-site` のビルドが通り、`shortcuts.md` に「ウィンドウが表示されている間だけ有効」が残っていない
- [ ] `docs/timeline-release-debt-review.md` の優先度 低が全件「対応済」または「現状維持（仕様書に明記）」になっている
- [ ] 実装後、**code-review** スキルでレビューを通してからユーザーへ提示する

## レビュー却下メモ（2026-09-14 plan-review）

- 「セーブ確認ダイアログの表示中に `IsValidData()` が false へ変わると、`SaveTimeline()` が早期 return した後も `TimelineLoadManager.Reload()` が走る」 — 却下。`Reload()` は一覧の再読込だけで副作用が無く、変更前の `OnSaveClicked` も同じ順序で呼んでいる。今回の変更で新しく生まれる問題ではない
- 「`disablePoseHistory` の削除はスコープクリープなので実装前にユーザーへ確認」 — 一部取り込み。削除自体は残し（読み手ゼロの死にフィールドで、リリース後は消しづらい）、決定範囲外である旨を Task 2 Step 7 とコミットメッセージに明記する形にした。実装後の報告でユーザーが不要と判断したらその行だけ戻す
