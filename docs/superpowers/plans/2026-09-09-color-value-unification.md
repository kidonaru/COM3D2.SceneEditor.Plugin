# 色値の統一（ColorValueInfo）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインのキーフレームデータで色を float / string と並ぶ第 3 の値種別（`ColorValueInfo`）として統一し、キーフレーム詳細で全ての色をカラーピッカー行で編集できるようにする。

**Architecture:** `ITransformData` に `GetColorValueInfoMap()` と色の取得・設定 API を追加し、`hasColor` 系 6 メンバーを廃止する。既存の主色・副色は `"color"` / `"subColor"` キーで同じマップに載せ、`color` / `subColor` プロパティは基底クラスの糖衣として残す。3 段階（追加 → 15 型のマップ定義 → 旧メンバー撤去と UI 切替）で進め、各段階でビルドとテストが通る状態を保つ。

**Tech Stack:** C#（COM3D2 = .NET 3.5 / COM3D25 = net48 の 2 構成）、xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`、`dotnet test`）、Unity IMGUI ラッパー `GUIView`

**Spec:** `docs/superpowers/specs/2026-09-09-color-value-unification-design.md`

## Global Constraints

- git worktree を使わない。メインの作業ディレクトリで作業する
- COM3D2 / COM3D25 の両構成をビルドする。MSBuild を直接叩く（`debug.bat` は使わない）:
  ```bash
  cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
  export $(sed 's/\r$//' ../../.env | xargs -d '\n')
  MSB="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
  for gv in COM3D2 COM3D25; do MSYS_NO_PATHCONV=1 "$MSB" COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=$gv "-p:COM3D2_DIR=$COM3D2_DIR" "-p:COM3D25_DIR=$COM3D25_DIR" -v:m -nologo 2>&1 | grep -i "error\|->"; done
  ```
  **注意**: MSBuild 直叩きでも `Sybaris/UnityInjector` の DLL は更新される（前回の実測）。ゲーム起動中はコピーが失敗して警告になるだけ
- テストは `cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin && dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- 本体 csproj は旧形式で `Compile Include` を明示している。**新規 .cs を本体に足す場合は csproj へ追加が必要**。テスト csproj は SDK 形式で自動収集
- コメント・ログ文言は日本語。XML レイアウト（`values` / `InTangents` の並び）は変えない。`TimelineData.CurrentVersion` も上げない
- コミットは `commit` スキル（Conventional Commits、日本語）

---

## 背景（読む順）

- 値の実体は `TransformDataBase._values`（`ValueData[]`）。`Initialize` と `Clone` で配列が差し替わるので、派生配列のキャッシュは `ClearValueDataListCache()` で捨てる（`TransformDataBase.cs:30-35` に既存の 3 種）
- 色の現状は spec 参照。15 型の色 index はすべて **R,G,B,(A) が連続**している（下表）
- 色を参照する UI は `KeyFrameInspector`（`hasColor` / `initialColor`）、`PostEffectRowDrawer` / `StageLaserRowDrawer` / `StageLightRowDrawer`（`defaultTrans.initialColor` / `initialSubColor`）のみ。レイヤーの再生コードは `trans.color` / `trans.subColor` 糖衣だけを使う
- 副色を持つ型（Bloom / DistanceFog / Paraffin / Rimlight / StageLaser / StageLaserController / StageLightController）はすべて `hasSubColor => true` と `subColorValues` を対で持つ。新マップでは `"subColor"` を登録する
- **Task 1 と Task 2 は間を空けずに一続きで実施する。** Task 1 だけの DLL では 15 型の色マップが空で、`trans.color` の取得は白、設定は無視されるため、実機へ反映すると色編集が機能しない

### 15 型の色定義一覧（Task 2 で使う）

| 型 | キー | 表示名 | 先頭 index | A | 既定色 |
|---|---|---|---|---|---|
| BGColor | color | 色 | 0 | 無 | `Color.black` |
| BGGroundColor | color | 色 | 6 | 無 | `BGGround.DefaultColor` |
| Bloom | color | しきい値色 | `Index.ColorR` (2) | 有 | `Color.white` |
| Bloom | subColor | ﾌﾚｱ色A | `Index.SubColorR` (6) | 有 | `new Color(0.4f, 0.4f, 0.8f, 0.75f)` |
| Bloom | flareColorB | ﾌﾚｱ色B | `Index.FlareColorBR` (31) | 有 | `InitialFlareColorB` |
| Bloom | flareColorC | ﾌﾚｱ色C | `Index.FlareColorCR` (35) | 有 | `InitialFlareColorC` |
| Bloom | flareColorD | ﾌﾚｱ色D | `Index.FlareColorDR` (39) | 有 | `InitialFlareColorD` |
| DistanceFog | color | 色1 | `Index.ColorR` (0) | 有 | `new Color(1f, 1f, 1f, 1f)` |
| DistanceFog | subColor | 色2 | `Index.SubColorR` (4) | 有 | `new Color(1f, 1f, 1f, 0f)` |
| Light | color | 色 | `Index.ColorR` (7) | 無 | `Color.white` |
| ModelMaterial | color | 色 | `Index.ColorR` (1) | 有 | `Color.white` |
| ModelMaterial | ShadowColor | 影色 | `Index.ShadowColorR` (5) | 有 | `new Color(0f, 0f, 0f, 1f)` |
| ModelMaterial | RimColor | リム色 | `Index.RimColorR` (9) | 有 | `new Color(0f, 0f, 0f, 1f)` |
| ModelMaterial | OutlineColor | アウトライン | `Index.OutlineColorR` (13) | 有 | `new Color(0f, 0f, 0f, 1f)` |
| ModelMaterial | EmissionColor | 発光色 | `Index.EmissionColorR` (21) | 有 | `Color.white` |
| ModelMaterial | MatcapColor | マットキャップ色 | `Index.MatcapColorR` (25) | 有 | `Color.white` |
| ModelMaterial | MatcapMaskColor | マットキャップマスク色 | `Index.MatcapMaskColorR` (29) | 有 | `Color.white` |
| ModelMaterial | RimLightColor | リムライト色 | `Index.RimLightColorR` (33) | 有 | `Color.white` |
| Paraffin | color / subColor | 色1 / 色2 | `Index.ColorR` (0) / `Index.SubColorR` (4) | 有 | `(1,1,1,1)` / `(1,1,1,0)` |
| PngObject | color | 色 | `Index.ColorR` (6) | 有 | `Color.white` |
| Rimlight | color / subColor | 色1 / 色2 | `Index.ColorR` (3) / `Index.SubColorR` (7) | 有 | `(1,1,1,1)` / `(1,1,1,0)` |
| StageLaser | color / subColor | 中心色 / 錯乱色 | `Index.ColorR` (4) / `Index.SubColorR` (8) | 有 | `StageLaser.DefaultColor1` / `DefaultColor2` |
| StageLaserController | color / subColor | 中心色 / 錯乱色 | `Index.ColorR` (6) / `Index.SubColorR` (10) | 有 | `StageLaser.DefaultColor1` / `DefaultColor2` |
| StageLight | color | 色 | `Index.ColorR` (7) | 有 | `new Color(1f, 1f, 1f, 0.3f)` |
| StageLightController | color / subColor | 最小色 / 最大色 | `Index.ColorR` (12) / `Index.SubColorR` (16) | 有 | `new Color(1f, 1f, 1f, 0.3f)` 両方 |
| Text | color | 色 | `Index.ColorR` (9) | 有 | `Color.white` |
| PsylliumBar | color1a / color1b / color1c | 中心色1 / 縁色1 / 散乱色1 | `Index.Color1aR` (0) / `Color1bR` (4) / `Color1cR` (8) | 有 | `defaultConfig.color1a` / `color1b` / `color1c` |
| PsylliumBar | color2a / color2b / color2c | 中心色2 / 縁色2 / 散乱色2 | `Index.Color2aR` (12) / `Color2bR` (16) / `Color2cR` (20) | 有 | `defaultConfig.color2a` / `color2b` / `color2c` |

表示名は現在値 UI（`PostEffectRowDrawer` の `/色1`・`/しきい値色`・`/ﾌﾚｱ色A`、`StageLaserRowDrawer` の「中心色」「錯乱色」、`StageLightRowDrawer` の「最小色」）に合わせている。

---

### Task 1: 色値 API の追加（既存メンバーは残す）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（`StrValueInfo` の直後、インターフェース末尾）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ColorValueInfoTests.cs`

**Interfaces:**
- Produces:
  - `class ColorValueInfo { string name; int indexR, indexG, indexB, indexA = -1; Color defaultValue; bool hasAlpha; static ColorValueInfo Rgba(string name, int indexR, Color defaultValue); static ColorValueInfo Rgb(string name, int indexR, Color defaultValue); }`
  - `ITransformData`: `Dictionary<string, ColorValueInfo> GetColorValueInfoMap()`, `ColorValueInfo GetColorValueInfo(string key)`, `Color GetColorValue(string key)`, `void SetColorValue(string key, Color color)`, `Color GetDefaultColorValue(string key)`, `bool HasColorValue(string key)`, `string GetColorValueName(string key)`
  - `TransformDataBase.ColorKey.Main = "color"`, `ColorKey.Sub = "subColor"`
  - `TransformDataBase.valuesWithoutColors`（`protected ValueData[]`、キャッシュ付き）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ColorValueInfoTests.cs` を新規作成する。テスト専用の派生型で API の挙動を固定する。

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ColorValueInfoTests
    {
        /// <summary>values[0..3] に RGBA、values[4..6] に RGB、values[7] に非色の値を持つ型</summary>
        private class FakeColorTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 8;
            public override bool hasTangent => true;
            public override ValueData[] tangentValues => valuesWithoutColors;

            private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
                new Dictionary<string, ColorValueInfo>
                {
                    { ColorKey.Main, ColorValueInfo.Rgba("色", 0, new Color(0.1f, 0.2f, 0.3f, 0.4f)) },
                    { "rgb", ColorValueInfo.Rgb("RGB", 4, Color.black) },
                };

            public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
        }

        private static FakeColorTransform Create()
        {
            var trans = new FakeColorTransform();
            trans.Initialize("fake");
            return trans;
        }

        [Fact]
        public void Rgbaは連続4成分でアルファ有り()
        {
            var info = ColorValueInfo.Rgba("色", 5, Color.white);
            Assert.Equal(5, info.indexR);
            Assert.Equal(6, info.indexG);
            Assert.Equal(7, info.indexB);
            Assert.Equal(8, info.indexA);
            Assert.True(info.hasAlpha);
        }

        [Fact]
        public void Rgbはアルファ無し()
        {
            var info = ColorValueInfo.Rgb("色", 2, Color.white);
            Assert.Equal(4, info.indexB);
            Assert.Equal(-1, info.indexA);
            Assert.False(info.hasAlpha);
        }

        [Fact]
        public void SetColorValueで書いた色をGetColorValueで読める()
        {
            var trans = Create();
            trans.SetColorValue(ColorKeyMain, new Color(0.5f, 0.6f, 0.7f, 0.8f));
            Assert.Equal(new Color(0.5f, 0.6f, 0.7f, 0.8f), trans.GetColorValue(ColorKeyMain));
            Assert.Equal(0.8f, trans.values[3].value);
        }

        [Fact]
        public void RGBのみの色はアルファ1で読めアルファは書かれない()
        {
            var trans = Create();
            trans.values[7].value = 9f;
            trans.SetColorValue("rgb", new Color(0.1f, 0.2f, 0.3f, 0.5f));
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), trans.GetColorValue("rgb"));
            Assert.Equal(9f, trans.values[7].value);
        }

        [Fact]
        public void 糖衣のcolorはMainキーと同じ値()
        {
            var trans = Create();
            trans.color = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            Assert.Equal(trans.GetColorValue(ColorKeyMain), trans.color);
        }

        [Fact]
        public void 既定値と存在判定と名前()
        {
            var trans = Create();
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetDefaultColorValue(ColorKeyMain));
            Assert.True(trans.HasColorValue("rgb"));
            Assert.False(trans.HasColorValue("none"));
            Assert.Equal("RGB", trans.GetColorValueName("rgb"));
        }

        [Fact]
        public void Resetで色が既定値に戻る()
        {
            var trans = Create();
            trans.SetColorValue(ColorKeyMain, Color.red);
            trans.SetColorValue("rgb", Color.red);
            trans.Reset();
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetColorValue(ColorKeyMain));
            Assert.Equal(new Color(0f, 0f, 0f, 1f), trans.GetColorValue("rgb"));
        }

        [Fact]
        public void valuesWithoutColorsは色成分を含まない()
        {
            var trans = Create();
            var tangents = trans.tangentValues;
            Assert.Single(tangents);
            Assert.Same(trans.values[7], tangents[0]);
        }

        private const string ColorKeyMain = "color";
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin && dotnet test source/COM3D2.SceneEditor.Plugin.Tests 2>&1 | grep -i "error\|失敗\|合格" | head`
Expected: コンパイルエラー（`ColorValueInfo` が未定義）

- [ ] **Step 3: ColorValueInfo とインターフェースを追加する**

`ITransformData.cs` の `StrValueInfo` クラス直後に追加する。

```csharp
    /// <summary>
    /// 色 1 個ぶんの値ビュー。values 内の R/G/B/(A) の添字と既定色を持つ。
    /// float (CustomValueInfo) / string (StrValueInfo) と並ぶ第 3 の値種別で、
    /// 色は線形補間に統一するためタンジェント編集の対象から外す
    /// </summary>
    public class ColorValueInfo
    {
        public string name;
        public int indexR;
        public int indexG;
        public int indexB;
        /// <summary>-1 なら RGB のみ (アルファ成分を持たない)</summary>
        public int indexA = -1;
        public Color defaultValue;

        public bool hasAlpha => indexA >= 0;

        /// <summary>indexR から連続 4 成分 (R,G,B,A) の色</summary>
        public static ColorValueInfo Rgba(string name, int indexR, Color defaultValue)
        {
            return new ColorValueInfo
            {
                name = name,
                indexR = indexR,
                indexG = indexR + 1,
                indexB = indexR + 2,
                indexA = indexR + 3,
                defaultValue = defaultValue,
            };
        }

        /// <summary>indexR から連続 3 成分 (R,G,B) の色</summary>
        public static ColorValueInfo Rgb(string name, int indexR, Color defaultValue)
        {
            return new ColorValueInfo
            {
                name = name,
                indexR = indexR,
                indexG = indexR + 1,
                indexB = indexR + 2,
                indexA = -1,
                defaultValue = defaultValue,
            };
        }
    }
```

`ITransformData` インターフェースの `GetStrValueInfoMap();` の直後に追加する。

```csharp
        Dictionary<string, ColorValueInfo> GetColorValueInfoMap();
        ColorValueInfo GetColorValueInfo(string colorKey);
        Color GetColorValue(string colorKey);
        void SetColorValue(string colorKey, Color color);
        Color GetDefaultColorValue(string colorKey);
        bool HasColorValue(string colorKey);
        string GetColorValueName(string colorKey);
```

- [ ] **Step 4: TransformDataBase に実装を追加する**

(a) クラス先頭付近（`public ValueData[] values => _values;` の直後）にキー定数を置く。

```csharp
        /// <summary>主色 / 副色の固定キー。color / subColor 糖衣が参照する</summary>
        public static class ColorKey
        {
            public const string Main = "color";
            public const string Sub = "subColor";
        }
```

(b) `color` / `subColor` プロパティ（`TransformDataBase.cs:154-164`）を色マップ経由に置き換える。

```csharp
        public Color color
        {
            get => GetColorValue(ColorKey.Main);
            set => SetColorValue(ColorKey.Main, value);
        }

        public Color subColor
        {
            get => GetColorValue(ColorKey.Sub);
            set => SetColorValue(ColorKey.Sub, value);
        }
```

(c) `ClearValueDataListCache()`（`:30-35`）に 1 行足す。

```csharp
        private void ClearValueDataListCache()
        {
            _valueDataListCache = null;
            _inTangentListCache = null;
            _outTangentListCache = null;
            _valuesWithoutColors = null;
        }
```

(d) `public virtual ValueData[] tangentValues => new ValueData[0];`（`:230`）の直後に追加する。

```csharp
        private ValueData[] _valuesWithoutColors = null;

        /// <summary>
        /// values から色マップの全成分を除いた配列。色は線形補間に統一するため
        /// タンジェント編集の対象にしない型が tangentValues として返す。
        /// _values の差し替え時 (Initialize / Clone) に ClearValueDataListCache で捨てる
        /// </summary>
        protected ValueData[] valuesWithoutColors
        {
            get
            {
                if (_valuesWithoutColors != null)
                {
                    return _valuesWithoutColors;
                }

                var colorIndices = new HashSet<int>();
                foreach (var info in GetColorValueInfoMap().Values)
                {
                    colorIndices.Add(info.indexR);
                    colorIndices.Add(info.indexG);
                    colorIndices.Add(info.indexB);
                    if (info.hasAlpha)
                    {
                        colorIndices.Add(info.indexA);
                    }
                }

                var list = new List<ValueData>(values.Length);
                for (var i = 0; i < values.Length; i++)
                {
                    if (!colorIndices.Contains(i))
                    {
                        list.Add(values[i]);
                    }
                }
                _valuesWithoutColors = list.ToArray();
                return _valuesWithoutColors;
            }
        }
```

(e) `GetStrValueInfoMap()`（`:844` 付近）の手前、`GetDefaultCustomValue` の直後に色 API を追加する。

```csharp
        /// <summary>色を持たない型が毎フレームの UI 描画で空マップを作り直さないよう使い回す</summary>
        private static readonly Dictionary<string, ColorValueInfo> EmptyColorValueInfoMap
            = new Dictionary<string, ColorValueInfo>();

        public virtual Dictionary<string, ColorValueInfo> GetColorValueInfoMap()
        {
            return EmptyColorValueInfoMap;
        }

        public ColorValueInfo GetColorValueInfo(string colorKey)
        {
            ColorValueInfo info;
            if (GetColorValueInfoMap().TryGetValue(colorKey, out info))
            {
                return info;
            }

            MTEUtils.LogError("ColorValueが見つかりません colorKey={0}", colorKey);
            return null;
        }

        /// <summary>RGB のみの色はアルファ 1 で返す</summary>
        public Color GetColorValue(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            if (info == null)
            {
                return Color.white;
            }
            return new Color(
                values[info.indexR].value,
                values[info.indexG].value,
                values[info.indexB].value,
                info.hasAlpha ? values[info.indexA].value : 1f);
        }

        /// <summary>RGB のみの色はアルファを捨てる</summary>
        public void SetColorValue(string colorKey, Color color)
        {
            var info = GetColorValueInfo(colorKey);
            if (info == null)
            {
                return;
            }
            values[info.indexR].value = color.r;
            values[info.indexG].value = color.g;
            values[info.indexB].value = color.b;
            if (info.hasAlpha)
            {
                values[info.indexA].value = color.a;
            }
        }

        public Color GetDefaultColorValue(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            return info != null ? info.defaultValue : Color.white;
        }

        public bool HasColorValue(string colorKey)
        {
            return GetColorValueInfoMap().ContainsKey(colorKey);
        }

        public string GetColorValueName(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            return info != null ? info.name : colorKey;
        }
```

(f) `Reset()`（`:1037`）の `if (hasColor) {...}` と `if (hasSubColor) {...}` の 2 ブロックを削除し、カスタム値のループの直前に色のループを足す。

```csharp
            foreach (var pair in GetColorValueInfoMap())
            {
                SetColorValue(pair.Key, pair.Value.defaultValue);
            }
```

`hasColor` / `hasSubColor` / `colorValues` / `subColorValues` / `initialColor` / `initialSubColor` の virtual 宣言自体はこの Task では**残す**（15 型がまだ override している）。

- [ ] **Step 5: 両構成をビルドし、テストが通ることを確認する**

Run: Global Constraints のビルドコマンド、続けて `dotnet test`
Expected: ビルド 0 エラー。`ColorValueInfoTests` 8 件を含め全 PASS。
注意: 既存型はまだ色マップが空なので、`color` 糖衣を通る XML ラウンドトリップ系テストがあれば `ColorValueが見つかりません` のログが出る。**ログは出てもテストは通る**（`GetColorValue` は白を返す）。通らない場合は Task 2 完了まで待たず、原因を確認する

- [ ] **Step 6: コミットする**

`commit` スキルで:
```
feat(timeline): 色値 API (ColorValueInfo) を TransformData に追加する
```

---

### Task 2: 15 型に色マップを定義する（旧メンバーと並存）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/` の 15 ファイル（一覧表の型）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/ColorValueInfoTests.cs`（総当たりテストを追加）

**Interfaces:**
- Consumes: Task 1 の `ColorValueInfo.Rgba / Rgb`、`ColorKey.Main / Sub`、`GetColorValueInfoMap()`
- Produces: 15 型それぞれの `GetColorValueInfoMap()` override。キーは一覧表どおり

- [ ] **Step 1: 旧定義との整合を突き合わせる総当たりテストを書く**

`ColorValueInfoTests.cs` に追加する。この時点では旧メンバー（`colorValues` 等）が残っているので、新マップが同じ `ValueData` を指すことを参照同一性で検証できる。**このテストは Task 3 で旧メンバーと一緒に削除する。**

```csharp
        /// <summary>アセンブリ内の具象 TransformData を全部生成する</summary>
        private static IEnumerable<TransformDataBase> AllTransforms()
        {
            var baseType = typeof(TransformDataBase);
            foreach (var type in baseType.Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(baseType) || type.IsAbstract
                    || type.GetConstructor(System.Type.EmptyTypes) == null)
                {
                    continue;
                }
                var trans = (TransformDataBase)System.Activator.CreateInstance(type);
                trans.Initialize("test");
                yield return trans;
            }
        }

        [Fact]
        public void 旧colorValuesと新マップは同じ値を指す()
        {
            foreach (var trans in AllTransforms())
            {
                if (trans.colorValues.Length > 0)
                {
                    var info = trans.GetColorValueInfo("color");
                    Assert.NotNull(info);
                    Assert.Same(trans.colorValues[0], trans.values[info.indexR]);
                    Assert.Same(trans.colorValues[1], trans.values[info.indexG]);
                    Assert.Same(trans.colorValues[2], trans.values[info.indexB]);
                    Assert.Equal(trans.colorValues.Length == 4, info.hasAlpha);
                    if (info.hasAlpha)
                    {
                        Assert.Same(trans.colorValues[3], trans.values[info.indexA]);
                    }
                    Assert.Equal(trans.initialColor, info.defaultValue);
                }
                if (trans.subColorValues.Length > 0)
                {
                    var info = trans.GetColorValueInfo("subColor");
                    Assert.NotNull(info);
                    Assert.Same(trans.subColorValues[0], trans.values[info.indexR]);
                    Assert.Same(trans.subColorValues[3], trans.values[info.indexA]);
                    Assert.Equal(trans.initialSubColor, info.defaultValue);
                }
            }
        }

        [Fact]
        public void ModelMaterialの追加色は型付きアクセサと一致する()
        {
            var trans = new TransformDataModelMaterial();
            trans.Initialize("mm");
            Assert.Same(trans.ShadowColorValues[0], trans.values[trans.GetColorValueInfo("ShadowColor").indexR]);
            Assert.Same(trans.RimColorValues[0], trans.values[trans.GetColorValueInfo("RimColor").indexR]);
            Assert.Same(trans.OutlineColorValues[0], trans.values[trans.GetColorValueInfo("OutlineColor").indexR]);
            Assert.Same(trans.EmissionColorValues[0], trans.values[trans.GetColorValueInfo("EmissionColor").indexR]);
            Assert.Same(trans.MatcapColorValues[0], trans.values[trans.GetColorValueInfo("MatcapColor").indexR]);
            Assert.Same(trans.MatcapMaskColorValues[0], trans.values[trans.GetColorValueInfo("MatcapMaskColor").indexR]);
            Assert.Same(trans.RimLightColorValues[0], trans.values[trans.GetColorValueInfo("RimLightColor").indexR]);
        }

        [Fact]
        public void PsylliumBarとBloomの追加色は型付きアクセサと一致する()
        {
            var bar = new TransformDataPsylliumBar();
            bar.Initialize("bar");
            Assert.Same(bar.color1aValues[0], bar.values[bar.GetColorValueInfo("color1a").indexR]);
            Assert.Same(bar.color2cValues[3], bar.values[bar.GetColorValueInfo("color2c").indexA]);

            var bloom = new TransformDataBloom();
            bloom.Initialize("bloom");
            bloom.flareColorB = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.Equal(bloom.flareColorB, bloom.GetColorValue("flareColorB"));
            bloom.flareColorD = new Color(0.5f, 0.6f, 0.7f, 0.8f);
            Assert.Equal(bloom.flareColorD, bloom.GetColorValue("flareColorD"));
        }
```

ファイル先頭の using に `using System.Collections.Generic;` があることを確認（Task 1 で追加済み）。

`AllTransforms()` は 47 の具象型を Unity ランタイム無しで生成する。既存テストで実績があるのは `TransformDataFaceSetting` / `TransformDataPsylliumArea` のみなので、静的初期化子が Unity API を触る型があれば `TypeInitializationException` で落ちる。その場合はその型だけ `AllTransforms()` の先頭で名前を挙げて除外し、理由をコメントに書く（除外リストを黙って増やさない）。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test ...`
Expected: `旧colorValuesと新マップは同じ値を指す` が `GetColorValueInfo` の null で FAIL（他 2 件も FAIL）

- [ ] **Step 3: 15 型へ色マップを追加する**

各ファイルのクラス内（`GetCustomValueInfoMap()` override がある型はその直前、無い型はコンストラクタ直後）に、一覧表の内容で追加する。すべて同じ形なので、代表として 4 パターンを示す。**他の型も表の値をそのまま当てはめる。**

**静的フィールドの宣言順に注意**: C# の `static readonly` 初期化はクラス内の宣言順に走る。`ColorValueInfoMap` が既定色として参照する既存フィールド（PsylliumBar の `defaultConfig`（`:44`）、Bloom の `InitialFlareColorB / C / D`（`:112-114`））より**後ろ**に置くこと。前に置くと PsylliumBar は `NullReferenceException`、Bloom は既定色が `(0,0,0,0)` に化けて Task 3 の Reset テストまで気づけない。

BGColor（Index 列挙が無く RGB のみ）:
```csharp
        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgb("色", 0, Color.black) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
```

Light（Index 列挙あり、RGB のみ）:
```csharp
        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgb("色", (int)Index.ColorR, Color.white) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
```

Paraffin（主色 + 副色。DistanceFog / Rimlight / StageLaser / StageLaserController / StageLightController も同型で名前と既定色だけ表に従う）:
```csharp
        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色1", (int)Index.ColorR, new Color(1f, 1f, 1f, 1f)) },
                { ColorKey.Sub, ColorValueInfo.Rgba("色2", (int)Index.SubColorR, new Color(1f, 1f, 1f, 0f)) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
```

ModelMaterial（主色 + 追加 7 色。キー名は既存の型付きアクセサ名に揃える）:
```csharp
        public const string ShadowColorKey = "ShadowColor";
        public const string RimColorKey = "RimColor";
        public const string OutlineColorKey = "OutlineColor";
        public const string EmissionColorKey = "EmissionColor";
        public const string MatcapColorKey = "MatcapColor";
        public const string MatcapMaskColorKey = "MatcapMaskColor";
        public const string RimLightColorKey = "RimLightColor";

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色", (int)Index.ColorR, Color.white) },
                { ShadowColorKey, ColorValueInfo.Rgba("影色", (int)Index.ShadowColorR, new Color(0f, 0f, 0f, 1f)) },
                { RimColorKey, ColorValueInfo.Rgba("リム色", (int)Index.RimColorR, new Color(0f, 0f, 0f, 1f)) },
                { OutlineColorKey, ColorValueInfo.Rgba("アウトライン", (int)Index.OutlineColorR, new Color(0f, 0f, 0f, 1f)) },
                { EmissionColorKey, ColorValueInfo.Rgba("発光色", (int)Index.EmissionColorR, Color.white) },
                { MatcapColorKey, ColorValueInfo.Rgba("マットキャップ色", (int)Index.MatcapColorR, Color.white) },
                { MatcapMaskColorKey, ColorValueInfo.Rgba("マットキャップマスク色", (int)Index.MatcapMaskColorR, Color.white) },
                { RimLightColorKey, ColorValueInfo.Rgba("リムライト色", (int)Index.RimLightColorR, Color.white) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
```

PsylliumBar と Bloom も同様にキー定数（`Color1aKey = "color1a"` … / `FlareColorBKey = "flareColorB"` …）を置いて表の値で定義する。Bloom の既定色は既存の `InitialFlareColorB / C / D` を使う。

- [ ] **Step 4: ビルドとテストを通す**

Run: 両構成ビルド → `dotnet test`
Expected: 0 エラー、全 PASS。`旧colorValuesと新マップは同じ値を指す` が通れば index の写し間違いは無い

- [ ] **Step 5: コミットする**

```
feat(timeline): 全 TransformData に色マップ (ColorValueInfoMap) を定義する
```

---

### Task 3: 旧色メンバーの撤去とタンジェント除外、UI の切替

**Files:**
- Modify: `Timeline/TransformData/ITransformData.cs`（6 メンバー削除）
- Modify: `Timeline/TransformData/TransformDataBase.cs`（6 virtual 削除）
- Modify: 15 型（override 削除、`tangentValues` 差し替え、成分カスタム値の削除、型付きアクセサの書き換え）
- Modify: `PostEffectRowDrawer.cs`、`StageLaserRowDrawer.cs`、`StageLightRowDrawer.cs`
- Modify: `KeyFrameInspector.cs`（`DrawColorRow` → 色マップの列挙）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/ColorValueInfoTests.cs`（突き合わせテストを不変条件テストへ置き換え）

**Interfaces:**
- Consumes: Task 1 / 2 の色 API と各型のキー定数
- Produces: `ITransformData` から `hasColor` / `hasSubColor` / `colorValues` / `subColorValues` / `initialColor` / `initialSubColor` が消える。色を持つ型の `tangentValues` は色成分を含まない

- [ ] **Step 1: 不変条件テストへ置き換える**

`ColorValueInfoTests.cs` から Task 2 の 3 テスト（`旧colorValuesと新マップは同じ値を指す` / `ModelMaterialの追加色は…` / `PsylliumBarとBloomの追加色は…`）を削除し、以下を追加する。`AllTransforms()` は残す。

```csharp
        private static IEnumerable<int> ColorIndices(ColorValueInfo info)
        {
            yield return info.indexR;
            yield return info.indexG;
            yield return info.indexB;
            if (info.hasAlpha)
            {
                yield return info.indexA;
            }
        }

        [Fact]
        public void 色indexは範囲内で色同士もカスタム値とも重複しない()
        {
            foreach (var trans in AllTransforms())
            {
                var used = new HashSet<int>();
                foreach (var info in trans.GetColorValueInfoMap().Values)
                {
                    foreach (var index in ColorIndices(info))
                    {
                        Assert.InRange(index, 0, trans.valueCount - 1);
                        Assert.True(used.Add(index), trans.type + " の色 index が重複: " + index);
                    }
                }
                foreach (var custom in trans.GetCustomValueInfoMap().Values)
                {
                    Assert.False(used.Contains(custom.index),
                        trans.type + " のカスタム値 " + custom.name + " が色 index と重複");
                }
            }
        }

        [Fact]
        public void tangentValuesは色成分を含まない()
        {
            foreach (var trans in AllTransforms())
            {
                var colorValues = new HashSet<ValueData>();
                foreach (var info in trans.GetColorValueInfoMap().Values)
                {
                    foreach (var index in ColorIndices(info))
                    {
                        colorValues.Add(trans.values[index]);
                    }
                }
                foreach (var value in trans.tangentValues)
                {
                    Assert.False(colorValues.Contains(value), trans.type + " の tangentValues に色成分が残っている");
                }
            }
        }

        [Fact]
        public void 全型でResetすると色は既定値になる()
        {
            foreach (var trans in AllTransforms())
            {
                foreach (var pair in trans.GetColorValueInfoMap())
                {
                    trans.SetColorValue(pair.Key, new Color(0.123f, 0.456f, 0.789f, 0.5f));
                }
                trans.Reset();
                foreach (var pair in trans.GetColorValueInfoMap())
                {
                    var expected = pair.Value.defaultValue;
                    if (!pair.Value.hasAlpha)
                    {
                        expected.a = 1f;
                    }
                    Assert.Equal(expected, trans.GetColorValue(pair.Key));
                }
            }
        }

        [Fact]
        public void ModelMaterialの型付きアクセサは色マップを読む()
        {
            var trans = new TransformDataModelMaterial();
            trans.Initialize("mm");
            trans.ShadowColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetColorValue(TransformDataModelMaterial.ShadowColorKey));
            trans.SetColorValue(TransformDataModelMaterial.RimLightColorKey, Color.red);
            Assert.Equal(Color.red, trans.RimLightColor);
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test ...`
Expected: `tangentValuesは色成分を含まない` が FAIL（Bloom / DistanceFog / Light / ModelMaterial / Paraffin / Rimlight / Text がまだ `values` 全体を返す）。`色indexは…重複しない` も ModelMaterial / PsylliumBar / Bloom の成分カスタム値で FAIL

- [ ] **Step 3: インターフェースと基底クラスから旧メンバーを消す**

`ITransformData.cs` から次の 6 行を削除する: `Color initialColor { get; }`、`Color initialSubColor { get; }`、`bool hasColor { get; }`、`bool hasSubColor { get; }`、`ValueData[] colorValues { get; }`、`ValueData[] subColorValues { get; }`。`Color color { get; set; }` と `Color subColor { get; set; }` は残す。

`TransformDataBase.cs` から次を削除する: `public virtual bool hasColor => false;`、`public virtual bool hasSubColor => false;`（`:185-186`）、`public virtual ValueData[] colorValues => new ValueData[0];`、`public virtual ValueData[] subColorValues => new ValueData[0];`（`:225-226`）、`public virtual Color initialColor => Color.white;`、`public virtual Color initialSubColor => Color.white;`（`:299-301`）。

- [ ] **Step 4: 15 型から override を消し、tangentValues を差し替える**

各型で削除するもの: `hasColor` / `hasSubColor` / `colorValues` / `subColorValues` / `initialColor` / `initialSubColor` の override（表の全型）。

`tangentValues => values;` を `tangentValues => valuesWithoutColors;` へ変える型: Bloom（`:82`）、DistanceFog（`:43`）、Light（`:74` 付近）、ModelMaterial（`:74`）、Paraffin（`:47`）、Rimlight（`:51`）、Text（`:50` 付近。複数行の場合も戻り値を `valuesWithoutColors` にする）。PngObject / StageLaser / StageLaserController / StageLight / StageLightController は `_tangentValues` リストに色を入れていないので変更しない。BGColor / BGGroundColor / PsylliumBar は `hasTangent` を持たないので変更しない。

- [ ] **Step 5: 成分カスタム値を削除し、型付きアクセサを色マップ経由にする**

ModelMaterial（`TransformDataModelMaterial.cs`）:
- `CustomValueInfoMap` から `"ShadowColor.r"` 〜 `"RimLightColor.a"` の 28 エントリを削除する（`_Shininess` などの数値エントリは残す）
- `ShadowColorValues` 〜 `RimLightColorValues` の 7 つの `ValueData[]` プロパティを削除する
- 型付きアクセサを書き換える（7 色すべて同じ形）:
```csharp
        public Color ShadowColor
        {
            get => GetColorValue(ShadowColorKey);
            set => SetColorValue(ShadowColorKey, value);
        }
```

PsylliumBar（`TransformDataPsylliumBar.cs`）:
- `CustomValueInfoMap` から `"color1aR"` 〜 `"color2cA"` の 24 エントリを削除する（`baseScale` 以降は残す）
- `color1aValues` 〜 `color2cValues` の 6 プロパティを削除する
- `color1a` 〜 `color2c` を `GetColorValue(Color1aKey)` / `SetColorValue(Color1aKey, value)` の形にする

Bloom（`TransformDataBloom.cs`）:
- `CustomValueInfoMap` から `"flareColorBR"` 〜 `"flareColorDA"` の 12 エントリ（`:329-340`）を削除する
- `private ValueData[] FlareColorValues(Index startIndex)`（`:521`）を削除する
- `flareColorB / C / D` を `GetColorValue(FlareColorBKey)` / `SetColorValue(FlareColorBKey, value)` の形にする。`Channel(...)` ヘルパーは他のエントリが使っていれば残す

- [ ] **Step 6: 現在値 UI の initialColor 参照を置き換える**

`PostEffectRowDrawer.cs` の 5 箇所（`:231`、`:324`、`:399`、`:603`、`:695`）:
- `defaultTrans.initialColor` → `defaultTrans.GetDefaultColorValue(MTEP.TransformDataBase.ColorKey.Main)`
- `defaultTrans.initialSubColor` → `defaultTrans.GetDefaultColorValue(MTEP.TransformDataBase.ColorKey.Sub)`

同じ置き換えを `StageLaserRowDrawer.cs`（`:153`、`:161`）と `StageLightRowDrawer.cs`（`:149`、`:157`）にも行う。これらのファイルは `using COM3D2.MotionTimelineEditor.Plugin;` を直接持っているので `TransformDataBase.ColorKey.Main` と書けばよい（`MTEP.` エイリアスが無ければ付けない）。

- [ ] **Step 7: KeyFrameInspector の色行を色マップの列挙にする**

`KeyFrameInspector.cs` の `DrawColorRow`（`:401-424`）を以下に置き換え、`DrawBoneBlock` の呼び出し名も `DrawColorRows` に合わせる。

```csharp
        /// <summary>
        /// 色マップの全色をカラーピッカー付きの行で描く (現在値 UI と同じ部品)。
        /// ColorPickerWindow はラベル文字列で編集対象を同定するため、
        /// 先頭に色名を置きつつフレーム番号とボーン名でキーフレームごとに一意にする。
        /// コールバックは ColorPickerWindow の描画中に呼ばれるが、bone.transform は
        /// 使い捨てではなく永続参照なので、そのまま書き換えてよい
        /// </summary>
        private void DrawColorRows(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetColorValueInfoMap())
            {
                var colorKey = pair.Key;
                var info = pair.Value;
                var fieldCache = view.GetColorFieldCache(
                    string.Format("{0} (F{1}) {2}", info.name, bone.frameNo, bone.name), info.hasAlpha);
                view.DrawColor(fieldCache, transform.GetColorValue(colorKey), info.defaultValue, newValue =>
                {
                    transform.SetColorValue(colorKey, newValue);
                    Apply(bone);
                });
            }
        }
```

- [ ] **Step 8: 残骸が無いか確認する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source && grep -rn "hasColor\b\|hasSubColor\|colorValues\|subColorValues\|initialColor\b\|initialSubColor\|ShadowColor\.r\|flareColorBR\|color1aR\|FlareColorValues\|ShadowColorValues\|color1aValues" --include=*.cs . | grep -v "ModelMaterial.cs:.*hasColorProperties\|ModelMaterial.cs:.*initialColors\|MaterialPropertyRowsDrawer.cs"
```
Expected: 0 件（`Timeline/ModelMaterial.cs` の `hasColorProperties` / `initialColors` と `MaterialPropertyRowsDrawer.cs` のローカル変数 `initialColor` は別物なので除外している）

- [ ] **Step 9: ビルドとテストを通す**

Run: 両構成ビルド → `dotnet test`
Expected: 0 エラー、全 PASS（`ColorValueInfoTests` は Task 1 の 8 件 + 本 Task の 4 件）。既存の `XmlRoundTripTests` / `MteCompatibilityTests` / `TangentUnificationTests` に退行が無いこと

- [ ] **Step 10: コミットする**

```
refactor(timeline): 色を ColorValueInfo に統一し hasColor 系メンバーとタンジェント対象から外す
```

---

### Task 4: キーフレーム詳細のラベル幅を広げる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs:207-262`（`DrawCustomValueFloat / Int / Bool`）
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`（定数追加、`DrawCustomValueRow` の呼び出し）

**Interfaces:**
- Produces: `DrawCustomValueFloat(this GUIView view, CustomValueInfo info, float value, Action<float> onChanged, Action onReset = null, float labelWidth = 40f)`、`DrawCustomValueInt(..., Action<int> onChanged, float labelWidth = 40f)`。`DrawCustomValueBool` は全幅トグルでラベル幅の概念が無いためシグネチャ変更なし

- [ ] **Step 1: Extensions に labelWidth を追加する**

`DrawCustomValueFloat` のシグネチャ末尾に `float labelWidth = 40f` を足し、`SliderOption` / `FloatFieldOption` の `labelWidth = 40,` を `labelWidth = labelWidth,` にする。`DrawCustomValueInt` も同様。`DrawCustomValueBool` は `DrawToggle(info.name, value, -1, 20, onChanged)` のままラベル幅の概念が無いため、引数だけ揃えて未使用にせず、`labelWidth` を受け取らない（シグネチャ変更なし）。

- [ ] **Step 2: KeyFrameInspector から 70 を渡す**

定数を追加する（`StrLabelWidth` の直後）:
```csharp
        /// <summary>カスタム値のラベル幅 (「アウトライン色」等が収まる幅。文字列値と揃える)</summary>
        private const float CustomLabelWidth = 70f;
```

`DrawCustomValueRow` の `DrawCustomValueInt` 呼び出しに `labelWidth: CustomLabelWidth` を、`DrawCustomValueFloat` 呼び出しの `onReset` の後に `CustomLabelWidth` を渡す。

- [ ] **Step 3: ビルドとテスト**

Run: 両構成ビルド → `dotnet test`
Expected: 0 エラー、全 PASS

- [ ] **Step 4: コミットする**

```
fix(inspector): キーフレーム詳細のカスタム値ラベルを 70px に広げる
```

---

### Task 5: 実機確認と変更履歴

**Files:**
- Modify: `CHANGELOG.md`（「## 未リリース」節）

- [ ] **Step 1: 実機で見た目を確認する**

ゲームを再起動して新 DLL を載せた後、MCP `com3d25-devbridge` で:
1. `ping` が通ることを確認
2. タイムラインで **モデルマテリアル** のキーフレームを選択し、`screenshot window="#8903352"`（Inspector）を撮る。影色・リム色・アウトライン・発光色・マットキャップ色・マットキャップマスク色・リムライト色が「ラベル + 色見本 + 編集」の行になっていること
3. **ポストエフェクト（ブルーム）** のキーフレームで、しきい値色・ﾌﾚｱ色A〜D の 5 行が色行になっていること
4. 「補間曲線」タブで色チャンネル（R/G/B/A）が並ばないこと
5. カスタム値のラベル（「アウトライン幅」等）が切れずに読めること

ゲームが起動できない場合はこの Step を省略し、最終報告で「実機未確認」と明記する。

- [ ] **Step 2: CHANGELOG に追記する**

`## 未リリース` の既存項目「Inspector のキーフレーム詳細で、数値・色・表示の行が…」の直後に追加する。

```markdown
- タイムラインのキーフレームで、マテリアルの影色やブルームのフレア色など全ての色をカラーピッカーで編集できるようになりました
  - 色の補間は線形に統一し、「補間曲線」タブとカーブエディタからは色のチャンネルを外しました
  - 保存形式は変わらないため、既存のタイムライン XML はそのまま読み込めます
```

- [ ] **Step 3: コミットする**

```
docs(changelog): 色値の統一とカラーピッカー編集を追記する
```

---

## Self-Review

- **Spec coverage**: §1 データモデル → Task 1（API）+ Task 2（マップ定義）+ Task 3 Step 3-5（旧メンバー撤去・成分カスタム値削除・型付きアクセサ）。§2 補間とタンジェント → Task 3 Step 4（`valuesWithoutColors`）。§3 UI → Task 3 Step 7（色行）+ Task 4（ラベル幅）。§4 XML 互換 → 変更なし、Task 3 Step 9 の既存テストで確認。§5 テスト → Task 1 / 2 / 3 の `ColorValueInfoTests`。§6 影響ファイル → 各 Task の Files。
- **Placeholder scan**: 15 型の追加コードは代表 4 パターン + 一覧表で全値を明示している。「同様に」で省いた PsylliumBar / Bloom のキー定数名は Task 3 Step 5 で `Color1aKey` / `FlareColorBKey` として使う名前と一致させた。
- **Type consistency**: `ColorValueInfo.Rgba / Rgb`（Task 1）を Task 2 で使用。`ColorKey.Main / Sub`（Task 1）を Task 2 / 3 / 6 で使用。`valuesWithoutColors`（Task 1、protected）を Task 3 Step 4 で使用。`ShadowColorKey` 等（Task 2）を Task 3 Step 5 / テストで使用。`DrawColorRows`（Task 3 Step 7）は `DrawBoneBlock` の呼び出し名変更を明記。
