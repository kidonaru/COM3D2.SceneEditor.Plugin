# タイムライン 表情レイヤー（シェイプ・瞳）移植 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の `ShapeKeyTimelineLayer`（メイドシェイプ）と `EyesTimelineLayer`（メイド瞳）を SceneEditor へ移植し、表情のキーフレーム再生を可能にする（ロードマップ Phase 3 項目 4 の表情部分。指は MotionTimelineLayer の FingerBlend で移植済み）。

**Architecture:** 両レイヤーとも必要インフラ（MaidCache の blendshape/瞳メンバ、MaidBlendShape、TransformType、XML 型、TextureUtils）は SE に移植済みで、新規インフラ移植はゼロ。MTE から diff 最小でコピーし、DCM 出力と StudioModelManager 依存（Eyes の LookAtTargetType.Model 分岐）だけを削る。依存ゼロで小さい ShapeKey を先、UI が大きい Eyes を後に進める。

**Tech Stack:** C# (.NET 3.5, 旧形式 csproj), IMGUI, MTE Timeline コア（移植済み）

**Spec:** `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 3 項目 4「表情・指レイヤー」）

## Global Constraints

- 名前空間 `COM3D2.MotionTimelineEditor.Plugin` 維持、MTE diff 最小化、削除には日本語理由コメント
- csproj は `<Compile Include>` をアルファベット順に追加
- DCM 連携（OutputMotions / OutputDCM 実体）は持ち込まない（空スタブ、既存前例と同文コメント）
- `view.IsComboBoxFocused()` は `view.focusedComboBox == null` に置換、`view.DrawComboBox()` 呼び出しは除去（ポップアップ描画はホストウィンドウ側の責務）— いずれも既存前例と同じ
- ビルド検証は MSBuild 直接実行（COM3D25 で都度、最終 Task で COM3D2 も）:
  `cd source/COM3D2.SceneEditor.Plugin && MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj '/p:Configuration=Debug' '/p:GameVersion=COM3D25' '/p:COM3D2_DIR=W:\COM3D2' '/p:COM3D25_DIR=W:\COM3D2_5' /nologo /v:minimal`

---

### Task 1: TransformData 3 種の移植

**Files:**
- Create: `Timeline/TransformData/TransformDataShapeKey.cs`（MTE 55 行、無改変）
- Create: `Timeline/TransformData/TransformDataEyes.cs`（MTE 68 行、無改変）
- Create: `Timeline/TransformData/TransformDataLookAtTarget.cs`（MTE 89 行、無改変）
- Modify: csproj（アルファベット順: Eyes は TransformDataExtendBone の**後**（Ex < Ey）、LookAtTarget は TransformDataLight の後、ShapeKey は TransformDataRotation の後）

**Interfaces:**
- Consumes: `TransformDataBase`、`TransformType.ShapeKey/Eyes/LookAtTarget`（ITransformData.cs に定義済み）
- Produces: `TransformDataShapeKey`（valueCount=2: easing/weight）、`TransformDataEyes`（easing/horizon/vertical）、`TransformDataLookAtTarget`（TargetTypeNames = 手動/カメラ/メイド/モデル）。Task 2・3 のレイヤーと Task 4 の RegisterTransform が参照

- [ ] **Step 1: MTE から 3 ファイルを無改変コピー**（`cp` で `TransformData/` 配下から）
- [ ] **Step 2: csproj に 3 行追加、ビルド確認（COM3D25）**
- [ ] **Step 3: コミット** `feat(timeline): 表情系 TransformData (ShapeKey/Eyes/LookAtTarget) を移植`

### Task 2: ShapeKeyTimelineLayer の移植

**Files:**
- Create: `Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs`（MTE 403 行）
- Modify: csproj（MotionTimelineLayer.cs の後）

**Interfaces:**
- Consumes: `TimelineLayerBase`（`hasSlotNo` 経路）、`maidCache.GetBlendShape/SetBlendShapeValue/GetBlendShapeValue/FixBlendValues`（MaidCache.cs に移植済み）、`timeline.HasMaidShapeKey/AddMaidShapeKey/RemoveMaidShapeKey/GetMaidShapeKeys`（TimelineData.cs に移植済み）、`TransformDataShapeKey`（Task 1）
- Produces: `ShapeKeyTimelineLayer`（`[TimelineLayerDesc("メイドシェイプ", 13)]`、`hasSlotNo => true`、`static Create(int slotNo)`）

- [ ] **Step 1: MTE からコピーし DCM 出力（OutputMotions L129-221 / OutputDCM L223-247 相当）を削除**、`OutputDCM` は既存前例と同文の空スタブに置換
- [ ] **Step 2: IsComboBoxFocused / DrawComboBox の SE 差分置換**（出現箇所は grep で全数確認して置換・除去）
- [ ] **Step 3: csproj 追加、ビルド確認（COM3D25）**
- [ ] **Step 4: コミット** `feat(timeline): ShapeKeyTimelineLayer を移植 (DCM 出力は削除)`

### Task 3: EyesTimelineLayer の移植

**Files:**
- Create: `Timeline/TimelineLayer/EyesTimelineLayer.cs`（MTE 767 行、enum MotionEyesType 含む）
- Modify: csproj（CameraTimelineLayer.cs の後）

**Interfaces:**
- Consumes: `maidCache.eyesPosL/R・eyesScaL/R・eyeEulerAngle・lookAtTargetType/Index/MaidPointType・GetPointTransform`（移植済み）、`TextureUtils`（MTEUtils）、`TransformDataEyes` / `TransformDataLookAtTarget`（Task 1）
- Produces: `EyesTimelineLayer`（`[TimelineLayerDesc("メイド瞳", 12)]`、`hasSlotNo => true`）

- [ ] **Step 1: MTE からコピーし DCM 出力（L307-397 相当）を空スタブ化**
- [ ] **Step 2: StudioModelManager 依存の除去**: `_modelComboBox` フィールドと `modelManager.models` を使う UI 行（L518-520 付近）、`GetLookAtTarget` の `LookAtTargetType.Model` 分岐（L211 付近）を削除し `null` フォールバック（default 扱い）にする。`LookAtTargetType.Model` の enum 値自体は XML 互換のため MaidCache 側に残す。削除位置にコメント: `// StudioModelManager は未移植のためモデル注視は提供しない`。加えて注視タイプのコンボボックス（DrawEyesLookAt）の選択肢から `LookAtTargetType.Model` を除外し、「選んでも効かない」無音の機能欠落を防ぐ（enum 値と XML 互換は維持）
- [ ] **Step 3: IsComboBoxFocused / DrawComboBox の SE 差分置換**（grep で全数確認）
- [ ] **Step 4: csproj 追加、ビルド確認（COM3D25）**
- [ ] **Step 5: コミット** `feat(timeline): EyesTimelineLayer を移植 (DCM 出力・モデル注視は削除)`

### Task 4: 統合（登録）

**Files:**
- Modify: `Timeline/TimelineIntegration.cs`

- [ ] **Step 1: RegisterLayer 2 件追加**（LightTimelineLayer 登録の後）:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.EyesTimelineLayer), MTEP.EyesTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.ShapeKeyTimelineLayer), MTEP.ShapeKeyTimelineLayer.Create);
```

- [ ] **Step 2: RegisterTransform 3 件をアルファベット順に追加**（Eyes は ExtendBone の**後**（Ex < Ey）、LookAtTarget は Light の後、ShapeKey は Rotation の後。RegisterLayer 側は歴史的経緯で非アルファベット順のため末尾追加でよい）:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.Eyes,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataEyes>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.LookAtTarget,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataLookAtTarget>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ShapeKey,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataShapeKey>);
```

- [ ] **Step 3: ビルド確認（COM3D25 / COM3D2 両方）**
- [ ] **Step 4: コミット** `feat(timeline): 表情レイヤー (シェイプ・瞳) を登録`

### Task 5: ロードマップ進捗の記録

- [ ] **Step 1:** Phase 3 項目 4 の行末に追記: ` — **移植完了（2026-08-23）**。表情は ShapeKey / Eyes の 2 レイヤーで実現（指は MotionTimelineLayer の FingerBlend で移植済み）。DCM 出力・モデル注視は未移植方針に合わせ削除。CRC ボディと旧ボディ間のシェイプキー名互換（eyeclose1 サフィックス等）は将来課題`
- [ ] **Step 2: コミット** `docs(timeline): 表情レイヤー移植の完了を記録`

## 主要リスク

| リスク | 対応 |
|---|---|
| CRC 顔のモーフ名サフィックス（CRC で保存した TL を旧ボディで開くと無音で不一致） | 本計画では同一ボディ内の整合のみ保証（UI は実在タグから列挙するため成立）。クロスボディ正規化（MaidFaceMorphController.ResolveMorphIndex 相当を MaidCache.GetBlendShapeInternal へ）は別タスク化し、ロードマップに将来課題として記録 |
| face morph API（TMorph.GetTags/hash）の CRC 実機動作 | 静的には MaidCache 移植済みコードが同 API を既に使用中。実機検証はユーザーのゲーム起動時に依頼（devbridge ping が通れば `maid.body0.Face.morph.GetTags()` を確認） |
| SE ネイティブ MaidFaceWindow との同フレーム競合 | 再生中は isPoseEditing ガードでタイムライン側が優先。相互排他の本格配線は Phase 4（編集体験統合）で扱う |
| FixBlendValues の毎フレームコスト | 登録シェイプキー数が少ない通常運用では軽微。問題化したら計測・間引き |

## スコープ外（明示）

- モデル注視（LookAtTargetType.Model）・StudioModelManager
- クロスボディのシェイプキー名正規化
- MaidFaceWindow との相互排他配線（Phase 4）
