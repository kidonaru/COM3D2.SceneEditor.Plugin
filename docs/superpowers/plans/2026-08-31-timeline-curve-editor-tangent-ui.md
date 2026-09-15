# タイムラインカーブエディタの補間曲線 UI 統一 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインウィンドウ下部のカーブエディタ (`TimelineCurveEditor`) のサイドツールバーを、Inspector 補間曲線タブ (`KeyFrameTangentDrawer`) と同じ操作感にする。表示値選択コンボをカスタム値まで含む共有実装へ置き換え、プリセットを曲線サムネ化し、Out/In をドラッグラベル付きの行にする。あわせて分かりにくい「回転進行度」チャンネルを撤去する。

**Architecture:** 共通部分を 2 つの新規クラスへ切り出して Inspector と共有する。`TangentTargetList` は「選択キーフレームが実際に持つ編集対象 (軸ごとの値種別 + カスタム値) の候補リストと選択状態」を持ち、`TangentCurveTexture` は「正規化タンジェント曲線と基準線のテクスチャ描画 + プリセットサムネ生成」を持つ。`KeyFrameTangentDrawer` は自前実装を捨ててこの 2 クラスへ委譲し、`TimelineCurveEditor` は `CurveValueFilter` enum を捨てて `TangentTargetList` を使う。カーブ描画側は「回転進行度」チャンネル (`ChannelKind.RotationProgress` とその評価・ドラッグ経路) を削除し、クォータニオン回転は表示専用の Euler 3 本のみにする。

**Tech Stack:** C# (net35/net48, 旧形式 csproj), Unity IMGUI (`GUIView`), Texture2D への CPU ピクセル描画, xUnit (net48)

**Spec:** 正式な spec なし。ブレスト結果 (本セッション) が仕様。ユーザー決定事項:
1. カスタム値は**個別候補としてコンボに追加**する (選ぶとそのカスタム値だけ表示・編集対象)
2. **「回転進行度」はオミット** (コンボ候補・チャンネル・評価経路ごと削除)
3. プリセットは**曲線サムネに合わせる** (テキストボタンは廃止)
4. Out/In は **Inspector 流** (ラベル左右ドラッグで差分編集 + 数値欄で絶対値編集)
5. プリセット・Out/In の編集範囲は**現状維持＝選択キー自身の Out/In 両ハンドル** (Inspector の「区間 = 前キーの Out + 選択キーの In」とは意味が違うまま)

## Global Constraints

- `deploy.bat` / `deploy.ps1` / `debug.bat` / `release.bat` は実行禁止。ビルドは MSBuild 直叩きのみ
- 両構成をビルドすること: `/p:GameVersion=COM3D25` と `/p:GameVersion=COM3D2` (対象フレームワークが異なるため片方だけでは不十分)
- コードのコメント・ログメッセージは日本語
- 新規 .cs は csproj (旧形式) へ `<Compile Include>` を手動追加
- 純粋ロジックの追加はないため新規テストは書かない。既存テスト (`KeyFrameTangentLogicTests` 等) が通り続けることを各タスクで確認する
- `KeyFrameTangentLogic.ValueToTextureY` は既存テストが直接参照しているため移動しない (新クラスから呼ぶ)
- ドラッグ中に毎フレーム走る編集の履歴は `MTEP.TimelineHistoryManager.instance.AddHistory` ではなく `timelineManager.RequestHistory(説明)` を使う (Inspector と同じ作法)
- 単発クリック起点の編集 (プリセット・自動補間トグル) の履歴経路は現状維持。`TimelineCurveEditor` は `AddHistory` 直呼び、Inspector は `RequestHistory` 経由という非対称が残るが、どちらも正しく 1 件積まれるため本計画では揃えない (スコープ外)

ビルドコマンド (以後「両構成ビルド」と呼ぶ。作業ディレクトリは `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`):

```powershell
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

テスト実行:

```powershell
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

---

### Task 1: TangentCurveTexture の切り出し (Inspector を委譲)

タンジェント曲線テクスチャの描画を `KeyFrameTangentDrawer` から独立クラスへ移す。見た目は一切変えない純粋な抽出。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TangentCurveTexture.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `KeyFrameTangentLogic.ValueToTextureY(float value, int size, int padding)`、`MTEP.PluginUtils.HermiteSimplified(float outTangent, float inTangent, float t)`、`MTEP.TangentPair.GetDefault(MTEP.TangentType)`、`TextureUtils.ClearTexture(Texture2D, Color)`
- Produces:
  - `public static void TangentCurveTexture.DrawCurve(Texture2D texture, float outTangent, float inTangent, Color lineColor, int lineWidth, int padding)`
  - `public static void TangentCurveTexture.DrawGuideLines(Texture2D texture, int padding)`
  - `public static Texture2D[] TangentCurveTexture.CreatePresetTextures(int size, Color bgColor, Color lineColor)` — `TangentType.Smooth` を除く 4 種 (EaseInOut / EaseIn / EaseOut / Linear) のサムネを `Apply()` 済みで返す

- [ ] **Step 1: 新規クラスを作る**

`source/COM3D2.SceneEditor.Plugin/TangentCurveTexture.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 正規化タンジェント曲線 (t は 0..1) をテクスチャへ CPU 描画するヘルパー。
    /// Inspector の補間曲線 (KeyFrameTangentDrawer) と
    /// タイムラインカーブエディタのプリセットサムネ (TimelineCurveEditor) で共有する
    /// </summary>
    public static class TangentCurveTexture
    {
        /// <summary>基準線 (値 0/1 と区間の始点/終点で囲む枠) の色</summary>
        private static readonly Color GuideFrameColor = new Color(1f, 1f, 1f, 0.35f);
        /// <summary>線形補間 (勾配 1) を示す対角線の色</summary>
        private static readonly Color GuideLinearColor = new Color(1f, 1f, 1f, 0.25f);
        /// <summary>対角線の破線パターン (この px 数ごとに描画と空白を切り替える)</summary>
        private const int GuideDashLength = 4;

        /// <summary>プリセットサムネの線幅 (40px 程度では 1px だと細くて形が読めない)</summary>
        private const int PresetLineWidth = 3;
        /// <summary>プリセットサムネの内側余白 (小さいので控えめに)</summary>
        private const int PresetPadding = 3;

        /// <summary>
        /// 曲線を読む基準線を描く。内側領域の枠 (下辺=値 0 / 上辺=値 1 /
        /// 左辺=区間の始点 / 右辺=終点) と、線形補間を示す対角線 (破線)。
        /// 曲線より先に描いて背面に置く。
        /// GPU への転送をまとめるため Apply は呼び出し側で行う
        /// </summary>
        public static void DrawGuideLines(Texture2D texture, int padding)
        {
            var size = texture.width;
            // 曲線と同じ写像で端を求め、枠と曲線がずれないようにする
            var min = KeyFrameTangentLogic.ValueToTextureY(0f, size, padding);
            var max = KeyFrameTangentLogic.ValueToTextureY(1f, size, padding);

            // 対角線は内側領域が正方形であることを使い、x と y に同じ値を使う。
            // 曲線と紛れないよう GuideDashLength px ごとに描画と空白を切り替える。
            // 枠より先に描いて、重なる角は枠の色を残す
            for (var i = min; i <= max; i++)
            {
                var dashSegment = (i - min) / GuideDashLength;
                if (dashSegment % 2 == 0)
                {
                    texture.SetPixel(i, i, GuideLinearColor);
                }
            }

            for (var i = min; i <= max; i++)
            {
                texture.SetPixel(i, min, GuideFrameColor);
                texture.SetPixel(i, max, GuideFrameColor);
                texture.SetPixel(min, i, GuideFrameColor);
                texture.SetPixel(max, i, GuideFrameColor);
            }
        }

        /// <summary>
        /// 正規化タンジェント形状 (t は 0..1) の Hermite 曲線をテクスチャへ描く
        /// (MTE KeyFrameUI.UpdateTangentTexture の移植)。
        /// padding px は四辺の余白として空け、内側領域だけに描く。
        /// 複数の曲線を重ねられるよう Apply は呼び出し側で行う
        /// </summary>
        public static void DrawCurve(
            Texture2D texture,
            float outTangent,
            float inTangent,
            Color lineColor,
            int lineWidth,
            int padding)
        {
            var size = texture.width;
            var halfLineWidth = lineWidth / 2;
            var innerWidth = size - padding * 2;
            // 最終列で t=1 に到達させ、曲線の終端を in ハンドルの原点へ合わせる
            var lastX = Mathf.Max(1, innerWidth - 1);

            for (var x = 0; x < innerWidth; x++)
            {
                var t = x / (float)lastX;
                var value = MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t);
                var y = KeyFrameTangentLogic.ValueToTextureY(value, size, padding) - halfLineWidth;

                for (var i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, padding, size - 1 - padding);
                    texture.SetPixel(padding + x, yy, lineColor);
                }
            }
        }

        /// <summary>
        /// プリセットサムネを作る。添字は TangentType の値と一致する
        /// (自動補間 (Smooth) は固定形状を持たないため除く)
        /// </summary>
        public static Texture2D[] CreatePresetTextures(int size, Color bgColor, Color lineColor)
        {
            var textures = new Texture2D[(int)MTEP.TangentType.Smooth];
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = new Texture2D(size, size);
                textures[i] = texture;
                TextureUtils.ClearTexture(texture, bgColor);
                var pair = MTEP.TangentPair.GetDefault((MTEP.TangentType)i);
                DrawCurve(
                    texture, pair.outTangent, pair.inTangent, lineColor,
                    lineWidth: PresetLineWidth, padding: PresetPadding);
                texture.Apply();
            }
            return textures;
        }
    }
}
```

- [ ] **Step 2: KeyFrameTangentDrawer から重複コードを消して委譲する**

`KeyFrameTangentDrawer.cs` を次のとおり編集する。

1. 定数の削除 (L29, L33, L52-57): `PresetLineWidth` / `PresetPadding` / `GuideFrameColor` / `GuideLinearColor` / `GuideDashLength` を削除する。`CurvePadding` (L31) はハンドル座標計算でも使うので残す。
2. メソッドの削除: ファイル末尾の `private static void DrawGuideLines(...)` と `private static void UpdateTangentTexture(...)` を丸ごと削除する。
3. `EnsureTextures` のプリセット生成部分を差し替える:

```csharp
        private void EnsureTextures()
        {
            if (_tangentTex != null)
            {
                return;
            }

            _tangentTex = new Texture2D(CurveTexSize, CurveTexSize);
            _presetTextures = TangentCurveTexture.CreatePresetTextures(
                PresetTexSize, config.curveBgColor, config.curveLineColor);
        }
```

4. `UpdateCurveTextureIfNeeded` 内の呼び出しを差し替える:

```csharp
            TextureUtils.ClearTexture(_tangentTex, config.curveBgColor);
            TangentCurveTexture.DrawGuideLines(_tangentTex, CurvePadding);
            foreach (var tangent in _workTangents)
            {
                var color = tangent.isSmooth ? config.curveLineSmoothColor : config.curveLineColor;
                TangentCurveTexture.DrawCurve(
                    _tangentTex, tangent.outTangent, tangent.inTangent, color,
                    lineWidth: 1, padding: CurvePadding);
            }
```

- [ ] **Step 3: csproj へ追加**

`<Compile Include="KeyFrameTangentDrawer.cs" />` の行の直後に追加:

```xml
    <Compile Include="TangentCurveTexture.cs" />
```

- [ ] **Step 4: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ともビルド成功、全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TangentCurveTexture.cs source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(timeline): タンジェント曲線のテクスチャ描画を共有クラスへ切り出す"
```

---

### Task 2: TangentTargetList の切り出し (Inspector を委譲)

編集対象コンボの候補構築・選択状態・タンジェント取り出しを `KeyFrameTangentDrawer` から独立クラスへ移す。ここでも挙動は変えない。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TangentTargetList.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `MTEP.BoneData.transform`、`MTEP.ITransformData.hasTangent / GetValueDataList / GetCustomValueInfoMap / GetCustomValueName / HasCustomValue / GetCustomValue`、`MTEP.TangentValueType`
- Produces:
  - `public struct TangentTarget { public string name; public MTEP.TangentValueType valueType; public string customKey; public bool isCustom { get; } public string id { get; } }`
  - `public class TangentTargetList`
    - `public HashSet<MTEP.TangentValueType> excludedValueTypes` (候補から外す軸種別。既定は空)
    - `public List<TangentTarget> items { get; }`
    - `public TangentTarget current { get; }`
    - `public int currentIndex { get; }`
    - `public void Select(TangentTarget target)`
    - `public void Update(IEnumerable<MTEP.BoneData> bones)`
    - `public static MTEP.TangentData[] GetTangents(MTEP.ITransformData transform, TangentTarget target, bool isOut)`

- [ ] **Step 1: 新規クラスを作る**

`source/COM3D2.SceneEditor.Plugin/TangentTargetList.cs`:

```csharp
using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タンジェント編集の対象。軸ごとの値種別 (TangentValueType) と、
    /// 軸に属さないカスタム値 (ポストエフェクトの焦点距離など) を同じ土俵で扱う
    /// </summary>
    public struct TangentTarget
    {
        public string name;
        public MTEP.TangentValueType valueType;
        /// <summary>カスタム値のキー。null なら軸ごとの値種別</summary>
        public string customKey;

        public bool isCustom => customKey != null;

        /// <summary>
        /// 選択状態を覚えるための識別子。
        /// 候補は毎フレーム作り直すので添字では覚えられない。
        /// 軸 (axis) とカスタム値でキー空間が衝突しないよう接頭辞で分ける
        /// </summary>
        public string id => isCustom
            ? TangentTargetList.CustomIdPrefix + customKey
            : TangentTargetList.AxisIdPrefix + valueType;
    }

    /// <summary>
    /// 選択キーフレームが実際に持つ編集対象だけを並べたコンボ候補と、その選択状態。
    /// Inspector の補間曲線 (KeyFrameTangentDrawer) と
    /// タイムラインカーブエディタ (TimelineCurveEditor) で共有する。
    /// 先頭には必ず「すべて」が入るので、添字 0 は常に有効
    /// </summary>
    public class TangentTargetList
    {
        public const string AxisIdPrefix = "a:";
        public const string CustomIdPrefix = "c:";

        private static readonly MTEP.TangentData[] EmptyTangents = new MTEP.TangentData[0];

        /// <summary>候補から外す軸種別 (タイムライン側は W回転 をカーブに出さないため除く)</summary>
        public readonly HashSet<MTEP.TangentValueType> excludedValueTypes
            = new HashSet<MTEP.TangentValueType>();

        private readonly List<TangentTarget> _targets = new List<TangentTarget>();
        /// <summary>候補へ入れ終えたカスタム値キー (重複判定用。毎フレームの確保を避ける)</summary>
        private readonly HashSet<string> _addedCustomKeys = new HashSet<string>();

        /// <summary>選択中の編集対象の識別子</summary>
        private string _targetId = AxisIdPrefix + MTEP.TangentValueType.すべて;

        public TangentTargetList()
        {
            _targets.Add(CreateAxisTarget(MTEP.TangentValueType.すべて));
        }

        public List<TangentTarget> items => _targets;

        /// <summary>選択中の編集対象。候補から消えていたら先頭 (すべて) を返す</summary>
        public TangentTarget current
        {
            get
            {
                var index = IndexOf(_targetId);
                return index >= 0 ? _targets[index] : _targets[0];
            }
        }

        /// <summary>コンボへ渡す添字。候補から消えていたら 0 (すべて)</summary>
        public int currentIndex => Math.Max(0, IndexOf(_targetId));

        public void Select(TangentTarget target)
        {
            _targetId = target.id;
        }

        /// <summary>
        /// 候補を、渡されたボーンが実際に持つ対象だけに絞り直す。
        /// 「すべて」は常に候補に残し、選択中の対象が消えたらそこへ戻す
        /// </summary>
        public void Update(IEnumerable<MTEP.BoneData> bones)
        {
            _targets.Clear();
            _targets.Add(CreateAxisTarget(MTEP.TangentValueType.すべて));

            foreach (MTEP.TangentValueType valueType in
                Enum.GetValues(typeof(MTEP.TangentValueType)))
            {
                if (valueType == MTEP.TangentValueType.すべて
                    || excludedValueTypes.Contains(valueType))
                {
                    continue;
                }
                if (HasValueType(bones, valueType))
                {
                    _targets.Add(CreateAxisTarget(valueType));
                }
            }

            AddCustomValueTargets(bones);

            if (IndexOf(_targetId) < 0)
            {
                _targetId = _targets[0].id;
            }
        }

        /// <summary>編集対象に対応するタンジェントを取り出す。対象を持たない transform では空</summary>
        public static MTEP.TangentData[] GetTangents(
            MTEP.ITransformData transform, TangentTarget target, bool isOut)
        {
            if (target.isCustom)
            {
                if (!transform.HasCustomValue(target.customKey))
                {
                    return EmptyTangents;
                }
                var value = transform.GetCustomValue(target.customKey);
                return new[] { isOut ? value.outTangent : value.inTangent };
            }

            return isOut
                ? transform.GetOutTangentDataList(target.valueType)
                : transform.GetInTangentDataList(target.valueType);
        }

        /// <summary>軸ごとの値種別を表す候補を作る</summary>
        private static TangentTarget CreateAxisTarget(MTEP.TangentValueType valueType)
        {
            return new TangentTarget
            {
                name = valueType.ToString(),
                valueType = valueType,
            };
        }

        /// <summary>
        /// カスタム値 (ポストエフェクトの焦点距離など) を候補へ足す。
        /// 選択キーフレームで種類が違うこともあるので、いずれかが持つキーをすべて並べる
        /// </summary>
        private void AddCustomValueTargets(IEnumerable<MTEP.BoneData> bones)
        {
            _addedCustomKeys.Clear();

            foreach (var bone in bones)
            {
                var transform = bone.transform;
                if (transform == null || !transform.hasTangent)
                {
                    continue;
                }

                foreach (var pair in transform.GetCustomValueInfoMap())
                {
                    var customKey = pair.Key;
                    if (!_addedCustomKeys.Add(customKey))
                    {
                        continue;
                    }

                    _targets.Add(new TangentTarget
                    {
                        name = transform.GetCustomValueName(customKey),
                        customKey = customKey,
                    });
                }
            }
        }

        /// <summary>いずれかのボーンが指定種別の値を持つか</summary>
        private static bool HasValueType(
            IEnumerable<MTEP.BoneData> bones, MTEP.TangentValueType valueType)
        {
            foreach (var bone in bones)
            {
                var transform = bone.transform;
                if (transform != null && transform.GetValueDataList(valueType).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private int IndexOf(string targetId)
        {
            return _targets.FindIndex(target => target.id == targetId);
        }
    }
}
```

- [ ] **Step 2: KeyFrameTangentDrawer を委譲へ書き換える**

`KeyFrameTangentDrawer.cs` を次のとおり編集する。

0. 削除するメソッドに付いている XML コメントは新クラス側へ移し終えているので、**そのまま消す**こと (`UpdateAvailableTargets` のコメントは `TimelineCurveEditor.UpdateValueTypeFilter` を名指ししているが、そのメソッドは Task 4 で消えるため引き継がない)。
1. 削除するメンバー: `private struct TangentTarget` とその周辺 (`AxisIdPrefix` / `CustomIdPrefix` / `CreateAxisTarget` / `EmptyTangents` / `_targetId` / `_availableTargets` / `_addedCustomKeys`)、および `UpdateAvailableTargets` / `AddCustomValueTargets` / `HasValueType` / `FindTargetIndex` / `currentTarget` / `GetTangents` の各メソッド。
2. 代わりにフィールドを 1 つ持つ:

```csharp
        /// <summary>編集対象コンボの候補と選択状態 (TimelineCurveEditor と共有の実装)</summary>
        private readonly TangentTargetList _targets = new TangentTargetList();
```

3. コンボの型引数を差し替える:

```csharp
        // 候補は選択内容で変わるため、items は毎フレーム差し替える
        private readonly GUIComboBox<TangentTarget> _targetComboBox =
            new GUIComboBox<TangentTarget>
            {
                getName = (target, index) => target.name,
                buttonSize = new Vector2(100, 20),
            };
```

(型名は同じ `TangentTarget` だが、名前空間直下の共有 struct を参照するようになる)

4. `Draw` 先頭の `UpdateAvailableTargets();` を `_targets.Update(selectedBones);` に差し替える。
5. `DrawTangentFields` の冒頭を差し替える:

```csharp
            _targetComboBox.items = _targets.items;
            _targetComboBox.currentIndex = _targets.currentIndex;
            _targetComboBox.onSelected = (target, index) =>
            {
                _targets.Select(target);
            };
            _targetComboBox.DrawButton(subView);
```

6. `CollectTangents` / `ForEachOutTangent` / `ForEachInTangent` 内の `GetTangents(transform, target, isOut: ...)` 呼び出しを `TangentTargetList.GetTangents(transform, target, isOut: ...)` に、`currentTarget` を `_targets.current` に置き換える。

- [ ] **Step 3: csproj へ追加**

`<Compile Include="TangentCurveTexture.cs" />` の行の直後に追加:

```xml
    <Compile Include="TangentTargetList.cs" />
```

- [ ] **Step 4: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ともビルド成功、全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TangentTargetList.cs source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(timeline): タンジェント編集対象の候補リストを共有クラスへ切り出す"
```

---

### Task 3: 回転進行度チャンネルの撤去

`TimelineCurveEditor` から「回転進行度」を消す。クォータニオン格納ボーンの回転は表示専用の Euler 3 本だけになり、カーブ上のハンドルドラッグでは編集できなくなる (サイドツールバーの Out/In・プリセット・自動補間ではこれまでどおり編集できる)。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Produces: `CurveValueFilter` から `回転進行度` が消える (Task 4 で enum ごと消えるが、このタスクでは残す)。`CurveChannel.kind` は `Normal` と `EulerDisplay` の 2 値になる。

- [ ] **Step 1: 進行度チャンネルの生成をやめ、Euler 表示の元データ用に組み替える**

`BuildRotationProgressChannel` は Euler 表示チャンネル (`BuildEulerDisplayChannel`) の元データとしても使われているため、削除ではなく「表示しない元データ生成」へ改名・降格する。

- `BuildRotationProgressChannel` を `BuildQuaternionSourceChannel` へ改名し、`kind` を `ChannelKind.EulerDisplay`、`displayName` / `color` の指定を削除する。XML コメントを次に差し替える:

```csharp
        /// <summary>クォータニオン格納ボーンの回転から Euler 表示チャンネルの元データを作る。
        /// このチャンネル自体は表示せず、BuildEulerDisplayChannel の入力にのみ使う。
        /// キーが 1 つも拾えなければ null</summary>
```

- `AddQuaternionRotationChannels` を次のとおり書き換える:

```csharp
        /// <summary>クォータニオン格納ボーンの回転チャンネルを追加する。
        /// 回転/すべて → 表示用 Euler 3 本、X/Y/Z回転 → 該当軸の Euler 表示 1 本。
        /// Euler 表示は導出値のため表示専用 (値・タンジェントとも編集不可)</summary>
        private void AddQuaternionRotationChannels(
            List<CurveChannel> channels, ref int totalChannelCount,
            string boneName, List<int> frameNos, List<MTEP.BoneData> bones)
        {
            var addAllEulers = _valueTypeFilter == CurveValueFilter.すべて
                || _valueTypeFilter == CurveValueFilter.回転;
            var isAxisFilter = IsAxisRotationFilter(_valueTypeFilter);
            if (!addAllEulers && !isAxisFilter)
            {
                return;
            }

            var sourceChannel = BuildQuaternionSourceChannel(boneName, frameNos, bones);
            if (sourceChannel == null)
            {
                return;
            }

            totalChannelCount += (addAllEulers ? 3 : 0) + (isAxisFilter ? 1 : 0);

            if (addAllEulers)
            {
                for (var axis = 0; axis < 3 && channels.Count < MAX_CHANNELS; axis++)
                {
                    channels.Add(BuildEulerDisplayChannel(sourceChannel, axis));
                }
            }
            if (isAxisFilter && channels.Count < MAX_CHANNELS)
            {
                channels.Add(BuildEulerDisplayChannel(
                    sourceChannel, GetAxisIndex(_valueTypeFilter)));
            }
        }
```

- [ ] **Step 2: 進行度専用の型・評価・ドラッグ経路を削除する**

`TimelineCurveEditor.cs` から次を削除する:

1. `enum ChannelKind` の `RotationProgress` 項目とその XML コメント
2. `CurveChannel.isRotationProgress` プロパティ
3. `CurveChannel.GetKeyValue` から `isRotationProgress` の三項だけを取り除く。**`isEulerDisplay` 分岐は残すこと** (消すと Euler 表示チャンネルのキー点が代表クォータニオン成分の生値で描かれ、カーブ線とずれる。`EvaluateChannel` のキー範囲外の値も壊れる):

```csharp
            /// <summary>キー位置の表示値。Euler 表示では連続化済みの角度 (度)</summary>
            public float GetKeyValue(int i)
            {
                return isEulerDisplay ? eulerKeyValues[i] : values[i].value;
            }
```

4. `ColorRotationProgress` フィールド
5. `_dragTangents` フィールドと、`HandleInput` / `UpdateDrag` / `EndDrag` 内のその参照。具体的には:
   - `HandleInput` のタンジェント掴み処理から `_dragTangents = channel.isRotationProgress ? ... : null;` の 5 行を削除
   - 値ドラッグのスキップ条件を `if (channel.isEulerDisplay)` に変更 (コメントも「Euler 表示は導出値のため値ドラッグ不可 (表示値→成分の逆変換ができない)」へ)
   - `UpdateDrag` の重複判定コメントから「(回転進行度では 4 成分が常に一括更新され同値になるため)」を削除し、適用部分を次に置き換える:

```csharp
            _dragTangent.normalizedValue = normalized;
            _dragTangent.isSmooth = false;
```

   - `EndDrag` の `_dragTangents = null;` を削除
6. `GetHandlePos` の `channel.isRotationProgress` 分岐を削除し、通常経路だけにする:

```csharp
            var tangent = isOut
                ? channel.values[keyIndex].outTangent
                : channel.values[keyIndex].inTangent;

            // TangentData.value は値/秒なのでフレームあたり勾配へ換算してから画面勾配にする
            var slopePerFrame = tangent.value * timeline.frameDuration;
```

   あわせて引数 `baseSlopePerFrame` が未使用になるため引数から外し、呼び出し元 2 箇所 (`HandleInput` のヒットテストと `DrawChannelHandles`) を修正する。`TryGetBaseSlopePerFrame` はタンジェントドラッグの正規化基準として引き続き必要なので残す。
7. `GetAverageNormalizedTangent` メソッド
8. `EvaluateRotationProgress` / `EvaluateSegmentProgress` メソッドと、`EvaluateChannel` 内の `if (channel.isRotationProgress) { return EvaluateRotationProgress(channel, frameNo); }` 分岐
9. `HasValueType` の先頭にある `回転進行度` 分岐と `HasQuaternionRotation` メソッド (他に呼び出し元がないことを `grep -n "HasQuaternionRotation" TimelineCurveEditor.cs` で確認してから削除する)
10. `CurveValueFilter` の `回転進行度` 項目、`ToTangentValueType` / `ExpandValueTypes` の該当 case、`ForEachTangent` の進行度スキップ (`if (_valueTypeFilter == CurveValueFilter.回転進行度 && !transform.hasRotation) continue;`)、および L88-90 / L109-113 の enum・候補コメントから進行度に関する記述

- [ ] **Step 3: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ともビルド成功、全テスト PASS。未使用フィールド警告が出ないこと

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs
git commit -m "refactor(timeline): カーブエディタから回転進行度チャンネルを削除する"
```

---

### Task 4: 表示値フィルタを TangentTargetList へ置き換える

`CurveValueFilter` enum を捨て、Inspector と同じ候補 (軸ごとの値種別 + カスタム値 1 個ずつ) にする。カスタム値を選ぶとそのカーブだけを表示し、ツールバーの編集もそのカスタム値だけに効く。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Consumes: `TangentTargetList` / `TangentTarget` (Task 2)
- Produces: `_valueTypeFilter` (CurveValueFilter) が `_targets` (TangentTargetList) に置き換わる。Task 5 は `_targets.current` を前提にする

- [ ] **Step 1: フィルタの型を差し替える**

削除するもの: `enum CurveValueFilter`、`_valueTypeFilter`、`_availableValueTypes`、`FilterCandidates`、`ToTangentValueType`、`IsAxisRotationFilter`、`UpdateValueTypeFilter`、`HasValueType`、`GetAxisIndex(CurveValueFilter)`。

追加するフィールド (`_valueTypeComboBox` の直前):

```csharp
        /// <summary>表示・編集の対象 (軸ごとの値種別 + カスタム値)。Inspector と同じ実装を共有する。
        /// W回転 はカーブに出さないため候補から外す (クォータニオン回転は Euler 表示に置き換わる)</summary>
        private readonly TangentTargetList _targets = new TangentTargetList
        {
            excludedValueTypes = { MTEP.TangentValueType.W回転 },
        };
```

コンボの型引数を差し替える:

```csharp
        private readonly GUIComboBox<TangentTarget> _valueTypeComboBox
            = new GUIComboBox<TangentTarget>
        {
            getName = (target, index) => target.name,
            buttonSize = new Vector2(60, 20),
            showArrow = true,
        };
```

軸インデックス取得を `TangentValueType` ベースにする (`IsAxisRotationType` は既存のものを使う):

```csharp
        /// <summary>軸別回転種別の軸インデックス (X=0, Y=1, Z=2)。
        /// X/Y/Z回転 以外を渡さないこと (それ以外は Z 扱いになる)</summary>
        private static int GetAxisIndex(MTEP.TangentValueType valueType)
        {
            if (valueType == MTEP.TangentValueType.X回転) return 0;
            if (valueType == MTEP.TangentValueType.Y回転) return 1;
            return 2;
        }
```

`ExpandValueTypes` を `TangentValueType` 受け取りにする:

```csharp
        /// <summary>複合型の値種別を単チャンネル型へ展開する</summary>
        private static List<MTEP.TangentValueType> ExpandValueTypes(MTEP.TangentValueType valueType)
        {
            switch (valueType)
            {
                case MTEP.TangentValueType.すべて:
                    return MoveChannelTypes
                        .Concat(RotationChannelTypes)
                        .Concat(ScaleChannelTypes)
                        .ToList();
                case MTEP.TangentValueType.移動:
                    return MoveChannelTypes.ToList();
                case MTEP.TangentValueType.回転:
                    return RotationChannelTypes.ToList();
                case MTEP.TangentValueType.拡縮:
                    return ScaleChannelTypes.ToList();
                default:
                    return new List<MTEP.TangentValueType> { valueType };
            }
        }
```

- [ ] **Step 2: 候補更新の呼び出し元を差し替える**

`UpdateValueTypeFilter()` の呼び出し箇所 (`grep -n "UpdateValueTypeFilter" *.cs` で TimelineWindow 側も含めて洗い出す) を `_targets.Update(selectedBones);` に置き換える。

- [ ] **Step 3: チャンネル収集をカスタム値対応にする**

`CollectChannels` を次のとおり書き換える (ボーンごとのループ内)。

冒頭の展開を差し替える:

```csharp
            var target = _targets.current;
            var valueTypes = target.isCustom
                ? new List<MTEP.TangentValueType>()
                : ExpandValueTypes(target.valueType);
```

クォータニオン回転チャンネルの追加条件をカスタム値では通らないようにする:

```csharp
                var isQuaternionRotation = firstTransform.hasRotation;
                if (isQuaternionRotation && !target.isCustom)
                {
                    AddQuaternionRotationChannels(
                        channels, ref totalChannelCount, boneName, frameNos, bones);
                }
```

カスタム値チャンネルの収集条件を「すべて または 当該カスタム値が選択中」に変える。`// カスタム値チャンネル (TangentValueType に含まれないためキー名で収集する)` のコメント行から、その `foreach` ブロックの閉じ括弧までを**まるごと**次に差し替える (`totalChannelCount` の二重カウント / 未カウントを避けるため、ループ本体全体を示す):

```csharp
                // カスタム値チャンネル (TangentValueType に含まれないためキー名で収集する)。
                // 「すべて」では全部、カスタム値を名指しで選んでいるときはそれ 1 本だけ出す
                if (!target.isCustom && target.valueType != MTEP.TangentValueType.すべて)
                {
                    continue;
                }

                var customIndex = 0;
                foreach (var customValue in firstTransform.GetCustomValueInfoMap())
                {
                    var customKey = customValue.Key;
                    // 色の割り当ては「すべて」表示時と揃えたいので、
                    // 名指しで絞る場合も customIndex は全キーぶん先に進める
                    customIndex++;
                    if (target.isCustom && customKey != target.customKey)
                    {
                        continue;
                    }

                    totalChannelCount++;
                    if (channels.Count >= MAX_CHANNELS)
                    {
                        continue;
                    }

                    var channel = new CurveChannel
                    {
                        boneName = boneName,
                        customKey = customKey,
                        displayName = customValue.Value.name,
                        color = CustomChannelColors[(customIndex - 1) % CustomChannelColors.Length],
                    };
                    for (var k = 0; k < bones.Count; k++)
                    {
                        var transform = bones[k].transform;
                        if (!transform.HasCustomValue(customKey))
                        {
                            continue;
                        }
                        channel.frameNos.Add(frameNos[k]);
                        channel.values.Add(transform.GetCustomValue(customKey));
                        channel.keyBones.Add(bones[k]);
                    }
                    if (channel.values.Count > 0)
                    {
                        channels.Add(channel);
                    }
                }
```

`AddQuaternionRotationChannels` 内の `_valueTypeFilter` 参照を `_targets.current` ベースへ書き換える (`ForEachTangent` は Step 4 で差し替える。`HandleInput` は `_valueTypeFilter` を参照していないので触らない):

```csharp
            var target = _targets.current;
            var addAllEulers = target.valueType == MTEP.TangentValueType.すべて
                || target.valueType == MTEP.TangentValueType.回転;
            var isAxisFilter = IsAxisRotationType(target.valueType);
```

(`AddQuaternionRotationChannels` 末尾の `GetAxisIndex(_valueTypeFilter)` も `GetAxisIndex(target.valueType)` にする)

- [ ] **Step 4: 編集対象の走査をカスタム値対応にする**

`ForEachTangent` を次に差し替える:

```csharp
        /// <summary>選択キー自身の out/in Tangent を走査する
        /// (前キー側ではなく、選択している頂点の両ハンドルが対象)</summary>
        private void ForEachTangent(System.Action<MTEP.TangentData, bool> callback)
        {
            var target = _targets.current;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform == null)
                {
                    continue;
                }

                foreach (var tangent in TangentTargetList.GetTangents(transform, target, isOut: true))
                {
                    callback(tangent, true);
                }

                foreach (var tangent in TangentTargetList.GetTangents(transform, target, isOut: false))
                {
                    callback(tangent, false);
                }
            }
        }
```

- [ ] **Step 5: コンボと空表示メッセージを差し替える**

`DrawSideToolbar` のコンボ設定部分:

```csharp
            _valueTypeComboBox.items = _targets.items;
            // buttonSize は矢印を含まないため、矢印の分を引いて 1 行に収める
            _valueTypeComboBox.buttonSize = new Vector2(
                Mathf.Max(20f, width - COMBO_ARROW_WIDTH), TOOL_ROW_HEIGHT);
            _valueTypeComboBox.currentIndex = _targets.currentIndex;
            _valueTypeComboBox.onSelected = (target, index) => _targets.Select(target);
            _valueTypeComboBox.DrawButton(view);
```

`DrawPane` の空表示メッセージ:

```csharp
                    : string.Format("選択中のボーンに「{0}」の値がありません", _targets.current.name);
```

- [ ] **Step 6: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ともビルド成功、全テスト PASS

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs
git commit -m "feat(timeline): カーブエディタの表示値選択でカスタム値を個別に選べるようにする"
```

---

### Task 5: プリセットサムネと Out/In 行を Inspector 流にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs`

**Interfaces:**
- Consumes: `TangentCurveTexture.CreatePresetTextures` (Task 1)、`_targets` (Task 4)、`GUIView.DrawDragLabel(string text, float width, float height, float sensitivity, Action<float> onDelta)`、`GUIView.DrawTexture(Texture2D, float width, float height, Color, EventType, Action<Vector2>)`

- [ ] **Step 1: プリセットのテキストボタン実装を削除する**

削除するもの: `TangentPresets` 配列、`MakePreset`、`_gsPresetButton`、`PRESET_FONT_SIZE_MAX` / `PRESET_FONT_SIZE_MIN`、`GetPresetButtonStyle`。

追加する定数 (`LEGEND_PADDING` の下あたり):

```csharp
        /// <summary>プリセットサムネの生成解像度 (表示は幅・高さに合わせて縮小する)</summary>
        private const int PRESET_TEX_SIZE = 40;
        /// <summary>プリセットサムネの表示サイズの上下限 (px)</summary>
        private const float PRESET_DRAW_SIZE_MAX = 40f;
        private const float PRESET_DRAW_SIZE_MIN = 20f;
        /// <summary>タンジェント行のラベル幅 ("Out" が収まる幅)</summary>
        private const float TANGENT_LABEL_WIDTH = 30f;
        /// <summary>ラベルドラッグ 1px あたりのタンジェント増減量 (Inspector と同じ)</summary>
        private const float TANGENT_DRAG_SENSITIVITY = 0.01f;
```

追加するフィールド:

```csharp
        /// <summary>プリセットサムネ (添字は TangentType の値)。GUI.skin と同じく OnGUI 内で遅延生成する</summary>
        private Texture2D[] _presetTextures = null;
```

- [ ] **Step 2: ツールバーを組み直す**

`DrawSideToolbar` の、コンボ描画から末尾までを次に差し替える (コンボ設定は Task 4 のものをそのまま使う):

```csharp
            _valueTypeComboBox.DrawButton(view);
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            if (selectedBones.Count == 0)
            {
                return;
            }

            // 正規化タンジェント (線形勾配比、1=線形) の直接入力。
            // Inspector の補間曲線タブと同じく Out → In の順に並べる
            DrawTangentRow(view, x, ref y, width, "Out", true, _outTangentFieldCache);
            DrawTangentRow(view, x, ref y, width, "In", false, _inTangentFieldCache);

            var isSmooth = IsAllTangentSmooth();
            view.currentPos = new Vector2(x, y);
            view.DrawToggle("自動補間", isSmooth, width, TOOL_ROW_HEIGHT, newIsSmooth =>
            {
                ForEachTangent((tangent, isOut) => tangent.isSmooth = newIsSmooth);
                currentLayer.ApplyCurrentFrame(true);
                MTEP.TimelineHistoryManager.instance.AddHistory(timeline, "カーブ: 自動補間");
            });
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            DrawPresetThumbnails(view, x, ref y, width, toolbarRect);
        }

        /// <summary>
        /// タンジェント 1 行。ラベルを左右ドラッグすると差分編集、数値欄への入力で絶対値編集。
        /// 選択内で値が混在していると数値欄は空欄 (NaN) になるが、差分編集は効かせたいので
        /// 絶対値しか渡さない DrawDragFloatField ではなく DrawDragLabel を直接使う
        /// </summary>
        private void DrawTangentRow(
            GUIView view, float x, ref float y, float width,
            string label, bool isOut, FloatFieldCache fieldCache)
        {
            var current = GetUniformNormalizedTangent(isOut);
            // NaN は NaN と不一致扱いになり毎フレーム text が空に戻って入力中の文字
            // ("-" など未確定の文字列) を潰すため、双方 NaN のときは更新しない
            if (!(float.IsNaN(current) && float.IsNaN(fieldCache.value)))
            {
                fieldCache.UpdateValue(current);
            }

            var diff = 0f;

            view.currentPos = new Vector2(x, y);
            view.BeginHorizontal();
            {
                view.DrawDragLabel(
                    label, TANGENT_LABEL_WIDTH, TOOL_ROW_HEIGHT,
                    TANGENT_DRAG_SENSITIVITY, value => diff += value);

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    value = current,
                    fieldCache = fieldCache,
                    width = Mathf.Max(20f, width - TANGENT_LABEL_WIDTH),
                    height = TOOL_ROW_HEIGHT,
                    onChanged = value => ApplyNormalizedTangent(isOut, value),
                });
            }
            view.EndLayout();
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            // 各タンジェントへ個別に足すので、混在 (空欄) でも値の差を保ったまま相対変更できる
            if (diff != 0f)
            {
                AddNormalizedTangent(isOut, diff);
            }
        }

        /// <summary>プリセットの曲線サムネを 2 列で並べる。
        /// サムネ一辺は幅とツールバーの残り高さに収まるよう縮める</summary>
        private void DrawPresetThumbnails(
            GUIView view, float x, ref float y, float width, Rect toolbarRect)
        {
            if (_presetTextures == null)
            {
                _presetTextures = TangentCurveTexture.CreatePresetTextures(
                    PRESET_TEX_SIZE, config.curveBgColor, config.curveLineColor);
            }

            var remainHeight = toolbarRect.yMax - TOOL_PADDING_Y - y;
            var size = Mathf.Clamp(
                Mathf.Min(
                    (width - TOOL_ROW_SPACING) * 0.5f,
                    (remainHeight - TOOL_ROW_SPACING) * 0.5f),
                PRESET_DRAW_SIZE_MIN,
                PRESET_DRAW_SIZE_MAX);

            for (var i = 0; i < _presetTextures.Length; i++)
            {
                var tangentType = (MTEP.TangentType)i;
                var isRightColumn = (i % 2) == 1;

                view.currentPos = new Vector2(
                    x + (isRightColumn ? size + TOOL_ROW_SPACING : 0f), y);
                view.DrawTexture(
                    _presetTextures[i], size, size, Color.white, EventType.MouseDown,
                    _ => ApplyTangentPreset(tangentType));

                // 行の最後を描いたら改行する (プリセットが奇数個でも行送りが止まらないように)
                if (isRightColumn || i == _presetTextures.Length - 1)
                {
                    y += size + TOOL_ROW_SPACING;
                }
            }
        }
```

- [ ] **Step 3: 差分適用とプリセット適用を書き換える**

`ApplyNormalizedTangent` の直後へ差分版を追加する:

```csharp
        /// <summary>選択キーの片側タンジェントへ正規化値の差分を加算する</summary>
        private void AddNormalizedTangent(bool isOut, float diff)
        {
            ForEachTangent((tangent, tangentIsOut) =>
            {
                if (tangentIsOut != isOut)
                {
                    return;
                }
                tangent.normalizedValue += diff;
                tangent.isSmooth = false;
            });

            currentLayer.ApplyCurrentFrame(true);
            // ドラッグ中は毎フレーム呼ばれるため、履歴はマウスを離すまで集約させる
            timelineManager.RequestHistory("カーブ: タンジェント入力 " + (isOut ? "Out" : "In"));
        }
```

`ApplyTangentPreset` を `TangentType` 受け取りに変える:

```csharp
        private void ApplyTangentPreset(MTEP.TangentType tangentType)
        {
            var tangentPair = MTEP.TangentPair.GetDefault(tangentType);

            ForEachTangent((tangent, isOut) =>
            {
                tangent.normalizedValue = isOut ? tangentPair.outTangent : tangentPair.inTangent;
                tangent.isSmooth = tangentPair.isSmooth;
            });

            currentLayer.ApplyCurrentFrame(true);
            MTEP.TimelineHistoryManager.instance.AddHistory(
                timeline,
                "カーブ: プリセット " + MTEP.TangentData.TangentTypeNames[(int)tangentType]);
        }
```

不要になった `using System.Linq;` の参照が残っているかを確認し (`GetPresetButtonStyle` の削除で減る)、他で使っていれば残す。

- [ ] **Step 4: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 両構成ともビルド成功、全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs
git commit -m "feat(timeline): カーブエディタのプリセットと Out/In UI を Inspector に揃える"
```

---

### Task 6: 実機確認とレビュー

**Files:** なし (確認のみ)

- [ ] **Step 1: 実機確認 (ゲーム起動中のみ。MCP `com3d25-devbridge` の `ping` が通る場合)**

`screenshot` / `capture` でタイムラインウィンドウのカーブエディタを開いた状態を確認する:
- サイドツールバーが上から「編集対象コンボ → Out → In → 自動補間 → プリセットサムネ 2×2」の順で並び、`menuWidth` を最小 (100px) にしても見切れないこと
- コンボにカスタム値 (ポストエフェクトの焦点距離など) が個別に並び、選ぶとそのカーブだけが表示され、Out/In・プリセットがそのカスタム値にだけ効くこと
- コンボに「回転進行度」と「W回転」が出ないこと。クォータニオン回転のボーンでは Euler 表示 3 本が出て、キー点・ハンドルのドラッグができないこと
- Out/In ラベルの左右ドラッグでタンジェントが増減し、離したときに Undo 履歴が 1 件だけ積まれること
- プリセットサムネのクリックで曲線が変わり、履歴に「カーブ: プリセット …」が積まれること

ゲーム未起動で省略した場合は「レイアウト・操作未検証」であることを最終報告に明記し、ユーザーへ実機確認を依頼する。

- [ ] **Step 2: コードレビュー**

`code-review` スキルでレビューし、妥当な指摘を取り込む。修正が入ったら両構成ビルドとテストをやり直す。
