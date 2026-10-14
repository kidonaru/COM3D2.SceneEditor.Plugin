# カメラの身長補正（項目キー化）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（CLAUDE.md の方針で subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 身長補正の基準メイドを、カメラレイヤーの独立した項目 `身長補正` のキーで持つ形に作り直す。区間内は 2 人の補正結果を線形補間する。

**Architecture:**
- 初版（同じブランチの `2026-10-12-camera-height-correction.md` で実装済み）の、タイムライン単位の 3 設定を置き換える
- 補間の数式は Unity 非依存の `CameraHeightCorrection` に、ライブ値と補間の状態は Unity 非依存の `CameraHeightState` に置いて単体テストする
- キーは新しい `TransformDataCameraHeight`。手ブレと同じく `CameraTimelineLayer.UpdateFrame` でカメラキーと一緒に書く
- 基準身長は `TimelineData` にスロットごとに持つ

**Tech Stack:** C#（Unity 5.6 / Mono、COM3D2 と COM3D25 の 2 構成ビルド）、xUnit（`COM3D2.SceneEditor.Plugin.Tests`）、com3d25-devbridge（実機確認）

**Spec:** `docs/superpowers/specs/2026-10-12-camera-height-correction-design.md`（c51d6e3 で改訂済み）

## Global Constraints

- **キーの値**: 基準メイドのスロット番号だけ（値数 1、-1 は補正なし、タンジェントなし）。基準身長はキーに入れない
- **基準身長**: タイムライン単位でスロットごとに持つ。未設定のときだけ、UI でそのスロットを選んだときに取り込む。設定済みの値を上書きするのは `取得` ボタンと手入力だけ。履歴（Undo）の管理外で、Undo/Redo でも今の値を引き継ぐ（シェーダー変更 `materialShaders` と同じ扱い）
- **キーの書き方**: 身長補正のキーは、UI で基準メイドを、キーで当たっている値（キーが無ければ -1）から変えたときだけ書く（`CameraHeightState.isLiveChanged`）。編集開始時のスナップショットには身長補正のボーンを入れない。補正を使わないタイムラインには身長補正のキーが入らない（新規レイヤーのフレーム 0 にも入らない）
- **ライブ値のリセット**: タイムラインの新規作成・読込・アンロード（`TimelineManager.ResetTimelineState`）でだけ -1 に戻す。Undo/Redo では戻さない
- **1 人ぶんの補正**: `s_k = 今の身長 / 基準身長`、`Correct_k(y) = r_k + (y − r_k)·s_k`。基準メイド -1・基準身長が未設定または 0 以下・メイドなし・身長 0 以下では補正しない（倍率 1）。ボディのロード状態は条件にしない
- **補間**: `y' = (1 − w)·Correct_A(y) + w·Correct_B(y)`、w は区間の進み具合（線形、0〜1）。スロット番号は補間しない
- **逆補間**: `S = (1 − w)·s_A + w·s_B`、`R = (1 − w)·r_A·(1 − s_A) + w·r_B·(1 − s_B)`、`y = (y' − R) / S`
- **適用順**: 手ブレ → 身長補正 → カメラ。キーが無いフレームはライブ値を使う
- **補正の対象**: 追従なしのカメラキーの注視点の Y だけ。追従キー・XZ・距離・FOV・ロール・サブカメラ・カメラ構図のプリセットは対象外
- **身長**: props からゲームのプロフィールと同じ式で求める（初版の `MaidHeightReader.GetHeight` をそのまま使う）
- **XML**: キーは `TransformType.CameraHeight`。基準身長はルートの `<CameraHeightBases>`（`<CameraHeightBase>` ごとに `MaidSlotNo` / `Height`）で、空なら書き出さない（`*Specified` を空の setter 付きで使う。`ShouldSerialize*` は COM3D2 2.0 の Mono が見ないため使わない）。version は上げない
- **初版の要素**: `IsCameraHeightCorrection` / `CameraHeightMaidSlotNo` / `CameraHeightBase` は削除する。未リリースのため読込互換は持たない
- **新規ファイル**: 本体の csproj はファイルを明示列挙しているため、新規の .cs は必ず `<Compile Include>` に足す
- **コメント**: 日本語。原則「なぜ他の案を採らなかったか」だけを書く
- **テスト**: Unity のネイティブ呼び出し（`Quaternion.Euler` 等）はできない。`Mathf` も避け、純粋な C# で書く

## Review Focus

1. **基準メイドのスロットにメイドがいない**（キーが参照するスロット・ライブ値のどちらでも）
   - 期待: 例外を出さず倍率 1（その側は補正なし）で再生される。UI には補正が無効だと警告が出る
   - 確かめる場所: Task 1（倍率 1 の側は補正なしでつながるテスト）、Task 6 の実機確認
2. **編集モードに入る前に UI で基準メイドを選び、編集モードでカメラだけ動かして差分登録した**
   - 期待: 身長補正のキーも書かれる。書かれないと、保存して開き直したときに補正が外れる
   - 確かめる場所: Task 2（`isLiveChanged` のテスト）、Task 5（スナップショットに身長補正のボーンを入れない）、Task 6 の実機確認
3. **身長補正のキーを全部消した**
   - 期待: 前に適用したキーの状態が残らず、ライブ値で補正される
   - 確かめる場所: Task 2（`BeginApply` のテスト）、Task 5（`ApplyPlayData` で毎回戻す）
4. **別のメイドで開いたタイムラインで、基準メイドを選び直す・キーを打つ・Undo する**
   - 期待: 設定済みの基準身長は上書きされず、Undo でも巻き戻らない。Undo で基準メイドのライブ値が「なし」に戻らない
   - 確かめる場所: Task 4（`SetCameraHeightBaseIfUnset` と差分比較から外すテスト）、Task 6 の実機確認
5. **旧 XML（初版 SE の 3 要素を含む XML・MTE 産の XML）**
   - 期待: 例外を出さずに読め、基準身長は空（補正なし）になる
   - 確かめる場所: Task 4 のテスト

テスト実行コマンド（リポジトリ直下から。COM3D2 → COM3D25 → dotnet test の順が必須。以降この名前で呼ぶ）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
P=source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
"$MSB" $P /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m -nologo \
 && "$MSB" $P /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" -v:m -nologo \
 && dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~<テストクラス名>"
```

filter を外すと全テストを走らせる。debug.bat はゲーム停止中に実機へ DLL をコピーするので使わない。

---

### Task 1: 2 人の補正の補間と逆補間（純粋関数）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs`

**Interfaces:**
- Consumes: 既存の `CameraHeightCorrection.Correct(float y, float rootY, float scale)`
- Produces:
  - `public static float CameraHeightCorrection.Blend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)`
  - `public static float CameraHeightCorrection.Unblend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)`

- [ ] **Step 1: 失敗するテストを書く**

`CameraHeightCorrectionTests` の末尾（`補正と逆補正は往復で元に戻る` の後）に追加する:

```csharp
        [Fact]
        public void 補間の端はそれぞれのメイドの補正に一致する()
        {
            var a = CameraHeightCorrection.Correct(1.5f, 0.2f, 0.8f);
            var b = CameraHeightCorrection.Correct(1.5f, -0.1f, 1.2f);
            Assert.Equal(a, CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, -0.1f, 1.2f, 0f), 5);
            Assert.Equal(b, CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, -0.1f, 1.2f, 1f), 5);
        }

        [Fact]
        public void 補正しない側は倍率1としてつながる()
        {
            var a = CameraHeightCorrection.Correct(1.5f, 0.2f, 0.8f);
            Assert.Equal(0.5f * a + 0.5f * 1.5f,
                CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, 123f, 1f, 0.5f), 5);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(1f)]
        public void 全員の倍率が1なら補間しても動かない(float w)
        {
            Assert.Equal(1.5f, CameraHeightCorrection.Blend(1.5f, 0.2f, 1f, -0.4f, 1f, w), 5);
        }

        [Theory]
        [InlineData(1.4f, 0f, 0.9f, 0.5f, 1.1f, 0f)]
        [InlineData(1.4f, 0f, 0.9f, 0.5f, 1.1f, 0.35f)]
        [InlineData(-0.3f, 0.5f, 1.12f, -1f, 0.85f, 0.8f)]
        [InlineData(2.0f, -1f, 1f, 0.3f, 1.3f, 1f)]
        public void 補間と逆補間は往復で元に戻る(
            float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            Assert.Equal(y, CameraHeightCorrection.Unblend(
                CameraHeightCorrection.Blend(y, rootA, scaleA, rootB, scaleB, w),
                rootA, scaleA, rootB, scaleB, w), 4);
            Assert.Equal(y, CameraHeightCorrection.Blend(
                CameraHeightCorrection.Unblend(y, rootA, scaleA, rootB, scaleB, w),
                rootA, scaleA, rootB, scaleB, w), 4);
        }
```

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraHeightCorrectionTests`）。Expected: `Blend` / `Unblend` が無く、テストプロジェクトのコンパイルが失敗する。

- [ ] **Step 3: 実装する**

`CameraHeightCorrection` の `Uncorrect` の後に追加する（`Uncorrect` は Task 5 で消すので、ここでは残す）:

```csharp
        /// <summary>
        /// 2 人ぶんの補正を w (0〜1) で線形補間した Y。補正しない側は scale に 1 を渡す
        /// </summary>
        public static float Blend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            return (1f - w) * Correct(y, rootA, scaleA) + w * Correct(y, rootB, scaleB);
        }

        /// <summary>Blend の逆。scale はどちらも正を前提とする</summary>
        public static float Unblend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            var slope = (1f - w) * scaleA + w * scaleB;
            var offset = (1f - w) * rootA * (1f - scaleA) + w * rootB * (1f - scaleB);
            return (y - offset) / slope;
        }
```

- [ ] **Step 4: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `CameraHeightCorrectionTests`）。Expected: 全件 PASS。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): 身長補正の 2 人ぶんの補間と逆補間を追加する` などのメッセージにする。

---

### Task 2: ライブ値と補間の状態

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Timeline\MaidHeightReader.cs" />` の直後に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightStateTests.cs`

**Interfaces:**
- Produces（namespace `COM3D2.MotionTimelineEditor.Plugin`）:
  - `public class CameraHeightState`（`public CameraHeightState()`、`public static CameraHeightState instance`）
  - `public const int NoSlot = -1;`
  - `public int liveSlotNo { get; }` / `public int slotA { get; }` / `public int slotB { get; }` / `public float weight { get; }`
  - `public int appliedSlotNo { get; }`（キーで当たっている基準メイド。キーが当たっていなければ -1）
  - `public bool isLiveChanged { get; }`（`liveSlotNo != appliedSlotNo`）
  - `public void SetLive(int slotNo)` / `public void BeginApply()` / `public void ApplyKeys(int startSlotNo, int endSlotNo, float t)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightStateTests
    {
        [Fact]
        public void 初期状態は補正なし()
        {
            var state = new CameraHeightState();
            Assert.Equal(CameraHeightState.NoSlot, state.liveSlotNo);
            Assert.Equal(CameraHeightState.NoSlot, state.slotA);
            Assert.Equal(CameraHeightState.NoSlot, state.slotB);
            Assert.Equal(0f, state.weight);
        }

        [Fact]
        public void ライブ値を選ぶと補間なしでそのメイドになる()
        {
            var state = new CameraHeightState();
            state.SetLive(2);
            Assert.Equal(2, state.liveSlotNo);
            Assert.Equal(2, state.slotA);
            Assert.Equal(2, state.slotB);
            Assert.Equal(0f, state.weight);
        }

        [Fact]
        public void キーの区間を適用すると始点と終点を補間しライブ値は始点になる()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 0.25f);
            Assert.Equal(0, state.slotA);
            Assert.Equal(1, state.slotB);
            Assert.Equal(0.25f, state.weight);
            Assert.Equal(0, state.liveSlotNo);
        }

        [Fact]
        public void 区間の終端ではライブ値は終点になる()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 1f);
            Assert.Equal(1f, state.weight);
            Assert.Equal(1, state.liveSlotNo);
            Assert.Equal(1, state.appliedSlotNo);
        }

        [Theory]
        [InlineData(-0.5f, 0f)]
        [InlineData(1.5f, 1f)]
        public void 区間の進み具合は0から1に収める(float t, float expected)
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, t);
            Assert.Equal(expected, state.weight);
        }

        [Fact]
        public void 適用を始めると前に適用したキーの補間が残らない()
        {
            var state = new CameraHeightState();
            state.SetLive(3);
            state.ApplyKeys(0, 1, 0.5f);
            state.BeginApply();
            Assert.Equal(0, state.slotA);
            Assert.Equal(0, state.slotB);
            Assert.Equal(0f, state.weight);
            Assert.Equal(CameraHeightState.NoSlot, state.appliedSlotNo);
        }

        [Fact]
        public void キーで当たっている値のままなら変更なし()
        {
            var state = new CameraHeightState();
            state.BeginApply();
            state.ApplyKeys(0, 1, 0.5f);
            Assert.Equal(0, state.appliedSlotNo);
            Assert.False(state.isLiveChanged);
        }

        [Fact]
        public void キーが無いまま補正なしなら変更なし()
        {
            var state = new CameraHeightState();
            state.BeginApply();
            Assert.False(state.isLiveChanged);
        }

        [Fact]
        public void UIで選び直すと変更あり()
        {
            var state = new CameraHeightState();
            state.BeginApply();
            state.ApplyKeys(0, 1, 0.5f);
            state.SetLive(2);
            Assert.True(state.isLiveChanged);
            Assert.Equal(0, state.appliedSlotNo);
        }

        [Fact]
        public void キーが無いときにUIで選ぶと変更あり()
        {
            var state = new CameraHeightState();
            state.BeginApply();
            state.SetLive(1);
            Assert.True(state.isLiveChanged);
        }
    }
}
```

`適用を始めると前に適用したキーの補間が残らない` の期待値が 3 ではなく 0 なのは、`ApplyKeys` がライブ値を区間の始点（0）に書き換えるため（spec「再生でキーを適用すると、ライブ値も区間の始点のキーの値になる」）。

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraHeightStateTests`）。Expected: `CameraHeightState` が無く、テストプロジェクトのコンパイルが失敗する。

- [ ] **Step 3: 実装して csproj に登録する**

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正のライブ値 (UI で選ぶ基準メイド) と、再生で決まった補間の状態。
    /// 補正するのは slotA と slotB の補正結果を weight で補間した値
    /// </summary>
    public class CameraHeightState
    {
        public const int NoSlot = -1;

        private static CameraHeightState _instance;
        public static CameraHeightState instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraHeightState();
                }
                return _instance;
            }
        }

        public int liveSlotNo { get; private set; } = NoSlot;
        public int slotA { get; private set; } = NoSlot;
        public int slotB { get; private set; } = NoSlot;
        public float weight { get; private set; }

        /// <summary>キーで当たっている基準メイド。キーが当たっていなければ NoSlot</summary>
        public int appliedSlotNo { get; private set; } = NoSlot;

        /// <summary>UI で、キーで当たっている値から基準メイドを変えたか。変えたときだけ身長補正のキーを書く</summary>
        public bool isLiveChanged => liveSlotNo != appliedSlotNo;

        /// <summary>UI で基準メイドを選ぶ。キーで当たっている値 (appliedSlotNo) は変えない</summary>
        public void SetLive(int slotNo)
        {
            liveSlotNo = slotNo;
            slotA = slotNo;
            slotB = slotNo;
            weight = 0f;
        }

        /// <summary>
        /// キーの適用を始める。補間をライブ値へ戻し、キーで当たっている値を消す。
        /// 続く ApplyKeys が呼ばれなければ (キーが無い・最初のキーより前)、ライブ値で補正する
        /// </summary>
        public void BeginApply()
        {
            slotA = liveSlotNo;
            slotB = liveSlotNo;
            weight = 0f;
            appliedSlotNo = NoSlot;
        }

        /// <summary>t は区間の進み具合 (0〜1、範囲外は収める)</summary>
        public void ApplyKeys(int startSlotNo, int endSlotNo, float t)
        {
            slotA = startSlotNo;
            slotB = endSlotNo;
            weight = t < 0f ? 0f : (t > 1f ? 1f : t);
            liveSlotNo = weight >= 1f ? endSlotNo : startSlotNo;
            appliedSlotNo = liveSlotNo;
        }
    }
}
```

csproj の `<Compile Include="Timeline\MaidHeightReader.cs" />` の直後に `<Compile Include="Timeline\CameraHeightState.cs" />` を追加する。

- [ ] **Step 4: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `CameraHeightStateTests`）。Expected: 全件 PASS。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): 身長補正のライブ値と補間の状態を追加する` などのメッセージにする。

---

### Task 3: 身長補正のキー（TransformDataCameraHeight）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCameraHeight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（`enum TransformType` の `Camera,` の直後に `CameraHeight,`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（`CameraShake` の `RegisterTransform` の直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Timeline\TransformData\TransformDataCameraShake.cs" />` の直後）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformDataCameraHeightTests.cs`

**Interfaces:**
- Produces:
  - `TransformType.CameraHeight`
  - `public class TransformDataCameraHeight : TransformDataBase`（`public int maidSlotNo { get; set; }`、値数 1）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TransformDataCameraHeightTests
    {
        private static TransformDataCameraHeight Create()
        {
            var trans = new TransformDataCameraHeight();
            trans.Initialize("height");
            return trans;
        }

        [Fact]
        public void 値は1個でタンジェントを持たない()
        {
            var trans = Create();
            Assert.Equal(TransformType.CameraHeight, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.False(trans.hasPosition);
            Assert.False(trans.hasEulerAngles);
            Assert.False(trans.hasScale);
            Assert.False(trans.hasTangent);
        }

        [Fact]
        public void 基準メイドの既定は補正なし()
        {
            Assert.Equal(-1f, Create().GetCustomValueInfo("maidSlotNo").defaultValue);
        }

        [Fact]
        public void 基準メイドはXML往復で保持される()
        {
            var trans = Create();
            trans.maidSlotNo = 2;

            var loaded = Create();
            loaded.FromXml(trans.ToXml());
            Assert.Equal(2, loaded.maidSlotNo);
        }
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `TransformDataCameraHeightTests`）。Expected: `TransformDataCameraHeight` が無く、テストプロジェクトのコンパイルが失敗する。

- [ ] **Step 3: enum に追加し、キーのクラスを作る**

`ITransformData.cs` の `enum TransformType` で、`Camera,` の直後（`CameraShake,` の前）に `CameraHeight,` を足す。enum はアルファベット順に並んでいる。XML には enum 名で書かれるため、途中に足しても既存データの値は変わらない。

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正のキー。基準メイドのスロット番号だけを持ち (-1 は補正なし)、
    /// 区間内は CameraTimelineLayer が 2 人の補正結果を線形補間する
    /// </summary>
    public class TransformDataCameraHeight : TransformDataBase
    {
        public override TransformType type => TransformType.CameraHeight;

        public enum Index
        {
            MaidSlotNo = 0,
        }

        public override int valueCount => 1;

        public override bool hasPosition => false;
        public override bool hasEulerAngles => false;
        public override bool hasScale => false;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap
            = new Dictionary<string, CustomValueInfo>
        {
            {
                "maidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.MaidSlotNo,
                    name = "基準メイド",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData maidSlotNoValue => values[(int)Index.MaidSlotNo];

        public int maidSlotNo
        {
            get => maidSlotNoValue.intValue;
            set => maidSlotNoValue.intValue = value;
        }

        public TransformDataCameraHeight()
        {
        }
    }
}
```

`hasTangent` は基底の既定（false）のままにする。スロット番号はタンジェントで補間する値ではないため。

`TimelineIntegration.cs` の `CameraShake` の登録の直後に追加する:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.CameraHeight,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataCameraHeight>);
```

csproj の `<Compile Include="Timeline\TransformData\TransformDataCameraShake.cs" />` の直後に `<Compile Include="Timeline\TransformData\TransformDataCameraHeight.cs" />` を追加する。

- [ ] **Step 4: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `TransformDataCameraHeightTests`）。Expected: 全件 PASS。`GetCustomValueInfo` の名前が違ってコンパイルが通らない場合は、`TransformDataCameraShakeTests` で使っている呼び方に合わせる。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): 身長補正のキーの型を追加する` などのメッセージにする。

---

### Task 4: スロットごとの基準身長と XML

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`TimelineMaidBodySliderKeyXml` の後にクラスを追加、`maidBodySliderKeys` の後に要素を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`cameraHeightBase` 宣言の後に追加、`FromXml` / `ToXml`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXmlDiff.cs`（`SerializeWithoutLayers`、`materialShaders` を外している箇所）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（`UpdateTimeline` の先頭、`materialShaders` を引き継いでいる箇所）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightBaseTests.cs`

初版の 3 項目は Task 5 で消す。このタスクでは残したまま追加する（`CameraTimelineLayer` と UI がまだ参照しているため）。

**Interfaces:**
- Produces:
  - `public class TimelineCameraHeightBaseXml { public int maidSlotNo; public int height; }`（`MaidSlotNo` / `Height`）
  - `TimelineXml.cameraHeightBases`（`List<TimelineCameraHeightBaseXml>`、`<CameraHeightBases>` / `<CameraHeightBase>`）
  - `TimelineData.cameraHeightBases`（`Dictionary<int, int>`）
  - `public int TimelineData.GetCameraHeightBase(int slotNo)`（未設定は 0）
  - `public void TimelineData.SetCameraHeightBase(int slotNo, int height)`（0 以下は無視）
  - `public void TimelineData.SetCameraHeightBaseIfUnset(int slotNo, int height)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightBaseTests
    {
        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 未設定のスロットの基準身長は0()
        {
            Assert.Equal(0, new TimelineData().GetCameraHeightBase(1));
        }

        [Fact]
        public void 基準身長はスロットごとに持つ()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            data.SetCameraHeightBase(1, 145);
            Assert.Equal(160, data.GetCameraHeightBase(0));
            Assert.Equal(145, data.GetCameraHeightBase(1));
        }

        [Fact]
        public void 0以下の身長は設定しない()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            data.SetCameraHeightBase(0, 0);
            Assert.Equal(160, data.GetCameraHeightBase(0));
        }

        [Fact]
        public void 未設定のときだけ取り込む()
        {
            var data = new TimelineData();
            data.SetCameraHeightBaseIfUnset(0, 160);
            data.SetCameraHeightBaseIfUnset(0, 140);
            Assert.Equal(160, data.GetCameraHeightBase(0));
        }

        [Fact]
        public void 基準身長が空なら要素を書き出さない()
        {
            var text = Serialize(new TimelineData().ToXml());
            Assert.DoesNotContain("CameraHeightBases", text);
        }

        [Fact]
        public void 基準身長はXML往復で保持される()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(1, 145);
            data.SetCameraHeightBase(0, 160);
            var text = Serialize(data.ToXml());
            Assert.Contains("<CameraHeightBases>", text);

            var loaded = new TimelineData();
            loaded.FromXml(Deserialize(text));
            Assert.Equal(160, loaded.GetCameraHeightBase(0));
            Assert.Equal(145, loaded.GetCameraHeightBase(1));
        }

        [Fact]
        public void 要素の無いXMLは基準身長が空で読む()
        {
            var loaded = new TimelineData();
            loaded.SetCameraHeightBase(0, 150);
            loaded.FromXml(Deserialize(Serialize(new TimelineXml())));
            Assert.Equal(0, loaded.GetCameraHeightBase(0));
        }

        [Fact]
        public void 初版の要素を含むXMLも読める()
        {
            var text = Serialize(new TimelineXml()).Replace(
                "</TimelineData>",
                "<IsCameraHeightCorrection>true</IsCameraHeightCorrection>"
                + "<CameraHeightMaidSlotNo>0</CameraHeightMaidSlotNo>"
                + "<CameraHeightBase>160</CameraHeightBase></TimelineData>");
            Assert.Contains("<IsCameraHeightCorrection>", text);
            var loaded = new TimelineData();
            loaded.FromXml(Deserialize(text));
            Assert.Equal(0, loaded.GetCameraHeightBase(0));
        }

        [Fact]
        public void 基準身長はUndoの差分比較に含めない()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            var xml = data.ToXml();
            Assert.DoesNotContain("CameraHeightBases", TimelineXmlDiff.SerializeWithoutLayers(xml));
            // 比較用に一時的に外すだけで、xml 自体からは消さない
            Assert.Single(xml.cameraHeightBases);
        }
    }
}
```

`TimelineXmlDiff` が別の名前空間にある場合は `using` を足す（`rg -n "class TimelineXmlDiff" -B5 source` で確かめる）。

`初版の要素を含むXMLも読める` は、Task 5 で初版の項目を消した後に意味を持つ（未知の要素を `XmlSerializer` が読み飛ばすことを固定する）。Task 4 の時点でも通る。ルート要素は `[XmlRoot("TimelineData")]`（`TimelineXml.cs:241`）なので閉じタグは `</TimelineData>`。置換が空振りしていないことを `Assert.Contains` で確かめている。

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraHeightBaseTests`）。Expected: `GetCameraHeightBase` などが無く、コンパイルが失敗する。

- [ ] **Step 3: `TimelineXml` に要素を足す**

`TimelineMaidBodySliderKeyXml` クラスの後に追加する:

```csharp
    /// <summary>カメラの身長補正の基準身長 1 件 (SE 独自)。Height はゲーム内身長 (cm)</summary>
    public class TimelineCameraHeightBaseXml
    {
        [XmlElement("MaidSlotNo")]
        public int maidSlotNo;
        [XmlElement("Height")]
        public int height;
    }
```

`maidBodySliderKeys` と `maidBodySliderKeysSpecified` の行の後に追加する:

```csharp
        // 身長補正の基準身長は SE 独自。空でも書き出すと、MTE 産の XML を保存し直しただけで内容が変わる
        [XmlArray("CameraHeightBases")]
        [XmlArrayItem("CameraHeightBase")]
        public List<TimelineCameraHeightBaseXml> cameraHeightBases = new List<TimelineCameraHeightBaseXml>();
        [XmlIgnore] public bool cameraHeightBasesSpecified { get { return cameraHeightBases != null && cameraHeightBases.Count > 0; } set { } }
```

初版の `CameraHeightBase`（int の単一要素）と `<CameraHeightBases>` の子要素名 `CameraHeightBase` は、親要素が違うため衝突しない。

- [ ] **Step 4: `TimelineData` に保持と読み書きを足す**

宣言（初版の `cameraHeightBase` 宣言の後）:

```csharp
        /// <summary>身長補正の基準身長 (スロット番号 → ゲーム内身長 cm)。未設定のスロットは補正しない</summary>
        public Dictionary<int, int> cameraHeightBases = new Dictionary<int, int>();

        public int GetCameraHeightBase(int slotNo)
        {
            int height;
            return cameraHeightBases.TryGetValue(slotNo, out height) ? height : 0;
        }

        public void SetCameraHeightBase(int slotNo, int height)
        {
            if (height > 0)
            {
                cameraHeightBases[slotNo] = height;
            }
        }

        /// <summary>
        /// 未設定のスロットに限り取り込む。設定済みを上書きすると、
        /// 別のメイドで開いて基準メイドを選び直したりキーを打ったりしただけで作者の基準が失われる
        /// </summary>
        public void SetCameraHeightBaseIfUnset(int slotNo, int height)
        {
            if (GetCameraHeightBase(slotNo) <= 0)
            {
                SetCameraHeightBase(slotNo, height);
            }
        }
```

`FromXml`（`maidBodySliderKeysMap` を詰める foreach の後）:

```csharp
            cameraHeightBases.Clear();
            foreach (var baseXml in xml.cameraHeightBases)
            {
                SetCameraHeightBase(baseXml.maidSlotNo, baseXml.height);
            }
```

`ToXml`（`xml.maidBodySliderKeys` を詰めるループの後）:

```csharp
            // 辞書の列挙順に依らないよう、スロット順で書く (保存ファイルと TimelineXmlDiff の比較を安定させる)
            xml.cameraHeightBases = new List<TimelineCameraHeightBaseXml>();
            foreach (var slotNo in cameraHeightBases.Keys.OrderBy(x => x))
            {
                xml.cameraHeightBases.Add(new TimelineCameraHeightBaseXml
                {
                    maidSlotNo = slotNo,
                    height = cameraHeightBases[slotNo],
                });
            }
```

- [ ] **Step 5: 基準身長を履歴の管理外にする**

基準身長は UI の選択・`取得`・手入力で変わるが、`RequestHistory` は呼ばない。比較に含めたままだと、変更後の最初のキー編集の Undo がレイヤー外の差分で全再構築へ倒れ、基準身長まで古い値へ巻き戻る。シェーダー変更（`materialShaders`）と同じく、比較から外し、Undo/Redo でも今の値を引き継ぐ。

`TimelineXmlDiff.SerializeWithoutLayers` で、`materialShaders` と並べて外す:

```csharp
            var layers = xml.layers;
            xml.layers = EmptyLayers;
            // シェーダー変更と身長補正の基準身長は履歴の管理外で、Undo/Redo でも今の値を引き継ぐ (TimelineManager.UpdateTimeline)。
            // 比較に含めると、変更後の最初のキー編集の Undo が毎回全再構築へ倒れる
            var materialShaders = xml.materialShaders;
            xml.materialShaders = null;
            var cameraHeightBases = xml.cameraHeightBases;
            xml.cameraHeightBases = null;
            try
            {
                using (var writer = new StringWriter())
                {
                    Serializer.Serialize(writer, xml);
                    return writer.ToString();
                }
            }
            finally
            {
                xml.layers = layers;
                xml.materialShaders = materialShaders;
                xml.cameraHeightBases = cameraHeightBases;
            }
```

`cameraHeightBasesSpecified` は null を `Count > 0` の前で弾いているので、null のまま直列化しても例外にならない。

`TimelineManager.UpdateTimeline` で、`materialShaders` と並べて今の値を引き継ぐ:

```csharp
            // シェーダー変更は履歴の管理外 (Undo は SE 側の MaterialSnapshot が担う)。
            // 履歴の XML の値で上書きすると、無関係なキー編集の Undo でシェーダーが変わるため今の値を引き継ぐ
            var materialShaders = _timeline != null ? _timeline.materialShaders : null;
            // 身長補正の基準身長も履歴の管理外。履歴の XML の値で上書きすると、Undo で作者の基準へ戻せなくなる
            var cameraHeightBases = _timeline != null ? _timeline.cameraHeightBases : null;

            ClearTimeline();

            _timeline = new TimelineData();
            _timeline.FromXml(xml);
            if (materialShaders != null)
            {
                _timeline.materialShaders = materialShaders;
            }
            if (cameraHeightBases != null)
            {
                _timeline.cameraHeightBases = cameraHeightBases;
            }
```

- [ ] **Step 6: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `CameraHeightBaseTests`）。Expected: 全件 PASS。続けて filter なしで全テストを走らせ、既存テストが壊れていないことを確かめる。

- [ ] **Step 7: コミットする**

commit スキルで、`feat(timeline): 身長補正の基準身長をスロットごとに保存する` などのメッセージにする。

---

### Task 5: カメラレイヤー・UI を項目キーへ切り替え、初版の設定を消す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`（`ResetTimelineState` の末尾）
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs`（全面書き換え）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` / `TimelineXml.cs`（初版の 3 項目を削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs`（`Uncorrect` を削除）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/CameraShakeLayerTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs`（`補正と逆補正は往復で元に戻る` を削除）
- Delete: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightSettingTests.cs`

**Interfaces:**
- Consumes: Task 1 の `Blend` / `Unblend`、Task 2 の `CameraHeightState`、Task 3 の `TransformDataCameraHeight`・`TransformType.CameraHeight`、Task 4 の `GetCameraHeightBase` / `SetCameraHeightBase` / `SetCameraHeightBaseIfUnset`、既存の `MaidHeightReader.GetHeight(Maid)`、`MaidFollowRowDrawer.FillFollowMaidItems` / `ToFollowMaidIndex` / `ToFollowMaidSlotNo` / `GetFollowMaidName`
- Produces:
  - `public static string CameraTimelineLayer.HeightBoneName = "height";`
  - `public static float CameraTimelineLayer.GetSlotScale(TimelineData timeline, int slotNo, out float rootY)`
  - `public static int CameraTimelineLayer.GetSlotHeight(int slotNo)`

- [ ] **Step 1: 失敗するテストを書く**

`CameraShakeLayerTests` の最初の 2 テストを書き換える:

```csharp
        [Fact]
        public void カメラレイヤーはカメラと手ブレと身長補正の3ボーンを持つ()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(3, layer.allBoneNames.Count);
            Assert.Contains(CameraTimelineLayer.CameraBoneName, layer.allBoneNames);
            Assert.Contains(CameraTimelineLayer.ShakeBoneName, layer.allBoneNames);
            Assert.Contains(CameraTimelineLayer.HeightBoneName, layer.allBoneNames);
        }

        [Fact]
        public void ボーン名に応じたTransformTypeを返す()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(TransformType.Camera,
                layer.GetTransformType(CameraTimelineLayer.CameraBoneName));
            Assert.Equal(TransformType.CameraShake,
                layer.GetTransformType(CameraTimelineLayer.ShakeBoneName));
            Assert.Equal(TransformType.CameraHeight,
                layer.GetTransformType(CameraTimelineLayer.HeightBoneName));
        }
```

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraShakeLayerTests`）。Expected: `HeightBoneName` が無く、コンパイルが失敗する。

- [ ] **Step 3: `CameraTimelineLayer` を書き換える**

項目の追加（`ShakeDisplayName` の後、`_allBoneNames`、`InitMenuItems`、`GetTransformType`）:

```csharp
        public static string HeightBoneName = "height";
        public static string HeightDisplayName = "身長補正";

        private List<string> _allBoneNames = new List<string> { CameraBoneName, ShakeBoneName, HeightBoneName };
```

```csharp
            allMenuItems.Add(new BoneMenuItem(CameraBoneName, CameraDisplayName));
            allMenuItems.Add(new BoneMenuItem(ShakeBoneName, ShakeDisplayName));
            allMenuItems.Add(new BoneMenuItem(HeightBoneName, HeightDisplayName));
```

```csharp
        public override TransformType GetTransformType(string name)
        {
            if (name == ShakeBoneName)
            {
                return TransformType.CameraShake;
            }
            if (name == HeightBoneName)
            {
                return TransformType.CameraHeight;
            }
            return TransformType.Camera;
        }
```

ライブ値のリセットは `CameraTimelineLayer.Init` には置かない。`Init` は Undo/Redo の全再構築（`UpdateTimeline` → `LayerInit`）や部分再構築（`UpdateTimelineLayers`）でも呼ばれ、身長補正のキーが無いタイムラインで Undo するたびに基準メイドが「なし」に戻ってしまうため。代わりに `TimelineManager.ResetTimelineState`（新規作成・読込・アンロードだけが通る）の末尾、`_layerBaselineStore.Clear();` の後に足す:

```csharp
            _layerBaselineStore.Clear();
            CameraHeightState.instance.SetLive(CameraHeightState.NoSlot);
```

`ApplyPlayData` の適用順:

```csharp
            // 手ブレ → 身長補正 → カメラの順に適用する (依存の向きを明示するため順序を固定する)
            ApplyPlayDataByType(TransformType.CameraShake);
            // 戻さないと、身長補正のキーを全部消した後も最後に適用したキーの補間が残る
            CameraHeightState.instance.BeginApply();
            ApplyPlayDataByType(TransformType.CameraHeight);
            ApplyPlayDataByType(TransformType.Camera);
```

`ApplyMotion` の先頭の分岐に追加する:

```csharp
            if (motion.start.type == TransformType.CameraHeight)
            {
                ApplyHeightMotion(motion, t);
                return;
            }
```

`ApplyShakeMotion` の後に追加する:

```csharp
        private static void ApplyHeightMotion(MotionData motion, float t)
        {
            var start = motion.start as TransformDataCameraHeight;
            var end = motion.end as TransformDataCameraHeight;
            var startSlotNo = start.maidSlotNo;
            CameraHeightState.instance.ApplyKeys(
                startSlotNo, end != null ? end.maidSlotNo : startSlotNo, t);
        }
```

初版の `GetHeightScale` / `CorrectTargetPos` / `UncorrectTargetPos` を、次で置き換える:

```csharp
        /// <summary>
        /// スロット 1 人ぶんの身長補正の倍率と、補正の中心になるルートの高さ。
        /// 補正しない条件では 1 を返し、rootY は 0
        /// </summary>
        public static float GetSlotScale(TimelineData timeline, int slotNo, out float rootY)
        {
            rootY = 0f;
            if (timeline == null || slotNo < 0)
            {
                return 1f;
            }

            var maidCache = MaidManager.instance.GetMaidCache(slotNo);
            var maid = maidCache != null ? maidCache.maid : null;
            // isLoadedBody を条件にすると、着替えでボディを再ロードする間だけ s = 1 に落ちてカメラが跳ねる
            if (maid == null)
            {
                return 1f;
            }

            rootY = maid.transform.position.y;
            return CameraHeightCorrection.CalcScale(
                timeline.GetCameraHeightBase(slotNo), MaidHeightReader.GetHeight(maid));
        }

        /// <summary>スロットのメイドのゲーム内身長 (cm)。メイドがいなければ 0</summary>
        public static int GetSlotHeight(int slotNo)
        {
            var maidCache = MaidManager.instance.GetMaidCache(slotNo);
            return maidCache != null ? MaidHeightReader.GetHeight(maidCache.maid) : 0;
        }

        private Vector3 CorrectTargetPos(Vector3 position)
        {
            var state = CameraHeightState.instance;
            float rootA, rootB;
            var scaleA = GetSlotScale(timeline, state.slotA, out rootA);
            var scaleB = GetSlotScale(timeline, state.slotB, out rootB);
            position.y = CameraHeightCorrection.Blend(
                position.y, rootA, scaleA, rootB, scaleB, state.weight);
            return position;
        }

        private Vector3 UncorrectTargetPos(Vector3 position)
        {
            var state = CameraHeightState.instance;
            float rootA, rootB;
            var scaleA = GetSlotScale(timeline, state.slotA, out rootA);
            var scaleB = GetSlotScale(timeline, state.slotB, out rootB);
            position.y = CameraHeightCorrection.Unblend(
                position.y, rootA, scaleA, rootB, scaleB, state.weight);
            return position;
        }
```

`UpdateFrame` の末尾（手ブレのボーンを書いた後）に追加する:

```csharp
            // 基準メイドを変えていないのに書くと、補正を使わないタイムラインにも身長補正のキーが入り MTE で開けなくなる。
            // 編集開始時のスナップショットに入れると、編集前に選んだ基準メイドが差分にならず、最初の差分登録で書かれない
            var heightState = CameraHeightState.instance;
            if (!initialEdit && heightState.isLiveChanged)
            {
                var heightTrans = CreateTransformData<TransformDataCameraHeight>(HeightBoneName);
                heightTrans.maidSlotNo = heightState.liveSlotNo;

                var heightBone = frame.CreateBone(heightTrans);
                frame.UpdateBone(heightBone);
            }
```

この条件で次のようになる:
- **新規レイヤー**: `TimelineLayerBase.Init` がフレーム 0 を `UpdateFrame` で作るが、ライブ値は -1 でキーも無いので変更なしとなり、身長補正のキーは入らない
- **補正を使わないタイムライン**: 全登録でも身長補正のキーは書かれない
- **補間の途中**: UI を触っていなければ、ライブ値はキーで当たっている値（区間の始点）のままなので書かれない。書くとそのフレームが補間から外れ、今の補間で逆補正したカメラのキーとずれる
- **編集前に UI で選んだ場合**: スナップショットには身長補正のボーンが無く、差分登録の一時フレームにはある。`FrameData.GetDiffBones` はスナップショットに無いボーンを差分として扱うので、最初の差分登録で書かれる
- **書いた後**: 登録の後の再適用で `appliedSlotNo` が書いた値になり、以降は変更なしに戻る。再適用されないまま次の登録が走っても、`AddKeyFrameDiffBones` が「現在フレームに同じ値で登録済みのボーン」を除く

`UpdateFrame` では基準身長を取り込まない。`UpdateFrame` は編集開始時のスナップショットや差分登録の一時フレームでも呼ばれ、キーを打たない操作でもタイムラインが書き換わるため。取り込みは UI で選んだときだけにする（Step 5）。

編集モード中に UI で基準メイドを変えると、`SetLive` は補間を外し（w = 0）、カメラの表示はそのまま残る。次の登録では、カメラのキーが新しい基準で逆補正した値になって差分に入り、身長補正のキーと一緒に書かれる。見た目を保つための意図した動きで、手当ては要らない。

最初のキーより前のフレームでは、`MotionPlayData` が区間を持たず身長補正もカメラも適用されない（カメラは今の位置のまま）。そのため、ライブ値が前に適用したキーの値のまま残っていても、補正はかからない。

- [ ] **Step 4: 初版の設定と `Uncorrect` を消す**

- `TimelineXml.cs`: 初版のコメント 1 行・`isCameraHeightCorrection` / `cameraHeightMaidSlotNo` / `cameraHeightBase` の 3 フィールドと、3 つの `*Specified` を削除する
- `TimelineData.cs`: 3 フィールドの宣言（`<summary>` 付き）と、`FromXml` / `ToXml` の各 3 行を削除する
- `CameraHeightCorrection.cs`: `Uncorrect` を削除する
- `CameraHeightCorrectionTests.cs`: `補正と逆補正は往復で元に戻る` を削除する（往復は Task 1 の `補間と逆補間は往復で元に戻る` が w = 0 で担う）
- `CameraHeightSettingTests.cs`: ファイルごと削除する（テストプロジェクトはファイルを列挙していないので csproj の変更は不要。`rg -n "Compile Include" source/COM3D2.SceneEditor.Plugin.Tests/*.csproj` で列挙が無いことを確かめる）

- [ ] **Step 5: `CameraHeightRowDrawer` を書き換える**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の設定行 (基準メイド・そのスロットの基準身長)。
    /// 基準メイドはライブ値で、カメラキーを打つと身長補正のキーにも書かれる。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class CameraHeightRowDrawer
    {
        private const float ButtonWidth = 60f;
        private const float FieldWidth = 50f;
        // 手入力の途中値で倍率が極端にならないよう、式の取りうる範囲 (134〜199cm) より広めに絞る
        private const int MinBaseHeight = 100;
        private const int MaxBaseHeight = 250;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.CameraHeightState heightState => MTEP.CameraHeightState.instance;

        /// <summary>先頭に「なし」(null) を含む基準メイドの選択肢</summary>
        private readonly List<MTEP.MaidCache> _maidItems = new List<MTEP.MaidCache>();

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = MaidFollowRowDrawer.GetFollowMaidName,
        };

        public void Draw(GUIView view, float labelWidth, float rowHeight)
        {
            var timeline = timelineManager.timeline;
            if (timeline == null)
            {
                return;
            }

            MaidFollowRowDrawer.FillFollowMaidItems(_maidItems);
            _maidComboBox.items = _maidItems;
            _maidComboBox.currentIndex =
                MaidFollowRowDrawer.ToFollowMaidIndex(heightState.liveSlotNo, _maidItems.Count);
            _maidComboBox.onSelected = (maidCache, index) =>
            {
                var selectedSlotNo = MaidFollowRowDrawer.ToFollowMaidSlotNo(index);
                heightState.SetLive(selectedSlotNo);
                if (selectedSlotNo >= 0)
                {
                    timeline.SetCameraHeightBaseIfUnset(
                        selectedSlotNo, MTEP.CameraTimelineLayer.GetSlotHeight(selectedSlotNo));
                }
            };
            LabeledComboRow.Draw(view, "身長補正", _maidComboBox, labelWidth, rowHeight);

            var slotNo = heightState.liveSlotNo;
            if (slotNo < 0)
            {
                return;
            }

            // コンボは範囲外のスロットを末尾のメイドへ寄せて表示するため、補正が無効なことをここで明示する
            if (maidManager.GetMaidCache(slotNo) == null)
            {
                view.DrawLabel(string.Format("基準メイドが見つかりません (スロット {0})。補正は無効です",
                    slotNo), -1, rowHeight, textColor: Color.yellow);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = "基準身長",
                    labelWidth = labelWidth,
                    value = timeline.GetCameraHeightBase(slotNo),
                    minValue = MinBaseHeight,
                    maxValue = MaxBaseHeight,
                    fieldWidth = FieldWidth,
                    height = rowHeight,
                    onChanged = x => timeline.SetCameraHeightBase(slotNo, x),
                });

                if (view.DrawButton("取得", ButtonWidth, rowHeight))
                {
                    timeline.SetCameraHeightBase(slotNo, MTEP.CameraTimelineLayer.GetSlotHeight(slotNo));
                }
            }
            view.EndLayout();

            float rootY;
            var scale = MTEP.CameraTimelineLayer.GetSlotScale(timeline, slotNo, out rootY);
            view.DrawLabel(string.Format("現在 {0}cm / 補正 x{1:F2}",
                MTEP.CameraTimelineLayer.GetSlotHeight(slotNo), scale), -1, rowHeight);
        }
    }
}
```

初版にあった `ApplyCurrentFrame(true)` の呼び出しは入れない。手ブレの行（`CameraShakeRowDrawer`）と同じく、ライブ値を書くだけにする。再生中・編集モード外は次のフレームの `ApplyPlayData` が反映し、`ApplyCurrentFrame(true)` は全レイヤーのアニメを作り直すため重い。

- [ ] **Step 6: `CameraItemInspector` で身長補正だけを選んだときの表示を足す**

`IsShakeOnly` を、ボーン名を受け取る `IsOnly` に置き換える:

```csharp
        private static bool IsOnly(IList<MTEP.IBoneMenuItem> items, string boneName)
        {
            foreach (var item in items)
            {
                if (item.name != boneName)
                {
                    return false;
                }
            }
            return true;
        }
```

`DrawItems` の先頭を次にする:

```csharp
            // 手ブレ・身長補正の項目だけが選ばれているときは、その設定行だけを出す
            if (items.Count > 0 && IsOnly(items, MTEP.CameraTimelineLayer.ShakeBoneName))
            {
                CameraShakeRowDrawer.Draw(view, RowHeight);
                return;
            }
            if (items.Count > 0 && IsOnly(items, MTEP.CameraTimelineLayer.HeightBoneName))
            {
                _heightRowDrawer.Draw(view, LabelWidth, RowHeight);
                return;
            }
```

クラスの `<summary>` の「「手ブレ」項目だけを選んだときは揺れパラメータの行に切り替える」を、「「手ブレ」「身長補正」の項目だけを選んだときは、その設定行に切り替える」に直す。カメラ項目のときの `_heightRowDrawer.Draw` の呼び出し（追従行の直後）はそのまま残す。`CameraWindow` は変更しない。

- [ ] **Step 7: 両構成をビルドし、全テストを走らせる**

Run: テスト実行コマンド（filter なし）。Expected: COM3D2 と COM3D25 の両方のビルドが警告を増やさずに成功し、全テストが PASS する。`rg -n "isCameraHeightCorrection|cameraHeightMaidSlotNo|cameraHeightBase\b|GetHeightScale|Uncorrect\(" source` が何も出さないことも確かめる。

- [ ] **Step 8: コミットする**

commit スキルで、`feat(camera): 身長補正の基準メイドをカメラレイヤーの項目のキーで持つ` などのメッセージにする。本文に、タイムライン単位の 1 人の基準ではメイドが複数いるタイムラインでカットごとに合わせられないことを書く。

---

### Task 6: 実機確認とドキュメント

**Files:**
- Modify: `docs-site/timeline/layers-camera.md`（「身長補正」の節を書き直す。冒頭の項目の説明も合わせる）
- Modify: `docs-site/guide/staging.md`（Camera ウィンドウの節の身長補正の 1 行）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の身長補正の項目を差し替える。git 管理外なのでコミットしない）

- [ ] **Step 1: 実機へ反映する**

`com3d25-devbridge:restart-verify` スキルの手順で、COM3D25 構成の DLL（`bin/Debug/COM3D25/`）を反映し、ゲームを再起動する。通常シーン（デイリー画面でエディタを有効にする。撮影モードは不可）で確かめる。

- [ ] **Step 2: 補正の効きを確かめる**

メイドを 2 人用意する。2 人目は呼出ウィンドウで呼ぶ。呼べない場合は、1 人のメイドで身長スライダーを変えて代わりに確かめ、その旨を ledger に裁定として残す。身長を変えたら、終わったときに元に戻す。

1. **項目**: カメラレイヤーに `身長補正` 項目が出る。Camera ウィンドウの `Main` タブと Inspector で基準メイドを選べ、「なし」も選べる
2. **カットごとの補正**: 新規タイムラインにカメラレイヤーを足した直後は、フレーム 0 に身長補正のキーが無いことを確かめる。編集モードに入る前に UI で基準メイド A を選び、編集モードでフレーム 0 のカメラを動かして差分登録（登録ボタン / Enter）すると、身長補正のキーも入る。続けてフレーム 30 で基準メイド B を選んでカメラキーを打つ。B だけ身長を変えると、フレーム 30 の注視点だけが B の倍率で動き、フレーム 0 は動かない
3. **補間**: フレーム 15 で、注視点の Y が A の補正と B の補正の中間（`Blend` の w = 0.5）になる
4. **逆補正**: 身長を変えたまま補間区間の外でキーを打ち直し、身長を戻すと、打つ前と同じ構図に戻る。UI を触らずに補間区間の途中（フレーム 15）で打つと身長補正のキーは書かれず、打つ前と同じ構図のまま再生される
5. **キーを全部消す**: 身長補正のキーをすべて消すと、ライブ値の基準メイドで補正される
6. **補正を使わないタイムライン**: 基準メイドを「なし」のまま全登録しても、身長補正のキーが書かれない
7. **基準身長の保護**: 基準身長を設定した後、基準メイドを選び直してもキーを打っても値が変わらない。`取得` を押したときだけ変わる。キーを打った後に Undo しても、基準身長と、身長補正のキーが無いときのライブ値（基準メイド）は戻らない
8. **メイドがいない**: 基準メイドにメイドのいないスロットを指定すると、例外が出ず（`tail_log` で確かめる）補正なしで再生され、UI に警告が出る
9. **追従キー・着替え**: 追従キーのオフセットは変わらない。補正中に基準メイドを着替えさせてもカメラが跳ねない
10. **XML**: 保存した XML に `<Type>CameraHeight</Type>` のキーと `<CameraHeightBases>` が入り、初版の 3 要素は入らない

問題があれば修正し、テスト実行コマンド（filter なし）を走らせ直してからコミットする。

- [ ] **Step 3: ユーザー向けドキュメントを更新する**

`docs-site/timeline/layers-camera.md`:

- 冒頭の「項目は `カメラ` と `手ブレ` の 2 つで、」を「項目は `カメラ` / `手ブレ` / `身長補正` の 3 つで、」に直す
- 「#### 身長補正」の節を、見出しを `### 身長補正` に上げたうえで次に差し替え、`### 手ブレ` の節の後に移す

```markdown
### 身長補正

作成時と身長の違うメイドでタイムラインを再生したときに、注視点の高さを合わせます。カットごとに、どのメイドの身長に合わせるかを選べます。設定は Camera ウィンドウの `Main` タブと、`カメラ` または `身長補正` 項目を選んだときの Inspector にあります（タイムラインを読み込んでいるときだけ表示されます）。

| 値 | 既定 | 補間 |
|---|---|---|
| 基準メイド（`身長補正` の選択） | なし（補正なし） | 線形補間（下記） |

| 設定 | 内容 |
|---|---|
| `身長補正` | 身長に合わせるメイド。変えてからキーを打つと、同じフレームに `身長補正` 項目のキーとして記録されます。変えていなければ記録されません |
| `基準身長` | 作成時の、そのメイドの身長（cm、プロフィールの身長）。メイドごとに持ち、タイムラインに保存されます。`取得` で今の身長を取り込み直します |

- 補正の倍率は「基準メイドの今の身長 ÷ 基準身長」です
- 追従なしのキーの注視点の高さだけを、基準メイドの足元を中心に倍率で伸縮します。距離・FoV・角度は変わりません
- 追従中のキーは、追従点がもともと身長に合わせて動くため補正しません
- `身長補正` のキーの間では、前のキーのメイドに合わせた高さから、次のキーのメイドに合わせた高さへなめらかに移ります。カットで切り替えたいときは、隣り合うフレームにキーを打ちます
- `基準身長` は、そのメイドを初めて選んだときに 1 回だけ取り込みます。選び直しや保存で取り込み直すことはありません。Undo / Redo の対象にもなりません
- 補正中にキーを打つと、基準身長での値に戻して記録します。別のメイドで編集しても、作成時のメイドで再生した構図は崩れません
- `身長補正` のキーが当たっている間、編集モードの外では選んだ基準メイドがキーの値に戻ります（手ブレと同じ）。基準メイドを変えるときは、編集モードで選んでからキーを打ちます
- 基準メイドが上下に移動すると、カメラも移動量の (1 − 倍率) 倍だけ上下します
- `身長補正` のキーを含むタイムラインは、手ブレと同じく MTE では開けません
```

`docs-site/guide/staging.md` の「`Main` タブには、タイムラインを読み込んでいるときだけ `身長補正` の設定も出ます（…）。」の 1 行は、そのまま残す（内容は変わらない）。

- [ ] **Step 4: CLAUDE.md の互換性の節を差し替える**

`W:\COM3D2_5\work\CLAUDE.md` の「- カメラの身長補正（ルートの `IsCameraHeightCorrection` / …）」で始まる 1 行を、次に差し替える:

```markdown
- カメラの身長補正は SE 独自。カメラレイヤーの `身長補正` 項目のキー（`TransformType.CameraHeight`、値数 1 = 基準メイドのスロット、-1 は補正なし）と、ルートの `<CameraHeightBases>`（`MaidSlotNo` / `Height`、基準身長はゲーム内身長の cm、空なら書き出さない）で持つ。version は上げていない。キーを含む XML は MTE・旧 SE では未知の enum 値（`<Type>CameraHeight</Type>`）でデシリアライズに失敗する。補正は再生時に追従なしのカメラキーの注視点の Y だけにかけ、区間内は 2 人の補正結果を線形補間する。キーには基準身長の空間の値を記録する（キーを打つとき逆補間）
```

- [ ] **Step 5: コミットする**

commit スキルで、docs-site の変更を `docs(timeline): 身長補正の説明を項目キーの形に書き直す` などのメッセージでコミットする。CLAUDE.md は git 管理外なのでコミットしない。

---

## 実装後

CLAUDE.md の標準フローに従い、code-review スキルでブランチ全体（初版の計画の実装を含む `da34cce..HEAD`）をレビューしてからユーザーに提示する。

## レビュー却下メモ

- CHANGELOG.md への記載を計画に足す — 変更履歴は release-prep スキルがリリース時に git log からまとめて書く運用のため
- 編集モード中に基準メイドを変えるとカメラの表示が更新されず、次の登録にカメラのキーが混ざる（plan-review 🟡5） — 新しい基準で逆補正して見た目を保つための意図した動き。計画の Task 5 Step 4 に説明を残した
- `ApplyTransformDirect` でカメラのキーへ当てる補正が、最後に再生したフレームの補間の状態を使う点をコメントで明記する（plan-review 🟡7、確信度 低） — プレビュー用途のみで影響が小さく、未確認のまま見送り
- Task 5 のコミットが大きい（plan-review 🟡9） — 初版の 3 項目を消すと、レイヤーと UI を同時に直さないとビルドが通らないため、分けられない
- カメラのキーを書くたびに身長補正のキーも足す（`AddDependentBones`、plan-review 🔴） — ユーザーとの協議で「基準メイドを変えたときだけ書く」に改めた。足りなかったのは編集前に選んだ基準メイドが差分にならない点だけで、編集開始時のスナップショットに身長補正のボーンを入れないことで解消する
