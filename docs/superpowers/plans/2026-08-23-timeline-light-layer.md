# タイムライン ライトレイヤー移植 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の `LightTimelineLayer` / `TransformDataLight` を SceneEditor へ移植し、タイムラインでライト（メイン + 追加ライト）のキーフレーム再生を可能にする（ロードマップ Phase 3 項目 2）。

**Architecture:** MTE の StudioLightManager は写真モード専用の Hack 経路（lightHackManager → studio.lightWindow）に依存するため**そのまま移植できない**。MTE と同じ公開サーフェス（lights / GetLight / ApplyLight / イベント群）を持つアダプタ版 `Timeline/Manager/StudioLightManager.cs` を新規実装し、バックエンドを SE ネイティブの `COM3D2.SceneEditor.Plugin.StudioLightManager`（AddLight / RemoveLight / SetLightType）に差し替える。レイヤー・TransformData・StudioLightStat は MTE から diff 最小で移植する。stat のインデックス 0 は常にメインライト（GameMain.Instance.MainLight）、1 以降が SE 追加ライト。

**Tech Stack:** C# (.NET 3.5, 旧形式 csproj), IMGUI, MTE Timeline コア（移植済み）

**Spec:** `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 3 項目 2「ライトレイヤー: SceneEditor の StudioLightManager に接続」）

## Global Constraints

- 移植ファイルは名前空間 `COM3D2.MotionTimelineEditor.Plugin` を維持（SE ネイティブの同名 `StudioLightManager` とは名前空間で区別。境界ファイルではエイリアス `MTEP.` / `SE.` を使用）
- 旧形式 csproj のため新規ファイルは `<Compile Include>` をアルファベット順の位置に追加
- DCM 連携（OutputDCM 実体 / OutputMotions）は持ち込まない（空スタブ、前例: MotionTimelineLayer / CameraTimelineLayer）
- `isLightCompatibilityMode`（写真モードの LightPoint/LightSpot 子プレハブ互換）は SE のライトに該当構造がないため **機能としては no-op**。XML フィールドは互換のため残す
- SE 版 GUIView には `IsComboBoxFocused()` がない。出現したら `view.focusedComboBox == null` に置換（前例: CameraTimelineLayer）
- テスト基盤なし。検証はビルド（MSBuild 直接、両 GameVersion）
- ビルドコマンド（Git Bash から。パス変換抑止が必要）:
  `cd source/COM3D2.SceneEditor.Plugin && MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj '/p:Configuration=Debug' '/p:GameVersion=COM3D25' '/p:COM3D2_DIR=W:\COM3D2' '/p:COM3D25_DIR=W:\COM3D2_5' /nologo /v:minimal`
  （GameVersion=COM3D2 でも同様に確認）

---

### Task 1: TransformDataLight の移植

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`TransformDataIKHold.cs` の直後、`TransformDataRoot.cs` の前）

**Interfaces:**
- Consumes: `TransformDataBase` / `ValueData` / `CustomValueInfo` / `TransformType.Light`（すべて移植済み）
- Produces: `TransformDataLight`（valueCount=18、position/rotation/color/visible/easing/tangent、CustomValue: range/intensity/spotAngle/shadowStrength/shadowBias/maidSlotNo と各型付きプロパティ）。Task 4 のレイヤーと Task 5 の RegisterTransform が参照

- [ ] **Step 1: MTE から無改変コピー**

```
cp W:/COM3D2_5/work/COM3D2.MotionTimelineEditor.Plugin/source/COM3D2.MotionTimelineEditor.Plugin/TransformData/TransformDataLight.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs
```

- [ ] **Step 2: csproj に追加**

```xml
    <Compile Include="Timeline\TransformData\TransformDataLight.cs" />
```

- [ ] **Step 3: ビルド確認（COM3D25）**
- [ ] **Step 4: コミット** `feat(timeline): TransformDataLight を移植`

### Task 2: StudioLightStat の完全版移植（スタブ置換）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioStats.cs`（スタブ `StudioLightStat` クラスを削除、ヘッダコメントの「ライト」言及を「モデル」のみに修正）
- Modify: csproj（`Timeline\StudioStats.cs` の直前 = アルファベット順で StudioLightStat < StudioStats）

**Interfaces:**
- Consumes: `MaidManager.instance` / `MaidCache` / `StudioHackManager.instance.studioHack` / `PluginUtils.GetGroupSuffix(int)`（移植済み。存在しない場合は MTE の `PluginUtils.GetGroupSuffix` を SE 版 `Timeline/PluginUtils.cs` に追随移植する）/ `GetOrAddComponent<T>` 拡張（MTEUtils/Extensions.cs）
- Produces: `MaidFollowLight`（MonoBehaviour: メイド Pelvis 追従）と完全版 `StudioLightStat`（type/visible/light/transform/obj/index/name/displayName/typeOrder/followLight/position/rotation/InitName/FromStat/Clone、`LightTypeNames` 辞書）。`TimelineData.TimelineLightData.FromStat` はこの型を受けるためソース互換

- [ ] **Step 1: MTE `StudioLightStat.cs`（222 行）を `Timeline/StudioLightStat.cs` へ無改変コピー**

`obj` フィールドは写真モード由来だが unused-harmless のため diff 最小化を優先し**残す**。

- [ ] **Step 2: `Timeline/StudioStats.cs` からスタブを削除**

```csharp
    public class StudioLightStat
    {
        public string name { get; set; }
        public LightType type { get; set; } = LightType.Directional;
    }
```
を削除し、ヘッダコメントを「MTE の StudioModelStat.cs の最小移植。モデル管理 (StudioModelManager 等) は未移植のため〜」に更新（ライトは完全版 StudioLightStat.cs へ移行した旨を一行追記）。

- [ ] **Step 3: csproj に追加、ビルド確認（COM3D25）**

`PluginUtils.GetGroupSuffix` 不在でエラーになる場合は MTE `PluginUtils.cs` から該当メソッドを SE `Timeline/PluginUtils.cs` へ追加する。

- [ ] **Step 4: コミット** `feat(timeline): StudioLightStat を完全版へ置換 (MaidFollowLight 含む)`

### Task 3: アダプタ版 StudioLightManager の実装

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ManagerBase.cs`（`lightManager` アクセサ追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs`（`lightManager` アクセサ追加）
- Modify: csproj（`Timeline\Manager\StudioHackManager.cs` の直後）

**Interfaces:**
- Consumes: SE ネイティブ `COM3D2.SceneEditor.Plugin.StudioLightManager`（`lights : List<Light>` / `AddLight()` / `RemoveLight(Light)` / `SetLightType(Light, LightType)` / `mainLight : LightMain`）、`TimelineLightData`（Timeline/TimelineData.cs:62）、`timelineManager.RequestHistory(string)`（存在を実装時に確認。無ければ履歴呼び出しは行わない）
- Produces: MTE 互換サーフェスの `COM3D2.MotionTimelineEditor.Plugin.StudioLightManager`: `lights` / `lightNames` / `GetLight(string|int)` / `LateUpdate(bool force)` / `SetupLights(List<TimelineLightData>)` / `CanCreateLight()` / `CreateLight/DeleteLight/ChangeLight/ApplyLight(StudioLightStat)` / `Reset()` / static `onLightAdded/onLightRemoved/onLightUpdated`。Task 4 のレイヤーと Task 5 の統合が参照

- [ ] **Step 1: MTE `Manager/StudioLightManager.cs`（303 行）をベースにアダプタを作成**

MTE 版をコピーし、`lightHackManager` への参照を以下の内部実装に置換する（キャッシュ diff・イベント発火・UpdateTimelineLights・SetupLights の骨格は MTE のまま維持）:

```csharp
        // SE ネイティブのライト管理（実体の生成・破棄・種別変更を担当）
        private static SceneEditor.Plugin.StudioLightManager seLightManager
            => SceneEditor.Plugin.StudioLightManager.instance;

        // 写真モードの lightHackManager 相当。index 0 はメインライト、1 以降が追加ライト。
        // メインライトが取得できないシーンでは空リストを返し、
        // 「index 0 = メインライト」の不変条件を崩さない（追加ライトの index ずれ・誤削除ガードを防ぐ）
        private List<StudioLightStat> BuildLightList()
        {
            var result = new List<StudioLightStat>();

            var mainLight = seLightManager.mainLight;
            var mainLightComponent = mainLight != null ? mainLight.GetComponent<Light>() : null;
            if (mainLightComponent == null)
            {
                return result;
            }
            result.Add(new StudioLightStat(mainLightComponent, mainLightComponent.transform, null, 0));

            foreach (var light in seLightManager.lights)
            {
                if (light == null)
                {
                    continue;
                }
                result.Add(new StudioLightStat(light, light.transform, null, result.Count));
            }

            return result;
        }
```

置換規則（MTE 原本 → アダプタ）:
- `lightHackManager.lightList` → `BuildLightList()`（`LateUpdate` 内・`SetupLights` 内）
- `lightHackManager.CreateLight(stat)` → 追加ライトを生成して種別を適用:
  ```csharp
            var newLight = seLightManager.AddLight();
            seLightManager.SetLightType(newLight, stat.type);
  ```
- `lightHackManager.DeleteLight(stat)` → メインライトは削除不可のためガード:
  ```csharp
            if (stat.index <= 0 || stat.light == null)
            {
                return;
            }
            seLightManager.RemoveLight(stat.light);
  ```
  （`DeleteLight(StudioLightStat)` 公開メソッド側のガードとして実装）
- `lightHackManager.ChangeLight(newStat)` → `seLightManager.SetLightType(newStat.light, newStat.type)`（メインライト index 0 は種別変更不可としてガード）
- `lightHackManager.ApplyLight(stat)` → `stat.light.enabled = stat.visible;`（位置・回転・色・強度等はレイヤーが stat 経由で直接書くため、ここでは可視状態のみ反映）
- `lightHackManager.CanCreateLight()` → `return true;`
- `lightHackManager.DeleteAllLights()` → `seLightManager.ClearAll();`
- `lightHackManager.SetLightCompatibilityMode(...)` → 呼び出しごと削除し、コメントを残す: `// SE のライトに写真モードの互換プレハブ構造はないため isLightCompatibilityMode は no-op`
- `MaidFollowLight` の生成先である `stat.transform` は追加ライトの GameObject transform（SE 側と同一実体）
- `timelineManager.RequestHistory(...)` は SE 版 TimelineManager に存在確認済み（TimelineManager.cs:1449）のためそのまま使う。`currentLayer.isAnmPlaying` も TimelineLayerBase.cs:69 に存在確認済み

`OnLoad` / `Reset` / `OnChangedSceneLevel` / `UpdateTimelineLights` / イベント発火ループは MTE 原本のまま。

- [ ] **Step 2: `Timeline/Manager/ManagerBase.cs` と `Timeline/TimelineLayer/TimelineLayerBase.cs` に共通アクセサを追加**

MTE 原本と同じ形（両ファイルの他マネージャアクセサ群の並びに合わせる）:

```csharp
        protected static StudioLightManager lightManager => StudioLightManager.instance;
```

- [ ] **Step 3: csproj に追加、ビルド確認（COM3D25）**
- [ ] **Step 4: コミット** `feat(timeline): SE ライト管理に接続するアダプタ版 StudioLightManager を実装`

### Task 4: LightTimelineLayerBase / LightTimelineLayer の移植

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayerBase.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs`
- Modify: csproj（`CameraTimelineLayer.cs` の直後に LightTimelineLayer.cs → LightTimelineLayerBase.cs をアルファベット順で。Light… < MotionTimelineLayer）

**Interfaces:**
- Consumes: `TimelineLayerBase` / `lightManager`（Task 3）/ `TransformDataLight`（Task 1）/ `StudioLightStat`（Task 2）/ `timeline.isTangentLight` ほか 4 フラグ（移植済み）
- Produces: `LightTimelineLayer`（`[TimelineLayerDesc("ライト", 41)]`、`layerName == nameof(LightTimelineLayer)`、`static Create(int slotNo)`）。Task 5 の RegisterLayer と XML `className` 復元が参照

- [ ] **Step 1: `LightTimelineLayerBase.cs`（98 行）をコピーし互換モード呼び出しを除去**

`DrawLightManage` 内の `studioHack.SetLightCompatibilityMode(newValue)` 呼び出し行を削除し、トグル自体は `timeline.isLightCompatibilityMode` の読み書きとして残す（XML 互換維持）。削除位置にコメント: `// SE では互換モードの実体がないためフラグの保存のみ行う`。

- [ ] **Step 2: `LightTimelineLayer.cs`（738 行）をコピーし SE 未移植依存を除去**

1. DCM 出力の削除: `OutputMotions(...)`（MTE L343-430）を削除し、`OutputDCM`（L432-454）を空スタブに置換:
   ```csharp
        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }
   ```
2. `view.IsComboBoxFocused()` を `view.focusedComboBox == null` 判定へ置換（コメント付き、CameraTimelineLayer と同文）。出現箇所: LightTimelineLayer.cs に 3 箇所（L504 / L523 / L730）、LightTimelineLayerBase.cs に 3 箇所（L36 / L78 ×2）。全 6 箇所を漏れなく置換する
3. 上記以外は変更しない（ApplyMotion 3 分岐・イベント購読・UpdateFrame・DrawWindow の操作/管理タブは原文のまま）

- [ ] **Step 3: csproj に 2 行追加、ビルド確認（COM3D25）**

コンパイルエラーが出た場合は SE 版 GUIView / MTEUtils との API 差分（CameraTimelineLayer で対応した類）に限定して最小修正し、修正箇所へ理由コメントを残す。

- [ ] **Step 4: コミット** `feat(timeline): LightTimelineLayer を移植 (DCM 出力は削除)`

### Task 5: 統合（登録・更新ループ・OnLoad）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`

**Interfaces:**
- Consumes: Task 1〜4 の全成果物
- Produces: XML `className="LightTimelineLayer"` の復元、再生時のライト反映

- [ ] **Step 1: `_managers` 配列にアダプタ版 StudioLightManager を追加**

`TimelineUpdateManager` の `_managers` 配列（MTEP.TimelineManager.instance の後ろ）へ `MTEP.StudioLightManager.instance` を追加する。これにより Update/LateUpdate/OnLoad/OnPluginDisable/OnChangedSceneLevel が既存ループで駆動される。

- [ ] **Step 2: レイヤーと Transform を登録**

`Initialize` 内、CameraTimelineLayer 登録の直後:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.LightTimelineLayer), MTEP.LightTimelineLayer.Create);
```

`RegisterTransform` 群のアルファベット順の位置（IKHold の後、Root の前）:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.Light,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataLight>);
```

- [ ] **Step 3: ビルド確認（COM3D25 と COM3D2 の両方）**
- [ ] **Step 4: コミット** `feat(timeline): ライトレイヤーを登録しタイムライン更新ループへ接続`

### Task 6: ロードマップ進捗の記録

**Files:**
- Modify: `docs/superpowers/specs/timeline-window-roadmap.md`

- [ ] **Step 1: Phase 3 項目 2 の行末に追記**

` — **移植完了（2026-08-23）**。MTE の写真モード Hack 経路は使わず、SE ネイティブ StudioLightManager 接続のアダプタ版を実装。DCM 出力・互換モード実体は未移植方針に合わせ削除`

- [ ] **Step 2: コミット** `docs(timeline): ライトレイヤー移植の完了を記録`

## 主要リスク

| リスク | 対応 |
|---|---|
| `LightMain.GetComponent<Light>()` が null（メインライトの Light 実体の取得方法が想定と違う） | SE の `LightWindow.cs` / `Manager/History/LightSnapshot.cs` のメインライト操作コードを実装時に確認し、同じ取得経路に合わせる |
| `RequestHistory` / `currentLayer.isAnmPlaying` が SE 版 TimelineManager に無い | 実装時に grep で確認。無ければ該当行を削除（コメントで明記） |
| BuildLightList を毎回生成することで diff 判定（light/transform 参照比較）が常に更新扱いになる | 比較対象は Light/Transform の参照そのものなので実体が同じなら一致する。`obj` は常に null で一致。type は実ライトから読むため問題なし |
| メインライトへの visible=false 適用の副作用 | `light.enabled` の切替のみで、SE の LightWindow も同等操作を提供しているため許容 |
| `mainLight` が null のシーンで index 0 前提が崩れる | BuildLightList はメインライト不在時に空リストを返し不変条件を維持（🔴 指摘反映） |
| SE 側リストの中間要素削除で index シフトし、イベントが実変化とずれて発火 | MTE 原本の diff ロジック（index 位置比較）由来の既知の構造。実害は履歴ログ・イベント通知先のずれに限定され、UI 操作は末尾優先のため許容。将来問題化したら参照ベース diff へ改修 |

## スコープ外（明示）

- レイヤー UI ホスト（DrawWindow 呼び出し経路）: 既存レイヤーと同じく未接続のまま
- 写真モード互換モード（LightPoint/LightSpot プレハブ）: SE に該当構造なし
- Phase 3 項目 3〜4（モーションレイヤーは移植済み、表情・指レイヤーは次回以降）
