# BGM のサウンドウィンドウ移設・動画ウィンドウ新設・シーンプリセット対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン設定の BGM/動画セクションをサウンドウィンドウと新設の動画ウィンドウへ移し、タイムライン未読込でも編集でき、シーンプリセット (演出カテゴリ) で保存・復元できるようにする。

**Architecture:** MTE 側の `TimelineData` に散らばる BGM/動画フィールドを `BgmSettings` / `VideoSettings` の DTO に集約し、`BGMManager.settings` / `MovieManager.settings` が「タイムライン読込中は timeline 側、未読込時はマネージャ保持の standalone 値」を返す (`TimelineTextManager.textCount` と同じ方式)。UI とプリセットはこの `settings` だけを読み書きする。プリセットは `ScenePresetEffects` に `sound` / `video` 要素を追加して v30 にする。

**Tech Stack:** C# (.NET 3.5 / 4.7.1 の 2 構成)、UnityInjector、IMGUI (`GUIView`)、xUnit (`dotnet test`)

**Spec:** `docs/superpowers/specs/2026-09-03-sound-video-window-design.md`

## Global Constraints

- COM3D2 (.NET 3.5) と COM3D25 (.NET 4.7.1) の **2 構成を必ず両方ビルド**する。`debug.bat` は実機へ DLL をコピーするため使わず、MSBuild を直接叩く:
  ```
  cd source/COM3D2.SceneEditor.Plugin
  MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
  MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- テストは COM3D25 構成のビルド後に `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` (リポジトリ直下で実行)
- .NET 3.5 で使えないもの (入力 5 個以上の `Func<>`/`Action<>`、`CallerMemberName` 等) を使わない
- コードのコメント・ログ文言は日本語
- コミットメッセージは Conventional Commits の日本語。末尾に
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BUk85A97uVqG1SReQV62RH
  ```
- git worktree は使わない
- `TimelineXml` の形式 (要素名) は変更しない
- 新ウィンドウ ID は `8903396` (既存最大は 8903395)

---

### Task 1: BgmSettings / VideoSettings DTO と TimelineXml との写し

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/BgmSettings.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`

**Interfaces:**
- Produces:
  - `COM3D2.MotionTimelineEditor.Plugin.BgmSettings { string bgmPath; float bpm; bool isShowBPMLine; float bpmLineOffsetFrame; void CopyFrom(BgmSettings); void ReadFrom(TimelineXml); void WriteTo(TimelineXml) }`
  - `COM3D2.MotionTimelineEditor.Plugin.VideoSettings { bool enabled; VideoDisplayType displayType; string path; Vector3 position; Vector3 rotation; float scale; float startTime; float volume; float alpha; Vector2 guiPosition; float guiScale; float guiAlpha; Vector2 backmostPosition; float backmostScale; float backmostAlpha; Vector2 frontmostPosition; float frontmostScale; float frontmostAlpha; void CopyFrom(VideoSettings); void ReadFrom(TimelineXml); void WriteTo(TimelineXml) }`

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// BGM / 動画設定 DTO と TimelineXml の写しが対称であることを固定する。
    /// 片方向だけ項目を落とすとタイムライン保存時に設定が黙って消えるため
    /// </summary>
    public class BgmVideoSettingsXmlTests
    {
        [Fact]
        public void BgmSettings_TimelineXmlとの往復で値が保持される()
        {
            var src = new BgmSettings
            {
                bgmPath = @"C:\music\test.ogg",
                bpm = 128.5f,
                isShowBPMLine = true,
                bpmLineOffsetFrame = -3.5f,
            };

            var xml = new TimelineXml();
            src.WriteTo(xml);
            var dst = new BgmSettings();
            dst.ReadFrom(xml);

            Assert.Equal(@"C:\music\test.ogg", dst.bgmPath);
            Assert.Equal(128.5f, dst.bpm);
            Assert.True(dst.isShowBPMLine);
            Assert.Equal(-3.5f, dst.bpmLineOffsetFrame);
        }

        [Fact]
        public void VideoSettings_TimelineXmlとの往復で値が保持される()
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

            var xml = new TimelineXml();
            src.WriteTo(xml);
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
        public void CopyFrom_全項目を写す()
        {
            var bgm = new BgmSettings { bgmPath = "a.ogg", bpm = 90f, isShowBPMLine = true, bpmLineOffsetFrame = 2f };
            var bgmCopy = new BgmSettings();
            bgmCopy.CopyFrom(bgm);
            Assert.Equal("a.ogg", bgmCopy.bgmPath);
            Assert.Equal(90f, bgmCopy.bpm);
            Assert.True(bgmCopy.isShowBPMLine);
            Assert.Equal(2f, bgmCopy.bpmLineOffsetFrame);

            var video = new VideoSettings { path = "b.mp4", displayType = VideoDisplayType.Mesh, frontmostAlpha = 0.1f };
            var videoCopy = new VideoSettings();
            videoCopy.CopyFrom(video);
            Assert.Equal("b.mp4", videoCopy.path);
            Assert.Equal(VideoDisplayType.Mesh, videoCopy.displayType);
            Assert.Equal(0.1f, videoCopy.frontmostAlpha);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter BgmVideoSettingsXmlTests`
Expected: ビルドエラー (`BgmSettings` / `VideoSettings` が未定義)

- [ ] **Step 3: DTO を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/BgmSettings.cs`:

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムライン BGM ファイルの設定。
    /// TimelineData が持つがタイムライン未読込時は BGMManager が standalone 値として保持する
    /// </summary>
    public class BgmSettings
    {
        public string bgmPath = "";
        public float bpm = 120f;
        public bool isShowBPMLine = false;
        public float bpmLineOffsetFrame = 0f;

        public void CopyFrom(BgmSettings src)
        {
            bgmPath = src.bgmPath;
            bpm = src.bpm;
            isShowBPMLine = src.isShowBPMLine;
            bpmLineOffsetFrame = src.bpmLineOffsetFrame;
        }

        // TimelineXml の要素名は互換維持のため変えない (ReadFrom / WriteTo で写す)
        public void ReadFrom(TimelineXml xml)
        {
            bgmPath = xml.bgmPath;
            bpm = xml.bpm;
            isShowBPMLine = xml.isShowBPMLine;
            bpmLineOffsetFrame = xml.bpmLineOffsetFrame;
        }

        public void WriteTo(TimelineXml xml)
        {
            xml.bgmPath = bgmPath;
            xml.bpm = bpm;
            xml.isShowBPMLine = isShowBPMLine;
            xml.bpmLineOffsetFrame = bpmLineOffsetFrame;
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs`:

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 動画の読み込み・表示設定。
    /// TimelineData が持つがタイムライン未読込時は MovieManager が standalone 値として保持する
    /// </summary>
    public class VideoSettings
    {
        public bool enabled = true;
        public VideoDisplayType displayType = VideoDisplayType.GUI;
        public string path = "";
        public Vector3 position = new Vector3(0, 0, 0);
        public Vector3 rotation = new Vector3(0, 0, 0);
        public float scale = 1f;
        public float startTime = 0f;
        public float volume = 0.5f;
        public float alpha = 1f;
        public Vector2 guiPosition = new Vector2(0, 0);
        public float guiScale = 1f;
        public float guiAlpha = 1f;
        public Vector2 backmostPosition = new Vector2(0, 0);
        public float backmostScale = 1f;
        public float backmostAlpha = 0.5f;
        public Vector2 frontmostPosition = new Vector2(-0.8f, 0.8f);
        public float frontmostScale = 0.38f;
        public float frontmostAlpha = 1f;

        public void CopyFrom(VideoSettings src)
        {
            enabled = src.enabled;
            displayType = src.displayType;
            path = src.path;
            position = src.position;
            rotation = src.rotation;
            scale = src.scale;
            startTime = src.startTime;
            volume = src.volume;
            alpha = src.alpha;
            guiPosition = src.guiPosition;
            guiScale = src.guiScale;
            guiAlpha = src.guiAlpha;
            backmostPosition = src.backmostPosition;
            backmostScale = src.backmostScale;
            backmostAlpha = src.backmostAlpha;
            frontmostPosition = src.frontmostPosition;
            frontmostScale = src.frontmostScale;
            frontmostAlpha = src.frontmostAlpha;
        }

        // TimelineXml の要素名 (video 接頭辞) は互換維持のため変えない
        public void ReadFrom(TimelineXml xml)
        {
            enabled = xml.videoEnabled;
            displayType = xml.videoDisplayType;
            path = xml.videoPath;
            position = xml.videoPosition;
            rotation = xml.videoRotation;
            scale = xml.videoScale;
            startTime = xml.videoStartTime;
            volume = xml.videoVolume;
            alpha = xml.videoAlpha;
            guiPosition = xml.videoGUIPosition;
            guiScale = xml.videoGUIScale;
            guiAlpha = xml.videoGUIAlpha;
            backmostPosition = xml.videoBackmostPosition;
            backmostScale = xml.videoBackmostScale;
            backmostAlpha = xml.videoBackmostAlpha;
            frontmostPosition = xml.videoFrontmostPosition;
            frontmostScale = xml.videoFrontmostScale;
            frontmostAlpha = xml.videoFrontmostAlpha;
        }

        public void WriteTo(TimelineXml xml)
        {
            xml.videoEnabled = enabled;
            xml.videoDisplayType = displayType;
            xml.videoPath = path;
            xml.videoPosition = position;
            xml.videoRotation = rotation;
            xml.videoScale = scale;
            xml.videoStartTime = startTime;
            xml.videoVolume = volume;
            xml.videoAlpha = alpha;
            xml.videoGUIPosition = guiPosition;
            xml.videoGUIScale = guiScale;
            xml.videoGUIAlpha = guiAlpha;
            xml.videoBackmostPosition = backmostPosition;
            xml.videoBackmostScale = backmostScale;
            xml.videoBackmostAlpha = backmostAlpha;
            xml.videoFrontmostPosition = frontmostPosition;
            xml.videoFrontmostScale = frontmostScale;
            xml.videoFrontmostAlpha = frontmostAlpha;
        }
    }
}
```

- [ ] **Step 4: 両構成をビルドしテストを通す**

Run: Global Constraints の MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter BgmVideoSettingsXmlTests`
Expected: 3 tests PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/BgmSettings.cs source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs
git commit -m "feat(timeline): BGM/動画設定の DTO (BgmSettings/VideoSettings) を追加する"
```

---

### Task 2: TimelineData のフィールドを DTO に集約する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:428-431, 442-460, 849-852, 861-878, 1008-1011, 1020-1037`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:795-798`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` (`timeline.bgmPath` 等の参照。Task 4/5 で削除するが、本タスク時点でコンパイルを通す)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGMManager.cs:63`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs:29,47,78,81`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs` (`timeline.videoXxx` 参照)

**Interfaces:**
- Consumes: Task 1 の `BgmSettings` / `VideoSettings`
- Produces: `TimelineData.bgm : BgmSettings`, `TimelineData.video : VideoSettings` (個別フィールド `bgmPath` / `bpm` / `isShowBPMLine` / `bpmLineOffsetFrame` / `videoXxx` は削除)

- [ ] **Step 1: TimelineData のフィールドを置き換える**

`Timeline/TimelineData.cs` の 428-431 行:

```csharp
        public string bgmPath = "";
        public float bpm = 120f;
        public bool isShowBPMLine = false;
        public float bpmLineOffsetFrame = 0f;
```
を
```csharp
        public BgmSettings bgm = new BgmSettings();
```
に、442-460 行 (`// 動画` から `videoFrontmostAlpha` まで) を
```csharp
        // 動画
        public VideoSettings video = new VideoSettings();
```
に置き換える。

- [ ] **Step 2: FromXml / ToXml を DTO 経由にする**

FromXml (849-852 行の `bgmPath = xml.bgmPath;` 〜 `bpmLineOffsetFrame = xml.bpmLineOffsetFrame;`) を `bgm.ReadFrom(xml);` に、861-878 行 (`videoEnabled = xml.videoEnabled;` 〜 `videoFrontmostAlpha = xml.videoFrontmostAlpha;`) を `video.ReadFrom(xml);` に置き換える。

ToXml (1008-1011 行) を `bgm.WriteTo(xml);` に、1020-1037 行を `video.WriteTo(xml);` に置き換える。

- [ ] **Step 3: 参照側を機械的に追随させる**

以下を `source/COM3D2.SceneEditor.Plugin` で実行する (Git Bash):

```bash
# BGM 系 (TimelineWindow / TimelineSettingWindow / BGMManager)
sed -i -E 's/timeline\.(bgmPath|bpm|isShowBPMLine|bpmLineOffsetFrame)\b/timeline.bgm.\1/g' TimelineWindow.cs TimelineSettingWindow.cs Timeline/Manager/BGMManager.cs
# 動画系 (GUI 接頭辞を先に処理してから残りを小文字化)
sed -i -E 's/timeline\.videoGUI([A-Z])/timeline.video.gui\1/g; s/timeline\.video([A-Z])/timeline.video.\l\1/g' TimelineSettingWindow.cs Timeline/Manager/MovieManager.cs Timeline/MoviePlayerImpl.cs
```

置換後に `grep -rn 'timeline\.video\.\|timeline\.bgm\.' --include=*.cs .` で `timeline.video.enabled` / `timeline.video.displayType` / `timeline.video.path` / `timeline.video.guiPosition` / `timeline.bgm.bgmPath` の形になっていることを目視確認する。さらに `git diff` を全行目視し、意図しない箇所 (コメント内・別シンボル) が書き換わっていないことを確認する (GNU sed の `\l` が効かない環境では `timeline.video.Enabled` のような大文字残りになるので、その場合は手で直す)。

- [ ] **Step 4: 両構成をビルドし既存テストを通す**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS (フィクスチャ往復テスト `XmlRoundTripTests` が XML 形式不変を保証する)

- [ ] **Step 5: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "refactor(timeline): TimelineData の BGM/動画フィールドを BgmSettings/VideoSettings へ集約する"
```

---

### Task 3: BGMManager / MovieManager に settings を追加しタイムライン非依存にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:38-43, 301-312`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGMManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`

**Interfaces:**
- Consumes: Task 2 の `timeline.bgm` / `timeline.video`
- Produces:
  - `TimelineManager.onClearTimeline : static event UnityAction` (タイムライン破棄の直前、`timeline` がまだ非 null の時点で発火)
  - `BGMManager.settings : BgmSettings` (読込中は `timeline.bgm`、未読込時はマネージャ保持の standalone)
  - `MovieManager.settings : VideoSettings` (同上)
  - `BGMManager.Load()` / `Play()` / `Pause()` / `Stop()` はタイムライン未読込でも動く

**背景 (plan-review 指摘):** `TimelineManager.ClearTimeline()` は `_timeline = null` を代入するだけで各マネージャへ通知しない。通知なしだと、破棄の瞬間に `settings` が standalone の既定値へ切り替わる一方で、再生中の音声クリップ / MoviePlayerImpl の実体はそのまま残り、UI 表示と実体が乖離する。破棄直前に timeline 側の値を standalone へ写すことで、破棄後も同じ設定値で再生が続く (テキストの `_standaloneTextCount = timeline.textCount` 同期と同じ考え方)。

- [ ] **Step 1: TimelineManager に破棄通知イベントを追加する**

`Timeline/Manager/TimelineManager.cs` の `public static event UnityAction onSeekCurrentFrame;` の直後に追加:

```csharp
        /// <summary>
        /// タイムライン破棄の直前 (timeline がまだ非 null の時点) に発火する。
        /// BGM / 動画マネージャが timeline 側の設定値を standalone 値へ引き継ぐために使う
        /// </summary>
        public static event UnityAction onClearTimeline;
```

`ClearTimeline()`:

```csharp
            if (timeline != null)
            {
                _timeline.Dispose();
```
を
```csharp
            if (timeline != null)
            {
                onClearTimeline?.Invoke();
                _timeline.Dispose();
```
に置き換える。

- [ ] **Step 2: BGMManager に settings を追加し Load / Update の timeline 依存を外す**

`Timeline/Manager/BGMManager.cs`:

フィールド群 (`private float _prevMotionTime = 0f;` の直後) に追加:

```csharp
        /// <summary>タイムライン未読込時に使う設定。読込中は timeline 側が正</summary>
        private readonly BgmSettings _standaloneSettings = new BgmSettings();

        /// <summary>
        /// BGM 設定。タイムライン読込中は timeline 側 (TimelineXml に保存される)、
        /// 未読込時はマネージャ保持の standalone 値 (TimelineTextManager.textCount と同じ方式)
        /// </summary>
        public BgmSettings settings => timeline != null ? timeline.bgm : _standaloneSettings;

        /// <summary>
        /// タイムライン破棄時に timeline 側の値を引き継ぐ。
        /// 引き継がないと再生中のクリップはそのままなのに表示だけ既定値へ戻ってしまう
        /// </summary>
        private void OnClearTimeline()
        {
            _standaloneSettings.CopyFrom(timeline.bgm);
        }
```

`Init()` に `TimelineManager.onClearTimeline += OnClearTimeline;` を追加する。

`Load()` の先頭:

```csharp
            if (timeline == null)
            {
                return false;
            }

            var bgmPath = timeline.bgm.bgmPath;
```
を
```csharp
            var bgmPath = settings.bgmPath;
```
に置き換える。同じメソッド内の `_audioMgr.audiosource.pitch = timelineManager.anmSpeed;` はそのまま (未読込時は 1 倍)。

`Update()`:

```csharp
            if (!IsLoaded())
            {
                return;
            }

            var isAnmPlaying = defaultLayer.isAnmPlaying;
```
を
```csharp
            // 未読込時は手動再生 (Play/Pause/Stop) のみで、タイムラインとの同期は行わない
            if (!IsLoaded() || timeline == null)
            {
                return;
            }

            var isAnmPlaying = defaultLayer.isAnmPlaying;
```
に置き換える。

`SeekPlayingTime()`:

```csharp
            if (IsLoaded())
            {
                var motionTime = defaultLayer.playingTime;
```
を
```csharp
            if (IsLoaded() && timeline != null)
            {
                var motionTime = defaultLayer.playingTime;
```
に置き換える (`Play()` から呼ばれるため未読込時の NRE を防ぐ)。

- [ ] **Step 3: MovieManager に settings を追加する**

`Timeline/Manager/MovieManager.cs`:

```csharp
        private string videoPath
        {
            get => timeline != null ? timeline.video.path : "";
        }
```
を
```csharp
        /// <summary>タイムライン未読込時に使う設定。読込中は timeline 側が正</summary>
        private readonly VideoSettings _standaloneSettings = new VideoSettings();

        /// <summary>
        /// 動画設定。タイムライン読込中は timeline 側 (TimelineXml に保存される)、
        /// 未読込時はマネージャ保持の standalone 値 (TimelineTextManager.textCount と同じ方式)
        /// </summary>
        public VideoSettings settings => timeline != null ? timeline.video : _standaloneSettings;

        private string videoPath => settings.path;
```
に置き換える。

```csharp
        public bool isEnabled
        {
            get => isValidPath && timeline.video.enabled;
        }
```
を
```csharp
        public bool isEnabled
        {
            get => isValidPath && settings.enabled;
        }
```
に置き換える。

`Init()`:

```csharp
        public override void Init()
        {
            TimelineManager.onStop += UpdateSeekTime;
            TimelineManager.onAnmSpeedChanged += UpdateSpeed;
            TimelineManager.onSeekCurrentFrame += UpdateSeekTime;
        }
```
を
```csharp
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
            _standaloneSettings.CopyFrom(timeline.video);
        }
```
に置き換える。

`SetupImpl()`:

```csharp
            if (_videoDisplayType != timeline.video.displayType)
            {
                UnloadMovie();
                _videoDisplayType = timeline.video.displayType;
            }
```
を
```csharp
            if (_videoDisplayType != settings.displayType)
            {
                UnloadMovie();
                _videoDisplayType = settings.displayType;
            }
```
に置き換える。

- [ ] **Step 4: MoviePlayerImpl を settings 経由にする**

`Timeline/MoviePlayerImpl.cs` の static プロパティ群 (`private static Config config => ...` の直後) に追加:

```csharp
        private static VideoSettings video => MovieManager.instance.settings;
```

その後 Git Bash で:

```bash
sed -i -E 's/timeline\.video\./video./g' Timeline/MoviePlayerImpl.cs
```

`targetSeekTimeMs`:

```csharp
            get => (currentTime + timeline.startOffsetTime + video.startTime) * 1000f;
```
を
```csharp
            // 未読込時はタイムラインのオフセットが無いため 0 として扱う
            get => (currentTime + (timeline != null ? timeline.startOffsetTime : 0f) + video.startTime) * 1000f;
```
に置き換える。

`Update()` の `timeline == null || currentLayer == null` ガードはそのまま残す (シーク・速度同期はタイムライン駆動)。`LateUpdate()` は timeline を参照しなくなるためガードを外し、未読込時も最背面表示のカメラ追従が動くようにする:

```csharp
        public void LateUpdate()
        {
            // SE 追加ガード: Update と同じくタイムライン破棄直後の NRE を防ぐ
            if (timeline == null)
            {
                return;
            }

            if (isDisplayBackmost)
```
を
```csharp
        public void LateUpdate()
        {
            // 参照先は settings とカメラだけなので、タイムライン未読込でも最背面のカメラ追従を続ける
            if (isDisplayBackmost)
```
に置き換える。

- [ ] **Step 5: 両構成をビルドしテストを通す**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS。`grep -n 'timeline\.' Timeline/MoviePlayerImpl.cs` の結果に `timeline.video` が残っていないこと。`git diff` を全行目視し、sed の置換漏れ・過剰置換がないことを確認する

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline
git commit -m "feat(timeline): BGMManager/MovieManager にタイムライン非依存の settings を追加する"
```

---

### Task 4: サウンドウィンドウ BGM タブへ BGM ファイル設定を移す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/SoundWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` (`DrawBGMSetting` 削除)

**Interfaces:**
- Consumes: `MTEP.BGMManager.instance.settings`, `BGMManager.Load()/Reload()/Play()/Pause()/Stop()/IsLoaded()/volumeDance`

- [ ] **Step 1: SoundWindow に using と BGM ファイル区画を追加する**

`SoundWindow.cs` の using に追加:

```csharp
// UnityEngine と同名型 (Screen 等) の衝突を避けるため WinForms はエイリアスで参照する
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
```

static プロパティ群に追加:

```csharp
        private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;
        private static MTEP.TimelineData timeline => MTEP.TimelineManager.instance.timeline;
```

`DrawBgm` を次に置き換える:

```csharp
        /// <summary>
        /// ゲーム BGM の一覧表示・再生・停止と、タイムライン BGM ファイルの設定。
        /// 一覧はフォトモードの PhotoSoundData、再生は SoundMgr.PlayBGM の同一経路を使う
        /// </summary>
        private void DrawBgm(GUIView view)
        {
            if (!BgmUtils.EnsureSoundDataLoaded())
            {
                view.DrawLabel("BGM一覧を取得できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            var playingFileName = BgmUtils.GetPlayingFileName();

            DrawCurrentBgmRow(view, playingFileName);
            view.DrawHorizontalLine();
            DrawBgmFileSection(view);
            view.DrawHorizontalLine();
            view.DrawTextField("検索", LABEL_WIDTH, _bgmSearchText, -1, ROW_HEIGHT,
                value => _bgmSearchText = value);
            DrawBgmList(view, playingFileName);
        }

        /// <summary>
        /// タイムライン BGM ファイルの設定 (タイムライン設定ウィンドウから移設)。
        /// 値は BGMManager.settings に入るため、タイムライン未読込でも編集できる。
        /// 未読込時はタイムライン再生に追随しないため手動の再生/停止ボタンを出す
        /// </summary>
        private void DrawBgmFileSection(GUIView view)
        {
            var settings = bgmManager.settings;

            view.DrawLabel("BGMファイル", 100, ROW_HEIGHT);

            view.BeginHorizontal();
            {
                view.DrawLabel("パス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "BGMファイルを選択してください",
                        Filter = "音楽ファイル (*.wav;*.ogg)|*.wav;*.ogg",
                        InitialDirectory = settings.bgmPath,
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        settings.bgmPath = openFileDialog.FileName;
                        bgmManager.Load();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    bgmManager.Reload();
                }
            }
            view.EndLayout();

            view.DrawTextField(settings.bgmPath, 240, ROW_HEIGHT, newText => settings.bgmPath = newText);

            if (timeline == null)
            {
                view.BeginHorizontal();
                {
                    view.SetEnabled(bgmManager.IsLoaded());
                    if (view.DrawButton("再生", 60, ROW_HEIGHT))
                    {
                        bgmManager.Play();
                    }
                    if (view.DrawButton("一時停止", 80, ROW_HEIGHT))
                    {
                        bgmManager.Pause();
                    }
                    if (view.DrawButton("停止", 60, ROW_HEIGHT))
                    {
                        bgmManager.Stop();
                    }
                    view.SetEnabled(true);
                    view.DrawLabel("タイムライン読込後は再生に追随します", -1, ROW_HEIGHT, textColor: Color.gray);
                }
                view.EndLayout();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音量",
                labelWidth = 50,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 100,
                step = 0,
                defaultValue = 100,
                value = bgmManager.volumeDance,
                onChanged = value =>
                {
                    bgmManager.volumeDance = (int)value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawToggle("BPMライン表示", settings.isShowBPMLine, 120, ROW_HEIGHT, newValue =>
            {
                settings.isShowBPMLine = newValue;
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "BPM",
                labelWidth = 50,
                min = 1,
                max = 300,
                step = 0.1f,
                defaultValue = 120,
                value = settings.bpm,
                onChanged = value => settings.bpm = value,
            });

            // オフセットの範囲はフレームレート依存。未読込時は既定の 30 を使う
            var frameRate = timeline != null ? timeline.frameRate : 30f;
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "オフセット",
                labelWidth = 50,
                min = -frameRate,
                max = frameRate,
                step = 0.1f,
                defaultValue = 0,
                value = settings.bpmLineOffsetFrame,
                onChanged = value => settings.bpmLineOffsetFrame = value,
            });
        }
```

クラス冒頭の summary の「BGM タブはタイムラインに依存せず単独で利用できる」はそのまま (依然として正しい)。

- [ ] **Step 2: TimelineSettingWindow から BGM セクションを削除する**

`TimelineSettingWindow.cs`:
- `DrawBGMSetting(view);` の呼び出し (268 行付近) を削除
- `DrawBGMSetting` メソッド全体 (322-405 行付近、`/// <summary>BGM の読み込みと BPM ライン表示` から `view.DrawHorizontalLine(Color.gray);\n        }` まで) を削除
- `private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;` を削除

- [ ] **Step 3: 両構成をビルドする**

Run: MSBuild 2 本
Expected: ビルド成功。`grep -n 'bgmManager\|bgm\.' TimelineSettingWindow.cs` が空

- [ ] **Step 4: 実機で確認する (ゲーム起動中のみ。起動していなければスキップして次へ)**

- サウンドウィンドウ BGM タブにファイル区画が出る
- タイムライン未読込で ogg を選択 → 再生ボタンで鳴る → 停止で止まる
- タイムライン読込後は再生/停止ボタンが消え、タイムライン再生で鳴る

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/SoundWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(sound): タイムライン設定の BGM 設定をサウンドウィンドウへ移す"
```

---

### Task 5: 動画ウィンドウを新設する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:200-206` (サウンドウィンドウの直後に追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:64`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:130`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` (`DrawVideoSetting` と関連フィールドを削除)

**Interfaces:**
- Consumes: `MTEP.MovieManager.instance.settings`, `MovieManager.LoadMovie()/UnloadMovie()/ReloadMovie()/UpdateTransform()/UpdateVolume()/UpdateSeekTime()/UpdateColor()/UpdateMesh()/duration/frameRate`
- Produces: `VideoWindow.instance`, `VideoWindow.WINDOW_ID = 8903396`

- [ ] **Step 1: Config に配置項目を追加する**

`Config.cs` の `public bool soundVisible = false;` の直後に追加:

```csharp

        // 動画ウィンドウ
        public int videoPosX = -1;
        public int videoPosY = -1;
        public int videoWidth = 340;
        public int videoHeight = 420;
        public bool videoVisible = false;
```

- [ ] **Step 2: VideoWindow を作る**

`source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`:

```csharp
using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
// UnityEngine と同名型 (Screen 等) の衝突を避けるため WinForms はエイリアスで参照する
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 動画の読み込みと表示形式ごとの配置調整ウィンドウ (タイムライン設定ウィンドウから移設)。
    /// 値は MovieManager.settings に入るためタイムライン未読込でも編集できる。
    /// 未読込時はループ再生のみで、シークや再生速度のタイムライン同期は読込後に働く
    /// </summary>
    public class VideoWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903396;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "動画";

        private static readonly int ROW_HEIGHT = 20;

        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "GUI",
            "3Dビュー",
            "最背面",
            "最前面",
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;
        private static MTEP.VideoSettings settings => movieManager.settings;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private readonly GUIComboBox<MTEP.VideoDisplayType> _videoDisplayTypeComboBox = new GUIComboBox<MTEP.VideoDisplayType>
        {
            items = Enum.GetValues(typeof(MTEP.VideoDisplayType)).Cast<MTEP.VideoDisplayType>().ToList(),
            getName = (type, index) => VideoDisplayTypeNames[index],
            onSelected = (type, index) =>
            {
                settings.displayType = type;
                movieManager.ReloadMovie();
            },
        };

        private static VideoWindow _instance = null;
        public static VideoWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VideoWindow();
                }
                return _instance;
            }
        }

        private VideoWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.videoPosX;
            y = config.videoPosY;
            width = config.videoWidth;
            height = config.videoHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.videoPosX = x;
            config.videoPosY = y;
            config.videoWidth = width;
            config.videoHeight = height;
        }

        public override bool savedVisible
        {
            get => config.videoVisible;
            set => config.videoVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            _view.SetEnabled(_view.focusedComboBox == null);
            DrawVideoSetting(_view);

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawVideoSetting(GUIView view)
        {
            var isEnabled = settings.enabled;

            view.DrawToggle("有効", isEnabled, 60, ROW_HEIGHT, newValue =>
            {
                settings.enabled = newValue;
                if (newValue)
                {
                    movieManager.LoadMovie();
                }
                else
                {
                    movieManager.UnloadMovie();
                }
            });

            _videoDisplayTypeComboBox.currentIndex = (int)settings.displayType;
            _videoDisplayTypeComboBox.DrawButton("表示形式", view);

            view.SetEnabled(isEnabled && view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.DrawLabel("動画パス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "動画ファイルを選択してください",
                        Filter = "動画ファイル (*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm)|*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm|すべてのファイル (*.*)|*.*",
                        InitialDirectory = settings.path
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        settings.path = openFileDialog.FileName;
                        movieManager.LoadMovie();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    movieManager.ReloadMovie();
                }
            }
            view.EndLayout();

            view.DrawTextField(settings.path, 240, ROW_HEIGHT, newText => settings.path = newText);

            if (timeline == null)
            {
                view.DrawLabel("タイムライン読込後にシークと再生速度が同期します", -1, ROW_HEIGHT, textColor: Color.gray);
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始位置",
                labelWidth = 60,
                min = -1f,
                max = movieManager.duration,
                step = movieManager.frameRate > 0f ? 1f / movieManager.frameRate : 0.01f,
                defaultValue = 0f,
                value = settings.startTime,
                onChanged = newValue =>
                {
                    settings.startTime = newValue;
                    movieManager.UpdateSeekTime();
                },
            });

            switch (settings.displayType)
            {
                case MTEP.VideoDisplayType.GUI:
                    DrawGuiSetting(view);
                    break;
                case MTEP.VideoDisplayType.Mesh:
                    DrawMeshSetting(view);
                    break;
                case MTEP.VideoDisplayType.Backmost:
                    DrawBackmostSetting(view);
                    break;
                case MTEP.VideoDisplayType.Frontmost:
                    DrawFrontmostSetting(view);
                    break;
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音量",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0.5f,
                value = settings.volume,
                onChanged = newValue =>
                {
                    settings.volume = newValue;
                    movieManager.UpdateVolume();
                },
            });

            view.SetEnabled(true);
        }

        private void DrawGuiSetting(GUIView view)
        {
            var guiPosition = settings.guiPosition;
            var newGUIPosition = guiPosition;
            for (var i = 0; i < 2; i++)
            {
                var value = guiPosition[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newGUIPosition[i] = newValue,
                });
            }

            if (newGUIPosition != guiPosition)
            {
                settings.guiPosition = newGUIPosition;
                movieManager.UpdateTransform();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.guiScale,
                onChanged = value =>
                {
                    settings.guiScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.guiAlpha,
                onChanged = value =>
                {
                    settings.guiAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawMeshSetting(GUIView view)
        {
            var position = settings.position;
            var newPosition = position;
            for (var i = 0; i < 3; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -timelineConfig.positionRange,
                    max = timelineConfig.positionRange,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.position = newPosition;
                movieManager.UpdateTransform();
            }

            var rotation = MTEP.TransformDataBase.GetNormalizedEulerAngles(settings.rotation);
            var newRotation = rotation;
            for (var i = 0; i < 3; i++)
            {
                var value = rotation[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.RotationNames[i],
                    labelWidth = 60,
                    min = -180f,
                    max = 180f,
                    step = 1f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newRotation[i] = newValue,
                });
            }

            if (newRotation != rotation)
            {
                settings.rotation = newRotation;
                movieManager.UpdateTransform();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 5f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.scale,
                onChanged = value =>
                {
                    settings.scale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.alpha,
                onChanged = value =>
                {
                    settings.alpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawBackmostSetting(GUIView view)
        {
            var position = settings.backmostPosition;
            var newPosition = position;
            for (var i = 0; i < 2; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -2f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.backmostPosition = newPosition;
                movieManager.UpdateMesh();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 2f,
                step = 0.1f,
                defaultValue = 1f,
                value = settings.backmostScale,
                onChanged = value =>
                {
                    settings.backmostScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0.5f,
                value = settings.backmostAlpha,
                onChanged = value =>
                {
                    settings.backmostAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawFrontmostSetting(GUIView view)
        {
            var position = settings.frontmostPosition;
            var newPosition = position;
            for (var i = 0; i < 2; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -2f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = i == 0 ? -0.8f : 0.8f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.frontmostPosition = newPosition;
                movieManager.UpdateMesh();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 2f,
                step = 0.1f,
                defaultValue = 0.38f,
                value = settings.frontmostScale,
                onChanged = value =>
                {
                    settings.frontmostScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.frontmostAlpha,
                onChanged = value =>
                {
                    settings.frontmostAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }
    }
}
```

- [ ] **Step 3: ウィンドウを登録する**

`Manager/WindowManager.cs` の `AddWindow(TextWindow.instance);` の直後に `AddWindow(VideoWindow.instance);` を追加。
`MenuBarWindow.cs` の `CreateWindowItem("テキスト", TextWindow.instance),` の直後に `CreateWindowItem("動画", VideoWindow.instance),` を追加。

- [ ] **Step 4: TimelineSettingWindow から動画セクションを削除する**

`TimelineSettingWindow.cs`:
- `DrawVideoSetting(view);` の呼び出しと直後の `view.DrawHorizontalLine(Color.gray);` の重複を整理 (`DrawLightToggleSection(view);` → 横線 → 「個別設定を初期化」ボタンの並びにする)
- `DrawVideoSetting` メソッド全体を削除
- `VideoDisplayTypeNames` 配列と `_videoDisplayTypeComboBox` フィールドを削除
- `private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;` を削除
- 不要になった `using` (System / System.Linq) は他で使っていれば残す (`_singleFrameTypeComboBox` 等で `Enum.GetValues` / `Cast` を使うため残る)

- [ ] **Step 5: 両構成をビルドしテストを通す**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS。`grep -n 'video\|movie' -i TimelineSettingWindow.cs` の結果が `videoPrebufferTime` (共通設定) の行だけ

- [ ] **Step 6: 実機で確認する (ゲーム起動中のみ。起動していなければスキップ)**

- Window メニューに「動画」が出て開ける
- タイムライン未読込で mp4 を選択 → GUI 表示でループ再生される
- 表示形式を切り替えると各形式のスライダーが出る

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/VideoWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(video): 動画ウィンドウを新設しタイムライン設定から動画設定を移す"
```

---

### Task 6: シーンプリセット v30 のデータ定義

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:726-739, 823-827`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs`

**Interfaces:**
- Produces:
  - `ScenePresetSound { string gameBgmFile; string bgmPath; float bpm; bool isShowBPMLine; float bpmLineOffsetFrame }`
  - `ScenePresetVideo { bool enabled; int displayType; string path; Vector3 position; Vector3 rotation; float scale; float startTime; float volume; float alpha; Vector2 guiPosition; float guiScale; float guiAlpha; Vector2 backmostPosition; float backmostScale; float backmostAlpha; Vector2 frontmostPosition; float frontmostScale; float frontmostAlpha }`
  - `ScenePresetEffects.sound : ScenePresetSound` / `ScenePresetEffects.video : ScenePresetVideo` (null = 未記録)
  - `ScenePresetData.CurrentVersion = 30`

- [ ] **Step 1: テストを追加する**

`ScenePresetEffectsTests.cs` に追加:

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
            data.effects.video = new ScenePresetVideo
            {
                enabled = false,
                displayType = 3,
                path = @"C:\movie\a.mp4",
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(4f, 5f, 6f),
                scale = 2f,
                startTime = 0.5f,
                volume = 0.25f,
                alpha = 0.75f,
                guiPosition = new Vector2(0.1f, 0.2f),
                guiScale = 0.9f,
                guiAlpha = 0.8f,
                backmostPosition = new Vector2(0.3f, 0.4f),
                backmostScale = 1.1f,
                backmostAlpha = 0.6f,
                frontmostPosition = new Vector2(-0.7f, 0.7f),
                frontmostScale = 0.35f,
                frontmostAlpha = 0.5f,
            };

            var restored = RoundTrip(data);

            Assert.Equal(30, ScenePresetData.CurrentVersion);
            Assert.NotNull(restored.effects.sound);
            Assert.Equal("BGM020.ogg", restored.effects.sound.gameBgmFile);
            Assert.Equal(@"C:\music\dance.ogg", restored.effects.sound.bgmPath);
            Assert.Equal(128f, restored.effects.sound.bpm);
            Assert.True(restored.effects.sound.isShowBPMLine);
            Assert.Equal(1.5f, restored.effects.sound.bpmLineOffsetFrame);

            Assert.NotNull(restored.effects.video);
            Assert.False(restored.effects.video.enabled);
            Assert.Equal(3, restored.effects.video.displayType);
            Assert.Equal(@"C:\movie\a.mp4", restored.effects.video.path);
            Assert.Equal(new Vector3(1f, 2f, 3f), restored.effects.video.position);
            Assert.Equal(0.5f, restored.effects.video.startTime);
            Assert.Equal(new Vector2(-0.7f, 0.7f), restored.effects.video.frontmostPosition);
            Assert.Equal(0.5f, restored.effects.video.frontmostAlpha);
        }

        [Fact]
        public void V29Preset_WithoutSoundAndVideo_ReadsThemAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            var xml = "<ScenePresetData version=\"29\" savedEffects=\"true\"><effects /></ScenePresetData>";
            using (var reader = new StringReader(xml))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.NotNull(restored.effects);
                Assert.Null(restored.effects.sound);
                Assert.Null(restored.effects.video);
            }
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetEffectsTests`
Expected: ビルドエラー (`ScenePresetSound` 未定義)

- [ ] **Step 3: データ定義を追加する**

`ScenePresetData.cs` の `ScenePresetEffects` クラスの直前 (726 行付近) に追加:

```csharp
    /// <summary>
    /// サウンド (v30)。ゲーム BGM とタイムライン BGM ファイル設定。
    /// 要素なし (null) は未記録として適用時に触らない
    /// </summary>
    public class ScenePresetSound
    {
        /// <summary>再生中のゲーム BGM のファイル名 (例 "BGM020.ogg")。空文字は無音で、適用時に停止する</summary>
        [XmlAttribute]
        public string gameBgmFile = "";

        public string bgmPath = "";
        public float bpm = 120f;
        public bool isShowBPMLine;
        public float bpmLineOffsetFrame;
    }

    /// <summary>
    /// 動画 (v30)。MTE の VideoSettings と同じ項目。
    /// 要素なし (null) は未記録として適用時に触らない
    /// </summary>
    public class ScenePresetVideo
    {
        [XmlAttribute]
        public bool enabled = true;

        /// <summary>VideoDisplayType の int 値 (GUI=0, Mesh=1, Backmost=2, Frontmost=3)</summary>
        [XmlAttribute]
        public int displayType;

        public string path = "";
        public Vector3 position;
        public Vector3 rotation;
        public float scale = 1f;
        public float startTime;
        public float volume = 0.5f;
        public float alpha = 1f;
        public Vector2 guiPosition;
        public float guiScale = 1f;
        public float guiAlpha = 1f;
        public Vector2 backmostPosition;
        public float backmostScale = 1f;
        public float backmostAlpha = 0.5f;
        public Vector2 frontmostPosition = new Vector2(-0.8f, 0.8f);
        public float frontmostScale = 0.38f;
        public float frontmostAlpha = 1f;
    }
```

`ScenePresetEffects` を次のように変更:

```csharp
    /// <summary>
    /// MTE 由来の演出状態 (v29)。テキスト / サブカメラ / サウンド (v30) / 動画 (v30)。
    /// 旧プリセット (要素なし) は null になり、適用時に触らない。
    /// リスト項目は「空リスト = 保存時に実体なし」を未記録と同義として触らない。
    /// sound / video は要素なし (null) を未記録として触らない。
    /// ポストエフェクトは PostEffects.Plugin のサイドカープリセットが担うためここには持たない
    /// </summary>
    public class ScenePresetEffects
    {
        [XmlElement("text")]
        public List<ScenePresetText> texts = new List<ScenePresetText>();

        [XmlElement("subCamera")]
        public List<ScenePresetSubCamera> subCameras = new List<ScenePresetSubCamera>();

        public ScenePresetSound sound;

        public ScenePresetVideo video;
    }
```

バージョン注記 (823 行付近の `// v29: ...` の直後) に追加し、`CurrentVersion` を 30 にする:

```csharp
        // v30: effects に sound (ゲーム BGM + タイムライン BGM ファイル設定) と video (動画設定) を追加。
        //      v9 で外したゲーム BGM を再び保存対象にする。旧形式は null で読め、適用時に触らない
        public static readonly int CurrentVersion = 30;
```

- [ ] **Step 4: 両構成をビルドしテストを通す**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "feat(preset): シーンプリセット v30 に sound/video 要素を追加する"
```

---

### Task 7: プリセットの保存・復元と UI 文言

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs:221`

**Interfaces:**
- Consumes: Task 3 の `BGMManager.settings` / `MovieManager.settings`、Task 6 の `ScenePresetSound` / `ScenePresetVideo`、`BgmUtils.EnsureSoundDataLoaded()/GetPlayingFileName()/Stop()`、`PhotoSoundData.Get(string)` / `.Play()`

- [ ] **Step 1: MteEffectsSnapshot に capture / apply を追加する**

`Manager/MteEffectsSnapshot.cs`:

static プロパティ群に追加:

```csharp
        private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;
```

`CaptureState` / `ApplyState` を次に変更:

```csharp
        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            CaptureTexts(data);
            CaptureSubCameras(data);
            data.sound = CaptureSound();
            data.video = CaptureVideo();
            return data;
        }

        /// <summary>null (旧プリセット / 未保存) なら何もしない</summary>
        public static void ApplyState(ScenePresetEffects data)
        {
            if (data == null)
            {
                return;
            }
            ApplyTexts(data);
            ApplySubCameras(data);
            ApplySound(data.sound);
            ApplyVideo(data.video);
        }
```

クラス末尾に追加:

```csharp
        private static ScenePresetSound CaptureSound()
        {
            var settings = bgmManager.settings;
            return new ScenePresetSound
            {
                // 無音は空文字。適用時に「停止」として働く
                gameBgmFile = BgmUtils.GetPlayingFileName() ?? "",
                bgmPath = settings.bgmPath,
                bpm = settings.bpm,
                isShowBPMLine = settings.isShowBPMLine,
                bpmLineOffsetFrame = settings.bpmLineOffsetFrame,
            };
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplySound(ScenePresetSound src)
        {
            if (src == null)
            {
                return;
            }

            var settings = bgmManager.settings;
            settings.bgmPath = src.bgmPath;
            settings.bpm = src.bpm;
            settings.isShowBPMLine = src.isShowBPMLine;
            settings.bpmLineOffsetFrame = src.bpmLineOffsetFrame;
            // パスが空なら Stop だけが走る
            bgmManager.Reload();

            // タイムライン BGM ファイルが読めていてタイムライン再生中なら、次フレームの
            // BGMManager.Update が Play() → SoundMgr.StopBGM でゲーム BGM を止めてしまう。
            // 一瞬鳴って止まるより復元しない方が分かりやすいので、警告を出してスキップする
            var timeline = MTEP.TimelineManager.instance.timeline;
            var isTimelinePlaying = timeline != null && timeline.defaultLayer.isAnmPlaying;
            if (bgmManager.IsLoaded() && isTimelinePlaying)
            {
                if (!string.IsNullOrEmpty(src.gameBgmFile))
                {
                    MTEUtils.LogWarning("タイムライン BGM 再生中のためゲームBGMは復元しません: {0}", src.gameBgmFile);
                }
                return;
            }

            ApplyGameBgm(src.gameBgmFile);
        }

        private static void ApplyGameBgm(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                BgmUtils.Stop();
                return;
            }

            if (!BgmUtils.EnsureSoundDataLoaded())
            {
                MTEUtils.LogWarning("BGM一覧を取得できないためゲームBGMを復元できません: {0}", fileName);
                return;
            }

            var soundData = PhotoSoundData.Get(fileName);
            if (soundData == null)
            {
                MTEUtils.LogWarning("プリセットのゲームBGMが見つかりません: {0}", fileName);
                return;
            }

            // 同じ曲が再生中でも頭出しとして再生し直す (サウンドウィンドウの一覧と同じ挙動)
            soundData.Play();
        }

        private static ScenePresetVideo CaptureVideo()
        {
            var s = movieManager.settings;
            return new ScenePresetVideo
            {
                enabled = s.enabled,
                displayType = (int)s.displayType,
                path = s.path,
                position = s.position,
                rotation = s.rotation,
                scale = s.scale,
                startTime = s.startTime,
                volume = s.volume,
                alpha = s.alpha,
                guiPosition = s.guiPosition,
                guiScale = s.guiScale,
                guiAlpha = s.guiAlpha,
                backmostPosition = s.backmostPosition,
                backmostScale = s.backmostScale,
                backmostAlpha = s.backmostAlpha,
                frontmostPosition = s.frontmostPosition,
                frontmostScale = s.frontmostScale,
                frontmostAlpha = s.frontmostAlpha,
            };
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplyVideo(ScenePresetVideo src)
        {
            if (src == null)
            {
                return;
            }

            var s = movieManager.settings;
            s.enabled = src.enabled;
            s.displayType = (MTEP.VideoDisplayType)src.displayType;
            s.path = src.path;
            s.position = src.position;
            s.rotation = src.rotation;
            s.scale = src.scale;
            s.startTime = src.startTime;
            s.volume = src.volume;
            s.alpha = src.alpha;
            s.guiPosition = src.guiPosition;
            s.guiScale = src.guiScale;
            s.guiAlpha = src.guiAlpha;
            s.backmostPosition = src.backmostPosition;
            s.backmostScale = src.backmostScale;
            s.backmostAlpha = src.backmostAlpha;
            s.frontmostPosition = src.frontmostPosition;
            s.frontmostScale = src.frontmostScale;
            s.frontmostAlpha = src.frontmostAlpha;
            // 無効やパス空なら Unload だけが走る (LoadMovie は isEnabled を見る)
            movieManager.ReloadMovie();
        }
```

`BgmUtils` / `PhotoSoundData` / `MTEUtils` は `COM3D2.SceneEditor.Plugin` 名前空間・グローバルで解決できる (SoundWindow と同じ using 構成: `using COM3D2.MotionTimelineEditor;` を追加する)。

クラス冒頭 summary を「MTE 由来の演出状態 (テキスト / サブカメラ / サウンド / 動画) のプリセット断面。」に更新する。

- [ ] **Step 2: 保存ポップアップの文言を更新する**

`SavePresetPopupWindow.cs` 221 行の `"演出 (テキスト・サブカメラ・ポストエフェクト)"` を `"演出 (テキスト・サブカメラ・サウンド・動画)"` に変更する。

- [ ] **Step 3: 両構成をビルドしテストを通す**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS

- [ ] **Step 4: 実機で確認する (ゲーム起動中のみ。起動していなければスキップ)**

- ゲーム BGM を再生し、動画パスを設定して「演出」込みでプリセット保存 → XML に `<sound gameBgmFile="...">` と `<video ...>` が出る
- BGM を停止・動画を無効にしてからプリセット読込 → BGM が再開し動画が再表示される
- タイムライン BGM ファイルを読み込んで再生中にプリセット読込 → ゲーム BGM は復元されず警告ログが出る (タイムライン BGM は鳴り続ける)
- タイムラインを閉じても再生中の BGM ファイル / 動画の表示設定がサウンド・動画ウィンドウに残っている
- v29 以前のプリセットを読み込んでも BGM / 動画が変わらない

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs
git commit -m "feat(preset): 演出カテゴリでサウンドと動画を保存・復元する"
```

---

### Task 8: 仕上げ (コードレビューと最終確認)

**Files:** 全変更ファイル

- [ ] **Step 1: 残存参照の確認**

```bash
cd source/COM3D2.SceneEditor.Plugin
grep -rn 'timeline\.\(bgmPath\|bpm\b\|isShowBPMLine\|bpmLineOffsetFrame\|video[A-Z]\)' --include=*.cs .
```
Expected: 出力なし

- [ ] **Step 2: 両構成ビルドと全テスト**

Run: MSBuild 2 本 → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 成功

- [ ] **Step 3: code-review スキルでレビューし、妥当な指摘を取り込む**

指摘を取り込んだら `fix:` / `refactor:` でコミットする。
