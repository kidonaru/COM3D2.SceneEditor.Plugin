# モデルの表示レイヤーをタイムラインへ保存する 設計

## 目的

ModItemExplorer（MIE）で配置したモデルは、表示レイヤー（Default / Charactor）を切り替えられる。いまはこれがタイムラインに保存されていない。

- レイヤーの実体は GameObject の `layer` だけ
- 保存先は MIE のモデル配置プリセット（`ModelPlacementPresetItem.layer`）と、それを取り込む SE のシーンプリセットだけ
- タイムラインを読み込み直すと、モデルは MIE の既定レイヤー（設定の `defaultModelLayerType`）で作り直され、切り替えた値が戻る

Inspector の共通表示（`2026-09-25-model-inspector-unify`）で、タイムラインの項目選択時にもレイヤー行を出すようにした。そのため、レイヤーがタイムラインに保存されると誤解されやすくなった。

## 確定した方針（ユーザー決定）

- レイヤーをモデル定義（`<Models>` の各 `<Model>`）としてタイムライン XML に保存する
- キーフレームにはしない（アニメーションしない）

## 値の表し方

- Unity のレイヤー番号（`int`）をそのまま持つ。MIE のプリセットも同じ表し方をしている
- 未指定は `-1`
  - プロバイダがレイヤー API を持たない場合も、要素が無い旧 XML も `-1`
  - `-1` のときは、読込時にレイヤーへ触れない（MIE の既定レイヤーのまま）

## XML

- `TimelineModelXml` に要素 `<ModelLayer>` を足す
  - `[XmlElement("ModelLayer")] public int layer = -1;`
  - `-1` のときは書き出さない（`ShouldSerializelayer`）
  - 旧 SE の XML は変わらない
- 要素名を `Layer` にしないのは、タイムライン XML で `<Layer>` がタイムラインレイヤーの意味で既に使われていて、紛らわしいため
- `TimelineData.CurrentVersion` は上げない
  - 追加の要素なので変換が要らない
  - `XmlSerializer` は未知の要素を読み飛ばすので、旧 SE や MTE で読んでも壊れない

## プロバイダ API（任意メンバ）

```csharp
int  GetModelLayer(GameObject obj);            // モデルのレイヤー番号。管理外・不明なら -1
void SetModelLayer(GameObject obj, int layer); // レイヤーを変える。0〜31 以外は無視する
```

- SE の `ModelPlacerProviderBinder` は、既存の `GetModelAttachBone` と同じく任意メンバとしてバインドする
- 2 つのうち片方しか無い場合は、両方とも無効にする（`BeginBatch` / `EndBatch` と同じ扱い）
- MIE の実装
  - `GetModelLayer`: 自前配置のモデルなら `go.layer`
  - `SetModelLayer`: 既存の `SelfModelPlacer.SetLayer(model, layer)`（Undo 用の直接適用。履歴は積まない）を呼ぶ

## SE のデータの流れ

1. **stat**: `StudioModelStat` に `public int layer = -1` を足す。`FromModel` でもコピーする（複製・マネージャ側のキャッシュへの写しに必要）
2. **プロバイダ → stat**
   - `ExternalModelHack.GetOrCreateStat` は新規・キャッシュ両方の経路で、`SyncAttachFromProvider` の隣に `SyncLayerFromProvider` を呼ぶ
   - `getModelLayer` が無ければ何もしない。例外はログに出して握りつぶす
3. **stat → マネージャ → タイムライン**
   - `StudioModelManager.LateUpdate` の変化判定に `layer` を足す
   - 変わっていれば `FromModel` と `updatedModels` へ追加し、`UpdateTimelineModels` でタイムラインのモデル定義へ反映する
4. **保存**: `TimelineModelData.FromModel` / `ToXml` で `layer` を書く
5. **読込**
   - `TimelineModelData.FromXml` で `layer` を読む
   - `StudioModelManager.SetupModels` は、新規生成・既存流用のどちらでも、`modelData.layer >= 0` なら stat に書く。そのうえで `modelHackManager.UpdateLayer(model)` を呼び、プロバイダへ適用する
   - 適用は、既存の `BeginBatch` / `EndBatch` の区間の中で行う
6. **複製**
   - `TimelineManager.CopyModel` は `FromModel` で `layer` も写す
   - `ExternalModelHack.CreateModel` の最後で、`layer >= 0` ならプロバイダへ適用する

`ModelHackBase` には `virtual void UpdateLayer(StudioModelStat model)` を足す（既定は何もしない）。`ExternalModelHack` は、これをオーバーライドして `setModelLayer` を呼ぶ。`ModelHackManager.UpdateLayer` は `UpdateAttachPoint` と同じく例外を隔離する。

## 変更の記録

- MIE の UI（Inspector のレイヤー行・モデル操作ウィンドウ）で切り替えた値は、次の同期（`StudioModelManager.LateUpdate` の 30 フレーム間隔）でタイムラインのモデル定義へ入る。保存時に XML へ書かれる
- レイヤーはキーではないので、編集モードや自動キー登録とは関係しない

## 互換

| 組み合わせ | 結果 |
|---|---|
| 新 SE + 旧 MIE（API 無し） | レイヤーは `-1` のまま。XML に書かれず、読込でも触らない |
| 旧 SE で新 XML を読む | `<ModelLayer>` を読み飛ばす。レイヤーは MIE の既定 |
| MTE で新 XML を読む | 同上。モデル定義の他の値は影響を受けない |

CLAUDE.md の「タイムライン XML の互換方向」に 1 行足す。

## テスト（SE 単体）

- `TimelineModelData` / `TimelineModelXml`
  - `layer >= 0` は `<ModelLayer>` として往復する
  - `-1` は書き出されない
  - 要素の無い XML は `-1` として読む
- `StudioModelStat.FromModel` が `layer` をコピーする
- `ModelPlacerProviderBinder`
  - `GetModelLayer` / `SetModelLayer` を任意メンバとしてバインドする
  - 無い型では null
  - 片方だけの型では両方 null

実機（devbridge）:
- MIE のモデルを Charactor にして保存し、読み直すと Charactor に戻る
- 旧 XML（要素なし）では既定のまま
- 複製したモデルは元と同じレイヤーになる

## 範囲外

- レイヤーのキーフレーム化
- SE がプロバイダを介さずに持つモデル（現状は無い）のレイヤー
- 背景モデルのレイヤー
