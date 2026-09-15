# ドッキングUI改善 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タブドッキングの操作性を改善する — (1) タブ幅下限 + `<` `>` スクロール、(2) タブバー右クリックのタブ切替メニュー、(3) タブ左右ドラッグによるグループ内並び替え、(4) 外部プラグイン窓のドッキング構成の Config 復元。

**Architecture:** タブ列のレイアウト計算を純粋関数 `TabBarLayout`（MTEUtils 共有ソース）へ抽出し、描画は `TabBarDrawer` に残す。並び替え・復元はホスト側（`TabGroup` / `TabGroupManager`）のリスト操作で行い、ゲストへは既存の `PushTabBarState` 経路で伝搬させる（旧ゲストもそのまま動く）。右クリックメニューの「他タブのアクティブ化」だけは新ホスト API `ActivateTabIndex` が必要（既存 `NotifyTabMouseDown` はつまみドラッグ候補を記録するため誤分離を招く）。

**Tech Stack:** C# (.NET Framework / Unity IMGUI)。純粋ロジックは xunit（`source/COM3D2.SceneEditor.Plugin.Tests`、net48、COM3D25 ビルドの DLL 参照）でテスト。GUI 描画・入力はテスト対象外でビルド検証。

**Spec:** 本計画の冒頭要件 4 点（ユーザー指示）。設計判断は各タスクの doc コメント方針に記載。

## Global Constraints

- ビルドは **COM3D2 / COM3D25 の両方**を必ず通すこと。`debug.bat` はゲームフォルダへ DLL をコピーするため使わず、MSBuild を直接叩く:
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
  ```
- テスト実行はプラグインの **COM3D25 構成ビルド後**に `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`（Tests.csproj が COM3D25 出力を参照するため順序必須）。
- コメント・ログ文言は日本語。既存コードのコメント密度・流儀（「なぜ」を書く doc コメント）に合わせる。
- `MTEUtils/` 配下（`TabBarDrawer` / `TabBarLayout` / `DockableWindowBase` / `DockingClient`）はゲストプラグインへコピーされる共有ソース。**ホスト↔ゲスト間の契約はプリミティブ + デリゲートのみ**（型は DLL 間で共有できない）。`DockingHost` の公開メソッドはシグネチャ変更禁止（追加のみ可）。
- ヘッダーの既存定数（両クラスで同値）: `HEADER_HEIGHT=26, FRAME=4, CLOSE_BUTTON_WIDTH=20, CLOSE_BUTTON_HEIGHT=16, CLOSE_BUTTON_MARGIN=2, LOCK_BUTTON_WIDTH=20`、タブ: `TAB_WIDTH=90, TAB_HEIGHT=20, TAB_MARGIN=2`。

---

### Task 1: TabBarLayout — タブ列レイアウトの純粋計算 + テスト

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarLayout.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`MTEUtils\TabBarDrawer.cs` の隣に `<Compile Include="MTEUtils\TabBarLayout.cs" />` を追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TabBarLayoutTests.cs`

**Interfaces:**
- Produces: `COM3D2.MotionTimelineEditor.TabBarLayout`（public static class）
  - `struct Result { public float tabWidth; public bool scrollable; public int firstVisible; public int visibleCount; public float tabsOriginX; }`
  - `Result Calc(int count, float availableWidth, int scrollOffset, int activeIndex)`
  - `float CalcAvailableWidth(float headerWidth)` — ヘッダー幅からタブ列の利用可能幅（左右フレームと閉じる/ロックボタン領域を除いた値）を求める。描画側 2 箇所と並び替え（Task 4 の TabGroupManager）で同じ式を 3 重複させないための集約
  - 定数 `public const float MIN_TAB_WIDTH = 60f; public const float SCROLL_BUTTON_WIDTH = 16f;`
- 仕様:
  - 非スクロール時: `tabWidth = min(TabBarDrawer.TAB_WIDTH, (availableWidth - TAB_MARGIN*(count-1)) / count)`（現行式と同じ）。`tabWidth >= MIN_TAB_WIDTH` ならそのまま `scrollable=false, firstVisible=0, visibleCount=count, tabsOriginX=0`。
  - スクロール時（上式が MIN_TAB_WIDTH を下回る場合）: `scrollable=true, tabWidth=MIN_TAB_WIDTH`。タブ領域は両端の `<` `>` ボタン分を除いた `availableWidth - (SCROLL_BUTTON_WIDTH + TAB_MARGIN)*2`。`visibleCount = max(1, floor((領域 + TAB_MARGIN) / (MIN_TAB_WIDTH + TAB_MARGIN)))`。`tabsOriginX = SCROLL_BUTTON_WIDTH + TAB_MARGIN`。
  - `firstVisible` は `scrollOffset` を `[0, count - visibleCount]` へクランプした後、`activeIndex >= 0` なら activeIndex が `[firstVisible, firstVisible + visibleCount - 1]` に入るよう自動追従（左に外れたら firstVisible=activeIndex、右に外れたら firstVisible=activeIndex-visibleCount+1）。
  - `count <= 0` は `visibleCount=0` を返す（描画側は何もしない）。

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TabBarLayoutTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using Xunit;

public class TabBarLayoutTests
{
    // 収まる枚数なら従来通り縮小のみでスクロールしない
    [Fact]
    public void FitsWithoutScroll()
    {
        // 3枚 * (90+2) は 300 に収まらないので縮小されるが MIN(60) は上回る
        var r = TabBarLayout.Calc(3, 300f, 0, 0);
        Assert.False(r.scrollable);
        Assert.Equal(0, r.firstVisible);
        Assert.Equal(3, r.visibleCount);
        Assert.True(r.tabWidth >= TabBarLayout.MIN_TAB_WIDTH);
        Assert.Equal(0f, r.tabsOriginX);
    }

    // 下限を割る枚数ならスクロールモードへ入り、幅は MIN 固定
    [Fact]
    public void EntersScrollMode()
    {
        // 8枚 を 300px へ: (300 - 2*7)/8 = 35.75 < 60
        var r = TabBarLayout.Calc(8, 300f, 0, 0);
        Assert.True(r.scrollable);
        Assert.Equal(TabBarLayout.MIN_TAB_WIDTH, r.tabWidth);
        // タブ領域 = 300 - (16+2)*2 = 264 → floor((264+2)/62) = 4
        Assert.Equal(4, r.visibleCount);
        Assert.Equal(TabBarLayout.SCROLL_BUTTON_WIDTH + 2f, r.tabsOriginX);
    }

    // scrollOffset は範囲へクランプされる
    [Fact]
    public void ClampsScrollOffset()
    {
        var r = TabBarLayout.Calc(8, 300f, 99, -1);
        Assert.Equal(4, r.firstVisible); // 8 - 4
        var r2 = TabBarLayout.Calc(8, 300f, -5, -1);
        Assert.Equal(0, r2.firstVisible);
    }

    // アクティブタブが見えるよう自動追従する
    [Fact]
    public void FollowsActiveTab()
    {
        // 右へ外れているケース: active=7, offset=0 → firstVisible = 7-4+1 = 4
        var r = TabBarLayout.Calc(8, 300f, 0, 7);
        Assert.Equal(4, r.firstVisible);
        // 左へ外れているケース: active=1, offset=4 → firstVisible = 1
        var r2 = TabBarLayout.Calc(8, 300f, 4, 1);
        Assert.Equal(1, r2.firstVisible);
    }

    [Fact]
    public void EmptyReturnsZeroVisible()
    {
        var r = TabBarLayout.Calc(0, 300f, 0, -1);
        Assert.Equal(0, r.visibleCount);
    }

    // ヘッダー幅→利用可能幅: フレーム*2 + 閉じる(20+2*2) + ロック(20+2) を除く
    [Fact]
    public void CalcAvailableWidthSubtractsFrameAndButtons()
    {
        Assert.Equal(400f - 4 * 2 - (20 + 2 * 2) - (20 + 2), TabBarLayout.CalcAvailableWidth(400f));
    }
}
```

- [ ] **Step 2: プラグインを COM3D25 でビルドしてテストが失敗することを確認**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TabBarLayoutTests
```
Expected: プラグインビルドは `TabBarLayout` 未定義でエラー（＝コンパイルレベルの失敗確認。テスト側は型が無いので当然失敗）。

- [ ] **Step 3: TabBarLayout を実装**

`source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarLayout.cs`:

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// タブ列のレイアウト計算 (純粋関数)。描画・入力判定は TabBarDrawer が行う。
    /// GUI に依存させないのはユニットテストのため
    /// </summary>
    public static class TabBarLayout
    {
        /// <summary>タブ幅の下限。これを割る場合は縮小せずスクロールモードへ入る</summary>
        public const float MIN_TAB_WIDTH = 60f;
        /// <summary>スクロールモードで両端に出す &lt; &gt; ボタンの幅</summary>
        public const float SCROLL_BUTTON_WIDTH = 16f;

        public struct Result
        {
            public float tabWidth;
            /// <summary>全タブが収まらずスクロールボタンを出すか</summary>
            public bool scrollable;
            /// <summary>表示する先頭タブの index (クランプ・アクティブ追従適用済み)</summary>
            public int firstVisible;
            public int visibleCount;
            /// <summary>タブ列の描画開始 X (タブバー左端からの相対)</summary>
            public float tabsOriginX;
        }

        /// <summary>
        /// ヘッダー幅からタブ列の利用可能幅を求める。
        /// 左右フレームと右側の閉じる + ロックボタン領域を除いた値。
        /// 描画側 (EditorSubWindow / DockableWindowBase) と並び替え (TabGroupManager) で共有する
        /// </summary>
        public static float CalcAvailableWidth(float headerWidth)
        {
            return headerWidth - DockableWindowBase.FRAME * 2
                - (DockableWindowBase.CLOSE_BUTTON_WIDTH + DockableWindowBase.CLOSE_BUTTON_MARGIN * 2)
                - (DockableWindowBase.LOCK_BUTTON_WIDTH + DockableWindowBase.CLOSE_BUTTON_MARGIN);
        }

        public static Result Calc(int count, float availableWidth, int scrollOffset, int activeIndex)
        {
            var result = new Result();
            if (count <= 0)
            {
                return result;
            }

            var margin = TabBarDrawer.TAB_MARGIN;
            var shrunkWidth = Mathf.Min(
                TabBarDrawer.TAB_WIDTH,
                (availableWidth - margin * (count - 1)) / count);

            if (shrunkWidth >= MIN_TAB_WIDTH)
            {
                result.tabWidth = shrunkWidth;
                result.visibleCount = count;
                return result;
            }

            // スクロールモード: 幅は下限固定、両端のボタン分を除いた領域に入る枚数だけ表示する
            result.scrollable = true;
            result.tabWidth = MIN_TAB_WIDTH;
            result.tabsOriginX = SCROLL_BUTTON_WIDTH + margin;
            var tabsArea = availableWidth - (SCROLL_BUTTON_WIDTH + margin) * 2;
            result.visibleCount = Mathf.Max(
                1, Mathf.FloorToInt((tabsArea + margin) / (MIN_TAB_WIDTH + margin)));
            if (result.visibleCount >= count)
            {
                // 防御分岐: shrunkWidth < MIN の時点で数学的にここへは到達しないはずだが、
                // 万一入った場合はボタンなしの非スクロール表示へフォールバックする
                result.scrollable = false;
                result.tabsOriginX = 0f;
                result.visibleCount = count;
                return result;
            }

            var maxOffset = count - result.visibleCount;
            var first = Mathf.Clamp(scrollOffset, 0, maxOffset);

            // アクティブタブが常に見えるよう追従する (切替直後に見失わないため)
            if (activeIndex >= 0 && activeIndex < count)
            {
                if (activeIndex < first)
                {
                    first = activeIndex;
                }
                else if (activeIndex > first + result.visibleCount - 1)
                {
                    first = activeIndex - result.visibleCount + 1;
                }
            }

            result.firstVisible = first;
            return result;
        }
    }
}
```

csproj へ `<Compile Include="MTEUtils\TabBarLayout.cs" />` を追加（`TabBarDrawer.cs` の行の隣）。

- [ ] **Step 4: ビルド + テストが通ることを確認**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TabBarLayoutTests
```
Expected: PASS (6 tests)

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarLayout.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TabBarLayoutTests.cs
git commit -m "feat(docking): タブ列レイアウト計算を TabBarLayout として抽出しタブ幅下限を導入"
```

---

### Task 2: TabBarDrawer のスクロール描画 + 各ウィンドウのオフセット保持

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarDrawer.cs`（Draw シグネチャ変更）
- Modify: `source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs:390-402`（DrawTabBar）
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs:307-319`（DrawTabBar）

**Interfaces:**
- Consumes: `TabBarLayout.Calc`（Task 1）
- Produces: `TabBarDrawer.Draw(string[] titles, int activeIndex, float x, float y, float availableWidth, ref int scrollOffset, Action<int, Vector2> onTabMouseDown)`
  - `scrollOffset` は呼び出し元ウィンドウが保持するフィールド（`<` `>` 押下と自動追従の結果を書き戻す）
  - `onTabMouseDown` の index は**グループ全体の index**（表示スロットではない）

- [ ] **Step 1: TabBarDrawer.Draw をスクロール対応に書き換える**

`TabBarDrawer.Draw` を以下へ置き換え（既存の描画ロジック本体はループ範囲とオフセットが変わるだけ）:

```csharp
public static void Draw(
    string[] titles, int activeIndex,
    float x, float y, float availableWidth,
    ref int scrollOffset,
    Action<int, Vector2> onTabMouseDown)
{
    if (titles == null || titles.Length == 0)
    {
        return;
    }

    var count = titles.Length;
    var layout = TabBarLayout.Calc(count, availableWidth, scrollOffset, activeIndex);
    if (layout.visibleCount <= 0)
    {
        return;
    }
    // クランプ・アクティブ追従の結果を呼び出し元の保持値へ書き戻す
    scrollOffset = layout.firstVisible;

    var e = Event.current;

    if (layout.scrollable)
    {
        // 両端のスクロールボタン。端に達している側は無効化する
        var leftRect = new Rect(x, y, TabBarLayout.SCROLL_BUTTON_WIDTH, TAB_HEIGHT);
        var rightRect = new Rect(
            x + availableWidth - TabBarLayout.SCROLL_BUTTON_WIDTH, y,
            TabBarLayout.SCROLL_BUTTON_WIDTH, TAB_HEIGHT);
        var maxOffset = count - layout.visibleCount;

        GUI.enabled = layout.firstVisible > 0;
        if (GUI.Button(leftRect, "<", tabLabelStyle))
        {
            scrollOffset = layout.firstVisible - 1;
        }
        GUI.enabled = layout.firstVisible < maxOffset;
        if (GUI.Button(rightRect, ">", tabLabelStyle))
        {
            scrollOffset = layout.firstVisible + 1;
        }
        GUI.enabled = true;
    }

    var tabX = x + layout.tabsOriginX;
    var last = layout.firstVisible + layout.visibleCount - 1;
    for (var i = layout.firstVisible; i <= last; i++)
    {
        var tabRect = new Rect(tabX, y, layout.tabWidth, TAB_HEIGHT);
        var isActive = i == activeIndex;

        if (e.type == EventType.MouseDown && e.button == 0 && tabRect.Contains(e.mousePosition))
        {
            if (onTabMouseDown != null)
            {
                onTabMouseDown(i, e.mousePosition);
            }
            // タブ押下でウィンドウ全体のドラッグが始まらないよう消費する
            e.Use();
        }

        // (背景・アクセントライン・ラベル描画は現行コードをそのまま移す)
        ...現行 79-101 行の描画ブロック (tabWidth を layout.tabWidth に読み替え)...

        tabX += layout.tabWidth + TAB_MARGIN;
    }
}
```

注意: `GUI.Button` はスタイル引数に `tabLabelStyle` を使う（`GUI.skin.button` だと高さ・フォントが浮く）。ボタン背景は非アクティブタブと同じ暗色矩形を `GUI.Button` の手前に `GUI.DrawTexture` で敷く（見た目をタブと揃える）。

- [ ] **Step 2: 呼び出し元 2 箇所を更新する**

`EditorSubWindow` にフィールド `private int _tabScrollOffset;` を追加し（`_tabActiveIndex` の隣、82 行付近）、`DrawTabBar` を（既存の手書き `available` 計算は `TabBarLayout.CalcAvailableWidth` へ置き換える）:

```csharp
private void DrawTabBar()
{
    // タブ列がヘッダー右のボタンへ食い込まないよう、利用可能幅の算出は TabBarLayout に集約している
    var available = TabBarLayout.CalcAvailableWidth(_windowRect.width);
    TabBarDrawer.Draw(
        _tabTitles, _tabActiveIndex,
        FRAME, (HEADER_HEIGHT - TabBarDrawer.TAB_HEIGHT) * 0.5f, available,
    ref _tabScrollOffset,
        (index, pos) => TabGroupManager.instance.OnTabPressed(this, index, pos));
}
```

`DockableWindowBase` も同様に `private int _tabScrollOffset;` を追加（`_tabActiveIndex` の隣、118 行付近）し、`DrawTabBar` の `available` 計算を `TabBarLayout.CalcAvailableWidth(_windowRect.width)` へ置き換え、呼び出しへ `ref _tabScrollOffset,` を挿入する。`SetTabBarState` で `titles == null`（グループ離脱）時に `_tabScrollOffset = 0` へ戻す処理を両クラスに入れる。

- [ ] **Step 3: 両 GameVersion でビルド確認**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TabBarLayoutTests
```
Expected: ビルド 2 本成功、テスト PASS

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarDrawer.cs source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs
git commit -m "feat(docking): タブ列が収まらないとき <> ボタンで左右スクロールできるようにする"
```

---

### Task 3: タブバー右クリックメニューでタブ切替

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarDrawer.cs`（メニュー状態と描画を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/DockingHost.cs`（`ActivateTabIndex` 追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockingClient.cs`（`ActivateTabIndex` バインディング追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs` / `source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs`（Draw 呼び出しへ onTabSelected を渡す + メニュー描画呼び出し）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs`（`ActivateTabIndex(member, index)` 追加）

**Interfaces:**
- Produces:
  - `TabBarDrawer.Draw(int windowId, string[] titles, int activeIndex, float x, float y, float availableWidth, ref int scrollOffset, Action<int, Vector2> onTabMouseDown)` — 最終シグネチャ（Task 2 のものへ `windowId` を先頭に追加）。右クリック検知でメニューを開く。メニュー項目の選択通知は `DrawContextMenu` 側の `onTabSelected(グループindex)` が担う
  - `TabBarDrawer.DrawContextMenu(int windowId, string[] titles, int activeIndex, Action<int> onTabSelected)` — **DrawWindow の最後**（他コントロール描画後）に呼ぶ。開いていなければ何もしない
  - `DockingHost.ActivateTabIndex(object handle, int tabIndex)` — グループ内 index のタブをアクティブ化（ドラッグ候補は記録しない）
  - `DockingClient.ActivateTabIndex(object handle, int tabIndex)` / `isActivateTabIndexAvailable` — 旧ホストでは no-op
  - `TabGroupManager.ActivateTabIndex(IDockableWindow member, int tabIndex)` — `member.group.windows[tabIndex]` を `SetActive`（範囲外は無視）

- [ ] **Step 1: TabBarDrawer にメニュー状態と右クリック検知を実装**

```csharp
// ---- 右クリックメニュー状態 (同時に開くのは 1 窓だけなので static で持つ) ----
private static int _menuWindowId = -1;
private static Rect _menuRect;

private const float MENU_ITEM_HEIGHT = 22f;
private const float MENU_WIDTH = 140f;

/// <summary>この window でメニューが開いているか</summary>
public static bool IsContextMenuOpen(int windowId) => _menuWindowId == windowId;
```

`Draw` の中（タブループの前）に右クリック検知を追加。windowId は Draw の引数へ `int windowId` として追加する:

```csharp
// タブバー領域 (ボタン含む availableWidth 全域) の右クリックでメニューを開く
var barRect = new Rect(x, y, availableWidth, TAB_HEIGHT);
if (e.type == EventType.MouseDown && e.button == 1 && barRect.Contains(e.mousePosition))
{
    _menuWindowId = windowId;
    _menuRect = new Rect(
        e.mousePosition.x, y + TAB_HEIGHT,
        MENU_WIDTH, titles.Length * MENU_ITEM_HEIGHT);
    e.Use();
}
```

`DrawContextMenu` を追加:

```csharp
/// <summary>
/// 右クリックで開いたタブ一覧メニュー。DrawWindow の最後 (全コントロールの後) に
/// 呼んで最前面へ描く。メニュー外クリックか項目選択で閉じる
/// </summary>
public static void DrawContextMenu(
    int windowId, string[] titles, int activeIndex, Action<int> onTabSelected)
{
    if (_menuWindowId != windowId || titles == null)
    {
        return;
    }

    var e = Event.current;
    // メニュー外の押下で閉じる (項目押下は下のボタンが先に拾う)
    if (e.type == EventType.MouseDown && !_menuRect.Contains(e.mousePosition))
    {
        _menuWindowId = -1;
        return;
    }

    // 背景 (下のコントロールへのクリック透過を防ぐため不透明寄りにする)
    var oldColor = GUI.color;
    GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
    GUI.DrawTexture(_menuRect, Texture2D.whiteTexture);
    GUI.color = oldColor;

    for (var i = 0; i < titles.Length; i++)
    {
        var itemRect = new Rect(
            _menuRect.x, _menuRect.y + i * MENU_ITEM_HEIGHT,
            MENU_WIDTH, MENU_ITEM_HEIGHT);
        var isActive = i == activeIndex;
        if (isActive)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(itemRect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }
        if (GUI.Button(itemRect, GetTruncatedTitle(titles[i], MENU_WIDTH), tabLabelStyle))
        {
            _menuWindowId = -1;
            if (!isActive && onTabSelected != null)
            {
                onTabSelected(i);
            }
        }
    }
}
```

- [ ] **Step 2: ホスト API とクライアントバインディングを追加**

`DockingHost` へ（`ActivateTab` の隣に、公開 API 追加のみ）:

```csharp
/// <summary>
/// ゲストのメニュー選択によるタブアクティブ化。tabIndex はグループ内 index。
/// NotifyTabMouseDown と違いつまみドラッグ候補は記録しない (メニュー選択はドラッグではない)
/// </summary>
public static void ActivateTabIndex(object handle, int tabIndex)
{
    var adapter = handle as ExternalWindowAdapter;
    if (adapter == null)
    {
        return;
    }
    TabGroupManager.instance.ActivateTabIndex(adapter, tabIndex);
}
```

`TabGroupManager` へ:

```csharp
/// <summary>メニュー選択によるアクティブ化。押下由来ではないためドラッグ候補は記録しない</summary>
public void ActivateTabIndex(IDockableWindow member, int tabIndex)
{
    var group = member.group;
    if (group == null || tabIndex < 0 || tabIndex >= group.windows.Count)
    {
        return;
    }
    group.SetActive(group.windows[tabIndex]);
}
```

`DockingClient` へ既存の `_activateTab` と同じパターンで `private static Action<object, int> _activateTabIndex;` を追加し、`Initialize` で `type.GetMethod("ActivateTabIndex", ...)` を単独検出（null なら無効のまま）、catch 節のリセットにも追加。公開メソッド:

```csharp
/// <summary>メニュー選択で指定 index のタブをアクティブ化する。未対応ホストでは何もしない</summary>
public static void ActivateTabIndex(object handle, int tabIndex)
{
    Initialize();
    if (handle != null && _activateTabIndex != null)
    {
        _activateTabIndex(handle, tabIndex);
    }
}
```

- [ ] **Step 3: 呼び出し元 2 箇所を配線する**

`EditorSubWindow.DrawTabBar`: `Draw` へ `windowId` と `onTabSelected` を渡し、`DrawWindow` 本体の**最後**（HandleDragInput の後）に:

```csharp
TabBarDrawer.DrawContextMenu(
    windowId, _tabTitles, _tabActiveIndex,
    index => TabGroupManager.instance.ActivateTabIndex(this, index));
```

`DockableWindowBase.DrawWindowInternal` も同様に最後へ:

```csharp
TabBarDrawer.DrawContextMenu(
    windowId, _tabTitles, _tabActiveIndex,
    index => DockingClient.ActivateTabIndex(_dockHandle, index));
```

（`windowId` は各クラスの GUI.Window 登録 ID。EditorSubWindow は `windowId` プロパティ、DockableWindowBase は `windowId` フィールドを既に持つ — 実装時に実名を確認して使う。）

注意: メニュー表示中はメニュー領域の MouseDown が下のコントロールに落ちないこと（GUI.Button が拾って消費する）。`HandleDragInput` の空き領域ドラッグ判定より**後**に描くだけでは MouseDown の先取りはできないため、メニュー表示中 (`TabBarDrawer.IsContextMenuOpen(windowId)`) は `HandleDragInput` をスキップする。

- [ ] **Step 4: 両 GameVersion でビルド確認**

Global Constraints の MSBuild 2 本 + `dotnet test`。Expected: 成功。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/TabBarDrawer.cs source/COM3D2.SceneEditor.Plugin/DockingHost.cs source/COM3D2.SceneEditor.Plugin/MTEUtils/DockingClient.cs source/COM3D2.SceneEditor.Plugin/EditorSubWindow.cs source/COM3D2.SceneEditor.Plugin/MTEUtils/DockableWindowBase.cs source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs
git commit -m "feat(docking): タブバー右クリックメニューからアクティブタブを切替できるようにする"
```

---

### Task 4: タブ左右ドラッグでグループ内並び替え

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TabGroup.cs`（`Move` 追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs`（`UpdateTabDrag` へ並び替え処理追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TabGroupMoveTests.cs`

**Interfaces:**
- Consumes: `TabBarLayout.Calc`（タブ幅の算出）、既存の `_tabDragWindow` / `_tabGrabOffset` 追跡
- Produces: `TabGroup.Move(IDockableWindow window, int newIndex)` — windows リスト内で移動し `PushTabBarState`。範囲外・非メンバー・同位置は無視

- [ ] **Step 1: TabGroup.Move の失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TabGroupMoveTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

public class TabGroupMoveTests
{
    /// <summary>並びとタブ状態 push だけ検証できる最小フェイク</summary>
    private class FakeWindow : IDockableWindow
    {
        public int tabWindowId { get; set; }
        public string windowTitleForTab => "W" + tabWindowId;
        public TabGroup group { get; set; }
        public Rect windowRect { get; set; }
        public Rect headerRect => windowRect;
        public bool isShowWnd => true;
        public bool isTabVisible => group == null || group.activeWindow == this;
        public string[] lastTitles;
        public int lastActiveIndex = -1;
        public void NotifyTabVisibleChanged() { }
        public void SavePlacement() { }
        public void SetTabBarState(string[] titles, int activeIndex)
        {
            lastTitles = titles;
            lastActiveIndex = activeIndex;
        }
    }

    private static TabGroup MakeGroup(out List<FakeWindow> wins)
    {
        var group = new TabGroup();
        wins = new List<FakeWindow>();
        for (var i = 0; i < 3; i++)
        {
            var w = new FakeWindow { tabWindowId = i };
            wins.Add(w);
            group.Add(w, activate: false);
        }
        return group;
    }

    [Fact]
    public void MoveReordersAndPushes()
    {
        var group = MakeGroup(out var wins);
        group.Move(wins[0], 2);
        Assert.Equal(new[] { wins[1], wins[2], wins[0] }, group.windows);
        // 並び替え後のタイトルが全員へ push されている
        Assert.Equal(new[] { "W1", "W2", "W0" }, wins[0].lastTitles);
    }

    [Fact]
    public void MoveKeepsActiveWindow()
    {
        var group = MakeGroup(out var wins);
        group.SetActive(wins[2]);
        group.Move(wins[2], 0);
        Assert.Same(wins[2], group.activeWindow);
        Assert.Equal(0, wins[0].lastActiveIndex);
    }

    [Fact]
    public void MoveIgnoresInvalid()
    {
        var group = MakeGroup(out var wins);
        var before = new List<IDockableWindow>(group.windows);
        group.Move(wins[0], 5);                       // 範囲外
        group.Move(new FakeWindow(), 1);              // 非メンバー
        group.Move(wins[1], 1);                       // 同位置
        Assert.Equal(before, group.windows);
    }
}
```

（`IDockableWindow` は `IConnectableWindow` を継承する。FakeWindow がコンパイルエラーになる場合は `IConnectableWindow` の要求メンバー — `windowRect` / `headerRect` / `isShowWnd` / `group` get 等 — を実装時に実インターフェース定義へ合わせて補完する。）

- [ ] **Step 2: ビルド + テストが失敗することを確認**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TabGroupMoveTests
```
Expected: `Move` 未定義でテスト側コンパイルエラー（失敗の確認）。

- [ ] **Step 3: TabGroup.Move を実装**

```csharp
/// <summary>
/// タブの並び順を変更する。アクティブウィンドウは変えず、並びだけ動かして push する
/// (タブドラッグ並び替え用。範囲外・非メンバー・同位置は何もしない)
/// </summary>
public void Move(IDockableWindow window, int newIndex)
{
    var oldIndex = windows.IndexOf(window);
    if (oldIndex < 0 || newIndex < 0 || newIndex >= windows.Count || newIndex == oldIndex)
    {
        return;
    }
    windows.RemoveAt(oldIndex);
    windows.Insert(newIndex, window);
    PushTabBarState();
}
```

- [ ] **Step 4: テストが通ることを確認**

COM3D25 ビルド → `dotnet test --filter TabGroupMoveTests`。Expected: PASS (3 tests)

- [ ] **Step 5: TabGroupManager にドラッグ並び替えを実装**

方式: **絶対座標マッピングではなく隣接スワップ**。押下タブの表示位置やスクロールオフセットはウィンドウ側にしか無いため、押下点からの X 移動量が「タブ 1 枚幅」を超えるたびに隣とスワップする。フィールド追加:

```csharp
// タブ並び替えの追跡。押下時点の X を基準に、タブ幅ぶん動くたびに隣へ移す
private float _tabReorderBaseX;
private bool _tabReordered;
```

`OnTabMouseDown` で `_tabReorderBaseX = InputRemapper.rawGuiPosition.x; _tabReordered = false;` を記録。

`UpdateTabDrag` の未分離ブロック（`if (!_tabDetached)` 内、Detach 判定の**後**）へ:

```csharp
// ヘッダー内に留まっている間の横ドラッグはグループ内の並び替えとして扱う
if (!_tabDetached && _tabDragWindow.group != null)
{
    var group = _tabDragWindow.group;
    var header = group.activeWindow.headerRect;
    // タブ幅は描画側と同じレイアウト計算 (TabBarLayout に集約) で求める
    var available = TabBarLayout.CalcAvailableWidth(header.width);
    var layout = TabBarLayout.Calc(group.windows.Count, available, 0, -1);
    var step = layout.tabWidth + TabBarDrawer.TAB_MARGIN;

    var dx = guiPos.x - _tabReorderBaseX;
    while (Mathf.Abs(dx) >= step)
    {
        var index = group.windows.IndexOf(_tabDragWindow);
        var newIndex = dx > 0 ? index + 1 : index - 1;
        if (newIndex < 0 || newIndex >= group.windows.Count)
        {
            break;
        }
        group.Move(_tabDragWindow, newIndex);
        _tabReordered = true;
        // 基準点を 1 枚ぶん進めて次のスワップ判定へ (往復ドラッグでも破綻しない)
        _tabReorderBaseX += dx > 0 ? step : -step;
        dx = guiPos.x - _tabReorderBaseX;
    }
}
```

マウスアップ処理（`if (!Input.GetMouseButton(0))` ブロック）へ、並び替えが起きていたら保存を追加:

```csharp
if (_tabReordered)
{
    // 並び順も config (tabGroups) の一部なので保存する
    MarkGroupsDirty();
    _tabReordered = false;
}
```

- [ ] **Step 6: 両 GameVersion でビルド + 全テスト確認**

MSBuild 2 本 + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Expected: 成功。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TabGroup.cs source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs source/COM3D2.SceneEditor.Plugin.Tests/TabGroupMoveTests.cs
git commit -m "feat(docking): タブの左右ドラッグでグループ内の並び順を変更できるようにする"
```

---

### Task 5: 外部プラグイン窓のドッキング構成を Config から復元

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs`（保存エントリのパースを public 化 + `TryRestoreExternal` 追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/DockingHost.cs`（`UpdateExternals` の自動ドッキングで config 復元を優先）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TabGroupConfigEntryTests.cs`

**背景:** `SaveGroups` は外部窓の tabWindowId も保存済みだが、起動時の `RestoreGroups` は外部窓の登録前に走るため復元されない。現状はヘッダー位置一致（`MergeIfHeaderMatches`）による近似復元のみ（DockingHost.cs:284 のコメント参照）。これを ID ベースの確実な復元に格上げする。ヘッダー位置一致は「保存エントリに無い窓」のフォールバックとして残す。

**Interfaces:**
- Produces:
  - `struct TabGroupConfigEntry { public int activeId; public List<int> memberIds; }`（`COM3D2.SceneEditor.Plugin` 名前空間、TabGroupManager.cs 内に定義）
  - `static bool TabGroupConfigEntry.TryParse(string entry, out TabGroupConfigEntry result)` — `"activeId:id1,id2,..."` 書式。既存 `RestoreGroups` のパース処理をここへ集約する
  - `TabGroupManager.TryRestoreExternal(IDockableWindow adapter)` → bool — 起動時スナップショットに adapter の所属エントリがあり、他メンバーが 1 つでも表示中なら復帰させ true
  - `TabGroupManager.Merge(IDockableWindow source, IDockableWindow target, bool activate = true)` — 既存 `Merge` へ省略可能引数を追加（既存呼び出しは無変更）。`activate: false` はドラッグ由来ではない復元用で、source をアクティブ化せず並びだけ作る（SceneView 系の Activate/Deactivate 連鎖でカメラ・RT を無駄に作り直さないため。既存 `RestoreGroups` の `activate: false` と同じ配慮）

- [ ] **Step 1: パースの失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TabGroupConfigEntryTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

public class TabGroupConfigEntryTests
{
    [Fact]
    public void ParsesValidEntry()
    {
        Assert.True(TabGroupConfigEntry.TryParse("10:10,20,30", out var e));
        Assert.Equal(10, e.activeId);
        Assert.Equal(new[] { 10, 20, 30 }, e.memberIds);
    }

    [Fact]
    public void SkipsInvalidMemberIds()
    {
        Assert.True(TabGroupConfigEntry.TryParse("10:10,abc,30", out var e));
        Assert.Equal(new[] { 10, 30 }, e.memberIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("abc:1,2")]
    [InlineData("1:2:3")]
    public void RejectsMalformedEntries(string entry)
    {
        Assert.False(TabGroupConfigEntry.TryParse(entry, out _));
    }
}
```

- [ ] **Step 2: ビルド + テストが失敗することを確認**

COM3D25 ビルド。Expected: `TabGroupConfigEntry` 未定義でテスト側コンパイルエラー。

- [ ] **Step 3: TabGroupConfigEntry を実装し RestoreGroups を書き換える**

TabGroupManager.cs（クラス外、同ファイル末尾の namespace 内）:

```csharp
/// <summary>
/// config.tabGroups の 1 エントリ ("activeId:id1,id2,...") のパース結果。
/// 起動時の RestoreGroups と外部窓の遅延復元 (TryRestoreExternal) で共有する
/// </summary>
public struct TabGroupConfigEntry
{
    public int activeId;
    public List<int> memberIds;

    public static bool TryParse(string entry, out TabGroupConfigEntry result)
    {
        result = default;
        if (string.IsNullOrEmpty(entry))
        {
            return false;
        }
        var parts = entry.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out result.activeId))
        {
            return false;
        }
        result.memberIds = new List<int>();
        foreach (var idText in parts[1].Split(','))
        {
            if (int.TryParse(idText, out var id))
            {
                result.memberIds.Add(id);
            }
        }
        return result.memberIds.Count > 0;
    }
}
```

（`List<int>` 用に `using System.Collections.Generic;` は既にある。）既存 `RestoreGroups` のパース部分（parts.Split / int.TryParse）を `TabGroupConfigEntry.TryParse` 呼び出しへ置き換える（挙動は同一、重複排除のみ）。

- [ ] **Step 4: テストが通ることを確認**

COM3D25 ビルド → `dotnet test --filter TabGroupConfigEntryTests`。Expected: PASS

- [ ] **Step 5: TryRestoreExternal を実装し UpdateExternals へ配線する**

TabGroupManager へ:

```csharp
/// <summary>
/// 外部窓の遅延復元。起動時の RestoreGroups は外部窓の登録前に走るため、
/// 登録後の自動ドッキング猶予中に config の所属エントリを引いて復帰させる。
/// 保存された並び順を保つため、表示中メンバーの直近の前方メンバーの隣へ挿す
/// </summary>
public bool TryRestoreExternal(IDockableWindow adapter)
{
    foreach (var text in config.tabGroups)
    {
        if (!TabGroupConfigEntry.TryParse(text, out var entry))
        {
            continue;
        }
        var myIndex = entry.memberIds.IndexOf(adapter.tabWindowId);
        if (myIndex < 0)
        {
            continue;
        }

        // 表示中の他メンバーを探す (誰も居なければまだ復元できない)
        IDockableWindow peer = null;
        foreach (var id in entry.memberIds)
        {
            if (id == adapter.tabWindowId)
            {
                continue;
            }
            var w = FindWindow(id);
            if (w != null && w.isShowWnd)
            {
                peer = w;
                break;
            }
        }
        if (peer == null)
        {
            return false;
        }

        // activate: false で並びだけ作る (復元でアクティブタブを奪わない + Activate 連鎖防止)
        Merge(adapter, peer, activate: false);

        // Merge は末尾追加なので、保存された並び順 (エントリ内の相対順) へ移す
        var group = adapter.group;
        if (group != null)
        {
            var target = 0;
            foreach (var member in group.windows)
            {
                if (member == adapter)
                {
                    continue;
                }
                var idx = entry.memberIds.IndexOf(member.tabWindowId);
                if (idx >= 0 && idx < myIndex)
                {
                    target++;
                }
            }
            group.Move(adapter, target);

            // 保存時にアクティブだった窓を優先する (SetActive は同一窓なら no-op なので連鎖は最大 1 回)
            var active = FindWindow(entry.activeId);
            if (active != null && group.Contains(active))
            {
                group.SetActive(active);
            }
        }
        return true;
    }
    return false;
}
```

`DockingHost.UpdateExternals` の自動再ドッキング分岐（`adapter.group == null && adapter.autoDockRetryFrames > 0`）で、`MergeIfHeaderMatches` の**前に** config 復元を試す:

```csharp
adapter.autoDockRetryFrames--;
// まず config の保存構成による確実な復元を試し、
// 保存エントリの無い窓は従来どおりヘッダー位置一致で復帰させる
if (!TabGroupManager.instance.TryRestoreExternal(adapter))
{
    TabGroupManager.instance.MergeIfHeaderMatches(adapter);
}
```

注意 (スナップショット設計): `Merge` は `MarkGroupsDirty` → `SaveGroups` を呼ぶため、復元途中（メンバーがまだ全員登録されていない状態）で config が「部分的なグループ」で上書きされる。config を直接参照すると、**復元順によっては未登録メンバーがエントリから消え、後から登録された窓が復帰できなくなる**。そこで復元は起動時点のスナップショットを参照する。ただしスナップショットを無期限に持つと、**セッション中にユーザーが手動でタブ構成を変えた後に外部窓が再表示されたとき、古い構成へ巻き戻してしまう**。両立させるルール:

- `TryRestoreExternal` は `config.tabGroups` ではなく `_restoreSnapshot`（`List<string>`）を参照する
- スナップショットは `RestoreGroups` の直前（起動時 WindowManager.cs:183 付近、レイアウト適用時 WindowLayoutManager.cs:277 付近）で `CaptureRestoreSnapshot()` により確保する
- **ユーザー操作由来のグループ構成変更（`MarkGroupsDirty`）が起きたらスナップショットを破棄する**。破棄後の外部窓再表示は従来どおりヘッダー位置一致（`MergeIfHeaderMatches`）へフォールバックする（手動変更を古い構成で巻き戻さないため）
- `TryRestoreExternal` 自身が呼ぶ `Merge` → `MarkGroupsDirty` では破棄しない（破棄すると後続メンバーが復元できない）。復元中フラグでガードする

実装: TabGroupManager へ

```csharp
/// <summary>
/// RestoreGroups 時点の保存構成。外部窓の遅延復元用 (復元途中の SaveGroups 上書きから守る)。
/// ユーザー操作でグループ構成が変わったら破棄し、以降の外部窓復帰は
/// ヘッダー位置一致のフォールバックに任せる (古い構成への巻き戻し防止)
/// </summary>
private readonly List<string> _restoreSnapshot = new List<string>();

/// <summary>TryRestoreExternal 実行中か。復元自身の Merge でスナップショットを破棄しないためのガード</summary>
private bool _isRestoringExternal;

/// <summary>RestoreGroups の直前に呼ぶ。遅延復元用のスナップショットを確保する</summary>
public void CaptureRestoreSnapshot()
{
    _restoreSnapshot.Clear();
    _restoreSnapshot.AddRange(config.tabGroups);
}
```

`MarkGroupsDirty` の先頭へ:

```csharp
// ユーザー操作による構成変更後は起動時構成が陳腐化するため遅延復元を打ち切る
// (復元処理自身の Merge 由来では破棄しない)
if (!_isRestoringExternal)
{
    _restoreSnapshot.Clear();
}
```

`TryRestoreExternal` は本体全体を `_isRestoringExternal = true; try { ... } finally { _isRestoringExternal = false; }` で包み、ループを `foreach (var text in _restoreSnapshot)` とする。`WindowManager.cs:183` の `TabGroupManager.instance.RestoreGroups();` の直前に `TabGroupManager.instance.CaptureRestoreSnapshot();` を追加。`WindowLayoutManager.cs:277` のレイアウト適用側でも `RestoreGroups` 前に `CaptureRestoreSnapshot` を呼ぶ（レイアウト適用後の外部窓復帰も新構成基準にするため）。

`Merge` のシグネチャは `public void Merge(IDockableWindow source, IDockableWindow target, bool activate = true)` へ変更（既存呼び出しは無変更で従来挙動）。`activate: false` のときは、sourceGroup ありの分岐では末尾の `targetGroup.SetActive(source);` をスキップし、source が独立窓の分岐では `targetGroup.Add(source, activate: false);` を使う（target 側の初期アクティブ化 — `Add` の `_activeWindow == null` 条件 — はそのままでよい）。

また、ユーザーが**手動でタブを分離した**外部窓が猶予中に再吸着しないよう、既存の仕組みを踏襲する: 手動分離（Detach / RemoveFromGroup 経由）の時点で `autoDockRetryFrames` は 0 のまま（ResetAutoDockRetry は登録・再表示・ホスト無効化・レイアウト適用でしか呼ばれない）ため追加対処は不要。

- [ ] **Step 6: 両 GameVersion でビルド + 全テスト確認**

MSBuild 2 本 + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Expected: 成功。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/TabGroupManager.cs source/COM3D2.SceneEditor.Plugin/DockingHost.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowLayoutManager.cs source/COM3D2.SceneEditor.Plugin.Tests/TabGroupConfigEntryTests.cs
git commit -m "feat(docking): 外部プラグイン窓のドッキング構成を config から ID ベースで復元する"
```

---

### Task 6: ゲストガイドのドキュメント更新 + 最終確認

**Files:**
- Modify: `docs-site/dev/docking-guest-guide.md`（`ActivateTabIndex` の追記、タブバー節にスクロール・右クリックメニュー・並び替えの挙動を追記）

**Interfaces:**
- Consumes: Task 3 の `DockingHost.ActivateTabIndex(object handle, int tabIndex)` / `DockingClient.ActivateTabIndex`

- [ ] **Step 1: docking-guest-guide.md を更新**

既存の API 一覧の書式に合わせて追記する（実装時に実ファイルの構成を確認して整合させる）:
- ホスト API `ActivateTabIndex(object handle, int tabIndex)`: グループ内 index 指定のアクティブ化。ドラッグ候補を記録しない点で `NotifyTabMouseDown` と異なる。旧ホストでは `DockingClient` 側が no-op
- タブバー挙動の変更点: タブ幅下限 60px、収まらない場合の `<` `>` スクロール、右クリックメニュー、左右ドラッグ並び替え（いずれも MTEUtils 共有ソース `TabBarDrawer`/`TabBarLayout` 更新の取り込みで有効化。旧 MTEUtils のままのゲストは従来挙動）
- 外部窓のドッキング構成が config から ID ベースで復元されるようになったこと（ゲスト側の対応は不要。windowId が安定していることが前提になる旨を明記）

- [ ] **Step 2: 全体の最終検証**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド 2 本成功、全テスト PASS。

可能ならゲーム起動中に MCP `com3d25-devbridge` で実機確認（多数タブのグループを作りスクロール・右クリック・並び替え・再起動後の外部窓復元を目視）。起動していなければユーザーへ実機確認を依頼する。

- [ ] **Step 3: コミット**

```bash
git add docs-site/dev/docking-guest-guide.md
git commit -m "docs(docking): ゲストガイドへ ActivateTabIndex とタブバー改善の挙動を追記"
```

---

## レビュー却下メモ

- `_restoreSnapshot` 内に同一 tabWindowId を含む複数エントリ（config 破損・手動編集）への防御を追加すべき（確信度: 低）— 未確認のまま見送り。first-match で決定的に動作し実害が低い異常系のため

