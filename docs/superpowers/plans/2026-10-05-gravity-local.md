# 重力のローカル座標（Bip01 基準）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重力の各カテゴリ（髪 / スカート）に「ローカル（Bip01）」フラグを足し、ON のときは入力した offset を Bip01 の回転に追従する軸として解釈して揺れものへ渡す。

**Architecture:**
- 回転と ±1 への収め方の純粋計算を静的クラス `GravityLocalSpace` に切り出し、ゲーム外テストで固定する
- `MaidGravityController` はカテゴリごとに local フラグを持ち、`ApplyCategory` で `local ? GravityLocalSpace.ToForce(bip01.rotation, offset) : offset` を `GravityTransformControl.transform.localPosition` へ書く。local ON かつ有効なカテゴリは `Update()` で毎フレーム書き直す（Bip01 は前フレームの最終姿勢。1 フレーム遅れは許容）
- タイムライン（`TransformDataGravity` index 4、値数 5）・シーンプリセット（`ScenePresetGravity.local` 属性）・Undo（`GravitySnapshot`）・UI（`GravityRowDrawer`）へ local を通す

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-05-user-feedback-batch-design.md` の「B. 重力のローカル座標（Bip01 基準）」

## Global Constraints

- local の既定は OFF（今までどおりワールド）
- 基準の回転 `R = Bip01.rotation × Inverse(Bip01 の基準姿勢の回転)`、書き込む値 `force = R × offset`
- 回転後にいずれかの成分の絶対値が 1 を超えたら、最大成分が 1 になるようベクトル全体を縮める（方向を保つ）
- 着替え中（`wasBusy`）は書き込まない（既存どおり）
- UI のトグル文言: 「ローカル（Bip01）」。履歴ラベル: `"重力: " + category.name + " ローカル"`
- タイムライン: `TransformDataGravity` index 4 = Local（bool）、値数 5。補間せず区間開始時に適用。4 値以下の旧キーは `FromXml` で明示的に Local=false へ補正
- シーンプリセット: `ScenePresetGravity` に `[XmlAttribute] local`、false は書き出さない
- タイムライン（38）・シーンプリセット（40）の版は上げない（後述「版の判断」）
- コードのコメント・ログは日本語。両構成（COM3D2 / COM3D25）でビルドする
- テストで Unity のネイティブ呼び出し（`Quaternion.Euler` / `Quaternion.Inverse` 等）は使えない。回転は `TestQuaternions` か成分指定の `new Quaternion(...)` で作る。`GravityLocalSpace` 内でも `Quaternion.Inverse` は使わず共役で逆回転を作る（単位クォータニオン前提）

## 基準姿勢の実測結果（2026-10-06、devbridge）

spec の未決事項「Bip01 の基準姿勢の回転の取り方」は計画作成時に実測で決めた。

旧ボディ（メイド 0、`IsCrcBody=false`、ルート回転は単位）で、メイドのルート基準の Bip01 の回転を測った。

| 姿勢 | Bip01 の回転 (x, y, z, w) | bindpose からの差 |
|---|---|---|
| body メッシュの bindpose（T ポーズ） | (-0.5, 0.5, 0.5, 0.5) | 0° |
| `maid_stand01.anm`（エディットの標準立ち）の 0 秒 | (-0.5415668, 0.5415668, 0.4546487, 0.4546487)（符号反転して表記） | X 軸まわり約 10° |
| `stand000_sudati.anm` の 0 秒 | (-0.4829, 0.4829, 0.5165, 0.5165) | X 軸まわり約 4° |
| デイリーで再生中のモーション（91.7 秒時点） | — | 約 19° |

- 測り方: Bip01 の `Animation` の各 `AnimationState` の enabled / weight を一時的に対象クリップだけにして `Animation.Sample()` し、`Inverse(maid.transform.rotation) * Bip01.rotation` を読む。読んだ直後に enabled / weight / time を戻して再度 `Sample()` した（再生中のモーションは継続を確認済み）
- spec の基準姿勢は「メイドのルート回転が単位で、立ちポーズ」。T ポーズの bindpose は立ちポーズではないので採らず、エディットの標準の立ちポーズ `maid_stand01.anm` の値を基準姿勢の定数にする: `new Quaternion(-0.5415668f, 0.5415668f, 0.4546487f, 0.4546487f)`
- 骨盤の傾きはポーズごとに違う（上表で 4〜19°）。定数ひとつでは吸収できないため、「`maid_stand01` でワールドと一致し、ほかのポーズでは骨盤の傾きの分だけずれる」を仕様として docs に書く
- CRC ボディのメイドは計画作成時にロードされていなかった。段 3 の実機確認で、CRC の `maid_stand01` の Bip01 が同じ値かを同じ手順で確かめ、違えば計画に追記して `GravityLocalSpace` をボディ別にする

## 版の判断

- タイムライン: 値を 1 個足すだけで、旧キーは Local=false（従来のワールド）で読める。ライトの輪郭の硬さ（index 19）と同じく版は上げない
- シーンプリセット: 属性の追加だけで、無ければ false（従来のワールド）。spec「共通方針」の「既定値が旧挙動と一致するなら上げない」に従い上げない。`ScenePresetGravity` のコメントに追加の経緯を書く

## Review Focus

1. **立ちポーズでも骨盤の傾き分ずれる**: 基準は `maid_stand01` の実測値で、ほかの立ちポーズでは 4〜19° ずれる。仕様として docs に書く → Task 5 の docs。実機確認で `maid_stand01` の一致を見る
2. **Bip01 が取れない（ボディ再ロード中・破棄直後）**: 例外を出さず、ワールド（offset そのまま）として書く → Task 2 の `GetBip01` が null を返したときの分岐。テストは書けないので実機確認で着替え中・後の `tail_log` を見る
3. **回転後に成分が ±1 を超える（offset (1,1,1) を 45° 回すなど）**: 方向を保って最大成分 1 に縮む → Task 1 のテスト `FitToUnitBox_*`
4. **4 値の旧タイムラインキー**: Local=false で読む → Task 3 のテスト `旧キーはローカルOFFで読む`
5. **local だけ ON で他は既定のメイドへのプリセット・Undo 適用**: 「全部既定なら触らない」判定に local を含め、local ON が捨てられない → Task 3 の `isDefault` テスト、Task 4 の `IsDefaultGravity` / `IsAllDefault` の変更

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/GravityLocalSpace.cs` | 新規 | 基準姿勢の定数・回転・±1 への収め方（純粋計算） |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | `<Compile Include="MaidManipulation\GravityLocalSpace.cs" />` |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidGravityController.cs` | 変更 | local の保持・書き込み・毎フレーム追従 |
| `source/COM3D2.SceneEditor.Plugin/GravityRowDrawer.cs` | 変更 | 「ローカル（Bip01）」トグル |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataGravity.cs` | 変更 | index 4 Local、値数 5、旧キー補正 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/GravityTimelineLayer.cs` | 変更 | Local の適用・キー作成 |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 変更 | `ScenePresetGravity.local` |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 変更 | Capture / Apply / 既定判定に local |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/GravitySnapshot.cs` | 変更 | local の記録・復元・比較 |
| `source/COM3D2.SceneEditor.Plugin.Tests/GravityLocalSpaceTests.cs` | 新規 | `GravityLocalSpace` のテスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs` | 変更 | 値数 5・旧キー・isDefault |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetGravityXmlTests.cs` | 新規 | local 属性の書き出し・往復 |
| `docs-site/guide/maid-editing.md` | 変更 | 「重力」節 |
| `docs-site/timeline/layers-maid.md` / `compatibility.md` | 変更 | 値の表・互換の注意 |
| `W:\COM3D2_5\work\CLAUDE.md` | 変更 | 互換の注意（リポジトリ外。コミット対象外） |

テストプロジェクトはビルド済みのプラグイン DLL を参照するため、ソースの追加は本体 csproj への登録だけで足りる（テスト csproj は SDK 形式で自動収集）。

---

### Task 1: 回転計算 `GravityLocalSpace`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/GravityLocalSpace.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`MaidManipulation\MaidGravityController.cs` の Compile 行の直前）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/GravityLocalSpaceTests.cs`

**Interfaces:**
- Produces:
  - `public static class GravityLocalSpace`（namespace `COM3D2.SceneEditor.Plugin`）
  - `public static readonly Quaternion Bip01BaseRotation`
  - `public static Quaternion GetBodyRotation(Quaternion bip01Rotation)` — `bip01Rotation × 共役(Bip01BaseRotation)`
  - `public static Vector3 ToForce(Quaternion bip01Rotation, Vector3 offset)` — `FitToUnitBox(GetBodyRotation(bip01Rotation) * offset)`
  - `public static Vector3 FitToUnitBox(Vector3 v)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/GravityLocalSpaceTests.cs`:

```csharp
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>重力のローカル座標（Bip01 基準）の回転と ±1 への収め方を固定する</summary>
    public class GravityLocalSpaceTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.InRange(actual.x, expected.x - Tolerance, expected.x + Tolerance);
            Assert.InRange(actual.y, expected.y - Tolerance, expected.y + Tolerance);
            Assert.InRange(actual.z, expected.z - Tolerance, expected.z + Tolerance);
        }

        [Fact]
        public void 基準姿勢の回転は実測値()
        {
            // maid_stand01 の Bip01 を実測した値。変えると既存シーンの重力の向きが変わる
            var q = GravityLocalSpace.Bip01BaseRotation;
            Assert.Equal(-0.5415668f, q.x);
            Assert.Equal(0.5415668f, q.y);
            Assert.Equal(0.4546487f, q.z);
            Assert.Equal(0.4546487f, q.w);
        }

        [Fact]
        public void 基準姿勢ではoffsetがそのまま返る()
        {
            var force = GravityLocalSpace.ToForce(
                GravityLocalSpace.Bip01BaseRotation, new Vector3(0.2f, -1f, 0.3f));

            AssertVector(new Vector3(0.2f, -1f, 0.3f), force);
        }

        [Fact]
        public void Bip01をX軸まわりに90度回すと下向きの重力も同じだけ回る()
        {
            // 寝そべり相当: 体ごとワールド X 軸まわりに 90 度倒す。(0,-1,0) は (0,0,-1) へ回る
            var bip01 = TestQuaternions.AroundX(90f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(0f, -1f, 0f));

            AssertVector(new Vector3(0f, 0f, -1f), force);
        }

        [Fact]
        public void メイドのY軸回転にも追従する()
        {
            var bip01 = TestQuaternions.AroundY(90f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(0f, 0f, 1f));

            AssertVector(new Vector3(1f, 0f, 0f), force);
        }

        [Fact]
        public void FitToUnitBox_範囲内ならそのまま()
        {
            AssertVector(new Vector3(0.5f, -1f, 0f),
                GravityLocalSpace.FitToUnitBox(new Vector3(0.5f, -1f, 0f)));
        }

        [Fact]
        public void FitToUnitBox_はみ出したら最大成分が1になるよう方向を保って縮める()
        {
            var fitted = GravityLocalSpace.FitToUnitBox(new Vector3(2f, -1f, 0.5f));

            AssertVector(new Vector3(1f, -0.5f, 0.25f), fitted);
        }

        [Fact]
        public void 回転後も範囲内なら縮めない()
        {
            // (1,1,0) を Y 軸まわりに 45 度回すと (0.7071, 1, -0.7071)
            var bip01 = TestQuaternions.AroundY(45f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(1f, 1f, 0f));

            AssertVector(new Vector3(0.7071f, 1f, -0.7071f), force);
        }

        [Fact]
        public void 回転で成分が1を超えると方向を保って縮む()
        {
            // (1,0,1) を Y 軸まわりに 45 度回すと (1.4142, 0, 0) になる。最大成分 1 へ縮めて (1,0,0)
            var bip01 = TestQuaternions.AroundY(45f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(1f, 0f, 1f));

            AssertVector(new Vector3(1f, 0f, 0f), force);
        }
    }
}
```

Y 軸まわり +θ の回転は (x, z) → (x cosθ + z sinθ, −x sinθ + z cosθ)。(0,0,1) は 90° で (1,0,0)、(1,0,1) は 45° で (1.4142, 0, 0)、(1,1,0) は 45° で (0.7071, 1, −0.7071)。X 軸まわり +90° は (y, z) → (−z, y) なので (0,−1,0) → (0,0,−1)。

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」（spec「ループ運用」）を実行する。Expected: `GravityLocalSpace` が無くテストプロジェクトのコンパイルが失敗する。

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/GravityLocalSpace.cs`:

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力のローカル座標（Bip01 基準）の計算。
    /// 立ちポーズでワールドと一致し、体を倒すと重力も体に合わせて回るよう、
    /// Bip01 の回転から基準姿勢の回転を除いた分だけ offset を回す
    /// </summary>
    public static class GravityLocalSpace
    {
        /// <summary>
        /// メイドのルート回転が単位のときの Bip01 の基準姿勢（ワールド回転）。
        /// エディットの標準の立ちポーズ maid_stand01.anm の 0 秒を旧ボディで実測した値。
        /// 骨盤の傾きはポーズごとに違うため、ほかの立ちポーズではその分だけワールドからずれる
        /// </summary>
        public static readonly Quaternion Bip01BaseRotation =
            new Quaternion(-0.5415668f, 0.5415668f, 0.4546487f, 0.4546487f);

        /// <summary>基準姿勢からの体の回転</summary>
        public static Quaternion GetBodyRotation(Quaternion bip01Rotation)
        {
            // Quaternion.Inverse はネイティブ呼び出しでテストから使えないため、単位クォータニオンの共役で逆回転を作る
            var baseInverse = new Quaternion(
                -Bip01BaseRotation.x, -Bip01BaseRotation.y, -Bip01BaseRotation.z, Bip01BaseRotation.w);
            return bip01Rotation * baseInverse;
        }

        /// <summary>offset を体の回転に合わせて回し、ゲーム側の ±1 クランプで向きが崩れない範囲へ収める</summary>
        public static Vector3 ToForce(Quaternion bip01Rotation, Vector3 offset)
        {
            return FitToUnitBox(GetBodyRotation(bip01Rotation) * offset);
        }

        /// <summary>
        /// いずれかの成分の絶対値が 1 を超えたら、最大成分が 1 になるよう全体を縮める。
        /// GravityTransformControl は成分ごとに ±1 へ切り詰めるため、回転後の値をそのまま渡すと方向が変わる
        /// </summary>
        public static Vector3 FitToUnitBox(Vector3 v)
        {
            var max = Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
            if (max <= 1f)
            {
                return v;
            }
            return v / max;
        }
    }
}
```

csproj（`<Compile Include="MaidManipulation\MaidGravityController.cs" />` の直前）:

```xml
    <Compile Include="MaidManipulation\GravityLocalSpace.cs" />
```

- [ ] **Step 4: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 両構成ビルド成功、`GravityLocalSpaceTests` 全件 PASS、既存テストも PASS。

---

### Task 2: `MaidGravityController` に local を持たせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidGravityController.cs`

**Interfaces:**
- Consumes: `GravityLocalSpace.ToForce(Quaternion, Vector3)`
- Produces:
  - `public bool GetLocal(Maid maid, GravityCategory category)` — 記録が無ければ false
  - `public void SetLocal(Maid maid, GravityCategory category, bool value)`

ゲーム外テストは書けない（`GravityTransformControl` と `Maid` がゲーム実体）。実機確認（段 3）で確かめる。

- [ ] **Step 1: `Entry` に local と Bip01 のキャッシュを足す**

`Entry` の `offsets` の次に足す:

```csharp
            /// <summary>カテゴリごとのローカル（Bip01 基準）フラグ。無ければ OFF（ワールド）</summary>
            public readonly Dictionary<string, bool> locals = new Dictionary<string, bool>();

            public Maid maid;

            /// <summary>ローカル指定の回転の元。ボディ再ロードで破棄されたら取り直す</summary>
            public Transform bip01;
```

`GetOrCreateEntry` の新規作成を次に変える:

```csharp
            entry = new Entry { maid = maid, wasBusy = maid.IsAllProcPropBusy };
```

- [ ] **Step 2: `GetLocal` / `SetLocal` を足す**

`SetOffset` の直後に足す:

```csharp
        /// <summary>記録が無いメイドは既定 OFF（ワールド）として扱う</summary>
        public bool GetLocal(Maid maid, GravityCategory category)
        {
            var entry = GetEntry(maid);
            bool value;
            if (entry == null || !entry.locals.TryGetValue(category.id, out value))
            {
                return false;
            }
            return value;
        }

        public void SetLocal(Maid maid, GravityCategory category, bool value)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.locals[category.id] = value;
            ApplyCategory(entry, category);
        }
```

- [ ] **Step 3: `ApplyCategory` で回転後の値を書く**

`control.transform.localPosition = offset;` を次に置き換える:

```csharp
            control.transform.localPosition = IsLocal(entry, category)
                ? ToLocalForce(entry, offset)
                : offset;
```

`ApplyCategory` の直後に足す:

```csharp
        private static bool IsLocal(Entry entry, GravityCategory category)
        {
            bool local;
            return entry.locals.TryGetValue(category.id, out local) && local;
        }

        /// <summary>
        /// offset を Bip01 の回転に合わせて回す。
        /// Bip01 が取れない間（ボディ再ロード中など）はワールドとして扱い、取れた次のフレームで書き直す
        /// </summary>
        private static Vector3 ToLocalForce(Entry entry, Vector3 offset)
        {
            var bip01 = GetBip01(entry);
            if (bip01 == null)
            {
                return offset;
            }
            return GravityLocalSpace.ToForce(bip01.rotation, offset);
        }

        private static Transform GetBip01(Entry entry)
        {
            // 破棄済みの Transform は == null が true になるので取り直す
            if (entry.bip01 == null && entry.maid != null && entry.maid.body0 != null)
            {
                entry.bip01 = entry.maid.body0.GetBone("Bip01");
            }
            return entry.bip01;
        }
```

- [ ] **Step 4: `Update` で local ON のカテゴリを毎フレーム書き直す**

`Update` の `foreach (var control in entry.controls.Values) { ... control.OnChangeMekure(); ... }` の直後（`deadMaids` の処理より前、メイドごとのループ内）に足す:

```csharp
                // ローカル指定は体の向きに追従させるため毎フレーム確かめる。
                // 無効なカテゴリはゲーム側が力を 0 にするので書かない（有効化時に SetEnabled が書く）
                if (!entry.wasBusy)
                {
                    foreach (var category in categories)
                    {
                        if (IsLocal(entry, category) && GetEnabled(maid, category))
                        {
                            RefreshLocalForce(entry, category);
                        }
                    }
                }
```

`ToLocalForce` の直後に足す。ゲーム側は値が変わるたびに揺れものの `UpdateParameters()` を呼ぶため、ダンス中など Bip01 が常に動く場面で毎フレーム再計算させないよう、わずかな変化は書かない:

```csharp
        /// <summary>
        /// 毎フレームの追従で書き直す最小の変化（offset 空間の距離の 2 乗）。
        /// 0.005 は向きにして約 0.3°。ゲーム側は値が変わるたびに揺れものの UpdateParameters を呼ぶため、
        /// 骨盤の細かな揺れでは書かない
        /// </summary>
        private const float LOCAL_REFRESH_EPSILON_SQR = 0.005f * 0.005f;

        /// <summary>ローカル指定のカテゴリを Bip01 の今の回転で書き直す。変化がわずかなら書かない</summary>
        private void RefreshLocalForce(Entry entry, GravityCategory category)
        {
            GravityTransformControl control;
            if (!entry.controls.TryGetValue(category.id, out control) || control == null)
            {
                return;
            }

            Vector3 offset;
            if (!entry.offsets.TryGetValue(category.id, out offset))
            {
                offset = Vector3.zero;
            }
            var force = ToLocalForce(entry, offset);
            if ((force - control.transform.localPosition).sqrMagnitude < LOCAL_REFRESH_EPSILON_SQR)
            {
                return;
            }
            control.transform.localPosition = force;
        }
```

`RefreshLocalForce` は `wasBusy` と enabled を呼び出し側で見ているので、`ApplyCategory` と違い isEnabled は触らない。

- [ ] **Step 5: クラスの summary を直す**

クラス summary の「このクラスは「コンポーネントの生成」と「着替えで作り直された際の焼き直し」を担う。」を次に置き換える:

```csharp
    /// このクラスは「コンポーネントの生成」「着替えで作り直された際の焼き直し」と、
    /// ローカル指定（Bip01 基準）のカテゴリを体の回転に合わせて毎フレーム書き直すことを担う。
```

`CreateControls` の summary 中「置き場所の位置・回転は結果に影響しない」は正しいまま（回転は SE 側で掛けてから書く）なので変えない。

- [ ] **Step 6: ビルドする**

「ビルド＆テスト」を実行する。Expected: 両構成ビルド成功、全テスト PASS。

---

### Task 3: タイムライン（`TransformDataGravity` / `GravityTimelineLayer`）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataGravity.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/GravityTimelineLayer.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/GravityTimelineLayerTests.cs`

**Interfaces:**
- Consumes: `MaidGravityController.GetLocal` / `SetLocal`（Task 2）
- Produces:
  - `TransformDataGravity.Index.Local = 4`
  - `public const int LegacyValueCount = 4`
  - `public ValueData localValue`、`public bool local { get; set; }`
  - `isDefault` は `!enabled && !local && offset == Vector3.zero`

- [ ] **Step 1: 失敗するテストを書く**

`GravityTimelineLayerTests.cs` の `TransformDataGravity_は4値で有効フラグはBool型として扱われる` を次に置き換える:

```csharp
        [Fact]
        public void TransformDataGravity_は5値で有効フラグとローカルはBool型として扱われる()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.Gravity, trans.type);
            Assert.Equal(5, trans.valueCount);
            Assert.Equal(4, (int)MTEP.TransformDataGravity.Index.Local);
            Assert.Equal(4, MTEP.TransformDataGravity.LegacyValueCount);

            var infoMap = trans.GetCustomValueInfoMap();
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["enabled"].type);
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["local"].type);
            Assert.Equal(-1f, infoMap["x"].min);
            Assert.Equal(1f, infoMap["x"].max);
        }
```

`TransformDataGravity_のisDefaultは無効かつzeroのときだけtrueになる` の末尾（最後の `Assert.False` の後）に足す:

```csharp

            trans.offset = Vector3.zero;
            trans.local = true;
            Assert.False(trans.isDefault);
```

ファイル末尾（`GravityItemInspector_…` の後）に足す:

```csharp

        [Fact]
        public void TransformDataGravity_のlocalは値配列へ書き戻され補間対象に入らない()
        {
            var trans = CreateTransform();
            trans.local = true;

            Assert.True(trans.localValue.boolValue);
            Assert.Equal(1f, trans.values[(int)MTEP.TransformDataGravity.Index.Local].value);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(3)]
        public void 旧キーはローカルOFFで読む(int valueCount)
        {
            var values = new float[valueCount];
            values[0] = 1f;
            var xml = new MTEP.TransformXml
            {
                name = "hair",
                type = MTEP.TransformType.Gravity,
                values = values,
                inTangents = new float[valueCount],
                outTangents = new float[valueCount],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };

            var trans = CreateTransform();
            trans.local = true;
            trans.FromXml(xml);

            Assert.False(trans.local);
            Assert.True(trans.enabled);
        }

        [Fact]
        public void ローカルONのキーは往復で残る()
        {
            var trans = CreateTransform();
            trans.enabled = true;
            trans.local = true;
            trans.offset = new Vector3(0f, -1f, 0f);

            var restored = CreateTransform();
            restored.FromXml(trans.ToXml());

            Assert.True(restored.local);
            Assert.Equal(-1f, restored.offset.y);
        }
```

（`TransformXml` の組み方は `LightCookieHardnessKeyTests` と同じ。`TransformDataBase` に `public virtual TransformXml ToXml()` があるので往復テストはそれを使う）

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を実行する。Expected: `Index.Local` / `LegacyValueCount` / `local` が無くテストのコンパイルが失敗する。

- [ ] **Step 3: `TransformDataGravity` を実装する**

`Index` に足す:

```csharp
            Z = 3,
            Local = 4,
```

`valueCount` を変え、定数を足す:

```csharp
        public override int valueCount => 5;

        /// <summary>ローカル（index 4）を持たない旧キーの値数</summary>
        public const int LegacyValueCount = 4;
```

`CustomValueInfoMap` の `"z"` の後に足す:

```csharp
            {
                "local",
                new CustomValueInfo
                {
                    index = (int)Index.Local,
                    name = "ローカル",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
```

`enabledValue` の次に `localValue`、`enabled` の次に `local` を足す:

```csharp
        public ValueData localValue => values[(int)Index.Local];
```

```csharp
        /// <summary>offset を Bip01 の回転に追従させるか。有効フラグと同じく補間せず区間開始時に適用する</summary>
        public bool local
        {
            get => localValue.boolValue;
            set => localValue.boolValue = value;
        }
```

`isDefault` を変える:

```csharp
        /// <summary>既定値 (無効・ワールド・オフセット zero) か。適用を省く判定に使う</summary>
        public bool isDefault => !enabled && !local && offset == Vector3.zero;
```

クラス末尾に `FromXml` を足す:

```csharp
        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            // ローカルを持たない旧データ (旧 SE) は従来どおりワールドとして読む。
            // 不足分の埋め方に頼らず明示的に OFF にする
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                local = false;
            }
        }
```

クラス summary を「有効フラグとオフセット (-1〜1)、ローカル（Bip01 基準）フラグを持つ。有効・ローカルは補間せず区間開始時に適用し、オフセットは Tangent 補間する」に直す。

- [ ] **Step 4: `GravityTimelineLayer` を直す**

`ApplyMotion` の `if (indexUpdated) { ... }` を次に置き換える:

```csharp
            // 有効・ローカルは補間できないので区間の開始値をそのまま使う
            if (indexUpdated)
            {
                gravityController.SetLocal(maid, category, start.local);
                gravityController.SetEnabled(maid, category, start.enabled);
            }
```

`UpdateFrame` の `trans.offset = ...` の次に足す:

```csharp
                trans.local = gravityController.GetLocal(maid, category);
```

クラス summary の「有効フラグは区間開始時に適用し」を「有効フラグとローカルは区間開始時に適用し」に直す。

- [ ] **Step 5: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 全件 PASS。

---

### Task 4: シーンプリセットと Undo

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:227-238`（`ScenePresetGravity`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`CaptureGravity` / `ApplyGravity` / `IsDefaultGravity`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/GravitySnapshot.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetGravityXmlTests.cs`

**Interfaces:**
- Consumes: `MaidGravityController.GetLocal` / `SetLocal`（Task 2）
- Produces: `ScenePresetGravity.local`（`[XmlAttribute]`、false は書き出さない）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetGravityXmlTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットの重力のローカル（local 属性）の保存と読込を固定する。
    /// OFF は書き出さず、属性の無い旧プリセットは OFF（ワールド）として読む
    /// </summary>
    public class ScenePresetGravityXmlTests
    {
        private static string Serialize(ScenePresetGravity gravity)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetGravity));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, gravity);
                return writer.ToString();
            }
        }

        private static ScenePresetGravity Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetGravity));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetGravity)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void OFFは書き出さない()
        {
            var gravity = new ScenePresetGravity { category = "skirt", enabled = true };

            Assert.DoesNotContain("local", Serialize(gravity));
        }

        [Fact]
        public void ONを往復する()
        {
            var gravity = new ScenePresetGravity
            {
                category = "skirt",
                enabled = true,
                local = true,
                offset = new Vector3(0f, -1f, 0f),
            };

            var text = Serialize(gravity);

            Assert.Contains("local=\"true\"", text);
            var restored = Deserialize(text);
            Assert.True(restored.local);
            Assert.True(restored.enabled);
            Assert.Equal(-1f, restored.offset.y);
        }

        [Fact]
        public void 属性の無い旧プリセットはOFFで読む()
        {
            var restored = Deserialize(
                "<ScenePresetGravity category=\"hair\" enabled=\"true\"><offset><x>0</x><y>-1</y><z>0</z></offset></ScenePresetGravity>");

            Assert.False(restored.local);
            Assert.True(restored.enabled);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を実行する。Expected: `ScenePresetGravity.local` が無くコンパイルが失敗する。

- [ ] **Step 3: `ScenePresetGravity` に local を足す**

`public bool enabled;` の次に足す:

```csharp

        /// <summary>
        /// offset を Bip01 の回転に追従させるか。OFF（ワールド）では書き出さない。
        /// 属性の無い旧プリセットは OFF として読め、従来の見た目と変わらないため版は上げていない
        /// </summary>
        [XmlAttribute]
        public bool local;

        [XmlIgnore] public bool localSpecified { get { return local; } set { } }
```

- [ ] **Step 4: `ScenePresetManager` を直す**

`CaptureGravity` の初期化子に足す:

```csharp
                    local = controller.GetLocal(maid, category),
```

`ApplyGravity` のループを次にする（local を先に入れ、offset の書き込みで回転後の値が 1 回で決まるようにする）:

```csharp
                controller.SetLocal(maid, category, entry.local);
                controller.SetOffset(maid, category, entry.offset);
                controller.SetEnabled(maid, category, entry.enabled);
```

`IsDefaultGravity` を変える:

```csharp
        /// <summary>重力が既定値（無効・ワールド・オフセット 0）か</summary>
        private static bool IsDefaultGravity(ScenePresetGravity entry)
        {
            return !entry.enabled && !entry.local && entry.offset == Vector3.zero;
        }
```

- [ ] **Step 5: `GravitySnapshot` を直す**

summary を「重力のスナップショット (カテゴリごとの有効フラグ・ローカル・オフセット)」にする。フィールドに `private bool[] _locals;` を足す。

`Capture`:

```csharp
            var locals = new bool[categories.Count];
```

ループ内に `locals[i] = controller.GetLocal(maid, categories[i]);`、初期化子に `_locals = locals,` を足す。

`Apply` のループ:

```csharp
                controller.SetLocal(maid, categories[i], _locals[i]);
                controller.SetOffset(maid, categories[i], _offsets[i]);
                controller.SetEnabled(maid, categories[i], _enabled[i]);
```

`Approximately` の `_enabled[i] != o._enabled[i]` を `_enabled[i] != o._enabled[i] || _locals[i] != o._locals[i]` にする。

`IsAllDefault` の条件を `_enabled[i] || _locals[i] || _offsets[i] != Vector3.zero` にし、summary を「全カテゴリが既定値（無効・ワールド・オフセット 0）か」にする。

- [ ] **Step 6: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 全件 PASS。

---

### Task 5: UI とドキュメント

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/GravityRowDrawer.cs`
- Modify: `docs-site/guide/maid-editing.md`
- Modify: `docs-site/timeline/layers-maid.md`
- Modify: `docs-site/timeline/compatibility.md`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（リポジトリ外。コミット対象外）

**Interfaces:**
- Consumes: `MaidGravityController.GetLocal` / `SetLocal`（Task 2）

- [ ] **Step 1: `GravityRowDrawer` にトグルを足す**

「有効」「リセット」の行（`view.EndLayout();`）の直後、`var offset = ...` の前に 1 行足す。タイムラインのインスペクタは幅が狭いので、同じ行に並べず別の行にする:

```csharp

            view.DrawToggle("ローカル（Bip01）", gravityController.GetLocal(target, category),
                -1, rowHeight,
                value =>
                {
                    RecordEdit(target, category, "ローカル");
                    gravityController.SetLocal(target, category, value);
                });
```

（幅 -1 で残り幅いっぱいにするのは `BackgroundRowDrawer.cs:87` の「地面を表示」と同じ呼び方）

クラス summary の「(有効トグル・リセット・XYZ スライダー)」を「(有効トグル・リセット・ローカルトグル・XYZ スライダー)」にする。リセットボタンは offset だけを 0 に戻す既存の挙動のまま（ローカルは触らない）。

- [ ] **Step 2: ユーザー向けドキュメント**

`docs-site/guide/maid-editing.md` の `## ボーン編集` の直前に足す:

```markdown
## 重力

`メイド` → `重力` のウィンドウで、髪・スカートの揺れものにかかる力の向きを、メイドごとに変えられます。`髪` / `スカート` のタブで切り替えます。

- `有効` で力を掛けます。`X` / `Y` / `Z` で向きと強さ（各 -1〜1）を決めます
- `ローカル（Bip01）` を ON にすると、`X` / `Y` / `Z` を体（腰の Bip01）の向きに合わせた軸として扱います。寝そべりなどで体を倒すと重力も一緒に回ります。OFF のときはワールドの向きです
- ON と OFF で向きがそろうのは、エディットの標準の立ちポーズ（`maid_stand01`）のときです。腰の傾きはポーズごとに違うので、ほかの立ちポーズでは数度〜20° ほどずれます
- 斜めの向き（例: `X` と `Z` がどちらも 1）では、体の向きによって力の強さが少し変わります。各軸 -1〜1 の範囲に収めるためです
- 体の向きへの追従は 1 フレーム遅れます
- タイムラインの「メイド重力」レイヤーとシーンプリセットにも、ローカルの ON / OFF が保存されます。ローカルは補間せず、キーの区間の始まりで切り替わります

```

- [ ] **Step 2b: タイムラインの docs**

`docs-site/timeline/layers-maid.md` の「メイド重力」節の表を次にする:

```markdown
| 値 | 既定 | 補間 |
|---|---|---|
| `有効` | OFF | なし（区間開始値） |
| `ローカル` | OFF | なし（区間開始値） |
| `X` / `Y` / `Z` | 0 | タンジェント補間（-1〜1） |
```

表の直後の段落の後に 1 行足す:

```markdown
`ローカル` が ON の区間では、`X` / `Y` / `Z` を体（Bip01）の向きに合わせた軸として扱います（[メイド編集の「重力」](../guide/maid-editing.md#重力)）。ローカルを持たない以前のキーは OFF として読み込まれます。
```

`docs-site/timeline/compatibility.md` の「`メイド表情` の `強制上書き` キーと `メイド重力` レイヤーは SceneEditor 独自です。…」の行の直後に足す:

```markdown
- `メイド重力` の `ローカル` は以前の SceneEditor では読み飛ばされ、ワールドの向きとして表示されます
```

`docs-site/guide/scene-preset.md` は「重力」を項目名として挙げているだけなので変えない。

- [ ] **Step 3: CLAUDE.md の互換の注意**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」節の末尾に足す:

```markdown
- 重力キーのローカル（index 4、値数 5、Bip01 基準）は SE 独自。version は上げていない。4 値以下の旧キーは `TransformDataGravity.FromXml` が OFF（ワールド）に補正する。重力レイヤー自体が SE 独自で MTE には無い。旧 SE は 5 値目を読み飛ばしワールドとして表示する。シーンプリセットは版を上げずに `ScenePresetGravity` へ `local` 属性（OFF では書き出さない）を追加。属性の無い旧プリセットは OFF で読む
```

- [ ] **Step 4: ビルド＆テスト**

「ビルド＆テスト」を実行する。Expected: 両構成ビルド成功、全テスト PASS。

---

## 実機確認（ループの段 3 で行う。計画の実装範囲外）

spec「B. 実機確認」の項目を devbridge で行う。補足:

- `MaidGravityController` は `MaidManipulateManager.instance.gravityController`。`SetEnabled` / `SetOffset` / `SetLocal` をリフレクションで呼び、翌フレーム以降に `EW_Gravity_skirt` の `GravityTransformControl.transform.localPosition` を読む
- ローカル ON のメイドにダンス等の動きの大きいモーションを再生させ、`RefreshLocalForce` の書き込みで fps が目に見えて落ちないこと、`tail_log` に例外が無いことを見る（`UpdateParameters` の毎フレーム呼び出しのコストは未計測のため）
- 立ちポーズの一致は `maid_stand01.anm` を再生して確かめる。ほかの立ちポーズでは骨盤の傾き分ずれるのが仕様（「基準姿勢の実測結果」）
- Bip01 を回す確認はアニメ再生で上書きされるので、メイドのルート（`maid.transform`）を X 軸まわりに 90° 回す方法を第一とする（R にはルート回転も入るため同じ検証になる）。期待値は `GravityLocalSpace.ToForce(bip01.rotation, offset)` を eval 内で同じ式で計算した値と、成分誤差 0.01 以内で一致すること。確認後にルート回転を戻す
- CRC ボディのメイドがいれば、`maid_stand01.anm` の Bip01 を「基準姿勢の実測結果」と同じ手順で測り、`GravityLocalSpace.Bip01BaseRotation` と一致するか（成分誤差 0.01 以内、符号反転は同じ回転）確かめる。いなければユーザー確認待ちに回す

## レビュー却下メモ

plan-review（🟡、取り込み 7 件・却下 2 件）:

- 取り込み: 基準姿勢を bindpose から `maid_stand01` の実測値へ変更（実測で bindpose と約 10° 差を確認）、docs（layers-maid / compatibility）の追記、毎フレーム書き込みのしきい値、`FromXml` の null ガード、テスト名の方向語、斜め方向の強さが向きで変わる旨の docs 追記、ローカルトグルを別の行へ
- 旧キー補正テストが不足分の 0 埋めでも通ってしまう — 明示補正は将来の既定値変更への備えとして残し、テストはその結果を固定するもので十分。退行検知の弱さは許容
- Bip01 が見つからない間 `GetBone` が毎フレーム検索になる — ローカル ON かつ有効なカテゴリがあり、かつ Bip01 が無い（ボディ再ロード中の短い間）ときだけで、`CMT.SearchObjName` はボーン数百本の走査にとどまる。試行間隔を設ける複雑さに見合わない
