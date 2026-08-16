# ウィンドウロックボタン (コネクトボタン置き換え) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ヘッダーのコネクトボタン（◆/◇）を削除し（コネクト機能自体は残す）、代わりに常時表示のロックボタンを配置して、ロック中はウィンドウの移動・リサイズを禁止する（誤動作防止）。

**Architecture:** ボタン描画箇所は 3 つ（`EditorSubWindow` / `GameViewWindow` / `DockableWindowBase`）。各ウィンドウに `isLocked` 状態を持たせ、ロック中は (1) リサイズ開始 (`_resize.TryBegin`)、(2) 移動ドラッグ (`GUI.DragWindow`)、(3) ドッキング/スナップのドラッグ起点通知、(4) リサイズカーソル表示、をすべてスキップする。内部ウィンドウのロック状態は `Config` に windowId 単位で永続化する。外部プラグイン用共有基底 `DockableWindowBase` は仮想フック経由で永続化可能にする（既定はセッション内のみ）。

**Tech Stack:** C# (.NET 3.5 相当 / Unity IMGUI)、XmlSerializer ベースの Config

**Spec:** 本ファイル冒頭の Goal / 仕様詳細（別ファイルなし。ユーザー指示: 「コネクト機能をオミット(機能はそのままでボタンだけ削除)。代わりにロックボタンを配置して、ロックするとウィンドウサイズ/位置を変更できなくする」）

## 仕様詳細

- コネクト機能（`WindowConnectManager` のグループ管理・`ToggleConnect`・`HasAdjacent`・config の `connectGroups`・保存済みグループの復元）は**一切削除しない**。UI からトグル手段がなくなるだけ。
- ロックボタンは**常時表示**（コネクトボタンは隣接時のみ表示だったが、ロックはいつでも押せる必要がある）。位置はコネクトボタンと同じ（閉じる/最大化ボタンの左隣）。
- ボタンの見た目は既存のコネクトボタンをそのまま流用する: ロック中 `"◆"` + `ACCENT_COLOR`（シアン）、非ロック中 `"◇"` + 白。変わるのは押したときの処理（コネクトトグル→ロックトグル）と表示条件（隣接時のみ→常時）だけ。
- ロック中もできること: 閉じる、タブ切り替え、コンテンツ操作、レイアウト適用 (`ApplyPlacement`) や画面サイズ変更 (`RestorePlacement`) による再配置（明示操作・自動補正はロック対象外）。
- ロック中にできないこと: マウスによる移動ドラッグ、リサイズ、ドッキング/スナップ/タブ統合のドラッグ起点になること。
- 既知の許容仕様: 保存済みコネクトグループのメンバーとして他ウィンドウのドラッグに連動して動く場合、およびタブ列のタブドラッグによる分離は、ロックでは抑止しない（マネージャー駆動の再配置であり誤クリック起因ではないため）。
- 意図的な副作用: `IsOverResizeHandle` は SceneViewWindow / InputRemapper で「リサイズハンドル帯を 3D シーン操作の対象外にする」判定にも使われている。ロック中はハンドルが存在しない扱い（常に false）になるため、縁の帯も 3D シーン操作の有効領域になる。これは「ロック中はリサイズ UI が存在しない」仕様として意図した挙動。

## Global Constraints

- ビルドコマンド: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`
- 同じソースが COM3D2 (2.0) と COM3D2.5 の両方にビルドされる。`ビルドに成功しました` が 2 回・`0 エラー` であること（ゲーム起動中はデプロイのみ失敗するが想定内）
- **テストフレームワークは存在しない**。検証は「ビルドが通ること」＋「実機での目視確認」
- コードコメント・ログは日本語
- コミットは Conventional Commits 形式の日本語メッセージ
- `MTEUtils/DockableWindowBase.cs` は外部プラグイン（例: COM3D2.ModItemExplorer.Plugin）へコピーされる共有コード。**他プラグイン側コピーへの反映は本タスクの対象外**（別途フォローアップ）

---

### Task 1: Config にロック状態の永続化を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`connectGroups` フィールド付近、262-269 行あたり）

**Interfaces:**
- Produces: `bool Config.IsWindowLocked(int windowId)` / `void Config.SetWindowLocked(int windowId, bool locked)`（Task 2, 3 が使用）

- [ ] **Step 1: フィールドとヘルパーを追加**

`windowScreens` フィールド定義の直後に追加:

```csharp
// ロック中 (移動・リサイズ禁止) のウィンドウ ID。1 要素 = 1 ウィンドウ
[XmlElement("lockedWindow")]
public List<int> lockedWindows = new List<int>();

/// <summary>windowId のウィンドウがロック中か</summary>
public bool IsWindowLocked(int windowId)
{
    return lockedWindows.Contains(windowId);
}

/// <summary>windowId のロック状態を記録する</summary>
public void SetWindowLocked(int windowId, bool locked)
{
    if (locked == lockedWindows.Contains(windowId))
    {
        return;
    }
    if (locked)
    {
        lockedWindows.Add(windowId);
    }
    else
    {
        lockedWindows.Remove(windowId);
    }
}
```

- [ ] **Step 2: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`
Expected: `error CS` 0 件、`ビルドに成功しました` 2 回

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Config.cs
git commit -m "feat(config): ウィンドウロック状態の永続化を追加"
```

---

### Task 2: EditorSubWindow のコネクトボタンをロックボタンへ置き換え

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs`

**Interfaces:**
- Consumes: `config.IsWindowLocked` / `config.SetWindowLocked`（Task 1）
- Produces: `public bool isLocked { get; }`（外部からの参照は現状なし）

- [ ] **Step 1: 定数リネームと isLocked プロパティ追加**

`CONNECT_BUTTON_WIDTH`（21 行目）を `LOCK_BUTTON_WIDTH` にリネーム（ファイル内の参照 3 箇所も置換）。

`isWndVisible` プロパティ付近に追加:

```csharp
/// <summary>ロック中 (移動・リサイズ禁止) か。誤動作防止用で config に永続化する</summary>
public bool isLocked => config.IsWindowLocked(windowId);

private void ToggleLock()
{
    config.SetWindowLocked(windowId, !isLocked);
    config.dirty = true;
}
```

- [ ] **Step 2: DrawWindow / DrawHeaderButtons / DrawTabBar の書き換え**

`DrawWindow` 冒頭の `showConnectButton` 判定（248-249 行）を削除し、`DrawTabBar(showConnectButton)` → `DrawTabBar()`、`DrawHeaderButtons(closeRect, showConnectButton)` → `DrawHeaderButtons(closeRect)` に変更。さらにロック中は入力処理を丸ごとスキップする:

```csharp
private void DrawWindow(int id)
{
    DrawContent();
    DrawToolbar();

    if (_tabTitles != null)
    {
        DrawTabBar();
    }

    // 閉じるボタン (ヘッダー右端)。ヘッダー高さを変えても縦中央に来るようにする
    var closeRect = new Rect(
        _windowRect.width - CLOSE_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN * 2,
        (HEADER_HEIGHT - CLOSE_BUTTON_HEIGHT) * 0.5f,
        CLOSE_BUTTON_WIDTH,
        CLOSE_BUTTON_HEIGHT);
    if (!DrawHeaderButtons(closeRect))
    {
        // 閉じられたウィンドウには以降の入力判定を走らせない
        return;
    }

    DrawDropHighlight();

    // ロック中は移動・リサイズ・ドッキング起点の入力を受け付けない (誤動作防止)
    if (!isLocked)
    {
        HandleDragInput(closeRect);
    }
}
```

`DrawHeaderButtons` のコネクトボタン部分（290-307 行）をロックボタンに置換:

```csharp
private bool DrawHeaderButtons(Rect closeRect)
{
    // グループ時はアクティブタブだけを閉じる
    if (GUI.Button(closeRect, "x"))
    {
        isShowWnd = false;
        TabGroupManager.instance.RemoveFromGroup(this);
        WindowConnectManager.instance.OnWindowHidden(this);
        return false;
    }

    // ロックボタン (閉じるボタンの左隣)。ロック中はアクセントカラーで状態を示す
    var lockRect = new Rect(
        closeRect.x - LOCK_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN,
        closeRect.y,
        LOCK_BUTTON_WIDTH,
        CLOSE_BUTTON_HEIGHT);
    var oldColor = GUI.color;
    // ロック中はアクセントカラーで塗って状態を示す
    GUI.color = isLocked ? ACCENT_COLOR : Color.white;
    if (GUI.Button(lockRect, isLocked ? "◆" : "◇"))
    {
        ToggleLock();
    }
    GUI.color = oldColor;

    return true;
}
```

`DrawTabBar`（385-399 行）は引数なしにし、ロックボタンは常時表示なので幅を常に確保する:

```csharp
/// <summary>グループ時のタブ列。描画は MTEUtils の TabBarDrawer と共通</summary>
private void DrawTabBar()
{
    // タブ列がヘッダー右のボタン (閉じる + ロック) へ食い込まないよう、利用可能幅を先に確定する
    var available = _windowRect.width - FRAME * 2
        - (CLOSE_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN * 2)
        - (LOCK_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN);

    TabBarDrawer.Draw(
        _tabTitles, _tabActiveIndex,
        FRAME, (HEADER_HEIGHT - TabBarDrawer.TAB_HEIGHT) * 0.5f, available,
        (index, pos) => TabGroupManager.instance.OnTabPressed(this, index, pos));
}
```

- [ ] **Step 3: リサイズカーソルとつかみ判定をロックで無効化**

```csharp
public bool IsOverResizeHandle(Vector2 guiPos)
{
    // ロック中はリサイズ不可なのでつかみ範囲も存在しない扱いにする
    return !isLocked && _resize.IsOverHandle(_windowRect, guiPos);
}

public ResizeCursor.Kind desiredCursorKind =>
    _resize.GetCursorKind(
        _windowRect, isWndVisible && gameViewManager.isWindowMode && !isLocked, windowId);
```

- [ ] **Step 4: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`
Expected: `error CS` 0 件、`ビルドに成功しました` 2 回

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs
git commit -m "feat(window): サブウィンドウのコネクトボタンをロックボタンへ置き換える"
```

---

### Task 3: GameViewWindow のコネクトボタンをロックボタンへ置き換え

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs`

**Interfaces:**
- Consumes: `config.IsWindowLocked` / `config.SetWindowLocked`（Task 1）

- [ ] **Step 1: 定数リネームと isLocked 追加**

`CONNECT_BUTTON_WIDTH`（24 行目）を `LOCK_BUTTON_WIDTH` にリネーム。クラス内に追加:

```csharp
/// <summary>ロック中 (移動・リサイズ禁止) か。誤動作防止用で config に永続化する</summary>
public bool isLocked => config.IsWindowLocked(WINDOW_ID);

private void ToggleLock()
{
    config.SetWindowLocked(WINDOW_ID, !isLocked);
    config.dirty = true;
}
```

（`config` プロパティが未定義なら `ConfigManager.instance.config` を参照する既存の書き方に合わせる）

- [ ] **Step 2: DrawWindow のボタン・入力処理を書き換え**

コネクトボタンブロック（195-215 行）をロックボタンに置換し、ロック中は入力処理をスキップする:

```csharp
// ロックボタン (最大化ボタンの左隣)。ロック中はアクセントカラーで状態を示す
var lockRect = new Rect(
    maximizeRect.x - LOCK_BUTTON_WIDTH - HEADER_BUTTON_MARGIN,
    maximizeRect.y,
    LOCK_BUTTON_WIDTH,
    HEADER_BUTTON_HEIGHT);
var buttonsLeft = lockRect.x;

var oldColor = GUI.color;
GUI.color = isLocked ? EditorSubWindow.ACCENT_COLOR : Color.white;
if (GUI.Button(lockRect, isLocked ? "◆" : "◇"))
{
    ToggleLock();
}
GUI.color = oldColor;

// ロック中は移動・リサイズ・スナップ起点の入力を受け付けない (誤動作防止)
if (isLocked)
{
    return;
}

// モード終了ボタンは先に描いているので、重なる右上角より優先して押せる。
// 右ドラッグ (カメラ回転) でリサイズが始まらないよう左ボタンに限定する
var e = Event.current;
if (e.type == EventType.MouseDown && e.button == 0 &&
    _resize.TryBegin(_windowRect, e.mousePosition))
{
    e.Use();
}

// ヘッダー上の左押下をスナップ・コネクトのドラッグ起点として通知する
// (ボタン列の上は除く)。イベントは消費せず GUI.DragWindow の移動に使わせる
if (e.type == EventType.MouseDown && e.button == 0 &&
    e.mousePosition.y <= HEADER_HEIGHT && e.mousePosition.x < buttonsLeft)
{
    WindowConnectManager.instance.OnDragMouseDown(this);
}

// 吸着中はマネージャーの配置だけに任せる (理由は IsSnapDragging 参照)
if (!_resize.isResizing &&
    !WindowConnectManager.instance.IsSnapDragging(this))
{
    GUI.DragWindow(new Rect(0, 0, _windowRect.width, HEADER_HEIGHT));
}
```

- [ ] **Step 3: リサイズカーソルとつかみ判定をロックで無効化**

```csharp
public bool IsOverResizeHandle(Vector2 guiPos)
{
    // ロック中はリサイズ不可なのでつかみ範囲も存在しない扱いにする
    return !isLocked && _resize.IsOverHandle(_windowRect, guiPos);
}

public ResizeCursor.Kind desiredCursorKind =>
    _resize.GetCursorKind(
        _windowRect, isShowWnd && gameViewManager.isWindowMode && !isLocked, WINDOW_ID);
```

- [ ] **Step 4: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`
Expected: `error CS` 0 件、`ビルドに成功しました` 2 回

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/GameViewWindow.cs
git commit -m "feat(gameview): GameView のコネクトボタンをロックボタンへ置き換える"
```

---

### Task 4: DockableWindowBase (外部プラグイン共有基底) の置き換え

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs`

**Interfaces:**
- Produces: `protected virtual bool LoadLocked()` / `protected virtual void StoreLocked(bool locked)`（外部プラグインが永続化したい場合にオーバーライドする。既定はセッション内のみ）

- [ ] **Step 1: 定数・色のリネームとロック状態の追加**

- `CONNECT_BUTTON_WIDTH`（23 行目）→ `LOCK_BUTTON_WIDTH`
- `CONNECT_ACCENT_COLOR`（25 行目）→ `LOCK_ACCENT_COLOR`（コメントも「ロック中表示のアクセント色。ホスト (EditorSubWindow.ACCENT_COLOR) と揃えること」に更新）

クラス内に追加（`LoadPlacement` / `StorePlacement` の隣に置く）:

```csharp
/// <summary>ロック状態の復元。既定では永続化しない</summary>
protected virtual bool LoadLocked()
{
    return false;
}

/// <summary>ロック状態の保存。既定では何もしない</summary>
protected virtual void StoreLocked(bool locked)
{
}

/// <summary>ロック中 (移動・リサイズ禁止) か。誤動作防止用</summary>
private bool _isLocked;
public bool isLocked => _isLocked;

private void ToggleLock()
{
    _isLocked = !_isLocked;
    StoreLocked(_isLocked);
}
```

`Init()` の末尾に `_isLocked = LoadLocked();` を追加。

- [ ] **Step 2: DrawWindowInternal / DrawHeaderButtons / DrawTabBar の書き換え**

`DrawWindowInternal`（217-244 行）から `isConnected` / `showConnectButton` 判定を削除し、末尾をロックガードにする:

```csharp
private void DrawWindowInternal(int id)
{
    DrawContent();

    if (_tabTitles != null)
    {
        DrawTabBar();
    }

    // 閉じるボタン (ヘッダー右端)
    var closeRect = new Rect(
        _windowRect.width - CLOSE_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN * 2,
        (HEADER_HEIGHT - CLOSE_BUTTON_HEIGHT) * 0.5f,
        CLOSE_BUTTON_WIDTH,
        CLOSE_BUTTON_HEIGHT);
    if (!DrawHeaderButtons(closeRect))
    {
        // 閉じられたウィンドウには以降の入力判定を走らせない
        return;
    }

    // ロック中は移動・リサイズ・ドッキング起点の入力を受け付けない (誤動作防止)
    if (!_isLocked)
    {
        HandleDragInput(closeRect);
    }
}
```

`DrawHeaderButtons`（267-295 行）:

```csharp
/// <summary>
/// ヘッダー右のボタン列を描く。閉じるボタンが押されたら false を返す。
/// 構成・見た目は内部窓 (EditorSubWindow.DrawHeaderButtons) と揃える
/// </summary>
private bool DrawHeaderButtons(Rect closeRect)
{
    if (GUI.Button(closeRect, "x"))
    {
        Close();
        return false;
    }

    // ロックボタン (閉じるボタンの左隣)。見た目・条件は内部窓と揃える
    var lockRect = new Rect(
        closeRect.x - LOCK_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN,
        closeRect.y,
        LOCK_BUTTON_WIDTH,
        CLOSE_BUTTON_HEIGHT);
    var oldColor = GUI.color;
    // ロック中はアクセントカラーで塗って状態を示す
    GUI.color = _isLocked ? LOCK_ACCENT_COLOR : Color.white;
    if (GUI.Button(lockRect, _isLocked ? "◆" : "◇"))
    {
        ToggleLock();
    }
    GUI.color = oldColor;

    return true;
}
```

`DrawTabBar`（247-261 行）は引数なしにし、幅を常に確保する:

```csharp
/// <summary>グループ時のタブ列。構成・見た目は内部窓 (EditorSubWindow.DrawTabBar) と揃える</summary>
private void DrawTabBar()
{
    // タブ列がヘッダー右のボタン (閉じる + ロック) へ食い込まないよう、利用可能幅を先に確定する
    var available = _windowRect.width - FRAME * 2
        - (CLOSE_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN * 2)
        - (LOCK_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN);

    TabBarDrawer.Draw(
        _tabTitles, _tabActiveIndex,
        FRAME, (HEADER_HEIGHT - TabBarDrawer.TAB_HEIGHT) * 0.5f, available,
        (index, pos) => DockingClient.NotifyTabMouseDown(_dockHandle, index, pos.x, pos.y));
}
```

- [ ] **Step 3: リサイズカーソルをロックで無効化**

```csharp
public ResizeCursor.Kind desiredCursorKind =>
    _resize.GetCursorKind(
        _windowRect, _isShowWnd && !_dockTabHidden && !_isLocked, windowId);
```

- [ ] **Step 4: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`
Expected: `error CS` 0 件、`ビルドに成功しました` 2 回

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs
git commit -m "feat(dockable): 外部窓基底のコネクトボタンをロックボタンへ置き換える"
```

---

### Task 5: 実機確認

**Files:** なし（検証のみ）

- [ ] **Step 1: ゲーム稼働確認**

MCP `com3d25-devbridge` の `ping` でゲームの生存を確認する。起動していなければユーザーに起動を依頼するか、目視確認をユーザーへ依頼して完了とする。

- [ ] **Step 2: 目視確認項目（ユーザー依頼 or screenshot で確認）**

1. 各ウィンドウ（SceneView / Hierarchy / Inspector / GameView）のヘッダーに ◇ ボタンが**常時**表示されること（隣接していなくても出ること）
2. ◇ を押すと ◆（シアン）に変わり、その状態でヘッダードラッグ・辺/隅ドラッグをしても動かない/リサイズしないこと（リサイズカーソルも出ないこと）
3. もう一度押すと解除され、移動・リサイズが復活すること
4. ロック中も「x」で閉じられること
5. ゲーム再起動（または Editor 終了→再開）後もロック状態が復元されること（内部ウィンドウのみ）
6. タブグループ化した状態（複数窓を 1 グループに統合）で、ロックボタンとタブ列が重ならず表示されること。ロック中の窓と非ロックの窓が混在するグループでも表示が崩れないこと
7. ロック中、ウィンドウ縁のリサイズハンドル帯で 3D シーン操作（カメラ等）が反応するようになること（意図的な副作用、仕様詳細参照）
