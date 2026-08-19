# モデルボーン編集対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 外部プラグイン（ModItemExplorer）が配置したモデルのボーンを、メイドと同様に SceneEditor のボーン編集ウィンドウで選択・編集・プリセット保存できるようにする。

**Architecture:** MTEUtils 共有 submodule に `ModelProviderClient`（リフレクションブリッジ）を追加し、SceneEditor 側に `ModelProviderHost` を新設して ModItemExplorer がモデル提供者として登録する。SceneEditor のボーン編集系（BoneEditManager / BoneEditWindow / BoneLineRenderer / InspectorWindow / PartsEditPresetIO）に「モデルモード」の分岐を追加し、ボーン列挙・差分ストア・履歴・PartsEdit 互換プリセット（`bMaidParts=false`）を再利用する。ボーンギズモは SceneEditor 側が持つ（モデルルートのギズモは従来通り ModItemExplorer 側、`showGizmo:false` で選択同期）。

**Tech Stack:** C# (.NET 3.5 / Unity 5.6 相当), BepInEx/UnityInjector プラグイン, XmlSerializer

**Spec:** 本計画の冒頭調査まとめ（会話ログ）+ memory `model-bone-edit-research.md`

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree は使わない（メイン作業ディレクトリで作業）
- リポジトリは 3 つ: SceneEditor（本体）、MTEUtils（submodule: `source/COM3D2.SceneEditor.Plugin/MTEUtils`、ModItemExplorer 側にも同 submodule あり）、ModItemExplorer（`W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`）
- ビルド確認: リポジトリルートで `cmd /c debug.bat com3d25`（ユニットテストは無い。ビルド成功＋ゲーム内確認が検証手段）
- ホスト公開 API（`ModelProviderHost`）はリフレクション発見されるため、公開後のシグネチャ変更禁止
- C# 古い言語バージョンのため tuple / string interpolation / null 条件演算子の乱用は避け、周辺コードの記法（`string.Format`、明示型）に合わせる

---

### Task 1: SceneEditor に ModelProviderHost を新設

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ModelProviderHost.cs`

**Interfaces:**
- Produces: `ModelProviderHost.Register(string, Func<List<GameObject>>, Func<GameObject, string>) : object` / `Unregister(object)` / `GetModels() : List<ExternalModelEntry>`、`ExternalModelEntry { GameObject obj; string displayName; string pluginName; }`
- Consumes: `MTEUtils.LogException`（既存）

- [ ] **Step 1: ModelProviderHost.cs を作成**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>外部プラグインが配置したモデル 1 件分。表示名はプラグイン側の管理名を使う</summary>
    public class ExternalModelEntry
    {
        public GameObject obj;
        public string displayName;
        public string pluginName;
    }

    /// <summary>
    /// 外部プラグインが管理するモデル一覧を SceneEditor へ提供させる公開 API。
    /// MTEUtils の ModelProviderClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// GameObject は Unity 本体の型なので DLL 間でそのまま受け渡せる
    ///
    /// 契約:
    /// - getModels は「現在配置中のモデルのルート GameObject」を毎回列挙して返す
    ///   (SceneEditor 側は保持せず都度呼ぶため、増減はそのまま反映される)
    /// - getDisplayName は null 可。null または空文字を返した場合は GameObject 名で表示する
    /// - Register の戻り値は解除用ハンドル。不要になったら必ず Unregister すること
    /// - 提供デリゲートの例外はホスト側で握り潰す (他プラグインを巻き込まない)
    /// - ホスト側は重複排除を行わない。同一 GameObject を複数経路で提供しないのは提供側の責務
    /// </summary>
    public static class ModelProviderHost
    {
        private class Provider
        {
            public string pluginName;
            public Func<List<GameObject>> getModels;
            public Func<GameObject, string> getDisplayName;
        }

        private static readonly List<Provider> _providers = new List<Provider>();

        /// <summary>モデル提供者を登録する。戻り値は解除用ハンドル (引数不正なら null)</summary>
        public static object Register(
            string pluginName,
            Func<List<GameObject>> getModels,
            Func<GameObject, string> getDisplayName)
        {
            if (string.IsNullOrEmpty(pluginName) || getModels == null)
            {
                MTEUtils.LogError("ModelProviderHost.Register: pluginName と getModels は必須です");
                return null;
            }

            var provider = new Provider
            {
                pluginName = pluginName,
                getModels = getModels,
                getDisplayName = getDisplayName,
            };
            _providers.Add(provider);
            return provider;
        }

        public static void Unregister(object handle)
        {
            var provider = handle as Provider;
            if (provider != null)
            {
                _providers.Remove(provider);
            }
        }

        /// <summary>
        /// 全提供者のモデルを集めて返す。
        /// 提供者ごとに例外を握り潰し、1 プラグインの不具合で他を巻き込まない
        /// </summary>
        public static List<ExternalModelEntry> GetModels()
        {
            var result = new List<ExternalModelEntry>();
            // 列挙中の Register / Unregister に耐えるよう複製して回す
            var providers = _providers.ToArray();
            foreach (var provider in providers)
            {
                try
                {
                    var models = provider.getModels();
                    if (models == null)
                    {
                        continue;
                    }

                    foreach (var go in models)
                    {
                        if (go == null)
                        {
                            continue;
                        }

                        string name = null;
                        if (provider.getDisplayName != null)
                        {
                            name = provider.getDisplayName(go);
                        }
                        result.Add(new ExternalModelEntry
                        {
                            obj = go,
                            displayName = string.IsNullOrEmpty(name) ? go.name : name,
                            pluginName = provider.pluginName,
                        });
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 2: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ModelProviderHost.cs
git commit -m "feat(model-bone): 外部プラグインのモデル一覧を受け取る ModelProviderHost を追加"
```

---

### Task 2: MTEUtils に ModelProviderClient を追加

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MTEUtils/ModelProviderClient.cs`（MTEUtils submodule 内。コミットは submodule リポジトリで行う）

**Interfaces:**
- Consumes: `DockingClient.FindHostType("ModelProviderHost")`（既存）、Task 1 の `ModelProviderHost.Register/Unregister`
- Produces: `ModelProviderClient.isAvailable : bool` / `Register(string, Func<List<GameObject>>, Func<GameObject, string>) : object` / `Unregister(object)`

- [ ] **Step 1: ModelProviderClient.cs を作成（InspectorHostClient と同型）**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// SceneEditor プラグインの ModelProviderHost へのリフレクションブリッジ。
    /// 登録すると、SceneEditor のボーン編集ウィンドウ等に自プラグイン管理のモデルが
    /// 対象として列挙されるようになる。
    /// SceneEditor が不在・旧バージョンの場合は isAvailable が false になり、
    /// 呼び出し側は登録しない (再試行は呼び出し側の登録ループの責務)
    ///
    /// 契約 (ホスト側 ModelProviderHost と同じ):
    /// - getModels は現在配置中のモデルのルート GameObject を毎回列挙して返すこと
    /// - getDisplayName は null 可。null / 空文字なら GameObject 名で表示される
    /// - Register の戻り値は解除用ハンドル。不要になったら必ず Unregister すること
    /// </summary>
    public static class ModelProviderClient
    {
        private delegate object RegisterDelegate(
            string pluginName,
            Func<List<GameObject>> getModels,
            Func<GameObject, string> getDisplayName);

        private static RegisterDelegate _register;
        private static Action<object> _unregister;
        private static bool _initialized;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _register != null;
            }
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            // ロード順によってはホストのアセンブリが未登場のことがあるため、
            // 型が見つかるまでは _initialized を立てずに再試行を続ける
            var type = DockingClient.FindHostType("ModelProviderHost");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                var register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
                var unregister = type.GetMethod("Unregister", BindingFlags.Public | BindingFlags.Static);
                if (register == null || unregister == null)
                {
                    MTEUtils.LogWarning("ModelProviderClient: ModelProviderHost にシグネチャの一致するメソッドが見つかりませんでした");
                    return;
                }

                _register = (RegisterDelegate)Delegate.CreateDelegate(typeof(RegisterDelegate), register);
                _unregister = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), unregister);
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合は登録を無効化する
                MTEUtils.LogWarning("ModelProviderClient: ModelProviderHost との接続に失敗しました: " + e.Message);
                _register = null;
                _unregister = null;
            }
        }

        /// <summary>モデル提供者をホストへ登録する。戻り値はハンドル (ホスト不在なら null)</summary>
        public static object Register(
            string pluginName,
            Func<List<GameObject>> getModels,
            Func<GameObject, string> getDisplayName)
        {
            if (!isAvailable)
            {
                return null;
            }

            try
            {
                return _register(pluginName, getModels, getDisplayName);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ModelProviderClient: ModelProviderHost への登録に失敗しました: " + e.Message);
                return null;
            }
        }

        public static void Unregister(object handle)
        {
            if (handle == null || !isAvailable)
            {
                return;
            }

            try
            {
                _unregister(handle);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ModelProviderClient: ModelProviderHost からの登録解除に失敗しました: " + e.Message);
            }
        }
    }
}
```

- [ ] **Step 2: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 3: MTEUtils submodule 内でコミット**

```bash
cd source/COM3D2.SceneEditor.Plugin/MTEUtils
git add ModelProviderClient.cs
git commit -m "feat: SceneEditor の ModelProviderHost へ接続する ModelProviderClient を追加"
# Task 8 (ModItemExplorer 側 submodule 更新) が pull できるよう必ず push する
git push origin master
cd ../../..
```

（SceneEditor 親リポジトリの submodule ポインタ更新は Task 7 の最終コミットに含める）

---

### Task 3: BoneEditManager にモデルモードを追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs`

**Interfaces:**
- Produces: `BoneEditTargetType { Maid, Model }`、`BoneEditManager.targetType` / `targetModel` / `isModelMode` / `ModelSlotKey`（定数 "model"）/ `GetModelStore(GameObject)` / `FindModelStore(GameObject)` / `SelectModelBone(Transform)` / `NotifyModelBoneEdited(Transform)`
- Consumes: `SelectionManager.Select(GameObject, bool showGizmo)`、`HistoryManager.BeforeEdit(null, HistoryScope.Object, ...)`（maid null は Object スコープで許容済み: HistoryScope.cs の RequiresMaid 参照）

- [ ] **Step 1: enum・フィールド・ストア API を追加**

クラス冒頭（namespace 直下、`BoneEditManager` クラスの上）に enum を追加:

```csharp
    /// <summary>ボーン編集の対象種別</summary>
    public enum BoneEditTargetType
    {
        Maid,
        Model,
    }
```

`BoneEditManager` のフィールド群（`targetSlotName` の下）に追加:

```csharp
        /// <summary>編集対象の種別。Model のときは targetModel 配下のボーンを編集する</summary>
        public BoneEditTargetType targetType = BoneEditTargetType.Maid;

        /// <summary>モデルモードの編集対象 (外部プラグインが配置したモデルのルート)</summary>
        public GameObject targetModel;

        /// <summary>モデルの差分ストアで slotName の代わりに使う固定キー (モデルにスロット概念は無い)</summary>
        public const string ModelSlotKey = "model";

        public bool isModelMode => targetType == BoneEditTargetType.Model;

        private readonly Dictionary<GameObject, BoneEditStore> _modelStores
            = new Dictionary<GameObject, BoneEditStore>();

        private readonly List<GameObject> _deadModels = new List<GameObject>();
```

`FindStore` の下にモデル用ストア API を追加:

```csharp
        /// <summary>モデルの差分ストア。無ければ作る</summary>
        public BoneEditStore GetModelStore(GameObject model)
        {
            BoneEditStore store;
            if (!_modelStores.TryGetValue(model, out store))
            {
                store = new BoneEditStore();
                _modelStores[model] = store;
            }
            return store;
        }

        /// <summary>既存のモデル差分ストアを引くだけで新規生成はしない (FindStore と同じ趣旨)</summary>
        public BoneEditStore FindModelStore(GameObject model)
        {
            BoneEditStore store;
            return model != null && _modelStores.TryGetValue(model, out store) ? store : null;
        }
```

- [ ] **Step 2: モデル用の選択・編集記録 API を追加**

`SelectBone` の下に追加:

```csharp
        /// <summary>
        /// モデルのボーンを選択する。Inspector にはモデルルートを選択として反映するが、
        /// モデルルートのギズモは外部プラグイン側が持つため showGizmo は出さない
        /// (ボーン自体のギズモは externalTargetProvider 経由でこちらが出す)
        /// </summary>
        public void SelectModelBone(Transform bone)
        {
            selectedBone = bone;
            if (targetModel == null || bone == null)
            {
                return;
            }

            selectionManager.Select(targetModel, false);

            // 選択オブジェクトの変更エッジで自分の選択を解除しないよう前フレーム値を揃える
            _lastSelectedObject = selectionManager.selectedObject;
        }

        /// <summary>選択中モデルのボーンが編集されたことを記録する (モデルに揺れもの・着替えは無い)</summary>
        public void NotifyModelBoneEdited(Transform bone)
        {
            if (targetModel == null || bone == null)
            {
                return;
            }
            GetModelStore(targetModel).RecordEdit(ModelSlotKey, null, bone);
        }
```

- [ ] **Step 3: 基準回転・オフセット系をモデル対応に一般化**

`GetSelectedBoneBaseRotation` を差し替え:

```csharp
        /// <summary>
        /// 選択中ボーンの差分エントリ。対象種別に応じたストアから引く (未編集なら null)
        /// </summary>
        private BoneEditEntry GetSelectedBoneEntry(Maid maid)
        {
            if (isModelMode)
            {
                var store = FindModelStore(targetModel);
                return store != null ? store.GetEntry(ModelSlotKey, selectedBone.name) : null;
            }

            var maidStore = FindStore(maid);
            return maidStore != null ? maidStore.GetEntry(targetSlotName, selectedBone.name) : null;
        }

        /// <summary>
        /// 選択中ボーンの基準回転。編集済みなら記録時の元値、未編集なら現在値
        /// (未編集 = オフセット 0 として扱う)
        /// </summary>
        private Quaternion GetSelectedBoneBaseRotation(Maid maid)
        {
            var entry = GetSelectedBoneEntry(maid);
            return entry != null ? entry.origRotation : selectedBone.localRotation;
        }
```

`GetSelectedBoneOffset` / `SetSelectedBoneOffsetAxis` のガードを対象種別で分岐（maid はモデルモードでは null で呼ばれる）:

```csharp
        /// <summary>対象種別に応じた編集対象が揃っているか (モデルモードでは maid は使わない)</summary>
        private bool HasEditTarget(Maid maid)
        {
            return selectedBone != null && (isModelMode ? targetModel != null : maid != null);
        }
```

- `GetSelectedBoneOffset` の先頭ガードを `if (!HasEditTarget(maid))` に変更
- `SetSelectedBoneOffsetAxis` の先頭ガードを `if (!HasEditTarget(maid))` に変更し、末尾の `NotifyBoneEdited(maid, selectedBone);` を以下へ:

```csharp
            if (isModelMode)
            {
                NotifyModelBoneEdited(selectedBone);
            }
            else
            {
                NotifyBoneEdited(maid, selectedBone);
            }
```

- [ ] **Step 4: GetCurrentBoneTree をモデル対応**

```csharp
        public List<SlotBoneNode> GetCurrentBoneTree()
        {
            GameObject obj;
            if (isModelMode)
            {
                obj = targetModel;
            }
            else
            {
                var maid = MaidManipulateManager.instance.targetMaid;
                obj = SlotBoneManager.GetSlotObject(maid, targetSlotName);
            }

            // 破棄済み参照どうしの比較は等しくなるため、空でない状態で obj が消えた場合も作り直す
            if (obj != _boneTreeSource || (obj == null && _boneTree.Count > 0))
            {
                _boneTree = SlotBoneManager.BuildBoneTree(obj);
                _boneTreeSource = obj;
                selectedBone = null;
            }
            return _boneTree;
        }
```

- [ ] **Step 5: RecordGizmoDrag をモデル対応**

対象有無の判定と履歴記録を分岐:

```csharp
        private void RecordGizmoDrag()
        {
            var maid = MaidManipulateManager.instance.targetMaid;
            if (!editMode || !HasEditTarget(isModelMode ? null : maid))
            {
                _wasGizmoDragging = false;
                return;
            }

            var isDragging = IsGizmoDragging(SceneViewManager.instance.gizmoRenderer)
                || IsGizmoDragging(GameViewManager.instance.gizmoRenderer);

            if (isDragging || _wasGizmoDragging)
            {
                // ドラッグ開始エッジで変更前状態を控える (初回フレームの微小移動分の誤差は許容)
                if (!_wasGizmoDragging)
                {
                    if (isModelMode)
                    {
                        HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                            "ボーン編集: " + selectedBone.name, new[] { selectedBone });
                    }
                    else
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ボーン編集: " + selectedBone.name, new[] { selectedBone });

                        // ボーンを動かし始めたらメニューバーの編集モード (isEditMode) へ自動遷移する。
                        // 本クラスの editMode (ウィンドウ表示状態) とは別物。
                        // ドラッグ点・ボーンギズモ経由の遷移は MaidManipulateManager.Update が担う
                        MaidManipulateManager.instance.isEditMode = true;
                    }
                }

                if (isModelMode)
                {
                    NotifyModelBoneEdited(selectedBone);
                }
                else
                {
                    NotifyBoneEdited(maid, selectedBone);
                }
            }
            _wasGizmoDragging = isDragging;
        }
```

- [ ] **Step 6: モデルストアの掃除とシーン遷移リセット**

`Update()` に `CleanupModelStores();` を追加（`UpdateStores();` の後）し、メソッドを追加:

```csharp
        /// <summary>
        /// 削除されたモデルのストアと対象参照を捨てる。
        /// モデルはメイドと違い着替え (再ロード) が無いため、破棄検出だけでよい
        /// </summary>
        private void CleanupModelStores()
        {
            _deadModels.Clear();
            foreach (var pair in _modelStores)
            {
                if (pair.Key == null)
                {
                    _deadModels.Add(pair.Key);
                }
            }
            foreach (var model in _deadModels)
            {
                _modelStores.Remove(model);
            }

            // 対象モデルが削除されたら参照を実 null に落とし、選択も外す
            if (targetModel == null && !ReferenceEquals(targetModel, null))
            {
                targetModel = null;
                if (isModelMode)
                {
                    selectedBone = null;
                }
            }
        }
```

`OnChangedSceneLevel` に追加:

```csharp
            _modelStores.Clear();
            targetModel = null;
```

- [ ] **Step 7: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs
git commit -m "feat(model-bone): BoneEditManager にモデルモード (対象種別・モデル用差分ストア) を追加"
```

---

### Task 4: PartsEditPresetIO にモデル用の保存/適用を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/PartsEditPresetIO.cs`

**Interfaces:**
- Produces: `PartsEditPresetIO.SaveModel(GameObject, string, BoneEditStore) : bool` / `ApplyModel(GameObject, PartsEditPresetData, BoneEditStore) : int`
- Consumes: `BoneEditManager.ModelSlotKey`（Task 3）、既存 `SlotBoneManager.FindBone` / `ResolveBones`

- [ ] **Step 1: 共通処理を private に抽出**

既存 `Save` の XML 書き込み部（`var path = GetPresetFilePath(presetName);` から末尾まで）を抽出:

```csharp
        /// <summary>DTO を XML ファイルへ書く (BOM 無し UTF-8、PartsEdit 本体と同じ)</summary>
        private static bool WriteFile(string presetName, PartsEditPresetData data)
        {
            var path = GetPresetFilePath(presetName);
            try
            {
                Directory.CreateDirectory(directoryPath);
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    _serializer.Serialize(writer, data);
                }
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogError("PartsEdit プリセットの保存に失敗しました: {0}\n{1}", path, e);
                return false;
            }
        }
```

既存 `Save` の末尾を `return WriteFile(presetName, data);` に置き換える。

同様に `Apply` のボーン適用ループを抽出:

```csharp
        /// <summary>
        /// プリセット内のボーン TRS を rootObj 配下へ適用し、適用数を返す。
        /// 見つからないボーンはスキップし、現在値と同値のボーンは編集扱いにしない
        /// </summary>
        private static int ApplyTransformList(GameObject rootObj, string slotKey,
            string itemFileName, PartsEditPresetData data, BoneEditStore store)
        {
            var applied = 0;
            foreach (var trsData in data.transformDataList)
            {
                var bone = SlotBoneManager.FindBone(rootObj, trsData.name);
                if (bone == null)
                {
                    continue;
                }

                var position = trsData.position.ToVector3();
                var rotation = trsData.rotation.ToQuaternion();
                var scale = trsData.scale.ToVector3();

                // PartsEdit 本体は未編集ボーンも全て保存するため、値が変わらないものまで
                // 編集済み (*) にしないよう同値はスキップする
                if (bone.localPosition == position
                    && bone.localRotation == rotation
                    && bone.localScale == scale)
                {
                    continue;
                }

                // 先に呼んで適用前の値を元値として控える (ScenePresetManager.ApplyBoneEdits と同じ)
                store.RecordEdit(slotKey, itemFileName, bone);

                bone.localPosition = position;
                bone.localRotation = rotation;
                bone.localScale = scale;

                store.RecordEdit(slotKey, itemFileName, bone);
                applied++;
            }
            return applied;
        }
```

既存 `Apply` は抽出後こうなる:

```csharp
        public static int Apply(Maid maid, string slotName, PartsEditPresetData data, BoneEditStore store)
        {
            var slotObj = SlotBoneManager.GetSlotObject(maid, slotName);
            if (slotObj == null)
            {
                return 0;
            }

            var itemFileName = SlotBoneManager.GetSlotItemFileName(maid, slotName);
            var applied = ApplyTransformList(slotObj, slotName, itemFileName, data, store);

            // 保存時のスロット揺れ状態を復元する (bYure)。OFF のプリセットでは
            // ボーン編集値が物理に毎フレーム上書きされるのを防ぐ役目も兼ねる
            SlotYureUtil.SetSlotYureState(maid, slotName, data.bYure);

            return applied;
        }
```

- [ ] **Step 2: モデル用 Save/Apply を追加**

```csharp
        /// <summary>
        /// モデルの編集済みボーンをプリセットとして保存する。
        /// PartsEdit のモデル用プリセット (bMaidParts=false, slotName 空, bYure=false) と互換
        /// </summary>
        public static bool SaveModel(GameObject modelObj, string presetName, BoneEditStore store)
        {
            if (modelObj == null)
            {
                return false;
            }

            var data = new PartsEditPresetData
            {
                slotName = "",
                bMaidParts = false,
                bYure = false,
                // 先方の GetFileList(category, name) が rootData.name を参照するため null にしない。
                // 値は保存時点のスナップショット (こちらではルートを編集しない)
                rootData = new PartsEditPresetData.TransformData
                {
                    name = modelObj.name,
                    position = PartsEditPresetData.Vec3.From(modelObj.transform.localPosition),
                    rotation = PartsEditPresetData.Quat.From(modelObj.transform.localRotation),
                    scale = PartsEditPresetData.Vec3.From(modelObj.transform.localScale),
                },
            };

            foreach (var entry in store.GetEntries(BoneEditManager.ModelSlotKey))
            {
                data.transformDataList.Add(new PartsEditPresetData.TransformData
                {
                    name = entry.boneName,
                    position = PartsEditPresetData.Vec3.From(entry.position),
                    rotation = PartsEditPresetData.Quat.From(entry.rotation),
                    scale = PartsEditPresetData.Vec3.From(entry.scale),
                });
            }

            return WriteFile(presetName, data);
        }

        /// <summary>
        /// プリセットをモデルへ適用する。ボーン TRS のみ扱い、
        /// rootData は適用しない (モデルルートの配置は外部プラグイン管理のため)。
        /// PartsEdit 本体はモデルにも rootData.scale を適用するが、ここでは触らない
        /// </summary>
        public static int ApplyModel(GameObject modelObj, PartsEditPresetData data, BoneEditStore store)
        {
            if (modelObj == null)
            {
                return 0;
            }
            return ApplyTransformList(modelObj, BoneEditManager.ModelSlotKey, null, data, store);
        }
```

- [ ] **Step 3: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/PartsEditPresetIO.cs
git commit -m "feat(model-bone): PartsEdit 互換プリセットのモデル用保存/適用を追加"
```

---

### Task 5: BoneEditWindow に対象種別タブとモデル UI を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs`

**Interfaces:**
- Consumes: `ModelProviderHost.GetModels()`（Task 1）、`BoneEditManager.targetType / targetModel / isModelMode / ModelSlotKey / GetModelStore / FindModelStore / SelectModelBone`（Task 3）、`PartsEditPresetIO.SaveModel / ApplyModel`（Task 4）
- Produces: なし（UI のみ）

- [ ] **Step 1: 対象種別タブと共通ヘルパーを追加**

クラス冒頭に対象種別タブ enum を追加（表示名がそのままタブ名になる既存流儀）:

```csharp
        /// <summary>編集対象の種別タブ</summary>
        private enum TargetTabType
        {
            メイド,
            モデル,
        }
```

フィールド追加:

```csharp
        private readonly GUIComboBox<ExternalModelEntry> _modelComboBox = new GUIComboBox<ExternalModelEntry>
        {
            getName = (entry, _) => entry.displayName,
        };
```

基底のメイド選択行は自前で出すため、プロパティを追加:

```csharp
        // 対象種別タブをメイド選択行より上に置くため、基底の選択行は使わず自前で描く
        protected override bool showMaidSelector => false;
```

ストアのキーを対象種別で切り替えるヘルパーを追加:

```csharp
        /// <summary>差分ストアのキー。メイドはスロット名、モデルは固定キー</summary>
        private string activeSlotKey => boneEditManager.isModelMode
            ? BoneEditManager.ModelSlotKey
            : boneEditManager.targetSlotName;

        /// <summary>描画中の対象の差分ストア。モデルモードでは対象モデルのストア</summary>
        private BoneEditStore GetActiveStore(Maid target)
        {
            return boneEditManager.isModelMode
                ? boneEditManager.GetModelStore(boneEditManager.targetModel)
                : boneEditManager.GetStore(target);
        }
```

- [ ] **Step 2: DrawMaidContent を対象種別で分岐**

`DrawMaidContent` の先頭を差し替え（既存のメイド向けガードより前にタブを描く）:

```csharp
        protected override void DrawMaidContent(Maid target)
        {
            var prevType = boneEditManager.targetType;
            var tab = DrawInnerTabs(
                prevType == BoneEditTargetType.Model ? TargetTabType.モデル : TargetTabType.メイド,
                TAB_WIDTH);
            boneEditManager.targetType = tab == TargetTabType.モデル
                ? BoneEditTargetType.Model
                : BoneEditTargetType.Maid;
            if (boneEditManager.targetType != prevType)
            {
                // 選択ボーンだけ落とす。targetModel / targetSlotName はタブを往復しても
                // 復帰できるよう意図的に保持する
                boneEditManager.ClearBoneSelection();
            }

            if (boneEditManager.isModelMode)
            {
                DrawModelContent();
                return;
            }

            // showMaidSelector を切っているため、メイドモードではここで選択行を描く
            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                // 以下、既存のメイド向け描画のまま
```

（既存の `if (target == null) return;` は上記に統合して削除する）

- [ ] **Step 3: モデル用コンテンツ描画を追加**

```csharp
        /// <summary>
        /// モデルモードの描画。対象は外部プラグイン (ModelProviderHost) が提供するモデル一覧から選ぶ
        /// </summary>
        private void DrawModelContent()
        {
            var models = ModelProviderHost.GetModels();
            var manager = boneEditManager;

            view.BeginHorizontal();
            {
                view.DrawLabel("対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var comboWidth = CalcLabeledComboWidth(view);
                _modelComboBox.buttonSize = new Vector2(comboWidth, ROW_HEIGHT);
                _modelComboBox.contentSize = new Vector2(comboWidth, 300f);

                _modelComboBox.items = models;
                _modelComboBox.currentIndex = models.FindIndex(
                    entry => entry.obj == manager.targetModel);
                // defaultName は非 null だと選択項目より優先されるため、未選択時だけ設定する
                _modelComboBox.defaultName = _modelComboBox.currentIndex >= 0
                    ? null
                    : models.Count == 0 ? "モデルがありません" : "選択してください";
                _modelComboBox.onSelected = (entry, _) =>
                {
                    manager.targetModel = entry.obj;
                    manager.ClearBoneSelection();
                };
                _modelComboBox.DrawButton(view);
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            if (manager.targetModel == null)
            {
                view.DrawLabel(models.Count == 0
                    ? "対応プラグインの配置モデルがありません"
                    : "操作対象のモデルを選択してください", -1, ROW_HEIGHT);
                return;
            }

            DrawModelHeaderRow();

            var prevTab = _tabType;
            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);
            if (_tabType != prevTab && _tabType == BoneTabType.プリセット)
            {
                // フォルダを直接編集された場合もタブを開き直せば一覧に反映される
                RefreshPresetList();
            }

            if (_tabType == BoneTabType.プリセット)
            {
                DrawModelPresetContent();
            }
            else
            {
                DrawModelResetButtons();
                DrawBoneTree(null);
            }
        }

        /// <summary>モデルモードのヘッダー。ボーン表示トグルとプリセット保存 (DrawHeaderRow と同構成)</summary>
        private void DrawModelHeaderRow()
        {
            var manager = MaidManipulateManager.instance;
            var store = boneEditManager.GetModelStore(boneEditManager.targetModel);

            view.BeginHorizontal();
            {
                view.DrawToggle("ボーン表示", manager.isBoneVisible, 100, ROW_HEIGHT,
                    true, value => manager.isBoneVisible = value);

                // 保存対象は編集差分。差分が無いときは押させない
                if (view.DrawButton("プリセット保存", 110, ROW_HEIGHT,
                    store.GetEntries(BoneEditManager.ModelSlotKey).Count > 0))
                {
                    SaveBonePresetPopupWindow.Show(presetName => SaveModelPreset(presetName));
                }
            }
            view.EndLayout();
        }

        /// <summary>モデルモードのプリセットタブ。一覧描画は DrawPresetContent と同構成</summary>
        private void DrawModelPresetContent()
        {
            view.DrawTextField("検索", LABEL_WIDTH, _presetSearchText, -1, ROW_HEIGHT,
                value => _presetSearchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            DrawModelPresetList();
            view.EndScrollView();
        }

        /// <summary>モデルモードのプリセット一覧。名前を押すと対象モデルへ適用する</summary>
        private void DrawModelPresetList()
        {
            if (_presetNames.Count == 0)
            {
                view.DrawLabel("保存されたプリセットはありません", -1, ROW_HEIGHT);
                return;
            }

            const int deleteButtonWidth = 50;
            var nameButtonWidth = view.viewRect.width - view.padding.x * 2
                - deleteButtonWidth - view.margin;

            var matched = 0;
            foreach (var presetName in _presetNames)
            {
                if (!string.IsNullOrEmpty(_presetSearchText) && presetName
                    .IndexOf(_presetSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                matched++;

                view.BeginHorizontal();
                {
                    if (view.DrawButton(presetName, nameButtonWidth, ROW_HEIGHT))
                    {
                        LoadModelPreset(presetName);
                    }

                    if (view.DrawButton("削除", deleteButtonWidth, ROW_HEIGHT))
                    {
                        DialogPopupWindow.ShowConfirmDialog(
                            "プリセット「" + presetName + "」を削除しますか？",
                            () =>
                            {
                                PartsEditPresetIO.Delete(presetName);
                                RefreshPresetList();
                            });
                    }
                }
                view.EndLayout();
            }

            if (matched == 0)
            {
                view.DrawLabel("一致するプリセットはありません", -1, ROW_HEIGHT);
            }
        }

        /// <summary>プリセットのボーン TRS を対象モデルへ適用する</summary>
        private void LoadModelPreset(string presetName)
        {
            var data = PartsEditPresetIO.Load(presetName);
            if (data == null)
            {
                ToastManager.Show("プリセットの読み込みに失敗しました", ToastType.Error);
                return;
            }

            var modelObj = boneEditManager.targetModel;
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                "プリセットをロード: " + presetName,
                PartsEditPresetIO.ResolveBones(modelObj, data));
            var applied = PartsEditPresetIO.ApplyModel(
                modelObj, data, boneEditManager.GetModelStore(modelObj));
            ToastManager.Show(string.Format("プリセットを適用しました ({0} ボーン)", applied),
                ToastType.Success);
        }

        /// <summary>ポップアップで確定した名前で、対象モデルの編集差分を保存する</summary>
        private void SaveModelPreset(string presetName)
        {
            var modelObj = boneEditManager.targetModel;

            // ポップアップ表示中に対象が変わる・消えるケースは中止する (SavePreset と同じ趣旨)
            if (modelObj == null)
            {
                DialogPopupWindow.ShowDialog("対象のモデルが失われたため保存を中止しました");
                return;
            }

            var store = boneEditManager.GetModelStore(modelObj);
            if (store.GetEntries(BoneEditManager.ModelSlotKey).Count == 0)
            {
                DialogPopupWindow.ShowDialog("編集内容が失われたため保存を中止しました");
                return;
            }

            if (!PartsEditPresetIO.SaveModel(modelObj, presetName, store))
            {
                ToastManager.Show("プリセットの保存に失敗しました", ToastType.Error);
                return;
            }
            RefreshPresetList();
            ToastManager.Show("プリセットを保存しました: " + presetName, ToastType.Success);
        }

        /// <summary>モデルモードのリセット行 (DrawResetButtons のモデル版)</summary>
        private void DrawModelResetButtons()
        {
            var modelObj = boneEditManager.targetModel;
            var store = boneEditManager.GetModelStore(modelObj);
            var bone = boneEditManager.selectedBone;
            var hasBoneEdit = bone != null
                && store.GetEntry(BoneEditManager.ModelSlotKey, bone.name) != null;

            var buttonWidth = Mathf.Min(
                ResetButtonWidth,
                (view.viewRect.width - view.padding.x * 2 - view.margin) * 0.5f);

            view.BeginHorizontal();
            {
                if (view.DrawButton("ボーンをリセット", buttonWidth, ROW_HEIGHT, hasBoneEdit))
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                        "ボーンをリセット: " + bone.name, new[] { bone });
                    store.ResetBone(BoneEditManager.ModelSlotKey, bone);
                }

                if (view.DrawButton("モデルをリセット", buttonWidth, ROW_HEIGHT,
                    store.GetEntries(BoneEditManager.ModelSlotKey).Count > 0))
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                        "モデルをリセット: " + modelObj.name,
                        GetSlotEditedBones(store, BoneEditManager.ModelSlotKey, modelObj));
                    store.ResetSlot(BoneEditManager.ModelSlotKey, modelObj);
                }
            }
            view.EndLayout();
        }
```

- [ ] **Step 4: DrawBoneTree・ツリービュー配線をモデル対応**

`DrawBoneTree(Maid target)` の `_drawingStore` 設定を差し替え:

```csharp
            // ラベル生成から参照するため、描画に入る前に置いておく
            _drawingTarget = target;
            _drawingStore = GetActiveStore(target);
```

`SetupTreeView` 内の `getLabel` と `onSelected` を差し替え:

```csharp
            _treeView.getLabel = node =>
            {
                var isEdited = _drawingStore != null &&
                    _drawingStore.GetEntry(activeSlotKey, node.name) != null;
                return isEdited ? node.name + " *" : node.name;
            };
```

```csharp
            _treeView.onSelected = node =>
            {
                // 編集 UI は Inspector 側に出すため、選択を Inspector にも反映する
                if (boneEditManager.isModelMode)
                {
                    boneEditManager.SelectModelBone(node.transform);
                }
                else
                {
                    boneEditManager.SelectBone(_drawingTarget, node.transform);
                }
                _lastSelectedBone = node.transform;
            };
```

- [ ] **Step 5: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs
git commit -m "feat(model-bone): ボーン編集ウィンドウに対象種別タブとモデル編集 UI を追加"
```

---

### Task 6: BoneLineRenderer と InspectorWindow をモデル対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs`（TryPickBone の選択先分岐）
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs`（DrawSlotBoneContent の分岐）

**Interfaces:**
- Consumes: `BoneEditManager.isModelMode / SelectModelBone`（Task 3）

- [ ] **Step 1: TryPickBone の選択反映を分岐**

`BoneLineRenderer.TryPickBone` 内の `boneEditManager.SelectBone(MaidManipulateManager.instance.targetMaid, best);` を差し替え:

```csharp
            if (boneEditManager.isModelMode)
            {
                boneEditManager.SelectModelBone(best);
            }
            else
            {
                boneEditManager.SelectBone(MaidManipulateManager.instance.targetMaid, best);
            }
```

- [ ] **Step 2: DrawSlotBoneContent をモデル対応**

`InspectorWindow.DrawSlotBoneContent` を差し替え:

```csharp
        /// <summary>
        /// ボーン編集ウィンドウで選択したスロット/モデルボーン（ポーズ定義なし）の専用表示。
        /// 差分ストアの元値を基準にしたオフセット回転スライダーで編集し、編集はストアへ記録する
        /// </summary>
        private void DrawSlotBoneContent()
        {
            var isModel = boneEditManager.isModelMode;
            var maid = isModel ? null : maidManager.targetMaid;
            var bone = boneEditManager.selectedBone;

            // 退避中は表示に戻す際に上書きされるため操作させない (DrawBoneContent と同じ理由)
            if (!isModel && !maidManager.IsVisible(maid))
            {
                _view.DrawLabel("非表示中はボーンを操作できません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            _view.DrawLabel(bone.name, -1, RowHeight);

            DrawGizmoToolRow();
            if (!isModel)
            {
                // 揺れものはメイドの装着物専用
                DrawSlotBoneYureToggle(maid, bone);
            }

            var offset = boneEditManager.GetSelectedBoneOffset(maid);

            for (var i = 0; i < SlotBoneAxisLabels.Length; i++)
            {
                var axisIndex = i;

                _view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = SlotBoneAxisLabels[i],
                    labelWidth = LabelWidth,
                    width = -1,
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                    value = offset[axisIndex],
                    onChanged = value =>
                    {
                        if (isModel)
                        {
                            HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                                "ボーン編集: " + bone.name, new[] { bone });
                        }
                        else
                        {
                            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                                "ボーン編集: " + bone.name, new[] { bone });
                        }
                        boneEditManager.SetSelectedBoneOffsetAxis(maid, axisIndex, value);
                    },
                });
            }
        }
```

- [ ] **Step 3: ビルド確認**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/BoneLineRenderer.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs
git commit -m "feat(model-bone): ボーンピックと Inspector のスロットボーン編集をモデル対応"
```

---

### Task 7: SceneEditor 側の submodule 更新と動作確認

**Files:**
- Modify: submodule ポインタ `source/COM3D2.SceneEditor.Plugin/MTEUtils`

- [ ] **Step 1: ビルド確認（全体）**

Run: `cmd /c debug.bat com3d25`
Expected: ビルド成功

- [ ] **Step 2: ゲーム内確認（ゲーム起動中なら MCP `com3d25-devbridge` を使用）**

確認項目:
1. ボーン編集ウィンドウに「メイド / モデル」タブが出る
2. モデルタブで ModItemExplorer 未接続時（Task 8 完了前）は「対応プラグインの配置モデルがありません」が出て、エラーが出ない
3. メイドタブの既存機能（スロット選択・ボーン編集・プリセット保存/適用・undo）が退行していない

- [ ] **Step 3: コミット（submodule ポインタ更新込み）**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(deps): MTEUtils を ModelProviderClient 追加まで更新"
```

---

### Task 8: ModItemExplorer 側でモデル提供者を登録

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\ModelPlacement\SelfModelPlacer.cs`
- Modify: submodule ポインタ `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils`

**Interfaces:**
- Consumes: `ModelProviderClient.isAvailable / Register`（Task 2）、`ModelPlacerManager.instance.modelList`（既存）、`StudioModelStatWrapper.obj / displayName`（既存）

- [ ] **Step 1: ModItemExplorer 側の MTEUtils submodule を更新**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git fetch origin
git checkout master
git pull
cd ../../..
```

（Task 2 のコミットが master に反映済みであること。未 push なら SceneEditor 側 submodule から push してから行う）

- [ ] **Step 2: SelfModelPlacer にモデル提供者登録を追加**

フィールド追加（`_inspectorHandle` の近く）:

```csharp
        // SceneEditor の ModelProviderHost へ登録済みか。SceneEditor は後からロードされる
        // 可能性があるため成功するまで再試行する (選択購読の再試行間隔に相乗りする)
        private object _modelProviderHandle;
```

`TryRegisterHostConnections` の完了判定と呼び出しを更新:

```csharp
        private void TryRegisterHostConnections()
        {
            if (_selectionHandlerRegistered && _inspectorHandle != null && _modelProviderHandle != null)
            {
                return;
            }

            var frame = Time.frameCount;
            if (frame - _lastSelectionAttemptFrame < SelectionRetryIntervalFrames)
            {
                return;
            }
            _lastSelectionAttemptFrame = frame;

            if (!_selectionHandlerRegistered)
            {
                _selectionHandlerRegistered = SelectionClient.AddSelectionChangedHandler(OnHostSelectionChanged);
            }
            TryRegisterInspector();
            TryRegisterModelProvider();
        }
```

登録メソッドと提供デリゲートを追加（`TryRegisterInspector` の下）:

```csharp
        /// <summary>
        /// SceneEditor のボーン編集等へ配置モデルの一覧を提供する。
        /// SceneEditor 不在時は ModelProviderClient が無効を返すため何もしない
        /// </summary>
        private void TryRegisterModelProvider()
        {
            if (_modelProviderHandle != null || !ModelProviderClient.isAvailable)
            {
                return;
            }

            _modelProviderHandle = ModelProviderClient.Register(
                "ModItemExplorer",
                GetProvidedModels,
                GetProvidedModelName);

            if (_modelProviderHandle != null)
            {
                MTEUtils.LogDebug("SelfModelPlacer: ModelProviderHost へ登録しました");
            }
        }

        /// <summary>配置中モデルのルート GameObject 一覧 (MTE 配置分も含む)</summary>
        private List<GameObject> GetProvidedModels()
        {
            var result = new List<GameObject>();
            foreach (var model in ModelPlacerManager.instance.modelList)
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    result.Add(go);
                }
            }
            return result;
        }

        /// <summary>GameObject からモデルの表示名を逆引きする。管理外なら null (ホスト側が GO 名で表示)</summary>
        private string GetProvidedModelName(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            foreach (var model in ModelPlacerManager.instance.modelList)
            {
                if ((model.obj as GameObject) == go)
                {
                    return model.displayName;
                }
            }
            return null;
        }
```

- [ ] **Step 3: ModItemExplorer をビルド確認**

Run: `cmd /c debug.bat com3d25`（ModItemExplorer リポジトリルートで実行。debug.bat が無い/引数が違う場合はリポジトリの build 手順に従う）
Expected: ビルド成功

- [ ] **Step 4: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(model-placement): SceneEditor の ModelProviderHost へ配置モデル一覧を提供"
```

---

### Task 9: 統合動作確認とドキュメント更新

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\docs\external-plugin-api.md`（存在すれば連携の追記）
- Modify: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\CHANGELOG.md`

- [ ] **Step 1: 両プラグインをデプロイしてゲーム内で統合確認**

確認項目（MCP `com3d25-devbridge` の screenshot / eval_csharp を併用）:
1. ModItemExplorer でモデルを配置 → ボーン編集ウィンドウのモデルタブに表示名付きで列挙される
2. モデル選択 → 骨格線が表示され、関節クリックでボーン選択できる（SMR の無いモデルは「ボーンがありません」）
3. ギズモ/Inspector スライダーでボーン編集でき、undo (HistoryScope.Object) が効く
4. プリセット保存 → `UnityInjector\Config\PartsEdit\*.xml` に `bMaidParts=false` で書かれる
5. PartsEdit で保存したモデル用プリセットを SceneEditor から適用できる（逆も可）
6. モデル削除後にウィンドウ・ストアがエラーなく空へ戻る
7. メイドタブの既存機能に退行がない

- [ ] **Step 2: ドキュメントと CHANGELOG を更新**

- `external-plugin-api.md` に ModelProviderHost / ModelProviderClient の契約（Register シグネチャ・getModels/getDisplayName の意味・Unregister の義務）を追記
- SceneEditor の CHANGELOG.md に機能追加を記載

- [ ] **Step 3: コミット（各リポジトリ）**

```bash
git add CHANGELOG.md
git commit -m "docs: モデルボーン編集対応を CHANGELOG に記載"
```

---

## 既知の制限（スコープ外・計画に含めない）

- **シーンプリセット**: モデルのボーン編集はシーンプリセットへ保存されない（ScenePresetManager はメイド単位。モデル本体は ModItemExplorer の ScenePresetProvider が保存するが、ボーン差分は含まれない）。必要なら別タスクで ModelPlacementPresetProvider 側の XML にボーン差分を足す
- **undo とストア表示**: モデルボーンの undo は Transform を戻すが、BoneEditStore のエントリ（`*` 表示・記録値）は残る。元値は保持されるためリセットは正しく動く（メイド側 ObjectSnapshot 経由の編集と同じ挙動）
- **ギズモの二重表示**: ボーン選択中は SceneEditor がボーンギズモを出し、モデルルートの選択は `showGizmo:false` で行うため SceneEditor 側の二重表示はない。ModItemExplorer 自前のルートギズモが同時に出るかは ModelGizmoManager の仕様に依存する（問題があれば別タスクで調整）
- **アニメーション付きモデル**: Animation が回っているボーンは編集値が上書きされる（PartsEdit と同じ制限）

## レビュー却下メモ

- GetModels/GetProvidedModels の毎フレーム再構築による GC 負荷 — 却下: 既存 UI（GetLoadedSlotNames 等）も毎フレーム列挙の流儀で、想定モデル数（数十）では実害なし。未計測の最適化は見送り
- モデル削除後の Undo 安全性が未検証 — 却下（誤検知）: ObjectSnapshot.CanApply が `_bones.hasAliveBones` で生存チェックし、Apply も Transform ごとに null ガード済みを確認
- `_modelComboBox.currentIndex` の毎フレーム FindIndex — 却下: 線形探索コストは無視できる規模。既存コンボ（slotNames.IndexOf）と同等
- PartsEdit とのルート scale 適用非対称のユーザー向け説明 — 却下: Task 9 のドキュメント更新で対応済み（計画内に含まれる）
