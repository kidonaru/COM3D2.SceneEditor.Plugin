# ギズモの参照カメラをカメラごとに切り替える実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: プロジェクトの CLAUDE.md に従い superpowers:executing-plans でタスク単位に実装すること（subagent-driven-development は使わない）。ステップはチェックボックス (`- [ ]`) で進捗管理する。

**Goal:** ゲーム側 `GizmoRender.RenderGizmos` が常に `Camera.main` を参照しているせいで SceneView に描かれるボーンギズモのサイズ・向きがゲーム画面基準になってしまう問題を、SceneView カメラの描画パスだけ `Camera.current` を参照するよう差し替えて解消する。

**Architecture:** 既存の `GizmoScaleFixPatch`（FoV 計算式の Transpiler 修正）を拡張する。`RenderGizmos` の IL 中の `call UnityEngine.Camera::get_main` をすべて自前の静的メソッド `GizmoScaleFixPatch.GetRenderCamera()` 呼び出しへ差し替える。参照カメラの解決は Prefix で 1 回だけ行って静的フィールドへ載せ、`GetRenderCamera()` はそれを返すだけにする（Prefix・本体・Postfix が必ず同じカメラを見ることを保証しつつ、解決コストを 1 回に抑える）。加えて Postfix で、掴み判定に漏れる private フィールド（`generalLens` / `uForward` / `rForward` / `fForward`）を Prefix で退避した値へ復元し、当たり判定は従来どおり `Camera.main` 基準に保つ。

**Tech Stack:** C# (.NET Framework 3.5 / 4.x の 2 構成), Unity, HarmonyX (BepInEx 同梱 0Harmony), UnityInjector プラグイン

**Spec:** 本ドキュメントの「背景と決定事項」節（独立した spec は起こさない）

---

## 背景と決定事項

### 実機で確認済みの事実（COM3D2.5 稼働中、devbridge の `eval_csharp` で計測）

- `OnRenderObject` は **カメラごとに 1 回ずつ** 呼ばれる。1 フレームで観測されたのは 6 パス:
  `SceneEditor SceneView Camera(fov=35, ortho=False)` → `CameraMain(fov=45, ortho=False)` →
  `MTEFrontCamera(fov=60, ortho=True)` → `TimelineTextCamera(fov=60)` →
  `SceneEditorClearCamera(fov=60)` → `Camera(fov=60, ortho=True)`
- `Camera.current` はその描画中のカメラを正しく返す（SceneView と CameraMain で別の fov が取れている）
- `GizmoRender.RenderGizmos` の IL 上、`ldc.r4 -2` は index 14、`call get_main` は index 16、`ldc.r4 50` は index 23（`W:\COM3D2_5\work\Assembly-CSharp\GizmoRender.cs:690-696` に対応）
- 移植元の単体プラグイン `COM3D2.RenderGizmosScaleFix.Plugin.dll` が **`W:\COM3D2\BepInEx\plugins\` にインストール済み**（COM3D2.5 側には未インストール）。同じ `RenderGizmos` を Transpiler で書き換えるため二重適用のリスクがある

### ゲーム側の該当コード（`W:\COM3D2_5\work\Assembly-CSharp\GizmoRender.cs`）

| 行 | 内容 |
|---|---|
| `:550` | `OnRenderObject` の末尾で `RenderGizmos()` を呼ぶ。掴み判定は **その手前** で行われる |
| `:692` | `Dir = Camera.main.transform.position - base.transform.position;` |
| `:694` | `generalLens = -2f * Mathf.Tan(0.5f * Camera.main.fieldOfView) * magnitude / 50f;`（本計画の前に `GizmoScaleFixPatch` で修正済み） |
| `:704` | `Vector3 rhs = base.transform.InverseTransformPoint(Camera.main.transform.position);`（回転リングの表側の基準） |
| `:715 / :726 / :737` | `rForward` / `uForward` / `fForward` に `vty` を代入（`rhs` 由来） |
| `:361 / :500 / :645` | 掴み判定で `Vector3.Dot(point, uForward) >= 0f` 等として **前回の RenderGizmos が残した値** を読む |
| `:273` ほか多数 | 掴み判定で `generalLens` を読む |

`Dir`（`:44`）は `RenderGizmos` 内で完結しており掴み判定へは漏れないため、退避・復元は不要。

### 決定事項（ユーザー確認済み）

1. **対象カメラは SceneView カメラとゲーム画面 (`Camera.main`) のみ。** それ以外（`MTEFrontCamera` / `TimelineTextCamera` / `SceneEditorClearCamera` / `Camera`）は従来どおり `Camera.main` 基準にフォールバックする。GameView ウィンドウは `CameraMain` をそのまま映しているため個別対応は不要。
2. **SceneView がオルソ投影のとき（`config.sceneViewOrthographic`）は `Camera.main` へフォールバックする。** `Mathf.Tan(fov/2)` とカメラ位置からの距離はオルソでは意味を持たないため、正しくスケールさせる対応は今回のスコープ外とする。
3. 新規設定は追加しない（`GizmoScaleFixPatch` と同じく常時有効）。

### 非目標

- SceneView 内でのボーンギズモの**ドラッグ操作**への対応。掴み判定は `Camera.main.ScreenPointToRay(Input.mousePosition)`（ゲーム画面のスクリーン座標）に固定されており、本計画では触らない。本計画は「SceneView での**見た目**を正す」ことに限定する。
- SceneView オルソ時のギズモスケール適正化。

## Global Constraints

- コメントとログメッセージは日本語で書く（CLAUDE.md）。
- ハードコーディングは絶対に必要な場合を除き避ける（CLAUDE.md）。
- COM3D2 (2.0) と COM3D2.5 の **2 構成でビルドが通ること**。対象フレームワークが異なるため必ず両方ビルドする。
- `deploy.bat` / `deploy.ps1` は実行しない。ビルド確認は MSBuild を直接叩く（`debug.bat` はゲーム停止中に実機へ反映してしまうため使わない）。
- パッチ失敗時はゲーム標準の挙動へ安全に落ちること。`GizmoScaleFixPatch` の既存方針（Transpiler は all-or-nothing、失敗時は `UnpatchSelf()` して `_harmony = null`）を崩さない。
- `RenderGizmos` は毎フレーム × カメラ数 × ギズモ数だけ走る。以下を守る:
  - Prefix/Postfix でのリフレクションは `AccessTools.FieldRefAccess` で作った型付きアクセサに限り、`GetValue`/`SetValue` は使わない
  - Prefix/Postfix は全体を try/catch で囲み、例外でゲーム側の描画を止めない（`SkirtHookDriftPatch.UpdatePrefix` / `UpdatePostfix` と同じ作法）
  - 参照カメラの解決は `RenderGizmos` 1 回につき 1 回だけ行う

## File Structure

- `source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs` — **変更**。単一ファイルで完結させる。
  - 既存: FoV 計算式の Transpiler 修正、Init / Unpatch。
  - 追加: 参照カメラの解決とキャッシュ、`call get_main` の差し替え、掴み判定用フィールドの Prefix/Postfix 退避・復元、旧プラグインとの二重パッチ検知。
  - 現状 190 行程度。追加後も 350 行を超えない見込みなので分割しない。パッチ対象が単一メソッド `RenderGizmos` である以上、責務は 1 つに収まっている。
- 他ファイルの変更なし。`SceneViewManager.instance.sceneCamera`（`Manager/SceneViewManager.cs:13,30`）を読むだけ。

## 検証方針

このパッチは IL 書き換えと Unity の描画コールバックに依存するため、`source/COM3D2.SceneEditor.Plugin.Tests` の単体テストでは検証できない（テスト側からゲームの `GizmoRender` の実 IL を用意できない）。代わりに **稼働中のゲームに対する devbridge (`mcp__com3d25-devbridge__eval_csharp`) での実測** を各タスクの検証手段とする。CLAUDE.md の「静的解析で結論が出ないときは必ず実機で裏取りする」に沿う。

既存の単体テストは本変更と無関係だが、リグレッション確認のため最終タスクで一度実行する。なお `EyesItemInspectorTests.ResolveRowKind` の 4 件は本変更以前から失敗している既知の失敗であり、本計画の対象外。

---

### Task 1: 参照カメラの解決とキャッシュを追加する

`RenderGizmos` から呼ばれる解決関数だけを先に足す。この時点では IL からは呼ばれないため挙動は変わらない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs`

**Interfaces:**
- Consumes: `SceneViewManager.instance.sceneCamera`（`Camera`、SceneView 未生成なら null）
- Produces:
  - `public static UnityEngine.Camera GizmoScaleFixPatch.GetRenderCamera()` — 引数なし。Prefix が載せたキャッシュを返す。Transpiler が差し替える `Camera.get_main`（`static Camera get_main()`）と**同じシグネチャ**である必要があるため、必ず `public static` かつ引数なし・戻り値 `Camera` にすること。
  - `private static UnityEngine.Camera GizmoScaleFixPatch.ResolveRenderCamera(out bool overridden)` — 描画中のカメラを解決する。`overridden` は「`Camera.main` 以外を返したか」。

- [x] **Step 1: 解決関数とキャッシュを追加する**

`GizmoScaleFixPatch` クラス内、`Unpatch()` の直後に以下を追加する:

```csharp
        /// <summary>
        /// Prefix が解決した参照カメラ。RenderGizmos は再入しないため 1 個で足りる。
        /// Prefix・本体・Postfix が必ず同じカメラを見ることを保証し、
        /// 解決コストも RenderGizmos 1 回につき 1 度で済ませる
        /// </summary>
        private static Camera _renderCamera = null;

        /// <summary>
        /// RenderGizmos が参照するカメラを返す。Transpiler が Camera.main の呼び出しを
        /// これに差し替えるため、Camera.get_main と同じ「引数なし・戻り値 Camera」で揃えている。
        /// Prefix が走らなかった場合の保険として Camera.main へ落とす
        /// </summary>
        public static Camera GetRenderCamera()
        {
            return _renderCamera != null ? _renderCamera : Camera.main;
        }

        /// <summary>
        /// 描画中のカメラから参照カメラを解決する。
        ///
        /// SceneView は専用カメラで描くため、Camera.main のままだとギズモの大きさも
        /// 回転リングの表裏もゲーム画面基準になってしまう。描画中のカメラが SceneView なら
        /// そちらを返して見た目を正す。
        ///
        /// SceneView 以外のカメラ (MTEFrontCamera 等のオーバーレイ) と、
        /// SceneView がオルソ投影のときは Camera.main へ落とす。
        /// オルソでは Mathf.Tan(fov/2) もカメラ位置からの距離も意味を持たないため
        /// </summary>
        private static Camera ResolveRenderCamera(out bool overridden)
        {
            var current = Camera.current;
            if (current != null && !current.orthographic && current == SceneViewManager.instance.sceneCamera)
            {
                overridden = true;
                return current;
            }

            overridden = false;
            return Camera.main;
        }
```

- [x] **Step 2: 2 構成でビルドが通ることを確認する**

作業ディレクトリ `source/COM3D2.SceneEditor.Plugin` に以下の bat を用意して実行する（Git Bash から MSBuild へ `/p:` を直接渡すと引数が壊れるため）。

```bat
@echo off
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=%1 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo
```

Run: 上記 bat を `COM3D25` と `COM3D2` の両方の引数で実行
Expected: 両方とも `COM3D2.SceneEditor.Plugin -> ...\COM3D2.SceneEditor.Plugin.dll` が出て error なし

- [x] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs
git commit -m "feat(gizmo): 描画中のカメラから参照カメラを解決する関数を追加する"
```

---

### Task 2: 掴み判定へ漏れる状態の退避・復元を追加する

参照カメラの差し替え（Task 3）を入れる**前に**、状態の退避・復元を先に入れる。順序を逆にすると、差し替えを入れた瞬間だけ掴み判定が壊れた中間状態になるため。この時点では `ResolveRenderCamera` が常に `overridden = false` を返すので退避・復元は空振りし、挙動は変わらない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs`

**Interfaces:**
- Consumes: `GizmoScaleFixPatch.ResolveRenderCamera(out bool)`（Task 1）
- Produces: `RenderGizmos` への Prefix/Postfix。外部から呼ぶ API は増やさない。

**なぜ必要か:** `OnRenderObject` は前半で掴み判定を行い、末尾（`GizmoRender.cs:550`）で `RenderGizmos()` を呼ぶ。つまり掴み判定が読む `generalLens`（`:273` ほか）と `uForward` / `rForward` / `fForward`（`:361 / :500 / :645`）は、**直前に走った RenderGizmos が残した値**である。参照カメラを差し替えると SceneView パスの値が残ってしまい、掴める範囲と回転リングの表裏判定がゲーム画面とズレる。SceneView カメラは depth = -100 で `CameraMain` より先に描かれるが、他のオーバーレイカメラは後に描かれるため「最後のパスが必ず main」とも言えない。よって差し替えたパスの前後で退避・復元する。

- [x] **Step 1: フィールドアクセサと Prefix/Postfix を追加する**

`GizmoScaleFixPatch` クラスに以下を追加する。フィールド定義は `_patched` の下、メソッドは `ResolveRenderCamera()` の下に置く。

```csharp
        /// <summary>
        /// 掴み判定へ漏れる private フィールドの名前。
        /// generalLens はハンドルの当たり範囲、*Forward は回転リングの表側判定に使われる
        /// </summary>
        private const string GENERAL_LENS_FIELD = "generalLens";
        private const string U_FORWARD_FIELD = "uForward";
        private const string R_FORWARD_FIELD = "rForward";
        private const string F_FORWARD_FIELD = "fForward";

        /// <summary>毎フレームの reflection を避けるため Init で一度だけ作る型付きアクセサ</summary>
        private static AccessTools.FieldRef<GizmoRender, float> _generalLensRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _uForwardRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _rForwardRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _fForwardRef = null;

        /// <summary>
        /// Prefix と Postfix の間で共有する退避。RenderGizmos はメインスレッドで再入しないため
        /// 1 組のバッファを使い回してフレーム毎の確保を避ける
        /// </summary>
        private static float _savedGeneralLens = 0f;
        private static Vector3 _savedUForward = Vector3.zero;
        private static Vector3 _savedRForward = Vector3.zero;
        private static Vector3 _savedFForward = Vector3.zero;

        /// <summary>
        /// 参照カメラを解決してキャッシュし、差し替えるパスなら掴み判定用の状態を退避する。
        /// __state で Postfix へ「復元が必要か」を伝える。
        /// 毎フレーム走るため例外は握り潰し、ゲーム側の描画を止めない
        /// </summary>
        private static void RenderGizmosPrefix(GizmoRender __instance, out bool __state)
        {
            __state = false;
            try
            {
                bool overridden;
                _renderCamera = ResolveRenderCamera(out overridden);
                if (!overridden)
                {
                    return;
                }

                _savedGeneralLens = _generalLensRef(__instance);
                _savedUForward = _uForwardRef(__instance);
                _savedRForward = _rForwardRef(__instance);
                _savedFForward = _fForwardRef(__instance);
                __state = true;
            }
            catch (Exception e)
            {
                // 退避に失敗したら復元もしない。ギズモは Camera.main 基準で描かれるだけで済む
                _renderCamera = null;
                __state = false;
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 描画は差し替えたカメラ基準で終わっているので、掴み判定が読む状態だけ
        /// Camera.main 基準の値へ戻す
        /// </summary>
        private static void RenderGizmosPostfix(GizmoRender __instance, bool __state)
        {
            _renderCamera = null;

            if (!__state)
            {
                return;
            }

            try
            {
                _generalLensRef(__instance) = _savedGeneralLens;
                _uForwardRef(__instance) = _savedUForward;
                _rForwardRef(__instance) = _savedRForward;
                _fForwardRef(__instance) = _savedFForward;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

- [x] **Step 2: Init でアクセサを作り、Prefix/Postfix を登録する**

`Init()` の `var transpiler = ...` の直前に、フィールドの存在確認とアクセサ生成を追加する:

```csharp
                foreach (var name in new[] { GENERAL_LENS_FIELD, U_FORWARD_FIELD, R_FORWARD_FIELD, F_FORWARD_FIELD })
                {
                    if (AccessTools.Field(typeof(GizmoRender), name) == null)
                    {
                        throw new Exception(name + " が見つかりません");
                    }
                }
                _generalLensRef = AccessTools.FieldRefAccess<GizmoRender, float>(GENERAL_LENS_FIELD);
                _uForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(U_FORWARD_FIELD);
                _rForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(R_FORWARD_FIELD);
                _fForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(F_FORWARD_FIELD);
```

`_harmony.Patch(...)` の呼び出しを Prefix/Postfix 込みへ差し替える:

```csharp
                var prefix = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosPrefix));
                var postfix = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosPostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".GizmoScaleFix");
                _harmony.Patch(
                    original,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix),
                    transpiler: new HarmonyMethod(transpiler));
```

`Unpatch()` の `_harmony = null;` の直前に、アクセサも未初期化へ戻す行を足す（`SkirtHookDriftPatch.Init` の失敗時と同じ作法）:

```csharp
            _generalLensRef = null;
            _uForwardRef = null;
            _rForwardRef = null;
            _fForwardRef = null;
            _renderCamera = null;
```

- [x] **Step 3: 2 構成でビルドが通ることを確認する**

Run: Task 1 Step 2 と同じ bat を `COM3D25` / `COM3D2` で実行
Expected: 両方成功

- [x] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs
git commit -m "feat(gizmo): 参照カメラ差し替え時に掴み判定用の状態を退避・復元する"
```

---

### Task 3: Transpiler で `Camera.main` を `GetRenderCamera()` へ差し替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs`

**Interfaces:**
- Consumes: `GizmoScaleFixPatch.GetRenderCamera()`（Task 1）、`RenderGizmosPrefix` / `RenderGizmosPostfix`（Task 2）
- Produces: なし（既存 `RenderGizmosTranspiler` の内部変更）

**方針:** `RenderGizmos` 内の `call UnityEngine.Camera::get_main` を**すべて** `call GizmoScaleFixPatch::GetRenderCamera` に差し替える。`RenderGizmos` 内の `Camera.main` 参照（`GizmoRender.cs:692 / :694 / :704`、実 IL で 3 箇所）はいずれも「今描いているカメラ」を意味しているため、全数置換が意図そのものになる。件数を期待値と突き合わせる方式は取らない（2.0 と 2.5 で出現数が違いうるうえ、部分適用の危険が無いため）。代わりに置換件数を Init の成功ログへ出し、ゲーム更新で数が変わったことに後から気付けるようにする。

**注意:** 既存の `FindFovPattern` は `codes[i + 2]` が `Call` かつオペランドに `get_main` を含むことを見ている。差し替えを FoV パターン検出より**後**に行わないとパターンが一致しなくなる。

- [x] **Step 1: 定数と置換件数フィールドを用意する**

`GizmoScaleFixPatch` のフィールド群に追加する:

```csharp
        /// <summary>差し替え対象。Camera.main のゲッター名</summary>
        private const string CAMERA_MAIN_GETTER = "get_main";

        /// <summary>Camera.main を差し替えた件数。ゲーム更新で数が変わったことに気付くためログへ出す</summary>
        private static int _replacedCameraMainCount = 0;
```

- [x] **Step 2: `RenderGizmosTranspiler` に差し替え処理を足す**

`_patched = true;` の直前（除数と FoV の書き換えが済んだ後）に以下を挿入する:

```csharp
            // SceneView の描画パスでは参照カメラを差し替える。
            // FoV パターンの検出が get_main の並びを見ているため、検出が済んでから置き換える
            var getRenderCamera = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(GetRenderCamera));
            _replacedCameraMainCount = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                if (IsCall(codes[i], OpCodes.Call, CAMERA_MAIN_GETTER))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, getRenderCamera);
                    _replacedCameraMainCount++;
                }
            }
```

- [x] **Step 3: 成功ログに置換件数を出す**

`Init()` の `MTEUtils.Log("GizmoRender.RenderGizmos のフックに成功しました");` を差し替える:

```csharp
                MTEUtils.Log("GizmoRender.RenderGizmos のフックに成功しました (Camera.main の差し替え: "
                    + _replacedCameraMainCount + " 箇所)");
```

- [x] **Step 4: 2 構成でビルドが通ることを確認する**

Run: Task 1 Step 2 と同じ bat を `COM3D25` / `COM3D2` で実行
Expected: 両方成功

- [x] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs
git commit -m "feat(gizmo): SceneView の描画パスで参照カメラを差し替える"
```

---

### Task 4: 旧プラグインとの二重パッチを検知する

`COM3D2.RenderGizmosScaleFix.Plugin.dll` は `W:\COM3D2\BepInEx\plugins\` にインストール済みで、同じ `RenderGizmos` を Transpiler で書き換える。両方が効くと Deg2Rad の乗算や除数の置換が二重に適用され、元の式とも修正後の式とも違う値になる。しかも「ギズモのサイズがなんとなく変」という気付きにくい壊れ方をする。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs`

**Interfaces:**
- Consumes: `HarmonyLib.Harmony.GetPatchInfo(MethodBase)`
- Produces: なし（`Init()` の内部処理）

**方針:** 自分がパッチする前に `RenderGizmos` の既存 Transpiler の owner を調べ、旧プラグインが居たら **自分はパッチせず警告を出す**。旧プラグイン側の修正だけが残るので FoV 修正自体は効いたまま（SceneView のカメラ別対応は効かない）で、二重適用による破綻は避けられる。ロード順によっては旧プラグインが後からパッチする可能性もあるが、その場合は検知できない。ログで存在を知らせて DLL の削除を促すのが目的であり、完全な防御は狙わない。

- [x] **Step 1: 検知処理を追加する**

`GizmoScaleFixPatch` のフィールド群に追加する:

```csharp
        /// <summary>
        /// 移植元の単体プラグイン。同じ RenderGizmos を Transpiler で書き換えるため併存できない。
        /// Harmony の owner id は BepInPlugin の GUID と同じ
        /// </summary>
        private const string LEGACY_PLUGIN_ID = "COM3D2.RenderGizmosScaleFix.Plugin";
```

`Init()` の `var transpiler = ...` の直前に追加する:

```csharp
                if (HasLegacyPluginPatch(original))
                {
                    throw new Exception(LEGACY_PLUGIN_ID
                        + " が同じメソッドをパッチしています。二重適用を避けるため本体側の修正を見送ります。"
                        + "BepInEx\\plugins から同 DLL を削除してください");
                }
```

`Unpatch()` の下にヘルパーを追加する:

```csharp
        /// <summary>
        /// 旧プラグインが同じメソッドを既にパッチしているか。
        /// ロード順によっては後からパッチされて検知できないが、
        /// 併存に気付けるようにするのが目的なので完全な防御は狙わない
        /// </summary>
        private static bool HasLegacyPluginPatch(MethodBase original)
        {
            var info = Harmony.GetPatchInfo(original);
            if (info == null)
            {
                return false;
            }

            foreach (var owner in info.Owners)
            {
                if (owner == LEGACY_PLUGIN_ID)
                {
                    return true;
                }
            }
            return false;
        }
```

ファイル先頭の using に `using System.Reflection;` を追加する（`MethodBase` のため）。

- [x] **Step 2: 2 構成でビルドが通ることを確認する**

Run: Task 1 Step 2 と同じ bat を `COM3D25` / `COM3D2` で実行
Expected: 両方成功

- [x] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/GizmoScaleFixPatch.cs
git commit -m "feat(gizmo): 旧 RenderGizmosScaleFix との二重パッチを検知して見送る"
```

---

### Task 5: 実機で検証する

ホットリロードでゲームを再起動せずに検証する。CLAUDE.md の「Harmony プラグインのホットリロード」手順に従う。

**Files:**
- 変更なし（検証のみ）

**前提:** COM3D2.5 が起動していること（`mcp__com3d25-devbridge__ping` で `mainThreadAlive: true`）。起動していなければゲームを起動してもらう。COM3D2.5 側には旧プラグインが入っていないため、Task 4 の検知には引っかからない。

- [x] **Step 1: 差し替えが実際に効いているか、IL レベルで確認する**

devbridge の `eval_csharp` で、パッチ後の `RenderGizmos` に `Camera.get_main` が残っていないことを確認する:

```csharp
var mi = HarmonyLib.AccessTools.Method(typeof(GizmoRender), "RenderGizmos");
var list = HarmonyLib.PatchProcessor.GetCurrentInstructions(mi);
int mainCount = 0, renderCount = 0;
foreach (var c in list) {
    if (c.operand == null) continue;
    var s = c.operand.ToString();
    if (s.Contains("get_main")) mainCount++;
    if (s.Contains("GetRenderCamera")) renderCount++;
}
"get_main=" + mainCount + " GetRenderCamera=" + renderCount```

Expected: `get_main=0 GetRenderCamera=3`（Init のログに出た置換件数と一致すること）

- [x] **Step 2: SceneView と CameraMain が別カメラとして解決されることを確認する**

SceneView ウィンドウを開き、ボーンギズモを表示した状態で、SceneView の FoV とゲーム画面の FoV を意図的に変える。そのうえで両者が別カメラとして取れることを確認する:

```csharp
var sv = COM3D2.SceneEditor.Plugin.SceneViewManager.instance.sceneCamera;
"sceneCamera=" + (sv == null ? "null" : sv.name + " fov=" + sv.fieldOfView + " ortho=" + sv.orthographic)
  + " / main=" + UnityEngine.Camera.main.name + " fov=" + UnityEngine.Camera.main.fieldOfView
```

Expected: `sceneCamera` が非 null で、`main` と別の fov を持つ

- [x] **Step 3: 見た目を目視確認する**

以下を確認する（`mcp__com3d25-devbridge__screenshot` と SceneView ウィンドウの目視）:

1. SceneView 内のボーンギズモが、SceneView のカメラ距離・FoV に応じた自然な大きさで描かれる
2. SceneView の FoV / 距離を変えるとギズモの見かけの大きさが追従する
3. ゲーム画面側のギズモの大きさが本変更の前後で変わっていない
4. `MTEFrontCamera` などのオーバーレイに異常なギズモが出ていない

- [x] **Step 4: 掴み判定が壊れていないことを確認する**

ゲーム画面でボーンギズモの軸・面・回転リングをドラッグし、以下を確認する:

1. ハンドルを掴める範囲が本変更の前と変わらない
2. 回転リングの手前半分だけが掴める（表裏判定が反転していない）
3. SceneView ウィンドウを開いている状態と閉じている状態で、掴み心地が変わらない
4. ドラッグ中に描かれる角度表示の円弧（`GizmoRender.DrawCam`）が SceneView 側でも破綻していない。
   `DrawCam` は `RenderGizmos()` より手前で `generalLens` を読むため Postfix の復元後の値（= main 基準）を使う。
   これは本変更前からの挙動だが、SceneView 側で極端な大きさになっていないか一度見ておく

3 が最重要。SceneView を開くと参照カメラの差し替えパスが増えるため、Task 2 の退避・復元が効いていなければここで差が出る。

- [x] **Step 5: SceneView をオルソに切り替えてフォールバックを確認する**

SceneView のツールバーで直交投影に切り替え、SceneView 内のギズモがゲーム画面基準のサイズ（＝本変更前と同じ挙動）に戻ることを確認する。ギズモが消える・極端に巨大化するといった破綻が無いこと。

---

### Task 6: 仕上げ（CHANGELOG・テスト・レビュー）

**Files:**
- Modify: `CHANGELOG.md`

- [x] **Step 1: CHANGELOG に追記する**

`## 未リリース` の、FoV ギズモ修正の項目を以下へ差し替える:

```markdown
- 視野角(FoV)を変更するとギズモのサイズが破綻する問題を修正しました
  - RenderGizmosScaleFix 相当の修正を本体に取り込んだため、同プラグインは不要になります
    (併存すると二重に適用されるため、`BepInEx\plugins` から削除してください)
  - SceneView のギズモが SceneView カメラの視野角・距離に応じたサイズで描かれるようになりました
    (SceneView が直交投影のときはゲーム画面基準のままです)
```

- [x] **Step 2: 単体テストを実行する**

Run: `test-runner` サブエージェントで `source/COM3D2.SceneEditor.Plugin.Tests` を実行
Expected: 389 成功 / 4 失敗。失敗は `EyesItemInspectorTests.ResolveRowKind` の既知の 4 件のみで、新規の失敗が無いこと

- [x] **Step 3: コードレビュー**

`code-review` スキルで機能面・可読性の 2 エージェントを並列起動し、指摘を反映する。

- [x] **Step 4: コミット**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): SceneView のギズモが SceneView カメラ基準になったことを追記する"
```

---

## レビュー却下メモ

- **Task 3 の `get_main` 一括置換に期待件数（3）との一致チェックを入れるべき** — 却下。`RenderGizmos` 内の `Camera.main` 参照はすべて「今描いているカメラ」の意味であり、全数置換が意図そのもの。部分適用による破綻が起こりえないため all-or-nothing のガードは不要。件数を定数で持つと 2.0 と 2.5 で出現数が違った場合に誤って無効化される副作用のほうが大きい。代わりに置換件数を Init の成功ログへ出し、後から気付ける形にした（Task 3 Step 3）。
