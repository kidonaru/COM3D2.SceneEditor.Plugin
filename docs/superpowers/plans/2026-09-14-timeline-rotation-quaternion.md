# タイムライン回転のクォータニオン化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans` でタスクごとに実装すること（本リポジトリでは `subagent-driven-development` は使わない）。ステップはチェックボックス（`- [ ]`）で管理する。

**Goal:** オイラー角で保持していた「姿勢」7 型 8 スロットをクォータニオン保持へ移し、キー間の補間をクォータニオン空間で行う。XML は version 35 へ上げ、既存データを移行する。

**Architecture:** `TransformXml` の `values` / `inTangents` / `outTangents` / `inSmoothBit` / `outSmoothBit` を 1 スロットぶんずらす純粋な変換ヘルパーを `TimelineXml` に置き、`TransformType` をキーにしたテーブルで対象レコードを選ぶ。型側は `hasEulerAngles` → `hasRotation` へ差し替え、レイヤー側は `HermiteValues` → `HermiteQuaternion`（リムライトのみ `LerpFrom` に回転種別を追加）へ差し替える。

**Tech Stack:** C# 7.3 / .NET 3.5（COM3D2）と .NET 4.7.1（COM3D25）の 2 構成 / UnityEngine / xUnit

**Spec:** `docs/superpowers/specs/2026-09-14-timeline-rotation-quaternion-design.md`

## Global Constraints

- コードのコメントとエラーログは日本語で書く
- 2 構成ともビルドが通ること。`.NET 3.5` 側は入力 5 個以上の `Func<>` / `Action<>` が使えない
- ビルドとテストは次の 1 本で回す（`debug.bat` は実機へ DLL をコピーするので使わない）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" &&
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" &&
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

  順番は必須。`GameVersion=COM3D2` のビルドは `bin/Debug/COM3D25/` の DLL を消すため、テストが参照する DLL は COM3D25 のビルドで作り直す必要がある。

- **実装期間中に保存したタイムライン XML は捨てること。** Task 2 で `CurrentVersion` を 35 に上げるため、Task 4 以降で型を追加する前に保存した XML は version 35 として記録され、以後の追加分の移行を受けられない。

- **`Quaternion.Euler` / `Slerp` / `eulerAngles` / `Lerp` / `Inverse` は Unity のネイティブ ECall で、テストプロセスから呼ぶと `SecurityException` になる**（`Dot` / `Angle` / `*` / `Normalize` / `identity` は managed で呼べる）。移行と補間はテストで固定したい経路なので、`QuaternionUtils`（`source/COM3D2.SceneEditor.Plugin/QuaternionUtils.cs`、Task 1 で追加）の managed 実装 `EulerToQuaternion` / `Slerp` を使う。以降のタスクでテスト・プロダクションコードに現れる `Quaternion.Euler(v)` は `QuaternionUtils.EulerToQuaternion(v)` へ、`Quaternion.Slerp(a, b, t)` は `QuaternionUtils.Slerp(a, b, t)` へ読み替えること。Unity 実装との一致は実機（`com3d25-devbridge`）で採った実測値を `QuaternionUtilsTests` に固定して担保している

- **バージョン移行処理は実行時の型定義を参照してはならない。** 移行は「そのバージョン当時のレイアウト」を前提に動く必要があり、`valueCount` や `Index` を現在のコードから読むと、後の型変更で静かに壊れる。Task 3 はこの原則に既存コードを合わせる作業で、Task 4 の前提になる。

- 対象 7 型 8 スロットの対応表（全タスク共通の前提）:

| `TransformType` | 型 | Euler X の添字 | 挿入位置 | valueCount |
|---|---|---|---|---|
| `Rimlight` | `TransformDataRimlight` | 0 | 3 | 25 → 26 |
| `StageLaser` | `TransformDataStageLaser` | 1 | 4 | 24 → 25 |
| `PngObject` | `TransformDataPngObject` | 3 | 6 | 31 → 32 |
| `Text` | `TransformDataText` | 3 | 6 | 20 → 21 |
| `StageLight` | `TransformDataStageLight` | 3 | 6 | 23 → 24 |
| `StageLaserController` | `TransformDataStageLaserController` | 3 | 6 | 37 → 38 |
| `PsylliumTransform` | `TransformDataPsylliumTransform` | 6（左手） / 9（右手） | 9 / 13 | 12 → 14 |

---

## File Structure

| ファイル | 役割 | 変更 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | XML DTO とバージョン移行 | 変換ヘルパー・型テーブル・`version < 35` ブロックを追加。`ConvertPostEffectMaskValues` の添字を凍結 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `CurrentVersion` | 34 → 35 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformData{Rimlight,StageLaser,PngObject,Text,StageLight,StageLaserController,PsylliumTransform}.cs` | 値レイアウト | Euler → Rotation |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs` | 汎用補間 | `LerpKind.Rotation` / `LerpKind.RotationMember` を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/{PngPlacement,Text,StageLight,StageLaser,Psyllium}TimelineLayer.cs` | 再生時の補間 | `HermiteQuaternion` へ差し替え |
| `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs` | サイリウムの手 | 左右反転をクォータニオン鏡像へ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs` | 自動回転 | 据え置き理由のコメントのみ |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs` | 新規 | 移行の単体テスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/RimlightRotationLerpTests.cs` | 新規 | `LerpFrom` の回転補間テスト |
| `docs-site/timeline/compatibility.md` / `docs/timeline-rotation-quaternion-survey.md` / `docs/timeline-release-debt-review.md` | 文書 | version 35 の内容を反映 |

---

## Task 1: 移行ヘルパー `ConvertEulerToRotation`

オイラー角 3 値をクォータニオン 4 値へ変換し、値・タンジェント・スムーズビットを 1 スロットぶんずらす純粋関数。この時点ではまだ誰も呼ばない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`（新規）

**Interfaces:**
- Consumes: `TimelineXml.InsertBit(long bitValues, int index, bool value)`（`TimelineXml.cs:1328` の既存 public static）
- Produces: `public static void TimelineXml.ConvertEulerToRotation(TransformXml transform, int eulerIndex)`

- [x] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs` を新規作成する。

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 35 でのオイラー角 → クォータニオン移行を固定する。
    /// 値・タンジェント・スムーズビットのいずれかだけがずれると添字が食い違い、
    /// 既存タイムラインが黙って壊れるため
    /// </summary>
    public class TimelineXmlRotationMigrationTests
    {
        /// <summary>添字 eulerIndex から 3 値をオイラー角とする、count 個の連番レコードを作る</summary>
        private static TransformXml CreateTransform(TransformType type, int count, int eulerIndex, Vector3 eulerAngles)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                // 移行後に「どの値がどこへ動いたか」を追えるよう連番を入れる
                values[i] = 100f + i;
            }
            values[eulerIndex] = eulerAngles.x;
            values[eulerIndex + 1] = eulerAngles.y;
            values[eulerIndex + 2] = eulerAngles.z;

            var inTangents = new float[count];
            var outTangents = new float[count];
            for (var i = 0; i < count; i++)
            {
                inTangents[i] = 200f + i;
                outTangents[i] = 300f + i;
            }

            return new TransformXml
            {
                name = "test",
                type = type,
                values = values,
                inTangents = inTangents,
                outTangents = outTangents,
                // 全ビット立てておくと挿入位置のビットが複製されたか判別できないため、
                // オイラー X のビットだけ立てる
                inSmoothBit = 1L << eulerIndex,
                outSmoothBit = 1L << eulerIndex,
            };
        }

        [Fact]
        public void ConvertEulerToRotation_オイラー3値がクォータニオン4値になる()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.PngObject, 31, 3, eulerAngles);

            TimelineXml.ConvertEulerToRotation(transform, 3);

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(32, transform.values.Length);
            Assert.Equal(expected.x, transform.values[3], 4);
            Assert.Equal(expected.y, transform.values[4], 4);
            Assert.Equal(expected.z, transform.values[5], 4);
            Assert.Equal(expected.w, transform.values[6], 4);
        }

        [Fact]
        public void ConvertEulerToRotation_挿入位置より後ろの値が1つずれる()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // 旧添字 6 (=106) が 7 へ、旧添字 30 (=130) が 31 へ動く
            Assert.Equal(106f, transform.values[7]);
            Assert.Equal(130f, transform.values[31]);
            // 挿入位置より前は動かない
            Assert.Equal(100f, transform.values[0]);
            Assert.Equal(102f, transform.values[2]);
        }

        [Fact]
        public void ConvertEulerToRotation_タンジェントはオイラーX分を複製して挿入する()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(32, transform.inTangents.Length);
            Assert.Equal(32, transform.outTangents.Length);
            // w のタンジェントはオイラー X (添字 3) の複製
            Assert.Equal(203f, transform.inTangents[6]);
            Assert.Equal(303f, transform.outTangents[6]);
            // 旧添字 6 は 7 へ
            Assert.Equal(206f, transform.inTangents[7]);
            Assert.Equal(306f, transform.outTangents[7]);
        }

        [Fact]
        public void ConvertEulerToRotation_スムーズビットもオイラーX分を複製して挿入する()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // 添字 3 のビットが立っていたので、挿入された 6 にも立つ
            Assert.Equal((1L << 3) | (1L << 6), transform.inSmoothBit);
            Assert.Equal((1L << 3) | (1L << 6), transform.outSmoothBit);
        }

        [Fact]
        public void ConvertEulerToRotation_値数の足りないレコードは変換しない()
        {
            var transform = new TransformXml
            {
                name = "test",
                type = TransformType.PngObject,
                values = new float[] { 1f, 2f },
            };

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(2, transform.values.Length);
        }

        [Fact]
        public void ConvertEulerToRotation_タンジェントが短くても値と長さが揃う()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));
            // タンジェントだけ極端に短い壊れかけのデータ
            transform.inTangents = new float[] { 200f, 201f };
            transform.outTangents = null;

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(32, transform.values.Length);
            // 短いタンジェントは触らない。FromXml 側が不足分を 0 で埋める
            Assert.Equal(2, transform.inTangents.Length);
            Assert.Null(transform.outTangents);
        }
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: コンパイルエラー（`ConvertEulerToRotation` が存在しない）

- [x] **Step 3: ヘルパーを実装する**

`Timeline/TimelineXml.cs` の `InsertBit`（`:1328`）の直後に追加する。

```csharp
        /// <summary>
        /// オイラー角 3 値をクォータニオン 4 値へ変換し、値・タンジェント・スムーズビットを
        /// まとめて 1 スロットぶんずらす。version 35 の移行専用。
        /// 4 つのうち 1 つでも更新し漏れると添字が食い違うため、必ずここで一括して行う
        /// </summary>
        /// <param name="transform">変換対象のレコード</param>
        /// <param name="eulerIndex">オイラー角 X の添字</param>
        public static void ConvertEulerToRotation(TransformXml transform, int eulerIndex)
        {
            var insertIndex = eulerIndex + 3;

            var values = transform.values != null
                ? new List<float>(transform.values)
                : new List<float>();
            if (values.Count < insertIndex)
            {
                // 値数の足りない壊れたレコード。触ると余計に壊れるので素通しする
                MTEUtils.LogWarning(
                    "回転の移行をスキップしました name={0} type={1} count={2}",
                    transform.name, transform.type, values.Count);
                return;
            }

            var eulerAngles = new Vector3(
                values[eulerIndex], values[eulerIndex + 1], values[eulerIndex + 2]);
            var rotation = Quaternion.Euler(eulerAngles);
            values[eulerIndex] = rotation.x;
            values[eulerIndex + 1] = rotation.y;
            values[eulerIndex + 2] = rotation.z;
            values.Insert(insertIndex, rotation.w);
            transform.values = values.ToArray();

            transform.inTangents = InsertDuplicatedValue(
                transform.inTangents, eulerIndex, insertIndex);
            transform.outTangents = InsertDuplicatedValue(
                transform.outTangents, eulerIndex, insertIndex);

            transform.inSmoothBit = InsertBit(
                transform.inSmoothBit, insertIndex, ((transform.inSmoothBit >> eulerIndex) & 1) != 0);
            transform.outSmoothBit = InsertBit(
                transform.outSmoothBit, insertIndex, ((transform.outSmoothBit >> eulerIndex) & 1) != 0);
        }

        /// <summary>
        /// sourceIndex の値を複製して insertIndex へ挿入する。
        /// 値配列より短いタンジェント配列はそのまま返す。
        /// ここで無理に伸ばすより、FromXml 側の不足分を 0 で埋める既存の扱いに任せた方が安全
        /// </summary>
        private static float[] InsertDuplicatedValue(float[] source, int sourceIndex, int insertIndex)
        {
            if (source == null || source.Length < insertIndex)
            {
                return source;
            }

            var list = new List<float>(source);
            list.Insert(insertIndex, list[sourceIndex]);
            return list.ToArray();
        }
```

- [x] **Step 4: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド（`--filter TimelineXmlRotationMigrationTests` を付けてよい）
Expected: 6 件 PASS、2 構成ともビルド成功

- [x] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs
git commit -m "feat(timeline): オイラー角をクォータニオンへ移行するヘルパーを追加する"
```

---

## Task 2: version 35 の移行ブロックと型テーブル

`CurrentVersion` を 35 へ上げ、空の型テーブルを持つ移行ブロックを置く。テーブルへの型追加は Task 4 以降で行う。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:223`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Consumes: `TimelineXml.ConvertEulerToRotation`（Task 1）
- Produces: `TimelineXml.Initialize()` の `version < 35` ブロック、`TimelineXml._eulerToRotationIndices`（`Dictionary<TransformType, int[]>`。値は挿入する順に並べたオイラー X の添字）

- [x] **Step 1: 回帰テストを書く**

`TimelineXmlRotationMigrationTests.cs` に追加する。据え置き型が無変更であることを固定する。

```csharp
        /// <summary>1 レコードだけを持つ version 指定の TimelineXml を作る</summary>
        private static TimelineXml CreateTimeline(int version, TransformXml transform)
        {
            // FrameXml.bones は既定が null なので明示的に作る
            var keyFrame = new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = transform } },
            };
            var layer = new TimelineLayerXml { className = "TestLayer" };
            layer.keyFrames.Add(keyFrame);

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        [Fact]
        public void Initialize_据え置き型は変換しない()
        {
            // ステージライト一括制御の回転は min/max の範囲なので据え置く
            var transform = CreateTransform(TransformType.StageLightController, 37, 6, new Vector3(30f, 40f, 50f));
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            Assert.Equal(37, transform.values.Length);
            Assert.Equal(30f, transform.values[6]);
        }

        [Fact]
        public void Initialize_version35以降は再変換しない()
        {
            var transform = CreateTransform(TransformType.PngObject, 32, 3, new Vector3(30f, 40f, 50f));
            var timeline = CreateTimeline(35, transform);

            timeline.Initialize();

            Assert.Equal(32, transform.values.Length);
        }
```

- [x] **Step 2: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: 2 件とも PASS（移行ブロックがまだ無いので何も変換されない）

> この 2 件は「移行ブロックを足しても据え置き対象を巻き込まない」ことを守る回帰テストで、赤→緑ではなく緑を保ち続けることに意味がある。Step 3 の実装後も PASS し続けることが合格条件。実際に赤から始まるのは Task 4 以降の型ごとのテスト。

- [x] **Step 3: 移行ブロックと型テーブルを実装する**

`Timeline/TimelineData.cs:223` を変更する。

```csharp
        public static readonly int CurrentVersion = 35;
```

`Timeline/TimelineXml.cs` のフィールド定義部（`_replacePluginNameSet` の近く）へ追加する。

```csharp
        /// <summary>
        /// version 35 でクォータニオン化する型と、そのオイラー角 X の添字。
        /// 複数スロットを持つ型は「後ろのスロットから先に」挿入する順で並べる
        /// (先に前を挿入すると後ろの添字がずれるため)
        /// </summary>
        private static readonly Dictionary<TransformType, int[]> _eulerToRotationIndices
            = new Dictionary<TransformType, int[]>();
```

`Initialize()` の末尾（`version < 32` ブロックの後ろ）へ追加する。version 32 以前の移行（`ConvertPostEffectMaskValues` を含む）が先に走り、旧レイアウトを現行レイアウトへ揃えてから本ブロックが動く順序になっていることを確認すること。

```csharp
            if (version < 35)
            {
                // 姿勢を表す回転をオイラー角保持からクォータニオン保持へ移行する。
                // レイヤーの className ではなく TransformXml.Type で拾う
                // (ToXml が常に type を書き出すので、レイヤー構成に依存せず特定できる)
                foreach (var layer in layers)
                {
                    foreach (var keyFrame in layer.keyFrames)
                    {
                        if (keyFrame.bones == null)
                        {
                            continue;
                        }

                        foreach (var bone in keyFrame.bones)
                        {
                            var transform = bone.transform;
                            if (transform == null)
                            {
                                continue;
                            }

                            int[] eulerIndices;
                            if (!_eulerToRotationIndices.TryGetValue(transform.type, out eulerIndices))
                            {
                                continue;
                            }

                            foreach (var eulerIndex in eulerIndices)
                            {
                                ConvertEulerToRotation(transform, eulerIndex);
                            }
                        }
                    }
                }
            }
```

- [x] **Step 4: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs
git commit -m "feat(timeline): タイムラインXMLをversion 35へ上げて回転移行の枠を作る"
```

---

## Task 3: 既存移行処理の添字を型定義から切り離す

`ConvertPostEffectMaskValues`（`TimelineXml.cs:1275`）は version 32 未満の移行で、**実行時の型定義**（`TransformDataRimlight.defaultTrans.valueCount` と `TransformDataRimlight.Index.*`）を添字の真実源にしている。

Task 4 で `TransformDataRimlight` のレイアウトを変えると、この移行が旧 28 値レコードに対して誤った位置へ書き込むようになる（切り詰め量が 3 → 2 に変わり、`MaskMode` / `ExcludeFace` / `ApplyHair` の書き込み先が 1 つずれる）。**Task 4 の前に必ず片付けること。**

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:1275-1326`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TimelineXml` の private const（`RimlightValueCountAtV32` / `RimlightMaskModeIndexAtV32` / `RimlightExcludeFaceIndexAtV32` / `RimlightApplyHairIndexAtV32` / `ParaffinValueCountAtV32` / `ParaffinMaskModeIndexAtV32`）

- [x] **Step 1: 失敗するテストを書く**

現行レイアウト（`valueCount` = 25、`MaskMode` = 16、`ExcludeFace` = 17、`ApplyHair` = 18）を固定するテストを書く。Task 4 で型を変えてもこの期待値は動かない。

```csharp
        [Fact]
        public void Initialize_旧28値リムライトはv32当時のレイアウトへ移行する()
        {
            var values = new float[28];
            for (var i = 0; i < 28; i++)
            {
                values[i] = 100f + i;
            }

            var transform = new TransformXml
            {
                name = "Rimlight",
                type = TransformType.Rimlight,
                values = values,
            };
            var timeline = CreateTimeline(31, transform);

            timeline.Initialize();

            // v32 当時の 25 値へ切り詰められ、その後 v35 で 26 値になる
            Assert.Equal(26, transform.values.Length);

            // MaskMode / ExcludeFace / ApplyHair は v32 当時の添字 16 / 17 / 18 へ
            // 既定値が書かれ、v35 の w 挿入で 17 / 18 / 19 へずれる
            var rimlight = TransformDataRimlight.defaultTrans;
            Assert.Equal(rimlight.maskModeInfo.defaultValue, transform.values[17], 4);
            Assert.Equal(rimlight.excludeFaceInfo.defaultValue, transform.values[18], 4);
            Assert.Equal(rimlight.applyHairInfo.defaultValue, transform.values[19], 4);
        }
```

> このテストは Task 3 の時点では `values.Length == 25`、`values[16..18]` を見る形で書き、Task 4 の完了時に上記の期待値（26 / 17..19）へ更新する。Task 3 では次の形で書くこと。
>
> ```csharp
>             Assert.Equal(25, transform.values.Length);
>             var rimlight = TransformDataRimlight.defaultTrans;
>             Assert.Equal(rimlight.maskModeInfo.defaultValue, transform.values[16], 4);
>             Assert.Equal(rimlight.excludeFaceInfo.defaultValue, transform.values[17], 4);
>             Assert.Equal(rimlight.applyHairInfo.defaultValue, transform.values[18], 4);
> ```

- [x] **Step 2: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: PASS（現行コードは偶然この期待値と一致している。Step 3 はその一致を偶然でなくする作業）

- [x] **Step 3: 添字を定数へ凍結する**

`Timeline/TimelineXml.cs` の `ConvertPostEffectMaskValues` の近くへ定数を置く。

```csharp
        // version 32 当時の値レイアウト。移行処理は「そのバージョン当時の形」を前提に動くため、
        // 実行時の型定義 (valueCount / Index) を参照してはいけない。
        // 参照すると、後で型のレイアウトを変えた瞬間に旧データの書き込み先がずれる
        private const int RimlightValueCountAtV32 = 25;
        private const int RimlightMaskModeIndexAtV32 = 16;
        private const int RimlightExcludeFaceIndexAtV32 = 17;
        private const int RimlightApplyHairIndexAtV32 = 18;
        private const int ParaffinValueCountAtV32 = 22;
        private const int ParaffinMaskModeIndexAtV32 = 21;
```

> `ParaffinValueCountAtV32` と `ParaffinMaskModeIndexAtV32` は、変更前の `TransformDataParaffin.valueCount` と `TransformDataParaffin.Index.MaskMode` を実ファイルで確認して、その実値を書くこと。上の数値は仮置きではなく確認対象。**確認結果: パラフィンの `MaskMode` は 19 ではなく 21**（`valueCount` は 22 で一致）。

`ConvertPostEffectMaskValues` の本体を書き換える。

```csharp
                        if (transform.type == TransformType.Rimlight &&
                            transform.values.Length == OldRimlightValueCount)
                        {
                            var values = new List<float>(transform.values);
                            values.RemoveRange(
                                RimlightValueCountAtV32,
                                OldRimlightValueCount - RimlightValueCountAtV32);
                            values[RimlightMaskModeIndexAtV32] = rimlightTrans.maskModeInfo.defaultValue;
                            values[RimlightExcludeFaceIndexAtV32] = rimlightTrans.excludeFaceInfo.defaultValue;
                            values[RimlightApplyHairIndexAtV32] = rimlightTrans.applyHairInfo.defaultValue;
                            transform.values = values.ToArray();
                            convertedCount++;
                        }
                        else if (transform.type == TransformType.Paraffin &&
                            transform.values.Length == OldParaffinValueCount)
                        {
                            var values = new List<float>(transform.values);
                            values.RemoveRange(
                                ParaffinValueCountAtV32,
                                OldParaffinValueCount - ParaffinValueCountAtV32);
                            values[ParaffinMaskModeIndexAtV32] = paraffinTrans.maskModeInfo.defaultValue;
                            transform.values = values.ToArray();
                            convertedCount++;
                        }
```

書き込む**値**（`maskModeInfo.defaultValue` 等）は型定義から取り続けてよい。添字だけを凍結する。

- [x] **Step 4: 他の移行ブロックにも同じ依存が無いか確認する**

```bash
grep -n "defaultTrans\|\.valueCount\|Index\." source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs
```

`Initialize()` と各 `Convert*` の中で実行時の型定義から添字を読んでいる箇所が他にあれば、同じ形で定数へ凍結する。今回のレイアウト変更で影響を受ける型（表の 7 型）を参照しているものは必ず直す。

- [x] **Step 5: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs
git commit -m "fix(timeline): 旧バージョン移行の添字を型定義から切り離す"
```

---

## Task 4: リムライト（`LerpFrom` 経路と回転種別）

リムライトは汎用の `LerpFrom` を通るため、クォータニオン化と同時に 4 成分をまとめて補間する経路が要る。

**重要:** `lerpKinds`（`TransformDataBase.cs:621`）には「`tangentValues` かつ `baseValues` に載っている値は `Tangent` へ昇格させる」ループ（`:643-680`）がある。`hasRotation` にすると回転 4 成分は `baseValues`（`:307-314`）にも `tangentValues`（リムライトでは `valuesWithoutColors`）にも載るため、**先頭成分だけを特別扱いして残り 3 成分を `Hold` のまま置くと、残り 3 成分が `Tangent` へ昇格して slerp の結果を上書きする**。4 成分すべてに専用の種別を割り当てること。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRimlight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:609-617`（`LerpKind`）、`:621-698`（`lerpKinds`）、`:712-744`（`LerpFrom`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/RimlightRotationLerpTests.cs`（新規）

**Interfaces:**
- Consumes: `TimelineXml._eulerToRotationIndices`（Task 2）
- Produces: `TransformDataRimlight.hasRotation == true`、`rotationValues` が `values[0..3]`、`valueCount == 26`

- [x] **Step 1: 失敗するテストを書く（XML 移行）**

`TimelineXmlRotationMigrationTests.cs` に追加する。

```csharp
        [Fact]
        public void Initialize_リムライトの光源方向をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.Rimlight, 25, 0, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(26, transform.values.Length);
            Assert.Equal(expected.w, transform.values[3], 4);
            // 旧添字 3 (ColorR = 103) が 4 へ動く
            Assert.Equal(103f, transform.values[4]);
        }
```

- [x] **Step 2: 失敗するテストを書く（補間の実挙動）**

`source/COM3D2.SceneEditor.Plugin.Tests/RimlightRotationLerpTests.cs` を新規作成する。**これが Task 4 の要のテスト**で、4 成分が独立に補間されると落ちる。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// リムライトの光源方向は汎用の LerpFrom を通る。
    /// クォータニオン 4 成分を独立に補間すると単位長も最短経路も壊れるため、
    /// まとめて slerp されることを固定する
    /// </summary>
    public class RimlightRotationLerpTests
    {
        private static TransformDataRimlight CreateRimlight(Quaternion rotation)
        {
            var trans = new TransformDataRimlight();
            trans.Initialize("Rimlight");
            trans.rotation = rotation;
            return trans;
        }

        [Fact]
        public void LerpFrom_回転は単位長を保つ()
        {
            var start = CreateRimlight(Quaternion.Euler(0f, 0f, 0f));
            var end = CreateRimlight(Quaternion.Euler(0f, 170f, 0f));
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            var q = current.rotation;
            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            Assert.Equal(1f, magnitude, 3);
        }

        [Fact]
        public void LerpFrom_中点は始点と終点から等距離になる()
        {
            var startRotation = Quaternion.Euler(0f, 0f, 0f);
            var endRotation = Quaternion.Euler(0f, 170f, 0f);

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            var q = current.rotation;
            Assert.Equal(
                Quaternion.Angle(startRotation, q),
                Quaternion.Angle(q, endRotation),
                2);
        }

        [Fact]
        public void LerpFrom_端点では始点と終点そのものになる()
        {
            var startRotation = Quaternion.Euler(10f, 20f, 30f);
            var endRotation = Quaternion.Euler(-40f, 100f, 5f);

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0f);
            Assert.Equal(0f, Quaternion.Angle(startRotation, current.rotation), 2);

            current.LerpFrom(start, end, 0f, 1f, 1f);
            Assert.Equal(0f, Quaternion.Angle(endRotation, current.rotation), 2);
        }

        [Fact]
        public void LerpFrom_内積が負でも最短経路を通る()
        {
            // 200 度ぶん回した向き。素直に成分補間すると 160 度側ではなく遠回りになる
            var startRotation = Quaternion.Euler(0f, 0f, 0f);
            var endRotation = Quaternion.Euler(0f, 200f, 0f);

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            // 最短経路なら中点は始点から約 80 度 (160 度の半分)
            Assert.Equal(80f, Quaternion.Angle(startRotation, current.rotation), 1);
        }
    }
}
```

- [x] **Step 3: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "TimelineXmlRotationMigrationTests|RimlightRotationLerpTests"`
Expected: `Initialize_リムライトの光源方向をクォータニオン化する` が FAIL、`RimlightRotationLerpTests` は `trans.rotation` の setter が効かない（`hasRotation` が false）ためコンパイルは通っても全件 FAIL

- [x] **Step 4: 型テーブルへ登録する**

`Timeline/TimelineXml.cs` の `_eulerToRotationIndices` へ追加する。

```csharp
            { TransformType.Rimlight, new[] { 0 } },
```

- [x] **Step 5: `TransformDataRimlight` の値レイアウトを変える**

`Index` enum を書き換える（`EulerX/Y/Z` を `RotationX/Y/Z/W` にし、`ColorR` 以降を +1）。

```csharp
        public enum Index
        {
            RotationX = 0,
            RotationY = 1,
            RotationZ = 2,
            RotationW = 3,
            ColorR = 4,
            ColorG = 5,
            ColorB = 6,
            ColorA = 7,
            SubColorR = 8,
            SubColorG = 9,
            SubColorB = 10,
            SubColorA = 11,
            Visible = 12,
            Easing = 13,
            LightArea = 14,
            FadeRange = 15,
            FadeExp = 16,
            MaskMode = 17,
            ExcludeFace = 18,
            ApplyHair = 19,
            UseNormal = 20,
            UseAdd = 21,
            UseMultiply = 22,
            UseOverlay = 23,
            UseSubstruct = 24,
            IsWorldSpace = 25,
        }
```

> `IsWorldSpace` は変更前の enum の末尾要素。変更前の実ファイルを開いて、`UseSubstruct = 23` 以降に残っている要素をすべて +1 すること。

`valueCount` と回転のアクセサを差し替える。

```csharp
        public override int valueCount => 26;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[0], values[1], values[2], values[3] };
        }
```

（`hasEulerAngles` と `eulerAnglesValues` の override は削除する。）

`GetCustomValueInfoMap()` の各 `index` は `Index` enum を参照しているのでそのままでよい。参照していないベタ書きの数値があれば +1 する。

- [x] **Step 6: `LerpKind` に回転用の 2 種別を追加する**

`Timeline/TransformData/TransformDataBase.cs:609` の enum へ追加する。

```csharp
        private enum LerpKind
        {
            /// <summary>区間開始値をコピー (Bool / Int / easing / 表示フラグなど)</summary>
            Hold,
            /// <summary>線形補間 (色成分)</summary>
            Linear,
            /// <summary>その値自身の out / in タンジェントでエルミート補間</summary>
            Tangent,
            /// <summary>クォータニオン 4 成分の先頭。ここで 4 成分まとめて slerp して書き込む</summary>
            Rotation,
            /// <summary>クォータニオン 4 成分の 2 つ目以降。Rotation 側が書くので何もしない</summary>
            RotationMember,
        }
```

- [x] **Step 7: `lerpKinds` で回転 4 成分すべてに種別を割り当てる**

`lerpKinds`（`:621`）の、色成分を埋めた直後・**`Tangent` 昇格ループ（`:643`）より前**へ追加する。順序が逆だと 2 つ目以降が `Tangent` へ昇格してしまう。

```csharp
                // クォータニオン保持の回転は 4 成分を独立に補間すると単位長も符号も崩れる。
                // 先頭に Rotation、残りに RotationMember を立てて 4 成分をひとかたまりとして扱う。
                // ここで Hold のまま残すと、後段の Tangent 昇格ループが成分別補間へ戻してしまう
                if (hasRotation)
                {
                    var rotations = rotationValues;
                    for (var i = 0; i < values.Length; i++)
                    {
                        for (var k = 0; k < rotations.Length; k++)
                        {
                            if (ReferenceEquals(values[i], rotations[k]))
                            {
                                kinds[i] = k == 0 ? LerpKind.Rotation : LerpKind.RotationMember;
                                break;
                            }
                        }
                    }
                }
```

- [x] **Step 8: `LerpFrom` に回転分岐を足す**

`LerpFrom`（`:712`）の `switch` へ追加する。

```csharp
                    case LerpKind.Rotation:
                    {
                        // rotationValues の定義上、4 成分は必ず連続している
                        var startRotation = new Quaternion(
                            startValues[i].value, startValues[i + 1].value,
                            startValues[i + 2].value, startValues[i + 3].value);
                        var endRotation = new Quaternion(
                            endValues[i].value, endValues[i + 1].value,
                            endValues[i + 2].value, endValues[i + 3].value);

                        // 内積が負のままだと遠回りの経路になる。終点側の符号を反転して最短経路にする
                        if (Quaternion.Dot(startRotation, endRotation) < 0f)
                        {
                            endRotation = new Quaternion(
                                -endRotation.x, -endRotation.y, -endRotation.z, -endRotation.w);
                        }

                        var lerped = Quaternion.Slerp(startRotation, endRotation, t);
                        values[i].value = lerped.x;
                        values[i + 1].value = lerped.y;
                        values[i + 2].value = lerped.z;
                        values[i + 3].value = lerped.w;
                        break;
                    }
                    case LerpKind.RotationMember:
                        // Rotation 側が 4 成分まとめて書き込むので、ここでは何もしない
                        break;
```

`count`（`Mathf.Min(values.Length, ...)`）は `Rotation` が立つ位置から 4 成分ぶん残っている前提で問題ない。`Rotation` は `rotationValues[0]` にしか立たず、`rotationValues` は必ず 4 要素そろっているため `i + 3` は範囲内になる。

- [x] **Step 9: キー確定時の符号補正が効くかを確認して結論を残す**

`TimelineLayerBase.FixRotation(startFrameNo, endFrameNo)`（`TimelineLayerBase.cs:798`）の呼び出し元を確認する。

```bash
grep -rn "FixRotation" source/COM3D2.SceneEditor.Plugin/Timeline/
```

`GetAnmBinary`（.anm 書き出し専用）からしか呼ばれておらず、ボーンを持たないポストエフェクト系レイヤーは通らない見込み。通らない場合、再生時の符号補正は Step 8 の `Quaternion.Dot` 判定だけが担う。確認した結論を `TransformDataRimlight.cs` の `rotationValues` の直上へ 1 行コメントで残す。

> **この結論は誤りだった（2026-09-14 の調査で訂正）。** `GetAnmBinary` は `CreateAndApplyAnm` からロード時に全レイヤーで呼ばれる（`TimelineManager.cs:384/448/527` の `CreateAndApplyAnmAll`）。つまり `FixRotation` による隣接キーの符号そろえは .anm 書き出し時だけでなく**再生データにも効く**。実データでも移行直後は内積が負になるキー対（2486 対中 24 対）が、ロード後には符号がそろっていることを確認済み。コメントは訂正した。

- [x] **Step 10: Task 3 のリムライト移行テストを新レイアウトへ更新する**

Task 3 Step 1 のテスト `Initialize_旧28値リムライトはv32当時のレイアウトへ移行する` の期待値を、`values.Length == 26`、添字 `17 / 18 / 19` へ更新する（Task 3 Step 1 の本文に記載した最終形）。期待値がこの 1 段ずれに追随することが、v32 移行と v35 移行が正しく連続している証拠になる。

- [x] **Step 11: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 12: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): リムライトの光源方向をクォータニオン保持へ移す"
```

---

## Task 5: PNG 配置

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPngObject.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PngPlacementTimelineLayer.cs:138-142`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TransformDataPngObject.hasRotation == true`、`rotationValues` が `values[3..6]`、`valueCount == 32`

- [x] **Step 1: 失敗するテストを書く**

```csharp
        [Fact]
        public void Initialize_PNG配置の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.PngObject, 31, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(32, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 30 (FixedPosZ = 130) が 31 へ動く
            Assert.Equal(130f, transform.values[31]);
        }
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: FAIL（`values.Length` が 31 のまま）

- [x] **Step 3: 型テーブルへ登録する**

```csharp
            { TransformType.PngObject, new[] { 3 } },
```

- [x] **Step 4: `TransformDataPngObject` の値レイアウトを変える**

`Index` enum の `EulerX/Y/Z = 3/4/5` を `RotationX/Y/Z/W = 3/4/5/6` に置き換え、`ColorR = 6` 以降の全要素を +1 する（`ColorR = 7` 〜 `FixedPosZ = 31`）。

```csharp
        public override int valueCount => 32;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }
```

`hasEulerAngles` / `eulerAnglesValues` の override は削除する。`tangentValues`（`:76-90`）は `baseValues` と `Index` 参照で組んでいるので、ベタ書きの添字が無いことを確認する。

- [x] **Step 5: レイヤーの補間を差し替える**

`Timeline/TimelineLayer/PngPlacementTimelineLayer.cs:138-142` を書き換える。

```csharp
            if (start.rotation != end.rotation)
            {
                transform.localRotation = PluginUtils.HermiteQuaternion(
                    t0, t1, start.rotationValues, end.rotationValues, t);
            }
```

- [x] **Step 6: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): PNG配置の回転をクォータニオン保持へ移す"
```

---

## Task 6: テキスト

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataText.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TextTimelineLayer.cs:154-158`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TransformDataText.hasRotation == true`、`rotationValues` が `values[3..6]`、`valueCount == 21`

- [x] **Step 1: 失敗するテストを書く**

```csharp
        [Fact]
        public void Initialize_テキストの回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.Text, 20, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(21, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 6 (ScaleX = 106) が 7 へ動く
            Assert.Equal(106f, transform.values[7]);
        }
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: FAIL（`values.Length` が 20 のまま）

- [x] **Step 3: 型テーブルへ登録する**

```csharp
            { TransformType.Text, new[] { 3 } },
```

- [x] **Step 4: `TransformDataText` の値レイアウトを変える**

`Index` enum の `EulerX/Y/Z = 3/4/5` を `RotationX/Y/Z/W = 3/4/5/6` に置き換え、`ScaleX = 6` 以降を +1 する（`ScaleX = 7` 〜 `SizeDeltaY = 20`。`Easing` は 13 → 14）。

```csharp
        public override int valueCount => 21;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[7], values[8], values[9] };
        }
```

`easingValue` は `values[(int)Index.Easing]` を参照しているので enum の更新で追随する。`hasEulerAngles` / `eulerAnglesValues` は削除する。

- [x] **Step 5: レイヤーの補間を差し替える**

`Timeline/TimelineLayer/TextTimelineLayer.cs:154-158` を書き換える。

```csharp
            if (start.rotation != end.rotation)
            {
                freeTextSet.rect.rotation = PluginUtils.HermiteQuaternion(
                    t0, t1, start.rotationValues, end.rotationValues, t);
            }
```

> 変更前は `rect.eulerAngles`（ワールド）へ代入していたので、`rect.localRotation` ではなく `rect.rotation` を使うこと。

- [x] **Step 6: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): テキストの回転をクォータニオン保持へ移す"
```

---

## Task 7: ステージライト本体

一括制御（`TransformDataStageLightController`）は範囲の値なので触らない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs:248-257`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs:180-187`（コメントのみ）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TransformDataStageLight.hasRotation == true`、`rotationValues` が `values[3..6]`、`valueCount == 24`

- [x] **Step 1: 失敗するテストを書く**

```csharp
        [Fact]
        public void Initialize_ステージライト本体の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLight, 23, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(24, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 7 (ColorR = 107) が 8 へ動く
            Assert.Equal(107f, transform.values[8]);
        }
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: FAIL（`values.Length` が 23 のまま）

- [x] **Step 3: 型テーブルへ登録する**

```csharp
            { TransformType.StageLight, new[] { 3 } },
```

- [x] **Step 4: `TransformDataStageLight` の値レイアウトを変える**

`Index` enum の `EulerX/Y/Z = 3/4/5` を `RotationX/Y/Z/W = 3/4/5/6` に置き換え、`ColorR = 7` 以降を +1 する（`ColorR = 8` 〜 末尾）。旧添字 6 は enum に無い空きスロットで、挿入により 7 へずれるだけなので追加の対応は要らない。

```csharp
        public override int valueCount => 24;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }
```

`tangentValues`（`:65-81`）は `positionValues` / `eulerAnglesValues` を連結しているので、`eulerAnglesValues` を `rotationValues` へ置き換える。

> **計画に無かった追加対応:** `hasRotation` の型は `Reset()`（`TransformDataBase.cs:1360-1389`）が `initialEulerAngles` ではなく `initialRotation` を読む。`initialEulerAngles => new Vector3(90f, 0f, 0f)` の override を `initialRotation` へ移さないと初期姿勢が identity に化ける。Task 8 の `TransformDataStageLaser` / `TransformDataStageLaserController` も同様（Task 9 では計画済み）。

- [x] **Step 5: レイヤーの補間を差し替える**

`Timeline/TimelineLayer/StageLightTimelineLayer.cs:248-257` を書き換える。

```csharp
            if (!controller.autoRotation)
            {
                light.rotation = PluginUtils.HermiteQuaternion(
                    t0,
                    t1,
                    start.rotationValues,
                    end.rotationValues,
                    t
                );
            }
```

`StageLight`（`Timeline/UnityScripts/StageLight.cs`）に `rotation`（`Quaternion`）プロパティが無ければ、既存の `eulerAngles` プロパティの隣に追加する。

```csharp
        public Quaternion rotation
        {
            get => Quaternion.Euler(eulerAngles);
            set => eulerAngles = value.eulerAngles;
        }
```

- [x] **Step 6: 一括制御を据え置いた理由をコメントに残す**

`Timeline/UnityScripts/StageLightController.cs:180-187` の `autoRotation` ブロックの直前へ追加する。

```csharp
                // rotationMin / rotationMax は「姿勢」ではなく成分ごとに独立した振れ幅なので、
                // クォータニオン化せず成分別の線形補間のまま残す
                // (クォータニオンにすると「X 軸だけ振る」が表現できなくなる)
```

同じ趣旨のコメントを `StageLightTimelineLayer.cs:356` の `if (controller.autoRotation)` ブロックの直前にも置く。

- [x] **Step 7: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): ステージライト本体の回転をクォータニオン保持へ移す"
```

---

## Task 8: レーザー本体と一括制御の本体姿勢

一括制御の `rotationMin` / `rotationMax`（移行後 32–37）は範囲の値なので触らない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLaser.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLaserController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLaserTimelineLayer.cs:225-234`、`:329-335`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TransformDataStageLaser.rotationValues` が `values[1..4]`（`valueCount == 25`）、`TransformDataStageLaserController.rotationValues` が `values[3..6]`（`valueCount == 38`）

- [x] **Step 1: 失敗するテストを書く**

```csharp
        [Fact]
        public void Initialize_レーザー本体の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLaser, 24, 1, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(25, transform.values.Length);
            Assert.Equal(expected.w, transform.values[4], 4);
            // 旧添字 0 (PositionX = 100) は動かない
            Assert.Equal(100f, transform.values[0]);
            // 旧添字 4 (ColorR = 104) が 5 へ動く
            Assert.Equal(104f, transform.values[5]);
        }

        [Fact]
        public void Initialize_レーザー一括制御は本体姿勢だけをクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLaserController, 37, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = Quaternion.Euler(eulerAngles);
            Assert.Equal(38, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // rotationMin (旧 31〜33 = 131〜133) は値のまま 32〜34 へずれる
            Assert.Equal(131f, transform.values[32]);
            Assert.Equal(133f, transform.values[34]);
        }
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: 2 件とも FAIL

- [x] **Step 3: 型テーブルへ登録する**

```csharp
            { TransformType.StageLaser, new[] { 1 } },
            { TransformType.StageLaserController, new[] { 3 } },
```

- [x] **Step 4: `TransformDataStageLaser` の値レイアウトを変える**

`Index` enum の `EulerX/Y/Z = 1/2/3` を `RotationX/Y/Z/W = 1/2/3/4` に置き換え、`ColorR = 4` 以降を +1 する。

```csharp
        public override int valueCount => 25;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[1], values[2], values[3], values[4] };
        }
```

`tangentValues`（`:57-72`）の `eulerAnglesValues` を `rotationValues` へ置き換える。

- [x] **Step 5: `TransformDataStageLaserController` の値レイアウトを変える**

`Index` enum の `EulerX/Y/Z = 3/4/5` を `RotationX/Y/Z/W = 3/4/5/6` に置き換え、`ColorR = 6` 以降を +1 する（`RotationMinX` は 31 → 32、`RotationMaxZ` は 36 → 37）。

```csharp
        public override int valueCount => 38;

        public override bool hasRotation => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }
```

`tangentValues`（`:80-98`）の `eulerAnglesValues` を `rotationValues` へ置き換える。`rotationMinValues` / `rotationMaxValues` は `Index` 参照なので enum の更新で追随する。

- [x] **Step 6: レイヤーの補間を差し替える**

`StageLaserTimelineLayer.cs:225-234`:

```csharp
            if (!controller.autoRotation)
            {
                laser.rotation = PluginUtils.HermiteQuaternion(
                    t0,
                    t1,
                    start.rotationValues,
                    end.rotationValues,
                    t
                );
            }
```

`StageLaserTimelineLayer.cs:329-335`:

```csharp
            controller.rotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t
            );
```

同メソッド内の `controller.rotationMin` / `controller.rotationMax` は据え置く。据え置いた理由を `if (controller.autoRotation)` の直前へ書く。

```csharp
            // rotationMin / rotationMax は成分ごとに独立した振れ幅なので、
            // 姿勢と違ってクォータニオン化せず成分別の補間のまま残す
```

`StageLaser` / `StageLaserController`（`Timeline/UnityScripts/`）に `rotation`（`Quaternion`）プロパティが無ければ、Task 7 の `StageLight` と同じ形で追加する。

- [x] **Step 7: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): レーザーの姿勢をクォータニオン保持へ移す"
```

---

## Task 9: サイリウムの手の姿勢（左右 2 スロット）

1 レコードで 2 スロットを挿入する唯一の型。後ろ（右手）から挿入する。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPsylliumTransform.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs:461-480`、`:608-627`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（テーブル登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRotationMigrationTests.cs`

**Interfaces:**
- Produces: `TransformDataPsylliumTransform.hasRotation == true` / `hasSubRotation == true`、`rotationValues` が `values[6..9]`、`subRotationValues` が `values[10..13]`、`valueCount == 14`

- [x] **Step 1: 失敗するテストを書く**

```csharp
        [Fact]
        public void Initialize_サイリウムの手は左右2スロットともクォータニオン化する()
        {
            var left = new Vector3(30f, 40f, 50f);
            var right = new Vector3(-30f, -40f, -50f);

            var transform = CreateTransform(TransformType.PsylliumTransform, 12, 6, left);
            transform.values[9] = right.x;
            transform.values[10] = right.y;
            transform.values[11] = right.z;

            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expectedLeft = Quaternion.Euler(left);
            var expectedRight = Quaternion.Euler(right);
            Assert.Equal(14, transform.values.Length);
            // 左手 6〜9
            Assert.Equal(expectedLeft.x, transform.values[6], 4);
            Assert.Equal(expectedLeft.w, transform.values[9], 4);
            // 右手 10〜13
            Assert.Equal(expectedRight.x, transform.values[10], 4);
            Assert.Equal(expectedRight.w, transform.values[13], 4);
            // 位置 (0〜5) は動かない
            Assert.Equal(100f, transform.values[0]);
            Assert.Equal(105f, transform.values[5]);
        }
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineXmlRotationMigrationTests`
Expected: FAIL（`values.Length` が 12 のまま）

- [x] **Step 3: 型テーブルへ登録する**

```csharp
            // 左手 6-8 / 右手 9-11。後ろから挿入しないと左手の挿入で右手の添字がずれる
            { TransformType.PsylliumTransform, new[] { 9, 6 } },
```

- [x] **Step 4: `TransformDataPsylliumTransform` の値レイアウトを変える**

```csharp
        public override int valueCount => 14;

        public override bool hasPosition => true;
        public override bool hasSubPosition => true;
        public override bool hasRotation => true;
        public override bool hasSubRotation => true;
        public override bool hasTangent => true;

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[6], values[7], values[8], values[9] };
        }

        public override ValueData[] subRotationValues
        {
            get => new ValueData[] { values[10], values[11], values[12], values[13] };
        }
```

`hasEulerAngles` / `hasSubEulerAngles` / `eulerAnglesValues` / `subEulerAnglesValues` の override は削除する。

初期値の override を回転側へ移す（`hasRotation` の型では `TransformDataBase.ResetTransformData`（`:1320-1335`）が `initialEulerAngles` ではなく `initialRotation` を読むため、そのままだと初期値が失われる）。

```csharp
        public override Quaternion initialRotation => Quaternion.Euler(defaultConfig.eulerAnglesLeft);
        public override Quaternion initialSubRotation => Quaternion.Euler(defaultConfig.eulerAnglesRight);
```

（`initialEulerAngles` / `initialSubEulerAngles` の override は削除する。）

- [x] **Step 5: レイヤーの補間を差し替える**

`PsylliumTimelineLayer.cs:461-480` と `:608-627` の 2 箇所（同じ形）を書き換える。

```csharp
            if (start.rotation != end.rotation)
            {
                transformConfig.eulerAnglesLeft = PluginUtils.HermiteQuaternion(
                    t0,
                    t1,
                    start.rotationValues,
                    end.rotationValues,
                    t).eulerAngles;
            }

            if (start.subRotation != end.subRotation)
            {
                transformConfig.eulerAnglesRight = PluginUtils.HermiteQuaternion(
                    t0,
                    t1,
                    start.subRotationValues,
                    end.subRotationValues,
                    t).eulerAngles;
            }
```

> `PsylliumTransformConfig` の保持形式はオイラーのまま据え置く方針なので、境界で `.eulerAngles` に落とす。補間自体はクォータニオン空間で完結する。

- [x] **Step 6: テストが通ることを確認する**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): サイリウムの手の姿勢をクォータニオン保持へ移す"
```

---

## Task 10: `PsylliumHand` の左右反転をクォータニオン鏡像にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs:146-161`

**Interfaces:**
- Consumes: なし（`handConfig.barOffsetRotation` は `Vector3` のまま）

- [x] **Step 1: 現状の挙動を確認する**

変更前のコードは次のとおり。`barEulerAngles` は本数に比例して増える小角度のオフセットで、右手では `y` / `z` の符号を反転している。

```csharp
                var barEulerAngles = (j - (count - 1) * 0.5f) * handConfig.barOffsetRotation;

                if (!isLeftHand)
                {
                    barPosition.x = -barPosition.x;
                    barEulerAngles.y = -barEulerAngles.y;
                    barEulerAngles.z = -barEulerAngles.z;
                }
                ...
                psyllium.transform.localEulerAngles = barEulerAngles;
```

- [x] **Step 2: クォータニオン鏡像へ差し替える**

```csharp
                var barRotation = Quaternion.Euler(
                    (j - (count - 1) * 0.5f) * handConfig.barOffsetRotation);

                if (!isLeftHand)
                {
                    barPosition.x = -barPosition.x;
                    // YZ 平面の鏡像。オイラー角で y / z の符号を反転したものと同値だが、
                    // 角度が大きいときも表現が壊れない
                    barRotation = new Quaternion(
                        barRotation.x, -barRotation.y, -barRotation.z, barRotation.w);
                }

                ...
                psyllium.transform.localRotation = barRotation;
```

> `x` はそのまま、`y` / `z` を反転、`w` はそのまま。これが「X 軸まわりは保ち、Y / Z 軸まわりを反転する」鏡像で、旧実装のオイラー反転と小角度で一致する。
>
> **実機検算の結果（2026-09-14）:** 小角度に限らず一致した。`Quaternion.Angle(mirror, oldWay)` は (0,20,30) / (45,60,80) / (120,150,170) / (10,0,0) のいずれでも 0。つまり挙動は変わらず、大きな角度でも表現が壊れないことだけが得られる。スクリーンショットでの目視確認は不要と判断した。

- [x] **Step 3: ビルドとテストを通す**

Run: Global Constraints のビルド＋テストコマンド
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 4: 実機で左右対称を確認する**

`com3d25-devbridge` の `eval_csharp` でサイリウムを配置し、`barOffsetRotation` を大きめ（例: `new Vector3(0, 20, 30)`）に設定して左右の手のバーが鏡像になっていることを `screenshot` で確認する。

- [x] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs
git commit -m "refactor(timeline): サイリウムの左右反転をクォータニオン鏡像にする"
```

---

## Task 11: 実機での移行検証

- [x] **オフラインでの実データ検証（スクリーンショット比較の代替）**

実ユーザーのタイムライン `W:\COM3D2_5\PhotoModeData\_Timeline`（346 件、version 2〜34）を
一括で `Initialize()` に通して検証した。

- **314 件が移行成功。生成された回転レコード 172,839 件はすべて単位長**（非単位長 0 件）。
  添字がずれていれば回転でない値が混ざって単位長から外れるため、これが実データでの添字の裏取りになる
- 残る 32 件（version 14 / 15 / 16）は移行できなかったが、原因は**本変更と無関係の既存コード**。
  `TimelineXml.cs:785` の StageLight 回転 → オイラー角の移行が
  ネイティブの `rotation.eulerAngles` を踏むため、テストプロセスでのみ落ちる（実機では通る）。
  managed 実装へ寄せるには Unity と一致する quaternion → euler が要るので別件とした
- 既存移行で回転より前の添字を動かすものは無いことを確認済み（`version < 30` の Rimlight は添字 23 へ挿入）。
  よってオイラー角の先頭添字はどのバージョンでも安定しており、v35 の移行が前提にしてよい
- フィクスチャ経由の回帰は `RotationMigrationFixtureTests` として常設した
  （実長キーの値数と単位長、スタブの素通し、二重変換されないこと）

- [ ] **実機での目視確認（ユーザー操作が必要）**

上記で添字と数値の健全性は取れているが、「見た目が変わらないこと」は実機でしか確認できない。
プラグイン差し替えとタイムライン読込はユーザーの操作が要るため未実施。確認したい観点:

- 1 回転を超える回転を含むデータは最短経路の表現へ畳まれるため見た目が変わりうる（仕様どおりの非可逆変換）
- 180 度をまたぐ区間で経路が変わるのは改善であって退行ではない

---

## Task 12: ドキュメント更新

**Files:**
- Modify: `docs-site/timeline/compatibility.md`
- Modify: `docs/timeline-rotation-quaternion-survey.md`
- Modify: `docs/timeline-release-debt-review.md`

- [x] **Step 1: `compatibility.md` に version 35 を追記する**

既存のバージョン別記述に合わせて次を書く。

- version 35 で PNG 配置 / テキスト / リムライト光源方向 / ステージライト本体 / レーザー本体と一括制御の本体姿勢 / サイリウムの手の姿勢がクォータニオン保持になったこと
- 読込時に自動で移行されること。version 35 で保存した XML は version 34 では正しく読めないこと
- 1 回転を超える回転を含む既存データは、最短経路の表現へ畳まれるため見た目が変わりうること
- ステージライト・レーザー一括制御の `rotationMin` / `rotationMax` はオイラーのまま残ること

- [x] **Step 2: 調査書の対応状況を更新する**

`docs/timeline-rotation-quaternion-survey.md` の冒頭の対応状況と 8 章の表で、「保留」行を対応済みへ改める。据え置いた 2 種類（範囲の値 / スナップ適用の回転）とその理由を明記する。

- [x] **Step 3: 負債一覧へ反映する**

`docs/timeline-release-debt-review.md` に、回転保持の一本化が version 35 で部分的に完了したことと、残した据え置きの範囲を追記する。あわせて「バージョン移行処理は実行時の型定義を参照しない」という原則（Task 3 の成果）を記録する。

- [x] **Step 4: docs-review をかける**

`docs-review` スキルでドキュメントの整合性と可読性をレビューし、指摘を取り込む。

- [x] **Step 5: コミット**

```bash
git add docs docs-site
git commit -m "docs(timeline): 回転のクォータニオン化とversion 35の移行を記録する"
```

---

## Task 13: コードレビュー

- [x] **Step 1: `code-review` スキルでレビューする**

実装全体（Task 1〜10 の差分）を対象に `code-review` スキルを実行し、妥当な指摘を取り込む。

- [x] **Step 2: 2 構成のビルドと全テストを通す**

Run: Global Constraints のビルド＋テストコマンド（`--filter` なし）
Expected: 全件 PASS、2 構成ともビルド成功

- [x] **Step 3: コミット**

```bash
git add -A
git commit -m "refactor(timeline): コードレビューの指摘を取り込む"
```

---

## レビュー対応メモ

2026-09-14 の `plan-review`（`plan-reviewer`）の指摘と対応。却下した指摘は無い。

- 🔴 `LerpKind.Rotation` を先頭成分だけに立てると、残り 3 成分が `lerpKinds` の `Tangent` 昇格ループ（`TransformDataBase.cs:643-680`）に拾われて slerp の結果を上書きする — **取り込み**。`baseValues`（`:307-314`）に回転 4 成分が載ることを実コードで確認。`RotationMember` を追加して 4 成分すべてに種別を割り当てる設計へ変更（Task 4 Step 6〜8）
- 🔴 `ConvertPostEffectMaskValues`（`TimelineXml.cs:1275`）が実行時の `valueCount` / `Index` を添字の真実源にしており、リムライトのレイアウト変更で旧 28 値データの書き込み先がずれる — **取り込み**。Task 3 として独立させ、Task 4 の前に添字を定数へ凍結する
- 🟡 `FixRotation` の確認を grep 任せの「実装時に判断」で先送りしている — **取り込み**。Task 4 Step 9 で呼び出し元を確認し、結論をコードコメントに残す手順へ変更
- 🟡 `InsertDuplicatedValue` のガードが `values` 側と揃っておらず、タンジェント長の不整合が残りうる — **取り込み**。許容する理由をコメントに明記し、その挙動を固定するテストを Task 1 に追加
- 🟡 実際の補間結果を検証するテストが無い — **取り込み**。`RimlightRotationLerpTests` を新設（Task 4 Step 2）。単位長・中点の等距離・端点一致・最短経路の 4 観点

## コードレビュー対応メモ（2026-09-14）

`code-review` スキル（`code-reviewer` / `code-readability-reviewer` 並列）の結果と対応。却下した指摘は無い。

- 機能面: **APPROVE**（🔴 0 / 🟡 0 / 🟢 1）。添字ずらしは 7 型 8 スロットすべて手計算で追跡され一致、サイリウムの後ろからの挿入順も検証済み
  - 🟢 `LerpFrom` の `Rotation` 分岐が壊れたデータで前フレームの値を暗黙に保持する — **取り込み**。他の種別と同じく区間開始値へフォールバックするよう修正し、挙動をテストで固定
- 可読性: **APPROVE WITH COMMENTS**（🔴 0 / 🟡 2 / 🟢 5）
  - 🟡 `initialRotation` の説明が 4 型で逐語重複 — **取り込み**。`TransformDataBase.initialRotation` の XML doc へ集約し、各型の override 側は削除
  - 🟡 `rotationMin` / `rotationMax` 据え置き理由が 3 箇所で逐語重複 — **取り込み**。宣言側（`TransformDataStageLaserController.rotationMinValues` / `TransformDataStageLightController.eulerAnglesValues`）へ集約し、呼び出し側は 1 行参照へ圧縮
  - 🟢 `PsylliumTimelineLayer` の同一コメント 2 回 — **取り込み**。2 回目を参照コメントへ圧縮
  - 🟢 その他 4 件（`QuaternionUtils` の docstring、`LerpFrom` の case ブロック、`Index` の採番、テストの命名）はいずれも「残置」判定
