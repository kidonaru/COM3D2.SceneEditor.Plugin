# 設定リファレンス

設定は `Sybaris\UnityInjector\Config\SceneEditor.xml` に保存されます（初回起動時に自動生成）。
主な項目は設定ウィンドウ（メニューバー `Window` → `設定`）から変更できます。

## データの保存先

| 内容 | 場所 |
|---|---|
| 設定 | `Sybaris\UnityInjector\Config\SceneEditor.xml` |
| シーンプリセット | `Sybaris\UnityInjector\Config\SceneEditor\ScenePreset\` |
| ウィンドウレイアウト | `Sybaris\UnityInjector\Config\SceneEditor\WindowLayout\` |
| 表情プリセット | `Sybaris\UnityInjector\Config\SceneEditor\FacePreset\` |
| 指プリセット | `Sybaris\UnityInjector\Config\SceneEditor\FingerPreset\` |
| PNG 配置の画像 | `Sybaris\UnityInjector\Config\PngPlacement\`、`PhotoModeData\Texture\` |
| ポーズ | `PhotoModeData\MyPose\`（ゲーム標準のマイポーズと共通） |
| スクリーンショット | ゲームフォルダの `ScreenShot\` |

## 全般

| 項目 | 既定値 | 内容 |
|---|---|---|
| `pluginEnabled` | `true` | プラグイン全体の有効/無効 |
| `keyRepeatTimeFirst` | `0.15` | キーリピート開始までの秒数 |
| `keyRepeatTime` | `0.0333` | キーリピート間隔（秒） |
| `useHSVColor` | `false` | カラーピッカーを HSV 表示にする |
| `windowHoverColor` | 暗いグレー | ウィンドウのホバー色 |
| `backgroundColor` | `0.15, 0.15, 0.15` | GameView の外側とレターボックス余白の色 |
| `screenshotScale` | `1` | スクリーンショットの解像度倍率（1〜4） |
| `historyLimit` | `20` | 操作履歴の最大保持数（`0` で無効） |

## ウィンドウの配置

ウィンドウごとに以下の項目が保存されます。

| 項目 | 内容 |
|---|---|
| `<名前>PosX` / `<名前>PosY` | ウィンドウの位置（`-1` は画面中央） |
| `<名前>Width` / `<名前>Height` | ウィンドウのサイズ |
| `<名前>Visible` | モード開始時に開くか |

メニューバーは `menuBarPosX` / `menuBarPosY` のみ持ちます。

`<名前>` に入る値とウィンドウの対応は以下のとおりです。

| `<名前>` | ウィンドウ |
|---|---|
| `gameView` | GameView |
| `sceneView` | SceneView |
| `hierarchy` | Hierarchy |
| `inspector` | Inspector |
| `camera` | Camera |
| `background` | 背景 |
| `bgm` | BGM |
| `light` | ライト |
| `pngPlacement` | PNG配置 |
| `preset` | シーンプリセット |
| `history` | 操作履歴 |
| `setting` | 設定 |
| `maidCall` | メイド呼出 |
| `maidPose` | モーション |
| `maidFace` | 表情 |
| `maidFinger` | 指 |
| `maidIK` | IK |
| `maidUndress` | 脱衣 |
| `maidGravity` | 重力 |
| `boneEdit` | ボーン |

主なウィンドウの既定サイズは以下のとおりです。

| ウィンドウ | 既定サイズ |
|---|---|
| GameView | `960 × 560` |
| SceneView | `640 × 400` |
| Hierarchy | `260 × 480` |
| Inspector | `280 × 360` |

## グリッド

| 項目 | 既定値 | 内容 |
|---|---|---|
| `isGridVisible` | `true` | グリッド全体の表示 |
| `isGridVisibleOnlyEdit` | `true` | 編集モード中だけ表示する |
| `isGridVisibleInWorld` | `true` | 床（XZ 平面）グリッドの表示 |
| `isGridAxisVisible` | `true` | XYZ 軸線（赤/緑/青）の表示 |
| `gridCountInWorld` | `20` | 原点を中心とした 1 辺のマス数 |
| `gridCellSize` | `0.5` | 1 マスの大きさ（m） |
| `gridAlphaInWorld` / `gridColorInWorld` / `gridLineWidthInWorld` | `0.3` / 白 / `2` | 床グリッドの見た目 |
| `isGridVisibleInDisplay` | `false` | 画面分割グリッドの表示（ゲーム画面のみ） |
| `gridCountInDisplay` | `4` | 画面の分割数（`3` で三分割法） |
| `gridAlphaInDisplay` / `gridColorInDisplay` / `gridLineWidthInDisplay` | `0.3` / 白 / `1` | 分割線の見た目 |

## シーンプリセット

| 項目 | 既定値 | 内容 |
|---|---|---|
| `scenePresetSaveCamera` | `true` | 保存ダイアログの「カメラ」の既定状態 |
| `scenePresetSaveMaids` | `true` | 保存ダイアログの「メイド」の既定状態 |
| `scenePresetAutoLoadKey` | 空 | 事務所シーンで自動ロードするプリセット |
| `scenePresetAutoLoadOnceOnly` | `false` | 自動ロードをセッション中 1 回のみにする |

## キー設定

`keyBind` 要素で変更します。`Ctrl+Z` のように修飾キーを `+` でつなげて書けます。

| キー名 | 既定値 | 内容 |
|---|---|---|
| `PluginToggle` | `F10` | モード切替 |
| `EditModeToggle` | `Tab` | 編集モード切替 |
| `GizmoMove` | `Z` | ギズモを移動モードにする |
| `GizmoRotate` | `X` | ギズモを回転モードにする |
| `GizmoScale` | `C` | ギズモを拡縮モードにする |
| `Undo` | `Ctrl+Z` | 操作を戻す |
| `Redo` | `Ctrl+X` | 操作を進める |
