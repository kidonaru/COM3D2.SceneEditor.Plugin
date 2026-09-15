# サイリウム更新の軽量化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** シーク時の `PsylliumTimelineLayer.ApplyPlayData` が約 10ms かかる原因（キー境界ごとの全席再配置）を取り除き、毎フレームのハンド更新も並列化して軽くする。

**Architecture:** `PsylliumController.refreshRequired`（bool）を `[Flags] PsylliumRefreshKind`（Placement / Mesh / Material）へ拡張し、Bar/Hand 設定の差分から必要な再構築だけを `ManualUpdate` で実行する。差分分類は Unity 非依存の `PsylliumBarConfig` / `PsylliumHandConfig` 側に置いて xunit でテストする。コントローラー Transform の変更は setter が直接反映するので Refresh を要求しない。あわせて COM3D25 でも `PreUpdateTransform` を `ParallelHelper` で並列化し、`UpdateMesh` の配列確保をキャッシュ化する。

**Tech Stack:** C# (LangVersion は COM3D2 構成で .NET 3.5 相当の制約あり)、Unity、xunit (`source/COM3D2.SceneEditor.Plugin.Tests`)。

**Spec:** 本セッションの調査結果（実機計測）:

| 処理 | 時間 |
|---|---|
| 通常フレームの `ApplyPlayData` | 0.95〜1.8 ms |
| `ApplyPlayData`（refreshRequired あり） | 6.7 ms |
| `PsylliumController.Refresh()` | 6.2 ms（うち `PsylliumArea.Refresh()` ×4 = 5.4ms、`UpdateMeshs()` = 1.0ms、`UpdateMaterials()` = 0.01ms） |
| `PreUpdateTransform` 5116 個 直列 / 並列 | 0.84 ms / 0.25 ms |

根本原因: `PsylliumTimelineLayer.ApplyControllerMotionInit` が無条件に `controller.refreshRequired = true` を立てるため、コントローラーキーの境界をまたぐたびに全席再配置とメッシュ再生成が走る。

## Global Constraints

- COM3D2 構成（.NET 3.5）と COM3D25 構成（.NET 4.7.1）の両方でビルドが通ること。入力 5 個以上の `Func<>`/`Action<>`・`Tuple`・`CallerMemberName` は使わない
- 既存の `refreshRequired` 書き込み箇所（`LiveEffectSnapshot.cs:464,475`、`PsylliumArea.SetPlacement`、Unity Editor の `OnValidate`）は無変更で動くこと（= 全再構築のまま）
- MonoBehaviour はテストから生成できない。テスト対象は POCO の Config クラスに限る
- コードのコメントは日本語
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
| `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumConfig.cs` | `PsylliumRefreshKind` enum を追加。`PsylliumBarConfig.GetRefreshKind(other)` / `PsylliumHandConfig.GetRefreshKind(other)` で差分を分類 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs` | `refreshKind` フィールド + `refreshRequired` 互換プロパティ、`RequestRefresh(kind)`、`ManualUpdate` の部分再構築、`UpdateMesh` の配列キャッシュ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs` | `UpdateTransform` の `#if COM3D2` ガード撤去（常に並列） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs` | `ApplyControllerMotionInit` の無条件 Refresh 撤去、Bar/Hand Init で `RequestRefresh(GetRefreshKind)` |
| `source/COM3D2.SceneEditor.Plugin.Tests/PsylliumRefreshKindTests.cs` | 差分分類のテスト |

---

### Task 1: `ApplyControllerMotionInit` の無条件 Refresh を撤去する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs:263-278`

**Interfaces:**
- Consumes: `PsylliumController.visible` / `position` / `eulerAngles` の setter（各自 `transform` / `SetActive` を直接更新し、`Refresh()` は Transform に触らない）
- Produces: なし

- [ ] **Step 1: `refreshRequired = true` 行を削除し、理由コメントを付ける**

```csharp
        private void ApplyControllerMotionInit(MotionData motion, float t)
        {
            var controller = psylliumManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumController;

            // 各 setter が transform / SetActive を直接反映するので席の再配置は不要。
            // ここで refreshRequired を立てるとキー境界ごとに全席再配置（約 6ms）が走る
            controller.visible = start.visible;
            controller.position = start.position;
            controller.eulerAngles = start.eulerAngles;
        }
```

- [ ] **Step 2: 両構成ビルド**

Run: Global Constraints のビルドコマンド（COM3D2 → COM3D25）
Expected: エラー 0

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs
git commit -m "perf(psyllium): コントローラーキー適用時の無条件な全席再配置をやめる"
```

---

### Task 2: `PsylliumRefreshKind` と Config の差分分類（テスト付き）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumConfig.cs`（`PsylliumBarConfig.Equals` の直後、`PsylliumHandConfig.Equals` の直後）
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/PsylliumRefreshKindTests.cs`

**Interfaces:**
- Produces:
  - `[System.Flags] public enum PsylliumRefreshKind { None = 0, Placement = 1, Mesh = 2, Material = 4, All = Placement | Mesh | Material }`（namespace `COM3D2.MotionTimelineEditor.Plugin`）
  - `public PsylliumRefreshKind PsylliumBarConfig.GetRefreshKind(PsylliumBarConfig other)`
  - `public PsylliumRefreshKind PsylliumHandConfig.GetRefreshKind(PsylliumHandConfig other)`

分類の根拠:
- Bar: `color1a..color2c` / `cutoffAlpha` は `UpdateMaterials` のみで使う → `Material`
- Bar: `width` / `height` / `positionY` / `radius` / `topThreshold` は `UpdateMesh` のみで使う → `Mesh`
- Bar: `baseScale` は `UpdateMesh` と席配置（`Area.Refresh` / `UpdatePsylliums`）の両方 → `Placement | Mesh`
- Hand: `handSpacing` / `barOffsetPosition` / `barOffsetRotation` は `Area.Refresh` 経由で `UpdatePsylliums` が使う → `Placement`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PsylliumRefreshKindTests
    {
        [Fact]
        public void BarConfig_SameValues_ReturnsNone()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig();
            Assert.Equal(PsylliumRefreshKind.None, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_ColorOrCutoff_ReturnsMaterialOnly()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { color1a = Color.red, cutoffAlpha = 0.1f };
            Assert.Equal(PsylliumRefreshKind.Material, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_ShapeValues_ReturnsMeshOnly()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { width = 0.5f, height = 0.5f, positionY = 0.3f, radius = 0.2f, topThreshold = 0.4f };
            Assert.Equal(PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_BaseScale_ReturnsPlacementAndMesh()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { baseScale = 2f };
            Assert.Equal(PsylliumRefreshKind.Placement | PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_MixedChanges_ReturnsUnion()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { color2b = Color.cyan, width = 0.3f };
            Assert.Equal(PsylliumRefreshKind.Material | PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void HandConfig_SameValues_ReturnsNone()
        {
            var a = new PsylliumHandConfig();
            var b = new PsylliumHandConfig();
            Assert.Equal(PsylliumRefreshKind.None, a.GetRefreshKind(b));
        }

        [Fact]
        public void HandConfig_AnyChange_ReturnsPlacement()
        {
            var a = new PsylliumHandConfig();
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { handSpacing = 1f }));
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { barOffsetPosition = Vector3.one }));
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { barOffsetRotation = Vector3.one }));
        }
    }
}
```

- [ ] **Step 2: テストが失敗（コンパイルエラー）することを確認**

Run: 両構成ビルド後に `dotnet test --nologo -v q --filter PsylliumRefreshKindTests`
Expected: `PsylliumRefreshKind` / `GetRefreshKind` 未定義でコンパイルエラー

- [ ] **Step 3: enum と分類メソッドを実装**

`PsylliumConfig.cs` の `namespace` 直下（`PsylliumBarConfig` の前）に追加:

```csharp
    /// <summary>
    /// サイリウムの設定変更で必要になる再構築の種類。
    /// 席の再配置は約 6ms かかるので、色やメッシュだけの変更では走らせない
    /// </summary>
    [System.Flags]
    public enum PsylliumRefreshKind
    {
        None = 0,
        Placement = 1, // 席の再配置（PsylliumArea.Refresh）
        Mesh = 2,      // バーメッシュの再生成（UpdateMeshs）
        Material = 4,  // マテリアル色の更新（UpdateMaterials）
        All = Placement | Mesh | Material,
    }
```

`PsylliumBarConfig.Equals` の直後に追加:

```csharp
        /// <summary>other へ変更したときに必要な再構築の種類を返す（other は non-null 前提。Equals と同じ）</summary>
        public PsylliumRefreshKind GetRefreshKind(PsylliumBarConfig other)
        {
            var kind = PsylliumRefreshKind.None;

            if (color1a != other.color1a
                || color1b != other.color1b
                || color1c != other.color1c
                || color2a != other.color2a
                || color2b != other.color2b
                || color2c != other.color2c
                || cutoffAlpha != other.cutoffAlpha)
            {
                kind |= PsylliumRefreshKind.Material;
            }

            if (width != other.width
                || height != other.height
                || positionY != other.positionY
                || radius != other.radius
                || topThreshold != other.topThreshold)
            {
                kind |= PsylliumRefreshKind.Mesh;
            }

            // baseScale はメッシュ寸法と席の間隔の両方に効く
            if (baseScale != other.baseScale)
            {
                kind |= PsylliumRefreshKind.Placement | PsylliumRefreshKind.Mesh;
            }

            return kind;
        }
```

`PsylliumHandConfig.Equals` の直後に追加:

```csharp
        /// <summary>other へ変更したときに必要な再構築の種類を返す（全項目が席配置に効く）</summary>
        public PsylliumRefreshKind GetRefreshKind(PsylliumHandConfig other)
        {
            return Equals(other) ? PsylliumRefreshKind.None : PsylliumRefreshKind.Placement;
        }
```

- [ ] **Step 4: 両構成ビルド + テスト**

Run: Global Constraints のコマンド一式
Expected: ビルド成功、`PsylliumRefreshKindTests` 7 件 PASS、既存テストも PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumConfig.cs source/COM3D2.SceneEditor.Plugin.Tests/PsylliumRefreshKindTests.cs
git commit -m "feat(psyllium): 設定差分から必要な再構築種別を求める PsylliumRefreshKind を追加"
```

---

### Task 3: `PsylliumController` を種別ごとの部分再構築にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs:42`（`public bool refreshRequired;`）、`:182-193`（`Refresh`）、`:403-424`（`ManualUpdate`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs:280-330`（`ApplyBarConfigMotionInit` / `ApplyHandConfigMotionInit`）

**Interfaces:**
- Consumes: Task 2 の `PsylliumRefreshKind`、`GetRefreshKind`
- Produces:
  - `public PsylliumRefreshKind PsylliumController.refreshKind`（フィールド）
  - `public bool PsylliumController.refreshRequired { get; set; }`（互換: get は `refreshKind != None`、set true は `All`、false は `None`）
  - `public void PsylliumController.RequestRefresh(PsylliumRefreshKind kind)`（OR で積む）

- [ ] **Step 1: フィールドと互換プロパティ、`RequestRefresh` を追加**

`public bool refreshRequired;` を次に置き換える:

```csharp
        public PsylliumRefreshKind refreshKind;

        /// <summary>互換用。true で全再構築を要求する</summary>
        public bool refreshRequired
        {
            get { return refreshKind != PsylliumRefreshKind.None; }
            set { refreshKind = value ? PsylliumRefreshKind.All : PsylliumRefreshKind.None; }
        }

        /// <summary>必要な再構築種別を積む。次の ManualUpdate でまとめて実行される</summary>
        public void RequestRefresh(PsylliumRefreshKind kind)
        {
            refreshKind |= kind;
        }
```

- [ ] **Step 2: `Refresh()` を種別対応にし、無引数版は全再構築のまま残す**

```csharp
        public void Refresh()
        {
            Refresh(PsylliumRefreshKind.All);
        }

        public void Refresh(PsylliumRefreshKind kind)
        {
            if ((kind & PsylliumRefreshKind.Placement) != 0)
            {
                foreach (var area in areas)
                {
                    area.Refresh();
                }
            }

            if ((kind & PsylliumRefreshKind.Material) != 0)
            {
                UpdateMaterials();
            }

            if ((kind & PsylliumRefreshKind.Mesh) != 0)
            {
                UpdateMeshs();
            }

            refreshKind = PsylliumRefreshKind.None;
        }
```

- [ ] **Step 3: `ManualUpdate` の先頭を差し替え**

席の再配置は `area.refreshRequired` へ委譲する。`Refresh(kind)` から `area.Refresh()` を直接呼ぶと、`area.Refresh()` 末尾の `UpdateTransform()` と直後の `area.ManualUpdate()` 内の `UpdateTransform()` で 5116 ハンドの位置計算が同一フレームに 2 回走る（旧コードからある無駄）。`area.ManualUpdate()` は `refreshRequired` なら `Refresh()` して return するので、委譲すれば 1 回で済み、しかも `time` とパターン更新後の値で配置される。

```csharp
        public void ManualUpdate(float time)
        {
            if (refreshKind != PsylliumRefreshKind.None)
            {
                // 席の再配置は area.ManualUpdate に任せる（area.Refresh 内の UpdateTransform と
                // 直後の area.ManualUpdate で位置計算が二重に走るのを避ける）
                if ((refreshKind & PsylliumRefreshKind.Placement) != 0)
                {
                    foreach (var area in areas)
                    {
                        area.refreshRequired = true;
                    }
                }

                if ((refreshKind & PsylliumRefreshKind.Material) != 0)
                {
                    UpdateMaterials();
                }

                if ((refreshKind & PsylliumRefreshKind.Mesh) != 0)
                {
                    UpdateMeshs();
                }

                refreshKind = PsylliumRefreshKind.None;
            }

            this.time = time;

            foreach (var pattern in patterns)
            {
                pattern.ManualUpdate();
            }

            foreach (var area in areas)
            {
                area.ManualUpdate();
            }
        }
```

（未使用だった `var stopwatch = new StopwatchDebug();` とコメントアウト行は削除する）

- [ ] **Step 4: レイヤー側の Bar/Hand Init を差分種別に置き換え**

`ApplyBarConfigMotionInit` の末尾:

```csharp
            var start = motion.start as TransformDataPsylliumBar;
            var targetConfig = start.ToConfig();

            var kind = barConfig.GetRefreshKind(targetConfig);
            if (kind == PsylliumRefreshKind.None)
            {
                return;
            }

            barConfig.CopyFrom(targetConfig);
            controller.RequestRefresh(kind);
```

`ApplyHandConfigMotionInit` の末尾:

```csharp
            var start = motion.start as TransformDataPsylliumHand;
            var targetConfig = start.ToConfig();

            var kind = handConfig.GetRefreshKind(targetConfig);
            if (kind == PsylliumRefreshKind.None)
            {
                return;
            }

            handConfig.CopyFrom(targetConfig);
            controller.RequestRefresh(kind);
```

- [ ] **Step 5: 両構成ビルド + テスト**

Run: Global Constraints のコマンド一式
Expected: ビルド成功、全テスト PASS。`grep -rn "refreshRequired" source` で `LiveEffectSnapshot.cs` / `PsylliumArea.cs` / `PsylliumTimelineLayer.cs:355`（area 側）の参照が残っていてもプロパティ経由で動くこと

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PsylliumTimelineLayer.cs
git commit -m "perf(psyllium): 設定差分に応じて席配置・メッシュ・マテリアルを個別に再構築する"
```

---

### Task 4: COM3D25 でも `PreUpdateTransform` を並列化する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs:143-159`（`UpdateTransform`）

**Interfaces:**
- Consumes: `COM3D2.MotionTimelineEditor.ParallelHelper.ForEach<T>(List<T>, Action<T>)`
- 安全性の根拠: `PsylliumHand.PreUpdateTransform` は `controller.time`・`patterns` リスト・Config 値・自身のフィールドしか読まず、Unity API に触らない。書き込みは自身の `_calculatedPosition` / `_calculatedRotation` のみ。実機で 40 回並列実行して例外なし、0.84ms → 0.25ms

- [ ] **Step 1: `#if COM3D2` ガードを外す**

```csharp
        public void UpdateTransform()
        {
            // PreUpdateTransform は Unity API に触らず managed データしか読まないので並列化できる。
            // Transform への書き込みは UpdateTransform 側でメインスレッドから行う
            ParallelHelper.ForEach(hands, hand =>
            {
                hand.PreUpdateTransform();
            });

            foreach (var hand in hands)
            {
                hand.UpdateTransform();
            }
        }
```

- [ ] **Step 2: 両構成ビルド**

Run: Global Constraints のビルドコマンド
Expected: エラー 0

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumArea.cs
git commit -m "perf(psyllium): COM3D25 でもハンドの位置計算を並列化する"
```

---

### Task 5: `UpdateMesh` の配列確保をキャッシュ化する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs:246-322`（`UpdateMesh`）

**Interfaces:** なし（内部変更のみ）

- [ ] **Step 1: 配列をフィールドへ移し、`UpdateMesh` を書き換える**

`UpdateMeshs()` の直前に追加:

```csharp
        // UpdateMesh 用の作業配列。頂点 8 個固定なので使い回す（メインスレッドから逐次呼ぶ前提。
        // Mesh のセッターは配列をコピーするので、書き込み後に再利用してよい）
        private readonly Vector3[] _meshVertices = new Vector3[8];
        private readonly Vector2[] _meshUv = new Vector2[8];
        private readonly Vector2[] _meshUv2 = new Vector2[8];
        private static readonly int[] MeshTriangles = new int[] {
            0, 1, 2,
            1, 3, 2,
            1, 4, 3,
            4, 5, 3,
            4, 6, 5,
            6, 7, 5,
        };
```

`UpdateMesh` 本体:

```csharp
        public void UpdateMesh(int colorIndex)
        {
            var halfWidth = barConfig.width * 0.5f * barConfig.baseScale;
            var barHeight = barConfig.height * barConfig.baseScale;
            var barRadius = barConfig.radius * barConfig.baseScale;
            var positionY = barConfig.positionY * barConfig.baseScale;
            var barTopThreshold = barConfig.topThreshold;

            var vertices = _meshVertices;
            vertices[0] = new Vector3(-halfWidth, positionY, 0);
            vertices[1] = new Vector3(-halfWidth, positionY, 0);
            vertices[2] = new Vector3( halfWidth, positionY, 0);
            vertices[3] = new Vector3( halfWidth, positionY, 0);
            vertices[4] = new Vector3(-halfWidth, positionY + barHeight, 0);
            vertices[5] = new Vector3( halfWidth, positionY + barHeight, 0);
            vertices[6] = new Vector3(-halfWidth, positionY + barHeight, 0);
            vertices[7] = new Vector3( halfWidth, positionY + barHeight, 0);

            var uv = _meshUv;
            uv[0] = new Vector2(0, 0);
            uv[1] = new Vector2(0, barTopThreshold);
            uv[2] = new Vector2(1, 0);
            uv[3] = new Vector2(1, barTopThreshold);
            uv[4] = new Vector2(0, 1 - barTopThreshold);
            uv[5] = new Vector2(1, 1 - barTopThreshold);
            uv[6] = new Vector2(0, 1);
            uv[7] = new Vector2(1, 1);

            // uv2.y は colorIndex（シェーダー側で色セットの選択に使う）
            var uv2 = _meshUv2;
            uv2[0] = new Vector2(-barRadius, colorIndex);
            uv2[1] = new Vector2(0, colorIndex);
            uv2[2] = new Vector2(-barRadius, colorIndex);
            uv2[3] = new Vector2(0, colorIndex);
            uv2[4] = new Vector2(0, colorIndex);
            uv2[5] = new Vector2(0, colorIndex);
            uv2[6] = new Vector2(barRadius, colorIndex);
            uv2[7] = new Vector2(barRadius, colorIndex);

            var mesh = meshes[colorIndex];
            mesh.Clear();

            mesh.subMeshCount = 2;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = uv2;

            mesh.SetTriangles(MeshTriangles, 0);
            mesh.SetTriangles(MeshTriangles, 1);
        }
```

（元の `vertices` / `uv` / `uv2` / `triangles` のローカル `new` と後続の `for` ループは削除。頂点の `y += positionY` は初期化時に加算済み、`uv2.y = colorIndex` も初期化時に設定済みなので値は元と同じ）

- [ ] **Step 2: 両構成ビルド + テスト**

Run: Global Constraints のコマンド一式
Expected: ビルド成功、全テスト PASS

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PsylliumController.cs
git commit -m "perf(psyllium): バーメッシュ生成の作業配列を使い回す"
```

---

### Task 6: 実機検証

**Files:** なし

- [ ] **Step 1: ゲーム停止中なら `debug.bat com3d25` で DLL を反映、起動中なら CLAUDE.md のホットリロード手順で反映**

- [ ] **Step 2: devbridge で計測**

`profile_add` を `COM3D2.MotionTimelineEditor.Plugin.PsylliumTimelineLayer:ApplyPlayData` に 300 フレーム仕掛け、タイムラインをシーク・再生する。

Expected:
- 通常フレーム: 1.8ms → 約 1.2ms 以下（並列化分）
- シーク時（コントローラーキー境界）: `ApplyPlayData: PsylliumTimelineLayer: 10ms` のログが 1〜2ms になる
- バー色だけ変わるキー境界では席が再配置されない（サイリウムの配置がチラつかない）
- `tail_log` に `ParallelHelper` 由来の例外が出ない

- [ ] **Step 3: 見た目の確認**

`screenshot` でサイリウムが以前と同じ位置・色・形で描画されていることを確認する（メッシュ配列キャッシュ化で頂点値が変わっていないこと）。

---

## Self-Review

- 調査で挙げた 1〜4 は Task 1 / Task 2+3 / Task 4 / Task 5 に対応
- `refreshRequired` を書く既存 3 箇所は互換プロパティで無変更
- `PsylliumArea.refreshRequired`（bool）は変更対象外。Area 設定の変更は従来どおり Area 単位の再配置
- Task 3 の `RequestRefresh` / `refreshKind` / `Refresh(kind)` は Task 2 の enum 名と一致
- Task 3 の `ManualUpdate` は Placement を `area.refreshRequired` へ委譲し、位置計算の二重実行を避ける（plan-review の指摘を反映）

## レビュー却下メモ

- `PsylliumBarConfig` の `groupIndex` / `name` / `displayName` が `GetRefreshKind` の対象外である旨をコメントで触れる — 既存の `Equals` / `CopyFrom` と同じ扱いで自明。コメントを増やす価値が薄いため見送り
