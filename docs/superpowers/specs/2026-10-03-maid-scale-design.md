# メイドスケール（嘘パース用の腕ボーン拡縮）設計

## 目的

カメラに近い腕（主に手・前腕）をキーフレームごとに大きくして、遠近感を誇張する（嘘パース）。

成功条件:

- 左右の上腕・前腕・手を、メイドごとに均一の倍率で拡縮できる
- 倍率をタイムラインのキーとして打て、補間で再生される
- 体型スライダー由来の元スケールを壊さない（元の値に倍率を掛ける）
- レイヤーを外す、または倍率を 1 に戻すと見た目が元へ戻る

## 範囲

- 含めるもの
  - 対象の骨: `Bip01 L/R UpperArm`、`Bip01 L/R Forearm`、`Bip01 L/R Hand` の 6 本
  - 均一スケールのみ。子の骨へ継承させ、打ち消しはしない
    - 「手」を拡大すると手と指が大きくなる
    - 「前腕」を拡大すると前腕・手・指が大きくなり、腕も伸びる
  - 編集 UI は Inspector の行だけ
- 含めないもの（必要になったら別途設計する）
  - 脚や頭など、腕以外の骨
  - XYZ 別のスケール
  - 子へ継承させない打ち消し
  - SceneView でのギズモ操作
  - 撮影モード（既存の方針どおり非対応）

## 実機調査で分かった前提

COM3D2.5 で、旧ボディのメイドを devbridge で調べた結果。

- **書き込み先は複製骨**: メッシュは `TBodySkin` ごとに複製された骨（`_SM_xxx/Bip01/...`）にスキニングされている
  - 本体の骨（`m_Bones` 直下の `Bip01`）の `localScale` を変えても見た目は変わらない
  - `TBodySkin.CopyTrans` が本体から複製骨へコピーするのは `localRotation` と `localPosition` だけ。スケールのコピーは `Mune_L/R` などの一部に限られる（`CMT.BindTrans`）
- **書き込みが効く**: body スロットの複製骨で `Bip01 L Hand` を 2 倍にすると、手が大きく表示された
- **上腕と手は値が残る**: 書いた値が、次のフレーム以降もそのまま残る
- **前腕は毎フレーム戻される**: 元の値（例: `(0.996, 1.032, 1)`）へ戻される
  - 書き戻しているのはゲーム本体ではなく、導入済みの MOD **MaidVoicePitch** の `ForeArmFix`。Harmony で `Transform.set_localScale` の呼び出し元を取って特定した
  - 経路は `TBody.LateUpdate → TBody.MoveHeadAndEye → MaidVoicePitch のコールバック`
  - バニラの環境では戻されない見込み。ただし、この MOD を入れている利用者は多い
- **描画直前（Camera.onPreCull）に書いても効かない**: COM3D2.5 の Unity 2022.3 は、スキニングを描画より前（PostLateUpdate）に計算する
  - onPreCull で手を 2 倍にし、onPostRender で戻す実験をしたところ、見た目は変わらなかった
  - 同じ手を Update 中に 2 倍にすると、大きく表示された
- **`TBody.LateUpdate` の後ろで書けば効く**: Harmony の postfix で前腕を 2 倍にすると、前腕と手が大きく表示された（ForeArmFix の後になる）
  - `TBody.LateUpdate` は最後に全スロット・全サブスロットの `TBodySkin.Update`（2.5 の CRC は `SelfLateUpdate`）を回す
- **元のスケールは 1 ではない**: `UpperArm` に `(0.90, 1, 1)` のような体型由来の値が入っている。上書きせず、元の値に倍率を掛ける
- **.anm はスケールを書かない**: 既存のメイドアニメが生成する .anm は、7 チャンネルまでに制限していてスケールを書かない（`MotionTimelineLayer.GetAnmBinaryInternal`）。そのためアニメーションとはぶつからない

## 方式

**`TBody.LateUpdate` の後ろ（Harmony の postfix）で、そのメイドの全スロットの複製骨へ倍率を掛ける。次の `TBody.LateUpdate` の前（prefix）と、プラグインの `Update` で元へ戻す。**

- **掛ける時点**: メイドごとの `TBody.LateUpdate` の postfix
  - CopyTrans や MOD（MaidVoicePitch の ForeArmFix）の書き込みが済んだ後で、スキニングの計算より前になる
  - メイドごとに 1 フレーム 1 回なので、カメラの台数に関係なく重ならない
- **掛け方**: その時点の `localScale` を読んで退避し、`退避値 × 倍率` を書く
- **戻す時点**: 次のフレームのプラグイン `Update`、および次の `TBody.LateUpdate` の prefix（二重掛けを防ぐ保険）
  - 戻すのは、今の値が自分の書いた値のままの骨だけにする。体型スライダーなどが途中で書き直した値を、古い退避値で潰さないため
  - 毎フレーム「その時点の値」に掛けるので、体型スライダーを動かしても倍率が積み上がらない
  - `Update` の実行順は決まっていないので、プラグインより先に走るスクリプトには倍率つきの値が見える。腕の複製骨を読み、加工して書き戻すゲーム側の処理は、体型の Blend（変更時だけ）のほかに見当たらないので受け入れる
- **プラグインが無効（`config.pluginEnabled` が false）のとき**: 掛けない（戻す処理は動かす）
- **本体の骨には触らない**: IK、ポーズ編集のハンドル、アタッチ位置はスケールの影響を受けない
  - スロット obj 配下にある骨（`IsChildOf`）だけを対象にし、本体の骨を参照する SkinnedMeshRenderer があっても書かない

採らなかった案:

- **描画直前（Camera.onPreCull）に掛ける**: スキニングに間に合わず効かない（上の実機調査）
- **プラグインの LateUpdate で掛ける**: プラグインの LateUpdate は `TBody.LateUpdate` より先に走るので、前腕は MaidVoicePitch に戻される
- **`TBody.OnLateUpdate` / `onLateUpdateEnd` のイベントに登録する**: 2.0 と 2.5 で型と寿命が違う。2.5 はボディ読込時に `Clear` し、2.0 は毎フレーム null にする。Harmony なら両方で同じ `TBody.LateUpdate` に差し込める

## 構成

### データ: `TransformDataMaidScale`

- `TransformType.MaidScale` を追加する
- 値は 1 個（均一の倍率）。初期値は 1
- タンジェント補間あり（`tangentValues = values`）
- `TimelineIntegration` で `RegisterTransform` する

### レイヤー: `MaidScaleTimelineLayer`

- `[TimelineLayerDesc("メイドスケール", 19, TimelineLayerCategory.Maid)]`、`hasSlotNo = true`
- `GravityTimelineLayer` を雛形にする
- `allBoneNames` は 6 本の骨名。メニューの表示名は「左上腕」「左前腕」「左手」「右上腕」「右前腕」「右手」
- **再生中**: ポーズ編集中でなければ `ApplyMotion` で倍率をエルミート補間し、適用器の状態へ書く
- **`UpdateFrame`**: 適用器の今の倍率からキーを作る
- **`OnPoseEditEnd`**: 再生データを適用し直す
- **レイヤーを外したとき**: 担当メイドの倍率をすべて 1 に戻す
- **XML**: 新しいレイヤーとして保存するだけで、`TimelineData.CurrentVersion` は上げない
  - キーの `<Type>MaidScale</Type>` は MTE の `TransformType` に無い enum 値なので、このレイヤーを含む XML は MTE ではデシリアライズに失敗する（表情の `FaceSetting` と同じ。一方向互換の方針の範囲内）
  - CLAUDE.md の互換方向の節に追記する

### 状態と適用: `MaidScaleController`

- `MaidGravityController` と同じく `MaidManipulateManager` が所有する。メイド解除（`Release`）と全破棄（`Destroy`）も同じ場所で呼ぶ
- **状態**: メイドごと・骨ごとの倍率。既定は 1。レイヤー・Inspector・履歴の三者が読み書きする
  - 倍率がすべて 1 に戻ったメイドの状態は捨てる（`HasState` が false になる）
- **`TBody.LateUpdate` の postfix**: 倍率が 1 でない骨について、メイドの全スロット（2.5 はサブスロットも）の複製骨の `localScale` を退避して、倍率を掛ける
- **戻す処理**: プラグインの `Update`（全メイド）と `TBody.LateUpdate` の prefix（そのメイド）で、退避した骨を戻す
- **骨のキャッシュ**: スロット obj ごとに、SkinnedMeshRenderer の `bones` から対象骨を集めてキャッシュする
  - 着替えやボディ再ロードで複製骨は作り直される。スロット obj の並びが変わったら作り直す
  - 着替え中（`IsAllProcPropBusy`）・ボディ読込中は掛けない
  - 戻すときは、破棄済みの Transform（Unity の null 判定）を飛ばす

### パッチ: `MaidScaleLateUpdatePatch`

- プラグインの初期化で 1 回だけ、`TBody.LateUpdate`（2.0 / 2.5 とも private）へ prefix と postfix を当てる。`SkirtHookDriftPatch` と同じ作法にする
- フックに失敗したら、エラーログを出してメイドスケールを無効にする（ゲームは通常どおり動く）

### Inspector: `MaidScaleItemInspector`

- `TimelineItemInspectorRegistry` に登録する
- `GravityItemInspector` と同じく、選んだ骨の行に倍率のスライダーと数値欄（0.1〜3）を出し、リセットボタンを付ける
- 編集すると適用器の状態を書き換える。キー登録は既存の操作（今の状態からキーを作る）に任せる

## 既知の制約（仕様として受け入れる）

- **持ち物のずれ**: 手に持たせたモデルなどのアタッチは本体の骨に付く。前腕・上腕を拡大すると、見た目の手と持ち物がずれる
  - 手だけの拡大なら、手首の位置は変わらない
- **新ボディ（CRC）**: body の SkinnedMeshRenderer は上腕・前腕を直接参照せず、子のツイスト骨（`UpperTwist*` / `ForeTwist*`）だけを参照する。そのため骨は SMR.bones からスロット obj までの祖先も含めて集める（実機で 6 本とも効くことを確認済み）
- **Update 中の読み取り**: プラグインの `Update` より先に走るスクリプトには、倍率つきの複製骨が見える（上の「方式」）

## テスト

**単体テスト**（Unity のネイティブ呼び出しは使わない）

- `TransformDataMaidScale` の初期値・値数
- XML の往復
- 補間の値

**実機（devbridge、通常シーンで旧ボディと新ボディの両方）**

- 6 本それぞれで、倍率 1.5 が見た目に効く（前腕を含む）
- 体型スライダーを動かした後も倍率が積み上がらず、倍率 1 に戻すと元の値と一致する
- 着替え・ボディ再ロードの後も効く
- レイヤーを外すと元へ戻る
- タイムラインの再生で補間される
- ポーズ編集の開始・終了で値が飛ばない
