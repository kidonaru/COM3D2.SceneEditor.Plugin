# Phase W2: トラック設定 / タイムラインロード UI 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: 親ワークスペースの標準フローに従い superpowers:executing-plans でタスク単位に実装する（subagent-driven-development は使わない）。ステップはチェックボックス（`- [ ]`）で追跡する。

**Goal:** MTE の `TimelineTrackUI` / `TimelineLoadUI` 相当を SE へ移植し、トラックの追加・編集と、サムネイル付き一覧からのタイムラインロードを GUI で完結させる。

**Architecture:** トラック設定は Phase W1 で追加した `TimelineSettingWindow` の新規タブ「トラック」として実装する（データが `TimelineData.tracks` に属し、既存の設定タブと同じ編集対象のため）。ロード UI は表示面積が要るので新規 `TimelineLoadWindow`（`EditorSubWindow` 派生）とし、一覧の構築は SE の既存プリセット一覧（`ScenePresetManager` + `ScenePresetItem` + `PresetWindow` のタイルビュー）と同じ流儀で `TimelineLoadManager` + `TimelineLoadItem` を新設する。TimelineWindow のロード用コンボボックスは、タイル一覧へ置き換えず併存させる（キーボード操作なしで素早く選べる導線として価値があるため）。

**Tech Stack:** C# 7.3 / .NET 3.5 相当（UnityInjector プラグイン）、IMGUI（MTEUtils の `GUIView` / `ITileViewContent`）、xUnit（net48 テストプロジェクト）

**Spec:** `docs/superpowers/specs/timeline-editing-roadmap.md`（Phase W2）

## Global Constraints

- ブランチは `feature/timeline-window` のまま。worktree は使わない
- csproj は非 SDK 形式の手動管理。新規 `.cs` は必ず `<Compile Include="..." />` を追加する
- コメント・ラベル・エラーメッセージは日本語
- ビルドは MSBuild を直接叩く（`debug.bat` は実機へ DLL をコピーするため使わない）:
  `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- テストは `dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj`（先にプラグインを COM3D25 構成でビルドしておくこと）
- 既存の XML 形式（`TimelineXml` / `TrackXml`）は変更しない
- サムネイルテクスチャは `Texture2D`。一覧を作り直すときは古いテクスチャを破棄する（`ScenePresetManager.ClearThumbnails` と同じ扱い。放置するとリロードのたびに GPU メモリが積み上がる）

## 既存資産の確認結果（実装前の調査で確定済み）

| 必要なもの | SE での状態 |
|---|---|
| `TimelineManager.AddTrack` / `SetActiveTrack` / `RemoveTrack` / `MoveUpTrack` / `MoveDownTrack` | すべて実装済み（`Timeline/Manager/TimelineManager.cs:958-1010`） |
| `TrackData` / `TrackXml` / `TimelineData.tracks` / `activeTrackIndex` / `IsValidTrack` | 実装済み |
| `GUIView.DrawContentListView` / `DrawTileView` / `ITileViewContent` / `TileViewContentBase` | 実装済み（`MTEUtils/GUIView.cs`） |
| サムネ付き一覧の先例 | `Manager/ScenePresetManager.cs` + `PresetWindow.cs`（フォルダ移動・更新・削除・マウスオーバー名表示まで実装済み） |
| `MTEUtils.OpenDirectory` / `TextureUtils.LoadTexture` | 実装済み |
| `MTEP.PluginUtils.TimelineDirPath` / `ConvertThumPath` / `TimelineManager.LoadTimeline(name, directoryName)` | 実装済み |
| `TimelineLoadManager` / `TimelineLoadItem` | **未移植（本 Phase で新設）** |

## File Structure

| ファイル | 責務 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`（変更） | 「トラック」タブの追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/TimelineLoadManager.cs`（新規） | タイムライン XML の一覧ツリー構築・サムネ読み込み・表示中フォルダの保持 |
| `source/COM3D2.SceneEditor.Plugin/TimelineLoadWindow.cs`（新規） | タイル一覧のロード UI |
| `source/COM3D2.SceneEditor.Plugin/Config.cs`（変更） | `timelineLoad*` の配置・表示状態フィールド追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`（変更） | ウィンドウ登録 |
| `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`（変更） | Window メニューへの項目追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（変更） | ロード一覧ウィンドウを開くボタン追加、保存時の一覧更新 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（変更） | Compile Include 追加 |
| `docs/superpowers/specs/timeline-editing-roadmap.md`（変更） | Phase W2 の完了記録 |

---

### Task 1: トラック設定タブ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`

**Interfaces:**
- Consumes: `timeline.tracks`（`List<TrackData>`）、`timeline.activeTrack`、`timelineManager.AddTrack()` / `SetActiveTrack(TrackData, bool)` / `RemoveTrack(TrackData)` / `MoveUpTrack(TrackData)` / `MoveDownTrack(TrackData)` / `ApplyCurrentFrame(bool)`、`GUIView.DrawContentListView<T>`
- Produces: なし

- [ ] **Step 1: タブに「トラック」を足す**

`SettingTabType` を次のようにする:

```csharp
        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum SettingTabType
        {
            個別,
            共通,
            トラック,
        }
```

`DrawBody` の switch にも分岐を足す。ただし **トラックタブは共有スクロールビューの外で描く**。
`GUIView` はネストしたスクロールビューに対応しておらず（`GUIView.cs:300-304`。`scrollViewRect` が
インスタンスに 1 つしか無く、内側の `EndScrollView` が状態をリセットしてしまう）、
トラック一覧に使う `DrawContentListView` が内部で自前の `BeginScrollView` を張るため。
`PresetWindow` のタイル一覧も同じ理由で `DrawContent` の最上位から直接呼んでいる。

`DrawBody` の switch 前後を次のように書き換える:

```csharp
            // トラックタブの一覧 (DrawContentListView) は自前でスクロールするため、
            // 共有のスクロールビューには入れない (ネストしたスクロールは GUIView が非対応)
            if (_tabType == SettingTabType.トラック)
            {
                DrawTrackSetting(_view);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            switch (_tabType)
            {
                case SettingTabType.個別:
                    DrawSongSetting(_view);
                    break;
                case SettingTabType.共通:
                    DrawCommonSetting(_view);
                    break;
            }

            _view.EndScrollView();
```

- [ ] **Step 2: トラック一覧の描画を実装する**

`DrawCommonToggleSection` の後ろに追記する。1 行あたりの高さ 55px は MTE と同じで、名前行 + 範囲行 + 区切り線がちょうど収まる値:

```csharp
        /// <summary>1 トラックあたりの行の高さ (名前行 + 範囲行 + 区切り線)</summary>
        private static readonly int TRACK_ROW_HEIGHT = 55;

        /// <summary>トラック設定 (再生範囲の分割) の描画</summary>
        private void DrawTrackSetting(GUIView view)
        {
            if (view.DrawButton("追加", 80, ROW_HEIGHT))
            {
                timelineManager.AddTrack();
            }

            view.AddSpace(10);

            var tracks = timeline.tracks;
            if (tracks.Count == 0)
            {
                view.DrawLabel("トラックがありません", -1, ROW_HEIGHT);
                return;
            }

            // 行の内側で位置を決めるため、リスト側の余白は殺す
            view.padding = Vector2.zero;
            view.DrawContentListView(tracks, DrawTrack, -1, -1, TRACK_ROW_HEIGHT);
        }

        /// <summary>トラック 1 件分の行。有効化トグル・名前・範囲・並べ替え・削除</summary>
        private void DrawTrack(GUIView view, MTEP.TrackData track, int index)
        {
            if (track == null)
            {
                return;
            }

            var width = view.viewRect.width;

            view.currentPos.x = 5;
            view.currentPos.y = 5;

            view.BeginHorizontal();
            {
                var isActive = timeline.activeTrack == track;

                view.DrawToggle("", isActive, 20, ROW_HEIGHT, newValue =>
                {
                    timelineManager.SetActiveTrack(track, !isActive);
                });

                // 並べ替えボタン (右端 30px) に被らない幅で名前欄を取る
                view.DrawTextField(track.name, width - 30 - view.currentPos.x, ROW_HEIGHT, newText =>
                {
                    track.name = newText;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("範囲", 40, ROW_HEIGHT);

                var updated = false;
                updated |= view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = track.startFrameNo,
                    width = 50,
                    height = ROW_HEIGHT,
                    onChanged = x => track.startFrameNo = x,
                });

                view.DrawLabel("～", 15, ROW_HEIGHT);

                updated |= view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = track.endFrameNo,
                    width = 50,
                    height = ROW_HEIGHT,
                    onChanged = x => track.endFrameNo = x,
                });

                if (view.DrawButton("削除", 50, ROW_HEIGHT))
                {
                    timelineManager.RemoveTrack(track);
                }

                // 再生中のトラックの範囲を変えたときだけ、その場で再生位置へ反映する
                if (updated && track == timeline.activeTrack)
                {
                    timelineManager.ApplyCurrentFrame(true);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);

            view.BeginLayout(GUIView.LayoutDirection.Free);
            {
                view.currentPos.x = width - 30;
                view.currentPos.y = 5;

                if (view.DrawButton("∧", 20, ROW_HEIGHT))
                {
                    timelineManager.MoveUpTrack(track);
                }

                view.currentPos.y += 25;
                if (view.DrawButton("∨", 20, ROW_HEIGHT))
                {
                    timelineManager.MoveDownTrack(track);
                }
            }
            view.EndLayout();
        }
```

- [ ] **Step 3: `DrawContentListView` のシグネチャを実装に合わせて確認する**

Run:
```
sed -n '1860,1900p' source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs
```
Expected: `DrawContentListView<T>(List<T> items, Action<GUIView, T, int> drawContent, float width, float height, float rowHeight)` 相当。引数の順序・型が違えば呼び出し側を実装に合わせる。`BeginLayout(LayoutDirection.Free)` に `EndLayout()` が要るかも同ファイルで確認し、要らなければ Step 2 の末尾 `view.EndLayout();` を落とす（MTE 側は呼んでいない）。

- [ ] **Step 4: ビルドする**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(timeline): 設定ウィンドウにトラック設定タブを追加"
```

---

### Task 2: TimelineLoadManager（一覧ツリーとサムネ）

UI を持たない一覧構築側だけを先に作り、テストで固定する。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/TimelineLoadManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLoadManagerTests.cs`（新規）

**Interfaces:**
- Consumes: `MTEP.PluginUtils.TimelineDirPath`、`MTEP.PluginUtils.ConvertThumPath(string)`、`TextureUtils.LoadTexture(string)`、`TileViewContentBase`、`MTEUtils.LogException(Exception)`
- Produces:
  - `public class TimelineLoadItem : TileViewContentBase { public string path; }`
  - `public static class TimelineLoadManager` — `rootItem` / `currentDirItem`（get/set）/ `currentDirPath` / `GetOrLoadCurrentDirItem()` / `Reload()` / `GetRelativeDirectoryName(TimelineLoadItem item)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLoadManagerTests.cs` を新規作成。ファイル探索はゲーム非依存なので、相対ディレクトリ名の算出だけを対象にする（`Reload` は `TimelineDirPath` がゲーム環境に依存するためテストしない）:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ロード一覧のフォルダ名算出を固定する。
    /// LoadTimeline は「ディレクトリ名」と「ファイル名」を分けて受け取るため、
    /// ここがずれるとサブフォルダのタイムラインを開けなくなる
    /// </summary>
    public class TimelineLoadManagerTests
    {
        [Theory]
        // ルート直下はディレクトリ名なし
        [InlineData(@"C:\timeline", @"C:\timeline\sample.xml", "")]
        // サブフォルダは階層をそのまま返す
        [InlineData(@"C:\timeline", @"C:\timeline\sub\sample.xml", "sub")]
        [InlineData(@"C:\timeline", @"C:\timeline\sub\nest\sample.xml", @"sub\nest")]
        public void 相対ディレクトリ名を算出できる(string rootPath, string filePath, string expected)
        {
            var actual = TimelineLoadManager.GetRelativeDirectoryName(rootPath, filePath);
            Assert.Equal(expected, actual);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run:
```
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj --filter TimelineLoadManagerTests
```
Expected: コンパイルエラー（`TimelineLoadManager` が存在しない）

- [ ] **Step 3: TimelineLoadManager を実装する**

`source/COM3D2.SceneEditor.Plugin/Manager/TimelineLoadManager.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>タイル一覧の 1 項目。ファイルならタイムライン XML、フォルダなら下位階層</summary>
    public class TimelineLoadItem : TileViewContentBase
    {
        /// <summary>XML (ファイル) またはフォルダの絶対パス</summary>
        public string path;
    }

    /// <summary>
    /// タイムライン XML の一覧をタイルビュー用のツリーとして構築する。
    /// 一覧の作法は ScenePresetManager に合わせている (サムネは XML と同名の PNG)
    /// </summary>
    public static class TimelineLoadManager
    {
        public static string rootPath => MTEP.PluginUtils.TimelineDirPath;

        // ルートパスの解決が GameMain 経由になるため、静的初期化子では作らない。
        // 静的コンストラクタで例外が出ると TypeInitializationException がキャッシュされ、
        // そのゲームセッション中ずっとこのクラスが使えなくなる
        /// <summary>タイル一覧用のツリールート。初回の Reload で作られる</summary>
        public static TimelineLoadItem rootItem { get; private set; }

        /// <summary>表示中のフォルダ。UI のフォルダ移動で書き換える</summary>
        public static TimelineLoadItem currentDirItem { get; set; }

        public static string currentDirPath
            => currentDirItem != null ? currentDirItem.path : rootPath;

        private static bool _loaded = false;

        /// <summary>初回参照時だけ一覧を読み込む。以後は保存時と「更新」ボタンで作り直す</summary>
        public static TimelineLoadItem GetOrLoadCurrentDirItem()
        {
            if (!_loaded)
            {
                Reload();
            }
            return currentDirItem;
        }

        /// <summary>一覧を作り直す。GUI から呼ばれるため例外は握って空のまま返す</summary>
        public static void Reload()
        {
            _loaded = true;

            // 作り直しで表示中フォルダの実体が入れ替わるため、相対パスで控えて後から解決し直す
            // (初回は currentDirItem が無く、ルート相当の空文字になる)
            var currentRelativeDir = GetRelativeDirectoryPath(currentDirPath);

            ClearThumbnails(rootItem);
            rootItem = CreateRootItem();
            currentDirItem = rootItem;

            try
            {
                if (Directory.Exists(rootPath))
                {
                    var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        GetCanonicalPath(rootPath),
                    };
                    SearchItems(rootItem, visitedDirs);
                }

                currentDirItem = FindDirItem(rootItem, currentRelativeDir) ?? rootItem;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// タイムラインのロードに渡すディレクトリ名 (ルートからの相対パス、区切りは \)。
        /// ルート直下なら空文字
        /// </summary>
        public static string GetRelativeDirectoryName(TimelineLoadItem item)
        {
            return item == null ? "" : GetRelativeDirectoryName(rootPath, item.path);
        }

        /// <summary>ルートとファイルパスから、そのファイルが属するフォルダの相対パスを返す</summary>
        public static string GetRelativeDirectoryName(string rootPath, string filePath)
        {
            var dirPath = Path.GetDirectoryName(filePath) ?? "";
            return GetRelativePath(rootPath, dirPath);
        }

        private static string GetRelativeDirectoryPath(string dirPath)
        {
            return GetRelativePath(rootPath, dirPath);
        }

        /// <summary>rootPath から見た相対パス。範囲外・同一なら空文字</summary>
        private static string GetRelativePath(string rootPath, string path)
        {
            var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar);
            if (path.Length <= normalizedRoot.Length)
            {
                return "";
            }

            // 区切り文字まで含めて比較する。接頭辞が同じ兄弟フォルダ
            // (例: ルートが ...\Timeline のとき ...\TimelineBackup) を配下と誤判定しないため
            if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return path.Substring(normalizedRoot.Length)
                .Trim(Path.DirectorySeparatorChar);
        }

        private static TimelineLoadItem CreateRootItem()
        {
            return new TimelineLoadItem
            {
                name = "Timeline",
                path = rootPath,
                isDir = true,
                children = new List<ITileViewContent>(16),
            };
        }

        /// <summary>1 フォルダ分の読み込み。読めないフォルダがあっても一覧全体は諦めない</summary>
        private static void SearchItems(TimelineLoadItem dirItem, HashSet<string> visitedDirs)
        {
            try
            {
                SearchItemsCore(dirItem, visitedDirs);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("タイムラインフォルダを読み込めませんでした: {0}", dirItem.path);
                MTEUtils.LogException(e);
            }
        }

        /// <summary>ファイルを先、フォルダを後に並べる (ScenePresetManager と同じ構成)</summary>
        private static void SearchItemsCore(TimelineLoadItem dirItem, HashSet<string> visitedDirs)
        {
            var xmlPaths = Directory.GetFiles(dirItem.path, "*.xml")
                .OrderBy(path => path, new NaturalStringComparer());
            foreach (var xmlPath in xmlPaths)
            {
                var item = new TimelineLoadItem
                {
                    name = Path.GetFileNameWithoutExtension(xmlPath),
                    path = xmlPath,
                };

                var thumPath = MTEP.PluginUtils.ConvertThumPath(xmlPath);
                if (File.Exists(thumPath))
                {
                    item.thum = TextureUtils.LoadTexture(thumPath);
                }

                dirItem.AddChild(item);
            }

            var dirPaths = Directory.GetDirectories(dirItem.path)
                .OrderBy(path => path, new NaturalStringComparer());
            foreach (var dirPath in dirPaths)
            {
                var childDirItem = new TimelineLoadItem
                {
                    name = Path.GetFileName(dirPath),
                    path = dirPath,
                    isDir = true,
                    // 空フォルダでもタイルビューが children を走査するため必ず実体を持たせる
                    children = new List<ITileViewContent>(16),
                };
                dirItem.AddChild(childDirItem);

                // ジャンクション等が祖先を指していると無限再帰になり、
                // StackOverflowException は握れずゲームごと落ちるため訪問済みは辿らない
                if (visitedDirs.Add(GetCanonicalPath(dirPath)))
                {
                    SearchItems(childDirItem, visitedDirs);
                }
            }
        }

        /// <summary>循環検出用にフォルダパスを正規化する (大文字小文字は HashSet 側で無視する)</summary>
        private static string GetCanonicalPath(string dirPath)
        {
            return Path.GetFullPath(dirPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>ルートからの相対パスでフォルダ項目を探す。見つからなければ null</summary>
        private static TimelineLoadItem FindDirItem(TimelineLoadItem dirItem, string relativeDir)
        {
            if (string.IsNullOrEmpty(relativeDir))
            {
                return dirItem;
            }

            var current = dirItem;
            foreach (var name in relativeDir.Split(Path.DirectorySeparatorChar))
            {
                TimelineLoadItem next = null;
                foreach (var child in current.children)
                {
                    var childItem = child as TimelineLoadItem;
                    if (childItem != null && childItem.isDir &&
                        string.Equals(childItem.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        next = childItem;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }
                current = next;
            }

            return current;
        }

        /// <summary>作り直し前に古いサムネテクスチャを破棄する (放置すると GPU メモリが積み上がる)</summary>
        private static void ClearThumbnails(TimelineLoadItem dirItem)
        {
            if (dirItem?.children == null)
            {
                return;
            }

            foreach (var child in dirItem.children)
            {
                var childItem = child as TimelineLoadItem;
                if (childItem == null)
                {
                    continue;
                }

                childItem.thum = null;
                ClearThumbnails(childItem);
            }
        }
    }
}
```

- [ ] **Step 4: csproj に Compile Include を追加する**

csproj の Compile リストはアルファベット順ではないため、`Manager\` 配下のエントリが並ぶブロック
（`<Compile Include="Manager\...` が続く箇所）を実際に見て、その末尾付近へ追記する:

```xml
    <Compile Include="Manager\TimelineLoadManager.cs" />
```

- [ ] **Step 5: ビルドしてテストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj
```
Expected: ビルド成功、全テスト PASS。`TileViewContentBase.thum` の setter が `Object.Destroy` を呼ぶため、テストからは `thum` を触らないこと（Unity ランタイム外では例外になる）。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/TimelineLoadManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TimelineLoadManagerTests.cs
git commit -m "feat(timeline): タイムライン一覧のツリー構築を追加"
```

---

### Task 3: TimelineLoadWindow（タイル一覧の UI）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLoadWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 2 の `TimelineLoadManager` / `TimelineLoadItem`、`EditorSubWindow`、`GUIView.DrawTileView`、`MTEUtils.OpenDirectory(string)`、`timelineManager.LoadTimeline(string name, string directoryName)`、`timelineConfig.thumWidth` / `thumHeight`
- Produces: `TimelineLoadWindow.instance`、`TimelineLoadWindow.WINDOW_ID = 8903386`

- [ ] **Step 1: Config に配置フィールドを追加する**

`timelineSettingVisible` の直後に追記:

```csharp
        // タイムラインロードウィンドウ
        public int timelineLoadPosX = -1;
        public int timelineLoadPosY = -1;
        public int timelineLoadWidth = 480;
        public int timelineLoadHeight = 420;
        public bool timelineLoadVisible = false;
```

- [ ] **Step 2: ウィンドウを実装する**

`source/COM3D2.SceneEditor.Plugin/TimelineLoadWindow.cs` を新規作成:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 保存済みタイムラインのロード用ウィンドウ。
    /// サムネイル付きのタイル一覧からフォルダを辿って開く。
    /// TimelineWindow のロードコンボ (素早く選ぶ導線) とは併存させる
    /// </summary>
    public class TimelineLoadWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903386;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムラインロード";

        private static readonly int ROW_HEIGHT = 20;
        /// <summary>タイルの幅。高さはサムネの縦横比から決める</summary>
        private static readonly int TILE_WIDTH = 120;

        private readonly GUIView _view = new GUIView();

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static TimelineLoadWindow _instance = null;
        public static TimelineLoadWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineLoadWindow();
                }
                return _instance;
            }
        }

        private TimelineLoadWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineLoadPosX;
            y = config.timelineLoadPosY;
            width = config.timelineLoadWidth;
            height = config.timelineLoadHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineLoadPosX = x;
            config.timelineLoadPosY = y;
            config.timelineLoadWidth = width;
            config.timelineLoadHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineLoadVisible;
            set => config.timelineLoadVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            var currentDirItem = TimelineLoadManager.GetOrLoadCurrentDirItem();

            DrawHeader(currentDirItem);
            DrawTiles(currentDirItem);
        }

        /// <summary>階層移動・フォルダを開く・一覧の更新</summary>
        private void DrawHeader(TimelineLoadItem currentDirItem)
        {
            _view.BeginHorizontal();
            {
                var parent = currentDirItem.parent as TimelineLoadItem;
                if (_view.DrawButton("<", 20, ROW_HEIGHT, parent != null))
                {
                    TimelineLoadManager.currentDirItem = parent;
                }

                _view.DrawLabel(currentDirItem.name, -1, ROW_HEIGHT);

                // 右寄せ。ボタン 2 つ分 + 余白を確保する
                _view.currentPos.x = _view.viewRect.width - 110;

                if (_view.DrawButton("開く", 50, ROW_HEIGHT))
                {
                    MTEUtils.OpenDirectory(currentDirItem.path);
                }

                if (_view.DrawButton("更新", 50, ROW_HEIGHT))
                {
                    TimelineLoadManager.Reload();
                }
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);
        }

        /// <summary>タイル一覧。フォルダはクリックで移動、タイムラインはクリックでロードする</summary>
        private void DrawTiles(TimelineLoadItem currentDirItem)
        {
            if (currentDirItem.children == null || currentDirItem.children.Count == 0)
            {
                _view.DrawLabel("タイムラインがありません", -1, ROW_HEIGHT);
                return;
            }

            TimelineLoadItem selectedItem = null;
            TimelineLoadItem mouseOverItem = null;

            // 下部にマウスオーバー中の名前表示行を残し、残りをタイルへ充てる
            var tileViewHeight = _view.viewRect.height - _view.currentPos.y
                - ROW_HEIGHT - GUIView.defaultMargin;
            // 名前ラベルの分 (20px) をサムネの縦横比に足す
            var tileHeight = TILE_WIDTH * timelineConfig.thumHeight / timelineConfig.thumWidth + 20;

            _view.DrawTileView(
                currentDirItem,
                -1,
                tileViewHeight,
                TILE_WIDTH,
                tileHeight,
                item =>
                {
                    selectedItem = item as TimelineLoadItem;
                },
                item =>
                {
                    mouseOverItem = item as TimelineLoadItem;
                });

            OpenItem(selectedItem);

            _view.DrawBox(-1, ROW_HEIGHT);

            if (mouseOverItem != null)
            {
                _view.DrawLabel(mouseOverItem.name, -1, ROW_HEIGHT);
            }
        }

        /// <summary>フォルダなら移動、タイムラインならロードする</summary>
        private void OpenItem(TimelineLoadItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.isDir)
            {
                TimelineLoadManager.currentDirItem = item;
                return;
            }

            timelineManager.LoadTimeline(item.name, TimelineLoadManager.GetRelativeDirectoryName(item));
        }
    }
}
```

- [ ] **Step 3: ウィンドウを登録する**

`Manager/WindowManager.cs` の `AddWindow(TimelineSettingWindow.instance);` の直後:

```csharp
            AddWindow(TimelineLoadWindow.instance);
```

`MenuBarWindow.cs` の `CreateWindowItem("タイムライン設定", TimelineSettingWindow.instance),` の直後:

```csharp
                        CreateWindowItem("タイムラインロード", TimelineLoadWindow.instance),
```

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="TimelineSettingWindow.cs" />` の直前:

```xml
    <Compile Include="TimelineLoadWindow.cs" />
```

- [ ] **Step 4: ビルドする**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功。`GUIView.DrawTileView` の引数や `TileViewContentBase.parent` の型が異なる場合は `PresetWindow.DrawPresetTiles` の呼び出しに合わせる（そちらが動作実績のある正）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineLoadWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): サムネイル付きのタイムラインロードウィンドウを追加"
```

---

### Task 4: TimelineWindow からの導線と一覧の更新

保存直後に一覧へ出ないと「保存したのに無い」と見えるため、保存経路から一覧を作り直す。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`

**Interfaces:**
- Consumes: `WindowManager.ToggleWindowVisible(EditorSubWindow)`、`TimelineLoadManager.Reload()`
- Produces: なし

- [ ] **Step 1: 「一覧」ボタンを追加する**

`DrawControlPanel` の「設定」ボタンの直後に追記:

```csharp
                if (view.DrawButton("一覧", 50, 20))
                {
                    WindowManager.ToggleWindowVisible(TimelineLoadWindow.instance);
                }
```

- [ ] **Step 2: 保存後に一覧を作り直す**

同じメソッド内の「セーブ」ボタンの `timelineManager.SaveTimeline();` の直後に追記:

```csharp
                    // 保存したタイムラインをサムネ付きで一覧へ出す
                    TimelineLoadManager.Reload();
                    RefreshTimelineFileList();
```

- [ ] **Step 3: ビルドしてテストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj
```
Expected: ビルド成功、全テスト PASS

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): ロード一覧の導線と保存後の一覧更新を追加"
```

---

### Task 5: 実機確認とドキュメント更新

**Files:**
- Modify: `docs/superpowers/specs/timeline-editing-roadmap.md`

- [ ] **Step 1: 実機で確認する**

ゲームを再起動して以下を確認する（Phase W1 の未確認項目も併せて消化する）:

- トラックの追加・名前変更・範囲変更・並べ替え・削除ができ、アクティブトラックの範囲でループ再生される
- ロードウィンドウにサムネイル付きでタイムラインが並び、フォルダを辿れる
- タイルのクリックでタイムラインがロードされる（サブフォルダのものも開ける）
- 「開く」でエクスプローラが開き、「更新」で一覧が作り直される
- タイムラインを保存すると、一覧に即座に出る
- 一覧を何度も更新してもメモリ使用量が積み上がらない（`MTEUtils.showMemoryUsage` 相当の表示か、タスクマネージャで確認する）

- [ ] **Step 2: ロードマップを更新する**

`docs/superpowers/specs/timeline-editing-roadmap.md` の以下を書き換える:

1. 「1. 現状 → 未対応」表の TimelineLoad 行と Track 行を実装済みへ更新する
2. 「Phase W2」節に完了日と実装先（`TimelineSettingWindow` のトラックタブ / `TimelineLoadWindow` + `TimelineLoadManager`）、および「TimelineWindow のロードコンボは併存させた」判断を追記する
3. Step 1 で確認できなかった項目は「限定確認」として残す

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/timeline-editing-roadmap.md
git commit -m "docs(timeline): Phase W2 の完了を記録"
```

---

## 実装後の必須ステップ

- `code-review` スキルでコードレビューを行い、指摘を取り込んでからユーザーへ提示する

## レビュー却下メモ

（却下した指摘なし。plan-reviewer の指摘 6 件はすべて取り込んだ）
