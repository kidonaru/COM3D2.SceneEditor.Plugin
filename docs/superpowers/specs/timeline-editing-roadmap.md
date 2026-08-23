# タイムライン編集体験ロードマップ

MTE（MotionTimelineEditor）由来のタイムラインを SceneEditor 上で「再生できる」から「GUI で作れる・調整できる」状態へ引き上げるためのロードマップ。初期統合ロードマップ（`timeline-window-roadmap.md`、Phase 0〜5 完了）と全レイヤー移植ロードマップ（`timeline-layers-roadmap.md`、Phase L0〜L8 完了）の後続。

作成日: 2026-08-23

## 1. 現状

### 完了していること

- レイヤー 28 件登録済み。MTE 実プロジェクト XML は未登録レイヤーなしで読み込み・再生・保存往復できる（MteCompatibilityTests で機械確認済み）
- MTE の SubWindowType 8 種のうち 4 種は SE 側で対応済み:

| MTE サブウィンドウ | SE での対応 |
|---|---|
| KeyFrame（補間編集） | ✅ CurveEditorWindow 接続済み（Phase 2） |
| TimelineLayer | ✅ 「接続しない」と決定済み（2026-08-23）。レイヤー固有編集は SE 各ウィンドウへ委譲 |
| History | ✅ SE HistoryManager / HistoryWindow へブリッジ済み（Phase 4） |
| IKHold（IK固定） | ✅ MaidIKWindow が MTE の IK固定相当として実装済み（四肢の空間固定 + 足の接地） |

### 未対応（本ロードマップの対象）

**A. MTE サブウィンドウの未移植・不足 4 種**

| MTE サブウィンドウ | 状態 | 備考 |
|---|---|---|
| TimelineLoad（ロード） | ⚠️ 簡易版のみ | TimelineWindow 内蔵のコンボボックス（相対パスの文字列一覧。`RefreshTimelineFileList` / `LoadTimelineByRelativePath`）はあるが、MTE のサムネイルタイル表示・ディレクトリ階層の移動・エクスプローラで開く・更新ボタンは未移植。サムネイル自体は SE でも保存時に出力済み（`TimelineManager.SaveTimeline` / `SaveThumbnail`）のため、不足は表示 UI のみ |
| TimelineSetting | ❌ UI 未移植 | 設定項目（ループ再生・目線制御・顔/胸の固定化・フレームレート等。全項目は Phase W1 参照）は XML 互換のため TimelineData に全て存在するが、編集 UI はコントロールパネルの maxFrameNo のみ。**値は読めるが GUI から変えられない** |
| Track（トラック設定） | ❌ UI 未移植 | `TrackData` / `activeTrack` はデータ層に存在し、TimelineWindow はスクロールジャンプで参照するだけ。トラックの追加・名前変更・範囲編集 UI がない |
| Template（テンプレート） | ❌ 完全未移植 | `TimelineTemplateManager` 自体が SE に存在しない。キーフレームのカテゴリ / テンプレ管理機能ごと持ち込みが必要 |

**B. レイヤー個別編集の受け皿が無い領域**

方針「レイヤー UI（DrawWindow）は接続せず、SE 各ウィンドウへ機能を吸収する」（2026-08-23 決定）に対し、吸収先ウィンドウ自体が無い・不足しているレイヤーが残っている。これらは XML を読み込めば再生できるが、SE 上でキーフレームの中身を GUI で作成・編集できない。

受け皿あり（対応済みか小さい追加で済む）:

| レイヤー | SE の受け皿 |
|---|---|
| Camera / SubCamera(再生のみ) | CameraWindow |
| Light | LightWindow |
| BG | BackgroundWindow |
| Dress / Undress | MaidUndressWindow |
| Motion / ModelBone | BoneEditWindow / MaidPoseWindow |
| PngObject | PngPlacementWindow |
| Morph（表情） | MaidFaceWindow |
| Eyes | MaidFaceWindow（視線タブ） |
| Move | メイド移動（ドラッグ / Inspector） |

受け皿なし・不足:

| 領域 | 対象レイヤー | 現状の穴 |
|---|---|---|
| 演出系 | Psyllium / StageLight / StageLaser | パラメータ編集 UI なし。MTE ではレイヤー DrawWindow が編集 UI の本体だったため、実質最大の塊 |
| マテリアル | MaidMaterial / ModelMaterial / BGModelMaterial | 3 系統ともマテリアルプロパティ編集 UI なし |
| シェイプキー | ShapeKey / ModelShapeKey | MaidFaceWindow は公式表情モーフ（目/眉/口/オプション）のみで任意 blendshape は編集不可。モデル側は完全に受け皿なし |
| ポストエフェクト | PostEffect（DoF / Fog / GTToneMap / Paraffin / Rimlight） | 編集 UI なし |
| サブカメラ | SubCamera | 位置・画角等の編集 UI なし（表示は PIP で対応済み） |
| 効果音 | Se | SE 名（公式 92 件）の選択・試聴 UI なし |
| ボイス | Voice | ボイスファイル指定 UI なし |
| 字幕 | Text | テキスト・フォント・サイズ・配置のスタイル編集 UI なし |
| 地面色 | BGColor | 背景色自体は BackgroundWindow に編集 UI あり（`camera.backgroundColor` を共有するため既に GUI 連動）。地面色（BGGroundColorDisplayName）の表示・色のみ受け皿なし |
| 背景モデル | BGModel | 背景構成オブジェクトの表示切替・Transform 編集 UI なし |

### 参考: 先行ロードマップの残タスク（本ロードマップのスコープ外）

- L7 記載の「次回ゲーム起動時の実機通し確認チェックリスト」（L1〜L7 各レイヤーの再生通し確認）。L8 の Morph / Se / Text は 2026-08-23 に実機確認済み
- L8 の限定確認 3 点:
  - CRC/FB 顔での `CheckMorphFB` 未検証
  - Se レイヤーの interval 再トリガ未検証
  - 非日本語 OS の字幕フォントフォールバック未対応

## 2. スコープ方針

- **目標**: 全レイヤーについて「SE の GUI だけでキーフレームの作成・値編集が完結する」状態にする
- **委譲**: 対象の操作 UI は SE 既存ウィンドウへの機能追加を第一候補とし、既存ウィンドウに馴染まないものだけ新規ウィンドウを起こす
- **スコープ外**: DCM 出力（CSV/XML 生成）、MTE の Setting UI にある DCM 出力・連番画像出力関連の項目（未移植方針に合わせて持ち込まない）

## 3. ロードマップ

優先度は **TimelineSetting > Track / TimelineLoad > レイヤー編集の受け皿 > Template**。Setting はタイムライン品質（ループ再生・顔/胸物理・目線制御）に直結し、データ層が既にあるため UI だけの作業で済む。

### Phase W1: タイムライン設定 UI

- MTE `TimelineSettingUI` を SE 流儀で移植。配置は新規ウィンドウではなく、TimelineWindow からの呼び出し（設定ボタン → ポップアップまたはタブ）を計画時に決める
- 対象項目（MTE の個別設定 + 共通設定）:
  - 格納ディレクトリ名、フレームレート（30/60 プリセット付き）
  - メイド目線（eyeMoveType）、1フレーム調整（singleFrameType）
  - 顔/瞳の固定化（useHeadKey）、胸(左/右)の物理無効（useMuneKeyL/R）
  - ループアニメーション（isLoopAnm）、イージングを次のキーフレームに適用、カメラ / メイド移動のタンジェント補間有効化
  - ポストエフェクトの色拡張・ブレンド拡張、地面色と背景表示の連動
  - オフセット時間・フェード時間・アスペクト比・レターボックス透過度
  - 共通設定: 初期補間曲線（defaultTangentType）等
- DCM 出力・連番画像出力・サムネ更新の項目は持ち込まない
- 個別設定の初期化ボタンは確認ダイアログ付きで移植
- 成果物: タイムラインの再生挙動・物理・目線を GUI から制御できる状態

### Phase W2: トラック設定 / タイムラインロード UI

- MTE `TimelineTrackUI` 相当（トラック追加・名前変更・開始/終了フレーム編集・アクティブ切替・削除）を移植
- TimelineWindow 内の表示（アクティブトラック範囲のハイライト等）との連動を確認
- MTE `TimelineLoadUI` 相当のロード UI を移植: サムネイルタイル表示（`config.thumWidth/thumHeight`）、ディレクトリ階層の移動、一覧の更新、格納フォルダをエクスプローラで開く
  - サムネイルは SE でも保存時に出力済みのため、表示側の実装のみ
  - 既存のコンボボックス簡易ロードを残すか置き換えるかは計画時に決める
- 成果物: トラック運用と、サムネイル付き一覧からのタイムラインロードが GUI で完結する状態

### Phase W3: レイヤー編集の受け皿整備（大物）

MTE の各レイヤー DrawWindow が持っていた編集機能を SE ウィンドウへ吸収する。工数が大きいものから独立して進められる。

1. **演出系ウィンドウ**（Psyllium / StageLight / StageLaser）: 新規ウィンドウが必要。MTE の DrawWindow 実装（Psyllium 1794 行が最大）を SE ウィンドウ流儀で再構成。共通パターン（カウント管理 + Transform + 色 + 時間パラメータ）が多いため、3 種まとめて設計する
2. **マテリアル編集**: メイド / モデル / 背景モデルの 3 系統。InspectorWindow への統合か専用ウィンドウかを計画時に決める。走査系（ModelMaterialController / materialMap）は移植済みなので UI のみ
3. **シェイプキー編集**: メイドの任意 blendshape（MaidFaceWindow への「全シェイプキー」タブ追加が候補）とモデルのシェイプキー（BoneEditWindow への統合が候補）
4. **ポストエフェクト編集**: DoF / DistanceFog / GTToneMap / Paraffin / Rimlight の 5 種。専用ウィンドウ 1 枚に集約する
- 成果物: 演出・マテリアル・シェイプキー・ポストエフェクトのキーフレームを GUI で作成・調整できる状態

### Phase W4: レイヤー編集の受け皿整備（小物）

キーフレーム値の編集で足りる可能性が高い領域。専用ウィンドウを起こす前に「TimelineWindow で選択したキーフレームの値を InspectorWindow で編集する」方式で足りるかの設計判断を先に行う。

1. **効果音（Se）**: SE 名の一覧選択 + 試聴 + interval 設定
2. **ボイス（Voice）**: ボイスファイル指定 + 試聴
3. **字幕（Text）**: テキスト・フォント・サイズ・色・配置の編集
4. **地面色（BGColor）**: 地面色の表示・色編集（BackgroundWindow への追加が候補。背景色自体は BackgroundWindow で編集済みのため対象外）
5. **背景モデル（BGModel）**: 表示切替・Transform（HierarchyWindow / InspectorWindow 連携が候補）
6. **サブカメラ**: 位置・回転・画角（CameraWindow へのサブカメラタブ追加が候補）
- 成果物: 全レイヤーのキーフレーム編集が SE の GUI で完結する状態

### Phase W5: テンプレート機能

- MTE `TimelineTemplateManager` + `TimelineTemplateUI`（操作 / カテゴリ編集 / テンプレ編集の 3 タブ）を移植
- キーフレームのテンプレ保存・適用。保存先ディレクトリ・XML 形式は MTE 互換とし、MTE で作成したテンプレ資産をそのまま使えるようにする
- 優先度は最も低い。W1〜W4 完了後にユーザー需要を見て実施判断してもよい
- 成果物: よく使うキーフレームセットをテンプレとして再利用できる状態

## 4. 主要リスクと対応

| リスク | 影響 | 対応 |
|---|---|---|
| 演出系 DrawWindow の移植量（Psyllium 1794 行等） | Phase W3 の工数膨張 | DrawWindow のうち編集 UI 部分だけを抽出し、GUIView 共通部品（スライダー + カラーピッカー）へ寄せて圧縮する。3 種の共通パターンを先に設計する |
| InspectorWindow 方式と専用ウィンドウ方式の混在 | UX の一貫性低下 | Phase W4 冒頭で「キーフレーム値編集の標準方式」を 1 つ決めてから各レイヤーに展開する |
| TimelineData 設定項目の適用タイミング | 設定変更が再生に反映されない | MTE では各レイヤーが毎フレーム timeline フィールドを参照する設計。SE 移植版でも同じ参照経路になっているか、項目ごとに実機確認する |
| テンプレ XML の MTE 互換 | ユーザー資産の移行 | 保存形式・ディレクトリ構成を MTE と揃え、フィクスチャによるラウンドトリップテストを Phase L0 基盤に追加する |
| csproj 手動管理（非 SDK 形式） | ファイル追加漏れ | 各 Phase の計画に Compile Include 追加を明記 |

## 5. 進め方

- 各 Phase は親ワークスペースの標準フロー（writing-plans → plan-review → executing-plans → code-review → commit）に従い、Phase ごとに個別の実装計画を作成する
- W1 / W2 は小さいためまとめて 1 計画にしてもよい。W3 はレイヤー群単位（演出系 / マテリアル / シェイプキー / ポストエフェクト）で計画を分割する
- W3 と W4 は依存が独立しているため、需要に応じて順序を入れ替えてよい
