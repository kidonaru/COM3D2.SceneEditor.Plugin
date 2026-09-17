# レイヤー編集ウィンドウ (TimelineLayerWindow) 削除に向けた残存 UI 調査

作成: 2026-08-30
前提: 全 28 具象レイヤーの ItemInspector 対応（`Timeline/ItemInspector/`）が完了した時点での、各 `TimelineLayer.DrawWindow` 残存 UI の全数調査。`docs/layer-window-duplication-survey.md`（2026-08-27/29 版）の A/B/C 分類との差分も反映する。

## 結論サマリ

- **16 レイヤー**は委譲ラベルのみで即削除可。
- 編集系 UI はほぼ全て ItemInspector / 個別ウィンドウで代替済み。**未代替として残るのは「管理系 8 件 + 編集系 3 件」**（下記総括）。
- これらを移設し終えるまで TimelineLayerWindow は削除できない。

**2026-08-30 追記: 移設と撤去は完了した。** #1〜#11 の移設先は下記総括の「移設先（実装）」列を参照。
`TimelineLayerWindow` と全レイヤーの `DrawWindow` / `ResetDraw`、Config の `timelineLayer*`、
メニューバー「レイヤー編集」と TimelineWindow のレイヤー追加時の自動オープンはすべて削除済み。

---

## 1. レイヤーごとの状態

### 1-A. 委譲ラベルのみ（実 UI ゼロ = そのまま削除可）

| レイヤー | DrawWindow | 委譲先 |
|---|---|---|
| BGColorTimelineLayer | `BGColorTimelineLayer.cs:169-173` | BackgroundWindow / `BGColorItemInspector` |
| BGTimelineLayer | `BGTimelineLayer.cs:150-155` | BackgroundWindow / `BGItemInspector` |
| CameraTimelineLayer | `CameraTimelineLayer.cs:172-178` | CameraWindow / `CameraItemInspector` |
| MaidMaterialTimelineLayer | `MaidMaterialTimelineLayer.cs:176-180` | MaterialEditWindow / `MaidMaterialItemInspector` |
| MorphTimelineLayer | `MorphTimelineLayer.cs:228-238` | MaidFaceWindow / `MorphItemInspector` |
| MotionTimelineLayer | `MotionTimelineLayer.cs:801-809` | BoneEditWindow / MaidIKWindow / MaidFingerWindow / `MotionItemInspector` |
| MoveTimelineLayer | `MoveTimelineLayer.cs:160-165` | ギズモ・InspectorWindow / `MoveItemInspector` |
| PngPlacementTimelineLayer | `PngPlacementTimelineLayer.cs:201-206` | PngPlacementWindow / `PngPlacementItemInspector` |
| PsylliumTimelineLayer | `PsylliumTimelineLayer.cs:745-749` | LiveEffectWindow / `PsylliumItemInspector` |
| SeTimelineLayer | `SeTimelineLayer.cs:77-81` | SoundWindow / `SeItemInspector` |
| ShapeKeyTimelineLayer | `ShapeKeyTimelineLayer.cs:135-139` | ShapeKeyEditWindow / `ShapeKeyItemInspector` |
| StageLaserTimelineLayer | `StageLaserTimelineLayer.cs:451-455` | LiveEffectWindow / `StageLaserItemInspector` |
| StageLightTimelineLayer | `StageLightTimelineLayer.cs:460-464` | LiveEffectWindow / `StageLightItemInspector` |
| UndressTimelineLayer | `UndressTimelineLayer.cs:107-111` | MaidUndressWindow / `UndressItemInspector` |
| VoiceTimelineLayer | `VoiceTimelineLayer.cs:104-108` | SoundWindow / `VoiceItemInspector` |
| TimelineLayerBase（基底） | `TimelineLayerBase.cs:489-492` 空実装 | — |

### 1-B. ラベル + モデル管理 UI（編集は委譲済み、管理系のみ残存）

| レイヤー | 実装位置 | 代替状況 |
|---|---|---|
| ModelBoneTimelineLayer | `ModelBoneTimelineLayer.cs:228-235` | 編集は BoneEditWindow / `ModelBoneItemInspector` で代替済み。**管理は未代替** |
| ModelMaterialTimelineLayer | `ModelMaterialTimelineLayer.cs:202-209` | 編集は MaterialEditWindow / `ModelMaterialItemInspector`。**管理は未代替** |
| ModelShapeKeyTimelineLayer | `ModelShapeKeyTimelineLayer.cs:213-220` | 編集は ShapeKeyEditWindow / `ModelShapeKeyItemInspector`。**管理は未代替** |

共通の管理 UI は `ModelTimelineLayerBase.DrawModelManage`（`ModelTimelineLayerBase.cs:22-98`）+ `DrawModelContent`（同 `:100-190`）:
**モデル一覧 / 表示トグル / 実装プラグイン選択コンボ / 複製ボタン(:139) / 削除ボタン(:144) / アタッチ先メイド選択(:169) / アタッチポイント選択(:184)**。全て管理系。

### 1-C. タブ構成（操作タブは概ね委譲済み・管理タブ残存）

| レイヤー | DrawWindow | 操作タブ | 管理タブ |
|---|---|---|---|
| LightTimelineLayer | `LightTimelineLayer.cs:280-296` | `DrawLightEdit`（`:298-302`）ラベルのみ、LightWindow へ委譲済み | `LightTimelineLayerBase.DrawLightManage`（`LightTimelineLayerBase.cs:11-59`） |
| ModelTimelineLayer | `ModelTimelineLayer.cs:202-217` | `DrawModelEdit`（`:219-273`）**実 UI 残存**: 操作種類コンボ、表示トグル(:249)、Transform 編集(:259) | `ModelTimelineLayerBase.DrawModelManage` |
| BGModelTimelineLayer | `BGModelTimelineLayer.cs:201-216` | `DrawModelEdit`（`:218-246`）+ `DrawModel`（`:248-275`）**実 UI 残存**: 操作種類コンボ、表示トグル(:255)、Transform(:263) | `BGModelTimelineLayerBase.DrawModelManage`（`:11-68`） |
| BGModelMaterialTimelineLayer | `BGModelMaterialTimelineLayer.cs:174-189` | `DrawMaterial`（`:191-298`）**実 UI 残存**: モデル/マテリアル選択コンボ、初期化(:244)、色(:261)・数値(:279)プロパティ → **すべて Inspector で代替済み** | `BGModelTimelineLayerBase.DrawModelManage` |
| PostEffectTimelineLayer | `PostEffectTimelineLayer.cs:234-258` 5タブ | 1-E 参照 | — |
| EyesTimelineLayer | `EyesTimelineLayer.cs:320-336` 2タブ | 視線タブ `DrawEyesLookAt`（`:338-342`）ラベルのみ | 位置タブ `DrawEyesPos`（`:344-395`）**実 UI 残存・未代替** |

管理タブの中身:

- `LightTimelineLayerBase.DrawLightManage`: ライトごとの表示トグル(:75)・削除ボタン(:83)（`DrawLightContent` `:61-92`）+ **タイムライン全体トグル 3 種（色補間 `:42` / 拡張補間 `:47` / 互換性モード `:52`）**
- `BGModelTimelineLayerBase.DrawModelManage`: 背景モデルの sourceName ごとの `-`/`+`（削除 `:48`・追加 `:56`）と配置数表示

### 1-D. 実 UI が全面的に残るレイヤー

| レイヤー | 残存 UI | 系統 | 代替状況 |
|---|---|---|---|
| AnimationTimelineLayer `:271-289` → `DrawAnimeLayer` `:291-378` | ループトグル(:317)、時間上書きトグル(:322)、アニメ名(:329)、開始時間スライダー(:335) | 編集 | **代替済み**。`AnimationItemInspector.cs:46` が同じ `DrawAnimeLayer` を再利用（public のまま残す） |
| DressTimelineLayer `:197-200` → `DrawDress` `:202-260` | 初期化ボタン(:208)、初期値更新ボタン(:213)、部位ごとの衣装名表示(:254) | 管理 + 表示 | 表示は `DressItemInspector.cs:77` で代替。**初期化 / 初期値更新ボタンは未代替** |
| EyesTimelineLayer 位置タブ `DrawEyesPos` `:344-395` | 瞳位置の画像ドラッグ UI(`DrawEyesImage` `:456`)、初期化(:369)、EyesPosL/R・EyesScaL/R スライダー(`:397-455`) | 編集 | `EyesItemInspector.ResolveRowKind`（`:33-50`）は EyesRot / LookAtTarget のみ。**瞳位置・瞳スケールは未代替** |
| SubCameraTimelineLayer `:230-426` | カメラ選択(:251)、**追加(:253)・削除(:263)**、有効(:280)、追従メイド(:300)・ポイント(:315)、向き反映(:319)、位置(:337)、回転(:341)、FoV(:345)、ビューポート(:364-414) | 管理 + 編集 | 編集系は `SubCameraRowDrawer.cs:51-` で**代替済み**。**追加・削除は未代替**（`SubCameraManager.AddNewCamera` / `RemoveLastCamera` の唯一の UI） |
| TextTimelineLayer `:211-377` | **テキスト表示数の増減(:222-240)**、対象選択(:249)、本文(:271)、フォント(:286)、サイズ(:288)、行間(:301)、整列(:314)、幅(:322)、高さ(:340)、色(:358)、Transform(:364) | 管理 + 編集 | 編集系は `TextRowDrawer.cs:58-167` で**代替済み**。**`timeline.textCount` の増減 UI は未代替**（他に UI 無し / `TimelineData.cs:436`） |

### 1-E. PostEffectTimelineLayer（partial 5 種）

| タブ | 実装 | 残存 UI | 代替状況 |
|---|---|---|---|
| 被写界深度 | `_DepthOfField.cs:22-116` | 有効化(:34)、追従メイド(:62-76)、共通設定(:88,95,104) | `PostEffectRowDrawer.DrawDepthOfFieldRows`（`:106-`）で**全項目代替済み** |
| パラフィン | `_Parrifin.cs:71-329` | **エフェクト数増減(:79-98)**、対象選択(:111)、各パラメータ(:132-262)、**コピー(:265,268)**、デバッグ表示(:283) | 編集は `DrawParaffinRows`（`:196-`）で代替。**数・コピー未代替** |
| 距離フォグ | `_DistanceFog.cs:71-299` | 同上構成（数 `:79-98`、コピー `:236,238`） | `DrawDistanceFogRows`（`:320-`）で編集代替。**数・コピー未代替** |
| リムライト | `_Rimlight.cs:71-349` | 同上 + ライト方向(:188)（数 `:79-98`、コピー `:285,287`） | `DrawRimlightRows`（`:415-`、ライト方向含む）で編集代替。**数・コピー未代替** |
| GTToneMap | `_GTToneMap.cs:119-201` | 有効化(:131) + 各パラメータ、**トーンカーブのテクスチャ表示(:199)** | `DrawGTToneMapRows`（`:561-`）で代替。**トーンカーブ画像プレビューのみ Inspector 側に無い** |

---

## 2. TimelineLayerWindow 本体の機能と削除時の扱い

`source/COM3D2.SceneEditor.Plugin/TimelineLayerWindow.cs`（199 行）

| 機能 | 位置 | 削除時の扱い |
|---|---|---|
| レイヤー選択コンボ（`usingLayerInfoList` → `ChangeActiveLayer`） | `:36-46`, `:177-179` | **移設不要**。TimelineWindow のレイヤー行クリックと `TimelineWindow.cs:133`（追加コンボ）/ `:255`,`:269`（選択連動）で切替可能 |
| 操作対象メイド選択コンボ（`hasSlotNo` のとき） | `:48-58`, `:181-189` | **要確認**。`TimelineWindow.cs:253-256` はヒエラルキー選択連動の slotNo 切替のみ。明示的なメイド切替 UI の受け皿が必要 |
| `currentLayer.DrawWindow` 呼び出し + 例外隔離 | `:128-158` | ウィンドウごと削除。`ITimelineLayer.DrawWindow`（`ITimelineLayer.cs:60`）と `TimelineLayerBase.DrawWindow` の宣言も撤去対象 |
| `ResetDraw` 通知 | `:164-168` | 呼び出し元が消えるため `ITimelineLayer.ResetDraw` の要否を再検討 |
| ComboBoxPopupWindow のフォーカス処理ホスト | `:105` | ウィンドウ固有。削除で不要 |
| ウィンドウ配置 Config（`timelineLayerPosX/Y/Width/Height/Visible`） | `:77-97` | `Config.cs` の該当項目を削除（後方互換の読み飛ばし可否を確認） |
| TimelineWindow からの自動オープン | `TimelineWindow.cs:135-138` | 削除必須（レイヤー追加時に本ウィンドウを開く導線） |

その他の参照: `Manager/WindowManager.cs`（登録）、`MenuBarWindow.cs`（Window > レイヤー編集）。

---

## 3. 総括: 未代替で移設が必要な機能

### 管理系（追加・削除・複製・数の増減）

| # | 機能 | 移設先（実装） |
|---|---|---|
| 1 | サブカメラ 追加 / 削除 | `TimelineSettingWindow.DrawElementCountSection`（個別タブの「要素数」行）。増減の実処理は新設した `SubCameraManager.SetCameraCount` |
| 2 | モデル管理（表示トグル・実装プラグイン選択・複製・削除・アタッチ先） | `ModelManageRowDrawer` を新設し `ModelItemInspector` から呼ぶ。一覧性は HierarchyWindow が受け持つ（選択 → Inspector で操作） |
| 3 | 背景モデルの 追加 / 削除（sourceName ごとの `+`/`-`） | `BackgroundWindow.DrawBgModelSection`（既定は畳んだ折りたたみセクション） |
| 4 | テキスト表示数の増減 | `TimelineSettingWindow.DrawElementCountSection` |
| 5 | ポストエフェクトのエフェクト数（パラフィン / 距離フォグ / リムライト） | `TimelineSettingWindow.DrawElementCountSection` |
| 6 | ポストエフェクトの他インデックスへコピー | `PostEffectRowDrawer.DrawCopyRow`（各エフェクトの行末に「コピー先 + コピー」） |
| 7 | 衣装 初期化 / 初期値更新 ボタン | `DressItemInspector.DrawItems` の先頭行 |
| 8 | ライト用タイムライン全体トグル 3 種（色補間 / 拡張補間 / 互換性モード） | `TimelineSettingWindow.DrawLightToggleSection`。3 つとも `TimelineData` の個別設定のため、計画の共通タブではなく個別タブへ置いた |

### 編集系

| # | 機能 | 移設先（実装） |
|---|---|---|
| 9 | 瞳位置 / 瞳スケール（画像ドラッグ UI・スライダー・初期化） | `EyesPosRowDrawer` を新設し、`MaidFaceWindow.DrawEyesPosSection`（視線タブ）と `EyesItemInspector`（`RowKind.EyesPos`）で共有。値の読み書きは `EyesTimelineLayer.GetEyesValue/ApplyEyes` の静的版 |
| 10 | GTToneMap のトーンカーブ画像プレビュー | `PostEffectRowDrawer.DrawGTToneMapCurve` |
| 11 | モデル / 背景モデルの表示トグル (visible) | `ModelTransformItemInspectorBase.DrawModelManageRows`（配置モデルは `ModelManageRowDrawer`、背景モデルは `BGModelItemInspector` の表示トグル） |

> ライトごとの表示トグルは LightWindow の「有効」トグル（`light.enabled`）が
> `StudioLightStat.visible` と同じ実体を書くため、追加実装は不要だった。

### 代替済みで単純削除できるもの（確認済み）

- AnimationTimelineLayer: `AnimationItemInspector.cs:46` が `DrawAnimeLayer` を直接再利用。レイヤーの `DrawWindow`（`:271-289`）だけ削ればよい
- SubCameraTimelineLayer の編集項目一式 → `SubCameraRowDrawer`
- TextTimelineLayer の編集項目一式 → `TextRowDrawer`
- PostEffect 5 種の編集項目 → `PostEffectRowDrawer`
- BGModelMaterial 操作タブ → `BGModelMaterialItemInspector` + `MaterialItemInspectorBase` + `MaterialPropertyRowsDrawer`（初期化は `MaterialPropertyRowsDrawer.cs:85`）
- ModelTimelineLayer / BGModelTimelineLayer 操作タブの Transform → `ModelItemInspector` / `BGModelItemInspector`（`ModelTransformItemInspectorBase.cs:52`, `ObjectTransformRowDrawer`）

---

## 4. `docs/layer-window-duplication-survey.md` との差分（更新推奨箇所）

- A 分類「BGModelMaterialTimelineLayer は対象外」→ ItemInspector 経由で代替済み
- B 分類「SubCameraTimelineLayer は対象外」→ 編集系は代替済み、残るのは追加/削除のみ
- C 分類「ModelTimelineLayer / BGModelTimelineLayer 操作タブは対象外」→ Transform は代替済み、残るのは visible と操作種類コンボ
- C 分類「AnimationTimelineLayer は対象外」→ Inspector が同一メソッドを再利用しており委譲可能
- C 分類「TextTimelineLayer / PostEffectTimelineLayer は対象外」→ 編集系は代替済み、残るのは数・コピーの管理系のみ
