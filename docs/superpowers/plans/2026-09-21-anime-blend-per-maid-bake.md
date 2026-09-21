# アニメブレンド: メイド単位の状態化と編集ポーズの anm 化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** アニメブレンドの選択状態とロックをメイド単位にし、停止中は編集モード外ならベースタブでもブレンドを見せ、手編集は編集モードを抜けるときに anm 化してベースへ差し替える。あわせてシーンプリセットへの層保存、`時間上書き` の無効化、タイムライン側解除の履歴化を行う。

**Architecture:** 状態は `MaidAnimationBlendController` にメイドごとの `BlendEditState` として持ち、静的フラグ `isBlendLayerSelected` を廃止する。停止中に層を残すかは pure な `ShouldKeepLayersWhileStopped(isLayerSelected, isEditMode)` 1 本で決め、編集モードの出入りは `MaidManipulateManager.isEditMode` の setter から通知する。anm 化はシーンプリセット復元と同じ `CapturePoseBinary` → `ApplyPoseBinary` 経路を `MaidEditPoseBaker` に切り出す。

**Tech Stack:** C# (LangVersion 9, .NET Framework 4.8 / Unity 5.6 系 API)、xunit、MSBuild 2 構成 (COM3D2 / COM3D25)

**Spec:** `docs/superpowers/specs/2026-09-21-anime-blend-per-maid-bake-design.md`

## Global Constraints

- コードのコメントとログ文言は日本語
- テストは Unity ネイティブ (Transform / Animation / Quaternion.Euler 等) を呼べない。pure なロジックだけをテストする
- ビルドとテストは必ず **COM3D2 → COM3D25 → dotnet test** の順で 1 本にする (COM3D2 ビルドが `bin/Debug/COM3D25/` を消すため)。`debug.bat` はゲームフォルダへ DLL をコピーするので使わない:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m \
&& "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m \
&& cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test
```

- コミットは `commit` スキルの書式 (Conventional Commits、日本語)。末尾に session の attribution を付ける
- `AnimationLayerInfo` の層番号は `MaidAnimationBlendController.MinLayer` (=2) 〜 `MaxLayer` (=8)
- 常駐ポーズクリップのタグは `MaidPoseFileManager.POSE_CLIP_TAG` (`"_scene_editor_pose"`、現在 private)

---

## ファイル構成

| ファイル | 責務 |
|---|---|
| `MaidManipulation/MaidAnimationBlendController.cs` (変更) | メイドごとの `BlendEditState`、停止中に層を残す判定、編集モード出入りの通知受け |
| `MaidManipulation/MaidEditPoseBaker.cs` (新規) | 手編集ポーズの anm 化とベース差し替え |
| `MaidManipulation/MaidPoseFileManager.cs` (変更) | 最後に適用した常駐バイナリの控えと公開 |
| `Manager/History/PoseSnapshot.cs` (変更) | 常駐バイナリの復元 |
| `MaidManipulation/MaidManipulateManager.cs` (変更) | 編集モード出入りの通知、ロック判定のメイド化、非表示時の解除の削除 |
| `MaidManipulation/MaidMotionState.cs` (変更) | 停止時の層残し判定の差し替え、ベース差し替え時の `hasBoneEdit` クリア |
| `MaidPoseWindow.cs` / `MaidPoseBlendRows.cs` (変更) | 適用先タブの状態を `BlendEditState` へ移す、`時間上書き` の無効化 |
| `IMaidDragPoint.cs` / `Maid*DragPoint.cs` / `MaidBoneSliderController.cs` / `InspectorWindow.cs` / `MaidWindowBase.cs` / `MaidIKWindow.cs` (変更) | ロック判定をメイド単位へ |
| `ScenePresetData.cs` / `Manager/ScenePresetManager.cs` (変更) | 層の保存・復元、ベースのみのポーズ保存 |
| `Timeline/TimelineLayer/AnimationTimelineLayer.cs` (変更) | 解除の履歴化 |
| `docs-site/guide/maid-editing.md` / `docs-site/guide/scene-preset.md` / `docs-site/timeline/layers-maid.md` / `docs/superpowers/specs/2026-09-20-anime-blend-window-redesign.md` (変更) | ドキュメント |
| `Tests/MaidBlendEditStateTests.cs` / `Tests/ScenePresetAnimationLayerTests.cs` (新規) | pure ロジックのテスト |

---

### Task 1: メイドごとの BlendEditState と「停止中に層を残す」判定

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs:27-36`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidBlendEditStateTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class BlendEditState { public int selectedLayer; public bool hasBoneEdit; }`
  - `public static BlendEditState GetEditState(Maid maid)` (無ければ作る。maid null は既定値の使い捨て)
  - `public static bool IsLayerSelected(Maid maid)`
  - `public static int GetSelectedLayer(Maid maid)`
  - `public static void MarkBoneEdit(Maid maid)` / `public static void ClearBoneEdit(Maid maid)` / `public static bool HasBoneEdit(Maid maid)`
  - `public static void ForgetEditState(Maid maid)`
  - `public static bool ShouldKeepLayersWhileStopped(bool isLayerSelected, bool isEditMode)` (pure)
  - `public static bool ShouldKeepLayersWhileStopped(Maid maid)` (`IsLayerSelected(maid)` と `MaidManipulateManager.instance.isEditMode` を渡す)
  - `public static bool ResolveSelectedLayer(int selectedLayer, IList<string> anmNamesByLayer)` は作らない (YAGNI)。空の段を選び続けない処理は `SyncFromAnimation` 内で行う
- 既存の `isBlendLayerSelected` はこの Task ではまだ残す (Task 2 で全参照を置き換えてから削除)

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidBlendEditStateTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 停止中にブレンド層を有効のまま残すかの判定を固定する。
    /// 「レイヤータブ選択中」または「編集モード外」なら残す
    /// </summary>
    public class MaidBlendEditStateTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        public void レイヤー選択中か編集モード外なら層を残す(
            bool isLayerSelected, bool isEditMode, bool expected)
        {
            Assert.Equal(expected,
                MaidAnimationBlendController.ShouldKeepLayersWhileStopped(isLayerSelected, isEditMode));
        }

        [Fact]
        public void 既定はベース選択でボーン未編集()
        {
            var state = new MaidAnimationBlendController.BlendEditState();
            Assert.Equal(MaidPoseBlendRows.BaseLayer, state.selectedLayer);
            Assert.False(state.hasBoneEdit);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Global Constraints のビルド + テストコマンドを実行。
Expected: COM3D25 ビルドは通り、`dotnet test` が `ShouldKeepLayersWhileStopped` / `BlendEditState` 未定義でコンパイルエラー。

- [ ] **Step 3: 実装する**

`MaidAnimationBlendController.cs` の `isBlendLayerSelected` プロパティの直後に追加:

```csharp
        /// <summary>
        /// モーションウィンドウの適用先とボーン編集の有無。メイドごとに持つ
        /// (静的フラグ 1 つだとメイド切替で前のメイドの層が取り残される)
        /// </summary>
        public sealed class BlendEditState
        {
            /// <summary>適用先タブ。BaseLayer はベース</summary>
            public int selectedLayer = MaidPoseBlendRows.BaseLayer;
            /// <summary>この編集セッションでドラッグ点・ボーンスライダー・反転がボーンを書いたか</summary>
            public bool hasBoneEdit;
        }

        private static readonly Dictionary<Maid, BlendEditState> _editStates
            = new Dictionary<Maid, BlendEditState>();

        /// <summary>メイドの状態。無ければ作る。maid が null なら既定値の使い捨てを返す</summary>
        public static BlendEditState GetEditState(Maid maid)
        {
            if (maid == null)
            {
                return new BlendEditState();
            }
            BlendEditState state;
            if (!_editStates.TryGetValue(maid, out state))
            {
                state = new BlendEditState();
                _editStates[maid] = state;
            }
            return state;
        }

        public static bool IsLayerSelected(Maid maid)
        {
            return GetSelectedLayer(maid) != MaidPoseBlendRows.BaseLayer;
        }

        public static int GetSelectedLayer(Maid maid)
        {
            BlendEditState state;
            return maid != null && _editStates.TryGetValue(maid, out state)
                ? state.selectedLayer
                : MaidPoseBlendRows.BaseLayer;
        }

        public static bool HasBoneEdit(Maid maid)
        {
            BlendEditState state;
            return maid != null && _editStates.TryGetValue(maid, out state) && state.hasBoneEdit;
        }

        /// <summary>ボーンを書く操作の開始時に呼ぶ。編集を抜けるときの anm 化の要否になる</summary>
        public static void MarkBoneEdit(Maid maid)
        {
            if (maid != null)
            {
                GetEditState(maid).hasBoneEdit = true;
            }
        }

        /// <summary>ベースを差し替えたとき・anm 化したときに呼ぶ</summary>
        public static void ClearBoneEdit(Maid maid)
        {
            BlendEditState state;
            if (maid != null && _editStates.TryGetValue(maid, out state))
            {
                state.hasBoneEdit = false;
            }
        }

        /// <summary>メイドの退避・削除時に呼ぶ</summary>
        public static void ForgetEditState(Maid maid)
        {
            if (maid != null)
            {
                _editStates.Remove(maid);
            }
        }

        /// <summary>
        /// 停止中に層 (とベース) を有効 / 速度 0 のまま残してブレンドを見せるか。
        /// レイヤータブ選択中は常に残す。ベースタブでは編集モード外だけ残す
        /// (編集モード中に残すと、ブレンド込みのポーズがボーン編集の基準になり寄与を分離できない)
        /// </summary>
        public static bool ShouldKeepLayersWhileStopped(bool isLayerSelected, bool isEditMode)
        {
            return isLayerSelected || !isEditMode;
        }

        public static bool ShouldKeepLayersWhileStopped(Maid maid)
        {
            return ShouldKeepLayersWhileStopped(
                IsLayerSelected(maid), MaidManipulateManager.instance.isEditMode);
        }
```

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: 2 構成ともビルド成功、`dotnet test` 全件 PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidBlendEditStateTests.cs
git commit -m "feat(maid): アニメブレンドの選択状態をメイドごとに持つ"
```

---

### Task 2: ロックと適用先タブをメイド単位へ切り替え、静的フラグを廃止する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs` (`isBlendLayerSelected` / `SetBlendLayerSelected` の削除、`SyncFromAnimation` の末尾)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:292-321, 333-341, 610-622`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/IMaidDragPoint.cs:55-70`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneRotateDragPoint.cs:110-118`, `MaidFaceDragPoint.cs:52-60`, `MaidFingerDragPoint.cs:125-133`, `MaidIKDragPoint.cs:117-125`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneSliderController.cs:222-231`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:368`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidWindowBase.cs:180-190`, `source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs:79`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs:31-35, 186-275, 698-760`

**Interfaces:**
- Consumes: Task 1 の `IsLayerSelected(maid)` / `GetSelectedLayer(maid)` / `GetEditState(maid)` / `ForgetEditState(maid)`
- Produces: `public static void SetSelectedLayer(Maid maid, int layer)` (Task 4 で anm 化のフックを足す)。`MaidWindowBase.DrawBlendLayerGate(Maid maid)`

- [ ] **Step 1: `SetSelectedLayer` を追加し、`SetBlendLayerSelected` / `isBlendLayerSelected` を削除する**

`MaidAnimationBlendController.cs` で `SetBlendLayerSelected` (666-693 行) を次に置き換える。`isBlendLayerSelected` プロパティも削除する:

```csharp
        /// <summary>
        /// 適用先タブの切替。切り替わった瞬間だけ、停止中のポーズを層ありなしで取り直す
        /// (停止中に層を残すかは ShouldKeepLayersWhileStopped が決める)
        /// </summary>
        public static void SetSelectedLayer(Maid maid, int layer)
        {
            var state = GetEditState(maid);
            if (state.selectedLayer == layer)
            {
                return;
            }
            var wasKeeping = ShouldKeepLayersWhileStopped(maid);
            state.selectedLayer = layer;
            var keeping = ShouldKeepLayersWhileStopped(maid);

            var anim = GetAnimation(maid);
            if (anim == null || MaidMotionState.IsPlaying(maid) || wasKeeping == keeping)
            {
                // 再生中は層の有効状態を再生側が持っているので触らない
                return;
            }

            if (keeping)
            {
                KeepLayersAfterStop(maid);
            }
            else
            {
                DisableLayers(maid, anim);
            }
            SampleStopped(maid, anim);
        }
```

`SampleStopped` (473-493 行) と `KeepLayersAfterStop` の doc コメント内の `isBlendLayerSelected` 参照を `ShouldKeepLayersWhileStopped` に書き換え、`SampleStopped` 本体の `baseState.enabled = isBlendLayerSelected; if (isBlendLayerSelected)` を `var keep = ShouldKeepLayersWhileStopped(maid); baseState.enabled = keep; if (keep)` にする。

`SyncFromAnimation` で `info.Reset()` を呼ぶ分岐 (found == null) の直後に、選択中の段が空になったらベースへ戻す処理を足す:

```csharp
                if (found == null)
                {
                    if (info.state != null || !string.IsNullOrEmpty(info.anmName))
                    {
                        info.Reset();
                        // 空になった段を選び続ける意味は無いのでベースへ戻す
                        if (GetSelectedLayer(maid) == layer)
                        {
                            GetEditState(maid).selectedLayer = MaidPoseBlendRows.BaseLayer;
                        }
                    }
                    continue;
                }
```

- [ ] **Step 2: `MaidMotionState` の参照を差し替える**

`MaidMotionState.cs:160` と `:205` の `MaidAnimationBlendController.isBlendLayerSelected` を `MaidAnimationBlendController.ShouldKeepLayersWhileStopped(maid)` にする。`RestoreBaseAfterSample(AnimationState state)` は maid を受けないので、シグネチャを `RestoreBaseAfterSample(Maid maid, AnimationState state)` に変え、`SampleWhileStopped(Animation anim, AnimationState state, float time)` も `SampleWhileStopped(Maid maid, Animation anim, AnimationState state, float time)` にして呼び出し元 (StopMotion 内 1 箇所、`SetPlaybackTime` 内があればそこも) を直す。

- [ ] **Step 3: `MaidManipulateManager` をメイド単位にする**

`isBlendLayerSelected` プロパティ (294 行) を次に置き換える:

```csharp
        /// <summary>
        /// 操作対象メイドがアニメブレンドのレイヤーを調整中か。この間はそのメイドのボーン / IK を触らせない
        /// (理由は MaidAnimationBlendController.ShouldKeepLayersWhileStopped を参照)
        /// </summary>
        public bool isBlendLayerSelected => MaidAnimationBlendController.IsLayerSelected(targetMaid);
```

`isBoneEditing` / `isDragPointActive` はそのまま (`isBlendLayerSelected` が targetMaid 基準になる)。白丸ドラッグ点は `Update()` の `dragPointController.SetTarget(isDragPointActive ? movableMaid : null)` の 1 箇所で操作対象メイドにだけ作られ、`movableMaid` は `targetMaid` 由来なので追加の変更は要らない。`IsDragPointAllowed` は作らない。

`Update()` の 333-341 行 (「見えていない間は必ず解除する」ブロック) を削除する。

617 行付近 (`calledMaids.Remove(maid)`) の直前に `MaidAnimationBlendController.ForgetEditState(maid);` を足す。377 行の `calledMaids.RemoveAll(m => !IsAlive(m))` は消滅したメイドのキーが辞書に残るが、`Maid` は使い回されるため次回 `GetEditState` で既定値が要る。`RemoveAll` を次に変える:

```csharp
            // 消滅したメイドを追跡リストから外す（シーン遷移や外部プラグインでの解除に追従）
            calledMaids.RemoveAll(m =>
            {
                if (IsAlive(m))
                {
                    return false;
                }
                MaidAnimationBlendController.ForgetEditState(m);
                return true;
            });
```

- [ ] **Step 4: ドラッグ点・スライダー・ウィンドウの参照を差し替える**

- `IMaidDragPoint.cs:61` `MaidDragBoneTracker.BeginDrag`: `if (MaidAnimationBlendController.isBlendLayerSelected)` → `if (MaidAnimationBlendController.IsLayerSelected(maid))`。`ReleaseForBoneEdit(maid)` の行はこの Task では残す (Task 3 で `MarkBoneEdit` に置き換える)
- `MaidBoneRotateDragPoint.cs:112` / `MaidFaceDragPoint.cs:54` / `MaidFingerDragPoint.cs:127` / `MaidIKDragPoint.cs:119`: 各クラスが持つメイド (フィールド名は各ファイルで `maid` / `_maid` を確認) を渡して `IsLayerSelected(maid)` にする
- `MaidBoneSliderController.cs:224`: `IsLayerSelected(maid)`
- `InspectorWindow.cs:368`: `IsLayerSelected(maid)` (同メソッド内で `maid` が使える)
- `MaidWindowBase.cs:180`: `protected bool DrawBlendLayerGate(Maid maid)` にし、`IsLayerSelected(maid)` で判定。`MaidIKWindow.cs:79` は IK ウィンドウが描いている対象メイド (`DrawMaidSelector` の戻り値を保持している変数) を渡す

- [ ] **Step 5: `MaidPoseWindow` の適用先を `BlendEditState` へ移す**

`_blendTargetLayer` / `_blendTargetMaid` フィールド (31-35 行) を削除し、参照箇所を全て `MaidAnimationBlendController.GetSelectedLayer(maid)` に置き換える (`grep -n _blendTargetLayer` で 12 箇所)。`DrawTargetSection` の 700-706 行 (メイド切替でベースへ戻すブロック) を削除する。

`IsScriptMotionApplied(maid)` の分岐は `MaidAnimationBlendController.SetSelectedLayer(maid, MaidPoseBlendRows.BaseLayer);` に、`DrawTargetTabs` の `layer => _blendTargetLayer = layer` は `layer => MaidAnimationBlendController.SetSelectedLayer(maid, layer)` に、フォールバック (`DrawTargetTabs` が false) も `SetSelectedLayer(maid, BaseLayer)` にする。728-729 行の `SetBlendLayerSelected(...)` 呼び出しは削除する。

- [ ] **Step 6: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: `isBlendLayerSelected` / `SetBlendLayerSelected` の参照が残っていればコンパイルエラーになるので、`grep -rn "isBlendLayerSelected\|SetBlendLayerSelected" source --include=*.cs` が `MaidManipulateManager.isBlendLayerSelected` のプロパティ定義と `isBoneEditing` / `isDragPointActive` の参照だけになるまで直す。テスト全件 PASS。

- [ ] **Step 7: コミット**

```bash
git add -A source
git commit -m "feat(maid): アニメブレンドのロックと適用先をメイド単位にする"
```

---

### Task 3: 編集モード出入りで層を切り替え、ボーン編集での層破棄をやめる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs` (`ReleaseForBoneEdit` の削除、`OnEditModeChanged` の追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:207-237`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/IMaidDragPoint.cs:66`, `MaidBoneSliderController.cs:231`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs:321-333` (反転)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidMotionState.cs` (`PlayMotion` / `PlayClip` / `Discard`)

**Interfaces:**
- Consumes: Task 1 の `MarkBoneEdit` / `ClearBoneEdit` / `ShouldKeepLayersWhileStopped`
- Produces: `public static void OnEditModeChanged(Maid maid, bool isEditMode)` (Task 4 で anm 化のフックを足す)

- [ ] **Step 1: `ReleaseForBoneEdit` を `MarkBoneEdit` に置き換える**

`MaidAnimationBlendController.ReleaseForBoneEdit` (438-448 行) を削除する。`IMaidDragPoint.cs:66` の `MaidAnimationBlendController.ReleaseForBoneEdit(maid);` と `MaidBoneSliderController.cs:231` の同呼び出しを `MaidAnimationBlendController.MarkBoneEdit(maid);` にする。コメントは「ボーンを書く操作なので、編集を抜けるときに anm 化する対象として控える」に直す。

`MaidPoseWindow.cs` の `反転` ボタン内、`MaidPoseFlipper.Flip(maid);` の直後に `MaidAnimationBlendController.MarkBoneEdit(maid);` を足す。

- [ ] **Step 2: 編集モードの出入りを通知する**

`MaidAnimationBlendController.cs` の `SetSelectedLayer` の直後に追加:

```csharp
        /// <summary>
        /// 編集モードの出入り。MaidManipulateManager.isEditMode の setter が呼出中の全メイドについて呼ぶ。
        /// ベースタブのメイドは編集モード中だけ層を無効化してベースのみでサンプルし、
        /// 抜けたら層を戻して再サンプルする。層の名前・重みは保持する (破棄しない)
        /// </summary>
        public static void OnEditModeChanged(Maid maid, bool isEditMode)
        {
            var anim = GetAnimation(maid);
            if (anim == null || MaidMotionState.IsPlaying(maid) || !HasAnyLayer(maid))
            {
                return;
            }
            var keep = ShouldKeepLayersWhileStopped(IsLayerSelected(maid), isEditMode);
            if (keep)
            {
                KeepLayersAfterStop(maid);
            }
            else
            {
                DisableLayers(maid, anim);
            }
            SampleStopped(maid, anim);
        }
```

`MaidManipulateManager.isEditMode` の setter を次に変える (`_isEditMode = value;` の後):

```csharp
                if (value)
                {
                    // 編集開始時点のポーズを基準にできるよう、呼出中の全メイドのモーションを止める
                    foreach (var maid in calledMaids)
                    {
                        MaidMotionState.StopMotion(maid);
                        MaidAnimationBlendController.OnEditModeChanged(maid, true);
                    }
                    ikHoldController.OnEditModeStarted();
                }
                else
                {
                    // モードを抜けた瞬間はメイドルートのギズモも消えるため、
                    // ボーン表示 OFF と同じく見えないギズモを掴んだままにしない
                    EndGizmoDrag(SceneViewManager.instance.gizmoRenderer);
                    EndGizmoDrag(GameViewManager.instance.gizmoRenderer);
                    foreach (var maid in calledMaids)
                    {
                        MaidAnimationBlendController.OnEditModeChanged(maid, false);
                    }
                }
```

`StopMotion` は `_isEditMode` を読む `ShouldKeepLayersWhileStopped(maid)` を通るので、`_isEditMode = value` を先に代入している現在の順序を保つこと。

- [ ] **Step 3: ベース差し替えで `hasBoneEdit` を落とす**

`MaidMotionState.PlayMotion` の `anim.Play(clipName);` の直後、`PlayClip` の `anim.Play(clipName);` の直後、`Discard` の `MaidBoneSliderController.ClearBasePose(maid);` の直後に `MaidAnimationBlendController.ClearBoneEdit(maid);` を足す。`Discard` は `PhotoMotionUtils.Apply` と `ApplyPoseBinary(startPlaying: true)` が通るので、一覧クリックとマイポーズ読込はこれで落ちる。

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: `ReleaseForBoneEdit` の参照が残っていないこと (`grep -rn ReleaseForBoneEdit source` が空)、テスト全件 PASS。

- [ ] **Step 5: 実機で確認する (ゲーム起動中なら `com3d25-devbridge` の `screenshot` で見る)**

- ベースタブ・停止中・編集モード外 (メニューの編集モード OFF) で層を載せる → ブレンドが見える
- ドラッグ点を掴む (編集モードに入る) → ベースだけのポーズになり、層の名前は `適用先` タブの `*` に残る
- 編集モード OFF → 層が戻る (この時点では手編集は消える。Task 4 で anm 化する)

- [ ] **Step 6: コミット**

```bash
git add -A source
git commit -m "feat(maid): 編集モードの出入りでブレンド層を切り替え、ボーン編集での破棄をやめる"
```

---

### Task 4: 手編集ポーズの anm 化 (MaidEditPoseBaker) と常駐バイナリの履歴復元

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidEditPoseBaker.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidPoseFileManager.cs:223-266, 338-404`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs:33-40, 70-77, 146-156`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs` (`SetSelectedLayer` / `OnEditModeChanged` にフック)

**Interfaces:**
- Consumes: Task 1 の `HasBoneEdit` / `ClearBoneEdit`、Task 3 の `OnEditModeChanged`
- Produces:
  - `MaidEditPoseBaker.BakeIfEdited(Maid maid)`: `hasBoneEdit` かつタイムラインのモーションレイヤーが無いときだけ anm 化。戻り値 bool
  - `MaidEditPoseBaker.EditPoseDisplayName` (= `"編集ポーズ"`)
  - `MaidPoseFileManager.PoseClipTag` (public、`POSE_CLIP_TAG` の公開)
  - `MaidPoseFileManager.GetResidentBinary(Maid maid)`: 最後に常駐枠へ載せたバイナリ。無ければ null
  - `MaidPoseFileManager.ReplaceResidentClip(Maid maid, byte[] binary)`: 常駐クリップの差し替えだけを行う軽量版。`ApplyPoseBinary` と違い停止・IK 解除・バストキー反映・ダイアログ表示・サンプルをしない。戻り値は登録した `AnimationState` (失敗時 null、警告ログのみ)

`ApplyPoseBinary` は UI 操作向けで、`DetachAllIK` (ゲーム側 IK の全解除)・`ApplyBustKeyFlags` (揺れものフラグの上書き)・失敗時ダイアログ・`StopMotion`・`SetAppliedMotion(null)` を含む。anm 化と履歴復元は `▶` / Space / Ctrl+Z の裏で走るので、これらの副作用を持ち込まない `ReplaceResidentClip` を使う。

- [ ] **Step 1: 常駐バイナリを控え、軽量な差し替え API を切り出す**

`MaidPoseFileManager.cs`:

```csharp
        /// <summary>ポーズ/アニメ用の常駐クリップの内部タグ。ゲーム側のクリップ名と衝突させない</summary>
        private const string POSE_CLIP_TAG = "_scene_editor_pose";
        public static string PoseClipTag => POSE_CLIP_TAG;

        /// <summary>
        /// メイドごとに最後に常駐枠へ適用したバイナリ。
        /// 常駐枠は 1 メイド 1 クリップで差し替えると中身が失われるため、
        /// 履歴 (PoseSnapshot) が差し替え前の中身へ戻すのに使う
        /// </summary>
        private static readonly Dictionary<Maid, byte[]> _residentBinaries = new Dictionary<Maid, byte[]>();

        public static byte[] GetResidentBinary(Maid maid)
        {
            byte[] binary;
            return maid != null && _residentBinaries.TryGetValue(maid, out binary) ? binary : null;
        }
```

`ApplyPoseBinary` で `RegisterResidentClip` が成功した直後 (`if (state == null) {...}` の後) に `_residentBinaries[maid] = binary;` を足す。`ReleaseClip(Maid maid)` と `ClearClips()` で `_residentBinaries.Remove(maid)` / `_residentBinaries.Clear()` する。

`ApplyPoseBinary` の直前に軽量版を追加する:

```csharp
        /// <summary>
        /// 常駐クリップの差し替えだけを行う。編集ポーズの anm 化と履歴の復元が使う。
        /// ApplyPoseBinary と違い、停止・ゲーム側 IK の解除・バストキー反映・ダイアログ・サンプルはしない
        /// (▶ / Space / Ctrl+Z の裏で走るため、ユーザー操作向けの副作用を持ち込まない)。
        /// 失敗時は警告ログだけ出して null を返す
        /// </summary>
        public static AnimationState ReplaceResidentClip(Maid maid, byte[] binary)
        {
            var anim = maid != null ? maid.GetAnimation() : null;
            if (anim == null || binary == null)
            {
                MTEUtils.LogWarning("常駐クリップを差し替えられません (Animation かバイナリが無い)");
                return null;
            }
            var nativeClip = ImportCM.LoadAniClipNative(binary,
                load_l_mune_anime: true, load_r_mune_anime: true);
            if (nativeClip == null)
            {
                MTEUtils.LogWarning("常駐クリップの読み込みに失敗しました");
                return null;
            }
            var state = RegisterResidentClip(maid, anim, nativeClip);
            if (state == null)
            {
                MTEUtils.LogWarning("常駐クリップの登録に失敗しました");
                return null;
            }
            _residentBinaries[maid] = binary;
            return state;
        }
```

- [ ] **Step 2: `MaidEditPoseBaker` を作る**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidEditPoseBaker.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 手編集したポーズの anm 化。編集モードを抜けるとき (▶ / Space / メニューの OFF) と、
    /// 編集モード中にレイヤータブへ切り替えるときに、現在ポーズを anm にしてベースへ差し替える。
    /// 層を有効へ戻すと Unity が毎フレームサンプルして手編集を上書きするため、
    /// 先にベース自体を編集込みの anm にしておく (タイムラインがキーから anm を作るのと同じ考え方)。
    /// 経路はシーンプリセット復元と同じ CapturePoseBinary → ApplyPoseBinary(startPlaying: false)
    /// </summary>
    public static class MaidEditPoseBaker
    {
        /// <summary>anm 化後の「再生中」の表示名。一覧のどのエントリでもないのでハイライトしない</summary>
        public const string EditPoseDisplayName = "編集ポーズ";

        /// <summary>
        /// ボーンを触っていれば anm 化する。タイムラインに対象メイドのモーションレイヤーがあるときは
        /// タイムライン側が anm を作るので行わない。anm 化したら true
        /// </summary>
        public static bool BakeIfEdited(Maid maid)
        {
            if (maid == null || !MaidAnimationBlendController.HasBoneEdit(maid))
            {
                return false;
            }
            if (HasMotionTimelineLayer(maid))
            {
                MaidAnimationBlendController.ClearBoneEdit(maid);
                return false;
            }
            if (MaidMotionState.IsPlaying(maid))
            {
                // 再生中は手編集が既に流れて消えているので固めるものが無い
                MaidAnimationBlendController.ClearBoneEdit(maid);
                return false;
            }

            // 直前の編集に確定待ちがあればそこへマージされる (ラベルは元の操作名のまま)
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "編集ポーズ", () => PoseSnapshot.GetAllBodyBones(maid));

            var binary = MaidPoseFileManager.CapturePoseBinary(maid);
            if (binary == null)
            {
                MTEUtils.LogWarning("編集ポーズの anm 化に失敗しました (ポーズを取得できません)");
                return false;
            }

            // 常駐クリップだけ差し替える (停止・IK 解除・ダイアログを伴う ApplyPoseBinary は使わない)。
            // ポーズは既に Transform に乗っているのでサンプルは要らず、
            // 呼び出し元 (OnEditModeChanged / SetSelectedLayer) が層を戻したあとに再サンプルする
            var state = MaidPoseFileManager.ReplaceResidentClip(maid, binary);
            if (state == null)
            {
                return false;
            }
            state.enabled = false;
            state.weight = 1f;
            state.time = 0f;

            // ▶ の再開先とリセットの戻り先を固めたポーズ自身にする (プリセット復元と同じ扱い)
            MaidMotionState.SetResumeClip(maid, MaidPoseFileManager.PoseClipTag);
            MaidMotionState.SetAppliedMotion(maid, new MaidMotionState.AppliedMotionInfo
            {
                displayName = EditPoseDisplayName,
            });
            MaidPoseFileManager.MarkPoseAsResetTarget(maid);
            MaidBoneSliderController.CaptureBasePose(maid);
            MaidAnimationBlendController.ClearBoneEdit(maid);
            MTEUtils.LogDebug("編集ポーズを anm 化しました: " + maid.name);
            return true;
        }

        private static bool HasMotionTimelineLayer(Maid maid)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            if (timelineManager == null || timelineManager.timeline == null)
            {
                return false;
            }
            var cache = MTEP.MaidManager.instance.GetMaidCache(maid);
            return cache != null
                && timelineManager.GetLayer(typeof(MTEP.MotionTimelineLayer), cache.slotNo) != null;
        }
    }
}
```

`ReplaceResidentClip` は `RegisterResidentClip` で旧常駐クリップを破棄して差し替えるだけで、元のモーションクリップ (ゲーム側のクリップ) は `Animation` に残る。`BakeIfEdited` は `OnEditModeChanged(false)` の先頭で呼ばれ、その後 `KeepLayersAfterStop` → `SampleStopped` が走るので、固めた常駐クリップがベースとして層と一緒にサンプルされる (`SampleStopped` は `GetCurrentAnimationState` = `_resumeClipNames` のクリップを使う)。

- [ ] **Step 3: anm 化のフックを入れる**

`MaidAnimationBlendController.OnEditModeChanged` の先頭 (anim null チェックの前) に:

```csharp
            if (!isEditMode)
            {
                // 層を戻す前に手編集を固める (戻すと再サンプルで消えるため)
                MaidEditPoseBaker.BakeIfEdited(maid);
            }
```

`SetSelectedLayer` の `state.selectedLayer = layer;` の直前に:

```csharp
            if (layer != MaidPoseBlendRows.BaseLayer && MaidManipulateManager.instance.isEditMode)
            {
                // 編集モード中にレイヤーへ切り替えると層が有効に戻る。先に手編集を固める
                MaidEditPoseBaker.BakeIfEdited(maid);
            }
```

`▶` で抜ける経路は `AutoEditMode.Exit()` → `isEditMode = false` → `OnEditModeChanged(false)` で anm 化 → `PlayMotion` が `GetPlayableClipName` で `POSE_CLIP_TAG` (SetResumeClip 済み) を再生する。`MaidPoseWindow.cs:250` と `COM3D2.SceneEditor.Plugin.cs:285` の順序 (`Exit` が先) を変えないこと。

- [ ] **Step 4: `PoseSnapshot` で常駐バイナリを戻す**

フィールドを追加:

```csharp
        /// <summary>
        /// 記録時のベースが常駐ポーズクリップだったときの中身。
        /// 常駐枠は 1 つで anm 化やポーズ読込のたびに差し替わるため、名前だけでは戻せない
        /// </summary>
        private byte[] _residentBinary;
```

`Capture` の `snapshot._clipName = ...` の直後:

```csharp
            snapshot._residentBinary = snapshot._clipName == MaidPoseFileManager.PoseClipTag
                ? MaidPoseFileManager.GetResidentBinary(maid) : null;
```

`RestoreMotion` の先頭:

```csharp
            // 常駐枠の中身が差し替わっていれば先に戻す (参照比較で足りる。同じ配列なら差し替わっていない)。
            // ボーンは _bones.Apply() で戻し済みなのでクリップの差し替えだけでよい
            // (ApplyPoseBinary は IK 解除やダイアログを伴うため使わない)
            if (_residentBinary != null
                && !ReferenceEquals(_residentBinary, MaidPoseFileManager.GetResidentBinary(maid)))
            {
                var state = MaidPoseFileManager.ReplaceResidentClip(maid, _residentBinary);
                if (state != null)
                {
                    state.enabled = false;
                    state.weight = 1f;
                    state.time = 0f;
                }
            }
```

続く `SetAppliedMotion(maid, _appliedMotion)` と `PlayClip` / `SetResumeClip` は従来どおり。`_clipName` が `POSE_CLIP_TAG` のときはこの差し替えで `PlayClip` の `anim.GetClip(clipName)` が新しい常駐クリップを掴む。

- [ ] **Step 5: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: 2 構成ビルド成功、テスト全件 PASS。

- [ ] **Step 6: 実機で確認する**

- 層を載せる → ベースタブでドラッグ点を動かす → `▶`: `再生中` が `編集ポーズ` になり、静止ポーズの上で層だけ動く
- Ctrl+Z: 元のモーション名と手編集前のポーズへ戻る
- マイポーズをベースに読込 → 手編集 → `▶` → Ctrl+Z: `再生中` がマイポーズ名へ戻り、`リセット` でマイポーズのポーズになる
- 編集モード中にレイヤータブへ切替: 手編集が残ったままブレンドが見える
- メイド A と B を両方手編集 → A で `▶`: 編集モードは全体で抜けるので B も `編集ポーズ` になる (B は停止したまま)。B の手編集が消えていないこと、B の `▶` で静止ポーズが再生できることを確認する
- ゲーム側 IK (アタッチ) を使っている状態で `▶` / Ctrl+Z しても IK が外れないこと (`ApplyPoseBinary` の `DetachAllIK` を通らない確認)

- [ ] **Step 7: コミット**

```bash
git add -A source
git commit -m "feat(maid): 編集モードを抜けるときに手編集ポーズを anm 化してベースへ差し替える"
```

---

### Task 5: シーンプリセットにブレンド層を保存し、ポーズはベースのみで固める

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:620-640, 672-675, 1017`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1062-1072, 2060-2082`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs` (`SampleBaseOnly` / `ApplyLayerStates` の追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetAnimationLayerTests.cs`

**Interfaces:**
- Produces:
  - `ScenePresetAnimationLayer` (XML: `<animationLayer layer="2" anmName=".." startTime=".." weight=".." speed=".." loop="true"/>`)
  - `ScenePresetAnimationLayer.FromLayerState(MaidAnimationBlendController.LayerState)` / `ToLayerState()` (pure)
  - `MaidAnimationBlendController.CaptureBaseOnlyPoseBinary(Maid maid)`: 層を一時無効化してベースのみサンプルし `CapturePoseBinary`、層を戻して再サンプル
  - `MaidAnimationBlendController.ApplyLayerStates(Maid maid, List<LayerState> states)`: `Restore` の公開薄ラッパ (履歴と同じ復元経路を使う)

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetAnimationLayerTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの層と履歴用 LayerState の相互変換を固定する</summary>
    public class ScenePresetAnimationLayerTests
    {
        [Fact]
        public void LayerStateから作り戻すと同じ値になる()
        {
            var source = new MaidAnimationBlendController.LayerState
            {
                layer = 3, anmName = "C:/mod/a.anm", time = 1.5f, weight = 0.4f, speed = 0.8f, loop = false,
                playing = true, overrideTime = true,
            };

            var preset = ScenePresetAnimationLayer.FromLayerState(source);
            var restored = preset.ToLayerState();

            Assert.Equal(3, restored.layer);
            Assert.Equal("C:/mod/a.anm", restored.anmName);
            Assert.Equal(1.5f, restored.time);
            Assert.Equal(0.4f, restored.weight);
            Assert.Equal(0.8f, restored.speed);
            Assert.False(restored.loop);
            // 再生中かはベースの再生状態から決まるので保存しない。時間上書きはタイムライン専用
            Assert.False(restored.playing);
            Assert.False(restored.overrideTime);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Global Constraints のコマンドを実行。Expected: `ScenePresetAnimationLayer` 未定義でコンパイルエラー。

- [ ] **Step 3: データクラスを追加する**

`ScenePresetData.cs` の `ScenePresetMotion` クラスの直後に追加:

```csharp
    /// <summary>
    /// アニメブレンド層 1 段 (v35)。停止中・再生中を問わず、載っている層だけ記録する。
    /// anmName はタイムライン XML と同じ AnimationLayerInfo.anmName (Mod は絶対パス)
    /// </summary>
    public class ScenePresetAnimationLayer
    {
        [XmlAttribute] public int layer;
        [XmlAttribute] public string anmName;
        [XmlAttribute] public float startTime;
        [XmlAttribute] public float weight = 1f;
        [XmlAttribute] public float speed = 1f;
        [XmlAttribute] public bool loop = true;

        public static ScenePresetAnimationLayer FromLayerState(MaidAnimationBlendController.LayerState state)
        {
            return new ScenePresetAnimationLayer
            {
                layer = state.layer,
                anmName = state.anmName,
                startTime = state.time,
                weight = state.weight,
                speed = state.speed,
                loop = state.loop,
            };
        }

        /// <summary>playing はベースの再生状態から決まり、overrideTime はタイムライン専用なので既定値</summary>
        public MaidAnimationBlendController.LayerState ToLayerState()
        {
            return new MaidAnimationBlendController.LayerState
            {
                layer = layer,
                anmName = anmName,
                time = startTime,
                weight = weight,
                speed = speed,
                loop = loop,
            };
        }
    }
```

`ScenePresetMaid` の `poseAnmBinary` の直後に:

```csharp
        /// <summary>アニメブレンド層 (v35)。旧プリセットは null になり、適用時に層へ触らない</summary>
        [XmlElement("animationLayer")]
        public List<ScenePresetAnimationLayer> animationLayers;
```

`CurrentVersion = 34` を `35` にする。

- [ ] **Step 4: 保存側**

`MaidAnimationBlendController.cs` に追加:

```csharp
        /// <summary>
        /// 層を一時的に外してベースだけのポーズを anm 化する。
        /// 停止中に層を残している間はボーンにブレンドが乗っているため、
        /// シーンプリセットのポーズにブレンドを焼き込まないためのもの。取り終えたら元の状態へ戻す
        /// </summary>
        public static byte[] CaptureBaseOnlyPoseBinary(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null || !HasAnyLayer(maid) || MaidMotionState.IsPlaying(maid))
            {
                return MaidPoseFileManager.CapturePoseBinary(maid);
            }
            var keep = ShouldKeepLayersWhileStopped(maid);
            DisableLayers(maid, anim);
            var baseState = MaidMotionState.GetCurrentAnimationState(maid);
            if (baseState != null)
            {
                baseState.enabled = true;
                baseState.weight = 1f;
                anim.Sample();
                baseState.enabled = keep;
            }
            var binary = MaidPoseFileManager.CapturePoseBinary(maid);
            if (keep)
            {
                KeepLayersAfterStop(maid);
                SampleStopped(maid, anim);
            }
            return binary;
        }

        /// <summary>シーンプリセットの復元。履歴の Restore と同じ経路で層を載せ直す</summary>
        public static void ApplyLayerStates(Maid maid, List<LayerState> states)
        {
            Restore(maid, states);
        }
```

`ScenePresetManager.CaptureMaid` (1066-1070 行) を次に変える:

```csharp
                state.motion = CaptureMotion(maid);
                if (state.motion == null)
                {
                    // 停止中に層を残していてもブレンドは焼き込まない (層は animationLayers で別に持つ)
                    state.poseAnmBinary = MaidAnimationBlendController.CaptureBaseOnlyPoseBinary(maid);
                }
                var layerStates = MaidAnimationBlendController.Capture(maid);
                if (layerStates.Count > 0)
                {
                    state.animationLayers = new List<ScenePresetAnimationLayer>();
                    foreach (var layerState in layerStates)
                    {
                        state.animationLayers.Add(ScenePresetAnimationLayer.FromLayerState(layerState));
                    }
                }
```

- [ ] **Step 5: 復元側**

`ScenePresetManager.ApplyMaid` (2060-2082 行) の `if (state.visible) { ... }` ブロック末尾、`MarkPoseAsResetTarget` の後 (ブロックの閉じ括弧の直前) に:

```csharp
                    if (state.animationLayers != null)
                    {
                        // ベースの適用後に載せる (層の enabled/speed はベースの再生中かで決まる)。
                        // 停止中なら層は速度 0 で残り、編集モード外なのでブレンドが見える
                        var layerStates = new List<MaidAnimationBlendController.LayerState>();
                        foreach (var layer in state.animationLayers)
                        {
                            var layerState = layer.ToLayerState();
                            // プリセットは層ごとの停止を持たない。ベースが再生中なら層も流す
                            // (Restore は basePlaying && playing で速度を決める。履歴と共有なので Restore 側は変えない)
                            layerState.playing = true;
                            layerStates.Add(layerState);
                        }
                        MaidAnimationBlendController.ApplyLayerStates(maid, layerStates);
                    }
```

`Restore` の内部で `cache.LoadAnimationLayer(info)` が null を返す段は `info.Reset()` されて飛ぶ (既存挙動)。読み込めなかった段の警告は `LoadAnimationLayer` 側のログに任せる。`Restore` 本体は変更しない (履歴の undo/redo と共有しており、`target.playing` を無視すると層単独の停止状態を戻せなくなる)。

- [ ] **Step 6: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: テスト全件 PASS。

- [ ] **Step 7: 実機で確認する**

- 層を載せて停止 (編集モード外) → プリセット保存 → 別ポーズにしてから復元: ブレンド込みの見た目が戻る
- 保存したプリセットのポーズ anm (サイドカー) を層無しで読むとベースだけのポーズになっている (復元後に `適用先` の層を `削除` して確認)
- 再生中に保存 → 復元で層も流れる
- 脱衣状態や衣装の違うプリセットを復元しても (`ApplyUndress` が層の適用より後に走る) ブレンド層が残っている

- [ ] **Step 8: コミット**

```bash
git add -A source
git commit -m "feat(preset): シーンプリセットにアニメブレンド層を保存し、ポーズはベースのみで固める"
```

---

### Task 6: タイムラインが無いときは `時間上書き` を操作不可にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseBlendRows.cs:109-209`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs:740-745`

**Interfaces:**
- `MaidPoseBlendRows.DrawLayerValues(GUIView view, Maid maid, AnimationLayerInfo info, float rowHeight, float labelWidth, bool overrideTimeEnabled)`

- [ ] **Step 1: 引数を足す**

`DrawLayerValues` に `bool overrideTimeEnabled` を追加し、`時間上書き` のトグルを `enabled` 付きオーバーロード (`DrawToggle(string label, bool value, float width, float height, bool enabled, Action<bool> onChanged)`) にする:

```csharp
                // タイムライン再生時だけ効く値なので、タイムラインが無ければ触らせない
                view.DrawToggle("時間上書き", info.overrideTime, halfWidth, rowHeight,
                    overrideTimeEnabled, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド時間上書き", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetOverrideTime(maid, layer, value);
                });
```

- [ ] **Step 2: 呼び出し側でゲート状態を渡す**

`MaidPoseWindow.cs:741-743`:

```csharp
            var gateState = TimelineLayerGate.Begin(view, typeof(MTEP.AnimationTimelineLayer), maid, ROW_HEIGHT);

            var layerInfo = GetTargetLayerInfo(maid);
            DrawTargetRow(view, maid, layerInfo, true);
            MaidPoseBlendRows.DrawLayerValues(view, maid, layerInfo, ROW_HEIGHT, LABEL_WIDTH,
                gateState == TimelineLayerGateState.Ready);
```

`TimelineLayerGateState` の「登録済み」の値名は `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateText.cs` の enum で確認する (`Ready` 相当)。

- [ ] **Step 3: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: PASS。

- [ ] **Step 4: コミット**

```bash
git add -A source
git commit -m "feat(pose): タイムラインが無いときは時間上書きを操作不可にする"
```

---

### Task 7: タイムライン側の層解除を履歴に残す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/AnimationTimelineLayer.cs:204-230`

- [ ] **Step 1: 外す前に `BeforeEdit` を呼ぶ**

`OnPoseEditEnd` の `if (timelineManager.isMotionEditing)` ブロックを次に変える:

```csharp
            // モーション編集中はアニメーションレイヤー無効化
            if (timelineManager.isMotionEditing)
            {
                // 外す層があるときだけ履歴を積む (フレーム移動やレイヤー切替でも来るため、
                // 既に外れていれば何もしない)。確定待ちの編集があればそこへマージされる
                var hasEnabledLayer = false;
                foreach (var info in maidCache.animationLayerInfos)
                {
                    if (info.state != null && info.state.enabled && info.layer > 0)
                    {
                        hasEnabledLayer = true;
                        break;
                    }
                }
                if (hasEnabledLayer)
                {
                    var maid = this.maid;
                    SE.HistoryManager.instance.BeforeEdit(maid, SE.HistoryScope.Pose,
                        "ブレンド解除", () => SE.PoseSnapshot.GetAllBodyBones(maid));
                }

                var stateUpdated = false;
                foreach (var info in maidCache.animationLayerInfos)
                {
                    if (info.state != null && info.state.enabled && info.layer > 0)
                    {
                        info.state.enabled = false;
                        info.state = null;
                        stateUpdated = true;
                    }
                }
                ...
```

`SE` エイリアスはこのファイルの using を確認する (`TimelineManager.cs:433` が `SE.HistoryManager.instance` を使っているので同じ alias `using SE = COM3D2.SceneEditor.Plugin;` を足す)。`this.maid` は `TimelineLayerBase.maid`。

- [ ] **Step 2: ビルドしてテストが通ることを確認する**

Global Constraints のコマンドを実行。Expected: PASS。

- [ ] **Step 3: 実機で確認する**

タイムライン読込 → 層を載せる → モーションレイヤーをアクティブにしてドラッグ点を動かす → 層が外れる → Ctrl+Z で層とポーズが戻る。履歴ウィンドウのラベルは「ボーン回転: …」のまま。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/AnimationTimelineLayer.cs
git commit -m "feat(timeline): モーション編集でのブレンド層解除を履歴に残す"
```

---

### Task 8: ドキュメント

**Files:**
- Modify: `docs-site/guide/maid-editing.md:231-307`
- Modify: `docs-site/guide/scene-preset.md` (保存項目の一覧)
- Modify: `docs-site/timeline/layers-maid.md:27-40`
- Modify: `docs/superpowers/specs/2026-09-20-anime-blend-window-redesign.md:1-5`

- [ ] **Step 1: `maid-editing.md` のアニメブレンド節を書き直す**

以下の段落を差し替える (見出しと表はそのまま):

- 「`適用先` がレイヤーの間は、停止してもそのレイヤーは有効なまま残ります。」〜「ボーンを触りたいときは `適用先` を `ベース` へ戻してください。」の 2 段落を:

```markdown
停止中は、`適用先` が `ベース` でも編集モードの外ならレイヤーは有効なまま残り、
止めた位置でブレンドの結果が見えます。編集モードに入るとレイヤーは一時的に外れ、
ベースだけのポーズになります (レイヤーの名前や重みは残ります)。
ブレンド込みのポーズを基準にボーンを編集すると、寄与を分離できなくなるためです。

`適用先` がレイヤーの間は編集モードでもレイヤーが残る代わりに、ボーンと IK の編集ができません。
白丸のドラッグ点・ボーンギズモ・骨格線は表示されず、IK 固定ウィンドウと
Inspector のボーンスライダーも無効になります。ボーンを触りたいときは `適用先` を `ベース` へ戻してください。
`適用先` の選択はメイドごとに保持され、別のメイドに切り替えても元のメイドの状態は変わりません。
```

- 「`適用先` を `ベース` に戻すと、停止中はベースのポーズだけになります。」の段落を削除
- 「アニメブレンドのチェックを外すと `適用先` も `ベース` へ戻ります。」はそのまま
- 「ボーン編集との関係:」の箇条書きを:

```markdown
ボーン編集との関係:

- ベースタブでボーンを手編集したあと編集モードを抜けると (`▶` / `Space` / メニューの編集モード OFF)、
  そのポーズが anm 化されてベースになります。`再生中` は `編集ポーズ` になり、
  `▶` はその静止ポーズの上でレイヤーだけを動かします。元のモーションへ戻すには一覧から選び直してください
- anm 化は直前の編集と一緒に履歴に積まれるので、Ctrl+Z で元のモーションと手編集前のポーズへ戻せます
- 編集モード中に `適用先` をレイヤーへ切り替えたときも同じように anm 化されます
- タイムラインでモーションレイヤー・移動レイヤーを編集しているときはレイヤーが解除されます。
  こちらも履歴に残るので Ctrl+Z で戻せますが、そのまま編集を続けると次のフレーム移動で再び解除されます
```

- `時間上書き` の行の末尾に「(タイムラインを読み込んでいないときは操作できません)」を足す
- 「タイムラインとの連携:」の末尾に「- シーンプリセットにはレイヤーの内容も保存され、復元時に同じブレンドが再現されます。プリセットのポーズ自体にはブレンドは焼き込まれません」を足す

- [ ] **Step 2: `scene-preset.md` にメイドの保存項目としてブレンド層を追記する**

保存項目の一覧 (メイドの項) に「アニメブレンドのレイヤー (アニメ名・開始時間・重み・速度・ループ)。ポーズはベースのみで保存されます」を足す。ファイル内の一覧の書式に合わせる。

- [ ] **Step 3: `layers-maid.md` のメイドアニメブレンド節に追記する**

「編集モード中は再生に反映されません。」の後に:

```markdown
モーションレイヤー・移動レイヤーの編集で外れたレイヤーは履歴に積まれ、Ctrl+Z で戻せます。
```

- [ ] **Step 4: 古い再設計メモに置き換え注記を足す**

`2026-09-20-anime-blend-window-redesign.md` の先頭見出しの直後に:

```markdown
> **2026-09-21: このメモの「別ウィンドウ化」「ピッカーモード」「停止中はブレンドを見せない」は
> 65f3019 / 67481b7 で撤回され、`2026-09-21-anime-blend-per-maid-bake-design.md` に置き換えられた。**
```

- [ ] **Step 5: コミット**

```bash
git add docs-site docs/superpowers/specs/2026-09-20-anime-blend-window-redesign.md
git commit -m "docs: アニメブレンドのメイド単位化と編集ポーズの anm 化を反映する"
```

---

## 自己レビュー (計画作成時に実施)

- **仕様カバレッジ**: 1 節 → Task 1, 2 / 2 節 → Task 3 / 3 節 → Task 4 / 4 節 → Task 5 / 5 節 → Task 6 / 6 節 → Task 7 / ドキュメント → Task 8。検証項目の IK 固定との衝突は Task 3 Step 5 と Task 4 Step 6 の実機確認で見る (ブレるなら `ikHoldController` の書き込みを `ShouldKeepLayersWhileStopped` 中は止める追加 Task を起こす)
- **型の整合**: `BlendEditState` / `GetEditState` / `IsLayerSelected` / `GetSelectedLayer` / `MarkBoneEdit` / `ClearBoneEdit` / `HasBoneEdit` / `ForgetEditState` / `ShouldKeepLayersWhileStopped` / `SetSelectedLayer` / `OnEditModeChanged` / `CaptureBaseOnlyPoseBinary` / `ApplyLayerStates` は全て Task 1〜5 で定義。`MaidPoseFileManager.PoseClipTag` / `GetResidentBinary` は Task 4。`ScenePresetAnimationLayer.FromLayerState` / `ToLayerState` は Task 5

## レビュー却下メモ

plan-review (2026-09-21) の指摘は 6 件すべて取り込んだ。却下した指摘は無い。

- `Restore` の速度計算変更 → 取り消し。プリセット復元側で `playing = true` を立てる
- `PoseSnapshot` / anm 化が `ApplyPoseBinary` を使う → 副作用の無い `ReplaceResidentClip` を新設して置き換え
- 存在しないドラッグ点ループの修正指示 → 削除
- 他メイドの手編集も `▶` で一括 anm 化される / ゲーム側 IK / 衣装差し替え → 実機検証項目へ追加
