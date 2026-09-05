# 動画の複数表示 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 動画ウィンドウで最大 4 本の動画を同時に読み込み・配置でき、タイムラインファイルとシーンプリセットに全本を保存・復元できるようにする。

**Architecture:** `VideoSettings` は 1 本分の DTO のまま、`TimelineData` / `MovieManager` がリストで持つ。`MoviePlayerImpl` は静的に `MovieManager.settings` を見る代わりに生成時に自分の `VideoSettings` を注入される。UI は TextWindow と同じ「数の増減行 + 操作対象コンボ」方式で、選択中の 1 本だけを既存 UI で編集する。

**Tech Stack:** C# (Unity 5.6 IMGUI / AVProVideo), XmlSerializer, xUnit (`dotnet test`), MSBuild 2 構成 (COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1)

**Spec:** `docs/superpowers/specs/2026-09-05-multiple-videos-design.md`

## Global Constraints

- 最大本数 4、最小 1（`MovieManager.MinVideoCount = 1` / `MaxVideoCount = 4`）
- `TimelineData.CurrentVersion` 32 → 33、`ScenePresetData.CurrentVersion` 31 → 32
- 旧タイムラインファイルの `Video*` フラット要素は読込時のみ 1 本目として取り込む。書き出しは `<Video>` リストのみ
- 旧プリセットの単体 `<video>` 要素は要素名を変えないことで自動的に 1 件リストとして読む
- コメント・ログは日本語。ハードコーディングは避ける
- .NET 3.5 制約: 入力 5 個以上の `Func<>` / `Action<>` は使わない
- ビルドは COM3D2 / COM3D25 の両構成を MSBuild 直接実行で確認する（`debug.bat` は実機へコピーするため使わない）:
  ```
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" /v:m
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m
  ```
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- git worktree は使わない。コミットは commit スキルの規約（Conventional Commits 日本語）

---

### Task 1: VideoSettingsXml と TimelineXml の動画リスト

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`videoEnabled` 〜 `videoFrontmostAlpha` 付近、`Initialize()` の `version < 4` ブロック）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`

**Interfaces:**
- Produces: `class VideoSettingsXml`（18 項目、`[XmlElement]` 名は `Video` 接頭辞なし）、`TimelineXml.videos : List<VideoSettingsXml>`（`[XmlElement("Video")]`）、`VideoSettings.ReadFrom(VideoSettingsXml)`、`VideoSettings.ToXml() : VideoSettingsXml`。`TimelineXml.Initialize()` は `videos` が空ならフラット項目から 1 件作る

- [ ] **Step 1: 失敗するテストを書く**

`BgmVideoSettingsXmlTests.cs` の既存 `VideoSettings_TimelineXmlとの往復で値が保持される` を `VideoSettingsXml` 経由に書き換え、旧形式取り込みのテストを追加する:

```csharp
[Fact]
public void VideoSettings_VideoSettingsXmlとの往復で値が保持される()
{
    var src = new VideoSettings
    {
        enabled = false,
        displayType = VideoDisplayType.Frontmost,
        path = @"C:\movie\test.mp4",
        position = new Vector3(1f, 2f, 3f),
        rotation = new Vector3(10f, 20f, 30f),
        scale = 2.5f,
        startTime = 1.25f,
        volume = 0.3f,
        alpha = 0.9f,
        guiPosition = new Vector2(0.1f, 0.2f),
        guiScale = 0.8f,
        guiAlpha = 0.7f,
        backmostPosition = new Vector2(0.3f, 0.4f),
        backmostScale = 1.5f,
        backmostAlpha = 0.6f,
        frontmostPosition = new Vector2(-0.5f, 0.5f),
        frontmostScale = 0.4f,
        frontmostAlpha = 0.2f,
    };

    var xml = src.ToXml();
    var dst = new VideoSettings();
    dst.ReadFrom(xml);

    Assert.False(dst.enabled);
    Assert.Equal(VideoDisplayType.Frontmost, dst.displayType);
    Assert.Equal(@"C:\movie\test.mp4", dst.path);
    Assert.Equal(new Vector3(1f, 2f, 3f), dst.position);
    Assert.Equal(new Vector3(10f, 20f, 30f), dst.rotation);
    Assert.Equal(2.5f, dst.scale);
    Assert.Equal(1.25f, dst.startTime);
    Assert.Equal(0.3f, dst.volume);
    Assert.Equal(0.9f, dst.alpha);
    Assert.Equal(new Vector2(0.1f, 0.2f), dst.guiPosition);
    Assert.Equal(0.8f, dst.guiScale);
    Assert.Equal(0.7f, dst.guiAlpha);
    Assert.Equal(new Vector2(0.3f, 0.4f), dst.backmostPosition);
    Assert.Equal(1.5f, dst.backmostScale);
    Assert.Equal(0.6f, dst.backmostAlpha);
    Assert.Equal(new Vector2(-0.5f, 0.5f), dst.frontmostPosition);
    Assert.Equal(0.4f, dst.frontmostScale);
    Assert.Equal(0.2f, dst.frontmostAlpha);
}

[Fact]
public void TimelineXml_旧形式のフラット動画項目はInitializeで1件目として取り込まれる()
{
    var xml = new TimelineXml
    {
        version = 10,
        videoEnabled = false,
        videoDisplayType = VideoDisplayType.Backmost,
        videoPath = @"C:\movie\legacy.mp4",
        videoStartTime = 2f,
        videoBackmostAlpha = 0.3f,
    };

    xml.Initialize();

    Assert.Single(xml.videos);
    Assert.False(xml.videos[0].enabled);
    Assert.Equal(VideoDisplayType.Backmost, xml.videos[0].displayType);
    Assert.Equal(@"C:\movie\legacy.mp4", xml.videos[0].path);
    Assert.Equal(2f, xml.videos[0].startTime);
    Assert.Equal(0.3f, xml.videos[0].backmostAlpha);
}

[Fact]
public void TimelineXml_version4未満はDisplayOnGUIと開始オフセットを変換してから取り込む()
{
    var xml = new TimelineXml
    {
        version = 3,
        videoDisplayOnGUI = false,
        videoStartTime = 5f,
        startOffsetTime = 1f,
    };

    xml.Initialize();

    Assert.Single(xml.videos);
    Assert.Equal(VideoDisplayType.Mesh, xml.videos[0].displayType);
    Assert.Equal(4f, xml.videos[0].startTime);
}

[Fact]
public void TimelineXml_動画リストが既にあればフラット項目は取り込まない()
{
    var xml = new TimelineXml { version = 33, videoPath = @"C:\movie\ignored.mp4" };
    xml.videos.Add(new VideoSettingsXml { path = @"C:\movie\a.mp4" });
    xml.videos.Add(new VideoSettingsXml { path = @"C:\movie\b.mp4" });

    xml.Initialize();

    Assert.Equal(2, xml.videos.Count);
    Assert.Equal(@"C:\movie\a.mp4", xml.videos[0].path);
}
```

注: `TimelineXml.Initialize()` は `layers` / `models` 等も触るため、テストで `new TimelineXml()` の既定値で呼んで例外が出ないことを実行して確かめる。出るなら該当箇所の null ガードを追加する。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー（`VideoSettingsXml` / `ToXml` / `videos` が存在しない）

- [ ] **Step 3: VideoSettingsXml を追加し、TimelineXml にリストと取り込みを入れる**

`TimelineXml.cs` の `TimelinePsylliumXml` の直後（`TimelineXml` クラスの前）に追加:

```csharp
/// <summary>
/// 動画 1 本分の保存形式。
/// 旧形式は TimelineXml 直下に Video 接頭辞付きで平置きされていたため、こちらは接頭辞なし
/// </summary>
public class VideoSettingsXml
{
    [XmlElement("Enabled")]
    public bool enabled = true;
    [XmlElement("DisplayType")]
    public VideoDisplayType displayType = VideoDisplayType.GUI;
    [XmlElement("Path")]
    public string path = "";
    [XmlElement("Position")]
    public Vector3 position = new Vector3(0, 0, 0);
    [XmlElement("Rotation")]
    public Vector3 rotation = new Vector3(0, 0, 0);
    [XmlElement("Scale")]
    public float scale = 1f;
    [XmlElement("StartTime")]
    public float startTime = 0f;
    [XmlElement("Volume")]
    public float volume = 0.5f;
    [XmlElement("Alpha")]
    public float alpha = 1f;
    [XmlElement("GUIPosition")]
    public Vector2 guiPosition = new Vector2(0, 0);
    [XmlElement("GUIScale")]
    public float guiScale = 1f;
    [XmlElement("GUIAlpha")]
    public float guiAlpha = 1f;
    [XmlElement("BackmostPosition")]
    public Vector2 backmostPosition = new Vector2(0, 0);
    [XmlElement("BackmostScale")]
    public float backmostScale = 1f;
    [XmlElement("BackmostAlpha")]
    public float backmostAlpha = 0.5f;
    [XmlElement("FrontmostPosition")]
    public Vector2 frontmostPosition = new Vector2(-0.8f, 0.8f);
    [XmlElement("FrontmostScale")]
    public float frontmostScale = 0.38f;
    [XmlElement("FrontmostAlpha")]
    public float frontmostAlpha = 1f;
}
```

`TimelineXml` の `videoFrontmostAlpha` の直後に追加:

```csharp
/// <summary>動画設定 (v33 以降)。1 本目も含めて全本をここへ保存する</summary>
[XmlElement("Video")]
public List<VideoSettingsXml> videos = new List<VideoSettingsXml>();
```

既存の `videoEnabled` 〜 `videoFrontmostAlpha` の直前にコメントを追加:

```csharp
// 以下の Video* 平置き項目は v32 以前の読込互換用。書き出しは videos リストで行い、こちらには値を入れない
```

`Initialize()` の `if (version < 4) { ... }` ブロックの直後に追加:

```csharp
// v32 以前は動画 1 本を平置き項目で保存していたため、リストが空ならそこから 1 本目を起こす
if (videos.Count == 0)
{
    videos.Add(new VideoSettingsXml
    {
        enabled = videoEnabled,
        displayType = videoDisplayType,
        path = videoPath,
        position = videoPosition,
        rotation = videoRotation,
        scale = videoScale,
        startTime = videoStartTime,
        volume = videoVolume,
        alpha = videoAlpha,
        guiPosition = videoGUIPosition,
        guiScale = videoGUIScale,
        guiAlpha = videoGUIAlpha,
        backmostPosition = videoBackmostPosition,
        backmostScale = videoBackmostScale,
        backmostAlpha = videoBackmostAlpha,
        frontmostPosition = videoFrontmostPosition,
        frontmostScale = videoFrontmostScale,
        frontmostAlpha = videoFrontmostAlpha,
    });
}
```

- [ ] **Step 4: VideoSettings の変換を VideoSettingsXml 向けに置き換える**

`VideoSettings.cs` の `ReadFrom(TimelineXml)` / `WriteTo(TimelineXml)` を削除し、以下に置き換える:

```csharp
public void ReadFrom(VideoSettingsXml xml)
{
    enabled = xml.enabled;
    displayType = xml.displayType;
    path = xml.path;
    position = xml.position;
    rotation = xml.rotation;
    scale = xml.scale;
    startTime = xml.startTime;
    volume = xml.volume;
    alpha = xml.alpha;
    guiPosition = xml.guiPosition;
    guiScale = xml.guiScale;
    guiAlpha = xml.guiAlpha;
    backmostPosition = xml.backmostPosition;
    backmostScale = xml.backmostScale;
    backmostAlpha = xml.backmostAlpha;
    frontmostPosition = xml.frontmostPosition;
    frontmostScale = xml.frontmostScale;
    frontmostAlpha = xml.frontmostAlpha;
}

public VideoSettingsXml ToXml()
{
    return new VideoSettingsXml
    {
        enabled = enabled,
        displayType = displayType,
        path = path,
        position = position,
        rotation = rotation,
        scale = scale,
        startTime = startTime,
        volume = volume,
        alpha = alpha,
        guiPosition = guiPosition,
        guiScale = guiScale,
        guiAlpha = guiAlpha,
        backmostPosition = backmostPosition,
        backmostScale = backmostScale,
        backmostAlpha = backmostAlpha,
        frontmostPosition = frontmostPosition,
        frontmostScale = frontmostScale,
        frontmostAlpha = frontmostAlpha,
    };
}
```

クラスコメントの `// TimelineXml の要素名 (video 接頭辞) は互換維持のため変えない` は削除する。

この時点で `TimelineData.cs` の `video.ReadFrom(xml)` / `video.WriteTo(xml)` はコンパイルエラーになる。Task 2 で直すので、Task 1 のテスト実行のために暫定で以下に置き換える（Task 2 で再度書き換える）:

```csharp
video.ReadFrom(xml.videos[0]);
```
```csharp
xml.videos.Add(video.ToXml());
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS（`XmlRoundTripTests` / `MteCompatibilityTests` も含む。フィクスチャの往復でフラット `Video*` 要素が既定値で出力されることによる差分が出た場合は、その比較が要素の有無まで見ているかを確認し、見ているなら該当フィクスチャの期待値を更新する）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs
git commit -m "feat(video): タイムライン XML の動画設定をリスト形式にし旧形式を読込時に取り込む"
```

---

### Task 2: TimelineData.videos と version 33

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`CurrentVersion`、`public VideoSettings video`、`FromXml` の `video.ReadFrom`、`ToXml` の `video.WriteTo`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`

**Interfaces:**
- Consumes: Task 1 の `VideoSettingsXml` / `TimelineXml.videos` / `VideoSettings.ReadFrom(VideoSettingsXml)` / `ToXml()`
- Produces: `TimelineData.videos : List<VideoSettings>`（常に 1 件以上、最大 `MovieManager.MaxVideoCount`）、`TimelineData.CurrentVersion = 33`、`MovieManager.MinVideoCount` / `MaxVideoCount` 定数（本タスクで定数だけ先に追加する）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact]
public void TimelineData_動画リストはFromXmlとToXmlで件数と順序が保持される()
{
    var xml = new TimelineXml { version = TimelineData.CurrentVersion };
    xml.videos.Add(new VideoSettingsXml { path = @"C:\movie\a.mp4", displayType = VideoDisplayType.GUI });
    xml.videos.Add(new VideoSettingsXml { path = @"C:\movie\b.mp4", displayType = VideoDisplayType.Backmost });
    xml.videos.Add(new VideoSettingsXml { path = @"C:\movie\c.mp4", displayType = VideoDisplayType.Mesh });

    var data = new TimelineData();
    data.FromXml(xml);

    Assert.Equal(3, data.videos.Count);
    Assert.Equal(@"C:\movie\b.mp4", data.videos[1].path);
    Assert.Equal(VideoDisplayType.Backmost, data.videos[1].displayType);

    var outXml = data.ToXml();
    Assert.Equal(3, outXml.videos.Count);
    Assert.Equal(@"C:\movie\c.mp4", outXml.videos[2].path);
    Assert.Equal(VideoDisplayType.Mesh, outXml.videos[2].displayType);
}

[Fact]
public void TimelineData_動画リストが空なら既定値1件になる()
{
    var data = new TimelineData();
    data.FromXml(new TimelineXml { version = TimelineData.CurrentVersion });

    Assert.Single(data.videos);
    Assert.Equal("", data.videos[0].path);
}

[Fact]
public void TimelineData_動画リストは最大本数で切り詰められる()
{
    var xml = new TimelineXml { version = TimelineData.CurrentVersion };
    for (var i = 0; i < MovieManager.MaxVideoCount + 2; i++)
    {
        xml.videos.Add(new VideoSettingsXml { path = "v" + i });
    }

    var data = new TimelineData();
    data.FromXml(xml);

    Assert.Equal(MovieManager.MaxVideoCount, data.videos.Count);
}
```

注: `TimelineData.FromXml` がレイヤー生成等で Unity 依存の処理を呼び例外になる場合は、既存の `XmlRoundTripTests` がどう `FromXml` を呼んでいるかに倣う（テストプロジェクトはゲーム DLL 参照でコンパイルしているため通常は動く）。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー（`data.videos` / `MovieManager.MaxVideoCount` が存在しない）

- [ ] **Step 3: 実装**

`MovieManager.cs` のクラス先頭に定数を追加（残りの改修は Task 3）:

```csharp
public const int MinVideoCount = 1;
public const int MaxVideoCount = 4;
```

`TimelineData.cs`:

```csharp
public static readonly int CurrentVersion = 33;
```

`public VideoSettings video = new VideoSettings();` を置き換え:

```csharp
/// <summary>動画設定。常に 1 件以上、最大 MovieManager.MaxVideoCount 件</summary>
public List<VideoSettings> videos = new List<VideoSettings> { new VideoSettings() };
```

`FromXml` の暫定行 `video.ReadFrom(xml.videos[0]);` を置き換え:

```csharp
videos.Clear();
foreach (var videoXml in xml.videos)
{
    if (videos.Count >= MovieManager.MaxVideoCount)
    {
        break;
    }
    var video = new VideoSettings();
    video.ReadFrom(videoXml);
    videos.Add(video);
}
// 動画ウィンドウは常に 1 本目を編集対象にするため、空にはしない
if (videos.Count == 0)
{
    videos.Add(new VideoSettings());
}
```

`ToXml` の暫定行 `xml.videos.Add(video.ToXml());` を置き換え:

```csharp
foreach (var video in videos)
{
    xml.videos.Add(video.ToXml());
}
```

`TimelineData.cs` 内で他に `video.` を参照している箇所があれば `videos[0].` にする（`grep -n '\bvideo\.' TimelineData.cs` で確認）。

注: 旧形式の取り込みは `TimelineXml.Initialize()` が担う。`TimelineManager.LoadTimeline` は `Initialize()` → `FromXml` の順で呼ぶが、`TimelineManager.UpdateTimeline(TimelineXml)`（undo/redo 経路）は `Initialize()` を呼ばない。渡されるのは `ToXml()` 由来のスナップショットで常に `videos` が入っているため現状は問題ないが、将来 `UpdateTimeline` に外部 XML を渡す経路を足すときは `Initialize()` 済みであることが前提になる。この前提を `TimelineXml.videos` のコメントに足す:

```csharp
/// <summary>
/// 動画設定 (v33 以降)。1 本目も含めて全本をここへ保存する。
/// 旧形式からの取り込みは Initialize() が行うため、FromXml へ渡す前に Initialize() を通すこと
/// </summary>
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs
git commit -m "feat(timeline): TimelineData の動画設定を複数本のリストにする (v33)"
```

---

### Task 3: MovieManager の複数プレイヤー化と MoviePlayerImpl への設定注入

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs`（全面）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`（`video` プロパティ、`Awake`）

**Interfaces:**
- Consumes: `TimelineData.videos`
- Produces（Task 4 / 5 が使う）:
  - `MovieManager.settingsList : List<VideoSettings>`
  - `MovieManager.videoCount : int`（get/set、set で Min/Max に丸めて伸縮）
  - `MovieManager.IsValidIndex(int) : bool`、`GetSettings(int) : VideoSettings`
  - `MovieManager.IsValidPath(int)`, `IsEnabled(int)`
  - `GetCurrentTime(int)`, `GetDuration(int)`, `GetFrameRate(int)`, `GetTexture(int) : Texture`, `RequiresVerticalFlip(int)`
  - `LoadMovie()`, `LoadMovie(int)`, `UnloadMovie()`, `UnloadMovie(int)`, `ReloadMovie()`, `ReloadMovie(int)`, `UpdateTransform()`, `UpdateTransform(int)`, `UpdateVolume()`, `UpdateVolume(int)`, `UpdateSpeed()`, `UpdateSpeed(int)`, `UpdateSeekTime()`, `UpdateSeekTime(int)`, `UpdateColor()`, `UpdateColor(int)`, `UpdateMesh()`, `UpdateMesh(int)`, `UpdateShader()`, `UpdateShader(int)`
  - `MoviePlayerImpl.Setup(VideoSettings)`

この時点では UI (`VideoWindow` / `VideoPreviewWindow`) と `MteEffectsSnapshot` が旧 API を参照してコンパイルエラーになる。Task 3 では `MovieManager` / `MoviePlayerImpl` だけ書き換え、ビルドは Task 5 完了後に確認する（Task 3〜5 は同一コミットにせず、各タスク末尾でコミットだけ行う）。

- [ ] **Step 1: MoviePlayerImpl に設定注入を入れる**

`private static VideoSettings video => MovieManager.instance.settings;` を置き換え:

```csharp
/// <summary>この面が表示する動画の設定。MovieManager が生成直後に Setup で注入する</summary>
private VideoSettings _video;
private VideoSettings video => _video;
```

`public void Awake()` を `public void Setup(VideoSettings video)` に改名し、先頭に `_video = video;` を入れる。`AddComponent` は `Awake` を同期呼び出しするため、設定を読む初期化を `Awake` に置くと注入前に走ってしまう。メソッド本体（`_mediaPlayer` 生成 〜 `CreateGridMaterial()`）はそのまま `Setup` に残す:

```csharp
/// <summary>
/// 設定を注入して初期化する。AddComponent 直後に MovieManager が呼ぶ。
/// Awake では設定がまだ無いため、表示形式に依存する生成はここで行う
/// </summary>
public void Setup(VideoSettings video)
{
    _video = video;

    _mediaPlayer = gameObject.AddComponent<MediaPlayer>();
    _mediaPlayer.Events.AddListener(OnVideoEvent);
    // ... 以降は旧 Awake の本体そのまま
}
```

`Update` / `LateUpdate` / `OnGUI` 等が `_video == null`（`Setup` 前）で走る可能性は、`AddComponent` と `Setup` が同フレーム内の同期呼び出しであれば無い。念のため `Update` と `LateUpdate` の先頭に `if (_video == null) return;` を入れる。

- [ ] **Step 2: MovieManager を書き換える**

`MovieManager.cs` を以下の内容にする（定数は Task 2 で追加済み）:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // VideoDisplayType enum は Timeline/VideoDisplayType.cs に分離定義している

    public class MovieManager : ManagerBase
    {
        public const int MinVideoCount = 1;
        public const int MaxVideoCount = 4;

        /// <summary>index ごとのプレイヤー。未読込は null。settingsList と同じ長さに保つ</summary>
        private readonly List<MoviePlayerImpl> _players = new List<MoviePlayerImpl>();
        private readonly List<string> _loadedVideoPaths = new List<string>();
        private readonly List<VideoDisplayType> _loadedDisplayTypes = new List<VideoDisplayType>();

        private static MovieManager _instance;
        public static MovieManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MovieManager();
                }
                return _instance;
            }
        }

        /// <summary>タイムライン未読込時に使う設定。読込中は timeline 側が正</summary>
        private readonly List<VideoSettings> _standaloneSettingsList = new List<VideoSettings> { new VideoSettings() };

        /// <summary>
        /// 動画設定の一覧。タイムライン読込中は timeline 側 (TimelineXml に保存される)、
        /// 未読込時はマネージャ保持の standalone 値 (TimelineTextManager.textCount と同じ方式)
        /// </summary>
        public List<VideoSettings> settingsList => timeline != null ? timeline.videos : _standaloneSettingsList;

        public int videoCount
        {
            get => settingsList.Count;
            set
            {
                var count = Mathf.Clamp(value, MinVideoCount, MaxVideoCount);
                var list = settingsList;
                // 減らす分はプレイヤーを先に破棄する (設定を消してから Unload すると添字がずれる)
                while (list.Count > count)
                {
                    UnloadMovie(list.Count - 1);
                    list.RemoveAt(list.Count - 1);
                }
                // 増やした分はパス未設定の既定値。ユーザーがパスを選んだ時点で読み込む
                while (list.Count < count)
                {
                    list.Add(new VideoSettings());
                }
                SyncPlayerListLength();
            }
        }

        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < settingsList.Count;
        }

        public VideoSettings GetSettings(int index)
        {
            return settingsList[index];
        }

        public bool IsValidPath(int index)
        {
            var path = GetSettings(index).path;
            return path.Length > 0 && System.IO.File.Exists(path);
        }

        public bool IsEnabled(int index)
        {
            return IsValidPath(index) && GetSettings(index).enabled;
        }

        private MoviePlayerImpl GetPlayer(int index)
        {
            SyncPlayerListLength();
            return IsValidIndex(index) ? _players[index] : null;
        }

        public float GetCurrentTime(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.currentTime : 0f;
        }

        public float GetDuration(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.duration : 0f;
        }

        public float GetFrameRate(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.frameRate : 0f;
        }

        /// <summary>プレビュー表示用の動画テクスチャ。未読込時は null</summary>
        public Texture GetTexture(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.texture : null;
        }

        public bool RequiresVerticalFlip(int index)
        {
            var player = GetPlayer(index);
            return player != null && player.requiresVerticalFlip;
        }

        private MovieManager()
        {
        }

        public override void Init()
        {
            TimelineManager.onStop += UpdateSeekTime;
            TimelineManager.onAnmSpeedChanged += UpdateSpeed;
            TimelineManager.onSeekCurrentFrame += UpdateSeekTime;
            TimelineManager.onClearTimeline += OnClearTimeline;
        }

        /// <summary>
        /// タイムライン破棄時に timeline 側の値を引き継ぐ。
        /// 引き継がないと MoviePlayerImpl は残ったまま表示だけ既定値へ戻り、
        /// 次のスライダー操作で配置が唐突にリセットされる
        /// </summary>
        private void OnClearTimeline()
        {
            _standaloneSettingsList.Clear();
            foreach (var src in timeline.videos)
            {
                var copy = new VideoSettings();
                copy.CopyFrom(src);
                _standaloneSettingsList.Add(copy);
            }
            if (_standaloneSettingsList.Count == 0)
            {
                _standaloneSettingsList.Add(new VideoSettings());
            }
        }

        /// <summary>
        /// プレイヤー側リストを settingsList の長さに合わせる。
        /// タイムライン読込で settingsList の実体が差し替わっても添字対応を保つため、
        /// 各操作の入口で呼ぶ。余った末尾のプレイヤーは破棄する
        /// </summary>
        private void SyncPlayerListLength()
        {
            var count = settingsList.Count;
            while (_players.Count > count)
            {
                var last = _players.Count - 1;
                DestroyPlayer(last);
                _players.RemoveAt(last);
                _loadedVideoPaths.RemoveAt(last);
                _loadedDisplayTypes.RemoveAt(last);
            }
            while (_players.Count < count)
            {
                _players.Add(null);
                _loadedVideoPaths.Add("");
                _loadedDisplayTypes.Add(VideoDisplayType.GUI);
            }
        }

        private void DestroyPlayer(int index)
        {
            if (_players[index] != null)
            {
                Object.Destroy(_players[index].gameObject);
                _players[index] = null;
            }
            _loadedVideoPaths[index] = "";
        }

        private void SetupImpl(int index)
        {
            var settings = GetSettings(index);
            if (_loadedDisplayTypes[index] != settings.displayType)
            {
                UnloadMovie(index);
                _loadedDisplayTypes[index] = settings.displayType;
            }

            if (!IsEnabled(index))
            {
                return;
            }

            if (_players[index] == null)
            {
                var guid = System.Guid.NewGuid().ToString();
                var gameObject = new GameObject("MoviePlayer_" + index + "_" + guid);
                var player = gameObject.AddComponent<MoviePlayerImpl>();
                player.Setup(settings);
                _players[index] = player;
            }
        }

        public void LoadMovie()
        {
            SyncPlayerListLength();
            for (var i = 0; i < settingsList.Count; i++)
            {
                LoadMovie(i);
            }
        }

        public void LoadMovie(int index)
        {
            SyncPlayerListLength();
            if (!IsValidIndex(index) || !IsEnabled(index))
            {
                return;
            }

            var path = GetSettings(index).path;
            if (_loadedVideoPaths[index] == path)
            {
                return;
            }
            _loadedVideoPaths[index] = path;

            SetupImpl(index);

            if (_players[index] != null)
            {
                _players[index].LoadMovie(path);
            }
        }

        public void UnloadMovie()
        {
            SyncPlayerListLength();
            for (var i = 0; i < _players.Count; i++)
            {
                DestroyPlayer(i);
            }
        }

        public void UnloadMovie(int index)
        {
            SyncPlayerListLength();
            if (IsValidIndex(index))
            {
                DestroyPlayer(index);
            }
        }

        public void ReloadMovie()
        {
            UnloadMovie();
            LoadMovie();
        }

        public void ReloadMovie(int index)
        {
            UnloadMovie(index);
            LoadMovie(index);
        }

        public void UpdateTransform() { ForEachPlayer(p => p.UpdateTransform()); }
        public void UpdateTransform(int index) { WithPlayer(index, p => p.UpdateTransform()); }
        public void UpdateVolume() { ForEachPlayer(p => p.UpdateVolume()); }
        public void UpdateVolume(int index) { WithPlayer(index, p => p.UpdateVolume()); }
        public void UpdateSpeed() { ForEachPlayer(p => p.UpdateSpeed()); }
        public void UpdateSpeed(int index) { WithPlayer(index, p => p.UpdateSpeed()); }
        public void UpdateSeekTime() { ForEachPlayer(p => p.UpdateSeekTime()); }
        public void UpdateSeekTime(int index) { WithPlayer(index, p => p.UpdateSeekTime()); }
        public void UpdateColor() { ForEachPlayer(p => p.UpdateColor()); }
        public void UpdateColor(int index) { WithPlayer(index, p => p.UpdateColor()); }
        public void UpdateMesh() { ForEachPlayer(p => p.UpdateMesh()); }
        public void UpdateMesh(int index) { WithPlayer(index, p => p.UpdateMesh()); }
        public void UpdateShader() { ForEachPlayer(p => p.UpdateShader()); }
        public void UpdateShader(int index) { WithPlayer(index, p => p.UpdateShader()); }

        private void ForEachPlayer(System.Action<MoviePlayerImpl> action)
        {
            SyncPlayerListLength();
            foreach (var player in _players)
            {
                if (player != null)
                {
                    action(player);
                }
            }
        }

        private void WithPlayer(int index, System.Action<MoviePlayerImpl> action)
        {
            var player = GetPlayer(index);
            if (player != null)
            {
                action(player);
            }
        }

        public override void OnLoad()
        {
            ReloadMovie();
        }

        public override void OnPluginDisable()
        {
            UnloadMovie();
        }
    }
}
```

`Update*` 群の 1 行メソッドは、既存コードの体裁（ブロックを改行する）に合わせて整形する。

- [ ] **Step 3: 旧 API の呼び出しが他に残っていないか確認する**

Run: `grep -rn 'movieManager\.settings\b\|MovieManager.instance.settings\|movieManager\.isEnabled\|movieManager\.isValidPath\|movieManager\.texture\|movieManager\.duration\|movieManager\.frameRate\|movieManager\.requiresVerticalFlip\|timeline\.video\b' source --include=*.cs`
Expected: `VideoWindow.cs` / `VideoPreviewWindow.cs` / `MteEffectsSnapshot.cs` のみ（Task 4 / 5 で直す）。それ以外が出たら本タスクで `GetSettings(0)` 等ではなく、用途に応じた index 付き API へ置き換える

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs
git commit -m "feat(video): MovieManager を複数プレイヤー対応にし MoviePlayerImpl へ設定を注入する"
```

---

### Task 4: VideoWindow の動画数・操作対象と VideoPreviewWindow の選択追従

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`

**Interfaces:**
- Consumes: Task 3 の `MovieManager` index 付き API、`CountRowDrawer.Draw(GUIView, string, float, int, int, int, Action<int>)`、`GUIComboBox<int>`
- Produces: `VideoWindow.selectedIndex : int`

- [ ] **Step 1: VideoWindow に選択状態と動画数行を入れる**

`using System.Collections.Generic;` を追加。`private static MTEP.VideoSettings settings => movieManager.settings;` を置き換え:

```csharp
/// <summary>操作対象の動画添字</summary>
private int _videoIndex = 0;

/// <summary>プレビューウィンドウが表示対象を合わせるために参照する</summary>
public int selectedIndex => _videoIndex;

private MTEP.VideoSettings settings => movieManager.GetSettings(_videoIndex);

private readonly GUIComboBox<int> _videoComboBox = new GUIComboBox<int>
{
    getName = (index, _) => "動画" + (index + 1),
    labelWidth = 70,
    buttonSize = new Vector2(150, 20),
    contentSize = new Vector2(150, 120),
};

/// <summary>コンボ選択肢 (0〜videoCount-1)。本数変更時だけ作り直す</summary>
private readonly List<int> _videoIndexItems = new List<int>();
```

`settings` が static でなくなるため、`_videoDisplayTypeComboBox` の初期化子内ラムダはインスタンスメンバーを参照できない（フィールド初期化子から `this` は使えない）。`_videoDisplayTypeComboBox` の `onSelected` はコンストラクタで設定する:

```csharp
private VideoWindow()
{
    _videoComboBox.onSelected = (index, _) => _videoIndex = index;
    _videoDisplayTypeComboBox.onSelected = (type, index) =>
    {
        settings.displayType = type;
        movieManager.ReloadMovie(_videoIndex);
    };
}
```

フィールド初期化子からは `onSelected` を外す。

`DrawContent` の `DrawVideoSetting(_view);` を以下に置き換える:

```csharp
DrawVideoSelector(_view);
if (movieManager.IsValidIndex(_videoIndex))
{
    DrawVideoSetting(_view);
}
```

新メソッド:

```csharp
private void DrawVideoSelector(GUIView view)
{
    CountRowDrawer.Draw(view, "動画数", ROW_HEIGHT, movieManager.videoCount,
        MTEP.MovieManager.MinVideoCount,
        MTEP.MovieManager.MaxVideoCount,
        count => movieManager.videoCount = count);

    var videoCount = movieManager.videoCount;
    _videoIndex = Mathf.Clamp(_videoIndex, 0, videoCount - 1);

    if (_videoIndexItems.Count != videoCount)
    {
        _videoIndexItems.Clear();
        for (var i = 0; i < videoCount; i++)
        {
            _videoIndexItems.Add(i);
        }
    }

    _videoComboBox.items = _videoIndexItems;
    _videoComboBox.currentIndex = _videoIndex;
    _videoComboBox.DrawButton("操作対象", view);
}
```

- [ ] **Step 2: DrawVideoSetting 以下の呼び出しを index 付きにする**

`DrawVideoSetting` / `DrawGuiSetting` / `DrawMeshSetting` / `DrawBackmostSetting` / `DrawFrontmostSetting` 内の `movieManager.LoadMovie()` → `movieManager.LoadMovie(_videoIndex)`、`UnloadMovie()` → `UnloadMovie(_videoIndex)`、`ReloadMovie()` → `ReloadMovie(_videoIndex)`、`UpdateSeekTime()` → `UpdateSeekTime(_videoIndex)`、`UpdateVolume()` → `UpdateVolume(_videoIndex)`、`UpdateTransform()` → `UpdateTransform(_videoIndex)`、`UpdateColor()` → `UpdateColor(_videoIndex)`、`UpdateMesh()` → `UpdateMesh(_videoIndex)` に置き換える。

開始位置スライダーの `max = movieManager.duration` → `max = movieManager.GetDuration(_videoIndex)`、`step = movieManager.frameRate > 0f ? 1f / movieManager.frameRate : 0.01f` → `GetFrameRate(_videoIndex)` を使う（ローカル変数に受ける）。

置換後に `grep -n 'movieManager\.[A-Za-z]*()' VideoWindow.cs` で引数なし呼び出しが残っていないことを確認する。

- [ ] **Step 3: VideoPreviewWindow を選択追従にする**

`protected override string windowTitle => "動画プレビュー";` を置き換え:

```csharp
/// <summary>操作対象の動画を示す。VideoWindow の選択に追従する</summary>
protected override string windowTitle => "動画プレビュー (" + (videoIndex + 1) + ")";

private static int videoIndex => VideoWindow.instance.selectedIndex;
```

`DrawContent` の `var texture = movieManager.texture;` → `var texture = movieManager.GetTexture(videoIndex);`、`movieManager.requiresVerticalFlip` → `movieManager.RequiresVerticalFlip(videoIndex)` に置き換える。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/VideoWindow.cs source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs
git commit -m "feat(video): 動画ウィンドウに動画数と操作対象を追加しプレビューを選択に追従させる"
```

---

### Task 5: シーンプリセット v32 と MteEffectsSnapshot の複数化

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetEffects.video`、`CurrentVersion`、バージョン履歴コメント）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`（`CaptureVideo` / `ApplyVideo`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs`

**Interfaces:**
- Consumes: Task 3 の `settingsList` / `videoCount` / `GetSettings` / `ReloadMovie()`
- Produces: `ScenePresetEffects.videos : List<ScenePresetVideo>`（`[XmlElement("video")]`）、`ScenePresetData.CurrentVersion = 32`

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetEffectsTests.cs` の `SoundAndVideo_RoundTrip_PreservesValues` を `videos` に 2 件入れる形へ書き換え、`CurrentVersion` の期待値を 32 にする。`V29Preset_WithoutSoundAndVideo_ReadsThemAsNull` の `Assert.Null(restored.effects.video)` を `Assert.Empty(restored.effects.videos)` にする。旧形式の読込テストを追加:

```csharp
[Fact]
public void SoundAndVideo_RoundTrip_PreservesValues()
{
    var data = new ScenePresetData { effects = new ScenePresetEffects() };
    data.effects.sound = new ScenePresetSound
    {
        gameBgmFile = "BGM020.ogg",
        bgmPath = @"C:\music\dance.ogg",
        bpm = 128f,
        isShowBPMLine = true,
        bpmLineOffsetFrame = 1.5f,
    };
    data.effects.videos.Add(new ScenePresetVideo
    {
        enabled = false,
        displayType = 3,
        path = @"C:\movie\a.mp4",
        position = new Vector3(1f, 2f, 3f),
        startTime = 0.5f,
        frontmostPosition = new Vector2(-0.7f, 0.7f),
        frontmostAlpha = 0.5f,
    });
    data.effects.videos.Add(new ScenePresetVideo
    {
        displayType = 2,
        path = @"C:\movie\b.mp4",
        backmostAlpha = 0.25f,
    });

    var restored = RoundTrip(data);

    Assert.Equal(32, ScenePresetData.CurrentVersion);
    Assert.NotNull(restored.effects.sound);
    Assert.Equal("BGM020.ogg", restored.effects.sound.gameBgmFile);

    Assert.Equal(2, restored.effects.videos.Count);
    Assert.False(restored.effects.videos[0].enabled);
    Assert.Equal(3, restored.effects.videos[0].displayType);
    Assert.Equal(@"C:\movie\a.mp4", restored.effects.videos[0].path);
    Assert.Equal(new Vector3(1f, 2f, 3f), restored.effects.videos[0].position);
    Assert.Equal(0.5f, restored.effects.videos[0].startTime);
    Assert.Equal(new Vector2(-0.7f, 0.7f), restored.effects.videos[0].frontmostPosition);
    Assert.Equal(0.5f, restored.effects.videos[0].frontmostAlpha);
    Assert.Equal(2, restored.effects.videos[1].displayType);
    Assert.Equal(@"C:\movie\b.mp4", restored.effects.videos[1].path);
    Assert.Equal(0.25f, restored.effects.videos[1].backmostAlpha);
}

[Fact]
public void V31Preset_SingleVideoElement_ReadsAsOneItemList()
{
    var serializer = new XmlSerializer(typeof(ScenePresetData));
    var xml = "<ScenePresetData version=\"31\" savedEffects=\"true\"><effects>"
        + "<video enabled=\"true\" displayType=\"2\"><path>C:\\movie\\old.mp4</path><backmostAlpha>0.4</backmostAlpha></video>"
        + "</effects></ScenePresetData>";
    using (var reader = new StringReader(xml))
    {
        var restored = (ScenePresetData)serializer.Deserialize(reader);
        Assert.Single(restored.effects.videos);
        Assert.Equal(2, restored.effects.videos[0].displayType);
        Assert.Equal(@"C:\movie\old.mp4", restored.effects.videos[0].path);
        Assert.Equal(0.4f, restored.effects.videos[0].backmostAlpha);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー（`effects.videos` が存在しない）

- [ ] **Step 3: ScenePresetData を変更する**

`ScenePresetEffects` の `public ScenePresetVideo video;` を置き換え:

```csharp
/// <summary>動画 (v32 で複数化)。要素名は v30 の単体 video のままなので旧形式も 1 件として読める</summary>
[XmlElement("video")]
public List<ScenePresetVideo> videos = new List<ScenePresetVideo>();
```

クラスコメントの `sound / video は要素なし (null) を未記録として触らない` を `sound は要素なし (null)、video は空リストを未記録として触らない` に直す。`ScenePresetVideo` のクラスコメント `要素なし (null) は未記録として適用時に触らない` を `空リストは未記録として適用時に触らない` に直す。

バージョン履歴コメントの v30 行にある「video (動画設定)」の説明はそのまま残し、末尾に v32 行を追記して `CurrentVersion` を上げる（v30 の「旧形式は null で読め」は sound にだけ当てはまるようになるため、v32 行で video 側の読み方を明記する）:

```csharp
// v32: effects.video を複数化。要素名 video を繰り返す形式で、旧形式の単体 video は 1 件として読める
public static readonly int CurrentVersion = 32;
```

- [ ] **Step 4: MteEffectsSnapshot を書き換える**

`data.video = CaptureVideo();` → `CaptureVideos(data);`、`ApplyVideo(data.video);` → `ApplyVideos(data);`。メソッド本体:

```csharp
private static void CaptureVideos(ScenePresetEffects data)
{
    foreach (var settings in movieManager.settingsList)
    {
        data.videos.Add(new ScenePresetVideo
        {
            enabled = settings.enabled,
            displayType = (int)settings.displayType,
            path = settings.path,
            position = settings.position,
            rotation = settings.rotation,
            scale = settings.scale,
            startTime = settings.startTime,
            volume = settings.volume,
            alpha = settings.alpha,
            guiPosition = settings.guiPosition,
            guiScale = settings.guiScale,
            guiAlpha = settings.guiAlpha,
            backmostPosition = settings.backmostPosition,
            backmostScale = settings.backmostScale,
            backmostAlpha = settings.backmostAlpha,
            frontmostPosition = settings.frontmostPosition,
            frontmostScale = settings.frontmostScale,
            frontmostAlpha = settings.frontmostAlpha,
        });
    }
}

/// <summary>空 (旧プリセット / 未保存) なら触らない</summary>
private static void ApplyVideos(ScenePresetEffects data)
{
    if (data.videos.Count == 0)
    {
        return;
    }

    // 手編集や破損 XML の異常値で大量生成しないよう UI と同じ上限へ丸める
    var count = Mathf.Min(data.videos.Count, MTEP.MovieManager.MaxVideoCount);
    movieManager.videoCount = count;

    for (var i = 0; i < count; i++)
    {
        var src = data.videos[i];
        var settings = movieManager.GetSettings(i);
        settings.enabled = src.enabled;
        settings.displayType = (MTEP.VideoDisplayType)src.displayType;
        settings.path = src.path;
        settings.position = src.position;
        settings.rotation = src.rotation;
        settings.scale = src.scale;
        settings.startTime = src.startTime;
        settings.volume = src.volume;
        settings.alpha = src.alpha;
        settings.guiPosition = src.guiPosition;
        settings.guiScale = src.guiScale;
        settings.guiAlpha = src.guiAlpha;
        settings.backmostPosition = src.backmostPosition;
        settings.backmostScale = src.backmostScale;
        settings.backmostAlpha = src.backmostAlpha;
        settings.frontmostPosition = src.frontmostPosition;
        settings.frontmostScale = src.frontmostScale;
        settings.frontmostAlpha = src.frontmostAlpha;
    }
    // 無効やパス空の本は Unload だけが走る (LoadMovie は IsEnabled を見る)
    movieManager.ReloadMovie();
}
```

- [ ] **Step 5: テストとビルドを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

Run: Global Constraints の MSBuild 2 コマンド
Expected: 両構成とも「エラー 0」。`VideoWindow` / `VideoPreviewWindow` / `MteEffectsSnapshot` の旧 API 参照が残っていればここで出るので直す

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "feat(preset): シーンプリセットの動画設定を複数本に対応する (v32)"
```

---

### Task 6: 実機確認とドキュメント

**Files:**
- Modify: `docs/` 配下で動画ウィンドウに触れているファイル（`grep -rln '動画ウィンドウ\|動画設定' docs/*.md docs/references 2>/dev/null` で特定）

- [ ] **Step 1: 実機で確認する**

ゲーム起動中に MCP `com3d25-devbridge` の `ping` で死活確認し、Harmony ホットリロード手順（ルート `CLAUDE.md` 参照）または通常のゲーム再起動で新ビルドを反映する。確認項目:

1. 動画ウィンドウで動画数を 2 にし、動画 1 を GUI、動画 2 を最背面でそれぞれ別ファイルを指定して同時に表示されること
2. プレビューウィンドウのタイトルが「動画プレビュー (2)」になり、動画 2 のテクスチャが出ること
3. タイムラインを読み込んで再生・シークしたとき両方の動画が追随すること（`eval_csharp` で `MovieManager.instance.GetCurrentTime(0)` と `(1)` を比べる）
4. 動画数を 1 に戻すと動画 2 の GameObject（`MoviePlayer_1_*`）が消えること
5. タイムラインを破棄しても本数と配置が維持されること
6. シーンプリセットに保存して読み直すと 2 本とも復元されること
7. 旧タイムラインファイル（v32 以前）を読み込むと 1 本目として設定が入ること

問題があれば該当タスクへ戻って修正し、テストと 2 構成ビルドを再実行する。

- [ ] **Step 2: ドキュメント更新**

動画ウィンドウを説明している docs に「動画数（最大 4）」「操作対象コンボ」「プレビューは操作対象に追従」を追記する。該当 docs が無ければこの Step は省略する。

- [ ] **Step 3: コードレビューとコミット**

`code-review` スキルでレビューし、妥当な指摘を取り込んでから commit スキルでコミットする。

---

## Self-Review

- Spec coverage: §1 データモデル → Task 1〜3、§2 UI → Task 4、§3 プリセット → Task 5、§4 テスト → 各タスクの Step 1 と Task 6、両構成ビルド → Task 5 Step 5
- 型整合: `VideoSettingsXml` / `TimelineXml.videos` / `TimelineData.videos` / `MovieManager.settingsList` / `videoCount` / `GetSettings(int)` / index 付き `Update*` / `VideoWindow.selectedIndex` / `ScenePresetEffects.videos` を全タスクで同名で使用
- `MoviePlayerImpl.Setup` は `AddComponent` が `Awake` を同期実行する制約を踏まえ、`Awake` 本体を `Setup` へ移す方式に統一

## レビュー却下メモ

- `VideoWindow` のフィールド初期化子内ラムダはインスタンスメンバーを参照できる、という指摘 — 誤検知。C# ではインスタンスフィールドの初期化子（ラムダ内を含む）から `this` のメンバーを参照すると CS0236 になる。現行コードは `settings` が static だから通っている。TextWindow が `onSelected` をコンストラクタで設定しているのも同じ理由
- `MoviePlayerImpl` の `video` に `_video ?? 既定値` フォールバックを入れる提案 — 未確認のまま見送り。`AddComponent` 直後に同一メソッド内で `Setup` を呼ぶため現状は到達不能。フォールバックは設定の取り違えを隠すため入れない
