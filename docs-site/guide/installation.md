# インストール

## 前提

- COM3D2.5 または COM3D2 (2.0)
- Sybaris（UnityInjector）導入済みの環境

## 手順

1. [GitHub の Releases](https://github.com/kidonaru/COM3D2.SceneEditor.Plugin/releases) から配布物をダウンロードする
2. ゲームのバージョンに合わせて DLL を `Sybaris\UnityInjector\` へ配置する

| ゲーム | 配置する DLL |
|---|---|
| COM3D2.5 | `UnityInjector25\COM3D2.SceneEditor.Plugin.dll` |
| COM3D2 (2.0) | `UnityInjector\COM3D2.SceneEditor.Plugin.dll` |

3. ゲームを起動し、事務所などのシーンで `F10` を押してエディタウィンドウモードが起動すれば導入完了です

## アンインストール

配置した `COM3D2.SceneEditor.Plugin.dll` を削除してください。

設定やプリセットも消したい場合は、以下も削除します（[データの保存先](/guide/configuration#データの保存先)参照）。

- `Sybaris\UnityInjector\Config\SceneEditor.xml`
- `Sybaris\UnityInjector\Config\SceneEditor\` フォルダ
