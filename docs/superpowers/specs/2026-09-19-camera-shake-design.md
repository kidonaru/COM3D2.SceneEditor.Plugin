# カメラ手ブレ (CameraShake) 設計

作成日: 2026-09-19

## 目的

メインカメラに手ブレ（handheld shake）を付けられるようにする。
`W:\COM3D2_5\work\soCameraShaking`（MTE へリフレクション接続してキーを焼き込む外部プラグイン）を参考にするが、
SceneEditor はタイムラインを内包しているためリフレクションは使わず、**非破壊のキーフレームパラメータ**として実装する。

## 参考実装との違い

| 観点 | soCameraShaking | 本実装 |
|---|---|---|
| 接続 | MTE へリフレクション | 同一アセンブリ内の直接呼び出し |
| データ | 2 キー間の全フレームへカメラキーを焼き込む | 揺れパラメータを持つ専用ボーンをキーフレーム化 |
| エンベロープ | `sin(πt)` で両端を 0 に固定 | 振幅キーで表現（不要） |
| やり直し | 再生成 + ロールバック | パラメータのキーを編集するだけ |
| 適用先 | カメラキーの position / eulerAngles | カメラ Transform（UOCamera の内部状態には書かない） |

ノイズ生成（後述）は参考実装の `SampleShake` / `Hash01` をほぼそのまま踏襲する。

## スコープ

対象はメインカメラ（`CameraTimelineLayer`）のみ。サブカメラは対象外。

## データ構造

### TransformType

`TransformType` に `CameraShake` を追加する（`Camera` の直後）。
`TransformXml.type` は `XmlSerializer` の既定で**名前**として直列化されるため、
enum の途中に足しても既存 XML の読み込みは壊れない。

### TransformDataCameraShake

`TransformDataBase` を継承し、`type => TransformType.CameraShake`。

| index | 名称 | 内容 | 既定値 | 範囲 |
|---|---|---|---|---|
| 0-2 | 位置振幅 X/Y/Z | カメラ自身の右/上/前方向の振幅 (m) | 0 | 0〜0.2 |
| 3-5 | 回転振幅 X/Y/Z | ピッチ/ヨー/ロールの振幅 (度) | 0 | 0〜5 |
| 6 | 周波数倍率 | ノイズ周波数の倍率 | 1.0 | 0.1〜5 |
| 7 | シード | 波形を変えるための整数 | 0 | 0〜9999 |

- `valueCount = 8`
- `hasPosition` / `hasEulerAngles` / `hasScale` はいずれも false。8 値すべてを `GetCustomValueInfoMap()` のカスタム値として公開する
- `hasTangent = true`（カメラ値と同じく常時 Tangent 補間）
- シードは `CustomValueInfo.step = 1` の整数値として扱う。補間せず区間の始点の値を使う（「適用経路」参照）

### TimelineData のバージョン

キーの追加だけで既存データの解釈は変わらないため `CurrentVersion` は上げない。
旧 XML は shake ボーンを持たないだけで、振幅 0 と同じ挙動になる。
（本プラグインで保存した XML を MTE で開けないのは既知の非互換で、本件も同じ扱い）

## ノイズ生成

`CameraShakeNoise`（静的クラス）に参考実装の式を移植する。

```
Sample(axis, seconds, seed, frequencyScale):
    f1 = 0.18 + Hash01(seed + axis*1013 + 17)  * 0.18
    f2 = 0.40 + Hash01(seed + axis*2029 + 53)  * 0.24
    f3 = 0.72 + Hash01(seed + axis*4051 + 97)  * 0.34
    p1 = Hash01(seed + axis*8081  + 193) * 2π
    p2 = Hash01(seed + axis*16001 + 389) * 2π
    p3 = Hash01(seed + axis*32003 + 769) * 2π
    t  = seconds * frequencyScale
    return sin(2π*f1*t + p1)*0.64 + sin(2π*f2*t + p2)*0.26 + sin(2π*f3*t + p3)*0.10
```

`Hash01` は参考実装と同じ xorshift（`x ^= x<<13; x ^= x>>17; x ^= x<<5;` の下位 24bit を正規化）。
決定的なので実行環境・フレームレートに依存しない。

**時間軸は `TimelineLayerBase.playingTime`**（= `playingFrameNoFloat * timeline.frameDuration`）を使う。
`Time.time` を使わないことで、スクラブ・再生・動画出力のいずれでも同じフレームなら必ず同じ揺れになる。

## 適用経路

### レイヤー側

`CameraTimelineLayer` に shake ボーンを追加する。

- `CameraBoneName = "camera"` に加えて `ShakeBoneName = "shake"`（表示名「手ブレ」）
- `allBoneNames` は `{ "camera", "shake" }`
- `InitMenuItems()` で `BoneMenuItem` を 2 つ登録
- `GetTransformType(name)` が名前で `Camera` / `CameraShake` を返す
- `UpdateFrame()` は両ボーンのキーを作る。shake 側は現在の揺れパラメータ（レイヤーが保持する編集中の値）をそのまま保存する
- `ApplyPlayData()` を override し、`ApplyPlayDataByType(TransformType.CameraShake)` → `ApplyPlayDataByType(TransformType.Camera)` の順で呼ぶ。
  基底実装は `_playDataMap.Values` の列挙順に依存するため、順序を明示する
- shake の `ApplyMotion` はノイズを評価して `CameraShakeApplier.SetOffset(positionOffset, eulerOffset)` を呼ぶだけ。
  `ApplyMotion` が呼ばれないフレーム（キーが無い / レイヤー非適用）では揺れ 0 に戻す必要があるため、
  `ApplyPlayData` の先頭で `ClearOffset()` してから適用する
- **シードは補間しない**。区間の始点の値を使う（カメラレイヤーが追従設定で採っているのと同じ方針）。
  Hermite 補間するとシードが毎フレーム連続変化し、ハッシュのカオス性で周波数・位相が不連続にジャンプして波形が壊れる
- `LateUpdate()` を override し、`CameraShakeApplier.Apply(camera)` を呼ぶ

### 適用側 (CameraShakeApplier)

`Manager/CameraShakeApplier.cs`。**Harmony パッチは使わない。**

カメラ Transform は 1 フレームの Update フェーズ中に複数の経路から直接書かれる:

- `UltimateOrbitCamera.Update()`（`cameraMove` のときに位置・回転を再計算）
- `CameraTimelineLayer.ApplyMotion()` → `uoCamera.SetAroundAngle()` → `UltimateOrbitCamera.SetTransform()`
  （`_transform.rotation` / `_transform.position` を直接書く）
- 同じく `camera.SetRotationZ()`（`transform.eulerAngles` を直接書く）

これらの実行順は Unity のスクリプト実行順に依存して不定なので、`UltimateOrbitCamera.Update` にパッチを当てる方式では
揺れが上書きされたりちらついたりする。Unity は**全 `Update()` の後に `LateUpdate()` を回す**ため、
揺れの適用は `CameraTimelineLayer.LateUpdate()`（`TimelineManager` が毎フレーム呼ぶ）から行う。

`Apply(Camera camera)` は 1 フレームにつき 1 回、次の順で行う。

1. **復元**: 前フレームに適用済み（`_applied`）で、かつ現在の Transform が前回書いた
   `_appliedPosition` / `_appliedRotation` と一致するなら、`_basePosition` / `_baseRotation` へ戻す。
   一致しない（Update フェーズで誰かが書き直した / GUI で編集された）ならそのまま残す（後から書いた方が勝つ）。
   いずれの場合も `_applied = false` にする
2. **適用**: 揺れが 0 なら何もしない。0 でなければ現在の `transform.position` / `transform.rotation` を
   `_basePosition` / `_baseRotation` として控え、カメラ自身の軸で適用する
   - `transform.position = _basePosition + _baseRotation * new Vector3(sx, sy, sz)`
   - `transform.rotation = _baseRotation * Quaternion.Euler(rx, ry, rz)`
3. 適用後の値を `_appliedPosition` / `_appliedRotation` に控え、`_applied = true`

差し引き演算ではなく「揺れ前の値そのもの」を持ち回すため、揺れが累積しない。
`SetTransform` が毎フレーム位置・回転を作り直すフレームでも、作り直されないフレーム
（キーが無く `cameraMove` も false）でも、手順 1 の復元があるので同じ結果になる。
UOCamera の内部状態（`target.position` / `xNowRotate` / `yNowRotate` / `distance` / `fieldOfView`）には一切書かない。

プラグイン無効化・シーン遷移時は `ClearOffset()` + `Apply()` 相当で揺れを戻してから終う
（`CameraTimelineLayer.OnPluginDisable()` で `CameraShakeApplier.Restore(camera)` を呼ぶ）。

### ロールの読み書き

`CameraTimelineLayer.UpdateFrame()` がカメラキーを作るとき読む値のうち、
注視点・旋回角・距離・FOV は UOCamera の内部状態なので**揺れの影響を受けない**。
唯一ロールだけが Transform 由来（`transform.eulerAngles.z`）なので、読み書きの両方を
`CameraShakeApplier` 経由にする。GUI は OnGUI（LateUpdate の後）で動くため、
素で読むと必ず揺れ込みの値になる。

```
GetCleanRotationZ(camera):
    return _applied ? _baseRotation.eulerAngles.z : camera.transform.eulerAngles.z;

SetCleanRotationZ(camera, z):
    揺れ適用中なら _baseRotation の Z を差し替えてから transform を base * 揺れ で書き直す
    適用中でなければ従来どおり transform.eulerAngles.z へ書く
```

経由させるのは次の 3 箇所:

- `CameraTimelineLayer.UpdateFrame()`（読み）
- `MainCameraRowDrawer.DrawAngleSliders()` のロール表示値（読み。現状 `camera.transform.eulerAngles.z` を直読み）
- 同ロールスライダーの `onChanged`（書き。現状 `camera.transform.eulerAngles` へ直書き）

`MTEUtils/Extensions.cs` の `GetRotationZ` / `SetRotationZ` は共有 submodule のため変更しない。

### 追従 (MaidFollowMainCamera) との順序

`MaidFollowMainCamera` も LateUpdate で注視点を書き、そこからカメラ位置が再計算される。
同じ LateUpdate フェーズ内の順序は不定なので、**実装時に devbridge で実機確認する**。
揺れが上書きされるなら、`CameraTimelineLayer.LateUpdate()` の中で追従の適用後に呼ぶ、
あるいは `MaidFollowMainCamera` の適用直後に `Apply()` を呼ぶ形へ変える。
適用ロジックは `CameraShakeApplier.Apply()` に閉じているので、呼び出し位置だけの変更で済む。

### ループ再生時の既知の挙動

ノイズは絶対経過秒（`playingTime`）の関数なので、ループ境界でフレームが巻き戻ると波形も巻き戻り、
揺れが一瞬跳ぶ。振幅が小さい用途では目立たないため、今回は許容する（対策は非対象）。

## UI

`CameraItemInspector` は現状「項目は 1 つだけ」前提で選択項目を無視しているため、選択項目名で分岐させる。

- `camera` 選択時: 現行どおり（追従・注視点・回転・距離・FOV）
- `shake` 選択時: 新設 `CameraShakeRowDrawer` が描画する
  - 位置振幅 X/Y/Z（スライダー + 数値、0〜0.2 m）
  - 回転振幅 X/Y/Z（スライダー + 数値、0〜5 度）
  - 周波数倍率（0.1〜5）
  - シード（整数、乱数ボタン付き）

専用ウィンドウは作らない（タイムラインのボーンメニューから操作する）。
`shake` の編集値は `CameraTimelineLayer` が保持し、`UpdateFrame` がそこからキーを作る。

## テスト

### ユニットテスト

Unity ネイティブ呼び出しは使えないため、成分計算で組む（既存の `TestQuaternions` と同じ方針）。

- `CameraShakeNoise.Sample` が同一 (axis, seconds, seed, frequencyScale) で必ず同値を返す
- seed が違えば波形が変わる
- 振幅 0 のとき算出オフセットが 0
- `frequencyScale` を 2 倍にすると半分の時間で同じ位相になる
- `TransformDataCameraShake` の既定値・`valueCount`・カスタム値の index 対応
- シードが補間されず区間の始点の値になること

`CameraShakeApplier` は Unity Transform に依存するためユニットテスト対象外とし、
復元判定（前回書いた値と現在値が一致するか）だけを純粋関数として切り出してテストする。

### 実機検証 (devbridge)

1. shake ボーンに振幅キーを打ち、再生してカメラが揺れること
2. 同じフレームへスクラブすると毎回同じ揺れになること
3. 揺れ適用中にカメラキーを追加しても、注視点・旋回角・距離・FOV・ロールに揺れが焼き込まれないこと
4. 追従 ON のときに位置の揺れが消えないこと（消えるなら適用点を変更）
5. 振幅を 0 に戻すとカメラが揺れ前の位置・向きへ完全に復帰すること
6. タイムラインを閉じる / プラグインを無効化したときに揺れが残らないこと
7. 揺れ適用中にロールスライダーを操作しても、揺れ分が値に混入しないこと

### ビルド

`debug.bat all` 相当で COM3D2 / COM3D25 の両構成をビルドする。

## 変更ファイル一覧

| ファイル | 変更内容 |
|---|---|
| `Timeline/TransformData/ITransformData.cs` | `TransformType.CameraShake` を追加 |
| `Timeline/TransformData/TransformDataCameraShake.cs` | 新規 |
| `Timeline/TimelineIntegration.cs` | `RegisterTransform(CameraShake, ...)` を追加 |
| `Timeline/TimelineLayer/CameraTimelineLayer.cs` | shake ボーン追加、`ApplyPlayData` override、`ApplyMotion` 分岐、`UpdateFrame` 拡張 |
| `Timeline/CameraShakeNoise.cs` | 新規（ノイズ生成） |
| `Manager/CameraShakeApplier.cs` | 新規（揺れの適用・復元、編集中パラメータの保持） |
| `MainCameraRowDrawer.cs` | ロール行の読み書きを CameraShakeApplier 経由にする |
| `Timeline/ItemInspector/CameraItemInspector.cs` | 選択項目名で分岐 |
| `CameraShakeRowDrawer.cs` | 新規（揺れパラメータの行描画） |
| `source/COM3D2.SceneEditor.Plugin.Tests/` | ノイズと TransformData のテスト |

## 非対象 (YAGNI)

- サブカメラへの揺れ
- 揺れの実キーへのベイク出力
- 軸ごとの周波数指定（共通の倍率のみ）
- プリセット（「歩き」「手持ち」等）の同梱
- ループ再生境界でのノイズ連続化
