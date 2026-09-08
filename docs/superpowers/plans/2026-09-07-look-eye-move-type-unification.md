# 視線仕様の整理（メイド目線への一本化）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 「視線をキー化」トグルと「顔を向ける」「目を向ける」トグルを撤去し、視線の入口をタイムラインの注視先キーと「メイド目線」（`TimelineData.eyeMoveType`）の 2 つに絞る。あわせて向け先「マウス」をキー化し、シーンプリセットもメイド目線で保存・復元する。

**Architecture:** 向け先（`body0.trsLookTarget`）の書き手は引き続き `MaidLookController` 1 本で、タイムラインの `MaidCache` が `MaidLookBridge` 経由で常時それを駆動する（従来の「キー化 ON」相当が常態になる）。`boHeadToCam` / `boEyeToCam` / `boEyeSorashi` の書き手は `MaidLookBridge.ApplyEyeMoveType` だけになり、「タイムラインが視線を動かさない」状態は「メイド目線: 無し」で表す。SE 側だけにあった向け先 UI（マウス / オブジェクト / 無し と SE の顔向きスライダー）は表情ウィンドウから消し、マウスはキーの注視先種別として吸収する。

**Tech Stack:** C# 9 / net48 / xunit 2.9 / MSBuild（`GameVersion=COM3D2` と `COM3D25` の 2 構成）

**Spec:** ブレインストーミング結果（この計画の冒頭に要約。別ファイルの spec は無し）

## Global Constraints

- コードのコメントとログ文言は日本語
- git worktree は使わない。作業はメインの作業ディレクトリで行う
- ビルド確認に `debug.bat` を使わない（ゲームフォルダへ DLL をコピーするため）。MSBuild を直接叩く
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。テストプロジェクトは COM3D25 構成のプラグイン出力を参照するので、先に COM3D25 構成でビルドする
- 両構成（COM3D2 / COM3D25）を必ずビルドする
- タイムライン XML は MTE → SceneEditor の一方向互換のみ。`UseHeadKey` 要素は削除し、旧ファイルの要素は XmlSerializer が読み飛ばす（移行処理は入れない）
- `MaidLookMode.オブジェクト` / `無し` の列挙値と、プリセットの読み書き（`ScenePresetLook.mode` / `targetPath` 等）は互換のため残す。UI から選べなくするだけ
- コミットは `commit` スキルで行う（Conventional Commits・日本語）

## 決定事項（ブレインストーミングの結論）

| 項目 | 決定 |
|---|---|
| 「視線をキー化」（`useHeadKey`） | 撤去。タイムラインの注視先が常時の入口 |
| 「顔を向ける」「目を向ける」トグル | 撤去。メイド目線が `ApplyEyeMoveType` で決める |
| 向け先「マウス」 | `LookAtTargetType.Mouse` を追加してキー化 |
| 向け先「オブジェクト」「無し」 | UI から外す（列挙値・プリセット互換は残す） |
| 頭ボーン回転キーを隠す条件 | `MaidLookBridge.IsHeadToCam(timeline.eyeMoveType)` |
| シーンプリセット | `ScenePresetLook.eyeMoveType` を追加（v33）。新規保存では `headToCam` / `eyeToCam` を書かず、旧プリセットの読み込みだけ従来どおり |
| タイムライン未読込時の視線タブ | 灰色の案内だけを出す（SE 向け先 UI は出さない） |

## File Structure

| ファイル | 責務 / 変更 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs` | `LookAtTargetType` に `Mouse` を追加。`UpdateLookAtTarget` の `ResolveLookMode` 呼び出しから `useHeadKey` を外す |
| `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs` | マウスの写像追加、`ResolveLookMode` の `useHeadKey` 引数削除、`GetSelectableModes()` を 1 本化 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLookAtTarget.cs` | `TargetTypeNames` に「マウス」を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs` | `UpdateFrame` の switch に `Mouse` を追加（対象番号なし） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `useHeadKey` プロパティと XML 往復を削除。`eyeMoveType` の XML 往復を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `UseHeadKey` 要素を削除、`EyeMoveType` 要素を追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs` | `boHeadToCam` / `boEyeToCam` の記録・復元を撤去し、復元後はメイド目線で塗り直す |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs:251` | 追従フラグの所有者を示すコメントを修正 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRotation.cs` | 頭の `isHidden` をメイド目線で判定 |
| `source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs` | `IsHeadKeyEnabled` / `HeadKeyDisabledMessage` を削除、向け先リストを `GetSelectableModes()` に |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/EyesItemInspector.cs` | キー化無効の案内分岐を削除 |
| `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs` | 視線タブをタイムライン行だけに組み替え。SE 向け先コンボ・顔向きスライダー・追従トグル・オブジェクト指定 UI を削除 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs` | 追従再開の手段を示すコメントを修正 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs` | 同上（該当コメントがあれば） |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetLook.eyeMoveType` 追加、`CurrentVersion = 33` |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | `CaptureLook` / `ApplyLook` をメイド目線に対応 |
| `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs` | 新シグネチャ・マウス写像に更新 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs` | `useHeadKey` を外す |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs` | `eyeMoveType` の往復と旧形式の読み込みを追加 |
| `docs/se-mte-state-duplication-survey.md` | 「視線の統合後仕様」を更新 |

---

### Task 1: MaidLookBridge にマウスを追加し、useHeadKey 依存を外す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:25-31`（`LookAtTargetType`）、`:546-570`（`UpdateLookAtTarget`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLookAtTarget.cs:19-25`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs:258-276`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs`

**Interfaces:**
- Produces: `MTEP.LookAtTargetType.Mouse`（末尾に追加、値 4）
- Produces: `MaidLookMode MaidLookBridge.ResolveLookMode(MTEP.LookAtTargetType targetType, bool hasTarget, bool isEyeSorashi)`（`useHeadKey` 引数を削除。null を返す経路が消えるので戻り値は非 null の `MaidLookMode`）
- Produces: `List<MaidLookMode> MaidLookBridge.GetSelectableModes()`（引数なし。順序: カメラ, マウス, メイド, モデル, 方向指定）
- Produces: `MaidLookBridge.ToLookMode(Mouse) == MaidLookMode.マウス`、`ToTargetType(MaidLookMode.マウス) == Mouse`

- [ ] **Step 1: テストを新仕様に書き換える**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs` を以下の内容で置き換える（`ResolveLookMode` から第 1 引数を外し、`useHeadKey` 無効のケースを削除、マウスのケースを追加、`GetSelectableModes` を 1 本に）:

```csharp
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>タイムライン視線 → SE 向け先モードの写像</summary>
    public class MaidLookBridgeTests
    {
        [Theory]
        [InlineData(MTEP.LookAtTargetType.Camera, true, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Camera, false, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Mouse, true, MaidLookMode.マウス)]
        [InlineData(MTEP.LookAtTargetType.Mouse, false, MaidLookMode.マウス)]
        [InlineData(MTEP.LookAtTargetType.Maid, true, MaidLookMode.メイド)]
        [InlineData(MTEP.LookAtTargetType.Model, true, MaidLookMode.モデル)]
        [InlineData(MTEP.LookAtTargetType.None, false, MaidLookMode.方向指定)]
        public void ResolveLookMode_注視先種別を向け先モードへ写す(
            MTEP.LookAtTargetType targetType, bool hasTarget, MaidLookMode expected)
        {
            var mode = MaidLookBridge.ResolveLookMode(targetType, hasTarget, isEyeSorashi: false);
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.Maid)]
        [InlineData(MTEP.LookAtTargetType.Model)]
        public void ResolveLookMode_注視対象が未解決なら顔向きの方向指定へ倒す(
            MTEP.LookAtTargetType targetType)
        {
            var mode = MaidLookBridge.ResolveLookMode(targetType, false, isEyeSorashi: false);
            Assert.Equal(MaidLookMode.方向指定, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.None)]
        [InlineData(MTEP.LookAtTargetType.Maid)]
        public void ResolveLookMode_注視先なしでそらし中は向け先無しにする(
            MTEP.LookAtTargetType targetType)
        {
            // そらし演出は trsLookTarget == null のときだけ動くため、
            // 方向指定の注視点を作らず向け先を空ける
            var mode = MaidLookBridge.ResolveLookMode(targetType, false, isEyeSorashi: true);
            Assert.Equal(MaidLookMode.無し, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.Camera, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Mouse, MaidLookMode.マウス)]
        public void ResolveLookMode_対象の同定が要らない注視先はそらし中でも優先する(
            MTEP.LookAtTargetType targetType, MaidLookMode expected)
        {
            var mode = MaidLookBridge.ResolveLookMode(targetType, true, isEyeSorashi: true);
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData(Maid.EyeMoveType.無し, false, false, false)]
        [InlineData(Maid.EyeMoveType.無視する, false, false, false)]
        [InlineData(Maid.EyeMoveType.顔を向ける, true, true, false)]
        [InlineData(Maid.EyeMoveType.顔だけ動かす, true, false, false)]
        [InlineData(Maid.EyeMoveType.顔をそらす, true, true, true)]
        [InlineData(Maid.EyeMoveType.目と顔を向ける, true, true, false)]
        [InlineData(Maid.EyeMoveType.目だけ向ける, false, true, false)]
        [InlineData(Maid.EyeMoveType.目だけそらす, false, true, true)]
        public void 目線種別のフラグはEyeToCameraと同じ組み合わせになる(
            Maid.EyeMoveType eyeMoveType, bool headToCam, bool eyeToCam, bool eyeSorashi)
        {
            Assert.Equal(headToCam, MaidLookBridge.IsHeadToCam(eyeMoveType));
            Assert.Equal(eyeToCam, MaidLookBridge.IsEyeToCam(eyeMoveType));
            Assert.Equal(eyeSorashi, MaidLookBridge.IsEyeSorashi(eyeMoveType));
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.None, MaidLookMode.方向指定)]
        [InlineData(MTEP.LookAtTargetType.Camera, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Mouse, MaidLookMode.マウス)]
        [InlineData(MTEP.LookAtTargetType.Maid, MaidLookMode.メイド)]
        [InlineData(MTEP.LookAtTargetType.Model, MaidLookMode.モデル)]
        public void ToLookMode_キーの注視先種別を統合列挙へ写す(
            MTEP.LookAtTargetType targetType, MaidLookMode expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToLookMode(targetType));
        }

        [Theory]
        [InlineData(MaidLookMode.カメラ, MTEP.LookAtTargetType.Camera)]
        [InlineData(MaidLookMode.マウス, MTEP.LookAtTargetType.Mouse)]
        [InlineData(MaidLookMode.モデル, MTEP.LookAtTargetType.Model)]
        [InlineData(MaidLookMode.メイド, MTEP.LookAtTargetType.Maid)]
        [InlineData(MaidLookMode.方向指定, MTEP.LookAtTargetType.None)]
        // キー化できない値は顔向きキーで駆動する None へ丸める
        [InlineData(MaidLookMode.無し, MTEP.LookAtTargetType.None)]
        [InlineData(MaidLookMode.オブジェクト, MTEP.LookAtTargetType.None)]
        public void ToTargetType_統合列挙をキーの注視先種別へ写す(
            MaidLookMode mode, MTEP.LookAtTargetType expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToTargetType(mode));
        }

        [Fact]
        public void GetSelectableModes_キー化できる向け先だけを出す()
        {
            Assert.Equal(
                new[]
                {
                    MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.メイド,
                    MaidLookMode.モデル, MaidLookMode.方向指定,
                },
                MaidLookBridge.GetSelectableModes());
        }

        [Fact]
        public void GetSelectableModes_返したリストを書き換えても次の呼び出しに影響しない()
        {
            var modes = MaidLookBridge.GetSelectableModes();
            modes.Clear();
            Assert.Equal(5, MaidLookBridge.GetSelectableModes().Count);
        }
    }
}
```

- [ ] **Step 2: ビルドが失敗することを確認する**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter MaidLookBridgeTests
```
Expected: テストプロジェクトのビルドエラー（`LookAtTargetType.Mouse` が無い、`ResolveLookMode` の引数不一致）

- [ ] **Step 3: `LookAtTargetType` に `Mouse` を追加する**

`source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:25-31`:

```csharp
    public enum LookAtTargetType
    {
        None,
        Camera,
        Maid,
        Model,
        /// <summary>
        /// マウスカーソル位置。注視点の生成と追従は SE の MaidLookController が担う。
        /// キー値は int のため末尾に足し、既存 XML の値をずらさない
        /// </summary>
        Mouse,
    }
```

- [ ] **Step 4: `TargetTypeNames` と `EyesTimelineLayer.UpdateFrame` にマウスを足す**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLookAtTarget.cs:19-25`:

```csharp
        public static readonly string[] TargetTypeNames = new string[]
        {
            "手動",
            "カメラ",
            "メイド",
            "モデル",
            "マウス",
        };
```

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs:264-276` の switch に `Mouse` を追加（対象番号・ポイントは持たない）:

```csharp
                    switch (targetType)
                    {
                        case LookAtTargetType.Camera:
                        case LookAtTargetType.Mouse:
                            break;
                        case LookAtTargetType.Maid:
                            trans.targetIndex = maidCache.lookAtTargetIndex;
                            trans.maidPointType = maidCache.lookAtMaidPointType;
                            break;
                        case LookAtTargetType.Model:
                            trans.targetIndex = maidCache.lookAtTargetIndex;
                            break;
                    }
```

- [ ] **Step 5: `MaidLookBridge` を書き換える**

`source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs` の `KeyedModes` / `UnkeyedModes` / `GetSelectableModes` / `ToLookMode` / `ToTargetType` / `ResolveLookMode` を以下に置き換える（`ResolveNoTargetMode` 以降は変更なし）:

```csharp
        /// <summary>
        /// 向け先の選択肢。キーの注視先種別に写せる値だけを出す。
        /// 「無し」は None が方向指定と衝突するため出さず、そらし演出は目線種別で表す。
        /// 「オブジェクト」は任意 Transform の同定情報が int 3 値のキーに収まらないため出さない
        /// </summary>
        private static readonly MaidLookMode[] SelectableModes =
        {
            MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.メイド,
            MaidLookMode.モデル, MaidLookMode.方向指定,
        };

        /// <summary>
        /// 向け先の選択肢。コンボボックスへ渡す List を呼び出し側が持ち回るため、毎回複製して返す
        /// </summary>
        public static List<MaidLookMode> GetSelectableModes()
        {
            return new List<MaidLookMode>(SelectableModes);
        }

        /// <summary>キーの注視先種別を統合列挙へ写す (UI 表示用)</summary>
        public static MaidLookMode ToLookMode(MTEP.LookAtTargetType targetType)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Mouse:
                    return MaidLookMode.マウス;
                case MTEP.LookAtTargetType.Maid:
                    return MaidLookMode.メイド;
                case MTEP.LookAtTargetType.Model:
                    return MaidLookMode.モデル;
                default:
                    return MaidLookMode.方向指定;
            }
        }

        /// <summary>
        /// 統合列挙をキーの注視先種別へ写す。
        /// キー化できない値 (任意オブジェクト・無し) は、顔向きキーで駆動する
        /// None へ丸める (選択肢には出さないが、外部から渡っても壊れないようにする)
        /// </summary>
        public static MTEP.LookAtTargetType ToTargetType(MaidLookMode mode)
        {
            switch (mode)
            {
                case MaidLookMode.カメラ:
                    return MTEP.LookAtTargetType.Camera;
                case MaidLookMode.マウス:
                    return MTEP.LookAtTargetType.Mouse;
                case MaidLookMode.メイド:
                    return MTEP.LookAtTargetType.Maid;
                case MaidLookMode.モデル:
                    return MTEP.LookAtTargetType.Model;
                default:
                    return MTEP.LookAtTargetType.None;
            }
        }

        /// <summary>
        /// タイムラインの注視先から SE の向け先モードを決める。
        ///
        /// 注視先が無い (種別 None または対象が未解決) ときは、顔向きキーが
        /// 効くよう「方向指定」にする。ただし視線そらし中は TBody の演出が
        /// trsLookTarget == null を要求するため「無し」へ倒す。
        /// カメラとマウスは対象の同定が要らないため、常にそのまま写す
        /// </summary>
        /// <param name="targetType">タイムラインの注視先種別</param>
        /// <param name="hasTarget">注視先の Transform が解決できたか</param>
        /// <param name="isEyeSorashi">目線種別が視線そらしか</param>
        public static MaidLookMode ResolveLookMode(
            MTEP.LookAtTargetType targetType,
            bool hasTarget,
            bool isEyeSorashi)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Mouse:
                    return MaidLookMode.マウス;
                case MTEP.LookAtTargetType.Maid:
                    // メイド注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.メイド;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                case MTEP.LookAtTargetType.Model:
                    // モデル注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.モデル;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                default:
                    return ResolveNoTargetMode(isEyeSorashi);
            }
        }
```

クラス冒頭の summary にある「フラグだけを ApplyEyeMoveType で写す」の説明はそのまま。`KeyedModes` / `UnkeyedModes` は削除する。

- [ ] **Step 6: `MaidCache.UpdateLookAtTarget` の呼び出しを合わせる**

`source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:553-560`:

```csharp
            var lookAtTarget = GetLookAtTarget();
            var lookMode = SEP.MaidLookBridge.ResolveLookMode(
                lookAtTargetType, lookAtTarget != null,
                SEP.MaidLookBridge.IsEyeSorashi(timeline.eyeMoveType));

            // メイド注視・モデル注視は Transform ではなく対象の同定情報で渡し、
            // 実際の Transform は SE のコントローラが適用のたびに引き直す
            SEP.MaidLookBridge.ApplyLookMode(
                maid, lookMode, lookAtTarget, _lookDirection,
                GetLookAtMaid(), lookAtMaidPointType, GetLookAtModelName());
```

`if (lookMode == null) return;` と `lookMode.Value` を消す。`GetLookAtTarget()` は `Mouse` で `default` に落ちて null を返すので変更不要（マウス注視点は SE 側が作る）。

- [ ] **Step 7: ここまでで `TimelineLookRowDrawer` のコンパイルを通す**

`source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs:37-39` の `KeyedLookModes` を引数なしに変える（`IsHeadKeyEnabled` の削除は Task 3 で行う）:

```csharp
        /// <summary>向け先の選択肢。毎フレーム複製しないよう控えておく</summary>
        private static readonly List<MaidLookMode> SelectableLookModes =
            MaidLookBridge.GetSelectableModes();
```

同ファイルの `DrawLookAtTargetRows` 内の `KeyedLookModes` 2 箇所を `SelectableLookModes` に置き換え、直前のコメントを「選択肢に無い値 (無し・オブジェクト) は ToLookMode が方向指定へ丸める」に直す。

`TimelineLookRowDrawer.IsHeadKeyEnabled` は `timeline.useHeadKey` を参照しているが、`useHeadKey` の削除は Task 2 なので、この時点ではまだコンパイルが通る。

- [ ] **Step 8: ビルドとテストが通ることを確認する**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter MaidLookBridgeTests
```
Expected: PASS（全ケース）

- [ ] **Step 9: コミット**

`commit` スキルで以下をコミットする:
- `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs`
- `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs`
- `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLookAtTarget.cs`
- `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs`
- `source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs`
- `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs`

メッセージ例: `feat(timeline): 注視先キーにマウスを追加し、向け先の解決からキー化フラグを外す`

---

### Task 2: `useHeadKey` を `eyeMoveType` の XML 保存に置き換え、頭ボーン回転キーの表示条件をメイド目線に変える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:315-329`（プロパティ）、`:809`、`:963`（XML 往復）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:206-207`（`UseHeadKey` → `EyeMoveType`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRotation.cs:15-36`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs:18-35`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/EyesItemInspector.cs:65-67`, `:89-94`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs:32`, `:51`

**Interfaces:**
- Consumes: `MaidLookBridge.IsHeadToCam(Maid.EyeMoveType)`（既存）
- Produces: `TimelineData.useHeadKey` / `TimelineXml.useHeadKey` / `TimelineLookRowDrawer.IsHeadKeyEnabled` / `TimelineLookRowDrawer.HeadKeyDisabledMessage` の削除
- Produces: `TimelineXml.eyeMoveType`（`Maid.EyeMoveType`、要素名 `EyeMoveType`、既定 `無し`）と `TimelineData` の往復

- [ ] **Step 1: テストを `useHeadKey` から `eyeMoveType` へ差し替える**

現状 `TimelineData.eyeMoveType` はタイムライン XML に保存されていない（`TimelineXml` に対応要素が無い）。`useHeadKey` の代わりにメイド目線が「タイムラインが視線を動かすか」を決めるので、保存→再読込で `無し` に戻ると機能退行になる。XML へ往復させる。

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineSettingXmlTests.cs:32` の `useHeadKey = true,` を `eyeMoveType = Maid.EyeMoveType.目と顔を向ける,` に、`:51` の `Assert.True(dst.useHeadKey);` を `Assert.Equal(Maid.EyeMoveType.目と顔を向ける, dst.eyeMoveType);` に置き換える。

さらに同クラスに旧形式の読み込みテストを追加する（`RoundTrip` ヘルパーの隣に、XML 文字列から直接デシリアライズする形。既存の `serializer` フィールドを使う）:

```csharp
        [Fact]
        public void メイド目線を持たない旧XMLは無しとして読める()
        {
            // 旧 SE / MTE 産の XML は UseHeadKey を持ち EyeMoveType を持たない。
            // UseHeadKey は読み飛ばし、メイド目線は既定 (無し) になる
            var xml = "<?xml version=\"1.0\" encoding=\"utf-16\"?>"
                + "<TimelineData><UseHeadKey>true</UseHeadKey></TimelineData>";
            using (var reader = new System.IO.StringReader(xml))
            {
                var dst = (TimelineXml) serializer.Deserialize(reader);
                Assert.Equal(Maid.EyeMoveType.無し, dst.eyeMoveType);
            }
        }
```

ルート要素名は `TimelineXml` の `[XmlRoot]` 属性に合わせる（`grep -n 'XmlRoot' source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` で確認し、無ければクラス名 `TimelineXml`）。

- [ ] **Step 2: `TimelineData` / `TimelineXml` を書き換える**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:206-207` の `[XmlElement("UseHeadKey")] public bool useHeadKey = false;` を次に置き換える。XmlSerializer は未知要素を読み飛ばすので、`UseHeadKey` を持つ MTE 産・旧 SE 産の XML はそのまま読める:

```csharp
        /// <summary>メイド目線。旧形式 (要素なし) は既定の 無し で読む</summary>
        [XmlElement("EyeMoveType")]
        public Maid.EyeMoveType eyeMoveType = Maid.EyeMoveType.無し;
```

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:315-329` の `_useHeadKey` フィールドと `useHeadKey` プロパティを削除。`:809` の `useHeadKey = xml.useHeadKey;` を `eyeMoveType = xml.eyeMoveType;` に、`:963` の `xml.useHeadKey = useHeadKey;` を `xml.eyeMoveType = eyeMoveType;` に置き換える。ロード時は setter が `maidManager.UpdateHeadLook()` を呼ぶが、`useMuneKeyL/R` の setter も同様に副作用を持つ並びなので問題ない。

- [ ] **Step 3: 頭ボーン回転キーの表示条件を変える**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRotation.cs:15-36`:

```csharp
        /// <summary>
        /// 頭はメイド目線が顔を向ける系のとき隠す。視線が頭を駆動している間は
        /// 回転キーが効かないため。胸は物理無効の設定に従う
        /// </summary>
        public override bool isHidden
        {
            get
            {
                if (isHead)
                {
                    return COM3D2.SceneEditor.Plugin.MaidLookBridge.IsHeadToCam(timeline.eyeMoveType);
                }

                if (isBustL)
                {
                    return !timeline.useMuneKeyL;
                }

                if (isBustR)
                {
                    return !timeline.useMuneKeyR;
                }

                return false;
            }
        }
```

このファイルの namespace は `COM3D2.MotionTimelineEditor.Plugin` なので、`MaidLookBridge` は完全修飾で参照する（同ファイルに `using SEP = COM3D2.SceneEditor.Plugin;` が無ければ追加して `SEP.MaidLookBridge` でもよい。`MaidCache.cs` は `SEP` エイリアスを使っているので、それに揃える）。

- [ ] **Step 4: `TimelineLookRowDrawer` と `EyesItemInspector` の案内分岐を消す**

`source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs:18-35` の `HeadKeyDisabledMessage` 定数と `IsHeadKeyEnabled` プロパティを削除する。クラスの summary から「キー化」の言及を外し、次に置き換える:

```csharp
    /// <summary>
    /// タイムライン視線 (注視先 / 顔向きキー) の行描画。
    /// MaidFaceWindow の視線タブと TimelineItemInspector (瞳レイヤーの項目表示) で共有する。
    /// 書き込み先はどのスナップショットにも含まれない MaidCache のため履歴は記録しない。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
```

`source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/EyesItemInspector.cs:65-67` の `isHeadKeyEnabled` 取得とコメント、`:89-94` の `if (!isHeadKeyEnabled) { ... continue; }` ブロックを削除する。

- [ ] **Step 5: ビルドし、残る参照を潰す**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
```
Expected: `MaidFaceWindow.cs` の `IsHeadKeyEnabled`（:309）と `timeline.useHeadKey`（:402-403）でエラー。これは Task 3 で直すので、この時点では `MaidFaceWindow.cs:309` を `var isKeyed = timeline != null;`、`:402-403` を削除して一時的に通す（Task 3 で行ごと書き換える）。

Run:
```
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: PASS

- [ ] **Step 6: コミット**

`commit` スキルでコミットする。メッセージ例: `refactor(timeline): 視線のキー化フラグをメイド目線の保存に置き換え、頭の回転キーはメイド目線で隠す`

---

### Task 3: 視線タブをタイムライン行だけに組み替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs`（`DrawLookContent` 〜 `DrawLookSlider`、`_lookModeComboBox` / `_lookMaidComboBox` / `_lookModelComboBox` などの SE 向け先用フィールド、`UnkeyedLookModes`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs:196-206`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs:74-76`（コメントのみ）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs:251`（コメントのみ）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs:16-17`, `:59-60`, `:197-198`, `:244-245`

**Interfaces:**
- Consumes: `TimelineLookRowDrawer.DrawLookAtTargetRows(view, maidCache, labelWidth, rowHeight)` / `DrawLookDirectionRows(view, maidCache, labelWidth)`（既存）
- Consumes: `EyesPosRowDrawer`（既存、変更なし）
- Consumes: `MaidLookBridge.ApplyEyeMoveType(Maid, Maid.EyeMoveType)`（既存）

- [ ] **Step 1: 視線タブの描画を置き換える**

`source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs` の `DrawLookContent` から `DrawLookSlider` までを次の構成に書き換える。残す private メソッドは `DrawLookContent` / `DrawEyesPosSection`（変更なし）/ `DrawEyeMoveTypeRow` / `DrawKeyedLookResetRow`。削除するのは `DrawLookKeyToggleRow` / `DrawLookTargetRows` / `DrawHeadToCamRow` / `DrawLookDirectionSliders` / `DrawLookMaidRows` / `DrawLookModelRow` / `DrawLookObjectRows` / `DrawLookSlider` と、それらだけが使うフィールド（`_lookModeComboBox` / `_lookMaidComboBox` / `_lookMaidPointComboBox` / `_lookModelComboBox` / `UnkeyedLookModes` 等。削除前に `grep -n` で他タブから参照されていないことを確認する）。`lookController` プロパティも視線タブ以外で使っていなければ削除する。

```csharp
        /// <summary>
        /// 視線タブ。向け先 (trsLookTarget) の所有者は MaidLookController だが、
        /// 入口はタイムラインの注視先キー (MaidCache) に一本化しており、
        /// MaidLookBridge がそれをコントローラへ流す。
        /// 顔・瞳の追従フラグは「メイド目線」が決めるため、ここでは個別に出さない
        /// </summary>
        private void DrawLookContent(GUIView view, Maid target)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawTimelineLookSection(view, target, timeline);
            DrawEyesPosSection(view, target, timeline);

            view.EndScrollView();
        }

        /// <summary>
        /// メイド目線・注視先・顔向きキー。書き込み先はタイムライン設定と MaidCache で、
        /// タイムライン未読込のときは編集できないため案内だけ出す
        /// </summary>
        private void DrawTimelineLookSection(GUIView view, Maid target, MTEP.TimelineData timeline)
        {
            if (timeline == null)
            {
                view.DrawLabel("タイムライン未読込のため視線は編集できません",
                    -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (maidCache == null)
            {
                view.DrawLabel("タイムライン側の対象メイドが見つかりません",
                    -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            DrawEyeMoveTypeRow(view, timeline);
            _timelineLookRowDrawer.DrawLookAtTargetRows(view, maidCache, LABEL_WIDTH, ROW_HEIGHT);

            view.AddSpace(5);
            _timelineLookRowDrawer.DrawLookDirectionRows(view, maidCache, LABEL_WIDTH);

            DrawKeyedLookResetRow(view, maidCache);
        }

        /// <summary>
        /// 目線種別。顔・瞳の追従とそらしをまとめて決める (MaidLookBridge.ApplyEyeMoveType)。
        /// 「無し」がタイムラインに視線を動かさせない指定になる
        /// </summary>
        private void DrawEyeMoveTypeRow(GUIView view, MTEP.TimelineData timeline)
        {
            _eyeMoveTypeComboBox.currentIndex = (int) timeline.eyeMoveType;
            DrawLabeledComboBox("メイド目線", _eyeMoveTypeComboBox);
        }

        /// <summary>キー化される視線の初期化。書き込み先は MaidCache のため履歴は記録しない</summary>
        private void DrawKeyedLookResetRow(GUIView view, MTEP.MaidCache maidCache)
        {
            if (view.DrawButton("タイムライン視線を初期化", 190, ROW_HEIGHT))
            {
                maidCache.lookDirection = Vector2.zero;
                maidCache.lookAtTargetType = MTEP.LookAtTargetType.None;
            }
        }
```

`DrawEyesPosSection` は既存のまま。`_eyeMoveTypeComboBox` の summary は「顔/瞳の追従トグルのプリセットで」を「顔/瞳の追従フラグを決める設定で」に直す。

- [ ] **Step 2: 使わなくなった using を確認する**

`MaidFaceWindow.cs` で `SelectionManager` / `MTEUtils.GetReadyMaidList` 等が視線タブ専用だった場合、削除後に未使用 using の警告が出ないか確認する（警告のみなら放置でよいが、`System.Linq` 等は他所で使っているはず）。

- [ ] **Step 3: 頭ドラッグ／ギズモのコメントを直す**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs:203-205` を次に置き換える:

```csharp
        /// 離した後も追従は切ったままにする。戻すと回した頭が即カメラ向きへ引かれて操作が無に帰すため。
        /// 追従の再開は表情ウィンドウ視線タブの「メイド目線」を選び直すか、
        /// タイムラインの再生 (UpdateHeadLook) で行う（顔向きドラッグ MaidFaceDragPoint も同じ扱い）
```

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs:74` のコメントは「追従が効いたままだと LateUpdate で上書きされるため切る。戻すのは「メイド目線」の選び直し」に変える。

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs:251` の「boHeadToCam / boEyeToCam はウィンドウのトグルの持ち物なのでここでは触らない。」を「boHeadToCam / boEyeToCam はメイド目線 (MaidLookBridge.ApplyEyeMoveType) の持ち物なのでここでは触らない。」に変える。

- [ ] **Step 3b: Undo スナップショットから追従フラグを外す**

`PoseSnapshot` は `boHeadToCam` / `boEyeToCam` を直接記録・復元している。トグル撤去後もこれが残ると、メイド目線を変えた後の Undo でフラグだけが古い値へ戻り、メイド目線と食い違う。フラグの書き手をメイド目線に一本化するため、記録をやめて復元後に塗り直す。

`source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs`:
- `:16-17` の `private bool _headToCam;` / `private bool _eyeToCam;` を削除
- `:59-60` の `snapshot._headToCam = body.boHeadToCam;` / `snapshot._eyeToCam = body.boEyeToCam;` を削除
- `:244-245` の `|| _headToCam != o._headToCam` / `|| _eyeToCam != o._eyeToCam` を削除
- `:197-198` の `body.boHeadToCam = _headToCam;` / `body.boEyeToCam = _eyeToCam;` を次に置き換える:

```csharp
                // 追従フラグはメイド目線の持ち物なので記録せず、復元後にメイド目線で塗り直す。
                // 頭ドラッグが切った追従も、ドラッグ前へ戻す Undo ではここで戻る
                var timeline = MTEP.TimelineManager.instance.timeline;
                if (timeline != null)
                {
                    MaidLookBridge.ApplyEyeMoveType(maid, timeline.eyeMoveType);
                }
```

`PoseSnapshot.cs` に `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` が既にあることを確認する（`_lookMaidPointType` の型 `MTEP.MaidPointType` で使っているはず）。

- [ ] **Step 4: 両構成をビルドし、テストを流す**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: 両構成ビルド成功、テスト PASS

- [ ] **Step 5: 実機で確認する（ゲーム起動中なら）**

MCP `com3d25-devbridge` の `ping` が通れば、表情ウィンドウの視線タブを `screenshot` で確認する:
- 行が「メイド目線 → 向け先 → (対象) → 顔向き左右/上下 → タイムライン視線を初期化 → 瞳位置」の順で並ぶこと
- 向け先で「マウス」を選ぶとメイドがカーソルを追うこと
- 「メイド目線: 無し」で顔も瞳も動かなくなること

ゲームが起動していなければこの手順は省略し、最終報告でその旨を書く。

- [ ] **Step 6: コミット**

`commit` スキルでコミットする。メッセージ例: `refactor(face): 視線タブをメイド目線と注視先キーに一本化する`

---

### Task 4: シーンプリセットをメイド目線に揃える（v33）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:468-492`（`ScenePresetLook`）、`:905-906`（バージョン）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1213-1231`（`CaptureLook`）、`:1777-1798`（`ApplyLook` 末尾）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs`

**Interfaces:**
- Produces: `ScenePresetLook.eyeMoveType`（`string`、`[XmlAttribute]`、`Maid.EyeMoveType` の名前。null = 未記録）
- Consumes: `TimelineData.eyeMoveType` の setter（`UpdateHeadLook` を呼ぶ）、`TryParseEnum<T>(string, out T)`（`ScenePresetManager` 既存）

- [ ] **Step 1: 往復テストと旧形式テストを追加する**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs` のクラス末尾に追加する:

```csharp
        [Fact]
        public void メイド目線が往復で保たれる()
        {
            var restored = RoundTrip(new ScenePresetLook
            {
                mode = "カメラ",
                eyeMoveType = "目と顔を向ける",
            });

            Assert.Equal("目と顔を向ける", restored.eyeMoveType);
        }

        [Fact]
        public void メイド目線を持たない旧プリセットは追従トグルだけを読める()
        {
            // v32 以前は headToCam / eyeToCam を持ち、eyeMoveType は無い
            var xml = "<?xml version=\"1.0\" encoding=\"utf-16\"?>"
                + "<ScenePresetLook mode=\"カメラ\" headToCam=\"true\" eyeToCam=\"false\">"
                + "<lookX>0</lookX><lookY>0</lookY>"
                + "</ScenePresetLook>";

            using (var reader = new StringReader(xml))
            {
                var look = (ScenePresetLook) Serializer.Deserialize(reader);
                Assert.Null(look.eyeMoveType);
                Assert.True(look.headToCam);
                Assert.False(look.eyeToCam);
            }
        }
```

既存テスト `旧プリセットはタイムライン視線が未記録として読める` の XML 形式（:79 付近）を参考に、要素名・属性名を揃える。

- [ ] **Step 2: テストが失敗することを確認する**

Run:
```
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetLookXmlTests
```
Expected: ビルドエラー（`eyeMoveType` が無い）

- [ ] **Step 3: `ScenePresetLook` にフィールドを足し、バージョンを上げる**

`source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` の `ScenePresetLook` で、`headToCam` / `eyeToCam` の summary を次に書き換え、その直後に `eyeMoveType` を追加する:

```csharp
        /// <summary>
        /// TBody.boHeadToCam (顔を向ける)。v32 以前のプリセットの読み込み用。
        /// v33 からは eyeMoveType が追従を決めるため保存しない。
        /// v14 以前や、保存時にボディを取得できなかったメイドでは headToCamSpecified が false になり、
        /// 適用時にトグルへ触らない
        /// </summary>
        [XmlAttribute]
        public bool headToCam;
        [XmlIgnore]
        public bool headToCamSpecified;

        /// <summary>TBody.boEyeToCam (目を向ける)。互換の扱いは headToCam と同じ</summary>
        [XmlAttribute]
        public bool eyeToCam;
        [XmlIgnore]
        public bool eyeToCamSpecified;

        /// <summary>
        /// メイド目線 (Maid.EyeMoveType の名前)。顔・瞳の追従とそらしをまとめて表す。
        /// タイムライン全体の設定だが、注視先の指定値と同じく look に持つ。
        /// v32 以前のプリセットやタイムライン未読込で保存した場合は null になり、
        /// 適用時はメイド目線へ触らず headToCam / eyeToCam を戻す
        /// </summary>
        [XmlAttribute]
        public string eyeMoveType;
```

バージョン注記（`:903` 付近）に追記し、`CurrentVersion` を 33 にする:

```csharp
        // v33: look に eyeMoveType（メイド目線）を追加し、headToCam / eyeToCam は新規保存では書かない。
        //      旧形式は eyeMoveType が null で読め、従来どおり headToCam / eyeToCam を TBody へ戻す
        public static readonly int CurrentVersion = 33;
```

- [ ] **Step 4: `CaptureLook` を書き換える**

`source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1223-1231` の「追従トグルは lookController ではなく TBody が持つ (v15)」ブロックを次に置き換える:

```csharp
            // 追従は TBody のフラグではなくメイド目線 (タイムライン設定) で表す (v33)。
            // タイムライン未読込なら未記録にし、適用時にメイド目線へ触らない
            var timeline = MTEP.TimelineManager.instance.timeline;
            if (timeline != null)
            {
                look.eyeMoveType = timeline.eyeMoveType.ToString();
            }
```

- [ ] **Step 5: `ApplyLook` の末尾を書き換える**

`source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1777-1798`（`ApplyTimelineLook(maid, look);` 以降）を次に置き換える:

```csharp
            // TBody に依存しないため、追従の復元より前に戻す
            // (未ロードのメイドでも指定値だけは欠落させない)
            ApplyTimelineLook(maid, look);

            // v33 以降はメイド目線が追従フラグを決める。setter が UpdateHeadLook を呼び、
            // boHeadToCam / boEyeToCam / boEyeSorashi をまとめて揃える。
            // タイムライン全体の設定なので、複数メイドの復元では最後の値が残る
            // (同じタイムラインから保存した値は全員同じなので実害はない)
            if (!string.IsNullOrEmpty(look.eyeMoveType))
            {
                var timeline = MTEP.TimelineManager.instance.timeline;
                Maid.EyeMoveType eyeMoveType;
                if (timeline == null)
                {
                    return;
                }
                // XML は外部入力のため、未知の名前は復元せず既定のままにする
                if (!TryParseEnum(look.eyeMoveType, out eyeMoveType))
                {
                    MTEUtils.LogWarning("メイド目線の設定が不明です: {0}", look.eyeMoveType);
                    return;
                }
                timeline.eyeMoveType = eyeMoveType;
                return;
            }

            // v32 以前のプリセット: 追従トグルを TBody へ直接戻す。
            // 割合 (HeadToCamPer) は触らず、ゲーム側のフェードに任せる。
            // ここはロード完了後に呼ばれるためボディは揃っている想定だが、
            // 他の TBody アクセスと同じ防御的ガードに揃えておく
            var body = maid.body0;
            if (body == null || !body.isLoadedBody)
            {
                return;
            }
            if (look.headToCamSpecified)
            {
                body.boHeadToCam = look.headToCam;
            }
            if (look.eyeToCamSpecified)
            {
                body.boEyeToCam = look.eyeToCam;
            }
        }
```

`ApplyTimelineLook` の summary（:1801-1808）にある「この指定値が向け先へ波及するかは、復元先のタイムラインの「視線をキー化」(useHeadKey) が決める。…」の 3 行を削除し、「指定値は MaidCache のセッター経由で即座に向け先へ波及する」に置き換える。

`TryParseEnum` が `Maid.EyeMoveType` でも使える汎用メソッド（`where T : struct`）であることを `grep -n 'TryParseEnum' source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` で確認する。

- [ ] **Step 6: ビルドとテストが通ることを確認する**

Run:
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: 両構成ビルド成功、テスト PASS

- [ ] **Step 7: コミット**

`commit` スキルでコミットする。メッセージ例: `feat(preset): 視線の追従をメイド目線で保存・復元する (v33)`

---

### Task 5: ドキュメントを現状に合わせる

**Files:**
- Modify: `docs/se-mte-state-duplication-survey.md:143-178`（「視線の統合後仕様(A-1 まとめ)」）
- Modify: `docs/superpowers/specs/timeline-editing-roadmap.md:95`

- [ ] **Step 1: 「視線の統合後仕様」を書き換える**

`docs/se-mte-state-duplication-survey.md` の「### 視線の統合後仕様(A-1 まとめ)」節を次に置き換える（後続の「A-1a の実装メモ」以降は経緯として残す）:

```markdown
### 視線の統合後仕様(A-1 まとめ)

A-1a〜c と 2026-09-07 の整理（「視線をキー化」「顔を向ける」「目を向ける」の撤去）後の現行仕様。経緯・実装差分は後続の各実装メモを参照。

**所有者と経路**

- 向け先(`body0.trsLookTarget`)の書き手は SE の `MaidLookController` の一本のみ。MTE 側 (`MaidCache`) は `Timeline/MaidLookBridge.cs` 経由でこのコントローラを常時駆動する
- `MaidCache` の `lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `lookDirection`(顔向きキー)は `TimelineXml` / `EyesTimelineLayer` が読み書きする**キーフレームの指定値**であって、ゲーム状態ではない
- 顔・瞳の追従フラグ(`boHeadToCam` / `boEyeToCam` / `boEyeSorashi` / `HeadToCamFadeSpeed`)の書き手は `MaidLookBridge.ApplyEyeMoveType`(メイド目線)のみ。個別トグルは無い
- **瞳回転という概念は撤去した**(A-1 追補)。方向の指定は顔向き一本

**UI**

- 入口は表情ウィンドウの視線タブ 1 箇所。行は「メイド目線 → 向け先 → (対象) → 顔向き左右/上下 → タイムライン視線を初期化 → 瞳位置」
- 向け先の選択肢はカメラ / マウス / メイド / モデル / 方向指定(`MaidLookBridge.GetSelectableModes`)。SE 側だけにあった「オブジェクト」「無し」は UI から外した(列挙値とプリセット互換は残る)
- 「タイムラインが視線を動かさない」状態は「メイド目線: 無し」で表す。メイド目線はタイムライン XML の `EyeMoveType` 要素へ保存する。旧「視線をキー化」(`useHeadKey`)は撤去済みで、XML の `UseHeadKey` 要素は読み飛ばす
- Pose スコープの Undo(`PoseSnapshot`)は追従フラグを記録せず、復元後にメイド目線で塗り直す
- タイムライン未読込のときは案内だけを出し、視線は編集できない

**注視先が無いときの向け先**

- 注視先「方向指定」(または対象が未解決)のときは**顔向きキー**(`MaidCache.lookDirection`)が注視点を駆動する
- ただし目線種別が「そらす」系なら「無し」へ倒す。`TBody` のそらし演出は `trsLookTarget == null` かつ `boLockHeadAndEye == false` が条件のため
- カメラ / マウスは対象の同定が要らないため、そらし中でもそのまま写す

**頭ボーンの回転キー**

- メイドアニメレイヤーの頭の回転キーは `MaidLookBridge.IsHeadToCam(timeline.eyeMoveType)` のとき隠す(視線が頭を駆動している間はキーが効かないため)

**プリセット(v33)**

- `ScenePresetLook` は SE 側の向け先一式に加え、キーフレーム指定値(`timelineTargetType` / `timelineTargetIndex` / `timelineMaidPointType` / `timelineLookX/Y`)と `eyeMoveType`(メイド目線)を保存する
- 新規保存では `headToCam` / `eyeToCam` を書かない。v32 以前のプリセット(`eyeMoveType` が null)の読み込みだけ従来どおり TBody へ戻す
```

- [ ] **Step 2: ロードマップの記述を直す**

`docs/superpowers/specs/timeline-editing-roadmap.md:95` の「顔/瞳の固定化（useHeadKey）、胸(左/右)の物理無効（useMuneKeyL/R）」を「胸(左/右)の物理無効（useMuneKeyL/R）。顔/瞳の固定化（useHeadKey）は 2026-09-07 に撤去し、メイド目線で表す」に変える。

- [ ] **Step 3: コミット**

`commit` スキルでコミットする。メッセージ例: `docs: 視線の統合後仕様をメイド目線への一本化に合わせて更新する`

---

## レビュー却下メモ

- `ApplyLook` のループで `timeline.eyeMoveType` を毎回設定すると `UpdateHeadLook` が多重に走る — 却下。setter は同値なら早期 return し、同じプリセットから復元する値は全メイド同じなので実質 1 回。最適化は不要
- `docs/superpowers/specs/timeline-editing-roadmap.md:122` の「顔/瞳の固定化 ON」 — 却下。日付入りの実機確認ログで、当時の事実として残す（他節も同じ方針）

## Self-Review

- **Spec coverage**: 「視線をキー化」撤去 → Task 2 / 3。「顔を向ける」「目を向ける」撤去 → Task 3。マウスのキー化 → Task 1。オブジェクト・無しの UI 撤去 → Task 3。頭回転キーの表示条件 → Task 2。プリセット v33 → Task 4。ドキュメント → Task 5。
- **Placeholder scan**: なし。
- **Type consistency**: `ResolveLookMode` は Task 1 で戻り値 `MaidLookMode`（非 null）に変更し、Task 1 Step 6 の `MaidCache` 側もそれに合わせている。`GetSelectableModes()` は Task 1 と Task 3 の `TimelineLookRowDrawer` で引数なし。`ScenePresetLook.eyeMoveType` は Task 4 で string。
