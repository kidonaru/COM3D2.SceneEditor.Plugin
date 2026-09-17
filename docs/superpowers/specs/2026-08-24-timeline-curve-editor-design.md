# タイムラインカーブエディタ設計 (2026-08-24)

## 目的

Inspector (`KeyFrameInspector`) 内の補間曲線 UI (`DrawTangent`/`DrawEasing`) を廃止し、
一般的なタイムライン形式の**実値カーブエディタ**(Unity の Curves 表示相当)を
`TimelineWindow` 下部の分割ペインとして新設する。
あわせて補間モデルを Tangent に一本化し、Easing ベースの補間は廃止する
(旧データはロード時に Tangent へ近似変換)。

## 決定事項

| 論点 | 決定 |
|---|---|
| プロット内容 | 実値カーブ (チャンネルの実際の値を縦軸にプロット) |
| 配置 | TimelineWindow 下部の分割ペイン (折りたたみ・高さリサイズ可) |
| 編集操作 | タンジェントハンドルのドラッグ + キー点の上下ドラッグ (値変更)。フレーム移動は既存ドープシート側に任せる |
| Inspector 側 | 曲線 UI (DrawTangent/DrawEasing) を丸ごと削除。数値 Transform 編集は残す |
| Easing | 廃止して Tangent に統一。旧フォーマット (MTE 産含む) はロード時に近似変換 |
| MTE 互換 | **MTE → SE の一方向のみ考慮**。SE で保存したファイルを本家 MTE で開くと easing ハードコードのレイヤーが線形補間になるのは許容 |
| 近似精度 | 端点微分ベースの近似で可 (Quint/Exp 系の完全一致は要求しない) |

## 前提となる調査結果

- 全 `ValueData` は既に in/out `TangentData` を常時保持し、XML (`TransformXml`) にも
  `inTangents`/`outTangents` がシリアライズ済み。`hasEasing` は UI/再生経路の選択フラグにすぎない
- easing を消費するレイヤーは約 20 ファイル。うち Move/Camera 等 7 種は
  `timeline.isTangentXXX` フラグで Tangent 再生経路を既に持つ。
  ShapeKey/Eyes/PostEffect 系など 10 種以上は easing 再生がハードコード
- `MoveEasingType` は全 22 種が単調イージング (Bounce/Elastic なし) のため
  端点微分 → 正規化 Tangent への写像で近似可能
- 再生は `PlayDataBase.Update` が lerpFrame (0-1) を出し、各レイヤーの Apply が
  `CalcEasingValue` または `PluginUtils.HermiteXXX` で補間する構造

## Phase A — カーブエディタ本体

### 新規コンポーネント `TimelineCurveEditor`

`KeyFrameInspector` と同様のシングルトンビュークラス (非ウィンドウ)。
`TimelineWindow.DrawTimeline` のキーフレームグリッド下に描画する。

- **ペイン**: 折りたたみトグル + 境界ドラッグで高さリサイズ。高さ・折りたたみ状態は Config に永続化
- **横軸**: ドープシートとフレームスケール (px/frame)・横スクロール位置を完全共有
- **縦軸**: 表示中チャンネルの値域に余白付き自動フィット。目盛りラベルを描画
- **表示対象**: 選択中キーフレームのボーンのチャンネル
  (位置 XYZ / 回転 XYZ / 拡縮 / 色 / カスタム値)。
  既存 `TangentValueType` コンボで種別フィルタ + チャンネル別カラー (X=赤, Y=緑, Z=青系)
- **描画方式**: 補間関数をピクセル列サンプリングし 1px テクスチャのセグメント描画。
  毎フレームの Texture2D 再生成はしない

### 編集操作

- キー点の上下ドラッグ → 値変更。ドラッグ中はプレビュー適用、
  マウスアップで確定 (`ApplyCurrentFrame` + 履歴記録)
- 選択キーのタンジェントハンドルドラッグ → スクリーン座標から正規化 Tangent へ逆変換して適用
- ツールバー: プリセット 4 種・自動補間トグル (Inspector から移植)、
  チャンネル表示トグル、(Phase A 暫定) Easing コンボ

### Inspector からの削除

`KeyFrameInspector` から `DrawTangent`/`DrawEasing` と関連フィールド
(tangentTex/easingTex/プリセットテクスチャ/easingComboBox/_tangentValueTypeComboBox 等) を削除。
数値 Transform 編集 (DrawTransform/DrawCustomValues/DrawStrValues) は残す。

### Phase A 時点の easing レイヤー

カーブ表示 (easing 関数で描画) + キー点の値ドラッグ + ツールバーの Easing コンボ選択。
タンジェントハンドルは出さない。

## Phase B — Easing 廃止・Tangent 統一

- **ロード時変換**: XML ロード後、easing ベースの区間は easing 関数の端点微分から
  in/out Tangent を近似算出 (正規化域にクランプ) して書き込み、easing は 0 (Linear) へ。
  MTE 産ファイルも同じ経路で自動変換
- **モデル統一**: `hasEasing` を全廃 (全 TransformData で `hasTangent = true` 相当)。
  `isTangentXXX` フラグは XML 読み込み互換のため定義は残すが動作は常に Tangent
- **再生経路**: easing 消費の約 20 レイヤーの Apply を、Move/Camera レイヤーの
  Tangent 分岐 (`PluginUtils.HermiteVector3` 等) と同じ形へ書き換え
- **UI**: Easing コンボと `MoveEasingType` 依存 UI を撤去
  (enum 自体は変換テーブルとして残す)
- **保存**: MTE フォーマットのまま (easing=0 + Tangent 値)。SE で読み戻せば完全再現

## テスト

既存 xUnit スイート (`source/COM3D2.SceneEditor.Plugin.Tests`、70 件) に追加:

1. easing→Tangent 近似変換: 全 `MoveEasingType` をサンプリングし、
   変換後 Hermite との誤差が閾値以下であること
2. XML 往復: Tangent 統一後もゴールデン往復テストが通ること
   (easing フィクスチャはロード時変換を通した結果を検証)
3. カーブビューのスクリーン座標⇔値マッピング関数 (純粋ロジックに切り出す) の単体テスト

## スコープ外

- キー点の左右ドラッグ (フレーム移動) — ドープシート側の既存機能に任せる
- 縦軸の手動ズーム/パン — 自動フィットのみ
- SE → MTE 方向の互換維持
