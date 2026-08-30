# タイムライン 動画/BGM 移植 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の動画再生（MovieManager / MoviePlayerImpl）と BGM 再生（BGMManager）を SceneEditor のタイムラインへ移植し、タイムライン設定ウィンドウから操作可能にする。

**Architecture:** MTE 逐語移植方針。`MovieManager` / `BGMManager` は `Timeline/Manager/` に、`MoviePlayerImpl` は `Timeline/` に移植し、`TimelineIntegration` の `_managers` 配列へ登録する。Frontmost 表示用に MTE `CameraManager` の frontCamera 部分のみを trim 移植する（LetterBoxView はスコープ外）。設定 UI は `TimelineSettingWindow` の個別タブに BGM/動画セクション、共通タブに動画先読み秒数を追加する。

**Tech Stack:** C# (.NET Framework), Unity IMGUI, AVProVideo（`RenderHeads.Media.AVProVideo`、2.0/2.5 とも Assembly-CSharp.dll 内に存在。API surface（MediaPlayer / DisplayIMGUI / ApplyToMaterial / SeekWithTolerance / OpenVideoFromFile）は 2.5 でも確認済み）、System.Windows.Forms（OpenFileDialog 用、csproj へ参照追加）

**Spec:** `docs/superpowers/specs/timeline-remaining-work.md`（動画/BGM は同 doc 未掲載の残移植。データ層 `TimelineData`/`TimelineXml` の bgmPath / video* フィールドは移植済みで未参照）

## Global Constraints

- MTE 逐語移植（namespace `COM3D2.MotionTimelineEditor.Plugin` を維持。ロジック改変は SE 未移植機能への依存除去のみ）
- 移植元: `W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin\source\COM3D2.MotionTimelineEditor.Plugin\`（以下「MTE/」）
- ビルド確認は MSBuild 直叩き（debug.bat は実機 DLL コピーが走るため使わない）:
  `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- コメント・ログは日本語。deploy.bat / release.bat は実行禁止
- DCM 出力（song XML 等）・アスペクト比/レターボックス・`startOffsetTime` の UI 化はスコープ外（spec の決定どおり）。ただし BGM/動画のシーク時刻計算内での `startOffsetTime` 参照は MTE どおり残す

---

### Task 1: csproj 参照追加 + CameraManager（trim 移植）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Reference Include="System.Xml" />` 付近に `<Reference Include="System.Windows.Forms" />` を追加）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs`

**Interfaces:**
- Produces: `CameraManager.instance`, `Camera frontCamera`（取得時に生成）, `Camera mainCamera`。`ManagerBase` 継承、`OnLoad()` で生成 / `OnPluginDisable()` で破棄
- 注意: `_managers` への登録は Task 3 Step 4 で行う。Task 1 完了時点では OnLoad/OnPluginDisable は呼ばれない（ビルドが通るだけ）

**Steps:**

- [ ] **Step 1: csproj に System.Windows.Forms 参照を追加**（MTE csproj 57 行目と同形式）
- [ ] **Step 2: CameraManager.cs を作成**

MTE/`Manager/CameraManager.cs` から移植。ただし LetterBoxView / ResetCache / アスペクト比関連は SE スコープ外なので削除し、以下のみ残す:

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// MTE の CameraManager から frontCamera 部分のみ移植。
    /// LetterBoxView (レターボックス描画) は SE スコープ外のため削除している
    /// </summary>
    public class CameraManager : ManagerBase
    {
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

        private Camera _frontCamera = null;

        public Camera mainCamera => PluginUtils.MainCamera;

        public Camera frontCamera
        {
            get
            {
                CreateCamera();
                return _frontCamera;
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
            CreateCamera();
        }

        public override void OnPluginDisable()
        {
            DestroyCamera();
        }

        private void CreateCamera()
        {
            if (_frontCamera == null)
            {
                GameObject go = new GameObject("MTEFrontCamera");
                _frontCamera = go.AddComponent<Camera>();

                _frontCamera.enabled = true;
                _frontCamera.orthographic = true;
                _frontCamera.orthographicSize = 1.0f;
                _frontCamera.transform.position = new Vector3(0.0f, -6601.0f, -0.4f);
                _frontCamera.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                _frontCamera.fieldOfView = 60f;
                _frontCamera.nearClipPlane = -10f;
                _frontCamera.farClipPlane = 10f;
                _frontCamera.depth = 5f;
                _frontCamera.cullingMask = 256;
                _frontCamera.renderingPath = RenderingPath.Forward;
                _frontCamera.clearFlags = CameraClearFlags.Depth;
                _frontCamera.allowHDR = false;
                _frontCamera.allowMSAA = false;
            }
        }

        private void DestroyCamera()
        {
            if (_frontCamera != null)
            {
                Object.Destroy(_frontCamera.gameObject);
                _frontCamera = null;
            }
        }
    }
}
```

（MTE の DestroyCamera 実体は実装時に MTE/`Manager/CameraManager.cs` を確認し、frontCamera 破棄部分を逐語で合わせる）

- [ ] **Step 3: ビルド確認**（Global Constraints の MSBuild コマンド。Expected: Build succeeded）
- [ ] **Step 4: コミット** `feat(timeline): CameraManager(frontCamera) を trim 移植し WinForms 参照を追加`

### Task 2: MoviePlayerImpl 移植

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`

**Interfaces:**
- Consumes: `CameraManager.instance.frontCamera`（Task 1）、`TimelineManager.instance`（currentTime / anmSpeed / currentLayer）、`ConfigManager.instance.config`（videoShaderName / videoPrebufferTime / isGridVisible / isGridVisibleInVideo / isGridVisibleOnlyEdit / gridColorInVideo / gridAlpha / gridCount — 全て SE `Timeline/Config.cs` に存在確認済み）、`StudioHackManager.instance.isPoseEditing`
- Produces: `MoviePlayerImpl : MonoBehaviour`。`LoadMovie(string)`, `UpdateTransform/UpdateVolume/UpdateSpeed/UpdateSeekTime/UpdateColor/UpdateMesh/UpdateShader()`, `currentTime/duration/frameRate` プロパティ

**Steps:**

- [ ] **Step 1: MTE/`MoviePlayerImpl.cs`（663 行）を逐語コピーして作成**。適合修正は以下のみ:
  - `cameraManager` アクセサ: `private static CameraManager cameraManager => CameraManager.instance;`（SE の CameraManager は Task 1 のもの。MTE と同名なので参照式は変更不要）
  - 他の static アクセサ（timelineManager / timeline / currentLayer / studioHackManager / studioHack / config）は SE の同名クラスがそのまま解決するため変更なし
  - `MTEUtils.LogError` / `LogDebug` は SE の `MTEUtils/MTEUtils.cs` に存在するため変更なし
- [ ] **Step 2: ビルド確認**（Expected: Build succeeded。AVProVideo 型が解決されない場合は Assembly-CSharp 参照で解決されているか確認）
- [ ] **Step 3: コミット** `feat(timeline): MoviePlayerImpl を移植`

### Task 3: MovieManager / BGMManager 移植と統合

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGMManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ManagerBase.cs`（アクセサ追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（`_managers` 配列へ登録）

**Interfaces:**
- Consumes: Task 2 の `MoviePlayerImpl`、SE `TimelineManager` の静的イベント `onPlay/onStop/onRefresh/onAnmSpeedChanged/onSeekCurrentFrame`（存在確認済み: TimelineManager.cs:38-44）
- Produces: `MovieManager.instance`（`LoadMovie/UnloadMovie/ReloadMovie/UpdateTransform/UpdateVolume/UpdateSpeed/UpdateSeekTime/UpdateColor/UpdateMesh/UpdateShader`, `isValidPath/isEnabled/currentTime/duration/frameRate`）、`BGMManager.instance`（`Load/Reload/Play/Stop/Pause/Resume/UpdateVolume/SeekPlayingTime`, `volumeDance`）、`ManagerBase.movieManager/bgmManager`

**Steps:**

- [ ] **Step 1: MTE/`Manager/MovieManager.cs` を逐語コピーして作成**。適合修正:
  - 冒頭の `public enum VideoDisplayType {...}` は削除（SE では `Timeline/VideoDisplayType.cs` に定義済み。同ファイルのコメント「動画再生機能自体は未移植」を「MTE の MovieManager.cs から移植した enum。定義位置のみ分離している」に更新）
- [ ] **Step 2: MTE/`Manager/BGMManager.cs` を逐語コピーして作成**。適合修正:
  - MTE 版は `OnChangedSceneLevel` でのみ `Stop()` し `OnPluginDisable` 未実装のため、プラグイン無効化後も BGM が鳴り続ける（MovieManager と非対称）。SE では以下を追加する:

```csharp
        public override void OnPluginDisable()
        {
            // MTE には無いが、プラグイン無効化後に BGM が鳴り続けるため停止する
            Stop();
        }
```

  - それ以外は逐語（`PluginUtils.MainCamera` / `defaultLayer.isAnmPlaying` / `defaultLayer.playingTime` / `timelineManager.anmSpeed` は SE に存在）。`WWW` + `Thread.Sleep` の同期読込（最大 10 秒ブロック）も MTE どおり据え置く
- [ ] **Step 3: ManagerBase にアクセサ追加**（MTE ManagerBase.cs:34-35 と同じ）:

```csharp
        protected static BGMManager bgmManager => BGMManager.instance;
        protected static MovieManager movieManager => MovieManager.instance;
```

- [ ] **Step 4: TimelineIntegration の `_managers` 配列に追加**（`MTEP.TimelineSeManager.instance,` の並びに合わせて）:

```csharp
                MTEP.CameraManager.instance,
                MTEP.MovieManager.instance,
                MTEP.BGMManager.instance,
```

- [ ] **Step 5: ビルド確認**（Expected: Build succeeded）
- [ ] **Step 6: コミット** `feat(timeline): MovieManager/BGMManager を移植しタイムラインへ統合`

### Task 4: TimelineSettingWindow に BGM/動画設定 UI を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`

**Interfaces:**
- Consumes: `MovieManager.instance`（`movieManager` として参照）、`BGMManager.instance`、MTE/`TimelineSettingUI.cs` の `DrawBGMSetting`（698 行付近〜）/ `DrawVideoSetting`（811 行付近〜）/ `VideoDisplayTypeNames` / `_videoDisplayTypeComboBox` / 共通設定の動画先読み秒数スライダー（565 行付近）
- Produces: 個別タブ末尾に「BGM設定」「動画設定」セクション、共通タブに「動画先読み秒数」スライダー

**Steps:**

- [ ] **Step 1: MTE/`TimelineSettingUI.cs` から `DrawBGMSetting` / `DrawVideoSetting` / `VideoDisplayTypeNames` / `_videoDisplayTypeComboBox` を逐語移植**し、`DrawSongSetting` の末尾から `DrawHorizontalLine` 区切りで呼び出す。適合修正:
  - `using System.Windows.Forms;` を追加（`Screen` など UnityEngine と衝突する型があればエイリアス `using WinFormsDialog = System.Windows.Forms.OpenFileDialog;` 方式で回避し、`GUIView` 等の既存識別子を壊さない）
  - `movieManager` / `bgmManager` のローカルアクセサを `TimelineSettingWindow` に追加: `private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;` ほか（本ファイルの既存の manager 参照スタイルに合わせる。`MTEP` エイリアス未定義なら using を確認して追随）
  - `TransformDataBase.PositionNames` / `RotationNames` / `GetNormalizedEulerAngles`、`config.positionRange` は SE に存在するか実装時に確認し、無ければ MTE から該当メンバーのみ追随移植
  - OpenFileDialog は STA 制約でゲーム内から失敗する報告があるが、MTE 実績どおりまず逐語で移植し実機確認に委ねる（失敗時はパス手入力 TextField が代替導線として既にある）
- [ ] **Step 2: 共通タブ（`DrawCommonSetting`）に動画先読み秒数スライダーを移植**（MTE TimelineSettingUI.cs:560-570 付近の `config.videoPrebufferTime`。SE 共通設定の dirty/保存パターンに合わせる）
- [ ] **Step 3: ビルド確認**（Expected: Build succeeded）
- [ ] **Step 4: コミット** `feat(timeline): タイムライン設定に BGM/動画セクションを追加`

### Task 5: コードレビューと実機確認

**Steps:**

- [ ] **Step 1: code-review スキルでレビュー**し、妥当な指摘を反映・コミット
- [ ] **Step 1.5: AVPro ネイティブデコーダの同梱確認**: `W:\COM3D2_5\COM3D2x64_Data\Plugins\`（x86_64 含む）に AVPro のネイティブ DLL（AVProVideo*.dll 等）が存在するか確認する。型解決＝実行時動作ではないため、欠如していれば実機確認前にユーザーへ報告する
- [ ] **Step 2: 実機確認**（ゲーム起動中なら devbridge、そうでなければ手順をユーザーへ提示）:
  1. タイムライン読み込み → 設定ウィンドウ個別タブに BGM/動画セクションが出る
  2. BGM: .ogg 指定 → 再生でタイムラインに同期、シークで追従、速度変更でピッチ変化
  3. 動画: mp4 指定・GUI 表示で再生同期、表示形式 4 種切替（Frontmost で frontCamera 生成）、音量/透過度スライダー反映
  4. タイムライン停止・プラグイン無効化で動画 GameObject と BGM が破棄される
  5. 追加観点: BGM「選択/再読込」時の UI フリーズが許容範囲か（WWW 同期読込のため最大 10 秒）、Mesh/Backmost 表示でシェーダ `CM3D2/Unlit_Texture_Photo_MyObject` が 2.5 で解決されるか（`Shader.Find` null 時は Awake で例外になりうる）
- [ ] **Step 3: spec 更新**: `docs/superpowers/specs/timeline-remaining-work.md` に動画/BGM 移植完了を追記し、docs コミット

## Self-Review 結果

- スコープ: MTE の動画/BGM 実行系（Manager 2 + Impl 1 + UI）を網羅。DCM song 出力・レターボックスは spec どおり除外
- 型整合: `CameraManager.frontCamera`（Task 1）→ `MoviePlayerImpl.targetCamera`（Task 2）、`MoviePlayerImpl` の公開メソッド（Task 2）→ `MovieManager` の委譲呼び出し（Task 3）→ UI からの `movieManager.Update*`（Task 4）で一致
- 既知リスク: (1) AVProVideo の実行時挙動は 2.5 実機でのみ検証可能（ビルドは通る見込み）。(2) OpenFileDialog のゲーム内動作。いずれも Task 5 の実機確認で判定
