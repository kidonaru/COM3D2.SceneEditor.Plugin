# ステージライト 体積円錐化 設計

作成日: 2026-09-11

## 目的

ステージライト（`StageLight`）の見た目を、カメラへ向けた板ポリ 1 枚のビルボードから、視線と円錐の交差区間を積分する体積円錐に置き換え、実際のスポットライトの光芒に近づける。

## 現状

- `Timeline/UnityScripts/StageLight.cs` が `segmentAngle × segmentRange` の平面メッシュを生成し、`UpdateTransform` で毎フレーム `LookAt(camera)` してビルボード化している（ライト軸回りの回転のみ許可）。真上・真下から見ると板が薄く見えて破綻する。
- シェーダ `MTE/StageLight` は `UnityProject/Assets/Shaders/StageLight.shader` にあり、Unity 5.6.4f1 でビルドした `mte_bundle`（SE の DLL に `EmbeddedResource` として埋め込み）から `TimelineBundleManager.LoadMaterial("StageLight")` で読み込む。
- 実機（COM3D2.5 = Unity 2022.3.62f2）で 5.6 製バンドルのシェーダは `isSupported=true`。メインカメラの `depthTextureMode` は `Depth | DepthNormals`。

## 方針

### スコープ

- 対応対象は **COM3D2.5 のみ**。バンドルは Unity 2022.3.62f2 でビルドする。
- COM3D2 (2.0) ビルドは従来の 5.6 製 `mte_bundle` を埋め込み続け、レーザー・ペンライト・アイコン等は従来どおり動かす。ステージライトの描画は **非対応**（レンダラを作らず警告ログを 1 回出す）。
- 注視点ターゲット等の操作性改善はスコープ外。
- データ形式（`TransformDataStageLight` の値数・XML）、UI（`StageLightRowDrawer` / `StageLightItemInspector`）は変更しない。

### 1. シェーダ `MTE/StageLight`（`UnityProject/Assets/Shaders/StageLight.shader`）

- 描画対象は閉じた円錐メッシュ 1 個。`Cull Front`（裏面のみ描画）でカメラが円錐内部にあっても描ける。`ZTest Always` / `ZWrite Off` / `Blend One One`（加算）。
- 頂点シェーダ: クリップ座標とワールド座標を出力するのみ。
- フラグメントシェーダ:
  1. カメラ位置と視線方向をオブジェクト空間へ変換（`unity_WorldToObject`）。
  2. 無限円錐 `x² + y² = (z·tanHalf)²` と視線の交差区間 `[t0, t1]` を 2 次方程式で解析的に求め、`z ∈ [_OffsetRange, _SpotRange]` の平面でクリップする。カメラが円錐内にある場合は `t0 = 0`。
  3. `_DepthClip` が有効なら `_CameraDepthTexture` からシーン深度（`LinearEyeDepth`）を取り、視線方向のワールド距離に換算して `t1` を切り詰める。
  4. 区間を `SAMPLE_COUNT = 16` で等分し、各サンプル点で「距離減衰（`pow(1 - z/_SpotRange, _FalloffExp)` + `_EdgeSoftness` の smoothstep）× 半径減衰（`_CoreRadius` 以内は 1、外側へ smoothstep で 0）× ノイズ」を積算する。ノイズはサンプル点のワールド座標から XY 平面と ZY 平面の 2 枚を平均（`StageLaser.shader` と同じ）し、`_ScrollSpeed` でスクロールする。
  5. 積算値 × 区間長 / `SAMPLE_COUNT` × `_Density` を透過率とし、`lerp(_SubColor, _Color, alpha)` に乗算して出力する。
- 新規プロパティ: `_OffsetRange`（円錐の開始 z）、`_Density`（明るさ正規化係数。既定 `1 / _SpotRange` 相当を C# 側で計算して渡す）、`_DepthClip`（0/1）。既存の `_ZTest` プロパティは削除し、C# 側の `zTest` トグルは `_DepthClip` に写像する。
- 既存プロパティ（`_MainTex` / `_Color` / `_SubColor` / `_ScrollSpeed` / `_FalloffExp` / `_EdgeSoftness` / `_SpotRange` / `_NoiseStrength` / `_NoiseScaleInv` / `_CoreRadius` / `_TanHalfAngle`）は維持。`_SpotAngle` は未使用になるため削除する。

### 2. `Timeline/UnityScripts/StageLight.cs`

- `UpdateMesh`: 頂点 = 先端リング（`z = offsetRange`、半径 `offsetRange·tanHalf`）と底面リング（`z = range`、半径 `range·tanHalf`）の各 `segmentAngle + 1` 点 + 両リングの中心点。側面・先端キャップ・底面キャップの三角形を生成し、法線が外向きになる巻き順にする（`Cull Front` で裏面が描かれる）。`segmentAngle` の下限は 3 に丸める。`segmentRange` はデータ互換のため保持し未使用。
- `UpdateTransform`: `LookAt` を削除。メッシュ子オブジェクトは常にローカル恒等回転・原点。スポットライト実体（`spotLight`）への位置・回転同期は維持。
- `UpdateMaterial`: `_OffsetRange` / `_Density` / `_DepthClip` を追加、`_SpotAngle` / `_ZTest` の設定を削除。
- `Initialize`: `#if COM3D25` 以外ではメッシュオブジェクト・レンダラを生成せず、静的フラグで 1 回だけ「COM3D2 ではステージライトの描画は未対応」を警告ログに出す。`visible` は `_meshObject == null` のとき false を返す（既存実装のまま）。
- `UnityProject/Assets/Scripts/StageLight.cs` にも同じ内容を反映する（エディタ確認用。正本は SE 側）。

### 3. バンドルとビルド手順

- `UnityProject/` をリポジトリに追加する。`.gitignore` に `UnityProject/Library` `UnityProject/Logs` `UnityProject/Temp` `UnityProject/obj` `UnityProject/UserSettings` `UnityProject/*.sln` `UnityProject/*.csproj` `UnityProject/Assets/PostProcessing*` を追加。`Assets/Bundles/` は生成物なので `UnityProject/Assets/Bundles/` も無視する（正本は `source` 側のコピー）。
- `build-bundle.bat`（リポジトリ直下）を新設: `.env` の `UNITY_2022_DIR`（未設定時は `C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe`）で `-batchmode -nographics -quit -projectPath UnityProject -executeMethod CreateAssetBundles.BuildAllAssetBundlesBatch -logFile UnityProject/Logs/build-bundle.log` を実行し、成功後 `UnityProject/Assets/Bundles/mte_bundle` を `source/COM3D2.SceneEditor.Plugin/Timeline/mte_bundle_2022` へコピーする。
- csproj: `<EmbeddedResource Include="Timeline\mte_bundle">` を `GameVersion == COM3D2` 条件付きにし、`GameVersion == COM3D25` では `Timeline\mte_bundle_2022` を同じ `LogicalName=mte_bundle` で埋め込む。`TimelineBundleManager` は無変更。
- README（またはビルド手順ドキュメント）に「UnityProject 内 Scripts はエディタ確認用で正本は `Timeline/UnityScripts`」「バンドル再ビルドは `build-bundle.bat`」を追記する。

### 4. 検証

- シェーダ調整: devbridge `eval_csharp` で `AssetBundle.LoadFromFile` により新バンドルをファイルから読み、REPL 上で円錐メッシュ + 新マテリアルのテスト物体を生成し `screenshot` で確認する。ゲーム再起動なしで反復する。バンドル名が既存ロード済みと衝突するため、テスト用は別名でコピーして読む。
- 既定値（角度 10°、距離 10、減衰 0.5、コア 0.2、ノイズ 0.2）で従来と近い明るさになるよう `_Density` の係数を調整する。
- ビルド: `debug.bat all`（または MSBuild 直叩き）で COM3D2 / COM3D25 両構成がビルドできること。
- 最終確認: 新 DLL でゲームを再起動し、既存タイムライン（`tools/timeline/scenes/kasou-hiro`）のステージライトが表示され、真上・真下・円錐内部からの視点で破綻しないこと、床・人物との交差がソフトであること。

## 影響範囲

- 変更: `Timeline/UnityScripts/StageLight.cs`、`UnityProject/Assets/Shaders/StageLight.shader`、`UnityProject/Assets/Scripts/StageLight.cs`、csproj、`.gitignore`、README
- 追加: `UnityProject/`（新規追跡）、`build-bundle.bat`、`Timeline/mte_bundle_2022`
- 無変更: データ形式、XML、UI、`TimelineBundleManager`、他レイヤー
