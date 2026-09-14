# タイムライン リリース前 負債対応 設計

作成日: 2026-09-13
前提: `docs/timeline-release-debt-review.md` の優先度 高 8 項目を検討した結果。XML version は 34 のまま変えない。

## 決定一覧

| # | 項目 | 決定 | 実装 |
|---|---|---|---|
| 1 | メイド衣装がメイド単位でない | メイド単位化する | あり |
| 2 | モデル識別が配置順依存 | グループ番号を生成時に固定し振り直さない | あり |
| 3 | ステップ補間がない | 現状維持（`1フレーム調整` のまま） | なし |
| 4 | ポストエフェクト・マテリアルの一括補間 | 値ごとのタンジェントで個別補間に変える（XML 形式は不変） | あり |
| 5 | 保存されるが使われない値 | 読み手のないスカラー値の書き出し停止。PNG 配置は現状維持。レターボックスは実機確認 | あり |
| 6 | 空レイヤーの実体化 | メイド切替時の暗黙レイヤー作成をやめる。仕様書を訂正 | あり |
| 7 | フレームレート変更でキー非再配置 | 仕様書に明記のみ | 文書のみ |
| 8 | テンプレ保存先が MTE と共用 | 現状維持 | なし |

## 1. メイド衣装のメイド単位化

### 現状
- `Timeline/TimelineLayer/DressTimelineLayer.cs` は `hasSlotNo` を上書きせず既定 `false`。`TimelineManager.GetLayer` はスロット判定を飛ばして先頭の衣装レイヤーを返すため、2 人目以降のレイヤーは作られない
- XML の `TimelineLayerXml` は既に `<SlotNo>` を持ち、読み書きともスロット番号を通している
- `MaidMaterialTimelineLayer.cs:15-16` が同じ問題を `hasSlotNo => true` で解決済み

### 変更
- `DressTimelineLayer` に `public override bool hasSlotNo => true;` を追加（マテリアルと同じコメントを付ける）
- `TimelineLayerGateHost.cs:56` がリテラル `0` を渡している箇所は、衣装レイヤーが対象なら `maidManager.maidSlotNo` を渡すよう合わせる（他レイヤーの呼び出しに影響しないことを確認する）
- `docs-site/timeline/layers.md` / `layers-maid.md` / `compatibility.md` の「メイド単位ではない」記述を削除

### 互換
- 旧 XML の衣装レイヤーは `SlotNo` に生成時の値（通常 0）が入っており、メイド 0 のレイヤーとして読まれる。挙動は変わらない

### テスト
- `TimelineData.FromXml` で衣装レイヤーを 2 スロット分読み、`GetLayer(typeof(DressTimelineLayer), 1)` がスロット 1 のレイヤーを返すこと
- メイド削除時（`TimelineData.cs:677, 691` 付近の `hasSlotNo` 判定）に該当スロットの衣装レイヤーが除去されること

## 2. モデルのグループ番号を生成時固定にする

### 現状
- `StudioModelStat.name = info.fileName + " (group)"`。キーは 4 レイヤーとも `name` 文字列で対応付ける。XML の `<Model>` は名前・アタッチ先・プラグイン名のみ
- `ModelHackManager.modelList` の getter が毎回 `FixGroup` を呼び、列挙順で 0, 2, 3… を振り直す。並び順が変わると同名ファイルのキー割当先がずれる
- `ExternalModelHack.GetOrCreateStat` は GameObject をキーに `StudioModelStat` をキャッシュしており、グループ以外は安定している

### 変更
- `StudioModelStat.group` の初期値を未採番（`-1`）にする
- `FixGroup` を「未採番の stat にだけ番号を振る」処理に変える。番号は同じ `fileName` を持つ既存 stat が使っていない最小値（0, 2, 3… の規則は維持）
- 採番済み stat の番号は削除されるまで変えない。削除で空いた番号は次の生成で再利用してよい
- タイムライン読込（`StudioModelManager.SetupModels`）で名前に一致するモデルが無く生成する場合は、名前から抽出したグループをそのまま stat に採番する（現状の `createModel(..., group, ...)` 経路を維持し、生成後に `FixGroup` が上書きしないことを保証する）
- `ExternalModelHack.GetOrCreateStat` のコメント（振り直し前提）を更新

### 互換
- XML 形式は不変。読込時の名前はこれまでどおり `fileName (N)` で解決される
- 起動直後に既存モデルへ振られる番号は従来と同じ列挙順なので、既存タイムラインの読込結果は変わらない

### テスト
- 同一ファイルのモデル A, B を生成後、リストの順序を入れ替えても `name` が変わらないこと
- B を削除して再生成すると空き番号が再利用されること
- 名前 `x.menu (2)` を含む XML を読み込むと、生成された stat の group が 2 になること

## 4. 集約型レイヤーを値ごとのタンジェントで補間する

### 現状
- 対象は `PostEffectTimelineLayer`（Bloom / DepthOfField / DistanceFog / GTToneMap / Paraffin / Rimlight）と `MaidMaterial` / `ModelMaterial` / `BGModelMaterial` の 3 マテリアルレイヤー。計 9 か所が `CalcTangentValue(motion, t)` で `easingValue` 1 本のタンジェントから t を歪め、`PEP.PostEffectDataLerp.Lerp` や `ModelMaterial.Lerp` で全項目を一括補間している
- 各 TransformData は `hasTangent => true` で、`tangentValues`（色を除く全値）に値ごとの in / out タンジェントを保存・編集済み。`UpdateTangent` も値ごとに自動タンジェントを計算している。つまり **データは揃っていて適用側が読んでいないだけ**
- MTE は per-key easing の一括補間で、SE の代表タンジェントはその等価変換。MTE との差分が出ることは許容する

### 変更
- `TransformDataBase` に補間結果を自分の `values` へ書き込むメソッドを追加する

  ```csharp
  // start〜end の区間を t で補間した値を自身へ書き込む。集約型レイヤーの再生用
  public void LerpFrom(ITransformData start, ITransformData end, float t0, float t1, float t)
  ```

  値ごとの扱いは `CustomValueInfo.type` と色定義で決める

  | 値の種類 | 補間 |
  |---|---|
  | `FloatValue` / `FloatSlider` かつ `tangentValues` に含まれる | `PluginUtils.HermiteValue`（その値自身の out / in タンジェント） |
  | 色（`GetColorValueInfoMap` の RGBA index） | `Mathf.Lerp`（線形。現状と同じ） |
  | `BoolValue` / `IntValue`、`easingValue`、上記以外 | 区間開始値をコピー（`PostEffectDataLerp` が `blurIterations` 等を補間しない現状と同じ） |

  `strValues` は開始値をコピーする
- 各レイヤーは型ごとにスクラッチ用 TransformData を 1 つ保持し（`start.Clone()` で初回生成）、毎フレーム `scratch.LerpFrom(start, end, t0, t1, t)` してから既存の構造体ゲッター（`scratch.bloom` / `scratch.distanceFog` / `scratch.data` 等）で構造体を取り出して適用する。`t0` / `t1` は `motion.stFrame * frameDuration` / `motion.edFrame * frameDuration`（`LightTimelineLayer.ApplyMotionUpdateTangent` と同じ）
- マテリアル 3 レイヤーは `material.Lerp(start, end, lerpTime, t)` を `material.Apply(scratch)` に置き換える。`Lerp` は削除する
- `TimelineLayerBase.CalcTangentValue` と「集約型のためフィールド個別補間はできない」系のコメントを削除。`easingValue` は XML 互換のため残すが、`TransformDataBloom.cs:77-78` 等の「補間形状キャリア」コメントは「XML 互換のみ」に改める
- `PEP.PostEffectDataLerp` は他で使わなくなれば削除する（`MTEUtils/PostEffectData.cs`）

### 互換
- XML 形式は不変。version も 34 のまま
- 既存データは、値ごとのタンジェントが未編集なら自動補間（`isSmooth`）で再生されるため、代表タンジェントを手で編集していたキーは形が変わりうる。カーブエディタの相対編集は全チャンネルへ同時加算されるので、多くの場合は同じ形になる。これは許容する

### 文書
- `interpolation.md:451`、`compatibility.md:42`、`layers-effect.md:85` の「ポストエフェクトは値ごとの個別補間ができない」を削除

### テスト
- `LerpFrom` の単体テスト: Bool / Int 値は開始値、色は線形、Float 値は `HermiteValue` と一致、`strValues` は開始値
- Bloom で `intensity` と `threshold` に異なるタンジェントを与え、中間フレームで両者が別の曲線になること

## 5. 使われない値の整理

### 現状（調査結果）
| 値 | 実態 |
|---|---|
| `aspectWidth` / `aspectHeight` / `letterBoxAlpha` | `LetterBoxView` が実装済みで `CameraManager` が生成している。仕様書の「反映されない」は未検証 |
| `startOffsetTime` | BGM・動画のシーク、DCM の CSV 出力で使用中 |
| `endOffsetTime` / `startFadeTime` / `endFadeTime` | 読み手なし、UI なし |
| PNG 配置の 13 値（反転、カメラ追従回転、相対固定、アタッチ、APNG 等） | 適用も取り込みもされない |

### 変更
- `endOffsetTime` / `startFadeTime` / `endFadeTime`: `TimelineData` と `TimelineXml` から削除する。読込時は XML に要素があっても無視される（`XmlSerializer` の未知要素は捨てられる）ため、読込互換のための残置は不要
- PNG 配置の 13 値: **現状維持**。`<Value>` は enum index の float 配列で、中間の値を抜くと配列長が変わり version 移行が必要になる。死に値は 0 埋めで無害なので、形式を固定する今回は触らない。仕様書の「保持されるが適用されない」記述はそのまま
- レターボックス: 実機で `アスペクト比` を設定して描画されるか確認する。描画されるなら `settings.md` / `compatibility.md` の「反映されない」を削除。描画されないなら原因を別途調査（本設計の範囲外）

### テスト
- `TimelineXml` のラウンドトリップテストで、削除した 3 要素が出力に含まれないこと
- 旧 XML（3 要素あり）を読んでも例外にならないこと

## 6. メイド切替時の暗黙レイヤー作成をやめる

### 現状
- 保存処理はレイヤーを作らない。仕様書の「未使用のレイヤーは保存時に空のレイヤーとして実体化」は誤り
- `TimelineManager.OnMaidSlotNoChanged` が `ChangeActiveLayer(currentLayer.layerType, maidSlotNo)` を呼び、切替先メイドに同種レイヤーが無ければ作成して履歴も積む。これが空レイヤー増殖の主経路
- レイヤーゲートは「インスタンスの有無」で判定し、空レイヤーも存在扱い

### 変更
- `OnMaidSlotNoChanged` は作成せず、次の順でアクティブレイヤーを切り替える
  1. 切替先メイドに同種レイヤーがあればそれ
  2. 無ければ切替先メイドの `メイドアニメ` レイヤー
  3. それも無ければメイド非依存のレイヤーの先頭
  4. 何も無ければアクティブレイヤーを変えない
- `ChangeActiveLayer` 自体は `+` コンボ・ゲートからの明示操作で使うので変更しない
- 仕様書を訂正する
  - `compatibility.md`: 「未使用のレイヤーは保存時に空のレイヤーとして実体化されます」を削除
  - `editing.md` 複数メイド節・`control.md` 操作対象節: 「操作対象で選んだ時点でレイヤーが作られる」を「レイヤーは `+` かレイヤーゲートで追加する」に書き換え

### テスト
- 2 人目のメイドへ切り替えてもレイヤー数と履歴が増えないこと
- 切替先に同種レイヤーがあればそれがアクティブになること

## 7. フレームレート変更の仕様明記

`docs-site/timeline/playback.md` の「フレームレートと最終フレーム」に追記する。

> フレームレートを変えてもキーフレームのフレーム番号は変わりません。同じフレーム番号のまま 1 フレームの時間だけが変わるため、30 → 60 にするとアニメーション全体が 2 倍速になります。尺を保ちたい場合はキーを手で打ち直してください。

## 対象外にした項目の根拠

- 3: `Hold` フラグの追加は形式追加になるが、`1フレーム調整` で運用できており、ユーザー判断で現状維持
- 8: ユーザー判断で現状維持

## 実装順

1. 項目 1（衣装）
2. 項目 6（メイド切替）
3. 項目 5（スカラー値削除）
4. 項目 4（集約型の個別補間）
5. 項目 2（モデル番号）
6. 仕様書更新（1, 4, 5, 6, 7 の記述）とレターボックス実機確認

各項目は独立しており、順序を入れ替えても問題ない。
