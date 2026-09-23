# anm への 1 フレーム調整適用 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン設定のトグル（既定 OFF）で、ボーンモーションの anm 出力にも `1フレーム調整`（Delay / Advance）を適用し、自動補間・タンジェント UI・anm の 3 者が同じカット判定を見るようにする。

**Architecture:** 判定源をレイヤーの `GetSingleFrameType(transformType)` に一本化し、Transform 側の `singleFrameType` プロパティは削除する。anm への反映は純粋関数 `AnmSingleFrameAdjuster.Adjust` でキー時刻とステップ化フラグを算出し、`MotionTimelineLayer.GetAnmBinaryInternal` がその結果でキー時刻とタンジェント（`float.PositiveInfinity` でステップ）を書き出す。ゲーム側 anm ローダー（`Assembly-CSharp/ImportCM.cs:976` 付近）はタンジェントをそのまま `Keyframe` に流し込むので Infinity がそのまま効く。

**Tech Stack:** C# (LangVersion 9, .NET Framework 4.8 / Unity 5.6 系 API)、xunit、MSBuild 2 構成 (COM3D2 / COM3D25)

**Spec:** ブレインストーミングでチャット承認済み（本計画冒頭の Goal / Architecture が仕様）。要点:
- 設定 `isSingleFrameAnm`（XML `IsSingleFrameAnm`、既定 false）。旧 XML は要素が無く false のまま
- 潰し規則は `PlayDataBase.Setup` と同じ: 1 フレーム差のペア (A, B) で、B がそのボーンの全キー中で最後のキーなら対象外（最後の区間は潰さない）。編集中プレビューは `activeTrack` の範囲へ切り出した窓を書き出すので、「窓の末尾」ではなく「ボーン全体の末尾」で判定する。Delay は A を `t(B) - ε` へ後ろ倒し、Advance は B を `t(A)` へ前倒しし A を `t(A) - ε`。A の outTangent と B の inTangent を Infinity
- スコープ外: ポーズ CSV / DCM 出力、MTE との相互運用

## Global Constraints

- コードのコメントとログ文言は日本語
- テストは Unity ネイティブ (Transform / Animation / Quaternion.Euler 等) を呼べない。pure なロジックだけをテストする
- ビルドとテストは必ず **COM3D2 → COM3D25 → dotnet test** の順で 1 本にする (COM3D2 ビルドが `bin/Debug/COM3D25/` を消すため)。`debug.bat` はゲームフォルダへ DLL をコピーするので使わない:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m \
&& "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m \
&& cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test
```

- テストプロジェクトは `bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll` を参照するため、テスト前に必ずプラグインをビルドする
- コミットは `commit` スキルの書式 (Conventional Commits、日本語)。末尾に session の attribution を付ける
- git worktree は使わない

## Review Focus

1. **範囲切り出しの境界キー**: `GetAnmBinaryInternal` が開始/終了フレームに合成するコピー用キーは、隣の実キーと 1 フレーム差でも潰してはならない → Task 1 のテスト「合成キーを含むペアは潰さない」
2. **activeTrack で切り出した編集中プレビュー**: 窓の末尾が 1 フレームペアでも、ボーン全体の最後でなければ潰す（`GetAnmBinary(forOutput: false)` 経路） → Task 1 のテスト「窓の末尾でもボーン全体の最後でなければ潰す」+ Task 5 で `isLast` を `bones` 末尾から算出
3. **3 キー連鎖 (f, f+1, f+2)**: 潰した結果の時刻が同値・逆順にならず、`ε` 刻みで狭義単調増加になること → Task 1 のテスト「連鎖」
4. **先頭ペアの Advance**: 開始フレーム 0 のキーが `-ε` に動く。Unity は負時刻キーを受け付けるので許容するが、順序は保つ → Task 1 のテスト「先頭ペアの Advance は負時刻を許す」
5. **設定 OFF 時の互換**: トグル OFF では anm バイナリが従来と完全一致すること（時刻もタンジェントも変えない） → Task 1 のテスト「None は入力をそのまま返す」+ Task 3 の判定でレイヤーが None を返す
6. **自動補間の末尾区間**: 再生側は最後の区間を潰さないので、`UpdateTangent` も末尾ペアでは隣キーを無視しない → Task 4 の実装条件

---

## ファイル構成

| ファイル | 責務 |
|---|---|
| `Timeline/AnmSingleFrameAdjuster.cs` (新規) | キー列（フレーム番号・時刻・合成フラグ）から潰し後の時刻とステップ化フラグを算出する純粋関数 |
| `Timeline/TimelineData.cs` / `Timeline/TimelineXml.cs` (変更) | 設定 `isSingleFrameAnm` の保持と XML 往復 |
| `TimelineSettingWindow.cs` (変更) | トグル「anm にも適用」 |
| `Timeline/TimelineLayer/MotionTimelineLayer.cs` (変更) | Root / Rotation / ExtendBone の判定を設定に従わせる。anm 出力へ潰しを適用 |
| `Timeline/TimelineLayer/TimelineLayerBase.cs` (変更) | `UpdateTangent` の判定をレイヤー判定へ統一し、末尾区間を除外 |
| `Timeline/TransformData/ITransformData.cs` / `TransformDataBase.cs` / `TransformDataRoot.cs` / `TransformDataRotation.cs` (変更) | Transform 側の `singleFrameType` を削除 |
| `Tests/AnmSingleFrameAdjusterTests.cs` (新規) / `Tests/TimelineSettingXmlTests.cs` (変更) | pure ロジックと XML 往復のテスト |
| `docs-site/timeline/settings.md` / `docs-site/timeline/playback.md` (変更) | ドキュメント |

---

### Task 1: AnmSingleFrameAdjuster（純粋関数）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/AnmSingleFrameAdjuster.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/AnmSingleFrameAdjusterTests.cs`

**Interfaces:**
- Produces:
  - `struct AnmKeySource { int frameNo; float time; bool isSynthetic; bool isLast; }`（namespace `COM3D2.MotionTimelineEditor.Plugin`）。`isLast` はそのボーンの全キー中で最後の実キー
  - `struct AnmKeyTiming { float time; bool stepIn; bool stepOut; }`
  - `static AnmKeyTiming[] AnmSingleFrameAdjuster.Adjust(IList<AnmKeySource> keys, SingleFrameType type, float epsilon)`
  - `const float AnmSingleFrameAdjuster.EpsilonRatio = 0.01f`（ε = フレーム長 × この比）

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/AnmSingleFrameAdjusterTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// anm 出力の 1 フレーム調整が PlayDataBase.Setup と同じ規則でキーを潰すことを固定する
    /// </summary>
    public class AnmSingleFrameAdjusterTests
    {
        private const float Frame = 1f / 30f;
        private const float Eps = Frame * AnmSingleFrameAdjuster.EpsilonRatio;

        /// <summary>末尾をボーン全体の最後の実キー (isLast) として並べる</summary>
        private static List<AnmKeySource> Keys(params int[] frameNos)
        {
            var keys = new List<AnmKeySource>();
            for (var i = 0; i < frameNos.Length; i++)
            {
                keys.Add(new AnmKeySource
                {
                    frameNo = frameNos[i],
                    time = frameNos[i] * Frame,
                    isSynthetic = false,
                    isLast = i == frameNos.Length - 1,
                });
            }
            return keys;
        }

        private static void AssertUntouched(AnmKeyTiming[] result, List<AnmKeySource> keys)
        {
            Assert.Equal(keys.Count, result.Length);
            for (var i = 0; i < keys.Count; i++)
            {
                Assert.Equal(keys[i].time, result[i].time);
                Assert.False(result[i].stepIn);
                Assert.False(result[i].stepOut);
            }
        }

        [Fact]
        public void Noneは入力をそのまま返す()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.None, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void 一フレーム差が無ければ変更しない()
        {
            var keys = Keys(0, 10, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void Delayは前キーを次キー直前へ後ろ倒しステップ化する()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);

            Assert.Equal(11 * Frame - Eps, result[1].time, 6);
            Assert.Equal(11 * Frame, result[2].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
            Assert.False(result[1].stepIn);
            Assert.False(result[2].stepOut);
            Assert.Equal(0f, result[0].time);
            Assert.Equal(20 * Frame, result[3].time);
        }

        [Fact]
        public void Advanceは次キーを前キー位置へ前倒しステップ化する()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);

            Assert.Equal(10 * Frame - Eps, result[1].time, 6);
            Assert.Equal(10 * Frame, result[2].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
        }

        [Fact]
        public void 最後の区間は潰さない()
        {
            var keys = Keys(0, 10, 11);
            var delay = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(delay, keys);
            var advance = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            AssertUntouched(advance, keys);
        }

        [Fact]
        public void 窓の末尾でもボーン全体の最後でなければ潰す()
        {
            // activeTrack で切り出したプレビュー窓: 末尾ペアが 1 フレーム差でも真の終端ではない
            var keys = Keys(0, 10, 11);
            keys[2] = new AnmKeySource { frameNo = 11, time = 11 * Frame, isSynthetic = false, isLast = false };
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            Assert.Equal(11 * Frame - Eps, result[1].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
        }

        [Fact]
        public void 四連鎖でも単調増加とステップ化が保たれる()
        {
            var keys = Keys(5, 6, 7, 8, 20);
            foreach (var type in new[] { SingleFrameType.Delay, SingleFrameType.Advance })
            {
                var r = AnmSingleFrameAdjuster.Adjust(keys, type, Eps);
                for (var i = 1; i < r.Length; i++)
                {
                    Assert.True(r[i - 1].time < r[i].time, type + ": 時刻が単調増加でない: " + i);
                }
                for (var i = 0; i < 3; i++)
                {
                    Assert.True(r[i].stepOut, type + ": stepOut " + i);
                    Assert.True(r[i + 1].stepIn, type + ": stepIn " + (i + 1));
                }
                Assert.False(r[4].stepIn);
            }
        }

        [Fact]
        public void 合成キーを含むペアは潰さない()
        {
            var keys = Keys(0, 1, 10);
            keys[0] = new AnmKeySource { frameNo = 0, time = 0f, isSynthetic = true, isLast = false };
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void 連鎖はイプシロン刻みで狭義単調増加になる()
        {
            var keys = Keys(0, 10, 11, 12, 20);

            var delay = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            Assert.Equal(11 * Frame - Eps, delay[1].time, 6);
            Assert.Equal(12 * Frame - Eps, delay[2].time, 6);
            Assert.Equal(12 * Frame, delay[3].time, 6);
            Assert.True(delay[1].stepOut && delay[2].stepIn && delay[2].stepOut && delay[3].stepIn);

            var advance = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            Assert.Equal(10 * Frame - 2 * Eps, advance[1].time, 6);
            Assert.Equal(10 * Frame - Eps, advance[2].time, 6);
            Assert.Equal(10 * Frame, advance[3].time, 6);

            foreach (var r in new[] { delay, advance })
            {
                for (var i = 1; i < r.Length; i++)
                {
                    Assert.True(r[i - 1].time < r[i].time, "時刻が単調増加でない: " + i);
                }
            }
        }

        [Fact]
        public void 先頭ペアのAdvanceは負時刻を許す()
        {
            var keys = Keys(0, 1, 10);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            Assert.Equal(-Eps, result[0].time, 6);
            Assert.Equal(0f, result[1].time, 6);
            Assert.True(result[0].stepOut);
            Assert.True(result[1].stepIn);
        }

        [Fact]
        public void キーが1つ以下なら空か同値()
        {
            Assert.Empty(AnmSingleFrameAdjuster.Adjust(new List<AnmKeySource>(), SingleFrameType.Delay, Eps));
            var one = Keys(5);
            AssertUntouched(AnmSingleFrameAdjuster.Adjust(one, SingleFrameType.Delay, Eps), one);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Global Constraints のビルド + テストコマンドを実行。
Expected: プラグインビルドは通る（テスト側のみ変更）が、`dotnet test` は `AnmSingleFrameAdjuster` 未定義でコンパイルエラー。

- [ ] **Step 3: 実装**

`source/COM3D2.SceneEditor.Plugin/Timeline/AnmSingleFrameAdjuster.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>anm へ書き出すキー 1 つ分の入力</summary>
    public struct AnmKeySource
    {
        public int frameNo;
        public float time;
        /// <summary>出力範囲の端を埋めるために合成したキー (実キーとのペアは潰さない)</summary>
        public bool isSynthetic;
        /// <summary>そのボーンの全キー中で最後の実キー。
        /// 出力範囲が activeTrack で切り出されていても、最後の区間かどうかはここで判定する</summary>
        public bool isLast;
    }

    /// <summary>anm へ書き出すキー 1 つ分の潰し結果</summary>
    public struct AnmKeyTiming
    {
        public float time;
        /// <summary>直前区間をステップにする (inTangent を Infinity にする)</summary>
        public bool stepIn;
        /// <summary>直後区間をステップにする (outTangent を Infinity にする)</summary>
        public bool stepOut;
    }

    /// <summary>
    /// anm 出力向けの 1 フレーム調整。PlayDataBase.Setup と同じ規則で
    /// 1 フレーム差のキー対を潰し、区間をステップ化する時刻を算出する。
    /// Unity の AnimationCurve は同時刻キーを持てないため、潰した側は ε だけ手前へずらす
    /// </summary>
    public static class AnmSingleFrameAdjuster
    {
        /// <summary>ε のフレーム長に対する比</summary>
        public const float EpsilonRatio = 0.01f;

        public static AnmKeyTiming[] Adjust(IList<AnmKeySource> keys, SingleFrameType type, float epsilon)
        {
            var result = new AnmKeyTiming[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                result[i].time = keys[i].time;
            }

            if (type == SingleFrameType.None || keys.Count < 2)
            {
                return result;
            }

            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                // 最後の区間 (B がボーン全体の最後のキー) は Setup の走査対象外なので潰さない
                if (a.isSynthetic || b.isSynthetic || b.isLast || b.frameNo - a.frameNo != 1)
                {
                    continue;
                }

                if (type == SingleFrameType.Delay)
                {
                    // A への補間を B の位置まで延長し、B で瞬間切替
                    result[i].time = b.time - epsilon;
                }
                else
                {
                    // B を A の位置へ前倒しし、A は直前で瞬間切替
                    result[i + 1].time = result[i].time;
                    result[i].time = result[i].time - epsilon;
                }
                result[i].stepOut = true;
                result[i + 1].stepIn = true;
            }

            // 連鎖で同時刻・逆順になったキーを末尾から ε 刻みで押し戻す
            for (var i = keys.Count - 2; i >= 0; i--)
            {
                if (result[i].time >= result[i + 1].time)
                {
                    result[i].time = result[i + 1].time - epsilon;
                }
            }

            return result;
        }
    }
}
```

- [ ] **Step 4: ビルド + テストが通ることを確認**

Global Constraints のコマンドを実行。Expected: `AnmSingleFrameAdjusterTests` 全件 PASS、既存テストも PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/AnmSingleFrameAdjuster.cs source/COM3D2.SceneEditor.Plugin.Tests/AnmSingleFrameAdjusterTests.cs
```
`commit` スキルで `feat(timeline): anm 向け 1 フレーム調整の時刻計算を追加` としてコミット。

---

### Task 2: 設定 `isSingleFrameAnm` の追加（Data / XML / 設定 UI）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:386`（フィールド）, `:788`（ReadFrom 相当）, `:936`（WriteTo 相当）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:224`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs:228-229`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs:25-64`

**Interfaces:**
- Produces: `TimelineData.isSingleFrameAnm` (bool, 既定 false), `TimelineXml.isSingleFrameAnm` (XML 要素 `IsSingleFrameAnm`)

- [ ] **Step 1: XML 往復テストへ項目を追加**

`TimelineSettingXmlTests.設定UIで編集する項目が往復で保持される` の `src` 初期化子に `isSingleFrameAnm = true,` を、Assert 群に `Assert.True(dst.isSingleFrameAnm);` を追加。さらに同クラスへ次を追加:

```csharp
        [Fact]
        public void IsSingleFrameAnmを持たない旧XMLは無効として読める()
        {
            var src = new TimelineXml();
            var dst = RoundTrip(src);
            Assert.False(dst.isSingleFrameAnm);
        }
```

- [ ] **Step 2: テストが失敗することを確認**

Expected: `dotnet test` がコンパイルエラー（`isSingleFrameAnm` 未定義）。

- [ ] **Step 3: 実装**

`TimelineXml.cs` の `singleFrameType` 直後に追加:

```csharp
        /// <summary>1 フレーム調整をボーンの anm 出力にも適用するか。旧 XML には無いので既定 false</summary>
        [XmlElement("IsSingleFrameAnm")]
        public bool isSingleFrameAnm = false;
```

`TimelineData.cs` の `singleFrameType` フィールド直後に追加:

```csharp
        /// <summary>1 フレーム調整をボーンの anm 出力にも適用するか</summary>
        public bool isSingleFrameAnm = false;
```

`TimelineData.cs` の読み込み（`singleFrameType = xml.singleFrameType;` の直後）に `isSingleFrameAnm = xml.isSingleFrameAnm;`、書き込み（`xml.singleFrameType = singleFrameType;` の直後）に `xml.isSingleFrameAnm = isSingleFrameAnm;` を追加。

`TimelineSettingWindow.cs` の `_singleFrameTypeComboBox.DrawButton("1フレーム調整", view);` の直後に追加:

```csharp
            view.DrawToggle("1フレーム調整を anm にも適用", timeline.isSingleFrameAnm, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isSingleFrameAnm = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });
```

- [ ] **Step 4: ビルド + テストが通ることを確認**

Expected: 全 PASS。

- [ ] **Step 5: コミット**

`feat(timeline): 1 フレーム調整を anm へ適用する設定を追加`

---

### Task 3: 判定源の一本化（Transform 側 `singleFrameType` の削除）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:228`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:357`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRoot.cs:58`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRotation.cs:67`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:913-923`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:691-703`

**Interfaces:**
- Consumes: `TimelineData.isSingleFrameAnm`（Task 2）
- Produces: `MotionTimelineLayer.GetSingleFrameType` が Root / Rotation / ExtendBone で `timeline.isSingleFrameAnm ? timeline.singleFrameType : None` を返す。`ITransformData.singleFrameType` は廃止

- [ ] **Step 1: Transform 側プロパティを削除**

- `ITransformData.cs` の `SingleFrameType singleFrameType { get; }` 行を削除
- `TransformDataBase.cs` の `public virtual SingleFrameType singleFrameType => timeline.singleFrameType;` 行を削除
- `TransformDataRoot.cs` / `TransformDataRotation.cs` の `public override SingleFrameType singleFrameType => SingleFrameType.None;` 行を削除

- [ ] **Step 2: レイヤー判定を設定に従わせる**

`MotionTimelineLayer.GetSingleFrameType` を次に置換:

```csharp
        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            switch (transformType)
            {
                case TransformType.Grounding:
                case TransformType.FingerBlend:
                    return SingleFrameType.None;
                case TransformType.Root:
                case TransformType.Rotation:
                case TransformType.ExtendBone:
                    // ボーンは anm のカーブ補間で再生されるため、設定で有効にしたときだけ潰す
                    return timeline.isSingleFrameAnm ? timeline.singleFrameType : SingleFrameType.None;
            }

            return base.GetSingleFrameType(transformType);
        }
```

- [ ] **Step 3: `UpdateTangent` の判定をレイヤー判定へ差し替える**

`TimelineLayerBase.UpdateTangent` 内の

```csharp
                    // 1フレーム補間が有効な場合は自身を使用
                    if (bone.transform.singleFrameType == SingleFrameType.Delay ||
                        bone.transform.singleFrameType == SingleFrameType.Advance)
```

を

```csharp
                    // 1 フレーム調整で潰れる区間は再生時に補間されないので、隣キーではなく自身を使う。
                    // 判定は再生側 (PlayDataBase.Setup) と同じくレイヤーの GetSingleFrameType に従う
                    if (GetSingleFrameType(bone.transform.type) != SingleFrameType.None)
```

に置換（ブロック内の `prevBone = bone;` / `nextBone = bone;` 差し替えはそのまま）。

- [ ] **Step 4: ビルド + テストが通ることを確認**

Expected: 全 PASS。`singleFrameType` の参照が残っていればビルドエラーになるので、`grep -rn "transform.singleFrameType" source` が空であることも確認。

- [ ] **Step 5: コミット**

`refactor(timeline): 1 フレーム調整の判定をレイヤー側へ一本化`

---

### Task 4: `UpdateTangent` の末尾区間除外

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:691-703`

**Interfaces:**
- Consumes: `GetNextBone2(int, List<BoneData>, out int, bool loopSearch)`（既存、`:1113`）

- [ ] **Step 1: 実装**

Task 3 Step 3 で置換したブロックを次に置換:

```csharp
                    // 1 フレーム調整で潰れる区間は再生時に補間されないので、隣キーではなく自身を使う。
                    // 判定は再生側 (PlayDataBase.Setup) と同じくレイヤーの GetSingleFrameType に従い、
                    // 別ループへ回り込んだキーと最後の区間 (Setup の走査対象外) は潰さない
                    if (GetSingleFrameType(bone.transform.type) != SingleFrameType.None)
                    {
                        int dummyFrameNo;
                        var isLastBone = GetNextBone2(bone.frameNo, bones, out dummyFrameNo, false) == null;
                        var isNextLastBone = nextBone != bone
                            && GetNextBone2(nextBone.frameNo, bones, out dummyFrameNo, false) == null;

                        if (bone.frameNo - prevFrameNo == 1 && prevFrameNo == prevBone.frameNo && !isLastBone)
                        {
                            prevBone = bone;
                        }
                        if (nextFrameNo - bone.frameNo == 1 && nextFrameNo == nextBone.frameNo && !isNextLastBone)
                        {
                            nextBone = bone;
                        }
                    }
```

- [ ] **Step 2: ビルド + テストが通ることを確認**

Expected: 全 PASS（この処理は BoneData / レイヤー依存で pure テスト不可。Task 5 の実機確認で裏取りする）。

- [ ] **Step 3: コミット**

`fix(timeline): 自動補間で最後の 1 フレーム区間を再生側と同じく潰さない`

---

### Task 5: anm 出力へ潰しを適用

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:670-671`（キャッシュ）, `:706-750`（write_bones）

**Interfaces:**
- Consumes: `AnmSingleFrameAdjuster.Adjust`, `AnmKeySource`, `AnmKeyTiming`（Task 1）、`GetSingleFrameType`（Task 3）

- [ ] **Step 1: キャッシュを差し替える**

```csharp
        List<float> _timesCache = new List<float>(128);
        List<ValueData[]> _valuesListCache = new List<ValueData[]>(128);
```

を

```csharp
        List<AnmKeySource> _keysCache = new List<AnmKeySource>(128);
        List<ValueData[]> _valuesListCache = new List<ValueData[]>(128);
```

に置換。

- [ ] **Step 2: write_bones のキー収集を差し替える**

`_timesCache.Clear();` を `_keysCache.Clear();` にし、3 か所の `_timesCache.Add(...)` を次のように置換:

- 開始フレーム補完: `_timesCache.Add(0f);` → `_keysCache.Add(new AnmKeySource { frameNo = _startFrameNo, time = 0f, isSynthetic = true });`
- 終了フレーム補完: `_timesCache.Add(endSecond - startSecond);` → `_keysCache.Add(new AnmKeySource { frameNo = _endFrameNo, time = endSecond - startSecond, isSynthetic = true });`
- 実キー: `_timesCache.Add(timeline.GetFrameTimeSeconds(bone.frameNo) - startSecond);` → `_keysCache.Add(new AnmKeySource { frameNo = bone.frameNo, time = timeline.GetFrameTimeSeconds(bone.frameNo) - startSecond, isSynthetic = false, isLast = bone == lastBone });`
  ここで `lastBone` は `write_bones` 冒頭（`var firstBone = bones[0];` の直後）で `var lastBone = bones[bones.Count - 1];` として取る。`bones` は範囲切り出し前のそのボーンの全キーなので、これがボーン全体の末尾になる（`BuildPlayData` の `rows` と同じ列）

`_timesCache.Count == 0` の判定は `_keysCache.Count == 0` に置換。

- [ ] **Step 3: 書き出しで潰し結果を使う**

チャンネル書き出しループを次に置換:

```csharp
                // 1 フレーム調整 (設定で anm へ適用したときだけ None 以外) で潰す区間を算出する
                var singleFrameType = GetSingleFrameType(firstBone.transform.type);
                var epsilon = timeline.GetFrameTimeSeconds(1) * AnmSingleFrameAdjuster.EpsilonRatio;
                var timings = AnmSingleFrameAdjuster.Adjust(_keysCache, singleFrameType, epsilon);

                // anmフォーマットのチャンネル107以降はマテリアルUV(_MainTex_ST等)に割り当てられているため、
                // スケール値は書き出さない (回転4 + 位置3 の最大7チャンネルまで)
                var channelCount = Mathf.Min(firstBone.transform.valueCount, 7);
                for (int i = 0; i < channelCount; i++)
                {
                    w.Write((byte)(100 + i));
                    w.Write(timings.Length);
                    for (int j = 0; j < timings.Length; j++)
                    {
                        var value = _valuesListCache[j][i];
                        // Infinity タンジェントは Unity の AnimationCurve でステップ補間になる
                        w.Write(timings[j].time);
                        w.Write(value.value);
                        w.Write(timings[j].stepIn ? float.PositiveInfinity : value.inTangent.value);
                        w.Write(timings[j].stepOut ? float.PositiveInfinity : value.outTangent.value);
                    }
                }
```

- [ ] **Step 4: ビルド + テストが通ることを確認**

Expected: 全 PASS。

- [ ] **Step 5: 実機確認（ゲーム起動中なら devbridge、そうでなければ `com3d25-restart-verify` スキル）**

1. メイドを配置し、モーションレイヤーで任意ボーンにキーを 3 つ打つ（frame 0、10、11、20 の 4 つ。10 と 11 で値を大きく変える）
2. 設定 OFF で再生し、10→11 が 1 フレームかけて補間されることを確認（従来挙動）
3. 「1フレーム調整を anm にも適用」ON + `1F遅らせる` で再生し、11 フレーム目で瞬間的に切り替わり、10 までの補間が 11 まで延びることを確認
4. `1F早める` にし、10 フレーム目で切り替わることを確認
4-2. `1F早める` のまま frame 0 と 1 にキーを打ち（先頭キーが `-ε` になる形）、再生・シーク・ループ再生で例外やクリップ長の異常（先頭で値が飛ぶ、ループ境界で停止する等）が出ないことを `tail_log` と目視で確認
5. タンジェント表示（キーフレーム Inspector のカーブ）が ON/OFF で「1 フレーム区間はカット扱い」の表示に追従することを確認
6. `eval_csharp` でエディタ内 anm の該当カーブを取り出す場合の参考: `maid.body0.m_Animation` からクリップを取得し `AnimationClipCurveData` は取れないため、書き出した .anm を `BinaryReader` で読み、対象ボーンのキー時刻とタンジェントに Infinity が入っていることを確認する

- [ ] **Step 6: コミット**

`feat(timeline): 1 フレーム調整を anm 出力へ適用`

---

### Task 6: ドキュメント

**Files:**
- Modify: `docs-site/timeline/settings.md:19`
- Modify: `docs-site/timeline/playback.md:80-92`

- [ ] **Step 1: settings.md の表へ行を追加**

`1フレーム調整` の行の直後に:

```markdown
| `1フレーム調整を anm にも適用` | OFF | メイドのボーン（モーションレイヤーの回転・位置・拡張ボーン）にも `1フレーム調整` を効かせる。OFF ではボーンは 1 フレーム間隔でも補間される。[再生とシーク](/timeline/playback#1-フレーム調整) |
```

- [ ] **Step 2: playback.md へ節を追記**

「値を瞬間的に切り替えたいときは…」の段落の直後に:

```markdown
メイドのボーン（モーションレイヤーの回転・位置・拡張ボーン）は .anm のカーブ補間で再生されるため、既定では `1フレーム調整` の対象外で、1 フレーム間隔でも補間されます。`タイムライン設定` の `1フレーム調整を anm にも適用` を ON にすると、ボーンにも同じ規則が適用され、書き出した .anm にもその段差が記録されます。ON のときは自動補間の計算とタンジェント表示もボーンの 1 フレーム区間をカット扱いにします。
```

- [ ] **Step 3: docs-review スキルでレビューし、指摘を反映**

- [ ] **Step 4: コミット**

`docs(timeline): 1 フレーム調整の anm 適用設定を記載`

---

### Task 7: コードレビューと仕上げ

- [ ] **Step 1: `code-review` スキルを実行し、妥当な指摘を反映**
- [ ] **Step 2: ビルド + テストを再実行して全 PASS を確認**
- [ ] **Step 3: 反映があれば `commit` スキルでコミット**

---

## レビュー却下メモ

- Task 4 (`UpdateTangent` 末尾除外) を pure ヘルパーに切り出して xunit で検証すべき — 未確認のまま見送り。条件は `GetNextBone2(loopSearch: false)` に依存しレイヤー抜きでの切り出しは実装コストに見合わない。Task 5 Step 5 の実機確認で裏取りする
