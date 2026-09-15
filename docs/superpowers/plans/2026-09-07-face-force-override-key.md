# 表情の強制上書きキーフレーム化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans` でタスクごとに実装すること（本リポジトリでは subagent-driven-development は使わない）。手順は `- [ ]` チェックボックスで追跡する。

**Goal:** 表情の「強制上書き」（まばたき抑止）を `MorphTimelineLayer` のキーフレームとして持たせ、特定フレームで ON/OFF を切り替えられるようにする。

**Architecture:** モーフ値とは別の `TransformType.FaceSetting` を新設し、`MorphTimelineLayer` に `faceForceOverride` という特殊ボーンを 1 本だけ常設する。`EyesTimelineLayer` が `LookAtTarget` を同一レイヤーに混ぜている前例（`EyesTimelineLayer.cs:304`）と同じ形。値は 0/1 のステップで、キーが無い XML（既存データ・MTE 産データ）は ON 扱いにする。

**Tech Stack:** C# 9 / net48 / xunit 2.9 / MSBuild（`GameVersion=COM3D2` と `COM3D25` の 2 構成）

**Spec:** 本計画の「設計（合意済み）」節が仕様書を兼ねる（別 spec ファイルは作らない）。

## Global Constraints

- コードのコメント・ログメッセージは日本語で書く
- ビルド確認に `debug.bat` を使わない（ゲームフォルダへ DLL をコピーするため）。MSBuild を直接叩く
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。テストプロジェクトは COM3D25 構成のプラグイン出力を参照するので、先に COM3D25 構成でビルドする
- 2 構成（COM3D2 / COM3D25）でビルドが通ること
- `TransformType` の enum 値は XML に名前で保存される（`TransformXml.type`）。int キャストの利用箇所は無いので、アルファベット順の位置へ挿入してよい

## 設計（合意済み）

### データ

- `TransformType.FaceSetting` を追加（`ITransformData.cs` の enum、`Eyes` と `FingerBlend` の間＝アルファベット順の位置）
- `TransformDataFaceSetting`: 値 1 個、`CustomValueInfo { name = "強制上書き", defaultValue = 1f }`、タンジェント無し
- ボーン名は ASCII の `faceForceOverride`（XML の `<Name>`）。表示名は「強制上書き」
- 既存の `FaceMorphUtils.faceOptionMorphJp`（頬・涙などのモーフ）と紛らわしいため、型名に `FaceOption` は使わない

### レイヤー（`MorphTimelineLayer`）

- `allBoneNames` は `faceForceOverride` ＋ `trackedBoneNames`。`trackedCandidateNames`（＝`saveMorphNames`）には入れないので、チェック連動の自動追加・自動削除（`TimelineLayerBaseTracking.cs`）の対象外で常に表示される
- `GetTransformType(name)`: `faceForceOverride` → `FaceSetting`、それ以外 → `Morph`
- `ApplyMotion`: `FaceSetting` は `_applyMorphMap` に入れず `_isForceOverride` へ。補間せず、`t < 0.99f` なら `start`、それ以外は `end` の値を採る（既存のステップモーフと同じ扱い。終端キーの値が効かなくなるのを防ぐ）
- `Init()` で 0F にキーが無ければ既定 ON のキーを打つ。`BuildPlayData`（`TimelineLayerBase.cs:376`）はボーンごとに `rows.Count - 1` 個の `MotionData` しか作らないため、キーが 1 個だけだと `MotionPlayData.Update` が即 false を返して `ApplyMotion` が一度も呼ばれない。強制上書きは `trackedCandidateNames` に入れない設計上、`AddTrackedFirstFrameKeys` の 0F 自動登録が効かないので、レイヤー側で 0F キーを保証する
- `ApplyPlayData`: 毎フレーム `_isForceOverride = true` で初期化 → `base.ApplyPlayData()` → `UpdateMabatakiSuppression(_isForceOverride ? maid : null)`。**キーが無ければ ON**（既存 XML・MTE 産 XML の互換）
- モーフ適用（`SetMorphValue`）は強制上書き OFF 中も従来どおり続ける
- `UpdateFrame`: `faceForceOverride` だけ現在の実効値（下記 `IsForceOverride`）を書く

### UI

- 実効値の読み書きは `MaidFaceMorphController` に集約する
  - `IsForceOverride(maid)` = `IsMabatakiSuppressed(maid) || !GetMabataki(maid)`（抑止中は必ず ON が効いているため）
  - `SetForceOverride(maid, value)` = `SetMabataki(maid, !value)`
- `MaidFaceWindow.DrawHeader`: `IsForceOverride` / `SetForceOverride` を使い、グレーアウト（`SetEnabled`）を廃止
- `MorphItemInspector`: `faceForceOverride` の行を専用トグルで描く（現状は `FindDef` が null を返して「(未対応)」のラベルになる）
- 編集はポーズ編集中（レイヤーが適用を止めている状態）に行い、キー登録時に `UpdateFrame` が現在値を読む。再生中に触ると次フレームでキー値に戻るのはモーフ行と同じ挙動

### 互換

- 読み込み: `faceForceOverride` ボーンが無い XML は ON。`TimelineData.CurrentVersion` は 33 のまま（`Migrate` 段は追加しない）
- 書き出し: このキーを含む XML は `<Type>FaceSetting</Type>` を持つため MTE では読めない。MTE → SE の一方向互換のみ保証する既存方針の範囲内だが、ワークスペースの `CLAUDE.md` に明記する

---

## Task 1: 値の型と実効値ロジック

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（enum `TransformType`）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataFaceSetting.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（`RegisterTransform` の並び、`TransformType.Eyes` 登録の直前）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/FaceMorphUtils.cs`（末尾に定数と変換）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceForceOverrideTests.cs`（新規）

**Interfaces:**
- Consumes: なし
- Produces:
  - `MTEP.TransformType.FaceSetting`
  - `MTEP.TransformDataFaceSetting`（`forceOverride` プロパティ: `float`）
  - `MTEP.FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME` (`"faceForceOverride"`)
  - `MTEP.FaceMorphUtils.FORCE_OVERRIDE_DISPLAY_NAME` (`"強制上書き"`)
  - `MTEP.FaceMorphUtils.FORCE_OVERRIDE_SET_NAME` (`"faceSetting"`) / `FORCE_OVERRIDE_SET_DISPLAY_NAME` (`"表情設定"`)
  - `static bool MTEP.FaceMorphUtils.ToForceOverride(float value)`
  - `static float MTEP.FaceMorphUtils.ToForceOverrideValue(bool enabled)`
  - `static bool MTEP.FaceMorphUtils.ResolveForceOverride(bool isMabatakiSuppressed, bool mabataki)`
  - `static bool COM3D2.SceneEditor.Plugin.MaidFaceMorphController.IsForceOverride(Maid maid)`
  - `static void COM3D2.SceneEditor.Plugin.MaidFaceMorphController.SetForceOverride(Maid maid, bool enabled)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/FaceForceOverrideTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 強制上書きキーの値変換と実効値の決定を固定する。
    /// レイヤー本体は Unity 依存で単体テストできないため、判定だけを純関数として切り出している
    /// </summary>
    public class FaceForceOverrideTests
    {
        [Theory]
        [InlineData(0f, false)]
        [InlineData(0.49f, false)]
        [InlineData(0.5f, true)]
        [InlineData(1f, true)]
        public void 値はしきい値05でONOFFに変換される(float value, bool expected)
        {
            Assert.Equal(expected, FaceMorphUtils.ToForceOverride(value));
        }

        [Fact]
        public void ONは1でOFFは0へ変換される()
        {
            Assert.Equal(1f, FaceMorphUtils.ToForceOverrideValue(true));
            Assert.Equal(0f, FaceMorphUtils.ToForceOverrideValue(false));
        }

        [Fact]
        public void 抑止中は退避値によらず実効値がONになる()
        {
            Assert.True(FaceMorphUtils.ResolveForceOverride(true, true));
            Assert.True(FaceMorphUtils.ResolveForceOverride(true, false));
        }

        [Fact]
        public void 非抑止中はまばたきの反転が実効値になる()
        {
            Assert.False(FaceMorphUtils.ResolveForceOverride(false, true));
            Assert.True(FaceMorphUtils.ResolveForceOverride(false, false));
        }

        [Fact]
        public void 強制上書きキーの既定値はONである()
        {
            var trans = new TransformDataFaceSetting();
            trans.Initialize(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);

            Assert.Equal(TransformType.FaceSetting, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.True(FaceMorphUtils.ToForceOverride(trans.forceOverride));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FaceForceOverrideTests`
Expected: コンパイルエラー（`TransformDataFaceSetting` / `ToForceOverride` などが未定義）

- [ ] **Step 3: enum に `FaceSetting` を足す**

`ITransformData.cs` の `TransformType`、`Eyes,` の次の行に挿入する（アルファベット順。`Ey` < `Fa` < `Fi`）:

```csharp
        Eyes,
        FaceSetting,
        FingerBlend,
```

- [ ] **Step 4: `TransformDataFaceSetting` を作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataFaceSetting.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情レイヤーのモーフ以外の設定値。現状は強制上書き (まばたき抑止) の 1 値のみ。
    /// モーフ値 (TransformDataMorph) と型を分けているのは、適用時に
    /// モーフ名として TMorph へ流さないため
    /// </summary>
    public class TransformDataFaceSetting : TransformDataBase
    {
        public enum Index
        {
            ForceOverride = 0
        }

        public override TransformType type => TransformType.FaceSetting;

        public override int valueCount => 1;

        public TransformDataFaceSetting()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "forceOverride",
                new CustomValueInfo
                {
                    index = (int)Index.ForceOverride,
                    // キーが無い既存データを ON 扱いにするため、既定値は 1 にする
                    name = "強制上書き",
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData forceOverrideValue => values[(int)Index.ForceOverride];

        public float forceOverride
        {
            get => forceOverrideValue.value;
            set => forceOverrideValue.value = value;
        }
    }
}
```

- [ ] **Step 5: `FaceMorphUtils` に定数と変換を足す**

`FaceMorphUtils.cs` の `IsStepMorph` の直後（クラス末尾）に追加:

```csharp
        /// <summary>強制上書きキーのボーン名。XML の Bone 名なので変更しないこと</summary>
        public const string FORCE_OVERRIDE_BONE_NAME = "faceForceOverride";

        /// <summary>強制上書きキーの表示名</summary>
        public const string FORCE_OVERRIDE_DISPLAY_NAME = "強制上書き";

        /// <summary>強制上書きキーを入れるボーンメニューのセット名</summary>
        public const string FORCE_OVERRIDE_SET_NAME = "faceSetting";

        /// <summary>強制上書きキーのセット表示名</summary>
        public const string FORCE_OVERRIDE_SET_DISPLAY_NAME = "表情設定";

        /// <summary>キー値 (0/1) を ON/OFF へ変換する</summary>
        public static bool ToForceOverride(float value)
        {
            return value >= 0.5f;
        }

        /// <summary>ON/OFF をキー値 (0/1) へ変換する</summary>
        public static float ToForceOverrideValue(bool enabled)
        {
            return enabled ? 1f : 0f;
        }

        /// <summary>
        /// 強制上書きの実効値。タイムラインが抑止しているときは
        /// 退避されたユーザー設定によらず ON が効いている
        /// </summary>
        public static bool ResolveForceOverride(bool isMabatakiSuppressed, bool mabataki)
        {
            return isMabatakiSuppressed || !mabataki;
        }
```

- [ ] **Step 6: `MaidFaceMorphController` に実効値 API を足す**

`MaidFaceMorphController.cs` の `GetMabataki` の直後に追加する（Task 2 の `UpdateFrame` と Task 3 の UI が両方これを使う）:

```csharp
        /// <summary>
        /// 強制上書きの実効値。タイムラインの表情レイヤーが抑止している間は
        /// 退避されたユーザー設定によらず ON が効いている
        /// </summary>
        public static bool IsForceOverride(Maid maid)
        {
            if (maid == null)
            {
                return false;
            }

            return MTEP.FaceMorphUtils.ResolveForceOverride(
                IsMabatakiSuppressed(maid), GetMabataki(maid));
        }

        /// <summary>強制上書きの切り替え。抑止中は退避値へ書き、解除時に反映される</summary>
        public static void SetForceOverride(Maid maid, bool enabled)
        {
            SetMabataki(maid, !enabled);
        }
```

`MaidFaceMorphController.cs` の using に `MTEP` エイリアス（`using MTEP = COM3D2.MotionTimelineEditor.Plugin;`）が無ければ追加する。

- [ ] **Step 7: `TimelineIntegration` へ登録する**

`TimelineIntegration.cs` の `TransformType.Eyes` を登録している行の直前に追加:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.FaceSetting,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataFaceSetting>);
```

- [ ] **Step 8: ビルドしてテストを通す**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FaceForceOverrideTests
```
Expected: PASS

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataFaceSetting.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/Timeline/FaceMorphUtils.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs source/COM3D2.SceneEditor.Plugin.Tests/FaceForceOverrideTests.cs
git commit -m "feat(timeline): 表情設定用の TransformData と強制上書きの実効値 API を追加する"
```

---

## Task 2: レイヤーへの常設ボーンと適用

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceForceOverrideTests.cs`（Task 1 で作成したファイルに追記）

**Interfaces:**
- Consumes: Task 1 の `TransformDataFaceSetting` / `FaceMorphUtils.FORCE_OVERRIDE_*` / `ToForceOverride`
- Produces: `MorphTimelineLayer` が `faceForceOverride` ボーンを常に持ち、キー値で `UpdateMabatakiSuppression` を切り替える

- [ ] **Step 1: XML 往復の回帰テストを追記する**

`FaceForceOverrideTests.cs` に追記（レイヤー本体は Unity 依存でインスタンス化できないため、XML 往復だけを固定する）:

```csharp
        [Fact]
        public void 強制上書きキーはXML往復で保持される()
        {
            var src = new TimelineXml
            {
                layers = new System.Collections.Generic.List<TimelineLayerXml>
                {
                    new TimelineLayerXml
                    {
                        className = "MorphTimelineLayer",
                        slotNo = 0,
                        keyFrames = new System.Collections.Generic.List<FrameXml>
                        {
                            new FrameXml
                            {
                                frameNo = 0,
                                bones = new System.Collections.Generic.List<BoneXml>
                                {
                                    new BoneXml
                                    {
                                        transform = new TransformXml
                                        {
                                            name = FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME,
                                            type = TransformType.FaceSetting,
                                            values = new[] { 0f },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TimelineXml));
            TimelineXml dst;
            using (var ms = new System.IO.MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                dst = (TimelineXml)serializer.Deserialize(ms);
            }

            var trans = dst.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME, trans.name);
            Assert.Equal(TransformType.FaceSetting, trans.type);
            Assert.False(FaceMorphUtils.ToForceOverride(trans.values[0]));
        }
```

- [ ] **Step 2: テストを走らせる**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter FaceForceOverrideTests`
Expected: PASS（型は Task 1 で揃っているため、この時点で通る。FAIL する場合は Task 1 の enum 追加か登録漏れ）

- [ ] **Step 3: `allBoneNames` に常設ボーンを足す**

`MorphTimelineLayer.cs` の `allBoneNames` を差し替える。フィールドも足す:

```csharp
        private readonly List<string> _allBoneNamesCache = new List<string>();

        /// <summary>
        /// 強制上書きは変更追跡 (チェック) の対象外なので、絞り込み結果へ常に足す。
        /// 先頭に置いてボーンメニューでも最初に出す
        /// </summary>
        public override List<string> allBoneNames
        {
            get
            {
                var tracked = trackedBoneNames;
                _allBoneNamesCache.Clear();
                _allBoneNamesCache.Add(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);
                _allBoneNamesCache.AddRange(tracked);
                return _allBoneNamesCache;
            }
        }
```

- [ ] **Step 4: `GetTransformType` を分岐させる**

```csharp
        public override TransformType GetTransformType(string name)
        {
            if (name == FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME)
            {
                return TransformType.FaceSetting;
            }
            return TransformType.Morph;
        }
```

- [ ] **Step 5: ボーンメニューへ「表情設定」セットを足す**

`InitMenuItems()` の `allMenuItems.Clear();` の直後に挿入する:

```csharp
            // 強制上書きはモーフではないので、専用セットを先頭に置く
            var settingSetMenuItem = new BoneSetMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_SET_NAME, FaceMorphUtils.FORCE_OVERRIDE_SET_DISPLAY_NAME);
            settingSetMenuItem.AddChild(new BoneMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME, FaceMorphUtils.FORCE_OVERRIDE_DISPLAY_NAME));
            allMenuItems.Add(settingSetMenuItem);
```

同メソッド内の `var targetNames = new HashSet<string>(allBoneNames);` はそのままでよい（強制上書きはモーフ名の対応表 `morphNameToSetNameMap` に無いので、モーフ側のループには現れない）。

- [ ] **Step 6: 適用側を差し替える**

フィールドを追加:

```csharp
        /// <summary>このフレームで適用する強制上書き。キーが無ければ ON</summary>
        private bool _isForceOverride = true;
```

`ApplyMotion` の先頭で分岐する:

```csharp
        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (motion.name == FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME)
            {
                // ON/OFF に中間値は無いのでステップ適用する。
                // 終端 (t >= 0.99) では end 側を採らないと、最後のキーの値が永久に効かない
                var startSetting = motion.start as TransformDataFaceSetting;
                var endSetting = motion.end as TransformDataFaceSetting;
                var settingValue = t < 0.99f ? startSetting.forceOverride : endSetting.forceOverride;
                _isForceOverride = FaceMorphUtils.ToForceOverride(settingValue);
                return;
            }

            var start = motion.start as TransformDataMorph;
            var end = motion.end as TransformDataMorph;
            var morphName = motion.name;

            if (indexUpdated)
            {
                _applyMorphMap[morphName] = start.morphValue;
            }

            if (start.morphValue != end.morphValue)
            {
                _applyMorphMap[morphName] = Lerp(start.morphValue, end.morphValue, t, morphName);
            }
        }
```

`ApplyPlayData` を差し替える:

```csharp
        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                UpdateMabatakiSuppression(null);
                return;
            }

            _applyMorphMap.Clear();

            // キーが無い既存データ (MTE 産を含む) は ON 扱いにするため、毎フレーム ON で初期化する
            _isForceOverride = true;

            base.ApplyPlayData();

            UpdateMabatakiSuppression(_isForceOverride ? maid : null);

            // 強制上書き OFF 中もモーフ適用は続ける (まばたきに潰されるのは目まわりだけ)
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }
```

- [ ] **Step 7: `UpdateFrame` を分岐させる**

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            foreach (var name in allBoneNames)
            {
                if (name == FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME)
                {
                    var setting = frame.GetOrCreateTransformData<TransformDataFaceSetting>(name);
                    setting.forceOverride = FaceMorphUtils.ToForceOverrideValue(
                        SEP.MaidFaceMorphController.IsForceOverride(maid));
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataMorph>(name);
                trans.morphValue = GetMorphValue(name);
            }
        }
```

`MorphTimelineLayer.cs` は既に `using COM3D2.SceneEditor.Plugin;` を持つので、`MaidFaceMorphController.IsForceOverride(maid)` と直接書いてよい（`SEP` エイリアスを使っている箇所に合わせても可）。`IsForceOverride` は Task 1 Step 6 で追加済み。

- [ ] **Step 8: 0F キーを保証する `Init()` を足す**

キーが 1 個だけのボーンは `BuildPlayData`（`TimelineLayerBase.cs:376-406`）が `MotionData` を 1 つも作らず、`MotionPlayData.Update` が `motions.Count == 0` で即 false を返すため `ApplyMotion` が呼ばれない。強制上書きは `trackedCandidateNames` に入れない設計なので `AddTrackedFirstFrameKeys` の 0F 自動登録が効かず、ユーザーが途中フレームに 1 個だけキーを打つと**キーが黙って無視される**。これを防ぐためレイヤー側で 0F キーを保証する。

`Init()` は読み込み後に `TimelineData.LayerInit()`（`TimelineData.cs:616-621`）から全レイヤーに対して呼ばれ、その後の `CreateAndApplyAnm` → `BuildPlayData` より前に走る:

```csharp
        public override void Init()
        {
            base.Init();

            // キーが 1 個だけのボーンは MotionData が作られず適用されないため、
            // 0F にキーが無い読み込みデータへ既定 ON のキーを補う
            var firstFrame = GetOrCreateFrame(0);
            if (firstFrame.GetBone(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME) == null)
            {
                var setting = firstFrame.GetOrCreateTransformData<TransformDataFaceSetting>(
                    FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);
                setting.forceOverride = FaceMorphUtils.ToForceOverrideValue(true);
            }
        }
```

- [ ] **Step 9: ビルドとテスト**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド成功・全 PASS

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs source/COM3D2.SceneEditor.Plugin.Tests/FaceForceOverrideTests.cs
git commit -m "feat(timeline): 表情レイヤーへ強制上書きのキーを追加する"
```

---

## Task 3: 実効値の API と UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:219-245`（`DrawHeader`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MorphItemInspector.cs`

**Interfaces:**
- Consumes: Task 1 の `MaidFaceMorphController.IsForceOverride` / `SetForceOverride` / `FaceMorphUtils.FORCE_OVERRIDE_*`
- Produces: UI から実効値を読み書きできる状態（新しい公開 API は無い）

テストは Task 1 の純関数テストで担保済み。UI 層は Unity 依存のため単体テストを追加しない。

- [ ] **Step 1: 表情ウィンドウのヘッダーを差し替える**

`MaidFaceWindow.cs` の `DrawHeader` 内、`var isForceOverride = ...` から `view.SetEnabled(true);` までを置き換える:

```csharp
                // まばたき中は全カテゴリのモーフ値が毎フレーム上書きされるため、明示的に切り替えられるようにする。
                // 表示は「編集した表情を固定するか」の視点に揃えるため、まばたきの反転として扱う。
                // 表情レイヤー再生中はキー値が実効値になる。再生中の変更が次フレームで戻るのは
                // モーフ行と同じ挙動なので、ここでは無効化しない
                var isForceOverride = MaidFaceMorphController.IsForceOverride(target);
                view.DrawToggle("強制上書き", isForceOverride, 95, ROW_HEIGHT,
                    newIsForceOverride =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face, "強制上書き切替");
                        MaidFaceMorphController.SetForceOverride(target, newIsForceOverride);
                    });
```

- [ ] **Step 2: ボーンメニュー側の行を描く**

`MorphItemInspector.DrawItems` の `foreach` 先頭に分岐を足す:

```csharp
            foreach (var item in items)
            {
                if (item.name == MTEP.FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME)
                {
                    view.DrawToggle(MTEP.FaceMorphUtils.FORCE_OVERRIDE_DISPLAY_NAME,
                        MaidFaceMorphController.IsForceOverride(maid), 130, RowHeight,
                        newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Face, "強制上書き切替");
                            MaidFaceMorphController.SetForceOverride(maid, newValue);
                        });
                    continue;
                }

                var def = MaidFaceMorphController.FindDef(item.name);
```

- [ ] **Step 3: ビルドとテスト**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: 両構成でビルド成功・全 PASS

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MorphItemInspector.cs
git commit -m "feat(face): 強制上書きをキー値と連動した実効値で表示する"
```

---

## Task 4: 実機確認と互換の明記

**Files:**
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」節）

- [ ] **Step 1: 実機で確認する**

ゲーム起動中に `com3d25-devbridge` の `eval_csharp` でプラグインをホットリロードし（CLAUDE.md の手順）、次を確認する:

1. `仮装狂騒曲 篠澤広124.xml` のような既存タイムラインを読み込むと、表情ウィンドウの「強制上書き」が ON 表示になる
2. ボーンメニューに「表情設定 > 強制上書き」が出る
3. ポーズ編集中に強制上書きを OFF にしてキー登録 → 再生でそのフレームからまばたきが戻る
4. 保存した XML に `<Name>faceForceOverride</Name>` と `<Type>FaceSetting</Type>` が入る
5. 途中フレームに強制上書きのキーを 1 個だけ打っても効く（0F の自動キー補完が効いているか）
6. ボーンメニューの「全キー登録済み」表示が壊れていない。`FrameData.isFullBone`（`FrameData.cs:26-29`）は `_boneMap.Count == allBoneNames.Count` で判定するため、常設ボーンが 1 本増えたぶん既存フレームが「未完了」表示になりうる。実害があるなら別途対応する

- [ ] **Step 2: 互換の但し書きを追記する**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向（MTE → SceneEditor の一方向のみ）」節の箇条書き末尾へ:

```markdown
- 表情レイヤーの強制上書きキー（`faceForceOverride` / `<Type>FaceSetting</Type>`）は SceneEditor 独自。これを含む XML は MTE では未知の enum 値でデシリアライズに失敗する。キーが無い XML は強制上書き ON として読まれる
```

- [ ] **Step 3: コミット**

```bash
git add ../CLAUDE.md
git commit -m "docs: 強制上書きキーの MTE 非互換を明記する"
```

（`CLAUDE.md` はワークスペース側でこのリポジトリの外にある。リポジトリ外なら git add せず、ユーザーへ手動反映を依頼すること）

---

## 完了条件

- 既存タイムライン（強制上書きキー無し）を読み込むと強制上書きが ON で効く
- ボーンメニューからフレーム単位で ON/OFF を切り替えられ、XML に保存・復元される
- 2 構成でビルドが通り、`dotnet test` が全 PASS
- `code-review` スキルのレビュー済み

## レビュー却下メモ

- 「`SetForceOverride` はポーズ編集中でないと実体へ書けないため、ボーンメニューのトグルが効かないように見える恐れがある」（確信度: 低） — 却下。再生中の変更が次フレームでキー値に戻るのはモーフ行と同じ挙動であり、ユーザーと合意済みの設計（グレーアウトはしない）。実機確認 Step 1 の項目 3 で挙動を確認する
