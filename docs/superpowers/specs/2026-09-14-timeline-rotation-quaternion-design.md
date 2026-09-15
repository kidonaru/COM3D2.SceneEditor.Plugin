# タイムライン回転のクォータニオン化 設計

作成日: 2026-09-14
対象: `source/COM3D2.SceneEditor.Plugin/`（パスは同ディレクトリ起点）
出典: `docs/timeline-rotation-quaternion-survey.md` の「保留」行（1〜2 章のオイラー保持そのもの）

## 目的

オイラー角で保持している `TransformData` のうち、**姿勢を表すもの**をクォータニオン保持へ移行し、成分ごとのオイラー補間をやめる。あわせて XML を version 35 へ上げ、既存データの移行処理を入れる。

解決する問題は次の 2 つ。

- 成分ごとの Hermite 補間はジンバルロック付近で補間経路が破綻する。`GetFixedEulerAngles` の ±360 補正は成分ごとの補正しかできず、オイラー表現が別の三つ組へ飛ぶケースを救えない（調査書 3 章）
- 同じ回転を 2 通りの方式で保持しているため、補正ロジックと UI 経路が二重になっている（調査書 3〜4 章）

## 方針

**姿勢（向きそのもの）だけをクォータニオン化し、範囲（min〜max のサンプリング元）はオイラーのまま残す。**

オイラー保持の値には意味の異なる 2 種類が混ざっている。

| 種類 | 該当 | 判断 |
|---|---|---|
| 姿勢 | PNG 配置 / テキスト / リムライト光源方向 / ステージライト本体 / レーザー本体 / レーザー一括制御の本体 / サイリウムの手の姿勢 | クォータニオン化する |
| 範囲 | ステージライト一括制御の `eulerAngles`(=`rotationMin`) と `subEulerAngles`(=`rotationMax`)、レーザー一括制御の `rotationMin` / `rotationMax` | **据え置く**。成分ごとに独立した振れ幅を指定する値なので、クォータニオンにすると「X 軸だけ振る」が表現できなくなる |

さらに、**キー間で補間されない（区間開始値をスナップ適用するだけの）回転も据え置く**。クォータニオン化しても挙動は 1 ミリも変わらず、マイグレーションのリスクだけが増えるため。

カメラ・サブカメラは今回の対象外とする（アラウンド角という別の座標系を持つため）。

## スコープ

### 対象 7 型 8 スロット

| 型 | 現 Euler 添字 → 新 Rotation 添字 | valueCount |
|---|---|---|
| `TransformDataRimlight` | 0–2 → 0–3 | 25 → 26 |
| `TransformDataStageLaser` | 1–3 → 1–4 | 24 → 25 |
| `TransformDataPngObject` | 3–5 → 3–6 | 31 → 32 |
| `TransformDataText` | 3–5 → 3–6 | 20 → 21 |
| `TransformDataStageLight` | 3–5 → 3–6 | 23 → 24 |
| `TransformDataStageLaserController`（本体姿勢のみ） | 3–5 → 3–6 | 37 → 38 |
| `TransformDataPsylliumTransform`（左手 / 右手の 2 スロット） | 6–8 → 6–9、9–11 → 10–13 | 12 → 14 |

### 対象外

- `TransformDataStageLightController`: 保持している回転 2 組はどちらも範囲なので全体を据え置く
- `TransformDataStageLaserController` の `rotationMin` / `rotationMax`（現 31–36）: 範囲なので据え置く。ただし本体姿勢の w 挿入により添字は 32–37 へずれる
- `TransformDataPsylliumController` / `TransformDataPsylliumArea` / `TransformDataPsylliumPattern`: 適用経路が `ApplyControllerMotionInit`（`PsylliumTimelineLayer.cs:275`）/ `ApplyAreaMotionInit`（`:347`）/ `ApplyPatternMotionInit`（`:367`）で、いずれも区間開始値のスナップ適用。補間しないので据え置く
- `TransformDataBG`: 同じくスナップ適用（`BGTimelineLayer.cs:124`）
- `TransformDataCamera` / `TransformDataSubCamera`
- シーンプリセット XML（`ScenePresetData.CurrentVersion` = 33）の `rotation`: 静止スナップショットで補間しないため、オイラーの `Vector3` のまま維持する

## XML マイグレーション（version 34 → 35）

`TimelineData.CurrentVersion` を 35 へ上げ、`TimelineXml.Initialize` に `if (version < 35)` ブロックを追加する。

version 28 / 29 で Model / ModelBone / Move / Light に対して同じ変換を実施した前例があり、`Timeline/TimelineXml.cs:1060-1105` がその手本になる。

### 分岐キー

既存の移行処理はレイヤーの `className` で分岐しているが、本移行は **`TransformXml.Type`（`TransformType`）で分岐する**。`TransformXml` は `Type` を保存しているため、レイヤー構成や `className` の変遷に依存せず対象レコードを特定できる。

### 変換手順

型ごとに「挿入位置（= Euler 先頭 + 3）」のテーブルを持ち、共通ヘルパーで 1 レコードにつき次の 4 点を同時に更新する。いずれかだけを更新すると添字が食い違うため、必ずセットで行う。

1. `values`: Euler 3 値から `Quaternion.Euler` を作り、x / y / z を上書きしたうえで w を挿入位置へ挿入する
2. `inTangents` / `outTangents`: Euler X のタンジェント値を複製して挿入位置へ挿入する（version 28 / 29 と同じ扱い）
3. `inSmoothBit` / `outSmoothBit`: 既存の `TimelineXml.InsertBit` で Euler X のビットを複製して挿入位置へ挿入する
4. 挿入位置より後ろの添字は全て +1（`List.Insert` の結果として自動的にずれる）

`TransformDataPsylliumTransform` は 1 レコードで 2 スロットを挿入する。**後ろのスロット（右手 9–11）から先に**挿入し、前のスロットの添字がずれないようにする。

### 値の欠けたレコード

`values` の要素数が想定より少ない旧データは変換せずスキップする（`TransformDataBase._valuesForXml` の setter が不足分を `0` で埋めるため、変換すると `Quaternion.Euler(0,0,0)` ではなく壊れた値が入りうる）。既存の version 28 / 29 の移行処理も `values.Count > 5` という同種のガードを置いている。

## 型定義の変更

各対象型で次を行う。

- `hasEulerAngles` → `hasRotation`（sub 側は `hasSubEulerAngles` → `hasSubRotation`）
- `eulerAnglesValues` → `rotationValues`（4 値）
- `Index` enum の挿入位置以降を +1、`valueCount` を +1
- `tangentValues` の構築（`TransformDataStageLight` 等は明示的に列挙している）を新添字へ追随
- `GetCustomValueInfoMap()` の `index` を新添字へ追随
- `initialEulerAngles` を返している型は `initialRotation` へ寄せる

`TransformDataStageLight` の添字 6 は現状どこからも参照されていない空きスロットで、挿入により 7 へずれる。挙動への影響はない。

`TransformDataBase.eulerAngles` の getter / setter は既に `hasRotation` 側を処理しているため（`TransformDataBase.cs:97-145`）、`KeyFrameInspector` / `KeyFrameBatchDrawer` / 行エディタの Euler 入力は無改修で動作する。

## 補間経路の変更

### Hermite 経路（PNG / テキスト / ステージライト / レーザー / サイリウム）

各レイヤーの `PluginUtils.HermiteValues(..., start.eulerAnglesValues, end.eulerAnglesValues, t).ToVector3()` を `PluginUtils.HermiteQuaternion(..., start.rotationValues, end.rotationValues, t)` へ差し替える。`HermiteQuaternion` は戻り値を正規化済みなので追加対応は不要。

対象箇所（調査書 2 章）:

- `Timeline/TimelineLayer/PngPlacementTimelineLayer.cs:140`
- `Timeline/TimelineLayer/TextTimelineLayer.cs:156`
- `Timeline/TimelineLayer/StageLightTimelineLayer.cs:250`（本体。`:358` / `:366` の一括制御は範囲なので据え置く）
- `Timeline/TimelineLayer/StageLaserTimelineLayer.cs:227`、`:329`（本体姿勢のみ。同メソッド内の `rotationMin` / `rotationMax` は据え置く）
- `Timeline/TimelineLayer/PsylliumTimelineLayer.cs:463` / `:473` / `:610` / `:620`（手の姿勢の左右）

### `LerpFrom` 経路（リムライトのみ）

`TransformDataBase.LerpFrom`（`:712`）は値を 1 成分ずつ処理するため、そのままクォータニオンを流すと 4 成分が独立に Hermite 補間され、符号の未整合と非正規化が生じる。

`LerpKind` に回転用の種別を追加し、`lerpKinds`（`:645`）で `rotationValues` の 4 成分にそれを割り当てる。`LerpFrom` 側では 4 成分をまとめて読み、符号を揃えたうえで補間し、正規化して書き戻す。

### キー確定時の補正

`TimelineLayerBase.FixRotation`（`:625-647`）は `hasRotation` / `hasEulerAngles` を見て分岐しているため、対象型は自動的にクォータニオン側（`Quaternion.Dot < 0` で符号反転）の経路へ移る。

ポストエフェクトレイヤー（リムライト）がこの補正経路を通るかは実装時に確認する。通らない場合は、`LerpFrom` 側の符号合わせだけで成立するか、キー確定時の補正を明示的に呼ぶ必要があるかを判断する。

## ランタイム側（調査書 7 章）

- `Timeline/UnityScripts/PsylliumHand.cs:146-161`: `barOffsetRotation` のオイラー加算と、左右反転の `y` / `z` 符号反転をクォータニオンの鏡像へ差し替える
- `Timeline/UnityScripts/StageLightController.cs:180-187` の `autoRotation`: `rotationMin` → `rotationMax` の成分別 `Mathf.Lerp` は**範囲の補間**なので据え置く。据え置いた理由をコードコメントに残す
- `Timeline/UnityScripts/PsylliumArea.cs:221`: 補間を伴わないスナップ適用なので据え置く

### 設定オブジェクトの保持形式

`PsylliumConfig` などランタイム設定クラスの一次保持はオイラー（`Vector3`）のまま維持し、レイヤー適用の境界で `Quaternion` から `eulerAngles` へ落とす。

補間はクォータニオン空間で完結するため本設計の目的は達成でき、UI ドライバ（`LiveEffectWindow` の `DrawEulerAngles` 経路）とシーンプリセットの保持形式へ波及させずに済む。設定クラス自体のクォータニオン化は本設計の範囲外とする。

## UI への影響

クォータニオン保持へ移った型は、カーブエディタで回転の**値編集**ができなくなる（`TimelineCurveEditor.cs:1198` の `isQuaternionRotation` 分岐により、Euler 表示チャンネル＋タンジェント編集のみになる）。

これは既存のクォータニオン型（Move / Model / Light 等）と同じ挙動であり、`docs/timeline-release-debt-review.md` の項目 18 で既に許容済みの制約なので、本設計でも許容する。インスペクタ・一括編集・行エディタでの Euler 数値入力は従来どおり使える。

## テスト

`source/COM3D2.SceneEditor.Plugin.Tests` に `TimelineXmlRotationMigrationTests` を追加する（`ConfigKeyBindMigrationTests` / `BoneEditStoreVersionTests` と同じ作法）。

- 対象 7 型それぞれについて、version 34 の `TransformXml` を組んで移行を通し、`values` / `inTangents` / `outTangents` / `inSmoothBit` / `outSmoothBit` の配置が期待どおりずれること、回転 4 値が `Quaternion.Euler` の結果と一致することを検証する
- `TransformDataPsylliumTransform` の 2 スロット挿入で、左右どちらの回転も正しい位置と値になること
- 据え置き対象（`TransformDataStageLightController` / サイリウムの Controller・Area・Pattern / カメラ / サブカメラ / 背景）が無変更であること
- `values` の要素数が不足したレコードがスキップされること

実機検証（`com3d25-devbridge`）:

- version 34 で保存した既存タイムラインを読み込み、ライブ演出 3 種（ステージライト / レーザー / サイリウム）とリムライト・PNG 配置・テキストの見た目が移行前と一致すること
- キーをまたぐ回転再生で、移行前に破綻していた経路（180 度をまたぐ回転）が改善していること

## ドキュメント

- `docs-site/timeline/compatibility.md`: version 35 の変換内容と、移行が非可逆であること（多回転の表現が失われること）を追記
- `docs/timeline-rotation-quaternion-survey.md`: 8 章の「保留」行を対応済みへ更新し、据え置いた範囲値の理由を記録
- `docs/timeline-release-debt-review.md`: 該当箇所へ反映

## 非可逆性と互換方向

version 35 で保存した XML は version 34 では読めない（`TransformDataBase._valuesForXml` の setter が値数不足を `0` で埋めるため、誤読される）。これは `CLAUDE.md` に記載済みの「MTE → SceneEditor の一方向互換のみ保証」と同じ整理で、想定内として扱う。

オイラー保持で表現できていた 360 度を超える多回転は、クォータニオン化により失われる。移行時は `Quaternion.Euler` が最短経路の表現へ畳むため、1 回転を超える回転を持つ既存データは見た目が変わる。この点は移行の告知に含める。
