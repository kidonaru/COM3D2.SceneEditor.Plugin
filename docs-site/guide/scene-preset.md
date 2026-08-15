# シーンプリセット

メイド・カメラ・背景・ライト・BGM・PNG 配置をまとめて 1 つのプリセットとして保存し、
あとから丸ごと復元できます。

## 保存と適用

メニューバー `Window` → `プリセット` でプリセットウィンドウを開きます。

- 保存ダイアログでは「カメラ」「メイド」など、保存する要素を選べます
  （既定状態は設定項目 `scenePresetSaveCamera` / `scenePresetSaveMaids` で変更できます）
- プリセットは `Sybaris\UnityInjector\Config\SceneEditor\ScenePreset\` に保存されます

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
