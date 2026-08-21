# タブドッキング / スナップ連携

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）のタブドッキングとスナップ/コネクトへ、
他プラグインのウィンドウを参加させるための資料。

ホストのアセンブリ名・名前空間はいずれも **`COM3D2.SceneEditor.Plugin`**。

対象バージョン: SceneEditor `3f1817a` 以降（DockingHost 搭載）。
スナップ/コネクトは SceneEditor `17f6541` 以降、タブバーのゲスト描画は
SceneEditor `efa84bd` 以降が必要（いずれも MTEUtils 側は DockingClient /
DockableWindowBase の対応版が必要。未対応バージョンでは該当機能だけが自動で無効になる）。

## できること / できないこと

| 機能 | 扱い |
|---|---|
| タブドッキング（ヘッダー重ねで統合、タブ切替、つまみドラッグで分離） | ✅ 開放 |
| タブバー表示 | ✅ `EnableTabBar` のオプトイン宣言 + 自前ヘッダーへの描画が必要（MTEUtils の `DockableWindowBase` は実装済み）。未宣言だとグループ加入中もタブバーが出ない |
| 受け身スナップ（内部窓がゲスト窓の辺へ吸着する） | ✅ `Register` するだけで有効。ゲスト側の追加実装は不要 |
| ドラッグスナップ（ゲスト窓をドラッグして吸着させる） | ✅ 協調が必要（下記「スナップ/コネクトの実装義務」参照） |
| リサイズスナップ（ゲスト窓の辺をリサイズして吸着させる） | ✅ 協調が必要（下記「リサイズ吸着の実装義務」参照） |
| コネクト（連結移動） | ✅ `EnableConnect` のオプトイン宣言 + 協調が必要 |
| グループ構成の次回起動時復元 | ❌ 外部窓を含むグループは復元されない（起動順が不定で、ホストの復元時点で未 Register のため）。内部窓のみのグループは従来どおり復元される。コネクトグループも同様。ただし**ドッキングしていた位置のまま再表示すれば自動で再ドッキングされる**（下記「挙動の詳細」参照） |
| 名前付きレイアウトの保存 / 適用 | ❌ ゲスト窓の位置・表示状態はレイアウトに含まれない。レイアウト適用時は全タブグループ・コネクトグループが解体されるため、ゲストは独立ウィンドウへ戻される |

## 連携方法は 2 通り

### A. MTEUtils を使う（推奨）

MTEUtils を submodule 参照しているプラグインは、ウィンドウを
`DockableWindowBase`（`COM3D2.MotionTimelineEditor` 名前空間）継承にするだけでよい。

```csharp
public class MyWindow : DockableWindowBase
{
    protected override int windowId => 912345;      // 自前の GUI.Window ID
    protected override string windowTitle => "My Window"; // タブ表示名

    protected override void DrawContent()
    {
        // ヘッダー下の内容を描く (座標はウィンドウローカル)
    }

    // 任意: 配置の永続化
    protected override void LoadPlacement(out int x, out int y, out int width, out int height) { ... }
    protected override void StorePlacement(int x, int y, int width, int height) { ... }

    // 任意: 非アクティブタブ化されたときの後始末 (RT 停止など)
    protected override void OnTabVisibleChanged(bool visible) { ... }
}
```

基底が提供するもの:

- ヘッダー（タイトル・閉じるボタン）、ヘッダードラッグ移動、右下角リサイズ、画面内クランプ
- `isShowWnd = true` で自動的に DockingHost へ登録、`false` で解除
- SceneEditor 不在の環境では**そのまま独立ウィンドウとして動作**（standalone フォールバック）
- 非アクティブタブ化されると `OnGUI` を自動でスキップ（描画・入力停止）
- スナップ/コネクト協調一式（オプトイン宣言、ドラッグ起点通知、吸着中の `GUI.DragWindow` 抑止、
  コネクトボタンの描画、連結中の個別クランプ抑止）。ホストが未対応バージョンなら自動で無効化される
- タブバー協調一式（`EnableTabBar` の宣言、push された状態のヘッダー描画、
  タブ押下の `NotifyTabMouseDown` 通知、グループ加入中のタイトル非表示）

寸法（`HEADER_HEIGHT = 26` 等）はホストの `EditorSubWindow` と揃えてあり、
**変更してはならない**（内部窓とタブ列の見た目・位置を揃える前提）。

### B. 自前実装（リフレクション直叩き）

MTEUtils を使わない場合は、`DockingClient.cs` と同じ手順で接続する。

```csharp
// 1. ホスト発見 (見つからなければドッキングなしで動作させる)
var type = Type.GetType(
    "COM3D2.SceneEditor.Plugin.DockingHost, COM3D2.SceneEditor.Plugin");
// ロード順によっては null になるため、AppDomain からのフォールバック + 後刻再試行を推奨:
// AppDomain.CurrentDomain.GetAssemblies() から
//   GetName().Name == "COM3D2.SceneEditor.Plugin" を探して GetType(...)

// 2. メソッドを Delegate.CreateDelegate でキャッシュ (毎回 Invoke しない)
```

公開 API（シグネチャは安定契約。変更されず、追加のみ）:

```csharp
// COM3D2.SceneEditor.Plugin.DockingHost (public static)
object Register(
    int windowId,              // GUI.Window の ID (下記「windowId の注意」参照)
    string title,              // タブ表示名
    Func<Rect> getRect,        // 現在のウィンドウ矩形 (スクリーンGUI座標)
    Action<Rect> setRect,      // ホストからの矩形反映 (タブ同期・グループ移動)
    Func<bool> isVisible,      // 表示中か (isShowWnd 相当)
    Action<bool> setTabVisible // false = 非アクティブタブ化 → 描画・入力を停止せよ
);                             // 戻り値: ハンドル (失敗時 null)
void Unregister(object handle);
void NotifyHeaderMouseDown(object handle); // ヘッダー左押下の通知

// --- スナップ/コネクト系（後発。旧ホストには存在しない） ---
void EnableConnect(object handle);        // コネクト参加のオプトイン宣言
void NotifyDragMouseDown(object handle);  // ヘッダー/空き領域の左押下 = ドラッグ起点
bool IsSnapDragging(object handle);       // ドラッグ中にいずれかの軸が吸着しているか
bool HasAdjacent(object handle);          // 辺が密着している窓があるか (ボタン表示条件)
bool IsConnected(object handle);          // コネクトグループ所属中か
void ToggleConnect(object handle);        // 連結トグル (連結中なら自分だけ外れる)

// --- タブバー描画系（後発。旧ホストには存在しない） ---
void EnableTabBar(object handle, Action<string[], int> onTabBarChanged);
                                          // タブバー描画のオプトイン宣言。
                                          // 加入/離脱/アクティブ切替のたびに
                                          // (タブ名一覧, アクティブindex) が push される。
                                          // titles=null はグループ非加入
void NotifyTabMouseDown(object handle, int tabIndex, float x, float y);
                                          // ゲストが描いたタブの押下通知
                                          // (x/y はウィンドウローカルの押下位置)
void ActivateTab(object handle);          // 自窓のタブをアクティブへ切り替える
                                          // (押下由来でないためドラッグ候補は記録しない。
                                          //  グループ非加入なら何もしない)

// --- リサイズ吸着（後発。旧ホストには存在しない） ---
Rect SnapResize(object handle, Rect rect, int edges);
                                          // リサイズ中の矩形へ辺スナップを適用して返す。
                                          // edges はつかんでいる辺のビット
                                          // (Left=1, Right=2, Top=4, Bottom=8)
```

ゲスト側の実装義務:

1. **表示開始時に `Register`、非表示化・破棄時に `Unregister`** を呼ぶ。
   `isVisible` は「**実際に描画しているか**」を返すこと。ホストはこれを
   ドロップ先候補・自動再ドッキングの判定に使うため、描いていない窓が true を返すと
   見えない窓へドッキングでき、タブを切り替えた瞬間にグループごと消えたように見える。
   プラグイン無効中・シーン制限中など「登録はあるが描かない」状態を持つなら、
   その条件も `isVisible` へ含めるか、その間は `Unregister` しておくこと
2. **ヘッダー左押下（閉じるボタン除く）で `NotifyHeaderMouseDown`** を呼ぶ。
   イベントは消費せず、自前の `GUI.DragWindow` にそのまま使わせてよい。
   これがドッキング判定（ドラッグして他窓のヘッダーへ重ねる → 統合）の起点になる
3. **`setTabVisible(false)` を受けたら描画と入力を止める**（`OnGUI` 早期 return）。
   `true` で再開する。矩形はホストが `setRect` で同期し続けるので保持すること
4. ヘッダー高さは **26px** に合わせる（タブ列を描く領域）

### タブバー描画の実装義務

タブバー系 2 メソッドも**ペアで検出すること**（欠けるホストでは機能ごと無効にする）。
未対応でもタブドッキング自体は従来どおり動く（タブバーが出ないだけ）。

1. **`Register` 直後に `EnableTabBar`** を呼び、受け取った `(titles, activeIndex)` を保持する。
   `titles == null` はグループ非加入なので通常タイトル表示へ戻す
2. **`titles != null` の間は `GUI.Window` のタイトルを空文字にし**、
   ヘッダーへ自前でタブ列を描く。位置は左端 `FRAME`、縦中央、
   タブ幅は 90px 上限（右のボタン列分を差し引いた幅に収める）
3. **タブ押下で `NotifyTabMouseDown(handle, index, x, y)`** を呼び、イベントを消費する
   （消費しないとウィンドウ全体のドラッグが始まる）。アクティブ切替・つまみドラッグ分離は
   ホスト側で処理される
4. 見た目を内部窓と揃えたい場合は MTEUtils の `TabBarDrawer.Draw(...)` をそのまま使える
5. プログラムからタブを前面へ出したい場合は `ActivateTab(handle)` を使う。
   `NotifyTabMouseDown` はつまみドラッグ候補の記録を伴うため、実クリック以外から
   呼んではいけない（次フレームでタブが分離してカーソルへ吸い付く）。
   `ActivateTab` も後発 API なので、単独で存在検出して欠けるホストでは無効化すること

### スナップ/コネクトの実装義務

受け身スナップ（内部窓がゲスト窓へ吸着する側）は `Register` だけで有効なので、
以下はゲスト窓自身を吸着・連結させたい場合のみ必要。

スナップ/コネクト系 6 メソッドは**一括で検出すること**（1 つでも欠けるホストでは機能ごと無効にする）。
部分的に使うと「スナップは効くがボタンは出ない」といった中間状態になる。

1. **`Register` 直後に `EnableConnect`** を呼ぶ。これがコネクト参加のオプトイン宣言で、
   宣言しないゲストはコネクトの候補から外れる（下記 5 を実装できていない前提とみなされる）
2. **ヘッダー左押下と空き領域の左押下で `NotifyDragMouseDown`** を呼ぶ。ドラッグ追跡の起点
3. **`IsSnapDragging` が true の間は `GUI.DragWindow` を呼ばない**。
   マウス追従位置と吸着位置がフレームごとに行き来して表示がばたつくため、
   吸着中の配置はホストの絶対配置だけに任せる
4. **`IsConnected || HasAdjacent` のときヘッダーへコネクトボタンを描き**、押下で `ToggleConnect` を呼ぶ。
   位置は閉じるボタンの左隣（幅 20px + マージン 2px）、連結中は `Color.cyan` で塗る。
   タブ列の利用可能幅からこの分を差し引くのはゲスト自身の責務
   （差し引かないとタブ列が食い込んでボタンを押せなくなる）
5. **`IsConnected` が true の間は自前の画面内クランプを行わない**。
   個別にクランプするとメンバー間のオフセットが壊れる。連結中はホストが
   群のバウンディングボックスでまとめてクランプする

### リサイズ吸着の実装義務

`SnapResize` は単独で完結するため、上記 6 メソッドとは**別に検出すること**
（欠けるホストではリサイズ吸着だけが無効になり、他の機能は従来どおり動く）。

1. **リサイズ中に毎フレーム `SnapResize(handle, rect, edges)`** を呼び、戻り値を採用する。
   `rect` は吸着前（マウス位置どおり）の矩形、`edges` はつかんでいる辺のビット。
   ホストはつかんでいる辺だけを画面枠・他ウィンドウの辺へ吸着させ、対辺は動かさない
2. **吸着で最小サイズを割り込む軸は吸着を捨てる**。最小サイズへ丸めると
   狙った辺とは別の位置へ貼り付いて見える
3. 移動ドラッグと違い矩形はマウス位置から毎フレーム組み直されるため、
   吸着結果が次フレームの入力へ混入せず、ヒステリシス（解除距離）は不要

## 挙動の詳細・注意点

- **`setRect` はホスト都合で呼ばれる**: タブグループ所属中は矩形がグループと常時同期される。
  ゲストが自前で矩形を保存する場合はこの書き換えを前提にすること
- **windowId の注意**: 8903349〜8903377（内部ウィンドウ）は SceneEditor の予約帯。
  この帯や他プラグインと重複しない一意な ID を使う（重複時はホストがログ警告を出す）。
  ID は保存・復元でウィンドウを特定するキーにもなるため、リリース後は変えない
- **同じ windowId で再 Register** すると古い登録は自動解除される（プラグインリロード対策）
- **デリゲートが例外を投げても** ホストは巻き込まれない（try/catch 済み）が、
  ログ（MTEUtils.LogException）に出るのでゲスト側で潰しておくこと
- **ヘッダーはゲストが全面的に描く**。タブ列に使ってよい幅は
  ウィンドウ幅 − 枠 2 × 4px − 閉じるボタン分 24px（コネクトボタン表示中はさらに 22px）
- **Editor モードが OFF になると** グループは解体され、ゲストは独立ウィンドウへ戻される
  （`setTabVisible(true)` が届く）。登録自体は維持されるので再登録は不要
- **名前付きレイアウトを適用したときも同様にグループは解体される**（ゲストは独立ウィンドウへ戻る）。
  レイアウトが復元するのは内部窓の配置・表示とそのグループ構成だけで、ゲスト窓の矩形は触られない
- **ゲスト窓の再表示時は自動で再ドッキングされることがある**: 非表示 → 表示のときに、
  ヘッダー位置が数 px 以内で一致する表示中ウィンドウがあればそのグループへ統合される。
  ドッキング解除して出したいなら、再表示前に矩形をずらしておくこと
- タブのクリック切替・つまみドラッグ分離・グループ移動ドラッグの判定はすべて
  ホスト側（TabGroupManager）が処理する。ゲストは押下位置の通知だけ行えばよい
- **ゲスト窓が非表示になるとコネクトグループからも自動で外れる**（ホストがポーリングする）。
  `Unregister` 時・Editor モード OFF 時も同様に解除される
- **タブ統合とコネクトは独立した層**。タブグループ丸ごとが 1 ノードとしてコネクトへ参加するため、
  統合中の窓を連結しても、分離しても破綻しない

## 動作確認の手順（devbridge REPL での擬似ゲスト）

実プラグインを書く前に、MCP `com3d25-devbridge` の `eval_csharp` でダミー登録して
挙動を確認できる（2026-08-11 の実機検証で使用した手順）:

```csharp
var t = System.Type.GetType("COM3D2.SceneEditor.Plugin.DockingHost, COM3D2.SceneEditor.Plugin");
var rect = new UnityEngine.Rect(1300, 200, 398, 312);
System.Func<UnityEngine.Rect> getR = () => rect;
System.Action<UnityEngine.Rect> setR = r => { rect = r; };
System.Func<bool> isV = () => true;
System.Action<bool> setTV = v => { /* 描画停止指示 */ };
var handle = t.GetMethod("Register").Invoke(null, new object[] { 900001, "外部テスト", getR, setR, isV, setTV });
// ... TabGroupManager.Merge をリフレクションで呼べばドラッグなしで統合テストできる
t.GetMethod("Unregister").Invoke(null, new object[] { handle });
```

## 関連ソース

| ファイル | 内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/DockingHost.cs` | 公開 API + ExternalWindowAdapter |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarDrawer.cs` | 状態ベースのタブ列描画（内部窓・ゲスト窓で共有） |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowConnectManager.cs` | スナップ吸着・コネクトグループの管理 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockingClient.cs` | リフレクションブリッジ（ゲスト側の参考実装） |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs` | ゲスト向けウィンドウ基底 |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowLayoutManager.cs` | 名前付きレイアウトの保存 / 適用（内部窓のみ） |

## 他の連携 API

同じリフレクションブリッジ方式で、以下のホスト API も公開されている
（型名は `COM3D2.SceneEditor.Plugin.<型>`。MTEUtils 側に対応クライアントがある）。

| ホスト型 | MTEUtils クライアント | 用途 | ガイド |
|---|---|---|---|
| `HistoryAPI` | `HistoryClient` | 操作履歴（undo/redo）への参加 | [操作履歴連携](history-guest-guide.md) |
| `EditorStateHost` | `EditorStateClient` | SceneEditor の有効/無効への追従 | [有効/無効の連動](editor-state-guest-guide.md) |
| `MaidSelectHost` | `MaidSelectClient` | 選択中メイドの共有（読み取り・購読・外部からの選択変更） | [選択中メイドの共有](maid-select-guest-guide.md) |
| `GizmoHost` | `GizmoHostClient` | 外部ギズモを SceneView / GameView の入力・描画へ参加させる | [ギズモ連携](gizmo-guest-guide.md) |
| `InspectorHost` | `InspectorHostClient` | Inspector の内容描画の委譲 | [Inspector 描画の委譲](inspector-guest-guide.md) |
| `SelectionManager` | `SelectionClient` | 選択オブジェクトの取得・変更通知・選択時の SceneView フォーカス指定 | [その他の連携](misc-guest-guide.md) |
| `GizmoRenderer` | `GizmoToolClient` | ギズモツール（移動 / 回転 / 拡縮）の状態共有 | [その他の連携](misc-guest-guide.md) |
| `InputRemapper` | `InputRemapperClient` | GameView 上のマウス座標取り違え防止 | [その他の連携](misc-guest-guide.md) |

シーンプリセットへの相乗りだけは属性ベースの自動発見で、リフレクション接続は不要
（[シーンプリセット連携](scene-preset-provider-guide.md)）。
