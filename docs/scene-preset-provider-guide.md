# 外部プラグイン向け シーンプリセット連携ガイド

SceneEditor プラグイン（COM3D2.SceneEditor.Plugin）のシーンプリセットへ、
他プラグインの状態を相乗りさせるための資料。

対象: `ScenePresetProviderRegistry` 搭載以降の SceneEditor。
プリセット形式は `ScenePresetData.CurrentVersion = 14`。

v10 でサイドカーのファイル名を本体 XML へ記録する形に変更し、
プロバイダ契約に任意メンバとして拡張子指定とバイナリ対が加わった。
以降の追加（v11: メイドの `look`（視線）、v12: `gravity`（髪・スカートの重力）、
v13: `motion`（再生中モーション）、v14: `pngPlacement`（PNG 配置））は
いずれもプロバイダ契約に影響しない。

## 概要

シーンプリセットは「カメラ・背景・メイド（配置 / ポーズ / 表情）」を 1 ファイルに
記録する機能。外部プラグインは **プロバイダ** を 1 つ公開することで、
自前の状態を同じプリセットに載せられる。

- 収集は**属性ベースの自動発見**。SceneEditor へのアセンブリ参照は不要
- 受け渡すのは**ペイロード 1 本**だけ。テキスト（既定）でもバイナリでもよく、中身の構造は各プラグインの自由
- ペイロードは本体 XML に埋め込まず、**サイドカーファイル**として別出しされる。拡張子はプロバイダが指定できる

## 実装手順

### 1. 属性を自前定義する

SceneEditor のアセンブリを参照させないため、レジストリは属性を型の完全一致ではなく
**短名 `ScenePresetProviderAttribute` の一致**で判定する。各プラグインが自分の
名前空間に同名の属性を定義すればよい（既に同名の属性を持っているなら流用可）。

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ScenePresetProviderAttribute : Attribute
{
}
```

### 2. 契約メンバを持つ型に属性を付ける

```csharp
[ScenePresetProvider]
public static class ModelPlacementPresetProvider
{
    public static string PresetProviderId => "ModItemExplorer.ModelPlacement";

    public static string PresetProviderDisplayName => "モデル配置 (ModItemExplorer)";

    /// <summary>読込トグルなど狭い場所で使う短縮名（任意）</summary>
    public static string PresetProviderShortDisplayName => "モデル";

    /// <summary>現在状態を XML で返す。保存すべき状態が無い・失敗時は null</summary>
    public static string CapturePresetXml()
    {
        return SelfModelPlacer.instance.GetPlacementXml();
    }

    /// <summary>XML を現在のシーンへ反映する。成功可否を返す</summary>
    public static bool ApplyPresetXml(string xml)
    {
        return SelfModelPlacer.instance.ApplyPlacementXml(xml);
    }
}
```

（実物: ModItemExplorer の
`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPresetProvider.cs`）

## 契約仕様

すべて **public static**。1 つでも欠けるとエラーログを出してそのプロバイダだけ無効化される。

| メンバ | 型 | 必須 | 役割 |
|---|---|---|---|
| `PresetProviderId` | `string`（プロパティ） | 必須 | プロバイダ識別子。プリセット XML とサイドカーのファイル名に使われる |
| `PresetProviderDisplayName` | `string`（プロパティ） | 必須 | 保存ポップアップのチェックボックス表示名。空なら id が使われる |
| `PresetProviderShortDisplayName` | `string`（プロパティ） | 任意 | プリセットウィンドウの読込トグルに出す短縮名（例: `"モデル"`）。未定義・空なら `PresetProviderDisplayName` が使われる |
| `PresetProviderFileExtension` | `string`（プロパティ） | 任意 | サイドカーの拡張子。未定義なら `xml`。`.anm` と `anm` のどちらの書き方でもよい |
| `CapturePresetXml()` | `string` を返す引数なしメソッド | テキスト対 | 現在状態をテキストで返す |
| `ApplyPresetXml(string)` | `bool` を返す `string` 1 引数メソッド | テキスト対 | テキストを適用し、成功可否を返す |
| `CapturePresetBinary()` | `byte[]` を返す引数なしメソッド | バイナリ対 | 現在状態をバイナリで返す |
| `ApplyPresetBinary(byte[])` | `bool` を返す `byte[]` 1 引数メソッド | バイナリ対 | バイナリを適用し、成功可否を返す |
| `ApplySceneCaptureXml(string)` | `bool` を返す `string` 1 引数メソッド | 任意 | SceneCapture プリセット XML（`<Preset>` 全体）を適用し、成功可否を返す。詳細は [scenecapture-import-guide.md](scenecapture-import-guide.md) |

`ApplySceneCaptureXml` は**単独ではプロバイダとして登録されない**。
上記の必須メンバ（id / displayName + テキスト対かバイナリ対）が揃っていることが前提で、
シグネチャが合わないものは契約不備とはせず単に無視される。

**テキスト対**（`CapturePresetXml` / `ApplyPresetXml`）と**バイナリ対**
（`CapturePresetBinary` / `ApplyPresetBinary`）は、どちらか一方が揃っていればよい。
対の片方だけしか無い場合は契約不備として無効化される。両方揃っている場合は
バイナリ対が使われる。

いずれも**プロパティ**と**メソッド**の別に注意（id / displayName はフィールドでは
バインドされない）。バインドは `Delegate.CreateDelegate` で行われるため、
シグネチャは完全一致している必要がある。

### PresetProviderId の制約

id はサイドカーのファイル名（`<プリセット名>.<id>.xml`）になるため、以下は登録時に弾かれる。

- 空文字列
- `Path.GetInvalidFileNameChars()` に含まれる文字（`/` `\` `:` `*` 等）
- `..` を含むもの（パストラバーサル対策。プリセット XML はユーザー間で受け渡されうるため、
  読み込み側でも再検証される）

ドットは使用可能で、`Plugin名.機能名` の形が推奨（例: `ModItemExplorer.ModelPlacement`）。
発見済みプロバイダ間で id が重複した場合は**先勝ち**で、後続はエラーログを出して登録されない。

### PresetProviderFileExtension の制約

拡張子もファイル名の一部になるため、以下は登録時に弾かれる。

- 空文字列（先頭ドットを除いた結果が空になるものを含む）
- `Path.GetInvalidFileNameChars()` に含まれる文字
- ドットを含むもの（多重拡張子・`..` によるパストラバーサル対策）

### 戻り値の意味

| 状況 | 返すもの | SceneEditor の挙動 |
|---|---|---|
| 保存する状態がある | テキスト / バイナリ | サイドカーに書き出し、本体へ `<external id="..." file="..." />` を追加 |
| 保存する状態が無い | `null` または空 | その external を記録しない（サイドカーも作らない） |
| 適用成功 | `true` | 何もしない |
| 適用失敗 | `false` | 警告ログのみ。他カテゴリの復元は続行 |

例外を投げてもプロバイダ単位で catch され、他カテゴリの保存 / 復元は続行する。
ただしログが出るだけでユーザーには伝わらないため、想定内の失敗は `null` / `false` で返すのが望ましい。

## ファイル構成

保存先は `Config\SceneEditor\ScenePreset`（`PluginUtils.PluginDataPath` 配下）。サブフォルダを作って分類でき、
ウィンドウのタイルビューから辿れる。保存はウィンドウで表示中のフォルダに対して行われ、
サイドカーとサムネは常に本体 XML と同じフォルダに置かれる。

```
ScenePreset/
  MyPreset.xml                                 ← プリセット本体
  MyPreset.png                                 ← サムネイル (240x135)
  MyPreset.pose0.anm                           ← サイドカー (1 人目のメイドのポーズ)
  MyPreset.pose1.anm                           ← サイドカー (2 人目のメイドのポーズ)
  MyPreset.ModItemExplorer.ModelPlacement.xml  ← サイドカー (プロバイダのペイロード)
  お気に入り/                                   ← サブフォルダ（同じ構成で入れ子にできる）
    AnotherPreset.xml
    AnotherPreset.png
```

本体 XML には id とサイドカーのファイル名だけが残る。

```xml
<ScenePresetData version="14">
  <camera>...</camera>
  <background bgId="...">...</background>
  <light>...</light>
  <pngPlacement>...</pngPlacement>
  <maid guid="..." poseAnmFile="MyPreset.pose0.anm">...</maid>
  <external id="ModItemExplorer.ModelPlacement"
            file="MyPreset.ModItemExplorer.ModelPlacement.xml" />
</ScenePresetData>
```

サイドカーのファイル名は保存時に
`<プリセット名>.<プロバイダid>.<拡張子>`（ポーズは `<プリセット名>.pose<索引>.anm`）で
決まり、本体 XML に記録される。読み込み・削除は記録された名前を使うため、
プラグイン未導入の環境でもサイドカーが取り残されない。

サイドカーは**プロバイダが返したペイロードがそのまま**書き込まれる（再シリアライズしない）。
そのためプラグイン自身のプリセットファイルと**ファイル単位で相互流用できる**。
これを狙うなら、capture / apply はプラグイン既存のプリセット入出力と同じフォーマット・
同じ拡張子を使うとよい。

サイドカー判別は「拡張子以外にドットを含むファイル名」で行うため、プリセット名側は
ドット禁止（`ValidatePresetName`）になっている。

## ライフサイクル

### 発見

`AppDomain.CurrentDomain.GetAssemblies()` の全型走査。初回参照時にキャッシュされ、
**保存ポップアップを開くたびに `Refresh()`** が呼ばれる。`Refresh()` はロード済み
アセンブリ数が前回から増えていなければキャッシュを維持するため、遅延ロードされた
プラグインも次にポップアップを開けば拾われる。

つまりプロバイダ側は**登録処理を書く必要がなく**、初期化タイミングを合わせる必要もない。
ただし `PresetProviderId` / `PresetProviderDisplayName` は発見時（≒ポップアップを開いた時）に
評価されるため、**重い初期化やシーン依存の処理を getter に書かない**こと。

### 保存

1. ユーザーが保存ポップアップでカテゴリを選ぶ（発見されたプロバイダが 1 行ずつ並ぶ）
2. チェックされたプロバイダの `CapturePresetXml()`（バイナリ対なら `CapturePresetBinary()`）を呼ぶ
3. 中身のある分をサイドカーへ書き出し、ファイル名を記録した本体 XML を書く
4. 上書き保存時、旧プリセットにだけ存在したサイドカーを削除する

チェック状態は Config（`scenePresetDisabledProviders`、無効化 id のカンマ区切り）に
永続化され、次回のデフォルトになる。**未知のプロバイダはデフォルト有効**。

### 適用

ロード時に選択 UI は無く、**プリセットに入っているものは全部適用される**。
順序は カメラ → 背景 → ライト → PNG 配置 → メイド → （視線 → externals）。

- サイドカーが見つからない、またはファイル名が不正な場合は警告ログを出してスキップ（適用メソッドは呼ばれない）
- id に一致するプロバイダが未登録（プラグイン未導入等）の場合も警告ログのみで続行

**externals は全メイドのロード完了後に適用される**。新規呼出したメイドの適用は
ロード待ちで遅延するため、その間 externals は保留され、保留が捌けた時点で
視線（`look`）と一緒にまとめて反映される（`FinishApply`）。
つまり外部プロバイダの `Apply*` が呼ばれる時点でメイドは揃っており、
メイドの状態に依存する復元を書いてよい。

適用中は `ScenePresetManager.isLoading` が true になる。別のプリセットを
続けて適用すると保留は破棄され、前のプリセットの externals は適用されない。

#### SceneCapture プリセットの適用

一覧には SceneCapture プラグインのプリセット（`Config\SceneCapture\Presets\*.xml`）も
読み込み専用の仮想フォルダとして並ぶ。これを読み込んだ場合の経路は上記とは別で、

1. カメラ・背景・ライトは SceneEditor 本体が変換して適用する（メイドには触らない）
2. `<Models>` / `<Effects>` のどちらかに中身があれば、`ApplySceneCaptureXml` を
   実装している**全プロバイダ**へ `<Preset>` XML 全体をそのまま渡す

サイドカーも `ApplyPresetXml` も介さず、メイドのロード待ちも挟まない同期実行になる。
実装者向けの詳細は [scenecapture-import-guide.md](scenecapture-import-guide.md) を参照。

### 削除

本体 XML に記録された全サイドカー（ポーズ anm を含む）も削除する。

## 注意点

- **UI 側の要件はない**。プラグイン未導入なら行が出ないだけで、プリセット自体は壊れない
- **旧形式は読めない**: v9 以前のプリセットはサイドカーのファイル名を持たないため、
  externals とメイドのポーズが復元されない（v10 で互換を切った）。
  旧形式のサイドカー（`<プリセット名>.<プロバイダid>.xml`）は本体を上書き保存・削除しても
  追跡されずに残るため、不要なら手動で削除すること
- `CapturePresetXml()` は**ポップアップの「保存」押下時に 1 回**だけ呼ばれる。
  毎フレーム呼ばれる想定ではないが、UI をブロックするため長時間かかる処理は避ける
- 適用は同期実行される。非同期ロードを伴う復元は、プロバイダ側で保留キューを持つ設計にすること
- **プリセット適用時にホストの操作履歴は全クリアされる**（外部プラグインが
  [HistoryAPI](history-guest-guide.md) へ積んだエントリも同時に破棄される）。
  クリアは適用の開始時点なので、その後に走る externals 適用の中で `Register` すると
  エントリは残ってしまう。復元は履歴に積まない（undo 対象にしない）のが望ましい

## 動作確認

BepInEx のログに以下が出れば発見されている。

```
SceneEditor: プリセットプロバイダを発見しました: ModItemExplorer.ModelPlacement (COM3D2.ModItemExplorer.Plugin.ModelPlacementPresetProvider)
```

契約不備の場合は代わりにエラーが出る（いずれも `SceneEditor: ` プレフィックス付き）。

```
プリセットプロバイダの契約メンバが不足しています: <型名>
プリセットプロバイダの ID が空です: <型名>
プリセットプロバイダの ID に使用できない文字が含まれています: <id>
プリセットプロバイダの ID が重複しています: <id> (<型名>)
プリセットプロバイダの拡張子に使用できない文字が含まれています: <型名>
プリセットプロバイダのバインドに失敗しました: <型名>
```

## 参照

- 本体実装: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetProviderRegistry.cs`
- 保存 / 適用フロー: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
- データ構造: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- 保存ポップアップ: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs`
- 他の連携ガイド: [docking-guest-guide.md](docking-guest-guide.md) /
  [history-guest-guide.md](history-guest-guide.md)
