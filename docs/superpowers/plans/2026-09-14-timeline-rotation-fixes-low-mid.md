# タイムライン回転表現 優先度「中 / 低」修正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> （このリポジトリでは CLAUDE.md の規定により subagent-driven-development は使わない）

**Goal:** 回転表現の調査（`docs/timeline-rotation-quaternion-survey.md`）で未対応として残った 3 件 — 3 章の角度補正ロジック重複（中）、5-2 の `FrameData.Flip()` オイラー算術（中）、6 章の非正規化 nlerp（低） — を解消する。

**Architecture:** 3 件とも XML 形式・値の個数・添字に触れない独立した変更なので、1 本の計画で順に片づける。
角度補正の重複は既存の `AngleUtils`（`source/COM3D2.SceneEditor.Plugin/AngleUtils.cs`）へ集約し、`TransformDataBase` の public static API は互換のため残して委譲にする。
`FrameData.Flip()` は挙動変更のリスクが高いため段階式で進める — まず反転規則を純粋関数 `PoseFlipUtils` へ抽出して現挙動をユニットテストで固定し（Task 2）、次にクォータニオン鏡像式との一致を実機で確認できたボーン種別（= 例外規則を持たない「その他」の大多数）だけをクォータニオン経路へ差し替える（Task 3）。例外規則を持つ 4 種別（Root / Pelvis / Spine0 / Bust）は本計画では現状維持とし、検証結果を調査ドキュメントへ残す。
6 章は `PluginUtils.HermiteQuaternion` の戻り値を正規化して正しい nlerp にする（Task 4）。

**Tech Stack:** C# / .NET 3.5（COM3D2 構成）および .NET 4.7.1（COM3D25 構成） / Unity / xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`）

**Spec:** `docs/timeline-rotation-quaternion-survey.md`
（本計画の対象は 8 章「対応候補と優先度」の表で「未対応」となっている 3 行 = 5-2 / 3 章重複 / 6 章。優先度 高 の 2 件と 5-1 は前計画 `docs/superpowers/plans/2026-09-14-timeline-rotation-fixes.md` で対応済み）

## Global Constraints

- **2 構成ビルド必須**: `GameVersion=COM3D2`（.NET 3.5）と `GameVersion=COM3D25`（.NET 4.7.1）の両方をビルドする。片方だけの成功を「ビルド成功」と報告しない。
- **`debug.bat` / `deploy.bat` / `release.bat` は使わない**。`debug.bat` はゲームフォルダへ DLL をコピーするため、MSBuild を直接叩く。
- **ビルド順**: `COM3D2` → `COM3D25` → `dotnet test`。`GameVersion=COM3D2` のビルドは `bin/Debug/COM3D25/` の DLL を消すため、テストは必ず COM3D25 ビルドの後に走らせる。
- **Git Bash から MSBuild を叩くときは `export MSYS2_ARG_CONV_EXCL="*"` を先に実行する**。付けないと `/p:` がパスへ変換され MSB1008 になる。MSBuild の実体は `/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe`（PATH に無い）。
- **XML 形式は変更しない**。`TimelineData.CurrentVersion` は 34 のまま。全タスクで保存形式・値の個数・添字を変えない。
- **ユニットテストで Unity ネイティブ呼び出しを踏まないこと**。`Quaternion.Euler` / `Quaternion.eulerAngles` は extern 実装で xUnit プロセスからは呼べない（`ColorValueInfoTests.cs:187` に同趣旨の既存メモあり）。テストは `float` / `Vector3` / `Quaternion` の**構造体演算と `Mathf` の managed 実装のみ**で書き、オイラー ⇄ クォータニオン変換が要る検証は実機（MCP `com3d25-devbridge`）で行う。
- **`.NET 3.5` の制約**: 入力 5 個以上の `Func<>` / `Action<>` は使えない。本計画では該当しないが、実装中に導入しないこと。
- **コメントとログメッセージは日本語**で書く。
- **コミットメッセージの `Co-Authored-By` / `Claude-Session` 行は、本ファイルのテンプレートをそのまま使わず、実装セッション時点の attribution 指示に従う**。テンプレート中の行は計画作成時点のもの。

### 共通コマンド

ビルド（2 構成）とテストを 1 本で回す:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

単一テストクラスだけ走らせたいとき（プラグインは COM3D25 構成でビルド済みであること）:

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~AngleUtilsTests"
```

### 決定事項（計画作成時にユーザーが選択）

- **5-2 は段階式**で進める。全面書き換えはしない。Task 2（抽出とテスト固定、挙動不変） → Task 3（実機検証のうえ「その他」ボーンのみクォータニオン化）の順。
- **3 章の共通化では「1 度幅の不感帯」を塞ぐ**。`(int)` 切り捨てを `Mathf.Round` ベースの厳密な式へ置き換えるため、差分が 180.0〜181.0 度の帯だけ補正が発火するようになる（＝改善方向の挙動変更）。

---

## Task 1: 角度補正ロジックを `AngleUtils` へ集約する（調査 3 章 / 優先度 中）

同じ ±360 度補正の式が 3 箇所に散っている。`AngleUtils` へ寄せて 1 実装にし、あわせて `(int)` 切り捨て由来の 1 度幅の不感帯を塞ぐ。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/AngleUtils.cs`（末尾へメソッド追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:439-471`（`GetFixedEulerAngles` / `GetNormalizedEulerAngles` を委譲へ）
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/MTEUtils.cs:442-457`（未使用の重複実装を削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs:595-605`（手書きの 360 度補正を共通ヘルパーへ）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/AngleUtilsTests.cs`（既存ファイルへ追記）

**Interfaces:**
- Consumes: 既存の `COM3D2.SceneEditor.Plugin.AngleUtils.NormalizeAngle(float)`
- Produces:
  - `COM3D2.SceneEditor.Plugin.AngleUtils.NormalizeAngles(Vector3 angles) -> Vector3`
  - `COM3D2.SceneEditor.Plugin.AngleUtils.GetFixedAngle(float angle, float prevAngle) -> float`
  - `COM3D2.SceneEditor.Plugin.AngleUtils.GetFixedAngles(Vector3 angles, Vector3 prevAngles) -> Vector3`
  - `COM3D2.MotionTimelineEditor.Plugin.TransformDataBase.GetFixedEulerAngles` / `GetNormalizedEulerAngles` は public static のまま維持（呼び出し元 `TimelineLayerBase.cs:1710` / `:1718`、`VideoWindow.cs:431`、`TransformDataBase.cs:153` / `:158` を壊さない）

**影響範囲の注意:** 不感帯の解消は UI 表示・編集経路（`TimelineLayerBase.cs:1710` / `:1718`）だけでなく、
`TransformDataBase.cs:492 FixEulerAngles` → `TimelineLayerBase.cs:636` / `:644` の経路を通って
**キー確定時に XML へ保存される角度値そのもの**にも波及する。
ただし差が生じるのは補正が新たに発火する 180.0〜181.0 度の帯だけで、その帯の値は
補正後も回転としては同一（360 度単位で寄るだけ）。XML の形式・値の個数・添字は変わらない。

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/AngleUtilsTests.cs` の `AngleUtilsTests` クラス内、既存の `NormalizeAngle_角度を180度以内へ正規化する` の後ろへ追記する。

```csharp
        [Theory]
        // 差が 180 度以内ならそのまま
        [InlineData(0f, 0f, 0f)]
        [InlineData(90f, 0f, 90f)]
        [InlineData(180f, 0f, 180f)]
        // 180 度を超えたら 360 度単位で前の角度側へ寄せる
        [InlineData(200f, 0f, -160f)]
        [InlineData(-200f, 0f, 160f)]
        [InlineData(370f, 10f, 10f)]
        [InlineData(730f, 0f, 10f)]
        // 旧実装の (int) 切り捨てでは補正が発火しなかった 180.0〜181.0 度の帯を塞ぐ
        [InlineData(180.5f, 0f, -179.5f)]
        [InlineData(-180.5f, 0f, 179.5f)]
        public void GetFixedAngle_前の角度から180度以内へ寄せる(float angle, float prevAngle, float expected)
        {
            Assert.Equal(expected, AngleUtils.GetFixedAngle(angle, prevAngle), 3);
        }

        [Fact]
        public void GetFixedAngles_成分ごとに前の角度へ寄せる()
        {
            var actual = AngleUtils.GetFixedAngles(
                new Vector3(200f, -200f, 370f),
                new Vector3(0f, 0f, 10f));

            Assert.Equal(-160f, actual.x, 3);
            Assert.Equal(160f, actual.y, 3);
            Assert.Equal(10f, actual.z, 3);
        }

        [Fact]
        public void NormalizeAngles_成分ごとに180度以内へ正規化する()
        {
            var actual = AngleUtils.NormalizeAngles(new Vector3(200f, -200f, 180f));

            Assert.Equal(-160f, actual.x, 3);
            Assert.Equal(160f, actual.y, 3);
            Assert.Equal(180f, actual.z, 3);
        }
```

ファイル先頭の `using` に `UnityEngine`（`Vector3` 用）を追加する。追加後の先頭は次のとおり。

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;
```

- [ ] **Step 2: テストが落ちることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~AngleUtilsTests"
```

Expected: コンパイルエラー。`AngleUtils` に `GetFixedAngle` / `GetFixedAngles` / `NormalizeAngles` が存在しない。

- [ ] **Step 3: `AngleUtils` へ 3 メソッドを追加する**

`source/COM3D2.SceneEditor.Plugin/AngleUtils.cs` の `NormalizeAngle` の後ろへ追記する。

```csharp
        /// <summary>成分ごとに <see cref="NormalizeAngle"/> を適用する</summary>
        public static Vector3 NormalizeAngles(Vector3 angles)
        {
            return new Vector3(
                NormalizeAngle(angles.x),
                NormalizeAngle(angles.y),
                NormalizeAngle(angles.z));
        }

        /// <summary>
        /// 前の角度との差が ±180 度以内になるよう、360 度単位で寄せる。
        /// キー間の補間で遠回りの経路を選ばせないための補正。
        /// 差がちょうど ±180 度のときは寄せない（どちらへ回っても等距離のため）
        /// </summary>
        public static float GetFixedAngle(float angle, float prevAngle)
        {
            return angle - Mathf.Round((angle - prevAngle) / 360f) * 360f;
        }

        /// <summary>成分ごとに <see cref="GetFixedAngle"/> を適用する</summary>
        public static Vector3 GetFixedAngles(Vector3 angles, Vector3 prevAngles)
        {
            return new Vector3(
                GetFixedAngle(angles.x, prevAngles.x),
                GetFixedAngle(angles.y, prevAngles.y),
                GetFixedAngle(angles.z, prevAngles.z));
        }
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~AngleUtilsTests"
```

Expected: PASS。

- [ ] **Step 5: `TransformDataBase` の 2 メソッドを委譲にする**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:439-471` の `GetFixedEulerAngles` / `GetNormalizedEulerAngles` の本体を差し替える（シグネチャと public static は維持する。`TimelineLayerBase` / `VideoWindow` から呼ばれている）。

```csharp
        public static Vector3 GetFixedEulerAngles(Vector3 angles, Vector3 prevAngles)
        {
            return AngleUtils.GetFixedAngles(angles, prevAngles);
        }

        public static Vector3 GetNormalizedEulerAngles(Vector3 angles)
        {
            return AngleUtils.NormalizeAngles(angles);
        }
```

同ファイル先頭の `using` へ `COM3D2.SceneEditor.Plugin` を追加する（`Timeline/` 配下では既に 13 ファイルが同じ using を持つので前例どおり）。

- [ ] **Step 6: `MTEUtils` の未使用な重複実装を削除する**

`source/COM3D2.SceneEditor.Plugin/MTEUtils/MTEUtils.cs:442-457` の `GetNormalizedEulerAngles` をメソッドごと削除する。呼び出し元は存在しない（`VideoWindow.cs:431` が呼ぶのは `TransformDataBase` 側）。

削除後に呼び出し元が残っていないことを確認する。

```bash
grep -rn "MTEUtils.GetNormalizedEulerAngles" source/ --include=*.cs
```

Expected: ヒット 0 件。

- [ ] **Step 7: `MaidIKHoldController` の手書き補正を共通ヘルパーへ置き換える**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs:595-605` の次のブロックを

```csharp
                // 360 度差を除いて近い方の角度を採用する
                var diffAngle = (int)(footStretchAngle - targetAngle);
                if (diffAngle > 180)
                {
                    footStretchAngle -= (diffAngle + 180) / 360 * 360;
                }
                else if (diffAngle < -180)
                {
                    footStretchAngle -= (diffAngle - 180) / 360 * 360;
                }
```

次へ差し替える。

```csharp
                // 360 度差を除いて近い方の角度を採用する
                footStretchAngle = AngleUtils.GetFixedAngle(footStretchAngle, targetAngle);
```

`MaidIKHoldController.cs` は `namespace COM3D2.SceneEditor.Plugin` 配下なので using の追加は不要。念のため確認する。

```bash
sed -n '1,12p' source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs
```

Expected: `namespace COM3D2.SceneEditor.Plugin` が現れる。

- [ ] **Step 8: 2 構成ビルドと全テストを通す**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/AngleUtils.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs \
        source/COM3D2.SceneEditor.Plugin/MTEUtils/MTEUtils.cs \
        source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/AngleUtilsTests.cs
git commit -m "refactor(timeline): 角度の360度補正を AngleUtils へ集約する"
```

---

## Task 2: ポーズ左右反転の規則を `PoseFlipUtils` へ抽出する（調査 5-2 段階 1 / 優先度 中）

`FrameData.Flip()` はボーン種別テーブルの構築・反転規則・`BoneData` の作り直しが 1 メソッドに同居していてテストできない。まず反転規則とテーブルを純粋な静的クラスへ切り出し、**現在の挙動をそのままユニットテストで固定する**。このタスクでは挙動を一切変えない。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs:226-308`（テーブルと `InitBoneTypes` を削除）、`:310-390`（`Flip()` を `PoseFlipUtils` 呼び出しへ）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs`（新規）

**Interfaces:**
- Consumes: `IKManager.BoneType`（`Assembly-CSharp`）、`COM3D2.MotionTimelineEditor.Plugin.BoneUtils.GetBoneName`
- Produces（すべて `namespace COM3D2.MotionTimelineEditor.Plugin` の `public static class PoseFlipUtils`）:
  - `bool IsNotFlipType(IKManager.BoneType boneType)`
  - `IKManager.BoneType GetFlippedBoneType(IKManager.BoneType boneType)` — 左右入れ替え。対応が無い種別はそのまま返す
  - `Vector3 FlipEulerAngles(IKManager.BoneType flippedBoneType, Vector3 eulerAngles)` — **引数は左右入れ替え後**のボーン種別

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs` を新規作成する。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 反転規則の現挙動を固定するテスト。
    /// Quaternion.Euler / eulerAngles は Unity ネイティブでテストプロセスから呼べないため、
    /// ここではオイラー角の算術だけを検証する（クォータニオン等価性の確認は実機で行う）
    /// </summary>
    public class PoseFlipUtilsTests
    {
        [Theory]
        [InlineData(IKManager.BoneType.TopFixed, true)]
        [InlineData(IKManager.BoneType.Mouth, true)]
        [InlineData(IKManager.BoneType.Nipple_R, true)]
        [InlineData(IKManager.BoneType.Hand_L, false)]
        [InlineData(IKManager.BoneType.Root, false)]
        public void IsNotFlipType_反転対象外の種別を判定する(IKManager.BoneType boneType, bool expected)
        {
            Assert.Equal(expected, PoseFlipUtils.IsNotFlipType(boneType));
        }

        [Theory]
        [InlineData(IKManager.BoneType.Hand_R, IKManager.BoneType.Hand_L)]
        [InlineData(IKManager.BoneType.Hand_L, IKManager.BoneType.Hand_R)]
        [InlineData(IKManager.BoneType.Bust_L, IKManager.BoneType.Bust_R)]
        [InlineData(IKManager.BoneType.Finger0_Root_L, IKManager.BoneType.Finger0_Root_R)]
        [InlineData(IKManager.BoneType.Toe0_Root_R, IKManager.BoneType.Toe0_Root_L)]
        // 左右の対が無い種別はそのまま
        [InlineData(IKManager.BoneType.Root, IKManager.BoneType.Root)]
        [InlineData(IKManager.BoneType.Pelvis, IKManager.BoneType.Pelvis)]
        public void GetFlippedBoneType_左右を入れ替える(
            IKManager.BoneType boneType, IKManager.BoneType expected)
        {
            Assert.Equal(expected, PoseFlipUtils.GetFlippedBoneType(boneType));
        }

        [Theory]
        // その他: X と Y を符号反転し Z は据え置き
        [InlineData(IKManager.BoneType.Hand_L, 10f, 20f, 30f, -10f, -20f, 30f)]
        [InlineData(IKManager.BoneType.Spine1, 10f, 20f, 30f, -10f, -20f, 30f)]
        // Root: y = 180 - (y - 180), z = 270 - (z - 270)
        [InlineData(IKManager.BoneType.Root, 10f, 20f, 30f, 10f, 340f, 510f)]
        // Pelvis: y += 180, z += 180
        [InlineData(IKManager.BoneType.Pelvis, 10f, 20f, 30f, 10f, 200f, 210f)]
        // Spine0: x = 270 - (x - 270)
        [InlineData(IKManager.BoneType.Spine0, 10f, 20f, 30f, 530f, 20f, 30f)]
        // Bust: y = 360 - (y - 180), z = 270 - (z - 270)
        [InlineData(IKManager.BoneType.Bust_L, 10f, 20f, 30f, 10f, 520f, 510f)]
        [InlineData(IKManager.BoneType.Bust_R, 10f, 20f, 30f, 10f, 520f, 510f)]
        public void FlipEulerAngles_種別ごとの反転規則を適用する(
            IKManager.BoneType flippedBoneType,
            float x, float y, float z,
            float expectedX, float expectedY, float expectedZ)
        {
            var actual = PoseFlipUtils.FlipEulerAngles(flippedBoneType, new Vector3(x, y, z));

            Assert.Equal(expectedX, actual.x, 3);
            Assert.Equal(expectedY, actual.y, 3);
            Assert.Equal(expectedZ, actual.z, 3);
        }
    }
}
```

- [ ] **Step 2: テストが落ちることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~PoseFlipUtilsTests"
```

Expected: コンパイルエラー。`PoseFlipUtils` が存在しない。

- [ ] **Step 3: `PoseFlipUtils` を作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs` を新規作成する。中身は `FrameData.cs:226-308` のテーブル構築をそのまま移し、反転規則を `FlipEulerAngles` として切り出したもの。

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ポーズの左右反転規則。
    /// 反転はボーン種別ごとの手書き規則で、オイラー角の算術として表現されている
    /// （経緯は docs/timeline-rotation-quaternion-survey.md の 5-2 を参照）
    /// </summary>
    public static class PoseFlipUtils
    {
        private static bool _initialized = false;
        private static HashSet<IKManager.BoneType> _notFlipTypes = null;
        private static Dictionary<IKManager.BoneType, IKManager.BoneType> _swapFlipDic = null;

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            MTEUtils.LogDebug("ボーンタイプの初期化");

            _notFlipTypes = new HashSet<IKManager.BoneType>
            {
                IKManager.BoneType.TopFixed,
            };
            for (int i = (int) IKManager.BoneType.Mouth; i <= (int) IKManager.BoneType.Nipple_R; i++)
            {
                _notFlipTypes.Add((IKManager.BoneType)i);
            }

            var leftFingerTypes = new List<IKManager.BoneType>(16);
            var rightFingerTypes = new List<IKManager.BoneType>(16);
            var leftToeTypes = new List<IKManager.BoneType>(6);
            var rightToeTypes = new List<IKManager.BoneType>(6);

            for (int i = (int) IKManager.BoneType.Finger0_Root_L; i <= (int) IKManager.BoneType.Finger4_1_L; i++)
            {
                leftFingerTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Finger0_Root_R; i <= (int) IKManager.BoneType.Finger4_1_R; i++)
            {
                rightFingerTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Toe0_Root_L; i <= (int) IKManager.BoneType.Toe2_0_L; i++)
            {
                leftToeTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Toe0_Root_R; i <= (int) IKManager.BoneType.Toe2_0_R; i++)
            {
                rightToeTypes.Add((IKManager.BoneType)i);
            }

            _swapFlipDic = new Dictionary<IKManager.BoneType, IKManager.BoneType>
            {
                { IKManager.BoneType.Clavicle_R, IKManager.BoneType.Clavicle_L },
                { IKManager.BoneType.UpperArm_R, IKManager.BoneType.UpperArm_L },
                { IKManager.BoneType.Forearm_R, IKManager.BoneType.Forearm_L },
                { IKManager.BoneType.Thigh_R, IKManager.BoneType.Thigh_L },
                { IKManager.BoneType.Calf_R, IKManager.BoneType.Calf_L },
                { IKManager.BoneType.Hand_R, IKManager.BoneType.Hand_L },
                { IKManager.BoneType.Foot_R, IKManager.BoneType.Foot_L },
                { IKManager.BoneType.Bust_L, IKManager.BoneType.Bust_R },
            };

            var swapList = _swapFlipDic.ToList();
            foreach (var pair in swapList)
            {
                _swapFlipDic.Add(pair.Value, pair.Key);
            }

            for (int i = 0; i < leftFingerTypes.Count; i++)
            {
                _swapFlipDic.Add(leftFingerTypes[i], rightFingerTypes[i]);
                _swapFlipDic.Add(rightFingerTypes[i], leftFingerTypes[i]);
            }

            for (int i = 0; i < leftToeTypes.Count; i++)
            {
                _swapFlipDic.Add(leftToeTypes[i], rightToeTypes[i]);
                _swapFlipDic.Add(rightToeTypes[i], leftToeTypes[i]);
            }
        }

        /// <summary>反転対象外のボーン種別か</summary>
        public static bool IsNotFlipType(IKManager.BoneType boneType)
        {
            Initialize();
            return _notFlipTypes.Contains(boneType);
        }

        /// <summary>左右を入れ替えたボーン種別を返す。対応が無ければそのまま返す</summary>
        public static IKManager.BoneType GetFlippedBoneType(IKManager.BoneType boneType)
        {
            Initialize();

            IKManager.BoneType flipped;
            if (_swapFlipDic.TryGetValue(boneType, out flipped))
            {
                return flipped;
            }
            return boneType;
        }

        /// <summary>
        /// ボーン種別ごとの反転規則をオイラー角へ適用する。
        /// <paramref name="flippedBoneType"/> は <see cref="GetFlippedBoneType"/> を通した後の種別を渡すこと
        /// </summary>
        public static Vector3 FlipEulerAngles(IKManager.BoneType flippedBoneType, Vector3 eulerAngles)
        {
            var newEulerAngles = eulerAngles;

            if (flippedBoneType == IKManager.BoneType.Root)
            {
                newEulerAngles.y = 180f - (eulerAngles.y - 180f);
                newEulerAngles.z = 270f - (eulerAngles.z - 270f);
            }
            else if (flippedBoneType == IKManager.BoneType.Pelvis)
            {
                newEulerAngles.y = eulerAngles.y + 180f;
                newEulerAngles.z = eulerAngles.z + 180f;
            }
            else if (flippedBoneType == IKManager.BoneType.Spine0)
            {
                newEulerAngles.x = 270f - (eulerAngles.x - 270f);
            }
            else if (flippedBoneType == IKManager.BoneType.Bust_L ||
                     flippedBoneType == IKManager.BoneType.Bust_R)
            {
                newEulerAngles.y = 360f - (eulerAngles.y - 180f);
                newEulerAngles.z = 270f - (eulerAngles.z - 270f);
            }
            else
            {
                newEulerAngles.x = -eulerAngles.x;
                newEulerAngles.y = -eulerAngles.y;
            }

            return newEulerAngles;
        }
    }
}
```

移植元に残っていたコメントアウト行（`//newEulerAngles.z = 90f - (eulerAngles.z - 90f);`）は持ち込まない。規則の由来は調査ドキュメント側に記録済み。

- [ ] **Step 4: `FrameData` から重複を消して `PoseFlipUtils` を使う**

`source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs:226-308` の `initializedBoneTypes` / `notFlipTypes` / `swapFlipDic` / `leftFingerTypes` / `rightFingerTypes` / `leftToeTypes` / `rightToeTypes` / `InitBoneTypes()` をすべて削除し、`Flip()`（`:310-390`）を次へ差し替える。

```csharp
        public void Flip()
        {
            var newBones = new List<BoneData>(_boneMap.Count);

            foreach (var bone in bones)
            {
                var boneType = bone.boneType;
                if (PoseFlipUtils.IsNotFlipType(boneType))
                {
                    newBones.Add(bone);
                    continue;
                }

                var transform = bone.transform;
                var eulerAngles = transform.eulerAngles;

                boneType = PoseFlipUtils.GetFlippedBoneType(boneType);
                var newEulerAngles = PoseFlipUtils.FlipEulerAngles(boneType, eulerAngles);

                MTEUtils.LogDebug("Flip Bone：" + boneType + " " + eulerAngles + " -> " + newEulerAngles);

                var newTransform = timelineManager.CreateTransform(
                    transform.type, BoneUtils.GetBoneName(boneType));
                newTransform.eulerAngles = newEulerAngles;

                if (boneType == IKManager.BoneType.Root)
                {
                    var localPosition = transform.position;
                    localPosition.x = -localPosition.x;
                    newTransform.position = localPosition;
                }

                newBones.Add(CreateBone(newTransform));
            }

            ClearBones();
            SetBones(newBones);
        }
```

旧実装にあった `transformMap`（構築だけされて一度も読まれていない `Dictionary`）は復元しない。削除して構わないデッドコードである。

- [ ] **Step 5: テストが通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~PoseFlipUtilsTests"
```

Expected: PASS。

- [ ] **Step 6: 2 構成ビルドと全テストを通す**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs
git commit -m "refactor(timeline): ポーズ左右反転の規則を PoseFlipUtils へ抽出する"
```

---

## Task 3: 「その他」ボーンの反転をクォータニオン鏡像へ差し替える（調査 5-2 段階 2 / 優先度 中）

例外規則を持たない大多数のボーン（四肢・指・つま先・頭・Spine1 以降）に適用される規則 `x = -x, y = -y, z そのまま` は、**ローカル XY 平面での鏡像変換とオイラー角上で厳密に等価**である。

導出: Unity の `Quaternion.Euler(X, Y, Z)` は `Ry(Y)·Rx(X)·Rz(Z)`。法線 `n` の平面による鏡像は共役 `M·R·M` で表され、回転軸は `n` で反射され角度は符号反転する。`n = (0,0,1)`（ローカル XY 平面）のとき `Rx(X) → Rx(-X)`、`Ry(Y) → Ry(-Y)`、`Rz(Z) → Rz(Z)` となり、結果は `Quaternion.Euler(-X, -Y, Z)` — 現行の「その他」規則そのもの。クォータニオンでは `q' = (-x, -y, z, w)` と書ける。

クォータニオンで直接書けばオイラー角の往復が消え、ジンバルロック近傍でも表現が飛ばない。ただし導出が正しいことを机上だけで確定させず、実機で数値一致を確認してから差し替える。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs`（`FlipRotation` を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs`（`Flip()` のクォータニオン経路）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs`（`FlipRotation` のテスト追加）
- Modify: `docs/timeline-rotation-quaternion-survey.md`（検証結果と状態の反映）

**Interfaces:**
- Consumes: Task 2 の `PoseFlipUtils.IsNotFlipType` / `GetFlippedBoneType` / `FlipEulerAngles`
- Produces: `Quaternion PoseFlipUtils.FlipRotation(Quaternion rotation)` — ローカル XY 平面での鏡像 `(-x, -y, z, w)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs` へ追記する。

```csharp
        [Fact]
        public void FlipRotation_XY平面の鏡像はXとYの符号を反転する()
        {
            var actual = PoseFlipUtils.FlipRotation(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));

            Assert.Equal(-0.1f, actual.x, 5);
            Assert.Equal(-0.2f, actual.y, 5);
            Assert.Equal(0.3f, actual.z, 5);
            Assert.Equal(0.9f, actual.w, 5);
        }

        [Fact]
        public void FlipRotation_2回適用すると元へ戻る()
        {
            var original = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            var actual = PoseFlipUtils.FlipRotation(PoseFlipUtils.FlipRotation(original));

            Assert.Equal(original.x, actual.x, 5);
            Assert.Equal(original.y, actual.y, 5);
            Assert.Equal(original.z, actual.z, 5);
            Assert.Equal(original.w, actual.w, 5);
        }
```

- [ ] **Step 2: テストが落ちることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~PoseFlipUtilsTests"
```

Expected: コンパイルエラー。`PoseFlipUtils.FlipRotation` が存在しない。

- [ ] **Step 3: `FlipRotation` を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs` の `FlipEulerAngles` の後ろへ追記する。

```csharp
        /// <summary>
        /// ローカル XY 平面での鏡像回転を返す。
        /// 鏡像は共役 M·R·M で表され、回転軸が平面の法線で反射し角度が符号反転するため、
        /// 法線 (0,0,1) では (x, y, z, w) → (-x, -y, z, w) になる。
        /// これは <see cref="FlipEulerAngles"/> の「その他」規則
        /// （X と Y を符号反転し Z は据え置き）とオイラー角上で厳密に等価で、
        /// オイラー角の往復が無いぶんジンバルロック近傍でも表現が飛ばない
        /// </summary>
        public static Quaternion FlipRotation(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
        }
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~PoseFlipUtilsTests"
```

Expected: PASS。

- [ ] **Step 5: 実機でオイラー規則との等価性を確認する**

ゲームを起動した状態で MCP `com3d25-devbridge` の `eval_csharp` を使う。ホットリロードは不要で、Step 4 でビルドした DLL が実機に入っていなくてもよい（比較対象の式をその場で書くため）。

ゲームが起動していることを先に確認する（MCP `com3d25-devbridge` の `ping`）。起動していなければユーザーに起動を依頼し、このステップは待つ。

`eval_csharp` へ次を渡す。Unity 型は完全修飾名で書くこと（CLAUDE.md の注意事項）。

```csharp
var samples = new UnityEngine.Vector3[] {
    new UnityEngine.Vector3(10f, 20f, 30f),
    new UnityEngine.Vector3(-45f, 135f, 200f),
    new UnityEngine.Vector3(89.9f, 10f, 10f),
    new UnityEngine.Vector3(90f, 30f, 60f),
    new UnityEngine.Vector3(90.1f, 30f, 60f),
    new UnityEngine.Vector3(-90f, 0f, 0f),
    new UnityEngine.Vector3(179f, 179f, 179f),
    new UnityEngine.Vector3(270f, 350f, 5f),
    // Y / Z が ±180 度境界をまたぐケース（符号反転で表現が飛ばないかの確認）
    new UnityEngine.Vector3(0f, 180f, 0f),
    new UnityEngine.Vector3(0f, -180f, 0f),
    new UnityEngine.Vector3(0f, 0f, 180f),
    new UnityEngine.Vector3(20f, 180f, 180f),
};
var sb = new System.Text.StringBuilder();
foreach (var e in samples)
{
    var q = UnityEngine.Quaternion.Euler(e);
    var byQuaternion = new UnityEngine.Quaternion(-q.x, -q.y, q.z, q.w);
    var byEuler = UnityEngine.Quaternion.Euler(new UnityEngine.Vector3(-e.x, -e.y, e.z));
    sb.AppendLine(e + " angle=" + UnityEngine.Quaternion.Angle(byQuaternion, byEuler).ToString("F5"));
}
sb.ToString()
```

Expected: すべての行で `angle=0.00000`（浮動小数の誤差で `0.001` 未満なら合格）。

- **すべて一致した場合**: Step 6 へ進む。
- **一致しない行がある場合**: Step 6・Step 7 を飛ばし、Step 8 で調査ドキュメントへ「クォータニオン化は不可（実機で不一致）」と一致しなかった入力を記録してコミットする。`FlipRotation` は将来の検討材料として残す。

- [ ] **Step 6: `Flip()` の「その他」経路をクォータニオンにする**

`source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs` の `Flip()` を次へ差し替える（Task 2 Step 4 の版が起点）。

```csharp
        public void Flip()
        {
            var newBones = new List<BoneData>(_boneMap.Count);

            foreach (var bone in bones)
            {
                var boneType = bone.boneType;
                if (PoseFlipUtils.IsNotFlipType(boneType))
                {
                    newBones.Add(bone);
                    continue;
                }

                var transform = bone.transform;
                boneType = PoseFlipUtils.GetFlippedBoneType(boneType);

                var newTransform = timelineManager.CreateTransform(
                    transform.type, BoneUtils.GetBoneName(boneType));

                // 例外規則を持たないボーンは、オイラー角を経由せずクォータニオンの鏡像で反転する。
                // 規則「x = -x, y = -y」はローカル XY 平面の鏡像と厳密に等価で（PoseFlipUtils.FlipRotation 参照）、
                // 往復変換が無いぶんジンバルロック近傍でも表現が飛ばない
                if (transform.hasRotation && !PoseFlipUtils.HasEulerFlipRule(boneType))
                {
                    newTransform.rotation = PoseFlipUtils.FlipRotation(transform.rotation);
                }
                else
                {
                    var eulerAngles = transform.eulerAngles;
                    var newEulerAngles = PoseFlipUtils.FlipEulerAngles(boneType, eulerAngles);
                    MTEUtils.LogDebug("Flip Bone：" + boneType + " " + eulerAngles + " -> " + newEulerAngles);
                    newTransform.eulerAngles = newEulerAngles;
                }

                if (boneType == IKManager.BoneType.Root)
                {
                    var localPosition = transform.position;
                    localPosition.x = -localPosition.x;
                    newTransform.position = localPosition;
                }

                newBones.Add(CreateBone(newTransform));
            }

            ClearBones();
            SetBones(newBones);
        }
```

`PoseFlipUtils` へ判定を追加する（`FlipEulerAngles` の直前へ置く）。

```csharp
        /// <summary>
        /// ボーン種別ごとの例外規則（クォータニオン鏡像では表せない手書きの規則）を持つか。
        /// 持たない種別は <see cref="FlipRotation"/> で反転できる
        /// </summary>
        public static bool HasEulerFlipRule(IKManager.BoneType boneType)
        {
            return boneType == IKManager.BoneType.Root ||
                   boneType == IKManager.BoneType.Pelvis ||
                   boneType == IKManager.BoneType.Spine0 ||
                   boneType == IKManager.BoneType.Bust_L ||
                   boneType == IKManager.BoneType.Bust_R;
        }
```

`FlipEulerAngles` の `else` 節（その他の規則）はそのまま残す。`hasRotation` が false の型が来た場合のフォールバックとして使われる。

- [ ] **Step 7: `HasEulerFlipRule` のテストを足して通す**

`source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs` へ追記する。

```csharp
        [Theory]
        [InlineData(IKManager.BoneType.Root, true)]
        [InlineData(IKManager.BoneType.Pelvis, true)]
        [InlineData(IKManager.BoneType.Spine0, true)]
        [InlineData(IKManager.BoneType.Bust_L, true)]
        [InlineData(IKManager.BoneType.Bust_R, true)]
        [InlineData(IKManager.BoneType.Spine1, false)]
        [InlineData(IKManager.BoneType.Hand_L, false)]
        [InlineData(IKManager.BoneType.Finger0_Root_R, false)]
        public void HasEulerFlipRule_例外規則を持つ種別を判定する(
            IKManager.BoneType boneType, bool expected)
        {
            Assert.Equal(expected, PoseFlipUtils.HasEulerFlipRule(boneType));
        }
```

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 8: 調査ドキュメントへ結果を反映する**

`docs/timeline-rotation-quaternion-survey.md` の 5-2 節の末尾へ、実機検証の結果に応じて次のいずれかを追記する。

Step 5 がすべて一致した場合:

```markdown
> **2026-09-14 対応**: 例外規則を持たないボーン（四肢・指・つま先・頭・Spine1 以降）の反転を
> クォータニオンの鏡像 `q' = (-x, -y, z, w)` へ差し替えた。この式は規則「x = -x, y = -y」と
> オイラー角上で厳密に等価で、実機で `Quaternion.Angle` が全サンプル 0 になることを確認済み。
> 例外規則を持つ 4 種別（Root / Pelvis / Spine0 / Bust）はクォータニオン鏡像では表せないため
> オイラー算術のまま残している。規則自体は `Timeline/PoseFlipUtils.cs` へ抽出済みで、
> `PoseFlipUtilsTests` が現挙動を固定している。
```

Step 5 に不一致があった場合（不一致だった入力を実際の値に置き換えて書く）:

```markdown
> **2026-09-14 調査**: クォータニオン鏡像 `q' = (-x, -y, z, w)` への差し替えは見送った。
> 実機検証でオイラー規則と一致しない入力があったため（該当入力と `Quaternion.Angle` の値をここに列挙）。
> 規則自体は `Timeline/PoseFlipUtils.cs` へ抽出済みで、`PoseFlipUtilsTests` が現挙動を固定している。
```

さらに 8 章の表の 5-2 の行の「状態」列を、一致した場合は `対応済（その他ボーンのみクォータニオン化）`、
不一致だった場合は `抽出のみ（クォータニオン化は見送り）` へ書き換える。

- [ ] **Step 9: コミット**

Step 5 がすべて一致した場合:

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/FrameData.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs \
        docs/timeline-rotation-quaternion-survey.md
git commit -m "refactor(timeline): 例外規則を持たないボーンの左右反転をクォータニオン化する"
```

不一致だった場合:

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/PoseFlipUtils.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/PoseFlipUtilsTests.cs \
        docs/timeline-rotation-quaternion-survey.md
git commit -m "docs(timeline): 左右反転のクォータニオン化を見送った経緯を残す"
```

---

## Task 4: `HermiteQuaternion` の結果を正規化する（調査 6 章 / 優先度 低）

`PluginUtils.HermiteQuaternion` は 4 成分を独立に Hermite 補間した結果をそのまま返すため、戻り値は単位長ではない。`Transform.rotation` へ代入する 5 箇所は Unity 側の正規化に救われるが、`LightHoldKeyConversion.cs:163` は結果を `TransformDataLight.rotation` へ入れて **XML へ保存する**ため、非正規化のクォータニオンがデータに残る。戻り値を正規化して正しい nlerp にする。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs:299-313`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/InterpolationTests.cs`（既存ファイルへ追記）

**Interfaces:**
- Consumes: 既存の `PluginUtils.HermiteValues(float, float, ValueData[], ValueData[], float)` と `ValueData[].ToQuaternion()`
- Produces: `PluginUtils.HermiteQuaternion` のシグネチャは不変。戻り値が常に単位長になる（退化時は `Quaternion.identity`）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/InterpolationTests.cs` の `InterpolationTests` クラスへ追記する。

タンジェント既定値（0）では Hermite は始点と終点の重み付き和になるため、`t = 0.5` で 2 つのクォータニオンの中点が出る。中点は単位長にならないので、正規化の有無がそのまま判定できる。

```csharp
        private static ValueData[] CreateValues(params float[] values)
        {
            var result = new ValueData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = new ValueData { value = values[i] };
            }
            return result;
        }

        [Fact]
        public void HermiteQuaternionは単位長のクォータニオンを返す()
        {
            // identity と Y 軸 90 度。中点の素の大きさは約 0.924 で単位長ではない
            var start = CreateValues(0f, 0f, 0f, 1f);
            var end = CreateValues(0f, 0.70710678f, 0f, 0.70710678f);

            var q = MTEPluginUtils.HermiteQuaternion(0f, 1f, start, end, 0.5f);
            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);

            Assert.Equal(1f, magnitude, 4);
            // 向きは変わらない（x / z はゼロのまま、y と w の比が中点のまま）
            Assert.Equal(0f, q.x, 5);
            Assert.Equal(0f, q.z, 5);
            Assert.Equal(0.41421356f, q.y / q.w, 4);
        }

        [Fact]
        public void HermiteQuaternionは退化した入力でidentityを返す()
        {
            var zero = CreateValues(0f, 0f, 0f, 0f);

            var q = MTEPluginUtils.HermiteQuaternion(0f, 1f, zero, zero, 0.5f);

            Assert.Equal(0f, q.x, 5);
            Assert.Equal(0f, q.y, 5);
            Assert.Equal(0f, q.z, 5);
            Assert.Equal(1f, q.w, 5);
        }
```

ファイル先頭の `using` に `UnityEngine`（`Mathf` 用）を追加する。追加後の先頭は次のとおり。

```csharp
using System;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using MTEPluginUtils = COM3D2.MotionTimelineEditor.Plugin.PluginUtils;
using UnityEngine;
using Xunit;
```

- [ ] **Step 2: テストが落ちることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~InterpolationTests"
```

Expected: `HermiteQuaternionは単位長のクォータニオンを返す` が FAIL（`magnitude` が約 0.9239）。`HermiteQuaternionは退化した入力でidentityを返す` も FAIL（`w` が 0）。

- [ ] **Step 3: 正規化を実装する**

`source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs:299-313` の `HermiteQuaternion` を差し替える。

`Quaternion.normalized` / `Quaternion.Normalize` に頼らず自前で計算する。テストプロセスから Unity ネイティブ実装を踏まないことを確実にするため。

```csharp
        // Quaternionの補間
        // 4 成分を独立に補間したままでは単位長にならないため、正規化して nlerp として扱う。
        // Transform への代入時には Unity 側でも正規化されるが、
        // LightHoldKeyConversion のように結果を XML へ保存する経路があるためここで揃える
        public static Quaternion HermiteQuaternion(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            var q = HermiteValues(
                t0,
                t1,
                start,
                end,
                t
            ).ToQuaternion();

            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (magnitude < 1e-6f)
            {
                // 全成分がゼロへ潰れた退化ケース。回転なしへフォールバックする
                return Quaternion.identity;
            }

            return new Quaternion(
                q.x / magnitude,
                q.y / magnitude,
                q.z / magnitude,
                q.w / magnitude);
        }
```

`PluginUtils.cs` が `using UnityEngine;` を持つことを確認する。無ければ追加する。

```bash
sed -n '1,10p' source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~InterpolationTests"
```

Expected: PASS。

- [ ] **Step 5: 2 構成ビルドと全テストを通す**

`LightHoldKeyConversionTests` が回転の数値を見ていないことは確認済みだが、保持キー変換は `HermiteQuaternion` を通るため全テストで確かめる。

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

Expected: 両構成ビルド成功、全テスト PASS。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/InterpolationTests.cs
git commit -m "fix(timeline): クォータニオン補間の結果を正規化する"
```

---

## Task 5: 調査ドキュメントの状態を更新する

Task 1 / Task 4 の対応内容を `docs/timeline-rotation-quaternion-survey.md` へ反映する（5-2 は Task 3 Step 8 で反映済み）。

**Files:**
- Modify: `docs/timeline-rotation-quaternion-survey.md`

**Interfaces:**
- Consumes: Task 1〜4 の実装結果
- Produces: なし（ドキュメントのみ）

- [ ] **Step 1: 冒頭の注記を書き換える**

現在の「> **2026-09-14: 優先度「高」は対応済み。**」で始まる引用ブロックの末尾の文
「その他の優先度 中 / 低 と「保留」は未対応。」を、次へ差し替える。

```markdown
> 続いて優先度 中 / 低 も対応した（3 章の補正ロジック重複、5-2 の反転規則抽出、6 章の非正規化 nlerp）。
> 未対応で残っているのは「保留」の 1〜2 章のみ。
```

- [ ] **Step 2: 3 章の記述を更新する**

3 章の表の `MTEUtils/MTEUtils.cs:442` の行を削除し（実装ごと削除したため）、
`TransformDataBase.cs:436 GetFixedEulerAngles` と `:456 GetNormalizedEulerAngles` の「内容」列へ
`AngleUtils へ委譲` を明記する。

さらに 3 章末尾の限界リストのうち「**1 度幅の不感帯がある**」の項目を次へ差し替える。

```markdown
- ~~**1 度幅の不感帯がある**~~（2026-09-14 解消）。`AngleUtils.GetFixedAngle` の `Mathf.Round` ベースの式へ統一したため、差分 180.0〜181.0 度でも補正が発火する。
```

「**成分ごとの ±360 補正しかしない**」の項目はそのまま残す（クォータニオン保持へ移行しない限り解消しないため）。

- [ ] **Step 3: 6 章の記述を更新する**

6 章の最初の箇条書き `ToQuaternion（...）は正規化しないため、実質 nlerp になる。` を次へ差し替える。

```markdown
  - `ToQuaternion`（`Timeline/Extensions.cs:74` / `MTEUtils/Extensions.cs:509`）は正規化しないが、`HermiteQuaternion` の戻り値側で正規化するようにした（2026-09-14）。正しい nlerp になり、XML へ保存される `LightHoldKeyConversion` の経路でも単位長が保たれる。
```

- [ ] **Step 4: 8 章の表を更新する**

8 章の表の「状態」列を次のとおり書き換える。

- 中 / 3 章の補正ロジック重複 → `対応済（AngleUtils へ集約。不感帯も解消）`
- 低 / 6 章の非正規化 nlerp → `対応済`

5-2 の行は Task 3 Step 8 で更新済みなので触らない。「保留」の行は未対応のまま残す。

- [ ] **Step 5: docs-review でレビューする**

`docs-review` スキルを起動し、更新した `docs/timeline-rotation-quaternion-survey.md` と実コードの整合性をレビューして指摘を取り込む。

- [ ] **Step 6: コミット**

```bash
git add docs/timeline-rotation-quaternion-survey.md
git commit -m "docs(timeline): 回転表現調査に優先度 中/低 の対応状況を反映する"
```

---

## 完了条件

- [ ] `AngleUtils` に角度補正が 1 実装へ集約され、`MTEUtils.GetNormalizedEulerAngles` が消えている
- [ ] `PoseFlipUtils` が反転規則を保持し、`PoseFlipUtilsTests` が全ボーン種別の規則を固定している
- [ ] 例外規則を持たないボーンの反転経路について、クォータニオン化したか見送ったかが調査ドキュメントに記録されている
- [ ] `HermiteQuaternion` が常に単位長を返す
- [ ] 2 構成（COM3D2 / COM3D25）でビルドが通り、`dotnet test` が全件 PASS する
- [ ] `docs/timeline-rotation-quaternion-survey.md` の 8 章の表で、未対応が「保留」の行だけになっている
- [ ] `code-review` スキルによるレビューを通している（CLAUDE.md の標準フロー）
