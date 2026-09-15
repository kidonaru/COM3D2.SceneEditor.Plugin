# パラメータ変更時の編集モード自動移行 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** どのウィンドウでパラメータを変更しても編集モードへ自動で入るようにし、「編集モード中のみ操作可」だった UI を常時操作可にして自動移行へ統一する。

**Architecture:** 自動移行の入口は 1 つ (`AutoEditMode.Enter()`)。呼び出し元は 2 系統で、(1) `HistoryManager.BeforeEdit` (履歴を残す全操作) と (2) `GUIView.onBeforeValueChanged` フック (履歴を残さない演出系の行ドロワーを描く区間を `BeginAutoEditMode()` / `EndAutoEditMode()` で囲む)。`Enter()` は編集モードへ入った直後にタイムライン側の「編集開始スナップショット」を同フレームで取るため、値変更が翌フレームの `ApplyCurrentFrame` に巻き戻されない。

**Tech Stack:** C# (.NET 3.5 / net48 テスト)、Unity IMGUI、MSBuild (COM3D2 / COM3D25 の 2 構成)

**Spec:** ユーザー指示 (本計画冒頭の Goal)。設計判断は下記「Global Constraints」と各タスクの説明に記す。

## Global Constraints

- コードのコメント・ログは日本語。
- **両構成 (COM3D2 → COM3D25) をビルドしてから `dotnet test`** を回す (メモリ `msbuild-from-bash` 参照)。`debug.bat` はゲーム停止中だと実機へ DLL をコピーするため、確認ビルドは MSBuild 直叩きにする。
- `deploy.bat` / `deploy.ps1` は実行しない。
- `MTEUtils/` 配下 (GUIView / GUIComboBox) は MTE 由来の共通コードなので、SE 固有の型 (`AutoEditMode` 等) を参照しない。汎用フック (`Action`) だけを足す。
- 編集モードへ入る副作用 (全メイドのモーション停止、タイムライン再生停止、IK 固定の即時有効化) は既存の `MaidManipulateManager.isEditMode` / `SceneEditorHack.isPoseEditing` の setter に任せ、本計画で挙動を変えない。
- キーフレーム「登録」「ポーズP」ボタン、タイムライン行の A/D ボタンは「パラメータ変更」ではなくスナップショット前提の操作なので、編集モード中のみのままにする (対象外)。
- ボーンギズモ・白丸ドラッグ点の表示条件 (`isBoneEditing`) はウィンドウではないので変えない。

## 現状の整理 (調査結果)

| 種別 | 場所 | 現状 |
| --- | --- | --- |
| 編集モードの実体 | `MaidManipulation/MaidManipulateManager.cs:207` `isEditMode` | setter で全メイド停止・IK 固定開始 |
| タイムライン側の窓口 | `Timeline/Hack/SceneEditorHack.cs:70` `isPoseEditing` | `isEditMode` へ委譲。ON で `isAnmPlaying=false` |
| キャッシュ | `Timeline/Manager/StudioHackManager.cs:15-31, 100-104` | `isPoseEditing` getter は PreUpdate で更新される `_isPoseEditing` を返す |
| スナップショット | `Timeline/Manager/TimelineManager.cs:215-226` | Update で切替を検出し `OnPoseEditStart()` (= `ApplyCurrentFrame(false)` + `OnPoseEditUpdated()`) |
| 履歴の入口 | `Manager/HistoryManager.cs:100-150` `BeforeEdit` 3 種 → `BeforeEditCore` | 38 ファイルから呼ばれる |
| 「編集モード中のみ」ゲート | 下記 Task 4〜7 の一覧 | `SetEnabled(... && isPoseEditing)` か早期 return |

`MaidManipulateManager.isEditMode` の summary にある「ボーンを動かし始めると自動で ON になる」は現状のコードに実装が無い (grep で `isEditMode = true` / `isPoseEditing = true` の呼び出しはトグル以外に存在しない)。本計画で実装される。

## ファイル構成

| ファイル | 役割 |
| --- | --- |
| Create `source/COM3D2.SceneEditor.Plugin/AutoEditMode.cs` | `Enter()`: 編集モードへ入り、タイムラインのスナップショットを同フレームで同期する唯一の入口 |
| Create `source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs` | `BeginAutoEditMode()` / `EndAutoEditMode()`: `GUIView.onBeforeValueChanged` に `AutoEditMode.Enter` を張る区間ヘルパー |
| Modify `Timeline/Manager/StudioHackManager.cs` | `SyncPoseEditing()` 公開 (PreUpdate から抽出) |
| Modify `Timeline/Manager/TimelineManager.cs` | `SyncPoseEditing()` 公開 (Update から抽出) |
| Modify `Manager/HistoryManager.cs` | `BeforeEditCore` 冒頭で `AutoEditMode.Enter()` |
| Modify `MTEUtils/GUIView.cs` | `onBeforeValueChanged` フックと各コールバック直前の通知、サブビューへの伝播 |
| Modify `MTEUtils/GUIComboBox.cs` | 描画時にフックを控え、`onSelected` の直前に呼ぶ |
| Modify ゲートを持つウィンドウ / 行ドロワー / ItemInspector (Task 4〜7) | ゲート撤去 + `BeginAutoEditMode` 区間化 |
| Modify `CHANGELOG.md` | 未リリース欄に追記 |

---

### Task 1: 編集モードへ即時に入る入口 `AutoEditMode.Enter()`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/AutoEditMode.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioHackManager.cs:97-104`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:215-226`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:206` (summary の文言)

**Interfaces:**
- Produces: `COM3D2.SceneEditor.Plugin.AutoEditMode.Enter()` (static, 引数なし, 冪等)
- Produces: `MTEP.StudioHackManager.SyncPoseEditing()` (public), `MTEP.TimelineManager.SyncPoseEditing()` (public)

- [ ] **Step 1: StudioHackManager のキャッシュ更新を `SyncPoseEditing()` に抽出する**

`PreUpdate()` 末尾の `if (mte.isEnable) { ... }` ブロックを以下のメソッド呼び出しに置き換え、メソッドを追加する。

```csharp
        public override void PreUpdate()
        {
            // ... (既存の _studioHack 選定はそのまま)

            SyncPoseEditing();
        }

        /// <summary>
        /// studioHack 側の編集モードをキャッシュへ反映し、変化していれば通知する。
        /// PreUpdate のほか、パラメータ変更で同フレーム中に編集モードへ入ったとき
        /// (AutoEditMode.Enter) にも呼び、以降の描画が古いキャッシュを見ないようにする
        /// </summary>
        public void SyncPoseEditing()
        {
            if (!mte.isEnable)
            {
                return;
            }

            var isPoseEditingNow = _studioHack?.isPoseEditing ?? false;
            if (isPoseEditingNow != _isPoseEditing)
            {
                _isPoseEditing = isPoseEditingNow;
                onPoseEditingChanged?.Invoke(isPoseEditingNow);
            }
        }
```

- [ ] **Step 2: TimelineManager の切替検出を `SyncPoseEditing()` に抽出する**

`Update()` 内の以下のブロックを置き換える。

```csharp
            // 置き換え前
            var isPoseEditing = studioHackManager.isPoseEditing;
            if (isPrevPoseEditing != isPoseEditing)
            {
                if (isPoseEditing)
                {
                    OnPoseEditStart();
                }
                else
                {
                    OnPoseEditEnd();
                }
                isPrevPoseEditing = isPoseEditing;
            }

            if (isPoseEditing && config.disablePoseHistory)
```

```csharp
            // 置き換え後
            SyncPoseEditing();

            var isPoseEditing = studioHackManager.isPoseEditing;
            if (isPoseEditing && config.disablePoseHistory)
```

メソッドを追加する (`OnPoseEditStart` の近くに置く)。

```csharp
        /// <summary>
        /// 編集モードの切り替わりを検出し、開始 / 終了処理を行う。
        /// Update のほか、パラメータ変更で編集モードへ入った直後 (AutoEditMode.Enter) にも呼ぶ。
        /// 翌フレームの Update まで待つと OnPoseEditStart の ApplyCurrentFrame が
        /// 変更したばかりの値を再生値で上書きし、スナップショットも変更後の値になってしまう
        /// </summary>
        public void SyncPoseEditing()
        {
            var isPoseEditing = studioHackManager.isPoseEditing;
            if (isPrevPoseEditing == isPoseEditing)
            {
                return;
            }

            if (isPoseEditing)
            {
                OnPoseEditStart();
            }
            else
            {
                OnPoseEditEnd();
            }
            isPrevPoseEditing = isPoseEditing;
        }
```

- [ ] **Step 3: `AutoEditMode.cs` を作る**

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// パラメータ変更時に編集モードへ自動で入るための唯一の入口。
    /// 値を書き換える「前」に呼ぶこと。編集モード外はタイムラインのレイヤーが
    /// 毎フレーム再生値を書き戻すため、先に入っておかないと変更が巻き戻る。
    /// 既に編集モードなら何もしない
    /// </summary>
    public static class AutoEditMode
    {
        public static void Enter()
        {
            var studioHackManager = MTEP.StudioHackManager.instance;
            var studioHack = studioHackManager.studioHack;

            if (studioHack == null)
            {
                // タイムライン側が未登録 (タイトル画面等) なら SE 本体のフラグだけ立てる
                MaidManipulateManager.instance.isEditMode = true;
                return;
            }

            if (studioHack.isPoseEditing)
            {
                return;
            }

            // StudioHackManager 経由で入ると再生停止も一緒に行われる (SceneEditorHack.isPoseEditing)
            studioHackManager.isPoseEditing = true;

            // キャッシュとスナップショットを同フレームで揃える。
            // 翌フレームの PreUpdate / Update に任せると、このあと書く値が
            // OnPoseEditStart の ApplyCurrentFrame で上書きされる
            studioHackManager.SyncPoseEditing();
            MTEP.TimelineManager.instance.SyncPoseEditing();
        }
    }
}
```

- [ ] **Step 4: `MaidManipulateManager.isEditMode` の summary を実態に合わせる**

```csharp
        /// <summary>編集モード。メニューバーのトグルと連動する。パラメータを変更すると自動で ON になる (AutoEditMode.Enter)</summary>
```

- [ ] **Step 5: COM3D25 構成でビルドして通ることを確認する**

Git Bash から:

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin" && export MSYS2_ARG_CONV_EXCL="*" && "/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo
```

Expected: `0 エラー`

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/AutoEditMode.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioHackManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs
git commit -m "feat(edit-mode): 編集モードへ即時に入る AutoEditMode.Enter を追加"
```

---

### Task 2: 履歴を残す全操作から自動移行する (`HistoryManager.BeforeEdit`)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs:117-124` (`BeforeEditCore` 冒頭)

**Interfaces:**
- Consumes: `AutoEditMode.Enter()` (Task 1)

- [ ] **Step 1: `BeforeEditCore` の冒頭 (早期 return より前) で `AutoEditMode.Enter()` を呼ぶ**

```csharp
        private void BeforeEditCore(Maid maid, HistoryScope scope, string description,
            object targetKey, Func<IStateSnapshot> capture, IEnumerable<Transform> targetBones)
        {
            // 値を書き換える操作の直前に必ず通る場所なので、ここで編集モードへ入る。
            // 履歴が無効 (historyLimit <= 0) でも自動移行は必要なため、早期 return より前に置く
            AutoEditMode.Enter();

            if ((maid == null && HistoryScopeUtils.RequiresMaid(scope))
                || config.historyLimit <= 0)
            {
                return;
            }
            // ... (以下既存のまま)
```

- [ ] **Step 2: `BeforeEdit` (targetBonesProvider 版) の早期 return を確認する**

`HistoryManager.cs:165-172` の provider 版は `_pending` があると `BeforeEdit` を呼ばずに return する。`_pending` がある = 直前に `BeforeEditCore` を通っており既に編集モードなので追加不要。コメントを 1 行足す。

```csharp
            // 確定待ちがあるなら BeforeEditCore を通っており編集モードにも入っているため、そのまま抜けてよい
            if (_pending != null && _pending.maid == maid && _pending.scope == scope)
```

- [ ] **Step 3: Undo / Redo の適用前にも編集モードへ入る**

`TryApply` (`HistoryManager.cs:411` 付近) はスナップショットを直接当てるため `BeforeEditCore` を通らない。編集モード外で Undo すると、戻した値が翌フレームのレイヤー書き戻しで消えるので、適用直前に入る。

```csharp
        private static bool TryApply(IHistoryEntry entry, bool useBefore)
        {
            try
            {
                // canApply も外部プラグインの実装が入るため適用と同じ try で保護する
                if (!entry.canApply)
                {
                    return false;
                }

                // 戻した値もパラメータ変更と同じく編集モード外では書き戻されるため、当てる前に入る
                AutoEditMode.Enter();

                if (useBefore)
```

- [ ] **Step 4: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs
git commit -m "feat(edit-mode): 履歴を残す操作と Undo/Redo で編集モードへ自動移行する"
```

---

### Task 3: GUIView の値変更フックと区間ヘルパー

演出系 (ステージライト / レーザー / サイリウム / ポストエフェクト / ボイス / 効果音 / 瞳位置 / モデル管理) の行ドロワーは `HistoryManager.BeforeEdit` を通さず、`view.DrawXxx(..., onChanged)` のコールバック内で直接値を書く (合計 100 箇所超)。個別に `Enter()` を足す代わりに GUIView 側で「コールバックを呼ぶ直前」に通知するフックを設け、区間単位で張る。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIComboBox.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs`

**Interfaces:**
- Produces: `GUIView.onBeforeValueChanged` (`public Action`)、`GUIView.NotifyBeforeValueChanged()` (public: GUIComboBox など外部の部品からも呼ぶ)
- Produces: 拡張メソッド `view.BeginAutoEditMode()` / `view.EndAutoEditMode()`

- [ ] **Step 1: GUIView にフックと通知メソッドを追加し、サブビューへ伝播させる**

`parent` プロパティの近くに追加する。

```csharp
        /// <summary>
        /// ユーザー操作で値が変わり、onChanged 系コールバックを呼ぶ直前に発火する。
        /// 編集モードへの自動移行 (値を書く前に入る必要がある) のために使う。
        /// サブビューは親のフックを引き継ぐ
        /// </summary>
        public Action onBeforeValueChanged;

        public void NotifyBeforeValueChanged()
        {
            onBeforeValueChanged?.Invoke();
        }
```

`parent` setter で伝播させる。

```csharp
                if (_parent != null)
                {
                    SetEnabled(_parent.guiEnabled);
                    onBeforeValueChanged = _parent.onBeforeValueChanged;
                }
```

`BeginSubView` は毎回 `subView.parent = this` を代入するので、区間ごとに変わるフックも追従する。

- [ ] **Step 2: GUIView の各コールバック直前に `NotifyBeforeValueChanged()` を入れる**

対象は「ユーザー操作の結果として値を渡す」箇所。`grep -n "onChanged(\|onChanged?.Invoke\|onColorChanged(\|onChangedAxis(" GUIView.cs` で列挙し、以下の各サイトの直前に 1 行入れる (他のオーバーロードへ委譲しているだけの行は対象外)。

| 行 (現状) | メソッド | 入れる場所 |
| --- | --- | --- |
| 1094 | `DrawToggle(label, ...)` | `if (newValue != value) {` の直後 |
| 1135 | `DrawToggle(icon, ...)` | 同上 |
| 1270 | `DrawTextField` | `if (newText != text) {` の直後 |
| 1336 | `DrawTextField(option)` の「P」貼り付け | `option.onChanged(GUIUtility.systemCopyBuffer)` の直前 |
| 1427, 1460 | `DrawFloatField` | `option.onChanged(newValue)` の直前 |
| 1668, 1672 | ベクトル欄 (`onChangedAxis` / `onChanged`) | `if (option.onChangedAxis != null)` の直前に 1 回 |
| 1736, 1755 | ベクトル欄のドラッグ / 入力 | 各 `option.onChanged(value)` の直前 |
| 1810 | `DrawIntField` | `option.onChanged(newValue)` の直前 |
| 1897, 1980 | スライダー (`onChanged?.Invoke`) | `value = newValue;` の直前 |
| 2801, 2929 | `DrawSliderValue` 系 | `onChanged(newValue)` の直前 |
| 3045 | `DrawColor` | `onColorChanged(fieldCache.color)` の直前 |
| 3080 | カーブの「R」 | `curve.CopyFrom(...)` の直前 |

例 (`DrawToggle`):

```csharp
            if (newValue != value)
            {
                NotifyBeforeValueChanged();
                onChanged(newValue);
                return true;
            }
```

行番号は目安。編集時は必ず grep で現在位置を確認し、漏れがないか最後に `grep -c "NotifyBeforeValueChanged" GUIView.cs` で件数 (通知メソッド定義 1 + 上記 18 サイト = 19 以上) を確認する。

- [ ] **Step 3: `ColorPickerWindow` が `onColorChanged` を直接呼んでいないか確認する**

```bash
grep -n "onColorChanged\|onChanged" source/COM3D2.SceneEditor.Plugin/MTEUtils/ColorPickerWindow.cs
```

`ColorPickerWindow` は `Open()` (102-111 行) と `Sync()` (132-147 行) の両方で `_onColorChanged` を控え、自身の OnGUI (362-365 行付近) から直接呼ぶ。ピッカーは `GUIView` を持たないので、**`DrawColor` の冒頭で `onColorChanged` をラップし、`Open` と `Sync` の両方にラップ済みを渡す** (`Open` に生のコールバックを渡すと、初回クリック直後の 1 フレームだけフックを通らない経路が残る)。

```csharp
            // ピッカーからの変更も同じフックを通す (値を書く前に編集モードへ入る)。
            // Open / Sync の両方に渡し、生のコールバックがピッカーに残らないようにする
            Action<Color> notifyingOnColorChanged = c =>
            {
                NotifyBeforeValueChanged();
                onColorChanged(c);
            };
```

`DrawColor` 内の `picker.Open(..., onColorChanged, ...)` と `picker.Sync(..., onColorChanged)` の引数を `notifyingOnColorChanged` に置き換える。3045 行の `onColorChanged(fieldCache.color)` は Step 2 の通り `NotifyBeforeValueChanged()` を直前に置く (こちらは生のまま呼んでよい)。`CurveEditorWindow` についても同様に `grep -n "onChanged" MTEUtils/CurveEditorWindow.cs` で確認し、直接呼ぶ経路があれば同じ形でラップする。

- [ ] **Step 4: GUIComboBox の `onSelected` 5 箇所を 1 本にまとめ、フックを通す**

`GUIComboBox.cs` に private ヘルパーとフィールドを追加する。

```csharp
        /// <summary>
        /// 描画したビューの値変更フック。ポップアップ側で選択が確定したとき (別ビュー) にも
        /// 同じフックを通せるよう、DrawButton のたびに控える
        /// </summary>
        private Action _onBeforeSelected;

        private void InvokeSelected()
        {
            if (this.onSelected == null)
            {
                return;
            }
            _onBeforeSelected?.Invoke();
            this.onSelected(this.items[this.currentIndex], this.currentIndex);
        }
```

`DrawButton(view, ...)` の冒頭 (view を受け取る各オーバーロードの実体) で `_onBeforeSelected = view.onBeforeValueChanged;` を代入する。`grep -n "public void DrawButton\|public bool DrawButton" GUIComboBox.cs` で実体を特定する。

131-133 / 155-157 / 196-198 / 220-222 / 257-259 の 5 箇所を `InvokeSelected();` に置き換える。

- [ ] **Step 5: 区間ヘルパー `GUIViewAutoEditModeExtensions.cs` を作る**

```csharp
using System;
using COM3D2.MotionTimelineEditor;   // GUIView の名前空間 (GUIView.cs:6)

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「この区間の値変更は編集モードへ自動移行する」を宣言する GUIView 拡張。
    /// 従来 view.SetEnabled(focusedComboBox == null && isPoseEditing) で塞いでいた区間を
    /// 常時操作可にし、代わりに値を書く直前に AutoEditMode.Enter を呼ばせる
    /// </summary>
    public static class GUIViewAutoEditModeExtensions
    {
        public static void BeginAutoEditMode(this GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);
            view.onBeforeValueChanged = AutoEditMode.Enter;
        }

        public static void EndAutoEditMode(this GUIView view)
        {
            view.onBeforeValueChanged = null;
            view.SetEnabled(view.focusedComboBox == null);
        }
    }
}
```

`GUIView` の名前空間は `COM3D2.MotionTimelineEditor` (`.Plugin` 無し)。`MTEP` alias とは別物なので混同しない。

- [ ] **Step 6: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIComboBox.cs source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs
git commit -m "feat(gui): 値変更コールバック直前のフック onBeforeValueChanged を追加"
```

---

### Task 4: ウィンドウ側の「編集モード中のみ」ゲートを区間ヘルパーへ置き換える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs:292, 345, 510, 584, 767, 819, 853, 931, 1245`
- Modify: `source/COM3D2.SceneEditor.Plugin/SoundWindow.cs:388, 444`
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs:311-317`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs:679-694`
- Modify: `source/COM3D2.SceneEditor.Plugin/TextWindow.cs:147-164`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/AnimationTimelineLayer.cs:280`

**Interfaces:**
- Consumes: `view.BeginAutoEditMode()` / `view.EndAutoEditMode()` (Task 3)

- [ ] **Step 1: LiveEffectWindow の 9 箇所**

各 `view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);` を `view.BeginAutoEditMode();` に置き換える。それぞれのスクロールビュー末尾 (`view.EndScrollView()` の直前) にある `view.SetEnabled(view.focusedComboBox == null);` があれば `view.EndAutoEditMode();` に置き換え、無ければ `view.EndScrollView()` の直前に `view.EndAutoEditMode();` を足す。

```bash
grep -n "isPoseEditing\|EndScrollView\|SetEnabled" source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs
```

で対応を確認しながら進める。

- [ ] **Step 2: SoundWindow の 2 箇所**

同じ置き換え。388 行の区間は 391 行 `view.SetEnabled(view.focusedComboBox == null);` が End 相当。444 行の区間は `view.EndScrollView()` の直前に `EndAutoEditMode()` を足す。

- [ ] **Step 3: BackgroundWindow の背景モデルツリー**

```csharp
            _view.BeginAutoEditMode();

            _modelTreeView.Draw(_view, _view.GetDrawRect(-1, -1));

            _view.EndAutoEditMode();
```

ツリーのチェック (`onCheckChanged`) は `GUITreeView.cs:324` で `DrawToggle` を経由するためフックが効く。ただしチェックの反映は `ApplyPendingCheck` に遅延されるので、`_pendingCheckSourceName` を立てる `onCheckChanged` の中で編集モードに入っていることを確認する (`BackgroundWindow.cs` の `onCheckChanged` 設定箇所を grep)。フックはトグル直前に発火しているので追加コードは不要。

- [ ] **Step 4: CameraWindow のサブカメラ**

`canEdit` と黄色ラベルを消し、区間化する。

```csharp
            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // 値を書く直前に編集モードへ入る (編集モード外はレイヤーが毎フレーム再生値を書き戻すため)
            _view.BeginAutoEditMode();

            _subCameraRowDrawers.Get(cameraData.name)
                .Draw(_view, cameraData, LABEL_WIDTH, ROW_HEIGHT);

            _view.EndAutoEditMode();
            _view.EndScrollView();
```

- [ ] **Step 5: TextWindow のテキスト**

同様に `canEdit` とラベルを消し、`_textRowDrawers.Get(boneName).Draw(...)` を `BeginAutoEditMode()` / `EndAutoEditMode()` で囲む。冒頭コメントは「値を書く直前に編集モードへ入る」に書き換える。

- [ ] **Step 6: AnimationTimelineLayer のレイヤー UI**

`view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);` を `view.BeginAutoEditMode();` に置き換える。メソッド末尾 (次に `SetEnabled(view.focusedComboBox == null)` を戻している箇所か、無ければメソッドの最後) に `view.EndAutoEditMode();` を足す。

- [ ] **Step 7: 区間内でフックを通らずに値を書く「コピー」ボタンへ `AutoEditMode.Enter()` を足す**

`DrawButton` は値変更コールバックではないため Task 3 のフックを通らない。解禁した区間の中で押下時に値を書くボタンを洗い出す。

```bash
grep -n "DrawButton(\"コピー\"\|DrawButton(\"ペースト\"\|DrawButton(\"リセット\"" source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs source/COM3D2.SceneEditor.Plugin/SoundWindow.cs source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs source/COM3D2.SceneEditor.Plugin/StageLightRowDrawer.cs source/COM3D2.SceneEditor.Plugin/StageLaserRowDrawer.cs source/COM3D2.SceneEditor.Plugin/PsylliumRowDrawer.cs source/COM3D2.SceneEditor.Plugin/SubCameraRowDrawer.cs source/COM3D2.SceneEditor.Plugin/TextRowDrawer.cs source/COM3D2.SceneEditor.Plugin/SeRowDrawer.cs source/COM3D2.SceneEditor.Plugin/VoiceRowDrawer.cs
```

調査時点で該当するのは `LiveEffectWindow.cs` の「コピー」7 箇所 (357 / 522 / 596 / 779 / 1026 / 1159 / 1259 行、いずれも `CopyFrom(...)` で別対象へ値を書く) と `PostEffectRowDrawer.cs:90` の「コピー」。それぞれ値を書く直前に 1 行入れる。

```csharp
                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLight != null && copyToLight != light)
                    {
                        // ボタンは GUIView の値変更フックを通らないため、書く前に編集モードへ入る
                        AutoEditMode.Enter();
                        copyToLight.CopyFrom(light);
                    }
```

`LightRowDrawer.cs` の「ペースト」は `RecordLightEdit` (= `HistoryManager.BeforeEdit`) を通るので Task 2 で自動移行済み。「コピー」はクリップボードへ読むだけで値を書かないので対象外。grep で上記以外が見つかったら、押下時に値を書くかを読んで判断する。

- [ ] **Step 8: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 9: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs source/COM3D2.SceneEditor.Plugin/SoundWindow.cs source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs source/COM3D2.SceneEditor.Plugin/TextWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/AnimationTimelineLayer.cs
git commit -m "feat(edit-mode): 演出・カメラ・テキスト・音のウィンドウを常時操作可にして自動移行に統一"
```

---

### Task 5: Inspector の ItemInspector 9 種のゲートを撤去する

**Files:**
- Modify: `Timeline/ItemInspector/PngPlacementItemInspector.cs:33-39`
- Modify: `Timeline/ItemInspector/PostEffectItemInspector.cs:29-35`
- Modify: `Timeline/ItemInspector/PsylliumItemInspector.cs:30-36`
- Modify: `Timeline/ItemInspector/SeItemInspector.cs:32-37`
- Modify: `Timeline/ItemInspector/StageLaserItemInspector.cs:27-33`
- Modify: `Timeline/ItemInspector/StageLightItemInspector.cs:27-33`
- Modify: `Timeline/ItemInspector/SubCameraItemInspector.cs:30-36`
- Modify: `Timeline/ItemInspector/TextItemInspector.cs:29-35`
- Modify: `Timeline/ItemInspector/VoiceItemInspector.cs:30-35`

(すべて `source/COM3D2.SceneEditor.Plugin/` 配下)

**Interfaces:**
- Consumes: `view.BeginAutoEditMode()` / `view.EndAutoEditMode()` (Task 3)

- [ ] **Step 1: 各 `DrawItems` の早期 return ブロックを削除し、行描画を区間で囲む**

例 (`StageLightItemInspector`):

```csharp
        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集モード外はレイヤーが毎フレーム再生値を書き戻すため、
            // 値を書く直前に編集モードへ入る (BeginAutoEditMode)
            view.BeginAutoEditMode();

            foreach (var item in items)
            {
                // ... (既存のまま)
            }

            _rowDrawers.PruneExcept(items);   // 既存の末尾処理があればそのまま残す
            view.EndAutoEditMode();
        }
```

削除するのは `if (!MTEP.StudioHackManager.instance.isPoseEditing) { DrawLabel(...); PruneExcept(...); return; }` のブロックだけ。`PruneExcept` がループ後にも呼ばれているか各ファイルで確認し、無ければ (早期 return 内でしか呼んでいなければ) ループ後に残す。

- [ ] **Step 2: テスト側に ItemInspector の編集モード依存が無いことを確認する**

```bash
grep -rn "isPoseEditing\|編集モード" source/COM3D2.SceneEditor.Plugin.Tests/*ItemInspectorTests.cs
```

Expected: 該当なし (あれば該当テストの期待値を「常に行を描く」へ直す)。

- [ ] **Step 3: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/
git commit -m "feat(inspector): 演出系 ItemInspector を編集モード外でも操作可にする"
```

---

### Task 6: 行ドロワー内の個別ゲート (瞳位置 / モデル管理 / 背景モデル管理)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs:125-140, 226-230, 268-290`
- Modify: `source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs:51-53, 137-138`
- Modify: `source/COM3D2.SceneEditor.Plugin/BGModelManageRowDrawer.cs:116-118, 152-154`

**Interfaces:**
- Consumes: `AutoEditMode.Enter()` (Task 1)、`view.BeginAutoEditMode()` / `view.EndAutoEditMode()` (Task 3)

- [ ] **Step 1: EyesPosRowDrawer**

`BeginEditable` / `EndEditable` を区間ヘルパーに置き換える (メソッドごと削除し、呼び出し箇所を `view.BeginAutoEditMode()` / `view.EndAutoEditMode()` にする)。summary の「編集モード中だけ操作させる」は「値を書く直前に編集モードへ入る」に直す。

位置図のドラッグは GUIView のコールバックを通らないので明示的に入る。

```csharp
            if (e.type == EventType.MouseDown && e.button == 0 &&
                drawRect.Contains(e.mousePosition) &&
                view.focusedComboBox == null)
            {
                // 位置図は GUIView のコールバックを通らないため、掴んだ時点で編集モードへ入る
                AutoEditMode.Enter();
                // ... (既存の掴み処理)
```

`&& studioHackManager.isPoseEditing` の条件は削除する。ドラッグ中に編集モードを抜けたら掴みを離す判定 (`!studioHackManager.isPoseEditing`) は残す。

229 行のマーカー色 `studioHackManager.isPoseEditing ? Color.white : Color.gray` は常に `Color.white` にする (操作不可の表現だったため)。

- [ ] **Step 2: ModelManageRowDrawer**

51-53 行の `view.SetEnabled(... && isPoseEditing)` を `view.BeginAutoEditMode();` に、137-138 行の `view.SetEnabled(view.focusedComboBox == null);` を `view.EndAutoEditMode();` に置き換える。コメントは「複製・削除・表示切替はレイヤーの書き戻し対象なので、値を書く直前に編集モードへ入る」にする。

「複製」「削除」はボタン (`DrawButton`) でフックを通らないため、明示的に入れる。

```csharp
                if (view.DrawButton("複製", 45, RowHeight))
                {
                    AutoEditMode.Enter();
                    timelineManager.CopyModel(model);
                }

                if (view.DrawButton("削除", 45, RowHeight))
                {
                    AutoEditMode.Enter();
                    modelManager.DeleteModel(model);
                }
```

- [ ] **Step 3: BGModelManageRowDrawer**

116-118 行と 152-154 行の `SetEnabled(... && isPoseEditing)` を `view.BeginAutoEditMode();` に置き換え、それぞれの区間の終わり (元の `SetEnabled(view.focusedComboBox == null)` か `EndLayout()` の直前) に `view.EndAutoEditMode();` を置く。「複製」「削除」ボタンの `action = ...` 代入の直前に `AutoEditMode.Enter();` を足す (ボタンはフックを通らない)。

- [ ] **Step 4: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs source/COM3D2.SceneEditor.Plugin/BGModelManageRowDrawer.cs
git commit -m "feat(edit-mode): 瞳位置・モデル管理の行を編集モード外でも操作可にする"
```

---

### Task 7: トグル・注意ラベル類の整理

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:229-241`
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs:351, 363-364`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs:94-100`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:290-295`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs:672, 678`

**Interfaces:**
- Consumes: `AutoEditMode.Enter()` (Task 1)

- [ ] **Step 1: メニューバーの「ボーン表示」を常時有効にし、ON にしたら編集モードへ入る**

```csharp
                new MenuItem
                {
                    label = "ボーン表示",
                    isOn = () => MaidManipulateManager.instance.isBoneVisible,
                    toggle = () =>
                    {
                        var manager = MaidManipulateManager.instance;
                        var turnOn = !manager.isBoneVisible;
                        // ボーンは編集モード中しか出ないため、ON にするなら編集モードへ入る
                        if (turnOn)
                        {
                            AutoEditMode.Enter();
                        }
                        manager.isBoneVisible = turnOn;
                    },
                },
```

`enabled = () => ...isEditMode` の行は削除する。

- [ ] **Step 2: BoneEditWindow のヘッダーのボーン表示トグルも同じ挙動にする**

```csharp
                view.DrawToggle("ボーン表示", manager.isBoneVisible, 100, ROW_HEIGHT, value =>
                {
                    // ボーンは編集モード中しか出ないため、ON にするなら編集モードへ入る
                    if (value)
                    {
                        AutoEditMode.Enter();
                    }
                    manager.isBoneVisible = value;
                });
```

summary (351 行) の「(編集モード中のみ操作できる)」を「(ON にすると編集モードへ入る)」に直す。

- [ ] **Step 3: IK 固定の黄色ラベルを消す**

`MaidIKWindow.cs:94-100` の `if (!MaidManipulateManager.instance.isEditMode) { DrawLabel("※編集モード外は…") }` と `InspectorWindow.cs:290-295` の `if (!maidManager.isEditMode) { DrawLabel("※編集モードで有効") }` を削除する。IK 固定トグルは `HistoryManager.BeforeEdit` を通るので Task 2 で自動移行する。`MaidIKWindow` の `BeginHorizontal` に残るのが `DrawLabel("IK固定")` だけになるならレイアウトを崩さないようそのまま残してよい。

- [ ] **Step 4: TimelineControlWindow の視野角固定 / フォーカス固定トグル**

`enabled` 引数から `&& studioHackManager.isPoseEditing` を外し、コールバック冒頭で `AutoEditMode.Enter();` を呼ぶ (これらは編集中のカメラ挙動を決める設定で、編集モード外では意味を持たないため)。

```csharp
                cameraUpdated |= DrawIconToggle(view, ToolbarIcons.Kind.FovLock, "視野角固定", timelineConfig.isFixedFoV, !currentLayer.isCameraLayer, newValue =>
                {
                    // 編集中のカメラ固定なので、切り替えたら編集モードへ入る
                    AutoEditMode.Enter();
                    timelineConfig.isFixedFoV = newValue;
                    timelineConfig.dirty = true;
                });
```

フォーカス固定も同様。

- [ ] **Step 5: 残ったゲートの棚卸し**

```bash
grep -rn "isPoseEditing\|isEditMode" --include=*.cs source/COM3D2.SceneEditor.Plugin | grep -v "Timeline/Manager\|Timeline/Hack\|Timeline/TimelineLayer\|MaidManipulation\|Manager/Gizmo\|Manager/Grid\|AutoEditMode.cs"
```

残ってよいもの: `TimelineControlWindow` の 登録 / ポーズP / 案内ラベル、`TimelineWindow` の A/D ボタン、`EyesPosRowDrawer` のドラッグ解放判定、`COM3D2.SceneEditor.Plugin.cs` のキー切替、`MotionData` の frame 切替、`TimelineCurveEditor` のコメント。それ以外が残っていれば Task 4〜7 のどれかの漏れなので直す。

- [ ] **Step 6: ビルド (Task 1 Step 5 と同じコマンド)**

Expected: `0 エラー`

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs
git commit -m "feat(edit-mode): ボーン表示・カメラ固定トグルを常時操作可にし IK の注意ラベルを外す"
```

---

### Task 8: 両構成ビルド・テスト・変更履歴

**Files:**
- Modify: `CHANGELOG.md` (「## 未リリース」の先頭)

- [ ] **Step 1: COM3D2 → COM3D25 の順でビルドし、テストを回す**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin" && export MSYS2_ARG_CONV_EXCL="*" && MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" && "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo && "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo && cd ../.. && dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: 両構成 `0 エラー`、テスト全件 Passed。

- [ ] **Step 2: CHANGELOG に追記する**

```markdown
- どのウィンドウでパラメータを変更しても、自動で編集モードに入るようになりました
  - ライブ演出・サブカメラ・テキスト・音・瞳位置・モデル管理など「編集モード中のみ」だった項目は常に操作できます
  - Inspector の演出系の行も編集モード外で表示されます
  - 「ボーン表示」を ON にすると編集モードに入ります
```

- [ ] **Step 3: 実機確認 (ゲーム起動中なら)**

MCP `com3d25-devbridge` の `ping` で起動中か確認し、起動中なら CLAUDE.md のホットリロード手順で新 DLL を積み増して以下を確認する。起動していなければこの Step は省略し、その旨を報告に書く。

1. タイムライン読込 + 再生中にライブ演出ウィンドウのスライダーを動かす → 再生が止まり編集モードに入り、値が巻き戻らない
2. 編集モード外で Inspector のステージライト行が出ていて、色を変えると編集モードに入る
3. 「登録」を押すと 1 の変更がキーフレームに入る (スナップショットが変更前で取れている)
4. メニューバー「ボーン表示」が編集モード外でも押せ、ON でボーンが出る

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: 編集モード自動移行を変更履歴に追記"
```

---

## Self-Review

- **Spec coverage:** 「各ウィンドウのパラメータ変更で自動移行」→ Task 2 (履歴経由) + Task 3 (フック経由)。「編集モード中のみ変更可能なウィンドウを変更可能に」→ Task 4〜7 で全ゲートを列挙 (Task 7 Step 5 で棚卸し)。「自動移行に統一」→ 入口を `AutoEditMode.Enter()` に一本化。
- **Placeholder scan:** 行番号は「現状の目安」と明記し、grep での再確認手順を各所に置いた。
- **レビュー反映:** 区間内の「コピー」ボタン (Task 4 Step 7)、Undo/Redo の適用前 (Task 2 Step 3)、ColorPickerWindow.Open へのラップ済みコールバック (Task 3 Step 3)、GUIView の名前空間修正を取り込んだ。
- **Type consistency:** `AutoEditMode.Enter()` / `SyncPoseEditing()` / `onBeforeValueChanged` / `NotifyBeforeValueChanged()` / `BeginAutoEditMode()` / `EndAutoEditMode()` / `InvokeSelected()` の名前は全タスクで統一。

## レビュー却下メモ

- `AutoEditMode.Enter()` の冪等性や `BeforeEditCore` 経由の呼び出しを単体テストすべき — `AutoEditMode` は `StudioHackManager` / `TimelineManager` / `MaidManipulateManager` のシングルトンと Unity 型に依存し、テストプロジェクト (純粋ロジックのみ) ではモック基盤が無い。テスト用の抽象化を足すのは本件の範囲を超えるため見送り (未確認のまま見送り)。実機確認 (Task 8 Step 3) で代替する
