# GameView 表示トグルの整理 設計

## 目的

SceneView / GameView / タイムライン操作ウィンドウのツールバーにある表示 ON/OFF の役割を整理する。GameView のツールバーを「GameView（メインカメラ）の表示だけを制御する場所」として揃え、状態を Config に保存する。

## 決定事項

| 項目 | 決定 |
|---|---|
| GameView ツールバーのトグル | 背景 / メイド / モデル / エフェクト / ギズモ の 5 つ。PNG は削除 |
| 効く範囲 | メインカメラだけ（通常表示・スクリーンショット・連番出力）。SceneView・サブカメラには効かない |
| 保存 | すべて Config に保存し、エディタ有効化時に全表示へ戻す処理はやめる |
| タイムライン操作ウィンドウ | 変更しない。メイド表示 / モデル表示 / 背景表示は今まで通り全カメラに効き、GameView とは別の状態 |
| SceneView ツールバー | 背景 / メイド / モデル / エフェクト / ギズモ。エフェクトを追加し、ギズモ表示がボーン表示も兼ねるようになる（後述） |

## 各トグルの仕様

### 背景 / メイド / モデル

- 既存の `ViewCullingFilter`（メインカメラに付いたもの）の `hideBg` / `hideMaid` / `hideModel` を使う。メイドは全メイドをまとめて切り替える
- Config に `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel`（既定 true）を追加する
- サムネイル撮影はフィルタを無効にして撮るため、今まで通り全部写る

### エフェクト

OFF のとき、次の 2 つを GameView から外す。タイムラインの「ポスプロ同期」とは独立した状態。Config に `gameViewShowEffect`（既定 true）を追加する。

1. **ポストエフェクト（PostEffects.Plugin のもの）**
   - 実体は PostEffects.Plugin がメインカメラに付けている。SceneView カメラには元から掛かっていないため、PostEffects.Plugin 全体を一時停止すれば「GameView だけ OFF」と同じ結果になる
   - PostEffects.Plugin に一時停止の口を足す（後述）。タイムラインの値・PostEffects の設定値には触れない
   - ゲーム本来の Bloom / DoF はゲーム設定どおり残る
   - 一時停止の反映は PostEffects.Plugin の LateUpdate なので、サムネイルもエフェクト無しで撮られる（見分け用なので許容）
   - PostEffects.Plugin が未導入・旧版（一時停止の口が無い）ときは、ポストエフェクト部分は何もしない。ライブ演出の非表示だけ効く
2. **ライブ演出（ステージライト / ステージレーザー / サイリウム）**
   - すべて MeshRenderer で描かれ、3 マネージャの GameObject 配下にまとまっている
   - `ViewCullingFilter` に `hideLiveEffect` を追加し、3 マネージャ配下の Renderer を集めて既存の仕組み（60 フレームごとのキャッシュ再構築）で止める
   - サムネイルには写る（フィルタが無効になるため）

### SceneView のエフェクト

- SceneView ツールバーにエフェクトのトグルを追加する（アイコンは `PostEffect`、ギズモの前に置く）。Config に `sceneViewShowEffect`（既定 true）を追加し、他の SceneView トグルと同じく `SceneViewManager.ApplyViewSettings` で反映する
- 対象はライブ演出だけ。SceneView カメラの `ViewCullingFilter.hideLiveEffect` で止める。GameView のエフェクトとは別の状態
- ポストエフェクトは SceneView カメラに元から掛かっていないため対象外（ON でもポストエフェクトは表示されない）

### ギズモ

- メニューバーの「ボーン表示」（`MaidManipulateManager.isBoneVisible`）と同じ状態。どちらで切り替えても両方に反映される。BoneEditWindow の切り替えも同じ状態を共有する
- 効くのは GameView だけ。GameView では今まで通り「編集モード中かつボーン表示 ON」で骨格線・ボーンギズモ・白丸ドラッグ点・オブジェクトギズモが出る
- Config に保存する（`boneVisible`、既定 true）。起動時に `isBoneVisible` へ反映する
- **SceneView を `isBoneVisible` から切り離す**: SceneView の骨格線・ボーンギズモ・白丸ドラッグ点（表示とホバー判定）は「編集モード中かつ SceneView ツールバーのギズモ ON」で決める
  - その結果、ボーン表示 OFF でも SceneView ではボーンを選んで回せる。ボーン編集できる条件（`isBoneEditing`）はビューごとに判定する
  - ボーン表示 OFF にした瞬間に打ち切るドラッグは GameView 側のものだけにする

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
- `Config.cs`: `gameViewShowBg` / `gameViewShowMaid` / `gameViewShowModel` / `gameViewShowEffect` / `boneVisible` を追加
- `Manager/GameViewManager.cs`: `showBg` / `showMaid` / `showModel` / `showEffect` を Config 読み書きのプロパティにし、`ApplyCullingSettings` で反映する。エフェクトはライブ演出のフィルタと PostEffects の一時停止の両方を当てる。`EnterWindowMode` で全表示へ戻す処理をやめ、フィルタ作成後に保存値を当てる。ウィンドウモード終了・プラグイン無効化時は PostEffects の一時停止を解除する
- `Manager/ViewCullingFilter.cs`: `hideLiveEffect` の追加、`hidePng` の削除
- `Config.cs` / `SceneViewWindow.cs` / `Manager/SceneViewManager.cs`: `sceneViewShowEffect` の追加、SceneView ツールバーへのエフェクトトグル追加と `hideLiveEffect` への反映
- `GameViewWindow.cs`: ツールバーを 背景 / メイド / モデル / エフェクト / ギズモ に並べ替える（アイコンは既存の `Bg` / `Maid` / `Model` / `PostEffect` / `Gizmo`）。ツールバー幅の計算も合わせる。ギズモトグルは `isBoneVisible` を切り替える
- `MaidManipulation/MaidManipulateManager.cs` ほか（`BoneLineRenderer` / `BoneEditManager` / `SceneViewWindow` / `MaidDragPointRing` / `GizmoRenderer`）: SceneView 側の骨格線・ボーンギズモ・白丸の判定を `isBoneVisible` から SceneView のギズモ表示へ切り替える
- `PngPlacementManager.cs`: PNG 非表示処理の削除

## ドキュメント

- `docs-site/guide/windows.md`: GameView ツールバーの表を 5 項目に更新。「非表示は保存されず…」を「保存され、次回も引き継がれます」に。エフェクトの範囲（PostEffects.Plugin のポストエフェクトとライブ演出、ゲーム本来の Bloom / DoF は残る、サムネイルの写り方）を書く
- `docs-site/guide/scene-view.md`: エフェクト（ライブ演出だけが対象、ポストエフェクトは SceneView には元から出ない）を追加。ギズモ表示がボーン表示も兼ね、メニューバーのボーン表示の影響を受けないことを書く
- `docs-site/guide/maid-editing.md`: ボーン表示が GameView だけに効くことを書く
- `docs-site/timeline/control.md`: タイムラインの表示トグルは全カメラに効き、GameView のトグルとは別の状態であることを書く

## リスク

- 非表示を保存するため、隠したまま撮影する恐れがある。ツールバーのアイコン点灯（点灯 = 表示）で判別する
- `ViewCullingFilter` は 60 フレームごとにキャッシュを作り直すため、増えたライブ演出の Renderer が最大 1 秒ほど GameView に映ることがある（既存トグルと同じ制約）
- PostEffects.Plugin と SceneEditor の両方のリリースが要る。旧版 PostEffects.Plugin では一時停止できない
- メイドを隠すとき、`CharacterShadowProxyManager`（キャラの影の複製）がメインカメラでどう見えるかを実機で確認する

## 確認

- 両プラグインを COM3D2 / COM3D25 の両構成で MSBuild ビルドする
- 実機（デイリー画面でエディタ有効化）で次を確認する
  - 各トグル OFF で GameView だけから消え、SceneView には映る
  - SceneView のエフェクト OFF でライブ演出が SceneView だけから消え、GameView には映る
  - エディタの無効化・再有効化、ゲーム再起動後も状態が残る
  - エフェクト OFF でパラフィン・リムライト等も消え、ON で元の値に戻る。タイムライン再生中も OFF が保たれる
  - ボーン表示 OFF でも SceneView ではギズモ ON ならボーンが出て操作でき、SceneView のギズモ OFF で消える
  - スクリーンショットに非表示が効き、サムネイルには背景・メイド・モデル・ライブ演出が写る
