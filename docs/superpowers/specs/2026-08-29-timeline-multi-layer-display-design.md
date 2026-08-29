# タイムライン レイヤー複数表示 設計

作成: 2026-08-29 / 対象ブランチ: feature/timeline-window

## 目的

タイムラインウィンドウのレイヤー選択を複数選択できるようにし、選択したレイヤーを
すべてドープシートに縦に積んで表示する。各レイヤーの先頭にレイヤー名のカテゴリ行を
置き、レイヤーごとに折りたたみ可能にする。

## 決定事項（ユーザー確認済み）

| 論点 | 決定 |
|---|---|
| 選択単位 | レイヤーインスタンス単位（例: モーション(メイド1) とモーション(メイド2) は別項目） |
| 編集スコープ | アクティブレイヤー（currentLayer）のみ。従来どおり単一 |
| 選択 UI | メニューバーのポップアップと同じ流儀のチェック付きコンボ（クリックで閉じない） |
| 状態保存 | セッション内のみ（XML・Config へは保存しない） |
| カテゴリ行 | 左端の「ー/＋」で折りたたみ、レイヤー名クリックでアクティブ化 |

## 現状の構造（前提）

- `TimelineWindow.DrawTimeline` / `DrawBoneMenu` は `currentLayer` 単体を前提に、
  `BoneMenuManager.GetVisibleItems()`（= `currentLayer.allMenuItems` 由来）と
  `currentLayer.keyFrames` を描いている。
- レイヤー選択はボーンメニュー上部の `_layerComboBox`（レイヤー型単位の
  `TimelineLayerInfo` 一覧）→ `ChangeActiveLayer(layerType, maidSlotNo)`。
- 編集操作（キー追加/削除・コピペ・矩形選択・キードラッグ・カーブ編集・ポーズ編集）は
  すべて `currentLayer` に束縛されている。

## 設計

### 1. 選択モデル（セッション内のみ）

- `TimelineWindow` がビュー状態を保持する:
  - 表示レイヤー集合: `HashSet<ITimelineLayer>` 相当（インスタンス参照）
  - 折りたたみ集合: 同上
- **アクティブレイヤーは常に表示対象に含める**（集合に無くても表示時に補完）。
- レイヤー削除・タイムライン再読込で死んだ参照を掃除する
  （描画前に `timeline.layers` に存在するものだけ残すプルーニングで足りる）。
- タイムライン読み直し後は「アクティブレイヤーのみ表示・全展開」の初期状態に戻る。

### 2. 選択 UI（チェック付きコンボ）

- `_layerComboBox` をインスタンス一覧のマルチセレクト版に置き換える。
  - items: `timeline.layers`（使用中レイヤーインスタンス全件、layers の並び順）
  - 項目名: `✓ `（表示中）/ 空白 prefix + インスタンス表示名
  - インスタンス表示名: スロット付きレイヤーは「レイヤー名 (メイドN)」、
    スロット無しはレイヤー名のみ。表示名ヘルパーを新設する
  - クリック = 表示トグル。**ポップアップは閉じない**（メニューバー流儀）
- 実装は SE 側の派生クラス `GUIMultiSelectComboBox<T>`（`GUIComboBox<T>` を継承し
  `DrawPopupContent` をオーバーライド。トグル後 false を返して開いたままにする。
  外側クリックで閉じる挙動は `ComboBoxPopupWindow` の既存機構に任せる）。
  **MTEUtils（submodule）本体は変更しない。**
- コンボのボタン面はアクティブレイヤーの表示名 + 他に表示中があれば「 他N」。
- `-`（削除）/ `+`（追加）ボタンは現状維持（`-` はアクティブレイヤー型の全インスタンス
  削除のまま。挙動変更はスコープ外）。
- アクティブレイヤーの切替はカテゴリ行クリック（下記）で行う。

### 3. 行モデルとレンダリング

- 描画前に毎フレーム「行リスト」を組み立てる:
  - 表示レイヤーを `timeline.layers` の並び順で走査
  - 各レイヤーにつきカテゴリ行 1 行 + （展開中なら）そのレイヤーの可視ボーンメニュー行
  - `BoneMenuManager` にレイヤー引数版 `GetVisibleItems(ITimelineLayer)` を追加する
    （既存の引数無し版は currentLayer 委譲のまま残す）
- 行高は既存 `frameHeight` に統一し、背景タイル描画・スクロール計算・
  コンテンツ高さ算出（行数 × frameHeight）をそのまま流用する。
- `DrawBoneMenu` / `DrawTimeline` はこの行リストで描画する:
  - カテゴリ行（ボーンメニュー側）: 「ー/＋」（折りたたみトグル）+ レイヤー名。
    アクティブレイヤーは強調色。レイヤー名クリック → `SetCurrentLayer(layer)`
  - カテゴリ行（ドープシート側）: キーを描かない帯（背景色で区切りを示す）
  - ボーン行のキーフレーム描画は「行 → 所属レイヤー」対応で各レイヤーの
    `keyFrames` を描く
- 非アクティブレイヤーの扱い:
  - キーは表示のみ。選択ハイライトは出ない（選択状態はアクティブレイヤーにしか
    存在しない）。フルボーン未満の灰色化は従来ロジックを流用
  - キー/ボーン行クリックはまず `SetCurrentLayer(layer)` でアクティブ化してから
    選択処理へ入る（1 クリックで切替 + 選択）
  - 矩形選択・キードラッグの対象判定はアクティブレイヤーの行のみ
- ポーズ編集中の A/D ボタン（キー追加/削除）はアクティブレイヤーの行のみに出す。
- `isEasyEdit`（簡易表示）時は従来どおり単一レイヤー表示とし、複数表示・
  カテゴリ行は無効。

### 4. 影響範囲

- 変更:
  - `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（行モデル導入・描画の大部分）
  - `source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs`
    （`GetVisibleItems(ITimelineLayer)` 追加）
  - 新規: `GUIMultiSelectComboBox<T>`（SE 側。置き場は TimelineWindow と同階層か
    Timeline/ 配下）
  - 新規: レイヤーインスタンス表示名ヘルパー
- 不変: `TimelineManager` の currentLayer 機構・履歴・XML フォーマット・
  カーブエディタ・`TimelineLayerWindow`・キーバインド。
- テスト: 行リスト構築ロジックを描画から分離した純粋クラスにし、既存 xUnit
  プロジェクト（`source/COM3D2.SceneEditor.Plugin.Tests`）でユニットテストする
  （表示集合・折りたたみ・プルーニング・アクティブ補完の各ケース）。
  UI 描画・操作感は次回ゲーム起動時に実機確認。

## スコープ外

- 表示/折りたたみ状態の永続化（XML・Config）
- 複数レイヤー横断の編集操作（矩形選択・コピペ等）
- `-` 削除ボタンのインスタンス単位化
- DCM 連携・MTE 本体へのフィードバック
