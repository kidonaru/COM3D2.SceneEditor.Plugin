# タイムラインウィンドウ統合ロードマップ

MotionTimelineEditor（MTE）のタイムライン機能を、SceneEditor の内部ウィンドウ「タイムライン」として統合するための実現可能性評価と全体ロードマップ。

作成日: 2026-08-17

## 1. 実現可能性の結論

**実現可能。ソースマージによる内部ウィンドウ化を推奨。**

### 追い風（統合を容易にする要素）

| 要素 | 内容 |
|---|---|
| 共通ライブラリ | 両プラグインが `MTEUtils` submodule を共有。GUIView / ウィンドウ基盤 / KeyBind 等は共通コード |
| UI 方式の一致 | 両方とも完全 IMGUI（GUI.Window）。uGUI 変換は不要 |
| シリアライズ流儀の一致 | 両方とも XmlSerializer ベース（MTE: TimelineXml、SceneEditor: ScenePresetData） |
| 機能重複なし | SceneEditor は「1 フレームのポーズ編集」まで。時間軸・キーフレームの概念がなく、綺麗に上乗せできる |
| 未使用資産 | SceneEditor 側 MTEUtils に `CurveEditorWindow` / `CurveData` がコンパイル済み・未登録で待機中。`AnimationLayerInfo` / `BoneUtils` / `BinaryLoader` 等も csproj 追加だけで使える |
| 拡張ポイント | MTE 側は `RegisterLayer` / `RegisterTransform` / `StudioHackBase` で拡張性が実証済み（外部 DLL 連携 7 例）。SceneEditor 側はウィンドウ追加 6 手順が確立 |
| ライセンス | 両方とも自作（MIT）。制約なし |

### 障壁（作業が発生する箇所）

1. **COM3D2.5（CRC ボディ）非対応**: MTE は `IsCrcBody` 言及ゼロ、v3.5 単一ビルド、動作確認は COM3D2 Ver.2.33.1 のみ。`IKManager` 382 箇所・`TBody` 34 箇所・`CacheBoneDataArray` 19 箇所のゲーム内部 API 依存があり、特に `MaidCache.cs`（1219 行）と `MotionTimelineLayer.GetAnmBinaryInternal()`（.anm バイナリ独自生成）は新ボディのボーン構成に強く依存する。
2. **全域シングルトン設計**: `TimelineManager.instance` 等を UI・データモデルが静的参照。UI だけの切り出しは不可能で、TimelineManager + Config + StudioHack をセットで持ち込む必要がある。
3. **UI とデータの相互参照**: `ITimelineLayer`（約 100 メンバ）が `DrawWindow(GUIView)` を持ち、レイヤーが描画責務も兼ねる。`FrameData` ↔ `BoneData` ↔ レイヤーの双方向参照。
4. **同名クラスの衝突**: `Config` / `ConfigManager` / `ManagerBase` / `WindowManager` / `StudioLightManager` / `PluginUtils` 等が両リポジトリに存在。名前空間は別だが、マージ時に using 整理が必要。
5. **MainWindow の一枚岩**: タイムライン描画（`DrawTimeline` + `DrawBoneMenu`）は 1465 行の `MainWindow` に埋まっており、`HEADER_HEIGHT=200` 前提のレイアウト計算を `EditorSubWindow`（HEADER_HEIGHT=26）流儀に載せ替える必要がある。

### 統合方式の比較

| 方式 | 概要 | 評価 |
|---|---|---|
| **A. ソースマージ（推奨）** | MTE のタイムラインコアを SceneEditor に移植し、`TimelineWindow : EditorSubWindow` として内部ウィンドウ化 | レイアウト・タブ統合・履歴・プリセットの一等市民になれる。COM3D2.5 対応もこの過程で行う。工数は大きいが段階分割可能 |
| B. 外部ゲスト窓連携 | MTE を別 DLL のまま `DockingClient` + `SceneEditorStudioHack` 拡張 DLL で参加させる | コード移植ほぼゼロだが、①MTE 自体の COM3D2.5 対応が別途必要 ②外部窓はタブグループ復元・名前付きレイアウト対象外 ③MTE の巨大 UI（MainWindow 640x480 最小）がそのまま残る。「タイムラインウィンドウとして持ってくる」という目的に合わない |

方式 B は棄却。ただし Phase 0 の検証には MTE 単体の COM3D2.5 起動確認が含まれ、そこで得た知見は方式 A に全て流用できる。

## 2. スコープ方針

- **移植するもの**: タイムラインデータモデル（FrameData / BoneData / ValueData / TangentData / TrackData / MotionPlayData / TransformData 群）、再生エンジン（TimelineManager 相当 + レイヤー機構）、タイムライン UI（キーフレームグリッド + ボーンメニュー + 再生コントロール）、XML 保存
- **SceneEditor 既存機能に委譲するもの**: メイド操作（MaidManipulateManager）、ライト（StudioLightManager）、カメラ、IK（MaidIKHoldController）、表情（MaidFaceMorphController）、Undo/Redo（HistoryManager）、設定（Config/ConfigManager）
- **初期スコープ外（後続判断）**: Psyllium / StageLight / StageLaser / PostEffect / PngObject / DCM 出力等の MTE 固有レイヤー、mte_bundle アセット、外部プラグイン連携 Hack 群

## 3. ロードマップ

### Phase 0: 実機検証スパイク（リスク潰し）

最大リスク「CRC ボディで .anm 生成・適用が動くか」を先に潰す。

- devbridge（`eval_csharp`）で COM3D2.5 実機の CRC ボディに対して:
  - `CacheBoneDataArray` / `IKManager` の API 差分確認（2.0 の ilspycmd 出力と Assembly-CSharp 逆コンパイル済みソースの突き合わせ）
  - MTE 方式の .anm バイナリを手組みで生成 → `Animation` に適用できるか検証
  - ExtendBone / IKHold / FingerBlend の直接適用経路（MaidCache 相当）の成立性確認
- 成果物: 検証メモ（新旧ボディ差分表、動く/動かない API 一覧）
- **ここで CRC ボディの .anm 適用が成立しない場合、モーションレイヤーの設計を根本から見直す**（旧ボディ限定で先行する等の判断ポイント）

### Phase 1: データモデル・補間層の移植

UI とゲーム API に依存しない純粋データ層を先に持ち込む。

- `FrameData` / `BoneData` / `ValueData` / `TangentData` / `TangentPair` / `TrackData` / `MotionData` / `MotionPlayData` / `TransformDataBase` + 必要な TransformData 派生を移植
- 双方向参照（`parentFrame` / `parentLayer`）とマネージャ静的参照（`TimelineData` → `TimelineManager.instance`）の除去・整理
- `IKManager.BoneType` 等ゲーム型のデータモデルからの分離（文字列 or 自前 enum 化）
- XmlSerializer による保存/読込（TimelineXml 系）の移植とラウンドトリップテスト
- 成果物: SceneEditor 内でタイムライン XML の読み書き・補間計算が単体で動く状態

### Phase 2: タイムラインウィンドウ UI

- `TimelineWindow : EditorSubWindow`（WINDOW_ID = 8903383 以降）を新設
  - MTE `MainWindow.DrawTimeline` / `DrawBoneMenu` を参考に GUIView で再構成（ヘッダ 200px 前提を捨て、SceneEditor のウィンドウ流儀・リサイズ・タブ統合に適合）
  - キーフレームグリッド、フレーム目盛り、現在フレームライン、範囲選択、キーフレームドラッグ、行（ボーンメニュー）ツリー
- 再生コントロール（再生/停止/シーク/速度）を同ウィンドウまたは MaidPoseWindow と統合
- `CurveEditorWindow`（既存未使用資産）を WindowManager に登録し、キーフレームの補間カーブ編集に接続
- Config へのウィンドウ配置永続化、MenuBar への項目追加、WindowManager 登録
- 成果物: ダミーデータ（Phase 1 の XML）を表示・編集できるタイムラインウィンドウ

### Phase 3: 再生エンジン統合（レイヤー段階導入）

依存の浅いレイヤーから順に動かす。

1. **カメラレイヤー**: ボディ非依存で最も安全。SceneEditor の CameraWindow / GameViewManager と接続
2. **ライトレイヤー**: SceneEditor の StudioLightManager に接続
3. **モーションレイヤー**: Phase 0 の検証結果に基づき CRC ボディ対応版 MaidCache 相当を実装。.anm 生成 + ExtendBone / IKHold / FingerBlend / Grounding の毎フレーム適用
4. **表情・指レイヤー**: MaidFaceMorphController / MaidFingerBlendController に接続
- `TimelineManager` 相当（Update/LateUpdate ループ、Play/Stop/Pause/Seek、レイヤーレジストリ）を SceneEditor の ManagerRegistry 規約（IManager）で実装
- 成果物: タイムラインで打ったキーフレームが実機のメイド・カメラ・ライトに反映され再生できる状態

### Phase 4: 編集体験の統合

- **ポーズ編集 → キーフレーム化**: SceneEditor の既存ポーズ編集（IK ドラッグ・ボーン編集）の結果を現在フレームのキーフレームとして登録する経路
- **Undo/Redo**: キーフレーム操作を HistoryManager のスナップショット方式に統合
- **シーンプリセット連携**: タイムラインをシーンプリセットのサイドカー（`<プリセット名>.timeline.xml`）または独立保存として設計・実装
- SelectionManager との連携（Hierarchy で選択したメイド/オブジェクトのトラックへジャンプ等）
- 成果物: SceneEditor の編集フローに自然に組み込まれたタイムライン

### Phase 5: 仕上げ・拡張判断

- .anm エクスポート（マイポーズ/モーションとしての書き出し）
- パフォーマンス調整（キーフレーム描画カリングは MTE 実装済みのものを踏襲）
- docs-site へのユーザーガイド追加、CHANGELOG、リリース
- 拡張判断: MTE 固有レイヤー（Psyllium / PostEffect / PngObject 等）や DCM 出力を追加するか、MTE 本体との XML 互換（相互読み込み）をどこまで保証するか

## 4. 主要リスクと対応

| リスク | 影響 | 対応 |
|---|---|---|
| CRC ボディで .anm 生成が不成立 | モーションレイヤー全体 | Phase 0 で最初に検証。不成立なら旧ボディ限定リリース → 後続対応に分割 |
| ITimelineLayer（約 100 メンバ）の複雑さ | 移植工数の膨張 | 巨大 interface をそのまま持ち込まず、SceneEditor 用に最小のレイヤー contract を再設計 |
| 同名クラス衝突（Config / WindowManager 等） | ビルドエラー・混乱 | MTE 由来コードは移植時に `COM3D2.SceneEditor.Plugin.Timeline` 名前空間へ寄せる |
| MTE 本体とのタイムライン XML 互換 | ユーザー資産の移行 | version 属性（MTE は CurrentVersion=31）を尊重し、読み込み互換は努力目標として Phase 5 で判断 |
| csproj 手動管理（非 SDK 形式） | ファイル追加漏れ | 各 Phase の計画に Compile Include 追加を明記 |

## 5. 進め方

各 Phase は親ワークスペースの標準フロー（writing-plans → plan-review → executing-plans → code-review → commit）に従い、Phase ごとに個別の実装計画を作成する。Phase 0 は計画不要（devbridge での検証作業のみ、成果は検証メモと memory に記録）。
