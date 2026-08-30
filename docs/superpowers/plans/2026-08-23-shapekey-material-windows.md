# シェイプキー / マテリアル編集ウィンドウ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BoneEditWindow を雛形に、シェイプキー編集ウィンドウ（メイド任意 blendshape / モデル）とマテリアル編集ウィンドウ（メイド / モデル / 背景モデルの 3 系統）を SE ネイティブの EditorSubWindow として追加する。

**Architecture:** 既存タイムラインデータ層（MaidCache / StudioModelStat / BGModelStat / ModelMaterial）をそのまま使い、UI だけを新設する。編集 UI の中身は既存 4 レイヤー（ShapeKeyTimelineLayer / ModelShapeKeyTimelineLayer / MaidMaterialTimelineLayer 他 2 系統）の DrawWindow を移植し、対象種別タブ（BoneEditWindow の メイド/モデル タブと同型）で切り替える。キーフレーム化は既存の AddKeyFrameAll 経路（Shift+Return / TimelineWindow ボタン）に委ね、ウィンドウからは値を直接書くだけとする（BoneEditWindow / 既存レイヤーと同じ設計）。

**Tech Stack:** C# (.NET Framework, Unity IMGUI / GUIView), 非 SDK 形式 csproj, MSBuild

**Spec:** `docs/superpowers/specs/timeline-remaining-work.md` §3「Phase W3-旧」のうち「マテリアル編集 3 系統」「シェイプキー編集」。関連: `docs/superpowers/specs/timeline-editing-roadmap.md:182-183`

## 設計判断（計画時に確定）

- **専用ウィンドウ 2 枚を新設する。** ロードマップ当初案（MaidFaceWindow へタブ追加 / BoneEditWindow へ統合 / InspectorWindow 統合）ではなく、ユーザー指示「ボーンウィンドウ参考に追加して」に従い独立ウィンドウとする。
- **タイムラインデータ層に依存する**ため、`MTEP.TimelineManager.instance.timeline == null` のときは TimelineLayerWindow と同じ黄色ラベル「タイムラインが読み込まれていません」を出して打ち切る（`TimelineLayerWindow.cs:112-116` と同型）。
- **編集可否ガードは既存レイヤーと同一**: `view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing)`。MTE 逐語移植方針を維持し、再生中の値の取り合いを避ける。
- **Undo（SE HistoryManager）対応はスコープ外。** HistoryScope にマテリアル/シェイプキー用スコープが無く、Snapshot 実装が必要になるため将来課題とする（spec の将来課題リストに追記する）。タイムライン側 Undo は既存 `AddKeyFrameAll` 内の `timelineManager.RequestHistory` で従来どおり効く。
- **MaidFaceWindow との相互排他は本計画では扱わない**（spec §4 で既に将来課題として明記済み）。
- **ColorFieldCache は既存レイヤーと同じく空文字ラベルで取得する。** `GUIView.GetColorFieldCache(label, hasAlpha)` は呼び出し順でプールする位置ベースのキャッシュで、`label` はキーではなく `DrawColor` 内部で描画されるラベル文字列（`GUIView.cs:2477-2499`）。プロパティ名を渡すと外側の `DrawLabel` と二重描画になるため、空文字を渡し、プロパティ名は外側の `DrawLabel` だけで出す。

## Global Constraints

- 新ウィンドウの namespace は `COM3D2.SceneEditor.Plugin`。タイムライン型は `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` エイリアスで参照する（`TimelineLayerWindow.cs:4` の流儀）
- WINDOW_ID: ShapeKeyEditWindow = **8903389**、MaterialEditWindow = **8903390**（現最大 8903388 の続番）
- csproj は非 SDK 形式。新規 .cs は `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` へ `<Compile Include="..." />` を手動追加（漏れるとビルドに入らない）
- `WindowManager.Init()` での `AddWindow` は必ず `ComboBoxPopupWindow` / `ColorPickerWindow` の登録（`Manager/WindowManager.cs:71-75`）より前に置く（登録順 = 描画順）
- GUIView の `BeginScrollView` はネスト不可（1 段だけ）
- コメント・ログは日本語
- ビルド確認は MSBuild 直叩き（debug.bat はゲームフォルダへ DLL をコピーするため使わない）:
  ```
  MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
  （環境によりプロパティ名が異なる場合は直近ビルドで使ったコマンドに合わせる。deploy.bat / release.bat は実行禁止）

## 参照実装マップ（実装者は必ず先に読むこと）

| 参照元 | 何の雛形か |
|---|---|
| `BoneEditWindow.cs:15-74, 159-190, 201-330` | ウィンドウ骨格・対象種別タブ・配置/可視の保存・シングルトン |
| `TimelineLayerWindow.cs:99-121` | timeline null ガード、ComboBoxPopupWindow.ProcessFocus の呼び方 |
| `Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs:131-284` | メイドシェイプキーの追加/操作タブ UI |
| `Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs:181-283` | モデルシェイプキーの操作 UI |
| `Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs:142-283` | メイドマテリアル UI（スロット/マテリアルコンボ + 色/値ループ） |
| `Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs:163-308` / `BGModelMaterialTimelineLayer.cs:147-300` | モデル / 背景モデルのマテリアル UI |
| `TimelineTemplateWindow.cs` + コミット `fbfe88b` / `b912910` | 直近のウィンドウ追加・配線の実例 |

---

### Task 1: ShapeKeyEditWindow 本体

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`timelineTemplate` ブロック 222-227 の直後に追記）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`TimelineTemplateWindow.cs` の Compile 行 286 付近に追加）

**Interfaces:**
- Consumes: `MaidWindowBase`（`DrawMaidContent(Maid)` / `DrawMaidSelector` / `DrawInnerTabs` / `CalcLabeledComboWidth`）、`MTEP.MaidManager.instance.GetMaidCache(Maid)`、`MTEP.MaidCache.GetBlendShape(string)/FixBlendValues(IEnumerable<string>)`、`MTEP.TimelineManager.instance.timeline` の `HasMaidShapeKey/AddMaidShapeKey/RemoveMaidShapeKey/GetMaidShapeKeys(int)`、`MTEP.StudioModelManager.instance.models`、`MTEP.StudioModelStat.blendShapes/FixBlendValues()`、`MTEP.StudioHackManager.instance.isPoseEditing`
- Produces: `ShapeKeyEditWindow.instance`（Task 2 の配線が参照）、`ShapeKeyEditWindow.WINDOW_ID = 8903389`

- [ ] **Step 1: Config にフィールド追加**

`Config.cs` の timelineTemplate ブロック（222-227）直後:

```csharp
// シェイプキー編集ウィンドウ
public int shapeKeyEditPosX = -1;
public int shapeKeyEditPosY = -1;
public int shapeKeyEditWidth = 400;
public int shapeKeyEditHeight = 480;
public bool shapeKeyEditVisible = false;
```

- [ ] **Step 2: ShapeKeyEditWindow.cs を作成**

骨格は BoneEditWindow と同じ MaidWindowBase 派生 + 対象種別タブ。編集 UI は ShapeKeyTimelineLayer / ModelShapeKeyTimelineLayer の DrawWindow を移植する。

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シェイプキー編集ウィンドウ。
    /// メイドの任意 blendshape とモデルのシェイプキーを直接編集する。
    /// キーフレーム化は TimelineWindow のキーフレーム全登録に委ねる
    /// </summary>
    public class ShapeKeyEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903389;

        private enum TargetTabType
        {
            メイド,
            モデル,
        }

        private enum MaidTabType
        {
            操作,
            追加,
        }

        private TargetTabType _targetTab = TargetTabType.メイド;
        private MaidTabType _maidTab = MaidTabType.操作;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.MaidManager timelineMaidManager => MTEP.MaidManager.instance;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;

        // 追加タブのスロット選択 (ShapeKeyTimelineLayer.cs:131-136 と同型)
        private readonly GUIComboBox<string> _slotNameComboBox = new GUIComboBox<string>
        {
            getName = (slotName, index) => slotName,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<MTEP.StudioModelStat> _modelComboBox
            = new GUIComboBox<MTEP.StudioModelStat>
        {
            getName = (model, index) => model.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private static ShapeKeyEditWindow _instance;
        public static ShapeKeyEditWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ShapeKeyEditWindow();
                }
                return _instance;
            }
        }

        private ShapeKeyEditWindow()
        {
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "シェイプキー";
        protected override int minWidth => 300;
        protected override int minHeight => 300;

        // 対象種別タブをメイド選択行より上に出すため自前で描く (BoneEditWindow.cs:74 と同じ)
        protected override bool showMaidSelector => false;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.shapeKeyEditPosX;
            y = config.shapeKeyEditPosY;
            width = config.shapeKeyEditWidth;
            height = config.shapeKeyEditHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.shapeKeyEditPosX = x;
            config.shapeKeyEditPosY = y;
            config.shapeKeyEditWidth = width;
            config.shapeKeyEditHeight = height;
        }

        public override bool savedVisible
        {
            get => config.shapeKeyEditVisible;
            set => config.shapeKeyEditVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            // シェイプキー編集はタイムラインのデータ層 (MaidCache 等) に依存する
            if (timeline == null)
            {
                view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _targetTab = DrawInnerTabs(_targetTab, 70);
            view.AddSpace(4);

            if (_targetTab == TargetTabType.モデル)
            {
                DrawModelContent();
                return;
            }

            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            var maidCache = timelineMaidManager.GetMaidCache(target);
            if (maidCache == null)
            {
                view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _maidTab = DrawInnerTabs(_maidTab, 50);
            view.AddSpace(4);

            if (_maidTab == MaidTabType.追加)
            {
                DrawMaidShapeKeyAdd(target, maidCache);
            }
            else
            {
                DrawMaidShapeKeyEdit(maidCache);
            }
        }

        // ShapeKeyTimelineLayer.DrawBlendShapesAdd (175-231) の移植。
        // タイムラインへの登録トグル (登録キーは操作タブとレイヤー編集の両方で使われる)
        private void DrawMaidShapeKeyAdd(Maid target, MTEP.MaidCache maidCache)
        {
            var maidSlotNo = maidCache.slotNo;

            // morph を持つスロット名を収集 (goSlot は列挙不可のためインデックス走査)。
            // 上限は参照実装 (ShapeKeyTimelineLayer.cs:184) と同じく SlotID.end で切る
            var slotNames = new List<string>();
            var slotCount = Mathf.Min((int)TBody.SlotID.end, target.body0.goSlot.Count);
            for (var i = 0; i < slotCount; i++)
            {
                var slot = target.body0.GetSlot(i);
                if (slot != null && slot.morph != null && slot.morph.hash.Count > 0)
                {
                    slotNames.Add(slot.Category.ToString());
                }
            }

            _slotNameComboBox.items = slotNames;
            view.DrawLabel("スロット", LABEL_WIDTH, ROW_HEIGHT);
            _slotNameComboBox.DrawButton(view);

            // 参照実装 (ShapeKeyTimelineLayer.cs:200-206) と同じく IsSlotNo で存在確認してから取得する
            var slotName = _slotNameComboBox.currentItem;
            if (string.IsNullOrEmpty(slotName) || !target.body0.IsSlotNo(slotName))
            {
                return;
            }
            var morph = target.body0.GetSlot(slotName).morph;
            if (morph == null)
            {
                return;
            }

            var tags = morph.GetTags().OrderBy(tag => tag, System.StringComparer.Ordinal).ToList();

            view.SetEnabled(view.focusedComboBox == null);
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var tag in tags)
            {
                var enable = timeline.HasMaidShapeKey(maidSlotNo, tag);
                view.DrawToggle(tag, enable, -1, ROW_HEIGHT, newValue =>
                {
                    if (newValue)
                    {
                        timeline.AddMaidShapeKey(maidSlotNo, tag);
                    }
                    else
                    {
                        timeline.RemoveMaidShapeKey(maidSlotNo, tag);
                    }
                });
            }

            view.EndScrollView();
        }

        // ShapeKeyTimelineLayer.DrawBlendShapesEdit (237-284) の移植
        private void DrawMaidShapeKeyEdit(MTEP.MaidCache maidCache)
        {
            var maidSlotNo = maidCache.slotNo;
            var shapeKeys = timeline.GetMaidShapeKeys(maidSlotNo);
            if (shapeKeys.Count == 0)
            {
                view.DrawLabel("追加タブでシェイプキーを登録してください", -1, ROW_HEIGHT);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var shapeKey in shapeKeys.OrderBy(x => x, System.StringComparer.Ordinal))
            {
                var blendShape = maidCache.GetBlendShape(shapeKey);
                if (blendShape == null)
                {
                    continue;
                }

                var weight = blendShape.weight;
                var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = shapeKey,
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
                    maidCache.FixBlendValues(new[] { shapeKey });
                }
            }

            view.EndScrollView();
        }

        // ModelShapeKeyTimelineLayer.DrawBlendShapes (218-283) の移植
        private void DrawModelContent()
        {
            var models = modelManager.models;
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

            var blendShapes = model.blendShapes;
            if (blendShapes.Count == 0)
            {
                view.DrawLabel("シェイプキーを持たないモデルです", -1, ROW_HEIGHT);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var blendShape in blendShapes)
            {
                var weight = blendShape.weight;
                var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = blendShape.shapeKeyName,
                    labelWidth = 100,
                    min = -1f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = weight,
                    onChanged = x => weight = x,
                });

                // FixBlendValues は頂点全走査で重いため変更時のみ呼ぶ
                // (参照実装 ModelShapeKeyTimelineLayer.cs:255-279 と同じ)
                if (updateTransform)
                {
                    blendShape.weight = weight;
                    model.FixBlendValues();
                }
            }

            view.EndScrollView();
        }
    }
}
```

**実装時の注意（コードとの突き合わせ必須）:**
- `GUIView.SliderOption` に `label` / `labelWidth` が無い場合は、既存レイヤー（`ShapeKeyTimelineLayer.cs:258-274`）と同様に `view.DrawLabel(shapeKey, ...)` を別行で描いてから option なしの `DrawSliderValue` を呼ぶ形へ合わせる。**必ず既存レイヤーの実際の呼び方をコピーする**
- `DrawSliderValue` の戻り値が bool でない場合も同様に既存レイヤーの `updateTransform` 判定の書き方に合わせる
- スロット名収集は `ShapeKeyTimelineLayer.cs:182-193` の実装をそのまま使う（`GetSlot(int)` / `Category` のシグネチャは COM3D2.5 の 2 次元 goSlot 対応済みコードが正）
- `MaidCache.slotNo` が public でない場合は `timelineMaidManager` から slotNo を引く既存経路（`TimelineLayerBase.cs:26` 参照）に合わせる

- [ ] **Step 3: csproj に Compile 追加**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="TimelineTemplateWindow.cs" />`（286 行付近）の近くに:

```xml
<Compile Include="ShapeKeyEditWindow.cs" />
```

- [ ] **Step 4: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded、エラー 0

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): シェイプキー編集ウィンドウを追加"
```

---

### Task 2: ShapeKeyEditWindow の配線

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:44-70`（`Init()`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:145-154`（メイドメニュー）

**Interfaces:**
- Consumes: `ShapeKeyEditWindow.instance`（Task 1）、`WindowManager.AddWindow(EditorSubWindow)`、`MenuBarWindow.CreateWindowItem(string, EditorSubWindow)`
- Produces: メニューバー「メイド > シェイプキー」から表示できるウィンドウ

- [ ] **Step 1: WindowManager に登録**

`Manager/WindowManager.cs` の `Init()`、`AddWindow(BoneEditWindow.instance);`（57 行）の直後に追加。**ComboBoxPopupWindow / ColorPickerWindow の AddWindow（71-75）より前であること**:

```csharp
AddWindow(ShapeKeyEditWindow.instance);
```

- [ ] **Step 2: メニューバーに導線追加**

`MenuBarWindow.cs` の「メイド」メニュー内、`CreateWindowItem("ボーン", BoneEditWindow.instance)`（153 行）の直後に:

```csharp
CreateWindowItem("シェイプキー", ShapeKeyEditWindow.instance),
```

（モデルも対象にするが、主対象がメイドでボーンウィンドウと並ぶため同じメニューに置く）

- [ ] **Step 3: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs
git commit -m "feat(timeline): シェイプキー編集ウィンドウを配線"
```

---

### Task 3: MaterialEditWindow 本体

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（Task 1 で追加した shapeKeyEdit ブロックの直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `MaidWindowBase`、`MTEP.MaidManager.instance.GetMaidCache(Maid)`、`MTEP.MaidCache.slotStats/UpdateMaterials()`、`MTEP.MaidSlotStat.materials`、`MTEP.StudioModelManager.instance.models` / `MTEP.StudioModelStat.materials`、`MTEP.BGModelManager.instance.models` / `MTEP.BGModelStat.materials`、`MTEP.ModelMaterial`（`ColorPropertyTypes/ValuePropertyTypes/HasColor/GetColor/SetColor/GetInitialColor/HasValue/GetValue/SetValue/GetInitialValue/Reset`）、`MTEP.TransformDataModelMaterial.defaultTrans.GetCustomValueInfo(...)`
- Produces: `MaterialEditWindow.instance`、`MaterialEditWindow.WINDOW_ID = 8903390`

- [ ] **Step 1: Config にフィールド追加**

```csharp
// マテリアル編集ウィンドウ
public int materialEditPosX = -1;
public int materialEditPosY = -1;
public int materialEditWidth = 400;
public int materialEditHeight = 480;
public bool materialEditVisible = false;
```

- [ ] **Step 2: MaterialEditWindow.cs を作成**

対象種別タブ（メイド/モデル/背景）で対象を選び、マテリアル 1 件の色/値プロパティを編集する。色/値ループは 3 系統で完全に共通なので `DrawMaterialProperties` に一本化する（既存 3 レイヤーでは同一コードが 3 回重複している）。

```csharp
using UnityEngine;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル編集ウィンドウ。
    /// メイドスロット / 配置モデル / 背景モデルのマテリアル色・数値プロパティを直接編集する。
    /// キーフレーム化は TimelineWindow のキーフレーム全登録に委ねる
    /// </summary>
    public class MaterialEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903390;

        private enum TargetTabType
        {
            メイド,
            モデル,
            背景,
        }

        private TargetTabType _targetTab = TargetTabType.メイド;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.MaidManager timelineMaidManager => MTEP.MaidManager.instance;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;

        private readonly GUIComboBox<MTEP.MaidSlotStat> _slotComboBox = new GUIComboBox<MTEP.MaidSlotStat>
        {
            getName = (slot, index) => slot.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private readonly GUIComboBox<MTEP.StudioModelStat> _modelComboBox = new GUIComboBox<MTEP.StudioModelStat>
        {
            getName = (model, index) => model.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private readonly GUIComboBox<MTEP.BGModelStat> _bgModelComboBox = new GUIComboBox<MTEP.BGModelStat>
        {
            getName = (model, index) => model.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private readonly GUIComboBox<MTEP.ModelMaterial> _materialComboBox = new GUIComboBox<MTEP.ModelMaterial>
        {
            getName = (material, index) => material.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private static MaterialEditWindow _instance;
        public static MaterialEditWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaterialEditWindow();
                }
                return _instance;
            }
        }

        private MaterialEditWindow()
        {
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "マテリアル";
        protected override int minWidth => 300;
        protected override int minHeight => 300;
        protected override bool showMaidSelector => false;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.materialEditPosX;
            y = config.materialEditPosY;
            width = config.materialEditWidth;
            height = config.materialEditHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.materialEditPosX = x;
            config.materialEditPosY = y;
            config.materialEditWidth = width;
            config.materialEditHeight = height;
        }

        public override bool savedVisible
        {
            get => config.materialEditVisible;
            set => config.materialEditVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            // マテリアル走査はタイムラインのデータ層 (ModelMaterialController 等) に依存する
            if (timeline == null)
            {
                view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _targetTab = DrawInnerTabs(_targetTab, 70);
            view.AddSpace(4);

            switch (_targetTab)
            {
                case TargetTabType.メイド:
                    DrawMaidMaterial(target);
                    break;
                case TargetTabType.モデル:
                    DrawModelMaterial();
                    break;
                case TargetTabType.背景:
                    DrawBGModelMaterial();
                    break;
            }
        }

        // MaidMaterialTimelineLayer.DrawMaterial (167-283) の移植
        private void DrawMaidMaterial(Maid target)
        {
            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            var maidCache = timelineMaidManager.GetMaidCache(target);
            if (maidCache == null)
            {
                view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _slotComboBox.items = maidCache.slotStats;
            view.BeginHorizontal();
            view.DrawLabel("スロット", LABEL_WIDTH, ROW_HEIGHT);
            _slotComboBox.DrawButton(view);
            if (view.DrawButton("更新", 50, ROW_HEIGHT))
            {
                maidCache.UpdateMaterials();
            }
            view.EndLayout();

            var slot = _slotComboBox.currentItem;
            if (slot == null)
            {
                return;
            }

            DrawMaterialSelector(slot.materials);
        }

        // ModelMaterialTimelineLayer.DrawMaterial (207-308) の移植
        private void DrawModelMaterial()
        {
            var models = modelManager.models;
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

        // BGModelMaterialTimelineLayer.DrawMaterial (191-300) の移植
        private void DrawBGModelMaterial()
        {
            var models = bgModelManager.models;
            if (models.Count == 0)
            {
                view.DrawLabel("背景モデルがありません", -1, ROW_HEIGHT);
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

        private void DrawMaterialSelector(System.Collections.Generic.List<MTEP.ModelMaterial> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                view.DrawLabel("マテリアルがありません", -1, ROW_HEIGHT);
                return;
            }

            _materialComboBox.items = materials;
            view.DrawLabel("マテリアル", LABEL_WIDTH, ROW_HEIGHT);
            _materialComboBox.DrawButton(view);

            var material = _materialComboBox.currentItem;
            if (material == null)
            {
                return;
            }

            DrawMaterialProperties(material);
        }

        // 3 レイヤー共通の色/値ループ (MaidMaterialTimelineLayer.cs:216-278 の移植)
        private void DrawMaterialProperties(MTEP.ModelMaterial material)
        {
            var defaultTrans = MTEP.TransformDataModelMaterial.defaultTrans;

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            if (view.DrawButton("初期化", 80, ROW_HEIGHT))
            {
                material.Reset();
            }

            foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
            {
                if (!material.HasColor(propertyType))
                {
                    continue;
                }

                var color = material.GetColor(propertyType);
                var initialColor = material.GetInitialColor(propertyType);
                // 既存レイヤーは空文字キーだが、複数プロパティ間の cache 競合を避けるためプロパティ名をキーにする
                // 空文字ラベル必須: GetColorFieldCache は位置ベースのプールで、
                // ここに名前を渡すと DrawColor 内部でも描かれて二重表示になる
                var cache = view.GetColorFieldCache("", true);
                view.DrawLabel(propertyType.ToString(), 200, ROW_HEIGHT);
                view.DrawColor(cache, color, initialColor,
                    newColor => material.SetColor(propertyType, newColor));
            }

            foreach (var propertyType in MTEP.ModelMaterial.ValuePropertyTypes)
            {
                if (!material.HasValue(propertyType))
                {
                    continue;
                }

                var value = material.GetValue(propertyType);
                var initialValue = material.GetInitialValue(propertyType);
                var info = defaultTrans.GetCustomValueInfo(propertyType);

                // _OutlineWidth は値が極小のため桁数を増やす (既存 3 レイヤーと同じ分岐)
                var fieldType = propertyType == MTEP.ModelMaterial.ValuePropertyType._OutlineWidth
                    ? FloatFieldType.F4
                    : FloatFieldType.Float;

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = info.name,
                    labelWidth = 100,
                    fieldType = fieldType,
                    min = info.min,
                    max = info.max,
                    step = info.step,
                    defaultValue = initialValue,
                    value = value,
                    onChanged = x => material.SetValue(propertyType, x),
                });
            }

            view.EndScrollView();
        }
    }
}
```

**実装時の注意:**
- `GUIView.SliderOption` のメンバー名（`label` / `labelWidth` / `fieldType`）と `GetCustomValueInfo` の引数型（enum 直接か文字列か）は `MaidMaterialTimelineLayer.cs:253-278` の実物に必ず合わせる。ラベルを option で渡せない場合は `view.DrawLabel(info.name, ...)` を別行にする
- `FloatFieldType` の namespace（MTEP 側なら `MTEP.FloatFieldType`）は既存レイヤーの using に合わせる
- `TransformDataModelMaterial.defaultTrans` の型・アクセスは `MaidMaterialTimelineLayer.cs:216` の実物に合わせる

- [ ] **Step 3: csproj に Compile 追加**

```xml
<Compile Include="MaterialEditWindow.cs" />
```

- [ ] **Step 4: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): マテリアル編集ウィンドウを追加"
```

---

### Task 4: MaterialEditWindow の配線

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`

**Interfaces:**
- Consumes: `MaterialEditWindow.instance`（Task 3）
- Produces: メニューバー「メイド > マテリアル」からの表示導線

- [ ] **Step 1: WindowManager に登録**

Task 2 で追加した `AddWindow(ShapeKeyEditWindow.instance);` の直後に:

```csharp
AddWindow(MaterialEditWindow.instance);
```

- [ ] **Step 2: メニューバーに導線追加**

Task 2 で追加した「シェイプキー」項目の直後に:

```csharp
CreateWindowItem("マテリアル", MaterialEditWindow.instance),
```

- [ ] **Step 3: ビルド確認**

Run: Global Constraints の MSBuild コマンド
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs
git commit -m "feat(timeline): マテリアル編集ウィンドウを配線"
```

---

### Task 5: spec 更新と実機確認

**Files:**
- Modify: `docs/superpowers/specs/timeline-remaining-work.md`（§3 の該当項目を実装済みへ更新、§4 に Undo 未対応を追記）

**Interfaces:**
- Consumes: Task 1〜4 の成果
- Produces: 更新済み spec、実機確認結果

- [ ] **Step 1: spec を更新**

`timeline-remaining-work.md` §3 の「マテリアル編集 3 系統（メイド / モデル / 背景モデル）、シェイプキー編集（メイド任意 blendshape / モデル）」に実装済みの注記（日付・ウィンドウ名・導線）を追加。§4 の表へ「シェイプキー / マテリアル編集ウィンドウの SE HistoryManager (Undo) 未対応」を出典「本計画の設計判断」として追記。

- [ ] **Step 2: 実機確認（ゲーム起動中のみ。devbridge が使えない場合は手順をユーザーへ提示して終了）**

1. `mcp__com3d25-devbridge__ping` で死活確認。応答がなければ「ゲーム再起動後に以下を確認してください」として下記チェックリストを提示して完了とする
2. タイムライン読み込み済みの状態で、メニューバー「メイド > シェイプキー」「メイド > マテリアル」からウィンドウを開き screenshot で表示確認
3. シェイプキー: 追加タブでタグ登録 → 操作タブでスライダー変更 → 実機の見た目に反映されること（`eval_csharp` で `maidCache.GetBlendShapeValue` を読む）
4. マテリアル: メイド/モデル/背景の各タブで色・値スライダー変更が反映されること、「初期化」で戻ること
5. タイムライン未読み込み時に黄色ラベルが出て操作できないこと
6. `Shift+Return`（AddKeyFrameAll）で編集値がキーフレーム化されること

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/timeline-remaining-work.md
git commit -m "docs(timeline): シェイプキー/マテリアルウィンドウの実装を記録"
```

---

## Self-Review 結果

- **Spec coverage**: 対象は spec §3「マテリアル編集 3 系統 / シェイプキー編集」のみ。演出系・ポストエフェクト・W4 小物は本計画のスコープ外（ユーザー指示の 2 ウィンドウに限定)
- **Placeholder scan**: コードは参照実装の移植で全量記載。GUIView API のシグネチャ差異のみ「既存レイヤーの実物に合わせる」と明示（実装時の突き合わせ指示であり TBD ではない）
- **Type consistency**: `ShapeKeyEditWindow.instance` / `MaterialEditWindow.instance` / WINDOW_ID 8903389・8903390 / Config フィールド名（shapeKeyEdit* / materialEdit*）は全タスクで一致

## レビュー却下メモ（plan-review 2026-08-23）

- Undo 非対応の非対称性をユーザーへ示す UI/ログの確認観点追加 — 却下: Undo 非対応は設計判断に明記済みのスコープ外。表示追加は受け皿実装が必要で本計画の範囲を超える（将来課題として spec に記録する Task 5 で足りる）
