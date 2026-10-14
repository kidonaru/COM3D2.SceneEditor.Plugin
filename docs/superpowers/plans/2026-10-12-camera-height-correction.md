# カメラの身長補正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（CLAUDE.md の方針で subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** カメラレイヤーに、基準メイドのゲーム内身長（cm）で追従なしのカメラキーの注視点の高さを補正する設定を追加する。

**Architecture:**
- 身長の式と補正の計算は、Unity に依存しない静的クラス `CameraHeightCorrection` に切り出して単体テストする
- 設定 3 項目は `TimelineData` / `TimelineXml` に置き、OFF のときは `*Specified` で書き出さない
- `CameraTimelineLayer` は、再生時（`ApplyMotion` の追従なし分岐）に補正し、キーを打つとき（`UpdateFrame`）に逆補正する
- UI は、新しい `CameraHeightRowDrawer` を `CameraItemInspector` に差し込む

**Tech Stack:** C#（Unity 5.6 / Mono、COM3D2 と COM3D25 の 2 構成ビルド）、xUnit（`COM3D2.SceneEditor.Plugin.Tests`）、com3d25-devbridge（実機確認）

**Spec:** `docs/superpowers/specs/2026-10-12-camera-height-correction-design.md`

## Global Constraints

- **身長**: ゲームのプロフィールと同じ式 `(int)((1340 + sintyou·4 + DouPer·(1 + sintyou·0.005) + KubiScl·0.5 + HeadY·0.5) / 10)` を、メイドの今の props から計算する
  - 旧ボディ（`Status.UpdateBodyParam`）でも CRC ボディ（`BodyProfileManager.Height`）でも、ゲームは同じ式を使う
  - `status.body.height` は使わない（更新が遅れるため）。`Status.UpdateBodyParam` も呼ばない（セーブデータの `status` を書き換えるため）
- **倍率**: s = 基準メイドの今の身長 / 基準身長
  - s = 1 にする条件: 補正 OFF、基準身長が 0 以下、基準メイドがいない、身長が 0 以下
  - ボディのロード状態は条件にしない
- **補正の対象**: 追従なしのキーの注視点の Y だけを `y' = rootY + (y − rootY)·s` で補正する。rootY は基準メイドの `maid.transform.position.y`
  - 追従キー・XZ・距離・FOV・ロールは補正しない
  - サブカメラは対象外
- **XML**: 要素名は `IsCameraHeightCorrection` / `CameraHeightMaidSlotNo` / `CameraHeightBase`
  - OFF のときは 3 つとも書き出さない。version は上げない
  - 書き出しの抑止は `*Specified`（空の setter 付き）で行い、`ShouldSerialize*` は使わない。COM3D2 2.0 の Mono は `*Specified` しか見ないため
- **新規ファイル**: 本体の csproj はファイルを明示列挙しているため（例: `COM3D2.SceneEditor.Plugin.csproj:689`）、新規の .cs は必ず `<Compile Include>` に足す
- **コメント**: 日本語。原則「なぜ他の案を採らなかったか」だけを書く
- **テスト**: Unity のネイティブ呼び出し（`Quaternion.Euler` 等）はできない

## Review Focus

1. **基準メイドが居なくなった**（スロットのメイドを外した・別シーンで開いた）
   - 期待: 例外を出さず s = 1 で、補正なしで再生される。UI のコンボも範囲外にならない
   - 確かめる場所: Task 1（`CalcScale` のテスト）、Task 3（null ガード）、Task 4（コンボの index のクランプ）
2. **補正 ON で基準身長が 0**（XML の手編集など）
   - 期待: s = 1
   - 確かめる場所: Task 1 のテスト
3. **補正中にキーを打って再生し直す**
   - 期待: 見た目の注視点が、打つ前と同じ位置に戻る
   - 確かめる場所: Task 1 の往復テスト
4. **旧 XML・MTE データ**
   - 期待: 要素が無ければ OFF。OFF で保存すると要素を書き出さない
   - 確かめる場所: Task 2 のテスト
5. **追従キー・着替え中**
   - 期待: 追従キーのオフセットは変わらない。ボディを再ロードしている間もカメラが跳ねない
   - 確かめる場所: Task 5 の実機確認

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

### Task 1: 身長の式と補正の計算（純粋関数）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` の追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs`

**Interfaces:**
- Produces（namespace `COM3D2.MotionTimelineEditor.Plugin`）:
  - `public static int CameraHeightCorrection.CalcHeight(float sintyou, float douPer, float kubiScl, float headY)`
  - `public static float CameraHeightCorrection.CalcScale(bool enabled, int baseHeight, int currentHeight)`
  - `public static float CameraHeightCorrection.Correct(float y, float rootY, float scale)`
  - `public static float CameraHeightCorrection.Uncorrect(float y, float rootY, float scale)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightCorrectionTests
    {
        [Theory]
        [InlineData(0f, 0f, 0f, 0f, 134)]
        [InlineData(50f, 50f, 50f, 50f, 165)]   // 1652.5 / 10
        [InlineData(1f, 0f, 0f, 0f, 134)]       // 134.4 は切り捨て
        [InlineData(0f, 0f, 19f, 0f, 134)]      // 134.95 も切り捨て
        [InlineData(100f, 100f, 100f, 100f, 199)]
        public void 身長はプロフィールと同じ式で求める(
            float sintyou, float douPer, float kubiScl, float headY, int expected)
        {
            Assert.Equal(expected, CameraHeightCorrection.CalcHeight(sintyou, douPer, kubiScl, headY));
        }

        [Fact]
        public void 基準と同じ身長なら倍率は1()
        {
            Assert.Equal(1f, CameraHeightCorrection.CalcScale(true, 160, 160));
        }

        [Fact]
        public void 倍率は今の身長と基準身長の比()
        {
            Assert.Equal(0.9f, CameraHeightCorrection.CalcScale(true, 160, 144), 4);
        }

        [Theory]
        [InlineData(false, 160, 144)]
        [InlineData(true, 0, 144)]
        [InlineData(true, -1, 144)]
        [InlineData(true, 160, 0)]
        public void 補正できない条件では倍率1(bool enabled, int baseHeight, int currentHeight)
        {
            Assert.Equal(1f, CameraHeightCorrection.CalcScale(enabled, baseHeight, currentHeight));
        }

        [Fact]
        public void 補正はルートの高さを中心に拡縮する()
        {
            Assert.Equal(1.0f + (1.5f - 1.0f) * 0.8f, CameraHeightCorrection.Correct(1.5f, 1.0f, 0.8f), 5);
            Assert.Equal(1.0f, CameraHeightCorrection.Correct(1.0f, 1.0f, 0.8f), 5);
        }

        [Theory]
        [InlineData(1.4f, 0f, 0.9f)]
        [InlineData(-0.3f, 0.5f, 1.12f)]
        [InlineData(2.0f, -1f, 1f)]
        public void 補正と逆補正は往復で元に戻る(float y, float rootY, float scale)
        {
            Assert.Equal(y, CameraHeightCorrection.Uncorrect(
                CameraHeightCorrection.Correct(y, rootY, scale), rootY, scale), 4);
            Assert.Equal(y, CameraHeightCorrection.Correct(
                CameraHeightCorrection.Uncorrect(y, rootY, scale), rootY, scale), 4);
        }
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraHeightCorrectionTests`）。Expected: `CameraHeightCorrection` が無く、テストプロジェクトのコンパイルが失敗する。

- [ ] **Step 3: 実装して csproj に登録する**

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の計算。補正するのは追従なしのカメラキーの注視点の Y だけで、
    /// scale は常に正 (CalcScale が 0 以下を返さない) を前提とする
    /// </summary>
    public static class CameraHeightCorrection
    {
        /// <summary>プロフィールに出るゲーム内身長 (cm)</summary>
        public static int CalcHeight(float sintyou, float douPer, float kubiScl, float headY)
        {
            return (int)((1340f + sintyou * 4f + douPer * (1f + sintyou * 0.005f)
                + kubiScl * 0.5f + headY * 0.5f) / 10f);
        }

        public static float CalcScale(bool enabled, int baseHeight, int currentHeight)
        {
            if (!enabled || baseHeight <= 0 || currentHeight <= 0)
            {
                return 1f;
            }
            return (float)currentHeight / baseHeight;
        }

        public static float Correct(float y, float rootY, float scale)
        {
            return rootY + (y - rootY) * scale;
        }

        public static float Uncorrect(float y, float rootY, float scale)
        {
            return rootY + (y - rootY) / scale;
        }
    }
}
```

`CalcHeight` の式は `Status.UpdateBodyParam`（`Assembly-CSharp/MaidStatus/Status.cs:1361`）・`BodyProfileManager.Height`（`Assembly-CSharp/MaidEdit/BodyProfileManager.cs:25`）と同じにする。

csproj の `<Compile Include="Timeline\TransformData\TransformDataCamera.cs" />` と同じ ItemGroup に、`<Compile Include="Timeline\CameraHeightCorrection.cs" />` を追加する。

- [ ] **Step 4: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `CameraHeightCorrectionTests`）。Expected: 全件 PASS。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): 身長補正の身長の式と倍率・補正の計算を追加する` などのメッセージにする。

---

### Task 2: 設定 3 項目の保持と XML

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`（`isTangentFace` の宣言 :419 付近、`FromXml` :900 付近、`ToXml` :1068 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`（`isTangentFace` :366 付近）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightSettingTests.cs`

**Interfaces:**
- Produces（`TimelineData`、`TimelineXml` とも同名）:
  - `public bool isCameraHeightCorrection = false;`
  - `public int cameraHeightMaidSlotNo = 0;`
  - `public int cameraHeightBase = 0;`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightSettingTests
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
        public void 新規タイムラインは身長補正OFF()
        {
            var data = new TimelineData();
            Assert.False(data.isCameraHeightCorrection);
            Assert.Equal(0, data.cameraHeightMaidSlotNo);
            Assert.Equal(0, data.cameraHeightBase);
        }

        [Fact]
        public void OFFでは要素を書き出さない()
        {
            var data = new TimelineData { cameraHeightMaidSlotNo = 2, cameraHeightBase = 160 };
            var text = Serialize(data.ToXml());
            Assert.DoesNotContain("CameraHeight", text);
        }

        [Fact]
        public void 要素の無いXMLはOFFで読む()
        {
            var text = Serialize(new TimelineXml());
            var loaded = new TimelineData { isCameraHeightCorrection = true, cameraHeightBase = 150 };
            loaded.FromXml(Deserialize(text));
            Assert.False(loaded.isCameraHeightCorrection);
            Assert.Equal(0, loaded.cameraHeightBase);
        }

        [Fact]
        public void ONの設定はXML往復で保持される()
        {
            var data = new TimelineData
            {
                isCameraHeightCorrection = true,
                cameraHeightMaidSlotNo = 1,
                cameraHeightBase = 165,
            };
            var text = Serialize(data.ToXml());
            Assert.Contains("<IsCameraHeightCorrection>true</IsCameraHeightCorrection>", text);

            var loaded = new TimelineData();
            loaded.FromXml(Deserialize(text));
            Assert.True(loaded.isCameraHeightCorrection);
            Assert.Equal(1, loaded.cameraHeightMaidSlotNo);
            Assert.Equal(165, loaded.cameraHeightBase);
        }
    }
}
```

`FaceTangentSettingTests` では、`new TimelineXml()` をそのまま `FromXml` に渡して通っている。同じ前提で書いている。

- [ ] **Step 2: テストを走らせて失敗を確かめる**

Run: テスト実行コマンド（filter `CameraHeightSettingTests`）。Expected: フィールドが無く、コンパイルが失敗する。

- [ ] **Step 3: `TimelineXml` に要素を足す**

`isTangentFace` の宣言の直後に追加する:

```csharp
        // カメラの身長補正は SE 独自。OFF では 3 項目とも書き出さず、MTE 産の XML を保存し直しても内容を変えない
        [XmlElement("IsCameraHeightCorrection")]
        public bool isCameraHeightCorrection = false;

        [XmlElement("CameraHeightMaidSlotNo")]
        public int cameraHeightMaidSlotNo = 0;

        [XmlElement("CameraHeightBase")]
        public int cameraHeightBase = 0;

        [XmlIgnore] public bool isCameraHeightCorrectionSpecified { get { return isCameraHeightCorrection; } set { } }
        [XmlIgnore] public bool cameraHeightMaidSlotNoSpecified { get { return isCameraHeightCorrection; } set { } }
        [XmlIgnore] public bool cameraHeightBaseSpecified { get { return isCameraHeightCorrection; } set { } }
```

- [ ] **Step 4: `TimelineData` に項目と読み書きを足す**

宣言（`isTangentFace` の直後）:

```csharp
        /// <summary>カメラの身長補正。ON のとき、追従なしのカメラキーの注視点の高さを基準メイドの身長で補正する</summary>
        public bool isCameraHeightCorrection = false;
        /// <summary>身長補正の基準メイドのスロット番号</summary>
        public int cameraHeightMaidSlotNo = 0;
        /// <summary>作成時の基準メイドのゲーム内身長 (cm)。0 以下は未設定で補正しない</summary>
        public int cameraHeightBase = 0;
```

`FromXml`（`isTangentFace = xml.isTangentFace;` の直後）:

```csharp
            isCameraHeightCorrection = xml.isCameraHeightCorrection;
            cameraHeightMaidSlotNo = xml.cameraHeightMaidSlotNo;
            cameraHeightBase = xml.cameraHeightBase;
```

`ToXml`（`xml.isTangentFace = isTangentFace;` の直後）:

```csharp
            xml.isCameraHeightCorrection = isCameraHeightCorrection;
            xml.cameraHeightMaidSlotNo = cameraHeightMaidSlotNo;
            xml.cameraHeightBase = cameraHeightBase;
```

- [ ] **Step 5: テストを走らせて通ることを確かめる**

Run: テスト実行コマンド（filter `CameraHeightSettingTests`）。Expected: 全件 PASS。続けて filter なしで全テストを走らせ、既存テストが壊れていないことを確かめる。

- [ ] **Step 6: コミットする**

commit スキルで、`feat(timeline): カメラの身長補正の設定を XML に保存する` などのメッセージにする。

---

### Task 3: カメラレイヤーに補正を組み込む

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidHeightReader.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` の追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`（`ApplyMotion` :150-171、`UpdateFrame` :225-226）

**Interfaces:**
- Consumes: `CameraHeightCorrection.*`（Task 1）、`TimelineData` の 3 項目（Task 2）
- Produces:
  - `public static int MaidHeightReader.GetHeight(Maid maid)`（namespace `COM3D2.MotionTimelineEditor.Plugin`。maid が null なら 0）
  - `public static float CameraTimelineLayer.GetHeightScale(TimelineData timeline, out float rootY)`（Task 4 の UI が倍率の表示に使う）

- [ ] **Step 1: 現在の追従なし分岐を確かめる**

`CameraTimelineLayer.cs` を読み、次の 3 か所を確かめる:
- `ApplyMotion` の `uoCamera.SetTargetPos(position)` 2 か所（`follow == null` の分岐と `!follow.isFollow` の分岐）
- `UpdateFrame` の `trans.position = target.position;` 1 か所

`TimelineLayerBase` の `timeline` プロパティの型が `TimelineData` であることも確かめる。

- [ ] **Step 2: `MaidHeightReader` を作り、csproj に登録する**

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    public static class MaidHeightReader
    {
        /// <summary>プロフィールに出るゲーム内身長 (cm)。取得できなければ 0</summary>
        public static int GetHeight(Maid maid)
        {
            if (maid == null)
            {
                return 0;
            }
            // status.body.height はゲームが UpdateBodyParam を呼んだときしか更新されず SE の体型変更に遅れる。
            // UpdateBodyParam を呼ぶと体重・バスト等の status まで書き換えるため、props から同じ式で求める
            return CameraHeightCorrection.CalcHeight(
                maid.GetProp(MPN.sintyou).value,
                maid.GetProp(MPN.DouPer).value,
                maid.GetProp(MPN.KubiScl).value,
                maid.GetProp(MPN.HeadY).value);
        }
    }
}
```

`MPN.DouPer` / `KubiScl` / `HeadY` が COM3D2 2.0 にもあることは、Step 4 の 2 構成ビルドで確かめる（2.0 の Status.UpdateBodyParam が同じ MPN を使っている）。csproj に `<Compile Include="Timeline\MaidHeightReader.cs" />` を追加する。

- [ ] **Step 3: `CameraTimelineLayer` に倍率の取得と補正を足す**

クラス内（`GetUOCamera` の近く）に追加する:

```csharp
        /// <summary>
        /// 身長補正の倍率と、補正の中心になる基準メイドのルートの高さ。
        /// 補正しない条件では 1 を返し、rootY は 0
        /// </summary>
        public static float GetHeightScale(TimelineData timeline, out float rootY)
        {
            rootY = 0f;
            if (timeline == null || !timeline.isCameraHeightCorrection)
            {
                return 1f;
            }

            var maidCache = MaidManager.instance.GetMaidCache(timeline.cameraHeightMaidSlotNo);
            var maid = maidCache != null ? maidCache.maid : null;
            // isLoadedBody を条件にすると、着替えでボディを再ロードする間だけ s = 1 に落ちてカメラが跳ねる
            if (maid == null)
            {
                return 1f;
            }

            rootY = maid.transform.position.y;
            return CameraHeightCorrection.CalcScale(
                true, timeline.cameraHeightBase, MaidHeightReader.GetHeight(maid));
        }

        private Vector3 CorrectTargetPos(Vector3 position)
        {
            float rootY;
            var scale = GetHeightScale(timeline, out rootY);
            position.y = CameraHeightCorrection.Correct(position.y, rootY, scale);
            return position;
        }

        private Vector3 UncorrectTargetPos(Vector3 position)
        {
            float rootY;
            var scale = GetHeightScale(timeline, out rootY);
            position.y = CameraHeightCorrection.Uncorrect(position.y, rootY, scale);
            return position;
        }
```

`ApplyMotion` の 2 か所を差し替える:

```csharp
            if (follow == null)
            {
                uoCamera.SetTargetPos(CorrectTargetPos(position));
            }
```

```csharp
                else
                {
                    uoCamera.SetTargetPos(CorrectTargetPos(position));
                }
```

追従中の `follow.state.offset = position;` は変えない。追従先の部位が、もともと身長に合わせて動くため。

`UpdateFrame` の 1 か所を差し替える:

```csharp
            trans.position = UncorrectTargetPos(target.position);
```

追従中は、この後で `trans.position = follow.state.offset;` に上書きされる。そのため追従キーには逆補正がかからない。

- [ ] **Step 4: 両構成をビルドし、全テストを走らせる**

Run: テスト実行コマンド（filter なし）。Expected: COM3D2 と COM3D25 の両方のビルドが警告を増やさずに成功し、全テストが PASS する。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): 追従なしのカメラキーの注視点を基準メイドの身長で補正する` などのメッセージにする。本文には、`status.body.height` ではなく props から計算する理由を書く。

---

### Task 4: Inspector の UI

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` の追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs`（フィールドの追加と、`DrawItems` の追従行の直後）

**Interfaces:**
- Consumes:
  - `TimelineData` の 3 項目（Task 2）
  - `MaidHeightReader.GetHeight`・`CameraTimelineLayer.GetHeightScale`（Task 3）
  - `MTEP.TimelineManager.instance.timeline`、`MTEP.TimelineManager.instance.ApplyCurrentFrame(bool)`
  - `LabeledComboRow.Draw`、`GUIComboBox<T>`
  - `GUIView.DrawToggle` / `DrawDragIntField` / `DrawButton` / `DrawLabel`
- Produces: `public class CameraHeightRowDrawer { public void Draw(GUIView view, float labelWidth, float rowHeight); }`

- [ ] **Step 1: 既存の UI 部品のシグネチャを確かめる**

次を実物で確かめ、下のコードの呼び出しを合わせる:
- `GUIView.DragIntFieldOption`（`label` / `labelWidth` / `value` / `minValue` / `maxValue` / `fieldWidth` / `height` / `onChanged`。`MTEUtils/GUIView.cs:2039`、使用例は `TextRowDrawer.cs:212`）
- `DrawButton(string, float, float, ...)`
- `DrawLabel` の引数
- `LabeledComboRow.Draw(view, label, combo, labelWidth, rowHeight)`
- `BeginHorizontal` と対になる終了メソッド（`TimelineSettingWindow.cs:250` 付近の使い方）

- [ ] **Step 2: `CameraHeightRowDrawer` を作り、csproj に登録する**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の設定行 (ON/OFF・基準メイド・基準身長)。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class CameraHeightRowDrawer
    {
        private const float ButtonWidth = 60f;
        private const float FieldWidth = 50f;
        // プロフィールの身長の式は props 0〜100 で 134〜199cm。手入力の途中値で倍率が極端にならないよう余裕を持って絞る
        private const int MinBaseHeight = 100;
        private const int MaxBaseHeight = 250;

        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, index) => maidCache != null ? maidCache.fullName : "なし",
        };

        public void Draw(GUIView view, float labelWidth, float rowHeight)
        {
            var timeline = timelineManager.timeline;
            if (timeline == null)
            {
                return;
            }

            view.DrawToggle("身長補正", timeline.isCameraHeightCorrection, -1, rowHeight, newValue =>
            {
                timeline.isCameraHeightCorrection = newValue;
                // 保存のたびに取り込むと、別のメイドで開いて保存しただけで作者の基準が失われる
                if (newValue)
                {
                    CaptureBaseHeight(timeline);
                }
                timelineManager.ApplyCurrentFrame(true);
            });

            if (!timeline.isCameraHeightCorrection)
            {
                return;
            }

            var maidCaches = maidManager.maidCaches;
            if (maidCaches.Count == 0)
            {
                view.DrawLabel("メイドがいません", -1, rowHeight, textColor: Color.yellow);
                return;
            }

            _maidComboBox.items = maidCaches;
            // 読込後にメイド数が減っても範囲外を渡さない (MaidFollowRowDrawer.ToFollowMaidIndex と同じ)
            _maidComboBox.currentIndex = Mathf.Clamp(timeline.cameraHeightMaidSlotNo, 0, maidCaches.Count - 1);
            _maidComboBox.onSelected = (maidCache, index) =>
            {
                timeline.cameraHeightMaidSlotNo = index;
                timelineManager.ApplyCurrentFrame(true);
            };
            LabeledComboRow.Draw(view, "基準メイド", _maidComboBox, labelWidth, rowHeight);

            view.BeginHorizontal();
            {
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = "基準身長",
                    labelWidth = labelWidth,
                    value = timeline.cameraHeightBase,
                    minValue = MinBaseHeight,
                    maxValue = MaxBaseHeight,
                    fieldWidth = FieldWidth,
                    height = rowHeight,
                    onChanged = x =>
                    {
                        timeline.cameraHeightBase = x;
                        timelineManager.ApplyCurrentFrame(true);
                    },
                });

                if (view.DrawButton("取得", ButtonWidth, rowHeight))
                {
                    CaptureBaseHeight(timeline);
                    timelineManager.ApplyCurrentFrame(true);
                }
            }
            view.EndLayout();

            float rootY;
            var scale = MTEP.CameraTimelineLayer.GetHeightScale(timeline, out rootY);
            view.DrawLabel(string.Format("現在 {0}cm / 補正 x{1:F2}",
                GetCurrentHeight(timeline), scale), -1, rowHeight);
        }

        private static int GetCurrentHeight(MTEP.TimelineData timeline)
        {
            var maidCache = maidManager.GetMaidCache(timeline.cameraHeightMaidSlotNo);
            return maidCache != null ? MTEP.MaidHeightReader.GetHeight(maidCache.maid) : 0;
        }

        private static void CaptureBaseHeight(MTEP.TimelineData timeline)
        {
            var height = GetCurrentHeight(timeline);
            if (height > 0)
            {
                timeline.cameraHeightBase = height;
            }
        }
    }
}
```

`view.EndLayout()` は、Step 1 で確かめた実際の終了メソッド名に合わせる。csproj には `<Compile Include="CameraHeightRowDrawer.cs" />` を追加する（`MaidFollowRowDrawer.cs` の行の近く）。

- [ ] **Step 3: `CameraItemInspector` に差し込む**

フィールドを追加する:

```csharp
        private readonly CameraHeightRowDrawer _heightRowDrawer = new CameraHeightRowDrawer();
```

`DrawItems` の追従行ブロック（`_followRowDrawer.Draw(...)` と `view.DrawHorizontalLine();`）の直後に追加する:

```csharp
            _heightRowDrawer.Draw(view, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
```

- [ ] **Step 4: 両構成をビルドし、全テストを走らせる**

Run: テスト実行コマンド（filter なし）。Expected: 両構成のビルドが成功し、全テストが PASS する。

- [ ] **Step 5: コミットする**

commit スキルで、`feat(camera): カメラの Inspector に身長補正の設定行を追加する` などのメッセージにする。

---

### Task 5: 実機確認とドキュメント

**Files:**
- Modify: `docs-site/timeline/layers-camera.md`（「カメラ」節の追従の説明の後）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の末尾。git 管理外なのでコミットしない）

- [ ] **Step 1: 実機へ反映する**

`com3d25-devbridge:restart-verify` スキルの手順で、COM3D25 構成の DLL（`bin/Debug/COM3D25/`）を反映し、ゲームを再起動する。

- [ ] **Step 2: 補正の効きを確かめる**

通常シーン（デイリー画面でエディタを有効にする。撮影モードは不可）で、身長の違うメイドを 2 人以上用意する。身長はスライダーで変えてよいが、終わったら元に戻す。

1. **身長の一致**: Inspector の `現在 cm` が、エディット画面のプロフィールの身長と一致する。CRC ボディのメイドがいれば、そのメイドでも確かめる
2. **補正の効き**: メイド A を基準メイドにして補正を ON にし、追従なしのカメラキーで顔のアップを打つ。基準メイドをメイド B に切り替え、`screenshot` で注視点の高さがメイド B の顔に合っていることを確かめる
3. **逆補正**: メイド B のままキーを打ち直し、基準メイドをメイド A に戻しても構図が崩れない
4. **追従キー**（Review Focus 5）: 追従点を頭にしたキーを打つ。基準メイドを切り替えても、オフセットが変わらない
5. **着替え**（Review Focus 5）: 補正 ON で再生しながら基準メイドを着替えさせ、カメラが跳ねない
6. **メイドがいない**（Review Focus 1）: 基準メイドのスロットのメイドを外す。例外が出ず（`tail_log` で確かめる）、補正なしで再生され、Inspector のコンボも壊れない
7. **XML**: 補正 ON で保存した XML には 3 要素が入り、OFF で保存すると入らない

問題があれば修正し、テスト実行コマンド（filter なし）を走らせ直してからコミットする。

- [ ] **Step 3: ユーザー向けドキュメントを更新する**

`docs-site/timeline/layers-camera.md` の「カメラ」節で、追従の説明（「`向き反映` を ON にすると…」の段落）の直後に追加する:

```markdown
#### 身長補正

作成時と身長の違うメイドでタイムラインを再生したときに、注視点の高さを合わせます。設定は、`カメラ` 項目を選んだときの Inspector にだけあります（Camera ウィンドウにはありません）。

| 設定 | 内容 |
|---|---|
| `身長補正` | ON にすると、そのときの基準メイドの身長を `基準身長` に取り込みます |
| `基準メイド` | 身長を参照するメイド |
| `基準身長` | 作成時の基準メイドの身長（cm、プロフィールの身長）。`取得` で今の基準メイドの身長を取り込み直します |

- 補正の倍率は「基準メイドの今の身長 ÷ 基準身長」です
- 追従なしのキーの注視点の高さだけを、基準メイドの足元を中心に倍率で伸縮します。距離・FoV・角度は変わりません
- 追従中のキーは、追従点がもともと身長に合わせて動くため補正しません
- 補正 ON のままキーを打つと、基準身長での値に戻して記録します。別のメイドで編集しても、作成時のメイドで再生した構図は崩れません
- 保存のたびに `基準身長` を取り込み直すことはありません
- 基準メイドが上下に移動すると、カメラも移動量の (1 − 倍率) 倍だけ上下します
- MTE で開くと補正は無効になります
```

- [ ] **Step 4: CLAUDE.md の互換性の節に追記する**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」の箇条書きの末尾に追加する:

```markdown
- カメラの身長補正（ルートの `IsCameraHeightCorrection` / `CameraHeightMaidSlotNo` / `CameraHeightBase`。基準身長はゲーム内身長の cm）は SE 独自。OFF では書き出さない。version は上げていない。要素の無い XML は OFF で読む。補正は再生時に追従なしのカメラキーの注視点の Y だけにかけ、キーには基準身長の空間の値を記録する（キーを打つとき逆補正）。MTE・旧 SE は要素を読み飛ばし、補正なしで表示する
```

- [ ] **Step 5: コミットする**

commit スキルで、docs-site の変更を `docs(timeline): カメラの身長補正の説明を追加する` などのメッセージでコミットする。CLAUDE.md は git 管理外なのでコミットしない。

---

## 実装後

CLAUDE.md の標準フローに従い、code-review スキルでブランチ全体をレビューしてからユーザーに提示する。

## レビュー却下メモ

- CHANGELOG.md への記載を計画に足す — 変更履歴は release-prep スキルがリリース時に git log からまとめて書く運用のため
