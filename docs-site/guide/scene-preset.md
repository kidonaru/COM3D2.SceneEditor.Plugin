# シーンプリセット

メイド・カメラ・背景・ライト・BGM・PNG 配置をまとめて 1 つのプリセットとして保存し、
あとから丸ごと復元できます。

## 保存と適用

メニューバー `Window` → `プリセット` でプリセットウィンドウを開きます。

- 保存ダイアログでは「カメラ」「メイド」「背景」（背景・ライト・PNG 配置）と、
  連携プラグインが登録した項目から保存する要素を選べます
  （既定状態は設定項目 `scenePresetSaveCamera` / `scenePresetSaveMaids` / `scenePresetSaveBackground` で変更できます）
- 保存しなかった要素はプリセットに記録されず、適用時にも変更されません
- プリセットは `Sybaris\UnityInjector\Config\SceneEditor\ScenePreset\` に保存されます
- ウィンドウ上部の「読込:」トグルで、プリセット適用時に反映する要素を絞れます
  - `カメラ` / `メイド` / `背景`（背景・ライト・PNG 配置）の 3 つに加え、
    連携プラグインが登録した項目（モデル配置・ポストエフェクトなど）が並びます
  - `メイド` を OFF にすると、プリセットにメイドが含まれていても呼出も解除も行いません
  - このトグルは自動ロードと SceneCapture プリセットの適用にも効きます
  - トグルは一時的な絞り込みのため保存されず、ゲームを起動し直すと全 ON に戻ります

## SceneCapture プリセットの読み込み

SceneCapture プラグインのプリセット（`Sybaris\UnityInjector\Config\SceneCapture\Presets\*.xml`）がある場合、
一覧に `SceneCapture` という読み込み専用の仮想フォルダが表示されます。

- 中のプリセットをクリックすると、SceneEditor 形式へ変換して適用されます
- サムネイルを持たないため、タイルではなくリスト表示になります
- 読み込み専用なので保存はできません（`保存` ボタンは無効化表示になり、削除ボタンは出ません）
- 「読込:」トグルによる絞り込みはここでも効きます
- メイド（配置・ポーズ・表情）は SceneCapture プリセットに含まれないため、適用しても変化しません
- モデル配置・ポストエフェクトの復元には、対応する連携プラグインの導入が必要です

## 自動ロード

事務所シーンに入ったとき、指定したプリセットを自動で適用できます。
対象はプリセット一覧のタイルに表示されるホームアイコンで指定します（指定中はアイコンがアクセント色になります）。

| 設定項目 | 内容 |
|---|---|
| `scenePresetAutoLoadKey` | 事務所シーンで自動ロードするプリセット |
| `scenePresetAutoLoadOnceOnly` | 自動ロードをセッション中 1 回のみにする |

## 外部プラグインとの連携

シーンプリセットには他プラグインが独自のデータを相乗りできる公開 API があります。

現在、以下のプラグインが連携に対応しています（[インストール](/guide/installation#推奨プラグイン連携対応)も参照）。

| プラグイン | 保存/復元される内容 | 対応バージョン |
|---|---|---|
| [COM3D2.ModItemExplorer.Plugin](https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin/releases) | モデル配置 | v2.0.0.0 以降 |
| [COM3D25.PostEffects.Plugin](https://github.com/kidonaru/COM3D25.PostEffects.Plugin/releases) | ポストエフェクト設定 | v2.0.0.0 以降 |

開発者向けの詳細は
[シーンプリセット連携ガイド](/dev/scene-preset-provider-guide)
を参照してください。
