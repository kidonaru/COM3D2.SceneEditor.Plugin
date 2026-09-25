# ModItemExplorer のアタッチをモデルキーへ取り込む 設計

## 目的

ModItemExplorer（MIE）の UI（MIE の Inspector と操作ウィンドウ）でモデルをメイドへアタッチしたとき、SceneEditor（SE）のモデルキーへ正しく記録されるようにする。

現状（`2026-09-24-model-attach-keyframe` 実装後）の問題:
- MIE は付け替えを `SelfModelPlacer.Attach` の中で完結させ、SE へ知らせる手段がない。SE の stat は「アタッチなし」のまま
- その状態でキーを登録すると「アタッチなし」+ ボーン基準のローカル位置が記録され、保存して読み直すと別の場所に出る
- 部位の一覧が SE と MIE で食い違う。MIE の胸（`Bip01 Spine1a`）・骨盤（`Bip01 Pelvis`）は SE のキーで表せない。SE の原点・右胸・左胸は MIE の UI に無い

## 確定した方針（ユーザー決定）

- 案 A: MIE がアタッチ状態を返す API を追加し、SE がそれを stat へ取り込む。MIE の UI はそのまま使う
- 不足しているボーンは両側に追加して揃える

## 1. SE の部位一覧を拡張する

- ゲームの `PhotoTransTargetObject.AttachPoint`（0〜18）はそのまま使い、SE 独自の値を足す
  - 19 = 胸（`Bip01 Spine1a`）
  - 20 = 骨盤（`Bip01 Pelvis`）
- 独自値は IK 管理外なので、ボーン名で `maid.body0.GetBone` から引く
- 一覧（名前・最大値・独自値のボーン名）は SE 内の新設クラス `ModelAttachPoints` に置く。共有サブモジュール MTEUtils の `BoneUtils` は変えない
- キーの `attachPoint` の上限、キーフレーム詳細の部位コンボ、モデル行の部位コンボは拡張後の一覧を使う

## 2. MIE の部位一覧を拡張する

- `SelfModelPlacer.AttachPoints` に原点（`Bip01`）・右胸（`Mune_R`）・左胸（`Mune_L`）を足す
- SE の「固定」（`IKManager.BoneType.TopFixed`）はボディのモデルオブジェクト（例: `_BO_LOhighpoly_nudism_v2_beta`）で名前がボディごとに変わるため、MIE の固定ボーン名の一覧には入れない
  - SE から固定で付けた場合は、MIE の `AttachByBoneName` が臨時のアタッチポイントを作るので動作する
  - MIE の UI からは選べない

## 3. アタッチ状態の取得 API（プロバイダ任意メンバ）

- MIE の `ModelPlacerProvider` に `Transform GetModelAttachBone(GameObject obj)` を追加する
  - アタッチ中ならその親ボーン、未アタッチ・不明なら null
- SE の `ModelPlacerProviderBinder` は任意メンバとしてバインドする（無いプロバイダでは同期しない）
- `docs-site/dev/model-placer-guest-guide.md`（SE）と `docs/external-plugin-api.md`（MIE、該当節がある場合）に追記する

## 4. SE の同期

- `ExternalModelHack` がモデル一覧を返すたびに（`GetOrCreateStat` の新規・キャッシュ両経路）、`GetModelAttachBone` の結果を stat の `attachPoint` / `attachMaidSlotNo` へ反映する
  - 親ボーン → メイド: `GetComponentInParent<Maid>()` → `MaidManager.GetMaidCache(maid)`
  - 親ボーン → 部位: 拡張一覧の各部位をそのメイドで解決し、同じ Transform を探す
  - 一覧で表せないボーン、SE がキャッシュを持たないメイドは同期しない（stat を変えない）。この場合キーは不正確なまま残るので、同じボーンについて 1 度だけ警告ログを出す
- SE 自身の付け替え（UI の `UpdateAttachPoint`、再生の `ApplyAttach`）は、プロバイダを呼ぶ前に stat を書くので、同期しても差分は出ない
- `StudioModelManager.LateUpdate` で、プロバイダ側 stat とマネージャ側 stat のアタッチが食い違ったときは外部からの変更とみなし、新設イベント `onModelAttachChanged` を発火する

## 5. MIE の UI 変更をキーへ記録する

- MIE の UI からのアタッチは、値を書く前に `AutoEditModeClient.Enter("ModelTimelineLayer")` を呼ぶ
  - 編集モード外ではレイヤーが毎フレーム再生値を書き戻し、アタッチが元に戻るため
  - SE から来た `AttachModel`（再生・読込）では呼ばない
  - MIE の MTEUtils サブモジュールには `AutoEditModeClient` が無いので、SE と同じコミット（`2da6959`、origin/master に push 済み）へ進める
- SE の `TimelineWindow` は `onModelAttachChanged` を受けて `AutoKeyFrameAfterEdit(ModelTimelineLayer)` を呼ぶ。自動登録が ON ならキーへ入る。同じフレームで複数モデルが変わっても登録は 1 回（登録は全モデルの差分をまとめて取るため）
- 検出は `StudioModelManager.LateUpdate` の定期実行（30 フレーム間隔）に乗るため、キー登録は最大で約 0.5 秒遅れる。その間に編集モードを抜けると、変更はキーにならず再生値へ戻る

## 6. 互換

- 部位の値 19・20 は SE 独自。MTE ではモデルキーのアタッチ自体を扱わないため、新たな非互換は増えない
- この変更より前の SE で 19・20 のキーを読むと、`BoneUtils.GetBoneType` の既定値により「固定」へ付く
- CLAUDE.md の「タイムライン XML の互換方向」に、部位 19・20 が SE 独自の値であることを追記する

## テスト

単体テスト（SE、Unity ネイティブ呼び出し不可）:
- `ModelAttachPoints`: 名前は 21 件で末尾が胸・骨盤、独自値のボーン名、`TryFindAttachPoint` の一致・不一致
- キーの `attachPoint` の上限が 20、キーフレーム詳細の部位コンボの変換が骨盤まで通る
- `ModelPlacerProviderBinder` が `GetModelAttachBone` を任意メンバとしてバインドする

実機（devbridge）:
- MIE の UI 経路（`SelfModelPlacer` の UI 用メソッド）で右手・胸・骨盤へアタッチすると、SE の stat とキー（自動登録 ON）がそのアタッチになる
- SE のキーで胸・骨盤へアタッチでき、再生で付け替わる
- 再生・シークで SE の stat が勝手に揺れない（同期で差分が出ない）

## 範囲外

- MIE のアタッチ先メイドの選択（MIE は編集中のメイド固定のまま）
- MIE の UI から「固定」を選べるようにすること
- MIE の Undo/Redo とプリセット復元による付け替え。これらは `AutoEditModeClient.Enter` を通らないため、タイムラインの編集モード外では他の編集と同じく次の同期（約 0.5 秒後）で再生値へ戻る。編集モード中なら通常どおり取り込まれ、自動登録の対象になる
