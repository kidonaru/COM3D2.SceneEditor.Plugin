# モデル配置プロバイダ連携

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）のタイムラインが扱うモデルを、
他プラグインの配置機能で賄うための資料。

対象: `ModelPlacerProviderRegistry` 搭載以降の SceneEditor。

## 概要

タイムラインのモデル系レイヤー（モデル / モデルボーン / モデルシェイプキー /
モデルマテリアル）は、モデルの**配置そのものを持たない**。
配置のライフサイクル（生成・削除・表示切替・アタッチ）は **モデル配置プロバイダ** が担い、
タイムラインはプロバイダが列挙する `GameObject` をキーフレームの対象として扱うだけになる。

- 発見は**属性ベースの自動発見**。SceneEditor へのアセンブリ参照は不要
- 境界を越える型は `UnityEngine.GameObject` / `Maid` / プリミティブのみ
- プロバイダが 1 つも見つからない環境では、モデル系レイヤーは登録されず起動時に警告が 1 回出る
- 複数見つかった場合は**先勝ち**で 1 つだけ使う

## 実装手順

### 1. 属性を自前定義する

SceneEditor のアセンブリを参照させないため、レジストリは属性を型の完全一致ではなく
**短名 `ModelPlacerProviderAttribute` の一致**で判定する。各プラグインが自分の
名前空間に同名の属性を定義すればよい。

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ModelPlacerProviderAttribute : Attribute
{
}
```

### 2. 契約メンバを持つ public static クラスに属性を付ける

```csharp
[ModelPlacerProvider]
public static class ModelPlacerProvider
{
    public static string ModelPlacerId => "ModItemExplorer";
    public static string ModelPlacerDisplayName => "モデル配置 (ModItemExplorer)";

    /// <summary>配置中モデルの列挙。タイムラインはこの一覧をキーフレーム対象にする</summary>
    public static List<GameObject> GetModels() { ... }

    /// <summary>モデルの元ファイル名。タイムラインの名前解決に使う</summary>
    public static string GetModelFileName(GameObject obj) { ... }

    /// <summary>モデルを配置する。失敗時は null を返す</summary>
    public static GameObject CreateModel(
        string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) { ... }

    /// <summary>モデル1体を削除する</summary>
    public static void DeleteModel(GameObject obj) { ... }

    /// <summary>配置中モデルを全削除する</summary>
    public static void DeleteAllModels() { ... }

    /// <summary>表示・非表示を切り替える</summary>
    public static void SetModelVisible(GameObject obj, bool visible) { ... }

    /// <summary>メイドのボーンへ追従させる。maid が null か boneName が空なら解除</summary>
    public static void AttachModel(GameObject obj, Maid maid, string boneName) { ... }
}
```

（実物: ModItemExplorer の
`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`）

## 契約仕様

いずれも `public static`。シグネチャは戻り値の型まで一致すること
（一致しないメンバは「無い」ものとして扱われる）。

### 必須メンバ

| メンバ | シグネチャ | 役割 |
|---|---|---|
| `ModelPlacerId` | `string`（プロパティ） | プロバイダ ID。タイムライン XML の `pluginName` として保存される。空なら登録されない |
| `ModelPlacerDisplayName` | `string`（プロパティ） | 表示名。空なら ID を代用する |
| `GetModels` | `List<GameObject> GetModels()` | 配置中モデルの列挙。タイムラインはこの一覧をキーフレーム対象にする |
| `GetModelFileName` | `string GetModelFileName(GameObject)` | モデルの元ファイル名。タイムラインの名前解決に使う |
| `CreateModel` | `GameObject CreateModel(string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible)` | モデルを配置し、生成した `GameObject` を返す。失敗時は null |
| `DeleteModel` | `void DeleteModel(GameObject)` | モデル 1 体を削除する |
| `DeleteAllModels` | `void DeleteAllModels()` | 配置中モデルを全削除する |
| `SetModelVisible` | `void SetModelVisible(GameObject, bool)` | 表示・非表示を切り替える |
| `AttachModel` | `void AttachModel(GameObject, Maid, string boneName)` | メイドのボーンへ追従させる / 解除する |

必須メンバが 1 つでも欠けていると、そのプロバイダは登録されず、
欠落したメンバ名がログに出る。

### 任意メンバ

| メンバ | シグネチャ | 役割 |
|---|---|---|
| `GetModelDisplayName` | `string GetModelDisplayName(GameObject)` | 一覧に出す表示名 |
| `BeginBatch` / `EndBatch` | `void BeginBatch()` / `void EndBatch()` | タイムライン読込のような一括操作の開始・終了通知。**2 つ揃っているときだけ**バインドされる |

## 各メンバの詳細

### `CreateModel` の `type`

SceneEditor 側 `StudioModelType` の enum 名がそのまま文字列で渡る。

| `type` | 内容 | 主に使う引数 |
|---|---|---|
| `Mod` | MOD アイテム（`.menu`） | `fileName` |
| `Prefab` | 公式 BG プレハブ（アセットバンドル / Resources） | `fileName` |
| `Asset` | 背景オブジェクト（`.asset_bg`） | `fileName` |
| `MyRoom` | マイルームカスタムの配置オブジェクト | `myRoomId` |

未知の種別が来たときは黙って落とさず、警告を出して null を返すこと。

### `CreateModel` の `bgObjectId`

公式の背景オブジェクト一覧（`PhotoBGObjectData`）における ID。
SceneEditor が自分のカタログを引くための識別子で、該当が無いモデルでは 0 が渡る。

`fileName` と `myRoomId` だけで生成先を決められるなら**無視してよい**
（ModItemExplorer の実装も使っていない）。

### `group` はヒントでしかない

`CreateModel` に渡る `group` は「呼び出し側の希望値」であり、
ゲスト側は別の値で採番してよい。タイムラインが実際に使う group は
`ModelHackManager` が列挙順で振り直すため、SceneEditor はゲストの採番を読み戻さない。

### `AttachModel` の `boneName`

SceneEditor 側が `AttachPoint` からボーンの `Transform` まで解決し、その**名前だけ**を渡す。
ゲスト側に `AttachPoint` enum の対応表を持たせずに済ませるための取り決め。

- `maid` が null、または `boneName` が空文字なら**解除**（ワールド配置へ戻す）
- 渡るボーン名はゲスト側が用意した定番一覧に無いものも含まれるため、
  一覧に無い名前でもアタッチできるようにしておくこと

### `BeginBatch` / `EndBatch`

タイムライン読込時の一括生成・削除を挟む形で呼ばれる。
ゲスト側が操作履歴（undo/redo）を持つ場合、この間は履歴への登録を止めておくと、
読込のたびに履歴が積み上がるのを避けられる。

`EndBatch` は例外時も必ず呼ばれる（`finally` から呼び出される）。

## 既知の相互作用

- ゲスト側がギズモを持つ場合、タイムライン再生中は毎フレーム Transform が
  上書きされるため、ギズモ操作は再生停止時のみ有効になる
- 旧バージョンの SceneEditor が自前配置していた時代のタイムライン XML は
  `pluginName` に `"SceneEditor"` を持つ。読込時に現行のプロバイダ ID へ自動で読み替えられる
