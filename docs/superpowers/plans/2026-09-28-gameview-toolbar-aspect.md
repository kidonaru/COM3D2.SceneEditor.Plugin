# GameView ツールバー・表示比率・非表示トグル Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GameView に、マウスオーバー時だけ出る左上ツールバー (撮影・表示比率・背景 / モデル / PNG の非表示) を追加する。表示比率は RT を画面解像度のまま中央で切り出して表示・撮影する。

**Architecture:**
- 比率の計算は純関数の `GameViewAspect` に集め、GameView の表示 (`GUI.DrawTextureWithTexCoords` で RT の中央だけ描く)、マウス座標変換、画面分割グリッド、スクリーンショットの出力サイズがすべてそこを参照する。RT とメインカメラは今のまま画面解像度なので、ゲーム側の `UltimateOrbitCamera` (画面中央を注視点とみなす) の前提は崩れない
- 非表示は SceneView 用のカリングフィルタを汎用化した `ViewCullingFilter` をメインカメラにも付ける。メインカメラの描画中だけ Renderer を切るので、通常表示・スクリーンショット・連番出力のすべてに同じく効き、SceneView やサブカメラには影響しない
- ツールバーの部品 (帯・アイコントグル・アイコンボタン) は SceneView から `ViewToolbarDrawer` へ切り出して共有する

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48), Node (アイコン生成 `assets/icons/generate.js`)

**Spec:** 本計画の「仕様」節 (機能要望の調査結果に対するユーザー回答: 「RT は現状のままで、GameView に描画するときにクリッピング (比率も対応)」「透過撮影は撮影時だけでなく通常の表示も対応。SceneView 同様にツールバーに隠す用のボタンを追加」、元要望「GameView 窓の左上に SceneView 同様にツールバーを配置して、撮影ボタンとサイズ変更 (GameView にマウスオーバーしないと表示されないように)」)

## 仕様

### 表示比率

- 選択肢: `画面` (切り出しなし、既定) / `16:9` / `4:3` / `1:1` / `3:4` / `9:16` / `カスタム` (幅 × 高さを数値指定)
- RT (`GameViewManager.renderTexture`) は画面解像度のまま作る。比率は GameView への描画時に RT の中央を切り出して表す。比率が画面より横長なら上下を、縦長なら左右を切る
- GameView ウィンドウ内では、切り出した絵をそのアスペクトで表示領域に収め、余りは従来どおり背景色のレターボックス
- マウス座標の変換 (カメラ操作・ドラッグ点・ギズモ・ボーン選択) は切り出し後の表示領域を基準にする。切り出しの外側 (レターボックス) はシーン操作の対象外
- 画面分割グリッド (構図用) は切り出し範囲の中を等分する
- 最大化・ウィンドウ一時非表示 (直接描画) 中は切り出さない (画面全体)。ツールバーも出ない
- スクリーンショットの出力サイズ:
  - `画面`: 従来どおり 画面解像度 × 倍率
  - 比率指定: 画面を比率で切り出した範囲 × 倍率 (例: 1920×1080 画面で 3:4、2 倍 → 1620×2160)
  - `カスタム`: 指定した幅 × 高さちょうど (倍率は使わない)。画面アスペクトで描いて中央を切り出す (連番出力の `ImageOutputLayout` と同じ方式なので、GameView の見た目と同じ構図になる)
  - いずれも描画サイズの長辺が GPU の上限 (`SystemInfo.maxTextureSize`) を超えたら、描画・出力の両方を同率で縮める (従来の縮め方と同じ)
- 設定は Config に保存する (`gameViewAspectMode` / `gameViewCustomWidth` 既定 1080 / `gameViewCustomHeight` 既定 1920)。カスタムの幅・高さは 1〜8192
- 設定ウィンドウのスクリーンショット節に「比率」コンボとカスタムの幅・高さ入力を足し、既存の「出力サイズ」表示は新しい計算結果を出す
- プリセットのサムネイル撮影 (`ThumbnailCapture`) と連番画像出力のサイズ指定は変えない

### 非表示トグル

- 対象: `背景` (BgMgr.BgObject 配下。背景モデルの複製も配下にある) / `モデル` (MTE の StudioModel) / `PNG` (PNG 配置の各ルート)
- メインカメラの描画中だけ Renderer を切る。通常表示・スクリーンショット・連番出力に同じく効く。SceneView・サブカメラ・ゲーム本体の状態 (Renderer.enabled の恒久値) は変えない
- 状態は保存しない (エディタ有効化のたびに全表示へ戻す)。ツールバーはマウスオーバー時しか見えないので、隠したことを忘れたまま次回起動で「背景が出ない」状態にしないため
- 背景を隠した部分はメインカメラのクリア色 (= 背景色) になる。透過 PNG にしたいときは従来どおり背景色のアルファを下げる (撮影時の差分合成は既存の `CaptureTransparent`)
- 隠したものは影も落とさない (メインカメラのシャドウマップ描画も同じ区間で行われるため)
- プリセット・タイムラインのサムネイル撮影 (`ThumbnailCapture`) は非表示を無視して全部写す (一覧で見分けるための画像なので)。撮影中だけフィルタのコンポーネントを無効にする

### ツールバー

- GameView ウィンドウの表示領域の左上 (ヘッダーの直下) に、SceneView と同じ半透明の帯で置く
- 並び: `撮影` ボタン / 比率コンボ / `背景` `モデル` `PNG` トグル (トグルは ON = 表示)
- 表示条件: マウスが GameView ウィンドウ上にあり他の IMGUI ウィンドウに覆われていないとき、または比率コンボのポップアップを開いているとき
- ツールバーの帯の上はシーン操作の対象外 (`InputRemapper.IsGameViewActiveAt` から除外)。帯の矩形は描画結果ではなく常に計算で求め、マウスが入った最初のフレームのクリックもシーンへ通さない
- `撮影` は `ScreenshotManager.Capture()` と同じ (ツールバー自体は IMGUI なので写らない)
- アイコンを 2 つ追加する: `Screenshot` (カメラ) / `Png` (角を折った画像カード)。背景・モデルは既存の `Bg` / `Model` を使う

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

## Review Focus

1. 切り出し中に GameView 上のドラッグ点・ギズモ・ボーン選択・カメラ回転が、見た目どおりの位置で反応する (座標変換が `GuiToRtPoint` 1 箇所に集約されていること。`InputRemapper.MousePositionPostfix` の重複計算を消す) — Task 2 の実機確認
2. `カスタム` で出力したスクリーンショットの構図が GameView の見た目と一致する (切り出しの計算が表示と撮影で同じ関数) — Task 1 のテスト「カスタムの切り出しは表示の UV と同じ範囲」、Task 3 の実機確認
3. GPU 上限を超える指定 (例: カスタム 8192×8192 で上限 8192 の環境、比率指定 4 倍の 4K 画面) でも例外にならず同率で縮む — Task 1 のテスト
4. ツールバーのボタンを押したとき、同じクリックでカメラが回ったりメイドが選択されたりしない (帯の矩形を入力判定から除外) — Task 6 の実機確認
5. 背景を隠したまま SceneView を開いても SceneView 側は SceneView 自身のトグルに従う (フィルタはカメラごとのインスタンス) — Task 4 の実機確認
6. 背景を隠したままプリセット・タイムラインのサムネイルを保存しても、サムネイルには全部写る (`ThumbnailCapture` もメインカメラを直接描くため) — Task 4 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/Manager/GameViewAspect.cs` | 比率の列挙・表示名・切り出し UV・撮影レイアウト (純関数) |
| Modify `source/COM3D2.SceneEditor.Plugin/Config.cs` | 比率設定 3 項目 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs` | `cropUV`、非表示状態、フィルタの付け外し |
| Modify `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs` | 切り出し描画・座標変換・ツールバー |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs` | 座標変換の一本化・ツールバー除外 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs` | 画面分割グリッドの範囲 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs` | 撮影レイアウト |
| Modify `source/COM3D2.SceneEditor.Plugin/SettingWindow.cs` | 比率・カスタムサイズの設定 UI |
| Rename `Manager/SceneViewCullingFilter.cs` → `Manager/ViewCullingFilter.cs` | PNG の追加、クラス名の汎用化 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs` | 型名の追随 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/ThumbnailCapture.cs` | サムネイル撮影中はフィルタを止める |
| Create `source/COM3D2.SceneEditor.Plugin/ViewToolbarDrawer.cs` | ツールバー部品 |
| Modify `source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs` | 部品の共有化 |
| Modify `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs`、Create `assets/icons/Screenshot.svg` `assets/icons/Png.svg`、Modify `assets/icons/generate.js` | アイコン |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/GameViewAspectTests.cs` | 比率計算のテスト |
| Modify `docs-site/guide/getting-started.md` ほか (Task 7 で特定) | 説明 |

---

### Task 1: 比率の計算を純関数にする

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewAspect.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs` (GameView の節、`gameViewMaximized` の下)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/GameViewAspectTests.cs`

**Interfaces:**
- Produces:
  - `enum GameViewAspectMode { Screen, Ratio16x9, Ratio4x3, Ratio1x1, Ratio3x4, Ratio9x16, Custom }`
  - `GameViewAspect.modes : GameViewAspectMode[]` (コンボの並び順)
  - `GameViewAspect.GetDisplayName(GameViewAspectMode) : string`
  - `GameViewAspect.TryGetAspect(GameViewAspectMode mode, int customWidth, int customHeight, out float aspect) : bool` (`Screen` は false)
  - `GameViewAspect.GetCropUV(float screenAspect, float targetAspect) : Rect` (0〜1 の中央寄せ矩形)
  - `GameViewAspect.GetCaptureLayout(int screenWidth, int screenHeight, int scale, GameViewAspectMode mode, int customWidth, int customHeight, int maxTextureSize, out int renderWidth, out int renderHeight, out Rect cropRect)` (cropRect は描画結果上のピクセル矩形。出力サイズ = cropRect のサイズ)
  - `GameViewAspect.MAX_CUSTOM_SIZE = 8192`
  - `Config.gameViewAspectMode` / `Config.gameViewCustomWidth` / `Config.gameViewCustomHeight`

- [ ] **Step 1: 失敗するテストを書く**

`GameViewAspectTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>GameView の表示比率の切り出しと撮影サイズを固定する</summary>
    public class GameViewAspectTests
    {
        private const int NoLimit = 100000;

        [Fact]
        public void 比率_画面は切り出しなし()
        {
            float aspect;
            Assert.False(GameViewAspect.TryGetAspect(GameViewAspectMode.Screen, 1080, 1920, out aspect));
        }

        [Fact]
        public void 比率_カスタムは幅と高さの比()
        {
            float aspect;
            Assert.True(GameViewAspect.TryGetAspect(GameViewAspectMode.Custom, 1080, 1920, out aspect));
            Assert.Equal(1080f / 1920f, aspect, 5);
        }

        [Fact]
        public void 比率_カスタムの0以下は1として扱う()
        {
            float aspect;
            Assert.True(GameViewAspect.TryGetAspect(GameViewAspectMode.Custom, 0, -5, out aspect));
            Assert.Equal(1f, aspect, 5);
        }

        [Fact]
        public void 切り出し_縦長の比率は左右を切る()
        {
            // 16:9 の画面から 9:16 → 幅 (9/16)/(16/9) = 0.3164 を中央に
            var uv = GameViewAspect.GetCropUV(16f / 9f, 9f / 16f);
            Assert.Equal(0.31640625f, uv.width, 5);
            Assert.Equal(1f, uv.height, 5);
            Assert.Equal((1f - 0.31640625f) * 0.5f, uv.x, 5);
            Assert.Equal(0f, uv.y, 5);
        }

        [Fact]
        public void 切り出し_横長の比率は上下を切る()
        {
            // 4:3 の画面から 16:9 → 高さ (4/3)/(16/9) = 0.75
            var uv = GameViewAspect.GetCropUV(4f / 3f, 16f / 9f);
            Assert.Equal(1f, uv.width, 5);
            Assert.Equal(0.75f, uv.height, 5);
            Assert.Equal(0.125f, uv.y, 5);
        }

        [Fact]
        public void 撮影_画面は画面サイズ掛ける倍率()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 2, GameViewAspectMode.Screen, 0, 0, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(3840, rw);
            Assert.Equal(2160, rh);
            Assert.Equal(new Rect(0, 0, 3840, 2160), crop);
        }

        [Fact]
        public void 撮影_比率は切り出し範囲掛ける倍率()
        {
            // 1920x1080 の 3:4 → 810x1080、2 倍で 1620x2160
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 2, GameViewAspectMode.Ratio3x4, 0, 0, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(3840, rw);
            Assert.Equal(2160, rh);
            Assert.Equal(1620f, crop.width);
            Assert.Equal(2160f, crop.height);
            Assert.Equal((3840f - 1620f) * 0.5f, crop.x);
        }

        [Fact]
        public void 撮影_カスタムは指定サイズちょうどで倍率を使わない()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 4, GameViewAspectMode.Custom, 1080, 1920, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(1080f, crop.width);
            Assert.Equal(1920f, crop.height);
            // 画面アスペクト (16:9) で高さ 1920 を覆う → 幅 3413
            Assert.Equal(3413, rw);
            Assert.Equal(1920, rh);
        }

        [Fact]
        public void 撮影_カスタムの切り出しは表示のUVと同じ範囲()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 1, GameViewAspectMode.Custom, 1080, 1920, NoLimit,
                out rw, out rh, out crop);
            var uv = GameViewAspect.GetCropUV(1920f / 1080f, 1080f / 1920f);
            Assert.Equal(uv.x, crop.x / rw, 2);
            Assert.Equal(uv.width, crop.width / rw, 2);
        }

        [Fact]
        public void 撮影_GPU上限を超えたら描画と出力を同率で縮める()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(3840, 2160, 4, GameViewAspectMode.Ratio9x16, 0, 0, 8192,
                out rw, out rh, out crop);
            Assert.True(Mathf.Max(rw, rh) <= 8192);
            Assert.True(crop.width <= rw && crop.height <= rh);
            Assert.True(crop.x >= 0f && crop.y >= 0f);
            // 9:16 のまま
            Assert.Equal(9f / 16f, crop.width / crop.height, 2);
        }

        [Fact]
        public void 撮影_カスタムが上限を超えても例外にならない()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 1, GameViewAspectMode.Custom, 8192, 8192, 8192,
                out rw, out rh, out crop);
            Assert.True(Mathf.Max(rw, rh) <= 8192);
            Assert.Equal(crop.width, crop.height, 0);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter GameViewAspectTests`
Expected: コンパイルエラー (`GameViewAspect` が無い)

- [ ] **Step 3: 実装する**

`Manager/GameViewAspect.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>GameView の表示比率。Config に保存するため値の並びは変えないこと</summary>
    public enum GameViewAspectMode
    {
        Screen,
        Ratio16x9,
        Ratio4x3,
        Ratio1x1,
        Ratio3x4,
        Ratio9x16,
        Custom,
    }

    /// <summary>
    /// GameView の表示比率の計算。RT は画面解像度のまま描き、表示と撮影で中央を切り出す。
    /// 表示・マウス座標変換・画面分割グリッド・撮影がすべてここを通るので、
    /// 見た目と撮影結果の構図が食い違わない
    /// </summary>
    public static class GameViewAspect
    {
        /// <summary>カスタムサイズの上限。GPU の上限とは別に、入力ミスで巨大な撮影をしないための枠</summary>
        public const int MAX_CUSTOM_SIZE = 8192;

        /// <summary>コンボの並び順</summary>
        public static readonly GameViewAspectMode[] modes =
        {
            GameViewAspectMode.Screen,
            GameViewAspectMode.Ratio16x9,
            GameViewAspectMode.Ratio4x3,
            GameViewAspectMode.Ratio1x1,
            GameViewAspectMode.Ratio3x4,
            GameViewAspectMode.Ratio9x16,
            GameViewAspectMode.Custom,
        };

        public static string GetDisplayName(GameViewAspectMode mode)
        {
            switch (mode)
            {
                case GameViewAspectMode.Ratio16x9: return "16:9";
                case GameViewAspectMode.Ratio4x3: return "4:3";
                case GameViewAspectMode.Ratio1x1: return "1:1";
                case GameViewAspectMode.Ratio3x4: return "3:4";
                case GameViewAspectMode.Ratio9x16: return "9:16";
                case GameViewAspectMode.Custom: return "カスタム";
                default: return "画面";
            }
        }

        /// <summary>切り出す比率 (幅 / 高さ)。画面全体 (切り出しなし) なら false</summary>
        public static bool TryGetAspect(
            GameViewAspectMode mode, int customWidth, int customHeight, out float aspect)
        {
            switch (mode)
            {
                case GameViewAspectMode.Ratio16x9: aspect = 16f / 9f; return true;
                case GameViewAspectMode.Ratio4x3: aspect = 4f / 3f; return true;
                case GameViewAspectMode.Ratio1x1: aspect = 1f; return true;
                case GameViewAspectMode.Ratio3x4: aspect = 3f / 4f; return true;
                case GameViewAspectMode.Ratio9x16: aspect = 9f / 16f; return true;
                case GameViewAspectMode.Custom:
                    aspect = (float)Mathf.Max(customWidth, 1) / Mathf.Max(customHeight, 1);
                    return true;
                default:
                    aspect = 0f;
                    return false;
            }
        }

        /// <summary>画面から targetAspect を中央で切り出す範囲 (0〜1)。横長なら上下を、縦長なら左右を切る</summary>
        public static Rect GetCropUV(float screenAspect, float targetAspect)
        {
            if (targetAspect >= screenAspect)
            {
                var height = screenAspect / targetAspect;
                return new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }

            var width = targetAspect / screenAspect;
            return new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        /// <summary>
        /// 撮影の描画サイズと切り出し矩形。出力サイズは cropRect のサイズ。
        /// カスタムは指定サイズちょうどを出すため倍率を使わず、画面アスペクトで描いて中央を切り出す
        /// (連番出力と同じ ImageOutputLayout の方式)。
        /// 描画サイズが GPU の上限を超えると RenderTexture を確保できないため、描画・出力を同率で縮める
        /// </summary>
        public static void GetCaptureLayout(
            int screenWidth, int screenHeight, int scale,
            GameViewAspectMode mode, int customWidth, int customHeight, int maxTextureSize,
            out int renderWidth, out int renderHeight, out Rect cropRect)
        {
            screenWidth = Mathf.Max(screenWidth, 1);
            screenHeight = Mathf.Max(screenHeight, 1);
            var screenAspect = (float)screenWidth / screenHeight;

            int outputWidth, outputHeight;
            float aspect;
            if (mode == GameViewAspectMode.Custom)
            {
                outputWidth = Mathf.Clamp(customWidth, 1, MAX_CUSTOM_SIZE);
                outputHeight = Mathf.Clamp(customHeight, 1, MAX_CUSTOM_SIZE);
                ImageOutputLayout.GetRenderSize(screenAspect, new Vector2(outputWidth, outputHeight),
                    out renderWidth, out renderHeight);
            }
            else
            {
                renderWidth = screenWidth * scale;
                renderHeight = screenHeight * scale;
                outputWidth = renderWidth;
                outputHeight = renderHeight;
                if (TryGetAspect(mode, customWidth, customHeight, out aspect))
                {
                    var uv = GetCropUV(screenAspect, aspect);
                    outputWidth = Mathf.Max(Mathf.RoundToInt(renderWidth * uv.width), 1);
                    outputHeight = Mathf.Max(Mathf.RoundToInt(renderHeight * uv.height), 1);
                }
            }

            // 縦横を個別にクランプすると絵が歪むので、はみ出した分の比率を全辺へ等しくかける
            var longest = Mathf.Max(renderWidth, renderHeight);
            if (longest > maxTextureSize)
            {
                var ratio = (float)maxTextureSize / longest;
                renderWidth = Mathf.Max(Mathf.RoundToInt(renderWidth * ratio), 1);
                renderHeight = Mathf.Max(Mathf.RoundToInt(renderHeight * ratio), 1);
                outputWidth = Mathf.Clamp(Mathf.RoundToInt(outputWidth * ratio), 1, renderWidth);
                outputHeight = Mathf.Clamp(Mathf.RoundToInt(outputHeight * ratio), 1, renderHeight);
            }

            cropRect = ImageOutputLayout.GetCropRect(renderWidth, renderHeight, outputWidth, outputHeight);
        }
    }
}
```

`Config.cs` の `gameViewMaximized` の下に追加する:

```csharp
        // GameView の表示比率。RT は画面解像度のまま、表示とスクリーンショットだけ中央を切り出す
        public GameViewAspectMode gameViewAspectMode = GameViewAspectMode.Screen;
        // 比率「カスタム」の幅・高さ (px)。スクリーンショットはこのサイズちょうどで出力する
        public int gameViewCustomWidth = 1080;
        public int gameViewCustomHeight = 1920;
```

csproj の `<Compile Include="Manager\GameViewManager.cs" />` の直前に `<Compile Include="Manager\GameViewAspect.cs" />` を足す。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter GameViewAspectTests` が PASS。落ちたテストは期待値の算術を先に見直し、仕様 (出力サイズの定義) を変えないこと。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GameViewAspect.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/GameViewAspectTests.cs
git commit -m "feat(gameview): 表示比率の切り出しと撮影サイズの計算を追加する"
```

### Task 2: GameView を比率で切り出して表示する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs:60-87,172-189,290-313`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs:171-199`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs:233-269`

**Interfaces:**
- Consumes: `GameViewAspect.TryGetAspect`, `GameViewAspect.GetCropUV`
- Produces: `GameViewManager.cropUV : Rect` (直接描画中は `(0,0,1,1)`)、`GridRenderer.getDisplayArea : Func<Rect>` (null なら全面)

- [ ] **Step 1: Context7 で `GUI.DrawTextureWithTexCoords(Rect, Texture, Rect, bool alphaBlend)` のシグネチャと texCoords の原点 (左下) を確認する**

- [ ] **Step 2: GameViewManager に cropUV を足す**

`isGizmoDispatchActive` の上に追加する:

```csharp
        /// <summary>
        /// RT のうち GameView に表示する範囲 (0〜1、中央寄せ)。
        /// 直接描画中は画面へそのまま描くので切り出さない
        /// </summary>
        public Rect cropUV
        {
            get
            {
                float aspect;
                if (isDirectRender || !GameViewAspect.TryGetAspect(config.gameViewAspectMode,
                        config.gameViewCustomWidth, config.gameViewCustomHeight, out aspect))
                {
                    return new Rect(0f, 0f, 1f, 1f);
                }
                return GameViewAspect.GetCropUV((float)Screen.width / Screen.height, aspect);
            }
        }
```

`AttachGizmoRenderer` の `displayGridRenderer.drawDisplayGrid = true;` の下に足す:

```csharp
            // 比率で切り出している間は、切り出した範囲を等分する
            displayGridRenderer.getDisplayArea = () => cropUV;
```

- [ ] **Step 3: GameViewWindow の drawRect・描画・座標変換を切り出し基準にする**

`drawRect` を置き換える:

```csharp
        /// <summary>
        /// 実際にRTを描画する矩形 (スクリーンGUI座標)。
        /// RT のうち表示範囲 (cropUV) のアスペクトを保って viewRect 内に収めるため、
        /// 縦横比が合わない分は余白 (レターボックス) になる。
        /// picking の座標変換もこの矩形を基準にする
        /// </summary>
        public Rect drawRect
        {
            get
            {
                var view = viewRect;
                var crop = gameViewManager.cropUV;
                var displayAspect = (float)Screen.width * crop.width / (Screen.height * crop.height);

                var width = view.width;
                var height = width / displayAspect;
                if (height > view.height)
                {
                    height = view.height;
                    width = height * displayAspect;
                }

                return new Rect(
                    view.x + (view.width - width) * 0.5f,
                    view.y + (view.height - height) * 0.5f,
                    width,
                    height);
            }
        }
```

`DrawWindow` の `GUI.DrawTexture(draw, rt, ScaleMode.StretchToFill, false);` を置き換える (直前のコメントは残す):

```csharp
                GUI.DrawTextureWithTexCoords(draw, rt, gameViewManager.cropUV, false);
```

`GuiToRtPoint` の RT 描画中の変換 (`return new Vector2(...)` の部分) を置き換える:

```csharp
            // drawRect 内の相対位置 → 表示範囲 (cropUV) 内の位置 → RT ピクセル (左下原点)
            var crop = gameViewManager.cropUV;
            var u = crop.x + (guiPos.x - rect.x) / rect.width * crop.width;
            var v = crop.y + (1f - (guiPos.y - rect.y) / rect.height) * crop.height;
            return new Vector2(u * rt.width, v * rt.height);
```

summary の「レターボックスの余白を除いた drawRect が基準」はそのまま、「表示範囲の切り出しも反映する」を 1 文足す。

- [ ] **Step 4: InputRemapper の座標変換を GuiToRtPoint へ一本化する**

`MousePositionPostfix` の `// 描画領域内 → RTピクセル座標 (左下原点)` 以降を置き換える:

```csharp
            // 描画領域内 → RTピクセル座標 (左下原点)。表示範囲の切り出しを含め GameViewWindow と同じ変換を使う
            var rtPoint = GameViewWindow.instance.GuiToRtPoint(guiPos);
            __result = new Vector3(rtPoint.x, rtPoint.y, 0f);
```

(直前の `rt == null` の早期 return は `GuiToRtPoint` も同じ判定を持つが、ここでは座標を書き換えない条件として残す)

- [ ] **Step 5: 画面分割グリッドを表示範囲に合わせる**

`GridRenderer` の `drawDisplayGrid` の下にフィールドを足す (using System; が無ければ追加):

```csharp
        /// <summary>
        /// 画面分割グリッドを引く範囲 (0〜1)。null なら画面全体。
        /// GameView が比率で切り出している間は、見えている範囲を等分するために使う
        /// </summary>
        public Func<Rect> getDisplayArea = null;
```

`DrawDisplayGrid` のループを範囲つきにする (線幅の換算と Begin/End はそのまま):

```csharp
            var area = getDisplayArea != null ? getDisplayArea() : new Rect(0f, 0f, 1f, 1f);

            for (var i = 1; i < count; i++)
            {
                var x = area.x + area.width * i / count;
                var y = area.y + area.height * i / count;

                GL.Vertex3(x - halfWidthX, area.yMin, 0f);
                GL.Vertex3(x + halfWidthX, area.yMin, 0f);
                GL.Vertex3(x + halfWidthX, area.yMax, 0f);
                GL.Vertex3(x - halfWidthX, area.yMax, 0f);

                GL.Vertex3(area.xMin, y - halfWidthY, 0f);
                GL.Vertex3(area.xMin, y + halfWidthY, 0f);
                GL.Vertex3(area.xMax, y + halfWidthY, 0f);
                GL.Vertex3(area.xMax, y - halfWidthY, 0f);
            }
```

summary を「画面 (または getDisplayArea の範囲) を等分する構図用グリッド」に直す。

- [ ] **Step 6: 両構成をビルドし、全テストを通す**

- [ ] **Step 7: 実機確認 (Review Focus 1)**

devbridge の `eval_csharp` で `ConfigManager.instance.config.gameViewAspectMode` を `Ratio9x16` にして、GameView の表示 (`screenshot`) が縦長の中央切り出しになること、切り出し内の四隅付近でドラッグ点・カメラ回転が見た目どおりに反応することを確認する。`Screen` に戻すと従来表示。画面分割グリッドを ON にして切り出し範囲が 3 等分されること。

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs
git commit -m "feat(gameview): 表示比率で RT の中央を切り出して表示する"
```

### Task 3: スクリーンショットを比率で撮る

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs:31-49,93-116,157-248`
- Modify: `source/COM3D2.SceneEditor.Plugin/SettingWindow.cs:120-157`

**Interfaces:**
- Consumes: `GameViewAspect.GetCaptureLayout`, `GameViewAspect.modes/GetDisplayName/MAX_CUSTOM_SIZE`
- Produces: `ScreenshotManager.GetCaptureLayout(out int renderWidth, out int renderHeight, out Rect cropRect)`、`ScreenshotManager.GetCaptureSize(out int width, out int height)` (出力サイズ = cropRect のサイズ。既存名のまま意味を出力サイズに揃える)

- [ ] **Step 1: 撮影サイズの取得を置き換える**

`GetCaptureSize` を置き換える:

```csharp
        /// <summary>
        /// 現在の設定での撮影の描画サイズと切り出し矩形。
        /// GameView の表示比率に合わせて中央を切り出す (計算は GameViewAspect.GetCaptureLayout)
        /// </summary>
        public static void GetCaptureLayout(out int renderWidth, out int renderHeight, out Rect cropRect)
        {
            GameViewAspect.GetCaptureLayout(
                Screen.width, Screen.height, Mathf.Clamp(config.screenshotScale, 1, MAX_SCALE),
                config.gameViewAspectMode, config.gameViewCustomWidth, config.gameViewCustomHeight,
                SystemInfo.maxTextureSize,
                out renderWidth, out renderHeight, out cropRect);
        }

        /// <summary>現在の設定で撮影した場合の出力解像度。設定ウィンドウの表示に使う</summary>
        public static void GetCaptureSize(out int width, out int height)
        {
            int renderWidth, renderHeight;
            Rect cropRect;
            GetCaptureLayout(out renderWidth, out renderHeight, out cropRect);
            width = (int)cropRect.width;
            height = (int)cropRect.height;
        }
```

- [ ] **Step 2: Capture を描画サイズと切り出しで撮る**

`Capture` の try 内先頭を置き換える:

```csharp
                int renderWidth, renderHeight;
                Rect cropRect;
                GetCaptureLayout(out renderWidth, out renderHeight, out cropRect);
                // RT 描画には QualitySettings の MSAA が反映されないため、画面と同じ段数を明示する
                renderTexture = RenderTexture.GetTemporary(
                    renderWidth, renderHeight, 24,
                    RenderTextureFormat.Default, RenderTextureReadWrite.Default,
                    Mathf.Max(1, QualitySettings.antiAliasing));
```

`CaptureTransparent(...)` / `CaptureOpaque(...)` の呼び出しに `cropRect` を末尾引数で渡す。

`CaptureOpaque` のシグネチャに `Rect cropRect` を足し、読み出しを切り出しにする:

```csharp
            RenderTexture.active = renderTexture;
            var texture = new Texture2D((int)cropRect.width, (int)cropRect.height,
                TextureFormat.RGB24, false);
            texture.ReadPixels(cropRect, 0, 0);
            texture.Apply();
            return texture;
```

`CaptureTransparent` と `RenderAndRead` にも `Rect cropRect` を足し、`RenderAndRead` の Texture2D を cropRect のサイズで作って `ReadPixels(cropRect, 0, 0)`、`CaptureTransparent` の出力 Texture2D も cropRect のサイズで作る。summary の「メインカメラを一時 RT へ描画するため…」の後に「GameView の表示比率に合わせて中央を切り出す」を足す。

- [ ] **Step 3: 設定ウィンドウに比率とカスタムサイズを足す**

`SettingWindow` のフィールドにコンボを足す (既存のコンボ宣言の近く。`_rootView` があれば ProcessFocus 済みか確認し、無ければ BackgroundWindow と同じく `DrawContent` 末尾で `ComboBoxPopupWindow.instance.ProcessFocus(<ルートビュー>, this)` を呼ぶ):

```csharp
        private readonly GUIComboBox<GameViewAspectMode> _aspectComboBox = new GUIComboBox<GameViewAspectMode>
        {
            items = new List<GameViewAspectMode>(GameViewAspect.modes),
            getName = (mode, _) => GameViewAspect.GetDisplayName(mode),
            buttonSize = new Vector2(100, 20),
            contentSize = new Vector2(100, 160),
        };
```

`DrawScreenshotSection` の倍率行の後、出力サイズ行の前に足す:

```csharp
            _view.BeginHorizontal();
            {
                _view.DrawLabel("比率", LABEL_WIDTH, ROW_HEIGHT);
                _aspectComboBox.currentIndex = Array.IndexOf(GameViewAspect.modes, config.gameViewAspectMode);
                _aspectComboBox.onSelected = (mode, _) =>
                {
                    config.gameViewAspectMode = mode;
                    config.dirty = true;
                };
                _aspectComboBox.DrawButton(_view);
            }
            _view.EndLayout();

            if (config.gameViewAspectMode == GameViewAspectMode.Custom)
            {
                _view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "幅",
                    labelWidth = LABEL_WIDTH,
                    value = config.gameViewCustomWidth,
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + 80,
                    height = ROW_HEIGHT,
                    onChanged = value =>
                    {
                        config.gameViewCustomWidth = value;
                        config.dirty = true;
                    },
                });
                _view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "高さ",
                    labelWidth = LABEL_WIDTH,
                    value = config.gameViewCustomHeight,
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + 80,
                    height = ROW_HEIGHT,
                    onChanged = value =>
                    {
                        config.gameViewCustomHeight = value;
                        config.dirty = true;
                    },
                });
            }
```

倍率ボタン列は `カスタム` のとき使われないので、その間は `_view.BeginEnabled(config.gameViewAspectMode != GameViewAspectMode.Custom)` / `EndEnabled()` で無効表示にする。`using System;` と `using System.Collections.Generic;` が無ければ足す。`DrawIntField` の fieldCache は label で引かれるため、同じ label (`幅` / `高さ`) を他の節で使っていないか `rg -n "label = \"幅\"" source/COM3D2.SceneEditor.Plugin/SettingWindow.cs` で確かめ、衝突するなら `fieldCache` を専用に持つ。

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

- [ ] **Step 5: 実機確認 (Review Focus 2)**

`16:9` 以外の各比率と `カスタム 1080×1920` でスクリーンショットを撮り、`ScreenShot` フォルダの PNG のサイズが設定ウィンドウの「出力サイズ」と一致し、構図が GameView の表示と一致することを確認する。背景色のアルファを下げた透過撮影でも同じ。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs source/COM3D2.SceneEditor.Plugin/SettingWindow.cs
git commit -m "feat(screenshot): GameView の表示比率とカスタムサイズで撮影する"
```

### Task 4: 背景・モデル・PNG をメインカメラから隠す

**Files:**
- Rename: `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewCullingFilter.cs` → `Manager/ViewCullingFilter.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:236`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs:17,161`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ThumbnailCapture.cs:53-78`

**Interfaces:**
- Produces: `ViewCullingFilter` (`hideBg` / `hideMaid` / `hideModel` / `hidePng`、`InvalidateCache()`)、`GameViewManager.showBg` / `showModel` / `showPng` (bool プロパティ、setter でフィルタへ反映)

- [ ] **Step 1: リネームする**

```bash
git mv source/COM3D2.SceneEditor.Plugin/Manager/SceneViewCullingFilter.cs source/COM3D2.SceneEditor.Plugin/Manager/ViewCullingFilter.cs
```

クラス名を `ViewCullingFilter` にし、csproj の Include と `SceneViewManager` の型名 2 箇所を直す。summary を「アタッチしたカメラの描画中だけ背景 / メイド / モデル / PNG のレンダラーを無効化するフィルタ。SceneView カメラとメインカメラ (GameView の非表示トグル) で使う。OnPreCull/OnPostRender はアタッチ先カメラの描画時にのみ呼ばれるため、他のカメラの描画には影響しない」に直す (GameObject の非アクティブ化をしない理由の文は残す)。

- [ ] **Step 2: PNG を足す**

フィールド・キャッシュ・無効化を既存と同じ形で足す:

```csharp
        public bool hidePng = false;
        private readonly List<Renderer> _pngRenderers = new List<Renderer>();
        private bool _pngCacheValid = false;
```

`InvalidateCache` に `_pngCacheValid = false;`、`OnPreCull` に

```csharp
            if (hidePng)
            {
                DisableRenderers(_pngRenderers, ref _pngCacheValid, CollectPngRenderers);
            }
```

収集関数:

```csharp
        /// <summary>PNG 配置の各ルート配下のレンダラーを集める</summary>
        private static void CollectPngRenderers(List<Renderer> results)
        {
            foreach (var png in PngPlacementManager.instance.pngObjects)
            {
                if (png != null && png.rootObject != null)
                {
                    results.AddRange(png.rootObject.GetComponentsInChildren<Renderer>(true));
                }
            }
        }
```

(`PngPlacementManager.instance` と `pngObjects` / `PngObjectData.rootObject` の実際の名前とアクセス修飾子を `Manager/PngPlacementManager.cs:23-64,147-160` で確かめてから書く)

- [ ] **Step 3: GameViewManager にトグル状態とフィルタを持たせる**

フィールド・プロパティを足す (`displayGridRenderer` の下):

```csharp
        /// <summary>メインカメラの描画から背景・モデル・PNG を隠すフィルタ。通常表示と撮影の両方に効く</summary>
        public ViewCullingFilter cullingFilter { get; private set; }

        // GameView ツールバーの表示トグル。隠したまま忘れて次回起動しないよう保存せず、
        // エディタ有効化のたびに全表示へ戻す
        private bool _showBg = true;
        private bool _showModel = true;
        private bool _showPng = true;

        public bool showBg
        {
            get => _showBg;
            set { _showBg = value; ApplyCullingSettings(); }
        }

        public bool showModel
        {
            get => _showModel;
            set { _showModel = value; ApplyCullingSettings(); }
        }

        public bool showPng
        {
            get => _showPng;
            set { _showPng = value; ApplyCullingSettings(); }
        }

        private void ApplyCullingSettings()
        {
            if (cullingFilter == null)
            {
                return;
            }
            cullingFilter.hideBg = !_showBg;
            cullingFilter.hideModel = !_showModel;
            cullingFilter.hidePng = !_showPng;
            cullingFilter.InvalidateCache();
        }
```

`AttachGizmoRenderer` の `worldGridRenderer` の生成の後に:

```csharp
            // 非表示トグルはメインカメラの描画だけに効かせる (SceneView は自分のフィルタを持つ)
            cullingFilter = camera.gameObject.AddComponent<ViewCullingFilter>();
            ApplyCullingSettings();
```

`DetachGizmoRenderer` に他と同じ形で `cullingFilter` の Destroy と null 代入を足す。`EnterWindowMode` の先頭 (`isWindowMode` の早期 return の後) で `_showBg = _showModel = _showPng = true;` として毎回全表示から始める。

- [ ] **Step 4: サムネイル撮影では非表示を無視する**

`Manager/ThumbnailCapture.cs` の `CaptureToFile` で、`camera.Render()` の前後にフィルタを止めて戻す (無効な MonoBehaviour には OnPreCull が届かない):

```csharp
            // サムネイルは一覧で見分けるための画像なので、GameView の非表示トグルを無視して全部写す
            var cullingFilter = GameViewManager.instance.cullingFilter;
            var filterWasEnabled = cullingFilter != null && cullingFilter.enabled;
            ...
            try
            {
                if (filterWasEnabled)
                {
                    cullingFilter.enabled = false;
                }
                camera.targetTexture = renderTexture;
                camera.Render();
                ...
            }
            finally
            {
                if (filterWasEnabled)
                {
                    cullingFilter.enabled = true;
                }
                ...
            }
```

(`...` は既存コードのまま。宣言は try の外、既存の `savedTargetTexture` の並びに置く)

- [ ] **Step 5: 両構成をビルドし、全テストを通す**

- [ ] **Step 6: 実機確認 (Review Focus 5・6)**

devbridge で `GameViewManager.instance.showBg = false` などを順に切り替え、GameView の `screenshot` で背景・モデル・PNG が消えて背景色になること、SceneView を開いたときは SceneView 側のトグルに従うこと、スクリーンショットにも反映されることを確認する。背景を隠したままシーンプリセットのサムネイルを保存し、サムネイルには背景が写ること。`showBg = true` で元へ戻ること。

- [ ] **Step 7: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin/Manager source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(gameview): 背景・モデル・PNG をメインカメラの描画から隠せるようにする"
```

### Task 5: ツールバー部品を共有化し、アイコンを足す

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ViewToolbarDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs:22-32,46-51,72-77,283-433`
- Modify: `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs`
- Create: `assets/icons/Screenshot.svg`, `assets/icons/Png.svg`
- Modify: `assets/icons/generate.js`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces:
  - `ViewToolbarDrawer.TOOLBAR_HEIGHT` (24) / `ITEM_HEIGHT` (20) / `ITEM_MARGIN` (2) / `TEXT_ITEM_WIDTH` (72)
  - `ViewToolbarDrawer.CreateView(float paddingX) : GUIView`
  - `ViewToolbarDrawer.GetItemWidth(Texture2D icon) : float`
  - `ViewToolbarDrawer.DrawBackground(Rect localRect)`
  - `ViewToolbarDrawer.DrawToggle(GUIView view, Texture2D icon, string label, bool value, Action<bool> onChanged)`
  - `ViewToolbarDrawer.DrawButton(GUIView view, Texture2D icon, string label) : bool`
  - `ToolbarIcons.Kind.Screenshot` / `ToolbarIcons.Kind.Png`

- [ ] **Step 1: ViewToolbarDrawer を作る**

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView・GameView のシーン描画に重ねるツールバーの部品。
    /// アイコンは正方形、読み込めなかったときは固定幅のテキストにフォールバックする
    /// </summary>
    public static class ViewToolbarDrawer
    {
        public static readonly int TOOLBAR_HEIGHT = 24;
        public static readonly int ITEM_HEIGHT = 20;
        public static readonly int ITEM_MARGIN = 2;
        // アイコン読み込み失敗時のテキスト項目の幅
        public static readonly int TEXT_ITEM_WIDTH = 72;
        // アイコンをボタン枠より少し小さく描くための余白 (両側合計)
        private const float ICON_OFFSET = 4f;
        // シーン描画に重ねる帯の色
        private static readonly Color BG_COLOR = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>項目を横並びに置くビュー。帯の高さの中で縦中央に揃える</summary>
        public static GUIView CreateView(float paddingX)
        {
            return new GUIView
            {
                padding = new Vector2(paddingX, (TOOLBAR_HEIGHT - ITEM_HEIGHT) * 0.5f),
                margin = ITEM_MARGIN,
            };
        }

        public static float GetItemWidth(Texture2D icon)
        {
            return icon != null ? ITEM_HEIGHT : TEXT_ITEM_WIDTH;
        }

        /// <summary>半透明の帯を敷く (ウィンドウローカル座標)</summary>
        public static void DrawBackground(Rect localRect)
        {
            var prevColor = GUI.color;
            GUI.color = BG_COLOR;
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        public static void DrawToggle(
            GUIView view, Texture2D icon, string label, bool value, Action<bool> onChanged)
        {
            if (icon != null)
            {
                view.DrawToggle(icon, value, ITEM_HEIGHT, ITEM_HEIGHT, onChanged, ICON_OFFSET, label);
            }
            else
            {
                view.DrawToggle(label, value, TEXT_ITEM_WIDTH, ITEM_HEIGHT, onChanged);
            }
        }

        /// <summary>押した瞬間だけ true のアイコンボタン</summary>
        public static bool DrawButton(GUIView view, Texture2D icon, string label)
        {
            if (icon == null)
            {
                return view.DrawButton(label, TEXT_ITEM_WIDTH, ITEM_HEIGHT);
            }

            var rect = view.GetDrawRect(ITEM_HEIGHT, ITEM_HEIGHT);
            var clicked = GUI.Button(rect, "", GUIView.gsButton);
            var inset = ICON_OFFSET * 0.5f;
            GUI.DrawTexture(
                new Rect(rect.x + inset, rect.y + inset, rect.width - ICON_OFFSET, rect.height - ICON_OFFSET),
                icon);
            TooltipDrawer.RegisterIfHovered(rect, label);
            view.NextElement(rect);
            return clicked;
        }
    }
}
```

(`TooltipDrawer` の名前空間と `GUIView.GetDrawRect` / `NextElement` が public であることを確認済み。ずれていたら GUIView 側を変えずにこちらで合わせる)

csproj に `<Compile Include="ViewToolbarDrawer.cs" />` を足す。

- [ ] **Step 2: SceneViewWindow を部品に寄せる**

- `TOOLBAR_HEIGHT` / `TOOLBAR_ITEM_HEIGHT` / `TOOLBAR_TOGGLE_WIDTH` / `TOOLBAR_ITEM_MARGIN` / `TOOLBAR_ICON_OFFSET` / `TOOLBAR_BG_COLOR` を削除し、参照を `ViewToolbarDrawer.*` に置き換える (`VIEW_PRESET_BUTTON_WIDTH` は残す)
- `_toolbarView` / `_toolbarRightView` の初期化子を `ViewToolbarDrawer.CreateView(FRAME)` に
- 帯の塗り 2 箇所を `ViewToolbarDrawer.DrawBackground(...)` に
- `GetToolbarToggleWidth` を削除して `ViewToolbarDrawer.GetItemWidth` に
- `DrawToolbarToggle` は config 保存と `ApplyViewSettings` の包みだけを残し、描画は `ViewToolbarDrawer.DrawToggle(view, icon, label, value, onChanged)` を呼ぶ

振る舞いは変えない (寸法・色・並びが同じ)。

- [ ] **Step 3: アイコンを足す**

`assets/icons/Screenshot.svg` (カメラ。既存 SVG と同じ黒縁 + 白線の流儀):

```xml
<!--
  GameView ツールバー「撮影」アイコン (32x32)。カメラ本体とレンズ。
  白地に黒縁取りで、明るい背景でも暗い背景でも視認できるようにしている。
-->
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <g fill="none" stroke="#000000" stroke-width="4" stroke-linejoin="round">
    <path d="M 4 10 L 10 10 L 12 6 L 20 6 L 22 10 L 28 10 L 28 26 L 4 26 Z" />
    <circle cx="16" cy="17" r="5" />
  </g>
  <g fill="none" stroke="#ffffff" stroke-width="2.2" stroke-linejoin="round">
    <path d="M 4 10 L 10 10 L 12 6 L 20 6 L 22 10 L 28 10 L 28 26 L 4 26 Z" />
    <circle cx="16" cy="17" r="5" />
  </g>
</svg>
```

`assets/icons/Png.svg` (右上の角を折った画像カードと小さな山):

```xml
<!--
  GameView ツールバー「PNG 表示」アイコン (32x32)。右上の角を折った画像カードと山。
  白地に黒縁取りで、明るい背景でも暗い背景でも視認できるようにしている。
-->
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <g fill="none" stroke="#000000" stroke-width="4" stroke-linejoin="round">
    <path d="M 6 4 L 20 4 L 26 10 L 26 28 L 6 28 Z M 20 4 L 20 10 L 26 10" />
    <path d="M 9 24 L 14 17 L 18 21 L 20 19 L 23 24" />
  </g>
  <g fill="none" stroke="#ffffff" stroke-width="2.2" stroke-linejoin="round">
    <path d="M 6 4 L 20 4 L 26 10 L 26 28 L 6 28 Z M 20 4 L 20 10 L 26 10" />
    <path d="M 9 24 L 14 17 L 18 21 L 20 19 L 23 24" />
  </g>
</svg>
```

`generate.js` の `ICONS` 末尾に `'Screenshot', 'Png'` を足し、`node assets/icons/generate.js` を実行する (`assets/cursors/node_modules` が無ければ `assets/cursors` で `npm install`)。出力された 2 つの base64 を `ToolbarIcons.PNG_BASE64` の末尾へ `// Screenshot` `// Png` のコメント付きで貼り、`Kind` の末尾に

```csharp
            // 撮影 (カメラ本体とレンズ)
            Screenshot,
            // PNG 表示 (角を折った画像カードと山)
            Png,
```

を足す。貼る前に、generate.js の出力の末尾 2 件の見出しが `Screenshot` と `Png` であることを確かめる (ICONS は 29 件、`Kind` / `PNG_BASE64` は 27 件で、`CategoryMode` / `LayerMode` の分すでにずれている)。`Kind` と `PNG_BASE64` は添字で対応するので、既存の並びに差し込まないこと (generate.js の ICONS と Kind の並びが既に一部ずれている場合も、貼るのは新しい 2 つの base64 だけにする)。生成画像を Read で目視確認する。

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

- [ ] **Step 5: 実機確認**

SceneView のツールバーが従来と同じ見た目・動作であること (`screenshot` で確認)。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ViewToolbarDrawer.cs source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj assets/icons
git commit -m "refactor(toolbar): ビューのツールバー部品を共有化し撮影・PNG アイコンを追加する"
```

### Task 6: GameView のマウスオーバーツールバー

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs:77-93`

**Interfaces:**
- Consumes: `ViewToolbarDrawer.*`、`ToolbarIcons.Kind.Screenshot/Png/Bg/Model`、`GameViewManager.showBg/showModel/showPng`、`GameViewAspect.modes/GetDisplayName`、`ScreenshotManager.Capture()`、`ComboBoxPopupWindow`
- Produces: `GameViewWindow.IsOverToolbar(Vector2 guiPos) : bool`

- [ ] **Step 1: ツールバーの寸法と部品を足す**

フィールドを足す:

```csharp
        // 比率コンボのボタン幅。「カスタム」が収まる幅
        private const int ASPECT_COMBO_WIDTH = 64;

        /// <summary>ツールバーの描画用ビュー。コンボのフォーカス管理のルートも兼ねる</summary>
        private readonly GUIView _toolbarView = ViewToolbarDrawer.CreateView(FRAME);

        private readonly GUIComboBox<GameViewAspectMode> _aspectComboBox = new GUIComboBox<GameViewAspectMode>
        {
            items = new List<GameViewAspectMode>(GameViewAspect.modes),
            getName = (mode, _) => GameViewAspect.GetDisplayName(mode),
            buttonSize = new Vector2(ASPECT_COMBO_WIDTH, ViewToolbarDrawer.ITEM_HEIGHT),
            contentSize = new Vector2(ASPECT_COMBO_WIDTH + 20, 160),
        };
```

(`using System.Collections.Generic;` を足す)

帯の矩形 (ウィンドウローカル) を描画と独立に求めるメソッド:

```csharp
        /// <summary>
        /// ツールバーの帯 (ウィンドウローカル座標)。入力の除外判定にも使うため、描画結果ではなく
        /// 常に計算で求める (マウスが入った最初のフレームのクリックもシーンへ通さないため)
        /// </summary>
        private Rect GetToolbarLocalRect()
        {
            // 項目: 撮影 / 比率 / 背景 / モデル / PNG。マージンは項目間の 4 箇所分
            var width = FRAME * 2 + ViewToolbarDrawer.ITEM_MARGIN * 4 + ASPECT_COMBO_WIDTH
                + ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(ToolbarIcons.Kind.Screenshot))
                + ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(ToolbarIcons.Kind.Bg))
                + ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(ToolbarIcons.Kind.Model))
                + ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(ToolbarIcons.Kind.Png));
            return new Rect(0, HEADER_HEIGHT, width, ViewToolbarDrawer.TOOLBAR_HEIGHT);
        }

        /// <summary>ツールバーを出すか。マウスオーバー中と、比率コンボのポップアップを開いている間</summary>
        private bool IsToolbarVisible()
        {
            if (ComboBoxPopupWindow.instance.IsOpenFor(this))
            {
                return true;
            }
            var guiPos = InputRemapper.rawGuiPosition;
            return _windowRect.Contains(guiPos) &&
                !GuiWindowTracker.IsOverWindowExcept(WINDOW_ID, guiPos);
        }

        /// <summary>GUI 座標がツールバーの帯の上か。帯の上ではシーンへの入力を無効にする</summary>
        public bool IsOverToolbar(Vector2 guiPos)
        {
            if (!isShowWnd || gameViewManager.isDirectRender)
            {
                return false;
            }
            var local = GetToolbarLocalRect();
            return new Rect(_windowRect.x + local.x, _windowRect.y + local.y, local.width, local.height)
                .Contains(guiPos);
        }
```

- [ ] **Step 2: ツールバーを描く**

```csharp
        /// <summary>撮影・表示比率・背景 / モデル / PNG 表示のトグル列。シーン描画に重ねる</summary>
        private void DrawToolbar()
        {
            var rect = GetToolbarLocalRect();
            ViewToolbarDrawer.DrawBackground(rect);

            var view = _toolbarView;
            view.Init(rect.x, rect.y, rect.width, rect.height);
            view.BeginHorizontal();

            if (ViewToolbarDrawer.DrawButton(
                view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Screenshot), "撮影"))
            {
                ScreenshotManager.Capture();
            }

            _aspectComboBox.currentIndex = Array.IndexOf(GameViewAspect.modes, config.gameViewAspectMode);
            _aspectComboBox.onSelected = (mode, _) =>
            {
                config.gameViewAspectMode = mode;
                config.dirty = true;
            };
            _aspectComboBox.DrawButton(view);

            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Bg), "背景",
                gameViewManager.showBg, value => gameViewManager.showBg = value);
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Model), "モデル",
                gameViewManager.showModel, value => gameViewManager.showModel = value);
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Png), "PNG",
                gameViewManager.showPng, value => gameViewManager.showPng = value);

            view.EndLayout();

            // ボタン押下で登録されたフォーカスをポップアップへ引き渡す (BackgroundWindow と同じ流儀)
            ComboBoxPopupWindow.instance.ProcessFocus(view, this);
        }
```

(`using System;` を足す。`GUIView.Init(float, float, float, float)` のオーバーロードは SceneView の `_toolbarView.Init(0, HEADER_HEIGHT, ...)` で使われている)

`DrawWindow` の RT 描画ブロックの直後 (最大化ボタンより前) に:

```csharp
            // RT の上に重ね、ヘッダーボタンより先に描く (押下の優先はヘッダー側に残す)
            if (IsToolbarVisible())
            {
                DrawToolbar();
            }
```

`Close()` に `ComboBoxPopupWindow.instance` が自分のものなら閉じる処理を足す:

```csharp
            if (ComboBoxPopupWindow.instance.IsOpenFor(this))
            {
                ComboBoxPopupWindow.instance.Close();
            }
```

`SetMaximized(true)` でウィンドウを隠す経路でもポップアップが残らないよう、`GameViewManager.SetMaximized` の `maximized` 分岐で同じ処理を呼ぶ (`GameViewWindow.instance.CloseToolbarPopup()` として public メソッドにまとめ、Close と共有する)。

- [ ] **Step 3: 入力判定から帯を除く**

`InputRemapper.IsGameViewActiveAt` の RT 描画中の条件に `!window.IsOverToolbar(guiPos) &&` を足す (`!window.IsOverResizeHandle(guiPos)` の次)。summary の除外対象の列挙に「ツールバー」を足す。`GameViewWindow.UpdateGizmoInput` とマウス座標の書き換え・`s_MouseUsed` の解除はこの関数を通るので追加の変更は要らないことを `rg -n "IsGameViewActiveAt" source/COM3D2.SceneEditor.Plugin` で確かめる。

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

- [ ] **Step 5: 実機確認 (Review Focus 4)**

1. GameView 外にカーソル → ツールバーなし (`screenshot`)。GameView 上 → 左上に出る
2. SceneView など他のウィンドウが GameView に重なった部分にカーソル → 出ない
3. 比率コンボを開いてポップアップへカーソルを移しても消えず、選ぶと表示が切り替わる
4. 撮影ボタン → `ScreenShot` に保存され、ツールバーは写っていない。同じクリックでカメラが回らない・メイドが選択されない
5. 背景 / モデル / PNG のトグルで表示が切り替わる。エディタを無効→有効で全表示に戻る
6. 最大化中はツールバーが出ない。ウィンドウ化に戻すと出る
7. 帯の上で右ドラッグしてもカメラが回らない

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs
git commit -m "feat(gameview): マウスオーバーで出る撮影・比率・表示トグルのツールバーを追加する"
```

### Task 7: ドキュメント

**Files:**
- Modify: `docs-site/guide/` の GameView とスクリーンショットの説明 (`rg -n "GameView|スクリーンショット" docs-site/guide` で特定。`getting-started.md` / `configuration.md` / `scene-view.md` が候補)
- Modify: `docs-site/guide/limitations.md` (直接描画中は比率を適用しない、を追記するか判断)

- [ ] **Step 1: GameView ツールバーの説明を足す**

GameView の説明箇所に、マウスオーバーで出るツールバーの項目 (撮影 / 比率 / 背景・モデル・PNG の表示) を SceneView ツールバーの説明と同じ形式 (表) で足す。非表示トグルは保存されずエディタ有効化で全表示に戻ること、透過 PNG は背景色のアルファを下げると撮れることを書く。

- [ ] **Step 2: スクリーンショットの出力サイズを直す**

`configuration.md` のスクリーンショット設定に「比率」「幅・高さ (カスタム)」を足し、出力サイズの決まり方 (比率は切り出し × 倍率、カスタムは指定サイズちょうどで倍率を使わない) を書く。

- [ ] **Step 3: 最大化中の扱い**

最大化・ウィンドウ一時非表示中は比率の切り出しとツールバーが無いことを GameView の説明か `limitations.md` に 1 行書く。

- [ ] **Step 4: コミット**

```bash
git add docs-site
git commit -m "docs(gameview): ツールバーと表示比率の説明を追加する"
```

## レビュー却下メモ

- なし (指摘 2 件はどちらも取り込み: サムネイル撮影への非表示の波及、アイコン base64 の末尾確認)
