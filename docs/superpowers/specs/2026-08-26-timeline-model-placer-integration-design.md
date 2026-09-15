# タイムラインのモデル管理を ModItemExplorer へ一本化する設計

作成日: 2026-08-26
対象リポジトリ: `COM3D2.SceneEditor.Plugin`（以下 SE） / `COM3D2.ModItemExplorer.Plugin`（以下 MIE）

## 背景

SE のタイムライン（MTE 移植分）は `SceneEditorHack`（`pluginName = "SceneEditor"`）が
アセットバンドル・プレハブ・マイルーム・MOD menu を自前でロードし、`_modelList` に保持している。
一方 MIE は `SelfModelPlacer` が独自にモデルを配置し、SE へは `ModelProviderHost`
（読み取り専用の GameObject 一覧提供）でしか繋がっていない。

結果としてモデルの配置管理が 2 系統に分かれ、以下が起きている。

- 同じモデルを SE タイムラインと MIE の両方から配置でき、一覧も別々に見える
- MIE のギズモ / インスペクタ / 配置プリセットは MIE 配置分にしか効かない
- ロード処理（menu 解析・アセットバンドル・マイルーム）が両プラグインに重複している

## 目的

タイムラインのモデル管理を **MIE 側へ一本化**し、
「配置のライフサイクル（生成・削除・表示・アタッチ）は MIE が持ち、
SE タイムラインはそれをキーフレーム対象として扱う」構造にする。

### 非目的

- MTE 本体（`COM3D2.MotionTimelineEditor.Plugin`）との連携仕様の変更。
  MIE 側の既存 `MTEHack`（MTE 本体を `Assembly.LoadFile` で叩く経路）はそのまま残す
- 既存 `ModelProviderHost`（ボーン編集 / マテリアル編集の対象列挙）の置き換え。
  役割を「編集対象の列挙」と「配置のライフサイクル」に分けて併存させる
- 背景モデル（`BGModelTimelineLayer` 系、`BgMgr.BgObject` 配下）の管理方式変更

## 決定事項（ユーザー判断）

| 論点 | 決定 |
|---|---|
| 主従 | MIE を配置の本体にする。SE タイムラインは生成・削除も MIE へ委譲する |
| タイムライン読込時 | 不足モデルは MIE へ自動配置を依頼する（MTE と同じ振る舞い） |
| 既存 `SceneEditorHack` のモデル経路 | MIE へ一本化し、削除する |
| MIE 未導入時 | MIE 必須とし、モデル系レイヤーを無効化する |
| 公式 BG / マイルームの生成 | MIE 側へ生成経路を移す（MIE 単体でも配置可能になる） |
| MIE の配置プリセット | タイムライン生成分も含める。適用時は既存配置を総入れ替えする |

## アーキテクチャ

```
SE                                        MIE
┌──────────────────────────┐              ┌────────────────────────────┐
│ StudioModelManager       │              │ [ModelPlacerProvider]      │
│   └ ModelHackManager     │              │ ModelPlacerProvider        │
│       └ ExternalModelHack│──delegate──▶ │   └ SelfModelPlacer        │
│           (IModelHack)   │              │       ├ menu / .asset_bg   │
│ ModelPlacerProviderRegistry             │       ├ 公式BG（新規）      │
│   （属性短名で発見）      │              │       └ マイルーム（新規）  │
└──────────────────────────┘              └────────────────────────────┘
```

既存の `ScenePresetProviderRegistry` と同じ「属性の短名一致でゲストの public static クラスを
発見し、`Delegate.CreateDelegate` で束ねる」規約に乗せる。
両プラグインは互いのアセンブリを参照しない。

## 規約仕様: `ModelPlacerProvider`

ゲスト側は `ModelPlacerProviderAttribute` を**自前定義**し（短名一致で判定するため型の同一性は不要）、
public static クラスに付与する。SE は起動時とシーン切り替え時にロード済みアセンブリを走査して発見する。

### 必須メンバ（すべて public static）

| メンバ | 意味 |
|---|---|
| `string ModelPlacerId` | プロバイダ ID。そのままタイムラインの `pluginName` になる（MIE は `"ModItemExplorer"`） |
| `string ModelPlacerDisplayName` | UI 表示名 |
| `List<GameObject> GetModels()` | 現在配置中のモデルのルート GameObject を列挙する。SE は保持せず毎回呼ぶ |
| `string GetModelFileName(GameObject obj)` | そのモデルの生成元ファイル名（`.menu` 名 / アセットバンドル名 / `MYR_<id>`）。SE の `name` 生成と `OfficialObjectInfo` 逆引きに使う |
| `GameObject CreateModel(string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible)` | モデルを生成して返す。失敗時は null。`type` は `StudioModelType` の enum 名文字列（`Mod` / `Prefab` / `Asset` / `MyRoom`）。`group` は希望値であり、プロバイダが別の値を採ってもよい |
| `void DeleteModel(GameObject obj)` | 指定モデルを破棄する |
| `void DeleteAllModels()` | このプロバイダが配置した分をすべて破棄する |
| `void SetModelVisible(GameObject obj, bool visible)` | 表示 / 非表示を切り替える |
| `void AttachModel(GameObject obj, Maid maid, string boneName)` | メイドのボーンへ追従させる。`boneName` は SE 側が `PhotoTransTargetObject.AttachPoint` から `MaidCache.GetAttachPointTransform` でボーン Transform まで解決した結果の名前。ゲスト側に enum の対応表は不要。`maid == null` または空文字で解除 |

### 任意メンバ

| メンバ | 未実装時の代替 |
|---|---|
| `string GetModelDisplayName(GameObject obj)` | SE 側で `OfficialObjectInfo.label` から生成 |
| `void BeginBatch()` / `void EndBatch()` | 何もしない。タイムライン読込時の一括生成でプロバイダ側の履歴登録・UI 更新を抑止するために使う |

### 契約

- やり取りする型は `GameObject` / `Maid` / プリミティブのみ。両プラグインの独自型は境界を越えない
- `CreateModel` に渡す `group` はゲスト側への**ヒント**でしかなく、ゲストが別の値で採番してよい。
  タイムラインが使う group は既存の `ModelHackManager.FixGroup` が列挙順で振り直すため、
  SE はゲストの採番を読み戻さない。`StudioModelStat.name`（`info.fileName + " (group)"`）は
  この正規化後の group で確定する
- SE 側はプロバイダのデリゲート呼び出しを try/catch で囲み、例外でタイムライン全体を巻き込まない
- SE 側は重複排除しない。同一 GameObject を複数経路で提供しないのはプロバイダの責務

## SE 側の変更

### 追加

- `Manager/ModelPlacerProviderRegistry.cs`
  - `ScenePresetProviderRegistry` と同じ走査 / 束縛ロジック。発見結果は `ModelPlacerProvider` 型（デリゲート束）で保持
  - `Refresh()` はプラグイン初期化時とシーン切り替え時に呼ぶ
- `Timeline/Hack/ExternalModelHack.cs`
  - `ModelHackBase` 派生。`pluginName` はプロバイダの `ModelPlacerId`
  - `modelList`: `GetModels()` を毎回列挙し、`Dictionary<GameObject, StudioModelStat>` のキャッシュで
    同一 GameObject に対する `StudioModelStat` の同一性を保つ（`ProviderModelStat` と同じ考え方）。
    Destroy 済みキーの掃除も同様に行う
  - `CreateModel` / `DeleteModel` / `DeleteAllModels` / `SetModelVisible` / `UpdateAttachPoint` を
    プロバイダへ委譲する。`UpdateAttachPoint` は `attachMaidSlotNo` から `MaidCache` を引き、
    `GetAttachPointTransform` で解決したボーン名を `AttachModel` へ渡す
  - `StudioModelStat` のキャッシュ判定に group は使わない。group は `ModelHackManager.FixGroup` が
    列挙順で振り直すため、ゲストの採番と突き合わせると毎回作り直しになる

### 変更

- `Timeline/Hack/SceneEditorHack.cs`
  - `_modelList` / `CreateModel` / `DeleteModel` / `DeleteAllModels` / `UpdateAttachPoint` /
    `AttachItem` / `LoadGameModel` / `LoadMyRoomObject` / `LoadModObject` / `GetModelParent` を
    削除する（MIE へ移設）
  - `modelList` の override と `IModelHack` 実装は**残す**。`StudioHackBase : IModelHack` で
    `modelList` が abstract のため外せない。空リストを返す実装にする
  - `PhotoBGObjectData.Create()` の明示ロードは `StudioModelManager` の
    `OfficialObjectLabelMap` / `BGObjectIdMap` が依存しているため**残す**
- `Timeline/TimelineIntegration.cs`（登録箇所）
  - `ModelHackManager.Register(new ExternalModelHack(provider))` をプロバイダ発見時に行う
  - プロバイダ未発見時はモデル系レイヤー
    （`ModelTimelineLayer` / `ModelBoneTimelineLayer` / `ModelShapeKeyTimelineLayer` /
    `ModelMaterialTimelineLayer`）を登録せず、
    起動時に一度だけ「ModItemExplorer が必要」という警告を出す
- `Timeline/Manager/StudioModelManager.cs`
  - `SetupModels` の入口で、`modelData.pluginName` が `"SceneEditor"` の場合はプロバイダ ID へ読み替える
    （旧タイムライン XML の互換。読み替えは読み込み時のみで、保存は新しい ID で行う）
  - `SetupModels` の生成ループを `BeginBatch` / `EndBatch` で挟む

### 影響を確認する箇所

- `ScenePresetManager` / `MaterialEditWindow` / `BoneEditWindow` / `ModelSelectHost` は
  `ModelProviderHost` 経由で MIE のモデルを見ている。MIE が両方の窓口（ProviderHost と
  ModelPlacerProvider）で同じ GameObject を出すため、**タイムラインのモデル一覧と
  ボーン / マテリアル編集の一覧に同じモデルが並ぶ**のは期待どおり
- `TimelineIntegration.RegisterModelProvider()` はタイムラインのモデルを
  `"SceneEditor.Timeline"` として `ModelProviderHost` へ提供している。移行後は同じ GameObject を
  MIE 自身も提供するため二重に並ぶ。**この登録は削除する**

## MIE 側の変更

### 追加

- `ModelPlacement/ModelPlacerProvider.cs`
  - `ModelPlacerProviderAttribute` の自前定義（`ModelPlacementPresetProvider.cs` の
    `ScenePresetProviderAttribute` と同じ流儀）
  - `[ModelPlacerProvider] public static class ModelPlacerProvider`。実体は `SelfModelPlacer` へ委譲する。
    `ModelPlacerId` は `SelfModelPlacer.PluginName`（`"ModItemExplorer"`）
- `SelfModelPlacer` に生成経路を追加
  - `CreateGameModel(string assetName, int group, bool visible)`:
    `GameMain.Instance.BgMgr.CreateAssetBundle` → `Resources.Load<GameObject>("Prefab/"+name)`
    → `Resources.Load<GameObject>("BG/"+name)` → menu 経路フォールバック（SE の `LoadGameModel` を移植）
  - `CreateMyRoomObject(int myRoomId, int group, bool visible)`（SE の `LoadMyRoomObject` を移植）
  - いずれも既存 `RegisterCreatedModel` に合流させ、ラッパー GameObject / ギズモ / 履歴の扱いを揃える
- `SelfModelPlacer` にボーン名アタッチを追加
  - `AttachByBoneName(model, maid, boneName)`。既存 `Attach` は `AttachPoints` に載っている
    ポイントしか受け取れないため、一覧に無いボーンは臨時のアタッチポイントを作って委譲する
  - `RestoreAttachState` も同様に臨時ポイントへフォールバックさせ、タイムライン経由で
    アタッチしたモデルを配置プリセットから復元できるようにする
- バッチ抑止
  - `BeginBatch` / `EndBatch` の間は `history.RegisterCreate` / `history.RegisterAttach` と
    `selectedModel` の更新を行わない。タイムライン読込のたびに MIE の Undo 履歴が
    大量に積まれるのを防ぐ

### 変更

- `ModelPlacementPreset` / `GetPlacementXml` / `ApplyPlacementXml`
  - 生成種別（`type` / `myRoomId` / `bgObjectId`）を保存できるようスキーマを拡張する。
    要素が無い旧 XML は `type="Mod"` として読む（後方互換）
- `ModelPlacerManager`
  - 変更なし。SE 連携は `ModelPlacerProvider` が `SelfModelPlacer` を直接見るため、
    MTE 連携（`ModelHackManagerWrapper`）とは独立に動く

## 移行と互換

- 旧タイムライン XML の `pluginName="SceneEditor"` は読み込み時に `"ModItemExplorer"` へ読み替える。
  モデル名（`info.fileName + " (group)"`）の体系は変えないため、同名モデルはそのまま紐付く
- MIE の旧配置プリセット XML は `type` 要素が無いため `Mod` として読む
- MIE 未導入で SE のみの環境では、タイムラインのモデル系レイヤーが使えなくなる。
  これは意図した非互換であり、CHANGELOG と `docs-site/guide/timeline.md` に明記する

## リスクと対処

| リスク | 対処 |
|---|---|
| MIE のギズモ操作とタイムライン再生が同じ Transform を奪い合う | MIE はラッパー GameObject にギズモを付け、タイムラインも同じラッパーを動かす。再生中はタイムラインが毎フレーム上書きするため、ギズモ操作は再生停止時のみ有効という扱いにする（挙動を guest guide に明記） |
| ゲストの group 採番と `ModelHackManager.FixGroup` の再採番が食い違い、`StudioModelStat` が作り直され続ける（= Bone/Material コントローラの再生成ループ） | `ExternalModelHack` のキャッシュ判定に group を使わず、`fileName` の変化だけで作り直す。group は `FixGroup` に委ねる |
| 配置プリセット適用でモデルが総入れ替えされ、タイムラインの参照が切れる | `StudioModelManager.LateUpdate` の差分検出（`onModelAdded` / `onModelRemoved`）が既にあり、名前一致で再バインドされる。適用直後に `LateUpdate(true)` を強制する |
| タイムライン読込のたびに MIE の Undo 履歴が汚れる | `BeginBatch` / `EndBatch` で履歴登録を抑止する |
| プロバイダ発見の失敗（MIE の更新漏れ、規約メンバの欠落） | 束縛失敗時はどのメンバが欠けているかを警告ログに出す。`ScenePresetProviderRegistry` と同じ流儀 |
| 公式 BG / マイルームのロードを MIE へ移植した際の挙動差 | 移植はロジックを変えない逐語移植とし、実機で SE 単体時代と同じモデルが出ることを確認する |

## テスト

SE 側 xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`、`dotnet test` で実行）:

- `ModelPlacerProviderRegistry`
  - 必須メンバをすべて備えたダミー型が発見・束縛されること
  - 必須メンバが 1 つ欠けた型は発見されず、欠落メンバ名が警告に出ること
  - 任意メンバ未実装でも束縛が成功すること
- `pluginName` 読み替え
  - `pluginName="SceneEditor"` の `TimelineModelData` がプロバイダ ID へ読み替えられること
  - 保存時は新しい ID で書き出されること
- 名前解決
  - 希望 group と異なる group をプロバイダが返した場合に、`StudioModelStat.name` が
    実際の group で確定すること

Unity / ゲーム本体に依存する部分（実ロード、アタッチ、プリセット往復）はユニットテスト対象外とし、
次回ゲーム起動時に実機で確認する。

### 実機確認チェックリスト

1. MIE の一覧からモデルを配置 → タイムラインのモデルレイヤーに現れる
2. タイムラインでモデルを追加 → MIE の一覧に現れる
3. タイムライン XML を読み込み → 不足モデルが MIE 側に自動配置される / 余剰が削除される
4. 公式 BG プレハブ・マイルームオブジェクトが MIE 経由で配置できる
5. アタッチポイント指定でメイドのボーンへ追従する
6. MIE の配置プリセット保存 → 適用でモデルが総入れ替えされ、タイムラインが再バインドする
7. MIE 未導入の環境でモデル系レイヤーが無効化され、警告が 1 回だけ出る

## ドキュメント

- `docs-site/dev/model-placer-guest-guide.md` を新規作成（既存の `*-guest-guide.md` に倣う）
- `docs-site/dev/index.md` にリンクを追加
- `docs-site/guide/timeline.md` に MIE 必須である旨を追記
- 両リポジトリの `CHANGELOG.md` に非互換変更として記載

## 実装順序

1. SE: `ModelPlacerProviderRegistry` + ダミープロバイダによるユニットテスト
2. MIE: `ModelPlacerProvider` 規約クラス（既存 `SelfModelPlacer` の範囲＝menu / `.asset_bg` のみ）
3. SE: `ExternalModelHack` + `ModelHackManager` 登録、`SceneEditorHack` のモデル経路削除
4. SE: `pluginName` 読み替えとモデル系レイヤーの条件登録
5. MIE: 公式 BG / マイルームの生成経路移植、アタッチ変換、`BeginBatch` / `EndBatch`
6. MIE: 配置プリセットのスキーマ拡張
7. ドキュメントと CHANGELOG
8. 実機確認（次回ゲーム起動時）
