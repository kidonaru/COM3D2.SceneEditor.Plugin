# タイムライン カメラレイヤー移植 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の `CameraTimelineLayer` / `TransformDataCamera` を SceneEditor の Timeline 層へ移植し、タイムラインでカメラのキーフレーム再生を可能にする（ロードマップ Phase 3 項目 1）。

**Architecture:** 既存の Timeline/ 移植規約に従い、MTE ソースをほぼそのままコピーし、SE で未移植の依存（DCM 出力・StudioModelManager）だけを削る。レイヤー登録は `TimelineIntegration.Initialize` に追記する。UI（DrawWindow）は MotionTimelineLayer と同じく「移植するが呼び出し経路は未接続」の状態を維持する。

**Tech Stack:** C# (.NET 3.5, 旧形式 csproj), IMGUI, MTE Timeline コア（移植済み）

**Spec:** `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 3 項目 1「カメラレイヤー: ボディ非依存で最も安全」）

## Global Constraints

- 移植ファイルは名前空間 `COM3D2.MotionTimelineEditor.Plugin` を維持する（Timeline/ 配下の規約）
- 旧形式 csproj のため新規ファイルは必ず `<Compile Include>` をアルファベット順の位置に追加する
- テスト基盤なし。検証はビルド（MSBuild 直接実行、debug.bat は使わない）＋必要に応じ devbridge 実機確認
- MTE 由来コードとの diff を最小化する（意図的な削除には日本語コメントで理由を残す）
- DCM 連携（OutputDCM / OutputBones / OutputMotions）は未移植方針のため持ち込まない
- ビルドコマンド:
  `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"`
  （実機へコピーしないよう MSBuild を直接叩く）

---

### Task 1: TransformDataCamera の移植

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCamera.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`TransformDataBase.cs` の行の直後、`TransformDataExtendBone.cs` の前）

**Interfaces:**
- Consumes: `TransformDataBase`（移植済み）、`TransformType.Camera`（`ITransformData.cs:14` に定義済み）
- Produces: `TransformDataCamera`（valueCount=10, position=values[0..2], eulerAngles=values[3..5], easing=values[6], scale=values[7..9]=距離/FoV/ダミー, initialScale=(1,35,0)）。Task 2 の `CreateTransformData<TransformDataCamera>` と Task 3 の `RegisterTransform` が参照する

- [ ] **Step 1: MTE からファイルをコピー**

MTE `TransformData/TransformDataCamera.cs`（44 行）を変更なしでそのままコピーする（namespace も同一のため無改変）:

```
cp W:/COM3D2_5/work/COM3D2.MotionTimelineEditor.Plugin/source/COM3D2.MotionTimelineEditor.Plugin/TransformData/TransformDataCamera.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCamera.cs
```

- [ ] **Step 2: csproj に Compile Include を追加**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Timeline\TransformData\TransformDataBase.cs" />`（現 337 行付近）の直後に追加:

```xml
    <Compile Include="Timeline\TransformData\TransformDataCamera.cs" />
```

- [ ] **Step 3: ビルドして成功を確認**

Global Constraints のビルドコマンドを実行し、エラー 0 を確認する。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataCamera.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): TransformDataCamera を移植"
```

### Task 2: CameraTimelineLayer の移植（DCM 出力・対象モデル UI を削除）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Timeline\TimelineLayer\ITimelineLayer.cs` の行の前）

**Interfaces:**
- Consumes: `TimelineLayerBase`（`CalcEasingValue` / `CreateTransformData<T>` / `GetPrevBone` / `GetEasing`）、`TransformDataCamera`（Task 1）、`PluginUtils.MainCamera` / `PluginUtils.HermiteVector3`、`UltimateOrbitCamera` 拡張（`MTEUtils/Extensions.cs` に移植済み）、`config.isCameraSync/isFixedFoV/isFixedFocus/positionRange`（`Timeline/Config.cs:59-70` 定義済み）、`MaidCache.GetPointTransform(MaidPointType)`
- Produces: `CameraTimelineLayer`（`[TimelineLayerDesc("カメラ", 20)]`、`layerName == nameof(CameraTimelineLayer)`、`static Create(int slotNo)`）。Task 3 の `RegisterLayer` が参照する

- [ ] **Step 1: MTE からコピーして SE 未移植依存を削る**

MTE `TimelineLayer/CameraTimelineLayer.cs`（566 行）をコピーし、以下だけを変更する:

1. **DCM 出力の削除**: `OutputBones(...)`（MTE L181-228）と `OutputMotions(...)`（L230-300）を丸ごと削除し、`OutputDCM` override（L302-328）を MotionTimelineLayer と同じ空スタブに置換:

```csharp
        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }
```

2. **対象モデル UI の削除**: `_modelComboBox` フィールド（MTE L344-349）、`DrawWindow` 内の `focusToModel` アクションと「対象モデル」行（L527-554）を削除し、削除位置に理由コメントを残す:

```csharp
            // StudioModelManager は未移植のため「対象モデル」フォーカスは提供しない
```

3. **不要 using の削除**: `System.IO`（StreamWriter）と `System.Text`（StringBuilder）は OutputBones/OutputMotions 削除で完全に不要になるため削除する。`System.Linq` は `_targetMaidPointComboBox` の `.Cast<MaidPointType>()` で使うため残す。

他は一切変更しない（`Update` / `ApplyPlayData` / `ApplyMotion` / `GetUOCamera` / `UpdateFrame` / スライダー 8 本と対象メイド・対象ポイント UI・`GetTransformType` は原文のまま）。

- [ ] **Step 2: csproj に Compile Include を追加**

`<Compile Include="Timeline\TimelineLayer\ITimelineLayer.cs" />`（現 329 行付近）の直前に追加:

```xml
    <Compile Include="Timeline\TimelineLayer\CameraTimelineLayer.cs" />
```

- [ ] **Step 3: ビルドして成功を確認**

Global Constraints のビルドコマンドを実行し、エラー 0 を確認する。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): CameraTimelineLayer を移植 (DCM 出力・対象モデル UI は削除)"
```

### Task 3: レイヤー登録と hasCameraLayer の実装

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:132-133` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:89-90` 付近

**Interfaces:**
- Consumes: `CameraTimelineLayer.Create`（Task 2）、`TransformDataCamera`（Task 1）、`TimelineManager.RegisterLayer` / `RegisterTransform` / `FindLayers(Type)`
- Produces: XML の `className="CameraTimelineLayer"` 復元（`TimelineData.cs:903` の `CreateLayer(layerXml.className, ...)` 経由）と `timelineManager.hasCameraLayer` の実値

- [ ] **Step 1: TimelineIntegration にレイヤー・Transform 登録を追加**

`TimelineIntegration.cs` の `RegisterLayer(typeof(MTEP.MotionTimelineLayer), ...)` の直後に追加:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.CameraTimelineLayer), MTEP.CameraTimelineLayer.Create);
```

`RegisterTransform` 群のアルファベット順の位置（`ExtendBone` の前）に追加:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.Camera,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataCamera>);
```

- [ ] **Step 2: hasCameraLayer を MTE 準拠の実装に戻す**

`TimelineManager.cs` の

```csharp
        // カメラ・ポストエフェクトレイヤーは未移植のため常に false
        public bool hasCameraLayer => false;
```

を以下に置換し、「ポストエフェクトレイヤーは未移植のため常に false」コメントは `hasPostEffectLayer` プロパティの直前に移す:

```csharp
        public bool hasCameraLayer
        {
            get => FindLayers(typeof(CameraTimelineLayer)).Count > 0;
        }

        // ポストエフェクトレイヤーは未移植のため常に false
        public bool hasPostEffectLayer => false;
```

（`FindLayers(Type)` は SE 移植済み・利用実績あり）

置換前に `hasCameraLayer` の呼び出し元を grep し、「常に false」前提に依存する分岐がないことを確認する。

- [ ] **Step 3: ビルドして成功を確認**

Global Constraints のビルドコマンドを実行し、エラー 0 を確認する。

- [ ] **Step 4: 実機確認（ゲーム起動中のみ）**

devbridge の `ping` が通る場合のみ、ホットリロードはせずビルド成果のみで以下を静的確認する（実機反映はユーザー操作に委ねる）。ゲーム未起動ならスキップして良い。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "feat(timeline): カメラレイヤーを登録し hasCameraLayer を実装"
```

### Task 4: ロードマップ進捗の記録

**Files:**
- Modify: `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 3 セクション）

**Interfaces:**
- Consumes: なし
- Produces: なし（ドキュメントのみ）

- [ ] **Step 1: Phase 3 項目 1 に完了注記を追加**

「1. **カメラレイヤー**: …」の行末に ` — **移植完了（2026-08-23）**。DCM 出力・対象モデル UI は未移植方針に合わせ削除。レイヤー UI（DrawWindow）の呼び出し経路接続は今後の課題` を追記する。

- [ ] **Step 2: コミット**

```bash
git add docs/superpowers/specs/timeline-window-roadmap.md
git commit -m "docs(timeline): カメラレイヤー移植の完了を記録"
```

## スコープ外（明示）

- レイヤー UI ホスト（`DrawWindow` の呼び出し経路）: SE には MTE の `TimelineLayerUI` 相当が未実装で、MotionTimelineLayer も同状態。別タスクで扱う
- SubCamera レイヤー、CameraManager（フロントカメラ / LetterBoxView）: CameraTimelineLayer は参照しないため不要
- ライトレイヤー以降（Phase 3 項目 2〜4）
