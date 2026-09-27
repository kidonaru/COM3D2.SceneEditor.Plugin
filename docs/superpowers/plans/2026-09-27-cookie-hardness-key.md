# 輪郭の硬さのキー化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** スポットライトの輪郭の「硬さ」をライト定義からライトキー (index 19) へ移し、タイムラインで補間できるようにする。

**Architecture:** `TransformDataLight` に値 `CookieHardness` (index 19、値数 20) を足し、ライトレイヤーの記録・適用で `LightCookie` の硬さを読み書きする。輪郭の種類と画像はライト定義のまま残し、定義 XML からは `CookieHardness` を外す。定義の変更検出は硬さを無視して比較し、再生中の硬さの変化で定義の同期が走らないようにする。

**Tech Stack:** C# (Unity 2022.3 / COM3D2 両ビルド), XmlSerializer, xunit (net48)

**Spec:** 本計画の「仕様」節。前提は `docs/superpowers/plans/2026-09-27-spot-light-cookie.md` (同じブランチ `feat/spot-light-cookie`、未リリース)

## 仕様

- 硬さ (0〜1、既定 0.8) はライトキーの値 index 19。他の数値と同じく Tangent 補間する
- 輪郭の種類 (既定 / 硬さ / 画像) と画像はライト定義 (`<Light>` の `CookieMode` / `CookieImage`) のまま。キー化しない
- ライト定義の `CookieHardness` 要素は廃止する。輪郭機能は未リリースなので読込互換は持たない (要素があっても読み飛ばす)
- 19 値以下の旧キー (MTE・旧 SE) は硬さを既定 0.8 で読む (`_valuesForXml` の不足分 0 埋めを補正する。PNG 彩度と同じ作法)
- メインライト (index 0) は輪郭を持たないので、記録は既定 0.8 固定、適用は無視する
- 硬さは輪郭の種類が「硬さ」以外でも記録・適用する (種類を後で「硬さ」へ切り替えたときに値を失わないため)
- シーンプリセット・Undo・クリップボードは従来どおり輪郭一式 (硬さを含む) を持つ。変更しない
- `TimelineData.CurrentVersion` は上げない (PNG 彩度の追加と同じ扱い)

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"` (COM3D2 構成は `/p:GameVersion=COM3D2`。Git Bash からは `/p:` を `-p:` と書く)。COM3D2 構成のビルドで COM3D25 の出力 DLL が消えるため、テスト前は COM3D25 を最後にビルドする
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- タイムライン XML は MTE → SE の一方向互換。MTE のライトキーは 18 値までしか読まない (余分な値は読み飛ばす)

## Review Focus

1. 19 値の旧キーを読むと硬さが 0 (中心から滑らかに減衰する最も柔らかい輪郭) にならず 0.8 になる (Task 1 のテスト)
2. 再生中に硬さが補間されても、ライト定義の同期 (`UpdateTimelineLights`) が毎回走らない (Task 2 の `EqualsIgnoringHardness` テスト)
3. `<CookieHardness>` を含む開発中の XML を読んでも例外にならず、硬さは既定で読む (Task 2 のテスト)
4. 輪郭が「既定」のライトにも硬さを適用するが、既定値 0.8 のままなら部品 (`LightCookieHolder`) を増やさない (Task 3 の `SetHardness`、実機確認 Task 5)
5. メインライトのキーに硬さを書いても、メインライトへは何も適用しない (Task 3、実機確認 Task 5)

---

## File Structure

| ファイル | 責務 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs` | index 19 `CookieHardness`、旧キーの補正、カスタム値 |
| Modify `source/COM3D2.SceneEditor.Plugin/LightCookieData.cs` | `EqualsIgnoringHardness`、コメント更新 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineLightXml.cookieHardness` 削除 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | 定義の硬さを読み書きしない |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` | 定義の変更検出で硬さを無視 |
| Modify `source/COM3D2.SceneEditor.Plugin/LightCookie.cs` | `SetHardness` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs` | 記録・適用 |
| Tests `LightCookieHardnessKeyTests.cs` (新規) / `LightTargetTests.cs` / `LightCookieXmlTests.cs` | |
| Modify `docs-site/guide/staging.md` / `docs-site/timeline/compatibility.md` / `W:\COM3D2_5\work\CLAUDE.md` | ドキュメント |

---

### Task 1: ライトキーに硬さを足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs` (`ライトキーの値数は19で末尾が対象`)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/LightCookieHardnessKeyTests.cs`

**Interfaces:**
- Produces: `TransformDataLight.Index.CookieHardness = 19`、`TransformDataLight.LegacyValueCount = 19`、`TransformDataLight.cookieHardnessValue (ValueData)`、`TransformDataLight.cookieHardness (float)`

- [ ] **Step 1: 失敗するテストを書く**

`LightCookieHardnessKeyTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライトキーに追加した輪郭の硬さ (index 19) を固定する。
    /// 旧データ (19 値以下) は不足分が 0 で埋まるため、補正しないと最も柔らかい輪郭で読まれる
    /// </summary>
    public class LightCookieHardnessKeyTests
    {
        private static TransformDataLight CreateKey()
        {
            var trans = new TransformDataLight();
            trans.Initialize("Light1");
            return trans;
        }

        private static TransformXml CreateXml(float[] values)
        {
            return new TransformXml
            {
                name = "Light1",
                type = TransformType.Light,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 硬さはindex19で値数は20()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            Assert.Equal(19, (int)TransformDataLight.Index.CookieHardness);
            Assert.Equal(20, CreateKey().valueCount);
            Assert.Equal(19, TransformDataLight.LegacyValueCount);
            Assert.True(CreateKey().GetCustomValueInfoMap().ContainsKey("cookieHardness"));
        }

        [Fact]
        public void リセットしたキーの硬さは既定値()
        {
            var trans = CreateKey();
            trans.Reset();

            Assert.Equal(LightCookieData.DefaultHardness, trans.cookieHardness);
        }

        [Theory]
        [InlineData(18)]
        [InlineData(19)]
        public void 硬さを持たない旧キーは既定値で読む(int valueCount)
        {
            var trans = CreateKey();
            trans.FromXml(CreateXml(new float[valueCount]));

            Assert.Equal(LightCookieData.DefaultHardness, trans.cookieHardness);
        }

        [Fact]
        public void 硬さを持つキーは保存値で読む()
        {
            var values = new float[20];
            values[19] = 0.25f;
            var trans = CreateKey();
            trans.FromXml(CreateXml(values));

            Assert.Equal(0.25f, trans.cookieHardness);
        }

        [Fact]
        public void 硬さは往復で保たれる()
        {
            var trans = CreateKey();
            trans.cookieHardness = 0.4f;

            var restored = CreateKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(0.4f, restored.cookieHardness);
        }
    }
}
```

`LightTargetTests.cs` の `ライトキーの値数は19で末尾が対象` を次に置き換える (末尾が硬さになったため):

```csharp
        [Fact]
        public void 対象はindex18()
        {
            var trans = new TransformDataLight();
            Assert.Equal(18, (int)TransformDataLight.Index.LightTarget);
            Assert.True(trans.GetCustomValueInfoMap().ContainsKey("lightTarget"));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightCookieHardnessKey|FullyQualifiedName~LightTarget"`
Expected: コンパイルエラー (`Index.CookieHardness` / `LegacyValueCount` / `cookieHardness` が無い)

- [ ] **Step 3: 実装する**

`TransformDataLight.cs`:

- enum `Index` の `LightTarget = 18` を `LightTarget = 18,` にし、続けて:

```csharp
            // SE 独自。MTE は 18 値までしか読まない
            CookieHardness = 19
```

- `public override int valueCount => 19;` を次に置き換える:

```csharp
        /// <summary>輪郭の硬さ (index 19) を持たない旧データ (MTE・旧 SE) の値数</summary>
        public const int LegacyValueCount = 19;

        public override int valueCount => (int)Index.CookieHardness + 1;
```

- `CustomValueInfoMap` の `lightTarget` エントリの後に:

```csharp
            {
                "cookieHardness", new CustomValueInfo
                {
                    index = (int)Index.CookieHardness,
                    name = "硬さ",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = SceneEditor.Plugin.LightCookieData.DefaultHardness,
                }
            },
```

- 値アクセサに `public ValueData cookieHardnessValue => values[(int)Index.CookieHardness];`
- プロパティアクセサの末尾に:

```csharp

        /// <summary>輪郭の硬さ。輪郭の種類が「硬さ」のときだけ見た目に効く</summary>
        public float cookieHardness
        {
            get => cookieHardnessValue.value;
            set => cookieHardnessValue.value = value;
        }

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            // 硬さを持たない旧データ (MTE・旧 SE) は不足分が 0 で埋まり、最も柔らかい輪郭になる。既定値へ補正する
            if (xml.values.Length <= LegacyValueCount)
            {
                cookieHardness = SceneEditor.Plugin.LightCookieData.DefaultHardness;
            }
        }
```

- [ ] **Step 4: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS (`TangentValueCoverageTests` / `LightHoldKeyConversionTests` を含む)

- [ ] **Step 5: コミット** (例: `feat(timeline): ライトキーに輪郭の硬さを追加する`)

---

### Task 2: ライト定義から硬さを外す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightCookieData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` (`TimelineLightXml`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` (`TimelineLightData.FromXml` / `ToXml`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` (`LateUpdate(bool)`)
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/LightCookieXmlTests.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightCookieXmlTests.cs` に追記 (`EqualsIgnoringHardness` もここで固定する)

**Interfaces:**
- Produces: `bool LightCookieData.EqualsIgnoringHardness(LightCookieData other)`

- [ ] **Step 1: 失敗するテストを書く**

`LightCookieXmlTests.cs` の `硬さ指定は硬さだけを往復する` を次に置き換える:

```csharp
        [Fact]
        public void 硬さ指定は種類だけを往復し硬さは定義に書かない()
        {
            // 硬さはライトキー (index 19) で持つ
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Spot,
                cookie = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.95f, image = "a.png" },
            };

            var text = Serialize(data.ToXml());
            var restored = FromText(text);

            Assert.Contains("<CookieMode>1</CookieMode>", text);
            Assert.DoesNotContain("CookieHardness", text);
            Assert.DoesNotContain("CookieImage", text);
            Assert.Equal(LightCookieMode.Generated, restored.cookie.mode);
            Assert.Equal(LightCookieData.DefaultHardness, restored.cookie.hardness);
        }

        [Fact]
        public void 開発中の定義にあるCookieHardnessは読み飛ばす()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type>" +
                "<CookieMode>1</CookieMode><CookieHardness>0.3</CookieHardness></TimelineLightXml>");

            Assert.Equal(LightCookieMode.Generated, restored.cookie.mode);
            Assert.Equal(LightCookieData.DefaultHardness, restored.cookie.hardness);
        }

        [Fact]
        public void 硬さだけが違う輪郭は定義として同じ()
        {
            var a = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.2f, image = "" };
            var b = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.9f, image = "" };
            var c = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.2f, image = "a.png" };

            Assert.True(a.EqualsIgnoringHardness(b));
            Assert.False(a.EqualsIgnoringHardness(c));
            Assert.False(c.EqualsIgnoringHardness(new LightCookieData { mode = LightCookieMode.Image, image = "b.png" }));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightCookieXml"`
Expected: コンパイルエラー (`EqualsIgnoringHardness` が無い)

- [ ] **Step 3: `LightCookieData` を変更する**

- 型の summary を次に置き換える:

```csharp
    /// <summary>
    /// 追加ライト 1 灯分の輪郭設定。シーンプリセット・Undo・クリップボードでは一式を受け渡す。
    /// タイムラインでは種類と画像をライト定義、硬さをライトキー (index 19) で持つ
    /// </summary>
```

- `Equals(LightCookieData other)` の前に:

```csharp
        /// <summary>
        /// タイムラインのライト定義として同じか。硬さはライトキーで補間されるので比べない
        /// (比べると再生中に毎回定義の同期が走る)
        /// </summary>
        public bool EqualsIgnoringHardness(LightCookieData other)
        {
            return mode == other.mode
                && string.Equals(image ?? "", other.image ?? "", StringComparison.Ordinal);
        }
```

- [ ] **Step 4: 定義 XML から硬さを外す**

`TimelineXml.cs` の `TimelineLightXml`:
- コメント 1 行目 `// 輪郭 (cookie) は SE 独自。既定の輪郭では書き出さず、MTE 産の XML を保存し直しても内容を変えない。` を `// 輪郭 (cookie) の種類と画像は SE 独自 (硬さはライトキーで持つ)。既定の輪郭では書き出さず、MTE 産の XML を保存し直しても内容を変えない。` に置き換える (2 行目「モードは未知の値でも〜」はそのまま)
- `[XmlElement("CookieHardness")] public float cookieHardness = ...;` の 2 行と `ShouldSerializecookieHardness` の行を削除する

`TimelineData.cs` の `TimelineLightData`:
- `FromXml` の `hardness = xml.cookieHardness,` を `hardness = SE.LightCookieData.DefaultHardness,` に置き換える
- `ToXml` の `cookieHardness = cookie.hardness,` の行を削除する

`StudioLightManager.cs` (Timeline/Manager) の `LateUpdate(bool)`:
- `if (!cachedLight.cookie.Equals(stat.cookie) || cachedLight.shadows != stat.shadows)` を `if (!cachedLight.cookie.EqualsIgnoringHardness(stat.cookie) || cachedLight.shadows != stat.shadows)` に置き換える
- 直前のコメントを `// 輪郭の種類・画像と影の種類はライト定義の値なので、一覧の作り直し (イベント発火) はせず定義だけ同期する` に置き換える

- [ ] **Step 5: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS

- [ ] **Step 6: コミット** (例: `refactor(timeline): 輪郭の硬さをライト定義から外す`)

---

### Task 3: ライトレイヤーで硬さを記録・適用する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightCookie.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs`

**Interfaces:**
- Consumes: `TransformDataLight.cookieHardness` / `cookieHardnessValue` (Task 1)
- Produces: `static void LightCookie.SetHardness(Light light, float hardness)`

Unity の `Light` / `AddComponent` を使うため単体テストは書かない。Task 5 の実機確認で固定する。

- [ ] **Step 1: `LightCookie.SetHardness` を足す** (`Set` の後)

```csharp
        /// <summary>
        /// 硬さだけを差し替える (タイムラインのキー適用用)。再生中は毎フレーム呼ばれるため、
        /// 値が変わらなければ何もしない。生成テクスチャは段階ごとにキャッシュされる
        /// </summary>
        public static void SetHardness(Light light, float hardness)
        {
            if (light == null)
            {
                return;
            }

            var data = Get(light);
            var next = new LightCookieData { mode = data.mode, hardness = hardness, image = data.image }.Normalized();
            if (next.hardness == data.hardness)
            {
                return;
            }

            Set(light, next);
        }
```

(既定の輪郭で硬さも既定なら `Set` は部品を増やさない。硬さが既定と違うときは種類を後で「硬さ」へ切り替えたときのために部品へ保持する)

- [ ] **Step 2: `LightTimelineLayer` の適用に足す**

`ApplyMotionInit` の照射対象ブロック (`if (stat.index > 0) { light.cullingMask = ...; }`) の中、`cullingMask` の行の後に:

```csharp
                SceneEditor.Plugin.LightCookie.SetHardness(light, start.cookieHardness);
```

(直前のコメントを `// 照射対象は補間しない。メインライト (index 0) はゲーム側の恒久オブジェクトのため照射対象・輪郭を触らない` に置き換える)

`ApplyMotionUpdateTangent` の `light.shadowBias = PluginUtils.HermiteValue(...);` の後に:

```csharp

            // 輪郭の硬さも他の数値と同じく補間する。メインライトは輪郭を持たない
            if (stat.index > 0)
            {
                SceneEditor.Plugin.LightCookie.SetHardness(light, PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.cookieHardnessValue,
                    end.cookieHardnessValue,
                    t));
            }
```

- [ ] **Step 3: `UpdateFrame` の記録に足す** (`trans.lightTarget = ...;` の後)

```csharp
                trans.cookieHardness = stat.index > 0
                    ? SceneEditor.Plugin.LightCookie.Get(light).hardness
                    : SceneEditor.Plugin.LightCookieData.DefaultHardness;
```

- [ ] **Step 4: 両構成でビルドし、テストを流す**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: エラー 0、全件 PASS

- [ ] **Step 5: コミット** (例: `feat(light): 輪郭の硬さをタイムラインで補間する`)

---

### Task 4: ドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md` (輪郭の表の後の段落)
- Modify: `docs-site/timeline/compatibility.md` (輪郭の行)
- Modify: `W:\COM3D2_5\work\CLAUDE.md` (git 管理外)

- [ ] **Step 1: `staging.md`** — 輪郭の表の直後、「`画像` は、透過付きの画像なら〜」の段落の前に 1 段落足す:

```markdown
`硬さ` はライトキーに記録され、タイムラインでキー間を補間できます（縁をだんだんぼかす演出など）。輪郭の種類と画像はキーではなくライトの設定として保存され、途中で切り替わりません。
```

- [ ] **Step 2: `compatibility.md`** — 輪郭の行を次に置き換える:

```markdown
- スポットライトの `輪郭` の種類と画像はライト定義、`硬さ` はライトキーとして保存されます。SceneEditor 独自の値で、MTE や以前の SceneEditor で読むと既定の輪郭で表示されます
```

- [ ] **Step 3: ワークスペースの `CLAUDE.md`** — ライト定義の輪郭の行を次に置き換え、直後にライトキーの行を足す:

```markdown
- ライト定義（`<Lights>` の `<Light>`）の輪郭（`CookieMode` / `CookieImage`）と影の種類（`Shadows`、LightShadows の数値）は SE 独自。既定の輪郭（cookie なし）・影なしでは書き出さない。MTE は要素を読み飛ばすため Unity 内蔵の輪郭・影なしで表示される。シーンプリセットは v37 で同名の属性（硬さ `cookieHardness` を含む）を追加
- ライトキーの輪郭の硬さ（index 19、値数 20）は SE 独自。19 値以下の旧キーは `TransformDataLight.FromXml` が既定 0.8 に補正する。MTE は 18 値までしか読まないため硬さは失われる
```

- [ ] **Step 4: コミット** (例: `docs(light): 輪郭の硬さがキーになったことを説明する`。CLAUDE.md はリポジトリ外なので含めない)

---

### Task 5: 実機確認

**Files:** なし (確認のみ)

- [ ] **Step 1: restart-verify スキルで DLL を反映し、ゲームを起動してセーブをロードする** (再起動前にユーザーへ確認する)
- [ ] **Step 2: devbridge で確認する**
  - SceneEditor を有効にし、新規タイムラインでスポットを追加して輪郭を「硬さ」にする
  - フレーム 0 で硬さ 0.1、フレーム 60 で硬さ 1.0 のキーを登録し、フレーム 30 で `LightCookie.Get(light).hardness` が中間値 (0.1〜1.0 の間) になること、`screenshot` で縁の硬さが変わること
  - 再生中に `MTEP.StudioLightManager` の定義同期が毎回走らないこと (`timeline.lights[i].cookie` の硬さが変わらない、`UpdateTimelineLights` が呼ばれない)
  - タイムラインを保存し、`<Light>` に `CookieHardness` が無く、ライトキーが 20 値であること
  - 保存したタイムラインを読み直し、フレーム 0 で硬さが 0.1 に戻ること (`SetupLights` が定義の既定 0.8 で上書きした後、レイヤーの現在フレーム適用がキー値で上書きする順序の確認)
  - ライトウィンドウの硬さスライダー相当の操作 (`LightCookie.Set` で硬さを変更) の後にキーを登録すると、そのキーの index 19 に新しい値が入ること
  - 輪郭が「既定」のライトで、硬さ 0.8 のキーだけなら `LightCookieHolder` が付かないこと
  - メインライトのキーの index 19 が 0.8 で、メインライトに `LightCookieHolder` が付かないこと
  - `tail_log` に SceneEditor 由来の例外が無いこと
- [ ] **Step 3: 後片付け**: テスト用ライト・タイムラインを削除する

## レビュー却下メモ

- `SetHardness` の厳密比較で、補間中は同じ生成テクスチャを毎フレーム `light.cookie` へ再代入する — 辞書引きと同一参照の代入だけで実害が小さい。丸め段階で比較すると保持する硬さがキー値とずれ、`UpdateFrame` の記録値が変わるため見送り
