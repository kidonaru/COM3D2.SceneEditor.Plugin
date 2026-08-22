# メイドギズモの表示対象対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> （本ワークスペースでは subagent-driven-development は使わない）

**Goal:** SceneEditor でメイドルートの移動/回転/拡縮ギズモを全メイド分に表示できるようにし、その「表示対象（すべて表示 / 選択中）」を ModItemExplorer と双方向に連動させる。

**Architecture:** SceneEditor の `GizmoRenderer` は現在 `TransformGizmo` を 1 個だけ持ち、選択中オブジェクトにしか描いていない。ここへ非選択メイド用のギズモプールを足す。表示対象の設定は既存の `currentTool` / `useLocalSpace` と同じく `GizmoRenderer` の static プロパティに置き、ModItemExplorer（以下 MIE）が `GizmoToolClient` のリフレクションブリッジ経由でポーリング同期する。

**Tech Stack:** C# (.NET 3.5 相当 / Unity 5.x)、MSBuild、Harmony なし。テストフレームワークは無く、検証はビルド成功 + 実機（MCP `com3d25-devbridge`）で行う。

**Spec:** `docs/superpowers/specs/2026-08-22-maid-gizmo-target-type-design.md`

## Global Constraints

- **git worktree を使わない**。作業は常にメインの作業ディレクトリで行う
- **`deploy.bat` / `deploy.ps1` は実行しない**
- **`debug.bat` はゲーム停止中に実行するとゲームフォルダへ DLL が反映される**。本計画では MSBuild を直接叩く
- コードのコメントとエラーログメッセージは**日本語**で書く
- 2 リポジトリにまたがる。パスはそれぞれ以下を基準にする
  - SceneEditor: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`
  - ModItemExplorer: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`
- **`MTEUtils/` 配下は両リポジトリで同一ソースを複製している**。片方を編集したら必ずもう片方へコピーし、`diff` で一致を確認する
- **csproj はファイルを明示列挙している**（`<Compile Include="..." />`）。ファイルの追加・削除時は該当 csproj も必ず更新する
- ビルドは **COM3D2 版と COM3D2.5 版の両方**が通ること。コードはどちらのゲームバージョンでもコンパイルできる範囲に収める
- コミットは Conventional Commits 形式・日本語（`type(scope): 要約`）

### ビルドコマンド

`.env`（各リポジトリ直下）の `COM3D2_DIR` / `COM3D25_DIR` を使う。値はワークスペース標準では `W:\COM3D2` / `W:\COM3D2_5`。実行前に `.env` の実値を確認すること。

SceneEditor:

```bash
MSBUILD="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSBUILD" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
"$MSBUILD" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

ModItemExplorer:

```bash
MSBUILD="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
"$MSBUILD" COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
"$MSBUILD" COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

期待: どちらも `0 Error(s)`。

### CHANGELOG について

このリポジトリの `CHANGELOG.md` はリリース時にバージョン単位でまとめて書く運用（`bump-version.bat` と同じタイミング）。本計画では触らない。

---

## ファイル構成

| リポジトリ | ファイル | 責務 |
|---|---|---|
| SceneEditor | `MTEUtils/TransformGizmo.cs` | `GizmoTargetType` enum の定義（`GizmoTool` と同居） |
| SceneEditor | `MTEUtils/GizmoTargetRowDrawer.cs`（新規） | 表示対象の切替行。状態を持たずデリゲート注入 |
| SceneEditor | `MTEUtils/GizmoToolClient.cs` | ホストの静的プロパティへのリフレクションブリッジ |
| SceneEditor | `Config.cs` | `gizmoTargetType` / `gizmoUseLocalSpace` の永続化 |
| SceneEditor | `Manager/GizmoRenderer.cs` | static 設定の保持、メイドギズモのプール・描画・掴み |
| SceneEditor | `InspectorWindow.cs` | 表示対象行の配置 |
| MIE | `MTEUtils/*`（3 ファイル） | SceneEditor と同一ソースの複製 |
| MIE | `ModelPlacement/GizmoTargetRowDrawer.cs` | 削除（共有版へ統合） |
| MIE | `ModelOperationWindow.cs` | 共有版の呼び出しへ差し替え |
| MIE | `ModelPlacement/ModelInspectorDrawer.cs` | 委譲描画から重複行を削除 |
| MIE | `ModelPlacement/SelfModelPlacer.cs` | 表示対象のホスト同期 |

---

### Task 1: 表示対象行ドローワの共通化

MIE の `GizmoTargetRowDrawer` は `SelfModelPlacer` を直接参照していて共有できない。隣の `GizmoToolRowDrawer` と同じデリゲート注入の形へ直し、`MTEUtils/` の共有ソースへ移す。この時点では SceneEditor 側に呼び出し元は無く、MIE の見た目・挙動は変わらない（純粋なリファクタ）。

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs:5-13`
- Create: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoTargetRowDrawer.cs`
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Copy to MIE: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils/TransformGizmo.cs`, `.../MTEUtils/GizmoTargetRowDrawer.cs`
- Delete: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/GizmoTargetRowDrawer.cs`
- Modify: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs:517-520`
- Modify: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs:73`
- Modify: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj`

**Interfaces:**
- Produces: `COM3D2.MotionTimelineEditor.GizmoTargetType { All, Selected }`、`COM3D2.MotionTimelineEditor.GizmoTargetRowOption`（`labelWidth` / `height` / `labelStyle` / `getTargetType` / `setTargetType`）、`GizmoTargetRowDrawer.Draw(GUIView view, GizmoTargetRowOption option)`、`GizmoTargetRowDrawer.ButtonWidth = 80f`

- [ ] **Step 1: `GizmoTargetType` を `TransformGizmo.cs` へ追加**

`GizmoTool` enum の直後（`public class TransformGizmo` の前）に挿入する。

```csharp
    /// <summary>ギズモを表示する対象</summary>
    public enum GizmoTargetType
    {
        All,
        Selected,
    }
```

- [ ] **Step 2: 共有ドローワを新規作成**

`COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoTargetRowDrawer.cs`:

```csharp
using System;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>ギズモ表示対象行の設定。状態は持たず、取得・変更はデリゲートで注入する</summary>
    public struct GizmoTargetRowOption
    {
        public float labelWidth;
        /// <summary>行の高さ。0 なら 20</summary>
        public float height;
        /// <summary>ラベルのスタイル。null なら既定</summary>
        public GUIStyle labelStyle;
        public Func<GizmoTargetType> getTargetType;
        public Action<GizmoTargetType> setTargetType;
    }

    /// <summary>
    /// ギズモの表示対象 (すべて/選択中) の切替行。
    /// SceneEditor の Inspector と ModItemExplorer のモデル操作ウィンドウで共通に使う
    /// </summary>
    public static class GizmoTargetRowDrawer
    {
        private static readonly GizmoTargetType[] Types =
            { GizmoTargetType.All, GizmoTargetType.Selected };

        private static readonly string[] Names = { "すべて表示", "選択中" };

        /// <summary>表示対象を選ぶボタンの幅。最長の「すべて表示」が収まる幅にする</summary>
        public static readonly float ButtonWidth = 80f;

        public static void Draw(GUIView view, GizmoTargetRowOption option)
        {
            var height = option.height > 0f ? option.height : 20f;

            view.BeginHorizontal();
            {
                view.DrawLabel("表示対象", option.labelWidth, height, style: option.labelStyle);

                var current = option.getTargetType();
                for (var i = 0; i < Types.Length; i++)
                {
                    var targetType = Types[i];
                    view.DrawToggle(Names[i], current == targetType, ButtonWidth, height,
                        // 選択中の項目を再度押しても解除しない (ギズモ行と同じ規約)
                        on => { if (on) option.setTargetType(targetType); });
                }
            }
            view.EndLayout();
        }
    }
}
```

- [ ] **Step 3: SceneEditor の csproj へ登録**

`MTEUtils\GizmoToolRowDrawer.cs` の `<Compile Include>` 行のすぐ隣（アルファベット順の位置）に追加する。

```xml
    <Compile Include="MTEUtils\GizmoTargetRowDrawer.cs" />
```

- [ ] **Step 4: SceneEditor をビルド**

「ビルドコマンド」節の SceneEditor 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 5: 共有ソースを MIE へコピー**

```bash
SE=W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MIE=W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
cp "$SE/MTEUtils/TransformGizmo.cs" "$MIE/MTEUtils/TransformGizmo.cs"
cp "$SE/MTEUtils/GizmoTargetRowDrawer.cs" "$MIE/MTEUtils/GizmoTargetRowDrawer.cs"
diff "$SE/MTEUtils/TransformGizmo.cs" "$MIE/MTEUtils/TransformGizmo.cs" && echo IDENTICAL
diff "$SE/MTEUtils/GizmoTargetRowDrawer.cs" "$MIE/MTEUtils/GizmoTargetRowDrawer.cs" && echo IDENTICAL
```

- [ ] **Step 6: MIE の旧ドローワを削除**

`COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/GizmoTargetRowDrawer.cs` を削除する。MIE の各ファイルは `using COM3D2.MotionTimelineEditor;` を持つため、同名クラスが 2 つあると曖昧参照でコンパイルエラーになる。

```bash
git -C W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin rm source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/GizmoTargetRowDrawer.cs
```

- [ ] **Step 7: MIE の csproj を更新**

`ModelPlacement\GizmoTargetRowDrawer.cs` の `<Compile Include>` 行を削除し、代わりに追加する。

```xml
    <Compile Include="MTEUtils\GizmoTargetRowDrawer.cs" />
```

- [ ] **Step 8: `ModelOperationWindow` の呼び出しを差し替え**

`DrawGizmoTargetRow` の中身を置き換える。

変更前:

```csharp
        private void DrawGizmoTargetRow(GUIView view)
        {
            GizmoTargetRowDrawer.Draw(view, LABEL_WIDTH, ROW_HEIGHT, GUIView.gsLabelRight);
        }
```

変更後:

```csharp
        private void DrawGizmoTargetRow(GUIView view)
        {
            var placer = SelfModelPlacer.instance;

            GizmoTargetRowDrawer.Draw(view, new GizmoTargetRowOption
            {
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                labelStyle = GUIView.gsLabelRight,
                getTargetType = () => placer.gizmoTargetType,
                setTargetType = value => placer.gizmoTargetType = value,
            });
        }
```

- [ ] **Step 9: `SelfModelPlacer.gizmoTargetType` の型を共有 enum へ変更**

`SelfModelPlacer` 内の入れ子 `public enum GizmoTargetType { All, Selected }`（`SelfModelPlacer.cs:176-180`）を削除し、`gizmoTargetType` プロパティと `Config.gizmoTargetType` フィールドの型を `COM3D2.MotionTimelineEditor.GizmoTargetType` へ差し替える。

`SelfModelPlacer.cs`:

```csharp
        /// <summary>
        /// ギズモを表示する対象。多数配置するとギズモが重なって選びづらくなるため、
        /// 選択中のモデルだけに絞れるようにしている。設定は Config に永続化する
        /// </summary>
        public GizmoTargetType gizmoTargetType
```

`ApplyGizmoTarget` の比較も型名だけ変わる:

```csharp
            var selectedOnly = gizmoTargetType == GizmoTargetType.Selected;
```

`Config.cs`（MIE、50 行目付近）:

```csharp
        public GizmoTargetType gizmoTargetType = GizmoTargetType.All;
```

`Config.cs` の先頭に `using COM3D2.MotionTimelineEditor;` が無ければ追加する。

> 補足: MIE の Config は XML シリアライズされる。enum の名前（`All` / `Selected`）は変わらないため、既存の設定ファイルはそのまま読める。

- [ ] **Step 10: `ModelInspectorDrawer` の呼び出しを差し替え**

`ModelInspectorDrawer.cs:73` の 1 行を置き換える。

変更前:

```csharp
            GizmoTargetRowDrawer.Draw(_view, LabelWidth, RowHeight);
```

変更後:

```csharp
            GizmoTargetRowDrawer.Draw(_view, new GizmoTargetRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTargetType = () => placer.gizmoTargetType,
                setTargetType = value => placer.gizmoTargetType = value,
            });
```

> `placer` は同ファイル内に既にあるアクセサ。無ければ `SelfModelPlacer.instance` をローカル変数に取る。

- [ ] **Step 11: MIE をビルド**

「ビルドコマンド」節の MIE 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 12: コミット（2 リポジトリ）**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs \
        source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoTargetRowDrawer.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(gizmo): ギズモ表示対象の切替行を共有ドローワへ切り出す"

cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add -u
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils/TransformGizmo.cs \
        source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoTargetRowDrawer.cs
git commit -m "refactor(gizmo): 表示対象の切替行を共有ドローワへ移す"
```

---

### Task 2: SceneEditor の設定を Config へ永続化

`GizmoRenderer` の static プロパティのバッキングを Config のフィールドにして、表示対象と軸空間を再起動後も保持する。

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Config.cs:54-61`
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:14-22`

**Interfaces:**
- Consumes: `GizmoTargetType`（Task 1）
- Produces: `GizmoRenderer.gizmoTargetType`（static、`GizmoTargetType`、既定 `All`）、`GizmoRenderer.useLocalSpace`（static、`bool`、既定 `true`）、`Config.gizmoTargetType`、`Config.gizmoUseLocalSpace`

- [ ] **Step 1: Config へフィールドを追加**

`Config.cs` の SceneView トグル群（`sceneViewAutoFocus` の直後）に追加する。ファイル先頭に `using COM3D2.MotionTimelineEditor;` が無ければ追加する。

```csharp
        // ギズモ設定。SceneView / GameView 双方のギズモが共有する。
        // 操作種別 (currentTool) はホットキーで頻繁に切り替える一時的なモードなので永続化しない
        public GizmoTargetType gizmoTargetType = GizmoTargetType.All;
        public bool gizmoUseLocalSpace = true;
```

- [ ] **Step 2: `GizmoRenderer` の static プロパティを Config バッキングへ変更**

`Manager/GizmoRenderer.cs` の以下を置き換える。

変更前:

```csharp
        /// <summary>
        /// 操作種別と軸空間。SceneView と GameView のギズモで共有するため static で持つ。
        /// 切り替え UI は Inspector にある
        /// </summary>
        public static GizmoTool currentTool { get; set; } = GizmoTool.Move;
        public static bool useLocalSpace { get; set; } = true;
```

変更後:

```csharp
        /// <summary>
        /// 操作種別・軸空間・表示対象。SceneView と GameView のギズモで共有するため static で持つ。
        /// 切り替え UI は Inspector にある。
        /// 軸空間と表示対象は一度決めたら変えない設定なので Config へ永続化する
        /// (バッキングを Config のフィールドにして、GizmoRenderer の生成順に依存させない)。
        /// 操作種別はホットキーで頻繁に切り替える一時的なモードなので永続化しない
        /// </summary>
        public static GizmoTool currentTool { get; set; } = GizmoTool.Move;

        public static bool useLocalSpace
        {
            get => config.gizmoUseLocalSpace;
            set
            {
                if (config.gizmoUseLocalSpace == value)
                {
                    return;
                }
                config.gizmoUseLocalSpace = value;
                config.dirty = true;
            }
        }

        /// <summary>
        /// ギズモを表示する対象。メイドを多数呼ぶとギズモが重なって選びづらくなるため、
        /// 選択中だけに絞れるようにしている。ModItemExplorer とは GizmoToolClient 経由で連動する
        /// </summary>
        public static GizmoTargetType gizmoTargetType
        {
            get => config.gizmoTargetType;
            set
            {
                if (config.gizmoTargetType == value)
                {
                    return;
                }
                config.gizmoTargetType = value;
                config.dirty = true;
            }
        }
```

- [ ] **Step 3: `config` アクセサを追加**

`GizmoRenderer` に無ければ、`selectionManager` アクセサ（`Manager/GizmoRenderer.cs:58` 付近）の隣に追加する。

```csharp
        private static Config config => ConfigManager.instance.config;
```

- [ ] **Step 4: SceneEditor をビルド**

「ビルドコマンド」節の SceneEditor 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Config.cs \
        source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): ギズモの軸空間と表示対象を設定へ永続化する"
```

---

### Task 3: Inspector に表示対象行を追加

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:596-608`

**Interfaces:**
- Consumes: `GizmoTargetRowDrawer.Draw` / `GizmoTargetRowOption`（Task 1）、`GizmoRenderer.gizmoTargetType`（Task 2）

- [ ] **Step 1: `DrawGizmoToolRow` に表示対象行を足す**

`DrawGizmoToolRow` は 2 箇所（`InspectorWindow.cs:173` のオブジェクト用と `:420` のメイド用）から呼ばれる。中に含めることで両方へ出る。

変更前:

```csharp
        /// <summary>
        /// ギズモの操作種別と軸空間の切り替え。
        /// SceneView / GameView 双方のギズモがこの設定を共有する
        /// </summary>
        private void DrawGizmoToolRow()
        {
            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => GizmoRenderer.currentTool,
                setTool = tool => GizmoRenderer.currentTool = tool,
                getUseLocalSpace = () => GizmoRenderer.useLocalSpace,
                setUseLocalSpace = value => GizmoRenderer.useLocalSpace = value,
            });
        }
```

変更後:

```csharp
        /// <summary>
        /// ギズモの操作種別・軸空間・表示対象の切り替え。
        /// SceneView / GameView 双方のギズモがこの設定を共有する
        /// </summary>
        private void DrawGizmoToolRow()
        {
            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => GizmoRenderer.currentTool,
                setTool = tool => GizmoRenderer.currentTool = tool,
                getUseLocalSpace = () => GizmoRenderer.useLocalSpace,
                setUseLocalSpace = value => GizmoRenderer.useLocalSpace = value,
            });

            GizmoTargetRowDrawer.Draw(_view, new GizmoTargetRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTargetType = () => GizmoRenderer.gizmoTargetType,
                setTargetType = value => GizmoRenderer.gizmoTargetType = value,
            });
        }
```

- [ ] **Step 2: SceneEditor をビルド**

「ビルドコマンド」節の SceneEditor 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 3: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs
git commit -m "feat(inspector): ギズモの表示対象切替行を追加する"
```

> この時点では MIE の Inspector 委譲描画にも表示対象行が残っているため、MIE のモデルを選ぶと行が 2 つ並ぶ。Task 7 で MIE 側を削除して解消する。

---

### Task 4: 非選択メイドのギズモを描画する

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs`（フィールド追加、`SyncGizmo`、`OnPostRender`）

**Interfaces:**
- Consumes: `GizmoRenderer.gizmoTargetType`（Task 2）
- Produces: `GizmoRenderer._maidGizmos` / `_maidGizmoTargets`（private）、`RebuildMaidGizmos()`（private）

- [ ] **Step 1: フィールドを追加**

`private readonly TransformGizmo _gizmo = new TransformGizmo();` の直後に置く。

```csharp
        /// <summary>
        /// 非選択メイド用のギズモ。表示対象が「すべて表示」のときだけ使う。
        /// 対象が増減しても List を作り直さず、必要な数だけ確保して使い回す
        /// </summary>
        private readonly List<TransformGizmo> _maidGizmos = new List<TransformGizmo>();

        /// <summary>_maidGizmos と同じ添字で対象メイドのルートを保持する</summary>
        private readonly List<GameObject> _maidGizmoTargets = new List<GameObject>();

        /// <summary>現在有効な要素数。_maidGizmos は縮めずに使い回すため件数を別に持つ</summary>
        private int _maidGizmoCount;

        /// <summary>ドラッグ中のギズモ。_gizmo か _maidGizmos のいずれか</summary>
        private TransformGizmo _activeDragGizmo;
```

ファイル先頭に `using System.Collections.Generic;` が無ければ追加する。

- [ ] **Step 2: `RebuildMaidGizmos` を実装**

`SyncGizmo` の直後に追加する。

```csharp
        /// <summary>
        /// 非選択メイド用ギズモの対象を組み直す。
        /// ボーン編集中 (externalTargetProvider) とポーズボーン選択中は、ボーン用ギズモと
        /// 根本のギズモが重なって掴み間違いが起きるため 1 件も出さない。
        /// IK ドラッグ点の選択中は抑止しない (選択中メイドのルートギズモも出たままのため、
        /// SelectionManager.gizmoSuppressed の意味にそのまま揃える)
        /// </summary>
        private void RebuildMaidGizmos()
        {
            _maidGizmoCount = 0;

            if (gizmoTargetType != GizmoTargetType.All ||
                externalTargetProvider?.Invoke() != null ||
                selectionManager.gizmoSuppressed)
            {
                return;
            }

            var selected = gizmoTarget;
            var maidManager = MaidManipulateManager.instance;

            foreach (var maid in maidManager.calledMaids)
            {
                // 退避中のメイドは画面外に居るのでギズモも出さない
                if (maid == null || !maidManager.IsVisible(maid))
                {
                    continue;
                }

                var go = maid.gameObject;
                if (go == null || go == selected)
                {
                    // 選択中のメイドは _gizmo が担当する (二重描画しない)
                    continue;
                }

                AddMaidGizmo(go);
            }
        }

        /// <summary>ギズモを 1 件ぶん確保して対象と表示設定を反映する</summary>
        private void AddMaidGizmo(GameObject go)
        {
            if (_maidGizmoCount >= _maidGizmos.Count)
            {
                _maidGizmos.Add(new TransformGizmo());
                _maidGizmoTargets.Add(null);
            }

            var gizmo = _maidGizmos[_maidGizmoCount];
            gizmo.target = go.transform;
            gizmo.tool = currentTool;
            gizmo.useLocalSpace = useLocalSpace;

            _maidGizmoTargets[_maidGizmoCount] = go;
            _maidGizmoCount++;
        }
```

- [ ] **Step 3: `SyncGizmo` から呼ぶ**

`SyncGizmo` の末尾へ 1 行足す。ドラッグ中は構成を変えない（掴んだインスタンスが別の対象へ使い回されるのを防ぐ）。

変更前:

```csharp
        /// <summary>static な UI 設定と選択対象をギズモ本体へ反映する</summary>
        private void SyncGizmo()
        {
            var go = gizmoTarget;
            _gizmo.target = go != null ? go.transform : null;
            _gizmo.tool = currentTool;
            _gizmo.useLocalSpace = useLocalSpace;
        }
```

変更後:

```csharp
        /// <summary>static な UI 設定と選択対象をギズモ本体へ反映する</summary>
        private void SyncGizmo()
        {
            // 非選択メイドのギズモを掴んでいる間は選択がそのメイドへ移っているため、
            // そのまま同期すると _gizmo と掴んでいるインスタンスが同じ Transform を指して
            // 二重に描かれる。掴んでいる側だけに任せる (軸のハイライトも掴んだ側に出る)
            var isMaidGizmoDragging = _activeDragGizmo != null && _activeDragGizmo != _gizmo;

            var go = isMaidGizmoDragging ? null : gizmoTarget;
            _gizmo.target = go != null ? go.transform : null;
            _gizmo.tool = currentTool;
            _gizmo.useLocalSpace = useLocalSpace;

            // ドラッグ中に組み直すと、掴んでいるインスタンスが別のメイドへ
            // 使い回されて操作対象がすり替わる
            if (_activeDragGizmo == null)
            {
                RebuildMaidGizmos();
            }
        }
```

- [ ] **Step 4: `OnPostRender` で描画する**

`SyncGizmo(); _gizmo.Draw(_camera);` の 2 行を置き換える。

変更前:

```csharp
            // ギズモ本体は自前でマトリクスとマテリアルを設定するため、固有描画の外で呼ぶ
            SyncGizmo();
            _gizmo.Draw(_camera);
```

変更後:

```csharp
            // ギズモ本体は自前でマトリクスとマテリアルを設定するため、固有描画の外で呼ぶ
            SyncGizmo();
            _gizmo.Draw(_camera);

            for (var i = 0; i < _maidGizmoCount; i++)
            {
                _maidGizmos[i].Draw(_camera);
            }
```

- [ ] **Step 5: SceneEditor をビルド**

「ビルドコマンド」節の SceneEditor 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 6: 実機で描画を確認**

ゲームが起動していること。MCP `com3d25-devbridge` の `ping` で確認してから、SceneEditor を有効にしてメイドを 2 体呼び出す。

確認項目:
1. 表示対象「すべて表示」で、選択していないメイドにもギズモが出る
2. 表示対象「選択中」に切り替えると、選択中メイドのギズモだけになる
3. ボーン編集ウィンドウを開いている間はメイドルートのギズモが出ない
4. 退避（非表示）中のメイドにはギズモが出ない
5. IK ドラッグ点を選択している間は、選択中メイドと同じく非選択メイドのギズモも出たままになる
6. SceneView / GameView の両方で 1〜5 が成立する。特に GameView は「すべて表示」で
   ギズモが乱立してゲーム画面が見づらくならないか、見た目の許容度を確認する

`screenshot` / `capture` で見た目を確認してよい。

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): 非選択メイドのギズモを表示対象に応じて描画する"
```

---

### Task 5: メイドギズモの掴み・ドラッグ・選択移動

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:57`（`isDragging`）、`:455-490`（`TryBeginDrag` / `UpdateDrag` / `EndDrag`）

**Interfaces:**
- Consumes: `_maidGizmos` / `_maidGizmoTargets` / `_maidGizmoCount` / `_activeDragGizmo`（Task 4）
- Produces: 既存の公開シグネチャは変えない（`bool TryBeginDrag(Vector2)` / `void UpdateDrag(Vector2)` / `void EndDrag()` / `bool isDragging`）

- [ ] **Step 1: `isDragging` をドラッグ主基準に変える**

変更前:

```csharp
        public bool isDragging => _gizmo.isDragging;
```

変更後:

```csharp
        public bool isDragging => _activeDragGizmo != null && _activeDragGizmo.isDragging;
```

- [ ] **Step 2: `TryBeginDrag` を書き換える**

変更前:

```csharp
        /// <summary>rtPoint がいずれかのギズモ要素上ならドラッグを開始して true</summary>
        public bool TryBeginDrag(Vector2 rtPoint)
        {
            // 非表示のギズモは掴めない (呼び出し側は通常のオブジェクト選択へフォールバックする)
            if (!drawEnabled)
            {
                return false;
            }

            SyncGizmo();
            var began = _gizmo.TryBeginDrag(_camera, rtPoint);

            // ボーン編集 (externalTargetProvider) 中は BoneEditManager 側が
            // Pose スコープで記録するため、通常のオブジェクト操作だけ記録する
            if (began && externalTargetProvider?.Invoke() == null)
            {
                var go = gizmoTarget;
                if (go != null)
                {
                    HistoryManager.instance.BeforeEdit(
                        go.GetComponent<Maid>(), HistoryScope.Object,
                        "ギズモ操作: " + go.name, new[] { go.transform });
                }
            }
            return began;
        }
```

変更後:

```csharp
        /// <summary>rtPoint がいずれかのギズモ要素上ならドラッグを開始して true</summary>
        public bool TryBeginDrag(Vector2 rtPoint)
        {
            // 非表示のギズモは掴めない (呼び出し側は通常のオブジェクト選択へフォールバックする)
            if (!drawEnabled)
            {
                return false;
            }

            SyncGizmo();

            // 選択中のギズモを先に試す。ギズモが重なっている場合は選択中を優先する
            if (_gizmo.TryBeginDrag(_camera, rtPoint))
            {
                _activeDragGizmo = _gizmo;
                RecordGizmoDragHistory(gizmoTarget);
                return true;
            }

            for (var i = 0; i < _maidGizmoCount; i++)
            {
                if (!_maidGizmos[i].TryBeginDrag(_camera, rtPoint))
                {
                    continue;
                }

                _activeDragGizmo = _maidGizmos[i];

                var go = _maidGizmoTargets[i];
                RecordGizmoDragHistory(go);

                // 掴んだメイドを選択へ移す。カメラは寄せない (掴んだ位置から視点が飛ぶため)。
                // 次フレーム以降このメイドは _gizmo が担当するが、_activeDragGizmo が
                // インスタンスを直接掴んでいるのでドラッグはそのまま続く
                if (go != null)
                {
                    selectionManager.Select(go, true, false);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// ギズモ操作を Undo 履歴へ記録する。
        /// ボーン編集 (externalTargetProvider) 中は BoneEditManager 側が Pose スコープで
        /// 記録するため、通常のオブジェクト操作だけ記録する
        /// </summary>
        private void RecordGizmoDragHistory(GameObject go)
        {
            if (go == null || externalTargetProvider?.Invoke() != null)
            {
                return;
            }

            HistoryManager.instance.BeforeEdit(
                go.GetComponent<Maid>(), HistoryScope.Object,
                "ギズモ操作: " + go.name, new[] { go.transform });
        }
```

- [ ] **Step 3: `UpdateDrag` / `EndDrag` をドラッグ主へ流す**

変更前:

```csharp
        public void UpdateDrag(Vector2 rtPoint)
        {
            _gizmo.UpdateDrag(rtPoint);
        }

        public void EndDrag()
        {
            _gizmo.EndDrag();
        }
```

変更後:

```csharp
        public void UpdateDrag(Vector2 rtPoint)
        {
            _activeDragGizmo?.UpdateDrag(rtPoint);
        }

        public void EndDrag()
        {
            if (_activeDragGizmo == null)
            {
                return;
            }

            _activeDragGizmo.EndDrag();
            _activeDragGizmo = null;
        }
```

- [ ] **Step 4: SceneEditor をビルド**

「ビルドコマンド」節の SceneEditor 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 5: 実機で操作を確認**

メイドを 2 体呼び出し、表示対象を「すべて表示」にする。

確認項目:
1. 非選択メイドのギズモを掴むと、そのメイドが選択され、そのまま最後まで移動できる（掴んだ途中で対象がすり替わらない）
2. ドラッグ中にギズモが二重に描かれない（掴んだ軸のハイライトが 1 つだけ出る）
3. カメラが掴んだメイドへ飛ばない
4. 操作後に Undo すると、掴んだメイドの位置が戻る
5. 選択中メイドのギズモは従来通り操作できる
6. ギズモの無い場所をクリックすると従来通りオブジェクト選択が働く
7. 表示対象「選択中」に戻すと、非選択メイドのギズモは掴めない
8. SceneView / GameView の両方で 1〜7 が成立する

- [ ] **Step 6: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): 非選択メイドのギズモを掴めるようにし選択を追従させる"
```

---

### Task 6: `GizmoToolClient` に表示対象ブリッジを追加

**Files:**
- Modify: `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolClient.cs`
- Copy to MIE: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoToolClient.cs`

**Interfaces:**
- Consumes: `GizmoRenderer.gizmoTargetType`（Task 2）、`GizmoTargetType`（Task 1）
- Produces: `GizmoToolClient.isTargetTypeAvailable`（`bool`）、`GizmoToolClient.targetType`（`GizmoTargetType`、get/set）

- [ ] **Step 1: フィールドを追加**

既存のフィールド宣言（`_toolProp` ほか）へ足す。

```csharp
        private static PropertyInfo _targetTypeProp;
        private static Type _hostTargetTypeType;
```

- [ ] **Step 2: `isTargetTypeAvailable` と `targetType` を追加**

`useLocalSpace` プロパティの直後に置く。

```csharp
        /// <summary>
        /// ホストが表示対象プロパティを持っているか。
        /// 旧版 SceneEditor には存在しないため、これだけ欠けても
        /// 操作種別・軸空間の同期は止めない (isAvailable とは別に持つ)
        /// </summary>
        public static bool isTargetTypeAvailable
        {
            get
            {
                Initialize();
                return _targetTypeProp != null && !_failed;
            }
        }

        /// <summary>
        /// SceneEditor 側のギズモ表示対象。取得失敗時は SceneEditor の既定と同じ All。
        /// 失敗時の扱いは tool と同じ (isTargetTypeAvailable で判別する)
        /// </summary>
        public static GizmoTargetType targetType
        {
            get
            {
                if (!isTargetTypeAvailable)
                {
                    return GizmoTargetType.All;
                }

                try
                {
                    return (GizmoTargetType)Convert.ToInt32(_targetTypeProp.GetValue(null, null));
                }
                catch (Exception e)
                {
                    FailTargetType("表示対象の取得に失敗しました", e);
                    return GizmoTargetType.All;
                }
            }
            set
            {
                if (!isTargetTypeAvailable)
                {
                    return;
                }

                try
                {
                    _targetTypeProp.SetValue(null, Enum.ToObject(_hostTargetTypeType, (int)value), null);
                }
                catch (Exception e)
                {
                    FailTargetType("表示対象の設定に失敗しました", e);
                }
            }
        }
```

- [ ] **Step 3: `Initialize` で表示対象プロパティを任意解決する**

`_hostToolType = toolProp.PropertyType;` の直後に足す。必須条件には含めない。

```csharp
            // 表示対象は後から追加されたプロパティ。旧版 SceneEditor には無いため、
            // 見つからなくても他の同期は続けられるように任意扱いにする
            var targetProp = type.GetProperty("gizmoTargetType", BindingFlags.Public | BindingFlags.Static);
            if (targetProp != null && targetProp.PropertyType.IsEnum && targetProp.CanWrite)
            {
                _targetTypeProp = targetProp;
                _hostTargetTypeType = targetProp.PropertyType;
            }
            else
            {
                MTEUtils.LogWarning("GizmoToolClient: GizmoRenderer に表示対象プロパティが見つかりませんでした (表示対象は同期しません)");
            }
```

- [ ] **Step 4: 表示対象だけを無効化する `FailTargetType` を追加**

既存の `Fail` の隣に置く。`Fail` は `_failed` を立てて全同期を止めるため、表示対象の失敗では使わない。

```csharp
        /// <summary>
        /// 表示対象だけを無効化する。操作種別・軸空間の同期は生かしたままにする
        /// </summary>
        private static void FailTargetType(string message, Exception e)
        {
            MTEUtils.LogWarning("GizmoToolClient: " + message + ": " + e.Message);
            _targetTypeProp = null;
        }
```

- [ ] **Step 5: 両リポジトリをビルド**

まず SceneEditor をビルドし、次に MIE へコピーしてから MIE をビルドする。

```bash
SE=W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MIE=W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
cp "$SE/MTEUtils/GizmoToolClient.cs" "$MIE/MTEUtils/GizmoToolClient.cs"
diff "$SE/MTEUtils/GizmoToolClient.cs" "$MIE/MTEUtils/GizmoToolClient.cs" && echo IDENTICAL
```

「ビルドコマンド」節の 4 コマンド（SceneEditor 2 + MIE 2）を実行する。
期待: すべて `0 Error(s)`。

- [ ] **Step 6: コミット（2 リポジトリ）**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolClient.cs
git commit -m "feat(link): ギズモ表示対象のブリッジを GizmoToolClient へ追加する"

cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoToolClient.cs
git commit -m "feat(link): ギズモ表示対象のブリッジを GizmoToolClient へ追加する"
```

---

### Task 7: MIE 側の同期と重複行の削除

**Files:**
- Modify: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs:1685-1757`
- Modify: `COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs:73`

**Interfaces:**
- Consumes: `GizmoToolClient.targetType` / `isTargetTypeAvailable`（Task 6）、`SelfModelPlacer.gizmoTargetType`（Task 1 で共有 enum へ変更済み）

- [ ] **Step 1: 同期用フィールドを追加**

`_lastSyncedUseLocalSpace` の隣に置く。

```csharp
        private GizmoTargetType _lastSyncedGizmoTargetType;
```

- [ ] **Step 2: 初回同期に表示対象を足す**

`UpdateGizmoToolSync` の初回ブロックを書き換える。

変更前:

```csharp
            if (!_gizmoToolSyncStarted)
            {
                // 初回は SceneEditor 側の現在値へ合わせる (SceneEditor を正とする)
                _gizmoToolSyncStarted = true;
                dragType = FromGizmoTool(hostTool);
                useLocalSpace = hostUseLocalSpace;
                _lastSyncedDragType = dragType;
                _lastSyncedUseLocalSpace = useLocalSpace;
                return;
            }
```

変更後:

```csharp
            if (!_gizmoToolSyncStarted)
            {
                // 初回は SceneEditor 側の現在値へ合わせる (SceneEditor を正とする)
                _gizmoToolSyncStarted = true;
                dragType = FromGizmoTool(hostTool);
                useLocalSpace = hostUseLocalSpace;
                _lastSyncedDragType = dragType;
                _lastSyncedUseLocalSpace = useLocalSpace;

                // 表示対象は旧版 SceneEditor に無いため、扱えるときだけ合わせる。
                // 扱えない間は MIE 自身の Config が正のまま
                if (GizmoToolClient.isTargetTypeAvailable)
                {
                    gizmoTargetType = GizmoToolClient.targetType;
                }
                _lastSyncedGizmoTargetType = gizmoTargetType;
                return;
            }
```

- [ ] **Step 3: 毎フレームの差分同期に表示対象を足す**

`_lastSyncedUseLocalSpace = useLocalSpace;` の直後（メソッド末尾）に追加する。

```csharp
            if (GizmoToolClient.isTargetTypeAvailable)
            {
                if (gizmoTargetType != _lastSyncedGizmoTargetType)
                {
                    GizmoToolClient.targetType = gizmoTargetType;
                }
                else
                {
                    gizmoTargetType = GizmoToolClient.targetType;
                }
                _lastSyncedGizmoTargetType = gizmoTargetType;
            }
```

- [ ] **Step 4: Inspector 委譲描画から重複行を削除**

Task 1 の Step 10 で差し替えた `ModelInspectorDrawer.cs` の表示対象行を丸ごと削除する。SceneEditor の Inspector には Task 3 で追加した行が既にあり、同じ設定の行が 2 つ並ぶため。

削除するコード:

```csharp
            GizmoTargetRowDrawer.Draw(_view, new GizmoTargetRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTargetType = () => placer.gizmoTargetType,
                setTargetType = value => placer.gizmoTargetType = value,
            });
```

- [ ] **Step 5: MIE をビルド**

「ビルドコマンド」節の MIE 用 2 コマンドを実行する。
期待: どちらも `0 Error(s)`。

- [ ] **Step 6: 実機で連動を確認**

SceneEditor と MIE の両 DLL を反映した状態で確認する（ゲーム起動中なら CLAUDE.md のホットリロード手順、そうでなければ再起動）。

確認項目:
1. SceneEditor の Inspector で表示対象を切り替えると、MIE のモデル操作ウィンドウの表示が追従する
2. MIE のモデル操作ウィンドウで切り替えると、SceneEditor 側とメイドギズモの表示が追従する
3. MIE のモデルを選択したとき、SceneEditor の Inspector に表示対象行が 1 つだけ出る
4. 配置モデルのギズモが表示対象に従う（従来通り）
5. ゲームを再起動しても表示対象と軸空間（Local/Global）が保持され、初回同期で MIE 側も保存値へ揃う

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs \
        source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs
git commit -m "feat(link): ギズモ表示対象を SceneEditor と双方向同期する"
```

---

## 実装後

`code-review` スキルでレビューし、指摘を取り込んでからユーザーへ提示する（ワークスペースの標準フロー）。

## レビュー却下メモ

- `_maidGizmoTargets` は `_maidGizmos[i].target.gameObject` から導出できて冗長 — 却下。掴んだ対象の GameObject を明示的に持つほうが `_maidGizmoCount` と添字が揃って読みやすく、フィールド 1 つぶんの削減に見合わない
- Config の永続化フィールドを独立したコメントブロックへ切り出すべき — 却下。計画の Task 2 Step 1 は既に「// ギズモ設定。…」という独立ブロックとして追加する内容になっており、対処済み
- ポーズボーン / IK 選択中の抑止に `hasIKSelection` を足すべき — 却下（要件の記述側を訂正）。`SelectionManager.SelectIK` は `_gizmoSuppressed = false` を明示しており、IK 選択中も選択中メイドのルートギズモは出たままになるのが既存の意図。非選択メイドだけ消すと扱いが食い違うため、`gizmoSuppressed` の意味にそのまま揃え、設計書の「IK 選択中」という記述のほうを訂正した
