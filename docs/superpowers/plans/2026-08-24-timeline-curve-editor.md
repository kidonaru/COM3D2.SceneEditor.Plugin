# タイムラインカーブエディタ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (このリポジトリでは subagent-driven-development は使用禁止)

**Goal:** Inspector の補間曲線 UI を廃止し、TimelineWindow 下部に実値カーブエディタ(Unity 風)を新設。補間モデルを Tangent に統一し Easing を廃止する。

**Architecture:** Phase A で `TimelineCurveEditor`(非ウィンドウのビュークラス)を TimelineWindow の分割ペインとして追加し、Inspector の DrawTangent/DrawEasing を削除。Phase B で easing→Tangent のロード時近似変換を導入し、約 20 レイヤーの再生経路を Hermite に一本化する。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI)、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** `docs/superpowers/specs/2026-08-24-timeline-curve-editor-design.md`

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree 禁止。作業はメイン作業ディレクトリの `feature/timeline-window` ブランチ
- ビルド確認: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`(debug.bat はゲームフォルダへコピーするため使わない)。deploy.bat / release.bat は絶対に実行しない
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`(既存 70 件を退行させない)
- MTEUtils 新旧差分: `view.IsComboBoxFocused()` は存在しない → `view.focusedComboBox == null` を使う。コンボを描くウィンドウは DrawContent 末尾で `ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this)`(TimelineWindow は対応済み)
- windowId の変更禁止(新規ウィンドウは作らないので今回は追加なし)
- テスト名前空間では SE 側 `PluginUtils` が優先される → MTE 側は `MTEPluginUtils` 等の別名エイリアスで参照
- 新規ファイルは csproj の `<Compile Include>` への追加を忘れない
- コミットフッター: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_01DLFeUa1GdhB8PtLZ3d3VCQ`

## データモデルの前提知識(全タスク共通)

- `ValueData` は値 + `inTangent`/`outTangent`(`TangentData`)を常時保持
- `TangentData.normalizedValue` は「区間の線形勾配に対する倍率」(1=線形、0=フラット)。実勾配 `value` は `UpdateValue(baseSlope)` で `normalizedValue * baseSlope` として更新される(`TransformDataBase.cs:430-475` の `UpdateTangent` 参照)。キー B の `inTangent` は流入区間の勾配 `v0`、`outTangent` は流出区間の勾配 `v1` が基準
- 実値補間は `PluginUtils.Hermite(t0, t1, v0, v1, outTangent, inTangent, t)`(タンジェントは実勾配、`h10 * dt * outTangent` でスケール)
- **単位系の原則(全タスク共通・厳守)**: `TangentData.normalizedValue` は常に無次元比(区間線形勾配に対する倍率)。実勾配 `TangentData.value` は **値/秒** (`UpdateTangent` が `GetFrameTimeSeconds` 由来の秒単位 dt で算出)。したがって `Hermite` を実値で呼ぶときの `t0`/`t1` は必ず **秒単位** (`frameNo * timeline.frameDuration`)、`t` は 0〜1 正規化(再生経路 `MoveTimelineLayer.cs:121-136` + `MotionPlayData.lerpFrame` と同じ組み合わせ)。`t0=0, t1=1` で呼ぶとタンジェント項のスケールが再生結果と乖離するので禁止
- Easing 補間は `EasingFunctions.MoveEasing(t, MoveEasingType)`(全 22 種、単調)
- チャンネル列挙は `transform.GetValueDataList(TangentValueType)`(`TransformDataBase.cs:835`)
- キーフレームは `ITimelineLayer.keyFrames` (`List<FrameData>`) → `FrameData.bones` → `BoneData.transform`
- 選択状態は `TimelineManager.instance.selectedBones` (`HashSet<BoneData>`)
- 履歴は `TimelineHistoryManager.instance.AddHistory(timeline, "説明")`
- 編集反映は `currentLayer.ApplyCurrentFrame(true)`

---

## Phase A — カーブエディタ本体

### Task A1: カーブ座標マッピングの純粋ロジック

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CurveViewMapping.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(Compile Include 追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CurveViewMappingTests.cs`

**Interfaces:**
- Produces: `CurveViewMapping`(コンストラクタ `(float frameWidth, float paneHeight, float valueMin, float valueMax)`、`float FrameToX(float)` / `float ValueToY(float)` / `float YToValue(float)` / `float ScreenSlopeToValueSlope(float dxPx, float dyPx)` / `static CurveViewMapping AutoFit(float frameWidth, float paneHeight, IEnumerable<float> sampledValues)`)

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CurveViewMappingTests
    {
        [Fact]
        public void 値からY座標へ_上端が最大値()
        {
            // paneHeight=100, 値域 0..10 → 値10 は Y=0(上端)、値0 は Y=100(下端)
            var m = new CurveViewMapping(frameWidth: 11f, paneHeight: 100f, valueMin: 0f, valueMax: 10f);
            Assert.Equal(0f, m.ValueToY(10f), 3);
            Assert.Equal(100f, m.ValueToY(0f), 3);
            Assert.Equal(50f, m.ValueToY(5f), 3);
        }

        [Fact]
        public void Y座標から値へ_往復が一致()
        {
            var m = new CurveViewMapping(11f, 100f, -2f, 8f);
            Assert.Equal(3.5f, m.YToValue(m.ValueToY(3.5f)), 3);
        }

        [Fact]
        public void フレームからX座標へ_フレーム中心()
        {
            var m = new CurveViewMapping(10f, 100f, 0f, 1f);
            // ドープシートのキー描画に合わせフレーム中心 (frameNo * frameWidth + frameWidth/2)
            Assert.Equal(5f, m.FrameToX(0), 3);
            Assert.Equal(35f, m.FrameToX(3), 3);
        }

        [Fact]
        public void スクリーン勾配から値勾配へ()
        {
            // frameWidth=10px, 値域 0..10 を 100px → 1 値 = 10px
            // 画面上 右10px・上10px の勾配 = 1フレームあたり値 +1
            var m = new CurveViewMapping(10f, 100f, 0f, 10f);
            Assert.Equal(1f, m.ScreenSlopeToValueSlope(10f, -10f), 3);
        }

        [Fact]
        public void AutoFit_余白付きで値域を決める()
        {
            var m = CurveViewMapping.AutoFit(11f, 100f, new[] { 0f, 10f });
            // 上下 10% 余白
            Assert.Equal(-1f, m.valueMin, 3);
            Assert.Equal(11f, m.valueMax, 3);
        }

        [Fact]
        public void AutoFit_値域ゼロでも潰れない()
        {
            var m = CurveViewMapping.AutoFit(11f, 100f, new[] { 5f, 5f });
            Assert.True(m.valueMax > m.valueMin);
            Assert.Equal(50f, m.ValueToY(5f), 3);
        }
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter CurveViewMappingTests`
Expected: コンパイルエラー(CurveViewMapping 未定義)

- [ ] **Step 3: 実装**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カーブビューのスクリーン座標⇔値のマッピング (純粋ロジック、単体テスト対象)。
    /// 横軸はドープシートとフレームスケールを共有し、縦軸は表示値域に自動フィットする
    /// </summary>
    public class CurveViewMapping
    {
        public float frameWidth { get; private set; }
        public float paneHeight { get; private set; }
        public float valueMin { get; private set; }
        public float valueMax { get; private set; }

        public CurveViewMapping(float frameWidth, float paneHeight, float valueMin, float valueMax)
        {
            this.frameWidth = frameWidth;
            this.paneHeight = paneHeight;
            this.valueMin = valueMin;
            this.valueMax = valueMax;
        }

        public float FrameToX(float frameNo)
        {
            // ドープシートのキー描画に合わせてフレーム中心へ置く
            return frameNo * frameWidth + frameWidth * 0.5f;
        }

        public float ValueToY(float value)
        {
            var range = valueMax - valueMin;
            return paneHeight * (1f - (value - valueMin) / range);
        }

        public float YToValue(float y)
        {
            var range = valueMax - valueMin;
            return valueMin + (1f - y / paneHeight) * range;
        }

        /// <summary>スクリーン上の移動量 (px) を「1フレームあたりの値変化量」へ変換</summary>
        public float ScreenSlopeToValueSlope(float dxPx, float dyPx)
        {
            if (dxPx == 0f)
            {
                return 0f;
            }
            var valuePerPx = (valueMax - valueMin) / paneHeight;
            var framePerPx = 1f / frameWidth;
            return (-dyPx * valuePerPx) / (dxPx * framePerPx);
        }

        public static CurveViewMapping AutoFit(
            float frameWidth,
            float paneHeight,
            IEnumerable<float> sampledValues)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var v in sampledValues)
            {
                if (float.IsNaN(v)) continue;
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            if (min > max)
            {
                min = 0f;
                max = 1f;
            }

            var range = max - min;
            if (range <= 0f)
            {
                // 値域ゼロ (全キー同値) は中央表示になるよう固定幅を与える
                range = Mathf.Max(1f, Mathf.Abs(min) * 0.2f);
                min -= range * 0.5f;
                max += range * 0.5f;
                return new CurveViewMapping(frameWidth, paneHeight, min, max);
            }

            // 上下 10% 余白
            return new CurveViewMapping(
                frameWidth, paneHeight, min - range * 0.1f, max + range * 0.1f);
        }
    }
}
```

csproj の `<Compile Include="Timeline\CurveData.cs" />` 付近に `<Compile Include="Timeline\CurveViewMapping.cs" />` を追加。

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter CurveViewMappingTests`
Expected: 6 件 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/CurveViewMapping.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/CurveViewMappingTests.cs
git commit -m "feat(timeline): カーブビューの座標マッピングロジックを追加"
```

### Task A2: カーブペインの土台 (折りたたみ・リサイズ・スクロール共有)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`(DrawTimeline のビュー高さ分割 + 呼び出し)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs`(永続化フィールド追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `CurveViewMapping` (Task A1)
- Produces: `TimelineCurveEditor.instance`(シングルトン)、`bool isOpen`、`float paneHeight`、`void DrawPane(GUIView view, Rect paneRect, float scrollX, float frameWidth, bool guiEnabled)`、`void DrawToggleBar(GUIView view, Rect barRect)`(高さ 16px のバー: 開閉トグル + 境界ドラッグでリサイズ)

- [ ] **Step 1: Config に永続化フィールドを追加**

`Config.cs` の `curveBgColor` 付近に追加:

```csharp
public bool isCurveEditorOpen = false;
public int curveEditorHeight = 150;
```

- [ ] **Step 2: TimelineCurveEditor の骨格を実装**

`TimelineCurveEditor.cs` を新規作成。`KeyFrameInspector` と同じシングルトン流儀:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン下部の実値カーブエディタ。
    /// 選択中キーフレームのチャンネル値をカーブとして描画し、
    /// キー点の値ドラッグとタンジェントハンドル編集を提供する
    /// </summary>
    public class TimelineCurveEditor
    {
        private const float TOGGLE_BAR_HEIGHT = 16f;
        private const int MIN_PANE_HEIGHT = 80;
        private const int MAX_PANE_HEIGHT = 400;

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private static TimelineCurveEditor _instance = null;
        public static TimelineCurveEditor instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineCurveEditor();
                }
                return _instance;
            }
        }

        public bool isOpen
        {
            get => config.isCurveEditorOpen;
            set
            {
                if (config.isCurveEditorOpen == value) return;
                config.isCurveEditorOpen = value;
                config.dirty = true;
            }
        }

        public float paneHeight => config.curveEditorHeight;

        /// <summary>開閉トグルと高さリサイズを持つバーの描画</summary>
        public void DrawToggleBar(GUIView view, Rect barRect) { /* Task A2 Step 3 */ }

        /// <summary>カーブ描画領域。paneRect はスクロールビュー内ローカル座標</summary>
        public void DrawPane(GUIView view, Rect paneRect, float scrollX, float frameWidth, bool guiEnabled) { /* Task A3 以降 */ }
    }
}
```

(検証済み: `Config.dirty` は `Config.cs:269` に存在し、`ConfigManager.cs:43` が `dirty && Input.GetMouseButtonUp(0)` で保存する。ドラッグ中に保存 IO は発生しないためリサイズ中の dirty 更新は安全)

- [ ] **Step 3: トグルバー実装 (開閉 + ドラッグリサイズ)**

```csharp
        private bool _isResizing = false;
        private float _resizeStartY = 0f;
        private int _resizeStartHeight = 0;

        public void DrawToggleBar(GUIView view, Rect barRect)
        {
            view.currentPos = new Vector2(barRect.x, barRect.y);
            view.DrawTexture(GUIView.texWhite, barRect.width, barRect.height,
                new Color(0.2f, 0.2f, 0.2f, 0.8f));

            view.currentPos = new Vector2(barRect.x + 5, barRect.y);
            if (view.DrawButton(isOpen ? "▼ カーブ" : "▲ カーブ", 80, 16))
            {
                isOpen = !isOpen;
            }

            // 開いている間はバー全体を上下ドラッグで高さ変更
            if (isOpen)
            {
                var screenRect = new Rect(barRect.x + 90, barRect.y, barRect.width - 90, barRect.height);
                var mousePos = Event.current.mousePosition;
                if (Event.current.type == EventType.MouseDown && screenRect.Contains(mousePos))
                {
                    _isResizing = true;
                    _resizeStartY = mousePos.y;
                    _resizeStartHeight = config.curveEditorHeight;
                }
                if (_isResizing)
                {
                    if (Event.current.type == EventType.MouseUp || !Input.GetMouseButton(0))
                    {
                        _isResizing = false;
                        config.dirty = true;
                    }
                    else
                    {
                        var diff = _resizeStartY - mousePos.y;
                        config.curveEditorHeight = Mathf.Clamp(
                            _resizeStartHeight - (int)diff, MIN_PANE_HEIGHT, MAX_PANE_HEIGHT);
                    }
                }
            }
        }
```

- [ ] **Step 4: TimelineWindow に組み込む**

`TimelineWindow.cs` の変更:
1. `timelineViewHeight` (line 90 付近) を分割: カーブペインが開いている間はドープシート領域を縮める

```csharp
        private float curvePaneTotalHeight =>
            TimelineCurveEditor.instance.isOpen
                ? TimelineCurveEditor.instance.paneHeight + 16f  // トグルバー分
                : 16f;
        // ウィンドウが低い状態でペインを開いてもドープシートが負高さにならないようガード
        private int timelineViewHeight => Mathf.Max(
            60, (int)(_contentHeight - FRAME_LABEL_HEIGHT - curvePaneTotalHeight));
```

(ガードで確保した分ペインがウィンドウ下端からはみ出るケースは、`DrawPane` 側で `paneRect.height` を `_contentHeight - paneTop - 16f` にクランプして吸収する)

2. `DrawTimeline` の `view.BeginScrollView(...)~EndScrollView` の後(スクロール外、ウィンドウローカル座標)で、下部にトグルバーとペイン領域を描画:

```csharp
            // カーブエディタペイン (ドープシート下部)
            var curveEditor = TimelineCurveEditor.instance;
            var paneTop = FRAME_LABEL_HEIGHT + timelineViewHeight;
            curveEditor.DrawToggleBar(view, new Rect(menuWidth, paneTop, viewWidth, 16f));
            if (curveEditor.isOpen)
            {
                curveEditor.DrawPane(
                    view,
                    new Rect(menuWidth, paneTop + 16f, viewWidth, curveEditor.paneHeight),
                    scrollPosition.x,
                    frameWidth,
                    guiEnabled);
            }
```

(実装時の注意: `DrawTimeline` 内の `viewHeight`/スクロールバー表示と競合しないこと。`scrollPosition` は `BeginScrollView` 後に取得済みの変数を使う)

3. csproj に `<Compile Include="TimelineCurveEditor.cs" />` を追加(`KeyFrameInspector.cs` の近く)

- [ ] **Step 5: ビルド確認**

Run: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
Expected: ビルド成功

- [ ] **Step 6: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "feat(timeline): カーブエディタペインの土台を追加 (開閉・リサイズ・永続化)"
```

### Task A3: カーブ描画 (チャンネル列挙 + Hermite/Easing サンプリング)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Consumes: `CurveViewMapping`、`transform.GetValueDataList(TangentValueType)`、`PluginUtils.Hermite`、`EasingFunctions.MoveEasing`
- Produces: `DrawPane` 内部で使う `List<CurveChannel> CollectChannels()`(CurveChannel = ボーン名 + TangentValueType 単チャンネル + 色 + キー列)

- [ ] **Step 1: チャンネル収集を実装**

選択ボーン(`selectedBones`)のボーン名ごとに、レイヤーの全キーフレームから同名ボーンの `BoneData` を時系列で集める。チャンネルは種別フィルタ(`_valueTypeFilter`、初期値 `TangentValueType.すべて`)に応じ、単チャンネル型(X移動〜Z拡縮)へ展開:

```csharp
        /// <summary>1 本のカーブ = 1 ボーン × 1 値チャンネル</summary>
        private class CurveChannel
        {
            public string boneName;
            public MTEP.TangentValueType valueType;  // 単チャンネル型のみ
            public Color color;
            // フレーム番号順のキー列 (frameNo, ValueData)
            public List<KeyValuePair<int, MTEP.ValueData>> keys
                = new List<KeyValuePair<int, MTEP.ValueData>>();
            public List<MTEP.BoneData> keyBones = new List<MTEP.BoneData>();
        }

        private static readonly MTEP.TangentValueType[] SingleChannelTypes = {
            MTEP.TangentValueType.X移動, MTEP.TangentValueType.Y移動, MTEP.TangentValueType.Z移動,
            MTEP.TangentValueType.X回転, MTEP.TangentValueType.Y回転, MTEP.TangentValueType.Z回転,
            MTEP.TangentValueType.X拡縮, MTEP.TangentValueType.Y拡縮, MTEP.TangentValueType.Z拡縮,
        };

        private static readonly Color[] ChannelColors = {
            new Color(0.9f, 0.3f, 0.3f),  // X = 赤
            new Color(0.3f, 0.9f, 0.3f),  // Y = 緑
            new Color(0.4f, 0.6f, 1.0f),  // Z = 青
        };
```

収集ロジック: フィルタが複合型(移動/回転/拡縮/すべて)の場合は該当する単チャンネル型に展開し、`GetValueDataList(単チャンネル型)` が空配列を返すチャンネルはスキップ。**表示チャンネル数は `MAX_CHANNELS = 12` でクランプ**し、超過時はペイン左上に「表示上限 12 チャンネル (全 N)」ラベルを出す(全身ボーン選択時の draw call 急増対策)。色は X/Y/Z 系で `ChannelColors[i % 3]`。W回転は UI 対象外(クォータニオン成分の直接編集は不自然なため回転は euler 表示のみ)。カスタム値チャンネル(`GetCustomValueInfoMap`)は `TangentValueType` に無いため、`CurveChannel` に `customKey` フィールドを持たせて別経路で収集し、色はパレット(黄・橙・紫系)を順番に割り当てる。

- [ ] **Step 2: カーブサンプリングと描画を実装**

`DrawPane` 内: 表示範囲のフレーム(scrollX から viewWidth 分)についてチャンネルごとに 2px 刻みでサンプリングし、`view.DrawTexture(GUIView.texWhite, ...)` の 2px 幅セグメントを縦に置いて折れ線描画(ドープシートの縦線描画と同じ流儀)。既存 `CurveEditorWindow`/`KeyFrameInspector` の Texture2D プロット方式は採用しない — スクロール・選択・ドラッグのたびに `SetPixel` + `Apply` の全面再生成が必要になり、インタラクティブ編集には不向きなため(静的表示前提の方式)。セグメント間の縦ギャップは太さ 2px の矩形を上下に伸ばして繋ぐ。

区間評価関数:

```csharp
        /// <summary>キー区間 [a, b] 内のフレーム位置 f における実値を返す。
        /// 再生経路 (MoveTimelineLayer.ApplyMotionUpdateTangent) と同じ単位系:
        /// t0/t1 は秒、t は 0〜1 正規化、タンジェントは値/秒</summary>
        private static float EvaluateSegment(
            int frameA, MTEP.ValueData a,
            int frameB, MTEP.ValueData b,
            MTEP.BoneData boneB,   // easing はキー B (区間終端) 側が持つ
            float f)
        {
            var dtFrames = frameB - frameA;
            if (dtFrames <= 0) return a.value;
            var t = (f - frameA) / dtFrames;

            if (boneB.transform.hasEasing)
            {
                // Phase A 時点: easing レイヤーは easing 関数で描画
                var e = MTEP.EasingFunctions.MoveEasing(t, (MTEP.MoveEasingType)boneB.transform.easing);
                return a.value + (b.value - a.value) * e;
            }

            // 再生と同一形状にするため t0/t1 は秒単位で渡す (単位系の原則を参照)
            var frameDuration = timelineManager.timeline.frameDuration;
            return MTEP.PluginUtils.Hermite(
                frameA * frameDuration, frameB * frameDuration,
                a.value, b.value,
                a.outTangent.value, b.inTangent.value, t);
        }
```

(根拠: 再生経路 `MoveTimelineLayer.cs:121-136` は `t0=stFrame*frameDuration, t1=edFrame*frameDuration`(秒)+ `t=lerpFrame`(0〜1)で `HermiteVector3` を呼ぶ。`TangentData.value` は `UpdateTangent` で秒単位勾配として更新される。描画もこれと完全に同じ引数構成にしないと、カーブエディタの表示形状がゲーム内の実際の補間と食い違う)

縦軸: `CurveViewMapping.AutoFit` に「全表示チャンネルのキー値 + 各区間を 8 分割したサンプル値」を渡して生成。目盛りは上端/中央/下端の 3 ラベル(`view.DrawLabel` で値を小数 2 桁表示)。

- [ ] **Step 3: キー点マーカー描画**

各キー位置(`FrameToX(frameNo)`, `ValueToY(value)`)に 6x6px の矩形マーカー(`GUIView.texWhite`)。選択中ボーン(`selectedBones.Contains(keyBones[i])`)のキーはチャンネル色、非選択は同色 α=0.5。

- [ ] **Step 4: ビルド + 目視ログ確認**

Run: MSBuild(Global Constraints のコマンド)
Expected: ビルド成功(実機確認は次回ゲーム起動時にまとめて行う — 既存フローに合わせる)

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs
git commit -m "feat(timeline): カーブビューの実値カーブ描画を実装"
```

### Task A4: 編集操作 (値ドラッグ + タンジェントハンドル)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Consumes: `CurveViewMapping.YToValue` / `ScreenSlopeToValueSlope`、`TimelineHistoryManager.instance.AddHistory`、`currentLayer.ApplyCurrentFrame(true)`

- [ ] **Step 1: キー点の値ドラッグ**

- `EventType.MouseDown` でキーマーカー 8px 以内をヒットテスト → 対象 (channel, keyIndex) を `_dragTarget` に記録、開始値を保持
- ドラッグ中 (`Input.GetMouseButton(0)`): `YToValue(mouseY)` で新値を計算し `ValueData.value` に直接代入 + `currentLayer.ApplyCurrentFrame(true)` でプレビュー
- `MouseUp`: 値が変わっていれば `TimelineHistoryManager.instance.AddHistory(timelineManager.timeline, "カーブ: 値変更")` を呼んで確定

ドラッグ排他: `_dragTarget != null` の間はトグルバーのリサイズ処理とスクロールビューのドラッグを発火させない(`Event.current.Use()` でイベントを消費)。

- [ ] **Step 2: タンジェントハンドル描画**

選択キー(`selectedBones` に含まれる `BoneData` のキー)のみ、in/out ハンドルを表示:

- ハンドル長 `HANDLE_LEN = 30f` px。out ハンドルはキーから右方向、in ハンドルは左方向
- `tangent.value` は **値/秒** なので、フレームあたり勾配へ `slopePerFrame = tangent.value * timeline.frameDuration` で換算してから画面勾配へ: `dyPx = -slopePerFrame * pxPerValue, dxPx = frameWidth`。正規化して長さ HANDLE_LEN の線分 + 先端 6x6px マーカー
- 線分は 2px セグメントの連続描画(カーブと同じ方式)
- hasEasing の transform にはハンドルを出さない(Phase A 暫定)

- [ ] **Step 3: ハンドルドラッグで normalizedValue を更新**

- 先端マーカーのヒットテスト → ドラッグ中はマウス位置とキー点の差分から `ScreenSlopeToValueSlope(dxPx, dyPx)` で「値/フレーム」勾配を計算
- 正規化: `normalized = valueSlope / baseSlope`。baseSlope は `UpdateTangent` と同じ定義(inTangent は流入区間の線形勾配、outTangent は流出区間)。valueSlope と baseSlope を**同じ単位(両方フレームあたり、または両方秒あたり)で揃えて割る**こと — 比は無次元なので単位が揃っていれば結果は一致する。baseSlope が 0 の場合は更新しない
- 代入時は `tangent.normalizedValue = normalized; tangent.isSmooth = false;`(KeyFrameInspector の適用コードと同じ)して `currentLayer.ApplyCurrentFrame(true)`
- `MouseUp` で `AddHistory(timeline, "カーブ: タンジェント変更")`

- [ ] **Step 4: ビルド確認 + コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs
git commit -m "feat(timeline): カーブビューの値ドラッグとタンジェントハンドル編集を実装"
```

### Task A5: ツールバー移植 + Inspector から曲線 UI を削除

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`(ツールバー)
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`(削除)

**Interfaces:**
- Consumes: `MTEP.TangentPair.GetDefault(TangentType)`、`GUIComboBox<T>`(TimelineWindow と同じ ProcessFocus 流儀 — TimelineCurveEditor は TimelineWindow の view 上に描くため追加対応不要)

- [ ] **Step 1: ツールバーを DrawToggleBar の右側に実装**

トグルバー内(開いている時のみ)に横並びで:
1. 種別フィルタ: `GUIComboBox<MTEP.TangentValueType>`(KeyFrameInspector の `_tangentValueTypeComboBox` と同構成、単チャンネル型 + 複合型 + すべて)
2. プリセット 4 ボタン: `EaseInOut` / `EaseIn` / `EaseOut` / `線形`(テクスチャではなくテキストボタンで簡素化)。押下時は KeyFrameInspector の `プリセット反映` と同じ適用コード(選択ボーンの out/in Tangent へ `TangentPair.GetDefault` を書き込み `ApplyCurrentFrame(true)` + `AddHistory`)。適用対象の out 側は `currentLayer.GetPrevBones(selectedBones)` を使う(KeyFrameInspector.cs:712-734 の foreachOutTangent/foreachInTangent を移植)
3. 自動補間トグル: `view.DrawToggle("自動補間", ...)`(同じく移植)
4. Easing コンボ(Phase A 暫定): 選択ボーンに `hasEasing` がある場合のみ表示。KeyFrameInspector の `easingComboBox` + `updateEasing` を移植

- [ ] **Step 2: KeyFrameInspector から曲線 UI を削除**

削除対象:
- フィールド: `tangentTex`, `tangentValueType`, `cachedTangents`, `tangentPresetTextures`, `easingTex`, `cachedEasings`, `easingComboBox`, `_tangentValueTypeComboBox`
- メソッド: `InitTextures`, `DrawTangent`, `DrawEasing`, `UpdateTangentTexture`, `UpdateEasingTexture`
- `Draw()` 内の `InitTextures()` 呼び出しと `DrawTangent(view); DrawEasing(view);` と直前の `DrawHorizontalLine`

残すもの: `DrawTransform` / `DrawCustomValues` / `DrawStrValues` / 初期化ボタン / `ShouldDraw`。

- [ ] **Step 3: ビルド + 全テスト**

Run: MSBuild + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、76 件 PASS(既存 70 + A1 の 6)

- [ ] **Step 4: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "feat(timeline): カーブツールバーを移植し Inspector の曲線 UI を削除"
```

---

## Phase B — Easing 廃止・Tangent 統一

### Task B1: easing→Tangent 近似変換ロジック

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/EasingToTangent.cs`
- Modify: csproj
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/EasingToTangentTests.cs`

**Interfaces:**
- Produces: `static class EasingToTangent` — `TangentPair Convert(MoveEasingType easing)`(out=区間開始側 outTangent、in=区間終端側 inTangent の normalizedValue)

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class EasingToTangentTests
    {
        [Fact]
        public void Linear変換_線形タンジェント()
        {
            var pair = EasingToTangent.Convert(MoveEasingType.Linear);
            Assert.Equal(1f, pair.outTangent, 2);
            Assert.Equal(1f, pair.inTangent, 2);
        }

        [Fact]
        public void QuadIn変換_始端フラット()
        {
            // f(t)=t^2 → f'(0)=0, f'(1)=2
            var pair = EasingToTangent.Convert(MoveEasingType.QuadIn);
            Assert.Equal(0f, pair.outTangent, 1);
            Assert.Equal(2f, pair.inTangent, 1);
        }

        [Theory]
        [InlineData(MoveEasingType.SineInOut)]
        [InlineData(MoveEasingType.CubicInOut)]
        [InlineData(MoveEasingType.QuadOut)]
        public void 単調Easingの近似誤差が閾値以下(MoveEasingType easing)
        {
            var pair = EasingToTangent.Convert(easing);
            // 変換後 Hermite と元 easing の最大誤差を全域サンプリングで検証
            float maxError = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float expected = EasingFunctions.MoveEasing(t, easing);
                float actual = MTEPluginUtils.HermiteSimplified(pair.outTangent, pair.inTangent, t);
                maxError = System.Math.Max(maxError, System.Math.Abs(expected - actual));
            }
            // In/Out 系 (二次〜三次) は端点微分一致の Hermite で 5% 以内に収まる
            Assert.True(maxError < 0.05f, $"maxError={maxError}");
        }

        [Fact]
        public void 全Easing種で変換が発散しない()
        {
            for (int i = 0; i < (int)MoveEasingType.Max; i++)
            {
                var pair = EasingToTangent.Convert((MoveEasingType)i);
                Assert.False(float.IsNaN(pair.outTangent) || float.IsInfinity(pair.outTangent));
                Assert.False(float.IsNaN(pair.inTangent) || float.IsInfinity(pair.inTangent));
                Assert.InRange(pair.outTangent, 0f, 10f);
                Assert.InRange(pair.inTangent, 0f, 10f);
            }
        }
    }
}
```

(注: テスト側の `MTEPluginUtils` はファイル冒頭に `using MTEPluginUtils = COM3D2.MotionTimelineEditor.Plugin.PluginUtils;` のエイリアスを置く — Global Constraints 参照)

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter EasingToTangentTests`
Expected: コンパイルエラー(EasingToTangent 未定義)

- [ ] **Step 3: 実装 (数値微分ベース)**

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// Easing 関数を正規化 Tangent ペアへ近似変換する。
    /// 端点微分を採用するため Quint/Exp 系など急峻なカーブは中腹で誤差が出るが、
    /// 全 MoveEasingType が単調のため形状の破綻はない (仕様で許容済み)
    /// </summary>
    public static class EasingToTangent
    {
        private const float EPSILON = 1e-3f;
        private const float MAX_TANGENT = 10f;  // Exp 系の端点微分の暴れを抑えるクランプ

        public static TangentPair Convert(MoveEasingType easing)
        {
            // 端点微分の数値近似: f'(0) ≒ f(ε)/ε, f'(1) ≒ (1 - f(1-ε))/ε
            float d0 = EasingFunctions.MoveEasing(EPSILON, easing) / EPSILON;
            float d1 = (1f - EasingFunctions.MoveEasing(1f - EPSILON, easing)) / EPSILON;

            return new TangentPair
            {
                outTangent = Mathf.Clamp(d0, 0f, MAX_TANGENT),
                inTangent = Mathf.Clamp(d1, 0f, MAX_TANGENT),
                isSmooth = false,
            };
        }
    }
}
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter EasingToTangentTests`
Expected: 全件 PASS(誤差閾値 5% が厳しすぎる場合は SineInOut/CubicInOut の実測値を確認して閾値を実態に合わせ、コメントに実測値を書く)

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/EasingToTangent.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/EasingToTangentTests.cs
git commit -m "feat(timeline): easing から Tangent への近似変換を追加"
```

### Task B2: ロード時変換の組み込み

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TangentUnification.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`(ロード後フック)
- Modify: csproj
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TangentUnificationTests.cs`

**Interfaces:**
- Consumes: `EasingToTangent.Convert`、`ITimelineLayer.keyFrames`、`FrameData.bones`
- Produces: `static class TangentUnification` — `void ConvertTimeline(TimelineData timeline)`(全レイヤーの easing 使用 transform を Tangent 化し easing を 0 にする)

- [ ] **Step 1: 変換ロジック実装**

`TangentUnification.ConvertTimeline`:
1. 各レイヤーの `keyFrames` をフレーム番号順に走査し、ボーン名ごとに連続キーペア (prevBone, bone) を作る。**先頭キー(prevBone が存在しない)は流入区間が無いため easing 変換対象外**(inTangent は線形 1f の明示書き込みのみ行う)。末尾キーの outTangent も同様に線形 1f を書く
2. `bone.transform.hasEasing && bone.transform.easing != 0` の区間について `EasingToTangent.Convert` を呼び、`prevBone.transform` の全 `values[i].outTangent` へ `outTangent`、`bone.transform` の全 `values[i].inTangent` へ `inTangent` を書き込む(`normalizedValue` 設定 + `isSmooth = false`)
3. 書き込み後 `bone.transform.easing = 0`
4. easing が 0 (Linear) のキーは Tangent 初期値が normalizedValue=0 のままだと**フラット補間になってしまう**ため、`hasEasing` だった transform は easing=0 でも全 values の in/out `normalizedValue = 1f`(線形)を明示的に書き込むこと(shouldSerialize が「!isSmooth && normalizedValue != 0」なので保存もされる)

(実装時の確認: `hasEasing` 判定は Phase B3 で削除されるため、この変換は「XML から読んだ easing フィールドが意味を持っていた transform」を対象にする。B3 適用後は `hasEasing` の代わりに旧 easing 保持の有無で判定できるよう、B3 と同一 PR 内で `wasEasingTransform` 相当の判定(easingValue が values に含まれる型かどうか)を `TransformDataBase` の `easingValue` override 有無で判別する — `easingValue` は `TransformDataBase.cs:167` で virtual 定義)

- [ ] **Step 2: TimelineManager のロード経路にフック**

`TimelineManager.cs` の `_timeline.FromXml(xml)` 直後(line 389 と 445 の 2 箇所)に:

```csharp
                // 旧フォーマットの easing 補間を Tangent へ近似変換 (Tangent 統一)
                TangentUnification.ConvertTimeline(_timeline);
```

(注: レイヤーの keyFrames が FromXml 時点で構築されているかを確認。レイヤー構築が `mte.OnLoad()` 側なら、フック位置を OnLoad 後へずらす)

- [ ] **Step 3: テストを書いて確認**

`TangentUnificationTests.cs`: TimelineData をテスト内で組み立て(既存の `TimelineSettingXmlTests` 等のフィクスチャ構築流儀を参照)、easing=QuadIn のキーペアを変換 → outTangent.normalizedValue≒0 / inTangent.normalizedValue≒2、easing==0、Linear キーの normalizedValue==1 をアサート。

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TangentUnificationTests`
Expected: PASS

- [ ] **Step 4: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): ロード時の easing→Tangent 変換を組み込み"
```

### Task B3: hasEasing 全廃と再生経路の Hermite 化

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs` + hasEasing を override する全 TransformData(約 18 ファイル、`grep -rln "hasEasing" source/COM3D2.SceneEditor.Plugin/Timeline/TransformData`)
- Modify: easing を消費する全レイヤー(約 20 ファイル、`grep -rln "CalcEasingValue\|motion.easing" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`(isTangentXXX を常時 true 扱い)

**Interfaces:**
- Consumes: `PluginUtils.HermiteValue/HermiteValues/HermiteVector3`(既存)、B2 の変換で全データに Tangent が入っている前提

- [ ] **Step 1: TransformData の統一**

- `TransformDataBase.hasTangent` を確認し、`hasEasing` の全 override を削除、base の `hasEasing => false` のみ残す。`hasTangent` が `!hasEasing` 定義でなければ、easing だった型の `hasTangent` override を `true` に揃える(現状の定義を `grep -n "hasTangent" source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs` で確認して合わせる)
- `InitTangent`(`TransformDataBase.cs:490`)の `if (hasEasing)` ブロックは**中身を読んでから**削除する(現状は `easing = (int)config.defaultEasingType` の代入のみだが、削除により新規キーの初期補間が `config.defaultTangentPair` 側に一本化されることを確認する。旧 easing 型の新規キーで Tangent 初期値が入るよう、`if (hasTangent)` 側が常時実行される形になっていること)
- `TimelineData` の `isTangentCamera/isTangentMove` 等のプロパティは XML 互換のため残すが、setter/getter を常に true を返す形へ(`// Tangent 統一後は常に true (XML 互換のためフィールドのみ残す)`)。B2 の変換判定に必要なら「XML から読んだ生値」を変換時に参照してから上書きする順序にする

- [ ] **Step 2: レイヤー再生経路の書き換え (機械的パターン)**

対象 20 ファイルそれぞれで、easing 補間の呼び出しを Tangent Hermite に置換する。既存の二経路レイヤー(MoveTimelineLayer)が正解パターン:

Before(easing 経路の典型形):
```csharp
var t = CalcEasingValue(lerpFrame, motion.easing);
var pos = Vector3.Lerp(start.position, end.position, t);
```

After(MoveTimelineLayer.cs:124 と同形):
```csharp
var pos = PluginUtils.HermiteVector3(
    motion.stFrame, motion.edFrame,
    start.positionValues, end.positionValues,
    lerpFrame);
```

- スカラー値は `PluginUtils.HermiteValue(motion.stFrame, motion.edFrame, start.値のValueData, end.値のValueData, lerpFrame)`
- 色は各成分 ValueData の `HermiteValues` → Color 再構成
- 既に `isTangentXXX` 分岐を持つレイヤー(Move/Camera/SubCamera/Model/ModelBone/ModelShapeKey/Light)は easing 側分岐を削除して Tangent 側のみ残す
- Quaternion 補間を easing でやっているレイヤーは `PluginUtils.HermiteQuaternion`(存在を `grep -n "HermiteQuaternion" source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs` で確認)へ
- 書き換え中に `motion.easing` 参照が残らないこと: 完了後 `grep -rn "CalcEasingValue\|motion.easing" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer` が ITimelineLayer の定義(あれば)以外 0 件になること
- `ITimelineLayer.CalcEasingValue` 自体と `MotionData.easing` プロパティは EasingToTangent の変換テーブルと XML 読み込みで使うため削除しない(未使用警告が出る場合のみ internal 化)

- [ ] **Step 3: UpdateTangent の適用確認**

Tangent の実勾配 (`TangentData.value`) は `UpdateTangent` で更新される。easing だったレイヤーが `UpdateTangent` 経路(呼び出し元を `grep -rn "UpdateTangent" source/COM3D2.SceneEditor.Plugin/Timeline` で確認)を通っているかを検証し、通っていないレイヤーがあれば ApplyCurrentFrame/Setup 相当の箇所に追加する。

- [ ] **Step 4: ビルド + 全テスト**

Run: MSBuild + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全件 PASS。XML ゴールデンテスト(MteCompatibilityTests 等)が easing 値の変化で落ちる場合は「ロード時変換で easing=0 + Tangent 化される」新仕様に合わせてゴールデンを更新し、更新理由をコミットメッセージに書く

- [ ] **Step 5: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(timeline): 補間を Tangent に統一し easing 再生経路を廃止"
```

### Task B4: Easing UI の撤去と仕上げ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`(暫定 Easing コンボ削除、全キーでハンドル表示)
- Modify: easing UI が残る他ファイル(`grep -rln "MoveEasingType\|easingComboBox" source/COM3D2.SceneEditor.Plugin --include=*.cs` で棚卸し。TimelineSettingWindow の defaultEasingType 設定等)

- [ ] **Step 1: カーブエディタの暫定コードを削除**

- Task A5 で入れた Easing コンボと `EvaluateSegment` の `hasEasing` 分岐を削除(全区間 Hermite)
- ハンドル非表示条件 (`hasEasing`) を削除して全キーでハンドル操作可能に

- [ ] **Step 2: 残存 Easing UI の棚卸しと削除**

`grep -rn "MoveEasingType" source/COM3D2.SceneEditor.Plugin --include=*.cs` の結果から:
- UI 参照(設定画面の defaultEasingType 選択等)を削除
- `EasingFunctions` / `MoveEasingType` enum / `EasingToTangent` は変換テーブルとして残す
- `Config.defaultEasingType` はフィールド定義を残し(旧 config.xml 互換)、参照 UI のみ削除

- [ ] **Step 3: ビルド + 全テスト + 最終確認**

Run: MSBuild + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS。`grep -rn "hasEasing" source/COM3D2.SceneEditor.Plugin --include=*.cs` が TransformDataBase の定義(常時 false)と変換関連のみになること

- [ ] **Step 4: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "feat(timeline): Easing UI を撤去し Tangent 統一を完了"
```

### Task 最終: 実機確認チェックリスト (次回ゲーム起動時)

コード完了後、ゲーム起動時に確認する項目(このタスクはコミット不要、確認のみ):

- [ ] カーブペインの開閉・高さリサイズ・再起動後の状態復元
- [ ] 選択キーのカーブ表示がドープシートの横スクロールと同期する
- [ ] キー点ドラッグで値が変わり、Undo で戻る
- [ ] ハンドルドラッグで補間形状が変わり、再生に反映される
- [ ] MTE 産 XML (easing 使用) をロードして補間形状が概ね維持される
- [ ] Inspector に曲線 UI が出ない・数値編集は生きている
- [ ] カーブ表示形状と実際の再生結果が一致する (Move レイヤーで急な Tangent を付けて目視比較)

## レビュー却下メモ

- Config.dirty の保存タイミング未確認 — 検証済みで解消 (ConfigManager.cs:43 が MouseUp 時のみ保存、ドラッグ中 IO なし)。計画に検証結果を反映済み
- 既存 CurveEditorWindow / Texture2D 方式との整合性検討がない — セグメント描画採用の理由 (インタラクティブ編集で毎フレーム Texture2D 再生成は不向き) を計画に明記して対応。CurveEditorWindow 自体は AnimationCurve ベースで Tangent データモデルと別物のため流用しない
