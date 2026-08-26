# 変更追跡(チェックボックス)横展開ロードマップ

表情モーフで実装した「変更追跡チェック」(チェック済み項目のみプリセット保存・タイムラインのボーンメニュー表示/キー書き込み対象にする仕組み)を、他の編集領域・タイムラインレイヤーへ広げるためのロードマップ。

作成日: 2026-08-26
前提: `2026-08-26-face-morph-modified-tracking-design.md`(表情での初回実装、feature/timeline-window で完了済み)

## 1. 現状(表情での as-built)

コミット c17ef3d〜eed7ab9 で実装済み。仕組みの構成要素:

| 部品 | 実体 | 汎用性 |
|---|---|---|
| 変更追跡ストア | `MaidManipulation/FaceEditStore.cs`(名前集合 + version カウンタ) | **完全汎用**。表情固有要素はコメントのみ |
| Maid 別レジストリ | `MaidManipulation/FaceEditManager.cs`(ManagerBase、死亡メイド掃除 + シーン遷移クリア) | ほぼ汎用。キーが `Maid` 固定 |
| チェックボックス行 UI | `MaidFaceWindow.DrawMorphList` にインライン実装(`DrawToggle(bool,20,20,…)` + BeginHorizontal) | ヘルパー未抽出 |
| プリセット絞り込み | `ScenePresetManager` / `MaidFacePresetManager` の Capture を `IsModified` フィルタへ、Apply で `SetNames` 復元 | 領域ごとに 15 行程度の定型 |
| 履歴 | `FaceSnapshot` にチェック集合を含め、集合だけの変更も undo 可能 | 領域ごとに要対応 |
| タイムライン絞り込み | `MorphTimelineLayer`: `allBoneNames` = チェック済み ∪ 既存キー記載、`Update()` で store.version をポーリング(+30F 間引き)してメニュー再構築・**チェック追加時の 0F 自動キー**・**解除時の全フレームキー削除** | アルゴリズムは汎用だが `FaceEditStore` + 固定テーブル前提で実装 |

`UpdateFrame` が `allBoneNames` を回す既存イディオムのおかげで、**allBoneNames を絞ればキー書き込みの絞り込みは自動で付いてくる**。これが横展開の主レバー。

## 2. 設計方針(全 Phase 共通ルール)

1. **書き込み絞り込みの適用基準**: 行ごとの値が独立な領域(モーフ・シェイプキー・マテリアル)のみキー書き込みも絞る。**ポーズ系(MotionTimelineLayer のコアボーン)は表示絞り込みのみ**。ボーンは連動して動くため、キーからの間引きは補間の意味を変えてしまう。
2. **ソース・オブ・トゥルースは編集側ストア 1 つ**。タイムライン側に既存の opt-in 機構(`TimelineData.maidShapeKeysMap` / `extendBoneNames`、TimelineXml へ永続化・イベント駆動)がある領域では、**ストア → タイムライン opt-in への片方向同期**とし、二重管理を作らない(逆方向はタイムラインロード時の初期化のみ)。
3. **動的名前空間の扱い**: シェイプキー・モデルボーン・マテリアルは衣装/モデル依存の動的リスト。固定テーブル順ではなく現物リスト順で表示し、**現物に無いチェック名は捨てずに保持**する(`BoneEditStore.ReapplySlot` の「構成違いは記録を残したまま飛ばす」方式)。着替え検出には `BoneEditStore.itemFileName` / `DiscardSlotIfItemChanged` のパターンを流用する。
4. **ポーリングからイベント通知へ**: 表情の 30F 間引きポーリングは候補数が少ないから成立している。候補が数百になる領域(モデルシェイプキー等)では `OnShapeKeyAdded/Removed` 流のコールバック再構築を優先する。
5. **Undo 前提の確認**: シェイプキー/マテリアル編集ウィンドウは SE HistoryManager 未対応(`timeline-remaining-work.md` §4)。チェック集合を履歴に含める場合は、先にその領域の History スコープ整備が必要(各 Phase に前提タスクとして明記)。

## 3. 対象領域の評価

### 展開する(ペイオフ順)

| 領域 | 編集ウィンドウ | タイムラインレイヤー | 行数 | 既存資産 | 主な障害 |
|---|---|---|---|---|---|
| メイドシェイプキー | ShapeKeyEditWindow | ShapeKeyTimelineLayer | 数十〜数百/衣装 | `TimelineData.maidShapeKeysMap` + `OnShapeKeyAdded/Removed`(イベント駆動再構築が既にある) | ストアと map の二重管理回避、着替え検出、Undo 未対応、プリセット適用が未保存タグをゼロ化しない非対称 |
| モデルシェイプキー | ShapeKeyEditWindow(モデルタブ) | ModelShapeKeyTimelineLayer | 数百/モデル | 名前がモデル修飾済みで文字列集合がそのまま使える | モデルリスト再構築で全消しされる(`StudioModelManager`)、Undo 未対応 |
| モデルボーン | BoneEditWindow | ModelBoneTimelineLayer | 50〜200+/モデル | **BoneEditStore が「編集済み集合」そのもの**。編集済み表示(`" *"`)も既にある | store に version が無い、`transform.name` とモデル修飾名のマッピング |
| マテリアル系 | MaterialEditWindow | MaidMaterial / ModelMaterial / BGModelMaterial | 数十/スロット | `ModelMaterialTimelineLayer` に 0F 自動キーの既存ヘルパー `AddFirstBones` あり | ストア未整備、Undo 未対応 |
| メイドモーション(コアボーン) | — | MotionTimelineLayer | 80〜100+ | 拡張ボーン側には opt-in(`extendBoneNames`)が既にある | **表示絞り込みのみ**(方針 1)。編集ウィンドウが無いのでチェック UI の置き場所設計 |

### 展開しない(スキップ)

| 領域 | 理由 |
|---|---|
| 指(MaidFingerWindow / MotionTimelineLayer の FingerBlend) | メニュー 4 行のみ。プリセットも `isDefault` で絞り込み済み |
| 視線/目(EyesTimelineLayer) | 固定 6 行のみ |
| Camera / Move / Light / BG / Dress / Undress / PostEffect / 演出系ほか | 行数が少ない固定集合で絞る価値がない |

## 4. ロードマップ

### Phase M0: 共通基盤の汎用化 ✅ 完了 (2026-08-26)

表情実装から汎用部分を抽出し、以降の Phase の定型コストを下げる。

- `FaceEditStore` → `EditTargetStore` へリネーム(コード変更ゼロの改名。ジェネリクスは不要 — 全領域が string キー)。`FaceEditManager` は `EditTargetStore` を持つ表情用レジストリとして存続
- チェックボックス行ヘルパーの抽出(`GUIView` またはウィンドウ基底に「チェック + ラベル + スライダー」行を 1 呼び出しで描く形。MaidFaceWindow を置き換えて動作等価を確認)
- `MorphTimelineLayer` の「version ポーリング → 差分検出 → InitMenuItems / 0F 自動キー / 解除時キー削除」を `TimelineLayerBase` の opt-in 部品へ抽出(`protected virtual EditTargetStore trackedStore => null` + 候補名リスト。null の既存レイヤーは挙動不変)。既存ヘルパー `AddFirstBones` との統合可否もここで判断
- テストを `EditTargetStoreTests` へ一般化

完了条件: 表情の既存挙動が全て維持され(テスト + 実機確認)、新規領域が「ストア生成 + ウィンドウにチェック行 + レイヤーに trackedStore 指定 + プリセット 2 箇所」だけで載る状態。

実装の要点:

- `EditTargetStore`(旧 `FaceEditStore`)— API・実装は無変更。テストは `EditTargetStoreTests` へ
- `GUIView.DrawTrackedSliderValue` / `DrawTrackedToggle` / `TrackedCheckWidth`(20f)— チェック 20px + 行本体を 1 呼び出しで描く(submodule MTEUtils)
- `Timeline/TimelineLayer/TimelineLayerBaseTracking.cs` — `trackedStore` / `trackedCandidateNames` / `trackedHistoryPrefix` の 3 つを override するだけで、メニュー絞り込み・0F 自動キー・解除時キー削除が有効になる。`TimelineLayerBase.Update()` の既定実装が opt-in 時のみ `UpdateTrackedBoneFilter()` を呼ぶ
- 既存ヘルパー `AddFirstBones` / `RemoveAllBones` との統合は**見送り**(履歴文言・`CleanFrames`/`ApplyCurrentFrame` の有無・`_dummyLastFrame` の扱いが異なり、統合すると振る舞いが変わるため)

### Phase M1: モデルボーン ✅ 完了 (2026-08-26)

`BoneEditStore` が既に「編集済みボーン集合」を持つため、ストア新設が不要。

- `BoneEditStore` に version カウンタを追加(RecordEdit / ResetBone / ResetSlot / Restore 系で増分)
- `transform.name` ↔ ModelBoneTimelineLayer のモデル修飾名(`model.bones` の name)の対応表を整備
- `ModelBoneTimelineLayer` に M0 部品を接続(メニュー絞り込み + 0F 自動キー + 解除時キー削除。書き込みも絞る — モデルボーンは行独立が前提の編集なので方針 1 の例外にしない。判断に迷えば表示のみへ後退)
- BoneEditWindow のボーンツリーへチェック表示(既存の編集で自動チェック相当は RecordEdit が担う。チェック解除 = ResetBone)
- シーンプリセットのモデルボーン保存は既に BoneEditStore 由来(2026-08-20 実装)のため変更不要のはず — 差分が無いことの確認のみ

実装の要点:

- `BoneEditStore` へエントリ集合の version を追加(記録の増減でのみ進む)。値の更新では進めない — タイムラインのメニュー再構築は集合の変化だけを見ればよいため
- `ModelQualifiedNames` — 編集側の生名 (`transform.name`) とタイムライン側のモデル修飾名 (`"{model.name}/{生名}"`) の橋渡しを 1 箇所に閉じ込めた。M2 と共有する
- `ModelTrackedNameStore<TKey>` — モデルごとの記録を修飾名の集合へ集約する読み取り専用ビュー。**修飾名を記録に使わない**のが要点で、`ModelHackManager.FixGroup` の group 振り直しで修飾名は変わるため、生名を正として毎回組み直す。モデルの増減は記録側の version を動かさないので `Invalidate()` で明示的に作り直させる
- `BoneEditWindow` のボーンツリーへチェック列を追加(submodule MTEUtils の `GUITreeView` 対応が必要だった)
- シーンプリセットのモデルボーン保存は既に `BoneEditStore` 由来のため変更なし(想定どおり)

### Phase M2: モデルシェイプキー ✅ 完了 (2026-08-26)

- モデル用 `EditTargetStore` レジストリ(キーは `GameObject` または `StudioModelStat`。`BoneEditManager.GetModelStore` と同型)
- ShapeKeyEditWindow モデルタブへチェック行(スライダー操作で自動チェック、解除で 0 + キー削除)
- `ModelShapeKeyTimelineLayer` へ M0 部品接続。`StudioModelManager` のリスト全消し再構築(`boneNames`/`blendShapeNames` clear)に耐えるよう、モデルリスト側の世代カウンタも再構築トリガーに含める
- シーンプリセット `modelShapeKeys` の保存をチェック済みフィルタへ
- 前提タスク: モデルシェイプキー編集の HistoryManager 対応(チェック集合を履歴へ含めるため。対応コストが高ければ「履歴はチェック集合を含めない」と明記して先送り可)

実装の要点:

- `ModelShapeKeyEditManager` — キーは `GameObject`。M1 の `ModelTrackedNameStore` をそのまま再利用し、集約だけを差し替えた
- **`StudioModelManager.onModelAdded/onModelRemoved` の購読は解除しない**。`Init` はプラグイン起動時の 1 回だけだが `OnPluginDisable` は UI をトグルするたび呼ばれるため、そこで解除すると UI を一度閉じただけで購読が復活しなくなる
- モデル名の解決は `BlendShapeController.model.name`。コントローラはタイムラインのロード時にしか付かないため、未ロードのうちは解決できず `ModelTrackedNameStore` の再試行 (30F ごと) に任せる
- シーンプリセット `modelShapeKeys` の保存はチェック済みのみ(重み 0 でも意図して選んだものは残す)
- チェック集合は履歴に含めない(モデルシェイプキー編集は SE の HistoryManager 未対応。対応コストに見合わないと判断した縮退)

### Phase M3: メイドシェイプキー ✅ 完了 (2026-08-27)

- **設計確定を最初に行う**(このフェーズ最大の作業): per-Maid `EditTargetStore` を唯一のソースとし、`timeline.AddMaidShapeKey/RemoveMaidShapeKey` へ片方向同期する(方針 2)。タイムラインロード時は maidShapeKeysMap → ストアへ初期反映。`timeline-remaining-work.md` §3 の「タグ登録はレイヤー編集ウィンドウの責務」という過去決定の更新を含む
- ShapeKeyEditWindow メイドタブへチェック行。着替えで消えたタグの扱いは方針 3(保持 + 表示は現物リスト準拠)
- `ShapeKeyTimelineLayer` は既存の `OnShapeKeyAdded/Removed` イベント再構築をそのまま活用(ポーリング不要)
- シーンプリセット `shapeKeys` の保存をチェック済みフィルタへ。**適用側の非対称に注意**: 現状は未保存タグをゼロ化しない(表情と違い「保存集合 = チェック集合」の等式が崩れる)。適用時にゼロ化まで揃えるかはこのフェーズで仕様決定する
- 前提タスク: シェイプキー編集の HistoryManager 対応(M2 と共通化)
- 関連する既知問題: ShapeKey/Eyes レイヤーと MaidFaceWindow の相互排他(`timeline-remaining-work.md` §4)はこのフェーズで解決しない(スコープ外と明記)

実装の要点:

- **タイムライン側は既に完全な opt-in 機構を持っていた**(`ShapeKeyTimelineLayer.allBoneNames` = `timeline.GetMaidShapeKeys(slotNo)`、`OnShapeKeyAdded` が 0F 自動キー、`OnShapeKeyRemoved` がキー削除)。そのため **M0 の追跡部品は接続していない**。やったのはタグ登録の主導権の移動だけ
- ソース・オブ・トゥルースは per-Maid `EditTargetStore`(`MaidShapeKeyEditManager`)。タイムラインへは毎フレーム差分同期し、`ShapeKeySyncDiff` が実際に変わったタグだけを出す(`Add/RemoveMaidShapeKey` は 0F キーとキー削除を伴う重い操作のため)
- **取り込み契機は `OnLoad` ではなく「タイムラインの参照が変わったこと」**。SE の `ManagerRegistry.OnLoad()` は UI の有効化でしか走らず、タイムラインのロードでは呼ばれない(`TimelineManager` が呼ぶ `mte.OnLoad()` は `MotionTimelineEditor` の別リストを回す)
- 取り込みは**タグを持つスロットだけ**反映し、空ならストアを保持する。こうしないとタイムラインの新規作成でチェックが全部消える
- レイヤー編集ウィンドウのタグトグルもストア経由へ変えた。`Add/RemoveMaidShapeKey` を直接叩く経路は `MaidShapeKeyEditManager` の 1 箇所だけになった
- シーンプリセットは v24。保存はチェック済みのみ(表情モーフは従来どおり除外)、**適用は積み増しで未保存タグのゼロ化はしない**(表情の v22 の `SetNames` とは非対称。ゼロ化しないのにチェックだけ消すと値が残ったまま追跡から外れるため)
- チェック集合は履歴に含めない(M2 と同じ縮退判断)。**M4 でも同じ判断を引き継ぐか要検討**
- `TrackedDirtyGate` — 「世代 + version 合計」で前回からの変化を見る門番。M1/M2 の `ModelTrackedNameStore` と共有する

### Phase M4: マテリアル系

- マテリアル用ストア(キー: メイドは Maid、モデルは M2 と同じモデルキー)
- MaterialEditWindow へチェック行、`MaidMaterialTimelineLayer` / `ModelMaterialTimelineLayer` / `BGModelMaterialTimelineLayer` へ M0 部品接続
- シーンプリセットのマテリアル保存(`slotMaterials` / `modelMaterials` / `bgMaterials`)をチェック済みフィルタへ
- 前提タスク: マテリアル編集の HistoryManager 対応

### Phase M5(任意・需要判断): メイドモーションのメニュー表示絞り込み

- **表示のみ**。キー書き込み・0F 自動キー・解除時削除は行わない(方針 1)
- コアボーン約 67 本に表示 opt-in を追加(拡張ボーンの `extendBoneNames` と対になる仕組み。チェック UI の置き場所はレイヤーウィンドウ内が第一候補 — 専用編集ウィンドウが無いため)
- 既定は全表示(現状維持)にし、絞り込みはユーザーが明示的に有効化する形とする

## 5. 主要リスクと対応

| リスク | 対応 |
|---|---|
| ストアと `TimelineData` opt-in の二重管理で不整合 | 方針 2 の片方向同期を M3 設計確定で文書化。同期の向きをテストで固定 |
| 動的名前(着替え・モデル差し替え)でチェックが黙って陳腐化 | 方針 3(保持 + itemFileName 方式の無効化検出)。M2/M3 でそれぞれ実機確認項目化 |
| 候補数百 × キーフレーム走査のポーリングコスト | M0 でイベント/世代カウンタ駆動へ寄せ、30F 間引きポーリングは表情限定の暫定実装として残さない |
| レイヤー `Update()` はタイムラインロード中しか回らず、0F 自動キー等が黙って不発 | M0 部品の仕様として明記。チェック操作時にタイムライン未ロードなら何もしない(表情と同挙動) |
| Undo 未対応領域(シェイプキー/マテリアル)で履歴の置き場が無い | 各 Phase の前提タスクとして History 対応を先行。コスト過大なら「チェック集合は履歴外」と明記して縮退 |
| M0 の抽出リファクタで表情のデグレ | 既存テスト + 実機確認チェックリストを M0 完了条件に含める |

## 6. 進め方

- 1 Phase = 1 計画(superpowers:writing-plans)+ plan-review + 実装 + code-review の通常フロー
- 順序は M0 → M1 → M2 → M3 → M4 →(需要あれば M5)。M1 と M2 は独立なので入れ替え可。M3 は設計確定が重いので、M1/M2 で M0 部品の実績を作ってから着手する
- 各 Phase でシーンプリセットのスキーマバージョンを上げる場合は `ScenePresetData.cs` のバージョン履歴コメントに追記(表情の v22 と同様、構造変更なし・保存対象の選別ルール変更のみが原則)
- 実機確認はゲーム再起動のタイミングでまとめて行う(確認項目は各 Phase の計画に記載)
