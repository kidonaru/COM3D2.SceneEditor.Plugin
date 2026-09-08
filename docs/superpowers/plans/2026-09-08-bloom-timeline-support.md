# ブルームのタイムライン対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans` でタスク単位に実装すること（本リポジトリの CLAUDE.md により subagent-driven-development は使わない）。手順は `- [ ]` チェックボックスで追跡する。

**Goal:** MTEUtils に追加された共有 DTO `BloomData` に追従し、SceneEditor のポストエフェクトレイヤーでブルームをキーフレーム制御・編集できるようにする。

**Architecture:** 既存 5 エフェクト（被写界深度 / GTToneMap / パラフィン / 距離フォグ / リムライト）と完全に同じ経路をなぞる。すなわち `PostEffectType` と `TransformType` に `Bloom` を足し、`PostEffectManager` が `PostEffectsClient.GetBloom/ApplyBloom` へ委譲し、`TransformDataBloom` が値を保持し、`PostEffectTimelineLayer` の partial が区間補間して適用し、`PostEffectRowDrawer` が編集 UI を描く。ブルームは単数エフェクト（インデックス無し）なので、被写界深度・GTToneMap と同じ「1 項目固定」の扱いにする。

**Tech Stack:** C# (net35 / net46 の 2 構成), Unity 5.6 系 IMGUI, MSBuild, MTEUtils サブモジュール経由のリフレクション連携

**Spec:** 本計画に内包（別途スペック文書は無い）。追従元は MTEUtils の `dc7ed7a feat(mteutils): 共有ポストエフェクト DTO にブルームを追加する`、および実体側 `COM3D2.PostEffects.Plugin` の `3d2b915 feat(timeline): ブルームをタイムライン制御の対象に追加する`。

## Global Constraints

- コメント・ログメッセージは日本語で書く。
- プラグイン間連携はリフレクション経由（`PostEffectsClient`）のみ。`COM3D2.PostEffects.Plugin` をコンパイル時参照してはいけない。
- ブルームの DTO は `COM3D2.MotionTimelineEditor.PostEffects.BloomData`（エイリアス `PEP` / `PEData`）を**そのまま**使う。`DepthOfFieldData` / `GTToneMapData` のようなプラグイン内複製クラスは新設しない（複製は追従漏れの温床であり、パラフィン・距離フォグ・リムライトは既に共有 DTO 直用に揃っている）。
- 補間は `PEP.PostEffectDataLerp.Lerp(BloomData, BloomData, float)` を使う。
- `TransformType` は XML へ**名前**でシリアライズされる（`TransformXml.type` は `XmlSerializer` 既定の enum 名出力）ため、enum の途中へ `Bloom` を挿入しても既存 XML は壊れない。アルファベット順を維持して `BGModel` の後・`Camera` の前へ入れる。
- ビルド確認は `debug.bat` を使わない（ゲーム停止中に実機へ反映されるため）。MSBuild を直接叩く。**COM3D2 版と COM3D25 版の両方**をビルドすること。
  ```
  cd source/COM3D2.SceneEditor.Plugin
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- 本リポジトリには自動テストが無い。各タスクの検証は「両構成のビルドが警告無しで通ること」＋「差分が計画どおりであること」で行う。実機確認は全タスク完了後にまとめて行う（末尾の「実機検証」節）。

## ファイル構成

| ファイル | 責務 | 変更種別 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/MTEUtils` | 共有 DTO のサブモジュール参照 | 更新（`dc7ed7a` へ） |
| `Timeline/PostEffectUtils.cs` | `PostEffectType.Bloom` と和名 | 変更 |
| `Timeline/TransformData/ITransformData.cs` | `TransformType.Bloom` | 変更 |
| `Timeline/Manager/PostEffectManager.cs` | `PostEffectsClient` への委譲・全無効化 | 変更 |
| `Timeline/TransformData/TransformDataBloom.cs` | キーフレーム値の保持と DTO 変換 | 新規 |
| `Timeline/TimelineIntegration.cs` | `TransformType.Bloom` の生成関数登録 | 変更 |
| `Timeline/TimelineLayer/PostEffectTimelineLayer_Bloom.cs` | 区間補間して実体へ適用 | 新規 |
| `Timeline/TimelineLayer/PostEffectTimelineLayer.cs` | ボーン一覧・再生・キー記録への組み込み | 変更 |
| `PostEffectRowDrawer.cs` | ブルームの編集 UI | 変更 |
| `Timeline/ItemInspector/PostEffectItemInspector.cs` | メニュー項目 → 編集 UI の振り分け | 変更 |

`BloomData` は 43 値ぶんの平置きフィールドを持つため `TransformDataBloom.cs` は他の TransformData より大きくなるが、既存の 1 エフェクト 1 ファイル構成を崩す理由が無いので分割しない。

---

### Task 1: 共有 DTO 追従と実体への委譲

MTEUtils サブモジュールの参照を進め、`PostEffectManager` からブルームを読み書きできるようにする。この時点ではタイムラインへは現れない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils`（サブモジュール参照）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/PostEffectUtils.cs:6-27`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:6-12`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs:196-230`

**Interfaces:**
- Consumes: `PostEffectsClient.GetBloom() : PEData.BloomData`, `PostEffectsClient.ApplyBloom(PEData.BloomData)`（MTEUtils `dc7ed7a` で追加済み）
- Produces:
  - `PostEffectType.Bloom`
  - `TransformType.Bloom`
  - `PostEffectManager.GetBloomData() : PEData.BloomData`
  - `PostEffectManager.ApplyBloom(PEData.BloomData data)`

- [ ] **Step 1: サブモジュールが目的のコミットを指していることを確認する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git submodule status source/COM3D2.SceneEditor.Plugin/MTEUtils
```

期待: `+dc7ed7aaadb0b81a63aa9621fb3c6d11ac9092c0 source/COM3D2.SceneEditor.Plugin/MTEUtils (heads/master)`
（先頭 `+` は「作業ツリーの checkout が親リポの記録と違う」の意味。Step 6 のコミットでこの差分を確定させる）

もし `dc7ed7a` でなければ、サブモジュール側で `git fetch && git checkout dc7ed7a` してから進める。

- [ ] **Step 2: `PostEffectType` に `Bloom` を足す**

`Timeline/PostEffectUtils.cs` の enum とマップへ追記する。順序はメニューの並び順になるため、末尾（リムライトの後）へ置く。

```csharp
    public enum PostEffectType
    {
        DepthOfField,
        GTToneMap,
        Paraffin,
        DistanceFog,
        Rimlight,
        Bloom,
    }
```

```csharp
        public static readonly Dictionary<PostEffectType, string> PostEffectTypeToJpNameMap = new Dictionary<PostEffectType, string>
        {
            { PostEffectType.DepthOfField, "被写界深度" },
            { PostEffectType.GTToneMap, "GTToneMap" },
            { PostEffectType.Paraffin, "パラフィン" },
            { PostEffectType.DistanceFog, "距離フォグ" },
            { PostEffectType.Rimlight, "リムライト" },
            { PostEffectType.Bloom, "ブルーム" },
        };
```

- [ ] **Step 3: `TransformType` に `Bloom` を足す**

`Timeline/TransformData/ITransformData.cs` の enum に、アルファベット順を保って挿入する。

```csharp
        None = 0,
        Animation,
        BG,
        BGColor,
        BGGroundColor,
        BGModel,
        Bloom,
        Camera,
```

- [ ] **Step 4: `PostEffectManager` へ委譲メソッドを足す**

`GetGTToneMapData` / `ApplyGTToneMap` の下（クラス末尾）へ追記する。DTO をそのまま流すので変換は不要。

```csharp
        // ブルームは共有 DTO をそのまま流す (実体側と同じ平置き構造のため変換が要らない)
        public PEData.BloomData GetBloomData()
        {
            return PostEffectsClient.GetBloom();
        }

        public void ApplyBloom(PEData.BloomData data)
        {
            PostEffectsClient.ApplyBloom(data);
        }
```

- [ ] **Step 5: `DisableAllEffects` でブルームも切る**

`Timeline/Manager/PostEffectManager.cs` の `DisableAllEffects` の末尾（`toneMap` の後）へ追記する。

```csharp
            var bloom = PostEffectsClient.GetBloom();
            bloom.enabled = false;
            PostEffectsClient.ApplyBloom(bloom);
```

- [ ] **Step 6: 両構成をビルドして通ることを確認する**

Run:
```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```
Expected: 両方 `Build succeeded.` / `0 Error(s)`

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils \
        source/COM3D2.SceneEditor.Plugin/Timeline/PostEffectUtils.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs
git commit -m "feat(posteffect): ブルームを実体へ読み書きする経路を追加する"
```

---

### Task 2: `TransformDataBloom`（キーフレーム値の器）

ブルームの全パラメータをタイムラインの値配列へ載せる。しきい値色を `color`、フレア色 A を `subColor` に割り当て、`TransformDataBase` が 2 色までしか面倒を見ないためフレア色 B/C/D は各チャンネルを CustomValue として持つ。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBloom.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:85-96`

**Interfaces:**
- Consumes: `PostEffectType.Bloom`（Task 1）、`PEP.BloomData`
- Produces:
  - `TransformDataBloom.defaultTrans`（UI が min/max/既定値を引くための静的インスタンス）
  - `TransformDataBloom.bloom { get; set; } : PEP.BloomData`
  - 各 `CustomValueInfo` アクセサ：`intensityInfo` `thresholdInfo` `blurIterationsInfo` `blurSpreadInfo` `hdrInfo` `screenBlendModeInfo` `highQualityInfo` `gameEffectDisabledInfo` `separationEnabledInfo` `separationCharactersEnabledInfo` `separationBackgroundEnabledInfo` `separationCharacterIntensityInfo` `separationCharacterThresholdInfo` `separationCharacterRadiusInfo` `lensFlareModeInfo` `lensFlareIntensityInfo` `lensFlareSaturationInfo` `lensFlareThresholdInfo` `flareRotationInfo` `hollyStretchWidthInfo` `hollywoodFlareBlurIterationsInfo`
  - `initialColor`（しきい値色 = 白）/ `initialSubColor`（フレア色 A）

- [ ] **Step 1: `TransformDataBloom.cs` を新規作成する**

値の並びは `Index` enum が唯一の正。`valueCount` は 43。フレア色 B/C/D の 12 チャンネルは `TransformDataBase.Reset()` が `GetCustomValueInfoMap().Keys` を回して既定値を書き戻すため、すべて `CustomValueInfoMap` へ登録する（未登録だとリセット時に 0 = 黒のまま残ってしまう）。

```csharp
using System.Collections.Generic;
using UnityEngine;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ブルーム 1 つ分のキーフレーム値。
    /// 色は 5 つあるが TransformDataBase が面倒を見るのは color / subColor の 2 つまでなので、
    /// しきい値色を color、フレア色 A を subColor に割り当て、
    /// フレア色 B/C/D は RGBA 各チャンネルを CustomValue として持つ
    /// </summary>
    public class TransformDataBloom : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            Visible = 1,

            // しきい値色
            ColorR = 2,
            ColorG = 3,
            ColorB = 4,
            ColorA = 5,

            // フレア色 A
            SubColorR = 6,
            SubColorG = 7,
            SubColorB = 8,
            SubColorA = 9,

            GameEffectDisabled = 10,
            Hdr = 11,
            ScreenBlendMode = 12,
            HighQuality = 13,
            Intensity = 14,
            Threshold = 15,
            BlurIterations = 16,
            BlurSpread = 17,

            SeparationEnabled = 18,
            SeparationCharactersEnabled = 19,
            SeparationBackgroundEnabled = 20,
            SeparationCharacterIntensity = 21,
            SeparationCharacterThreshold = 22,
            SeparationCharacterRadius = 23,

            LensFlareMode = 24,
            LensFlareIntensity = 25,
            LensFlareSaturation = 26,
            LensFlareThreshold = 27,
            FlareRotation = 28,
            HollyStretchWidth = 29,
            HollywoodFlareBlurIterations = 30,

            FlareColorBR = 31,
            FlareColorBG = 32,
            FlareColorBB = 33,
            FlareColorBA = 34,
            FlareColorCR = 35,
            FlareColorCG = 36,
            FlareColorCB = 37,
            FlareColorCA = 38,
            FlareColorDR = 39,
            FlareColorDG = 40,
            FlareColorDB = 41,
            FlareColorDA = 42,
        }

        public static TransformDataBloom defaultTrans = new TransformDataBloom();

        public override TransformType type => TransformType.Bloom;

        public override int valueCount => 43;

        public override bool hasColor => true;
        public override bool hasSubColor => true;
        public override bool hasVisible => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換と
        // 集約型レイヤーの補間形状キャリアとして残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => values;

        public override ValueData[] colorValues
        {
            get => new ValueData[]
            {
                values[(int)Index.ColorR],
                values[(int)Index.ColorG],
                values[(int)Index.ColorB],
                values[(int)Index.ColorA]
            };
        }

        public override ValueData[] subColorValues
        {
            get => new ValueData[]
            {
                values[(int)Index.SubColorR],
                values[(int)Index.SubColorG],
                values[(int)Index.SubColorB],
                values[(int)Index.SubColorA]
            };
        }

        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

        public override Color initialColor => Color.white;
        public override Color initialSubColor => new Color(0.4f, 0.4f, 0.8f, 0.75f);

        public static readonly Color InitialFlareColorB = new Color(0.4f, 0.8f, 0.8f, 0.75f);
        public static readonly Color InitialFlareColorC = new Color(0.8f, 0.4f, 0.8f, 0.75f);
        public static readonly Color InitialFlareColorD = new Color(0.8f, 0.4f, 0f, 0.75f);

        public TransformDataBloom()
        {
        }

        private static CustomValueInfo Toggle(Index index, string name, float defaultValue)
        {
            // bool は 0/1 の 2 値スライダーとして持つ (リムライトの excludeFace 等と同じ作法)
            return new CustomValueInfo
            {
                index = (int)index,
                name = name,
                min = 0f,
                max = 1f,
                step = 1f,
                defaultValue = defaultValue,
            };
        }

        private static CustomValueInfo Channel(Index index, string name, float defaultValue)
        {
            return new CustomValueInfo
            {
                index = (int)index,
                name = name,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = defaultValue,
            };
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            { "gameEffectDisabled", Toggle(Index.GameEffectDisabled, "ゲーム効果無効", 0f) },
            {
                // 0=Auto / 1=On / 2=Off。実体は enum なので丸めて渡す
                "hdr", new CustomValueInfo
                {
                    index = (int)Index.Hdr,
                    name = "HDR",
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
            // 0=Screen / 1=Add の 2 値。CustomValueInfo は min0/max1/step1 を BoolValue と
            // 判定するので、汎用のキーフレーム編集 UI に合わせてトグルとして扱う
            { "screenBlendMode", Toggle(Index.ScreenBlendMode, "加算合成", 0f) },
            { "highQuality", Toggle(Index.HighQuality, "高品質", 1f) },
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "強度",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = 2.1375f,
                }
            },
            {
                "threshold", new CustomValueInfo
                {
                    index = (int)Index.Threshold,
                    name = "しきい値",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.7f,
                }
            },
            {
                "blurIterations", new CustomValueInfo
                {
                    index = (int)Index.BlurIterations,
                    name = "ﾌﾞﾗｰ回数",
                    min = 1f,
                    max = 10f,
                    step = 1f,
                    defaultValue = 3f,
                }
            },
            {
                "blurSpread", new CustomValueInfo
                {
                    index = (int)Index.BlurSpread,
                    name = "ﾌﾞﾗｰ広がり",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 3.48f,
                }
            },

            { "separationEnabled", Toggle(Index.SeparationEnabled, "ｷｬﾗ背景分離", 0f) },
            { "separationCharactersEnabled", Toggle(Index.SeparationCharactersEnabled, "ｷｬﾗ有効", 1f) },
            { "separationBackgroundEnabled", Toggle(Index.SeparationBackgroundEnabled, "背景有効", 1f) },
            {
                "separationCharacterIntensity", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterIntensity,
                    name = "ｷｬﾗ強度",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 2.1375f,
                }
            },
            {
                "separationCharacterThreshold", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterThreshold,
                    name = "ｷｬﾗしきい値",
                    min = 0f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 0.7f,
                }
            },
            {
                "separationCharacterRadius", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterRadius,
                    name = "ｷｬﾗ広がり",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 3.48f,
                }
            },

            {
                // 0=Ghosting / 1=Anamorphic / 2=Combined
                "lensFlareMode", new CustomValueInfo
                {
                    index = (int)Index.LensFlareMode,
                    name = "ﾌﾚｱｽﾀｲﾙ",
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 1f,
                }
            },
            {
                "lensFlareIntensity", new CustomValueInfo
                {
                    index = (int)Index.LensFlareIntensity,
                    name = "ﾌﾚｱ強度",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "lensFlareSaturation", new CustomValueInfo
                {
                    index = (int)Index.LensFlareSaturation,
                    name = "ﾌﾚｱ彩度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0.75f,
                }
            },
            {
                "lensFlareThreshold", new CustomValueInfo
                {
                    index = (int)Index.LensFlareThreshold,
                    name = "ﾌﾚｱしきい値",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.3f,
                }
            },
            {
                "flareRotation", new CustomValueInfo
                {
                    index = (int)Index.FlareRotation,
                    name = "ﾌﾚｱ回転",
                    min = 0f,
                    max = 6.28f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "hollyStretchWidth", new CustomValueInfo
                {
                    index = (int)Index.HollyStretchWidth,
                    name = "伸縮幅",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 2.5f,
                }
            },
            {
                // 実体側の許容範囲 (BloomSetting.MIN/MAX_HOLLYWOOD_FLARE_BLUR_ITERATIONS) に合わせる
                "hollywoodFlareBlurIterations", new CustomValueInfo
                {
                    index = (int)Index.HollywoodFlareBlurIterations,
                    name = "ﾌﾚｱﾌﾞﾗｰ回数",
                    min = 0f,
                    max = 10f,
                    step = 1f,
                    defaultValue = 2f,
                }
            },

            // フレア色 B/C/D。既定値へリセットできるようチャンネル単位で登録する
            { "flareColorBR", Channel(Index.FlareColorBR, "ﾌﾚｱ色B R", 0.4f) },
            { "flareColorBG", Channel(Index.FlareColorBG, "ﾌﾚｱ色B G", 0.8f) },
            { "flareColorBB", Channel(Index.FlareColorBB, "ﾌﾚｱ色B B", 0.8f) },
            { "flareColorBA", Channel(Index.FlareColorBA, "ﾌﾚｱ色B A", 0.75f) },
            { "flareColorCR", Channel(Index.FlareColorCR, "ﾌﾚｱ色C R", 0.8f) },
            { "flareColorCG", Channel(Index.FlareColorCG, "ﾌﾚｱ色C G", 0.4f) },
            { "flareColorCB", Channel(Index.FlareColorCB, "ﾌﾚｱ色C B", 0.8f) },
            { "flareColorCA", Channel(Index.FlareColorCA, "ﾌﾚｱ色C A", 0.75f) },
            { "flareColorDR", Channel(Index.FlareColorDR, "ﾌﾚｱ色D R", 0.8f) },
            { "flareColorDG", Channel(Index.FlareColorDG, "ﾌﾚｱ色D G", 0.4f) },
            { "flareColorDB", Channel(Index.FlareColorDB, "ﾌﾚｱ色D B", 0f) },
            { "flareColorDA", Channel(Index.FlareColorDA, "ﾌﾚｱ色D A", 0.75f) },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // CustomValueInfo アクセサ
        public CustomValueInfo gameEffectDisabledInfo => GetCustomValueInfo("gameEffectDisabled");
        public CustomValueInfo hdrInfo => GetCustomValueInfo("hdr");
        public CustomValueInfo screenBlendModeInfo => GetCustomValueInfo("screenBlendMode");
        public CustomValueInfo highQualityInfo => GetCustomValueInfo("highQuality");
        public CustomValueInfo intensityInfo => GetCustomValueInfo("intensity");
        public CustomValueInfo thresholdInfo => GetCustomValueInfo("threshold");
        public CustomValueInfo blurIterationsInfo => GetCustomValueInfo("blurIterations");
        public CustomValueInfo blurSpreadInfo => GetCustomValueInfo("blurSpread");
        public CustomValueInfo separationEnabledInfo => GetCustomValueInfo("separationEnabled");
        public CustomValueInfo separationCharactersEnabledInfo => GetCustomValueInfo("separationCharactersEnabled");
        public CustomValueInfo separationBackgroundEnabledInfo => GetCustomValueInfo("separationBackgroundEnabled");
        public CustomValueInfo separationCharacterIntensityInfo => GetCustomValueInfo("separationCharacterIntensity");
        public CustomValueInfo separationCharacterThresholdInfo => GetCustomValueInfo("separationCharacterThreshold");
        public CustomValueInfo separationCharacterRadiusInfo => GetCustomValueInfo("separationCharacterRadius");
        public CustomValueInfo lensFlareModeInfo => GetCustomValueInfo("lensFlareMode");
        public CustomValueInfo lensFlareIntensityInfo => GetCustomValueInfo("lensFlareIntensity");
        public CustomValueInfo lensFlareSaturationInfo => GetCustomValueInfo("lensFlareSaturation");
        public CustomValueInfo lensFlareThresholdInfo => GetCustomValueInfo("lensFlareThreshold");
        public CustomValueInfo flareRotationInfo => GetCustomValueInfo("flareRotation");
        public CustomValueInfo hollyStretchWidthInfo => GetCustomValueInfo("hollyStretchWidth");
        public CustomValueInfo hollywoodFlareBlurIterationsInfo => GetCustomValueInfo("hollywoodFlareBlurIterations");

        // 値アクセサ
        private ValueData[] FlareColorValues(Index startIndex)
        {
            var i = (int)startIndex;
            return new ValueData[] { values[i], values[i + 1], values[i + 2], values[i + 3] };
        }

        public Color flareColorB
        {
            get => FlareColorValues(Index.FlareColorBR).ToColor();
            set => FlareColorValues(Index.FlareColorBR).FromColor(value);
        }

        public Color flareColorC
        {
            get => FlareColorValues(Index.FlareColorCR).ToColor();
            set => FlareColorValues(Index.FlareColorCR).FromColor(value);
        }

        public Color flareColorD
        {
            get => FlareColorValues(Index.FlareColorDR).ToColor();
            set => FlareColorValues(Index.FlareColorDR).FromColor(value);
        }

        public PEP.BloomData bloom
        {
            get => new PEP.BloomData
            {
                enabled = visible,
                gameEffectDisabled = values[(int)Index.GameEffectDisabled].boolValue,
                hdr = values[(int)Index.Hdr].intValue,
                screenBlendMode = values[(int)Index.ScreenBlendMode].intValue,
                highQuality = values[(int)Index.HighQuality].boolValue,
                intensity = values[(int)Index.Intensity].value,
                threshold = values[(int)Index.Threshold].value,
                thresholdColor = color,
                blurIterations = values[(int)Index.BlurIterations].intValue,
                blurSpread = values[(int)Index.BlurSpread].value,

                separationEnabled = values[(int)Index.SeparationEnabled].boolValue,
                separationCharactersEnabled = values[(int)Index.SeparationCharactersEnabled].boolValue,
                separationBackgroundEnabled = values[(int)Index.SeparationBackgroundEnabled].boolValue,
                separationCharacterIntensity = values[(int)Index.SeparationCharacterIntensity].value,
                separationCharacterThreshold = values[(int)Index.SeparationCharacterThreshold].value,
                separationCharacterRadius = values[(int)Index.SeparationCharacterRadius].value,

                lensFlareMode = values[(int)Index.LensFlareMode].intValue,
                lensFlareIntensity = values[(int)Index.LensFlareIntensity].value,
                lensFlareSaturation = values[(int)Index.LensFlareSaturation].value,
                lensFlareThreshold = values[(int)Index.LensFlareThreshold].value,
                flareRotation = values[(int)Index.FlareRotation].value,
                hollyStretchWidth = values[(int)Index.HollyStretchWidth].value,
                hollywoodFlareBlurIterations = values[(int)Index.HollywoodFlareBlurIterations].intValue,
                flareColorA = subColor,
                flareColorB = flareColorB,
                flareColorC = flareColorC,
                flareColorD = flareColorD,
            };
            set
            {
                visible = value.enabled;
                values[(int)Index.GameEffectDisabled].boolValue = value.gameEffectDisabled;
                values[(int)Index.Hdr].intValue = value.hdr;
                values[(int)Index.ScreenBlendMode].intValue = value.screenBlendMode;
                values[(int)Index.HighQuality].boolValue = value.highQuality;
                values[(int)Index.Intensity].value = value.intensity;
                values[(int)Index.Threshold].value = value.threshold;
                color = value.thresholdColor;
                values[(int)Index.BlurIterations].intValue = value.blurIterations;
                values[(int)Index.BlurSpread].value = value.blurSpread;

                values[(int)Index.SeparationEnabled].boolValue = value.separationEnabled;
                values[(int)Index.SeparationCharactersEnabled].boolValue = value.separationCharactersEnabled;
                values[(int)Index.SeparationBackgroundEnabled].boolValue = value.separationBackgroundEnabled;
                values[(int)Index.SeparationCharacterIntensity].value = value.separationCharacterIntensity;
                values[(int)Index.SeparationCharacterThreshold].value = value.separationCharacterThreshold;
                values[(int)Index.SeparationCharacterRadius].value = value.separationCharacterRadius;

                values[(int)Index.LensFlareMode].intValue = value.lensFlareMode;
                values[(int)Index.LensFlareIntensity].value = value.lensFlareIntensity;
                values[(int)Index.LensFlareSaturation].value = value.lensFlareSaturation;
                values[(int)Index.LensFlareThreshold].value = value.lensFlareThreshold;
                values[(int)Index.FlareRotation].value = value.flareRotation;
                values[(int)Index.HollyStretchWidth].value = value.hollyStretchWidth;
                values[(int)Index.HollywoodFlareBlurIterations].intValue = value.hollywoodFlareBlurIterations;
                subColor = value.flareColorA;
                flareColorB = value.flareColorB;
                flareColorC = value.flareColorC;
                flareColorD = value.flareColorD;
            }
        }
    }
}
```

- [ ] **Step 2: `TimelineIntegration` へ生成関数を登録する**

`Timeline/TimelineIntegration.cs` の `TryRegisterPostEffects` 内、`RegisterTransform` の並び（アルファベット順）の先頭へ挿入する。

```csharp
                timelineManager.RegisterTransform(
                    MTEP.TransformType.Bloom,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBloom>);
                timelineManager.RegisterTransform(
                    MTEP.TransformType.DepthOfField,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDepthOfField>);
```

- [ ] **Step 3: 両構成をビルドして通ることを確認する**

Run: Task 1 Step 6 と同じ 2 コマンド
Expected: 両方 `Build succeeded.` / `0 Error(s)`

- [ ] **Step 4: `Index` と `valueCount` の整合を目視確認する**

`Index` の最大値は `FlareColorDA = 42`、`valueCount` は 43。`CustomValueInfoMap` に登録されていないのは `Easing` / `Visible` / 色 8 チャンネル（`ColorR`〜`SubColorA`）のみであることを確認する。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBloom.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
git commit -m "feat(timeline): ブルームの TransformData を追加する"
```

---

### Task 3: レイヤーへの組み込み（キー記録と再生）

ポストエフェクトレイヤーのボーン一覧へ `Bloom` を足し、キー記録・再生の分岐を通す。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer_Bloom.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer.cs`

**Interfaces:**
- Consumes: `PostEffectManager.GetBloomData()` / `ApplyBloom(...)`（Task 1）、`TransformDataBloom.bloom`（Task 2）
- Produces: `PostEffectTimelineLayer.ApplyBloom(MotionData, float)`（private）、ボーン名 `"Bloom"`

- [ ] **Step 1: `PostEffectTimelineLayer_Bloom.cs` を新規作成する**

パラフィン等と違いインデックスを持たないため、名前の生成関数は不要。

```csharp
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyBloom(MotionData motion, float t)
        {
            var start = motion.start as TransformDataBloom;
            var end = motion.end as TransformDataBloom;

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            var bloom = PEP.PostEffectDataLerp.Lerp(start.bloom, end.bloom, lerpTime);

            postEffectManager.ApplyBloom(bloom);
        }
    }
}
```

- [ ] **Step 2: ボーン一覧へ `Bloom` を足す**

`PostEffectTimelineLayer.cs` の `allBoneNames`。固定エフェクトが 2 → 3 になるので初期容量も直す。

```csharp
                    _allBoneNames = new List<string>(
                        3 + timeline.paraffinCount + timeline.distanceFogCount + timeline.rimlightCount);
                    _allBoneNames.Add("DepthOfField");
                    _allBoneNames.Add("GTToneMap");
                    _allBoneNames.AddRange(paraffinNames);
                    _allBoneNames.AddRange(distanceFogNames);
                    _allBoneNames.AddRange(rimlightNames);
                    _allBoneNames.Add("Bloom");
```

- [ ] **Step 3: `Update` の件数判定を 3 固定エフェクトに合わせる**

同ファイルの `Update`。ここが 2 のままだとボーン一覧が毎フレーム作り直される。

```csharp
            var boneCount = 3
                + timeline.paraffinCount
                + timeline.distanceFogCount
                + timeline.rimlightCount;
```

- [ ] **Step 4: `ApplyPlayData` へブルームを足す**

`ApplyPlayDataByType(TransformType.GTToneMap);` の後へ追記する。

```csharp
            ApplyPlayDataByType(TransformType.Bloom);
            //stopwatch.ProcessEnd("  Bloom");
```

- [ ] **Step 5: `ApplyMotion` の分岐へ足す**

```csharp
                case TransformType.Bloom:
                    ApplyBloom(motion, t);
                    break;
```

- [ ] **Step 6: `UpdateFrame` の分岐へ足す**

`case PostEffectType.GTToneMap:` ブロックの後へ追記する。

```csharp
                    case PostEffectType.Bloom:
                    {
                        var trans = CreateTransformData<TransformDataBloom>(effectName);
                        trans.bloom = postEffectManager.GetBloomData();

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
```

- [ ] **Step 7: `GetTransformType` の分岐へ足す**

```csharp
                case PostEffectType.Bloom:
                    return TransformType.Bloom;
```

- [ ] **Step 8: 両構成をビルドして通ることを確認する**

Run: Task 1 Step 6 と同じ 2 コマンド
Expected: 両方 `Build succeeded.` / `0 Error(s)`

- [ ] **Step 9: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer_Bloom.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer.cs
git commit -m "feat(timeline): ブルームをポストエフェクトレイヤーで再生・記録する"
```

---

### Task 4: 編集 UI

Inspector からブルームのパラメータを編集できるようにする。色が 5 つあり既存の 2 色ペア用キャッシュでは足りないため、ブルーム専用のキャッシュを持たせる。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/PostEffectItemInspector.cs`

**Interfaces:**
- Consumes: `TransformDataBloom.defaultTrans` の各 Info（Task 2）、`PostEffectManager.GetBloomData()/ApplyBloom(...)`（Task 1）
- Produces: `PostEffectRowDrawer.DrawBloomRows(GUIView view)`

- [ ] **Step 1: ブルーム用の色欄キャッシュを足す**

`PostEffectRowDrawer.cs` のフィールド宣言部を直す。まず `_color3FieldCache` / `_color4FieldCache` は宣言と `SetColorLabels` でのラベル設定しか無く、どの `DrawXxxRows` からも参照されていない死んだフィールドなので削除する（`DrawColorPair` が使うのは `_color1` / `_color2` だけ）。

```csharp
        // 削除する 2 行
        private readonly ColorFieldCache _color3FieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color4FieldCache = new ColorFieldCache("", true);
```

`SetColorLabels` 内の対応する 2 行も削除する。

```csharp
        // 削除する 2 行
            _color3FieldCache.label = colorLabelPrefix + "/色3";
            _color4FieldCache.label = colorLabelPrefix + "/色4";
```

その上で、`_color2FieldCache` の下へブルーム用のキャッシュを足す。しきい値色はアルファを使わないので `hasAlpha` を false にする。

```csharp
        // ブルームは色が 5 つあり色1/色2 のペア用キャッシュでは足りないため、専用に持つ
        private readonly ColorFieldCache _bloomThresholdColorFieldCache = new ColorFieldCache("", false);
        private readonly ColorFieldCache[] _bloomFlareColorFieldCaches =
        {
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
        };

        private static readonly string[] BloomFlareColorLabels = { "A", "B", "C", "D" };
```

- [ ] **Step 2: `SetColorLabels` でブルームのラベルも設定する**

残った 2 行（色1 / 色2）の後へ追記する。

```csharp
            _bloomThresholdColorFieldCache.label = colorLabelPrefix + "/しきい値色";
            for (var i = 0; i < _bloomFlareColorFieldCaches.Length; i++)
            {
                _bloomFlareColorFieldCaches[i].label = colorLabelPrefix + "/ﾌﾚｱ色" + BloomFlareColorLabels[i];
            }
```

- [ ] **Step 3: `DrawBloomRows` を足す**

`DrawGTToneMapRows` の後（`DrawGTToneMapCurve` の前）へ追記する。実体側 UI と同じく、分離が無効なときはキャラ用の行を出さない。

```csharp
        /// <summary>ブルーム</summary>
        public void DrawBloomRows(GUIView view)
        {
            var bloom = postEffectManager.GetBloomData();
            var updateTransform = false;
            var defaultTrans = TransformDataBloom.defaultTrans;

            updateTransform = view.DrawToggle("有効化", bloom.enabled, 80, 20, newValue =>
            {
                bloom.enabled = newValue;
            });

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.gameEffectDisabledInfo,
                bloom.gameEffectDisabled,
                newValue => bloom.gameEffectDisabled = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.hdrInfo,
                bloom.hdr,
                newValue => bloom.hdr = newValue);

            // 0=Screen / 1=Add
            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.screenBlendModeInfo,
                bloom.screenBlendMode != 0,
                newValue => bloom.screenBlendMode = newValue ? 1 : 0);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.highQualityInfo,
                bloom.highQuality,
                newValue => bloom.highQuality = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.intensityInfo,
                bloom.intensity,
                newValue => bloom.intensity = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.thresholdInfo,
                bloom.threshold,
                newValue => bloom.threshold = newValue);

            updateTransform |= view.DrawColor(
                _bloomThresholdColorFieldCache,
                bloom.thresholdColor,
                defaultTrans.initialColor,
                newValue => bloom.thresholdColor = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.blurIterationsInfo,
                bloom.blurIterations,
                newValue => bloom.blurIterations = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.blurSpreadInfo,
                bloom.blurSpread,
                newValue => bloom.blurSpread = newValue);

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("キャラと背景の分離", 200, 20);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.separationEnabledInfo,
                bloom.separationEnabled,
                newValue => bloom.separationEnabled = newValue);

            // 分離が無効なうちは以降の値が効かないので出さない (実体側 UI と同じ条件)
            if (bloom.separationEnabled)
            {
                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.separationCharactersEnabledInfo,
                    bloom.separationCharactersEnabled,
                    newValue => bloom.separationCharactersEnabled = newValue);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.separationBackgroundEnabledInfo,
                    bloom.separationBackgroundEnabled,
                    newValue => bloom.separationBackgroundEnabled = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterIntensityInfo,
                    bloom.separationCharacterIntensity,
                    newValue => bloom.separationCharacterIntensity = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterThresholdInfo,
                    bloom.separationCharacterThreshold,
                    newValue => bloom.separationCharacterThreshold = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterRadiusInfo,
                    bloom.separationCharacterRadius,
                    newValue => bloom.separationCharacterRadius = newValue);
            }

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("レンズフレア", 200, 20);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.lensFlareModeInfo,
                bloom.lensFlareMode,
                newValue => bloom.lensFlareMode = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareIntensityInfo,
                bloom.lensFlareIntensity,
                newValue => bloom.lensFlareIntensity = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareSaturationInfo,
                bloom.lensFlareSaturation,
                newValue => bloom.lensFlareSaturation = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareThresholdInfo,
                bloom.lensFlareThreshold,
                newValue => bloom.lensFlareThreshold = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.flareRotationInfo,
                bloom.flareRotation,
                newValue => bloom.flareRotation = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.hollyStretchWidthInfo,
                bloom.hollyStretchWidth,
                newValue => bloom.hollyStretchWidth = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.hollywoodFlareBlurIterationsInfo,
                bloom.hollywoodFlareBlurIterations,
                newValue => bloom.hollywoodFlareBlurIterations = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[0],
                bloom.flareColorA,
                defaultTrans.initialSubColor,
                newValue => bloom.flareColorA = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[1],
                bloom.flareColorB,
                TransformDataBloom.InitialFlareColorB,
                newValue => bloom.flareColorB = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[2],
                bloom.flareColorC,
                TransformDataBloom.InitialFlareColorC,
                newValue => bloom.flareColorC = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[3],
                bloom.flareColorD,
                TransformDataBloom.InitialFlareColorD,
                newValue => bloom.flareColorD = newValue);

            if (updateTransform)
            {
                postEffectManager.ApplyBloom(bloom);
            }
        }
```

- [ ] **Step 4: クラスの説明コメントへブルームを足す**

`PostEffectRowDrawer.cs` 冒頭の `<summary>` 1 行目を書き換える。

```csharp
    /// ポストエフェクト 1 つ分のパラメータ行 (被写界深度 / パラフィン / 距離フォグ / リムライト / GTToneMap / ブルーム)。
```

- [ ] **Step 5: Inspector の振り分けへ足す**

`Timeline/ItemInspector/PostEffectItemInspector.cs` の `DrawItem`。`case MTEP.PostEffectType.Rimlight:` の後へ追記する。

```csharp
                case MTEP.PostEffectType.Bloom:
                    drawer.DrawBloomRows(view);
                    return;
```

あわせてクラスの説明コメントを直す。

```csharp
    /// 項目は被写界深度・GTToneMap・ブルームが 1 つずつ、パラフィン・距離フォグ・リムライトが
```

- [ ] **Step 6: 両構成をビルドして通ることを確認する**

Run: Task 1 Step 6 と同じ 2 コマンド
Expected: 両方 `Build succeeded.` / `0 Error(s)`

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/PostEffectItemInspector.cs
git commit -m "feat(posteffect): ブルームの編集 UI を追加する"
```

---

## 実機検証（全タスク完了後）

ゲームを起動し、SceneEditor のタイムラインで以下を確認する。`COM3D2.PostEffects.Plugin` は `3d2b915` 以降のビルドが入っていること（`TimelineBridge.GetBloom/ApplyBloom` が無いと `PostEffectsClient` が接続を諦め、ポストエフェクトのレイヤーごと登録されない）。

1. ポストエフェクトレイヤーのボーン一覧末尾に「ブルーム」が現れる。
2. 編集モードで Inspector からブルームを開き、強度・しきい値・フレア色などを変えると画面へ即反映される。
3. 2 か所のフレームに違う強度でキーを打ち、再生して連続的に変化する（`blurIterations` など反復回数は補間されず start 側のまま）。
4. 保存して読み込み直すと値が復元される。XML に `<Type>Bloom</Type>` のボーンが出ていること。
5. ブルームのキーが無い既存 XML を読み込んでもエラーが出ず、他エフェクトが従来どおり動く。
6. タイムラインを閉じる（レイヤー非カレント化）とブルームが無効へ戻る。

## レビュー却下メモ

- 「`PostEffects.Plugin` が旧版だと `PostEffectsClient.Initialize` が全メンバ揃わず `isAvailable=false` になり、ブルーム以外のポストエフェクトまで巻き添えで止まる。リリースノートへ明記すべき」 — 事実として正しいが、MTEUtils `dc7ed7a` で確定済みの既存設計であり本計画のスコープ外。計画の「実機検証」節に前提として記載済みで、リリースノートは release-prep スキルの責務なのでここでは扱わない。

## 完了後のフロー

CLAUDE.md の標準フローに従い、実装完了後に **code-review** スキルでレビューしてからユーザーへ提示する。
