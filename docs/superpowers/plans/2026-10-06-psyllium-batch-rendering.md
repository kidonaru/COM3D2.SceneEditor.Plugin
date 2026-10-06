# サイリウムのバッチ描画化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** サイリウム 1 本ごとの GameObject（手 + バー）をやめ、最大 1023 本を 1 つの Renderer にまとめて描き、タイムライン再生中の描画（約 4ms/f）と位置更新（約 1.8ms/f）を減らす。

**Architecture:** バー 1 本の位置と上方向をエリアローカル座標で計算し、`Vector4` 配列（`_BarPos` / `_BarUp`）に詰めて `MaterialPropertyBlock` でシェーダーへ渡す。メッシュは「バー 1023 本ぶんの形を重ねたもの」を 1 つ共有し、頂点の `uv2.y` に入れたスロット番号で配列を引いてビルボードを組む。手は GameObject を持たない素の C# クラスにし、エリアの子にバッチ用 GameObject（`PsylliumBatchRenderer`）を必要数だけ置く。

**Tech Stack:** C# (COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1)、Unity 5.6 でビルドする se_bundle のシェーダー（HLSL / CG、target 4.5）、xunit

**Spec:** 本計画の「背景」節（2026-10-06 の実機プロファイル結果）

## 背景

2026-10-06 に、サイリウム入りタイムラインの再生中（コントローラー 1、エリア 4、手 5116、バー 5412）を実機で計測した結果:

| 項目 | 値 |
|---|---|
| フレーム全体 | 約 25ms/f（約 40fps、2560x1440） |
| バーの描画（全バーの Renderer を切った A/B） | **約 4.2ms/f** |
| `PsylliumArea.UpdateTransform`（4 エリア合計） | **約 1.83ms/f**（`PreUpdateTransform` 約 0.8ms + Transform 書き込み約 0.6ms） |
| TimelineManager.Update 全体 | 約 2.6ms/f |

- バーは 1 本ずつ MeshRenderer を持ち、サブメッシュ 2 つ（不透明 `MTE/Psyllium` + 加算 `MTE/PsylliumAdd`）なので **ドローコールは約 1 万**。シェーダーはオブジェクト行列（`unity_ObjectToWorld`）からバーの根元と上方向を取り出してビルボードを組むので、`DisableBatching` でバッチングも効かない
- **GPU インスタンシングは採らない**（2.5 専用にしてよい前提でも同じ）:
  - se_bundle は Unity 5.6 でしかビルドできない（UnityProject は 5.6 固定）。5.6 でコンパイルしたインスタンシング用バリアントの定数バッファ配置（5.6 は `unity_ObjectToWorldArray`、2018 以降は `unity_Builtins0Array` の構造体配列）が Unity 2022.3 のエンジン側と合う保証がなく、事前の検証が要る
  - バーごとの Renderer を残して `enableInstancing` にする形だと、5412 個の GameObject のカリングと Transform 書き込み（約 0.6ms）が残る
  - Renderer を使わない `Graphics.DrawMeshInstanced` だと、撮影の手動 `Camera.Render` や SceneView に写るかの検証が別に要る
  - 下の手動バッチなら、インスタンシングで得られる削減（ドローコール約 1 万 → 十数回）を同じだけ得られ、上の不確定要素が無い
- **採る方式（手動バッチ）**: 普通の配列 uniform（`float4 _BarPos[1023]`）は両バージョンで同じに動く。`MaterialPropertyBlock.SetVectorArray(int, Vector4[])` は 2.0（Unity 5.6）・2.5（2022.3.62f2）の両方にあることを確認済み。2.0 も追加の手間なく対象に含められる
- 1023 は Unity のシェーダー配列プロパティの要素数の上限。配列長は最初の Set で固定されるため、常に 1023 要素の配列を渡す
- Renderer を実体として残すのは、手動で `Camera.Render` する撮影（撮影ボタン / サムネイル）や SceneView・レイヤー・表示状態（`SetActive`）を今までどおり効かせるため。`Graphics.DrawMesh` はこれらと噛み合わない恐れがある

### 意図した挙動の変更・割り切り

- **カリング**: バッチのメッシュ bounds はエリアローカルで 1000 角の固定値にする（実際の位置はシェーダーが決めるため）。カメラの後ろのエリアも頂点処理されるが、1 バッチ 8184 頂点 × 2 サブメッシュで、最大 30 バッチ程度（手 10000 × 3 本の上限時）でも頂点シェーダーだけの負荷なので許容する。Task 5 の性能測定で確かめる
- **パターン無しの手**: 旧コードは Transform を書かないか、未初期化（ゼロ）の回転を書いていた。新コードは配置位置・配置回転で静止させる（改善として扱う）
- **不透明パスの描画順**: 旧方式はバー単位で前後ソートされていた。新方式はバッチ内でスロット順になる。`PsylliumBatch` は ZWrite On + アルファブレンドなので、cutoff を越えた半透明の縁のにじみ方が変わる可能性がある。Task 5 の画像比較で縁を見る。加算パスは順序に依存しない
- **棒の形状の変更コスト**: 形状（幅・長さ・半径など）を変えると 8184 頂点のメッシュを作り直す（約 300KB の一時確保）。キー境界とスライダー操作時だけなので許容する

### 見た目の等価性

旧シェーダーはバーの `unity_ObjectToWorld` の第 1 列（上方向、親のスケール込み）と平行移動（根元）だけを使い、横方向・前方向はカメラから作り直している。新方式では

- 根元 = エリア行列 × (手の位置 + 手の回転 × バーのローカル位置)
- 上方向 = エリア行列の 3x3 × (手の回転 × バーのローカル回転 × (0, 1, 0))

を計算する。これは旧来の「エリア → 手 → バー」の Transform 階層（手・バーのスケールは 1）と同じ合成になる。エリア行列はバッチの GameObject（エリアの子、単位 Transform）の `unity_ObjectToWorld` でシェーダー側が掛けるので、コントローラーの移動・回転・スケールは今までどおり Transform 階層で即時に効く。

## Global Constraints

- コメント・ログは日本語
- テストから Unity のネイティブ ECall（`Quaternion.Euler` / `AngleAxis` / `Matrix4x4.TRS` / `Mesh` / `Shader.PropertyToID` 等）を呼ばない。`new Quaternion(...)`、`Quaternion * Vector3`、`Quaternion * Quaternion`、`new Vector3/Vector4`、`Vector3.up`、`Mathf` は可
- `ParallelHelper.ForEach` の中（別スレッド）で Unity のネイティブ API を呼ばない。`PsylliumHand.PreUpdateTransform` / `WriteBars` は純粋な計算と配列書き込みだけにする
- .NET 3.5 でもビルドできること（ビルド確認は COM3D2 → COM3D25 の順で両構成）
- `deploy.bat` は実行しない。ビルド確認は MSBuild 直叩き。実機反映はゲーム停止中の `debug.bat com3d25`（Task 5 の restart-verify の手順内だけ）
- se_bundle のシェーダーは Unity 5.6 でビルドする（`build-bundle.bat`）。UnityProject を Unity 2022 化しない・削除しない
- UnityProject 側の旧シェーダー（`Psyllium.shader` / `PsylliumAdd.shader` / `PsylliumVert.cginc`）と `.mat`、`UnityProject/Assets/Scripts/Psyllium*.cs` は変更しない（Unity エディタでのプレビュー用。既にプラグイン側と分岐している）
- 保存形式・XML・シーンプリセットは変えない（version も上げない）

## Review Focus

1. バーの総数がちょうど 1023 の倍数・0 本のエリア → 余計なバッチが増えず、0 本ならバッチ GameObject も無い（Task 2 のテスト + Task 4 の `SyncBatchRenderers`）
2. 再配置（席の間隔・エリアサイズ・本数の変更）でバーが減ったとき → 前回の位置が配列に残って「幽霊バー」が描かれない（Task 2 のテスト `ShrinkClearsStaleSlots`）
3. エリアの表示 OFF・コントローラーの表示 OFF・ライブ演出の同期による非表示 → 何も描かれない（Task 5 の実機確認）
4. 棒の幅・長さ・半径・色・明るさの閾値の変更 → 全バッチへ即時に反映される（Task 5 の実機確認）
5. 撮影ボタン・サムネイル・SceneView ウィンドウ → サイリウムが写る（Task 5 の実機確認）

---

### Task 1: 変更前の基準値を実機で取る

コードは変えない。Task 5 の比較に使う数値と画像を取る。

**Files:** なし（結果は executing-plans の ledger に記録する）

- [ ] **Step 1: 死活確認とタイムラインの特定**

`com3d25-devbridge` の `ping` → `scene_info`。エディタ有効・サイリウム入りタイムラインが読み込まれていることを確かめ、`eval_csharp` で名前を控える（REPL からプラグイン型は直接書けないのでリフレクションで辿る）:

```csharp
var asm = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.Contains("SceneEditor"));
var BF = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy;
var tmType = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.TimelineManager");
var tm = tmType.GetProperty("instance", BF).GetValue(null, null);
var tl = tmType.GetProperty("timeline", BF).GetValue(tm, null);
tl.GetType().GetField("anmName", BF).GetValue(tl) + " / " + tl.GetType().GetField("directoryName", BF).GetValue(tl)
```

サイリウムが無いタイムラインなら、ユーザーにサイリウム入りタイムラインを開いてもらう。`anmName` / `directoryName` を ledger に記録する。

- [ ] **Step 2: 再生中のフレーム時間と更新コスト**

再生中に `profile_add` で `COM3D2.MotionTimelineEditor.Plugin.PsylliumArea:UpdateTransform`（frames 300）を張り、完了後 `profile_read`。続けて `eval_csharp` でフレーム時間を測る（2 回の eval の間を数秒空ける）:

```csharp
int abF = UnityEngine.Time.frameCount; float abT = UnityEngine.Time.realtimeSinceStartup;
```

```csharp
var f = UnityEngine.Time.frameCount - abF; var s = UnityEngine.Time.realtimeSinceStartup - abT; (s * 1000 / f).ToString("F2") + "ms/f"
```

ms/f と `UpdateTransform` の ms/f を ledger に記録する。

- [ ] **Step 3: 固定フレームの画像**

一時停止して同じフレームへ移動し、画面を撮る:

```csharp
tmType.GetMethod("Pause", BF).Invoke(tm, null);
tmType.GetMethod("SeekCurrentFrame", BF).Invoke(tm, new object[] { 600 });
```

次の eval（1 フレーム以上後）で `screenshot` を撮り、保存先を ledger に記録する。サイリウムが画面に入っていなければ、フレーム番号を変えて撮り直し、使った番号を記録する。最後に `tmType.GetMethod("Play", BF).Invoke(tm, null)` で再生へ戻す。

---

### Task 2: バッチ用の純粋データ（配列・バー位置・メッシュ形状）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Timeline\UnityScripts\PsylliumArea.cs" />` の次の行に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/PsylliumBatchDataTests.cs`

**Interfaces:**
- Produces:
  - `public class PsylliumBatchBuffer`
    - `public const int Capacity = 1023;`
    - `public readonly List<Vector4[]> positions;` / `public readonly List<Vector4[]> ups;`（各要素は長さ `Capacity`）
    - `public int barCount { get; }` / `public int batchCount { get; }`
    - `public static int GetBatchCount(int barCount)`
    - `public void SetBarCount(int count)`
    - `public void Write(int barIndex, Vector4 position, Vector4 up)`
  - `public static class PsylliumBarMath`
    - `public static void ComputeLocal(Vector3 handPosition, Quaternion handRotation, Vector3 barPosition, Quaternion barRotation, int colorIndex, out Vector4 position, out Vector4 up)`
  - `public static class PsylliumBatchMesh`
    - `public const int VertexCountPerBar = 8;` / `public const int IndexCountPerBar = 18;`
    - `public static void Build(float halfWidth, float positionY, float barHeight, float barRadius, float topThreshold, int capacity, out Vector3[] vertices, out Vector2[] uv, out Vector2[] uv2, out int[] triangles)`
  - 名前空間はすべて `COM3D2.MotionTimelineEditor.Plugin`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PsylliumBatchDataTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PsylliumBatchDataTests
    {
        private static void AssertVector(Vector4 expected, Vector4 actual)
        {
            Assert.Equal(expected.x, actual.x, 4);
            Assert.Equal(expected.y, actual.y, 4);
            Assert.Equal(expected.z, actual.z, 4);
            Assert.Equal(expected.w, actual.w, 4);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(1023, 1)]
        [InlineData(1024, 2)]
        [InlineData(2046, 2)]
        public void GetBatchCountRoundsUp(int barCount, int expected)
        {
            Assert.Equal(expected, PsylliumBatchBuffer.GetBatchCount(barCount));
        }

        [Fact]
        public void SetBarCountAllocatesFullCapacityArrays()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1030);
            Assert.Equal(2, buffer.batchCount);
            Assert.Equal(1030, buffer.barCount);
            Assert.Equal(PsylliumBatchBuffer.Capacity, buffer.positions[1].Length);
            Assert.Equal(PsylliumBatchBuffer.Capacity, buffer.ups[1].Length);
        }

        [Fact]
        public void SetBarCountZeroRemovesAllBatches()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(2046);
            buffer.SetBarCount(0);
            Assert.Equal(0, buffer.batchCount);
            Assert.Empty(buffer.ups);
        }

        [Fact]
        public void ShrinkClearsStaleSlots()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(5);
            buffer.Write(4, new Vector4(1, 2, 3, 1), new Vector4(0, 1, 0, 1));
            buffer.SetBarCount(3);
            AssertVector(Vector4.zero, buffer.positions[0][4]);
            AssertVector(Vector4.zero, buffer.ups[0][4]);
        }

        [Fact]
        public void ShrinkAcrossBatchClearsTailOfLastBatch()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1023);
            buffer.Write(1022, new Vector4(1, 2, 3, 1), new Vector4(0, 1, 0, 0));
            buffer.SetBarCount(1000);
            Assert.Equal(1, buffer.batchCount);
            AssertVector(Vector4.zero, buffer.positions[0][1022]);
        }

        [Fact]
        public void WriteGoesToBatchAndSlot()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1030);
            var position = new Vector4(1, 2, 3, 1);
            var up = new Vector4(0, 1, 0, 1);
            buffer.Write(1025, position, up);
            AssertVector(position, buffer.positions[1][2]);
            AssertVector(up, buffer.ups[1][2]);
        }

        [Fact]
        public void ComputeLocalWithIdentityAddsOffsets()
        {
            Vector4 position, up;
            PsylliumBarMath.ComputeLocal(
                new Vector3(1, 2, 3), Quaternion.identity,
                new Vector3(0.5f, 0, 0), Quaternion.identity, 1,
                out position, out up);
            AssertVector(new Vector4(1.5f, 2, 3, 1), position);
            AssertVector(new Vector4(0, 1, 0, 1), up);
        }

        [Fact]
        public void ComputeLocalComposesHandAndBarRotation()
        {
            const float h = 0.70710678f;
            var handRotation = new Quaternion(0, h, 0, h); // Y 軸 +90°
            var barRotation = new Quaternion(0, 0, h, h);  // Z 軸 +90°
            Vector4 position, up;
            PsylliumBarMath.ComputeLocal(
                new Vector3(10, 0, 0), handRotation,
                new Vector3(1, 0, 0), barRotation, 0,
                out position, out up);
            // (1,0,0) を Y +90° 回すと (0,0,-1)
            AssertVector(new Vector4(10, 0, -1, 1), position);
            // 上 (0,1,0) を Z +90° で (-1,0,0)、さらに Y +90° で (0,0,1)
            AssertVector(new Vector4(0, 0, 1, 0), up);
        }

        [Fact]
        public void BuildRepeatsBarShapeWithSlotIndex()
        {
            Vector3[] vertices;
            Vector2[] uv;
            Vector2[] uv2;
            int[] triangles;
            PsylliumBatchMesh.Build(0.5f, 0.1f, 2f, 0.3f, 0.2f, 3,
                out vertices, out uv, out uv2, out triangles);

            Assert.Equal(3 * PsylliumBatchMesh.VertexCountPerBar, vertices.Length);
            Assert.Equal(3 * PsylliumBatchMesh.IndexCountPerBar, triangles.Length);

            // 形は全バー共通
            Assert.Equal(vertices[4], vertices[2 * 8 + 4]);
            Assert.Equal(uv[7], uv[2 * 8 + 7]);
            // 先端 (index 4..7) は positionY + barHeight
            Assert.Equal(2.1f, vertices[4].y, 4);
            Assert.Equal(-0.5f, vertices[4].x, 4);
            // uv2.x は太さ方向のオフセット、uv2.y はスロット番号
            Assert.Equal(0.3f, uv2[2 * 8 + 6].x, 4);
            Assert.Equal(-0.3f, uv2[2 * 8 + 0].x, 4);
            Assert.Equal(2f, uv2[2 * 8 + 6].y, 4);
            // 三角形はスロットごとに頂点番号をずらす
            Assert.Equal(new[] { 16, 17, 18 }, new[] { triangles[36], triangles[37], triangles[38] });
            Assert.Equal(2 * 8 + 5, triangles[3 * 18 - 1]);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

テストはビルド済みのプラグイン DLL を参照する。Git Bash で:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter PsylliumBatchDataTests
```

Expected: ビルドエラー（`PsylliumBatchBuffer` などが見つからない）

- [ ] **Step 3: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchData.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// サイリウムのバーをまとめて描くためのシェーダー配列。
    /// 1 バッチ = 1 Renderer で、_BarPos / _BarUp に Capacity 本ぶん詰める
    /// </summary>
    public class PsylliumBatchBuffer
    {
        /// <summary>
        /// シェーダー配列の要素数。Unity の配列プロパティの上限 (1023) に合わせる。
        /// 配列長は最初の SetVectorArray で固定されるため、常にこの長さで渡す
        /// </summary>
        public const int Capacity = 1023;

        /// <summary>xyz = エリアローカルの根元位置、w = 1 なら有効なスロット</summary>
        public readonly List<Vector4[]> positions = new List<Vector4[]>();

        /// <summary>xyz = エリアローカルの上方向、w = 色番号</summary>
        public readonly List<Vector4[]> ups = new List<Vector4[]>();

        public int barCount { get; private set; }

        public int batchCount
        {
            get { return positions.Count; }
        }

        public static int GetBatchCount(int barCount)
        {
            return (barCount + Capacity - 1) / Capacity;
        }

        /// <summary>
        /// 本数を変える。count 以降のスロットは無効 (w = 0) にする。
        /// 前回の位置が残ると、減ったはずのバーが描かれ続けるため
        /// </summary>
        public void SetBarCount(int count)
        {
            if (count < 0)
            {
                count = 0;
            }

            var needed = GetBatchCount(count);
            while (positions.Count < needed)
            {
                positions.Add(new Vector4[Capacity]);
                ups.Add(new Vector4[Capacity]);
            }
            while (positions.Count > needed)
            {
                positions.RemoveAt(positions.Count - 1);
                ups.RemoveAt(ups.Count - 1);
            }

            var end = needed * Capacity;
            for (int i = count; i < end; i++)
            {
                positions[i / Capacity][i % Capacity] = Vector4.zero;
                ups[i / Capacity][i % Capacity] = Vector4.zero;
            }

            barCount = count;
        }

        /// <summary>別スレッドから呼ぶ。バーごとに書き込み先が分かれるのでロックは要らない</summary>
        public void Write(int barIndex, Vector4 position, Vector4 up)
        {
            positions[barIndex / Capacity][barIndex % Capacity] = position;
            ups[barIndex / Capacity][barIndex % Capacity] = up;
        }
    }

    public static class PsylliumBarMath
    {
        /// <summary>
        /// バー 1 本のエリアローカルの根元位置と上方向を求める。
        /// 旧来の「エリア → 手 → バー」の Transform 階層（手・バーのスケールは 1）と同じ合成をする。
        /// 別スレッドから呼ぶため Unity のネイティブ API を使わないこと
        /// </summary>
        public static void ComputeLocal(
            Vector3 handPosition,
            Quaternion handRotation,
            Vector3 barPosition,
            Quaternion barRotation,
            int colorIndex,
            out Vector4 position,
            out Vector4 up)
        {
            var p = handPosition + handRotation * barPosition;
            var u = handRotation * (barRotation * Vector3.up);
            position = new Vector4(p.x, p.y, p.z, 1f);
            up = new Vector4(u.x, u.y, u.z, colorIndex);
        }
    }

    public static class PsylliumBatchMesh
    {
        public const int VertexCountPerBar = 8;
        public const int IndexCountPerBar = 18;

        private static readonly int[] BarTriangles = new int[] {
            0, 1, 2,
            1, 3, 2,
            1, 4, 3,
            4, 5, 3,
            4, 6, 5,
            6, 7, 5,
        };

        /// <summary>
        /// バー capacity 本ぶんの形を重ねたメッシュデータを作る。実際の位置はシェーダーが配列から決める。
        /// uv2.x は太さ方向のオフセット、uv2.y はシェーダー配列のスロット番号
        /// </summary>
        public static void Build(
            float halfWidth,
            float positionY,
            float barHeight,
            float barRadius,
            float topThreshold,
            int capacity,
            out Vector3[] vertices,
            out Vector2[] uv,
            out Vector2[] uv2,
            out int[] triangles)
        {
            var top = positionY + barHeight;
            var baseVertices = new Vector3[] {
                new Vector3(-halfWidth, positionY, 0),
                new Vector3(-halfWidth, positionY, 0),
                new Vector3( halfWidth, positionY, 0),
                new Vector3( halfWidth, positionY, 0),
                new Vector3(-halfWidth, top, 0),
                new Vector3( halfWidth, top, 0),
                new Vector3(-halfWidth, top, 0),
                new Vector3( halfWidth, top, 0),
            };
            var baseUv = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(0, topThreshold),
                new Vector2(1, 0),
                new Vector2(1, topThreshold),
                new Vector2(0, 1 - topThreshold),
                new Vector2(1, 1 - topThreshold),
                new Vector2(0, 1),
                new Vector2(1, 1),
            };
            var baseRadius = new float[] {
                -barRadius, 0, -barRadius, 0, 0, 0, barRadius, barRadius,
            };

            vertices = new Vector3[capacity * VertexCountPerBar];
            uv = new Vector2[capacity * VertexCountPerBar];
            uv2 = new Vector2[capacity * VertexCountPerBar];
            triangles = new int[capacity * IndexCountPerBar];

            for (int slot = 0; slot < capacity; slot++)
            {
                var vertexOffset = slot * VertexCountPerBar;
                for (int i = 0; i < VertexCountPerBar; i++)
                {
                    vertices[vertexOffset + i] = baseVertices[i];
                    uv[vertexOffset + i] = baseUv[i];
                    uv2[vertexOffset + i] = new Vector2(baseRadius[i], slot);
                }

                var indexOffset = slot * IndexCountPerBar;
                for (int i = 0; i < IndexCountPerBar; i++)
                {
                    triangles[indexOffset + i] = vertexOffset + BarTriangles[i];
                }
            }
        }
    }
}
```

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\UnityScripts\PsylliumArea.cs" />` の次に追加:

```xml
    <Compile Include="Timeline\UnityScripts\PsylliumBatchData.cs" />
```

- [ ] **Step 4: 両構成ビルド + テスト**

Git Bash で:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter PsylliumBatchDataTests
```

Expected: 両ビルド成功（MSBuild の DLL コピー警告は可）、PsylliumBatchDataTests 全件 PASS

- [ ] **Step 5: Commit**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchData.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/PsylliumBatchDataTests.cs
```

commit スキルでコミットする（例: `feat(psyllium): バッチ描画用のバー配列とメッシュ形状を追加する`）。

---

### Task 3: バッチ描画用シェーダーと se_bundle の再ビルド

**Files:**
- Create: `UnityProject/Assets/Shaders/PsylliumBatchVert.cginc`
- Create: `UnityProject/Assets/Shaders/PsylliumBatch.shader`
- Create: `UnityProject/Assets/Shaders/PsylliumBatchAdd.shader`
- Create（Unity が生成）: 上記 3 ファイルの `.meta`
- Modify（再ビルド）: `source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle`

**Interfaces:**
- Consumes: Task 2 のメッシュ形状（`uv2.x` = 太さ方向のオフセット、`uv2.y` = スロット番号）と配列の意味（`_BarPos.w` = 有効フラグ、`_BarUp.w` = 色番号）
- Produces: バンドル内のシェーダー `Assets/Shaders/PsylliumBatch.shader`（`MTE/PsylliumBatch`）と `Assets/Shaders/PsylliumBatchAdd.shader`（`MTE/PsylliumBatchAdd`）。`TimelineBundleManager.LoadShader("PsylliumBatch")` / `LoadShader("PsylliumBatchAdd")` で読める。マテリアルのプロパティ名（`_MainTex` / `_Color1a`〜`_Color2c` / `_CutoffAlpha`）は旧シェーダーと同じ

- [ ] **Step 1: 頂点シェーダーの include を書く**

`UnityProject/Assets/Shaders/PsylliumBatchVert.cginc`:

```hlsl
#ifndef PSYLLIUM_BATCH_VERT_INCLUDED
#define PSYLLIUM_BATCH_VERT_INCLUDED

// PsylliumVert.cginc のバッチ描画版。バー 1 本ごとの位置をオブジェクト行列ではなく配列から読む

#include "UnityCG.cginc"

#define PSYLLIUM_BATCH_CAPACITY 1023

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _Color1a;
float4 _Color1b;
float4 _Color1c;
float4 _Color2a;
float4 _Color2b;
float4 _Color2c;
float _CutoffAlpha;

// バッチの親 (エリア) のローカル座標。_BarPos.w = 1 で有効、_BarUp.w = 色番号
float4 _BarPos[PSYLLIUM_BATCH_CAPACITY];
float4 _BarUp[PSYLLIUM_BATCH_CAPACITY];

struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1; // uv2.x = Radius, uv2.y = スロット番号
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1; // uv2.y = 色番号 (frag は旧シェーダーと同じ読み方)
};

v2f vert(appdata v)
{
    v2f o;
    int slot = (int)(v.uv2.y + 0.5);
    float4 barPosData = _BarPos[slot];
    float4 barUpData = _BarUp[slot];

    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.uv2 = float2(v.uv2.x, barUpData.w);

    if (barPosData.w < 0.5)
    {
        // 未使用スロットはクリップ範囲外へ飛ばして描かない
        o.pos = float4(2, 2, 2, 1);
        return o;
    }

    float3 barPos = mul(unity_ObjectToWorld, float4(barPosData.xyz, 1.0)).xyz;
    float3 barUp = mul((float3x3) unity_ObjectToWorld, barUpData.xyz);
    float3 cameraToBar = barPos - _WorldSpaceCameraPos;
    float3 barSide = normalize(cross(barUp, cameraToBar));
    float3 barForward = normalize(cross(barSide, barUp));

    // 旧シェーダーの「行列の列を差し替えて mul」と同じ合成
    float3 vertex = barSide * v.vertex.x + barUp * v.vertex.y + barForward * v.vertex.z + barPos;

    float3 offsetVec = normalize(cross(cameraToBar, barSide));
    vertex += offsetVec * v.uv2.x;

    o.pos = mul(UNITY_MATRIX_VP, float4(vertex, 1.0));
    return o;
}

#endif
```

- [ ] **Step 2: 2 つのシェーダーを書く**

`UnityProject/Assets/Shaders/PsylliumBatch.shader`（旧 `Psyllium.shader` と同じ描画状態・frag、include とシェーダー名だけ違う）:

```hlsl
// 参考文献
// https://qiita.com/kaneta1992/items/af7793e5450b891c2e27

Shader "MTE/PsylliumBatch"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color1a ("Color1a", Color) = (1, 1, 1, 1)
        _Color1b ("Color1b", Color) = (1, 1, 1, 1)
        _Color1c ("Color1c", Color) = (1, 1, 1, 1)
        _Color2a ("Color2a", Color) = (1, 1, 1, 1)
        _Color2b ("Color2b", Color) = (1, 1, 1, 1)
        _Color2c ("Color2c", Color) = (1, 1, 1, 1)
        _CutoffAlpha ("Cutoff Alpha", Float) = 0.5
    }
    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
            "DisableBatching" = "True"
        }

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "PsylliumBatchVert.cginc"

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - _CutoffAlpha);

                fixed4 colorA = lerp(_Color1a, _Color2a, i.uv2.y);
                fixed4 colorB = lerp(_Color1b, _Color2b, i.uv2.y);
                col.rgb = lerp(colorB, colorA, col.r);

                return col;
            }
            ENDCG
        }
    }
}
```

`UnityProject/Assets/Shaders/PsylliumBatchAdd.shader`:

```hlsl
// 参考文献
// https://qiita.com/kaneta1992/items/af7793e5450b891c2e27

Shader "MTE/PsylliumBatchAdd"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color1a ("Color1a", Color) = (1, 1, 1, 1)
        _Color1b ("Color1b", Color) = (1, 1, 1, 1)
        _Color1c ("Color1c", Color) = (1, 1, 1, 1)
        _Color2a ("Color2a", Color) = (1, 1, 1, 1)
        _Color2b ("Color2b", Color) = (1, 1, 1, 1)
        _Color2c ("Color2c", Color) = (1, 1, 1, 1)
        _CutoffAlpha ("Cutoff Alpha", Float) = 0.5
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "DisableBatching" = "True"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Less
            Blend One One
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "PsylliumBatchVert.cginc"

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 colorC = lerp(_Color1c, _Color2c, i.uv2.y);
                col.rgb = colorC.rgb * colorC.a * col.a;
                col.a = 1;

                return col;
            }
            ENDCG
        }
    }
}
```

旧ファイルと同じく UTF-8 BOM 付きで保存する（`head -c3 UnityProject/Assets/Shaders/Psyllium.shader | xxd` で旧ファイルの BOM 有無を確かめて合わせる）。

- [ ] **Step 3: se_bundle を再ビルドする**

Unity エディタで UnityProject を開いていないことを確かめてから、Git Bash で:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
cmd //c build-bundle.bat
```

Expected: `コピーしました: ...\Timeline\se_bundle`。失敗したら `UnityProject/Logs/build-bundle.log` の `Shader error` を探して直す。

- [ ] **Step 4: 生成物を確かめる**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git status --short UnityProject source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
grep -c "PsylliumBatch" UnityProject/Assets/Bundles/se_bundle.manifest
grep -n "Shader error\|error CS" UnityProject/Logs/build-bundle.log | head
```

Expected: 3 ファイル + `.meta` 3 つが新規、`se_bundle` が変更。manifest に `PsylliumBatch` が 3 件以上（2 シェーダー + cginc）。ログにエラーなし。`.meta` 以外に Unity が書き換えたファイル（`ProjectSettings` 等）があれば `git diff` を見て、無関係なら `git checkout` で戻す。

- [ ] **Step 5: Commit**

```bash
git add UnityProject/Assets/Shaders/PsylliumBatchVert.cginc UnityProject/Assets/Shaders/PsylliumBatchVert.cginc.meta \
  UnityProject/Assets/Shaders/PsylliumBatch.shader UnityProject/Assets/Shaders/PsylliumBatch.shader.meta \
  UnityProject/Assets/Shaders/PsylliumBatchAdd.shader UnityProject/Assets/Shaders/PsylliumBatchAdd.shader.meta \
  UnityProject/Assets/Bundles/se_bundle UnityProject/Assets/Bundles/se_bundle.manifest \
  source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
```

`UnityProject/Assets/Bundles/*` が git 管理外なら add から外す（`git ls-files UnityProject/Assets/Bundles` で確かめる）。commit スキルでコミットする（例: `feat(psyllium): 配列で位置を受け取るバッチ描画用シェーダーを追加する`）。

---

### Task 4: 手とバーの GameObject をやめてバッチで描く

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchRenderer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs`（全面書き換え）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/Psyllium.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: Task 2 の `PsylliumBatchBuffer` / `PsylliumBarMath.ComputeLocal` / `PsylliumBatchMesh.Build`、Task 3 のシェーダー名 `PsylliumBatch` / `PsylliumBatchAdd`
- Produces:
  - `public class PsylliumBatchRenderer : MonoBehaviour` — `public void Setup(PsylliumController controller)` / `public void Apply(Vector4[] positions, Vector4[] ups)`
  - `PsylliumController.batchMesh`（`public Mesh`。旧 `meshes` は削除）
  - `PsylliumHand`（MonoBehaviour ではない）— `public PsylliumHand(PsylliumController controller, PsylliumArea area)` / `public void PreUpdateTransform()` / `public void WriteBars(PsylliumBatchBuffer buffer)` / `UpdatePsylliums(...)` の末尾に `int barStartIndex` 引数を追加

このタスクは実機でしか動作を確かめられない（MonoBehaviour・メッシュ・シェーダー）。ビルドが通ることまでを確かめ、見た目と性能は Task 5 で確かめる。

- [ ] **Step 1: `PsylliumBatchRenderer` を追加する**

`source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchRenderer.cs`:

```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// サイリウムのバー最大 PsylliumBatchBuffer.Capacity 本を 1 つの Renderer で描く。
    /// 位置はメッシュではなくシェーダー配列 (_BarPos / _BarUp) で渡す
    /// </summary>
    public class PsylliumBatchRenderer : MonoBehaviour
    {
        private static readonly int BarPosId = Shader.PropertyToID("_BarPos");
        private static readonly int BarUpId = Shader.PropertyToID("_BarUp");

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        public void Setup(PsylliumController controller)
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = controller.batchMesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterials = controller.materials;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            // ライティングを使わないシェーダーなので、プローブの補間を省く
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _block = new MaterialPropertyBlock();
        }

        public void Apply(Vector4[] positions, Vector4[] ups)
        {
            _block.SetVectorArray(BarPosId, positions);
            _block.SetVectorArray(BarUpId, ups);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
```

- [ ] **Step 2: `PsylliumHand` を素のクラスにする**

`source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs` を次で置き換える（`PreUpdateTransform` の計算式と `UpdatePsylliums` のバー配置の式は旧コードのまま。パターンが無い手だけ「配置位置に置く」へ変える。旧コードは Transform を書かずに放置していたため、新規の手はエリア原点に出ていた）:

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 観客 1 人の片手。GameObject は持たず、持っているバーの位置をエリアのバッチ配列へ書き込む
    /// </summary>
    public class PsylliumHand
    {
        public const int MAX_PSYLLIUM_COUNT = 3;

        public readonly PsylliumController controller;
        public readonly PsylliumArea area;
        public int patternIndex;
        public int timeIndex;
        public float timeShiftParam;
        public int randomPositionIndex;
        public int randomRotationIndex;
        public Vector3 basePosition;
        public Quaternion placementRotation = Quaternion.identity;
        public bool isLeftHand;

        /// <summary>エリア内でのバーの通し番号の先頭。バッチ配列の書き込み位置になる</summary>
        public int barStartIndex;
        public int barCount;

        private Vector3[] _barPositions = new Vector3[MAX_PSYLLIUM_COUNT];
        private Quaternion[] _barRotations = new Quaternion[MAX_PSYLLIUM_COUNT];
        private int[] _colorIndexes = new int[MAX_PSYLLIUM_COUNT];

        public PsylliumHand(PsylliumController controller, PsylliumArea area)
        {
            this.controller = controller;
            this.area = area;
        }

        public PsylliumBarConfig barConfig
        {
            get
            {
                return controller.barConfig;
            }
        }

        public PsylliumHandConfig handConfig
        {
            get
            {
                return controller.handConfig;
            }
        }

        public PsylliumPattern pattern
        {
            get
            {
                return controller.GetPattern(patternIndex);
            }
        }

        public PsylliumPatternConfig patternConfig
        {
            get
            {
                if (pattern == null) return null;
                return pattern.patternConfig;
            }
        }

        private Vector3 _calculatedPosition;
        private Quaternion _calculatedRotation = Quaternion.identity;

        /// <summary>別スレッドから呼ぶ。Unity のネイティブ API を使わないこと</summary>
        public void PreUpdateTransform()
        {
            if (controller == null || area == null)
            {
                return;
            }

            var pattern = this.pattern;
            if (pattern == null)
            {
                // パターンが無い手は揺らさず配置位置に置く
                _calculatedPosition = basePosition;
                _calculatedRotation = placementRotation;
                return;
            }

            var patternConfig = pattern.patternConfig;
            var timeShift = patternConfig.timeShiftMin + (patternConfig.timeShiftMax - patternConfig.timeShiftMin) * timeShiftParam;
            var timeIndex = this.timeIndex + (int)(controller.time * timeShift);

            var position = pattern.GetAnimationPosition(timeIndex, isLeftHand);
            var rotation = pattern.GetAnimationRotation(timeIndex, isLeftHand);

            var randomPosition = pattern.GetRandomAnimationPosition(randomPositionIndex);
            var randomRotation = pattern.GetRandomAnimationRotation(randomRotationIndex);

            position = basePosition + placementRotation * (position + randomPosition);
            rotation = placementRotation * rotation * randomRotation;

            _calculatedPosition = position;
            _calculatedRotation = rotation;
        }

        /// <summary>別スレッドから呼ぶ。PreUpdateTransform の結果からバーの位置をバッチ配列へ書く</summary>
        public void WriteBars(PsylliumBatchBuffer buffer)
        {
            for (int j = 0; j < barCount; j++)
            {
                Vector4 position, up;
                PsylliumBarMath.ComputeLocal(
                    _calculatedPosition, _calculatedRotation,
                    _barPositions[j], _barRotations[j], _colorIndexes[j],
                    out position, out up);
                buffer.Write(barStartIndex + j, position, up);
            }
        }

        public void UpdatePsylliums(
            Vector3 handPos,
            int count,
            int patternIndex,
            int timeIndex,
            float timeShiftParam,
            int[] colorIndexes,
            int randomPositionIndex,
            int randomRotationIndex,
            bool isLeftHand,
            int barStartIndex)
        {
            this.basePosition = handPos;
            this.isLeftHand = isLeftHand;
            this.patternIndex = patternIndex;
            this.timeIndex = timeIndex;
            this.timeShiftParam = timeShiftParam;
            this.randomPositionIndex = randomPositionIndex;
            this.randomRotationIndex = randomRotationIndex;
            this.barStartIndex = barStartIndex;

            EnsureBarCapacity(count);
            barCount = count;

            for (int j = 0; j < count; j++)
            {
                var barPosition = (j - (count - 1) * 0.5f) * handConfig.barOffsetPosition * barConfig.baseScale;
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

                _barPositions[j] = barPosition;
                _barRotations[j] = barRotation;
                _colorIndexes[j] = colorIndexes[j];
            }
        }

        private void EnsureBarCapacity(int count)
        {
            if (_barPositions.Length >= count)
            {
                return;
            }

            _barPositions = new Vector3[count];
            _barRotations = new Quaternion[count];
            _colorIndexes = new int[count];
        }
    }
}
```

実装前に旧ファイルの `PreUpdateTransform` / `UpdatePsylliums` と上の式を見比べ、ずれていれば旧コードの式に合わせる（このステップは計算式を変えない）。

- [ ] **Step 3: `PsylliumArea` をバッチ化する**

`source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs` を次のとおり変える。

フィールド（`public List<PsylliumHand> hands;` と `private int _handCurrentIndex;` の周辺）:

```csharp
        public List<PsylliumHand> hands = new List<PsylliumHand>();
        public bool refreshRequired;
    
        private int _handCurrentIndex;

        // Refresh 中に振るバーの通し番号。最後にバッチ配列の本数になる
        private int _barCount;
        private readonly PsylliumBatchBuffer _batchBuffer = new PsylliumBatchBuffer();
        private readonly List<PsylliumBatchRenderer> _batchRenderers = new List<PsylliumBatchRenderer>();
```

`Initialize()`（OnEnable でも呼ばれる。手はもう子の GameObject ではないので集め直さない。再表示で手を消すと配置が失われる）:

```csharp
        public void Initialize()
        {
            if (hands == null)
            {
                hands = new List<PsylliumHand>();
            }
            UpdateName();
        }
```

`UpdateTransform()`:

```csharp
        public void UpdateTransform()
        {
            var buffer = _batchBuffer;
#if COM3D2
            ParallelHelper.ForEach(hands, hand =>
            {
                hand.PreUpdateTransform();
                hand.WriteBars(buffer);
            });
#else
            foreach (var hand in hands)
            {
                hand.PreUpdateTransform();
                hand.WriteBars(buffer);
            }
#endif

            var count = Mathf.Min(_batchRenderers.Count, buffer.batchCount);
            for (int i = 0; i < count; i++)
            {
                _batchRenderers[i].Apply(buffer.positions[i], buffer.ups[i]);
            }
        }
```

`GetOrAddHand(int index)` の GameObject 生成部分:

```csharp
            while (hands.Count <= index)
            {
                if (hands.Count >= MAX_PSYLLIUM_HAND_COUNT)
                {
                    Debug.LogError("Too many hands: " + hands.Count);
                    return null;
                }

                hands.Add(new PsylliumHand(controller, this));
            }

            return hands[index];
```

`RemoveUnusedHands()`:

```csharp
        public void RemoveUnusedHands()
        {
            var keepCount = _handCurrentIndex + 1;
            if (hands.Count > keepCount)
            {
                hands.RemoveRange(keepCount, hands.Count - keepCount);
            }
        }
```

`Refresh()` の `_handCurrentIndex = -1;` の直後に `_barCount = 0;` を足し、末尾の `RemoveUnusedHands(); UpdateTransform();` を次にする:

```csharp
            RemoveUnusedHands();
            _batchBuffer.SetBarCount(_barCount);
            SyncBatchRenderers();
            UpdateTransform();
```

`Refresh()` の後ろに追加:

```csharp
        /// <summary>バッチ配列の数に合わせて描画用の子 GameObject を増減する</summary>
        private void SyncBatchRenderers()
        {
            var needed = _batchBuffer.batchCount;
            while (_batchRenderers.Count < needed)
            {
                var obj = new GameObject("PsylliumBatch");
                obj.layer = gameObject.layer;
                obj.transform.SetParent(transform, false);

                var batchRenderer = obj.AddComponent<PsylliumBatchRenderer>();
                batchRenderer.Setup(controller);
                _batchRenderers.Add(batchRenderer);
            }

            while (_batchRenderers.Count > needed)
            {
                var last = _batchRenderers[_batchRenderers.Count - 1];
                _batchRenderers.RemoveAt(_batchRenderers.Count - 1);
                // Destroy はフレーム末まで遅れるため、このフレームに古い配列で描かれないよう先に隠す
                last.gameObject.SetActive(false);

                if (Application.isPlaying)
                {
                    Destroy(last.gameObject);
                }
                else
                {
                    DestroyImmediate(last.gameObject);
                }
            }
        }
```

`RefreshSeat` の 2 か所の `hand.UpdatePsylliums(...)` に通し番号を渡して進める:

```csharp
            if (randomValues.leftCount > 0)
            {
                var hand = GetOrCreateHand();
                if (hand == null) return false;
                hand.placementRotation = rotation;
                hand.UpdatePsylliums(basePosition + spacing, randomValues.leftCount,
                    randomValues.patternIndex, randomValues.timeIndex, randomValues.timeShiftParam,
                    randomValues.leftColorIndexes, randomValues.leftRandomPositionIndex,
                    randomValues.leftRandomRotationIndex, true, _barCount);
                _barCount += randomValues.leftCount;
            }
            if (randomValues.rightCount > 0)
            {
                var hand = GetOrCreateHand();
                if (hand == null) return false;
                hand.placementRotation = rotation;
                hand.UpdatePsylliums(basePosition - spacing, randomValues.rightCount,
                    randomValues.patternIndex, randomValues.timeIndex, randomValues.timeShiftParam,
                    randomValues.rightColorIndexes, randomValues.rightRandomPositionIndex,
                    randomValues.rightRandomRotationIndex, false, _barCount);
                _barCount += randomValues.rightCount;
            }
```

`using System.Linq;` は `GetComponentsInChildren(...).ToList()` が消えて不要になるなら削除する（ほかで使っていればそのまま）。

- [ ] **Step 4: `PsylliumController` をバッチ用メッシュ・新シェーダーへ切り替える**

`source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs`:

1. フィールド `public Mesh[] meshes;` を `public Mesh batchMesh;` に置き換える
2. `CreateMaterial` を次にする（引数はシェーダー名）:

```csharp
        private Material CreateMaterial(string shaderName)
        {
#if COM3D2
            var shader = bundleManager.LoadShader(shaderName);
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            // Unity 5.6 のマテリアルは手書きできないため新シェーダー用の .mat は作らず、
            // テクスチャは旧マテリアルのものを流用する
            var textureSource = bundleManager.LoadMaterial("Psyllium");
            if (textureSource != null)
            {
                material.mainTexture = textureSource.mainTexture;
                Destroy(textureSource);
            }
#else
            var material = new Material(Shader.Find("MTE/" + shaderName));
            material.SetTexture("_MainTex", Resources.Load<Texture2D>("psyllium"));
#endif
            return material;
        }
```

3. `Initialize()` のマテリアル・メッシュ生成を次にする:

```csharp
            if (materials == null || materials.Length == 0)
            {
                materials = new Material[2];
                materials[0] = CreateMaterial("PsylliumBatch");
                materials[1] = CreateMaterial("PsylliumBatchAdd");
            }

            if (batchMesh == null)
            {
                batchMesh = new Mesh();
            }
```

4. `UpdateMaterials()` はそのまま（`foreach (var material in materials)` の中で null を飛ばすよう `if (material == null) continue;` を先頭に足す。シェーダーが読めなかったときに NRE で他の処理を止めないため）
5. 作業配列（`_meshVertices` / `_meshUv` / `_meshUv2` / `MeshTriangles` とそのコメント）と `UpdateMesh(int colorIndex)` を削除し、`UpdateMeshs()` を次にする:

```csharp
        // エリアのローカル座標で客席全体を十分に覆う大きさ。
        // 頂点はバー 1 本の形を重ねただけで、実際の位置はシェーダーが配列から決めるため
        private const float BatchBoundsSize = 1000f;

        public void UpdateMeshs()
        {
            Vector3[] vertices;
            Vector2[] uv;
            Vector2[] uv2;
            int[] triangles;
            PsylliumBatchMesh.Build(
                barConfig.width * 0.5f * barConfig.baseScale,
                barConfig.positionY * barConfig.baseScale,
                barConfig.height * barConfig.baseScale,
                barConfig.radius * barConfig.baseScale,
                barConfig.topThreshold,
                PsylliumBatchBuffer.Capacity,
                out vertices, out uv, out uv2, out triangles);

            var mesh = batchMesh;
            mesh.Clear();

            mesh.subMeshCount = 2;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = uv2;

            mesh.SetTriangles(triangles, 0);
            mesh.SetTriangles(triangles, 1);

            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BatchBoundsSize);
        }
```

6. `grep -n "meshes\|UpdateMesh(" source/COM3D2.SceneEditor.Plugin -r --include=*.cs` で残りの参照が無いことを確かめる

- [ ] **Step 5: `Psyllium.cs` を削除する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git rm source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/Psyllium.cs
```

`COM3D2.SceneEditor.Plugin.csproj` から `<Compile Include="Timeline\UnityScripts\Psyllium.cs" />` を消し、`<Compile Include="Timeline\UnityScripts\PsylliumBatchData.cs" />` の次に追加:

```xml
    <Compile Include="Timeline\UnityScripts\PsylliumBatchRenderer.cs" />
```

`grep -rn "\bPsyllium>" source/COM3D2.SceneEditor.Plugin --include=*.cs` で `Psyllium` コンポーネントへの参照が残っていないことを確かめる。

- [ ] **Step 6: 両構成ビルド + 全テスト**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

Expected: 両ビルド成功、テスト全件 PASS（Psyllium 系の既存テスト `PsylliumPlacementTests` / `PsylliumRefreshKindTests` を含む）

- [ ] **Step 7: Commit**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumBatchRenderer.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumHand.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
```

（`Psyllium.cs` の削除は Step 5 の `git rm` でステージ済み）。commit スキルでコミットする（例: `perf(psyllium): バーを 1023 本ずつ 1 つの Renderer でまとめて描く`）。

---

### Task 5: 実機で見た目と性能を確かめる

**Files:** 不具合があれば Task 4 のファイルを直す

- [ ] **Step 1: DLL を実機へ反映して再起動する**

MonoBehaviour の構成が変わるためホットリロードはできない。`com3d25-devbridge:restart-verify` スキルの手順で、ゲーム終了 → 停止中に `debug.bat com3d25` → 起動 → セーブロード → デイリー画面でエディタ有効化まで進める（撮影モードは非対応。通常シーンで検証する）。

- [ ] **Step 2: Task 1 と同じタイムラインを読み、固定フレームを撮って比べる**

Task 1 Step 1 の `asm` / `BF` / `tmType` / `tm` 取得を `eval_csharp` で再実行し、`tmType.GetMethod("LoadTimeline", BF).Invoke(tm, new object[] { "<anmName>", "<directoryName>" })` で読み込む。Task 1 Step 3 と同じ手順（Pause → 同じフレーム番号へ Seek → 次の eval で `screenshot`）で撮る。

Task 1 の画像と並べて見比べる。バーの位置・向き・色・加算の光り方が同じであること。バーが重なる箇所の縁のにじみ方も見る（描画順の変化。「背景」の「意図した挙動の変更」参照）。目立つ差があればユーザーに画像を見せて判断を仰ぐ。動画プレイヤーなど時間で変わる要素の差は無視してよい。違いがあれば、まず `PsylliumBarMath.ComputeLocal` の合成順と Task 3 の頂点シェーダーの式を旧シェーダー（`PsylliumVert.cginc`）と見比べる。

- [ ] **Step 3: 性能を測る**

再生へ戻し、Task 1 Step 2 と同じ手順で ms/f と `PsylliumArea:UpdateTransform` を測る。加えて、バッチ描画の寄与を A/B で測る（切って測り、必ず戻す）:

```csharp
var batchT = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.PsylliumBatchRenderer");
var batchRs = UnityEngine.Object.FindObjectsOfType(batchT).Select(o => ((UnityEngine.Component)o).GetComponent<UnityEngine.Renderer>()).ToArray();
foreach (var r in batchRs) r.enabled = false;
int abF = UnityEngine.Time.frameCount; float abT = UnityEngine.Time.realtimeSinceStartup;
batchRs.Length
```

数秒後:

```csharp
var f = UnityEngine.Time.frameCount - abF; var s = UnityEngine.Time.realtimeSinceStartup - abT;
foreach (var r in batchRs) r.enabled = true;
(s * 1000 / f).ToString("F2") + "ms/f, 戻した本数=" + batchRs.Count(r => r.enabled)
```

Expected（合格基準）:
- バッチ Renderer の数 = 各エリアの `ceil(バー数 / 1023)` の合計（2026-10-06 のシーンなら 8 前後）
- バッチ描画の A/B 差が 1ms/f 未満（旧: 約 4.2ms/f）
- `PsylliumArea.UpdateTransform` の 4 エリア合計が旧値（約 1.83ms/f）以下
- フレーム全体が Task 1 の値より 3ms/f 以上短い

基準を満たさなければ、どの項目が外れたかを ledger に残してユーザーに報告する（基準を下げて先へ進まない）。

- [ ] **Step 4: Review Focus の動作確認**

ライブ演出ウィンドウ（サイリウム）で操作し、`screenshot` で確かめる。確認のたびに値を元へ戻す。

1. エリアの表示 OFF → そのエリアのバーが消える。ON で戻る。コントローラーの表示 OFF も同様
2. 席の間隔を広げて本数を減らす → 減ったバーが残らない（幽霊バーが無い）。元に戻すと本数も戻る
3. 棒の幅・長さ・半径・色（1a〜2c）を変える → 全エリアのバーへ即時に反映される
4. 撮影ボタンで 1 枚撮る → 出力画像にサイリウムが写っている。SceneView ウィンドウを開いている場合はそこにも写る
5a. （code-review で追加）SceneView でメイド・モデルをクリック → サイリウムのバッチではなくクリックした物が選ばれる。Hierarchy からエリア・コントローラーを選んでフォーカスしても、カメラが遠くへ飛ばない
5b. （code-review で追加）ステージライトのビーム・PNG 板とサイリウムが重なる構図 → 加算の光が不自然に消えたり、奥の光が手前に乗ったりしない（バッチの bounds 中心がエリア原点のため Transparent キュー内の並びが変わる）。目立つ場合はユーザーに画像を見せて判断を仰ぐ
5. パターンを 0 個にする（またはパターン未割り当ての手がある状態）→ バーが配置位置に静止して表示され、エラーログが出ない（`tail_log` で確認）
6. 通し番号の整合（2 の前後で実行）。各エリアで、手の `barStartIndex` が直前の手の `barStartIndex + barCount` と一致し、最後の手の末尾がバッファの `barCount` と一致すること:

```csharp
var areaT = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.PsylliumArea");
var handT = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.PsylliumHand");
var bufT = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.PsylliumBatchBuffer");
string.Join("\n", UnityEngine.Object.FindObjectsOfType(areaT).Select(a => {
    var hands = ((System.Collections.IEnumerable)areaT.GetField("hands", BF).GetValue(a)).Cast<object>().ToList();
    var buf = areaT.GetField("_batchBuffer", BF).GetValue(a);
    int next = 0, broken = 0;
    foreach (var h in hands) {
        var start = (int)handT.GetField("barStartIndex", BF).GetValue(h);
        if (start != next) broken++;
        next = start + (int)handT.GetField("barCount", BF).GetValue(h);
    }
    return ((UnityEngine.Object)a).name + " hands=" + hands.Count + " end=" + next + " buffer=" + bufT.GetProperty("barCount", BF).GetValue(buf, null) + " broken=" + broken;
}).ToArray())
```

Expected: 全エリアで `broken=0` かつ `end == buffer`

- [ ] **Step 5: 修正があればコミット**

Step 2〜4 で直したファイルを add し、commit スキルでコミットする（例: `fix(psyllium): <直した内容>`）。修正が無ければこのステップは飛ばす。

---

## 範囲外

- COM3D2 (2.0) の実機確認（devbridge は 2.5 専用）。両構成のビルドが通ることまでを確かめる。2.0 でも同じ 5.6 製シェーダーと同じ API（`SetVectorArray(int, Vector4[])` は 2.0 の UnityEngine.dll に存在することを確認済み）を使う
- `PreUpdateTransform` の計算そのものの高速化（パターンのアニメーションをシェーダーで計算するなど）。本計画で描画と Transform 書き込みを落とした後、なお重ければ別計画にする
- UnityProject（Unity エディタのプレビュー）側のスクリプトのバッチ化

## レビュー却下メモ

- `Build` の出力配列の再利用、形状を uniform 化してメッシュ再生成を無くす — 形状変更はキー境界とスライダー操作時だけで、一時確保（約 300KB）は許容範囲。「背景」に割り切りとして明記した
- エリア非表示中は `SetVectorArray` を省く — 削減は小さい。ポーズ編集中（`ApplyPlayData` が止まる）に再表示すると古い配列が残る恐れがあるため、毎フレーム渡す今の形を保つ
- `barStartIndex` の割り当てを純粋関数へ切り出してテストする — 割り当ては `RefreshSeat` の 2 行だけなので、代わりに Task 5 Step 4 の 6 で実機の整合チェックを加えた
- Task 1 の devbridge 手順の名前（`TimelineManager.instance` / `timeline` / `anmName` / `directoryName`）が未検証 — 計画作成時のセッションで同じ eval を実機で実行済み（誤検知）
- COM3D2 構成でテストが通る保証がない — テストは COM3D25 の DLL を参照する既存の構成で、対象は純粋ロジックのため変更しない
