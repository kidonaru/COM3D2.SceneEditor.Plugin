# Phase W1: タイムライン設定 UI 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: 親ワークスペースの標準フローに従い superpowers:executing-plans でタスク単位に実装する（subagent-driven-development は使わない）。ステップはチェックボックス（`- [ ]`）で追跡する。

**Goal:** MTE の `TimelineSettingUI` 相当を SE 流儀のサブウィンドウとして移植し、TimelineData / timelineConfig の設定項目を GUI から編集できるようにする。

**Architecture:** 新規 `TimelineSettingWindow`（`EditorSubWindow` 派生、`SettingWindow` と同じタブ + スクロール構成）を追加し、「個別」タブで `timeline`（TimelineData）を、「共通」タブで `timelineConfig`（Timeline/Config）を編集する。TimelineWindow のコントロールパネルに「設定」ボタンを置き、そこから表示をトグルする。データ層は既存のまま（XML 互換は変更しない）。

**Tech Stack:** C# 7.3 / .NET 3.5 相当（UnityInjector プラグイン）、IMGUI（MTEUtils の `GUIView` / `GUIComboBox`）、xUnit（net48 テストプロジェクト）

**Spec:** `docs/superpowers/specs/timeline-editing-roadmap.md`（Phase W1）

## Global Constraints

- ブランチは `feature/timeline-window` のまま。worktree は使わない
- csproj は非 SDK 形式の手動管理。新規 `.cs` は必ず `<Compile Include="..." />` を追加する
- コメント・ラベル・エラーメッセージは日本語
- ビルド確認は MSBuild を直接叩く（`debug.bat` は実機へ DLL をコピーするため使わない）:
  `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- テスト実行は `dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj`（先にプラグインを COM3D25 構成でビルドしておくこと）
- 既存の XML 形式（`TimelineXml`）は変更しない。UI 追加のみ

## スコープ判断（ロードマップからの絞り込み）

調査の結果、以下は **SE 側に適用経路が存在しない（GUI を出しても何も起きない）** ため W1 では出さない。除外理由はロードマップにも追記する（Task 6）。

| 項目 | 除外理由 |
|---|---|
| アスペクト比 / レターボックス透過度 | `aspectWidth` / `aspectHeight` / `letterBoxAlpha` を参照する描画コードが SE に存在しない（MTE では cameraManager のレターボックス描画が消費していた） |
| オフセット時間 / フェード時間 | `startOffsetTime` は DCM CSV 出力のみ、`endOffsetTime` / `startFadeTime` / `endFadeTime` は SE 内で未参照。DCM 出力はスコープ外 |
| 連番画像出力設定 / DCM 出力先を開く / 画像出力先を開く / サムネ更新 / デバッグ情報表示 | ロードマップで持ち込まないと明記 |
| Trans詳細表示数 / Tangent表示数 / 自動で BackgroundCustom に登録 / 動画先読み秒数 / 簡易設定の表示切り替え | `detailTransformCount` / `detailTangentCount` / `autoResisterBackgroundCustom` / `videoPrebufferTime` / `IsEasySettingVisible` は SE 内で未参照 |
| ウィンドウ幅 / 高さ / ボーンリスト幅（config.windowWidth 等） | ウィンドウサイズは SE の EditorSubWindow がドラッグリサイズと config 保存を担う。`menuWidth` は TimelineWindow 上のドラッグで既に調整可能 |

逆に、MTE ではレイヤー DrawWindow 側にあったが SE では受け皿が無い以下は「個別」タブに含める（レイヤー UI を接続しない方針の帰結）:
`isTangentLight` / `isTangentModel` / `isTangentModelBone` / `isTangentModelShapeKey`

## File Structure

| ファイル | 責務 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`（新規） | タイムライン設定ウィンドウ本体。タブ（個別 / 共通）と各設定行の描画 |
| `source/COM3D2.SceneEditor.Plugin/Config.cs`（変更） | `timelineSetting*` の配置・表示状態フィールド追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs`（変更） | ウィンドウ登録 |
| `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs`（変更） | Window メニューへの項目追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（変更） | コントロールパネルへ「設定」ボタン追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（変更） | 地面色連動の再適用復元（BGColorTimelineLayer は移植済み） |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（変更） | Compile Include 追加 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs`（新規） | W1 で編集する設定値の XML 往復テスト |
| `docs/superpowers/specs/timeline-editing-roadmap.md`（変更） | Phase W1 の完了記録と除外項目の明記 |

---

### Task 1: 設定値の XML 往復テストと地面色連動の再適用復元

設定 UI から編集する値がセーブ／ロードで失われないことを機械的に固定し、UI で露出させる「地面色表示を背景表示と連動」が実際に効くよう、移植済みの `BGColorTimelineLayer` への再適用を復元する。

**Files:**
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs`（新規）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:343-360`（`isBackgroundVisible` setter）

**Interfaces:**
- Consumes: `COM3D2.MotionTimelineEditor.Plugin.TimelineXml`、`TimelineManager.GetLayer<T>(int slotNo = 0)`、`BGColorTimelineLayer.ApplyCurrentFrame(bool)`
- Produces: なし（後続タスクは UI のみ）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs` を新規作成:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// Phase W1 の設定 UI が編集する項目が XML 往復で保存されることを固定する。
    /// UI から変更できても保存されない項目があると設定が黙って消えるため
    /// </summary>
    public class TimelineSettingXmlTests
    {
        private static TimelineXml RoundTrip(TimelineXml src)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                return (TimelineXml)serializer.Deserialize(ms);
            }
        }

        [Fact]
        public void 設定UIで編集する項目が往復で保持される()
        {
            var src = new TimelineXml
            {
                directoryName = "テストフォルダ",
                frameRate = 60f,
                useHeadKey = true,
                useMuneKeyL = true,
                useMuneKeyR = true,
                isLoopAnm = false,
                isGroundLinkedToBackground = true,
                singleFrameType = SingleFrameType.Advance,
                isEasingAppliedToNextKeyframe = true,
                isTangentCamera = true,
                isTangentLight = true,
                isTangentMove = true,
                isTangentModel = true,
                isTangentModelBone = true,
                isTangentModelShapeKey = true,
                usePostEffectExtraColor = true,
                usePostEffectExtraBlend = true,
            };

            var dst = RoundTrip(src);

            Assert.Equal("テストフォルダ", dst.directoryName);
            Assert.Equal(60f, dst.frameRate);
            Assert.True(dst.useHeadKey);
            Assert.True(dst.useMuneKeyL);
            Assert.True(dst.useMuneKeyR);
            Assert.False(dst.isLoopAnm);
            Assert.True(dst.isGroundLinkedToBackground);
            Assert.Equal(SingleFrameType.Advance, dst.singleFrameType);
            Assert.True(dst.isEasingAppliedToNextKeyframe);
            Assert.True(dst.isTangentCamera);
            Assert.True(dst.isTangentLight);
            Assert.True(dst.isTangentMove);
            Assert.True(dst.isTangentModel);
            Assert.True(dst.isTangentModelBone);
            Assert.True(dst.isTangentModelShapeKey);
            Assert.True(dst.usePostEffectExtraColor);
            Assert.True(dst.usePostEffectExtraBlend);
        }
    }
}
```

- [ ] **Step 2: テストを実行して通ることを確認する（既存データ層の回帰確認）**

Run:
```
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj --filter TimelineSettingXmlTests
```
Expected: PASS。ここは既存の XML 層が既に正しいことの確認であり、失敗した場合は `TimelineXml` に該当フィールドが無い / `[XmlIgnore]` が付いている等の欠落なので、`Timeline/TimelineXml.cs` を修正してから次へ進む。

- [ ] **Step 3: 地面色連動の再適用を復元する**

`Timeline/TimelineData.cs` の `isBackgroundVisible` setter を差し替える。現状:

```csharp
                _isBackgroundVisible = value;
                studioHack.SetBackgroundVisible(value);

                // BGColorTimelineLayer は未移植のため、地面色連動の再適用は行わない
```

変更後（MTE と同じ経路。`BGColorTimelineLayer` は Phase L 系で移植済み）:

```csharp
                _isBackgroundVisible = value;
                studioHack.SetBackgroundVisible(value);

                // 地面色を背景表示に連動させる設定のときだけ、地面色レイヤーを再適用する
                if (isGroundLinkedToBackground)
                {
                    var bgColorLayer = timelineManager.GetLayer<BGColorTimelineLayer>();
                    bgColorLayer?.ApplyCurrentFrame(true);
                }
```

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj
```
Expected: ビルド成功（警告のみ可）、全テスト PASS

- [ ] **Step 5: csproj にテストファイルの追加が不要なことを確認する**

テストプロジェクトは SDK 形式（ワイルドカード）なので追加不要。`source\COM3D2.SceneEditor.Plugin.Tests\COM3D2.SceneEditor.Plugin.Tests.csproj` に `<Compile Include=` が無いことを確認するだけでよい。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs
git commit -m "fix(timeline): 地面色連動の再適用を復元し設定値のXML往復テストを追加"
```

---

### Task 2: TimelineSettingWindow の骨格と登録

タブ（個別 / 共通）とスクロールだけを持つ空のウィンドウを作り、配置保存・メニュー登録・ビルドまでを通す。中身は Task 3 / 4 で埋める。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`// 設定ウィンドウ` ブロックの直後に追記）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:66`（`AddWindow(SettingWindow.instance);` の直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:132`（`CreateWindowItem("タイムライン", ...)` の直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `EditorSubWindow`（`windowId` / `windowTitle` / `LoadPlacement` / `StorePlacement` / `savedVisible` / `DrawContent`）、`GUIView`
- Produces: `TimelineSettingWindow.instance`（`EditorSubWindow`）、`TimelineSettingWindow.WINDOW_ID = 8903385`、`private void DrawSongSetting(GUIView view)` / `private void DrawCommonSetting(GUIView view)`（Task 3 / 4 が中身を埋める）

- [ ] **Step 1: Config に配置フィールドを追加する**

`Config.cs` の `settingVisible` の行の直後に追記:

```csharp
        // タイムライン設定ウィンドウ
        public int timelineSettingPosX = -1;
        public int timelineSettingPosY = -1;
        public int timelineSettingWidth = 320;
        public int timelineSettingHeight = 420;
        public bool timelineSettingVisible = false;
```

- [ ] **Step 2: ウィンドウ本体を作成する**

`source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` を新規作成:

```csharp
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインの設定ウィンドウ。
    /// 「個別」タブは読み込み中のタイムライン (TimelineData) を、
    /// 「共通」タブはプラグイン共通の設定 (timelineConfig) を編集する。
    /// TimelineWindow のコントロールパネルの「設定」ボタンから開く
    /// </summary>
    public class TimelineSettingWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903385;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン設定";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int TAB_WIDTH = 60;
        /// <summary>横並びトグルの幅。ラベルが見切れない程度に固定する</summary>
        private static readonly int TOGGLE_WIDTH = 130;

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum SettingTabType
        {
            個別,
            共通,
        }

        private SettingTabType _tabType = SettingTabType.個別;

        private readonly GUIView _view = new GUIView();

        private static MTEP.TimelineData timeline => MTEP.TimelineManager.instance.timeline;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static TimelineSettingWindow _instance = null;
        public static TimelineSettingWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineSettingWindow();
                }
                return _instance;
            }
        }

        private TimelineSettingWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineSettingPosX;
            y = config.timelineSettingPosY;
            width = config.timelineSettingWidth;
            height = config.timelineSettingHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineSettingPosX = x;
            config.timelineSettingPosY = y;
            config.timelineSettingWidth = width;
            config.timelineSettingHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineSettingVisible;
            set => config.timelineSettingVisible = value;
        }

        protected override void DrawContent()
        {
            DrawBody();

            // ボタン押下で登録されたフォーカスをポップアップへ引き渡す (TimelineWindow と同じ流儀)。
            // タイムライン未読込で早期 return しても飛ばさないよう、本体とは分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            // タブはスクロールビューの外に置き、どこまでスクロールしても切り替えられるようにする
            _tabType = _view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」になるため、
            // SettingWindow と同じく通常の行間に合わせて詰める
            _view.currentPos.y -= 5 + GUIView.defaultMargin;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            switch (_tabType)
            {
                case SettingTabType.個別:
                    DrawSongSetting(_view);
                    break;
                case SettingTabType.共通:
                    DrawCommonSetting(_view);
                    break;
            }

            _view.EndScrollView();
        }

        /// <summary>個別設定 (TimelineData) の描画。中身は Task 3 で実装する</summary>
        private void DrawSongSetting(GUIView view)
        {
        }

        /// <summary>共通設定 (timelineConfig) の描画。中身は Task 4 で実装する</summary>
        private void DrawCommonSetting(GUIView view)
        {
        }
    }
}
```

- [ ] **Step 3: WindowManager へ登録する**

`Manager/WindowManager.cs` の `AddWindow(SettingWindow.instance);` の直前に追記（タイムライン関連を隣接させる）:

```csharp
            AddWindow(TimelineSettingWindow.instance);
```

- [ ] **Step 4: メニューバーへ追加する**

`MenuBarWindow.cs` の `CreateWindowItem("タイムライン", TimelineWindow.instance),` の直後に追記:

```csharp
                        CreateWindowItem("タイムライン設定", TimelineSettingWindow.instance),
```

- [ ] **Step 5: csproj に Compile Include を追加する**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="TimelineWindow.cs" />` の直前（アルファベット順を保つ）に追記:

```xml
    <Compile Include="TimelineSettingWindow.cs" />
```

- [ ] **Step 6: ビルドして通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功。`timeline` / `timelineManager` / `timelineConfig` の参照方法が既存ウィンドウと異なりコンパイルエラーになる場合は、`TimelineWindow.cs` 冒頭の static プロパティ定義に合わせて修正する（実装の正とする）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): タイムライン設定ウィンドウの骨格を追加"
```

---

### Task 3: 個別設定タブ（TimelineData）の実装

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`（`DrawSongSetting` と、その上のコンボボックス定義）

**Interfaces:**
- Consumes: Task 2 の `DrawSongSetting(GUIView view)`、`timeline`（`MTEP.TimelineData`）、`timelineManager.ApplyCurrentFrame(bool)` / `Refresh()` / `GetLayer<T>()` / `FindLayers(Type)`、`MTEUtils.ShowConfirmDialog(string, Action, Action)`
- Produces: なし

- [ ] **Step 1: コンボボックスのフィールドを追加する**

`TimelineSettingWindow` クラス内、`_view` の定義の直後に追記:

```csharp
        private static readonly string[] SingleFrameTypeNames = new string[]
        {
            "なし",
            "1F遅らせる",
            "1F早める",
        };

        private readonly GUIComboBox<Maid.EyeMoveType> _eyeMoveTypeComboBox = new GUIComboBox<Maid.EyeMoveType>
        {
            items = System.Enum.GetValues(typeof(Maid.EyeMoveType)).Cast<Maid.EyeMoveType>().ToList(),
            getName = (type, index) => type.ToString(),
            onSelected = (type, index) =>
            {
                timeline.eyeMoveType = type;
            },
        };

        private readonly GUIComboBox<MTEP.SingleFrameType> _singleFrameTypeComboBox = new GUIComboBox<MTEP.SingleFrameType>
        {
            items = System.Enum.GetValues(typeof(MTEP.SingleFrameType)).Cast<MTEP.SingleFrameType>().ToList(),
            getName = (type, index) => SingleFrameTypeNames[index],
            onSelected = (type, index) =>
            {
                timeline.singleFrameType = type;
                timelineManager.ApplyCurrentFrame(true);
            },
        };
```

ファイル冒頭の using に以下を追加:

```csharp
using System;
using System.Linq;
```

- [ ] **Step 2: DrawSongSetting を実装する**

Task 2 で空にした `DrawSongSetting` を差し替える:

```csharp
        /// <summary>個別設定 (読み込み中のタイムラインに紐づく設定) の描画</summary>
        private void DrawSongSetting(GUIView view)
        {
            view.DrawLabel("格納ディレクトリ名", -1, ROW_HEIGHT);
            view.DrawTextField(timeline.directoryName, -1, ROW_HEIGHT, newText => timeline.directoryName = newText);

            view.BeginHorizontal();
            {
                var newFrameRate = timeline.frameRate;

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "フレームレート",
                    value = timeline.frameRate,
                    width = 150,
                    height = ROW_HEIGHT,
                    onChanged = x => newFrameRate = x,
                });

                if (view.DrawButton("30", 30, ROW_HEIGHT))
                {
                    newFrameRate = 30;
                }

                if (view.DrawButton("60", 30, ROW_HEIGHT))
                {
                    newFrameRate = 60;
                }

                if (newFrameRate != timeline.frameRate)
                {
                    timeline.frameRate = newFrameRate;
                    timelineManager.ApplyCurrentFrame(true);
                }
            }
            view.EndLayout();

            _eyeMoveTypeComboBox.currentIndex = (int)timeline.eyeMoveType;
            _eyeMoveTypeComboBox.DrawButton("メイド目線", view);

            _singleFrameTypeComboBox.currentIndex = (int)timeline.singleFrameType;
            _singleFrameTypeComboBox.DrawButton("1フレーム調整", view);

            view.DrawToggle("顔/瞳の固定化", timeline.useHeadKey, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
            {
                timeline.useHeadKey = newValue;
            });

            view.BeginHorizontal();
            {
                view.DrawToggle("胸(左)の物理無効", timeline.useMuneKeyL, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timeline.useMuneKeyL = newValue;
                });

                view.DrawToggle("胸(右)の物理無効", timeline.useMuneKeyR, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timeline.useMuneKeyR = newValue;
                });
            }
            view.EndLayout();

            view.DrawToggle("ループアニメーション", timeline.isLoopAnm, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isLoopAnm = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });

            view.DrawToggle("イージングを次のキーフレームに適用", timeline.isEasingAppliedToNextKeyframe, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isEasingAppliedToNextKeyframe = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("タンジェント補間", -1, ROW_HEIGHT);

            DrawTangentToggle(view, "カメラ", timeline.isTangentCamera,
                newValue => timeline.isTangentCamera = newValue, typeof(MTEP.CameraTimelineLayer));

            DrawTangentToggle(view, "ライト", timeline.isTangentLight,
                newValue => timeline.isTangentLight = newValue, typeof(MTEP.LightTimelineLayer));

            DrawTangentToggle(view, "メイド移動", timeline.isTangentMove,
                newValue => timeline.isTangentMove = newValue, typeof(MTEP.MoveTimelineLayer));

            DrawTangentToggle(view, "モデル", timeline.isTangentModel,
                newValue => timeline.isTangentModel = newValue, typeof(MTEP.ModelTimelineLayer));

            DrawTangentToggle(view, "モデルボーン", timeline.isTangentModelBone,
                newValue => timeline.isTangentModelBone = newValue, typeof(MTEP.ModelBoneTimelineLayer));

            DrawTangentToggle(view, "モデルシェイプ", timeline.isTangentModelShapeKey,
                newValue => timeline.isTangentModelShapeKey = newValue, typeof(MTEP.ModelShapeKeyTimelineLayer));

            view.DrawHorizontalLine(Color.gray);

            view.DrawToggle("ポストエフェクトの色拡張", timeline.usePostEffectExtraColor, -1, ROW_HEIGHT, newValue =>
            {
                timeline.usePostEffectExtraColor = newValue;
            });

            view.DrawToggle("ポストエフェクトのブレンド拡張", timeline.usePostEffectExtraBlend, -1, ROW_HEIGHT, newValue =>
            {
                timeline.usePostEffectExtraBlend = newValue;
            });

            view.DrawToggle("地面色表示を背景表示と連動", timeline.isGroundLinkedToBackground, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isGroundLinkedToBackground = newValue;
            });

            view.DrawHorizontalLine(Color.gray);

            if (view.DrawButton("個別設定を初期化", 130, ROW_HEIGHT))
            {
                MTEUtils.ShowConfirmDialog("個別設定を初期化しますか？", () =>
                {
                    timeline.ResetSettings();
                    timelineManager.Refresh();
                    timelineManager.ApplyCurrentFrame(true);
                }, null);
            }
        }

        /// <summary>
        /// タンジェント補間トグル 1 行。
        /// 切り替え時は対象レイヤーのタンジェントを作り直して現在フレームを再適用する
        /// </summary>
        private void DrawTangentToggle(
            GUIView view, string label, bool value, Action<bool> setValue, Type layerType)
        {
            view.DrawToggle(label, value, -1, ROW_HEIGHT, newValue =>
            {
                setValue(newValue);

                foreach (var targetLayer in timelineManager.FindLayers(layerType))
                {
                    targetLayer.InitTangent();
                    targetLayer.ApplyCurrentFrame(true);
                }
            });
        }
```

- [ ] **Step 3: レイヤークラス名を実装に合わせて確認する**

Run:
```
grep -n "class \(Camera\|Light\|Move\|Model\|ModelBone\|ModelShapeKey\)TimelineLayer" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/*.cs
```
Expected: 6 クラスすべてが見つかる。名前が違う場合（`LightTimelineLayer` が抽象基底で具象名が別、等）は `DrawTangentToggle` に渡す型を実装側の具象クラス名へ合わせる。`MTEUtils.ShowConfirmDialog` の第 1 引数がダイアログを閉じる責務を持つかも確認し、MTE のように `GameMain.Instance.SysDlg.Close()` が必要なら `DialogPopupWindow` の実装に合わせる（SE は独自ダイアログのため通常は不要）。

- [ ] **Step 4: ビルドして通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(timeline): 設定ウィンドウの個別設定タブを実装"
```

---

### Task 4: 共通設定タブ（timelineConfig）の実装

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`（`DrawCommonSetting` と、その上のコンボボックス定義）

**Interfaces:**
- Consumes: Task 2 の `DrawCommonSetting(GUIView view)`、`timelineConfig`（`MTEP.Config`）、`MTEP.TangentData.TangentTypeNames`
- Produces: なし

- [ ] **Step 1: コンボボックスのフィールドを追加する**

Task 3 で追加したコンボボックス定義の直後に追記:

```csharp
        private readonly GUIComboBox<MTEP.TangentType> _defaultTangentTypeComboBox = new GUIComboBox<MTEP.TangentType>
        {
            items = System.Enum.GetValues(typeof(MTEP.TangentType)).Cast<MTEP.TangentType>().ToList(),
            getName = (type, index) => MTEP.TangentData.TangentTypeNames[index],
            onSelected = (type, index) =>
            {
                timelineConfig.defaultTangentType = type;
                timelineConfig.dirty = true;
            },
        };

        private readonly GUIComboBox<MTEP.MoveEasingType> _defaultEasingTypeComboBox = new GUIComboBox<MTEP.MoveEasingType>
        {
            // Max は要素数を表す番兵で、選ぶとイージング関数の添字が範囲外になるため候補から外す
            items = System.Enum.GetValues(typeof(MTEP.MoveEasingType)).Cast<MTEP.MoveEasingType>()
                .Where(type => type != MTEP.MoveEasingType.Max).ToList(),
            getName = (type, index) => type.ToString(),
            onSelected = (type, index) =>
            {
                timelineConfig.defaultEasingType = type;
                timelineConfig.dirty = true;
            },
        };
```

- [ ] **Step 2: DrawCommonSetting を実装する**

```csharp
        /// <summary>共通設定 (タイムライン全体で共有する設定) の描画</summary>
        private void DrawCommonSetting(GUIView view)
        {
            _defaultTangentTypeComboBox.currentIndex = (int)timelineConfig.defaultTangentType;
            _defaultTangentTypeComboBox.DrawButton("初期補間曲線", view);

            _defaultEasingTypeComboBox.currentIndex = (int)timelineConfig.defaultEasingType;
            _defaultEasingTypeComboBox.DrawButton("初期イージング", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "移動範囲",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 100f,
                step = 0.1f,
                defaultValue = 5f,
                value = timelineConfig.positionRange,
                onChanged = value =>
                {
                    timelineConfig.positionRange = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "拡縮範囲",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 10f,
                step = 0.1f,
                defaultValue = 5f,
                value = timelineConfig.scaleRange,
                onChanged = value =>
                {
                    timelineConfig.scaleRange = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "ボイス最大秒数",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 30f,
                step = 0f,
                defaultValue = 20f,
                value = timelineConfig.voiceMaxLength,
                onChanged = value =>
                {
                    timelineConfig.voiceMaxLength = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "背景透過度",
                labelWidth = 100,
                width = -1,
                min = 0f,
                max = 1f,
                step = 0f,
                defaultValue = 0.5f,
                value = timelineConfig.timelineBgAlpha,
                onChanged = value =>
                {
                    timelineConfig.timelineBgAlpha = value;
                    timelineConfig.dirty = true;
                },
            });

            view.BeginHorizontal();
            {
                view.DrawToggle("自動スクロール", timelineConfig.isAutoScroll, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.isAutoScroll = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("ポーズ履歴無効", timelineConfig.disablePoseHistory, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.disablePoseHistory = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("自動揺れボーン", timelineConfig.isAutoYureBone, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.isAutoYureBone = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("常にIKを表示", timelineConfig.alwaysShowIK, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.alwaysShowIK = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("色をHSVで指定", timelineConfig.useHSVColor, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.useHSVColor = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("処理時間出力", timelineConfig.outputElapsedTime, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.outputElapsedTime = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();
        }
```

- [ ] **Step 3: 背景透過度の反映経路を確認する**

Run:
```
grep -n "timelineBgAlpha" source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
```
Expected: タイムライン背景テクスチャの生成箇所で参照されている。テクスチャがキャッシュされていて変更が反映されない場合は、`onChanged` で `TimelineWindow.instance` のテクスチャ再生成を要求する（`TimelineWindow` 側に再生成要求の public メソッドが無ければ追加する）。

- [ ] **Step 4: ビルドして通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(timeline): 設定ウィンドウの共通設定タブを実装"
```

---

### Task 5: TimelineWindow からの導線

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:589-660`（`DrawControlPanel` の 1 行目、「更新」ボタンの直後）

**Interfaces:**
- Consumes: `TimelineSettingWindow.instance`（Task 2）、`TabGroupManager.instance.MergeIfHeaderOverlaps(EditorSubWindow)`
- Produces: なし

- [ ] **Step 1: 「設定」ボタンを追加する**

`DrawControlPanel` の最初の `BeginHorizontal` ブロック内、`if (view.DrawButton("更新", 40, 20))` ブロックの直後に追記:

```csharp
                if (view.DrawButton("設定", 50, 20))
                {
                    var settingWindow = TimelineSettingWindow.instance;
                    settingWindow.isShowWnd = !settingWindow.isShowWnd;

                    if (settingWindow.isShowWnd)
                    {
                        // 表示位置のヘッダーが他ウィンドウと重なっていればそのままドッキングする
                        TabGroupManager.instance.MergeIfHeaderOverlaps(settingWindow);
                    }
                }
```

- [ ] **Step 2: メニューバーと同じ扱いになっているか確認する**

Run:
```
grep -n "MergeIfHeaderOverlaps" -B 8 source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs
```
Expected: `CreateWindowItem` の toggle と同じ処理になっている。非表示化側で追加処理（`window.Close()` 等）を行っていれば同じ処理を「設定」ボタンにも入れる。

- [ ] **Step 3: ビルドして通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): TimelineWindow から設定ウィンドウを開く導線を追加"
```

---

### Task 6: 実機確認とドキュメント更新

**Files:**
- Modify: `docs/superpowers/specs/timeline-editing-roadmap.md`

**Interfaces:**
- Consumes: Task 1〜5 の成果物
- Produces: なし

- [ ] **Step 1: 実機で設定が効くことを確認する**

ゲーム起動中であれば MCP `com3d25-devbridge` で新 DLL をホットリロードするか、ゲーム停止中に DLL を差し替えて起動し、以下を確認する（適用タイミングのリスクがロードマップ 4 章に挙がっているため、項目ごとに見る）:

- 設定ウィンドウが「設定」ボタンとメニューバーの両方から開閉でき、配置が再起動後も復元される
- フレームレート変更後、再生速度が変わる
- メイド目線 / 顔・瞳の固定化 / 胸(左右)の物理無効が即座に反映される
- ループアニメーションの ON/OFF で再生末尾の挙動が変わる
- タンジェント補間トグルの切り替えでカーブが作り直される
- 個別設定の初期化が確認ダイアログ付きで動く
- 共通設定（初期補間曲線・移動範囲・背景透過度など）の変更が保存され、再起動後も残る

確認できない項目があれば、その項目だけロードマップに未確認として残す（機能自体は削らない）。

- [ ] **Step 2: ロードマップを更新する**

`docs/superpowers/specs/timeline-editing-roadmap.md` の以下を書き換える:

1. 「1. 現状 → 未対応」表の TimelineSetting 行を、実装済み（`TimelineSettingWindow`）へ更新する
2. 「Phase W1」節に完了日（2026-08-23）と、本計画のスコープ判断で除外した項目（アスペクト比 / レターボックス、オフセット・フェード時間、連番画像出力、DCM 出力先、サムネ更新、Trans詳細表示数 / Tangent表示数、簡易設定の表示切り替え、ウィンドウサイズ系）を除外理由付きで追記する
3. MTE ではレイヤー DrawWindow にあったタンジェント補間トグル（ライト / モデル / モデルボーン / モデルシェイプ）を個別設定タブへ集約した旨を追記する
4. Step 1 で確認できなかった項目があれば「限定確認」として列挙する

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/timeline-editing-roadmap.md
git commit -m "docs(timeline): Phase W1 の完了とスコープ判断を記録"
```

---

## 実装後の必須ステップ

- `code-review` スキルでコードレビューを行い、指摘を取り込んでからユーザーへ提示する

## レビュー却下メモ

- 「設定」ボタンをコントロールパネル 1 行目に置くと狭幅時に水平方向へはみ出す懸念 — 却下（未確認のまま見送り）。同じ行の状態メッセージラベル（幅 400）は既にはみ出し得るが操作不能にはならず、タイムライン操作の近くに導線を置く価値が上回る。メニューバーからも開けるため退避経路もある
- `_view` というフィールド名が既存の命名慣習と異なる — 却下（誤検知）。`SettingWindow.cs` も `_view` を使っており慣習どおり。レビュアー自身も取り下げている
