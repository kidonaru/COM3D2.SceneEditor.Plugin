# ライブ演出 / 背景モデルのシーンプリセット対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ステージライト / ステージレーザー / サイリウム（ライブ演出 3 種）と背景モデル（表示・transform・複製）をシーンプリセットで保存・復元できるようにする。

**Architecture:** ライブ演出は Undo 用に既にある `LiveEffectState` DTO と `LiveEffectSnapshot` の収集・復元をそのまま `ScenePresetEffects.liveEffect` に載せる（`MteEffectsSnapshot` から呼べる public static な `CaptureLiveEffect` / `ApplyLiveEffect` を追加）。背景モデルは新 DTO `ScenePresetBgModel` を「背景」カテゴリ (`savedBackground`) に追加し、`BGModelManager` に「現在の背景オブジェクトへ同期する」public メソッドを生やして、`FinishApply`（背景確定後）で個数合わせ → 値の書き戻しを行う。

**Tech Stack:** C# (.NET Framework 4.8 / Unity 5.6 互換の C# 構文), `XmlSerializer`, xunit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** `docs/scene-preset-timeline-coverage-gap.md`（§2-1 ライブ演出 3 種、§2-2 背景モデル）。メイド衣装・アニメブレンド（§2-3, §2-4）は本計画のスコープ外。

## Global Constraints

- **スキーマ規約**: 新フィールドは「旧プリセットでは null → 適用時に触らない」。`ScenePresetData.CurrentVersion` を 33 → 34 に上げ、バージョン履歴コメントを追記する（`ScenePresetData.cs` の規約どおり）
- **カテゴリ**: ライブ演出は「演出」カテゴリ（`savedEffects` / `loadEffects`）、背景モデルは「背景」カテゴリ（`savedBackground` / `loadBackground`）に属する
- **適用タイミング**: どちらも `FinishApply`（全メイドロード完了後）で適用する。背景モデルは `ApplyBgMaterials` より **前**（マテリアル差分の適用先に複製モデルが含まれうるため）
- **タイムライン未読込でも保存・復元できること**（`scene-preset-mte-state-survey.md` の決定済み方針）。3 つのライブ演出マネージャは MonoBehaviour シングルトンで `timeline == null` を各自ガード済み。`BGModelManager` は `LateUpdate` が `IsValidData()`（タイムライン有効時のみ）でゲートされているため、プリセット側から明示的に同期メソッドを呼ぶ
- **2 構成ビルド**: 変更後は COM3D2 / COM3D25 の両方をビルドする（memory `dual-gameversion-build`）。ゲーム停止中に `debug.bat` を実行すると実機へ DLL がコピーされる点に注意（`CLAUDE.md`）
- **テスト**: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`（リポジトリルートで実行）
- 各 Task 完了後は commit スキルでコミットする。コメント・ログは日本語

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs` | 変更 | `LiveEffectPsylliumControllerState` に `placements` 追加。`CaptureLiveEffect` / `ApplyLiveEffect` を public static に公開（Undo とプリセットの共用点） |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 変更 | `ScenePresetEffects.liveEffect`、`ScenePresetBgModel` / `ScenePresetBgModels` DTO、`ScenePresetData.bgModels`、`CurrentVersion = 34` |
| `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs` | 変更 | `CaptureState` / `ApplyState` にライブ演出を接続 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGModelManager.cs` | 変更 | `SyncToCurrentBg()` 公開（背景切替直後の再列挙） |
| `source/COM3D2.SceneEditor.Plugin/Manager/BgModelSnapshot.cs` | 新規 | 背景モデルの DTO 収集・復元（`MteEffectsSnapshot` と同じ static クラス様式） |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 変更 | `CaptureModelAppearances` で背景モデル収集、`FinishApply` で適用 |
| `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs` | 変更 | 「演出」トグルの説明文にライブ演出を追記 |
| `source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs` | 変更 | placements の等価判定テスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs` | 変更 | liveEffect のラウンドトリップ・旧形式 null テスト |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetBgModelTests.cs` | 新規 | bgModels のラウンドトリップ・旧形式 null テスト |
| `docs/scene-preset-timeline-coverage-gap.md` | 変更 | 対応済みへ移動 |

---

### Task 1: サイリウム配置 (placement) を LiveEffectState に含める

Undo 用 DTO にはメッシュ配置（`PsylliumArea.placement`）が入っておらず、`ApplyPsylliums` の `psylliumManager.Setup` は `area.SetPlacement(null)` で矩形配置へ戻してしまう。プリセットに載せる前に DTO を完全にする。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs:85-97`（`LiveEffectPsylliumControllerState`）, `:239-260`（Capture）, `:410-430`（Apply）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs`

**Interfaces:**
- Produces: `LiveEffectPsylliumControllerState.placements : List<PsylliumPlacement>`（`areaIndex` 付き。`TimelinePsylliumData.placements` と同じ意味）

- [ ] **Step 1: 失敗するテストを書く**

`LiveEffectStateTests.cs` の `BuildSample()` の `psylliumController` 生成直後に配置を足し、テストを追加する:

```csharp
// BuildSample() 内、psylliumController.patterns.Add(...) の直後
var placement = new PsylliumPlacement { areaIndex = 0, name = "arena" };
placement.points.Add(new PsylliumPlacementPoint { x = 1f, y = 0f, z = 2f, yaw = 90f });
psylliumController.placements.Add(placement);
```

```csharp
[Fact]
public void サイリウム配置の違いも検出できる()
{
    var a = BuildSample();
    var b = BuildSample();
    b.psylliumControllers[0].placements[0].points[0].yaw = 45f;

    Assert.False(PresetDtoUtils.AreEqual(a, b));
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LiveEffectStateTests"`
Expected: ビルドエラー（`placements` が未定義）

- [ ] **Step 3: DTO と収集・復元を実装する**

`LiveEffectPsylliumControllerState` に追加:

```csharp
/// <summary>メッシュ配置。areaIndex で対象エリアを指す。無いエリアは矩形配置</summary>
public List<PsylliumPlacement> placements = new List<PsylliumPlacement>();
```

`CaptureState` のサイリウム部、`foreach (var area in controller.areas)` ループを以下へ差し替え:

```csharp
foreach (var area in controller.areas)
{
    if (area == null) continue;
    dto.areas.Add(area.areaConfig.Clone());

    if (area.placement != null)
    {
        var placement = area.placement.Clone();
        placement.areaIndex = area.index;
        dto.placements.Add(placement);
    }
}
```

`ApplyPsylliums` の `datas.Add(...)` を以下へ差し替え（`Setup` が placements をエリアへ戻す）:

```csharp
datas.Add(new TimelinePsylliumData
{
    areaCount = dto.areas.Count,
    patternCount = dto.patterns.Count,
    placements = dto.placements.Select(p => p.Clone()).ToList(),
});
```

ファイル先頭に `using System.Linq;` を追加する。

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LiveEffectStateTests"`
Expected: PASS（既存 3 件 + 新規 1 件）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs
git commit -m "fix(history): ライブ演出の履歴にサイリウムのメッシュ配置を含める"
```

---

### Task 2: ライブ演出をシーンプリセットの「演出」カテゴリへ載せる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs:130-145`（Capture 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:820-836`（`ScenePresetEffects`）, `:929-931`（バージョン履歴 / `CurrentVersion`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs:22-45`
- Modify: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs:17,221`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:180`（コメント）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs`

**Interfaces:**
- Consumes: `LiveEffectState`（Task 1 で placements 込み）
- Produces:
  - `public static LiveEffectState LiveEffectSnapshot.CaptureLiveEffect()`（既存の `protected override CaptureState()` の本体を private static `CaptureStateCore()` へ移し、両者から呼ぶ）
  - `public static void LiveEffectSnapshot.ApplyLiveEffect(LiveEffectState state)`（同様に `ApplyStateCore` へ委譲。null なら何もしない）
  - `ScenePresetEffects.liveEffect : LiveEffectState`（null = 未記録。非 null で全リスト空 = 全削除の状態として復元する）

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetEffectsTests.cs` に追加:

```csharp
[Fact]
public void LiveEffect_RoundTrip_PreservesValues()
{
    var data = new ScenePresetData { effects = new ScenePresetEffects() };
    data.effects.liveEffect = new LiveEffectState();
    var lightController = new LiveEffectStageLightControllerState
    {
        autoColor = true,
        colorMin = Color.red,
        patternType = StageLightController.PatternType.None,
    };
    lightController.lights.Add(new LiveEffectStageLightState
    {
        position = new Vector3(1f, 2f, 3f),
        color = Color.green,
    });
    data.effects.liveEffect.stageLightControllers.Add(lightController);
    var laserController = new LiveEffectStageLaserControllerState();
    laserController.lasers.Add(new LiveEffectStageLaserState { intensity = 0.5f });
    data.effects.liveEffect.stageLaserControllers.Add(laserController);
    var psyllium = new LiveEffectPsylliumControllerState { position = new Vector3(0f, 0f, 11f) };
    psyllium.areas.Add(new PsylliumAreaConfig());
    var placement = new PsylliumPlacement { areaIndex = 0 };
    placement.points.Add(new PsylliumPlacementPoint { x = 1f, z = 2f, yaw = 90f });
    psyllium.placements.Add(placement);
    data.effects.liveEffect.psylliumControllers.Add(psyllium);

    var restored = RoundTrip(data);

    var live = restored.effects.liveEffect;
    Assert.NotNull(live);
    var light = Assert.Single(live.stageLightControllers);
    Assert.True(light.autoColor);
    Assert.Equal(Color.red, light.colorMin);
    Assert.Equal(new Vector3(1f, 2f, 3f), Assert.Single(light.lights).position);
    Assert.Equal(0.5f, Assert.Single(Assert.Single(live.stageLaserControllers).lasers).intensity);
    var restoredPsyllium = Assert.Single(live.psylliumControllers);
    Assert.Equal(new Vector3(0f, 0f, 11f), restoredPsyllium.position);
    Assert.Equal(90f, Assert.Single(Assert.Single(restoredPsyllium.placements).points).yaw);
}

[Fact]
public void LiveEffect_EmptyState_RoundTripsAsNonNull()
{
    // 「全削除した状態」(非 null・空) と「未記録」(null) を XML 上で区別できること
    var data = new ScenePresetData { effects = new ScenePresetEffects { liveEffect = new LiveEffectState() } };

    var restored = RoundTrip(data);

    Assert.NotNull(restored.effects.liveEffect);
    Assert.Empty(restored.effects.liveEffect.stageLightControllers);
}

[Fact]
public void V33Preset_WithoutLiveEffect_ReadsLiveEffectAsNull()
{
    var serializer = new XmlSerializer(typeof(ScenePresetData));
    using (var reader = new StringReader(
        "<ScenePresetData version=\"33\"><effects><text text=\"a\" /></effects></ScenePresetData>"))
    {
        var restored = (ScenePresetData)serializer.Deserialize(reader);
        Assert.NotNull(restored.effects);
        Assert.Null(restored.effects.liveEffect);
    }
}
```

ファイル先頭に `using COM3D2.MotionTimelineEditor.Plugin;` を追加する。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePresetEffectsTests"`
Expected: ビルドエラー（`liveEffect` が未定義）

- [ ] **Step 3: スキーマを追加する**

`ScenePresetEffects` の末尾に追加:

```csharp
/// <summary>
/// ライブ演出 (ステージライト / ステージレーザー / サイリウム) (v34)。
/// Undo 用の LiveEffectState をそのまま載せる。
/// null = 未記録 (旧プリセット) で適用時に触らない。非 null で全リスト空 = 全削除として復元する
/// </summary>
public LiveEffectState liveEffect;
```

バージョン履歴コメントの末尾（v33 の行の後）に追記し、`CurrentVersion` を更新:

```csharp
// v34: effects に liveEffect (ステージライト / レーザー / サイリウム) と、
//      ルートに bgModels (背景モデルの表示・transform・複製) を追加。
//      旧形式はどちらも null で読め、適用時に触らない
public static readonly int CurrentVersion = 34;
```

- [ ] **Step 4: LiveEffectSnapshot に public な収集・復元を公開する**

C# では static と instance で同名同シグネチャのメソッドを共存できないため、公開名は `CaptureLiveEffect` / `ApplyLiveEffect` にする（`MteEffectsSnapshot.CaptureState` と名前が揃わないのはこの制約のため）。

`LiveEffectSnapshot` の既存 `protected override LiveEffectState CaptureState()` の本体を `private static LiveEffectState CaptureStateCore()` へ、`protected override void ApplyState(LiveEffectState state)` の本体を `private static void ApplyStateCore(LiveEffectState state)` へ移し、`Capture()` の直後に以下を置く:

```csharp
/// <summary>現在のライブ演出を DTO へ吸い出す。履歴とシーンプリセットで共用</summary>
public static LiveEffectState CaptureLiveEffect() => CaptureStateCore();

/// <summary>ライブ演出を書き戻す。null (旧プリセット / 未記録) なら何もしない</summary>
public static void ApplyLiveEffect(LiveEffectState state) => ApplyStateCore(state);

protected override LiveEffectState CaptureState() => CaptureStateCore();
protected override void ApplyState(LiveEffectState state) => ApplyStateCore(state);
```

`ApplyStateCore` 先頭の `if (state == null) return;` は既存のまま残す（プリセットの null 規約を兼ねる）。

- [ ] **Step 5: MteEffectsSnapshot に接続する**

`MteEffectsSnapshot.CaptureState()` の `data.videos = CaptureVideos();` の直後に:

```csharp
data.liveEffect = LiveEffectSnapshot.CaptureLiveEffect();
```

`MteEffectsSnapshot.ApplyState()` の `ApplyVideos(...)` の直後に:

```csharp
// ライブ演出は個数も状態に含むため、非 null なら空でも「全削除」として復元する
LiveEffectSnapshot.ApplyLiveEffect(data.liveEffect);
```

クラス冒頭の summary を「(テキスト / サブカメラ / サウンド / 動画 / ライブ演出)」に直す。

- [ ] **Step 6: UI 文言とコメントを更新する**

- `SavePresetPopupWindow.cs:17` の summary を `テキスト・サブカメラ・サウンド・動画・ライブ演出をまとめた「演出」カテゴリ (v29, v34 でライブ演出追加)` に
- `SavePresetPopupWindow.cs:221` のトグル文言を `"演出 (テキスト・サブカメラ・サウンド・動画・ライブ演出)"` に
- `ScenePresetManager.cs:180` の summary を `演出カテゴリ (テキスト・サブカメラ・サウンド・動画・ライブ演出) を適用するか` に

- [ ] **Step 7: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "feat(preset): ライブ演出 (ステージライト・レーザー・サイリウム) をシーンプリセットに保存する"
```

---

### Task 3: BGModelManager に背景切替直後の同期メソッドを公開する

`BGModelManager` は背景切替の検出を `LateUpdate`（タイムライン有効時のみ実行）で行う。プリセット適用は `ChangeBg`（同期）の直後・同じフレーム内に走るため、旧背景の子（`Object.Destroy` 予約済みでまだ生きている）を掴んだまま値を書いてしまう。プリセット側から明示的に同期できる入口を作る。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGModelManager.cs:137-149`（`LateUpdate`）, `:151-175`（`SetupModels`）

**Interfaces:**
- Produces:
  - `public void BGModelManager.SyncToCurrentBg()` — 背景オブジェクトが前回と違えば `Reset` → `SetupModelInfo` して `_prevBgObject` を更新する（列挙の同期のみ。**個数合わせ `SetupModels` は呼ばない**。呼び出し側が目的の一覧で `SetupModels` を 1 回だけ呼ぶ）。同じなら `SetupModelInfo` のみ（未列挙なら初回列挙、列挙済みなら no-op）
  - `SetupModels(List<TimelineBGModelData>)` は既存。タイムライン未読込でも動く（`UpdateTimelineBGModels` は `timeline == null` ガード済み）。`LateUpdate` は従来どおり `SyncToCurrentBg` の後に `timeline.bgModels` で個数を合わせる

- [ ] **Step 1: 同期メソッドを実装し、LateUpdate から呼ぶ**

`LateUpdate` を以下に置き換え、`SyncToCurrentBg` を追加する:

```csharp
public override void LateUpdate()
{
    if (SyncToCurrentBg())
    {
        SetupModels(timeline.bgModels);
    }
}

/// <summary>
/// 現在の背景オブジェクトへ列挙を同期する。背景が切り替わっていれば
/// 旧背景の子を掴んだままにならないよう列挙し直し、true を返す。
/// 個数合わせ (SetupModels) は行わないので、呼び出し側が目的の一覧で 1 回だけ呼ぶこと
/// (ここで timeline.bgModels を使うと、プリセット適用時に無関係な複製の生成・破棄が一瞬走る)。
/// LateUpdate はタイムライン有効時しか回らないため、シーンプリセットの適用は
/// 背景適用の後にこれを直接呼ぶ
/// </summary>
public bool SyncToCurrentBg()
{
    var bgObject = this.bgObject;
    if (bgObject == _prevBgObject)
    {
        // タイムライン未読込で一度も列挙していない場合の初回列挙 (列挙済みなら no-op)
        SetupModelInfo();
        return false;
    }

    Reset();
    SetupModelInfo();
    _prevBgObject = bgObject;
    return true;
}
```

注意: `Reset()` は `_prevBgObject = null` にするため、ここで `_prevBgObject = bgObject` を立て直さないと次の `LateUpdate` が再度 `Reset` して適用済みの transform を初期値へ戻してしまう。`bgObject == null`（背景削除）の場合は `SetupModelInfo` が何も列挙せず `_prevBgObject = null` で戻り、次フレームも `null == null` で同期済み扱いになる。`LateUpdate` 側は従来と同じく背景変化時だけ `SetupModels(timeline.bgModels)` を呼ぶ（`LateUpdate` は `IsValidData()` ゲート内なので `timeline` は非 null）。

- [ ] **Step 2: ビルドが通ることを確認する**

Run（リポジトリルート、ゲーム停止中に実機反映させたくない場合は MSBuild 直叩き。memory `msbuild-from-bash` 参照）:
`debug.bat all`
Expected: COM3D2 / COM3D25 ともにエラーなし

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/BGModelManager.cs
git commit -m "refactor(bgmodel): 背景切替直後の再列挙を SyncToCurrentBg として公開する"
```

---

### Task 4: 背景モデルの DTO とスナップショット

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetMaterial` の定義付近に DTO 追加、ルートにフィールド追加）
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/BgModelSnapshot.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetBgModelTests.cs`（新規）

**Interfaces:**
- Consumes: `BGModelManager.SyncToCurrentBg()`, `BGModelManager.SetupModels(List<TimelineBGModelData>)`, `BGModelManager.GetModel(string name)`, `BGModelStat.visible / transform / sourceName / group / name`
- Produces:
  - `ScenePresetBgModel { string sourceName; int group; bool visible; Vector3 position; Vector3 rotation; Vector3 scale; }`（`XmlAttribute` で sourceName / group / visible、他は要素）
  - `ScenePresetBgModels { [XmlElement("model")] List<ScenePresetBgModel> models }` — コンテナ。`[XmlElement]` 付きの生リストは空だと XML に何も残らず null で読み戻るため、「制御対象なし」(要素あり・0 件) と「未記録」(要素なし = null) を区別する目的で包む
  - `ScenePresetData.bgModels : ScenePresetBgModels`（null = 未記録）
  - `public static ScenePresetBgModels BgModelSnapshot.CaptureState()`
  - `public static void BgModelSnapshot.ApplyState(ScenePresetBgModels data)`（null なら何もしない。0 件は「制御対象なし」として既存の制御対象を全て外す）

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetBgModelTests.cs` を新規作成:

```csharp
using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ScenePresetBgModelTests
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
        public void BgModels_RoundTrip_PreservesValues()
        {
            var data = new ScenePresetData { bgModels = new ScenePresetBgModels() };
            data.bgModels.models.Add(new ScenePresetBgModel
            {
                sourceName = "Stage/Speaker_L",
                group = 2,
                visible = false,
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(0f, 90f, 0f),
                scale = new Vector3(2f, 2f, 2f),
            });

            var restored = RoundTrip(data);

            var model = Assert.Single(restored.bgModels.models);
            Assert.Equal("Stage/Speaker_L", model.sourceName);
            Assert.Equal(2, model.group);
            Assert.False(model.visible);
            Assert.Equal(new Vector3(1f, 2f, 3f), model.position);
            Assert.Equal(new Vector3(0f, 90f, 0f), model.rotation);
            Assert.Equal(new Vector3(2f, 2f, 2f), model.scale);
        }

        [Fact]
        public void BgModels_EmptyList_RoundTripsAsEmptyNotNull()
        {
            // 「制御対象なし」(要素あり・0 件) と「未記録」(null) を区別できること
            var data = new ScenePresetData { bgModels = new ScenePresetBgModels() };

            var restored = RoundTrip(data);

            Assert.NotNull(restored.bgModels);
            Assert.Empty(restored.bgModels.models);
        }

        [Fact]
        public void V33Preset_WithoutBgModels_ReadsAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader("<ScenePresetData version=\"33\" />"))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.Null(restored.bgModels);
            }
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePresetBgModelTests"`
Expected: ビルドエラー（`ScenePresetBgModel` が未定義）

- [ ] **Step 3: DTO を追加する**

`ScenePresetData.cs` の `ScenePresetEffects` クラスの直前に追加:

```csharp
/// <summary>
/// 背景モデル 1 件 (v34)。BGModelManager の制御対象 (表示切替・transform 編集・複製したもの) だけを記録する。
/// sourceName は背景ルートからの相対パス、group は複製番号 (0 = 元)。transform はローカル値
/// </summary>
public class ScenePresetBgModel
{
    [XmlAttribute]
    public string sourceName;

    [XmlAttribute]
    public int group;

    [XmlAttribute]
    public bool visible = true;

    public Vector3 position;

    /// <summary>ローカル回転 (オイラー角・度)</summary>
    public Vector3 rotation;

    public Vector3 scale = Vector3.one;
}

/// <summary>
/// 背景モデルの一覧 (v34)。生の List に [XmlElement] を付けると 0 件のとき XML に何も残らず
/// null で読み戻るため、「制御対象なし」(この要素あり・0 件) と「未記録」(要素なし) を
/// 区別する目的で要素に包む
/// </summary>
public class ScenePresetBgModels
{
    [XmlElement("model")]
    public List<ScenePresetBgModel> models = new List<ScenePresetBgModel>();
}
```

`ScenePresetData` の `bgMaterials` フィールドの直後に追加:

```csharp
/// <summary>
/// 背景モデルの表示・transform・複製 (v34)。「背景」カテゴリ保存時のみ入る。
/// 0 件は「制御対象なし」として既存の制御対象を全て外す。
/// 旧プリセット（要素なし）は null になり、適用時に触らない
/// </summary>
public ScenePresetBgModels bgModels;
```

- [ ] **Step 4: BgModelSnapshot を作成する**

`source/COM3D2.SceneEditor.Plugin/Manager/BgModelSnapshot.cs` を新規作成:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデル (BGModelManager の制御対象) のプリセット断面。
    /// 実体は背景オブジェクトの子 GameObject にしか無いため、値をここで DTO へ吸い出す。
    /// 背景切替のたびに子は作り直されるので、復元は背景適用の後に呼ぶ
    /// </summary>
    public static class BgModelSnapshot
    {
        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;

        /// <summary>制御対象の背景モデル全件を DTO へ吸い出す</summary>
        public static ScenePresetBgModels CaptureState()
        {
            var data = new ScenePresetBgModels();
            foreach (var model in bgModelManager.models)
            {
                var transform = model.transform;
                if (transform == null)
                {
                    continue;
                }
                data.models.Add(new ScenePresetBgModel
                {
                    sourceName = model.sourceName,
                    group = model.group,
                    visible = model.visible,
                    position = transform.localPosition,
                    rotation = transform.localEulerAngles,
                    scale = transform.localScale,
                });
            }
            return data;
        }

        /// <summary>
        /// 背景モデルを書き戻す。null (旧プリセット / 未記録) なら何もしない。
        /// 個数 (複製) を先に合わせてから各モデルへ値を書く。
        /// 保存時の背景と違う背景に適用したときは、見つからないモデルを警告だけ出して飛ばす
        /// </summary>
        public static void ApplyState(ScenePresetBgModels data)
        {
            if (data == null)
            {
                return;
            }

            var models = data.models;
            var manager = bgModelManager;
            // 背景切替後に LateUpdate が回っていない (タイムライン未読込 / 同一フレーム) と
            // 旧背景の子を掴んだままなので、必ず現在の背景へ同期してから触る。
            // 個数合わせは下の SetupModels 1 回に一本化する
            manager.SyncToCurrentBg();

            var dataList = new List<MTEP.TimelineBGModelData>();
            foreach (var state in models)
            {
                if (manager.GetModelInfo(state.sourceName) == null)
                {
                    MTEUtils.LogWarning("背景モデルが現在の背景にありません: {0}", state.sourceName);
                    continue;
                }
                dataList.Add(new MTEP.TimelineBGModelData
                {
                    sourceName = state.sourceName,
                    group = state.group,
                });
            }
            // 不足分の追加 (複製の生成含む) と余剰の削除をまとめて行い、タイムラインの一覧も同期する
            manager.SetupModels(dataList);

            foreach (var state in models)
            {
                var model = manager.GetModel(state.sourceName + MTEP.PluginUtils.GetGroupSuffix(state.group));
                var transform = model != null ? model.transform : null;
                if (transform == null)
                {
                    continue;
                }
                model.visible = state.visible;
                transform.localPosition = state.position;
                transform.localEulerAngles = state.rotation;
                transform.localScale = state.scale;
            }
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePresetBgModelTests"`
Expected: PASS（3 件）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/BgModelSnapshot.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetBgModelTests.cs
git commit -m "feat(preset): 背景モデルの DTO とスナップショットを追加する"
```

---

### Task 5: 背景モデルをシーンプリセットの保存・適用に接続する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:869-873`（`CaptureModelAppearances` の背景分）, `:2563-2571`（`ApplyModelAppearances`）

**Interfaces:**
- Consumes: `BgModelSnapshot.CaptureState()` / `BgModelSnapshot.ApplyState(ScenePresetBgModels)`, `ShouldApplyBackground(ScenePresetData)`

- [ ] **Step 1: 保存側に接続する**

`CaptureModelAppearances` の以下の部分:

```csharp
// 背景分は背景セクションと同じカテゴリに従う
if (options.saveBackground)
{
    CaptureBgMaterials(data);
}
```

を次に置き換える:

```csharp
// 背景分は背景セクションと同じカテゴリに従う。
// マテリアル差分の owner に複製モデルのパスが入りうるため、複製の記録 (bgModels) も同じカテゴリで持つ
if (options.saveBackground)
{
    data.bgModels = BgModelSnapshot.CaptureState();
    CaptureBgMaterials(data);
}
```

- [ ] **Step 2: 適用側に接続する**

`ApplyModelAppearances` を次に置き換える:

```csharp
private static void ApplyModelAppearances(ScenePresetData data)
{
    ProviderModelStat.CleanupDestroyed();

    var entries = ModelProviderHost.GetModels();
    ApplyModelMaterials(data, entries);
    ApplyModelShapeKeys(data, entries);
    // 複製モデルがマテリアル差分の適用先になりうるため、背景モデルを先に復元する
    ApplyBgModels(data);
    ApplyBgMaterials(data);
}

/// <summary>保存された背景モデル (表示・transform・複製) を復元する。背景を復元しない設定のときは触らない</summary>
private static void ApplyBgModels(ScenePresetData data)
{
    if (data.bgModels == null || !ShouldApplyBackground(data))
    {
        return;
    }
    BgModelSnapshot.ApplyState(data.bgModels);
}
```

`ApplyModelAppearances` の summary コメント（既存の「ApplyModelBoneEdits と同じく…」）は残す。

- [ ] **Step 3: ビルドとテストを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` → 全 PASS
Run: `debug.bat all`（または MSBuild 直叩き）→ COM3D2 / COM3D25 ともにエラーなし

- [ ] **Step 4: 実機で確認する（ゲーム起動中のみ。MCP `com3d25-devbridge`）**

確認項目（`eval_csharp` またはゲーム内操作で行う）:

1. タイムラインを読み込んだ状態で、背景モデルを 1 つ複製・非表示・移動し、ステージライトを 1 灯追加して色を変え、プリセットを保存する
2. 別の背景へ切り替え、ステージライトを削除してから、保存したプリセットを読み込む
3. 期待: 背景が戻り、複製モデル・表示状態・位置が再現され、ステージライトも個数・色が戻る
4. `eval_csharp` で確認: `COM3D2.MotionTimelineEditor.Plugin.BGModelManager.instance.models.Count` と `COM3D2.MotionTimelineEditor.Plugin.StageLightManager.instance.controllers.Count` が保存時と一致する
5. **タイムライン未読込**（新規作成前）の状態で同じプリセットを読み込み、ライブ演出が復元され、例外ログが出ないことを `tail_log` で確認する（背景モデルは `SyncToCurrentBg` が初回列挙を行うため復元されるはず。されない場合はログの警告文言を報告に含める）
6. **タイムライン読込中 + メイド呼出を伴う**プリセット（未呼出のメイドを含む）を読み込む。メイドロード待ちの数フレームは `BGModelManager.LateUpdate` が `timeline.bgModels` で先に個数合わせを行い、その後 `FinishApply` がプリセットの一覧で上書きする。最終状態が保存時と一致すること、`tail_log` に例外が無いことを確認する（途中の一瞬のちらつき・「背景モデルを追加/削除しました」のデバッグログは想定内）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): 背景モデルの表示・transform・複製をシーンプリセットに保存する"
```

---

### Task 6: 調査資料を更新する

**Files:**
- Modify: `docs/scene-preset-timeline-coverage-gap.md`

- [ ] **Step 1: 対応済みへ移す**

§2 の未対応一覧からステージライト / ステージレーザー / サイリウム / 背景モデルの 4 行を削除し、§2-1 と §2-2 の本文を以下の短い記録に置き換える（見出しは残す）:

```markdown
### 2-1. ライブ演出 3 種（ステージライト / ステージレーザー / サイリウム）

**対応済み (v34, 2026-09-15)**。`ScenePresetEffects.liveEffect` に Undo 用の `LiveEffectState` をそのまま載せ、`LiveEffectSnapshot.CaptureLiveEffect` / `ApplyLiveEffect` を `MteEffectsSnapshot` から呼ぶ。「演出」カテゴリに属する。あわせてサイリウムのメッシュ配置 (`placements`) を DTO に追加した（Undo でも保持されるようになった）。

### 2-2. 背景モデル

**対応済み (v34, 2026-09-15)**。`ScenePresetData.bgModels` (`ScenePresetBgModels` に包んだ `ScenePresetBgModel`: sourceName / group / visible / ローカル transform) を「背景」カテゴリで保存し、`BgModelSnapshot` が `BGModelManager.SyncToCurrentBg()` → `SetupModels` → 値の書き戻しの順で復元する。適用は `FinishApply` の `ApplyModelAppearances` 内、`ApplyBgMaterials` の直前。保存時と違う背景へ適用した場合、見つからないモデルは警告のみで飛ばす。
```

§4 の対応済み一覧に「ライブ演出（ステージライト・レーザー・サイリウム）、背景モデル（表示・transform・複製）」を追記し、§5 の 1・2 を削除して 3 だけ残す。

- [ ] **Step 2: コミット**

```bash
git add docs/scene-preset-timeline-coverage-gap.md
git commit -m "docs: ライブ演出と背景モデルのプリセット対応を調査資料へ反映する"
```

---

## 既知の制限（仕様として受け入れる）

- タイムライン読込中にメイド呼出を伴うプリセットを適用すると、メイドロード待ちの間に `BGModelManager.LateUpdate` が `timeline.bgModels` で個数合わせを先行し、`FinishApply` でプリセットの一覧に上書きされる。最終状態は正しいが、その間に一瞬の見た目のちらつきとデバッグログが出うる。`Apply` 冒頭で履歴をクリアしているため Undo 履歴は汚れない

- タイムライン未読込で復元した背景モデル・ライブ演出は、その後にタイムラインを **新規作成** すると `OnLoad` が空の一覧で `SetupModels` / `SetupLights` を呼ぶため消える。ステージライト等の既存挙動と同じで、本計画では変えない
- 背景モデルの照合は `sourceName`（背景ルートからの相対パス）+ `group`。保存時と違う背景に適用したときは一致するパスのものだけ復元する
- `LiveEffectState` を `ScenePresetEffects` に直接載せるため、XML の要素名は DTO のフィールド名（`stageLightControllers` 等）になる。プリセット XML は内部形式なので許容する

## Self-Review

- **Spec coverage**: §2-1（Task 1, 2）、§2-2（Task 3, 4, 5）、資料更新（Task 6）。§2-3 / §2-4 はスコープ外として明記
- **Placeholder scan**: 実機確認（Task 5 Step 4）は手順と期待値を具体化済み
- **Type consistency**: `LiveEffectSnapshot.CaptureLiveEffect()` / `ApplyLiveEffect(LiveEffectState)`（Task 2 Step 4 で確定、Step 5 で使用）、`BgModelSnapshot.CaptureState()` / `ApplyState(ScenePresetBgModels)`（Task 4 定義、Task 5 使用）、`BGModelManager.SyncToCurrentBg()`（Task 3 定義、Task 4 使用）で一致

## レビュー却下メモ

- `BgModelSnapshot.ApplyState` の「保存時と違う背景で sourceName が一致しないモデルを飛ばす」分岐に単体テストが無い — `BGModelManager` / `BgMgr` シングルトンと Unity の GameObject に依存するため xunit では実行できない。実機確認（Task 5 Step 4）の手順 2 で別背景への適用を含めて検証する
