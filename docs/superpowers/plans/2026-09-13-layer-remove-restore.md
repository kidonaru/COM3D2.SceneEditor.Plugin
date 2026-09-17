# レイヤー削除時のシーン状態復元 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインレイヤーを削除したとき、そのレイヤーが書き換えていたシーンの値を「レイヤーが生まれた時点」の状態へ戻す。

**Architecture:** 全レイヤーが持つ `UpdateFrame`（シーン→キー）と `FromXml` + `CreateAndApplyAnm`（キー→シーン）を使い、レイヤー種別ごとの個別実装なしで退避・復元する。レイヤー誕生時（読込 / 新規作成 / 追加）に 1 キー分の `FrameXml` を `TimelineManager` 所有のストアへ退避し、`RemoveLayer` で `Dispose` の直前に流し込む。書き換え範囲が生存中に広がる追跡系レイヤーだけは、項目が追跡対象に入る瞬間に断面をボーン単位で積み増す。復元可否は `TimelineLayerDescAttribute` の宣言で持つ。

**Tech Stack:** C#（COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成ビルド）、xunit 2.9（net48、`source/COM3D2.SceneEditor.Plugin.Tests`）、MSBuild 直叩き。

**Spec:** `docs/superpowers/specs/2026-09-13-layer-remove-restore-design.md`

## Global Constraints

- コードのコメントとエラーログメッセージは**日本語**で書く。
- **2 構成ともビルドする**。COM3D2 構成は .NET 3.5 なので `Tuple`、入力 5 個以上の `Func<>` / `Action<>`、`CallerMemberName` は使えない。
- ビルドは `debug.bat` を使わない（実機へ DLL をコピーするため）。MSBuild を直接叩く。
- Git Bash から MSBuild を叩くときは `export MSYS2_ARG_CONV_EXCL="*"` が必須（無いと `/p:` がパス変換されて MSB1008）。
- ビルド順は **COM3D2 → COM3D25 → `dotnet test`**。COM3D2 構成のビルドが `bin/Debug/COM3D25/` の DLL を消すため。
- プラグイン csproj は旧形式で `<Compile Include>` を明示列挙する。新規 `.cs` は csproj への追記が必須（テストプロジェクトは SDK 形式なので追記不要）。
- `deploy.bat` / `release.bat` は実行しない。
- 復元対象外レイヤー: `MotionTimelineLayer` / `VoiceTimelineLayer` / `SeTimelineLayer` / `DressTimelineLayer`。
- 追跡系レイヤー（`TimelineLayerBaseTracking` を使う `ModelBoneTimelineLayer` / `ModelShapeKeyTimelineLayer` / `ModelMaterialTimelineLayer` / `MaidMaterialTimelineLayer` / `MorphTimelineLayer`）は積み増し退避が要る。

**共通ビルドコマンド**（以降の手順で「共通ビルド」と書いたらこれ）:

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
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs`（変更） | `TimelineLayerDescAttribute` に `CanRestoreOnRemove` を追加。復元可否の宣言をレイヤー定義のすぐ隣に置く |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/{Motion,Voice,Se,Dress}TimelineLayer.cs`（変更） | 属性に `CanRestoreOnRemove = false` を付ける |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRestorePolicy.cs`（新規） | 型から復元可否を読む純粋ロジック。Unity 実体に触れないのでテストできる |
| `source/COM3D2.SceneEditor.Plugin/TimelineLayerBaselineStore.cs`（新規） | `(レイヤー型, スロット番号)` → `FrameXml` の保管庫。置き換え記録とボーン単位の先勝ち積み増しを持つ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（変更） | 退避の呼び出し、`ResetTimelineState` でのクリア、`RemoveLayer` での復元 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs`（変更） | 追跡対象が増えた瞬間に断面を積み増す |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（変更） | 新規 2 ファイルの `Compile Include` |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs`（新規） | 全レイヤー型の復元可否が期待どおりか |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerBaselineStoreTests.cs`（新規） | 保管庫の入出力と積み増し |
| `docs-site/timeline/layers.md` / `layers-camera.md` / `layers-background.md`（変更） | 削除時の挙動の記述 |

---

### Task 1: 復元可否をレイヤー定義で宣言する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs:22-34`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:25`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/VoiceTimelineLayer.cs:8`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs:7`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/DressTimelineLayer.cs:8`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRestorePolicy.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs`

**Interfaces:**
- Produces: `COM3D2.MotionTimelineEditor.Plugin.TimelineLayerRestorePolicy.CanRestoreOnRemove(System.Type layerType) -> bool`
- Produces: `TimelineLayerDescAttribute.CanRestoreOnRemove { get; set; }`（既定 `true`、名前付きプロパティなので既存の 29 個の属性指定は無変更で通る）

- [x] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs` を新規作成する。
`TimelineLayerCategoryTests` と同じ「全具象レイヤー型を走査して期待表と突き合わせる」形にして、レイヤーを増やしたときに表の更新漏れが落ちるようにする。

```csharp
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRestorePolicyTests
    {
        // 設計書「決定事項」の表。レイヤー型を増やしたらここにも足すこと
        private static readonly HashSet<string> EXPECTED_EXCLUDED = new HashSet<string>
        {
            "MotionTimelineLayer",
            "VoiceTimelineLayer",
            "SeTimelineLayer",
            "DressTimelineLayer",
        };

        [Fact]
        public void 復元対象外のレイヤーだけがfalseを返す()
        {
            var types = TimelineLayerTestUtils.GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = !EXPECTED_EXCLUDED.Contains(type.Name);
                var actual = TimelineLayerRestorePolicy.CanRestoreOnRemove(type);
                Assert.True(expected == actual,
                    type.Name + " の復元可否が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のレイヤー型がすべて実在する()
        {
            var names = TimelineLayerTestUtils.GetConcreteLayerTypes()
                .Select(t => t.Name).ToList();
            foreach (var name in EXPECTED_EXCLUDED)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void 型がnullなら復元しない()
        {
            Assert.False(TimelineLayerRestorePolicy.CanRestoreOnRemove(null));
        }

        [Fact]
        public void レイヤーでない型は復元しない()
        {
            Assert.False(TimelineLayerRestorePolicy.CanRestoreOnRemove(typeof(string)));
        }
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineLayerRestorePolicyTests`
Expected: コンパイルエラー（`TimelineLayerRestorePolicy` が存在しない）

- [x] **Step 3: 属性にプロパティを足す**

`Timeline/TimelineLayerInfo.cs` の `TimelineLayerDescAttribute` へ追記する。

```csharp
    public class TimelineLayerDescAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public int Priority { get; set; }
        public TimelineLayerCategory Category { get; set; }

        /// <summary>
        /// レイヤー削除時に「レイヤーが生まれた時点」のシーン状態へ戻すか。
        /// 適用が再生トリガや非同期の着替えを起こすレイヤーは false にする
        /// </summary>
        public bool CanRestoreOnRemove { get; set; } = true;

        public TimelineLayerDescAttribute(string displayName, int priority, TimelineLayerCategory category)
        {
            DisplayName = displayName;
            Priority = priority;
            Category = category;
        }
    }
```

- [x] **Step 4: 対象外レイヤーへ宣言を付ける**

4 ファイルの属性行を書き換える。行の前後は変えない。

```csharp
// Timeline/TimelineLayer/MotionTimelineLayer.cs:25
    [TimelineLayerDesc("メイドアニメ", 0, TimelineLayerCategory.Maid, CanRestoreOnRemove = false)]

// Timeline/TimelineLayer/VoiceTimelineLayer.cs:8
    [TimelineLayerDesc("メイドボイス", 14, TimelineLayerCategory.Maid, CanRestoreOnRemove = false)]

// Timeline/TimelineLayer/SeTimelineLayer.cs:7
    [TimelineLayerDesc("効果音", 51, TimelineLayerCategory.Other, CanRestoreOnRemove = false)]

// Timeline/TimelineLayer/DressTimelineLayer.cs:8
    [TimelineLayerDesc("メイド衣装", 15, TimelineLayerCategory.Maid, CanRestoreOnRemove = false)]
```

- [x] **Step 5: 判定クラスを作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRestorePolicy.cs` を新規作成する。
`GetCustomAttribute<T>` は .NET 3.5 に無いため、`MTEUtils/Extensions.cs:187` の自前拡張を使う（`TimelineLayerInfo` と同じ作法）。

```csharp
using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// レイヤー削除時にシーン状態を復元してよいかの判定。
    /// 判定の根拠は各レイヤーの TimelineLayerDesc 属性にあり、ここはその読み手。
    /// Unity 実体に触れない純粋ロジックなのでテストから直接呼べる
    /// </summary>
    public static class TimelineLayerRestorePolicy
    {
        /// <summary>
        /// 属性が無い型は復元しない。宣言漏れのレイヤーでシーンを触ると
        /// 「何が戻ったのか分からない」事故になるため、安全側へ倒す
        /// </summary>
        public static bool CanRestoreOnRemove(Type layerType)
        {
            if (layerType == null)
            {
                return false;
            }

            var attr = layerType.GetCustomAttribute<TimelineLayerDescAttribute>();
            return attr != null && attr.CanRestoreOnRemove;
        }
    }
}
```

- [x] **Step 6: csproj へ追記する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\TimelineLayerInfo.cs" />` の隣へ 1 行足す。

```xml
    <Compile Include="Timeline\TimelineLayerRestorePolicy.cs" />
```

- [x] **Step 7: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、`TimelineLayerRestorePolicyTests` の 4 テストが PASS、既存テストに退行なし

- [x] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerInfo.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayerRestorePolicy.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/VoiceTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/DressTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerRestorePolicyTests.cs
git commit -m "feat(timeline): レイヤー削除時の復元可否を属性で宣言する"
```

---

### Task 2: 誕生時断面の保管庫を作る

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerBaselineStore.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerBaselineStoreTests.cs`

**Interfaces:**
- Consumes: `COM3D2.MotionTimelineEditor.Plugin.FrameXml`（`Timeline/FrameXml.cs`。`int frameNo` と `List<BoneXml> bones`。`bones` は既定 `null`）、`BoneXml`（`TransformXml transform`）、`TransformXml`（`string name`）
- Produces: `COM3D2.SceneEditor.Plugin.TimelineLayerBaselineStore`
  - `void Set(System.Type layerType, int slotNo, FrameXml frameXml)` — 置き換え
  - `void Merge(System.Type layerType, int slotNo, FrameXml frameXml, List<string> boneNames)` — `boneNames` に含まれ、かつ未記録のボーンだけ足す（先勝ち）
  - `bool TryGet(System.Type layerType, int slotNo, out FrameXml frameXml)`
  - `void Clear()`
  - `int count { get; }`

- [x] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerBaselineStoreTests.cs` を新規作成する。

```csharp
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerBaselineStoreTests
    {
        private static FrameXml CreateFrame(params string[] boneNames)
        {
            return new FrameXml
            {
                frameNo = 0,
                bones = boneNames
                    .Select(name => new BoneXml { transform = new TransformXml { name = name } })
                    .ToList(),
            };
        }

        private static List<string> NamesOf(FrameXml frame)
        {
            return frame.bones.Select(bone => bone.transform.name).ToList();
        }

        [Fact]
        public void 記録した断面をそのまま取り出せる()
        {
            var store = new TimelineLayerBaselineStore();
            var frame = CreateFrame("Light0");

            store.Set(typeof(LightTimelineLayer), 0, frame);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
            Assert.Same(frame, actual);
        }

        [Fact]
        public void スロット番号が違えば別の断面になる()
        {
            var store = new TimelineLayerBaselineStore();
            var frame0 = CreateFrame("A");
            var frame1 = CreateFrame("B");

            store.Set(typeof(MorphTimelineLayer), 0, frame0);
            store.Set(typeof(MorphTimelineLayer), 1, frame1);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 1, out actual));
            Assert.Same(frame1, actual);
        }

        [Fact]
        public void Setは同じキーを上書きする()
        {
            var store = new TimelineLayerBaselineStore();
            var older = CreateFrame("A");
            var newer = CreateFrame("B");

            store.Set(typeof(LightTimelineLayer), 0, older);
            store.Set(typeof(LightTimelineLayer), 0, newer);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
            Assert.Same(newer, actual);
            Assert.Equal(1, store.count);
        }

        [Fact]
        public void 未記録のキーは取り出せない()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(LightTimelineLayer), 0, CreateFrame("A"));

            FrameXml actual;
            Assert.False(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Null(actual);
            Assert.False(store.TryGet(typeof(LightTimelineLayer), 1, out actual));
        }

        [Fact]
        public void nullは記録しない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Set(null, 0, CreateFrame("A"));
            store.Set(typeof(LightTimelineLayer), 0, null);

            Assert.Equal(0, store.count);

            FrameXml actual;
            Assert.False(store.TryGet(null, 0, out actual));
        }

        [Fact]
        public void クリアで全件消える()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(LightTimelineLayer), 0, CreateFrame("A"));
            store.Set(typeof(MorphTimelineLayer), 1, CreateFrame("B"));

            store.Clear();

            Assert.Equal(0, store.count);
            FrameXml actual;
            Assert.False(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
        }

        [Fact]
        public void Mergeは指定した未記録のボーンだけ足す()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(MorphTimelineLayer), 0, CreateFrame("hoho"));

            // 追加指定は eyeclose だけ。mayu は指定外なので足さない
            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho", "eyeclose", "mayu"), new List<string> { "eyeclose" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Equal(new List<string> { "hoho", "eyeclose" }, NamesOf(actual));
        }

        [Fact]
        public void Mergeは記録済みのボーンを上書きしない()
        {
            var store = new TimelineLayerBaselineStore();
            var original = CreateFrame("hoho");
            var originalBone = original.bones[0];
            store.Set(typeof(MorphTimelineLayer), 0, original);

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho"), new List<string> { "hoho" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Single(actual.bones);
            Assert.Same(originalBone, actual.bones[0]);
        }

        [Fact]
        public void 断面が無いキーへのMergeは指定分だけで新規作成する()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho", "eyeclose"), new List<string> { "eyeclose" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Equal(new List<string> { "eyeclose" }, NamesOf(actual));
        }

        [Fact]
        public void Mergeは一致するボーンが無ければ断面を作らない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho"), new List<string> { "mayu" });

            Assert.Equal(0, store.count);
        }

        [Fact]
        public void Mergeは空指定や欠損入力で何もしない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0, CreateFrame("A"), new List<string>());
            store.Merge(typeof(MorphTimelineLayer), 0, null, new List<string> { "A" });
            store.Merge(typeof(MorphTimelineLayer), 0, CreateFrame("A"), null);
            store.Merge(null, 0, CreateFrame("A"), new List<string> { "A" });

            Assert.Equal(0, store.count);
        }
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineLayerBaselineStoreTests`
Expected: コンパイルエラー（`TimelineLayerBaselineStore` が存在しない）

- [x] **Step 3: 保管庫を実装する**

`source/COM3D2.SceneEditor.Plugin/TimelineLayerBaselineStore.cs` を新規作成する。
キーを `Tuple` にしないこと（COM3D2 構成の .NET 3.5 に無い）。型ごとの入れ子辞書にする。

```csharp
using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤーが生まれた時点のシーン断面を (レイヤー型, スロット番号) で保管する。
    ///
    /// レイヤーのインスタンスには持たせない。undo/redo の TimelineManager.UpdateTimeline が
    /// ClearTimeline → FromXml → LayerInit を通すため、レイヤーは差し替えのたびに別物になる。
    /// 断面の生存期間はタイムラインのセッション単位で、ResetTimelineState でまとめて捨てる
    /// </summary>
    public class TimelineLayerBaselineStore
    {
        private readonly Dictionary<Type, Dictionary<int, MTEP.FrameXml>> _map
            = new Dictionary<Type, Dictionary<int, MTEP.FrameXml>>();

        /// <summary>保管している断面の総数 (テストと調査用)</summary>
        public int count
        {
            get
            {
                var total = 0;
                foreach (var slotMap in _map.Values)
                {
                    total += slotMap.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// 誕生時の断面を記録する。同じキーは置き換える
        /// (削除したレイヤーを追加し直したときに基準を取り直すため)
        /// </summary>
        public void Set(Type layerType, int slotNo, MTEP.FrameXml frameXml)
        {
            if (layerType == null || frameXml == null)
            {
                return;
            }
            GetOrCreateSlotMap(layerType)[slotNo] = frameXml;
        }

        /// <summary>
        /// 追跡系レイヤーが対象を増やしたときの積み増し。
        /// boneNames に挙がっていて、かつまだ記録の無いボーンだけを足す (先勝ち)。
        /// 記録済みのボーンを上書きしないのは、誕生時の値こそが戻すべき基準だから
        /// </summary>
        public void Merge(
            Type layerType, int slotNo, MTEP.FrameXml frameXml, List<string> boneNames)
        {
            if (layerType == null || frameXml == null || frameXml.bones == null
                || boneNames == null || boneNames.Count == 0)
            {
                return;
            }

            MTEP.FrameXml baseline;
            TryGet(layerType, slotNo, out baseline);

            // 空の断面を作らないよう、足すボーンを決めてから登録する
            var knownNames = new HashSet<string>();
            if (baseline != null && baseline.bones != null)
            {
                foreach (var bone in baseline.bones)
                {
                    if (bone != null && bone.transform != null)
                    {
                        knownNames.Add(bone.transform.name);
                    }
                }
            }

            var targetNames = new HashSet<string>(boneNames);
            var addedBones = new List<MTEP.BoneXml>();
            foreach (var bone in frameXml.bones)
            {
                if (bone == null || bone.transform == null)
                {
                    continue;
                }
                var name = bone.transform.name;
                if (!targetNames.Contains(name) || !knownNames.Add(name))
                {
                    continue;
                }
                addedBones.Add(bone);
            }

            if (addedBones.Count == 0)
            {
                return;
            }

            if (baseline == null)
            {
                baseline = new MTEP.FrameXml { frameNo = 0, bones = new List<MTEP.BoneXml>() };
                GetOrCreateSlotMap(layerType)[slotNo] = baseline;
            }
            else if (baseline.bones == null)
            {
                baseline.bones = new List<MTEP.BoneXml>();
            }

            baseline.bones.AddRange(addedBones);
        }

        public bool TryGet(Type layerType, int slotNo, out MTEP.FrameXml frameXml)
        {
            frameXml = null;

            Dictionary<int, MTEP.FrameXml> slotMap;
            if (layerType == null || !_map.TryGetValue(layerType, out slotMap))
            {
                return false;
            }
            return slotMap.TryGetValue(slotNo, out frameXml);
        }

        public void Clear()
        {
            _map.Clear();
        }

        private Dictionary<int, MTEP.FrameXml> GetOrCreateSlotMap(Type layerType)
        {
            Dictionary<int, MTEP.FrameXml> slotMap;
            if (!_map.TryGetValue(layerType, out slotMap))
            {
                slotMap = new Dictionary<int, MTEP.FrameXml>();
                _map[layerType] = slotMap;
            }
            return slotMap;
        }
    }
}
```

- [x] **Step 4: csproj へ追記する**

`<Compile Include="TimelineLayerGate.cs" />` の隣へ 1 行足す。

```xml
    <Compile Include="TimelineLayerBaselineStore.cs" />
```

- [x] **Step 5: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、`TimelineLayerBaselineStoreTests` の 11 テストが PASS

- [x] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineLayerBaselineStore.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerBaselineStoreTests.cs
git commit -m "feat(timeline): レイヤー誕生時のシーン断面を保管する仕組みを追加する"
```

---

### Task 3: TimelineManager へ退避と復元を組み込む

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`
  - フィールド追加（`:36` 付近）
  - `ResetTimelineState`（`:323-329`）
  - `CreateNewTimeline`（`:367-369`）
  - `LoadTimeline`（`:426`）
  - `ChangeActiveLayer`（`:1680-1681`）
  - `RemoveLayer`（`:1701-1733`）

**Interfaces:**
- Consumes: `TimelineLayerBaselineStore.Set / Merge / TryGet / Clear`（Task 2）、`TimelineLayerRestorePolicy.CanRestoreOnRemove`（Task 1）
- Consumes: `ITimelineLayer.CreateFrame(int)` / `UpdateFrame(FrameData)` / `FromXml(TimelineLayerXml)` / `CreateAndApplyAnm()` / `layerType` / `slotNo` / `layerName`
- Produces: `TimelineManager.CaptureLayerBaseline(ITimelineLayer)`（private）
- Produces: `TimelineManager.CaptureAllLayerBaselines()`（private）
- Produces: `TimelineManager.MergeLayerBaseline(ITimelineLayer layer, FrameXml frameXml, List<string> boneNames)`（**public**。Task 4 で `TimelineLayerBaseTracking` から呼ぶ）
- Produces: `TimelineManager.RestoreLayerBaseline(ITimelineLayer)`（private）

> 本タスクは Unity 実体に触れるためユニットテストで検証できない。ビルド通過と Task 5 の実機検証が検証手段になる。

- [x] **Step 1: ストアのフィールドを足す**

`TimelineManager.cs` の `public int timelineSessionId { get; private set; }` の直後（`:36` 付近）へ追記する。
`SE` は同ファイル冒頭（`:16`）で `using SE = SceneEditor.Plugin;` として別名定義済み。

```csharp
        /// <summary>
        /// レイヤーが生まれた時点のシーン断面。レイヤー削除時にここへ戻す。
        /// タイムラインのセッション単位で捨てる (ResetTimelineState)
        /// </summary>
        private readonly SE.TimelineLayerBaselineStore _layerBaselineStore
            = new SE.TimelineLayerBaselineStore();
```

- [x] **Step 2: 退避・復元のヘルパーを足す**

`RemoveLayer` の直前（`:1700` 付近）へ 4 メソッドを追加する。

```csharp
        /// <summary>
        /// レイヤーが今見ているシーンの値を 1 キー分の断面として控える。
        /// 呼ぶのは Init 済み・まだ適用前の一点だけ。適用後に呼ぶと
        /// 「タイムラインが書いた値」を基準にしてしまい復元の意味が無くなる
        /// </summary>
        private void CaptureLayerBaseline(ITimelineLayer layer)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            var frame = layer.CreateFrame(0);
            layer.UpdateFrame(frame);
            _layerBaselineStore.Set(layer.layerType, layer.slotNo, frame.ToXml());
        }

        /// <summary>読込・新規作成の直後に全レイヤー分の断面を控える</summary>
        private void CaptureAllLayerBaselines()
        {
            foreach (var layer in layers)
            {
                CaptureLayerBaseline(layer);
            }
        }

        /// <summary>
        /// 追跡系レイヤーが対象を増やしたときの積み増し。
        /// 追加された項目はまだこのレイヤーに駆動されていないので、
        /// 今の値がそのまま「レイヤーが触り始める直前」の基準になる。
        /// TimelineLayerBaseTracking.AddTrackedFirstFrameKeys から呼ぶ
        /// </summary>
        public void MergeLayerBaseline(
            ITimelineLayer layer, FrameXml frameXml, List<string> boneNames)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            _layerBaselineStore.Merge(layer.layerType, layer.slotNo, frameXml, boneNames);
        }

        /// <summary>
        /// 控えた断面をシーンへ書き戻す。キーを 1 本だけにしてから anm を組み直す。
        /// 末尾には _dummyLastFrame が積まれるので区間が 1 本でき、
        /// 現在フレームがどこでも定数値として適用される。
        /// Dispose は _keyFrames と _dummyLastFrame を捨てるので、必ずその前に呼ぶこと
        /// </summary>
        private void RestoreLayerBaseline(ITimelineLayer layer)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            FrameXml frameXml;
            if (!_layerBaselineStore.TryGet(layer.layerType, layer.slotNo, out frameXml))
            {
                // undo/redo で生えたレイヤーには断面が無い。シーンは触らず現状のままにする
                MTEUtils.LogDebug("復元する断面がありません: {0}", layer.layerName);
                return;
            }

            // FromXml は keyFrames しか読まないので className / slotNo は設定しない
            var xml = new TimelineLayerXml();
            xml.keyFrames.Add(frameXml);

            layer.FromXml(xml);
            layer.CreateAndApplyAnm();
        }
```

- [x] **Step 3: RemoveLayer から復元を呼ぶ**

`RemoveLayer`（`:1701`）の `layer.Dispose();` の直前へ足す。前後の行は変えない。

変更前:
```csharp
            selectedBones.RemoveWhere(bone => bone.parentLayer == layer);

            layer.Dispose();
```

変更後:
```csharp
            selectedBones.RemoveWhere(bone => bone.parentLayer == layer);

            // レイヤーが書き換えていた値を、そのレイヤーが生まれた時点へ戻す。
            // 実体 (ライトやモデルの個数) は触らない
            RestoreLayerBaseline(layer);

            layer.Dispose();
```

- [x] **Step 4: セッション切替で断面を捨てる**

`ResetTimelineState`（`:323`）へ 1 行足す。

変更前:
```csharp
            ClearTimeline();
            historyManager.ClearHistory();
            timelineSessionId++;
            currentLayerIndex = 0;
```

変更後:
```csharp
            ClearTimeline();
            historyManager.ClearHistory();
            _layerBaselineStore.Clear();
            timelineSessionId++;
            currentLayerIndex = 0;
```

- [x] **Step 5: 新規作成で断面を控える**

`CreateNewTimeline`（`:367`）の `_timeline.LayerInit();` と `CreateAndApplyAnmAll();` の間へ足す。

変更前:
```csharp
            _timeline.LayerInit();

            CreateAndApplyAnmAll();
```

変更後:
```csharp
            _timeline.LayerInit();
            CaptureAllLayerBaselines();

            CreateAndApplyAnmAll();
```

- [x] **Step 6: 読込で断面を控える**

`LoadTimeline`（`:426`）の `_timeline.LayerInit();` の直後へ足す。`using` ブロックの中。

変更前:
```csharp
                _timeline.LayerInit();

                _usingLayerInfoList = null;
```

変更後:
```csharp
                _timeline.LayerInit();
                CaptureAllLayerBaselines();

                _usingLayerInfoList = null;
```

> `UpdateTimeline`（`:488`、undo/redo）には**足さない**。この経路が走る時点でシーンは既にタイムラインに駆動されており、そこで取った断面は基準として誤りになる。

- [x] **Step 7: レイヤー追加で断面を控える**

`ChangeActiveLayer`（`:1680`）の `newLayer.Init();` と `newLayer.CreateAndApplyAnm();` の間へ足す。

変更前:
```csharp
            newLayer.Init();
            newLayer.CreateAndApplyAnm();
```

変更後:
```csharp
            newLayer.Init();
            CaptureLayerBaseline(newLayer);
            newLayer.CreateAndApplyAnm();
```

- [x] **Step 8: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、既存テスト全 PASS

- [x] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "feat(timeline): レイヤー削除時にレイヤー誕生時の状態へ復元する"
```

---

### Task 4: 追跡系レイヤーの断面を積み増す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs:168-186`

**Interfaces:**
- Consumes: `TimelineManager.MergeLayerBaseline(ITimelineLayer, FrameXml, List<string>)`（Task 3）

**背景:** `ModelBoneTimelineLayer` / `ModelShapeKeyTimelineLayer` / `ModelMaterialTimelineLayer` / `MaidMaterialTimelineLayer` / `MorphTimelineLayer` の `allBoneNames` は `trackedBoneNames`（チェック済み ∪ 既存キーフレーム記載、`:50-72`）で、レイヤーの生存中に増える。誕生時の断面だけでは、追加直後にチェックした項目が復元対象から漏れる。

`AddTrackedFirstFrameKeys`（`:168`）は「新たに対象へ入った項目に 0F キーを打つ」処理で、すでに `UpdateFrame(force: true)` でライブ値を読んだ `tmpFrame` を持っている。キーを打つ前にこれを積み増しへ渡せば、追加の `UpdateFrame` 呼び出しなしで基準を確保できる。

- [x] **Step 1: 積み増しの呼び出しを足す**

`UpdateBones(0, bones);` の直前へ足す。

変更前:
```csharp
            var bones = tmpFrame.GetFilterBones(boneNames);
            if (bones.Count == 0)
            {
                return;
            }

            UpdateBones(0, bones);
```

変更後:
```csharp
            var bones = tmpFrame.GetFilterBones(boneNames);
            if (bones.Count == 0)
            {
                return;
            }

            // 追加された項目はまだこのレイヤーに駆動されていない。
            // キーを打つ前の今の値が、レイヤー削除時に戻すべき基準になる
            timelineManager.MergeLayerBaseline(this, tmpFrame.ToXml(), boneNames);

            UpdateBones(0, bones);
```

- [x] **Step 2: 共通ビルドとテストを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、既存テスト全 PASS

- [x] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs
git commit -m "feat(timeline): 追跡対象が増えたとき復元用の断面を積み増す"
```

---

### Task 5: 実機で挙動を確認する

**Files:** なし（検証のみ）

ゲームを起動したうえで、ビルド済み DLL を実機へ反映して確認する。反映はユーザーに依頼するか、MCP `com3d25-devbridge` の `eval_csharp` でホットリロードする（手順は `W:\COM3D2_5\work\CLAUDE.md`「Harmony プラグインのホットリロード」）。

- [ ] **Step 1: ライトレイヤーで往復を確認する**

1. シーンにライトを置き、色を赤にする
2. タイムラインを新規作成し、`ライト` レイヤーを追加する
3. 適当なフレームでライトを青にしてキーを打ち、そのフレームを表示したままにする
4. `-` ボタンで `ライト` レイヤーを削除する

Expected: ライトが**赤（レイヤー追加時の色）**に戻る。ライトの台数は変わらない

- [ ] **Step 2: 読込済みタイムラインで確認する**

1. Step 1 のタイムラインを保存して読み込み直す（読込前のライトは緑にしておく）
2. 青のキーがあるフレームを表示する
3. `ライト` レイヤーを削除する

Expected: ライトが**緑（読込直前の色）**に戻る

- [ ] **Step 3: undo を確認する**

Step 2 の直後に Ctrl+Z する。

Expected: `ライト` レイヤーが復活し、表示中フレームのキーの色（青）が再適用される

- [ ] **Step 4: 対象外レイヤーを確認する**

`メイドボイス` レイヤーを追加してから削除する。

Expected: 音が鳴らず、例外も出ない（`tail_log` で確認）

- [ ] **Step 5: モデルレイヤーで確認する**

モデルを配置し、`モデル` レイヤーを追加 → 移動キーを打つ → 削除する。

Expected: モデルの位置が追加時の位置へ戻り、**モデル自体は消えない**

- [ ] **Step 6: 追跡系レイヤー（メイド表情）で確認する**

Task 4 の積み増しが効いているかを見る。**レイヤー追加より後にチェックする**のが要点。

1. メイドの表情を素の状態にする
2. `メイド表情` レイヤーを追加する（この時点では何もチェックしない）
3. 表情ウィンドウで `ほほ` にチェックを入れ、値を大きくしてキーを打つ
4. `メイド表情` レイヤーを削除する

Expected: `ほほ` が素の値へ戻る

- [ ] **Step 7: 追跡系レイヤー（モデルボーン）で確認する**

1. モデルを配置し、`モデルボーン` レイヤーを追加する
2. ボーンを 1 本チェックして回転キーを打つ
3. `モデルボーン` レイヤーを削除する

Expected: そのボーンの回転がチェック時点の姿勢へ戻る

- [ ] **Step 8: 結果を記録する**

確認した内容と実機での結果を、この計画書の本セクションへ追記する（PASS / 差異）。

---

### Task 6: ドキュメントを更新する

**Files:**
- Modify: `docs-site/timeline/layers.md:73-75`
- Modify: `docs-site/timeline/layers-camera.md:32`
- Modify: `docs-site/timeline/layers-background.md:23`

- [x] **Step 1: layers.md の削除の説明を書き換える**

`:75` の 1 行を置き換え、対象外レイヤーの但し書きを足す。

変更前:
```markdown
- レイヤーを削除してもシーン上の実体（サブカメラ、地面、モデルなど）は変わりません
```

変更後:
```markdown
- レイヤーを削除すると、そのレイヤーが動かしていた値は**レイヤーを追加した時点**（読み込んだタイムラインに元からあったレイヤーなら**読み込む直前**）の状態へ戻ります
- チェックで対象を選ぶレイヤー（`メイド表情` / `メイドマテリアル` / `モデルボーン` / `モデルシェイプ` / `モデルマテリアル`）は、レイヤー追加後にチェックした項目については**チェックを入れた時点**の値へ戻ります
- シーン上の実体（サブカメラ、地面、モデルなど）の数は変わりません。戻るのは値だけです
- `メイドアニメ` / `メイドボイス` / `効果音` / `メイド衣装` は値を戻しません（音の再生や着替えをやり直してしまうため）
```

- [x] **Step 2: layers-camera.md の但し書きを直す**

変更前:
```markdown
- `サブカメラ` レイヤーを削除してもサブカメラの実体は残ります。台数を減らすには Camera ウィンドウの `サブカメラ数` を使ってください
```

変更後:
```markdown
- `サブカメラ` レイヤーを削除するとビューポートや画角はレイヤー追加時の値へ戻りますが、サブカメラの実体は残ります。台数を減らすには Camera ウィンドウの `サブカメラ数` を使ってください
```

- [x] **Step 3: layers-background.md の但し書きを直す**

変更前:
```markdown
- レイヤーを削除しても地面は消えません
```

変更後:
```markdown
- レイヤーを削除すると背景色と地面の値はレイヤー追加時の状態へ戻りますが、地面そのものは消えません
```

- [x] **Step 4: 記述とコードの整合を確認する**

Run: `docs-review` スキル
Expected: 指摘なし、または指摘を取り込んで書き直し済み

- [x] **Step 5: コミット**

```bash
git add docs-site/timeline/layers.md docs-site/timeline/layers-camera.md docs-site/timeline/layers-background.md
git commit -m "docs(timeline): レイヤー削除時に値が復元されることを記載する"
```

---

## 完了条件

- 2 構成（COM3D2 / COM3D25）ともビルドが通る
- `dotnet test` が全 PASS
- Task 5 の実機確認 7 項目がすべて期待どおり
- `docs-site/timeline/` の記述が実装と一致している

## レビュー却下メモ

- **`ChangeActiveLayer` 経路で `UpdateFrame` が二重に走る（`Init` 内で一度、`CaptureLayerBaseline` でもう一度）ので `firstFrame.ToXml()` を再利用すべき** — 却下。捕捉はレイヤー追加時に 1 回だけで、毎フレームの処理ではない。`LoadTimeline` / `CreateNewTimeline` 経路では `firstFrame` が XML 由来になるため同じ最適化ができず、経路ごとに分岐が増える。読みやすさの損失に見合わない
