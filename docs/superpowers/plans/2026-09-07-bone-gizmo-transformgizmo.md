# ボーン回転ギズモを TransformGizmo へ寄せる実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: プロジェクトの CLAUDE.md に従い superpowers:executing-plans でタスク単位に実装すること（subagent-driven-development は使わない）。ステップはチェックボックス (`- [ ]`) で進捗管理する。

**Goal:** Alt 修飾キーで出るボーン回転ギズモ（手首/足首/頭・肘/膝・肩/腿/鎖骨）を、ゲーム側 `GizmoRender` を継承した `ModelGizmoRender` から、プラグイン自前の `TransformGizmo` へ載せ替え、SceneView ウィンドウ内でもドラッグできるようにする。

**Architecture:** `GizmoRenderer`（カメラごとに 1 個ぶら下がる `MonoBehaviour`）に、既存の「非選択メイド用ギズモ」と同じプール方式でボーン用 `TransformGizmo` の器を足す。対象ボーンは静的デリゲート `boneGizmoTargetsProvider` 経由で `MaidBoneGizmoController` から供給する（既存の `externalTargetProvider` と同じ作法）。`MaidBoneGizmoController` は `ModelGizmoRender` の生成をやめ、修飾キーによる表示グループ判定とボーン解決だけを担う。掴んだ瞬間のモーション停止・履歴記録は `onBoneGizmoDragBegin` コールバックで受ける。

**Tech Stack:** C# (.NET Framework 3.5 / 4.x の 2 構成), Unity, xUnit（純ロジックのみ）, UnityInjector プラグイン

**Spec:** 本ドキュメントの「背景と決定事項」節（独立した spec は起こさない）

---

## 背景と決定事項

### なぜ必要か

ゲーム側 `GizmoRender` の掴み判定は `Camera.main.ScreenPointToRay(Input.mousePosition)` に固定されている（`W:\COM3D2_5\work\Assembly-CSharp\GizmoRender.cs:205, :265`）。マウス座標をゲーム画面のスクリーン座標として解釈するため、SceneView ウィンドウ内でドラッグしても当たり判定がまったく合わない。2026-09-06 の `gizmo-per-camera-scale` で見た目（サイズ・回転リングの表裏）は SceneView カメラ基準に直したが、ドラッグは非目標として残していた。

一方 `TransformGizmo`（`source/COM3D2.SceneEditor.Plugin/MTEUtils/TransformGizmo.cs`）はカメラ非依存に作られている。

| メンバ | 行 | 備考 |
|---|---|---|
| `Draw(Camera camera)` | `:158` | 描画先カメラを引数で受ける |
| `TryBeginDrag(Camera camera, Vector2 rtPoint)` | `:420` | 掴み判定のカメラと RT ピクセル座標を引数で受け、`_dragCamera` に保持（`:488`） |
| `UpdateDrag(Vector2 rtPoint)` | `:677` | 解決は `_dragCamera` 基準。回転は `target.rotation` へ書く（`:702`） |
| `EndDrag()` | `:769` | |
| `sizeScale` / `tool` / `useLocalSpace` | `:36 / :33 / :34` | |

つまり **入出力の器はすでに揃っている**。載せ替えれば SceneView でも掴める。

### 既存の配線（調査済み・そのまま使える）

- SceneView の入力ルーティング: `SceneViewWindow.UpdatePointerInput`（`source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs:465-553`）が `gizmo.isDragging` → `UpdateDrag` / `EndDrag`、押下時は `gizmo.TryBeginDrag(rtPoint)` を呼ぶ。`gizmo` は `sceneViewManager.gizmoRenderer`。
- ゲーム画面側の入力ルーティング: `GameViewWindow.UpdateGizmoInput`（`source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs:319-372`）が同じ形で呼ぶ。`GuiToRtPoint`（`:295-313`）が最大化時は生のスクリーン座標、ウィンドウ時は RT 座標へ変換する。
- ゲーム画面への `GizmoRenderer` の載せ方: `GameViewManager.AttachGizmoRenderer`（`source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs:233-241`）が**メインカメラの GameObject** に付ける。`EnterWindowMode`（`:104`）から呼ばれ、`EnterWindowMode` は `OnPluginEnable`（`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:498`）で必ず実行される。したがってプラグイン有効中はゲーム画面にも自前ギズモが出る。
- 既存のプール実装の手本: `GizmoRenderer._maidGizmos`（`source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:109, 267-286`）。List を縮めずに件数だけ持ち回す。
- 既存の外部フックの手本: `GizmoRenderer.externalTargetProvider`（`:182`）を `BoneEditManager.Init`（`source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs:155`）が設定する。

### 移し替える現行の責務（`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs`）

| 行 | 責務 | 移行先 |
|---|---|---|
| `:60-99` (`SetTarget`) | ボーン名からボーンを解決し `ModelGizmoRender` を `AddComponent` | Transform だけ解決して保持する形へ変更 |
| `:122-150` (`Update`) | 修飾キーで表示グループを切替 / 押下フレームでモーション停止・履歴記録 | グループ判定は残す。停止・記録は掴んだ瞬間のコールバックへ移す |
| `:104-118` (`grabbedBoneName`) | 掴み中のボーン名。`GizmoRenderHack.IsGrabbed` に依存 | `GizmoRenderer.draggingBoneTarget` から引く |
| `:180-195` (`StopHeadToCamWhileGrabbingHead`) | 頭ギズモを掴んでいる間だけ顔追従を切る | 同上（掴み判定の取得元だけ差し替え） |
| `:198-212` (`GetVisibleGroup`) | Alt / Alt+Ctrl / Alt+Shift → グループ | そのまま（純ロジックへ切り出して単体テストを付ける） |

### 決定事項

1. **ボーンギズモは回転専用・ローカル軸固定にする。** 現行は `gizmo.eRotate = true` の回転リングのみで、軸空間もボーンのローカル軸。共有 UI 設定（`GizmoRenderer.currentTool` / `useLocalSpace`）には追従させない。追従させると Alt を押した瞬間に移動ギズモが出てボーンが平行移動できてしまい、現行の操作感から乖離する。
2. **表示倍率は既定 FoV でおおよそ現行と一致する値にする。** 現行のギズモ長は FoV 修正後の `generalLens` に `offsetScale = 0.25` を掛けた値で、`0.5 * tan(fov/2) * dist * 0.25`。FoV 45° なら `0.0518 * dist`。`TransformGizmo` は `dist * 0.15 * sizeScale`（`TransformGizmo.cs:126-135`）なので `sizeScale = 0.0518 / 0.15 ≒ 0.345`。**この値は実機で目視調整する前提の初期値**とする。
3. **見かけの大きさが FoV に追従しなくなる差異は許容する。** ゲーム側は `tan(fov/2)` を掛けていたので FoV を変えても画面上の大きさが一定だったが、`TransformGizmo` は距離のみに比例する。プラグインの他のギズモ（選択オブジェクト・メイドルート）はすべて後者なので、揃うほうが一貫する。
4. **モーション停止・履歴記録は「掴んだ瞬間」に移す。** 現行は表示グループがある状態の左クリック押下すべてで、実際には掴まなかったクリックでも `StopMotion` と履歴記録が走っていた。掴んだボーンが分かるようになるので、記録対象もグループ全体から掴んだ 1 本へ絞る。
5. **`ModelGizmoRender` と `GizmoRenderHack` は削除する。** 移行後は他に利用者がいない（`ScreenshotManager.cs:254` の隠蔽ループも不要になる。`GizmoRenderer` 自体は `:243` で既に隠している）。
6. **`GizmoScaleFixPatch` は残す。** ボーンギズモが `GizmoRender` を使わなくなっても、ゲーム本体のエディット画面等が使う `GizmoRender` は残るため、FoV 修正と SceneView 用のカメラ差し替えはそのまま有効にしておく。

### 非目標

- ボーン編集ウィンドウの「選択ボーン」ギズモ（`BoneEditManager.Init` が `externalTargetProvider` で出しているもの）の変更。これは既に `TransformGizmo` 経由なので SceneView で掴めるはず。Task 1 の冒頭で現状を確認するだけにとどめる。
- ドラッグ点（IK・顔・上体・骨盤）の変更。
- ギズモの見た目のデザイン変更。`TransformGizmo` の既存の描画をそのまま使う。

## Global Constraints

- コメントとログメッセージは日本語で書く（CLAUDE.md）。
- ハードコーディングは絶対に必要な場合を除き避ける（CLAUDE.md）。
- COM3D2 (2.0) と COM3D2.5 の **2 構成でビルドが通ること**。対象フレームワークが異なるため必ず両方ビルドする。
- `deploy.bat` / `deploy.ps1` は実行しない。`debug.bat` も使わない（ゲーム停止中に実機へ反映してしまう）。ビルド確認は MSBuild を直接叩く。
- `GizmoRenderer.OnPostRender` と `TryBeginDrag` はカメラごとに毎フレーム走る。ボーンギズモの組み直しでフレーム毎のアロケーションを増やさない（List は縮めず件数だけ持ち回す既存方式に合わせる）。
- 既存の公開シグネチャを壊さない。`MaidBoneGizmoController` の `SetTarget` / `Update` / `Destroy` / `grabbedBoneName` は呼び出し側（`MaidManipulateManager.cs:251, 258, 259, 571` / `InspectorWindow.cs:339`）がそのまま使えること。

## File Structure

- `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` — **変更**。ボーンギズモのプール・供給フック・掴み判定・描画を追加する。既存の `_maidGizmos` と同じ構造を 1 段増やすだけで、責務は「このカメラのギズモ群を描いて掴ませる」のまま変わらない。
- `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs` — **変更**。`ModelGizmoRender` の生成・破棄をやめ、ボーン解決とグループ判定に絞る。
- `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` — **変更**。`Init()` を追加して静的フックを登録する。
- `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs` — **変更**。`ModelGizmoRender` の隠蔽ループを削除する。
- `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelGizmoRender.cs` — **削除**。
- `source/COM3D2.SceneEditor.Plugin/MaidManipulation/GizmoRenderHack.cs` — **削除**。
- `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` — **変更**。削除した 2 ファイルの `<Compile Include>` を消す。
- `source/COM3D2.SceneEditor.Plugin.Tests/MaidBoneGizmoGroupTests.cs` — **新規**。修飾キー → 表示グループの純ロジックを検証する。

## 検証方針

`GizmoRenderer` / `TransformGizmo` は `Camera` と `MonoBehaviour` に依存するため、テストプロジェクト（`source/COM3D2.SceneEditor.Plugin.Tests`、Unity ランタイム無しで DLL を参照する xUnit）では動かせない。純ロジックに切り出せる「修飾キー → 表示グループ」だけ単体テストを書き、残りは **稼働中のゲームに対する devbridge (`mcp__com3d25-devbridge__eval_csharp`) と目視** で検証する。CLAUDE.md の「静的解析で結論が出ないときは必ず実機で裏取りする」に沿う。

既存テストは本変更と無関係だが、リグレッション確認のため最終タスクで一度実行する。`EyesItemInspectorTests.ResolveRowKind` の 4 件は本変更以前から失敗している既知の失敗であり、本計画の対象外（期待値: 389 成功 / 4 失敗）。

### ビルド確認用の bat

作業ディレクトリ `source/COM3D2.SceneEditor.Plugin` に `_build_check.bat` を作って使う（Git Bash から MSBuild へ `/p:` を直接渡すと引数が壊れるため）。**このファイルはコミットせず、最終タスクで削除する。**

```bat
@echo off
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=%1 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo
```

Git Bash からは絶対パスで呼ぶこと（`cmd.exe //c "W:\...\_build_check.bat" COM3D25`）。

**注意: `COM3D2` 構成のビルドは `bin/Debug/COM3D25/` の出力を消す。** 実機へホットリロードする前は必ず `COM3D25` を最後にビルドし直すこと。

---

### Task 1: GizmoRenderer にボーンギズモの器を追加する

供給フックが未設定なら何も起きない純粋な追加なので、この時点では挙動が変わらない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs`

**Interfaces:**
- Consumes: `TransformGizmo`（`MTEUtils/TransformGizmo.cs`）の `target` / `tool` / `useLocalSpace` / `sizeScale` / `Draw(Camera)` / `TryBeginDrag(Camera, Vector2)` / `isDragging`
- Produces:
  - `public static Func<IList<Transform>> GizmoRenderer.boneGizmoTargetsProvider` — 表示したいボーンの一覧を返す。null / 空なら 1 本も出さない。毎フレーム呼ばれる。
  - `public static Action<Transform> GizmoRenderer.onBoneGizmoDragBegin` — ボーンギズモを掴んだ直後、最初の `UpdateDrag` より前に 1 回呼ばれる。
  - `public Transform GizmoRenderer.draggingBoneTarget { get; private set; }` — ボーンギズモをドラッグ中ならそのボーン、それ以外は null。

- [ ] **Step 1: 既存の選択ボーンギズモが SceneView で掴めるか実機で確認する**

移行範囲を正しく切るための事前確認。ゲームを起動し、ボーン編集モードでポーズ定義を持たないボーン（`Bip01` 系スライダー対象以外。例: 髪・胸・スカートのボーン）を選び、SceneView ウィンドウ内でギズモをドラッグする。

Expected: 掴めて回る（`BoneEditManager.Init` の `externalTargetProvider` 経由で既に `TransformGizmo` が使われているため）。掴めない場合は SceneView 側の入力配線に別の問題があるので、**先に進まずユーザーへ報告する**。

- [ ] **Step 2: フィールドとフックを追加する**

`GizmoRenderer` クラスの `_activeDragGizmo` の宣言（`private TransformGizmo _activeDragGizmo;`）の直後に追加する:

```csharp
        /// <summary>
        /// ボーン回転ギズモの表示倍率。
        /// ゲーム側 GizmoRender のボーンギズモは generalLens (= 0.5 * tan(fov/2) * 距離) に
        /// offsetScale 0.25 を掛けた長さで、FoV 45 度では距離の 0.0518 倍だった。
        /// TransformGizmo は距離の 0.15 倍 * sizeScale なので、そこへ合わせた値
        /// </summary>
        private const float BoneGizmoSizeScale = 0.345f;

        /// <summary>
        /// ボーン回転ギズモの対象を供給する外部フック (MaidBoneGizmoController が設定する)。
        /// 修飾キーで表示グループが変わるため毎フレーム引き直す。
        /// GizmoRenderer をメイド操作のコードから切り離すため、
        /// externalTargetProvider と同じくデリゲート経由にしている
        /// </summary>
        public static Func<IList<Transform>> boneGizmoTargetsProvider;

        /// <summary>
        /// ボーンギズモを掴んだ直後に 1 回だけ呼ばれる。
        /// モーション停止と履歴記録は「どのボーンを掴んだか」を知っている側の責務なので外へ出す
        /// </summary>
        public static Action<Transform> onBoneGizmoDragBegin;

        /// <summary>
        /// ボーン回転ギズモ。非選択メイド用と同じく List は縮めず、有効件数だけ持ち回す
        /// </summary>
        private readonly List<TransformGizmo> _boneGizmos = new List<TransformGizmo>();

        /// <summary>_boneGizmos と同じ添字で対象ボーンを保持する</summary>
        private readonly List<Transform> _boneGizmoTargets = new List<Transform>();

        /// <summary>現在有効な要素数</summary>
        private int _boneGizmoCount;

        /// <summary>ドラッグ中のボーン。ボーンギズモを掴んでいなければ null</summary>
        public Transform draggingBoneTarget { get; private set; }
```

ファイル先頭の using に `System.Collections.Generic` は既にあるので追加不要。`System`（`Func` / `Action` 用）も既にある。

- [ ] **Step 3: 対象の組み直しを追加する**

`RebuildMaidGizmos()` メソッドの直前に追加する:

```csharp
        /// <summary>
        /// ボーン回転ギズモの対象を組み直す。
        /// _gizmo が担当しているボーン (ボーン編集ウィンドウの選択ボーン) は
        /// 同じ Transform に 2 個描かれてしまうため除く
        /// </summary>
        private void RebuildBoneGizmos()
        {
            _boneGizmoCount = 0;

            var provider = boneGizmoTargetsProvider;
            if (provider == null)
            {
                return;
            }

            var targets = provider();
            if (targets == null)
            {
                return;
            }

            var selectedBone = _gizmo.target;
            for (var i = 0; i < targets.Count; i++)
            {
                var bone = targets[i];
                if (bone == null || bone == selectedBone)
                {
                    continue;
                }

                if (_boneGizmoCount >= _boneGizmos.Count)
                {
                    _boneGizmos.Add(new TransformGizmo());
                    _boneGizmoTargets.Add(null);
                }

                var gizmo = _boneGizmos[_boneGizmoCount];
                gizmo.target = bone;
                // ボーンギズモは回転専用・ローカル軸固定。共有 UI 設定には追従させない
                // (Alt を押した瞬間に移動ギズモが出てボーンが平行移動できてしまうため)
                gizmo.tool = GizmoTool.Rotate;
                gizmo.useLocalSpace = true;
                gizmo.sizeScale = BoneGizmoSizeScale;

                _boneGizmoTargets[_boneGizmoCount] = bone;
                _boneGizmoCount++;
            }
        }
```

- [ ] **Step 4: SyncGizmo から組み直しを呼ぶ**

`SyncGizmo()` 末尾の以下を差し替える:

```csharp
            // ドラッグ中に組み直すと、掴んでいるインスタンスが別のメイドへ
            // 使い回されて操作対象がすり替わる
            if (_activeDragGizmo == null)
            {
                RebuildMaidGizmos();
            }
```

差し替え後:

```csharp
            // ドラッグ中に組み直すと、掴んでいるインスタンスが別のメイド・別のボーンへ
            // 使い回されて操作対象がすり替わる
            if (_activeDragGizmo == null)
            {
                RebuildBoneGizmos();
                RebuildMaidGizmos();
            }
```

`RebuildBoneGizmos` は `_gizmo.target` を読むため、`_gizmo.target` を設定した後に呼ぶ必要がある。`SyncGizmo` の該当箇所は既にその順序になっている。

- [ ] **Step 5: 描画に追加する**

`OnPostRender()` の末尾を差し替える:

```csharp
            for (var i = 0; i < _maidGizmoCount; i++)
            {
                _maidGizmos[i].Draw(_camera);
            }
```

差し替え後:

```csharp
            for (var i = 0; i < _boneGizmoCount; i++)
            {
                _boneGizmos[i].Draw(_camera);
            }

            for (var i = 0; i < _maidGizmoCount; i++)
            {
                _maidGizmos[i].Draw(_camera);
            }
```

- [ ] **Step 6: 掴み判定に追加する**

`TryBeginDrag(Vector2 rtPoint)` の、選択中ギズモを試す `if (_gizmo.TryBeginDrag(...))` ブロックの直後（`for (var i = 0; i < _maidGizmoCount; i++)` の直前）に挿入する:

```csharp
            // ボーンはメイドルートより小さく重なりやすいので、メイドルートより先に試す
            for (var i = 0; i < _boneGizmoCount; i++)
            {
                if (!_boneGizmos[i].TryBeginDrag(_camera, rtPoint))
                {
                    continue;
                }

                _activeDragGizmo = _boneGizmos[i];
                draggingBoneTarget = _boneGizmoTargets[i];

                // 最初の UpdateDrag より前に呼ぶ。履歴は変更前の姿勢を記録する必要がある
                if (onBoneGizmoDragBegin != null)
                {
                    onBoneGizmoDragBegin(draggingBoneTarget);
                }
                return true;
            }
```

- [ ] **Step 7: ドラッグ終了時に掴んだボーンを落とす**

`UpdateDrag` と `EndDrag` で `_activeDragGizmo` を null にしている箇所が 2 つあるので、`draggingBoneTarget` も一緒に落とす。まず後始末を 1 箇所へまとめるヘルパーを `EndDrag()` の直前に追加する:

```csharp
        /// <summary>掴んでいる状態を落とす。掴んだボーンの持ち回しも一緒に切る</summary>
        private void ClearActiveDrag()
        {
            _activeDragGizmo = null;
            draggingBoneTarget = null;
        }
```

`UpdateDrag` の以下を差し替える:

```csharp
            if (!_activeDragGizmo.isDragging)
            {
                _activeDragGizmo = null;
            }
```

差し替え後:

```csharp
            if (!_activeDragGizmo.isDragging)
            {
                ClearActiveDrag();
            }
```

`EndDrag` の以下を差し替える:

```csharp
            _activeDragGizmo.EndDrag();
            _activeDragGizmo = null;
```

差し替え後:

```csharp
            _activeDragGizmo.EndDrag();
            ClearActiveDrag();
```

- [ ] **Step 8: 2 構成でビルドが通ることを確認する**

Run: `_build_check.bat` を `COM3D25` と `COM3D2` の両方の引数で実行
Expected: 両方とも `COM3D2.SceneEditor.Plugin -> ...\COM3D2.SceneEditor.Plugin.dll` が出て error なし

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): GizmoRenderer にボーン回転ギズモの器を追加する"
```

---

### Task 2: MaidBoneGizmoController を TransformGizmo ベースへ差し替える

ここでボーンギズモが `ModelGizmoRender` から `TransformGizmo` へ切り替わる。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/MaidBoneGizmoGroupTests.cs`

**Interfaces:**
- Consumes: `GizmoRenderer.boneGizmoTargetsProvider` / `GizmoRenderer.onBoneGizmoDragBegin` / `GizmoRenderer.draggingBoneTarget`（Task 1）
- Produces:
  - `public static MaidBoneGizmoController.BoneGroup? MaidBoneGizmoController.ResolveGroup(bool alt, bool ctrl, bool shift)` — 修飾キーの組から表示グループを返す純関数。単体テストの対象にするため `public` かつ `Input` 非依存にする。`BoneGroup` も `public` へ上げる。
  - `public void MaidBoneGizmoController.RegisterGizmoHooks()` — GizmoRenderer の静的フックを繋ぐ。マネージャの `Init()` から 1 回だけ呼ぶ。
  - 既存の `SetTarget(Maid)` / `Update(bool)` / `Destroy()` / `grabbedBoneName` は名前・シグネチャを変えない。`SetTarget` 冒頭の `if (_maid == maid) return;` ガードも現行どおり残す（毎フレーム呼ばれるため、外すと毎フレームボーンを探し直す）。

- [ ] **Step 1: 表示グループ判定の失敗するテストを書く**

Create: `source/COM3D2.SceneEditor.Plugin.Tests/MaidBoneGizmoGroupTests.cs`

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidBoneGizmoGroupTests
    {
        [Fact]
        public void ResolveGroup_Alt非押下ならグループ無し()
        {
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, false, false));
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, true, false));
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, false, true));
        }

        [Theory]
        [InlineData(false, false, MaidBoneGizmoController.BoneGroup.Tip)]
        [InlineData(true, false, MaidBoneGizmoController.BoneGroup.Mid)]
        [InlineData(false, true, MaidBoneGizmoController.BoneGroup.Root)]
        // Ctrl と Shift を同時に押した場合は Ctrl を優先する (現行の判定順どおり)
        [InlineData(true, true, MaidBoneGizmoController.BoneGroup.Mid)]
        public void ResolveGroup_Alt押下中は修飾キーでグループが決まる(
            bool ctrl, bool shift, MaidBoneGizmoController.BoneGroup expected)
        {
            Assert.Equal(expected, MaidBoneGizmoController.ResolveGroup(true, ctrl, shift));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests -v q`
Expected: `MaidBoneGizmoGroupTests` がコンパイルエラーで失敗する（`ResolveGroup` / `BoneGroup` が公開されていないため）

- [ ] **Step 3: MaidBoneGizmoController を書き換える**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs` の中身を以下で全置換する:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン回転ギズモ。修飾キーで表示対象グループを切り替える（MultipleMaids 準拠）。
    /// Alt=手首/足首/頭、Alt+Ctrl=肘/膝、Alt+Shift=肩/腿/鎖骨。
    ///
    /// ギズモの実体はカメラごとの GizmoRenderer が持つ TransformGizmo で、
    /// ここは「どのボーンに出すか」を供給するだけ。
    /// ゲーム側 GizmoRender は掴み判定が Camera.main のスクリーン座標に固定されており
    /// SceneView で操作できないため、自前ギズモへ寄せている
    /// </summary>
    public class MaidBoneGizmoController
    {
        public enum BoneGroup
        {
            Tip,    // 手首/足首/頭（Alt）
            Mid,    // 肘/膝（Alt+Ctrl）
            Root,   // 肩/腿/鎖骨（Alt+Shift）
        }

        private static readonly Dictionary<BoneGroup, string> GroupDisplayNames
            = new Dictionary<BoneGroup, string>
        {
            { BoneGroup.Tip, "手首/足首/頭" },
            { BoneGroup.Mid, "肘/膝" },
            { BoneGroup.Root, "肩/腿/鎖骨" },
        };

        private const string HeadBoneName = "Bip01 Head";

        private static readonly Dictionary<BoneGroup, string[]> BoneNames
            = new Dictionary<BoneGroup, string[]>
        {
            { BoneGroup.Tip, new[] { "Bip01 L Hand", "Bip01 R Hand", "Bip01 L Foot", "Bip01 R Foot",
                HeadBoneName } },
            { BoneGroup.Mid, new[] { "Bip01 L Forearm", "Bip01 R Forearm", "Bip01 L Calf", "Bip01 R Calf" } },
            // 鎖骨は肩と同時に整えることが多いため Root グループに同居させる
            { BoneGroup.Root, new[] { "Bip01 L UpperArm", "Bip01 R UpperArm", "Bip01 L Thigh", "Bip01 R Thigh",
                "Bip01 L Clavicle", "Bip01 R Clavicle" } },
        };

        private Maid _maid = null;

        /// <summary>グループごとの対象ボーン。SetTarget で 1 回だけ解決する</summary>
        private readonly Dictionary<BoneGroup, List<Transform>> _bones
            = new Dictionary<BoneGroup, List<Transform>>();

        private Transform _headBone = null;

        /// <summary>いま表示しているグループ。非表示なら null</summary>
        private BoneGroup? _visibleGroup = null;

        /// <summary>ギズモに渡す一覧。毎フレームの確保を避けるため使い回す</summary>
        private readonly List<Transform> _visibleBones = new List<Transform>();

        /// <summary>
        /// GizmoRenderer からの供給フックを繋ぐ。
        /// マネージャの Init から 1 回だけ呼ぶ
        /// </summary>
        public void RegisterGizmoHooks()
        {
            GizmoRenderer.boneGizmoTargetsProvider = GetVisibleBones;
            GizmoRenderer.onBoneGizmoDragBegin = OnDragBegin;
        }

        public void SetTarget(Maid maid)
        {
            if (_maid == maid)
            {
                return;
            }

            Destroy();

            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody
                || maid.body0.m_Bones == null)
            {
                return;
            }

            _maid = maid;

            foreach (var pair in BoneNames)
            {
                var list = new List<Transform>();
                foreach (var boneName in pair.Value)
                {
                    var bone = CMT.SearchObjName(maid.body0.m_Bones.transform, boneName, false);
                    if (bone == null)
                    {
                        // ボーンが見つからない部位はスキップする
                        MTEUtils.LogWarning("ボーンが見つかりません: {0}", boneName);
                        continue;
                    }

                    list.Add(bone);

                    if (boneName == HeadBoneName)
                    {
                        _headBone = bone;
                    }
                }
                _bones[pair.Key] = list;
            }
        }

        /// <summary>
        /// ギズモを掴んで操作中のボーン名。掴んでいなければ null。
        /// ポーズタブがスライダー表示の自動追従に使う
        /// </summary>
        public string grabbedBoneName
        {
            get
            {
                var bone = GrabbedBone();
                return bone != null ? bone.name : null;
            }
        }

        /// <summary>毎フレーム呼ぶ。修飾キーの状態で表示グループを切り替える</summary>
        public void Update(bool enabled)
        {
            _visibleGroup = enabled ? GetVisibleGroup() : null;
            StopHeadToCamWhileGrabbingHead();
        }

        /// <summary>
        /// 表示中グループのボーン。GizmoRenderer から毎フレーム呼ばれる。
        /// 対象が無いフレームは空リストを返す (null を返しても同じ扱いだが、
        /// 呼び出し側の分岐を減らすため常にリストを返す)。
        ///
        /// 返すのは使い回しの共有バッファで、次の呼び出しで中身が入れ替わる。
        /// SceneView と GameView の GizmoRenderer が同じフレームに順番に呼ぶため、
        /// 呼び出し側はその場で中身を自分の配列へ写し取ること。参照を保持してはいけない
        /// </summary>
        private IList<Transform> GetVisibleBones()
        {
            _visibleBones.Clear();

            List<Transform> bones;
            if (!_visibleGroup.HasValue || !_bones.TryGetValue(_visibleGroup.Value, out bones))
            {
                return _visibleBones;
            }

            foreach (var bone in bones)
            {
                if (bone != null)
                {
                    _visibleBones.Add(bone);
                }
            }
            return _visibleBones;
        }

        /// <summary>
        /// ギズモを掴んだ瞬間にモーションを止めて履歴を残す。
        /// 動いたままだとボーンが毎フレーム上書きされて操作できない
        /// </summary>
        private void OnDragBegin(Transform bone)
        {
            if (_maid == null || bone == null || !_visibleGroup.HasValue)
            {
                return;
            }

            MaidMotionState.StopMotion(_maid);
            HistoryManager.instance.BeforeEdit(_maid, HistoryScope.Pose,
                "ギズモ操作: " + GroupDisplayNames[_visibleGroup.Value], new[] { bone });
        }

        /// <summary>
        /// いずれかのビューで掴んでいるボーン。掴んでいなければ null。
        /// SceneView と GameView のどちらで掴んでもよい
        /// </summary>
        private static Transform GrabbedBone()
        {
            var sceneView = SceneViewManager.instance.gizmoRenderer;
            if (sceneView != null && sceneView.draggingBoneTarget != null)
            {
                return sceneView.draggingBoneTarget;
            }

            var gameView = GameViewManager.instance.gizmoRenderer;
            if (gameView != null && gameView.draggingBoneTarget != null)
            {
                return gameView.draggingBoneTarget;
            }

            return null;
        }

        /// <summary>
        /// 頭のギズモを掴んでいる間は顔追従を切る。
        /// TBody.MoveHeadAndEye が trsHead.localRotation を毎フレーム上書きするため、
        /// 切らないと回した端から追従先へ引き戻される。
        /// boHeadToCam を倒すだけだと HeadToCamPer がフェードし終えるまで Slerp が効き続けるので、
        /// 追従の割合そのものも 0 にする。
        /// 離した後も追従は切ったままにする。戻すと回した頭が即カメラ向きへ引かれて操作が無に帰すため。
        /// 追従の再開は「顔をカメラへ」トグル（MaidOperationWindow）でユーザーが選ぶ
        /// （顔向きドラッグ MaidFaceDragPoint も同じ扱い）
        /// </summary>
        private void StopHeadToCamWhileGrabbingHead()
        {
            if (_maid == null || _maid.body0 == null || _headBone == null)
            {
                return;
            }

            if (GrabbedBone() != _headBone)
            {
                return;
            }

            _maid.body0.boHeadToCam = false;
            _maid.body0.HeadToCamPer = 0f;
        }

        /// <summary>修飾キーの組から表示するグループを決める。Alt 非押下なら非表示</summary>
        private static BoneGroup? GetVisibleGroup()
        {
            return ResolveGroup(
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        /// <summary>
        /// 修飾キーの組み合わせから表示グループを決める純関数。
        /// Input に触らないので単体テストできる
        /// </summary>
        public static BoneGroup? ResolveGroup(bool alt, bool ctrl, bool shift)
        {
            if (!alt)
            {
                return null;
            }

            if (ctrl)
            {
                return BoneGroup.Mid;
            }
            if (shift)
            {
                return BoneGroup.Root;
            }
            return BoneGroup.Tip;
        }

        public void Destroy()
        {
            _bones.Clear();
            _visibleBones.Clear();
            _visibleGroup = null;
            _headBone = null;
            _maid = null;
        }
    }
}
```

- [ ] **Step 4: マネージャからフックを登録する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` の `boneGizmoController` フィールド宣言の下に `Init()` を追加する:

```csharp
        public override void Init()
        {
            // ボーンギズモの実体はカメラごとの GizmoRenderer が持つため、
            // 「どのボーンに出すか」と「掴んだときに何をするか」をここで繋ぐ
            boneGizmoController.RegisterGizmoHooks();
        }
```

`MaidManipulateManager` は `ManagerBase` を継承し（`:12`）、`COM3D2.SceneEditor.Plugin.cs:406` で `managerRegistry.RegisterManager` されているので `Init()` は自動で呼ばれる。既に `Init()` が存在する場合は中身を追記するだけにすること。

- [ ] **Step 5: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests -v q`
Expected: `MaidBoneGizmoGroupTests` の 5 ケースが PASS。既知の失敗 4 件（`EyesItemInspectorTests.ResolveRowKind`）以外に失敗が無いこと

- [ ] **Step 6: 2 構成でビルドが通ることを確認する**

Run: `_build_check.bat` を `COM3D25` / `COM3D2` で実行
Expected: 両方成功

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs \
        source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/MaidBoneGizmoGroupTests.cs
git commit -m "feat(gizmo): ボーン回転ギズモを TransformGizmo へ寄せて SceneView で掴めるようにする"
```

---

### Task 3: 使われなくなったゲーム側ギズモの薄皮を削除する

`ModelGizmoRender` と `GizmoRenderHack` は Task 2 で最後の利用者が消える。残しておくと「ボーンギズモはゲーム側 `GizmoRender` で動いている」という誤読を招くので消す。

**Files:**
- Delete: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelGizmoRender.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/GizmoRenderHack.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: なし
- Produces: なし（削除のみ）

- [ ] **Step 1: 参照が本当に残っていないことを確認する**

Run:

```bash
grep -rn "ModelGizmoRender\|GizmoRenderHack" --include=*.cs source/
```

Expected: `ModelGizmoRender.cs` / `GizmoRenderHack.cs` 自身と `ScreenshotManager.cs:254` のループ以外にヒットが無いこと。他にヒットがあれば削除せず**ユーザーへ報告する**。

- [ ] **Step 2: ScreenshotManager から隠蔽ループを削除する**

`HideOverlays` の以下を削除する:

```csharp
            // ボーン編集の回転ギズモは Alt 押下中だけ出る。Alt を押したままメニューを
            // クリックされると写り込むため、これも止める
            foreach (var gizmo in UnityEngine.Object.FindObjectsOfType<ModelGizmoRender>())
            {
                HideOverlay(hidden, gizmo);
            }
```

ボーンギズモは `GizmoRenderer` が描くようになり、`HideOverlay(hidden, gameViewManager.gizmoRenderer)`（同メソッド内）で既に止まるため、この個別対応は不要になる。

- [ ] **Step 3: ファイルを削除して csproj から外す**

```bash
git rm source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelGizmoRender.cs \
       source/COM3D2.SceneEditor.Plugin/MaidManipulation/GizmoRenderHack.cs
```

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` から次の 2 行を削除する（それぞれ 171 行目と 206 行目。削除で行番号がずれるので後ろから消すこと）:

```xml
    <Compile Include="MaidManipulation\GizmoRenderHack.cs" />
    <Compile Include="MaidManipulation\ModelGizmoRender.cs" />
```

- [ ] **Step 4: 2 構成でビルドが通ることを確認する**

Run: `_build_check.bat` を `COM3D25` / `COM3D2` で実行
Expected: 両方成功。`ModelGizmoRender` / `GizmoRenderHack` 未定義のエラーが出ないこと

- [ ] **Step 5: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin/
git commit -m "refactor(gizmo): 使われなくなった ModelGizmoRender と GizmoRenderHack を削除する"
```

---

### Task 4: 実機で検証する

ホットリロードでは `GizmoRenderer` が `MonoBehaviour` として既存カメラに付いており差し替えられないため、**DLL を入れてゲームを再起動して検証する**。

**Files:**
- 変更なし（検証のみ）

- [ ] **Step 1: ビルド済み DLL を入れてゲームを再起動する**

`COM3D25` 構成でビルドし直し、`source/COM3D2.SceneEditor.Plugin/bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll` を `W:\COM3D2_5\Sybaris\UnityInjector\` へコピーする。ゲームが起動中だとコピーに失敗するので、**先にゲームを終了してもらう**こと。コピー後の再起動はユーザーの操作に任せる。

`debug.bat` は使わない（ゲーム停止中に実機へ反映してしまう挙動を持つため、意図せぬタイミングで走らせない）。

- [ ] **Step 2: ゲーム画面でのリグレッションを確認する**

以下をユーザーに確認してもらう:

1. Alt を押すと手首/足首/頭に回転ギズモが出る。Alt+Ctrl で肘/膝、Alt+Shift で肩/腿/鎖骨に切り替わる
2. ギズモの大きさが変更前とおおむね同じ（大きすぎ・小さすぎなら `BoneGizmoSizeScale` を調整して Task 1 のコミットを追加修正する）
3. リングをドラッグするとボーンが回る。回転軸がボーンのローカル軸になっている
4. 掴んだ瞬間にモーションが止まる
5. Undo でギズモ操作を 1 手戻せる
6. 頭のギズモを掴むと顔追従が切れる（掴んだ端から正面へ引き戻されない）
7. ポーズタブのスライダー表示が、掴んだボーンへ自動追従する

- [ ] **Step 3: SceneView でのドラッグを確認する**

SceneView ウィンドウを開き、同じ操作を SceneView 内で行う。

Expected: SceneView 内でギズモを掴んで回せる。ギズモの大きさが SceneView のカメラ距離に応じて自然に見える。SceneView とゲーム画面のどちらで掴んでも、掴んでいる間もう一方のビューでは掴めない

- [ ] **Step 4: ギズモの重なりを確認する**

ボーン編集ウィンドウでボーンを選択し（選択ボーンのギズモが出る状態）、そのボーンが Alt グループに含まれる状態（例: `Bip01 Head` を選んで Alt）にする。

Expected: 同じボーンにギズモが 2 個重なって描かれない（`RebuildBoneGizmos` の除外が効いている）

- [ ] **Step 5: ドラッグ中のメイド切替を確認する**

メイドを 2 人以上呼び出し、片方のボーンギズモを掴んだままもう一方へ選択を移す。

`SetTarget` は切替で即座に `Destroy()` して `_bones` を空にするが、`GizmoRenderer` 側はドラッグ中 `RebuildBoneGizmos` を呼ばないため、掴んでいる Transform の参照だけは生き残る。

Expected: クラッシュしない。マウスを離せばドラッグが終わり、ギズモの表示が新しいメイドへ切り替わる。旧実装でも同種の状態は起きえたため、振る舞いが変わっていなければ良しとする。クラッシュや操作不能になる場合はユーザーへ報告し、`SetTarget` でドラッグ中なら両ビューの `EndDrag()` を呼ぶ対応を追加する

- [ ] **Step 6: スクリーンショットに写り込まないことを確認する**

Alt を押したままスクリーンショット撮影を実行する。

Expected: 保存された画像にボーンギズモが写っていない

---

### Task 5: 仕上げ（CHANGELOG・テスト・レビュー）

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: CHANGELOG に追記する**

`## 未リリース` の先頭付近へ追加する:

```markdown
- ボーンの回転ギズモを SceneView でも操作できるようになりました
  - ギズモの実装をプラグイン自前のものへ差し替えたため、SceneView / ゲーム画面の
    どちらからでも同じように掴めます
  - 掴んだ瞬間だけモーション停止と履歴記録を行うようになり、
    ギズモを外したクリックで履歴が増えなくなりました
```

- [ ] **Step 2: 一時ファイルを削除する**

```bash
rm -f source/COM3D2.SceneEditor.Plugin/_build_check.bat
git status --short
```

Expected: `_build_check.bat` が残っていないこと

- [ ] **Step 3: 単体テストを実行する**

Run: `test-runner` サブエージェントで `source/COM3D2.SceneEditor.Plugin.Tests` を実行
Expected: 394 成功 / 4 失敗（Task 2 で 5 ケース増える）。失敗は `EyesItemInspectorTests.ResolveRowKind` の既知 4 件のみで、新規の失敗が無いこと

- [ ] **Step 4: コードレビュー**

`code-review` スキルで機能面・可読性の 2 エージェントを並列起動し、指摘を反映する。

- [ ] **Step 5: コミット**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): ボーン回転ギズモが SceneView でも操作できることを追記する"
```

---

## レビュー却下メモ

- **`BoneGizmoSizeScale = 0.345f` のマジックナンバー化** — 却下。指摘したレビュアー自身が「是認できる範囲」「Config 化までは過剰 (YAGNI)」と結論している。値の導出はコメントへ残し、他のギズモサイズ定数と同じく const で持つ既存の慣習に合わせる。
