# インストール

## 前提

- COM3D2.5 または COM3D2 (2.0)
- Sybaris（UnityInjector）導入済みの環境
- **BepInEx が導入済みであること**

BepInEx が無い環境では、起動時のログに次のようなエラーが出てプラグインが読み込まれません。

```
System.Reflection.ReflectionTypeLoadException: The classes in the module cannot be loaded.
  at System.Reflection.Assembly.GetTypes ()
  at UnityInjector.PluginManager.LoadPlugins_DLL (System.String dll, System.String exe)
```

## 手順

1. [GitHub の Releases](https://github.com/kidonaru/COM3D2.SceneEditor.Plugin/releases) から `COM3D2.SceneEditor.Plugin-vX.X.X.zip` をダウンロードして展開する

   展開すると以下の構成になっています。

   ```
   COM3D2.SceneEditor.Plugin/
   ├── UnityInjector/              … COM3D2 (2.0) 用
   ├── UnityInjector (COM3D2.5)/   … COM3D2.5 用
   └── README.txt
   ```

2. ゲームのバージョンに合わせて、対応するフォルダの**中身**を `Sybaris\UnityInjector\` へコピーする

| ゲーム | コピー元 |
|---|---|
| COM3D2.5 | `UnityInjector (COM3D2.5)\` の中身 |
| COM3D2 (2.0) | `UnityInjector\` の中身 |

   どちらのフォルダにも `COM3D2.SceneEditor.Plugin.dll` と、サンプルのウィンドウレイアウト / シーンプリセットが入った `Config\` が含まれています。DLL 以外は共通なので、間違えて両方をコピーした場合は DLL だけ入れ直せば問題ありません。

3. ゲームを起動し、事務所などのシーンで `F10` を押してエディタウィンドウモードが起動すれば導入完了です

## 推奨プラグイン（連携対応）

以下のプラグインは SceneEditor との連携に対応しています。導入すると、タブドッキングやシーンプリセットへの相乗り（モデル配置・ポストエフェクトの保存/復元）が利用できます。

| プラグイン | 連携内容 | 対応バージョン |
|---|---|---|
| [COM3D2.ModItemExplorer.Plugin](https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin/releases) | MOD アイテムの閲覧・モデル配置。配置したモデルをシーンプリセットに保存/復元できます | v2.0.0.0 以降 |
| [COM3D25.PostEffects.Plugin](https://github.com/kidonaru/COM3D25.PostEffects.Plugin/releases) | ポストエフェクト。エフェクト設定をシーンプリセットに保存/復元できます | v2.0.0.0 以降 |

## アンインストール

配置した `COM3D2.SceneEditor.Plugin.dll` を削除してください。

設定やプリセットも消したい場合は、以下も削除します（[データの保存先](/guide/configuration#データの保存先)参照）。

- `Sybaris\UnityInjector\Config\SceneEditor.xml`
- `Sybaris\UnityInjector\Config\SceneEditor\` フォルダ
