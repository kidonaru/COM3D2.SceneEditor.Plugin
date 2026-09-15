# メイド重力タイムラインレイヤー実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 髪・スカートの重力（`MaidGravityController` が保持する有効フラグ + オフセット）をタイムラインのキーとして記録・再生できる `GravityTimelineLayer` を追加し、編集 UI は既存の重力ウィンドウ（`MaidGravityWindow`）と、その行描画を共有する Inspector で提供する。

**Architecture:** メイド系レイヤーの既存 4 点セット（レイヤー本体 / TransformData / `TimelineIntegration` 登録 / ItemInspector）に倣う。値の読み書きは `MaidManipulateManager.instance.gravityController` に集約し、レイヤーは補間結果を `SetOffset` / `SetEnabled` で流し込むだけにする。重力ウィンドウの `DrawCategory` 本体を `GravityRowDrawer` へ切り出し、ウィンドウと `GravityItemInspector` の両方から使う。重力ウィンドウには `TimelineLayerGate` を挿入し、タイムライン読込中にレイヤー未登録なら無効表示＋追加ボタンにする。

**Tech Stack:** C# (.NET 3.5 / 4.7.1 の 2 構成)、Unity IMGUI (`GUIView`)、xunit (net48)

**Spec:** 本計画冒頭の「要件」節（ブレインストーミングでユーザー確認済み）

## 要件（ユーザー確認済み）

- レイヤー名は「メイド重力」、メイド単位（`hasSlotNo = true`）、カテゴリは `TimelineLayerCategory.Maid`
- メニュー項目は `MaidGravityController.categories` と同じ 2 件（`hair`=髪 / `skirt`=スカート）
- キーの値は「有効 (bool)」+「オフセット XYZ (-1〜1)」。XYZ は Tangent 補間、有効フラグは区間開始時に step 適用
- レイヤー未使用（全キーが既定値: 無効・zero）のメイドには再生時に何も書き込まない。`GravitySnapshot` / `ScenePresetManager.ApplyGravity` と同じく、重力コンポーネントの無駄な生成を避ける
- ポーズ編集中はレイヤーから適用しない（移動・瞳レイヤーと同じ）
- 編集 UI は既存の重力ウィンドウを使う。Inspector も必要（重力ウィンドウと同じ行を出す）
- XML は SceneEditor 独自レイヤー（MTE 非互換で可。CLAUDE.md の一方向互換方針どおり）

## Global Constraints

- コメント・ログ文言は日本語
- COM3D2 (.NET 3.5) と COM3D25 (.NET 4.7.1) の両構成でビルドを通す
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild 直叩き
- `COM3D2.SceneEditor.Plugin.csproj` は `Compile Include` を明示列挙しているため、新規ファイルは必ず追加する
- レイヤーからの値適用では履歴 (`HistoryManager.BeforeEdit`) を記録しない。記録するのはユーザー操作（ウィンドウ / Inspector のスライダー）だけ
- `TimelineLayerGate.Begin` は内部タブ描画の後に呼ぶ（タブがゲートで無効化されると抜けられなくなる）。`End` は `MaidWindowBase.DrawContent` の finally で保証済み

## ビルド・テストコマンド（Git Bash）

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" /v:m /nologo \
&& "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test --nologo -v q
```

順番は必ず COM3D2 → COM3D25 → test（COM3D2 ビルドが COM3D25 の DLL を消すため）。テストは COM3D25 構成の DLL を参照するので、テスト前に必ず両構成をビルドする。

## ファイル構成

| ファイル | 役割 |
|---|---|
| Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataGravity.cs` | キー 1 件の値（Enabled / X / Y / Z） |
| Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs` | `TransformType.Gravity` 追加 |
| Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/GravityTimelineLayer.cs` | レイヤー本体（キー化・補間・適用） |
| Create: `source/COM3D2.SceneEditor.Plugin/GravityRowDrawer.cs` | 有効トグル・リセット・XYZ スライダーの行描画（ウィンドウと Inspector で共有） |
| Modify: `source/COM3D2.SceneEditor.Plugin/MaidGravityWindow.cs` | `DrawCategory` を `GravityRowDrawer` へ委譲、`TimelineLayerGate` 挿入 |
| Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/GravityItemInspector.cs` | メニュー項目 → 重力カテゴリの行描画 |
| Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` | レイヤー / TransformData / Inspector の登録 |
| Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規 4 ファイルの `Compile Include` |
| Modify: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs` | 期待表に `GravityTimelineLayer` 追加 |
| Create: `source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs` | TransformData の値配置と Inspector のカテゴリ解決のテスト |

---

### Task 1: TransformDataGravity と TransformType.Gravity

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataGravity.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs` (enum `TransformType`、`Grounding` の次)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs`

**Interfaces:**
- Produces: `TransformDataGravity : TransformDataBase` — `type == TransformType.Gravity`、`valueCount == 4`、`enabledValue` / `offsetValues` (ValueData[3])、`bool enabled` / `Vector3 offset` / `bool isDefault` プロパティ。カスタム値キー `"enabled"` / `"x"` / `"y"` / `"z"`

- [ ] **Step 1: enum に `Gravity` を追加**

`ITransformData.cs` の `TransformType` で `Grounding,` の直後に追加:

```csharp
        Grounding,
        Gravity,
        GTToneMap,
```

- [ ] **Step 2: TransformDataGravity を作成**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 重力キー 1 件分。MaidGravityController のカテゴリ (髪 / スカート) ごとに
    /// 有効フラグとオフセット (-1〜1) を持つ。
    /// 有効フラグは補間せず区間開始時に適用し、オフセットは Tangent 補間する
    /// </summary>
    public class TransformDataGravity : TransformDataBase
    {
        public enum Index
        {
            Enabled = 0,
            X = 1,
            Y = 2,
            Z = 3,
        }

        public override TransformType type => TransformType.Gravity;

        public override int valueCount => 4;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => offsetValues;

        public TransformDataGravity()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "enabled",
                new CustomValueInfo
                {
                    index = (int)Index.Enabled,
                    name = "有効",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
            {
                "x",
                new CustomValueInfo
                {
                    index = (int)Index.X,
                    name = "X",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "y",
                new CustomValueInfo
                {
                    index = (int)Index.Y,
                    name = "Y",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "z",
                new CustomValueInfo
                {
                    index = (int)Index.Z,
                    name = "Z",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData enabledValue => values[(int)Index.Enabled];

        public ValueData[] offsetValues
        {
            get => new ValueData[]
            {
                values[(int)Index.X],
                values[(int)Index.Y],
                values[(int)Index.Z],
            };
        }

        public bool enabled
        {
            get => enabledValue.boolValue;
            set => enabledValue.boolValue = value;
        }

        public Vector3 offset
        {
            get => new Vector3(
                values[(int)Index.X].value,
                values[(int)Index.Y].value,
                values[(int)Index.Z].value);
            set
            {
                values[(int)Index.X].value = value.x;
                values[(int)Index.Y].value = value.y;
                values[(int)Index.Z].value = value.z;
            }
        }

        /// <summary>既定値 (無効・オフセット zero) か。適用を省く判定に使う</summary>
        public bool isDefault => !enabled && offset == Vector3.zero;
    }
}
```

- [ ] **Step 3: csproj に追加**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\TransformData\TransformDataGrounding.cs" />` の直後に:

```xml
    <Compile Include="Timeline\TransformData\TransformDataGravity.cs" />
```

- [ ] **Step 4: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs` を新規作成:

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class GravityTimelineLayerTests
    {
        private static MTEP.TransformDataGravity CreateTransform()
        {
            var trans = new MTEP.TransformDataGravity();
            trans.Initialize("hair");
            return trans;
        }

        [Fact]
        public void TransformDataGravity_は4値で有効フラグはBool型として扱われる()
        {
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.Gravity, trans.type);
            Assert.Equal(4, trans.valueCount);

            var infoMap = trans.GetCustomValueInfoMap();
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["enabled"].type);
            Assert.Equal(-1f, infoMap["x"].min);
            Assert.Equal(1f, infoMap["x"].max);
        }

        [Fact]
        public void TransformDataGravity_のoffsetとenabledは値配列へ書き戻される()
        {
            var trans = CreateTransform();
            trans.enabled = true;
            trans.offset = new Vector3(0.5f, -0.25f, 1f);

            Assert.True(trans.enabledValue.boolValue);
            Assert.Equal(0.5f, trans.offsetValues[0].value);
            Assert.Equal(-0.25f, trans.offsetValues[1].value);
            Assert.Equal(1f, trans.offsetValues[2].value);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Fact]
        public void TransformDataGravity_のisDefaultは無効かつzeroのときだけtrueになる()
        {
            var trans = CreateTransform();
            Assert.True(trans.isDefault);

            trans.enabled = true;
            Assert.False(trans.isDefault);

            trans.enabled = false;
            trans.offset = new Vector3(0f, 0.1f, 0f);
            Assert.False(trans.isDefault);
        }
    }
}
```

- [ ] **Step 5: 両構成をビルドしてテストを実行**

上記「ビルド・テストコマンド」を実行。Expected: ビルド成功、`GravityTimelineLayerTests` の 3 件が PASS。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataGravity.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs
git commit -m "feat(timeline): 重力キーの値を持つ TransformDataGravity を追加する"
```

---

### Task 2: GravityTimelineLayer 本体と登録

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/GravityTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` (`RegisterLayer` 群の `MoveTimelineLayer` の直後、`RegisterTransform` 群の `Grounding` の直後)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs`

**Interfaces:**
- Consumes: `TransformDataGravity` (Task 1)、`MaidGravityController.categories` / `FindCategory(string)` / `GetEnabled` / `GetOffset` / `SetEnabled` / `SetOffset`
- Produces: `GravityTimelineLayer : TimelineLayerBase`（`Create(int slotNo)`、メニュー項目名 = `GravityCategory.id`）

- [ ] **Step 1: 期待表テストを更新**

`TimelineLayerCategoryTests.cs` の `EXPECTED` に `MoveTimelineLayer` の行の直後を追加:

```csharp
            { "GravityTimelineLayer", TimelineLayerCategory.Maid },
```

- [ ] **Step 2: テストを実行して失敗を確認**

`dotnet test --nologo -v q`（DLL は Task 1 時点のもの）。Expected: `TimelineLayerCategoryTests` が「GravityTimelineLayer が見つからない」旨で FAIL。

- [ ] **Step 3: レイヤーを作成**

```csharp
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 髪・スカートの重力 (MaidGravityController) をキー化するレイヤー。
    /// 項目名は GravityCategory.id ("hair" / "skirt") で、履歴・プリセットと同じキーを使う。
    /// 有効フラグは区間開始時に適用し、オフセットは Tangent 補間する
    /// </summary>
    [TimelineLayerDesc("メイド重力", 18, TimelineLayerCategory.Maid)]
    public class GravityTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(GravityTimelineLayer);
        public override string layerName => nameof(GravityTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var category in MaidGravityController.categories)
                    {
                        _allBoneNames.Add(category.id);
                    }
                }
                return _allBoneNames;
            }
        }

        private static MaidGravityController gravityController
            => MaidManipulateManager.instance.gravityController;

        private GravityTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static GravityTimelineLayer Create(int slotNo)
        {
            return new GravityTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var category in MaidGravityController.categories)
            {
                _allMenuItems.Add(new BoneMenuItem(category.id, category.name));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var category = MaidGravityController.FindCategory(motion.name);
            if (category == null)
            {
                return;
            }

            var start = motion.start as TransformDataGravity;
            var end = motion.end as TransformDataGravity;

            // 重力を一度も使っていないメイドへ既定値だけを書き戻すと、
            // 何も変わらないのにコンポーネントだけが作られて常駐コストになる
            // (GravitySnapshot.Apply と同じ判定)
            if (!gravityController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            var offset = PluginUtils.HermiteVector3(
                t0, t1, start.offsetValues, end.offsetValues, t);
            gravityController.SetOffset(maid, category, offset);

            // 有効フラグは補間できないので区間の開始値をそのまま使う
            if (indexUpdated)
            {
                gravityController.SetEnabled(maid, category, start.enabled);
            }
        }

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            foreach (var category in MaidGravityController.categories)
            {
                var trans = CreateTransformData<TransformDataGravity>(category.id);
                trans.enabled = gravityController.GetEnabled(maid, category);
                trans.offset = gravityController.GetOffset(maid, category);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Gravity;
        }
    }
}
```

- [ ] **Step 4: TimelineIntegration に登録**

`RegisterLayer` 群の `MoveTimelineLayer` の直後に:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.GravityTimelineLayer), MTEP.GravityTimelineLayer.Create);
```

`RegisterTransform` 群の `Grounding` の直後に:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.Gravity,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataGravity>);
```

- [ ] **Step 5: csproj に追加**

`<Compile Include="Timeline\TimelineLayer\EyesTimelineLayer.cs" />` の直後に:

```xml
    <Compile Include="Timeline\TimelineLayer\GravityTimelineLayer.cs" />
```

- [ ] **Step 6: 両構成をビルドしてテストを実行**

Expected: ビルド成功、`TimelineLayerCategoryTests` を含む全テスト PASS。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/GravityTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs
git commit -m "feat(timeline): 髪・スカートの重力をキー化する GravityTimelineLayer を追加する"
```

---

### Task 3: GravityRowDrawer の切り出しと重力ウィンドウのゲート

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/GravityRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidGravityWindow.cs` (`DrawMaidContent` / `DrawCategory` / `DrawAxisSlider` / `RecordEdit`)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `GravityRowDrawer.Draw(GUIView view, Maid target, GravityCategory category, float rowHeight)` — 静的。揺れものが無ければ案内ラベルだけ描く

- [ ] **Step 1: GravityRowDrawer を作成**

現在の `MaidGravityWindow.DrawCategory` / `DrawAxisSlider` / `RecordEdit` をそのまま移す:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力カテゴリ 1 件分の行描画 (有効トグル・リセット・XYZ スライダー)。
    /// 重力ウィンドウと TimelineItemInspector (重力レイヤーの項目表示) で共有する。
    /// 値の読み書きは MaidGravityController を通し、操作は履歴に記録する
    /// </summary>
    public static class GravityRowDrawer
    {
        private static MaidGravityController gravityController
            => MaidManipulateManager.instance.gravityController;

        public static void Draw(GUIView view, Maid target, GravityCategory category, float rowHeight)
        {
            if (!gravityController.IsValid(target, category))
            {
                // 着ていない・揺れものを持たない衣装では力の掛け先が無い
                view.DrawLabel("対象の揺れものがありません", -1, rowHeight);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawToggle("有効", gravityController.GetEnabled(target, category),
                    80, rowHeight, true,
                    value =>
                    {
                        RecordEdit(target, category, "有効");
                        gravityController.SetEnabled(target, category, value);
                    });

                if (view.DrawButton("リセット", 80, rowHeight))
                {
                    RecordEdit(target, category, "リセット");
                    gravityController.SetOffset(target, category, Vector3.zero);
                }
            }
            view.EndLayout();

            var offset = gravityController.GetOffset(target, category);
            DrawAxisSlider(view, target, category, "X", offset.x,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.x = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(view, target, category, "Y", offset.y,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.y = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(view, target, category, "Z", offset.z,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.z = value;
                    gravityController.SetOffset(target, category, current);
                });
        }

        /// <summary>共通書式のスライダー 1 行 (LightWindow と同形式)</summary>
        private static void DrawAxisSlider(
            GUIView view, Maid target, GravityCategory category, string label, float value,
            Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = 20,
                width = -1,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = newValue =>
                {
                    RecordEdit(target, category, label);
                    onChanged(newValue);
                },
            });
        }

        /// <summary>重力操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid target, GravityCategory category, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Gravity,
                "重力: " + category.name + " " + label);
        }
    }
}
```

- [ ] **Step 2: MaidGravityWindow を委譲に置き換え、ゲートを挿入**

`MaidGravityWindow.cs` から `DrawCategory` / `DrawAxisSlider` / `RecordEdit` の 3 メソッドを削除し、`DrawMaidContent` を次のように書き換える（`using MTEP = COM3D2.MotionTimelineEditor.Plugin;` をファイル先頭に追加）:

```csharp
        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }
            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // タブ切替はゲートの対象外にするため、タブを描いた後で判定する
            TimelineLayerGate.Begin(view, typeof(MTEP.GravityTimelineLayer), target, ROW_HEIGHT);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            GravityRowDrawer.Draw(view, target, MaidGravityController.categories[(int)_tabType], ROW_HEIGHT);

            view.EndScrollView();
        }
```

不要になった `using UnityEngine;` は `Color.gray` で使うので残す。

- [ ] **Step 3: csproj に追加**

`<Compile Include="EyesPosRowDrawer.cs" />` の直後に:

```xml
    <Compile Include="GravityRowDrawer.cs" />
```

- [ ] **Step 4: 両構成をビルドしてテストを実行**

Expected: ビルド成功、全テスト PASS（このタスクは振る舞いを変えない切り出し）。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/GravityRowDrawer.cs \
        source/COM3D2.SceneEditor.Plugin/MaidGravityWindow.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(gravity): 重力ウィンドウの行描画を GravityRowDrawer へ切り出しレイヤーゲートを付ける"
```

---

### Task 4: GravityItemInspector

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/GravityItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` (`TimelineItemInspectorRegistry.Register` 群の `MoveTimelineLayer` の直後)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs`

**Interfaces:**
- Consumes: `GravityRowDrawer.Draw` (Task 3)、`MaidGravityController.FindCategory`
- Produces: `GravityItemInspector : ITimelineItemInspector`、静的 `ResolveCategory(string itemName)` (未知の項目名なら null)

- [ ] **Step 1: テストを追加**

`GravityTimelineLayerTests.cs` に追加:

```csharp
        [Fact]
        public void GravityItemInspector_は項目名からカテゴリを引き未知の名前ではnullを返す()
        {
            Assert.Equal("hair", GravityItemInspector.ResolveCategory("hair").id);
            Assert.Equal("skirt", GravityItemInspector.ResolveCategory("skirt").id);
            Assert.Null(GravityItemInspector.ResolveCategory("unknown"));
        }
```

- [ ] **Step 2: テストを実行して失敗を確認**

`dotnet test --nologo -v q`。Expected: `GravityItemInspector` が無いためテストプロジェクトのコンパイルエラーで FAIL。

- [ ] **Step 3: Inspector を作成**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力レイヤー (GravityTimelineLayer) のメニュー項目 → 重力カテゴリの編集UI。
    /// 重力ウィンドウと同じ行 (GravityRowDrawer) を出す
    /// </summary>
    public class GravityItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名 (GravityCategory.id) からカテゴリを求める。未知なら null</summary>
        public static GravityCategory ResolveCategory(string itemName)
        {
            return MaidGravityController.FindCategory(itemName);
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var item in items)
            {
                var category = ResolveCategory(item.name);
                if (category == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのカテゴリの行か分かるよう見出しを出す
                view.DrawLabel(category.name, -1, RowHeight);
                GravityRowDrawer.Draw(view, maid, category, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 重力に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
```

- [ ] **Step 4: TimelineIntegration に登録**

`TimelineItemInspectorRegistry.Register` 群の `MoveTimelineLayer` の直後に:

```csharp
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.GravityTimelineLayer), new GravityItemInspector());
```

- [ ] **Step 5: csproj に追加**

`<Compile Include="Timeline\ItemInspector\EyesItemInspector.cs" />` の直後に:

```xml
    <Compile Include="Timeline\ItemInspector\GravityItemInspector.cs" />
```

- [ ] **Step 6: 両構成をビルドしてテストを実行**

Expected: ビルド成功、`GravityTimelineLayerTests` 4 件を含む全テスト PASS。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/GravityItemInspector.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs
git commit -m "feat(timeline): 重力レイヤーの項目を重力ウィンドウと同じ行で編集する GravityItemInspector を追加する"
```

---

### Task 5: 実機確認（ゲーム起動中のみ）

**Files:** なし（検証のみ）

- [ ] **Step 1: ホットリロードで新ビルドを反映**

ゲームが起動中なら CLAUDE.md「Harmony プラグインのホットリロード」の手順で反映する。起動していなければこのタスクはスキップし、最終報告で「実機未確認」と明記する。

- [ ] **Step 2: 確認項目**

1. タイムラインを読み込み、重力ウィンドウを開くと「メイド重力」の追加ボタンが出て項目が無効化される
2. 追加ボタンでレイヤーが作られ、タイムラインウィンドウのアクティブレイヤーが「メイド重力」に切り替わる
3. 重力ウィンドウでスカートを有効にし X を変えてキー追加 → 別フレームで値を変えてキー追加 → 再生でオフセットが補間される
4. Inspector でメニュー項目「スカート」を選ぶと重力ウィンドウと同じ行が出て操作できる
5. 保存 → 再読込でキーが復元される

## レビュー却下メモ

（却下した指摘なし。plan-review の 🟡 2 件はいずれも取り込み済み: 既定値キーのみのメイドへの書き込み省略、未使用 Easing スロットの削除）
