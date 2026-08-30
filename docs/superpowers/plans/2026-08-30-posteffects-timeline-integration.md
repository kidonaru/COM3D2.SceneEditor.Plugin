# PostEffects.Plugin タイムライン連携 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン対応 5 系統 (DoF / GTToneMap / パラフィン / 距離フォグ / リムライト) の実体を PostEffects.Plugin へ一本化し、SceneEditor のタイムラインはブリッジ経由で PostEffects.Plugin の設定値を駆動する。PostEffects.Plugin には対応 5 系統の集約ビュー「タイムライン」タブを追加する。

**Architecture:** ① PostEffects.Plugin に公開 `TimelineBridge`(型付き static API) を新設。② SceneEditor は `COM3D25.PostEffects.Plugin.dll` を直接参照し、アクセスを遅延バインドの `PostEffectsBridge` 1 クラスに隔離。`MTEP.PostEffectManager` の実装をブリッジ委譲へ置換し、`Timeline/UnityScripts/PostEffect/` の実体を削除。③ 未導入時はポストエフェクトレイヤー未登録 + UI 非表示。④ v29 プリセットからポストエフェクト部分を撤去し、PostEffects サイドカーへ一本化。**v29 撤去 (Task 4) は旧型 (`MTEP.ColorParaffinData` 等) への依存を先に断つため、委譲化 (Task 5) より前に行う。**

**Tech Stack:** C# (Unity IMGUI / XmlSerializer)、2 リポジトリ (COM3D2.PostEffects.Plugin / COM3D2.SceneEditor.Plugin)、xUnit

**Spec:** `docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md`

## Global Constraints

- **両構成ビルド必須**: 両リポジトリとも COM3D2 (.NET 3.5) / COM3D25 (.NET 4.7.1) を MSBuild 直叩きで確認する。`debug.bat` / `build.bat` 経由は使わない (実機へ DLL をコピーするため)
- .NET 3.5 制約: 入力 5 個以上の `Func<>`/`Action<>` 禁止、`?.` 以外の新しめ構文はリポジトリ既存コードに合わせる
- コメント・ログは日本語
- SceneEditor 側から PostEffects の型へは `using PEP = COM3D25.PostEffects.Plugin;` エイリアス経由でアクセスし、**PostEffectsBridge とタイムライン層 (Manager/TransformData/TimelineLayer/RowDrawer) 以外に漏らさない**
- PostEffects のアセンブリ名は両構成とも `COM3D25.PostEffects.Plugin`、名前空間も `COM3D25.PostEffects.Plugin` (確認済み)
- 実機での動作確認は次回ゲーム起動時チェックリストに積む (本計画ではビルドとテストまで)

## ビルド・テストコマンド

PostEffects.Plugin (Task 1-2):

```
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
MSBuild COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
MSBuild COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D25_DIR=W:\COM3D2_5"
```

SceneEditor.Plugin (Task 3-6):

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2"
MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

(SceneEditor のテストは COM3D25 ビルドの DLL を参照するため COM3D25 ビルド後に実行)

---

### Task 1: PostEffects.Plugin に TimelineBridge を新設

SceneEditor のタイムラインから毎フレーム呼ばれる公開 API。現行 SceneEditor `MTEP.PostEffectManager` の API 形 (Get/Apply/Count) を踏襲する。

**Files:**
- Create: `source/COM3D25.PostEffects.Plugin/TimelineBridge.cs`
- Modify: `source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj` (Compile 追加)

**Interfaces:**
- Consumes: `EffectSettings.instance` (paraffin/distanceFog/rimlight/gtToneMap/depthOfField の各 Setting)、`PostEffectManager.instance.controllers`、各 Settings の `GetDataCount()/GetData(int)/SetData(int, data)/AddData(data)/RemoveDataLast()`、`ColorParaffinEffectModel.MAX_PARAFFIN_COUNT` / `DistanceFogEffectModel.MAX_FOG_COUNT` / `RimlightEffectModel.MAX_RIMLIGHT_COUNT` (いずれも値 4。レビューで確認済み)
- Produces (Task 3 の PostEffectsBridge が消費):
  - `TimelineBridge.paraffinCount / distanceFogCount / rimlightCount { get; set; }` (int)
  - `GetParaffinData(int): ColorParaffinData` / `ApplyParaffin(int, ColorParaffinData)` / `paraffinEnabled { get; set; }` (bool)、距離フォグ・リムライトも同形
  - `GetGTToneMap(): GTToneMapSetting` / `ApplyGTToneMap(GTToneMapSetting)`
  - `GetDepthOfField(): DepthOfFieldSetting` / `ApplyDepthOfField(DepthOfFieldSetting)`
  - `MaxParaffinCount / MaxDistanceFogCount / MaxRimlightCount` (public const int。SceneEditor 側 UI が上限表示に使う)

- [ ] **Step 1: 事前確認 — 依存メンバの実在を検証する**

以下を grep で確認し、名前が違えば以降のコードを実名へ合わせる:

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
grep -n "dirty" EffectSettings.cs                          # 変更通知フィールドの実名
grep -n "MAX_" Effects/ColorParaffinEffectModel.cs Effects/DistanceFogEffectModel.cs Effects/RimlightEffectModel.cs
grep -n "RemoveDataLast\|RemoveData\|AddData\|SetData\|GetData" Effects/ColorParaffinEffectSettings.cs
grep -n "depthOfField\|gtToneMap" EffectSettings.cs        # Setting プロパティの実名
```

Expected: `EffectSettings` に dirty 相当のフラグ (MainWindow.cs が `settings.dirty = true;` と書いている)、各 Model に MAX_*_COUNT 定数 (各 4)、Settings に AddData/RemoveData 系がある。

- [ ] **Step 2: TimelineBridge.cs を作成**

```csharp
using UnityEngine;

namespace COM3D25.PostEffects.Plugin
{
    /// <summary>
    /// SceneEditor のタイムラインからポストエフェクトを駆動するための公開 API。
    /// 再生中は毎フレーム呼ばれるため、XML やリフレクションを挟まず設定値を直接読み書きする。
    /// 対象はタイムライン対応 5 系統 (DoF / GTToneMap / パラフィン / 距離フォグ / リムライト) のみ
    /// </summary>
    public static class TimelineBridge
    {
        // 各系統の上限。実体側のシェーダーバッファ上限と同値で、SceneEditor 側 UI もこれを参照する
        public const int MaxParaffinCount = ColorParaffinEffectModel.MAX_PARAFFIN_COUNT;
        public const int MaxDistanceFogCount = DistanceFogEffectModel.MAX_FOG_COUNT;
        public const int MaxRimlightCount = RimlightEffectModel.MAX_RIMLIGHT_COUNT;

        private static EffectSettings settings => EffectSettings.instance;

        /// <summary>パラフィンのデータ数。set は上限で丸めて増減する</summary>
        public static int paraffinCount
        {
            get => settings.paraffin.GetDataCount();
            set => ResizeData(
                settings.paraffin.GetDataCount(),
                Mathf.Clamp(value, 0, MaxParaffinCount),
                () => settings.paraffin.AddData(new ColorParaffinData()),
                () => settings.paraffin.RemoveDataLast());
        }

        public static bool paraffinEnabled
        {
            get => settings.paraffin.enabled;
            set { settings.paraffin.enabled = value; settings.dirty = true; }
        }

        public static ColorParaffinData GetParaffinData(int index)
        {
            return settings.paraffin.GetData(index);
        }

        public static void ApplyParaffin(int index, ColorParaffinData data)
        {
            // 個別データが有効なら系統ごと有効化する (SceneEditor 旧実装と同じ規約)
            if (data.enabled)
            {
                settings.paraffin.enabled = true;
            }
            settings.paraffin.SetData(index, data);
            settings.dirty = true;
        }

        /// <summary>距離フォグのデータ数。set は上限で丸めて増減する</summary>
        public static int distanceFogCount
        {
            get => settings.distanceFog.GetDataCount();
            set => ResizeData(
                settings.distanceFog.GetDataCount(),
                Mathf.Clamp(value, 0, MaxDistanceFogCount),
                () => settings.distanceFog.AddData(new DistanceFogData()),
                () => settings.distanceFog.RemoveDataLast());
        }

        public static bool distanceFogEnabled
        {
            get => settings.distanceFog.enabled;
            set { settings.distanceFog.enabled = value; settings.dirty = true; }
        }

        public static DistanceFogData GetDistanceFogData(int index)
        {
            return settings.distanceFog.GetData(index);
        }

        public static void ApplyDistanceFog(int index, DistanceFogData data)
        {
            if (data.enabled)
            {
                settings.distanceFog.enabled = true;
            }
            settings.distanceFog.SetData(index, data);
            settings.dirty = true;
        }

        /// <summary>リムライトのデータ数。set は上限で丸めて増減する</summary>
        public static int rimlightCount
        {
            get => settings.rimlight.GetDataCount();
            set => ResizeData(
                settings.rimlight.GetDataCount(),
                Mathf.Clamp(value, 0, MaxRimlightCount),
                () => settings.rimlight.AddData(new RimlightData()),
                () => settings.rimlight.RemoveDataLast());
        }

        public static bool rimlightEnabled
        {
            get => settings.rimlight.enabled;
            set { settings.rimlight.enabled = value; settings.dirty = true; }
        }

        public static RimlightData GetRimlightData(int index)
        {
            return settings.rimlight.GetData(index);
        }

        public static void ApplyRimlight(int index, RimlightData data)
        {
            if (data.enabled)
            {
                settings.rimlight.enabled = true;
            }
            settings.rimlight.SetData(index, data);
            settings.dirty = true;
        }

        public static GTToneMapSetting GetGTToneMap()
        {
            return settings.gtToneMap;
        }

        public static void ApplyGTToneMap(GTToneMapSetting data)
        {
            settings.gtToneMap = data;
            settings.dirty = true;
        }

        public static DepthOfFieldSetting GetDepthOfField()
        {
            return settings.depthOfField;
        }

        public static void ApplyDepthOfField(DepthOfFieldSetting data)
        {
            settings.depthOfField = data;
            settings.dirty = true;
        }

        /// <summary>データ数を target へ寄せる。増減どちらも 1 件ずつで、書き込み後は dirty を立てる</summary>
        private static void ResizeData(
            int current, int target,
            System.Action addOne, System.Action removeLast)
        {
            if (current == target)
            {
                return;
            }
            while (current < target)
            {
                addOne();
                current++;
            }
            while (current > target)
            {
                removeLast();
                current--;
            }
            settings.dirty = true;
        }
    }
}
```

注意:
- `settings.dirty` の実名が違う場合 (Step 1 の確認結果)、全箇所を実名へ置換する。dirty 相当が無く LateUpdate が毎フレーム適用する構造なら dirty 行を削除する
- `GTToneMapSetting` / `DepthOfFieldSetting` は参照渡しで settings を差し替える。SceneEditor 側 (Task 5) は Get で受けた参照を直接書き換えず、値を詰め替えてから Apply する

- [ ] **Step 3: csproj に Compile を追加**

`COM3D25.PostEffects.Plugin.csproj` の `<Compile Include="EffectSettings.cs" />` の近くに:

```xml
<Compile Include="TimelineBridge.cs" />
```

- [ ] **Step 4: 両構成をビルド**

「ビルド・テストコマンド」の PostEffects 側 MSBuild 2 本を実行。Expected: 両方 Build succeeded。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/TimelineBridge.cs source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj
git commit -m "feat(bridge): タイムライン連携用の TimelineBridge を追加する"
```

---

### Task 2: PostEffects.Plugin に「タイムライン」タブを追加

MainWindow のモード (現在 `{ "エフェクト", "プリセット" }`) に「タイムライン」を追加し、対応 5 系統だけを既存の行描画 (`DrawEffectRow`) で集約表示する。

**Files:**
- Modify: `source/COM3D25.PostEffects.Plugin/MainWindow.cs`

**Interfaces:**
- Consumes: `PostEffectManager.instance.controllers`、`GetController<T>()`、既存 `DrawEffectRow(GUIView, EffectControllerBase)`
- Produces: UI のみ (後続タスクからの依存なし)

- [ ] **Step 1: 事前確認 — モード切替の分岐箇所を読む**

`MainWindow.cs` の `DrawContent()` (123 行付近) と `_modeIndex` の分岐 (エフェクト/プリセットの出し分け) を読み、分岐形式 (if / switch) を把握する。

- [ ] **Step 2: ModeNames とタイムラインモードの描画を追加**

1. `ModeNames` を変更:

```csharp
private static readonly string[] ModeNames = { "エフェクト", "タイムライン", "プリセット" };

private int _modeIndex = 0;  // 0: エフェクト, 1: タイムライン, 2: プリセット
```

2. `_modeIndex` の分岐に「タイムライン」を追加する。既存分岐が `_modeIndex == 1` でプリセットを出しているなら、プリセット判定を 2 へずらし、1 で以下の新メソッドを呼ぶ:

```csharp
/// <summary>
/// タイムライン対応 5 系統の集約ビュー。
/// SceneEditor のタイムラインが駆動する対象をまとめて確認・編集するためのタブで、
/// 描画は既存の DrawEffectRow をそのまま使う (個別タブと同じ操作性)
/// </summary>
private void DrawTimelineContent(GUIView view)
{
    view.DrawLabel("タイムライン対応エフェクト", -1, 20);
    view.DrawHorizontalLine(Color.gray);
    view.AddSpace(5);

    view.BeginScrollView(-1, GetScrollHeight(view), GUIView.AutoScrollViewRect, false, true);
    {
        if (_timelineControllers.Count == 0)
        {
            InitTimelineControllers();
        }
        foreach (var controller in _timelineControllers)
        {
            DrawEffectRow(view, controller);
        }
    }
    view.EndScrollView();
}
```

3. フィールドと初期化を追加:

```csharp
// タイムライン対応 5 系統。表示順は SceneEditor のポストエフェクトレイヤーの項目順に合わせる
private readonly List<EffectControllerBase> _timelineControllers = new List<EffectControllerBase>();

private void InitTimelineControllers()
{
    _timelineControllers.Clear();
    var manager = PostEffectManager.instance;
    _timelineControllers.Add(manager.GetController<DepthOfFieldController>());
    _timelineControllers.Add(manager.GetController<GTToneMapController>());
    _timelineControllers.Add(manager.GetController<ParaffinController>());
    _timelineControllers.Add(manager.GetController<DistanceFogController>());
    _timelineControllers.Add(manager.GetController<RimlightController>());
    // 登録前に呼ばれた場合に null を掴まないよう除去する
    _timelineControllers.RemoveAll(c => c == null);
}
```

`InitTimelineControllers()` は `DrawTimelineContent` 内で `_timelineControllers.Count == 0` のときに呼ぶ (コントローラ登録完了後に確実に解決するため。全 5 件解決できるまで毎描画リトライになるが、登録完了後は 1 回で確定する)。

- [ ] **Step 3: 両構成をビルド**

PostEffects 側 MSBuild 2 本。Expected: 両方 Build succeeded。

- [ ] **Step 4: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MainWindow.cs
git commit -m "feat(window): タイムライン対応エフェクトの集約タブを追加する"
```

---

### Task 3: SceneEditor に参照追加と PostEffectsBridge を新設

`COM3D25.PostEffects.Plugin.dll` への参照と、型ロード失敗を隔離する遅延バインドのブリッジ。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (Reference + Compile 追加)
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectsBridge.cs`

**Interfaces:**
- Consumes: Task 1 の `PEP.TimelineBridge` 全メンバ
- Produces (Task 5-6 が消費):
  - `PostEffectsBridge.isAvailable` (static bool) — PostEffects.Plugin の DLL が読めるか

- [ ] **Step 1: csproj に参照を追加**

`COM3D2.SceneEditor.Plugin.csproj` の共通 `<ItemGroup>` (Assembly-CSharp 等の Reference が並ぶ箇所) に追加。DLL は実機の Sybaris フォルダを参照する (両 GameDir に PostEffects.Plugin がデプロイ済みであること):

```xml
<Reference Include="COM3D25.PostEffects.Plugin">
  <HintPath>$(GameDir)\Sybaris\UnityInjector\COM3D25.PostEffects.Plugin.dll</HintPath>
  <Private>False</Private>
</Reference>
```

事前確認: 両ゲームフォルダに DLL があるか。

```bash
ls "W:/COM3D2/Sybaris/UnityInjector/COM3D25.PostEffects.Plugin.dll" "W:/COM3D2_5/Sybaris/UnityInjector/COM3D25.PostEffects.Plugin.dll"
```

無い側があれば、Task 1 のビルド出力 (`bin/Debug/…`) から**手動コピーせず**、ユーザーに配置方法を確認する (ゲームフォルダへの書き込みは実機反映になるため)。ビルドだけ通したい場合は HintPath をビルド出力の相対パスに変える選択肢を提示する。

- [ ] **Step 2: PostEffectsBridge.cs を作成**

```csharp
using System;
using PEP = COM3D25.PostEffects.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// COM3D25.PostEffects.Plugin への参照を隔離する唯一のクラス。
    /// 未導入環境では型ロードに失敗するため、PEP の型に触れるコードはすべて
    /// このクラス経由 (または isAvailable ガードの内側) に置くこと。
    /// isAvailable == false のときは他のメンバを呼んではならない。
    /// isAvailable の初回参照は PostEffects 側の初期化を誘発するため、
    /// プラグインロード完了後 (TimelineIntegration.Initialize 以降) に行うこと
    /// </summary>
    public static class PostEffectsBridge
    {
        private static bool? _isAvailable;

        /// <summary>PostEffects.Plugin が導入されているか。初回参照時に型ロードを試す</summary>
        public static bool isAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    _isAvailable = CheckAvailable();
                }
                return _isAvailable.Value;
            }
        }

        // 型ロードを別メソッドに隔離し、JIT が isAvailable 自体のコンパイルで
        // PEP の型解決を要求しないようにする
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CheckAvailable()
        {
            try
            {
                TouchBridge();
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning(
                    "PostEffects.Plugin が見つからないため、ポストエフェクトのタイムライン機能を無効化します: {0}",
                    e.GetType().Name);
                return false;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void TouchBridge()
        {
            // 定数参照だけでは型ロードを誘発できないため、静的メンバへ触れる。
            // paraffinCount の get は EffectSettings.instance の生成を伴う
            PEP.TimelineBridge.paraffinCount.GetHashCode();
        }
    }
}
```

注意: `MTEUtils.LogWarning` のシグネチャ (format, args) はリポジトリ既存の呼び出しに合わせる。

- [ ] **Step 3: csproj に Compile を追加**

```xml
<Compile Include="Timeline\Manager\PostEffectsBridge.cs" />
```

- [ ] **Step 4: 両構成をビルド**

SceneEditor 側 MSBuild 2 本。Expected: 両方 Build succeeded。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectsBridge.cs
git commit -m "feat(bridge): PostEffects.Plugin への遅延バインドブリッジを追加する"
```

---

### Task 4: v29 プリセットからポストエフェクト部分を撤去

演出カテゴリをテキスト + サブカメラのみにし、ポストエフェクトのプリセットは PostEffects サイドカープロバイダへ一本化する。

**このタスクを委譲化 (Task 5) より先に行う理由**: `ScenePresetData.effects` と `MteEffectsSnapshot` は削除予定の旧型 (`MTEP.ColorParaffinData` 等) と旧 API (`postEffectManager.paraffin.enabled` 等) に依存しており、先に撤去しないと Task 5 のビルド確認が通らない (レビュー指摘)。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs`

**Interfaces:**
- Consumes: なし (削除のみ。この時点では旧 API のまま)
- Produces: `ScenePresetEffects` は `texts` / `subCameras` のみを持つ。`MteEffectsSnapshot` はポストエフェクトに触れない

- [ ] **Step 1: テストを先に新仕様へ書き換える (TDD)**

`ScenePresetEffectsTests.cs` を修正:

1. `Effects_RoundTrip_PreservesValues` から paraffins / distanceFogs / rimlights / 各 enabled の構築とアサーション (`data.effects.paraffins.Add(...)` 以降のポストエフェクト部) を削除する。テキストとサブカメラのアサーションは変更しない
2. `using MTEP = ...` が不要になれば削除する
3. 旧形式テスト `OldPreset_WithoutEffects_ReadsEffectsAsNull` は変更不要
4. 新規テストを追加 — v29 で保存されたポストエフェクト入り XML が要素無視で読めること:

```csharp
[Fact]
public void V29Preset_WithPostEffectElements_IgnoresThemSafely()
{
    // ポストエフェクト部を撤去する前の v29 が出力していた形。
    // XmlSerializer は未知要素・未知属性を黙って読み飛ばすことを確認する
    var xml =
        "<ScenePresetData version=\"29\" savedEffects=\"true\">" +
        "<effects paraffinEnabled=\"true\" distanceFogEnabled=\"false\" rimlightEnabled=\"false\">" +
        "<text text=\"hello\" fontSize=\"20\" />" +
        "<paraffin enabled=\"true\" useAdd=\"0.5\" />" +
        "<distanceFog fogEnd=\"25\" />" +
        "<rimlight isWorldSpace=\"true\" />" +
        "</effects>" +
        "</ScenePresetData>";

    var serializer = new XmlSerializer(typeof(ScenePresetData));
    using (var reader = new StringReader(xml))
    {
        var restored = (ScenePresetData)serializer.Deserialize(reader);
        Assert.NotNull(restored.effects);
        var text = Assert.Single(restored.effects.texts);
        Assert.Equal("hello", text.text);
    }
}
```

- [ ] **Step 2: ScenePresetData.cs から撤去**

1. `ScenePresetEffects` から `paraffins` / `distanceFogs` / `rimlights` フィールドと `paraffinEnabled` / `distanceFogEnabled` / `rimlightEnabled` 属性を削除
2. 不要になった `using MTEP = ...` が他で未使用なら削除
3. `ScenePresetEffects` の doc コメントを「テキスト / サブカメラ」へ更新
4. バージョン履歴コメントの v29 行を実態へ更新:

```csharp
// v29: effects（テキスト / サブカメラ）と savedEffects を追加。
//      旧形式は effects が null で読め、適用時に演出へ触らない。
//      ポストエフェクトは PostEffects.Plugin のサイドカープリセットが担う
```

(CurrentVersion は 29 のまま。v29 は未リリースのため番号を上げない)

- [ ] **Step 3: MteEffectsSnapshot.cs から撤去**

1. `CapturePostEffects` / `ApplyPostEffects` メソッドと、`CaptureState` / `ApplyState` からの呼び出しを削除
2. `MaxParaffinCount` / `MaxDistanceFogCount` / `MaxRimlightCount` 定数と `postEffectManager` プロパティを削除
3. クラスの doc コメントを「テキスト / サブカメラのプリセット断面」へ更新

- [ ] **Step 4: ビルド → テスト**

MSBuild 2 本 (COM3D2 / COM3D25) → Build succeeded、`dotnet test` → 全件 PASS (新規テスト含む)。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs
git commit -m "refactor(preset): 演出カテゴリからポストエフェクトを撤去しサイドカーへ一本化する"
```

---

### Task 5: PostEffectManager の委譲化と実体削除

SceneEditor 側の 5 系統の実体 (`Timeline/UnityScripts/PostEffect/`) を削除し、`MTEP.PostEffectManager` の公開 API を維持したままブリッジ委譲に置き換える。データ型は PEP の同名クラス (`ColorParaffinData` / `DistanceFogData` / `RimlightData`。メンバ構成は同一と確認済み) へ切り替える。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs` (全面書き換え)
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PostEffect/` 配下 11 ファイル
  (ColorParaffinEffectModel / ColorParaffinEffectSettings / DistanceFogEffectModel / DistanceFogEffectSettings / GTToneMapController / PostEffectContext / PostEffectController / PostEffectModelBase / PostEffectSettingsBase / RimlightEffectModel / RimlightEffectSettings)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (削除ファイルの Compile 除去)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Config.cs` (孤児化するデバッグ設定の削除)
- Modify: PEP データ型を参照する既存ファイル (using 追加。実対象は Step 1 で確定):
  `Timeline/TimelineLayer/PostEffectTimelineLayer*.cs`、`Timeline/TransformData/TransformData{Paraffin,DistanceFog,Rimlight,GTToneMap,DepthOfField}.cs`、`PostEffectRowDrawer.cs`

**Interfaces:**
- Consumes: Task 3 の `PostEffectsBridge` / `PEP.TimelineBridge` (Task 1 の全メンバ)
- Produces (既存呼び出し元が引き続き使う。シグネチャ維持):
  - `PostEffectManager.instance`、`paraffinCount / distanceFogCount / rimlightCount { get; set; }`
  - `GetParaffinData(int): PEP.ColorParaffinData` / `ApplyParaffin(int, PEP.ColorParaffinData)`、フォグ・リムライト同形 (**戻り値の型だけ MTEP → PEP に変わる**)
  - `GetDepthOfFieldData(): DepthOfFieldData` / `ApplyDepthOfField(DepthOfFieldData)` (MTEP の DTO を維持)
  - `GetGTToneMapData(): GTToneMapData` / `ApplyGTToneMap(GTToneMapData)` (MTEP の DTO を維持。定義を PostEffectManager.cs 内へ移設)
  - `InitPostEffects()` / `DisableAllEffects()` / `paraffinEnabled / distanceFogEnabled / rimlightEnabled { get; set; }`
  - `MaxParaffinCount / MaxDistanceFogCount / MaxRimlightCount` (PEP.TimelineBridge の同名定数への委譲。値は 4)

- [ ] **Step 1: 影響範囲を確定する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -rln "ColorParaffinData\|DistanceFogData\|RimlightData\|GTToneMapData\|ColorParaffinEffectSettings\|DistanceFogEffectSettings\|RimlightEffectSettings\|PostEffectController\|GTToneMapController" source/COM3D2.SceneEditor.Plugin --include='*.cs'
grep -n "GTToneMapData" source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PostEffect/GTToneMapController.cs
# 上限定数の消費箇所 (旧 8 → 新 4 に揃える対象)
grep -rn "MaxParaffinCount\|MaxDistanceFogCount\|MaxRimlightCount" source/COM3D2.SceneEditor.Plugin --include='*.cs'
# 孤児化するデバッグ設定の定義と UI
grep -rn "paraffinDebug\|distanceFogDebug\|rimlightDebug\|dofVisualizeFocus\|dofHighResolution\|dofNearBlur" source/COM3D2.SceneEditor.Plugin --include='*.cs'
```

Expected: データ型の参照はレイヤー・TransformData・RowDrawer・Manager に限られる (Task 4 で Snapshot/Preset は撤去済み)。`GTToneMapData` クラス定義の現在地とメンバ (enabled / maxBrightness / contrast / linearStart / linearLength / blackTightness / blackOffset を想定) を控える。`MaxParaffinCount` 等 (旧値 8) とデバッグ設定の消費箇所を一覧化する。想定外の参照元があれば本 Step で一覧化してから進む。

- [ ] **Step 2: PostEffectManager.cs を委譲実装へ書き換える**

方針 (既存の public シグネチャは維持):

1. ファイル冒頭に `using PEP = COM3D25.PostEffects.Plugin;` を追加
2. `DepthOfFieldData` (既存 DTO) はそのまま残す。`GTToneMapData` の定義を旧 GTToneMapController.cs からこのファイルへ移設する (フィールドは Step 1 で控えたまま)
3. `_controller` / `controller` / `gtToneMapController` / `paraffin` / `distanceFog` / `rimlight` / `gtToneMap` / `depthOfField` など旧実体へのアクセサを削除
4. 上限定数を PEP へ委譲する (旧値 8 は実体上限 4 と食い違っていたため、実効値へ是正される):

```csharp
// 各系統の上限。実体側 (PostEffects.Plugin) のシェーダーバッファ上限に従う
public const int MaxParaffinCount = PEP.TimelineBridge.MaxParaffinCount;
public const int MaxDistanceFogCount = PEP.TimelineBridge.MaxDistanceFogCount;
public const int MaxRimlightCount = PEP.TimelineBridge.MaxRimlightCount;
```

5. count プロパティは直前のリファクタで入れた standalone 値との二重化をやめ、実データ数へ委譲する (実体が常に存在するため standalone 値が不要になる。タイムライン読込中の timeline 同期は維持):

```csharp
/// <summary>パラフィン数。実体は PostEffects.Plugin 側が所有する。
/// タイムライン読込中は timeline 側 (TimelineXml に保存) と同期する</summary>
public int paraffinCount
{
    get => PEP.TimelineBridge.paraffinCount;
    set
    {
        PEP.TimelineBridge.paraffinCount = value;
        if (timeline != null)
        {
            timeline.paraffinCount = value;
        }
    }
}
```

(distanceFogCount / rimlightCount も同形。`_standaloneParaffinCount` 等 3 フィールドは削除)

6. `InitPostEffects()` は「timeline 読込時に timeline 側の count を実体へ反映する」だけにする:

```csharp
public void InitPostEffects()
{
    if (timeline != null)
    {
        // TimelineXml 読込で timeline 側だけ変わった場合に実体数を追随させる
        PEP.TimelineBridge.paraffinCount = timeline.paraffinCount;
        PEP.TimelineBridge.distanceFogCount = timeline.distanceFogCount;
        PEP.TimelineBridge.rimlightCount = timeline.rimlightCount;
    }
}
```

7. Get/Apply/Add/Remove/Count は素通し委譲。パラフィンの例 (フォグ・リムライトも同形):

```csharp
public int GetParaffinCount() => PEP.TimelineBridge.paraffinCount;
public void AddParaffinData() => PEP.TimelineBridge.paraffinCount++;
public void RemoveParaffinData() => PEP.TimelineBridge.paraffinCount--;
public PEP.ColorParaffinData GetParaffinData(int index)
    => PEP.TimelineBridge.GetParaffinData(index);
public void ApplyParaffin(int index, PEP.ColorParaffinData data)
    => PEP.TimelineBridge.ApplyParaffin(index, data);
```

8. DoF は DTO 変換を挟む。maidSlotNo は PEP 側の maidFocus/maidIndex へ写像:

```csharp
public DepthOfFieldData GetDepthOfFieldData()
{
    var setting = PEP.TimelineBridge.GetDepthOfField();
    return new DepthOfFieldData
    {
        enabled = setting.enabled,
        focalLength = setting.focalLength,
        focalSize = setting.focalSize,
        aperture = setting.aperture,
        maxBlurSize = setting.maxBlurSize,
        maidSlotNo = setting.maidFocus ? setting.maidIndex : -1,
    };
}

public void ApplyDepthOfField(DepthOfFieldData data)
{
    var setting = PEP.TimelineBridge.GetDepthOfField();
    setting.enabled = data.enabled;
    setting.focalLength = data.focalLength;
    setting.focalSize = data.focalSize;
    setting.aperture = data.aperture;
    setting.maxBlurSize = data.maxBlurSize;
    setting.maidFocus = data.maidSlotNo >= 0;
    setting.maidIndex = data.maidSlotNo >= 0 ? data.maidSlotNo : 0;
    PEP.TimelineBridge.ApplyDepthOfField(setting);
    studioHack.OnUpdateDepthOfField();
}
```

(旧実装が config から書いていた visualizeFocus / highResolution / nearBlur は PostEffects 側 UI の管轄になるため書かない。旧実装のメイド頭 Transform 解決も PEP 側の GetMaidHeadTransform に委ねる)

9. GTToneMap も DTO 変換 (フィールド 1:1 コピー。実フィールド名は Step 1 で控えたものに合わせる):

```csharp
public GTToneMapData GetGTToneMapData()
{
    var setting = PEP.TimelineBridge.GetGTToneMap();
    return new GTToneMapData
    {
        enabled = setting.enabled,
        maxBrightness = setting.maxBrightness,
        contrast = setting.contrast,
        linearStart = setting.linearStart,
        linearLength = setting.linearLength,
        blackTightness = setting.blackTightness,
        blackOffset = setting.blackOffset,
    };
}

public void ApplyGTToneMap(GTToneMapData data)
{
    var setting = PEP.TimelineBridge.GetGTToneMap();
    setting.enabled = data.enabled;
    setting.maxBrightness = data.maxBrightness;
    setting.contrast = data.contrast;
    setting.linearStart = data.linearStart;
    setting.linearLength = data.linearLength;
    setting.blackTightness = data.blackTightness;
    setting.blackOffset = data.blackOffset;
    PEP.TimelineBridge.ApplyGTToneMap(setting);
}
```

10. `DisableAllEffects()`:

```csharp
public void DisableAllEffects()
{
    PEP.TimelineBridge.paraffinEnabled = false;
    PEP.TimelineBridge.distanceFogEnabled = false;
    PEP.TimelineBridge.rimlightEnabled = false;

    var dof = PEP.TimelineBridge.GetDepthOfField();
    dof.enabled = false;
    PEP.TimelineBridge.ApplyDepthOfField(dof);

    var toneMap = PEP.TimelineBridge.GetGTToneMap();
    toneMap.enabled = false;
    PEP.TimelineBridge.ApplyGTToneMap(toneMap);
}
```

11. 旧 `paraffin.enabled` 等を直接読んでいた呼び出し元 (Step 1 の grep 結果) は、系統別に `paraffinEnabled` 等のプロパティを Manager に生やして置換する:

```csharp
public bool paraffinEnabled
{
    get => PEP.TimelineBridge.paraffinEnabled;
    set => PEP.TimelineBridge.paraffinEnabled = value;
}
```

(distanceFogEnabled / rimlightEnabled も同形)

12. `Init()` / `OnLoad()` / `OnPluginDisable()` 等ライフサイクルの旧実体生成・破棄コードを削除する。`OnPluginDisable` 相当では `DisableAllEffects()` を呼ぶだけにする

13. **このクラスの全メンバは「PostEffects 導入済み」を前提にする**。未導入ガードは呼び出し元 (Task 6) で行い、Manager 内には入れない (毎フレームパスに分岐を増やさない)

- [ ] **Step 3: UnityScripts/PostEffect を削除し csproj から除去**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git rm source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/PostEffect/*.cs
```

csproj から該当 11 ファイルの `<Compile Include="Timeline\UnityScripts\PostEffect\...">` を全て削除する。

- [ ] **Step 4: 参照元の型を PEP へ切り替える**

Step 1 で確定したファイルごとに、ファイル冒頭へ `using PEP = COM3D25.PostEffects.Plugin;` を追加し、`ColorParaffinData` / `DistanceFogData` / `RimlightData` の出現を `PEP.ColorParaffinData` 等へ置換する (同一ファイル内で MTEP 名前空間に居るため裸名は解決不能になり、ビルドエラーが置換漏れを検出してくれる)。`GTToneMapData` / `DepthOfFieldData` は MTEP の DTO のままなので触らない。

`PostEffectRowDrawer.cs` 内で旧 Settings 型 (`ColorParaffinEffectSettings` 等) や `isDebugView` に触れている箇所があれば、その行は削除する (デバッグ表示は PostEffects 側 UI の管轄に移る)。上限判定に `MaxParaffinCount` 等を使っている箇所はそのままで良い (Step 2 方針 4 で値が 4 へ是正される)。

- [ ] **Step 5: 孤児化するデバッグ設定を削除する**

Step 1 の grep で見つけた `Config` のフィールド (`paraffinDebug` / `distanceFogDebug` / `rimlightDebug` / `dofVisualizeFocus` / `dofHighResolution` / `dofNearBlur`) と、それを操作する設定 UI (トグル等) を削除する。書き込み先 (`isDebugView` 等) が PostEffects 側へ移り、操作しても何も起きない死に設定になるため。Config の XML 直列化で旧設定ファイルに残った属性は XmlSerializer が読み飛ばすので互換対応は不要。

- [ ] **Step 6: 両構成をビルド**

SceneEditor 側 MSBuild 2 本。Expected: 両方 Build succeeded。エラーが出た箇所は置換漏れなので Step 4 の方針で潰す。

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "refactor(posteffect): ポストエフェクト実体を PostEffects.Plugin へ委譲する"
```

---

### Task 6: 未導入時の無効化 (レイヤー登録・UI のゲート)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` (PostEffectTimelineLayer の RegisterLayer / RegisterTransform 箇所)
- Modify: `PostEffectRowDrawer.cs` の呼び出し元ウィンドウ (Step 1 で確定)

**Interfaces:**
- Consumes: Task 3 の `PostEffectsBridge.isAvailable`
- Produces: なし (挙動ゲートのみ)

- [ ] **Step 1: 登録箇所と UI 呼び出し元を確定する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "PostEffectTimelineLayer" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
grep -rn "RegisterTransform" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs | grep -i "paraffin\|fog\|rimlight\|tonemap\|depth\|posteffect"
grep -rn "PostEffectRowDrawer" source/COM3D2.SceneEditor.Plugin --include='*.cs' | grep -v "PostEffectRowDrawer.cs:"
```

- [ ] **Step 2: レイヤー・Transform 登録をゲートする**

`TimelineIntegration` の該当箇所を以下の形で囲む (`Initialize` はプラグインロード完了後に呼ばれるため、ここでの `isAvailable` 初回参照が PostEffects 側の初期化順を乱すことはない):

```csharp
// PostEffects.Plugin 未導入時はポストエフェクトの実体が無いため、レイヤーごと外す。
// 該当レイヤー入りのタイムライン XML は未登録レイヤーとして読み飛ばされる (既存挙動)
if (PostEffectsBridge.isAvailable)
{
    timelineManager.RegisterLayer(
        typeof(MTEP.PostEffectTimelineLayer), MTEP.PostEffectTimelineLayer.Create);
    // Step 1 で見つけたポストエフェクト系の RegisterTransform もこのブロック内へ移す
}
```

- [ ] **Step 3: UI をゲートする**

`PostEffectRowDrawer` を描画しているウィンドウ側で、描画前に:

```csharp
if (!PostEffectsBridge.isAvailable)
{
    _view.DrawLabel("PostEffects.Plugin が見つかりません。導入するとポストエフェクトを使用できます",
        -1, ROW_HEIGHT, textColor: Color.gray);
    return;
}
```

(呼び出し元の view 変数名・行高定数は実コードに合わせる)

- [ ] **Step 4: 未登録レイヤー入り XML の読込挙動を確認する**

`TimelineManager.CreateLayer` (1558 行付近) が未登録レイヤーで `MTEUtils.LogError("未登録のレイヤークラス: ...")` を出して null を返し、呼び出し元が null で継続する (読込全体が失敗しない) ことはレビューで実コード確認済み。挙動が変わっていないことを目視で再確認するのみで良い。

- [ ] **Step 5: 両構成をビルド → コミット**

MSBuild 2 本 → Build succeeded を確認。

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin
git commit -m "feat(posteffect): PostEffects.Plugin 未導入時はレイヤーと UI を無効化する"
```

---

### Task 7: ドキュメント更新 (スペック状態・ビルド前提)

**Files:**
- Modify: `docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md`
- Modify: `CLAUDE.md` (SceneEditor リポジトリ直下。ビルド前提の追記)

- [ ] **Step 1: スペック末尾に実装状態と実機チェックリストを追記**

スペックの「テスト・検証」節の実機確認項目の先頭に以下を追記する:

```markdown
> 実装済み (2026-08-30、実装計画: `docs/superpowers/plans/2026-08-30-posteffects-timeline-integration.md`)。
> 以下は次回ゲーム起動時の実機確認チェックリスト。
```

- [ ] **Step 2: ビルド前提を CLAUDE.md に追記**

SceneEditor リポジトリの `CLAUDE.md` のビルド関連の節に追記 (該当節が無ければビルドコマンド記載箇所の近くに追加):

```markdown
- **ビルド前提**: `COM3D25.PostEffects.Plugin.dll` が両ゲームフォルダ
  (`W:\COM3D2\Sybaris\UnityInjector\` / `W:\COM3D2_5\Sybaris\UnityInjector\`) に
  デプロイ済みであること。SceneEditor はこの DLL を直接参照するため、
  未配置だと SceneEditor に無関係な変更でもビルドが通らない
```

- [ ] **Step 3: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md CLAUDE.md
git commit -m "docs: PostEffects タイムライン連携の実装状態とビルド前提を追記する"
```

---

## 補足 (実装者向けメモ)

- **API 名の食い違いは実物優先**: 本計画のコードは静的調査に基づく。Task 1 Step 1 / Task 5 Step 1 の事前確認で実名が違った場合、計画のコードを実名へ合わせて読み替える (`settings.dirty`、`RemoveDataLast`、GTToneMapData のフィールド名が主な候補。レビュー時点で dirty / DepthOfFieldSetting / GTToneMapSetting / RemoveDataLast / AddData / GetData は現物一致を確認済み)
- **参照渡しの罠**: `TimelineBridge.GetGTToneMap()` / `GetDepthOfField()` は settings 内の実インスタンスを返す。SceneEditor 側は取得した参照へ書いてから同じ参照を Apply へ渡す設計 (Task 5 Step 2 のコード) で成立するが、「Get だけして Apply しない」書き換えは dirty が立たないので行わないこと
- **上限値の是正**: SceneEditor 旧定数 `MaxParaffinCount = 8` / `MaxRimlightCount = 8` は実体側シェーダーバッファ上限 (4) と食い違っており、元々 5 個目以降は機能していなかった。委譲後は PEP 側定数 (4) に統一され、UI の追加ボタン活性化条件も実効上限に一致する。タイムライン XML に 5 個以上の count が保存されていた場合は 4 に丸まる (実害なし: 元々描画されていない)
- **counts の standalone 値廃止**: 直前のリファクタ (feature/timeline-window の 0616f09) で入れた `_standalone*Count` は、実体が PostEffects 側に常在するようになるため不要になる。TextWindow 側 (`TimelineTextManager.textCount`) は本計画のスコープ外でそのまま
- **PostEffects 側のプリセットとの整合**: シーンプリセット適用時はサイドカー (`PostEffectsScenePresetProvider.ApplyPresetXml`) が全エフェクトを復元し、タイムライン読込中ならその後の再生でキーフレーム持ちの 5 系統が上書きされる。既存の「適用はする・再生側が優先」規約のままで調停は入れない
- **`AddParaffinData()` の既定値差**: 旧実装は `new ColorParaffinData()` (enabled=false) を追加していた。TimelineBridge の count セッターも同じ既定値で追加するため挙動互換。一方 PostEffects の UI (「追加」ボタン) は `enabled = true` で足すが、これは PostEffects 側の既存挙動でありスコープ外
- **isAvailable の初回参照タイミング**: `PostEffectsBridge.isAvailable` の初回参照は `EffectSettings.instance` の生成 (PostEffects 側の初期化) を誘発する。初回参照は `TimelineIntegration.Initialize` (プラグインロード完了後) 以降に限ること。それより早い静的初期化子等から触らない
- **ゲームフォルダの DLL 前提**: SceneEditor のビルドは `$(GameDir)\Sybaris\UnityInjector\COM3D25.PostEffects.Plugin.dll` の存在が前提になる。CI や新環境では PostEffects.Plugin を先にデプロイしておくこと (Task 3 Step 1 の確認を飛ばさない。Task 7 で CLAUDE.md にも明記する)
- **実機確認チェックリスト** (次回ゲーム起動時): 併用時の二重掛かり解消 / タイムライン再生で PostEffects 値が動きタイムラインタブに反映 / DLL を外して起動 → レイヤー・UI 無効化と警告ログ 1 回 / ポストエフェクトレイヤー入り旧 XML の読込継続 / シーンプリセットの演出カテゴリとサイドカーの独立動作 / DoF メイド追従の写像 (maidSlotNo ↔ maidFocus+maidIndex) が保存・復元で往復すること / 5 個以上の count を持つ旧タイムライン XML が 4 に丸まって読めること

## レビュー却下メモ

- (却下なし。plan-reviewer の指摘 🔴1 件・🟡4 件はすべて取り込み済み: タスク順入れ替え (旧 Task 6 → Task 4)、上限定数 8→4 の是正、デバッグ設定の孤児化対応 (Task 5 Step 5)、ビルド前提のドキュメント化 (Task 7 Step 2)、isAvailable 初期化タイミングの規約化)
