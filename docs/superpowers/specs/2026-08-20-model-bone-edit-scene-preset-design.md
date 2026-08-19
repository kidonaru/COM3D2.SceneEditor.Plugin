# モデルボーン編集のシーンプリセット統合 設計

日付: 2026-08-20

## 目的

SceneEditor のボーン編集機能でモデル（ModItemExplorer 等の外部プラグインが配置した GameObject）に加えたボーン差分を、シーンプリセットに保存・復元できるようにする。

## 背景

- メイドのボーン編集は既に `ScenePresetData.maid[].boneEdits` として保存済み（v6 以降）。
- モデルのボーン差分は `BoneEditManager._modelStores`（キー: GameObject 参照）に保持され、PartsEdit 互換の個別プリセット保存（`PartsEditPresetIO.SaveModel/ApplyModel`）までは実装済み。シーンプリセットには未統合。
- モデル配置の保存/復元は ModItemExplorer がシーンプリセットプロバイダ規約（external）に相乗りしており、`ScenePresetManager.FinishApply` → `ApplyExternals` で**同期**復元される。external 適用直後にはモデル GameObject が全て存在する。

## 決定事項

| 論点 | 決定 |
|---|---|
| 保存場所 | SceneEditor 側の `ScenePresetData`（ModItemExplorer は無改修） |
| 照合キー | ラッパー GameObject の `name`（+ `pluginName`） |
| 適用タイミング | プリセット復元時の 1 回のみ（`ApplyExternals` 直後）。再生成追跡はしない |

## 設計

### 1. データモデル（ScenePresetData.cs）

- `ScenePresetData.version` を 18 に更新（互換履歴コメントに追記）。
- 新 DTO `ScenePresetModelBoneEdit` を追加:
  - `modelName`: ラッパー GameObject の name（照合キー。ModItemExplorer の `GetModelName(fileName, group)` 由来で、同名複数体は group で区別済み）
  - `pluginName`: ModelProviderHost に登録されたプロバイダ名（他プロバイダとの名前衝突回避用）
  - `bone` / `pos[3]` / `rot[4]` / `scl[3]`: 既存 `ScenePresetBoneEdit` と同じ生 float 配列形式
- slot/item は保存しない（モデルは固定スロット `"model"` のみ、itemFileName は null のため）。
- 旧プリセットは `modelBoneEdits` が空になるだけで互換性は保たれる。

### 2. 保存（ScenePresetManager.Capture）

- メイド boneEdits の保存箇所（`ScenePresetManager.cs:845` 付近）と並べて、モデル分を保存。
- `BoneEditManager` に `_modelStores` を列挙する公開 API（例: `EnumerateModelStores()`）を追加。
- 各 GameObject を `ModelProviderHost.GetModels()` の結果と突き合わせて `modelName` / `pluginName` を解決。解決できないもの（破棄済み・提供元不明）はスキップ。

### 3. 復元（ScenePresetManager.FinishApply）

- `ApplyExternals(data)` の直後に `ApplyModelBoneEdits(data)` を追加。
- 処理:
  1. `ModelProviderHost.GetModels()` で現在のモデル一覧を取得
  2. `modelName`（+ `pluginName`）で照合。同名複数は先勝ち
  3. 適用前に当該モデルの既存 `_modelStores` エントリをリセット（プリセット状態で置き換え）
  4. `BoneEditManager.GetModelStore(go)` にエントリを積み、Transform に適用。orig 値は適用時点の Transform から採取（`PartsEditPresetIO.ApplyModel` と同じ要領）
- 照合できない差分は警告ログのみで捨てる（ペンディング保持なし）。

### 4. ModItemExplorer / MTEUtils 側

改修不要。既存の `ModelProviderHost`（GameObject + 表示名のプル型 API）だけで完結する。

### 5. エッジケース

- モデル配置プロバイダの読込トグル OFF: モデル未復元 → 照合失敗 → 警告のみ（妥当な挙動）
- 同名モデル複数: group サフィックスで通常は区別済み。万一衝突したら先勝ち
- モデルには着せ替え（item 変更）が存在しないため、メイドの `DiscardSlotIfItemChanged` 相当は不要

## テスト・検証

- 実機（com3d25-devbridge）で: モデル配置 → ボーン編集 → プリセット保存 → シーンリセット → プリセット読込 → ボーン差分が復元されること
- 旧バージョンのプリセット読込でデグレがないこと
- モデル配置プロバイダ OFF での読込時に警告のみで完走すること
