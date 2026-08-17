# その他の連携（選択・ギズモツール・入力座標）

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）が公開する小さめの連携 API のまとめ。
いずれも MTEUtils 側にリフレクションブリッジのクライアントがあり、SceneEditor が
不在・旧バージョンの場合は `isAvailable` が false になって連携だけが無効になる。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

## 選択の同期（SelectionClient）

SceneEditor 側の選択オブジェクトを読み書きし、選択変更を購読する。
自前ギズモを持つプラグインが、選択だけ SceneEditor の Inspector と同期させたい
ケースを想定している。

```csharp
// SceneEditor 側の選択を設定する。go = null で選択解除。
// showGizmo = false なら SceneEditor 側ギズモを抑止する（自前ギズモを持つ場合）。
// focus = true なら SceneView のカメラを対象へ寄せる
SelectionClient.Select(go, showGizmo: false, focus: true);

// 現在の選択を読む（SceneEditor 不在時は null）
var selected = SelectionClient.selectedObject;

// 選択変更の購読。SceneEditor 不在時は false を返すので true になるまで再試行してよい
SelectionClient.AddSelectionChangedHandler(OnSelectionChanged);
```

注意点:

- 連動は双方向: `Select` で SceneEditor へ反映でき、SceneEditor 側での選択変更は
  ハンドラで受け取れる。エコーバック（自分の `Select` による通知）をループさせない
  よう、ハンドラ側で現在値と比較すること
- フォーカス指定（`focus`）に対応しない旧 SceneEditor では選択のみ反映される
- ホスト実体は `SelectionManager`（`instance` シングルトン、`onSelectionChanged` イベント、
  `Select(GameObject, bool, bool)`）

## ギズモツールの状態共有（GizmoToolClient）

SceneEditor のツールバーが持つギズモ操作種別（None / Move / Rotate / Scale）と
軸空間（Local / Global）を読み書きする。外部ギズモの操作モードを SceneEditor の
ツールバーと揃えるために使う。

```csharp
if (GizmoToolClient.isAvailable)
{
    var tool = GizmoToolClient.tool;            // GizmoTool（int 経由で授受）
    var local = GizmoToolClient.useLocalSpace;  // true = Local
}
```

注意点:

- enum はアセンブリ間で別型になるため **int 経由で授受**する
  （None=0 / Move=1 / Rotate=2 / Scale=3）
- 取得に失敗すると `tool` は None、`useLocalSpace` は true（既定値）へ倒れ、
  同時に `isAvailable` も false になる。既定値を現在値と取り違えないよう、
  読み出し後に `isAvailable` を確認すること
- ホスト実体は `GizmoRenderer` の静的プロパティ `currentTool` / `useLocalSpace`

## マウス座標の取り違え防止（InputRemapperClient）

SceneEditor の `InputRemapper` は GameView の描画領域内で `Input.mousePosition` を
RT 座標へ書き換える。スクリーン座標前提の窓上判定（`MTEUtils.IsMouseOverWindowRect` 等）が
変換済み座標を読むと GameView 上なのに「ウィンドウ上」と誤判定し、入力ブロックや
ギズモ抑止が誤発動する。

`InputRemapperClient.Update()` を毎フレーム呼ぶだけで、`MTEUtils.mousePositionGetter` が
生のスクリーン座標へ差し替えられる。SceneEditor が存在しない環境では何もしない。

```csharp
// 毎フレーム（Update 等）呼んでよい。解決済みなら何もしない
InputRemapperClient.Update();
```

MTEUtils のウィンドウ基盤（`DockableWindowBase` 等）を使うプラグインで、
GameView 上のマウス判定がずれる場合はこれを入れる。

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/SelectionManager.cs` | 選択のホスト実体 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/SelectionClient.cs` | 選択のリフレクションブリッジ |
| `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` | ギズモツール状態のホスト実体 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoToolClient.cs` | ギズモツール状態のブリッジ |
| `source/COM3D2.SceneEditor.Plugin/Manager/InputRemapper.cs` | 入力座標の書き換え元 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/InputRemapperClient.cs` | 座標取り違え防止のブリッジ |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[タブドッキング / スナップ連携](docking-guest-guide.md) の「他の連携 API」を参照。
