# 操作確定時の自動キーフレーム登録 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインの「自動登録」を、メイドのドラッグ編集だけでなく各ウィンドウ（ライト / カメラ / 背景 / 表情 / 脱衣 / IK / 重力 / モデル配置 等）での値変更確定時にも発火させる。

**Architecture:** SceneEditor の操作履歴 `HistoryManager` は、各ウィンドウが値変更前に `BeforeEdit` を呼び、マウス解放時に 1 操作として確定（`CommitPending`）する。この「確定」を `onEditCommitted` イベントとして公開し、`TimelineWindow` が購読して `TimelineManager.AddKeyFrameDiff` を呼ぶ。発火条件は純粋ロジック `AutoKeyFrameGate` に切り出してテストする。ドラッグ完了（`MaidDragBoneTracker.onDragCompleted`）と履歴確定の二重発火は、`AddKeyFrameDiffBones` で「同じ値で登録済みのボーン」を除外することで無害化する（2 回目は差分ゼロで何もしない）。

**Tech Stack:** C# (Unity 5.6 / Mono、.NET 3.5 相当の構文制約: `out var` 不可、パターンマッチ不可)、xunit (net48) テスト。

**Spec:** 本ファイル冒頭の Goal / Architecture と「前提となる調査結果」節が仕様。別途スペック文書は無い。

## Global Constraints

- コメント・ログ文言は日本語。
- ビルドは COM3D2 / COM3D25 の両 GameVersion を必ず通す（対象フレームワークが異なる）。ゲームフォルダへコピーしない MSBuild 直接実行を使う（下記コマンド）。
- `git worktree` は使わない。
- プラグイン間連携は不要（すべて同一アセンブリ内）。
- 既存の `isAutoKeyFrame` 設定（`Timeline/Config.cs:64`）と「自動登録」トグル（`TimelineControlWindow.cs:633`）を流用し、新しい設定項目は追加しない。

### ビルド・テストコマンド

Git Bash から（`.env` に `COM3D2_DIR` / `COM3D25_DIR` がある前提）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export COM3D2_DIR="$(grep '^COM3D2_DIR=' ../../.env | cut -d= -f2- | tr -d '\r')"
export COM3D25_DIR="$(grep '^COM3D25_DIR=' ../../.env | cut -d= -f2- | tr -d '\r')"
export MSYS_NO_PATHCONV=1
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for gv in COM3D25 COM3D2; do
  "$MSB" COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=$gv \
    "-p:COM3D2_DIR=$COM3D2_DIR" "-p:COM3D25_DIR=$COM3D25_DIR" -nologo -v:m 2>&1 | grep -E "error CS|Plugin ->"
done
```

期待: 2 行の `COM3D2.SceneEditor.Plugin -> ...dll` が出て `error CS` が無い。

テスト（テストプロジェクトはプラグイン DLL を参照するため、先に COM3D2 版 Debug ビルドが必要）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~AutoKeyFrameGateTests"
```

### 行末コード

対象ファイルは作業ツリー上 CRLF。Python 等でファイル全体を書き戻す場合は `newline=''` で読み書きし、元の改行を保持すること（LF 化すると diff が全行になる）。`Edit` ツールでの部分置換なら問題ない。

---

## 前提となる調査結果（2026-09-08 時点）

- **既存の自動登録経路**: `TimelineWindow.OnDragCompleted(Maid)`（`TimelineWindow.cs:194-216`）が `MaidDragBoneTracker.onDragCompleted` を購読し、`isAutoKeyFrame` かつ編集モード中（`timelineManager.initialEditFrame != null`）かつドラッグ対象がアクティブメイド（`MTEP.MaidManager.instance.maid`）なら `TimelineManager.AddKeyFrameDiff()` を呼ぶ。発火元は `MaidBoneRotateDragPoint` / `MaidFaceDragPoint` / `MaidFingerDragPoint` / `MaidIKDragPoint` の 4 つのみ。ウィンドウでの値変更には発火経路が無い。
- **キーフレーム差分登録**: `TimelineManager.AddKeyFrameDiff()`（`Timeline/Manager/TimelineManager.cs`、`editTargetLayers` 全レイヤーの `AddKeyFrameDiffBones()` を呼び、変更のあったレイヤーだけ `ApplyCurrentFrame(true)`、`RequestHistory("キーフレーム登録")` を 1 回）。`TimelineLayerBase.AddKeyFrameDiffBones()` は編集開始時スナップショットとの差分ボーンを `UpdateBones` して件数を返す。
- **操作履歴**: `Manager/HistoryManager.cs`。`BeforeEdit(maid, scope, description)` で変更前スナップショットを `_pending` に控え、`Update()` でマウス左ボタン非押下なら `CommitPending()`。`CommitPending` は値が変わっていなければ積まず（`before.Approximately(after)`）、変わっていれば `AddEntry(pending)`。`HistoryEntry.maid` は Pose / Face / Undress / IK / Gravity 等では対象メイド、Object / Light / Camera / Background / PngPlacement では null。
- **`BeforeEdit` を呼ぶウィンドウ・ドロワー**（= 今回の対象）: BackgroundRowDrawer, BackgroundWindow, BoneSliderRowDrawer, FaceMorphRowDrawer, InspectorWindow, LightRowDrawer, MaidCallWindow, MaidFaceWindow, MaidFingerWindow, MaidGravityWindow, MaidIKWindow, MaidPoseWindow, MaidUndressWindow, MainCameraRowDrawer, ModelBoneRowDrawer, ObjectTransformRowDrawer, PngPlacementInspector, PngPlacementWindow, GizmoRenderer, BoneEditManager, 各 DragPoint, Timeline/ItemInspector の Morph / Undress。
- **`BeforeEdit` を呼ばない（= 今回も自動登録されない）ウィンドウ**: MaterialEditWindow, ShapeKeyEditWindow (MaidShapeKeyRowDrawer / ModelShapeKeyRowDrawer), LiveEffectWindow, SoundWindow, TextWindow, VideoWindow, PostEffectRowDrawer, StageLight/StageLaser/Psyllium 系ドロワー, EyesPosRowDrawer, MaidFollowRowDrawer, SubCameraRowDrawer。これらは操作履歴未対応のため、履歴対応を追加するのが先。本計画のスコープ外とし、ドキュメントに明記する。
- **タイムライン側の履歴**: `TimelineHistoryManager.AddHistory` は `HistoryManager.AddEntry(TimelineHistoryEntry)` を直接呼ぶ。`CommitPending` を通らないので、自動登録 → `RequestHistory` → `AddEntry` の流れで `onEditCommitted` が再発火するループは起きない。
- **二重発火**: ドラッグ編集は `onDragCompleted` と履歴確定の両方が発火する（同一フレームとは限らない）。2 回目の `AddKeyFrameDiff` は「スナップショットとの差分」を再登録するため、現状では同じ値の `UpdateBones` と `RequestHistory` がもう一度走り、履歴に「キーフレーム登録」が 2 件積まれる。Task 2 でこれを無害化する。
- **設定の既定値**: `Timeline/Config.cs:64` は `isAutoKeyFrame = true`。`docs-site/guide/timeline.md:57` は「既定は無効」と書いており実装とずれている。Task 5 で文言を直す。

---

## ファイル構成

| ファイル | 役割 | 操作 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs` | 操作確定イベント `onEditCommitted` を公開 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/AutoKeyFrameGate.cs` | 自動登録の発火条件（純粋ロジック） | 新規 |
| `source/COM3D2.SceneEditor.Plugin.Tests/AutoKeyFrameGateTests.cs` | 上記のテスト | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs` | 登録済みボーンの除外で再登録を no-op に | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | `AddKeyFrameDiff(bool quiet)` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs` | 履歴確定の購読、ドラッグ完了との共通化 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` | `isAutoKeyFrame` のコメント | 変更 |
| `docs-site/guide/timeline.md` | 自動登録の説明 | 変更 |
| `docs/superpowers/specs/timeline-window-roadmap.md` | 実装状況の追記 | 変更 |

---

### Task 1: 発火条件 `AutoKeyFrameGate` を純粋ロジックとして追加（TDD）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/AutoKeyFrameGate.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/AutoKeyFrameGateTests.cs`

**Interfaces:**
- Produces: `COM3D2.SceneEditor.Plugin.AutoKeyFrameGate.ShouldRegister(bool isAutoKeyFrame, bool isEditing, object editedMaid, object activeMaid) : bool`
  - `editedMaid` は操作対象メイド（メイドに紐づかない操作なら null）、`activeMaid` はタイムラインのアクティブメイド。`Maid` 型に依存させないよう `object` で受け、参照一致で比較する。

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/AutoKeyFrameGateTests.cs`:

```csharp
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 自動キーフレーム登録の発火条件を固定する。
    /// 「メイドに紐づかない操作 (ライト・カメラ等) はアクティブメイドに関係なく登録する」
    /// 「別メイドへの操作は登録しない」の 2 点が要
    /// </summary>
    public class AutoKeyFrameGateTests
    {
        private static readonly object maidA = new object();
        private static readonly object maidB = new object();

        [Fact]
        public void 自動登録が無効なら登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(false, true, maidA, maidA));
        }

        [Fact]
        public void 編集モード外なら登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, false, maidA, maidA));
        }

        [Fact]
        public void アクティブメイドへの操作は登録する()
        {
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, maidA, maidA));
        }

        [Fact]
        public void 別メイドへの操作は登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, true, maidB, maidA));
        }

        [Fact]
        public void メイドに紐づかない操作はアクティブメイドに関係なく登録する()
        {
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, null, maidA));
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, null, null));
        }

        [Fact]
        public void アクティブメイドが居ないときのメイド操作は登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, true, maidA, null));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run（先に COM3D2 版 Debug ビルドが通っている状態で）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~AutoKeyFrameGateTests"
```

Expected: ビルドエラー `CS0103: 'AutoKeyFrameGate' が存在しません`（テストプロジェクトはプラグイン DLL を参照するため、プラグイン側に型が無いと落ちる）。

- [ ] **Step 3: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/AutoKeyFrameGate.cs`:

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 操作確定時に現在フレームへ自動でキーフレーム登録するかの判定。
    /// ドラッグ完了と操作履歴の確定の両方から使うため、Unity 型に依存しない純粋ロジックにしている
    /// </summary>
    public static class AutoKeyFrameGate
    {
        /// <param name="isAutoKeyFrame">「自動登録」トグルの状態</param>
        /// <param name="isEditing">タイムラインの編集モード中か (編集開始時スナップショットがあるか)</param>
        /// <param name="editedMaid">操作対象のメイド。ライト・カメラ等メイドに紐づかない操作は null</param>
        /// <param name="activeMaid">タイムラインのアクティブメイド。未配置なら null</param>
        public static bool ShouldRegister(
            bool isAutoKeyFrame, bool isEditing, object editedMaid, object activeMaid)
        {
            if (!isAutoKeyFrame || !isEditing)
            {
                return false;
            }

            // 登録対象レイヤーはアクティブメイドのスロットに限られるため、
            // 別メイドへの操作は差分が出ず、登録しても無駄になる
            if (editedMaid != null && !ReferenceEquals(editedMaid, activeMaid))
            {
                return false;
            }

            return true;
        }
    }
}
```

- [ ] **Step 4: 両 GameVersion をビルドし、テストが通ることを確認する**

Run: 「ビルド・テストコマンド」節のビルドコマンド → テストコマンド。

Expected: ビルド 2 本成功、`Passed! - Failed: 0, Passed: 6`。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/AutoKeyFrameGate.cs source/COM3D2.SceneEditor.Plugin.Tests/AutoKeyFrameGateTests.cs
git commit -m "feat(timeline): 自動キーフレーム登録の発火条件を AutoKeyFrameGate に切り出す"
```

---

### Task 2: 同じ値で登録済みのボーンを差分登録から除外する

二重発火（ドラッグ完了 + 履歴確定）や「登録」ボタン連打で、同じ内容の「キーフレーム登録」履歴が重複しないようにする。

この除外は手動「登録」にも適用する（仕様）。理由:
- `ValueData.Equals` は値のみを比較し、補間（tangent）は見ない。一方 `FrameData.UpdateBone` → `FromTransformData` → `ValueData.FromValue` は補間値まで上書きする。つまり「同じ値のボーンを再登録する」と、そのボーンの補間設定がシーンから取り直した既定値で潰れる。
- 差分の基準は「編集開始時スナップショット」であり「前回登録」ではない。自動登録が 1 編集セッション中に何度も走ると、最初に登録したボーンが以後の登録のたびに再登録され続け、その間にユーザーが調整した補間設定が毎回消える。除外はこの事故を防ぐために必須。
- 「同じ値で登録し直して補間を既定に戻す」操作は文書化された機能ではなく、補間の編集にはカーブエディタ / タンジェント UI がある。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs`（`AddKeyFrameDiffBones`）

**Interfaces:**
- Consumes: `FrameData GetFrame(int frameNo)`（同ファイル、`_keyFrames` から frameNo 一致を返す。無ければ null）、`FrameData.GetBone(string name)`、`ITransformData.Equals(object)`（`TransformDataBase.Equals` は name と values を比較）。
- Produces: `AddKeyFrameDiffBones()` の戻り値は「実際に値が変わったボーン数」になる。既存の呼び出し元（`TimelineManager.AddKeyFrameDiff`）はそのまま。

- [ ] **Step 1: 実装を書く**

`TimelineLayerBase.cs` の `AddKeyFrameDiffBones()` を次の形にする（`var diffBones = tmpFrame.GetDiffBones(initialFrame);` の直後に除外処理を挟む）:

```csharp
        public int AddKeyFrameDiffBones()
        {
            var initialFrame = timelineManager.GetInitialEditFrame(this);
            if (initialFrame == null || maid == null)
            {
                return 0;
            }

            var tmpFrame = CreateFrame(timelineManager.currentFrameNo);
            UpdateFrame(tmpFrame);

            var diffBones = tmpFrame.GetDiffBones(initialFrame);

            // ドラッグ完了と操作履歴の確定で自動登録が 2 回走ることがあるため、
            // 現在フレームに同じ値で登録済みのボーンは除いて 2 回目を no-op にする
            var existingFrame = GetFrame(timelineManager.currentFrameNo);
            if (existingFrame != null)
            {
                diffBones.RemoveAll(bone =>
                {
                    var existingBone = existingFrame.GetBone(bone.name);
                    return existingBone != null && bone.transform.Equals(existingBone.transform);
                });
            }

            if (diffBones.Count == 0)
            {
                return 0;
            }

            UpdateBones(timelineManager.currentFrameNo, diffBones);
            return diffBones.Count;
        }
```

- [ ] **Step 2: 両 GameVersion をビルドする**

Run: ビルドコマンド。Expected: `error CS` 無し。

- [ ] **Step 3: 既存テストが通ることを確認する**

Run:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: `Failed: 0`。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs
git commit -m "fix(timeline): 同じ値で登録済みのボーンを差分登録から除き、二重登録で履歴が重複しないようにする"
```

---

### Task 3: `HistoryManager` に操作確定イベントを追加し、`AddKeyFrameDiff` に quiet モードを付ける

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs`（`onChanged` 宣言の直後、`CommitPending` の末尾）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（`AddKeyFrameDiff`）

**Interfaces:**
- Produces: `HistoryManager.onEditCommitted : event Action<HistoryEntry>`。値が変わった内部操作（`BeforeEdit` 経由）が 1 件確定するたびに発火。`TimelineHistoryEntry` や `HistoryAPI.Register` の外部エントリでは発火しない。
- Produces: `TimelineManager.AddKeyFrameDiff(bool quiet = false)`。`quiet` が true のとき、編集モード外 / メイド未配置 / 変更なし の情報ログを出さずに戻る（自動登録は毎操作で呼ばれるためログを汚さない）。既存呼び出し `AddKeyFrameDiff()` の挙動は不変。

- [ ] **Step 1: `HistoryManager` にイベントを追加する**

`Manager/HistoryManager.cs` の

```csharp
        /// <summary>履歴が変化した (追加/undo/redo/ジャンプ/クリア)。ウィンドウ更新用</summary>
        public event Action onChanged;
```

の直後に追加:

```csharp
        /// <summary>
        /// 内部操作 (BeforeEdit 経由) が値の変更を伴って 1 件確定した。
        /// タイムラインの自動キーフレーム登録が購読する。
        /// 外部プラグインの登録 (AddEntry 直接) や undo/redo では発火しない
        /// </summary>
        public event Action<HistoryEntry> onEditCommitted;
```

`CommitPending()` にパラメータを追加し、末尾でイベントを発火する。`AddEntry` は `_isApplying` 中に何もしないため、履歴に積まれていないのに自動登録だけ走る不整合を避けて同じ条件で発火を止める:

```csharp
        /// <param name="notify">確定を onEditCommitted で通知するか。プラグイン無効化時の掃き出しでは通知しない</param>
        private void CommitPending(bool notify = true)
        {
            var pending = _pending;
            _pending = null;

            if (pending.maid == null && HistoryScopeUtils.RequiresMaid(pending.scope))
            {
                return;
            }

            // 変更後は before と同じ対象集合をその時点の値で取り直す
            pending.after = pending.before.CaptureCurrent();

            // クリックのみで値が変わっていない操作 (ドラッグ点の選択等) は積まない
            if (pending.before.Approximately(pending.after))
            {
                return;
            }

            AddEntry(pending);

            // AddEntry は適用中 (_isApplying) に受け付けないため、履歴に載らない操作は通知しない
            if (notify && !_isApplying)
            {
                onEditCommitted?.Invoke(pending);
            }
        }
```

`OnPluginDisable()` の `CommitPending();` は `CommitPending(notify: false);` にする（無効化中はタイムライン側のシングルトンが片付いている可能性があり、自動登録を走らせない）。他の `CommitPending()` 呼び出し（`BeforeEdit` / `Update` / `AddEntry` / `BeginApply`）は引数なしのまま。

- [ ] **Step 2: `TimelineManager.AddKeyFrameDiff` に quiet を追加する**

`Timeline/Manager/TimelineManager.cs` の `AddKeyFrameDiff()` を次の形にする:

```csharp
        /// <summary>
        /// 編集開始時のスナップショットから変化したパラメータを、編集対象レイヤー全てにキーフレーム登録する
        /// </summary>
        /// <param name="quiet">true なら登録しなかった理由の情報ログを出さない (自動登録用)</param>
        public void AddKeyFrameDiff(bool quiet = false)
        {
            if (initialEditFrame == null)
            {
                if (!quiet)
                {
                    MTEUtils.Log("編集モード中のみキーフレームの登録ができます");
                }
                return;
            }

            if (maid == null)
            {
                if (!quiet)
                {
                    MTEUtils.LogError("メイドが配置されていません");
                }
                return;
            }

            var changedLayers = new List<ITimelineLayer>();
            foreach (var layer in editTargetLayers)
            {
                try
                {
                    if (layer.AddKeyFrameDiffBones() > 0)
                    {
                        changedLayers.Add(layer);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("キーフレーム登録に失敗しました layer={0}", layer.layerName);
                    MTEUtils.LogException(e);
                }
            }

            if (changedLayers.Count == 0)
            {
                if (!quiet)
                {
                    MTEUtils.Log("変更がないのでキーフレームの登録をスキップしました");
                }
                return;
            }

            foreach (var layer in changedLayers)
            {
                layer.ApplyCurrentFrame(true);
            }

            RequestHistory("キーフレーム登録");
        }
```

- [ ] **Step 3: 両 GameVersion をビルドする**

Run: ビルドコマンド。Expected: `error CS` 無し。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "feat(history): 操作確定イベント onEditCommitted を追加し、AddKeyFrameDiff に quiet モードを付ける"
```

---

### Task 4: `TimelineWindow` で履歴確定を購読し、ドラッグ完了と経路を共通化する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（コンストラクタの購読 3 行付近、`OnDragCompleted`）

**Interfaces:**
- Consumes: `HistoryManager.instance.onEditCommitted`（Task 3）、`HistoryEntry.maid`、`AutoKeyFrameGate.ShouldRegister`（Task 1）、`TimelineManager.AddKeyFrameDiff(quiet: true)`（Task 3）。

- [ ] **Step 1: 購読を追加する**

コンストラクタの

```csharp
            MaidDragBoneTracker.onDragCompleted += OnDragCompleted;
```

の直後に追加:

```csharp
            HistoryManager.instance.onEditCommitted += OnEditCommitted;
```

（`onDragCompleted` と同様、このウィンドウはシングルトンで解除しない。`HistoryManager` も常駐のため参照が残っても問題ない）

- [ ] **Step 2: `OnDragCompleted` を共通メソッドへ寄せる**

既存の `OnDragCompleted(Maid maid)` 本体（コメント含む）を次の 3 メソッドに置き換える:

```csharp
        // ドラッグ編集完了時の自動キーフレーム登録 (SE 独自機能)
        private void OnDragCompleted(Maid maid)
        {
            TryAutoKeyFrame(maid);
        }

        // 各ウィンドウでの値変更が操作履歴として確定したときの自動キーフレーム登録。
        // ドラッグ編集は onDragCompleted と両方から届くが、2 回目は登録済みボーンが除かれて no-op になる
        private void OnEditCommitted(HistoryEntry entry)
        {
            TryAutoKeyFrame(entry.maid);
        }

        /// <summary>
        /// 自動登録が有効で編集モード中なら、現在フレームへ差分をキーフレーム登録する。
        /// 指ドラッグ等は選択同期を経ずアクティブメイドが別メイドのままになり得るため、
        /// 操作対象メイドが登録対象 (アクティブメイド) と一致する場合のみ登録する。
        /// メイドに紐づかない操作 (ライト・カメラ等) は editedMaid が null で常に対象
        /// </summary>
        private void TryAutoKeyFrame(Maid editedMaid)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            var isEditing = timelineManager.currentLayer != null
                && timelineManager.initialEditFrame != null;

            if (!AutoKeyFrameGate.ShouldRegister(
                MTEP.ConfigManager.instance.config.isAutoKeyFrame,
                isEditing,
                editedMaid,
                MTEP.MaidManager.instance.maid))
            {
                return;
            }

            timelineManager.AddKeyFrameDiff(quiet: true);
        }
```

- [ ] **Step 3: 両 GameVersion をビルドする**

Run: ビルドコマンド。Expected: `error CS` 無し。

- [ ] **Step 4: 実機確認（ゲーム起動中の場合）**

`debug.bat` はゲームフォルダへコピーするため、ゲーム停止中に実行すると実機に反映される。反映してよい場合のみ `debug.bat com3d25` を実行し、ゲームを起動して以下を確認する。ゲーム起動中で反映できない場合は、この Step をユーザーへの確認依頼として報告に残す。

1. タイムラインを新規作成し、編集モード ON、「自動登録」ON にする
2. ライトウィンドウで追加ライトの位置スライダーを動かして離す → 現在フレームにライトレイヤーのキーフレームが増える（レイヤー表示を「全て表示」にして確認）
3. 表情ウィンドウでモーフのスライダーを動かして離す → 表情レイヤーにキーフレームが増える
4. IK ドラッグでポーズを変えて離す → モーションレイヤーに 1 回だけ登録され、履歴ウィンドウの「キーフレーム登録」が 1 件だけ増える（二重登録で 2 件にならない）
5. 「登録」ボタンを 2 回連続で押す → 2 回目は履歴が増えない
6. 別メイドを Hierarchy で選ばずにそのメイドの指ドラッグ点を操作 → 登録されない（ログにも何も出ない）
7. 自動登録 OFF で 2〜3 を再実施 → 登録されない
8. `Ctrl+Z` を 2 回 → 1 回目でキーフレーム登録が戻り、2 回目でスライダー値が戻る

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): 各ウィンドウでの値変更確定時にも自動キーフレーム登録する"
```

---

### Task 5: 設定コメントとドキュメントを更新する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs:63`
- Modify: `docs-site/guide/timeline.md:57-58`
- Modify: `docs/superpowers/specs/timeline-window-roadmap.md:97`

- [ ] **Step 1: `Config.cs` のコメントを直す**

```csharp
        // ドラッグ編集完了時に現在フレームへ自動でキーフレーム登録する (SE 独自機能)
        public bool isAutoKeyFrame = true;
```

を

```csharp
        // ドラッグ編集の完了時と、操作履歴に載る値変更の確定時に
        // 現在フレームへ自動でキーフレーム登録する (SE 独自機能)
        public bool isAutoKeyFrame = true;
```

にする。

- [ ] **Step 2: `docs-site/guide/timeline.md` を直す**

```markdown
「自動登録」トグルを有効にすると、ドラッグ編集（IK・ボーン回転・指・顔）を離したタイミングで
現在フレームへ自動的にキーフレームが登録されます（既定は無効。スライダーによる編集は対象外）。
```

を

```markdown
「自動登録」トグルを有効にすると、ドラッグ編集（IK・ボーン回転・指・顔）を離したタイミングや、
各ウィンドウ（ライト・カメラ・背景・表情・脱衣・IK・重力・モデル配置など）で値を変更して確定したタイミングで、
現在フレームへ自動的にキーフレームが登録されます（既定は有効）。
アクティブなメイド以外への操作は登録されません。
操作履歴（Undo）に対応していないウィンドウ（マテリアル編集・シェイプキー編集・演出系・音声・テキスト・動画）での変更は対象外なので、「登録」ボタンか `Return` で登録してください。
```

にする。

- [ ] **Step 3: ロードマップに追記する**

`docs/superpowers/specs/timeline-window-roadmap.md:97` の末尾「（既定 OFF、スライダー編集は対象外）」を「（既定 ON。2026-09-08 に操作履歴の確定へ発火元を広げ、履歴対応ウィンドウでのスライダー編集も対象にした。履歴未対応ウィンドウは対象外）」に置き換える。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs docs-site/guide/timeline.md docs/superpowers/specs/timeline-window-roadmap.md
git commit -m "docs(timeline): 自動登録が各ウィンドウの値変更確定でも動くことを記載する"
```

---

## 自己レビュー

- **仕様カバレッジ**: 発火条件（Task 1）、二重発火の無害化（Task 2）、イベント公開と quiet（Task 3）、購読（Task 4）、文書（Task 5）。履歴未対応ウィンドウは明示的にスコープ外とし Task 5 で告知。
- **型の整合**: `AutoKeyFrameGate.ShouldRegister(bool, bool, object, object)` は Task 1 定義・Task 4 使用で一致。`HistoryManager.onEditCommitted : Action<HistoryEntry>` は Task 3 定義・Task 4 の `OnEditCommitted(HistoryEntry)` で一致。`AddKeyFrameDiff(bool quiet = false)` は Task 3 定義・Task 4 の `AddKeyFrameDiff(quiet: true)` で一致。
- **既知のリスク**:
  - `HistoryManager.Update` は `TimelineManager.Update` と同じフレームで走る。確定 → `AddKeyFrameDiff` → `RequestHistory` → 次の `TimelineManager.Update` で `AddHistory` の順になり、履歴は「値変更」「キーフレーム登録」の順で積まれる（実機確認 8 で検証）。
  - `historyLimit <= 0`（履歴無効）の環境では `BeforeEdit` が何もしないためウィンドウ経由の自動登録も動かない。ドラッグ経路は従来通り動く。ドキュメントには書かず、報告で触れる。
  - `onEditCommitted` は `AddEntry` が受け付けない適用中（`_isApplying`）とプラグイン無効化時の掃き出しでは発火しない（Task 3）。
  - Task 2 の除外は手動「登録」の「同じ値の再登録で補間を既定に戻す」挙動を無くす。上記の通り仕様として受け入れる。

## レビュー却下メモ

- `requestedHistoryDesc` が単一フィールドのため同一フレーム内の複数 `RequestHistory` でラベルが後勝ちになる — 既存の設計上の性質で、積まれる履歴エントリはタイムライン全体の XML なのでデータは失われずラベルのみの問題。本計画では扱わない。
- `TryAutoKeyFrame` の統合的な振る舞いに自動テストが無い — `MaidManager` / `TimelineManager` / `Maid` は Unity・ゲーム実行時に依存しテストプロジェクトから駆動できない。テスト可能な判定部分は `AutoKeyFrameGate` に切り出し済みで、残りは実機確認で担保する。
- Task 2 の除外を自動登録経由のみに限定する案 — 上記「Task 2 の仕様」の通り、手動登録でも補間設定を守る方向に働くため限定しない。
