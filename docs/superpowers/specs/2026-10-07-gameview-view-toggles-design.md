# GameView 表示トグルの整理 設計

## 目的

SceneView / GameView / タイムライン操作ウィンドウのツールバーにある表示 ON/OFF の役割を整理する。GameView とタイムライン操作ウィンドウのトグルを「GameView（メインカメラ）の表示だけを制御する」同じ状態に揃え、Config に保存する。SceneView は SceneView 自身のトグルだけで決まる。

## 決定事項

| 項目 | 決定 |
|---|---|
| GameView ツールバーのトグル | 背景 / メイド / モデル / エフェクト / ギズモ の 5 つ。PNG は削除 |
| 効く範囲 | メインカメラだけ（通常表示・スクリーンショット・連番出力）。SceneView・サブカメラには効かない |
| 保存 | すべて Config に保存し、エディタ有効化時に全表示へ戻す処理はやめる |
| タイムライン操作ウィンドウ | 背景 / メイド / モデル / エフェクト / ギズモ。すべて GameView の同名トグルと同じ状態（メインカメラだけ・Config 保存）。操作対象メイドだけを消す仕様は廃止。ポスプロ同期はエフェクトへ統合して削除 |
| 並び順 | 3 ウィンドウとも表示トグルは 背景 → メイド → モデル → エフェクト → ギズモ。表示トグル以外は前後にまとめる（下表） |
| 地面色の背景連動 | タイムライン設定の「地面色表示を背景表示と連動」を削除（地面の表示は背景ウィンドウで制御する） |
| SceneView ツールバー | 背景 / メイド / モデル / エフェクト / ギズモ。エフェクトを追加し、ギズモ表示がボーン表示も兼ねるようになる（後述） |

## ツールバーの並び順

| ウィンドウ | 表示トグルより前 | 表示トグル | 表示トグルより後 |
|---|---|---|---|
| SceneView | — | 背景 / メイド / モデル / エフェクト / ギズモ | 平行投影 / 追従 |
| GameView | 撮影 / 比率 | 背景 / メイド / モデル / エフェクト / ギズモ | — |
| タイムライン操作 | 編集モード / 自動登録 | 背景 / メイド / モデル / エフェクト / ギズモ | カメラ同期 / 視野角固定 / フォーカス固定 |

アイコンは既存の `Bg` / `Maid` / `Model` / `PostEffect` / `Gizmo`。タイムライン操作ウィンドウのエフェクト・ギズモは常に表示する（ポスプロ同期のような「該当レイヤーがあるときだけ表示」はしない）。

## 各トグルの仕様

### 背景 / メイド / モデル

- 既存の `ViewCullingFilter`（メインカメラに付いたもの）の `hideBg` / `hideMaid` / `hideModel` を使う。メイドは全メイドをまとめて切り替える
- Config に `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel`（既定 true）を追加する
- サムネイル撮影はフィルタを無効にして撮るため、今まで通り全部写る

### エフェクト

OFF のとき、次の 2 つを GameView から外す。タイムライン操作ウィンドウのエフェクトも同じ状態で、旧「ポスプロ同期」を置き換える。Config に `gameViewShowEffect`（既定 true）を追加する。

1. **ポストエフェクト（PostEffects.Plugin のもの）**
   - 実体は PostEffects.Plugin がメインカメラに付けている。SceneView カメラには元から掛かっていないため、PostEffects.Plugin 全体を一時停止すれば「GameView だけ OFF」と同じ結果になる
   - PostEffects.Plugin に一時停止の口を足す（後述）。タイムラインの値・PostEffects の設定値には触れない
   - ゲーム本来の Bloom / DoF はゲーム設定どおり残る
   - 一時停止の反映は PostEffects.Plugin の LateUpdate なので、サムネイルもエフェクト無しで撮られる（見分け用なので許容）
   - PostEffects.Plugin が未導入・旧版（一時停止の口が無い）ときは、ポストエフェクト部分は何もしない。ライブ演出の非表示だけ効く
2. **ライブ演出（ステージライト / ステージレーザー / サイリウム）**
   - すべて MeshRenderer で描かれ、3 マネージャの GameObject 配下にまとまっている
   - `ViewCullingFilter` に `hideStageLight` / `hideStageLaser` / `hidePsyllium` を追加し、各マネージャ配下の Renderer を集めて既存の仕組み（60 フレームごとのキャッシュ再構築）で止める。種類ごとに分けるのは、編集中レイヤーの例外（後述）を種類単位で効かせるため
   - サムネイルには写る（フィルタが無効になるため）

#### 編集中レイヤーの例外（旧ポスプロ同期の挙動を引き継ぐ）

エフェクト OFF でも、タイムラインのカレントレイヤーがエフェクト系なら、その種類だけは GameView に表示する。エフェクトを隠して作業しつつ、編集中のエフェクトは見えるようにするため。

| カレントレイヤー | OFF 中も GameView に出すもの |
|---|---|
| ポストエフェクト（`PostEffectTimelineLayer`） | ポストエフェクト（PostEffects.Plugin の一時停止を解除） |
| ステージライト | ステージライト |
| ステージレーザー | ステージレーザー |
| サイリウム | サイリウム |

- `GameViewManager` がカレントレイヤーの種類を見て、毎フレーム（変化時のみ）フィルタのフラグと一時停止を当て直す。タイムライン未使用時は例外なし
- 各レイヤーは非表示に関わらず常に値を適用する（旧ポスプロ同期 OFF の `DisableAllEffects` / マネージャの `SetActive(false)` / `ApplyPlayData` の早期 return はやめる）

### SceneView のエフェクト

- SceneView ツールバーにエフェクトのトグルを追加する（アイコンは `PostEffect`、ギズモの前に置く）。Config に `sceneViewShowEffect`（既定 true）を追加し、他の SceneView トグルと同じく `SceneViewManager.ApplyViewSettings` で反映する
- 対象はライブ演出だけ。SceneView カメラの `ViewCullingFilter` の `hideStageLight` / `hideStageLaser` / `hidePsyllium` をまとめて立てる。GameView のエフェクトとは別の状態で、編集中レイヤーの例外も無い
- ポストエフェクトは SceneView カメラに元から掛かっていないため対象外（ON でもポストエフェクトは表示されない）

### ギズモ

- メニューバーの「ボーン表示」（`MaidManipulateManager.isBoneVisible`）と同じ状態。どちらで切り替えても両方に反映される。BoneEditWindow の切り替えも同じ状態を共有する
- 効くのは GameView だけ。GameView では今まで通り「編集モード中かつボーン表示 ON」で骨格線・ボーンギズモ・白丸ドラッグ点・オブジェクトギズモが出る
- Config に保存する（`gameViewShowGizmo`、既定 true）。起動時に `isBoneVisible` へ反映する
- **SceneView を `isBoneVisible` から切り離す**: SceneView の骨格線・ボーンギズモ・白丸ドラッグ点（表示とホバー判定）は「編集モード中かつ SceneView ツールバーのギズモ ON」で決める
  - その結果、ボーン表示 OFF でも SceneView ではボーンを選んで回せる。ボーン編集できる条件（`isBoneEditing`）はビューごとに判定する
  - ボーン表示 OFF にした瞬間に打ち切るドラッグは GameView 側のものだけにする

### タイムライン操作ウィンドウ

- `メイド表示` / `モデル表示` / `背景表示` は `GameViewManager.showMaid` / `showModel` / `showBg` を読み書きする。GameView ツールバーと同じ状態で、どちらで切り替えても両方に反映される。全カメラに効く今の仕組み（退避・SetActive）はやめる
- `メイド表示` は全メイドをまとめて切り替える。今の「操作対象のメイドだけを (100,0,0) へ退避」はタイムラインからは行わない。Inspector の各メイドの表示切替（退避方式、`SetVisibleByUser`）はそのまま残す
- `モデル表示`: `StudioModelManager.Visible` を削除する（タイムライン操作ウィンドウ以外に使用箇所なし）
- `背景表示`: `TimelineData.isBackgroundVisible` と `SceneEditorHack.SetBackgroundVisible` を削除する。BG レイヤー適用時の当て直し（`BGTimelineLayer`）とプラグイン無効化時の表示戻し（`TimelineData`）も不要になる
- `エフェクト` / `ギズモ` を追加する。`GameViewManager.showEffect` / `MaidManipulateManager.isBoneVisible` を読み書きする
- `ポスプロ同期` を削除する。`Timeline/Config.cs` の `isPostEffectSync` と `ConfigTag.ポスプロ同期`、`TimelineManager.hasNonCurrentPostEffectSyncLayer`、`TimelineLayerBase.UpdateLiveEffectSyncVisibility` / `RestoreLiveEffectSyncVisibility` と各ライブ演出レイヤー（ステージライト・レーザー・サイリウム）の呼び出し、`PostEffectTimelineLayer` の `DisableAllEffects` 分岐を削除する。旧タイムライン Config の要素は読み飛ばされる
- 並び順を 背景 / メイド / モデル / エフェクト / ギズモ にする
- 背景・メイド・モデルの見た目がタイムライン XML に依存しなくなる（GameView の Config 状態で決まる）

### 地面色の背景連動（削除）

- タイムライン設定の「地面色表示を背景表示と連動」（`TimelineData.isGroundLinkedToBackground`）を削除する。地面の表示は背景ウィンドウ `地面` タブ・地面色レイヤーのキーの `表示` だけで決まる
- `BGColorTimelineLayer` の「連動 ON かつ背景非表示なら地面も消す」処理を削除する
- 「個別設定を初期化」の対象からも外れる

### タイムライン XML の互換

- `TimelineXml.isBackgroundVisible` / `isGroundLinkedToBackground` はフィールドごと削除する。XmlSerializer は未知要素を読み飛ばすため、旧 XML はエラーにならず値が捨てられる。version は上げない
- 旧 XML で背景非表示・連動 ON だったものは、背景・地面が表示される（背景は GameView の背景トグル次第）
- SE で保存した XML を MTE で読むと要素が無いため既定値（背景表示 ON・連動 OFF）として読まれる
- `W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」に追記する

### PNG（削除）

PNG は Inspector の各 PNG の「表示」で切り替えられるため、GameView のトグルは削除する。合わせて GameView 専用だった次の処理も削除する。

- `GameViewManager.showPng`
- `ViewCullingFilter.hidePng` と PNG Renderer の収集（SceneView でも未使用）
- `PngPlacementManager.IsHiddenByGameView` / `HideAllProjectors`（SceneView 用の `HideOverlayProjectors` は残す）
- `ToolbarIcons.Kind.Png` はアイコン素材とセットなので残す

## PostEffects.Plugin 側の変更

別リポジトリ `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin`。

- `Manager/PostEffectManager.cs` に `suspended` フラグ（保存しない）を追加する。ユーザー操作の `effectsEnabled`（MainWindow の「有効」）とは別のフラグにして取り合わない。`LateUpdate` の適用条件を `effectsEnabled && !suspended && controller.effectEnabled` にする
- Hub 系（パラフィン / 距離フォグ / リムライト）は Restore で止まらないため、一時停止中は `PostEffectHub` が描かないようにする（`effectsEnabled` OFF でも描かれ続ける既存の穴も同時に塞ぐ）
- `TimelineBridge.cs` に `SetSuspended(bool)` / `IsSuspended()` を追加する

## SceneEditor 側の変更

- `MTEUtils/PostEffectsClient.cs`（共有 submodule）: `SetSuspended` / `IsSuspended` を任意メソッドとして解決する（`LoadStartupPreset` と同じく、旧版ホストでは null のまま接続は有効）。submodule 側で commit し、親リポの参照を進める
- `Config.cs`: `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel` / `gameViewShowEffect` / `gameViewShowGizmo` を追加
- `Manager/GameViewManager.cs`: `showBg` / `showMaid` / `showModel` / `showEffect` を Config 読み書きのプロパティにし、`ApplyCullingSettings` で反映する。エフェクトはライブ演出のフィルタと PostEffects の一時停止の両方を当てる。`EnterWindowMode` で全表示へ戻す処理をやめ、フィルタ作成後に保存値を当てる。ウィンドウモード終了・プラグイン無効化時は PostEffects の一時停止を解除する
- `Manager/ViewCullingFilter.cs`: `hideStageLight` / `hideStageLaser` / `hidePsyllium` の追加、`hidePng` の削除
- `Config.cs` / `SceneViewWindow.cs` / `Manager/SceneViewManager.cs`: `sceneViewShowEffect` の追加、SceneView ツールバーへのエフェクトトグル追加とライブ演出 3 フラグへの反映
- `GameViewWindow.cs`: ツールバーを 背景 / メイド / モデル / エフェクト / ギズモ に並べ替える（アイコンは既存の `Bg` / `Maid` / `Model` / `PostEffect` / `Gizmo`）。ツールバー幅の計算も合わせる。ギズモトグルは `isBoneVisible` を切り替える
- `MaidManipulation/MaidManipulateManager.cs` ほか（`BoneLineRenderer` / `BoneEditManager` / `SceneViewWindow` / `MaidDragPointRing` / `GizmoRenderer`）: SceneView 側の骨格線・ボーンギズモ・白丸の判定を `isBoneVisible` から SceneView のギズモ表示へ切り替える
- `PngPlacementManager.cs`: PNG 非表示処理の削除
- `TimelineControlWindow.cs`: 背景 / メイド / モデル / エフェクト / ギズモを `GameViewManager` / `isBoneVisible` の状態へ付け替え、並び替える。ポスプロ同期を削除
- `Timeline/Config.cs` / `Timeline/Manager/TimelineManager.cs` / `Timeline/TimelineLayer/TimelineLayerBase.cs` / `PostEffectTimelineLayer.cs` / `StageLightTimelineLayer.cs` / `StageLaserTimelineLayer.cs` / `PsylliumTimelineLayer.cs`: ポスプロ同期の削除
- `Manager/GameViewManager.cs`: カレントレイヤーに応じた編集中レイヤーの例外の適用
- `Timeline/TimelineData.cs` / `Timeline/TimelineXml.cs` / `Timeline/Hack/SceneEditorHack.cs` / `Timeline/TimelineLayer/BGTimelineLayer.cs` / `Timeline/TimelineLayer/BGColorTimelineLayer.cs` / `Timeline/Manager/StudioModelManager.cs` / `Timeline/Manager/TimelineManager.cs`（コメント）: 背景表示・地面連動・モデル表示の削除
- `TimelineSettingWindow.cs`: 「地面色表示を背景表示と連動」の削除

## ドキュメント

- `docs-site/guide/windows.md`: GameView ツールバーの表を 5 項目に更新。「非表示は保存されず…」を「保存され、次回も引き継がれます」に。エフェクトの範囲（PostEffects.Plugin のポストエフェクトとライブ演出、ゲーム本来の Bloom / DoF は残る、サムネイルの写り方）を書く
- `docs-site/guide/scene-view.md`: エフェクト（ライブ演出だけが対象、ポストエフェクトは SceneView には元から出ない）を追加。ギズモ表示がボーン表示も兼ね、メニューバーのボーン表示の影響を受けないことを書く
- `docs-site/guide/maid-editing.md`: ボーン表示が GameView だけに効くことを書く
- `docs-site/timeline/control.md`: 表示トグル（背景 / メイド / モデル / エフェクト / ギズモ）が GameView のトグルと同じ状態で、GameView だけに効き Config に保存されること。エフェクトの編集中レイヤーの例外。「背景表示はタイムラインに保存されます」とポスプロ同期の行・注記を削除
- `docs-site/timeline/settings.md`: 「地面色表示を背景表示と連動」の行と、「個別設定を初期化」の説明から背景表示・地面連動を削除
- `docs-site/timeline/layers-background.md`: 地面連動の注記を削除

## リスク

- 非表示を保存するため、隠したまま撮影する恐れがある。ツールバーのアイコン点灯（点灯 = 表示）で判別する
- `ViewCullingFilter` は 60 フレームごとにキャッシュを作り直すため、増えたライブ演出の Renderer が最大 1 秒ほど GameView に映ることがある（既存トグルと同じ制約）
- PostEffects.Plugin と SceneEditor の両方のリリースが要る。旧版 PostEffects.Plugin では一時停止できない
- ポスプロ同期 OFF は全カメラ（撮影含む）から消していたが、エフェクト OFF は GameView だけ。SceneView にはライブ演出が映る（SceneView のエフェクトで別途隠せる）
- ポストエフェクトの例外判定はカレントレイヤーだけを見るため、カレントがポストエフェクトなら PostEffects.Plugin 側で UI 設定したエフェクトも含めて全部出る
- タイムラインのメイド表示で操作対象だけを消す使い方はできなくなる（Inspector の各メイドの表示切替で代替）
- 旧タイムライン XML の背景非表示・地面連動の設定は失われる
- メイドを隠すとき、`CharacterShadowProxyManager`（キャラの影の複製）がメインカメラでどう見えるかを実機で確認する

## 確認

- 両プラグインを COM3D2 / COM3D25 の両構成で MSBuild ビルドする
- 実機（デイリー画面でエディタ有効化）で次を確認する
  - 各トグル OFF で GameView だけから消え、SceneView には映る
  - タイムライン操作ウィンドウと GameView のトグルが連動する（エフェクト・ギズモ含む）
  - エフェクト OFF でポストエフェクト / 各ライブ演出レイヤーをカレントにすると、その種類だけ GameView に出る
  - 3 ウィンドウの表示トグルの並びが揃っている
  - 背景非表示・地面連動 ON の旧タイムライン XML がエラーなく読める
  - SceneView のエフェクト OFF でライブ演出が SceneView だけから消え、GameView には映る
  - エディタの無効化・再有効化、ゲーム再起動後も状態が残る
  - エフェクト OFF でパラフィン・リムライト等も消え、ON で元の値に戻る。タイムライン再生中も OFF が保たれる
  - ボーン表示 OFF でも SceneView ではギズモ ON ならボーンが出て操作でき、SceneView のギズモ OFF で消える
  - スクリーンショットに非表示が効き、サムネイルには背景・メイド・モデル・ライブ演出が写る
