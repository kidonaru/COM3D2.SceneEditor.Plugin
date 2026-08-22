# Inspector 描画の委譲

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の Inspector ウィンドウで、
他プラグインが管理するオブジェクト選択時に内容描画を丸ごと委譲してもらうための資料。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `InspectorHost` 搭載版以降。未対応バージョンでは
クライアントの `isAvailable` が false になり、SceneEditor の Inspector は
既定描画のままになる（ゲスト側の他機能には影響しない）。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| 選択オブジェクトが自分の管理下のとき、Inspector の内容描画を丸ごと受け取る | ✅ `canDraw` が true を返した登録者の `draw` が呼ばれる |
| Inspector ウィンドウの矩形・表示状態を読む | ✅ `GetWindowRect` / `IsWindowVisible`（自前ドロップダウン等の座標計算用） |
| ヘッダー行（ギズモ行 + アクティブ・名前・フォーカス）を自前のスクロールビュー内へ描く | ✅ `drawsHeader: true` で登録し、`DrawHeader` を呼ぶ（ヘッダーも一緒にスクロールする） |
| ヘッダー行の内容そのものの変更 | ❌ 中身を描くのはホスト。委譲先が選べるのは描く位置だけ |
| 複数登録者による同一オブジェクトの分担描画 | ❌ 最初に `canDraw` が true を返した 1 者が全面を描く |

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは `InspectorHostClient` をそのまま呼べる。

```csharp
private object _inspectorHandle;

// 有効化時
_inspectorHandle = InspectorHostClient.Register(
    "MyPlugin",
    canDraw: go => IsMyObject(go),          // 自分の管理下のオブジェクトなら true
    draw:    (go, contentRect) => DrawInspector(go, contentRect));

// 破棄時
InspectorHostClient.Unregister(_inspectorHandle);
_inspectorHandle = null;
```

ヘッダー行（ギズモ行 + アクティブ・名前・フォーカス行）は既定ではホストが
委譲領域の外へ固定表示する。内容と一緒にスクロールさせたい場合は
`drawsHeader: true` で登録し、自前のスクロールビューの先頭で `DrawHeader` を呼ぶ:

```csharp
_inspectorHandle = InspectorHostClient.Register(
    "MyPlugin", canDraw, draw,
    drawsHeader: InspectorHostClient.isHeaderDrawAvailable);

// draw の中（自前の BeginScrollView の直後）
var height = InspectorHostClient.DrawHeader(go, _view.GetDrawRect(-1, 0f));
_view.DrawEmpty(-1, height);   // 描いたぶんだけレイアウトを送る
```

`isHeaderDrawAvailable` が false（旧ホスト）のときは `drawsHeader` に false を渡すこと。
その場合ヘッダーは従来どおりホストが固定表示し、`contentRect` はその下の残り領域になる。

コンボのドロップダウンを自前ウィンドウとして出す場合は、ボタン座標を
スクリーン座標へ直す基準にホストのウィンドウ状態を使う:

```csharp
if (InspectorHostClient.isWindowStateAvailable)
{
    var hostRect = InspectorHostClient.hostWindowRect;   // Inspector のスクリーン矩形
    var visible = InspectorHostClient.isHostWindowVisible; // タブ非選択・一時非表示中は false
}
else
{
    // 旧ホスト: ドロップダウンを出さずに済む UI（前後送りボタン等）へ倒す
}
```

### B. 自前実装（リフレクション直叩き）

`InspectorHostClient.cs` と同じ手順で接続する。ホストの型解決は
[タブドッキング / スナップ連携](docking-guest-guide.md) の `DockingClient.FindHostType` と同様、
`Type.GetType` + 全アセンブリ走査のフォールバックと後刻の再試行を推奨。

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.InspectorHost (public static)
object Register(
    string name,                        // 登録名。同名の再登録は置き換え（リロード対応）
    Func<GameObject, bool> canDraw,     // 選択オブジェクトを自分が描くべきか
    Action<GameObject, Rect> draw);     // contentRect（ヘッダー下の残り領域）の中だけを描く
object Register2(                       // ヘッダーの描画者を選べる登録（後発 API）
    string name,
    Func<GameObject, bool> canDraw,
    Action<GameObject, Rect> draw,
    bool drawsHeader);                  // true なら contentRect からヘッダー行のぶんを引かない
void Unregister(object handle);
Rect GetWindowRect();                   // Inspector ウィンドウのスクリーン矩形（後発 API）
bool IsWindowVisible();                 // Inspector が描画中か（後発 API）
float DrawHeader(GameObject go, Rect rect);  // ヘッダー行を描く。戻り値は使った高さ（後発 API）
```

## 挙動の詳細・注意点

- **呼び出しは OnGUI 経由**。`draw` は 1 フレームに複数回（Layout / Repaint 等）呼ばれる
  前提で書く。`contentRect` の中だけを描き、外へはみ出さないこと
- **`canDraw` は毎回問い合わせる**。登録順に判定し、最初に true を返した 1 者へ委譲する
- **例外は登録者単位で隔離される**
  - 失敗したフレームは既定描画へフォールバックする
  - **連続 5 回失敗すると以後は呼ばれなくなる**（警告ログが出る）
  - 復帰には再登録が必要
- **同名の再登録は置き換え**として扱われる（プラグインのリロード対応）
- `GetWindowRect` / `IsWindowVisible` は後から足した API のため、旧ホストには無い。
  A の `isWindowStateAvailable` で有無を判定し、無ければドロップダウンを使わない UI へ倒す
- `Register2` / `DrawHeader` も後発 API。A の `isHeaderDrawAvailable` で有無を判定する。
  `DrawHeader` はホスト側の別ビューで描くため、呼び出し元のレイアウトは進まない。
  戻り値の高さぶんを自分で送ること（末尾の余白は含まれない）
- **不要になったら必ず `Unregister`**。ホストは常駐するため、解除を怠ると
  デリゲートが掴んだ参照ごと残る

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/InspectorHost.cs` | 公開 API |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/InspectorHostClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs` | Inspector ウィンドウ本体（委譲の呼び出し元） |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[タブドッキング / スナップ連携](docking-guest-guide.md) の「他の連携 API」を参照。
