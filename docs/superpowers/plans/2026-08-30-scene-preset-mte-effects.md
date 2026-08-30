# MTE 由来演出のシーンプリセット保存 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE から移植した演出状態 (フリーテキスト / サブカメラ / ポストエフェクトのパラフィン・距離フォグ・リムライト) をシーンプリセット (v29) で保存・復元できるようにする。**タイムライン未読込のシーンでも復元できる**ことが要件。

**Architecture:** ①要素数 (textCount / paraffinCount 等) の所有をマネージャ側プロパティへ移し、タイムライン読込中は timeline 側と同期・未読込時は自前値で動く形にする (サブカメラの `_restoreCameraCount` 方式の一般化)。②値スナップショット (`ScenePresetEffects`) を `ScenePresetData` v29 に追加し、capture/apply は新設の `MteEffectsSnapshot` に集約する。③保存/読込 UI に「演出」カテゴリを追加する。適用規約は視線 (v26) と同じ「未記録 (null / 空) なら触らない」。

**Tech Stack:** C# (Unity IMGUI / XmlSerializer)、MTE 側マネージャ (MTEP.TimelineTextManager / MTEP.SubCameraManager / MTEP.PostEffectManager)、xUnit (SDK テストプロジェクト)

**Spec:** `docs/scene-preset-mte-state-survey.md` (調査資料 + 決定済み方針。§5 の障害一覧と §7 の方針の当たりを本計画が具体化する)

## Global Constraints

- **両構成ビルド必須**: COM3D2 (.NET 3.5) / COM3D25 (.NET 4.7.1) を必ず両方 MSBuild 直叩きで確認する。`debug.bat` は使わない (実機へ DLL をコピーするため)
- .NET 3.5 制約: 入力 5 個以上の `Func<>`/`Action<>` 禁止
- コメント・ログは日本語
- SceneEditor.Plugin 名前空間から MTE 側へのアクセスは `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` エイリアス経由
- 履歴 (HistoryAPI) は記録しない (プリセット適用は `HistoryManager.ClearHistory()` 済みの文脈)
- 適用規約: 旧プリセット / 未保存カテゴリは「触らない」(v11〜v28 と同じ流儀)
- UnityEngine.Rect を XmlSerializer で直列化しない (読み書きプロパティが多く出力が不安定なため 4 つの float で持つ)

## ビルド・テストコマンド (全タスク共通)

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

(テストは COM3D25 ビルドの DLL を参照するため、COM3D25 ビルド後に実行すること)

---

### Task 1: 要素数のタイムライン非依存化 + ウィンドウの編集ゲート緩和

テキスト表示数とポストエフェクト数の所有者をマネージャへ移し、`timeline == null` でも実体を生成・編集できるようにする。サブカメラは台数が元からマネージャ所有のため UI ゲートの緩和のみ。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/TextWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs` (DrawSubCameraContent :599-654)

**Interfaces:**
- Produces: `MTEP.TimelineTextManager.textCount { get; set; }` (int)、`MTEP.PostEffectManager.paraffinCount / distanceFogCount / rimlightCount { get; set; }` (int)。いずれも「timeline 読込中は timeline 側が正、未読込時は自前値」。Task 2 の `MteEffectsSnapshot` がこれらを消費する
- 不変条件: タイムライン読込中の挙動は従来と同一 (レイヤーの `timeline.textCount` 直読みは値が一致するため無変更で成立する)

- [ ] **Step 1: TimelineTextManager に textCount プロパティを追加し InitTexts を非依存化**

`_textData` フィールド定義の直後に追加:

```csharp
/// <summary>
/// タイムライン未読込時のテキスト表示数。
/// プリセット復元をタイムライン非依存にするための自前の所有者
/// </summary>
private int _standaloneTextCount = 1;

/// <summary>
/// テキスト表示数。タイムライン読込中は timeline 側が正 (TimelineXml に保存されるため)、
/// 未読込時は自前値で動く
/// </summary>
public int textCount
{
    get => timeline != null ? timeline.textCount : _standaloneTextCount;
    set
    {
        _standaloneTextCount = value;
        if (timeline != null)
        {
            timeline.textCount = value;
        }
    }
}
```

`InitTexts()` の先頭 (`if (timeline == null || _textData.Length == timeline.textCount)` の置き換え):

```csharp
public void InitTexts()
{
    if (timeline != null)
    {
        // タイムライン読込 (TimelineXml) で timeline 側だけ変わった場合に自前値を追随させる
        _standaloneTextCount = timeline.textCount;
    }

    if (_textData.Length == textCount)
    {
        return;
    }

    ReleaseTexts();

    _textData = new FreeTextSet[textCount];
```

(以降のループは `_textData.Length` 基準のため変更不要)

- [ ] **Step 2: PostEffectManager に 3 つの count プロパティを追加し InitPostEffects を非依存化**

`depthOfFieldMaidSlotId` フィールドの後に追加:

```csharp
// タイムライン未読込時の各エフェクト数。プリセット復元をタイムライン非依存にするための自前の所有者。
// 既定値は TimelineData の既定値 (各 1) と揃える
private int _standaloneParaffinCount = 1;
private int _standaloneDistanceFogCount = 1;
private int _standaloneRimlightCount = 1;

/// <summary>パラフィン数。タイムライン読込中は timeline 側が正、未読込時は自前値で動く</summary>
public int paraffinCount
{
    get => timeline != null ? timeline.paraffinCount : _standaloneParaffinCount;
    set
    {
        _standaloneParaffinCount = value;
        if (timeline != null)
        {
            timeline.paraffinCount = value;
        }
    }
}

/// <summary>距離フォグ数。タイムライン読込中は timeline 側が正、未読込時は自前値で動く</summary>
public int distanceFogCount
{
    get => timeline != null ? timeline.distanceFogCount : _standaloneDistanceFogCount;
    set
    {
        _standaloneDistanceFogCount = value;
        if (timeline != null)
        {
            timeline.distanceFogCount = value;
        }
    }
}

/// <summary>リムライト数。タイムライン読込中は timeline 側が正、未読込時は自前値で動く</summary>
public int rimlightCount
{
    get => timeline != null ? timeline.rimlightCount : _standaloneRimlightCount;
    set
    {
        _standaloneRimlightCount = value;
        if (timeline != null)
        {
            timeline.rimlightCount = value;
        }
    }
}
```

`InitPostEffects()` を書き換え (timeline 同期を追加):

```csharp
public void InitPostEffects()
{
    if (timeline != null)
    {
        // タイムライン読込 (TimelineXml) で timeline 側だけ変わった場合に自前値を追随させる
        _standaloneParaffinCount = timeline.paraffinCount;
        _standaloneDistanceFogCount = timeline.distanceFogCount;
        _standaloneRimlightCount = timeline.rimlightCount;
    }

    InitParrifinEffect();
    InitDistanceFogEffect();
    InitRimlightEffect();
}
```

`InitParrifinEffect` / `InitDistanceFogEffect` / `InitRimlightEffect` 内の `timeline.paraffinCount` / `timeline.distanceFogCount` / `timeline.rimlightCount` をそれぞれ `paraffinCount` / `distanceFogCount` / `rimlightCount` に置換する (各メソッド 2 箇所ずつ)。

- [ ] **Step 3: TextWindow をタイムライン非依存にする**

`DrawBody()` を書き換え。冒頭の `timeline == null` ブロックを削除し、`timeline.textCount` を `textManager.textCount` に置き換え、編集ゲートを緩和する:

```csharp
private void DrawBody()
{
    _view.SetEnabled(_view.focusedComboBox == null);

    DrawTextCountRow();

    var textCount = textManager.textCount;

    // タイムライン未読込時は実体を作る経路がレイヤーに無いため、ウィンドウ表示中に直接補う
    // (テキストの初期値は空文字列なので、作られても画面には何も出ない)
    if (timeline == null && textManager.TextData.Length != textCount)
    {
        textManager.InitTexts();
    }

    _textIndex = Mathf.Clamp(_textIndex, 0, textCount - 1);

    if (_textIndexItems.Count != textCount)
    {
        _textIndexItems.Clear();
        for (var i = 0; i < textCount; i++)
        {
            _textIndexItems.Add(i);
        }
    }

    _textComboBox.items = _textIndexItems;
    _textComboBox.currentIndex = _textIndex;
    _textComboBox.DrawButton("操作対象", _view);

    if (!textManager.IsValidIndex(_textIndex))
    {
        // 表示数変更の反映はマネージャの更新タイミング待ちになる (レイヤー側と同じ扱い)
        _view.DrawLabel("テキストが見つかりません", -1, ROW_HEIGHT,
            textColor: Color.gray);
        return;
    }

    _view.DrawHorizontalLine(Color.gray);
    _view.AddSpace(5);

    _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

    // タイムライン読込中はレイヤーが毎フレーム再生値を書き戻すため編集モード中のみ、
    // 未読込時はレイヤーが動かないため常時編集できる
    var canEdit = timeline == null || studioHackManager.isPoseEditing;
    if (!canEdit)
    {
        _view.DrawLabel("編集モード中のみテキストを操作できます", -1, ROW_HEIGHT,
            textColor: Color.yellow);
    }
    _view.SetEnabled(_view.focusedComboBox == null && canEdit);

    // 直前キーの参照と色ピッカーの同定に使うため、レイヤーの項目名と同じ名前を渡す
    var boneName = MTEP.TextTimelineLayer.TextBoneName + _textIndex;

    _textRowDrawers.Get(boneName)
        .Draw(_view, textManager.GetFreeTextSet(_textIndex), ROW_HEIGHT, boneName);

    _view.SetEnabled(_view.focusedComboBox == null);
    _view.EndScrollView();
}
```

`DrawTextCountRow()` と `SetTextCount()` も置き換え:

```csharp
/// <summary>テキスト表示数の増減行</summary>
private void DrawTextCountRow()
{
    CountRowDrawer.Draw(_view, "テキスト表示数", ROW_HEIGHT,
        textManager.textCount, MinTextCount, MaxTextCount, SetTextCount);
}

/// <summary>
/// テキスト表示数を変更する。
/// テキストレイヤーがあれば LateUpdate 側が実体とメニュー項目をまとめて作り直す。
/// ここで先に InitTexts を呼ぶとその追随ガード
/// (TextData.Length != textCount) が空振りしてメニュー項目が更新されないため、
/// レイヤーが無いとき (タイムライン未読込含む) だけ実体の再生成を補う
/// </summary>
private static void SetTextCount(int count)
{
    textManager.textCount = count;

    if (timelineManager.FindLayers(typeof(MTEP.TextTimelineLayer)).Count == 0)
    {
        textManager.InitTexts();
    }
}
```

クラス先頭の doc コメントから「タイムライン未読込時は使えない」の記述を「タイムライン未読込時も使用できる (実体はウィンドウ表示中に生成される)」へ更新する。

- [ ] **Step 4: CameraWindow のサブカメラタブのゲートを緩和**

`DrawSubCameraContent()` 冒頭の `timelineManager.timeline == null` ブロック (:604-609) を削除する。編集ゲート (:640-647) を TextWindow と同様に置き換え:

```csharp
// タイムライン読込中はレイヤーが毎フレーム再生値を書き戻すため編集モード中のみ、
// 未読込時はレイヤーが動かないため常時編集できる
var canEdit = timelineManager.timeline == null ||
    studioHackManager.isPoseEditing;
if (!canEdit)
{
    _view.DrawLabel("編集モード中のみサブカメラを操作できます", -1, ROW_HEIGHT,
        textColor: Color.yellow);
}
_view.SetEnabled(_view.focusedComboBox == null && canEdit);
```

メソッドの doc コメント (:596-597 の「タイムライン未読込時は使えない」) も「タイムライン未読込時も台数の増減と編集ができる」へ更新する。

- [ ] **Step 5: 両構成をビルドして確認**

「ビルド・テストコマンド」の MSBuild 2 本を実行。Expected: 両方 Build succeeded。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs source/COM3D2.SceneEditor.Plugin/TextWindow.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs
git commit -m "feat(timeline): テキスト/ポストエフェクトの要素数をタイムライン非依存にする"
```

---

### Task 2: プリセットスキーマ v29 と MteEffectsSnapshot

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (Compile 追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs` (新規)

**Interfaces:**
- Consumes: Task 1 の `textCount` / `paraffinCount` / `distanceFogCount` / `rimlightCount` プロパティ、`MTEP.SubCameraManager.SetCameraCount(int)`、`MTEP.PostEffectManager.ApplyParaffin(int, ColorParaffinData)` ほか Get/Apply 群、`MTEP.ColorParaffinData.CopyFrom(IPostEffectData)`
- Produces: `ScenePresetEffects` (DTO)、`MteEffectsSnapshot.CaptureState(): ScenePresetEffects`、`MteEffectsSnapshot.ApplyState(ScenePresetEffects)` (null 安全)。Task 3 が消費する

- [ ] **Step 1: ScenePresetData.cs にスキーマを追加**

ファイル先頭の using 群に (無ければ) `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` を追加する。

`ScenePresetExternal` クラスの直前に以下の 3 クラスを追加:

```csharp
/// <summary>フリーテキスト 1 件分の表示状態 (v29)</summary>
public class ScenePresetText
{
    [XmlAttribute]
    public string text = "";

    [XmlAttribute]
    public string font = "";

    [XmlAttribute]
    public int fontSize;

    [XmlAttribute]
    public float lineSpacing;

    /// <summary>TextAnchor の int 値</summary>
    [XmlAttribute]
    public int alignment;

    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale = Vector3.one;
    public Color color = Color.white;

    [XmlAttribute]
    public float sizeDeltaX;

    [XmlAttribute]
    public float sizeDeltaY;
}

/// <summary>
/// サブカメラ 1 台分の状態 (v29)。
/// position / rotation は SubCameraData のプロパティ準拠
/// (メイド追従中はオフセット、非追従中はワールド値)
/// </summary>
public class ScenePresetSubCamera
{
    [XmlAttribute]
    public bool visible = true;

    [XmlAttribute]
    public float fieldOfView = 35f;

    public Vector3 position;
    public Vector3 rotation;

    // Rect は読み書きプロパティが多く XmlSerializer の出力が安定しないため 4 値で持つ
    [XmlAttribute]
    public float viewportX;

    [XmlAttribute]
    public float viewportY;

    [XmlAttribute]
    public float viewportWidth;

    [XmlAttribute]
    public float viewportHeight;

    /// <summary>追従先メイドのスロット番号。-1 で追従なし</summary>
    [XmlAttribute]
    public int maidSlotNo = -1;

    /// <summary>MaidPointType の int 値</summary>
    [XmlAttribute]
    public int maidPointType;

    [XmlAttribute]
    public bool followRotation;
}

/// <summary>
/// MTE 由来の演出状態 (v29)。テキスト / サブカメラ / ポストエフェクト。
/// 旧プリセット (要素なし) は null になり、適用時に触らない。
/// 各グループとも「空リスト = 保存時に実体なし」は未記録と同義として触らない
/// </summary>
public class ScenePresetEffects
{
    [XmlElement("text")]
    public List<ScenePresetText> texts = new List<ScenePresetText>();

    [XmlElement("subCamera")]
    public List<ScenePresetSubCamera> subCameras = new List<ScenePresetSubCamera>();

    // ポストエフェクトは MTE の値クラス (Serializable な公開フィールドのみで
    // XmlSerializer と相性が良い) をそのまま直列化する。
    // 上流でフィールドが増えても既存要素は既定値で読める
    [XmlElement("paraffin")]
    public List<MTEP.ColorParaffinData> paraffins = new List<MTEP.ColorParaffinData>();

    [XmlElement("distanceFog")]
    public List<MTEP.DistanceFogData> distanceFogs = new List<MTEP.DistanceFogData>();

    [XmlElement("rimlight")]
    public List<MTEP.RimlightData> rimlights = new List<MTEP.RimlightData>();

    // 各エフェクトのマスター有効フラグ。個別データの enabled とは別に効果全体を握っている
    [XmlAttribute]
    public bool paraffinEnabled;

    [XmlAttribute]
    public bool distanceFogEnabled;

    [XmlAttribute]
    public bool rimlightEnabled;
}
```

`ScenePresetData` クラスへの追加:

1. バージョン履歴コメントの末尾 (v28 の後) に追記:

```csharp
// v29: effects（テキスト / サブカメラ / ポストエフェクトの MTE 由来演出）と savedEffects を追加。
//      旧形式は effects が null で読め、適用時に演出へ触らない。
//      タイムライン未読込のシーンでも保存・復元できる
```

2. `CurrentVersion` を 29 へ変更。

3. `savedBackground` フィールドの直後に追加:

```csharp
/// <summary>「演出」カテゴリ (テキスト・サブカメラ・ポストエフェクト) を保存したか (v29)</summary>
[XmlAttribute]
public bool savedEffects = true;
```

4. `bgMaterials` フィールドの直後に追加:

```csharp
/// <summary>
/// MTE 由来の演出 (v29)。旧プリセット（要素なし）は null になり、適用時に演出へ触らない
/// </summary>
public ScenePresetEffects effects;
```

- [ ] **Step 2: Manager/MteEffectsSnapshot.cs を作成**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// MTE 由来の演出状態 (テキスト / サブカメラ / ポストエフェクト) のプリセット断面。
    /// 実体は Unity コンポーネント側にしか無いため、値をここで DTO へ吸い出す。
    /// 要素数はマネージャ側プロパティが所有しており、タイムライン未読込でも保存・復元できる
    /// </summary>
    public static class MteEffectsSnapshot
    {
        // 適用時の上限。手編集や破損 XML の異常値で大量生成しないよう UI と同じ範囲へ丸める
        private const int MaxTextCount = 16;
        private const int MaxParaffinCount = 8;
        private const int MaxDistanceFogCount = 4;
        private const int MaxRimlightCount = 8;

        private static MTEP.TimelineTextManager textManager
            => MTEP.TimelineTextManager.instance;
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;
        private static MTEP.PostEffectManager postEffectManager
            => MTEP.PostEffectManager.instance;

        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            CaptureTexts(data);
            CaptureSubCameras(data);
            CapturePostEffects(data);
            return data;
        }

        /// <summary>null (旧プリセット / 未保存) なら何もしない</summary>
        public static void ApplyState(ScenePresetEffects data)
        {
            if (data == null)
            {
                return;
            }
            ApplyTexts(data);
            ApplySubCameras(data);
            ApplyPostEffects(data);
        }

        private static void CaptureTexts(ScenePresetEffects data)
        {
            foreach (var freeTextSet in textManager.TextData)
            {
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;
                if (text == null || rect == null)
                {
                    continue;
                }

                data.texts.Add(new ScenePresetText
                {
                    text = text.text,
                    font = text.font != null ? text.font.name : "",
                    fontSize = text.fontSize,
                    lineSpacing = text.lineSpacing,
                    alignment = (int)text.alignment,
                    position = rect.localPosition,
                    rotation = rect.eulerAngles,
                    scale = rect.localScale,
                    color = text.color,
                    sizeDeltaX = rect.sizeDelta.x,
                    sizeDeltaY = rect.sizeDelta.y,
                });
            }
        }

        private static void ApplyTexts(ScenePresetEffects data)
        {
            // 空 = 保存時に実体なし。「未記録」と区別できないため触らない
            if (data.texts.Count == 0)
            {
                return;
            }

            var count = Mathf.Min(data.texts.Count, MaxTextCount);
            textManager.textCount = count;
            // タイムライン読込中はテキストレイヤーの LateUpdate も作り直すが、
            // 未読込時はここが唯一の生成経路のため直接呼ぶ (InitTexts は冪等)
            textManager.InitTexts();

            for (var i = 0; i < count; i++)
            {
                if (!textManager.IsValidIndex(i))
                {
                    break;
                }
                var freeTextSet = textManager.GetFreeTextSet(i);
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;
                if (text == null || rect == null)
                {
                    continue;
                }

                var src = data.texts[i];
                text.text = src.text;
                if (!string.IsNullOrEmpty(src.font))
                {
                    text.font = textManager.GetFont(src.font);
                }
                text.fontSize = src.fontSize;
                text.lineSpacing = src.lineSpacing;
                text.alignment = (TextAnchor)src.alignment;
                text.color = src.color;
                rect.localPosition = src.position;
                rect.eulerAngles = src.rotation;
                rect.localScale = src.scale;
                rect.sizeDelta = new Vector2(src.sizeDeltaX, src.sizeDeltaY);

                textManager.UpdateFreeTextSet(i, freeTextSet);
            }
        }

        private static void CaptureSubCameras(ScenePresetEffects data)
        {
            foreach (var cameraData in subCameraManager.subCameras)
            {
                if (cameraData.camera == null)
                {
                    continue;
                }

                var follow = cameraData.follow;
                data.subCameras.Add(new ScenePresetSubCamera
                {
                    visible = cameraData.visible,
                    fieldOfView = cameraData.camera.fieldOfView,
                    // 追従中はオフセットが入る (SubCameraData のプロパティ準拠)
                    position = cameraData.position,
                    rotation = cameraData.rotation.eulerAngles,
                    viewportX = cameraData.viewportRect.x,
                    viewportY = cameraData.viewportRect.y,
                    viewportWidth = cameraData.viewportRect.width,
                    viewportHeight = cameraData.viewportRect.height,
                    maidSlotNo = follow.maidSlotNo,
                    maidPointType = (int)follow.maidPointType,
                    followRotation = follow.followRotation,
                });
            }
        }

        private static void ApplySubCameras(ScenePresetEffects data)
        {
            if (data.subCameras.Count == 0)
            {
                return;
            }

            subCameraManager.SetCameraCount(data.subCameras.Count);

            var subCameras = subCameraManager.subCameras;
            for (var i = 0; i < data.subCameras.Count && i < subCameras.Count; i++)
            {
                var cameraData = subCameras[i];
                if (cameraData.camera == null)
                {
                    continue;
                }

                var src = data.subCameras[i];
                // 追従設定を先に入れることで position / rotation プロパティの
                // 書き込み先 (オフセット / ワールド値) を保存時と一致させる
                var follow = cameraData.follow;
                follow.maidSlotNo = src.maidSlotNo;
                follow.maidPointType = (MTEP.MaidPointType)src.maidPointType;
                follow.followRotation = src.followRotation;

                cameraData.position = src.position;
                cameraData.rotation = Quaternion.Euler(src.rotation);
                cameraData.camera.fieldOfView = src.fieldOfView;
                cameraData.ApplyViewport(new Rect(
                    src.viewportX, src.viewportY, src.viewportWidth, src.viewportHeight));
                cameraData.visible = src.visible;
            }
        }

        private static void CapturePostEffects(ScenePresetEffects data)
        {
            for (var i = 0; i < postEffectManager.GetParaffinCount(); i++)
            {
                // 適用中の実体を直に直列化しないようコピーを取る
                var copy = new MTEP.ColorParaffinData();
                copy.CopyFrom(postEffectManager.GetParaffinData(i));
                data.paraffins.Add(copy);
            }
            data.paraffinEnabled = postEffectManager.paraffin.enabled;

            for (var i = 0; i < postEffectManager.GetDistanceFogCount(); i++)
            {
                var copy = new MTEP.DistanceFogData();
                copy.CopyFrom(postEffectManager.GetDistanceFogData(i));
                data.distanceFogs.Add(copy);
            }
            data.distanceFogEnabled = postEffectManager.distanceFog.enabled;

            for (var i = 0; i < postEffectManager.GetRimlightCount(); i++)
            {
                var copy = new MTEP.RimlightData();
                copy.CopyFrom(postEffectManager.GetRimlightData(i));
                data.rimlights.Add(copy);
            }
            data.rimlightEnabled = postEffectManager.rimlight.enabled;
        }

        private static void ApplyPostEffects(ScenePresetEffects data)
        {
            var paraffinCount = Mathf.Min(data.paraffins.Count, MaxParaffinCount);
            var distanceFogCount = Mathf.Min(data.distanceFogs.Count, MaxDistanceFogCount);
            var rimlightCount = Mathf.Min(data.rimlights.Count, MaxRimlightCount);

            if (paraffinCount == 0 && distanceFogCount == 0 && rimlightCount == 0)
            {
                return;
            }

            // 記録があるグループだけ要素数を合わせてから一括で実体を作り直す。
            // InitPostEffects は 3 グループ全ての count プロパティを毎回突き合わせるが、
            // 未記録グループは count を触っていないため実体数は変わらない
            if (paraffinCount > 0)
            {
                postEffectManager.paraffinCount = paraffinCount;
            }
            if (distanceFogCount > 0)
            {
                postEffectManager.distanceFogCount = distanceFogCount;
            }
            if (rimlightCount > 0)
            {
                postEffectManager.rimlightCount = rimlightCount;
            }
            postEffectManager.InitPostEffects();

            for (var i = 0; i < paraffinCount; i++)
            {
                postEffectManager.ApplyParaffin(i, data.paraffins[i]);
            }
            if (paraffinCount > 0)
            {
                // Apply* は個別データが有効だとマスターを ON にするため、保存値で確定させる
                postEffectManager.paraffin.enabled = data.paraffinEnabled;
            }

            for (var i = 0; i < distanceFogCount; i++)
            {
                postEffectManager.ApplyDistanceFog(i, data.distanceFogs[i]);
            }
            if (distanceFogCount > 0)
            {
                postEffectManager.distanceFog.enabled = data.distanceFogEnabled;
            }

            for (var i = 0; i < rimlightCount; i++)
            {
                postEffectManager.ApplyRimlight(i, data.rimlights[i]);
            }
            if (rimlightCount > 0)
            {
                postEffectManager.rimlight.enabled = data.rimlightEnabled;
            }
        }
    }
}
```

- [ ] **Step 3: csproj に Compile を追加**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Manager\ScenePresetManager.cs" />` の近くに:

```xml
<Compile Include="Manager\MteEffectsSnapshot.cs" />
```

- [ ] **Step 4: シリアライズ往復テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs` を新規作成:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ScenePresetEffectsTests
    {
        private static ScenePresetData RoundTrip(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                xml = writer.ToString();
            }
            using (var reader = new StringReader(xml))
            {
                return (ScenePresetData)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void Effects_RoundTrip_PreservesValues()
        {
            var data = new ScenePresetData { effects = new ScenePresetEffects() };
            data.effects.texts.Add(new ScenePresetText
            {
                text = "こんにちは",
                font = "Yu Gothic Bold",
                fontSize = 40,
                lineSpacing = 30f,
                alignment = 4,
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(0f, 0f, 45f),
                scale = new Vector3(2f, 2f, 2f),
                color = new Color(1f, 0.5f, 0.25f, 0.75f),
                sizeDeltaX = 800f,
                sizeDeltaY = 600f,
            });
            data.effects.subCameras.Add(new ScenePresetSubCamera
            {
                visible = false,
                fieldOfView = 50f,
                position = new Vector3(0f, 1f, -2f),
                rotation = new Vector3(10f, 20f, 30f),
                viewportX = 0.1f,
                viewportY = 0.2f,
                viewportWidth = 0.3f,
                viewportHeight = 0.4f,
                maidSlotNo = 2,
                maidPointType = 3,
                followRotation = true,
            });
            data.effects.paraffins.Add(new MTEP.ColorParaffinData
            {
                enabled = true,
                useAdd = 0.5f,
                color1 = new Color(0.1f, 0.2f, 0.3f, 0.4f),
            });
            data.effects.paraffinEnabled = true;
            data.effects.distanceFogs.Add(new MTEP.DistanceFogData { fogEnd = 25f });
            data.effects.rimlights.Add(new MTEP.RimlightData
            {
                rotation = new Vector3(5f, -15f, 0f),
                isWorldSpace = true,
            });

            var restored = RoundTrip(data);

            Assert.NotNull(restored.effects);
            var text = Assert.Single(restored.effects.texts);
            Assert.Equal("こんにちは", text.text);
            Assert.Equal("Yu Gothic Bold", text.font);
            Assert.Equal(40, text.fontSize);
            Assert.Equal(4, text.alignment);
            Assert.Equal(new Vector3(1f, 2f, 3f), text.position);
            Assert.Equal(new Color(1f, 0.5f, 0.25f, 0.75f), text.color);
            Assert.Equal(800f, text.sizeDeltaX);

            var subCamera = Assert.Single(restored.effects.subCameras);
            Assert.False(subCamera.visible);
            Assert.Equal(50f, subCamera.fieldOfView);
            Assert.Equal(0.3f, subCamera.viewportWidth);
            Assert.Equal(2, subCamera.maidSlotNo);
            Assert.Equal(3, subCamera.maidPointType);
            Assert.True(subCamera.followRotation);

            var paraffin = Assert.Single(restored.effects.paraffins);
            Assert.True(paraffin.enabled);
            Assert.Equal(0.5f, paraffin.useAdd);
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), paraffin.color1);
            Assert.True(restored.effects.paraffinEnabled);

            Assert.Equal(25f, Assert.Single(restored.effects.distanceFogs).fogEnd);
            var rimlight = Assert.Single(restored.effects.rimlights);
            Assert.Equal(new Vector3(5f, -15f, 0f), rimlight.rotation);
            Assert.True(rimlight.isWorldSpace);
        }

        [Fact]
        public void OldPreset_WithoutEffects_ReadsEffectsAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader("<ScenePresetData version=\"28\" />"))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.Null(restored.effects);
                // saved* の既定 true 規約 (v16) と同じく、effects null 側で「触らない」を担保する
                Assert.True(restored.savedEffects);
            }
        }
    }
}
```

注意 (Phase L0 の知見): テスト名前空間 `COM3D2.SceneEditor.Plugin.Tests` 内では親名前空間の SE 側 `PluginUtils` がエイリアスより優先される。本テストは `PluginUtils` を使わないため影響しないが、追記時は `MTEP` のような別名エイリアスを使うこと。

- [ ] **Step 5: COM3D25 ビルド → テスト実行**

MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Expected: 新規 2 件を含め全件 PASS。COM3D2 側の MSBuild も実行して Build succeeded を確認。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "feat(preset): MTE 由来演出のスキーマ v29 とスナップショットを追加する"
```

---

### Task 3: 保存/読込への配線と「演出」カテゴリ UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs` (:211-217 付近の読込トグル)
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs` (:325 付近)

**Interfaces:**
- Consumes: Task 2 の `MteEffectsSnapshot.CaptureState()` / `ApplyState(ScenePresetEffects)`、`ScenePresetData.effects` / `.savedEffects`
- Produces: `ScenePresetSaveOptions.saveEffects` (bool)、`ScenePresetManager.loadEffects` (static bool)、`Config.scenePresetSaveEffects` (bool)

- [ ] **Step 1: ScenePresetSaveOptions と Config にフラグを追加**

`SavePresetPopupWindow.cs` の `ScenePresetSaveOptions` に追加 (saveBackground の後):

```csharp
/// <summary>テキスト・サブカメラ・ポストエフェクトをまとめた「演出」カテゴリ (v29)</summary>
public bool saveEffects = true;
```

`Config.cs` の `scenePresetSaveBackground` (:325) の直後に追加:

```csharp
public bool scenePresetSaveEffects = true;
```

- [ ] **Step 2: ScenePresetManager に capture / apply を配線**

1. `loadBackground` プロパティ (:133) の直後に追加:

```csharp
public static bool loadEffects { get; set; } = true;
```

2. `ShouldApplyBackground` (:173-176) の直後に追加:

```csharp
/// <summary>演出カテゴリ (テキスト・サブカメラ・ポストエフェクト) を適用するか</summary>
private static bool ShouldApplyEffects(ScenePresetData data)
{
    // 旧プリセット (v28 以前) は effects が null のため、この条件で自然に読み飛ばされる
    return data.savedEffects && loadEffects && data.effects != null;
}
```

3. `Capture` (:698) 内、`data.savedBackground = options.saveBackground;` の直後に `data.savedEffects = options.saveEffects;` を追加し、`options.saveBackground` ブロックの後に追加:

```csharp
if (options.saveEffects)
{
    data.effects = MteEffectsSnapshot.CaptureState();
}
```

4. `FinishApply` (:1640) 内、冒頭の `var applyMaids = ShouldApplyMaids(data);` の直後に追加:

```csharp
// MTE 由来の演出は仕上げ段 (全メイドのロード完了後) で適用する。
// メイド追従サブカメラの position/rotation は追従先メイドの有無で
// 書き込み先 (オフセット / ワールド値) が変わるため、
// メイドが揃う前に適用すると保存時と書き込み先を取り違える。
// タイムライン読込中に適用した場合、キーフレームを持つ項目は
// 再生側 (レイヤーの ApplyPlayData) が優先して上書きする
if (ShouldApplyEffects(data))
{
    MteEffectsSnapshot.ApplyState(data.effects);
}
```

(`Apply` 本体は変更しない。保留メイドが無い場合は `Apply` が同期的に `FinishApply(data)` を呼ぶため、演出だけのプリセットでも適用される)

- [ ] **Step 3: SavePresetPopupWindow に「演出」トグルを追加**

1. フィールド `_saveBackground` の直後に `private bool _saveEffects;` を追加
2. `Show()` に `window._saveEffects = config.scenePresetSaveEffects;` を追加
3. `ConfirmSave()` に `config.scenePresetSaveEffects = _saveEffects;` と options への `saveEffects = _saveEffects,` を追加
4. `OnGUI()` の行数算出 `var rowCount = 2 + 3 + ...` を `var rowCount = 2 + 4 + ...` に変更
5. `DrawWindow()` の背景トグルの直後に追加:

```csharp
_view.DrawToggle("演出 (テキスト・サブカメラ・ポストエフェクト)", _saveEffects,
    contentWidth, ROW_HEIGHT, value => _saveEffects = value);
```

- [ ] **Step 4: PresetWindow に読込トグルを追加**

背景の `DrawLoadToggle` (:216-217) の直後に追加:

```csharp
DrawLoadToggle("演出", ScenePresetManager.loadEffects,
    value => ScenePresetManager.loadEffects = value);
```

- [ ] **Step 5: 両構成をビルドし、テストを再実行**

MSBuild 2 本 → Build succeeded、`dotnet test` → 全件 PASS を確認。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs source/COM3D2.SceneEditor.Plugin/PresetWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs
git commit -m "feat(preset): シーンプリセットに演出カテゴリを追加する"
```

---

### Task 4: 調査資料の追記

**Files:**
- Modify: `docs/scene-preset-mte-state-survey.md` (§7)

- [ ] **Step 1: §7 の末尾「詳細計画は superpowers:writing-plans で別途作成する。」を以下へ置き換える**

```markdown
実装計画: `docs/superpowers/plans/2026-08-30-scene-preset-mte-effects.md` (v29、実装済み)
```

- [ ] **Step 2: コミット**

```bash
git add docs/scene-preset-mte-state-survey.md
git commit -m "docs: MTE 由来状態の調査資料に実装計画を追記する"
```

---

## 補足 (実装者向けメモ)

- **適用時のレイヤー競合は仕様**: タイムライン読込中にプリセットを適用した場合、テキスト / サブカメラ / ポストエフェクトのキーフレームを持つ項目は非編集モードでレイヤーが毎フレーム書き戻すため、プリセット値は一瞬で上書きされうる。視線 (v26) と同様に「適用はする・再生側が優先」の割り切りで、調停ロジックは入れない
- **サブカメラのライフサイクル**: `SubCameraTimelineLayer.Dispose` は `DestroyAllCameras()` を呼ぶため、プリセット復元後にタイムラインを閉じるとサブカメラは消える (従来仕様のまま)。プリセットを再適用すれば戻る
- **DoF / GTToneMap / ステージライト / レーザー / Psyllium は今回のスコープ外**。需要が出たら `ScenePresetEffects` にグループを追加する (空リスト = 触らない規約なので後方互換で足せる)
- **`postEffectManager.controller` の getter はメインカメラへの GetOrAddComponent**。CaptureState でポストエフェクト未使用でもコンポーネントが付くが、`enabled = false` のままなら描画への影響はない
- **サブカメラの maidSlotNo はスロット番号のまま保存する**。プリセットのメイド割当 (guid ベース) とは独立で、スロット構成が変わると追従先がずれうる。追従先メイドがいない場合は `MaidFollowSubCamera.LateUpdate` が何もしないため安全
- **フォントの環境依存**: `ScenePresetText.font` は OS フォント名で保存する。復元環境に同名フォントが無い場合、`Font.CreateDynamicFontFromOSFont` は例外を出さずフォールバック描画になる (見た目が変わるだけ)。プリセット共有時の既知の制限として扱う
- 実機での動作確認 (タイムライン未読込での保存→復元、メイド追従サブカメラ込みの保存→復元、タイムライン読込中の適用、旧プリセットの読込) は次回ゲーム起動時のチェックリストに積む

## レビュー却下メモ

- InitPostEffects が 3 グループ全てを毎回突き合わせる副作用の把握を実装者に委ねている — 対処済み (ApplyPostEffects のコメントに明記する形で取り込み)。それ以上の構造変更は不要と判断
