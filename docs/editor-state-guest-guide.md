# 外部プラグイン向け 有効/無効 連動ガイド

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）の有効/無効へ、
他プラグインの有効/無効を追従させるための資料。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `EditorStateHost` 搭載版以降。未対応バージョンでは
クライアントの `isAvailable` が false になり、連動だけが無効になる。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| SceneEditor の ON/OFF をゲストへ通知する | ✅ `Subscribe` で購読する |
| 現在の SceneEditor の有効状態を読む | ✅ `isEditorEnabled` |
| ゲスト側の ON/OFF を SceneEditor へ反映する | ❌ 連動は SceneEditor → 外部の一方向のみ |
| 連動の ON/OFF をゲスト側から切り替える | ❌ 設定は SceneEditor の設定ウィンドウ「連携」タブが持つ（読み取りのみ `isLinkEnabled`） |

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは `EditorStateClient` をそのまま呼べる。

```csharp
private void OnEditorEnabledChanged(bool enabled)
{
    // 自プラグインの有効/無効をここで切り替える
    isEnable = enabled;
}

// 有効化時（Awake / Start 等）
EditorStateClient.Subscribe(OnEditorEnabledChanged);
// ロード順は不定なので、接続直後は現状へ自分で合わせる
if (EditorStateClient.isAvailable && EditorStateClient.isLinkEnabled)
{
    isEnable = EditorStateClient.isEditorEnabled;
}

// 破棄時
EditorStateClient.Unsubscribe(OnEditorEnabledChanged);
```

SceneEditor 不在の環境では `isAvailable` が false になり、`Subscribe` 等はすべて無視される
（呼び出し側で分岐する必要はない）。

### B. 自前実装（リフレクション直叩き）

MTEUtils を使わない場合は `EditorStateClient.cs` と同じ手順で接続する。
ホストの型は `Type.GetType("COM3D2.SceneEditor.Plugin.EditorStateHost, COM3D2.SceneEditor.Plugin")`
で取得する。ロード順によっては null になるため、`AppDomain.CurrentDomain.GetAssemblies()` からの
フォールバックと後刻の再試行を推奨（`DockingClient.FindHostType` と同じ）。

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.EditorStateHost (public static)
bool isEditorEnabled { get; }        // SceneEditor の UI が現在有効か
bool isLinkEnabled { get; }          // 連動設定が ON か
void Subscribe(Action<bool> onChanged);   // 変化の購読。引数は変化後の有効状態
void Unsubscribe(Action<bool> onChanged);
```

## 挙動の詳細・注意点

- **一方向のみ**: ゲスト側で自分を無効にしても SceneEditor は無効にならない
- **通知のタイミング**: ホストの有効化・無効化処理（ウィンドウの表示復元、位置保存、
  マネージャの片付け）が完了した後に流れる
- **連動設定が OFF の間は通知が来ない**。購読自体は維持されるので、再登録は不要。
  OFF → ON へ切り替えられた瞬間だけ、その時点の有効状態が 1 回プッシュされる。
  ON → OFF ではプッシュされない（ゲストは現状維持）
- **`Subscribe` した時点では通知が来ない**。プラグインのロード順は不定なので、
  接続直後に現状へ合わせたいなら `isEditorEnabled` を自分で読むこと
- **同じデリゲートの二重登録は無視される**（多重発火しない）
- **不要になったら必ず `Unsubscribe`**。ホストは常駐するため、解除を怠ると
  ハンドラが掴んだ参照ごと残る
- **デリゲートが例外を投げてもホストは巻き込まれない**（購読者ごとに try/catch 済み）が、
  ログ（MTEUtils.LogException）に出るのでゲスト側で潰しておくこと
- SceneEditor 自体がタイトル画面へ戻ったときにも無効化されるため、`false` の通知が流れる

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/EditorStateHost.cs` | 公開 API |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/EditorStateClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/SettingWindow.cs` | 連動設定の UI（「連携」タブ） |

## 他の連携 API

タブドッキング・履歴・シーンプリセット等は
[docking-guest-guide.md](docking-guest-guide.md) の「他の連携 API」を参照。
