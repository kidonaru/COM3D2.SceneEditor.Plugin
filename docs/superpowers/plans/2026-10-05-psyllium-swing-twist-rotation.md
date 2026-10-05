# サイリウム回転の「傾き + ひねり」編集 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ライブ演出ウィンドウのサイリウム「移動回転」の回転入力を、Unity オイラー角（ZXY）から「傾き（X/Z）+ ひねり（Y）」表現へ変え、棒を前へ水平に倒した姿勢（X = ±90°）で Y と Z が同じ動きになるジンバルロックを解消する。

**Architecture:** 純 C# の変換ユーティリティ `SwingTwistAngles`（クォータニオン ⇔ 傾きベクトル + ひねり角）を追加し、`LiveEffectWindow` の回転行だけを差し替える。保持形式（`PsylliumTransformConfig.eulerAngles*`、タイムラインキーのクォータニオン）は変えず、UI の入出力境界で変換する。特異点は「棒が真下を向く（傾き 180°）」だけになる。

**Tech Stack:** C# (COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1)、Unity 5.6 API、xunit

**Spec:** 本計画の「背景」節（会話で合意した案 1）

## 背景

- サイリウムのバーは Y 軸方向に伸びるビルボード板で、棒の軸まわりのひねりは見た目にほぼ効かない
- Unity のオイラー角は `qY * qX * qZ`。X = ±90° で Z（最初に掛かるローカル回転）が Y と同じ軸になり、「Z = 左右へ傾ける」が「水平面内で首を振る」に化ける。前へ振りかぶる動きがちょうどこの姿勢を通る
- キーの保存（`TransformDataPsylliumTransform` の rotationValues 4 値）と補間（`PluginUtils.HermiteQuaternion`）は既にクォータニオンなので変更不要

## 表現の定義

UI の 3 値 `angles` を次のように解釈する。

- `angles.x` / `angles.z`: 傾きベクトル（度）。向き `(x, 0, z) / |…|` を回転軸、長さ `sqrt(x² + z²)` を回転角とする回転 `swing`
- `angles.y`: Y 軸（棒の軸）まわりのひねり（度）`twist`
- 姿勢 `q = swing * twist`（ひねりを先に、ローカルで掛ける）

単軸だけの値は Unity オイラー角と同じ姿勢になる（既定値 `(-10, 0, 0)` も同じ）。逆変換は swing-twist 分解で、傾きの長さは [0, 180]、ひねりは (-180, 180] に正規化する。

## Global Constraints

- コメント・ログは日本語
- テストから Unity のネイティブ ECall（`Quaternion.Euler` / `AngleAxis` / `eulerAngles` / `Inverse` 等）を呼ばない。`QuaternionUtils.EulerToQuaternion`、`new Quaternion(...)`、`Quaternion * Quaternion`、`Mathf` は可
- .NET 3.5 でもビルドできること（ビルド確認は COM3D2 → COM3D25 の両構成）
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild 直叩き
- 保存形式・XML・シーンプリセットは変えない（version も上げない）

## Review Focus

1. 棒が真下（傾き 180°）を向くクォータニオン → NaN を出さず、ひねり 0・傾き長 180 で返る（Task 1 のテストで固定）
2. 符号反転したクォータニオン（同じ姿勢の別表現）→ 同じ 3 値を返す（Task 1 のテストで固定）
3. 既存キーの Unity オイラー値がジンバル姿勢（例 `(-90, 0, 30)`）→ 変換し直しても姿勢が保たれる（Task 1 のテストで固定）
4. 既定値 `(-10, 0, 0)` の表示とリセットボタンが今と同じ数値になる（Task 1 の単軸一致テスト + Task 2 の実機確認）
5. 「両手」タブで編集すると右手へ同じ回転が写る（既存処理のまま。Task 2 の実機確認）

---

### Task 1: `SwingTwistAngles` 変換ユーティリティ

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/SwingTwistAngles.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="QuaternionUtils.cs" />` の次の行に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/SwingTwistAnglesTests.cs`

**Interfaces:**
- Produces:
  - `public static Quaternion SwingTwistAngles.ToQuaternion(Vector3 angles)`
  - `public static Vector3 SwingTwistAngles.FromQuaternion(Quaternion rotation)`（`x`/`z` = 傾きベクトル、`y` = ひねり (-180, 180]）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/SwingTwistAnglesTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SwingTwistAnglesTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 3);
            Assert.Equal(expected.y, actual.y, 3);
            Assert.Equal(expected.z, actual.z, 3);
        }

        /// <summary>符号違いも同じ姿勢として比べる</summary>
        private static void AssertSameRotation(Quaternion expected, Quaternion actual)
        {
            Assert.Equal(1f, Mathf.Abs(Quaternion.Dot(expected, actual)), 4);
        }

        /// <summary>棒（ローカル +Y）の向き。q * (0,1,0) を成分で展開したもの</summary>
        private static Vector3 StickDirection(Quaternion q)
        {
            return new Vector3(
                2f * (q.x * q.y - q.w * q.z),
                1f - 2f * (q.x * q.x + q.z * q.z),
                2f * (q.y * q.z + q.w * q.x));
        }

        [Theory]
        [InlineData(-10f, 0f, 0f)]
        [InlineData(0f, 0f, 30f)]
        [InlineData(0f, 45f, 0f)]
        [InlineData(-90f, 0f, 0f)]
        public void ToQuaternion_単軸ならUnityオイラー角と同じ姿勢になる(float x, float y, float z)
        {
            var angles = new Vector3(x, y, z);
            AssertSameRotation(
                QuaternionUtils.EulerToQuaternion(angles),
                SwingTwistAngles.ToQuaternion(angles));
        }

        [Theory]
        [InlineData(-10f, 0f, 0f)]
        [InlineData(-90f, 0f, 30f)]
        [InlineData(-90f, 25f, 30f)]
        [InlineData(120f, -60f, -50f)]
        [InlineData(0f, 170f, 0f)]
        [InlineData(0f, 0f, 0f)]
        public void FromQuaternion_ToQuaternionの値へ戻る(float x, float y, float z)
        {
            var angles = new Vector3(x, y, z);
            AssertVector(angles,
                SwingTwistAngles.FromQuaternion(SwingTwistAngles.ToQuaternion(angles)));
        }

        [Fact]
        public void 前へ水平に倒してもZで左右へ傾きYは棒の向きを変えない()
        {
            var forward = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 0f, 0f)));
            var leaned = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 0f, 30f)));
            var twisted = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 30f, 0f)));

            // Z は棒を左右（X 方向）へ傾ける
            Assert.True(Mathf.Abs(leaned.x - forward.x) > 0.1f);
            // Y は棒の軸まわりのひねりなので向きは変わらない
            AssertVector(forward, twisted);
        }

        [Fact]
        public void FromQuaternion_符号反転した同じ姿勢は同じ値になる()
        {
            var q = SwingTwistAngles.ToQuaternion(new Vector3(40f, -70f, 20f));
            var negated = new Quaternion(-q.x, -q.y, -q.z, -q.w);

            AssertVector(SwingTwistAngles.FromQuaternion(q), SwingTwistAngles.FromQuaternion(negated));
        }

        [Fact]
        public void FromQuaternion_真下向きはひねり0と傾き180で返る()
        {
            var down = SwingTwistAngles.ToQuaternion(new Vector3(180f, 0f, 0f));
            var angles = SwingTwistAngles.FromQuaternion(down);

            // 180° と -180° はどちらも真下で、float の cos(π/2) の符号次第でどちらにもなる
            Assert.Equal(180f, Mathf.Abs(angles.x), 3);
            Assert.Equal(0f, angles.y, 3);
            Assert.Equal(0f, angles.z, 3);
        }

        [Fact]
        public void 真下向きでひねりがあっても変換し直した姿勢は保たれる()
        {
            var original = SwingTwistAngles.ToQuaternion(new Vector3(180f, 30f, 0f));

            AssertSameRotation(original,
                SwingTwistAngles.ToQuaternion(SwingTwistAngles.FromQuaternion(original)));
        }

        [Fact]
        public void FromQuaternion_ひねり180度は範囲の端に収まる()
        {
            var angles = SwingTwistAngles.FromQuaternion(
                SwingTwistAngles.ToQuaternion(new Vector3(0f, 180f, 0f)));

            // float 誤差で 180 と -180 のどちらにもなりうる。どちらも同じ姿勢
            Assert.Equal(180f, Mathf.Abs(angles.y), 3);
        }

        [Theory]
        [InlineData(-90f, 0f, 30f)]
        [InlineData(-90f, 40f, 0f)]
        [InlineData(30f, 200f, -75f)]
        public void 既存キーのオイラー角を変換し直しても姿勢が保たれる(float x, float y, float z)
        {
            var original = QuaternionUtils.EulerToQuaternion(new Vector3(x, y, z));
            var angles = SwingTwistAngles.FromQuaternion(original);

            AssertSameRotation(original, SwingTwistAngles.ToQuaternion(angles));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter SwingTwistAnglesTests`
Expected: `SwingTwistAngles` が無いためコンパイルエラー（CS0103）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/SwingTwistAngles.cs`:

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 回転を「傾き + ひねり」の 3 値で表す。Y 軸に伸びる棒（サイリウム）の向きを編集するためのもの。
    ///
    /// Unity のオイラー角（ZXY = <c>qY * qX * qZ</c>）は X = ±90° で Z が Y と同じ軸になり、
    /// 棒を前へ水平に倒した姿勢で「Z = 左右へ傾ける」が効かなくなる。
    /// この表現では x / z を傾きベクトル（向き = XZ 平面上の回転軸、長さ = 傾ける角度）、
    /// y を棒の軸まわりのひねりとし、<c>swing * twist</c> で合成する。
    /// 表現が定まらないのは棒が真下を向く（傾き 180°）ときだけ。
    /// 単軸だけの値は Unity のオイラー角と同じ姿勢になる。
    /// テストから呼べるよう、Unity のネイティブ ECall は使わない
    /// </summary>
    public static class SwingTwistAngles
    {
        private const float Epsilon = 1e-6f;

        public static Quaternion ToQuaternion(Vector3 angles)
        {
            var swing = Quaternion.identity;
            var swingAngle = Mathf.Sqrt(angles.x * angles.x + angles.z * angles.z);
            if (swingAngle > Epsilon)
            {
                var halfSwing = swingAngle * 0.5f * Mathf.Deg2Rad;
                // 軸 (x, 0, z) / swingAngle の正規化と sin(θ/2) を 1 つの係数にまとめる
                var scale = Mathf.Sin(halfSwing) / swingAngle;
                swing = new Quaternion(angles.x * scale, 0f, angles.z * scale, Mathf.Cos(halfSwing));
            }

            var halfTwist = angles.y * 0.5f * Mathf.Deg2Rad;
            var twist = new Quaternion(0f, Mathf.Sin(halfTwist), 0f, Mathf.Cos(halfTwist));

            return swing * twist;
        }

        public static Vector3 FromQuaternion(Quaternion rotation)
        {
            var q = QuaternionUtils.Normalize(rotation);

            // ひねりは q を Y 軸へ射影したもの。真下向きでは射影が 0 になり定まらないので 0 とする
            var twist = Quaternion.identity;
            var twistLength = Mathf.Sqrt(q.y * q.y + q.w * q.w);
            if (twistLength > Epsilon)
            {
                twist = new Quaternion(0f, q.y / twistLength, 0f, q.w / twistLength);
            }

            // swing = q * twist^-1。y 成分は構成上 0 になる
            var swing = q * new Quaternion(0f, -twist.y, 0f, twist.w);

            // 傾き角を [0, 180] に収めるため swing の w を非負にそろえる。
            // 両方の符号を反転するので合成した姿勢は変わらない
            if (swing.w < 0f)
            {
                swing = new Quaternion(-swing.x, -swing.y, -swing.z, -swing.w);
                twist = new Quaternion(0f, -twist.y, 0f, -twist.w);
            }

            var x = 0f;
            var z = 0f;
            var sinHalfSwing = Mathf.Sqrt(swing.x * swing.x + swing.z * swing.z);
            if (sinHalfSwing > Epsilon)
            {
                var swingAngle = 2f * Mathf.Atan2(sinHalfSwing, swing.w) * Mathf.Rad2Deg;
                x = swing.x / sinHalfSwing * swingAngle;
                z = swing.z / sinHalfSwing * swingAngle;
            }

            var twistAngle = 2f * Mathf.Atan2(twist.y, twist.w) * Mathf.Rad2Deg;
            return new Vector3(x, Mathf.DeltaAngle(0f, twistAngle), z);
        }
    }
}
```

csproj（`<Compile Include="QuaternionUtils.cs" />` の直後）:

```xml
    <Compile Include="SwingTwistAngles.cs" />
```

- [ ] **Step 4: 両構成ビルド + テスト**

Git Bash で:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter SwingTwistAnglesTests
```

Expected: 両ビルド成功（MSBuild の DLL コピー警告は可）、SwingTwistAnglesTests 全件 PASS

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/SwingTwistAngles.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/SwingTwistAnglesTests.cs
git commit -m "feat(psyllium): 回転を傾き+ひねりで表す変換を追加する"
```

### Task 2: ライブ演出ウィンドウの回転行を差し替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`（`DrawPsylliumTransformEdit` 相当の回転 2 ブロック、現 824〜865 行付近。定数は 28 行付近）
- Modify: `docs-site/timeline/layers-effect.md`（71 行目の箇条の次）

**Interfaces:**
- Consumes: `SwingTwistAngles.ToQuaternion` / `FromQuaternion`（Task 1）、`QuaternionUtils.EulerToQuaternion`、`TimelineLayerBase.DrawTransformVector3(GUIView, string, float, Vector3, Vector3, Action<Vector3>, FloatFieldType, bool, float)`

- [ ] **Step 1: 定数とヘルパーを追加する**

`private const float PsylliumTransformLabelWidth = 80f;` の次に:

```csharp
        private const float PsylliumRotationSensitivity = 1f;
```

`DrawPsylliumAreaEdit` の直前に:

```csharp
        /// <summary>
        /// サイリウムの手の回転を「傾き + ひねり」で編集する（<see cref="SwingTwistAngles"/>）。
        /// オイラー角のままだと棒を前へ水平に倒した姿勢で Y と Z が同じ動きになるため。
        /// 保持形式はオイラー角のままなので、表示と書き戻しの境界で変換する
        /// </summary>
        private static bool DrawPsylliumRotation(
            GUIView view, ref Vector3 eulerAngles, Vector3 initialEulerAngles)
        {
            var angles = SwingTwistAngles.FromQuaternion(
                QuaternionUtils.EulerToQuaternion(eulerAngles));
            var initialAngles = SwingTwistAngles.FromQuaternion(
                QuaternionUtils.EulerToQuaternion(initialEulerAngles));

            var updated = TimelineLayerBase.DrawTransformVector3(
                view, "回転", PsylliumRotationSensitivity, angles, initialAngles,
                value => angles = value,
                labelWidth: PsylliumTransformLabelWidth);

            if (updated)
            {
                // eulerAngles は [0, 360) で返る。従来どおり (-180, 180] で持たせ、
                // 既定値 (-10, 0, 0) へのリセット後も Equals で既定値と一致させる
                var euler = SwingTwistAngles.ToQuaternion(angles).eulerAngles;
                eulerAngles = new Vector3(
                    Mathf.DeltaAngle(0f, euler.x),
                    Mathf.DeltaAngle(0f, euler.y),
                    Mathf.DeltaAngle(0f, euler.z));
            }

            return updated;
        }
```

- [ ] **Step 2: 回転 2 ブロックを差し替える**

`if (_handTabType == HandTabType.右手) { var initialEulerAngles = defaultConfig.eulerAnglesRight; ... } else { ... transformConfig.eulerAnglesLeft = transformCache.eulerAngles; } }` の回転ブロック全体を次へ置き換える（移動ブロックとその後の `if (updateTransform)` は触らない）:

```csharp
            if (_handTabType == HandTabType.右手)
            {
                var eulerAngles = transformConfig.eulerAnglesRight;
                if (DrawPsylliumRotation(view, ref eulerAngles, defaultConfig.eulerAnglesRight))
                {
                    transformConfig.eulerAnglesRight = eulerAngles;
                    updateTransform = true;
                }
            }
            else
            {
                var eulerAngles = transformConfig.eulerAnglesLeft;
                if (DrawPsylliumRotation(view, ref eulerAngles, defaultConfig.eulerAnglesLeft))
                {
                    transformConfig.eulerAnglesLeft = eulerAngles;
                    updateTransform = true;
                }
            }
```

`defaultTrans` 変数が未使用になったら削除する（`grep -n defaultTrans` で同メソッド内の他用途を確認）。

- [ ] **Step 3: ユーザードキュメント**

`docs-site/timeline/layers-effect.md` の「`パターン` と `移動回転` は Inspector では編集できず…」の箇条の次に追加:

```markdown
- ライブ演出ウィンドウの `移動回転` の `回転` は「傾き + ひねり」で入力します。`X` が前後、`Z` が左右の傾きで、両方入れると斜めへ傾きます（傾く角度は `√(X² + Z²)`）。`Y` が棒の軸まわりのひねりです。棒を前へ水平に倒しても `Y` と `Z` が同じ動きになりません。1 軸だけの値は通常の角度と同じ向きになります
```

- [ ] **Step 4: 両構成ビルド + 全テスト**

Task 1 Step 4 のコマンドの `--filter SwingTwistAnglesTests` を外して実行。
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 5: 実機確認（ゲーム起動中のみ）**

devbridge が ping に応答する場合、ゲーム停止 → DLL 反映が必要なので restart-verify スキルの手順はユーザー確認の上で行う。応答しない／反映できない場合はスキップし、未確認として報告する。確認項目:
- 既定パターンの回転が `-10, 0, 0` と表示される。リセットで同値へ戻る
- X を -90 にしたあと Z を動かすと棒が左右へ傾き、Y を動かしても棒の向きがほぼ変わらない
- 「両手」タブで回転を変えると右手にも写る
- X を -90 付近でドラッグ・数値入力しても棒の向きが勝手にずれない（オイラー角経由の保持で精度が落ちないか）
- 傾き 180° 超・ひねり ±180° を跨ぐドラッグで、表示値の符号反転が起きても棒の向きが飛ばない

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs docs-site/timeline/layers-effect.md
git commit -m "feat(psyllium): 移動回転の回転を傾き+ひねりで編集してジンバルロックを避ける"
```

## 範囲外

- キーフレーム詳細（`KeyFrameInspector` / `KeyFrameBatchDrawer`）の回転行は全レイヤー共通の Unity オイラー角のまま。サイリウムの移動回転キーを大きく傾けると、ライブ演出ウィンドウと数値の見え方が異なる
- コントローラー・エリア・バー間角度・ランダム角度の回転はオイラー角のまま（ほぼヨーのみ・小角度のため）

## レビュー却下メモ

- 保持をクォータニオン化して Unity `eulerAngles` 経由の往復をやめる（`RotationCache` に四元数 setter 追加）— 未確認のまま見送り。劣化は X≈±90° での asin の量子化（0.0x° 程度）で、編集のたびに表示値から再計算するため累積しない見込み。`PsylliumTransformConfig` は素の Vector3 で、構造変更が UI 境界の範囲を超える。Task 2 Step 5 の実機確認で問題が出たら再検討する
- 傾き 179.9° 近傍でひねりありの値の往復テスト — 特異点近傍は float 誤差でひねりの精度が落ち、3 桁の比較が不安定。姿勢保存は真下 + ひねりのテストで固定した
