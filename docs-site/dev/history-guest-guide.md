# 操作履歴（undo/redo）連携

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の操作履歴へ、
他プラグインの操作を積んで undo/redo させるための資料。

対象バージョン: SceneEditor `8e8b93f` 以降（HistoryAPI 搭載）、
MTEUtils は `HistoryClient` 搭載以降。
ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

登録した操作は内部操作（ポーズ・表情・脱衣など）と同じ 1 本の履歴に時系列で並び、
undo/redo キー・履歴ウィンドウの一覧・クリックによる任意地点へのジャンプが
そのまま効くようになる。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| 確定済み操作 1 件の登録 | ✅ `Register` |
| undo / redo の実行 | ✅ `Undo` / `Redo` |
| 履歴ウィンドウへの表示・任意地点へのジャンプ | ✅ 登録するだけで対象になる |
| 履歴変化の購読（自前 UI の更新用） | ✅ `Subscribe` / `Unsubscribe` |
| ドラッグ中の連続変更を 1 件へ集約 | ❌ ゲスト側の責務（下記「実装義務」1 参照） |
| 内部の pending 機構（`BeforeEdit`）の利用 | ❌ 非公開。集約ルールが API 契約になるのを避けるため |
| シーンをまたぐ履歴の保持 | ❌ シーン遷移で履歴は全クリアされる |

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を持つプラグインは `HistoryClient`（`COM3D2.MotionTimelineEditor` 名前空間）を呼ぶだけでよい。

```csharp
// 操作が確定した時点で 1 回だけ呼ぶ
var oldColor = target.color;
target.color = newColor;

HistoryClient.Register("色変更",
    () => target.color = oldColor,   // undo
    () => target.color = newColor,   // redo
    () => target != null);           // 任意: 対象が生きているか
```

SceneEditor が存在しない環境では `isAvailable` が false になり、
`Register` / `Undo` / `Redo` はすべて無視される。**ゲスト側に分岐は要らない**。

履歴の変化に追従して自前 UI を更新したい場合:

```csharp
private readonly Action _onHistoryChanged = () => { /* 表示更新 */ };

void OnEnable()  { HistoryClient.Subscribe(_onHistoryChanged); }
void OnDisable() { HistoryClient.Unsubscribe(_onHistoryChanged); }
```

### B. 自前実装（リフレクション直叩き）

MTEUtils を使わない場合は、`HistoryClient.cs` と同じ手順で接続する。

```csharp
// 1. ホスト発見 (見つからなければ履歴連携なしで動作させる)
var type = Type.GetType(
    "COM3D2.SceneEditor.Plugin.HistoryAPI, COM3D2.SceneEditor.Plugin");
// ロード順によっては null になるため、AppDomain からのフォールバック + 後刻再試行を推奨:
// AppDomain.CurrentDomain.GetAssemblies() から
//   GetName().Name == "COM3D2.SceneEditor.Plugin" を探して GetType(...)

// 2. メソッドを Delegate.CreateDelegate でキャッシュ (毎回 Invoke しない)
```

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.HistoryAPI (public static)
void Register(
    string description,   // 履歴ウィンドウに表示する操作名 (記録時刻はホストが付与)
    Action undo,          // 操作前の状態へ書き戻す処理 (必須)
    Action redo,          // 操作後の状態へ書き戻す処理 (必須)
    Func<bool> canApply = null  // 今は適用できないとき false。null なら常に適用可
);
void Undo();
void Redo();
bool canUndo { get; }
bool canRedo { get; }
event Action onChanged;   // 追加/undo/redo/ジャンプ/クリアで発火
```

シグネチャを標準型（`string` / `Action` / `Func<bool>`）だけで構成してあるのは、
ゲストがこのアセンブリをハード参照せず `Delegate.CreateDelegate` で掴めるようにするため。
独自型（`HistoryScope` 等）は公開面に出していない。

`onChanged` は event なので、リフレクションでは `EventInfo` の
`GetAddMethod()` / `GetRemoveMethod()` を `Action<Action>` として束ねる。

## ゲスト側の実装義務

1. **`Register` は「確定済み」の操作 1 件に対して呼ぶ**。
   スライダーやドラッグの途中経過を毎フレーム登録してはいけない。
   内部窓はマウス解放を検出して 1 ドラッグ = 1 エントリにまとめているが、
   この集約機構は公開していないため、**まとめるのはゲスト自身の責務**。
   典型的には「ドラッグ開始時に変更前値を控え、マウス解放時に 1 回だけ `Register`」とする
2. **undo/redo クロージャは冪等にする**。履歴ウィンドウのジャンプでは
   複数エントリが連続適用されるため、順序や呼び出し回数に依存してはいけない
3. **undo/redo/canApply の中から `Register` / `Undo` / `Redo` を呼び返さない**。
   適用中の再入は走査中の履歴リストを壊すため、ホストは受け付けず警告ログを出して無視する
4. **`Subscribe` したら不要になった時点で必ず `Unsubscribe`** する。
   履歴はシングルトンで常駐するため、解除を怠るとハンドラと、それが掴んだ参照ごと残り続ける。
   ウィンドウの表示トグルのたびに購読する実装では多重登録にも注意
5. **変更が無いなら `Register` を呼ばない**。ホストは内部エントリについては
   前後比較で無変化エントリを捨てているが、デリゲート式エントリの中身は判定できないため、
   呼べばそのまま 1 件積まれる

## 挙動の詳細・注意点

- **`description` に記録時刻はホストが付与する**（`[HH:mm:ss] ` を前置）。ゲストは操作名だけ渡す
- **クロージャが例外を投げても** ホストは巻き込まれない（try/catch 済み）。
  そのエントリは適用失敗として飛ばされ、`MTEUtils.LogException` に出る。
  `canApply` も外部実装が入る前提で同じ try で保護されている
- **`canApply` が false のエントリは飛ばされる**。undo で戻る先が無くなった場合は
  さらに 1 つ前のエントリへ進む。対象オブジェクトが破棄されうるなら必ず実装すること
- **redo 側は新規登録で切り捨てられる**。undo した状態で `Register` すると
  それ以降の redo は消える（一般的な undo スタックと同じ挙動）
- **履歴は `historyLimit` で上限管理**され（既定 20 件）、超過分は古い順に捨てられる。
  `historyLimit <= 0` のとき履歴機能自体が無効で、`Register` は無視される
- **次のタイミングで履歴は全クリアされる**。外部プラグインが登録したエントリも同時に破棄され、
  クロージャが掴んだ参照もそこで解放されるため、ゲスト側で後始末を書く必要はない
  - シーン遷移
  - メイドの呼出 / 解除
  - シーンプリセットの適用
  - 履歴ウィンドウのクリアボタン（ユーザー操作）
- **プラグイン無効化時に確定待ちが残らないよう**ホストは `OnPluginDisable` で内部 pending を確定する。
  ゲスト側は確定済みしか登録しないためこの影響を受けない

## 動作確認の手順（devbridge REPL での擬似ゲスト）

実プラグインを書く前に、MCP `com3d25-devbridge` の `eval_csharp` でダミー登録して
挙動を確認できる:

```csharp
var t = System.Type.GetType("COM3D2.SceneEditor.Plugin.HistoryAPI, COM3D2.SceneEditor.Plugin");
var log = new System.Collections.Generic.List<string>();
System.Action undo = () => log.Add("undo");
System.Action redo = () => log.Add("redo");
t.GetMethod("Register").Invoke(null, new object[] { "外部テスト", undo, redo, null });
// 履歴ウィンドウに [HH:mm:ss] 外部テスト が出ることを確認してから
t.GetMethod("Undo").Invoke(null, null);   // log に "undo" が入る
t.GetMethod("Redo").Invoke(null, null);   // log に "redo" が入る
```

MTEUtils を組み込んだゲストからは、接続できているかを
`COM3D2.MotionTimelineEditor.HistoryClient.isAvailable` で確認するのが早い。
束縛に失敗しても例外は出ず警告ログのみで履歴連携が黙って無効になるため、
初回接続時に一度は確認しておくこと。

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/HistoryAPI.cs` | 公開 API + DelegateHistoryEntry |
| `source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs` | 履歴本体（IHistoryEntry / スナップショット式エントリ） |
| `source/COM3D2.SceneEditor.Plugin/HistoryWindow.cs` | 履歴一覧ウィンドウ |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/HistoryClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
