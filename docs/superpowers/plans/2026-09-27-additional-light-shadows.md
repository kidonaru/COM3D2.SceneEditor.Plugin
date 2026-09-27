# 追加ライトの影の種類 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 追加ライト (ポイント / スポット / 平行) に影の種類「なし / ハード / ソフト」を設定できるようにし、タイムライン・シーンプリセット / Undo・クリップボードへ保存する。

**Architecture:** 影の種類は Unity の `Light.shadows` そのものを状態として使う (輪郭のような保持用コンポーネントは要らない)。外部入力の int を `LightShadows` へ丸める純粋関数を `LightShadowValues` に置き、保存経路は輪郭 (cookie) と同じ 3 経路に載せる。影の濃さ・距離は既存のライトキー (index 14 / 15) のまま。

**Tech Stack:** C# (Unity 2022.3 / COM3D2 両ビルド), IMGUI (`GUIView`), XmlSerializer, xunit (net48)

**Spec:** 本計画の「仕様」節。前提の輪郭 (cookie) 実装は `docs/superpowers/plans/2026-09-27-spot-light-cookie.md` (同じブランチ `feat/spot-light-cookie`)

## 仕様

- 背景 (実機で確認済み): SE は `light.shadows` をどこでも設定しておらず、追加ライトは常に影なしで生成される。そのため追加した平行光源の「影の濃さ / 影の距離」スライダーは効いていない。スポットで `LightShadows.Soft` を設定するとメイドの影が床に落ちる (描画モード Auto でも出る)。ゲームのシェーダーは追加ライトの影 (fwdadd_fullshadows) に対応しているため、ポイントも対象にする
- 対象は追加ライトの全種別 (ポイント / スポット / 平行)。メインライトは触らない (ゲーム側で既に Soft)
- 影の種類は `LightShadows` の 3 値: なし (None=0) / ハード (Hard=1) / ソフト (Soft=2)。既定は なし (今までの見た目を変えない)
- UI: 追加ライトの編集行に「影」ボタン 3 つ。影が なし 以外のときだけ「影の濃さ」「影の距離」スライダーを出す (平行光源に限らず全種別)
- 保存
  - タイムライン: `<Light>` に SE 独自要素 `Shadows` (int)。なし では書かない。`TimelineData.CurrentVersion` は上げない
  - シーンプリセット / Undo: `ScenePresetAdditionalLight` に属性 `shadows` (int)。なし では書かない。`ScenePresetData.CurrentVersion` は同じブランチで未リリースの 37 のまま、v37 の変更履歴コメントに追記する
  - ライトのコピー / ペースト: 追加ライト間で写す。メインライトからコピーしたときはメインライトの影の種類を写す。メインライトへのペーストでは反映しない (既存の方針どおり)
- 未知の int (範囲外) は なし として読む
- 影の種類はキーフレーム化しない (ライト定義の値)。影の濃さ・距離は従来どおりキー

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"` (COM3D2 構成は `/p:GameVersion=COM3D2`)。COM3D2 構成のビルドで COM3D25 の出力 DLL が消えるため、テスト前は COM3D25 を最後にビルドする
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- 新規 .cs は csproj の `<Compile Include>` へ追加する
- タイムライン XML は MTE → SE の一方向互換。SE 独自要素は MTE で読み飛ばされる前提でよい

## Review Focus

1. 影なしの旧タイムライン / 旧プリセットを読み、保存し直しても XML が変わらない (Task 2・3 のテスト)
2. XML の `Shadows` / `shadows` に範囲外の値 (例: 9, -1) があっても例外にならず なし で読む (Task 1・2・3 のテスト)
3. UI で影の種類を変えた直後にタイムラインを保存しても、新しい値が書かれる (Task 4 で `LateUpdate(true)` を呼ぶ。輪郭と同じ同期経路)
4. Undo で影の種類が戻り、無関係な編集の Undo 判定 (`Approximately`) を壊さない (Task 3)
5. 種別を平行 → スポットへ変えても影の種類は保持される (`Light.shadows` は種別変更で変わらない。実機確認 Task 6)

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/LightShadowValues.cs` | 外部入力の int → `LightShadows` の丸め |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineLightXml` に `Shadows` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `TimelineLightData.shadows` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs` | stat に `shadows` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` | 変更検出と同期、ロード時の適用 |
| Modify `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetAdditionalLight.shadows`、v37 コメント |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs` | 記録・復元・比較 |
| Modify `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs` | コピー / ペースト |
| Modify `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs` | 影の UI |
| Tests `LightShadowValuesTests.cs` / `LightShadowXmlTests.cs` / `ScenePresetLightShadowTests.cs` | |
| Modify `docs-site/guide/staging.md` / `docs-site/timeline/compatibility.md` / `W:\COM3D2_5\work\CLAUDE.md` | ドキュメント |

---

### Task 1: 影の種類の丸め

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LightShadowValues.cs`
- Modify: csproj (`<Compile Include="ImagePathResolver.cs" />` の後に 1 行)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightShadowValuesTests.cs`

**Interfaces:**
- Produces: `static class LightShadowValues { LightShadows FromInt(int value); }` (namespace `COM3D2.SceneEditor.Plugin`)

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>XML 由来の影の種類の丸めを固定する</summary>
    public class LightShadowValuesTests
    {
        [Theory]
        [InlineData(0, LightShadows.None)]
        [InlineData(1, LightShadows.Hard)]
        [InlineData(2, LightShadows.Soft)]
        public void 定義済みの値はそのまま読む(int value, LightShadows expected)
        {
            Assert.Equal(expected, LightShadowValues.FromInt(value));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(9)]
        public void 範囲外の値は影なしにする(int value)
        {
            Assert.Equal(LightShadows.None, LightShadowValues.FromInt(value));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightShadowValues"`
Expected: コンパイルエラー (`LightShadowValues` が無い)

- [ ] **Step 3: 実装する**

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 追加ライトの影の種類。XML には LightShadows の数値 (0=なし / 1=ハード / 2=ソフト) で保存する
    /// </summary>
    public static class LightShadowValues
    {
        /// <summary>XML など外部入力由来の値を丸める。範囲外は影なし</summary>
        public static LightShadows FromInt(int value)
        {
            switch (value)
            {
                case (int)LightShadows.Hard:
                    return LightShadows.Hard;
                case (int)LightShadows.Soft:
                    return LightShadows.Soft;
                default:
                    return LightShadows.None;
            }
        }
    }
}
```

csproj (`<Compile Include="ImagePathResolver.cs" />` の直後):

```xml
    <Compile Include="LightShadowValues.cs" />
```

- [ ] **Step 4: テストが通ることを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightShadowValues"`
Expected: PASS (6 件)

- [ ] **Step 5: コミット** (例: `feat(light): 追加ライトの影の種類の丸めを追加する`)

---

### Task 2: タイムラインのライト定義へ保存する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` (`TimelineLightXml`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` (`TimelineLightData`、このファイルは別名 `SE = SceneEditor.Plugin` を使う)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` (`LateUpdate(bool)` / `SetupLights`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightShadowXmlTests.cs`

**Interfaces:**
- Consumes: `LightShadowValues.FromInt(int)`
- Produces: `TimelineLightXml.shadows (int)`、`TimelineLightData.shadows (LightShadows)`、`StudioLightStat.shadows (LightShadows)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義の影の種類 (&lt;Shadows&gt;) の保存と読込を固定する。
    /// 影なしは書き出さず、要素の無い旧 XML は影なしとして読む
    /// </summary>
    public class LightShadowXmlTests
    {
        private static string Serialize(TimelineLightXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineLightData FromText(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var reader = new StringReader(text))
            {
                var restored = new TimelineLightData();
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
                return restored;
            }
        }

        [Fact]
        public void 影なしは書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot };

            Assert.DoesNotContain("Shadows", Serialize(data.ToXml()));
        }

        [Fact]
        public void 影の種類を往復する()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot, shadows = LightShadows.Soft };

            var text = Serialize(data.ToXml());

            Assert.Contains("<Shadows>2</Shadows>", text);
            Assert.Equal(LightShadows.Soft, FromText(text).shadows);
        }

        [Fact]
        public void 要素の無い旧XMLは影なしとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type></TimelineLightXml>");

            Assert.Equal(LightShadows.None, restored.shadows);
        }

        [Fact]
        public void 範囲外の値は影なしとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type><Shadows>9</Shadows></TimelineLightXml>");

            Assert.Equal(LightShadows.None, restored.shadows);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightShadowXml"`
Expected: コンパイルエラー (`TimelineLightData.shadows` が無い)

- [ ] **Step 3: `TimelineLightXml` に要素を足す** (`ShouldSerializecookieImage` の後)

```csharp

        // 影の種類 (LightShadows の数値) も SE 独自。影なしでは書き出さない。
        // 範囲外の値でも読めるよう int で持ち、読込時に LightShadowValues.FromInt で丸める
        [XmlElement("Shadows")]
        public int shadows;

        public bool ShouldSerializeshadows() { return shadows != (int)LightShadows.None; }
```

- [ ] **Step 4: `TimelineLightData` に `shadows` を足す**

- フィールド: `public SE.LightCookieData cookie = ...;` の後に `public LightShadows shadows = LightShadows.None;`
- `FromStat`: `cookie = light.cookie;` の後に `shadows = light.shadows;`
- `FromXml`: cookie の代入の後に `shadows = SE.LightShadowValues.FromInt(xml.shadows);`
- `ToXml`: 初期化子の `cookieImage = cookie.image,` の後に `shadows = (int)shadows,`

- [ ] **Step 5: `StudioLightStat` に `shadows` を足す**

`cookie` フィールドの後に:

```csharp

        /// <summary>影の種類の写し。輪郭と同じく LateUpdate の比較でライト定義へ同期する</summary>
        public LightShadows shadows = LightShadows.None;
```

- `StudioLightStat(Light light, ...)` の末尾に `this.shadows = light.shadows;`
- `FromStat` の `cookie = stat.cookie;` の後に `shadows = stat.shadows;`

- [ ] **Step 6: タイムライン側 `StudioLightManager` を変更する**

`LateUpdate(bool)` の `cookieChanged` を `definitionChanged` に改名し、比較を次に置き換える:

```csharp
                // 輪郭と影の種類はライト定義の値なので、一覧の作り直し (イベント発火) はせず定義だけ同期する
                if (!cachedLight.cookie.Equals(stat.cookie) || cachedLight.shadows != stat.shadows)
                {
                    cachedLight.cookie = stat.cookie;
                    cachedLight.shadows = stat.shadows;
                    definitionChanged = true;
                }
```

(`else if (cookieChanged)` も `else if (definitionChanged)` にする)

`SetupLights` の、輪郭を適用している 2 箇所 (`if (i > 0)` のブロック内) にそれぞれ影の種類の適用を足す:

```csharp
                        newLight.shadows = lightData.shadows;
```

```csharp
                        stat.light.shadows = lightData.shadows;
```

- [ ] **Step 7: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS

- [ ] **Step 8: コミット** (例: `feat(timeline): ライト定義に追加ライトの影の種類を保存する`)

---

### Task 3: シーンプリセット・Undo・クリップボード

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` (`ScenePresetAdditionalLight`、v37 コメント)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightShadowTests.cs`

**Interfaces:**
- Consumes: `LightShadowValues.FromInt(int)`
- Produces: `ScenePresetAdditionalLight.shadows (int)`、`ScenePresetAdditionalLight.GetShadows()` / `SetShadows(LightShadows)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトの影の種類を固定する</summary>
    public class ScenePresetLightShadowTests
    {
        private static string Serialize(ScenePresetAdditionalLight light)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, light);
                return writer.ToString();
            }
        }

        private static ScenePresetAdditionalLight Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetAdditionalLight)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 影なしは属性を書き出さない()
        {
            Assert.DoesNotContain("shadows=", Serialize(new ScenePresetAdditionalLight()));
        }

        [Fact]
        public void 影の種類を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetShadows(LightShadows.Hard);

            var text = Serialize(light);

            Assert.Contains("shadows=\"1\"", text);
            Assert.Equal(LightShadows.Hard, Deserialize(text).GetShadows());
        }

        [Fact]
        public void 範囲外の値は影なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\" shadows=\"9\" />";

            Assert.Equal(LightShadows.None, Deserialize(text).GetShadows());
        }

        [Fact]
        public void 属性の無い旧プリセットは影なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.Equal(LightShadows.None, Deserialize(text).GetShadows());
        }
    }
}
```

注: 既存の要素 `shadowStrength` / `shadowBias` と紛れないよう、非出力の確認は `shadows=` (属性) で行う。

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePresetLightShadow"`
Expected: コンパイルエラー (`SetShadows` が無い)

- [ ] **Step 3: `ScenePresetAdditionalLight` に属性を足す** (`SetCookie` の後)

```csharp

        /// <summary>
        /// 影の種類 (LightShadows の数値)。旧プリセットには無いので影なしで読み、影なしでは書き出さない。
        /// 範囲外の値でもプリセット全体の読込を失敗させないよう int で持つ
        /// </summary>
        [XmlAttribute]
        public int shadows = (int)LightShadows.None;

        public bool ShouldSerializeshadows() { return shadows != (int)LightShadows.None; }

        public LightShadows GetShadows() { return LightShadowValues.FromInt(shadows); }

        public void SetShadows(LightShadows value) { shadows = (int)value; }
```

v37 の変更履歴コメントを次に置き換える (CurrentVersion は 37 のまま):

```csharp
        // v37: 追加ライトに輪郭 (cookieMode / cookieHardness / cookieImage) と影の種類 (shadows) を追加。
        //      既定の輪郭・影なしでは書き出さない。旧形式は属性が無く既定の輪郭・影なしとして読める
```

- [ ] **Step 4: `LightSnapshot` を変更する**

- `CaptureState`: `lightState.SetCookie(LightCookie.Get(light));` の後に `lightState.SetShadows(light.shadows);`
- `ApplyLightState`: `LightCookie.Set(light, lightState.GetCookie());` の後に `light.shadows = lightState.GetShadows();`
- `Approximately`: 追加ライト比較の条件末尾に `|| a.GetShadows() != b.GetShadows()`

- [ ] **Step 5: `LightClipboard` を変更する**

- `Data` に `public LightShadows shadows;`
- `Copy` の初期化子に `shadows = light.shadows,`
- `CopyMain` の初期化子に `shadows = light.shadows,` (メインライトの影の種類を写す)
- `Paste` の `LightCookie.Set(light, _data.cookie);` の後に `light.shadows = _data.shadows;`
- `PasteMain` は変更しない

- [ ] **Step 6: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS (`ScenePresetEffectsTests` の CurrentVersion == 37 も通る)

- [ ] **Step 7: コミット** (例: `feat(light): 影の種類をシーンプリセット・Undo・コピーに含める`)

---

### Task 4: ライト設定の UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs` (`DrawAdditionalLightParams` の影のブロック)

**Interfaces:**
- Consumes: `MTEP.StudioLightManager.instance.LateUpdate(true)`

- [ ] **Step 1: 影のブロックを置き換える**

現在の:

```csharp
            // 影は平行光源だけが落とす（ライトレイヤーの表示条件に合わせる）
            if (light.type == LightType.Directional)
            {
                DrawAxisSlider(view, labelWidth, "影の濃さ", ...);
                DrawAxisSlider(view, labelWidth, "影の距離", ...);
            }
```

を次にする:

```csharp
            DrawShadowTypeRow(view, labelWidth, rowHeight, light);

            // 濃さと距離は影を落とすときだけ意味を持つ
            if (light.shadows != LightShadows.None)
            {
                DrawAxisSlider(view, labelWidth, "影の濃さ", light.shadowStrength, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowStrength, value => light.shadowStrength = value);
                DrawAxisSlider(view, labelWidth, "影の距離", light.shadowBias, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowBias, value => light.shadowBias = value);
            }
```

- [ ] **Step 2: 描画メソッドを足す** (`DrawCookieRows` の前)

```csharp
        /// <summary>
        /// 影の種類。追加ライトは影なしで生成されるので、影を落とすにはここで種類を選ぶ。
        /// 影を落とす灯が増えるほどシャドウマップの描画が増えて重くなる
        /// </summary>
        private static void DrawShadowTypeRow(GUIView view, float labelWidth, float rowHeight, Light light)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("影", labelWidth, rowHeight);
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.None, "なし");
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.Hard, "ハード");
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.Soft, "ソフト");
            }
            view.EndLayout();
        }

        private static void DrawShadowTypeButton(
            GUIView view, float rowHeight, Light light, LightShadows shadows, string label)
        {
            var isCurrent = light.shadows == shadows;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("影");
                light.shadows = shadows;
                // 影の種類もタイムラインのライト定義に載るので、直後の保存で古い値が書かれないよう即時に同期させる
                MTEP.StudioLightManager.instance.LateUpdate(true);
            }
        }
```

- [ ] **Step 3: 両構成でビルドし、テストを流す**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: エラー 0、全件 PASS

- [ ] **Step 4: コミット** (例: `feat(light): 追加ライトの影の種類を設定する UI を追加する`)

---

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md` (追加ライトの種別の段落・輪郭の表の後)
- Modify: `docs-site/timeline/compatibility.md` (輪郭の行の後)
- Modify: `W:\COM3D2_5\work\CLAUDE.md` (輪郭の行に追記。git 管理外)

- [ ] **Step 1: `staging.md` に追記する** (輪郭の説明段落の後)

```markdown

追加ライトは `影` で影の種類（`なし` / `ハード` / `ソフト`）を選べます。追加したライトは `なし` で始まります。
`なし` 以外のときだけ `影の濃さ` / `影の距離` を編集できます。影を落とすライトが増えるほど重くなります。
特に `ポイント` の影は全方位 6 面ぶんの影を描くため、スポットや平行より重くなります。
```

- [ ] **Step 2: `compatibility.md` に 1 行足す** (輪郭の行の後)

```markdown
- 追加ライトの `影` の種類もライト定義として保存されます。SceneEditor 独自の値で、MTE や以前の SceneEditor で読むと影なしになります
```

- [ ] **Step 3: ワークスペースの `CLAUDE.md` の輪郭の行を置き換える**

```markdown
- ライト定義（`<Lights>` の `<Light>`）の輪郭（`CookieMode` / `CookieHardness` / `CookieImage`）と影の種類（`Shadows`、LightShadows の数値）は SE 独自。既定の輪郭（cookie なし）・影なしでは書き出さない。MTE は要素を読み飛ばすため Unity 内蔵の輪郭・影なしで表示される。シーンプリセットは v37 で同名の属性を追加
```

- [ ] **Step 4: コミット** (例: `docs(light): 追加ライトの影の種類を説明する`。CLAUDE.md はリポジトリ外なので含めない)

---

### Task 6: 実機確認

**Files:** なし (確認のみ)

- [ ] **Step 1: restart-verify スキルで DLL を反映し、ゲームを起動してセーブをロードする** (再起動前にユーザーへ確認する)
- [ ] **Step 2: devbridge で確認する**
  - 新規に追加したライトの `light.shadows` が None で始まること
  - 斜め上から照らすスポットを追加し、UI 相当の操作 (`light.shadows` の変更) で なし → ソフト の前後を `screenshot` で比べ、影が落ちることを確認する
  - 同じ配置でポイントライトでも なし → ソフト を比べ、影が落ちることを確認する (再生中なら一時停止して比べる)。落ちない場合はポイントの影ボタンを出さないよう UI を絞り、Ruling に残す
  - ライトウィンドウで「影」行と、影ありのときだけ濃さ・距離のスライダーが出ることを確認する
  - 平行 → スポットへ種別を変えても `light.shadows` が保持されること
  - `LightSnapshot.Capture` → 影なしへ変更 → `Apply` で戻ること。`LightClipboard.Copy` → 別ライトへ `Paste` で写ること
  - `TimelineLightData(stat).ToXml()` を XmlSerializer で書き、`<Shadows>` が影ありのライトにだけ出ること。`SetupLights` で影の種類が反映されること
  - `tail_log` に SceneEditor 由来の例外が無いこと
- [ ] **Step 3: 後片付け**: テスト用ライトを削除する
