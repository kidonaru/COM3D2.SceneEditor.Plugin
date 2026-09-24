# モデルのアタッチのキーフレーム化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（プロジェクト規約で subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 配置モデルのアタッチ先（メイド・部位）をモデルレイヤーのキーフレームに持たせ、タイムライン途中の持ち替えと、終点キー指定のワールド座標補間を可能にする。

**Architecture:** `TransformDataModel` に 3 値（アタッチ先メイド・部位・ワールド補間）を足し、`ModelTimelineLayer.ApplyMotion` が区間ごとに履歴を積まない経路で付け替える。モデル単位のアタッチ（`<Models>`）は v37 移行でキーへ移して以後書き出さない。UI はキーフレームインスペクタの既存コンボ機構（`CustomValueUIType`）に部位コンボを足す。

**Tech Stack:** C# (.NET Framework / Unity 2022 Mono、COM3D2 と COM3D25 の 2 構成), xUnit (net48)

**Spec:** `docs/superpowers/specs/2026-09-24-model-attach-keyframe-design.md`

## Global Constraints

- コードのコメント・ログは日本語
- 両構成（`GameVersion=COM3D2` / `COM3D25`）でビルドが通ること。テストは COM3D2 → COM3D25 の順にビルドしてから `dotnet test`（COM3D2 ビルドが `bin/Debug/COM3D25/` を消すため）
- ビルドは `debug.bat` ではなく MSBuild 直叩き（ゲームへの意図しない反映を避ける）。Git Bash では `export MSYS2_ARG_CONV_EXCL="*"` が必要
- テストで Unity ネイティブ呼び出し（`Quaternion.Euler` 等）をしない
- `deploy.bat` / `deploy.ps1` は実行しない
- タイムライン XML は MTE → SceneEditor の一方向互換のみ保証
- 値の既定: アタッチ先メイド `-1`（なし）、部位 `AttachPoint.Head`、ワールド補間 `0`
- 部位が `AttachPoint.Null` またはメイドのスロット番号が `< 0` のとき「アタッチなし」

## Review Focus

- Inspector のモデル行でアタッチを変えても、自動キー登録が発火せず編集モードを抜けると元に戻る → コンボ操作を `ObjectTransformRowDrawer.RecordEdit` で操作履歴へ記録し、確定時の自動登録に載せる（Task 3 Step 7、実機確認 Task 6 Step 7）
- メイドを削除・入れ替えした後にアタッチ付きキーを再生する → 例外を出さずアタッチなし扱い（プロバイダへ `maid = null`）になり、ワールド補間はローカル補間へフォールバックする（Task 3 の `GetAttachParent` が null を返す経路。実機確認 Task 6 Step 5）
- 最後のキーのアタッチが効かない（ステップ値を始点側だけで採ると終端キーの値が永久に適用されない。`MorphTimelineLayer` で既に踏んだ罠）→ `t >= StepEndThreshold` で終点側を採る（Task 3、実機確認 Task 6 Step 2）
- 再生・シークのたびに SE / ModItemExplorer の Undo 履歴が増える → 付け替えは `BeginBatch` 内・`RequestHistory` なし（Task 3、実機確認 Task 6 Step 4）
- キー再登録でワールド補間フラグが消える（`UpdateFrame` はシーンの現在状態から値を作り直す）→ 既存キーから引き継ぐ（Task 1 のテスト + Task 3）
- MTE 産（12 値）のモデルキーを読んだらメイド 0 にアタッチされる（不足分 0 埋め）→ `FromXml` で -1 / Head / 0 に補正（Task 1 のテスト）

---

### Task 1: `TransformDataModel` にアタッチ値を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataModel.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:75-83`（`CustomValueUIType` に `AttachPoint` を追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformDataModelTests.cs`（新規）

**Interfaces:**
- Produces:
  - `TransformDataModel.Index { AttachMaidSlotNo = 12, AttachPoint = 13, WorldLerp = 14 }`
  - `TransformDataModel.LegacyValueCount = 12`（public const。Task 2 の移行でも使う）
  - プロパティ `int attachMaidSlotNo`, `PhotoTransTargetObject.AttachPoint attachPoint`, `bool worldLerp`
  - `bool isAttached`（`attachPoint != Null && attachMaidSlotNo >= 0`）
  - `static bool IsAttached(AttachPoint point, int maidSlotNo)`
  - `void InheritKeySettings(TransformDataModel existing)`（ワールド補間を引き継ぐ。existing が null なら何もしない）
  - `CustomValueUIType.AttachPoint`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TransformDataModelTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルキーのアタッチ値 (index 12〜14) を固定する。
    /// 旧 12 値データの補正がずれると、MTE 産や旧 SE 産のモデルがメイド 0 にアタッチされてしまう
    /// </summary>
    public class TransformDataModelTests
    {
        private static TransformDataModel Create()
        {
            var trans = new TransformDataModel();
            trans.Initialize("test.menu");
            trans.Reset();
            return trans;
        }

        [Fact]
        public void アタッチ値は15値の末尾3つで既定はなし_Head_OFF()
        {
            var trans = Create();
            Assert.Equal(15, trans.valueCount);

            var map = trans.GetCustomValueInfoMap();
            Assert.Equal((int)TransformDataModel.Index.AttachMaidSlotNo, map["attachMaidSlotNo"].index);
            Assert.Equal(CustomValueUIType.MaidSlot, map["attachMaidSlotNo"].uiType);
            Assert.Equal((int)TransformDataModel.Index.AttachPoint, map["attachPoint"].index);
            Assert.Equal(CustomValueUIType.AttachPoint, map["attachPoint"].uiType);
            Assert.Equal((int)TransformDataModel.Index.WorldLerp, map["worldLerp"].index);
            Assert.Equal(CustomValueType.BoolValue, map["worldLerp"].type);

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.False(trans.worldLerp);
            Assert.False(trans.isAttached);
        }

        [Fact]
        public void 旧12値のキーはアタッチなしへ補正される()
        {
            var trans = Create();
            trans.FromXml(new TransformXml
            {
                name = "test.menu",
                type = TransformType.Model,
                values = new float[TransformDataModel.LegacyValueCount],
            });

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.False(trans.worldLerp);
        }

        [Fact]
        public void 15値のキーは補正しない()
        {
            var values = new float[15];
            values[(int)TransformDataModel.Index.AttachMaidSlotNo] = 0f;
            values[(int)TransformDataModel.Index.AttachPoint] = (float)AttachPoint.Hand_R;
            values[(int)TransformDataModel.Index.WorldLerp] = 1f;

            var trans = Create();
            trans.FromXml(new TransformXml { name = "test.menu", type = TransformType.Model, values = values });

            Assert.Equal(0, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Hand_R, trans.attachPoint);
            Assert.True(trans.worldLerp);
            Assert.True(trans.isAttached);
        }

        [Theory]
        [InlineData(AttachPoint.Null, 0, false)]
        [InlineData(AttachPoint.Head, -1, false)]
        [InlineData(AttachPoint.Head, 0, true)]
        public void IsAttachedは部位Nullかスロット負ならfalse(AttachPoint point, int slot, bool expected)
        {
            Assert.Equal(expected, TransformDataModel.IsAttached(point, slot));
        }

        [Fact]
        public void 再登録時は既存キーのワールド補間を引き継ぐ()
        {
            var existing = Create();
            existing.worldLerp = true;

            var trans = Create();
            trans.InheritKeySettings(existing);
            Assert.True(trans.worldLerp);

            var fresh = Create();
            fresh.InheritKeySettings(null);
            Assert.False(fresh.worldLerp);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run（Git Bash、リポジトリ直下）:
```bash
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FullyQualifiedName~TransformDataModelTests
```
Expected: テストプロジェクトのコンパイルエラー（`TransformDataModel.Index` / `LegacyValueCount` / `CustomValueUIType.AttachPoint` 未定義）

- [ ] **Step 3: `CustomValueUIType.AttachPoint` を追加**

`ITransformData.cs` の enum 末尾へ:

```csharp
        /// <summary>メイドの部位 (MaidPointType)</summary>
        MaidPoint,
        /// <summary>モデルのアタッチ部位 (PhotoTransTargetObject.AttachPoint)</summary>
        AttachPoint,
    }
```

- [ ] **Step 4: `TransformDataModel` を実装**

ファイル全体を以下にする:

```csharp
using System.Collections.Generic;
using System.IO;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    public class TransformDataModel : TransformDataBase
    {
        public override TransformType type => TransformType.Model;

        public enum Index
        {
            AttachMaidSlotNo = 12,
            AttachPoint = 13,
            WorldLerp = 14,
        }

        /// <summary>アタッチ値を持たない旧データ (MTE 産・version 36 以前) の値数</summary>
        public const int LegacyValueCount = 12;

        public override int valueCount => 15;

        public override bool hasPosition => true;
        public override bool hasRotation => true;
        public override bool hasScale => true;
        public override bool hasVisible => true;
        // Tangent 統一により常に Tangent 補間 (isTangentModel は XML 互換で残るのみ)
        public override bool hasTangent => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[7], values[8], values[9] };
        }

        public override ValueData visibleValue => values[11];
        public override ValueData easingValue => values[10];
        public override ValueData[] tangentValues => baseValues;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "attachMaidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.AttachMaidSlotNo,
                    name = "アタッチ先",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
            {
                "attachPoint", new CustomValueInfo
                {
                    index = (int)Index.AttachPoint,
                    name = "アタッチ部位",
                    min = 0f,
                    max = (float)AttachPoint.Foot_L,
                    step = 1f,
                    defaultValue = (float)AttachPoint.Head,
                    uiType = CustomValueUIType.AttachPoint,
                }
            },
            {
                "worldLerp", new CustomValueInfo
                {
                    index = (int)Index.WorldLerp,
                    name = "ワールド補間",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData attachMaidSlotNoValue => values[(int)Index.AttachMaidSlotNo];
        public ValueData attachPointValue => values[(int)Index.AttachPoint];
        public ValueData worldLerpValue => values[(int)Index.WorldLerp];

        public int attachMaidSlotNo
        {
            get => attachMaidSlotNoValue.intValue;
            set => attachMaidSlotNoValue.intValue = value;
        }

        public AttachPoint attachPoint
        {
            get => (AttachPoint)attachPointValue.intValue;
            set => attachPointValue.intValue = (int)value;
        }

        /// <summary>このキーへの区間をワールド座標で補間するか (終点キー側の設定)</summary>
        public bool worldLerp
        {
            get => worldLerpValue.boolValue;
            set => worldLerpValue.boolValue = value;
        }

        public bool isAttached => IsAttached(attachPoint, attachMaidSlotNo);

        public static bool IsAttached(AttachPoint point, int maidSlotNo)
        {
            return point != AttachPoint.Null && maidSlotNo >= 0;
        }

        /// <summary>
        /// キー固有の設定を既存キーから引き継ぐ。
        /// キー登録はシーンの現在状態から値を作り直すため、シーンに実体の無い設定は呼び出し側で残す
        /// </summary>
        public void InheritKeySettings(TransformDataModel existing)
        {
            if (existing == null)
            {
                return;
            }
            worldLerp = existing.worldLerp;
        }

        public TransformDataModel()
        {
        }

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            if (name.EndsWith(".menu", System.StringComparison.Ordinal))
            {
                name = Path.GetFileName(name);
            }

            // アタッチ値を持たない旧データは不足分が 0 で埋まり、スロット 0 のメイドへアタッチしてしまう。
            // 未アタッチ (-1) と既定値へ補正する
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                attachMaidSlotNo = -1;
                attachPoint = AttachPoint.Head;
                worldLerp = false;
            }
        }
    }
}
```

`ValueData.intValue` / `boolValue` は既存（`TransformDataCamera` と同じ使い方）。`AttachPoint.Foot_L` は `PhotoTransTargetObject.AttachPoint` の最終値（`W:\COM3D2_5\work\Assembly-CSharp\PhotoTransTargetObject.cs:8-28` で確認済み）。

- [ ] **Step 5: テストが通ることを確認**

Step 2 と同じコマンド。Expected: `TransformDataModelTests` 全件 PASS、両構成ビルド成功

- [ ] **Step 6: 全テストを回す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS（モデルの値数変更で既存テストが落ちたら内容を確認して直す）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataModel.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs source/COM3D2.SceneEditor.Plugin.Tests/TransformDataModelTests.cs
git commit -m "feat(timeline): モデルキーにアタッチ先・部位・ワールド補間の値を追加"
```

---

### Task 2: version 37 移行とモデル単位アタッチの書き出し停止

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`TimelineModelXml` に `ShouldSerialize*`、`Initialize` に `version < 37` 段）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:223`（`CurrentVersion = 37`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachMigrationTests.cs`（新規）

**Interfaces:**
- Consumes: `TransformDataModel.Index`, `TransformDataModel.LegacyValueCount`（Task 1）
- Produces: XML 読込後、モデルキー（`TransformXml.type == TransformType.Model`）の `values` は、モデル単位アタッチを持つモデルなら 15 値でアタッチ値入り。`TimelineModelXml` は `AttachPoint` / `AttachMaidSlotNo` を書き出さない

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachMigrationTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 37 でモデル単位のアタッチ (&lt;Models&gt;) をモデルキーへ移す移行を固定する。
    /// ずれると旧タイムラインのアタッチが読込で黙って外れる
    /// </summary>
    public class ModelAttachMigrationTests
    {
        private static TransformXml CreateModelKey(string name, TransformType type = TransformType.Model)
        {
            return new TransformXml
            {
                name = name,
                type = type,
                values = new float[TransformDataModel.LegacyValueCount],
            };
        }

        private static TimelineXml CreateTimeline(int version, TimelineModelXml model, params TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml { transform = transform });
            }

            // FrameXml.bones は既定が null なので明示的に作る
            var layer = new TimelineLayerXml { className = "ModelTimelineLayer" };
            layer.keyFrames.Add(new FrameXml { frameNo = 0, bones = bones });
            layer.keyFrames.Add(new FrameXml { frameNo = 10, bones = new List<BoneXml>(bones) });

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            timeline.models.Add(model);
            return timeline;
        }

        private static TimelineModelXml CreateModel(string name, AttachPoint point, int slot)
        {
            return new TimelineModelXml
            {
                name = name,
                attachPoint = point,
                attachMaidSlotNo = slot,
                pluginName = "ModItemExplorer",
            };
        }

        [Fact]
        public void Initialize_v36のモデル単位アタッチは同名モデルの全キーへ移る()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Hand_R, 1),
                CreateModelKey("cup.menu"));

            timeline.Initialize();

            foreach (var keyFrame in timeline.layers[0].keyFrames)
            {
                var values = keyFrame.bones[0].transform.values;
                Assert.Equal(15, values.Length);
                Assert.Equal(1f, values[(int)TransformDataModel.Index.AttachMaidSlotNo]);
                Assert.Equal((float)AttachPoint.Hand_R, values[(int)TransformDataModel.Index.AttachPoint]);
                Assert.Equal(0f, values[(int)TransformDataModel.Index.WorldLerp]);
            }
        }

        [Fact]
        public void Initialize_キー名がパス付きでもファイル名で対応付く()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Head, 0),
                CreateModelKey("menu/cup.menu"));

            timeline.Initialize();

            var values = timeline.layers[0].keyFrames[0].bones[0].transform.values;
            Assert.Equal((float)AttachPoint.Head, values[(int)TransformDataModel.Index.AttachPoint]);
        }

        [Fact]
        public void Initialize_アタッチなしのモデルと他の型のキーは変えない()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Null, -1),
                CreateModelKey("cup.menu"),
                CreateModelKey("cup.menu", TransformType.ModelBone));

            timeline.Initialize();

            var bones = timeline.layers[0].keyFrames[0].bones;
            Assert.Equal(TransformDataModel.LegacyValueCount, bones[0].transform.values.Length);
            Assert.Equal(TransformDataModel.LegacyValueCount, bones[1].transform.values.Length);
        }

        [Fact]
        public void Initialize_v37以降は変換しない()
        {
            var timeline = CreateTimeline(37,
                CreateModel("cup.menu", AttachPoint.Hand_R, 1),
                CreateModelKey("cup.menu"));

            timeline.Initialize();

            Assert.Equal(TransformDataModel.LegacyValueCount,
                timeline.layers[0].keyFrames[0].bones[0].transform.values.Length);
        }

        [Fact]
        public void モデル定義はアタッチを書き出さない()
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            var writer = new StringWriter();
            serializer.Serialize(writer, CreateModel("cup.menu", AttachPoint.Hand_R, 1));

            var xml = writer.ToString();
            Assert.DoesNotContain("<AttachPoint>", xml);
            Assert.DoesNotContain("<AttachMaidSlotNo>", xml);
            Assert.Contains("<Name>cup.menu</Name>", xml);
        }

        [Fact]
        public void 現行バージョンは37()
        {
            Assert.Equal(37, TimelineData.CurrentVersion);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: Task 1 Step 2 のビルド + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FullyQualifiedName~ModelAttachMigrationTests`
Expected: `Initialize_v36の...` / `パス付き` / `書き出さない` / `現行バージョンは37` が FAIL

- [ ] **Step 3: `TimelineModelXml` の書き出しを止める**

`TimelineXml.cs:11-21` を:

```csharp
    public class TimelineModelXml
    {
        [XmlElement("Name")]
        public string name;
        // version 37 でアタッチはモデルキーへ移った。旧 XML の読込 (Initialize の移行) でだけ使う
        [XmlElement("AttachPoint")]
        public AttachPoint attachPoint;
        [XmlElement("AttachMaidSlotNo")]
        public int attachMaidSlotNo = -1;
        [XmlElement("PluginName")]
        public string pluginName;

        public bool ShouldSerializeattachPoint() { return false; }
        public bool ShouldSerializeattachMaidSlotNo() { return false; }
    }
```

- [ ] **Step 4: `version < 37` 段を追加**

`TimelineXml.Initialize` の `version < 36` ブロックの直後（`ConvertPlugin();` の前）に:

```csharp
            if (version < 37)
            {
                ConvertModelAttachToKeys();
            }
```

`Initialize` の下（`OldRimlightValueCount` 定義の前）に:

```csharp
        /// <summary>
        /// モデル単位のアタッチ (&lt;Models&gt;) を、同名モデルのキー全部へ移す (version 37)。
        /// アタッチなしのモデルは触らない (旧 12 値のまま読めば TransformDataModel.FromXml が既定値へ補正する)
        /// </summary>
        private void ConvertModelAttachToKeys()
        {
            if (models == null || models.Count == 0)
            {
                return;
            }

            var attachMap = new Dictionary<string, TimelineModelXml>();
            foreach (var model in models)
            {
                if (model == null || string.IsNullOrEmpty(model.name) ||
                    !TransformDataModel.IsAttached(model.attachPoint, model.attachMaidSlotNo))
                {
                    continue;
                }
                attachMap[model.name] = model;
            }
            if (attachMap.Count == 0)
            {
                return;
            }

            foreach (var layer in layers)
            {
                foreach (var keyFrame in layer.keyFrames)
                {
                    if (keyFrame.bones == null)
                    {
                        continue;
                    }

                    foreach (var bone in keyFrame.bones)
                    {
                        var transform = bone.transform;
                        if (transform == null || transform.type != TransformType.Model || transform.name == null)
                        {
                            continue;
                        }

                        // TransformDataModel.FromXml と同じく、旧データのパス付き .menu 名はファイル名で引く
                        var name = transform.name.EndsWith(".menu", StringComparison.Ordinal)
                            ? Path.GetFileName(transform.name)
                            : transform.name;

                        TimelineModelXml model;
                        if (!attachMap.TryGetValue(name, out model))
                        {
                            continue;
                        }

                        var values = new float[TransformDataModel.LegacyValueCount + 3];
                        var copyCount = Math.Min(transform.values.Length, TransformDataModel.LegacyValueCount);
                        Array.Copy(transform.values, values, copyCount);
                        values[(int)TransformDataModel.Index.AttachMaidSlotNo] = model.attachMaidSlotNo;
                        values[(int)TransformDataModel.Index.AttachPoint] = (float)model.attachPoint;
                        values[(int)TransformDataModel.Index.WorldLerp] = 0f;
                        transform.values = values;

                        MTEUtils.LogDebug("Convert model attach to key name={0} frameNo={1} attach={2}/{3}",
                            name, keyFrame.frameNo, model.attachMaidSlotNo, model.attachPoint);
                    }
                }
            }
        }
```

`TimelineXml.cs` 冒頭の using に `System.IO` を追加する（`System` / `System.Collections.Generic` は既存）。`models` は `TimelineXml.cs:157` の `List<TimelineModelXml> models`（既定で空リスト）。

- [ ] **Step 5: `CurrentVersion` を 37 にする**

`TimelineData.cs:223`:

```csharp
        public static readonly int CurrentVersion = 37;
```

- [ ] **Step 6: テストが通ることを確認**

Run: Task 1 Step 2 のビルド + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS。バージョン番号を直接比較している既存テストが落ちたら 37 へ追従させる

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachMigrationTests.cs
git commit -m "feat(timeline): version 37 でモデル単位のアタッチをキーへ移行する"
```

---

### Task 3: 再生時の付け替え・ワールド補間・キー登録

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ModelHackBase.cs`（`IModelHack.unattachedParent`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs`（`unattachedParent` の記録）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs`（`UpdateAttachPointSilently` / `GetUnattachedParent`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs`（`ApplyAttach` / `GetAttachParent`、`SetupModels` のアタッチ除去、`UpdateAttachPoint` の `RequestHistory` 除去）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelTimelineLayer.cs`（`ApplyMotion` / `UpdateFrame`）
- Modify: `source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs`（アタッチコンボの操作履歴記録）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`TimelineModelData` のアタッチ欄に未使用の注記）
- Modify: `docs/superpowers/specs/2026-09-24-model-attach-keyframe-design.md`（履歴の扱いを実装に合わせる）

**Interfaces:**
- Consumes: `TransformDataModel.attachPoint` / `attachMaidSlotNo` / `worldLerp` / `IsAttached` / `InheritKeySettings`（Task 1）
- Produces:
  - `Transform IModelHack.unattachedParent { get; }`（`ModelHackBase` は null）
  - `void ModelHackManager.UpdateAttachPointSilently(StudioModelStat model)`
  - `Transform ModelHackManager.GetUnattachedParent(StudioModelStat model)`
  - `bool StudioModelManager.ApplyAttach(StudioModelStat model, AttachPoint attachPoint, int attachMaidSlotNo)`（変化したら true）
  - `Transform StudioModelManager.GetAttachParent(StudioModelStat model, AttachPoint attachPoint, int attachMaidSlotNo)`（解決できなければ null）

Unity のシーンが要るため単体テストは書かない（テストで Unity ネイティブ呼び出し不可）。ビルドと Task 6 の実機確認で担保する。

- [ ] **Step 1: `IModelHack.unattachedParent` を追加**

`ModelHackBase.cs` の `IModelHack` に:

```csharp
        void SetModelVisible(StudioModelStat model, bool visible);

        /// <summary>アタッチしていないモデルの親。ワールド補間でアタッチなし側の座標系に使う。不明なら null</summary>
        Transform unattachedParent { get; }
    }
```

`ModelHackBase` に:

```csharp
        public virtual Transform unattachedParent => null;
```

`ModelHackBase.cs` の using に `UnityEngine` が無ければ追加する（`Transform` のため）。

- [ ] **Step 2: `ExternalModelHack` で未アタッチの親を覚える**

フィールドとプロパティ:

```csharp
        private Transform _unattachedParent;

        /// <summary>
        /// プロバイダがアタッチなしのモデルを置く親。プロバイダ API に取得手段が無いため、
        /// アタッチしていないと確定している時点 (生成直後・解除直後) の親を控える
        /// </summary>
        public override Transform unattachedParent => _unattachedParent;
```

`CreateModel` の `model.transform = obj.transform;` の直前に:

```csharp
            // UpdateAttachPoint より前なので、ここでの親はプロバイダの配置ルート
            _unattachedParent = obj.transform.parent;
```

`UpdateAttachPoint` の末尾（`_provider.attachModel(...)` の後）に:

```csharp
            // 解除した直後の親は配置ルートと確定しているので控え直す。
            // 最初に見つかったときの親は、プロバイダ側で既にアタッチ済みだとボーンを拾ってしまうため使わない
            if (maid == null)
            {
                _unattachedParent = obj.transform.parent;
            }
```

`GetOrCreateStat` の新規作成経路（`var stat = modelManager.CreateModelStat(` の直前）では、親の上位にメイドがいないときだけ控える（ModItemExplorer の UI から直接置いたモデルは `CreateModel` を通らないため、ここでも拾う必要がある）:

```csharp
            // プロバイダ側で直接置かれたモデルは生成を経ないのでここで控える。
            // 既にメイドへアタッチ済みならボーンを拾ってしまうため、親の上位にメイドがいないときだけ
            var parent = obj.transform.parent;
            if (_unattachedParent == null && parent != null && parent.GetComponentInParent<Maid>() == null)
            {
                _unattachedParent = parent;
            }
```

どれも経ていない間は null のままで、ワールド補間はローカル補間へフォールバックする。

- [ ] **Step 3: `ModelHackManager` に履歴なし付け替えと親取得を追加**

`UpdateAttachPoint` の下に:

```csharp
        /// <summary>
        /// 再生中の付け替え用。プロバイダ側の配置履歴を積まないよう一括操作として囲む
        /// </summary>
        public void UpdateAttachPointSilently(StudioModelStat model)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack == null)
                {
                    return;
                }

                var externalHack = modelHack as ExternalModelHack;
                externalHack?.BeginBatch();
                try
                {
                    modelHack.UpdateAttachPoint(model);
                }
                finally
                {
                    externalHack?.EndBatch();
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public Transform GetUnattachedParent(StudioModelStat model)
        {
            var modelHack = GetOrDefault(model.pluginName);
            return modelHack != null ? modelHack.unattachedParent : null;
        }
```

`using UnityEngine;` が無ければ追加する。

- [ ] **Step 4: `StudioModelManager` を変更**

(a) `UpdateAttachPoint`（L571 付近）から `RequestHistory` を外す。アタッチは version 37 でキーの値になったため、UI の付け替えは Step 7 の `RecordEdit` 経由の自動キー登録（または手動のキー登録）で履歴に残る。ここで積むと XML に差分の無い空エントリになる:

```csharp
        public void UpdateAttachPoint(StudioModelStat model)
        {
            modelHackManager.UpdateAttachPoint(model);
            LateUpdate(true);
        }
```

(b) その下に追加:

```csharp
        /// <summary>
        /// 再生・シークでキーのアタッチ先へ付け替える。履歴は積まない。
        /// 現在と同じなら何もせず false を返す (プロバイダは付け替えでローカル位置・回転を 0 に戻すため、
        /// true のときは呼び出し側でローカル値を入れ直すこと)
        /// </summary>
        public bool ApplyAttach(StudioModelStat model, AttachPoint attachPoint, int attachMaidSlotNo)
        {
            if (!TransformDataModel.IsAttached(attachPoint, attachMaidSlotNo))
            {
                attachPoint = AttachPoint.Null;
                attachMaidSlotNo = -1;
            }

            if (model.attachPoint == attachPoint && model.attachMaidSlotNo == attachMaidSlotNo)
            {
                return false;
            }

            model.attachPoint = attachPoint;
            model.attachMaidSlotNo = attachMaidSlotNo;
            modelHackManager.UpdateAttachPointSilently(model);
            return true;
        }

        /// <summary>
        /// キーのアタッチ先に対応する親 Transform。ワールド補間の座標変換に使う。
        /// メイドやボーンが見つからなければ null
        /// </summary>
        public Transform GetAttachParent(StudioModelStat model, AttachPoint attachPoint, int attachMaidSlotNo)
        {
            if (!TransformDataModel.IsAttached(attachPoint, attachMaidSlotNo))
            {
                return modelHackManager.GetUnattachedParent(model);
            }

            var maidCache = maidManager.GetMaidCache(attachMaidSlotNo);
            return maidCache != null ? maidCache.GetAttachPointTransform(attachPoint) : null;
        }
```

`StudioModelManager` から `maidManager` が見えない場合は `private static MaidManager maidManager => MaidManager.instance;` を足す（`ModelHackBase.cs:39` と同じ書き方）。

(c) `SetupModels`（L415-446）でアタッチを使わない。生成時は:

```csharp
                        model = CreateModelStat(
                            modelData.name,
                            null,
                            AttachPoint.Null,
                            -1,
                            null,
                            modelData.pluginName,
                            true);
```

既存モデルの分岐は:

```csharp
                    else if (model.pluginName != modelData.pluginName)
                    {
                        modelHackManager.ChangePluginName(model, modelData.pluginName);
                    }
```

にし、`model.attachPoint = ...` / `model.attachMaidSlotNo = ...` / `modelHackManager.UpdateAttachPoint(model)` を消す（アタッチはレイヤーの `ApplyCurrentFrame` で入る）。この 2 箇所の直前に次のコメントを置く:

```csharp
                    // アタッチは version 37 からモデルキーの値。ここでは付けず、レイヤーの適用に任せる
```

- [ ] **Step 5: `ModelTimelineLayer` の適用を書き換える**

クラス先頭に定数:

```csharp
        /// <summary>
        /// ステップ値 (アタッチ先) を終点側へ切り替える区間内位置。
        /// 始点側だけで採ると最後のキーの値が永久に効かない (MorphTimelineLayer と同じ)
        /// </summary>
        private const float StepEndThreshold = 0.99f;
```

`ApplyMotion` / `ApplyMotionInit` / `ApplyMotionUpdateTangent` を次に置き換える:

```csharp
        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var model = modelManager.GetModel(motion.name);
            if (model == null || model.transform == null)
            {
                return;
            }

            var start = motion.start as TransformDataModel;
            var end = motion.end as TransformDataModel;
            if (start == null || end == null)
            {
                return;
            }

            var attachKey = t < StepEndThreshold ? start : end;
            var attachChanged = modelManager.ApplyAttach(
                model, attachKey.attachPoint, attachKey.attachMaidSlotNo);

            // 付け替えはローカル位置・回転を 0 に戻すので、区間頭と同じく入れ直す
            if (indexUpdated || attachChanged)
            {
                ApplyMotionInit(motion, model);
            }

            // 同値区間は ApplyMotionInit が入れた start の値のままでよい (MotionData.isConstant)
            if (motion.isConstant)
            {
                return;
            }

            if (end.worldLerp && t < StepEndThreshold && ApplyMotionWorldLerp(motion, t, model, start, end))
            {
                return;
            }

            ApplyMotionUpdateTangent(motion, t, model);
        }

        private void ApplyMotionInit(MotionData motion, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;

            transform.localPosition = start.position;
            transform.localRotation = start.rotation;
            transform.localScale = start.scale;

            modelManager.SetModelVisible(model, start.visible && modelManager.Visible);
            model.visible = start.visible;
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;
            var end = motion.end;
            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            transform.localPosition = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            transform.localRotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
        }

        /// <summary>
        /// 始点キー (始点の親基準) と終点キー (そのフレームの終点の親基準) のワールド姿勢を線形補間する。
        /// タンジェントはローカル座標系の傾きなので使わない。親が解決できなければ false (ローカル補間へ戻す)
        /// </summary>
        private bool ApplyMotionWorldLerp(
            MotionData motion,
            float t,
            StudioModelStat model,
            TransformDataModel start,
            TransformDataModel end)
        {
            var startParent = modelManager.GetAttachParent(model, start.attachPoint, start.attachMaidSlotNo);
            var endParent = modelManager.GetAttachParent(model, end.attachPoint, end.attachMaidSlotNo);
            if (startParent == null || endParent == null)
            {
                return false;
            }

            var transform = model.transform;
            transform.position = Vector3.Lerp(
                startParent.TransformPoint(start.position),
                endParent.TransformPoint(end.position),
                t);
            transform.rotation = Quaternion.Slerp(
                startParent.rotation * start.rotation,
                endParent.rotation * end.rotation,
                t);

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
            return true;
        }
```

`ApplyMotionInit` の引数から未使用の `t` を落としたので、呼び出しは上の 1 箇所だけであることを確認する。

- [ ] **Step 6: `UpdateFrame` でアタッチを記録しワールド補間を引き継ぐ**

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // 呼び出し元は一時フレームを渡すことが多いので、引き継ぎ元は登録済みキーから引く
            var existingFrame = GetFrame(frame.frameNo);

            foreach (var model in modelManager.models)
            {
                var modelName = model.name;

                var trans = CreateTransformData<TransformDataModel>(modelName);
                trans.position = model.transform.localPosition;
                trans.rotation = model.transform.localRotation;
                trans.scale = model.transform.localScale;
                trans.visible = model.visible;
                if (TransformDataModel.IsAttached(model.attachPoint, model.attachMaidSlotNo))
                {
                    trans.attachMaidSlotNo = model.attachMaidSlotNo;
                    trans.attachPoint = model.attachPoint;
                }
                else
                {
                    trans.attachMaidSlotNo = -1;
                    trans.attachPoint = AttachPoint.Head;
                }

                var existingBone = existingFrame != null ? existingFrame.GetBone(modelName) : null;
                trans.InheritKeySettings(existingBone != null ? existingBone.transform as TransformDataModel : null);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }
```

`GetFrame(int)` は `TimelineLayerBase` の既存メソッド（`AddKeyFrameDiffBones` で使用）。未アタッチ時に -1 / `Head` へ正規化するのは、「メイド選択済みで部位 Null」のような半端な状態をアタッチ扱いで記録しないためと、アタッチなし状態の部位値の違いでキー差分が出て不要なキーが増えないようにするため（既定値と揃える）。ファイル冒頭に `using AttachPoint = PhotoTransTargetObject.AttachPoint;` を namespace 内に追加する（`ModelTimelineLayerBase.cs:7` と同じ書き方）。

- [ ] **Step 7: モデル行のアタッチコンボを自動キー登録へ載せる**

`ModelManageRowDrawer.Draw` のメイド・部位コンボは引数なしの `view.BeginAutoEditMode()` 区間にあり、`AutoEditMode.Enter` しか呼ばない。自動キー登録（`TimelineWindow.OnEditCommitted` → `TryAutoKeyFrame`）は `HistoryManager.BeforeEdit` の確定でしか発火しないため、このままではコンボで変えたアタッチがキーに入らず、編集モードを抜けると始点キーの値へ戻る。位置・回転の行（`ObjectTransformRowDrawer.RecordEdit`）と同じ記録を、値を書き換える前に入れる。

`ModelManageRowDrawer.cs` の 2 つの `onSelected` を:

```csharp
                _maidComboBox.onSelected = (maidCache, index) =>
                {
                    // 位置・回転の行と同じく操作履歴へ記録し、確定時の自動キー登録に載せる
                    RecordAttachEdit(model);
                    model.attachMaidSlotNo = index - 1;
                    if (model.attachPoint == AttachPoint.Null)
                    {
                        model.attachPoint = AttachPoint.Head;
                    }
                    modelManager.UpdateAttachPoint(model);
                };
```

```csharp
                    _attachPointComboBox.onSelected = (_, index) =>
                    {
                        RecordAttachEdit(model);
                        model.attachPoint = (AttachPoint)index;
                        modelManager.UpdateAttachPoint(model);
                    };
```

にし、クラスに:

```csharp
        private static void RecordAttachEdit(MTEP.StudioModelStat model)
        {
            if (model.transform != null)
            {
                ObjectTransformRowDrawer.RecordEdit(model.transform.gameObject);
            }
        }
```

を足す（`ObjectTransformRowDrawer` は同じ `COM3D2.SceneEditor.Plugin` 名前空間）。

`TimelineData.cs` の `TimelineModelData` の `attachPoint` / `attachMaidSlotNo` 宣言の直前に、誤って直す対象にされないよう注記する:

```csharp
        // version 37 以降は保存されない (TimelineModelXml.ShouldSerialize*)。アタッチの正本はモデルキー (TransformDataModel)
```

- [ ] **Step 8: spec の履歴の記述を実装に合わせる**

`docs/superpowers/specs/2026-09-24-model-attach-keyframe-design.md` の §3 の

```
- モデル行（`ModelManageRowDrawer`）のメイド・部位コンボは「現在のアタッチ状態を変える」操作として残す。既存どおり UI 操作では「アタッチ変更」の Undo 履歴を積む。キーへの記録は通常のキー登録で行う
```

を

```
- モデル行（`ModelManageRowDrawer`）のメイド・部位コンボは「現在のアタッチ状態を変える」操作として残す。キーへの記録と Undo 履歴は通常のキー登録（自動登録なら「キーフレーム登録」）で行う。アタッチがキーの値になったため、コンボ操作単体で「アタッチ変更」を積むと XML 差分の無い空エントリになるので積まない
```

に置き換える。

- [ ] **Step 9: 両構成ビルドと全テスト**

Run: Task 1 Step 2 のビルド + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ビルド成功、全件 PASS

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ModelHackBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs docs/superpowers/specs/2026-09-24-model-attach-keyframe-design.md
git commit -m "feat(timeline): モデルレイヤーの再生でキーのアタッチ先へ付け替えワールド補間に対応する"
```

---

### Task 4: キーフレームインスペクタに部位コンボを出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFollowCustomValueDrawer.cs`

**Interfaces:**
- Consumes: `CustomValueUIType.AttachPoint`（Task 1）、`BoneUtils.AttachPointNames`（`MTEUtils/BoneUtils.cs:577`、`AttachPoint` の enum 順と同じ並び）
- Produces: `MaidFollowCustomValueDrawer.IsComboValue` が `AttachPoint` でも true。`KeyFrameInspector` / `KeyFrameBatchDrawer` は既にこの判定で分岐しているので追加変更なし

メイド選択（`attachMaidSlotNo`）は既存の `MaidSlot` コンボがそのまま使われる。ワールド補間は Bool なので既存のトグル描画に乗る。

- [ ] **Step 1: 判定とディスパッチに `AttachPoint` を足す**

```csharp
        /// <summary>このカスタム値をコンボで描くか (対象は MaidSlot / MaidPoint / AttachPoint)</summary>
        public static bool IsComboValue(MTEP.CustomValueInfo info)
        {
            return info.uiType == MTEP.CustomValueUIType.MaidSlot
                || info.uiType == MTEP.CustomValueUIType.MaidPoint
                || info.uiType == MTEP.CustomValueUIType.AttachPoint;
        }
```

`Draw` の switch に:

```csharp
                case MTEP.CustomValueUIType.AttachPoint:
                    DrawAttachPointCombo(view, rowKey, info, value, labelWidth, rowHeight, onChanged);
                    break;
```

- [ ] **Step 2: コンボ本体を追加**

フィールド（`_pointComboBoxes` の下）:

```csharp
        private readonly Dictionary<RowKey, GUIComboBox<string>> _attachPointComboBoxes =
            new Dictionary<RowKey, GUIComboBox<string>>();
```

`EndFrame` に `Sweep(_attachPointComboBoxes);`、`Clear` に `_attachPointComboBoxes.Clear();` を足す。

`DrawMaidPointCombo` の下に:

```csharp
        private void DrawAttachPointCombo(
            GUIView view,
            RowKey rowKey,
            MTEP.CustomValueInfo info,
            float value,
            float labelWidth,
            float rowHeight,
            Action<float> onChanged)
        {
            GUIComboBox<string> comboBox;
            if (!_attachPointComboBoxes.TryGetValue(rowKey, out comboBox))
            {
                // 並びは PhotoTransTargetObject.AttachPoint の enum 順なので、添字がそのまま値になる
                comboBox = new GUIComboBox<string>
                {
                    items = BoneUtils.AttachPointNames,
                    getName = (name, _) => name,
                };
                _attachPointComboBoxes[rowKey] = comboBox;
            }

            comboBox.defaultName = float.IsNaN(value) ? MixedName : null;
            comboBox.currentIndex = float.IsNaN(value)
                ? MixedIndex
                : Mathf.Clamp(Mathf.RoundToInt(value), 0, BoneUtils.AttachPointNames.Count - 1);
            comboBox.onSelected = (_, index) => onChanged(index);

            LabeledComboRow.Draw(view, info.name, comboBox, labelWidth, rowHeight);
        }
```

`getName` は `Func<T, int, string>`（`MTEUtils/GUIComboBox.cs:43`）で、`ModelManageRowDrawer.cs:34-38` の `_attachPointComboBox` と同じ初期化。`BoneUtils` の名前空間が見えなければ `ModelManageRowDrawer.cs` の using に合わせる。

- [ ] **Step 3: 両構成ビルド**

Run: Task 1 Step 2 のビルド
Expected: 両構成成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFollowCustomValueDrawer.cs
git commit -m "feat(inspector): モデルキーのアタッチ部位をドロップダウンで編集できるようにする"
```

---

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/timeline/layers-model.md:15`（アタッチの記述）と「モデル」節の表
- Modify: `docs-site/timeline/compatibility.md:8` と箇条書き
- Modify: `W:\COM3D2_5\work\CLAUDE.md`「タイムライン XML の互換方向」（このリポジトリ外のワークスペース CLAUDE.md。git 管理外なのでコミット対象外）

- [ ] **Step 1: `layers-model.md` を更新**

L15 を削除し、「モデル」節の表と箇条書きを:

```markdown
配置モデルの位置・回転・拡縮・表示・アタッチです。ギズモや Inspector、ModItemExplorer で編集します。

| 値 | 補間 |
|---|---|
| 位置 / 回転 / 拡縮 | タンジェント補間 |
| `表示` | なし（区間開始値） |
| `アタッチ先` / `アタッチ部位` | なし（区間開始値。最後のキーでは終点の値） |
| `ワールド補間` | このキーへ入る区間の補間方法（下記） |

- モデルの追加・削除に合わせて項目が増減し、追加時は 0 フレーム目にキーが打たれます
- モデルを複製すると、複製元のキーが複製先へコピーされます（モデルボーン・シェイプ・マテリアルも同様）
- アタッチ先はキーごとに持てるので、途中で持ち替えられます。Inspector のモデル行でアタッチ先を変えてキーを登録するか、キーフレーム詳細で直接変えます
- 位置・回転はアタッチ先ボーン（アタッチなしなら配置ルート）からの相対値です。アタッチ先が変わるキーでは、そのボーン基準の値を入れてください
- `ワールド補間` が OFF のキーでは、アタッチ先が違う区間はそのキーで切り替わります。滑らかにつなぎたいときは直前にもキーを打ちます
- `ワールド補間` を ON にしたキーへ入る区間は、前のキーの位置から「動いているアタッチ先に対するこのキーの位置」へワールド座標で直線的に移動します（緩急は中間キーで付けます）。この区間では位置・回転のタンジェントは使われず、曲線エディタの表示と実際の動きは一致しません
```

- [ ] **Step 2: `compatibility.md` を更新**

L8 の `version 35` を `version 37` にする。箇条書きの末尾（`姿勢を表す回転は…` の前）に:

```markdown
- モデルのアタッチ先はモデル定義からキーフレームへ移りました（version 37）。読み込み時に全キーへ自動で移します。MTE ではモデルのキーは読めますが、アタッチは失われます
```

- [ ] **Step 3: ワークスペース CLAUDE.md を更新**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」の箇条書き末尾に:

```markdown
- version 37 でモデルのアタッチ（`<Models>` の `AttachPoint` / `AttachMaidSlotNo`）はモデルキーの index 12〜14（アタッチ先メイド・部位・ワールド補間）へ移り、モデル定義には書き出さない。旧 XML は `TimelineXml.ConvertModelAttachToKeys` が同名モデルの全キーへ移す。MTE ではモデルキー 15 値は読めるがアタッチは失われる
```

- [ ] **Step 4: コミット（docs-site のみ）**

```bash
git add docs-site/timeline/layers-model.md docs-site/timeline/compatibility.md
git commit -m "docs(timeline): モデルのアタッチのキーフレーム化を反映"
```

---

### Task 6: 実機確認

`com3d25-restart-verify` スキルの手順でゲームを再起動し、新 DLL・最新セーブで確認する。REPL では Unity 型を完全修飾名で書く。状態を変える eval と読む eval は分ける。

- [ ] **Step 1: 準備**

新規タイムライン → `ac-cuteaudiop_i_.menu` を `StudioModelManager.CreateModel` で配置 → 0F・10F・20F にモデルキーを登録（`ModelTimelineLayer` の `AddKeyFrames` 相当、または `UpdateFrame(GetOrCreateFrame(n), force: true)`）。10F はアタッチなし、20F はメイド 0 / `Hand_R`

- [ ] **Step 2: キーで切替（ワールド補間 OFF）**

15F・19F・20F・最終フレームへシークし、モデルの `transform.parent.name` と `StudioModelStat.attachPoint` を読む
Expected: 15F / 19F は配置ルート、20F と最終フレームは `Bip01 R Hand`

- [ ] **Step 3: ワールド補間 ON**

20F キーの `worldLerp = true` にしてから 10F・15F・19F の `transform.position` を読む
Expected: 10F は 10F キーの位置、15F は 10F の位置と右手の位置の中間付近、19F は右手の近く。OFF 時と 15F の値が違う

- [ ] **Step 4: 履歴が増えない**

`SceneEditor.Plugin.HistoryManager.instance` の `_entries.Count` を控えて 0F→20F を再生・シークを数回してから再度読む。ModItemExplorer 側の履歴件数も同様に比較する
Expected: 両方とも件数が変わらない

- [ ] **Step 5: メイド不在のフォールバック**

20F キーを存在しないスロット（例: 9）に書き換えて 15F・20F へシーク
Expected: 例外なし。20F はアタッチなし（配置ルート）、`tail_log` にエラーが無い

- [ ] **Step 6: v36 データの読込**

テスト用に v36 形式（モデル単位 `AttachPoint=Head, AttachMaidSlotNo=0`、キー 12 値）の `TimelineXml` をメモリ上で作り、`Initialize` → `TimelineData.FromXml` 相当で読み込む（ファイルは作らない）
Expected: 読込後に 0F で頭にアタッチされる。保存用 XML（`ToXml` のシリアライズ）に `<Models>` の `AttachPoint` が無く、モデルキーが 15 値

- [ ] **Step 7: モデル行のコンボ操作がキーに入る**

「自動登録」ON・10F にシークした状態で、Inspector のモデル行と同じ手順（`ObjectTransformRowDrawer.RecordEdit` → アタッチ値変更 → `StudioModelManager.UpdateAttachPoint`）を実行し、`HistoryManager` の確定（数フレーム待つ、またはマウスを離した扱い）後に 10F キーの `attachPoint` / `attachMaidSlotNo` を読む。続けて再生を始め、10F に戻す
Expected: 10F キーにコンボで選んだアタッチが入っている。再生・シーク後もその値で適用される

- [ ] **Step 8: 未アタッチ親の推定**

ModItemExplorer の UI 経路（`ModelPlacerProvider` 側の生成）で置いたモデルを SE が初めて検出したときの `ExternalModelHack.unattachedParent` を読む。先にメイドへアタッチしてから SE に検出させた場合も読む
Expected: どちらも配置ルート（`ModItemExplorer Model Parent`）または null。メイドのボーンにならない（`Transform.GetComponentInParent<Maid>()` でボーンが弾けることの確認）

- [ ] **Step 9: 後始末**

テスト用モデルを削除する。結果（eval の戻り値・`tail_log` 抜粋・DLL ハッシュ一致）を記録する

## レビュー却下メモ

- v37 移行でモデル名に加えて pluginName でも突き合わせるべき — 同一タイムライン内のモデル名は `StudioModelManager.modelMap` のキーで一意（プロバイダが違っても同名は共存しない）ため誤移行は起きない。誤検知
