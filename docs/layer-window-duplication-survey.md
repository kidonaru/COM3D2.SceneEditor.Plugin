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
| MorphTimelineLayer「メイド表情」 | 表情モーフのスライダ/トグル | MaidFaceWindow `DrawMorphList`（:224） | 同機能。個別側は追跡チェック・プリセット連携あり | 対応済み（委譲ラベル化。強制上書きは編集経路の消失に伴い廃止） |
| MotionTimelineLayer「メイドアニメ」の手指/足指タブ | フィンガーブレンド編集 | MaidFingerWindow `DrawFingerBlend`（:190） | 同機能（プリセットは個別側のみ） | 対応済み（手指/足指タブを「指」タブへ統合し委譲ラベル化。ブレンド有効トグルは MaidFingerWindow のヘッダーへ「TL:ブレンド有効」として移設） |
| UndressTimelineLayer「メイド脱衣」 | スロット表示トグル | MaidUndressWindow `DrawCategoryList`（:96） | 同機能（一括ボタン・衣装変更は個別側のみ） | 対応済み（委譲ラベル化） |

**BGModelMaterialTimelineLayer を対象外とした理由:** MaterialEditWindow の背景タブは「現在の背景の Renderer」を対象とする（MaterialEditWindow.cs:285-286）のに対し、レイヤー側は「BGModelManager が配置した背景モデル」を対象とするため、両者の対象集合が食い違う。委譲すると配置背景モデルのマテリアルを編集する手段が失われるため、レイヤー側 UI を残している。

### B. 部分的に被っている（要検討）

> **対応状況（2026-08-27）:** カメラ / ライト / 背景 / 背景色 / メイド移動 / メイド瞳（視線タブ）/ メイドアニメ（編集タブ）の 7 件は、個別ウィンドウへ不足機能を追加したうえで案内ラベル委譲へ移行済み。残る 2 件は対象外。

| レイヤー | レイヤー側 UI | 対応する個別ウィンドウ | 差分・注意点 | 対応 |
|---|---|---|---|---|
| CameraTimelineLayer「カメラ」 | 位置/回転/距離/FoV、対象設定 | CameraWindow `DrawMainCameraContent` | 編集項目はほぼ同じ。ただし個別側にタイムライン連携（キー登録）は無い | 対応済み（CameraWindow へメイドフォーカス行を追加のうえ委譲ラベル化） |
| SubCameraTimelineLayer「サブカメラ」（:230） | カメラ追加/削除、追従、FoV、ビューポート | CameraWindow（メイン/SceneView のみ） | サブカメラの管理 UI は個別側に存在しない → 被りは薄い | **対象外**（対応ウィンドウが無く、委譲には新規ウィンドウ開発と `SubCameraManager` のライフサイクル見直しが必要） |
| LightTimelineLayer「ライト」 | ライト選択、Transform、色、range/強度/角度、管理タブ | LightWindow | 編集項目はほぼ重複。ただし実体が別系統（レイヤー側は MTE の `StudioLightStat`、個別側は `StudioLightManager`）で、色補間等のレイヤー固有トグルは基底 `LightTimelineLayerBase` にある | 対応済み（LightWindow へ位置/ロール/影/メイド追従を追加のうえ操作タブを委譲ラベル化。管理タブは残置） |
| BGTimelineLayer「背景」 | 背景選択、背景 Transform | BackgroundWindow | 背景選択は被り。背景オブジェクトの Transform 編集は個別側に無い | 対応済み（BackgroundWindow へ `current_bg_object` のローカル Transform 行を追加のうえ委譲ラベル化） |
| BGColorTimelineLayer「背景色」 | カメラ背景色、地面色/位置/スケール | BackgroundWindow `DrawBgColorRow` | 背景色のみ被り。地面設定はレイヤー側固有 | 対応済み（地面の所有を `BGGroundManager` へ持ち上げ、背景色行の常時表示＋地面 UI を BackgroundWindow へ集約のうえ委譲ラベル化） |
| MotionTimelineLayer「メイドアニメ」の編集タブ（`DrawTransformEdit`） | ボーンごとの Transform / IK 編集 | BoneEditWindow ＋ MaidIKWindow ＋ InspectorWindow | 概念的には被るが、レイヤー側はキーフレーム対象カテゴリ・操作種類の切替が密結合 | 対応済み（IK 固定・接地を SE の `MaidIKHoldController` へ一本化し、MTE の `Timeline/IKHoldEntity.cs` を撤去。再生中の固定（旧 `isAnime`）は IK ウィンドウの「アニメ」トグルへ移設。拡張ボーンの対象選択はボーンウィンドウのチェック（追跡ストア）へ移行のうえ委譲ラベル化） |
| MoveTimelineLayer「メイド移動」 | メイド本体の位置/回転/スケール | （直接対応なし。ギズモ/InspectorWindow 操作） | 個別ウィンドウとしての被りは無いが、ギズモ操作で代替可能かは要検討 | 対応済み（Inspector とギズモが同じ `maid.transform` を編集済みのため委譲ラベル化。ローカル座標で記録される旨を注記） |
| EyesTimelineLayer「メイド瞳」の視線タブ（`DrawEyesLookAt`） | 注視先・視線編集 | MaidFaceWindow `DrawLookContent` | 視線・注視は被り。瞳位置/スケールの画像 UI（位置タブ）はレイヤー側固有 | 対応済み（MaidFaceWindow 視線タブへ「タイムライン視線」セクション（注視先・瞳回転）を追加のうえ委譲ラベル化。位置タブは残置） |
| BGModelTimelineLayer / ModelTimelineLayer の管理タブ（各 Base の `DrawModelManage`） | モデル追加/削除/複製/アタッチ | HierarchyWindow ＋ InspectorWindow ＋（モデル導入は ModItemExplorer 系） | 管理機能は個別ウィンドウに完全対応するものが無い → 被りは薄い | **対象外**（個別ウィンドウに完全対応する機能が無く被りが薄い） |

### C. 被りなし（レイヤー固有 UI）

> **対応状況（2026-08-29）:** サウンド系 2 レイヤーとライブ演出系 3 レイヤーは、受け皿となるウィンドウ（サウンド / ライブ演出）を新設して委譲済み。残りは対象外。

| レイヤー | 理由 | 対応 |
|---|---|---|
| AnimationTimelineLayer「メイドアニメブレンド」（:271） | アニメレイヤーブレンド設定は個別ウィンドウに存在しない | **対象外** |
| DressTimelineLayer「メイド衣装」（:197） | 初期値との差分表示が主。MaidUndressWindow の衣装変更とは目的が異なる | **対象外** |
| VoiceTimelineLayer「メイドボイス」（:105） | ボイス再生パラメータは固有 | **対応済み**（サウンドウィンドウを新設して委譲） |
| SeTimelineLayer「効果音」（:148） | SE 管理は固有 | **対応済み**（サウンドウィンドウを新設して委譲。再生状態は `TimelineSeManager` へ移管） |
| TextTimelineLayer「テキスト」（:211) | テキスト表示は固有 | **対象外** |
| StageLightTimelineLayer（:495）/ StageLaserTimelineLayer（:494）/ PsylliumTimelineLayer（:827） | MTE 固有オブジェクトで個別ウィンドウ側に対応機能なし | **対応済み**（ライブ演出ウィンドウを新設しライト / レーザー / サイリウムの 3 タブへ委譲） |
| PostEffectTimelineLayer（:234、partial 5 種） | ポストエフェクト編集は個別ウィンドウに無い（タイムライン設定ウィンドウの有効化のみ） | **対象外** |
| ModelTimelineLayer「モデル」の操作タブ（:219）/ BGModelTimelineLayer 操作タブ（:218） | モデル Transform 編集。InspectorWindow/ギズモで部分代替できるが直接の被りウィンドウ無し | **対象外** |
| PngPlacementTimelineLayer（:201） | PNG 配置は個別ウィンドウと完全に重複 | **対応済み**（案内ラベルのみ、PngPlacementWindow へ委譲済み） |

## 削除方針の示唆

1. **A 分類は PngPlacementTimelineLayer 方式（案内ラベル＋個別ウィンドウ委譲）へ移行済み**（BGModelMaterial を除く 8 レイヤー）。マテリアル 2 種・シェイプキー 2 種・モデルボーン・表情は、個別ウィンドウ側が追跡チェックでタイムラインと連携しているため機能欠損なく削除できた。
2. 指ブレンド・脱衣は個別ウィンドウ側に追跡チェックが無いが、キー書き込みが `GetBaseFinger` / `maidCache.IsSlotVisible` というライブ状態を読む経路のため、個別ウィンドウでの変更もそのままキー化される。追跡機構の追加は不要と判断して委譲済み。
3. **B 分類のうち 6 件（カメラ / ライト / 背景 / 背景色 / メイド移動 / メイド瞳の視線タブ）も委譲済み。** 単純委譲では機能が欠けるため、先に個別ウィンドウへ不足機能を移植してから案内ラベル化した。キー書き込み（`UpdateFrame`）はいずれもライブ状態を読むため、個別ウィンドウでの編集はそのままキー化される。実装時に判明した点:
   - 回転の 360 度跨ぎ対策は `TimelineLayerBase.GetAnmBinary` の `FixRotation` が前キー基準で行うため、レイヤー UI 側の連続角化を移設する必要はなかった。
   - `StudioLightStat.visible` はライト実体の `enabled` と同期していなかったため、ライトウィンドウの「有効」トグルがキー化されるよう同期させた。
   - 地面（`BGGround`）はレイヤーが所有していたため、ウィンドウからレイヤー非依存で編集できるよう `BGGroundManager` へ持ち上げた。
4. B 分類の残り 2 件（サブカメラ / モデル系管理タブ）は対象外。理由は上表を参照。
5. **C 分類のうちサウンド系（ボイス / 効果音）とライブ演出系（ステージライト / レーザー / サイリウム）の 5 レイヤーも委譲済み。** 既存ウィンドウに受け皿が無かったため、サウンドウィンドウ（`SoundWindow`）とライブ演出ウィンドウ（`LiveEffectWindow`）を新設している。実装時に判明した点:
   - 効果音の再生状態（SE 名 / 再生間隔 / ループ）はレイヤーの private フィールドが握っており、ウィンドウとキー書き込み（`UpdateFrame`）で共有できなかったため `TimelineSeManager` へ移管した。間欠再生もレイヤーの `LateUpdate` 驅動からマネージャの `Update` 驅動へ変えている（SE レイヤー未追加でもウィンドウからの再生が繰り返すようにするため）。
   - ライブ演出系の UI は `TimelineLayerBase` の protected ヘルパ（`DrawPosition` / `DrawEulerAngles`）に依存していたため、インスタンス状態を持たないものを `public static` へ開放して共有している。直前キーの角度を参照する部分（`GetPrevBone`）はレイヤー固有のため、ウィンドウ側で `TimelineManager.GetLayer<T>()` 経由に解決し、レイヤー未追加時は初期値へフォールバックする。
   - 両ウィンドウともタイムライン未ロード時は案内ラベルを出して編集を禁じる。ライブ演出側はアセットバンドル（`TimelineBundleManager.IsValid()`）も前提とする。
6. C 分類の残り（アニメブレンド / 衣装 / テキスト / ポストエフェクト / モデル操作タブ）はレイヤー編集ウィンドウにしか無い UI のため残す。

## 参照

- レイヤー側詳細: `Timeline/TimelineLayer/*.cs` の各 `DrawWindow`
- 個別ウィンドウ詳細: リポジトリ直下 `source/COM3D2.SceneEditor.Plugin/*Window.cs`
- 連携基盤: `TimelineControlWindow.cs`（キー登録）、`KeyFrameInspector.cs`（キー値編集）、各追跡ストア（`track.getStore().Mark/Unmark`）
