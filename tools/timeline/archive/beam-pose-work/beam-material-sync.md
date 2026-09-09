# ステージレーザーと光源モデルの材質同期

`scripts/sync_beam_materials.py` はPython標準ライブラリのみで動作する。対象XMLを引数で受け取り、コントローラーと発光マテリアルの対応をJSONで指定する。既定は読み取り専用で、配置案・追加数・SHA256を表示する。

## 使用例

スキルフォルダーを基準に実行する。今回と同じモデル対応なら `references/beam-sync-kasou.json` を使用できる。

```powershell
python scripts/sync_beam_materials.py '対象.xml' --config references/beam-sync-kasou.json
python scripts/sync_beam_materials.py '対象.xml' --config references/beam-sync-kasou.json --channels color
```

検証結果が妥当なら、同じコマンドに `--apply --sha256 '検証結果のSHA256'` を追加する。原本更新の検知、同じフォルダーへの一意なバックアップ、保存後の再読検証を行う。作業領域外の反映・スキル更新はホストの許可手順に従う。再実行して変更がなければ書き込まず、バックアップも増やさない。

`--channels both` が既定で明るさと色を同期する。`color` はRGBのみを更新し、既存キーのEmissionValueを保つ。追加キーでは既存材質曲線からEmissionValueを算出する。`color`でもEmissionValueの維持は整数フレーム単位であり、追加キー間の連続曲線まで同一にはならない。`emission` は明るさのみを更新する。

## 対応表

JSONは `schema_version: 1`、`slot`、`color_source`、`emission_scale`、`mappings` を持つ。`mappings` の各要素に、XMLに存在する正確な `controller` 名とモデルマテリアルの `material` 名を入れる。材質名にはモデル名・複製接尾辞・スラッシュ以降の識別子もすべて含める。[今回の対応表](beam-sync-kasou.json)は床の(0)→ビームライト(11)、(1)→(15)、(2)→(8)、(3)→(18)のみ。高所の(4)(5)には割り当てていない。他シーンは実際の登録と位置から対応を決め、今回の番号を流用しない。

## 同期の意味

- **明るさ**：表示中の子レーザーの最大Intensity × `emission_scale`（既定1）。全消灯時は0。`autoVisible` / `autoLaserInfo` が無効な区間は各子レーザーのキーを使う。参考の1411/1412/1419フレームは0/0/1。Intensityの自動補間が1を超える場合も追従する。
- **色**：`autoColor` が有効ならコントローラー、無効なら各子レーザーの色キーを線形補間する。`color_source: core` は色1、`edge` は色2。既定の `alpha-weighted` は両色のRGBを各色のαで加重平均する。複数の子レーザーは表示中のIntensityで加重平均し、全消灯中は子レーザーの色を均等平均する。両色ともα=0の場合は色1を使う。
- レーザーの幅方向の2色を単色のEmissionColorへ代表させるため、グラデーションそのものの再現ではない。EmissionColorのαは元の値を保ち、レーザーのαを転写して光源を消さない。
- EmissionHDRExposureやEmissionPower、他の材質値、レーザーの位置・色・明るさのキー自体は変更しない。既存キーの補間情報は保持し、追加キーは線形補間にする。

## 対応範囲・検証

version 33、UTF-8（BOMあり可）、非ループ、フレーム0のキーがあるシーンに対応。実装参照は `StageLaserTimelineLayer`、`TransformDataBase.UpdateTangent`、`TimelineLayerBase.CalcTangentValue`。ライブラリAPIを調べ直す場合はContext7を使う。

整数フレーム単位で同期値を計算し、変化区間は毎フレーム、一定区間は両端を残す。元のキーは削除しない。すべての整数フレームで同期値を検証し、再実行時の重複を避ける。フレーム間の連続曲線は近似であり、サブフレーム撮影ではゲーム内の見た目も確認する。

発光RGB/EmissionValue以外の材質値がアニメーションしている場合、非ゼロのeasing値、未知の型や値数、ループアニメーションには適用せず停止する。別仕様に対応する際は実装を確認してから拡張する。対応表にないマテリアルやレイヤーは維持する。

反映後は `scripts/inspect_timeline.py` でもXMLと登録を検証する。ゲーム内への再読み込みは別工程。未保存編集を破棄せず、実施していない場合は「ゲーム内確認済み」と言わない。


## EmissionValueの上限

`emission_max` を指定すると、明るさ同期時に `Intensity × emission_scale` をその値で上限処理する。省略時は上限なし。今回の対応表は `emission_max: 0.5`。0.5以下の値はそのままとし、0.5を超えた値を0.5にする（全体を半分にする設定ではない）。`--channels color` は既存EmissionValueを維持するため上限処理しない。


## 原点XZとモデルのY軸回転

`scripts/sync_beam_pose.py '対象.xml' --config references/beam-sync-kasou.json` で検証し、同じ引数に `--apply --sha256 '検証時のSHA256'` を加えて反映する。同じ対応表を使い、レーザーコントローラーの全キーのX/Zをモデル原点に合わせ、Yは保持する。別系統のStageLightレイヤーは対象外。

モデルはレーザーの扇状配置の中央方向を水平面へ投影した方位にY軸回転させる。コントローラー回転とrotationMin/Maxの中央回転を合成し、変化区間は整数フレームごとにモデルキーを配置する。モデルの位置・スケール・表示と材質は保持する。クォータニオンの符号を連続させ、真上・真下の瞬間は直前の方位を保持する。フレーム間の曲線は近似。

非ループのversion 33、静止配置かつY軸のみの回転を持つモデル、一括回転が常に有効なレーザーに限定する。モデルの前方はローカル+Z、モデルとレーザーの親座標系がワールドと一致することを事前に確認する。位置やスケールをアニメーションするモデル、別の前方軸・親変換は先に実装を拡張する。`sync_beam_materials.py` が同じscriptsフォルダーに必要。

