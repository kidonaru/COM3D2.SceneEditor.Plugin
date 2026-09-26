# Inspector 値コピー＆ペースト Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない)。Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inspector のキーフレームブロックとタイムラインのレイヤー項目にメニューアイコン（☰）を置き、同じ TransformType の値をコピー＆ペーストできるようにする。

**Architecture:**
- 値はプロセス内のクリップボード `ItemValueClipboard` に `(名前, ITransformData)` の組で持つ
- キーフレームへの貼り付けは、`FromTransformData` でキーの値を書き換える
- レイヤー項目への貼り付けは、`TimelineLayerBase.ApplyTransformDirect` で `ApplyMotion`（または各レイヤーの上書き実装）を呼び、シーンへ直接当てる。事前に `HistoryManager.BeforeEdit` を通すので編集モードに入り、自動キー登録の対象になる
- UI は `GUIComboBox<T>` をアイコンボタンとして使う共通部品 `ItemClipboardMenu` で描く

**Tech Stack:** C# (COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1)、Unity IMGUI（MTEUtils の GUIView）、xunit (net48)

**Spec:** `docs/superpowers/specs/2026-09-26-inspector-value-clipboard-design.md`

## Global Constraints

- コードのコメントとログメッセージは日本語で書く
- MTEUtils サブモジュール（`source/COM3D2.SceneEditor.Plugin/MTEUtils/`）は変更しない
- .NET 3.5 で使えない API（入力 5 個以上の `Func<>` / `Action<>`、`Tuple`、C# 6 より新しい言語機能の一部）を使わない。既存コードと同じく `=>` 式本体・`?.`・文字列補間は使ってよいが、補間より `string.Format` / 連結が既存の流儀
- 新規 `.cs` は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ明示的に追加する（ワイルドカードではない）
- タイムライン XML の形式は変えない
- `debug.bat` / `deploy.bat` は使わない。ビルドは MSBuild を直接叩く（下記）
- 各タスク末尾ではコミットしない。全タスク完了後に code-review スキル → commit スキルの順で行う（CLAUDE.md の標準フロー）

ビルド + テストのコマンド（Git Bash、リポジトリ直下 `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin` で実行）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" -v:m -nologo \
 && "$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m -nologo \
 && dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

順序は COM3D2 → COM3D25 → テスト（COM3D2 構成のビルドは COM3D25 の出力を消すため）。以下「ビルド + テスト」はこのコマンドを指す。

## Review Focus

1. **項目の無い名前・型違いの貼り付け**: クリップボードにある型と違う項目へ貼ろうとしたら、「貼り付け」は無効表示のままで、何も書き換わらないこと（Task 1 のテスト、Task 3 の `Paste` の除外）
2. **Voice / SE への貼り付け**: 貼ると音が即再生されるだけなので、「貼り付け」は無効表示。コピーはできる（Task 2 の `canApplyTransformDirect`、Task 6 の有効判定）
3. **貼った直後に再生データで戻される**: キーのある項目へ貼っても、次のフレームで元の値へ戻らないこと。`BeforeEdit` が `AutoEditMode.Enter` を呼ぶ順序で担保する（Task 3。実機確認は Task 9）
4. **複数ブロックでのポップアップ位置**: キーフレームブロックが複数並んでも、ポップアップは押したボタンの直下に出ること。コンボをキーごとに別インスタンスにして担保する（Task 4）
5. **Motion レイヤーのブレンドレイヤー調整中**: `MaidAnimationBlendController.IsLayerSelected(maid)` のときは、ボーンスライダーと同じく書き込まない（Task 2）

---

### Task 1: クリップボードと履歴スコープ

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ItemValueClipboard.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs`（enum 末尾と `RequiresMaid`）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="LightClipboard.cs" />` の直後に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ItemValueClipboardTests.cs`

**Interfaces:**
- Produces:
  - `ItemValueClipboard.hasData : bool`
  - `ItemValueClipboard.Set(IList<string> names, IList<MTEP.ITransformData> transforms)`（各 transform は `Clone()` して保持。件数 0 なら何もしない）
  - `ItemValueClipboard.Resolve(string targetName, MTEP.TransformType targetType) : MTEP.ITransformData`（当たらなければ null。返り値はクリップボード内の実体なので、書き換えずに `FromTransformData` / `ApplyTransformDirect` の入力にだけ使う）
  - `ItemValueClipboard.CanResolve(string targetName, MTEP.TransformType targetType) : bool`
  - `ItemValueClipboard.ResolveIndex(IList<string> names, IList<MTEP.TransformType> types, string targetName, MTEP.TransformType targetType) : int`（純粋関数。テスト用に public）
  - `HistoryScope.TimelineItem`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ItemValueClipboardTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ItemValueClipboardTests
    {
        private static int Resolve(string[] names, TransformType[] types, string name, TransformType type)
        {
            return ItemValueClipboard.ResolveIndex(
                new List<string>(names), new List<TransformType>(types), name, type);
        }

        [Fact]
        public void 名前と型が一致する組を優先する()
        {
            var index = Resolve(
                new[] { "Bip01 Spine", "Bip01 Neck" },
                new[] { TransformType.Rotation, TransformType.Rotation },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(1, index);
        }

        [Fact]
        public void 名前が一致しても型が違えば使わない()
        {
            var index = Resolve(
                new[] { "Bip01", "Bip01 Spine" },
                new[] { TransformType.Root, TransformType.Rotation },
                "Bip01", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 一件だけなら型が同じ別名へ貼れる()
        {
            var index = Resolve(
                new[] { "Bip01 Spine" }, new[] { TransformType.Rotation },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(0, index);
        }

        [Fact]
        public void 複数件で名前が当たらなければ貼らない()
        {
            var index = Resolve(
                new[] { "Bip01 Spine", "Bip01 Neck" },
                new[] { TransformType.Rotation, TransformType.Rotation },
                "Bip01 Head", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 一件でも型が違えば貼らない()
        {
            var index = Resolve(
                new[] { "eyeclose" }, new[] { TransformType.Morph },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 空なら貼らない()
        {
            var index = Resolve(new string[0], new TransformType[0], "a", TransformType.Morph);
            Assert.Equal(-1, index);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: ビルド + テスト
Expected: `ItemValueClipboardTests.cs` のコンパイルエラー（`ItemValueClipboard` が存在しない）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/ItemValueClipboard.cs`:

```csharp
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector のキーフレーム・レイヤー項目の値のコピー & ペースト用クリップボード。
    /// (名前, 値) の組を 1 件以上持ち、キーフレームとレイヤー項目で共有する。
    /// タイムラインの「フレームコピー」(システムクリップボードの XML) とは別にプロセス内だけで保持する
    /// </summary>
    public static class ItemValueClipboard
    {
        private static readonly List<string> _names = new List<string>();
        private static readonly List<MTEP.TransformType> _types = new List<MTEP.TransformType>();
        private static readonly List<MTEP.ITransformData> _transforms = new List<MTEP.ITransformData>();

        public static bool hasData => _transforms.Count > 0;

        /// <summary>値を記録する。元データを後から書き換えられても影響しないよう複製して持つ</summary>
        public static void Set(IList<string> names, IList<MTEP.ITransformData> transforms)
        {
            if (names == null || transforms == null || transforms.Count == 0)
            {
                return;
            }

            _names.Clear();
            _types.Clear();
            _transforms.Clear();
            for (var i = 0; i < transforms.Count; i++)
            {
                var transform = transforms[i];
                _names.Add(names[i]);
                _types.Add(transform.type);
                _transforms.Add(transform.Clone());
            }
        }

        /// <summary>貼り付け先 1 件に当てる値。当たらなければ null</summary>
        public static MTEP.ITransformData Resolve(string targetName, MTEP.TransformType targetType)
        {
            var index = ResolveIndex(_names, _types, targetName, targetType);
            return index >= 0 ? _transforms[index] : null;
        }

        public static bool CanResolve(string targetName, MTEP.TransformType targetType)
        {
            return ResolveIndex(_names, _types, targetName, targetType) >= 0;
        }

        /// <summary>
        /// 貼り付け先に当てる組の添字。名前と型が一致する組を優先し、
        /// 無ければ 1 件だけのときに限り型が同じなら使う (別のボーン・モーフへ写す用途)。
        /// 型が同じなら値の並びも同じなので FromTransformData で安全に写せる
        /// </summary>
        public static int ResolveIndex(
            IList<string> names, IList<MTEP.TransformType> types,
            string targetName, MTEP.TransformType targetType)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i] == targetName && types[i] == targetType)
                {
                    return i;
                }
            }

            if (types.Count == 1 && types[0] == targetType)
            {
                return 0;
            }
            return -1;
        }
    }
}
```

`HistoryScope.cs` の enum の `External,` の後に追加:

```csharp
        /// <summary>
        /// タイムラインのレイヤー項目の現在値 (Inspector の値ペースト)。
        /// 捕捉・復元は TimelineItemSnapshot が ApplyTransformDirect で行う
        /// </summary>
        TimelineItem,
```

`RequiresMaid` の `case HistoryScope.External:` の次行に `case HistoryScope.TimelineItem:` を足す（メイドに紐付かないレイヤーもあるため false を返す側）。

csproj に `<Compile Include="ItemValueClipboard.cs" />` を追加する。

- [ ] **Step 4: テストが通ることを確認する**

Run: ビルド + テスト
Expected: 両構成のビルド成功、`ItemValueClipboardTests` 6 件 PASS、既存テストも PASS

---

### Task 2: レイヤーの直接適用 API

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs`
- Modify: `ShapeKeyTimelineLayer.cs`、`ModelShapeKeyTimelineLayer.cs`、`DressTimelineLayer.cs`、`StageLaserTimelineLayer.cs`、`StageLightTimelineLayer.cs`、`PsylliumTimelineLayer.cs`、`MorphTimelineLayer.cs`、`MotionTimelineLayer.cs`、`VoiceTimelineLayer.cs`、`SeTimelineLayer.cs`（すべて `Timeline/TimelineLayer/` 配下）

**Interfaces:**
- Produces（`ITimelineLayer` に追加）:
  - `void ApplyTransformDirect(ITransformData transform)`
  - `bool canApplyTransformDirect { get; }`
- Produces（`TimelineLayerBase`）: `protected virtual void ApplyTransformDirectCore(MotionData motion)`

このタスクは Unity 実体に依存するため単体テストを書かず、ビルドと Task 9 の実機確認で検証する。

- [ ] **Step 1: インターフェースと基底実装を追加する**

`ITimelineLayer.cs` の `void ApplyCurrentFrame(bool motionUpdate);` の直後に追加:

```csharp
        /// <summary>1 項目の値をキーフレームを介さずシーンへ当てる (Inspector の値ペースト用)</summary>
        void ApplyTransformDirect(ITransformData transform);
        /// <summary>直接適用に意味があるレイヤーか。音を鳴らすだけのイベント型は false</summary>
        bool canApplyTransformDirect { get; }
```

`TimelineLayerBase.cs` の `protected abstract void ApplyMotion(...)` 宣言の直後に追加:

```csharp
        public virtual bool canApplyTransformDirect => true;

        /// <summary>
        /// 1 項目の値をキーフレームを介さずシーンへ当てる。
        /// 始点と終点に同じ値を持つ区間を作るので、補間はどのレイヤーでもその値になる
        /// (isConstant が立ち、Hermite も dt == 0 で始点値を返す)
        /// </summary>
        public void ApplyTransformDirect(ITransformData transform)
        {
            if (transform == null || !canApplyTransformDirect)
            {
                return;
            }

            var frameNo = timelineManager.currentFrameNo;
            ApplyTransformDirectCore(new MotionData(transform, transform, frameNo, frameNo));
        }

        /// <summary>
        /// 既定は ApplyMotion を 1 回呼ぶ。indexUpdated = true は区間の切り替わり時だけ
        /// 書く値 (表示状態・背景の種類など) も当てるため。playData を参照するのは
        /// AnimationTimelineLayer だけで、null を渡すと区間先頭の時刻を使う。
        /// ApplyPlayData 側の後処理が要るレイヤーは上書きする
        /// </summary>
        protected virtual void ApplyTransformDirectCore(MotionData motion)
        {
            ApplyMotion(motion, 0f, true, null);
        }
```

`timelineManager` プロパティが基底に無ければ `TimelineManager.instance.currentFrameNo` を使う（基底の他メソッドで使っている書き方に合わせる）。

- [ ] **Step 2: 後処理が要るレイヤーを上書きする**

各ファイルで、既存の `protected override void ApplyPlayData()` の直後に追加する。

`ShapeKeyTimelineLayer.cs`:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            if (maidCache == null)
            {
                return;
            }
            base.ApplyTransformDirectCore(motion);
            maidCache.FixBlendValues(new string[] { motion.name });
        }
```

`ModelShapeKeyTimelineLayer.cs`（ApplyPlayData と同じく全モデルを確定する。対象モデルの特定は ApplyMotion 側の命名規則に依存するため、全件で揃える）:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            base.ApplyTransformDirectCore(motion);
            foreach (var model in modelManager.models)
            {
                model.FixBlendValues();
            }
        }
```

`DressTimelineLayer.cs`:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            _propUpdated = false;
            base.ApplyTransformDirectCore(motion);

            if (_propUpdated)
            {
                maid.AllProcPropSeqStart();
            }
        }
```

`StageLaserTimelineLayer.cs`:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            base.ApplyTransformDirectCore(motion);
            foreach (var controller in stageLaserManager.controllers)
            {
                controller.UpdateLasers();
            }
        }
```

`StageLightTimelineLayer.cs`:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            base.ApplyTransformDirectCore(motion);
            foreach (var controller in stageLightManager.controllers)
            {
                controller.UpdateLights();
            }
        }
```

`PsylliumTimelineLayer.cs`:

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            base.ApplyTransformDirectCore(motion);
            var playingTime = this.playingTime;
            foreach (var controller in psylliumManager.controllers)
            {
                controller.ManualUpdate(playingTime);
            }
        }
```

`VoiceTimelineLayer.cs` と `SeTimelineLayer.cs`（クラス本体の先頭付近）:

```csharp
        /// <summary>区間の切り替わりで即再生するだけのイベントなので、値の直接適用は受け付けない</summary>
        public override bool canApplyTransformDirect => false;
```

- [ ] **Step 3: Morph レイヤーを上書きする**

`MorphTimelineLayer.cs` の `ApplyPlayData()` の直後に追加。`ApplyMotion` はモーフ値を `_applyMorphMap` に貯めるだけで、実際の書き込みは ApplyPlayData の末尾にあるため、同じ書き込みを対象項目だけ行う。強制上書きは Inspector のトグル（`MorphItemInspector`）と同じ経路で書く。

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            if (FaceMorphUtils.IsForceOverrideBone(motion.name))
            {
                var setting = motion.start as TransformDataFaceSetting;
                if (setting != null)
                {
                    SEP.MaidFaceMorphController.SetForceOverride(
                        maid, FaceMorphUtils.ToForceOverride(setting.forceOverride));
                }
                return;
            }

            _applyMorphMap.Clear();
            base.ApplyTransformDirectCore(motion);
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }
```

`SEP` エイリアスがこのファイルに無ければ、`TimelineFaceManager.cs` と同じ `using SEP = COM3D2.SceneEditor.Plugin;` を追加する。

- [ ] **Step 4: Motion レイヤーを上書きする**

`MotionTimelineLayer.cs` の `ApplyMotion` の直後に追加。体ボーンは anm で動いていて `ApplyMotion` の switch を通らないので、`UpdateFrame` の読み取り（`localPosition` / `localRotation` / `localScale`）と対称に直接書く。書き込み前の処理はボーンスライダー（`BoneSliderRowDrawer` → `MaidBoneSliderController.SetOffsetAxis`）と揃える。

```csharp
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            var transform = motion.start;
            switch (transform.type)
            {
                case TransformType.Rotation:
                case TransformType.Root:
                    ApplyBoneTransformDirect(transform, true);
                    return;
                case TransformType.ExtendBone:
                    // 再生側 (ApplyExtendBoneMotion) は位置と拡縮しか書かないので、回転もここで当てる
                    ApplyBoneTransformDirect(transform, false);
                    return;
            }
            base.ApplyTransformDirectCore(motion);
        }

        /// <summary>
        /// ボーンの Transform へ直接書く。stopMotion は anm 再生中の体ボーンだけ true
        /// (再生中は毎フレーム上書きされるため、ボーンスライダーと同じく操作の瞬間に止める)
        /// </summary>
        private void ApplyBoneTransformDirect(ITransformData transform, bool stopMotion)
        {
            var maid = this.maid;
            var maidCache = this.maidCache;
            if (maid == null || maidCache == null)
            {
                return;
            }

            // ブレンドレイヤー調整中はボーンを書かせない (ボーンスライダーと同じ扱い)
            if (SEP.MaidAnimationBlendController.IsLayerSelected(maid))
            {
                MTEUtils.LogWarning("レイヤー調整中はボーンを貼り付けできません: {0}", transform.name);
                return;
            }

            var bone = maidCache.GetBoneTransform(transform.name);
            if (bone == null)
            {
                return;
            }

            if (stopMotion)
            {
                SEP.MaidMotionState.StopMotion(maid);
            }
            SEP.MaidAnimationBlendController.MarkBoneEdit(maid);

            if (transform.hasPosition)
            {
                bone.localPosition = transform.position;
            }
            if (transform.hasRotation)
            {
                bone.localRotation = transform.rotation;
            }
            if (transform.hasScale)
            {
                bone.localScale = transform.scale;
            }
        }
```

`SEP` エイリアスが無ければ追加する。`MTEUtils.LogWarning` のシグネチャ（書式 + 引数）は既存呼び出しに合わせる。

- [ ] **Step 5: ビルドする**

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 3: 現在値の捕捉・スナップショット・貼り付け処理

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineItemValues.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/TimelineItemSnapshot.cs`
- Modify: csproj（`ItemValueClipboard.cs` の次と `Manager\History\LiveEffectSnapshot.cs` の次に追加）

**Interfaces:**
- Consumes: `ItemValueClipboard.Set/Resolve`、`ITimelineLayer.ApplyTransformDirect/canApplyTransformDirect`、`HistoryScope.TimelineItem`
- Produces:
  - `TimelineItemValues.Capture(MTEP.ITimelineLayer layer, IList<string> names, List<string> outNames, List<MTEP.ITransformData> outTransforms)`（捕捉できた項目だけを out へ入れる）
  - `TimelineItemValues.Copy(MTEP.ITimelineLayer layer, IList<string> names)`
  - `TimelineItemValues.CanPaste(MTEP.ITimelineLayer layer, IList<string> names) : bool`
  - `TimelineItemValues.Paste(MTEP.ITimelineLayer layer, IList<string> names)`
  - `TimelineItemSnapshot : IStateSnapshot`（`static TimelineItemSnapshot Capture(MTEP.ITimelineLayer layer, IList<string> names)`）

- [ ] **Step 1: スナップショットを書く**

`Manager/History/TimelineItemSnapshot.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤー項目の現在値。値ペーストの変更前後を控える。
    /// 捕捉はキーフレーム登録と同じく UpdateFrame で、復元は ApplyTransformDirect で行う
    /// </summary>
    public class TimelineItemSnapshot : IStateSnapshot
    {
        private readonly MTEP.ITimelineLayer _layer;
        private readonly List<string> _names = new List<string>();
        private readonly List<MTEP.ITransformData> _transforms = new List<MTEP.ITransformData>();

        private TimelineItemSnapshot(MTEP.ITimelineLayer layer)
        {
            _layer = layer;
        }

        public static TimelineItemSnapshot Capture(MTEP.ITimelineLayer layer, IList<string> names)
        {
            var snapshot = new TimelineItemSnapshot(layer);
            TimelineItemValues.Capture(layer, names, snapshot._names, snapshot._transforms);
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent()
        {
            return Capture(_layer, _names);
        }

        public void Apply(Maid maid)
        {
            foreach (var transform in _transforms)
            {
                _layer.ApplyTransformDirect(transform);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var snapshot = other as TimelineItemSnapshot;
            if (snapshot == null || snapshot._layer != _layer
                || snapshot._transforms.Count != _transforms.Count)
            {
                return false;
            }

            for (var i = 0; i < _transforms.Count; i++)
            {
                if (!MTEP.TransformDataDiff.IsApproximatelyEqual(_transforms[i], snapshot._transforms[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>レイヤーがタイムラインから外れたら書き戻さない</summary>
        public bool CanApply(Maid maid)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            return timelineManager.timeline != null && timelineManager.layers.Contains(_layer);
        }
    }
}
```

`TransformDataDiff` の名前空間は `Timeline/TransformDataDiff.cs` の宣言に合わせる。

- [ ] **Step 2: 捕捉と貼り付けの処理を書く**

`TimelineItemValues.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤー項目の現在値のコピー & ペースト。
    /// キーフレームを作らず、シーン上の値を読み書きする
    /// </summary>
    public static class TimelineItemValues
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private static readonly List<string> _copyNames = new List<string>();
        private static readonly List<MTEP.ITransformData> _copyTransforms = new List<MTEP.ITransformData>();
        private static readonly List<string> _pasteNames = new List<string>();

        /// <summary>
        /// 現在値を読み出す。キーフレーム登録 (AddKeyFrames) と同じく一時フレームへ
        /// UpdateFrame で全項目を書き出し、指定の名前だけを取り出す。
        /// 値を持たない項目 (実体の無いモデル等) は結果に含めない
        /// </summary>
        public static void Capture(
            MTEP.ITimelineLayer layer, IList<string> names,
            List<string> outNames, List<MTEP.ITransformData> outTransforms)
        {
            outNames.Clear();
            outTransforms.Clear();
            if (layer == null || names == null || names.Count == 0)
            {
                return;
            }

            var frame = layer.CreateFrame(timelineManager.currentFrameNo);
            layer.UpdateFrame(frame, force: true);

            foreach (var name in names)
            {
                var bone = frame.GetBone(name);
                if (bone == null)
                {
                    continue;
                }
                outNames.Add(name);
                outTransforms.Add(bone.transform);
            }
        }

        public static void Copy(MTEP.ITimelineLayer layer, IList<string> names)
        {
            Capture(layer, names, _copyNames, _copyTransforms);
            if (_copyTransforms.Count == 0)
            {
                MTEUtils.LogWarning("コピーできる値がありません");
                return;
            }
            ItemValueClipboard.Set(_copyNames, _copyTransforms);
        }

        /// <summary>貼り付け先のうち 1 件以上に当たる値があるか。毎フレーム呼ぶので値の捕捉はしない</summary>
        public static bool CanPaste(MTEP.ITimelineLayer layer, IList<string> names)
        {
            if (layer == null || names == null || !layer.canApplyTransformDirect)
            {
                return false;
            }
            foreach (var name in names)
            {
                if (ItemValueClipboard.CanResolve(name, layer.GetTransformType(name)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 当たる値を現在値へ直接当てる。先に BeforeEdit を通して編集モードへ入るので、
        /// 貼った値が再生データで上書きされず、確定時に自動キーフレーム登録の対象になる
        /// </summary>
        public static void Paste(MTEP.ITimelineLayer layer, IList<string> names)
        {
            if (!CanPaste(layer, names))
            {
                return;
            }

            _pasteNames.Clear();
            foreach (var name in names)
            {
                if (ItemValueClipboard.CanResolve(name, layer.GetTransformType(name)))
                {
                    _pasteNames.Add(name);
                }
            }

            // Capture は確定待ちが無いときだけ評価されるため、対象名は複製して渡す
            var targets = new List<string>(_pasteNames);
            var description = targets.Count == 1
                ? "貼り付け: " + targets[0]
                : string.Format("貼り付け: {0}件", targets.Count);
            // 貼り付けごとに別の操作として確定させるため、対象キーは毎回新しくする
            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.TimelineItem, description, new object(),
                () => TimelineItemSnapshot.Capture(layer, targets));

            foreach (var name in targets)
            {
                layer.ApplyTransformDirect(
                    ItemValueClipboard.Resolve(name, layer.GetTransformType(name)));
            }
        }
    }
}
```

`frame.GetBone` / `layer.CreateFrame` / `UpdateFrame` の引数名は `TimelineLayerBase.AddKeyFrames` の呼び出しに合わせる。

- [ ] **Step 3: csproj へ追加してビルドする**

`<Compile Include="TimelineItemValues.cs" />` と `<Compile Include="Manager\History\TimelineItemSnapshot.cs" />` を追加する。

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 4: メニューアイコンと共通ボタン部品

**Files:**
- Create: `assets/icons/Menu.svg`
- Modify: `assets/icons/generate.js`（`ICONS` の末尾に `'Menu'`）
- Modify: `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs`（`Kind.Menu` と base64）
- Create: `source/COM3D2.SceneEditor.Plugin/ItemClipboardMenu.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs`（`DrawContent` 先頭で `BeginFrame`）
- Modify: csproj

**Interfaces:**
- Produces:
  - `ToolbarIcons.Kind.Menu`
  - `ItemClipboardMenu.ButtonWidth : float`（= 20）
  - `ItemClipboardMenu.GetReservedWidth(GUIView view) : float`（= `ButtonWidth + view.margin`。直前の要素の幅から引く量）
  - `ItemClipboardMenu.GetRemainingWidth(GUIView view) : float`（現在位置から、右端のボタンぶんを除いた残り幅）
  - `ItemClipboardMenu.instance.Draw(GUIView view, object owner, string key, bool canCopy, bool canPaste, Action onCopy, Action onPaste)`（現在位置に 20x20 のボタンを描く）
  - `ItemClipboardMenu.instance.DrawRightAligned(...)`（引数は Draw と同じ。行の右端へ寄せて描く）
  - `ItemClipboardMenu.instance.BeginFrame()`

- [ ] **Step 1: アイコンを作る**

`assets/icons/Menu.svg`（既存アイコンと同じく黒の縁取り + 白線）:

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <path d="M7 9 H25 M7 16 H25 M7 23 H25" fill="none" stroke="#000000" stroke-width="4.4" stroke-linecap="round"/>
  <path d="M7 9 H25 M7 16 H25 M7 23 H25" fill="none" stroke="#ffffff" stroke-width="2.2" stroke-linecap="round"/>
</svg>
```

`generate.js` の `ICONS` 配列の末尾（`'Undo', 'Redo',` の後）に `'Menu',` を足し、生成する:

```bash
cd assets/icons && node generate.js > /c/Users/kidon/AppData/Local/Temp/claude/W--COM3D2-5-work-COM3D2-SceneEditor-Plugin/d0b7b28b-2695-4eaa-8f6e-1174f1168d92/scratchpad/icons.txt
```

出力の `// Menu` の次の行（`"iVBOR...",`）を控える。他のアイコンの PNG が再生成で変わっていないことを `git status assets/icons` で確認する（変わっていたら `git checkout` で戻す）。

- [ ] **Step 2: ToolbarIcons に登録する**

`ToolbarIcons.Kind` の末尾（`Redo,` の後）:

```csharp
            // 項目メニュー (横線 3 本)
            Menu,
```

`PNG_BASE64` の末尾（`// Redo` の文字列の後）に `// Menu` と控えた base64 文字列を足す。

- [ ] **Step 3: 共通ボタン部品を書く**

`ItemClipboardMenu.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 値のコピー / 貼り付けを出すメニューアイコンボタン。
    /// GUIComboBox をアイコンボタン・矢印なしで使い、ポップアップは Inspector の
    /// ComboBoxPopupWindow.ProcessFocus が出す。ポップアップは押したボタンの位置を
    /// 基準に出るため、描く行ごとに別インスタンスを持つ (owner + key で区別する)
    /// </summary>
    public class ItemClipboardMenu
    {
        public const float ButtonWidth = 20f;

        private enum Command
        {
            Copy,
            Paste,
        }

        private class Entry
        {
            public GUIComboBox<Command> comboBox;
            public bool canCopy;
            public bool canPaste;
            public Action onCopy;
            public Action onPaste;
            public int lastFrame;
        }

        private static readonly List<Command> _commands = new List<Command> { Command.Copy, Command.Paste };

        private readonly Dictionary<object, Dictionary<string, Entry>> _entries =
            new Dictionary<object, Dictionary<string, Entry>>();
        private readonly List<object> _removeOwners = new List<object>();
        private readonly List<string> _removeKeys = new List<string>();
        private int _currentFrame = -1;

        private static ItemClipboardMenu _instance = null;
        public static ItemClipboardMenu instance => _instance ?? (_instance = new ItemClipboardMenu());

        private ItemClipboardMenu()
        {
        }

        /// <summary>右端のボタンを置くために直前の要素の幅から引く量</summary>
        public static float GetReservedWidth(GUIView view)
        {
            return ButtonWidth + view.margin;
        }

        /// <summary>現在位置から、右端のボタンと末尾の余白を除いた残り幅</summary>
        public static float GetRemainingWidth(GUIView view)
        {
            return view.viewRect.width - view.currentPos.x - view.padding.x * 2
                - GetReservedWidth(view) - view.margin;
        }

        /// <summary>
        /// 前のフレームに描かれなかったボタンを捨てる。
        /// OnGUI はイベントごとに複数回呼ばれるので、フレームが変わったときだけ掃除する
        /// </summary>
        public void BeginFrame()
        {
            var frame = Time.frameCount;
            if (frame == _currentFrame)
            {
                return;
            }
            var previous = _currentFrame;
            _currentFrame = frame;

            _removeOwners.Clear();
            foreach (var ownerPair in _entries)
            {
                _removeKeys.Clear();
                foreach (var pair in ownerPair.Value)
                {
                    if (pair.Value.lastFrame < previous)
                    {
                        _removeKeys.Add(pair.Key);
                    }
                }
                foreach (var key in _removeKeys)
                {
                    ownerPair.Value.Remove(key);
                }
                if (ownerPair.Value.Count == 0)
                {
                    _removeOwners.Add(ownerPair.Key);
                }
            }
            foreach (var owner in _removeOwners)
            {
                _entries.Remove(owner);
            }
        }

        /// <summary>現在位置に 20x20 のメニューボタンを描く</summary>
        public void Draw(
            GUIView view, object owner, string key,
            bool canCopy, bool canPaste, Action onCopy, Action onPaste)
        {
            var entry = GetEntry(owner, key ?? string.Empty);
            entry.canCopy = canCopy;
            entry.canPaste = canPaste;
            entry.onCopy = onCopy;
            entry.onPaste = onPaste;
            entry.lastFrame = Time.frameCount;

            // コピーは値を書き換えないので、ビューの編集開始フック (AutoEditMode.Enter 等) を通さない。
            // 貼り付けは TimelineItemValues.Paste / キーフレーム側が自前で履歴を扱う
            var onBeforeValueChanged = view.onBeforeValueChanged;
            view.onBeforeValueChanged = null;
            entry.comboBox.currentIndex = -1;
            entry.comboBox.DrawTextureButton(view);
            view.onBeforeValueChanged = onBeforeValueChanged;
        }

        /// <summary>行の右端へ寄せてメニューボタンを描く (横並びの中で使う)</summary>
        public void DrawRightAligned(
            GUIView view, object owner, string key,
            bool canCopy, bool canPaste, Action onCopy, Action onPaste)
        {
            view.currentPos.x = view.viewRect.width - view.padding.x * 2 - ButtonWidth;
            Draw(view, owner, key, canCopy, canPaste, onCopy, onPaste);
        }

        private Entry GetEntry(object owner, string key)
        {
            Dictionary<string, Entry> ownerEntries;
            if (!_entries.TryGetValue(owner, out ownerEntries))
            {
                ownerEntries = new Dictionary<string, Entry>();
                _entries[owner] = ownerEntries;
            }

            Entry entry;
            if (ownerEntries.TryGetValue(key, out entry))
            {
                return entry;
            }

            entry = new Entry();
            var captured = entry;
            entry.comboBox = new GUIComboBox<Command>
            {
                items = _commands,
                defaultTexture = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Menu),
                showArrow = false,
                currentIndex = -1,
                buttonSize = new Vector2(ButtonWidth, ButtonWidth),
                contentSize = new Vector2(100, 300),
                getName = (command, _) => command == Command.Copy ? "コピー" : "貼り付け",
                getEnabled = (command, _) => command == Command.Copy ? captured.canCopy : captured.canPaste,
                onSelected = (command, _) =>
                {
                    // ポップアップを開いたまま選択が変わり、この行が描かれなくなった場合は
                    // 古い対象に束縛されたコールバックを発火させない
                    if (captured.lastFrame < Time.frameCount - 1)
                    {
                        return;
                    }
                    var action = command == Command.Copy ? captured.onCopy : captured.onPaste;
                    if (action != null)
                    {
                        action();
                    }
                },
            };
            ownerEntries[key] = entry;
            return entry;
        }
    }
}
```

実装時の確認点:
- `GUIComboBox.DrawTextureButton` は `getTexture` が null でも `defaultTexture` があれば使う（`GUIComboBox.cs` の `DrawTextureButton` 冒頭）。アイコンの読み込みに失敗して `defaultTexture` が null になる環境では、テクスチャ無しのボタンになる。既存の `InspectorHeaderRowDrawer` と同じく、この場合のフォールバックは設けない
- `currentIndex = -1` のままポップアップを描くと、現在項目の強調が出ない（`DrawPopupContent` の `isCurrent` 判定）。ツールチップは出ない（`GUIComboBox` に引数が無い）

- [ ] **Step 4: Inspector で毎フレーム掃除する**

`InspectorWindow.DrawContent` の先頭（`_rootView.Init(...)` の前）に追加:

```csharp
            // 描かれなくなった項目メニューのボタンを捨てる
            ItemClipboardMenu.instance.BeginFrame();
```

- [ ] **Step 5: csproj へ追加してビルドする**

`<Compile Include="ItemClipboardMenu.cs" />` を追加する。

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 5: キーフレーム詳細のメニュー

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`（`DrawBlockHeader`）
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameBatchDrawer.cs`（`DrawGroupHeader`）

**Interfaces:**
- Consumes: `ItemValueClipboard.Set/Resolve/CanResolve`、`ItemClipboardMenu.instance.Draw`、`ItemClipboardMenu.GetReservedWidth`

- [ ] **Step 1: 個別ブロックのヘッダーにメニューを足す**

`KeyFrameInspector.DrawBlockHeader` のラベル幅計算を、メニュー 1 個ぶん詰める:

```csharp
            // 要素は 5 個 (マーク・名前・初期化・削除・メニュー) なので margin を 5 個ぶん引く
            var labelWidth = available
                - FoldMarkWidth - HeaderButtonWidth * 2 - ItemClipboardMenu.ButtonWidth
                - view.margin * 5;
```

「削除」ボタンの `if` ブロックの後に追加:

```csharp
                var transform = bone.transform;
                ItemClipboardMenu.instance.Draw(view, bone, null,
                    true,
                    ItemValueClipboard.CanResolve(bone.name, transform.type),
                    () => ItemValueClipboard.Set(
                        new[] { bone.name }, new MTEP.ITransformData[] { transform }),
                    () => PasteToBone(bone));
```

クラスに追加:

```csharp
        /// <summary>クリップボードの値をキーへ写す (値・文字列値・タンジェントを丸ごと)</summary>
        private void PasteToBone(MTEP.BoneData bone)
        {
            var source = ItemValueClipboard.Resolve(bone.name, bone.transform.type);
            if (source == null)
            {
                return;
            }

            bone.transform.FromTransformData(source);
            MTEUtils.LogDebug("キーフレームへ貼り付けます：" + bone.name);
            bone.parentLayer.ApplyCurrentFrame(true);
            timelineManager.RequestHistory("キーフレーム貼り付け: " + bone.name);
        }
```

- [ ] **Step 2: 一括モードのグループヘッダーにメニューを足す**

`KeyFrameBatchDrawer.DrawGroupHeader` のラベル幅も Step 1 と同じく `ItemClipboardMenu.ButtonWidth` と margin 1 個ぶんを追加で引く。「削除」の後に追加:

```csharp
                // どのキーを写すか決まらないので、グループからのコピーは受け付けない
                ItemClipboardMenu.instance.Draw(view, this, group.type.ToString(),
                    false,
                    CanPasteToGroup(group),
                    null,
                    () => PasteToGroup(group));
```

クラスに追加:

```csharp
        private static bool CanPasteToGroup(Group group)
        {
            foreach (var bone in group.bones)
            {
                if (ItemValueClipboard.CanResolve(bone.name, bone.transform.type))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>グループ内の各キーへ、当たる値を写す。履歴はまとめて 1 回</summary>
        private void PasteToGroup(Group group)
        {
            var pasted = false;
            foreach (var bone in group.bones)
            {
                var source = ItemValueClipboard.Resolve(bone.name, bone.transform.type);
                if (source == null)
                {
                    continue;
                }
                bone.transform.FromTransformData(source);
                pasted = true;
            }

            if (!pasted)
            {
                return;
            }
            MTEUtils.LogDebug("キーフレームへ一括で貼り付けます：" + group.type);
            Apply(group, "キーフレーム一括貼り付け: " + group.type);
        }
```

既存の `Apply(Group group)`（`KeyFrameBatchDrawer.cs` 末尾付近）は履歴の説明文を `"キーフレーム一括編集: " + group.type` に固定している。Undo 履歴で貼り付けと区別できるよう、説明文を受け取るオーバーロード `Apply(Group group, string description)` を足し、既存の `Apply(group)` はそれを `"キーフレーム一括編集: " + group.type` で呼ぶ形にする。`Group` の型名・`group.bones` の要素型は `KeyFrameBatchDrawer` の定義に合わせる。

- [ ] **Step 3: ビルドする**

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 6: レイヤー項目の共通ヘルパーと選択全体のメニュー

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemClipboard.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs`
- Modify: csproj（`Timeline\ItemInspector\TimelineItemInspector.cs` の次）

**Interfaces:**
- Consumes: `TimelineItemValues.Copy/CanPaste/Paste`、`ItemClipboardMenu`
- Produces:
  - `TimelineItemClipboard.DrawMenu(GUIView view, MTEP.ITimelineLayer layer, string itemName)`（1 項目のメニューを右端へ寄せて描く）
  - `TimelineItemClipboard.DrawMenu(GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items, string key)`（複数項目をまとめて扱うメニューを右端へ）
  - `TimelineItemClipboard.DrawHeading(GUIView view, string label, MTEP.ITimelineLayer layer, string itemName, Color? textColor = null)`（見出しラベル + 右端のメニューの 1 行）
  - `TimelineItemClipboard.DrawHeading(GUIView view, string label, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)`（選択全体を対象にする見出し行）

- [ ] **Step 1: ヘルパーを書く**

`Timeline/ItemInspector/TimelineItemClipboard.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤー項目の現在値 UI に置く、値のコピー / 貼り付けメニューの描画ヘルパー。
    /// 見出し行を持つ項目は見出しの右端、1 行で完結する項目は行の右端に置く
    /// </summary>
    public static class TimelineItemClipboard
    {
        private const float RowHeight = 20f;

        private static readonly List<string> _singleName = new List<string>(1);

        /// <summary>1 項目のメニューを行の右端へ寄せて描く (横並びの中で呼ぶ)</summary>
        public static void DrawMenu(GUIView view, MTEP.ITimelineLayer layer, string itemName)
        {
            _singleName.Clear();
            _singleName.Add(itemName);
            var canPaste = TimelineItemValues.CanPaste(layer, _singleName);

            ItemClipboardMenu.instance.DrawRightAligned(view, layer, itemName,
                true, canPaste,
                () => TimelineItemValues.Copy(layer, new[] { itemName }),
                () => TimelineItemValues.Paste(layer, new[] { itemName }));
        }

        /// <summary>複数項目をまとめて扱うメニューを行の右端へ寄せて描く (横並びの中で呼ぶ)</summary>
        public static void DrawMenu(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items, string key)
        {
            var names = new List<string>(items.Count);
            foreach (var item in items)
            {
                names.Add(item.name);
            }

            ItemClipboardMenu.instance.DrawRightAligned(view, layer, key,
                names.Count > 0, TimelineItemValues.CanPaste(layer, names),
                () => TimelineItemValues.Copy(layer, names),
                () => TimelineItemValues.Paste(layer, names));
        }

        /// <summary>見出しラベル + 右端のメニューの 1 行 (従来の DrawLabel(label, -1, 20) の置き換え)</summary>
        public static void DrawHeading(
            GUIView view, string label, MTEP.ITimelineLayer layer, string itemName,
            Color? textColor = null)
        {
            view.BeginHorizontal();
            {
                DrawHeadingLabel(view, label, textColor);
                DrawMenu(view, layer, itemName);
            }
            view.EndLayout();
        }

        /// <summary>選択全体を対象にする見出し行 (項目ごとの見出しを持たない表示向け)</summary>
        public static void DrawHeading(
            GUIView view, string label, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            view.BeginHorizontal();
            {
                DrawHeadingLabel(view, label, null);
                DrawMenu(view, layer, items, "*heading*");
            }
            view.EndLayout();
        }

        private static void DrawHeadingLabel(GUIView view, string label, Color? textColor)
        {
            var width = ItemClipboardMenu.GetRemainingWidth(view);
            if (textColor.HasValue)
            {
                view.DrawLabel(label, width, RowHeight, textColor: textColor.Value);
            }
            else
            {
                view.DrawLabel(label, width, RowHeight);
            }
        }
    }
}
```

`GUIView.DrawLabel` の `textColor` 引数の型（`Color` か `Color?` か）は `GUIView.cs:1220` の宣言に合わせる。`Color?` を受けるなら分岐は不要なので 1 行にまとめる。

- [ ] **Step 2: 選択全体のメニュー行を足す**

`TimelineItemInspector.Draw` の `inspector.DrawItems(view, layer, _leafItems);` の直前に追加:

```csharp
            // 選択中の全項目をまとめてコピー / 貼り付けする行
            view.BeginHorizontal();
            {
                view.DrawLabel(
                    string.Format("選択中の項目 ({0}件)", _leafItems.Count),
                    ItemClipboardMenu.GetRemainingWidth(view), 20f);
                TimelineItemClipboard.DrawMenu(view, layer, _leafItems, "*selection*");
            }
            view.EndLayout();
            view.DrawHorizontalLine();
```

- [ ] **Step 3: csproj へ追加してビルドする**

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 7: 見出しを持つ項目・見出しを足す項目

各ファイルで、項目ごとの見出しラベル（`view.DrawLabel(<見出し>, -1, RowHeight)`）を `TimelineItemClipboard.DrawHeading(view, <見出し>, layer, item.name)` に置き換える。色付きの見出しは `textColor` を引き継ぐ。行番号は調査時点の目安なので、実装時に該当行を読んでから置き換える。

**Files（置き換え）:**

| ファイル | 対象の行（目安） | 置き換え後 |
|---|---|---|
| `Timeline/ItemInspector/MotionItemInspector.cs` | :47 `view.DrawLabel(def.displayName, -1, RowHeight);` | `TimelineItemClipboard.DrawHeading(view, def.displayName, layer, item.name);` |
| `EyesItemInspector.cs` | :76 `DrawLabel(item.displayName, -1, ...)` | `DrawHeading(view, item.displayName, layer, item.name)` |
| `GravityItemInspector.cs` | :49 `DrawLabel(category.name, -1, ...)` | `DrawHeading(view, category.name, layer, item.name)` |
| `DressItemInspector.cs` | :92 `DrawLabel(displayName + ": " + file, -1, ..., 色)` | `DrawHeading(view, displayName + ": " + file, layer, item.name, 色)` |
| `SubCameraItemInspector.cs` | :42 | `DrawHeading(view, cameraData.displayName, layer, item.name)` |
| `LightItemInspector.cs` | :43 | `DrawHeading(view, stat.displayName, layer, item.name)` |
| `BGItemInspector.cs` | :49 | `DrawHeading(view, displayName + ": " + bg, layer, item.name)` |
| `BGColorItemInspector.cs` | 地面 :33 | 地面の見出しを `DrawHeading` に置換 |
| `TextItemInspector.cs` | :41 | `DrawHeading` に置換 |
| `StageLightItemInspector.cs` | :30 | `DrawHeading` に置換 |
| `StageLaserItemInspector.cs` | :30 | `DrawHeading` に置換 |
| `PsylliumItemInspector.cs` | :33 | `DrawHeading` に置換 |
| `PngPlacementItemInspector.cs` | :48 | `DrawHeading` に置換 |
| `PostEffectItemInspector.cs` | :32 | `DrawHeading` に置換 |
| `ModelBoneItemInspector.cs` | :44 `DrawLabel(model.displayName + "/" + item.displayName, -1, ...)` | `DrawHeading` に置換 |
| `ModelShapeKeyItemInspector.cs` | :37 `DrawLabel(model.displayName, -1, ...)` | `DrawHeading` に置換 |

「未対応」「見つかりません」などの灰色ラベル（実体の無い項目）は置き換えない。見出しの後に「見つかりません」を出している StageLight / StageLaser / PostEffect は、見出しにメニューが出ても `Copy` は「コピーできる値がありません」の警告で終わり、`CanPaste` は型だけで判定するので貼り付けも無害（`ApplyMotion` 側が実体の無さを無視する）。描画順は変えない。

**Files（見出しを足す。選択全体が対象）:**

| ファイル | 位置 | 追加するコード |
|---|---|---|
| `MoveItemInspector.cs` | :44 の `ObjectTransformRowDrawer.Draw` の前 | `TimelineItemClipboard.DrawHeading(view, items[0].displayName, layer, items);` |
| `CameraItemInspector.cs` | 手ブレ分岐（:28）の前と、本体の描画（:44）の前 | 各分岐の先頭で `TimelineItemClipboard.DrawHeading(view, items[0].displayName, layer, items);`（分岐ごとに 1 回だけ出す） |
| `VoiceItemInspector.cs` | :31 の `VoiceRowDrawer.Draw` の前 | 同上 |
| `SeItemInspector.cs` | :33 の `SeRowDrawer.Draw` の前 | 同上 |
| `BGColorItemInspector.cs` | 背景色 :28 の `DrawBgColorRow` の前 | `TimelineItemClipboard.DrawHeading(view, item.displayName, layer, item.name);`。:27 の「見出しを重ねない」コメントを「コピー / 貼り付けのメニューを置くため見出しを出す」に更新する |

- [ ] **Step 0: 現物の行を一括で確認する**

行番号は調査時点の目安なので、着手前にまとめて現物を確認し、表とずれている箇所を控える:

```bash
cd source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector
grep -n "DrawLabel(" MotionItemInspector.cs EyesItemInspector.cs GravityItemInspector.cs DressItemInspector.cs \
  SubCameraItemInspector.cs LightItemInspector.cs BGItemInspector.cs BGColorItemInspector.cs TextItemInspector.cs \
  StageLightItemInspector.cs StageLaserItemInspector.cs PsylliumItemInspector.cs PngPlacementItemInspector.cs \
  PostEffectItemInspector.cs ModelBoneItemInspector.cs ModelShapeKeyItemInspector.cs
```

- [ ] **Step 1: 置き換え表の 16 ファイルを編集する**
- [ ] **Step 2: 見出し追加表の 5 ファイルを編集する**
- [ ] **Step 3: ビルドする**

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 8: 行の右端・既存ヘッダーの右端に置く項目

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/FaceMorphRowDrawer.cs`、`Timeline/ItemInspector/MorphItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidShapeKeyRowDrawer.cs`、`Timeline/ItemInspector/ShapeKeyItemInspector.cs`
- Modify: `Timeline/ItemInspector/UndressItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorHeaderRowDrawer.cs`、`ModelManageRowDrawer.cs`、`Timeline/ItemInspector/BGModelItemInspector.cs`、`Timeline/ItemInspector/ModelTransformItemInspectorBase.cs`、`Timeline/ItemInspector/ModelItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs`、`Timeline/ItemInspector/MaterialItemInspectorBase.cs`
- Modify: `Timeline/TimelineLayer/AnimationTimelineLayer.cs`、`Timeline/ItemInspector/AnimationItemInspector.cs`

**Interfaces:**
- Consumes: `TimelineItemClipboard.DrawMenu(view, layer, itemName)`、`ItemClipboardMenu.GetReservedWidth`
- Produces: 各 drawer に省略可能な末尾引数 `Action<GUIView> drawTrailing = null`。null のときは従来と同じ描画（MaidFaceWindow 等の既存の呼び出し元は変更しない）

- [ ] **Step 1: 表情モーフ**

`FaceMorphRowDrawer.Draw` に `Action<GUIView> drawTrailing = null` を足す。`drawTrailing == null` なら従来の `DrawTrackedToggle` / `DrawTrackedSliderValue` をそのまま呼ぶ。非 null のときは、同じ行をヘルパーを使わずに展開して右端へメニューを描く（MTEUtils の `DrawTracked*` は変更しないため）:

```csharp
            if (drawTrailing != null)
            {
                view.BeginHorizontal();
                {
                    view.DrawToggle(isModified, GUIView.TrackedCheckWidth, rowHeight, onCheckChanged);
                    if (def.isToggle)
                    {
                        view.DrawToggle(def.displayName, value >= 0.5f, 130, rowHeight, onToggleChanged);
                    }
                    else
                    {
                        var option = sliderOption;
                        // 右端のメニューぶんスライダーを縮める (-1 だと右端まで伸びて重なる)
                        option.width = ItemClipboardMenu.GetRemainingWidth(view);
                        view.DrawSliderValue(option);
                    }
                    drawTrailing(view);
                }
                view.EndLayout();
                return;
            }
```

これに合わせて、既存のトグル用コールバックを `Action<bool> onToggleChanged`、スライダーの `GUIView.SliderOption` を `sliderOption` というローカル変数へ切り出し、従来経路と共有する。`DrawTrackedToggle` / `DrawTrackedSliderValue` の中身（`GUIView.cs:3132-3153`）と同じ要素・同じ順序であること。

`MorphItemInspector` の呼び出しを `FaceMorphRowDrawer.Draw(view, maid, def, LabelWidth, RowHeight, v => TimelineItemClipboard.DrawMenu(v, layer, item.name));` に変える。

強制上書きトグル（:28-37）も実在の項目なので、`view.BeginHorizontal()` で包み、トグルの後に `TimelineItemClipboard.DrawMenu(view, layer, item.name);` を置いて `view.EndLayout()` で閉じる。

- [ ] **Step 2: シェイプキー**

`MaidShapeKeyRowDrawer.Draw` に `Action<GUIView> drawTrailing = null` を足す。見出し相当の `view.DrawTrackedLabel(isModified, onCheckChanged, shapeKeyName, -1, rowHeight);` を、非 null のときだけ次に置き換える:

```csharp
            if (drawTrailing != null)
            {
                view.BeginHorizontal();
                {
                    view.DrawToggle(isModified, GUIView.TrackedCheckWidth, rowHeight, onCheckChanged);
                    view.DrawLabel(shapeKeyName, ItemClipboardMenu.GetRemainingWidth(view), rowHeight);
                    drawTrailing(view);
                }
                view.EndLayout();
            }
            else
            {
                view.DrawTrackedLabel(isModified, onCheckChanged, shapeKeyName, -1, rowHeight);
            }
```

`ShapeKeyItemInspector`（:39）の呼び出しに `v => TimelineItemClipboard.DrawMenu(v, layer, item.name)` を渡す。

- [ ] **Step 3: 脱衣**

`UndressItemInspector` の単一行トグル（:81 `view.DrawToggle(displayName, ..., ToggleWidth, ...)`）を `view.BeginHorizontal()` で包み、トグルの後に `TimelineItemClipboard.DrawMenu(view, layer, item.name);` を置いて `view.EndLayout()` で閉じる（`DrawMenu` が右端へ寄せるので空白の詰め物は要らない）。

- [ ] **Step 4: モデル・背景モデルのヘッダー**

`InspectorHeaderRowDrawer.Draw` に `Action<GUIView> drawTrailing = null` を足す。非 null のときはラベル幅からさらに `ItemClipboardMenu.GetReservedWidth(view)` を引き、フォーカスボタンの後に `drawTrailing(view)` を呼ぶ。`InspectorWindow.DrawHeader` の呼び出しは変えない。

`ModelTransformItemInspectorBase`:
- `DrawModelHeaderRow(GUIView view, TModel model)` を `DrawModelHeaderRow(GUIView view, TModel model, Action<GUIView> drawTrailing)` に変え、派生先（`ModelItemInspector` → `ModelManageRowDrawer.DrawHeaderRow`、`BGModelItemInspector`）から `InspectorHeaderRowDrawer.Draw(..., drawTrailing)` まで渡す。`ModelManageRowDrawer.DrawHeaderRow` にも同じ省略可能引数を足す
- `DrawModel(GUIView view, TModel model)` を `DrawModel(GUIView view, TModel model, MTEP.ITimelineLayer layer)` にし、`layer != null` のとき `v => TimelineItemClipboard.DrawMenu(v, layer, model.name)`、null のとき null を渡す
- `DrawItems` からは受け取った `layer` を渡す
- `TryDrawSelected` からは `FindOwnLayer()` の結果を渡す。オブジェクト選択時もタイムラインにこのモデルのレイヤーがあればメニューを出すため:

```csharp
        /// <summary>
        /// このプロバイダが担当するレイヤーを読み込み中のタイムラインから探す。
        /// Inspector のモデル本体選択はメニュー項目を経由しないため、型で逆引きする
        /// (InspectorWindow は登録済みとは別インスタンスを持つので、インスタンスではなく型で比べる)
        /// </summary>
        private MTEP.ITimelineLayer FindOwnLayer()
        {
            var timelineManager = MTEP.TimelineManager.instance;
            if (timelineManager.timeline == null)
            {
                return null;
            }
            foreach (var layer in timelineManager.layers)
            {
                var inspector = TimelineItemInspectorRegistry.Find(layer);
                if (inspector != null && inspector.GetType() == GetType())
                {
                    return layer;
                }
            }
            return null;
        }
```

- [ ] **Step 5: マテリアル**

`MaterialPropertyRowsDrawer.Draw` に `Action<GUIView> drawTrailing = null` を足し、private の `DrawNameRow` へ渡す。`DrawNameRow` は非 null のときラベル幅（:53-54）からさらに `ItemClipboardMenu.GetReservedWidth(view)` を引き、ペーストボタンの後に `drawTrailing(view)` を呼ぶ。`MaterialItemInspectorBase`（:42）から `v => TimelineItemClipboard.DrawMenu(v, layer, item.name)` を渡す。

マテリアル行の既存の「コピー」「ペースト」ボタン（`MaterialClipboard`）は、マテリアルのプロパティ全体をコピーする別機能なので残す。

- [ ] **Step 6: アニメレイヤー**

`AnimationTimelineLayer.DrawAnimeLayer(GUIView view, int layer)` に `Action<GUIView> drawTrailing = null` を足し、見出しの横並び（:303-318）の `EndLayout` の直前で `if (drawTrailing != null) drawTrailing(view);` を呼ぶ。見出しの要素は固定幅（段名 100・ループ 60・時間上書き 80）なので、`DrawMenu` の右寄せだけで重ならない。`AnimationItemInspector`（:46）から `v => TimelineItemClipboard.DrawMenu(v, layer, item.name)` を渡す（`layer` はメソッド引数の `MTEP.ITimelineLayer`。`DrawAnimeLayer` の第 2 引数の int と名前が衝突する場合は、呼び出し側のローカル名を合わせて読み替える）。

- [ ] **Step 7: ビルドする**

Run: ビルド + テスト
Expected: 両構成のビルド成功、全テスト PASS

---

### Task 9: 実機確認

実機検証は通常シーン（デイリー画面でエディタを有効にする）で行う。撮影モードは非対応。

DLL の反映は `com3d25-devbridge:restart-verify` スキルの手順に従う（ゲーム停止 → DLL 更新 → 起動 → セーブロード）。

- [ ] **Step 1: キーフレーム**
  - 同じ型の 2 つのキー（例: メイドの Spine と Neck の回転キー）で、コピー → 貼り付けすると値が写る
  - 型違いのキー（例: 表情キー）では「貼り付け」が無効表示になる
  - 複数ブロックを並べても、各ボタンのポップアップがそのボタンの直下に出る
  - 一括モードのグループで「コピー」が無効、「貼り付け」で全キーへ写る
  - Undo（タイムライン履歴）で戻る
- [ ] **Step 2: レイヤー項目（既定の直接適用）**
  - Camera / Light / Model（オブジェクト選択時のヘッダー含む）/ ModelBone / PngPlacement で、キーのある項目へ貼っても次のフレームで元に戻らない
- [ ] **Step 3: レイヤー項目（後処理・個別実装）**
  - Morph（スライダー行の右端、強制上書きトグル）、ShapeKey、Dress、StageLight、Psyllium で見た目に反映される
  - Motion の体ボーン（回転）、Root（位置 + 回転）、ExtendBone（回転を含む）で反映される
  - ブレンドレイヤー調整中の Motion では書き込まれず、警告ログが出る
- [ ] **Step 4: 履歴・自動キー**
  - 自動キーフレーム登録 ON で貼り付けると、その項目のキーが登録される
- [ ] **Step 5: 対象外・副作用**
  - Voice / SE の「貼り付け」が無効表示（コピーはできる）
  - Move レイヤーなしで編集モード中に Motion の項目をコピーしても、メイドの位置がずれない
- [ ] **Step 6: 問題があれば修正し、ビルド + テストをやり直す**

完了後、code-review スキルでレビュー → commit スキルでコミットする。

## レビュー却下メモ

- Motion レイヤーのコピー時に `maid.transform` が動く副作用へのコード緩和 — `UpdateFrame` の分岐は `maid.transform` を編集開始位置へ戻したあと、ルートボーンのワールド位置・回転を元に戻すため、見た目の姿勢は変わらない（MotionTimelineLayer.cs の `initialEditFrame != null && !HasMoveLayer()` 分岐）。キーフレーム登録と同じ経路でもあるので、緩和コードは入れず Task 9 Step 5 の実機確認で見る
- `GetRemainingWidth` の margin 二重減算 — 末尾の margin は既存の `InspectorHeaderRowDrawer` / `DrawSlotBoneHeader` と同じ意図的な余白で、ボタンは右寄せで配置するため重ならない。既存の見た目と揃えるため現状維持
