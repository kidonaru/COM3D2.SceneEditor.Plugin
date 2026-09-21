# アニメブレンド 再設計 (MTE 準拠) 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** アニメブレンドを「タイムライン再生の道具」として MTE と同じ位置づけに直し、専用ウィンドウへ移してポーズ編集と排他にする。

**Architecture:** 停止中サンプルからブレンド層を外し（`anim.Stop()` の前後で層を保つ処理は残す）、ボーンを実際に書く操作（ドラッグ点の掴み / ボーンスライダー）が始まったらブレンド層を解除する。UI は `MaidPoseWindow` の区間から独立ウィンドウ `MaidAnimationBlendWindow` へ移し、モーションの選択は `MaidPoseWindow` をピッカーモードで再利用する。`適用先` コンボは廃止する。

**Tech Stack:** C# (既存コードの範囲で `?.` / expression-bodied member 可)、Unity 5.6 legacy `Animation`、xunit (net48)、MSBuild 2 構成 (COM3D2 / COM3D25)。

**Spec:** `docs/superpowers/specs/2026-09-20-anime-blend-window-redesign.md`

## Global Constraints

- コメント・ログ・ダイアログ文言は日本語
- Unity ネイティブを呼ぶコードはテストしない。pure なロジックだけ切り出してテストする
- ビルドは COM3D2 → COM3D25 の順で両方通す。Git Bash からは `export MSYS2_ARG_CONV_EXCL="*"` を付け、
  `/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe` を直接叩く
  （`debug.bat` はゲーム停止中に実行すると実機へ反映されるため使わない）
- テスト実行: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test`（先に COM3D25 構成をビルドしておく）
- **新規 `.cs` は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` に手で追加する**（グロブではない）
- **ファイルは BOM なしで書く**（リポジトリの多数派。BOM を足すと差分ノイズになる）
- `git worktree` は使わない。作業ブランチは `main`
- 各タスクの最後に `commit` スキル相当のメッセージ（Conventional Commits の日本語）でコミットする
- レイヤー番号は `MTEP.MaidCache.MinLayerIndex` (=2) 〜 `MaxLayerIndex` (=8) を参照し、数値をハードコードしない

---

### Task 1: 停止中サンプルからブレンド層を外す

2026-09-19 版が入れた「停止中もブレンド層を乗せてサンプルする」経路だけを外す。
これが `AnimationTimelineLayer.OnPoseEditEnd` の思想と真逆で、ポーズ・履歴・キーの汚染源だった。

**`CaptureTimesBeforeStop` と `ResumeAfterPlay` は残す。** この 2 つは
「`anim.Stop()` が全 `AnimationState` を無効化して巻き戻す」ことへの対処であって、
停止中サンプルとは別の責務。消すと `MaidMotionState.StopMotion` を呼ぶ **18 箇所**
（`MaidFingerWindow` / `MaidIKHoldController` / `MaidPoseFileManager` /
`MaidManipulateManager` / `MaidBoneGizmoController` / `MotionTimelineLayer` /
`SceneEditorHack` など、Task 3 の解除トリガーに含まれない経路を多数含む）のいずれかを
通っただけで、`anmName` は残ったまま `state.enabled` が false のブレンド層が
再生を再開しても戻らなくなる。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidMotionState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs`

**Interfaces:**
- 削除: `MaidAnimationBlendController.EnableStatesForSample` / `DisableStatesAfterSample`
- 据え置き: `MaidAnimationBlendController.CaptureTimesBeforeStop` / `ResumeAfterPlay`
- 変更: `MaidMotionState.SampleWhileStopped(Maid, Animation, AnimationState, float)` → `SampleWhileStopped(Animation, AnimationState, float)`
- 変更: `MaidAnimationBlendController` の private `SampleStopped(Maid, Animation)` はベースのみで再サンプルする

- [ ] **Step 1: `SampleWhileStopped` からブレンド層を外す**

`maid` 引数ごと落として元の形へ戻す:

```csharp
        private static void SampleWhileStopped(Animation anim, AnimationState state, float time)
        {
            if (state == null)
            {
                return;
            }

            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            anim.Sample();
            state.enabled = false;
        }
```

呼び出し元 2 箇所を戻す:
- `StopMotion`: `SampleWhileStopped(anim, playingState, stoppedTime)`
- `ResetPoseWhileStopped`: `SampleWhileStopped(anim, state, state.time)`

**`StopMotion` の `MaidAnimationBlendController.CaptureTimesBeforeStop(maid);` の行は消さない。**

- [ ] **Step 2: `SetPlaybackTime` からブレンド層を外す**

`EnableStatesForSample` / `DisableStatesAfterSample` の呼び出しだけを消す
（`InvalidateIsPlayingCache` と `CaptureBasePose` は残す）:

```csharp
            var wasPlaying = anim.isPlaying;
            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            anim.Sample();

            if (!wasPlaying)
            {
                state.enabled = false;
                // シーク後のポーズをボーンスライダーの基準に取り直す
                MaidBoneSliderController.CaptureBasePose(maid);
            }
```

`PlayMotion` / `PlayClip` / `ResetPose` の `ResumeAfterPlay(maid);` は **4 箇所とも残す**。

- [ ] **Step 3: `MaidAnimationBlendController` からサンプル用フック 2 つを削除する**

`EnableStatesForSample` と `DisableStatesAfterSample` のメソッド本体を削除する。
`CaptureTimesBeforeStop` / `ResumeAfterPlay` は残し、セクションの見出しコメントを実態へ直す:

```csharp
        // ---- anim.Stop() は全 state を巻き戻すため、その前後で層の位置と有効状態を保つ ----
```

- [ ] **Step 4: `SampleStopped` をベースのみにする**

```csharp
        /// <summary>
        /// 停止中にベースのみで 1 フレームぶんサンプルする。
        /// ブレンド層は乗せない: 乗せるとボーンの Transform に寄与が焼き込まれ、
        /// ポーズ保存・履歴・タイムラインのキーが汚染される (MTE の OnPoseEditEnd と同じ考え方)。
        /// このためベース停止中はブレンドの結果が画面に出ない。再生を再開すると混ざる
        /// </summary>
        private static void SampleStopped(Maid maid, Animation anim)
        {
            var baseState = MaidMotionState.GetCurrentAnimationState(maid);
            if (baseState != null)
            {
                baseState.enabled = true;
                baseState.weight = 1f;
            }
            anim.Sample();
            if (baseState != null)
            {
                baseState.enabled = false;
            }
            MaidBoneSliderController.CaptureBasePose(maid);
        }
```

- [ ] **Step 5: 両構成をビルドし、テスト全体を通す**

Run:
```
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test
```
Expected: 両方ビルド成功、812 tests PASS

- [ ] **Step 6: コミット**

`fix(maid): 停止中のサンプルからアニメブレンド層を外す`

---

### Task 2: ブレンド層の解除 API を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidAnimationBlendReleaseTests.cs` (新規)

**Interfaces:**
- Consumes: `MaidAnimationBlendController.RemoveStateOnly`（既存 private）、`GetLayerInfos`
- Produces:
  - `static bool MaidAnimationBlendController.HasAnyLayer(Maid maid)` — 載っている層が 1 つでもあるか
  - `static bool MaidAnimationBlendController.ReleaseAll(Maid maid)` — 全層をクリップごと破棄して `Reset()`。解除した層があれば true
  - `static List<int> MaidAnimationBlendController.GetLoadedLayers(IList<string> anmNamesByLayer, int minLayer, int maxLayer)` — anmName が入っている層番号（pure、テスト対象）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 解除対象の層の洗い出しを固定する。
    /// 実際の解除は Unity の Animation を触るためテストしない
    /// </summary>
    public class MaidAnimationBlendReleaseTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 名前のある層だけを昇順で返す()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(Names, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 適用先と違い空の層は含めない()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(new string[9], 2, 8);
            Assert.Empty(result);
        }

        [Fact]
        public void 配列が短くても落ちない()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(new string[3] { "", "", "a.anm" }, 2, 8);
            Assert.Equal(new List<int> { 2 }, result);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter MaidAnimationBlendReleaseTests`
Expected: ビルドエラー（`GetLoadedLayers` が存在しない）

- [ ] **Step 3: 実装する**

`MaidAnimationBlendController` に追加する:

```csharp
        /// <summary>
        /// anmName が入っている層番号を昇順で返す。
        /// 解除対象の洗い出しに使う (Unity に触らない pure なロジック)
        /// </summary>
        public static List<int> GetLoadedLayers(IList<string> anmNamesByLayer, int minLayer, int maxLayer)
        {
            var result = new List<int>();
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(layer);
                }
            }
            return result;
        }

        /// <summary>載っている層が 1 つでもあるか</summary>
        public static bool HasAnyLayer(Maid maid)
        {
            var infos = GetLayerInfos(maid);
            if (infos == null)
            {
                return false;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && !string.IsNullOrEmpty(info.anmName))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 全てのブレンド層をクリップごと破棄して空に戻す。解除した層があれば true。
        /// ボーンを触る操作が始まったときに呼び、ブレンドの寄与がポーズへ焼き込まれるのを防ぐ
        /// (MTE の AnimationTimelineLayer.OnPoseEditEnd と同じ考え方)。
        /// 一方向の解除で、自動では再開しない
        /// </summary>
        public static bool ReleaseAll(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return false;
            }

            var released = false;
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || string.IsNullOrEmpty(info.anmName))
                {
                    continue;
                }
                RemoveStateOnly(maid, anim, info);
                info.Reset();
                released = true;
            }

            if (released && !MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
            return released;
        }
```

- [ ] **Step 4: 両構成をビルドし、テストを通す**

Run: Task 1 Step 4 と同じ MSBuild 2 回 + `dotnet test --filter MaidAnimationBlendReleaseTests`
Expected: ビルド成功、3 tests PASS

- [ ] **Step 5: コミット**

`feat(maid): アニメブレンド層の一括解除を追加する`

---

### Task 3: ボーンを触る操作でブレンドを解除する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/IMaidDragPoint.cs`（`MaidDragBoneTracker.BeginDrag`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneRotateDragPoint.cs:135`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs:83`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFingerDragPoint.cs:178`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKDragPoint.cs:138`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneSliderController.cs`（`SetOffsetAxis`）

**Interfaces:**
- Consumes: `MaidAnimationBlendController.HasAnyLayer / ReleaseAll`、`HistoryManager.instance.BeforeEdit`、`PoseSnapshot.GetAllBodyBones`
- 変更: `MaidDragBoneTracker.BeginDrag(string boneName)` → `BeginDrag(Maid maid, string boneName)`
- Produces: `static void MaidAnimationBlendController.ReleaseForBoneEdit(Maid maid)` — 履歴を積んでから `ReleaseAll`

**トリガーに `MaidManipulateManager.isEditMode` を使ってはいけない。**
`AutoEditMode.Enter` が全てのパラメータ変更で走るため、ブレンドの重みを動かすだけで
自分自身を解除してしまう。「実際にボーンを書く操作」だけを拾う。

- [ ] **Step 1: 履歴つき解除を `MaidAnimationBlendController` へ足す**

```csharp
        /// <summary>
        /// ボーンを触る操作の直前に呼ぶ解除。載っている層があるときだけ履歴を積んでから外す。
        /// ブレンド中のポーズをボーン編集の基準にすると寄与が分離できなくなるため、
        /// 先に落としてベースだけのポーズへ戻す。誤操作は Ctrl+Z で戻せる
        /// </summary>
        public static void ReleaseForBoneEdit(Maid maid)
        {
            if (!HasAnyLayer(maid))
            {
                return;
            }
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド解除", PoseSnapshot.GetAllBodyBones(maid));
            ReleaseAll(maid);
            MTEUtils.LogDebug("ボーン編集開始のためアニメブレンドを解除しました");
        }
```

`BeforeEdit` のラベルは履歴に出ないことがある。`HistoryManager.BeforeEditCore`
(`Manager/HistoryManager.cs:143-160`) は同じ `(maid, scope, targetKey)` の確定待ちが
既にあると `description` を上書きせず対象ボーンを足すだけで、5 つの呼び出し元は
いずれも自分の `BeforeEdit`（「ボーン回転: X」等）を先に呼ぶ。
ここでの `BeforeEdit` の役割は**ラベル表示ではなく、確定待ちが無い場合にスナップショットを
取らせること**。`PoseSnapshot.Capture` は `MaidAnimationBlendController.Capture` を含むため、
どちらの順序でも Ctrl+Z でブレンド層は戻る。

- [ ] **Step 2: `MaidDragBoneTracker.BeginDrag` に `maid` を足す**

`IMaidDragPoint.cs` の `MaidDragBoneTracker.BeginDrag` を差し替える:

```csharp
        /// <summary>
        /// 掴み開始。ボーンを書く操作なので、ここでアニメブレンドを解除する
        /// (ブレンド込みのポーズを基準にすると寄与が分離できなくなるため)
        /// </summary>
        public static void BeginDrag(Maid maid, string boneName)
        {
            MaidAnimationBlendController.ReleaseForBoneEdit(maid);
            draggingBoneName = boneName;
            isDragging = true;
        }
```

- [ ] **Step 3: 呼び出し元 4 箇所に `maid` を渡す**

それぞれ `maid` がスコープにあることを確認して置き換える:

```csharp
// MaidBoneRotateDragPoint.cs:135
            MaidDragBoneTracker.BeginDrag(maid, GetTrackedBoneName());

// MaidFaceDragPoint.cs:83
                MaidDragBoneTracker.BeginDrag(maid, neckBone.name);

// MaidFingerDragPoint.cs:178
            MaidDragBoneTracker.BeginDrag(maid, followBone.name);

// MaidIKDragPoint.cs:138
            MaidDragBoneTracker.BeginDrag(maid, sliderBoneName ?? followBone.name);
```

`maid` という名前のフィールド／プロパティが無い点があればそのクラスの対象メイド参照へ読み替える
（4 クラスとも `MaidMotionState.StopMotion(maid)` 等で既に `maid` を使っている）。

`MaidFaceDragPoint` の目線モード (`_isEyeMode`) は `MaidDragBoneTracker.BeginDrag` を
呼ばない経路だが、書き換えるのは `quaDefEyeL` / `quaDefEyeR` だけで、
アニメがサンプルするボーン Transform には触れないため解除は不要。
`BeginDrag` を呼ばない理由のコメント（「瞳モードはボーンを回さないので追従させない」）の
すぐ下に、解除も不要である旨を 1 行足しておく。

- [ ] **Step 4: ボーンスライダーでも解除する**

`MaidBoneSliderController.SetOffsetAxis` の先頭へ:

```csharp
        public static void SetOffsetAxis(Maid maid, BoneSliderDef def, int axisIndex, float value)
        {
            var bone = GetBone(maid, def.boneName);
            if (bone == null)
            {
                return;
            }

            // スライダーもボーンを書く操作なのでブレンドを落とす
            MaidAnimationBlendController.ReleaseForBoneEdit(maid);

            var baseRot = GetBaseRotation(maid, def, bone);
            _offsetCache.SetOffsetAxisFromLocalBase(bone, baseRot, axisIndex, value, true);
        }
```

- [ ] **Step 5: 両構成をビルドし、テスト全体を通す**

Run: Task 1 Step 4 と同じ
Expected: ビルド成功、815 tests PASS

- [ ] **Step 6: コミット**

`feat(maid): ボーンを触る操作でアニメブレンドを解除する`

---

### Task 4: `IsMotionEditingState` を MTE と揃える

MTE は `MotionTimelineLayer` に加えて `MoveTimelineLayer` も条件に入れている。
SceneEditor の移植で落ちていたので足す。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:2741-2746`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/PoseEditNullLayerGuardTests.cs`（既存に追記）

**Interfaces:**
- Produces: `static bool TimelineManager.IsMotionEditingState(bool isPoseEditing, ITimelineLayer currentLayer)`（シグネチャ据え置き、判定条件のみ変更）

- [ ] **Step 1: 失敗するテストを追記する**

`PoseEditNullLayerGuardTests` に足す:

```csharp
        [Fact]
        public void ポーズ編集中の移動レイヤーもモーション編集中になる()
        {
            // MTE の UpdateMotionEditing は MotionTimelineLayer と MoveTimelineLayer を
            // 同じ扱いにしている。どちらもボーンを書くレイヤーなのでブレンドを無効化する
            var layer = MoveTimelineLayer.Create(0);

            Assert.True(TimelineManager.IsMotionEditingState(true, layer));
        }
```

`MoveTimelineLayer.Create(0)` のシグネチャが `MotionTimelineLayer.Create(0)` と異なる場合は
`MoveTimelineLayer.cs` の `Create` に合わせる。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter PoseEditNullLayerGuardTests`
Expected: `ポーズ編集中の移動レイヤーもモーション編集中になる` が FAIL

- [ ] **Step 3: 判定条件を足す**

```csharp
        /// <summary>
        /// モーション編集中か。アクティブレイヤーは無いことがある
        /// (タイムライン未作成のままライト等を触ると AutoEditMode.Enter が
        ///  IsValidData のガードを通らずにここまで来る) ので null を許す。
        /// 移動レイヤーもボーンを書くため、MTE と同じくモーションレイヤーと同じ扱いにする
        /// </summary>
        public static bool IsMotionEditingState(bool isPoseEditing, ITimelineLayer currentLayer)
        {
            if (!isPoseEditing || currentLayer == null)
            {
                return false;
            }
            return currentLayer.layerType == typeof(MotionTimelineLayer)
                || currentLayer.layerType == typeof(MoveTimelineLayer);
        }
```

- [ ] **Step 4: 両構成をビルドし、テスト全体を通す**

Run: Task 1 Step 4 と同じ
Expected: ビルド成功、816 tests PASS

- [ ] **Step 5: コミット**

`fix(timeline): 移動レイヤー編集中もモーション編集中として扱う`

---

### Task 5: `MaidPoseWindow` をピッカーモード対応にし、ブレンド区間を外す

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MotionPickRequest.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MotionPickRequestTests.cs`

**Interfaces:**
- Produces:
  - `sealed class MotionPickRequest { Maid maid; int layer; Action<PhotoMotionData> onMotionPicked; Action<string, string> onMyPosePicked; bool IsValidFor(Maid) }`
  - `void MaidPoseWindow.BeginPick(MotionPickRequest request)` — ピッカーモードへ入り、ウィンドウを表示する
  - `void MaidPoseWindow.CancelPick()`
  - `bool MaidPoseWindow.isPicking`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ピッカー要求の有効判定を固定する。
    /// 対象メイドが変わったらピッカーを畳む (別のメイドの層へ載せない)
    /// </summary>
    public class MotionPickRequestTests
    {
        [Fact]
        public void メイドがnullの要求は無効()
        {
            var request = new MotionPickRequest { maid = null, layer = 2 };

            Assert.False(request.IsValidFor(null));
        }

        [Fact]
        public void レイヤーが範囲外の要求は無効()
        {
            var request = new MotionPickRequest { maid = null, layer = 0 };

            Assert.False(request.IsValidFor(null));
        }
    }
}
```

`Maid` は Unity のオブジェクトなのでテストでは `null` しか渡せない。
`IsValidFor` は「maid が非 null かつ引数と同一」「layer が範囲内」の 2 条件で、
テストでは無効側だけを固定する。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter MotionPickRequestTests`
Expected: ビルドエラー（`MotionPickRequest` が存在しない）

- [ ] **Step 3: `MotionPickRequest` を実装する**

```csharp
using System;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「モーションウィンドウの一覧から 1 件選んでほしい」という要求。
    /// アニメブレンドウィンドウが層へ載せるモーションを選ばせるために使う。
    /// 一覧の描画 (カテゴリ / 検索 / マイポーズのフォルダ移動) を複製しないための仕組み
    /// </summary>
    public sealed class MotionPickRequest
    {
        /// <summary>載せる対象のメイド</summary>
        public Maid maid;

        /// <summary>載せ先のアニメレイヤー</summary>
        public int layer;

        /// <summary>モーション一覧から選ばれたとき</summary>
        public Action<PhotoMotionData> onMotionPicked;

        /// <summary>マイポーズ一覧から選ばれたとき (第 1 引数はフォルダ、第 2 引数はポーズ名)</summary>
        public Action<string, string> onMyPosePicked;

        /// <summary>
        /// この要求が今も有効か。対象メイドが変わったり外れたりしたら畳む
        /// (別のメイドの層へ載ってしまわないように)
        /// </summary>
        public bool IsValidFor(Maid currentMaid)
        {
            return maid != null
                && maid == currentMaid
                && layer >= MaidAnimationBlendController.MinLayer
                && layer <= MaidAnimationBlendController.MaxLayer;
        }
    }
}
```

`.csproj` の `<Compile Include="MaidPoseBlendRows.cs" />` の隣へ
`<Compile Include="MotionPickRequest.cs" />` を追加する。

- [ ] **Step 4: `MaidPoseWindow` のブレンド区間と適用先を削除する**

次を全て消す:

- フィールド `_blendTargetLayer` / `_blendTargetMaid`
- メソッド `DrawBlendSection` / `ApplyMotionEntryToTarget` / `LoadMyPoseEntryToTarget`
- `DrawMaidContent` の `DrawBlendSection(view, target);` の行

一覧クリックは元の静的メソッドへ戻す:

```csharp
                    ApplyMotionEntry(maid, data);
```
```csharp
                    LoadMyPoseEntry(maid, myPoseDir, poseName);
```

`Config.maidPoseBlendVisible` は Task 6 でブレンドウィンドウの表示フラグへ置き換えるため、
ここでは消さずに残しておく。

- [ ] **Step 5: ピッカーモードを足す**

フィールド:

```csharp
        /// <summary>ピッカー要求。非 null の間は一覧クリックが層への適用になる</summary>
        private MotionPickRequest _pickRequest;

        public bool isPicking => _pickRequest != null;
```

公開メソッド:

```csharp
        /// <summary>
        /// 一覧から 1 件選ばせるモードへ入る。ウィンドウが閉じていれば開く
        /// (アニメブレンドウィンドウから呼ばれる)
        /// </summary>
        public void BeginPick(MotionPickRequest request)
        {
            _pickRequest = request;
            if (request != null && !isShowWnd)
            {
                // ヘッダーの重なり判定込みで開く (メニューバーのトグルと同じ経路)
                WindowManager.ToggleWindowVisible(this);
            }
        }

        public void CancelPick()
        {
            _pickRequest = null;
        }

        /// <summary>ウィンドウを閉じたらピッカーも畳む (帯だけ残って見えなくならないように)</summary>
        protected override void OnShowChanged(bool visible)
        {
            base.OnShowChanged(visible);
            if (!visible)
            {
                CancelPick();
            }
        }
```

表示切替は `EditorSubWindow.isShowWnd`（`EditorSubWindow.cs:132`）で、
メニューバーは `WindowManager.ToggleWindowVisible`（`Manager/WindowManager.cs:190`）を通す。
`visible` という公開プロパティは無いので使わないこと。
`OnShowChanged` は `protected virtual`（`EditorSubWindow.cs:493`）なので上書きできる。

`DrawMaidContent` の `TimelineLayerGate.Begin(...)` の直前にピッカーの帯を描く:

```csharp
            // ピッカーモードの帯。対象メイドが変わったら畳む
            if (_pickRequest != null && !_pickRequest.IsValidFor(target))
            {
                CancelPick();
            }
            if (_pickRequest != null)
            {
                view.BeginHorizontal();
                {
                    view.DrawLabel("▶ レイヤー" + _pickRequest.layer + " へ載せる", -1, ROW_HEIGHT,
                        textColor: Color.yellow);
                    if (view.DrawButton("キャンセル", 90, ROW_HEIGHT))
                    {
                        CancelPick();
                    }
                }
                view.EndLayout();
                view.DrawHorizontalLine();
            }
```

一覧クリックをピッカーへ振り分ける。`DrawMotionButtons` のクリック処理:

```csharp
                    if (_pickRequest != null)
                    {
                        var request = _pickRequest;
                        CancelPick();
                        request.onMotionPicked?.Invoke(data);
                    }
                    else
                    {
                        ApplyMotionEntry(maid, data);
                    }
```

`DrawMyPoseEntries` のクリック処理:

```csharp
                    if (_pickRequest != null)
                    {
                        var request = _pickRequest;
                        CancelPick();
                        request.onMyPosePicked?.Invoke(myPoseDir, poseName);
                    }
                    else
                    {
                        LoadMyPoseEntry(maid, myPoseDir, poseName);
                    }
```

ピッカー中はベースを触る操作を塞ぐ。`DrawPlaybackRows` の
`再生中` の `<` / `>`、`ポーズ保存`、`反転`、`リセット` の各 `DrawButton` の
`enabled:` に `!isPicking` を足す（`enabled:` を渡していないボタンには `enabled: !isPicking` を追加する）。

- [ ] **Step 6: 両構成をビルドし、テストを通す**

Run: Task 1 Step 4 と同じ
Expected: ビルド成功、818 tests PASS

- [ ] **Step 7: コミット**

`feat(pose): モーションウィンドウにピッカーモードを足しブレンド区間を外す`

---

### Task 6: `MaidAnimationBlendWindow` を新設する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidAnimationBlendWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseBlendRows.cs`（適用先コンボを外し、`選択` と `時間上書き` を足す）
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:96-99` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:155-158` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/MaidPoseBlendRowsTests.cs`

**Interfaces:**
- Consumes: `MaidPoseWindow.instance.BeginPick`、`MotionPickRequest`、`MaidAnimationBlendController` 全般、`TimelineLayerGate.Begin/End`
- Produces:
  - `MaidAnimationBlendWindow.instance`（`WINDOW_ID = 8903361`）
  - `static void MaidPoseBlendRows.Draw(GUIView view, Maid maid, float rowHeight, float labelWidth)` — 適用先の引数が消える
  - `static List<int> MaidPoseBlendRows.GetVisibleLayers(IList<string> anmNamesByLayer, int minLayer, int maxLayer)` — 適用先の引数が消える

- [ ] **Step 1: `GetVisibleLayers` のテストを新しい形へ直す**

`MaidPoseBlendRowsTests` を差し替える。適用先が無くなったので「空の層は常に 1 行だけ末尾に見せる」
（`未設定` の行が 1 つも無いと新規に載せられなくなるため）に変える:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンドウィンドウに描く層の決定を固定する。
    /// 載っている層に加えて、空いている先頭の層を 1 つだけ「未設定」行として見せる
    /// (7 段すべて並べると縦に長くなりすぎ、1 つも無いと新規に載せられない)
    /// </summary>
    public class MaidPoseBlendRowsTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 載っている層と空き先頭の層を昇順で返す()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 2, 8);
            Assert.Equal(new List<int> { 2, 3, 4 }, result);
        }

        [Fact]
        public void 何も載っていなければ先頭の層だけ()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(new string[9], 2, 8);
            Assert.Equal(new List<int> { 2 }, result);
        }

        [Fact]
        public void 全て埋まっていれば空き行は出ない()
        {
            var full = new string[] { "", "", "a", "b", "c", "d", "e", "f", "g" };

            var result = MaidPoseBlendRows.GetVisibleLayers(full, 2, 8);
            Assert.Equal(new List<int> { 2, 3, 4, 5, 6, 7, 8 }, result);
        }

        [Fact]
        public void 配列が短くても落ちない()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(new string[3] { "", "", "a.anm" }, 2, 8);
            Assert.Equal(new List<int> { 2, 3 }, result);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter MaidPoseBlendRowsTests`
Expected: ビルドエラー（引数の数が合わない）

- [ ] **Step 3: `GetVisibleLayers` を実装し直す**

```csharp
        /// <summary>
        /// 描く層の番号。載っている層に加えて、空いている先頭の層を 1 つだけ含める。
        /// (7 段すべて並べると縦に長くなりすぎ、1 つも無いと新規に載せられない)
        /// </summary>
        public static List<int> GetVisibleLayers(IList<string> anmNamesByLayer, int minLayer, int maxLayer)
        {
            var result = new List<int>();
            var emptyAdded = false;
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(layer);
                    continue;
                }
                if (!emptyAdded)
                {
                    result.Add(layer);
                    emptyAdded = true;
                }
            }
            result.Sort();
            return result;
        }
```

- [ ] **Step 4: `MaidPoseBlendRows` を適用先なしの形へ直す**

`_targetComboBox` / `_targetItems` / `BaseLayer` / 適用先の行を削除し、
`Draw` のシグネチャから `targetLayer` と `setTargetLayer` を落とす:

```csharp
        public static void Draw(GUIView view, Maid maid, float rowHeight, float labelWidth)
        {
            var infos = MaidAnimationBlendController.GetLayerInfos(maid);
            if (infos == null)
            {
                view.DrawLabel("アニメレイヤーの情報がありません", -1, rowHeight, textColor: Color.gray);
                return;
            }

            var names = new List<string>(infos.Count);
            foreach (var info in infos)
            {
                names.Add(info.anmName);
            }

            foreach (var layer in GetVisibleLayers(names,
                MaidAnimationBlendController.MinLayer, MaidAnimationBlendController.MaxLayer))
            {
                DrawLayer(view, maid, infos[layer], rowHeight, labelWidth);
            }
        }
```

`DrawLayer` の 1 行目のボタン並びへ `選択` を足す（`AddRightAlignSpace` の幅も合わせる）:

```csharp
                view.AddRightAlignSpace(50 + 30 + 50 + view.margin * 2, rowHeight);

                if (view.DrawButton("選択", 50, rowHeight))
                {
                    BeginPick(maid, layer);
                }

                var playing = MaidAnimationBlendController.IsLayerPlaying(maid, layer);
                // 層の ▶ はベース再生中しか効かない (停止編集を崩さない)
                if (view.DrawButton(playing ? "■" : "▶", 30, rowHeight,
                    enabled: hasState && (playing || MaidMotionState.IsPlaying(maid))))
                {
                    ...
                }

                if (view.DrawButton("削除", 50, rowHeight, enabled: hasState))
                {
                    ...
                }
```

`BeginPick` を足す:

```csharp
        /// <summary>
        /// モーションウィンドウをピッカーとして開く。
        /// 一覧描画を複製しないため、選択だけ向こうに任せて結果を受け取る
        /// </summary>
        private static void BeginPick(Maid maid, int layer)
        {
            MaidPoseWindow.instance.BeginPick(new MotionPickRequest
            {
                maid = maid,
                layer = layer,
                onMotionPicked = data =>
                {
                    AutoEditMode.Enter();
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド適用: " + data.name, PoseSnapshot.GetAllBodyBones(maid));
                    if (!MaidAnimationBlendController.ApplyMotion(maid, layer, data))
                    {
                        DialogPopupWindow.ShowDialog("このモーションはブレンドできません (スクリプト経由か読み込みに失敗)");
                    }
                },
                onMyPosePicked = (dir, poseName) =>
                {
                    AutoEditMode.Enter();
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド適用: " + poseName, PoseSnapshot.GetAllBodyBones(maid));
                    if (!MaidAnimationBlendController.ApplyMyPose(maid, layer,
                        System.IO.Path.Combine(dir, poseName)))
                    {
                        DialogPopupWindow.ShowDialog("ポーズの読み込みに失敗しました");
                    }
                },
            });
        }
```

`ループ` のトグル行へ `時間上書き` を足す（MTE にあって SceneEditor に無かった項目。
`AnimationLayerInfo.overrideTime` は既存で `TransformDataAnimation` がシリアライズする）:

```csharp
                view.DrawToggle("ループ", info.loop, 70, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンドループ", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetLoop(maid, layer, value);
                });

                view.DrawToggle("時間上書き", info.overrideTime, 90, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド時間上書き", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetOverrideTime(maid, layer, value);
                });
```

`MaidAnimationBlendController` へ `SetOverrideTime` を足す:

```csharp
        /// <summary>
        /// タイムライン再生時に層の時間をフレームで上書きするか (MTE の「時間上書き」)。
        /// 実 AnimationState には反映しない。タイムラインの CalcAnimationTime だけが見る値
        /// </summary>
        public static void SetOverrideTime(Maid maid, int layer, bool overrideTime)
        {
            var info = GetLayerInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.overrideTime = overrideTime;
        }
```

`LayerState` に `overrideTime` を足し、`Capture` / `Restore` / `Approximately` でも扱う
（履歴で戻らないと他の項目と挙動が揃わない）:

```csharp
            public bool overrideTime;
```
`Approximately` の最初の比較ブロックへ `|| overrideTime != other.overrideTime` を追加。
`Capture` の初期化子へ `overrideTime = info.overrideTime,` を追加。
`Restore` の値の書き戻しへ `info.overrideTime = target.overrideTime;` を追加。
`MaidAnimationBlendLayerStateTests` の `Create` に `bool overrideTime = false` を足し、
`名前_ループ_再生中の違いは別物` へ
`Assert.False(Create().Approximately(Create(overrideTime: true)));` を追加。

- [ ] **Step 5: Config にウィンドウの位置・サイズ・表示を足す**

`Config.cs` の `maidPoseBlendVisible` を消し、`maidIK*` の並びに倣って追加する:

```csharp
        public int maidAnimationBlendPosX = -1;
        public int maidAnimationBlendPosY = -1;
        public int maidAnimationBlendWidth = 320;
        public int maidAnimationBlendHeight = 400;
        public bool maidAnimationBlendVisible = false;
```

- [ ] **Step 6: ウィンドウ本体を実装する**

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// アニメブレンドウィンドウ。ベースモーションの上にアニメレイヤー 2〜8 を重ねる。
    /// モーションの選択はモーションウィンドウをピッカーとして借りる (一覧描画を複製しない)。
    /// ボーンを触る操作が始まると層は解除される (MaidAnimationBlendController.ReleaseForBoneEdit)
    /// </summary>
    public class MaidAnimationBlendWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903361;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "アニメブレンド";

        private static MaidAnimationBlendWindow _instance = null;
        public static MaidAnimationBlendWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidAnimationBlendWindow();
                }
                return _instance;
            }
        }

        private MaidAnimationBlendWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidAnimationBlendPosX;
            y = config.maidAnimationBlendPosY;
            width = config.maidAnimationBlendWidth;
            height = config.maidAnimationBlendHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidAnimationBlendPosX = x;
            config.maidAnimationBlendPosY = y;
            config.maidAnimationBlendWidth = width;
            config.maidAnimationBlendHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidAnimationBlendVisible;
            set => config.maidAnimationBlendVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                view.DrawLabel("メイドを選択してください", -1, ROW_HEIGHT);
                return;
            }

            MaidAnimationBlendController.SyncFromAnimation(target);

            // 停止中のサンプルはベースのみなので、止めているとブレンドの見た目が出ない。
            // 「効いていない」と誤解されないよう理由を出しておく
            if (!MaidMotionState.IsPlaying(target))
            {
                view.DrawLabel("ベース再生中に反映されます", -1, ROW_HEIGHT, textColor: Color.gray);
            }

            // ウィンドウ全体をアニメレイヤーのゲートで囲む。
            // モーションウィンドウ内で張り直していた頃と違い、注意ラベルは 1 回で済む
            TimelineLayerGate.Begin(view, typeof(MTEP.AnimationTimelineLayer), target, ROW_HEIGHT);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            MaidPoseBlendRows.Draw(view, target, ROW_HEIGHT, LABEL_WIDTH);
            view.EndScrollView();

            TimelineLayerGate.End(view);
        }
    }
}
```

`MaidWindowBase` / `EditorSubWindow` の実際の抽象メンバー（`DrawMaidContent` の引数、
`savedVisible` の有無、スクロールビューの開始メソッド名）は `MaidIKWindow.cs` と
`MaidPoseWindow.cs` を読んで合わせること。上のコードは `MaidIKWindow` の形に倣っている。

`.csproj` へ `<Compile Include="MaidAnimationBlendWindow.cs" />` を追加する。

- [ ] **Step 7: ウィンドウを登録する**

`Manager/WindowManager.cs` の `AddWindow(MaidIKWindow.instance);` の隣へ:

```csharp
            AddWindow(MaidAnimationBlendWindow.instance);
```

`MenuBarWindow.cs` の `CreateWindowItem("IK", MaidIKWindow.instance),` の隣へ:

```csharp
                        CreateWindowItem("アニメブレンド", MaidAnimationBlendWindow.instance),
```

- [ ] **Step 8: 両構成をビルドし、テストを通す**

Run: Task 1 Step 4 と同じ
Expected: ビルド成功、全 tests PASS

- [ ] **Step 9: 実機で確認する (COM3D2.5)**

新 DLL を反映してゲームを起動し、次を確認する。

**前提: ブレンドの見た目はベース再生中にしか出ない**（停止中のサンプルはベースのみのため）。
以下の「混ざる」系の確認は、必ずベースを ▶ で再生してから行うこと。

- `メイド` → `アニメブレンド` でウィンドウが開く
- レイヤー2 の行の `選択` → モーションウィンドウが開き `▶ レイヤー2 へ載せる [キャンセル]` の帯が出る。
  ピッカー中は `<` `>` / `ポーズ保存` / `反転` / `リセット` が押せない
- 一覧のモーションをクリック → 帯が消え、レイヤー2 に載って混ざって動く。空き行 (レイヤー3) が増える
- `重み` 0.5 で半分混ざる。`速度` / `ループ` / `時間上書き` が効く
- `削除` → ベースだけに戻る。Ctrl+Z で層が戻る
- **ボーンのドラッグ点を掴む → ブレンドが解除され、ベースだけのポーズになる。
  Ctrl+Z でブレンド層が戻る**（履歴のラベルは「ボーン回転: …」等その操作名のままで、
  `ブレンド解除` という項目は出ない。`BeforeEditCore` が確定待ちへ統合するため）
- **ボーンスライダーを動かしても同じく解除される**
- ブレンドの `重み` スライダーを動かしても解除されない（`AutoEditMode.Enter` で自己解除しないこと）
- 同じモーションをレイヤー2 → 3 に続けて適用 → レイヤー2 が解放され警告ログ
- タイムラインのモーションレイヤーをアクティブにしてポーズ編集 → 終了で層が落ちる（MTE と同じ）
- タイムライン読込中、アニメレイヤー未登録ならウィンドウが無効化され `追加` ボタンが出る
- スクリプト経由モーションをレイヤーへ → ダイアログ
- ピッカー中に対象メイドを切り替える → 帯が消える

問題があれば該当タスクを直してから進む。

- [ ] **Step 10: コミット**

`feat(maid): アニメブレンドを専用ウィンドウへ移す`

---

### Task 7: ドキュメントを直す

**Files:**
- Modify: `docs-site/guide/maid-editing.md`（「### アニメブレンド」節）

- [ ] **Step 1: 節を書き直す**

「## モーションとポーズ」の中にある `### アニメブレンド` 節を、独立ウィンドウの説明へ差し替える。
`## ボーン編集` の直前から移し、`メイド` → `アニメブレンド` のウィンドウとして書く:

```markdown
## アニメブレンド

`メイド` → `アニメブレンド` でアニメブレンドウィンドウを開きます。
ベースモーションの上に別のモーションやマイポーズをアニメレイヤー (2〜8) へ重ねて再生できます。

| 項目 | 内容 |
|---|---|
| `選択` | モーションウィンドウをピッカーとして開き、一覧から選んだものをそのレイヤーへ載せます |
| `▶` / `■` | そのレイヤーだけを再生 / 停止します (ベースが停止中は操作できません) |
| `削除` | レイヤーからアニメを外します |
| `開始時間` | レイヤーの再生位置。停止中は即座にそのフレームが混ざります |
| `重み` | 混ぜる割合 (0〜1) |
| `速度` | レイヤーの再生速度 (0〜2) |
| `ループ` | レイヤーをループ再生します |
| `時間上書き` | タイムライン再生時に、レイヤーの時間をフレーム位置で上書きします |

`選択` を押すとモーションウィンドウに `▶ レイヤーN へ載せる` の帯が出ます。
一覧から選ぶと載り、`キャンセル` で戻ります。帯が出ている間はベースを触る操作
(`<` `>` / `ポーズ保存` / `反転` / `リセット`) が使えません。

ボーン編集との関係:

- **ドラッグ点を掴む、またはボーンスライダーを動かすと、ブレンドは解除されます**。
  ブレンド込みのポーズをボーン編集の基準にすると、保存したポーズやタイムラインのキーに
  ブレンド分が焼き込まれてしまうためです。解除は履歴に積まれるので Ctrl+Z で戻せます
- タイムラインでモーションレイヤー・移動レイヤーを編集しているときも、同じ理由で解除されます

載せられないモーション:

- スクリプト経由で再生されるモーション（エディットカテゴリの一部）はレイヤーへ載せられません
- 同じモーションを複数のレイヤーへ同時に重ねると、後から載せた側が有効になり、
  先に載っていたレイヤーは自動的に外れます

タイムラインとの連携:

- タイムライン読込中は「メイドアニメブレンド」レイヤーの範囲として扱われ、値の変更がキーになります
- Mod モーションを載せた状態はタイムライン XML に絶対パスで保存されるため、別環境では再生できません
```

- [ ] **Step 2: docs-review スキルでレビューし、指摘を反映する**

- [ ] **Step 3: コミット**

`docs(docs-site): アニメブレンドの別ウィンドウ化を反映する`

---

## 自己レビュー結果

- **Spec coverage**:
  - A. 別ウィンドウ化 → Task 6
  - B. モーションウィンドウをピッカーに → Task 5 + Task 6 Step 4
  - C-1. `IsMotionEditingState` に `MoveTimelineLayer` → Task 4
  - C-2. ボーン編集開始で解除 → Task 2 + Task 3
  - D. 停止編集フックの撤去 → Task 1
  - `時間上書き` の追加 → Task 6 Step 4
  - ドキュメント → Task 7
- **型の一貫性**: `GetVisibleLayers` は Task 6 で引数が `(names, minLayer, maxLayer)` の 3 つに変わる。
  Task 2 で足す `GetLoadedLayers` とは別物（前者は空き行を 1 つ含める、後者は含めない）で、
  用途が違うため統合しない
- **既知の割り切り**:
  - ブレンド解除は一方向で、ボーン編集をやめても自動では戻らない（MTE も戻さない）。
    誤操作の救済は履歴（Ctrl+Z）に任せる
  - `MotionPickRequest.IsValidFor` は `Maid` を引数に取るためテストでは無効側しか固定できない
  - ピッカーは同時に 1 件だけ。複数レイヤーへ続けて載せるには `選択` を押し直す
  - `MaidPoseBlendRows` はウィンドウ専用になるが、行描画としてファイルは分けたまま残す
    （`MaidAnimationBlendWindow` が肥大化しないように）
  - **ベース停止中はブレンドの結果が画面に出ない**。停止中にブレンドを足しても、
    再生を再開するまで見た目は変わらない。ウィンドウ側では停止中に
    「ベース再生中に反映されます」の注記行を出す（Task 6 Step 6 のウィンドウ実装に含める）
  - ピッカー中に別の行の `選択` を押すと要求が差し替わる（同時に 1 件だけ）
  - ピッカー中に Undo 等で対象レイヤーが空になっても要求は残る。選んだものは
    その空レイヤーへ新規に載るだけなので実害はないと判断し、生存チェックは入れない

## レビュー却下メモ

- 解除トリガーを個別列挙ではなく `MaidMotionState.StopMotion` 自体へ寄せる — 却下。
  ブレンド適用・値変更は `HistoryManager.BeforeEdit` 経由で `AutoEditMode.Enter` を呼び、
  それが `MaidManipulateManager.isEditMode` の setter で全メイドを `StopMotion` するため、
  ブレンドを操作するたびに自分自身を解除する循環になる。
  `CaptureTimesBeforeStop` / `ResumeAfterPlay` を残す（Task 1 の修正）ことで、
  この指摘が挙げていた「StopMotion 経由でブレンドが沈黙する」問題は解消される
- ブレンドウィンドウを開いたままメイドを切り替えた場合の表示更新 — 却下。
  `DrawMaidContent(Maid target)` は毎フレーム現在の対象を受け取り `SyncFromAnimation(target)` を
  呼ぶ構造なので、前のメイドの層情報が残る余地がない
