import { defineConfig } from 'vitepress';

// GitHub Pages のサブパス。favicon 等 head のリソースは base を自動付与されないため共有する
const base = '/COM3D2.SceneEditor.Plugin/';

export default defineConfig({
  lang: 'ja-JP',
  title: 'COM3D2.SceneEditor.Plugin',
  description:
    'COM3D2 / COM3D2.5 のゲーム画面をウィンドウ化し、エディタ風 UI でメイドのポーズ・表情・衣装、カメラ、ライト、背景、BGM を編集して撮影まで完結できる UnityInjector プラグイン',
  base,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}favicon.svg` }]],
  themeConfig: {
    logo: '/favicon.svg',
    nav: [
      { text: 'ガイド', link: '/' },
      { text: '開発者向け', link: '/dev/' },
      { text: 'ダウンロード', link: 'https://github.com/kidonaru/COM3D2.SceneEditor.Plugin/releases' },
    ],
    sidebar: {
      '/dev/': [
        {
          text: '開発者向け',
          items: [
            { text: '概要', link: '/dev/' },
            { text: 'ビルド方法', link: '/dev/build' },
          ],
        },
        {
          text: '外部プラグイン連携',
          items: [
            { text: 'タブドッキング / スナップ', link: '/dev/docking-guest-guide' },
            { text: '操作履歴（undo/redo）', link: '/dev/history-guest-guide' },
            { text: 'シーンプリセット', link: '/dev/scene-preset-provider-guide' },
            { text: 'SceneCapture 取り込み', link: '/dev/scenecapture-import-guide' },
            { text: 'ギズモ', link: '/dev/gizmo-guest-guide' },
            { text: 'Inspector 描画の委譲', link: '/dev/inspector-guest-guide' },
            { text: '有効/無効の連動', link: '/dev/editor-state-guest-guide' },
            { text: '選択中メイドの共有', link: '/dev/maid-select-guest-guide' },
            { text: '選択中モデルの共有', link: '/dev/model-select-guest-guide' },
            { text: 'その他（選択 / ツール / 入力）', link: '/dev/misc-guest-guide' },
          ],
        },
      ],
      '/': [
        {
          text: 'ガイド',
          items: [
            { text: 'はじめに', link: '/' },
            { text: 'インストール', link: '/guide/installation' },
            { text: '基本操作', link: '/guide/getting-started' },
            { text: 'ウィンドウ管理', link: '/guide/windows' },
            { text: 'SceneView / Hierarchy / Inspector', link: '/guide/scene-view' },
            { text: 'メイド編集', link: '/guide/maid-editing' },
            { text: '演出と撮影', link: '/guide/staging' },
            { text: 'シーンプリセット', link: '/guide/scene-preset' },
            { text: 'ショートカット', link: '/guide/shortcuts' },
            { text: '設定リファレンス', link: '/guide/configuration' },
            { text: '既知の制限', link: '/guide/limitations' },
          ],
        },
      ],
    },
    search: { provider: 'local' },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/kidonaru/COM3D2.SceneEditor.Plugin' },
      { icon: 'x', link: 'https://x.com/kidonaru' },
    ],
    outline: { label: 'このページの内容' },
    docFooter: { prev: '前のページ', next: '次のページ' },
    darkModeSwitchLabel: '外観',
    sidebarMenuLabel: 'メニュー',
    returnToTopLabel: 'トップへ戻る',
  },
});
