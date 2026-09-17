# オーバーレイカメラ集約とグリッドのポストエフェクト回避 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** グリッド・ギズモ・骨格線をメインカメラから専用のオーバーレイカメラへ移してポストエフェクトの影響を外し、プラグインが作るオーバーレイ系カメラ (clear / front / text / gizmo) の所有と追随を `CameraManager` に一本化する。

**Architecture:** `MTEP.CameraManager` が 4 台のカメラを所有し、`LateUpdate` と明示呼び出し (`SyncToMainCamera`) でメインカメラの `targetTexture` / `depth` / `allowHDR` へ追随させる。描画コンポーネント 3 種は「付いているカメラ (ホスト)」と「行列・ピッキングの基準 (viewCamera)」を分け、GameView では gizmo カメラをホスト、メインカメラを viewCamera にする。SceneView は無変更。

**Tech Stack:** C# (LangVersion 9 / Unity 5.6 ランタイム相当の API)、xunit (`source/COM3D2.SceneEditor.Plugin.Tests`)、実機検証は MCP `com3d25-devbridge`。

**Spec:** `docs/superpowers/specs/2026-09-16-overlay-camera-consolidation-design.md`

## Global Constraints

- コードのコメント・ログ文言は日本語
- git worktree は使わない。作業はこのディレクトリで行う
- ビルドは `debug.bat` を使わず MSBuild を直接叩く (実機へコピーされるのを避ける)。Git Bash からは以下の 1 本で両構成ビルド + テストを回す。**COM3D2 → COM3D25 の順**でないとテストが参照する `bin/Debug/COM3D25` の DLL が消える

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& "$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& dotnet test source/COM3D2.SceneEditor.Plugin.Tests --nologo -v q
```

- 単体テストでは Unity の `Camera` / `GameObject` を生成できない (ランタイム無し)。テストは既存の `GizmoHostVisibilityTests` / `ManagerPluginEnableTests` と同じくリフレクションと定数の検証に留め、カメラの実挙動は Task 7 の実機検証で担保する
- 描画順 (depth): clear = -100、front = 5、text = main + 100、gizmo = main + 200
- コミットメッセージは Conventional Commits 形式の日本語。末尾に次を付ける

```
Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018ymry5nLvAL9rewcVPnNGi
```

---

## ファイル構成

| ファイル | 役割 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs` | clear / front / text / gizmo カメラの所有・追随・列挙 (Task 1) |
| `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` | `viewCamera` 導入、viewCamera からの逆引きレジストリ (Task 2) |
| `source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs` | `viewCamera` 導入 (Task 2) |
| `source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs` | `viewCamera` 導入、床グリッドの深度テスト除去 (Task 2) |
| `source/COM3D2.SceneEditor.Plugin/GizmoHost.cs` | `IsGizmoVisible` を viewCamera 逆引きに対応 (Task 2) |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs` | カメラ所有を手放し `cameraManager.textCamera` を借りる (Task 3) |
| `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs` | gizmo カメラへの付け替え、clear カメラ委譲、front 同期削除 (Task 4) |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs` | `GetOverlayCameras` へ置換 (Task 5) |
| `source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs` | 新規。定数と公開 API の検証 (Task 1, 2) |
| `docs/se-mte-state-duplication-survey.md` | `MTEFrontCamera` 名の追従 (Task 6) |

---

### Task 1: CameraManager にオーバーレイカメラの所有・追随・列挙を集約する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs` (新規)

**Interfaces:**
- Consumes: `PluginUtils.MainCamera` (既存)、`ManagerBase` のライフサイクル (既存)
- Produces (後続 Task が使う):
  - `public const float ClearCameraDepth = -100f; FrontCameraDepth = 5f; TextCameraDepthOffset = 100f; GizmoCameraDepthOffset = 200f;`
  - `public Camera clearCamera { get; }` (未生成なら null)
  - `public Camera frontCamera { get; }` (既存、遅延生成) / `public Camera createdFrontCamera { get; }` (既存)
  - `public Camera textCamera { get; }` (遅延生成) / `public Camera createdTextCamera { get; }`
  - `public Camera gizmoCamera { get; }` (`OnPluginEnable` で生成。未生成なら getter で生成)
  - `public void SetClearCameraActive(bool active, Color backgroundColor)`
  - `public bool IsOverlayCamera(Camera camera)`
  - `public void GetOverlayCameras(List<Camera> cameras)`
  - `public void SyncToMainCamera()`

- [ ] **Step 1: 定数と API の存在を検証するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs` を新規作成する。

```csharp
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// オーバーレイカメラの描画順と公開 API を検証する。
    /// Camera / GameObject の生成は Unity ランタイムが要るためここでは行わず、
    /// 追随や描画の実挙動は実機 (devbridge) で担保する
    /// </summary>
    public class OverlayCameraTests
    {
        [Fact]
        public void 描画順はclear_front_text_gizmoの順になる()
        {
            // メインカメラの depth は 0 前後。front は固定値、text / gizmo はメイン基準のオフセット
            Assert.True(MTEP.CameraManager.ClearCameraDepth < 0f);
            Assert.True(MTEP.CameraManager.FrontCameraDepth > 0f);
            Assert.True(MTEP.CameraManager.TextCameraDepthOffset > MTEP.CameraManager.FrontCameraDepth);
            Assert.True(MTEP.CameraManager.GizmoCameraDepthOffset > MTEP.CameraManager.TextCameraDepthOffset);
        }

        [Theory]
        [InlineData("clearCamera")]
        [InlineData("frontCamera")]
        [InlineData("textCamera")]
        [InlineData("gizmoCamera")]
        [InlineData("createdFrontCamera")]
        [InlineData("createdTextCamera")]
        public void カメラのプロパティを公開している(string name)
        {
            var property = typeof(MTEP.CameraManager).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.Equal(typeof(Camera), property.PropertyType);
        }

        [Fact]
        public void 列挙と判定のAPIを公開している()
        {
            var type = typeof(MTEP.CameraManager);

            var getOverlay = type.GetMethod("GetOverlayCameras", new[] { typeof(List<Camera>) });
            Assert.NotNull(getOverlay);

            var isOverlay = type.GetMethod("IsOverlayCamera", new[] { typeof(Camera) });
            Assert.NotNull(isOverlay);
            Assert.Equal(typeof(bool), isOverlay.ReturnType);

            var setClear = type.GetMethod("SetClearCameraActive", new[] { typeof(bool), typeof(Color) });
            Assert.NotNull(setClear);

            var sync = type.GetMethod("SyncToMainCamera", System.Type.EmptyTypes);
            Assert.NotNull(sync);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Global Constraints のビルド + テストコマンドを実行する。
Expected: `OverlayCameraTests` がコンパイルエラー (`ClearCameraDepth` 等が未定義) で失敗する。

- [ ] **Step 3: CameraManager を書き換える**

`source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs` を以下に置き換える。

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// プラグインが作るオーバーレイ系カメラの所有者。
    /// メインカメラのポストエフェクトを避けるため、メインカメラの上に別カメラで重ね描きする
    /// 実体 (背景クリア・最前面動画/レターボックス・字幕・グリッド/ギズモ/骨格線) を
    /// ここで作り、描画先 (GameView の RT) と描画順をメインカメラへ追随させる。
    /// カメラを足すときはこのクラスに足し、撮影側 (ScreenshotManager) は
    /// GetOverlayCameras で列挙するだけにする
    /// </summary>
    public class CameraManager : ManagerBase
    {
        /// <summary>GameView 背景色クリア用。メインより先に画面 (バックバッファ) を塗る</summary>
        public const float ClearCameraDepth = -100f;

        /// <summary>最前面動画・レターボックス用。MTE 由来の固定値</summary>
        public const float FrontCameraDepth = 5f;

        /// <summary>字幕用。PIP (サブカメラ = main + 1) の後に描いて最前面にする</summary>
        public const float TextCameraDepthOffset = 100f;

        /// <summary>グリッド・ギズモ・骨格線用。編集用の線は字幕・最前面動画よりも手前に出す</summary>
        public const float GizmoCameraDepthOffset = 200f;

        /// <summary>
        /// 字幕カメラの配置。SceneView のカメラは "UI" レイヤーも描くため、
        /// シーンから遠く離してキャンバスが編集画面に映り込まないようにする
        /// </summary>
        private static readonly Vector3 TextCameraPosition = new Vector3(0f, -10000f, 0f);

        /// <summary>
        /// 字幕キャンバスをカメラから離す距離。遠いほどキャンバスがワールド座標で大きくなり、
        /// カメラを原点から離して置いても座標精度が落ちにくい
        /// </summary>
        public const float TextCanvasPlaneDistance = 100f;

        private static readonly int TextLayer = LayerMask.NameToLayer("UI");

        private static CameraManager _instance = null;
        public static CameraManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraManager();
                }
                return _instance;
            }
        }

        private Camera _clearCamera = null;
        private Camera _frontCamera = null;
        private Camera _textCamera = null;
        private Camera _gizmoCamera = null;
        private LetterBoxView _letterBoxView = null;

        public Camera mainCamera => PluginUtils.MainCamera;

        /// <summary>背景クリアカメラ。ウィンドウモード外では null</summary>
        public Camera clearCamera => _clearCamera;

        public Camera frontCamera
        {
            get
            {
                CreateFrontCamera();
                return _frontCamera;
            }
        }

        /// <summary>
        /// 生成済みの前面カメラ。未生成なら null。
        /// 毎フレームの判定に使うため、frontCamera と違って生成を伴わない
        /// </summary>
        public Camera createdFrontCamera => _frontCamera;

        /// <summary>字幕カメラ。字幕が初めて要るときに遅延生成する</summary>
        public Camera textCamera
        {
            get
            {
                CreateTextCamera();
                return _textCamera;
            }
        }

        /// <summary>生成済みの字幕カメラ。未生成なら null</summary>
        public Camera createdTextCamera => _textCamera;

        /// <summary>グリッド・ギズモ・骨格線の描画ホスト。何も映さず OnPostRender だけ使う</summary>
        public Camera gizmoCamera
        {
            get
            {
                CreateGizmoCamera();
                return _gizmoCamera;
            }
        }

        private CameraManager()
        {
        }

        public override void Init()
        {
        }

        public override void OnLoad()
        {
            CreateFrontCamera();
        }

        /// <summary>
        /// 再有効化でカメラを作り直す。動画の最前面表示・レターボックス・ギズモが使う実体で、
        /// タイムラインの有無とは無関係に要るため timeline 必須の OnLoad 任せにはできない。
        /// 字幕カメラは字幕生成時の遅延生成に任せる
        /// </summary>
        public override void OnPluginEnable()
        {
            CreateFrontCamera();
            CreateGizmoCamera();
        }

        public override void OnPluginDisable()
        {
            DestroyCameras();
        }

        public override void LateUpdate()
        {
            SyncToMainCamera();
        }

        /// <summary>レターボックスの描画キャッシュを破棄し、次フレームで再計算させる</summary>
        public void ResetCache()
        {
            if (_letterBoxView != null)
            {
                _letterBoxView.ResetCache();
            }
        }

        /// <summary>
        /// 背景クリアカメラの有効/無効を切り替える。
        /// メインカメラが RT へ逃げている間 (ウィンドウ化中) だけ画面を背景色で塗る
        /// </summary>
        public void SetClearCameraActive(bool active, Color backgroundColor)
        {
            if (!active)
            {
                if (_clearCamera != null)
                {
                    Object.Destroy(_clearCamera.gameObject);
                    _clearCamera = null;
                }
                return;
            }

            if (_clearCamera == null)
            {
                var go = new GameObject("SceneEditorClearCamera");
                Object.DontDestroyOnLoad(go);
                _clearCamera = go.AddComponent<Camera>();
                _clearCamera.depth = ClearCameraDepth;
                _clearCamera.clearFlags = CameraClearFlags.SolidColor;
                _clearCamera.cullingMask = 0;
            }
            _clearCamera.backgroundColor = backgroundColor;
        }

        /// <summary>
        /// プラグインのオーバーレイカメラか。GameView の UI カメラ隠蔽から除外する判定に使う
        /// (NGUI レイヤーを描く front カメラがゲーム UI と誤認されるのを防ぐ)
        /// </summary>
        public bool IsOverlayCamera(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }
            return camera == _clearCamera
                || camera == _frontCamera
                || camera == _textCamera
                || camera == _gizmoCamera;
        }

        /// <summary>
        /// メインカメラの後に重ね描きするカメラ (front / text / gizmo) のうち実際に描かれるものを
        /// cameras へ足し、全体を depth 昇順に整える。手動描画の経路 (スクショ・連番出力) が
        /// これらを描かないと撮影結果に写らない。clear カメラは画面側を塗る役なので含めない。
        /// cameras を呼び出し側が渡すのは、毎フレーム呼ぶ連番画像出力がリストを使い回して
        /// アロケーションを避けられるようにするため
        /// </summary>
        public void GetOverlayCameras(List<Camera> cameras)
        {
            AddIfRenderable(cameras, _frontCamera);
            AddIfRenderable(cameras, _textCamera);
            AddIfRenderable(cameras, _gizmoCamera);

            // 画面表示と同じ重なりで合成するため、Unity の描画順と同じ depth 昇順に並べる
            cameras.Sort(CameraDepthComparison);
        }

        private static readonly System.Comparison<Camera> CameraDepthComparison =
            (a, b) => a.depth.CompareTo(b.depth);

        private static void AddIfRenderable(List<Camera> cameras, Camera camera)
        {
            if (camera != null && camera.enabled)
            {
                cameras.Add(camera);
            }
        }

        /// <summary>
        /// 描画先 (GameView の RenderTexture)・描画順・HDR をメインカメラへ追随させる。
        /// LateUpdate で毎フレーム呼ぶほか、GameViewManager がメインカメラの targetTexture を
        /// 付け替えた直後にも呼ぶ (RT を破棄する前に参照を外すため)
        /// </summary>
        public void SyncToMainCamera()
        {
            var main = mainCamera;
            if (main == null)
            {
                return;
            }

            SyncCamera(_frontCamera, main, FrontCameraDepth);
            SyncCamera(_textCamera, main, main.depth + TextCameraDepthOffset);
            SyncCamera(_gizmoCamera, main, main.depth + GizmoCameraDepthOffset);
        }

        private static void SyncCamera(Camera camera, Camera main, float depth)
        {
            if (camera == null)
            {
                return;
            }

            // 破棄済み RT は Unity の演算子オーバーロードで null と等価判定されるため、
            // != 比較だと破棄済み参照の掃除がスキップされる。参照同一性で比較する
            if (!ReferenceEquals(camera.targetTexture, main.targetTexture))
            {
                camera.targetTexture = main.targetTexture;
            }

            if (camera.depth != depth)
            {
                camera.depth = depth;
            }

            // HDR 不一致のまま LDR の GameView RT と往復すると画面に黒点が焼き付くため、
            // ゲーム側の HDR 切替に毎フレーム追随する
            if (camera.allowHDR != main.allowHDR)
            {
                camera.allowHDR = main.allowHDR;
            }
        }

        private void CreateFrontCamera()
        {
            if (_frontCamera == null)
            {
                var go = new GameObject("SceneEditorFrontCamera");
                _frontCamera = go.AddComponent<Camera>();

                _frontCamera.enabled = true;
                _frontCamera.orthographic = true;
                _frontCamera.orthographicSize = 1.0f;
                _frontCamera.transform.position = new Vector3(0.0f, -6601.0f, -0.4f);
                _frontCamera.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                _frontCamera.fieldOfView = 60f;
                _frontCamera.nearClipPlane = -10f;
                _frontCamera.farClipPlane = 10f;
                _frontCamera.depth = FrontCameraDepth;
                _frontCamera.cullingMask = PluginUtils.NGUILayerMask;
                _frontCamera.renderingPath = RenderingPath.Forward;
                _frontCamera.clearFlags = CameraClearFlags.Depth;
                _frontCamera.allowHDR = false;
                _frontCamera.allowMSAA = false;
            }

            if (_letterBoxView == null)
            {
                var go = new GameObject("LetterBoxView");
                _letterBoxView = go.AddComponent<LetterBoxView>();
            }
        }

        private void CreateTextCamera()
        {
            if (_textCamera != null)
            {
                return;
            }

            var go = new GameObject("SceneEditorTextCamera");
            go.transform.position = TextCameraPosition;
            go.transform.rotation = Quaternion.identity;

            _textCamera = go.AddComponent<Camera>();
            _textCamera.cullingMask = 1 << TextLayer;
            // 背景と 3D はメインカメラが描き終えているので、深度だけ消して上に重ねる
            _textCamera.clearFlags = CameraClearFlags.Depth;
            _textCamera.orthographic = false;
            _textCamera.fieldOfView = 60f;
            // 生成直後はメインカメラを取得できないことがあるため既定で切っておく
            // (取得できればこの直後の SyncToMainCamera がメインカメラへ揃え直す)
            _textCamera.allowHDR = false;
            _textCamera.nearClipPlane = 1f;
            // キャンバス (planeDistance の位置) が確実に収まるよう余裕を持たせる
            _textCamera.farClipPlane = TextCanvasPlaneDistance * 2f;

            SyncToMainCamera();
        }

        private void CreateGizmoCamera()
        {
            if (_gizmoCamera != null)
            {
                return;
            }

            var go = new GameObject("SceneEditorGizmoCamera");
            _gizmoCamera = go.AddComponent<Camera>();
            // 何も映さない。グリッド・ギズモ・骨格線は OnPostRender の GL 描画で、
            // 行列は viewCamera (メインカメラ) から取るのでこのカメラの位置・画角は使わない
            _gizmoCamera.cullingMask = 0;
            _gizmoCamera.clearFlags = CameraClearFlags.Depth;
            _gizmoCamera.allowHDR = false;
            _gizmoCamera.allowMSAA = false;

            SyncToMainCamera();
        }

        private void DestroyCameras()
        {
            SetClearCameraActive(false, Color.black);

            if (_frontCamera != null)
            {
                Object.Destroy(_frontCamera.gameObject);
                _frontCamera = null;
            }

            if (_textCamera != null)
            {
                Object.Destroy(_textCamera.gameObject);
                _textCamera = null;
            }

            if (_gizmoCamera != null)
            {
                Object.Destroy(_gizmoCamera.gameObject);
                _gizmoCamera = null;
            }

            if (_letterBoxView != null)
            {
                Object.Destroy(_letterBoxView.gameObject);
                _letterBoxView = null;
            }
        }
    }
}
```

注意: 旧コードの `cullingMask = 256` は NGUI レイヤー (8) のビットマスクなので `NGUILayerMask` に置き換えている。ただし `CameraManager` の名前空間 (`COM3D2.MotionTimelineEditor.Plugin`) にも別の `PluginUtils` があり、そちらには `NGUILayerMask` が無い (`MainCamera` はある)。上のコードの `PluginUtils.NGUILayerMask` は **`COM3D2.SceneEditor.Plugin.PluginUtils.NGUILayerMask` と完全修飾で書く**こと。

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行する。
Expected: 両構成ビルド成功、`OverlayCameraTests` 3 種と既存テスト (特に `ManagerPluginEnableTests`: `CameraManager` は引き続き `OnPluginEnable` を自前実装している) が PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs
git commit -m "feat(camera): CameraManager にオーバーレイカメラの所有と追随を集約する"
```

---

### Task 2: 描画コンポーネントに viewCamera を導入し、床グリッドを最前面描画にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:99-106,203-205`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs:38-45`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs:41-91,113-125`
- Modify: `source/COM3D2.SceneEditor.Plugin/GizmoHost.cs:99-125`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs`

**Interfaces:**
- Produces:
  - `GizmoRenderer` / `BoneLineRenderer` / `GridRenderer` に `public Camera viewCamera { get; set; }`。null を入れるとホスト (`GetComponent<Camera>()`) に戻る
  - `public static GizmoRenderer GizmoRenderer.FindByViewCamera(Camera camera)`: 有効な GizmoRenderer のうち `viewCamera == camera` のものを返す。無ければ null

- [ ] **Step 1: viewCamera プロパティの存在を検証するテストを足す**

`OverlayCameraTests.cs` に以下を追加する。

```csharp
        [Theory]
        [InlineData(typeof(GizmoRenderer))]
        [InlineData(typeof(BoneLineRenderer))]
        [InlineData(typeof(GridRenderer))]
        public void 描画コンポーネントは視点カメラを差し替えられる(System.Type type)
        {
            // GameView ではホスト (gizmo カメラ) とは別にメインカメラを視点にするため、
            // 3 コンポーネントとも同じ名前で公開する
            var property = type.GetProperty("viewCamera", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.Equal(typeof(Camera), property.PropertyType);
            Assert.True(property.CanWrite);
        }

        [Fact]
        public void GizmoRendererを視点カメラから逆引きできる()
        {
            // GizmoHost.IsGizmoVisible は外部プラグインからメインカメラで問われるため、
            // メインカメラに GizmoRenderer が付いていなくても viewCamera から引ける必要がある
            var method = typeof(GizmoRenderer).GetMethod(
                "FindByViewCamera", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Camera) }, null);
            Assert.NotNull(method);
            Assert.Equal(typeof(GizmoRenderer), method.ReturnType);
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Global Constraints のコマンドを実行する。
Expected: 追加した 2 テストが FAIL (`viewCamera` / `FindByViewCamera` が null)。

- [ ] **Step 3: GizmoRenderer に viewCamera とレジストリを足す**

`GizmoRenderer.cs` の `private Camera _camera;` (99 行目付近) を次に置き換える。

```csharp
        private Camera _camera;

        /// <summary>
        /// 行列・ピッキング・サイズ計算の基準になるカメラ。既定は自分が付いているカメラ。
        /// GameView ではポストエフェクトを避けるため gizmo カメラに付け、視点はメインカメラにする
        /// </summary>
        public Camera viewCamera
        {
            get => _camera;
            set => _camera = value != null ? value : GetComponent<Camera>();
        }

        /// <summary>有効な GizmoRenderer。GizmoHost が視点カメラから逆引きするのに使う</summary>
        private static readonly List<GizmoRenderer> _activeRenderers = new List<GizmoRenderer>();

        /// <summary>
        /// 視点カメラが camera の GizmoRenderer を返す。
        /// メインカメラは GizmoRenderer を持たない (gizmo カメラに付く) ため、
        /// GetComponent では引けない GameView 側のレンダラをここで解決する
        /// </summary>
        public static GizmoRenderer FindByViewCamera(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }
            for (var i = 0; i < _activeRenderers.Count; i++)
            {
                var renderer = _activeRenderers[i];
                if (renderer != null && renderer._camera == camera)
                {
                    return renderer;
                }
            }
            return null;
        }

        private void OnEnable()
        {
            if (!_activeRenderers.Contains(this))
            {
                _activeRenderers.Add(this);
            }
        }

        private void OnDisable()
        {
            _activeRenderers.Remove(this);
        }
```

`Awake` の `_camera = GetComponent<Camera>();` はそのまま残す (viewCamera 未設定時の既定)。`using System.Collections.Generic;` がファイル先頭に無ければ足す (`_maidGizmos` が `List` なので既にあるはず)。

`DrawMainCameraFrustum` の `if (mainCamera == null || mainCamera == _camera)` は `_camera` が視点カメラになったのでそのままで正しい (GameView では視点 = メインカメラなので描かれない)。変更不要だが、コメントを次のように補う。

```csharp
            // 視点がメインカメラそのもの (GameView) なら自分の視錐台は描かない
            if (mainCamera == null || mainCamera == _camera)
```

- [ ] **Step 4: BoneLineRenderer と GridRenderer に viewCamera を足す**

`BoneLineRenderer.cs` の `private Camera _camera;` (38 行目付近) を次に置き換える。

```csharp
        private Camera _camera;

        /// <summary>
        /// 行列・ピッキング・線幅計算の基準になるカメラ。既定は自分が付いているカメラ。
        /// GameView ではポストエフェクトを避けるため gizmo カメラに付け、視点はメインカメラにする
        /// </summary>
        public Camera viewCamera
        {
            get => _camera;
            set => _camera = value != null ? value : GetComponent<Camera>();
        }
```

`GridRenderer.cs` の 41〜47 行目 (`_worldMaterial` / `_overlayMaterial` / `_camera` の宣言) を次に置き換える。

```csharp
        /// <summary>
        /// 床グリッドと画面分割グリッド共用。ポストエフェクト後に別カメラで描くため
        /// メインカメラの深度は参照できず、どちらも深度テスト無しで常に手前に出す
        /// </summary>
        private Material _lineMaterial;

        private Camera _camera;

        /// <summary>
        /// 行列・線幅計算の基準になるカメラ。既定は自分が付いているカメラ。
        /// GameView ではポストエフェクトを避けるため gizmo カメラに付け、視点はメインカメラにする
        /// </summary>
        public Camera viewCamera
        {
            get => _camera;
            set => _camera = value != null ? value : GetComponent<Camera>();
        }
```

`Awake` の末尾 2 行を次に置き換える。

```csharp
            _lineMaterial = CreateLineMaterial(shader);
```

`CreateLineMaterial` から `zTest` 引数を外し、`_ZTest` を `Always` 固定にする。

```csharp
        private static Material CreateLineMaterial(Shader shader)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return material;
        }
```

`OnDestroy` を 1 マテリアル分にする。

```csharp
        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }
```

`OnPostRender` の分岐を次に置き換える。

```csharp
            if (_lineMaterial == null || !isActive)
            {
                return;
            }

            if (config.isGridVisibleInWorld)
            {
                DrawWorldGrid();
            }

            if (drawDisplayGrid && config.isGridVisibleInDisplay)
            {
                DrawDisplayGrid();
            }
```

`DrawWorldGrid` 内の `_worldMaterial.SetPass(0);` と `DrawDisplayGrid` 内の `_overlayMaterial.SetPass(0);` を `_lineMaterial.SetPass(0);` に変える (`grep -n "_worldMaterial\|_overlayMaterial" GridRenderer.cs` で残りが無いことを確認する)。クラス冒頭の summary コメントの「床の XZ 平面グリッド…」はそのまま、「描画・座標変換の作法は BoneLineRenderer に揃えている」の後に「ポストエフェクトを避けるためメインカメラではなく gizmo カメラの OnPostRender で描き、行列は viewCamera から取る」を足す。

- [ ] **Step 5: GizmoHost.IsGizmoVisible を viewCamera 逆引きに対応させる**

`GizmoHost.cs` の `var renderer = camera.GetComponent<GizmoRenderer>();` を次に置き換える。

```csharp
            // SceneView はカメラ自身に付いている。GameView はメインカメラを視点にした
            // gizmo カメラ側に付いているため、視点カメラからの逆引きも試す
            var renderer = camera.GetComponent<GizmoRenderer>() ?? GizmoRenderer.FindByViewCamera(camera);
```

summary の「ホストが駆動していないカメラ (standalone の Camera.main や ウィンドウモードのまま非表示になった GameView のカメラ)」の記述はそのまま有効。

- [ ] **Step 6: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行する。
Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs source/COM3D2.SceneEditor.Plugin/Manager/GridRenderer.cs source/COM3D2.SceneEditor.Plugin/GizmoHost.cs source/COM3D2.SceneEditor.Plugin.Tests/OverlayCameraTests.cs
git commit -m "feat(gizmo): 描画コンポーネントの視点カメラをホストから分離する"
```

---

### Task 3: TimelineTextManager が字幕カメラを CameraManager から借りる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs:40-55,100-135,195-215,270-345`

**Interfaces:**
- Consumes: `CameraManager.textCamera` / `createdTextCamera` / `TextCanvasPlaneDistance` (Task 1)
- Produces: `TimelineTextManager.textCamera` は残すが `cameraManager.createdTextCamera` への転送にする (Task 5 で参照が消えたら削除)

- [ ] **Step 1: カメラ関連の定数・フィールドを削除する**

以下を削除する。

- `CameraPosition` (46 行目付近) と `CameraDepthOffset` (49 行目付近) と `CanvasPlaneDistance` (55 行目付近) の 3 定数と、それぞれの summary コメント
- `private GameObject _cameraObject = null;` と `private Camera _camera = null;`

`TextLayer` は `TimelineText` オブジェクトの layer 設定で使うので残す。

`textCamera` プロパティを次に置き換える。

```csharp
        /// <summary>
        /// 字幕カメラ。所有は CameraManager で、未生成なら null。
        /// 連番画像出力のようにカメラを手動描画する経路はこのカメラも描かないと字幕が写らない
        /// </summary>
        public Camera textCamera => cameraManager.createdTextCamera;
```

- [ ] **Step 2: LateUpdate と OnPluginDisable からカメラ処理を外す**

`LateUpdate` (`UpdateRenderTarget()` だけを呼んでいる) をメソッドごと削除する。

`OnPluginDisable` の `_cameraObject` を破棄するブロック (`if (_cameraObject != null) { ... }`) を削除する。カメラは `CameraManager.OnPluginDisable` が壊す。

- [ ] **Step 3: CreateCanvasAndCamera をキャンバスだけにする**

メソッド名を `CreateCanvas` に変え、`CreateText` 内の呼び出しも `CreateCanvas();` に変える。本体は次にする (summary も更新)。

```csharp
        /// <summary>
        /// 字幕用のキャンバスを作り、CameraManager の字幕カメラに紐付ける。
        /// GameView はメインカメラを RenderTexture に描いて表示しているため、
        /// 同じ RT へ後乗せする専用カメラで字幕を GameView 内に映す。
        /// メインカメラに相乗りするとポストエフェクトの対象に入ってしまう
        /// </summary>
        private void CreateCanvas()
        {
            _canvasObject = new GameObject("TimelineTextCanvas");
            _canvasObject.layer = TextLayer;

            var canvas = _canvasObject.AddComponent<Canvas>();
            // worldCamera は renderMode と同時に必ず入れること。カメラ未設定のまま
            // 一度でも描画されたキャンバスに後からカメラを挿すと、字幕が左右反転する
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cameraManager.textCamera;
            canvas.planeDistance = CameraManager.TextCanvasPlaneDistance;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;

            var scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            scaler.referencePixelsPerUnit = 100f;
        }
```

末尾の `UpdateRenderTarget();` 呼び出しと `UpdateRenderTarget` メソッド本体 (315〜345 行目付近) を削除する。`grep -n "_camera\b\|_cameraObject\|UpdateRenderTarget\|CanvasPlaneDistance" TimelineTextManager.cs` で残りが無いことを確認する。

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行する。
Expected: 両構成ビルド成功、全テスト PASS (`ManagerPluginEnableTests`: `TimelineTextManager` は `OnPluginEnable` を引き続き自前実装している)。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs
git commit -m "refactor(timeline): 字幕カメラの所有を CameraManager へ移す"
```

---

### Task 4: GameViewManager をオーバーレイカメラ経由に切り替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs:32,98-104,126-132,160-185,226-258,300-360,385-410,486-505`

**Interfaces:**
- Consumes: `CameraManager.gizmoCamera` / `SetClearCameraActive` / `IsOverlayCamera` / `SyncToMainCamera` (Task 1)、renderer の `viewCamera` (Task 2)

- [ ] **Step 1: クリアカメラを CameraManager へ委譲する**

- `private Camera _clearCamera = null;` (32 行目) を削除
- `CreateClearCamera` / `DestroyClearCamera` の 2 メソッド (391〜409 行目付近) を削除
- `EnterWindowMode` / `SetMaximized(false 側)` の `CreateClearCamera();` を `cameraManager.SetClearCameraActive(true, config.backgroundColor);` に置き換える
- `ExitWindowMode` / `SetMaximized(true 側)` の `DestroyClearCamera();` を `cameraManager.SetClearCameraActive(false, config.backgroundColor);` に置き換える

`cameraManager` は `ManagerBase` の `protected static CameraManager cameraManager` で参照できる (`GameViewManager : ManagerBase`)。

- [ ] **Step 2: メインカメラの targetTexture 付け替え直後に追随させる**

`EnterWindowMode` の `camera.targetTexture = renderTexture;` の直後、`SetMaximized` の両分岐、`ExitWindowMode` の `camera.targetTexture = null;` の直後に次を入れる。

`SetMaximized(true)` 側は `if (camera.targetTexture == renderTexture) { camera.targetTexture = null; }` という条件付き代入なので、**if ブロックの外 (条件の成否によらず通る位置)、かつ `ReleaseRenderTexture()` より前**に置く。タイムライン系 (AMCameraFade 等) に targetTexture を奪われて条件が false になるケースでも、RT 破棄前にオーバーレイカメラの参照を外すため。

```csharp
            // RT を付け替えた直後にオーバーレイカメラも揃える (RT 破棄前に参照を外す)
            cameraManager.SyncToMainCamera();
```

`ExitWindowMode` では `SyncFrontCameraTarget();` の行をこの呼び出しで置き換える (順序は `DetachGizmoRenderer()` → `SyncToMainCamera()` → `RestoreUICameras()` → `SetClearCameraActive(false)` → `ReleaseRenderTexture()`)。

`LateUpdate` 内の 2 箇所の `SyncFrontCameraTarget();` は削除し (`CameraManager.LateUpdate` が毎フレーム行う)、`SyncFrontCameraTarget` メソッド本体 (343〜357 行目付近) も削除する。`LateUpdate` の最大化分岐のコメント「サイズ追従も targetTexture の保険も不要」はそのまま。

- [ ] **Step 3: 描画コンポーネントを gizmo カメラへ付ける**

`AttachGizmoRenderer` を次に置き換える。

```csharp
        /// <summary>
        /// ギズモ・骨格線・グリッドの描画をオーバーレイ (gizmo) カメラへ載せる。
        /// メインカメラに載せると OnPostRender の GL 描画にポストエフェクトが乗るため、
        /// 何も映さない専用カメラで後から重ね描きし、視点だけメインカメラを使う
        /// </summary>
        private void AttachGizmoRenderer(Camera camera)
        {
            DetachGizmoRenderer();

            var host = cameraManager.gizmoCamera.gameObject;

            gizmoRenderer = host.AddComponent<GizmoRenderer>();
            gizmoRenderer.viewCamera = camera;
            // GameView はゲーム本来の見え方を保ちたいため選択枠は出さない (SceneView のみ)
            gizmoRenderer.showSelectionBounds = false;
            gizmoRenderer.showLightGizmos = false;
            // 編集モード外・ボーン表示 OFF ではギズモも出さない (SceneView はツールバー連動のみ)
            gizmoRenderer.followsBoneVisibility = true;
            gizmoRenderer.isHostActive = IsGizmoHostActive;

            boneLineRenderer = host.AddComponent<BoneLineRenderer>();
            boneLineRenderer.viewCamera = camera;
            boneLineRenderer.isHostActive = IsGizmoHostActive;

            gridRenderer = host.AddComponent<GridRenderer>();
            gridRenderer.viewCamera = camera;
            gridRenderer.isHostActive = IsGizmoHostActive;
            // 構図合わせ用の画面分割グリッドはゲーム画面側にだけ出す
            gridRenderer.drawDisplayGrid = true;
        }
```

`DetachGizmoRenderer` は変更不要 (コンポーネント単位で `Destroy` している)。

- [ ] **Step 4: UI カメラ隠蔽の除外判定をまとめる**

`HideUICameras` 内の

```csharp
                if (cam == null || cam == camera || cam == _clearCamera)
```

を

```csharp
                // 自前のオーバーレイカメラ (背景クリア・最前面動画・字幕・ギズモ) は
                // ゲーム UI ではなく編集中も見せる描画物なので隠す対象から外す
                if (cam == null || cam == camera || cameraManager.IsOverlayCamera(cam))
```

に置き換え、その下の `if (cam == MTEP.CameraManager.instance.createdFrontCamera) { continue; }` ブロック (コメント含む) を削除する。

クラス summary の「背景をクリアカメラで塗る」はそのままでよい。`grep -n "_clearCamera\|SyncFrontCameraTarget\|CreateClearCamera\|DestroyClearCamera" GameViewManager.cs` で残りが無いことを確認する。

- [ ] **Step 5: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行する。
Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs
git commit -m "fix(gameview): グリッド・ギズモ・骨格線をオーバーレイカメラで描きポストエフェクトを避ける"
```

---

### Task 5: ScreenshotManager の重ね描きカメラ列挙を CameraManager に任せる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs:257-284`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs` (`textCamera` 削除)

**Interfaces:**
- Consumes: `CameraManager.GetOverlayCameras(List<Camera>)` (Task 1)

- [ ] **Step 1: AddExtraCameras を置き換える**

`AddExtraCameras` の summary と本体、`CameraDepthComparison`、`AddIfRenderable` を次に置き換える (`RenderExtras` は残す)。

```csharp
        /// <summary>
        /// メインカメラの後に重ね描きするカメラ (最前面動画・字幕・ギズモ) を cameras へ足す。
        /// どれもメインカメラのカリング対象外の専用カメラで描かれるため、
        /// 手動描画の経路がこれらを描かないと撮影結果に写らない。
        /// 列挙と depth 順は所有者の CameraManager に任せ、カメラが増えてもここは変えない
        /// </summary>
        internal static void AddExtraCameras(List<Camera> cameras)
        {
            MTEP.CameraManager.instance.GetOverlayCameras(cameras);
        }
```

`RenderExtras` の summary「いずれも clearFlags が Depth のため」はそのまま正しい (gizmo カメラも Depth)。

補足: `RenderExtras` は有効なカメラへ明示的に `Render()` を呼ぶため、撮影フレームでは Unity の通常描画と合わせて gizmo カメラの `OnPostRender` (グリッド・ギズモ・骨格線の GL 描画) が 2 回走る。front / text カメラと同じ既存の前提で、撮影時だけの一時的なコストなので許容する。`RenderExtras` の summary にこの旨を 1 行足しておく。

- [ ] **Step 2: TimelineTextManager.textCamera を削除する**

`grep -rn "\.textCamera" --include=*.cs source` で参照が `ScreenshotManager` 以外に無いことを確認し、Task 3 で転送用に残した `TimelineTextManager.textCamera` プロパティを削除する。

- [ ] **Step 3: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行する。
Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs
git commit -m "refactor(capture): 重ね描きカメラの列挙を CameraManager に任せる"
```

---

### Task 6: ドキュメントのカメラ名を追従させる

**Files:**
- Modify: `docs/se-mte-state-duplication-survey.md:61`

- [ ] **Step 1: 名称を更新する**

61 行目の「`Timeline/Manager/CameraManager` はオーバーレイ専用の `MTEFrontCamera` で競合しない」を「`Timeline/Manager/CameraManager` はオーバーレイ専用カメラ (front / text / gizmo / clear) の所有者で競合しない」に変える。

- [ ] **Step 2: コミット**

```bash
git add docs/se-mte-state-duplication-survey.md
git commit -m "docs: オーバーレイカメラ集約に合わせてカメラ名の記述を更新する"
```

---

### Task 7: 実機検証 (devbridge)

**Files:** なし (検証のみ。問題があれば該当 Task のファイルを修正してコミット)

前提: ゲーム (COM3D2.5) を起動し、`debug.bat com3d25` または手動コピーで新 DLL を反映する (ゲーム起動中は DLL コピーが失敗するので、停止中に反映してから起動する)。MCP `com3d25-devbridge` の `ping` で疎通を確認する。

- [ ] **Step 1: カメラの生成と追随を確認する**

`eval_csharp` で次を評価し、4 台 (clear はウィンドウモード中のみ) が存在し、`targetTexture` と `allowHDR` がメインカメラと一致し、depth が -100 / 5 / main+100 / main+200 であることを確認する。

```csharp
var cm = COM3D2.MotionTimelineEditor.Plugin.CameraManager.instance;
var main = cm.mainCamera;
var list = new System.Collections.Generic.List<UnityEngine.Camera>();
cm.GetOverlayCameras(list);
var sb = new System.Text.StringBuilder();
sb.AppendFormat("main depth={0} rt={1} hdr={2}\n", main.depth, main.targetTexture != null, main.allowHDR);
sb.AppendFormat("clear={0}\n", cm.clearCamera != null);
foreach (var c in list) sb.AppendFormat("{0} depth={1} rt={2} hdr={3} mask={4}\n", c.name, c.depth, c.targetTexture != null, c.allowHDR, c.cullingMask);
sb.ToString()
```

Expected: `SceneEditorFrontCamera` (mask 256)、`SceneEditorGizmoCamera` (mask 0)、字幕を 1 つでも使っていれば `SceneEditorTextCamera` が depth 昇順で並び、rt / hdr が main と一致する。

- [ ] **Step 2: ポストエフェクトが乗らないことを確認する**

タイムラインのポストエフェクトレイヤーで被写界深度 (DepthOfField) を強めにかけ、パラフィンで画面全体の色を変えた状態で `screenshot` を撮り、床グリッド・画面分割グリッド・ギズモ・骨格線がぼけず元の色で描かれていることを目視する。比較用に変更前の挙動 (ぼける・色が変わる) を覚えておく。

- [ ] **Step 3: 既存機能の退行を確認する**

以下を順に確認する。

1. 字幕: テキストレイヤーで字幕を出し、GameView ウィンドウ内・最大化中・ウィンドウモード外の 3 状態で見えること
2. 最前面動画・レターボックス: 動画の表示形式「最前面」とレターボックスが 3 状態で見えること
3. スクショ (ScreenshotManager) と連番画像出力に字幕・動画・グリッド・ギズモが写ること
4. GameView でギズモをドラッグして選択オブジェクトが動くこと (ピッキングの視点がメインカメラのままであること)
5. ModItemExplorer 併用時、GameView でツールバーのギズモ表示を OFF にすると外部ギズモも消えること (`GizmoHost.IsGizmoVisible` の逆引き)
6. SceneView の描画 (グリッド・ギズモ・骨格線・メインカメラ視錐台) が変わらないこと
7. プラグイン無効化 → 再有効化で Step 1 の一覧が復帰すること
8. GameView の背景色 (クリアカメラ) がウィンドウモードで塗られ、最大化・モード外では消えること

- [ ] **Step 4: 問題があれば修正してコミットする**

修正はその原因の Task のファイルへ加え、`fix(...)` で個別にコミットする。

- [ ] **Step 5: code-review スキルでレビューし、指摘を取り込む**

ユーザーへ提示する前に `code-review` スキルを実行し、妥当な指摘を反映してコミットする。

## レビュー却下メモ

- 新規テストがリフレクションの存在確認と定数比較に留まり、`SyncCamera` / `GetOverlayCameras` / `FindByViewCamera` の挙動を検証していない — 3 つとも `Camera` の実体 (targetTexture / enabled / depth) を扱うロジックで、Unity ランタイム無しのテストホストでは切り出しても検証対象が残らない。実機検証 (Task 7) で担保する方針を維持 (制約上の見送り)
