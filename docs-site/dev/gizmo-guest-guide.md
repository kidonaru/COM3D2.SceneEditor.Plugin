# ギズモ連携

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の SceneView / GameView に、
他プラグインのギズモ（3D 操作ハンドル）の描画と入力を参加させるための資料。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `GizmoHost` 搭載版以降。未対応バージョンでは
クライアントの `isAvailable` が false になり、ゲストは自前の standalone 経路
（`Camera.main` + `Input.mousePosition`）で従来通り動作する。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| SceneView / GameView のカメラでギズモを描画する | ✅ `draw` デリゲートが各ビューカメラの `OnPostRender` から呼ばれる |
| ビュー上のドラッグでギズモを操作する | ✅ `tryBeginDrag` / `updateDrag` / `endDrag` が RT ピクセル座標で呼ばれる |
| ドラッグ中の選択・カメラ操作の抑止 | ✅ ホスト側が自動で行う（ドラッグが始まったビューのみ） |
| 複数ギズモの同時ドラッグ | ❌ 同時に掴めるのは全登録者を通じて 1 個だけ（登録順に判定し、最初にヒットした 1 個） |
| ビューが 1 つも稼働していないときの駆動 | ❌ `IsViewActive()` が false の間はゲスト側が standalone 経路で駆動する |

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは `GizmoHostClient` をそのまま呼べる。

```csharp
private object _gizmoHandle;

// 有効化時
_gizmoHandle = GizmoHostClient.Register(
    "MyPlugin",
    tryBeginDrag: (camera, rtPoint) => TryPick(camera, rtPoint),  // 掴めたら true
    updateDrag:   (camera, rtPoint) => UpdateDrag(camera, rtPoint),
    endDrag:      () => EndDrag(),
    isDragging:   () => _dragging,
    draw:         camera => DrawGizmo(camera));

// 毎フレーム: ホストのビューが稼働していない間だけ standalone で駆動する
if (!GizmoHostClient.isViewActive)
{
    DriveStandalone();  // Camera.main + Input.mousePosition で従来通り動かす
}

// 破棄時
GizmoHostClient.Unregister(_gizmoHandle);
_gizmoHandle = null;
```

クライアントが肩代わりすること:

- **ロード順**: ホストのアセンブリが見つかるまで接続を再試行する
- **バージョン差**: シグネチャ不一致時は `isAvailable` が false になり standalone へ倒れる。
  `IsViewActive` を持たない旧ホストでは `isViewActive` は常に true 扱い（従来動作を維持）
- **ホスト側の失敗**: 登録・解除で例外が出ても警告ログのみで続行する

### B. 自前実装（リフレクション直叩き）

`GizmoHostClient.cs` と同じ手順で接続する。ホストの型解決は
[タブドッキング / スナップ連携](docking-guest-guide.md) の `DockingClient.FindHostType` と同様、
`Type.GetType` + 全アセンブリ走査のフォールバックと後刻の再試行を推奨。

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.GizmoHost (public static)
object Register(
    string name,                              // 登録名。同名の再登録は置き換え（リロード対応）
    Func<Camera, Vector2, bool> tryBeginDrag, // ビューのカメラと RT ピクセル座標。掴めたら true
    Action<Camera, Vector2> updateDrag,
    Action endDrag,
    Func<bool> isDragging,
    Action<Camera> draw);                     // 各ビューカメラの OnPostRender から呼ばれる
void Unregister(object handle);
bool IsViewActive();                          // 描画・入力を届けられるビューが稼働しているか
```

## 座標系

`tryBeginDrag` / `updateDrag` に渡されるのは **ビューの RenderTexture ピクセル座標**
（左下原点）。スクリーン座標ではない点に注意。レイ計算は渡された `camera` の
`ScreenPointToRay` にそのまま渡せる（カメラは RT へレンダリングしているため）。

## 挙動の詳細・注意点

- **`IsViewActive()` は毎フレーム問い合わせる**
  - 「登録済みかどうか」と「ビューが稼働しているか」は別の状態
  - GameView は window mode でないとメインカメラに描画・入力のディスパッチが付かない
  - false の間はゲストが standalone 経路で駆動する
- **ドラッグはビュー単位**: ドラッグを開始したカメラ以外からの `updateDrag` は
  ホストが無視する。SceneView と GameView が同フレームに呼んでも二重更新にならない
- **同名の再登録は置き換え**として扱われる（プラグインのリロード対応）
- **デリゲートが例外を投げてもホストは巻き込まれない**（呼び出しごとに try/catch 済み）。
  `updateDrag` で例外が出るとそのドラッグは強制終了される
- **不要になったら必ず `Unregister`**。ホストは常駐するため、解除を怠ると
  デリゲートが掴んだ参照ごと残る

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/GizmoHost.cs` | 公開 API |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/GizmoHostClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/Manager/GizmoRenderer.cs` | ビュー側の描画・入力ディスパッチ |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[タブドッキング / スナップ連携](docking-guest-guide.md) の「他の連携 API」を参照。
