# 体型スライダー（MaidScale 廃止・ModsSlider 一本化）設計メモ

**状態: 計画化済み（2026-10-06）**。計画は `docs/superpowers/plans/2026-10-06-body-slider.md`。ユーザー FB 対応（`2026-10-05-user-feedback-batch-design.md`）から切り出した調査結果と設計案。変換式の詳細（MVP v0.2.17.6 の逆コンパイル結果から抽出）は計画の「変換表」に転記した。

作成日: 2026-10-05


## 前提（調査結果）

- **AddModsSlider の役割**: ExSaveData（プラグイン名 `CM3D2.MaidVoicePitch`）の値を編集する UI にすぎない。骨に掛けているのは MaidVoicePitch（MVP）。導入済みの MVP は v0.2.17.6。
- **MVP は CRC ボディでは体型スライダーを一切適用しない**: `WideSlider` / `boneMorph_BlendCallback` / `ForeArmFix` は、冒頭で `maid.IsCrcBody` なら return している。
  - したがって **ExSaveData に書く方式は CRC では成立しない**。SE が自分で骨に掛ける方式にする。この方式なら MVP が無い環境でも動く。
- **MVP の掛け方**: `BoneMorph_.Blend` の PostCall で、`linkT`（本体骨と複製骨）に絶対値を書く。
  - 毎フレームではなく、初期化時とスライダー変更時だけ書く。例外として MUNESCL（jiggle の後で乗算）と FARMFIX は毎フレーム書く。
- **SE の値の重ね方**: ユーザーの決定どおり、ExSaveData は上書きしない。各フレームの骨の現在値（MVP の結果を含む）に対して、スケールは乗算、位置は加算する。こうすると、メイドごとの MVP の設定はそのまま残る。

## パラメータ定義

- 対象は ModsParam.xml の体型系のうち、スケールと位置のもの。体型スライダーに回転系は無い。
- 骨・軸・単位の変換式は、MVP の `WideSlider` に合わせる（逆コンパイル結果の約 2605〜3070 行）。
  - スケール: 値 `(height, depth, width)` を骨の `(x, y, z)` へ割り当てる。
  - 位置: `/1000` の項目（THIPOS / THI2POS / HIPPOS）と `/10` の項目がある。軸の入れ替えと、L/R での符号反転を伴う。
  - 名前の `?` は L と R に展開する。
- 定義はコード内の定義テーブル（`BodySliderDefs`）に 1 か所でまとめる。1 エントリに持つもの: prop 名、表示名、種類（scale / pos）、対象骨（L/R 展開あり）、成分の割り当て、単位、スライダーの範囲。
- 主な項目:

| prop | 骨 | 種類 |
|---|---|---|
| THISCL / THISCL2 | Bip01 ?Thigh / Bip01 ?Thigh_SCL_ | scale |
| MTWSCL / MMNSCL | momotwist_? / momoniku_? | scale |
| PELSCL | Bip01 Pelvis_SCL_（Hip_? にも乗算） | scale |
| HIPSCL | Hip_? | scale |
| CALFSCL / FOOTSCL | Bip01 ?Calf / Bip01 ?Foot | scale |
| SKTSCL | Skirt | scale |
| SPISCL / S0ASCL / S1_SCL / S1ASCL | Bip01 Spine / Spine0a / Spine1 / Spine1a の _SCL_ | scale |
| S1ABASESCL | Bip01 Spine1a | scale |
| NECKSCL | Bip01 Neck_SCL_ | scale |
| KATASCL / CLVSCL | Kata_? / Bip01 ?Clavicle | scale |
| UPARMSCL / FARMSCL / HANDSCL | Bip01 ?UpperArm / Forearm / Hand | scale |
| MUNESCL / MUNESUBSCL | Mune_? / Mune_?_sub | scale |
| THIPOS / THI2POS / HIPPOS | Bip01 ?Thigh・Hip_? / Bip01 ?Thigh_SCL_ / Hip_? | pos（/1000） |
| MTWPOS / MMNPOS / SKTPOS | momotwist_? / momoniku_? / Skirt | pos（/10） |
| SPIPOS / S0APOS / S1POS / S1APOS / NECKPOS | Bip01 Spine / Spine0a / Spine1 / Spine1a / Neck | pos（/10） |
| CLVPOS / MUNEPOS / MUNESUBPOS | Bip01 ?Clavicle / Mune_? / Mune_?_sub | pos（/10） |

- **対象外**: 体型以外の項目。WIDESLIDER、PROPSET_OFF、LIPSYNC_OFF、HYOUJOU_OFF、EYETOCAMERA_OFF、MUHYOU、FARMFIX、FACE_OFF、FACEBLEND_OFF、EYETOCAM、HEADTOCAM、EYE_TRACK、HEAD_TRACK、PITCH、MABATAKI、LIPSYNC_INTENISTY、EYEBALL、EYE_ANG。
- **既定値**: スケールは (1,1,1)（乗算なので変化なし）、位置は (0,0,0)（差分なし）。
- **骨ごとの合成**: 1 つの骨に複数の項目が掛かる場合（例: Hip_? = PELSCL × HIPSCL、Hip_? の位置 = THIPOS + HIPPOS）は、骨ごとにスケールを積で、位置を和で合成してから書く。

## 適用

- 既存の `Manager/MaidScaleLateUpdatePatch.cs` を汎用化する。名前は `BodySliderLateUpdatePatch` とする。
  - **postfix**（`TBody.LateUpdate` の後）: 複製骨（`smr.bones` とその祖先のうち slotRoot 配下）に書く。
    - スケール: `localScale = 現在値 × 倍率`
    - 位置: `localPosition = 現在値 + 差分`
    - 書く前の値は記録しておく。
  - **戻し**: 次の prefix / Update で、記録した値と今の値が一致する場合（`ShouldRestore`）だけ戻す。
    - MVP はスケールを Blend 時にしか書き直さないので、戻さないと倍率が積み上がる。
    - 位置は旧ボディでは `CopyTrans` が毎フレーム上書きするが、CRC の `SelfLateUpdate` の挙動が未確認なので、スケールと同じく記録して戻す方式に揃える。
- 本体骨には書かない。IK・アタッチ・ギズモの基準を崩さないためで、MaidScale の既存方針と同じ。
- 撮影中の一時停止・再開（Suspend / ResumeApplied）、着替え中のキャッシュ無効化（`IsAllProcPropBusy`）、Release・Destroy の作法は MaidScale から引き継ぐ。
- 存在しない骨は黙って飛ばす。CRC で無い骨がありうる。

## MaidScale の廃止と移行

- **UI**: `BoneEditWindow` の「メイドスケール」タブを「体型」タブに置き換える。prop ごとに 3 成分のスライダーを置く。リセットは成分ごとのスライダーのリセットと「すべて既定に戻す」で行う（2026-10-06 変更。項目ごとの専用ボタンは置かない）。
- **削除**: MaidScaleBones / Controller / State / LateUpdatePatch / Snapshot / RowDrawer / ItemInspector / TimelineLayer / TransformData と、それぞれの登録箇所・テスト。
  - 削除範囲の一覧は、調査時の行番号付きで計画に転記する（主な登録箇所: `COM3D2.SceneEditor.Plugin.cs`、`MaidManipulateManager.cs`、`ScreenshotManager.cs`、`ThumbnailCapture.cs`、`TimelineIntegration.cs`、`HistoryScope.cs`、`SnapshotFactory.cs`、`ScenePresetManager.cs`、`ScenePresetData.cs`、csproj）。
- **enum `TransformType.MaidScale` は残す**: 消すと、旧タイムラインの読込が XmlSerializer の例外で失敗する。読込互換専用として残す。
- **タイムラインの移行**: `TimelineXml.Initialize` の版変換チェーンに 1 段を追加する。
  - `MaidScaleTimelineLayer` のキーを、新しい `BodySliderTimelineLayer` の UPARMSCL / FARMSCL / HANDSCL のキーへ移す。均一倍率 s は (s,s,s) にする。
  - レイヤーの className も書き換える（`TimelineManager.cs:690` が className で照合している）。
- **プリセットの移行**: `ScenePresetMaid.maidScale` は読込専用として残す。新形式の要素が無く maidScale だけがある旧プリセットは、適用時に体型値へ変換する。
- **左右差（2026-10-06 決定）**: 腕に掛かる prop（UPARMSCL / FARMSCL / HANDSCL）は、SE では左右別の項目（`UPARMSCL_L` / `UPARMSCL_R` など）に分けて調整できるようにする。旧 MaidScale の倍率 s は対応する側の項目の (s,s,s) へそのまま移せるので、値は失われない。肩（KATASCL / CLVSCL / CLVPOS）は ModsParam どおり左右共通のまま。

## タイムライン

- `BodySliderTimelineLayer` と `TransformType.BodySlider` を新しく作る。
- `allBoneNames` は prop 名の一覧。値は 3 個（xyz）で、タンジェント補間に対応する。値の意味（倍率か差分か）は prop の種類で決まり、キー側では区別しない。
- キーがある prop だけを適用する。キーが無い prop は触らない（UI で入れた値を保つ）。（2026-10-06 変更。当初は「既定値として扱う」だったが、毎フレーム既定値へ戻すと UI の値がすぐ消えるため）

## シーンプリセット

- `ScenePresetMaid` に `bodySlider` 要素を足す。中身は既定値でない prop だけの `<param name x y z>`。
  - 要素が無い旧プリセットは「触らない」として扱う。ただし maidScale があれば、上記のとおり変換して適用する。
  - 空要素は「全項目を既定値に戻す」として扱う。

## 互換

- MTE と旧 SE は、未知の enum `<Type>BodySlider</Type>` でデシリアライズに失敗する。
- 変換後に保存した XML は、MaidScale のキーを持たなくなる（非可逆）。

## 検証項目

- 旧ボディ + MVP 導入環境で、MVP の値の上に乗算・加算で重なること。MVP で値を変えたあと（Blend の再実行後）に、倍率が積み上がらないこと。
- CRC ボディで、各骨（`_SCL_`、momotwist / momoniku、Kata_、Skirt、Hip_、Hip2_）が複製骨に存在するか。devbridge で各スロットの `smr.bones` を列挙して確かめる。
- MUNESCL / MUNEPOS と揺れ骨の干渉。旧ボディは jiggleBone、CRC は dbMune（DynamicBone）。
- MVP の FARMFIX（前腕を毎フレーム絶対値で書く）と FARMSCL の順序。SE の postfix のほうが後に掛かることを確認する。
- 位置の変換式が MVP と一致すること。同じ値を ModsSlider と SE に入れて、見た目を比べる。


## 決定事項（2026-10-06）

- 旧 MaidScale の左右差 → 腕の prop を左右別の項目にする（上記「左右差」）
- S1ABASESCL（Bip01 Spine1a のスケール）は対象外にする。MVP は本体骨格の Spine1a にはスケールを書かず（頭へ波及させないため）、どの Transform に効くのかが未確認。複製骨へ掛けると首・頭・腕まで拡縮される
- MaidScale は未リリース（v2.5.0.0 より後の追加）だが、開発中のデータを失わないよう移行は入れる
