# 選択中メイドの共有

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の選択中メイドを
他プラグインと共有するための資料。読み取り・変更通知の購読に加え、
外部から SceneEditor の選択を変更することもできる（双方向）。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `MaidSelectHost` 搭載版以降。未対応バージョンでは
クライアントの `isAvailable` が false になり、連携だけが無効になる。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| 現在の選択中メイドを読む | ✅ `selectedMaid` |
| 選択の変化を購読する（選択解除 = null も流れる） | ✅ `Subscribe` |
| 外部から SceneEditor の選択を変更する | ✅ `TrySelectMaid` |
| 連携の ON/OFF をゲスト側から切り替える | ❌ 設定は SceneEditor の設定ウィンドウ「連携」タブが持つ（読み取りのみ `isLinkEnabled`） |

`Maid` はゲーム本体（Assembly-CSharp）の型で全プラグインが共有参照しているため、
プラグイン独自型と違い DLL 間でそのまま受け渡せる。

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは `MaidSelectClient` をそのまま呼べる。

```csharp
private void OnSelectedMaidChanged(Maid maid)
{
    // maid は選択解除時 null。自分の TrySelectMaid にもエコーされる点に注意
    _currentMaid = maid;
}

// 有効化時（Awake / Start 等）
MaidSelectClient.Subscribe(OnSelectedMaidChanged);

// 破棄時
MaidSelectClient.Unsubscribe(OnSelectedMaidChanged);

// 外部から SceneEditor の選択を変更する
if (!MaidSelectClient.TrySelectMaid(maid))
{
    // SceneEditor が無効 / 不在 / 連携設定 OFF / メイドが呼出済みでない
}
```

クライアントが以下を肩代わりするため、ゲスト側の分岐は不要:

- **ロード順**: SceneEditor が自分より後にロードされても、接続できるまで自動で再試行する
  （最大 30 秒。それまでに見つからなければ SceneEditor 不在とみなして諦める）
- **初期同期**: 接続できた時点で現在の選択を 1 回プッシュする。
  ただし連携設定が OFF の間はプッシュしない（ゲストは現状維持）
- **SceneEditor 不在**: `isAvailable` が false になり、`Subscribe` 等はすべて無視される。
  `selectedMaid` は null、`TrySelectMaid` は false を返す

### B. 自前実装（リフレクション直叩き）

MTEUtils を使わない場合は `MaidSelectClient.cs` と同じ手順で接続する。
ホストの型は `Type.GetType("COM3D2.SceneEditor.Plugin.MaidSelectHost, COM3D2.SceneEditor.Plugin")`
で取得する。ロード順によっては null になるため、`AppDomain.CurrentDomain.GetAssemblies()` からの
フォールバックと後刻の再試行を推奨（`DockingClient.FindHostType` と同じ）。

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.MaidSelectHost (public static)
Maid selectedMaid { get; }           // 現在の選択中メイド。未選択・解放済みなら null
bool isLinkEnabled { get; }          // 連携設定が ON か
bool TrySelectMaid(Maid maid);       // 外部からの選択変更。無効時は何もせず false
void Subscribe(Action<Maid> onChanged);   // 選択変更の購読。引数は変化後のメイド（解除は null）
void Unsubscribe(Action<Maid> onChanged);
```

## 挙動の詳細・注意点

- **通知のタイミング**: SceneEditor 側の選択切り替え処理（ギズモ選択の同期）が
  完了した後に流れる。選択解除（null）も流れる
- **エコーに注意**: `TrySelectMaid` で選択を変更すると、自分の購読ハンドラにも
  同じ変更の通知が流れる。無限ループや二重処理を避けたい場合は、
  自分が直前に Set した値と同じ通知を無視するなどの抑止をゲスト側で行うこと
- **`TrySelectMaid` が false になる条件**: SceneEditor の UI が無効 /
  連携設定が OFF / `maid` が null / メイドが SceneEditor の呼出済みリストに居ない
- **連携設定が OFF の間は通知が来ない**。購読自体は維持されるので、再登録は不要。
  OFF → ON へ切り替えられた瞬間だけ、その時点の選択が 1 回プッシュされる
- **ホストの `Subscribe` は購読時に通知しない**。A の `MaidSelectClient` はこれを補って
  接続時に現在値を 1 回プッシュするが、B の自前実装では接続直後に `selectedMaid` を
  自分で読むこと
- **受け取った `Maid` を保持する場合は生存チェックをすること**。メイドの解除・
  シーン遷移で実体が入れ替わるため、利用時に `maid != null && maid.body0 != null` 等を
  確認する。ホスト側の `selectedMaid` は解放済みメイドを返さない（自動で null になる）が、
  その際に解除通知が流れるとは限らない
- **同じデリゲートの二重登録は無視される**（多重発火しない）
- **不要になったら必ず `Unsubscribe`**。ホストは常駐するため、解除を怠ると
  ハンドラが掴んだ参照ごと残る
- **デリゲートが例外を投げてもホストは巻き込まれない**（購読者ごとに try/catch 済み）が、
  ログ（MTEUtils.LogException）に出るのでゲスト側で潰しておくこと

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/MaidSelectHost.cs` | 公開 API |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/MaidSelectClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` | 選択の実体（`targetMaid`）と通知の発火点 |
| `source/COM3D2.SceneEditor.Plugin/SettingWindow.cs` | 連携設定の UI（「連携」タブ） |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[タブドッキング / スナップ連携](docking-guest-guide.md) の「他の連携 API」を参照。
