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

## 自動ロード

事務所シーンに入ったとき、指定したプリセットを自動で適用できます。

| 設定項目 | 内容 |
|---|---|
| `scenePresetAutoLoadKey` | 事務所シーンで自動ロードするプリセット |
| `scenePresetAutoLoadOnceOnly` | 自動ロードをセッション中 1 回のみにする |

## 外部プラグインとの連携

シーンプリセットには他プラグインが独自のデータを相乗りできる公開 API があります。
開発者向けの詳細は
[scene-preset-provider-guide.md](https://github.com/kidonaru/COM3D2.SceneEditor.Plugin/blob/main/docs/scene-preset-provider-guide.md)
を参照してください。
