# メイドスケールのウィンドウ編集とシーンプリセット対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイドスケール（腕 6 本の均一倍率）を、ボーンウィンドウの「腕スケール」タブから編集でき、シーンプリセット（v38）で保存・復元できるようにする。

**Architecture:**
- シーンプリセットは `ScenePresetMaid.maidScale`（`ScenePresetMaidScale`）を足し、変換と読み取りは Maid を受けない純粋関数にしてテストで固定する。保存・適用は `ScenePresetManager` の重力と同じ場所で呼ぶ
- プラグインの `OnGUI` でウィンドウを描く間だけ、掛けた倍率を外す（`SuspendApplied()`）。描き終えたら掛け直す（`ResumeApplied()`）。GUI（ボーンウィンドウ）には倍率を含まない素の値を見せつつ、フレーム末の撮影（`WaitForEndOfFrame` の後の `camera.Render()`）には倍率を写す
- 行描画を `MaidScaleRowDrawer` に切り出し、Inspector とボーンウィンドウの新タブで共有する

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Unity IMGUI（`GUIView`）、XmlSerializer、xUnit、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-03-maid-scale-window-preset-design.md`（前提: `docs/superpowers/specs/2026-10-03-maid-scale-design.md`）

## Global Constraints

- ブランチは `feature/maid-scale` のまま続ける（メイドスケール本体の上に積む）
- `ScenePresetData.CurrentVersion` は 37 → 38。`TimelineData.CurrentVersion` は上げない
- プリセットの要素名は `maidScale`、子要素は `bone`（属性 `name` / `scale`）
- タブ名は「腕スケール」、全リセットのボタン名は「すべて 1 に戻す」
- 履歴のスコープは既存の `HistoryScope.MaidScale`、説明文は `"メイドスケール: " + ラベル`
- ボーンウィンドウの PartsEdit 互換プリセットには含めない
- コードのコメントとエラーログは日本語で書く
- **2 構成のビルド**: `COM3D2`（.NET 3.5）と `COM3D25`（.NET 4.7.1）の両方を通す
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild を直接叩く
- テストでは Unity のネイティブ呼び出しを使わない（`Mathf.Clamp`・`Vector3` の演算・XmlSerializer は可）
- プラグイン本体の csproj は `<Compile Include>` を明示列挙している。新しいファイルは必ず追記する（テスト側は SDK 形式なので不要）

## Review Focus

- **手で書き換えたプリセットの不正な倍率**: `scale="NaN"` を読んでも腕が消えず倍率 1 として扱う。範囲外（`scale="10"`）は 3 に丸める — Task 1 のテストで固定する（`MaidScaleBones.Clamp` の NaN 対策を含む）
- **旧プリセット**: `maidScale` 要素の無い v37 以前のプリセットを適用しても、今の倍率を変えない — Task 1 のテスト（null で読める）と Task 4 の実機検証
- **全骨 1 のプリセット**: 空の `<maidScale />` を適用すると、拡縮中のメイドが元の大きさへ戻る — Task 1 のテスト（空リストで読める）と Task 4 の実機検証
- **ボーンウィンドウで倍率つきの腕を編集**: 前腕を 1.5 倍にした状態で「編集」タブから前腕を選ぶと、スケール欄は素の値を示し、スケールを編集・リセットしても倍率が焼き込まれない — Task 2 の実装、Task 4 の実機検証
- **フレーム末の撮影**: スクリーンショット（`ScreenshotHotkeyPatch` → `WaitForEndOfFrame` → `ScreenshotManager.Capture` の `camera.Render()`）とサムネイルに、GUI を描いた後も倍率が写る — Task 2 の実装（resume）、Task 4 の実機検証
- **タイムラインでメイドアニメのゲートが閉じている**: タイムライン読込中にそのメイドがタイムラインにいない等で、ボーン編集のゲートが無効化していても、タブバーは押せて「腕スケール」へ移れる — Task 3 の実装（ゲートをタブバーの後へ移す）、Task 4 の実機検証
- **モデル対象への切替**: 「腕スケール」タブを開いたまま対象を「モデル」に切り替えると、タブは「編集」に戻り、モデル側に「腕スケール」は出ない — Task 3 の実装、Task 4 の実機検証

## ファイル構成

| ファイル | 役割 |
|---|---|
| Modify `<P>/MaidManipulation/MaidScaleBones.cs` | `Clamp` が NaN を 1 にする |
| Modify `<P>/ScenePresetData.cs` | `ScenePresetMaidScaleBone` / `ScenePresetMaidScale` の追加、`ScenePresetMaid.maidScale`、v38 |
| Modify `<P>/Manager/ScenePresetManager.cs` | `CaptureMaidScale` / `ApplyMaidScale` と呼び出し |
| Modify `<P>/MaidManipulation/MaidScaleController.cs` | `SuspendApplied()` / `ResumeApplied()` |
| Modify `<P>/COM3D2.SceneEditor.Plugin.cs` | `OnGUI` でウィンドウ描画を suspend / resume で挟む |
| Create `<P>/MaidScaleRowDrawer.cs` | 倍率スライダー 1 行と「すべて 1 に戻す」ボタン |
| Modify `<P>/Timeline/ItemInspector/MaidScaleItemInspector.cs` | 行描画を `MaidScaleRowDrawer` へ委譲 |
| Modify `<P>/MaidWindowBase.cs` | 見出し指定版の `DrawInnerTabs` |
| Modify `<P>/BoneEditWindow.cs` | 「腕スケール」タブ、モデル時のタブ絞り込み、タイムラインからのフォーカス |
| Modify `<P>/COM3D2.SceneEditor.Plugin.csproj` | `MaidScaleRowDrawer.cs` を追記 |
| Test `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs` | プリセット要素のテスト |
| Modify `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs` | NaN の丸め |
| Modify `W:\COM3D2_5\work\CLAUDE.md` | メイドスケールの互換メモへシーンプリセット v38 を追記（git 管理外） |

以下、`source/COM3D2.SceneEditor.Plugin/` 配下のパスは `<P>/` と略す。

## ビルド・テストのコマンド

Git Bash から実行する。順番は「COM3D2 → COM3D25 → dotnet test」（COM3D2 構成のビルドが `bin/Debug/COM3D25/` を消すため）。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

以下では、これを「**ビルド＆テスト**」と呼ぶ。特定のテストだけ回すときは、末尾を `dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~<名前>"` に置き換える。

---

### Task 1: シーンプリセット v38 の `maidScale`

**Files:**
- Modify: `<P>/MaidManipulation/MaidScaleBones.cs`（`Clamp`）
- Modify: `<P>/ScenePresetData.cs`（`ScenePresetGravity` の次にクラス 2 つ、`ScenePresetMaid.gravity` の次にフィールド、`CurrentVersion` の直前に版コメント）
- Modify: `<P>/Manager/ScenePresetManager.cs`（`state.gravity = CaptureGravity(maid);` の次、`ApplyGravity(maid, state);` の try ブロックの次、`CaptureGravity` / `ApplyGravity` の隣にメソッド）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs`（新規）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`（追記）

**Interfaces:**
- Consumes: 既存の `MaidScaleBones.bones` / `Find` / `Clamp` / `IsDefault` / `DefaultScale`、`MaidScaleController.GetScale` / `SetScale`
- Produces:
  - `COM3D2.SceneEditor.Plugin.ScenePresetMaidScaleBone`（`string name`、`float scale`）
  - `COM3D2.SceneEditor.Plugin.ScenePresetMaidScale`
    - `List<ScenePresetMaidScaleBone> bones`
    - `static ScenePresetMaidScale FromScales(IEnumerable<KeyValuePair<string, float>> scales)`
    - `float GetScale(string boneName)`
  - `ScenePresetMaid.maidScale`（`ScenePresetMaidScale`、旧プリセットは null）
  - `ScenePresetData.CurrentVersion == 38`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのメイドスケール (v38) を固定する</summary>
    public class ScenePresetMaidScaleTests
    {
        private static string Serialize(ScenePresetMaid maid)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, maid);
                return writer.ToString();
            }
        }

        private static ScenePresetMaid Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaid)serializer.Deserialize(reader);
            }
        }

        private static KeyValuePair<string, float> Pair(string name, float scale)
        {
            return new KeyValuePair<string, float>(name, scale);
        }

        [Fact]
        public void 倍率1と対象外の骨は書き出さない()
        {
            var preset = ScenePresetMaidScale.FromScales(new[]
            {
                Pair("Bip01 L Hand", 1.5f),
                Pair("Bip01 R Hand", 1f),
                Pair("Bip01 Head", 2f),
            });

            Assert.Single(preset.bones);
            Assert.Equal("Bip01 L Hand", preset.bones[0].name);
            Assert.Equal(1.5f, preset.bones[0].scale);
        }

        [Fact]
        public void XMLの往復で倍率を保ち記録の無い骨は1になる()
        {
            var maid = new ScenePresetMaid
            {
                maidScale = ScenePresetMaidScale.FromScales(new[] { Pair("Bip01 L Hand", 1.5f) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<bone name=\"Bip01 L Hand\" scale=\"1.5\" />", text);

            var loaded = Deserialize(text);
            Assert.Equal(1.5f, loaded.maidScale.GetScale("Bip01 L Hand"));
            Assert.Equal(1f, loaded.maidScale.GetScale("Bip01 R Hand"));
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaid><visible>true</visible></ScenePresetMaid>";

            Assert.Null(Deserialize(text).maidScale);
        }

        [Fact]
        public void 全骨1でも空要素を書き空リストとして読む()
        {
            var maid = new ScenePresetMaid
            {
                maidScale = ScenePresetMaidScale.FromScales(new[] { Pair("Bip01 L Hand", 1f) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<maidScale />", text);

            var loaded = Deserialize(text);
            Assert.NotNull(loaded.maidScale);
            Assert.Empty(loaded.maidScale.bones);
        }

        [Fact]
        public void 同じ骨が複数あれば先の値を使う()
        {
            var preset = new ScenePresetMaidScale();
            preset.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L Hand", scale = 1.5f });
            preset.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L Hand", scale = 2f });

            Assert.Equal(1.5f, preset.GetScale("Bip01 L Hand"));
        }

        [Theory]
        [InlineData("NaN", 1f)]
        [InlineData("10", 3f)]
        [InlineData("0", 0.1f)]
        public void 不正な倍率は範囲へ丸めNaNは1にする(string scaleText, float expected)
        {
            var text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaid><maidScale><bone name=\"Bip01 L Hand\" scale=\"" + scaleText + "\" />" +
                "</maidScale></ScenePresetMaid>";

            Assert.Equal(expected, Deserialize(text).maidScale.GetScale("Bip01 L Hand"));
        }

        [Fact]
        public void シーンプリセットの版は38()
        {
            Assert.Equal(38, ScenePresetData.CurrentVersion);
        }
    }
}
```

`MaidScaleTests.cs` の `倍率は範囲内へ丸める` の直後に追加する:

```csharp
        [Fact]
        public void NaNの倍率は元の大きさとして扱う()
        {
            Assert.Equal(1f, MaidScaleBones.Clamp(float.NaN));
            Assert.True(MaidScaleBones.IsDefault(float.NaN));
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScale"` で実行する。
期待: `ScenePresetMaidScale` / `ScenePresetMaidScaleBone` / `ScenePresetMaid.maidScale` 未定義（CS0246 / CS0117）でテストのコンパイルが失敗する。

- [ ] **Step 3: `Clamp` を NaN に強くする**

`<P>/MaidManipulation/MaidScaleBones.cs` の `Clamp` を置き換える。`Mathf.Clamp` は NaN をそのまま返すため、手で書き換えたプリセットの NaN が骨のスケールに入ると腕が消える。

```csharp
        /// <summary>範囲へ丸める。NaN (手で書き換えたプリセットなど) は元の大きさにする</summary>
        public static float Clamp(float scale)
        {
            if (float.IsNaN(scale))
            {
                return DefaultScale;
            }
            return Mathf.Clamp(scale, MinScale, MaxScale);
        }
```

- [ ] **Step 4: プリセットのクラスを追加する**

`<P>/ScenePresetData.cs` の `ScenePresetGravity` クラスの閉じ括弧の次に追加する。

```csharp

    /// <summary>メイドスケールの骨 1 本分 (v38)</summary>
    public class ScenePresetMaidScaleBone
    {
        /// <summary>MaidScaleBones の骨名</summary>
        [XmlAttribute]
        public string name;

        [XmlAttribute]
        public float scale = MaidScaleBones.DefaultScale;
    }

    /// <summary>
    /// メイドスケール (v38)。倍率が 1 でない骨だけを持つ。
    /// 全骨 1 でも要素自体は書き、旧プリセット (要素なし = null) と区別する
    /// </summary>
    public class ScenePresetMaidScale
    {
        [XmlElement("bone")]
        public List<ScenePresetMaidScaleBone> bones = new List<ScenePresetMaidScaleBone>();

        /// <summary>骨名と倍率の組から作る。倍率 1 と対象外の骨は書かない</summary>
        public static ScenePresetMaidScale FromScales(IEnumerable<KeyValuePair<string, float>> scales)
        {
            var result = new ScenePresetMaidScale();
            foreach (var pair in scales)
            {
                if (MaidScaleBones.Find(pair.Key) == null || MaidScaleBones.IsDefault(pair.Value))
                {
                    continue;
                }
                result.bones.Add(new ScenePresetMaidScaleBone
                {
                    name = pair.Key,
                    scale = MaidScaleBones.Clamp(pair.Value),
                });
            }
            return result;
        }

        /// <summary>
        /// 骨の倍率。記録の無い骨は 1。同じ骨が複数あれば先のものを使う。
        /// 手で書き換えた値に備えて範囲へ丸める
        /// </summary>
        public float GetScale(string boneName)
        {
            foreach (var bone in bones)
            {
                if (bone != null && bone.name == boneName)
                {
                    return MaidScaleBones.Clamp(bone.scale);
                }
            }
            return MaidScaleBones.DefaultScale;
        }
    }
```

`ScenePresetMaid` の `gravity` フィールドの次に追加する。

```csharp

        /// <summary>メイドスケール (v38)。旧プリセットは null になり、適用時に倍率へ触らない</summary>
        public ScenePresetMaidScale maidScale;
```

`CurrentVersion` の直前の版コメント（v37 の行の次）に追記し、値を 38 にする。

```csharp
        // v38: maid に maidScale (メイドスケール。腕の骨の倍率で、1 以外の骨だけを bone 要素に持つ) を追加。
        //      全骨 1 でも空要素を書く。旧形式は null で読め、適用時に倍率へ触らない
        public static readonly int CurrentVersion = 38;
```

- [ ] **Step 5: テストが通ることを確かめる**

「ビルド＆テスト」を `--filter "FullyQualifiedName~MaidScale"` で実行する。
期待: 2 構成のビルドが成功し、`ScenePresetMaidScaleTests` と `MaidScaleTests` が全件 PASS する。

- [ ] **Step 6: 保存と適用を組み込む**

`<P>/Manager/ScenePresetManager.cs`

`CaptureGravity` メソッドの次に追加する。

```csharp

        /// <summary>
        /// メイドスケールを記録する。全骨 1 でも空要素を残し、
        /// 適用時に前のシーンの倍率が残らないようにする
        /// </summary>
        private static ScenePresetMaidScale CaptureMaidScale(Maid maid)
        {
            var controller = maidManager.maidScaleController;
            return ScenePresetMaidScale.FromScales(MaidScaleBones.bones.Select(
                bone => new KeyValuePair<string, float>(bone.boneName, controller.GetScale(maid, bone.boneName))));
        }
```

`ApplyGravity` メソッド（と `IsDefaultGravity`）の次に追加する。

```csharp

        /// <summary>
        /// メイドスケールを復元する。旧プリセット (maidScale 無し) では変更しない。
        /// 記録の無い骨は 1 へ戻す
        /// </summary>
        private static void ApplyMaidScale(Maid maid, ScenePresetMaid state)
        {
            if (state.maidScale == null)
            {
                return;
            }

            // 状態の無いメイドへ 1 を書いても SetScale は状態を作らないので、
            // ApplyGravity のような既定値だけのプリセットの判定は要らない
            var controller = maidManager.maidScaleController;
            foreach (var bone in MaidScaleBones.bones)
            {
                controller.SetScale(maid, bone.boneName, state.maidScale.GetScale(bone.boneName));
            }
        }
```

保存: `state.gravity = CaptureGravity(maid);` の次の行に追加する。

```csharp
            state.maidScale = CaptureMaidScale(maid);
```

適用: `ApplyGravity(maid, state);` を囲む try / catch の次に、同じ形で追加する。

```csharp
            try
            {
                ApplyMaidScale(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
```

- [ ] **Step 7: 全体のビルドとテスト**

「ビルド＆テスト」を実行する（フィルタなし）。
期待: 2 構成のビルドが成功し、全件 PASS する。

- [ ] **Step 8: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleBones.cs \
  source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs
git commit -m "feat(preset): シーンプリセット v38 でメイドスケールを保存・復元する"
```

---

### Task 2: GUI には倍率を含まない骨を見せる

**Files:**
- Modify: `<P>/MaidManipulation/MaidScaleController.cs`（`Entry` にフラグ、`RestoreApplied` の次にメソッド 2 つ、クラスコメント）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.cs`（`OnGUI`）

**Interfaces:**
- Consumes: `MaidScaleController` の既存 private `Restore(Entry)` / `ApplyScales(Entry)`
- Produces:
  - `MaidScaleController.SuspendApplied()`
  - `MaidScaleController.ResumeApplied()`

OnGUI は描画の後に走るが、`TBody.LateUpdate` の直後に掛けた倍率は次の Update まで残る。そのまま GUI を描くと、ボーンウィンドウ（`BoneEditManager`）が倍率込みのスケールを表示・元値として記録・焼き込みする。一方で、スクリーンショット（`ScreenshotHotkeyPatch` は `WaitForEndOfFrame` の後に `ScreenshotManager.Capture` の `camera.Render()` を呼ぶ）とサムネイルは OnGUI の後に描くので、倍率を外したままにすると撮影に写らない。そこで、ウィンドウを描く間だけ外して、描き終えたら掛け直す。

GUI のイベントとフレームの順序に依存する処理で（`Maid` は MonoBehaviour なのでテストで作れない）、Unity なしのテストにはできない。ビルドの成功と Task 4 の実機検証で確かめる。

- [ ] **Step 1: `SuspendApplied` / `ResumeApplied` を足す**

`<P>/MaidManipulation/MaidScaleController.cs`

`Entry` クラスの `applied` フィールドの次に追加する。

```csharp

            /// <summary>GUI の間だけ外している。ResumeApplied で掛け直す</summary>
            public bool isSuspended;
```

`RestoreApplied` メソッドの次に追加する。

```csharp

        /// <summary>
        /// GUI が骨を読み書きする間だけ、掛けた分を外す。ResumeApplied と対で呼ぶ。
        /// ボーンウィンドウに倍率込みのスケールを表示・記録・焼き込みさせないため
        /// </summary>
        public void SuspendApplied()
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.applied.Count == 0)
                {
                    continue;
                }
                Restore(entry);
                entry.isSuspended = true;
            }
        }

        /// <summary>
        /// SuspendApplied で外した分を掛け直す。GUI が書き換えた値はその値を元として掛ける。
        /// OnGUI の後に描くフレーム末の撮影 (camera.Render) にも倍率を写すため。
        /// GUI の中で状態ごと捨てたメイドは _entries に無いので掛け直さない
        /// </summary>
        public void ResumeApplied()
        {
            foreach (var entry in _entries.Values)
            {
                if (!entry.isSuspended)
                {
                    continue;
                }
                entry.isSuspended = false;
                ApplyScales(entry);
            }
        }
```

クラスコメントの「次フレームの Update と TBody.LateUpdate の直前で元へ戻す。」の次の行に「GUI を描く間だけは外して掛け直す (SuspendApplied / ResumeApplied)。」を足す。

- [ ] **Step 2: `OnGUI` で挟む**

`<P>/COM3D2.SceneEditor.Plugin.cs` の `OnGUI` を次のようにする。ウィンドウ描画で例外が出ても掛け直すよう `finally` で戻す。

```csharp
        public void OnGUI()
        {
            try
            {
                if (isEnable)
                {
                    var scaleController = MaidManipulateManager.instance.maidScaleController;
                    scaleController.SuspendApplied();
                    try
                    {
                        windowManager.OnGUI();
                    }
                    finally
                    {
                        scaleController.ResumeApplied();
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

`Update` 冒頭の `RestoreApplied()` はそのまま残す。

- [ ] **Step 3: ビルドとテスト**

「ビルド＆テスト」を実行する（フィルタなし）。
期待: 2 構成のビルドが成功し、全件 PASS する。

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs \
  source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleController.cs
git commit -m "fix(maid): ウィンドウ描画の間だけメイドスケールを外し、ボーンウィンドウへ倍率込みの値を見せない"
```

---

### Task 3: ボーンウィンドウの「腕スケール」タブ

**Files:**
- Create: `<P>/MaidScaleRowDrawer.cs`
- Modify: `<P>/Timeline/ItemInspector/MaidScaleItemInspector.cs`
- Modify: `<P>/MaidWindowBase.cs`（既存の `DrawInnerTabs<T>` の次）
- Modify: `<P>/BoneEditWindow.cs`（`BoneTabType`、`TryFocusTimelineLayer`、`DrawContentTabs`）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`（`GravityRowDrawer.cs` の次）

**Interfaces:**
- Consumes: `MaidScaleBones`、`MaidScaleController.GetScale` / `SetScale` / `HasState`、`HistoryManager.instance.BeforeEdit(Maid, HistoryScope, string)`、`GUIView.DrawSliderValue` / `DrawButton(string, float, float, bool enabled)` / `DrawTabs(IList<string>, int, float, float)`、`TimelineLayerGate.Begin(GUIView, Type, Maid, float)`（解除は `MaidWindowBase.DrawContent` の finally が呼ぶ `End`）
- Produces:
  - `MaidScaleRowDrawer.Draw(GUIView view, Maid maid, MaidScaleBone bone, string label, float labelWidth)`
  - `MaidScaleRowDrawer.DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)`
  - `MaidWindowBase.DrawInnerTabs(IList<string> labels, int currentIndex, float width)`

IMGUI の描画で、Unity なしのテストにはできない。ビルドの成功と Task 4 の実機検証で確かめる。spec の `Draw(GUIView, Maid, MaidScaleBone, float rowHeight)` は、スライダーが行の高さを取らず、ラベルが呼び出し側で違う（Inspector は「倍率」、ウィンドウは骨の表示名）ため、`label` / `labelWidth` を受ける形にする。

- [ ] **Step 1: `MaidScaleRowDrawer` を作る**

`<P>/MaidScaleRowDrawer.cs`:

```csharp
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールの骨 1 本分の倍率スライダーと、全骨を元の大きさへ戻すボタン。
    /// ボーンウィンドウの「腕スケール」タブと TimelineItemInspector (メイドスケールレイヤーの項目表示) で共有する。
    /// 値の読み書きは MaidScaleController を通し、操作は履歴に記録する
    /// </summary>
    public static class MaidScaleRowDrawer
    {
        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

        public static void Draw(GUIView view, Maid maid, MaidScaleBone bone, string label, float labelWidth)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = MaidScaleBones.MinScale,
                max = MaidScaleBones.MaxScale,
                defaultValue = MaidScaleBones.DefaultScale,
                value = scaleController.GetScale(maid, bone.boneName),
                onChanged = newValue =>
                {
                    RecordEdit(maid, bone.displayName);
                    scaleController.SetScale(maid, bone.boneName, newValue);
                },
            });
        }

        /// <summary>
        /// 6 本すべてを元の大きさへ戻す。履歴は 1 件にまとめる。
        /// 拡縮していないメイドでは無変更の履歴を積まないよう押せなくする
        /// </summary>
        public static void DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)
        {
            if (!view.DrawButton("すべて 1 に戻す", width, rowHeight, scaleController.HasState(maid)))
            {
                return;
            }

            RecordEdit(maid, "すべて 1 に戻す");
            foreach (var bone in MaidScaleBones.bones)
            {
                scaleController.SetScale(maid, bone.boneName, MaidScaleBones.DefaultScale);
            }
        }

        /// <summary>メイドスケールの操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid maid, string label)
        {
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.MaidScale, "メイドスケール: " + label);
        }
    }
}
```

csproj（`<Compile Include="GravityRowDrawer.cs" />` の次）:

```xml
    <Compile Include="MaidScaleRowDrawer.cs" />
```

- [ ] **Step 2: Inspector を `MaidScaleRowDrawer` へ委譲する**

`<P>/Timeline/ItemInspector/MaidScaleItemInspector.cs`

`DrawItems` の `DrawScaleSlider(view, maid, bone);` を次に置き換える。

```csharp
                MaidScaleRowDrawer.Draw(view, maid, bone, "倍率", 40);
```

`DrawScaleSlider` メソッドと、使われなくなった `scaleController` プロパティを削除する。クラスコメントの「値は MaidScaleController を通して読み書きし、操作は履歴に記録する」は「行の描画と履歴への記録は MaidScaleRowDrawer に任せる」に直す。

- [ ] **Step 3: 見出し指定版の `DrawInnerTabs` を足す**

`<P>/MaidWindowBase.cs` の `DrawInnerTabs<T>` の次に追加する。

```csharp

        /// <summary>見出しを直接指定する版。対象によって出すタブを絞るときに使う</summary>
        protected int DrawInnerTabs(IList<string> labels, int currentIndex, float width)
        {
            var result = view.DrawTabs(labels, currentIndex, width, ROW_HEIGHT);
            view.currentPos.y -= 5 + GUIView.defaultMargin;
            return result;
        }
```

- [ ] **Step 4: ボーンウィンドウにタブを足す**

`<P>/BoneEditWindow.cs`

`BoneTabType` に追加する（並びはタブの表示順。モデル用の見出しは先頭 2 つを使う）。

```csharp
        /// <summary>ウィンドウ内の内部タブ。腕スケールはメイドだけで出す</summary>
        private enum BoneTabType
        {
            編集,
            プリセット,
            腕スケール,
        }

        /// <summary>モデルを対象にしているときのタブ。BoneTabType の先頭 2 つと同じ並び</summary>
        private static readonly string[] MODEL_TAB_LABELS =
        {
            BoneTabType.編集.ToString(),
            BoneTabType.プリセット.ToString(),
        };
```

`TryFocusTimelineLayer` を次にする。

```csharp
        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.ModelBoneTimelineLayer))
            {
                SwitchTargetType(BoneEditTargetType.Model);
                return true;
            }
            if (layerType == typeof(MTEP.MaidScaleTimelineLayer))
            {
                SwitchTargetType(BoneEditTargetType.Maid);
                _tabType = BoneTabType.腕スケール;
                return true;
            }
            return false;
        }
```

`DrawMaidContent` の次の行を削除する（ゲートはタブバーの後へ移す）。

```csharp
            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), target, ROW_HEIGHT);
```

今はこのゲートがヘッダー行・スロット選択・タブバーより前に掛かっている。そのメイドがタイムラインにいないなどでゲートが閉じると、タブバーごと無効になり「腕スケール」（別レイヤー）へ移れない。`TimelineLayerGate` のコメント（タブ切替はゲートの対象外にし、タブを描いた後に Begin を呼ぶ）に合わせ、タブバーの後でタブごとのレイヤーのゲートを掛ける。ヘッダー行（ボーン表示トグル・PartsEdit プリセット保存）とスロット選択はタイムラインの値を書かないので、ゲートの外に出てよい。モデルモードのゲート（`ModelBoneTimelineLayer`）は今のまま `DrawMaidContent` で掛ける。

`DrawContentTabs` を次に置き換え、2 つのメソッドを足す。

```csharp
        /// <summary>
        /// 編集 / プリセット / 腕スケールタブとその中身。対象種別で描き分ける箇所は
        /// 各メソッドが activeSlotKey / GetActiveStore / GetActiveRootObject で吸収する
        /// </summary>
        private void DrawContentTabs(Maid target)
        {
            var prevTab = _tabType;
            _tabType = DrawContentTabBar();
            if (_tabType != prevTab && _tabType == BoneTabType.プリセット)
            {
                // フォルダを直接編集された場合もタブを開き直せば一覧に反映される
                RefreshPresetList();
            }

            // タブ切替はゲートの対象外にするため、タブを描いた後で判定する。
            // モデルモードのゲートは DrawMaidContent で掛け済み
            if (!boneEditManager.isModelMode)
            {
                var layerType = _tabType == BoneTabType.腕スケール
                    ? typeof(MTEP.MaidScaleTimelineLayer)
                    : typeof(MTEP.MotionTimelineLayer);
                TimelineLayerGate.Begin(view, layerType, target, ROW_HEIGHT);
            }

            if (_tabType == BoneTabType.プリセット)
            {
                DrawPresetContent(target);
            }
            else if (_tabType == BoneTabType.腕スケール)
            {
                DrawMaidScaleContent(target);
            }
            else
            {
                DrawResetButtons(target);
                DrawBoneTree(target);
            }
        }

        /// <summary>腕スケールはメイド専用なので、モデルでは編集 / プリセットだけを出す</summary>
        private BoneTabType DrawContentTabBar()
        {
            if (!boneEditManager.isModelMode)
            {
                return DrawInnerTabs(_tabType, TAB_WIDTH);
            }

            if (_tabType == BoneTabType.腕スケール)
            {
                _tabType = BoneTabType.編集;
            }
            return (BoneTabType)DrawInnerTabs(MODEL_TAB_LABELS, (int)_tabType, TAB_WIDTH);
        }

        /// <summary>腕 6 本の倍率 (メイドスケール)</summary>
        private void DrawMaidScaleContent(Maid target)
        {
            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var bone in MaidScaleBones.bones)
            {
                MaidScaleRowDrawer.Draw(view, target, bone, bone.displayName, 60);
            }
            MaidScaleRowDrawer.DrawResetAll(view, target, ResetButtonWidth, ROW_HEIGHT);

            view.EndScrollView();
        }
```

メイドモードでは `DrawMaidContent` がボディ未読込（「ボディが読み込まれていません」）・アイテム処理中（「プロパティ適用中...」）を先に弾くので、`DrawMaidScaleContent` に来る `target` は読込済みのメイドになる。spec の「ボディの読み込みを待っています」はこの既存の案内で代える。

- [ ] **Step 5: ビルドとテスト**

「ビルド＆テスト」を実行する（フィルタなし）。
期待: 2 構成のビルドが成功し、全件 PASS する。

- [ ] **Step 6: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MaidScaleRowDrawer.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MaidScaleItemInspector.cs \
  source/COM3D2.SceneEditor.Plugin/MaidWindowBase.cs \
  source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(bone): ボーンウィンドウに腕スケールのタブを追加する"
```

---

### Task 4: 実機検証と互換メモ

**Files:**
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の節のメイドスケールの行）

DLL を実機へ反映するには、ゲームを止めてから入れ替える必要がある。**com3d25-devbridge:restart-verify** スキルの手順に従う（ゲーム終了 → COM3D25 構成の DLL をコピー → 起動 → 最新セーブのロード）。検証は通常シーン（デイリー画面）で行い、撮影モードは使わない。シーンプリセットを保存するときは、既存のプリセットを上書きしない名前（`zz_maidscale_verify` など）を使い、検証後に削除する。

- [ ] **Step 1: 実機へ反映して起動する**

restart-verify スキルで DLL を反映し、`tail_log` で `TBody.LateUpdate のフックに成功しました` を確かめる。

- [ ] **Step 2: ボーンウィンドウの「腕スケール」タブ**

devbridge で、プラグインの UI を有効にし（`SceneEditorPlugin.isEnable = true`）、ボーンウィンドウを表示して「腕スケール」タブを選ぶ（`BoneEditWindow.instance` の `_tabType` をリフレクションで設定してよい）。`screenshot` でタブと 6 行・「すべて 1 に戻す」が出ていることを確かめる。

- タブの「左前腕」のスライダーを操作する代わりに、`MaidScaleRowDrawer` と同じ経路（`HistoryManager.instance.BeforeEdit(m, HistoryScope.MaidScale, ...)` → `SetScale`）で 1.5 にし、効くこと・`HistoryManager.Undo()` / `Redo()` で 1 ⇄ 1.5 になることを確かめる（タイムライン未読込の状態で行う）
- 「すべて 1 に戻す」に相当する操作（`BeforeEdit` 1 回 → 6 本 `SetScale(1)`）を 2 本拡縮した状態で行い、`Undo()` 1 回で両方戻ることを確かめる。全骨 1 のときはボタンが無効表示であることを screenshot で確かめる
- 対象を「モデル」に切り替えたとき（`SwitchTargetType` 相当）、タブが「編集」に戻り「腕スケール」が出ないことを screenshot で確かめる
- タイムラインを読み込み、メイドスケールレイヤーが無い状態で「腕スケール」タブを開くと、注意ラベルとレイヤー追加ボタンが出て行が無効になり、タブバーは押せることを screenshot で確かめる

- [ ] **Step 3: ボーン編集との共存（GUI の中で読む値）**

eval は Update の段階で走り、その時点では Update 冒頭の `RestoreApplied` で既に素の値に戻っている。そのため eval で直接読んでも Task 2 の効果は判別できない。GUI の中の値を、Harmony（リフレクション経由。`0Harmony` の型の二重定義を避けるため、`MaidScaleLateUpdatePatch._harmony` のフィールド型からアセンブリを取る）で `WindowManager.OnGUI` の prefix に記録用メソッドを当てて読む。

1. 左前腕を 1.5 にする
2. ボーンウィンドウの「編集」タブで body スロットの `Bip01 L Forearm` を選ぶ
3. `WindowManager.OnGUI` の prefix で、選択骨の `localScale` を数フレーム分記録する。同じフレームの `Camera.onPreCull` でも記録する（陰性対照: 倍率込みの値になるはず）
   - 期待: OnGUI の中は素の値（例: `(0.996, 1.030, 1.000)`）、onPreCull は 1.5 倍の値
4. ボーンウィンドウでスケールのリセット（`ResetSelectedBoneScale`）を行った後、倍率を 1 に戻し、onPreCull での前腕の値が素の値と一致する（倍率が焼き込まれていない）ことを確かめる
5. 記録用のパッチを外す

- [ ] **Step 3b: フレーム末の撮影に倍率が写る**

1. プラグインの UI を有効にしたまま、左前腕を 1.5 にする
2. eval から `SceneEditorPlugin` のインスタンスで `StartCoroutine` し、`WaitForEndOfFrame` の後に `Bip01 L Forearm`（body スロット）の `localScale` を記録する
   - 期待: 1.5 倍の値（OnGUI の後に掛け直されている）
3. 同じく `WaitForEndOfFrame` の後に `ScreenshotManager.Capture()` を呼んで保存された画像を開き、左前腕が拡大されて写っていることを確かめる（保存先はログか `ScreenshotManager` の実装で確かめ、検証後に削除する）

- [ ] **Step 4: シーンプリセット**

1. 左手 1.5・右上腕 1.2 の状態でシーンプリセットを `zz_maidscale_verify` として保存し、XML に `<maidScale>` と 2 つの `<bone>` があることを確かめる
2. 倍率をすべて 1 に戻してからプリセットを適用し、2 本の倍率が戻ることを確かめる
3. 倍率をすべて 1 にした状態で保存し直したプリセット（`<maidScale />`）を、左手 1.5 の状態で適用すると、1 に戻ることを確かめる
4. 既存の v37 のプリセット（`version="37"`、`maidScale` 無し）を左手 1.5 の状態で適用し、1.5 のまま変わらないことを確かめる
5. 検証用のプリセットファイル（XML とサイドカー）を削除し、倍率を 1 に戻す

- [ ] **Step 5: CLAUDE.md の互換メモを直す**

`W:\COM3D2_5\work\CLAUDE.md` のメイドスケールの行（`- メイドスケールレイヤー（...`）の末尾に追記する。

```markdown
。シーンプリセットは v38 で maid に `maidScale` 要素（1 以外の骨だけを `<bone name scale>` で持つ。全骨 1 は空要素）を追加。要素の無い旧プリセットは適用時に倍率へ触らない
```

このファイルは git 管理外なのでコミットは不要。

- [ ] **Step 6: 検証結果を記録する**

各ステップの成否を台帳とユーザーへの報告にまとめる。コード変更が生じた場合は、内容に合わせて `fix(...)` でコミットする。

## レビュー却下メモ

- 腕スケールタブではヘッダー行・スロット選択を隠す — タブバーの位置がタブで変わらないよう今の配置を保つ。どちらもタイムラインの値を書かず、ゲートの外に出しても害はない
- CLAUDE.md の追記先の行が実在するか — `- メイドスケールレイヤー（MaidScaleTimelineLayer` で始まる行が 1 行あることを確認済み
- ApplyMaidScale の単体テスト — Controller が Unity に依存してテストできない。Task 4 Step 4 の実機検証で確かめる（手順は省略しない）
