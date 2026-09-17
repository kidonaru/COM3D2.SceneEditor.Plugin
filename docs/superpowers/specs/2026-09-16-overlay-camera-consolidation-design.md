# オーバーレイカメラの集約とグリッドのポストエフェクト回避 設計メモ

## 背景

グリッド (`GridRenderer`)・ギズモ (`GizmoRenderer`)・骨格線 (`BoneLineRenderer`) は
GameView ではゲームの **メインカメラ** に付けて `OnPostRender` で GL 描画している。
`OnPostRender` は `OnRenderImage` より前に走るため、ゲーム側の Bloom 等と
PostEffects.Plugin のポストエフェクトが編集用の線に乗ってしまう
(被写界深度でグリッドがぼける、パラフィンで色が変わる等)。

字幕は同じ理由で既に専用カメラ (`TimelineTextCamera`) を立てて回避している。
その結果、プラグインが作るオーバーレイ系カメラの所有・同期が複数箇所に散っている。

| カメラ | 所有者 | メインカメラへの追随 |
|---|---|---|
| `SceneEditorClearCamera` | `GameViewManager` | なし (モード切替で生成・破棄) |
| `MTEFrontCamera` | `MTEP.CameraManager` | `targetTexture` のみ `GameViewManager.SyncFrontCameraTarget` |
| `TimelineTextCamera` | `MTEP.TimelineTextManager` | `targetTexture` / `depth` / `allowHDR` を自前の `LateUpdate` |

`ScreenshotManager.AddExtraCameras` は front と text を個別に手で列挙しており、
カメラを増やすたびに撮影側へ追記が要る。

## 決定事項

### 1. 床グリッドの遮蔽は諦め、編集用の線は常に最前面に描く

ポストエフェクト後に別カメラで描くとメインカメラの深度バッファは参照できない
(イメージエフェクトの出力は色だけがブリットされる)。
深度テクスチャ + カスタムシェーダで遮蔽を再現する案はアセットバンドル更新が要るため採らない。
床グリッドの深度テストは外し、ギズモ・骨格線と同じく常に最前面に描く。

### 2. `MTEP.CameraManager` をプラグインのオーバーレイカメラ全部の所有者にする

新クラスは作らない。既に front カメラを持ち、manager リストに載っていて
`OnPluginEnable` / `OnPluginDisable` を持つ `CameraManager` を拡張する。

| カメラ | GameObject 名 | 用途 | depth | 生成・破棄 |
|---|---|---|---|---|
| clear | `SceneEditorClearCamera` | GameView 背景色クリア (`cullingMask = 0`, SolidColor) | -100 | `SetClearCameraActive(bool active, Color color)`。GameViewManager がウィンドウモード開始/終了・最大化切替で呼ぶ |
| front | `SceneEditorFrontCamera` (旧 `MTEFrontCamera`) | 最前面動画・レターボックス | 5 | 現行どおり `OnPluginEnable` で生成、`OnPluginDisable` で破棄 |
| text | `SceneEditorTextCamera` (旧 `TimelineTextCamera`) | 字幕キャンバス (UI レイヤー) | main + 100 | `textCamera` getter で遅延生成。`OnPluginDisable` で破棄 |
| gizmo | `SceneEditorGizmoCamera` (新設) | グリッド・ギズモ・骨格線の `OnPostRender` GL 描画 | main + 200 | `OnPluginEnable` で生成、`OnPluginDisable` で破棄 |

gizmo カメラの設定: `cullingMask = 0`、`clearFlags = Depth`、`allowHDR` はメインへ追随、
位置は原点で構わない (何も映さず、描画行列は viewCamera から取る)。

公開 API:

- `Camera clearCamera / frontCamera / textCamera / gizmoCamera` (front/text は既存の遅延生成を維持)
- `Camera createdFrontCamera` など「生成を伴わない参照」は現行どおり残す
- `bool IsOverlayCamera(Camera cam)`: GameViewManager の UI カメラ隠蔽から除外する判定に使う
- `void GetOverlayCameras(List<Camera> cameras)`: enabled な front / text / gizmo を足して depth 昇順に並べる。ScreenshotManager がこれを使う

追随ルール (`LateUpdate` で 4 台まとめて実施):

- `targetTexture` はメインカメラの `targetTexture` と同じにする (参照同一性で比較)。
  現行の front の「ウィンドウ化中は GameView RT、最大化中・モード外は null」はこの規則と同値
- `depth` は表の値。text / gizmo はメインの depth を基準にオフセット
- `allowHDR` はメインへ揃える (HDR 不一致で黒点が焼き付く既知の問題への対処を全カメラに広げる)
- clear カメラは追随の対象外。メインカメラが RT へ逃げている間に画面 (バックバッファ) を
  エディタの背景色で塗る役目なので `targetTexture` は常に null のまま

TimelineTextManager はキャンバスだけを持ち、`canvas.worldCamera = cameraManager.textCamera` とする。
自前の `UpdateRenderTarget` と `_cameraObject` は削除する。
`TimelineIntegration` の manager 順 (MovieManager → CameraManager → TimelineTextManager) は、
TimelineTextManager の `OnPluginDisable` がキャンバスだけを壊すので現行のままでよい。

### 3. 描画コンポーネントは「ホスト」と「視点」を分ける

`GridRenderer` / `GizmoRenderer` / `BoneLineRenderer` に `Camera viewCamera` を追加する。

- 行列 (`projectionMatrix` / `worldToCameraMatrix`)、ピッキング (`ScreenPointToRay` 系)、
  線幅・ギズモサイズ計算 (`pixelHeight` / `fieldOfView` / `transform`) は全て `viewCamera` を使う
- 未設定なら `GetComponent<Camera>()` を使う。SceneView は今までどおりホスト = 視点
- GameView では gizmo カメラをホストにし、`viewCamera` にメインカメラを入れる
- `GizmoRenderer.DrawMainCameraFrustum` の「自分がメインカメラなら描かない」判定は `viewCamera == mainCamera` にする
- `GridRenderer` の床グリッド用マテリアルは深度テストを外す (`ZTest Always`)。
  ワールド/オーバーレイの 2 マテリアルは深度設定が同じになるので 1 つにまとめてよい

### 4. GameViewManager の変更

- `AttachGizmoRenderer` はメインカメラではなく `cameraManager.gizmoCamera.gameObject` へ 3 コンポーネントを付け、`viewCamera` にメインカメラを渡す
- `CreateClearCamera` / `DestroyClearCamera` は `cameraManager.SetClearCameraActive` へ委譲。背景色変更時も同 API で色を渡す
- `SyncFrontCameraTarget` とその呼び出しは削除
- `HideUICameras` の除外判定 (`cam == _clearCamera`、`cam == createdFrontCamera`) は `cameraManager.IsOverlayCamera(cam)` にまとめる

### 5. ScreenshotManager の変更

`AddExtraCameras` は `cameraManager.GetOverlayCameras(cameras)` を呼ぶだけにする。
gizmo カメラも列挙されるので、スクショ・連番出力にはグリッド・ギズモが写る。
これは現行 (メインカメラの `OnPostRender` で描くため写っていた) と同じ挙動。

## 影響範囲

| ファイル | 変更 |
|---|---|
| `Timeline/Manager/CameraManager.cs` | clear / text / gizmo カメラの所有、追随、列挙 API |
| `Timeline/Manager/TimelineTextManager.cs` | カメラ所有と `UpdateRenderTarget` を削除、`textCamera` を借りる |
| `Manager/GameViewManager.cs` | 上記 4 |
| `Manager/ScreenshotManager.cs` | 上記 5 |
| `Manager/GridRenderer.cs` / `GizmoRenderer.cs` / `BoneLineRenderer.cs` | `viewCamera` 導入、床グリッドの深度テスト除去 |
| `Timeline/LetterBoxView.cs` / `Timeline/MoviePlayerImpl.cs` | `frontCamera` 参照のみで実質無変更 (GameObject 名は参照していない) |

SceneView カメラと PIP サブカメラ (`SubCameraManager`) は触らない。

## 検証

- 単体テスト (`source/COM3D2.SceneEditor.Plugin.Tests`): `viewCamera` 未設定時にホストへフォールバックすること、
  `GetOverlayCameras` が enabled なカメラだけを depth 昇順で返すこと
- 実機 (devbridge): ポストエフェクト ON (被写界深度・パラフィン) でグリッド・ギズモ・骨格線に効果が乗らないこと、
  字幕・最前面動画・レターボックスが従来どおり GameView 内と最大化・モード外で見えること、
  スクショ・連番出力に全て写ること、プラグイン無効化 → 再有効化で 4 カメラが復帰すること、
  SceneView の描画が変わらないこと
