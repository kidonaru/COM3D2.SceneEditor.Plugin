# カメラ・シーンプリセットの身長補正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development を使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** カメラプリセット・シーンプリセットに身長補正（基準メイド・基準身長）を保存・適用し、カメラウィンドウの追従・身長補正の変更が編集モードへ入らず履歴にも載らない不具合を直す。

**Architecture:** プリセットと履歴が共有する `ScenePresetCamera` / `CameraSnapshot` に `heightSlotNo` / `heightBase` を足す。追従なしの注視点は基準身長の空間の値で持ち、保存時に基準メイド 1 人分の倍率で逆補正、適用時に `CameraHeightCorrection.Correct` で補正する。シーンプリセットはメイドの読込前にカメラを置くため、補正だけを `FinishApply` へ遅らせる。

**Tech Stack:** C# 7.3 / .NET Framework（COM3D2 は 3.5、COM3D25 は 4.x）、Unity、xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`）

**Spec:** `docs/superpowers/specs/2026-10-14-camera-preset-height-correction-design.md`

## Global Constraints

- シーンプリセットの版は **43**（`ScenePresetData.CurrentVersion`）
- カメラプリセットの文字列は **21 値**。8 / 11 / 19 値の旧形式も読む
- `heightSlotNo` の既定は **-1**（補正なし）、`heightBase` の既定は **0**（未設定）。どちらも既定値では XML に書き出さない
- 補正に使う基準身長はプリセットの `heightBase`。タイムラインの基準身長は `SetCameraHeightBaseIfUnset` でだけ書き、設定済みなら上書きしない
- 追従中（`maidSlotNo >= 0`）は注視点に補正をかけない（設定の復元は行う）
- 基準身長は履歴の対象外（`Approximately` で `heightBase` を比較しない）
- コードのコメント・ログは日本語。コメントは「却下した案にすると起きる問題」だけを書く
- 必ず COM3D2 と COM3D25 の両構成でビルドする

## Review Focus

- 身長補正を持たない旧プリセット（8 / 11 / 19 値、v42 以前の XML、SceneCapture）を読むと、注視点はそのまま置かれ、基準メイドは「なし」になる → Task 2 のテストで値数ごとに `heightSlotNo == -1` を固定
- 基準身長が未設定（0）のまま保存したプリセットを読んでも、注視点が動かない → Task 3 で `CalcScale` 経由の倍率 1 をテスト
- 保存時の逆補正と適用時の補正の往復で、同じメイドなら Y が元に戻る（ルートの高さが 0 以外でも） → Task 3 の往復テスト
- シーンプリセットでメイドを新しく呼び出した場合も、補正は呼出完了後のメイドの身長とルートの高さで 1 回だけかかる（二重にかからない） → Task 4 の実機確認
- 追従中のプリセットに身長補正の設定が入っていても、オフセットに補正がかからない → Task 3 の実装の分岐と Task 6 の実機確認

---

## File Structure

| ファイル | 変更 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs` | `Uncorrect` を追加（`Correct` の逆） |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetCamera` に `heightSlotNo` / `heightBase`、版を 43 へ |
| `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs` | プリセット文字列を 21 値に、追従行に `onBeforeChange` |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs` | 身長補正の記録・適用・比較 |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 補正を `FinishApply` へ遅らせる |
| `source/COM3D2.SceneEditor.Plugin/MainCameraRowDrawer.cs` | 追従中のオフセット・ヨーオフセットで履歴を記録 |
| `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs` | 基準メイドで履歴を記録、基準身長で編集モードへ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs` | 追従行に `onBeforeChange` |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs` | `Uncorrect` のテスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraPresetStringTests.cs` | 21 値・旧形式のテスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetCameraHeightTests.cs` | 新規。XML の往復と旧形式 |
| `docs-site/timeline/layers-camera.md` / `docs-site/guide/staging.md` | プリセットが身長補正を持つことを追記 |
| `W:\COM3D2_5\work\CLAUDE.md` | 互換の節に v43 を追記（git 管理外） |

## ビルドとテストのコマンド

Git Bash から。COM3D2 → COM3D25 の順でビルドしてからテストする（COM3D2 のビルドが COM3D25 の DLL を消すため）。**debug.bat は実機へ DLL をコピーするので使わない。**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

以降「ビルドとテスト」はこのコマンドを指す。特定のテストだけ回すときは末尾に `--filter "FullyQualifiedName~<クラス名>"` を付ける（ビルドは省略不可。テストは COM3D25 の DLL を参照する）。

---

### Task 1: カメラウィンドウの追従・身長補正で編集モードへ入り履歴へ載せる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs:523`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs:53`
- Modify: `source/COM3D2.SceneEditor.Plugin/MainCameraRowDrawer.cs:26-40,75-80`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs:45-88`

**Interfaces:**
- Consumes: `MainCameraRowDrawer.RecordCameraEdit(string label)`（`HistoryManager.BeforeEdit` 経由で `AutoEditMode.Enter()` を呼ぶ）、`AutoEditMode.Enter()`
- Produces: なし

GUI のコールバックは Unity 抜きで単体テストできないため、この Task はビルドと Task 6 の実機確認で確かめる。基準メイドが履歴で戻るのは Task 3 で `CameraSnapshot` が基準メイドを持ってから。

- [ ] **Step 1: メインカメラタブの追従行に `onBeforeChange` を渡す**

`CameraWindow.cs` の `DrawMainCameraContent` 内:

```csharp
            var follow = MTEP.MaidFollowMainCamera.instance;
            if (follow != null)
            {
                _mainFollowRowDrawer.Draw(_view, follow.state, LABEL_WIDTH, ROW_HEIGHT,
                    () => MainCameraRowDrawer.RecordCameraEdit("追従"));
                _view.DrawHorizontalLine();
            }
```

- [ ] **Step 2: Inspector の追従行にも同じく渡す**

`CameraItemInspector.cs:53`:

```csharp
                _followRowDrawer.Draw(view, follow.state, LabelWidth, RowHeight,
                    () => MainCameraRowDrawer.RecordCameraEdit("追従"));
```

- [ ] **Step 3: 追従中のオフセット・ヨーオフセットで履歴を記録する**

`MainCameraRowDrawer.DrawTargetPosRow` の summary から「(追従設定は履歴対象外のため記録しない)」を消し、追従分岐を次へ置き換える:

```csharp
        /// <summary>
        /// 注視点のワールド座標。Inspector の座標行と同じ表示形式で編集する。
        /// 追従中は追従点からのオフセットを編集する
        /// </summary>
        public static void DrawTargetPosRow(
            GUIView view, CameraMain mainCamera, MTEP.MaidFollowMainCamera follow,
            float labelWidth, float rowHeight)
        {
            if (follow != null && follow.isFollow)
            {
                Vector3RowDrawer.Draw(view, "オフセット", PositionDragSensitivity, labelWidth, rowHeight,
                    follow.state.offset,
                    value =>
                    {
                        RecordCameraEdit("オフセット");
                        follow.state.offset = value;
                    },
                    () =>
                    {
                        RecordCameraEdit("オフセット");
                        follow.state.offset = Vector3.zero;
                    });
                return;
            }
```

`DrawAngleSliders` の向き反映分岐:

```csharp
            if (follow != null && follow.isFollow && follow.state.followRotation)
            {
                DrawAxisSlider(view, "横回転", AngleUtils.NormalizeAngle(follow.state.yawOffset),
                    -180f, 180f, 0.1f, 0f, labelWidth, rowHeight, value =>
                    {
                        RecordCameraEdit("横回転");
                        follow.state.yawOffset = value;
                    });
            }
```

- [ ] **Step 4: 身長補正の行で編集モードへ入る**

`CameraHeightRowDrawer.Draw` の基準メイドの `onSelected` の先頭で履歴を記録する。基準身長の入力と「取得」は履歴の対象外なので `AutoEditMode.Enter()` だけを呼ぶ:

```csharp
            _maidComboBox.onSelected = (maidCache, index) =>
            {
                MainCameraRowDrawer.RecordCameraEdit("身長補正");
                var selectedSlotNo = MaidFollowRowDrawer.ToFollowMaidSlotNo(index);
                heightState.SetLive(selectedSlotNo);
                if (selectedSlotNo >= 0)
                {
                    timeline.SetCameraHeightBaseIfUnset(
                        selectedSlotNo, MTEP.CameraTimelineLayer.GetSlotHeight(selectedSlotNo));
                }
            };
```

```csharp
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = "基準身長",
                    labelWidth = labelWidth,
                    value = timeline.GetCameraHeightBase(slotNo),
                    minValue = MinBaseHeight,
                    maxValue = MaxBaseHeight,
                    fieldWidth = FieldWidth,
                    height = rowHeight,
                    onChanged = x =>
                    {
                        // RecordCameraEdit にすると、基準身長を Undo の対象外にした方針 (全再構築で古い値へ戻る) に反する
                        AutoEditMode.Enter();
                        timeline.SetCameraHeightBase(slotNo, x);
                    },
                });

                if (view.DrawButton("取得", ButtonWidth, rowHeight))
                {
                    AutoEditMode.Enter();
                    timeline.SetCameraHeightBase(slotNo, MTEP.CameraTimelineLayer.GetSlotHeight(slotNo));
                }
```

summary の「基準メイドはライブ値で、カメラキーを打つと身長補正のキーにも書かれる。」の後に何も足さない（履歴の扱いはコードから読める）。

- [ ] **Step 5: ビルドとテスト**

Run: ビルドとテスト（上記コマンド）
Expected: 両構成のビルド成功、既存テストすべて PASS

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/CameraWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/CameraItemInspector.cs source/COM3D2.SceneEditor.Plugin/MainCameraRowDrawer.cs source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs
git commit -m "fix(camera): 追従と身長補正の変更で編集モードへ入り履歴へ記録する"
```

本文には「`CameraSnapshot` が追従を含むようになった後も記録していなかったため、変更が再生値で巻き戻り Undo もできなかった」を書く。

---

### Task 2: プリセットの保存形式に身長補正を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:13-80,1366-1378`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs:39-41,388-480`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/CameraPresetStringTests.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetCameraHeightTests.cs`

**Interfaces:**
- Produces:
  - `ScenePresetCamera.heightSlotNo`（`int`、既定 -1）、`ScenePresetCamera.heightBase`（`int`、既定 0）、`ScenePresetCamera.hasHeight`（`bool`、`heightSlotNo >= 0`、`[XmlIgnore]`）
  - `CameraWindow.SerializeCameraPreset` が 21 値を書く。`ParseCameraPreset` が 8 / 11 / 19 / 21 値を読む

- [ ] **Step 1: XML のテストを書く**

`ScenePresetCameraHeightTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのカメラの身長補正を固定する</summary>
    public class ScenePresetCameraHeightTests
    {
        private static string Serialize(ScenePresetCamera camera)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetCamera));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, camera);
                return writer.ToString();
            }
        }

        private static ScenePresetCamera Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetCamera));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetCamera)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 補正なしは属性を書き出さない()
        {
            var text = Serialize(new ScenePresetCamera());

            Assert.DoesNotContain("heightSlotNo=", text);
            Assert.DoesNotContain("heightBase=", text);
        }

        [Fact]
        public void 基準メイドと基準身長を往復する()
        {
            var restored = Deserialize(Serialize(new ScenePresetCamera { heightSlotNo = 2, heightBase = 158 }));

            Assert.Equal(2, restored.heightSlotNo);
            Assert.Equal(158, restored.heightBase);
            Assert.True(restored.hasHeight);
        }

        [Fact]
        public void 基準身長が未設定でも基準メイドは往復する()
        {
            var text = Serialize(new ScenePresetCamera { heightSlotNo = 0 });

            Assert.Contains("heightSlotNo=\"0\"", text);
            Assert.DoesNotContain("heightBase=", text);
            Assert.Equal(0, Deserialize(text).heightSlotNo);
        }

        [Fact]
        public void 属性の無い旧プリセットは補正なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetCamera maidSlotNo=\"-1\" maidPointType=\"0\" followRotation=\"false\"" +
                " shakeFrequencyScale=\"0\" shakeSeed=\"0\"><distance>2</distance></ScenePresetCamera>";

            var restored = Deserialize(text);

            Assert.Equal(-1, restored.heightSlotNo);
            Assert.Equal(0, restored.heightBase);
            Assert.False(restored.hasHeight);
        }

        [Fact]
        public void シーンプリセットの版は身長補正の追加後()
        {
            Assert.True(ScenePresetData.CurrentVersion >= 43);
        }
    }
}
```

- [ ] **Step 2: カメラプリセット文字列のテストを足す**

`CameraPresetStringTests.cs` の `CreateState` に `heightSlotNo = 1, heightBase = 160,` を足し、次のテストを追加する:

```csharp
        [Fact]
        public void 身長補正も往復できる()
        {
            var restored = CameraWindow.ParseCameraPreset(
                CameraWindow.SerializeCameraPreset(CreateState()));

            Assert.Equal(1, restored.heightSlotNo);
            Assert.Equal(160, restored.heightBase);
        }

        [Fact]
        public void 身長補正を持たない19値の旧形式は補正なしとして読める()
        {
            var restored = CameraWindow.ParseCameraPreset(
                "0.5000,1.2500,-2.0000,3.5000,30.00,-15.00,5.00,42.00,1,2,1," +
                "0.0500,0.0300,0.0000,1.000,0.500,0.250,2.500,1234");

            Assert.NotNull(restored);
            Assert.Equal(new Vector3(0.05f, 0.03f, 0f), restored.shakePositionAmplitude);
            Assert.Equal(1234, restored.shakeSeed);
            Assert.False(restored.hasHeight);
        }

        [Fact]
        public void 負の基準身長は未設定として読む()
        {
            var restored = CameraWindow.ParseCameraPreset(
                "0,0,0,3.5,0,0,0,42,-1,0,0,0,0,0,0,0,0,1,0,0,-5");

            Assert.NotNull(restored);
            Assert.Equal(0, restored.heightSlotNo);
            Assert.Equal(0, restored.heightBase);
        }
```

既存の `手ブレを持たない11値…` と `追従を持たない8値…` にも `Assert.False(restored.hasHeight);` を足す。既存の `範囲外の手ブレ値は丸められる` は 19 値のまま残す（旧形式の手ブレも丸めることを固定する）。

- [ ] **Step 3: テストが失敗することを確かめる**

Run: ビルドとテスト
Expected: `heightSlotNo` / `heightBase` / `hasHeight` が無いため、テストプロジェクトのコンパイルエラー

- [ ] **Step 4: `ScenePresetCamera` にフィールドを足す**

`ScenePresetData.cs` の `ScenePresetCamera` で、`followRotation` の後に足す:

```csharp
        /// <summary>
        /// 身長補正の基準メイドのスロット番号。-1 で補正なし (v43)。
        /// 追従していないときの targetPos は、このスロットの基準身長の空間の値 (補正前) になる
        /// </summary>
        [XmlAttribute]
        public int heightSlotNo = -1;

        [XmlIgnore] public bool heightSlotNoSpecified { get { return hasHeight; } set { } }

        /// <summary>基準身長 (cm)。0 以下は未設定で補正しない (v43)</summary>
        [XmlAttribute]
        public int heightBase;

        [XmlIgnore] public bool heightBaseSpecified { get { return heightBase > 0; } set { } }

        [XmlIgnore]
        public bool hasHeight => heightSlotNo >= 0;
```

版の履歴コメントに足し、版を上げる:

```csharp
        // v42: 追加ライトの照射対象 (target) に 3=顔のみ / 4=体のみ を追加。旧 SE は「全て」として読む
        // v43: camera に身長補正 (heightSlotNo / heightBase) を追加。補正なしでは書き出さない。
        //      追従なしの targetPos は基準身長の空間の値になる。旧形式は属性が無く補正なしで読める
        public static readonly int CurrentVersion = 43;
```

- [ ] **Step 5: カメラプリセット文字列を 21 値にする**

`CameraWindow.cs` の定数:

```csharp
        private const int LegacyPresetValueCount = 8;
        private const int FollowPresetValueCount = 11;
        private const int ShakePresetValueCount = 19;
        private const int PresetValueCount = 21;
```

`SerializeCameraPreset`（summary の書式説明に `,heightSlotNo,heightBase` を足す）:

```csharp
        /// <summary>
        /// カメラ状態を
        /// "tx,ty,tz,dist,yaw,pitch,roll,fov,maidSlotNo,maidPointType,followRotation,
        ///  shakePosX,shakePosY,shakePosZ,shakeRotX,shakeRotY,shakeRotZ,shakeFreq,shakeSeed,
        ///  heightSlotNo,heightBase"
        /// 形式へ変換する (追従中は tx〜tz がオフセット、向き反映中は yaw がヨーオフセット、
        /// 追従なしで heightSlotNo があれば ty は基準身長の空間の値)。
        /// CameraPresetStringTests から往復を直接検証するため public にしてある
        /// </summary>
        public static string SerializeCameraPreset(ScenePresetCamera state)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F4},{1:F4},{2:F4},{3:F4},{4:F2},{5:F2},{6:F2},{7:F2},{8},{9},{10}," +
                "{11:F4},{12:F4},{13:F4},{14:F3},{15:F3},{16:F3},{17:F3},{18},{19},{20}",
                state.targetPos.x, state.targetPos.y, state.targetPos.z,
                state.distance,
                state.yaw, state.pitch,
                state.roll,
                state.fov,
                state.maidSlotNo, state.maidPointType, state.followRotation ? 1 : 0,
                state.shakePositionAmplitude.x,
                state.shakePositionAmplitude.y,
                state.shakePositionAmplitude.z,
                state.shakeRotationAmplitude.x,
                state.shakeRotationAmplitude.y,
                state.shakeRotationAmplitude.z,
                state.shakeFrequencyScale,
                state.shakeSeed,
                state.heightSlotNo, state.heightBase);
        }
```

`ParseCameraPreset`（summary に「身長補正を持たない旧形式 (19 値) は補正なし」を足す）の値数判定と手ブレ・身長補正の読込:

```csharp
            var parts = value.Split(',');
            if (parts.Length != LegacyPresetValueCount &&
                parts.Length != FollowPresetValueCount &&
                parts.Length != ShakePresetValueCount &&
                parts.Length != PresetValueCount)
            {
                return null;
            }
```

手ブレの読込条件は `if (parts.Length == PresetValueCount)` のままだと 21 値で手ブレが読まれなくなる。条件式を書き換える（ブロックの中身は既存のまま）:

```csharp
            if (parts.Length >= ShakePresetValueCount)
            {
                // (既存の手ブレ 4 項目の読込はそのまま)
            }

            // スロットの範囲外は MaidManager.GetMaidCache が null を返すため補正なしになる
            if (parts.Length == PresetValueCount)
            {
                state.heightSlotNo = (int)values[19];
                state.heightBase = Mathf.Max(0, (int)values[20]);
            }
            return state;
```

`負の基準身長は未設定として読む` のテストで `heightSlotNo` に 0 を入れているのは、`values[19] = 0` のため。

- [ ] **Step 6: ビルドとテスト**

Run: ビルドとテスト
Expected: すべて PASS

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs source/COM3D2.SceneEditor.Plugin.Tests/CameraPresetStringTests.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetCameraHeightTests.cs
git commit -m "feat(camera): プリセットの保存形式に身長補正の基準メイドと基準身長を足す"
```

---

### Task 3: `CameraSnapshot` で身長補正を記録・適用する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs:237-260`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs`

**Interfaces:**
- Consumes: Task 2 の `ScenePresetCamera.heightSlotNo` / `heightBase` / `hasHeight`
- Produces:
  - `CameraHeightCorrection.Uncorrect(float y, float rootY, float scale)`（`Correct` の逆）
  - `CameraSnapshot.ApplyState(ScenePresetCamera state, bool applyHeight = true)`
  - `CameraSnapshot.ApplyHeight(ScenePresetCamera state)`（身長補正の設定を戻し、追従なしで基準メイドがあるときは `state.targetPos` を補正してメインカメラの注視点へ置く）
  - `CameraTimelineLayer.GetSlotScale(int slotNo, int baseHeight, out float rootY)`（基準身長を渡す overload）

- [ ] **Step 1: `Uncorrect` のテストを書く**

`CameraHeightCorrectionTests.cs` に追加:

```csharp
        [Theory]
        [InlineData(1.5f, 0f, 0.9f)]
        [InlineData(1.5f, 0.3f, 1.1f)]   // ルートが床から浮いていても戻る
        [InlineData(0.2f, 0.5f, 0.8f)]   // 注視点がルートより下
        public void 逆補正して補正すると元に戻る(float y, float rootY, float scale)
        {
            var uncorrected = CameraHeightCorrection.Uncorrect(y, rootY, scale);

            Assert.Equal(y, CameraHeightCorrection.Correct(uncorrected, rootY, scale), 4);
        }

        [Fact]
        public void 基準身長が未設定なら逆補正しても値は変わらない()
        {
            var scale = CameraHeightCorrection.CalcScale(0, 150);

            Assert.Equal(1.5f, CameraHeightCorrection.Uncorrect(1.5f, 0.2f, scale), 4);
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルドとテスト（`--filter "FullyQualifiedName~CameraHeightCorrectionTests"`）
Expected: `Uncorrect` が無いためコンパイルエラー

- [ ] **Step 3: `Uncorrect` を実装する**

`CameraHeightCorrection.cs` の `Correct` の直後:

```csharp
        /// <summary>Correct の逆</summary>
        public static float Uncorrect(float y, float rootY, float scale)
        {
            return rootY + (y - rootY) / scale;
        }
```

- [ ] **Step 4: テストが通ることを確かめる**

Run: ビルドとテスト（`--filter "FullyQualifiedName~CameraHeightCorrectionTests"`）
Expected: PASS

- [ ] **Step 5: 記録（`CaptureState`）に身長補正を足す**

`CameraSnapshot.cs` の summary を「メインカメラの構図スナップショット (メイド追従設定・身長補正を含む)。」にする。`CaptureState` の追従ブロックの後、`return state;` の前に足す:

```csharp
            var timeline = MTEP.TimelineManager.instance.timeline;
            if (timeline != null)
            {
                var slotNo = MTEP.CameraHeightState.instance.liveSlotNo;
                state.heightSlotNo = slotNo;
                state.heightBase = slotNo >= 0 ? timeline.GetCameraHeightBase(slotNo) : 0;

                // 再生中の補間 (CameraHeightState の slotA/B/weight) で逆補正すると、
                // 補間を持たないプリセットを同じメイドで読んだときに画面の位置へ戻らない
                if (!state.hasFollow && state.hasHeight)
                {
                    float rootY;
                    var scale = GetPresetScale(state, out rootY);
                    var pos = state.targetPos;
                    pos.y = MTEP.CameraHeightCorrection.Uncorrect(pos.y, rootY, scale);
                    state.targetPos = pos;
                }
            }
            return state;
```

倍率の取得は `CameraTimelineLayer.GetSlotScale` と基準身長の出所だけが違うため、そちらに基準身長を受け取る overload を作って共有する。`Timeline/TimelineLayer/CameraTimelineLayer.cs` の `GetSlotScale` を次の 2 つにする:

```csharp
        /// <summary>
        /// スロット 1 人ぶんの身長補正の倍率と、補正の中心になるルートの高さ。
        /// 補正しない条件では 1 を返し、rootY は 0
        /// </summary>
        public static float GetSlotScale(TimelineData timeline, int slotNo, out float rootY)
        {
            if (timeline == null)
            {
                rootY = 0f;
                return 1f;
            }
            return GetSlotScale(slotNo, timeline.GetCameraHeightBase(slotNo), out rootY);
        }

        /// <summary>基準身長を呼び出し側が渡す版。プリセットは保存した基準身長で補正する</summary>
        public static float GetSlotScale(int slotNo, int baseHeight, out float rootY)
        {
            rootY = 0f;
            if (slotNo < 0)
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
            return CameraHeightCorrection.CalcScale(baseHeight, MaidHeightReader.GetHeight(maid));
        }
```

`CameraSnapshot` 側の倍率の取得:

```csharp
        private static float GetPresetScale(ScenePresetCamera state, out float rootY)
        {
            // タイムラインの基準身長で補正すると、タイムラインの有無や設定で保存時と構図が変わる
            return MTEP.CameraTimelineLayer.GetSlotScale(state.heightSlotNo, state.heightBase, out rootY);
        }
```

- [ ] **Step 6: 適用（`ApplyState`）に身長補正を足す**

シグネチャと summary を変える:

```csharp
        /// <param name="applyHeight">
        /// false なら身長補正をかけずに注視点を置き、身長補正の設定も戻さない。
        /// メイドの呼出が終わる前に適用し、身長補正だけを後で ApplyHeight でかけるときに使う
        /// </param>
        public static void ApplyState(ScenePresetCamera state, bool applyHeight = true)
```

`!hasFollow` の分岐の注視点の書き込みを置き換える:

```csharp
            if (!hasFollow)
            {
                mainCamera.SetTargetPos(applyHeight ? CorrectTargetPos(state) : state.targetPos);
            }
```

`follow.Apply()` の前（メソッド末尾近く、`shakeParams` の書き戻しの後）に、身長補正の設定の復元を足す:

```csharp
            if (applyHeight)
            {
                RestoreHeightSetting(state);
            }
```

ヘルパーを足す:

```csharp
        /// <summary>
        /// 身長補正の設定を戻す。プリセットは断面として扱うため、補正を持たない旧形式は基準メイドなしになる。
        /// タイムラインが無いときはライブ値を持つ先が無いので触らない
        /// </summary>
        private static void RestoreHeightSetting(ScenePresetCamera state)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            if (timeline == null)
            {
                return;
            }

            MTEP.CameraHeightState.instance.SetLive(state.heightSlotNo);
            // 上書きすると、設定済みの基準身長を変えるのは「取得」と手入力だけという規則が崩れる
            if (state.hasHeight && state.heightBase > 0)
            {
                timeline.SetCameraHeightBaseIfUnset(state.heightSlotNo, state.heightBase);
            }
        }

        private static Vector3 CorrectTargetPos(ScenePresetCamera state)
        {
            var pos = state.targetPos;
            if (state.hasFollow || !state.hasHeight)
            {
                return pos;
            }

            float rootY;
            var scale = GetPresetScale(state, out rootY);
            pos.y = MTEP.CameraHeightCorrection.Correct(pos.y, rootY, scale);
            return pos;
        }

        /// <summary>
        /// ApplyState(state, false) で飛ばした身長補正 (設定の復元と注視点の補正) をかける。
        /// 補正前の値 (state.targetPos) から計算するため、何度呼んでも二重にはかからない
        /// </summary>
        public static void ApplyHeight(ScenePresetCamera state)
        {
            if (state == null)
            {
                return;
            }

            RestoreHeightSetting(state);
            if (state.hasFollow || !state.hasHeight)
            {
                return;
            }

            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            if (mainCamera == null)
            {
                return;
            }
            mainCamera.SetTargetPos(CorrectTargetPos(state));
        }
```

`Apply(Maid maid) => ApplyState(_state);` は既定の `applyHeight = true` のままでよい（Undo/Redo はメイドが揃っている）。Undo/Redo の補正にも snapshot の `heightBase`（記録時の基準身長）を使う。記録時と同じ倍率で戻すので、後から基準身長を変えていても記録時の画面の位置へ戻る

- [ ] **Step 7: 履歴の比較に基準メイドを足す**

`Approximately` の `&& _state.followRotation == o._state.followRotation` の後に足す:

```csharp
                // 基準身長 (heightBase) は Undo の対象外なので比べない
                && _state.heightSlotNo == o._state.heightSlotNo
```

- [ ] **Step 8: ビルドとテスト**

Run: ビルドとテスト
Expected: 両構成のビルド成功、すべて PASS

- [ ] **Step 9: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightCorrection.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightCorrectionTests.cs
git commit -m "feat(camera): カメラの構図の記録と適用に身長補正をかける"
```

本文には「別のメイドでプリセットを読んでも構図が合い、基準メイドも Undo で戻るようにするため」を書く。

---

### Task 4: シーンプリセットの身長補正をメイドの読込後にかける

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1472-1477,1739-1765`

**Interfaces:**
- Consumes: Task 3 の `CameraSnapshot.ApplyState(state, bool applyHeight)` / `CameraSnapshot.ApplyHeight(state)`
- Produces: なし

`ScenePresetManager` はメイドの呼出と非同期の読込に依存し、単体テストを持たない。実機で確かめる（Task 6）。

- [ ] **Step 1: `ApplyScenery` では補正をかけない**

```csharp
            if (ShouldApplyCamera(data))
            {
                // ここはメイドの呼出・配置より前で、基準メイドのルートの高さや身長が定まっていない。
                // ここで補正すると呼出前のメイドの身長で補正される。自動ロードの先出しではタイムラインが未生成で、
                // 基準メイドの選択も戻せない
                CameraSnapshot.ApplyState(data.camera, applyHeight: false);
            }
```

- [ ] **Step 2: `FinishApply` で身長補正をかける**

`FinishApply` の末尾（`_resolvedAssignments.Clear();` の前）に足す:

```csharp
            if (ShouldApplyCamera(data))
            {
                CameraSnapshot.ApplyHeight(data.camera);
            }
```

`FinishApply` は、保留が無ければ `Apply` の末尾で、保留があれば `_pendingApplies` が空になった時点（`ScenePresetManager.cs:1725` 付近、全メイドの `ApplyMaid` 後）で呼ばれる。自動ロードの先出し（`PreloadAutoLoadScenery`）の後も本適用は `Apply(data, skipScenery: true)` を通るため、補正は `FinishApply` で 1 回だけかかる。

- [ ] **Step 3: SceneCapture プリセットの経路を確かめる**

`ApplySceneCapturePreset` の `CameraSnapshot.ApplyState(converted.camera)` は変更しない。SceneCapture の変換結果は `heightSlotNo = -1` なので補正はかからず、基準メイドは「なし」になる（spec の断面の扱い）。

- [ ] **Step 4: ビルドとテスト**

Run: ビルドとテスト
Expected: 両構成のビルド成功、すべて PASS

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(scene-preset): カメラの身長補正をメイドの読込が終わってからかける"
```

---

### Task 5: ドキュメントを更新する

**Files:**
- Modify: `docs-site/timeline/layers-camera.md`（「身長補正」の節）
- Modify: `docs-site/guide/staging.md:17-20`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（タイムライン XML の互換の節。git 管理外なのでコミットしない）

- [ ] **Step 1: `layers-camera.md` の「身長補正」の節の末尾の箇条に足す**

```markdown
- カメラプリセットとシーンプリセットにも基準メイドと基準身長が保存されます。読み込むと、そのときのメイドの身長に合わせて注視点の高さを補正し、基準メイドの選択も戻します。タイムラインに基準身長が設定済みなら、プリセットの値で上書きしません
```

- [ ] **Step 2: `staging.md` のカメラプリセットの説明（17 行目付近）の後に足す**

```markdown
プリセットには身長補正の設定も保存され、身長の違うメイドで読んでも注視点の高さが合います（[カメラ系レイヤー](/timeline/layers-camera)の「身長補正」）。
```

- [ ] **Step 3: `W:\COM3D2_5\work\CLAUDE.md` の「カメラの身長補正は SE 独自。…」の項目の末尾に足す**

```markdown
シーンプリセットは v43 で camera に `heightSlotNo` / `heightBase` 属性（補正なしでは書き出さない）を追加し、追従なしの `targetPos` は基準身長の空間の値になった。旧 SE は属性を読み飛ばし、補正前の注視点のまま置く。カメラプリセットの文字列は 21 値（旧 SE では読めない）
```

- [ ] **Step 4: Commit（docs-site のみ）**

```bash
git add docs-site/timeline/layers-camera.md docs-site/guide/staging.md
git commit -m "docs(camera): プリセットが身長補正を保存することを説明する"
```

---

### Task 6: 実機確認

**Files:** なし（必要ならここで見つかった不具合を直してコミット）

`restart-verify` スキルで DLL を反映し、通常シーン（デイリー画面でエディタを有効にした状態）で確かめる。撮影モードでは確かめない。メイドは身長の違う 2 人（スロット 0 / 1）を呼ぶ。`com3d25-devbridge` の `eval_csharp` で `GameMain.Instance.MainCamera.GetTargetPos()` と `COM3D2.MotionTimelineEditor.Plugin.CameraHeightState.instance.liveSlotNo` を読んで数値で確かめる。

- [ ] **Step 1: カメラウィンドウの不具合修正**
  - タイムラインを読み込み、再生停止中（編集モード外）で追従メイドを変える → 編集モードに入り、値が戻らない。Ctrl+Z で追従が戻る
  - 追従中のオフセット・横回転を変える → 編集モードに入り、Undo で戻る
  - 身長補正の基準メイドを変える → 編集モードに入り、Undo で基準メイドが戻る
  - 基準身長を入力 → 編集モードに入る。Undo しても基準身長は戻らない
- [ ] **Step 2: カメラプリセット**
  - 基準メイド = スロット 0、基準身長取得済みでプリセットに保存 → 同じメイドで読み直すと注視点が同じ
  - スロット 0 のメイドの身長を変えて読む → 注視点の Y が `rootY + (y - rootY) * 今の身長 / 基準身長` になる
  - 別のタイムライン（基準身長未設定）で読む → 基準メイドがスロット 0 になり、基準身長がプリセットの値で入る。設定済みのタイムラインでは上書きされない
  - 旧形式（19 値）のプリセットを Config に手で書いて読む → 構図は従来どおり、基準メイドは「なし」
  - 追従中に保存したプリセットを読む → オフセットに補正がかからない
- [ ] **Step 3: シーンプリセット**
  - 身長補正ありで保存 → メイドを別の身長のメイドに替えた状態でメイドの読込込みで適用 → 呼出完了後に注視点が新しいメイドの身長で 1 回だけ補正される（`FinishApply` 後に `GetTargetPos` を読む）
  - 自動ロード（`scenePresetAutoLoadKey`）で同じプリセットを読む → 同じ結果
  - v42 以前のプリセットを読む → 従来どおり
  - タイムラインを読み込んだ状態で適用 → 基準メイドの選択も戻る（`liveSlotNo` を読む）
  - 呼出直後の `FinishApply` の時点で、新しいメイドの身長（`MaidHeightReader.GetHeight`）が反映済みか数値で確かめる
  - メイド読込を待つ間にカメラを動かすと、`FinishApply` で注視点がプリセットの位置へ戻る（仕様として許容。気になる挙動なら報告する）
- [ ] **Step 4: 修正があればコミットする**

## レビュー却下メモ

- Undo/Redo は snapshot の古い `heightBase` ではなくタイムラインの今の基準身長で補正すべき — snapshot は記録時の基準身長で逆補正しているため、同じ値で補正すれば記録時の画面の位置へ正確に戻る。タイムラインの値を使うと、基準身長を変えた後の Undo で記録時と違う位置になる。次のキーは今の基準身長で逆補正されるだけで値は跳ねない
- 保存時に基準メイドが不在だと Y が未補正のまま保存される — タイムラインのキーと同じ挙動で spec どおり。変更なし
