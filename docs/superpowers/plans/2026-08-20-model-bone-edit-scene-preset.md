# モデルボーン編集のシーンプリセット統合 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 外部プラグイン配置モデルのボーン編集差分をシーンプリセットに保存し、プリセット復元時に GameObject 名で照合して再適用する。

**Architecture:** `ScenePresetData` に `modelBoneEdits`（v18）を追加。保存は `Capture` で `BoneEditManager._modelStores` を `ModelProviderHost.GetModels()` と突き合わせて名前解決、復元は `FinishApply` の `ApplyExternals` 直後（ModItemExplorer のモデル復元は同期なので、この時点で GameObject が全て存在する）。ModItemExplorer / MTEUtils は無改修。

**Tech Stack:** C# (.NET 3.5 相当 / Unity 5.6 系), XmlSerializer, MSBuild

**Spec:** `docs/superpowers/specs/2026-08-20-model-bone-edit-scene-preset-design.md`

## Global Constraints

- コメント・ログメッセージは日本語
- このリポジトリにユニットテスト基盤は無い。検証はビルド（MSBuild 直接、debug.bat は DLL を実機へコピーするため使わない）と com3d25-devbridge の実機確認で行う
- ビルドコマンド（リポジトリルートの source\COM3D2.SceneEditor.Plugin で実行）:
  `MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- `ModelProviderHost` は公開 API（シグネチャ変更禁止）。読み取り利用のみで変更しない
- C# 古語法に注意: out var 不可の可能性あり。周辺コードは `TryGetValue(key, out store)` の旧スタイル。式形式は既存コードに合わせる

---

### Task 1: ScenePresetData に modelBoneEdits DTO を追加

**Files:**
- Modify: `source\COM3D2.SceneEditor.Plugin\ScenePresetData.cs`

**Interfaces:**
- Produces: `ScenePresetModelBoneEdit`（フィールド: `modelName`, `pluginName`, `bone`, `pos[3]`, `rot[4]`, `scl[3]`、`isValid`、`static FromEntry(string modelName, string pluginName, BoneEditEntry entry)`）と `ScenePresetData.modelBoneEdits`（`List<ScenePresetModelBoneEdit>`、既定 null）

- [ ] **Step 1: DTO クラスを追加**

`ScenePresetBoneEdit` クラス（`ScenePresetData.cs:227` の閉じ括弧の後）に続けて追加:

```csharp
    /// <summary>
    /// v18: 外部プラグイン配置モデルのボーン 1 本分の編集差分。
    /// モデルは GameObject 参照でしか識別できないため、
    /// ラッパー GameObject 名 + 提供プラグイン名を照合キーとして保存する
    /// </summary>
    public class ScenePresetModelBoneEdit
    {
        /// <summary>モデルのルート GameObject 名 (照合キー)</summary>
        [XmlAttribute]
        public string modelName;
        /// <summary>ModelProviderHost に登録された提供プラグイン名 (同名衝突の回避用)</summary>
        [XmlAttribute]
        public string pluginName;
        [XmlAttribute]
        public string bone;

        /// <summary>localPosition xyz</summary>
        public float[] pos;
        /// <summary>localRotation xyzw</summary>
        public float[] rot;
        /// <summary>localScale xyz</summary>
        public float[] scl;

        public static ScenePresetModelBoneEdit FromEntry(
            string modelName, string pluginName, BoneEditEntry entry)
        {
            return new ScenePresetModelBoneEdit
            {
                modelName = modelName,
                pluginName = pluginName,
                bone = entry.boneName,
                pos = new[] { entry.position.x, entry.position.y, entry.position.z },
                rot = new[]
                {
                    entry.rotation.x, entry.rotation.y, entry.rotation.z, entry.rotation.w,
                },
                scl = new[] { entry.scale.x, entry.scale.y, entry.scale.z },
            };
        }

        /// <summary>XML は外部入力なので、要素数が足りないものは適用しない</summary>
        public bool isValid =>
            !string.IsNullOrEmpty(modelName) && !string.IsNullOrEmpty(bone)
            && pos != null && pos.Length >= 3
            && rot != null && rot.Length >= 4
            && scl != null && scl.Length >= 3;
    }
```

- [ ] **Step 2: version 履歴とフィールドを追加**

`ScenePresetData.cs:494-496` を変更。互換履歴コメントの末尾（v17 の行の後）に追記し、`CurrentVersion` を 18 に:

```csharp
        // v18: modelBoneEdits（外部プラグイン配置モデルのボーン編集差分）を追加。
        //      旧形式は null で読め、適用時にモデルのボーンへ触らない
        public static readonly int CurrentVersion = 18;
```

`externals` フィールド（`:527`）の後にフィールドを追加:

```csharp
        /// <summary>
        /// 外部プラグイン配置モデルのボーン編集差分 (v18)。
        /// 旧プリセット（要素なし）は null になり、適用時にモデルのボーンへ触らない
        /// </summary>
        [XmlElement("modelBoneEdit")]
        public List<ScenePresetModelBoneEdit> modelBoneEdits;
```

- [ ] **Step 3: ビルド確認**

Run: `MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
Expected: ビルド成功（エラー 0）

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs
git commit -m "feat(scene-preset): モデルボーン編集差分の DTO を追加 (v18)"
```

---

### Task 2: BoneEditManager にモデルストア列挙 API を追加

**Files:**
- Modify: `source\COM3D2.SceneEditor.Plugin\MaidManipulation\BoneEditManager.cs`

**Interfaces:**
- Produces: `BoneEditManager.GetModelStoreEntries()` → `List<KeyValuePair<GameObject, BoneEditStore>>`（破棄済み GameObject は除外済み）

- [ ] **Step 1: 列挙 API を追加**

`FindModelStore`（`BoneEditManager.cs:281-285`）の直後に追加:

```csharp
        /// <summary>
        /// モデル差分ストアの一覧 (プリセット保存用)。
        /// 破棄済みモデルは Update の掃除を待たずここで除外する
        /// </summary>
        public List<KeyValuePair<GameObject, BoneEditStore>> GetModelStoreEntries()
        {
            var result = new List<KeyValuePair<GameObject, BoneEditStore>>();
            foreach (var pair in _modelStores)
            {
                if (pair.Key != null && !pair.Value.isEmpty)
                {
                    result.Add(pair);
                }
            }
            return result;
        }
```

- [ ] **Step 2: ビルド確認**

Run: `MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
Expected: ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs
git commit -m "feat(model-bone): モデル差分ストアの列挙 API を追加"
```

---

### Task 3: ScenePresetManager の保存・復元にモデルボーン差分を統合

**Files:**
- Modify: `source\COM3D2.SceneEditor.Plugin\Manager\ScenePresetManager.cs`

**Interfaces:**
- Consumes: Task 1 の `ScenePresetModelBoneEdit` / `ScenePresetData.modelBoneEdits`、Task 2 の `GetModelStoreEntries()`、既存の `ModelProviderHost.GetModels()`（`List<ExternalModelEntry>`、各要素は `obj` / `displayName` / `pluginName`）、`BoneEditManager.GetModelStore(GameObject)` / `FindModelStore(GameObject)`、`BoneEditStore.RecordEdit(slotName, itemFileName, bone)` / `ResetSlot(slotName, slotObj)`、`SlotBoneManager.FindBone(GameObject, string)`、`BoneEditManager.ModelSlotKey`

- [ ] **Step 1: 保存処理を追加**

`Capture`（`ScenePresetManager.cs:697-730`）の `CaptureExternals(data, options.enabledProviderIds);` の直後（`return data;` の前）に呼び出しを追加:

```csharp
            CaptureModelBoneEdits(data);
```

`CaptureExternals` メソッドの後に本体を追加:

```csharp
        /// <summary>
        /// 外部プラグイン配置モデルのボーン編集差分を収集する (v18)。
        /// モデルは GameObject 参照でしか識別できないため、ModelProviderHost の
        /// 現在の一覧と突き合わせて GameObject 名 + 提供プラグイン名へ変換して保存する。
        /// 提供元が見つからないモデル (破棄済み・提供解除済み) は保存しない。
        /// モデル自体は外部プロバイダが復元するため、外部プロバイダを 1 つも保存して
        /// いないプリセットにボーン差分だけ残しても復元先が無い。孤立データを避けるため
        /// external が空のときは保存しない
        /// (プロバイダ id と ModelProviderHost の pluginName は別体系のため個別対応は取らない)
        /// </summary>
        private static void CaptureModelBoneEdits(ScenePresetData data)
        {
            if (data.externals.Count == 0)
            {
                return;
            }

            var storeEntries = BoneEditManager.instance.GetModelStoreEntries();
            if (storeEntries.Count == 0)
            {
                return;
            }

            var models = ModelProviderHost.GetModels();
            foreach (var pair in storeEntries)
            {
                var entry = models.Find(m => m.obj == pair.Key);
                if (entry == null)
                {
                    MTEUtils.LogWarning(
                        "提供元が見つからないモデルのボーン編集は保存しません: " + pair.Key.name);
                    continue;
                }

                foreach (var edit in pair.Value.GetAllEntries())
                {
                    if (data.modelBoneEdits == null)
                    {
                        data.modelBoneEdits = new List<ScenePresetModelBoneEdit>();
                    }
                    data.modelBoneEdits.Add(ScenePresetModelBoneEdit.FromEntry(
                        pair.Key.name, entry.pluginName, edit));
                }
            }
        }
```

- [ ] **Step 2: 復元処理を追加**

`FinishApply`（`ScenePresetManager.cs:1344-1358`）の `ApplyExternals(data);` の直後に呼び出しを追加:

```csharp
            ApplyExternals(data);
            // 外部プロバイダのモデル復元 (同期) の後でないと GameObject が存在しない
            ApplyModelBoneEdits(data);
```

`ApplyBoneEdits`（`:1700-1742`）の後に本体を追加:

```csharp
        /// <summary>
        /// モデルのボーン編集差分を復元する (v18)。旧プリセット (modelBoneEdits 無し) では変更しない。
        /// GameObject 名 + 提供プラグイン名で現在のモデルへ照合する (同名複数は先勝ち)。
        /// メイドの ApplyBoneEdits と同じく RecordEdit を書き込みの前後で 2 回呼び、
        /// 「元値 = 適用前」「編集値 = 適用後」にする (リセットで戻せる)
        /// </summary>
        private static void ApplyModelBoneEdits(ScenePresetData data)
        {
            if (data.modelBoneEdits == null || data.modelBoneEdits.Count == 0)
            {
                return;
            }

            var models = ModelProviderHost.GetModels();

            // 差分を当てるモデルは既存の編集をリセットしてプリセットの状態で置き換える。
            // プロバイダの読込を OFF にした場合など、モデルが作り直されず残るケースへの対処
            var targets = new Dictionary<GameObject, bool>();
            foreach (var edit in data.modelBoneEdits)
            {
                if (edit == null || !edit.isValid)
                {
                    continue;
                }

                var entry = models.Find(m =>
                    m.obj.name == edit.modelName && m.pluginName == edit.pluginName);
                if (entry == null)
                {
                    MTEUtils.LogWarning(
                        "ボーン編集の対象モデルが見つかりません: " + edit.modelName);
                    continue;
                }

                var store = BoneEditManager.instance.GetModelStore(entry.obj);
                if (!targets.ContainsKey(entry.obj))
                {
                    store.ResetSlot(BoneEditManager.ModelSlotKey, entry.obj);
                    targets[entry.obj] = true;
                }

                var bone = SlotBoneManager.FindBone(entry.obj, edit.bone);
                if (bone == null)
                {
                    MTEUtils.LogWarning(
                        "ボーン編集の対象ボーンが見つかりません: "
                        + edit.modelName + "/" + edit.bone);
                    continue;
                }

                // 先に呼んで適用前の値を元値として控える
                store.RecordEdit(BoneEditManager.ModelSlotKey, null, bone);

                bone.localPosition = new Vector3(edit.pos[0], edit.pos[1], edit.pos[2]);
                bone.localRotation = new Quaternion(edit.rot[0], edit.rot[1], edit.rot[2], edit.rot[3]);
                bone.localScale = new Vector3(edit.scl[0], edit.scl[1], edit.scl[2]);

                store.RecordEdit(BoneEditManager.ModelSlotKey, null, bone);
            }
        }
```

注意: `MTEUtils.LogWarning` が存在しない場合は周辺コードで使われているログ API（`MTEUtils.LogError` 等、`ScenePresetManager.cs:775` 参照）に合わせること。

- [ ] **Step 3: ビルド確認**

Run: `MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
Expected: ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(scene-preset): モデルボーン編集差分の保存・復元を追加"
```

---

### Task 4: 実機検証と CHANGELOG 更新

**Files:**
- Modify: `CHANGELOG.md`（リポジトリルート。既存の書式に合わせて未リリース節へ追記）

**Interfaces:**
- Consumes: Task 1〜3 の全実装

- [ ] **Step 1: 実機検証（ゲーム起動中に com3d25-devbridge で確認）**

ホットリロード（CLAUDE.md の手順: 旧パッチ解除 → バイト配列ロード → 再パッチ）または ゲーム再起動で新 DLL を反映した上で、UI 操作により以下を確認:

1. ModItemExplorer でモデルを配置 → SceneEditor のボーン編集でボーンを動かす → シーンプリセット保存 → 保存された XML（`UnityInjector/Config/SceneEditor/ScenePreset/*.xml`）に `modelBoneEdit` 要素があること
2. モデルを削除（またはシーンリセット）→ プリセット読込 → モデルが復元され、ボーン差分も適用されていること（`eval_csharp` で対象ボーンの localPosition を読んで保存値と一致確認）
3. 旧バージョンのプリセット（v17 以前）を読込 → エラーなく完走すること
4. モデル配置プロバイダの読込トグル OFF でプリセット読込 → 警告ログのみで完走すること

Expected: 4 項目すべて成立

- [ ] **Step 2: CHANGELOG 追記**

`CHANGELOG.md` の未リリース節（無ければ既存書式に合わせて新設）に 1 行追加:

```markdown
- モデルのボーン編集差分をシーンプリセットに保存・復元できるようにした (プリセット v18)
```

- [ ] **Step 3: コミット**

```bash
git add CHANGELOG.md
git commit -m "docs: モデルボーン編集のプリセット対応を CHANGELOG に記載"
```

---

## レビュー却下メモ

- 差分ゼロのボーンを RecordEdit しない最適化（PartsEditPresetIO.ApplyTransformList:411-416 相当）が無い — メイド版 ApplyBoneEdits も同じく持たない既存パターンの意図的踏襲のため却下（挙動差を出さないことを優先）
