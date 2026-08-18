# 開発者向けドキュメント

プラグインのビルド方法と、外部プラグインから SceneEditor の各機能へ連携するための
ガイド。連携はいずれもリフレクションブリッジ方式（属性ベースのシーンプリセットを除く）で、
MTEUtils を submodule 参照していれば対応クライアントをそのまま呼べる。
SceneEditor が不在・旧バージョンの環境では該当機能だけが自動で無効になる。

## ビルド

- [ビルド方法](./build) — COM3D2 (2.0) / COM3D2.5 両対応のビルド手順

## 外部プラグイン連携ガイド

| ガイド | ホスト型 | MTEUtils クライアント | できること |
|---|---|---|---|
| [タブドッキング / スナップ連携](./docking-guest-guide) | `DockingHost` | `DockingClient` / `DockableWindowBase` | ウィンドウのタブ統合・スナップ/コネクトへの参加 |
| [操作履歴（undo/redo）連携](./history-guest-guide) | `HistoryAPI` | `HistoryClient` | 外部の操作を SceneEditor の履歴に積み、undo/redo・履歴ジャンプの対象にする |
| [シーンプリセット連携](./scene-preset-provider-guide) | `ScenePresetProviderRegistry` | （属性ベースの自動発見） | 外部プラグインの状態をシーンプリセットに相乗りさせる |
| [SceneCapture プリセット取り込み](./scenecapture-import-guide) | 同上 | 同上 | SceneCapture プリセットの担当セクション（Models / Effects）を外部側で適用する |
| [ギズモ連携](./gizmo-guest-guide) | `GizmoHost` | `GizmoHostClient` | 外部ギズモを SceneView / GameView の描画・入力に参加させる |
| [Inspector 描画の委譲](./inspector-guest-guide) | `InspectorHost` | `InspectorHostClient` | 自分の管理オブジェクト選択時に Inspector の内容描画を受け取る |
| [有効/無効の連動](./editor-state-guest-guide) | `EditorStateHost` | `EditorStateClient` | SceneEditor の ON/OFF に自プラグインを追従させる |
| [選択中メイドの共有](./maid-select-guest-guide) | `MaidSelectHost` | `MaidSelectClient` | 選択中メイドの読み取り・変更通知の購読・外部からの選択変更（双方向） |
| [その他の連携](./misc-guest-guide) | `SelectionManager` ほか | `SelectionClient` ほか | 選択の同期・ギズモツールの状態共有・マウス座標の取り違え防止 |

## 共通の前提

- ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**
- 公開 API のシグネチャは**安定契約**（変更されず、追加のみ。変更が要る場合は
  `Register2` 等の別名で追加される）
- 契約に使う型はプリミティブ + UnityEngine 型 + デリゲートのみ
  （プラグイン定義型は DLL 間で共有できない）
- 不要になった登録は必ず解除すること。ホストは常駐するため、解除を怠ると
  デリゲートが掴んだ参照ごと残る
