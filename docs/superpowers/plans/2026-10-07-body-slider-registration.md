# 体型項目のタイムライン登録チェック Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 体型タブの各項目にチェックボックスを付け、チェックした項目だけを体型レイヤーの BoneMenu に出してキーにする。シェイプキーの登録（`TimelineData.maidShapeKeysMap`）と同じ作りにする。

**Architecture:**
- **登録の保持**: `TimelineData` にメイドのスロットごとの登録集合 `maidBodySliderKeysMap` を持ち、タイムライン XML の新要素 `<MaidBodySliderKeys>` に保存する。
- **レイヤー**: `BodySliderTimelineLayer.allBoneNames` は登録集合を定義順に並べたもの。キーは登録項目を常に全部打つ（既定値でもキーにする）。登録したときは `AddFirstBones` で 0F に今の値のキーを打ち、外したときは `RemoveAllBones` でその項目のキーを全部消す（シェイプキーと同じ）。
- **置き換え**: 「既定でない項目とキー済み項目だけをキーにする」規則（`BuildKeyNames`）は不要になるので削除する。0F の既定値の行を補う仕組み（`PrependDefaultFirstRows`）は残す（旧 XML・MaidScale 移行・0F のキーの個別削除では、登録項目でも 0F にキーが無いことがあるため）。
- **旧 XML**: 要素が無い（またはキーのある項目が登録に無い）XML は、`TimelineXml.Initialize` が体型レイヤーにキーのある項目を登録に足す。MaidScale から移行したキーもこれで登録される。

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）

**Spec:** この計画自体が仕様を兼ねる。ユーザーの決定（2026-10-07）:
- チェックの意味: シェイプキーと同じ（チェックした項目だけ BoneMenu に出し、常にキー化。チェック時に 0F へ現在値のキーを打つ）
- チェックを外したとき: その項目の全キーを削除する（Undo 可能）
- 保存先: タイムライン XML（メイドのスロットごと）。旧 XML はキーのある項目だけ登録済みとして読む

前提: `docs/superpowers/plans/2026-10-06-body-slider.md`（体型スライダー本体。ブランチ `feat/body-slider` に実装済み）

## Global Constraints

- **登録の単位**: メイドのタイムラインスロット番号（`MaidCache.slotNo`）× 項目キー（`BodySliderDefs` のキー）。
- **未登録の項目**: BoneMenu に出さない。キーも打たない。体型タブでの値の編集はできる（値はコントローラーが持ち、タイムラインに関係なく効く）。
- **登録の UI**: タイムライン未ロード、またはメイドがタイムラインの管理外（スロット番号 < 0）のときはチェックボックスを無効表示にする。
- **XML**: `<MaidBodySliderKeys><MaidBodySliderKey><MaidSlotNo/><Key/></MaidBodySliderKey></MaidBodySliderKeys>`。空なら書き出さない（登録の無いタイムラインの XML を変えない）。MTE は未知の要素を読み飛ばす。版は上げない。
- **言語**: コードのコメント・ログは日本語。
- **ビルド**: 両構成（COM3D2 → COM3D25 の順）でビルドする。`deploy.bat` は実行しない。

## 決定事項

| 論点 | 決定 | 理由 |
|---|---|---|
| レイヤーへの通知 | `TimelineData.AddMaidBodySliderKey` / `RemoveMaidBodySliderKey` が同じスロットの `BodySliderTimelineLayer` を `as` で探して呼ぶ。`ITimelineLayer` にメソッドは足さない | 体型レイヤー専用の通知で、全レイヤーに既定実装を足すほどの汎用性が無い |
| 旧 XML の登録の補完 | `TimelineXml.Initialize` で毎回（版を問わず）体型レイヤーのキーの項目名を登録に足す | 要素の有無を XmlSerializer で区別できない（既定が空リスト）。登録を外すとキーも消えるので、「キーがあるのに未登録」は旧 XML にしか起きず、毎回足しても結果は同じ |
| 0F のキー | 登録時に `AddFirstBones`（今の値で 0F にキー）。レイヤー作成時は基底の `Init` が `UpdateFrame` で 0F に全登録項目のキーを打つ。それでも 0F にキーの無い登録項目（旧 XML、MaidScale 移行、0F のキーだけを消した場合）は、既存の `PrependDefaultFirstRows` が再生用の行に 0F の既定値を補う | シェイプキーと同じ。0F の補いを消すと、最初のキーより前の区間が直前の再生・シーク次第で変わる問題（前回レビューの HIGH、commit 64dca0a）が戻る |
| 登録集合の並び | `allBoneNames` は `BodySliderDefs.items` の定義順 | BoneMenu とキーの並びを体型タブと揃える |
| 履歴 | `AddMaidBodySliderKey` / `RemoveMaidBodySliderKey` の末尾で必ず `timelineManager.RequestHistory` を呼ぶ | `AddFirstBones` は 0F に既にキーがあると、`RemoveAllBones` は消すキーが無いと履歴を出さず、登録の変更だけが Undo できなくなる。`RequestHistory` は説明文を置くだけで、同じフレームの複数回は 1 件にまとまる。登録集合は XML のレイヤー外にあるので、Undo は全再構築（`TimelineXmlDiff`）になり登録集合も戻る |

## Review Focus

1. **旧 XML の読込**: `<MaidBodySliderKeys>` の無い XML で、体型レイヤーにキーのある項目が BoneMenu に出て再生されること（Task 1 のテスト）。
2. **MaidScale からの移行**: v38 の MaidScale キーが腕の項目へ変換されたうえで登録されること（Task 1 のテスト）。
3. **登録集合の並び**: 登録した順ではなく定義順で BoneMenu に出ること（Task 2 のテスト）。
4. **未登録項目のキー**: 値を変えても未登録の項目はキーにならないこと（Task 2 のテスト）。
5. **スロット違い**: 別スロットのメイドの登録が混ざらないこと（Task 1 のテスト）。

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineMaidBodySliderKeyXml`、`maidBodySliderKeys`、`Initialize` での登録の補完 `RegisterKeyedBodySliderItems` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `maidBodySliderKeysMap` と Get/Has/Add/Remove、FromXml/ToXml | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BodySliderTimelineLayer.cs` | 登録集合からメニューとキーを作る。登録・解除の通知を受ける | 変更 |
| `source/COM3D2.SceneEditor.Plugin/BodySliderRowDrawer.cs` | 項目の見出し行（チェックボックス + 表示名）`DrawHeader` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs` | 体型タブで見出しを `DrawHeader` に替える | 変更 |
| `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderRegistrationTests.cs` | テスト | 新規 |
| `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderLayerTests.cs` | `BuildKeyNames` のテストを削除 | 変更 |
| `docs-site/guide/maid-editing.md`、`docs-site/timeline/layers-maid.md`、`W:\COM3D2_5\work\CLAUDE.md` | ドキュメント | 変更 |

### ビルド + テストのコマンド（全タスク共通）

Git Bash で実行する。`<Filter>` はタスクごとに指定する（全件のときは `--filter` ごと外す）。

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "<Filter>"
```

---

### Task 1: 登録集合の保持と XML（`TimelineXml` / `TimelineData`）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`TimelineMaidShapeKeyXml` の直後にクラス、`maidShapeKeys` の直後にフィールド、`Initialize` の `ConvertPlugin();` の直前に呼び出し、`ConvertMaidScaleLayer` の直後にメソッド）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`maidShapeKeysMap` の直後にフィールド、`RemoveMaidShapeKey` の直後にメソッド、`FromXml` / `ToXml` の `maidShapeKeys` の処理の直後）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderRegistrationTests.cs`

**Interfaces:**
- Produces:
  - `class TimelineMaidBodySliderKeyXml { int maidSlotNo; string key; }`
  - `TimelineXml.maidBodySliderKeys : List<TimelineMaidBodySliderKeyXml>`（空なら書き出さない）
  - `static void TimelineXml.RegisterKeyedBodySliderItems(TimelineXml xml)`（`Initialize` から呼ぶ。テストからも呼べるよう public static）
  - `TimelineData.maidBodySliderKeysMap : Dictionary<int, HashSet<string>>`
  - `HashSet<string> TimelineData.GetMaidBodySliderKeys(int maidSlotNo)`、`bool HasMaidBodySliderKey(int, string)`、`void AddMaidBodySliderKey(int, string)`、`void RemoveMaidBodySliderKey(int, string)`

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderRegistrationTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型項目のタイムライン登録 (どの項目を BoneMenu に出してキーにするか) の保存と旧 XML の補完を固定する</summary>
    public class BodySliderRegistrationTests
    {
        private static TransformXml CreateKey(string name, TransformType type, float[] values)
        {
            return new TransformXml { name = name, type = type, values = values };
        }

        private static TimelineXml CreateTimeline(int version, string className, int slotNo, params TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml { transform = transform });
            }

            var layer = new TimelineLayerXml { className = className, slotNo = slotNo };
            layer.keyFrames.Add(new FrameXml { frameNo = 0, bones = bones });

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        private static string[] Registered(TimelineXml xml, int slotNo)
        {
            return xml.maidBodySliderKeys
                .Where(k => k.maidSlotNo == slotNo)
                .Select(k => k.key)
                .OrderBy(k => k)
                .ToArray();
        }

        [Fact]
        public void 登録の無い旧XMLは体型レイヤーにキーのある項目を登録する()
        {
            var timeline = CreateTimeline(39, "BodySliderTimelineLayer", 1,
                CreateKey("THISCL", TransformType.BodySlider, new[] { 1.2f, 1f, 1f }),
                CreateKey("MUNEPOS", TransformType.BodySlider, new[] { 0f, 0.1f, 0f }));

            timeline.Initialize();

            Assert.Equal(new[] { "MUNEPOS", "THISCL" }, Registered(timeline, 1));
            Assert.Empty(Registered(timeline, 0));
        }

        [Fact]
        public void 登録済みの項目は重複させない()
        {
            var timeline = CreateTimeline(39, "BodySliderTimelineLayer", 0,
                CreateKey("THISCL", TransformType.BodySlider, new[] { 1.2f, 1f, 1f }));
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 0, key = "THISCL" });
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 0, key = "SPIPOS" });

            timeline.Initialize();

            Assert.Equal(new[] { "SPIPOS", "THISCL" }, Registered(timeline, 0));
        }

        [Fact]
        public void メイドスケールから移行した腕の項目も登録する()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer", 0,
                CreateKey("Bip01 L Hand", TransformType.MaidScale, new[] { 1.5f }),
                CreateKey("Bip01 R UpperArm", TransformType.MaidScale, new[] { 1f }));

            timeline.Initialize();

            Assert.Equal(new[] { "HANDSCL_L", "UPARMSCL_R" }, Registered(timeline, 0));
        }

        [Fact]
        public void 他のレイヤーと未知の項目名は登録しない()
        {
            var timeline = CreateTimeline(39, "GravityTimelineLayer", 0,
                CreateKey("THISCL", TransformType.Gravity, new[] { 0f }));
            timeline.layers.Add(new TimelineLayerXml { className = "BodySliderTimelineLayer", slotNo = 0 });
            timeline.layers[1].keyFrames.Add(new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = CreateKey("UNKNOWN", TransformType.BodySlider, new[] { 1f, 1f, 1f }) } },
            });

            timeline.Initialize();

            Assert.Empty(timeline.maidBodySliderKeys);
        }

        [Fact]
        public void 登録はXMLの往復で保たれ空なら書き出さない()
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));

            var empty = new TimelineXml();
            string emptyText;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, empty);
                emptyText = writer.ToString();
            }
            Assert.DoesNotContain("MaidBodySliderKeys", emptyText);

            var timeline = new TimelineXml();
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 2, key = "HANDSCL_L" });
            string text;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, timeline);
                text = writer.ToString();
            }
            Assert.Contains("<MaidBodySliderKeys>", text);

            TimelineXml loaded;
            using (var reader = new StringReader(text))
            {
                loaded = (TimelineXml)serializer.Deserialize(reader);
            }
            Assert.Single(loaded.maidBodySliderKeys);
            Assert.Equal(2, loaded.maidBodySliderKeys[0].maidSlotNo);
            Assert.Equal("HANDSCL_L", loaded.maidBodySliderKeys[0].key);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySliderRegistrationTests` で実行する。
Expected: テストプロジェクトのコンパイルエラー（`maidBodySliderKeys` / `TimelineMaidBodySliderKeyXml` が無い）

- [ ] **Step 3: XML の型とフィールドを書く**

`TimelineXml.cs` の `TimelineMaidShapeKeyXml` クラスの直後に追加:

```csharp
    /// <summary>体型項目のタイムライン登録 1 件 (SE 独自)。登録した項目だけが体型レイヤーの BoneMenu に出てキーになる</summary>
    public class TimelineMaidBodySliderKeyXml
    {
        [XmlElement("MaidSlotNo")]
        public int maidSlotNo;
        [XmlElement("Key")]
        public string key;
    }
```

`TimelineXml` の `maidShapeKeys` フィールドの直後に追加:

```csharp
        // 体型項目の登録は SE 独自。登録の無いタイムラインの XML を変えないよう、空なら書き出さない
        [XmlArray("MaidBodySliderKeys")]
        [XmlArrayItem("MaidBodySliderKey")]
        public List<TimelineMaidBodySliderKeyXml> maidBodySliderKeys = new List<TimelineMaidBodySliderKeyXml>();
        [XmlIgnore] public bool maidBodySliderKeysSpecified { get { return maidBodySliderKeys != null && maidBodySliderKeys.Count > 0; } set { } }
```

- [ ] **Step 4: 旧 XML の登録の補完を書く**

`TimelineXml.Initialize` の `ConvertPlugin();` の直前（`if (version < BodySliderVersion)` の段の後）に追加:

```csharp
            // 版を問わず毎回行う。登録を外すとキーも消えるので、キーがあるのに未登録なのは旧 XML だけ
            RegisterKeyedBodySliderItems(this);
```

`ConvertMaidScaleLayer` の直後（`Triple` の前）にメソッドを追加:

```csharp
        /// <summary>
        /// 体型レイヤーにキーのある項目を、そのスロットの登録 (maidBodySliderKeys) へ足す。
        /// 登録を持たない旧 XML (登録の導入前、または MaidScale から移行したもの) でも、キーのある項目が BoneMenu に出るようにする
        /// </summary>
        public static void RegisterKeyedBodySliderItems(TimelineXml xml)
        {
            var registered = new HashSet<string>();
            foreach (var entry in xml.maidBodySliderKeys)
            {
                registered.Add(entry.maidSlotNo + "/" + entry.key);
            }

            foreach (var layer in xml.layers)
            {
                if (layer.className != BodySliderLayerNameAtV39)
                {
                    continue;
                }
                foreach (var keyFrame in layer.keyFrames)
                {
                    if (keyFrame.bones == null)
                    {
                        continue;
                    }
                    foreach (var bone in keyFrame.bones)
                    {
                        var transform = bone.transform;
                        if (transform == null || BodySliderDefs.Find(transform.name) == null
                            || !registered.Add(layer.slotNo + "/" + transform.name))
                        {
                            continue;
                        }
                        xml.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml
                        {
                            maidSlotNo = layer.slotNo,
                            key = transform.name,
                        });
                    }
                }
            }
        }
```

- [ ] **Step 5: `TimelineData` に登録集合を持たせる**

`TimelineData.cs` の `maidShapeKeysMap` の直後に追加:

```csharp
        /// <summary>maidSlotNo → 体型レイヤーに登録した項目キー (SE 独自)</summary>
        public Dictionary<int, HashSet<string>> maidBodySliderKeysMap = new Dictionary<int, HashSet<string>>();
```

`RemoveMaidShapeKey` の直後に追加:

```csharp
        public HashSet<string> GetMaidBodySliderKeys(int maidSlotNo)
        {
            HashSet<string> keys;
            if (!maidBodySliderKeysMap.TryGetValue(maidSlotNo, out keys))
            {
                keys = new HashSet<string>();
                maidBodySliderKeysMap[maidSlotNo] = keys;
            }
            return keys;
        }

        /// <summary>UI が毎フレーム呼ぶので、エントリを作らずに引く</summary>
        public bool HasMaidBodySliderKey(int maidSlotNo, string key)
        {
            HashSet<string> keys;
            return maidBodySliderKeysMap.TryGetValue(maidSlotNo, out keys) && keys.Contains(key);
        }

        /// <summary>体型項目を登録する。同じスロットの体型レイヤーが BoneMenu に出し、0F にキーを打つ</summary>
        public void AddMaidBodySliderKey(int maidSlotNo, string key)
        {
            if (!GetMaidBodySliderKeys(maidSlotNo).Add(key))
            {
                return;
            }
            foreach (var layer in layers)
            {
                var bodySliderLayer = layer as BodySliderTimelineLayer;
                if (bodySliderLayer != null && bodySliderLayer.slotNo == maidSlotNo)
                {
                    bodySliderLayer.OnBodySliderKeyAdded(key);
                }
            }
            // 0F に既にキーがあると AddFirstBones は履歴を出さないので、登録の変更として必ず 1 件積む
            timelineManager.RequestHistory("体型項目の登録: " + key);
        }

        /// <summary>体型項目の登録を外す。同じスロットの体型レイヤーがその項目のキーを全部消す</summary>
        public void RemoveMaidBodySliderKey(int maidSlotNo, string key)
        {
            if (!GetMaidBodySliderKeys(maidSlotNo).Remove(key))
            {
                return;
            }
            foreach (var layer in layers)
            {
                var bodySliderLayer = layer as BodySliderTimelineLayer;
                if (bodySliderLayer != null && bodySliderLayer.slotNo == maidSlotNo)
                {
                    bodySliderLayer.OnBodySliderKeyRemoved(key);
                }
            }
            // 消すキーが無いと RemoveAllBones は履歴を出さないので、登録の変更として必ず 1 件積む
            timelineManager.RequestHistory("体型項目の登録解除: " + key);
        }
```

`OnBodySliderKeyAdded` / `OnBodySliderKeyRemoved` は Task 2 で作る。このタスクのビルドを通すため、Task 2 の Step 3 の 2 メソッドだけを先に `BodySliderTimelineLayer.cs` へ空実装（`{ }`）で足しておき、Task 2 で中身を書く。

`FromXml` の `maidShapeKeysMap` を読むループの直後に追加:

```csharp
            maidBodySliderKeysMap.Clear();
            foreach (var keyXml in xml.maidBodySliderKeys)
            {
                if (BodySliderDefs.Find(keyXml.key) != null)
                {
                    GetMaidBodySliderKeys(keyXml.maidSlotNo).Add(keyXml.key);
                }
            }
```

`ToXml` の `xml.maidShapeKeys` を作るループの直後に追加:

```csharp
            xml.maidBodySliderKeys = new List<TimelineMaidBodySliderKeyXml>();
            foreach (var pair in maidBodySliderKeysMap)
            {
                foreach (var key in pair.Value)
                {
                    xml.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml
                    {
                        maidSlotNo = pair.Key,
                        key = key,
                    });
                }
            }
```

`TimelineData.cs` に `using COM3D2.SceneEditor.Plugin;` が無ければ追加する（`BodySliderDefs` のため）。

- [ ] **Step 6: テストが通ることを確かめる**

Step 2 と同じフィルタで実行する。
Expected: 両ビルド成功、BodySliderRegistrationTests 5 件 PASS

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(body-slider): 体型項目のタイムライン登録を XML に保存する"
```

---

### Task 2: 体型レイヤーを登録集合で動かす

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BodySliderTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderLayerTests.cs`（`BuildKeyNames` のテストを差し替え）

**Interfaces:**
- Consumes: `TimelineData.GetMaidBodySliderKeys(int)`（Task 1）
- Produces:
  - `static List<string> BodySliderTimelineLayer.OrderByDefinition(ICollection<string> registered)`（定義順に並べ、未知の名前は捨てる）
  - `void BodySliderTimelineLayer.OnBodySliderKeyAdded(string key)` / `void OnBodySliderKeyRemoved(string key)`
- 削除: `BuildKeyNames`
- 残す: `PrependDefaultFirstRows`、`_defaultFirstFrame`、`BuildTimelineBonesMap` の override（0F にキーの無い登録項目のため）

- [ ] **Step 1: テストを差し替える**

`BodySliderLayerTests.cs` から次のテストを削除する: `キーは既定でない項目とキー済み項目だけを定義順で作る`、`値もキーも無ければキーにしない`。`PrependDefaultFirstRows` のテスト（`CreateRow` と 2 件）は残す。旧 XML で最初のキーが 0F より後の登録項目は、このテストが担保する。

代わりに追加する:

```csharp
        [Fact]
        public void 登録項目は定義順に並べ未知の名前は捨てる()
        {
            var names = MTEP.BodySliderTimelineLayer.OrderByDefinition(
                new List<string> { "HANDSCL_R", "UNKNOWN", "THISCL", "MUNEPOS" });

            Assert.Equal(new[] { "THISCL", "MUNEPOS", "HANDSCL_R" }, names.ToArray());
        }

        [Fact]
        public void 登録が無ければ項目も無い()
        {
            Assert.Empty(MTEP.BodySliderTimelineLayer.OrderByDefinition(new List<string>()));
        }
```

Review Focus 4（未登録項目はキーにならない）は、`UpdateFrame` が `allBoneNames`（= `OrderByDefinition` の結果）だけを回すことで担保する。`UpdateFrame` はメイドとタイムラインを要するのでゲーム外テストにできない。Task 3 の実機確認で見る。

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySliderLayerTests` で実行する。
Expected: テストプロジェクトのコンパイルエラー（`OrderByDefinition` が無い）

- [ ] **Step 3: レイヤーを書き換える**

`BodySliderTimelineLayer.cs` の class summary を次に置き換える:

```csharp
    /// <summary>
    /// 体型スライダー (BodySliderController) をキー化するレイヤー。
    /// 項目名は BodySliderDefs のキーで、値 3 個を Tangent 補間する。
    /// BoneMenu に出してキーにするのは、体型タブで登録した項目 (TimelineData.maidBodySliderKeysMap) だけ。
    /// 登録時に 0F へキーを打ち、解除時にその項目のキーを全部消す (シェイプキーと同じ)。
    /// 骨への書き込みはコントローラーが TBody.LateUpdate の直後に行い、このレイヤーは値を渡すだけ
    /// </summary>
```

`allBoneNames` を static キャッシュからインスタンスのキャッシュへ変える（`_allBoneNames` の宣言と getter を置き換え）:

```csharp
        /// <summary>登録した項目を定義順に並べたもの。InitMenuItems で作り直す</summary>
        private List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = OrderByDefinition(timeline.GetMaidBodySliderKeys(slotNo));
                }
                return _allBoneNames;
            }
        }
```

`InitMenuItems` を置き換える:

```csharp
        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            _allBoneNames = null;
            foreach (var key in allBoneNames)
            {
                _allMenuItems.Add(new BoneMenuItem(key, BodySliderDefs.Find(key).displayName));
            }
        }
```

`BuildKeyNames`（summary 含む）を削除し、その位置に追加する（`_defaultFirstFrame`・`BuildTimelineBonesMap` の override・`PrependDefaultFirstRows` は残す。`PrependDefaultFirstRows` の summary の「キーは既定値でない項目だけに打つので、途中のフレームで初めて変えた項目は 0F の行を持たない。」は「旧 XML・MaidScale からの移行・0F のキーの個別削除では、登録項目でも 0F の行を持たないことがある。」に置き換える）:

```csharp
        /// <summary>登録した項目を定義順に並べる。定義に無い名前は捨てる</summary>
        public static List<string> OrderByDefinition(ICollection<string> registered)
        {
            var result = new List<string>();
            foreach (var item in BodySliderDefs.items)
            {
                if (registered.Contains(item.key))
                {
                    result.Add(item.key);
                }
            }
            return result;
        }

        /// <summary>体型タブで項目を登録した。BoneMenu に出し、0F に今の値のキーを打つ</summary>
        public void OnBodySliderKeyAdded(string key)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { key });
            ApplyCurrentFrame(true);
        }

        /// <summary>体型タブで項目の登録を外した。BoneMenu から消し、その項目のキーを全部消す</summary>
        public void OnBodySliderKeyRemoved(string key)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { key });
            ApplyCurrentFrame(true);
        }
```

`UpdateFrame` の本体（メイドの null チェックの後）を次に置き換える:

```csharp
            foreach (var key in allBoneNames)
            {
                var trans = CreateTransformData<TransformDataBodySlider>(key);
                trans.vector = bodySliderController.GetValues(maid, key);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
```

`ApplyMotion` の「一度も変えていないメイドへ既定値を書いても何も変わらない」の早期 return は残す（登録項目は既定値でもキーになるので、この判定がより効く）。

- [ ] **Step 4: テストが通ることを確かめる**

「ビルド + テストのコマンド」を `<Filter>` なし（全件）で実行する。
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(body-slider): 体型レイヤーは登録した項目だけをメニューに出してキーにする"
```

---

### Task 3: 体型タブのチェックボックスとドキュメント

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BodySliderRowDrawer.cs`（`DrawHeader` を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs`（`DrawBodySliderContent` の見出し）
- Modify: `docs-site/guide/maid-editing.md`、`docs-site/timeline/layers-maid.md`、`W:\COM3D2_5\work\CLAUDE.md`

**Interfaces:**
- Consumes: `TimelineData.HasMaidBodySliderKey / AddMaidBodySliderKey / RemoveMaidBodySliderKey`（Task 1）
- Produces: `BodySliderRowDrawer.DrawHeader(GUIView view, Maid maid, BodySliderItem item, float rowHeight)`

- [ ] **Step 1: 見出し行を書く**

`BodySliderRowDrawer.cs` に追加（`DrawComponents` の前）:

```csharp
        /// <summary>
        /// 項目の見出し。左のチェックで体型レイヤーへの登録 (BoneMenu に出してキーにするか) を切り替える。
        /// タイムライン未ロードか、メイドがタイムラインの管理外なら押せない
        /// </summary>
        public static void DrawHeader(GUIView view, Maid maid, BodySliderItem item, float rowHeight)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            var maidCache = maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            var slotNo = maidCache != null ? maidCache.slotNo : -1;
            var canRegister = timeline != null && slotNo >= 0;
            var isRegistered = canRegister && timeline.HasMaidBodySliderKey(slotNo, item.key);

            view.BeginHorizontal();
            {
                view.DrawToggle(null, isRegistered, GUIView.TrackedCheckWidth, rowHeight, canRegister, newValue =>
                {
                    if (newValue)
                    {
                        timeline.AddMaidBodySliderKey(slotNo, item.key);
                    }
                    else
                    {
                        timeline.RemoveMaidBodySliderKey(slotNo, item.key);
                    }
                });
                view.DrawLabel(item.displayName, -1, rowHeight);
            }
            view.EndLayout();
        }
```

`BodySliderRowDrawer.cs` の先頭に `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` を追加する。

`BoneEditWindow.cs` の `DrawBodySliderContent` で、

```csharp
                view.DrawLabel(item.displayName, -1, ROW_HEIGHT);
```

を次に置き換える:

```csharp
                BodySliderRowDrawer.DrawHeader(view, target, item, ROW_HEIGHT);
```

- [ ] **Step 2: ビルドとテスト**

「ビルド + テストのコマンド」を `<Filter>` なし（全件）で実行する。
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 3: ドキュメントを直す**

`docs-site/guide/maid-editing.md` の `### 体型` の箇条書きで、「タイムラインの「体型」レイヤーとシーンプリセットにも保存されます」を次の 2 行に置き換える:

```markdown
- 項目名の左のチェックで、その項目をタイムラインの「体型」レイヤーに登録します。登録した項目だけがレイヤーの項目一覧に出てキーフレームになります（タイムラインを読み込んでいないときは押せません）
- シーンプリセットには、登録に関係なく全項目の値が保存されます
```

`docs-site/timeline/layers-maid.md` の `## 体型` で、「キーフレームに登録されるのは、既定値でない項目と、…」の段落（2 行）を次に置き換える:

```markdown
項目一覧に出てキーフレームになるのは、`体型` タブでチェックを付けた項目だけです。チェックを付けると 0F にその時点の値のキーが登録され、外すとその項目のキーがすべて削除されます。チェックの無い項目は `体型` タブで入れた値のままです。
チェックはメイドごとにタイムラインへ保存されます。チェックを持たない以前のタイムラインは、キーのある項目にチェックが付いた状態で読み込まれます。
チェックはメイドの枠（スロット）ごとに残り、レイヤーを削除しても消えません。レイヤーを作り直すと、チェック済みの項目がその時点の値で 0F にキーフレーム登録されます。
```

`W:\COM3D2_5\work\CLAUDE.md` の「version 39 で体型レイヤー（…）」の項目の直後に追加:

```markdown
- 体型項目の登録（ルートの `<MaidBodySliderKeys>`、`MaidSlotNo` / `Key`。登録した項目だけが体型レイヤーの BoneMenu に出てキーになる）は SE 独自。空なら書き出さない。version は上げていない。要素の無い XML は `TimelineXml.RegisterKeyedBodySliderItems` が体型レイヤーにキーのある項目を登録として補う。MTE は要素を読み飛ばす
```

- [ ] **Step 4: 実機確認**

ゲームが旧 DLL を握っていれば `com3d25-devbridge:restart-verify` の手順で反映する。デイリー画面でエディタを有効にした通常シーンで確認する。

| # | 確認すること | 期待 |
|---|---|---|
| 1 | 体型タブで THISCL にチェック | 体型レイヤーの BoneMenu に「足全体のスケーリング」が出て、0F にキーができる |
| 2 | チェックしていない MUNESCL の値を変えてキーフレーム全登録 | MUNESCL のキーはできない |
| 3 | THISCL のチェックを外す | BoneMenu から消え、THISCL のキーがすべて消える。Undo で戻る |
| 4 | タイムラインを保存して読み直す | チェック状態が戻る |
| 5 | タイムライン未ロード時の体型タブ | チェックボックスが押せない |
| 5b | 体型レイヤーが無いとき（タブのゲートが閉じているとき） | チェックボックスが押せない（ゲートが無効化する） |
| 5c | 0F のキーがある項目のチェックを外して Undo | 登録とキーが両方戻る |
| 6 | 体型タブの見た目（`screenshot`） | チェックと表示名が 1 行に収まる |

確認できなかった項目は理由を添えてユーザーへ報告する。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin docs-site
git commit -m "feat(body-slider): 体型タブの各項目にタイムライン登録のチェックを付ける"
```

`W:\COM3D2_5\work\CLAUDE.md` はこのリポジトリの外なので、変更したことをユーザーへ伝える。

---

## レビュー却下メモ

- `OnBodySliderKeyAdded` でメイドが null のとき `SetBone(null)` になる — 誤検知。`FrameData.SetBone` は null を無視する。体型タブもメイドが無ければ描かない
- `GetMaidBodySliderKeys` が参照時にエントリを作る — 書き込み経路はシェイプキーと同じ作りで残す。UI が毎フレーム呼ぶ `HasMaidBodySliderKey` だけを作らない版にした（取り込み）
