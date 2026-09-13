# StudioHack 系の平坦化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (この作業では subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE 移植由来の `StudioHackBase` / `StudioHackManager` の多態機構を撤去し、SceneEditor 唯一の実装 `SceneEditorHack` を具象クラス 1 つに畳んで、到達しない分岐 (`IsValid` / `errorMessage`) と no-op 呼び出しを消す。

**Architecture:** `SceneEditorHack` を `sealed` な具象クラスにし、旧 `StudioHackManager.studioHack` の契約 (タイトル画面では null) を `SceneEditorHack.instance` が引き継ぐ。旧 `StudioHackManager.isPoseEditing` (null 安全) は `SceneEditorHack.isPoseEditing` の **static** プロパティへ移す。全所の `studioHack` アクセサ名は据え置き、参照先だけ差し替える。`IModelHack` / `ModelHackBase` / `ExternalModelHack` (ModItemExplorer 連携) は触らない。`StudioHackBase` の `IModelHack` 実装は `ModelHackManager` に自分を登録するためだけだったので、`ModelHackManager` から studioHack 参照を外して切り離す。

**Tech Stack:** C# (.NET 3.5 / .NET 4.7.1 の 2 構成)、MSBuild、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** 本計画が仕様を兼ねる (2026-09-13 の調査結果。下記「調査で確定した事実」参照)

## Global Constraints

- **2 構成ともビルドすること** (`GameVersion=COM3D2` は .NET 3.5、`COM3D25` は .NET 4.7.1)。順序は COM3D2 → COM3D25 → `dotnet test` (COM3D2 側のビルドが `bin/Debug/COM3D25/` を消すため)
- `debug.bat` / `deploy.bat` は使わない。MSBuild を直接叩く (下記コマンド)
- コードのコメント・ログ文言は日本語
- git worktree は使わない。作業ツリーには本計画と無関係な未コミット変更 (`MaidGravityWindow.cs` / `GravityRowDrawer.cs` / `docs/superpowers/plans/2026-09-13-gravity-timeline-layer.md` 等) が乗っているので、**コミットは本計画で触ったファイルだけを `git add` で個別指定する**
- `IModelHack` / `ModelHackBase` / `ExternalModelHack` / `PartsEditHackBase` は変更しない (MIE 連携の契約)
- タイムライン XML の互換に関わる `pluginName == "SceneEditor"` の文字列は変えない (`TimelineXml.ConvertPlugin` がモデルの `pluginName` 変換に使う)

### ビルド・テストコマンド (Git Bash)

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" /v:m /nologo \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
 && cd .. && dotnet test COM3D2.SceneEditor.Plugin.Tests
```

期待: 2 構成とも `エラー 0`、`dotnet test` が全件 Passed (現在の件数は実行時に確認。減っていないこと)。

## 調査で確定した事実 (2026-09-13)

- `StudioHackBase` の派生は `SceneEditorHack` 1 つだけ。`StudioHackManager` の `priority` ソート / `activeStudioHacks` 選別 / `IsValid()` フォールバックは 1 要素に対して空回りしている
- `SceneEditorHack.IsValid()` は常に true、`errorMessage` は常に空。これを見る分岐 10 か所 + `ShowDialog` 5 か所は到達しない
- `SceneEditorHack` が override していない virtual (`ChangeMaid` / `HasBoneRotateVisible` / `IsBoneRotateVisible` / `SetBoneRotateVisible` / `ClearPoseHistory` / `UpdateUndress` / `OnMotionUpdated` / `OnUpdateMyPose` / `OnUpdateDepthOfField` / `Update`) は基底で no-op。呼び出し側 12 か所は何もしていない
- `studioHack.` 経由でどこからも使われていないメンバー: `priority` / `depthOfField` / `isIKVisible` / `isUIVisible` / `motionSliderRate` / `GetMaidSlotNo` / `GetMaid` / `IsBackgroundVisible`。`subCamera` は常に null を返し `CameraTimelineLayer` の `if (subCamera != null)` を常に偽にしている
- null になる期間は実在する: `OnChangedSceneLevel` でシーン名が `SceneTitle` のとき `isSceneActive = false` → `PreUpdate` が `_studioHack = null`。各所の `studioHack == null` ガードはこの契約に依存しているので **契約は維持する**
- `StudioHackManager.isPoseEditing` は null 安全 (null なら false / 書き込み無視)。`ManagerBase.studioHackManager` 経由でレイヤー約 30 か所 + SE 側 (`AutoEditMode` / `COM3D2.SceneEditor.Plugin.cs` / `EyesPosRowDrawer` / `MotionData`) から参照されている
- `TimelineManager.cs` には `var isPoseEditing = studioHackManager.isPoseEditing;` というローカル変数が 4 か所 (217 / 973 / 1738 / 2240 行付近) ある。基底クラスに `isPoseEditing` メンバーを足すとローカルが自己参照になりコンパイルエラーになるため、**メンバー追加ではなく `SceneEditorHack.isPoseEditing` と型名付きで置換する**
- `ModelHackManager` は `studioHack.modelList` を無条件に連結し、`GetOrDefault` が studioHack へフォールバックする。`SceneEditorHack.modelList` は空固定なので、切り離しても `modelList` の中身は変わらない。`GetOrDefault` の呼び出し側のうち `DeleteModel` / `CreateModel` / `UpdateAttachPoint` / `SetModelVisible` は null チェック済みだが、**`ChangePluginName` (195-212 行付近) は prev/next を非 null 前提で使っている** (plan-review で検出)。フォールバック撤去時に早期 return を足す (Task 2 Step 5)
- `TimelineIntegration._managers` の中で `StudioHackManager` より前に並ぶ `ConfigManager` は `OnChangedSceneLevel` を override していない (`ManagerBase` の空実装)。`SceneEditorHack.OnChangedSceneLevel` をループ前へ出しても現時点で見える挙動は変わらないが、今後マネージャが override を足した場合は「シーン状態確定後の値を見る」ことになる (意図した順序)
- `SceneEditorHack.depthOfField` は `studioHack.` 経由の参照がゼロ (`PostEffectManager` も使っていない)
- `StudioModelManager.SetupModels` はプロバイダ無しをログ付きで打ち切る
- `LiveEffectWindow.cs:29` の `studioHackManager` アクセサは定義のみで未使用
- テストプロジェクトに StudioHack 系への参照は無い

## File Structure

| ファイル | 扱い |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs` | 書き直し。sealed 具象クラス + static `instance` / `isPoseEditing` / `Initialize` / `OnChangedSceneLevel` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/StudioHackBase.cs` | 削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioHackManager.cs` | 削除 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 上記 2 ファイルの `Compile Include` を削除 |
| `Timeline/Manager/ManagerBase.cs` / `Timeline/TimelineLayer/TimelineLayerBase.cs` / `Timeline/BoneMenu/BoneMenuItem.cs` / `Timeline/DressUtils.cs` / `Timeline/MaidCache.cs` / `Timeline/Manager/{Psyllium,StageLaser,StageLight,SubCamera}Manager.cs` / `Timeline/MotionTimelineEditor.cs` / `Timeline/StudioLightStat.cs` / `Timeline/TimelineData.cs` | `studioHack` アクセサの型と参照先を差し替え |
| `Timeline/TimelineIntegration.cs` / `TimelineControlWindow.cs` / `TimelineWindow.cs` / `Timeline/TimelineXml.cs` / `Timeline/MotionData.cs` / `EyesPosRowDrawer.cs` / `LiveEffectWindow.cs` / `AutoEditMode.cs` / `COM3D2.SceneEditor.Plugin.cs` | `StudioHackManager` 参照の置換、`IsValid` 分岐の除去 |
| `Timeline/TimelineLayer/*.cs` (約 25 ファイル) / `Timeline/Manager/{Maid,Timeline}Manager.cs` | `studioHackManager.isPoseEditing` → `SceneEditorHack.isPoseEditing` の機械置換 |
| `Timeline/BoneMenu/MaidBoneMenuItem.cs` / `Timeline/Manager/{Maid,PostEffect,Timeline}Manager.cs` / `Timeline/TimelineLayer/{Motion,Camera}TimelineLayer.cs` / `Timeline/Manager/ModelHackManager.cs` / `Timeline/Manager/StudioModelManager.cs` | no-op 呼び出しと studioHack 依存の除去 |
| `source/COM3D2.SceneEditor.Plugin.Tests/SceneEditorHackContractTests.cs` | 新規。XML 互換に関わる `pluginName` 文字列契約の固定 |

---

### Task 1: SceneEditorHack を単体クラス化し、StudioHackBase / StudioHackManager を撤去する

このタスクは「削除したメンバーの参照を全部置き換える」まで含めて 1 つの単位。途中の状態はコンパイルが通らないので、Step を順に全部終えてからビルドする。

**Files:**
- Rewrite: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/StudioHackBase.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioHackManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:453,466`
- Modify: `Timeline/Manager/ManagerBase.cs:17,24`
- Modify: `Timeline/TimelineLayer/TimelineLayerBase.cs:205`
- Modify: `Timeline/BoneMenu/BoneMenuItem.cs:33`、`Timeline/DressUtils.cs:59`、`Timeline/MaidCache.cs:372`、`Timeline/Manager/PsylliumManager.cs:56`、`Timeline/Manager/StageLaserManager.cs:39`、`Timeline/Manager/StageLightManager.cs:39`、`Timeline/Manager/SubCameraManager.cs:50`、`Timeline/MotionTimelineEditor.cs:44,55`、`Timeline/StudioLightStat.cs:36`、`Timeline/TimelineData.cs:437`
- Modify: `Timeline/TimelineIntegration.cs:21-22,30,128-133,179,207-216,283`
- Modify: `TimelineControlWindow.cs:36-37,384-395,433-437,451-455,468-480,489`
- Modify: `TimelineWindow.cs:514-517`
- Modify: `Timeline/TimelineXml.cs:1368-1374`
- Modify: `Timeline/MotionData.cs:20-30`
- Modify: `EyesPosRowDrawer.cs:40-41,259`、`LiveEffectWindow.cs:29`
- Modify: `AutoEditMode.cs:20-36`、`COM3D2.SceneEditor.Plugin.cs:223,236-239`
- Modify: `Timeline/Manager/TimelineManager.cs:354-358,387-391` と `studioHackManager.isPoseEditing` の全箇所
- Modify: `Timeline/Manager/ModelHackManager.cs:111`
- Modify: `Timeline/TimelineLayer/*.cs` の `studioHackManager.isPoseEditing` 全箇所
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/SceneEditorHackContractTests.cs` (新規)

**Interfaces:**
- Produces:
  - `public sealed class SceneEditorHack` (namespace `COM3D2.MotionTimelineEditor.Plugin`)
  - `public static SceneEditorHack instance { get; }` — `Initialize()` 前およびタイトル画面では null
  - `public static bool isPoseEditing { get; set; }` — instance が null なら get は false、set は無視 (旧 `StudioHackManager.isPoseEditing` と同じ)
  - `public static void Initialize()` — 旧 `Register(new SceneEditorHack())` + `Init()` 相当
  - `public static void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)`
  - `public const string pluginName = "SceneEditor"`
  - インスタンスメンバー: `selectedMaid` / `allMaids` / `selectedMaidSlotNo` / `outputAnmPath` / `isAnmPlaying` / `isAnmEnabled` / `useMuneKeyL` / `useMuneKeyR` / `ChangeBackground(string)` / `SetBackgroundVisible(bool)` (Task 2 で未使用メンバーを削るので、このタスクでは既存メンバーをそのまま残してよい)

- [ ] **Step 1: 契約テストを書く (失敗確認)**

`source/COM3D2.SceneEditor.Plugin.Tests/SceneEditorHackContractTests.cs` を新規作成:

```csharp
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// TimelineXml.ConvertPlugin がモデルの pluginName を現在のプラグイン名へ寄せる際の
    /// 比較キー。既存 XML との互換に関わるため文字列を固定する
    /// </summary>
    public class SceneEditorHackContractTests
    {
        [Fact]
        public void プラグイン名はSceneEditorで固定()
        {
            Assert.Equal("SceneEditor", MTEP.SceneEditorHack.pluginName);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source && dotnet test COM3D2.SceneEditor.Plugin.Tests 2>&1 | tail -5`
Expected: ビルドエラー (`pluginName` はまだインスタンスプロパティで、static アクセスできない。CS0120)

- [ ] **Step 3: SceneEditorHack.cs を書き直す**

`Timeline/Hack/SceneEditorHack.cs` を以下の内容で全面置換する (既存の `isPoseEditing` / `isAnmEnabled` / `ApplyMuneYure` / `outputAnmPath` / `allMaids` の本文とコメントはそのまま移す):

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    /// <summary>
    /// タイムライン (MTE 移植コード) からのメイド・編集状態アクセスを
    /// SceneEditor の各マネージャへ橋渡しする。
    /// MTE では StudioHackBase 派生をスタジオ種別ごとに切り替えていたが、
    /// SceneEditor 以外の実装は持たないので具象クラス 1 つに畳んでいる
    /// </summary>
    public sealed class SceneEditorHack
    {
        /// <summary>TimelineXml がモデルの pluginName を寄せる比較キー。既存 XML 互換のため固定</summary>
        public const string pluginName = "SceneEditor";

        private static SceneEditorHack _instance;
        private static bool _isSceneActive;

        /// <summary>
        /// Initialize 前とタイトル画面では null を返す。
        /// 呼び出し側の null ガードは「タイムラインが動く場面か」の判定として使われている
        /// </summary>
        public static SceneEditorHack instance => _isSceneActive ? _instance : null;

        /// <summary>
        /// ポーズ編集モード。instance の値をそのまま返す (キャッシュしない)。
        /// フレーム頭で同期するキャッシュを挟むと、同フレーム中に編集モードへ
        /// 入った直後の読み手が古い値を見て食い違う。
        /// instance が null (タイトル画面) のときは false 扱いで、書き込みは無視する
        /// </summary>
        public static bool isPoseEditing
        {
            get
            {
                var hack = instance;
                return hack != null && hack.isPoseEditingInternal;
            }
            set
            {
                var hack = instance;
                if (hack != null)
                {
                    hack.isPoseEditingInternal = value;
                }
            }
        }

        public static void Initialize()
        {
            if (_instance != null)
            {
                return;
            }
            _instance = new SceneEditorHack();

            // 登録がシーンロード後になるため、初期状態はアクティブ扱いにする
            _isSceneActive = true;

            // SceneEdit では photo mode の背景オブジェクト CSV が未ロードのため明示的に読み込む
            // (StudioModelManager の BGObjectIdMap / OfficialObjectLabelMap が PhotoBGObjectData.data に依存する)
            if (PhotoBGObjectData.data == null)
            {
                PhotoBGObjectData.Create();
            }
        }

        public static void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _isSceneActive = scene.name != "SceneTitle";
        }

        private SceneEditorHack()
        {
        }

        private static SE.MaidManipulateManager manipulateManager
            => SE.MaidManipulateManager.instance;

        private static MaidManager maidManager => MaidManager.instance;

        public Maid selectedMaid => manipulateManager.targetMaid;

        private readonly List<Maid> _allMaids = new List<Maid>();
        public List<Maid> allMaids
        {
            get
            {
                // (既存の本文をそのまま移す)
            }
        }

        public int selectedMaidSlotNo => allMaids.IndexOf(selectedMaid);

        public string outputAnmPath
        {
            get
            {
                // (既存の本文をそのまま移す)
            }
        }

        /// <summary>
        /// ボーン/IK の表示。SE の「ボーン表示」トグルの生値がそのまま実体で、
        /// 実際に出ているか (編集モードとの AND) は isBoneEditing が持つ。
        /// ポーズ編集モードには追従させない (static isPoseEditing 参照)
        /// </summary>
        public bool isIKVisible
        {
            get => manipulateManager.isBoneVisible;
            set => manipulateManager.isBoneVisible = value;
        }

        // 旧 StudioHackBase.isPoseEditing。外からは static isPoseEditing 経由で触る
        private bool isPoseEditingInternal
        {
            get => manipulateManager.isEditMode;
            set
            {
                if (value && isAnmPlaying)
                {
                    isAnmPlaying = false;
                }
                manipulateManager.isEditMode = value;
            }
        }

        public bool isAnmPlaying
        {
            get => maidManager.isAnmPlaying;
            set
            {
                if (value && isPoseEditingInternal)
                {
                    isPoseEditingInternal = false;
                }
                maidManager.isAnmPlaying = value;
            }
        }

        public bool isAnmEnabled
        {
            get
            {
                // (既存の本文をそのまま移す)
            }
            set
            {
                if (value && isPoseEditingInternal)
                {
                    return;
                }
                // (以下、既存の本文をそのまま移す)
            }
        }

        // タイムライン側が再生時間を直接制御するため、スライダー同期は不要
        public float motionSliderRate
        {
            set { }
        }

        public bool useMuneKeyL
        {
            set => ApplyMuneYure(true, value);
        }

        public bool useMuneKeyR
        {
            set => ApplyMuneYure(false, value);
        }

        // (ApplyMuneYure は既存のコメント込みでそのまま移す)

        public Camera subCamera => null;

        public bool isUIVisible
        {
            get => !SE.WindowManager.instance.isWindowsHidden;
            set => SE.WindowManager.instance.SetWindowsHidden(!value);
        }

        private static void DeleteBGObject()
        {
            BgMgr bgMgr = GameMain.Instance.BgMgr;
            UnityEngine.Object.Destroy(bgMgr.current_bg_object);
            bgMgr.DeleteBg();
        }

        public void ChangeBackground(string bgName)
        {
            if (bgName != GameMain.Instance.BgMgr.GetBGName())
            {
                DeleteBGObject();
                GameMain.Instance.BgMgr.ChangeBg(bgName);
            }
        }

        public void SetBackgroundVisible(bool visible)
        {
            var bgObject = GameMain.Instance.BgMgr.current_bg_object;
            if (bgObject != null)
            {
                bgObject.SetActive(visible);
            }
        }

        public bool IsBackgroundVisible()
        {
            var bgObject = GameMain.Instance.BgMgr.current_bg_object;
            return bgObject != null && bgObject.activeSelf;
        }

        public int GetMaidSlotNo(string guid)
        {
            var maids = this.allMaids;
            for (var i = 0; i < maids.Count; i++)
            {
                if (maids[i].status.guid == guid)
                {
                    return i;
                }
            }
            return -1;
        }

        public Maid GetMaid(int slotNo)
        {
            var maids = this.allMaids;
            if (slotNo < 0 || slotNo >= maids.Count)
            {
                return null;
            }
            return maids[slotNo];
        }
    }
}
```

削るもの: `StudioHackBase` 継承、`override` 修飾子、`priority`、`errorMessage` / `IsValid()`、`Init()` (→ static `Initialize`)、`isSceneActive` インスタンスプロパティ、`OnSceneActive` / `OnSceneDeactive`、`depthOfField` と冒頭の `DepthOfFieldEffect` using エイリアス (どこからも使われていない)、`modelList` と `_emptyModelList` (IModelHack を実装しなくなるため)、no-op virtual 群。
`isPoseEditing` の旧 `if (value && isAnmPlaying)` 条件は維持する (`isPoseEditingInternal` に移しただけ)。

- [ ] **Step 4: StudioHackBase.cs / StudioHackManager.cs を削除し csproj から外す**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
git rm Timeline/Hack/StudioHackBase.cs Timeline/Manager/StudioHackManager.cs
```

`COM3D2.SceneEditor.Plugin.csproj` から次の 2 行を削除:

```xml
    <Compile Include="Timeline\Hack\StudioHackBase.cs" />
    <Compile Include="Timeline\Manager\StudioHackManager.cs" />
```

- [ ] **Step 5: 基底アクセサを差し替える**

`Timeline/Manager/ManagerBase.cs`:

```csharp
// 変更前
        public virtual StudioHackBase studioHack => StudioHackManager.instance.studioHack;
        ...
        protected static StudioHackManager studioHackManager => StudioHackManager.instance;
// 変更後
        public virtual SceneEditorHack studioHack => SceneEditorHack.instance;
        (studioHackManager 行は削除)
```

`Timeline/TimelineLayer/TimelineLayerBase.cs:205`:

```csharp
        protected static SceneEditorHack studioHack => SceneEditorHack.instance;
```

`TimelineLayerBase.cs` に `studioHackManager` アクセサがあれば同様に削除する (`grep -n studioHackManager Timeline/TimelineLayer/TimelineLayerBase.cs` で確認)。

- [ ] **Step 6: 各ファイルのローカルアクセサを差し替える**

以下は全部同じ形。`private static StudioHackBase studioHack => StudioHackManager.instance.studioHack;` を `private static SceneEditorHack studioHack => SceneEditorHack.instance;` に置換する (BoneMenuItem は `protected static`):

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
sed -i 's/static StudioHackBase studioHack => StudioHackManager\.instance\.studioHack;/static SceneEditorHack studioHack => SceneEditorHack.instance;/' \
  Timeline/BoneMenu/BoneMenuItem.cs Timeline/DressUtils.cs Timeline/MaidCache.cs \
  Timeline/Manager/PsylliumManager.cs Timeline/Manager/StageLaserManager.cs \
  Timeline/Manager/StageLightManager.cs Timeline/Manager/SubCameraManager.cs \
  Timeline/MotionTimelineEditor.cs Timeline/StudioLightStat.cs Timeline/TimelineData.cs
grep -rn 'StudioHackBase' --include=*.cs . | grep -v 'Hack/SceneEditorHack.cs'
```

最後の grep が `TimelineIntegration.cs` / `TimelineControlWindow.cs` 以外を返さないこと (返したらそのファイルも同じ置換をする)。

- [ ] **Step 7: `studioHackManager.isPoseEditing` を機械置換する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
grep -rl 'studioHackManager\.isPoseEditing' --include=*.cs Timeline | xargs sed -i 's/studioHackManager\.isPoseEditing/SceneEditorHack.isPoseEditing/g'
grep -rn 'studioHackManager' --include=*.cs Timeline
```

最後の grep が `Timeline/MotionData.cs` と `Timeline/TimelineIntegration.cs` だけを返すこと。

`Timeline/MotionData.cs:20-30`:

```csharp
        // 変更前
        private static StudioHackManager studioHackManager => StudioHackManager.instance;

        public int stFrameActive
        {
            get => studioHackManager.isPoseEditing ? stFrameInEdit : stFrame;
        }
        public int edFrameActive
        {
            get => studioHackManager.isPoseEditing ? edFrameInEdit : edFrame;
        }
        // 変更後 (アクセサ行は削除)
        public int stFrameActive
        {
            get => SceneEditorHack.isPoseEditing ? stFrameInEdit : stFrame;
        }
        public int edFrameActive
        {
            get => SceneEditorHack.isPoseEditing ? edFrameInEdit : edFrame;
        }
```

- [ ] **Step 8: TimelineIntegration.cs を更新する**

```csharp
// 21-22 行付近: 変更前
            private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
            private static MTEP.StudioHackBase studioHack => studioHackManager.studioHack;
// 変更後
            private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
```

`_managers` の初期化リスト (30 行付近) から `studioHackManager,` の行を削除する。

`UpdateGuards` (128-133 行付近):

```csharp
// 変更前
                studioHackManager.PreUpdate();

                if (studioHack == null || !studioHack.IsValid())
                {
                    return false;
                }
// 変更後
                if (studioHack == null)
                {
                    return false;
                }
```

179 行付近の `if (studioHack == null || !studioHack.IsValid() ||` は `if (studioHack == null ||` に変える (後続の条件はそのまま)。

`OnChangedSceneLevel` (207-216 行付近): 旧 `StudioHackManager.OnChangedSceneLevel` が `_managers` ループ経由で呼ばれていた分を明示呼び出しに置き換える。ループより **前** に置く (マネージャ側が `studioHack` の有無を見て動くため、先にシーン状態を確定させる):

```csharp
            public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
            {
                // 旧 StudioHackManager が担っていたシーン有効判定。マネージャより先に確定させる
                MTEP.SceneEditorHack.OnChangedSceneLevel(scene, sceneMode);

                // SE より後にロードされたモデル配置プラグインをここで拾う。
                // 登録済みなら何もしないので、シーン切り替えごとの負荷は無視できる
                TryRegisterModelPlacer();

                foreach (var manager in _managers)
                {
                    manager.OnChangedSceneLevel(scene, sceneMode);
                }
            }
```

`Initialize` (283 行付近):

```csharp
// 変更前
            MTEP.StudioHackManager.instance.Register(new MTEP.SceneEditorHack());
// 変更後
            MTEP.SceneEditorHack.Initialize();
```

- [ ] **Step 9: TimelineControlWindow.cs から IsValid 分岐を外す**

```csharp
// 36-37 行付近: 変更前
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.StudioHackBase studioHack => studioHackManager.studioHack;
// 変更後
        private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
```

`DrawControls` (384 行付近):

```csharp
// 変更前
            var isStudioHackValid = studioHack.IsValid();
            var isMaidValid = maidManager.IsValid();

            var editEnabled = isMaidValid
                            && isStudioHackValid
                            && timeline != null
                            && maidManager.maid != null;

            DrawFileMenu(view, editEnabled);
            DrawStatusMessage(view, isStudioHackValid, isMaidValid);
// 変更後
            var isMaidValid = maidManager.IsValid();

            var editEnabled = isMaidValid
                            && timeline != null
                            && maidManager.maid != null;

            DrawFileMenu(view, editEnabled);
            DrawStatusMessage(view, isMaidValid);
```

`OnSaveClicked` / `OnLoadClicked` (433-437, 451-455 行付近) の以下のブロックを削除:

```csharp
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
```

`DrawStatusMessage` (468 行付近): シグネチャを `(GUIView view, bool isMaidValid)` にし、先頭の `if (!isStudioHackValid) { view.DrawLabel(studioHack.errorMessage, ...); } else if (!isMaidValid)` を `if (!isMaidValid)` に変える。`else if (studioHackManager.isPoseEditing)` (489 行付近) は `else if (MTEP.SceneEditorHack.isPoseEditing)` にする。

- [ ] **Step 10: TimelineWindow.cs / MotionTimelineEditor.cs / TimelineXml.cs / ModelHackManager.cs / TimelineManager.cs の IsValid 分岐を外す**

`TimelineWindow.cs:514-517`:

```csharp
            bool editEnabled = maidManager.IsValid()
                            && timeline != null
                            && maidManager.maid != null;
```

`Timeline/MotionTimelineEditor.cs:55`: `if (studioHack == null || !studioHack.IsValid())` → `if (studioHack == null)`

`Timeline/TimelineXml.cs:1368-1374`:

```csharp
// 変更前
            var studioHack = StudioHackManager.instance.studioHack;
            if (studioHack == null)
            {
                return;
            }

            var currentPluginName = studioHack.pluginName;
// 変更後
            var currentPluginName = SceneEditorHack.pluginName;
```

(`pluginName` は定数になったので null ガードは不要。読込時の変換をタイトル画面で走らせる経路は無い。)

`Timeline/Manager/ModelHackManager.cs:111`: `if (studioHack != null && studioHack.IsValid())` → `if (studioHack != null)` (このブロック自体は Task 2 で削除する。ここではコンパイルを通すだけ)

`Timeline/Manager/TimelineManager.cs` の `CreateNewTimeline` (354-358) と `LoadTimeline` (387-391) から以下を削除:

```csharp
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
```

- [ ] **Step 11: SE 側 (AutoEditMode / Plugin.cs / EyesPosRowDrawer / LiveEffectWindow) を更新する**

`AutoEditMode.cs`:

```csharp
        public static void Enter()
        {
            if (MTEP.SceneEditorHack.instance == null)
            {
                // タイムライン側が未初期化 (タイトル画面等) なら SE 本体のフラグだけ立てる
                MaidManipulateManager.instance.isEditMode = true;
                return;
            }

            if (MTEP.SceneEditorHack.isPoseEditing)
            {
                return;
            }

            // SceneEditorHack 経由で入ると再生停止も一緒に行われる
            MTEP.SceneEditorHack.isPoseEditing = true;

            // スナップショットを同フレームで取る。
            // 翌フレームの TimelineManager.Update に任せると、このあと書く値が
            // OnPoseEditStart の ApplyCurrentFrame で上書きされる
            MTEP.TimelineManager.instance.SyncPoseEditing();
        }
```

`COM3D2.SceneEditor.Plugin.cs:223` のコメント `タイムライン側 (StudioHackManager.isPoseEditing)` → `タイムライン側 (SceneEditorHack.isPoseEditing)`。236-239 行付近:

```csharp
                if (MTEP.SceneEditorHack.instance != null)
                {
                    MTEP.SceneEditorHack.isPoseEditing = !MTEP.SceneEditorHack.isPoseEditing;
                }
```

`EyesPosRowDrawer.cs:40-41` のアクセサを削除し、259 行の `studioHackManager.isPoseEditing` を `MTEP.SceneEditorHack.isPoseEditing` にする。

`LiveEffectWindow.cs:29` のアクセサ行を削除する (未使用)。

- [ ] **Step 12: 残存参照が無いことを確認する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
grep -rn 'StudioHackBase\|StudioHackManager\|studioHackManager\|studioHack\.IsValid\|studioHack\.errorMessage\|isStudioHackValid' --include=*.cs .
```

Expected: 出力なし (コメント内の残骸も含めて消す)。

- [ ] **Step 13: 2 構成ビルド + テスト**

「ビルド・テストコマンド」を実行。
Expected: 2 構成とも `エラー 0`、`dotnet test` 全件 Passed (Step 1 のテストが Passed に変わる)。

- [ ] **Step 14: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs \
  source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs \
  source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs source/COM3D2.SceneEditor.Plugin/AutoEditMode.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/SceneEditorHackContractTests.cs
git status --short   # MaidGravityWindow.cs / GravityRowDrawer.cs がステージされていないことを確認
```

コミットは commit スキルで行う (メッセージ例: `refactor(timeline): StudioHack の多態機構を撤去し SceneEditorHack 単体へ平坦化する`)。

---

### Task 2: no-op 呼び出し・未使用メンバー・ModelHackManager の studioHack 依存を除去する

**Files:**
- Modify: `Timeline/BoneMenu/MaidBoneMenuItem.cs`
- Modify: `Timeline/DressUtils.cs:326,335`
- Modify: `Timeline/Manager/MaidManager.cs:339-342`
- Modify: `Timeline/Manager/PostEffectManager.cs:242`
- Modify: `Timeline/Manager/TimelineManager.cs:217-221`
- Modify: `Timeline/TimelineLayer/MotionTimelineLayer.cs:606,657`
- Modify: `Timeline/TimelineLayer/CameraTimelineLayer.cs:17,151-154`
- Modify: `Timeline/Manager/ModelHackManager.cs:19,42-47,104,111-114`
- Modify: `Timeline/Manager/StudioModelManager.cs:386-387`
- Modify: `Timeline/Hack/SceneEditorHack.cs` (未使用メンバー削除)

**Interfaces:**
- Consumes: Task 1 の `SceneEditorHack`
- Produces: `SceneEditorHack` から `isIKVisible` / `isUIVisible` / `motionSliderRate` / `subCamera` / `IsBackgroundVisible` / `GetMaidSlotNo` / `GetMaid` を削除。`ModelHackManager.GetOrDefault` は未登録時 null を返す

- [ ] **Step 1: MaidBoneMenuItem の isSelectedMenu を簡約する**

`HasBoneRotateVisible` が常に false だったので、getter は基底そのもの、setter は `partsEditHack` 処理 + 基底代入になる:

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class MaidBoneMenuItem : BoneMenuItem
    {
        public readonly IKManager.BoneType boneType;

        public override bool isSelectedMenu
        {
            get => base.isSelectedMenu;
            set
            {
                if (partsEditHack != null)
                {
                    partsEditHack.SetBone(null);
                }
                base.isSelectedMenu = value;
            }
        }

        public MaidBoneMenuItem(string name, string displayName) : base(name, displayName)
        {
            this.boneType = BoneUtils.GetBoneTypeByName(name);
        }
    }
}
```

- [ ] **Step 2: no-op 呼び出し行を削除する**

- `Timeline/DressUtils.cs` の `studioHack.UpdateUndress(maidCache.maid, slotId, isVisible);` 2 行
- `Timeline/Manager/MaidManager.cs:341` の `studioHack.ChangeMaid(maid);`
- `Timeline/Manager/PostEffectManager.cs:242` の `studioHack.OnUpdateDepthOfField();`
- `Timeline/TimelineLayer/MotionTimelineLayer.cs` の `studioHack.OnMotionUpdated(maid);` と `studioHack.OnUpdateMyPose(anmPath, isExist);`。後者を消すと `isExist` 変数 (654 行付近 `bool isExist = File.Exists(anmPath);`) が未使用になるので一緒に消す
- `Timeline/Manager/TimelineManager.cs:217-221`:

```csharp
// 変更前
            var isPoseEditing = SceneEditorHack.isPoseEditing;
            if (isPoseEditing && config.disablePoseHistory)
            {
                studioHack.ClearPoseHistory();
            }
// 変更後: ブロックごと削除。`isPoseEditing` ローカルが他で使われていなければ宣言も削除
```

`config.disablePoseHistory` 自体 (`Timeline/Config.cs:83`、`TimelineSettingWindow.cs:497`) は MTE 互換の設定項目なので残す。ただし効果が無くなるため `TimelineSettingWindow.cs:497` のトグルは削除し、`Config.cs:83` には `// MTE 設定ファイル互換のため残す。SceneEditor では効果なし` とコメントする。

- [ ] **Step 3: CameraTimelineLayer の subCamera 分岐を削除する**

`Timeline/TimelineLayer/CameraTimelineLayer.cs:17` の `private static Camera subCamera => studioHack.subCamera;` と、151-154 行の

```csharp
            if (subCamera != null)
            {
                subCamera.fieldOfView = viewAngle;
            }
```

を削除する (`SceneEditorHack.subCamera` は常に null。サブカメラは `SubCameraManager` が別経路で扱う)。

- [ ] **Step 4: SceneEditorHack から未使用メンバーを削除する**

`isIKVisible` / `isUIVisible` / `motionSliderRate` / `subCamera` / `IsBackgroundVisible` / `GetMaidSlotNo` / `GetMaid` を削除する。削除前に参照ゼロを確認:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
grep -rnE 'studioHack\.(isIKVisible|isUIVisible|motionSliderRate|subCamera|IsBackgroundVisible|GetMaidSlotNo|GetMaid\(|depthOfField)' --include=*.cs .
```

Expected: 出力なし (`depthOfField` は Task 1 Step 3 で既に落としているが、取りこぼし検出のため網に含める)。

- [ ] **Step 5: ModelHackManager から studioHack 依存を外す**

```csharp
// modelList (19 行付近): 削除
                _modelList.AddRange(studioHack.modelList);

// pluginNames (42-47 行付近): 変更前
                if (studioHack == null)
                {
                    return _pluginNames;
                }

                _pluginNames.Add(studioHack.pluginName);
// 変更後: ブロックごと削除 (一覧はモデル配置プロバイダの名前だけになる)

// GetOrDefault (104 行付近): 変更前
            // 見つからない場合はアクティブなStudioHackを返す
            return studioHack;
// 変更後
            // モデル配置プロバイダ未登録なら null。呼び出し側はすべて null チェック済み
            return null;

// DeleteAllModels (111-114 行付近): 削除
                if (studioHack != null)
                {
                    studioHack.DeleteAllModels();
                }

// ChangePluginName (195-212 行付近): prev/next が null になり得るようになるので早期 return を足す
// (旧実装は studioHack へのフォールバックで非 null が保証されていた。
//  TimelineXml.ConvertPlugin で "SceneEditor" に寄せられたモデルは modelHackMap にキーが無く null になる)
                var prevModelHack = GetOrDefault(model.pluginName);
                var nextModelHack = GetOrDefault(pluginName);

                if (prevModelHack == null || nextModelHack == null)
                {
                    MTEUtils.LogWarning(
                        "モデル配置プロバイダが見つからないためプラグインを変更できません: {0} -> {1}",
                        model.pluginName, pluginName);
                    return;
                }

                if (nextModelHack != prevModelHack)
                {
                    prevModelHack.DeleteModel(model);
                    nextModelHack.CreateModel(model);
                }
```

`Timeline/Manager/StudioModelManager.cs:386-387` のコメント

```csharp
            // プロバイダが無いとモデルの生成・削除が黙って no-op になる
            // (ModelHackManager.GetOrDefault が studioHack へフォールバックするため)。
```

を

```csharp
            // プロバイダが無いとモデルの生成・削除が黙って no-op になる
            // (ModelHackManager.GetOrDefault が null を返し、呼び出し側が読み飛ばすため)。
```

に直す。

`ModelManageRowDrawer.cs:62-64` の `pluginNames` コンボは、これまで先頭に "SceneEditor" が並んでいたが今後はプロバイダ名のみになる。`GetPluginIndex` が `IndexOf` で -1 を返すケースの扱いを読んで、既存の防御 (123-130 行付近) で足りることを確認する。足りなければ `currentIndex` を 0 にクランプする。

- [ ] **Step 6: 残存確認**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
grep -rnE 'studioHack\.(HasBoneRotateVisible|IsBoneRotateVisible|SetBoneRotateVisible|ClearBoneRotateVisible|ClearPoseHistory|UpdateUndress|OnMotionUpdated|OnUpdateMyPose|OnUpdateDepthOfField|ChangeMaid|DeleteAllModels|modelList)' --include=*.cs .
grep -n 'studioHack' Timeline/Manager/ModelHackManager.cs
```

Expected: どちらも出力なし。

- [ ] **Step 7: 2 構成ビルド + テスト**

「ビルド・テストコマンド」を実行。
Expected: 2 構成とも `エラー 0`、`dotnet test` 全件 Passed。

- [ ] **Step 8: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs \
  source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs
git status --short   # 無関係ファイルが混じっていないこと
```

commit スキルでコミット (メッセージ例: `refactor(timeline): SceneEditorHack の no-op 呼び出しと未使用メンバーを削除する`)。

---

### Task 3: コードレビューと実機確認項目の記録

**Files:**
- (コード変更なし。レビュー指摘があれば該当ファイル)

- [ ] **Step 1: code-review スキルでレビューする**

Task 1〜2 の差分 (`git diff main...HEAD -- source/`) を対象に code-review スキルを実行し、妥当な指摘を取り込む。取り込んだ場合は 2 構成ビルド + テストを再実行してから commit スキルでコミットする。

- [ ] **Step 2: 実機確認項目 (次回ゲーム起動時)**

ゲーム起動中は DLL がロックされ差し替えできないため、次回起動時に以下を確認する。確認結果は memory `timeline-window-integration` に追記する。

1. SceneEdit でタイムラインウィンドウが開き、`E` キー (EditModeToggle) で編集モードが入る/抜ける。抜けたとき再生が止まったままにならない
2. 編集モード外でパラメータを触ると自動で編集モードへ入る (`AutoEditMode.Enter` 経路)
3. タイトルへ戻ると TimelineWindow が「シーンが有効ではありません」を出し、SceneEdit へ戻ると復帰する (`SceneEditorHack.OnChangedSceneLevel` の順序が正しいこと)
4. モデルレイヤー: MIE でモデルを置き、タイムラインに列挙される。`ModelManageRowDrawer` のプラグインコンボが MIE 名だけになり、選択が崩れない
5. モデル入りの MTE 産 XML を読み込み、`pluginName` が "SceneEditor" へ変換されてモデルが復元される
6. 保存 / 読込ボタンが動く (IsValid 分岐の除去で挙動が変わっていないこと)

## レビュー却下メモ

(plan-review 2026-09-13: 🔴 1 件 / 🟡 3 件をすべて取り込み。却下なし)
