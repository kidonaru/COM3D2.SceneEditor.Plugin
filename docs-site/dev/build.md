# ビルド方法

本プラグインは COM3D2 (2.0) 版と COM3D2.5 版を同一ソースからビルドします
（`GameVersion` プロパティで参照アセンブリと出力先を切り替えます）。

## 前提

- Visual Studio 2022 Community（MSBuild を使用）
  - パスは `source\COM3D2.SceneEditor.Plugin\build.bat` の `MSBUILD_PATH` で変更できます
- COM3D2 (2.0) と COM3D2.5 の両方のインストール（参照アセンブリの取得元）
- submodule の取得

  ```
  git submodule update --init --recursive
  ```

## セットアップ

`.env.sample` を `.env` にコピーし、ゲームのインストール先を設定します。

```
COM3D2_DIR=C:\KISS\COM3D2
COM3D25_DIR=C:\KISS\COM3D2.5
```

両方の指定が必須です。片方でも未設定だとビルドスクリプトが停止します。

## ビルド

開発中は、リポジトリ直下の `debug.bat` が Debug ビルドのショートカットです。

```
debug.bat            # Debug、2.0 / 2.5 両方
debug.bat com3d25    # Debug、2.5 のみ
debug.bat com3d2     # Debug、2.0 のみ
```

構成まで指定したい場合は `source\COM3D2.SceneEditor.Plugin\build.bat` を直接呼びます。

```
build.bat                 # Release、2.0 / 2.5 両方
build.bat debug           # Debug、2.0 / 2.5 両方
build.bat debug com3d25   # Debug、2.5 のみ
```

Release 構成ではビルド前に Debug / Release 両方の Clean が実行されます。

## 成果物

ビルドした DLL はリポジトリ内の以下へコピーされ、あわせて `.env` で指定したゲームの
`Sybaris\UnityInjector\` へも自動デプロイされます。

| 対象 | リポジトリ内のコピー先 |
|---|---|
| COM3D2 (2.0) | `UnityInjector\COM3D2.SceneEditor.Plugin.dll` |
| COM3D2.5 | `UnityInjector25\COM3D2.SceneEditor.Plugin.dll` |

ゲーム起動中は DLL がロックされているためデプロイに失敗しますが、警告を出すだけで
ビルド自体は成功扱いになります。

## アセットバンドル

ペンライト・レーザー・ポストエフェクト等のマテリアルはアセットバンドルとして
DLL に埋め込んでいます。バンドルは 2 つに分かれています。

| バンドル | 埋め込むファイル | 対象 | ビルド元 |
|---|---|---|---|
| `mte_bundle` | `Timeline\mte_bundle` | 両構成 | MTE 純正のビルド済みバイナリ（Unity 5.6、再ビルドしない） |
| `se_bundle` | `Timeline\se_bundle` | COM3D2.5 のみ | `build-bundle.bat`（Unity 2022.3.62f2） |

`mte_bundle` は MTE 由来の資産をそのまま使うため、`UnityProject/Assets/Shaders/` と
`Assets/Resources/` は**再ビルドの対象外**です（内容確認用に置いてあるだけ）。
SE 独自のアセットは `UnityProject/Assets/SceneEditor/` に置き、`se_bundle` としてビルドします。
現在ここにあるのは体積円錐版のステージライト（シェーダ名 `SE/StageLight`）だけです。

SE 独自のシェーダやテクスチャを変更したら、Unity エディタで UnityProject を閉じた状態で
`build-bundle.bat` を実行し、生成された `se_bundle` をコミットします。
Unity のパスは `.env` の `UNITY_2022_EXE` で変更できます。

`UnityProject/Assets/Scripts/` の C# はエディタ上の見た目確認用のコピーで、
正本は `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/` です。

`se_bundle` は Unity 2022 製で COM3D2 (2.0) には埋め込まないため、
ステージライトの描画は COM3D2.5 のみ対応です。
