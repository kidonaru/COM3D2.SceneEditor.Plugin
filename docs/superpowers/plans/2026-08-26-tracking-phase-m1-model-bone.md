# 変更追跡 Phase M1: モデルボーン 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** モデルボーンの編集差分ストア(`BoneEditStore`)を変更追跡のソースにして、`ModelBoneTimelineLayer` のボーンメニュー・キー書き込みを「編集済み(チェック済み)ボーン ∪ 既存キー記載」へ絞り込み、`BoneEditWindow` のボーンツリーからチェックの ON/OFF を操作できるようにする。

**Architecture:** M0 で用意した `TimelineLayerBase` の tracking 部品(`trackedStore` / `trackedCandidateNames`)は「1 レイヤー = 1 ストア + 生名文字列」を前提にしている。一方モデルボーンのストアはモデル(`GameObject`)ごとに分かれ、キーは修飾なしの `transform.name` である。そこで **`BoneEditManager` が全モデルストアを集約した読み取り専用の `EditTargetStore`(モデル修飾名 `model.name/bone.name` 入り)を毎フレーム片方向同期で作り**、レイヤーはそれを `trackedStore` として見る(ロードマップ方針 2 の片方向同期)。逆方向の書き込みは存在せず、チェック操作は必ず `BoneEditStore` 側(RecordEdit / ResetBone)を通す。

**Tech Stack:** C#(プラグイン本体は旧形式 csproj / `<Compile Include>` 明示列挙)、xUnit(net48、COM3D25 構成のプラグイン DLL 参照)、MSBuild 2 構成(COM3D2 / COM3D25)

**Spec:** `docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の「Phase M1」節

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` / `deploy.bat` / `release.bat` は実行しない**。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
- テストは COM3D25 構成の DLL を参照するため、**COM3D25 構成ビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`** の順で実行する
- 新規 .cs は `source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へアルファベット順の位置に追加する(テストプロジェクトは SDK 形式なので追加不要)
- xUnit は Unity ランタイム無しで動くため、**`Transform` / `GameObject` を生成するコードはテストできない**。テスト対象は純粋な文字列・集合ロジックに限り、Transform を伴う経路(`RecordEdit` / `ResetBone`)は Task 7 の実機確認で担保する
- ソース・オブ・トゥルースは `BoneEditStore` 1 つ。集約 `EditTargetStore` は読み取り専用の派生物で、そこへ直接 `Mark` / `Unmark` してはならない
- 実機確認はユーザーに依頼する(このセッションでゲームを操作しない)

---

### Task 1: BoneEditStore にエントリ集合の version カウンタを追加する

集約ストアの再構築を毎フレームのフル走査ではなく整数比較で判定できるようにする。値だけの変更(既存ボーンをさらに動かす)では追跡集合が変わらないため version は進めない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditStore.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BoneEditStoreVersionTests.cs`(新規)

**Interfaces:**
- Consumes: 既存 `BoneEditStore`
- Produces: `int BoneEditStore.version { get; }` — エントリ集合(slotName + boneName の組)が増減したときだけ増える。Task 3 の同期トリガに使う

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BoneEditStoreVersionTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using Xunit;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// BoneEditStore の version はエントリ集合の増減だけを表す。
    /// タイムライン側の追跡集合の再構築判定に使うため、
    /// 値だけの変更で進んだり、集合が変わったのに進まなかったりしてはいけない。
    /// Transform を要する経路 (RecordEdit / ResetBone) は Unity ランタイムが無いため実機確認で担保する
    /// </summary>
    public class BoneEditStoreVersionTests
    {
        private static SE.BoneEditEntry CreateEntry(string slotName, string boneName)
        {
            return new SE.BoneEditEntry
            {
                slotName = slotName,
                itemFileName = "test.menu",
                boneName = boneName,
            };
        }

        [Fact]
        public void 初期状態のversionは0()
        {
            var store = new SE.BoneEditStore();
            Assert.Equal(0, store.version);
        }

        [Fact]
        public void RestoreEntriesでversionが進む()
        {
            var store = new SE.BoneEditStore();
            store.RestoreEntries(new List<SE.BoneEditEntry> { CreateEntry("body", "Bip01") });
            Assert.True(store.version > 0);
        }

        [Fact]
        public void 空のClearではversionが進まない()
        {
            var store = new SE.BoneEditStore();
            var before = store.version;
            store.Clear();
            Assert.Equal(before, store.version);
        }

        [Fact]
        public void 中身のあるClearでversionが進む()
        {
            var store = new SE.BoneEditStore();
            store.RestoreEntries(new List<SE.BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.Clear();
            Assert.True(store.version > before);
        }

        [Fact]
        public void アイテム変更でスロットを捨てるとversionが進む()
        {
            var store = new SE.BoneEditStore();
            store.RestoreEntries(new List<SE.BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.DiscardSlotIfItemChanged("body", "other.menu");
            Assert.True(store.version > before);
            Assert.Empty(store.GetEntries("body"));
        }

        [Fact]
        public void アイテムが同じならDiscardでversionが進まない()
        {
            var store = new SE.BoneEditStore();
            store.RestoreEntries(new List<SE.BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.DiscardSlotIfItemChanged("body", "test.menu");
            Assert.Equal(before, store.version);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter BoneEditStoreVersionTests`
Expected: コンパイルエラー(`'BoneEditStore' に 'version' の定義が含まれていません`)

- [ ] **Step 3: version を実装する**

`BoneEditStore` のフィールド宣言直後へ:

```csharp
        /// <summary>
        /// エントリ集合 (slotName + boneName) の変更検知用。
        /// タイムライン側の追跡集合を再構築するかの判定に使うため、
        /// 既存エントリの値だけを更新した場合は進めない
        /// </summary>
        public int version { get; private set; }
```

`RecordEdit` の新規エントリ作成ブロック末尾(`slotDic[bone.name] = entry;` の直後)へ `version++;` を追加。

`ResetBone` の `_entries[slotName].Remove(entry.boneName);` を次に置き換える:

```csharp
            if (_entries[slotName].Remove(entry.boneName))
            {
                version++;
            }
```

`ResetSlot` の `_entries.Remove(slotName);` を次に置き換える:

```csharp
            // ボーンが見つからず戻せなかった分も含めて記録を捨てる。
            // ループ内の ResetBone でも version は進むため 1 回のリセットで複数回進むが、
            // version は「集合が変わったか」のシグナルであり変更回数ではないので問題ない
            if (_entries.Remove(slotName))
            {
                version++;
            }
```

`DiscardSlotIfItemChanged` の `_entries.Remove(slotName);` を `_entries.Remove(slotName); version++;` に置き換える(このブロックへ入る時点で `slotDic.Count > 0` が保証されているため、常に集合が変わる)。

`RestoreEntries` の先頭 `_entries.Clear();` の直後へ `version++;` を追加(スナップショット復元では同一集合かの判定が高くつくため常に進める。集約側の `EditTargetStore.SetNames` が集合比較で無駄な再構築を止める)。

`Clear()` を次に置き換える:

```csharp
        public void Clear()
        {
            if (_entries.Count > 0)
            {
                _entries.Clear();
                version++;
            }
        }
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS(既存テストの退行なし)

- [ ] **Step 5: COM3D2 構成でもビルドする**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
Expected: 成功

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditStore.cs source/COM3D2.SceneEditor.Plugin.Tests/BoneEditStoreVersionTests.cs
git commit -m "feat(tracking): BoneEditStore にエントリ集合の version を追加する"
```

---

### Task 2: モデル修飾名の対応ヘルパーを追加する

`BoneEditStore` は `transform.name`(修飾なし)、`ModelBoneTimelineLayer` は `model.name/transform.name`(修飾あり)で同じボーンを指す。両者の変換規則を 1 箇所に固定し、テストで守る。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelBoneTrackedNames.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(`<Compile Include>` 追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelBoneTrackedNamesTests.cs`(新規)

**Interfaces:**
- Consumes: `BoneEditEntry`(Task 1 で変更なし)
- Produces:
  - `static string ModelBoneTrackedNames.Qualify(string modelName, string boneName)` — `"{modelName}/{boneName}"`。どちらかが空なら `null`
  - `static void ModelBoneTrackedNames.Collect(string modelName, List<BoneEditEntry> entries, List<string> result)` — `entries` の `boneName` を修飾して `result` へ追加する(`result` はクリアしない)

**参照する既存規則:** `Timeline/ModelBoneController.cs:17-20` の `ModelBone.name`
```csharp
public string name => string.Format("{0}/{1}", model.name, transform != null ? transform.name : "");
```
`StudioModelManager.boneNames` はこの `ModelBone.name` を並べたものなので、修飾規則はこれと一致していなければならない。

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelBoneTrackedNamesTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using Xunit;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルボーンの修飾名生成を固定する。
    /// タイムライン側の候補名 (StudioModelManager.boneNames = ModelBone.name) と
    /// 同じ規則でなければ追跡集合が一切マッチしなくなる
    /// </summary>
    public class ModelBoneTrackedNamesTests
    {
        [Fact]
        public void モデル名とボーン名をスラッシュで連結する()
        {
            Assert.Equal(
                "test_furniture.menu/Bone_01",
                SE.ModelBoneTrackedNames.Qualify("test_furniture.menu", "Bone_01"));
        }

        [Fact]
        public void グループ付きモデル名でも規則は同じ()
        {
            Assert.Equal(
                "test_furniture.menu_2/Bone_01",
                SE.ModelBoneTrackedNames.Qualify("test_furniture.menu_2", "Bone_01"));
        }

        [Theory]
        [InlineData(null, "Bone_01")]
        [InlineData("", "Bone_01")]
        [InlineData("model", null)]
        [InlineData("model", "")]
        public void どちらかが空ならnullを返す(string modelName, string boneName)
        {
            Assert.Null(SE.ModelBoneTrackedNames.Qualify(modelName, boneName));
        }

        [Fact]
        public void Collectはエントリを修飾して追加する()
        {
            var entries = new List<SE.BoneEditEntry>
            {
                new SE.BoneEditEntry { boneName = "Bone_01" },
                new SE.BoneEditEntry { boneName = "Bone_02" },
            };
            var result = new List<string> { "既存" };

            SE.ModelBoneTrackedNames.Collect("model.menu", entries, result);

            Assert.Equal(
                new[] { "既存", "model.menu/Bone_01", "model.menu/Bone_02" },
                result);
        }

        [Fact]
        public void Collectはモデル名が空なら何も追加しない()
        {
            var entries = new List<SE.BoneEditEntry>
            {
                new SE.BoneEditEntry { boneName = "Bone_01" },
            };
            var result = new List<string>();

            SE.ModelBoneTrackedNames.Collect("", entries, result);

            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter ModelBoneTrackedNamesTests`
Expected: コンパイルエラー(`ModelBoneTrackedNames` が存在しない)

- [ ] **Step 3: ヘルパーを実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelBoneTrackedNames.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルボーンの名前変換。
    /// 編集側 (BoneEditStore) は Transform 名そのまま、タイムライン側 (StudioModelManager.boneNames) は
    /// モデル名で修飾した名前を使うため、その橋渡しをここ 1 箇所に閉じ込める。
    /// 修飾規則は ModelBone.name (Timeline/ModelBoneController.cs) と必ず一致させること
    /// </summary>
    public static class ModelBoneTrackedNames
    {
        /// <summary>タイムライン側のモデル修飾名を作る。名前が欠けていれば null</summary>
        public static string Qualify(string modelName, string boneName)
        {
            if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            return modelName + "/" + boneName;
        }

        /// <summary>編集済みエントリを修飾名にして result へ積む (result はクリアしない)</summary>
        public static void Collect(string modelName, List<BoneEditEntry> entries, List<string> result)
        {
            if (entries == null || result == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var name = Qualify(modelName, entry.boneName);
                if (name != null)
                {
                    result.Add(name);
                }
            }
        }
    }
}
```

- [ ] **Step 4: csproj へ登録する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `MaidManipulation\` ブロック(L168 付近から始まる)へアルファベット順で挿入する。`MaidManipulation\ModelBoneTrackedNames.cs` は `MaidManipulation\Maid*` 系の後、`MaidManipulation\Slot*` 等より前:

```xml
    <Compile Include="MaidManipulation\ModelBoneTrackedNames.cs" />
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelBoneTrackedNames.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ModelBoneTrackedNamesTests.cs
git commit -m "feat(tracking): モデルボーンの修飾名ヘルパーを追加する"
```

---

### Task 3: BoneEditManager に集約 EditTargetStore を持たせる

全モデルストアの編集済みボーンを 1 つの `EditTargetStore`(修飾名)へ片方向同期する。レイヤーはこれだけを見る。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs`
  - フィールド追加(L89 の `_modelStores` 宣言の直後)
  - `GetModelStore()`(L~417) / `CleanupModelStores()`(L~539) でストア集合の世代を進める
  - `Update()`(L~528)へ同期呼び出しを追加
  - `OnChangedSceneLevel()`(L~735)で集約もクリア
  - モデル名解決メソッドの追加

**Interfaces:**
- Consumes: `ModelBoneTrackedNames.Collect`(Task 2)、`BoneEditStore.version`(Task 1)、`EditTargetStore.SetNames`(M0)
- Produces: `EditTargetStore BoneEditManager.modelBoneTrackedStore { get; }` — 全モデルの編集済みボーンを修飾名で持つ読み取り専用ビュー。Task 5 の `ModelBoneTimelineLayer.trackedStore` が使う

**モデル名の解決:** `GameObject` から `ModelBoneController`(`Timeline/ModelBoneController.cs`)を取り、`controller.model.name` を使う。`StudioModelManager.models` を走査して `transform.gameObject` 一致を探す方式は取らない(コンポーネント経由なら一覧の再構築タイミングに依存しない)。

**重要 — モデル名が後から解決されるケース:** `ModelBoneController` は `StudioModelStat.transform` の setter → `CreateControllers()`(`Timeline/StudioModelStat.cs:184-194`)でしか付かず、その経路は `StudioModelManager.SetupModels()`(= タイムラインのロード)からしか通らない。一方 `BoneEditWindow` のモデルモードは `ModelProviderHost` 経由でモデルを選ぶため、**タイムライン未ロードのままモデルボーンを編集できる**。この順序では編集時点で `GetModelName` が `null` を返す。その後タイムラインをロードしても `BoneEditStore.version` は動かないため、version 差分だけを同期トリガにすると**編集済みボーンが永久に集約へ載らない**。そこで「名前を解決できなかったモデルが残っている間は、トリガに変化が無くても一定間隔で作り直す」条件を入れる。

- [ ] **Step 1: フィールドと公開プロパティを追加する**

`_modelStores` / `_deadModels` 宣言の直後へ:

```csharp
        /// <summary>
        /// 全モデルストアの編集済みボーンをモデル修飾名で集約した読み取り専用ビュー。
        /// ModelBoneTimelineLayer の追跡ストアとして使う。
        /// ソース・オブ・トゥルースはあくまで _modelStores 側で、ここへ直接 Mark/Unmark してはならない
        /// </summary>
        private readonly EditTargetStore _modelBoneTrackedStore = new EditTargetStore();

        public EditTargetStore modelBoneTrackedStore => _modelBoneTrackedStore;

        /// <summary>
        /// モデルストア集合の世代。ストアの生成・破棄で進める。
        /// version 合計だけだと生成と破棄が同フレームで釣り合ったときに変化を見逃すため、
        /// 集合の入れ替わりはこちらで別に検知する
        /// (世代が同じ = ストア集合が同じなら、各 version は単調増加なので合計の一致で判定できる)
        /// </summary>
        private int _modelStoreGeneration;

        // 集約の作り直し判定
        private int _lastModelStoreGeneration = -1;
        private int _lastModelStoreVersionSum = -1;

        // モデル名を解決できなかったモデルが前回の同期に残っていたか
        // (タイムラインのロードで後から解決されるため、残っている間は再試行する)
        private bool _hasUnresolvedModel;

        // 未解決モデルの再試行の間引き。毎フレーム GetComponent を回さないため
        private int _modelBoneSyncFrameCount;

        // 毎フレームの同期でリストを作り直さないよう使い回す
        private readonly List<string> _modelBoneTrackedNames = new List<string>();
```

- [ ] **Step 2: ストア集合の世代を進める**

`GetModelStore()` の新規生成ブロックへ:

```csharp
            if (!_modelStores.TryGetValue(model, out store))
            {
                store = new BoneEditStore();
                _modelStores[model] = store;
                _modelStoreGeneration++;
            }
```

`CleanupModelStores()` の削除ループへ:

```csharp
            foreach (var model in _deadModels)
            {
                _modelStores.Remove(model);
                _modelStoreGeneration++;
            }
```

- [ ] **Step 3: 同期メソッドを追加する**

`CleanupModelStores()` の直後へ:

```csharp
        /// <summary>
        /// モデルの編集済みボーンを集約ストアへ片方向同期する。
        /// 毎フレーム呼ばれるため、ストア集合の世代と version 合計が変わっていなければ何もしない。
        /// EditTargetStore.SetNames は中身が変わったときだけ version を進めるため、
        /// 再構築が走ってもタイムライン側のメニュー組み直しまでは連鎖しない
        /// </summary>
        private void SyncModelBoneTrackedStore()
        {
            var versionSum = 0;
            foreach (var pair in _modelStores)
            {
                versionSum += pair.Value.version;
            }

            var changed = _modelStoreGeneration != _lastModelStoreGeneration
                || versionSum != _lastModelStoreVersionSum;

            // 未解決モデルはタイムラインのロードで後から名前が付く。
            // その瞬間はストア側に何の変化も起きないため、こちらから定期的に取りに行く
            _modelBoneSyncFrameCount++;
            if (!changed && !(_hasUnresolvedModel && _modelBoneSyncFrameCount >= 30))
            {
                return;
            }
            _modelBoneSyncFrameCount = 0;
            _lastModelStoreGeneration = _modelStoreGeneration;
            _lastModelStoreVersionSum = versionSum;

            _hasUnresolvedModel = false;
            _modelBoneTrackedNames.Clear();
            foreach (var pair in _modelStores)
            {
                var modelName = GetModelName(pair.Key);
                if (modelName == null)
                {
                    // タイムライン側の名前が作れないモデルは集約に載せない。
                    // ストア側の記録は残すので、タイムラインがロードされれば次の再試行で復帰する
                    _hasUnresolvedModel = true;
                    continue;
                }
                ModelBoneTrackedNames.Collect(
                    modelName, pair.Value.GetEntries(ModelSlotKey), _modelBoneTrackedNames);
            }

            _modelBoneTrackedStore.SetNames(_modelBoneTrackedNames);
        }

        /// <summary>モデルルートからタイムライン側のモデル名を引く。取れなければ null</summary>
        private static string GetModelName(GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            var controller = model.GetComponent<MotionTimelineEditor.Plugin.ModelBoneController>();
            if (controller == null || controller.model == null)
            {
                return null;
            }
            return controller.model.name;
        }
```

冒頭の `using` に `using COM3D2.MotionTimelineEditor.Plugin;` は**足さない**(同名型の衝突を避けるため、`MotionTimelineEditor.Plugin.ModelBoneController` と完全修飾で書く)。

- [ ] **Step 4: Update とシーン遷移に組み込む**

`Update()` を次に変更:

```csharp
        public override void Update()
        {
            ReleaseBoneOnObjectSelected();
            // ポーズボーン選択はメイドルートの選択も伴うため、直前の解除で落ちた選択を
            // 同フレームで張り直す。この順序が前提
            SyncSelectedBoneFromBoneDef();
            RecordGizmoDrag();
            UpdateStores();
            CleanupModelStores();
            // 破棄済みモデルのストアを捨てた後で集約する
            SyncModelBoneTrackedStore();
        }
```

`OnChangedSceneLevel()` の `_modelStores.Clear();` の直後へ:

```csharp
            _modelBoneTrackedStore.Clear();
            _modelStoreGeneration++;
            _lastModelStoreGeneration = -1;
            _lastModelStoreVersionSum = -1;
            _hasUnresolvedModel = false;
```

- [ ] **Step 5: ビルドして通ることを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS(この時点ではまだレイヤー側が未接続なので挙動は変わらない)

- [ ] **Step 6: COM3D2 構成でもビルドする**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
Expected: 成功

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs
git commit -m "feat(tracking): モデルボーンの編集済み集合を集約ストアへ同期する"
```

---

### Task 4: 追跡部品にレイヤー向けの拡張点を 2 つ足す

(1) `AddTrackedFirstFrameKeys` は `maid == null` で早期 return する。モデルレイヤーの対象はメイドではないため、この条件を差し替えられるようにする。
(2) `_trackedBoneNames` は 30 フレーム間引きのポーリングでしか作り直されない。モデル追加/削除のようにレイヤー側が「今すぐ候補集合が変わった」と知っているときに即時反映できる無効化 API を足す。
どちらも既定の挙動は現行と同じで、表情レイヤーには影響しない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs`

**Interfaces:**
- Consumes: M0 の tracking 部品
- Produces:
  - `protected virtual bool isTrackedTargetReady => maid != null;` — Task 5 の `ModelBoneTimelineLayer` が override する
  - `protected void InvalidateTrackedBoneNames()` — 追跡集合のキャッシュを捨て、次の `UpdateTrackedBoneFilter()` で必ず作り直させる。Task 5 の `OnModelAdded` / `OnModelRemoved` が呼ぶ

- [ ] **Step 1: 仮想プロパティを追加する**

`trackedHistoryPrefix` の宣言の直後へ:

```csharp
        /// <summary>
        /// 0F 自動キーを打てる状態か。既定はメイド対象レイヤー向け。
        /// モデル対象レイヤーは maid を持たないため override して差し替える
        /// </summary>
        protected virtual bool isTrackedTargetReady => maid != null;
```

- [ ] **Step 2: AddTrackedFirstFrameKeys のガードを差し替える**

```csharp
            if (boneNames.Count == 0 || !isTrackedTargetReady)
            {
                return;
            }
```

(元の `maid == null` を `!isTrackedTargetReady` に置き換えるだけ。他の行は変えない)

- [ ] **Step 3: キャッシュ無効化 API を追加する**

`UpdateTrackedBoneFilter()` の直前へ:

```csharp
        /// <summary>
        /// 追跡集合のキャッシュを捨てる。
        /// 候補名リスト (trackedCandidateNames) がレイヤー都合で入れ替わったとき
        /// (モデルの追加・削除など) に呼ぶ。次の描画で作り直され、
        /// 間引きも解除するため UpdateTrackedBoneFilter が次フレームで必ず走る
        /// </summary>
        protected void InvalidateTrackedBoneNames()
        {
            _trackedBoneNames = null;
            _trackedRebuildFrameCount = 30;
        }
```

`_trackedBoneNames` は `?? (_trackedBoneNames = BuildTrackedBoneNames())` の遅延生成なので、null にすれば次の参照で作り直される。

**注意:** `UpdateTrackedBoneFilter()` の差分検出は `_trackedBoneNames != null` を「初回構築ではない」の判定に使っている(初回は 0F 自動キーを打たない)。ここで null に戻すと、無効化直後の再構築が初回扱いになり自動キーを打たない。モデル追加/削除は「そのモデルのボーンをまだチェックしていない」場面なので、これは望ましい挙動である(意図的にこの副作用に乗る)。

- [ ] **Step 4: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS(既定値が現行と同値なので表情の挙動は不変)

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs
git commit -m "refactor(tracking): 追跡部品にレイヤー向けの拡張点を追加する"
```

---

### Task 5: ModelBoneTimelineLayer へ追跡絞り込みを接続する

メニュー表示・キー書き込みの両方を追跡集合へ絞る(ロードマップ方針 1 の「行独立な領域は書き込みも絞る」に該当)。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelBoneTimelineLayer.cs`

**Interfaces:**
- Consumes: `BoneEditManager.modelBoneTrackedStore`(Task 3)、`isTrackedTargetReady`(Task 4)、M0 の `trackedBoneNames`
- Produces: なし(このレイヤー内で閉じる)

**変更前の挙動と変更後の対応:**

| 箇所 | 変更前 | 変更後 |
|---|---|---|
| `allBoneNames` | `modelManager.boneNames`(全ボーン) | `trackedBoneNames`(編集済み ∪ 既存キー記載) |
| `InitMenuItems` | 全ボーンをメニューへ | 追跡集合のボーンだけメニューへ |
| `UpdateFrame` | `modelManager.boneMap.Values` 全走査 | `allBoneNames` を走査 |
| `OnModelAdded` | 追加モデルの全ボーンに 0F キー(`AddFirstBones`) | 0F キーは打たない(チェックした時点で M0 部品が打つ) |
| `OnModelRemoved` | 削除モデルの全ボーンのキーを削除 | 変更なし(モデルが消えたキーの掃除は必要) |

- [ ] **Step 1: 追跡 override を追加する**

`allBoneNames` の宣言を置き換え、その直後へ override 群を足す:

```csharp
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => BoneEditManager.instance.modelBoneTrackedStore;

        protected override List<string> trackedCandidateNames => modelManager.boneNames;

        protected override string trackedHistoryPrefix => "モデルボーン";

        // モデルレイヤーは maid を持たない。モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;
```

ファイル冒頭の `using` に `using COM3D2.SceneEditor.Plugin;` を追加する(`EditTargetStore` / `BoneEditManager` の参照用。`TimelineLayerBaseTracking.cs` と同じ書き方)。

- [ ] **Step 2: InitMenuItems を追跡集合で絞る**

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            // 追跡対象だけをメニューへ出す。全ボーンを出すとモデル 1 体で数百行になる
            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.bones.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var bone in model.bones)
                {
                    if (!targetNames.Contains(bone.name))
                    {
                        continue;
                    }

                    // 対象ボーンが 1 本も無いモデルは見出しごと出さない
                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    setMenuItem.AddChild(new ModelBoneMenuItem(bone.name, bone.transform.name));
                }
            }
        }
```

- [ ] **Step 3: UpdateFrame を追跡集合で絞る**

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // 追跡対象だけキーを書く。allBoneNames を回すのはキー書き込み絞り込みの実体
            foreach (var boneName in allBoneNames)
            {
                var sourceBone = modelManager.GetBone(boneName);
                if (sourceBone == null || sourceBone.transform == null)
                {
                    // 既存キーにだけ残っているボーン (モデル差し替え等) は書けないので飛ばす
                    continue;
                }

                var trans = CreateTransformData<TransformDataModelBone>(boneName);
                trans.position = sourceBone.transform.localPosition;
                trans.rotation = sourceBone.transform.localRotation;
                trans.scale = sourceBone.transform.localScale;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }
```

- [ ] **Step 4: モデル追加/削除でキャッシュを無効化し、全ボーン 0F キーをやめる**

`modelManager.boneNames`(= `trackedCandidateNames`)はモデルの追加/削除で入れ替わる。`InitMenuItems()` を呼ぶ前に Task 4 の無効化 API を呼ばないと、`allBoneNames` が古いキャッシュのままメニューを組み、最大 30 フレーム反映が遅れる。

```csharp
        public void OnModelAdded(StudioModelStat model)
        {
            // 候補名リストが入れ替わるため、メニューを組む前にキャッシュを捨てる
            InvalidateTrackedBoneNames();

            // 追加直後のモデルは未編集なので追跡対象が無い。
            // 0F キーはボーンをチェックした時点で M0 の追跡部品が打つ
            InitMenuItems();
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var boneNames = model.bones.Select(x => x.name).ToList();
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }
```

- [ ] **Step 5: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS。特に `XmlRoundTripTests` / `MteCompatibilityTests` が通ること(既存タイムライン XML の読み込みでボーンが消えないこと。既存キー記載は `trackedBoneNames` に含まれるため対象に残る)

- [ ] **Step 6: COM3D2 構成でもビルドする**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
Expected: 成功

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelBoneTimelineLayer.cs
git commit -m "feat(timeline): モデルボーンレイヤーを編集済みボーンで絞り込む"
```

---

### Task 6: GUITreeView に追跡チェック列を追加する

ツリーの各行の先頭にチェックボックスを置けるようにする。既定(未設定)では現行と同じ見た目になる。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUITreeView.cs`

**Interfaces:**
- Consumes: `GUIView.DrawToggle(bool, float, float, Action<bool>)`、`GUIView.TrackedCheckWidth`(M0)
- Produces:
  - `public Func<T, bool> GUITreeView<T>.getChecked` — null なら列を出さない(既定)
  - `public Action<T, bool> GUITreeView<T>.onCheckChanged` — チェック操作時に呼ばれる

- [ ] **Step 1: デリゲートを宣言する**

`public Action<T> onSelected;` の直後へ:

```csharp
        /// <summary>
        /// 行頭の変更追跡チェックの状態。null なら列自体を出さない (既定)。
        /// onCheckChanged と対で設定すること
        /// </summary>
        public Func<T, bool> getChecked;

        /// <summary>チェック操作。getChecked が null なら呼ばれない</summary>
        public Action<T, bool> onCheckChanged;
```

- [ ] **Step 2: DrawRow でチェック列を描く**

`DrawRow` の `view.currentPos = ...` 以降を次に置き換える:

```csharp
            // チェック列はインデントの外へ出し、深さに関わらず縦に揃える
            var hasCheck = getChecked != null && onCheckChanged != null;
            var checkWidth = hasCheck ? GUIView.TrackedCheckWidth : 0f;

            view.currentPos = new Vector2(0, index * rowHeight);
            view.BeginHorizontal();
            {
                if (hasCheck)
                {
                    view.DrawToggle(getChecked(node), checkWidth, rowHeight,
                        newValue => onCheckChanged(node, newValue));
                }

                if (row.depth > 0)
                {
                    view.DrawEmpty(row.depth * indentWidth, rowHeight);
                }

                if (getChildCount(node) > 0)
                {
                    var id = getId(node);
                    var isExpanded = _expanded.Contains(id);
                    if (view.DrawButton(isExpanded ? "-" : "+", toggleWidth, rowHeight))
                    {
                        ToggleExpanded(id);
                    }
                }
                else
                {
                    // 子がなくてもラベルの開始位置は揃える
                    view.DrawEmpty(toggleWidth, rowHeight);
                }

                var labelWidth = contentWidth - view.currentPos.x;
                if (view.DrawButton(
                    getLabel(node), labelWidth, rowHeight, true,
                    getLabelColor(node), GUIView.gsLabel))
                {
                    onSelected(node);
                }
            }
            view.EndLayout();
```

- [ ] **Step 3: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS(`KeyFrameFlowLayoutTests` 等の既存レイアウトテストに退行がないこと)

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/GUITreeView.cs
git commit -m "feat(ui): GUITreeView に変更追跡チェック列を追加する"
```

---

### Task 7: BoneEditWindow のボーンツリーへチェックを出す

チェック ON = そのボーンを追跡対象にする(現在値を編集値として記録)、チェック OFF = リセット(元値へ戻して記録を消す)。既存の `" *"` サフィックスはチェック列と情報が重複するため落とす。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs`(`SetupTreeView`:L119-158 付近)

**Interfaces:**
- Consumes: `GUITreeView.getChecked` / `onCheckChanged`(Task 6)、`BoneEditManager.NotifyBoneEdited` / `NotifyModelBoneEdited` / `BeginEditHistory`、`BoneEditStore.ResetBone`
- Produces: なし

**注意:** チェック ON はメイド/モデルで通す API が違う。メイドは `NotifyBoneEdited`(揺れ物理の無効化を伴う)、モデルは `NotifyModelBoneEdited`。どちらも「選択中スロット / 選択中モデル」を暗黙の対象にするため、ツリーのノードは常にその対象配下である前提で呼んでよい(`GetCurrentBoneTree()` が対象配下のツリーを返すため)。

- [ ] **Step 1: getLabel から `" *"` を外す**

```csharp
            // 編集済みかは行頭のチェックで示すため、ラベルには印を付けない
            _treeView.getLabel = node => node.name;
```

- [ ] **Step 2: チェック列を配線する**

`_treeView.onSelected = ...` ブロックの直後へ:

```csharp
            _treeView.getChecked = node =>
                _drawingStore != null && _drawingStore.GetEntry(activeSlotKey, node.name) != null;

            _treeView.onCheckChanged = (node, isChecked) =>
            {
                var bone = node.transform;
                if (bone == null)
                {
                    return;
                }

                boneEditManager.BeginEditHistory(_drawingTarget,
                    (isChecked ? "ボーンを追跡: " : "ボーンの追跡を解除: ") + node.name,
                    new[] { bone });

                if (isChecked)
                {
                    // 値は変えず、現在値を編集値として記録して追跡対象に載せる
                    if (boneEditManager.isModelMode)
                    {
                        boneEditManager.NotifyModelBoneEdited(bone);
                    }
                    else
                    {
                        boneEditManager.NotifyBoneEdited(_drawingTarget, bone);
                    }
                }
                else
                {
                    // 解除は元値へ戻して記録を消す (リセットボタンと同じ意味)
                    _drawingStore.ResetBone(activeSlotKey, bone);
                }
            };
```

- [ ] **Step 3: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS

- [ ] **Step 4: COM3D2 構成でもビルドする**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
Expected: 成功

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs
git commit -m "feat(ui): ボーンツリーへ変更追跡チェックを表示する"
```

---

### Task 8: 実機確認とロードマップ更新

**Files:**
- Modify: `docs/superpowers/specs/modified-tracking-rollout-roadmap.md`(Phase M1 節)

**Interfaces:**
- Consumes: Task 1〜7 の成果
- Produces: なし

- [ ] **Step 1: ユーザーへ実機確認を依頼する**

以下のチェックリストを提示して確認してもらう(このセッションではゲームを操作しない)。

**モデルボーン編集ウィンドウ**
1. ボーン編集ウィンドウをモデルモードで開き、モデルを選択する。ツリー各行の先頭にチェックが出て、全て未チェックであること
2. 任意のボーンをギズモ/スライダーで動かすと、そのボーンのチェックが自動で入ること
3. チェックを手動で入れると、値が変わらないまま追跡対象になること(タイムラインのメニューに出る)
4. チェックを外すと元値へ戻り、ツリーの行が未チェックへ戻ること
5. 「ボーンをリセット」「モデルをリセット」ボタンでもチェックが外れること
6. undo/redo でチェック状態が追従すること

**タイムライン(モデルボーンレイヤー)**
7. タイムラインを開いた状態でボーンをチェックすると、そのボーンがボーンメニューへ現れ、0F にキーが自動登録されること
8. チェックを外すと、そのボーンのキーが全フレームから消え、メニューからも消えること
9. 未チェックのボーンを動かしてもキーが書かれないこと(自動チェックが入るので、実際には「動かすとチェックが入ってからキーが書かれる」になる)
10. モデルを追加しても、そのモデルのボーンが 0F へ一括登録されないこと(従来は全ボーンが登録されていた)
11. モデルを削除すると、そのモデルのキーが消えること
12. 既存のモデルボーンキーを含むタイムラインを読み込むと、未チェックでもそのボーンがメニューに出て編集できること
13. モデルを複製したとき、複製元のキーが複製先へ引き継がれること(`OnCopyModel`)

**タイムライン未ロードで編集したケース(Task 3 の遅延解決)**

14. **タイムラインを一度も開かない/ロードしない状態で**モデルボーンを編集する → その後タイムラインを作成しモデルを追加する → 編集済みボーンがメニューに現れること(集約ストアはモデル名を解決できるまで最大 30 フレーム間隔で再試行する。数秒待っても出ない場合は同期条件のバグ)
15. 同じ手順で、編集済みボーンにだけ 0F キーが付いていること(未編集ボーンには付かないこと)

**シーンプリセット**
16. モデルボーンを編集してシーンプリセットを保存 → 読み込みで、従来通り編集が復元されること(`ScenePresetManager.cs:801` の保存経路は `BoneEditStore` 由来のままで変更していないため、差分は無いはず)

- [ ] **Step 2: 指摘があれば修正し、再ビルド・再テストしてコミットする**

修正のたびに Global Constraints のビルド 2 構成 + `dotnet test` を回す。

- [ ] **Step 3: ロードマップの Phase M1 を完了として更新する**

`docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の `### Phase M1: モデルボーン(構造的に最も安い)` を `### Phase M1: モデルボーン ✅ 完了 (YYYY-MM-DD)` に変え、M0 節と同じ体裁で「実装の要点」を追記する。最低限次を書く:

- `BoneEditStore.version` はエントリ集合の増減だけを表す(値の更新では進まない)。厳密な変更回数ではなく変更有無のシグナル
- 集約は `BoneEditManager.modelBoneTrackedStore`(モデル修飾名)への片方向同期。逆流経路は無い
- モデル名の解決は `ModelBoneController` コンポーネント経由。ただし同コンポーネントはタイムラインのロード時にしか付かないため、**解決できないモデルが残っている間は 30 フレーム間隔で同期を再試行する**(タイムライン未ロードで編集したモデルを後から拾うため)
- ストア集合の入れ替わりは `_modelStoreGeneration` で検知する(version 合計だけだと生成と破棄が同フレームで釣り合ったときに見逃す)
- `TimelineLayerBase.InvalidateTrackedBoneNames()` — 候補名リストがレイヤー都合で入れ替わったときの即時反映用。呼ばないと最大 30 フレーム遅れる
- `ModelBoneTimelineLayer.OnModelAdded` の全ボーン 0F 一括登録を廃止した(仕様変更)
- `GUITreeView.getChecked` / `onCheckChanged` は opt-in。未設定のツリーは見た目が変わらない

- [ ] **Step 4: コミット**

```bash
git add docs/superpowers/specs/modified-tracking-rollout-roadmap.md
git commit -m "docs: 変更追跡ロードマップの M1 完了を記録する"
```

---

## Self-Review

**Spec coverage(ロードマップ Phase M1 の 5 項目):**

| ロードマップの項目 | 対応タスク |
|---|---|
| `BoneEditStore` に version カウンタを追加 | Task 1 |
| `transform.name` ↔ モデル修飾名の対応表を整備 | Task 2(`ModelBoneTrackedNames`)+ Task 3(モデル名の解決) |
| `ModelBoneTimelineLayer` に M0 部品を接続(書き込みも絞る) | Task 4(発火条件の差し替え)+ Task 5 |
| BoneEditWindow のボーンツリーへチェック表示 | Task 6(GUITreeView)+ Task 7(配線) |
| シーンプリセットのモデルボーン保存は差分が無いことの確認のみ | Task 8 Step 1 項目 14 |

**方針との整合:**
- 方針 1(行独立な領域は書き込みも絞る): モデルボーンは行独立の編集なので書き込みも絞る — Task 5
- 方針 2(片方向同期): `BoneEditStore` → 集約 `EditTargetStore` の一方向のみ — Task 3
- 方針 3(現物に無い名前は捨てない): `BoneEditStore` の記録はそのまま残し、集約に載らないだけ — Task 3 Step 2 のコメント / Task 5 Step 3 の `sourceBone == null` スキップ
- 方針 4(ポーリングからイベント/世代カウンタへ): version 合計比較で毎フレームのフル走査を避ける — Task 3。M0 の 30F 間引きポーリングはそのまま残るが、モデルボーンでは store.version 経由の即時検知が主経路になる
- 方針 5(Undo 前提): ボーン編集は既に `BeginEditHistory` 対応済みなので前提タスク不要 — Task 7 で既存 API を使う

## レビュー却下メモ

- 「count + versionSum の整数和は理論上衝突しうる」— **取り込み済み**。`_modelStoreGeneration` による集合の入れ替わり検知へ変更した(Task 3 Step 1-2)
- 「`ModelBoneController` の遅延生成で同期漏れ」— **取り込み済み**。未解決モデルが残る間の再試行を追加(Task 3 Step 3)+ 実機確認 14・15 を追加
- 「モデル追加直後の `InitMenuItems` が古いキャッシュを見る(最大 30F 遅延)」— **取り込み済み**。`InvalidateTrackedBoneNames()` を新設(Task 4 Step 3)し `OnModelAdded` / `OnModelRemoved` から呼ぶ(Task 5 Step 4)
- 「`ResetSlot` で version が二重に進む」— **コメントのみ修正して取り込み**。version は変更有無のシグナルなので実装は変えない(Task 1 Step 3)

## 残リスク(計画外メモ)

- `ModelBoneTimelineLayer.OnModelAdded` の全ボーン 0F 一括登録廃止は既存プロジェクトの作成手順を変える。Task 8 の実機確認 10 番で影響を確認する
- `EditTargetStore` は「名前集合」しか持たないため、同名ボーンを持つモデルが複数あっても修飾名で区別できる(`ModelBone.name` と同規則のため)。ただし `model.name` が同一になるモデルが 2 体存在する場合は区別できない — これは既存の `StudioModelManager.boneMap` も同じ制約なので新規リスクではない
