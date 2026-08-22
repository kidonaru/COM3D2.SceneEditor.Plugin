<script setup>
import VueTweet from 'vue-tweet'
</script>

# はじめに

**COM3D2.SceneEditor.Plugin** は、3D シーンの描画を移動・リサイズできるウィンドウ（GameView）の中に収め、
ゲーム標準の UI を隠した「エディタ風モード」を提供する UnityInjector 形式プラグインです。

メイドのポーズ・表情・衣装、カメラ、ライト、背景、BGM をその場で編集し、撮影まで完結できます。

## 紹介動画

<ClientOnly>
  <VueTweet tweet-url="https://x.com/kidonaru/status/2088985379563348236">
    <template #error>
      動画が表示できない場合は <a href="https://x.com/kidonaru/status/2088985379563348236" target="_blank" rel="noreferrer">X で見る</a>。
    </template>
  </VueTweet>
</ClientOnly>

## 特徴

- **GameView** — ゲーム画面をウィンドウ化。移動・リサイズ・最大化。どんな形にリサイズしても構図はゲーム本来の見え方から変わらない
- **SceneView** — 専用カメラによる俯瞰ビュー。UnityEditor 風のカメラ操作・クリック選択・ギズモ操作
- **Hierarchy / Inspector** — GameObject ツリーの表示と、Transform・ギズモ種別の編集
- **メイド操作** — 呼出・配置、IK ドラッグ点によるポーズ付け、モーション（再生・シーク）、表情、指（ブレンド + 関節ごとの個別編集）、視線、IK 固定、脱衣、重力、ボーン編集（装着アイテム / 配置モデル）
- **演出** — カメラ構図のプリセット（10 スロット）、背景切替、メイン/追加ライト（ポイント / スポット / 平行）、BGM 再生、PNG 配置
- **シーンプリセット** — メイド・カメラ・背景・ライト・BGM・PNG 配置をまとめて保存/適用。保存/読込する要素の絞り込み、事務所シーンの自動ロード、SceneCapture プリセットの読み込みにも対応
- **撮影** — UI が写らないスクリーンショット（最大 4 倍解像度、背景の透過に対応）
- **操作履歴** — 主要な操作の Undo / Redo
- **ウィンドウ管理** — スナップ吸着、タブドッキング、ウィンドウのロック、名前付きレイアウトの保存

## 対応バージョン

| ゲーム | 状態 |
|---|---|
| COM3D2.5 | 対応 |
| COM3D2 (2.0) | 対応 |

ダンスシーン・VR シーンには対応していません。詳しくは[既知の制限](/guide/limitations)を参照してください。

## 次に読む

- [インストール](/guide/installation) — DLL の配置場所と導入手順
- [基本操作](/guide/getting-started) — モードの起動とメニューバー
- [ウィンドウ管理](/guide/windows) — 移動・リサイズ・ロック・ドッキング・レイアウト
- [SceneView / Hierarchy / Inspector](/guide/scene-view) — 俯瞰ビューとオブジェクト編集
- [メイド編集](/guide/maid-editing) — ポーズ付けとメイド関連ウィンドウ
- [演出と撮影](/guide/staging) — カメラ・背景・ライト・BGM・PNG 配置・スクリーンショット
- [シーンプリセット](/guide/scene-preset) — シーン全体の保存と復元
- [ショートカット](/guide/shortcuts) — キーボード操作一覧
- [設定リファレンス](/guide/configuration) — 設定ファイルの全項目
- [既知の制限](/guide/limitations) — 非対応シーンや他プラグインとの干渉

