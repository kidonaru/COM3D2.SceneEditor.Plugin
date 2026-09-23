# 表情レイヤーのタンジェント補間対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 表情レイヤー（`MorphTimelineLayer`）のモーフ値をタンジェント（Hermite）補間できるようにし、タイムライン設定の ON/OFF で切り替える。旧タイムラインは OFF、新規タイムラインは ON。

**Architecture:** `TransformDataMorph.hasTangent` をタイムライン設定 `TimelineData.isTangentFace` に連動させる。既存の `isTangent*` フラグはロード時一回きりの移行判定（`TangentUnification`）で実行時分岐には使わないが、表情だけはユーザーが ON/OFF を選べる要件のため意図的に実行時参照する例外とする。既存基盤（`TimelineLayerBase.UpdateTangent` / `KeyFrameTangentDrawer` / `TangentTargetList` / XML のタンジェント直列化）は `hasTangent` で分岐するので、フラグ連動だけで再計算・カーブ編集・保存が有効になる。`MorphTimelineLayer.ApplyMotion` は `hasTangent` 時に `PluginUtils.HermiteValue` を使い、OFF 時とステップモーフ（頬・涙）は従来どおり線形/ステップ。

**Tech Stack:** C# (Unity 5.6 / C# 4 制約なし・既存コードは C# 7 相当を使用), xunit テスト (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** ユーザー指示（本会話）: 「タイムライン設定でタンジェント補間ON/OFF制御、古いタイムラインはOFF、新規作成したタイムラインはON」

## Global Constraints

- コメント・ログは日本語
- `TimelineData.CurrentVersion` は上げない（新要素の欠落＝false で旧タイムライン OFF が表現できるため移行処理不要）
- `TransformDataFaceSetting`（強制上書きキー）はタンジェント非対応のまま（ON/OFF ステップ）
- テストで Unity ネイティブ呼び出しはしない（`Quaternion.Euler` 等）。`TimelineManager.instance` と `ConfigManager.instance.config` はテストから利用可能

## Review Focus

- 旧 XML（`IsTangentFace` 要素なし）を読むと OFF になる → Task 1 の XML テストで固定
- 新規作成タイムライン（`new TimelineData()`）は ON → Task 1 のテストで固定
- `TimelineManager.instance.timeline` が null のとき `TransformDataMorph.hasTangent` が例外を出さず false を返す → Task 2 のテストで固定
- 設定を OFF→ON に切り替えた直後、既存キーのタンジェントが XML 未保存由来の 0/false で潰れていない → Task 4 で `InitTangent()` を呼んで既定タンジェントへ戻す（実機確認）
- ステップモーフ（頬・涙）は ON でも値が飛ばない → Task 3 でステップ判定を Hermite より先に置く

---

### Task 1: TimelineData / TimelineXml にフラグを追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:396-402` 付近（`isTangentModelShapeKey` の下）、`FromXml`（795-801 付近）、`ToXml`（944-950 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:253-254` 付近
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs`（新規）

**Interfaces:**
- Produces: `TimelineData.isTangentFace : bool`（既定 true）、`TimelineXml.isTangentFace : bool`（既定 false、要素名 `IsTangentFace`）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 表情レイヤーのタンジェント補間フラグの既定値と XML 往復を固定する。
    /// 旧 XML には要素が無いので OFF、新規作成は ON でなければならない
    /// </summary>
    public class FaceTangentSettingTests
    {
        [Fact]
        public void 新規タイムラインは表情タンジェントONである()
        {
            Assert.True(new TimelineData().isTangentFace);
        }

        [Fact]
        public void 要素の無い旧XMLを読むとOFFになる()
        {
            var xml = new TimelineXml();
            Assert.False(xml.isTangentFace);

            var data = new TimelineData();
            data.FromXml(xml);
            Assert.False(data.isTangentFace);
        }

        [Fact]
        public void 表情タンジェントはXML往復で保持される()
        {
            var data = new TimelineData { isTangentFace = true };
            var xml = data.ToXml();
            Assert.True(xml.isTangentFace);

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TimelineXml));
            TimelineXml dst;
            using (var ms = new System.IO.MemoryStream())
            {
                serializer.Serialize(ms, xml);
                ms.Position = 0;
                dst = (TimelineXml)serializer.Deserialize(ms);
            }
            Assert.True(dst.isTangentFace);

            var loaded = new TimelineData();
            loaded.FromXml(dst);
            Assert.True(loaded.isTangentFace);
        }
    }
}
```

注意: `TimelineData.FromXml` / `ToXml` の実際のシグネチャは `BgmVideoSettingsXmlTests.cs:148-180` を参照して合わせる（引数が異なる場合はそちらに従う）。

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd source && dotnet test COM3D2.SceneEditor.Plugin.Tests --filter FaceTangentSettingTests`
Expected: コンパイルエラー（`isTangentFace` 未定義）

- [ ] **Step 3: 実装**

`TimelineData.cs` の `isTangentModelShapeKey` の下:

```csharp
        /// <summary>表情レイヤーをタンジェント補間するか。旧 XML には要素が無く false で読まれ、新規作成は true</summary>
        public bool isTangentFace = true;
```

`FromXml`: `isTangentModelShapeKey = xml.isTangentModelShapeKey;` の下に `isTangentFace = xml.isTangentFace;`
`ToXml`: `xml.isTangentModelShapeKey = isTangentModelShapeKey;` の下に `xml.isTangentFace = isTangentFace;`

`TimelineXml.cs` の `isTangentModelShapeKey` の下:

```csharp
        // 表情レイヤーのタンジェント補間。旧 XML では要素が無いので false (線形補間のまま)
        [XmlElement("IsTangentFace")]
        public bool isTangentFace = false;
```

- [ ] **Step 4: テストが通ることを確認**

Run: 同上。Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs
git commit -m "feat(timeline): 表情タンジェント補間フラグを TimelineData/XML に追加"
```

---

### Task 2: TransformDataMorph をタンジェント対応にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMorph.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs`（追記）

**Interfaces:**
- Consumes: `TimelineData.isTangentFace`
- Produces: `TransformDataMorph.hasTangent`（タイムライン設定連動）、`tangentValues => values`

- [ ] **Step 1: 失敗するテストを追記**

```csharp
        [Fact]
        public void タイムライン未ロードではモーフのタンジェントは無効()
        {
            // テスト環境では TimelineManager.instance.timeline が null。例外を出さず false
            var trans = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            Assert.False(trans.hasTangent);
            Assert.False(trans.ShouldSerializeInTangents());
        }

        [Fact]
        public void モーフのタンジェント対象は値スロットである()
        {
            var trans = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            Assert.Single(trans.tangentValues);
            Assert.Same(trans.morphValueValue, trans.tangentValues[0]);
        }
```

- [ ] **Step 2: 失敗確認**

Run: `cd source && dotnet test COM3D2.SceneEditor.Plugin.Tests --filter FaceTangentSettingTests`
Expected: `モーフのタンジェント対象は値スロットである` が FAIL（tangentValues が空配列）

- [ ] **Step 3: 実装**

`TransformDataMorph.cs` の `valueCount` の下:

```csharp
        // タイムライン設定で ON/OFF する。旧 XML は OFF で読まれるので線形補間のまま再生される。
        // ロード前 (timeline が null) は false にして XML 直列化判定を安全に通す
        public override bool hasTangent => timeline != null && timeline.isTangentFace;
        public override ValueData[] tangentValues => values;
```

- [ ] **Step 4: 通過確認**

Run: 同上。Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMorph.cs source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs
git commit -m "feat(timeline): TransformDataMorph をタイムライン設定連動でタンジェント対応にする"
```

---

### Task 3: MorphTimelineLayer.ApplyMotion で Hermite 補間する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs:258-296`

**Interfaces:**
- Consumes: `TransformDataMorph.hasTangent`、`PluginUtils.HermiteValue(float t0, float t1, ValueData start, ValueData end, float t)`、`FaceMorphUtils.IsStepMorph(string)`

- [ ] **Step 1: 実装**

`ApplyMotion` の後半と `Lerp` を次に置き換える:

```csharp
            var start = motion.start as TransformDataMorph;
            var end = motion.end as TransformDataMorph;
            var morphName = motion.name;

            if (indexUpdated)
            {
                _applyMorphMap[morphName] = start.morphValue;
            }

            if (start.morphValue != end.morphValue)
            {
                _applyMorphMap[morphName] = Interpolate(motion, start, end, t, morphName);
            }
        }

        /// <summary>
        /// 頬・涙などのオプションモーフは中間値を持たないためステップ適用する。
        /// それ以外はタイムライン設定に応じて Hermite (タンジェント) か線形で補間する
        /// </summary>
        private float Interpolate(MotionData motion, TransformDataMorph start, TransformDataMorph end, float t, string morphName)
        {
            if (FaceMorphUtils.IsStepMorph(morphName))
            {
                return t < StepEndThreshold ? start.morphValue : end.morphValue;
            }

            if (start.hasTangent)
            {
                return PluginUtils.HermiteValue(
                    motion.stFrame * timeline.frameDuration,
                    motion.edFrame * timeline.frameDuration,
                    start.morphValueValue,
                    end.morphValueValue,
                    t);
            }

            return Mathf.Lerp(start.morphValue, end.morphValue, t);
        }
```

（旧 `Lerp` は `lerpFrame = 0` にして `Mathf.Lerp` → 結果は start 値。新コードの `start.morphValue` と等価）

- [ ] **Step 2: ビルド確認（両構成）**

Run（Git Bash からは memory `msbuild-from-bash` の作法に従う）:
`MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 ...` と `GameVersion=COM3D25`
Expected: エラー 0

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs
git commit -m "feat(timeline): 表情レイヤーのモーフ値を Hermite 補間する"
```

---

### Task 4a: 未編集タンジェントだけを既定へ戻すヘルパー

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/FaceTangentToggle.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs`（追記）

**Interfaces:**
- Consumes: `ITransformData.tangentValues`、`ITimelineLayer.keyFrames` / `InitTangent()`、`FrameData.bones` / `BoneData.transform`
- Produces: `FaceTangentToggle.IsUntouched(IEnumerable<ITransformData>) : bool`、`FaceTangentToggle.ResetIfUntouched(IEnumerable<ITimelineLayer>) : void`

背景: OFF で保存した XML はタンジェントを持たず、ロード時に全値が normalized 0 / isSmooth false で読まれる。ON に切り替えた直後にこの状態なら既定タンジェント（`config.defaultTangentPair`）へ戻したい。一方、同一セッションで ON→カーブ編集→OFF→ON とした場合は編集値を保持しなければならない。「全キーの in/out が 0 かつ非スムーズ」を「未編集」とみなして判定する。

- [ ] **Step 1: 失敗するテストを追記**

```csharp
        [Fact]
        public void 全タンジェントが0で非スムーズなら未編集とみなす()
        {
            var a = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            var b = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            foreach (var trans in new[] { a, b })
            {
                foreach (var value in trans.tangentValues)
                {
                    value.inTangent.normalizedValue = 0f;
                    value.inTangent.isSmooth = false;
                    value.outTangent.normalizedValue = 0f;
                    value.outTangent.isSmooth = false;
                }
            }
            Assert.True(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));

            b.tangentValues[0].outTangent.normalizedValue = 0.5f;
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));

            b.tangentValues[0].outTangent.normalizedValue = 0f;
            b.tangentValues[0].inTangent.isSmooth = true;
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));
        }

        [Fact]
        public void 空の列は未編集扱いにしない()
        {
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[0]));
        }
```

- [ ] **Step 2: 失敗確認**

Run: `cd source && dotnet test COM3D2.SceneEditor.Plugin.Tests --filter FaceTangentSettingTests`
Expected: コンパイルエラー（`FaceTangentToggle` 未定義）

- [ ] **Step 3: 実装**

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情タンジェント補間のトグル ON 時に、未編集のタンジェントだけを既定値へ戻す。
    /// OFF で保存した XML はタンジェントを持たず全値が 0 / 非スムーズで読まれるため、
    /// その状態なら config の既定タンジェントへ揃える。編集済みの値は保持する
    /// </summary>
    public static class FaceTangentToggle
    {
        /// <summary>全キーの in/out が 0 かつ非スムーズなら未編集とみなす。空の列は false</summary>
        public static bool IsUntouched(IEnumerable<ITransformData> transforms)
        {
            var any = false;
            foreach (var trans in transforms)
            {
                any = true;
                foreach (var value in trans.tangentValues)
                {
                    if (value.inTangent.isSmooth || value.inTangent.normalizedValue != 0f
                        || value.outTangent.isSmooth || value.outTangent.normalizedValue != 0f)
                    {
                        return false;
                    }
                }
            }
            return any;
        }

        /// <summary>表情レイヤーのタンジェントが未編集なら既定値へ戻す。isTangentFace を true にした後に呼ぶ</summary>
        public static void ResetIfUntouched(IEnumerable<ITimelineLayer> layers)
        {
            foreach (var layer in layers)
            {
                if (!(layer is MorphTimelineLayer))
                {
                    continue;
                }
                if (IsUntouched(CollectTransforms(layer)))
                {
                    layer.InitTangent();
                }
            }
        }

        private static IEnumerable<ITransformData> CollectTransforms(ITimelineLayer layer)
        {
            foreach (var frame in layer.keyFrames)
            {
                foreach (var bone in frame.bones)
                {
                    if (bone.transform is TransformDataMorph)
                    {
                        yield return bone.transform;
                    }
                }
            }
        }
    }
}
```

注意: `ITimelineLayer.keyFrames` の型名と `FrameData.bones` / `BoneData.transform` の実名は `TangentUnification.ConvertLayer`（`Timeline/TangentUnification.cs:40-70`）と同じアクセス方法を使う。

- [ ] **Step 4: 通過確認**

Run: 同上。Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/FaceTangentToggle.cs source/COM3D2.SceneEditor.Plugin.Tests/FaceTangentSettingTests.cs
git commit -m "feat(timeline): 表情タンジェント ON 時に未編集タンジェントだけ既定へ戻すヘルパーを追加"
```

---

### Task 4: タイムライン設定ウィンドウにトグルを追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs:230-234` 付近（「1フレーム調整を anm にも適用」の下）

**Interfaces:**
- Consumes: `TimelineData.isTangentFace`、`FaceTangentToggle.ResetIfUntouched(IEnumerable<ITimelineLayer>)`、`timelineManager.ApplyCurrentFrame(bool)`、`timeline.layers`

- [ ] **Step 1: 実装**

```csharp
            view.DrawToggle("表情をタンジェント補間", timeline.isTangentFace, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isTangentFace = newValue;

                // OFF で保存された XML はタンジェントを持たないので、未編集のときだけ既定へ戻す
                // (編集済みのタンジェントは ON/OFF を往復しても保持する)
                if (newValue)
                {
                    FaceTangentToggle.ResetIfUntouched(timeline.layers);
                }
                timelineManager.ApplyCurrentFrame(true);
            });
```

- [ ] **Step 2: ビルド確認（両構成）**

Task 3 と同じ。Expected: エラー 0

- [ ] **Step 3: 実機確認（ゲーム起動中なら devbridge）**

1. 新規タイムライン作成 → 設定ウィンドウでトグルが ON
2. 表情レイヤーにモーフキーを 2 つ置く → 中間フレームで値が Hermite（EaseInOut なら中点 0.5 で同じ、1/4 点で線形より小さい）
3. カーブエディタ / キーフレームタンジェント表示に表情キーが出る
4. トグル OFF → 線形に戻る。ON → 既定タンジェントで再計算
5. 旧 XML（`IsTangentFace` なし）をロード → OFF

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "feat(timeline): タイムライン設定に表情タンジェント補間トグルを追加"
```

---

### Task 5: CHANGELOG / CLAUDE.md 追記

**Files:**
- Modify: `CHANGELOG.md`（未リリース節または最新節）。「表情レイヤーをタンジェント補間できるようになりました」と、「タイムライン設定『表情をタンジェント補間』で切替。既存のタイムラインは OFF、新規作成は ON。OFF のまま保存するとタンジェント値は保存されない」を子項目で書く
- Modify: `W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」に 1 行: 「`IsTangentFace` は SceneEditor 独自。無い XML は OFF（線形）で読まれ、新規作成は ON」

- [ ] **Step 1: 追記してコミット**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): 表情レイヤーのタンジェント補間対応を追記"
```

## レビュー却下メモ

- Task 1 のテストが `TimelineXml.version` 未設定で version 分岐を踏む懸念 — 誤検知。`TimelineData.FromXml` は version を代入するだけで分岐は無い（移行は `TimelineXml.Initialize` 側で、テストでは呼ばない）
- ステップモーフ（頬・涙）のタンジェント編集 UI 露出 — 要件外として未対応。再生には影響せず、`TransformDataMorph` が単一値スロットでモーフ名単位の切替は構造変更を伴うため見送り
