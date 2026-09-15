# タイムライン DCM 連携 3 レイヤー（Morph/Se/Text）移植 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の DCM 連携 3 レイヤー（MorphTimelineLayer=メイド表情 / SeTimelineLayer=効果音 / TextTimelineLayer=テキスト字幕）を SceneEditor タイムラインへ移植し、MTE プロジェクト XML の再生・編集・保存往復を可能にする。**DCM 出力（CSV/XML 生成）はスコープ外**（OutputDCM は既存レイヤーと同じ空実装）。

**Architecture:** MTE ソース（`W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin\source\COM3D2.MotionTimelineEditor_DCM.Plugin\`）から diff 最小でコピーし、DCM 本体依存（MaidFaceManager / SoundManager / TextManager / MyConst）は DCM ソース（`W:\COM3D2_5\work\COM3D2.DanceCameraMotion.Plugin\src\`）を参照して**再生に必要な最小実装を Timeline 名前空間へ自前移植**する。TransformType は `Morph=1000, Se, Text` として ITransformData.cs に予約済みで enum 追加不要。`timeline.additionalSeNames` / `timeline.textCount` も XML 保持含め SE に実装済み。

**Tech Stack:** C# (.NET 3.5, 旧形式 csproj), IMGUI (GUIView), uGUI (UnityEngine.UI.Text — Text レイヤーの字幕描画), MTE Timeline コア（移植済み）

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md`（「未移植」表の MTE_DCM 3 レイヤー。DCM 出力スコープ外の方針は維持しつつレイヤー再生を追加する）

## Global Constraints

- .NET Framework 3.5 相当。C# 言語機能もそれに準拠（string interpolation 可、null 条件演算子可 — 既存コードに合わせる）
- 名前空間は `COM3D2.SceneEditor.Plugin.Timeline`（レイヤー/マネージャ）・TransformData は既存の `COM3D2.MotionTimelineEditor.Plugin`（MTEP エイリアス側）に合わせる — 既存移植済みレイヤーと同じ配置を踏襲すること
- csproj は旧形式・手動管理。新規ファイルは必ず `<Compile Include>` をパス昇順位置に追加
- ビルド確認は MSBuild 直叩き（ゲーム起動中は debug.bat のコピーが失敗するだけだが、停止中は実機反映されるため）:
  `MSBuild COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- テストは `source/COM3D2.SceneEditor.Plugin.Tests`（net48 SDK 形式、`dotnet test` 実行可）
- コメント・ログは日本語
- DCM 本体 DLL への参照・リフレクションアクセスは追加しない（DCMUtils.cs は移植しない）

## 検証済みの前提（調査結果）

- `ITransformData.cs:50-53` に `Morph = 1000, Se, Text` が定義済み → enum 変更不要
- `TimelineData.cs:416,428` / `TimelineXml.cs:211,238`（Load/Save は 873/882/1033/1042 行）に `additionalSeNames` / `textCount` が実装済み → XML 保持の追加作業ゼロ
- レイヤー登録は `TimelineIntegration.cs` の `Initialize`（ShapeKey: 151-152 行、Voice: 191-192 行が手本）、Transform 登録は同 194 行以降（ShapeKey: 315 行）
- `MteCompatibilityTests.cs:18-23` の `KnownExcluded` に 3 レイヤー名がハードコード → 削除が受け入れ条件
- `XmlRoundTripTests` は Fixtures ディレクトリの XML を自動走査 → フィクスチャを置くだけでよい
- UI 依存（GUIView / GUIComboBox / ColorFieldCache / DrawTransformRect / DrawMaskAll / BoneSetMenuItem）は SE に全て存在 → DrawWindow はほぼそのまま通る
- Morph の適用先は MaidCache ではなく `maid.body0.Face.morph`（TMorph）直接。`FixBlendValues_Face()` を 1 フレーム 1 回
- Se の再生は `GameMain.Instance.SoundMgr.PlaySe(name, isLoop)` / `StopSe()` の薄いラッパで足りる
- Text は uGUI。専用 Canvas（ScreenSpaceOverlay, CanvasScaler 1920x1080）+ `Font.CreateDynamicFontFromOSFont`

---

### Task 1: TransformData 3 種の移植と XML ラウンドトリップ

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMorph.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataSe.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataText.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（Compile Include 3 行）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（RegisterTransform 3 行）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l8-dcm-layers.xml`（新規フィクスチャ）

**Interfaces:**
- Consumes: `TransformDataBase`（values/strValues 汎用シリアライズ）、`TimelineManager.RegisterTransform` / `TimelineManager.CreateTransform<T>`
- Produces: `TransformDataMorph`（TransformType.Morph, valueCount=1, `morphValue` プロパティ）、`TransformDataSe`（TransformType.Se, valueCount=2/strValueCount=1, `interval`/`isLoop`/`fileName`）、`TransformDataText`（TransformType.Text, valueCount=20/strValueCount=2, position/eulerAngles/scale/color/easing の values override + `index`/`fontSize`/`lineSpacing`/`alignment`/`sizeDeltaX`/`sizeDeltaY`/`text`/`font`）— Task 2〜4 のレイヤーが `GetTransformType` と `GetOrCreateTransformData<T>` で使用

- [ ] **Step 1: MTE の TransformData 3 ファイルをコピーして SE 規約へ合わせる**

コピー元:
- `COM3D2.MotionTimelineEditor_DCM.Plugin\TransformDataMorph.cs`（47 行）
- `COM3D2.MotionTimelineEditor_DCM.Plugin\TransformDataSe.cs`（93 行）
- `COM3D2.MotionTimelineEditor_DCM.Plugin\TransformDataText.cs`（240 行）

変更点は既存移植（`TransformDataShapeKey.cs` / `TransformDataVoice.cs` を規約の手本にする）と同じ:
- 名前空間を SE 側 TransformData 群と同一にする
- using から DCM/MTE 固有のものを外す（`TransformDataBase` 等の参照先は SE 内に既存）
- ロジック・Index 定数・CustomValueInfoMap / StrValueInfoMap・デフォルト値（Text の font 既定 "Yu Gothic Bold"、fontSize 50、alignment 4、sizeDelta 1000x1000 等）は**一切変更しない**（XML 互換の生命線）

- [ ] **Step 2: csproj に Compile Include を追加**

`COM3D2.SceneEditor.Plugin.csproj` の TransformData ブロック（`TransformDataShapeKey.cs` が 445 行付近、`TransformDataVoice.cs` が 464 行付近）にパス昇順で 3 行:

```xml
    <Compile Include="Timeline\TransformData\TransformDataMorph.cs" />
    <Compile Include="Timeline\TransformData\TransformDataSe.cs" />
    <Compile Include="Timeline\TransformData\TransformDataText.cs" />
```

- [ ] **Step 3: TimelineIntegration に RegisterTransform を追加**

`TimelineIntegration.cs` の RegisterTransform 列（194 行以降、ShapeKey は 315 行）の末尾 DCM グループとして:

```csharp
            // DCM 由来レイヤーの TransformData
            timelineManager.RegisterTransform(MTEP.TransformType.Morph, MTEP.TimelineManager.CreateTransform<MTEP.TransformDataMorph>);
            timelineManager.RegisterTransform(MTEP.TransformType.Se, MTEP.TimelineManager.CreateTransform<MTEP.TransformDataSe>);
            timelineManager.RegisterTransform(MTEP.TransformType.Text, MTEP.TimelineManager.CreateTransform<MTEP.TransformDataText>);
```

（`MTEP.` プレフィックスの要否は既存行の記法に厳密に合わせること）

- [ ] **Step 4: フィクスチャ XML を作成**

`Fixtures/l8-dcm-layers.xml` を新規作成。実データ（`W:\COM3D2_5\PhotoModeData\_Timeline\仮装狂騒曲 篠澤広\仮装狂騒曲 篠澤広122.xml`）の `MorphTimelineLayer` / `TextTimelineLayer` の Layer 要素から代表フレーム（0 フレーム + 中間 1 フレーム程度）を抜粋し、SeTimelineLayer は手書きで最小構成（fileName="se022.ogg", interval=0, isLoop=0 の 1 キー）を加える。`version="31"`、既存フィクスチャ（`l1-layers.xml`）とルート構造を揃える。

- [ ] **Step 5: ビルドとテストを実行して確認**

Run: MSBuild（Global Constraints のコマンド）→ 成功すること
Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` → `XmlRoundTripTests` が l8-dcm-layers.xml を拾ってラウンドトリップ一致で PASS すること（`MteCompatibilityTests` は KnownExcluded が残っているためまだ通る）

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMorph.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataSe.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataText.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/l8-dcm-layers.xml
git commit -m "feat(timeline): DCM 系 TransformData (Morph/Se/Text) を移植"
```

---

### Task 2: 表情基盤（FaceMorphUtils + TimelineFaceManager）と MorphTimelineLayer

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineFaceManager.cs`（DCM MaidFaceManager の再生用最小移植）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/FaceMorphUtils.cs`（DCM MyConst のモーフ名表 + MTE MorphUtils の統合）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs`
- Modify: `COM3D2.SceneEditor.Plugin.csproj`（Compile Include 3 行）
- Modify: `TimelineIntegration.cs`（RegisterLayer 1 行）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests`（既存ラウンドトリップが引き続き通ること）

**Interfaces:**
- Consumes: `TransformDataMorph`（Task 1）、`TimelineLayerBase`、`maid.body0.Face.morph`（TMorph）
- Produces: `TimelineFaceManager` — `float GetMorphValue(Maid maid, string key)` / `void SetMorphValue(Maid maid, Dictionary<string, float> values)` / `void SetMabatakiOff(Maid maid)`。`FaceMorphUtils` — `List<string> saveMorphNames` / `Dictionary<string, string> eyeMorphJp / mayuMorphJp / mouthMorphJp / faceOptionMorphJp`（カテゴリ別 モーフ名→和名）。`MorphTimelineLayer.Create(int slotNo)`

- [ ] **Step 1: FaceMorphUtils を作成**

コピー元: MTE `MorphUtils.cs`（19-34, 88-103 行）+ DCM `MyConst.cs` の `EYE_MORPH` / `MAYU_MORPH` / `MOUTH_MORPH` / `FACE_OPTION_MORPH`（1679 行〜）・`ALL_FACIAL_MORPH`（1723 行）。DCM MyConst への参照を消すため、モーフ名⇔和名の辞書 4 つを**値ごと FaceMorphUtils にインライン展開**する。`FACE_OPTION_MORPH` 相当のキー集合は Lerp 抑制判定（頬・涙などはステップ適用）に使うため `IsStepMorph(string name)` として公開する。

FaceMorphUtils はゲーム型（Maid/TMorph）に依存しない純粋データクラスとして作り、Tests プロジェクトへ辞書整合テストを追加する（`saveMorphNames` が 4 カテゴリ辞書のキー合算と一致すること、重複キーが無いこと）。Maid 実インスタンスが要る `CheckMorph`/`CheckMorphFB` はゲーム外テスト不可のため Task 6 の実機確認で担保する。

- [ ] **Step 2: TimelineFaceManager を作成**

コピー元: DCM `MaidFaceManager.cs`（109-269 行 + SetMabatakiOff 482 行）。再生に必要な 6 メソッドのみ:

```csharp
        // GetMorphValue: morph.GetBlendValues((int)morph.hash[fixedKey]) / GetRatio(...)
        // SetMorphValue: 各キー SetBlendValues → AdjustClosedEye → morph.FixBlendValues_Face()（1 フレーム 1 回）
        // SetBlendValues: value == -1f と hash 未登録名はスキップ
        // CheckMorph / CheckMorphFB: 別名解決。FB 顔 (morph.bodyskin.PartsVersion >= 120) は crcFaceTypesStr 対応
        // GetRatio: eyeclose3 は 3f、他は 1f
        // SetMabatakiOff: maid.boMabataki = false; maid.body0.Face.morph.EyeMabataki = 0f;
```

`ManagerBase`（`Timeline/Manager/ManagerBase.cs` 側。他のタイムラインマネージャと同じ基底）を継承し、`TimelineIntegration` の既存マネージャ登録列に `RegisterManager` を追加する。CRC 顔（COM3D2.5 の FB 顔）対応の `CheckMorphFB` は**必ず含める**（2.5 実機で表情が乗る条件）。

- [ ] **Step 3: MorphTimelineLayer を移植**

コピー元: MTE `MorphTimelineLayer.cs`（392 行）。変更点:
- `using COM3D2.DanceCameraMotion.Plugin` を削除し、`_faceManager` を `TimelineFaceManager`（Step 2）へ差し替え
- `MyConst.*_MORPH` 参照を `FaceMorphUtils` の対応辞書へ差し替え（DrawWindow のタブ 4 種 目/眉/口/他 も同様）
- `OutputBones`（209-256 行、morph CSV 生成）を削除、`OutputDCM`（258-281 行）を既存レイヤーと同じ空実装 + `// DCM 連携は未移植のため出力しない` コメントへ
- `UpdateFrame` を SE 主流 API（`frame.GetOrCreateTransformData<TransformDataMorph>(name)` — `VoiceTimelineLayer.cs:86` 参照）へ書き換え
- `[TimelineLayerDesc("メイド表情", 10)]` は維持（**既存 25 レイヤーの priority を全列挙して照合済み**: 10 / 51 / 53 はすべて空き。10 はメイド移動(11)の手前に並ぶ）
- `_faceManager` などマネージャへの参照は MTE の static/instance 構成を機械的に踏襲せず、**SE 既存移植レイヤーのマネージャ参照慣習（シングルトン instance プロパティ経由）に合わせる**。構造の大改変はせず参照方式のみ揃える

- [ ] **Step 4: csproj / TimelineIntegration へ登録**

csproj: `Timeline\FaceMorphUtils.cs`・`Timeline\Manager\TimelineFaceManager.cs`・`Timeline\TimelineLayer\MorphTimelineLayer.cs` をパス昇順位置へ。
TimelineIntegration: ShapeKey 登録行（151-152 行）の並びに:

```csharp
            timelineManager.RegisterLayer(typeof(MTEP.MorphTimelineLayer), MTEP.MorphTimelineLayer.Create);
```

- [ ] **Step 5: ビルドとテスト**

Run: MSBuild → 成功。`dotnet test` → 全 PASS（ラウンドトリップに退行がないこと）

- [ ] **Step 6: Commit**

```bash
git add -- source/COM3D2.SceneEditor.Plugin/Timeline/FaceMorphUtils.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineFaceManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
git commit -m "feat(timeline): MorphTimelineLayer (メイド表情) を移植"
```

---

### Task 3: 効果音基盤（TimelineSeManager）と SeTimelineLayer

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineSeManager.cs`（DCM SoundManager の再生用最小移植）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs`
- Modify: csproj（2 行）/ TimelineIntegration.cs（RegisterLayer 1 行 + RegisterManager 1 行）

**Interfaces:**
- Consumes: `TransformDataSe`（Task 1）、`timeline.additionalSeNames`（SE 実装済み）、`GameMain.Instance.SoundMgr`
- Produces: `TimelineSeManager` — `List<string> seNames`（se000.ogg〜se100.ogg の存在確認済み列挙 + DCM MyConst.SE_EXT 相当の追加名 + `timeline.additionalSeNames`）/ `void PlaySe(string seName, bool isLoop)` / `void StopSe()`。`SeTimelineLayer.Create(int slotNo)`（slotNo 無視・常に単一）

- [ ] **Step 1: TimelineSeManager を作成**

コピー元: DCM `SoundManager.cs`（ctor 42-63 行の SE 候補列挙、PlaySe/StopSe 169-185 行）。実装は `GameMain.Instance.SoundMgr.PlaySe(name, loop)` / `StopSe()` の薄いラッパ + `GameUty.FileSystem.IsExistentFile` による `se000.ogg`〜`se100.ogg` 存在チェック列挙。`MyConst.SE_EXT`（se_nami.ogg 等 6 件、`MyConst.cs:3361-3366`）は値をインライン展開。`ManagerBase` 継承・`RegisterManager` 登録は Task 2 と同様。

- [ ] **Step 2: SeTimelineLayer を移植**

コピー元: MTE `SeTimelineLayer.cs`（376 行、partial は単一ファイルへ統合）。変更点:
- `SoundManager` 参照を `TimelineSeManager` へ。static/instance の不整合（`soundManager` が static、`_currentSeName` 等が instance）は **manager をインスタンスフィールドに揃えて**整理
- `OutputMotions`（144-183 行、se.csv 生成)を削除、`OutputDCM`（185-202 行）を空実装へ
- `UpdateFrame` を `GetOrCreateTransformData<TransformDataSe>` へ
- interval 再トリガ（`UpdateSe` 120-131 行）・`config.voiceMaxLength` / `MTEUtils.ShowDialog` を使う管理タブ UI はそのまま維持
- `[TimelineLayerDesc("効果音", 51)]` 維持

- [ ] **Step 3: csproj / TimelineIntegration へ登録、ビルド + テスト**

Task 2 Step 4-5 と同じ要領。Run: MSBuild + `dotnet test` → PASS

- [ ] **Step 4: Commit**

```bash
git add -- source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineSeManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SeTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
git commit -m "feat(timeline): SeTimelineLayer (効果音) を移植"
```

---

### Task 4: 字幕基盤（TimelineTextManager）と TextTimelineLayer

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs`（DCM TextManager + MTE MTETextManager の統合最小移植）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TextTimelineLayer.cs`
- Modify: csproj（2 行）/ TimelineIntegration.cs（RegisterLayer + RegisterManager）

**Interfaces:**
- Consumes: `TransformDataText`（Task 1）、`timeline.textCount`（SE 実装済み）
- Produces: `TimelineTextManager` — `FreeTextSet[] TextData` / `void InitializeText(int count)` / `Font GetFont(string name)` / `List<string> FontNames` / `void ReleaseTexts()`。`FreeTextSet` struct（obj/text/rect/isEnabled/lerpTime/index — DCM 定義をそのまま持ち込む）。`TextTimelineLayer.Create(int slotNo)`（slotNo 無視）

- [ ] **Step 1: TimelineTextManager を作成**

コピー元: DCM `TextManager.cs` の必要部（CreateCanvas 234-261 行 / テキスト生成 62-79 行 / GetFontNames・GetFont 263-284 行 / ReleaseDanceText 513 行）と MTE `MTETextManager.cs`（DCM 依存の隔離層。TextManager ラッパ構造の手本）。約 100〜150 行:
- Canvas は `ScreenSpaceOverlay`（DCM の planeDistance -9999f 相当のみ。ScreenSpaceCamera/背面表示は MTE レイヤーが使っていないため持ち込まない）+ `CanvasScaler.ScaleWithScreenSize` referenceResolution 1920x1080
- `UnityEngine.UI.Text` を AddComponent、`supportRichText = true`
- フォントは `Font.GetOSInstalledFontNames()` 列挙 + `Font.CreateDynamicFontFromOSFont(name, 0)` キャッシュ
- シーン破棄・タイムライン Unload 時に Canvas と Text を Destroy する `ReleaseTexts` を `ManagerBase` のライフサイクル（既存マネージャの OnUnload 相当）へ接続
- **csproj への参照追加に注意**: uGUI 使用のため `UnityEngine.UI` アセンブリ参照が必要。既存 `<Reference Include>` に無ければ COM3D2/COM3D25 両ゲームの Managed から追加する

- [ ] **Step 2: TextTimelineLayer を移植**

コピー元: MTE `TextTimelineLayer.cs`（483 行、partial 統合）。変更点:
- `MTETextManager` 参照を `TimelineTextManager` へ
- `OutputPlayData`（199-263 行、text.csv 生成）削除、`OutputDCM`（265-280 行）空実装へ
- `UpdateFrame` を `GetOrCreateTransformData<TransformDataText>` へ
- `allBoneNames` の `timeline.textCount` 追随（23-37 行）、`ApplyMotionInit` / `ApplyMotionUpdate` の Lerp 構造、DrawWindow（GUIComboBox×2 + TextAnchor コンボ + ColorFieldCache + DrawTransformRect）はそのまま維持
- `[TimelineLayerDesc("テキスト", 53)]` 維持

- [ ] **Step 3: csproj / TimelineIntegration へ登録、ビルド + テスト**

Run: MSBuild + `dotnet test` → PASS

- [ ] **Step 4: Commit**

```bash
git add -- source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TextTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
git commit -m "feat(timeline): TextTimelineLayer (字幕) を移植"
```

---

### Task 5: MTE 互換テストの除外解除と docs 更新

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/MteCompatibilityTests.cs:18-23`（KnownExcluded から 3 レイヤー名を削除）
- Modify: `docs/superpowers/specs/timeline-layers-roadmap.md`（未移植表の DCM 3 レイヤー行を移植済みへ、Phase L8 として完了記録を追記）
- Modify: docs-site の timeline ガイド（レイヤー一覧に 3 レイヤーを追加、「DCM レイヤーは破棄」の注意書きを削除）

**Interfaces:**
- Consumes: Task 2〜4 の RegisterLayer 登録（MteCompatibilityTests は TimelineIntegration.cs を正規表現パースして登録済み集合を作る）

- [ ] **Step 1: KnownExcluded から `MorphTimelineLayer` / `SeTimelineLayer` / `TextTimelineLayer` を削除**

- [ ] **Step 2: テスト実行**

Run: `dotnet test` → `MteCompatibilityTests` がローカル実プロジェクト XML（`COM3D2_TIMELINE_DIR`）全件を未登録レイヤーなしで PASS すること

- [ ] **Step 3: ロードマップと docs-site を更新（Phase L8 として記録）**

- [ ] **Step 4: Commit**

```bash
git add -- source/COM3D2.SceneEditor.Plugin.Tests/MteCompatibilityTests.cs docs/
git commit -m "test(timeline): DCM 3 レイヤーを互換テストの除外リストから解除"
```

---

### Task 6: 実機通し確認（ゲーム再起動後）

**Files:** なし（devbridge による検証のみ）

- [ ] **Step 1: DLL 反映**（ゲーム停止中に `debug.bat com3d25`、または起動中なら Harmony ホットリロード手順）

- [ ] **Step 2: 実機確認チェックリスト**

1. `仮装狂騒曲 篠澤広122.xml`（**123 ではなく 122**。123 は Morph/Text が消失済み）をロード → アクティブレイヤーに MorphTimelineLayer / TextTimelineLayer が現れること
2. タイムライン再生でメイドの表情（Morph）が変化すること（devbridge: `maid.body0.Face.morph.GetBlendValues` を watch）
3. Text レイヤーの字幕が GameView 上に表示されること（screenshot で確認）
4. Se レイヤー: フィクスチャ相当の SE キーを打って再生されること（ログ + 耳確認）
5. 保存し直し → XML に 3 レイヤーの ClassName が保持されること（保存前後の ClassName 一覧 diff）
6. ポーズ編集モード中（isPoseEditing）は Morph が MaidFaceWindow 操作を邪魔しないこと
7. Se レイヤー: シーク・巻き戻し・停止時に二重再生・鳴りっぱなしが起きないこと（interval 再トリガの currentTime リセット確認）
8. Text レイヤー: 既定フォント "Yu Gothic Bold" が OS に存在する環境での表示確認（非日本語 OS のフォールバックはスコープ外として既知課題に記録）

- [ ] **Step 3: ロードマップの実機確認済みマークを更新して Commit**

---

## 主要リスクと対応

| リスク | 影響 | 対応 |
|---|---|---|
| CRC/FB 顔でモーフ名が解決できない | 2.5 実機で表情が乗らない | DCM `CheckMorphFB`（`TMorph.crcFaceTypesStr` + PartsVersion>=120 判定）を必ず移植。実機確認 Task 6-2 で検証 |
| `UnityEngine.UI` 参照が csproj に無い | Task 4 ビルドエラー | Task 4 Step 1 で参照確認を明記。2.0/2.5 両方の Managed に存在するため両 GameVersion でビルド確認 |
| SysDlg 由来ダイアログ（Se 管理タブの ShowDialog） | 表示が裏に隠れる | 直近の MTEUtils 変更で DialogPopupWindow 委譲済み。追加対応不要 |
| MaidFaceWindow との同フレーム競合（既知の将来課題） | 編集体験の混乱 | 本計画では MTE と同じ isPoseEditing ガードまで。相互排他の本格配線はスコープ外を維持 |
| 123.xml にデータ消失が既に発生 | ユーザーデータ | Task 6 で 122 からのロードを明記。bak フォルダにも旧版あり |
| Text の Canvas がシーン遷移で残留 | GameObject リーク | TimelineTextManager.ReleaseTexts を Unload/シーン破棄ライフサイクルへ接続（Task 4 Step 1） |

## レビュー却下メモ

- Se/Text レイヤーの型書き換え量が過小記載の可能性（確信度 低）— 計画は移植元の行番号と削除対象を明記済みで、残りは機械的な型追随。実装時対応で足りる
- MteCompatibilityTests の COM3D2_TIMELINE_DIR 未設定時挙動が未記載（確信度 低）— 既存テストの既知挙動（既定パスへフォールバック）であり本計画のスコープ外
- 旧ボディでのモーフ名解決失敗時のフォールバック（確信度 低）— SetBlendValues が hash 未登録名をスキップする既存設計（無音スキップ）を踏襲。クロスボディ正規化はロードマップ既知の将来課題として別管理
