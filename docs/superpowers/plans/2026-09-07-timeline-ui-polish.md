# タイムライン/SceneView UI 調整 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ユーザー指示の 8 項目の UI 調整（SceneView のモデル非表示、タイムライン操作ウィンドウの整理とアイコン化、シークバーのドラッグ移動、ボーングループのダブルクリック展開、IK 項目の腕/足グループへの移動）を実装する。

**Architecture:** 既存の GUIView/EditorSubWindow 基盤の上で各ウィンドウの描画コードを修正する。新規アイコンは `assets/icons/*.svg` → `generate.js` → `ToolbarIcons.PNG_BASE64` の既存パイプラインで追加する。ロジックとして切り出せる部分（IK ホールド種別 → ボーングループ対応）は静的関数にしてテストする。

**Tech Stack:** C# (.NET Framework 3.5 / 4.x の 2 構成), Unity IMGUI, Node.js (`@resvg/resvg-js`) によるアイコン生成, MSBuild, xUnit テスト (`source/COM3D2.SceneEditor.Plugin.Tests`)。

**Spec:** ユーザー指示（本計画冒頭の 8 項目）。設計書なし。

## Global Constraints

- **2 構成ビルド必須**: `debug.bat all` 相当。ゲーム起動中でなければ `MSBuild ... /p:GameVersion=COM3D2` と `COM3D25` の両方を直接叩く（deploy.bat は絶対に実行しない）。
- コメント・ログは日本語。ハードコードは定数化する。
- git worktree は使わない。
- Unity 型は既存コードに合わせて `UnityEngine` を using 済みの短縮名でよい（REPL のみ完全修飾）。

## 要件と対応タスク

| # | 要件 | Task |
|---|---|---|
| 1 | SceneView にモデル非表示アイコンを追加 | 1 |
| 2 | タイムライン操作ウィンドウの簡易表示ボタンをオミット | 2 |
| 3 | 自動登録はデフォルト ON、アクティブ色を赤に | 2 |
| 4 | タイムライン操作ウィンドウの縦幅最小値を下げ、スクロール末尾の空行を削除 | 2 |
| 5 | フレーム操作をドラッグ可能テキストエリアに、ボタンをアイコン化して中央に再生ボタン | 3, 4 |
| 6 | シークバーはドラッグでフレーム移動、シークバー上のウィンドウドラッグ無効 | 5 |
| 7 | ボーングループ名ダブルクリックで展開/折りたたみ | 6 |
| 8 | IK のボーングループを左腕/右腕/左足/右足へ移動 | 7 |

---

### Task 1: SceneView にモデル表示トグルを追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:58-59`（`sceneViewShowMaid` の隣）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewCullingFilter.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/SceneViewManager.cs:111-116`
- Modify: `source/COM3D2.SceneEditor.Plugin/SceneViewWindow.cs:283-323`

**Interfaces:**
- Produces: `Config.sceneViewShowModel` (bool, 既定 true), `SceneViewCullingFilter.hideModel` (bool)

- [ ] **Step 1: Config に項目を追加**

```csharp
public bool sceneViewShowMaid = true;
public bool sceneViewShowModel = true;
```

- [ ] **Step 2: CullingFilter にモデルの非表示を追加**

`hideMaid` の隣に `public bool hideModel = false;`、キャッシュ `_modelRenderers` / `_modelCacheValid` を追加し、`InvalidateCache` と `OnPreCull` に同型の処理を足す。収集は MTE のモデル管理から行う（`MTEP.StudioModelManager.instance.models` の `transform` 配下）。

```csharp
private static void CollectModelRenderers(List<Renderer> results)
{
    var modelManager = COM3D2.MotionTimelineEditor.Plugin.StudioModelManager.instance;
    if (modelManager == null)
    {
        return;
    }
    foreach (var model in modelManager.models)
    {
        if (model != null && model.transform != null)
        {
            results.AddRange(model.transform.GetComponentsInChildren<Renderer>(true));
        }
    }
}
```

クラス冒頭の summary を「背景/メイド/モデル」に更新する。

- [ ] **Step 3: SceneViewManager.ApplyViewSettings で反映**

```csharp
cullingFilter.hideModel = !config.sceneViewShowModel;
```

- [ ] **Step 4: SceneViewWindow のツールバーにトグルを追加**

`maidIcon` の直後に `var modelIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Model);` を取り、幅計算に `GetToolbarToggleWidth(modelIcon)` を足し、`TOOLBAR_ITEM_MARGIN * 5` を `* 6` に変更（コメントの「5 箇所分」も 6 に）。メイドトグルの直後に描く:

```csharp
DrawToolbarToggle(view, modelIcon, "モデル", config.sceneViewShowModel,
    value => config.sceneViewShowModel = value);
```

summary の「背景/メイド/ギズモ表示」を「背景/メイド/モデル/ギズモ表示」に更新。

- [ ] **Step 5: 両構成ビルドして確認、コミット**

```
git commit -m "feat(sceneview): モデル表示トグルをツールバーへ追加する"
```

---

### Task 2: タイムライン操作ウィンドウの整理（簡易表示オミット / 自動登録既定 ON・赤 / 最小高さ / 末尾空行）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs:20, 226-237, 585-592, 667-682`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs:64`
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs:1073-1099`（アイコン DrawToggle に ON 色引数）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineConfigDefaultsTests.cs`（新規）

- [ ] **Step 1: 既定値のテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

public class TimelineConfigDefaultsTests
{
    [Fact]
    public void 自動登録は既定でONになる()
    {
        Assert.True(new Config().isAutoKeyFrame);
    }
}
```

テストプロジェクトはソースではなく COM3D25 構成でビルド済みの `bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll` を参照する。**テスト実行前に必ず COM3D25 構成で MSBuild する**こと（`EnsurePluginBuilt` ターゲットが要求する）。

- [ ] **Step 2: テストが失敗することを確認**

- [ ] **Step 3: Config の既定値を変更**

`Timeline/Config.cs:64` を `public bool isAutoKeyFrame = true;` にする。

- [ ] **Step 4: GUIView のアイコン DrawToggle に ON 色を指定できるようにする**

```csharp
public bool DrawToggle(
    Texture2D icon,
    bool value,
    float width,
    float height,
    Action<bool> onChanged,
    float offsetSize = 0f,
    string tooltip = null,
    Color? onColor = null)
{
    var drawRect = GetDrawRect(width, height);
    // ON 色の既定はアクセント色。録画系トグルなど目立たせたいものは呼び出し側で指定する
    BeginColor(value ? (onColor ?? option.accentColor) : Color.white);
```

- [ ] **Step 5: TimelineControlWindow を修正**

1. `minHeight => 190` を `minHeight => 60` にする（ヘッダー + 1 行分。折り返し前提のウィンドウなので最小 1 行で成立する）。
2. `DrawToggles` 先頭の `EasyEdit` トグル呼び出しブロック（585-591 行）を削除する。
3. `DrawIconToggle` に `Color? onColor = null` を追加して `view.DrawToggle(icon, value, ROW_HEIGHT, ROW_HEIGHT, onChanged, ICON_TOGGLE_OFFSET, label, onColor)` へ渡す。自動登録のみ `AUTO_KEY_ON_COLOR` を渡す:

```csharp
/// <summary>自動登録 ON の色。録画中を連想させる赤にして他トグルと区別する</summary>
private static readonly Color AUTO_KEY_ON_COLOR = new Color(1f, 0.3f, 0.3f);
```

4. 末尾の空行を削除する。`GUIView.EndScrollView` は `currentPos.y + 20` をコンテンツ高さにするが、横並びを `EndLayout` した時点で `currentPos.y` は最終行の下端になっているため 20px 余る。`DrawBody` のスクロールビュー内で `EndLayout` の後に `view.currentPos.y -= ROW_HEIGHT;` とせず、`GUIView` に「末尾余白なしで閉じる」オーバーロードを追加する:

```csharp
/// <summary>末尾に余白を足さずにスクロールビューを閉じる。横並び折り返しレイアウト用</summary>
public void EndScrollView(float trailingSpace)
{
    scrollViewContentRect.height = currentPos.y + trailingSpace;
    ...（既存 EndScrollView の残りと同じ処理）
}

public void EndScrollView()
{
    EndScrollView(20f);
}
```

`TimelineControlWindow.DrawBody` は `view.EndScrollView(0f);` を呼ぶ。

- [ ] **Step 6: テストと両構成ビルドを通してコミット**

```
git commit -m "refactor(timeline): 操作ウィンドウの簡易表示トグルを外し自動登録を既定ONの赤表示にする"
```

---

### Task 3: フレーム操作アイコンを追加する

**Files:**
- Create: `assets/icons/SkipStart.svg`, `PrevKey.svg`, `PrevFrame.svg`, `Play.svg`, `Pause.svg`, `NextFrame.svg`, `NextKey.svg`, `SkipEnd.svg`
- Modify: `assets/icons/generate.js:20-23`
- Modify: `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs:14-49, 52-72`

**Interfaces:**
- Produces: `ToolbarIcons.Kind.SkipStart / PrevKey / PrevFrame / Play / Pause / NextFrame / NextKey / SkipEnd`

- [ ] **Step 1: SVG を作成**

既存アイコンと同じ流儀（32x32、白図形に黒縁取り、`stroke-linejoin="round"`）。例:

```xml
<!-- Play.svg: 再生 (右向き三角) -->
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <polygon points="9,5 27,16 9,27" fill="#ffffff" stroke="#000000" stroke-width="2.4" stroke-linejoin="round" />
</svg>
```

- Pause: 縦長の四角 2 本 (x=8 と x=18、幅 6、y=6〜26)
- PrevFrame / NextFrame: 三角 1 個（左向き / 右向き、中央）
- PrevKey / NextKey: 三角 + 縦線側にひし形（キーフレームを示す）。例 PrevKey: ひし形 `points="8,16 12,12 16,16 12,20"` と右向きに寄せた三角 `points="27,7 15,16 27,25"`
- SkipStart / SkipEnd: 縦線 (幅 3) + 三角。SkipStart は左に線、右向き三角を左へ向ける (`points="27,6 11,16 27,26"`, 線 `x=5..8`)

各ファイル冒頭に既存と同じ日本語コメントを付ける。

- [ ] **Step 2: generate.js の ICONS に追加**

```js
const ICONS = [
    'Bg', 'Maid', 'Gizmo', 'Ortho', 'Change', 'Link', 'Home', 'Focus', 'Global',
    'EasyEdit', 'EditMode', 'AutoKey', 'Model', 'Camera', 'FovLock', 'FocusLock', 'PostEffect',
    'SkipStart', 'PrevKey', 'PrevFrame', 'Play', 'Pause', 'NextFrame', 'NextKey', 'SkipEnd',
];
```

- [ ] **Step 3: 生成して base64 を貼り付け**

```
cd assets/icons && node generate.js
```

出力のうち新規 8 個の base64 を `ToolbarIcons.PNG_BASE64` 末尾へ順番どおりに追加し、`Kind` にも同順で追加（各行に「// 先頭へ (縦線と左向き三角)」等のコメント）。PNG も生成されるのでそのまま追加する。

- [ ] **Step 4: ビルド確認、コミット**

```
git commit -m "feat(icons): フレーム操作用のアイコン 8 種を追加する"
```

---

### Task 4: フレーム操作をアイコンボタン + ドラッグ入力にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs:331-425`（`DrawFrameControls`）

**Interfaces:**
- Consumes: Task 3 の `ToolbarIcons.Kind.*`、`GUIView.DrawTextureButton`、`GUIView.DrawDragIntField`

- [ ] **Step 1: DrawFrameControls を書き換える**

現在フレームのフィールドを `DrawDragIntField`（ラベル「フレーム」）にし、ボタン列を `|< .< < [▶/■] > >. >|` の順のアイコンボタンにする。再生ボタンは中央に置く。

```csharp
/// <summary>フレーム操作アイコンボタンのサイズ</summary>
private const float FRAME_BUTTON_SIZE = 20f;
/// <summary>フレーム操作アイコンの内側余白</summary>
private const float FRAME_ICON_OFFSET = 4f;
/// <summary>現在フレーム欄のラベル幅と入力幅</summary>
private const float FRAME_LABEL_WIDTH = 50f;
private const float FRAME_FIELD_WIDTH = 50f;
```

```csharp
var newFrameNo = timelineManager.currentFrameNo;

// 現在フレームはラベルドラッグでも動かせる
WrapIfNeeded(view, FRAME_LABEL_WIDTH + GUIView.defaultMargin + FRAME_FIELD_WIDTH);
view.DrawDragIntField(new GUIView.DragIntFieldOption
{
    label = "フレーム",
    labelWidth = FRAME_LABEL_WIDTH,
    value = newFrameNo,
    minValue = 0,
    maxValue = timeline.maxFrameNo,
    fieldWidth = FRAME_FIELD_WIDTH,
    height = ROW_HEIGHT,
    onChanged = value => newFrameNo = value,
});

// シークボタン群 (|< .< < ▶ > >. >|) は分断すると操作しにくいためまとめて折り返す
WrapIfNeeded(view, FRAME_BUTTON_SIZE * 7);
view.margin = 0;
{
    if (DrawFrameButton(view, ToolbarIcons.Kind.SkipStart, "|<", "先頭へ"))
    {
        newFrameNo = 0;
    }
    if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.PrevKey, ".<", "前のキーへ"))
    {
        var prevFrame = timelineManager.GetPrevFrame(newFrameNo);
        if (prevFrame != null) newFrameNo = prevFrame.frameNo;
    }
    if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.PrevFrame, "<", "前のフレームへ"))
    {
        newFrameNo--;
    }

    if (currentLayer.isAnmPlaying)
    {
        if (DrawFrameButton(view, ToolbarIcons.Kind.Pause, "■", "停止"))
        {
            timelineManager.Pause();
        }
    }
    else
    {
        if (DrawFrameButton(view, ToolbarIcons.Kind.Play, "▶", "再生"))
        {
            timelineManager.Play();
        }
    }

    if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.NextFrame, ">", "次のフレームへ"))
    {
        newFrameNo++;
    }
    if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.NextKey, ">.", "次のキーへ"))
    {
        var nextFrame = timelineManager.GetNextFrame(newFrameNo);
        if (nextFrame != null) newFrameNo = nextFrame.frameNo;
    }
    if (DrawFrameButton(view, ToolbarIcons.Kind.SkipEnd, ">|", "最終へ"))
    {
        newFrameNo = timeline.maxFrameNo;
    }
}
view.margin = GUIView.defaultMargin;

if (newFrameNo != timelineManager.currentFrameNo)
{
    timelineManager.SeekCurrentFrame(newFrameNo);
    TimelineWindow.instance.FixScrollPosition();
}
```

以前の独立した「▶/■」ブロック（`WrapIfNeeded(view, 20)` 以降）は削除し、「フレーム操作」見出しラベル (`DrawGroupLabel`) も削除する（ボタンが自明になるため）。

ヘルパー（アイコンが読めないときは従来の文字ボタンにフォールバック）:

```csharp
private static bool DrawFrameButton(GUIView view, ToolbarIcons.Kind kind, string fallbackText, string tooltip)
{
    var icon = ToolbarIcons.GetTexture(kind);
    if (icon == null)
    {
        return view.DrawButton(fallbackText, FRAME_BUTTON_SIZE, ROW_HEIGHT);
    }
    return view.DrawTextureButton(icon, FRAME_BUTTON_SIZE, ROW_HEIGHT, FRAME_ICON_OFFSET, true, null, tooltip);
}
```

`DrawFrameRepeatButton` は `GUIView` に `DrawTextureRepeatButton(Texture2D texture, float width, float height, float offsetSize = 0f, string tooltip = null)` を追加して使う。実装は既存 `DrawRepeatButton` の `GUI.RepeatButton(drawRect, text, gsButton)` を `GUI.RepeatButton(drawRect, "", gsButton)` + `DrawTileThumb(...)` + `RegisterTooltip` に置き換えたもの。リピート判定部分は private メソッド `UpdateRepeatState(bool isPressed)` に切り出して両者で共有する。リピート状態 `repeatButtonInfo` はビュー単位で 1 つの共有フィールドだが、IMGUI では同一フレームに押下されるボタンは 1 つなので共有のままでよい（その旨をコメントに残す）。

- [ ] **Step 2: 両構成ビルド、可能ならゲームで表示確認、コミット**

```
git commit -m "feat(timeline): フレーム操作をアイコンボタンとドラッグ入力にする"
```

---

### Task 5: シークバーのドラッグでフレーム移動

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:1049-1061`

- [ ] **Step 1: MouseDown 処理をドラッグ対応にする**

既存の `view.InvokeActionOnEvent(-1, FRAME_LABEL_HEIGHT, EventType.MouseDown, ...)` を、`GUIView.DragInfo` を使ったドラッグ処理に置き換える。フィールド追加:

```csharp
/// <summary>フレーム番号バー (シークバー) のドラッグ状態</summary>
private readonly GUIView.DragInfo _seekDragInfo = new GUIView.DragInfo();
```

描画部:

```csharp
// フレーム移動 (シークバー)。押下位置でシークし、そのままドラッグでも追従させる
var seekRect = view.GetDrawRect(-1, FRAME_LABEL_HEIGHT);
if (view.InvokeActionOnDragStart(seekRect, _seekDragInfo, view.currentPos))
{
    SeekByBarPosition(Event.current.mousePosition.x - seekRect.x);
    // 消費しないと GUI.DragWindow が拾ってウィンドウごと動いてしまう
    Event.current.Use();
}
if (_seekDragInfo.isDragging)
{
    view.InvokeActionOnDragging(_seekDragInfo, pos =>
    {
        SeekByBarPosition(Event.current.mousePosition.x - seekRect.x);
    });
}
```

`InvokeActionOnDragStart(Rect, DragInfo, Vector2)` と `InvokeActionOnDragging(DragInfo, Action<Vector2>)` の実引数形は `GUIView.cs:968-1035` を読んで既存のキーフレームドラッグ（`TimelineWindow.cs:925-965`）と同じ呼び方に合わせること。ドラッグ中はマウス位置がバー外に出ても追従させる（X だけ使う）。

```csharp
/// <summary>シークバー上の X 座標から現在フレームを決める。範囲外は端にクランプ</summary>
private void SeekByBarPosition(float localX)
{
    var frameNo = (int)((scrollPosition.x + localX) / frameWidth);
    frameNo = Mathf.Clamp(frameNo, 0, timeline.maxFrameNo);
    if (frameNo != timelineManager.currentFrameNo)
    {
        timelineManager.SeekCurrentFrame(frameNo);
    }
}
```

`scrollPosition` / `frameWidth` は `DrawTimeline` 内のローカル変数なので、`SeekByBarPosition` はメソッド化せず同スコープのローカル関数またはインライン処理にする（メソッドにするなら両値を引数で渡す）。ドラッグ中の X は `DragInfo.pos.x` を使う: 開始時に `pos = new Vector2(localX, 0)` を渡すと `InvokeActionOnDragging` が移動量を加算した `info.pos` をコールバックに渡してくる。

- [ ] **Step 2: 両構成ビルド、コミット**

```
git commit -m "feat(timeline): シークバーのドラッグでフレーム移動できるようにする"
```

---

### Task 6: ボーングループ名のダブルクリックで展開/折りたたみ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:1335-1375`

- [ ] **Step 1: グループ名ラベルの MouseDown でクリック回数を見る**

`menuItem.isSetMenu` の行について、名前ラベルの `InvokeActionOnEvent(... EventType.MouseDown ...)` のハンドラ先頭で `Event.current.clickCount >= 2` ならトグルする:

```csharp
(pos) =>
{
    // グループ名のダブルクリックは展開/折りたたみ (折りたたみ記号を狙わなくてよいように)
    if (menuItem.isSetMenu && Event.current.clickCount >= 2)
    {
        menuItem.isOpenMenu = !menuItem.isOpenMenu;
        return;
    }
    ...既存の選択処理
});
```

レイヤーヘッダー行（`row.isHeader`）も同様に `_rowState.ToggleCollapsed(headerLayer)` をダブルクリックで行う。

- [ ] **Step 2: 両構成ビルド、実機確認、コミット**

`Event.clickCount` はリポジトリ内に前例が無いため、ゲームが起動していればホットリロードか再起動でダブルクリックが拾えることを確認する。拾えない場合は `Time.realtimeSinceStartup` で前回クリック時刻と対象を記録する自前判定（間隔 0.3 秒以内）に切り替える。

```
git commit -m "feat(timeline): ボーングループ名のダブルクリックで展開/折りたたみする"
```

---

### Task 7: IK 項目を左腕/右腕/左足/右足グループへ移動

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs:136`（`GetChainIndex` 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:209-223`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidIKHoldControllerTests.cs`（既存があれば追記、なければ新規）

**Interfaces:**
- Produces: `public static BoneSetMenuType MaidIKHoldController.GetBoneSetMenuType(MaidIKHoldType type)`

- [ ] **Step 1: テストを書く**

```csharp
[Theory]
[InlineData(MaidIKHoldType.Arm_L_Joint, BoneSetMenuType.LeftArm)]
[InlineData(MaidIKHoldType.Arm_L_Tip, BoneSetMenuType.LeftArm)]
[InlineData(MaidIKHoldType.Arm_R_Tip, BoneSetMenuType.RightArm)]
[InlineData(MaidIKHoldType.Foot_L_Joint, BoneSetMenuType.LeftLeg)]
[InlineData(MaidIKHoldType.Foot_R_Tip, BoneSetMenuType.RightLeg)]
public void IK固定種別は対応する腕脚グループへ割り当てられる(MaidIKHoldType type, BoneSetMenuType expected)
{
    Assert.Equal(expected, MaidIKHoldController.GetBoneSetMenuType(type));
}
```

`[InlineData]` は `MaidIKHoldType` の 8 値すべてを網羅する（上記に加え `Arm_L_Joint`→LeftArm 以外の残り: `Arm_R_Joint`→RightArm, `Foot_L_Tip`→LeftLeg, `Foot_R_Joint`→RightLeg）。テストは COM3D25 構成のビルド済み DLL を参照するので、事前に COM3D25 で MSBuild すること。

- [ ] **Step 2: 実装**

```csharp
/// <summary>IK 固定項目をボーンメニュー上で置く腕/脚グループ</summary>
public static BoneSetMenuType GetBoneSetMenuType(MaidIKHoldType type)
{
    switch (type)
    {
        case MaidIKHoldType.Arm_L_Joint:
        case MaidIKHoldType.Arm_L_Tip:
            return BoneSetMenuType.LeftArm;
        case MaidIKHoldType.Arm_R_Joint:
        case MaidIKHoldType.Arm_R_Tip:
            return BoneSetMenuType.RightArm;
        case MaidIKHoldType.Foot_L_Joint:
        case MaidIKHoldType.Foot_L_Tip:
            return BoneSetMenuType.LeftLeg;
        case MaidIKHoldType.Foot_R_Joint:
        case MaidIKHoldType.Foot_R_Tip:
            return BoneSetMenuType.RightLeg;
        default:
            return BoneSetMenuType.None;
    }
}
```

- [ ] **Step 3: MotionTimelineLayer.InitMenuItems で IK グループを廃止**

```csharp
// IK 固定は独立グループではなく対応する腕/脚グループの末尾に並べる
foreach (var boneName in MaidCache.ikHoldTypeMap.Keys)
{
    MaidIKHoldType holdType;
    if (!MaidIKHoldController.TryParseHoldType(boneName, out holdType))
    {
        continue;
    }

    var menuItem = new BoneMenuItem(boneName, MaidIKHoldController.GetHoldTypeName(holdType));
    var boneSetType = MaidIKHoldController.GetBoneSetMenuType(holdType);

    BoneSetMenuItem setMenuItem;
    if (boneSetType != BoneSetMenuType.None && setMenuItemMap.TryGetValue(boneSetType, out setMenuItem))
    {
        setMenuItem.AddChild(menuItem);
    }
    else
    {
        _allMenuItems.Add(menuItem);
    }
}
```

`ikSetMenuItem` の生成と `allMenuItems.Add(ikSetMenuItem)` は削除する。`setMenuItemMap` は同メソッド冒頭で定義済みのローカル変数。

- [ ] **Step 4: "IK" グループ名への参照が他に無いことを確認**

`grep -rn '"IK"' source/COM3D2.SceneEditor.Plugin --include=*.cs` が 0 件であること。

- [ ] **Step 5: テスト・両構成ビルド、コミット**

```
git commit -m "refactor(timeline): IK 固定項目を腕/脚のボーングループへ移す"
```

---

## Self-Review

- 要件 1〜8 はすべて Task に割り当て済み（上表）。
- 型名: `ToolbarIcons.Kind.Model` は既存、新規 8 種は Task 3 で定義し Task 4 で使用。`GUIView.EndScrollView(float)` / `DrawTextureRepeatButton` / `DrawToggle(... Color? onColor)` は Task 2/4 で追加し同 Task 内で使用。`GetBoneSetMenuType` は Task 7 内で定義・使用。
- 実機確認が必要な項目（ドラッグ挙動、ダブルクリック、アイコン見た目）はビルド後にゲームで確認する。ゲーム起動中は DLL コピーが失敗するためホットリロード手順（CLAUDE.md）を使うか、ユーザーに再起動確認を依頼する。

## レビュー却下メモ

- SceneView の「モデル」トグルと操作ウィンドウの「モデル表示」の意味的重複をツールチップで区別すべき — 既存の「メイド」トグルも同じ二重構造で、SceneView ツールバー上にある時点で作用範囲は明確なため、既存と揃える（却下）
- シークドラッグ中にウィンドウが閉じた場合の `_seekDragInfo` 残留 — `InvokeActionOnDragging` がボタン解放で毎回解除するため実害なし（却下）
