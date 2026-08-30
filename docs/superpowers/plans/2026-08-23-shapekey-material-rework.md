# シェイプキー / マテリアルウィンドウ作り直し（タイムライン非依存化 + シーンプリセット連携）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 既存の ShapeKeyEditWindow / MaterialEditWindow をタイムライン非依存に作り直し（メイドは全シェイプキー操作可能に）、編集値をシーンプリセットへ保存・適用できるようにする。

**Architecture:** タイムライン登録集合（`timeline.GetMaidShapeKeys` 等）への依存を全廃し、メイドは `MaidCache`（タイムライン無しで動くことを確認済み）の全スロット morph タグを直接編集する。マテリアルのモデルタブは `ModelProviderHost`（GameObject ベース）+ 軽量 `IModelStat` ラッパー、背景タブは現在の BG オブジェクト直下の Renderer 走査へ切り替え、`ModelMaterialController.GetOrCreate`（タイムライン非依存・GameObject 単位で冪等）で編集する。シーンプリセットには表情モーフ（`ScenePresetMorph` の差分保存方式）と `ScenePresetModelBoneEdit`（modelName + pluginName 照合）の既存パターンを踏襲して保存・適用する。

**Tech Stack:** C# (.NET Framework, Unity IMGUI / GUIView), XmlSerializer, 非 SDK 形式 csproj, MSBuild

**Spec:** ユーザー指示（2026-08-23）:「シェイプキー/マテリアルはタイムラインに関係なく操作可能にして。タイムラインに紐づくのはレイヤー編集のほう。こっちはすべてのシェイプキーが操作できる。シーンプリセットにも反映させて」

## 設計判断（計画時に確定）

1. **タイムライン登録集合はレイヤー編集（TimelineLayerWindow）専用に純化する。** ShapeKeyEditWindow から `timeline.Has/Add/Remove/GetMaidShapeKeys` を全廃し、追加/操作の 2 タブ構成を廃止して「スロット選択 → そのスロットの全タグをスライダー表示」の 1 画面にする。キーフレーム対象タグの登録はレイヤー編集ウィンドウ（ShapeKeyTimelineLayer の DrawWindow）の責務のまま。
2. **`isPoseEditing` ガードを両ウィンドウから撤去する。** これは SE の編集モードトグル（`MaidManipulateManager.isEditMode`）であり、SE ネイティブウィンドウ（BoneEditWindow / MaidFaceWindow）はいずれも依存していない。タイムライン再生中はレイヤーが毎フレーム値を書くためスライダーと取り合いになるが、これは既知の挙動として文書化する（ガードで操作不能にするより「常に操作できる」を優先。ユーザー指示とも整合）。
3. **シェイプキーのモデルタブは `StudioModelManager.models` のまま**（タイムラインガードは既に無い）。BlendShapeController は `.model` ファイル名（`StudioModelStat.info`）が必要で、ModelProviderHost の素の GameObject には構築できないため。タイムライン未読み込み時は「配置中のモデルがありません」表示になる（実質タイムライン経由でしかモデルが増えない制約は将来課題として spec に記録）。
4. **マテリアルのモデルタブは `ModelProviderHost.GetModels()` に切り替える。** `MTEP.IModelStat` は `name/displayName/transform` の 3 メンバーのみ（`StudioModelStat.cs:17-22`）なので、GameObject を包む `ProviderModelStat` を新設し `ModelMaterialController.GetOrCreate`（`GetOrAddComponent` ベースで GameObject 単位に冪等）へ渡す。タイムラインモデル（TimelineIntegration が provider 登録済み）も ModItemExplorer のモデルも同じ経路で編集できる。
5. **マテリアルの背景タブは BGModelManager を使わない。** `BGModelManager.models` はタイムライン由来でしか埋まらず、SE 側から `AddModel` すると `UpdateTimelineBGModels`（`BGModelManager.cs:165-193`）経由でタイムラインデータを汚染する。代わりに現在の BG オブジェクト（`GameMain.Instance.BgMgr.BgObject`）配下の Renderer を走査して `ProviderModelStat` で包む。タイムラインの BGModelStat と同じ GameObject を指す場合も `GetOrCreate` の冪等性で同一コントローラを共有する。
6. **シーンプリセットは「現在値の差分保存」。** タイムラインのキーフレームは従来どおりプリセットに入れない（2026-08-23 仕様決定と整合）。
   - メイドシェイプキー: 非ゼロ値のタグのみ保存。**公式表情モーフ（`MaidFaceMorphController.GetAvailableMorphs` の def.name）は除外**（表情は既存の faceName/morphs セクションの責務。二重管理を避ける）。適用は保存タグのみ設定し、未保存タグのゼロ化はしない（表情モーフを踏まないため。「プリセットに無い編集値は残る」制約として文書化）
   - マテリアル: `GetInitialColor/GetInitialValue` と異なるプロパティのみ保存。適用は保存プロパティのみ設定
7. **モデルの同定キーは `ScenePresetModelBoneEdit` と同じ「GameObject 名 + pluginName」**（`ScenePresetManager.cs:1877-1878` の照合パターンを流用）。モデルシェイプキーだけは StudioModelStat が必要なので、適用時に GameObject から `modelManager.models` を transform 一致で逆引きする。背景マテリアルは BG ルートからの相対パスで同定する（bgId は既存 background セクションが復元済みの前提）。
8. **`ModelMaterial` の「初期化」ボタンの意味変化を許容する。** プリセット適用後に `UpdateMaterial` が走ると `initial*` が適用後の値で上書きされ「初期化」がプリセット適用値へ戻るようになるが、これは既存のマテリアルライフサイクル（ロード時点の値 = 初期値）どおりの挙動。
9. **Undo（SE HistoryManager）は引き続きスコープ外**（前計画の判断を維持。プリセット適用は `ClearHistory` するため矛盾しない）。

## Global Constraints

- namespace は `COM3D2.SceneEditor.Plugin`、タイムライン型は `using MTEP = COM3D2.MotionTimelineEditor.Plugin;`
- 新規 .cs は csproj（非 SDK 形式）へ `<Compile Include="..." />` を手動追加
- `GUIView.BeginScrollView` はネスト不可（1 段だけ）
- `GetColorFieldCache("", true)` は空文字ラベルで取得（位置ベースプール。名前を渡すと二重描画）
- コメント・ログは日本語
- ビルド確認（deploy.bat / release.bat / debug.bat は使わない）:
  ```
  MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- `ScenePresetData` へのフィールド追加時は `CurrentVersion` を 21 へ上げ、バージョン履歴コメント（`ScenePresetData.cs:530-566`）へ 1 行追加
- プリセットの新セクションは「null = 旧プリセット（触らない）/ 空リスト = 保存したが空」の互換規約を守る

## 参照実装マップ

| 参照元 | 何の雛形か |
|---|---|
| `ShapeKeyEditWindow.cs`（現行 399 行） | 作り直しのベース。スロット/タグの二段キャッシュ（62-68, 244-279）は維持 |
| `MaterialEditWindow.cs`（現行 316 行） | 作り直しのベース。`DrawMaterialSelector` / `DrawMaterialProperties`（221-314）は維持 |
| `Timeline/MaidCache.cs:656-742` | `GetBlendShape` / `SetBlendShapeValue` / `FixBlendValues`（タイムライン非依存を確認済み） |
| `Timeline/ModelMaterialController.cs:61-74` | `GetOrCreate(IModelStat)` |
| `ScenePresetData.cs:355-361` (`ScenePresetMorph`), `:234-275` (`ScenePresetModelBoneEdit`) | 保存データ型の雛形 |
| `Manager/ScenePresetManager.cs:1982-2011` (`ApplyFace`), `:1858-1909` (`ApplyModelBoneEdits`), `:697-732` (`Capture`), `:828-925` (`CaptureMaid`), `:1415-1431` (`FinishApply`) | 保存・適用の組み込み位置 |

---

### Task 1: ShapeKeyEditWindow のタイムライン非依存化

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs`（全面書き直しに近い）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs`（キャッシュ破棄 API 追加）

**Interfaces:**
- Consumes: `MTEP.MaidManager.instance.GetMaidCache(Maid)`、`MaidCache.GetBlendShape(string)/FixBlendValues(IEnumerable<string>)`、`MTEP.StudioModelManager.instance.models`
- Produces: `MaidCache.ClearBlendShapeCache()`（Task 3 の適用処理も使用）、タイムライン参照ゼロの ShapeKeyEditWindow

- [ ] **Step 1: MaidCache にキャッシュ破棄 API を追加**

`Timeline/MaidCache.cs` の `GetBlendShape`（656 行）付近に追加。着替え後に `_blendShapeCache` が古い `TMorph` を掴んだままになる問題への対処で、ウィンドウの「更新」ボタンとプリセット適用から呼ぶ:

```csharp
/// <summary>着替えで TMorph が差し替わった後に呼び、シェイプキーのキャッシュを破棄する</summary>
public void ClearBlendShapeCache()
{
    _blendShapeCache.Clear();
}
```

- [ ] **Step 2: ShapeKeyEditWindow を書き直す**

変更点（現行コードからの差分方針。行番号は現行ファイル基準）:

1. **削除**: 45-46 の `timelineManager` / `timeline` プロパティ、36-40 の `MaidTabType` enum と `_maidTab`、178-234 の `DrawMaidShapeKeyAdd` 全体（タイムライン登録トグルはレイヤー編集の責務へ純化）
2. **削除**: 302 / 368 の `studioHackManager.isPoseEditing` 条件（`view.SetEnabled(view.focusedComboBox == null)` に変更）。49 の `studioHackManager` プロパティも参照が無くなるので削除
3. **メイドタブを 1 画面へ**: スロットコンボ（既存 `_slotNameComboBox` + `UpdateSlotNames` / `UpdateTags` の二段キャッシュを維持）→ 選択スロットの**全タグ**をスライダー表示。一覧のソースを `timeline.GetMaidShapeKeys`（291 行）から `_tags`（`UpdateTags` が作る全タグリスト）へ差し替える

書き直し後の `DrawMaidShapeKeyEdit` 相当（メソッド名は `DrawMaidShapeKeys` に変更）:

```csharp
// スロット選択 → そのスロットの全シェイプキーをスライダー表示。
// キーフレーム対象タグの登録はレイヤー編集ウィンドウの責務で、ここでは扱わない
private void DrawMaidShapeKeys(Maid target, MTEP.MaidCache maidCache)
{
    UpdateSlotNames(target);
    if (_slotNames.Count == 0)
    {
        view.DrawLabel("シェイプキーを持つスロットがありません", -1, ROW_HEIGHT);
        return;
    }

    _slotNameComboBox.items = _slotNames;
    view.BeginHorizontal();
    view.DrawLabel("スロット", LABEL_WIDTH, ROW_HEIGHT);
    _slotNameComboBox.DrawButton(view);
    // 着替えはウィンドウ側から検知できないため明示更新
    if (view.DrawButton("更新", 50, ROW_HEIGHT))
    {
        ClearSlotCache();
        maidCache.ClearBlendShapeCache();
    }
    view.EndLayout();

    var slotName = _slotNameComboBox.currentItem;
    if (string.IsNullOrEmpty(slotName))
    {
        return;
    }

    UpdateTags(target, slotName);

    view.SetEnabled(view.focusedComboBox == null);
    view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

    foreach (var tag in _tags)
    {
        var blendShape = maidCache.GetBlendShape(tag);
        if (blendShape == null || blendShape.entities.Count == 0)
        {
            continue;
        }

        var weight = blendShape.weight;
        var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
        {
            label = tag,
            labelWidth = 100,
            min = 0f,
            max = 2f,
            step = 0.01f,
            defaultValue = 0f,
            value = weight,
            onChanged = x => weight = x,
        });

        if (updateTransform)
        {
            blendShape.weight = weight;
            maidCache.FixBlendValues(new[] { tag });
        }
    }

    view.EndScrollView();
}
```

`DrawBody` は「対象種別タブ → モデルなら `DrawModelContent()` / メイドなら `DrawMaidSelector` → `GetMaidCache` → `DrawMaidShapeKeys`」の直列にする（内部タブ呼び出しを削除）。`DrawModelContent`（339-397）は isPoseEditing 撤去以外は現行のまま。

**実装時の注意:**
- `MaidBlendShape.entities` が private の場合は `MaidBlendShape.cs` に `public int entityCount => entities.Count` 等の公開手段を追加してガードに使う（weight getter は entities 空で `InvalidOperationException` を投げるため、空チェックは必須）
- `UpdateSlotNames` / `UpdateTags` / `ClearSlotCache`（現行 237-279）は変更不要でそのまま使う

- [ ] **Step 3: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded、エラー 0

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs
git commit -m "feat(timeline): シェイプキー編集をタイムライン非依存化し全シェイプキーを操作可能にする"
```

---

### Task 2: MaterialEditWindow のタイムライン非依存化（ProviderModelStat 導入）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ProviderModelStat.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `MTEP.IModelStat`（`name/displayName/transform`、`StudioModelStat.cs:17-22`）、`ModelProviderHost.GetModels()`（`ExternalModelEntry { GameObject obj; string displayName; string pluginName; }`）、`MTEP.ModelMaterialController.GetOrCreate(IModelStat)`
- Produces: `ProviderModelStat`（Task 4 のプリセット保存・適用も使用）:
  - `public ProviderModelStat(GameObject obj, string displayName)`
  - `public GameObject obj { get; }` / `string name` / `string displayName` / `Transform transform`
  - `public List<MTEP.ModelMaterial> materials`（`GetOrCreate` 経由）
  - `public static ProviderModelStat GetOrCreate(GameObject obj, string displayName)`（GameObject 単位のインスタンスキャッシュ）

- [ ] **Step 1: ProviderModelStat.cs を作成**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 任意の GameObject を IModelStat として扱う軽量ラッパー。
    /// ModelProviderHost 提供モデルや背景オブジェクトのマテリアル編集に使う。
    /// ModelMaterialController は GameObject 単位で冪等なため、
    /// タイムラインの StudioModelStat と同じ GameObject を指しても同一コントローラを共有する
    /// </summary>
    public class ProviderModelStat : MTEP.IModelStat
    {
        public GameObject obj { get; private set; }
        public string name => obj != null ? obj.name : "";
        public string displayName { get; private set; }
        public Transform transform => obj != null ? obj.transform : null;

        private MTEP.ModelMaterialController _materialController;

        public List<MTEP.ModelMaterial> materials
        {
            get
            {
                if (obj == null)
                {
                    return new List<MTEP.ModelMaterial>();
                }
                if (_materialController == null)
                {
                    _materialController = MTEP.ModelMaterialController.GetOrCreate(this);
                }
                return _materialController != null
                    ? _materialController.materials
                    : new List<MTEP.ModelMaterial>();
            }
        }

        private ProviderModelStat(GameObject obj, string displayName)
        {
            this.obj = obj;
            this.displayName = displayName;
        }

        // GameObject が破棄されたエントリはリークしないよう都度掃除する
        private static readonly Dictionary<GameObject, ProviderModelStat> _cache
            = new Dictionary<GameObject, ProviderModelStat>();

        public static ProviderModelStat GetOrCreate(GameObject obj, string displayName)
        {
            if (obj == null)
            {
                return null;
            }

            ProviderModelStat stat;
            if (!_cache.TryGetValue(obj, out stat))
            {
                stat = new ProviderModelStat(obj, displayName);
                _cache.Add(obj, stat);
            }
            stat.displayName = displayName;
            return stat;
        }

        public static void CleanupDestroyed()
        {
            var deadKeys = new List<GameObject>();
            foreach (var pair in _cache)
            {
                if (pair.Key == null)
                {
                    deadKeys.Add(pair.Key);
                }
            }
            foreach (var key in deadKeys)
            {
                _cache.Remove(key);
            }
        }
    }
}
```

**実装時の注意:** Unity の Destroy 済み GameObject は `== null` が真になるが Dictionary キーとしては生きているため、`CleanupDestroyed` は列挙（`DrawModelMaterial` / プリセット適用）の冒頭で呼ぶ。

- [ ] **Step 2: MaterialEditWindow のモデルタブを ModelProviderHost へ切り替え**

現行 `DrawModelMaterial`（177-197）を差し替え。`_modelComboBox` の型は `GUIComboBox<ProviderModelStat>` へ変更:

```csharp
private readonly GUIComboBox<ProviderModelStat> _modelComboBox = new GUIComboBox<ProviderModelStat>
{
    getName = (model, index) => model.displayName,
    buttonSize = new Vector2(200, 20),
    contentSize = new Vector2(200, 300),
};

// ModelProviderHost 経由で列挙する。タイムラインモデルも
// ModItemExplorer 等の外部モデルも同じ経路で編集できる
private void DrawModelMaterial()
{
    ProviderModelStat.CleanupDestroyed();

    var entries = ModelProviderHost.GetModels();
    var models = new List<ProviderModelStat>(entries.Count);
    foreach (var entry in entries)
    {
        var stat = ProviderModelStat.GetOrCreate(entry.obj, entry.displayName);
        if (stat != null)
        {
            models.Add(stat);
        }
    }

    if (models.Count == 0)
    {
        view.DrawLabel("配置中のモデルがありません", -1, ROW_HEIGHT);
        return;
    }

    _modelComboBox.items = models;
    view.DrawLabel("モデル", LABEL_WIDTH, ROW_HEIGHT);
    _modelComboBox.DrawButton(view);

    var model = _modelComboBox.currentItem;
    if (model == null)
    {
        return;
    }

    DrawMaterialSelector(model.materials);
}
```

- [ ] **Step 3: 背景タブを BG オブジェクト直下の Renderer 走査へ切り替え**

現行 `DrawBGModelMaterial`(199-219) を差し替え。`_bgModelComboBox` の型も `GUIComboBox<ProviderModelStat>` へ。`bgModelManager` プロパティ（34 行）は参照が無くなるので削除:

```csharp
// 現在の背景オブジェクト配下の Renderer 持ち GameObject を列挙する。
// BGModelManager はタイムラインデータと双方向同期するため使わない
private void DrawBGModelMaterial()
{
    ProviderModelStat.CleanupDestroyed();

    var bgObject = GameMain.Instance.BgMgr.BgObject;
    if (bgObject == null)
    {
        view.DrawLabel("背景が設定されていません", -1, ROW_HEIGHT);
        return;
    }

    var models = new List<ProviderModelStat>();
    foreach (var renderer in bgObject.GetComponentsInChildren<Renderer>(true))
    {
        var stat = ProviderModelStat.GetOrCreate(renderer.gameObject, renderer.gameObject.name);
        if (stat != null)
        {
            models.Add(stat);
        }
    }

    if (models.Count == 0)
    {
        view.DrawLabel("背景にマテリアルがありません", -1, ROW_HEIGHT);
        return;
    }

    _bgModelComboBox.items = models;
    view.DrawLabel("背景", LABEL_WIDTH, ROW_HEIGHT);
    _bgModelComboBox.DrawButton(view);

    var model = _bgModelComboBox.currentItem;
    if (model == null)
    {
        return;
    }

    DrawMaterialSelector(model.materials);
}
```

**実装時の注意:**
- `GameMain.Instance.BgMgr.BgObject` のプロパティ名は `Assembly-CSharp` の `BgMgr` 実物（`W:\COM3D2_5\work\Assembly-CSharp\BgMgr.cs`）で確認する（`current_bg_object` 等の可能性あり。`BGModelManager.cs` の `bgObject` プロパティの実装を見るのが早い）
- ParticleSystem 等の Renderer も混ざるが、`ModelMaterial.Init` は既知プロパティ（`HasProperty`）だけを拾うため実害はない。表示過多が問題なら実機確認時に判断する

- [ ] **Step 4: isPoseEditing ガード撤去**

`DrawMaterialProperties` の 254 行を `view.SetEnabled(view.focusedComboBox == null);` に変更。`studioHackManager` プロパティ（35 行）も参照が無くなるので削除。

- [ ] **Step 5: csproj に Compile 追加**

```xml
<Compile Include="ProviderModelStat.cs" />
```

- [ ] **Step 6: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ProviderModelStat.cs source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): マテリアル編集をタイムライン非依存化しモデル/背景を直接列挙する"
```

---

### Task 3: シーンプリセットへメイドのシェイプキー / マテリアルを保存・適用

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`

**Interfaces:**
- Consumes: `ScenePresetMorph`（name/value、`ScenePresetData.cs:355-361`）、`MaidCache.GetBlendShapeValue/SetBlendShapeValue/FixBlendValues/ClearBlendShapeCache`、`MaidCache.slotStats/UpdateMaterials()`、`MTEP.ModelMaterial` の Get/Set/GetInitial 系、`MaidFaceMorphController.GetAvailableMorphs(Maid, FaceMorphCategory)`
- Produces:
  - `ScenePresetMaterialColor { string name; float[] rgba; }` / `ScenePresetMaterialValue { string name; float value; }` / `ScenePresetMaterial { string owner; string material; List<...> colors/values; }`（Task 4 も再利用）
  - `ScenePresetMaid.shapeKeys : List<ScenePresetMorph>`（XmlElement "shapeKey"）
  - `ScenePresetMaid.materials : List<ScenePresetMaterial>`（XmlElement "material"）
  - `ScenePresetManager.CaptureMaterial(MTEP.ModelMaterial, string owner)` / `ApplyMaterial(MTEP.ModelMaterial, ScenePresetMaterial)`（static private、Task 4 も使用）

- [ ] **Step 1: ScenePresetData にデータ型とフィールドを追加**

`ScenePresetMorph`（361 行）の直後にマテリアル用データ型を追加:

```csharp
/// <summary>マテリアルの色プロパティ 1 件（初期値と異なるもののみ保存）</summary>
public class ScenePresetMaterialColor
{
    [XmlAttribute("name")]
    public string name;

    // Color を直に持つと XmlSerializer が余計なプロパティまで直列化し、
    // 自前の文字列化はカルチャ依存になるため、素の float 4 属性で持つ
    [XmlAttribute("r")]
    public float r;

    [XmlAttribute("g")]
    public float g;

    [XmlAttribute("b")]
    public float b;

    [XmlAttribute("a")]
    public float a;

    [XmlIgnore]
    public Color rgba
    {
        get => new Color(r, g, b, a);
        set { r = value.r; g = value.g; b = value.b; a = value.a; }
    }
}

/// <summary>マテリアルの数値プロパティ 1 件（初期値と異なるもののみ保存）</summary>
public class ScenePresetMaterialValue
{
    [XmlAttribute("name")]
    public string name;

    [XmlAttribute("value")]
    public float value;
}

/// <summary>マテリアル 1 件分の編集差分</summary>
public class ScenePresetMaterial
{
    /// <summary>持ち主の識別子。メイドはスロット名、モデル/背景は所属セクション側で解釈する</summary>
    [XmlAttribute("owner")]
    public string owner;

    [XmlAttribute("material")]
    public string material;

    /// <summary>同名マテリアルが複数ある場合の同定用インデックス（一覧内の位置）</summary>
    [XmlAttribute("index")]
    public int index;

    [XmlElement("color")]
    public List<ScenePresetMaterialColor> colors = new List<ScenePresetMaterialColor>();

    [XmlElement("value")]
    public List<ScenePresetMaterialValue> values = new List<ScenePresetMaterialValue>();

    [XmlIgnore]
    public bool isEmpty => (colors == null || colors.Count == 0) && (values == null || values.Count == 0);
}
```

`ScenePresetMaid` へフィールド追加（`morphs`（460-461）の直後）:

```csharp
/// <summary>任意シェイプキー（公式表情モーフを除く非ゼロ値のみ）。null は旧バージョン</summary>
[XmlElement("shapeKey")]
public List<ScenePresetMorph> shapeKeys;

/// <summary>スロットマテリアルの編集差分（owner = スロット名）。null は旧バージョン</summary>
[XmlElement("material")]
public List<ScenePresetMaterial> materials;
```

`CurrentVersion` を 21 へ上げ、履歴コメントへ追加:

```csharp
// v21: メイドの任意シェイプキー(shapeKey)とマテリアル差分(material)、モデル/背景のシェイプキー・マテリアルを追加
public static readonly int CurrentVersion = 21;
```

- [ ] **Step 2: ScenePresetManager にマテリアル共通ヘルパを追加**

`ApplyFace`（1982 行）付近に追加。Task 4 のモデル/背景でも使う:

```csharp
/// <summary>マテリアルの編集差分（初期値と異なるプロパティのみ）を控える。差分ゼロなら null</summary>
private static ScenePresetMaterial CaptureMaterial(MTEP.ModelMaterial material, string owner, int index)
{
    var data = new ScenePresetMaterial
    {
        owner = owner,
        material = material.displayName,
        index = index,
    };

    foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
    {
        if (!material.HasColor(propertyType)) continue;
        var color = material.GetColor(propertyType);
        if (color == material.GetInitialColor(propertyType)) continue;
        data.colors.Add(new ScenePresetMaterialColor
        {
            name = propertyType.ToString(),
            rgba = color,
        });
    }

    foreach (var propertyType in MTEP.ModelMaterial.ValuePropertyTypes)
    {
        if (!material.HasValue(propertyType)) continue;
        var value = material.GetValue(propertyType);
        if (value == material.GetInitialValue(propertyType)) continue;
        data.values.Add(new ScenePresetMaterialValue
        {
            name = propertyType.ToString(),
            value = value,
        });
    }

    return data.isEmpty ? null : data;
}

/// <summary>保存されたプロパティだけをマテリアルへ書き戻す</summary>
private static void ApplyMaterial(MTEP.ModelMaterial material, ScenePresetMaterial state)
{
    if (state.colors != null)
    {
        foreach (var color in state.colors)
        {
            MTEP.ModelMaterial.ColorPropertyType type;
            if (!TryParseEnum(color.name, out type)) continue;
            if (!material.HasColor(type)) continue;
            material.SetColor(type, color.rgba);
        }
    }

    if (state.values != null)
    {
        foreach (var value in state.values)
        {
            MTEP.ModelMaterial.ValuePropertyType type;
            if (!TryParseEnum(value.name, out type)) continue;
            if (!material.HasValue(type)) continue;
            material.SetValue(type, value.value);
        }
    }
}

/// <summary>
/// 保存されたマテリアルを一覧から同定する。
/// 同名マテリアルが複数ある場合に備え、保存時インデックスの名前一致を優先し、無ければ先頭の名前一致
/// </summary>
private static MTEP.ModelMaterial FindMaterial(List<MTEP.ModelMaterial> materials, ScenePresetMaterial state)
{
    if (state.index >= 0 && state.index < materials.Count
        && materials[state.index].displayName == state.material)
    {
        return materials[state.index];
    }
    return materials.Find(m => m.displayName == state.material);
}

/// <summary>Enum.TryParse が使えない場合に備えた安全なパース（未知の名前は無視して互換を保つ）</summary>
private static bool TryParseEnum<T>(string name, out T result) where T : struct
{
    try
    {
        result = (T)Enum.Parse(typeof(T), name);
        return true;
    }
    catch (Exception)
    {
        result = default(T);
        return false;
    }
}
```

- [ ] **Step 3: CaptureMaid へシェイプキーとマテリアルの収集を追加**

`CaptureMaid`（828-925）の表情収集（869-879）直後に追加:

```csharp
// 任意シェイプキー: 公式表情モーフは faceName/morphs の責務なので除外し、非ゼロ値だけを控える
try
{
    var maidCache = MTEP.MaidManager.instance.GetMaidCache(maid);
    if (maidCache != null)
    {
        var faceMorphNames = new HashSet<string>();
        foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
        {
            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
            {
                faceMorphNames.Add(def.name);
            }
        }

        state.shapeKeys = new List<ScenePresetMorph>();
        var slotCount = Mathf.Min((int)TBody.SlotID.end, maid.body0.goSlot.Count);
        var seenTags = new HashSet<string>();
        for (var i = 0; i < slotCount; i++)
        {
            var slot = maid.body0.GetSlot(i);
            if (slot == null || slot.morph == null || slot.morph.hash.Count == 0) continue;
            foreach (var tag in slot.morph.GetTags())
            {
                if (faceMorphNames.Contains(tag) || !seenTags.Add(tag)) continue;
                var value = maidCache.GetBlendShapeValue(tag);
                if (Mathf.Approximately(value, 0f)) continue;
                state.shapeKeys.Add(new ScenePresetMorph { name = tag, value = value });
            }
        }

        // スロットマテリアル: 初期値と異なるプロパティを持つものだけ控える
        maidCache.UpdateMaterials();
        state.materials = new List<ScenePresetMaterial>();
        foreach (var slotStat in maidCache.slotStats)
        {
            var slotMaterials = slotStat.materials;
            for (var m = 0; m < slotMaterials.Count; m++)
            {
                var data = CaptureMaterial(slotMaterials[m], slotStat.name, m);
                if (data != null)
                {
                    state.materials.Add(data);
                }
            }
        }
    }
}
catch (Exception e)
{
    PluginUtils.LogError("シェイプキー/マテリアルの保存に失敗しました: {0}", maid.name);
    PluginUtils.LogException(e);
}
```

**実装時の注意:** `CaptureMaid` 内の既存 try-catch とログ関数の流儀（`MTEUtils.LogError` か `PluginUtils.LogError` か）は周辺コードに合わせる。`ScenePresetMorph.value` のフィールド名は実物（`ScenePresetData.cs:355-361`）に合わせる。

- [ ] **Step 4: ApplyMaid へシェイプキーとマテリアルの適用を追加**

`ApplyMaid`（1623-1743）の `ApplyUndress`（1697 行）より**後**（`ApplyBoneEdits` 1721 行の並び）に、既存の try-catch 個別握り潰しパターンで追加:

```csharp
try
{
    ApplyShapeKeys(maid, state);
}
catch (Exception e)
{
    PluginUtils.LogError("シェイプキーの適用に失敗しました: {0}", maid.name);
    PluginUtils.LogException(e);
}

try
{
    ApplyMaidMaterials(maid, state);
}
catch (Exception e)
{
    PluginUtils.LogError("マテリアルの適用に失敗しました: {0}", maid.name);
    PluginUtils.LogException(e);
}
```

適用メソッド本体（`ApplyFace` 付近に追加）:

```csharp
/// <summary>
/// 保存された任意シェイプキーを適用する。未保存タグのゼロ化は行わない
/// (表情モーフと同じ TMorph を共有するため、ApplyFace の結果を踏まないようにする)
/// </summary>
private static void ApplyShapeKeys(Maid maid, ScenePresetMaid state)
{
    if (state.shapeKeys == null || state.shapeKeys.Count == 0)
    {
        return;
    }

    var maidCache = MTEP.MaidManager.instance.GetMaidCache(maid);
    if (maidCache == null)
    {
        return;
    }

    // 着替え直後に古い TMorph を掴まないようキャッシュを破棄してから書く
    maidCache.ClearBlendShapeCache();

    var appliedTags = new List<string>(state.shapeKeys.Count);
    foreach (var shapeKey in state.shapeKeys)
    {
        maidCache.SetBlendShapeValue(shapeKey.name, shapeKey.value);
        appliedTags.Add(shapeKey.name);
    }
    // FixBlendValues は対象 TMorph を集約するため 1 回でまとめて呼ぶ
    maidCache.FixBlendValues(appliedTags);
}

/// <summary>保存されたスロットマテリアル差分を適用する。スロット/マテリアル名が一致しないものは飛ばす</summary>
private static void ApplyMaidMaterials(Maid maid, ScenePresetMaid state)
{
    if (state.materials == null || state.materials.Count == 0)
    {
        return;
    }

    var maidCache = MTEP.MaidManager.instance.GetMaidCache(maid);
    if (maidCache == null)
    {
        return;
    }

    // ApplyUndress で装備が出入りした後の状態でスロット一覧を作り直す
    maidCache.UpdateMaterials();

    foreach (var materialState in state.materials)
    {
        var slotStat = maidCache.slotStats.Find(s => s.name == materialState.owner);
        if (slotStat == null)
        {
            continue;
        }
        var material = FindMaterial(slotStat.materials, materialState);
        if (material == null)
        {
            continue;
        }
        ApplyMaterial(material, materialState);
    }
}
```

**実装時の注意:** `MaidSlotStat.name` と `displayName` のどちらをキーにするかは実物（`Timeline/MaidSlotStat.cs:6-50`）を確認し、Capture と Apply で必ず同じものを使う。`SetBlendShapeValue` は未知タグでも安全（entities 空なら no-op）だが、`GetBlendShapeValue` 側の実装（`MaidCache.cs:707-715`）が entities 空で例外を投げる場合は Capture 側を try-catch でなくガードで守る。

- [ ] **Step 5: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): メイドのシェイプキーとマテリアルをシーンプリセットへ保存・適用する"
```

---

### Task 4: シーンプリセットへモデル / 背景のシェイプキー・マテリアルを保存・適用

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`

**Interfaces:**
- Consumes: `ScenePresetMaterial` / `CaptureMaterial` / `ApplyMaterial`（Task 3）、`ProviderModelStat`（Task 2）、`ModelProviderHost.GetModels()`、`MTEP.StudioModelManager.instance.models`（シェイプキー逆引き）、`ScenePresetManager.FinishApply`（1415-1431）
- Produces: `ScenePresetData.modelShapeKeys / modelMaterials / bgMaterials`

- [ ] **Step 1: ScenePresetData ルートへフィールド追加**

`modelBoneEdits`（603-604）の直後に追加。モデル用のシェイプキー型も追加:

```csharp
/// <summary>モデルのシェイプキー 1 件。modelName + pluginName で ModelProviderHost のモデルと照合する</summary>
public class ScenePresetModelShapeKey
{
    [XmlAttribute("model")]
    public string modelName;

    [XmlAttribute("plugin")]
    public string pluginName;

    [XmlAttribute("name")]
    public string name;

    [XmlAttribute("value")]
    public float value;
}
```

```csharp
/// <summary>モデルのシェイプキー（非ゼロ値のみ）。null は旧バージョン</summary>
[XmlElement("modelShapeKey")]
public List<ScenePresetModelShapeKey> modelShapeKeys;

/// <summary>モデルのマテリアル差分（owner = "modelName|pluginName"）。null は旧バージョン</summary>
[XmlElement("modelMaterial")]
public List<ScenePresetMaterial> modelMaterials;

/// <summary>背景オブジェクトのマテリアル差分（owner = BG ルートからの相対パス）。null は旧バージョン</summary>
[XmlElement("bgMaterial")]
public List<ScenePresetMaterial> bgMaterials;
```

- [ ] **Step 2: Capture へモデル / 背景の収集を追加**

`Capture`（697-732）の `CaptureModelBoneEdits(data)`（729 行）直後に `CaptureModelAppearances(data, options);` を追加し、本体を `CaptureModelBoneEdits`（792-826）付近へ実装。

**保存カテゴリの取り扱い（既存規約に合わせる）:**
- モデル分（modelMaterials / modelShapeKeys）は `CaptureModelBoneEdits` と同じ規約: **`data.externals` が空（外部プロバイダ未選択でモデル自体が復元されない）なら保存しない**。`CaptureModelBoneEdits`（792-826）冒頭の判定コードをそのまま踏襲する
- 背景分（bgMaterials）は **`options.saveBackground` が false なら保存しない**（背景セクションと同じカテゴリに従う）

**AddComponent 副作用の回避:** 捕捉時は `GetOrCreate` を使わず、**既に `ModelMaterialController` が付いている GameObject だけ**を対象にする。差分が生じるのは編集された（= コントローラが生成済みの）マテリアルだけなので取り漏らしは起きず、プリセット保存のたびに全 BG レンダラーへコンポーネントが増える副作用も避けられる。

```csharp
/// <summary>モデル/背景のシェイプキーとマテリアル差分を控える。
/// 編集済み (= ModelMaterialController 生成済み) のものだけが対象</summary>
private static void CaptureModelAppearances(ScenePresetData data, ScenePresetSaveOptions options)
{
    ProviderModelStat.CleanupDestroyed();

    // モデル分は ModelBoneEdit と同じ規約: 復元先が無い孤立データを避けるため
    // 外部プロバイダの保存が無い場合はスキップする
    var hasExternals = data.externals != null && data.externals.Count > 0;
    if (hasExternals)
    {
        data.modelShapeKeys = new List<ScenePresetModelShapeKey>();
        data.modelMaterials = new List<ScenePresetMaterial>();

        var entries = ModelProviderHost.GetModels();

        // モデルマテリアル: 編集済みコントローラを持つモデルから差分を控える
        foreach (var entry in entries)
        {
            if (entry.obj == null) continue;
            var controller = entry.obj.GetComponentInChildren<MTEP.ModelMaterialController>(true);
            if (controller == null) continue;
            var owner = entry.obj.name + "|" + entry.pluginName;
            var materials = controller.materials;
            for (var m = 0; m < materials.Count; m++)
            {
                var materialData = CaptureMaterial(materials[m], owner, m);
                if (materialData != null)
                {
                    data.modelMaterials.Add(materialData);
                }
            }
        }

        // モデルシェイプキー: BlendShapeController を持つ StudioModelStat からのみ取れる。
        // 照合キーは ModelBoneEdit と同じ「ルート GameObject 名 + pluginName」に寄せる
        foreach (var model in MTEP.StudioModelManager.instance.models)
        {
            if (model == null || model.transform == null) continue;
            var entry = FindProviderEntry(entries, model.transform);
            if (entry == null) continue;
            foreach (var blendShape in model.blendShapes)
            {
                if (Mathf.Approximately(blendShape.weight, 0f)) continue;
                data.modelShapeKeys.Add(new ScenePresetModelShapeKey
                {
                    modelName = entry.obj.name,
                    pluginName = entry.pluginName,
                    name = blendShape.shapeKeyName,
                    value = blendShape.weight,
                });
            }
        }
    }

    // 背景分は背景セクションと同じカテゴリに従う
    if (options.saveBackground)
    {
        data.bgMaterials = new List<ScenePresetMaterial>();

        var bgObject = GameMain.Instance.BgMgr.BgObject;
        if (bgObject != null)
        {
            // GetComponentsInChildren で編集済みコントローラだけを直接拾う
            // (全 Renderer へ AddComponent する副作用を避ける)
            foreach (var controller in bgObject.GetComponentsInChildren<MTEP.ModelMaterialController>(true))
            {
                var owner = GetRelativePath(bgObject.transform, controller.transform);
                var materials = controller.materials;
                for (var m = 0; m < materials.Count; m++)
                {
                    var materialData = CaptureMaterial(materials[m], owner, m);
                    if (materialData != null)
                    {
                        data.bgMaterials.Add(materialData);
                    }
                }
            }
        }
    }
}

/// <summary>Transform が ModelProviderHost のどのモデル配下かを逆引きする（entries は呼び出し側で 1 回だけ取得）</summary>
private static ExternalModelEntry FindProviderEntry(List<ExternalModelEntry> entries, Transform transform)
{
    foreach (var entry in entries)
    {
        if (entry.obj == null) continue;
        if (transform == entry.obj.transform || transform.IsChildOf(entry.obj.transform))
        {
            return entry;
        }
    }
    return null;
}

/// <summary>root からの相対パス（"child/grandchild" 形式）。root 自身は空文字</summary>
private static string GetRelativePath(Transform root, Transform target)
{
    if (target == root) return "";
    var path = target.name;
    var current = target.parent;
    while (current != null && current != root)
    {
        path = current.name + "/" + path;
        current = current.parent;
    }
    return path;
}
```

- [ ] **Step 3: FinishApply へ適用を追加**

`FinishApply`（1415-1431）の `ApplyModelBoneEdits(data)`（1424 行）直後に追加（外部プロバイダのモデル復元が同期で終わった後でないと GameObject が無い、という理由も同じ）:

```csharp
ApplyModelAppearances(data);
```

本体を `ApplyModelBoneEdits`（1858-1909）付近へ実装:

```csharp
/// <summary>モデル/背景のシェイプキーとマテリアル差分を適用する。照合失敗は警告のみで飛ばす</summary>
private static void ApplyModelAppearances(ScenePresetData data)
{
    ProviderModelStat.CleanupDestroyed();

    // モデルマテリアル
    if (data.modelMaterials != null && data.modelMaterials.Count > 0)
    {
        var entries = ModelProviderHost.GetModels();
        foreach (var materialState in data.modelMaterials)
        {
            var separatorIndex = (materialState.owner ?? "").LastIndexOf('|');
            if (separatorIndex < 0) continue;
            var modelName = materialState.owner.Substring(0, separatorIndex);
            var pluginName = materialState.owner.Substring(separatorIndex + 1);

            // ModelBoneEdit と同じ照合 (同名複数は先勝ち)
            var entry = entries.Find(e => e.obj != null && e.obj.name == modelName && e.pluginName == pluginName);
            if (entry == null)
            {
                PluginUtils.LogWarning("マテリアル適用先のモデルが見つかりません: {0}", materialState.owner);
                continue;
            }

            var stat = ProviderModelStat.GetOrCreate(entry.obj, entry.displayName);
            var material = FindMaterial(stat.materials, materialState);
            if (material == null) continue;
            ApplyMaterial(material, materialState);
        }
    }

    // モデルシェイプキー: StudioModelStat を transform 一致で逆引きする
    if (data.modelShapeKeys != null && data.modelShapeKeys.Count > 0)
    {
        var entries = ModelProviderHost.GetModels();
        var models = MTEP.StudioModelManager.instance.models;
        var touchedModels = new HashSet<MTEP.StudioModelStat>();
        foreach (var shapeKeyState in data.modelShapeKeys)
        {
            var entry = entries.Find(e => e.obj != null
                && e.obj.name == shapeKeyState.modelName && e.pluginName == shapeKeyState.pluginName);
            if (entry == null) continue;
            var model = models.Find(m => m != null && m.transform != null
                && (m.transform == entry.obj.transform || m.transform.IsChildOf(entry.obj.transform)));
            if (model == null)
            {
                PluginUtils.LogWarning("シェイプキー適用先のモデルが見つかりません: {0}", shapeKeyState.modelName);
                continue;
            }
            var blendShape = model.blendShapes.Find(b => b.shapeKeyName == shapeKeyState.name);
            if (blendShape == null) continue;
            blendShape.weight = shapeKeyState.value;
            touchedModels.Add(model);
        }
        // FixBlendValues は頂点全走査で重いためモデルごとに 1 回
        foreach (var model in touchedModels)
        {
            model.FixBlendValues();
        }
    }

    // 背景マテリアル (背景を復元しない設定のときは触らない)
    if (data.bgMaterials != null && data.bgMaterials.Count > 0 && ShouldApplyBackground(data))
    {
        var bgObject = GameMain.Instance.BgMgr.BgObject;
        if (bgObject != null)
        {
            foreach (var materialState in data.bgMaterials)
            {
                var target = string.IsNullOrEmpty(materialState.owner)
                    ? bgObject.transform
                    : bgObject.transform.Find(materialState.owner);
                if (target == null)
                {
                    PluginUtils.LogWarning("背景マテリアル適用先が見つかりません: {0}", materialState.owner);
                    continue;
                }
                var stat = ProviderModelStat.GetOrCreate(target.gameObject, target.name);
                var material = FindMaterial(stat.materials, materialState);
                if (material == null) continue;
                ApplyMaterial(material, materialState);
            }
        }
    }
}
```

**実装時の注意:**
- `ExternalModelEntry` の型名と `ModelProviderHost.GetModels()` の戻り値型は `ModelProviderHost.cs:8-13, 76` の実物に合わせる
- `PluginUtils.LogWarning` のシグネチャは既存呼び出し（`ScenePresetManager.cs:1899` 付近の `LogWarning`）に合わせる
- モデルシェイプキーは timeline 未読み込みだと `StudioModelManager.models` が空で適用スキップになる（警告ログのみ）。これは仕様（シェイプキー対応モデルがタイムライン経由でしか存在しないため）

- [ ] **Step 4: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): モデル/背景のシェイプキーとマテリアルをシーンプリセットへ保存・適用する"
```

---

### Task 5: spec 更新と実機確認

**Files:**
- Modify: `docs/superpowers/specs/timeline-remaining-work.md`

- [ ] **Step 1: spec を更新**

- §3 のシェイプキー/マテリアル項目へ「タイムライン非依存版へ作り直し（2026-08-23）: 全シェイプキー操作可能、モデル/背景は ModelProviderHost / BG Renderer 走査、シーンプリセット保存対応（v21）」を追記
- §4 の将来課題表へ以下を追加:
  - 「モデルのシェイプキー編集はタイムライン経由で配置したモデルのみ（BlendShapeController が StudioModelStat 依存）」
  - 「タイムライン再生中はレイヤーの毎フレーム適用とスライダー編集が取り合いになる（ガード無しを選択）」
  - 「プリセットのシェイプキー適用は保存タグのみ設定（未保存タグはゼロ化しない）」

- [ ] **Step 2: 実機確認（devbridge が応答しない場合はチェックリストをユーザーへ提示して完了）**

1. `mcp__com3d25-devbridge__ping` → 応答なしなら手順提示で終了
2. **タイムライン未読み込み**でシェイプキー/マテリアルウィンドウを開き、メイドの全シェイプキーが表示・編集できること（旧版は「タイムラインが読み込まれていません」だった）
3. マテリアル: メイドスロット / ModelProviderHost モデル / 背景オブジェクトそれぞれで色・値変更が反映されること
4. シーンプリセット保存 → シェイプキー/マテリアルを変えてから適用 → 保存値へ戻ること（XML に shapeKey / material / modelMaterial / bgMaterial 要素が出ていること）
5. 旧バージョン（v20 以前）のプリセットを読み込んでもエラーが出ないこと（新セクション null スキップ）
6. タイムライン読み込み済みでもレイヤー編集ウィンドウ（ShapeKey レイヤーの登録トグル）が従来どおり動くこと

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/timeline-remaining-work.md
git commit -m "docs(timeline): シェイプキー/マテリアルのタイムライン非依存化とプリセット対応を記録"
```

---

## Self-Review 結果

- **Spec coverage**: ユーザー指示 3 点（タイムライン非依存 / 全シェイプキー / シーンプリセット反映）をそれぞれ Task 1-2 / Task 1 / Task 3-4 が実装。レイヤー編集への純化（登録トグル削除）も Task 1 でカバー
- **Placeholder scan**: なし。API 実在が未確認の箇所（`BgMgr.BgObject` のプロパティ名、`MaidSlotStat.name` vs `displayName`、`ScenePresetMorph` のフィールド名、ログ関数名）は「実装時の注意」で実物への突き合わせ先を明示
- **Type consistency**: `ProviderModelStat.GetOrCreate(GameObject, string)` / `CaptureMaterial(material, owner, index)` / `ApplyMaterial` / `FindMaterial` / `ClearBlendShapeCache` の名前は Task 間で一致。owner のフォーマット（メイド=スロット名、モデル="name|pluginName"、背景=相対パス）は Capture / Apply で対応

## plan-review 反映メモ（2026-08-23）

**取り込み（🔴2 件 + 🟡3 件）:**
- 保存カテゴリ準拠: bgMaterials は `options.saveBackground` / `ShouldApplyBackground`、モデル分は `CaptureModelBoneEdits` と同じ「externals 空ならスキップ」規約に統一
- 色の自前文字列シリアライズ（カルチャ依存）→ float 4 属性（r/g/b/a）へ変更
- 捕捉時の AddComponent 副作用 → `GetComponent(sInChildren)` で既存コントローラのみ対象に変更（差分は編集済みマテリアルにしか生じないため取り漏らしなし）
- `FindProviderEntry` の O(n²) → entries をループ外で 1 回取得して引数渡し
- 同名マテリアルの誤同定 → `index` 属性 + `FindMaterial`（インデックス優先の名前一致）

**却下:**
- メイドシェイプキー捕捉の O(スロット×タグ) コスト — 却下: `GetBlendShape` はメモ化されており保存時 1 回きりの走査。保存操作の一括コストとして許容
- modelShapeKeys の YAGNI（タイムライン経由モデルのみ有効） — 却下: SE のモデル配置は現状タイムライン経由が主経路であり、その主経路で機能する。制約は spec の将来課題に記録する（Task 5）
