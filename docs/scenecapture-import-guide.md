# 外部プラグイン向け SceneCapture プリセット取り込みガイド

SceneEditor が読み込んだ **SceneCapture プリセット**の `<Models>` / `<Effects>` を、
外部プラグイン側で適用するための資料。

対象読者: ModItemExplorer（モデル配置）/ PostEffects（ポストプロセス）など、
SceneCapture の担当セクションを再現できるプラグインの実装者。

前提として [scene-preset-provider-guide.md](scene-preset-provider-guide.md) の
プロバイダ登録が済んでいること。

## 概要

SceneEditor はシーンプリセット一覧に、SceneCapture プラグインのプリセット
（`Config\SceneCapture\Presets\*.xml`）を**読み込み専用の仮想フォルダ**として並べる。
保存・削除・リネームは一切行わない。

読み込むとこう動く。

1. `<Camera>` / `<Misc>`（背景）/ `<Lights>` / `<LightShafts>` は
   SceneEditor 本体が内部形式へ変換して適用する
2. `<Models>` か `<Effects>` に中身があれば、`ApplySceneCaptureXml` を実装している
   **全プロバイダ**へ `<Preset>` XML 全体をそのまま渡す

各プロバイダは**自分の担当セクションだけを解釈**し、他は無視する。
メイド（配置・ポーズ・表情）は SceneCapture プリセットに含まれず、適用でも一切触らない。

## 契約

```csharp
/// <summary>SceneCapture プリセット XML を適用する。成功可否を返す</summary>
public static bool ApplySceneCaptureXml(string xml)
{
    return SceneCaptureImporter.Apply(xml);
}
```

- **public static**、`bool` を返す `string` 1 引数メソッド。シグネチャは完全一致が必要
- 担当セクションが無い / 空なら、何もせず `true` を返す
- 適用に失敗したら `false`。SceneEditor は警告ログを出すだけで他プロバイダの適用は続行する
- 例外を投げてもプロバイダ単位で catch される（エラーログのみ）

**この 1 メソッドだけではプロバイダとして登録されない。**
従来どおり `PresetProviderId` / `PresetProviderDisplayName` と、
テキスト対（`CapturePresetXml` + `ApplyPresetXml`）かバイナリ対
（`CapturePresetBinary` + `ApplyPresetBinary`）のどちらかが必要。
シグネチャが合わない `ApplySceneCaptureXml` は契約不備とはせず単に無視される。

`ApplySceneCaptureXml` を実装したプロバイダが 1 つも見つからない場合、
SceneEditor は次の警告を出す。

```
SceneCapture のモデル・エフェクトを適用できる外部プラグインが見つかりません
```

## XML 全体構造

```xml
<Preset>
  <Effects>
    <BloomDef><bloomIntensity>2.85</bloomIntensity>...</BloomDef>
    <!-- 有効なエフェクトのみ Def 要素が存在。値は「フィールド名 = 要素名」で文字列化 -->
  </Effects>
  <Lights>
    <Light>
      <Position>0,2,0</Position><EulerAngles>40,180,18</EulerAngles>
      <Intensity>0.9</Intensity><Range>10</Range><SpotAngle>30</SpotAngle>
      <Color>255,255,255,255</Color><Type>1</Type><Enabled>True</Enabled>
      <shadows>2</shadows><shadowStrength>0.098</shadowStrength>
      <shadowBias>0.01</shadowBias><shadowNormalBias>0.4</shadowNormalBias>
    </Light>
  </Lights>
  <LightShafts />   <!-- LightShaft: Light と同じ 12 要素 + シャフト固有要素 -->
  <Models />        <!-- Model: 下記参照 -->
  <Camera>
    <Position>0,1.5,0</Position>          <!-- 注視点 (MainCamera.GetTargetPos()) -->
    <Rotation>354.9,186.9,0.2</Rotation>  <!-- transform euler (x=pitch, y=yaw, z=roll) -->
    <Distance>2</Distance><FieldOfView>25</FieldOfView>
  </Camera>
  <Misc><Background></Background><Version>0.3.1.27</Version></Misc>
</Preset>
```

共通の書式:

| 型 | 書式 |
|---|---|
| 数値 | InvariantCulture |
| Vector3 | `"x,y,z"` |
| Quaternion | `"x,y,z,w"` |
| Color32 | `"r,g,b,a"`（各 0-255） |
| bool | `"True"` / `"False"` |
| enum | int 値 |

## `<Models>` セクション（ModItemExplorer 向け）

`Model` 要素 1 つがモデル 1 体。要素は以下の 12 個。

| 要素 | 型 | 内容 |
|---|---|---|
| `Position` | Vector3 | ワールド座標 |
| `Rotation` | Quaternion | `"x,y,z,w"`。オイラー角ではない |
| `LocalScale` | Vector3 | |
| `ModelType` | int | 0=MaidEquip / 1=BGObject / 2=Background / 3=MyRoom / 4=MyRoomObject |
| `MenuFileName` | string | MaidEquip のとき `.menu` ファイル名 |
| `ObjectLayer` | int | 読み取れない場合の既定値は 20 |
| `BGObjectId` | long | |
| `myRoomObjectId` | int | |
| `ModelID` | string | |
| `ModelName` | string | |
| `ModelIconName` | string | |
| `ModelCastShadow` | bool | 影を落とすか（下記の注意あり） |

注意:

- **`ModelCastShadow` は本家では読み戻されない。** SceneCapture は書き出しを
  `ModelCastShadow`、読み込みを `CastShadow` という別名で行っているため、本家の読み込みでは
  常に既定値 `true` になる。取り込み側は `ModelCastShadow` を読めば意図どおりの値が得られる
- 古いプリセットには `ModelCastShadow` 自体が無い。欠落時は `true` 扱いが本家互換
- `ModelType` が `MaidEquip` かつ `MenuFileName` が `.menu` を含む場合、本家は
  読み込み時に menu の存在を確認し、失敗したらそのモデルを捨てる

参考実装: SceneCapture 本家の
`CM3D2/SceneCapture/Plugin/Instances.cs` の `LoadModel`（709 行目付近）/ `SaveModels`。

## `<Effects>` セクション（PostEffects 向け）

- **要素名 = Def クラス名**（例: `BloomDef`）
- 子要素名 = 対応するエフェクトコンポーネントの**フィールド名**、値はその文字列表現
- **要素が存在しないエフェクトは無効**という意味論。SceneCapture は
  `enabled == true` のエフェクトだけを書き出す
- Texture / Texture2D / Texture3D 型のフィールドは、Config フォルダからの
  相対パス文字列として保存される（`<フィールド名>File` プロパティの値）
- Shader 型フィールドは保存されない

書き出される Def は以下の 34 種。

```
MaidHideDef, AntialiasingDef, ColorCorrectionCurvesDef, ContrastDef, CreaseDef,
EdgeDetectDef, EdgeDetect2Def, GrayscaleDef, MotionBlurDef, NoiseAndGrainDef,
SepiaDef, SunShaftsDef, TiltShiftHdrDef, AnalogGlitchDef, DigitalGlitchDef,
IsolineDef, ObscuranceDef, CinematicBloomDef, CinematicBloomLayerDef,
FilmicBloomDef, BloomDef, StreakDef, DepthOfFieldDef, CinematicDepthOfFieldDef,
BokehDef, FilmicBokehDef, CinematicLensAberrationsDef, FisheyeDef,
FilmicMedianFilterDef, RampDef, ColorCorrectionLutDef, TonemappingColorGradingDef,
FilmicLetterBoxDef, StylisticFogDef
```

参考実装: SceneCapture 本家の
`CM3D2/SceneCapture/Plugin/SerializeStatic.cs` の `SaveDef` / `LoadDef`
（フィールド反射による読み書き）。

## SceneEditor 側が処理する範囲

以下は SceneEditor 本体が適用するため、プロバイダは**触らないこと**（二重適用になる）。

| セクション | SceneEditor 側の扱い |
|---|---|
| `<Camera>` | 注視点 / yaw / pitch / roll / 距離 / FOV としてメインカメラへ適用 |
| `<Misc>` の `Background` | 背景 id を逆引きして適用。空なら背景を変更しない |
| `<Lights>` | 先頭 1 灯をゲームのメインライト、以降を追加ライトとして適用 |
| `<LightShafts>` | シャフト固有の要素を捨て、追加ライトとして適用 |

追加ライトとして適用されるのは
`Position` / `EulerAngles` / `Intensity` / `Range` / `SpotAngle` / `Color` / `Type` / `Enabled`
の 8 要素のみ。SceneEditor の追加ライトが影のパラメータを持たないため、
影関連の 4 要素（`shadows` / `shadowStrength` / `shadowBias` / `shadowNormalBias`）は
`<LightShafts>` でも `<Lights>` の 2 灯目以降でも捨てられる
（メインライトになる 1 灯目のみ `shadowStrength` が反映される）。

## 適用タイミング

通常のシーンプリセットと違い、**メイドのロード待ちを挟まない同期実行**になる。
サイドカーも `ApplyPresetXml` も介さない。

通常のプリセット適用と同じく、**適用の開始時点でホストの操作履歴は全クリアされる**。
`ApplySceneCaptureXml` の中で [HistoryAPI](history-guest-guide.md) へ `Register` すると
エントリが残ってしまうため、復元は履歴に積まないのが望ましい。

## 参照

- 変換ローダー: `source/COM3D2.SceneEditor.Plugin/Manager/SceneCapturePresetLoader.cs`
- 適用フロー: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
  （`ApplySceneCapturePreset` / `ApplySceneCaptureExternals`）
- プロバイダ登録: [scene-preset-provider-guide.md](scene-preset-provider-guide.md)
