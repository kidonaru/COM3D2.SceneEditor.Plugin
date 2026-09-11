# タイムラインのレイヤー表示モード（カテゴリ / レイヤー）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインの表示/編集対象レイヤーを「カテゴリモード / レイヤーモード」の切替で決める方式へ置き換え、レイヤーごとの表示トグルと簡易表示（isEasyEdit）を撤去する。

**Architecture:** 各レイヤー型が `TimelineLayerDesc` 属性でカテゴリを自己申告し、`Config.layerViewMode` だけを新規状態として持つ。表示レイヤーの絞り込みは Unity 非依存の `TimelineLayerViewFilter` が担い、`TimelineWindow` は「操作対象 `_targetLayers` → 表示対象 `_displayLayers` → 行 `_rows`」の順で組み立てる。編集ガードは従来の `row.layer == currentLayer` のまま。

**Tech Stack:** C# (.NET 3.5 / 4.7.1 の 2 構成)、Unity IMGUI (`GUIView`)、xUnit (net48)、Node.js + @resvg/resvg-js（アイコン生成）

**Spec:** `docs/superpowers/specs/2026-09-11-timeline-layer-view-mode-design.md`

## Global Constraints

- ビルドは **COM3D2 (.NET 3.5) と COM3D25 (.NET 4.7.1) の 2 構成とも**通すこと。`debug.bat` は実機へ DLL をコピーするため使わない
- .NET 3.5 側では入力 5 個以上の `Func<>` / `Action<>` は使えない
- コードのコメントとログ文言は日本語
- csproj は手動管理（非 SDK 形式）。ファイルの追加・削除は `Compile Include` を必ず更新する
- ユニットテストは COM3D25 Debug 構成のプラグイン DLL を参照するため、テスト前に COM3D25 構成をビルドしておく
- `Config.CurrentVersion` は上げない（新フィールドは既定値で読める）
- `EasySettingType.簡易表示` の enum 値は削除しない（既存 config.xml のキー互換のため）
- コミットは `commit` スキルで行う（Conventional Commits、日本語）

共通コマンド（各タスクの「ビルド + テスト」ステップはこれを指す）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2"
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

期待: 両構成とも `エラー 0` / `Build succeeded`、`dotnet test` は失敗 0。

---

## ファイル構成

| ファイル | 責務 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs`（修正） | `TimelineLayerCategory` enum、属性のカテゴリ引数、`TimelineLayerInfo.category` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs`（修正） | `TimelineLayerCategory.ToDisplayName()` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/*.cs`（28 件、修正） | 属性へカテゴリ付与 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs`（修正） | `TimelineLayerViewMode` enum、`layerViewMode`、`isEasyEdit` 削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerViewFilter.cs`（新規） | 表示レイヤーの絞り込みとカテゴリ先頭レイヤー検索（純粋ロジック） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs`（修正） | 非表示集合の削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs`（修正）/ `EasyMenuItem.cs`（削除） | 簡易表示の撤去 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` / `Timeline/ItemInspector/TimelineItemInspector.cs` / `Timeline/TimelineIntegration.cs` / `TimelineControlWindow.cs`（修正） | 簡易表示の撤去 |
| `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs` / `assets/icons/*`（修正） | `CategoryMode` / `LayerMode` アイコン追加、`EasyEdit` 削除 |
| `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（修正） | モードボタン・選択コンボ・`_displayLayers` |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（修正） | Compile Include の増減 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs`（新規） | 全レイヤー型のカテゴリ網羅 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerViewFilterTests.cs`（新規） | フィルタの純粋ロジック |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs`（修正） | 非表示系テスト削除・シグネチャ追従 |
| `docs-site/guide/timeline.md`（修正） | モードの説明 |

---

### Task 1: レイヤーカテゴリの定義と全レイヤーへの付与

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs`（末尾、`ToName(this EasySettingType)` の直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/*.cs`（`[TimelineLayerDesc(...)]` を持つ 28 ファイル）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs`（新規）

**Interfaces:**
- Produces: `COM3D2.MotionTimelineEditor.Plugin.TimelineLayerCategory { Maid, Camera, Model, Background, Effect, Other }`
- Produces: `TimelineLayerDescAttribute(string displayName, int priority, TimelineLayerCategory category)`、`TimelineLayerDescAttribute.Category`
- Produces: `TimelineLayerInfo.category`（`readonly TimelineLayerCategory`）
- Produces: `string TimelineLayerCategory.ToDisplayName()`（拡張メソッド、`COM3D2.MotionTimelineEditor.Plugin.Extensions` 内）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerCategoryTests
    {
        // 設計書 §3.1 の表。レイヤー型を増やしたらここにも足すこと
        private static readonly Dictionary<string, TimelineLayerCategory> EXPECTED =
            new Dictionary<string, TimelineLayerCategory>
        {
            { "MotionTimelineLayer", TimelineLayerCategory.Maid },
            { "AnimationTimelineLayer", TimelineLayerCategory.Maid },
            { "MorphTimelineLayer", TimelineLayerCategory.Maid },
            { "MoveTimelineLayer", TimelineLayerCategory.Maid },
            { "EyesTimelineLayer", TimelineLayerCategory.Maid },
            { "ShapeKeyTimelineLayer", TimelineLayerCategory.Maid },
            { "VoiceTimelineLayer", TimelineLayerCategory.Maid },
            { "DressTimelineLayer", TimelineLayerCategory.Maid },
            { "UndressTimelineLayer", TimelineLayerCategory.Maid },
            { "MaidMaterialTimelineLayer", TimelineLayerCategory.Maid },
            { "CameraTimelineLayer", TimelineLayerCategory.Camera },
            { "SubCameraTimelineLayer", TimelineLayerCategory.Camera },
            { "ModelTimelineLayer", TimelineLayerCategory.Model },
            { "ModelBoneTimelineLayer", TimelineLayerCategory.Model },
            { "ModelShapeKeyTimelineLayer", TimelineLayerCategory.Model },
            { "ModelMaterialTimelineLayer", TimelineLayerCategory.Model },
            { "BGTimelineLayer", TimelineLayerCategory.Background },
            { "BGColorTimelineLayer", TimelineLayerCategory.Background },
            { "BGModelTimelineLayer", TimelineLayerCategory.Background },
            { "BGModelMaterialTimelineLayer", TimelineLayerCategory.Background },
            { "PngPlacementTimelineLayer", TimelineLayerCategory.Background },
            { "LightTimelineLayer", TimelineLayerCategory.Effect },
            { "StageLightTimelineLayer", TimelineLayerCategory.Effect },
            { "StageLaserTimelineLayer", TimelineLayerCategory.Effect },
            { "PsylliumTimelineLayer", TimelineLayerCategory.Effect },
            { "SeTimelineLayer", TimelineLayerCategory.Other },
            { "PostEffectTimelineLayer", TimelineLayerCategory.Other },
            { "TextTimelineLayer", TimelineLayerCategory.Other },
        };

        // プラグイン DLL 内の具象レイヤー型。ゲーム依存の型読み込みに失敗しても
        // 読めた分だけで検証できるよう ReflectionTypeLoadException は握る
        private static List<Type> GetConcreteLayerTypes()
        {
            var assembly = typeof(ITimelineLayer).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            return types
                .Where(t => !t.IsAbstract && typeof(ITimelineLayer).IsAssignableFrom(t))
                .ToList();
        }

        [Fact]
        public void 全レイヤー型が期待どおりのカテゴリを持つ()
        {
            var types = GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<TimelineLayerDescAttribute>();
                Assert.True(attr != null, type.Name + " に TimelineLayerDesc が無い");

                TimelineLayerCategory expected;
                Assert.True(EXPECTED.TryGetValue(type.Name, out expected),
                    type.Name + " が期待表に無い (設計書 §3.1 とテストを更新すること)");
                Assert.Equal(expected, attr.Category);
            }
        }

        [Fact]
        public void 期待表の型がすべて存在する()
        {
            var names = new HashSet<string>(GetConcreteLayerTypes().Select(t => t.Name));
            foreach (var name in EXPECTED.Keys)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void 全カテゴリに表示名がある()
        {
            foreach (TimelineLayerCategory category in Enum.GetValues(typeof(TimelineLayerCategory)))
            {
                Assert.False(string.IsNullOrEmpty(category.ToDisplayName()), category + " の表示名が空");
            }
        }
    }
}
```

- [ ] **Step 2: テストが失敗（コンパイルエラー）することを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: `TimelineLayerCategory` が見つからない旨のコンパイルエラー（CS0246）

- [ ] **Step 3: enum と属性引数を追加する**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs` を次のように変更する。

`TimelineLayerDescAttribute` の直前に enum を追加:

```csharp
    /// <summary>
    /// タイムラインのレイヤーカテゴリ。カテゴリモードの表示範囲とカテゴリコンボの並び順に使う。
    /// 並びは TimelineLayerDesc の priority 番号帯 (0〜17 / 20 番台 / 30 番台 / 40 番台 / 50 番台) に合わせる
    /// </summary>
    public enum TimelineLayerCategory
    {
        Maid,
        Camera,
        Model,
        Background,
        Effect,
        Other,
    }
```

`TimelineLayerDescAttribute` を書き換え:

```csharp
    [AttributeUsage(AttributeTargets.Class)]
    public class TimelineLayerDescAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public int Priority { get; set; }
        public TimelineLayerCategory Category { get; set; }

        public TimelineLayerDescAttribute(string displayName, int priority, TimelineLayerCategory category)
        {
            DisplayName = displayName;
            Priority = priority;
            Category = category;
        }
    }
```

`TimelineLayerInfo` にフィールドを追加し、コンストラクタで属性から読む:

```csharp
        public readonly int priority;
        public readonly TimelineLayerCategory category;
```

```csharp
            var displayNameAttr = layerType.GetCustomAttribute<TimelineLayerDescAttribute>();
            if (displayNameAttr != null)
            {
                displayName = displayNameAttr.DisplayName;
                priority = displayNameAttr.Priority;
                category = displayNameAttr.Category;
            }
```

- [ ] **Step 4: 表示名の拡張メソッドを追加する**

`source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs` の `ToName(this EasySettingType type)` の直後（同じ static クラス内）に追加:

```csharp
        public static string ToDisplayName(this TimelineLayerCategory category)
        {
            switch (category)
            {
                case TimelineLayerCategory.Maid: return "メイド";
                case TimelineLayerCategory.Camera: return "カメラ";
                case TimelineLayerCategory.Model: return "モデル";
                case TimelineLayerCategory.Background: return "背景";
                case TimelineLayerCategory.Effect: return "ライト・演出";
                case TimelineLayerCategory.Other: return "その他";
                default: return category.ToString();
            }
        }
```

- [ ] **Step 5: 28 レイヤーの属性へカテゴリを付与する**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/` 配下の各ファイルで `[TimelineLayerDesc("...", N)]` を次のとおり書き換える（第 3 引数を追加）:

| ファイル | 変更後 |
|---|---|
| `MotionTimelineLayer.cs` | `[TimelineLayerDesc("メイドアニメ", 0, TimelineLayerCategory.Maid)]` |
| `AnimationTimelineLayer.cs` | `[TimelineLayerDesc("メイドアニメブレンド", 1, TimelineLayerCategory.Maid)]` |
| `MorphTimelineLayer.cs` | `[TimelineLayerDesc("メイド表情", 10, TimelineLayerCategory.Maid)]` |
| `MoveTimelineLayer.cs` | `[TimelineLayerDesc("メイド移動", 11, TimelineLayerCategory.Maid)]` |
| `EyesTimelineLayer.cs` | `[TimelineLayerDesc("メイド瞳", 12, TimelineLayerCategory.Maid)]` |
| `ShapeKeyTimelineLayer.cs` | `[TimelineLayerDesc("メイドシェイプ", 13, TimelineLayerCategory.Maid)]` |
| `VoiceTimelineLayer.cs` | `[TimelineLayerDesc("メイドボイス", 14, TimelineLayerCategory.Maid)]` |
| `DressTimelineLayer.cs` | `[TimelineLayerDesc("メイド衣装", 15, TimelineLayerCategory.Maid)]` |
| `UndressTimelineLayer.cs` | `[TimelineLayerDesc("メイド脱衣", 16, TimelineLayerCategory.Maid)]` |
| `MaidMaterialTimelineLayer.cs` | `[TimelineLayerDesc("メイドマテリアル", 17, TimelineLayerCategory.Maid)]` |
| `CameraTimelineLayer.cs` | `[TimelineLayerDesc("カメラ", 20, TimelineLayerCategory.Camera)]` |
| `SubCameraTimelineLayer.cs` | `[TimelineLayerDesc("サブカメラ", 25, TimelineLayerCategory.Camera)]` |
| `ModelTimelineLayer.cs` | `[TimelineLayerDesc("モデル", 21, TimelineLayerCategory.Model)]` |
| `ModelBoneTimelineLayer.cs` | `[TimelineLayerDesc("モデルボーン", 22, TimelineLayerCategory.Model)]` |
| `ModelShapeKeyTimelineLayer.cs` | `[TimelineLayerDesc("モデルシェイプ", 23, TimelineLayerCategory.Model)]` |
| `ModelMaterialTimelineLayer.cs` | `[TimelineLayerDesc("モデルマテリアル", 24, TimelineLayerCategory.Model)]` |
| `BGTimelineLayer.cs` | `[TimelineLayerDesc("背景", 31, TimelineLayerCategory.Background)]` |
| `BGColorTimelineLayer.cs` | `[TimelineLayerDesc("背景色", 32, TimelineLayerCategory.Background)]` |
| `BGModelTimelineLayer.cs` | `[TimelineLayerDesc("背景モデル", 33, TimelineLayerCategory.Background)]` |
| `BGModelMaterialTimelineLayer.cs` | `[TimelineLayerDesc("背景モデルマテリアル", 34, TimelineLayerCategory.Background)]` |
| `PngPlacementTimelineLayer.cs` | `[TimelineLayerDesc("PNG配置", 35, TimelineLayerCategory.Background)]` |
| `LightTimelineLayer.cs` | `[TimelineLayerDesc("ライト", 41, TimelineLayerCategory.Effect)]` |
| `StageLightTimelineLayer.cs` | `[TimelineLayerDesc("ステージライト", 42, TimelineLayerCategory.Effect)]` |
| `StageLaserTimelineLayer.cs` | `[TimelineLayerDesc("ステージレーザー", 43, TimelineLayerCategory.Effect)]` |
| `PsylliumTimelineLayer.cs` | `[TimelineLayerDesc("サイリウム", 44, TimelineLayerCategory.Effect)]` |
| `SeTimelineLayer.cs` | `[TimelineLayerDesc("効果音", 51, TimelineLayerCategory.Other)]` |
| `PostEffectTimelineLayer.cs` | `[TimelineLayerDesc("ポストエフェクト", 52, TimelineLayerCategory.Other)]` |
| `TextTimelineLayer.cs` | `[TimelineLayerDesc("テキスト", 53, TimelineLayerCategory.Other)]` |

付け忘れの確認:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -rn "TimelineLayerDesc(" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/*.cs | grep -v "TimelineLayerCategory\." 
```
Expected: 出力なし

- [ ] **Step 6: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、`TimelineLayerCategoryTests` 3 件を含め全 PASS

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs
```
commit スキルでコミット。メッセージ案: `feat(timeline): レイヤー型にカテゴリ属性を追加する`

---

### Task 2: 表示モードとフィルタの純粋ロジック

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs`（`EasySettingType` の直前に enum、`isCameraSync` の直前にフィールド）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerViewFilter.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Timeline\TimelineLayerRowState.cs` の次行に Compile Include）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerViewFilterTests.cs`（新規）

**Interfaces:**
- Consumes: `TimelineLayerCategory`（Task 1）
- Produces: `COM3D2.MotionTimelineEditor.Plugin.TimelineLayerViewMode { Category, Layer }`
- Produces: `Config.layerViewMode`（既定 `TimelineLayerViewMode.Category`）
- Produces: `COM3D2.SceneEditor.Plugin.TimelineLayerViewFilter.Filter<TLayer>(IList<TLayer> targetLayers, TLayer currentLayer, TimelineLayerViewMode mode, Func<TLayer, TimelineLayerCategory> getCategory, List<TLayer> result)`
- Produces: `TLayer TimelineLayerViewFilter.FindFirstLayer<TLayer>(IList<TLayer> targetLayers, TimelineLayerCategory category, Func<TLayer, TimelineLayerCategory> getCategory, Func<TLayer, int> getPriority)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerViewFilterTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerViewFilterTests
    {
        // レイヤーは文字列で代用し、カテゴリと priority は表で引く
        private static readonly Dictionary<string, TimelineLayerCategory> CATEGORY =
            new Dictionary<string, TimelineLayerCategory>
        {
            { "motion", TimelineLayerCategory.Maid },
            { "eyes", TimelineLayerCategory.Maid },
            { "camera", TimelineLayerCategory.Camera },
            { "light", TimelineLayerCategory.Effect },
            { "psyllium", TimelineLayerCategory.Effect },
        };

        private static readonly Dictionary<string, int> PRIORITY = new Dictionary<string, int>
        {
            { "motion", 0 },
            { "eyes", 12 },
            { "camera", 20 },
            { "light", 41 },
            { "psyllium", 44 },
        };

        private static TimelineLayerCategory GetCategory(string layer) => CATEGORY[layer];
        private static int GetPriority(string layer) => PRIORITY[layer];

        private static readonly List<string> ALL = new List<string> { "motion", "eyes", "camera", "light", "psyllium" };

        [Fact]
        public void レイヤーモードはアクティブレイヤーだけを返す()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "camera", TimelineLayerViewMode.Layer, GetCategory, result);
            Assert.Equal(new[] { "camera" }, result);
        }

        [Fact]
        public void カテゴリモードは同カテゴリのレイヤーを入力順で返す()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "eyes", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "motion", "eyes" }, result);
        }

        [Fact]
        public void カテゴリモードでアクティブレイヤーが対象外でも含める()
        {
            // 他メイドのレイヤーがアクティブなど、操作対象一覧に無いケースの防御
            var targets = new List<string> { "camera", "light" };
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(targets, "motion", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "motion" }, result);
        }

        [Fact]
        public void アクティブレイヤーがnullなら空()
        {
            var result = new List<string> { "stale" };
            TimelineLayerViewFilter.Filter(ALL, null, TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Empty(result);
            TimelineLayerViewFilter.Filter(ALL, null, TimelineLayerViewMode.Layer, GetCategory, result);
            Assert.Empty(result);
        }

        [Fact]
        public void Filterは呼ぶたびに結果をクリアする()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "light", TimelineLayerViewMode.Category, GetCategory, result);
            TimelineLayerViewFilter.Filter(ALL, "light", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "light", "psyllium" }, result);
        }

        [Fact]
        public void FindFirstLayerはpriority昇順の先頭を返す()
        {
            var targets = new List<string> { "psyllium", "light" };
            var first = TimelineLayerViewFilter.FindFirstLayer(
                targets, TimelineLayerCategory.Effect, GetCategory, GetPriority);
            Assert.Equal("light", first);
        }

        [Fact]
        public void FindFirstLayerは同priorityなら入力順を保つ()
        {
            var targets = new List<string> { "b", "a" };
            var first = TimelineLayerViewFilter.FindFirstLayer(
                targets, TimelineLayerCategory.Maid, _ => TimelineLayerCategory.Maid, _ => 0);
            Assert.Equal("b", first);
        }

        [Fact]
        public void FindFirstLayerは該当なしでnull()
        {
            var first = TimelineLayerViewFilter.FindFirstLayer(
                ALL, TimelineLayerCategory.Background, GetCategory, GetPriority);
            Assert.Null(first);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: `TimelineLayerViewMode` / `TimelineLayerViewFilter` が見つからないコンパイルエラー

- [ ] **Step 3: Config に enum とフィールドを追加する**

`source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` の `// 簡易設定の種類` の直前に追加:

```csharp
    /// <summary>タイムラインの表示モード。表示/編集対象レイヤーの絞り方を決める</summary>
    public enum TimelineLayerViewMode
    {
        // アクティブレイヤーと同じカテゴリのレイヤーを並べる
        Category,
        // アクティブレイヤーだけを出す
        Layer,
    }
```

`Config` クラスの `public bool isCameraSync = true;` の直前に追加:

```csharp
        public TimelineLayerViewMode layerViewMode = TimelineLayerViewMode.Category;
```

- [ ] **Step 4: フィルタを実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerViewFilter.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表示モードに従ってタイムラインに出すレイヤーを絞る純粋ロジック。
    /// Unity 非依存のジェネリックにしてユニットテスト可能にしている
    /// </summary>
    public static class TimelineLayerViewFilter
    {
        /// <summary>
        /// 表示レイヤーを result へ詰める (result は毎回クリアする)。
        /// カテゴリモードではアクティブレイヤーが targetLayers に無くても必ず含める
        /// (行が消えて編集不能になるのを防ぐ)
        /// </summary>
        public static void Filter<TLayer>(
            IList<TLayer> targetLayers,
            TLayer currentLayer,
            TimelineLayerViewMode mode,
            Func<TLayer, TimelineLayerCategory> getCategory,
            List<TLayer> result)
            where TLayer : class
        {
            result.Clear();
            if (currentLayer == null)
            {
                return;
            }

            if (mode == TimelineLayerViewMode.Layer)
            {
                result.Add(currentLayer);
                return;
            }

            var category = getCategory(currentLayer);
            var containsCurrent = false;
            for (var i = 0; i < targetLayers.Count; i++)
            {
                var layer = targetLayers[i];
                if (getCategory(layer) != category)
                {
                    continue;
                }
                result.Add(layer);
                if (layer == currentLayer)
                {
                    containsCurrent = true;
                }
            }

            if (!containsCurrent)
            {
                result.Add(currentLayer);
            }
        }

        /// <summary>
        /// カテゴリ内で priority が最小のレイヤー。同値なら targetLayers の先に出た方。該当なしは null
        /// </summary>
        public static TLayer FindFirstLayer<TLayer>(
            IList<TLayer> targetLayers,
            TimelineLayerCategory category,
            Func<TLayer, TimelineLayerCategory> getCategory,
            Func<TLayer, int> getPriority)
            where TLayer : class
        {
            TLayer first = null;
            var firstPriority = int.MaxValue;
            for (var i = 0; i < targetLayers.Count; i++)
            {
                var layer = targetLayers[i];
                if (getCategory(layer) != category)
                {
                    continue;
                }
                var priority = getPriority(layer);
                if (first == null || priority < firstPriority)
                {
                    first = layer;
                    firstPriority = priority;
                }
            }
            return first;
        }
    }
}
```

- [ ] **Step 5: csproj に追加する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\TimelineLayerRowState.cs" />` の次行に追加:

```xml
    <Compile Include="Timeline\TimelineLayerViewFilter.cs" />
```

- [ ] **Step 6: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、`TimelineLayerViewFilterTests` 8 件を含め全 PASS

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerViewFilter.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerViewFilterTests.cs
```
commit スキルでコミット。メッセージ案: `feat(timeline): レイヤー表示モードとフィルタの純粋ロジックを追加する`

---

### Task 3: TimelineLayerRowState から非表示集合を外す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（呼び出し側の最小追従のみ。本格的な差し替えは Task 6）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs`

**Interfaces:**
- Produces: `TimelineLayerRowState<TLayer, TItem>` の残る public API:
  - `bool IsCollapsed(TLayer)` / `void ToggleCollapsed(TLayer)`
  - `void SetAllCollapsed(IList<TLayer> layers, bool collapsed)`（`currentLayer` 引数を削除）
  - `bool AreAllCollapsed(IList<TLayer> layers)`（同上）
  - `void Reset()` / `void Prune(IList<TLayer>)`
  - `void BuildRows(IList<TLayer> layers, Action<TLayer, List<TItem>> collectItems, List<LayerRow<TLayer, TItem>> result)`（`currentLayer` 引数を削除）
- 削除: `IsVisible` / `ToggleVisible` / `SetAllVisible` / `AreAllVisible`

- [ ] **Step 1: テストを新シグネチャへ書き換える**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs` を丸ごと次の内容にする:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRowStateTests
    {
        // TLayer/TItem は参照型なら何でもよいのでテストでは string を使い、
        // レイヤー自身をそのままキーにする
        private readonly TimelineLayerRowState<string, string> _state
            = new TimelineLayerRowState<string, string>(layer => layer);

        private static void CollectItems(string layer, List<string> result)
        {
            result.Add(layer + ":item0");
            result.Add(layer + ":item1");
        }

        [Fact]
        public void 折りたたみトグルが切り替わる()
        {
            // 未トグルのレイヤーは折りたたみ状態から始まる
            Assert.True(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.False(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.True(_state.IsCollapsed("A"));
        }

        [Fact]
        public void Pruneで死んだレイヤーが集合から消える()
        {
            _state.ToggleCollapsed("B");
            _state.Prune(new List<string> { "A" });
            // 展開状態が捨てられ、既定の折りたたみへ戻る
            Assert.True(_state.IsCollapsed("B"));
        }

        [Fact]
        public void 同じキーの別インスタンスへ状態が引き継がれる()
        {
            // Undo でタイムラインが作り直され、レイヤーが別インスタンスになる状況を模す
            var layer = new string("A".ToCharArray());
            var rebuiltLayer = new string("A".ToCharArray());
            Assert.False(ReferenceEquals(layer, rebuiltLayer));

            _state.ToggleCollapsed(layer);

            Assert.False(_state.IsCollapsed(rebuiltLayer));
        }

        [Fact]
        public void Prune後も同じキーなら状態が残る()
        {
            // Undo 後の Prune (キーキャッシュ貼り直し) で状態まで落とさないことを確認する
            _state.ToggleCollapsed("A");

            _state.Prune(new List<string> { new string("A".ToCharArray()), new string("B".ToCharArray()) });

            Assert.False(_state.IsCollapsed("A"));
        }

        [Fact]
        public void BuildRowsはレイヤーごとにカテゴリ行とアイテム行を積む()
        {
            _state.ToggleCollapsed("A");
            _state.ToggleCollapsed("B");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A", "B" }, CollectItems, rows);

            // A(ヘッダ+2行) + B(ヘッダ+2行)
            Assert.Equal(6, rows.Count);
            Assert.True(rows[0].isHeader);
            Assert.Equal("A", rows[0].layer);
            Assert.Equal("A:item0", rows[1].menuItem);
            Assert.Equal("A:item1", rows[2].menuItem);
            Assert.True(rows[3].isHeader);
            Assert.Equal("B", rows[3].layer);
        }

        [Fact]
        public void 折りたたみ中はカテゴリ行だけ残る()
        {
            // 既定が折りたたみなのでトグルせずそのまま組み立てる
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);

            Assert.Single(rows);
            Assert.True(rows[0].isHeader);
        }

        [Fact]
        public void SetAllCollapsedは渡したレイヤー全てを対象にする()
        {
            var layers = new List<string> { "A", "B" };
            _state.ToggleCollapsed("B");

            _state.SetAllCollapsed(layers, true);
            Assert.True(_state.IsCollapsed("A"));
            Assert.True(_state.IsCollapsed("B"));

            _state.SetAllCollapsed(layers, false);
            Assert.False(_state.IsCollapsed("A"));
            Assert.False(_state.IsCollapsed("B"));
        }

        [Fact]
        public void SetAllCollapsedは渡していないレイヤーに触れない()
        {
            _state.ToggleCollapsed("C");
            _state.SetAllCollapsed(new List<string> { "A", "B" }, true);
            Assert.False(_state.IsCollapsed("C"));
        }

        [Fact]
        public void AreAllCollapsedは全レイヤーが畳まれたときだけ真になる()
        {
            var layers = new List<string> { "A", "B" };

            // 既定は全て折りたたみ
            Assert.True(_state.AreAllCollapsed(layers));
            _state.ToggleCollapsed("A");
            Assert.False(_state.AreAllCollapsed(layers));
            _state.ToggleCollapsed("A");
            Assert.True(_state.AreAllCollapsed(layers));
        }

        [Fact]
        public void BuildRowsは呼ぶたびに結果をクリアして詰め直す()
        {
            _state.ToggleCollapsed("A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);
            Assert.Equal(3, rows.Count);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: `SetAllCollapsed` / `AreAllCollapsed` / `BuildRows` の引数数不一致でコンパイルエラー（CS1501 / CS1503）

- [ ] **Step 3: RowState を書き換える**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs` で次を行う:

1. クラスの summary を `タイムラインの複数レイヤー表示状態 (展開集合) と行リスト構築。` に直す（「非表示集合・」を削る）
2. `_hiddenKeys` フィールドと直前のコメント 2 行を削除
3. `IsVisible` / `ToggleVisible` / `SetAllVisible` / `AreAllVisible` の 4 メソッドを削除
4. `SetAllCollapsed` / `AreAllCollapsed` を次に置き換える:

```csharp
        /// <summary>渡したレイヤーの折りたたみを一括で切り替える (表示中の一覧を渡すこと)</summary>
        public void SetAllCollapsed(IList<TLayer> layers, bool collapsed)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var key = GetKey(layers[i]);
                if (collapsed)
                {
                    _expandedKeys.Remove(key);
                }
                else
                {
                    _expandedKeys.Add(key);
                }
            }
        }

        public bool AreAllCollapsed(IList<TLayer> layers)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (!IsCollapsed(layers[i]))
                {
                    return false;
                }
            }
            return true;
        }
```

5. `Reset` から `_hiddenKeys.Clear();` を削除
6. `Prune` から `_hiddenKeys.RemoveWhere(...)` の行を削除
7. `BuildRows` を次に置き換える:

```csharp
        /// <summary>
        /// レイヤーごとに「カテゴリ行 + (展開中なら) アイテム行」を layers の並び順で result へ組み立てる
        /// </summary>
        public void BuildRows(
            IList<TLayer> layers,
            Action<TLayer, List<TItem>> collectItems,
            List<LayerRow<TLayer, TItem>> result)
        {
            result.Clear();

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                result.Add(new LayerRow<TLayer, TItem> { layer = layer });

                if (IsCollapsed(layer))
                {
                    continue;
                }

                _itemBuffer.Clear();
                collectItems(layer, _itemBuffer);
                foreach (var item in _itemBuffer)
                {
                    result.Add(new LayerRow<TLayer, TItem> { layer = layer, menuItem = item });
                }
            }
        }
```

- [ ] **Step 4: TimelineWindow の呼び出しを最小限で追従させる**

`source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs` で次を行う（Task 6 で全面差し替えするが、ここではビルドを通すための最小変更）:

1. コンストラクタ内の `_displayLayerComboBox.getChecked = ...` から `_displayLayerComboBox.onHeader = ...` までの 7 行（`getName` の行は残す）を削除
2. `BuildRows()` 内の `_rowState.BuildRows(_targetLayers, currentLayer, CollectVisibleItems, _rows);` を `_rowState.BuildRows(_targetLayers, CollectVisibleItems, _rows);` に変更
3. `DrawRowStateControls` 内の `_rowState.AreAllCollapsed(layers, currentLayer)` を `_rowState.AreAllCollapsed(layers)` に、`_rowState.SetAllCollapsed(layers, currentLayer, !allCollapsed)` を `_rowState.SetAllCollapsed(layers, !allCollapsed)` に変更
4. `GetLayerComboLabel()` を次に置き換える（「他N」表記の廃止）:

```csharp
        /// <summary>コンボのボタン面ラベル。アクティブレイヤー名</summary>
        private string GetLayerComboLabel()
        {
            return GetLayerDisplayName(currentLayer, false);
        }
```

- [ ] **Step 5: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、`TimelineLayerRowStateTests` 10 件を含め全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRowState.cs source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRowStateTests.cs
```
commit スキルでコミット。メッセージ案: `refactor(timeline): レイヤー行状態から非表示集合を外す`

---

### Task 4: 簡易表示（isEasyEdit）の機能削除

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/EasyMenuItem.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（`SelectFramesRange` / `FindFrame` / `SelectVerticalBones`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs`（`ShouldDraw`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（コメント）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs`（「縦選択」）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（`BuildRows` / `alwaysShowVertical` / `DrawRowStateControls` / `indent`）

**Interfaces:**
- 削除: `Config.isEasyEdit`、`EasyMenuItem`、`BoneMenuManager.easyMenuItems`
- 残す: `EasySettingType.簡易表示`（enum 値）

- [ ] **Step 1: 参照箇所を確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -rn "isEasyEdit\|EasyMenuItem\|easyMenuItems" source --include=*.cs --include=*.csproj
```
Expected: Config.cs / BoneMenuManager.cs / EasyMenuItem.cs / TimelineManager.cs（3 箇所）/ TimelineItemInspector.cs / TimelineIntegration.cs（コメント）/ TimelineControlWindow.cs / TimelineWindow.cs（4 箇所）/ csproj の各行

- [ ] **Step 2: Config から削除する**

`source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` の `public bool isEasyEdit = false;` の行を削除する。

- [ ] **Step 3: BoneMenuManager から簡易項目を外し、EasyMenuItem を削除する**

`source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/BoneMenuManager.cs`:

1. `private List<IBoneMenuItem> easyMenuItems = null;` を削除
2. `Init()` の本体を空にする:

```csharp
        public override void Init()
        {
        }
```

3. `GetVisibleItems()` と `GetSelectedItems()` の先頭にある `if (config.isEasyEdit) { return easyMenuItems; }` ブロックをそれぞれ削除
4. `GetVisibleItems(ITimelineLayer layer, List<IBoneMenuItem> result)` の summary から `(isEasyEdit の分岐は呼び出し側が行う)` を削る

ファイル削除と csproj 更新:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git rm source/COM3D2.SceneEditor.Plugin/Timeline/BoneMenu/EasyMenuItem.cs
```
`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\BoneMenu\EasyMenuItem.cs" />` の行を削除する。

`BoneMenuManager.cs` 内で `config` が他に未使用になった場合は、その `config` プロパティ参照（`ManagerBase` 側のものなら残す）はそのままでよい。ビルド警告が出なければ触らない。

- [ ] **Step 4: TimelineManager の分岐を外す**

`source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`:

`SelectFramesRange` を次に置き換える（`else` 側の処理だけ残す）:

```csharp
        /// <summary>アクティブレイヤーの範囲を選択へ追加する (他レイヤーの既存選択には影響しない)</summary>
        public void SelectFramesRange(int startFrameNo, int endFrameNo)
        {
            var selectedMenuItems = boneMenuManager.GetSelectedItems();
            for (int i = startFrameNo; i <= endFrameNo; i++)
            {
                var frame = currentLayer.GetFrame(i);
                if (frame != null)
                {
                    foreach (var bone in frame.bones)
                    {
                        if (selectedMenuItems.Count == 0)
                        {
                            selectedBones.Add(bone);
                        }
                        else
                        {
                            foreach (var boneMenuItem in selectedMenuItems)
                            {
                                if (boneMenuItem.IsTargetBone(bone))
                                {
                                    selectedBones.Add(bone);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
```

`FindFrame` を次に置き換える:

```csharp
        private FrameData FindFrame(int start, int step)
        {
            var selectedMenuItems = boneMenuManager.GetSelectedItems();
            for (int i = start; i >= 0 && i <= timeline.maxFrameNo; i += step)
            {
                var frame = currentLayer.GetFrame(i);
                if (frame != null)
                {
                    if (selectedMenuItems.Count == 0)
                    {
                        return frame;
                    }

                    foreach (var bone in frame.bones)
                    {
                        foreach (var boneMenuItem in selectedMenuItems)
                        {
                            if (boneMenuItem.IsTargetBone(bone))
                            {
                                return frame;
                            }
                        }
                    }
                }
            }
            return null;
        }
```

`SelectVerticalBones` の先頭 `if (config.isEasyEdit) { return; }` ブロックを削除する。

- [ ] **Step 5: TimelineItemInspector / TimelineIntegration / TimelineControlWindow を直す**

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TimelineItemInspector.cs` の `ShouldDraw` から次の 5 行を削除:

```csharp
            // 簡易表示の GetSelectedItems は疑似項目 EasyMenuItem を常に返すため対象外
            if (timelineConfig.isEasyEdit)
            {
                return false;
            }
```

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` の `_managers` 初期化子内、`MTEP.BoneMenuManager.Instance,` の直前にあるコメント 2 行（`// Init で簡易表示用の...` と `// 簡易表示 ON 時に...`）を削除する。

`source/COM3D2.SceneEditor.Plugin/TimelineControlWindow.cs` の「縦選択」ボタンを常時有効にする:

```csharp
            WrapIfNeeded(view, 60);
            if (view.DrawButton("縦選択", 60, ROW_HEIGHT))
            {
                timelineManager.SelectVerticalBones();
            }
```

- [ ] **Step 6: TimelineWindow を直す**

`source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`:

1. `BuildRows()` を次に置き換える:

```csharp
        /// <summary>表示行リストを組み立てる</summary>
        private void BuildRows()
        {
            _rowState.BuildRows(_targetLayers, CollectVisibleItems, _rows);
        }
```

2. `bool alwaysShowVertical = !tc.isEasyEdit;` を `bool alwaysShowVertical = true;` に変更
3. `DrawRowStateControls` 冒頭の `if (tc.isEasyEdit) { return; }` ブロックを削除し、summary の「簡易表示はレイヤー行を持たないため出さない」の行を削る
4. `var indent = tc.isEasyEdit ? 0 : MENU_INDENT_WIDTH;` とその直前のコメント行 `// 簡易表示はカテゴリ行を持たないため字下げしない` を `var indent = MENU_INDENT_WIDTH;` に置き換える

- [ ] **Step 7: 参照が残っていないことを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -rn "isEasyEdit\|EasyMenuItem\|easyMenuItems" source --include=*.cs --include=*.csproj
```
Expected: 出力なし

- [ ] **Step 8: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、全テスト PASS

- [ ] **Step 9: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
```
commit スキルでコミット。メッセージ案: `refactor(timeline): 簡易表示 (isEasyEdit) を削除する`

---

### Task 5: モード切替アイコンとアイコンボタン描画

**Files:**
- Create: `assets/icons/CategoryMode.svg`、`assets/icons/LayerMode.svg`
- Delete: `assets/icons/EasyEdit.svg`、`assets/icons/EasyEdit.png`
- Modify: `assets/icons/generate.js`（`ICONS` 配列）
- Modify: `source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs`（`Kind` / `PNG_BASE64`）

**Interfaces:**
- Produces: `ToolbarIcons.Kind.CategoryMode` / `ToolbarIcons.Kind.LayerMode`（`Kind.EasyEdit` は削除）
- 既存を流用: `bool GUIView.DrawTextureButton(Texture2D texture, float width, float height, float offsetSize = 0f, bool enabled = true, GUIStyle style = null, string tooltip = null)`（GUIView への追加は行わない）

- [ ] **Step 1: SVG を作る**

`assets/icons/CategoryMode.svg`:

```xml
<!--
  タイムライン「カテゴリモード」アイコン (32x32)。角丸の枠の中に横線 3 本 (グループ化された行)。
  白地に黒縁取りで、明るい背景でも暗い背景でも視認できるようにしている。
-->
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <g fill="#ffffff" stroke="#000000" stroke-width="0.8" stroke-linejoin="round">
    <!-- 枠 -->
    <rect x="4" y="5" width="24" height="22" rx="3" ry="3" />
    <!-- 行 3 本 -->
    <rect x="8" y="9" width="16" height="3" rx="1" ry="1" />
    <rect x="8" y="14.5" width="16" height="3" rx="1" ry="1" />
    <rect x="8" y="20" width="16" height="3" rx="1" ry="1" />
  </g>
</svg>
```

`assets/icons/LayerMode.svg`:

```xml
<!--
  タイムライン「レイヤーモード」アイコン (32x32)。角丸の枠の中に横線 1 本 (単一の行)。
  白地に黒縁取りで、明るい背景でも暗い背景でも視認できるようにしている。
-->
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <g fill="#ffffff" stroke="#000000" stroke-width="0.8" stroke-linejoin="round">
    <!-- 枠 -->
    <rect x="4" y="5" width="24" height="22" rx="3" ry="3" />
    <!-- 行 1 本 (中央) -->
    <rect x="8" y="14.5" width="16" height="3" rx="1" ry="1" />
  </g>
</svg>
```

EasyEdit を削除:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git rm assets/icons/EasyEdit.svg assets/icons/EasyEdit.png
```

- [ ] **Step 2: generate.js の一覧を更新する**

`assets/icons/generate.js` の `ICONS` を次に置き換える（`EasyEdit` を外し、末尾に 2 つ追加）:

```js
// ToolbarIcons.Kind と対応させること
const ICONS = [
    'Bg', 'Maid', 'Gizmo', 'Ortho', 'Change', 'Link', 'Home', 'Focus', 'Global',
    'EditMode', 'AutoKey', 'Model', 'Camera', 'FovLock', 'FocusLock', 'PostEffect',
    'SkipStart', 'PrevKey', 'PrevFrame', 'Play', 'Pause', 'NextFrame', 'NextKey', 'SkipEnd',
    'CategoryMode', 'LayerMode',
];
```

- [ ] **Step 3: PNG と base64 を生成する**

依存の確認と生成:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/assets/icons
ls ../cursors/node_modules/@resvg/resvg-js >/dev/null 2>&1 || (cd ../cursors && npm install)
node generate.js > "$TEMP/icons-base64.txt"
```
（出力はリポジトリ外の一時ファイルへ落とす。貼り付け元として使うだけでコミットしない）

Expected: `CategoryMode.png` / `LayerMode.png` が生成され、標準出力に `// <名前> (N bytes)` と base64 が `ICONS` の順で 26 個 + `PluginIcon` の 1 個並ぶ。

- [ ] **Step 4: ToolbarIcons.cs を更新する**

`source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs`:

1. `Kind` から次の 2 行を削除:
```csharp
            // 簡易表示 (上下から中央線へ寄る山形)
            EasyEdit,
```

2. `Kind` の `SkipEnd,` の後に追加:
```csharp
            // タイムラインのカテゴリモード (枠の中に横線 3 本)
            CategoryMode,
            // タイムラインのレイヤーモード (枠の中に横線 1 本)
            LayerMode,
```

3. `PNG_BASE64` の中身を、Step 3 の出力のうち `ICONS` 分（先頭 26 個、`PluginIcon` は除く）で**丸ごと置き換える**（順序は `ICONS` = `Kind` の順）。生成出力に含まれる `// <名前> (N bytes)` のコメント行は**各 base64 の直前に残す**（添字と `Kind` の対応を目で追えるようにするため。既存の配列にはコメントが無いが、今回から付ける）。`PluginIcon` の出力は `PluginInfo.Icon` 用なので今回は使わない

4. 置き換え後の要素数と並び順を確認:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
awk '/PNG_BASE64 =/{f=1} f&&/^ *"/{n++} f&&/^ *};/{print n; exit}' source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs
grep -c "^            [A-Za-z]*,$" source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs
# 配列内のコメント名の並びが Kind の並びと一致すること
diff <(awk '/PNG_BASE64 =/{f=1} f&&/^ *\/\/ /{print $2} f&&/^ *};/{exit}' source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs) \
     <(awk '/enum Kind/{f=1} f&&/^            [A-Za-z]+,$/{sub(",","");print $1} f&&/^        }/{exit}' source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs)
```
Expected: 前 2 つはどちらも `26`、`diff` は出力なし

5. 生成した `assets/icons/CategoryMode.png` / `LayerMode.png` を Read で開き、図案（横線 3 本 / 1 本）が意図どおりか目視する

- [ ] **Step 5: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、全テスト PASS。`Kind.EasyEdit` 参照が残っていればビルドエラーになるので、その箇所を削除する

- [ ] **Step 6: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add assets/icons source/COM3D2.SceneEditor.Plugin/ToolbarIcons.cs
```
commit スキルでコミット。メッセージ案: `feat(icons): タイムライン表示モードのアイコンを追加し簡易表示アイコンを削除する`

---

### Task 6: TimelineWindow のモード切替 UI と表示範囲の接続

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`

**Interfaces:**
- Consumes: `TimelineLayerViewFilter.Filter` / `FindFirstLayer`（Task 2）、`Config.layerViewMode`（Task 2）、`TimelineLayerInfo.category` / `.priority`（Task 1）、`TimelineLayerCategory.ToDisplayName()`（Task 1）、`ToolbarIcons.Kind.CategoryMode` / `LayerMode`（Task 5）、既存の `GUIView.DrawTextureButton(Texture2D, float, float, float offsetSize, bool enabled, GUIStyle style, string tooltip)`、`TimelineLayerRowState.BuildRows(layers, collectItems, result)`（Task 3）
- 既存: `timelineManager.SetCurrentLayer(ITimelineLayer)`、`timelineManager.GetLayerInfo(Type)`、`MTEUtils.GetEnumValues<T>()`

- [ ] **Step 1: フィールドを差し替える**

`source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`:

1. `_displayLayerComboBox`（`GUIMultiSelectComboBox<MTEP.ITimelineLayer>`）のフィールド定義と summary を削除し、代わりに次を追加:

```csharp
        /// <summary>モードボタンのアイコン余白 (TimelineControlWindow.ICON_TOGGLE_OFFSET と同じ値)</summary>
        private static readonly float MODE_ICON_OFFSET = 4f;

        /// <summary>レイヤーモードの選択コンボ。操作対象レイヤーから 1 つ選んでアクティブ化する</summary>
        private readonly GUIComboBox<MTEP.ITimelineLayer> _layerComboBox = new GUIComboBox<MTEP.ITimelineLayer>
        {
            contentSize = new Vector2(200, 300),
            // menuWidth (100〜300px) に収めるため前後送りの矢印は省略する
            showArrow = false,
        };

        /// <summary>カテゴリモードの選択コンボ。カテゴリを選ぶとその先頭レイヤーをアクティブ化する</summary>
        private readonly GUIComboBox<MTEP.TimelineLayerCategory> _categoryComboBox
            = new GUIComboBox<MTEP.TimelineLayerCategory>
        {
            contentSize = new Vector2(200, 300),
            showArrow = false,
        };

        /// <summary>カテゴリの列挙順 (enum 定義順)</summary>
        private static readonly List<MTEP.TimelineLayerCategory> ALL_CATEGORIES =
            MTEUtils.GetEnumValues<MTEP.TimelineLayerCategory>();

        /// <summary>カテゴリごとの操作対象レイヤー数。BuildTargetLayers で詰め直す</summary>
        private readonly int[] _categoryLayerCounts = new int[ALL_CATEGORIES.Count];

        /// <summary>操作対象レイヤーが 1 件以上あるカテゴリ (コンボの項目)。BuildTargetLayers で詰め直す</summary>
        private readonly List<MTEP.TimelineLayerCategory> _availableCategories
            = new List<MTEP.TimelineLayerCategory>(ALL_CATEGORIES.Count);

        /// <summary>今フレームの表示対象レイヤー (_targetLayers を表示モードで絞ったもの)。DrawBody で詰め直す</summary>
        private readonly List<MTEP.ITimelineLayer> _displayLayers
            = new List<MTEP.ITimelineLayer>(32);
```

`MTEUtils` の名前空間が `using` されていなければ、`ToolbarIcons.cs` と同じく `using COM3D2.MotionTimelineEditor;` を先頭に足す。

2. `_targetLayers` の summary を次に直す:

```csharp
        /// <summary>
        /// 今フレームの操作対象レイヤー (他メイドのレイヤーを除いたもの)。DrawBody で詰め直す。
        /// 表示対象 (_displayLayers) はこれを表示モードで絞ったもの
        /// </summary>
```

- [ ] **Step 2: コンストラクタの初期化を差し替える**

コンストラクタ内の `_displayLayerComboBox.getName = ...` の行（と直前のコメント 2 行）を次に置き換える:

```csharp
            // フィールド初期化子ではインスタンスメンバーを参照できないためここで設定する
            // ドロップダウンは操作対象で絞った一覧なのでメイド名は省く
            _layerComboBox.getName = (layer, _) => GetLayerDisplayName(layer, false);
            _layerComboBox.onSelected = (layer, _) =>
            {
                if (layer != timelineManager.currentLayer)
                {
                    timelineManager.SetCurrentLayer(layer);
                }
            };

            _categoryComboBox.getName = (category, _) => GetCategoryLabel(category);
            _categoryComboBox.onSelected = (category, _) =>
            {
                var first = TimelineLayerViewFilter.FindFirstLayer(
                    _targetLayers, category, GetLayerCategory, GetLayerPriority);
                if (first != null && first != timelineManager.currentLayer)
                {
                    timelineManager.SetCurrentLayer(first);
                }
            };
```

- [ ] **Step 3: ヘルパーを追加する**

`GetLayerStateKey` の直前に追加:

```csharp
        // GetLayerInfo は未登録の型に null を返す。全レイヤー型は登録済みの前提だが、
        // GetLayerDisplayName と同じく null を握って「その他 / 最後尾」へ寄せる
        private static MTEP.TimelineLayerCategory GetLayerCategory(MTEP.ITimelineLayer layer)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            return info != null ? info.category : MTEP.TimelineLayerCategory.Other;
        }

        private static int GetLayerPriority(MTEP.ITimelineLayer layer)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            return info != null ? info.priority : int.MaxValue;
        }

        /// <summary>カテゴリコンボの項目名。カテゴリ名 + 操作対象レイヤー数</summary>
        private string GetCategoryLabel(MTEP.TimelineLayerCategory category)
        {
            return category.ToDisplayName() + " (" + _categoryLayerCounts[(int)category] + ")";
        }
```

- [ ] **Step 4: BuildTargetLayers でカテゴリ集計と表示フィルタを行う**

`BuildTargetLayers()` を次に置き換える:

```csharp
        /// <summary>
        /// タイムラインに出すレイヤーを詰め直す。メイドに紐づくレイヤーは
        /// タイムライン操作ウィンドウで選んだ操作対象のものだけに絞る
        /// (スロットを持たないカメラ・背景等は常に対象)。
        /// あわせてカテゴリ別の件数と、表示モードで絞った表示対象も組み立てる
        /// </summary>
        private void BuildTargetLayers()
        {
            _targetLayers.Clear();
            Array.Clear(_categoryLayerCounts, 0, _categoryLayerCounts.Length);

            var slotNo = maidManager.maidSlotNo;
            foreach (var layer in timelineManager.layers)
            {
                // アクティブレイヤーは操作対象と食い違っても常に出す (行が消えて編集不能になるのを防ぐ)
                if (!layer.hasSlotNo || layer.slotNo == slotNo || layer == currentLayer)
                {
                    _targetLayers.Add(layer);
                    _categoryLayerCounts[(int)GetLayerCategory(layer)]++;
                }
            }

            _availableCategories.Clear();
            foreach (var category in ALL_CATEGORIES)
            {
                if (_categoryLayerCounts[(int)category] > 0)
                {
                    _availableCategories.Add(category);
                }
            }

            TimelineLayerViewFilter.Filter(
                _targetLayers, currentLayer, timelineConfig.layerViewMode, GetLayerCategory, _displayLayers);
        }
```

`using System;` が無ければ先頭に足す（`Array.Clear` のため）。

`BuildRows()` を次に置き換える:

```csharp
        /// <summary>表示対象レイヤーから表示行リストを組み立てる</summary>
        private void BuildRows()
        {
            _rowState.BuildRows(_displayLayers, CollectVisibleItems, _rows);
        }
```

`DrawRowStateControls` の `var layers = _targetLayers;` を `var layers = _displayLayers;` に変更する。

- [ ] **Step 5: DrawLayerControls を差し替える**

`DrawLayerControls(GUIView view, int menuWidth)` を次に置き換え、`GetLayerComboLabel()` を削除する:

```csharp
        /// <summary>
        /// ボーンメニュー上部 (フレーム番号バーと同じ高さの空き領域) にレイヤー行を描く。
        /// モード切替 + 選択コンボ + 削除 + 追加をメニュー幅いっぱいに並べる
        /// </summary>
        private void DrawLayerControls(GUIView view, int menuWidth)
        {
            var layerType = currentLayer.layerType;
            var isCategoryMode = timelineConfig.layerViewMode == MTEP.TimelineLayerViewMode.Category;

            view.currentPos.x = 0;
            view.currentPos.y = 0;
            DrawViewModeButton(view, isCategoryMode);

            // メニュー幅が極端に狭くてもボタンが負座標へ回り込まないよう下限を設ける
            var comboWidth = Mathf.Max(
                LAYER_BUTTON_WIDTH, menuWidth - FRAME_LABEL_HEIGHT - LAYER_BUTTON_WIDTH * 2);

            view.currentPos.x = FRAME_LABEL_HEIGHT;
            view.currentPos.y = 0;
            if (isCategoryMode)
            {
                _categoryComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
                _categoryComboBox.items = _availableCategories;
                _categoryComboBox.currentIndex = _availableCategories.IndexOf(GetLayerCategory(currentLayer));
                _categoryComboBox.DrawButton(view);
            }
            else
            {
                _layerComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
                _layerComboBox.items = _targetLayers;
                _layerComboBox.currentIndex = _targetLayers.IndexOf(currentLayer);
                _layerComboBox.DrawButton(view);
            }

            view.currentPos.x = FRAME_LABEL_HEIGHT + comboWidth;
            view.currentPos.y = 0;
            if (view.DrawButton("-", LAYER_BUTTON_WIDTH, FRAME_LABEL_HEIGHT,
                    layerType != typeof(MTEP.MotionTimelineLayer)))
            {
                timelineManager.RemoveLayers(layerType);
            }

            view.currentPos.x = FRAME_LABEL_HEIGHT + comboWidth + LAYER_BUTTON_WIDTH;
            view.currentPos.y = 0;
            _addLayerComboBox.currentIndex = -1;
            // 現在のメイドでまだ使っていない型を列挙する (スロット無しレイヤーは存在チェックのみ)。
            // 旧レイヤーコンボが担っていた「型選択で現在メイドのインスタンスを自動生成する」導線の代替
            _addableLayerInfoList.Clear();
            foreach (var info in timelineManager.layerInfoList)
            {
                if (timelineManager.GetLayer(info.layerType, maidManager.maidSlotNo) == null)
                {
                    _addableLayerInfoList.Add(info);
                }
            }
            _addLayerComboBox.items = _addableLayerInfoList;
            _addLayerComboBox.DrawButton(view);
        }

        /// <summary>
        /// 表示モードの切替ボタン。現在のモードのアイコンを出し、押すともう一方へ切り替える。
        /// アイコンが読めない環境では 1 文字のテキストボタンにフォールバックする
        /// </summary>
        private void DrawViewModeButton(GUIView view, bool isCategoryMode)
        {
            var kind = isCategoryMode ? ToolbarIcons.Kind.CategoryMode : ToolbarIcons.Kind.LayerMode;
            var tooltip = isCategoryMode ? "カテゴリモード" : "レイヤーモード";
            var icon = ToolbarIcons.GetTexture(kind);

            var clicked = icon != null
                ? view.DrawTextureButton(icon, FRAME_LABEL_HEIGHT, FRAME_LABEL_HEIGHT, MODE_ICON_OFFSET, tooltip: tooltip)
                : view.DrawButton(isCategoryMode ? "カ" : "レ", FRAME_LABEL_HEIGHT, FRAME_LABEL_HEIGHT);
            if (!clicked)
            {
                return;
            }

            timelineConfig.layerViewMode = isCategoryMode
                ? MTEP.TimelineLayerViewMode.Layer
                : MTEP.TimelineLayerViewMode.Category;
            timelineConfig.dirty = true;
        }
```

- [ ] **Step 6: 旧コンボの残骸が無いことを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "_displayLayerComboBox\|GetLayerComboLabel\|IsVisible(\|ToggleVisible\|AreAllVisible\|SetAllVisible" source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
```
Expected: 出力なし（`GUIMultiSelectComboBox` クラス自体は他で使っていなくても残してよい）

- [ ] **Step 7: ビルド + テスト**

共通コマンドを実行する。Expected: 両構成ビルド成功、全テスト PASS

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
```
commit スキルでコミット。メッセージ案: `feat(timeline): レイヤー表示をカテゴリ/レイヤーモードの切替にする`

---

### Task 7: ドキュメント更新と実機確認

**Files:**
- Modify: `docs-site/guide/timeline.md`（「## レイヤー」節の冒頭）

- [ ] **Step 1: ガイドを書き換える**

`docs-site/guide/timeline.md` の `タイムラインはレイヤー単位で編集します。レイヤーコンボボックスで切り替えます。` の段落を次に置き換える:

```markdown
タイムラインはレイヤー単位で編集します。ボーンメニュー上部の行で表示モードとアクティブレイヤーを切り替えます。

| 操作 | 内容 |
|---|---|
| モードボタン（左端のアイコン） | **カテゴリモード**（枠に横線 3 本）と**レイヤーモード**（枠に横線 1 本）を切り替えます。設定に保存されます |
| 選択コンボ | カテゴリモードではカテゴリ（メイド / カメラ / モデル / 背景 / ライト・演出 / その他）を、レイヤーモードではレイヤーを選びます。カテゴリを選ぶとそのカテゴリの先頭レイヤーがアクティブになります |
| `-` / `+` | アクティブレイヤーの型を削除 / 未使用の型を追加してアクティブ化 |

カテゴリモードではアクティブレイヤーと同じカテゴリのレイヤーが並び、レイヤー見出し行のクリックでアクティブレイヤーを切り替えられます。レイヤーモードではアクティブレイヤーだけが表示されます。どちらのモードでも編集（キーフレームの追加・削除・移動）はアクティブレイヤーに対して行われ、表示されていないレイヤーは編集できません。
```

- [ ] **Step 2: コミット**

```bash
git add docs-site/guide/timeline.md
```
commit スキルでコミット。メッセージ案: `docs: タイムラインのレイヤー表示モードを説明する`

- [ ] **Step 3: 実機確認（ゲーム起動中のみ、devbridge）**

`mcp__com3d25-devbridge__ping` で死活確認後、新ビルドをホットリロードするか、ゲーム再起動後に以下を確認して結果を報告する（未確認の項目は「未確認」と明記する）:

1. モードボタンで両モードが切り替わり、アイコンが変わる。`Config\SceneEditor\` の設定 XML に `<layerViewMode>` が保存される
2. カテゴリコンボに操作対象レイヤーがあるカテゴリだけが「名前 (N)」で並び、選ぶと先頭レイヤーがアクティブになって表示が切り替わる
3. Hierarchy でメイド / 追加ライトを選択すると、アクティブレイヤーの切替に表示が追従する
4. 「+」でレイヤーを追加した直後、そのレイヤーが表示範囲に入っている
5. レイヤーモードでアクティブ以外のレイヤー行が出ない
6. 「縦選択」が常時押せて動作する

- [ ] **Step 4: code-review スキルでレビューする**

`code-review` スキルを起動し、指摘を取り込んでからユーザーへ報告する。

---

## Self-Review（作成時に実施済み）

- **Spec coverage**: §3.1 → Task 1、§3.2 → Task 2、§3.3 → Task 3、§4 → Task 2 + Task 6、§5.1〜5.2 → Task 6、§5.3 → Task 5、§6 → Task 4、§7 → 各 Task、§8 → Task 1/2/3 のテストと Task 7 Step 3
- **Placeholder scan**: なし
- **Type consistency**: `TimelineLayerViewFilter.Filter` / `FindFirstLayer` のシグネチャは Task 2 定義と Task 6 呼び出しで一致。`BuildRows(layers, collectItems, result)` / `SetAllCollapsed(layers, collapsed)` / `AreAllCollapsed(layers)` は Task 3 定義と Task 4/6 呼び出しで一致。Task 6 のアイコンボタンは既存 `GUIView.DrawTextureButton` の実シグネチャ（`offsetSize` 第 4 引数、`tooltip` は名前付き引数）に合わせている

## レビュー却下メモ

- `ALL_CATEGORIES`（static）と `_categoryLayerCounts`（インスタンス）の初期化順序にコメントを付けるべき — C# 仕様上インスタンス初期化子は静的初期化完了後に走り、static を参照するインスタンスフィールドはこのファイルにも既にある一般的な書き方なので、コメントは不要と判断して却下
