# タイムライン編集体験ロードマップ

MTE（MotionTimelineEditor）由来のタイムラインを SceneEditor 上で「再生できる」から「GUI で作れる・調整できる」状態へ引き上げるためのロードマップ。初期統合ロードマップ（`timeline-window-roadmap.md`、Phase 0〜5 完了）と全レイヤー移植ロードマップ（`timeline-layers-roadmap.md`、Phase L0〜L8 完了）の後続。

作成日: 2026-08-23

> Phase W1〜W3 完了時点の未移植機能・残作業の棚卸しは `timeline-remaining-work.md` を参照。

## 1. 現状

### 完了していること

- レイヤー 28 件登録済み。MTE 実プロジェクト XML は未登録レイヤーなしで読み込み・再生・保存往復できる（MteCompatibilityTests で機械確認済み）
- MTE の SubWindowType 8 種のうち 4 種は SE 側で対応済み:

| MTE サブウィンドウ | SE での対応 |
|---|---|
| KeyFrame（補間編集） | ✅ CurveEditorWindow 接続済み（Phase 2） |
| TimelineLayer | ✅ 汎用ホスト `TimelineLayerWindow` で接続（2026-08-23 に方針転換。下記参照） |
| History | ✅ SE HistoryManager / HistoryWindow へブリッジ済み（Phase 4） |
| IKHold（IK固定） | ✅ MaidIKWindow が MTE の IK固定相当として実装済み（四肢の空間固定 + 足の接地） |

### 未対応（本ロードマップの対象）

**A. MTE サブウィンドウの未移植・不足 4 種**

| MTE サブウィンドウ | 状態 | 備考 |
|---|---|---|
| TimelineLoad（ロード） | ✅ 移植済み（2026-08-23、Phase W2） | `TimelineLoadWindow` + `TimelineLoadManager` でサムネイルタイル表示・階層移動・エクスプローラで開く・更新に対応。TimelineWindow 内蔵のコンボボックスは素早く選ぶ導線として併存させている |
| TimelineSetting | ✅ 移植済み（2026-08-23、Phase W1） | `TimelineSettingWindow`（個別 / 共通タブ）を追加。TimelineWindow の「設定」ボタンとメニューバーから開く。SE に適用経路が無い項目は意図的に非対象（Phase W1 の除外項目を参照） |
| Track（トラック設定） | ✅ 移植済み（2026-08-23、Phase W2） | `TimelineSettingWindow` の「トラック」タブで追加・名前変更・範囲編集・並べ替え・削除・アクティブ切替に対応 |
| Template（テンプレート） | ❌ 完全未移植 | `TimelineTemplateManager` 自体が SE に存在しない。キーフレームのカテゴリ / テンプレ管理機能ごと持ち込みが必要 |

**B. レイヤー個別編集の受け皿が無い領域**

> **2026-08-23 追記（方針転換）**: 調査の結果、SE 側の全 28 レイヤーに編集 UI（`ITimelineLayer.DrawWindow`）が移植済みでありながら、呼び出し箇所が 1 つも無い（＝コードが死んでいる）ことが判明した。そのため「レイヤー UI は接続せず SE 各ウィンドウへ吸収する」方針を改め、汎用ホスト `TimelineLayerWindow` から `DrawWindow` を描く方式へ切り替えた（ユーザー判断）。**以下の「受け皿なし・不足」の領域は、この接続により GUI から編集できる状態になっている**。SE ネイティブなウィンドウへの置き換えは、需要の高い領域から段階的に判断する。

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

### Phase W1: タイムライン設定 UI ✅ 完了（2026-08-23）

実装: `TimelineSettingWindow`（`EditorSubWindow` 派生、内部タブ 個別 / 共通）。TimelineWindow のコントロールパネルの「設定」ボタン、またはメニューバー「Window > タイムライン設定」から開く。計画は `docs/superpowers/plans/2026-08-23-timeline-phase-w1-setting-ui.md`。

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

#### 実装時のスコープ判断（2026-08-23）

**SE 側に適用経路が無いため出さなかった項目**（GUI を出しても何も起きないため。実装するには受け皿側の機能追加が先に必要）:

| 項目 | 理由 |
|---|---|
| アスペクト比 / レターボックス透過度 | `aspectWidth` / `aspectHeight` / `letterBoxAlpha` を参照する描画コードが SE に無い（MTE ではカメラのレターボックス描画が消費していた） |
| オフセット時間 / フェード時間 | `startOffsetTime` は DCM CSV 出力のみで参照。`endOffsetTime` / `startFadeTime` / `endFadeTime` は SE 内で未参照 |
| Trans詳細表示数 / Tangent表示数 / 自動で BackgroundCustom に登録 / 動画先読み秒数 / 簡易設定の表示切り替え | 対応する config フィールドが SE 内で未参照 |
| ウィンドウ幅・高さ・ボーンリスト幅 | ウィンドウサイズは SE の EditorSubWindow がドラッグリサイズと config 保存を担う。ボーンリスト幅は TimelineWindow 上のドラッグで調整可能 |

**追加した項目**: ライト / モデル / モデルボーン / モデルシェイプのタンジェント補間トグル。MTE では各レイヤーの DrawWindow にあったが、SE はレイヤー UI を接続しない方針のため個別設定タブへ集約した。

**併せて直したもの**: `TimelineData.isBackgroundVisible` の setter で、背景表示の切替時に地面色レイヤーを再適用する経路を復元（BGColorTimelineLayer 未移植だった頃の名残で無効化されていた）。

**実機確認済み（2026-08-23）**:

- 設定ウィンドウが表示され、個別 / 共通 / トラックの各タブが描画される
- 個別設定タブに実タイムラインの値が反映される（格納ディレクトリ名・フレームレート 30・メイド目線・1フレーム調整・顔/瞳の固定化 ON・タンジェント補間 カメラ ON）
- トラックタブを表示した後に個別タブへ戻してもレイアウトが崩れない（padding 復元の確認）
- 「地面色表示を背景表示と連動」の経路: 背景表示を OFF にすると地面色が適用され、例外ログも出ない（本 Phase で復元した再適用パス）

**未確認**: 各設定値を変更したときの再生挙動への反映（フレームレート・ループ・物理無効など）、個別設定の初期化ダイアログ、共通設定の再起動後の永続化

### Phase W2: トラック設定 / タイムラインロード UI ✅ 完了（2026-08-23）

実装: トラック設定は `TimelineSettingWindow` の「トラック」タブ、ロード UI は新規 `TimelineLoadWindow` + `Manager/TimelineLoadManager.cs`。TimelineWindow のコントロールパネルの「一覧」ボタン、またはメニューバー「Window > タイムラインロード」から開く。計画は `docs/superpowers/plans/2026-08-23-timeline-phase-w2-track-load-ui.md`。

- MTE `TimelineTrackUI` 相当（トラック追加・名前変更・開始/終了フレーム編集・アクティブ切替・削除）を移植
- TimelineWindow 内の表示（アクティブトラック範囲のハイライト等）との連動を確認
- MTE `TimelineLoadUI` 相当のロード UI を移植: サムネイルタイル表示（`config.thumWidth/thumHeight`）、ディレクトリ階層の移動、一覧の更新、格納フォルダをエクスプローラで開く
  - サムネイルは SE でも保存時に出力済みのため、表示側の実装のみ
  - 既存のコンボボックス簡易ロードを残すか置き換えるかは計画時に決める
- 成果物: トラック運用と、サムネイル付き一覧からのタイムラインロードが GUI で完結する状態

#### 実装時の判断（2026-08-23）

- **既存のコンボボックスは残した**: タイル一覧は目で選べる一方、コンボは 2 クリックで開ける。役割が違うため置き換えず併存させた
- **一覧構築は `ScenePresetManager` の流儀に合わせた**: サムネ付きタイル一覧・階層移動・更新は SE のプリセット一覧に実績があるため、そちらへ寄せた。ツリー構築の処理は `ScenePresetManager` とほぼ同じだが、移植元 MTE との対応を追えるようにするため共通化していない
- **保存時に一覧を作り直す**: 保存したタイムラインが一覧に出ないと「保存できていない」ように見えるため
- **ジャンクション対策**: 保存先はユーザー環境配下でジャンクションが張られうる。無限再帰は `StackOverflowException` になり握れないため、訪問済みフォルダは辿らない

**実機確認済み（2026-08-23）**:

- トラックタブに読み込み中タイムラインの 4 トラックが範囲・アクティブ状態つきで表示される
- ロードウィンドウがサムネイル付きのタイル一覧で表示され、フォルダを辿れる
- 「更新」で一覧が作り直される（12 フォルダ + 1 ファイルで 310ms）
- 一覧を 5 回作り直してもテクスチャ数が基準値へ戻る（同フレーム内は破棄待ちで一時的に増えるだけ）

**未確認**: トラックの追加・名前変更・並べ替え・削除の操作、アクティブトラック範囲でのループ再生、タイル選択によるロード、「開く」でのエクスプローラ起動、保存直後の一覧反映（保存はユーザーデータを上書きするため実施していない）

### Phase W3: レイヤー編集ウィンドウ（DrawWindow 接続） ✅ 完了（2026-08-23）

実装: 新規 `TimelineLayerWindow`（`EditorSubWindow` 派生）。ヘッダーでレイヤーと操作対象メイドを選び、本体で `currentLayer.DrawWindow(view)` を呼ぶ薄いホスト。TimelineWindow の「編集」ボタン、またはメニューバー「Window > レイヤー編集」から開く。計画は `docs/superpowers/plans/2026-08-23-timeline-phase-w3-layer-edit-window.md`。

実装上の判断:

- **ホスト側でスクロールビューを張らない**: レイヤーの `DrawWindow` は 24 ファイルが内部で `BeginScrollView` を張り、`GUIView` はネストしたスクロールに対応しないため。内部スクロールを持たないレイヤー（BGColor / BG / Camera / Morph / Move / PngPlacement / SubCamera / Undress / Voice / PostEffect 系）向けに、既定サイズと最小サイズを大きめ（480x560 / 下限 400x400）に取っている
- **1 レイヤーの例外を切り離す**: 28 レイヤーの `DrawWindow` は本ウィンドウが初めて実行する経路のため、描画例外を捕まえてラベル表示に留める（同じレイヤーのログは 1 回だけ出す）
- **レイヤー側のコードは変更していない**: 破綻するレイヤーが出た場合は記録して別途対応する

**実機確認済み（2026-08-23）**:

- **28 レイヤーすべてを一巡し、描画例外ゼロ**（`_drawFailedLayerType` が全レイヤーで null、エラーログなし）
- モデルボーン（ボーン選択 + Transform スライダー）、サイリウム（基本 / バー / 持ち手 / アニメ / エリアのタブとコントローラー操作）、テキスト（本文・フォント・サイズ・整列・色）の編集 UI が正しく描画される
- レイヤーコンボでの切り替えが動作し、タイムライングリッドの行も追従する

**未確認**: 各編集 UI から実際に値を変更したときのキーフレーム登録・再生への反映、コンボポップアップの表示位置、内部スクロールを持たないレイヤー（SubCamera / Camera / PostEffect 等）での下部項目の到達性

---

### Phase W3-旧: レイヤー編集の受け皿整備（大物）— SE ネイティブ化（任意）

> DrawWindow 接続により編集自体は可能になったため、以下は「SE ネイティブなウィンドウへの置き換え」として優先度を下げる。需要を見て個別に判断する。

MTE の各レイヤー DrawWindow が持っていた編集機能を SE ウィンドウへ吸収する。工数が大きいものから独立して進められる。

1. **演出系ウィンドウ**（Psyllium / StageLight / StageLaser）: 新規ウィンドウが必要。MTE の DrawWindow 実装（Psyllium 1794 行が最大）を SE ウィンドウ流儀で再構成。共通パターン（カウント管理 + Transform + 色 + 時間パラメータ）が多いため、3 種まとめて設計する
2. **マテリアル編集**: メイド / モデル / 背景モデルの 3 系統。InspectorWindow への統合か専用ウィンドウかを計画時に決める。走査系（ModelMaterialController / materialMap）は移植済みなので UI のみ
3. **シェイプキー編集**: メイドの任意 blendshape（MaidFaceWindow への「全シェイプキー」タブ追加が候補）とモデルのシェイプキー（BoneEditWindow への統合が候補）
4. **ポストエフェクト編集**: DoF / DistanceFog / GTToneMap / Paraffin / Rimlight の 5 種。専用ウィンドウ 1 枚に集約する
- 成果物: 演出・マテリアル・シェイプキー・ポストエフェクトのキーフレームを GUI で作成・調整できる状態

### Phase W4: レイヤー編集の受け皿整備（小物）— SE ネイティブ化（任意）

> こちらも DrawWindow 接続で編集可能になっているため、置き換えの必要性は需要を見て判断する。

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
