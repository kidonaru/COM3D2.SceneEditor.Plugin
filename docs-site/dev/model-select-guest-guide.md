# 選択中モデルの共有

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の選択中モデルを
他プラグインと共有するための資料。読み取り・変更通知の購読に加え、
外部から SceneEditor の選択を変更することもできる（双方向）。

ここでいう「モデル」は **`ModelProviderHost`（MTEUtils なら `ModelProviderClient`）
経由で提供中の、外部プラグインが配置したモデルのルート GameObject** に限る。
SceneEditor 内製のオブジェクト（メイド・ライト等）は対象外で、
それらが選択された場合は「モデル選択解除（null）」として扱われる。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `ModelSelectHost` 搭載版以降。未対応バージョンでは
クライアントの `isAvailable` が false になり、連携だけが無効になる。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| 現在の選択中モデルを読む | ✅ `selectedModel` |
| 選択の変化を購読する（選択解除 = null も流れる） | ✅ `Subscribe` |
| 外部から SceneEditor の選択を変更する | ✅ `TrySelectModel` |
| 連携の ON/OFF をゲスト側から切り替える | ❌ 設定は SceneEditor の設定ウィンドウ「連携」タブが持つ（読み取りのみ `isLinkEnabled`） |

`GameObject` は Unity 本体の型なので、プラグイン独自型と違い DLL 間でそのまま受け渡せる。

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは `ModelSelectClient` をそのまま呼べる。

```csharp
private void OnSelectedModelChanged(GameObject model)
{
    // model は選択解除時 null。モデル以外 (メイド等) の選択も null として流れる。
    // 自分の TrySelectModel にもエコーされる点に注意
    _currentModel = model;
}

// 有効化時（Awake / Start 等）
ModelSelectClient.Subscribe(OnSelectedModelChanged);

// 破棄時
ModelSelectClient.Unsubscribe(OnSelectedModelChanged);

// 外部から SceneEditor の選択を変更する。
// 自前ギズモを持つプラグインは showGizmo: false で SceneEditor 側ギズモを抑止する
if (!ModelSelectClient.TrySelectModel(model, showGizmo: false))
{
    // SceneEditor が無効 / 不在 / 連携設定 OFF / モデルが提供中一覧に無い
}
```

クライアントが以下を肩代わりするため、ゲスト側の分岐は不要:

- **ロード順**: SceneEditor が自分より後にロードされても、接続できるまで自動で再試行する
  （最大 30 秒。それまでに見つからなければ SceneEditor 不在とみなして諦める）
- **初期同期**: 接続できた時点で現在の選択を 1 回プッシュする。
  ただし連携設定が OFF の間はプッシュしない（ゲストは現状維持）
- **SceneEditor 不在**: `isAvailable` が false になり、`Subscribe` 等はすべて無視される。
  `selectedModel` は null、`TrySelectModel` は false を返す

### B. 自前実装（リフレクション直叩き）

MTEUtils を使わない場合は `ModelSelectClient.cs` と同じ手順で接続する。
ホストの型は `Type.GetType("COM3D2.SceneEditor.Plugin.ModelSelectHost, COM3D2.SceneEditor.Plugin")`
で取得する。ロード順によっては null になるため、`AppDomain.CurrentDomain.GetAssemblies()` からの
フォールバックと後刻の再試行を推奨（`DockingClient.FindHostType` と同じ）。

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.ModelSelectHost (public static)
GameObject selectedModel { get; }    // 選択中モデル。モデル以外を選択中・未選択なら null
bool isLinkEnabled { get; }          // 連携設定が ON か
bool TrySelectModel(GameObject model, bool showGizmo);  // 外部からの選択変更。無効時は false
void Subscribe(Action<GameObject> onChanged);  // 変化の購読 (解除・モデル以外への切替は null)
void Unsubscribe(Action<GameObject> onChanged);
```

## 挙動の詳細・注意点

- **通知のタイミング**: SceneEditor 側の選択切り替え処理が完了した後に流れる。
  選択解除（null）も流れる
- **モデル以外への切替は null として流れる**。メイド等モデル以外のオブジェクトが
  選択された場合は 1 回だけ null が流れ、モデル以外同士の切替（メイド A → メイド B）では
  値が変わらないため通知されない
- **子オブジェクトはルートへ丸められる**。SceneView クリックでモデルの子メッシュが
  選択された場合も、そのモデルのルート GameObject として通知される
- **エコーに注意**: `TrySelectModel` で選択を変更すると、自分の購読ハンドラにも
  同じ変更の通知が流れる。無限ループや二重処理を避けたい場合は、
  自分が直前に Set した値と同じ通知を無視するなどの抑止をゲスト側で行うこと
- **`TrySelectModel` が false になる条件**: SceneEditor の UI が無効 /
  連携設定が OFF / `model` が null / モデルが `ModelProviderHost` で提供中の一覧に無い。
  受け付けるのは提供中モデルの**ルート GameObject** のみで、子オブジェクト指定は false になる
- **既に選択中のモデルへの `TrySelectModel` は true を返すが、通知は流れない**
  （選択の変化が無いため）
- **`showGizmo`**: false を渡すと SceneEditor 側のギズモ表示を抑止する。
  外部プラグインが自前のギズモを持つ場合に二重表示を避けるために使う
- **連携設定が OFF の間は通知が来ない**。購読自体は維持されるので、再登録は不要。
  OFF → ON へ切り替えられた瞬間だけ、その時点の選択が 1 回プッシュされる
- **ホストの `Subscribe` は購読時に通知しない**。A の `ModelSelectClient` はこれを補って
  接続時に現在値を 1 回プッシュするが、B の自前実装では接続直後に `selectedModel` を
  自分で読むこと
- **受け取った `GameObject` を保持する場合は生存チェックをすること**。モデルの削除・
  シーン遷移で破棄されるため、利用時に `model == null` を確認する
  （Unity の破棄済みオブジェクトは `== null` が true になる）
- **`selectedModel` は呼び出しごとに提供中モデルを列挙する**。毎フレーム参照するような
  高頻度呼び出しは避け、購読で受け取った値を保持すること
- **同じデリゲートの二重登録は無視される**（多重発火しない）
- **不要になったら必ず `Unsubscribe`**。ホストは常駐するため、解除を怠ると
  ハンドラが掴んだ参照ごと残る
- **デリゲートが例外を投げてもホストは巻き込まれない**（購読者ごとに try/catch 済み）が、
  ログ（MTEUtils.LogException）に出るのでゲスト側で潰しておくこと
- **汎用の `SelectionClient` との棲み分け**: `SelectionClient` は連携設定のゲートを持たない
  低レベルな選択同期 API。モデル選択の連携には、連携設定・モデル限定の写像・
  購読リトライを備えた本 API（`ModelSelectClient`）を使うこと

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/ModelSelectHost.cs` | 公開 API |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/ModelSelectClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/Manager/SelectionManager.cs` | 選択の実体と通知の発火点（`onSelectionChanged`） |
| `source/COM3D2.SceneEditor.Plugin/ModelProviderHost.cs` | 対象モデルの提供元 |
| `source/COM3D2.SceneEditor.Plugin/SettingWindow.cs` | 連携設定の UI（「連携」タブ） |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[タブドッキング / スナップ連携](docking-guest-guide.md) の「他の連携 API」を参照。
