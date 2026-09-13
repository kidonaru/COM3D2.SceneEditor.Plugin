# タイムライン

キーフレームを打って、メイドのモーション・表情・カメラ・ライト・モデル・演出を時間軸で編集し、再生・.anm 出力・連番画像出力まで行える機能です。
MotionTimelineEditor（MTE）のタイムラインを SceneEditor の内部ウィンドウとして統合したもので、MTE で作成したタイムライン XML をそのまま読み込めます。

::: warning MTE との互換は一方向です
MTE のタイムラインを SceneEditor で開くことはできますが、SceneEditor で保存したタイムラインを MTE で開くことはできません。
MTE の元データは別名で控えてから開いてください。詳しくは[MTE 互換と制限事項](/timeline/compatibility)を参照してください。
:::

## 関連ウィンドウ

すべてメニューバー `Window` から開きます。

| ウィンドウ | 役割 | 解説ページ |
|---|---|---|
| `タイムライン` | キーフレームのグリッド本体。レイヤー・項目の選択とキーの配置 | [タイムラインウィンドウ](/timeline/window) |
| `タイムライン操作` | ファイル操作、再生、キーフレーム登録、範囲操作、各種トグル | [タイムライン操作ウィンドウ](/timeline/control) |
| `タイムラインロード` | 保存済みタイムラインのサムネイル一覧 | [ファイル管理](/timeline/files) |
| `テンプレート` | 選択キーフレームの型を保存して再利用 | [ファイル管理](/timeline/files#テンプレート) |
| `タイムライン設定` | フレームレート、共通設定、トラック | [タイムライン設定](/timeline/settings) |

キーフレームの値の編集は Inspector ウィンドウで行います（[キーフレーム詳細](/timeline/inspector)）。
補間カーブはタイムラインウィンドウ下部のカーブエディタで編集します（[補間とカーブエディタ](/timeline/interpolation)）。

## 基本の流れ

1. メイドを配置し、`タイムライン操作` の `ファイル` → `新規作成` でタイムラインを作る
2. 必要なレイヤーを `+` で追加する（[レイヤー](/timeline/layers)）
3. `編集モード`（`F1`）に入り、フレームを選んでポーズやカメラ、ライトなどを操作する
4. `登録`（`Enter`）でキーフレームを打つ。`自動登録` が有効なら各ウィンドウの操作が自動でキーになる（[キーフレーム編集](/timeline/editing)）
5. `再生`（`Space`）で確認する（[再生とシーク](/timeline/playback)）
6. `セーブ` で保存する。必要なら `アニメ出力` や `連番画像出力` を行う（[ファイル管理](/timeline/files)）

## ページ一覧

- [タイムラインウィンドウ](/timeline/window) — 画面構成、ボーンメニュー、グリッド操作、表示モード
- [タイムライン操作ウィンドウ](/timeline/control) — ファイル、再生、キーフレーム操作、範囲操作、トグル
- [キーフレーム編集](/timeline/editing) — 編集モード、登録、自動登録、変更追跡、コピー、複数メイド、履歴
- [再生とシーク](/timeline/playback) — 再生・停止、フレーム移動、ループ、速度、トラック、BGM / 動画同期
- [補間とカーブエディタ](/timeline/interpolation) — タンジェント、プリセット、カーブエディタ
- [キーフレーム詳細](/timeline/inspector) — Inspector での値編集と補間曲線
- [レイヤー](/timeline/layers) — カテゴリと全レイヤー一覧、追加・削除、レイヤーゲート
  - [メイド系レイヤー](/timeline/layers-maid)
  - [カメラ系レイヤー](/timeline/layers-camera)
  - [モデル系レイヤー](/timeline/layers-model)
  - [背景系レイヤー](/timeline/layers-background)
  - [ライト・演出系レイヤー](/timeline/layers-effect)
  - [その他のレイヤーと BGM / 動画](/timeline/layers-other)
- [ファイル管理](/timeline/files) — 保存、読込、アンロード、.anm 出力、連番画像出力、テンプレート
- [タイムライン設定](/timeline/settings) — 個別 / 共通 / トラックの各タブと設定ファイル
- [ショートカット](/timeline/shortcuts) — タイムライン関連のキー一覧
- [MTE 互換と制限事項](/timeline/compatibility)
