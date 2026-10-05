# ステージライトの濃度を色から分離する Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ステージライトの色のアルファ（実体は光の柱の濃さ）を、色とは別の「濃度」パラメータ（0〜2、タンジェント補間）として編集・キー化できるようにする。

**Architecture:**
- 実行時の `StageLight` に `intensity`、`StageLightController` に `intensityMin` / `intensityMax` を足す。シェーダーは変えず、`StageLight.UpdateMaterial` が `_Color` / `_SubColor` のアルファへ `intensity` を詰める。`color.a` は描画に使わない
- キーは値を末尾に足す（個別ライト 24→25 値、コントローラー 37→39 値）。色は `ColorValueInfo.Rgb` にして、旧アルファの添字（11 / 15 / 19）は未使用にする
- 旧データの換算は「値数」で判定する（PNG の彩度・ライトの硬さと同じ作法）。`TransformDataStageLight(Controller).FromXml` が、旧値数のキーなら旧アルファを濃度へ移し、濃度のタンジェントを線形相当（正規化 1、非 smooth）にする。`TimelineData.CurrentVersion` は上げない
  - タイムライン XML・キーフレームテンプレート・クリップボードはすべて `FromXml` を通るので、この 1 か所で全経路が換算される
- ライブ演出の状態 DTO（Undo とシーンプリセット共用）には濃度フィールドを足し、既定値を「未記録」(-1) にする。適用時に未記録なら色のアルファから換算する

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成、Unity 側コピーは C# 4）、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** 本計画の冒頭（Goal / Architecture）と下記 Global Constraints。ユーザーとの合意事項（2026-10-05）:
- B案（独立パラメータ化）。値は末尾に新設し、旧データは旧アルファを濃度へ移す
- コントローラーは最小 / 最大の 2 本で、「一括色設定」のトグルに含める
- 濃度はタンジェント補間
- 範囲 0〜2、キーの既定値 0.3

## Global Constraints

- 表示名: 個別ライト「濃度」、コントローラー「最小濃度」/「最大濃度」。CustomValueInfo のキーは `intensity` / `intensityMin` / `intensityMax`
- 範囲 `min = 0f, max = 2f, step = 0.01f`、キーの既定値 `0.3f`
- 値の添字（保存形式。変えてはいけない）:
  - 個別ライト `TransformDataStageLight`: `Intensity = 24`、`valueCount = 25`、`LegacyValueCount = 24`、旧アルファ `ColorA = 11`
  - コントローラー `TransformDataStageLightController`: `IntensityMin = 37`、`IntensityMax = 38`、`valueCount = 39`、`LegacyValueCount = 37`、旧アルファ `ColorA = 15` / `SubColorA = 19`
- 実行時コンポーネントの既定値は今の見た目を保つ: `StageLight._intensity = 1f`（今の `_color = Color.white` の A）、`StageLightController.intensityMin/Max = 0.3f`（今の `colorMin/Max` の A）。`colorMin/Max` は `Color.white` にする
- 色のキー既定値は `Color.white`（RGB のみ）
- 濃度の負値: `StageLight.intensity` の setter で 0 未満を 0 にする。コントローラーの `intensityMin/Max` はクランプしないが、DTO へ記録するときに 0 で止める（-1 の「未記録」と区別するため）。上限 2 はスライダーだけの範囲で、実行時はクランプしない
- `TimelineData.CurrentVersion`（38）は上げない。`ScenePresetData.CurrentVersion` は 39 → 40
- `UnityProject/Assets/Scripts/StageLight.cs` / `StageLightController.cs` は本体と同じ変更を入れる（Unity 5.6 / C# 4: 式形式メンバー・`?.`・文字列補間を使わない）。`UnityProject` の `StageLightController.cs` にはコメント 2 行の差があり、それは残す
- コードのコメントとエラーログは日本語
- **2 構成のビルド**: `COM3D2`（.NET 3.5）と `COM3D25`（.NET 4.7.1）の両方を通す
- `debug.bat` / `deploy.bat` は実行しない
- テストで Unity のネイティブ呼び出し（`new GameObject`、`AddComponent`、`Quaternion.Euler` など）を使わない。`Color` 構造体の生成・比較は可
- 新しいファイルはテストだけ（テスト側は SDK 形式で csproj 追記不要）。本体の csproj は変更しない
- `MTEUtils/` はサブモジュール。今回は変更しない

## Review Focus

1. **旧タイムライン（MTE / 旧 SE）のステージライト**: 旧アルファ 0.3 のキーを読むと濃度 0.3 になり、見た目が変わらないこと。キー間でアルファが変わっていた区間は、従来どおり（実用上）線形に変化すること（タンジェント 0 のままだと S 字になる）→ Task 2 の補間結果テスト、Task 3 のタンジェント値テストで固定
2. **一括色設定 ON のコントローラー**: 配下ライトへ濃度が最小〜最大で配分されること。旧データの最小色 A / 最大色 A がそれぞれ最小濃度 / 最大濃度になること → Task 3 のテスト、Task 7 の実機確認
3. **旧シーンプリセット / 旧 Undo DTO**: `intensity` 要素の無いプリセットを適用すると、色のアルファが濃度になり、色のアルファは 1 になること → Task 5 のテスト
4. **新形式キーの往復**: 濃度 1.75（1 を超える値）が ToXml → FromXml で保たれ、旧形式扱いされないこと → Task 2 / 3 のテスト
5. **キーのリセット**: リセットしたキーの濃度が 0.3、色が白になること → Task 2 / 3 のテスト

---

## File Structure

| ファイル | 役割 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs` | `intensity` を持ち、マテリアルの `_Color.a` へ渡す |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs` | `intensityMin/Max` を持ち、一括色設定で配分する |
| Modify `UnityProject/Assets/Scripts/StageLight.cs` / `StageLightController.cs` | 上記と同じ（Unity 側のプレビュー用コピー） |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLight.cs` | 濃度の値・旧キー換算 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLightController.cs` | 最小 / 最大濃度の値・旧キー換算 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs` | 濃度の再生（Init はコピー、Update はエルミート補間） |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs` | DTO に濃度を足し、旧 DTO を換算する `StageLightIntensityCompat` |
| Modify `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `CurrentVersion` 40 とコメント |
| Modify `source/COM3D2.SceneEditor.Plugin/StageLightRowDrawer.cs` | 色欄を RGB にし、濃度スライダーを足す |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityKeyTests.cs` | キーの添字・既定値・旧キー換算・往復 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityCompatTests.cs` | DTO 換算・プリセット往復 |
| Modify `docs-site/timeline/layers-effect.md`、`docs-site/timeline/compatibility.md` | 利用者向けの説明 |
| Modify `W:\COM3D2_5\work\CLAUDE.md` | 「タイムライン XML の互換方向」へ 1 項目追加（git 管理外なのでコミットしない） |

## ビルド・テストのコマンド

Git Bash から実行する。順番は「COM3D2 → COM3D25 → dotnet test」。COM3D2 構成のビルドが `bin/Debug/COM3D25/` を消すため、この順でないとテストが古い DLL を見る。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

以下では、これを「**ビルド＆テスト**」と呼ぶ。特定のテストだけ回すときは末尾を `dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~StageLightIntensity"` に置き換える。

テストはビルド済みのプラグイン DLL を参照するため、「失敗するテストを書く」段階では、未定義のメンバーによるコンパイルエラーが「失敗」にあたる。

作業ブランチ: `feat/stage-light-intensity`（main から切る。worktree は使わない）

---

### Task 1: 実行時コンポーネントに濃度を持たせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs`
- Modify: `UnityProject/Assets/Scripts/StageLight.cs`、`UnityProject/Assets/Scripts/StageLightController.cs`

**Interfaces:**
- Produces: `StageLight.intensity`（float、get/set、変更でマテリアル更新）、`StageLightController.intensityMin` / `intensityMax`（public float フィールド）

MonoBehaviour なのでユニットテストは書かない（ネイティブ呼び出しが要る）。ビルドで確認し、見た目は Task 7 の実機確認で見る。

- [ ] **Step 1: ブランチを切る**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin && git switch -c feat/stage-light-intensity
```

- [ ] **Step 2: `StageLight` に `intensity` を足す**

`_color` プロパティ（91-104 行）の直後に追加する。C# 4 互換の書き方にする（Unity 側へそのままコピーするため）。

```csharp
        /// <summary>
        /// 光の柱の濃さ (シェーダーの密度に掛かる倍率)。
        /// 以前は color のアルファがこの役目だったが、色と分けて編集できるよう独立させた。color.a は描画に使わない
        /// </summary>
        [SerializeField]
        [Range(0f, 2f)]
        private float _intensity = 1f;
        public float intensity
        {
            get
            {
                return _intensity;
            }
            set
            {
                // タンジェント補間のオーバーシュートで負になりうる。負の密度はシェーダーで破綻するため 0 で止める
                value = Mathf.Max(0f, value);
                if (_intensity == value) return;
                _intensity = value;
                _requestedMaterialUpdate = true;
            }
        }
```

`CopyFrom`（418-434 行）の `color = other.color;` の次に `intensity = other.intensity;` を足す。

`UpdateMaterial`（568-587 行）の色の 2 行を差し替える:

```csharp
                // シェーダーは _Color.a を密度の倍率として読む。色のアルファではなく濃度を詰める
                var shaderColor = new Color(color.r, color.g, color.b, intensity);
                material.SetColor(Uniforms._Color, shaderColor);
                material.SetColor(Uniforms._SubColor, shaderColor);
```

- [ ] **Step 3: `StageLightController` に最小 / 最大濃度を足す**

63-66 行の一括色設定を次へ差し替える:

```csharp
        [Header("一括色設定")]
        public bool autoColor = false;
        public Color colorMin = Color.white;
        public Color colorMax = Color.white;
        [Range(0f, 2f)]
        public float intensityMin = 0.3f;
        [Range(0f, 2f)]
        public float intensityMax = 0.3f;
```

`UpdateLights` の autoColor ブロック（192-195 行）を差し替える:

```csharp
                if (autoColor)
                {
                    light.color = Color.Lerp(colorMin, colorMax, t);
                    light.intensity = Mathf.Lerp(intensityMin, intensityMax, t);
                }
```

- [ ] **Step 4: Unity 側のコピーへ同じ変更を入れる**

`UnityProject/Assets/Scripts/StageLight.cs` に Step 2 と同じ 3 か所、`UnityProject/Assets/Scripts/StageLightController.cs` に Step 3 と同じ 2 か所を入れる。終わったら差分を確かめる:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
diff <(sed 's/\r//' source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs) <(sed 's/\r//' UnityProject/Assets/Scripts/StageLight.cs) && echo SAME
diff <(sed 's/\r//' source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs) <(sed 's/\r//' UnityProject/Assets/Scripts/StageLightController.cs)
```

Expected: 1 つ目は `SAME`。2 つ目は既存の差（`// rotationMin / rotationMax は振れ幅なので…` のコメント 2 行）だけ。

- [ ] **Step 5: ビルド＆テスト**

Expected: 2 構成ともビルド成功、既存テストすべて PASS。

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLightController.cs UnityProject/Assets/Scripts/StageLight.cs UnityProject/Assets/Scripts/StageLightController.cs
git commit -m "feat(stage-light): ライトに濃度を持たせて色のアルファから切り離す"
```

---

### Task 2: 個別ライトのキーに濃度を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLight.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityKeyTests.cs`

**Interfaces:**
- Consumes: `StageLight.intensity`（Task 1）
- Produces: `TransformDataStageLight.Index.Intensity`（= 24）、`TransformDataStageLight.LegacyValueCount`（= 24）、`intensity`（float プロパティ）、`intensityValue`（ValueData）、`intensityInfo`（CustomValueInfo）

- [ ] **Step 1: 失敗するテストを書く**

`StageLightIntensityKeyTests.cs` を作る（Task 3 でコントローラーのテストを同じファイルへ足す）。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ステージライトの濃度 (旧: 色のアルファ) を独立した値にした保存形式と、旧キーの換算を固定する。
    /// 換算が外れると旧タイムラインの光の柱が消える (濃度 0) か、アルファの変化が S 字補間になる
    /// </summary>
    public class StageLightIntensityKeyTests
    {
        // 値の並びは保存形式。実装の Index ではなくベタ書きで固定する
        private const int LightColorAIndex = 11;
        private const int LightIntensityIndex = 24;
        private const int LightLegacyValueCount = 24;

        private static TransformDataStageLight CreateLightKey()
        {
            var trans = new TransformDataStageLight();
            trans.Initialize("StageLight (0, 0)");
            return trans;
        }

        private static TransformXml CreateXml(string name, TransformType type, float[] values)
        {
            return new TransformXml
            {
                name = name,
                type = type,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 個別ライト_濃度はindex24で値数は25()
        {
            Assert.Equal(LightIntensityIndex, (int)TransformDataStageLight.Index.Intensity);
            Assert.Equal(LightLegacyValueCount, TransformDataStageLight.LegacyValueCount);
            Assert.Equal(25, CreateLightKey().valueCount);
            Assert.True(CreateLightKey().GetCustomValueInfoMap().ContainsKey("intensity"));
        }

        [Fact]
        public void 個別ライト_色はアルファを持たない()
        {
            var info = CreateLightKey().GetColorValueInfoMap()[TransformDataBase.ColorKey.Main];

            Assert.False(info.hasAlpha);
        }

        [Fact]
        public void 個別ライト_濃度はタンジェント補間の対象()
        {
            var trans = CreateLightKey();

            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityValue));
        }

        [Fact]
        public void 個別ライト_リセットしたキーは濃度03で色は白()
        {
            var trans = CreateLightKey();
            trans.Reset();

            Assert.Equal(0.3f, trans.intensity);
            Assert.Equal(Color.white, trans.color);
        }

        [Fact]
        public void 個別ライト_旧キーは色のアルファを濃度へ移す()
        {
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = 0.45f;

            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));

            Assert.Equal(0.45f, trans.intensity);
        }

        [Fact]
        public void 個別ライト_旧キーの濃度は線形補間相当のタンジェントになる()
        {
            // 旧アルファは線形補間だった。タンジェント 0 のままだとエルミート補間が S 字になる
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = 0.45f;

            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));

            Assert.Equal(1f, trans.intensityValue.inTangent.normalizedValue);
            Assert.Equal(1f, trans.intensityValue.outTangent.normalizedValue);
            Assert.False(trans.intensityValue.inTangent.isSmooth);
            Assert.False(trans.intensityValue.outTangent.isSmooth);
        }

        private static TransformDataStageLight CreateLegacyLightKey(float alpha)
        {
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = alpha;
            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));
            return trans;
        }

        [Fact]
        public void 個別ライト_旧キー同士の濃度は区間内で線形に変化する()
        {
            // 時刻 0〜3 の旧キー 4 つ。区間 1→2 (0.8→0.9) の 1/4 地点は線形なら 0.825。
            // タンジェント 0 のままだと 0.8 + 0.1 * 0.15625 = 0.8156 になる
            var keys = new[]
            {
                CreateLegacyLightKey(0.3f),
                CreateLegacyLightKey(0.8f),
                CreateLegacyLightKey(0.9f),
                CreateLegacyLightKey(1.5f),
            };
            keys[1].UpdateTangent(keys[0], keys[2], 0f, 1f, 2f);
            keys[2].UpdateTangent(keys[1], keys[3], 1f, 2f, 3f);

            var value = PluginUtils.HermiteValue(1f, 2f, keys[1].intensityValue, keys[2].intensityValue, 0.25f);

            Assert.Equal(0.825f, value, 4);
        }

        [Fact]
        public void 個別ライト_アルファまで値の無い旧キーは既定の濃度で読む()
        {
            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, new float[LightColorAIndex]));

            Assert.Equal(0.3f, trans.intensity);
        }

        [Fact]
        public void 個別ライト_濃度は1を超えても往復で保たれる()
        {
            var trans = CreateLightKey();
            trans.intensity = 1.75f;

            var restored = CreateLightKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(1.75f, restored.intensity);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

ビルド＆テスト（`--filter "FullyQualifiedName~StageLightIntensityKeyTests"`）。
Expected: `Index.Intensity` / `LegacyValueCount` / `intensity` / `intensityValue` が無いためテストプロジェクトのコンパイルエラー。

- [ ] **Step 3: 実装する**

`TransformDataStageLight.cs`:

1. `Index` の `ZTest = 23` の後へ `Intensity = 24` を足す（`ZTest = 23,` とカンマも直す）
2. `valueCount` を差し替え、`LegacyValueCount` を足す:

```csharp
        /// <summary>濃度 (index 24) を持たない旧キー (MTE・旧 SE) の値数。旧キーは色のアルファが濃度だった</summary>
        public const int LegacyValueCount = 24;

        public override int valueCount => (int)Index.Intensity + 1;
```

3. `tangentValues` の追加リストに濃度を足す:

```csharp
                    _tangentValues.AddRange(new ValueData[] { 
                        values[(int)Index.SpotAngle], 
                        values[(int)Index.SpotRange],
                        values[(int)Index.Intensity],
                    });
```

4. `CustomValueInfoMap` の先頭（`spotAngle` の前）へ足す:

```csharp
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "濃度",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.3f,
                }
            },
```

5. 色を RGB にする:

```csharp
                { ColorKey.Main, ColorValueInfo.Rgb("色", (int)Index.ColorR, Color.white) },
```

`Index.ColorA = 11` は残し、上に `// 旧キーの濃度。今は使わない (換算元として FromXml だけが読む)` と書く。

6. アクセサを足す（既存の並びに合わせ、各グループの先頭へ）:

```csharp
        public ValueData intensityValue => values[(int)Index.Intensity];
```
```csharp
        public CustomValueInfo intensityInfo => CustomValueInfoMap["intensity"];
```
```csharp
        public float intensity
        {
            get => intensityValue.value;
            set => intensityValue.value = value;
        }
```

7. `FromStageLight` の `color = light.color;` の次に `intensity = light.intensity;` を足す
8. クラス末尾に `FromXml` を足す:

```csharp
        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                // 旧キーは色のアルファが濃度だった。
                // アルファも無いキー (v35 の移行後は通常起きない) は、0 埋めで光の柱が消えるのを避けて既定値にする
                intensity = xml.values.Length > (int)Index.ColorA
                    ? xml.values[(int)Index.ColorA]
                    : intensityInfo.defaultValue;
                SetLinearTangent(intensityValue);
            }
        }

        /// <summary>
        /// 旧アルファは線形補間だった。正規化タンジェント 1 (区間の傾きそのまま) にして実用上同じ変化にする。
        /// タンジェント 0 のままだとエルミート補間で S 字になる。
        /// 変化の無い区間の隣では UpdateTangent が傾き 0.01 で代用するため、ごく小さなこぶが出る
        /// </summary>
        public static void SetLinearTangent(ValueData value)
        {
            value.inTangent.normalizedValue = 1f;
            value.inTangent.isSmooth = false;
            value.outTangent.normalizedValue = 1f;
            value.outTangent.isSmooth = false;
        }
```

- [ ] **Step 4: テストが通ることを確かめる**

ビルド＆テスト（全件）。
Expected: `StageLightIntensityKeyTests` 全件 PASS。既存テストも全件 PASS。ステージライトの回転移行テスト（`TimelineXmlRotationMigrationTests`）は XML の値を直接見るので影響しない。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLight.cs source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityKeyTests.cs
git commit -m "feat(stage-light): 個別ライトのキーに濃度を足し旧キーの色のアルファを移す"
```

---

### Task 3: コントローラーのキーに最小 / 最大濃度を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLightController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityKeyTests.cs`

**Interfaces:**
- Consumes: `StageLightController.intensityMin/Max`（Task 1）、`TransformDataStageLight.SetLinearTangent(ValueData)`（Task 2）
- Produces: `TransformDataStageLightController.Index.IntensityMin`（= 37）/ `IntensityMax`（= 38）、`LegacyValueCount`（= 37）、`intensityMin` / `intensityMax`（float）、`intensityMinValue` / `intensityMaxValue`（ValueData）、`intensityMinInfo` / `intensityMaxInfo`（CustomValueInfo）

- [ ] **Step 1: 失敗するテストを書く**

`StageLightIntensityKeyTests` のクラス内、末尾へ足す:

```csharp
        private const int ControllerColorAIndex = 15;
        private const int ControllerSubColorAIndex = 19;
        private const int ControllerIntensityMinIndex = 37;
        private const int ControllerIntensityMaxIndex = 38;
        private const int ControllerLegacyValueCount = 37;

        private static TransformDataStageLightController CreateControllerKey()
        {
            var trans = new TransformDataStageLightController();
            trans.Initialize("StageLightController (0)");
            return trans;
        }

        [Fact]
        public void コントローラー_最小最大濃度はindex37と38で値数は39()
        {
            Assert.Equal(ControllerIntensityMinIndex, (int)TransformDataStageLightController.Index.IntensityMin);
            Assert.Equal(ControllerIntensityMaxIndex, (int)TransformDataStageLightController.Index.IntensityMax);
            Assert.Equal(ControllerLegacyValueCount, TransformDataStageLightController.LegacyValueCount);
            Assert.Equal(39, CreateControllerKey().valueCount);
            var map = CreateControllerKey().GetCustomValueInfoMap();
            Assert.True(map.ContainsKey("intensityMin"));
            Assert.True(map.ContainsKey("intensityMax"));
        }

        [Fact]
        public void コントローラー_最小色と最大色はアルファを持たない()
        {
            var map = CreateControllerKey().GetColorValueInfoMap();

            Assert.False(map[TransformDataBase.ColorKey.Main].hasAlpha);
            Assert.False(map[TransformDataBase.ColorKey.Sub].hasAlpha);
        }

        [Fact]
        public void コントローラー_最小最大濃度はタンジェント補間の対象()
        {
            var trans = CreateControllerKey();

            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityMinValue));
            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityMaxValue));
        }

        [Fact]
        public void コントローラー_リセットしたキーは濃度03で色は白()
        {
            var trans = CreateControllerKey();
            trans.Reset();

            Assert.Equal(0.3f, trans.intensityMin);
            Assert.Equal(0.3f, trans.intensityMax);
            Assert.Equal(Color.white, trans.color);
            Assert.Equal(Color.white, trans.subColor);
        }

        [Fact]
        public void コントローラー_旧キーは最小色と最大色のアルファを濃度へ移す()
        {
            var values = new float[ControllerLegacyValueCount];
            values[ControllerColorAIndex] = 0.2f;
            values[ControllerSubColorAIndex] = 0.8f;

            var trans = CreateControllerKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLightController, values));

            Assert.Equal(0.2f, trans.intensityMin);
            Assert.Equal(0.8f, trans.intensityMax);
            Assert.Equal(1f, trans.intensityMinValue.outTangent.normalizedValue);
            Assert.False(trans.intensityMaxValue.inTangent.isSmooth);
        }

        [Fact]
        public void コントローラー_濃度は往復で保たれる()
        {
            var trans = CreateControllerKey();
            trans.intensityMin = 0.1f;
            trans.intensityMax = 1.9f;

            var restored = CreateControllerKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(0.1f, restored.intensityMin);
            Assert.Equal(1.9f, restored.intensityMax);
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

ビルド＆テスト（`--filter "FullyQualifiedName~StageLightIntensityKeyTests"`）。
Expected: コントローラー側のメンバーが無いためコンパイルエラー。

- [ ] **Step 3: 実装する**

`TransformDataStageLightController.cs`:

1. `Index` の `ZTest = 36` の後へ `IntensityMin = 37, IntensityMax = 38` を足す
2. `valueCount` を差し替え、`LegacyValueCount` を足す:

```csharp
        /// <summary>最小 / 最大濃度 (index 37 / 38) を持たない旧キーの値数。旧キーは最小色 / 最大色のアルファが濃度だった</summary>
        public const int LegacyValueCount = 37;

        public override int valueCount => (int)Index.IntensityMax + 1;
```

3. `tangentValues` の追加リストを差し替える:

```csharp
                    _tangentValues.AddRange(new ValueData[] { 
                        values[(int)Index.SpotAngle], 
                        values[(int)Index.SpotRange],
                        values[(int)Index.IntensityMin],
                        values[(int)Index.IntensityMax],
                    });
```

4. `CustomValueInfoMap` の先頭（`spotAngle` の前）へ足す:

```csharp
            {
                "intensityMin", new CustomValueInfo
                {
                    index = (int)Index.IntensityMin,
                    name = "最小濃度",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.3f,
                }
            },
            {
                "intensityMax", new CustomValueInfo
                {
                    index = (int)Index.IntensityMax,
                    name = "最大濃度",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.3f,
                }
            },
```

5. 色を RGB にする:

```csharp
                { ColorKey.Main, ColorValueInfo.Rgb("最小色", (int)Index.ColorR, Color.white) },
                { ColorKey.Sub, ColorValueInfo.Rgb("最大色", (int)Index.SubColorR, Color.white) },
```

`Index.ColorA` / `SubColorA` は残し、それぞれ上に `// 旧キーの濃度。今は使わない (換算元として FromXml だけが読む)` と書く。

6. アクセサを足す:

```csharp
        public ValueData intensityMinValue => values[(int)Index.IntensityMin];
        public ValueData intensityMaxValue => values[(int)Index.IntensityMax];
```
```csharp
        public CustomValueInfo intensityMinInfo => CustomValueInfoMap["intensityMin"];
        public CustomValueInfo intensityMaxInfo => CustomValueInfoMap["intensityMax"];
```
```csharp
        public float intensityMin
        {
            get => intensityMinValue.value;
            set => intensityMinValue.value = value;
        }

        public float intensityMax
        {
            get => intensityMaxValue.value;
            set => intensityMaxValue.value = value;
        }
```

7. `FromStageLightController` の `subColor = controller.colorMax;` の次に足す:

```csharp
            intensityMin = controller.intensityMin;
            intensityMax = controller.intensityMax;
```

8. クラス末尾に `FromXml` を足す:

```csharp
        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                // 旧キーは最小色 / 最大色のアルファが濃度だった
                intensityMin = ReadLegacyAlpha(xml.values, (int)Index.ColorA, intensityMinInfo.defaultValue);
                intensityMax = ReadLegacyAlpha(xml.values, (int)Index.SubColorA, intensityMaxInfo.defaultValue);
                TransformDataStageLight.SetLinearTangent(intensityMinValue);
                TransformDataStageLight.SetLinearTangent(intensityMaxValue);
            }
        }

        private static float ReadLegacyAlpha(float[] values, int index, float defaultValue)
        {
            return values.Length > index ? values[index] : defaultValue;
        }
```

- [ ] **Step 4: テストが通ることを確かめる**

ビルド＆テスト（全件）。
Expected: 全件 PASS。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataStageLightController.cs source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityKeyTests.cs
git commit -m "feat(stage-light): コントローラーのキーに最小最大濃度を足し旧キーの色のアルファを移す"
```

---

### Task 4: タイムライン再生で濃度を反映する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs`

**Interfaces:**
- Consumes: Task 1〜3 の `intensity` 系メンバー、`PluginUtils.HermiteValue(float t0, float t1, ValueData start, ValueData end, float t)`（既存）

レイヤーは `stageLightManager` の実体を書き換えるのでユニットテストは書かない。ビルドと Task 7 の実機確認で見る。

- [ ] **Step 1: 個別ライトの Init（207-210 行）**

```csharp
            if (!controller.autoColor)
            {
                light.color = start.color;
                light.intensity = start.intensity;
            }
```

- [ ] **Step 2: 個別ライトの Update（276-279 行）**

```csharp
            if (!controller.autoColor)
            {
                light.color = Color.Lerp(start.color, end.color, t);
                light.intensity = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityValue,
                    end.intensityValue,
                    t);
            }
```

- [ ] **Step 3: コントローラーの Init（315-316 行の後）**

```csharp
            controller.colorMin = start.color;
            controller.colorMax = start.subColor;
            controller.intensityMin = start.intensityMin;
            controller.intensityMax = start.intensityMax;
```

- [ ] **Step 4: コントローラーの Update（394-398 行）**

```csharp
            if (controller.autoColor)
            {
                controller.colorMin = Color.Lerp(start.color, end.color, t);
                controller.colorMax = Color.Lerp(start.subColor, end.subColor, t);
                controller.intensityMin = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityMinValue,
                    end.intensityMinValue,
                    t);
                controller.intensityMax = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityMaxValue,
                    end.intensityMaxValue,
                    t);
            }
```

- [ ] **Step 5: ビルド＆テスト**

Expected: 2 構成ともビルド成功、全件 PASS。

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/StageLightTimelineLayer.cs
git commit -m "feat(stage-light): タイムライン再生で濃度をタンジェント補間して反映する"
```

---

### Task 5: Undo / シーンプリセットの状態に濃度を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityCompatTests.cs`

**Interfaces:**
- Consumes: `StageLight.intensity`、`StageLightController.intensityMin/Max`（Task 1）
- Produces: `LiveEffectStageLightState.intensity`、`LiveEffectStageLightControllerState.intensityMin/intensityMax`（float、既定 `StageLightIntensityCompat.Unrecorded`）、`static class StageLightIntensityCompat { const float Unrecorded = -1f; static void Resolve(Color storedColor, float storedIntensity, out Color color, out float intensity); }`

- [ ] **Step 1: 失敗するテストを書く**

`StageLightIntensityCompatTests.cs` を作る。プリセットの往復は既存の `ScenePresetEffectsTests` と同じく XmlSerializer で確かめる（同ファイルの `RoundTrip` は private なので、ここでは最小の往復を自前で書く）。

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライブ演出の状態 (Undo・シーンプリセット) のステージライト濃度を固定する。
    /// 濃度の無い旧プリセットは色のアルファが濃度だったので、適用時に換算しないと光の柱が既定の濃さに変わる
    /// </summary>
    public class StageLightIntensityCompatTests
    {
        [Fact]
        public void 未記録の濃度は色のアルファから換算し色のアルファは1にする()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(
                new Color(0.2f, 0.4f, 0.6f, 0.35f), StageLightIntensityCompat.Unrecorded,
                out color, out intensity);

            Assert.Equal(0.35f, intensity);
            Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 1f), color);
        }

        [Fact]
        public void 記録済みの濃度はそのまま使い色も触らない()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(new Color(0.2f, 0.4f, 0.6f, 0.35f), 1.5f, out color, out intensity);

            Assert.Equal(1.5f, intensity);
            Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 0.35f), color);
        }

        [Fact]
        public void 濃度0は記録済みとして扱う()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(Color.white, 0f, out color, out intensity);

            Assert.Equal(0f, intensity);
        }

        [Fact]
        public void 新しく作った状態の濃度は未記録()
        {
            Assert.Equal(StageLightIntensityCompat.Unrecorded, new LiveEffectStageLightState().intensity);
            var controller = new LiveEffectStageLightControllerState();
            Assert.Equal(StageLightIntensityCompat.Unrecorded, controller.intensityMin);
            Assert.Equal(StageLightIntensityCompat.Unrecorded, controller.intensityMax);
        }

        private static ScenePresetData RoundTrip(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                using (var reader = new StringReader(writer.ToString()))
                {
                    return (ScenePresetData)serializer.Deserialize(reader);
                }
            }
        }

        [Fact]
        public void プリセットは濃度を往復で保つ()
        {
            var data = new ScenePresetData { effects = new ScenePresetEffects() };
            data.effects.liveEffect = new LiveEffectState();
            var controller = new LiveEffectStageLightControllerState { intensityMin = 0.1f, intensityMax = 1.9f };
            controller.lights.Add(new LiveEffectStageLightState { intensity = 1.25f });
            data.effects.liveEffect.stageLightControllers.Add(controller);

            var restored = Assert.Single(RoundTrip(data).effects.liveEffect.stageLightControllers);

            Assert.Equal(0.1f, restored.intensityMin);
            Assert.Equal(1.9f, restored.intensityMax);
            Assert.Equal(1.25f, Assert.Single(restored.lights).intensity);
        }

        [Fact]
        public void 濃度要素の無い旧プリセットは未記録として読む()
        {
            // v39 以前のプリセットには intensity 要素が無い
            const string xml =
                "<LiveEffectStageLightState><color><r>1</r><g>1</g><b>1</b><a>0.4</a></color></LiveEffectStageLightState>";
            var serializer = new XmlSerializer(typeof(LiveEffectStageLightState));
            using (var reader = new StringReader(xml))
            {
                var state = (LiveEffectStageLightState)serializer.Deserialize(reader);

                Assert.Equal(StageLightIntensityCompat.Unrecorded, state.intensity);
                Assert.Equal(0.4f, state.color.a);
            }
        }

    }
}
```

既存テスト 2 件が `ScenePresetData.CurrentVersion` を 39 で固定している。どちらも 40 へ直す（現行バージョンの確認はこの 2 件に任せ、新しいテストは足さない）:
- `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightCharacterShadowTests.cs:57`
- `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs:149`

```csharp
            Assert.Equal(40, ScenePresetData.CurrentVersion);
```

- [ ] **Step 2: テストが失敗することを確かめる**

ビルド＆テスト（`--filter "FullyQualifiedName~StageLightIntensityCompatTests"`）。
Expected: `StageLightIntensityCompat` / `intensity` 系フィールドが無いためコンパイルエラー。

- [ ] **Step 3: DTO と換算を実装する**

`LiveEffectSnapshot.cs`:

1. `LiveEffectStageLightState` の `color` の次へ足す:

```csharp
        /// <summary>濃度。未記録 (旧プリセット) は色のアルファから換算する (StageLightIntensityCompat)</summary>
        public float intensity = StageLightIntensityCompat.Unrecorded;
```

2. `LiveEffectStageLightControllerState` の `colorMax` の次へ足す:

```csharp
        /// <summary>最小 / 最大濃度。未記録 (旧プリセット) は最小色 / 最大色のアルファから換算する</summary>
        public float intensityMin = StageLightIntensityCompat.Unrecorded;
        public float intensityMax = StageLightIntensityCompat.Unrecorded;
```

3. `LiveEffectStageLightControllerState` の直後に足す:

```csharp
    /// <summary>
    /// 濃度を持たない旧データ (v39 以前のシーンプリセット) の換算。
    /// 当時は色のアルファが濃度だったので、それを濃度へ移し、色のアルファは 1 にそろえる
    /// </summary>
    public static class StageLightIntensityCompat
    {
        /// <summary>濃度が記録されていないことを表す値。濃度は 0 以上なので負値で区別する</summary>
        public const float Unrecorded = -1f;

        public static void Resolve(Color storedColor, float storedIntensity, out Color color, out float intensity)
        {
            if (storedIntensity < 0f)
            {
                intensity = storedColor.a;
                color = new Color(storedColor.r, storedColor.g, storedColor.b, 1f);
                return;
            }
            intensity = storedIntensity;
            color = storedColor;
        }
    }
```

4. キャプチャ（コントローラー DTO の初期化子、179-181 行付近）に足す。コントローラーの値はクランプしていないフィールドで、再生中のオーバーシュートで負になりうる。負のまま記録すると適用時に「未記録」と誤判定されるため、記録時に 0 で止める（ライトは `StageLight.intensity` の setter が 0 で止めている）:

```csharp
                    // 負値は未記録 (Unrecorded) と区別できないため 0 で止めて記録する
                    intensityMin = Mathf.Max(0f, controller.intensityMin),
                    intensityMax = Mathf.Max(0f, controller.intensityMax),
```

ライト DTO の初期化子（197 行付近、`color = light.color,` の次）に `intensity = light.intensity,` を足す。

5. 適用（340-342 行付近）の色 2 行を差し替える:

```csharp
                Color colorMin, colorMax;
                float intensityMin, intensityMax;
                StageLightIntensityCompat.Resolve(dto.colorMin, dto.intensityMin, out colorMin, out intensityMin);
                StageLightIntensityCompat.Resolve(dto.colorMax, dto.intensityMax, out colorMax, out intensityMax);
                controller.colorMin = colorMin;
                controller.colorMax = colorMax;
                controller.intensityMin = intensityMin;
                controller.intensityMax = intensityMax;
```

ライトの適用（357 行付近の `light.color = lightDto.color;`）を差し替える:

```csharp
                    Color lightColor;
                    float lightIntensity;
                    StageLightIntensityCompat.Resolve(lightDto.color, lightDto.intensity, out lightColor, out lightIntensity);
                    light.color = lightColor;
                    light.intensity = lightIntensity;
```

6. `ScenePresetData.cs` の `CurrentVersion` のコメント末尾に足し、値を 40 にする:

```csharp
        // v40: liveEffect のステージライトに濃度 (intensity / intensityMin / intensityMax) を追加。
        //      旧形式は要素が無く未記録 (-1) で読め、適用時に色のアルファを濃度へ換算する
        public static readonly int CurrentVersion = 40;
```

- [ ] **Step 4: テストが通ることを確かめる**

ビルド＆テスト（全件）。
Expected: 全件 PASS（`LiveEffectStateTests` の等価判定・`ScenePresetEffectsTests` の往復も含む）。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin.Tests/StageLightIntensityCompatTests.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightCharacterShadowTests.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "feat(stage-light): Undo とシーンプリセットに濃度を足し旧プリセットは色のアルファから換算する"
```

---

### Task 6: ライブ演出ウィンドウ / 項目表示の UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/StageLightRowDrawer.cs`

**Interfaces:**
- Consumes: `TransformDataStageLight.defaultTrans.intensityInfo`、`TransformDataStageLightController.defaultTrans.intensityMinInfo/intensityMaxInfo`（Task 2 / 3）、`GUIView.DrawCustomValueFloat(CustomValueInfo, float, Action<float>, labelWidth:, sliderWidth:)`（既存）

キーフレーム詳細（`KeyFrameInspector`）と一括編集（`KeyFrameBatchDrawer`）は `GetColorValueInfoMap` / `GetCustomValueInfoMap` から自動で描くので変更不要。

- [ ] **Step 1: 色欄を RGB のみにする（28-29 行）**

```csharp
        // 濃度は別のスライダーで編集するため、色欄はアルファを出さない
        private readonly ColorFieldCache _color1FieldCache = new ColorFieldCache("", false);
        private readonly ColorFieldCache _color2FieldCache = new ColorFieldCache("", false);
```

- [ ] **Step 2: コントローラーの一括色設定に最小 / 最大濃度を足す（151-166 行の if ブロック末尾）**

```csharp
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityMinInfo,
                    controller.intensityMin,
                    x => controller.intensityMin = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityMaxInfo,
                    controller.intensityMax,
                    x => controller.intensityMax = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
```

- [ ] **Step 3: 個別ライトの色の後に濃度を足す（303-310 行の if ブロック末尾）**

```csharp
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityInfo,
                    light.intensity,
                    x => light.intensity = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
```

- [ ] **Step 4: ビルド＆テスト**

Expected: 2 構成ともビルド成功、全件 PASS。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/StageLightRowDrawer.cs
git commit -m "feat(stage-light): ライブ演出ウィンドウで色と濃度を分けて編集できるようにする"
```

---

### Task 7: ドキュメントと実機確認

**Files:**
- Modify: `docs-site/timeline/layers-effect.md`
- Modify: `docs-site/timeline/compatibility.md`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（git 管理外。コミットしない）

- [ ] **Step 1: `docs-site/timeline/layers-effect.md`**

ステージライトの補間表（10 行目付近の `| 色 | — | 線形補間 |` と 36 行目付近の `| 色 | 線形補間 |`）の次の行に、それぞれの表の列数に合わせて濃度の行を足す。個別ライトは `濃度`、コントローラーは `最小濃度 / 最大濃度` で、どちらも「タンジェント補間」。40 行目付近のコントローラーの説明の `最小色` / `最大色` の後に「`最小濃度` / `最大濃度`」を足す。表の列の意味は書く前に表ヘッダーを読んで合わせる。

- [ ] **Step 2: `docs-site/timeline/compatibility.md`**

既存の SE 独自項目の書き方に合わせて 1 項目足す。内容: 「ステージライトの濃度（光の柱の濃さ）は SE 独自。以前は色のアルファだった。旧タイムラインは読込時にアルファを濃度へ移す。MTE で開くと濃度は失われ、色のアルファ（未使用）で表示される」

- [ ] **Step 3: `W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」へ追記**

```markdown
- ステージライトの濃度（個別ライトキー index 24、値数 25。コントローラーキー index 37 / 38 = 最小 / 最大濃度、値数 39）は SE 独自。以前は色のアルファ（index 11 / 15 / 19）が濃度で、今は未使用。version は上げていない。旧値数のキーは `TransformDataStageLight(Controller).FromXml` が旧アルファを濃度へ移し、濃度のタンジェントを線形相当（正規化 1、非 smooth）にする。MTE は濃度を読まず未使用のアルファで表示するため濃さが変わる（新規キーは 0 で見えない）。シーンプリセットは v40 で `intensity` / `intensityMin` / `intensityMax` を追加。要素の無い旧プリセットは適用時に色のアルファを濃度へ換算する
```

- [ ] **Step 4: Commit（リポジトリ内のドキュメントだけ）**

```bash
git add docs-site/timeline/layers-effect.md docs-site/timeline/compatibility.md
git commit -m "docs(stage-light): 濃度の補間と互換性を追記する"
```

- [ ] **Step 5: 実機確認（ユーザーの了承を取ってから）**

ゲームが旧 DLL を握っているため、反映には再起動が要る。`com3d25-devbridge:restart-verify` スキルの手順で、ユーザーの了承を取ってから行う。デイリー画面（通常シーン）でエディタを有効にして確かめる（撮影モードは非対応）。

1. 旧タイムライン（ステージライトのアルファを 0.3 → 0.8 と変えているもの。無ければ現行 main でキーを 2 つ打って保存したもの）を読み込み、濃度スライダーが 0.3 / 0.8 になり、見た目が変わらないこと
2. 濃度スライダーを 0 → 2 に動かし、光の柱が消える → 濃くなること。色のピッカーにアルファが出ないこと
3. 一括色設定 ON で最小濃度 0.1・最大濃度 1.5 にし、配下のライトが端から端へ濃くなること
4. 濃度を変えて Undo / Redo で戻ること。シーンプリセットに保存 → 読込で濃度が戻ること
5. キーを 2 つ打って再生し、濃度が補間されること
6. タイムラインのステージライトの色帯が、以前（アルファ 0.3 で薄い帯）より濃く表示されること。色が RGB のみになり帯のアルファが 1 になるため。見づらければ別途相談する（今回は直さない）
7. 濃度 0 と 2 のキーを打ち、タンジェントを大きくして区間内で負へオーバーシュートさせても、光の柱が破綻しない（0 で止まる）こと。その状態で Undo して濃度が戻ること

devbridge の `eval_csharp` で `StageLight.intensity` と、マテリアルの `_Color.a` が一致することを読む（型は `COM3D2.MotionTimelineEditor.Plugin.StageLight`）。

---

## Self-Review メモ

- 合意事項 4 点（値の新設 + 旧データ換算 / コントローラー 2 本・一括色設定に統合 / タンジェント補間 / 0〜2・既定 0.3）は Task 2・3・6（値と UI）、Task 3・4（2 本と配分）、Task 2〜4（タンジェント）、Task 2・3（範囲・既定値）で実装する
- 合意時の説明では「version 39 の移行で旧アルファを移し、アルファを 1 にする」としていた。本計画では version を上げず、値数で旧キーを判定して `FromXml` で換算する。PNG の彩度・ライトの硬さと同じ作法で、テンプレートとクリップボードも同じ経路で換算されるため。旧アルファの添字は未使用になるので、1 への書き換えはしない

## レビュー却下メモ

- 新規キーは旧アルファ (index 11 / 15 / 19) が 0 のまま書かれ、換算キーは旧アルファが残る（MTE での見え方が分かれる）— SE → MTE は非対応の一方向互換。CLAUDE.md の追記案に記載済みで、計画は変えない
- docs-site の表の列数 — 実装時にヘッダーを読んで合わせる手順を既に入れている
