# レイヤー編集ウィンドウと個別ウィンドウの機能被り調査

調査日: 2026-08-27
目的: レイヤー編集ウィンドウ（`TimelineLayerWindow`）内で各 `TimelineLayer.DrawWindow` が描画する編集 UI と、SceneEditor の個別ウィンドウ群の機能被りを洗い出し、レイヤー編集ウィンドウ側の描画削除の判断材料にする。

## 前提となる仕組み

- レイヤー編集ウィンドウ: `TimelineLayerWindow.cs`
  - `DrawHeader()`（:171）でレイヤー選択コンボ＋操作対象メイド選択を共通描画
  - `DrawLayerWindow()`（:128）で `currentLayer.DrawWindow(_view)` を呼ぶ
  - `TimelineLayerBase.DrawWindow`（TimelineLayerBase.cs:499）は空実装。全具象レイヤーが override 済み
- 個別ウィンドウとタイムラインの連携方式
  - キーフレーム登録ボタンは `TimelineControlWindow`（:473〜）のみ
  - キーフレーム値の直接編集は `KeyFrameInspector`（InspectorWindow 内、KeyFrameInspector.cs:16）
  - 「追跡チェック」方式で間接連携: `BoneEditWindow` / `MaterialEditWindow` / `ShapeKeyEditWindow` / `MaidFaceWindow` — チェック ON の項目だけがタイムライン表示・キー書き込み対象
- 既に委譲方針の前例あり: `PngPlacementTimelineLayer.DrawWindow`（:201）は「PNG 配置の編集は PngPlacement ウィンドウで行ってください」の案内ラベル 1 行のみ

## 被り分類マトリクス

### A. 個別ウィンドウと明確に被っている（削除・委譲の第一候補）

> **対応状況（2026-08-27）:** BGModelMaterialTimelineLayer を除く 8 レイヤーは案内ラベル＋個別ウィンドウ委譲へ移行済み。

| レイヤー | レイヤー側 UI（DrawWindow） | 被る個別ウィンドウ | 被りの内容 | 対応 |
|---|---|---|---|---|
| MaidMaterialTimelineLayer「メイドマテリアル」 | スロット/マテリアル選択、色・数値プロパティ編集 | MaterialEditWindow `DrawMaidMaterial`（:157） | 完全に同機能。個別側は追跡チェック連携も持つ | 対応済み（委譲ラベル化） |
| ModelMaterialTimelineLayer「モデルマテリアル」操作タブ | モデル/マテリアル選択、色・数値プロパティ編集 | MaterialEditWindow `DrawModelMaterial`（:203） | 完全に同機能。追跡チェック連携あり | 対応済み（委譲ラベル化。管理 UI は常時表示へ） |
| BGModelMaterialTimelineLayer「背景モデルマテリアル」（:174 操作タブ） | 同上 | MaterialEditWindow `DrawBGModelMaterial`（:248） | 同機能。ただし個別側は追跡チェック非表示 | **対象外**（下記理由） |
| ShapeKeyTimelineLayer「メイドシェイプ」 | シェイプキー追加/重みスライダ | ShapeKeyEditWindow `DrawMaidShapeKeys`（:153） | 同機能。個別側のチェック集合がタイムライン opt-in のソース・オブ・トゥルース | 対応済み（委譲ラベル化） |
| ModelShapeKeyTimelineLayer「モデルシェイプ」操作タブ | ブレンドシェイプ weight スライダ | ShapeKeyEditWindow `DrawModelContent`（:292） | 同機能・追跡連携あり | 対応済み（委譲ラベル化。管理 UI は常時表示へ） |
| ModelBoneTimelineLayer「モデルボーン」操作タブ | モデル選択・ボーンごとの Transform 編集 | BoneEditWindow `DrawModelContent`（:304）＋ InspectorWindow ボーン編集 | 同機能。個別側はボーン追跡トグル連携あり | 対応済み（委譲ラベル化。管理 UI は常時表示へ） |
| MorphTimelineLayer「メイド表情」 | 表情モーフのスライダ/トグル | MaidFaceWindow `DrawMorphList`（:224） | 同機能。個別側は追跡チェック・プリセット連携あり | 対応済み（委譲ラベル化。強制上書きトグルは残置） |
| MotionTimelineLayer「メイドアニメ」の手指/足指タブ | フィンガーブレンド編集 | MaidFingerWindow `DrawFingerBlend`（:190） | 同機能（プリセットは個別側のみ） | 対応済み（手指/足指タブを「指」タブへ統合し委譲ラベル化。ブレンド有効トグルは残置） |
| UndressTimelineLayer「メイド脱衣」 | スロット表示トグル | MaidUndressWindow `DrawCategoryList`（:96） | 同機能（一括ボタン・衣装変更は個別側のみ） | 対応済み（委譲ラベル化） |

**BGModelMaterialTimelineLayer を対象外とした理由:** MaterialEditWindow の背景タブは「現在の背景の Renderer」を対象とする（MaterialEditWindow.cs:285-286）のに対し、レイヤー側は「BGModelManager が配置した背景モデル」を対象とするため、両者の対象集合が食い違う。委譲すると配置背景モデルのマテリアルを編集する手段が失われるため、レイヤー側 UI を残している。

### B. 部分的に被っている（要検討）

| レイヤー | レイヤー側 UI | 対応する個別ウィンドウ | 差分・注意点 |
|---|---|---|---|
| CameraTimelineLayer「カメラ」（:187） | 位置/回転/距離/FoV、対象設定 | CameraWindow `DrawMainCameraContent`（:353） | 編集項目はほぼ同じ。ただし個別側にタイムライン連携（キー登録）は無い |
| SubCameraTimelineLayer「サブカメラ」（:230） | カメラ追加/削除、追従、FoV、ビューポート | CameraWindow（メイン/SceneView のみ） | サブカメラの管理 UI は個別側に存在しない → 被りは薄い |
| LightTimelineLayer「ライト」（:294） | ライト選択、Transform、色、range/強度/角度、管理タブ | LightWindow（:13） | 編集項目はほぼ重複。ただし実体が別系統（レイヤー側は MTE の `StudioLightStat`、個別側は `StudioLightManager`）で、色補間等のレイヤー固有トグルは基底 `LightTimelineLayerBase` にある |
| BGTimelineLayer「背景」（:162） | 背景選択、背景 Transform | BackgroundWindow（:13） | 背景選択は被り。背景オブジェクトの Transform 編集は個別側に無い |
| BGColorTimelineLayer「背景色」（:184） | カメラ背景色、地面色/位置/スケール | BackgroundWindow `DrawBgColorRow`（:181） | 背景色のみ被り。地面設定はレイヤー側固有 |
| MotionTimelineLayer「メイドアニメ」の編集タブ（`DrawTransformEdit` :916） | ボーンごとの Transform / IK 編集 | BoneEditWindow ＋ MaidIKWindow ＋ InspectorWindow | 概念的には被るが、レイヤー側はキーフレーム対象カテゴリ・操作種類の切替が密結合。要精査 |
| MoveTimelineLayer「メイド移動」（:143） | メイド本体の位置/回転/スケール | （直接対応なし。ギズモ/SceneView 操作） | 個別ウィンドウとしての被りは無いが、ギズモ操作で代替可能かは要検討 |
| EyesTimelineLayer「メイド瞳」の視線タブ（`DrawEyesLookAt` :391） | 注視先・視線編集 | MaidFaceWindow `DrawLookContent`（:307） | 視線・注視は被り。瞳位置/スケールの画像 UI（位置タブ :475）はレイヤー側固有 |
| BGModelTimelineLayer / ModelTimelineLayer の管理タブ（各 Base の `DrawModelManage`） | モデル追加/削除/複製/アタッチ | HierarchyWindow ＋ InspectorWindow ＋（モデル導入は ModItemExplorer 系） | 管理機能は個別ウィンドウに完全対応するものが無い → 被りは薄い |

### C. 被りなし（レイヤー固有 UI、削除対象外）

| レイヤー | 理由 |
|---|---|
| AnimationTimelineLayer「メイドアニメブレンド」（:271） | アニメレイヤーブレンド設定は個別ウィンドウに存在しない |
| DressTimelineLayer「メイド衣装」（:197） | 初期値との差分表示が主。MaidUndressWindow の衣装変更とは目的が異なる |
| VoiceTimelineLayer「メイドボイス」（:105） | ボイス再生パラメータは固有 |
| SeTimelineLayer「効果音」（:148） | SE 管理は固有 |
| TextTimelineLayer「テキスト」（:211) | テキスト表示は固有 |
| StageLightTimelineLayer（:495）/ StageLaserTimelineLayer（:494）/ PsylliumTimelineLayer（:827） | MTE 固有オブジェクトで個別ウィンドウ側に対応機能なし |
| PostEffectTimelineLayer（:234、partial 5 種） | ポストエフェクト編集は個別ウィンドウに無い（タイムライン設定ウィンドウの有効化のみ） |
| ModelTimelineLayer「モデル」の操作タブ（:219）/ BGModelTimelineLayer 操作タブ（:218） | モデル Transform 編集。InspectorWindow/ギズモで部分代替できるが直接の被りウィンドウ無し |
| PngPlacementTimelineLayer（:201） | **対応済み**（案内ラベルのみ、PngPlacementWindow へ委譲済み） |

## 削除方針の示唆

1. **A 分類は PngPlacementTimelineLayer 方式（案内ラベル＋個別ウィンドウ委譲）へ移行済み**（BGModelMaterial を除く 8 レイヤー）。マテリアル 2 種・シェイプキー 2 種・モデルボーン・表情は、個別ウィンドウ側が追跡チェックでタイムラインと連携しているため機能欠損なく削除できた。
2. 指ブレンド・脱衣は個別ウィンドウ側に追跡チェックが無いが、キー書き込みが `GetBaseFinger` / `maidCache.IsSlotVisible` というライブ状態を読む経路のため、個別ウィンドウでの変更もそのままキー化される。追跡機構の追加は不要と判断して委譲済み。
3. B 分類（カメラ / ライト / 背景 / メイドアニメ編集タブ）は実体系統やレイヤー固有項目の差分があり、単純委譲では機能欠損が出る。個別に設計判断が必要。
4. C 分類はレイヤー編集ウィンドウにしか無い UI のため残す。

## 参照

- レイヤー側詳細: `Timeline/TimelineLayer/*.cs` の各 `DrawWindow`
- 個別ウィンドウ詳細: リポジトリ直下 `source/COM3D2.SceneEditor.Plugin/*Window.cs`
- 連携基盤: `TimelineControlWindow.cs`（キー登録）、`KeyFrameInspector.cs`（キー値編集）、各追跡ストア（`track.getStore().Mark/Unmark`）
