# カメラ手ブレ (CameraShake) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メインカメラに非破壊の手ブレを付けられるようにする（揺れパラメータをタイムラインのキーフレームとして持ち、UOCamera の内部状態を汚さずカメラ Transform へ適用する）。

**Architecture:** `CameraTimelineLayer` に `shake` ボーンを追加し、`TransformDataCameraShake`（振幅 6 + 周波数倍率 + シード）をキーフレーム化する。レイヤーは Update フェーズで補間済みパラメータから `CameraShakeNoise` のオフセットを算出し、`LateUpdate` で `CameraShakeApplier` がカメラ自身の軸へ適用する。カメラ Transform は Update フェーズ中に `UltimateOrbitCamera.Update` と `SetTransform` / `SetRotationZ` の複数経路から直接書かれるため、それらより確実に後になる LateUpdate を適用点にする（Harmony パッチは使わない）。UOCamera の注視点・旋回角・距離・FOV には一切書かないため、キー化時に揺れが混入しうるのはロールだけで、その読み書きだけ `CameraShakeApplier` を経由させる。

**Tech Stack:** C# (LangVersion 9 / net48 テスト, Unity 5.6 ランタイム), xUnit

**Spec:** `docs/superpowers/specs/2026-09-19-camera-shake-design.md`

## Global Constraints

- コードのコメントとエラーログメッセージは**日本語**で書く
- ハードコーディングは避ける（定数は名前付きで定義する）
- `MTEUtils/` は共有 submodule。**このプランでは一切変更しない**
- タイムライン側のコード（`Timeline/` 配下）の名前空間は `COM3D2.MotionTimelineEditor.Plugin`、プラグイン側は `COM3D2.SceneEditor.Plugin`。前者から後者を参照するときは既存の `using SE = SceneEditor.Plugin;` 別名を使う
- テストは Unity ランタイム無しで走る。`Quaternion.Euler` / `Quaternion.AngleAxis` など `InternalCall` の API はテストで使えない（`SecurityException` で落ちる）。回転が要るテストは `Tests/TestQuaternions.cs` の `AroundX` / `AroundY` / `Negate` を使う
- `CameraShakeNoise` は `UnityEngine.Mathf` ではなく `System.Math` を使って実装する（テストでネイティブ呼び出しを踏まないため）
- `TimelineData.CurrentVersion` は上げない
- ビルド・テストは次の 1 本で回す（`debug.bat` は実機へコピーするので使わない）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin" && \
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m && \
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m && \
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

COM3D2 構成のビルドは `bin/Debug/COM3D25/` の DLL を消すため、**必ず COM3D2 → COM3D25 → dotnet test の順**で実行する。

---

## File Structure

| ファイル | 責務 |
|---|---|
| `Timeline/CameraShakeParams.cs`（新規） | 揺れパラメータの値オブジェクト（振幅・周波数倍率・シード） |
| `Timeline/CameraShakeNoise.cs`（新規） | パラメータと経過秒からオフセットを算出する純粋関数 |
| `Timeline/TransformData/ITransformData.cs`（変更） | `TransformType.CameraShake` の追加 |
| `Timeline/TransformData/TransformDataCameraShake.cs`（新規） | 揺れパラメータのキーフレームデータ |
| `Timeline/TimelineIntegration.cs`（変更） | `TransformDataCameraShake` の生成登録 |
| `Manager/CameraShakeApplier.cs`（新規） | カメラ Transform への適用と復元、ロールの読み書き、編集中パラメータの保持 |
| `Timeline/TimelineLayer/CameraTimelineLayer.cs`（変更） | shake ボーンの追加・適用順・LateUpdate での適用・キー生成 |
| `CameraShakeRowDrawer.cs`（新規） | 揺れパラメータの行描画 |
| `Timeline/ItemInspector/CameraItemInspector.cs`（変更） | 選択項目による描画の分岐 |
| `MainCameraRowDrawer.cs`（変更） | ロール行の読み書きを CameraShakeApplier 経由にする |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeNoiseTests.cs`（新規） | ノイズの決定性・振幅・周波数 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TransformDataCameraShakeTests.cs`（新規） | 値数・既定値・カスタム値の index 対応 |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeApplierTests.cs`（新規） | 復元判定ロジック |

---

### Task 1: 揺れパラメータとノイズ生成

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeParams.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeNoise.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeNoiseTests.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `struct COM3D2.MotionTimelineEditor.Plugin.CameraShakeParams`
    - フィールド: `Vector3 positionAmplitude`, `Vector3 rotationAmplitude`, `float frequencyScale`, `int seed`
    - `static CameraShakeParams Default` (振幅 0 / 倍率 1 / シード 0)
    - `bool isZero` (位置・回転振幅がすべて 0 なら true)
  - `static class COM3D2.MotionTimelineEditor.Plugin.CameraShakeNoise`
    - `static float Sample(int axis, float seconds, int seed, float frequencyScale)`
    - `static void Evaluate(CameraShakeParams p, float seconds, out Vector3 positionOffset, out Vector3 eulerOffset)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeNoiseTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeNoiseTests
    {
        [Fact]
        public void 同じ引数なら常に同じ値を返す()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            Assert.Equal(a, b);
        }

        [Fact]
        public void シードが違えば波形が変わる()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(0, 1.25f, 43, 1f);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void 軸が違えば波形が変わる()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(1, 1.25f, 42, 1f);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void 振幅が1を超えない()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = CameraShakeNoise.Sample(i % 6, i * 0.05f, 7, 1f);
                Assert.InRange(value, -1f, 1f);
            }
        }

        [Fact]
        public void 周波数倍率2倍は半分の時間で同じ位相になる()
        {
            var a = CameraShakeNoise.Sample(2, 3.0f, 11, 1f);
            var b = CameraShakeNoise.Sample(2, 1.5f, 11, 2f);
            Assert.Equal(a, b, 5);
        }

        [Fact]
        public void 振幅0ならオフセットも0()
        {
            var p = CameraShakeParams.Default;
            Vector3 pos, euler;
            CameraShakeNoise.Evaluate(p, 1.7f, out pos, out euler);
            Assert.Equal(Vector3.zero, pos);
            Assert.Equal(Vector3.zero, euler);
        }

        [Fact]
        public void オフセットは振幅に比例する()
        {
            var p1 = CameraShakeParams.Default;
            p1.positionAmplitude = new Vector3(0.1f, 0f, 0f);
            var p2 = p1;
            p2.positionAmplitude = new Vector3(0.2f, 0f, 0f);

            Vector3 pos1, pos2, euler;
            CameraShakeNoise.Evaluate(p1, 1.7f, out pos1, out euler);
            CameraShakeNoise.Evaluate(p2, 1.7f, out pos2, out euler);

            Assert.Equal(pos1.x * 2f, pos2.x, 5);
        }

        [Fact]
        public void 位置と回転は別の軸番号を使うので独立に揺れる()
        {
            var p = CameraShakeParams.Default;
            p.positionAmplitude = Vector3.one;
            p.rotationAmplitude = Vector3.one;

            Vector3 pos, euler;
            CameraShakeNoise.Evaluate(p, 2.3f, out pos, out euler);

            Assert.NotEqual(pos.x, euler.x);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter CameraShakeNoiseTests`
Expected: コンパイルエラー（`CameraShakeNoise` / `CameraShakeParams` が存在しない）

- [ ] **Step 3: CameraShakeParams を実装**

`source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeParams.cs`:

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのパラメータ。
    /// タイムラインのキー (TransformDataCameraShake) と編集中の値 (CameraShakeApplier) の
    /// 受け渡しに使う値オブジェクト
    /// </summary>
    public struct CameraShakeParams
    {
        /// <summary>カメラ自身の右/上/前方向の振幅 (m)</summary>
        public Vector3 positionAmplitude;

        /// <summary>ピッチ/ヨー/ロールの振幅 (度)</summary>
        public Vector3 rotationAmplitude;

        /// <summary>ノイズ周波数の倍率</summary>
        public float frequencyScale;

        /// <summary>波形を変えるための整数</summary>
        public int seed;

        public static CameraShakeParams Default
        {
            get
            {
                return new CameraShakeParams
                {
                    positionAmplitude = Vector3.zero,
                    rotationAmplitude = Vector3.zero,
                    frequencyScale = 1f,
                    seed = 0,
                };
            }
        }

        /// <summary>振幅がすべて 0 で、揺れが発生しない状態か</summary>
        public bool isZero
        {
            get
            {
                return positionAmplitude == Vector3.zero &&
                    rotationAmplitude == Vector3.zero;
            }
        }
    }
}
```

- [ ] **Step 4: CameraShakeNoise を実装**

`source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeNoise.cs`:

```csharp
using System;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのノイズ生成。
    /// 軸ごとにハッシュした周波数・位相の正弦波 3 本を重ねて手持ちカメラらしい揺れを作る。
    /// 経過秒だけを入力とする純粋関数なので、スクラブ・再生・動画出力で必ず同じ結果になる。
    /// Unity のネイティブ呼び出しを避けるため三角関数は System.Math を使う (テストが実行時に落ちるため)
    /// </summary>
    public static class CameraShakeNoise
    {
        /// <summary>位置 XYZ が軸 0-2、回転 XYZ が軸 3-5</summary>
        private const int RotationAxisOffset = 3;

        private const double TwoPi = Math.PI * 2.0;

        /// <summary>重ねる 3 本の正弦波の配合比 (合計 1.0)</summary>
        private const double Weight1 = 0.64;
        private const double Weight2 = 0.26;
        private const double Weight3 = 0.10;

        /// <summary>
        /// 指定軸のノイズ値 (-1〜1)。
        /// 基本周波数は 0.18〜0.36 / 0.40〜0.64 / 0.72〜1.06 Hz で、ゆっくりした手持ちの揺らぎになる
        /// </summary>
        public static float Sample(int axis, float seconds, int seed, float frequencyScale)
        {
            var f1 = 0.18 + Hash01(seed + axis * 1013 + 17) * 0.18;
            var f2 = 0.40 + Hash01(seed + axis * 2029 + 53) * 0.24;
            var f3 = 0.72 + Hash01(seed + axis * 4051 + 97) * 0.34;

            var p1 = Hash01(seed + axis * 8081 + 193) * TwoPi;
            var p2 = Hash01(seed + axis * 16001 + 389) * TwoPi;
            var p3 = Hash01(seed + axis * 32003 + 769) * TwoPi;

            var t = seconds * (double)frequencyScale;

            return (float)(
                Math.Sin(TwoPi * f1 * t + p1) * Weight1 +
                Math.Sin(TwoPi * f2 * t + p2) * Weight2 +
                Math.Sin(TwoPi * f3 * t + p3) * Weight3);
        }

        /// <summary>パラメータと経過秒から位置・回転のオフセットを求める</summary>
        public static void Evaluate(
            CameraShakeParams p, float seconds,
            out Vector3 positionOffset, out Vector3 eulerOffset)
        {
            if (p.isZero)
            {
                positionOffset = Vector3.zero;
                eulerOffset = Vector3.zero;
                return;
            }

            positionOffset = new Vector3(
                p.positionAmplitude.x * Sample(0, seconds, p.seed, p.frequencyScale),
                p.positionAmplitude.y * Sample(1, seconds, p.seed, p.frequencyScale),
                p.positionAmplitude.z * Sample(2, seconds, p.seed, p.frequencyScale));

            eulerOffset = new Vector3(
                p.rotationAmplitude.x * Sample(RotationAxisOffset + 0, seconds, p.seed, p.frequencyScale),
                p.rotationAmplitude.y * Sample(RotationAxisOffset + 1, seconds, p.seed, p.frequencyScale),
                p.rotationAmplitude.z * Sample(RotationAxisOffset + 2, seconds, p.seed, p.frequencyScale));
        }

        /// <summary>整数から 0〜1 の決定的な疑似乱数を作る (xorshift)</summary>
        private static double Hash01(int input)
        {
            var x = (uint)input;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return (x & 0xFFFFFF) / 16777215.0;
        }
    }
}
```

- [ ] **Step 5: ビルドしてテストが通ることを確認**

Run: Global Constraints のビルド + テストコマンド
Expected: `CameraShakeNoiseTests` が 8 件 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeParams.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/CameraShakeNoise.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeNoiseTests.cs
git commit -m "feat(timeline): カメラ手ブレのノイズ生成を追加"
```

---

### Task 2: 揺れパラメータのキーフレームデータ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（`TransformType` に `CameraShake` を追加）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCameraShake.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:468` 付近（`TransformType.Camera` の登録の直後）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformDataCameraShakeTests.cs`

**Interfaces:**
- Consumes: Task 1 の `CameraShakeParams`
- Produces:
  - `TransformType.CameraShake`
  - `class TransformDataCameraShake : TransformDataBase`
    - `CameraShakeParams shakeParams { get; set; }`（8 値との相互変換）
    - カスタム値キー: `"posAmplitudeX"`, `"posAmplitudeY"`, `"posAmplitudeZ"`, `"rotAmplitudeX"`, `"rotAmplitudeY"`, `"rotAmplitudeZ"`, `"frequencyScale"`, `"seed"`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TransformDataCameraShakeTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TransformDataCameraShakeTests
    {
        private static TransformDataCameraShake Create()
        {
            var trans = new TransformDataCameraShake();
            trans.Initialize("shake");
            return trans;
        }

        [Fact]
        public void 値は8個で位置回転スケールを持たない()
        {
            var trans = Create();
            Assert.Equal(8, trans.valueCount);
            Assert.False(trans.hasPosition);
            Assert.False(trans.hasEulerAngles);
            Assert.False(trans.hasScale);
            Assert.True(trans.hasTangent);
        }

        [Fact]
        public void 既定値は振幅0で周波数倍率1()
        {
            var trans = Create();
            var p = trans.shakeParams;
            Assert.Equal(Vector3.zero, p.positionAmplitude);
            Assert.Equal(Vector3.zero, p.rotationAmplitude);
            Assert.Equal(1f, p.frequencyScale);
            Assert.Equal(0, p.seed);
        }

        [Fact]
        public void パラメータを書いて読み戻せる()
        {
            var trans = Create();
            trans.shakeParams = new CameraShakeParams
            {
                positionAmplitude = new Vector3(0.01f, 0.02f, 0.03f),
                rotationAmplitude = new Vector3(1f, 2f, 3f),
                frequencyScale = 2.5f,
                seed = 123,
            };

            var p = trans.shakeParams;
            Assert.Equal(new Vector3(0.01f, 0.02f, 0.03f), p.positionAmplitude);
            Assert.Equal(new Vector3(1f, 2f, 3f), p.rotationAmplitude);
            Assert.Equal(2.5f, p.frequencyScale);
            Assert.Equal(123, p.seed);
        }

        [Fact]
        public void カスタム値のindexが値配列と対応する()
        {
            var trans = Create();
            Assert.Equal(0, trans.GetCustomValueInfo("posAmplitudeX").index);
            Assert.Equal(2, trans.GetCustomValueInfo("posAmplitudeZ").index);
            Assert.Equal(3, trans.GetCustomValueInfo("rotAmplitudeX").index);
            Assert.Equal(5, trans.GetCustomValueInfo("rotAmplitudeZ").index);
            Assert.Equal(6, trans.GetCustomValueInfo("frequencyScale").index);
            Assert.Equal(7, trans.GetCustomValueInfo("seed").index);
        }

        [Fact]
        public void 型はCameraShake()
        {
            Assert.Equal(TransformType.CameraShake, Create().type);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test ... --filter TransformDataCameraShakeTests`
Expected: コンパイルエラー（`TransformDataCameraShake` が存在しない）

- [ ] **Step 3: TransformType に CameraShake を追加**

`Timeline/TransformData/ITransformData.cs` の `Camera,` の直後に 1 行追加する:

```csharp
        Camera,
        CameraShake,
        DepthOfField,
```

（`TransformXml.type` は `XmlSerializer` の既定で名前として直列化されるため、途中への追加でも既存 XML の読み込みは壊れない）

- [ ] **Step 4: TransformDataCameraShake を実装**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCameraShake.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのキーフレームデータ。
    /// 位置・回転そのものではなく揺れの振幅を持ち、CameraTimelineLayer がノイズへ通して
    /// カメラ Transform のオフセットに変換する。
    /// 振幅自体がキーで動くのでフェードイン/アウトは振幅キーで表現する
    /// </summary>
    public class TransformDataCameraShake : TransformDataBase
    {
        public override TransformType type => TransformType.CameraShake;

        public enum Index
        {
            PosAmplitudeX = 0,
            PosAmplitudeY = 1,
            PosAmplitudeZ = 2,
            RotAmplitudeX = 3,
            RotAmplitudeY = 4,
            RotAmplitudeZ = 5,
            FrequencyScale = 6,
            Seed = 7,
        }

        public override int valueCount => 8;

        public override bool hasPosition => false;
        public override bool hasEulerAngles => false;
        public override bool hasScale => false;
        // カメラ値と同じく常時 Tangent 補間
        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        /// <summary>振幅の上限。手持ちカメラとして自然な範囲に収める</summary>
        public const float MaxPositionAmplitude = 0.2f;
        public const float MaxRotationAmplitude = 5f;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap
            = new Dictionary<string, CustomValueInfo>
        {
            { "posAmplitudeX", new CustomValueInfo { index = (int)Index.PosAmplitudeX, name = "位置X", min = 0f, max = MaxPositionAmplitude, step = 0.001f, defaultValue = 0f } },
            { "posAmplitudeY", new CustomValueInfo { index = (int)Index.PosAmplitudeY, name = "位置Y", min = 0f, max = MaxPositionAmplitude, step = 0.001f, defaultValue = 0f } },
            { "posAmplitudeZ", new CustomValueInfo { index = (int)Index.PosAmplitudeZ, name = "位置Z", min = 0f, max = MaxPositionAmplitude, step = 0.001f, defaultValue = 0f } },
            { "rotAmplitudeX", new CustomValueInfo { index = (int)Index.RotAmplitudeX, name = "回転X", min = 0f, max = MaxRotationAmplitude, step = 0.01f, defaultValue = 0f } },
            { "rotAmplitudeY", new CustomValueInfo { index = (int)Index.RotAmplitudeY, name = "回転Y", min = 0f, max = MaxRotationAmplitude, step = 0.01f, defaultValue = 0f } },
            { "rotAmplitudeZ", new CustomValueInfo { index = (int)Index.RotAmplitudeZ, name = "回転Z", min = 0f, max = MaxRotationAmplitude, step = 0.01f, defaultValue = 0f } },
            { "frequencyScale", new CustomValueInfo { index = (int)Index.FrequencyScale, name = "周波数", min = 0.1f, max = 5f, step = 0.01f, defaultValue = 1f } },
            { "seed", new CustomValueInfo { index = (int)Index.Seed, name = "シード", min = 0f, max = 9999f, step = 1f, defaultValue = 0f } },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        /// <summary>8 個の値とパラメータ構造体の相互変換</summary>
        public CameraShakeParams shakeParams
        {
            get
            {
                return new CameraShakeParams
                {
                    positionAmplitude = new Vector3(
                        values[(int)Index.PosAmplitudeX].value,
                        values[(int)Index.PosAmplitudeY].value,
                        values[(int)Index.PosAmplitudeZ].value),
                    rotationAmplitude = new Vector3(
                        values[(int)Index.RotAmplitudeX].value,
                        values[(int)Index.RotAmplitudeY].value,
                        values[(int)Index.RotAmplitudeZ].value),
                    frequencyScale = values[(int)Index.FrequencyScale].value,
                    seed = values[(int)Index.Seed].intValue,
                };
            }
            set
            {
                values[(int)Index.PosAmplitudeX].value = value.positionAmplitude.x;
                values[(int)Index.PosAmplitudeY].value = value.positionAmplitude.y;
                values[(int)Index.PosAmplitudeZ].value = value.positionAmplitude.z;
                values[(int)Index.RotAmplitudeX].value = value.rotationAmplitude.x;
                values[(int)Index.RotAmplitudeY].value = value.rotationAmplitude.y;
                values[(int)Index.RotAmplitudeZ].value = value.rotationAmplitude.z;
                values[(int)Index.FrequencyScale].value = value.frequencyScale;
                values[(int)Index.Seed].intValue = value.seed;
            }
        }

        public TransformDataCameraShake()
        {
        }
    }
}
```

- [ ] **Step 5: 生成登録を追加**

`Timeline/TimelineIntegration.cs` の `TransformType.Camera` の登録（468 行付近）の直後に追加する:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.CameraShake,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataCameraShake>);
```

- [ ] **Step 6: ビルドしてテストが通ることを確認**

Run: Global Constraints のビルド + テストコマンド
Expected: `TransformDataCameraShakeTests` が 5 件 PASS、既存テストも全 PASS（`TangentValueCoverageTests` が新しい型を拾う場合はその期待値も更新する）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCameraShake.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/TransformDataCameraShakeTests.cs
git commit -m "feat(timeline): カメラ手ブレのキーフレームデータを追加"
```

---
### Task 3: カメラ Transform への適用

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/CameraShakeApplier.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeApplierTests.cs`

**設計の前提（実装者向け）:**
カメラ Transform は 1 フレームの Update フェーズ中に複数の経路から直接書かれる。

- `UltimateOrbitCamera.Update()`（`cameraMove` のとき位置・回転を再計算）
- `CameraTimelineLayer.ApplyMotion()` → `uoCamera.SetAroundAngle()` → `UltimateOrbitCamera.SetTransform()`（`_transform.rotation` / `_transform.position` を直接書く）
- 同じく `camera.SetRotationZ()`（`transform.eulerAngles` を直接書く）

これらの実行順は不定なので、`UltimateOrbitCamera.Update` に Harmony パッチを当てる方式では揺れが上書きされる。
Unity は全 `Update()` の後に `LateUpdate()` を回すため、適用は Task 4 で `CameraTimelineLayer.LateUpdate()` から呼ぶ。
このタスクでは適用ロジックだけを作る。

**Interfaces:**
- Consumes: Task 1 の `CameraShakeParams`
- Produces: `static class COM3D2.SceneEditor.Plugin.CameraShakeApplier`
  - `static CameraShakeParams editingParams`（public フィールド。行描画が編集し、`UpdateFrame` が読む編集中の値）
  - `static void SetOffset(Vector3 positionOffset, Vector3 eulerOffset)`（レイヤーが Update フェーズで呼ぶ）
  - `static void ClearOffset()`
  - `static void Apply(Camera camera)`（LateUpdate から 1 フレーム 1 回。復元 → 適用）
  - `static void Restore(Camera camera)`（揺れを戻して状態を捨てる。無効化・シーン遷移用）
  - `static float GetCleanRotationZ(Camera camera)`
  - `static void SetCleanRotationZ(Camera camera, float z)`
  - `static bool IsSameTransform(Vector3 a, Vector3 b, Quaternion ra, Quaternion rb)`（復元判定。テスト対象）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeApplierTests.cs`:

```csharp
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeApplierTests
    {
        [Fact]
        public void 完全一致なら同一とみなす()
        {
            var pos = new Vector3(1f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.True(CameraShakeApplier.IsSameTransform(pos, pos, rot, rot));
        }

        [Fact]
        public void 誤差の範囲内なら同一とみなす()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(1f + 1e-6f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.True(CameraShakeApplier.IsSameTransform(a, b, rot, rot));
        }

        [Fact]
        public void 位置が動いていれば別物とみなす()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(1.05f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.False(CameraShakeApplier.IsSameTransform(a, b, rot, rot));
        }

        [Fact]
        public void 回転が動いていれば別物とみなす()
        {
            var pos = new Vector3(1f, 2f, 3f);
            var ra = TestQuaternions.AroundY(30f);
            var rb = TestQuaternions.AroundY(35f);
            Assert.False(CameraShakeApplier.IsSameTransform(pos, pos, ra, rb));
        }

        [Fact]
        public void 符号が逆の同一回転は同一とみなす()
        {
            // Quaternion は q と -q が同じ姿勢を表す
            var pos = Vector3.zero;
            var ra = TestQuaternions.AroundY(30f);
            var rb = TestQuaternions.Negate(ra);
            Assert.True(CameraShakeApplier.IsSameTransform(pos, pos, ra, rb));
        }

        [Fact]
        public void 編集中パラメータの既定は振幅0で周波数倍率1()
        {
            CameraShakeApplier.editingParams =
                COM3D2.MotionTimelineEditor.Plugin.CameraShakeParams.Default;
            var p = CameraShakeApplier.editingParams;
            Assert.Equal(1f, p.frequencyScale);
            Assert.True(p.isZero);
        }
    }
}
```

`Tests/TestQuaternions.cs` に `AroundY` / `Negate` が無い場合は、同ファイルの既存の作法に合わせて追加する
（`Quaternion.Euler` は使わず、`new Quaternion(0, sin(θ/2), 0, cos(θ/2))` を `System.Math` で組む）。

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter CameraShakeApplierTests`
Expected: コンパイルエラー（`CameraShakeApplier` が存在しない）

- [ ] **Step 3: CameraShakeApplier を実装**

`source/COM3D2.SceneEditor.Plugin/Manager/CameraShakeApplier.cs`:

```csharp
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレをメインカメラの Transform へ適用する。
    ///
    /// カメラ Transform は Update フェーズ中に UltimateOrbitCamera.Update・SetTransform・SetRotationZ の
    /// 複数経路から直接書かれ、その実行順は不定。そのため適用は必ずそれらより後になる
    /// LateUpdate (CameraTimelineLayer.LateUpdate) から Apply を呼ぶ形にしている。
    ///
    /// 復元は差し引き計算ではなく「揺れ前の値そのもの」を書き戻す。揺れが累積しない。
    /// UOCamera の内部状態 (注視点・旋回角・距離・FOV) には一切書かないので、
    /// キー化時に揺れが混入しうるのはロールだけ。ロールの読み書きは Get/SetCleanRotationZ を通すこと
    /// </summary>
    public static class CameraShakeApplier
    {
        /// <summary>復元判定の許容誤差。浮動小数の誤差より十分大きく、意味のある編集量より十分小さい</summary>
        private const float SameTransformEpsilon = 1e-4f;

        /// <summary>今フレームに適用する揺れのオフセット</summary>
        private static Vector3 _positionOffset;
        private static Vector3 _eulerOffset;

        /// <summary>揺れを適用済みか</summary>
        private static bool _applied;

        /// <summary>揺れを足す前の値</summary>
        private static Vector3 _basePosition;
        private static Quaternion _baseRotation;

        /// <summary>揺れを足した後に書いた値 (外部から書き換えられていないかの判定に使う)</summary>
        private static Vector3 _appliedPosition;
        private static Quaternion _appliedRotation;

        /// <summary>行描画が編集し、UpdateFrame がキーへ書き出す編集中のパラメータ</summary>
        public static MTEP.CameraShakeParams editingParams = MTEP.CameraShakeParams.Default;

        /// <summary>レイヤーが Update フェーズで算出した揺れのオフセットを渡す</summary>
        public static void SetOffset(Vector3 positionOffset, Vector3 eulerOffset)
        {
            _positionOffset = positionOffset;
            _eulerOffset = eulerOffset;
        }

        /// <summary>揺れを止める (キーが無い / レイヤーが適用されないフレーム)</summary>
        public static void ClearOffset()
        {
            _positionOffset = Vector3.zero;
            _eulerOffset = Vector3.zero;
        }

        /// <summary>
        /// 1 フレームにつき 1 回、LateUpdate から呼ぶ。
        /// 前フレームの揺れを戻してから今フレームの揺れを足す
        /// </summary>
        public static void Apply(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            var transform = camera.transform;

            RestoreInternal(transform);

            if (_positionOffset == Vector3.zero && _eulerOffset == Vector3.zero)
            {
                return;
            }

            _basePosition = transform.position;
            _baseRotation = transform.rotation;

            // カメラ自身の軸に沿って揺らす (画面に対する揺れになる)
            transform.position = _basePosition + _baseRotation * _positionOffset;
            transform.rotation = _baseRotation * Quaternion.Euler(_eulerOffset);

            _appliedPosition = transform.position;
            _appliedRotation = transform.rotation;
            _applied = true;
        }

        /// <summary>揺れを戻して状態を捨てる (プラグイン無効化・シーン遷移用)</summary>
        public static void Restore(Camera camera)
        {
            ClearOffset();

            if (camera == null)
            {
                _applied = false;
                return;
            }

            RestoreInternal(camera.transform);
        }

        /// <summary>揺れを含まないロール。キー化と UI 表示はこちらを使う</summary>
        public static float GetCleanRotationZ(Camera camera)
        {
            if (_applied)
            {
                return _baseRotation.eulerAngles.z;
            }
            return camera.transform.eulerAngles.z;
        }

        /// <summary>
        /// 揺れを含まないロールを書く。
        /// 揺れ適用中は揺れ前の値を差し替えてから揺れを乗せ直すので、編集値に揺れ分が混ざらない
        /// </summary>
        public static void SetCleanRotationZ(Camera camera, float z)
        {
            var transform = camera.transform;

            if (!_applied)
            {
                var eulerAngles = transform.eulerAngles;
                eulerAngles.z = z;
                transform.eulerAngles = eulerAngles;
                return;
            }

            var baseEulerAngles = _baseRotation.eulerAngles;
            baseEulerAngles.z = z;
            _baseRotation = Quaternion.Euler(baseEulerAngles);

            transform.position = _basePosition + _baseRotation * _positionOffset;
            transform.rotation = _baseRotation * Quaternion.Euler(_eulerOffset);

            _appliedPosition = transform.position;
            _appliedRotation = transform.rotation;
        }

        /// <summary>復元してよいか (誰も間に書いていないか) の判定</summary>
        public static bool IsSameTransform(Vector3 a, Vector3 b, Quaternion ra, Quaternion rb)
        {
            if ((a - b).sqrMagnitude > SameTransformEpsilon * SameTransformEpsilon)
            {
                return false;
            }

            // Quaternion.Angle はネイティブ呼び出しのため Dot で比較する (テストでも使えるようにする)
            var dot = Quaternion.Dot(ra, rb);
            return 1f - Mathf.Abs(dot) <= SameTransformEpsilon;
        }

        /// <summary>前フレームに書いた揺れが残っていれば揺れ前へ戻す</summary>
        private static void RestoreInternal(Transform transform)
        {
            if (!_applied)
            {
                return;
            }

            // 間に誰も書いていなければ揺れ前へ戻す。書かれていたら後勝ちでそのまま残す
            if (IsSameTransform(transform.position, _appliedPosition,
                    transform.rotation, _appliedRotation))
            {
                transform.position = _basePosition;
                transform.rotation = _baseRotation;
            }

            _applied = false;
        }
    }
}
```

- [ ] **Step 4: ビルドしてテストが通ることを確認**

Run: Global Constraints のビルド + テストコマンド
Expected: `CameraShakeApplierTests` が 6 件 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/CameraShakeApplier.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeApplierTests.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/TestQuaternions.cs
git commit -m "feat(camera): カメラ手ブレの適用ロジックを追加"
```

---

### Task 4: カメラレイヤーへの shake ボーン追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeLayerTests.cs`（新規）

**Interfaces:**
- Consumes: Task 1 の `CameraShakeNoise` / `CameraShakeParams`、Task 2 の `TransformDataCameraShake`、Task 3 の `CameraShakeApplier`
- Produces:
  - `CameraTimelineLayer.ShakeBoneName = "shake"` / `ShakeDisplayName = "手ブレ"`
  - `allBoneNames` が `{ "camera", "shake" }`
  - `GetTransformType("shake") == TransformType.CameraShake`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeLayerTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeLayerTests
    {
        [Fact]
        public void カメラレイヤーはカメラと手ブレの2ボーンを持つ()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(2, layer.allBoneNames.Count);
            Assert.Contains(CameraTimelineLayer.CameraBoneName, layer.allBoneNames);
            Assert.Contains(CameraTimelineLayer.ShakeBoneName, layer.allBoneNames);
        }

        [Fact]
        public void ボーン名に応じたTransformTypeを返す()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(TransformType.Camera,
                layer.GetTransformType(CameraTimelineLayer.CameraBoneName));
            Assert.Equal(TransformType.CameraShake,
                layer.GetTransformType(CameraTimelineLayer.ShakeBoneName));
        }

        [Fact]
        public void シードは補間せず始点の値を使う()
        {
            var start = new TransformDataCameraShake();
            start.Initialize(CameraTimelineLayer.ShakeBoneName);
            var startParams = CameraShakeParams.Default;
            startParams.seed = 100;
            start.shakeParams = startParams;

            var end = new TransformDataCameraShake();
            end.Initialize(CameraTimelineLayer.ShakeBoneName);
            var endParams = CameraShakeParams.Default;
            endParams.seed = 900;
            end.shakeParams = endParams;

            // 区間の中間でも始点のシードが使われる
            Assert.Equal(100, CameraTimelineLayer.ResolveShakeSeed(start, 0.5f));
            Assert.Equal(100, CameraTimelineLayer.ResolveShakeSeed(start, 0.99f));
        }
    }
}
```

`CameraTimelineLayer.Create` が Unity への副作用で落ちる場合は、`TimelineLayerTestUtils` の既存レイヤーテストの作法に合わせる。
`ResolveShakeSeed` は Step 4 で追加する static メソッドで、シードを補間しないことをテストから確認するための切り出し。

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test ... --filter CameraShakeLayerTests`
Expected: コンパイルエラー（`ShakeBoneName` / `ResolveShakeSeed` が存在しない）

- [ ] **Step 3: ボーンの追加とメニュー登録**

`Timeline/TimelineLayer/CameraTimelineLayer.cs`:

```csharp
        public static string CameraBoneName = "camera";
        public static string CameraDisplayName = "カメラ";
        public static string ShakeBoneName = "shake";
        public static string ShakeDisplayName = "手ブレ";

        private List<string> _allBoneNames = new List<string> { CameraBoneName, ShakeBoneName };
```

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            allMenuItems.Add(new BoneMenuItem(CameraBoneName, CameraDisplayName));
            allMenuItems.Add(new BoneMenuItem(ShakeBoneName, ShakeDisplayName));
        }
```

```csharp
        public override TransformType GetTransformType(string name)
        {
            if (name == ShakeBoneName)
            {
                return TransformType.CameraShake;
            }
            return TransformType.Camera;
        }
```

ファイル先頭の `namespace` 直下に別名を追加する（`SceneEditorHack.cs` と同じ作法）:

```csharp
    using SE = SceneEditor.Plugin;
```

- [ ] **Step 4: 適用順と揺れの算出を実装**

`ApplyPlayData` を override して順序を固定する（基底は `_playDataMap.Values` の列挙順に依存するため）:

```csharp
        protected override void ApplyPlayData()
        {
            if (!isCurrent && !config.isCameraSync)
            {
                return;
            }

            // 基底の ApplyPlayData を呼ばなくなるため、同じメイド未ロードのガードを引き継ぐ
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            // キーが無いフレームで前フレームの揺れが残らないよう、毎フレーム 0 から始める
            SE.CameraShakeApplier.ClearOffset();

            // 手ブレ → カメラの順に適用する (依存の向きを明示するため順序を固定する)
            ApplyPlayDataByType(TransformType.CameraShake);
            ApplyPlayDataByType(TransformType.Camera);
        }
```

`ApplyMotion` の先頭で shake を分岐する:

```csharp
        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (motion.start.type == TransformType.CameraShake)
            {
                ApplyShakeMotion(motion, t);
                return;
            }

            // (以降は既存のカメラ適用処理)
```

```csharp
        /// <summary>
        /// 補間済みの揺れパラメータからオフセットを算出して適用側へ渡す。
        /// 実際に Transform へ書くのは LateUpdate (Update フェーズの直接書き込みより後にするため)
        /// </summary>
        private void ApplyShakeMotion(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataCameraShake>(motion, t);
            var shakeParams = scratch.shakeParams;
            shakeParams.seed = ResolveShakeSeed(motion.start as TransformDataCameraShake, t);

            SE.CameraShakeApplier.editingParams = shakeParams;

            Vector3 positionOffset, eulerOffset;
            CameraShakeNoise.Evaluate(shakeParams, playingTime, out positionOffset, out eulerOffset);
            SE.CameraShakeApplier.SetOffset(positionOffset, eulerOffset);
        }

        /// <summary>
        /// 使用するシード。補間すると毎フレーム値が変わり、ハッシュのカオス性で周波数・位相が
        /// 不連続にジャンプして波形が壊れるため、区間の始点の値をそのまま使う
        /// (追従設定を補間しないのと同じ方針)
        /// </summary>
        public static int ResolveShakeSeed(TransformDataCameraShake start, float t)
        {
            return start != null ? start.shakeParams.seed : 0;
        }
```

- [ ] **Step 5: LateUpdate で適用する**

```csharp
        public override void LateUpdate()
        {
            base.LateUpdate();

            // Update フェーズ中に UOCamera.Update / SetTransform / SetRotationZ が Transform を
            // 直接書くため、揺れは必ずそれらより後の LateUpdate で乗せる
            SE.CameraShakeApplier.Apply(camera);
        }

        public override void OnPluginDisable()
        {
            base.OnPluginDisable();

            SE.CameraShakeApplier.Restore(camera);
        }
```

- [ ] **Step 6: キー生成 (UpdateFrame) を拡張**

既存のカメラキー生成で、ロールは揺れ前の値を使う:

```csharp
            var rotZ = SE.CameraShakeApplier.GetCleanRotationZ(camera);
```

続けて手ブレキーを作る:

```csharp
            var shakeTrans = CreateTransformData<TransformDataCameraShake>(ShakeBoneName);
            shakeTrans.shakeParams = SE.CameraShakeApplier.editingParams;

            var shakeBone = frame.CreateBone(shakeTrans);
            frame.UpdateBone(shakeBone);
```

- [ ] **Step 7: ビルドしてテストが通ることを確認**

Run: Global Constraints のビルド + テストコマンド
Expected: 新規 3 件を含め全 PASS

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeLayerTests.cs
git commit -m "feat(timeline): カメラレイヤーに手ブレボーンを追加"
```

---

### Task 5: 揺れパラメータの編集 UI

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/CameraShakeRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MainCameraRowDrawer.cs:71` と同ファイルのロールスライダー `onChanged`

**Interfaces:**
- Consumes: Task 3 の `CameraShakeApplier.editingParams` / `GetCleanRotationZ` / `SetCleanRotationZ`、Task 2 の `TransformDataCameraShake.MaxPositionAmplitude` / `MaxRotationAmplitude`
- Produces: `static class CameraShakeRowDrawer` の `static void Draw(GUIView view, float rowHeight)`

- [ ] **Step 1: 行描画を実装**

`source/COM3D2.SceneEditor.Plugin/CameraShakeRowDrawer.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのパラメータ行 (位置振幅 3 軸・回転振幅 3 軸・周波数倍率・シード)。
    /// 編集先は CameraShakeApplier.editingParams で、キー化時はそこからキーが作られる
    /// </summary>
    public static class CameraShakeRowDrawer
    {
        private const float LabelWidth = 40f;
        private const int SeedMax = 9999;

        public static void Draw(GUIView view, float rowHeight)
        {
            var p = CameraShakeApplier.editingParams;

            view.DrawLabel("位置振幅 (m)", -1, rowHeight);
            DrawAmplitudeSlider(view, "X", p.positionAmplitude.x,
                MTEP.TransformDataCameraShake.MaxPositionAmplitude, 0.001f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.positionAmplitude.x = value;
                    CameraShakeApplier.editingParams = current;
                });
            DrawAmplitudeSlider(view, "Y", p.positionAmplitude.y,
                MTEP.TransformDataCameraShake.MaxPositionAmplitude, 0.001f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.positionAmplitude.y = value;
                    CameraShakeApplier.editingParams = current;
                });
            DrawAmplitudeSlider(view, "Z", p.positionAmplitude.z,
                MTEP.TransformDataCameraShake.MaxPositionAmplitude, 0.001f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.positionAmplitude.z = value;
                    CameraShakeApplier.editingParams = current;
                });

            view.DrawHorizontalLine();

            view.DrawLabel("回転振幅 (度)", -1, rowHeight);
            DrawAmplitudeSlider(view, "X", p.rotationAmplitude.x,
                MTEP.TransformDataCameraShake.MaxRotationAmplitude, 0.01f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.rotationAmplitude.x = value;
                    CameraShakeApplier.editingParams = current;
                });
            DrawAmplitudeSlider(view, "Y", p.rotationAmplitude.y,
                MTEP.TransformDataCameraShake.MaxRotationAmplitude, 0.01f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.rotationAmplitude.y = value;
                    CameraShakeApplier.editingParams = current;
                });
            DrawAmplitudeSlider(view, "Z", p.rotationAmplitude.z,
                MTEP.TransformDataCameraShake.MaxRotationAmplitude, 0.01f,
                value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.rotationAmplitude.z = value;
                    CameraShakeApplier.editingParams = current;
                });

            view.DrawHorizontalLine();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "周波数",
                labelWidth = LabelWidth,
                width = -1,
                min = 0.1f,
                max = 5f,
                step = 0.01f,
                defaultValue = 1f,
                value = p.frequencyScale,
                onChanged = value =>
                {
                    var current = CameraShakeApplier.editingParams;
                    current.frequencyScale = value;
                    CameraShakeApplier.editingParams = current;
                },
            });

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "シード",
                    labelWidth = LabelWidth,
                    width = -1,
                    min = 0f,
                    max = SeedMax,
                    step = 1f,
                    defaultValue = 0f,
                    value = p.seed,
                    onChanged = value =>
                    {
                        var current = CameraShakeApplier.editingParams;
                        current.seed = Mathf.RoundToInt(value);
                        CameraShakeApplier.editingParams = current;
                    },
                });

                if (view.DrawButton("ランダム", 70, rowHeight))
                {
                    var current = CameraShakeApplier.editingParams;
                    current.seed = Random.Range(0, SeedMax + 1);
                    CameraShakeApplier.editingParams = current;
                }
            }
            view.EndLayout();
        }

        private static void DrawAmplitudeSlider(
            GUIView view, string label, float value,
            float max, float step, System.Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LabelWidth,
                width = -1,
                min = 0f,
                max = max,
                step = step,
                defaultValue = 0f,
                value = value,
                onChanged = onChanged,
            });
        }
    }
}
```

- [ ] **Step 2: CameraItemInspector を分岐させる**

`Timeline/ItemInspector/CameraItemInspector.cs` の `DrawItems` 先頭で選択項目を見る。
クラスのドキュメントコメント（「項目は『カメラ』1 つだけのため選択内容によらず…」）も実態に合わせて更新すること:

```csharp
        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 手ブレ項目だけが選ばれているときは揺れパラメータの行を出す
            if (items.Count > 0 && IsShakeOnly(items))
            {
                CameraShakeRowDrawer.Draw(view, RowHeight);
                return;
            }

            // (以降は既存のカメラ構図の描画)
        }

        private static bool IsShakeOnly(IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                if (item.name != MTEP.CameraTimelineLayer.ShakeBoneName)
                {
                    return false;
                }
            }
            return true;
        }
```

- [ ] **Step 3: ロール行を揺れ非依存にする**

`MainCameraRowDrawer.DrawAngleSliders` のロールは現在 Transform を直接読み書きしている。
読み（71 行目付近）:

```csharp
            var roll = AngleUtils.NormalizeAngle(camera.transform.eulerAngles.z);
```

を次に差し替える:

```csharp
            // 手ブレ適用中は揺れ込みの値になるため、揺れ前のロールを読む
            var roll = AngleUtils.NormalizeAngle(CameraShakeApplier.GetCleanRotationZ(camera));
```

書き（同メソッド末尾のロールスライダーの `onChanged`）:

```csharp
                    RecordCameraEdit("ロール");
                    var eulerAngles = camera.transform.eulerAngles;
                    eulerAngles.z = value;
                    camera.transform.eulerAngles = eulerAngles;
```

を次に差し替える:

```csharp
                    RecordCameraEdit("ロール");
                    CameraShakeApplier.SetCleanRotationZ(camera, value);
```

- [ ] **Step 4: ビルドしてテストが通ることを確認**

Run: Global Constraints のビルド + テストコマンド
Expected: 全 PASS（UI は自動テスト対象外。コンパイルが通ることを確認する）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/CameraShakeRowDrawer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs \
        source/COM3D2.SceneEditor.Plugin/MainCameraRowDrawer.cs
git commit -m "feat(camera): カメラ手ブレの編集UIを追加"
```

---

### Task 6: 実機検証と適用点の確定

**Files:**
- Modify（必要な場合のみ）: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`（`Apply` の呼び出し位置）
- Modify: `docs/superpowers/specs/2026-09-19-camera-shake-design.md`（検証結果の反映）

**Interfaces:**
- Consumes: Task 3-5 の全実装
- Produces: 検証済みの適用点（変更がなければコード変更なし）

このタスクはゲームを起動した状態で MCP `com3d25-devbridge` を使う。ゲームが起動していない場合はユーザーに起動を依頼する。

- [ ] **Step 1: ビルドした DLL を実機へ反映**

ゲーム起動中は `debug.bat` でのコピーが失敗するため、ホットリロード（CLAUDE.md の手順）かゲーム再起動で反映する。

- [ ] **Step 2: 揺れの基本動作を確認**

手順: カメラレイヤーの「手ブレ」項目で位置振幅 0.05 / 回転振幅 1 を入れてキーを打ち、再生する。
Expected: カメラが揺れる。`screenshot` で目視確認する。ちらつき（1 フレームおきに揺れが消える）が無いことも見る。

- [ ] **Step 3: 決定性を確認**

手順: 同じフレームへ 2 回スクラブし、`eval_csharp` でカメラの `transform.position` を 2 回読む。
Expected: 2 回の値が一致する。

- [ ] **Step 4: キー化の非汚染を確認**

手順: 揺れ適用中に `eval_csharp` で UOCamera の `target.position` / `GetAroundAngle()` / `distance` と
`CameraShakeApplier.GetCleanRotationZ(camera)` を読み、揺れ振幅を 0 にしたときの同じ値と比較する。
Expected: いずれも一致する（揺れの影響を受けていない）。

- [ ] **Step 5: 追従 ON での順序を確認（設計上の懸念点）**

手順: `MaidFollowMainCamera` の追従を ON にして再生し、位置の揺れが残るか確認する。
`MaidFollowMainCamera` も LateUpdate で書くため、同フェーズ内の順序は不定。
Expected: 揺れが残る。**消える場合**は `CameraShakeApplier.Apply(camera)` の呼び出しを追従の適用後へ動かす
（適用ロジックは `Apply` に閉じているので、呼び出し位置の変更だけで済む）。

- [ ] **Step 6: ロール編集との相互作用を確認**

手順: 揺れ適用中にカメラ項目のロールスライダーを動かし、値が揺れ分だけ飛ばないこと、
離した後のロールが指定値どおりであることを確認する。
Expected: スライダーの表示値と実際のロール（揺れ前）が一致する。

- [ ] **Step 7: 後始末を確認**

手順: 振幅を 0 に戻す / タイムラインを閉じる / プラグインを無効化する、をそれぞれ試す。
Expected: いずれもカメラが揺れ前の位置・向きへ戻り、揺れが残らない。

- [ ] **Step 8: 検証結果を記録してコミット**

適用点の変更が必要だった場合はコードを修正し、仕様書
`docs/superpowers/specs/2026-09-19-camera-shake-design.md` の「追従 (MaidFollowMainCamera) との順序」節を実測結果で更新する。
変更が無かった場合も「実測で CameraTimelineLayer.LateUpdate のままでよいことを確認」と追記する。

```bash
git add -A
git commit -m "fix(camera): 実機検証に基づき手ブレの適用点を確定"
```

---

## 完了条件

- 全タスクのテストが PASS し、COM3D2 / COM3D25 の両構成がビルドできる
- Task 6 の実機検証 7 項目がすべて期待どおり
- `code-review` スキルによるレビューを通している（CLAUDE.md の標準フロー）

## レビュー却下メモ

- Harmony パッチを全 `UltimateOrbitCamera` インスタンスに当てる件（static 状態の混線リスク） — Harmony パッチ方式自体を取りやめたため該当しなくなった
- `CameraShakeApplier` の状態遷移をさらに純粋関数へ切り出してテストする提案 — 復元判定（`IsSameTransform`）が切り出せる唯一の実質的な分岐で、残りは Transform への代入そのもの。これ以上の切り出しは実装を歪めるため見送り
- ループ再生境界でのノイズ不連続 — 仕様の「非対象 (YAGNI)」へ既知の挙動として明記し、対策は行わない
