# メイドスケール Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイドの左右の上腕・前腕・手を、キーフレームごとに均一の倍率で拡縮できる「メイドスケール」レイヤーを追加する（嘘パース用）。

**Architecture:**
- 倍率の状態は `MaidScaleController` が持つ。重力の `MaidGravityController` と同じく `MaidManipulateManager` が所有し、レイヤー・Inspector・履歴の三者が読み書きする
- Harmony で `TBody.LateUpdate` に前後処理を差し込む（`MaidScaleLateUpdatePatch`）
  - postfix で、そのメイドの全スロットの複製骨（`SkinnedMeshRenderer.bones`）へ「その時点の値 × 倍率」を書く
  - 次のフレームのプラグイン `Update` と、次の `TBody.LateUpdate` の prefix で、書いた値が残っている骨だけ元へ戻す
  - 描画直前（onPreCull）ではスキニングに間に合わないことを、実機で確認済み（仕様の「実機調査」）
- レイヤーは `GravityTimelineLayer` と同じ形で、補間した倍率をコントローラーへ渡すだけにする

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Unity 5.6 系 API、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-03-maid-scale-design.md`

## Global Constraints

- 対象の骨は `Bip01 L UpperArm` / `Bip01 L Forearm` / `Bip01 L Hand` / `Bip01 R UpperArm` / `Bip01 R Forearm` / `Bip01 R Hand` の 6 本
- 表示名は「左上腕」「左前腕」「左手」「右上腕」「右前腕」「右手」
- 倍率は均一の 1 値。既定 1、範囲 0.1〜3
- レイヤーは `[TimelineLayerDesc("メイドスケール", 19, TimelineLayerCategory.Maid)]`
- `TimelineData.CurrentVersion` は上げない
- 本体の骨（`m_Bones` 直下の `Bip01`）には書かない。複製骨だけに書く
- コードのコメントとエラーログは日本語で書く
- **2 構成のビルド**: `COM3D2`（.NET 3.5）と `COM3D25`（.NET 4.7.1）の両方を通す
  - .NET 3.5 では入力 5 個以上の `Func<>` / `Action<>` が使えない
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild を直接叩く
- テストでは Unity のネイティブ呼び出し（`new GameObject`、`Quaternion.Euler` など）を使わない
  - `Mathf.Clamp`、`Mathf.Approximately`、`Vector3` の演算はマネージド実装なので使える
- プラグイン本体の csproj は `<Compile Include>` を明示列挙している。新しいファイルは必ず追記する（テスト側は SDK 形式なので不要）

## Review Focus

- **体型スライダー**: 倍率を掛けた状態で体型スライダーを動かすと、ゲームが骨のスケールを書き換える。戻す処理がゲームの新しい値を古い退避値で上書きしてはならない
  - 戻すのは「今の値 = 自分が書いた値」の骨だけにする。Task 2 の `ShouldRestore` テストで固定する
- **重ね掛け**: プラグインの `Update` が走らないフレーム（UI 無効など）でも、倍率が積み上がらないこと
  - postfix は掛ける前に必ず、そのメイドの前回分を戻す。Task 5 の実機検証で、UI を無効にした状態でも確認する
- **MaidVoicePitch の有無**: 前腕は MaidVoicePitch が毎フレーム書き戻すが、postfix はその後に掛けるので効くこと
  - Task 5 の実機検証で確認する（この環境には導入済み）
- **着替え・ボディ再ロード・CRC のサブスロット**: 着替えやボディ再ロードの後も倍率が効き、破棄済みの骨で例外を出さないこと。2.5 のサブスロットの衣装にも効くこと
  - 着替え中は掛けない。骨のキャッシュはスロット obj の並びの変化で作り直す。Task 5 の実機検証で確認する
- **倍率 1 の扱い**: 倍率 1 は「状態なし」と同じに扱う。何も書かず、全骨が 1 に戻ったメイドの状態は捨てる
  - Task 2 の `MaidScaleState` テストで固定する
- **未知の骨名**: 対象外の骨名を渡されたら無視すること
  - Task 1・Task 2 のテストで固定する

## ファイル構成

| ファイル | 役割 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleBones.cs` | 対象骨の一覧・表示名・倍率の範囲（純粋ロジック） |
| Create `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleState.cs` | メイド 1 体分の倍率の辞書と、戻してよいかの判定（純粋ロジック） |
| Create `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleController.cs` | 状態の保持、複製骨のキャッシュ、メイドごとの適用と復元 |
| Create `source/COM3D2.SceneEditor.Plugin/Manager/MaidScaleLateUpdatePatch.cs` | `TBody.LateUpdate` の prefix / postfix からコントローラーを呼ぶ |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMaidScale.cs` | キー 1 件（倍率 1 値） |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidScaleTimelineLayer.cs` | レイヤー |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MaidScaleItemInspector.cs` | Inspector の倍率スライダー |
| Create `source/COM3D2.SceneEditor.Plugin/Manager/History/MaidScaleSnapshot.cs` | 履歴のスナップショット |
| Modify `Timeline/TransformData/ITransformData.cs` | `TransformType.MaidScale` を追加 |
| Modify `Manager/History/HistoryScope.cs`、`SnapshotFactory.cs` | `HistoryScope.MaidScale` を追加 |
| Modify `MaidManipulation/MaidManipulateManager.cs` | コントローラーの所有・解放・破棄 |
| Modify `COM3D2.SceneEditor.Plugin.cs` | `Update` で復元を呼ぶ。初期化でパッチを当てる |
| Modify `Timeline/TimelineIntegration.cs` | レイヤー・Transform・Inspector の登録 |
| Modify `COM3D2.SceneEditor.Plugin.csproj` | Compile 追記 |
| Test `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs` | 純粋ロジックとキーのテスト |
| Modify `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs` | 期待表へ追加 |
| Modify `W:\COM3D2_5\work\CLAUDE.md` | XML 互換方向の節へ 1 行追加 |

以下、`source/COM3D2.SceneEditor.Plugin/` 配下のパスは `<P>/` と略す。

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

以下では、これを「**ビルド＆テスト**」と呼ぶ。特定のテストだけ回すときは、末尾を `dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~MaidScale"` に置き換える。

---

### Task 1: 対象骨の定義とキーのデータ型

**Files:**
- Create: `<P>/MaidManipulation/MaidScaleBones.cs`
- Create: `<P>/Timeline/TransformData/TransformDataMaidScale.cs`
- Modify: `<P>/Timeline/TransformData/ITransformData.cs`（`LookAtTarget,` の次の行）
- Modify: `<P>/Timeline/TimelineIntegration.cs`（`TransformType.Gravity` の RegisterTransform の直後）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`

**Interfaces:**
- Produces:
  - `COM3D2.SceneEditor.Plugin.MaidScaleBone`（`string boneName`、`string displayName`）
  - `COM3D2.SceneEditor.Plugin.MaidScaleBones`
    - 定数 `MinScale = 0.1f`、`MaxScale = 3f`、`DefaultScale = 1f`
    - `static readonly List<MaidScaleBone> bones`（6 本、上の表示順）
    - `static MaidScaleBone Find(string boneName)`（未知・null なら null）
    - `static float Clamp(float scale)`
  - `TransformType.MaidScale`
  - `COM3D2.MotionTimelineEditor.Plugin.TransformDataMaidScale`
    - `enum Index { Multiplier = 0 }`
    - `ValueData multiplierValue`、`float multiplier { get; set; }`、`bool isDefault`
    - 既定値は 1
    - 基底の `scale`（Vector3）と名前が衝突するので、`scale` という名前は使わない

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`:

```csharp
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidScaleTests
    {
        private static MTEP.TransformDataMaidScale CreateKey()
        {
            var trans = new MTEP.TransformDataMaidScale();
            trans.Initialize("Bip01 L Hand");
            return trans;
        }

        private static MTEP.TransformXml CreateXml(float[] values)
        {
            return new MTEP.TransformXml
            {
                name = "Bip01 L Hand",
                type = MTEP.TransformType.MaidScale,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 対象骨は左右の上腕前腕手の6本で表示名を持つ()
        {
            var names = MaidScaleBones.bones.ConvertAll(b => b.boneName);
            Assert.Equal(new[]
            {
                "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand",
                "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand",
            }, names.ToArray());
            Assert.Equal("左手", MaidScaleBones.Find("Bip01 L Hand").displayName);
            Assert.Equal("右上腕", MaidScaleBones.Find("Bip01 R UpperArm").displayName);
        }

        [Fact]
        public void 対象外の骨名はnullを返す()
        {
            Assert.Null(MaidScaleBones.Find("Bip01 Head"));
            Assert.Null(MaidScaleBones.Find(null));
            Assert.Null(MaidScaleBones.Find(""));
        }

        [Theory]
        [InlineData(0f, 0.1f)]
        [InlineData(1.5f, 1.5f)]
        [InlineData(10f, 3f)]
        public void 倍率は範囲内へ丸める(float input, float expected)
        {
            Assert.Equal(expected, MaidScaleBones.Clamp(input));
        }

        [Fact]
        public void キーは1値で既定は倍率1()
        {
            var trans = CreateKey();
            Assert.Equal(MTEP.TransformType.MaidScale, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(1f, trans.multiplier);
            Assert.True(trans.isDefault);
            Assert.Single(trans.tangentValues);

            var info = trans.GetCustomValueInfoMap()["multiplier"];
            Assert.Equal(MaidScaleBones.MinScale, info.min);
            Assert.Equal(MaidScaleBones.MaxScale, info.max);
            Assert.Equal(MaidScaleBones.DefaultScale, info.defaultValue);
        }

        [Fact]
        public void リセットしたキーは倍率1に戻る()
        {
            var trans = CreateKey();
            trans.multiplier = 2f;
            Assert.False(trans.isDefault);

            trans.Reset();
            Assert.Equal(1f, trans.multiplier);
        }

        [Fact]
        public void XMLの往復で倍率を保つ()
        {
            var trans = CreateKey();
            trans.multiplier = 1.75f;

            var loaded = CreateKey();
            loaded.FromXml(trans.ToXml());

            Assert.Equal(1.75f, loaded.multiplier);
            Assert.Equal("Bip01 L Hand", loaded.name);
        }

        [Fact]
        public void XMLの値が倍率としてそのまま読まれる()
        {
            var trans = CreateKey();
            trans.FromXml(CreateXml(new[] { 2.5f }));
            Assert.Equal(2.5f, trans.multiplier);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScaleTests"` で実行する。
期待: テストのコンパイルが `MaidScaleBones` / `TransformDataMaidScale` / `TransformType.MaidScale` 未定義（CS0246 / CS0117）で失敗する。

- [ ] **Step 3: `TransformType.MaidScale` を追加する**

`<P>/Timeline/TransformData/ITransformData.cs`。XML には enum の名前で保存されるので、アルファベット順の位置に挿入してよい。

```csharp
        LookAtTarget,
        MaidScale,
        Model,
```

- [ ] **Step 4: `MaidScaleBones` を作る**

`<P>/MaidManipulation/MaidScaleBones.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドスケールで拡縮できる骨 1 本分 (骨名とメニュー表示名)</summary>
    public class MaidScaleBone
    {
        public readonly string boneName;
        public readonly string displayName;

        public MaidScaleBone(string boneName, string displayName)
        {
            this.boneName = boneName;
            this.displayName = displayName;
        }
    }

    /// <summary>
    /// メイドスケール (嘘パース用の腕の拡縮) の対象骨と倍率の範囲。
    /// 並びはタイムラインのメニューと履歴スナップショットの並びを兼ねる
    /// </summary>
    public static class MaidScaleBones
    {
        public const float MinScale = 0.1f;
        public const float MaxScale = 3f;
        public const float DefaultScale = 1f;

        public static readonly List<MaidScaleBone> bones = new List<MaidScaleBone>
        {
            new MaidScaleBone("Bip01 L UpperArm", "左上腕"),
            new MaidScaleBone("Bip01 L Forearm", "左前腕"),
            new MaidScaleBone("Bip01 L Hand", "左手"),
            new MaidScaleBone("Bip01 R UpperArm", "右上腕"),
            new MaidScaleBone("Bip01 R Forearm", "右前腕"),
            new MaidScaleBone("Bip01 R Hand", "右手"),
        };

        /// <summary>骨名から対象骨を引く。対象外なら null</summary>
        public static MaidScaleBone Find(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            foreach (var bone in bones)
            {
                if (bone.boneName == boneName)
                {
                    return bone;
                }
            }
            return null;
        }

        public static float Clamp(float scale)
        {
            return Mathf.Clamp(scale, MinScale, MaxScale);
        }
    }
}
```

- [ ] **Step 5: `TransformDataMaidScale` を作る**

`<P>/Timeline/TransformData/TransformDataMaidScale.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドスケールキー 1 件分。腕の骨 1 本の均一倍率 (1 = 元の大きさ) を持ち、Tangent 補間する。
    /// 基底の scale (Vector3) と衝突しないよう、値は multiplier と呼ぶ
    /// </summary>
    public class TransformDataMaidScale : TransformDataBase
    {
        public enum Index
        {
            Multiplier = 0,
        }

        public override TransformType type => TransformType.MaidScale;

        public override int valueCount => 1;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        public TransformDataMaidScale()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るだけなので、元の大きさ (1) を既定にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            multiplier = GetDefaultCustomValue("multiplier");
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "multiplier",
                new CustomValueInfo
                {
                    index = (int)Index.Multiplier,
                    name = "倍率",
                    min = MaidScaleBones.MinScale,
                    max = MaidScaleBones.MaxScale,
                    step = 0.01f,
                    defaultValue = MaidScaleBones.DefaultScale,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData multiplierValue => values[(int)Index.Multiplier];

        public float multiplier
        {
            get => multiplierValue.value;
            set => multiplierValue.value = value;
        }

        /// <summary>元の大きさか。適用を省く判定に使う</summary>
        public bool isDefault => multiplier == MaidScaleBones.DefaultScale;
    }
}
```

- [ ] **Step 6: Transform を登録する**

`<P>/Timeline/TimelineIntegration.cs`。`TransformType.Gravity` の `RegisterTransform` の直後に追加する。

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.MaidScale,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataMaidScale>);
```

- [ ] **Step 7: csproj に追記する**

`<P>/COM3D2.SceneEditor.Plugin.csproj`。既存の `MaidGravityController.cs` と `TransformDataGravity.cs` の行の近くに入れる（並びはアルファベット順）。

```xml
    <Compile Include="MaidManipulation\MaidScaleBones.cs" />
```
```xml
    <Compile Include="Timeline\TransformData\TransformDataMaidScale.cs" />
```

- [ ] **Step 8: テストが通ることを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScaleTests"` で実行する。
期待: 2 構成のビルドが成功し、MaidScaleTests が全件 PASS する。

- [ ] **Step 9: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleBones.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMaidScale.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs
git commit -m "feat(timeline): メイドスケールのキーと対象骨の定義を追加する"
```

---

### Task 2: 倍率の状態と、複製骨への適用・復元

**Files:**
- Create: `<P>/MaidManipulation/MaidScaleState.cs`
- Create: `<P>/MaidManipulation/MaidScaleController.cs`
- Create: `<P>/Manager/MaidScaleLateUpdatePatch.cs`
- Modify: `<P>/MaidManipulation/MaidManipulateManager.cs`（フィールド宣言は `gravityController` の次、`Release` 内は `gravityController.Release(maid);` の次、`DestroyAll` 内は `gravityController.Destroy();` の次）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.cs`（`Update` 内と、`SkirtHookDriftPatch.Init();` の次）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`（追記）

**Interfaces:**
- Consumes: Task 1 の `MaidScaleBones`
- Produces:
  - `COM3D2.SceneEditor.Plugin.MaidScaleState`
    - `float Get(string boneName)`
    - `void Set(string boneName, float scale)`
    - `bool isDefault`
    - `Dictionary<string, float> nonDefaultScales`（読み取り専用で使う）
    - `static bool ShouldRestore(Vector3 current, Vector3 written)`
  - `COM3D2.SceneEditor.Plugin.MaidScaleController`
    - `float GetScale(Maid maid, string boneName)`
    - `void SetScale(Maid maid, string boneName, float scale)`
    - `bool HasState(Maid maid)`
    - `void RestoreApplied()`、`void Release(Maid maid)`、`void Destroy()`
    - `void OnBodyLateUpdateBegin(TBody body)`、`void OnBodyLateUpdateEnd(TBody body)`（パッチから呼ぶ）
  - `COM3D2.SceneEditor.Plugin.MaidScaleLateUpdatePatch.Init()`
  - `MaidManipulateManager.maidScaleController`（public フィールド）

- [ ] **Step 1: 失敗するテストを追記する**

`MaidScaleTests.cs` のクラス末尾に追加する（`using UnityEngine;` もファイル先頭に追加する）。

```csharp
        [Fact]
        public void 未設定の骨は倍率1で状態なし()
        {
            var state = new MaidScaleState();
            Assert.Equal(1f, state.Get("Bip01 L Hand"));
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 倍率を設定すると丸めて保持し1に戻すと状態なしになる()
        {
            var state = new MaidScaleState();
            state.Set("Bip01 L Hand", 10f);
            Assert.Equal(3f, state.Get("Bip01 L Hand"));
            Assert.False(state.isDefault);
            Assert.Single(state.nonDefaultScales);

            state.Set("Bip01 L Hand", 1f);
            Assert.True(state.isDefault);
            Assert.Empty(state.nonDefaultScales);
        }

        [Fact]
        public void 対象外の骨名への設定は無視する()
        {
            var state = new MaidScaleState();
            state.Set("Bip01 Head", 2f);
            state.Set(null, 2f);
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 自分が書いた値のままなら戻し他から書き換えられていたら戻さない()
        {
            var written = new Vector3(1.35f, 1.5f, 1.5f);
            Assert.True(MaidScaleState.ShouldRestore(written, written));
            // 体型スライダーなどでゲームが書き直した値を、古い退避値で潰さない
            Assert.False(MaidScaleState.ShouldRestore(new Vector3(0.9f, 1f, 1f), written));
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScaleTests"` で実行する。
期待: `MaidScaleState` 未定義でテストのコンパイルが失敗する。

- [ ] **Step 3: `MaidScaleState` を作る**

`<P>/MaidManipulation/MaidScaleState.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 体分のメイドスケール倍率。未設定の骨は 1 (元の大きさ)。
    /// 倍率 1 は保持しないので、全骨が元の大きさなら isDefault になる
    /// </summary>
    public class MaidScaleState
    {
        private readonly Dictionary<string, float> _scales = new Dictionary<string, float>();

        public bool isDefault => _scales.Count == 0;

        /// <summary>倍率が 1 でない骨だけ。呼び出し側は読むだけにする</summary>
        public Dictionary<string, float> nonDefaultScales => _scales;

        public float Get(string boneName)
        {
            float value;
            if (boneName == null || !_scales.TryGetValue(boneName, out value))
            {
                return MaidScaleBones.DefaultScale;
            }
            return value;
        }

        /// <summary>範囲へ丸めて保持する。対象外の骨名は無視する</summary>
        public void Set(string boneName, float scale)
        {
            if (MaidScaleBones.Find(boneName) == null)
            {
                return;
            }

            var clamped = MaidScaleBones.Clamp(scale);
            if (Mathf.Approximately(clamped, MaidScaleBones.DefaultScale))
            {
                _scales.Remove(boneName);
                return;
            }
            _scales[boneName] = clamped;
        }

        /// <summary>
        /// 退避値へ戻してよいか。今の値が自分の書いた値のままのときだけ戻す。
        /// 体型スライダー (BoneMorph_.Blend) などが途中で書き直した値を古い退避値で潰さないため
        /// </summary>
        public static bool ShouldRestore(Vector3 current, Vector3 written)
        {
            return current == written;
        }
    }
}
```

- [ ] **Step 4: `MaidScaleController` を作る**

`<P>/MaidManipulation/MaidScaleController.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケール (腕の骨の均一拡縮) の状態を持ち、スキニングへ反映する。
    /// メッシュは TBodySkin ごとの複製骨にスキニングされ、本体の骨のスケールはコピーされないため、
    /// 全スロットの複製骨 (SkinnedMeshRenderer.bones) へ直接掛ける。
    /// 掛けるのは TBody.LateUpdate の直後 (MaidScaleLateUpdatePatch)。
    /// それより前だと MOD (MaidVoicePitch の ForeArmFix) が前腕を書き戻し、
    /// 描画直前 (Camera.onPreCull) ではスキニングの計算に間に合わない。
    /// 次フレームの Update と TBody.LateUpdate の直前で元へ戻す。
    /// 本体の骨には触らないので、IK・ハンドル・アタッチ位置は拡縮の影響を受けない
    /// </summary>
    public class MaidScaleController
    {
        /// <summary>1 回分の書き込み記録。戻すときに使う</summary>
        private struct AppliedBone
        {
            public Transform bone;
            public Vector3 original;
            public Vector3 written;
        }

        /// <summary>メイド 1 体分の状態と複製骨のキャッシュ</summary>
        private class Entry
        {
            public readonly MaidScaleState state = new MaidScaleState();

            /// <summary>骨名 → 全スロットの複製骨</summary>
            public readonly Dictionary<string, List<Transform>> bones
                = new Dictionary<string, List<Transform>>();

            /// <summary>キャッシュを作った時点のスロット obj の並び。変わったら作り直す</summary>
            public readonly List<GameObject> slotObjects = new List<GameObject>();

            public bool isCacheValid;

            /// <summary>前回掛けた分。次に掛ける前か Update で戻す</summary>
            public readonly List<AppliedBone> applied = new List<AppliedBone>();
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>キャッシュの検証で毎回作らないよう使い回す</summary>
        private readonly List<GameObject> _slotObjectBuffer = new List<GameObject>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        public bool HasState(Maid maid)
        {
            return maid != null && _entries.ContainsKey(maid);
        }

        /// <summary>記録が無いメイドは倍率 1 として扱う</summary>
        public float GetScale(Maid maid, string boneName)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return MaidScaleBones.DefaultScale;
            }
            return entry.state.Get(boneName);
        }

        public void SetScale(Maid maid, string boneName, float scale)
        {
            if (maid == null || MaidScaleBones.Find(boneName) == null)
            {
                return;
            }

            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                // 元の大きさを書くだけなら状態を作らない (常駐コストを増やさない)
                if (Mathf.Approximately(MaidScaleBones.Clamp(scale), MaidScaleBones.DefaultScale))
                {
                    return;
                }
                entry = new Entry();
                _entries[maid] = entry;
            }

            entry.state.Set(boneName, scale);

            // 全骨が元の大きさに戻ったら状態ごと捨てる (HasState を「拡縮中か」に揃える)
            if (entry.state.isDefault)
            {
                Restore(entry);
                _entries.Remove(maid);
            }
        }

        /// <summary>
        /// 全メイドの前回分を戻す。プラグインの Update から毎フレーム呼び、
        /// ゲームのロジックにはなるべく拡縮していない骨を見せる。
        /// 解除済みのメイドの状態もここで捨てる
        /// </summary>
        public void RestoreApplied()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _deadMaids.Clear();
            foreach (var pair in _entries)
            {
                Restore(pair.Value);
                if (pair.Key == null || pair.Key.body0 == null)
                {
                    _deadMaids.Add(pair.Key);
                }
            }
            foreach (var maid in _deadMaids)
            {
                _entries.Remove(maid);
            }
        }

        /// <summary>メイド解除時。ストックの Maid は使い回されるので状態を持ち越さない</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }
            Restore(entry);
            _entries.Remove(maid);
        }

        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                Restore(entry);
            }
            _entries.Clear();
        }

        /// <summary>TBody.LateUpdate の直前。前フレームに掛けた分が残っていれば戻す</summary>
        public void OnBodyLateUpdateBegin(TBody body)
        {
            var entry = FindEntry(body);
            if (entry != null)
            {
                Restore(entry);
            }
        }

        /// <summary>
        /// TBody.LateUpdate の直後。CopyTrans と MOD の書き込みが済み、
        /// スキニングの計算より前なので、ここで掛けた値が描画に効く
        /// </summary>
        public void OnBodyLateUpdateEnd(TBody body)
        {
            var entry = FindEntry(body);
            if (entry == null)
            {
                return;
            }

            // Update が回らないフレームでも積み上げないよう、前回分を必ず戻してから掛ける
            Restore(entry);

            // 着替え・ボディ読込中は複製骨が破棄・再生成されるので触らない
            if (!body.isLoadedBody || body.maid.IsAllProcPropBusy)
            {
                entry.isCacheValid = false;
                return;
            }

            RefreshBonesIfNeeded(body, entry);

            foreach (var scalePair in entry.state.nonDefaultScales)
            {
                List<Transform> bones;
                if (!entry.bones.TryGetValue(scalePair.Key, out bones))
                {
                    continue;
                }
                foreach (var bone in bones)
                {
                    if (bone == null)
                    {
                        continue;
                    }
                    var original = bone.localScale;
                    var written = original * scalePair.Value;
                    bone.localScale = written;
                    entry.applied.Add(new AppliedBone
                    {
                        bone = bone,
                        original = original,
                        written = written,
                    });
                }
            }
        }

        private Entry FindEntry(TBody body)
        {
            if (_entries.Count == 0 || body == null || body.maid == null)
            {
                return null;
            }
            Entry entry;
            return _entries.TryGetValue(body.maid, out entry) ? entry : null;
        }

        /// <summary>自分が書いた値のまま残っている骨だけを戻し、破棄済みの骨は飛ばす</summary>
        private static void Restore(Entry entry)
        {
            var applied = entry.applied;
            for (var i = 0; i < applied.Count; i++)
            {
                var record = applied[i];
                if (record.bone != null
                    && MaidScaleState.ShouldRestore(record.bone.localScale, record.written))
                {
                    record.bone.localScale = record.original;
                }
            }
            applied.Clear();
        }

        private void RefreshBonesIfNeeded(TBody body, Entry entry)
        {
            CollectSlotObjects(body, _slotObjectBuffer);
            if (entry.isCacheValid && IsSameSlotObjects(entry.slotObjects, _slotObjectBuffer))
            {
                return;
            }

            entry.slotObjects.Clear();
            entry.slotObjects.AddRange(_slotObjectBuffer);
            RebuildBones(entry);
            entry.isCacheValid = true;
        }

        /// <summary>全スロットの obj を集める。2.5 はサブスロット (goSlot[i, j]) も含める</summary>
        private static void CollectSlotObjects(TBody body, List<GameObject> result)
        {
            result.Clear();
            // SlotID.end は番兵で goSlot に実体が無い。goSlot[int] は例外を握らないため上限を切る
            var count = Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
            for (var i = 0; i < count; i++)
            {
#if COM3D25
                var childCount = body.goSlot.CountChildren(i);
                for (var j = 0; j < childCount; j++)
                {
                    var slot = body.goSlot[i, j];
                    result.Add(slot != null ? slot.obj : null);
                }
#else
                var slot = body.GetSlot(i);
                result.Add(slot != null ? slot.obj : null);
#endif
            }
        }

        private static bool IsSameSlotObjects(List<GameObject> a, List<GameObject> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 各スロットの SkinnedMeshRenderer が実際に参照している骨から対象骨を集める。
        /// 名前で子孫を探すと、別スロットの骨や持ち物の中の同名ノードを拾うおそれがある。
        /// スロット obj 配下にない骨 (本体の骨を直接参照している場合) は書かない
        /// </summary>
        private static void RebuildBones(Entry entry)
        {
            entry.bones.Clear();
            var seen = new HashSet<Transform>();

            foreach (var slotObject in entry.slotObjects)
            {
                if (slotObject == null)
                {
                    continue;
                }
                var slotRoot = slotObject.transform;
                foreach (var renderer in slotObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = renderer.bones;
                    if (bones == null)
                    {
                        continue;
                    }
                    foreach (var bone in bones)
                    {
                        if (bone == null
                            || MaidScaleBones.Find(bone.name) == null
                            || !bone.IsChildOf(slotRoot)
                            || !seen.Add(bone))
                        {
                            continue;
                        }

                        List<Transform> list;
                        if (!entry.bones.TryGetValue(bone.name, out list))
                        {
                            list = new List<Transform>();
                            entry.bones[bone.name] = list;
                        }
                        list.Add(bone);
                    }
                }
            }
        }
    }
}
```

`#if COM3D25` は既存コード（`MaidGravityController.cs` など）と同じシンボルを使う。2.0 の `goSlot` は `List<TBodySkin>` で、`CountChildren` も `[i, j]` も無い。

- [ ] **Step 5: `MaidScaleLateUpdatePatch` を作る**

`<P>/Manager/MaidScaleLateUpdatePatch.cs`:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールを TBody.LateUpdate の前後で戻す・掛ける。
    /// LateUpdate の中で MOD (MaidVoicePitch の ForeArmFix) が前腕のスケールを書き戻し、
    /// スキニングは描画より前に計算されるため、掛けられるのは LateUpdate の直後だけ。
    /// TBody.OnLateUpdate などのイベントは 2.0 / 2.5 で型と寿命が違うため使わない
    /// </summary>
    public static class MaidScaleLateUpdatePatch
    {
        // Harmony インスタンスは他のパッチと独立させ、有効・無効判定を他パッチの状態から切り離す
        private static Harmony _harmony = null;

        /// <summary>
        /// パッチを適用する。プラグイン初期化から 1 回だけ呼ばれるが、
        /// 二重パッチは Harmony の例外になるため保険を残す
        /// </summary>
        public static void Init()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                var original = AccessTools.Method(typeof(TBody), "LateUpdate");
                if (original == null)
                {
                    throw new Exception("TBody.LateUpdate が見つかりません");
                }

                var prefix = AccessTools.Method(typeof(MaidScaleLateUpdatePatch), nameof(LateUpdatePrefix));
                var postfix = AccessTools.Method(typeof(MaidScaleLateUpdatePatch), nameof(LateUpdatePostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".MaidScale");
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                MTEUtils.LogDebug("TBody.LateUpdate のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗してもゲームは通常どおり動く。メイドスケールの見た目だけが効かない
                MTEUtils.LogError("TBody.LateUpdate のフックに失敗しました。メイドスケールは無効です");
                MTEUtils.LogException(e);
                _harmony = null;
            }
        }

        private static MaidScaleController controller
        {
            get
            {
                var manager = MaidManipulateManager.instance;
                return manager != null ? manager.maidScaleController : null;
            }
        }

        /// <summary>毎フレーム全メイド分走るため、例外は握り潰してゲーム側の更新を止めない</summary>
        private static void LateUpdatePrefix(TBody __instance)
        {
            try
            {
                var c = controller;
                if (c != null)
                {
                    c.OnBodyLateUpdateBegin(__instance);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private static void LateUpdatePostfix(TBody __instance)
        {
            try
            {
                // プラグインを無効にしている間は掛けない (戻す処理は prefix と Update で動き続ける)
                if (!ConfigManager.instance.config.pluginEnabled)
                {
                    return;
                }
                var c = controller;
                if (c != null)
                {
                    c.OnBodyLateUpdateEnd(__instance);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }
}
```

実装時に、次の 2 点を実物で確かめる:
- `MaidManipulateManager.instance` が初期化前に null を返すのか、例外を出すのか
- `ConfigManager.instance.config` を初期化前に読んでも問題ないか（`UIScaleHost` が同じ読み方をしている）

null 以外の挙動をする場合は、`SkirtHookDriftPatch` と同じく例外を握る形のまま、安全な判定に置き換える。

- [ ] **Step 6: `MaidManipulateManager` に組み込む**

`<P>/MaidManipulation/MaidManipulateManager.cs`

フィールド（`gravityController` の宣言の次）:

```csharp
        /// <summary>腕の骨の拡縮 (メイドスケール)。TBody.LateUpdate の直後に複製骨へ掛けるため常駐させる</summary>
        public MaidScaleController maidScaleController = new MaidScaleController();
```

メイド解除処理（`gravityController.Release(maid);` の次）:

```csharp
            // 腕の拡縮も持ち越さない（ストックの Maid は使い回される）
            maidScaleController.Release(maid);
```

`DestroyAll`（`gravityController.Destroy();` の次）:

```csharp
            maidScaleController.Destroy();
```

- [ ] **Step 7: プラグイン本体に組み込む**

`<P>/COM3D2.SceneEditor.Plugin.cs`

`Update` の先頭、`try {` の直後で、`if (!config.pluginEnabled)` のガードより**前**に追加する。プラグインを無効にした直後でも戻す処理を止めないため。

```csharp
                // 前フレームの TBody.LateUpdate の直後に掛けた腕の拡縮を戻す。
                // ゲームのロジック (体型・IK) にはなるべく拡縮していない骨を見せる
                MaidManipulateManager.instance.maidScaleController.RestoreApplied();
```

初期化（`SkirtHookDriftPatch.Init();` の次）:

```csharp
                // メイドスケールを TBody.LateUpdate の直後に掛ける。UI の有効状態に関係なく常時効かせる
                MaidScaleLateUpdatePatch.Init();
```

- [ ] **Step 8: csproj に追記する**

```xml
    <Compile Include="MaidManipulation\MaidScaleController.cs" />
    <Compile Include="MaidManipulation\MaidScaleState.cs" />
    <Compile Include="Manager\MaidScaleLateUpdatePatch.cs" />
```

- [ ] **Step 9: テストとビルドが通ることを確かめる**

「ビルド＆テスト」を実行する（フィルタなし）。
期待: 2 構成のビルドが成功し、全テストが PASS する。COM3D2 構成で `#else` 側がコンパイルされることも、この 2 構成ビルドで確かめる。

- [ ] **Step 10: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleState.cs \
  source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleController.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/MaidScaleLateUpdatePatch.cs \
  source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs
git commit -m "feat(maid): TBody.LateUpdate の直後に腕の複製骨へ倍率を掛ける適用器を追加する"
```

---

### Task 3: メイドスケールレイヤー

**Files:**
- Create: `<P>/Timeline/TimelineLayer/MaidScaleTimelineLayer.cs`
- Modify: `<P>/Timeline/TimelineIntegration.cs`（`GravityTimelineLayer` の RegisterLayer の直後）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs`

**Interfaces:**
- Consumes:
  - `MaidScaleBones.bones` / `MaidScaleBones.Find`
  - `TransformDataMaidScale.multiplierValue` / `multiplier` / `isDefault`
  - `MaidManipulateManager.instance.maidScaleController` の `GetScale` / `SetScale` / `HasState`
- Produces: `COM3D2.MotionTimelineEditor.Plugin.MaidScaleTimelineLayer`（`static Create(int slotNo)`）

レイヤーを外したときの復元は基底の仕組み（`CanRestoreOnRemove` 既定 true。誕生時の `UpdateFrame` の断面を `FromXml` + `CreateAndApplyAnm` で書き戻す）に任せる。`ResetOnRemove` は実装しない（`TimelineLayerResetOnRemoveTests` の期待表にも足さない）。

- [ ] **Step 1: 失敗するテストを書く**

`TimelineLayerCategoryTests.cs` の `EXPECTED` の `GravityTimelineLayer` の行の次に追加する。

```csharp
            { "MaidScaleTimelineLayer", TimelineLayerCategory.Maid },
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~TimelineLayerCategoryTests"` で実行する。
期待: 期待表のレイヤー型が実在しないため、`MaidScaleTimelineLayer` を含むアサートで FAIL する。

- [ ] **Step 3: レイヤーを作る**

`<P>/Timeline/TimelineLayer/MaidScaleTimelineLayer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 腕の骨の均一倍率 (MaidScaleController) をキー化するレイヤー。嘘パース用。
    /// 項目名は骨名 (MaidScaleBones) で、倍率は Tangent 補間する。
    /// 骨への書き込みはコントローラーが描画直前に行い、このレイヤーは倍率を渡すだけ
    /// </summary>
    [TimelineLayerDesc("メイドスケール", 19, TimelineLayerCategory.Maid)]
    public class MaidScaleTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MaidScaleTimelineLayer);
        public override string layerName => nameof(MaidScaleTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var bone in MaidScaleBones.bones)
                    {
                        _allBoneNames.Add(bone.boneName);
                    }
                }
                return _allBoneNames;
            }
        }

        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

        private MaidScaleTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MaidScaleTimelineLayer Create(int slotNo)
        {
            return new MaidScaleTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var bone in MaidScaleBones.bones)
            {
                _allMenuItems.Add(new BoneMenuItem(bone.boneName, bone.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || MaidScaleBones.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataMaidScale;
            var end = motion.end as TransformDataMaidScale;
            if (start == null || end == null)
            {
                return;
            }

            // 一度も拡縮していないメイドへ元の大きさを書いても何も変わらない
            if (!scaleController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var multiplier = PluginUtils.HermiteValue(
                motion.stFrame * timeline.frameDuration,
                motion.edFrame * timeline.frameDuration,
                start.multiplierValue,
                end.multiplierValue,
                t);
            scaleController.SetScale(maid, motion.name, multiplier);
        }

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            foreach (var bone in MaidScaleBones.bones)
            {
                var trans = CreateTransformData<TransformDataMaidScale>(bone.boneName);
                trans.multiplier = scaleController.GetScale(maid, bone.boneName);

                var frameBone = frame.CreateBone(trans);
                frame.UpdateBone(frameBone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.MaidScale;
        }
    }
}
```

- [ ] **Step 4: レイヤーを登録する**

`<P>/Timeline/TimelineIntegration.cs`。`GravityTimelineLayer` の `RegisterLayer` の直後に追加する。

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.MaidScaleTimelineLayer), MTEP.MaidScaleTimelineLayer.Create);
```

- [ ] **Step 5: csproj に追記する**

```xml
    <Compile Include="Timeline\TimelineLayer\MaidScaleTimelineLayer.cs" />
```

- [ ] **Step 6: テストが通ることを確かめる**

「ビルド＆テスト」を実行する（フィルタなし）。
期待:
- 2 構成のビルドが成功する
- `TimelineLayerCategoryTests`・`MaidLayerSlotNoTests`・`TimelineLayerResetOnRemoveTests` を含め、全件 PASS する

- [ ] **Step 7: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidScaleTimelineLayer.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs
git commit -m "feat(timeline): 腕の倍率をキー化するメイドスケールレイヤーを追加する"
```

---

### Task 4: Inspector と履歴

**Files:**
- Create: `<P>/Timeline/ItemInspector/MaidScaleItemInspector.cs`
- Create: `<P>/Manager/History/MaidScaleSnapshot.cs`
- Modify: `<P>/Manager/History/HistoryScope.cs`（enum の末尾 `TimelineItem,` の次）
- Modify: `<P>/Manager/History/SnapshotFactory.cs`（`case HistoryScope.Gravity:` の次）
- Modify: `<P>/Timeline/TimelineIntegration.cs`（`GravityItemInspector` の Register の直後）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`（追記）

**Interfaces:**
- Consumes:
  - `MaidScaleBones`
  - `MaidScaleController` の `GetScale` / `SetScale` / `HasState`
  - `GUIView.DrawSliderValue(GUIView.SliderOption)`
  - `TimelineItemClipboardMenu.DrawHeading(GUIView, string, ITimelineLayer, string)`
  - `HistoryManager.instance.BeforeEdit(Maid, HistoryScope, string)`
- Produces:
  - `MaidScaleItemInspector`（`static MaidScaleBone ResolveBone(string itemName)`）
  - `HistoryScope.MaidScale`
  - `MaidScaleSnapshot`

`HistoryScope` の enum は末尾に足す。途中に挿入すると、後ろの値の数値がずれる。`HistoryScopeUtils.RequiresMaid` は既定（true）のままでよい。

- [ ] **Step 1: 失敗するテストを追記する**

```csharp
        [Fact]
        public void Inspectorは項目名から対象骨を引き未知の名前ではnullを返す()
        {
            Assert.Equal("左前腕",
                MaidScaleItemInspector.ResolveBone("Bip01 L Forearm").displayName);
            Assert.Null(MaidScaleItemInspector.ResolveBone("hair"));
        }

        [Fact]
        public void 履歴スコープにメイドスケールがありメイドが必須()
        {
            Assert.True(HistoryScopeUtils.RequiresMaid(HistoryScope.MaidScale));
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScaleTests"` で実行する。
期待: `MaidScaleItemInspector` / `HistoryScope.MaidScale` 未定義でテストのコンパイルが失敗する。

- [ ] **Step 3: `HistoryScope.MaidScale` を追加する**

`<P>/Manager/History/HistoryScope.cs` の enum 末尾（`TimelineItem,` の次）に追加する。

```csharp
        /// <summary>メイドスケール (腕の骨の倍率)</summary>
        MaidScale,
```

- [ ] **Step 4: スナップショットを作る**

`<P>/Manager/History/MaidScaleSnapshot.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドスケールのスナップショット (骨ごとの倍率)</summary>
    public class MaidScaleSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        /// <summary>MaidScaleBones.bones と同じ並び</summary>
        private float[] _scales;

        private static MaidScaleController controller
            => MaidManipulateManager.instance.maidScaleController;

        public static MaidScaleSnapshot Capture(Maid maid)
        {
            var bones = MaidScaleBones.bones;
            var scales = new float[bones.Count];
            for (var i = 0; i < bones.Count; i++)
            {
                scales[i] = controller.GetScale(maid, bones[i].boneName);
            }

            return new MaidScaleSnapshot
            {
                _capturedMaid = maid,
                _scales = scales,
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            var bones = MaidScaleBones.bones;
            for (var i = 0; i < bones.Count && i < _scales.Length; i++)
            {
                controller.SetScale(maid, bones[i].boneName, _scales[i]);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaidScaleSnapshot;
            if (o == null || o._scales.Length != _scales.Length)
            {
                return false;
            }
            for (var i = 0; i < _scales.Length; i++)
            {
                if (!Mathf.Approximately(_scales[i], o._scales[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
```

`SetScale` は「状態が無いメイドに倍率 1 を書く」場合に状態を作らない。そのため、重力のような既定値判定は要らない。

`<P>/Manager/History/SnapshotFactory.cs`（`case HistoryScope.Gravity:` の 2 行の次）:

```csharp
                case HistoryScope.MaidScale:
                    return MaidScaleSnapshot.Capture(maid);
```

- [ ] **Step 5: Inspector を作る**

`<P>/Timeline/ItemInspector/MaidScaleItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールレイヤーのメニュー項目 → 骨の倍率スライダー。
    /// 値は MaidScaleController を通して読み書きし、操作は履歴に記録する
    /// </summary>
    public class MaidScaleItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

        /// <summary>メニュー項目名 (骨名) から対象骨を求める。対象外なら null</summary>
        public static MaidScaleBone ResolveBone(string itemName)
        {
            return MaidScaleBones.Find(itemName);
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var item in items)
            {
                var bone = ResolveBone(item.name);
                if (bone == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの骨の行か分かるよう見出しを出す
                TimelineItemClipboardMenu.DrawHeading(view, bone.displayName, layer, item.name);
                DrawScaleSlider(view, maid, bone);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // メイドスケールに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }

        private static void DrawScaleSlider(GUIView view, Maid maid, MaidScaleBone bone)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "倍率",
                labelWidth = 40,
                width = -1,
                min = MaidScaleBones.MinScale,
                max = MaidScaleBones.MaxScale,
                step = 0.01f,
                defaultValue = MaidScaleBones.DefaultScale,
                value = scaleController.GetScale(maid, bone.boneName),
                onChanged = newValue =>
                {
                    // ドラッグ中の連続変更は 1 件に集約される
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.MaidScale,
                        "メイドスケール: " + bone.displayName);
                    scaleController.SetScale(maid, bone.boneName, newValue);
                },
            });
        }
    }
}
```

- [ ] **Step 6: Inspector を登録する**

`<P>/Timeline/TimelineIntegration.cs`（`GravityItemInspector` の Register の直後）:

```csharp
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.MaidScaleTimelineLayer), new MaidScaleItemInspector());
```

- [ ] **Step 7: csproj に追記する**

```xml
    <Compile Include="Manager\History\MaidScaleSnapshot.cs" />
    <Compile Include="Timeline\ItemInspector\MaidScaleItemInspector.cs" />
```

- [ ] **Step 8: テストが通ることを確かめる**

「ビルド＆テスト」を実行する（フィルタなし）。
期待: 2 構成のビルドが成功し、全件 PASS する。

- [ ] **Step 9: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MaidScaleItemInspector.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/History/MaidScaleSnapshot.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/History/SnapshotFactory.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs
git commit -m "feat(inspector): メイドスケールの倍率スライダーと履歴を追加する"
```

---

### Task 5: 実機検証と互換メモ

**Files:**
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の節の末尾）

DLL を実機へ反映するには、ゲームを止めてから入れ替える必要がある。**com3d25-devbridge:restart-verify** スキルの手順に従う（ゲーム終了 → DLL 更新 → 起動 → セーブのロード）。検証は通常シーン（デイリー画面でエディタを有効にした状態）で行い、撮影モードは使わない。

- [ ] **Step 1: 実機へ反映して起動する**

restart-verify スキルで、COM3D25 構成の DLL を反映してゲームを起動する。

- [ ] **Step 2: 旧ボディのメイドで 6 本それぞれが効くことを確かめる**

1. タイムラインにメイドスケールレイヤーを追加する
2. Inspector で「左手」の倍率を 1.5 にする
3. devbridge の `eval_csharp` で、body スロットの複製骨のスケールを、`TBody.LateUpdate` の後の時点で読む
   - onPreCull に一度だけ記録するデリゲートを登録する。onPreCull は全メイドの LateUpdate より後なので、掛かった値が読める
   - ただし、見た目に効くかどうかは onPreCull の値では分からない。必ず次の 4 の screenshot で判定する

```csharp
var m = GameMain.Instance.CharacterMgr.GetMaid(0);
var smr = m.body0.goSlot[0].obj.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>(true);
var hand = System.Array.Find(smr.bones, b => b != null && b.name == "Bip01 L Hand");
var log = new System.Collections.Generic.List<string>();
UnityEngine.Camera.CameraCallback cb = null;
cb = c => { UnityEngine.Camera.onPreCull -= cb; log.Add(hand.localScale.ToString("F3")); };
UnityEngine.Camera.onPreCull += cb;
"armed"
```

次の eval で `string.Join(",", log.ToArray())` を読む。
期待: `(1.500, 1.500, 1.500)`（元が 1 の場合）。

4. screenshot で、左手が倍率 1 のときより大きく見えることを確かめる。比較のため、倍率 1 の画像も同じ範囲で撮る
5. 同じことを左右の上腕・前腕・手の 6 本すべてで確かめる
   - **前腕**はこの環境では MaidVoicePitch が毎フレーム書き戻す。必ず screenshot で効いていることを確かめる
   - 同じ onPreCull の記録の中で「掛けた値 ÷ 1.5」が、MaidVoicePitch の書いた値（倍率 1 のときの同じ時点の値）と一致することも確かめる
6. eval の中で直接読んだ値（eval はプラグインの Update より後、LateUpdate より前に走る）が、元の値に戻っていることも確かめる
7. UI を無効にして（プラグインのトグルキーで `isEnable` を false にする）、数秒待ってから 3 を繰り返す。倍率が積み上がらず 1.5 のままであることを確かめる

- [ ] **Step 3: 体型スライダーで倍率が積み上がらないことを確かめる**

1. 「左上腕」を 1.5 にした状態で、エディット画面の体型スライダー（腕の長さ・身長など）を動かす
2. 倍率を 1 に戻す
3. 複製骨 `Bip01 L UpperArm` の `localScale` が、スライダー操作後の素の値（倍率なしで同じスライダー値にしたときの値）と一致することを確かめる

- [ ] **Step 4: 着替え・ボディ再ロード後も効くことを確かめる**

1. 倍率 1.5 のまま衣装を着替える。完了後も効いていて、`tail_log` に例外が出ていないことを確かめる
2. ボディを再ロードして、同じことを確かめる

- [ ] **Step 5: タイムラインの操作を確かめる**

- 0 フレームに倍率 1、30 フレームに 2 のキーを打って再生し、途中の値が補間されること
- ポーズ編集を開始・終了しても値が飛ばないこと
- Undo / Redo で倍率が戻ること
- レイヤーを削除すると、元の大きさへ戻ること
- 保存して読み直しても、キーが残ること

- [ ] **Step 6: 新ボディ（CRC）のメイドで Step 2 を繰り返す**

`maid.IsCrcBody` が true のメイドを呼び出して、6 本それぞれが効くかを確かめる。サブスロット（`goSlot[i, j > 0]`）を持つ衣装（新スロットの outerwear / jacket など）を着せた状態でも、その衣装の腕が一緒に拡縮されることを確かめる。

効かない骨があれば、まず調べる:
- 複製骨の階層に `UpperArm_SCL_` などの中間骨があるか
- その骨が `SkinnedMeshRenderer.bones` に入っているか
- `TMorphBone.LinkedBone` が毎フレーム書き直していないか

結果はユーザーに報告し、対応方針（対象骨の名前を CRC 用に切り替えるなど）を相談する。この計画の範囲で勝手に仕様を広げない。

- [ ] **Step 7: CLAUDE.md の互換メモに 1 行足す**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」の節の末尾に追加する。

```markdown
- メイドスケールレイヤー（`MaidScaleTimelineLayer`、腕 6 本の均一倍率、`TransformType.MaidScale`）は SE 独自。version は上げていない。MTE は未登録のレイヤーとしてエラーログを出して読み飛ばすため、腕は元の大きさで表示される
```

このファイルは SceneEditor リポジトリの外（`W:\COM3D2_5\work\`）にある。git 管理されていないなら、コミットは不要。

- [ ] **Step 8: 検証結果を記録する**

実機検証の結果（各ステップの成否、CRC での挙動）を、実行時の台帳とユーザーへの報告にまとめる。コード変更が生じた場合は、内容に合わせて `fix(maid): ...` などでコミットする。

## レビュー却下メモ

- 複数カメラでスキニングが共有されるかの検証を足す — 方式を `TBody.LateUpdate` の postfix へ変え、メイドごとに 1 フレーム 1 回だけ掛けるようにしたので、カメラの台数は関係なくなった
- 破棄済みの Maid をキーにした Dictionary 操作の挙動を確認する — `MaidGravityController` と同じ扱い（`maid == null || maid.body0 == null` を Update で集めて捨てる）にそろえた。実害の報告もないので、未確認のまま見送り
- Inspector の `BeforeEdit` の呼び方を `GravityRowDrawer` と照合する — 同じ 3 引数のオーバーロードで、onChanged ごとに呼ぶ形も同じ。確認済みで変更不要
- 復元を onPostRender（カメラごと）に置く — onPreCull・onPostRender ではスキニングに間に合わないことを実機で確認したため、この案は成り立たない
