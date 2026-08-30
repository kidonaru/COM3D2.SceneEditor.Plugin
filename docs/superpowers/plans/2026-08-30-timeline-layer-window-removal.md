# レイヤー編集ウィンドウ (TimelineLayerWindow) 撤去 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** レイヤー編集ウィンドウにしか残っていない未代替機能(管理系 8 件・編集系 3 件)を個別ウィンドウ / ItemInspector へ移設し、TimelineLayerWindow と各レイヤーの `DrawWindow` 系 UI を完全撤去する。

**Architecture:** 「数の増減・全体トグル」は TimelineSettingWindow、「モデル/背景モデル/ライトの一覧管理」は BackgroundWindow / LightWindow / ItemInspector、「項目ごとの補助操作(コピー・初期化・プレビュー)」は各 ItemInspector / RowDrawer に寄せる。移設完了後に `ITimelineLayer.DrawWindow` / `ResetDraw` ごと宣言を削除し、TimelineLayerWindow・Config・メニュー導線を撤去する。

**Tech Stack:** C# (.NET Framework / Unity IMGUI 風 GUIView)。UI コードはユニットテスト対象外のため、検証は両 GameVersion のビルド成功とグレップによる参照残り確認。

**Spec:** `docs/timeline-layer-window-removal-survey.md`(未代替機能 #1〜#11 の全数調査)

## Global Constraints

- ビルドは **COM3D2 / COM3D25 の両方**を必ず通すこと(対象フレームワークが異なる)。`debug.bat` はゲームフォルダへ DLL をコピーするため使わず、MSBuild を直接叩く:
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- コードコメント・エラーログは日本語。
- レイヤー実装は `COM3D2.MotionTimelineEditor.Plugin` 名前空間(MTEP)、SE のウィンドウ / Inspector は `COM3D2.SceneEditor.Plugin` 名前空間。SE 側から MTEP へは `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` で参照する(既存ファイルの流儀に合わせる)。
- タスク中の行番号は 2026-08-30 時点のもの。先行タスクの編集でずれるため、シンボル名でアンカーを確認してから編集すること。
- 各タスク完了ごとにコミットする。

---

### Task 1: TimelineSettingWindow に「要素数」と「ライト補間トグル」を移設(#1, #4, #5, #8)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`(`DrawPostEffectSection` :287 の直後に新セクション、`DrawCommonToggleSection` :830 にトグル追加)

**Interfaces:**
- Consumes: `MTEP.TimelineData.textCount / paraffinCount / distanceFogCount / rimlightCount`、`MTEP.SubCameraManager`(`MinSubCameraCount=1`, `MaxSubCameraCount=8`, `EnsureCameraCount(int)` — `SubCameraManager.cs:266,327` 付近。正確なシグネチャは実装時に確認)、`timeline.isLightColorEasing / isLightExtraEasing / isLightCompatibilityMode`
- Produces: なし(後続タスクはレイヤー側の同 UI を削除するだけ)

- [ ] **Step 1: 要素数セクションを追加する**

`DrawPostEffectSection` の直後に以下を追加し、`DrawBody`(:156)のセクション呼び出し列の `DrawPostEffectSection` の次で呼ぶ:

```csharp
/// <summary>タイムライン上の要素数 (テキスト/ポストエフェクト/サブカメラ) の増減。
/// 旧レイヤー編集ウィンドウから移設</summary>
private void DrawElementCountSection(GUIView view)
{
    var timeline = MTEP.TimelineManager.instance.timeline;
    if (timeline == null)
    {
        return;
    }

    view.DrawLabel("要素数", 100, 20);

    DrawCountRow(view, "テキスト表示数", timeline.textCount, 1, 16,
        x => timeline.textCount = x);
    DrawCountRow(view, "パラフィン数", timeline.paraffinCount, 0, 8,
        x => timeline.paraffinCount = x);
    DrawCountRow(view, "距離フォグ数", timeline.distanceFogCount, 0, 8,
        x => timeline.distanceFogCount = x);
    DrawCountRow(view, "リムライト数", timeline.rimlightCount, 0, 8,
        x => timeline.rimlightCount = x);

    var subCameraManager = MTEP.SubCameraManager.instance;
    DrawCountRow(view, "サブカメラ数", subCameraManager.subCameras.Count,
        MTEP.SubCameraManager.MinSubCameraCount,
        MTEP.SubCameraManager.MaxSubCameraCount,
        x => subCameraManager.EnsureCameraCount(x));
}

/// <summary>整数値の増減行 (旧レイヤー編集ウィンドウの ±ボタン付き IntField と同型)</summary>
private void DrawCountRow(
    GUIView view, string label, int value, int min, int max,
    System.Action<int> onChanged)
{
    view.BeginHorizontal();
    {
        view.margin = 0;
        view.DrawLabel(label, view.labelWidth, 20);

        view.DrawIntField(new GUIView.IntFieldOption
        {
            value = value,
            width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
            height = 20,
            onChanged = x => onChanged(Mathf.Clamp(x, min, max)),
        });

        if (view.DrawButton("-", 20, 20, value > min))
        {
            onChanged(value - 1);
        }
        if (view.DrawButton("+", 20, 20, value < max))
        {
            onChanged(value + 1);
        }

        view.margin = GUIView.defaultMargin;
    }
    view.EndLayout();
}
```

注意点:
- `distanceFogCount` / `rimlightCount` のプロパティ名は `PostEffectTimelineLayer_DistanceFog.cs:86` / `_Rimlight.cs:86` の `timeline.〜Count` 実名に合わせる(実装時確認)。
- `SubCameraManager.EnsureCameraCount(int)` は **private かつ増加専用**(`SubCameraManager.cs:269-278`)。本タスクで `SubCameraManager` に public な `SetCameraCount(int count)` を増設する(増加は `AddNewCamera()`、減少は `RemoveLastCamera()` をループ。範囲は `MinSubCameraCount`〜`MaxSubCameraCount` に Clamp)。TimelineSettingWindow 側にループを書かないこと:

```csharp
/// <summary>サブカメラ数を指定数へ増減する (タイムライン設定ウィンドウの要素数 UI 用)</summary>
public void SetCameraCount(int count)
{
    count = Mathf.Clamp(count, MinSubCameraCount, MaxSubCameraCount);
    EnsureCameraCount(count);
    while (_subCameras.Count > count)
    {
        RemoveLastCamera();
    }
}
```

- `MTEP.SubCameraManager.instance` の取得方法は `SubCameraTimelineLayer.cs` の `subCameraManager` プロパティ実装を参照して同じ経路にする。
- 数を減らしたとき、減った対象(テキスト・エフェクト・サブカメラ)の項目をタイムライン / Inspector で選択中でも NRE にならないこと。既存の ItemInspector 側にインデックス範囲外ガードが無ければ「項目なし」表示へフォールバックさせる(Task 10 の実機確認にも項目あり)。

- [ ] **Step 2: ライト補間トグル 3 種を DrawCommonToggleSection に追加する**

`DrawCommonToggleSection`(:830)の末尾に、`LightTimelineLayerBase.cs:42-56` と同じ内容を追加:

```csharp
view.DrawToggle("ライトで色補間を有効化", timeline.isLightColorEasing, -1, 20, newValue =>
{
    timeline.isLightColorEasing = newValue;
});

view.DrawToggle("ライトで拡張補間を有効化", timeline.isLightExtraEasing, -1, 20, newValue =>
{
    timeline.isLightExtraEasing = newValue;
});

view.DrawToggle("ライトの互換性モードを有効化", timeline.isLightCompatibilityMode, -1, 20, newValue =>
{
    timeline.isLightCompatibilityMode = newValue;
    // SE では互換モードの実体がないためフラグの保存のみ行う
});
```

`DrawCommonToggleSection` 内の `timeline` の取り方は同メソッド既存コードに合わせる。

- [ ] **Step 3: 両バージョンをビルドする**

Global Constraints の MSBuild 2 コマンドを実行し、両方成功すること。

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(timeline): 要素数とライト補間トグルをタイムライン設定ウィンドウへ移設する"
```

---

### Task 2: PostEffectItemInspector にコピー行、PostEffectRowDrawer にトーンカーブプレビューを追加(#6, #10)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/PostEffectItemInspector.cs`(`DrawIndexedItem` :84)
- Modify: `source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs`(`DrawGTToneMapRows` :561)

**Interfaces:**
- Consumes: `MTEP.PostEffectManager` の `GetParaffinData/ApplyParaffin`・`GetDistanceFogData/ApplyDistanceFog`・`GetRimlightData/ApplyRimlight`(実名は `PostEffectTimelineLayer_*.cs` のコピー実装 `_Parrifin.cs:270-274` / `_DistanceFog.cs:236-244` / `_Rimlight.cs:285-293` を参照)、`MTEP.GTToneMap.ApplyTexture`(`_GTToneMap.cs:187-196`)
- Produces: なし

- [ ] **Step 1: インデックス付きエフェクトのコピー行を追加する**

`PostEffectItemInspector.DrawIndexedItem`(:84)で、各エフェクトの行描画後に「コピー先コンボ + コピーボタン」を描く。旧実装(`_Parrifin.cs:265-275`)と同じ動作:

```csharp
// エフェクト種別ごとにコンボの選択状態を保持する
private readonly Dictionary<MTEP.PostEffectType, GUIComboBox<string>> _copyToComboBoxes
    = new Dictionary<MTEP.PostEffectType, GUIComboBox<string>>();

/// <summary>同種の別インデックスへ設定をコピーする行。旧レイヤー編集ウィンドウから移設</summary>
private void DrawCopyRow(
    GUIView view,
    MTEP.PostEffectType effectType,
    IList<string> jpNames,
    int sourceIndex,
    System.Action<int> copyTo)
{
    GUIComboBox<string> comboBox;
    if (!_copyToComboBoxes.TryGetValue(effectType, out comboBox))
    {
        comboBox = new GUIComboBox<string> { getName = (name, index) => name };
        _copyToComboBoxes[effectType] = comboBox;
    }

    comboBox.items = jpNames;
    comboBox.DrawButton("コピー先", view);

    if (view.DrawButton("コピー", 60, 20))
    {
        var copyToIndex = comboBox.currentIndex;
        if (copyToIndex != -1 && copyToIndex != sourceIndex)
        {
            copyTo(copyToIndex);
        }
    }
}
```

呼び出し側(`DrawItem` :47 の Paraffin / DistanceFog / Rimlight 分岐)で、行描画の後に `DrawCopyRow` を呼ぶ。コピー実処理は各レイヤーの旧実装と同じく `postEffectManager.Apply〜(copyToIndex, data)`。`jpNames` は既存の `GetParaffinName` 等の名前配列生成ロジック(`PostEffectItemInspector.cs:101` 付近の添字接尾辞規約)を流用する。

- [ ] **Step 2: GTToneMap のトーンカーブプレビューを移設する**

`_GTToneMap.cs:119-201` のテクスチャ生成(`_gtToneMapTextureData` 比較 → `TextureUtils.ClearTexture` → `GTToneMap.ApplyTexture` → `view.DrawTexture`)を `PostEffectRowDrawer.DrawGTToneMapRows`(:561)の末尾へ移す。テクスチャとキャッシュ用フィールドは PostEffectRowDrawer のインスタンスフィールドにする(旧レイヤー側の `_gtToneMapTexture` / `_gtToneMapTextureData` の定義を `_GTToneMap.cs` 冒頭からそのまま移動)。

- [ ] **Step 3: 両バージョンをビルドする**

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/PostEffectItemInspector.cs source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs
git commit -m "feat(timeline): ポストエフェクトのコピーとトーンカーブ表示をInspectorへ移設する"
```

---

### Task 3: DressItemInspector に初期化 / 初期値更新ボタンを追加(#7)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/DressItemInspector.cs`(`DrawItems` :40 の冒頭)

**Interfaces:**
- Consumes: `MTEP.MaidCache.maidPropCache.ApplyInitialProp() / UpdateInitialProp()`(`DressTimelineLayer.cs:210,215` と同じ経路)
- Produces: なし

- [ ] **Step 1: DrawItems の冒頭(maidCache 解決後)にボタン行を追加する**

```csharp
// 衣装の初期化・初期値更新は旧レイヤー編集ウィンドウから移設
view.BeginHorizontal();
{
    if (view.DrawButton("初期化", 60, RowHeight))
    {
        maidCache.maidPropCache.ApplyInitialProp();
    }

    if (view.DrawButton("初期値更新", 100, RowHeight))
    {
        maidCache.maidPropCache.UpdateInitialProp();
    }
}
view.EndLayout();
```

`maidCache` の解決コードは `DrawItems` 既存実装(EyesItemInspector.cs:55-63 と同型のはず)を確認して合わせる。

- [ ] **Step 2: 両バージョンをビルドする**

- [ ] **Step 3: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/DressItemInspector.cs
git commit -m "feat(timeline): 衣装の初期化ボタンを衣装レイヤーのInspectorへ移設する"
```

---

### Task 4: 瞳位置・瞳スケール編集の移設(#9)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs`(視線タブ `DrawLookContent` :293 の末尾)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/EyesItemInspector.cs`(`RowKind` / `ResolveRowKind` :33 / `DrawItems` :52)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs`(共有化のための public 化のみ。UI 削除は Task 8)

**Interfaces:**
- Consumes: `MTEP.EyesTimelineLayer` の `GetEyesValue(MotionEyesType)` / `ApplyEyes(MotionEyesType, float, float)` / `EyesDisplayNameMap` / `EyesTypeMap`(`EyesTimelineLayer.cs:397-455` 参照。private なら internal/public 化するか、値の読み書き経路を MaidCache 側の同等 API に置き換える — 実装時に `ApplyEyes` の書き込み先を確認して決める)
- Produces: `EyesPosRowDrawer.DrawEyesPosImage(GUIView view)` / `DrawEyesSliderRows(GUIView view, MTEP.MotionEyesType eyesType)` / `DrawResetButton(GUIView view)`

- [ ] **Step 1: EyesPosRowDrawer を作る**

`EyesTimelineLayer.DrawEyesPos / DrawEyesSlider / DrawEyesImage`(`EyesTimelineLayer.cs:344-455` + 画像テクスチャ初期化 `InitTexture`)のロジックを SE 側の RowDrawer クラスへ移植する。描画は旧実装をそのまま移し、値の読み書きは `EyesTimelineLayer` の `GetEyesValue` / `ApplyEyes` を呼ぶ(アクセス可能にするため必要なら該当メソッドを public にする)。クラス構成は `TimelineLookRowDrawer`(MaidFaceWindow と EyesItemInspector で共有している既存例)に合わせる。

- [ ] **Step 2: MaidFaceWindow 視線タブに瞳位置セクションを追加する**

`DrawLookContent`(:293)の末尾(`DrawKeyedLookResetRow` の後)に追加:

```csharp
view.DrawHorizontalLine();
view.DrawLabel("瞳位置", -1, ROW_HEIGHT);
_eyesPosRowDrawer.DrawEyesPosImage(view);
_eyesPosRowDrawer.DrawResetButton(view);
foreach (var eyesType in EyesPosRowDrawer.AllEyesTypes) // PosL/PosR/ScaL/ScaR
{
    _eyesPosRowDrawer.DrawEyesSliderRows(view, eyesType);
}
```

フィールド `private readonly EyesPosRowDrawer _eyesPosRowDrawer = new EyesPosRowDrawer();` を `_timelineLookRowDrawer`(:83)の隣に追加。タイムライン未読込時はキー化されない旨のガードを `DrawLookKeyToggleRow`(:352)と同じ条件で入れる。

- [ ] **Step 3: EyesItemInspector を対応させる**

`RowKind` に `EyesPos` を追加し、`ResolveRowKind`(:33)の `default` 分岐のうち `EyesPosL/EyesPosR/EyesScaL/EyesScaR` を `EyesPos` に振り分ける。`DrawItems`(:75-83 の Unsupported 分岐)で `EyesPos` の場合は `_eyesPosRowDrawer.DrawEyesSliderRows(view, eyesType)` を呼ぶ。クラスコメント(:30-31)の「未対応」記述も更新する。

- [ ] **Step 4: 両バージョンをビルドする**

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/EyesItemInspector.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs
git commit -m "feat(timeline): 瞳位置・瞳スケール編集を表情ウィンドウとInspectorへ移設する"
```

---

### Task 5: モデル管理 UI を ItemInspector へ移設(#2, #11)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelTransformItemInspectorBase.cs`
- Modify(必要なら): `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelItemInspector.cs` / `BGModelItemInspector.cs`

**Interfaces:**
- Consumes: `MTEP.StudioModelManager`(`SetModelVisible` / `ChangePluginName` / `DeleteModel` / `UpdateAttachPoint`)、`MTEP.TimelineManager.CopyModel`、`MTEP.ModelHackManager.pluginNames`(`ModelTimelineLayerBase.cs:100-190` と同じ経路)
- Produces: `ModelTransformItemInspectorBase.DrawModelManageRows(GUIView view, TModel model)` — 派生 2 クラスの項目描画から呼ぶ

**設計判断(旧管理タブの「全モデル一覧」の受け皿):** Inspector は選択項目駆動のため、一覧性は **HierarchyWindow** が受け持つ(確認済み: `HierarchyWindow.cs:42` の `GUITreeView<GameObject>` によるシーン全体のツリーで、配置モデルの GameObject も一覧・クリック選択できる)。「Hierarchy で一覧 → 選択 → Inspector で管理操作」を正式経路とし、専用のモデル管理ウィンドウは新設しない。モデル 0 件時は選択できる項目が無い = 管理 UI が出ないだけで旧「モデルがありません」表示と実質等価。Hierarchy 選択 → タイムラインモデル項目の Inspector 表示が同期しない場合は、tracking フェーズ M0/M1 の選択ブリッジ(`TimelineSelectionBridge`)の対応範囲を確認し、モデル項目の逆同期が無ければ本タスクで補う。

- [ ] **Step 1: visible トグルと管理行を追加する**

`ModelTransformItemInspectorBase` の項目描画(Transform 行の前)に、対象モデルの管理行を追加する。内容は `ModelTimelineLayerBase.DrawModelContent`(:100-190)の移植:

- `view.DrawToggle(model.displayName, model.visible, ...)` → `modelManager.SetModelVisible`
- プラグイン選択コンボ(`modelHackManager.pluginNames`)→ `modelManager.ChangePluginName`
- 「複製」ボタン → `timelineManager.CopyModel(model)`
- 「削除」ボタン → `modelManager.DeleteModel(model)`
- アタッチ先メイドコンボ(先頭に「未選択」= null)+ アタッチポイントコンボ(`BoneUtils.AttachPointNames`)→ `model.attachMaidSlotNo` / `model.attachPoint` を設定して `modelManager.UpdateAttachPoint(model)`

注意:
- 配置モデル(`StudioModelStat`)専用の機能(プラグイン・複製・アタッチ)は背景モデル(`BGModelItemInspector` の TModel)には無い可能性が高い。`TModel` が `StudioModelStat` の場合のみ描くよう、管理行の描画は仮想メソッドにして `ModelItemInspector` 側でフル実装、基底では visible トグルのみとする(型は実装時に `ModelItemInspector.cs` / `BGModelItemInspector.cs` の TModel 実引数で確認)。
- コンボボックスは項目(モデル)ごとに使い回すため、旧実装(`ModelTimelineLayerBase.cs:15-17` のリスト方式)ではなく `Dictionary<string, GUIComboBox<...>>`(キー: `model.name`)で保持してよい。ただし基底の `_transformRowDrawers` が `PruneExcept(items)` で未選択分を破棄しているのに合わせ、コンボ辞書にも同じタイミングで Prune を入れる(モデル削除・複製で名前が変わった際にキャッシュが溜まり続けるのを防ぐ)。

- [ ] **Step 2: 背景モデルの visible トグルを確認する**

`BGModelItemInspector` の項目描画でも visible トグルが出ることを確認する(基底に置けば自動で付く)。背景モデルの visible 変更経路は `BGModelTimelineLayer.cs:255` の実装(`bgModelManager` 経由か `model.visible` 直接か)に合わせる。

- [ ] **Step 3: 両バージョンをビルドする**

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/
git commit -m "feat(timeline): モデルの管理操作をInspectorへ移設する"
```

---

### Task 6: BackgroundWindow に配置背景モデルセクションを追加(#3)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs`(`DrawBody` :119 のセクション列に追加)

**Interfaces:**
- Consumes: `MTEP.BGModelManager.modelInfoList / GetModels(string) / AddModelBySourceName(string) / DeleteModelBySourceName(string)`(`BGModelTimelineLayerBase.cs:18-60` と同じ)
- Produces: なし

- [ ] **Step 1: セクションを追加する**

`DrawFilterRows`(:215)と `DrawBgList`(:239)の間に、`BGModelTimelineLayerBase.DrawModelManage`(:11-68)を移植した `DrawBgModelSection` を追加する。タイムライン未読込(`MTEP.TimelineManager.instance.timeline == null`)なら描かない。行の描画は旧実装と同じ(インデント付き表示名 + `-` / 配置数 / `+`)。折りたたみ(他セクションに開閉トグルの流儀があればそれに合わせる)を付け、既定は閉にする。

- [ ] **Step 2: 両バージョンをビルドする**

- [ ] **Step 3: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/BackgroundWindow.cs
git commit -m "feat(timeline): 背景モデルの配置管理を背景ウィンドウへ移設する"
```

---

### Task 7: LightWindow のカバレッジ確認と補完

**Files:**
- Modify(不足時のみ): `source/COM3D2.SceneEditor.Plugin/LightWindow.cs` / `LightRowDrawer.cs`

**Interfaces:**
- Consumes: `MTEP.StudioLightManager.ApplyLight`(`LightTimelineLayerBase.cs:78`)
- Produces: なし

- [ ] **Step 1: 旧管理タブとの差分を確認する**

旧 `LightTimelineLayerBase.DrawLightManage / DrawLightContent`(:11-92)が持つ「ライトごとの visible トグル」が LightWindow(一覧 :105-145、編集セクション `DrawLightEditSection`)に存在するか確認する。追加/削除は既にある(`LightWindow.cs:112,119`)。

- [ ] **Step 2: visible トグルが無ければ追加する**

無い場合、`DrawLightEditSection` に `light.visible` トグルを追加し、変更時に `lightManager.ApplyLight(light)` を呼ぶ(タイムラインの `StudioLightStat` と SE の `Light` の対応付けは LightWindow 既存コードの流儀に合わせる)。既にある場合は本タスクはコミット不要でスキップ。

- [ ] **Step 3: (変更した場合)両バージョンをビルドして Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/LightWindow.cs
git commit -m "feat(timeline): ライトの表示トグルをライトウィンドウへ移設する"
```

---

### Task 8: 全レイヤーの DrawWindow 系 UI を削除する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/` 配下の以下:
  - `SubCameraTimelineLayer.cs:230-426`(DrawWindow 全体)
  - `TextTimelineLayer.cs:211-377`(DrawWindow 全体 + コンボ/カラーフィールド等の専用フィールド)
  - `PostEffectTimelineLayer.cs:234-258` と partial 5 ファイルの Draw 系メソッド・タブ enum・専用フィールド
  - `EyesTimelineLayer.cs:315-455+`(タブ enum、DrawWindow、DrawEyesLookAt/DrawEyesPos/DrawEyesSlider/DrawEyesImage のうち Task 4 で移設済みの残骸。EyesPosRowDrawer が参照する `GetEyesValue`/`ApplyEyes` 等は残す)
  - `DressTimelineLayer.cs:197-260`(DrawWindow/DrawDress)
  - `AnimationTimelineLayer.cs:271-289`(DrawWindow のみ。`DrawAnimeLayer` は `AnimationItemInspector.cs:46` が使うため **public のまま残す**)
  - `ModelTimelineLayer.cs:200-273` / `BGModelTimelineLayer.cs:201-275` / `BGModelMaterialTimelineLayer.cs:174-298`(DrawWindow・タブ・DrawModelEdit/DrawMaterial)
  - `ModelBoneTimelineLayer.cs:228-235` / `ModelMaterialTimelineLayer.cs:202-209` / `ModelShapeKeyTimelineLayer.cs:213-220`(DrawWindow)
  - `LightTimelineLayer.cs:280-302`(DrawWindow・タブ・DrawLightEdit)
  - 1-A 分類 16 レイヤーの委譲ラベル DrawWindow(`MorphTimelineLayer.cs:228-238` ほか。survey の 1-A 表参照)
  - `ModelTimelineLayerBase.cs`(DrawModelManage/DrawModelContent/GetPluginIndex とコンボリスト)
  - `BGModelTimelineLayerBase.cs`(DrawModelManage — クラスが空になる場合はクラス自体を残すか継承整理するかは差分最小の方を選ぶ)
  - `LightTimelineLayerBase.cs`(DrawLightManage/DrawLightContent)

**Interfaces:**
- Consumes: Task 1〜7 の移設完了が前提
- Produces: 各レイヤーから `DrawWindow` override が消える(宣言の削除は Task 9)

- [ ] **Step 1: レイヤーの DrawWindow override と専用 UI コードを削除する**

上記対象から `public override void DrawWindow(GUIView view)` と、その中からしか呼ばれない Draw ヘルパー・`TabType` enum・`_tabType`・UI 専用フィールド(コンボボックス、`ColorFieldCache`、テクスチャ等)を削除する。**Inspector / RowDrawer から参照されているメソッド・フィールドは残す**(削除前に各シンボルの参照 0 件を確認してから消すこと。リポジトリに `.codegraph/` があれば Grep 目視ではなく `codegraph_impact` / `codegraph explore` で被参照を機械的に確認する — `DrawWindow` 系の同名別概念への誤ヒットも避けられる)。

- [ ] **Step 2: 参照残りを確認する**

```bash
grep -rn "DrawModelManage\|DrawLightManage\|DrawModelEdit\|DrawEyesPos\|DrawDress\b" source --include=*.cs
```
ヒット 0 件であること(EyesPosRowDrawer 内の移植済みコードは名前を変えているため引っかからない想定)。

- [ ] **Step 3: 両バージョンをビルドする**

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/
git commit -m "refactor(timeline): 各レイヤーのDrawWindow編集UIを削除する"
```

---

### Task 9: TimelineLayerWindow と DrawWindow 宣言を撤去する

**Files:**
- Delete: `source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:72`(AddWindow 削除)
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:137`(「レイヤー編集」メニュー項目削除)
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:133-140` 付近(「編集」ボタン/自動オープン導線の削除。レイヤー追加コンボ自体は残す)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs:59-60`(`ResetDraw` / `DrawWindow` 宣言削除)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:484-492`(両 virtual 実装削除)
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:230-234`(`timelineLayerPosX/Y/Width/Height/Visible` 削除)

**Interfaces:**
- Consumes: Task 8 完了(全 override が消えていること)
- Produces: なし

- [ ] **Step 1: 操作対象メイド切替の受け皿を確認し、必要なら補完して単独コミットする**

旧ヘッダーのメイドコンボ(`TimelineLayerWindow.cs:48-58`)の機能 = `maidManager.ChangeMaid(maid)`。SE ではヒエラルキーのメイド選択で `TimelineWindow.cs:253-256` が slotNo を切り替えるため、**ヒエラルキー選択が受け皿として機能するか確認**し、経路が `ChangeMaid` 相当を呼んでいなければ TimelineWindow の選択連動処理に `maidManager.ChangeMaid` を補う。補完した場合はこの時点で両バージョンをビルドし、削除作業と混ぜず単独でコミットする:

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): ヒエラルキー選択でタイムラインの操作対象メイドを切り替える"
```

- [ ] **Step 2: ファイル削除と参照除去を行う**

上記 Files のとおり削除・修正する。`Config` の項目削除は、既存 XML に残った値が読み飛ばされること(SerializationのUnknown要素が例外にならないこと)を `Config.cs` のロード実装で確認する(XmlSerializer なら未知要素は無視されるため問題なし)。

- [ ] **Step 3: 参照残りを確認する**

```bash
grep -rn "TimelineLayerWindow\|timelineLayer\|DrawWindow\|ResetDraw" source --include=*.cs
```
`DrawWindow` は EditorSubWindow 系の別概念(`DrawContent` 等)に誤ヒットしないよう確認の上、レイヤー関連のヒットが 0 件であること。

- [ ] **Step 4: 両バージョンをビルドする**

- [ ] **Step 5: Commit**

```bash
git add -A source/COM3D2.SceneEditor.Plugin/
git commit -m "refactor(timeline): レイヤー編集ウィンドウを撤去する"
```

---

### Task 10: ドキュメント更新

**Files:**
- Modify: `docs/timeline-layer-window-removal-survey.md`(結果反映)
- Modify: `docs/layer-window-duplication-survey.md`(survey の第 4 節の差分 5 点を反映し、撤去完了を記録)
- Modify: タイムラインのロードマップ文書(editing-roadmap。W3 節に撤去完了と実機確認チェックリストを追記)

**Interfaces:** なし

- [ ] **Step 1: 各ドキュメントへ対応状況を反映する**

survey 第 3 節の #1〜#11 それぞれに移設先(実装済みのファイル名)を追記し、ロードマップへ以下の実機確認チェックリストを載せる:

```markdown
#### レイヤー編集ウィンドウ撤去・実機確認項目(次回ゲーム起動時)
- [ ] タイムライン設定: テキスト/パラフィン/フォグ/リムライト/サブカメラの数を増減できる
- [ ] タイムライン設定: ライト補間トグル 3 種が機能する(保存値が反映される)
- [ ] Inspector: パラフィン等のコピーが別インデックスへ反映される
- [ ] Inspector: GTToneMap 項目でトーンカーブ画像が出る
- [ ] Inspector: 衣装レイヤーの初期化 / 初期値更新が効く
- [ ] 表情ウィンドウ視線タブ: 瞳位置ドラッグ・スライダー・初期化が効き、キー化される
- [ ] Inspector: 瞳位置・瞳スケール項目が編集できる(「未対応」が消えている)
- [ ] Inspector: モデル項目で visible / プラグイン / 複製 / 削除 / アタッチが操作できる
- [ ] 背景ウィンドウ: 背景モデルの追加 / 削除ができ、レイヤーのキーと連動する
- [ ] ライトウィンドウ: ライトの表示トグルが機能する
- [ ] メニューバー / TimelineWindow に「レイヤー編集」への導線が残っていない
- [ ] 旧 config.xml(timelineLayer* 設定入り)を読み込んでもエラーにならない
- [ ] 要素数を減らしたとき、減った対象を選択中でも NRE にならない(項目なし表示へフォールバック)
- [ ] Hierarchy でモデルを選択 → Inspector にモデルの管理行(visible/複製/削除/アタッチ)が出る
- [ ] レイヤー切替・タイムライン閉鎖で NRE が出ない
```

- [ ] **Step 2: Commit**

```bash
git add docs/
git commit -m "docs(timeline): レイヤー編集ウィンドウ撤去の対応状況を反映する"
```

---

## レビュー却下メモ

- 却下した指摘なし(plan-reviewer の 🔴1 件・🟡4 件をすべて取り込み済み)。🔴「モデル一覧性の喪失」は Task 5 の設計判断(HierarchyWindow を一覧の受け皿とする。実コードで GUITreeView<GameObject> による一覧・選択が可能なことを確認済み)として解消。

## Self-Review メモ

- Spec(survey #1〜#11)との対応: #1,4,5,8 → Task 1 / #6,10 → Task 2 / #7 → Task 3 / #9 → Task 4 / #2,11 → Task 5 / #3 → Task 6 / ライト visible → Task 7 / UI 削除 → Task 8 / 本体撤去 + メイド切替受け皿 + Config → Task 9 / ドキュメント → Task 10。survey「本体機能」表の各行も Task 9 でカバー。
- レイヤー側の削除(Task 8)は移設(Task 1〜7)がすべて済んでから行う順序制約あり。
- `SubCameraManager.EnsureCameraCount` / `distanceFogCount` 等、一部 API 実名は実装時確認を明記済み(確認先ファイル・行を各タスクに記載)。
