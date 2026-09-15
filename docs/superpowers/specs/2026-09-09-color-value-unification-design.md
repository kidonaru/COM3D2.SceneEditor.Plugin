# 色値の統一（ColorValueInfo）設計

作成日: 2026-09-09
状態: 承認済み（実装計画は別ファイル）

## 背景

タイムラインのキーフレームデータ `ITransformData` は、値を平坦な `ValueData[] values` で持ち、その上に
「float カスタム値（`CustomValueInfo`）」「文字列値（`StrValueInfo`）」という 2 種の型付きビューを重ねている。
色はこの枠に収まっておらず、次の 2 系統に分かれている。

- **主色・副色**: `hasColor` / `colorValues` / `hasSubColor` / `subColorValues` / `initialColor` / `initialSubColor` と
  `color` / `subColor` プロパティ。14 型が実装する
- **3 色目以降**: R/G/B/A の 4 成分を個別の float カスタム値として登録する
  （ModelMaterial の影色・リム色・アウトライン・エミッション・Matcap・MatcapMask・RimLight、
  PsylliumBar の中心・縁・散乱 × 2、Bloom のフレア B/C/D）

この二重構造のため、Inspector のキーフレーム詳細では 3 色目以降が「影色 R」「影色 G」… の数値欄 4 行に
分かれて表示され、タイムライン項目の現在値 UI（カラーピッカー行）と見た目が揃わない。
また色成分が `tangentValues` に含まれるため「補間曲線」タブとカーブエディタに色チャンネルが並ぶが、
再生時の色補間は各レイヤーが `Color.Lerp` で線形に行っており、タンジェントを編集しても効かない。

## 目的

1. 色を float / string と並ぶ **第 3 の値種別** として統一し、主色・副色と 3 色目以降の区別を無くす
2. 色の補間は **線形** に統一し、タンジェント編集の対象から外す
3. キーフレーム詳細で全ての色をカラーピッカー行で編集できるようにし、数値欄のラベル切り詰めも解消する

## 非目的

- 再生時の補間処理の変更（各レイヤーの `Color.Lerp` は既に線形なので触らない）
- 現在値 UI（`Timeline/ItemInspector/*`、各 `*RowDrawer`）の見直し
- レイヤー固有の並び・見出しをキーフレーム詳細で再現すること（別案件「案 B」）
- HDR 色（成分 > 1）の編集対応。カラーピッカーの既存制約をそのまま引き継ぐ

## 設計

### 1. データモデル

`Timeline/TransformData/ITransformData.cs` に追加する。

```csharp
public class ColorValueInfo
{
    public string name;          // 表示名 (例: "色", "副色", "影色")
    public int indexR;
    public int indexG;
    public int indexB;
    public int indexA = -1;      // -1 なら RGB のみ (BGColor / Light など)
    public Color defaultValue;   // リセット時の色

    public bool hasAlpha => indexA >= 0;
}
```

`ITransformData` / `TransformDataBase` の変更:

| 区分 | メンバー | 内容 |
|---|---|---|
| 追加 | `Dictionary<string, ColorValueInfo> GetColorValueInfoMap()` | 型ごとの静的マップを返す。既定は空 |
| 追加 | `ColorValueInfo GetColorValueInfo(string key)` | 無ければ `LogError` して null（`GetCustomValueInfo` と同じ流儀） |
| 追加 | `Color GetColorValue(string key)` / `void SetColorValue(string key, Color color)` | `indexA < 0` の場合、取得時のアルファは 1、設定時のアルファは捨てる |
| 追加 | `Color GetDefaultColorValue(string key)` / `bool HasColorValue(string key)` / `string GetColorValueName(string key)` | float 用と同名規則 |
| 削除 | `hasColor` / `hasSubColor` / `colorValues` / `subColorValues` / `initialColor` / `initialSubColor` | 用途はすべて色マップへ移る |
| 維持 | `Color color { get; set; }` / `Color subColor { get; set; }` | 基底クラスの糖衣。固定キー `ColorKey.Main = "color"` / `ColorKey.Sub = "subColor"` を参照する |

- キー定数は `TransformDataBase` の入れ子 `public static class ColorKey` に置く
- `Reset()` は `GetColorValueInfoMap()` を列挙して `defaultValue` を書く（`hasColor` 分岐を置き換える）
- `GetCustomValueInfoMap()` からは色成分のエントリ（`"ShadowColor.r"`、`"中心色1R"` 等）を外す。
  これらのキーを名前で参照するコードは TransformData の外に存在しない（grep で確認済み）

各 TransformData の書き換え:

- `hasColor` を override している 14 型（BGColor / BGGroundColor / Bloom / DistanceFog / Light / ModelMaterial /
  Paraffin / PngObject / Rimlight / StageLaser / StageLaserController / StageLight / StageLightController / Text）:
  `colorValues` / `subColorValues` / `initialColor` / `initialSubColor` の定義を `ColorValueInfoMap` の
  `"color"` / `"subColor"` エントリへ移す
- ModelMaterial / PsylliumBar / Bloom（フレア）: 4 成分のカスタム値エントリを削除し、色 1 個 = 1 エントリを追加する。
  キー名は既存の型付きアクセサ名に揃える（例: `"ShadowColor"`、`"color1a"`、`"flareColorB"`）
- 既存の型付き `Color` アクセサ（`ShadowColor` / `color1a` / `flareColorB` など）は `GetColorValue(key)` /
  `SetColorValue(key, value)` の呼び出しへ実装を置き換えて **名前を維持する**。
  これにより `ModelMaterial.Lerp` や `PostEffectRowDrawer` 側の変更を避ける
- 現在値 UI が参照している `defaultTrans.initialColor` / `initialSubColor`（`PostEffectRowDrawer` /
  `StageLightRowDrawer` / `StageLaserRowDrawer`）は `defaultTrans.GetDefaultColorValue(ColorKey.Main / Sub)` へ置き換える

### 2. 補間とタンジェント

- 色は線形補間で統一する。各レイヤーの再生コードは既に `Color.Lerp(start.color, end.color, t)`
  （ModelMaterial は `colorTime` = 区間進行率）なので変更しない
- `tangentValues` から色成分を除外する。`TransformDataBase` に
  `protected ValueData[] valuesWithoutColors`（`values` から色マップの全 index を除いた配列。初回にキャッシュ）を追加し、
  `tangentValues => values` としている型は `=> valuesWithoutColors` へ差し替える。
  `baseValues` / `positionValues` を返している型はそのまま
- `TangentTargetList` / `TimelineCurveEditor` / 「補間曲線」タブは `tangentValues` と
  `GetCustomValueInfoMap()` しか見ていないため、コード変更なしで色チャンネルが消える

### 3. UI

`KeyFrameInspector`（キーフレーム詳細）:

- `GetColorValueInfoMap()` を列挙し、色ごとに `view.DrawColor(fieldCache, color, default, onChanged)` を描く。
  `fieldCache` は `view.GetColorFieldCache(label, info.hasAlpha)`
- ラベルは `"{info.name} (F{frameNo}) {bone.name}"`。`ColorPickerWindow` はラベル文字列で編集対象を同定するため
  キーフレームごとに一意である必要があり、先頭に色名を置いて 90px のラベル幅で読めるようにする
- 既存の「`hasColor` なら `transform.color` を描く」分岐は削除し、上の汎用経路に統一する

ラベル幅:

- `Extensions.DrawCustomValueFloat / Int / Bool` に省略可能な `float labelWidth = 40f` を追加し、
  キーフレーム詳細からは 70 を渡す。既定値を変えないので現在値 UI の見た目は不変
- `KeyFrameInspector` に定数 `CustomLabelWidth = 70f` を置く（文字列値の `StrLabelWidth` と同値）

### 4. XML 互換

- 値は `TransformXml.values`（`values` 順の平坦 float 配列）、タンジェントも `values` 順で保存されるため、
  **レイアウトは変わらない**。`TimelineData.CurrentVersion` の更新もマイグレーションも不要
- カスタム値のキー名は XML に出ないため、成分キーの廃止は互換に影響しない
- 既存ファイルに残る色成分のタンジェント値は読み込まれるが、`tangentValues` から外れるため使われない
- `TimelineXml.Migrate` の既存処理（ShadowColor 等へのアルファ追加、`TimelineXml.cs:1233` 付近）は
  index ベースなので影響なし
- MTE → SceneEditor の一方向互換（CLAUDE.md 記載）は維持される。逆方向は従来どおり非対応

### 5. テスト

`source/COM3D2.SceneEditor.Plugin.Tests` に `ColorValueInfoTests` を追加し、
`TimelineManager.CreateTransform` で生成できる全 `TransformType` を総当たりで検証する。

- 色マップの全 index が `0 <= index < valueCount` で、色同士・カスタム値と重複しない
- `tangentValues` に色マップの index が含まれない
- `"color"` キーを持つ型で `color` 糖衣と `GetColorValue(ColorKey.Main)` が一致する（`subColor` も同様）
- `Reset()` 後に各色が `defaultValue` になる
- 既存の XML ラウンドトリップテスト（`XmlRoundTripTests` / `MteCompatibilityTests`）が引き続き通ること

描画コードは Unity GUI 依存のため単体テストせず、両構成の MSBuild と実機スクリーンショット
（`screenshot window="#8903352"` で Inspector を切り抜く）で確認する。

### 6. 影響ファイル

| 区分 | ファイル |
|---|---|
| データ | `Timeline/TransformData/ITransformData.cs`、`TransformDataBase.cs`、色を持つ 15 型（`hasColor` を持つ上記 14 型 + カスタム値だけで色を持つ PsylliumBar） |
| UI | `KeyFrameInspector.cs`、`Timeline/Extensions.cs`、`PostEffectRowDrawer.cs`、`StageLightRowDrawer.cs`、`StageLaserRowDrawer.cs` |
| テスト | `COM3D2.SceneEditor.Plugin.Tests/ColorValueInfoTests.cs`（新規） |
| ドキュメント | `CHANGELOG.md` |

レイヤーの再生コード（`Timeline/TimelineLayer/*`）は `trans.color` / `trans.subColor` の糖衣を使い続けるため原則変更しない。
`hasColor` を直接参照している箇所があれば `HasColorValue(ColorKey.Main)` へ置き換える。

## 決定の記録

- 主色・副色も新マップへ統合する（A 案「3 色目以降だけ」ではなく B 案）。線形補間へ統一する意図を
  データ構造にも反映するため
- 色チャンネルはカーブ編集から完全に外す。線形なら直線しか出ず、閲覧専用で残す価値が薄い
- `color` / `subColor` の糖衣は残す。全廃すると 11 ファイル・約 50 箇所の機械的な書き換えになるが設計上の利点が薄い
