# タイムライン / カーブエディタのズーム Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GitHub issue #2 の要望「カーブエディターの画面を拡大縮小可能にしてほしい」に対し、カーブエディタの縦方向ズーム（ホイール）と、ドープシートとカーブエディタ共通の横方向ズーム（Ctrl+ホイール）を追加する。

**Architecture:**
- 縦: `CurveViewMapping` にズーム・パンの純粋関数を足し、`TimelineCurveEditor` が「手動の値域」（null なら従来の自動フィット）を持つ。ホイールでカーソル位置を軸に拡縮、中ボタンドラッグでパン、トグルバーの「フィット」ボタンか選択変更で自動フィットへ戻す。
- 横: 横軸は `config.frameWidth`（int px/フレーム）をドープシートとカーブで共有しているので、その値を段階的に変える。Ctrl+ホイールでカーソル下のフレームを固定したままスクロール位置を補正する。フレーム番号ラベルと背景の強調線の間隔は、幅に応じて自動で広げる。キーの菱形は幅によらず 5px 固定で、フレーム中心に置く（ユーザー指定）。1〜2px の幅ではフレームごとの中心線を引くと背景が塗りつぶされるため、強調線だけにする。
- 計算は `TimelineZoomMath`（新規、純粋ロジック）へ寄せて単体テストする。

**Tech Stack:** C# (.NET 3.5 / 4.x の 2 構成), Unity IMGUI, xUnit（`COM3D2.SceneEditor.Plugin.Tests`）

**Spec:** GitHub issue #2 の要望 1 項目目。ユーザー決定: 「カーブエディタは縦だけ。横方向は Ctrl+ホイールでタイムライン側含めた実装」

## Global Constraints

- コード内のコメントとログは日本語で書く
- COM3D2 / COM3D25 の両構成でビルドし、そのあと `dotnet test` を通す（`msbuild-from-bash`）
- テストでは Unity のネイティブ呼び出しを使わない（`Mathf.Min` / `Mathf.Clamp` のような managed 実装なら可）
- `config.frameWidth` は設定として保存される。そのためズームした幅は次回の起動にも残る（意図した挙動）

## Review Focus

1. ズーム後に、キーのクリック・ドラッグ、範囲選択、シークバーのクリック位置がずれない。いずれも `frameWidth` から計算しているため
2. 最小幅（1px）では、フレーム番号ラベルが重ならず、背景がフレーム線で塗りつぶされない
3. 手動ズーム中に別のボーンや値種別へ切り替えると自動フィットへ戻る。ズームしたまま別のカーブを開いても、何も映らない状態にならない。キーの追加・削除ではズームを保つ
4. Ctrl+ホイール以外のホイールは、従来どおりドープシートの縦スクロールに使われる
5. 表情ウィンドウの目線ドット（`EyesPosRowDrawer`）の大きさが、横ズームに引きずられない

---

### Task 1: TimelineZoomMath（純粋ロジック）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineZoomMath.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineZoomMathTests.cs`

**Interfaces:**
- Produces (namespace `COM3D2.MotionTimelineEditor.Plugin`):
  - `public static class TimelineZoomMath`
  - `public static readonly int[] FrameWidthSteps = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 13, 16, 20, 24, 30, 40 };`
  - `public static int StepFrameWidth(int current, int direction)`: `direction > 0` なら 1 段拡大、`< 0` なら 1 段縮小する。段の途中の値は最寄りの段に丸めてから動かし、両端でクランプする。
  - `public static float AnchorScrollX(float scrollX, float mouseX, int oldWidth, int newWidth)`: 戻り値は `Max(0, (scrollX + mouseX) * newWidth / oldWidth - mouseX)`。`mouseX` はビュー左端からの距離。
  - `public static int LabelInterval(int frameWidth, int baseInterval)`: `baseInterval × {1, 2, 6, 12, 60}` のうち、`間隔 × frameWidth >= MinLabelSpacing (50)` を満たす最小値。どれも満たさなければ最大値。
  - `public const int KeySize = 5;`: キーの菱形の一辺。フレーム幅によらず固定（ユーザー指定）。幅が 5px 未満のときは隣のキーと重なって描かれるが、掴めることを優先する
  - `public const int MinFrameLineWidth = 4;`: この幅未満ではフレームごとの中心線を引かない（1〜3px では線が背景を埋めてしまうため）
  - `public static bool IsControlHeld()`: `Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)`。Unity の Input を読むので単体テストの対象外

- [ ] **Step 1: テストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineZoomMathTests
    {
        [Fact]
        public void 拡大で1段上がる() => Assert.Equal(13, TimelineZoomMath.StepFrameWidth(11, 1));

        [Fact]
        public void 縮小で1段下がる() => Assert.Equal(9, TimelineZoomMath.StepFrameWidth(11, -1));

        [Fact]
        public void 両端でクランプする()
        {
            Assert.Equal(40, TimelineZoomMath.StepFrameWidth(40, 1));
            Assert.Equal(1, TimelineZoomMath.StepFrameWidth(1, -1));
        }

        [Fact]
        public void 段にない値は最寄りの段から動かす()
        {
            // 10 は 9 と 11 の中間。近い方 (同距離なら小さい方の 9) を基準に 1 段上げて 11
            Assert.Equal(11, TimelineZoomMath.StepFrameWidth(10, 1));
        }

        [Fact]
        public void カーソル下のフレームを固定してスクロールを補正する()
        {
            // 幅 10 でスクロール 100・カーソル 50 → カーソル下は 15 フレーム目。幅 20 では 300 - 50 = 250
            Assert.Equal(250f, TimelineZoomMath.AnchorScrollX(100f, 50f, 10, 20), 3);
        }

        [Fact]
        public void スクロールは負にならない()
        {
            Assert.Equal(0f, TimelineZoomMath.AnchorScrollX(0f, 50f, 20, 10), 3);
        }

        [Fact]
        public void ラベル間隔は幅が狭いほど広がる()
        {
            Assert.Equal(5, TimelineZoomMath.LabelInterval(11, 5));   // 55px
            Assert.Equal(10, TimelineZoomMath.LabelInterval(6, 5));   // 60px
            Assert.Equal(30, TimelineZoomMath.LabelInterval(3, 5));   // 90px (10 だと 30px で不足)
            Assert.Equal(60, TimelineZoomMath.LabelInterval(1, 5));   // 60px
        }
    }
}
```

- [ ] **Step 2: テストが落ちることを確認**（`TimelineZoomMath` が無いのでコンパイルエラー）
- [ ] **Step 3: 実装**（上の Interfaces どおり。定数には「なぜその値か」を 1 行コメントで添える: 1px が 1 フレームの最小表現、40px を超えると 1 画面に数十フレームしか入らない）
- [ ] **Step 4: 両構成ビルド → `dotnet test --filter TimelineZoomMathTests` が通る**

### Task 2: CurveViewMapping のズームとパン

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CurveViewMapping.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CurveViewMappingTests.cs`（追記）

**Interfaces:**
- Produces:
  - `public CurveViewMapping ZoomValue(float factor, float pivotY)`: `pivotY` の値を固定して値域を `1/factor` 倍にする（factor>1 で拡大）。
  - `public CurveViewMapping PanValue(float dyPx)`: 画面上で `dyPx` 下へずらした分だけ値域を上へずらす（ドラッグで掴んだ曲線が指に付いてくる向き）。

- [ ] **Step 1: テストを追記**

```csharp
[Fact]
public void 縦ズーム_ピボットの値が動かない()
{
    var m = new CurveViewMapping(10f, 100f, 0f, 10f);
    var pivotValue = m.YToValue(25f);
    var z = m.ZoomValue(2f, 25f);
    Assert.Equal(pivotValue, z.YToValue(25f), 3);
    Assert.Equal(5f, z.valueMax - z.valueMin, 3);
}

[Fact]
public void 縦パン_下へドラッグすると値域が上へずれる()
{
    var m = new CurveViewMapping(10f, 100f, 0f, 10f);
    var p = m.PanValue(10f);   // 10px = 値 1
    Assert.Equal(1f, p.valueMin, 3);
    Assert.Equal(11f, p.valueMax, 3);
}
```

- [ ] **Step 2: テストが落ちることを確認**
- [ ] **Step 3: 実装**

```csharp
/// <summary>縦ズーム。pivotY の値を画面上で固定したまま値域を 1/factor 倍にする</summary>
public CurveViewMapping ZoomValue(float factor, float pivotY)
{
    var pivotValue = YToValue(pivotY);
    var newMin = pivotValue - (pivotValue - valueMin) / factor;
    var newMax = pivotValue + (valueMax - pivotValue) / factor;
    return new CurveViewMapping(frameWidth, paneHeight, newMin, newMax);
}

/// <summary>縦パン。下へ dyPx ドラッグしたら曲線も下へ付いてくるよう値域を上へずらす</summary>
public CurveViewMapping PanValue(float dyPx)
{
    var delta = dyPx * (valueMax - valueMin) / paneHeight;
    return new CurveViewMapping(frameWidth, paneHeight, valueMin + delta, valueMax + delta);
}
```

- [ ] **Step 4: 両構成ビルド → `dotnet test --filter CurveViewMappingTests` が通る**

### Task 3: カーブエディタの縦ズーム UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Consumes: Task 2 の `ZoomValue` / `PanValue`
- Produces: `public bool isValueRangeManual`（トグルバーのボタン表示に使う）と `public void ResetValueRange()`

- [ ] **Step 1: 手動値域の状態を足す**

```csharp
/// <summary>手動ズーム・パン中の値域。null なら表示範囲へ自動フィットする</summary>
private float? _manualValueMin;
private float? _manualValueMax;
/// <summary>中ボタンでパン中か。押下位置からの差分ではなく前フレームとの差分で動かす</summary>
private bool _isPanning;
private float _panLastY;
/// <summary>OnGUI はイベントごとに走るため、ホイール処理をフレーム 1 回に絞る</summary>
private int _wheelZoomFrame = -1;
/// <summary>自動フィットへ戻す判定用。表示中チャンネルの種別と対象ボーンの組</summary>
private string _valueRangeViewKey;

public bool isValueRangeManual => _manualValueMin.HasValue;

public void ResetValueRange()
{
    _manualValueMin = null;
    _manualValueMax = null;
}
```

- [ ] **Step 2: `BuildMapping` で手動値域を優先する**

`DrawPane` の `_mapping = BuildMapping(...)` の直後に次を足す。サンプル収集（`channel.samples`）はカーブ描画に要るので `BuildMapping` は従来どおり呼ぶ。

```csharp
if (_manualValueMin.HasValue)
{
    _mapping = new MTEP.CurveViewMapping(
        frameWidth, graphRect.height, _manualValueMin.Value, _manualValueMax.Value);
}
```

- [ ] **Step 3: 表示対象が変わったら自動フィットへ戻す**

`channelsRebuilt` はキーの追加・削除（キー数の変化）でも立つ。これをトリガーにすると、ズームしたままキーを打つたびにズームが解除されてしまう。そこで表示対象の組（チャンネルの `displayName` と、`keyBones` のボーン名から重複を除いたもの）だけでキーを作り、それが変わったときだけ自動フィットへ戻す。

```csharp
/// <summary>チャンネルの種別と対象ボーンの組。キー数には依存させない</summary>
private static string BuildValueRangeViewKey(List<CurveChannel> channels)
{
    var sb = new StringBuilder();
    foreach (var channel in channels)
    {
        sb.Append(channel.displayName).Append('|');
        string lastBone = null;
        foreach (var bone in channel.keyBones)
        {
            if (bone != null && bone.name != lastBone)
            {
                lastBone = bone.name;
                sb.Append(lastBone).Append(',');
            }
        }
        sb.Append(';');
    }
    return sb.ToString();
}
```

`DrawPane` の `channelsRebuilt = true;` の直後に足す:

```csharp
var viewKey = BuildValueRangeViewKey(_channels);
if (viewKey != _valueRangeViewKey)
{
    _valueRangeViewKey = viewKey;
    ResetValueRange();
}
```

- [ ] **Step 4: `HandleInput` にホイールと中ボタンドラッグを足す**

`HandleInput` の `if (_dragMode != DragMode.None)` の前に置く。Ctrl 付きのホイールは横ズーム（Task 4）が処理するので、ここでは扱わない。

IMGUI の `ScrollWheel` イベントは、手前のコントロールに消費されて届かないことがある。そのため `TabBarDrawer.HandleWheelScroll` や `SceneViewWindow` と同じく、ホイール量は `Input.GetAxis("Mouse ScrollWheel")` から直接読む。イベントが届いた場合は、下のコントロールへ流さないように消費する。

```csharp
var inGraph = mouse.x >= 0f && mouse.x <= graphRect.width && mouse.y >= 0f && mouse.y <= graphRect.height;

if (inGraph && _mapping != null && !MTEP.TimelineZoomMath.IsControlHeld())
{
    if (e.type == EventType.ScrollWheel)
    {
        e.Use();
    }
    var wheel = Input.GetAxis("Mouse ScrollWheel");
    if (wheel != 0f && _wheelZoomFrame != Time.frameCount)
    {
        _wheelZoomFrame = Time.frameCount;
        // 奥へ回す (プラス) と拡大。1 ノッチ 1.2 倍
        var factor = wheel > 0f ? 1.2f : 1f / 1.2f;
        SetManualRange(_mapping.ZoomValue(factor, mouse.y));
    }
}

if (e.type == EventType.MouseDown && e.button == 2 && inGraph && _mapping != null)
{
    _isPanning = true;
    _panLastY = mouse.y;
    e.Use();
    return;
}
if (_isPanning)
{
    if (!Input.GetMouseButton(2))
    {
        _isPanning = false;
    }
    else if (e.type == EventType.MouseDrag)
    {
        SetManualRange(_mapping.PanValue(mouse.y - _panLastY));
        _panLastY = mouse.y;
        e.Use();
    }
    return;
}
```

```csharp
private void SetManualRange(MTEP.CurveViewMapping mapping)
{
    _manualValueMin = mapping.valueMin;
    _manualValueMax = mapping.valueMax;
    _mapping = mapping;
}
```

- [ ] **Step 5: トグルバーに「フィット」ボタンを出す**

`DrawToggleBar` の「▼ カーブ」ボタンの右に、`isOpen && isValueRangeManual` のときだけ幅 50 の「フィット」ボタンを出す。押すと `ResetValueRange()` を呼ぶ。

- [ ] **Step 6: 両構成ビルド**

### Task 4: 横ズーム（Ctrl+ホイール）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/EyesPosRowDrawer.cs:81`

**Interfaces:**
- Consumes: Task 1 の `TimelineZoomMath`

- [ ] **Step 1: Ctrl+ホイールの処理**

ドープシートの `view.BeginScrollView(...)` より前に置く。スクロールビューはホイールを自前の縦スクロールに使うので、Ctrl を押しているときは届いたイベントを先に `Use()` する。

- **ホイール量:** カーブ側と同じく `Input.GetAxis("Mouse ScrollWheel")` から読み、処理はフレームに 1 回へ絞る。
- **判定範囲:** カーブペインの上でも効くように、ドープシートとカーブペインを合わせた領域にする（`menuWidth` より右、`FRAME_LABEL_HEIGHT` から `_contentHeight` まで）。
- **余白の扱い:** `_contentHeight` はウィンドウ内容の高さなので、カーブペインを閉じていてもその下の余白まで範囲に入る。これは無害として許容する。

```csharp
/// <summary>OnGUI はイベントごとに走るため、横ズームをフレーム 1 回に絞る</summary>
private int _horizontalZoomFrame = -1;

private void HandleHorizontalZoom(GUIView view, float viewWidth)
{
    if (!MTEP.TimelineZoomMath.IsControlHeld())
    {
        return;
    }

    var e = Event.current;
    var tc = timelineConfig;
    var origin = view.GetDrawRect(tc.menuWidth, FRAME_LABEL_HEIGHT, 1f, 1f);
    var mouseX = e.mousePosition.x - origin.x;
    var mouseY = e.mousePosition.y - origin.y;
    if (mouseX < 0f || mouseX > viewWidth || mouseY < 0f || mouseY > _contentHeight - FRAME_LABEL_HEIGHT)
    {
        return;
    }

    // Ctrl+ホイールでドープシートが縦に流れないよう、届いたイベントは消費する
    if (e.type == EventType.ScrollWheel)
    {
        e.Use();
    }

    var wheel = Input.GetAxis("Mouse ScrollWheel");
    if (wheel == 0f || _horizontalZoomFrame == Time.frameCount)
    {
        return;
    }
    _horizontalZoomFrame = Time.frameCount;

    var oldWidth = tc.frameWidth;
    var newWidth = MTEP.TimelineZoomMath.StepFrameWidth(oldWidth, wheel > 0f ? 1 : -1);
    if (newWidth == oldWidth)
    {
        return;
    }

    timelineView.scrollPosition.x = MTEP.TimelineZoomMath.AnchorScrollX(
        timelineView.scrollPosition.x, mouseX, oldWidth, newWidth);
    tc.frameWidth = newWidth;
    tc.dirty = true;

    // 背景テクスチャは幅ごとに作り直す
    requestUpdateTexture = true;
}
```

キーのテクスチャは固定サイズなので、作り直すのは背景だけでよい。

- [ ] **Step 2: ラベル間隔を幅に合わせる**

`frameNoInterval` を使っている 5 箇所（`UpdateTexture` の `bgWidth` の余白計算、`CreateBGTexture` の引数、背景タイルのスナップループの刻みと可視判定、フレーム番号ラベル）を、`MTEP.TimelineZoomMath.LabelInterval(tc.frameWidth, tc.frameNoInterval)` の結果に置き換える。背景テクスチャの周期と、スナップの周期は必ず同じ値にする（ずれると縞がずれる）。

あわせて `TimelineData.CreateBGTexture` に `bool drawFrameLines` 引数を足し、`frameWidth >= TimelineZoomMath.MinFrameLineWidth` のときだけフレームごとの中心線を引く。強調線（`frameNo % frameNoInterval == 0` の列）は常に引く。幅 1px では `framePos == frameWidth / 2` が全列で成り立ってしまうため、この分岐が要る。

- [ ] **Step 3: キーの菱形を 5px 固定にしてフレーム中心へ置く**

`texKeyFrame` を作るときの大きさは `TimelineZoomMath.KeySize`（5）にする。描画とヒット判定の矩形はフレーム中心を基準に置き直す。幅が 5px 未満のときは `keyOffsetX` が負になるが、それで正しい。

```csharp
var keySize = MTEP.TimelineZoomMath.KeySize;
var keyOffsetX = (frameWidth - keySize) * 0.5f;
var adjustY = (frameHeight - keySize) / 2;
// view.currentPos.x = frameNo * frameWidth + keyOffsetX;
// keyFrameRect / DrawTexture の幅・高さは keySize
```

表示範囲外の判定（`view.currentPos.x < scrollPosition.x` など）は、フレーム左端の `frameNo * frameWidth` のまま比較する。

キーのドラッグは `view.currentPos` を起点に `InvokeActionOnDragStart` へ渡す。そのためドラッグ中の `newPos.x` にも `keyOffsetX` が乗っているので、着地フレームを求める計算（`targetFrameNo = (int)((newPos.x + halfFrameWidth) / frameWidth)`）からこれを差し引く。

```csharp
var targetFrameNo = (int)((newPos.x - keyOffsetX + halfFrameWidth) / frameWidth);
```

`keyOffsetX` はキー描画ループの外で 1 回だけ計算し、描画とドラッグ処理の両方で共有する。

- [ ] **Step 4: 目線ドットを横ズームから切り離す**

`EyesPosRowDrawer.cs:81` の `config.frameWidth` を、クラス内の定数 `EyesDotSize = 11`（従来の既定幅）に置き換える。

- [ ] **Step 5: 両構成ビルドと全テスト**

### Task 5: 実機検証・ドキュメント

**Files:**
- Modify: `docs-site/timeline/`（カーブエディタとドープシートの操作説明のページ。`grep -rn "カーブ" docs-site/timeline/*.md` で探す）

- [ ] **Step 1: 実機検証**（`com3d25-restart-verify`）
  - カーブペイン上のホイールで縦に拡縮する。中ボタンドラッグでパンする。「フィット」で自動フィットへ戻る。キーの選択を変えても自動フィットへ戻る
  - ドープシート上とカーブペイン上の Ctrl+ホイールで横に拡縮し、カーソル下のフレームが動かない
  - 最小幅と最大幅で、キーのクリック・ドラッグ（着地フレーム）、範囲選択、シークのクリック位置が合っている。ラベルが重ならない
  - 縦ズーム中にキーを追加・削除してもズームが保たれる。別のボーンや値種別へ切り替えると自動フィットへ戻る
  - 修飾キーなしのホイールで、ドープシートが縦スクロールする
  - 表情ウィンドウの目線ドットの大きさが変わらない
- [ ] **Step 2: docs-site へ操作説明を追記**（ホイール: 縦ズーム / 中ドラッグ: パン / フィット / Ctrl+ホイール: 横ズーム）
- [ ] **Step 3: code-review → commit**

## レビュー却下メモ

- なし（指摘 5 件はすべて取り込み済み）
