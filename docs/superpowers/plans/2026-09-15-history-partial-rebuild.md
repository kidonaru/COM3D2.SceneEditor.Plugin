# 操作履歴 Undo/Redo の部分再構築 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインの Undo/Redo（`TimelineHistoryEntry` → `TimelineManager.UpdateTimeline`）が全レイヤー＋モデル＋動画を作り直しているのをやめ、キーフレームが変わったレイヤーだけ再構築する。

**Architecture:** `TimelineHistoryManager.AddHistory` の時点で before/after の `TimelineXml` を Unity 非依存の `TimelineXmlDiff` で比較し、「レイヤー外の設定・構成が同一で、キーフレームだけ違うレイヤーの添字集合」を `TimelineHistoryEntry` に持たせる。Undo/Redo は差分が部分適用可能なら `TimelineManager.UpdateTimelineLayers(xml, indices)` で該当レイヤーだけ `Dispose → FromXml → Init → CreateAndApplyAnm` し、それ以外（レイヤー追加削除・設定変更・モデル/ライト/動画等の変更）は従来の `UpdateTimeline(xml)` へフォールバックする。レイヤー外項目の同一性は `layers` を空にして XML シリアライズした文字列比較で判定し、`TimelineXml` にフィールドが増えても比較漏れが起きないようにする（比較対象は常に `TimelineData.ToXml()` が生成する実行時スナップショット同士なので、`ShouldSerialize*` で常時抑止される旧フォーマット互換フィールドは両側とも除外され、判定に影響しない）。

部分適用の前提「現在のタイムラインが対になる側の XML と同一」は、`TimelineHistoryManager.lastCommittedXml` との**参照一致**で毎回検査する。`AddHistory` は前エントリの after をそのまま次エントリの before に使い、`Restore` は適用成功後に `lastCommittedXml` を適用した XML に置き換えるため、「Undo なら `lastCommittedXml == _after`、Redo なら `lastCommittedXml == _before`」が成り立つときだけ隣接適用だと保証できる。スキップされたエントリ・適用失敗・`RestoreTo` の多段ジャンプで前提が崩れた場合は参照が一致しないので全再構築へ倒れる。部分再構築の途中で例外が出た場合も、中間状態を残さないよう全再構築で復旧する。

**Tech Stack:** C# (COM3D2 構成は .NET 3.5 相当の制約あり)、Unity、xunit (`source/COM3D2.SceneEditor.Plugin.Tests`)。

**Spec:** 本セッションの実機計測（17 レイヤー・2496 キー、Undo 1 回 270ms / 74MB alloc）:

| 処理 | 時間 |
|---|---|
| `TimelineData.FromXml`（全レイヤー） | 50ms / 29MB |
| `MotionTimelineEditor.OnLoad` | 88ms（`StudioModelManager.SetupModels` 66ms、`MovieManager.ReloadMovie` 9ms、StageLaser 8ms） |
| `TimelineData.LayerInit` | 6ms |
| `CreateAndApplyAnmAll` | 125ms / 42MB（Motion 50ms、ModelMaterial 12ms、StageLight/Laser/Psyllium 各 6ms） |
| 登録側 `TimelineData.ToXml`（編集ごと） | 24ms |

根本原因: `TimelineHistoryEntry` が before/after の XML 丸ごとしか持たず、`UpdateTimeline` が無条件に全再構築するため。

## Global Constraints

- COM3D2 構成（.NET 3.5）と COM3D25 構成（.NET 4.7.1）の両方でビルドが通ること。入力 5 個以上の `Func<>`/`Action<>`・`Tuple`・`CallerMemberName` は使わない
- 既存の全再構築経路 `TimelineManager.UpdateTimeline(TimelineXml)` は無変更で残す（フォールバック先）
- レイヤーの `Init()` はイベント購読、`Dispose()` は購読解除を行う（例: `LightTimelineLayer`）。部分再構築で同一インスタンスを再初期化するときは必ず `Dispose()` を先に呼ぶ
- テストは Unity 非依存の POCO（`TimelineXml` 系 DTO と `TimelineXmlDiff`）に限る。`TimelineManager` / `TimelineHistoryEntry` の適用経路は実機で検証する
- コードのコメントは日本語。main csproj は `<Compile Include>` 明示列挙なので新規ファイルを追加すること（テスト csproj は SDK 形式で自動）
- ビルドは `debug.bat` ではなく MSBuild 直叩き（Git Bash では `export MSYS2_ARG_CONV_EXCL="*"`）。順序は COM3D2 → COM3D25 → `dotnet test`

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" /v:m /nologo
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo
cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test --nologo -v q
```

---

## File Structure

| ファイル | 責務 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXmlDiff.cs`（新規） | before/after `TimelineXml` の比較。`canApplyPartially` と `changedLayerIndices` を返す。Unity 非依存 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineHistoryEntry.cs` | 差分を保持し、部分適用可能なら `UpdateTimelineLayers` を呼ぶ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs` | `AddHistory` で差分を計算してエントリへ渡す |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | `UpdateTimelineLayers(TimelineXml, IList<int>)` を追加 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | `Timeline\TimelineXmlDiff.cs` を Compile に追加 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlDiffTests.cs`（新規） | 差分判定のテスト |

---

### Task 1: `TimelineXmlDiff` の差分判定を実装する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXmlDiff.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:587`（`Timeline\TimelineXml.cs` の直後に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlDiffTests.cs`

**Interfaces:**
- Consumes: `TimelineXml.layers : List<TimelineLayerXml>`、`TimelineLayerXml { className, slotNo, keyFrames : List<FrameXml> }`、`FrameXml { frameNo, bones : List<BoneXml> }`、`BoneXml.transform : TransformXml`、`TransformXml { name, type, values, inTangents, outTangents, inSmoothBit, outSmoothBit, strValues }`
- Produces:
  - `public class TimelineXmlDiff { public bool canApplyPartially; public List<int> changedLayerIndices; }`
  - `public static TimelineXmlDiff Compute(TimelineXml before, TimelineXml after)`
  - `public static bool LayerEquals(TimelineLayerXml a, TimelineLayerXml b)`（className / slotNo / keyFrames の完全一致）
  - `internal static string SerializeWithoutLayers(TimelineXml xml)`

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlDiffTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineXmlDiffTests
    {
        private static TransformXml CreateTransform(string name, float x)
        {
            return new TransformXml
            {
                name = name,
                type = TransformType.Move,
                values = new float[] { x, 0f, 0f },
            };
        }

        private static FrameXml CreateFrame(int frameNo, float x)
        {
            return new FrameXml
            {
                frameNo = frameNo,
                bones = new List<BoneXml>
                {
                    new BoneXml { transform = CreateTransform("bone", x) },
                },
            };
        }

        private static TimelineLayerXml CreateLayer(string className, int slotNo, float x)
        {
            return new TimelineLayerXml
            {
                className = className,
                slotNo = slotNo,
                keyFrames = new List<FrameXml> { CreateFrame(0, x), CreateFrame(10, x) },
            };
        }

        private static TimelineXml CreateTimeline()
        {
            var xml = new TimelineXml { maxFrameNo = 100, frameRate = 30f, anmName = "test" };
            xml.layers.Add(CreateLayer("MotionTimelineLayer", 0, 1f));
            xml.layers.Add(CreateLayer("CameraTimelineLayer", 0, 2f));
            xml.layers.Add(CreateLayer("LightTimelineLayer", 0, 3f));
            xml.models.Add(new TimelineModelXml { name = "model1" });
            return xml;
        }

        [Fact]
        public void 同一内容なら部分適用可能で変更レイヤーは無い()
        {
            var diff = TimelineXmlDiff.Compute(CreateTimeline(), CreateTimeline());

            Assert.True(diff.canApplyPartially);
            Assert.Empty(diff.changedLayerIndices);
        }

        [Fact]
        public void キー値が変わったレイヤーだけ添字に載る()
        {
            var after = CreateTimeline();
            after.layers[1].keyFrames[1].bones[0].transform.values[0] = 99f;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 1 }, diff.changedLayerIndices);
        }

        [Fact]
        public void キー追加も変更レイヤーとして検出する()
        {
            var after = CreateTimeline();
            after.layers[2].keyFrames.Add(CreateFrame(20, 3f));

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 2 }, diff.changedLayerIndices);
        }

        [Fact]
        public void タンジェントの有無の違いを検出する()
        {
            var after = CreateTimeline();
            after.layers[0].keyFrames[0].bones[0].transform.inTangents = new float[] { 0f, 0f, 0f };

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 0 }, diff.changedLayerIndices);
        }

        [Fact]
        public void レイヤー追加は部分適用不可()
        {
            var after = CreateTimeline();
            after.layers.Add(CreateLayer("MoveTimelineLayer", 0, 4f));

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void レイヤーの並び替えは部分適用不可()
        {
            var after = CreateTimeline();
            var first = after.layers[0];
            after.layers[0] = after.layers[1];
            after.layers[1] = first;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void レイヤー外の設定変更は部分適用不可()
        {
            var after = CreateTimeline();
            after.maxFrameNo = 200;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void モデル一覧の変更は部分適用不可()
        {
            var after = CreateTimeline();
            after.models.Add(new TimelineModelXml { name = "model2" });

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void どちらかがnullなら部分適用不可()
        {
            Assert.False(TimelineXmlDiff.Compute(null, CreateTimeline()).canApplyPartially);
            Assert.False(TimelineXmlDiff.Compute(CreateTimeline(), null).canApplyPartially);
        }

        [Fact]
        public void SerializeWithoutLayersは呼び出し後もlayersを保つ()
        {
            var xml = CreateTimeline();

            var text = TimelineXmlDiff.SerializeWithoutLayers(xml);

            Assert.Equal(3, xml.layers.Count);
            Assert.DoesNotContain("<Frame>", text);
            Assert.Contains("model1", text);
        }
    }
}
```

`internal` メンバーをテストから参照するため、main プロジェクトに `InternalsVisibleTo` があるか確認する。無ければ `SerializeWithoutLayers` は `public` にする。

- [ ] **Step 2: テストが失敗することを確認する**

Run（Global Constraints のビルド 3 行を実行）
Expected: `TimelineXmlDiff` が未定義でテストプロジェクトのビルドが失敗する

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXmlDiff.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 履歴エントリの before/after (TimelineXml) の差分。
    /// Undo/Redo でタイムライン全体を作り直さず、キーフレームが変わった
    /// レイヤーだけ再構築できるかを判定する
    /// </summary>
    public class TimelineXmlDiff
    {
        /// <summary>
        /// 変更レイヤーの差し替えだけで復元できるか。
        /// false のときはレイヤー構成かレイヤー外の設定が違うので全再構築が必要
        /// </summary>
        public bool canApplyPartially;

        /// <summary>キーフレームに差があるレイヤーの添字 (before/after で共通)</summary>
        public List<int> changedLayerIndices = new List<int>();

        // layers を空にしてシリアライズするときの差し替え用
        private static readonly List<TimelineLayerXml> EmptyLayers = new List<TimelineLayerXml>();

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(TimelineXml));

        public static TimelineXmlDiff Compute(TimelineXml before, TimelineXml after)
        {
            var diff = new TimelineXmlDiff();

            if (before == null || after == null)
            {
                return diff;
            }

            // レイヤー数・型・スロットが 1 つでも違えば構成変更 (追加/削除/並び替え)
            if (before.layers.Count != after.layers.Count)
            {
                return diff;
            }
            for (var i = 0; i < before.layers.Count; i++)
            {
                if (before.layers[i].className != after.layers[i].className ||
                    before.layers[i].slotNo != after.layers[i].slotNo)
                {
                    return diff;
                }
            }

            // レイヤー外の項目 (モデル・ライト・動画・各種設定) は項目数が多く今後も増えるため、
            // 個別比較ではなく layers を除いた XML 文字列の一致で判定する
            if (SerializeWithoutLayers(before) != SerializeWithoutLayers(after))
            {
                return diff;
            }

            for (var i = 0; i < before.layers.Count; i++)
            {
                if (!LayerEquals(before.layers[i], after.layers[i]))
                {
                    diff.changedLayerIndices.Add(i);
                }
            }

            diff.canApplyPartially = true;
            return diff;
        }

        public static bool LayerEquals(TimelineLayerXml a, TimelineLayerXml b)
        {
            if (a.className != b.className || a.slotNo != b.slotNo)
            {
                return false;
            }
            if (a.keyFrames.Count != b.keyFrames.Count)
            {
                return false;
            }
            for (var i = 0; i < a.keyFrames.Count; i++)
            {
                if (!FrameEquals(a.keyFrames[i], b.keyFrames[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FrameEquals(FrameXml a, FrameXml b)
        {
            if (a.frameNo != b.frameNo)
            {
                return false;
            }
            var aBones = a.bones ?? new List<BoneXml>();
            var bBones = b.bones ?? new List<BoneXml>();
            if (aBones.Count != bBones.Count)
            {
                return false;
            }
            for (var i = 0; i < aBones.Count; i++)
            {
                if (!TransformEquals(aBones[i].transform, bBones[i].transform))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TransformEquals(TransformXml a, TransformXml b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.name == b.name
                && a.type == b.type
                && a.inSmoothBit == b.inSmoothBit
                && a.outSmoothBit == b.outSmoothBit
                && FloatArrayEquals(a.values, b.values)
                && FloatArrayEquals(a.inTangents, b.inTangents)
                && FloatArrayEquals(a.outTangents, b.outTangents)
                && StringArrayEquals(a.strValues, b.strValues);
        }

        private static bool FloatArrayEquals(float[] a, float[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StringArrayEquals(string[] a, string[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// layers を除いた XML 文字列。レイヤー外の項目の同一性判定に使う。
        /// シリアライズ中だけ layers を空リストに差し替え、必ず元へ戻す
        /// </summary>
        public static string SerializeWithoutLayers(TimelineXml xml)
        {
            var layers = xml.layers;
            xml.layers = EmptyLayers;
            try
            {
                using (var writer = new StringWriter())
                {
                    Serializer.Serialize(writer, xml);
                    return writer.ToString();
                }
            }
            finally
            {
                xml.layers = layers;
            }
        }
    }
}
```

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\TimelineXml.cs" />` の次行に追加:

```xml
    <Compile Include="Timeline\TimelineXmlDiff.cs" />
```

- [ ] **Step 4: テストが通ることを確認する**

Run（Global Constraints のビルド 3 行）
Expected: 2 構成ともビルド成功、`TimelineXmlDiffTests` 10 件 PASS、既存テストも PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXmlDiff.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlDiffTests.cs
git commit -m "feat(history): 履歴エントリの before/after XML から変更レイヤーを求める TimelineXmlDiff を追加"
```

---

### Task 2: `TimelineManager.UpdateTimelineLayers` で変更レイヤーだけ再構築する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:507-531`（`UpdateTimeline` の直後に追加）

**Interfaces:**
- Consumes: `ITimelineLayer.Dispose()` / `FromXml(TimelineLayerXml)` / `Init()` / `CreateAndApplyAnm()`、`TimelineData.layers`、`initialEditFrame`、`OnPoseEditUpdated()`、`UnselectAll()`、`Refresh()`
- Produces: `public void UpdateTimelineLayers(TimelineXml xml, IList<int> layerIndices)`

- [ ] **Step 1: 実装する**

`UpdateTimeline(TimelineXml xml)` の直後に追加:

```csharp
        /// <summary>
        /// Undo/Redo 用の部分再構築。レイヤー構成とレイヤー外の設定が現在と同一である
        /// 前提で、指定添字のレイヤーだけをキーフレームごと作り直す。
        /// 前提が崩れている場合 (添字範囲外・型不一致) は UpdateTimeline へ倒す
        /// </summary>
        public void UpdateTimelineLayers(TimelineXml xml, IList<int> layerIndices)
        {
            if (timeline == null || xml.layers.Count != layers.Count)
            {
                UpdateTimeline(xml);
                return;
            }

            foreach (var index in layerIndices)
            {
                if (index < 0 || index >= layers.Count ||
                    layers[index].layerName != xml.layers[index].className ||
                    layers[index].slotNo != xml.layers[index].slotNo)
                {
                    MTEUtils.LogWarning("履歴の部分適用の前提が崩れたため全再構築します index={0}", index);
                    UpdateTimeline(xml);
                    return;
                }
            }

            // 選択中のボーンは差し替え前の FrameData を指すため、全再構築時と同様に解除する
            UnselectAll();

            try
            {
                foreach (var index in layerIndices)
                {
                    var layer = layers[index];
                    // Init がイベント購読を行うレイヤーがあるため、同一インスタンスの再初期化では
                    // 必ず Dispose (購読解除) を先に通す
                    layer.Dispose();
                    layer.FromXml(xml.layers[index]);
                    layer.Init();
                    layer.CreateAndApplyAnm();
                }
            }
            catch (Exception e)
            {
                // 途中まで差し替えた中間状態を残すと以後の履歴適用の前提が崩れるため、
                // 全再構築で xml の状態へ揃え直す
                MTEUtils.LogException(e);
                MTEUtils.LogWarning("履歴の部分適用に失敗したため全再構築します");
                UpdateTimeline(xml);
                return;
            }

            if (initialEditFrame != null)
            {
                OnPoseEditUpdated();
            }

            Refresh();
        }
```

`TimelineManager.cs` 先頭に `using System;` が無ければ追加する（`Exception` 参照のため）。

- [ ] **Step 2: ビルドが通ることを確認する**

Run（Global Constraints のビルド 3 行）
Expected: 2 構成ともビルド成功、既存テスト PASS

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "feat(history): 指定レイヤーだけ再構築する TimelineManager.UpdateTimelineLayers を追加"
```

---

### Task 3: 履歴エントリに差分を持たせ、部分適用へ切り替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineHistoryEntry.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs:41-56`

**Interfaces:**
- Consumes: `TimelineXmlDiff.Compute(before, after)`、`TimelineManager.UpdateTimelineLayers(xml, indices)`
- Produces: `TimelineHistoryEntry(TimelineXml before, TimelineXml after, TimelineXmlDiff diff, string description)`（既存 3 引数コンストラクタは呼び出し元が `AddHistory` だけなので削除する）

- [ ] **Step 1: `TimelineHistoryEntry` を書き換える**

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作 1 件の履歴エントリ。
    /// タイムラインの状態は IStateSnapshot で表現できないため、
    /// TimelineXml の前後スナップショットを対で持ち SE の履歴スタックへ参加する。
    /// 前後の差分 (変更レイヤー) も持ち、可能なら該当レイヤーだけ再構築する
    /// </summary>
    public class TimelineHistoryEntry : IHistoryEntry
    {
        public string description { get; set; }

        private readonly MTEP.TimelineXml _before;
        private readonly MTEP.TimelineXml _after;
        private readonly MTEP.TimelineXmlDiff _diff;

        public TimelineHistoryEntry(
            MTEP.TimelineXml before,
            MTEP.TimelineXml after,
            MTEP.TimelineXmlDiff diff,
            string description)
        {
            _before = before;
            _after = after;
            _diff = diff;
            this.description = description;
        }

        // タイムラインが閉じられている間は適用できない (エントリはスキップされる)
        public bool canApply => MTEP.TimelineManager.instance.timeline != null;

        public void ApplyBefore()
        {
            Restore(_before, _after);
        }

        public void ApplyAfter()
        {
            Restore(_after, _before);
        }

        /// <param name="xml">復元する側</param>
        /// <param name="paired">対になる側。現在のタイムラインがこれと同一のときだけ部分適用できる</param>
        private void Restore(MTEP.TimelineXml xml, MTEP.TimelineXml paired)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            var historyManager = MTEP.TimelineHistoryManager.instance;

            // 部分適用は「現在のタイムラインが paired と同一」が前提。AddHistory は前エントリの
            // after をそのまま次の before に使い、ここは適用後に lastCommittedXml を置き換えるため、
            // 隣接エントリを順に辿っている限り参照が一致する。エントリのスキップ・適用失敗・
            // RestoreTo の多段ジャンプで前提が崩れると一致しないので全再構築へ倒す
            if (_diff != null && _diff.canApplyPartially &&
                ReferenceEquals(historyManager.lastCommittedXml, paired))
            {
                timelineManager.UpdateTimelineLayers(xml, _diff.changedLayerIndices);
            }
            else
            {
                timelineManager.UpdateTimeline(xml);
            }
            // 次の編集の before がこの復元後状態を指すよう、確定済みスナップショットを更新する
            historyManager.lastCommittedXml = xml;
        }
    }
}
```

- [ ] **Step 2: `TimelineHistoryManager.AddHistory` で差分を計算して渡す**

```csharp
        public void AddHistory(TimelineData timeline, string description)
        {
            var beforeXml = lastCommittedXml;
            var afterXml = timeline.ToXml();

            // 直前スナップショットが無い場合 (新規作成・読み込み直後) は
            // それ以前へ戻る意味がないため積まない
            if (beforeXml != null)
            {
                // 差分は登録時に 1 度だけ求め、Undo/Redo の両方向で使い回す
                var diff = TimelineXmlDiff.Compute(beforeXml, afterXml);
                SceneEditor.Plugin.HistoryManager.instance.AddEntry(
                    new SceneEditor.Plugin.TimelineHistoryEntry(beforeXml, afterXml, diff, description));
            }

            lastCommittedXml = afterXml;
        }
```

- [ ] **Step 3: ビルドとテストが通ることを確認する**

Run（Global Constraints のビルド 3 行）
Expected: 2 構成ともビルド成功、全テスト PASS

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineHistoryEntry.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs
git commit -m "perf(history): Undo/Redo でキーフレームが変わったレイヤーだけ再構築する"
```

---

### Task 4: 実機で効果と正しさを検証する

**Files:** なし（検証のみ）

**Interfaces:**
- Consumes: MCP `com3d25-devbridge` の `profile_add` / `profile_read` / `eval_csharp`

- [ ] **Step 1: 新 DLL を実機へ反映する**

ゲームを終了した状態で `debug.bat com3d25` を実行するか、`bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll` を `W:\COM3D2_5\Sybaris\UnityInjector\` へコピーし、ゲームを起動してタイムライン（17 レイヤー規模のもの）を読み込む。プラグインは MonoBehaviour を含むためホットリロードは不可。

- [ ] **Step 2: キーフレーム編集の Undo/Redo を計測する**

1 レイヤー（例: カメラ）のキーを 1 つ登録し、devbridge で以下を測る:

```
profile_add target="COM3D2.MotionTimelineEditor.Plugin.TimelineManager:UpdateTimelineLayers" frames=900
profile_add target="COM3D2.MotionTimelineEditor.Plugin.TimelineManager:UpdateTimeline" frames=900
profile_add target="COM3D2.MotionTimelineEditor.Plugin.MotionTimelineEditor:OnLoad" frames=900
```

続けて `eval_csharp` で `HistoryManager.instance.Undo()` / `Redo()` をリフレクション経由で呼ぶ（`devbridge-plugin-reflection` memory 参照）。

Expected:
- `UpdateTimelineLayers` が 1 回呼ばれ、1 レイヤー分（カメラなら数 ms）で完了する
- `UpdateTimeline` と `OnLoad` は 0 回
- 画面上で該当キーが消える／戻る

- [ ] **Step 3: 全再構築へのフォールバックを確認する**

レイヤーを 1 つ追加してから Undo する。
Expected: `UpdateTimeline` が 1 回呼ばれ、追加したレイヤーが消える（従来と同じ挙動）

- [ ] **Step 4: モーションレイヤーの Undo で他レイヤーが再構築されないことを確認する**

ポーズ編集でキーを登録して Undo する。
Expected: `TimelineLayerBase:CreateAndApplyAnm` の呼び出しが 1 回（モーションのみ）、所要 50ms 前後

- [ ] **Step 5: 前提崩れ時のフォールバックを確認する**

キー登録を 2 回行い、履歴ウィンドウから 2 つ前のエントリへ直接ジャンプ（`RestoreTo`）する。
Expected: 1 段目は `UpdateTimelineLayers`、2 段目も `UpdateTimelineLayers` が呼ばれる（隣接適用なので参照一致が続く）。次に `eval_csharp` で `TimelineHistoryManager.instance.lastCommittedXml = null` にしてから Undo する。
Expected: `UpdateTimeline`（全再構築）が 1 回呼ばれる

- [ ] **Step 6: 計測結果を memory の `undo-redo-cost-breakdown.md` に追記する**

Before/After の数値を「**How to apply:**」の下に 2〜3 行で追記する。

---

## レビュー却下メモ

- `TimelineData.ToXml` が `Dictionary` を列挙するため `SerializeWithoutLayers` の文字列比較が列挙順に左右される — 誤検知として却下。before/after は同一の Dictionary インスタンスから連続して生成され、間に変更が無ければ列挙順は同じ。削除→再追加で順序が変わった場合も全再構築へ倒れるだけで正しさは損なわれない
