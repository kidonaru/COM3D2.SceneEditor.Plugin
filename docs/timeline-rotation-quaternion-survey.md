# タイムラインの回転表現調査（クォータニオン未使用箇所の洗い出し）

調査日: 2026-09-14
対象: `source/COM3D2.SceneEditor.Plugin/` 配下（パスは同ディレクトリ起点）
目的: タイムラインの角度計算のうちクォータニオンを使っていない箇所を全て洗い出し、修正の要否と優先度を判断する材料にする。

> **対応状況（2026-09-14 時点）**
> - 優先度 高: 5-3 `isFixRotation` の削除（`6bf6974`）、5-4 リムライト光源方向の無補間（`9bffd01`）ともに対応済み
> - 5-4 の対応過程で `baseValues` のキャッシュ不整合を追加で発見・修正（`7a78282`。本書の調査時点では未検出だった）
> - 5-1: 挙動は正しいと判明。意図をコードコメントに残すだけに留めた（挙動不変）
> - 優先度 中 / 低: 3 章の補正ロジック重複、5-2 の反転規則抽出、6 章の非正規化 nlerp を対応済み
> - 1〜2 章のオイラー保持: **7 型 8 スロットを version 35 でクォータニオン化して対応済み**（PNG 配置 / テキスト / リムライト光源方向 / ステージライト本体 / レーザー本体と一括制御の本体姿勢 / サイリウムの手の姿勢）
> - 据え置いたのは 2 種類:
>   - **範囲の値**（クォータニオン化すると表現できなくなるもの）: ステージライト / レーザー一括制御の `rotationMin` / `rotationMax` と、サイリウムのパターンの `randomEulerAnglesRange`。いずれも軸ごとに独立した振れ幅で、クォータニオンにすると「X 軸だけ振る」が表現できない。ステージライト一括制御はオイラー 2 組がそのまま `rotationMin` / `rotationMax` なので型ごと据え置き
>   - **姿勢だが今回スコープ外の型**: カメラ / サブカメラ / 背景 / サイリウムの配置エリア・一括制御

## 結論

- **保持方式**（調査時点）: 回転をクォータニオンで保持しているのは `Move` / `Root` / `Rotation` / `Model` / `ModelBone` / `BGModel` / `Light` / `ExtendBone` の 8 系統のみ。カメラ・ステージライト・レーザー・サイリウム・テキスト・PNG 配置・リムライト・背景はオイラー角のまま保持している。→ version 35 でこのうち 7 型がクォータニオン側へ移った（1 章の表を参照）。
- **制約**: オイラー保持は XML 形式（`TimelineData.CurrentVersion`）に直結するため、単純な置き換えはできない。→ version 35 でマイグレーションとセットで対応した。
- **単独で直せるもの**: 5 章の 4 件は XML 形式の互換性に影響しないため独立して修正できる。うち 5-1 は調査の結果「挙動は正しく可読性の問題だけ」と判明したので、実害があるのは 5-2 / 5-3 / 5-4 の 3 件。

## 1. 回転をオイラー角で保持している TransformData

`TransformDataBase` は回転の持ち方を 2 通り用意している（`Timeline/TransformData/TransformDataBase.cs:192-196`）。

- `hasRotation` = true: `rotationValues` に Quaternion の 4 値（x, y, z, w）を保持
- `hasEulerAngles` = true: `eulerAnglesValues` に オイラー角の 3 値（X, Y, Z）を保持

調査時点で `hasEulerAngles` 側（クォータニオン未使用）だった型は以下。**状態**は version 35 での対応結果。

| 型 | 用途 | sub 回転 | 状態 |
|---|---|---|---|
| `TransformDataPngObject` | PNG 配置 | - | クォータニオン化済 |
| `TransformDataText` | テキスト | - | クォータニオン化済 |
| `TransformDataRimlight` | リムライト光源方向 | - | クォータニオン化済 |
| `TransformDataStageLight` | ステージライト本体 | - | クォータニオン化済 |
| `TransformDataStageLaser` | ステージレーザー本体 | - | クォータニオン化済 |
| `TransformDataStageLaserController` | ステージレーザー一括制御（本体姿勢） | - | クォータニオン化済（`rotationMin` / `rotationMax` は別スロットで据え置き） |
| `TransformDataPsylliumTransform` | サイリウムの手の姿勢 | あり（右手） | クォータニオン化済（左右 2 スロット。sub 側も `hasSubRotation`） |
| `TransformDataStageLightController` | ステージライト一括制御 | あり（`rotationMax` 相当） | 据え置き（このオイラー 2 組は姿勢ではなく `rotationMin` / `rotationMax` そのもの） |
| `TransformDataCamera` | メインカメラ | - | 据え置き（今回スコープ外） |
| `TransformDataSubCamera` | サブカメラ | - | 据え置き（今回スコープ外） |
| `TransformDataBG` | 背景 | - | 据え置き（今回スコープ外） |
| `TransformDataPsylliumArea` | サイリウム配置エリア | - | 据え置き（今回スコープ外） |
| `TransformDataPsylliumController` | サイリウム一括制御 | - | 据え置き（今回スコープ外） |
| `TransformDataPsylliumPattern` | サイリウムのパターン | - | 据え置き（このオイラーは `randomEulerAnglesRange`＝振れ幅） |

調査時点でクォータニオン側だったのは `TransformDataMove` / `TransformDataRoot` / `TransformDataRotation` / `TransformDataModel` / `TransformDataModelBone` / `TransformDataBGModel` / `TransformDataLight` / `TransformDataExtendBone` の 8 型。version 35 で上記 7 型が加わった。

## 2. 成分ごとのオイラー補間をしている箇所

いずれも `PluginUtils.HermiteVector3`（`Timeline/PluginUtils.cs:232`）／`HermiteValues`（同 `:216`）で X / Y / Z を独立に補間している。Slerp は一切使っていない。

| 箇所 | 補間対象 |
|---|---|
| `Timeline/TimelineLayer/CameraTimelineLayer.cs:96` | メインカメラのアラウンド角 + ロール。結果は `:120` `SetAroundAngle` と `:121` `SetRotationZ` へ |
| `Timeline/TimelineLayer/SubCameraTimelineLayer.cs:147` | サブカメラの回転 |
| `Timeline/TimelineLayer/PngPlacementTimelineLayer.cs:140` | PNG 配置の `localEulerAngles` |
| `Timeline/TimelineLayer/TextTimelineLayer.cs:156` | テキスト矩形の `eulerAngles` |
| `Timeline/TimelineLayer/StageLightTimelineLayer.cs:250` | ステージライト本体 |
| `Timeline/TimelineLayer/StageLightTimelineLayer.cs:358` / `:366` | 一括制御の `rotationMin` / `rotationMax` |
| `Timeline/TimelineLayer/StageLaserTimelineLayer.cs:227` | レーザー本体 |
| `Timeline/TimelineLayer/StageLaserTimelineLayer.cs:329` | レーザー一括制御 |
| `Timeline/TimelineLayer/PsylliumTimelineLayer.cs:463` / `:473` / `:610` / `:620` | 左右の手の `eulerAnglesLeft` / `eulerAnglesRight` |

補間せず区間開始値をスナップ適用するだけの箇所が 2 つある。

- `Timeline/TimelineLayer/BGTimelineLayer.cs:124`: 背景の回転。仕様どおりで実害はない。
- `Timeline/TimelineLayer/PostEffectTimelineLayer_Rimlight.cs:14`: リムライトの光源方向。移植元の MTE では補間されていたものが脱落しており、退行として 5-4 に記載する。

## 3. オイラー角のまま補間するために必要になっている補正ロジック

クォータニオンで持っていれば不要な、360 度ラップ回避の後付け処理。

| 箇所 | 内容 |
|---|---|
| `Timeline/TransformData/TransformDataBase.cs:440 GetFixedEulerAngles` | 前キーとの差が ±180 度を超えたら成分ごとに 360 度単位で寄せる（`AngleUtils.GetFixedAngles` へ委譲） |
| `Timeline/TransformData/TransformDataBase.cs:445 GetNormalizedEulerAngles` | 同じ式で -180〜180 へ正規化（`AngleUtils.NormalizeAngles` へ委譲） |
| `Timeline/TransformData/TransformDataBase.cs:450 FixEulerAngles` | 上記をキーフレームへ適用 |
| `Timeline/TimelineLayer/TimelineLayerBase.cs:625-647` | キー確定時に前キーとの連続性を全ボーンへ適用（`FixRotation` は `:413` の Quaternion 版、`FixEulerAngles` は上記） |
| `Timeline/TimelineLayer/TimelineLayerBase.cs:1710` / `:1718` | UI での表示・編集時に同じ補正を掛ける |
| `MTEUtils/MTEUtils.cs:442` | `GetNormalizedEulerAngles` の同等実装が重複して存在。本プラグインからの呼び出しは無いが、共有サブモジュールを ModItemExplorer が使っているため残置 |

この補正には次の限界がある。

- **成分ごとの ±360 補正しかしない**。ジンバルロック付近（pitch ±90 度）では、オイラー表現そのものが別の三つ組へ飛ぶことがある（例: X / Y / Z が同時に 180 度ずれる）。この場合は元の表現を復元できず、補間経路が破綻する。
- **1 度幅の不感帯**: 2026-09-14 に解消。`AngleUtils.GetFixedAngle` の `Mathf.Round` ベースの式へ統一したため、差分 180.0〜181.0 度でも補正が発火する。

## 4. クォータニオン ⇄ オイラー の往復変換

値が落ちる／表現が正規化されてしまう経路。

| 箇所 | 内容 |
|---|---|
| `Timeline/TimelineLayer/SubCameraTimelineLayer.cs:166` / `:184` | 実体は `Quaternion` なのに、キーは `Quaternion.Euler(...)` ⇔ `rotation.eulerAngles` で往復する |
| `Timeline/Manager/SubCameraManager.cs:134-155` | 追従中の `rotation` プロパティが `Quaternion` ⇔ `eulerAnglesOffset` を毎回往復する |
| `Timeline/TransformData/TransformDataBase.cs:93-145` | `eulerAngles` / `subEulerAngles` の getter・setter が、`hasRotation` 型では Quaternion と暗黙変換する |
| `MTEUtils/TransformCache.cs:24-32` | `rotation` の setter が `value.eulerAngles`（0〜360 正規化）を同時保持。クォータニオン系レイヤーでも UI 編集経路はオイラーを通る |
| `Timeline/MaidCache.cs:775`、`Timeline/ModelBoneController.cs:22-25` | 初期回転をオイラーで返す |

## 5. 実装上の明確な問題（形式互換に影響しない、単独で直せるもの）

### 5-1. クォータニオンの成分を直接 0 代入している（挙動は正しい。可読性の問題）

`Timeline/UnityScripts/StageLaser.cs:598-603`

```csharp
_meshFilter.transform.LookAt(camera.transform, transform.forward);

var localRotation = _meshFilter.transform.localRotation;
localRotation.x = 0;
localRotation.y = 0;
_meshFilter.transform.localRotation = localRotation;
```

一見すると `Quaternion` の x / y をオイラー成分のように潰す誤りに見えるが、**これは Z 軸まわりの swing-twist 分解の twist 抽出そのもので、結果は正しい**。

- レーザーのメッシュは局所 XZ 平面のリボン（`:493-513` の頂点生成。幅が X、ビーム長が Z、法線が ±Y）。ビーム軸は親（`StageLaser`）の +Z
- `localRotation` は親基準なので、`(0, 0, z, w)` を正規化したものは親の Z 軸＝ビーム軸まわりの回転成分だけを取り出したものになる。これはリボンをビーム軸まわりに回してカメラへ向ける、まさに意図した処理
- 非正規化のまま代入しているが、Unity の `Transform.localRotation` setter が代入時に正規化する（実機で確認: `(0,0,0.3,0.4)` を代入して読み戻すと `(0,0,0.6,0.8)`）
- 退化ケース（z も w も 0）でも Unity は NaN にせず identity を返す（実機で確認）。ロール 0 にフォールバックするだけで破綻しない

したがって修正は不要。ただし **暗黙の正規化と swing-twist 分解という 2 つの非自明な前提に依存していて読み解けない**ため、`Quaternion.Normalize` を明示し意図をコメントに残す価値はある。`Quaternion.Normalize` は setter の挙動と完全に一致する（退化ケースも identity。実機で確認済み）ので、明示化しても挙動は変わらない。

### 5-2. ポーズ左右反転がオイラー角の算術

`Timeline/FrameData.cs:226-259 Flip()` / `Timeline/PoseFlipUtils.cs:130-161 FlipEulerAngles()`

ボーンは `TransformDataRotation`（クォータニオン保持）だが、調査時点の `Flip()` は全ボーンを次の 3 手順でオイラー角の算術により反転していた。

1. `rotation.eulerAngles` へ変換する
2. ボーン種別ごとの手書きルールで反転する
3. `Quaternion.Euler` で戻す

手順 2 のルールは以下（行番号は抽出先の `PoseFlipUtils.cs`）。

| ボーン種別 | 反転式 |
|---|---|
| `Root` | `y = 180 - (y - 180)`, `z = 270 - (z - 270)`（`:136-137`） |
| `Pelvis` | `y += 180`, `z += 180`（`:141-142`） |
| `Spine0` | `x = 270 - (x - 270)`（`:146`） |
| `Bust_L` / `Bust_R` | `y = 360 - (y - 180)`, `z = 270 - (z - 270)`（`:151-152`） |
| その他 | `x = -x`, `y = -y`（`:156-157`） |

種別ごとの例外規則と、抽出時に持ち込まなかったコメントアウト行（`Spine0` の `z` 補正）は、オイラー表現で鏡像変換を近似したことによる試行錯誤の跡。本来は鏡像変換（`q' = (x, -y, -z, w)` 系）＋親ボーン座標系の考慮で扱う領域。

**現状**: 上記 3 手順を通るのは例外規則を持つ 4 種別（Root / Pelvis / Spine0 / Bust）だけで、それ以外はクォータニオン鏡像経由へ差し替え済み（下記の対応注記を参照）。

> **2026-09-14 対応**
> - 対象: 例外規則を持たないボーン（四肢・指・つま先・頭・Spine1 以降）
> - 変更: 反転をクォータニオンの鏡像 `q' = (-x, -y, z, w)` へ差し替え（`PoseFlipUtils.FlipRotation`）
> - 等価性: 旧規則「x = -x, y = -y」とオイラー角上で厳密に等価。実機で `Quaternion.Angle` が全サンプル 0 になることを確認済み
> - 対象外: Root / Pelvis / Spine0 / Bust の 4 種別は鏡像で表せないためオイラー算術のまま
> - 実装: 規則は `Timeline/PoseFlipUtils.cs` へ抽出済みで、`PoseFlipUtilsTests` が現挙動を固定している

### 5-3. `isFixRotation` が定義されているだけで読まれていない

> **2026-09-14 対応**: `6bf6974` でフィールドを削除した。以下は削除前の状態の記録。

`isFixRotation` は「キー確定時の回転連続性補正（`FixRotation` / `FixEulerAngles`）を型ごとに OFF にする」ためのフラグ。

- 宣言: `Timeline/TransformData/ITransformData.cs:191`（削除前）
- 既定値: `Timeline/TransformData/TransformDataBase.cs:222`（`true`。削除前）
- 上書き: `Timeline/TransformData/TransformDataPsylliumTransform.cs:21`（`false`。削除前）

`TransformDataPsylliumTransform` は補正からの除外を意図していたが、実行元の `TimelineLayerBase.FixRotation`（`:625-647`）は `hasRotation` / `hasEulerAngles` しか見ていないため無視され、サイリウムにも補正が掛かっていた。

移植元の MTE 本体（`W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin`）でも同じ 3 箇所にしか出現せず読み手がないため、**移植時の取りこぼしではなく元から未配線**。対応はフラグを削除するか、`FixRotation` に条件を足して除外を実装するかの二択。

### 5-4. リムライトの光源方向がキー間で補間されない（移植による退行）

`Timeline/TimelineLayer/PostEffectTimelineLayer_Rimlight.cs:14` は汎用の `TransformDataBase.LerpFrom`（`:712`）を通る。補間種別を決める `lerpKinds`（`:645`）は、値を `LerpKind.Tangent` へ昇格させる条件を「`GetCustomValueInfoMap()` に登録済みかつ `FloatValue` / `FloatSlider`」に限定している。

`TransformDataRimlight` の `CustomValueInfoMap` には Euler X / Y / Z（`Index.EulerX` = 0 〜 `Index.EulerZ` = 2）のエントリが存在しない。そのため 3 成分は既定の `LerpKind.Hold` のまま留まり、`LerpFrom` の `default` 分岐で区間開始値をコピーするだけになる。`TransformDataRimlight.rimlight` getter（`:331`）が `rotation = eulerAngles` を読むので、補間されない値がそのまま適用される。

**移植元の MTE では補間されていた。** MTE は汎用経路ではなく専用の `RimlightData.Lerp` を呼んでおり（`COM3D2.MotionTimelineEditor.Plugin/.../PostEffectTimelineLayer_Rimlight.cs:16`）、その実装（`COM3D2.PostEffects.Plugin/.../Effects/RimlightEffectSettings.cs:58`）は `rotation = Vector3.Lerp(a.rotation, b.rotation, t)` で光源方向を成分別に線形補間する。移植時に汎用の `LerpFrom` へ切り替えた際、Euler を `CustomValueInfoMap` へ登録しなかったことで脱落した退行。

一方 UI 側（`PostEffectRowDrawer.cs:477`）では `DrawEulerAngles` で光源方向を編集でき、キーにも保存される。**編集・保存はできるが再生時は補間されず階段状に切り替わる**状態。`CustomValueInfoMap` へ Euler を登録するか、専用の補間経路を通すかの対応になる。

なお `LerpFrom` を通る 7 型（Rimlight / Paraffin / Bloom / DepthOfField / DistanceFog / GTToneMap / ModelMaterial）のうち、構造値（position / scale / eulerAngles）を持つのは Rimlight だけで、他の 6 型に同種の取りこぼしはない。

## 6. クォータニオンは使っているが Slerp ではない箇所

- `Timeline/PluginUtils.cs:303 HermiteQuaternion` は 4 成分を独立に Hermite 補間する。
  - `ToQuaternion`（`Timeline/Extensions.cs:74` / `MTEUtils/Extensions.cs:509`）自体は正規化しない。
  - 2026-09-14 に `HermiteQuaternion` の戻り値側で正規化するようにした。正しい nlerp になり、XML へ保存される `LightHoldKeyConversion` の経路でも単位長が保たれる（それ以前は `Transform` への代入時の Unity 側正規化に頼っており、XML 保存経路では非正規化のまま残っていた）。
  - タンジェント付きのため、オーバーシュート時は補間経路が歪む。向き自体は破綻しないが、角速度は一定にならない。
- 前処理として `TransformDataBase.FixRotation`（`:413`）が `Quaternion.Dot < 0` のとき符号を反転して最短経路を確保しているため、実用上の破綻は抑えられている。
- 呼び出し元: `BGModelTimelineLayer.cs:124` / `LightTimelineLayer.cs:173` / `ModelBoneTimelineLayer.cs:157` / `ModelTimelineLayer.cs:130` / `MoveTimelineLayer.cs:119` / `LightHoldKeyConversion.cs:163`

## 7. タイムラインが駆動する周辺のオイラー計算

タイムライン本体の外だが、タイムラインの値を受けて角度を計算している箇所。

| 箇所 | 内容 | 評価 |
|---|---|---|
| `Timeline/UnityScripts/StageLightController.cs:180-187` | `autoRotation` で `rotationMin` → `rotationMax` を `Mathf.Lerp` の成分別補間 | 2 章と同じ問題がランタイム側にも二重に存在 |
| `Timeline/UnityScripts/PsylliumHand.cs:146-161` | `barOffsetRotation` をオイラーで加算し、左右反転を `y` / `z` の符号反転で実装 | 小角度前提なら実害は小さい |
| `Timeline/UnityScripts/PsylliumArea.cs:221` | エリア回転を `localEulerAngles` で適用 | 補間しないので実害なし |
| `MaidManipulation/MaidIKHoldController.cs:597-606` | 接地角を `localEulerAngles.z` で直接書き換え。360 度補正は `AngleUtils.GetFixedAngle` を呼ぶ | 対応済（3 章の共通化に含む） |
| `Timeline/Manager/MaidFollowMainCamera.cs:65` | `faceRotation.eulerAngles.y` でヨーを抽出 | `UltimateOrbitCamera` の API がアラウンド角なので設計上妥当 |

なお `Timeline/Manager/MaidFollowState.cs:88-99` の追従向き算出は `Quaternion.LookRotation` を使っており、この経路自体は適切。

## 8. 対応候補と優先度

| 優先度 | 対象 | 理由 | 影響範囲 | 状態 |
|---|---|---|---|---|
| 高 | 5-3 `isFixRotation` の未使用 | 意図と実装の乖離。削除か実装かを決めるだけ | 3 ファイル。XML 不変 | 対応済（`6bf6974`。削除） |
| 高 | 5-4 リムライト光源方向の無補間 | 移植による退行。MTE では補間されていた挙動が失われている | `TransformDataRimlight.cs`。XML 不変だが既存データの見た目が変わる | 対応済（`9bffd01`） |
| 中 | 5-2 `FrameData.Flip()` のオイラー算術 | ボーン種別ごとの例外規則が破綻しやすい。ただし挙動変更を伴うため実機検証が必要 | `FrameData.cs`。XML 不変だが既存ポーズの反転結果が変わりうる | 対応済（その他ボーンのみクォータニオン化） |
| 中 | 3 章の補正ロジック重複（`MTEUtils.cs:442` と `TransformDataBase.cs:445`、および `MaidIKHoldController.cs:596`） | 同じ式が 3 箇所にある | 共通化のみ。挙動不変 | 対応済（`AngleUtils` へ集約。`MTEUtils` は共有サブモジュールのため残置）。あわせて 3 章末尾の不感帯も解消 |
| 低 | 5-1 `StageLaser` の twist 抽出の明示化 | 挙動は正しいが、暗黙の正規化と swing-twist 分解に依存していて読み解けない | `StageLaser.cs` のみ。挙動不変 | 対応済（コメントのみ。挙動不変） |
| 低 | 6 章の非正規化 nlerp | 現状 Unity 側の正規化で救われており、体感差は小さい | `PluginUtils.cs` | 対応済 |
| 保留 → 対応 | 1〜2 章のオイラー保持そのもの | XML 形式と識別子に直結する。直すなら version 35 とマイグレーションのセットになる | 全レイヤー | 対応済（version 35。姿勢 7 型 8 スロットをクォータニオン化。範囲の値と今回スコープ外の型は据え置き） |

1〜2 章は version 35 で対応した。据え置いた 2 種類（範囲の値 / 今回スコープ外の姿勢型）は上記のとおり。

なお移行の実装で「**バージョン移行処理は実行時の型定義（`valueCount` / `Index`）を参照してはならない**」という原則を確立した。参照していると型のレイアウトを変えた瞬間に旧データの書き込み先が静かにずれる。詳細は `docs/timeline-release-debt-review.md` を参照。
