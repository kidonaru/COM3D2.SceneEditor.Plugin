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
- EmissionHDRExposureやEmissionPower、他の材質値、レーザーの位置・色・明るさのキー自体は変更しない。同期対象の材質は、色を線形補間し、明るさをeasingスロットの正規化タンジェントによる三次曲線で補間する。保持キーもeasingのIn/Outタンジェントと対応するsmoothビットを更新する。他の補間成分は維持する。

## 対応範囲・検証

version 33、UTF-8（BOMあり可）、非ループ、フレーム0のキーがあるシーンに対応。実装参照は `StageLaserTimelineLayer`、`TransformDataBase.UpdateTangent`、`TimelineLayerBase.CalcTangentValue`。ライブラリAPIを調べ直す場合はContext7を使う。

整数フレーム単位で同期値を計算し、色の直線補間と明るさの三次曲線を組み合わせてキーを生成する。端点を固定してタンジェントを求め、色を含む全49成分の誤差が許容値を超える区間を分割する。これにより、非線形のフェードも毎フレームのキーにせず再現する。既存の同期対象キーも再配置の対象になる。消灯境界の両側、先頭・末尾、未知情報を持つキーは保持する。全整数フレームで保存候補の補間値を検証する。既定の絶対許容誤差は1e-6。`--reduction-tolerance`（0より大きく1e-4以下）で変更でき、`--no-reduce`で従来の毎フレーム出力に戻せる。サブフレームは滑らかな近似曲線になり、以前の毎フレーム出力の区間内の曲線とは一致を保証しない。

フローは「同期値計算 → 境界保持 → 補間キー削減 → 全成分の誤差検証 → バックアップ保存」。再実行で内容が同一なら原本のバイト列を維持する。`added_keys` / `removed_keys`は原本からの実増減、`generated_keys`は削減前の一時生成数、`reduction`は材質別の削減前後の件数を示す。

発光RGB/EmissionValue以外の材質値がアニメーションしている場合、非ゼロのeasing値、未知の型や値数、ループアニメーションには適用せず停止する。別仕様に対応する際は実装を確認してから拡張する。対応表にないマテリアルやレイヤーは維持する。

反映後は `scripts/inspect_timeline.py` でもXMLと登録を検証する。ゲーム内への再読み込みは別工程。未保存編集を破棄せず、実施していない場合は「ゲーム内確認済み」と言わない。


## EmissionValueの上限

`emission_max` を指定すると、明るさ同期時に `Intensity × emission_scale` をその値で上限処理する。省略時は上限なし。上限は使用する対応表で指定する。上限以下の値はそのままとし、超えた値だけを制限する。同期対象外のビーム・床ライトの基本値は変更しない。`--channels color` は既存EmissionValueを維持するため上限処理しない。


## 原点XZとモデルのY軸回転

`scripts/sync_beam_pose.py '対象.xml' --config references/beam-sync-kasou.json` で検証し、同じ引数に `--apply --sha256 '検証時のSHA256'` を加えて反映する。同じ対応表を使い、レーザーコントローラーの全キーのX/Zをモデル原点に合わせ、Yは保持する。別系統のStageLightレイヤーは対象外。

モデルはレーザーの扇状配置の中央方向を水平面へ投影した方位にY軸回転させる。コントローラー回転とrotationMin/Maxの中央回転を合成して整数フレームの向きを計算し、クォータニオン各成分の三次曲線で補間キーを生成する。回転成分のIn/Outタンジェントとsmoothビットだけを更新し、モデルの位置・スケール・表示と材質は保持する。全整数フレームの最大角度誤差を検証する。`--angle-tolerance`は度単位で既定0.05、0より大きく1以下。`--no-reduce`で従来出力に戻せる。クォータニオンの符号を連続させ、真上・真下の瞬間は直前の方位を保持する。フレーム間の曲線は近似。

非ループのversion 33、静止配置かつY軸のみの回転を持つモデル、一括回転が常に有効なレーザーに限定する。モデルの前方はローカル+Z、モデルとレーザーの親座標系がワールドと一致することを事前に確認する。位置やスケールをアニメーションするモデル、別の前方軸・親変換は先に実装を拡張する。`sync_beam_materials.py` と共通補間処理 `beam_curves.py` が同じscriptsフォルダーに必要。
