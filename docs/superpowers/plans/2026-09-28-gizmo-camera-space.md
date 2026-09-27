# ギズモのカメラ座標系 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ギズモの座標系を `Local` / `Global` / `Camera` の 3 値にする。`Camera` では軸がそのギズモを見ているカメラの右・上・前になり、回転ツールには視線まわりを回す外周リングが付く。

**Architecture:**
- 座標系の選び方 (ツールごとの実効座標系・軸 3 本の選択・出すハンドル・画面ドラッグ角) は `TransformGizmo.cs` の静的な純関数に集め、テストで固定する。ギズモ本体は描画・判定の入口で「今の軸 3 本」(`GizmoBasis`) を 1 回求めて使い回し、ドラッグ開始時に軸と面の法線を固定する
- `Camera` の回転は、視線を含む面にある右・上のリングが画面上で線に潰れるため、面との交点ではなくマウスの移動量を角度に換算する (トラックボール式)。視線まわりは画面に正対する外周リングで、従来どおり面との交点で角度を取る
- 3 値は `GizmoRenderer.gizmoSpace` (Config の `gizmoSpace`) が持ち、既存の `GizmoRenderer.useLocalSpace` (bool) は `gizmoSpace == Local` を返す互換プロパティとして残す。Inspector の数値欄はこの bool を読んでいるので、`Camera` では自動的にワールド表示になる

**Tech Stack:** C# (Unity IMGUI / GL、COM3D2 両ビルド)、xunit (net48)、MTEUtils サブモジュール

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「11. ギズモの座標系にカメラ方向」

## 仕様

仕様書 #11 の各項目と、本計画で決めた解釈。

### 座標系

- `Local` / `Global` / `Camera` の 3 値。`Camera` の軸は、そのギズモを描いている・掴んだカメラの右・上・前 (`camera.transform.right/up/forward`)。SceneView は SceneView カメラ、GameView はメインカメラ (`GizmoRenderer._camera`) なので、ビューごとに軸が違う
- ドラッグ中は開始時の軸・面の法線を固定する。ドラッグ中にカメラが動いても (タイムライン再生・自動フォーカス等) 操作方向は変わらない
- 拡縮ツールは `Camera` でも `Local` で描画・操作する (`localScale` はローカル軸でしか伸びないため)
- ボーン回転ギズモ (ボーン表示中の関節) は従来どおり `Local` 固定

### Camera のハンドル (本計画での判断)

- 移動: 右・上の矢印と、画面に平行な面ハンドル (法線 = 前軸) だけを出す。前軸の矢印は画面上で点に潰れて掴むと奥行きが飛ぶため出さない。視線を含む残り 2 面のハンドルも線に潰れるため出さない。奥行き方向の移動は `Local` / `Global` で行う
- 回転:
  - 視線まわり (前軸) は、画面に正対する白い外周リング (軸長の 1.2 倍の全周) で回す。角度はリングの面との交点から取る (従来のリングと同じ方式)
  - 右・上軸のリングは画面上で縦線・横線に潰れて描かれる。この線を掴み、線に沿ってドラッグするとリングの手前側がマウスに付いて転がる向きに回る (半径ぶん動かすと 1 rad)
- 外周リングは `Camera` のときだけ出す。`Local` / `Global` の見た目は変えない (TransformGizmo は ModItemExplorer にも入るため、既存の座標系の見た目を変えない)

### UI・設定・互換

- 既存の地球儀アイコンのボタン (Inspector のギズモ行と SceneView ツールバー) を、押すたびに `Local` → `Global` → `Camera` → `Local` と巡回させる。`Local` は消灯、`Global` は地球儀の点灯、`Camera` は既存のカメラアイコン (`ToolbarIcons.Kind.Camera`) の点灯。ツールチップは `座標系: Local` / `座標系: Global` / `座標系: Camera`
- Inspector の位置・回転の数値欄 (オブジェクト・ポーズボーン位置・スロットボーン・モデルボーン) は、`Camera` のときワールドとして表示・編集する (`GizmoRenderer.useLocalSpace` が false を返すことで実現。4 箇所のコードは変えない)
- 設定は `gizmoSpace` (既定 `Local`) を足し、`gizmoUseLocalSpace` は残す。書き込み時は両方を揃える (`Camera` は `gizmoUseLocalSpace = false`)。読み込み時に両者が食い違っていたら、旧版で bool 側だけ書き換えられたとみなして bool から `gizmoSpace` を作り直す (`gizmoSpace` の無い旧設定もこの規則で移行される)。`Config.CurrentVersion` は上げない
- 外部連携 (`GizmoToolClient`) はホストの `useLocalSpace` が `public static bool` で setter を持つことをリフレクションで要求しているので、型・名前を変えない。ホストが `Camera` のとき bool は false (ModItemExplorer 側は `Global` 表示・`Global` で動く)。false の代入では `Camera` を保ち、true の代入で `Local` になる。3 値の同期は足さない
- `TransformGizmo.useLocalSpace` はフィールドから同じ意味のプロパティに変える (ModItemExplorer のオブジェクト初期化子・代入はそのままコンパイルできる)。`GizmoToolRowOption` の `getUseLocalSpace` / `setUseLocalSpace` も残し、3 値用の `getSpace` / `setSpace` を設定しない利用側は従来の 2 値トグルのまま

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること。`Vector2Int` は 2.0 の Unity に無いので使わない。COM3D2 構成は .NET 3.5 なので入力 5 個以上の `Func<>` / `Action<>` は使わない
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`Quaternion.Euler`, `Quaternion.AngleAxis`, `GUI.*`, `Transform` のプロパティ, `Camera.*` 等) はテストから呼べない。`Mathf` / `Vector2` / `Vector3` の算術・`Vector3.Cross` は可
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)。テストプロジェクトは SDK 形式なので追加不要
- **`MTEUtils/` はサブモジュールで、本計画では変更する** (`TransformGizmo.cs` / `GizmoToolRowDrawer.cs` / `GizmoToolClient.cs`)。MTEUtils には新規ファイルを作らない (ModItemExplorer の csproj も MTEUtils のファイルを個別に列挙しているため、ファイルを足すと他リポの csproj 更新が要る)。新しい型 (`GizmoSpace` / `GizmoBasis`) は `TransformGizmo.cs` に置く
- MTEUtils の公開 API は互換を保つ: `TransformGizmo.useLocalSpace` (bool、読み書き可)、`GizmoToolRowOption.getUseLocalSpace` / `setUseLocalSpace` / `globalIcon`、`GizmoToolRowDrawer.DrawSpaceButton` / `GetSpaceButtonWidth` のシグネチャ。SceneEditor 側の `GizmoRenderer.useLocalSpace` は `public static bool` で setter を持つこと (`GizmoToolClient.Initialize` が要求)
- MTEUtils の変更は (1) サブモジュール内 (`git -C source/COM3D2.SceneEditor.Plugin/MTEUtils ...`、ブランチ `master`) で先にコミット → (2) 親リポで `git add source/COM3D2.SceneEditor.Plugin/MTEUtils` して参照を進め、SceneEditor 側の変更と一緒にコミットする。ModItemExplorer / PostEffects の参照更新と push は本計画の範囲外 (ユーザー判断)
- 実機検証は通常シーン (デイリー画面でエディタを有効化。撮影モード非対応)。ビルドした DLL の反映は `com3d25-devbridge:restart-verify` スキルで行う (GizmoRenderer は MonoBehaviour なのでホットリロードできない)
- 新しい Unity API は使わない (`Plane` / `Quaternion.AngleAxis` / `Camera.WorldToScreenPoint` / `GL.*` は既存コードと同じ使い方)
- プラグイン間連携はリフレクション経由 (`W:\COM3D2_5\work\CLAUDE.md`)。本計画で新たな連携は足さない

## Review Focus

1. `Camera` の回転で、右・上の線をドラッグしたときリングの手前側がマウスに付いて動く (上へドラッグ → 手前が上へ転がる、右へドラッグ → 手前が右へ転がる)。逆回転だと直感に反する — Task 1 のテスト「画面ドラッグ角の符号は回転の微分と一致する」、Task 3 の実機確認
2. `Camera` で掴んでいる最中にカメラが動いても (SceneView の自動フォーカス、GameView でのタイムライン再生) 操作方向が開始時のまま — Task 3 でドラッグ開始時に `_dragAxisDir` / `_dragPlaneNormal` を固定、実機確認
3. 旧版で `Global` に切り替えた設定 (`gizmoSpace` 要素なし、`gizmoUseLocalSpace` = false) を読むと `Global` で開き、`Camera` で保存した設定を旧版で開くと `Global` になる — Task 2 の移行テスト
4. ModItemExplorer 併用時、SceneEditor 側を `Camera` にしても ModItemExplorer の同期で `Global` に戻されない (MIE は自分の値が変わったときだけ書き込み、書き込むのは false) — Task 1 のテスト「互換 bool に false を代入しても Camera を保つ」、Task 2 で `GizmoRenderer.useLocalSpace` も同じ規則、Task 4 の実機確認
5. `Camera` で拡縮ツールに切り替えると、ローカル軸のギズモが出て掴んだ軸がそのまま伸びる (カメラ軸の見た目でローカル軸が伸びる食い違いにならない) — Task 1 のテスト「拡縮はカメラでもローカル」、Task 3 の実機確認
6. `Camera` の移動ツールで、ギズモの中心付近をクリックしても奥行き方向に飛ばない (前軸の矢印と潰れた面ハンドルを掴ませない) — Task 1 のテスト「カメラでは前軸の矢印と視線を含む面を出さない」、Task 3 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs` | `GizmoSpace` / `GizmoBasis`、座標系の純関数、カメラ座標系の描画・判定・ドラッグ |
| Modify `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolRowDrawer.cs` | 軸空間ボタンの 3 値巡回 (任意機能。未設定なら従来の 2 値) |
| Modify `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolClient.cs` | `useLocalSpace` の意味 (Camera は false) をコメントに記す |
| Modify `source/COM3D2.SceneEditor.Plugin/Config.cs` | `gizmoSpace` の追加と旧設定との整合 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` | `gizmoSpace` / 互換 `useLocalSpace`、ギズモへの反映、ボタン設定 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs` | 座標系の純関数・互換 bool・巡回順のテスト |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/ConfigGizmoSpaceMigrationTests.cs` | 設定の移行テスト |
| Modify `docs-site/guide/scene-view.md` / `configuration.md` / `maid-editing.md`、`docs-site/dev/misc-guest-guide.md` | 説明 |

変更しないが挙動が変わるもの: `InspectorWindow.cs:420,468` / `ObjectTransformRowDrawer.cs:27` / `ModelBoneRowDrawer.cs:36` (いずれも `GizmoRenderer.useLocalSpace` を読むので `Camera` でワールド表示になる)。`BoneEditManager` / `EulerOffsetCache` は bool 引数のままで、`Global` ⇔ `Camera` の切り替えでは bool が変わらないためキャッシュも保たれる。

---

### Task 1: 座標系の型と純関数

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs` (enum `GizmoTargetType` の後に型を足す、クラス先頭のフィールド `useLocalSpace` (:34) を置き換え、`CalcGizmoSizeFromHalfHeight` (:152-155) の後に純関数を足す)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs`

**Interfaces:**
- Produces (すべて namespace `COM3D2.MotionTimelineEditor`):
  - `enum GizmoSpace { Local, Global, Camera }`
  - `struct GizmoBasis { Vector3 right, up, forward; GizmoBasis(Vector3 right, Vector3 up, Vector3 forward); static readonly GizmoBasis world; static GizmoBasis FromTransform(Transform t); Vector3 this[int axis] { get; } }` (軸番号 0/1/2 = 右/上/前)
  - `TransformGizmo.space : GizmoSpace` (フィールド、既定 `Local`)
  - `TransformGizmo.useLocalSpace : bool` (プロパティ。get = `space == Local`、set true → `Local`、set false → `Camera` なら据え置き・それ以外は `Global`)
  - `TransformGizmo.ViewAxis = 2` (const int)
  - `TransformGizmo.ResolveSpace(GizmoSpace space, GizmoTool tool) : GizmoSpace`
  - `TransformGizmo.SelectBasis(GizmoSpace space, GizmoBasis local, GizmoBasis camera) : GizmoBasis`
  - `TransformGizmo.IsAxisHandleEnabled(GizmoSpace space, int axis) : bool`
  - `TransformGizmo.IsPlaneHandleEnabled(GizmoSpace space, int normalAxis) : bool`
  - `TransformGizmo.IsViewRing(GizmoSpace space, int axis) : bool`
  - `TransformGizmo.IsScreenDragRing(GizmoSpace space, int axis) : bool`
  - `TransformGizmo.CalcScreenDragAngle(int axis, Vector2 delta, float radiusPixels) : float` (度)

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ギズモの座標系 (Local / Global / Camera) の選び方を固定する。
    /// Transform / Camera の実体は Unity ランタイムが要るため、軸 3 本を直接渡す純関数側で確かめる
    /// </summary>
    public class GizmoSpaceTests
    {
        // Y 軸まわりに 90 度回した対象の軸 (右 = -Z、前 = +X)
        private static readonly GizmoBasis Local = new GizmoBasis(
            new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f));

        // 下を向いたカメラの軸 (上 = +Z、前 = -Y)
        private static readonly GizmoBasis Camera = new GizmoBasis(
            new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, -1f, 0f));

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 4);
            Assert.Equal(expected.y, actual.y, 4);
            Assert.Equal(expected.z, actual.z, 4);
        }

        [Theory]
        [InlineData(GizmoSpace.Local, GizmoTool.Move, GizmoSpace.Local)]
        [InlineData(GizmoSpace.Global, GizmoTool.Scale, GizmoSpace.Global)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Move, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Rotate, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Scale, GizmoSpace.Local)]
        public void 拡縮はカメラでもローカル(GizmoSpace space, GizmoTool tool, GizmoSpace expected)
        {
            Assert.Equal(expected, TransformGizmo.ResolveSpace(space, tool));
        }

        [Fact]
        public void 座標系ごとに軸3本の出どころを選ぶ()
        {
            var local = TransformGizmo.SelectBasis(GizmoSpace.Local, Local, Camera);
            AssertVector(Local.right, local[0]);
            AssertVector(Local.up, local[1]);
            AssertVector(Local.forward, local[2]);

            var global = TransformGizmo.SelectBasis(GizmoSpace.Global, Local, Camera);
            AssertVector(Vector3.right, global[0]);
            AssertVector(Vector3.up, global[1]);
            AssertVector(Vector3.forward, global[2]);

            var camera = TransformGizmo.SelectBasis(GizmoSpace.Camera, Local, Camera);
            AssertVector(Camera.right, camera[0]);
            AssertVector(Camera.up, camera[1]);
            AssertVector(Camera.forward, camera[2]);
        }

        [Fact]
        public void カメラでは前軸の矢印と視線を含む面を出さない()
        {
            for (var axis = 0; axis < 3; axis++)
            {
                Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Local, axis));
                Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Global, axis));
                Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Local, axis));
                Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Global, axis));
            }

            Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 0));
            Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 1));
            Assert.False(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 2));

            // 画面に平行な面 (法線 = 前軸) だけ残す
            Assert.False(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 0));
            Assert.False(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 1));
            Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 2));
        }

        [Fact]
        public void カメラの回転は前軸が外周リングで右上は画面ドラッグ()
        {
            Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Camera, 0));
            Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Camera, 1));
            Assert.True(TransformGizmo.IsViewRing(GizmoSpace.Camera, 2));
            Assert.True(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 0));
            Assert.True(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 1));
            Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 2));

            for (var axis = 0; axis < 3; axis++)
            {
                Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Local, axis));
                Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Global, axis));
                Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Local, axis));
                Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Global, axis));
            }
        }

        [Fact]
        public void 画面ドラッグは半径ぶんで1ラジアン回る()
        {
            Assert.Equal(Mathf.Rad2Deg, TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 100f), 100f), 3);
            Assert.Equal(-Mathf.Rad2Deg, TransformGizmo.CalcScreenDragAngle(1, new Vector2(100f, 0f), 100f), 3);
            // 線に沿わない成分は効かない
            Assert.Equal(0f, TransformGizmo.CalcScreenDragAngle(0, new Vector2(50f, 0f), 100f), 3);
            Assert.Equal(0f, TransformGizmo.CalcScreenDragAngle(1, new Vector2(0f, 50f), 100f), 3);
        }

        [Fact]
        public void 画面上の半径が0でも角度は有限()
        {
            var angle = TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 10f), 0f);
            Assert.False(float.IsNaN(angle));
            Assert.False(float.IsInfinity(angle));
        }

        [Fact]
        public void 画面ドラッグ角の符号は回転の微分と一致する()
        {
            // カメラ座標で、リングの手前側の点はカメラ方向 (後ろ = -前) にある。
            // 軸 a まわりに正回転したときの点 r の動きは a × r
            var nearPoint = Vector3.back;

            // 右軸まわりの正回転は手前側を上 (+Y) へ動かす → 上へのドラッグ (+y) が正の角度
            Assert.True(Vector3.Cross(Vector3.right, nearPoint).y > 0f);
            Assert.True(TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 10f), 100f) > 0f);

            // 上軸まわりの正回転は手前側を左 (-X) へ動かす → 左へのドラッグ (-x) が正の角度
            Assert.True(Vector3.Cross(Vector3.up, nearPoint).x < 0f);
            Assert.True(TransformGizmo.CalcScreenDragAngle(1, new Vector2(-10f, 0f), 100f) > 0f);
        }

        [Fact]
        public void 互換boolはLocalだけtrue()
        {
            Assert.True(new TransformGizmo { space = GizmoSpace.Local }.useLocalSpace);
            Assert.False(new TransformGizmo { space = GizmoSpace.Global }.useLocalSpace);
            Assert.False(new TransformGizmo { space = GizmoSpace.Camera }.useLocalSpace);
        }

        [Fact]
        public void 互換boolにfalseを代入してもCameraを保つ()
        {
            var gizmo = new TransformGizmo { space = GizmoSpace.Camera };

            gizmo.useLocalSpace = false;
            Assert.Equal(GizmoSpace.Camera, gizmo.space);

            gizmo.useLocalSpace = true;
            Assert.Equal(GizmoSpace.Local, gizmo.space);

            gizmo.useLocalSpace = false;
            Assert.Equal(GizmoSpace.Global, gizmo.space);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter GizmoSpaceTests`
Expected: ビルドエラー (`GizmoSpace` / `GizmoBasis` / `TransformGizmo.space` などが見つからない)

- [ ] **Step 3: 型を足す**

`TransformGizmo.cs` の `GizmoTargetType` enum の直後 (`/// <summary>カメラ非依存の ...` の前) に追加:

```csharp
    /// <summary>ギズモの軸の座標系</summary>
    public enum GizmoSpace
    {
        /// <summary>対象の回転に沿った軸</summary>
        Local,
        /// <summary>ワールド軸</summary>
        Global,
        /// <summary>ギズモを描いている (掴んだ) カメラの右・上・前</summary>
        Camera,
    }

    /// <summary>ギズモの軸 3 本の組。軸番号 0 / 1 / 2 = 右 / 上 / 前</summary>
    public struct GizmoBasis
    {
        public Vector3 right;
        public Vector3 up;
        public Vector3 forward;

        public static readonly GizmoBasis world =
            new GizmoBasis(Vector3.right, Vector3.up, Vector3.forward);

        public GizmoBasis(Vector3 right, Vector3 up, Vector3 forward)
        {
            this.right = right;
            this.up = up;
            this.forward = forward;
        }

        public static GizmoBasis FromTransform(Transform t)
        {
            return new GizmoBasis(t.right, t.up, t.forward);
        }

        public Vector3 this[int axis]
        {
            get
            {
                switch (axis)
                {
                    case 0: return right;
                    case 1: return up;
                    default: return forward;
                }
            }
        }
    }
```

- [ ] **Step 4: `useLocalSpace` をプロパティにし、`space` を足す**

`TransformGizmo.cs:34` の `public bool useLocalSpace = true;` を置き換える:

```csharp
        public GizmoSpace space = GizmoSpace.Local;

        /// <summary>
        /// 旧 API との互換用の bool 表現 (true = Local)。ModItemExplorer など 2 値で扱う利用側向け。
        /// Camera は Local ではないので false を返す。false の代入では Camera を保つ
        /// (bool しか知らない側は Global と Camera を区別できず、同期のたびに Camera が消えるため)
        /// </summary>
        public bool useLocalSpace
        {
            get { return space == GizmoSpace.Local; }
            set
            {
                if (value == useLocalSpace)
                {
                    return;
                }
                space = value ? GizmoSpace.Local : GizmoSpace.Global;
            }
        }
```

- [ ] **Step 5: 純関数を足す**

`CalcGizmoSizeFromHalfHeight` (:152-155) の直後に追加:

```csharp
        /// <summary>カメラ座標系で視線方向になる軸の番号 (前)</summary>
        public const int ViewAxis = 2;

        /// <summary>画面ドラッグ角の換算で、画面上の半径がこれより小さいときの下限 (px)。0 除算を避ける</summary>
        private const float MinScreenRadiusPixels = 1f;

        /// <summary>
        /// ツールに対して実際に使う座標系。拡縮は localScale をローカル軸で伸ばすだけなので、
        /// カメラ軸で描くと見た目の軸と伸びる方向が食い違う。カメラ指定でもローカルで動かす
        /// </summary>
        public static GizmoSpace ResolveSpace(GizmoSpace space, GizmoTool tool)
        {
            if (space == GizmoSpace.Camera && tool == GizmoTool.Scale)
            {
                return GizmoSpace.Local;
            }
            return space;
        }

        /// <summary>座標系に応じた軸 3 本。Global はワールド軸で、local / camera は使わない</summary>
        public static GizmoBasis SelectBasis(GizmoSpace space, GizmoBasis local, GizmoBasis camera)
        {
            switch (space)
            {
                case GizmoSpace.Local: return local;
                case GizmoSpace.Camera: return camera;
                default: return GizmoBasis.world;
            }
        }

        /// <summary>
        /// 軸ハンドル (移動・拡縮の矢印) を出すか。カメラ座標系の前軸は視線と重なって
        /// 画面上で点に潰れ、掴むと奥行きが大きく飛ぶため出さない
        /// </summary>
        public static bool IsAxisHandleEnabled(GizmoSpace space, int axis)
        {
            return space != GizmoSpace.Camera || axis != ViewAxis;
        }

        /// <summary>
        /// 面ハンドルを出すか。カメラ座標系では画面に平行な面 (法線が前軸) 以外は
        /// 視線を含んで線に潰れ、中心付近のクリックを奪うため出さない
        /// </summary>
        public static bool IsPlaneHandleEnabled(GizmoSpace space, int normalAxis)
        {
            return space != GizmoSpace.Camera || normalAxis == ViewAxis;
        }

        /// <summary>回転ツールで、画面に正対する外周リングとして描く軸か (カメラ座標系の前軸)</summary>
        public static bool IsViewRing(GizmoSpace space, int axis)
        {
            return space == GizmoSpace.Camera && axis == ViewAxis;
        }

        /// <summary>
        /// 回転ツールで、面との交点ではなく画面上の移動量で回す軸か (カメラ座標系の右・上)。
        /// このリングは視線を含む面にあり、画面上で線に潰れて角度が取れない
        /// </summary>
        public static bool IsScreenDragRing(GizmoSpace space, int axis)
        {
            return space == GizmoSpace.Camera && axis != ViewAxis;
        }

        /// <summary>
        /// カメラ座標系の右・上軸まわりのリングを画面ドラッグで回す角度 (度)。
        /// トラックボールと同じく、リングの手前側がマウスに付いて動く向きに回し、半径ぶん動かすと 1 rad。
        /// 回転の微分は「軸 × 点」で、手前側の点はカメラの後ろ向きにあるため、
        /// 右軸の正回転は手前側を画面の上へ、上軸の正回転は画面の左へ動かす
        /// </summary>
        /// <param name="axis">0 = 右、1 = 上</param>
        /// <param name="delta">ドラッグ開始からの RT ピクセル移動量 (左下原点)</param>
        /// <param name="radiusPixels">リング半径の画面上の長さ</param>
        public static float CalcScreenDragAngle(int axis, Vector2 delta, float radiusPixels)
        {
            var radius = Mathf.Max(radiusPixels, MinScreenRadiusPixels);
            var along = axis == 0 ? delta.y : -delta.x;
            return along / radius * Mathf.Rad2Deg;
        }
```

この時点では `AxisDirection` がまだ `useLocalSpace` (プロパティ) を読むため、既存の挙動は変わらない (`Camera` は Global として描かれる。Task 3 で置き換える)。

- [ ] **Step 6: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter GizmoSpaceTests` が PASS。続けて全テスト (`dotnet test source/COM3D2.SceneEditor.Plugin.Tests`) も PASS (`GizmoSizeFovTests` を含む)。

- [ ] **Step 7: コミット**

```bash
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils add TransformGizmo.cs
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils commit -m "feat(gizmo): ギズモの座標系 (Local/Global/Camera) の型と軸選択の純関数を追加する"
git add source/COM3D2.SceneEditor.Plugin/MTEUtils source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs
git commit -m "feat(gizmo): ギズモの座標系の軸選択をテストで固定する"
```

---

### Task 2: 3 値の設定と互換プロパティ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:93` (ギズモ設定)、`:613-624` (`ConvertVersion`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:17-38` (static 設定)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ConfigGizmoSpaceMigrationTests.cs`

**Interfaces:**
- Consumes: `GizmoSpace` (Task 1)
- Produces:
  - `Config.gizmoSpace : GizmoSpace` (既定 `Local`)、`Config.gizmoUseLocalSpace : bool` (残す)
  - `GizmoRenderer.gizmoSpace : GizmoSpace` (public static、get/set。set は `gizmoUseLocalSpace` も揃える)
  - `GizmoRenderer.useLocalSpace : bool` (public static、get = `gizmoSpace == Local`、set は Task 1 の `TransformGizmo.useLocalSpace` と同じ規則)

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ConfigGizmoSpaceMigrationTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ギズモの座標系設定の移行。3 値の gizmoSpace を後から足し、旧版互換の
    /// gizmoUseLocalSpace も残しているため、両者が食い違ったときの扱いを固定する
    /// </summary>
    public class ConfigGizmoSpaceMigrationTests
    {
        private static Config Load(string body)
        {
            var xml = "<?xml version=\"1.0\"?><Config version=\"2\">" + body + "</Config>";
            var serializer = new XmlSerializer(typeof(Config));
            using (var reader = new StringReader(xml))
            {
                var config = (Config) serializer.Deserialize(reader);
                config.ConvertVersion();
                return config;
            }
        }

        [Fact]
        public void gizmoSpaceの無い旧設定でGlobalならGlobalになる()
        {
            var config = Load("<gizmoUseLocalSpace>false</gizmoUseLocalSpace>");

            Assert.Equal(GizmoSpace.Global, config.gizmoSpace);
            Assert.False(config.gizmoUseLocalSpace);
            Assert.True(config.dirty);
        }

        [Fact]
        public void gizmoSpaceの無い旧設定でLocalならLocalのまま()
        {
            var config = Load("<gizmoUseLocalSpace>true</gizmoUseLocalSpace>");

            Assert.Equal(GizmoSpace.Local, config.gizmoSpace);
            Assert.False(config.dirty);
        }

        [Fact]
        public void Cameraとfalseの組は食い違いではないのでCameraを保つ()
        {
            var config = Load(
                "<gizmoUseLocalSpace>false</gizmoUseLocalSpace><gizmoSpace>Camera</gizmoSpace>");

            Assert.Equal(GizmoSpace.Camera, config.gizmoSpace);
            Assert.False(config.dirty);
        }

        [Theory]
        [InlineData("Camera")]
        [InlineData("Global")]
        public void 旧版でLocalへ戻した設定はLocalになる(string savedSpace)
        {
            // 旧版は gizmoSpace を知らず bool だけ書き換えるため、bool 側を正とする
            var config = Load(
                "<gizmoUseLocalSpace>true</gizmoUseLocalSpace><gizmoSpace>" + savedSpace + "</gizmoSpace>");

            Assert.Equal(GizmoSpace.Local, config.gizmoSpace);
            Assert.True(config.dirty);
        }

        [Fact]
        public void 旧版でGlobalへ切り替えた設定はGlobalになる()
        {
            var config = Load(
                "<gizmoUseLocalSpace>false</gizmoUseLocalSpace><gizmoSpace>Local</gizmoSpace>");

            Assert.Equal(GizmoSpace.Global, config.gizmoSpace);
            Assert.True(config.dirty);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ConfigGizmoSpaceMigrationTests`
Expected: ビルドエラー (`Config.gizmoSpace` が無い)

- [ ] **Step 3: Config に gizmoSpace と整合処理を足す**

`Config.cs:93` を置き換える:

```csharp
        /// <summary>
        /// 旧版互換の軸空間 (true = Local)。旧版は gizmoSpace を知らずこちらだけを読み書きするため残す。
        /// 書き込みは GizmoRenderer.gizmoSpace が gizmoSpace と揃えて行う (Camera は false)
        /// </summary>
        public bool gizmoUseLocalSpace = true;
        /// <summary>ギズモの軸の座標系 (Local / Global / Camera)</summary>
        public GizmoSpace gizmoSpace = GizmoSpace.Local;
```

`ConvertVersion` の `version = CurrentVersion;` の直前に追加:

```csharp
            ReconcileGizmoSpace();
```

`ConvertVersion` の直後にメソッドを足す:

```csharp
        /// <summary>
        /// gizmoSpace と旧版互換の gizmoUseLocalSpace が食い違っていれば、旧版で bool だけ
        /// 書き換えられたとみなして bool から作り直す。gizmoSpace の無い旧設定もここで移行される
        /// (要素が無いと既定の Local になり、bool が false なら Global へ直る)。
        /// Camera と false の組は正しい対応なので触らない
        /// </summary>
        private void ReconcileGizmoSpace()
        {
            if ((gizmoSpace == GizmoSpace.Local) == gizmoUseLocalSpace)
            {
                return;
            }
            gizmoSpace = gizmoUseLocalSpace ? GizmoSpace.Local : GizmoSpace.Global;
            dirty = true;
        }
```

`Config.cs` の先頭に `using COM3D2.MotionTimelineEditor;` が無ければ足す (`GizmoTargetType` を既に使っているので通常はある)。

- [ ] **Step 4: GizmoRenderer の static 設定を 3 値にする**

`GizmoRenderer.cs:26-38` の `useLocalSpace` を置き換える:

```csharp
        /// <summary>
        /// 軸の座標系。旧版互換の gizmoUseLocalSpace も同時に書き、
        /// 旧版で開いたときに Camera が Global として読まれるようにする
        /// </summary>
        public static GizmoSpace gizmoSpace
        {
            get => config.gizmoSpace;
            set
            {
                if (config.gizmoSpace == value)
                {
                    return;
                }
                config.gizmoSpace = value;
                config.gizmoUseLocalSpace = value == GizmoSpace.Local;
                config.dirty = true;
            }
        }

        /// <summary>
        /// 互換用の bool 表現 (true = Local)。ModItemExplorer の GizmoToolClient が
        /// public static bool で setter を持つことをリフレクションで要求しているため、型・名前を変えないこと。
        /// Camera は Local ではないので false を返し、Inspector の位置・回転の数値欄もワールドで表示する。
        /// false の代入では Camera を保つ (bool しか知らない側が同期で Camera を消さないように)
        /// </summary>
        public static bool useLocalSpace
        {
            get => gizmoSpace == GizmoSpace.Local;
            set
            {
                if (value == useLocalSpace)
                {
                    return;
                }
                gizmoSpace = value ? GizmoSpace.Local : GizmoSpace.Global;
            }
        }
```

`:17-23` のクラス冒頭の summary の「軸空間」の説明に「軸空間は Local / Global / Camera の 3 値 (gizmoSpace)。useLocalSpace は互換用」を 1 行足す。

- [ ] **Step 5: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全件 PASS (`ConfigKeyBindMigrationTests` の `dirty` 判定に影響しないこと: 既存テストの XML は `gizmoUseLocalSpace` を含まず既定 true と `Local` で一致するため `dirty` は変わらない)。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs source/COM3D2.SceneEditor.Plugin.Tests/ConfigGizmoSpaceMigrationTests.cs
git commit -m "feat(gizmo): ギズモの座標系を 3 値の設定で持ち、旧設定から移行する"
```

---

### Task 3: カメラ座標系の描画・判定・ドラッグ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs` (定数 :42-82、ドラッグ状態 :91-102、`AxisDirection` :162-181、`Draw` :183-225、`PlaneAxes` / `DrawPlaneHandle` :239-291、`DrawCircle` の後、`TryBeginDrag` :445-551、`IsInsidePlaneHandle` :553-577、`PlanePointAt` :595-610、`DistanceToCircle` の後、`UpdateDrag` :702-750、`UpdatePlaneDrag` :756-762、`EndDrag` :795-801)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:368,431,490`

**Interfaces:**
- Consumes: Task 1 の `GizmoSpace` / `GizmoBasis` / `ResolveSpace` / `SelectBasis` / `IsAxisHandleEnabled` / `IsPlaneHandleEnabled` / `IsViewRing` / `IsScreenDragRing` / `CalcScreenDragAngle` / `ViewAxis`、Task 2 の `GizmoRenderer.gizmoSpace`
- Produces: `TransformGizmo.Draw(Camera)` / `TryBeginDrag(Camera, Vector2)` / `UpdateDrag(Vector2)` の公開シグネチャは不変。`space` に従って描画・操作する

- [ ] **Step 1: 定数とドラッグ状態を足す**

`PlaneSizeRatio` (:82) の後に追加:

```csharp
        /// <summary>
        /// カメラ座標系の回転で視線まわりを回す外周リングの半径 (軸長に対する比)。
        /// 右・上の線 (長さ = 軸長) の端と離して掴み分けられるよう外側に置く
        /// </summary>
        private const float ViewRingRadiusRatio = 1.2f;
        /// <summary>外周リングは全周を描くので半周の倍の分割</summary>
        private const int FullCircleSegments = CircleSegments * 2;
        /// <summary>外周リングの色。軸の色と区別するため白にする</summary>
        private static readonly Color ViewRingColor = new Color(1f, 1f, 1f, 0.8f);
```

ドラッグ状態 (`_dragAxisDir` :102 の後) に追加:

```csharp
        // 面ドラッグの面の法線。軸と同じく開始時に固定する
        // (カメラ座標系ではドラッグ中にカメラが動くと面が変わってしまうため)
        private Vector3 _dragPlaneNormal;
        // 画面上の移動量で回しているか (カメラ座標系の右・上リング)
        private bool _dragByScreen;
        private Vector2 _dragStartRtPoint;
        private float _dragRadiusPixels;
```

- [ ] **Step 2: AxisDirection を「今の軸 3 本」に置き換える**

`AxisDirection` (:162-181) を削除し、代わりに追加:

```csharp
        /// <summary>ツールを考慮した実際の座標系 (拡縮のカメラはローカル)</summary>
        private GizmoSpace effectiveSpace
        {
            get { return ResolveSpace(space, tool); }
        }

        /// <summary>
        /// 今の座標系で使う軸 3 本。Transform のプロパティ取得を軸ごとに繰り返さないよう、
        /// 描画・判定の入口で 1 回だけ求めて使い回す。カメラ座標系では渡されたカメラの軸
        /// </summary>
        private GizmoBasis CurrentAxes(Camera camera)
        {
            return SelectBasis(effectiveSpace,
                GizmoBasis.FromTransform(target), GizmoBasis.FromTransform(camera.transform));
        }
```

- [ ] **Step 3: Draw を軸 3 本とカメラ座標系のハンドルに対応させる**

`Draw` の `var size = GizmoSize(camera, origin);` から `switch` の終わりまでを置き換える:

```csharp
            var size = GizmoSize(camera, origin);
            var drawSpace = effectiveSpace;
            var axes = CurrentAxes(camera);

            switch (tool)
            {
                case GizmoTool.Move:
                case GizmoTool.Scale:
                {
                    var isScale = tool == GizmoTool.Scale;
                    for (var axis = 0; axis < 3; axis++)
                    {
                        if (IsAxisHandleEnabled(drawSpace, axis))
                        {
                            DrawAxisLine(origin, axes[axis], size, AxisColor(axis), isScale);
                        }
                    }
                    // 2 軸を同時に動かす面ハンドル。移動は四角、拡縮は三角で GizmoRender と揃える
                    for (var axis = 0; axis < 3; axis++)
                    {
                        if (IsPlaneHandleEnabled(drawSpace, axis))
                        {
                            DrawPlaneHandle(origin, axes, axis, size * PlaneSizeRatio, PlaneColor(axis), isScale);
                        }
                    }
                    break;
                }
                case GizmoTool.Rotate:
                    for (var axis = 0; axis < 3; axis++)
                    {
                        if (IsViewRing(drawSpace, axis))
                        {
                            DrawFullCircle(origin, axes[axis], size * ViewRingRadiusRatio, ViewRingDrawColor(axis));
                        }
                        else
                        {
                            // カメラ座標系の右・上リングは視線を含む面にあり、画面上では線として描かれる
                            DrawCircle(camera, origin, axes[axis], size, AxisColor(axis));
                        }
                    }
                    break;
            }
```

`PlaneColor` の後に追加:

```csharp
        /// <summary>外周リングの色。掴んでいる間は軸と同じ選択色にする</summary>
        private Color ViewRingDrawColor(int axis)
        {
            return isDragging && _dragAxis == axis ? SelectedAxisColor : ViewRingColor;
        }
```

- [ ] **Step 4: 面ハンドルを軸 3 本から張る**

`PlaneAxes` と `DrawPlaneHandle` の冒頭を置き換える (本体の描画は変えない):

```csharp
        /// <summary>面ハンドルを張る 2 軸。法線の軸以外の 2 本を使う</summary>
        private static void PlaneAxes(GizmoBasis axes, int normalAxis, out Vector3 u, out Vector3 v)
        {
            u = axes[(normalAxis + 1) % 3];
            v = axes[(normalAxis + 2) % 3];
        }

        /// <summary>
        /// 2 軸を同時に動かす面ハンドル。半透明で塗ったうえで輪郭を描く
        /// (GizmoRender の DrawQuad / DrawTri と同じ見た目)
        /// </summary>
        private void DrawPlaneHandle(
            Vector3 origin, GizmoBasis axes, int normalAxis, float size, Color color, bool triangle)
        {
            Vector3 u, v;
            PlaneAxes(axes, normalAxis, out u, out v);
```

- [ ] **Step 5: 外周リングの描画と距離を足す**

`DrawCircle` の後に追加:

```csharp
        /// <summary>画面に正対する外周リング。手前・奥の区別が無いので全周を描く</summary>
        private static void DrawFullCircle(Vector3 center, Vector3 axis, float radius, Color color)
        {
            Vector3 basis1, basis2;
            CalcCircleBasis(axis, out basis1, out basis2);
            basis1 *= radius;
            basis2 *= radius;

            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < FullCircleSegments; i++)
            {
                GL.Vertex(ArcPoint(center, basis1, basis2, ArcAngle(i)));
                GL.Vertex(ArcPoint(center, basis1, basis2, ArcAngle(i + 1)));
            }
            GL.End();
        }
```

`DistanceToCircle` の後に追加:

```csharp
        /// <summary>外周リング (全周) までの画面距離。描画と同じ全周で判定する</summary>
        private float DistanceToFullCircle(
            Camera camera, Vector2 rtPoint, Vector3 center, Vector3 axis, float radius)
        {
            Vector3 basis1, basis2;
            CalcCircleBasis(axis, out basis1, out basis2);
            basis1 *= radius;
            basis2 *= radius;

            var best = float.MaxValue;
            for (var i = 0; i < FullCircleSegments; i++)
            {
                bool v0, v1;
                var p0 = ToRtPoint(camera, ArcPoint(center, basis1, basis2, ArcAngle(i)), out v0);
                var p1 = ToRtPoint(camera, ArcPoint(center, basis1, basis2, ArcAngle(i + 1)), out v1);
                if (v0 && v1)
                {
                    best = Mathf.Min(best, DistanceToSegment(rtPoint, p0, p1));
                }
            }
            return best;
        }

        /// <summary>
        /// 対象位置での長さ length の画面上のピクセル長。画面に平行な方向 (カメラの上) で測る。
        /// カメラの背後なら 0 (CalcScreenDragAngle 側で下限に丸める)
        /// </summary>
        private static float ScreenLength(Camera camera, Vector3 origin, float length)
        {
            bool v0, v1;
            var a = ToRtPoint(camera, origin, out v0);
            var b = ToRtPoint(camera, origin + camera.transform.up * length, out v1);
            return v0 && v1 ? Vector2.Distance(a, b) : 0f;
        }
```

- [ ] **Step 6: TryBeginDrag をカメラ座標系に対応させる**

`TryBeginDrag` の `var size = GizmoSize(camera, origin);` から `return true;` までを置き換える:

```csharp
            var size = GizmoSize(camera, origin);
            var dragSpace = effectiveSpace;
            var axes = CurrentAxes(camera);

            // 面ハンドルを先に見る。面の 2 辺は軸線と重なっているため、
            // 軸を優先すると四角形の内側でも軸を掴んでしまう
            var bestPlane = -1;
            if (tool != GizmoTool.Rotate)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    if (!IsPlaneHandleEnabled(dragSpace, axis))
                    {
                        continue;
                    }
                    if (IsInsidePlaneHandle(camera, rtPoint, origin, axes, axis, size * PlaneSizeRatio,
                        tool == GizmoTool.Scale))
                    {
                        bestPlane = axis;
                        break;
                    }
                }
            }

            // 面ハンドルを掴めなかったときだけ軸を見る
            var bestAxis = -1;
            if (bestPlane < 0)
            {
                var bestDistance = tool == GizmoTool.Rotate ? RotateHitThreshold : HitThreshold;
                for (var axis = 0; axis < 3; axis++)
                {
                    float distance;
                    var axisDir = axes[axis];
                    if (tool == GizmoTool.Rotate)
                    {
                        if (IsViewRing(dragSpace, axis))
                        {
                            distance = DistanceToFullCircle(
                                camera, rtPoint, origin, axisDir, size * ViewRingRadiusRatio);
                        }
                        else if (IsScreenDragRing(dragSpace, axis))
                        {
                            // 視線を含む面のリングは線に潰れて描かれる。面の角度は取れないが
                            // 画面上の移動量で回すので、描かれた線そのものを掴ませる
                            distance = DistanceToCircle(camera, rtPoint, origin, axisDir, size);
                        }
                        else
                        {
                            // 視線と平行に近い回転面は角度が安定しないので候補から外す。
                            // ここで弾いておけば手前に見えている別の軸を掴める
                            if (!IsRotationPlaneStable(RayDirection(camera, rtPoint), axisDir))
                            {
                                continue;
                            }
                            distance = DistanceToCircle(camera, rtPoint, origin, axisDir, size);
                        }
                    }
                    else
                    {
                        if (!IsAxisHandleEnabled(dragSpace, axis))
                        {
                            continue;
                        }
                        bool v0, v1;
                        var a = ToRtPoint(camera, origin, out v0);
                        var b = ToRtPoint(camera, origin + axisDir * size, out v1);
                        distance = (v0 && v1) ? DistanceToSegment(rtPoint, a, b) : float.MaxValue;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestAxis = axis;
                    }
                }
            }

            if (bestAxis < 0 && bestPlane < 0)
            {
                return false;
            }

            isDragging = true;
            // ドラッグ解決は掴んだカメラ基準で行う。別ビューの座標で解釈されないよう保持する
            _dragCamera = camera;
            _dragAxis = bestAxis;
            _dragPlane = bestPlane;
            // 軸・面の法線は開始時に固定する。Local では回転ドラッグで軸自体が動き、
            // Camera ではドラッグ中のカメラ移動で軸が変わるため、現在値を使うと対象が暴れる
            _dragAxisDir = bestAxis >= 0 ? axes[bestAxis] : Vector3.zero;
            _dragPlaneNormal = bestPlane >= 0 ? axes[bestPlane] : Vector3.zero;
            _dragByScreen = tool == GizmoTool.Rotate && bestAxis >= 0 && IsScreenDragRing(dragSpace, bestAxis);
            _dragStartPosition = target.position;
            _dragStartRotation = target.rotation;
            _dragStartScale = target.localScale;

            if (bestPlane >= 0)
            {
                // 視線と面がほぼ平行だと交点が取れない。基準点が定まらないまま
                // ドラッグを始めると前回の残留値を基準にして対象が飛ぶため、掴まない
                if (!PlanePointAt(camera, rtPoint, out _dragStartPlanePoint))
                {
                    EndDrag();
                    return false;
                }
            }
            else if (_dragByScreen)
            {
                _dragStartRtPoint = rtPoint;
                _dragRadiusPixels = ScreenLength(camera, origin, size);
            }
            else if (tool == GizmoTool.Rotate)
            {
                // 開始角が取れないまま掴むと、最初の更新で角度差が丸ごとズレて
                // 対象が飛ぶ。面ドラッグと同じく掴まないことで防ぐ
                if (!TryRotationAngleAt(camera, rtPoint, out _dragStartParam))
                {
                    EndDrag();
                    return false;
                }
            }
            else
            {
                _dragStartParam = AxisParamAt(camera, rtPoint);
            }
            return true;
```

- [ ] **Step 7: 面ハンドルの判定と面の交点を固定した軸で行う**

`IsInsidePlaneHandle` のシグネチャと冒頭を置き換える:

```csharp
        /// <summary>rtPoint が面ハンドルの内側か。画面へ投影した多角形で判定する</summary>
        private bool IsInsidePlaneHandle(
            Camera camera, Vector2 rtPoint, Vector3 origin, GizmoBasis axes, int normalAxis, float size,
            bool triangle)
        {
            Vector3 u, v;
            PlaneAxes(axes, normalAxis, out u, out v);
```

`PlanePointAt` を置き換える:

```csharp
        /// <summary>マウスレイと操作面 (開始時に固定した法線) の交点。面と平行で交わらなければ false</summary>
        private bool PlanePointAt(Camera camera, Vector2 rtPoint, out Vector3 point)
        {
            var plane = new Plane(_dragPlaneNormal, _dragStartPosition);
            var ray = camera.ScreenPointToRay(new Vector3(rtPoint.x, rtPoint.y, 0f));

            float enter;
            if (!plane.Raycast(ray, out enter))
            {
                point = _dragStartPlanePoint;
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }
```

`UpdatePlaneDrag` の `if (!PlanePointAt(_dragCamera, rtPoint, _dragPlane, out point))` を `if (!PlanePointAt(_dragCamera, rtPoint, out point))` にする。

Local の面ドラッグは従来も移動・拡縮中に対象の回転が変わらないため、法線を開始時に固定しても挙動は同じ。

- [ ] **Step 8: 画面ドラッグの回転を足す**

`UpdateDrag` の `case GizmoTool.Rotate:` のブロック先頭 (`float current;` の前) に追加:

```csharp
                    if (_dragByScreen)
                    {
                        var angle = CalcScreenDragAngle(_dragAxis, rtPoint - _dragStartRtPoint, _dragRadiusPixels);
                        target.rotation = Quaternion.AngleAxis(angle, _dragAxisDir) * _dragStartRotation;
                        break;
                    }
```

`EndDrag` に `_dragByScreen = false;` を足す。

- [ ] **Step 9: GizmoRenderer から 3 値を渡す**

- `GizmoRenderer.cs:368` `_gizmo.useLocalSpace = useLocalSpace;` → `_gizmo.space = gizmoSpace;`
- `GizmoRenderer.cs:431` `gizmo.useLocalSpace = true;` → `gizmo.space = GizmoSpace.Local;` (直前のコメント「回転専用・ローカル軸固定」はそのまま。カメラ座標系でも固定と読めるよう「共有 UI 設定 (座標系を含む) には追従させない」に直す)
- `GizmoRenderer.cs:490` `gizmo.useLocalSpace = useLocalSpace;` → `gizmo.space = gizmoSpace;`

`rg -n "AxisDirection|useLocalSpace" source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs` で、`AxisDirection` の残りが無く、`useLocalSpace` は互換プロパティの定義だけであることを確かめる。

- [ ] **Step 10: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全件 PASS。

- [ ] **Step 11: 実機確認 (Review Focus 1・2・5・6)**

`com3d25-devbridge:restart-verify` スキルで DLL を反映して再起動し、デイリー画面でエディタを有効化、メイドを呼んで選択する。UI は Task 4 で作るので、ここでは devbridge の `eval_csharp` でリフレクション経由で切り替える (ModItemExplorer にも同名名前空間の型があるため型名で直接書かない):

```csharp
var t = System.AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("COM3D2.SceneEditor.Plugin.GizmoRenderer"))
    .First(x => x != null);
var p = t.GetProperty("gizmoSpace");
p.SetValue(null, System.Enum.ToObject(p.PropertyType, 2), null); // 2 = Camera
p.GetValue(null, null)
```

1. 移動ツール: SceneView と GameView の両方で、矢印が画面の右 (赤) と上 (緑) を向き、中心に画面と平行な青い四角だけがある (`screenshot`)。SceneView のカメラを回すと SceneView のギズモだけ向きが変わる
2. 移動: 四角をドラッグすると画面に沿って動き、赤・緑の矢印はそれぞれ画面の左右・上下だけに動く。中心付近をクリックしても奥行きへ飛ばない
3. 回転ツール: 白い外周リングと、赤の縦線・緑の横線が出る。外周リングのドラッグで視線まわりに回る。赤の縦線を上へドラッグすると手前側が上へ転がる (うつむいていた顔が上を向く方向)。緑の横線を右へドラッグすると手前側が右へ転がる
4. 回転中に SceneView の追従 (自動フォーカス) でカメラが動いても、回転方向が途中で変わらない
4-2. 対象から大きく離れる・強い望遠にするなど、ギズモを画面上で小さくした状態で赤・緑の線を少しだけドラッグしても、回転が跳ねない (`MinScreenRadiusPixels` の下限だけでは増幅を抑えきれない場合は、下限を上げるか角度をクランプする)。回転方向 (3 の向き) はテストで確定できないので、この実機確認を省略しない
5. 拡縮ツール: ローカル軸 (メイドの向き) のギズモが出て、掴んだ軸がそのまま伸びる
6. ボーン表示を ON にして関節の回転ギズモがローカル軸のまま (カメラ軸にならない)
7. Inspector の位置・回転の数値がワールド値 (`Global` にしたときと同じ値) になる
8. `p.SetValue(null, System.Enum.ToObject(p.PropertyType, 0), null)` で `Local` に戻し、`Local` / `Global` (1) の見た目・操作が従来どおり (外周リングが出ない)

- [ ] **Step 12: コミット**

```bash
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils add TransformGizmo.cs
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils commit -m "feat(gizmo): カメラ座標系の軸・外周リング・画面ドラッグ回転を追加する"
git add source/COM3D2.SceneEditor.Plugin/MTEUtils source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): ギズモをカメラ座標系で描画・操作する"
```

---

### Task 4: 軸空間ボタンを 3 値の巡回にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolRowDrawer.cs` (`GizmoToolRowOption` :7-20、`DrawSpaceButton` :66-85、クラス summary :22-25)
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolClient.cs:74-77` (コメントのみ)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` (`CreateToolRowOption` :62-72)
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs` (巡回順のテストを足す)

**Interfaces:**
- Consumes: `GizmoSpace` (Task 1)、`GizmoRenderer.gizmoSpace` (Task 2)
- Produces:
  - `GizmoToolRowOption.getSpace : Func<GizmoSpace>` / `setSpace : Action<GizmoSpace>` / `cameraIcon : Texture2D` (いずれも任意。getSpace と setSpace が両方あるときだけ 3 値)
  - `GizmoToolRowDrawer.NextSpace(GizmoSpace) : GizmoSpace` / `GetSpaceName(GizmoSpace) : string`

- [ ] **Step 1: 失敗するテストを足す**

`GizmoSpaceTests.cs` の末尾 (クラス内) に追加:

```csharp
        [Theory]
        [InlineData(GizmoSpace.Local, GizmoSpace.Global)]
        [InlineData(GizmoSpace.Global, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoSpace.Local)]
        public void 軸空間ボタンはLocalからGlobalCameraの順に巡回する(GizmoSpace current, GizmoSpace expected)
        {
            Assert.Equal(expected, GizmoToolRowDrawer.NextSpace(current));
        }

        [Fact]
        public void 座標系の表示名()
        {
            Assert.Equal("Local", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Local));
            Assert.Equal("Global", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Global));
            Assert.Equal("Camera", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Camera));
        }
```

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter GizmoSpaceTests`
Expected: ビルドエラー (`NextSpace` / `GetSpaceName` が無い)

- [ ] **Step 2: GizmoToolRowOption に 3 値の口を足す**

`GizmoToolRowOption` の `setUseLocalSpace` の後に追加:

```csharp
        /// <summary>
        /// 3 値の座標系 (Local / Global / Camera) の取得・変更。両方設定されていれば軸空間ボタンは
        /// 3 値の巡回になり、getUseLocalSpace / setUseLocalSpace は使わない。
        /// 設定しない利用側 (ModItemExplorer 等) は従来の Local / Global トグルのまま
        /// </summary>
        public Func<GizmoSpace> getSpace;
        public Action<GizmoSpace> setSpace;
```

`globalIcon` の後に追加:

```csharp
        /// <summary>Camera のときのアイコン。null なら globalIcon を点灯表示する</summary>
        public Texture2D cameraIcon;
```

- [ ] **Step 3: DrawSpaceButton を 3 値に対応させる**

`DrawSpaceButton` の先頭 (`var useLocalSpace = option.getUseLocalSpace();` の前) に追加:

```csharp
            if (option.getSpace != null && option.setSpace != null)
            {
                DrawSpaceCycleButton(view, option, height);
                return;
            }
```

`DrawSpaceButton` の summary を「軸空間の切替ボタン 1 個。3 値の口 (getSpace / setSpace) があれば Local → Global → Camera の巡回、無ければ Local / Global のトグル。…」に直す。`DrawSpaceButton` の後に追加:

```csharp
        /// <summary>
        /// 押すたびに Local → Global → Camera → Local と巡回させる。
        /// Local 以外を点灯し、Camera はアイコンを替えて Global と見分ける
        /// </summary>
        private static void DrawSpaceCycleButton(GUIView view, GizmoToolRowOption option, float height)
        {
            var space = option.getSpace();
            var next = NextSpace(space);

            if (option.globalIcon != null)
            {
                var icon = space == GizmoSpace.Camera && option.cameraIcon != null
                    ? option.cameraIcon
                    : option.globalIcon;
                // トグルはクリックのたびに値が反転して onChanged が呼ばれる。
                // 反転後の値は使わず、次の座標系へ進める
                view.DrawToggle(icon, space != GizmoSpace.Local, height, height,
                    _ => option.setSpace(next), SpaceIconOffset, "座標系: " + GetSpaceName(space));
            }
            else if (view.DrawButton(GetSpaceName(space), SpaceButtonWidth, height))
            {
                option.setSpace(next);
            }
        }

        /// <summary>軸空間ボタンを押したときの次の座標系</summary>
        public static GizmoSpace NextSpace(GizmoSpace space)
        {
            switch (space)
            {
                case GizmoSpace.Local: return GizmoSpace.Global;
                case GizmoSpace.Global: return GizmoSpace.Camera;
                default: return GizmoSpace.Local;
            }
        }

        /// <summary>ボタンとツールチップに出す座標系の名前</summary>
        public static string GetSpaceName(GizmoSpace space)
        {
            switch (space)
            {
                case GizmoSpace.Local: return "Local";
                case GizmoSpace.Global: return "Global";
                default: return "Camera";
            }
        }
```

クラス summary (:22-25) の「軸空間 (Local/Global)」を「軸空間 (Local/Global、3 値の口があれば Camera も)」に直す。

- [ ] **Step 4: GizmoToolClient のコメントを直す**

`GizmoToolClient.cs:74-77` の `useLocalSpace` の summary を置き換える (コードは変えない):

```csharp
        /// <summary>
        /// SceneEditor 側のギズモ軸空間 (true = Local)。取得失敗時は SceneEditor の既定と同じ true。
        /// SceneEditor 側が Camera 座標系のときは false (Global と同じ扱い) を返し、
        /// false を書き込んでも Camera は保たれる。3 値の座標系は同期しない。
        /// 失敗時の扱いは tool と同じ (isAvailable で判別する)
        /// </summary>
```

- [ ] **Step 5: SceneEditor のボタン設定に 3 値を渡す**

`GizmoRenderer.CreateToolRowOption` の初期化子に追加 (`getUseLocalSpace` / `setUseLocalSpace` は残す):

```csharp
                getSpace = () => gizmoSpace,
                setSpace = value => gizmoSpace = value,
                cameraIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Camera),
```

Inspector のギズモ行 (`InspectorWindow.DrawGizmoHeader`) と SceneView ツールバー (`SceneViewWindow.cs:325`) はどちらも `CreateToolRowOption` を使うので変更不要。ボタン幅 (`GetSpaceButtonWidth`) もアイコンがあれば正方形のまま。

- [ ] **Step 6: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全件 PASS。

- [ ] **Step 7: 実機確認 (Review Focus 3・4)**

`com3d25-devbridge:restart-verify` で反映・再起動し、デイリー画面でエディタを有効化する。

1. Inspector のギズモ行の地球儀ボタンを押すたびに、消灯の地球儀 (Local) → 点灯の地球儀 (Global) → 点灯のカメラ (Camera) → 消灯の地球儀と変わり、ツールチップが `座標系: Local` / `Global` / `Camera` になる。SceneView ツールバーのボタンも同じ状態を示し、どちらから押しても連動する (`screenshot`)
2. `Camera` にしてゲームを再起動すると `Camera` で開く。`Config/SceneEditor.xml` (パスは `PluginUtils.ConfigPath`) に `<gizmoUseLocalSpace>false</gizmoUseLocalSpace>` と `<gizmoSpace>Camera</gizmoSpace>` が書かれている
3. ModItemExplorer が導入されていれば、SceneEditor 側を `Camera` にしても数秒待って `Camera` のまま (ModItemExplorer の同期で `Global` に戻らない)。ModItemExplorer のモデル操作ウィンドウの軸空間は `Global` 表示。ModItemExplorer 側で `Local` に切り替えると SceneEditor も `Local` になる。未導入ならこの項目は省略し、その旨を記録する

- [ ] **Step 8: コミット**

```bash
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils add GizmoToolRowDrawer.cs GizmoToolClient.cs
git -C source/COM3D2.SceneEditor.Plugin/MTEUtils commit -m "feat(gizmo): 軸空間ボタンを Local/Global/Camera の 3 値で巡回できるようにする"
git add source/COM3D2.SceneEditor.Plugin/MTEUtils source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs source/COM3D2.SceneEditor.Plugin.Tests/GizmoSpaceTests.cs
git commit -m "feat(gizmo): 地球儀ボタンでカメラ座標系へ切り替えられるようにする"
```

---

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/guide/scene-view.md:57,73,87`
- Modify: `docs-site/guide/configuration.md:100`
- Modify: `docs-site/guide/maid-editing.md:341`
- Modify: `docs-site/dev/misc-guest-guide.md:38-48`

変更履歴 (`CHANGELOG.md`) はリリース準備 (`release-prep` スキル) で書くので、ここでは触らない。

- [ ] **Step 1: SceneView ガイドの軸空間の説明を直す**

`scene-view.md:57` の表の行を「操作種別（なし / 移動 / 回転 / 拡縮）と軸空間（`Local` / `Global` / `Camera`）の切替」にする。

`:73` の段落を次に置き換える:

```markdown
軸空間は地球儀アイコンのボタンで切り替えます。押すたびに `Local`（消灯）→ `Global`（点灯）→ `Camera`（カメラアイコンで点灯）の順に変わります。SceneView のツールバー左上にも同じボタンがあります。

`Camera` では、軸がそのギズモを見ているカメラの右・上・前になります（SceneView と GameView でそれぞれのカメラに合わせます）。

| ツール | `Camera` での操作 |
|---|---|
| 移動 | 赤（画面の左右）・緑（画面の上下）の矢印と、画面に平行な四角で動かします。奥行き方向は `Local` / `Global` で動かします |
| 回転 | 白い外周リングで視線まわりに回します。赤の縦線・緑の横線は、線に沿ってドラッグするとその向きに転がるように回ります |
| 拡縮 | `Local` と同じです |

ボーン表示中の関節の回転ギズモは、軸空間に関わらず常に `Local` です。Inspector の位置・回転の数値は、`Camera` のときワールド座標で表示します。
ModItemExplorer と連動しているときは、`Camera` は ModItemExplorer 側では `Global` として扱われます。
```

`:87` の表の行を `| 軸空間（`Local` / `Global` / `Camera`） | 前回の状態で開く |` にする。

- [ ] **Step 2: 設定項目の表を直す**

`configuration.md:100` を次の 2 行に置き換える:

```markdown
| `gizmoSpace` | `Local` | ギズモの軸空間（`Local` / `Global` / `Camera`） |
| `gizmoUseLocalSpace` | `true` | 旧版互換用。`gizmoSpace` が `Local` のとき `true`。旧版で書き換えられていたら、読み込み時に `gizmoSpace` へ反映します |
```

- [ ] **Step 3: ボーン編集の座標系の記述を直す**

`maid-editing.md:341` を「表示・編集する座標系はギズモの軸空間に追従します（`Local` はローカル、`Global` と `Camera` はワールド）。」にする。

- [ ] **Step 4: 開発者向けガイドの GizmoToolClient を直す**

`misc-guest-guide.md:40` 付近の説明の後に 1 段落足す:

```markdown
SceneEditor 側の軸空間は `Local` / `Global` / `Camera` の 3 値ですが、`GizmoToolClient.useLocalSpace` は
従来どおり bool です。SceneEditor が `Camera` のときは `false`（`Global` と同じ扱い）を返し、
`false` を書き込んでも `Camera` は保たれます。`true` を書き込むと `Local` になります。
```

- [ ] **Step 5: コミット**

```bash
git add docs-site
git commit -m "docs(gizmo): ギズモのカメラ座標系の説明を追加する"
```

## レビュー却下メモ

- Camera 座標系のアイコンに `Kind.Camera` (タイムラインの「カメラ同期」と同じ絵) を使うと紛らわしい — 見送り。表示される場所 (Inspector のギズモ行・SceneView ツールバー) とタイムラインのカメラ同期は別ウィンドウで、ツールチップも違う。Task 4 の実機確認で紛らわしければ新しいアイコンを作る
