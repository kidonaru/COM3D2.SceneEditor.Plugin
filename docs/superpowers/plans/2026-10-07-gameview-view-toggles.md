# 表示トグルの整理（SceneView / GameView / タイムライン操作） Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SceneView / GameView / タイムライン操作ウィンドウの表示トグルを 背景 / メイド / モデル / エフェクト / ギズモ に揃える。GameView とタイムライン操作ウィンドウは同じ状態を共有してメインカメラだけに効かせ、Config に保存する。SceneView は自分のトグルだけで決まる。

**Architecture:**
- **カメラごとの非表示**: 既存の `ViewCullingFilter`（カメラの OnPreCull で Renderer を止め OnPostRender で戻す）に、ライブ演出 3 種のフラグを足す。PNG のフラグは消す。
- **ポストエフェクト**: 実体は PostEffects.Plugin がメインカメラに付けていて、SceneView カメラには掛かっていない。PostEffects.Plugin に「一時停止」を足し、SceneEditor から `PostEffectsClient` 経由（リフレクション）で呼ぶ。
- **エフェクトの状態解決**: 「エフェクト表示」とタイムラインのカレントレイヤーの種類から、何を隠すかを決める純粋関数 `ViewEffectVisibility.Resolve` を作り、`GameViewManager.LateUpdate` で毎フレーム解決して差分だけ当てる。
- **ボーン表示**: `MaidManipulateManager.isBoneVisible` を Config（`gameViewShowGizmo`）へ移し、GameView 専用にする。SceneView 側の骨格線・ボーンギズモ・白丸は `config.sceneViewShowGizmo` で判定する。
- **タイムライン**: 操作ウィンドウのトグルを GameView の状態へ付け替え、全カメラに効いていた旧実装（背景の SetActive・モデルの SetActive・メイドの退避・ポスプロ同期）と地面連動を削除する。

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Unity 5.6 系 API、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）

**Spec:** `docs/superpowers/specs/2026-10-07-gameview-view-toggles-design.md`

## Global Constraints

- **並び順**: 3 ウィンドウとも表示トグルは 背景 → メイド → モデル → エフェクト → ギズモ。SceneView は後ろに 平行投影 / 追従 / 軸空間。GameView は前に 撮影 / 比率。タイムライン操作は前に 編集モード / 自動登録、後ろに カメラ同期 / 視野角固定 / フォーカス固定。
- **アイコン**: `ToolbarIcons.Kind.Bg` / `Maid` / `Model` / `PostEffect` / `Gizmo`。`Kind.Png` は残す（素材とセット）。
- **Config の新項目**（すべて既定 `true`）: `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel` / `gameViewShowEffect` / `gameViewShowGizmo` / `sceneViewShowEffect`。spec の `boneVisible` は名前を `gameViewShowGizmo` に揃える（Task 8 で spec も直す）。
- **効く範囲**: GameView の状態はメインカメラだけ（通常表示・スクリーンショット・連番出力）。サムネイルはフィルタを無効にして撮るので背景・メイド・モデル・ライブ演出は写る。ポストエフェクトの一時停止はサムネイルにも効く（許容）。
- **タイムライン XML**: `IsBackgroundVisible` / `IsGroundLinkedToBackground` はフィールドごと削除。version は上げない。
- **プラグイン間連携**: PostEffects.Plugin はリフレクションで呼ぶ（`MTEUtils/PostEffectsClient.cs` の任意メソッドの作法）。旧版ホストではポストエフェクト部分だけ何もしない。
- **MTEUtils**: 共有 submodule。submodule 内で commit → SceneEditor / PostEffects.Plugin の親で参照を進める。push はしない。
- **言語**: コードのコメント・ログは日本語。
- **ビルド**: 両構成（COM3D2 → COM3D25 の順）。`deploy.bat` / `debug.bat` は実行しない。

## 決定事項

| 論点 | 決定 | 理由 |
|---|---|---|
| PostEffects の止め方 | PostEffects.Plugin の `PostEffectManager` に `suspended`（保存しない）を足し、適用条件を `isApplying => effectsEnabled && !suspended` にする | ユーザー操作の「有効」（`effectsEnabled`）と取り合わない。タイムライン・設定の値に触れない |
| Hub 系の穴 | `PostEffectHub.OnPreRender` の冒頭で buffer を Clear した後、`!PostEffectManager.instance.isApplying` なら `depthTextureMode` を戻して return | パラフィン / 距離フォグ / リムライトの Restore は Hub を止めないため、一時停止しても描かれ続ける。`effectsEnabled` OFF の既存の穴も同時に塞ぐ |
| 一時停止の送り方 | `GameViewManager` が最後に送った値を持ち、変化したときだけ `PostEffectsClient.SetSuspended` を呼ぶ。送れなかった（未接続・旧版）ときは記録しない | 毎フレームのリフレクション呼び出しを避けつつ、後から接続されたホストにも反映する |
| 編集中レイヤーの例外 | カレントレイヤーの型で種類を判定（`PostEffectTimelineLayer` / `StageLightTimelineLayer` / `StageLaserTimelineLayer` / `PsylliumTimelineLayer`）。その種類だけ隠さない | 旧ポスプロ同期の「カレントは常に反映」を引き継ぐ |
| ライブ演出フラグの粒度 | `hideStageLight` / `hideStageLaser` / `hidePsyllium` の 3 つ | 例外を種類単位で効かせるため |
| ボーン表示の保存先 | `Config.gameViewShowGizmo` を `isBoneVisible` の実体にする（フィールドを持たない） | メニューバー / BoneEditWindow / GameView / タイムライン操作が同じ状態を読む |
| SceneView のボーン系の条件 | 骨格線・ボーンギズモ・指の白丸 = 編集モード中 ∧ `sceneViewShowGizmo` ∧ SceneView 表示中 ∧ ブレンド調整中でない。体の白丸 = `sceneViewShowGizmo` ∧ SceneView 表示中 ∧ ブレンド調整中でない（編集モード外も出す今の挙動を保つ） | spec。ボーン表示 OFF でも SceneView では操作できる |
| ボーン系の実体を作る条件 | GameView 側（`isBoneEditing`）か SceneView 側のどちらかが出すとき（`isAnyBoneEditing`）。GameView の描画・掴みは従来どおり `isBoneEditing` / `isGameViewDragPointVisible` で絞る | 実体（ギズモコンポーネント・白丸のコライダ）は両ビュー共有のため |
| 指の白丸の GameView 掴み | `MaidFingerDragPoint.OnMouseDown` に `isGameViewDragPointVisible` の判定を足す | 今は `isBoneEditing` のときしか実体が無いので判定が無かった。SceneView だけ ON でも実体ができるようになる |
| 旧タイムライン Config の `isPostEffectSync` | フィールドを削除。XmlSerializer は未知要素を読み飛ばす | 移行処理は不要 |
| `EasySettingType` の `メイド表示` / `背景表示` / `ポスプロ同期` | 触らない | UI から参照されていない MTE の名残で、今回の挙動に関係しない |

## Review Focus

1. **旧タイムライン XML の読込**: `IsBackgroundVisible=false` / `IsGroundLinkedToBackground=true` を含む XML がエラーなく読めること（Task 7 のテスト）。
2. **旧 Config の読込**: 新項目の無い Config で表示トグルがすべて ON になること（Task 3 のテスト）。
3. **エフェクト OFF と編集中レイヤー**: OFF のままポストエフェクト / 各ライブ演出レイヤーをカレントにすると、その種類だけ出て他は隠れること（Task 3 のテスト）。
4. **ON のときは何も隠さない**: エフェクト ON ではカレントに関係なく一時停止もフィルタも立たないこと（Task 3 のテスト）。
5. **ボーン表示 OFF + SceneView**: GameView から骨が消えても SceneView では骨格線・白丸が出て掴めること（Task 6 の実機確認。Unity 依存のためゲーム外テストは書けない）。

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin\source\COM3D25.PostEffects.Plugin\Manager\PostEffectManager.cs` | `suspended` / `isApplying` | 変更 |
| `...\COM3D25.PostEffects.Plugin\Effects\PostEffectHub.cs` | 適用停止中は描かない | 変更 |
| `...\COM3D25.PostEffects.Plugin\TimelineBridge.cs` | `SetSuspended` / `IsSuspended` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectsClient.cs`（submodule） | `SetSuspended` / `IsSuspended` のラッパー | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/ViewEffectVisibility.cs` | エフェクトの状態解決（純粋関数） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Config.cs` | 新項目 6 つ | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/ViewCullingFilter.cs` | ライブ演出 3 フラグ追加、PNG 削除 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` | GameView の PNG 非表示処理を削除 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs` | Config 連動のトグル、エフェクトの解決と適用 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs` | ツールバー 5 項目 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs` / `SceneViewWindow.cs` | SceneView のエフェクト、ボーン系の判定 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` / `BoneEditManager.cs` / `MaidDragPointRing.cs` / `MaidFingerDragPoint.cs` / `Manager/BoneLineRenderer.cs` | ボーン表示の切り離し | 変更 |
| `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs` | トグルの付け替え・並び替え・ポスプロ同期削除 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/...`（Config / TimelineManager / TimelineData / TimelineXml / Hack/SceneEditorHack / Manager/StudioModelManager / TimelineLayer の BG・BGColor・PostEffect・StageLight・StageLaser・Psyllium・Base） | 旧実装の削除 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` | 地面連動の削除 | 変更 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ViewEffectVisibilityTests.cs` / `ConfigViewToggleTests.cs` / `TimelineXmlRemovedFieldsTests.cs` | テスト | 新規 |
| `docs-site/guide/windows.md` / `scene-view.md` / `maid-editing.md` / `getting-started.md`、`docs-site/timeline/control.md` / `settings.md` / `layers-background.md`、`W:\COM3D2_5\work\CLAUDE.md`、spec | ドキュメント | 変更 |

### ビルド + テストのコマンド（全タスク共通）

Git Bash で実行する。`<Filter>` はタスクごとに指定する（全件のときは `--filter` ごと外す）。

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "<Filter>"
```

PostEffects.Plugin のビルド（Task 1・2）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
"$MSB" COM3D25.PostEffects.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D25.PostEffects.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

---

### Task 1: PostEffects.Plugin に一時停止を足す

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin\source\COM3D25.PostEffects.Plugin\Manager\PostEffectManager.cs:12-14, 270`
- Modify: `...\Effects\PostEffectHub.cs`（`OnPreRender`、181 行付近）
- Modify: `...\TimelineBridge.cs`（`LoadStartupPreset` の後）

**Interfaces:**
- Produces: `TimelineBridge.SetSuspended(bool suspended)`（static void）、`TimelineBridge.IsSuspended()`（static bool）。`PostEffectManager.suspended`（bool）、`PostEffectManager.isApplying`（bool）

- [ ] **Step 1: `PostEffectManager` にフラグを足す**

`effectsEnabled` の宣言の直後に追加する。

```csharp
        // 外部 (SceneEditor の GameView の「エフェクト」トグル) からの一時停止。
        // ユーザー操作の effectsEnabled と取り合わないよう別に持つ。保存しない
        public bool suspended = false;

        /// <summary>エフェクトを適用してよいか。Hub 系の描画判定もこれに従う</summary>
        public bool isApplying => effectsEnabled && !suspended;
```

`LateUpdate` の `if (effectsEnabled && controller.effectEnabled)` を `if (isApplying && controller.effectEnabled)` に置き換える。

- [ ] **Step 2: `PostEffectHub.OnPreRender` で停止中は描かない**

buffer を Clear するループと `_depthModeCaptured` の取得の後、`bool anyActive = false;` の前に追加する。

```csharp
			// パラフィン・距離フォグ・リムライトの Restore は共有の Hub を止めないため、
			// 全体の無効化・一時停止中はここで描画を打ち切る (Clear 済みなので前フレームの描画も残らない)
			if (!PostEffectManager.instance.isApplying)
			{
				context.camera.depthTextureMode = _capturedDepthMode;
				return;
			}
```

インデントはファイルに合わせてタブにする。

同じく `OnPreCull` の先頭（`foreach` の前）にも追加する。CharacterMask の描画は `Camera.Render` を伴い、止めている間は無駄なコストになるため。

```csharp
			if (!PostEffectManager.instance.isApplying)
			{
				return;
			}
```

- [ ] **Step 3: `TimelineBridge` に公開メソッドを足す**

`LoadStartupPreset` の後に追加する。

```csharp
        /// <summary>
        /// エフェクトの適用を一時停止する。SceneEditor の GameView で「エフェクト」を OFF にしたときに呼ばれる。
        /// 各エフェクトの設定値は変えない
        /// </summary>
        public static void SetSuspended(bool suspended)
        {
            PostEffectManager.instance.suspended = suspended;
        }

        public static bool IsSuspended()
        {
            return PostEffectManager.instance.suspended;
        }
```

- [ ] **Step 4: 両構成でビルドする**

上の PostEffects.Plugin のビルドコマンドを実行する。Expected: 両構成とも `0 エラー`。

- [ ] **Step 5: Commit（PostEffects.Plugin リポジトリ）**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/Manager/PostEffectManager.cs source/COM3D25.PostEffects.Plugin/Effects/PostEffectHub.cs source/COM3D25.PostEffects.Plugin/TimelineBridge.cs
git commit -m "feat(bridge): 外部からエフェクトの適用を一時停止できるようにする"
```

コミットメッセージ本文に「`effectsEnabled` OFF でもパラフィン・距離フォグ・リムライトが描かれ続けていた不具合も直す」を書く。

---

### Task 2: `PostEffectsClient` に一時停止のラッパーを足す（MTEUtils submodule）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectsClient.cs`（58-61 行の任意メソッドの宣言、166-167 行の解決、438-445 行の `LoadStartupPreset` の後）

**Interfaces:**
- Consumes: Task 1 の `TimelineBridge.SetSuspended(bool)` / `IsSuspended()`
- Produces: `PostEffectsClient.SetSuspended(bool suspended)` → `bool`（送れたら true。未接続・旧版ホスト・失敗は false）

- [ ] **Step 1: 宣言を足す**

`private static Func<bool> _loadStartupPreset;` の直後に追加する。

```csharp
        private static Action<bool> _setSuspended;
```

- [ ] **Step 2: 解決を足す**

`_loadStartupPreset = CreateFuncBool(type, "LoadStartupPreset");` の直後に追加する。

```csharp
                _setSuspended = CreateActionBool(type, "SetSuspended");
```

- [ ] **Step 3: 公開メソッドを足す**

`LoadStartupPreset()` の後に追加する。

```csharp
        /// <summary>
        /// ホストのエフェクト適用を一時停止 / 再開する。送れなかった場合 (旧版ホスト・未接続・失敗) は false
        /// </summary>
        public static bool SetSuspended(bool suspended)
        {
            if (!isAvailable || _setSuspended == null)
            {
                return false;
            }
            try { _setSuspended(suspended); return true; }
            catch (Exception e) { LogHostError("SetSuspended", e); return false; }
        }
```

`IsSuspended` は使う側が無いので作らない（YAGNI）。

- [ ] **Step 4: SceneEditor を両構成でビルドする**

共通のビルドコマンドを `dotnet test` 抜きで実行する（`&& dotnet test ...` の行を外す）。Expected: 両構成 `0 エラー`。

- [ ] **Step 5: submodule で commit し、両リポの参照を進める**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git add PostEffectsClient.cs
git commit -m "feat(post-effects-client): エフェクトの一時停止を呼べるようにする"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(mteutils): PostEffectsClient の一時停止を取り込む"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils
git fetch /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils HEAD
git merge --ff-only FETCH_HEAD
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils
git commit -m "chore(mteutils): PostEffectsClient の一時停止を取り込む"
```

PostEffects.Plugin 側の submodule パスが違う場合は `git submodule status` で確かめてから進める。ff-only が失敗したら止めてユーザーに報告する。PostEffects.Plugin をもう一度両構成でビルドする。

---

### Task 3: エフェクトの状態解決と Config 項目（テスト付き）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/ViewEffectVisibility.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:74`（`gameViewCustomHeight` の後）、`:91`（`sceneViewShowGizmo` の後）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ViewEffectVisibilityTests.cs`、`ConfigViewToggleTests.cs`

**Interfaces:**
- Produces:
  - `enum EffectLayerKind { None, PostEffect, StageLight, StageLaser, Psyllium }`
  - `struct ViewEffectState { bool suspendPostEffect; bool hideStageLight; bool hideStageLaser; bool hidePsyllium; }`（`Equals` 用に `==` を持たせる代わりに、比較は `ViewEffectVisibility.SameAs(a, b)` を使う）
  - `static ViewEffectState ViewEffectVisibility.Resolve(bool showEffect, EffectLayerKind currentLayer)`
  - `static ViewEffectState ViewEffectVisibility.HideAll(bool hide)`（SceneView 用。ポストエフェクトは触らないので `suspendPostEffect=false`）
  - Config: `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel` / `gameViewShowEffect` / `gameViewShowGizmo` / `sceneViewShowEffect`（すべて `bool`、既定 `true`）

- [ ] **Step 1: 失敗するテストを書く（ViewEffectVisibility）**

`source/COM3D2.SceneEditor.Plugin.Tests/ViewEffectVisibilityTests.cs`:

```csharp
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// エフェクト表示 OFF のとき何を隠すか。旧ポスプロ同期の「編集中のレイヤーは常に反映」を
    /// 種類単位で引き継いでいることを固定する
    /// </summary>
    public class ViewEffectVisibilityTests
    {
        [Theory]
        [InlineData(EffectLayerKind.None)]
        [InlineData(EffectLayerKind.PostEffect)]
        [InlineData(EffectLayerKind.StageLight)]
        public void 表示ONなら何も隠さない(EffectLayerKind current)
        {
            var state = ViewEffectVisibility.Resolve(true, current);

            Assert.False(state.suspendPostEffect);
            Assert.False(state.hideStageLight);
            Assert.False(state.hideStageLaser);
            Assert.False(state.hidePsyllium);
        }

        [Fact]
        public void 表示OFFでカレントがエフェクト以外なら全部隠す()
        {
            var state = ViewEffectVisibility.Resolve(false, EffectLayerKind.None);

            Assert.True(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }

        [Fact]
        public void 表示OFFでもカレントがポストエフェクトならポストエフェクトだけ出す()
        {
            var state = ViewEffectVisibility.Resolve(false, EffectLayerKind.PostEffect);

            Assert.False(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }

        [Theory]
        [InlineData(EffectLayerKind.StageLight, false, true, true)]
        [InlineData(EffectLayerKind.StageLaser, true, false, true)]
        [InlineData(EffectLayerKind.Psyllium, true, true, false)]
        public void 表示OFFでもカレントのライブ演出だけ出す(
            EffectLayerKind current, bool hideLight, bool hideLaser, bool hidePsyllium)
        {
            var state = ViewEffectVisibility.Resolve(false, current);

            Assert.True(state.suspendPostEffect);
            Assert.Equal(hideLight, state.hideStageLight);
            Assert.Equal(hideLaser, state.hideStageLaser);
            Assert.Equal(hidePsyllium, state.hidePsyllium);
        }

        [Fact]
        public void SceneView用はライブ演出だけを隠しポストエフェクトに触れない()
        {
            var state = ViewEffectVisibility.HideAll(true);

            Assert.False(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }

        [Fact]
        public void SameAsは全フラグを比べる()
        {
            var a = ViewEffectVisibility.Resolve(false, EffectLayerKind.None);
            var b = ViewEffectVisibility.Resolve(false, EffectLayerKind.StageLight);

            Assert.True(ViewEffectVisibility.SameAs(a, a));
            Assert.False(ViewEffectVisibility.SameAs(a, b));
        }
    }
}
```

- [ ] **Step 2: 失敗するテストを書く（Config）**

`source/COM3D2.SceneEditor.Plugin.Tests/ConfigViewToggleTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>表示トグルを足す前の Config を読んでも、すべて表示 (ON) から始まることを固定する</summary>
    public class ConfigViewToggleTests
    {
        [Fact]
        public void 表示トグルの無い旧設定は全部ONで読まれる()
        {
            var xml = "<?xml version=\"1.0\"?><Config version=\"2\"><sceneViewShowBg>false</sceneViewShowBg></Config>";
            Config config;
            using (var reader = new StringReader(xml))
            {
                config = (Config) new XmlSerializer(typeof(Config)).Deserialize(reader);
            }

            Assert.True(config.gameViewShowBg);
            Assert.True(config.gameViewShowMaid);
            Assert.True(config.gameViewShowModel);
            Assert.True(config.gameViewShowEffect);
            Assert.True(config.gameViewShowGizmo);
            Assert.True(config.sceneViewShowEffect);
            Assert.False(config.sceneViewShowBg);
        }

        [Fact]
        public void 表示トグルは保存して読み直せる()
        {
            var src = new Config { gameViewShowMaid = false, gameViewShowGizmo = false, sceneViewShowEffect = false };
            var serializer = new XmlSerializer(typeof(Config));
            var writer = new StringWriter();
            serializer.Serialize(writer, src);
            Config dst;
            using (var reader = new StringReader(writer.ToString()))
            {
                dst = (Config) serializer.Deserialize(reader);
            }

            Assert.False(dst.gameViewShowMaid);
            Assert.False(dst.gameViewShowGizmo);
            Assert.False(dst.sceneViewShowEffect);
            Assert.True(dst.gameViewShowBg);
        }
    }
}
```

`Config` の名前空間が `COM3D2.SceneEditor.Plugin` 以外なら、`ConfigGizmoSpaceMigrationTests.cs` の using に合わせる（同ファイルは `using COM3D2.MotionTimelineEditor;` を持つ）。

- [ ] **Step 3: テストが失敗することを確かめる**

共通コマンドを `<Filter>` = `FullyQualifiedName~ViewEffectVisibilityTests|FullyQualifiedName~ConfigViewToggleTests` で実行する。Expected: コンパイルエラー（`EffectLayerKind` / `gameViewShowBg` が無い）。

- [ ] **Step 4: `ViewEffectVisibility.cs` を書く**

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>エフェクト表示の例外判定に使う、タイムラインのカレントレイヤーの種類</summary>
    public enum EffectLayerKind
    {
        None,
        PostEffect,
        StageLight,
        StageLaser,
        Psyllium,
    }

    /// <summary>あるビューでエフェクトのうち何を隠すか</summary>
    public struct ViewEffectState
    {
        public bool suspendPostEffect;
        public bool hideStageLight;
        public bool hideStageLaser;
        public bool hidePsyllium;
    }

    /// <summary>
    /// エフェクト表示トグルから、ポストエフェクトの一時停止とライブ演出の非表示を決める。
    /// OFF でも編集中 (カレント) のエフェクト系レイヤーの種類だけは見せる (旧ポスプロ同期の挙動)
    /// </summary>
    public static class ViewEffectVisibility
    {
        public static ViewEffectState Resolve(bool showEffect, EffectLayerKind currentLayer)
        {
            if (showEffect)
            {
                return new ViewEffectState();
            }
            return new ViewEffectState
            {
                suspendPostEffect = currentLayer != EffectLayerKind.PostEffect,
                hideStageLight = currentLayer != EffectLayerKind.StageLight,
                hideStageLaser = currentLayer != EffectLayerKind.StageLaser,
                hidePsyllium = currentLayer != EffectLayerKind.Psyllium,
            };
        }

        /// <summary>SceneView 用。SceneView カメラにはポストエフェクトが掛からないためライブ演出だけを扱う</summary>
        public static ViewEffectState HideAll(bool hide)
        {
            return new ViewEffectState
            {
                hideStageLight = hide,
                hideStageLaser = hide,
                hidePsyllium = hide,
            };
        }

        public static bool SameAs(ViewEffectState a, ViewEffectState b)
        {
            return a.suspendPostEffect == b.suspendPostEffect
                && a.hideStageLight == b.hideStageLight
                && a.hideStageLaser == b.hideStageLaser
                && a.hidePsyllium == b.hidePsyllium;
        }
    }
}
```

csproj が `Compile Include` を個別列挙している場合は `Manager\ViewEffectVisibility.cs` を追加する（`grep -n "ViewCullingFilter.cs" COM3D2.SceneEditor.Plugin.csproj` で確かめる）。テスト csproj も同様に確かめる。

- [ ] **Step 5: Config に項目を足す**

`Config.cs` の `gameViewCustomHeight` の行の後に追加する。

```csharp
        // GameView の表示トグル (メインカメラの描画だけに効く)。タイムライン操作ウィンドウのトグルも同じ値を使う。
        // gameViewShowGizmo はメニューバーの「ボーン表示」の実体 (MaidManipulateManager.isBoneVisible)
        public bool gameViewShowBg = true;
        public bool gameViewShowMaid = true;
        public bool gameViewShowModel = true;
        public bool gameViewShowEffect = true;
        public bool gameViewShowGizmo = true;
```

`sceneViewShowGizmo` の行の後に追加する。

```csharp
        // SceneView のエフェクトはライブ演出だけ (SceneView カメラにはポストエフェクトが掛からない)
        public bool sceneViewShowEffect = true;
```

- [ ] **Step 6: テストが通ることを確かめる**

Step 3 と同じコマンド。Expected: すべて PASS。

- [ ] **Step 7: Commit**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/ViewEffectVisibility.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin.Tests/ViewEffectVisibilityTests.cs source/COM3D2.SceneEditor.Plugin.Tests/ConfigViewToggleTests.cs
git commit -m "feat(view-toggle): エフェクト表示の解決と表示トグルの設定項目を追加する"
```

csproj を変えた場合はそれも add する。

---

### Task 4: GameView のツールバーとカメラ別の非表示

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ViewCullingFilter.cs`（全体）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs:614-646`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs:43-81, 157, 177-200, 377-379, 453-458`
- Modify: `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs:53-56, 462-469, 493-518`

**Interfaces:**
- Consumes: Task 2 `PostEffectsClient.SetSuspended(bool) → bool`、Task 3 `ViewEffectVisibility` / `ViewEffectState` / `EffectLayerKind` / Config 項目
- Produces: `ViewCullingFilter.hideStageLight` / `hideStageLaser` / `hidePsyllium`（bool）、`ViewCullingFilter.ApplyEffectState(ViewEffectState)`、`GameViewManager.showBg` / `showMaid` / `showModel` / `showEffect`（bool プロパティ、Config 読み書き）

- [ ] **Step 1: `ViewCullingFilter` のライブ演出対応と PNG の削除**

- クラスコメントの「背景 / メイド / モデル / PNG」を「背景 / メイド / モデル / ライブ演出」にする
- `hidePng` を削除し、`hideStageLight` / `hideStageLaser` / `hidePsyllium` を足す
- `_pngRenderers` と `_pngCacheValid`、その上の PNG デカールのコメント、`CollectPngRenderers` を削除する
- 3 種のキャッシュを足し、`InvalidateCache` / `OnPreCull` に追加する

```csharp
        public bool hideBg = false;
        public bool hideMaid = false;
        public bool hideModel = false;
        // ライブ演出は編集中レイヤーの例外 (ViewEffectVisibility) を種類単位で効かせるため分ける
        public bool hideStageLight = false;
        public bool hideStageLaser = false;
        public bool hidePsyllium = false;
```

```csharp
        private readonly List<Renderer> _stageLightRenderers = new List<Renderer>();
        private readonly List<Renderer> _stageLaserRenderers = new List<Renderer>();
        private readonly List<Renderer> _psylliumRenderers = new List<Renderer>();
        private bool _stageLightCacheValid = false;
        private bool _stageLaserCacheValid = false;
        private bool _psylliumCacheValid = false;
```

```csharp
        /// <summary>エフェクトの非表示状態を当てる。変わったときだけキャッシュを捨てる</summary>
        public void ApplyEffectState(ViewEffectState state)
        {
            if (hideStageLight == state.hideStageLight
                && hideStageLaser == state.hideStageLaser
                && hidePsyllium == state.hidePsyllium)
            {
                return;
            }
            hideStageLight = state.hideStageLight;
            hideStageLaser = state.hideStageLaser;
            hidePsyllium = state.hidePsyllium;
            InvalidateCache();
        }
```

`OnPreCull` の `hideModel` の分岐の後（旧 `hidePng` の位置）:

```csharp
            if (hideStageLight)
            {
                DisableRenderers(_stageLightRenderers, ref _stageLightCacheValid, CollectStageLightRenderers);
            }
            if (hideStageLaser)
            {
                DisableRenderers(_stageLaserRenderers, ref _stageLaserCacheValid, CollectStageLaserRenderers);
            }
            if (hidePsyllium)
            {
                DisableRenderers(_psylliumRenderers, ref _psylliumCacheValid, CollectPsylliumRenderers);
            }
```

収集関数（旧 `CollectPngRenderers` の位置）:

```csharp
        // ライブ演出はすべて MeshRenderer で、各マネージャの GameObject 配下にまとまっている
        private static void CollectStageLightRenderers(List<Renderer> results)
        {
            CollectChildren(COM3D2.MotionTimelineEditor.Plugin.StageLightManager.instance, results);
        }

        private static void CollectStageLaserRenderers(List<Renderer> results)
        {
            CollectChildren(COM3D2.MotionTimelineEditor.Plugin.StageLaserManager.instance, results);
        }

        private static void CollectPsylliumRenderers(List<Renderer> results)
        {
            CollectChildren(COM3D2.MotionTimelineEditor.Plugin.PsylliumManager.instance, results);
        }

        private static void CollectChildren(Component root, List<Renderer> results)
        {
            if (root != null)
            {
                results.AddRange(root.GetComponentsInChildren<Renderer>(true));
            }
        }
```

`InvalidateCache` は `_pngCacheValid = false;` を消し、3 つの `..CacheValid = false;` を足す。

- [ ] **Step 2: `PngPlacementManager` の GameView 用処理を削除する**

`OnPreCullDecals` の末尾を次にする（`else if (IsHiddenByGameView(camera)) { HideAllProjectors(); }` を消す）。

```csharp
            // 更新で Projector が有効へ戻るため、止めるのは更新の後
            if (camera == SceneViewManager.instance.sceneCamera)
            {
                HideOverlayProjectors();
            }
```

`IsHiddenByGameView`（コメント含む）と `HideAllProjectors` を削除する。`HideOverlayProjectors` / `RestoreHiddenProjectors` は残す。

- [ ] **Step 3: `GameViewManager` のトグルを Config 連動にする**

43-81 行（`cullingFilter` のコメント〜`ApplyCullingSettings`）を次に置き換える。

```csharp
        /// <summary>メインカメラの描画から背景・メイド・モデル・ライブ演出を隠すフィルタ。通常表示と撮影の両方に効く</summary>
        public ViewCullingFilter cullingFilter { get; private set; }

        // GameView の表示トグル。タイムライン操作ウィンドウのトグルも同じ値を読み書きする
        public bool showBg
        {
            get => config.gameViewShowBg;
            set { config.gameViewShowBg = value; config.dirty = true; ApplyCullingSettings(); }
        }

        public bool showMaid
        {
            get => config.gameViewShowMaid;
            set { config.gameViewShowMaid = value; config.dirty = true; ApplyCullingSettings(); }
        }

        public bool showModel
        {
            get => config.gameViewShowModel;
            set { config.gameViewShowModel = value; config.dirty = true; ApplyCullingSettings(); }
        }

        /// <summary>ポストエフェクトとライブ演出。反映は LateUpdate の UpdateEffectVisibility</summary>
        public bool showEffect
        {
            get => config.gameViewShowEffect;
            set { config.gameViewShowEffect = value; config.dirty = true; }
        }

        private void ApplyCullingSettings()
        {
            if (cullingFilter == null)
            {
                return;
            }
            cullingFilter.hideBg = !config.gameViewShowBg;
            cullingFilter.hideMaid = !config.gameViewShowMaid;
            cullingFilter.hideModel = !config.gameViewShowModel;
            cullingFilter.InvalidateCache();
        }

        // PostEffects.Plugin へ最後に送れた一時停止の値。送れていない間は再送する
        private bool _sentPostEffectSuspended = false;

        /// <summary>
        /// エフェクト表示とタイムラインのカレントレイヤーから、ライブ演出の非表示と
        /// ポストエフェクトの一時停止を当てる。カレントは毎フレーム変わりうるため LateUpdate で呼ぶ
        /// </summary>
        private void UpdateEffectVisibility()
        {
            var state = ViewEffectVisibility.Resolve(config.gameViewShowEffect, GetCurrentEffectLayerKind());
            if (cullingFilter != null)
            {
                cullingFilter.ApplyEffectState(state);
            }
            SetPostEffectSuspended(state.suspendPostEffect);
        }

        private void SetPostEffectSuspended(bool suspended)
        {
            if (_sentPostEffectSuspended == suspended)
            {
                return;
            }
            if (PostEffectsClient.SetSuspended(suspended))
            {
                _sentPostEffectSuspended = suspended;
            }
        }

        /// <summary>
        /// カレントレイヤーの種類。ライブ演出を種類単位で隠すため型で判定する
        /// (ライブ演出のレイヤーを増やしたら、ここと ViewCullingFilter に種類を足す)
        /// </summary>
        private static EffectLayerKind GetCurrentEffectLayerKind()
        {
            var layer = MTEP.TimelineManager.instance.currentLayer;
            if (layer is MTEP.PostEffectTimelineLayer)
            {
                return EffectLayerKind.PostEffect;
            }
            if (layer is MTEP.StageLightTimelineLayer)
            {
                return EffectLayerKind.StageLight;
            }
            if (layer is MTEP.StageLaserTimelineLayer)
            {
                return EffectLayerKind.StageLaser;
            }
            if (layer is MTEP.PsylliumTimelineLayer)
            {
                return EffectLayerKind.Psyllium;
            }
            return EffectLayerKind.None;
        }
```

`PostEffectsClient` の名前空間（`MTEUtils/PostEffectsClient.cs` の `namespace`）が未 using なら追加する。`MTEP.TimelineManager.instance` が null を返しうる実装なら `var manager = MTEP.TimelineManager.instance; var layer = manager != null ? manager.currentLayer : null;` にする（定義を開いて確かめる）。

- [ ] **Step 4: モード開始・終了とフレーム更新に組み込む**

- `EnterWindowMode` の `_showBg = _showModel = _showPng = true;` を次に置き換える（表示トグルは `AttachGizmoRenderer` → `ApplyCullingSettings()` が保存値を当てる）

```csharp
            // ホスト側に前回の一時停止が残っていても揃うよう、記録に関係なく一度解除を送る
            // (記録だけが初期値に戻る再読込などでずれないようにする)
            PostEffectsClient.SetSuspended(false);
            _sentPostEffectSuspended = false;
```
- `LateUpdate` の `if (!isWindowMode) { return; }` の直後に `UpdateEffectVisibility();` を足す（メインカメラが取れないフレームでも一時停止の状態は決めておく）
- `ExitWindowMode` の `DetachGizmoRenderer();` の直前に追加する

```csharp
            // モード外 (エディタ無効) ではゲーム本来の見え方へ戻す
            SetPostEffectSuspended(false);
```

- [ ] **Step 5: `GameViewWindow` のツールバーを 5 項目にする**

53-56 行のデリゲートを置き換える。

```csharp
        // OnGUI のたびにクロージャを作らないよう使い回す
        private static readonly Action<bool> SetShowBg = value => gameViewManager.showBg = value;
        private static readonly Action<bool> SetShowMaid = value => gameViewManager.showMaid = value;
        private static readonly Action<bool> SetShowModel = value => gameViewManager.showModel = value;
        private static readonly Action<bool> SetShowEffect = value => gameViewManager.showEffect = value;
        private static readonly Action<bool> SetShowGizmo = value => MaidManipulateManager.instance.isBoneVisible = value;
```

`GetToolbarLocalRect` を置き換える。

```csharp
        private static readonly ToolbarIcons.Kind[] ToggleKinds =
        {
            ToolbarIcons.Kind.Bg, ToolbarIcons.Kind.Maid, ToolbarIcons.Kind.Model,
            ToolbarIcons.Kind.PostEffect, ToolbarIcons.Kind.Gizmo,
        };

        private Rect GetToolbarLocalRect()
        {
            // 項目: 撮影 / 比率 / 表示トグル 5 つ。マージンは項目間の 6 箇所分
            var width = FRAME * 2 + ViewToolbarDrawer.ITEM_MARGIN * (1 + ToggleKinds.Length) + ASPECT_COMBO_WIDTH
                + ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(ToolbarIcons.Kind.Screenshot));
            foreach (var kind in ToggleKinds)
            {
                width += ViewToolbarDrawer.GetItemWidth(ToolbarIcons.GetTexture(kind));
            }
            return new Rect(0, HEADER_HEIGHT, width, ViewToolbarDrawer.TOOLBAR_HEIGHT);
        }
```

`DrawToolbar` のコメントを「撮影・表示比率・背景 / メイド / モデル / エフェクト / ギズモのトグル列」にし、トグル 3 行を置き換える。

```csharp
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Bg), "背景",
                gameViewManager.showBg, SetShowBg);
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Maid), "メイド",
                gameViewManager.showMaid, SetShowMaid);
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Model), "モデル",
                gameViewManager.showModel, SetShowModel);
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.PostEffect), "エフェクト",
                gameViewManager.showEffect, SetShowEffect);
            // ギズモはメニューバーの「ボーン表示」と同じ状態 (GameView だけに効く)
            ViewToolbarDrawer.DrawToggle(view, ToolbarIcons.GetTexture(ToolbarIcons.Kind.Gizmo), "ギズモ",
                MaidManipulateManager.instance.isBoneVisible, SetShowGizmo);
```

- [ ] **Step 6: ビルド + 全テスト**

共通コマンドを `--filter` 無しで実行する。Expected: 両構成 `0 エラー`、テスト全件 PASS。`showPng` / `hidePng` の参照が残っていればエラーになるので、`grep -rn "showPng\|hidePng\|IsHiddenByGameView" source/COM3D2.SceneEditor.Plugin --include=*.cs` が 0 件であることも確かめる。

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ViewCullingFilter.cs source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs
git commit -m "feat(game-view): 表示トグルを背景・メイド・モデル・エフェクト・ギズモにして設定へ保存する"
```

---

### Task 5: SceneView のエフェクトトグル

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs:101-127`（`ApplyViewSettings`）
- Modify: `source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs:265-296`（`DrawToolbar`）

**Interfaces:**
- Consumes: Task 3 `config.sceneViewShowEffect`、`ViewEffectVisibility.HideAll`、Task 4 `ViewCullingFilter.ApplyEffectState`

- [ ] **Step 1: `ApplyViewSettings` でライブ演出を当てる**

`cullingFilter.hideModel = ...;` の後に追加する。

```csharp
                cullingFilter.ApplyEffectState(ViewEffectVisibility.HideAll(!config.sceneViewShowEffect));
```

- [ ] **Step 2: ツールバーにエフェクトを足す**

- `var effectIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.PostEffect);` を `modelIcon` の後に足す
- 幅計算のマージンを `ITEM_MARGIN * 7` にし（コメントも「7 箇所分」）、`ViewToolbarDrawer.GetItemWidth(effectIcon)` を足す
- `モデル` と `ギズモ` のトグルの間に追加する

```csharp
            DrawToolbarToggle(view, effectIcon, "エフェクト", config.sceneViewShowEffect,
                value => config.sceneViewShowEffect = value);
```

- `DrawToolbar` の summary を「背景/メイド/モデル/エフェクト/ギズモ表示・パース・オートフォーカスのトグル列」にする

- [ ] **Step 3: ビルド**

共通コマンド（`dotnet test` 抜き）。Expected: 両構成 `0 エラー`。

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs
git commit -m "feat(scene-view): ライブ演出を隠すエフェクトのトグルを追加する"
```

---

### Task 6: ボーン表示を GameView 専用にし、SceneView を切り離す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:290-371, 388-400`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs:32-36, 81-86`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs:369-371`（`AttachGizmoRenderer`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs:155-157`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidDragPointRing.cs:155-165`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFingerDragPoint.cs:288-291`
- Modify: `source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs:477, 534-535`

**Interfaces:**
- Consumes: Task 3 `config.gameViewShowGizmo` / `config.sceneViewShowGizmo`
- Produces: `MaidManipulateManager.isBoneVisible`（Config 連動）、`isBoneEditing`（GameView 用、意味は従来どおり）、`isSceneViewBoneEditing`、`isAnyBoneEditing`、`BoneLineRenderer.isBoneEditingInView`（`Func<bool>`）

- [ ] **Step 1: `isBoneVisible` を Config へ移す**

`isBoneVisible` のプロパティと `_isBoneVisible` を置き換える。summary は次に書き換える。

```csharp
        /// <summary>
        /// ボーン表示 (GameView のギズモ)。メニューバー・BoneEditWindow・GameView・タイムライン操作ウィンドウの
        /// トグルが同じ値を読み書きし、Config に保存する。GameView にだけ効き、SceneView は
        /// ツールバーのギズモ表示 (config.sceneViewShowGizmo) で決まる。
        /// GameView で実際に出すかは編集モードとの AND (isBoneEditing) で決まる
        /// </summary>
        public bool isBoneVisible
        {
            get => config.gameViewShowGizmo;
            set
            {
                if (config.gameViewShowGizmo == value)
                {
                    return;
                }
                config.gameViewShowGizmo = value;
                config.dirty = true;

                if (!value)
                {
                    // 非表示に切り替えた瞬間は、見えないギズモを掴んだままにしない
                    EndGizmoDrag(GameViewManager.instance.gizmoRenderer);
                }
            }
        }
```

- [ ] **Step 2: ビューごとの判定を足す**

`isBoneEditing` の summary を「GameView でボーンギズモ・骨格線を出すか」に直し、その後に追加する。

```csharp
        /// <summary>
        /// SceneView でボーンギズモ・骨格線・指の白丸を出すか。ボーン表示 (isBoneVisible) ではなく
        /// SceneView ツールバーのギズモ表示に従う
        /// </summary>
        public bool isSceneViewBoneEditing =>
            isEditMode && config.sceneViewShowGizmo && SceneViewWindow.instance.isShowWnd && !isBlendLayerSelected;

        /// <summary>どちらかのビューがボーン系を出すか。ギズモ・白丸の実体は両ビュー共有のため、作る判定はこれで行う</summary>
        public bool isAnyBoneEditing => isBoneEditing || isSceneViewBoneEditing;
```

`isDragPointActive` を置き換える（summary の「isBoneVisible だけで作ると」以降も新しい条件に合わせて直す）。

```csharp
        private bool isDragPointActive =>
            !isBlendLayerSelected
            && ((isBoneVisible && isEditMode)
                || (config.sceneViewShowGizmo && SceneViewWindow.instance.isShowWnd));
```

`isGameViewDragPointVisible` の summary の「白丸自体は isBoneVisible だけで作られ SceneView では常に出すが」を「白丸の実体は SceneView のためにも作られるが」に直す。

- [ ] **Step 3: `Update` の実体の判定を `isAnyBoneEditing` にする**

```csharp
            // ボーンギズモはどちらかのビューで出すときだけ付ける (isAnyBoneEditing)。
            // GameView 側の描画は GizmoRenderer.isDrawEnabled が isBoneEditing で絞る。
            // 非表示の間はギズモコンポーネントを付けたままにしない
            // (呼出済みの全メイドへ常時アタッチされ、描画・ログのコストが残るため)
            boneGizmoController.SetTarget(isAnyBoneEditing ? movableMaid : null);
            // 指の個別編集中は同じ修飾キーで指関節のギズモへ切り替える
            boneGizmoController.Update(isAnyBoneEditing, isFingerEditMode);
```

```csharp
            fingerDragPointController.SetTarget(
                isAnyBoneEditing && isFingerEditMode ? movableMaid : null);
```

- [ ] **Step 4: `BoneLineRenderer` をビューごとに判定させる**

`drawEnabled` の宣言の後に追加し、`isActive` の最後の条件を差し替える。

```csharp
        /// <summary>このビューでボーンを出すか。既定は SceneView で、GameView 側は生成時に差し替える</summary>
        public Func<bool> isBoneEditingInView = () => MaidManipulateManager.instance.isSceneViewBoneEditing;
```

```csharp
        private bool isActive =>
            drawEnabled
            && SceneEditorPlugin.instance.isEnable
            && isHostActive()
            && boneEditManager.editMode
            && isBoneEditingInView();
```

`GameViewManager.AttachGizmoRenderer` の `boneLineRenderer.isHostActive = IsGizmoHostActive;` の後に追加する。

```csharp
            // GameView の骨格線はボーン表示 (isBoneVisible) に従う
            boneLineRenderer.isBoneEditingInView = () => MaidManipulateManager.instance.isBoneEditing;
```

- [ ] **Step 5: `BoneEditManager` のギズモ対象を両ビュー共有の判定にする**

```csharp
            // 骨格線と同じくどちらかのビューでボーンを出している間だけ。GameView 側の描画は isDrawEnabled が絞る
            GizmoRenderer.externalTargetProvider = () =>
                editMode && MaidManipulateManager.instance.isAnyBoneEditing
                    && selectedBone != null && !selectionManager.hasBoneSelection
                    ? selectedBone.gameObject : null;
```

既存のコメント行「骨格線と同じく編集モード＋ボーン表示 (isBoneEditing) 中だけ出す」をこの 1 行に置き換える。

- [ ] **Step 6: 白丸の SceneView 側の判定**

`MaidDragPointRing`（162 行の GameView の判定の後）に追加し、直前のコメントも直す。

```csharp
            // ゲーム画面側は編集モード外では白丸を出さない
            if (isMainCamera && !MaidManipulateManager.instance.isGameViewDragPointVisible)
            {
                return;
            }

            // SceneView はツールバーのギズモ表示に従う (実体は GameView のボーン表示だけで作られることもある)
            if (isSceneCamera && !ConfigManager.instance.config.sceneViewShowGizmo)
            {
                return;
            }
```

`SceneViewWindow` の 534-535 行の `!MaidManipulateManager.instance.isBoneVisible` を `!config.sceneViewShowGizmo` にする。477 行を次にする。

```csharp
                    var dragPoint = config.sceneViewShowGizmo
                        ? selectionManager.FindDragPointAtRay(camera, rtPoint) : null;
```

SceneView の骨格線は `isSceneViewBoneEditing` が `sceneViewShowGizmo` を含むので、`boneLineRenderer.drawEnabled` は触らない。

`MaidFingerDragPoint.OnMouseDown` に GameView の判定を足す（`MaidIKDragPoint.cs:324` と同じ作法）。

```csharp
        private void OnMouseDown()
        {
            // SceneView のためだけに実体があるときは、ゲーム画面からは掴ませない
            if (!MaidManipulateManager.instance.isGameViewDragPointVisible)
            {
                return;
            }
            BeginDrag(GetGameCamera(), Input.mousePosition);
        }
```

- [ ] **Step 7: 残った参照を確かめる**

```bash
grep -rn "isBoneVisible\|isBoneEditing" source/COM3D2.SceneEditor.Plugin --include=*.cs
```

Expected: GameView 用（`GizmoRenderer` の `followsBoneVisibility` 分岐、`isGameViewDragPointVisible`、`MenuBarWindow`、`BoneEditWindow`、`GameViewWindow`、Task 7 の `TimelineControlWindow`）と Task 6 で書いた箇所だけ。SceneView の判定に `isBoneVisible` が残っていないこと。`GizmoRenderer.cs:262-276, 370-379` のコメントの「SceneView は…」の記述が実装と合っているか読み、合っていなければ直す。

- [ ] **Step 8: ビルド + 全テスト**

共通コマンドを `--filter` 無しで。Expected: 両構成 `0 エラー`、全件 PASS。

- [ ] **Step 9: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs source/COM3D2.SceneEditor.Plugin/Manager/GameViewManager.cs source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(bone-visible): ボーン表示を GameView 専用にして保存し、SceneView はギズモ表示で決める"
```

---

### Task 7: タイムライン操作ウィンドウを GameView と統一し、旧実装を削除する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs:676-753`（`DrawToggles`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs:54`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:121-142, 2538`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:250-275`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer.cs:113-117`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs:94, 131-134`、`StageLaserTimelineLayer.cs:91, 128-131`、`PsylliumTimelineLayer.cs:132, 171-174`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:381-417, 684-685, 696, 931-932, 1101-1102`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:329-333`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs:250-257`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BGTimelineLayer.cs:80`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BGColorTimelineLayer.cs:130-134`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs:144-158`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs:273-277, 430-437`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs`

**Interfaces:**
- Consumes: Task 4 `GameViewManager.showBg` / `showMaid` / `showModel` / `showEffect`、Task 6 `MaidManipulateManager.isBoneVisible`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 背景表示・地面連動はタイムライン XML から外した。旧 XML の要素は読み飛ばされ、
    /// 書き出しにも出ないことを固定する
    /// </summary>
    public class TimelineXmlRemovedFieldsTests
    {
        [Fact]
        public void 旧XMLの背景表示と地面連動は読み飛ばされる()
        {
            var xml = "<?xml version=\"1.0\"?><TimelineData version=\"38\">"
                + "<IsBackgroundVisible>false</IsBackgroundVisible>"
                + "<IsGroundLinkedToBackground>true</IsGroundLinkedToBackground>"
                + "<IsLoopAnm>false</IsLoopAnm>"
                + "</TimelineData>";
            TimelineXml timeline;
            using (var reader = new StringReader(xml))
            {
                timeline = (TimelineXml) new XmlSerializer(typeof(TimelineXml)).Deserialize(reader);
            }

            // 後ろの要素まで読めている (途中で失敗していない)
            Assert.False(timeline.isLoopAnm);
        }

        [Fact]
        public void 書き出しに背景表示と地面連動は出ない()
        {
            var writer = new StringWriter();
            new XmlSerializer(typeof(TimelineXml)).Serialize(writer, new TimelineXml());
            var text = writer.ToString();

            Assert.DoesNotContain("IsBackgroundVisible", text);
            Assert.DoesNotContain("IsGroundLinkedToBackground", text);
        }
    }
}
```

`TimelineXml` のルートの `version` 属性名が違う場合は `TimelineXml.cs:241-250` を見て合わせる。

- [ ] **Step 2: テストが失敗することを確かめる**

`<Filter>` = `FullyQualifiedName~TimelineXmlRemovedFieldsTests`。Expected: `書き出しに背景表示と地面連動は出ない` が FAIL（要素が出る）。

- [ ] **Step 3: 背景表示・地面連動を削除する**

- `TimelineXml.cs`: `IsBackgroundVisible` と `IsGroundLinkedToBackground` のフィールド（属性含む）を削除する
- `TimelineData.cs`: `_isBackgroundVisible` / `isBackgroundVisible`、`_isGroundLinkedToBackground` / `isGroundLinkedToBackground` のプロパティ、`ResetSettings` の 2 行、`OnPluginDisable` の `studioHack?.SetBackgroundVisible(true);`、`FromXml` / `ToXml` の各 2 行を削除する
- `SceneEditorHack.cs`: `SetBackgroundVisible` を削除する
- `BGTimelineLayer.cs:80`: `studioHack.SetBackgroundVisible(timeline.isBackgroundVisible);` を削除する
- `BGColorTimelineLayer.cs:130-134`: `if (timeline.isGroundLinkedToBackground && !timeline.isBackgroundVisible) { visible = false; }` を削除し、`bgGround.visible = start.visible;` にまとめる
- `TimelineManager.cs:2538` 付近のコメントの「中身 (レイヤーへの配信と studioHack?.SetBackgroundVisible) は」を「中身 (レイヤーへの配信) は」にする
- `TimelineSettingWindow.cs`: `DrawGroundLinkSection(view);` とその直後の `view.DrawHorizontalLine(Color.gray);`、`DrawGroundLinkSection` メソッドを削除する
- `StudioModelManager.cs:144-158`: `_Visible` / `Visible` を削除する（`SetModelVisible` は他の呼び出し元があるので残す。`grep -rn "SetModelVisible" source/COM3D2.SceneEditor.Plugin --include=*.cs` で確かめる）

- [ ] **Step 4: ポスプロ同期を削除する**

- `Timeline/Config.cs:54`: `public bool isPostEffectSync = true;` を削除する
- `TimelineManager.cs:121-142`: `hasPostEffectSyncLayer` / `hasNonCurrentPostEffectSyncLayer` / `IsPostEffectSyncTarget` を削除する
- `TimelineLayerBase.cs`: `_isLiveEffectHidden`、`UpdateLiveEffectSyncVisibility`、`RestoreLiveEffectSyncVisibility` を削除する
- `PostEffectTimelineLayer.cs:113-117`: `if (!isCurrent && !config.isPostEffectSync) { postEffectManager.DisableAllEffects(); return; }` を削除する。`DisableAllEffects` が他から呼ばれていなければ残すか消すかは `grep -rn "DisableAllEffects"` で判断し、呼び出しが 0 件になったら `PostEffectManager.DisableAllEffects` も削除する
- `StageLightTimelineLayer.cs` / `StageLaserTimelineLayer.cs` / `PsylliumTimelineLayer.cs`: `Dispose` の `RestoreLiveEffectSyncVisibility(...)` と、`ApplyPlayData` の `if (!UpdateLiveEffectSyncVisibility(...)) { return; }` を削除する

- [ ] **Step 5: `TimelineControlWindow.DrawToggles` を付け替える**

`自動登録` の後から `背景表示` までを次に置き換える（メイド退避のコメント・`seMaidManager` / `targetMaid` / `isMaidVisible` も消す）。

```csharp
            // 表示トグルは GameView のツールバーと同じ状態 (GameView だけに効き、設定に保存される)
            var gameViewManager = GameViewManager.instance;
            DrawIconToggle(view, ToolbarIcons.Kind.Bg, "背景表示", gameViewManager.showBg, true, newValue =>
            {
                gameViewManager.showBg = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.Maid, "メイド表示", gameViewManager.showMaid, true, newValue =>
            {
                gameViewManager.showMaid = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.Model, "モデル表示", gameViewManager.showModel, true, newValue =>
            {
                gameViewManager.showModel = newValue;
            });

            // OFF でも編集中のエフェクト系レイヤーの種類だけは出す (ViewEffectVisibility)
            DrawIconToggle(view, ToolbarIcons.Kind.PostEffect, "エフェクト表示", gameViewManager.showEffect, true, newValue =>
            {
                gameViewManager.showEffect = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.Gizmo, "ギズモ表示", MaidManipulateManager.instance.isBoneVisible, true, newValue =>
            {
                MaidManipulateManager.instance.isBoneVisible = newValue;
            });
```

末尾の `if (timelineManager.hasPostEffectSyncLayer) { ... ポスプロ同期 ... }` を削除する。`maidManager` / `modelManager` / `timeline` がこのメソッド以外で使われていなければ、未使用になったフィールド・プロパティを確かめて消す（他メソッドで使っていれば残す）。

- [ ] **Step 6: 残った参照を確かめる**

```bash
grep -rn "isPostEffectSync\|PostEffectSyncLayer\|LiveEffectSyncVisibility\|isBackgroundVisible\|isGroundLinkedToBackground\|SetBackgroundVisible\|modelManager.Visible" source/COM3D2.SceneEditor.Plugin --include=*.cs
```

Expected: 0 件（`EasySettingType.ポスプロ同期` などの enum 名は対象外）。

- [ ] **Step 7: ビルド + 全テスト**

共通コマンドを `--filter` 無しで。Expected: 両構成 `0 エラー`、全件 PASS（Step 1 のテストを含む）。

- [ ] **Step 8: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs
git commit -m "feat(timeline-control): 表示トグルを GameView と統一し、ポスプロ同期と地面連動を削除する"
```

PostEffectManager を変えた場合はそれも add する。

---

### Task 8: ドキュメント

**Files:**
- Modify: `docs-site/guide/windows.md:16-30`、`docs-site/guide/scene-view.md:16, 80-84`、`docs-site/guide/maid-editing.md:36-44`、`docs-site/guide/getting-started.md:26`
- Modify: `docs-site/timeline/control.md:72-90`、`docs-site/timeline/settings.md:26, 32`、`docs-site/timeline/layers-background.md:22`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（タイムライン XML の互換方向）
- Modify: `docs/superpowers/specs/2026-10-07-gameview-view-toggles-design.md`（`boneVisible` → `gameViewShowGizmo`）

- [ ] **Step 1: `windows.md` の GameView ツールバー**

表の 3 行目を置き換える。

```markdown
| 背景 / メイド / モデル / エフェクト / ギズモのアイコン | ゲーム画面の表示を切り替える（点灯 = 表示） |
```

箇条書きの「背景・モデル・PNG の非表示は…」「非表示は保存されず…」の 2 行を次に置き換える。

```markdown
- 非表示は通常の表示・スクリーンショット・連番画像出力のすべてに効きます。SceneView とサブカメラには影響しません
- 状態は保存され、次回の起動やエディタの再有効化でも引き継がれます。タイムライン操作ウィンドウの表示トグルと同じ状態です
- `エフェクト` は PostEffects.Plugin のポストエフェクトと、ステージライト・ステージレーザー・サイリウムを隠します。ゲーム本来の Bloom・被写界深度は残ります。OFF でも、タイムラインでポストエフェクトや各ライブ演出のレイヤーを選択している間は、その種類だけ表示されます（タイムラインのウィンドウを閉じていても選択は残ります）
- `ギズモ` はメニューバーの `ボーン表示` と同じ状態です。編集モード中だけ、ギズモ・骨格線・ドラッグ点を表示します
- プリセット・タイムラインのサムネイルには背景・メイド・モデル・ライブ演出が写ります。ポストエフェクトは `エフェクト` が OFF なら掛かりません
```

- [ ] **Step 2: `scene-view.md`**

16 行目の内容を「背景 / メイド / モデル / エフェクト / ギズモ の描画切替、平行投影の切替、追従（オートフォーカス）の切替、ギズモの軸空間の切替」にする。表の直後に次の段落を足す。

```markdown
`エフェクト` はステージライト・ステージレーザー・サイリウムの表示を切り替えます。SceneView にはもともとポストエフェクトが掛かりません。
`ギズモ` はギズモ・ドラッグ点に加えて、編集モード中の骨格線・ボーン回転ギズモ・指のドラッグ点も切り替えます。メニューバーの `ボーン表示` は SceneView には影響しません。
```

83 行目の「GameView は編集モード中で `ボーン表示` が ON のときだけ表示されます」はそのまま（GameView の条件は変わらない）。

- [ ] **Step 3: `maid-editing.md` の表**

36-44 行の表と注記を置き換える。

```markdown
| 表示するもの | 表示される条件 |
|---|---|
| SceneView のドラッグ点とギズモ | SceneView ツールバーの `ギズモ` が ON |
| SceneView の骨格線・ボーン回転ギズモ・指のドラッグ点 | 編集モード中で SceneView ツールバーの `ギズモ` が ON |
| GameView のドラッグ点・ギズモ・骨格線・ボーン回転ギズモ・指のドラッグ点 | 編集モード中で `ボーン表示` トグルが ON |

`ボーン表示` トグルは既定で ON で、状態は保存されます。GameView ツールバー・タイムライン操作ウィンドウの `ギズモ` と同じ状態で、GameView にだけ効きます。
```

`getting-started.md:26` の説明を「GameView のメイドのドラッグ点と装着アイテムの骨格線の表示（SceneView はツールバーの `ギズモ` で切り替え）」にする。

- [ ] **Step 4: `timeline/control.md`**

表の `メイド表示` / `モデル表示` / `背景表示` / `ポスプロ同期` の行を次の並びに置き換える（`自動登録` と `カメラ同期` の間）。

```markdown
| `背景表示` | GameView の背景の表示 / 非表示 |
| `メイド表示` | GameView の全メイドの表示 / 非表示 |
| `モデル表示` | GameView の配置モデルの表示 / 非表示 |
| `エフェクト表示` | GameView のポストエフェクト・ライブ演出の表示 / 非表示。OFF でも、ポストエフェクトや各ライブ演出のレイヤーを選択している間はその種類だけ表示します |
| `ギズモ表示` | GameView のギズモ・ドラッグ点・骨格線の表示 / 非表示（メニューバーの `ボーン表示` と同じ） |
```

表の後に次の段落を足す。

```markdown
`背景表示` から `ギズモ表示` までは GameView ツールバーのトグルと同じ状態です。GameView にだけ効き（SceneView は SceneView のツールバーで切り替えます）、タイムラインではなく設定に保存されます。
```

「`ポスプロ同期` も同様に、ポストエフェクトレイヤーが存在するときだけ表示されます。」の行を削除する。

- [ ] **Step 5: `timeline/settings.md` と `layers-background.md`**

- `settings.md:26` の `地面色表示を背景表示と連動` の行を削除する
- `settings.md:32` の「フレームレート・胸物理・ループ・背景表示・地面連動などを既定に戻す」を「フレームレート・胸物理・ループなどを既定に戻す」にする
- `layers-background.md:22` の地面連動の箇条書きを削除する

- [ ] **Step 6: `W:\COM3D2_5\work\CLAUDE.md` に互換の注記を足す**

「タイムライン XML の互換方向」の箇条書きの末尾に追加する。

```markdown
- 背景表示（`IsBackgroundVisible`）と地面色の背景連動（`IsGroundLinkedToBackground`）はタイムライン XML から外した。表示トグルは GameView の設定（Config）に移り、地面連動は廃止。version は上げていない。旧 XML の要素は読み飛ばされ、背景非表示・連動 ON の設定は失われる。SE で保存した XML を MTE で読むと要素が無いため背景表示 ON・連動 OFF として読まれる。タイムラインの Config の `isPostEffectSync`（ポスプロ同期）も廃止し、GameView のエフェクト表示に統合した
```

- [ ] **Step 7: spec の項目名を直す**

spec の `boneVisible` を `gameViewShowGizmo` に置き換える（2 箇所）。

- [ ] **Step 8: Commit**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site docs/superpowers/specs/2026-10-07-gameview-view-toggles-design.md
git commit -m "docs(docs-site): 表示トグルの整理を反映する"
```

`W:\COM3D2_5\work\CLAUDE.md` は SceneEditor リポジトリの外。`git -C /w/COM3D2_5/work status` でリポジトリ管理下かを確かめ、管理下ならそこで commit する。管理外なら保存だけして報告する。

---

### Task 9: 実機確認

**Files:** なし（確認のみ）

- [ ] **Step 1: DLL を実機へ反映する**

`com3d25-devbridge:restart-verify` スキルに従い、ゲームを終了 → SceneEditor と PostEffects.Plugin の COM3D25 構成の DLL を `W:\COM3D2_5\Sybaris\UnityInjector\` へ反映 → 起動 → セーブをロード → デイリー画面でエディタを有効にする。DLL のコピー方法はスキルの手順に従う（`debug.bat` はゲーム停止中だと実機へコピーするので、スキルがそれを使うなら問題ない）。

- [ ] **Step 2: 確認項目**

devbridge の `screenshot` / `eval_csharp` で確かめる。

1. GameView の背景・メイド・モデルを OFF → GameView だけから消え、SceneView には映る
2. GameView のエフェクトを OFF → PostEffects のパラフィン・リムライトも消える。ON で元に戻る
3. エフェクト OFF のまま、タイムラインでポストエフェクトレイヤー → ステージライトレイヤーをカレントにすると、その種類だけ GameView に出る
4. SceneView のエフェクト OFF → ライブ演出が SceneView だけから消える
5. タイムライン操作ウィンドウと GameView のトグルが連動する（エフェクト・ギズモ含む）
6. ボーン表示 OFF → 編集モード中でも GameView に骨が出ない。SceneView のギズモ ON なら骨格線・白丸が出て、白丸を掴める。SceneView のギズモ OFF で SceneView からも消える
7. エディタの無効化・再有効化、ゲーム再起動後も GameView・SceneView のトグルの状態が残る
8. 背景非表示・地面連動 ON で保存された旧タイムライン XML がエラーなく読める
9. 3 ウィンドウの表示トグルの並びが 背景 / メイド / モデル / エフェクト / ギズモ
10. メイドを隠したとき、キャラの影の複製（`CharacterShadowProxyManager`）が GameView に残らない。残る場合はユーザーに報告して対応を相談する

結果（スクリーンショットを含む）をユーザーへ報告する。

## レビュー却下メモ

- `SetSuspended` の例外時に毎フレーム再試行してログが連発する — 誤検知。`LogHostError` はメンバーごとに 1 回しか出さない（`_errorLoggedMembers`）
- サムネイル撮影の間だけ一時停止を解除する — OnRenderImage 系は LateUpdate の Restore で無効化済みのため、同期 Render の直前に解除しても Hub 系だけが写る中途半端な絵になる。spec どおり許容し、docs に「サムネイルにはポストエフェクトが掛からない」と書く
- ステージライトの `Light` コンポーネントの照明が残る — 誤検知。ライブ演出 3 マネージャに `Light` の生成は無い（MeshRenderer だけ）
- ギズモトグル ON で編集モードに入らない — メニューバーの「ボーン表示」と同じ直接代入に揃える。spec の「メニューバーと同じ状態」に合わせた意図的な差
- PostEffects の「有効」トグルの挙動変化を変更履歴に書く — リリース準備（release-prep）の責務
- 消えたまま起動する — spec で合意済みの仕様
- テストの前提（`Config` のルート名・`TimelineXml` のシリアライズ）— 誤検知。`Config` に `[XmlRoot]` は無くルートは `Config`（既存の `ConfigGizmoSpaceMigrationTests` と同じ）。`TimelineXml` は既存テストでもシリアライズしている
