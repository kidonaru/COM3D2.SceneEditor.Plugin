# サブカメラ管理のカメラウィンドウ移設 + テキストウィンドウ新設 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** サブカメラの管理 (台数増減 + 各台の編集) を CameraWindow の第 3 タブへ移設し、テキストの管理 (表示数増減 + 各テキストの編集) を新設の TextWindow へ移設する。

**Architecture:** LiveEffectWindow / SoundWindow で確立した「委譲ウィンドウ」パターンを踏襲する。編集 UI の実体は既存の SubCameraRowDrawer / TextRowDrawer をそのまま使い、ウィンドウ側は対象選択コンボ + 台数/表示数の増減行 + 編集モードゲーティングだけを持つ。TimelineSettingWindow の「要素数」からサブカメラ数・テキスト表示数の行を撤去する (パラフィン等は残す)。

**Tech Stack:** C# (Unity IMGUI / GUIView)、MTE 側マネージャ (MTEP.SubCameraManager / MTEP.TimelineTextManager)

**Spec:** 本計画がスペックを兼ねる (ユーザー指示: 「サブカメラの管理はカメラウィンドウに移動、テキスト管理はテキストウィンドウを作成して」)

## Global Constraints

- **両構成ビルド必須**: COM3D2 (=.NET 3.5) / COM3D25 (=.NET 4.7.1) を必ず両方 MSBuild 直叩きで確認する。`debug.bat` は使わない (実機へ DLL をコピーするため)
- .NET 3.5 制約: 入力 5 個以上の `Func<>`/`Action<>` 禁止
- コメント・ログは日本語
- MTE 側へのアクセスは `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` エイリアス経由
- サブカメラ / テキストの実体はタイムライン文脈でのみ生成・更新されるため、`timeline == null` ガードと `StudioHackManager.instance.isPoseEditing` ゲーティングを必ず入れる (SubCameraItemInspector / TextItemInspector と同じ制約)
- 履歴 (HistoryAPI) は記録しない (RowDrawer の書き込み先はスナップショット非対象。既存 RowDrawer コメント準拠)

## ビルドコマンド (全タスク共通)

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

---

### Task 1: CameraWindow に「サブカメラ」タブを追加し、設定ウィンドウからサブカメラ数を撤去する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` (DrawElementCountSection からサブカメラ数の行を削除)

**Interfaces:**
- Consumes: `SubCameraRowDrawer.Draw(GUIView, MTEP.SubCameraData, float labelWidth, float rowHeight)`、`MTEP.SubCameraManager.instance`（`.subCameras: List<SubCameraData>` / `.SetCameraCount(int)` / `MinSubCameraCount=1` / `MaxSubCameraCount=8`）、`MTEP.SubCameraData.displayName` / `.name` / `.camera`
- Produces: なし (UI のみ)

- [ ] **Step 1: CameraWindow に対象タブとフィールドを追加**

`TargetNames` に "サブカメラ" を追加:

```csharp
private static readonly string[] TargetNames = { "Main", "SceneView", "サブカメラ" };
```

クラスへフィールドを追加 (`_focusPointComboBox` 定義の後ろあたり):

```csharp
// ---- サブカメラタブ ----

private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
private static MTEP.SubCameraManager subCameraManager => MTEP.SubCameraManager.instance;

private readonly GUIComboBox<MTEP.SubCameraData> _subCameraComboBox =
    new GUIComboBox<MTEP.SubCameraData>
    {
        getName = (cameraData, _) => cameraData.displayName,
        labelWidth = 70,
        buttonSize = new Vector2(150, ROW_HEIGHT),
        contentSize = new Vector2(150, 300),
    };

// 回転オフセットのキャッシュとコンボ開閉状態をカメラごとに分けるため名前で引く
// (台数上限 8 なので減った分の掃除はしない)
private readonly Dictionary<string, SubCameraRowDrawer> _subCameraRowDrawers =
    new Dictionary<string, SubCameraRowDrawer>();
```

`using System.Collections.Generic;` は既に先頭にあるので追加不要。

- [ ] **Step 2: DrawContent の分岐とタブ本体を追加**

`DrawContent()` の分岐を書き換え:

```csharp
if (_targetIndex == 0)
{
    // プリセットは Main カメラ専用のため他タブでは行を出さない
    DrawPresetRow();
    DrawMainCameraContent();
}
else if (_targetIndex == 1)
{
    DrawSceneViewCameraContent();
}
else
{
    DrawSubCameraContent();
}
```

クラス末尾 (DrawFocusRow の後) にタブ本体を追加:

```csharp
/// <summary>
/// サブカメラの管理タブ。台数の増減と選択したカメラの編集を行う。
/// サブカメラはタイムライン文脈でのみ生成・更新されるため、
/// タイムライン未読込時は使えない (SubCameraItemInspector と同じ制約)
/// </summary>
private void DrawSubCameraContent()
{
    _view.DrawHorizontalLine(Color.gray);
    _view.AddSpace(5);

    if (timelineManager.timeline == null)
    {
        _view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT,
            textColor: Color.yellow);
        return;
    }

    _view.SetEnabled(_view.focusedComboBox == null);

    DrawSubCameraCountRow();

    var subCameras = subCameraManager.subCameras;
    if (subCameras.Count == 0)
    {
        _view.DrawLabel("サブカメラが存在しません", -1, ROW_HEIGHT);
        return;
    }

    _subCameraComboBox.items = subCameras;
    _subCameraComboBox.DrawButton("操作対象", _view);

    var cameraData = _subCameraComboBox.currentItem;
    if (cameraData == null || cameraData.camera == null)
    {
        _view.DrawLabel("サブカメラを選択してください", -1, ROW_HEIGHT);
        return;
    }

    _view.DrawHorizontalLine(Color.gray);
    _view.AddSpace(5);

    _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

    // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
    // 編集モードでないときは触らせない (レイヤー UI と同じ制約)
    if (!studioHackManager.isPoseEditing)
    {
        _view.DrawLabel("編集モード中のみサブカメラを操作できます", -1, ROW_HEIGHT,
            textColor: Color.yellow);
    }
    _view.SetEnabled(_view.focusedComboBox == null && studioHackManager.isPoseEditing);

    SubCameraRowDrawer drawer;
    if (!_subCameraRowDrawers.TryGetValue(cameraData.name, out drawer))
    {
        drawer = new SubCameraRowDrawer();
        _subCameraRowDrawers[cameraData.name] = drawer;
    }
    drawer.Draw(_view, cameraData, LABEL_WIDTH, ROW_HEIGHT);

    _view.SetEnabled(_view.focusedComboBox == null);
    _view.EndScrollView();
}

/// <summary>サブカメラ台数の増減行 (TimelineSettingWindow の要素数行から移設)</summary>
private void DrawSubCameraCountRow()
{
    var count = subCameraManager.subCameras.Count;

    _view.BeginHorizontal();
    {
        _view.margin = 0;

        _view.DrawLabel("サブカメラ数", _view.labelWidth, ROW_HEIGHT);

        _view.DrawIntField(new GUIView.IntFieldOption
        {
            value = count,
            width = _view.viewRect.width - (_view.labelWidth + 40 + _view.padding.x * 2),
            height = ROW_HEIGHT,
            onChanged = x => subCameraManager.SetCameraCount(
                Mathf.Clamp(x, MTEP.SubCameraManager.MinSubCameraCount,
                    MTEP.SubCameraManager.MaxSubCameraCount)),
        });

        if (_view.DrawButton("-", 20, ROW_HEIGHT,
            count > MTEP.SubCameraManager.MinSubCameraCount))
        {
            subCameraManager.SetCameraCount(count - 1);
        }
        if (_view.DrawButton("+", 20, ROW_HEIGHT,
            count < MTEP.SubCameraManager.MaxSubCameraCount))
        {
            subCameraManager.SetCameraCount(count + 1);
        }

        _view.margin = GUIView.defaultMargin;
    }
    _view.EndLayout();
}
```

- [ ] **Step 3: TimelineSettingWindow からサブカメラ数の行を削除**

`DrawElementCountSection` から以下を削除:

```csharp
var subCameraManager = MTEP.SubCameraManager.instance;
DrawCountRow(view, "サブカメラ数", subCameraManager.subCameras.Count,
    MTEP.SubCameraManager.MinSubCameraCount,
    MTEP.SubCameraManager.MaxSubCameraCount,
    x => subCameraManager.SetCameraCount(x));
```

同メソッドの doc コメント `/// タイムライン上の要素数 (テキスト/ポストエフェクト/サブカメラ) の増減` は Task 2 でまとめて更新するのでここでは触らない。

- [ ] **Step 4: 両構成をビルドして確認**

上記「ビルドコマンド」の 2 本を実行。Expected: 両方 Build succeeded。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/CameraWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(camera): サブカメラの管理をカメラウィンドウへ移設する"
```

---

### Task 2: TextWindow を新設し、設定ウィンドウからテキスト表示数を撤去する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TextWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` (テキスト表示数の行を削除 + doc コメント更新)
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs` (配置保存フィールド追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs` (登録)
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs` (Window メニュー項目)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (Compile 追加)

**Interfaces:**
- Consumes: `TextRowDrawer.Draw(GUIView, MTEP.FreeTextSet, float rowHeight, string boneName)`、`MTEP.TimelineTextManager.instance`（`.IsValidIndex(int)` / `.GetFreeTextSet(int)`）、`MTEP.TextTimelineLayer.TextBoneName` ("Text")、`timeline.textCount` (範囲 1〜16、TimelineSettingWindow の旧行と同値)
- Produces: `TextWindow.instance` (WINDOW_ID 8903394)、`Config` の `textPosX/textPosY/textWidth/textHeight/textVisible`

- [ ] **Step 1: Config に配置フィールドを追加**

`Config.cs` の `liveEffectVisible` 群の並びに追加 (既存ウィンドウ群と同じ命名規則):

```csharp
public int textPosX = -1;
public int textPosY = -1;
public int textWidth = 320;
public int textHeight = 600;
public bool textVisible = false;
```

- [ ] **Step 2: TextWindow.cs を作成**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// フリーテキストの管理ウィンドウ。
    /// 表示数の増減と選択したテキストの内容・スタイル・枠 Transform の編集を行う。
    /// 編集 UI の実体は TextRowDrawer (書き込み先は TimelineTextManager の FreeTextSet)。
    /// テキストはタイムライン文脈でのみ生成・更新されるため、
    /// タイムライン未読込時は使えない (TextItemInspector と同じ制約)
    /// </summary>
    public class TextWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903394;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "テキスト";

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>テキスト表示数の範囲 (TimelineSettingWindow の旧・要素数行と同値)</summary>
        private const int MinTextCount = 1;
        private const int MaxTextCount = 16;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.TimelineTextManager textManager => MTEP.TimelineTextManager.instance;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        /// <summary>操作対象のテキスト添字</summary>
        private int _textIndex = 0;

        private readonly GUIComboBox<int> _textComboBox = new GUIComboBox<int>
        {
            getName = (index, _) => "テキスト" + index,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        /// <summary>コンボ選択肢 (0〜textCount-1)。表示数変更時だけ作り直す</summary>
        private readonly List<int> _textIndexItems = new List<int>();

        // コンボ開閉状態をテキストごとに分けるため添字ベースの項目名で引く
        // (表示数上限 16 なので減った分の掃除はしない)
        private readonly Dictionary<string, TextRowDrawer> _textRowDrawers =
            new Dictionary<string, TextRowDrawer>();

        private static TextWindow _instance = null;
        public static TextWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TextWindow();
                }
                return _instance;
            }
        }

        private TextWindow()
        {
            _textComboBox.onSelected = (index, _) => _textIndex = index;
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.textPosX;
            y = config.textPosY;
            width = config.textWidth;
            height = config.textHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.textPosX = x;
            config.textPosY = y;
            config.textWidth = width;
            config.textHeight = height;
        }

        public override bool savedVisible
        {
            get => config.textVisible;
            set => config.textVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            DrawBody();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawBody()
        {
            if (timeline == null)
            {
                // テキストの実体はタイムライン文脈でのみ生成・更新される
                _view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.SetEnabled(_view.focusedComboBox == null);

            DrawTextCountRow();

            _textIndex = Mathf.Clamp(_textIndex, 0, timeline.textCount - 1);

            if (_textIndexItems.Count != timeline.textCount)
            {
                _textIndexItems.Clear();
                for (var i = 0; i < timeline.textCount; i++)
                {
                    _textIndexItems.Add(i);
                }
            }

            _textComboBox.items = _textIndexItems;
            _textComboBox.currentIndex = _textIndex;
            _textComboBox.DrawButton("操作対象", _view);

            if (!textManager.IsValidIndex(_textIndex))
            {
                // 表示数変更の反映はマネージャの更新タイミング待ちになる (レイヤー側と同じ扱い)
                _view.DrawLabel("テキストが見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.gray);
                return;
            }

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (レイヤー UI と同じ制約)
            if (!studioHackManager.isPoseEditing)
            {
                _view.DrawLabel("編集モード中のみテキストを操作できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
            }
            _view.SetEnabled(_view.focusedComboBox == null && studioHackManager.isPoseEditing);

            // 直前キーの参照と色ピッカーの同定に使うため、レイヤーの項目名と同じ名前を渡す
            var boneName = MTEP.TextTimelineLayer.TextBoneName + _textIndex;

            TextRowDrawer drawer;
            if (!_textRowDrawers.TryGetValue(boneName, out drawer))
            {
                drawer = new TextRowDrawer();
                _textRowDrawers[boneName] = drawer;
            }
            drawer.Draw(_view, textManager.GetFreeTextSet(_textIndex), ROW_HEIGHT, boneName);

            _view.SetEnabled(_view.focusedComboBox == null);
            _view.EndScrollView();
        }

        /// <summary>テキスト表示数の増減行 (TimelineSettingWindow の要素数行から移設)</summary>
        private void DrawTextCountRow()
        {
            var count = timeline.textCount;

            _view.BeginHorizontal();
            {
                _view.margin = 0;

                _view.DrawLabel("テキスト表示数", _view.labelWidth, ROW_HEIGHT);

                _view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = count,
                    width = _view.viewRect.width - (_view.labelWidth + 40 + _view.padding.x * 2),
                    height = ROW_HEIGHT,
                    onChanged = x => SetTextCount(Mathf.Clamp(x, MinTextCount, MaxTextCount)),
                });

                if (_view.DrawButton("-", 20, ROW_HEIGHT, count > MinTextCount))
                {
                    SetTextCount(count - 1);
                }
                if (_view.DrawButton("+", 20, ROW_HEIGHT, count < MaxTextCount))
                {
                    SetTextCount(count + 1);
                }

                _view.margin = GUIView.defaultMargin;
            }
            _view.EndLayout();
        }

        /// <summary>
        /// テキスト表示数を変更し、実体を即座に作り直す。
        /// 実体の再生成は通常 TextTimelineLayer.LateUpdate 頼みのため、
        /// テキストレイヤー未追加のタイムラインでも反映されるよう直接呼ぶ
        /// (InitTexts は数が一致していれば何もしない)
        /// </summary>
        private static void SetTextCount(int count)
        {
            timeline.textCount = count;
            textManager.InitTexts();
        }
    }
}
```

- [ ] **Step 3: 登録 3 箇所 + csproj**

1. `Manager/WindowManager.cs` の `Init()`: `AddWindow(LiveEffectWindow.instance);` の直後に

```csharp
AddWindow(TextWindow.instance);
```

2. `MenuBarWindow.cs` の Window メニュー: `CreateWindowItem("ライブ演出", LiveEffectWindow.instance),` の直後に

```csharp
CreateWindowItem("テキスト", TextWindow.instance),
```

3. `COM3D2.SceneEditor.Plugin.csproj`: `<Compile Include="TextRowDrawer.cs" />` の近くに

```xml
<Compile Include="TextWindow.cs" />
```

- [ ] **Step 4: TimelineSettingWindow からテキスト表示数の行を削除し、コメントを更新**

`DrawElementCountSection` から以下を削除:

```csharp
DrawCountRow(view, "テキスト表示数", timeline.textCount, 1, 16,
    x => timeline.textCount = x);
```

同メソッドの doc コメントを実態に合わせて更新:

```csharp
/// <summary>
/// タイムライン上の要素数 (ポストエフェクト系) の増減。
/// テキスト表示数はテキストウィンドウ、サブカメラ数はカメラウィンドウへ移設済み
/// </summary>
```

- [ ] **Step 5: 両構成をビルドして確認**

上記「ビルドコマンド」の 2 本を実行。Expected: 両方 Build succeeded。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TextWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(text): テキスト管理ウィンドウを新設する"
```

---

## 補足 (実装者向けメモ)

- SubCameraItemInspector / TextItemInspector (キーフレーム Inspector 側の編集 UI) は今回の対象外。そのまま残す
- `MTEP.FreeTextSet` は struct だが `text` / `rect` は参照型フィールドのため、値渡しでも編集は実体へ届く (TextItemInspector と同じ渡し方)
- 実機での動作確認 (タブ切替・台数増減・編集反映) は次回ゲーム起動時のチェックリストに積む
- 自動テストは追加しない。純粋な IMGUI ウィンドウで、先例の LiveEffectWindow / SoundWindow にもテストが無い慣行に従う

## レビュー却下メモ

- Task 1 単独コミット時に「サブカメラ数だけ移設済み」の非対称な UI 配置になる — 却下理由: 連続実装する一時状態であり、各コミットは単独でビルド可能・機能欠落なし
- TextTimelineLayer 未アクティブ時に textCount 変更が反映されない懸念 — 取り込み済み (SetTextCount で textManager.InitTexts() を直接呼ぶ形に計画を修正。InitTexts は public かつ冪等であることを実コードで確認)
