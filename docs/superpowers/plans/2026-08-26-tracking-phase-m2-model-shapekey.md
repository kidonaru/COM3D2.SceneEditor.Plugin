# 変更追跡 Phase M2: モデルシェイプキー 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** モデルのシェイプキーに変更追跡チェックを導入し、`ModelShapeKeyTimelineLayer` のメニュー・キー書き込みと、シーンプリセットの `modelShapeKeys` 保存をチェック済みシェイプキーへ絞り込む。あわせて、M1 で作った「per-model 記録 → モデル修飾名の集約ストア」の仕組みを再利用可能な部品へ抜き出し、モデル名が振り直されたときの取りこぼしを両方で塞ぐ。

**Architecture:** モデル修飾名（`"{model.name}/{...}"`）は **安定した識別子ではない**。`model.name` は `info.fileName + groupSuffix` で、`group` は `ModelHackManager.FixGroup()` がモデル一覧を引くたびに列挙順で振り直す（`Timeline/Manager/ModelHackManager.cs:218-235`、`Timeline/PluginUtils.cs:131-138`）。同じファイル名のモデルを 2 体置いて片方を消すと、**消していない方の修飾名が変わる**。したがって追跡集合を修飾名のフラットな集合として持つと、無関係なモデルの削除でチェックが迷子になる。

そこで M1 と同じ二段構えを取る: **記録は「モデル（`GameObject`）ごと × 生名」で持ち、タイムラインへ渡す修飾名は毎回そこから組み直す**。組み直しの仕組み（世代・version 合計による変更検知、未解決モデルの再試行）は M1 の `BoneEditManager` に埋まっているので、**Unity 非依存のジェネリック部品 `ModelTrackedNameStore<TKey>` へ抜き出して M1 と M2 で共有**する。抜き出しにより、これまでテストできなかった変更検知ロジックが単体テストできるようになる。

**Tech Stack:** C#(プラグイン本体は旧形式 csproj / `<Compile Include>` 明示列挙)、xUnit(net48、COM3D25 構成のプラグイン DLL 参照)、MSBuild 2 構成(COM3D2 / COM3D25)

**Spec:** `docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の「Phase M2」節

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` / `deploy.bat` / `release.bat` は実行しない**。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
- テストは COM3D25 構成の DLL を参照するため、**COM3D25 構成ビルドを最後に流してから `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`** を実行する（COM3D2 構成でビルドし直した直後はテストプロジェクトが「プラグインが未ビルドです」で止まる）
- 新規 .cs は `source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ追加する（テストプロジェクトは SDK 形式なので追加不要）
- xUnit は Unity ランタイム無しで動くため、`Transform` / `GameObject` / `Mesh` を生成するコードはテストできない。**新規部品はこの制約を回避するためにジェネリックにする**（`TKey = string` でテストする）
- **HistoryManager 対応はこの Phase のスコープ外**。`ShapeKeyEditWindow` は SE の HistoryManager に未対応で（`timeline-remaining-work.md` §4）、対応コストが大きい。ロードマップ Phase M2 の縮退条項に従い「モデルシェイプキーのチェック集合は履歴に含めない」と明記して先送りする。undo でチェック集合は戻らない
- 実機確認はユーザーに依頼する（このセッションでゲームを操作しない）

## モデル名が振り直される条件（この計画の前提）

```csharp
// Timeline/Manager/ModelHackManager.cs:218-235
private void FixGroup(List<StudioModelStat> models)
{
    _modelGroupMap.Clear();
    foreach (var model in models)
    {
        int group = 0;
        if (_modelGroupMap.TryGetValue(model.info.fileName, out group))
        {
            group++;
            if (group == 1) group++; // 1は使わない
        }
        model.SetGroup(group);
        _modelGroupMap[model.info.fileName] = group;
    }
}
```

`modelList` の getter が呼ばれるたびに走る。同じ `fileName` のモデルが複数あるとき、番号の若い方を消すと残りが繰り上がり `model.name` が変わる。これに追従するには **モデル一覧が変わった契機で集約を作り直す** 必要がある。`StudioModelManager.onModelAdded` / `onModelRemoved`（static イベント）がその契機に一致するので、これを購読して集約を無効化する。

**M1 も同じ穴を持つ**: `BoneEditManager.SyncModelBoneTrackedStore` はストア集合の世代と version 合計でしか作り直しを判断しないため、「一度も編集していない同名モデルを消した」場合はどちらも動かず、生き残ったモデルの修飾名がずれたまま放置される。Task 2 でまとめて塞ぐ。

## M1 との差分

| 論点 | M1: モデルボーン | M2: モデルシェイプキー |
|---|---|---|
| per-model の記録 | `BoneEditStore`（値も持つ差分ストア） | `EditTargetStore`（名前集合のみ。値は `ModelBlendShape.weight` が現物） |
| 生名 | `transform.name` | `ModelBlendShape.shapeKeyName` |
| 修飾名 | `ModelBone.name` | `ModelBlendShape.name`（`Timeline/BlendShapeController.cs:15-18`） |
| モデル名の解決 | `ModelBoneController` コンポーネント経由 | `BlendShapeController` コンポーネント経由 |
| 履歴 | 対応済み（`BeginEditHistory`） | **未対応（スコープ外）** |

---

### Task 1: 集約ストアの共通部品を作る

M1 の `BoneEditManager` に埋まっている「per-model 記録 → 修飾名の集約」ロジックを、Unity 非依存のジェネリック部品として切り出す。ここで初めて単体テストが書ける。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelTrackedNameStore.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` 追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelTrackedNameStoreTests.cs`（新規）

**Interfaces:**
- Consumes: `EditTargetStore`（M0）
- Produces: `class ModelTrackedNameStore<TKey>` — Task 2（M1 の載せ替え）と Task 3（M2 の新規ストア）が使う
  - `EditTargetStore store { get; }` — 集約結果（修飾名）
  - `void Invalidate()` — 次の `Sync` で必ず作り直させる（モデル一覧変化・シーン遷移用）
  - `void Sync(IList<TKey> keys, Func<TKey,int> getVersion, Func<TKey, List<string>, bool> tryCollect)` — 毎フレーム呼ぶ。`tryCollect` は「そのモデルの修飾名を result へ積む。名前を解決できなければ false」
  - `void Clear()` — 集約を空にして判定状態も初期化する

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelTrackedNameStoreTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 集約ストアの作り直し判定を固定する。
    /// モデル修飾名は group 振り直しで変わりうるため、
    /// 「version が動かない名前変更」にも Invalidate 経由で追従できなければならない
    /// </summary>
    public class ModelTrackedNameStoreTests
    {
        /// <summary>テスト用の疑似モデル。キーは string、生名と version を自分で持つ</summary>
        private class FakeModel
        {
            public string prefix;
            public List<string> rawNames = new List<string>();
            public int version;
            public bool resolvable = true;
        }

        private static ModelTrackedNameStore<string> CreateStore(
            Dictionary<string, FakeModel> models, out List<string> keys)
        {
            keys = new List<string>(models.Keys);
            return new ModelTrackedNameStore<string>();
        }

        private static void Sync(
            ModelTrackedNameStore<string> store,
            List<string> keys,
            Dictionary<string, FakeModel> models)
        {
            store.Sync(
                keys,
                key => models[key].version,
                (key, result) =>
                {
                    var model = models[key];
                    if (!model.resolvable)
                    {
                        return false;
                    }
                    foreach (var name in model.rawNames)
                    {
                        result.Add(model.prefix + "/" + name);
                    }
                    return true;
                });
        }

        [Fact]
        public void 初回のSyncで修飾名が集約される()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel { prefix = "model.menu", rawNames = { "Key1", "Key2" } },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);

            Sync(store, keys, models);

            Assert.True(store.store.IsModified("model.menu/Key1"));
            Assert.True(store.store.IsModified("model.menu/Key2"));
        }

        [Fact]
        public void versionが変わると集約し直す()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel { prefix = "model.menu", rawNames = { "Key1" } },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);
            Sync(store, keys, models);

            models["a"].rawNames.Add("Key2");
            models["a"].version++;
            Sync(store, keys, models);

            Assert.True(store.store.IsModified("model.menu/Key2"));
        }

        [Fact]
        public void versionが同じなら集約し直さない()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel { prefix = "model.menu", rawNames = { "Key1" } },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);
            Sync(store, keys, models);

            // version を動かさずに名前だけ変える (group 振り直し相当)
            models["a"].prefix = "model.menu (2)";
            Sync(store, keys, models);

            Assert.True(store.store.IsModified("model.menu/Key1"));
            Assert.False(store.store.IsModified("model.menu (2)/Key1"));
        }

        [Fact]
        public void Invalidateすればversionが同じでも集約し直す()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel { prefix = "model.menu", rawNames = { "Key1" } },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);
            Sync(store, keys, models);

            models["a"].prefix = "model.menu (2)";
            store.Invalidate();
            Sync(store, keys, models);

            Assert.True(store.store.IsModified("model.menu (2)/Key1"));
            Assert.False(store.store.IsModified("model.menu/Key1"));
        }

        [Fact]
        public void 未解決モデルは一定フレーム後に再試行される()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel
                {
                    prefix = "model.menu", rawNames = { "Key1" }, resolvable = false,
                },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);
            Sync(store, keys, models);
            Assert.False(store.store.IsModified("model.menu/Key1"));

            // 名前が解決できるようになっても version は動かない (タイムラインのロード相当)
            models["a"].resolvable = true;
            for (var i = 0; i < ModelTrackedNameStore<string>.RetryInterval; i++)
            {
                Sync(store, keys, models);
            }

            Assert.True(store.store.IsModified("model.menu/Key1"));
        }

        [Fact]
        public void Clearで集約が空になる()
        {
            var models = new Dictionary<string, FakeModel>
            {
                ["a"] = new FakeModel { prefix = "model.menu", rawNames = { "Key1" } },
            };
            List<string> keys;
            var store = CreateStore(models, out keys);
            Sync(store, keys, models);

            store.Clear();

            Assert.True(store.store.isEmpty);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter ModelTrackedNameStoreTests`
Expected: コンパイルエラー（`ModelTrackedNameStore` が存在しない）

- [ ] **Step 3: 部品を実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelTrackedNameStore.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルごとの記録を「モデル修飾名の集合」へ集約する読み取り専用ビュー。
    /// タイムライン側の候補名はモデル修飾名だが、修飾名は group 振り直し
    /// (ModelHackManager.FixGroup) で変わりうるため、生名の記録を正とし
    /// 修飾名はここで毎回組み直す。
    /// Unity 型に依存しないよう対象キーはジェネリックにしてある (テスト可能性のため)
    /// </summary>
    public class ModelTrackedNameStore<TKey>
    {
        /// <summary>名前を解決できないモデルを取りに行く間隔 (フレーム)</summary>
        public const int RetryInterval = 30;

        private readonly EditTargetStore _store = new EditTargetStore();

        /// <summary>集約結果。タイムラインレイヤーの trackedStore として渡す</summary>
        public EditTargetStore store => _store;

        // 作り直し判定。世代は「対象集合そのものが入れ替わった」ことを表し、
        // version 合計は「各モデルの記録が変わった」ことを表す。
        // 世代が同じなら各 version は単調増加なので、合計の一致で変化なしと判定できる
        private int _generation;
        private int _lastGeneration = -1;
        private int _lastVersionSum = -1;

        // 名前を解決できなかったモデルが前回の集約に残っていたか
        private bool _hasUnresolved;
        private int _frameCount;

        // 毎フレームの集約でリストを作り直さないよう使い回す
        private readonly List<string> _names = new List<string>();

        /// <summary>
        /// 次の Sync で必ず作り直させる。
        /// モデルの追加・削除でモデル名が振り直されるが、それは記録側の version を動かさないため、
        /// 一覧が変わった契機ではこれを呼ぶ必要がある
        /// </summary>
        public void Invalidate()
        {
            _generation++;
        }

        /// <summary>集約を空にして判定状態も初期化する (シーン遷移用)</summary>
        public void Clear()
        {
            _store.Clear();
            _generation++;
            _lastGeneration = -1;
            _lastVersionSum = -1;
            _hasUnresolved = false;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。前回から変化が無ければ何もしない。
        /// tryCollect はそのモデルの修飾名を result へ積む。名前を解決できなければ false を返すこと
        /// (タイムライン未ロードなど。解決できるようになるまで RetryInterval ごとに再試行する)
        /// </summary>
        public void Sync(
            IList<TKey> keys,
            Func<TKey, int> getVersion,
            Func<TKey, List<string>, bool> tryCollect)
        {
            var versionSum = 0;
            for (var i = 0; i < keys.Count; i++)
            {
                versionSum += getVersion(keys[i]);
            }

            var changed = _generation != _lastGeneration || versionSum != _lastVersionSum;

            _frameCount++;
            if (!changed && !(_hasUnresolved && _frameCount >= RetryInterval))
            {
                return;
            }
            _frameCount = 0;
            _lastGeneration = _generation;
            _lastVersionSum = versionSum;

            _hasUnresolved = false;
            _names.Clear();
            for (var i = 0; i < keys.Count; i++)
            {
                if (!tryCollect(keys[i], _names))
                {
                    // 記録は呼び出し側に残るので、解決できるようになれば次の再試行で復帰する
                    _hasUnresolved = true;
                }
            }

            // EditTargetStore.SetNames は中身が変わったときだけ version を進めるため、
            // 集約し直してもタイムライン側のメニュー組み直しまでは連鎖しない
            _store.SetNames(_names);
        }
    }
}
```

**注意:** `keys` は毎フレーム渡される。呼び出し側は使い回しリストを渡してよい（`Sync` は内容を書き換えない）。`keys` の中身が入れ替わったときは呼び出し側が `Invalidate()` を呼ぶこと（`Sync` は集合の同一性を見ない）。

- [ ] **Step 4: csproj へ登録する**

`MaidManipulation\ModelBoneTrackedNames.cs` の直後へ:

```xml
    <Compile Include="MaidManipulation\ModelTrackedNameStore.cs" />
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelTrackedNameStore.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ModelTrackedNameStoreTests.cs
git commit -m "feat(tracking): モデル修飾名の集約部品を切り出す"
```

---

### Task 2: M1（モデルボーン）を共通部品へ載せ替え、名前振り直しに追従させる

`BoneEditManager` の手書き集約を Task 1 の部品へ置き換える。同時に、モデル一覧が変わった契機で `Invalidate()` するようにして「一度も編集していない同名モデルを消すと生き残りの修飾名がずれる」穴を塞ぐ。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs`
  - フィールド群（L94-123）を部品 1 つへ置き換え
  - `GetModelStore` / `CleanupModelStores` の `_modelStoreGeneration++` を `Invalidate()` へ
  - `SyncModelBoneTrackedStore` を部品呼び出しへ縮小
  - `Init()` でモデル一覧イベントを購読、`OnPluginDisable()` で解除
  - `OnChangedSceneLevel` を `Clear()` へ

**Interfaces:**
- Consumes: `ModelTrackedNameStore<GameObject>`（Task 1）、`ModelBoneTrackedNames.Collect`（M1）
- Produces: `EditTargetStore BoneEditManager.modelBoneTrackedStore { get; }` — **公開シグネチャは変えない**（`ModelBoneTimelineLayer` は無変更）

- [ ] **Step 1: フィールドを置き換える**

L94-123 の集約関連フィールド（`_modelBoneTrackedStore` / `_modelStoreGeneration` / `_lastModelStoreGeneration` / `_lastModelStoreVersionSum` / `_hasUnresolvedModel` / `ModelBoneSyncRetryInterval` / `_modelBoneSyncFrameCount` / `_modelBoneTrackedNames`）を次に置き換える:

```csharp
        /// <summary>
        /// 全モデルストアの編集済みボーンをモデル修飾名で集約した読み取り専用ビュー。
        /// ModelBoneTimelineLayer の追跡ストアとして使う。
        /// ソース・オブ・トゥルースはあくまで _modelStores 側で、ここへ直接 Mark/Unmark してはならない
        /// </summary>
        private readonly ModelTrackedNameStore<GameObject> _modelBoneTracked
            = new ModelTrackedNameStore<GameObject>();

        public EditTargetStore modelBoneTrackedStore => _modelBoneTracked.store;

        // Sync へ毎フレーム渡すキー列。使い回してゴミを出さない
        private readonly List<GameObject> _modelStoreKeys = new List<GameObject>();
```

- [ ] **Step 2: 世代更新を Invalidate へ置き換える**

`GetModelStore()`:

```csharp
            if (!_modelStores.TryGetValue(model, out store))
            {
                store = new BoneEditStore();
                _modelStores[model] = store;
                _modelBoneTracked.Invalidate();
            }
```

`CleanupModelStores()` の削除ループ:

```csharp
            foreach (var model in _deadModels)
            {
                _modelStores.Remove(model);
                _modelBoneTracked.Invalidate();
            }
```

- [ ] **Step 3: 同期メソッドを部品呼び出しへ縮小する**

`SyncModelBoneTrackedStore()` を次に置き換える（`GetModelName` は残す）:

```csharp
        /// <summary>モデルの編集済みボーンを集約ストアへ片方向同期する</summary>
        private void SyncModelBoneTrackedStore()
        {
            _modelStoreKeys.Clear();
            foreach (var pair in _modelStores)
            {
                _modelStoreKeys.Add(pair.Key);
            }

            _modelBoneTracked.Sync(
                _modelStoreKeys,
                model => _modelStores[model].version,
                (model, result) =>
                {
                    var modelName = GetModelName(model);
                    if (modelName == null)
                    {
                        return false;
                    }
                    ModelBoneTrackedNames.Collect(
                        modelName, _modelStores[model].GetEntries(ModelSlotKey), result);
                    return true;
                });
        }
```

- [ ] **Step 4: モデル一覧の変化を購読する**

`Init()` の末尾へ:

```csharp
            // モデルの増減で group が振り直され、モデル修飾名が変わる
            // (ModelHackManager.FixGroup)。記録側の version は動かないため、
            // 一覧が変わった契機で集約を作り直させる
            MotionTimelineEditor.Plugin.StudioModelManager.onModelAdded += OnTimelineModelChanged;
            MotionTimelineEditor.Plugin.StudioModelManager.onModelRemoved += OnTimelineModelChanged;
```

`Init()` の下へメソッドを足す:

```csharp
        private void OnTimelineModelChanged(MotionTimelineEditor.Plugin.StudioModelStat model)
        {
            _modelBoneTracked.Invalidate();
        }

        public override void OnPluginDisable()
        {
            MotionTimelineEditor.Plugin.StudioModelManager.onModelAdded -= OnTimelineModelChanged;
            MotionTimelineEditor.Plugin.StudioModelManager.onModelRemoved -= OnTimelineModelChanged;
        }
```

**注意:** `BoneEditManager` は `ManagerBase` 派生で `Init()` / `OnPluginDisable()` はどちらも `virtual`（`Timeline/Manager/ManagerBase.cs`）。既に `Init()` に `GizmoRenderer.externalTargetProvider` の設定があるので、その後ろへ足すこと。`OnPluginDisable()` は現在未 override なら新規に足す。

- [ ] **Step 5: シーン遷移のクリアを置き換える**

`OnChangedSceneLevel()` の集約クリア部分を次に置き換える:

```csharp
            _modelBoneTrackedStore はここでは触らず、部品側でまとめて初期化する
            _modelBoneTracked.Clear();
```

（実際に書くのは `_modelBoneTracked.Clear();` の 1 行。旧コードの `_modelBoneTrackedStore.Clear();` / `_modelStoreGeneration++;` / `_lastModelStoreGeneration = -1;` / `_lastModelStoreVersionSum = -1;` / `_hasUnresolvedModel = false;` の 5 行を消して置き換える）

- [ ] **Step 6: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS（`ModelBoneTimelineLayer` は無変更で動くこと）

- [ ] **Step 7: COM3D2 構成でもビルドする**

Run: `MSBuild ... /p:GameVersion=COM3D2` → その後 COM3D25 構成をもう一度ビルド
Expected: 成功

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs
git commit -m "fix(tracking): モデル名の振り直しにモデルボーンの集約を追従させる"
```

---

### Task 3: モデルシェイプキーの変更追跡ストアを置く

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelShapeKeyEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` 追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:373-374` 付近（マネージャ登録）

**Interfaces:**
- Consumes: `EditTargetStore`（M0）、`ModelTrackedNameStore<GameObject>`（Task 1）、`ManagerBase`
- Produces:
  - `EditTargetStore ModelShapeKeyEditManager.instance.GetStore(GameObject model)` — そのモデルの生名（`shapeKeyName`）チェック集合。無ければ作る
  - `EditTargetStore ModelShapeKeyEditManager.instance.FindStore(GameObject model)` — 生成しない読み取り用
  - `EditTargetStore ModelShapeKeyEditManager.instance.trackedStore` — モデル修飾名の集約。Task 6 のレイヤーが使う
  - `EditTargetStore ModelShapeKeyEditManager.instance.GetStore(MTEP.StudioModelStat model)` は作らない（呼び出し側が `model.transform.gameObject` を渡す）

- [ ] **Step 1: マネージャを作る**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelShapeKeyEditManager.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルシェイプキーの変更追跡。
    /// 記録はモデル (GameObject) ごとに生名 (shapeKeyName) で持ち、
    /// タイムラインへ渡すモデル修飾名は集約側で毎回組み直す。
    /// 修飾名を直接記録に使わないのは、group 振り直しで名前が変わりうるため
    /// (ModelHackManager.FixGroup)
    /// </summary>
    public class ModelShapeKeyEditManager : MTEP.ManagerBase
    {
        private static ModelShapeKeyEditManager _instance;
        public static ModelShapeKeyEditManager instance
            => _instance ?? (_instance = new ModelShapeKeyEditManager());

        private ModelShapeKeyEditManager()
        {
        }

        private readonly Dictionary<GameObject, EditTargetStore> _stores
            = new Dictionary<GameObject, EditTargetStore>();

        private readonly List<GameObject> _deadModels = new List<GameObject>();

        private readonly ModelTrackedNameStore<GameObject> _tracked
            = new ModelTrackedNameStore<GameObject>();

        // Sync へ毎フレーム渡すキー列。使い回してゴミを出さない
        private readonly List<GameObject> _keys = new List<GameObject>();

        /// <summary>モデル修飾名の集約。ModelShapeKeyTimelineLayer の追跡ストアとして使う</summary>
        public EditTargetStore trackedStore => _tracked.store;

        /// <summary>モデルのチェック集合 (生名)。無ければ作る</summary>
        public EditTargetStore GetStore(GameObject model)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(model, out store))
            {
                store = new EditTargetStore();
                _stores[model] = store;
                _tracked.Invalidate();
            }
            return store;
        }

        /// <summary>既存のチェック集合を引くだけで新規生成はしない (表示用)</summary>
        public EditTargetStore FindStore(GameObject model)
        {
            EditTargetStore store;
            return model != null && _stores.TryGetValue(model, out store) ? store : null;
        }

        public override void Init()
        {
            // モデルの増減で group が振り直され、モデル修飾名が変わる
            // (ModelHackManager.FixGroup)。記録側の version は動かないため、
            // 一覧が変わった契機で集約を作り直させる
            MTEP.StudioModelManager.onModelAdded += OnTimelineModelChanged;
            MTEP.StudioModelManager.onModelRemoved += OnTimelineModelChanged;
        }

        public override void OnPluginDisable()
        {
            MTEP.StudioModelManager.onModelAdded -= OnTimelineModelChanged;
            MTEP.StudioModelManager.onModelRemoved -= OnTimelineModelChanged;
        }

        private void OnTimelineModelChanged(MTEP.StudioModelStat model)
        {
            _tracked.Invalidate();
        }

        public override void Update()
        {
            CleanupStores();
            SyncTrackedStore();
        }

        /// <summary>削除されたモデルの記録を捨てる (モデルに着替えは無いので破棄検出だけでよい)</summary>
        private void CleanupStores()
        {
            _deadModels.Clear();
            foreach (var pair in _stores)
            {
                if (pair.Key == null)
                {
                    _deadModels.Add(pair.Key);
                }
            }
            foreach (var model in _deadModels)
            {
                _stores.Remove(model);
                _tracked.Invalidate();
            }
        }

        private void SyncTrackedStore()
        {
            _keys.Clear();
            foreach (var pair in _stores)
            {
                _keys.Add(pair.Key);
            }

            _tracked.Sync(
                _keys,
                model => _stores[model].version,
                (model, result) =>
                {
                    var modelName = GetModelName(model);
                    if (modelName == null)
                    {
                        return false;
                    }
                    foreach (var shapeKeyName in _stores[model].GetNames())
                    {
                        var name = ModelBoneTrackedNames.Qualify(modelName, shapeKeyName);
                        if (name != null)
                        {
                            result.Add(name);
                        }
                    }
                    return true;
                });
        }

        /// <summary>
        /// モデルルートからタイムライン側のモデル名を引く。取れなければ null。
        /// BlendShapeController はタイムラインのロード時にしか付かないため、
        /// 未ロードのうちは解決できず、集約側の再試行に任せる
        /// </summary>
        private static string GetModelName(GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            var controller = model.GetComponent<MTEP.BlendShapeController>();
            if (controller == null || controller.model == null)
            {
                return null;
            }
            return controller.model.name;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でモデルが全て入れ替わるため記録を丸ごと捨てる (BoneEditManager と同じ方式)
            _stores.Clear();
            _tracked.Clear();
        }
    }
}
```

**注意:** `ModelBoneTrackedNames.Qualify` はモデルボーン専用の名前ではなく「モデル修飾名の作り方」を表す汎用ヘルパーなので、シェイプキーからも使う。`ModelBlendShape.name`（`"{model.name}/{shapeKeyName}"`）と `ModelBone.name` は同じ規則である（`Timeline/BlendShapeController.cs:15-18` と `Timeline/ModelBoneController.cs:17-20` を突き合わせて確認済み）。

- [ ] **Step 2: csproj へ登録する**

`MaidManipulation\ModelTrackedNameStore.cs` の直後へ:

```xml
    <Compile Include="MaidManipulation\ModelShapeKeyEditManager.cs" />
```

（`<Compile Include>` の並びは厳密なアルファベット順ではないので、`ModelBoneTrackedNames` / `ModelTrackedNameStore` の近くに置けばよい）

- [ ] **Step 3: マネージャを登録する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs` の `managerRegistry.RegisterManager(FaceEditManager.instance);` の直後へ:

```csharp
                managerRegistry.RegisterManager(ModelShapeKeyEditManager.instance);
```

- [ ] **Step 4: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS（この時点ではまだ誰もストアを読み書きしないので挙動は変わらない）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelShapeKeyEditManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs
git commit -m "feat(tracking): モデルシェイプキーの変更追跡ストアを追加する"
```

---

### Task 4: チェック + ラベル行のヘルパーを GUIView へ足す

シェイプキー名は長くスライダーと同じ行に収まらないため、既存 UI は「ラベル行 + スライダー行」の 2 行構成になっている。M0 の `DrawTrackedSliderValue`（1 行にラベルもスライダーも詰める）は使えないので、ラベル行にチェックを置くヘルパーを足す。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs`（**submodule MTEUtils**。submodule 内でコミットしてから親リポの参照を更新する）

**Interfaces:**
- Consumes: `GUIView.DrawToggle(bool, float, float, Action<bool>)`、`GUIView.TrackedCheckWidth`（M0）
- Produces: `void GUIView.DrawTrackedLabel(bool isChecked, Action<bool> onCheckChanged, string label, float width, float height)` — Task 5 が使う

- [ ] **Step 1: ヘルパーを追加する**

`DrawTrackedToggle` の直後へ:

```csharp
        /// <summary>
        /// 変更追跡チェック付きのラベル行。
        /// 値の編集欄が別行になる縦長レイアウト (シェイプキー等) 向け
        /// </summary>
        public void DrawTrackedLabel(
            bool isChecked, Action<bool> onCheckChanged,
            string label, float width, float height)
        {
            BeginHorizontal();
            {
                DrawToggle(isChecked, TrackedCheckWidth, height, onCheckChanged);
                DrawLabel(label, width, height);
            }
            EndLayout();
        }
```

- [ ] **Step 2: ビルド**

Run: `MSBuild ... /p:GameVersion=COM3D25`
Expected: 成功

- [ ] **Step 3: submodule と親リポの両方でコミット**

```bash
cd source/COM3D2.SceneEditor.Plugin/MTEUtils
git add GUIView.cs
git commit -m "feat(gui): 変更追跡チェック付きのラベル行ヘルパーを追加する"
cd ../../..
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(submodule): MTEUtils を DrawTrackedLabel 対応へ更新する"
```

---

### Task 5: ShapeKeyEditWindow のモデルタブへチェックを出す

チェック ON = 現在の重みのまま追跡対象に載せる。OFF = 重みを 0 に戻して追跡から外す（表情タブと同じ意味。`MaidFaceWindow.cs:241-259` の `onCheckChanged` が手本）。スライダーを動かしたら自動でチェックが入る。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs`
  - クラス冒頭のコメント（L8-12）
  - `DrawModelContent()`（L264-322）

**Interfaces:**
- Consumes: `ModelShapeKeyEditManager.instance.GetStore/FindStore`（Task 3）、`GUIView.DrawTrackedLabel`（Task 4）
- Produces: なし

- [ ] **Step 1: using を足す**

ファイル冒頭に `using System;`（`Action<bool>` 用）が無ければ追加する。

- [ ] **Step 2: 描画ループを差し替える**

`DrawModelContent()` の `view.SetEnabled(view.focusedComboBox == null);` 以降を次に置き換える:

```csharp
            view.SetEnabled(view.focusedComboBox == null);

            var modelObject = model.transform.gameObject;
            // 表示判定用。まだ 1 つもチェックしていないモデルのストアを作らないよう FindStore を使う
            // (操作側のコールバックは GetStore で遅延生成する)
            var shapeKeyStore = ModelShapeKeyEditManager.instance.FindStore(modelObject);

            view.BeginScrollView();
            {
                foreach (var blendShape in blendShapes)
                {
                    var weight = blendShape.weight;
                    var shapeKeyName = blendShape.shapeKeyName;
                    var isModified = shapeKeyStore != null && shapeKeyStore.IsModified(shapeKeyName);

                    // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
                    // 手動 OFF は「未編集へ戻す」操作なので重みも 0 に戻す
                    Action<bool> onCheckChanged = newChecked =>
                    {
                        if (newChecked)
                        {
                            ModelShapeKeyEditManager.instance.GetStore(modelObject).Mark(shapeKeyName);
                        }
                        else
                        {
                            blendShape.weight = 0f;
                            model.FixBlendValues();
                            ModelShapeKeyEditManager.instance.GetStore(modelObject).Unmark(shapeKeyName);
                        }
                    };

                    view.DrawTrackedLabel(isModified, onCheckChanged, shapeKeyName, -1, ROW_HEIGHT);

                    var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = -1f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = weight,
                        onChanged = x => weight = x,
                    });

                    // FixBlendValues は全頂点を走査するため、値が変わったときだけ呼ぶ
                    if (updateTransform)
                    {
                        blendShape.weight = weight;
                        model.FixBlendValues();
                        // 編集したシェイプキーは自動で追跡対象にする
                        ModelShapeKeyEditManager.instance.GetStore(modelObject).Mark(shapeKeyName);
                    }
                }
            }
            view.EndScrollView();
```

- [ ] **Step 3: クラスコメントを実態へ合わせる**

L8-12 の `<summary>` の最終行（「キーフレーム対象タグの登録は…ここでは扱わない」）を次に置き換える:

```csharp
    /// メイドのシェイプキーについてはキーフレーム対象タグの登録はレイヤー編集ウィンドウ
    /// (ShapeKey レイヤー) の責務で、ここでは扱わない。
    /// モデルのシェイプキーは行頭のチェックが変更追跡 (プリセット保存とタイムライン表示の対象) を兼ねる。
    /// チェック集合は HistoryManager 未対応のため undo で戻らない
```

- [ ] **Step 4: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs
git commit -m "feat(ui): モデルシェイプキーへ変更追跡チェックを表示する"
```

---

### Task 6: ModelShapeKeyTimelineLayer へ追跡絞り込みを接続する

M1 の `ModelBoneTimelineLayer` と一対一で対応する（同じ `ModelTimelineLayerBase` 派生・同じイベント配線）。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs`

**Interfaces:**
- Consumes: `ModelShapeKeyEditManager.instance.trackedStore`（Task 3）、M0/M1 の `trackedBoneNames` / `isTrackedTargetReady` / `InvalidateTrackedBoneNames`
- Produces: なし

**変更前後の対応:**

| 箇所 | 変更前 | 変更後 |
|---|---|---|
| `allBoneNames` | `modelManager.blendShapeNames`（全シェイプキー） | `trackedBoneNames` |
| `InitMenuItems` | 全シェイプキーをメニューへ | 追跡集合のみ。対象 0 件のモデルは見出しごと出さない |
| `UpdateFrame` | `modelManager.blendShapeMap.Values` 全走査 | `allBoneNames` を走査 |
| `OnModelAdded` | 追加モデルの全シェイプキーに 0F キー | 0F キーは打たない + `InvalidateTrackedBoneNames()` |
| `OnModelRemoved` | 全シェイプキーのキー削除 | 変更なし + `InvalidateTrackedBoneNames()` |
| `OnCopyModel` | 複製 | 変更なし + 末尾で `InvalidateTrackedBoneNames()` |

- [ ] **Step 1: 追跡 override を追加する**

ファイル冒頭の `using` へ `using COM3D2.SceneEditor.Plugin;` を追加し、`allBoneNames` の宣言を置き換える:

```csharp
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => ModelShapeKeyEditManager.instance.trackedStore;

        protected override List<string> trackedCandidateNames => modelManager.blendShapeNames;

        protected override string trackedHistoryPrefix => "モデルシェイプ";

        // モデルレイヤーは maid を持たない。モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;
```

- [ ] **Step 2: InitMenuItems を絞る**

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            // 追跡対象だけをメニューへ出す。全シェイプキーを出すとモデル 1 体で数百行になる
            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.blendShapes.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var blendShape in model.blendShapes)
                {
                    if (!targetNames.Contains(blendShape.name))
                    {
                        continue;
                    }

                    // 対象が 1 件も無いモデルは見出しごと出さない
                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    setMenuItem.AddChild(new BoneMenuItem(blendShape.name, blendShape.shapeKeyName));
                }
            }
        }
```

- [ ] **Step 3: UpdateFrame を絞る**

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // 追跡対象だけキーを書く。allBoneNames を回すのがキー書き込み絞り込みの実体
            foreach (var boneName in allBoneNames)
            {
                var blendShape = modelManager.GetBlendShape(boneName);
                if (blendShape == null)
                {
                    // 既存キーにだけ残っているシェイプキー (モデル差し替え等) は書けないので飛ばす
                    continue;
                }

                var trans = CreateTransformData<TransformDataModelShapeKey>(boneName);
                trans.weight = blendShape.weight;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }
```

- [ ] **Step 4: モデル追加/削除/複製でキャッシュを無効化する**

```csharp
        public void OnModelAdded(StudioModelStat model)
        {
            // 候補名リストが入れ替わるため、メニューを組む前にキャッシュを捨てる
            InvalidateTrackedBoneNames();

            // 追加直後のモデルは未編集なので追跡対象が無い。
            // 0F キーはシェイプキーをチェックした時点で追跡部品が打つ
            InitMenuItems();
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var boneNames = model.blendShapes.Select(x => x.name).ToList();
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }
```

`OnCopyModel` のメソッド末尾（`foreach (var keyFrame in keyFrames)` ループの後ろ）へ:

```csharp
            // 複製先のキーを追跡集合へ即座に反映する (待つと間引きぶん遅れる)
            InvalidateTrackedBoneNames();
```

- [ ] **Step 5: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS。特に `XmlRoundTripTests` / `MteCompatibilityTests`（既存タイムライン XML のシェイプキーが読み込みで消えないこと）

- [ ] **Step 6: COM3D2 構成でもビルドする**

Run: `MSBuild ... /p:GameVersion=COM3D2` → その後 COM3D25 構成をもう一度ビルド
Expected: 成功

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs
git commit -m "feat(timeline): モデルシェイプレイヤーをチェック済みキーで絞り込む"
```

---

### Task 7: シーンプリセットの保存対象をチェック済みへ切り替える

現状は「重みが非 0」で絞っている。これを「チェック済み」に変え、適用時は記載分をチェック済みへ復元する（表情の v22 と同じ扱い）。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
  - `CaptureModelShapeKeys()`（L888-922）
  - `ApplyModelShapeKeys()`（L2396-2430 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
  - バージョン履歴コメント（L655-666）と `CurrentVersion`（L667）
  - `modelShapeKeys` フィールドのコメント（L708）

**Interfaces:**
- Consumes: `ModelShapeKeyEditManager.instance.GetStore/FindStore`（Task 3）
- Produces: なし（プリセット XML の構造は変わらない。選別ルールだけが変わる）

**キーの二系統について:** プリセットのモデル照合は `entry.obj.name` + `pluginName`（安定キー）で行い、チェック集合の参照はモデルの `GameObject` で行う。どちらも group 接尾辞つきの修飾名を使わないので、名前の振り直しの影響を受けない。`ScenePresetManager` は既に `entry.obj` を持っているので、そこから `GameObject` を得られる。

- [ ] **Step 1: 保存側をチェック済みフィルタへ変える**

`CaptureModelShapeKeys` の `foreach (var blendShape in model.blendShapes)` ブロックを次に置き換える:

```csharp
                // チェック集合はモデルの GameObject で引く (修飾名は group 振り直しで変わるため使わない)
                var shapeKeyStore = ModelShapeKeyEditManager.instance
                    .FindStore(model.transform.gameObject);
                if (shapeKeyStore == null)
                {
                    continue;
                }

                foreach (var blendShape in model.blendShapes)
                {
                    // 保存対象はチェック済みのみ。重み 0 でもユーザーが意図して選んだものは残す
                    if (!shapeKeyStore.IsModified(blendShape.shapeKeyName))
                    {
                        continue;
                    }
                    data.modelShapeKeys.Add(new ScenePresetModelShapeKey
                    {
                        modelName = entry.obj.name,
                        pluginName = entry.pluginName,
                        name = blendShape.shapeKeyName,
                        value = blendShape.weight,
                    });
                }
```

- [ ] **Step 2: 適用側でチェック集合を復元する**

`ApplyModelShapeKeys` の `blendShape.weight = shapeKeyState.value;` の直後へ:

```csharp
                blendShape.weight = shapeKeyState.value;
                // 保存されていた = ユーザーがチェックしていた。旧バージョンのプリセットでも
                // 「記載分 = チェック済み」で辻褄が合う (表情の v22 と同じ扱い)
                ModelShapeKeyEditManager.instance
                    .GetStore(model.transform.gameObject).Mark(shapeKeyState.name);
                touchedModels.Add(model);
```

**注意:** 表情は `SetNames` で集合を丸ごと置き換えるが、ここは `Mark` の積み増しにする。プリセットの部分適用（モデルが一部しか見つからない）で、適用できなかったモデルのチェックまで巻き添えで消さないため。per-model ストアなのでモデル単位の `SetNames` も原理的には可能だが、`ApplyModelShapeKeys` はシェイプキー単位のループで書かれており、モデル単位へ組み替えるのはこのフェーズのスコープを超える。

- [ ] **Step 3: スキーマバージョンを上げる**

`ScenePresetData.cs` の v22 行の直後へ足し、`CurrentVersion` を 23 にする:

```csharp
        // v23: モデルシェイプキーの保存対象を「重みが非 0」から「チェック済み (EditTargetStore)」へ変更。
        //      構造変更なし。旧データは記載シェイプキー (=非 0 保存分) を適用時にチェック済みへ復元する
        public static readonly int CurrentVersion = 23;
```

`modelShapeKeys` フィールドのコメント（L708）を次に置き換える:

```csharp
        /// モデルのシェイプキー (v21)。v23 以降はチェック済みのみ。外部プロバイダ保存時のみ入る。
```

- [ ] **Step 4: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS。プリセットのバージョン定数を参照するテストがあれば追随させる（`grep -rn "CurrentVersion\|version=\"22\"" source/COM3D2.SceneEditor.Plugin.Tests` で確認する）

- [ ] **Step 5: COM3D2 構成でもビルドする**

Run: `MSBuild ... /p:GameVersion=COM3D2` → その後 COM3D25 構成をもう一度ビルド
Expected: 成功

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs
git commit -m "feat(preset): モデルシェイプキーの保存対象をチェック済みへ絞る"
```

---

### Task 8: 実機確認とロードマップ更新

**Files:**
- Modify: `docs/superpowers/specs/modified-tracking-rollout-roadmap.md`（Phase M2 節、および M1 節への追記）

**Interfaces:**
- Consumes: Task 1〜7 の成果
- Produces: なし

- [ ] **Step 1: ユーザーへ実機確認を依頼する**

M1 の未確認項目（前フェーズの計画 Task 8）とあわせて確認してもらう。

**シェイプキー編集ウィンドウ（モデルタブ）**
1. シェイプキー名の行頭にチェックが出て、初期状態は全て未チェックであること
2. スライダーを動かすと自動でチェックが入ること
3. チェックを外すと重みが 0 に戻ること
4. チェックを手動で入れると、重みを変えずに追跡対象になること
5. メイドタブの表示・操作が変わっていないこと

**タイムライン（モデルシェイプレイヤー）**
6. シェイプキーをチェックするとメニューに現れ、0F にキーが自動登録されること
7. チェックを外すとキーが全フレームから消え、メニューからも消えること
8. モデルを追加しても、そのモデルのシェイプキーが 0F へ一括登録されないこと（従来は全件登録されていた）
9. モデルを削除するとそのモデルのキーが消えること
10. 既存のモデルシェイプキーを含むタイムラインを読み込むと、未チェックでもメニューに出て編集できること
11. モデルを複製したとき、複製元のキーが複製先へ引き継がれ、すぐメニューに出ること

**モデル名の振り直し（Task 1・2 の核心。M1 のモデルボーンでも同じ手順で確認する）**

12〜14 は **SE の UI を一度閉じて開き直してからも** 同じ結果になることを確認する
（モデル一覧イベントの購読が UI トグルで切れないことの確認。コードレビューで見つかった不具合の再発防止）

12. **同じファイルのモデルを 2 体配置** → 両方でシェイプキー（およびボーン）をチェック → **先に置いた方を削除** → 生き残ったモデルのチェックが保持され、タイムラインのメニューにも出たままであること
13. 同じ手順で、生き残ったモデルのキーフレームが引き続き編集できること
14. 一度もチェックしていない同名モデルを削除したときも、生き残りのチェックが保持されること（version が動かない経路の確認）

**シーンプリセット**
15. シェイプキーをチェックして重みを付けた状態でプリセット保存 → 読み込みで重みが復元され、チェックも入った状態になること
16. **重み 0 のままチェックしたシェイプキー**もプリセットに保存され、読み込み後にチェックが復元されること
17. v22 以前の旧プリセット（非 0 のシェイプキーを持つもの）を読み込むと、記載分がチェック済みで復元されること

**既知の制限（確認不要・仕様）**
- チェックの ON/OFF は undo で戻らない（HistoryManager 未対応。スコープ外）

- [ ] **Step 2: 指摘があれば修正し、再ビルド・再テストしてコミットする**

- [ ] **Step 3: ロードマップを更新する**

`### Phase M2: モデルシェイプキー` を `### Phase M2: モデルシェイプキー ✅ 完了 (YYYY-MM-DD)` に変え、M0/M1 と同じ体裁で「実装の要点」を追記する。最低限次を書く:

- **モデル修飾名は安定した識別子ではない**（`ModelHackManager.FixGroup` が一覧を引くたびに group を振り直す）。記録は必ず「モデル（`GameObject`）× 生名」で持ち、修飾名は毎回組み直す
- 組み直しの部品は `ModelTrackedNameStore<TKey>`（Unity 非依存・テスト付き）。M1 のモデルボーンもこれへ載せ替えた
- モデル一覧の変化（`StudioModelManager.onModelAdded/onModelRemoved`）で集約を `Invalidate()` する。記録側の version は動かないため、これが無いと名前の振り直しに追従できない
- チェック集合は HistoryManager 未対応のため履歴に含めない（縮退条項を適用。M3/M4 も同じ判断を引き継ぐか要検討）
- シーンプリセットは v23。構造変更なし・選別ルール変更のみ
- `GUIView.DrawTrackedLabel` — ラベルと値が別行になる縦長レイアウト向けのチェック行

あわせて Phase M1 節の「実装の要点」へ次を追記する:

- 集約は当初 `BoneEditManager` の手書きだったが、M2 で `ModelTrackedNameStore<GameObject>` へ載せ替え、モデル名の振り直しに追従するようになった

- [ ] **Step 4: コミット**

```bash
git add docs/superpowers/specs/modified-tracking-rollout-roadmap.md
git commit -m "docs: 変更追跡ロードマップの M2 完了を記録する"
```

---

## Self-Review

**Spec coverage（ロードマップ Phase M2 の 5 項目）:**

| ロードマップの項目 | 対応タスク |
|---|---|
| モデル用 `EditTargetStore` レジストリ（キーは `GameObject`。`BoneEditManager.GetModelStore` と同型） | Task 3（**ロードマップの文言どおり per-model**。単一ストア案は名前の不安定性により却下） |
| ShapeKeyEditWindow モデルタブへチェック行 | Task 4（ヘルパー）+ Task 5（配線） |
| `ModelShapeKeyTimelineLayer` へ M0 部品接続 | Task 6 |
| モデルリスト全消し再構築に耐える再構築トリガー | Task 1（`Invalidate`）+ Task 3（`onModelAdded/onModelRemoved` 購読）+ Task 6（`InvalidateTrackedBoneNames`） |
| シーンプリセット `modelShapeKeys` をチェック済みフィルタへ | Task 7 |
| 前提タスク: HistoryManager 対応 | **縮退**（Global Constraints に明記。ロードマップの「コスト過大なら…先送り可」条項） |

**方針との整合:**
- 方針 1（行独立な領域は書き込みも絞る）: シェイプキーは行独立なので書き込みも絞る — Task 6
- 方針 2（ソース・オブ・トゥルースは編集側ストア 1 つ）: per-model の `EditTargetStore` が正で、集約は読み取り専用の派生物。逆流経路は無い — Task 3
- 方針 3（現物に無い名前は捨てない）: モデル削除で記録は捨てるが（モデルに着替えは無く破棄が確定的）、集約に載らない名前は `UpdateFrame` の null スキップで安全に飛ばす — Task 6 Step 3
- 方針 4（ポーリングからイベントへ）: モデル一覧イベントで `Invalidate`。30F 再試行は「タイムライン未ロードでコンポーネントが付いていない」場合の保険に限定 — Task 1 / Task 3
- 方針 5（Undo 前提の確認）: 未対応と判断し明記して縮退 — Global Constraints

**残リスク（計画外メモ）:**
- チェック集合が履歴外なので、undo で重みだけ戻ってチェックが残る「ねじれ」が起きうる。実害はプリセット保存に重み 0 の項目が残る程度。M4 でマテリアルの History 対応を入れるときに合わせて解消するのが自然
- `OnModelAdded` の全シェイプキー 0F 一括登録の廃止は既存の作成手順を変える（M1 のモデルボーンと同じ性質の仕様変更）。実機確認 8 番で確認する
- タイムラインのキーフレーム自体もモデル修飾名で保存されているため、group 振り直しでキーが迷子になる問題は MTE 由来で残ったまま。この計画は**追跡集合が追従すること**までを担保し、キーフレーム側の追従は扱わない（既存の挙動を変えない）

## レビュー却下メモ

- 「単一 `EditTargetStore` で足りる」— **却下（自案を撤回）**。`ModelHackManager.FixGroup` がモデル一覧を引くたびに group を振り直すため修飾名は不安定。per-model + 集約の二段構えへ戻した（Task 1・3）
- 「実機確認に group 振り直しシナリオが無い」— **取り込み済み**。Task 8 の 12〜14 を追加
- 「プリセット適用が `Mark` 積み増しで表情の `SetNames` と非対称」— **設計として維持し、理由を明記**（Task 7 Step 2）。per-model ストアになったのでモデル単位の `SetNames` も可能だが、`ApplyModelShapeKeys` の走査単位を組み替える必要がありスコープ外
- 「HistoryManager 縮退が M2/M3/M4 で既定路線化する懸念」— **ロードマップ更新で「M3/M4 も同じ判断を引き継ぐか要検討」と明記して申し送り**（Task 8 Step 3）
