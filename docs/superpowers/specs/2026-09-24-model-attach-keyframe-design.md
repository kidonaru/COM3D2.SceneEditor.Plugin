# モデルのアタッチのキーフレーム化 設計

## 目的

配置モデル（ModItemExplorer プロバイダ経由のモデル）のアタッチ先を、1 本のタイムラインの途中で変えられるようにする。
想定する用途は「持ち替え」: 机の上の小物を途中で手に持つ、右手から左手へ持ち替える、手放して床に置く。

現状、アタッチはモデル単位の固定設定（`TimelineModelData.attachPoint` / `attachMaidSlotNo`）で、キーフレームには無い。MTE 本家にも無い新規機能。

## 確定した方針

- アタッチの正本はキーフレームに一本化する。モデル単位の設定は読込互換のためだけに残す
- 既定の挙動は「キーで切替」。区間中は始点キーのアタッチを保つ
- ワールド座標補間は **終点キー** の bool で ON にする（「このキーへワールド補間で入る」）

## 1. キーフレーム値（`TransformDataModel`）

値数を 12 → 15 にする。既存 index 0〜11（位置 3・回転 4・拡縮 3・easing・表示）は変えない。

| index | キー | 表示名 | 種別 | 既定値 |
|---|---|---|---|---|
| 12 | `attachMaidSlotNo` | アタッチ先 | `CustomValueUIType.MaidSlot`（-1 = なし） | -1 |
| 13 | `attachPoint` | アタッチ部位 | `CustomValueUIType.AttachPoint`（新設、`PhotoTransTargetObject.AttachPoint`） | `Head` |
| 14 | `worldLerp` | ワールド補間 | Bool | 0 |

- いずれもタンジェント補間の対象外（`lerpKinds` で Hold）。区間中は始点キーの値
- 旧データ（`xml.values.Length <= 12`）は不足分が 0 で埋まりメイド 0 へアタッチされてしまうため、`TransformDataModel.FromXml` で -1 / `Head` / 0 に補正する（`TransformDataCamera.FromXml` と同じ作法）
- `attachPoint` の値が `Null` のとき、または `attachMaidSlotNo < 0` のときは「アタッチなし」とみなす

## 2. 再生時の適用（`ModelTimelineLayer.ApplyMotion`）

### 区間開始（`indexUpdated`）

1. 始点キーのアタッチ先（メイド・部位）がモデルの現在状態と違えば付け替える
2. その後にローカル位置・回転・拡縮・表示を入れる（プロバイダの Attach はローカル位置・回転を 0 に戻すため、必ず付け替えの後に書く）

付け替えは履歴を積まない専用経路（`StudioModelManager.ApplyAttach` 相当、新設）で行う:
- SE の `RequestHistory("アタッチ変更")` を呼ばない（UI 操作の `UpdateAttachPoint` とは分ける）
- プロバイダ側の配置履歴も積まない（`ExternalModelHack.BeginBatch` / `EndBatch` で囲む）
- 現在状態と同じなら何もしない（毎区間のプロバイダ呼び出しを避ける）

### 区間内の補間

- **終点キーのワールド補間が OFF**: 現状どおりローカルでタンジェント補間する。アタッチ先が違う区間でも補間値はローカルとして入れ、終点キーでの付け替えで飛ぶ
- **終点キーのワールド補間が ON**: 毎フレーム以下を計算し、ワールドの `position` / `rotation` に書く
  - 始点ワールド姿勢 = 始点キーの親 Transform × 始点キーのローカル位置・回転
  - 終点ワールド姿勢 = 終点キーの親 Transform（そのフレームのボーン姿勢）× 終点キーのローカル位置・回転
  - 割合 `r = (t - t0) / (t1 - t0)` で位置は `Vector3.Lerp`、回転は `Quaternion.Slerp`
  - 拡縮はローカルのままタンジェント補間する
  - タンジェントはローカル座標系の傾き（値の単位を持つ）なのでワールド補間には使わない。緩急は中間キーで付ける
- 区間中のモデルの親は始点キーのアタッチ先のまま。ワールド setter で書くので親に依らず正しい位置になる

### 親 Transform の解決

- アタッチあり: `MaidCache.GetAttachPointTransform(attachPoint)`（`ExternalModelHack.UpdateAttachPoint` と同じ解決）
- アタッチなし: プロバイダの配置ルート。`ExternalModelHack` が未アタッチ状態のモデルを見たときの `transform.parent` を stat に覚えておき、それを使う
- 親が解決できない（メイド不在・ボーン不在）ときはワールド補間を諦め、ローカル補間にフォールバックする

## 3. キー登録と UI

- `ModelTimelineLayer.UpdateFrame` はモデルの現在状態（`attachMaidSlotNo` / `attachPoint`）をキーへ書く
- ワールド補間はキー固有の設定なので、同じフレームに既存キーがあればその値を引き継ぐ（無ければ既定 0）
- モデル行（`ModelManageRowDrawer`）のメイド・部位コンボは「現在のアタッチ状態を変える」操作として残す。既存どおり UI 操作では「アタッチ変更」の Undo 履歴を積む。キーへの記録は通常のキー登録で行う
- キーフレームインスペクタ（`KeyFrameInspector`）と一括編集（`KeyFrameBatchDrawer`）にアタッチ先・部位のコンボとワールド補間のトグルを出す
  - 部位コンボ用に `CustomValueUIType.AttachPoint` を新設し、`MaidFollowCustomValueDrawer` の対象に加える（候補は `BoneUtils.AttachPointNames`）

## 4. XML 互換（version 36 → 37）

- `TimelineData.CurrentVersion` を 37 にする
- `TimelineXml.Initialize` に `version < 37` 段を追加: `<Models>` の各モデルの `AttachPoint` / `AttachMaidSlotNo` を、モデルレイヤーの同名キー全部の index 12 / 13 へ書き込む（index 14 は 0）
  - 旧キーは値数 12 なので、この段の時点で 15 値へ拡張してから書く
- 保存時はモデル単位の `AttachPoint` / `AttachMaidSlotNo` を書き出さない（`TimelineModelXml` のフィールドは読込互換のため残す）
- `StudioModelManager.SetupModels` はアタッチなしでモデルを生成・復元する。アタッチはレイヤーの `ApplyCurrentFrame` で入る
- CLAUDE.md の「タイムライン XML の互換方向」に追記: version 37 でモデルのアタッチはキー（15 値）へ移る。MTE では 15 値のモデルキーは読めるがアタッチは失われる

## 5. テスト

単体テスト（`source/COM3D2.SceneEditor.Plugin.Tests/`、Unity ネイティブ呼び出し不可の制約に従う）:
- 12 値の旧モデルキーを読むと index 12〜14 が -1 / `Head` / 0 になる
- v36 XML のモデル単位アタッチが v37 移行で全キーへ移り、保存 XML にモデル単位の値が出ない
- `UpdateFrame` 相当で既存キーのワールド補間フラグが引き継がれる

実機（devbridge）:
- 10F アタッチなし → 20F 右手（ワールド補間 OFF）で 20F に付け替わる
- 同 ON で 10〜20F が手へ滑らかに寄る
- 再生・シークで SE / ModItemExplorer の Undo 履歴が増えない
- v36 の保存データを読んで、以前と同じアタッチで再生される

## 範囲外

- 背景モデル・PNG オブジェクトのアタッチ
- アタッチ変更時にワールド位置を保つ変換（付け替えるとボーン原点へ移る現状のまま）
