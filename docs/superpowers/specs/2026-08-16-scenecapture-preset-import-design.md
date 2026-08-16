# SceneCapture プリセット Readonly 読み込み — 設計書

日付: 2026-08-16
対象リポジトリ: COM3D2.SceneEditor.Plugin / COM3D2.ModItemExplorer.Plugin / COM3D2.PostEffects.Plugin

## 目的

SceneEditor のシーンプリセット機能で、COM3D2.SceneCapture.Plugin のプリセット
（`UnityInjector\Config\SceneCapture\Presets\*.xml`、ルート要素 `<Preset>`）を
**Readonly（読み込み専用）** で利用できるようにする。
モデル配置は ModItemExplorer、ポストエフェクトは PostEffects へ委譲して適用する。

## 背景（調査結果の要点）

- SceneEditor のプリセットは `ScenePresetManager`（`Manager/ScenePresetManager.cs`）が
  独自 XML（`ScenePresetData` v15）＋サイドカー＋PNG サムネで管理。
  外部プラグインとは `ScenePresetProviderRegistry`
  （短名 `ScenePresetProviderAttribute` + public static 4メンバ規約）で連携済み。
- SceneCapture プリセットは単一 XML。セクションは
  `Effects`（34種の `*Def`、有効なもののみ要素が存在）/ `Lights` / `LightShafts` /
  `Models` / `Camera` / `Misc`（Background / Version）。メイド情報・サムネイルは無い。
- ModItemExplorer: `ModelPlacementPresetProvider`（id `ModItemExplorer.ModelPlacement`）、
  配置本体は `SelfModelPlacer.instance`。
- PostEffects: `PostEffectsScenePresetProvider`（id `PostEffects`）、
  設定本体は `EffectSettings.instance`（SceneCapture の Def 群とほぼ同名のフィールドを持つ）。

## 全体アーキテクチャ

### SceneEditor（本体）

1. **仮想フォルダ**: `ScenePresetManager` のツリールート直下に「SceneCapture」仮想フォルダを
   追加し、`UnityInjector\Config\SceneCapture\Presets` 配下の `*.xml` を列挙する。
   フォルダが存在しない環境では仮想フォルダ自体を表示しない。
2. **Readonly アイテム**: `ScenePresetItem` に `isReadonly` / `isSceneCapture` フラグを追加。
   Readonly アイテムは削除ボタン非表示・上書き保存対象外。
   サムネイルはプレースホルダ表示（SceneCapture 形式にサムネは無い）。
3. **読み込み分岐**: 読み込み時にルート要素が `<Preset>` なら SceneCapture パーサへ分岐。
   新クラス `SceneCapturePresetLoader` が XML を解釈する:
   - **Camera / Misc(Background) / Lights** → 既存の `CameraSnapshot` /
     `BackgroundSnapshot` / `LightSnapshot` の Apply 経路にマッピングして直接適用。
     `LightShafts` はライト基本12要素（位置・回転・強度・範囲・色・種別等）のみ反映し、
     シャフト固有パラメータは対象外。
   - **Models / Effects** → 生の `<Preset>` XML 文字列を各外部プロバイダへ委譲。
4. **プロバイダ規約の拡張**: `ScenePresetProviderRegistry` に
   **任意メソッド `bool ApplySceneCaptureXml(string xml)`** のバインドを追加。
   バインドされているプロバイダにのみ `<Preset>` XML 全体を渡す
   （セクション抽出は各プラグインの責務）。
   `docs/scene-preset-provider-guide.md` を更新する。

### ModItemExplorer

5. `ModelPlacementPresetProvider` に `ApplySceneCaptureXml(string xml)` を追加。
   `<Models>/<Model>` を解釈し、`MenuFileName` / `Position` / `Rotation` /
   `LocalScale` / `ModelType` を `SelfModelPlacer.CreateModel` ベースで配置する。
   既存 `ApplyPresetXml` と同じ「自前配置を全削除してから復元」の置き換え動作とする。
   `ModelType`（0=MaidEquip / 1=BGObject / 2=Background / 3=MyRoom / 4=MyRoomObject）
   による menu 名解決の差異は SceneCapture の `Instances.LoadModel` 実装を参考に対応する。

### PostEffects

6. `PostEffectsScenePresetProvider` に `ApplySceneCaptureXml(string xml)` を追加。
   `<Effects>` の各 `*Def` 要素を `EffectSettings.instance` の対応フィールドへ
   マッピングする（例: `BloomDef` → `bloom`）。
   要素が存在しないエフェクトは無効化する（SceneCapture と同じ意味論）。
   フィールド名が一致しない・存在しないパラメータはベストエフォートとし、
   対応表は新クラス `SceneCaptureEffectMapper` に集約する。
   テクスチャ参照（LUT / Ramp 等、Config 相対パス文字列）は
   パスが解決できる場合のみ適用する。

## エラーハンドリング

- パース失敗・未対応要素は日本語の警告ログを出して続行する
  （1要素の失敗で全体の適用を止めない）。
- `ApplySceneCaptureXml` 未実装のプロバイダには SceneCapture XML を渡さない。
- `ApplySceneCaptureXml` が false を返した場合も警告ログのみで続行する。

## スコープ外

- SceneCapture 形式への書き出し（保存は常に SceneEditor 独自形式）。
- LightShafts のシャフト固有パラメータ再現。
- SceneCapture プリセットの編集・削除・リネーム。
- メイド関連の状態（SceneCapture 形式に含まれない）。

## テスト

- 実プリセット（`Config\SceneCapture\Presets` の実ファイル）をフィクスチャにした
  パーサ単体テスト（各リポジトリのテスト慣行に合わせる）。
- 実機検証: com3d25-devbridge でプリセット読み込みを実行し、
  カメラ / 背景 / ライト / モデル / エフェクトの反映をスクリーンショットで確認する。
