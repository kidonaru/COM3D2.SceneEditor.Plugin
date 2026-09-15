# キーフレーム詳細 Inspector 刷新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (このリポジトリでは subagent-driven-development は使用禁止)

**Goal:** Inspector のキーフレーム詳細を「選択中キーフレームを集約して 1 組のステップボタン行で一括編集する UI」から「選択キーフレームごとの折りたたみ可能なブロックを縦に並べ、各ブロック内で Transform を標準 Inspector と同じ横並び行、その他のパラメータをドラッグ可能な数値入力のフロー折り返しで個別編集する UI」へ作り替える。

**Architecture:** `KeyFrameInspector.cs` を全面書き換えする。折り返し位置の計算だけは GUI から切り離した純粋な静的クラス `KeyFrameFlowLayout` に抽出して単体テストする。Transform 行は既存の `GUIView.DrawVector3Row`(標準 Inspector と同じ実装)をそのまま使い、`GUIView` 側には手を入れない。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI)、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** 本計画にインラインで記載(ブレインストーミングで確定した設計。別途の spec ファイルは作らない)

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree 禁止。作業はメイン作業ディレクトリの `feature/timeline-window` ブランチ
- ビルド確認(COM3D25 構成):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- ビルド確認(COM3D2 構成。対象フレームワークが違うため必ず両方通す):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- `debug.bat` は内部でゲームフォルダへ DLL をコピーするため使わない。`deploy.bat` / `release.bat` / `deploy.ps1` は絶対に実行しない
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`(既存テストを退行させない)。テストは COM3D25 構成のビルド出力を参照するため、テスト実行前に必ず COM3D25 構成でビルドしておくこと
- 新規ファイルは `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ追加する(SDK スタイルではないので自動では拾われない)
- コミットフッター:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ThuwuFbZ9bS3vDPihyk5Fe
  ```

## 確定仕様(ユーザー回答済み)

1. 複数選択時は**個別編集のみ**。従来の集約(NaN 混在)一括編集行は廃止する
2. 表示上限は既存の `config.detailTransformCount`(既定 16)を流用。超過分は件数だけ表示して描画しない
3. 折りたたみは**キーフレームごとのブロックヘッダーのみ**。Transform / カスタム値のセクション単位では折りたたまない。開閉状態は config へ永続化せず、セッション中のみメモリ保持
4. ステップボタン(`<< < > >>`)は廃止。ドラッグラベル + 数値入力 + `R` リセットのみ
5. ブロックヘッダーは `▼/▶` + `ボーン名 (F<フレーム番号>)`、右端に `初期化` と `削除` ボタン。全体の「初期化」ボタンは廃止
6. スカラー値の横並びは固定目安幅で入るだけ並べて折り返す(等分しない)

## 実装判断(ユーザー確認事項ではなく計画側の決定)

- カスタム値のうち `CustomValueType.BoolValue`(min=0 / max=1 / step=1)は数値入力ではなく**トグル**で描く。0/1 しか取れない値をドラッグ入力にしても操作性が落ちるだけのため。`表示` トグルと見た目が揃う
- カスタム値・文字列値の**個別リセット(`R`)は残す**。旧実装の `DrawFloatSelect` にはリセットがあり、ヘッダーの「初期化」はブロック全体を戻してしまうので、1 項目だけ戻す操作を失わせない
- Transform 行はインデントせず、ブロック内容の左端をビュー左端に揃える。`GUIView.DrawVector3Row` は数値入力の幅を `viewRect.width` から算出しており、外側で `AddSpace` を消費すると右端をはみ出すため(標準 Inspector 自体もインデントしていない)

## データモデルの前提知識(全タスク共通)

- 選択状態は `MTEP.TimelineManager.instance.selectedBones` (`HashSet<MTEP.BoneData>`)
- `BoneData.name` は `transform.name`、`BoneData.frameNo` は `parentFrame.frameNo`、`BoneData.parentFrame` は `FrameData`
- `ITransformData` の主要メンバー(`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`):
  - 有無判定: `hasPosition` / `hasRotation` / `hasEulerAngles` / `hasScale` / `hasColor` / `hasVisible`
  - 値: `position` / `normalizedEulerAngles`(setter は `eulerAngles`)/ `scale` / `color` / `visible`
  - 初期値: `initialPosition` / `initialEulerAngles` / `initialScale` / `initialColor`
  - カスタム値: `GetCustomValueInfoMap()` → `Dictionary<string, CustomValueInfo>`、`GetCustomValueInfo(key)`、`GetCustomValue(key)` → `ValueData`(`.value` が float)、`GetCustomValueName(key)`、`HasCustomValue(key)`、`GetDefaultCustomValue(key)`
  - 文字列値: `GetStrValueInfoMap()`、`GetStrValueName(key)`、`GetStrValue(key)`、`SetStrValue(key, value)`、`HasStrValue(key)`
  - `Reset()` で初期値へ戻す
- `CustomValueInfo` は `name` / `min` / `max` / `step` / `defaultValue` と、そこから導かれる `type` (`CustomValueType.BoolValue` / `IntValue` / `FloatSlider` / `FloatValue`) を持つ
- 編集反映は `currentLayer.ApplyCurrentFrame(true)`
- 削除は `bone.parentFrame.RemoveBone(bone)` → `currentLayer.CleanFrames()` → `currentLayer.ApplyCurrentFrame(true)` → `selectedBones.Remove(bone)` → `timelineManager.RequestHistory("キーフレーム削除")`(`TimelineManager.RemoveSelectedFrame` と同じ手順)
- `Color.ToVector3()` / `Vector3.ToColor()` 拡張は `COM3D2.MotionTimelineEditor` 名前空間にある(既存コードが使用中)

## GUIView API の前提知識(全タスク共通)

- `void BeginHorizontal()` / `void EndLayout()` — 横並びレイアウトの開始・終了
- `Rect GetDrawRect(float width, float height)` — 位置送りせずに次の描画矩形を計算する
- `Rect viewRect` / `Vector2 padding` / `float margin` — 利用可能幅は `viewRect.width - padding.x * 2`。`margin` は要素ごとに `NextElement` が加算する
- `void DrawLabel(string text, float width, float height, Color? textColor = null, GUIStyle style = null, Action onClickAction = null)`
- `bool DrawButton(string text, float width, float height, bool enabled = true, Color? color = null, GUIStyle style = null)`
- `bool DrawToggle(string label, bool value, float width, float height, Action<bool> onChanged)`
- `bool DrawTextField(string label, float labelWidth, string text, float width, float height, Action<string> onChanged = null, bool hasNewLine = false, string controlName = null)`
- `void DrawVector3Row(Vector3RowOption option)` — `label` / `labelWidth` / `height` / `dragSensitivity` / `fieldType` / `value` / `onChanged` / `onReset`。数値入力の幅は `viewRect.width` から自動算出される
- `bool DrawDragFloatField(DragFloatFieldOption option)` — `label` / `labelWidth` / `value` / `minValue` / `maxValue` / `fieldWidth` / `height` / `dragSensitivity` / `fieldType` / `onChanged` / `onReset`。`minValue` と `maxValue` がともに 0 なら制限なし
- `bool DrawDragIntField(DragIntFieldOption option)` — float 版と同じ構成の int 版
- `void AddSpace(float size)` / `void DrawHorizontalLine(Color color)`
- `FloatFieldCache` は**ラベルではなく描画順のインデックス**で割り当てられる(`GetFieldCache`)。同じラベルが複数ブロックに現れても衝突しないが、**毎フレーム同じ順序で同じ個数を描くこと**が前提。折りたたみで描画数が変わるのはフレームをまたぐ変化なので問題ない

---

## Task 1: フロー折り返しレイアウトの計算を純粋関数として切り出す

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(`<Compile Include="KeyFrameFlowLayout.cs" />` を `KeyFrameInspector.cs` の行の直前へ追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs`

**Interfaces:**
- Consumes: なし
- Produces: `public static class KeyFrameFlowLayout` に `public static int GetColumnCount(float availableWidth, float itemWidth, float margin)`

計算の定義: 1 列目は `itemWidth`、2 列目以降は `margin + itemWidth` を消費する。`availableWidth` に収まる最大の列数を返す。1 列も入らない場合でも最低 1 を返す(潰れた表示になるより 1 列で横スクロールさせないほうがよい)。

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs` を新規作成:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class KeyFrameFlowLayoutTests
    {
        [Fact]
        public void 列数_ちょうど3列入る幅で3を返す()
        {
            // 120 + (4 + 120) * 2 = 368
            Assert.Equal(3, KeyFrameFlowLayout.GetColumnCount(368f, 120f, 4f));
        }

        [Fact]
        public void 列数_4列目に1px足りなければ3を返す()
        {
            // 4 列には 368 + 4 + 120 = 492 必要
            Assert.Equal(3, KeyFrameFlowLayout.GetColumnCount(491f, 120f, 4f));
        }

        [Fact]
        public void 列数_1列ぶんに満たない幅でも1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(10f, 120f, 4f));
        }

        [Fact]
        public void 列数_幅が0以下でも1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(0f, 120f, 4f));
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(-50f, 120f, 4f));
        }

        [Fact]
        public void 列数_要素幅が0以下なら1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(500f, 0f, 4f));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter KeyFrameFlowLayoutTests
```

期待: コンパイルエラー(`KeyFrameFlowLayout` が存在しない)

- [ ] **Step 3: 最小実装を書く**

`source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs`:

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細のパラメータをウィンドウ幅に合わせて折り返すためのレイアウト計算。
    /// GUI に依存しない純粋計算なので単体テストできる
    /// </summary>
    public static class KeyFrameFlowLayout
    {
        /// <summary>
        /// 指定幅に何列並べられるかを返す。
        /// 1 列目は itemWidth、2 列目以降は margin + itemWidth を消費する。
        /// 1 列も入らない場合でも 1 を返す (幅を切り詰めてでも 1 列は出す)
        /// </summary>
        public static int GetColumnCount(float availableWidth, float itemWidth, float margin)
        {
            if (itemWidth <= 0f || availableWidth < itemWidth)
            {
                return 1;
            }

            var count = 1 + Mathf.FloorToInt((availableWidth - itemWidth) / (itemWidth + margin));
            return Mathf.Max(1, count);
        }
    }
}
```

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="KeyFrameInspector.cs" />` の直前に次の行を追加:

```xml
    <Compile Include="KeyFrameFlowLayout.cs" />
```

- [ ] **Step 4: ビルドしてテストが通ることを確認する**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter KeyFrameFlowLayoutTests
```

期待: ビルド成功、5 件 PASS

- [ ] **Step 5: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs
git commit
```

コミットメッセージ:

```
feat(timeline): キーフレーム詳細のフロー折り返し計算を追加する

ウィンドウ幅に合わせてパラメータを横並び・折り返し表示するための
列数計算を GUI から切り離した純粋関数として実装する。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ThuwuFbZ9bS3vDPihyk5Fe
```

---

## Task 2: KeyFrameInspector をキーフレームごとのブロック表示へ作り替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`(全面書き換え)

**Interfaces:**
- Consumes: `KeyFrameFlowLayout.GetColumnCount(float, float, float)`(Task 1)
- Produces: `KeyFrameInspector.instance`(既存シングルトン)、`static bool ShouldDraw()`、`void Draw(GUIView view)` — いずれも `InspectorWindow.DrawContent` からの呼び出し規約は変えない

**呼び出し元の確認(変更不要)**: `InspectorWindow.cs` の `KeyFrameInspector.ShouldDraw()` / `KeyFrameInspector.instance.Draw(_view)` はシグネチャが変わらないので手を入れない。

- [ ] **Step 1: `KeyFrameInspector.cs` を新しい実装で置き換える**

ファイル全体を次の内容にする:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択中キーフレームの詳細表示・編集。
    /// 選択キーフレームごとに折りたたみ可能なブロックを縦に並べ、
    /// Transform は標準 Inspector と同じ横並び行、その他のパラメータは
    /// ウィンドウ幅に合わせて折り返すドラッグ可能な数値入力で個別に編集する
    /// (補間曲線の編集は TimelineCurveEditor が担当する)
    /// </summary>
    public class KeyFrameInspector
    {
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private const float RowHeight = 20f;
        /// <summary>Transform 行のラベル幅 (InspectorWindow と揃える)</summary>
        private const float TransformLabelWidth = 50f;
        /// <summary>ブロックヘッダーの開閉マーク幅</summary>
        private const float FoldMarkWidth = 16f;
        /// <summary>ブロックヘッダー右端のボタン幅</summary>
        private const float HeaderButtonWidth = 44f;
        /// <summary>フロー要素 1 個ぶんの目安幅 (ラベル + 数値入力)</summary>
        private const float FlowItemWidth = 130f;
        /// <summary>フロー要素内のラベル幅</summary>
        private const float FlowLabelWidth = 70f;
        /// <summary>ブロック内容の左インデント</summary>
        private const float BlockIndent = 8f;

        // 1px ドラッグあたりの増減量 (InspectorWindow と揃える)
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;
        private const float ColorSensitivity = 0.01f;

        /// <summary>
        /// 折りたたみ中のキーフレーム。既定は展開なので「畳んだもの」だけを覚える。
        /// Inspector 外 (タイムラインのショートカット等) からも削除されうるので、
        /// 毎フレーム選択中のものだけへ絞り込んで古い BoneData 参照を残さない
        /// (セッション中のみ有効。config へは永続化しない)
        /// </summary>
        private readonly HashSet<MTEP.BoneData> _collapsedBones = new HashSet<MTEP.BoneData>();

        /// <summary>描画順を安定させるための並べ替えバッファ (毎フレームの確保を避ける)</summary>
        private readonly List<MTEP.BoneData> _sortedBones = new List<MTEP.BoneData>();

        /// <summary>
        /// 削除は selectedBones を書き換えるため、描画ループ中には実行できない。
        /// 描画後にまとめて処理する
        /// </summary>
        private MTEP.BoneData _pendingDeleteBone = null;

        private static KeyFrameInspector _instance = null;
        public static KeyFrameInspector instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new KeyFrameInspector();
                }
                return _instance;
            }
        }

        private KeyFrameInspector()
        {
        }

        /// <summary>キーフレーム選択中で Inspector に表示すべき状態か</summary>
        public static bool ShouldDraw()
        {
            return timelineManager.timeline != null
                && currentLayer != null
                && timelineManager.HasSelected();
        }

        public void Draw(GUIView view)
        {
            if (!ShouldDraw())
            {
                _collapsedBones.Clear();
                view.DrawLabel("キーフレームが選択されていません", -1, RowHeight);
                return;
            }

            // Inspector 外の削除経路を通ると選択から外れた BoneData が残るため、ここで掃除する
            _collapsedBones.RemoveWhere(bone => !selectedBones.Contains(bone));

            var maxCount = Mathf.Max(1, config.detailTransformCount);
            var totalCount = selectedBones.Count;

            view.DrawLabel(
                string.Format("キーフレーム詳細 ({0}個選択中)", totalCount), -1, RowHeight);

            // 選択順は HashSet で不定なので、フレーム番号 → 名前で毎回同じ並びにする
            _sortedBones.Clear();
            _sortedBones.AddRange(selectedBones);
            _sortedBones.Sort(CompareBone);

            var drawCount = Mathf.Min(totalCount, maxCount);
            for (var i = 0; i < drawCount; i++)
            {
                DrawBoneBlock(view, _sortedBones[i]);
            }

            if (totalCount > drawCount)
            {
                view.DrawLabel(
                    string.Format("他 {0} 個は非表示", totalCount - drawCount), -1, RowHeight);
            }

            _sortedBones.Clear();

            ProcessPendingDelete();
        }

        private static int CompareBone(MTEP.BoneData a, MTEP.BoneData b)
        {
            var result = a.frameNo.CompareTo(b.frameNo);
            if (result != 0)
            {
                return result;
            }
            return string.CompareOrdinal(a.name, b.name);
        }

        private void DrawBoneBlock(GUIView view, MTEP.BoneData bone)
        {
            view.DrawHorizontalLine(Color.gray);

            var expanded = !_collapsedBones.Contains(bone);
            DrawBlockHeader(view, bone, expanded);

            if (!expanded)
            {
                return;
            }

            var transform = bone.transform;
            DrawTransform(view, bone, transform);
            DrawFlowValues(view, bone, transform);
        }

        /// <summary>開閉マーク + ボーン名(フレーム番号) + 初期化 / 削除ボタンの 1 行</summary>
        private void DrawBlockHeader(GUIView view, MTEP.BoneData bone, bool expanded)
        {
            var available = view.viewRect.width - view.padding.x * 2;
            // 要素は 4 個 (マーク・名前・初期化・削除) なので margin を 4 個ぶん引く
            var labelWidth = available
                - FoldMarkWidth - HeaderButtonWidth * 2 - view.margin * 4;
            labelWidth = Mathf.Max(labelWidth, 40f);

            view.BeginHorizontal();
            {
                Action toggle = () => ToggleCollapsed(bone);

                view.DrawLabel(expanded ? "▼" : "▶", FoldMarkWidth, RowHeight,
                    onClickAction: toggle);
                view.DrawLabel(
                    string.Format("{0} (F{1})", bone.name, bone.frameNo),
                    labelWidth, RowHeight, onClickAction: toggle);

                if (view.DrawButton("初期化", HeaderButtonWidth, RowHeight))
                {
                    bone.transform.Reset();
                    MTEUtils.LogDebug("キーフレームを初期化します：" + bone.name);
                    currentLayer.ApplyCurrentFrame(true);
                }

                if (view.DrawButton("削除", HeaderButtonWidth, RowHeight))
                {
                    _pendingDeleteBone = bone;
                }
            }
            view.EndLayout();
        }

        private void ToggleCollapsed(MTEP.BoneData bone)
        {
            if (!_collapsedBones.Remove(bone))
            {
                _collapsedBones.Add(bone);
            }
        }

        /// <summary>削除は selectedBones を変更するため、描画ループを抜けてから実行する</summary>
        private void ProcessPendingDelete()
        {
            var bone = _pendingDeleteBone;
            if (bone == null)
            {
                return;
            }
            _pendingDeleteBone = null;

            var frame = bone.parentFrame;
            if (frame != null)
            {
                frame.RemoveBone(bone);
            }

            selectedBones.Remove(bone);
            _collapsedBones.Remove(bone);

            currentLayer.CleanFrames();
            currentLayer.ApplyCurrentFrame(true);

            MTEUtils.LogDebug("キーフレームを削除します：" + bone.name);
            timelineManager.RequestHistory("キーフレーム削除");
        }

        /// <summary>位置 / 回転 / 拡縮 / 色 を標準 Inspector と同じ横並び行で描く</summary>
        private void DrawTransform(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            if (transform.hasPosition)
            {
                DrawVector3Row(view, bone, "位置", PositionSensitivity,
                    transform.position,
                    value => transform.position = value,
                    () => transform.position = transform.initialPosition);
            }

            if (transform.hasRotation || transform.hasEulerAngles)
            {
                DrawVector3Row(view, bone, "回転", RotationSensitivity,
                    transform.normalizedEulerAngles,
                    value => transform.eulerAngles = value,
                    () => transform.eulerAngles = transform.initialEulerAngles);
            }

            if (transform.hasScale)
            {
                DrawVector3Row(view, bone, "拡縮", ScaleSensitivity,
                    transform.scale,
                    value => transform.scale = value,
                    () => transform.scale = transform.initialScale);
            }

            if (transform.hasColor)
            {
                DrawVector3Row(view, bone, "色", ColorSensitivity,
                    transform.color.ToVector3(),
                    value => transform.color = value.ToColor(),
                    () => transform.color = transform.initialColor);
            }
        }

        private void DrawVector3Row(
            GUIView view,
            MTEP.BoneData bone,
            string label,
            float sensitivity,
            Vector3 value,
            Action<Vector3> setValue,
            Action resetValue)
        {
            // GUIView.DrawVector3Row は数値入力の幅を viewRect.width から算出するため、
            // 外側を BeginHorizontal + AddSpace で包むと右端をはみ出す。インデントしないこと
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = TransformLabelWidth,
                height = RowHeight,
                dragSensitivity = sensitivity,
                value = value,
                onChanged = newValue =>
                {
                    setValue(newValue);
                    Apply(bone);
                },
                onReset = () =>
                {
                    resetValue();
                    Apply(bone);
                },
            });
        }

        /// <summary>
        /// カスタム値・文字列値・表示トグルを、ウィンドウ幅に入るだけ横に並べて折り返す。
        /// 種類をまたいで詰めると型ごとの見分けがつかなくなるため、
        /// 「カスタム値 → 表示トグル」までを 1 つの流れとし、文字列値は 1 行ずつ別に描く
        /// </summary>
        private void DrawFlowValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            var items = new List<Action>();

            foreach (var pair in transform.GetCustomValueInfoMap())
            {
                var customKey = pair.Key;
                var info = pair.Value;
                if (!transform.HasCustomValue(customKey))
                {
                    continue;
                }

                items.Add(() => DrawCustomValueItem(view, bone, transform, customKey, info));
            }

            if (transform.hasVisible)
            {
                items.Add(() => DrawVisibleItem(view, bone, transform));
            }

            DrawFlow(view, items);

            DrawStrValues(view, bone, transform);
        }

        /// <summary>要素を折り返しながら並べる。1 行ぶんずつ BeginHorizontal で囲む</summary>
        private void DrawFlow(GUIView view, List<Action> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            var available = view.viewRect.width - view.padding.x * 2
                - BlockIndent - view.margin;
            var columnCount = KeyFrameFlowLayout.GetColumnCount(
                available, FlowItemWidth, view.margin);

            for (var i = 0; i < items.Count; i += columnCount)
            {
                view.BeginHorizontal();
                {
                    view.AddSpace(BlockIndent);

                    var end = Mathf.Min(i + columnCount, items.Count);
                    for (var j = i; j < end; j++)
                    {
                        items[j]();
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>
        /// カスタム値 1 個。bool 相当はトグル、整数相当は int 入力、
        /// それ以外はドラッグ可能な float 入力にする
        /// </summary>
        private void DrawCustomValueItem(
            GUIView view,
            MTEP.BoneData bone,
            MTEP.ITransformData transform,
            string customKey,
            MTEP.CustomValueInfo info)
        {
            var name = transform.GetCustomValueName(customKey);
            var valueData = transform.GetCustomValue(customKey);
            // リセットボタン (20px) はドラッグ入力側が fieldWidth から内側に取る
            var fieldWidth = FlowItemWidth - FlowLabelWidth - view.margin;

            Action reset = () =>
            {
                transform.GetCustomValue(customKey).value = transform.GetDefaultCustomValue(customKey);
                Apply(bone);
            };

            if (info.type == MTEP.CustomValueType.BoolValue)
            {
                view.DrawToggle(name, valueData.value != 0f, FlowItemWidth, RowHeight,
                    newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue ? 1f : 0f;
                        Apply(bone);
                    });
                return;
            }

            if (info.type == MTEP.CustomValueType.IntValue)
            {
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = name,
                    labelWidth = FlowLabelWidth,
                    value = Mathf.RoundToInt(valueData.value),
                    minValue = Mathf.RoundToInt(info.min),
                    maxValue = Mathf.RoundToInt(info.max),
                    fieldWidth = fieldWidth,
                    height = RowHeight,
                    // dragSensitivity は既定 (DefaultIntDragSensitivity = 0.5) に任せる。
                    // 1.0 にすると 1px で 1 段変わって細かい調整ができない
                    onChanged = newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue;
                        Apply(bone);
                    },
                    onReset = reset,
                });
                return;
            }

            view.DrawDragFloatField(new GUIView.DragFloatFieldOption
            {
                label = name,
                labelWidth = FlowLabelWidth,
                value = valueData.value,
                minValue = info.min,
                maxValue = info.max,
                fieldWidth = fieldWidth,
                height = RowHeight,
                dragSensitivity = info.step > 0f ? info.step : GUIView.DefaultFloatDragSensitivity,
                onChanged = newValue =>
                {
                    transform.GetCustomValue(customKey).value = newValue;
                    Apply(bone);
                },
                onReset = reset,
            });
        }

        private void DrawVisibleItem(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            view.DrawToggle("表示", transform.visible, FlowItemWidth, RowHeight,
                newValue =>
                {
                    transform.visible = newValue;
                    Apply(bone);
                });
        }

        /// <summary>文字列値は幅が読めないので 1 行ずつ全幅で描く</summary>
        private void DrawStrValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetStrValueInfoMap())
            {
                var strKey = pair.Key;
                if (!transform.HasStrValue(strKey))
                {
                    continue;
                }

                var value = transform.GetStrValue(strKey);

                view.BeginHorizontal();
                {
                    view.AddSpace(BlockIndent);
                    view.DrawTextField(
                        transform.GetStrValueName(strKey),
                        FlowLabelWidth,
                        value,
                        -1,
                        RowHeight,
                        newValue =>
                        {
                            if (newValue == value)
                            {
                                return;
                            }
                            transform.SetStrValue(strKey, newValue);
                            Apply(bone);
                        });
                }
                view.EndLayout();
            }
        }

        /// <summary>編集を即時反映する</summary>
        private void Apply(MTEP.BoneData bone)
        {
            MTEUtils.LogDebug("キーフレームを更新します：" + bone.name);
            currentLayer.ApplyCurrentFrame(true);
        }
    }
}
```

- [ ] **Step 2: COM3D25 構成でビルドし、コンパイルを通す**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

期待: ビルド成功。失敗した場合に確認すべき点(推測で書き換えず必ず定義を読むこと):
- `GUIView.DragIntFieldOption` の `dragSensitivity` の型(`float` か `int` か)。`MTEUtils/GUIView.cs` の `DragIntFieldOption` 定義を確認して合わせる
- `MTEP.CustomValueType` / `MTEP.CustomValueInfo` の実際の名前空間。`Timeline/TransformData/ITransformData.cs` の `namespace` 宣言に合わせる
- `ToVector3()` / `ToColor()` 拡張の名前空間。既存の `using COM3D2.MotionTimelineEditor;` で足りるはず
- `GUIView.DefaultFloatDragSensitivity` の可視性(`public static readonly`)

- [ ] **Step 3: COM3D2 構成でもビルドを通す**

```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

期待: ビルド成功(対象フレームワークが違うため COM3D25 だけでは検証にならない)

- [ ] **Step 4: 既存テストが退行していないことを確認する**

```
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

期待: 全件 PASS(Task 1 で追加した 5 件を含む)

- [ ] **Step 5: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs
git commit
```

コミットメッセージ:

```
feat(timeline): キーフレーム詳細を個別ブロック編集へ作り替える

選択キーフレームを集約して一括編集する方式をやめ、キーフレームごとに
折りたたみ可能なブロックを縦へ並べて個別に編集できるようにする。
Transform は標準 Inspector と同じ横並び行にし、カスタム値などは
ウィンドウ幅に合わせて折り返すドラッグ可能な数値入力へ変更する。
ステップボタンは廃止し、ブロックごとの初期化・削除ボタンを追加する。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ThuwuFbZ9bS3vDPihyk5Fe
```

---

## Task 3: 実機で表示・操作を確認して調整する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`(必要に応じた幅・感度の調整のみ)

**Interfaces:**
- Consumes: Task 2 の `KeyFrameInspector`
- Produces: なし

**前提**: ゲーム(COM3D2.5)が起動しており、MCP `com3d25-devbridge` が応答すること。ゲームが起動していない場合は、この Task をユーザーへ引き渡して「実機確認をお願いします」と伝え、勝手に `debug.bat` を実行しないこと。

- [ ] **Step 1: ホットリロードでビルド済み DLL を反映する**

`W:\COM3D2_5\work\CLAUDE.md` の「Harmony プラグインのホットリロード」手順に従う。ホットリロードが難しい構成であれば、この Step は飛ばしてユーザーにゲーム再起動を依頼する。

- [ ] **Step 2: 単一選択で表示を確認する**

タイムライン上でキーフレームを 1 個選択し、`mcp__com3d25-devbridge__screenshot` で Inspector を撮る。

確認項目:
- ヘッダーが `▼ <ボーン名> (F<番号>)` + `初期化` + `削除` になっている
- 位置 / 回転 / 拡縮 / 色 が `ラベル + X[入力] Y[入力] Z[入力] + R` の 1 行になっている
- カスタム値がラベル + 数値入力の形で横に並び、ウィンドウ右端で折り返している
- ステップボタン(`<< < > >>`)がどこにも残っていない

- [ ] **Step 3: 複数選択で表示を確認する**

キーフレームを 3 個以上選択してスクリーンショットを撮る。

確認項目:
- 選択数だけブロックが縦に並び、フレーム番号順に並んでいる
- 各ブロックのヘッダークリックで個別に開閉できる
- 17 個以上選択したとき、16 個だけ描画され末尾に `他 N 個は非表示` が出る

- [ ] **Step 4: 編集操作を確認する**

- X ラベルを左右ドラッグして値が増減し、シーンへ即時反映されること
- 数値入力へ直接打ち込んで反映されること
- Transform 行の数値入力と `R` ボタンがウィンドウ右端をはみ出していないこと(インデントを外した理由がこれ)
- `R` で該当行(Vector3 行全体)が初期値へ戻ること
- カスタム値の `R` でその値だけが既定値へ戻り、同じブロックの他の値が変わらないこと
- ヘッダーの `初期化` でそのブロックだけが初期値へ戻り、他のブロックが変わらないこと
- ヘッダーの `削除` でそのキーフレームだけが消え、他の選択が残ること。削除後に Undo(履歴)で戻せること
- ウィンドウ幅を狭める / 広げると、フロー要素の折り返し位置が追従すること

- [ ] **Step 5: 調整が入った場合のみコミットする**

幅・感度などを調整した場合:

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs
git commit
```

コミットメッセージ(調整内容に合わせて 1 行目を書き換える):

```
fix(timeline): キーフレーム詳細の表示幅を調整する

実機確認の結果に合わせてラベル幅とフロー要素幅を見直す。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ThuwuFbZ9bS3vDPihyk5Fe
```

調整が不要だった場合はコミットせず、Step 2〜4 の確認結果を報告して終了する。

---

## 完了後

実装完了後、ユーザーへ提示する前に **code-review スキル**でレビューする(`W:\COM3D2_5\work\CLAUDE.md` の標準フロー)。

## レビュー却下メモ

- bool 型カスタム値をトグル表示にするのは依頼原文「ドラッグ可能なテキストエリア」と字義的にズレる可能性があるので事前確認すべき（確信度: 低） — 却下。0/1 しか取れない値をドラッグ入力にすると操作性が落ちるだけで、依頼の意図（ステップボタンをやめて直接編集できるようにする）は満たしている。「実装判断」セクションに明記したうえで実機確認で見てもらう
