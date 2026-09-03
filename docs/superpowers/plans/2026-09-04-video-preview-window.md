# 動画プレビューウィンドウ 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 再生中の動画テクスチャをアスペクト維持で表示する「動画プレビュー」ウィンドウを追加し、動画ウィンドウの「プレビュー」ボタンでトグルできるようにする。

**Architecture:** `MoviePlayerImpl` が保持する AVPro `MediaPlayer` の `TextureProducer` から現在フレームの `Texture` を公開し、`MovieManager` 経由で新設の `VideoPreviewWindow : EditorSubWindow` が `GUI.DrawTextureWithTexCoords` で描く。表示形式 (GUI / 3D / 最背面 / 最前面) に依存せず、動画が読み込まれていれば常に表示できる。ウィンドウの登録・メニュー・config 保存は既存の `VideoWindow` と同じ作法に揃える。

**Tech Stack:** C# (.NET 3.5 / 4.7.1 の 2 構成)、UnityInjector、IMGUI (`GUIView`)、AVProVideo (`RenderHeads.Media.AVProVideo`、ゲーム同梱)

**Spec:** ユーザー要望 (本ファイル冒頭 Goal)。仕様書は無し

## Global Constraints

- COM3D2 (.NET 3.5) と COM3D25 (.NET 4.7.1) の **2 構成を必ず両方ビルド**する。`debug.bat` は実機へ DLL をコピーするため使わず MSBuild を直接叩く:
  ```
  cd source/COM3D2.SceneEditor.Plugin
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- 単体テストは IMGUI/Unity 依存のため対象外。ビルド成功と実機確認 (ゲーム起動中なら devbridge) で検証する
- コードのコメント・ログ文言は日本語
- 新ウィンドウ ID は `8903397` (既存最大は 8903396)
- git worktree は使わない

---

### Task 1: MoviePlayerImpl / MovieManager から動画テクスチャを公開する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs` (`duration` プロパティ付近)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs` (`frameRate` プロパティ付近)

**Interfaces:**
- Produces: `MoviePlayerImpl.texture : Texture` (未取得なら null)、`MoviePlayerImpl.requiresVerticalFlip : bool`、`MovieManager.texture : Texture`、`MovieManager.requiresVerticalFlip : bool`

- [ ] **Step 1: MoviePlayerImpl にプロパティを追加**

`public float frameRate => _frameRate;` の直後に追加:

```csharp
        /// <summary>現在フレームの動画テクスチャ。メタデータ未取得や未読込時は null</summary>
        public Texture texture
        {
            get
            {
                var producer = _mediaPlayer != null ? _mediaPlayer.TextureProducer : null;
                return producer != null ? producer.GetTexture() : null;
            }
        }

        /// <summary>プラットフォームによってテクスチャが上下反転しているため、描画側で補正する</summary>
        public bool requiresVerticalFlip
        {
            get
            {
                var producer = _mediaPlayer != null ? _mediaPlayer.TextureProducer : null;
                return producer != null && producer.RequiresVerticalFlip();
            }
        }
```

- [ ] **Step 2: MovieManager に委譲プロパティを追加**

`public float frameRate { ... }` の直後に追加:

```csharp
        /// <summary>プレビュー表示用の動画テクスチャ。未読込時は null</summary>
        public Texture texture
        {
            get => _moviePlayerImpl != null ? _moviePlayerImpl.texture : null;
        }

        public bool requiresVerticalFlip
        {
            get => _moviePlayerImpl != null && _moviePlayerImpl.requiresVerticalFlip;
        }
```

- [ ] **Step 3: 両構成をビルドして成功を確認**

- [ ] **Step 4: コミット**

```
feat(video): MovieManager から動画テクスチャを公開する
```

---

### Task 2: VideoPreviewWindow を新設して登録する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:209-213` の直後
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:65` の直後
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:131` の直後

**Interfaces:**
- Consumes: `MovieManager.texture` / `MovieManager.requiresVerticalFlip` (Task 1)
- Produces: `VideoPreviewWindow.instance` (Task 3 でトグル対象に使う)

- [ ] **Step 1: Config に配置フィールドを追加**

`public bool videoVisible = false;` の直後:

```csharp

        // 動画プレビューウィンドウ
        public int videoPreviewPosX = -1;
        public int videoPreviewPosY = -1;
        public int videoPreviewWidth = 480;
        public int videoPreviewHeight = 270;
        public bool videoPreviewVisible = false;
```

- [ ] **Step 2: VideoPreviewWindow.cs を作成**

```csharp
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 再生中の動画をウィンドウ内に表示するプレビュー。
    /// 表示形式 (GUI / 3D / 最背面 / 最前面) に関係なく、MediaPlayer のテクスチャを直接描くため
    /// ゲーム画面上で見えにくい配置でも内容を確認できる
    /// </summary>
    public class VideoPreviewWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903397;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "動画プレビュー";

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>動画未読込時の背景 (レターボックスと共通)</summary>
        private static readonly Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;

        private readonly GUIView _view = new GUIView();

        private static VideoPreviewWindow _instance = null;
        public static VideoPreviewWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VideoPreviewWindow();
                }
                return _instance;
            }
        }

        private VideoPreviewWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.videoPreviewPosX;
            y = config.videoPreviewPosY;
            width = config.videoPreviewWidth;
            height = config.videoPreviewHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.videoPreviewPosX = x;
            config.videoPreviewPosY = y;
            config.videoPreviewWidth = width;
            config.videoPreviewHeight = height;
        }

        public override bool savedVisible
        {
            get => config.videoPreviewVisible;
            set => config.videoPreviewVisible = value;
        }

        protected override void DrawContent()
        {
            var localRect = ToLocalRect(contentRect);

            var prevColor = GUI.color;
            GUI.color = BackgroundColor;
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            // メタデータ確定前はサイズ 0 のダミーが返ることがあり、そのままだと FitRect が NaN になる
            var texture = movieManager.texture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                _view.Init(localRect);
                _view.DrawLabel("動画が読み込まれていません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            var drawRect = FitRect(localRect, (float)texture.width / texture.height);

            // MediaFoundation 等ではテクスチャが上下反転しているため UV 側で戻す
            var texCoords = movieManager.requiresVerticalFlip
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords, false);
        }

        /// <summary>領域内にアスペクト比を保って収まる中央寄せ矩形を返す</summary>
        private static Rect FitRect(Rect area, float aspectRatio)
        {
            var width = area.width;
            var height = width / aspectRatio;
            if (height > area.height)
            {
                height = area.height;
                width = height * aspectRatio;
            }
            return new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);
        }
    }
}
```

- [ ] **Step 3: WindowManager / MenuBarWindow に登録**

`Manager/WindowManager.cs` の `AddWindow(VideoWindow.instance);` の直後に:

```csharp
            AddWindow(VideoPreviewWindow.instance);
```

`MenuBarWindow.cs` の `CreateWindowItem("動画", VideoWindow.instance),` の直後に:

```csharp
                        CreateWindowItem("動画プレビュー", VideoPreviewWindow.instance),
```

- [ ] **Step 4: 両構成をビルドして成功を確認**

- [ ] **Step 5: コミット**

```
feat(video): 再生中の動画を表示する動画プレビューウィンドウを追加する
```

---

### Task 3: 動画ウィンドウにプレビューのトグルボタンを追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs:127-143` (`DrawVideoSetting` 先頭)

**Interfaces:**
- Consumes: `VideoPreviewWindow.instance` (Task 2)、`WindowManager.ToggleWindowVisible(EditorSubWindow)` (既存)

- [ ] **Step 1: 「有効」トグルと同じ行にプレビュートグルを置く**

`DrawVideoSetting` 冒頭の `view.DrawToggle("有効", ...)` を横並びに包み、右側にプレビュートグルを追加する:

```csharp
            view.BeginHorizontal();
            {
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

                // プレビューウィンドウの表示切替。メニューバーの Window 項目と同じ経路で開閉する
                var previewWindow = VideoPreviewWindow.instance;
                view.DrawToggle("プレビュー", previewWindow.isShowWnd, 90, ROW_HEIGHT, _ =>
                {
                    WindowManager.ToggleWindowVisible(previewWindow);
                });
            }
            view.EndLayout();
```

- [ ] **Step 2: 両構成をビルドして成功を確認**

- [ ] **Step 3: 実機確認 (ゲーム起動中のみ)**

devbridge `ping` が通れば、`debug.bat` は使わずビルド済み DLL の反映はユーザーに任せる。起動していなければスキップし、報告に明記する。

- [ ] **Step 4: コミット**

```
feat(video): 動画ウィンドウにプレビューウィンドウのトグルを追加する
```

## レビュー却下メモ

- `m_Resample` 経路の未考慮 — 本プラグインは `m_Resample` を設定せず既定 false のまま。既存の `ApplyToMaterial` と同じ経路で表示しており、現状で実害なし（未確認のまま見送りではなく要件外）
- `FitRect` と GameViewWindow のレターボックス計算の重複 — GameView 側は Screen 比率と余白塗りが絡み形が違う。1 か所の 10 行なので共通化は見送り
