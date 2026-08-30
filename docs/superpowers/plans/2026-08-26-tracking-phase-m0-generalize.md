# 変更追跡 Phase M0: 共通基盤の汎用化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 表情で実装した変更追跡(チェックボックス)の汎用部分を抽出し、以降の Phase M1〜M4 が「ストア生成 + チェック行 + trackedStore 指定 + プリセット 2 箇所」だけで載る状態にする。

**Architecture:** 3 つの抽出を行う。(1) `FaceEditStore` → `EditTargetStore` へのリネーム(コードは表情非依存なので改名のみ)、(2) チェックボックス付き行の描画を `GUIView` のヘルパーへ抽出、(3) `MorphTimelineLayer` の「version ポーリング → メニュー再構築 → 0F 自動キー → 解除時キー削除」を `TimelineLayerBase` の partial ファイルへ opt-in 部品として抽出。**全タスクで表情の既存挙動を変えない**(振る舞い保存リファクタ)。

**Tech Stack:** C#(プラグイン本体は旧形式 csproj / Compile 明示列挙)、xUnit(net48、COM3D25 構成のプラグイン DLL 参照)、MSBuild 2 構成

**Spec:** `docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の「Phase M0」節

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` は使わない**。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
- テストは COM3D25 構成の DLL を参照するため、**COM3D25 構成ビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`** の順で実行する
- 新規/リネームした .cs は `source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` を必ず更新する(現在 L199 に `MaidManipulation\FaceEditStore.cs`)
- 振る舞い保存が最優先。抽出時にロジック・履歴文言・ガード条件を「改善」しない(気づきは計画外メモに留める)
- `deploy.bat` / `release.bat` は実行しない。実機確認はユーザーに依頼する

---

### Task 1: FaceEditStore → EditTargetStore リネーム

**Files:**
- Rename: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs` → `EditTargetStore.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:199`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditManager.cs`(型参照 6 箇所)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs:23,59,64`(型参照 3 箇所)
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:665`(コメント 1 箇所)
- Rename: `source/COM3D2.SceneEditor.Plugin.Tests/FaceEditStoreTests.cs` → `EditTargetStoreTests.cs`

**Interfaces:**
- Consumes: 既存 `FaceEditStore`(実装は変更しない)
- Produces: `class EditTargetStore` — API は現行と同一: `bool IsModified(string)` / `void Mark(string)` / `void Unmark(string)` / `void SetNames(IEnumerable<string>)` / `List<string> GetNames()` / `void Clear()` / `int version { get; }` / `bool isEmpty { get; }`。Task 3 と Phase M1 以降がこの名前を使う

- [ ] **Step 1: git mv でリネームする**

```bash
git mv source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/EditTargetStore.cs
git mv source/COM3D2.SceneEditor.Plugin.Tests/FaceEditStoreTests.cs source/COM3D2.SceneEditor.Plugin.Tests/EditTargetStoreTests.cs
```

- [ ] **Step 2: クラス名と表情固有コメントを汎用化する**

`EditTargetStore.cs` のクラス宣言とヘッダコメントを次に置き換える(メソッド実装は一切変更しない):

```csharp
    /// <summary>
    /// 編集対象 1 つ分(メイド 1 人・モデル 1 体など)の変更追跡。
    /// ユーザーが明示的に編集した (=チェック済みの) 項目名を持つ。
    /// プリセット保存の対象選別とタイムラインのボーンメニュー絞り込みの共通ソース。
    /// 編集側とタイムライン側で名前テーブルが分かれても共通に扱えるよう、生名文字列をキーにする
    /// </summary>
    public class EditTargetStore
```

`SetNames` 上の「同じ表情への undo/redo や〜」コメントは「同じ集合への undo/redo やプリセット再適用で〜」へ変更する。

- [ ] **Step 3: 参照側を一括更新する**

対象(`FaceEditStore` の文字列が残らないこと):

- `COM3D2.SceneEditor.Plugin.csproj` L199: `<Compile Include="MaidManipulation\FaceEditStore.cs" />` → `<Compile Include="MaidManipulation\EditTargetStore.cs" />`
- `FaceEditManager.cs`: 型名 6 箇所(`Dictionary<Maid, FaceEditStore>`、`GetStore`/`FindStore` の戻り値・ローカル変数・`new FaceEditStore()`)と L6 のコメント「メイドごとの FaceEditStore を管理する」→「メイドごとの EditTargetStore を管理する」。**クラス名 `FaceEditManager` は変えない**(表情ドメインのレジストリとして存続)
- `MorphTimelineLayer.cs`: L23 `private FaceEditStore FindFaceStore()`、L59 `private FaceEditStore _lastStore`、L64 `BuildCheckedNames(FaceEditStore store)` の型名(Task 3 でこれらのメンバー自体が基底へ移るが、Task 1 単体でもビルドが通るよう先に型名だけ直す)
- `ScenePresetData.cs` L665: コメント内 `(FaceEditStore)` → `(EditTargetStore)`
- `EditTargetStoreTests.cs`: クラス名 `FaceEditStoreTests` → `EditTargetStoreTests`、`new FaceEditStore()` 8 箇所 → `new EditTargetStore()`、L6 コメント「FaceEditStore は〜」→「EditTargetStore は〜」

確認: `grep -rn "FaceEditStore" source/` が 0 件になること。

- [ ] **Step 4: ビルドとテストを確認する**

Run: COM3D25 構成をビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter EditTargetStoreTests` → COM3D2 構成もビルド
Expected: 両ビルド成功、テスト 8 件 PASS

- [ ] **Step 5: コミット**

```bash
git add -A source/
git commit -m "refactor(tracking): FaceEditStore を EditTargetStore へ汎用化する"
```

---

### Task 2: GUIView チェック付き行ヘルパーの抽出

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs`(`DrawSliderValue` 末尾 L2673 付近の直後にメソッド追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:236-300`(`DrawMorphList` をヘルパー使用へ置き換え)

**Interfaces:**
- Consumes: 既存 `GUIView.DrawToggle(bool value, float width, float height, Action<bool> onChanged)`(L1036)、`DrawToggle(string label, bool value, float width, float height, Action<bool> onChanged)`、`DrawSliderValue(SliderOption)`、`BeginHorizontal()` / `EndLayout()`
- Produces: `GUIView.TrackedCheckWidth`(const float 20f)、`void DrawTrackedSliderValue(bool isChecked, Action<bool> onCheckChanged, float rowHeight, SliderOption option)`、`void DrawTrackedToggle(bool isChecked, Action<bool> onCheckChanged, string label, bool value, float width, float height, Action<bool> onChanged)`。Phase M1 以降の各編集ウィンドウがこれを使う

- [ ] **Step 1: GUIView にヘルパーを追加する**

`DrawSliderValue(SliderOption)` メソッドの終わり(L2673 付近)の直後に追加する:

```csharp
        /// <summary>変更追跡チェックボックスの列幅</summary>
        public const float TrackedCheckWidth = 20f;

        /// <summary>
        /// 変更追跡チェック付きのスライダー行。行頭にラベル無しチェックを置き、残り幅にスライダーを描く。
        /// option.labelWidth は呼び出し側で TrackedCheckWidth ぶん詰めて渡すこと
        /// </summary>
        public void DrawTrackedSliderValue(
            bool isChecked, Action<bool> onCheckChanged, float rowHeight, SliderOption option)
        {
            BeginHorizontal();
            {
                DrawToggle(isChecked, TrackedCheckWidth, rowHeight, onCheckChanged);
                DrawSliderValue(option);
            }
            EndLayout();
        }

        /// <summary>変更追跡チェック付きのトグル行</summary>
        public void DrawTrackedToggle(
            bool isChecked, Action<bool> onCheckChanged,
            string label, bool value, float width, float height, Action<bool> onChanged)
        {
            BeginHorizontal();
            {
                DrawToggle(isChecked, TrackedCheckWidth, height, onCheckChanged);
                DrawToggle(label, value, width, height, onChanged);
            }
            EndLayout();
        }
```

`GUIView.cs` 先頭に `using System;` が無ければ追加する(`Action` 用。既存コードが `Action<bool>` を多用しているため通常は既にある)。

- [ ] **Step 2: MaidFaceWindow.DrawMorphList を置き換える**

L236-300 の foreach 本体を次に置き換える(コールバックの中身・履歴文言・幅の値は現行と完全に同一。`BeginHorizontal`/`EndLayout` と 20px チェック描画がヘルパーへ移るだけ):

```csharp
            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(target, currentMorphCategory))
            {
                var value = MaidFaceMorphController.GetMorphValue(target, def);
                var isModified = faceStore != null && faceStore.IsModified(def.name);

                // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
                // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
                Action<bool> onCheckChanged = newChecked =>
                {
                    if (newChecked)
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情変更マーク: " + def.displayName);
                        FaceEditManager.instance.GetStore(target).Mark(def.name);
                    }
                    else
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情変更解除: " + def.displayName);
                        MaidFaceMorphController.SetMabataki(target, false);
                        MaidFaceMorphController.SetMorphValue(target, def, 0f);
                        FaceEditManager.instance.GetStore(target).Unmark(def.name);
                    }
                };

                if (def.isToggle)
                {
                    view.DrawTrackedToggle(isModified, onCheckChanged,
                        def.displayName, value >= 0.5f, 130, ROW_HEIGHT, newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情: " + def.displayName);
                            // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                            FaceEditManager.instance.GetStore(target).Mark(def.name);
                        });
                }
                else
                {
                    view.DrawTrackedSliderValue(isModified, onCheckChanged, ROW_HEIGHT,
                        new GUIView.SliderOption
                        {
                            label = def.displayName,
                            labelWidth = LABEL_WIDTH - GUIView.TrackedCheckWidth,
                            width = -1,
                            min = 0f,
                            max = 1f,
                            step = 0.01f,
                            defaultValue = 0f,
                            value = value,
                            onChanged = newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                    "表情: " + def.displayName);
                                // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                                MaidFaceMorphController.SetMabataki(target, false);
                                MaidFaceMorphController.SetMorphValue(target, def, newValue);
                                FaceEditManager.instance.GetStore(target).Mark(def.name);
                            },
                        });
                }
            }
```

`MaidFaceWindow.cs` 先頭に `using System;` が無ければ追加する(`Action<bool>` ローカル用)。foreach 前の `faceStore` 取得(L234)はそのまま残す。

- [ ] **Step 3: ビルドを確認する**

Run: COM3D25 構成 → COM3D2 構成で MSBuild
Expected: 両ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs
git commit -m "refactor(tracking): チェック付き行の描画を GUIView ヘルパーへ抽出する"
```

---

### Task 3: TimelineLayerBase への追跡絞り込みの抽出

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(Compile 追加。`Timeline\TimelineLayer\TimelineLayerBase.cs` の隣)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:238-241`(`Update` の既定実装)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs:18-188`(追跡ロジックを削除し override 4 つへ縮小)

**Interfaces:**
- Consumes: Task 1 の `EditTargetStore`(`version` / `IsModified`)。基底の既存メンバー: `maid`、`keyFrames` / `_keyFrames`、`CreateFrame(int)`、`UpdateFrame(FrameData, bool, bool)`、`UpdateBones(int, IEnumerable<BoneData>)`、`ApplyCurrentFrame(bool)`、`CleanFrames()`、`InitMenuItems()`、`FrameData.boneNames` / `GetFilterBones` / `RemoveBones`、`timelineManager.RequestHistory(string)`
- Produces: `TimelineLayerBase` の opt-in 仮想メンバー — `protected virtual EditTargetStore trackedStore => null;` / `protected virtual List<string> trackedCandidateNames => null;` / `protected virtual string trackedHistoryPrefix => "";` / `protected bool hasTrackedBoneFilter` / `protected List<string> trackedBoneNames`。Phase M1 以降のレイヤーはこの 3 つの virtual を override するだけで絞り込み一式が有効になる

- [ ] **Step 1: partial ファイルを新規作成する**

`TimelineLayerBase` は既に `partial` 宣言済み(L12)。`Timeline/TimelineLayer/TimelineLayerBaseTracking.cs` を作成する。ロジックは現行 `MorphTimelineLayer.cs` L18-188 の移設で、`FaceEditStore` → `EditTargetStore`、`FaceMorphUtils.saveMorphNames` → `trackedCandidateNames`、履歴文言の「表情」→ `trackedHistoryPrefix` に置き換える以外は変更しない:

```csharp
using System.Collections.Generic;
using System.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 変更追跡ストア (EditTargetStore) 連動のボーン絞り込み。
    /// trackedCandidateNames を override したレイヤーだけが opt-in する。
    /// チェック済み ∪ 既存キーフレーム記載を対象集合とし、チェック追加で 0F 自動キー、
    /// 解除でキー自動削除を行う (表情レイヤーで確立した仕組みの汎用化)
    /// </summary>
    public abstract partial class TimelineLayerBase
    {
        /// <summary>変更追跡ストア。対象未選択などで null を返してよい</summary>
        protected virtual EditTargetStore trackedStore => null;

        /// <summary>絞り込み候補の全ボーン名 (正準順)。null なら絞り込み無効</summary>
        protected virtual List<string> trackedCandidateNames => null;

        /// <summary>自動キー登録/削除の履歴表示に使う接頭辞 (例: "表情")</summary>
        protected virtual string trackedHistoryPrefix => "";

        protected bool hasTrackedBoneFilter => trackedCandidateNames != null;

        private List<string> _trackedBoneNames;
        private int _lastTrackedStoreVersion = -1;
        private EditTargetStore _lastTrackedStore;
        private int _trackedRebuildFrameCount;
        private HashSet<string> _lastTrackedCheckedNames = new HashSet<string>();

        /// <summary>絞り込み後の対象集合。opt-in レイヤーは allBoneNames の実装に使う</summary>
        protected List<string> trackedBoneNames
            => _trackedBoneNames ?? (_trackedBoneNames = BuildTrackedBoneNames());

        /// <summary>
        /// チェック済み ∪ 既存キーフレーム記載。表示順は trackedCandidateNames に揃える。
        /// 既存キーフレーム分を含めるのは、読み込んだアニメの項目を未チェックでも編集できるようにするため。
        /// チェックを外した項目は RemoveTrackedKeys でキーごと消えるため、ここには残らない
        /// </summary>
        private List<string> BuildTrackedBoneNames()
        {
            var store = trackedStore;

            var keyFrameNames = new HashSet<string>();
            foreach (var frame in keyFrames)
            {
                foreach (var name in frame.boneNames)
                {
                    keyFrameNames.Add(name);
                }
            }

            var result = new List<string>();
            foreach (var name in trackedCandidateNames)
            {
                if ((store != null && store.IsModified(name)) || keyFrameNames.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>候補テーブルに存在するチェック済み項目</summary>
        private HashSet<string> BuildTrackedCheckedNames(EditTargetStore store)
        {
            var result = new HashSet<string>();
            if (store == null)
            {
                return result;
            }

            foreach (var name in trackedCandidateNames)
            {
                if (store.IsModified(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>Update から毎フレーム呼ぶ。opt-in レイヤー以外では呼ばれない</summary>
        protected void UpdateTrackedBoneFilter()
        {
            // チェック変更 (store.version) は毎フレームの整数比較だけで検知する。
            // キー削除など store 以外由来の集合変化は 30 フレームごとの間引き再計算で拾う
            // (BuildTrackedBoneNames は keyFrames のフルコピーを伴うため毎フレームは回さない)
            var store = trackedStore;
            var version = store != null ? store.version : -1;
            var storeChanged = store != _lastTrackedStore || version != _lastTrackedStoreVersion;

            _trackedRebuildFrameCount++;
            if (!storeChanged && _trackedRebuildFrameCount < 30)
            {
                return;
            }
            _trackedRebuildFrameCount = 0;

            // 対象が入れ替わったときは前回のチェック集合を引き継がない
            // (別対象のチェック解除とみなしてキーを消さないようにするため)
            var targetChanged = store != _lastTrackedStore;
            _lastTrackedStore = store;
            _lastTrackedStoreVersion = version;

            var checkedNames = BuildTrackedCheckedNames(store);
            if (!targetChanged)
            {
                // チェックを外した項目はキーごと消す。0F 目の自動登録と対称にしないと、
                // 自動登録されたキーが残り続けてボーンメニューから消えなくなる
                RemoveTrackedKeys(_lastTrackedCheckedNames.Where(name => !checkedNames.Contains(name)).ToList());
            }
            _lastTrackedCheckedNames = checkedNames;

            var newNames = BuildTrackedBoneNames();
            if (_trackedBoneNames == null || !newNames.SequenceEqual(_trackedBoneNames))
            {
                // 初回構築 (_trackedBoneNames == null) は既存の対象を並べ直すだけなので追加扱いにしない
                var addedNames = _trackedBoneNames != null
                    ? newNames.Except(_trackedBoneNames).ToList()
                    : new List<string>();

                // UpdateFrame は allBoneNames を回すため、キー登録より先にキャッシュを差し替える
                _trackedBoneNames = newNames;
                InitMenuItems();

                AddTrackedFirstFrameKeys(addedNames);
            }
        }

        /// <summary>
        /// 新たに対象へ入った項目に 0F 目のキーを打つ。
        /// レイヤーの Update はタイムライン読み込み中しか回らないため、
        /// 読み込み済みのときにチェックを入れた場合だけ発火する。
        /// 0F 目にキーが無いとその項目はアニメの起点を持てないので、
        /// チェックした時点の現在値を基準値として登録する
        /// </summary>
        private void AddTrackedFirstFrameKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0 || maid == null)
            {
                return;
            }

            var tmpFrame = CreateFrame(0);
            UpdateFrame(tmpFrame, initialEdit: false, force: true);

            var bones = tmpFrame.GetFilterBones(boneNames);
            if (bones.Count == 0)
            {
                return;
            }

            UpdateBones(0, bones);
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory(trackedHistoryPrefix + "キーフレーム自動登録");
        }

        /// <summary>チェックを外した項目のキーを全フレームから消す</summary>
        private void RemoveTrackedKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0)
            {
                return;
            }

            var removed = false;
            foreach (var frame in _keyFrames)
            {
                var bones = frame.GetFilterBones(boneNames);
                if (bones.Count > 0)
                {
                    frame.RemoveBones(bones);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            CleanFrames();
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory(trackedHistoryPrefix + "キーフレーム自動削除");
        }
    }
}
```

注意: `AddTrackedFirstFrameKeys` の `maid == null` ガードは表情実装の現行挙動そのまま(メイド非依存レイヤーへ opt-in を広げる際に見直す前提。M0 では変更しない)。既存の `AddFirstBones`(L1148)/`RemoveAllBones`(L1180)との統合は**見送り**: 履歴文言・`CleanFrames`/`ApplyCurrentFrame` の有無・`_dummyLastFrame` の扱いが異なり、統合すると振る舞いが変わるため。

csproj へ追加(`Timeline\TimelineLayer\TimelineLayerBase.cs` の隣):

```xml
    <Compile Include="Timeline\TimelineLayer\TimelineLayerBaseTracking.cs" />
```

- [ ] **Step 2: TimelineLayerBase.Update の既定実装を変更する**

`TimelineLayerBase.cs` L238-241 を次に置き換える:

```csharp
        public virtual void Update()
        {
            if (hasTrackedBoneFilter)
            {
                UpdateTrackedBoneFilter();
            }
        }
```

- [ ] **Step 3: MorphTimelineLayer を縮小する**

`MorphTimelineLayer.cs` から以下を**削除**する(全て基底へ移設済み):

- L18-21: `_cachedBoneNames` フィールドと `allBoneNames` の現行実装
- L23-27: `FindFaceStore()`
- L29-56: `BuildBoneNames()`
- L58-61: `_lastStoreVersion` / `_lastStore` / `_rebuildCheckFrameCount` / `_lastCheckedNames`
- L63-80: `BuildCheckedNames()`
- L82-129: `Update()` override
- L131-158: `AddFirstFrameKeys()`
- L160-188: `RemoveAllKeys()`

代わりに次の override を追加する(削除した L18-21 の位置):

```csharp
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
        {
            get
            {
                var maid = this.maid;
                return maid != null ? FaceEditManager.instance.FindStore(maid) : null;
            }
        }

        protected override List<string> trackedCandidateNames => FaceMorphUtils.saveMorphNames;

        protected override string trackedHistoryPrefix => "表情";
```

`InitMenuItems`(L206-236)と `SetMorphValue` のマーク処理(L337-346)は**そのまま残す**。

- [ ] **Step 3b: 未使用 using を削除する**

`using System.Linq;` の使用箇所(L111/L116/L120)は全て削除範囲に含まれるため、削除後に `MorphTimelineLayer.cs` 内で LINQ の参照(`.Where` / `.Except` / `.ToList` / `.SequenceEqual` 等)が残っていないことを確認し、残っていなければ `using System.Linq;` を削除する。

- [ ] **Step 4: ビルドとテストを確認する**

Run: COM3D25 構成をビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`(全件)→ COM3D2 構成もビルド
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(timeline): 変更追跡の絞り込みを TimelineLayerBase へ抽出する"
```

---

### Task 4: 最終確認とロードマップ更新

**Files:**
- Modify: `docs/superpowers/specs/modified-tracking-rollout-roadmap.md`(Phase M0 節に完了マーク)

**Interfaces:**
- Consumes: Task 1〜3 の成果物
- Produces: なし

- [ ] **Step 1: クリーンな全体確認**

Run: COM3D25 構成 → COM3D2 構成で MSBuild、`dotnet test source\COM3D2.SceneEditor.Plugin.Tests`(フィルタ無し)
Expected: 両ビルド成功、全テスト PASS。加えて `grep -rn "FaceEditStore" source/ docs/superpowers/specs/modified-tracking-rollout-roadmap.md` で source/ 側が 0 件であること

- [ ] **Step 2: code-review スキルでレビューする**

実装完了後、ユーザーへ提示する前に code-review スキルを起動し、指摘を取り込む(プロジェクトの必須工程)。

- [ ] **Step 3: ロードマップへ完了を記録する**

`modified-tracking-rollout-roadmap.md` の「### Phase M0: 共通基盤の汎用化」見出しを「### Phase M0: 共通基盤の汎用化 ✅ 完了 (実施日)」へ更新し、節末尾に実装の要点(EditTargetStore / GUIView.DrawTrackedSliderValue・DrawTrackedToggle / TimelineLayerBaseTracking.cs の opt-in 仮想メンバー 3 つ)を 2〜3 行で追記してコミットする:

```bash
git add docs/superpowers/specs/modified-tracking-rollout-roadmap.md
git commit -m "docs: 変更追跡ロードマップの M0 完了を記録する"
```

- [ ] **Step 4: 実機確認の依頼事項をユーザーへ報告する**

振る舞い保存リファクタなので、確認は「表情の既存機能がデグレしていないこと」に絞る:

1. 表情タブのチェックボックス行(スライダー/トグル)の表示・操作が従来通り
2. チェック ON/OFF・スライダー編集での自動チェック・カテゴリリセットのチェック解除が従来通り
3. タイムラインのメイド表情レイヤーで、チェック追加時の 0F 自動キー・解除時のキー削除・ボーンメニュー絞り込みが従来通り
4. Undo/Redo でチェック集合が戻ることが従来通り

## レビュー却下メモ

- 抽出した追跡ロジック(UpdateTrackedBoneFilter 等)へのユニットテスト追加 — 見送り。TimelineLayerBase はゲーム型(Maid/TimelineManager)依存でテストハーネス構築コストが見合わず、既存実装も同粒度のテストなし(振る舞い保存リファクタで新規リスクは小)。実機確認で担保する
