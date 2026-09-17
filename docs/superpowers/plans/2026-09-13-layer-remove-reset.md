# レイヤー削除時の初期化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** レイヤーを削除・アンロード・読込したとき、実体を持つレイヤーは実体ごと破棄し、従属レイヤーは既定値へ戻し、値のみのレイヤーは従来どおり誕生時の断面へ復元する。

**Architecture:** 後始末を直交する 2 つに分ける。実体の後始末はレイヤー自身の `ResetOnRemove()`（どのマネージャを触るかはレイヤーが知っている）、値の復元は既存の `TimelineLayerDescAttribute.CanRestoreOnRemove` 宣言。`TimelineManager` は常に `ResetOnRemove()` を呼んでから、宣言が復元対象なら断面を戻す。`ライト` はこの直交性を使って「追加ライトは消す・メインライトは値を戻す」を表す。

**Tech Stack:** C#（COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成ビルド）、xunit 2.9（net48、`source/COM3D2.SceneEditor.Plugin.Tests`）、MSBuild 直叩き。

**Spec:** `docs/superpowers/specs/2026-09-13-layer-remove-reset-design.md`

## Global Constraints

- コードのコメントとエラーログメッセージは**日本語**で書く。
- **2 構成ともビルドする**。COM3D2 構成は .NET 3.5 なので `Tuple`、入力 5 個以上の `Func<>` / `Action<>` は使えない。
- ビルドは `debug.bat` を使わない（実機へ DLL をコピーするため）。MSBuild を直接叩く。
- Git Bash から MSBuild を叩くときは `export MSYS2_ARG_CONV_EXCL="*"` が必須。
- ビルド順は **COM3D2 → COM3D25 → `dotnet test`**。
- プラグイン csproj は旧形式。新規 `.cs` は `<Compile Include>` への追記が必須（テストプロジェクトは SDK 形式なので不要）。
- `deploy.bat` / `release.bat` は実行しない。
- メソッド名（テスト名を含む）に `-` は使えない。日本語識別子として妥当な名前にする。

**共通ビルドコマンド**（以降「共通ビルド」と書いたらこれ）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
&& "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
&& dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

## ファイル構成

| ファイル | 責務 |
|---|---|
| `Timeline/TimelineLayer/ITimelineLayer.cs`（変更） | `void ResetOnRemove()` の追加 |
| `Timeline/TimelineLayer/TimelineLayerBase.cs`（変更） | `ResetOnRemove()` の既定実装（何もしない） |
| `Timeline/TimelineLayer/{StageLight,StageLaser,Psyllium,Model,Light,PngPlacement,Text,SubCamera}TimelineLayer.cs`（変更） | 実体破棄の実装と属性宣言 |
| `Timeline/TimelineLayer/{ModelBone,ModelShapeKey,ModelMaterial}TimelineLayer.cs`（変更） | 既定値戻しの実装と属性宣言 |
| `Timeline/Manager/TimelineManager.cs`（変更） | `RemoveLayer` / `ResetTimelineState` からの後始末の呼び出し |
| `COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs`（変更） | 断面復元の対象表を新分類へ更新 |
| `COM3D2.SceneEditor.Plugin.Tests/TimelineLayerResetOnRemoveTests.cs`（新規） | `ResetOnRemove` を実装したレイヤーが期待表どおりか |
| `docs-site/timeline/layers.md` / `files.md` / `layers-camera.md` / `layers-background.md`（変更） | 削除時の挙動の記述 |

**新しい列挙も新しいマネージャ API も足さない。** `ModelMaterial.Reset()` と `SubCameraManager.SetCameraCount(int)` は実在するのでそのまま使う。

---

### Task 1: 後始末のフックを足し、宣言を新分類へ合わせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs`
- Modify: 11 レイヤーの属性行（実体破棄 7 + 従属 3 + サブカメラのコメント）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs`

**Interfaces:**
- Produces: `ITimelineLayer.ResetOnRemove()` — 実体の破棄または既定値戻し。既定は何もしない
- 変更なし: `TimelineLayerDescAttribute.CanRestoreOnRemove`（既定 `true`）、`TimelineLayerRestorePolicy.CanRestoreOnRemove(Type)`

- [ ] **Step 1: 断面復元の期待表を新分類へ直す（失敗するテスト）**

`TimelineLayerRestorePolicyTests.cs` の `EXPECTED_EXCLUDED` を差し替える。`ライト` は入れない（メインライトの値を戻すため復元対象のまま）。

```csharp
        // 設計メモ「決定事項」の表。レイヤー型を増やしたらここにも足すこと
        private static readonly HashSet<string> EXPECTED_EXCLUDED = new HashSet<string>
        {
            // 何もしない (再生トリガや着替えをやり直してしまう)
            "MotionTimelineLayer",
            "VoiceTimelineLayer",
            "SeTimelineLayer",
            "DressTimelineLayer",
            // 実体ごと破棄する
            "StageLightTimelineLayer",
            "StageLaserTimelineLayer",
            "PsylliumTimelineLayer",
            "ModelTimelineLayer",
            "PngPlacementTimelineLayer",
            "TextTimelineLayer",
            "SubCameraTimelineLayer",
            // 実体は残して既定値へ戻す
            "ModelBoneTimelineLayer",
            "ModelShapeKeyTimelineLayer",
            "ModelMaterialTimelineLayer",
        };
```

- [ ] **Step 2: ResetOnRemove の実装状況を検証するテストを書く（失敗するテスト）**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerResetOnRemoveTests.cs` を新規作成する。
Unity 実体に触れずに「どのレイヤーが後始末を実装しているか」だけをリフレクションで検証する。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerResetOnRemoveTests
    {
        // 設計メモ「決定事項」の表。実体を持つレイヤーと従属レイヤーだけが実装する
        private static readonly HashSet<string> EXPECTED_OVERRIDES = new HashSet<string>
        {
            "StageLightTimelineLayer",
            "StageLaserTimelineLayer",
            "PsylliumTimelineLayer",
            "ModelTimelineLayer",
            "LightTimelineLayer",
            "PngPlacementTimelineLayer",
            "TextTimelineLayer",
            "SubCameraTimelineLayer",
            "ModelBoneTimelineLayer",
            "ModelShapeKeyTimelineLayer",
            "ModelMaterialTimelineLayer",
        };

        private static bool HasOwnResetOnRemove(Type type)
        {
            var method = type.GetMethod("ResetOnRemove", Type.EmptyTypes);
            return method != null && method.DeclaringType == type;
        }

        [Fact]
        public void 後始末を実装しているレイヤーが期待どおり()
        {
            var types = TimelineLayerTestUtils.GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = EXPECTED_OVERRIDES.Contains(type.Name);
                var actual = HasOwnResetOnRemove(type);
                Assert.True(expected == actual,
                    type.Name + " の ResetOnRemove 実装が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のレイヤー型がすべて実在する()
        {
            var names = TimelineLayerTestUtils.GetConcreteLayerTypes().Select(t => t.Name).ToList();
            foreach (var name in EXPECTED_OVERRIDES)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void ライトだけは後始末と断面復元の両方を行う()
        {
            var lightType = TimelineLayerTestUtils.GetConcreteLayerTypes()
                .First(t => t.Name == "LightTimelineLayer");

            Assert.True(HasOwnResetOnRemove(lightType));
            Assert.True(TimelineLayerRestorePolicy.CanRestoreOnRemove(lightType));
        }
    }
}
```

- [ ] **Step 3: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "TimelineLayerResetOnRemoveTests|TimelineLayerRestorePolicyTests"`
Expected: `TimelineLayerRestorePolicyTests` が期待表の不一致で FAIL（`ResetOnRemove` はまだ基底にしか無いので `TimelineLayerResetOnRemoveTests` の 1 件目も FAIL）

- [ ] **Step 4: インターフェースと既定実装を足す**

`ITimelineLayer.cs` の `void Dispose();` の直前へ足す。

```csharp
        /// <summary>
        /// レイヤー削除・アンロード時の後始末。実体ごと捨てるか既定値へ戻す。
        /// 実体を持つレイヤーと、モデルに従属するレイヤーだけが実装する
        /// </summary>
        void ResetOnRemove();
```

`TimelineLayerBase.cs` の `public virtual void Dispose()` の直前へ足す。

```csharp
        /// <summary>既定では何もしない。実体を持つレイヤーだけが override する</summary>
        public virtual void ResetOnRemove()
        {
            // do nothing
        }
```

- [ ] **Step 5: 属性の宣言を直す**

実体破棄 7 レイヤーと従属 3 レイヤーへ `CanRestoreOnRemove = false` を足す。表示名・priority・カテゴリは変えない。`ライト` は触らない（既定の `true` のまま）。

```csharp
// Timeline/TimelineLayer/StageLightTimelineLayer.cs
    [TimelineLayerDesc("ステージライト", 42, TimelineLayerCategory.Effect, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/StageLaserTimelineLayer.cs
    [TimelineLayerDesc("ステージレーザー", 43, TimelineLayerCategory.Effect, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/PsylliumTimelineLayer.cs
    [TimelineLayerDesc("サイリウム", 44, TimelineLayerCategory.Effect, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/ModelTimelineLayer.cs
    [TimelineLayerDesc("モデル", 21, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/PngPlacementTimelineLayer.cs
    [TimelineLayerDesc("PNG配置", 35, TimelineLayerCategory.Background, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/TextTimelineLayer.cs
    [TimelineLayerDesc("テキスト", 53, TimelineLayerCategory.Other, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/SubCameraTimelineLayer.cs
    [TimelineLayerDesc("サブカメラ", 25, TimelineLayerCategory.Camera, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/ModelBoneTimelineLayer.cs
    [TimelineLayerDesc("モデルボーン", 22, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs
    [TimelineLayerDesc("モデルシェイプ", 23, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
// Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs
    [TimelineLayerDesc("モデルマテリアル", 24, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
```

現状の宣言を洗い出してから直す。

Run: `grep -rn "CanRestoreOnRemove" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/`
Expected: 変更後は 14 レイヤー（対象外 4 + 実体破棄 7 + 従属 3）に `= false` が並ぶ

- [ ] **Step 6: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功。`TimelineLayerRestorePolicyTests` は PASS、`TimelineLayerResetOnRemoveTests` は実装がまだなので 1 件目が FAIL（Task 2・3 で解消する）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ \
        source/COM3D2.SceneEditor.Plugin.Tests/
git commit -m "feat(timeline): レイヤー削除時の後始末フックを追加する"
```

---

### Task 2: 実体を破棄する後始末を実装する

**Files:**
- Modify: `Timeline/TimelineLayer/StageLightTimelineLayer.cs` / `StageLaserTimelineLayer.cs` / `PsylliumTimelineLayer.cs` / `ModelTimelineLayer.cs` / `LightTimelineLayer.cs` / `PngPlacementTimelineLayer.cs` / `TextTimelineLayer.cs` / `SubCameraTimelineLayer.cs`

**Interfaces:**
- Consumes: `timeline.stageLightCountList` / `stageLaserCountList` / `psylliums` / `models` / `lights` / `pngObjects` / `textCount`
- Consumes: `stageLightManager.SetupLights(List<int>)` / `stageLaserManager.SetupLasers(List<int>)` / `psylliumManager.Setup(List<TimelinePsylliumData>)` / `modelManager.SetupModels(List<TimelineModelData>)` / `lightManager.SetupLights(List<TimelineLightData>)` / `pngObjectManager.Setup(List<TimelinePngObjectData>)` / `textManager.ReleaseTexts()` / `subCameraManager.SetCameraCount(int)`

> Unity 実体に触れるためユニットテストで検証できない（実装の有無は Task 1 のリフレクションテストで担保する）。挙動は Task 5 の実機確認で見る。

- [ ] **Step 1: 各レイヤーがマネージャ参照を持つか確認する**

Run: `grep -n "stageLightManager\|stageLaserManager\|psylliumManager\|modelManager\|lightManager\|pngObjectManager\|textManager\|subCameraManager" source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ManagerBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs`
Expected: 参照が基底のプロパティとして生えている。生えていないものだけ、そのレイヤーで `XxxManager.instance` を使う

- [ ] **Step 2: ステージライト・レーザー・サイリウムを実装する**

各レイヤーの `Dispose()` の直前へ足す。

```csharp
// StageLightTimelineLayer.cs
        /// <summary>
        /// 本数の定義はタイムライン側にあるので、空にしてから作り直させると実体が消える
        /// </summary>
        public override void ResetOnRemove()
        {
            timeline.stageLightCountList.Clear();
            stageLightManager.SetupLights(timeline.stageLightCountList);
        }

// StageLaserTimelineLayer.cs
        /// <summary>本数の定義を空にして実体を消す</summary>
        public override void ResetOnRemove()
        {
            timeline.stageLaserCountList.Clear();
            stageLaserManager.SetupLasers(timeline.stageLaserCountList);
        }

// PsylliumTimelineLayer.cs
        /// <summary>定義を空にして実体を消す</summary>
        public override void ResetOnRemove()
        {
            timeline.psylliums.Clear();
            psylliumManager.Setup(timeline.psylliums);
        }
```

- [ ] **Step 3: モデル・PNG配置・テキストを実装する**

```csharp
// ModelTimelineLayer.cs
        /// <summary>配置したモデルをすべて消す</summary>
        public override void ResetOnRemove()
        {
            timeline.models.Clear();
            modelManager.SetupModels(timeline.models);
        }

// PngPlacementTimelineLayer.cs
        /// <summary>配置した PNG をすべて消す</summary>
        public override void ResetOnRemove()
        {
            timeline.pngObjects.Clear();
            pngObjectManager.Setup(timeline.pngObjects);
        }

// TextTimelineLayer.cs
        /// <summary>テキストの実体を配列ごと捨て、本数を既定へ戻す</summary>
        public override void ResetOnRemove()
        {
            timeline.textCount = 1;
            textManager.ReleaseTexts();
        }
```

- [ ] **Step 4: ライトを実装する（メインライトは残す）**

`StudioLightManager` は `index 0 = メインライト` を不変条件にしており、メインライトはゲーム側の恒久オブジェクトで消せない。空リストを渡すとメインライトまで `RemoveLight` に回るため、**先頭 1 件だけ残す**。残ったメインライトの値は、このあと `TimelineManager` が断面復元で戻す（`CanRestoreOnRemove = true` のまま）。

```csharp
// LightTimelineLayer.cs
        /// <summary>
        /// 追加ライトだけ消す。index 0 のメインライトはゲーム側の恒久オブジェクトで
        /// 消せないため残し、値は断面復元に任せる
        /// </summary>
        public override void ResetOnRemove()
        {
            if (timeline.lights.Count > 1)
            {
                timeline.lights.RemoveRange(1, timeline.lights.Count - 1);
            }
            lightManager.SetupLights(timeline.lights);
        }
```

- [ ] **Step 5: サブカメラを実装し、古いコメントを直す**

`SubCameraManager` は `MinSubCameraCount` を下回れないので、最小数まで減らすのが初期化になる。

```csharp
// SubCameraTimelineLayer.cs
        /// <summary>
        /// 増やしたサブカメラを最小数まで減らす。
        /// SubCameraManager は MinSubCameraCount を下回れないので、残る 1 台は消せない
        /// </summary>
        public override void ResetOnRemove()
        {
            subCameraManager.SetCameraCount(SubCameraManager.MinSubCameraCount);
        }
```

`SubCameraTimelineLayer.cs:53-55` の `Dispose()` にある次のコメントは新しい挙動と逆なので消す。

```csharp
            // レイヤー削除はシーン上の実体を変えない (全レイヤー共通)。
            // サブカメラの削除は Camera ウィンドウの「サブカメラ数」で行う
```

- [ ] **Step 6: MinSubCameraCount の可視性を確認する**

Run: `grep -n "MinSubCameraCount" source/COM3D2.SceneEditor.Plugin/Timeline/Manager/SubCameraManager.cs`
Expected: `public const int MinSubCameraCount` のように外から参照できる。`private` なら `public` へ上げる

- [ ] **Step 7: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功。`TimelineLayerResetOnRemoveTests` は従属 3 レイヤーの分だけまだ FAIL

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/
git commit -m "feat(timeline): 実体を持つレイヤーの削除で実体ごと破棄する"
```

---

### Task 3: 従属レイヤーの既定値戻しを実装する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelBoneTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelShapeKeyTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs`

**Interfaces:**
- Consumes: `ModelBoneController.initialPosition` / `initialRotation` / `initialScale`
- Consumes: `StudioModelStat.blendShapeController`（`ModelBlendShape.weight`）と `BlendShapeController.FixBlendValues()`
- Consumes: `ModelMaterial.Reset()`（初期値へ戻す既存メソッド）

- [ ] **Step 1: モデルの各コレクション名を確認する**

Run: `grep -n "public List<ModelBoneController>\|public List<ModelMaterial>\|blendShapeController" source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs`
Expected: ボーンとマテリアルのコレクション名、`blendShapeController` の綴りが分かる。以降のコードはこの実名に合わせる

- [ ] **Step 2: モデルボーンを実装する**

```csharp
        /// <summary>
        /// ボーンを読み込んだ時点の姿勢へ戻す。モデル自体は消さない
        /// </summary>
        public override void ResetOnRemove()
        {
            foreach (var model in modelManager.models)
            {
                if (model.bones == null)
                {
                    continue;
                }

                foreach (var bone in model.bones)
                {
                    if (bone == null || bone.transform == null)
                    {
                        continue;
                    }

                    bone.transform.localPosition = bone.initialPosition;
                    bone.transform.localRotation = bone.initialRotation;
                    bone.transform.localScale = bone.initialScale;
                }
            }
        }
```

- [ ] **Step 3: モデルシェイプを実装する**

```csharp
        /// <summary>
        /// シェイプキーの重みを 0 (読込直後の値) へ戻す
        /// </summary>
        public override void ResetOnRemove()
        {
            foreach (var model in modelManager.models)
            {
                var controller = model.blendShapeController;
                if (controller == null)
                {
                    continue;
                }

                foreach (var blendShape in controller.blendShapes)
                {
                    blendShape.weight = 0f;
                }
                controller.FixBlendValues();
            }
        }
```

- [ ] **Step 4: モデルマテリアルを実装する**

`ModelMaterial.Reset()` が初期色・初期値へ戻す処理を持っているので、それを呼ぶだけでよい。

```csharp
        /// <summary>
        /// マテリアルを読み込んだ時点の色・値へ戻す
        /// </summary>
        public override void ResetOnRemove()
        {
            foreach (var model in modelManager.models)
            {
                if (model.materials == null)
                {
                    continue;
                }

                foreach (var material in model.materials)
                {
                    material.Reset();
                }
            }
        }
```

- [ ] **Step 5: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功。`TimelineLayerResetOnRemoveTests` 3 件が PASS、既存テストも全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/
git commit -m "feat(timeline): モデル従属レイヤーの削除で既定値へ戻す"
```

---

### Task 4: TimelineManager から後始末を呼ぶ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`

**Interfaces:**
- Consumes: `ITimelineLayer.ResetOnRemove()`（Task 1）、`TimelineLayerRestorePolicy.CanRestoreOnRemove`（既存）

- [ ] **Step 1: 後始末メソッドを足す**

`RestoreLayerBaseline` の直後へ足す。

```csharp
        /// <summary>
        /// レイヤーを捨てる直前の後始末。実体の始末はレイヤーに任せ、
        /// 値の復元は宣言に従う。ライトのように両方走るレイヤーもある
        /// (追加ライトを消してからメインライトの値を戻す)。
        /// Dispose は _keyFrames と _dummyLastFrame を捨てるので、必ずその前に呼ぶこと
        /// </summary>
        private void CleanupLayerOnRemove(ITimelineLayer layer)
        {
            if (layer == null)
            {
                return;
            }

            // 後始末の失敗で呼び出し元を止めない。ここで抜けると
            // レイヤーの Dispose やタイムラインの破棄が実行されず中途半端な状態になる
            try
            {
                layer.ResetOnRemove();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.LogError("レイヤーの初期化に失敗しました: {0}", layer.layerName);
            }

            RestoreLayerBaseline(layer);
        }
```

- [ ] **Step 2: RemoveLayer の呼び出しを差し替える**

変更前:
```csharp
            // レイヤーが書き換えていた値を、そのレイヤーが生まれた時点へ戻す。
            // 実体 (ライトやモデルの個数) は触らない
            RestoreLayerBaseline(layer);
```

変更後:
```csharp
            // レイヤーが書き換えていたものを片付ける。
            // 実体ごと捨てるか値を戻すかはレイヤーごとに決まる
            CleanupLayerOnRemove(layer);
```

- [ ] **Step 3: ResetTimelineState の呼び出しを差し替える**

変更前:
```csharp
        private void RestoreAllLayerBaselines()
        {
            foreach (var layer in layers)
            {
                RestoreLayerBaseline(layer);
            }
        }
```

変更後:
```csharp
        private void CleanupAllLayersOnRemove()
        {
            foreach (var layer in layers)
            {
                CleanupLayerOnRemove(layer);
            }
        }
```

`ResetTimelineState` の呼び出し行を `CleanupAllLayersOnRemove();` へ直し、直前のコメントも「断面を書き戻す」から「後始末する」へ直す。

- [ ] **Step 4: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "refactor(timeline): レイヤー削除の後始末を一本化する"
```

---

### Task 5: 実機で挙動を確認する

**Files:** なし（検証のみ）

ビルド済み DLL を `%COM3D25_DIR%\Sybaris\UnityInjector\` へ反映してゲームを再起動する（反映はユーザーに依頼する）。各手順のあとに `tail_log` で例外が出ていないかを見る。

- [ ] **Step 1: ステージライトで確認する**

1. タイムラインを新規作成し、`ステージライト` レイヤーを追加する
2. ライブ演出ウィンドウでコントローラを 1 本足し、色を変えてキーを打つ
3. `-` で `ステージライト` レイヤーを削除する

Expected: ステージライトの実体が消える（本数 0）

- [ ] **Step 2: モデルで確認する**

1. `モデル` レイヤーを追加し、モデルを 1 体配置して移動キーを打つ
2. `モデル` レイヤーを削除する

Expected: モデルがシーンから消える

- [ ] **Step 3: モデルレイヤー削除がモデルボーンレイヤーを巻き込まないか確認する**

1. モデルを配置し、`モデル` と `モデルボーン` の両方のレイヤーを作る
2. ボーンを 1 本チェックしてキーを打つ
3. `モデル` レイヤーだけを削除する

Expected: モデルが消え、`モデルボーン` レイヤーは残る。`OnModelRemoved` 経由で例外が出ない

- [ ] **Step 4: モデルボーンで確認する**

1. モデルを配置し、`モデルボーン` レイヤーを追加する
2. ボーンを 1 本チェックして回転キーを打つ
3. `モデルボーン` レイヤーを削除する

Expected: ボーンが読込時の姿勢へ戻る。**モデル自体は消えない**

- [ ] **Step 5: ライトで確認する**

1. `ライト` レイヤーを追加し、ライトを 1 灯追加する（メインライトと合わせて 2 灯）
2. メインライトの色を赤にしてキーを打つ
3. `ライト` レイヤーを削除する

Expected: 追加した 1 灯が消え、メインライトは残ってレイヤー追加時の色へ戻る。シーンが真っ暗にならない

- [ ] **Step 6: アンロードと別タイムラインの読込で確認する**

1. ステージライトとモデルを置いた状態でタイムラインをアンロードする
2. 再度タイムラインを開き、ステージライトを置いてから、ステージライトを持たない別タイムラインを読み込む

Expected: 1 でどちらも消える。2 で前のタイムラインのステージライトが残らない

- [ ] **Step 7: undo で戻るか確認する**

Step 1 の削除直後に Ctrl+Z する。

Expected: レイヤーとステージライトの実体が復活する。戻らない場合は結果を記録し、別課題として切り出す

- [ ] **Step 8: 値のみレイヤーの退行が無いか確認する**

`メイド表情` レイヤーを追加 → `ほほ` をチェックしてキー → 削除する。

Expected: `ほほ` が素の値へ戻り、メイドは消えない

- [x] **Step 9: 結果を記録する**

### 確認結果（2026-09-14、COM3D25 実機・devbridge 経由）

検証は `eval_csharp` から `TimelineManager` の API を直接叩いて行った（GUI 操作と同じ経路）。
各手順のあと `tail_log` を見たが、**例外は 1 件も出ていない**。

| Step | 内容 | 結果 | 実測 |
|---|---|---|---|
| 1 | ステージライト削除 | PASS | コントローラ 1 → 0、`stageLightCountList` [1] → [] |
| 2 | モデル削除 | PASS | モデル 51 → 0 |
| 3 | モデル削除がモデルボーンを巻き込まない | PASS | `ModelBoneTimelineLayer` / `ModelMaterialTimelineLayer` は残存。`OnModelRemoved` で例外なし |
| 4 | モデルボーン削除 | PASS | モデル 51 体はそのまま残り、`ModelTimelineLayer` も無傷 |
| 5 | ライト削除 | PASS | ライト 2 → 1（追加分のみ消える）。メインライトは残り、色が赤 → 白（レイヤー追加時の値）へ復元 |
| 6 | アンロードと別タイムライン読込 | PASS | アンロードで 4/6/1/51 → 0、ライトは 3 → 1。別タイムライン読込でも前のステージライトが残らない |
| 7 | undo で戻る | PASS | レイヤーとステージライトの実体（コントローラ 1・ライト 1）が復活。モデル 51 体も同様に復活 |
| 8 | 値のみレイヤーの退行なし | PASS | `メイド表情` 削除でメイドは消えず、断面復元が走る |

検証後は元のタイムラインを再読込し、レイヤー 17 枚 / モデル 51 体 / ステージライト 4 / レーザー 6 / サイリウム 1 / ライト 3 の状態へ完全に戻したことを確認済み。

---

### Task 6: ドキュメントを更新する

**Files:**
- Modify: `docs-site/timeline/layers.md`
- Modify: `docs-site/timeline/files.md`
- Modify: `docs-site/timeline/layers-camera.md`
- Modify: `docs-site/timeline/layers-background.md`

- [ ] **Step 1: layers.md の削除の説明を書き換える**

変更前:
```markdown
- レイヤーを削除すると、そのレイヤーが動かしていた値は**レイヤーを追加した時点**の状態へ戻ります
- 読み込んだタイムラインに元からあったレイヤーは、**読み込む直前**の状態へ戻ります
- チェックで対象を選ぶレイヤー（`メイド表情` / `メイドマテリアル` / `モデルボーン` / `モデルシェイプ` / `モデルマテリアル`）は、レイヤー追加後にチェックした項目については**チェックを入れた時点**の値へ戻ります
- シーン上の実体（サブカメラ、地面、モデルなど）の数は変わりません。戻るのは値だけです
- `メイドアニメ` / `メイドボイス` / `効果音` / `メイド衣装` は値を戻しません（音の再生や着替えをやり直してしまうため）
```

変更後:
```markdown
- レイヤーを削除すると、そのレイヤーで増やした実体（ステージライト、レーザー、サイリウム、モデル、PNG、テキスト）は**まとめて消えます**。`サブカメラ` は最小数まで減ります
- `ライト` は追加したライトだけが消え、メインライトは残ってレイヤーを追加した時点の値へ戻ります
- `モデルボーン` / `モデルシェイプ` / `モデルマテリアル` はモデルを消さず、姿勢・シェイプ・マテリアルを**読み込んだ時点**の値へ戻します
- 実体を増やさないレイヤー（メイド系、`カメラ`、`背景`、`背景色`、`背景モデル`、`ポストエフェクト` など）は、値だけを**レイヤーを追加した時点**（読み込んだタイムラインに元からあったレイヤーなら**読み込む直前**）の状態へ戻します
- チェックで対象を選ぶメイド系レイヤー（`メイド表情` / `メイドシェイプ` / `メイドマテリアル`）は、レイヤー追加後にチェックした項目については**チェックを入れた時点**の値へ戻ります
- `メイドアニメ` / `メイドボイス` / `効果音` / `メイド衣装` は何もしません（音の再生や着替えをやり直してしまうため）
```

- [ ] **Step 2: files.md の読込・アンロードの説明を直す**

変更前:
```markdown
- アンロードすると、各レイヤーが動かしていた値はレイヤーを追加した時点（読み込んだタイムラインなら読み込む直前）の状態へ戻ります（[レイヤーの追加と削除](/timeline/layers#レイヤーの追加と削除)）
```

変更後:
```markdown
- アンロードすると、タイムラインで増やした実体（ステージライト、モデルなど）は消え、それ以外のレイヤーは値だけが読み込む直前の状態へ戻ります（[レイヤーの追加と削除](/timeline/layers#レイヤーの追加と削除)）
```

- [ ] **Step 3: layers-camera.md の但し書きを直す**

変更前:
```markdown
- `サブカメラ` レイヤーを削除するとビューポートや画角はレイヤー追加時の値へ戻りますが、サブカメラの実体は残ります。台数を減らすには Camera ウィンドウの `サブカメラ数` を使ってください
```

変更後:
```markdown
- `サブカメラ` レイヤーを削除すると、増やしたサブカメラは最小数まで減ります。残る 1 台は消えません
```

- [ ] **Step 4: layers-background.md へ PNG の但し書きを足す**

`PNG配置` の節の箇条書きへ 1 行足す。地面は `背景色` レイヤー（値のみ）なので現行のままでよい。

```markdown
- `PNG配置` レイヤーを削除すると、配置した PNG はすべて消えます
```

- [ ] **Step 5: docs-review スキルでレビューする**

Run: `docs-review` スキル
Expected: 指摘なし、または指摘を取り込んで書き直し済み

- [ ] **Step 6: コミット**

```bash
git add docs-site/timeline/
git commit -m "docs(timeline): レイヤー削除時に実体ごと消えることを記載する"
```

---

## 完了条件

- 2 構成（COM3D2 / COM3D25）ともビルドが通る
- `dotnet test` が全 PASS
- Task 5 の実機確認 8 項目がすべて期待どおり
- `docs-site/timeline/` の記述が実装と一致している

## 先行実装との関係

- `e9977d7`（対象数の増減を見た断面の積み増し）は残す。`メイド表情` / `メイドシェイプ` / `メイドマテリアル` は値のみレイヤーのまま対象が増えるため、引き続き必要
- `TimelineLayerBaselineStore` も残す。断面を控える対象が `CanRestoreOnRemove = true` のレイヤーだけに狭まる

## レビュー却下メモ

- **`背景モデル` / `背景モデルマテリアル` も実体を後から足せるので対象に含めるべきでは** — 対象外のまま。`BGModelManager.AddModel` が足すのは背景シーンに元からある子オブジェクトの登録で、プラグインが一から作った実体ではない。消すと背景そのものを壊す。理由は設計メモへ記載した
- **`TimelineLayerRemoveBehavior` 列挙で 3 分類する** — 却下。「メインライトだけ値を戻す」を表すには実体の後始末と値の復元が直交している必要があり、列挙 1 本では `ライト` が表せない。既存の `CanRestoreOnRemove` と `ResetOnRemove` の 2 軸で足りる
