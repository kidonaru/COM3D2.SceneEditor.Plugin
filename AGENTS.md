# 作業ガイド

## 基本ルール

- 思考は英語、回答は日本語で行う。
- コードのコメントとエラーログは日本語で記述する。
- ライブラリの使い方を調べる場合は、必ずContext7 MCPで最新情報を取得する。
- パス・シーン名・数値のハードコーディングは、必要な場合を除き避ける。
- 既存の未コミット変更を保持し、依頼と無関係な変更を戻さない。

## 最初に見る場所

| 作業 | 入口 |
| --- | --- |
| タイムラインXML、ビーム同期、床ライト | [tools/timeline/AGENTS.md](tools/timeline/AGENTS.md)、[ツール一覧](tools/timeline/README.md) |
| プラグイン本体 | `source/COM3D2.SceneEditor.Plugin/` |
| プラグインのテスト | `source/COM3D2.SceneEditor.Plugin.Tests/` |
| Blender用ツール | `tools/blender/` |
| ビルド・開発手順 | [README.md](README.md)、`docs/`、`docs-site/` |

タイムライン関連ファイルは `tools/timeline/` に集約する。ルートにスクリプト・JSON・画像を直置きしたり、`beam-XXX-work` のような作業フォルダーを増やしたりしない。新しい配置は同ディレクトリのAGENTS.mdに従う。

## 調査ツール

- リポジトリ直下に `.codegraph/` がある場合、コードの定義・呼び出し・影響範囲はCodeGraphを先に使う。`codegraph_explore` にプロジェクトパスとシンボル／ファイル名を指定する。索引がなければ作成せず、通常の検索を使う。
- 文字列・コメント・ログ文言とファイル名の検索は `rg` / `rg --files` を使う。
- 大きなXML、JSON、ログ、diff、20行を超える出力の集計・要約にはcontext-modeを使い、必要な結果だけを取り出す。
- 編集するファイルは内容を読んでから、対象を限定して編集する。
