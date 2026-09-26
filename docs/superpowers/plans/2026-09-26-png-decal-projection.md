# PNG配置のデカール投影 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このワークスペースでは subagent-driven-development を使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PNG配置オブジェクトに「デカール」表示タイプを追加し、背景・床・壁へ画像を投影できるようにする。

**Architecture:**
- **描画**
  - PNG 配置の root の下に、Unity 標準 `Projector` を持つ子 `PngDecal` を遅延生成する
  - 表示タイプ（板 / デカール）によって `PngQuad` と `PngDecal` を切り替える
  - 描く位置は、C# で計算した「ワールド → 投影箱」行列をシェーダー `SE/Decal` に渡して決める。Projector は描く物体の選別（カリング）にだけ使う
- **保存**: Undo・シーンプリセット・タイムラインは、PNG 配置の既存経路に項目を追加して扱う
- **計算**: 投影箱の計算は純粋関数 `PngDecalProjection` にまとめ、単体テストする

**Tech Stack:**
- C#（プラグイン: COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1、テスト: xUnit net48）
- Unity Built-in RP の `Projector`
- シェーダー: Unity 5.6.4f1 でビルドする `se_bundle`
- 実機検証: com3d25-devbridge

**Spec:** `docs/superpowers/specs/2026-09-26-png-decal-projection-design.md`

## Global Constraints

**コードの規則**
- 思考は英語、コメントとエラーログは日本語で書く
- 数値やパスの直書きは避け、名前付き定数にする。レイヤー番号は `LayerMask.NameToLayer` で名前から引く
- 表示タイプの値: `PngDisplayType.Board = 0` / `Decal = 1`
- ブレンドの値: `PngDecalBlendMode.Normal = 0` / `Multiply = 1` / `Additive = 2`
- デカール設定の既定値: `decalFadeAngle = 80`、`decalProjectOnMaids = false`
- 角度フェードは 1〜90 度に丸める。フェード開始比は 2/3
- シェーダー名は `SE/Decal`。ファイルは `UnityProject/Assets/Shaders/Decal.shader` で、Queue は `Transparent-500`
- 板のときは、表示タイプとデカール設定をプリセット XML にもタイムライン XML にも書き出さない（`ShouldSerialize*`）
- `ScenePresetData.CurrentVersion` は 35→36 に上げる。`TimelineData.CurrentVersion` は変えない
- COM3D2 構成は .NET 3.5 なので、入力 5 個以上の `Func<>` / `Action<>` は使わない
- UnityProject 側のコードは C# 4 の制約を受ける。ただし今回はシェーダーだけで、C# は追加しない
- テストでは Unity のネイティブ呼び出し（`Quaternion.Euler`、`Matrix4x4.TRS` など）を使わない。行列はフィールドを直接組み立てる

**実行禁止のコマンド**
- `deploy.bat` / `deploy.ps1`
- `debug.bat`（実機へ DLL をコピーしてしまうため）
- git worktree（使わない）

**ビルド手順（BUILD）** — 各タスクで「BUILD を実行」と書いた箇所では、次をそのまま実行する（Git Bash）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m -nologo /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" && \
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m -nologo /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

- 期待する結果: 両構成とも `0 エラー`
- 順番は COM3D2 → COM3D25 にする。COM3D2 のビルドは `bin/Debug/COM3D25/` を消すため、テストが参照する COM3D25 の DLL を最後に作る必要がある

**テスト手順（TEST <filter>）** — BUILD の後に実行する:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin.Tests
dotnet test -nologo -v q --filter "FullyQualifiedName~<filter>"
```

- フィルターを外せば、全テストを実行する

**コミット** は commit スキルで行う（Conventional Commits、日本語）。

## Review Focus

1. **タイムライン読込で XML が消える**: `Setup` に渡すのは `timeline.pngObjects` そのものだが、途中の `UpdateTimelineData` が同じリストを消して書き直す。XML の設定が失われず、読込後の実体に反映されること（Task 6。複製の検証を実機手順に含める）
2. **renderQueue の要素が無い XML**: 0 を適用すると描画順が壊れる。0 以下なら既定のまま残ること（Task 6 のテスト）
3. **範囲外の整数値**: タイムライン XML の `DisplayType=5` などは、例外にせず板や通常として読むこと（Task 6 のテスト）
4. **拡縮が 0 や負のギズモ操作**: 投影箱の計算が Infinity や NaN にならず、負の拡縮でも Projector が箱を覆うこと（Task 1 のテスト）
5. **板の保存内容が変わる回帰**: 板だけのシーンでは、プリセット XML にもタイムライン XML にも新しい属性や要素が一切出ないこと（Task 5・Task 6 のテスト）

---

## File Structure

| ファイル | 区分 | 役割 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/PngDecalProjection.cs` | 新規 | 投影箱の純粋計算（アスペクト比、Projector のフレーム、ワールド → 箱の行列、箱の頂点、フェードの cos 範囲） |
| `source/COM3D2.SceneEditor.Plugin.Tests/PngDecalProjectionTests.cs` | 新規 | 上記のテスト |
| `UnityProject/Assets/Shaders/Decal.shader`（+ Unity が作る `.meta`） | 新規 | デカールのシェーダー |
| `source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle` | 更新 | 再ビルドしたバンドル |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineBundleManager.cs` | 変更 | `LoadShader` を追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` | 変更 | 列挙型、データのフィールド、デカールの生成・更新・破棄、セッター、改訂カウンター |
| `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs` | 変更 | 表示タイプの切り替えと、デカール用の欄 |
| `source/COM3D2.SceneEditor.Plugin/PngPlacementWindow.cs` | 変更 | 「配置済み」一覧の `[デカール]` 表示 |
| `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` | 変更 | 投影箱のワイヤーと矢印 |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 変更 | プリセット DTO の属性とバージョン |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs` | 変更 | 捕捉・適用・比較 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs` | 新規 | プリセット DTO のテスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs` | 変更 | バージョンの期待値 35 → 36 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | 変更 | `TimelinePngObjectXml` の要素 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | 変更 | `TimelinePngObjectData` のフィールドと変換 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs` | 変更 | 書き戻しと読込時の適用 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelinePngDecalXmlTests.cs` | 新規 | タイムライン XML のテスト |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | `PngDecalProjection.cs` の `<Compile>` を追加 |
| `W:\COM3D2_5\work\CLAUDE.md` | 変更 | 互換リストに 1 行を追加（リポジトリ外） |

---

### Task 1: 投影箱の純粋計算 `PngDecalProjection`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/PngDecalProjection.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Manager\PngPlacementManager.cs" />` の直前に 1 行追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/PngDecalProjectionTests.cs`

**Interfaces:**
- Produces（名前空間 `COM3D2.SceneEditor.Plugin`）:
  - `public static class PngDecalProjection`
  - 定数: `DefaultFadeAngle = 80f`、`MinFadeAngle = 1f`、`MaxFadeAngle = 90f`、`CullMargin = 0.01f`、`MinScale = 0.0001f`
  - `public struct ProjectorFrame { float orthographicSize, aspectRatio, nearClipPlane, farClipPlane; Vector3 localPosition, localScale; }`
  - `Vector2 GetAspect(int width, int height)`
  - `ProjectorFrame ComputeFrame(Vector2 aspect, Vector3 rootScale)`
  - `Matrix4x4 ComputeDecalMatrix(Matrix4x4 rootWorldToLocal, Vector2 aspect)`
  - `void GetBoxCorners(Matrix4x4 rootLocalToWorld, Vector2 aspect, Vector3[] corners)`（並びは `PluginUtils.GetBoundsCorners` と同じ。bit0=X、bit1=Y、bit2=Z）
  - `float ClampFadeAngle(float fadeAngle)`
  - `Vector2 GetFadeCosRange(float fadeAngle)`（x = 不透明度 0 になる cos、y = 不透明度 1 になる cos）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PngDecalProjectionTests.cs`:

```csharp
using System;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// デカールの投影箱の計算を固定する。
    /// 投影箱は root ローカルで X ±aspect.x/2・Y ±aspect.y/2・Z ±0.5 の箱
    /// </summary>
    public class PngDecalProjectionTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertNear(float expected, float actual)
        {
            Assert.True(Math.Abs(expected - actual) < Tolerance,
                string.Format("expected {0} but was {1}", expected, actual));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            AssertNear(expected.x, actual.x);
            AssertNear(expected.y, actual.y);
            AssertNear(expected.z, actual.z);
        }

        private static void AssertFinite(float value)
        {
            Assert.False(float.IsNaN(value) || float.IsInfinity(value), "value = " + value);
        }

        /// <summary>root が position に平行移動し、各軸 scale で拡縮した worldToLocal (回転なし)</summary>
        private static Matrix4x4 WorldToLocal(Vector3 position, float scale)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f / scale;
            m.m11 = 1f / scale;
            m.m22 = 1f / scale;
            m.m03 = -position.x / scale;
            m.m13 = -position.y / scale;
            m.m23 = -position.z / scale;
            return m;
        }

        [Fact]
        public void 横長の画像は長辺を1にして短辺を比率にする()
        {
            var aspect = PngDecalProjection.GetAspect(200, 100);
            AssertNear(1f, aspect.x);
            AssertNear(0.5f, aspect.y);
        }

        [Fact]
        public void 縦長の画像は高さを1にする()
        {
            var aspect = PngDecalProjection.GetAspect(100, 400);
            AssertNear(0.25f, aspect.x);
            AssertNear(1f, aspect.y);
        }

        [Fact]
        public void 大きさが0の画像は正方形として扱う()
        {
            var aspect = PngDecalProjection.GetAspect(0, 100);
            AssertNear(1f, aspect.x);
            AssertNear(1f, aspect.y);
        }

        [Fact]
        public void 等倍の箱は余白込みで覆いProjectorを表の面の手前に置く()
        {
            var frame = PngDecalProjection.ComputeFrame(new Vector2(1f, 0.5f), Vector3.one);

            var m = PngDecalProjection.CullMargin;
            AssertNear(0.25f + m, frame.orthographicSize);
            AssertNear((0.5f + m) / (0.25f + m), frame.aspectRatio);
            AssertNear(m * 0.5f, frame.nearClipPlane);
            AssertNear(1f + m * 2f, frame.farClipPlane);
            AssertNear(new Vector3(0f, 0f, 0.5f + m), frame.localPosition);
            AssertNear(Vector3.one, frame.localScale);
        }

        [Fact]
        public void 拡縮した箱はワールド寸法で覆い子の拡縮で打ち消す()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(2f, 4f, 3f));

            var m = PngDecalProjection.CullMargin;
            AssertNear(2f + m, frame.orthographicSize);
            AssertNear((1f + m) / (2f + m), frame.aspectRatio);
            AssertNear(3f + m * 2f, frame.farClipPlane);
            // root ローカルの位置は root の拡縮で伸びるため、ワールドで 1.5 + m になるよう割り戻す
            AssertNear((1.5f + m) / 3f, frame.localPosition.z);
            AssertNear(new Vector3(0.5f, 0.25f, 1f / 3f), frame.localScale);
        }

        [Fact]
        public void 負の奥行きでもProjectorは表側の外に立つ()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(1f, 1f, -2f));

            var m = PngDecalProjection.CullMargin;
            // ワールドでの位置 = root の拡縮 × ローカル位置。符号が打ち消し合って表側 (+1 + m) になる
            AssertNear(1f + m, -2f * frame.localPosition.z);
            AssertNear(2f + m * 2f, frame.farClipPlane);
            AssertNear(-0.5f, frame.localScale.z);
        }

        [Fact]
        public void 拡縮0でも有限値を返す()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(0f, 0f, 0f));

            AssertFinite(frame.orthographicSize);
            AssertFinite(frame.aspectRatio);
            AssertFinite(frame.farClipPlane);
            AssertFinite(frame.localPosition.z);
            AssertFinite(frame.localScale.x);
            AssertFinite(frame.localScale.y);
            AssertFinite(frame.localScale.z);
            Assert.True(frame.orthographicSize > 0f);
            Assert.True(frame.aspectRatio > 0f);
        }

        [Fact]
        public void 行列は箱の端を0_5へ写す()
        {
            var matrix = PngDecalProjection.ComputeDecalMatrix(Matrix4x4.identity, new Vector2(1f, 0.5f));

            AssertNear(new Vector3(0.5f, 0.5f, 0.5f), matrix.MultiplyPoint3x4(new Vector3(0.5f, 0.25f, 0.5f)));
            AssertNear(Vector3.zero, matrix.MultiplyPoint3x4(Vector3.zero));
        }

        [Fact]
        public void 行列はrootの移動と拡縮を反映する()
        {
            var worldToLocal = WorldToLocal(new Vector3(10f, 0f, 0f), 2f);
            var matrix = PngDecalProjection.ComputeDecalMatrix(worldToLocal, Vector2.one);

            AssertNear(new Vector3(0.5f, 0.25f, 0f), matrix.MultiplyPoint3x4(new Vector3(11f, 0.5f, 0f)));
        }

        [Fact]
        public void 箱の頂点はバウンズと同じ並びで返す()
        {
            var corners = new Vector3[8];
            PngDecalProjection.GetBoxCorners(Matrix4x4.identity, new Vector2(1f, 0.5f), corners);

            AssertNear(new Vector3(-0.5f, -0.25f, -0.5f), corners[0]);
            AssertNear(new Vector3(0.5f, -0.25f, -0.5f), corners[1]);
            AssertNear(new Vector3(-0.5f, 0.25f, -0.5f), corners[2]);
            AssertNear(new Vector3(-0.5f, -0.25f, 0.5f), corners[4]);
            AssertNear(new Vector3(0.5f, 0.25f, 0.5f), corners[7]);
        }

        [Fact]
        public void フェード角90度は真横で0になり60度から薄くなる()
        {
            var range = PngDecalProjection.GetFadeCosRange(90f);
            AssertNear(0f, range.x);
            AssertNear(0.5f, range.y);
        }

        [Fact]
        public void フェード角は1から90度に丸める()
        {
            AssertNear(1f, PngDecalProjection.ClampFadeAngle(0f));
            AssertNear(90f, PngDecalProjection.ClampFadeAngle(120f));

            // 0 度指定でも smoothstep の両端が一致しない
            var range = PngDecalProjection.GetFadeCosRange(0f);
            Assert.True(range.x < range.y);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

BUILD を実行する。

期待する結果: テストプロジェクトはまだビルドしていないので、プラグインのビルドは成功する。次に TEST `PngDecalProjectionTests` を実行する。

期待する結果: `PngDecalProjection` が存在しないため、コンパイルエラー（CS0103 / CS0246）になる。

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/Manager/PngDecalProjection.cs`:

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// デカール投影の箱の計算。Unity のネイティブ呼び出しを含めず単体テストできるようにする。
    /// 投影箱は root ローカルで X ±aspect.x/2・Y ±aspect.y/2・Z ±0.5 の箱で、
    /// root の拡縮・回転がそのまま箱に効く
    /// </summary>
    public static class PngDecalProjection
    {
        public const float DefaultFadeAngle = 80f;
        public const float MinFadeAngle = 1f;
        public const float MaxFadeAngle = 90f;

        /// <summary>Projector の範囲を箱より広げる量 (m)。箱の境界に接する面がカリングで欠けないようにする</summary>
        public const float CullMargin = 0.01f;

        /// <summary>拡縮の絶対値の下限。0 除算で Infinity にしないため</summary>
        public const float MinScale = 0.0001f;

        /// <summary>fadeAngle に対してフェードが始まる角度の比</summary>
        private const float FadeStartRatio = 2f / 3f;

        /// <summary>Projector と子 (PngDecal) に設定する値一式</summary>
        public struct ProjectorFrame
        {
            public float orthographicSize;
            public float aspectRatio;
            public float nearClipPlane;
            public float farClipPlane;
            /// <summary>子の root ローカル位置。箱の表側の面より少し手前</summary>
            public Vector3 localPosition;
            /// <summary>root の拡縮を打ち消す子の localScale。Projector をワールド単位で扱うため</summary>
            public Vector3 localScale;
        }

        /// <summary>長辺を 1、短辺を比率にした画像の縦横。PNG 板の Quad と同じ規則</summary>
        public static Vector2 GetAspect(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return Vector2.one;
            }

            float w = width;
            float h = height;
            var ratio = Mathf.Min(w, h) / Mathf.Max(w, h);
            return w >= h ? new Vector2(1f, ratio) : new Vector2(ratio, 1f);
        }

        /// <summary>
        /// 投影箱を覆う Projector の設定を求める。
        /// Projector は子に置き、root の拡縮を子の localScale で打ち消してワールド単位で設定する。
        /// 位置は root ローカル (root の拡縮で伸びる) のため、ワールドでの距離を拡縮で割り戻す。
        /// 負の拡縮でも「拡縮 × ローカル位置」の符号が打ち消し合い、常に表側 (root の +Z) に立つ
        /// </summary>
        public static ProjectorFrame ComputeFrame(Vector2 aspect, Vector3 rootScale)
        {
            var sx = SafeScale(rootScale.x);
            var sy = SafeScale(rootScale.y);
            var sz = SafeScale(rootScale.z);

            var halfWidth = aspect.x * Mathf.Abs(sx) * 0.5f + CullMargin;
            var halfHeight = aspect.y * Mathf.Abs(sy) * 0.5f + CullMargin;
            var depth = Mathf.Abs(sz);

            return new ProjectorFrame
            {
                orthographicSize = halfHeight,
                aspectRatio = halfWidth / halfHeight,
                nearClipPlane = CullMargin * 0.5f,
                farClipPlane = depth + CullMargin * 2f,
                localPosition = new Vector3(0f, 0f, (depth * 0.5f + CullMargin) / sz),
                localScale = new Vector3(1f / sx, 1f / sy, 1f / sz),
            };
        }

        /// <summary>ワールド座標 → 投影箱の座標 (各軸 -0.5〜0.5) の行列</summary>
        public static Matrix4x4 ComputeDecalMatrix(Matrix4x4 rootWorldToLocal, Vector2 aspect)
        {
            var normalize = Matrix4x4.identity;
            normalize.m00 = 1f / Mathf.Max(aspect.x, MinScale);
            normalize.m11 = 1f / Mathf.Max(aspect.y, MinScale);
            return normalize * rootWorldToLocal;
        }

        /// <summary>
        /// 投影箱の 8 頂点をワールド座標で返す。
        /// 並びは PluginUtils.GetBoundsCorners と同じ (bit0=X, bit1=Y, bit2=Z) で、同じ辺の表を使える
        /// </summary>
        public static void GetBoxCorners(Matrix4x4 rootLocalToWorld, Vector2 aspect, Vector3[] corners)
        {
            var hx = aspect.x * 0.5f;
            var hy = aspect.y * 0.5f;
            const float hz = 0.5f;
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? -hx : hx,
                    (i & 2) == 0 ? -hy : hy,
                    (i & 4) == 0 ? -hz : hz);
                corners[i] = rootLocalToWorld.MultiplyPoint3x4(local);
            }
        }

        /// <summary>0 度では smoothstep の両端が一致するため下限を設ける</summary>
        public static float ClampFadeAngle(float fadeAngle)
        {
            return Mathf.Clamp(fadeAngle, MinFadeAngle, MaxFadeAngle);
        }

        /// <summary>
        /// 角度フェードの cos 範囲。面の法線と投影元の向きのなす角が
        /// fadeAngle 以上で不透明度 0 (x)、fadeAngle × 2/3 以下で 1 (y)
        /// </summary>
        public static Vector2 GetFadeCosRange(float fadeAngle)
        {
            var angle = ClampFadeAngle(fadeAngle) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Cos(angle * FadeStartRatio));
        }

        private static float SafeScale(float scale)
        {
            if (Mathf.Abs(scale) >= MinScale)
            {
                return scale;
            }
            return scale < 0f ? -MinScale : MinScale;
        }
    }
}
```

csproj（`<Compile Include="Manager\PngPlacementManager.cs" />` の直前）:

```xml
    <Compile Include="Manager\PngDecalProjection.cs" />
```

- [ ] **Step 4: テストが通ることを確認する**

BUILD と TEST `PngDecalProjectionTests` を実行する。期待する結果: 12 件がすべて PASS。

- [ ] **Step 5: コミット**

commit スキルでコミットする（例: `feat(png): デカール投影箱の計算を追加する`）。

---

### Task 2: デカールシェーダーとバンドル、実機での描画スモークテスト

**Files:**
- Create: `UnityProject/Assets/Shaders/Decal.shader`（`.meta` は Unity が生成する）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineBundleManager.cs`（`LoadMaterial` の直後に `LoadShader` を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle`（再ビルドした成果物）

**Interfaces:**
- Consumes: なし（スモークテストでは Task 1 の値を手で与える）
- Produces:
  - `TimelineBundleManager.LoadShader(string shaderName)` → `Shader`（無ければ null を返し、エラーログを出す）
  - シェーダー `SE/Decal` のプロパティ:
    - `_MainTex`、`_Color`
    - `_DecalMatrix`（float4x4）、`_DecalNormal`（float4）
    - `_FadeCosMin`、`_FadeCosMax`
    - `_BlendMode`（0 / 1 / 2）、`_SrcBlend`、`_DstBlend`

- [ ] **Step 1: シェーダーを書く**

`UnityProject/Assets/Shaders/Decal.shader`:

```hlsl
Shader "SE/Decal"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _FadeCosMin ("Fade Cos Min", Float) = 0.1736
        _FadeCosMax ("Fade Cos Max", Float) = 0.6428
        _BlendMode ("Blend Mode", Float) = 0
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        // 不透明物の後・半透明物の前に重ねる
        Tags
        {
            "Queue"="Transparent-500"
            "RenderType"="Transparent"
        }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Back
            // 受け側の面と同じ深度に描くため手前へずらして Z ファイティングを避ける
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            // ワールド → 投影箱 (各軸 -0.5〜0.5)。Projector 組込みの行列は
            // エンジンのバージョンで名前が変わるため使わず、C# から渡す
            float4x4 _DecalMatrix;
            // 投影元の向き (root の +Z = 板の表側) のワールド方向
            float4 _DecalNormal;
            float _FadeCosMin;
            float _FadeCosMax;
            float _BlendMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 boxPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.boxPos = mul(_DecalMatrix, worldPos).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 箱の外 (奥行き方向の突き抜けを含む) は描かない
                clip(0.5 - abs(i.boxPos));

                // 投影元を向く面ほど濃く、横向きの面は消す (床から壁の側面への伸びを抑える)
                float facing = dot(normalize(i.worldNormal), _DecalNormal.xyz);
                float fade = smoothstep(_FadeCosMin, _FadeCosMax, facing);

                // 板の Quad は Y180 回転で +X が root の -X になるため、U を反転して板と向きを揃える
                float2 uv = float2(0.5 - i.boxPos.x, i.boxPos.y + 0.5);
                fixed4 col = tex2D(_MainTex, uv) * _Color;
                col.a *= fade;

                if (_BlendMode > 0.5 && _BlendMode < 1.5)
                {
                    // 乗算 (DstColor Zero): 透明な所は白 (変化なし) へ寄せる
                    return fixed4(lerp(float3(1, 1, 1), col.rgb, col.a), 1);
                }
                return col;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: `LoadShader` を追加する**

`TimelineBundleManager.cs` の `LoadMaterial` メソッドの直後:

```csharp
        /// <summary>
        /// シェーダー単体をロードする。Unity 5.6 のマテリアルはバイナリ形式で手書きできないため、
        /// マテリアルを用意せずコード側で new Material する用途に使う
        /// </summary>
        public Shader LoadShader(string shaderName)
        {
            if (!IsValid())
            {
                return null;
            }

            var path = ShaderBasePath + shaderName + ".shader";
            var shader = _assetBundle.LoadAsset<Shader>(path);
            if (shader == null)
            {
                MTEUtils.LogError("シェーダーが見つかりません: {0}", path);
                return null;
            }
            return shader;
        }
```

- [ ] **Step 3: バンドルをビルドしてプラグインへコピーする**

Unity エディタで UnityProject を開いていないことを確認してから実行する（Git Bash）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
ROOT='W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin'
mkdir -p /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Logs
"/c/Program Files/Unity/Hub/Editor/5.6.4f1/Editor/Unity.exe" -batchmode -nographics -quit \
  -projectPath "$ROOT\UnityProject" \
  -executeMethod CreateAssetBundles.BuildAllAssetBundlesBatch \
  -logFile "$ROOT\UnityProject\Logs\build-bundle.log"
echo "exit=$?"
grep -n "error\|Decal" /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Logs/build-bundle.log | head -20
cp /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Assets/Bundles/se_bundle \
   /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin && git status --short UnityProject source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
```

期待する結果:
- `exit=0`
- ログにシェーダーのコンパイルエラーが無い
- `git status` に次の 3 つが出る
  - `Decal.shader` と `Decal.shader.meta`（新規）
  - `se_bundle`（変更）
  - `Bundles/*.manifest` などの更新

- [ ] **Step 4: ビルドする**

BUILD を実行する。期待する結果: 両構成とも 0 エラー。

- [ ] **Step 5: 実機（COM3D2.5）でシェーダー単体の描画スモークテストを行う**

devbridge の `ping` で、ゲームが起動していることを確認する。起動していなければこのステップは飛ばし、Task 7 の実機検証でまとめて確認する（飛ばしたことを報告に書く）。

プラグインがロード済みの `se_bundle` を、新しいバンドルに差し替える。同じ名前のバンドルが読み込まれたままだと `LoadFromFile` が失敗するため、差し替えが必要になる。`eval_csharp` で次を実行する:

```csharp
var asm = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "COM3D2.SceneEditor.Plugin");
var bmType = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.TimelineBundleManager");
var bm = bmType.GetProperty("instance").GetValue(null, null);
var field = bmType.GetField("_assetBundle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var old = (UnityEngine.AssetBundle)field.GetValue(bm);
if (old != null) old.Unload(false);
var nb = UnityEngine.AssetBundle.LoadFromFile(@"W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\UnityProject\Assets\Bundles\se_bundle");
field.SetValue(bm, nb);
var decalShader = nb.LoadAsset<UnityEngine.Shader>("Assets/Shaders/Decal.shader");
decalShader == null ? "shader null" : ("shader=" + decalShader.name + " supported=" + decalShader.isSupported)
```

期待する結果: `shader=SE/Decal supported=True`

続けて、カメラの前に板（Plane）と 2×2 色のテクスチャのデカールを置く。root の +Z をカメラへ向ける（板の表側と同じ向き）:

```csharp
var cam = UnityEngine.Camera.main.transform;
var center = cam.position + cam.forward * 3f;
var plane = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Plane);
plane.name = "__decal_plane";
plane.transform.position = center;
plane.transform.rotation = UnityEngine.Quaternion.LookRotation(cam.up, -cam.forward);
plane.transform.localScale = UnityEngine.Vector3.one * 0.3f;
var root = new UnityEngine.GameObject("__decal_root");
root.transform.position = center;
root.transform.rotation = UnityEngine.Quaternion.LookRotation(-cam.forward, cam.up);
var child = new UnityEngine.GameObject("__decal_proj");
child.transform.SetParent(root.transform, false);
child.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 180f, 0f);
child.transform.localPosition = new UnityEngine.Vector3(0f, 0f, 0.51f);
var proj = child.AddComponent<UnityEngine.Projector>();
proj.orthographic = true; proj.orthographicSize = 0.51f; proj.aspectRatio = 1f;
proj.nearClipPlane = 0.005f; proj.farClipPlane = 1.02f;
var tex = new UnityEngine.Texture2D(2, 2);
tex.SetPixels(new[] { UnityEngine.Color.red, UnityEngine.Color.green, UnityEngine.Color.blue, UnityEngine.Color.white });
tex.filterMode = UnityEngine.FilterMode.Point; tex.wrapMode = UnityEngine.TextureWrapMode.Clamp; tex.Apply();
var mat = new UnityEngine.Material(decalShader);
mat.mainTexture = tex;
mat.SetMatrix("_DecalMatrix", root.transform.worldToLocalMatrix);
mat.SetVector("_DecalNormal", root.transform.forward);
mat.SetFloat("_FadeCosMin", 0.1736f); mat.SetFloat("_FadeCosMax", 0.6428f);
proj.material = mat;
"placed"
```

devbridge の `screenshot` で画面を撮る。期待する結果:
- 画面中央付近の板の上に、1m 四方の 4 色の四角が映る
- 左下が赤、右下が緑、左上が青、右上が白（テクスチャの (0,0) が左下。左右反転していない）
- 四角の外には何も描かれない

片付け:

```csharp
foreach (var n in new[] { "__decal_plane", "__decal_root" }) { var g = UnityEngine.GameObject.Find(n); if (g != null) UnityEngine.Object.Destroy(g); }
"cleaned"
```

色の並びが左右反転していた場合は、シェーダーの U を `i.boxPos.x + 0.5` に変える。そのうえで Step 3 から繰り返し、spec の UV の記述も直す。

- [ ] **Step 6: コミット**

`Decal.shader`、`Decal.shader.meta`、バンドルの生成物（`UnityProject/Assets/Bundles/*`）、`Timeline/se_bundle`、`TimelineBundleManager.cs` を、commit スキルでコミットする（例: `feat(png): デカール投影シェーダーを追加する`）。

---

### Task 3: PngPlacementManager にデカール表示を実装する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs`

**Interfaces:**
- Consumes:
  - Task 1 の `PngDecalProjection.*`
  - Task 2 の `MTEP.TimelineBundleManager.instance.LoadShader("Decal")` と、シェーダーのプロパティ名
- Produces（名前空間 `COM3D2.SceneEditor.Plugin`）:
  - `public enum PngDisplayType { Board = 0, Decal = 1 }`
  - `public enum PngDecalBlendMode { Normal = 0, Multiply = 1, Additive = 2 }`
  - `PngObjectData` の新しいフィールド:
    - 設定値: `displayType`、`decalBlendMode`、`decalFadeAngle`、`decalProjectOnMaids`
    - 画像の縦横: `aspect`（Vector2）
    - デカールの実体: `decalObject`（GameObject）、`projector`（Projector）、`decalMaterial`（Material）
  - `PngPlacementManager` のセッター:
    - `SetDisplayType(PngObjectData, PngDisplayType)`
    - `SetDecalBlendMode(PngObjectData, PngDecalBlendMode)`
    - `SetDecalFadeAngle(PngObjectData, float)`
    - `SetDecalProjectOnMaids(PngObjectData, bool)`
  - `PngPlacementManager.entitySettingsRevision`（int、読み取り専用プロパティ）
    - `SetRenderQueue` と上の 4 セッターで、値が実際に変わったときだけ 1 増える

このタスクはゲームのランタイムに依存するので単体テストは無い。ビルドと、Task 7 の実機検証で確かめる。

- [ ] **Step 1: 列挙型とデータのフィールドを追加する**

ファイル先頭の `using` に次を追加する:

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
```

`PngObjectData` クラスの直前に、列挙型を追加する:

```csharp
    /// <summary>PNG 配置の表示タイプ。値はシーンプリセットとタイムライン XML に保存される</summary>
    public enum PngDisplayType
    {
        /// <summary>板 (Quad) として置く</summary>
        Board = 0,
        /// <summary>板の奥の面へ画像を投影する</summary>
        Decal = 1,
    }

    /// <summary>デカールのブレンド方式。値はシーンプリセットとタイムライン XML に保存される</summary>
    public enum PngDecalBlendMode
    {
        Normal = 0,
        Multiply = 1,
        Additive = 2,
    }
```

`PngObjectData` の `public bool visible = true;` の直後に、次のフィールドを追加する:

```csharp
        /// <summary>画像の縦横 (長辺 1)。板の Quad とデカールの投影箱の大きさに使う</summary>
        public Vector2 aspect = Vector2.one;

        public PngDisplayType displayType = PngDisplayType.Board;
        public PngDecalBlendMode decalBlendMode = PngDecalBlendMode.Normal;
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;
        public bool decalProjectOnMaids;

        /// <summary>デカール投影の子 (PngDecal)。初めてデカールにしたときに生成する</summary>
        public GameObject decalObject;
        public Projector projector;
        public Material decalMaterial;
```

- [ ] **Step 2: アスペクト比の計算を `PngDecalProjection.GetAspect` に置き換える**

`AddPng` の `ApplyAspectScale(quad.transform, texture);` を次に置き換える:

```csharp
            var aspect = PngDecalProjection.GetAspect(texture.width, texture.height);
            quad.transform.localScale = new Vector3(aspect.x, aspect.y, 1f);
```

`AddPng` で `new PngObjectData { ... }` を作っている箇所に、`aspect = aspect,` を追加する。

`ApplyAspectScale` メソッドとその XML コメントを削除する（呼び出し元はここだけ）。

- [ ] **Step 3: マネージャーのフィールドと定数を追加する**

`private Shader _shader = null;` の直後に追加する:

```csharp
        private const string DECAL_SHADER_NAME = "Decal";
        private const string DECAL_OBJECT_NAME = "PngDecal";

        /// <summary>「メイドにも投影」が OFF のとき Projector に無視させるレイヤー名</summary>
        private static readonly string[] MaidLayerNames = { "Charactor", "Face", "Man" };

        private static readonly int DecalMatrixId = Shader.PropertyToID("_DecalMatrix");
        private static readonly int DecalNormalId = Shader.PropertyToID("_DecalNormal");
        private static readonly int FadeCosMinId = Shader.PropertyToID("_FadeCosMin");
        private static readonly int FadeCosMaxId = Shader.PropertyToID("_FadeCosMax");
        private static readonly int BlendModeId = Shader.PropertyToID("_BlendMode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Shader _decalShader = null;
        private bool _isPreCullHooked = false;

        /// <summary>
        /// タイムラインの実体データへ保存する設定 (表示順・表示タイプ・デカール設定) の変更回数。
        /// タイムライン側は前回値と比べて保存データへ書き戻す。
        /// 色・表示はキー側の値で再生中に毎フレーム変わりうるため数えない
        /// </summary>
        public int entitySettingsRevision { get; private set; }
```

- [ ] **Step 4: 色とセッターを書き換える**

既存の `SetColor` / `SetRenderQueue` を次に置き換え、その後ろに新しいセッター群を追加する:

```csharp
        public void SetColor(PngObjectData data, Color color, float brightness)
        {
            data.color = color;
            data.brightness = brightness;
            var tint = GetTintColor(data);
            if (data.material != null)
            {
                data.material.SetColor(ColorId, tint);
            }
            if (data.decalMaterial != null)
            {
                data.decalMaterial.SetColor(ColorId, tint);
            }
        }

        public void SetRenderQueue(PngObjectData data, int renderQueue)
        {
            if (data.renderQueue != renderQueue)
            {
                entitySettingsRevision++;
            }
            data.renderQueue = renderQueue;
            if (data.material != null)
            {
                data.material.renderQueue = renderQueue;
            }
        }

        public void SetDisplayType(PngObjectData data, PngDisplayType displayType)
        {
            if (data.displayType != displayType)
            {
                entitySettingsRevision++;
            }
            data.displayType = displayType;
            ApplyDisplayType(data);
        }

        public void SetDecalBlendMode(PngObjectData data, PngDecalBlendMode blendMode)
        {
            if (data.decalBlendMode != blendMode)
            {
                entitySettingsRevision++;
            }
            data.decalBlendMode = blendMode;
            ApplyDecalBlendMode(data);
        }

        public void SetDecalFadeAngle(PngObjectData data, float fadeAngle)
        {
            var clamped = PngDecalProjection.ClampFadeAngle(fadeAngle);
            if (!Mathf.Approximately(data.decalFadeAngle, clamped))
            {
                entitySettingsRevision++;
            }
            data.decalFadeAngle = clamped;
            ApplyDecalFadeAngle(data);
        }

        public void SetDecalProjectOnMaids(PngObjectData data, bool projectOnMaids)
        {
            if (data.decalProjectOnMaids != projectOnMaids)
            {
                entitySettingsRevision++;
            }
            data.decalProjectOnMaids = projectOnMaids;
            ApplyDecalIgnoreLayers(data);
        }

        /// <summary>色 × 明るさ。α は明るさの影響を受けない不透明度</summary>
        private static Color GetTintColor(PngObjectData data)
        {
            var c = data.color;
            var b = data.brightness;
            return new Color(c.r * b, c.g * b, c.b * b, c.a);
        }
```

- [ ] **Step 5: デカールの生成・切り替え・マテリアルへの反映を追加する**

`GetShader()` メソッドの直後に追加する:

```csharp
        /// <summary>
        /// 表示タイプに合わせて板とデカールの一方だけを有効にする。
        /// デカールを作れない (シェーダーが無い等) ときは設定値を残したまま板で見せる
        /// </summary>
        private void ApplyDisplayType(PngObjectData data)
        {
            var isDecal = data.displayType == PngDisplayType.Decal;
            if (isDecal && data.decalObject == null && !CreateDecal(data))
            {
                isDecal = false;
            }

            if (data.quadObject != null)
            {
                data.quadObject.SetActive(!isDecal);
            }
            if (data.decalObject != null)
            {
                data.decalObject.SetActive(isDecal);
            }
        }

        private bool CreateDecal(PngObjectData data)
        {
            var shader = GetDecalShader();
            if (shader == null || data.rootObject == null)
            {
                return false;
            }

            var go = new GameObject(DECAL_OBJECT_NAME);
            go.transform.SetParent(data.rootObject.transform, false);
            // 板の Quad と同じく 180 度回し、投影方向を root の -Z (板の表から裏) へ向ける
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var material = new Material(shader);
            material.mainTexture = data.material != null ? data.material.mainTexture : null;

            var projector = go.AddComponent<Projector>();
            projector.orthographic = true;
            projector.material = material;

            data.decalObject = go;
            data.projector = projector;
            data.decalMaterial = material;

            material.SetColor(ColorId, GetTintColor(data));
            ApplyDecalBlendMode(data);
            ApplyDecalFadeAngle(data);
            ApplyDecalIgnoreLayers(data);
            UpdateDecal(data);
            HookPreCull();
            return true;
        }

        private Shader GetDecalShader()
        {
            if (_decalShader == null)
            {
                _decalShader = MTEP.TimelineBundleManager.instance.LoadShader(DECAL_SHADER_NAME);
            }
            return _decalShader;
        }

        private static void ApplyDecalBlendMode(PngObjectData data)
        {
            var material = data.decalMaterial;
            if (material == null)
            {
                return;
            }

            UnityEngine.Rendering.BlendMode src;
            UnityEngine.Rendering.BlendMode dst;
            switch (data.decalBlendMode)
            {
                case PngDecalBlendMode.Multiply:
                    src = UnityEngine.Rendering.BlendMode.DstColor;
                    dst = UnityEngine.Rendering.BlendMode.Zero;
                    break;
                case PngDecalBlendMode.Additive:
                    src = UnityEngine.Rendering.BlendMode.SrcAlpha;
                    dst = UnityEngine.Rendering.BlendMode.One;
                    break;
                default:
                    src = UnityEngine.Rendering.BlendMode.SrcAlpha;
                    dst = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                    break;
            }
            material.SetInt(SrcBlendId, (int)src);
            material.SetInt(DstBlendId, (int)dst);
            material.SetFloat(BlendModeId, (int)data.decalBlendMode);
        }

        private static void ApplyDecalFadeAngle(PngObjectData data)
        {
            if (data.decalMaterial == null)
            {
                return;
            }
            var range = PngDecalProjection.GetFadeCosRange(data.decalFadeAngle);
            data.decalMaterial.SetFloat(FadeCosMinId, range.x);
            data.decalMaterial.SetFloat(FadeCosMaxId, range.y);
        }

        private static void ApplyDecalIgnoreLayers(PngObjectData data)
        {
            if (data.projector == null)
            {
                return;
            }
            data.projector.ignoreLayers = data.decalProjectOnMaids ? 0 : GetMaidLayerMask();
        }

        private static int GetMaidLayerMask()
        {
            var mask = 0;
            foreach (var name in MaidLayerNames)
            {
                var layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }
            return mask;
        }

        /// <summary>
        /// 投影箱を root の Transform へ合わせる。
        /// 専用ルート (SceneEditorPngRoot) は拡縮しないため root の localScale をワールドの拡縮として扱う
        /// </summary>
        private static void UpdateDecal(PngObjectData data)
        {
            var root = data.rootObject.transform;
            var frame = PngDecalProjection.ComputeFrame(data.aspect, root.localScale);

            var decalTransform = data.decalObject.transform;
            decalTransform.localPosition = frame.localPosition;
            decalTransform.localScale = frame.localScale;

            var projector = data.projector;
            projector.orthographicSize = frame.orthographicSize;
            projector.aspectRatio = frame.aspectRatio;
            projector.nearClipPlane = frame.nearClipPlane;
            projector.farClipPlane = frame.farClipPlane;

            data.decalMaterial.SetMatrix(DecalMatrixId,
                PngDecalProjection.ComputeDecalMatrix(root.worldToLocalMatrix, data.aspect));
            data.decalMaterial.SetVector(DecalNormalId, root.forward);
        }

        private void HookPreCull()
        {
            if (_isPreCullHooked)
            {
                return;
            }
            Camera.onPreCull += OnPreCullDecals;
            _isPreCullHooked = true;
        }

        private void UnhookPreCull()
        {
            if (!_isPreCullHooked)
            {
                return;
            }
            Camera.onPreCull -= OnPreCullDecals;
            _isPreCullHooked = false;
        }

        /// <summary>
        /// デカールを Transform へ追従させる。ギズモ・タイムライン再生・Undo によるそのフレームの変更が
        /// 済んだ後、カメラのカリングより前に呼ばれるため投影が 1 フレーム遅れない
        /// </summary>
        private void OnPreCullDecals(Camera camera)
        {
            foreach (var data in _pngObjects)
            {
                if (data.displayType != PngDisplayType.Decal
                    || data.decalObject == null
                    || data.rootObject == null)
                {
                    continue;
                }
                UpdateDecal(data);
            }
        }
```

- [ ] **Step 6: 破棄処理とビルボードを直す**

`RemovePng` の `if (data.material != null) { Object.Destroy(data.material); }` の直後と、`ClearAll` のループ内の同じ箇所の直後に、それぞれ追加する:

```csharp
            if (data.decalMaterial != null)
            {
                Object.Destroy(data.decalMaterial);
            }
```

`ClearAll` のループ内は `data` を使う同じ形。インデントはループに合わせる。

`ReleaseAll` の `ClearAll();` の直後に `UnhookPreCull();` を追加する。

`LateUpdate` の条件を次に変える（デカールの root はビルボードで回さない）:

```csharp
                if (!data.billboard
                    || data.displayType != PngDisplayType.Board
                    || data.rootObject == null)
                {
                    continue;
                }
```

クラスの XML コメント（`PngPlacementManager` の summary）の最後に、次の 1 文を追加する:

```
        /// デカール表示では子の Projector が板の奥の面へ画像を投影する
```

- [ ] **Step 7: ビルドする**

BUILD を実行する。期待する結果: 両構成とも 0 エラー。
あわせて TEST（フィルターなし）で全テストを実行し、既存テストが壊れていないことを確認する。

- [ ] **Step 8: コミット**

commit スキルでコミットする（例: `feat(png): PNG配置にデカール表示を追加する`）。

---

### Task 4: Inspector・配置済み一覧・ギズモ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/PngPlacementWindow.cs`（`PlacedTileContent` と `SyncPlacedList`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs`

**Interfaces:**
- Consumes:
  - Task 3 の `PngDisplayType`、`PngDecalBlendMode`、セッター群、`PngObjectData.aspect`
  - Task 1 の `PngDecalProjection.GetBoxCorners`、`MinFadeAngle`、`MaxFadeAngle`、`DefaultFadeAngle`
- Produces: なし（UI のみ）

- [ ] **Step 1: Inspector を書き換える**

`PngPlacementInspector.cs` の定数群（`RENDER_QUEUE_BIG_STEP` の直後）に追加する:

```csharp
        private const float TAB_WIDTH = 80f;
        private const float BLEND_TAB_WIDTH = 60f;
        private const float FADE_ANGLE_STEP = 1f;

        // PngDisplayType / PngDecalBlendMode の値順に並べる
        private static readonly string[] DisplayTypeLabels = { "板", "デカール" };
        private static readonly string[] BlendModeLabels = { "通常", "乗算", "加算" };
```

`Draw` メソッドの `view.DrawHorizontalLine(Color.gray);` から、`return true;` の直前までを次に置き換える:

```csharp
            view.DrawHorizontalLine(Color.gray);

            var displayIndex = view.DrawTabs(
                DisplayTypeLabels, (int)data.displayType, TAB_WIDTH, ROW_HEIGHT);
            if (displayIndex != (int)data.displayType)
            {
                RecordPngEdit("表示タイプ");
                pngManager.SetDisplayType(data, (PngDisplayType)displayIndex);
            }
            var isDecal = data.displayType == PngDisplayType.Decal;

            view.BeginHorizontal();
            {
                view.DrawToggle("表示", data.visible, 60, ROW_HEIGHT, value =>
                {
                    RecordPngEdit("表示切替");
                    pngManager.SetVisible(data, value);
                });

                // デカールの root を回すと投影方向が変わるため、ビルボードは板専用
                if (!isDecal)
                {
                    view.DrawToggle("ビルボード", data.billboard, 100, ROW_HEIGHT, value =>
                    {
                        RecordPngEdit("ビルボード切替");
                        pngManager.SetBillboard(data, value);
                    });
                }
            }
            view.EndLayout();

            // ColorPickerWindow はラベル文字列で編集対象を識別するため、
            // 他ウィンドウの色行とラベルを重複させないこと
            var colorLabel = colorLabelPrefix == null ? "PNG色" : colorLabelPrefix + "/PNG色";
            var fieldCache = view.GetColorFieldCache(colorLabel, true);
            view.DrawColor(fieldCache, data.color, Color.white, value =>
            {
                RecordPngEdit("色");
                pngManager.SetColor(data, value, data.brightness);
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "明るさ",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = data.brightness,
                onChanged = value =>
                {
                    RecordPngEdit("明るさ");
                    pngManager.SetColor(data, data.color, value);
                },
            });

            if (isDecal)
            {
                DrawDecalRows(view, data);
            }
            else
            {
                DrawRenderQueueRow(view, data);
            }
```

クラス内（`RecordPngEdit` の直前）に追加する:

```csharp
        /// <summary>表示順は板の描画順で、デカールの描画順はシェーダー側で固定のため板専用</summary>
        private static void DrawRenderQueueRow(GUIView view, PngObjectData data)
        {
            view.DrawIntSelect("表示順", RENDER_QUEUE_STEP, RENDER_QUEUE_BIG_STEP,
                () =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, PngPlacementManager.DefaultRenderQueue);
                },
                data.renderQueue,
                value =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, value);
                },
                diff =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, data.renderQueue + diff);
                });
        }

        private static void DrawDecalRows(GUIView view, PngObjectData data)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("ブレンド", LABEL_WIDTH, ROW_HEIGHT);
                var blendIndex = view.DrawTabs(
                    BlendModeLabels, (int)data.decalBlendMode, BLEND_TAB_WIDTH, ROW_HEIGHT);
                if (blendIndex != (int)data.decalBlendMode)
                {
                    RecordPngEdit("ブレンド");
                    pngManager.SetDecalBlendMode(data, (PngDecalBlendMode)blendIndex);
                }
            }
            view.EndLayout();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "角度フェード",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = PngDecalProjection.MinFadeAngle,
                max = PngDecalProjection.MaxFadeAngle,
                step = FADE_ANGLE_STEP,
                defaultValue = PngDecalProjection.DefaultFadeAngle,
                value = data.decalFadeAngle,
                onChanged = value =>
                {
                    RecordPngEdit("角度フェード");
                    pngManager.SetDecalFadeAngle(data, value);
                },
            });

            view.DrawToggle("メイドにも投影", data.decalProjectOnMaids, 140, ROW_HEIGHT, value =>
            {
                RecordPngEdit("メイドにも投影");
                pngManager.SetDecalProjectOnMaids(data, value);
            });
        }
```

クラスの summary の 2 行目を、次の 2 行に置き換える:

```
    /// 位置・回転・拡縮は Inspector 共通の Transform 行が担うため、その続きに足す。
    /// 表示タイプ (板 / デカール) で固有欄を出し分ける
```

- [ ] **Step 2: 配置済み一覧に `[デカール]` を付ける**

`PngPlacementWindow.cs` の `PlacedTileContent` に、フィールドを追加する:

```csharp
            /// <summary>name に反映済みの表示タイプ。変わったときだけ表示名を作り直す</summary>
            public PngDisplayType labeledType;
```

定数群（`PHOTO_TAG_COLOR` の直後）に追加する:

```csharp
        private const string DECAL_LABEL_PREFIX = "[デカール] ";
```

`SyncPlacedList` の `new PlacedTileContent { name = data.name, ... }` を、`name = GetPlacedLabel(data), labeledType = data.displayType,` を含む形に変える。

`foreach (PlacedTileContent content in children)` のループ本体を、次に置き換える:

```csharp
            foreach (PlacedTileContent content in children)
            {
                content.isSelected = content.data.rootObject == selectedObject;
                if (content.labeledType != content.data.displayType)
                {
                    content.labeledType = content.data.displayType;
                    content.name = GetPlacedLabel(content.data);
                }
            }
```

`IsPlacedListSynced` の直前に追加する:

```csharp
        private static string GetPlacedLabel(PngObjectData data)
        {
            return data.displayType == PngDisplayType.Decal
                ? DECAL_LABEL_PREFIX + data.name
                : data.name;
        }
```

- [ ] **Step 3: ギズモで投影箱を描く**

`GizmoRenderer.cs` の色定数（`FrustumColor` の直後）に追加する:

```csharp
        private static readonly Color DecalBoxColor = new Color(0.4f, 1f, 0.6f, 0.9f);

        /// <summary>投影方向の矢じりの大きさ (投影箱の幅に対する比)</summary>
        private const float DecalArrowHeadRatio = 0.15f;

        // 8 頂点 (bit0=X, bit1=Y, bit2=Z) の箱の 12 辺
        private static readonly int[,] BoxEdges =
        {
            {0,1},{2,3},{4,5},{6,7},
            {0,2},{1,3},{4,6},{5,7},
            {0,4},{1,5},{2,6},{3,7},
        };
```

選択の枠を描く箇所:

```csharp
            if (target != null && showSelectionBounds)
            {
                DrawBoundsWire(PluginUtils.CalcObjectBounds(target));
            }
```

これを次に置き換える:

```csharp
            if (target != null && showSelectionBounds)
            {
                // デカールの root にはレンダラーが無く、位置だけの小さなバウンズになる。
                // 投影範囲を示せないため代わりに投影箱を描く
                var decal = FindDecal(target);
                if (decal != null)
                {
                    DrawDecalBox(decal);
                }
                else
                {
                    DrawBoundsWire(PluginUtils.CalcObjectBounds(target));
                }
            }
```

`DrawBoundsWire` を次に置き換え、あわせてメソッドを 3 つ追加する:

```csharp
        private void DrawBoundsWire(Bounds bounds)
        {
            PluginUtils.GetBoundsCorners(bounds, _boundsCorners);
            DrawBoxWire(_boundsCorners, BoundsColor);
        }

        private static void DrawBoxWire(Vector3[] corners, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < BoxEdges.GetLength(0); i++)
            {
                GL.Vertex(corners[BoxEdges[i, 0]]);
                GL.Vertex(corners[BoxEdges[i, 1]]);
            }
            GL.End();
        }

        /// <summary>選択中がデカール表示の PNG 配置なら、その配置物。そうでなければ null</summary>
        private static PngObjectData FindDecal(GameObject go)
        {
            var data = PngPlacementManager.instance.FindByRoot(go);
            return data != null && data.displayType == PngDisplayType.Decal ? data : null;
        }

        /// <summary>投影箱と投影方向 (表の面の中心から裏の面の中心へ向かう矢印) を描く</summary>
        private void DrawDecalBox(PngObjectData data)
        {
            var matrix = data.transform.localToWorldMatrix;
            PngDecalProjection.GetBoxCorners(matrix, data.aspect, _boundsCorners);
            DrawBoxWire(_boundsCorners, DecalBoxColor);

            var front = matrix.MultiplyPoint3x4(new Vector3(0f, 0f, 0.5f));
            var back = matrix.MultiplyPoint3x4(new Vector3(0f, 0f, -0.5f));
            var side = matrix.MultiplyVector(Vector3.right * (data.aspect.x * DecalArrowHeadRatio));
            var headBase = Vector3.Lerp(back, front, DecalArrowHeadRatio * 2f);

            GL.Begin(GL.LINES);
            GL.Color(DecalBoxColor);
            GL.Vertex(front);
            GL.Vertex(back);
            GL.Vertex(back);
            GL.Vertex(headBase + side);
            GL.Vertex(back);
            GL.Vertex(headBase - side);
            GL.End();
        }
```

- [ ] **Step 4: ビルドする**

BUILD を実行する。期待する結果: 両構成とも 0 エラー。

- [ ] **Step 5: コミット**

commit スキルでコミットする（例: `feat(png): デカールの Inspector 欄と投影箱のギズモを追加する`）。

---

### Task 5: Undo とシーンプリセットの保存

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
  - `ScenePresetPngObject`（:182〜）
  - バージョン履歴のコメントと `CurrentVersion`（:1050〜1063）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs:149`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs`

**Interfaces:**
- Consumes: Task 3 の列挙型、セッター群、`PngDecalProjection.DefaultFadeAngle`
- Produces:
  - `ScenePresetPngObject` の新しいフィールド: `displayType`、`decalBlendMode`、`decalFadeAngle`、`decalProjectOnMaids`
  - `ScenePresetData.CurrentVersion == 36`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットの PNG 配置に追加した表示タイプとデカール設定の保存と読込を固定する。
    /// 板では属性を書き出さず、属性の無い旧データは板として読む
    /// </summary>
    public class ScenePresetPngDecalTests
    {
        private static string Serialize(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static ScenePresetData Deserialize(string xml)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader(xml))
            {
                return (ScenePresetData)serializer.Deserialize(reader);
            }
        }

        private static ScenePresetData WithPng(ScenePresetPngObject png)
        {
            var data = new ScenePresetData { pngPlacement = new ScenePresetPngPlacement() };
            data.pngPlacement.objects.Add(png);
            return data;
        }

        [Fact]
        public void デカール設定は往復で保たれる()
        {
            var data = WithPng(new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                displayType = PngDisplayType.Decal,
                decalBlendMode = PngDecalBlendMode.Multiply,
                decalFadeAngle = 45f,
                decalProjectOnMaids = true,
            });

            var restored = Deserialize(Serialize(data));

            var png = Assert.Single(restored.pngPlacement.objects);
            Assert.Equal(PngDisplayType.Decal, png.displayType);
            Assert.Equal(PngDecalBlendMode.Multiply, png.decalBlendMode);
            Assert.Equal(45f, png.decalFadeAngle);
            Assert.True(png.decalProjectOnMaids);
        }

        [Fact]
        public void 板では表示タイプとデカール設定を書き出さない()
        {
            // ScenePresetData 全体だと動画設定の displayType と混ざるため PNG 1 枚だけを直列化する
            var png = new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                decalBlendMode = PngDecalBlendMode.Additive,
                decalProjectOnMaids = true,
            };

            string xml;
            var serializer = new XmlSerializer(typeof(ScenePresetPngObject));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, png);
                xml = writer.ToString();
            }

            Assert.DoesNotContain("displayType", xml);
            Assert.DoesNotContain("decal", xml);
        }

        [Fact]
        public void 属性の無い旧データは板と既定値で読む()
        {
            var data = WithPng(new ScenePresetPngObject { source = "config", relativePath = "logo.png" });
            var xml = Serialize(data);

            var restored = Deserialize(xml);

            var png = Assert.Single(restored.pngPlacement.objects);
            Assert.Equal(PngDisplayType.Board, png.displayType);
            Assert.Equal(PngDecalBlendMode.Normal, png.decalBlendMode);
            Assert.Equal(PngDecalProjection.DefaultFadeAngle, png.decalFadeAngle);
            Assert.False(png.decalProjectOnMaids);
        }

        [Fact]
        public void 現在のバージョンは36()
        {
            Assert.Equal(36, ScenePresetData.CurrentVersion);
        }
    }
}
```

`ScenePresetEffectsTests.cs:149` の `Assert.Equal(35, ScenePresetData.CurrentVersion);` を `Assert.Equal(36, ScenePresetData.CurrentVersion);` に変える。

- [ ] **Step 2: テストが失敗することを確認する**

BUILD を実行し、TEST `ScenePresetPngDecalTests` を実行する。
期待する結果: `ScenePresetPngObject` にフィールドが無いため、コンパイルエラーになる。

- [ ] **Step 3: DTO とバージョンを実装する**

`ScenePresetPngObject` の `public bool visible = true;` の直後に追加する:

```csharp

        [XmlAttribute]
        public PngDisplayType displayType = PngDisplayType.Board;

        [XmlAttribute]
        public PngDecalBlendMode decalBlendMode = PngDecalBlendMode.Normal;

        [XmlAttribute]
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;

        [XmlAttribute]
        public bool decalProjectOnMaids;

        // 板のプリセットを v35 以前と同じ内容に保つため、表示タイプとデカール設定は板では書き出さない
        public bool ShouldSerializedisplayType() { return displayType != PngDisplayType.Board; }
        public bool ShouldSerializedecalBlendMode() { return displayType != PngDisplayType.Board; }
        public bool ShouldSerializedecalFadeAngle() { return displayType != PngDisplayType.Board; }
        public bool ShouldSerializedecalProjectOnMaids() { return displayType != PngDisplayType.Board; }
```

`ScenePresetData.cs` のバージョン履歴コメント（`// v34: ...` のブロックの直後、`public static readonly int CurrentVersion` の直前）に追加する:

```csharp
        // v36: pngPlacement の png に表示タイプ (displayType) とデカール設定
        //      (decalBlendMode / decalFadeAngle / decalProjectOnMaids) を追加。板では書き出さない。
        //      旧形式は属性が無く板として読める
```

`public static readonly int CurrentVersion = 35;` を `36` に変える。

- [ ] **Step 4: スナップショットに組み込む**

`PngPlacementSnapshot.CaptureState` の `new ScenePresetPngObject { ... }` で、`visible = data.visible,` の直後に追加する:

```csharp
                    displayType = data.displayType,
                    decalBlendMode = data.decalBlendMode,
                    decalFadeAngle = data.decalFadeAngle,
                    decalProjectOnMaids = data.decalProjectOnMaids,
```

`ApplyState` の `manager.SetVisible(data, objState.visible);` の直後に追加する:

```csharp
                manager.SetDisplayType(data, objState.displayType);
                manager.SetDecalBlendMode(data, objState.decalBlendMode);
                manager.SetDecalFadeAngle(data, objState.decalFadeAngle);
                manager.SetDecalProjectOnMaids(data, objState.decalProjectOnMaids);
```

`Approximately` の条件の `|| a.visible != b.visible)` を、次に置き換える:

```csharp
                    || a.visible != b.visible
                    || a.displayType != b.displayType
                    || a.decalBlendMode != b.decalBlendMode
                    || !Mathf.Approximately(a.decalFadeAngle, b.decalFadeAngle)
                    || a.decalProjectOnMaids != b.decalProjectOnMaids)
```

- [ ] **Step 5: テストが通ることを確認する**

BUILD を実行し、TEST `ScenePreset` を実行する。期待する結果: 新しい 4 件と、既存の `ScenePreset*` テストがすべて PASS。

- [ ] **Step 6: コミット**

commit スキルでコミットする（例: `feat(png): デカール設定をシーンプリセットと Undo に保存する`）。

---

### Task 6: タイムライン XML の保存と同期の修正

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`TimelinePngObjectXml`、:66〜80）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`TimelinePngObjectData`、:181〜226）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelinePngDecalXmlTests.cs`

**Interfaces:**
- Consumes:
  - Task 3 の列挙型、セッター群、`entitySettingsRevision`
  - Task 1 の `PngDecalProjection.DefaultFadeAngle`
- Produces:
  - `TimelinePngObjectXml` と `TimelinePngObjectData` の新しいフィールド（どちらも同名）:
    - `int displayType`
    - `int decalBlendMode`
    - `float decalFadeAngle`
    - `bool decalProjectOnMaids`
  - XML 要素名: `DisplayType` / `DecalBlendMode` / `DecalFadeAngle` / `DecalProjectOnMaids`
  - `TimelinePngObjectData.FromXml` は、範囲外の整数を 0（板・通常）に直す

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelinePngDecalXmlTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムライン XML の PNG 実体 (&lt;PngObjects&gt;) に追加した表示タイプとデカール設定を固定する。
    /// 板では要素を書き出さず、要素の無い XML (MTE 産・旧 SE) は板として読む
    /// </summary>
    public class TimelinePngDecalXmlTests
    {
        private static string Serialize(TimelinePngObjectXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelinePngObjectXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelinePngObjectXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelinePngObjectXml));
            using (var reader = new StringReader(text))
            {
                return (TimelinePngObjectXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void デカール設定は往復で保たれる()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                displayType = (int)PngDisplayType.Decal,
                decalBlendMode = (int)PngDecalBlendMode.Additive,
                decalFadeAngle = 30f,
                decalProjectOnMaids = true,
            };

            var text = Serialize(data.ToXml());
            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Contains("<DisplayType>1</DisplayType>", text);
            Assert.Equal((int)PngDisplayType.Decal, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Additive, restored.decalBlendMode);
            Assert.Equal(30f, restored.decalFadeAngle);
            Assert.True(restored.decalProjectOnMaids);
        }

        [Fact]
        public void 板ではデカールの要素を書き出さない()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                decalBlendMode = (int)PngDecalBlendMode.Multiply,
                decalProjectOnMaids = true,
            };

            var text = Serialize(data.ToXml());

            Assert.DoesNotContain("DisplayType", text);
            Assert.DoesNotContain("Decal", text);
        }

        [Fact]
        public void 要素の無いMTEのXMLは板と既定値で読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>sample_png</ImageName><Group>0</Group>" +
                "<Primitive>0</Primitive><SquareUV>false</SquareUV><RenderQueue>3000</RenderQueue>" +
                "</TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal((int)PngDisplayType.Board, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Normal, restored.decalBlendMode);
            Assert.Equal(PngDecalProjection.DefaultFadeAngle, restored.decalFadeAngle);
            Assert.False(restored.decalProjectOnMaids);
            Assert.Equal(3000, restored.renderQueue);
        }

        [Fact]
        public void 範囲外の値は板と通常として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>logo</ImageName>" +
                "<DisplayType>5</DisplayType><DecalBlendMode>-1</DecalBlendMode>" +
                "</TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal((int)PngDisplayType.Board, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Normal, restored.decalBlendMode);
        }

        [Fact]
        public void 表示順の要素が無いXMLは0として読む()
        {
            // 0 以下は Setup で適用しない (描画順を壊さないため)。その前提となる読込値を固定する
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>logo</ImageName></TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal(0, restored.renderQueue);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

BUILD を実行し、TEST `TimelinePngDecalXmlTests` を実行する。
期待する結果: フィールドが無いため、コンパイルエラーになる。

- [ ] **Step 3: XML と Data を実装する**

`TimelineXml.cs` には、ファイル先頭に `using COM3D2.SceneEditor.Plugin;` が既にある。そのため `PngDisplayType` / `PngDecalProjection` はエイリアス無しで書ける（エイリアスは追加しない）。

`TimelinePngObjectXml` の `public int renderQueue;` の直後に追加する:

```csharp

        // 表示タイプとデカール設定は SE 独自。板では書き出さず、
        // MTE 産の XML を保存し直しても内容を変えない (MTE は未知の要素を読み飛ばし板として表示する)
        [XmlElement("DisplayType")]
        public int displayType;
        [XmlElement("DecalBlendMode")]
        public int decalBlendMode;
        [XmlElement("DecalFadeAngle")]
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;
        [XmlElement("DecalProjectOnMaids")]
        public bool decalProjectOnMaids;

        public bool ShouldSerializedisplayType() { return IsDecal(); }
        public bool ShouldSerializedecalBlendMode() { return IsDecal(); }
        public bool ShouldSerializedecalFadeAngle() { return IsDecal(); }
        public bool ShouldSerializedecalProjectOnMaids() { return IsDecal(); }

        private bool IsDecal() { return displayType != (int)PngDisplayType.Board; }
```

`TimelineData.cs` の `TimelinePngObjectData` で、`public int renderQueue;` の直後に追加する:

```csharp
        public int displayType;
        public int decalBlendMode;
        public float decalFadeAngle = SE.PngDecalProjection.DefaultFadeAngle;
        public bool decalProjectOnMaids;
```

`TimelineData.cs` には SE 名前空間の `using` が無い。そのため、名前空間ブロックの先頭（既存の `using AttachPoint = ...;` の直後）に、次のエイリアスを追加する。形は `PngObjectTimelineManager.cs` と同じにする。SE と MTE の両方に同名の `PluginUtils` があるので、名前空間まるごとの `using` にはしない:

```csharp
    using SE = SceneEditor.Plugin;
```

`FromXml` の `renderQueue = xml.renderQueue;` の直後に追加する:

```csharp
            // 未知の値 (手編集・将来版) は例外にせず既定の板・通常として読む
            displayType = System.Enum.IsDefined(typeof(SE.PngDisplayType), xml.displayType)
                ? xml.displayType
                : (int)SE.PngDisplayType.Board;
            decalBlendMode = System.Enum.IsDefined(typeof(SE.PngDecalBlendMode), xml.decalBlendMode)
                ? xml.decalBlendMode
                : (int)SE.PngDecalBlendMode.Normal;
            decalFadeAngle = xml.decalFadeAngle;
            decalProjectOnMaids = xml.decalProjectOnMaids;
```

`ToXml` のオブジェクト初期化子の `renderQueue = renderQueue,` の直後に追加する:

```csharp
                displayType = displayType,
                decalBlendMode = decalBlendMode,
                decalFadeAngle = decalFadeAngle,
                decalProjectOnMaids = decalProjectOnMaids,
```

- [ ] **Step 4: テストが通ることを確認する**

BUILD を実行し、TEST `TimelinePngDecalXmlTests` を実行する。期待する結果: 5 件すべて PASS。

- [ ] **Step 5: PngObjectTimelineManager の書き戻しと読込時の適用を直す**

フィールド群（`_dataMap` の直後）に追加する:

```csharp
        // 保存データへ書き戻した時点の SE 側の実体設定の改訂番号。
        // 表示順・表示タイプ・デカール設定は増減を伴わず変わるため、これで変化を検知する
        private int _syncedSettingsRevision = -1;
```

`RebuildIfChanged` の先頭部分を置き換える。現状は次のとおり:

```csharp
            var seObjects = sePngManager.pngObjects;
            if (!IsChanged(seObjects))
            {
                return;
            }
```

これを次に置き換える:

```csharp
            var seObjects = sePngManager.pngObjects;
            if (!IsChanged(seObjects))
            {
                if (_syncedSettingsRevision != sePngManager.entitySettingsRevision)
                {
                    UpdateTimelineData();
                }
                return;
            }
```

`Setup` を次に置き換える:

```csharp
        public void Setup(List<TimelinePngObjectData> pngObjectDatas)
        {
            // 引数は timeline.pngObjects そのもので、途中の RebuildIfChanged → UpdateTimelineData が
            // 同じリストを消して SE 側の状態で書き直しうる。XML の値を失わないよう先に複製する
            var sources = new List<TimelinePngObjectData>(pngObjectDatas);

            RebuildIfChanged();

            // ソースディレクトリの走査は 1 回にまとめ、画像名 → (source, relativePath) の辞書で解決する
            Dictionary<string, KeyValuePair<string, string>> imageIndex = null;

            foreach (var data in sources)
            {
                var name = data.name;
                if (_entryMap.ContainsKey(name))
                {
                    continue;
                }

                if (imageIndex == null)
                {
                    imageIndex = BuildImageIndex();
                }

                KeyValuePair<string, string> found;
                if (!imageIndex.TryGetValue(data.imageName, out found))
                {
                    MTEUtils.LogWarning(
                        "PNG 画像が見つかりません: {0} (UserData\\PngPlacement または PhotoModeData\\Texture へ配置してください)",
                        data.imageName);
                    continue;
                }

                var created = sePngManager.AddPng(found.Key, found.Value);
                if (created == null)
                {
                    MTEUtils.LogWarning("PNG の生成に失敗しました: {0}", data.imageName);
                }
            }

            RebuildIfChanged();

            // XML の実体設定を、生成した実体と名前が一致した既存の実体へ戻す
            foreach (var data in sources)
            {
                var entry = GetPngObject(data.name);
                if (entry != null && entry.data != null)
                {
                    ApplyEntitySettings(entry.data, data);
                }
            }
        }

        private static void ApplyEntitySettings(SE.PngObjectData target, TimelinePngObjectData source)
        {
            // 要素の無い XML は 0 になる。0 以下の表示順は板が背景より先に描かれ消えるため既定のまま残す
            if (source.renderQueue > 0)
            {
                sePngManager.SetRenderQueue(target, source.renderQueue);
            }
            // 範囲外の値は FromXml で既定値へ直してあるため、そのままキャストしてよい
            sePngManager.SetDisplayType(target, (SE.PngDisplayType)source.displayType);
            sePngManager.SetDecalBlendMode(target, (SE.PngDecalBlendMode)source.decalBlendMode);
            sePngManager.SetDecalFadeAngle(target, source.decalFadeAngle);
            sePngManager.SetDecalProjectOnMaids(target, source.decalProjectOnMaids);
        }
```

`UpdateTimelineData` を次に置き換える:

```csharp
        /// <summary>タイムライン保存用に配置一覧と実体設定を書き戻す</summary>
        public void UpdateTimelineData()
        {
            _syncedSettingsRevision = sePngManager.entitySettingsRevision;

            if (timeline == null)
            {
                return;
            }

            timeline.pngObjects.Clear();
            foreach (var entry in pngObjects)
            {
                var se = entry.data;
                var data = new TimelinePngObjectData
                {
                    imageName = entry.imageName,
                    group = entry.group,
                    // primitive / squareUV / shaderDisplay は SE では固定 Quad + 標準シェーダー
                    // で代替するため既定値のまま保存する (MTE 互換のためフィールド自体は維持)
                    renderQueue = se != null ? se.renderQueue : SE.PngPlacementManager.DefaultRenderQueue,
                };
                if (se != null)
                {
                    data.displayType = (int)se.displayType;
                    data.decalBlendMode = (int)se.decalBlendMode;
                    data.decalFadeAngle = se.decalFadeAngle;
                    data.decalProjectOnMaids = se.decalProjectOnMaids;
                }
                timeline.pngObjects.Add(data);
            }
        }
```

`Reset()` の末尾に `_syncedSettingsRevision = -1;` を追加する。

`Setup` の summary の最後に、次の 1 行を追加する:

```
        /// 最後に XML の実体設定 (表示順・表示タイプ・デカール設定) を SE 実体へ適用する
```

- [ ] **Step 6: ビルドと全テスト**

BUILD と TEST（フィルターなし）を実行する。期待する結果: 両構成とも 0 エラーで、全テストが PASS。

特に次のテストが通ることを確認する:
- `RotationMigrationFixtureTests`（フィクスチャ `l7-pngobject.xml` を読む）
- `MteCompatibilityTests`

- [ ] **Step 7: コミット**

commit スキルでコミットする（例: `feat(timeline): PNG 実体の表示タイプとデカール設定をタイムラインへ保存する`）。
本文に「読込時に表示順を XML から戻すようになった」ことを書く。

---

### Task 7: 実機検証と互換ドキュメント

**Files:**
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の箇条書きの末尾）

**Interfaces:**
- Consumes: Task 1〜6 のすべて
- Produces: なし

- [ ] **Step 1: 互換リストに追記する**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」の最後の箇条書きの後に、次を追加する:

```markdown
- PNG 実体（`<PngObjects>`）の表示タイプとデカール設定（`DisplayType` / `DecalBlendMode` / `DecalFadeAngle` / `DecalProjectOnMaids`）は SE 独自。板では書き出さない。MTE は要素を読み飛ばすため、デカールは板として表示される。読込時は `RenderQueue` も実体へ戻す（0 以下は適用しない）
```

- [ ] **Step 2: 実機へ反映する**

restart-verify スキルの手順に従う。

1. ゲームを終了する
2. BUILD の成果物（COM3D25 構成の DLL）を `W:\COM3D2_5\Sybaris\UnityInjector\` へ反映する
3. ゲームを再起動し、セーブをロードする
4. 撮影モード（スタジオ）に入り、背景に床と壁のあるもの（例: ステージ系）を選ぶ

- [ ] **Step 3: 動作確認（devbridge の `screenshot` / `eval_csharp`）**

UI 操作が必要な項目はユーザーに依頼し、画面は `screenshot` で確認する。各項目の結果（OK / NG と画像）を記録する。

1. PNG配置で画像を 1 枚置き、Inspector で「デカール」に切り替える。板の奥の床か壁に絵柄が映り、正面から見た向きが板のときと一致する
2. 選択中は、緑の投影箱ワイヤーと矢印が表示される。ギズモで拡縮すると、箱と投影範囲が一致したまま変わる
3. メイドを箱の中に立たせたとき、メイドには映らない。「メイドにも投影」を ON にすると映る
4. ブレンドの通常・乗算・加算で、見た目がそれぞれ変わる
5. 角度フェードを 30 にすると、床用のデカールが壁の側面に映らなくなる
6. 「配置済み」一覧に `[デカール]` が付く。板に戻すと外れる
7. Undo / Redo で、表示タイプとデカール設定が戻り、また進む
8. シーンプリセットを保存し、PNG を消してからプリセットを読み込むと、デカールとして復元される
9. PNG配置レイヤーのあるタイムラインで、デカール設定を変えて保存し、再読込すると設定が戻る。キー再生で投影が遅れずに追従する
10. 板だけのシーンでプリセットを保存すると、XML に `displayType` が出ない（`eval_csharp` でファイルを読み、`Contains` で確かめる）
11. 実体を板で保存したタイムラインを用意する。その実体を後からデカールにした状態で、このタイムラインを読み込むと、仕様どおり板に戻る（読込時は XML を正とする。spec の「読込時の適用」）

NG があった場合は、superpowers:systematic-debugging で原因を調べて直す。

- [ ] **Step 4: ドキュメントの更新を確認する**

`W:\COM3D2_5\work\CLAUDE.md` はリポジトリの外なので、このリポジトリのコミットには含めない。追記したことを最終報告に書く。

---

## 完了後

1. code-review スキルでレビューし、指摘を取り込む（CLAUDE.md の必須工程）
2. commit スキルで残りの変更をコミットする
3. 利用者向けドキュメント（docs-site）と CHANGELOG の更新は、release-prep で行う（本計画の範囲外）

## レビュー却下メモ

- **「`ApplyEntitySettings` が名前一致の既存実体へ、表示タイプとデカール設定を無条件に適用する。renderQueue と同様のガードが要る」** — 却下
  - 板では要素を書き出さない仕様なので、要素が無いことは「保存時点で板だった」ことを意味する。板へ戻すのが正しい復元になる
  - renderQueue のガードは「0 が無効な値」だからで、「未指定を区別する」ためのものではない
  - 挙動は spec に明記し、Task 7 の項目 11 で確認する
- **「v36 のコメントを置く位置は、v35 のコメントが欠けた v34 の塊の直後で、慣習から外れる」** — 却下
  - バージョン履歴の塊が正規の置き場所で、v36 はそこに置く
  - v35 の欠落は既存の問題で、本計画の範囲外
- **「テストで初めて `Matrix4x4` を使う。InternalCall かどうか未確認」** — 未確認のまま見送り
  - Unity 2022 の `Matrix4x4.identity`、`MultiplyPoint3x4`、`operator*` は managed 実装と見込まれる
  - Task 1 の Step 4 で即座に判明する
  - 失敗した場合は、float の成分計算へ置き換える
