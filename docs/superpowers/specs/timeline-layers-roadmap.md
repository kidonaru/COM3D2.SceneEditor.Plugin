# タイムライン全レイヤー移植ロードマップ

MTE（MotionTimelineEditor）の全タイムラインレイヤーを SceneEditor のタイムラインへ移植するためのロードマップ。初期統合ロードマップ（`timeline-window-roadmap.md`、Phase 0〜5 完了）の後続。

作成日: 2026-08-23

## 1. 現状

### 移植済み（5 レイヤー）

| レイヤー | 状態 |
|---|---|
| MotionTimelineLayer | CRC ボディ対応済み（.anm 生成 + ExtendBone / IKHold / FingerBlend / Grounding） |
| CameraTimelineLayer | SE CameraWindow / GameViewManager 接続 |
| LightTimelineLayer | SE ネイティブ StudioLightManager 接続のアダプタ版 |
| ShapeKeyTimelineLayer | MaidCache 直接経路（blendshape） |
| EyesTimelineLayer | MaidCache 直接経路（瞳 Transform） |

### 未移植（MTE コア 19 + 外部連携 4）

MTE 本体 `RegisterLayer` 登録順。行数は MTE ソースの目安。

| レイヤー | 行数 | 主依存 |
|---|---|---|
| AnimationTimelineLayer | 377 | ゲーム標準 .anm 再生 |
| BGColorTimelineLayer | 359 | 背景色 (GameMain) |
| BGModelMaterialTimelineLayer | 298 | BGModelManager |
| BGModelTimelineLayer | 274+69 | BGModelManager (479) |
| BGTimelineLayer | 295 | BgMgr（SE BackgroundWindow と同経路） |
| SubCameraTimelineLayer | 507 | SubCameraManager (432) |
| DressTimelineLayer | 267 | DressUtils（移植済み） |
| MaidMaterialTimelineLayer | 282 | メイドマテリアル走査 |
| ModelBoneTimelineLayer | 301 | StudioModelManager |
| ModelShapeKeyTimelineLayer | 284 | StudioModelManager |
| ModelMaterialTimelineLayer | 315 | StudioModelManager |
| ModelTimelineLayer | 418+235 | StudioModelManager (806) + ModelHackManager (244) |
| MoveTimelineLayer | 289 | メイド全体移動 |
| PostEffectTimelineLayer（本体+5分割） | 279+1289 | PostEffectManager (322) + PostEffectUtils |
| PsylliumTimelineLayer | 1794 | PsylliumManager (415) + **mte_bundle** |
| StageLaserTimelineLayer | 998 | StageLaserManager (274) + **mte_bundle** |
| StageLightTimelineLayer | 971 | StageLightManager (291) + **mte_bundle** |
| UndressTimelineLayer | 276 | DressUtils（移植済み） |
| VoiceTimelineLayer | 292 | メイドボイス再生 |

外部プラグイン連携（MTE 拡張 DLL 由来）:

| レイヤー | 由来 | 備考 |
|---|---|---|
| PngPlacementTimelineLayer | MTE_PngPlacement | SE は PngPlacementManager / PngPlacementWindow を既に保有。実装可能と確認済み（2026-08-23） |
| MorphTimelineLayer / SeTimelineLayer / TextTimelineLayer | MTE_DCM | DCM（DanceCameraMotion）連携。**DCM 出力は未移植方針のためスコープ外**（将来ユーザー要望があれば再判断） |

### 重要な技術前提

- **mte_bundle**: Psyllium / StageLight / StageLaser の描画実体は MTE 側の以下の資産で構成される
  - MonoBehaviour 本体: PsylliumController / StageLightController / StageLaserController 等（MTE の UnityProject で作成）
  - アセット: DLL 埋め込みアセットバンドル `mte_bundle`（TimelineBundleManager が `GetManifestResourceStream` → `AssetBundle.LoadFromMemory` でロード）
  - 移植で必要な作業: SE の DLL にも `mte_bundle` を埋め込み、TimelineBundleManager 相当と UnityProject スクリプト群を持ち込む
  - ライセンス: いずれも自作 MIT のため制約なし
- **CRC ボディの影響は小さい**: 未移植レイヤーはいずれもボーン構成非依存（背景・モデル・カメラ・演出・マテリアル）。Phase 0 相当の実機検証は不要で、レイヤーごとの動作確認のみでよい
- **MTE XML 互換**: 現状、未対応レイヤーは読み込み時に破棄される仕様（docs-site ガイドに明記済み）。レイヤーを移植するたびに破棄対象が減る。移植の際は TransformType / TransformData 派生の登録も対で行うこと
- **レイヤー UI（DrawWindow）は接続しない方針**（2026-08-23 決定）
  - 方針: レイヤー固有の編集は SE 各ウィンドウでの制御に委ねる
  - 例外時の扱い: DrawWindow の中身が持つ編集機能が SE 側ウィンドウに無い場合は、該当ウィンドウ（ライト・背景・モデル等）への機能追加として吸収する

## 2. スコープ方針

- **目標**: MTE コアの全レイヤーを SE タイムラインで再生・キーフレーム編集できる状態にする
- **委譲**: 各対象の操作 UI は SE 既存ウィンドウ（Inspector / Hierarchy / 各種ウィンドウ）に寄せ、タイムラインはキーフレーム管理と再生に徹する
- **スコープ外**: DCM 連携 3 レイヤー（Morph / Se / Text）、DCM 出力、mte_bundle 以外の MTE 固有アセット追加

## 3. ロードマップ

依存が浅く SE 既存機能に直結する順に進める。各 Phase は独立して価値が出る単位。

### Phase L0: テスト基盤（ゲーム外で回るデータ層テスト）✅ 完了 (2026-08-23)

レイヤー移植は「TransformData 派生 + XML シリアライズ + 補間」の同型パターンを 19 回繰り返すため、先にゲーム外テストを整備して各 Phase の完了条件を機械判定にする。

1. テストプロジェクト新設（NUnit または xUnit。既存の非 SDK csproj とは別に SDK 形式で追加し、データ層ソースを共有コンパイルまたは DLL 参照で取り込む）
2. **MTE XML ゴールデンテスト**: MTE で作成した実プロジェクト XML を「読み込み → 保存 → 差分ゼロ」で検証するラウンドトリップテスト。レイヤーを移植するたびに該当レイヤー入りのフィクスチャ XML を 1 つ追加する運用
3. 補間・Easing（EasingFunctions / TangentData / MotionPlayData）の数値テスト少数
4. test-runner エージェントから実行できるようにする（実行コマンドを README または CLAUDE.md に記載）

- スコープ外: 再生・適用ロジック（Maid / TBody 等ゲームランタイム依存）の自動テスト。実機側は従来どおり devbridge でのスモーク確認とする
- 成果物: 各レイヤー移植の完了条件に「フィクスチャ XML のラウンドトリップ一致」を組み込める状態

### Phase L1: 軽量レイヤー群（追加マネージャ不要）✅ 完了 (2026-08-23)

- 実機確認状況: devbridge で全 7 レイヤーの Create / PhotoBGData 経路を確認済み。タイムライン再生の通し確認はゲーム再起動後（新 DLL 反映後）に実施すること
- 将来課題: PhotoBGManager と SE BackgroundWindow / BackgroundUtils の BG 一覧管理が重複している。統合要否は L7 仕上げ時に検討

SE 既存機能・ゲーム API に直結し、専用マネージャの移植が要らない 7 レイヤー。

1. **BGTimelineLayer**: 背景切替。SE BackgroundWindow と同じ BgMgr.ChangeBg 経路
2. **BGColorTimelineLayer**: 背景色・地面色
3. **UndressTimelineLayer**: 脱衣状態。DressUtils 移植済みで SE MaidUndressWindow と整合
4. **DressTimelineLayer**: 衣装プリセット切替
5. **MoveTimelineLayer**: メイド全体の位置・回転
6. **AnimationTimelineLayer**: 任意モーションファイルの再生
7. **VoiceTimelineLayer**: ボイス再生
- 成果物: 背景・衣装・移動・ボイスがタイムライン制御できる状態

### Phase L2: モデル系（StudioModelManager 基盤 + 4 レイヤー）✅ 完了 (2026-08-23)

- モデル生成は MultipleMaidsHack 方式の直接ロードを SceneEditorHack に実装（photo studio 非依存）。SceneEdit では PhotoBGObjectData.Create() の明示ロードが必要（実機確認済み）
- 実機確認状況: devbridge でアセットロード・Instantiate・PlacementData 経路を確認済み。タイムライン再生の通し確認はゲーム再起動後に実施すること
- 将来課題: SE 自前配置モデルの ModelProviderHost へのプロバイダ登録（BoneEdit / ScenePreset 連携）は L7 で検討

MTE の StudioModelManager (806 行) + ModelHackManager (244 行) を SE の ManagerRegistry 規約で移植し、SE の Hierarchy / SelectionManager / ScenePreset と整合させるのが本丸。レイヤー自体は薄い。

1. 基盤: StudioModelManager / ModelHackManager の移植（SE のオブジェクト管理・Hierarchy 表示・Inspector 編集との重複整理を含む）
2. **ModelTimelineLayer**: モデルの配置・表示
3. **ModelBoneTimelineLayer**: モデルのボーン操作
4. **ModelShapeKeyTimelineLayer**: モデルのシェイプキー
5. **ModelMaterialTimelineLayer**: モデルのマテリアル
- リスク: SE には StudioModelManager 相当が無く、シーンプリセット（モデル保存）との整合が論点。移植計画時に SE 側モデル管理の現状調査を必須とする
- 成果物: スタジオモデルがタイムライン制御できる状態

### Phase L3: 背景モデル・マテリアル系（3 レイヤー）✅ 完了 (2026-08-23)

- MaidSlotStat のマテリアル機能 (ModelMaterialController) と MaidCache の materialMap / モデル注視 (LookAtTargetType.Model) を MTE から復元
- 実機確認状況: devbridge で BgMgr.BgObject 走査 (MeshRenderer 23 件) とメイドマテリアル走査 (body 3 件) を確認済み。タイムライン再生の通し確認はゲーム再起動後

1. 基盤: BGModelManager (479 行) の移植
2. **BGModelTimelineLayer**: 背景構成オブジェクトの操作
3. **BGModelMaterialTimelineLayer**: 背景モデルのマテリアル
4. **MaidMaterialTimelineLayer**: メイドのマテリアル（基盤不要のためここに同居）
- 成果物: 背景オブジェクト・マテリアルがタイムライン制御できる状態

### Phase L4: サブカメラ ✅ 完了 (2026-08-23)

- PIP 表示先の決定: SE 適合として、サブカメラの targetTexture をメインカメラへ毎フレームミラー（GameViewManager のウィンドウモード RT リダイレクトに自動追従し、PIP は GameViewWindow 内に合成される）
- 実機確認状況: devbridge でサブカメラ生成 + rect + RT ミラーを確認済み（tt=RT）。タイムライン再生の通し確認はゲーム再起動後

1. SubCameraManager (432 行) + **SubCameraTimelineLayer** (507 行)
- SE GameViewManager / CameraWindow との関係（ピクチャインピクチャ表示をどのウィンドウに出すか）を計画時に決める
- 成果物: サブカメラ演出がタイムライン制御できる状態

### Phase L5: 演出系（mte_bundle 導入 + 3 レイヤー）✅ 完了 (2026-08-23)

- 実機確認状況: devbridge で SE DLL 埋め込み mte_bundle の AssetBundle.LoadFromMemory が Unity 2022 (COM3D2.5) で成功（21 アセット、MTE/GTToneMap マテリアル取得 OK）→ 最大リスク解消。シェーダーの実表示とタイムライン再生の通し確認はゲーム再起動後
- TimelineBundleManager の周辺機能 (lockIcon / song.ogg / icon.png) は SE スコープ外

最大工数。mte_bundle 埋め込みと UnityProject スクリプト群の持ち込みが前提。

1. 基盤整備
   - TimelineBundleManager の移植
   - mte_bundle を SE DLL へ埋め込み（csproj に EmbeddedResource 追加）
   - PsylliumController / StageLightController / StageLaserController 等の MonoBehaviour 移植
2. **StageLightTimelineLayer** (971 行) + StageLightManager: SE StudioLightManager とは別系統（名前衝突に注意。Timeline 名前空間へ寄せる）
3. **StageLaserTimelineLayer** (998 行) + StageLaserManager
4. **PsylliumTimelineLayer** (1794 行) + PsylliumManager: 最大のレイヤー。TransformData 派生 6 種
- 成果物: ライブ演出（ステージライト・レーザー・ペンライト）がタイムライン制御できる状態

### Phase L6: ポストエフェクト ✅ 完了 (2026-08-23)

- 計画時決定: エフェクト実体は PostEffects.Plugin 連携ではなく MTE 実装持ち込み（UnityScripts/PostEffect + mte_bundle シェーダーで自己完結、ソフト依存不要）
- SE 適合: SceneEditorHack.depthOfField を GetOrAddComponent に上書き（ゲーム内カメラに DoF 不在時の NRE 防止）
- 実機確認状況: devbridge で PostEffect 型ロード確認済み。注意: SE がコンパイル時に束縛する DepthOfFieldScatter は Assembly-UnityScript-firstpass 側（グローバル名前空間）で、2.5 の Assembly-CSharp には別実装 PostEffects_Dummy.DepthOfFieldScatter も存在する。DoF の実表示検証はゲーム再起動後の通し確認で行うこと

1. PostEffectManager (322 行) + PostEffectUtils + **PostEffectTimelineLayer**（本体 + DepthOfField / DistanceFog / GTToneMap / Paraffin / Rimlight の 5 分割）
- PostEffects.Plugin との連携で実現可能と確認済み（2026-08-23）。エフェクト実体を PostEffects.Plugin に委ねるか MTE 実装を持ち込むかは計画時に決める
- 成果物: ポストエフェクトがタイムライン制御できる状態

### Phase L7: PngPlacement 連携 + 仕上げ ✅ 完了 (2026-08-23) — **全フェーズ完遂**

- PngPlacementTimelineLayer は SE ネイティブ PngPlacementManager 接続のアダプタ版（外部 PngPlacement.dll 非依存）。画像は UserData\PngPlacement / PhotoModeData\Texture から探索
- MTE 互換総点検: 登録網羅テスト（MteCompatibilityTests）でローカル実プロジェクト XML 全件に未登録レイヤーが無いことを機械確認済み（DCM 3 レイヤーは既知除外）
- docs-site の timeline ガイド（レイヤー一覧・互換性の注意書き）を更新済み

#### 次回ゲーム起動時の実機通し確認チェックリスト（全フェーズ共通の残タスク）
1. 各レイヤーのタイムライン再生（L1: 背景/衣装/移動/ボイス、L2: モデル、L3: 背景モデル/マテリアル、L4: サブカメラ PIP、L5: 演出系のシェーダー実表示、L6: ポストエフェクト実表示、L7: PNG 配置）
2. MTE 実プロジェクト XML の読み込み → 再生 → 保存し直しの通し確認

1. **PngPlacementTimelineLayer**: SE PngPlacementManager 経由で接続
2. MTE 互換性の総点検: MTE で作成した実プロジェクト XML を読み込み、全レイヤーが破棄されず再生できることを実機確認
3. docs-site ユーザーガイド更新（「未対応レイヤーは破棄」の注意書きを削減・更新）
- 成果物: MTE プロジェクトがほぼ完全な形で SE で開ける状態

## 4. 主要リスクと対応

| リスク | 影響 | 対応 |
|---|---|---|
| StudioModelManager と SE 既存オブジェクト管理の二重化 | Phase L2 の工数膨張・UX 混乱 | 計画時に SE 側モデル管理の現状調査を必須化。Hierarchy / ScenePreset との統合方針を先に決めてから移植する |
| mte_bundle のライセンス・ビルド再現性 | Phase L5 | 自作 MIT なので問題なし。バンドルは MTE リポジトリのビルド済みバイナリを流用し、UnityProject からの再ビルド手順は README 参照に留める |
| 同名クラス衝突（StageLightManager 等） | ビルドエラー | 初期ロードマップと同じく `COM3D2.SceneEditor.Plugin.Timeline` 名前空間へ寄せる |
| PostEffects.Plugin 依存の有無 | Phase L6 の設計分岐 | ソフト依存（リフレクション or 任意参照）とし、未導入環境ではレイヤーを非表示にする方向で計画する |
| csproj 手動管理（非 SDK 形式） | ファイル追加漏れ | 各 Phase の計画に Compile Include / EmbeddedResource 追加を明記 |
| MTE 側の更新追従 | 移植後の互換劣化 | MTE の TimelineXml CurrentVersion (=31) が上がった場合は差分を確認して追従する運用とする |

## 5. 進め方

- 各 Phase は親ワークスペースの標準フロー（writing-plans → plan-review → executing-plans → code-review → commit）に従い、Phase ごとに個別の実装計画を作成する
- Phase L1 は独立レイヤーの集合なので、レイヤー単位でさらに分割してもよい
- 順序は L0 → L1 → L2 → … を基本とするが、L4（サブカメラ）と L6（ポストエフェクト）は依存が独立しているため前倒し可能
