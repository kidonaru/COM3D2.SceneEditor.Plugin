# ウィンドウ内レイヤーゲート（未登録レイヤーの無効表示＋追加ボタン）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン読込中に、各ウィンドウ（およびウィンドウ内タブ）の項目が対応するタイムラインレイヤーに登録されていない場合、項目を無効表示にし、ウィンドウ内の「レイヤーを追加」ボタンからレイヤーを追加できるようにする。

**Architecture:** 共通の静的ドロワー `TimelineLayerGate` を新設し、各ウィンドウの描画入口で `Begin(view, layerType, slotNo)` / `End(view)` で囲む。未登録時は黄色の注意ラベルと追加ボタンを描き、`GUIView` に新設する `forceDisabled` で以降の `SetEnabled(true)` を無効化して確実に操作不能にする。追加は既存の `TimelineManager.ChangeActiveLayer`（作成＋アクティブ化＋履歴登録）に委譲する。タイムライン未読込時は現状の挙動を一切変えない。

**Tech Stack:** C# (.NET 3.5 / 4.7.1 の 2 構成)、Unity IMGUI (`GUIView` in MTEUtils submodule)、xunit (net48)

**Spec:** 本計画冒頭の「要件」節（ブレインストーミングでユーザー確認済み）

## 要件（ユーザー確認済み）

- 対象はタイムライン読込中のみ。未読込時の既存表示・早期 return は変更しない
- ウィンドウ内の追加ボタンは `ChangeActiveLayer` を使い、タイムラインウィンドウのアクティブレイヤーも追加したレイヤーへ切り替える
- レイヤーを持たない項目（髪/スカート重力、BGM、動画、SceneView カメラ、表情プリセット以外の純粋な非レイヤー項目）は現状維持
- Inspector / Hierarchy ウィンドウは今回対象外

## Global Constraints

- コメント・ログ文言は日本語
- COM3D2 (.NET 3.5) と COM3D25 (.NET 4.7.1) の両構成でビルドを通す。`Func<>`/`Action<>` は入力 4 個まで
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild 直叩き
- `GUIView.SetEnabled` はグローバル `GUI.enabled` を書き換えるため、`ComboBoxPopupWindow.instance.ProcessFocus` の前に必ず有効へ戻す
- レイヤー追加時の履歴登録は `ChangeActiveLayer` 内で行われるので、ウィンドウ側で `RequestHistory` を追加しない
- MTEUtils は submodule（`source/COM3D2.SceneEditor.Plugin/MTEUtils`、branch master）。変更は submodule 側でコミットしてから親でポインタを更新する

## ビルド・テストコマンド（Git Bash）

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" /v:m /nologo \
&& "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test --nologo -v q
```

順番は必ず COM3D2 → COM3D25 → test（COM3D2 ビルドが COM3D25 の DLL を消すため）。

## ファイル構成

| ファイル | 役割 |
|---|---|
| Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs` | `forceDisabled` フィールド追加。`SetEnabled`/`BeginEnabled` が強制無効を尊重する |
| Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateText.cs` | Unity 非依存の純粋ロジック（状態判定・文言生成）。テスト対象 |
| Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGate.cs` | GUI ドロワー `Begin`/`End`。`TimelineManager` に問い合わせ、ラベル・ボタン描画と `forceDisabled` の切替 |
| Create: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs` | 純粋ロジックのテスト |
| Modify: `MaidWindowBase.cs` | `DrawContent` で `DrawMaidContent` を try/finally で囲み `TimelineLayerGate.End(view)` を保証 |
| Modify: 各ウィンドウ（下表） | 描画入口へ `Begin` を挿入 |

### ウィンドウ / タブ → レイヤー対応表（挿入箇所）

| ウィンドウ (タブ) | レイヤー型 | slotNo | 挿入箇所 |
|---|---|---|---|
| MaidPoseWindow | `MotionTimelineLayer` | メイド | `DrawMaidContent` の非表示チェック後 |
| MaidFingerWindow（全タブ） | `MotionTimelineLayer` | メイド | `DrawHeader` 後、`DrawInnerTabs` 前 |
| MaidIKWindow | `MotionTimelineLayer` | メイド | `DrawMaidContent` 冒頭 null チェック後 |
| MaidFaceWindow（目/眉/口/オプション/プリセット） | `MorphTimelineLayer` | メイド | `DrawInnerTabs` 後の分岐前 |
| MaidFaceWindow（視線） | `EyesTimelineLayer` | メイド | 同上（タブで型を切替） |
| MaidUndressWindow | `UndressTimelineLayer` | メイド | body0 チェック後 |
| SoundWindow（ボイス） | `VoiceTimelineLayer` | メイド | `DrawVoice` の timeline/maidCache チェック後 |
| SoundWindow（効果音） | `SeTimelineLayer` | – | `DrawSe` の timeline チェック後 |
| MaterialEditWindow（メイド/モデル/背景） | `MaidMaterialTimelineLayer`（全メイド共用、hasSlotNo=false） / `ModelMaterialTimelineLayer` / `BGModelMaterialTimelineLayer` | – | `DrawBody` の `DrawInnerTabs` 後（メイドタブは `DrawMaidSelector` の後） |
| ShapeKeyEditWindow（メイド/モデル） | `ShapeKeyTimelineLayer` / `ModelShapeKeyTimelineLayer` | メイド / – | `DrawBody` の分岐内 |
| BoneEditWindow（メイド/モデル） | `MotionTimelineLayer` / `ModelBoneTimelineLayer` | メイド / – | `DrawMaidContent` の分岐内 |
| LightWindow | `LightTimelineLayer` | – | 区切り線後、スクロールビュー前 |
| CameraWindow（Main / Sub） | `CameraTimelineLayer` / `SubCameraTimelineLayer` | – | `DrawTargetRow` 後の分岐内 |
| BackgroundWindow（背景: 背景切替） | `BGTimelineLayer` | – | `DrawBgTab` の `DrawCurrentBgRow` と一覧を囲む |
| BackgroundWindow（背景: 背景色 / 地面） | `BGColorTimelineLayer` | – | `DrawBgColorRow` と `DrawGroundRows` を囲む |
| BackgroundWindow（モデル） | `BGModelTimelineLayer` | – | 既存の timeline null チェック後 |
| PngPlacementWindow（両タブ） | `PngPlacementTimelineLayer` | – | `DrawTabs` 後 |
| TextWindow | `TextTimelineLayer` | – | `DrawBody` 冒頭 |
| LiveEffectWindow（ライト/レーザー/サイリウム） | `StageLightTimelineLayer` / `StageLaserTimelineLayer` / `PsylliumTimelineLayer` | – | `DrawTopTabs` の分岐内 |

対象外: SoundWindow BGM、MaidGravityWindow、VideoWindow、CameraWindow SceneView、Hierarchy/Inspector、TimelineSettingWindow。

---

### Task 1: GUIView に強制無効フラグを追加（MTEUtils submodule）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs:326` 付近、`:833-847`

**Interfaces:**
- Produces: `public bool forceDisabled` フィールド。true の間は `SetEnabled(true)` / `BeginEnabled(true)` を呼んでも `guiEnabled` が true にならない

背景: 各ウィンドウは描画途中で `view.SetEnabled(view.focusedComboBox == null)` や `BeginAutoEditMode()`（内部で `SetEnabled`）を何度も呼ぶ。ゲート冒頭で `SetEnabled(false)` しただけではこれらに上書きされて無効化が解けるため、`GUIView` 側に「強制無効」を持たせて後続の再有効化を無視させる。

- [ ] **Step 1: フィールドと SetEnabled/BeginEnabled の変更**

`GUIView.cs:326` の `public bool guiEnabled = true;` の直後に追加:

```csharp
        /// <summary>
        /// 強制無効。true の間は SetEnabled(true) / BeginEnabled(true) を呼んでも有効にならない。
        /// 描画途中で SetEnabled(focusedComboBox == null) 等が何度も呼ばれる区間を、
        /// 外側から一括で無効化したいときに使う。解除する側が必ず false へ戻すこと。
        /// このフラグは子ビューへ伝播しない。子ビュー (GUIComboBox の _buttonSubView 等) は
        /// parent 代入時に親の guiEnabled を継承するので、子に直接 SetEnabled(true) を
        /// 呼ばない限り無効のまま保たれる
        /// </summary>
        public bool forceDisabled = false;
```

`SetEnabled` / `BeginEnabled` を次のように変更:

```csharp
        public void SetEnabled(bool enabled)
        {
            this.guiEnabled = enabled && !forceDisabled;
            EndEnabled();
        }

        public void BeginEnabled(bool enabled)
        {
            enabled = enabled && !forceDisabled;
            if (enabled) return;

            if (enabled != guiEnabled)
            {
                GUI.enabled = enabled;
            }
        }
```

- [ ] **Step 2: submodule 側で両構成ビルドが通ることを確認**

「ビルド・テストコマンド」の COM3D2 → COM3D25 を実行。Expected: エラー 0。

- [ ] **Step 3: submodule でコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git add GUIView.cs
git commit -m "feat(gui): GUIView に強制無効フラグ forceDisabled を追加する"
```

親リポジトリ側のポインタ更新は Task 9 でまとめて行う。

---

### Task 2: 純粋ロジック `TimelineLayerGateText`（TDD）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateText.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs`

**Interfaces:**
- Produces:
  - `enum TimelineLayerGateState { NoTimeline, MaidNotFound, Missing, Ready }`
  - `static TimelineLayerGateState TimelineLayerGateText.Resolve(bool timelineLoaded, bool needsMaid, bool maidFound, bool layerExists)`
  - `static string TimelineLayerGateText.NoticeText(string displayName)` → `"「<name>」レイヤーが未登録のためタイムラインに記録されません"`
  - `static string TimelineLayerGateText.AddButtonText(string displayName)` → `"「<name>」レイヤーを追加"`
  - `const string TimelineLayerGateText.MaidNotFoundText = "タイムライン側の対象メイドが見つかりません"`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerGateTextTests
    {
        [Fact]
        public void タイムライン未読込なら常にNoTimeline()
        {
            Assert.Equal(TimelineLayerGateState.NoTimeline,
                TimelineLayerGateText.Resolve(false, false, false, false));
            // レイヤーが存在していても未読込が優先される
            Assert.Equal(TimelineLayerGateState.NoTimeline,
                TimelineLayerGateText.Resolve(false, true, true, true));
        }

        [Fact]
        public void メイドが必要で見つからなければMaidNotFound()
        {
            Assert.Equal(TimelineLayerGateState.MaidNotFound,
                TimelineLayerGateText.Resolve(true, true, false, false));
            // メイド不要なら maidFound は無視される
            Assert.Equal(TimelineLayerGateState.Missing,
                TimelineLayerGateText.Resolve(true, false, false, false));
        }

        [Fact]
        public void レイヤーの有無でMissingとReadyが分かれる()
        {
            Assert.Equal(TimelineLayerGateState.Missing,
                TimelineLayerGateText.Resolve(true, true, true, false));
            Assert.Equal(TimelineLayerGateState.Ready,
                TimelineLayerGateText.Resolve(true, true, true, true));
        }

        [Fact]
        public void 文言にレイヤー表示名が埋まる()
        {
            Assert.Equal("「メイド表情」レイヤーが未登録のためタイムラインに記録されません",
                TimelineLayerGateText.NoticeText("メイド表情"));
            Assert.Equal("「メイド表情」レイヤーを追加",
                TimelineLayerGateText.AddButtonText("メイド表情"));
        }
    }
}
```

- [ ] **Step 2: テストが失敗（コンパイルエラー）することを確認**

「ビルド・テストコマンド」を実行。Expected: テストプロジェクトが `TimelineLayerGateText` 未定義でコンパイル失敗。

- [ ] **Step 3: 実装**

`source/COM3D2.SceneEditor.Plugin/TimelineLayerGateText.cs`:

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ウィンドウ内レイヤーゲートの判定結果</summary>
    public enum TimelineLayerGateState
    {
        /// <summary>タイムライン未読込。ゲートは何もしない（従来表示のまま）</summary>
        NoTimeline,
        /// <summary>メイド単位のレイヤーだが、対象メイドがタイムライン側に無い</summary>
        MaidNotFound,
        /// <summary>レイヤー未登録。無効表示＋追加ボタン</summary>
        Missing,
        /// <summary>レイヤー登録済み。通常表示</summary>
        Ready,
    }

    /// <summary>
    /// ウィンドウ内レイヤーゲートの状態判定と文言。
    /// Unity 非依存にしてユニットテスト可能にしている
    /// </summary>
    public static class TimelineLayerGateText
    {
        public const string MaidNotFoundText = "タイムライン側の対象メイドが見つかりません";

        public static TimelineLayerGateState Resolve(
            bool timelineLoaded, bool needsMaid, bool maidFound, bool layerExists)
        {
            if (!timelineLoaded) return TimelineLayerGateState.NoTimeline;
            if (needsMaid && !maidFound) return TimelineLayerGateState.MaidNotFound;
            return layerExists ? TimelineLayerGateState.Ready : TimelineLayerGateState.Missing;
        }

        public static string NoticeText(string displayName)
        {
            return "「" + displayName + "」レイヤーが未登録のためタイムラインに記録されません";
        }

        public static string AddButtonText(string displayName)
        {
            return "「" + displayName + "」レイヤーを追加";
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

「ビルド・テストコマンド」を実行。Expected: 新規 4 件を含め全テスト PASS。

- [ ] **Step 5: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/TimelineLayerGateText.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs
git commit -m "feat(timeline): ウィンドウ内レイヤーゲートの状態判定と文言を追加する"
```

---

### Task 3: GUI ドロワー `TimelineLayerGate`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGate.cs`

**Interfaces:**
- Consumes: Task 1 の `GUIView.forceDisabled`、Task 2 の `TimelineLayerGateText`、`MTEP.TimelineManager.instance.timeline / GetLayer(Type,int) / GetLayerInfo(Type) / ChangeActiveLayer(Type,int)`、`MTEP.MaidManager.instance.GetMaidCache(Maid)`
- Produces:
  - `static void TimelineLayerGate.Begin(GUIView view, Type layerType, float rowHeight)` — メイド非依存レイヤー用（`hasSlotNo == false` のレイヤーは全てこちら）
  - `static void TimelineLayerGate.Begin(GUIView view, Type layerType, Maid maid, float rowHeight)` — `hasSlotNo == true` のメイド単位レイヤー用。slotNo は `MaidCache.slotNo` から解決
  - 戻り値は持たない。呼び出し側は Begin の後もそのまま項目を描き続ける（要件は「隠す」ではなく「無効表示」）
  - `static void TimelineLayerGate.End(GUIView view)` — `forceDisabled` を解除して有効へ戻す。何度呼んでも安全（冪等）

- [ ] **Step 1: 実装**

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ウィンドウ内レイヤーゲート。
    /// タイムライン読込中に対象レイヤーが未登録なら、注意ラベルと追加ボタンを描いて
    /// 以降の項目を強制無効にする。追加は ChangeActiveLayer に委譲し、タイムライン
    /// ウィンドウのアクティブレイヤーも追加先へ切り替える（履歴登録も同メソッド内）。
    /// タイムライン未読込時は何も描かず true を返し、従来表示に一切干渉しない。
    ///
    /// Begin で無効化したら、同じ描画パス内で必ず End を呼ぶこと。
    /// SetEnabled はグローバル GUI.enabled を書き換えるため、戻し忘れると
    /// 後に描かれる ComboBoxPopupWindow まで操作できなくなる
    /// </summary>
    public static class TimelineLayerGate
    {
        private const float BUTTON_WIDTH = 220f;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        /// <summary>メイド非依存レイヤー用。呼び出し後もそのまま項目を描き続ける（無効表示にするだけで隠さない）</summary>
        public static void Begin(GUIView view, Type layerType, float rowHeight)
        {
            var timelineLoaded = timelineManager.timeline != null;
            var layerExists = timelineLoaded && timelineManager.GetLayer(layerType, 0) != null;
            var state = TimelineLayerGateText.Resolve(timelineLoaded, false, false, layerExists);
            Apply(view, layerType, 0, state, rowHeight);
        }

        /// <summary>
        /// メイド単位レイヤー (hasSlotNo == true) 用。slotNo はタイムライン側の MaidCache から引く。
        /// 対象メイドがタイムライン側に無い場合はレイヤーを作れないので、
        /// 追加ボタンは出さず案内だけ出して無効化する
        /// </summary>
        public static void Begin(GUIView view, Type layerType, Maid maid, float rowHeight)
        {
            var timelineLoaded = timelineManager.timeline != null;
            var maidCache = maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            var slotNo = maidCache != null ? maidCache.slotNo : 0;
            var layerExists = timelineLoaded && maidCache != null
                && timelineManager.GetLayer(layerType, slotNo) != null;
            var state = TimelineLayerGateText.Resolve(
                timelineLoaded, true, maidCache != null, layerExists);
            Apply(view, layerType, slotNo, state, rowHeight);
        }

        /// <summary>強制無効を解除して有効へ戻す。冪等</summary>
        public static void End(GUIView view)
        {
            view.forceDisabled = false;
            view.SetEnabled(true);
        }

        private static void Apply(
            GUIView view, Type layerType, int slotNo, TimelineLayerGateState state, float rowHeight)
        {
            switch (state)
            {
                case TimelineLayerGateState.NoTimeline:
                case TimelineLayerGateState.Ready:
                    return;

                case TimelineLayerGateState.MaidNotFound:
                    view.DrawLabel(TimelineLayerGateText.MaidNotFoundText, -1, rowHeight,
                        textColor: Color.yellow);
                    Disable(view);
                    return;

                case TimelineLayerGateState.Missing:
                default:
                    DrawMissing(view, layerType, slotNo, rowHeight);
                    Disable(view);
                    return;
            }
        }

        private static void DrawMissing(GUIView view, Type layerType, int slotNo, float rowHeight)
        {
            var info = timelineManager.GetLayerInfo(layerType);
            var displayName = info != null ? info.displayName : layerType.Name;

            view.DrawLabel(TimelineLayerGateText.NoticeText(displayName), -1, rowHeight,
                textColor: Color.yellow);

            // ボタンは強制無効の前に描く（押せる必要がある）
            if (view.DrawButton(TimelineLayerGateText.AddButtonText(displayName), BUTTON_WIDTH, rowHeight))
            {
                timelineManager.ChangeActiveLayer(layerType, slotNo);
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
        }

        private static void Disable(GUIView view)
        {
            view.forceDisabled = true;
            view.SetEnabled(false);
        }
    }
}
```

- [ ] **Step 2: 両構成ビルド**

「ビルド・テストコマンド」を実行。Expected: エラー 0、既存テスト PASS。

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineLayerGate.cs
git commit -m "feat(timeline): 未登録レイヤーを無効表示し追加ボタンを出す TimelineLayerGate を追加する"
```

---

### Task 4: MaidWindowBase で End を保証し、単一レイヤーのメイド系ウィンドウへ適用

**Files:**
- Modify: `MaidWindowBase.cs:176-189` (`DrawContent`)
- Modify: `MaidPoseWindow.cs:136-150`
- Modify: `MaidIKWindow.cs:70-76`
- Modify: `MaidUndressWindow.cs:58-71`
- Modify: `MaidFingerWindow.cs:120-135`

**Interfaces:**
- Consumes: Task 3 の `TimelineLayerGate.Begin(view, Type, Maid, float)` / `End(view)`

- [ ] **Step 1: MaidWindowBase.DrawContent で End を保証**

```csharp
        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            view.parent = _rootView;
            view.Init(ToLocalRect(contentRect));

            var target = showMaidSelector ? DrawMaidSelector(view) : maidManager.targetMaid;

            try
            {
                DrawMaidContent(target);
            }
            finally
            {
                // レイヤーゲートで強制無効にした状態を、派生クラスの早期 return や例外に
                // 関わらずここで必ず解く。解かないと後に描く ComboBoxPopupWindow まで操作不能になる
                TimelineLayerGate.End(view);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す。
            // 派生クラスの早期 return で呼び忘れないよう、この基底クラスが必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }
```

- [ ] **Step 2: MaidPoseWindow**

非表示チェックの直後（`return;` の後）に挿入:

```csharp
            // 未登録なら無効表示＋追加ボタン。項目自体は見せたいので return しない
            TimelineLayerGate.Begin(view, typeof(MotionTimelineLayer), target, ROW_HEIGHT);
```

`MotionTimelineLayer` の名前空間が `using` に無ければ `MTEP.MotionTimelineLayer` にする（ファイル冒頭の `using MTEP = ...` の有無を確認）。以降のタスクも同様。

- [ ] **Step 3: MaidIKWindow**

`if (target == null) return;` の直後に挿入:

```csharp
            TimelineLayerGate.Begin(view, typeof(MotionTimelineLayer), target, ROW_HEIGHT);
```

- [ ] **Step 4: MaidUndressWindow**

body0 チェックの直後、`DrawAllButtons` の前に挿入:

```csharp
            TimelineLayerGate.Begin(view, typeof(UndressTimelineLayer), target, ROW_HEIGHT);
```

- [ ] **Step 5: MaidFingerWindow**

`DrawHeader(view, target);` の直後に挿入。プリセットタブの `LoadPreset` は `MaidFingerBlendController` へ指ブレンドを直接書く（`MaidFingerPresetManager.cs:128`）ので、スライダーと同じく Motion レイヤーが無いと記録されない。タブを問わず判定する:

```csharp
            TimelineLayerGate.Begin(view, typeof(MotionTimelineLayer), target, ROW_HEIGHT);
```

- [ ] **Step 6: 両構成ビルド＋テスト**

Expected: エラー 0、全テスト PASS。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidWindowBase.cs source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs source/COM3D2.SceneEditor.Plugin/MaidUndressWindow.cs source/COM3D2.SceneEditor.Plugin/MaidFingerWindow.cs
git commit -m "feat(maid): ポーズ/IK/脱衣/指ウィンドウに未登録レイヤーのゲートを追加する"
```

---

### Task 5: タブでレイヤーが変わるメイド系ウィンドウへ適用

**Files:**
- Modify: `MaidFaceWindow.cs:139-165`
- Modify: `SoundWindow.cs:370-425`（`DrawVoice` / `DrawSe`）
- Modify: `MaterialEditWindow.cs:106-150`
- Modify: `ShapeKeyEditWindow.cs:106-145`
- Modify: `BoneEditWindow.cs:224-270`

**Interfaces:**
- Consumes: Task 3 の `Begin` 両オーバーロード

- [ ] **Step 1: MaidFaceWindow**

`_tab = DrawInnerTabs(_tab, 70);` の直後に挿入:

```csharp
            // 視線タブは瞳レイヤー、それ以外（表情・プリセット）は表情レイヤーへ記録される。
            // プリセット適用 (MaidFacePresetManager.Apply) もモーフ値を直接書くのでスライダーと同じ扱い
            var layerType = _tab == FaceTab.視線
                ? typeof(MTEP.EyesTimelineLayer)
                : typeof(MTEP.MorphTimelineLayer);
            TimelineLayerGate.Begin(view, layerType, target, ROW_HEIGHT);
```

`DrawTimelineLookSection` / `DrawEyesPosSection` の既存「タイムライン未読込」ラベルはそのまま残す（未読込時の挙動は不変）。

- [ ] **Step 2: SoundWindow ボイス**

`DrawVoice` の `if (timeline == null || maidCache == null) { ... return; }` の直後に挿入:

```csharp
            TimelineLayerGate.Begin(view, typeof(MTEP.VoiceTimelineLayer), target, ROW_HEIGHT);
```

- [ ] **Step 3: SoundWindow 効果音**

`DrawSe` の `if (timeline == null) { ... return; }` の直後、`view.SetEnabled(view.focusedComboBox == null);` の前に挿入:

```csharp
            TimelineLayerGate.Begin(view, typeof(MTEP.SeTimelineLayer), ROW_HEIGHT);
```

- [ ] **Step 4: MaterialEditWindow**

既存の `try { DrawBody } finally { view.SetEnabled(true); }` は基底の `End` が同じことをするので削除し、`DrawMaidContent` を `DrawBody(target);` の 1 行にする。`DrawBody` を次のように変更:

```csharp
        private void DrawBody(Maid target)
        {
            _targetTab = DrawInnerTabs(_targetTab, TAB_WIDTH);

            switch (_targetTab)
            {
                case TargetTabType.メイド:
                    DrawMaidMaterial(target);
                    break;
                case TargetTabType.モデル:
                    TimelineLayerGate.Begin(view, typeof(MTEP.ModelMaterialTimelineLayer), ROW_HEIGHT);
                    DrawModelMaterial();
                    break;
                case TargetTabType.背景:
                    TimelineLayerGate.Begin(view, typeof(MTEP.BGModelMaterialTimelineLayer), ROW_HEIGHT);
                    DrawBGModelMaterial();
                    break;
            }
        }
```

`DrawMaidMaterial` の `if (target == null) return;` の直後に挿入。メイドマテリアルレイヤーは `hasSlotNo == false` の全メイド共用 1 インスタンスで、`GetLayer` は slotNo を見ない。メイド版 `Begin` を使うと slotNo を渡す意味が無いので非メイド版を使う:

```csharp
            // メイドマテリアルレイヤーは全メイド共用 (hasSlotNo == false) なのでメイドを渡さない
            TimelineLayerGate.Begin(view, typeof(MTEP.MaidMaterialTimelineLayer), ROW_HEIGHT);
```

- [ ] **Step 5: ShapeKeyEditWindow**

MaterialEditWindow と同様に `try/finally` を外して `DrawBody(target);` のみにする。`DrawBody` のモデル分岐と、メイド側の maidCache 取得前に挿入:

```csharp
            if (_targetTab == TargetTabType.モデル)
            {
                TimelineLayerGate.Begin(view, typeof(MTEP.ModelShapeKeyTimelineLayer), ROW_HEIGHT);
                DrawModelContent();
                return;
            }
            // ... DrawMaidSelector / body0 チェック（既存） ...
            TimelineLayerGate.Begin(view, typeof(MTEP.ShapeKeyTimelineLayer), target, ROW_HEIGHT);
            var maidCache = timelineMaidManager.GetMaidCache(target);
```

- [ ] **Step 6: BoneEditWindow**

`if (boneEditManager.isModelMode)` 分岐と、メイド側の `IsAllProcPropBusy` チェック後に挿入:

```csharp
            if (boneEditManager.isModelMode)
            {
                TimelineLayerGate.Begin(view, typeof(MTEP.ModelBoneTimelineLayer), ROW_HEIGHT);
                DrawModelContent();
                return;
            }
            // ... 既存チェック ...
            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), target, ROW_HEIGHT);

            // スロット選択はプリセットの適用先も兼ねるため、タブの上に共通で置く
            DrawHeaderRow(target);
```

- [ ] **Step 7: 両構成ビルド＋テスト**

Expected: エラー 0、全テスト PASS。

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs source/COM3D2.SceneEditor.Plugin/SoundWindow.cs source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs
git commit -m "feat(maid): 表情/サウンド/マテリアル/シェイプ/ボーン編集ウィンドウにタブ別のレイヤーゲートを追加する"
```

---

### Task 6: EditorSubWindow 系（ライト/カメラ/テキスト/PNG 配置）へ適用

**Files:**
- Modify: `LightWindow.cs:92-123`
- Modify: `CameraWindow.cs:159-184`
- Modify: `TextWindow.cs:90-110`
- Modify: `PngPlacementWindow.cs:170-183`

これらは基底に `End` 保証が無いので、各 `DrawContent` で try/finally を書く。

- [ ] **Step 1: LightWindow**

```csharp
        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            try
            {
                TimelineLayerGate.Begin(_view, typeof(MTEP.LightTimelineLayer), ROW_HEIGHT);
                DrawBody();
            }
            finally
            {
                // 強制無効のまま抜けると ComboBoxPopupWindow まで操作できなくなるため必ず戻す
                TimelineLayerGate.End(_view);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawBody()
        {
            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // GetComponent を挟むため 1 描画につき 1 回だけ引いて使い回す
            var mainLight = LightRowDrawer.MainLightComponent;
            // 未選択・選択中のライトが消えた場合はメインライトを既定の編集対象にする
            var selectedLight = _selectedLight != null ? _selectedLight : mainLight;

            DrawLightListSection(mainLight, selectedLight);

            // 一覧での追加・削除を待たずに編集欄へ反映する（同じフレームで対象が変わる）
            selectedLight = _selectedLight != null ? _selectedLight : mainLight;

            if (selectedLight != null)
            {
                _view.DrawHorizontalLine();
                DrawLightEditSection(selectedLight, mainLight);
            }

            _view.EndScrollView();
        }
```

`ROW_HEIGHT` が LightWindow に無ければ既存の行高定数名に合わせる（`grep -n 'ROW_HEIGHT\|rowHeight' LightWindow.cs` で確認）。

- [ ] **Step 2: CameraWindow**

`DrawTargetRow();` 以降の分岐を try/finally で囲み、Main / Sub にゲートを入れる。SceneView（`_targetIndex == 1`）はレイヤーが無いので触らない:

```csharp
            DrawTargetRow();

            try
            {
                if (_targetIndex == 0)
                {
                    TimelineLayerGate.Begin(_view, typeof(MTEP.CameraTimelineLayer), ROW_HEIGHT);
                    // プリセットは Main カメラ専用のため他タブでは行を出さない
                    DrawPresetRow();
                    DrawMainCameraContent();
                }
                else if (_targetIndex == 1)
                {
                    DrawSceneViewCameraContent();
                }
                else if (_targetIndex == 2)
                {
                    TimelineLayerGate.Begin(_view, typeof(MTEP.SubCameraTimelineLayer), ROW_HEIGHT);
                    DrawSubCameraContent();
                }
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
```

- [ ] **Step 3: TextWindow**

`DrawContent` の `DrawBody();` を try/finally で囲み、`DrawBody` 冒頭の `_view.SetEnabled(_view.focusedComboBox == null);` の前に挿入:

```csharp
            TimelineLayerGate.Begin(_view, typeof(MTEP.TextTimelineLayer), ROW_HEIGHT);
```

```csharp
            try
            {
                DrawBody();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
```

- [ ] **Step 4: PngPlacementWindow**

```csharp
        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            DrawTabs();

            try
            {
                // 画像タブ（配置）も配置済みタブも PNG 配置レイヤーへ記録される
                TimelineLayerGate.Begin(_view, typeof(MTEP.PngPlacementTimelineLayer), ROW_HEIGHT);

                if (_tab == PngTab.配置済み)
                {
                    DrawPlacedTiles();
                }
                else
                {
                    DrawImageTab();
                }
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
        }
```

- [ ] **Step 5: 両構成ビルド＋テスト**

Expected: エラー 0、全テスト PASS。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/LightWindow.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs source/COM3D2.SceneEditor.Plugin/TextWindow.cs source/COM3D2.SceneEditor.Plugin/PngPlacementWindow.cs
git commit -m "feat(window): ライト/カメラ/テキスト/PNG配置ウィンドウに未登録レイヤーのゲートを追加する"
```

---

### Task 7: BackgroundWindow と LiveEffectWindow へ適用

**Files:**
- Modify: `BackgroundWindow.cs:174-275`
- Modify: `LiveEffectWindow.cs:92-135`

- [ ] **Step 1: BackgroundWindow.DrawContent を try/finally に**

```csharp
            try
            {
                DrawBody();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
```

- [ ] **Step 2: 背景タブ（背景切替は BG レイヤー、背景色は BG カラーレイヤー）**

`DrawBgTab` を次のように変更。2 つのレイヤーが 1 タブに混在するので、区間ごとに `Begin`〜`End` を順に置く（入れ子にしない）:

```csharp
        private void DrawBgTab()
        {
            var bgMgr = GameMain.Instance != null ? GameMain.Instance.BgMgr : null;
            if (bgMgr == null)
            {
                _view.DrawLabel("BgMgr が見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            if (!BackgroundUtils.EnsureBgDataLoaded())
            {
                _view.DrawLabel("背景一覧を取得できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            // 現在背景の行は背景レイヤー、背景色は背景色レイヤーへ記録されるため区間ごとに判定する
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGTimelineLayer), ROW_HEIGHT);
            DrawCurrentBgRow(bgMgr);
            TimelineLayerGate.End(_view);

            // 背景色は背景の有無に関わらず編集できる
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGColorTimelineLayer), ROW_HEIGHT);
            BackgroundRowDrawer.DrawBgColorRow(_view, ROW_HEIGHT);
            TimelineLayerGate.End(_view);

            _view.DrawHorizontalLine();
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGTimelineLayer), ROW_HEIGHT);
            DrawFilterRows();
            DrawBgList(bgMgr);
        }
```

- [ ] **Step 3: 地面タブとモデルタブ**

`DrawBody` の switch:

```csharp
                case BgTabType.地面:
                    // 地面は背景色レイヤーが BGGround ごとキー化する（BackgroundRowDrawer の注記と同じ）
                    TimelineLayerGate.Begin(_view, typeof(MTEP.BGColorTimelineLayer), ROW_HEIGHT);
                    BackgroundRowDrawer.DrawGroundRows(_view, LABEL_WIDTH, ROW_HEIGHT);
                    break;
```

`DrawBgModelTab` の既存 timeline null チェックの直後に挿入:

```csharp
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGModelTimelineLayer), ROW_HEIGHT);
```

- [ ] **Step 4: LiveEffectWindow**

`DrawContent` の `DrawTopTabs();` を try/finally で囲み、`DrawTopTabs` の switch にゲートを入れる:

```csharp
            try
            {
                DrawTopTabs();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
```

```csharp
            switch (_topTab)
            {
                case TopTab.ライト:
                    TimelineLayerGate.Begin(_view, typeof(StageLightTimelineLayer), ROW_HEIGHT);
                    DrawStageLight(_view);
                    break;
                case TopTab.レーザー:
                    TimelineLayerGate.Begin(_view, typeof(StageLaserTimelineLayer), ROW_HEIGHT);
                    DrawStageLaser(_view);
                    break;
                case TopTab.サイリウム:
                    TimelineLayerGate.Begin(_view, typeof(PsylliumTimelineLayer), ROW_HEIGHT);
                    DrawPsyllium(_view);
                    break;
            }
```

（`PsylliumTimelineLayer` は既にこのファイルで非修飾で使われているので `using` は揃っている）

- [ ] **Step 5: 両構成ビルド＋テスト**

Expected: エラー 0、全テスト PASS。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs
git commit -m "feat(window): 背景/ライブ演出ウィンドウにタブ別のレイヤーゲートを追加する"
```

---

### Task 8: 実機検証（devbridge）

**Files:** なし（検証のみ）

- [ ] **Step 1: ゲーム起動中ならホットリロード、停止中なら DLL コピー**

ゲーム起動中は CLAUDE.md の「Harmony プラグインのホットリロード」手順は本プラグイン（BaseUnityPlugin + 多数の static）には適用しにくいため、ユーザーにゲーム再起動を依頼する。停止中なら `debug.bat com3d25` 相当のコピーはユーザー操作に委ね、実行しない。

- [ ] **Step 2: 確認項目（MCP `com3d25-devbridge` の `screenshot` / `list_imgui_windows`）**

1. タイムライン未読込: 全ウィンドウが従来通り（注意ラベル・ボタンが出ない）
2. 新規タイムラインで表情ウィンドウを開く: 目タブに黄色ラベル＋「「メイド表情」レイヤーを追加」ボタン、スライダーが灰色で操作不能
3. ボタン押下: レイヤーが追加され、タイムラインウィンドウのアクティブレイヤーが「メイド表情」になり、ウィンドウ側は通常表示に戻る。履歴に「「メイド表情」レイヤー新規作成」が積まれる
4. 表情ウィンドウで視線タブへ切替: 瞳レイヤー未登録なら別のラベルが出る
5. 無効表示中にコンボボックス（例: ライトウィンドウの対象コンボ）を押しても開かない、かつ他ウィンドウのコンボは開ける（`GUI.enabled` の戻し漏れが無い）
6. 背景ウィンドウ 背景タブ: BG レイヤーだけ追加した状態で背景色行だけが無効になる

- [ ] **Step 3: 問題があれば該当タスクへ戻って修正・コミット**

---

### Task 9: submodule ポインタ更新とレビュー

- [ ] **Step 1: 親リポジトリで MTEUtils のポインタを更新してコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(submodule): MTEUtils を更新（GUIView.forceDisabled）"
```

（Task 4 以降のビルドが submodule の変更に依存するため、実際には Task 1 直後に行ってもよい。その場合はここをスキップする）

- [ ] **Step 2: code-review スキルでレビューし、指摘を取り込む**

- [ ] **Step 3: 最終の両構成ビルド＋テストを実行して結果を報告**

---

## 自己レビューメモ

- 要件カバレッジ: 「無効表示」= `forceDisabled` + `SetEnabled(false)`（Task 1, 3）、「追加ボタン」= `DrawMissing`（Task 3）、「ウィンドウ内およびタブ内」= Task 4〜7 の対応表全行。未読込時不変 = `Resolve` の `NoTimeline` で `true` 返却
- 型整合: `Begin(GUIView, Type, float)` / `Begin(GUIView, Type, Maid, float)` / `End(GUIView)` を全タスクで同じシグネチャで使用。`TimelineLayerGateText.Resolve(bool, bool, bool, bool)` の引数順は Task 2 のテストと実装で一致
- 既知の制約: `BackgroundWindow` 背景タブは `Begin`/`End` を区間ごとに順置きしており、入れ子にすると内側の `End` が外側の無効化を解いてしまう。他のウィンドウは 1 描画パスに `Begin` 1 回のみ
- `ROW_HEIGHT` は各ウィンドウの既存定数を使う。無いウィンドウは `grep` で行高の定数名を確認してから置き換える

## レビュー却下メモ

- 「MaterialEditWindow メイドタブでメイド B の編集がメイド A のレイヤーへ誤記録される」 — MaidMaterialTimelineLayer は設計上、全メイド共用の 1 インスタンス（hasSlotNo=false）。取り違えではなく仕様。ただし指摘を受けて非メイド版 Begin へ変更した
- 「BoneEditWindow の try/finally 削除の影響が未確認」 — BoneEditWindow に try/finally は存在せず、計画でも削除対象にしていない（削除は MaterialEdit / ShapeKey のみ）
- 「コンボポップアップ表示中にゲートが Missing へ遷移した場合の挙動」 — 未確認のまま見送り。Task 8 の実機確認項目 5 で挙動を見る
