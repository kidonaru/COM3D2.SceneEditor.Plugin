# 連番画像出力の倍率・比率指定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 連番画像出力の画像サイズを、スクリーンショットと同じ「倍率 (1〜4x) / 比率 (画面・16:9・4:3・1:1・3:4・9:16・カスタム) / カスタムの幅・高さ」で指定できるようにし、タイムライン設定と出力前の確認ポップアップの両方で変更できるようにする。

**Architecture:** レイアウト計算はスクリーンショットの `GameViewAspect.GetCaptureLayout` をそのまま使い、倍率のクランプとカスタムサイズの丸めだけを `ImageOutputLayout.GetLayout` に足す。設定はタイムラインごと (`TimelineData` / `TimelineXml`) に持ち、既存の `imageOutputSize` はカスタムの幅・高さとして引き継ぐ。比率の既定は `Custom` にして、旧 XML・新規タイムラインとも今の出力結果を変えない。設定 UI は `ImageOutputSizeDrawer` に切り出し、タイムライン設定ウィンドウと新設の `ImageOutputPopupWindow` (確認ダイアログの置き換え) で共有する。

**Tech Stack:** C# (Unity IMGUI、COM3D2 / COM3D25 の 2 構成ビルド、C# 4 相当の制約に注意)、xunit (net48)

**Spec:** ユーザー要望 (2026-10-12 会話):「スクリーンショットと同様に倍率/比率/カスタムサイズ指定。設定画面と出力前確認ポップアップで変更可能にする」「タイムラインごとに独立して保存する」

## Global Constraints

- コードのコメント・ログ・UI 文言は日本語。コメントは原則「却下した案と起きる問題」だけ (CLAUDE.md)
- 設定はタイムラインごと。GameView の表示比率 (`config.gameViewAspectMode`) やスクショ倍率 (`config.screenshotScale`) とは共有しない
- 旧 XML (新要素なし) は `Custom` + 倍率 1 として読み、出力サイズは従来の `ImageOutputSize` そのまま
- `TimelineData.CurrentVersion` は上げない。MTE は未知要素を読み飛ばす
- 倍率の範囲は 1〜4。上限は `GameViewAspect.MAX_SCALE` (const、新設) に置き、`ScreenshotManager.MAX_SCALE` はそれを参照する
- カスタムの幅・高さは `GameViewAspect.MAX_CUSTOM_SIZE` (=8192) でクランプされる (スクショと同じ)。従来の連番出力は上限なしだったため、8192 超を指定していた旧 XML だけは出力が縮む (許容し docs に明記)
- タイムライン設定ウィンドウの既定幅は 320px (`config.timelineSettingWidth`)。UI の 1 行はこれに収める
- `MTEUtils/` (共有 submodule) は変更しない。ポップアップは SE 側に新設する
- ビルドは MSBuild 直叩きで COM3D2 → COM3D25 の順、その後 `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` (memory: msbuild-from-bash)。`debug.bat` / `deploy.bat` は使わない
- Unity ネイティブ呼び出し (`Screen.*`, `SystemInfo.*`, `GUI.*`) はテストから呼べない

## Review Focus

1. 旧 XML の読込: `ImageOutputAspectMode` が無い XML は Custom・倍率 1 で、従来と同じサイズで出力される → Task 2 のテストで固定
2. カスタム時の倍率: カスタムは指定サイズちょうどで倍率を無視する (スクショと同じ)。UI でも倍率ボタンを無効表示 → Task 1 のテストで固定
3. 範囲外の倍率 (手編集 XML の 0 や 9): 1〜4 にクランプされ RT 確保に失敗しない → Task 1 のテストで固定
4. GPU 上限超え (例: 4K 画面 ×4): `maxTextureSize` に収まるよう描画・出力を同率で縮める → Task 1 のテストで固定
5. ポップアップ表示中にタイムラインがアンロード/差し替えされた: 開始を押しても別タイムラインへ書き戻さず、出力も始めない → Task 3 の実装 (`_timeline != timelineManager.timeline` で中止) と実機確認

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/ImageOutputLayout.cs` | Modify | `GetLayout` を追加 (倍率クランプ + `GameViewAspect.GetCaptureLayout` 委譲) |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | Modify | `imageOutputScale` / `imageOutputAspectMode` の保持と XML 写し |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | Modify | `ImageOutputScale` / `ImageOutputAspectMode` 要素 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | Modify | 出力時のレイアウト計算を `GetLayout` へ、確認をポップアップへ |
| `source/COM3D2.SceneEditor.Plugin/ImageOutputSizeDrawer.cs` | Create | 倍率・比率・カスタム幅高さ・出力サイズ表示の共通 UI |
| `source/COM3D2.SceneEditor.Plugin/ImageOutputPopupWindow.cs` | Create | 出力前確認モーダル (設定を編集して「開始」) |
| `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` | Modify | 「画像サイズ」行を共通 UI に置き換え |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs` | Modify | ポップアップ登録 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | Modify | 新規 2 ファイルの Compile 登録 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ImageOutputLayoutTests.cs` | Modify | `GetLayout` のテスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/ImageOutputSettingXmlTests.cs` | Create | XML 往復・旧 XML 既定値のテスト |
| `docs-site/timeline/files.md`, `docs-site/timeline/settings.md` | Modify | ユーザー向け説明 |
| `W:\COM3D2_5\work\CLAUDE.md` (リポジトリ外) | Modify | タイムライン XML 互換節に 1 項目追記 |

---

### Task 1: レイアウト計算 `ImageOutputLayout.GetLayout`

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ImageOutputLayout.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewAspect.cs:25-26`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs:23-24`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ImageOutputLayoutTests.cs`

**Interfaces:**
- Consumes: `GameViewAspect.GetCaptureLayout(int screenWidth, int screenHeight, int scale, GameViewAspectMode mode, int customWidth, int customHeight, int maxTextureSize, out int renderWidth, out int renderHeight, out Rect cropRect)`
- Produces: `GameViewAspect.MAX_SCALE` (`public const int`, 4)。`ScreenshotManager.MAX_SCALE` は `GameViewAspect.MAX_SCALE` を指す
- Produces: `ImageOutputLayout.GetLayout(int screenWidth, int screenHeight, int scale, GameViewAspectMode mode, Vector2 customSize, int maxTextureSize, out int renderWidth, out int renderHeight, out Rect cropRect)`。出力サイズは `cropRect` のサイズ

- [ ] **Step 1: 失敗するテストを書く** (`ImageOutputLayoutTests.cs` の末尾、クラス内に追加。`using COM3D2.SceneEditor.Plugin;` をファイル先頭に追加)

```csharp
        private const int NoLimit = 100000;

        [Fact]
        public void レイアウト_カスタムは倍率を無視して指定サイズちょうど()
        {
            // 従来の出力 (21:9 画面で 1920x1080) と同じ描画・切り出しになること
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(2520, 1080, 3, GameViewAspectMode.Custom,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(2520, w);
            Assert.Equal(1080, h);
            Assert.Equal(300f, crop.x, 3);
            Assert.Equal(1920f, crop.width, 3);
            Assert.Equal(1080f, crop.height, 3);
        }

        [Fact]
        public void レイアウト_画面は画面サイズに倍率をかける()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 2, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(3840, w);
            Assert.Equal(2160, h);
            Assert.Equal(3840f, crop.width, 3);
            Assert.Equal(2160f, crop.height, 3);
        }

        [Fact]
        public void レイアウト_比率は倍率をかけた描画から中央を切り出す()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 2, GameViewAspectMode.Ratio1x1,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(3840, w);
            Assert.Equal(2160, h);
            Assert.Equal(2160f, crop.width, 3);
            Assert.Equal(2160f, crop.height, 3);
            Assert.Equal(840f, crop.x, 3);
        }

        [Theory]
        [InlineData(0, 1920)]
        [InlineData(-3, 1920)]
        [InlineData(9, 7680)]
        public void レイアウト_倍率は1から4にクランプする(int scale, int expectedWidth)
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, scale, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(expectedWidth, w);
        }

        [Fact]
        public void レイアウト_GPU上限を超える倍率は上限に収める()
        {
            // 3840x2160 ×4 = 15360 は 16384 未満、×4 で上限 8192 なら半分強に縮む
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(3840, 2160, 4, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), 8192, out w, out h, out crop);
            Assert.Equal(8192, w);
            Assert.Equal(4608, h);
            Assert.True(crop.width <= w && crop.height <= h);
        }

        [Fact]
        public void レイアウト_カスタムサイズの0以下は1に丸める()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 1, GameViewAspectMode.Custom,
                new Vector2(0, -10), NoLimit, out w, out h, out crop);
            Assert.Equal(1f, crop.width, 3);
            Assert.Equal(1f, crop.height, 3);
        }
```

- [ ] **Step 2: ビルドしてテストが失敗 (コンパイルエラー) することを確認**

Run (Git Bash、リポジトリルートで):
```bash
export MSYS2_ARG_CONV_EXCL="*"; MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m && "$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m && dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ImageOutputLayoutTests
```
Expected: テストプロジェクトのコンパイルで `ImageOutputLayout` に `GetLayout` が無いエラー

- [ ] **Step 3a: 倍率上限を GameViewAspect へ移す**

`GameViewAspect.cs` の `MAX_CUSTOM_SIZE` の直後に:
```csharp
        /// <summary>解像度倍率の上限。これ以上はメモリと撮影時間が現実的でない</summary>
        public const int MAX_SCALE = 4;
```
`ScreenshotManager.cs:23-24` を次へ置き換え (呼び出し元 SettingWindow はそのまま):
```csharp
        // static readonly の独自値にすると、連番出力 (GameViewAspect 側) と上限がずれうる
        public const int MAX_SCALE = GameViewAspect.MAX_SCALE;
```

- [ ] **Step 3: 実装** (`ImageOutputLayout.cs`。`using COM3D2.SceneEditor.Plugin;` を追加し、クラス末尾に)

```csharp
        /// <summary>
        /// 連番画像出力の描画サイズと切り出し矩形。出力サイズは cropRect のサイズ。
        /// 倍率・比率・カスタムの意味はスクリーンショットと同じ (GameViewAspect.GetCaptureLayout)
        /// </summary>
        public static void GetLayout(
            int screenWidth, int screenHeight, int scale,
            GameViewAspectMode mode, Vector2 customSize, int maxTextureSize,
            out int renderWidth, out int renderHeight, out Rect cropRect)
        {
            int customWidth, customHeight;
            ClampImageSize(customSize, out customWidth, out customHeight);
            GameViewAspect.GetCaptureLayout(
                screenWidth, screenHeight, Mathf.Clamp(scale, 1, GameViewAspect.MAX_SCALE),
                mode, customWidth, customHeight, maxTextureSize,
                out renderWidth, out renderHeight, out cropRect);
        }
```

- [ ] **Step 4: Step 2 のコマンドを再実行して PASS を確認**

Expected: `ImageOutputLayoutTests` 全 PASS

- [ ] **Step 5: コミット** (commit スキル。メッセージ例 `feat(timeline): 連番画像出力のレイアウトを倍率・比率から計算する関数を追加`)

---

### Task 2: タイムラインごとの倍率・比率の保存

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:539-541`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:474-476, 966-968, 1133-1135`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ImageOutputSettingXmlTests.cs`

**Interfaces:**
- Produces: `TimelineData.imageOutputScale` (int, 既定 1)、`TimelineData.imageOutputAspectMode` (GameViewAspectMode, 既定 Custom)、`TimelineXml.imageOutputScale` / `imageOutputAspectMode` (同既定)。`imageOutputSize` はカスタムの幅・高さとして意味を変えずに残す

- [ ] **Step 1: 失敗するテストを書く** (`ImageOutputSettingXmlTests.cs` を新規作成。テストプロジェクトは SDK 形式なので csproj 登録は不要。BodySliderRegistrationTests の XmlSerializer 往復と同じ作法)

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ImageOutputSettingXmlTests
    {
        private static TimelineXml RoundTrip(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                using (var reader = new StringReader(writer.ToString()))
                {
                    return (TimelineXml)serializer.Deserialize(reader);
                }
            }
        }

        [Fact]
        public void 倍率と比率がXMLの往復で保持される()
        {
            var xml = new TimelineXml
            {
                imageOutputScale = 3,
                imageOutputAspectMode = GameViewAspectMode.Ratio9x16,
            };
            var loaded = RoundTrip(xml);
            Assert.Equal(3, loaded.imageOutputScale);
            Assert.Equal(GameViewAspectMode.Ratio9x16, loaded.imageOutputAspectMode);
        }

        [Fact]
        public void 要素の無い旧XMLはカスタムと倍率1で読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineData><ImageOutputSize><x>1280</x><y>720</y></ImageOutputSize></TimelineData>";
            var serializer = new XmlSerializer(typeof(TimelineXml));
            TimelineXml loaded;
            using (var reader = new StringReader(text))
            {
                loaded = (TimelineXml)serializer.Deserialize(reader);
            }
            Assert.Equal(GameViewAspectMode.Custom, loaded.imageOutputAspectMode);
            Assert.Equal(1, loaded.imageOutputScale);
            Assert.Equal(1280f, loaded.imageOutputSize.x);
            Assert.Equal(720f, loaded.imageOutputSize.y);
        }
    }
}
```

ルート要素名は `TimelineXml` の `[XmlRoot("TimelineData")]` (TimelineXml.cs:250) に合わせている。

- [ ] **Step 2: Task 1 Step 2 のビルド + `dotnet test ... --filter ImageOutputSettingXmlTests` で失敗を確認**

Expected: `imageOutputScale` / `imageOutputAspectMode` が無いコンパイルエラー

- [ ] **Step 3: `TimelineXml.cs` に要素を追加** (`ImageOutputSize` の直後。`using COM3D2.SceneEditor.Plugin;` は既にある)

```csharp
        [XmlElement("ImageOutputScale")]
        public int imageOutputScale = 1;

        // 既定を Screen にすると、要素の無い旧 XML の出力サイズが ImageOutputSize から画面サイズへ変わる
        [XmlElement("ImageOutputAspectMode")]
        public GameViewAspectMode imageOutputAspectMode = GameViewAspectMode.Custom;
```

- [ ] **Step 4: `TimelineData.cs` に保持と写しを追加**

フィールド (474-476 の後):
```csharp
        public int imageOutputScale = 1;
        public GameViewAspectMode imageOutputAspectMode = GameViewAspectMode.Custom;
```
FromXml (968 の後):
```csharp
            imageOutputScale = xml.imageOutputScale;
            imageOutputAspectMode = xml.imageOutputAspectMode;
```
ToXml (1135 の後):
```csharp
            xml.imageOutputScale = imageOutputScale;
            xml.imageOutputAspectMode = imageOutputAspectMode;
```
`TimelineData.cs` には既に `using COM3D2.SceneEditor.Plugin;` がある。

- [ ] **Step 5: ビルド + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` 全体が PASS することを確認**

- [ ] **Step 6: コミット** (`feat(timeline): 連番画像出力の倍率と比率をタイムラインに保存する`)

---

### Task 3: 共通 UI・確認ポップアップ・出力処理の切り替え

UI は IMGUI でテスト不可のため、ビルドと実機確認で検証する。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ImageOutputSizeDrawer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/ImageOutputPopupWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs:391-417`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:1221-1240, 1301-1305`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:138`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 1 `ImageOutputLayout.GetLayout`、Task 2 の `TimelineData` フィールド
- Produces:
  - `ImageOutputSizeDrawer.Draw(GUIView view, ref int scale, ref GameViewAspectMode mode, ref Vector2 customSize)`
  - `ImageOutputSizeDrawer.GetHeight(GameViewAspectMode mode)` (float、Draw が進める高さ)
  - `ImageOutputSizeDrawer.GetOutputSize(int scale, GameViewAspectMode mode, Vector2 customSize, out int width, out int height)`
  - `ImageOutputPopupWindow.Show(TimelineData timeline, string message, Action onStart)`

- [ ] **Step 1: `ImageOutputSizeDrawer.cs` を作成**

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 連番画像出力の倍率・比率・カスタムサイズの入力欄と出力サイズ表示。
    /// タイムライン設定ウィンドウと出力前ポップアップで共有する
    /// </summary>
    public static class ImageOutputSizeDrawer
    {
        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 60;
        private static readonly int SCALE_BUTTON_WIDTH = 40;
        private static readonly int ASPECT_BUTTON_WIDTH = 52;
        /// <summary>比率ボタンの 1 行目の個数。残りは 2 行目</summary>
        private static readonly int ASPECT_FIRST_ROW_COUNT = 4;
        private static readonly int INT_FIELD_WIDTH = 80;

        /// <summary>Draw が描く高さ (行数 × (行高 + margin))</summary>
        public static float GetHeight(GameViewAspectMode mode)
        {
            // 倍率 1 行・比率 2 行・出力サイズ 1 行 + カスタムの幅・高さ 2 行
            var rows = mode == GameViewAspectMode.Custom ? 6 : 4;
            return rows * (ROW_HEIGHT + GUIView.defaultMargin);
        }

        /// <summary>現在の画面サイズと GPU 上限で出力した場合の解像度</summary>
        public static void GetOutputSize(
            int scale, GameViewAspectMode mode, Vector2 customSize, out int width, out int height)
        {
            int renderWidth, renderHeight;
            Rect cropRect;
            MTEP.ImageOutputLayout.GetLayout(
                Screen.width, Screen.height, scale, mode, customSize, SystemInfo.maxTextureSize,
                out renderWidth, out renderHeight, out cropRect);
            width = (int)cropRect.width;
            height = (int)cropRect.height;
        }

        public static void Draw(
            GUIView view, ref int scale, ref GameViewAspectMode mode, ref Vector2 customSize)
        {
            var newScale = scale;
            var newMode = mode;
            var newSize = customSize;

            // カスタムは指定サイズちょうどで出力し倍率を使わないため、その間は倍率を無効表示にする。
            // 行全体を BeginEnabled で囲むと、DrawButton が終わるたびに GUI.enabled を戻すため 2 個目以降が有効に見える
            var scaleEnabled = mode != GameViewAspectMode.Custom;
            view.BeginHorizontal();
            {
                view.DrawLabel("倍率", LABEL_WIDTH, ROW_HEIGHT);
                for (var s = 1; s <= GameViewAspect.MAX_SCALE; s++)
                {
                    var isSelected = scale == s;
                    if (view.DrawButton(s + "x", SCALE_BUTTON_WIDTH, ROW_HEIGHT,
                        scaleEnabled, null, isSelected ? GUIView.gsSelectedButton : null) && !isSelected)
                    {
                        newScale = s;
                    }
                }
            }
            view.EndLayout();

            // コンボボックスにすると、ドロップダウンが別ウィンドウのためモーダルの中から開けない。
            // 1 行に 7 個並べると既定幅 320px のタイムライン設定ウィンドウからはみ出す
            var modes = GameViewAspect.modes;
            for (var row = 0; row < 2; row++)
            {
                var start = row == 0 ? 0 : ASPECT_FIRST_ROW_COUNT;
                var end = row == 0 ? ASPECT_FIRST_ROW_COUNT : modes.Length;
                view.BeginHorizontal();
                {
                    view.DrawLabel(row == 0 ? "比率" : "", LABEL_WIDTH, ROW_HEIGHT);
                    for (var i = start; i < end; i++)
                    {
                        var m = modes[i];
                        var isSelected = mode == m;
                        if (view.DrawButton(GameViewAspect.GetDisplayName(m), ASPECT_BUTTON_WIDTH, ROW_HEIGHT,
                            true, null, isSelected ? GUIView.gsSelectedButton : null) && !isSelected)
                        {
                            newMode = m;
                        }
                    }
                }
                view.EndLayout();
            }

            if (mode == GameViewAspectMode.Custom)
            {
                view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "幅",
                    labelWidth = LABEL_WIDTH,
                    value = Mathf.RoundToInt(customSize.x),
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + INT_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    onChanged = value => newSize.x = value,
                });
                view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "高さ",
                    labelWidth = LABEL_WIDTH,
                    value = Mathf.RoundToInt(customSize.y),
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + INT_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    onChanged = value => newSize.y = value,
                });
            }

            int width, height;
            GetOutputSize(scale, mode, customSize, out width, out height);
            view.BeginHorizontal();
            {
                view.DrawLabel("出力サイズ", LABEL_WIDTH, ROW_HEIGHT);
                view.DrawLabel(width + " x " + height, -1, ROW_HEIGHT);
            }
            view.EndLayout();

            scale = newScale;
            mode = newMode;
            customSize = newSize;
        }
    }
}
```

`GUIView.IntFieldOption` の `minValue` / `maxValue` / `labelWidth` のフィールド名は `SettingWindow.cs:206-235` の使い方と同じ。`DrawButton` の引数順は `SettingWindow.cs:178-181` と同じ。

- [ ] **Step 2: `TimelineSettingWindow.cs` の「画像サイズ」行 (391-417 の `BeginHorizontal` 〜 `EndLayout`) を置き換え**

```csharp
            ImageOutputSizeDrawer.Draw(view,
                ref timeline.imageOutputScale,
                ref timeline.imageOutputAspectMode,
                ref timeline.imageOutputSize);
```

- [ ] **Step 3: `ImageOutputPopupWindow.cs` を作成** (SavePresetPopupWindow と同じ IMGUI モーダル方式。WINDOW_ID 8903392 はワークスペース全体で未使用を確認済み)

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 連番画像出力の開始確認。倍率・比率・カスタムサイズをその場で変えられる。
    /// 編集値は「開始」を押したときだけタイムラインへ書き戻す
    /// </summary>
    public class ImageOutputPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903392;

        private static readonly int WINDOW_WIDTH = 360;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;
        private static readonly int BUTTON_SPACING = 10;

        /// <summary>開始時の応答。null なら閉じている</summary>
        private Action _onStart;
        private MTEP.TimelineData _timeline;
        private string _message = "";

        private int _scale;
        private GameViewAspectMode _aspectMode;
        private Vector2 _customSize;

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private readonly GUIView _view = new GUIView();

        private static GUIStyle _messageStyle;

        /// <summary>メッセージ用の中央揃えスタイル。GUIStyle は OnGUI 中でしか作れないため遅延生成する</summary>
        private static GUIStyle messageStyle
        {
            get
            {
                if (_messageStyle == null)
                {
                    _messageStyle = new GUIStyle(GUIView.gsLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                    };
                }
                return _messageStyle;
            }
        }

        private static ImageOutputPopupWindow _instance = null;
        public static ImageOutputPopupWindow instance
            => _instance ?? (_instance = new ImageOutputPopupWindow());

        public bool isOpen => _onStart != null;

        public static void Show(MTEP.TimelineData timeline, string message, Action onStart)
        {
            var window = instance;
            window._timeline = timeline;
            window._message = message;
            window._onStart = onStart;
            window._scale = timeline.imageOutputScale;
            window._aspectMode = timeline.imageOutputAspectMode;
            window._customSize = timeline.imageOutputSize;
        }

        private void ConfirmStart()
        {
            var timeline = _timeline;
            var onStart = _onStart;
            Close();

            // 表示中にアンロード・読込で差し替わったら、別のタイムラインへ書き戻さない
            if (timeline == null || timeline != MTEP.TimelineManager.instance.timeline)
            {
                return;
            }

            timeline.imageOutputScale = _scale;
            timeline.imageOutputAspectMode = _aspectMode;
            timeline.imageOutputSize = _customSize;
            onStart();
        }

        public void Init()
        {
        }

        public void Update()
        {
        }

        public void Close()
        {
            _onStart = null;
            _timeline = null;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        /// <summary>シーンが変わると出力対象の状況も変わるため、応答は呼ばず破棄する</summary>
        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Close();
        }

        public void OnGUI()
        {
            if (_onStart == null)
            {
                return;
            }

            var contentWidth = WINDOW_WIDTH - PADDING * 2;
            var messageHeight = GUIView.CalcHeight(messageStyle, _message, contentWidth);
            // 要素ごとに margin が 1 回入る (AddSpace も 1 要素)。実機で下端余白を見て過不足を確認する
            var windowHeight = PADDING
                + messageHeight + GUIView.defaultMargin
                + BUTTON_SPACING + GUIView.defaultMargin
                + ImageOutputSizeDrawer.GetHeight(_aspectMode)
                + BUTTON_SPACING + GUIView.defaultMargin
                + BUTTON_HEIGHT + PADDING;
            _windowRect = GUIScale.CenterOnScreen(WINDOW_WIDTH, windowHeight);

            // ModalWindow で背後のウィンドウ操作をブロックする
            GUIScale.ModalWindow(WINDOW_ID, _windowRect, DrawWindow, "", GUIView.gsWin);
            GUI.BringWindowToFront(WINDOW_ID);
        }

        private void DrawWindow(int id)
        {
            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));
            _view.currentPos = new Vector2(PADDING, PADDING);

            var contentWidth = _windowRect.width - PADDING * 2;
            var messageHeight = GUIView.CalcHeight(messageStyle, _message, contentWidth);
            _view.DrawLabel(_message, contentWidth, messageHeight, style: messageStyle);

            _view.AddSpace(BUTTON_SPACING);

            ImageOutputSizeDrawer.Draw(_view, ref _scale, ref _aspectMode, ref _customSize);

            _view.AddSpace(BUTTON_SPACING);

            _view.BeginHorizontal();
            {
                _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH * 2 - 10) / 2;

                if (_view.DrawButton("開始", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    ConfirmStart();
                }

                if (_view.DrawButton("キャンセル", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    Close();
                }
            }
            _view.EndLayout();
        }
    }
}
```

`MTEP.TimelineManager.instance` は TimelineManager.cs:143 の静的プロパティ。`GUIView.CalcHeight` / `DrawLabel(..., style:)` は `DialogPopupWindow.cs:200, 235` と同じ使い方。

- [ ] **Step 4: `WindowManager.cs` に登録** (`AddWindow(AboutPopupWindow.instance);` の直後、DialogPopupWindow より前。出力開始後のエラー通知がポップアップより手前に出るように)

```csharp
            AddWindow(ImageOutputPopupWindow.instance);
```

- [ ] **Step 5: `TimelineManager.OutputImage` を置き換え** (1221-1240)

```csharp
        public void OutputImage()
        {
            if (timeline == null)
            {
                return;
            }

            var message = "連番画像出力を開始しますか？\n出力中は[Esc]キーで停止できます";

            // 出力先は OutputImageInternal が中身ごと削除して作り直すため、事前に知らせる
            var outputDir = PluginUtils.GetImageOutputDirPath(timeline.anmName);
            if (Directory.Exists(outputDir))
            {
                message += "\n\n既存の出力先を中身ごと削除します\n" + outputDir;
            }

            SE.ImageOutputPopupWindow.Show(timeline, message, () =>
            {
                MTEUtils.Log("連番画像出力を開始しました");
                GameMain.Instance.StartCoroutine(OutputImageInternal());
            });
        }
```

(`SE` エイリアスはこのファイルで既に `SE.ScreenshotManager` として使われている)

- [ ] **Step 6: `OutputImageInternal` のレイアウト計算 (1301-1305) を置き換え**

```csharp
            int renderWidth, renderHeight;
            Rect cropRect;
            ImageOutputLayout.GetLayout(
                Screen.width, Screen.height, timeline.imageOutputScale,
                timeline.imageOutputAspectMode, timeline.imageOutputSize, SystemInfo.maxTextureSize,
                out renderWidth, out renderHeight, out cropRect);
            var imageWidth = (int)cropRect.width;
            var imageHeight = (int)cropRect.height;
```

`imageWidth` / `imageHeight` は後段の `new Texture2D(imageWidth, imageHeight, ...)` でそのまま使う。メソッド先頭の `<summary>` の「画面と同じアスペクトで描いて中央を切り出すため」は、比率指定時も中央切り出しなので変更不要。

- [ ] **Step 7: csproj に Compile 登録** (`SavePresetPopupWindow.cs` の行の近く、アルファベット順の位置に)

```xml
    <Compile Include="ImageOutputPopupWindow.cs" />
    <Compile Include="ImageOutputSizeDrawer.cs" />
```

- [ ] **Step 8: COM3D2 → COM3D25 ビルド + 全テスト**

Task 1 Step 2 のコマンドから `--filter` を外して実行。Expected: 両構成ビルド成功、テスト全 PASS

- [ ] **Step 9: 実機確認** (restart-verify スキル。通常シーンでエディタを有効化。撮影モードは非対応 — memory: verify-in-normal-scene)

1. タイムライン設定 → 個別 → 連番画像出力設定に 倍率 / 比率 (2 行) / (カスタム時) 幅・高さ / 出力サイズ が出る。既定幅 320px で行がはみ出さず「カスタム」の文字が切れない。カスタム中は倍率の 1x〜4x **4 個すべて**が無効表示
   - ポップアップの下端余白が他のポップアップと同程度 (高さ計算が過大・過小でない)
2. ファイル → 連番画像出力 でポップアップが出て、同じ欄を変えると出力サイズ表示が追従する。キャンセルでタイムライン設定の値は変わらない
3. 比率 1:1・倍率 2x で開始し、短いトラックで出力 → PNG のサイズが表示どおり (devbridge の eval で `System.Drawing` 不可なら PNG ヘッダを読むか、エクスプローラーのプロパティで確認)
4. カスタム 1920x1080 (旧既定) で出力して従来どおりのサイズ・構図
5. 保存して再読込で倍率・比率が残る
6. 画面 × 3x 程度の大サイズで数十フレーム出力が完走する

- [ ] **Step 10: コミット** (`feat(timeline): 連番画像出力の倍率・比率を設定と出力前ポップアップで指定できるようにする`)

---

### Task 4: ドキュメント

**Files:**
- Modify: `docs-site/timeline/files.md:86-94`
- Modify: `docs-site/timeline/settings.md:28`
- Modify: `W:\COM3D2_5\work\CLAUDE.md` (タイムライン XML 互換節。リポジトリ外なのでコミット対象外)

- [ ] **Step 1: `files.md` の連番画像出力節を更新**

確認文の説明を「確認ポップアップが出ます。ポップアップで倍率・比率・カスタムサイズを変えてから `開始` できます (変更はタイムラインに保存されます)」に変え、箇条書きの 2〜4 項目目を次へ差し替える:

```markdown
- フレームレートと `出力名`（既定 `image_{frame:D6}`）は `タイムライン設定` → `個別` の `連番画像出力設定` で変更します
- 画像サイズはスクリーンショットと同じく `倍率`（1x〜4x）と `比率`（画面 / 16:9 / 4:3 / 1:1 / 3:4 / 9:16 / カスタム）で決まります。`カスタム` は指定した幅・高さちょうど（各 8192 まで）で出力し、倍率は使いません。既定はカスタム 1920 × 1080 です
- 大きな倍率は 1 フレームごとのメモリと書き出し時間が大きく増えます。まず短いトラックで試してください
- 比率が画面と違う場合は、画面と同じ縦横比で描いて中央を切り出します。GPU の上限を超えるサイズは縦横比を保って縮めます
- アクティブトラックがあるとその範囲だけ出力します
```

- [ ] **Step 2: `settings.md` の表の `画像サイズ` 行を差し替え**

```markdown
| `倍率` | 1x | 連番画像の解像度倍率（画面サイズまたは比率で切り出したサイズに掛ける）。`カスタム` の間は無効 |
| `比率` | カスタム | 連番画像の縦横比。`画面` / `16:9` / `4:3` / `1:1` / `3:4` / `9:16` / `カスタム` |
| `幅` / `高さ` | 1920 × 1080 | `カスタム` のときの解像度（各 8192 まで） |
```

- [ ] **Step 3: `W:\COM3D2_5\work\CLAUDE.md` の互換節末尾に追記**

```markdown
- 連番画像出力の倍率・比率（ルートの `ImageOutputScale` / `ImageOutputAspectMode`）は SE 独自。version は上げていない。要素の無い旧 XML は倍率 1・`Custom` で読み、`ImageOutputSize` をカスタムの幅・高さとして従来どおり出力する（ただし各 8192 でクランプ）。MTE は要素を読み飛ばし `ImageOutputSize` で出力する
```

- [ ] **Step 4: コミット** (docs-site のみ。`docs(timeline): 連番画像出力の倍率・比率指定を説明する`)

## レビュー却下メモ

- TimelineData.FromXml → ToXml の往復テストを足す — 未確認のまま見送り（TimelineData の生成が Unity ネイティブに触れないか未確認。写しは 2 行ずつで、実機の保存・再読込確認で拾う）
- 既存スクショ UI (SettingWindow.cs:170) も倍率ボタンの無効表示が 1 個目にしか効かない — スコープ外。ユーザーへ報告する
