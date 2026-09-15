# ギズモ表示状態の連携 (GizmoHost.IsGizmoVisible) 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans` でタスク単位に実行すること（本リポジトリでは subagent-driven-development は使わない）。手順はチェックボックス (`- [ ]`) 形式。

**Goal:** SceneEditor の「ボーン表示」トグル／SceneView のギズモ表示トグルを OFF にしたとき、SceneEditor のビュー上に描かれる ModItemExplorer のギズモも一緒に消える（掴めなくなる）ようにする。

**Architecture:** SceneEditor の公開 API `GizmoHost` に `IsGizmoVisible(Camera)` を追加し、指定カメラに付いている `GizmoRenderer.isDrawEnabled`（= SceneView ツールバーのギズモ表示 AND メニューバーのボーン表示）を返す。MTEUtils の `GizmoHostClient` にリフレクション経由の同名メソッドを生やし、ModItemExplorer の `ModelGizmoManager` が描画・掴みの入口で参照する。ホスト不在・旧ホスト・取得失敗・ホストが駆動していないカメラ（standalone の Camera.main 等）はすべて「表示」に倒す（fail-open = 従来動作）。

**Tech Stack:** C# (COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1)、UnityInjector プラグイン、xunit (SceneEditor のみ)、MSBuild。

**Spec:** 本計画書がスペックを兼ねる（下記「設計の根拠」節）。

## Global Constraints

- **プラグイン間はリフレクション経由のみ**。ModItemExplorer から SceneEditor の型をコンパイル時参照してはならない（`CLAUDE.md` の作法。`MTEUtils/*Client.cs` に倣う）
- **`GizmoHost` の既存クラス名・メソッドシグネチャは変更禁止**（公開 API 契約。追加のみ可）。契約に使える型はプリミティブ + UnityEngine 型 + デリゲートのみ
- **ビルド確認は 2 構成とも行う**。COM3D2 = .NET 3.5（入力 5 個以上の `Func<>`/`Action<>` は使用不可）、COM3D25 = .NET 4.7.1
- MSBuild は Git Bash から直接叩く（`debug.bat` は実機へ DLL をコピーするので使わない）:
  - `export MSYS2_ARG_CONV_EXCL="*"`
  - 実体: `/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe`
  - ゲームパスは明示: `"/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
  - テストは COM3D2 → COM3D25 の順にビルドしてから `dotnet test`
- **MTEUtils は 2 リポジトリ共有の git submodule**（`https://github.com/kidonaru/COM3D2.MTEUtils.git`、現在の共通 HEAD は `d9c8901`）。submodule 側で 1 回コミットし、両親リポがそのコミットを指すよう別途コミットする
- **`deploy.bat` / `deploy.ps1` は実行しない**。submodule の `git push` もユーザー判断（本計画では実行しない）
- コメント・ログメッセージは日本語

## 設計の根拠（現状と変更点）

現状、外部ギズモは表示トグルを一切見ずに描かれている（`source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs:417-431`）:

```csharp
// 外部プラグインのギズモはツールバーの自前ギズモ表示 (drawEnabled) と
// 自前マテリアルの成否に依存せず描く
GizmoHost.DrawExternals(_camera);

if (_lineMaterial == null || !isDrawEnabled)
{
    return;
}
```

`isDrawEnabled` は `drawEnabled && MaidManipulateManager.instance.isBoneVisible`（同 `:164`）。`drawEnabled` は SceneView のみ `config.sceneViewShowGizmo` で設定され（`Manager/SceneViewManager.cs:125`）、GameView 側は既定 true。

**ホスト側では止めず、クライアント側（ModItemExplorer）が問い合わせて自分で止める**方針を採る。理由: ホスト側で `DrawExternals` をゲートすると、外部プラグインは「ホストが描いてくれないのに `IsViewActive()` は true なので standalone にも落ちない」という宙ぶらりんの状態になる。問い合わせ式なら standalone 経路でも同じ判断ができる。

**カメラ単位で答える**理由: `drawEnabled` は GizmoRenderer インスタンス（= ビュー）ごとの設定なので、グローバルな真偽値 1 個では SceneView と GameView を区別できない。

「SceneEditor のビューでないカメラ」の判定は **`GizmoRenderer` の有無だけでは足りない**。`GameViewManager.AttachGizmoRenderer`（`Manager/GameViewManager.cs:233`）はゲーム本体のメインカメラ（= `Camera.main`）へ `GizmoRenderer` を付け、ウィンドウモードを抜けるまで外さない。GameView をウィンドウモードのまま非表示にした状態では、`GizmoHostClient.isViewActive` は false（= 登録側は standalone へ落ちて `Camera.main` を直接使う）なのに `GizmoRenderer` は付いたまま、という組み合わせが起きる。そこで `GizmoRenderer.isHostActive()`（GameView 側は `IsGizmoHostActive` = `isShowWnd || isMaximized`、SceneView 側は `isShowWnd`）も併せて確認し、**ホストがそのカメラを実際に駆動している間だけ表示状態を返す**。それ以外は true（従来どおり表示）で、SceneEditor のウィンドウを閉じている間に ModItemExplorer のギズモが理由なく消える事故を防ぐ。

**ドラッグ中の可視性変化**も扱う。SceneEditor は `isBoneVisible` を false にした瞬間に自前ギズモのドラッグを打ち切る（`MaidManipulateManager.cs:253-259` の `EndGizmoDrag`）が、外部ギズモには届かない。ModItemExplorer 側は更新時にも可視性を見て、**非表示の間は対象を動かさない**。ただし掴み自体は解放せず、解放はマウスアップでホストから来る `endDrag` に任せる。

クライアントが自前で `EndDrag()` してしまうと、`SceneViewWindow.UpdatePointerInput` が同一フレーム内で `GizmoHost.IsExternalDragging(camera)` を再評価する（`SceneViewWindow.cs:551-552`）ため、マウスを押したままでもそのフレームからカメラ操作の抑止が外れて視点が跳ねる。ホスト側の `_dragEntry` も残ったままになる。更新を止めるだけならホストとクライアントのドラッグ状態が食い違わない。

---

### Task 1: SceneEditor に `GizmoHost.IsGizmoVisible(Camera)` を追加し、MTEUtils にブリッジを生やす

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\GizmoHost.cs`（`IsViewActive()` の直後、`:88` 付近に追加）
- Modify: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\MTEUtils\GizmoHostClient.cs`（submodule。`isViewActive` プロパティの後ろと `Initialize()` 内）
- Test: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin.Tests\GizmoHostVisibilityTests.cs`（新規）

**Interfaces:**
- Consumes: 既存の `GizmoRenderer.isDrawEnabled`（public, bool）、`SceneEditorPlugin.instance.isEnable`、`DockingClient.FindHostType(string)`（internal static）
- Produces:
  - `public static bool COM3D2.SceneEditor.Plugin.GizmoHost.IsGizmoVisible(UnityEngine.Camera camera)`
  - `public static bool COM3D2.MotionTimelineEditor.GizmoHostClient.IsGizmoVisible(UnityEngine.Camera camera)`
  - どちらも Task 2 の `ModelGizmoManager` が使う

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/GizmoHostVisibilityTests.cs` を新規作成:

```csharp
using System;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// GizmoHost の表示状態 API は外部プラグイン (ModItemExplorer) がリフレクションで
    /// 呼ぶ公開契約なので、シグネチャと不在時の既定値を機械検証する
    /// </summary>
    public class GizmoHostVisibilityTests
    {
        [Fact]
        public void GizmoHostがIsGizmoVisibleを公開している()
        {
            var method = typeof(GizmoHost).GetMethod(
                "IsGizmoVisible", BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);
            Assert.Equal(typeof(bool), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(Camera), parameters[0].ParameterType);
        }

        [Fact]
        public void プラグイン未起動時は表示扱いになる()
        {
            // テストホストでは SceneEditorPlugin.instance が null。
            // 表示状態が分からない場面では従来動作 (表示) に倒す
            Assert.True(GizmoHost.IsGizmoVisible(null));
        }

        [Fact]
        public void クライアントがホストのIsGizmoVisibleを解決できる()
        {
            // GizmoHostClient は同一アセンブリ内の GizmoHost を名前で探す。
            // 解決できていれば、ホスト側と同じ既定値 (表示) が返る
            Assert.True(GizmoHostClient.isAvailable);
            Assert.True(GizmoHostClient.IsGizmoVisible(null));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" && \
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter GizmoHostVisibilityTests
```

Expected: コンパイルエラー `CS0117: 'GizmoHost' に 'IsGizmoVisible' の定義がありません` および `'GizmoHostClient' に 'IsGizmoVisible' の定義がありません`

- [ ] **Step 3: `GizmoHost.IsGizmoVisible(Camera)` を実装する**

`source/COM3D2.SceneEditor.Plugin/GizmoHost.cs` の `IsViewActive()` 実装の直後（`Unregister` の手前）に追加:

```csharp
        /// <summary>
        /// 指定カメラのビューでギズモを表示してよいか。
        /// SceneView ツールバーのギズモ表示とメニューバーの「ボーン表示」の AND で、
        /// 外部ギズモも自前ギズモと同時に消えるようにするための問い合わせ口。
        /// プラグイン未起動時と、ホストが駆動していないカメラ (standalone の Camera.main や
        /// ウィンドウモードのまま非表示になった GameView のカメラ) では true を返し、
        /// 登録側の従来動作を変えない
        /// </summary>
        public static bool IsGizmoVisible(Camera camera)
        {
            var plugin = SceneEditorPlugin.instance;
            if (plugin == null || !plugin.isEnable)
            {
                return true;
            }

            if (camera == null)
            {
                return true;
            }

            var renderer = camera.GetComponent<GizmoRenderer>();
            if (renderer == null)
            {
                return true;
            }

            // GameView のカメラはウィンドウモードを抜けるまで GizmoRenderer が付いたままなので、
            // 付いているだけでは「ホストが駆動中のビュー」とは言えない
            if (renderer.isHostActive == null || !renderer.isHostActive())
            {
                return true;
            }

            return renderer.isDrawEnabled;
        }
```

- [ ] **Step 4: `GizmoHostClient.IsGizmoVisible(Camera)` を実装する**

`source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoHostClient.cs`（submodule）を 3 箇所変更する。

(a) フィールド宣言に追加（`private static bool _viewActiveFailed;` の直後）:

```csharp
        private static Func<Camera, bool> _isGizmoVisible;
        private static bool _gizmoVisibleFailed;
```

(b) `isViewActive` プロパティの直後にメソッドを追加:

```csharp
        /// <summary>
        /// 指定カメラのビューでホストがギズモ表示を許しているか。
        /// ホスト不在・旧ホスト・取得失敗時は true (従来どおり表示) に倒す。
        /// 毎フレーム呼ばれる経路なので、一度失敗したら以後は問い合わせずログも出さない
        /// </summary>
        public static bool IsGizmoVisible(Camera camera)
        {
            if (!isAvailable || _gizmoVisibleFailed || _isGizmoVisible == null)
            {
                return true;
            }

            try
            {
                return _isGizmoVisible(camera);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("GizmoHostClient: ギズモ表示状態の取得に失敗しました: " + e.Message);
                _gizmoVisibleFailed = true;
                return true;
            }
        }
```

(c) `Initialize()` 内、`IsViewActive` を解決している箇所の直後に追加:

```csharp
                // 表示状態の問い合わせも後発 API のため任意。
                // 旧ホストでは見つからず、その場合は常に表示扱いになる
                var isGizmoVisible = type.GetMethod("IsGizmoVisible", BindingFlags.Public | BindingFlags.Static);
                if (isGizmoVisible != null)
                {
                    _isGizmoVisible = (Func<Camera, bool>)Delegate.CreateDelegate(
                        typeof(Func<Camera, bool>), isGizmoVisible);
                }
```

さらに、同メソッド末尾の catch 節（シグネチャ不一致時に `_register = null; _unregister = null; _isViewActive = null;` としている箇所）に `_isGizmoVisible = null;` を追加する。

- [ ] **Step 5: テストが通ることを確認する（2 構成ビルド込み）**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" && \
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" && \
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

Expected: 両構成のビルド成功、テスト全件 PASS（`GizmoHostVisibilityTests` の 3 件を含む）

- [ ] **Step 6: submodule (MTEUtils) をコミットする**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git add GizmoHostClient.cs
git commit -m "$(cat <<'EOF'
feat(gizmo): ホストのギズモ表示状態を問い合わせられるようにする

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01FM5TTmLo86QBbzLUVyobSB
EOF
)"
git rev-parse HEAD
```

出力されたコミットハッシュを Task 2 で使うので控えること。

- [ ] **Step 7: SceneEditor 本体をコミットする**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/GizmoHost.cs \
        source/COM3D2.SceneEditor.Plugin/MTEUtils \
        source/COM3D2.SceneEditor.Plugin.Tests/GizmoHostVisibilityTests.cs \
        docs/superpowers/plans/2026-09-15-gizmo-visibility-host-api.md
git commit -m "$(cat <<'EOF'
feat(gizmo): 外部ギズモへ表示状態を公開する API を追加する

ボーン表示・ギズモ表示トグルの状態をカメラ単位で問い合わせられる
GizmoHost.IsGizmoVisible を追加し、ModItemExplorer 側が自前で
描画・掴みを止められるようにする。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01FM5TTmLo86QBbzLUVyobSB
EOF
)"
```

---

### Task 2: ModItemExplorer のギズモを表示状態へ追従させる

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils`（submodule ポインタを Task 1 Step 6 のコミットへ更新）
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\ModelPlacement\ModelGizmoManager.cs`（`TryBeginDrag` `:230` 付近 / `DrawAll` `:305` 付近 / `TryRegisterHost` の Register 呼び出し `:224` 付近 / `UpdateStandaloneInput` `:344` 付近）

**Interfaces:**
- Consumes: Task 1 で追加した `GizmoHostClient.IsGizmoVisible(Camera)`（ホスト不在時は true）
- Produces: なし（このタスクが最終利用者）

> ModItemExplorer にはテストプロジェクトが無いため、このタスクは自動テストを持たない。「非表示になる」分岐は Unity ランタイムでしか通らず xunit では検証できないので、**Step 6（実機確認）を通すまでコミット（Step 7）へ進まないこと**。ゲームが起動していない場合は Step 6 を保留し、その時点でユーザーへ報告して判断を仰ぐ（未検証のままコミットしない）。

- [ ] **Step 1: submodule を Task 1 のコミットへ進める**

`<HASH>` は Task 1 Step 6 で控えたハッシュ。

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git fetch /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils master
git merge --ff-only <HASH>
git log --oneline -1
```

Expected: HEAD が Task 1 のコミットになる（`GizmoHostClient.cs` に `IsGizmoVisible` が存在すること）

- [ ] **Step 2: 描画を表示状態でゲートする**

`ModelPlacement/ModelGizmoManager.cs` の `DrawAll` を次のように変更する（既存の `isPluginEnabled` ガードの直後に追加）:

```csharp
        private void DrawAll(Camera camera)
        {
            // ホストや描画フックはプラグインの有効状態を知らないため、ここで止める
            if (!isPluginEnabled)
            {
                return;
            }

            // SceneEditor のビューではホスト側の表示トグル (ボーン表示・ギズモ表示) に従う。
            // ホスト不在・SceneEditor のビューでないカメラでは true が返り従来どおり描く
            if (!GizmoHostClient.IsGizmoVisible(camera))
            {
                return;
            }

            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.Draw(camera);
            }
        }
```

- [ ] **Step 3: 掴みを表示状態でゲートする**

同ファイルの `TryBeginDrag` で、ウィンドウ上判定の直後に追加する:

```csharp
            // 自プラグインのウィンドウ上からの押下では掴まない。ホスト側は自分のウィンドウしか
            // 追跡していないため、この抑止は standalone / hosted の両経路で自前に行う
            if (WindowManager.instance.isMouseOverWindow)
            {
                return false;
            }

            // 見えていないギズモは掴ませない (描画側と同じ条件で判断する)
            if (!GizmoHostClient.IsGizmoVisible(camera))
            {
                return false;
            }
```

- [ ] **Step 4: ドラッグ中に非表示化されたら打ち切る**

SceneEditor は `isBoneVisible` を false にした瞬間に自前ギズモのドラッグを打ち切るが、外部ギズモには届かない。更新側でも可視性を見て、消えた瞬間に掴みを解放する。

(a) `TryRegisterHost` の `GizmoHostClient.Register` 呼び出しで `updateDrag` に渡す引数を、可視性を見る名前付きメソッドへ差し替える（他の引数と粒度を揃える）:

```csharp
            _hostHandle = GizmoHostClient.Register(
                "ModItemExplorer",
                TryBeginDrag,
                UpdateDragFromHost,
                EndDrag,
                () => isDragging,
                DrawAll);
```

`UpdateDrag` の手前に追加する:

```csharp
        /// <summary>
        /// ホスト経由のドラッグ更新。TransformGizmo は掴んだカメラを内部で保持し続けるため、
        /// 渡された camera は表示状態の判定にだけ使う。
        /// 非表示の間は動かさないが、掴み自体は解放しない。ここで自前に EndDrag すると
        /// ホスト側は同フレーム内でドラッグ終了とみなし、マウスを押したままでも
        /// カメラ操作の抑止が外れてしまう (SceneViewWindow.UpdatePointerInput)。
        /// 解放はマウスアップでホストから来る endDrag に任せる
        /// </summary>
        private void UpdateDragFromHost(Camera camera, Vector2 rtPoint)
        {
            if (!GizmoHostClient.IsGizmoVisible(camera))
            {
                return;
            }
            UpdateDrag(rtPoint);
        }
```

(b) `UpdateStandaloneInput` のドラッグ継続分岐にも同じ判定を入れる:

```csharp
            if (isDragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    EndDrag();
                }
                else if (GizmoHostClient.IsGizmoVisible(camera))
                {
                    // 旧バージョンの SceneEditor 環境では InputRemapper が GameView 内で
                    // RT 座標へ変換済みのため、Camera.main とのペアで正しく成立する。
                    // 非表示中は更新を止めるだけにする (ホスト経由の更新と同じ扱い)
                    UpdateDrag((Vector2)Input.mousePosition);
                }
                return;
            }
```

どちらも掴みは解放しない。クライアントが自前で `EndDrag()` すると、`SceneViewWindow.UpdatePointerInput` が同一フレーム内で `GizmoHost.IsExternalDragging(camera)` を再評価する（`SceneViewWindow.cs:551-552`）ため、マウスを押したままカメラ操作の抑止が外れて視点が跳ねる。

- [ ] **Step 5: 2 構成ともビルドする**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
"$MSBUILD" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" && \
"$MSBUILD" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

Expected: 両構成ともビルド成功

- [ ] **Step 6: 実機で挙動を確認する**

ゲームが起動していれば MCP `com3d25-devbridge` でホスト側の値を確認できる（プラグインのホットリロードまでは行わず、値の確認に留める）:

```csharp
// SceneView のカメラで表示状態が落ちることを確認する
var mm = COM3D2.SceneEditor.Plugin.MaidManipulateManager.instance;
var before = mm.isBoneVisible;
mm.isBoneVisible = false;
var hidden = COM3D2.SceneEditor.Plugin.GizmoHost.IsGizmoVisible(
    COM3D2.SceneEditor.Plugin.SceneViewManager.instance.gizmoRenderer.GetComponent<UnityEngine.Camera>());
mm.isBoneVisible = before;
hidden  // false になること
```

さらに、GameView をウィンドウモードのまま非表示にした状態（`isWindowMode = true`, `isShowWnd = false`, 非最大化）でも確認する。この state ではホストが駆動していない扱いになり、`Camera.main` に `GizmoRenderer` が付いたままでも表示（true）が返ることを見る:

```csharp
var gvm = COM3D2.SceneEditor.Plugin.GameViewManager.instance;
// ウィンドウモードのまま GameView ウィンドウを閉じた状態で実行すること
COM3D2.SceneEditor.Plugin.MaidManipulateManager.instance.isBoneVisible = false;
COM3D2.SceneEditor.Plugin.GizmoHost.IsGizmoVisible(UnityEngine.Camera.main)  // true になること
```

新ビルドの挙動まで見る場合は `CLAUDE.md` の Harmony ホットリロード手順に従う（アセンブリはバイト配列でロードする）。

ゲーム未起動などで確認できない場合は、**Step 7（コミット）へ進まずユーザーへ報告して判断を仰ぐ**。

- [ ] **Step 7: ModItemExplorer をコミットする**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils \
        source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoManager.cs
git commit -m "$(cat <<'EOF'
feat(gizmo): SceneEditor のギズモ表示トグルに追従する

ボーン表示・ギズモ表示が OFF のビューでは配置モデルのギズモも
描画・掴みを止める。ホスト不在時は従来どおり表示する。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01FM5TTmLo86QBbzLUVyobSB
EOF
)"
```

---

## 完了後の残作業（実行しない・ユーザー判断）

- MTEUtils submodule の `git push`（両親リポの submodule ポインタはローカルコミットを指しているため、公開前に push が必要）
- `deploy.bat` / `release.bat` は実行しない

## レビュー却下メモ

- コミットメッセージの `Co-Authored-By` は `Claude Sonnet 5` にすべき — 却下（誤検知）。本セッションに与えられた attribution 指示は `Claude Opus 5`。レビュアー（別モデルのサブエージェント）が自分側の指示を参照したもの。
