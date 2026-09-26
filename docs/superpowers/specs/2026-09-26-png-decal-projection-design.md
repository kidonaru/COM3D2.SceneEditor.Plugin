# PNG配置のデカール投影 設計

## 目的

背景・床・壁に画像を貼り付ける「デカール投影」を追加する。主な用途はステージ床のロゴ、壁のポスター、床の影や汚れ、光の模様。対象は静止物（背景・配置モデル）で、メイドは既定で投影対象から外す。

新しい機能として独立させず、**PNG配置の表示タイプの 1 つ**として実装する。1 つの PNG 配置オブジェクトは「板」と「デカール」を Inspector で切り替えられる。ウィンドウ・Undo・シーンプリセット・タイムラインレイヤーは PNG配置の既存の仕組みをそのまま使う。

## 決定事項

| 項目 | 決定 | 理由 |
|---|---|---|
| 投影方式 | Unity 標準 `Projector`（正射影）+ 専用シェーダー | Forward レンダリングで動き、COM3D2（Unity 5.6）と COM3D2.5（Unity 2022.3）の両方で使える（実機で生成確認済み）。`ignoreLayers` でメイドを除外できる |
| UI | PNG配置ウィンドウに統合し、Inspector で表示タイプを切り替える | 板で位置を合わせてからデカールへ切り替える使い方ができる。新しいウィンドウや保存形式を増やさずに済む |
| タイムライン | 既存の PNG配置レイヤーと 32 値のキーをそのまま使う | 板の幅・高さ（画像のアスペクト比 × ScaleX）は投影箱の幅・高さへ、ScaleZ は奥行きへそのまま読み替えられる |
| デカール固有の値 | キーでアニメーションさせず、実体の設定として持つ | ブレンドやフェード角を補間する需要が薄い（YAGNI） |
| ライティング | 行わない（Unlit）。明るさは手動で調整する | Projector のパスではライトの情報を扱いにくい。背景用途なら手動調整で足りる |

不採用とした案:
- 深度テクスチャを使うスクリーンスペース方式: メイドを除外できない
- メッシュ切り抜き方式: 背景メッシュが読み取り不可（`isReadable=false`）のことが多い

## 描画

### GameObject 構成

既存の PNG 配置（`SceneEditorPngRoot` → root → `PngQuad`）の root の下に、子の `PngDecal` を追加する。

- `PngDecal` は `Projector` を持ち、デカールへ初めて切り替えたときに遅延生成する
- 表示タイプに応じて、`PngQuad` と `PngDecal` のどちらか一方の GameObject だけを有効にする
- デカールのときは `PngQuad` を無効にし、MeshCollider によるクリック選択も効かなくなる

**向き**
- `PngDecal` は `PngQuad` と同じく localRotation を Y180 にする
- こうすると投影方向は root の -Z（板の表側から裏側へ）になる。板を正面から見たときと同じ向き（左右反転なし）で、板の奥の面に絵柄が映る

**投影箱**
- 投影箱は root のローカル座標で「X が ±画像アスペクトの X/2、Y が ±画像アスペクトの Y/2、Z が ±0.5」の箱とする。root の拡縮と回転がそのまま箱に効く
  - 画像アスペクトは既存の `ApplyAspectScale` と同じ規則（長辺を 1、短辺を比率）で求める
  - ワールドでの寸法は、幅 = アスペクトの X × |scale.x|、高さ = アスペクトの Y × |scale.y|、奥行き = |scale.z|
- **どこに描くかは、C# で計算した行列をシェーダーに渡して決める**。`Projector` 組込みの `_Projector` / `unity_Projector` 行列は使わない
  - Unity 5.6 でビルドしたシェーダーを Unity 2022 のゲーム（COM3D2.5）で使うので、エンジン側の組込み行列の名前の違いに影響されないようにするため
  - `Projector` は「どの物体に描くか」を選ぶカリングだけに使う
- `Projector` は `orthographic=true` とし、箱を少し余裕をもって覆うように設定する
  - 子の `PngDecal` の localScale を root の scale の逆数にして、ワールドでの拡縮を 1 に打ち消す。これで Projector の値はワールド単位で与えられ、Projector 自体が scale を扱うかどうかに左右されない
  - `orthographicSize = 高さ / 2`、`aspectRatio = 幅 / 高さ`
  - `PngDecal` は箱の表側の面より少し手前に置き、near / far で箱の奥行きを覆う
- 更新は `Camera.onPreCull` で行う。カリングより前で、ギズモ操作・タイムライン再生・Undo によるそのフレームの Transform 変更が済んだ後なので、投影が 1 フレーム遅れない
- 計算は純粋な静的関数（`PngDecalProjection`）にまとめ、単体テストで検証する

**投影対象**
- `projectOnMaids=false` のとき、`ignoreLayers` に `Charactor` / `Face` / `Man` の各レイヤーを含める
- レイヤー番号は `LayerMask.NameToLayer` で名前から引き、番号を直書きしない

### シェーダー

`UnityProject/Assets/Shaders/Decal.shader`（シェーダー名 `SE/Decal`）を新しく作る。`Assets/Shaders` フォルダーはフォルダーごと `se_bundle` に割り当てられているので、置くだけでバンドルに入る。ビルドは Unity 5.6 で行う。

`.mat` は Unity 5.6 のバイナリ形式で手書きできない。そのためマテリアルは作らず、`TimelineBundleManager` に `LoadShader(name)` を追加してシェーダーを直接ロードし、デカールごとに `new Material(shader)` する。

- **投影箱の座標**
  - C# から `_DecalMatrix`（ワールド → 投影箱の座標。各軸が -0.5〜0.5）を渡す
  - 箱の外（いずれかの軸で絶対値が 0.5 を超える所）は描かない。奥行き方向の突き抜けもこれで防ぐ
- **UV**: `u = 0.5 - x`、`v = y + 0.5`
  - 板の Quad は Y180 回転していて、Quad の +X が root の -X に当たる。そのため U を反転すると、板と同じ向きになる
- **角度フェード**
  - θ = 面のワールド法線と、投影元の向き（root の +Z。板の表側）がなす角
  - θ ≥ `fadeAngle` のとき不透明度 0、θ ≤ `fadeAngle × 2/3` のとき 1。その間は smoothstep で補間する
  - C# から次の値を渡す
    - `_DecalNormal`: 投影元の向き（ワールド）
    - `_FadeCosMin`: cos(`fadeAngle`)
    - `_FadeCosMax`: cos(`fadeAngle × 2/3`)
  - `fadeAngle` は 1〜90 度に丸める。0 度だと smoothstep の両端が一致してしまうため
  - 目的は、床のデカールが壁の側面へ伸びて映るのを抑えること
- **ブレンド**（`blendMode`）
  - C# から `_SrcBlend` / `_DstBlend` / `_BlendMode` を設定する
  - 通常: `SrcAlpha, OneMinusSrcAlpha`
  - 乗算: `DstColor, Zero`。出力は `lerp(1, 色, α)`
  - 加算: `SrcAlpha, One`
- **描画状態**: `ZWrite Off`、`Offset -1, -1`、`Cull Back`、Queue = `Transparent-500`（= 2500。不透明物の後、半透明物の前）
- **色**: `_Color = color × brightness`。α は不透明度として働く

## データモデル

`PngObjectData` に次のフィールドを追加する。

| フィールド | 型 | 既定値 | 内容 |
|---|---|---|---|
| `displayType` | `PngDisplayType`（`Board=0` / `Decal=1`） | `Board` | 表示タイプ |
| `decalBlendMode` | `PngDecalBlendMode`（`Normal=0` / `Multiply=1` / `Additive=2`） | `Normal` | ブレンド方式 |
| `decalFadeAngle` | float（0〜90 度） | 80 | 角度フェードの上限 |
| `decalProjectOnMaids` | bool | false | メイドにも投影するか |

既存の `billboard` と `renderQueue` は板専用。デカールのときは適用も表示もしないが、値は保持する。

### PngPlacementManager の変更

- セッターを追加する: `SetDisplayType` / `SetDecalBlendMode` / `SetDecalFadeAngle` / `SetDecalProjectOnMaids`
- タイムラインの実体データに保存する設定のセッター（`SetRenderQueue` と新しい 4 つ）は、値が実際に変わったときだけ `entitySettingsRevision`（int）を 1 増やす
  - タイムライン側は毎フレームこの値と前回値を比べ、変わっていれば保存データへ書き戻す
  - イベントの購読・解除を管理しなくて済むので、カウンター方式にする
  - 色と表示はキー側の値で、タイムライン再生中は毎フレーム変わりうる。そのため、これらのセッターではカウンターを増やさない
- `LateUpdate` のビルボード処理は、`displayType == Board` のときだけ行う
- デカールの投影箱と行列は `Camera.onPreCull` で更新する
  - 購読は最初のデカールを生成したときに始める
  - `ReleaseAll` で購読を外す
- `RemovePng` / `ClearAll` では、デカールのマテリアルも破棄する
- 投影箱のパラメータ計算は、Unity のネイティブ呼び出しを含まない純粋な静的関数（`PngDecalProjection`）に切り出し、テストで検証できるようにする

## UI

- **PngPlacementInspector**
  - 先頭に「表示タイプ」の切り替え（板 / デカール）を置く
  - 両方に共通の欄: 表示・色・明るさ
  - 板のとき: ビルボード・表示順
  - デカールのとき: ブレンド（通常 / 乗算 / 加算）・角度フェード・メイドにも投影
  - 変更は既存の `RecordPngEdit`（`HistoryScope.PngPlacement`）を通して Undo 履歴に記録する
- **PngPlacementWindow の「配置済み」一覧**: デカールの行は名前の前に `[デカール]` を付ける
- **GizmoRenderer**
  - 選択中の対象が PNG のデカールなら、投影箱の 12 辺と投影方向の矢印を GL の線で描く
  - 描画は既存の選択バウンディングと同じブロックに置く
  - デカールの root にはレンダラーが無いため、バウンディングは位置だけの小さな箱になり、投影範囲を示せない。そこで、バウンディングの代わりに投影箱を描く
- **変更しない箇所**: 選択の root への丸め、タイムラインレイヤーの自動切り替え、メニュー。PNG の既存経路がそのまま働く

## 保存

### Undo（PngPlacementSnapshot）

プリセット DTO と共用している `CaptureState` / `ApplyState` に新しいフィールドを追加する。変更検知の `Approximately` にも比較を追加する。`ApplyState` はセッターを経由させ、設定変更通知が飛ぶようにする。

### シーンプリセット（ScenePresetPngObject）

- `[XmlAttribute]` として `displayType` / `decalBlendMode` / `decalFadeAngle` / `decalProjectOnMaids` を追加する。既定値はデータモデルと同じ
- 板のときは、4 属性とも `ShouldSerialize*` で書き出さない。これで、板の XML は従来と同じ内容になる
- `ScenePresetData.CurrentVersion` を 35 から 36 に上げ、履歴コメントに追記する
- 属性の無い旧データは、既定値（板）として読む

### タイムライン（TimelinePngObjectXml / TimelinePngObjectData）

- XML 要素を追加する: `DisplayType`（int）/ `DecalBlendMode`（int）/ `DecalFadeAngle`（float、既定 80）/ `DecalProjectOnMaids`（bool）
- 既定値はフィールド初期化子で与え、要素の無い XML でも既定値になるようにする
- 板のときは、4 要素とも `ShouldSerialize*` で書き出さない。MTE 由来や板だけの XML は、保存しても従来と同じ内容になる
- 範囲外の整数値は既定値（板・通常）として扱う
- `FromXml` / `ToXml` に対応を追加する
- `TimelineData.CurrentVersion` は上げない（新しい要素の追加だけで済み、データ移行が要らないため。これまでの前例と同じ）
- キー（`TransformDataPngObject` の 32 値）は変更しない。`PngPlacementTimelineLayer` の既存の適用（位置・回転・ScaleX × ScaleMag・ScaleZ・色・明度・表示）が root の Transform と色に効き、そこから投影箱が決まる

**PngObjectTimelineManager の同期を直す**

現状は、実体の設定（`renderQueue`）がタイムライン XML と正しく往復していない。次の 2 点を直す。

1. **書き戻し**
   - `LateUpdate` で `PngPlacementManager.entitySettingsRevision` の変化を検知したら、`UpdateTimelineData()` を呼ぶ
   - これで Inspector・Undo・プリセット適用での変更が保存データに反映される
   - `UpdateTimelineData` は、デカールの 4 値と `renderQueue` を実体から書き出す
2. **読込時の適用**
   - `Setup` の最後に、XML の各実体データを対応する SE 実体に適用する。対象は、新しく生成した実体と、名前が一致した既存の実体の両方
   - 適用するのは `renderQueue` とデカールの 4 値
   - 読込時は XML を正とする。板では要素を書き出さないので、要素が無い XML（MTE 産や旧 SE を含む）を読み込むと、実体は板に戻る
   - `renderQueue` が 0 以下（要素が無い XML）のときは適用しない
   - `Setup` は引数に `timeline.pngObjects` そのものを受け取る。一方、途中の `RebuildIfChanged` → `UpdateTimelineData` は同じリストを消して書き直す。そのため、`Setup` の冒頭でリストを複製し、適用には複製を使う
   - `renderQueue` を適用するようになるのは挙動の変更だが、保存した値を読込時に戻すという本来の挙動に揃えるための修正とする

### MTE との互換

MTE の XmlSerializer は未知の要素を読み飛ばす。そのため SE で保存したデカールは、MTE で開くと板として表示される。一方向互換の方針の範囲内なので、`W:\COM3D2_5\work\CLAUDE.md` の互換リストに 1 行追記する。

## テスト

`COM3D2.SceneEditor.Plugin.Tests`（xUnit、net48）で行う。Unity のネイティブ呼び出しは避ける。

- **`PngDecalProjection`**: 横長・縦長・正方形の画像、scale が 1 以外の場合、負の scale の場合に、`orthographicSize` / `aspectRatio` / near / far / localPosition が期待どおりになること
- **`ScenePresetPngObject`**
  - 新しい属性を XmlSerializer で往復させて値が保たれること
  - 属性の無い旧 XML が既定値（板・通常・80・false）で読めること
- **`TimelinePngObjectXml` / `TimelinePngObjectData`**
  - `FromXml` / `ToXml` の往復で値が保たれること
  - 要素の無い XML が既定値で読めること

## 検証

- **ビルド**
  - COM3D2 と COM3D25 の両構成を MSBuild で直接ビルドする
  - `build-bundle.bat` で `se_bundle` を再生成する（UnityProject をエディタで開いていないこと）
- **実機検証**（COM3D2.5、devbridge、`restart-verify` の手順）
  1. スタジオ背景の床と壁に投影されること
  2. メイドが除外されること。「メイドにも投影」を ON にすると映ること
  3. ブレンド 3 種がそれぞれ効くこと。角度フェードで壁の側面への伸びが消えること
  4. 板とデカールを切り替えても、正面から見た絵柄の向きが一致すること
  5. ギズモで拡縮したとき、投影箱のワイヤーと実際の投影範囲が一致すること
  6. Undo / Redo、シーンプリセットの保存と読込、タイムラインの保存と読込とキー再生で、表示タイプとデカール設定が保たれること
- COM3D2（2.0）はビルドの確認までとし、実機検証は範囲外とする

## 既知の制約

- 投影範囲内のオブジェクトごとに描画が 1 回増える（負荷は範囲内オブジェクト数 × デカール数）
- 透明な物体や他の PNG 板にも映ることがある
- デカールはシーンのクリックでは選択できない。「配置済み」一覧から選ぶ
- ライティングは反映されない（明るさで手動調整する）

## 範囲外

- メイドの肌や衣装への追従
- デカール固有値のキーフレーム補間
- 利用者向けドキュメント（docs-site）の更新（release-prep の時点で追従させる）
