# Phase L2: モデル系（StudioModelManager 基盤 + 4 レイヤー） 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の StudioModelManager / ModelHackManager 基盤と Model 系 4 レイヤー（Model / ModelBone / ModelShapeKey / ModelMaterial）を SE に移植し、スタジオモデルをタイムライン制御できるようにする。

**Architecture:** SceneEdit には photo studio の objectManagerWindow が無いため、モデル生成は **MultipleMaidsHack 方式の直接ロード**（LoadGameModel / LoadMyRoomObject / LoadModObject。ImportCM / Resources 直接ロード、スタジオ非依存）を SE の SceneEditorHack に実装する。基盤クラス（StudioModelStat フル版・ModelBone/BlendShape/Material コントローラ・StudioModelManager・ModelHackManager）は MTE から逐語移植。既存の 31 行スタブ `Timeline/StudioStats.cs` の StudioModelStat はフル版で置換する。レイヤー 4 種は L1 と同じ定型（逐語コピー + DCM スタブ + focusedComboBox 適合 + 登録）。

**Tech Stack:** C# / net35 / 非 SDK csproj

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L2

## SE 側モデル管理の現状調査結果（計画の前提）

- SE に独自のモデル配置システムは無い。`ModelProviderHost` は外部プラグイン配置モデルの読み取り専用列挙（BoneEdit / ScenePreset 用）
- `Timeline/StudioStats.cs` に StudioModelStat の最小スタブ（31 行）が存在し、TimelineData の XML 互換のためだけに使われている → フル版で置換
- SE の `SceneEditorHack : StudioHackBase` にモデル系 API は無い（MTE 側 StudioHackBase の CreateModel 等 virtual は SE 移植時に削除されている可能性 → 実装時に確認し、無ければ virtual 群ごと復元）
- ゲーム側 `PhotoBGObjectData.Instantiate` は COM3D2.5 に存在し `_for25` プレハブへの自動フォールバックも持つ（Assembly-CSharp/PhotoBGObjectData.cs:165-180）
- タイムラインのモデル永続化は TimelineData.models（TimelineModelData）として既にデータ層にあり、`StudioModelManager.SetupModels` が再生時に生成を担う。ScenePreset との統合はスコープ外（将来課題としてロードマップに記載済み）

## Global Constraints

- ビルド: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`（debug.bat 禁止）
- 各タスク完了条件: ビルド成功 + `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` 全 PASS
- 移植定型（L1 で確立）: OutputDCM 空スタブ化（コメント「DCM 連携は未移植のため出力しない」）+ 関連 Output ヘルパー削除、`!view.IsComboBoxFocused()` → `view.focusedComboBox == null`、`view.DrawComboBox()` 行削除、未使用 using（System.IO/System.Text 等）除去
- MTE ソース（W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin）は読み取りのみ

---

### Task 1: モデル基盤クラスの移植（StudioModelStat フル版 + コントローラ群）

**Files:**
- Create: `Timeline/StudioModelStat.cs`（MTE 211 行）
- Create: `Timeline/ModelBoneController.cs` / `Timeline/BlendShapeController.cs` / `Timeline/ModelMaterial.cs` / `Timeline/ModelMaterialController.cs`（MTE 同名ファイル）
- Delete: `Timeline/StudioStats.cs` の `StudioModelStat` スタブ（StudioLightStat 等他クラスが同居していれば残す。ファイル全体がスタブならファイル削除）
- Modify: csproj

**Interfaces:**
- Produces: `StudioModelStat`（info/transform/obj/attachPoint 等フル API）、`ModelBone` / `ModelBlendShape` / `ModelMaterial`

- [ ] **Step 1: MTE からコピーし、StudioStats.cs のスタブと重複しないよう整理。ビルドエラー駆動で適合**
- [ ] **Step 2: ビルド + dotnet test PASS**
- [ ] **Step 3: コミット** `feat(timeline): モデル基盤クラスを移植 (StudioModelStat フル版)`

### Task 2: StudioModelManager + ModelHackManager の移植

**Files:**
- Create: `Timeline/Manager/StudioModelManager.cs`（806 行。OfficialObjectInfo 含む）
- Create: `Timeline/Manager/ModelHackManager.cs`（244 行）
- Create: `Timeline/Hack/ModelHackBase.cs`（IModelHack 定義。MTE Hack/ModelHackBase.cs）
- Modify: `Timeline/Hack/StudioHackBase.cs`（**方針決定: L1 の Light/BG 系はフラット virtual 追加だったが、モデル系は ModelHackManager が IModelHack 型を要求するため、MTE と同じ `IModelHack` 実装を採用**（ModelHackManager を逐語のまま保てる）。SE StudioHackBase にモデル系 API はこれまで存在しないため、`modelList`（abstract プロパティ）+ CreateModel / DeleteModel / DeleteAllModels / UpdateAttachPoint / SetModelVisible の virtual 群を MTE 版から **新規追加** する。modelManager プロパティも追加）
- Modify: `Timeline/TimelineLayer/TimelineLayerBase.cs` / `Timeline/Manager/ManagerBase.cs`（modelManager / modelHackManager プロパティを MTE と同形式で追加）
- Modify: `Timeline/TimelineIntegration.cs`（ManagerRegistry への登録が必要なら追加。StudioModelManager の LateUpdate 駆動を TimelineUpdateManager に接続）
- Modify: csproj

**Interfaces:**
- Consumes: Task 1 の StudioModelStat / コントローラ群
- Produces: `StudioModelManager.instance`（models / SetupModels / CreateModelStat / OnPluginDisable 等）、`ModelHackManager.instance`

- [ ] **Step 1: 逐語コピーし、SE に無い依存（backgroundCustomManager 等 MTE 固有 Config/Manager）はコンパイルエラー駆動で削除または簡略化。削除時は「未移植のため省略」コメントを残す**
- [ ] **Step 2: StudioModelManager の更新駆動（MTE では MTE 本体の LateUpdate から呼ばれる）を SE の TimelineUpdateManager に接続**
- [ ] **Step 3: ビルド + dotnet test PASS**
- [ ] **Step 4: コミット** `feat(timeline): StudioModelManager / ModelHackManager を移植`

### Task 3: SceneEditorHack にモデル生成を実装（MultipleMaidsHack 方式）

**Files:**
- Modify: `Timeline/Hack/SceneEditorHack.cs`（modelList / CreateModel / DeleteModel / DeleteAllModels / **UpdateAttachPoint** / SetModelVisible を override 実装。UpdateAttachPoint はモデルをメイドボーンへ追従させる機能で、MultipleMaidsHack:646 が参照実装）
- 参照実装: `COM3D2.MotionTimelineEditor_MultipleMaids.Plugin/MultipleMaidsHack.cs` の LoadGameModel(365-)/LoadMyRoomObject(454-)/LoadModObject(486-) と CreateModel(618-)

**Interfaces:**
- Consumes: ModelHackManager（StudioHackBase 経由で自動登録される場合は登録経路を確認）
- Produces: SE 環境で StudioModelType.{Prefab,Asset,MyRoom,Mod} のモデルを直接生成・削除できる Hack

- [ ] **Step 0: 事前確認**: `grep ImportCM /w/COM3D2_5/work/Assembly-CSharp/` で COM3D2.5 側の ImportCM ロード API（LoadSkinMesh_R 等）の存在とシグネチャを確認してからローダ移植に着手する
- [ ] **Step 1: MultipleMaidsHack のローダ 3 種を SceneEditorHack に移植（MultipleMaids 固有部分は除去。ImportCM / PhotoBGObjectData.Instantiate / MyRoomCustom 経路）。COM3D2.5 で API 差分があればコンパイルエラー駆動で適合し、解決しない場合は devbridge で実 API を確認**
- [ ] **Step 2: modelList プロパティ（自前生成したモデルの管理リスト）と DeleteModel / DeleteAllModels / SetModelVisible を実装**
- [ ] **Step 3: ビルド + dotnet test PASS。ゲーム起動中なら devbridge で LoadGameModel 相当をスモーク（バイト配列ロード + 生成 → Destroy）**
- [ ] **Step 4: コミット** `feat(timeline): SceneEditorHack にモデル生成を実装 (直接ロード方式)`

### Task 4: Model 系 4 レイヤーの移植

**Files:**
- Create: `Timeline/TimelineLayer/ModelTimelineLayer.cs`（418 行）/ `ModelBoneTimelineLayer.cs`（301 行）/ `ModelShapeKeyTimelineLayer.cs`（284 行）/ `ModelMaterialTimelineLayer.cs`（315 行）
- Create: `Timeline/TransformData/TransformDataModel.cs` / `TransformDataModelBone.cs` / `TransformDataModelShapeKey.cs` / `TransformDataModelMaterial.cs`
- Modify: `Timeline/TimelineIntegration.cs`（RegisterLayer ×4 + RegisterTransform ×4: Model / ModelBone / ModelShapeKey / ModelMaterial）
- Modify: csproj

- [ ] **Step 1: L1 定型どおり移植（レイヤーごとにビルド確認してよい）**
- [ ] **Step 2: ビルド + dotnet test PASS**
- [ ] **Step 3: コミット** `feat(timeline): Model 系 4 レイヤーを移植`（レイヤー単位の分割コミットでも可）

### Task 5: フィクスチャ + 実機スモーク + ロードマップ更新

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l2-model-layers.xml`（4 レイヤー + Models 要素入りの最小 XML。l1-layers.xml と同形式、Type は Model/ModelBone/ModelShapeKey/ModelMaterial）
- Modify: `docs/superpowers/specs/timeline-layers-roadmap.md`（L2 完了マーク + 実機確認状況）

- [ ] **Step 1: フィクスチャ作成、dotnet test 全 PASS**
- [ ] **Step 2: ゲーム起動中なら devbridge でモデル生成スモーク（新 DLL バイト配列ロード → CreateModel(Prefab 型) → transform 確認 → DeleteModel）。未起動ならスキップして注記**
- [ ] **Step 3: ロードマップ更新をコミット** `docs(timeline): Phase L2 完了をロードマップに反映`

## 主要リスク

| リスク | 対応 |
|---|---|
| ModelProviderHost（外部モデル列挙）と StudioModelManager の二重管理 | 今回は統合しない。SE 自前配置モデルを ModelProviderHost にプロバイダ登録するかは L7 で検討（ロードマップ将来課題に追記） |
| MultipleMaids ローダの COM3D2.5 API 差分（ImportCM 等） | コンパイルエラー駆動 + devbridge 実機確認。CRC ボディ非依存（roadmap 前提）なので構造的リスクは低い |
| コントローラ群（ModelBoneController 等）に MTE 固有依存が潜んでいる可能性（StudioModelManager 本体には backgroundCustomManager 参照無しを確認済み） | コンパイルエラー駆動で該当機能ごと削除し「未移植のため省略」コメントを残す |
| 削除済みモデルを参照するキーフレームの再生時挙動 | MTE 実装の null ガードを逐語維持することで同等の安全性を確保（新規ガードは追加しない）。実機通し確認で検証 |
| StudioStats.cs スタブ置換による既存参照の破壊 | フル版は スタブの上位互換（メンバー superset）のはず。ビルドで検証 |
