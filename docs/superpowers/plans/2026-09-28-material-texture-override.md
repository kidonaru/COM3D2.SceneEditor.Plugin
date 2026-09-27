# マテリアルのテクスチャ差し替え (toon・テクスチャ・影の濃さ) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** マテリアルウィンドウで、シェーダーに加えてテクスチャ (`_MainTex` / `_ShadowTex` / `_ToonRamp` / `_ShadowRateToon` / `_OutlineToonRamp`) を専用フォルダのファイルへ差し替えられるようにする。影の濃さ・影の ON/OFF は同梱のサンプル toon テクスチャで表す。

**Architecture:**
- シェーダー変更 (`docs/superpowers/plans/2026-09-27-material-shader-change.md`) の仕組みを広げる。`ModelMaterial` が差し替え前のテクスチャと読み込んだテクスチャ (所有) を `ModelMaterialTextures` に持ち、変更済みレジストリの条件を「シェーダーを変えた」から「シェーダーかテクスチャを変えた」へ広げる
- タイムラインは既存の `<MaterialShaders><MaterialShader>` に任意の子要素 `<Texture prop=".." file=".."/>` を足し、`MaterialShaderManager` が同じ片方向同期・保留・再試行で扱う。テクスチャだけのエントリは `<Shader>` を書かない (旧版はシェーダーが空のエントリを読まない)
- パスの検証・並べ替え・`.tex` の解析は純関数 (`MaterialTextureCatalog` / `MaterialTextureOverride` / `TexFile`) に集め、テストで固定する。ファイル読み込みと Unity オブジェクトの生成・破棄は `MaterialTextureFiles` と `ModelMaterialTextures` に閉じる

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), XmlSerializer, xunit (net48), Node (サンプル toon の生成 `assets/toon/generate.js`)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「7・13. マテリアルのテクスチャ差し替え」と、本計画の「仕様」節 (仕様の詳細化)

## 仕様

### 背景 (実機で確認済み、COM3D2.5)

- メイドの toon 系テクスチャは `253x14` 前後 (`toonSkin` / `toonSkin_shadow` / `toonDress_shadow` / `toonPurpleA1` 等、一部 `254x14` / `256x14`)、`ARGB32`、ミップマップなし、`filterMode = Bilinear`。`wrapMode` は大半が `Clamp` だが、`toonskin_SKB.tex` のように `Repeat` のものもある
- `_ShadowRateToon` はグレースケールで、左 (暗部) が黒、右 (明部) が白。肌 (`ToonSkin_Shadow`) は u ≈ 0.33 まで 10 以下、u ≈ 0.42 で 121、u ≈ 0.5 以降 255。値が小さいほど影テクスチャ・影色が強く出る。全面白なら影は出ない
- `_ShadowTex` / `_MainTex` は `Clamp`、ミップマップなし
- ゲームの `TMorph` は泡スキン (`AwaSkin`) のマテリアルを作るときだけ、体の `_ToonRamp` / `_ShadowRateToon` を 1 回写す (`Assembly-CSharp/TMorph.cs:226-229`)。顔のマテリアルは写さない。泡スキンへの追従は今回扱わない
- `ImportCM.CreateTexture` / `MTEUtils.TextureLoader` は `GameUty.FileSystem` (arc) 経由でしか読めず、任意パスの `.tex` には使えない。ゲームの `TextureResource(width, height, format, uvRects, data).CreateTexture2D()` は 2.0 / 2.5 の両方にあり、`.tex` の中身さえ取り出せば Texture2D を作れる
- `TBody.ChangeTex` は 2.0 / 2.5 で引数が違うので使わない。`material.SetTexture` を直接使う

### 対象と参照フォルダ

| 行の並び | プロパティ | 表示名 | 参照フォルダ |
|---|---|---|---|
| 1 | `_MainTex` | テクスチャ | `UnityInjector\Config\SceneEditor\Texture\` |
| 2 | `_ShadowTex` | 影テクスチャ | 同上 |
| 3 | `_ToonRamp` | トゥーン | `UnityInjector\Config\SceneEditor\Toon\` |
| 4 | `_ShadowRateToon` | 影の濃さ | 同上 |
| 5 | `_OutlineToonRamp` | 輪郭トゥーン | 同上 |

- 行は現在のシェーダーがそのプロパティを持つ (`material.HasProperty`) ときだけ出す。シェーダー行の直下、区切り線の上に並べる
- 選べるのは参照フォルダ内 (サブフォルダ含む) の `.png` / `.tex` (拡張子は大文字小文字を区別しない)。並びは自然順。先頭は `(元)` で、選ぶと差し替え前へ戻す
- 保存するのは `PluginUtils.PluginDataPath` (`Config\SceneEditor`) からの相対パスを `/` 区切りにしたもの (例: `Toon/0_影なし.png`)。保存・読込のどちらでも、絶対パス・`..`・`:` を含むもの、拡張子違い、プロパティの参照フォルダ外を指すものは捨てる (ほかの環境から渡された XML でフォルダ外のファイルを読まないため)
- 参照フォルダが無ければ、一覧を作るときに作る (ユーザーがどこへ置けばよいか分かるように)。一覧は初回表示時に作り、各行の `更新` で作り直す
- ゲームの arc・Mod 内のテクスチャは選択肢に出さない
- ファイルが見つからない指定は、行に `ファイル名 (見つかりません)` と出し、指定だけ保持する (マテリアルは元のテクスチャのまま)。警告ログは同じファイルにつき 1 回

### 読み込み

- `.png`: `new Texture2D(2, 2, TextureFormat.ARGB32, false)` + `LoadImage` (ミップマップなし。ゲームの toon と揃える)
- `.tex`: ヘッダー `CM3D2_TEX`、バージョン 1000〜1011 を自前で解析し、`TextureResource.CreateTexture2D()` で作る。1000 は PNG の IHDR から幅・高さを読む (ゲームと同じ)
- toon 系 3 プロパティは `wrapMode = Clamp` を強制する (元が `Repeat` でも。bilinear で両端が回り込むと、影の境目の反対側に色が滲むため)。テクスチャ系は元テクスチャの `wrapMode` を引き継ぐ (元が無ければ `Repeat`)。`filterMode` は元を引き継ぐ
- 読み込んだテクスチャの `name` は相対パス

### 差し替えと破棄

- 差し替え前のテクスチャを、プロパティごとに最初に差し替えた時点で控える。`(元)` と `初期化` でそこへ戻す
- 読み込んだテクスチャは差し替えたマテリアルが所有し、別ファイルへの差し替え・元に戻す・シーン遷移・マテリアルの作り直し (`ModelMaterial.Init`) ・一覧からの除去 (`ModelMaterial.Release`) で `Object.Destroy` する。同じファイルが指定済みで読み込めていれば何もしない (Undo/Redo の全再構築や同期で読み直さない)
- ファイルはマテリアルごとに読む (共有キャッシュは持たない。toon は数 KB で、同じ大きなテクスチャを多数のマテリアルへ貼る使い方は想定しない)
- ゲーム側がテクスチャを差し替えたら (パーツ色の変更で `_MainTex` が作り直される等)、その差し替えは外し、読み込んだテクスチャを破棄する (シェーダーの `AdoptExternalShader` と同じ考え方)
- シェーダーを変えると、差し替えたテクスチャを新シェーダーにも貼り直す (Unity は同名プロパティのテクスチャを引き継ぐが、差し替え時にプロパティが無かった場合に備える)
- シーン遷移では全マテリアルの差し替えを元へ戻して破棄する。タイムラインを開いていれば、遷移後の `OnLoad` が保留として再適用する
- 値の初期化 (`ModelMaterial.Reset`、タイムラインのキー再生・追跡チェック OFF) はテクスチャを戻さない。`初期化` ボタンは戻す
- テクスチャ差し替えは変更追跡 (チェック) の対象にしない (キーではないため)

### 保存

- タイムライン: `<MaterialShaders>` の各 `<MaterialShader>` に任意の子要素 `<Texture prop="_ToonRamp" file="Toon/0_影なし.png" />` を足す。`TimelineData.CurrentVersion` は上げない (v38 は未リリース)
  - シェーダーを変えていないエントリは `<Shader>` を書かない。読込時は「シェーダーが空 = シェーダーは元のまま」。旧版 SE は `<Shader>` が空のエントリを読まず、`<Texture>` は未知の要素として読み飛ばす。MTE は `<MaterialShaders>` ごと読み飛ばす。旧版で保存し直すとテクスチャ指定は消える
  - 読込時の適用はエントリの内容に揃える (シェーダーが空なら元へ戻し、エントリに無いプロパティの差し替えは外す)
  - `<Texture>` は `prop` の昇順で書く (比較・保存の揺れを防ぐ)
  - ファイルが見つからないエントリは差し替え指定として保持し、保存し直しても消さない
  - 背景タブの変更とタイムライン管理外の対象は書かない (シェーダーと同じ)
- **着替え等でマテリアルが作り直されたら再適用する** (仕様 #7・13)。前回の同期で保存対象だったマテリアルの Material が破棄・別物へ差し替え・一覧から除去されたら、そのときのエントリを保留へ戻す。保留は 30 フレームごとの再試行で、同じスロット・同名・同位置のマテリアルが現れた時点で適用する
  - これはシェーダーだけのエントリにも効く (前計画の「着替えでエントリは消える」を変更する)。別のアイテムへ着替えた場合、エントリは保留のままタイムラインに残る (同じアイテムへ戻すと再適用される)
  - ユーザーの `初期化` / `(元)` と、ゲーム側の上書き (シェーダー・テクスチャ) は作り直しではないので保留へ戻さない
  - 保留の再試行は `MaterialShaderManager` (MTE のマネージャ) が行うので、タイムラインが無い間は再適用されない
- シーンプリセット: `ScenePresetMaterial` に子要素 `<texture prop=".." file=".."/>` を足す。`ScenePresetData.CurrentVersion` は未リリースの 37 のまま、v37 のコメントに追記する。要素が無ければテクスチャは触らない。要素があれば、その分だけ差し替える (ほかのプロパティは触らない。シェーダー属性と同じ積み増し)
- Undo: `MaterialSnapshot` にテクスチャ指定を含める。適用はスナップショットの内容に揃える
- マテリアルのコピー / ペースト: テクスチャ指定も写す (ペースト先はコピー元の指定に揃える)
- 影を「落とす」側 (Renderer の影キャスト) の ON/OFF は作らない

### サンプル toon

- `UnityInjector/Config/SceneEditor/Toon/` に 4 枚同梱する (リリースの `release.bat` は `UnityInjector` を丸ごと xcopy するので、COM3D2 / COM3D2.5 の両パッケージに入る)。ゲームのテクスチャは再配布しない
- `assets/toon/generate.js` (Node 標準モジュールのみ) で生成する。256x16、RGBA、グレースケール。u (横) の明暗の境目を smoothstep でつなぐ

| ファイル | 暗部の値 | 境目 (u) | 用途 |
|---|---|---|---|
| `0_影なし.png` | 255 | — (全面白) | 影を消す。`_ToonRamp` に貼るとトゥーンの陰影も消える |
| `1_影薄め.png` | 160 | 0.33〜0.47 | 影の濃さを半分程度に |
| `2_影標準.png` | 0 | 0.33〜0.47 | ゲームの肌 (`ToonSkin_Shadow`) に近い |
| `3_影濃いめ.png` | 0 | 0.48〜0.62 | 影の範囲を広げる |

- 名前の数字は一覧の並び (自然順) を濃さの順にするため

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること。`Vector2Int` は 2.0 の Unity に無いので使わない。`Texture2D.LoadImage` は既存の `TextureUtils` と同じ書き方 (2.0 はインスタンスメソッド、2.5 は `ImageConversion` 拡張メソッド) で両方通る
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`Quaternion.Euler`, `GUI.*`, `SystemInfo.*` 等) はテストから呼べない。`Mathf` / `Rect` / `Vector2` の算術は可
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)。リネームは `git mv` と csproj の書き換えの両方
- `MTEUtils/` はサブモジュール。本計画では変更しない (`TextureLoader` は arc 専用なので使わず、`.tex` の解析は SE 側に置く)
- 実機検証は通常シーン (撮影モード非対応)
- Unity API (`Texture2D.LoadImage` の戻り値、`Material.GetTexture` / `SetTexture` に無いプロパティ名を渡したときの挙動) は実装前に Context7 で確認する。本計画は `HasProperty` で必ずガードする前提で書いている
- タイムライン XML は MTE → SE の一方向互換。SE 独自要素は MTE で読み飛ばされる前提でよい

## Review Focus

1. 着替え (同じアイテムの付け直し・ボディ再ロード) でマテリアルが作り直された後、30 フレーム以内にシェーダーとテクスチャが再適用され、タイムラインのエントリも消えない — Task 4 のテスト「作り直されたエントリは保留へ戻る」、Task 8 の実機確認
2. パーツ色の変更などゲーム側が `_MainTex` を差し替えたら、こちらの差し替えは外れ、読み込んだテクスチャが破棄される (見た目がゲームの色に従う) — Task 3 の `DropExternallyReplaced`、Task 8 の実機確認
3. `file="../../x.png"`・絶対パス・他フォルダ (`_ToonRamp` に `Texture/a.png`) を含むタイムライン / プリセットを読んでも、参照フォルダ外のファイルを読まない — Task 1 のテスト「フォルダ外を指すパスは捨てる」、Task 4 のテスト「不正なテクスチャ指定は読まない」
4. 参照ファイルが無い環境でタイムラインを読んでも例外にならず、行に `(見つかりません)` が出て、保存し直しても `<Texture>` が残る — Task 4 のテスト「テクスチャだけのエントリを往復する」、Task 8 の実機確認
5. タイムラインのキー再生・Undo/Redo の全再構築でテクスチャを読み直さない (同じ指定は no-op) — Task 3 の `Change` の早期 return、Task 8 の実機確認 (`Texture2D` の InstanceID が変わらない)
6. シーン遷移で読み込んだテクスチャが破棄され、残ったメイドのマテリアルが元のテクスチャに戻る (破棄済みテクスチャを参照したまま白くならない) — Task 3 の `ResetAllTextures`、Task 8 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/MaterialTextureCatalog.cs` | 対象プロパティ・表示名・参照フォルダ、パスの検証と正規化、一覧の絞り込み (純関数) |
| Create `source/COM3D2.SceneEditor.Plugin/MaterialTextureOverride.cs` | テクスチャ差し替え 1 件 (プロパティ + 相対パス、不変)、並べ替えと比較 |
| Create `source/COM3D2.SceneEditor.Plugin/TexFile.cs` | `.tex` のヘッダー解析 (純関数) |
| Create `source/COM3D2.SceneEditor.Plugin/MaterialTextureFiles.cs` | 参照フォルダの一覧 (キャッシュ) とファイルからの Texture2D 生成 |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterialTextures.cs` | マテリアル 1 件の差し替え状態 (元テクスチャ・読み込んだテクスチャの所有と破棄) |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs` | テクスチャ差し替え API、レジストリの一般化 (`changedMaterials` / `CollectChanged`) |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineMaterialTextureXml` と `<Texture>` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs` | `textures`、XML 変換、比較、複製 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | 読込時の絞り込み条件 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs` | 作り直されたエントリを保留へ戻す判定 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs` | テクスチャの適用・保留への戻し・シーン遷移での破棄 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | `ResetChangesNotIn` への改名 |
| Modify `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetMaterialTexture`、v37 コメント |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 捕捉・適用 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs` | テクスチャ指定の記録・復元・比較 |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialClipboard.cs` | テクスチャ指定のコピー / ペースト |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs` | `初期化` でテクスチャも戻す |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs` | テクスチャ行 |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規 .cs |
| Create `assets/toon/generate.js`、Create `UnityInjector/Config/SceneEditor/Toon/*.png` (4 枚) | サンプル toon の生成と同梱 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/MaterialTextureCatalogTests.cs` / `TexFileTests.cs` / `MaterialTextureXmlTests.cs` / `ScenePresetMaterialTextureTests.cs`、Modify `MaterialShaderSyncTests.cs` | テスト |
| Modify `docs-site/guide/maid-editing.md` / `docs-site/timeline/compatibility.md`、ワークスペースの `W:\COM3D2_5\work\CLAUDE.md` | 説明 |

---

### Task 1: パス・一覧・`.tex` の純関数

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialTextureCatalog.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialTextureOverride.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/TexFile.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (`<Compile Include="MaterialLookup.cs" />` の前後)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/MaterialTextureCatalogTests.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/TexFileTests.cs`

**Interfaces:**
- Produces (namespace `COM3D2.SceneEditor.Plugin`):
  - `MaterialTextureCatalog.ToonFolder = "Toon"` / `TextureFolder = "Texture"`
  - `MaterialTextureCatalog.Properties : string[]` (行の並び: `_MainTex`, `_ShadowTex`, `_ToonRamp`, `_ShadowRateToon`, `_OutlineToonRamp`)
  - `MaterialTextureCatalog.GetFolder(string property) : string` (対象外は null)
  - `MaterialTextureCatalog.IsToonProperty(string property) : bool`
  - `MaterialTextureCatalog.GetLabel(string property) : string`
  - `MaterialTextureCatalog.NormalizeFile(string file) : string` (不正なら null)
  - `MaterialTextureCatalog.TryNormalize(string property, string file, out string normalized) : bool` (プロパティの参照フォルダ内であること込み)
  - `MaterialTextureCatalog.IsTexFile(string file) : bool`
  - `MaterialTextureCatalog.ToRelativePath(string root, string fullPath) : string` (root 外は null)
  - `MaterialTextureCatalog.FilterFiles(string folder, IEnumerable<string> relativePaths) : List<string>` (正規化・フォルダ内・自然順)
  - `MaterialTextureCatalog.GetDisplayName(string file) : string` (先頭のフォルダ名を除く)
  - `class MaterialTextureOverride { readonly string property; readonly string file; ctor(property, file); static void Sort(List<MaterialTextureOverride>); static bool ListEquals(List<MaterialTextureOverride>, List<MaterialTextureOverride>) }`
  - `struct TexFileData { int version; int width; int height; int format; byte[] data; }`、`TexFile.TryParse(byte[] bytes, out TexFileData result) : bool`

- [ ] **Step 1: 失敗するテストを書く**

`MaterialTextureCatalogTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>テクスチャ差し替えのパスの検証と一覧の絞り込みを固定する</summary>
    public class MaterialTextureCatalogTests
    {
        [Theory]
        [InlineData("_ToonRamp", "Toon")]
        [InlineData("_ShadowRateToon", "Toon")]
        [InlineData("_OutlineToonRamp", "Toon")]
        [InlineData("_MainTex", "Texture")]
        [InlineData("_ShadowTex", "Texture")]
        [InlineData("_Color", null)]
        public void プロパティごとの参照フォルダ(string property, string folder)
        {
            Assert.Equal(folder, MaterialTextureCatalog.GetFolder(property));
        }

        [Theory]
        [InlineData("Toon\\0_影なし.png", "Toon/0_影なし.png")]
        [InlineData(" Toon/sub/a.TEX ", "Toon/sub/a.TEX")]
        [InlineData("Toon//a.png", "Toon/a.png")]
        public void 区切りを揃えて正規化する(string file, string expected)
        {
            Assert.Equal(expected, MaterialTextureCatalog.NormalizeFile(file));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("../Toon/a.png")]
        [InlineData("Toon/../../a.png")]
        [InlineData("C:/Toon/a.png")]
        [InlineData("/Toon/a.png")]
        [InlineData("\\\\server\\Toon\\a.png")]
        [InlineData("Toon/a.jpg")]
        [InlineData("Toon/a")]
        public void 不正なパスは捨てる(string file)
        {
            Assert.Null(MaterialTextureCatalog.NormalizeFile(file));
        }

        [Theory]
        [InlineData("_ToonRamp", "Toon/a.png", true)]
        [InlineData("_ToonRamp", "Toon/sub/a.png", true)]
        [InlineData("_ToonRamp", "Texture/a.png", false)]
        [InlineData("_ToonRamp", "Toonx/a.png", false)]
        [InlineData("_ToonRamp", "Toon", false)]
        [InlineData("_MainTex", "Texture/a.tex", true)]
        [InlineData("_Color", "Texture/a.png", false)]
        public void フォルダ外を指すパスは捨てる(string property, string file, bool expected)
        {
            string normalized;
            Assert.Equal(expected, MaterialTextureCatalog.TryNormalize(property, file, out normalized));
        }

        [Fact]
        public void 絶対パスをフォルダからの相対パスにする()
        {
            Assert.Equal("Toon/a.png", MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor", "C:\\g\\Config\\SceneEditor\\Toon\\a.png"));
            Assert.Equal("Toon/a.png", MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor\\", "c:\\g\\config\\sceneeditor\\Toon\\a.png"));
            Assert.Null(MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor", "C:\\g\\Config\\SceneEditorX\\Toon\\a.png"));
        }

        [Fact]
        public void 一覧はフォルダ内の画像だけを自然順に並べる()
        {
            var files = MaterialTextureCatalog.FilterFiles("Toon", new List<string>
            {
                "Toon/10_b.png", "Toon/2_a.png", "Toon/readme.txt", "Texture/x.png", "Toon/sub/c.tex",
            });

            Assert.Equal(new List<string> { "Toon/2_a.png", "Toon/10_b.png", "Toon/sub/c.tex" }, files);
        }

        [Fact]
        public void 表示名は先頭のフォルダを除く()
        {
            Assert.Equal("0_影なし.png", MaterialTextureCatalog.GetDisplayName("Toon/0_影なし.png"));
            Assert.Equal("sub/c.tex", MaterialTextureCatalog.GetDisplayName("Toon/sub/c.tex"));
        }

        [Fact]
        public void 差し替え一覧の比較はプロパティ順に揃えてから行う()
        {
            var a = new List<MaterialTextureOverride>
            {
                new MaterialTextureOverride("_ToonRamp", "Toon/a.png"),
                new MaterialTextureOverride("_MainTex", "Texture/b.png"),
            };
            var b = new List<MaterialTextureOverride>
            {
                new MaterialTextureOverride("_MainTex", "Texture/b.png"),
                new MaterialTextureOverride("_ToonRamp", "Toon/a.png"),
            };
            Assert.False(MaterialTextureOverride.ListEquals(a, b));

            MaterialTextureOverride.Sort(a);
            Assert.True(MaterialTextureOverride.ListEquals(a, b));

            b[1] = new MaterialTextureOverride("_ToonRamp", "Toon/c.png");
            Assert.False(MaterialTextureOverride.ListEquals(a, b));
        }
    }
}
```

`TexFileTests.cs`:

```csharp
using System.IO;
using System.Text;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>.tex のヘッダー解析を固定する (ゲームの ImportCM.LoadTextureFile と同じ読み方)</summary>
    public class TexFileTests
    {
        private static byte[] Build(int version, int width, int height, int format, byte[] data, int rectCount = 0)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("CM3D2_TEX");
                writer.Write(version);
                writer.Write("assets/texture/texture/a.png");
                if (version >= 1011)
                {
                    writer.Write(rectCount);
                    for (var i = 0; i < rectCount; i++)
                    {
                        writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(1f);
                    }
                }
                if (version >= 1010)
                {
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(format);
                }
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
                return stream.ToArray();
            }
        }

        [Fact]
        public void 版1010は幅_高さ_形式をヘッダーから読む()
        {
            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1010, 253, 14, 5, new byte[] { 1, 2, 3 }), out tex));

            Assert.Equal(253, tex.width);
            Assert.Equal(14, tex.height);
            Assert.Equal(5, tex.format);
            Assert.Equal(new byte[] { 1, 2, 3 }, tex.data);
        }

        [Fact]
        public void 版1011はUV矩形を読み飛ばす()
        {
            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1011, 64, 32, 12, new byte[] { 9 }, rectCount: 2), out tex));

            Assert.Equal(64, tex.width);
            Assert.Equal(32, tex.height);
            Assert.Equal(12, tex.format);
        }

        [Fact]
        public void 版1000はPNGのIHDRから幅と高さを読む()
        {
            var png = new byte[24];
            png[16] = 0; png[17] = 0; png[18] = 0x01; png[19] = 0x00; // 幅 256
            png[20] = 0; png[21] = 0; png[22] = 0; png[23] = 0x10;    // 高さ 16

            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1000, 0, 0, 0, png), out tex));

            Assert.Equal(256, tex.width);
            Assert.Equal(16, tex.height);
            Assert.Equal(5, tex.format); // ARGB32 (TextureFormat.ARGB32 = 5)
        }

        [Fact]
        public void ヘッダーが違えば読まない()
        {
            var bytes = Build(1010, 1, 1, 5, new byte[] { 0 });
            bytes[1] = (byte)'X';

            TexFileData tex;
            Assert.False(TexFile.TryParse(bytes, out tex));
        }

        [Fact]
        public void 途中で切れたファイルは読まない()
        {
            var bytes = Build(1010, 1, 1, 5, new byte[] { 1, 2, 3, 4 });
            var truncated = new byte[bytes.Length - 2];
            System.Array.Copy(bytes, truncated, truncated.Length);

            TexFileData tex;
            Assert.False(TexFile.TryParse(truncated, out tex));
            Assert.False(TexFile.TryParse(null, out tex));
        }

        [Fact]
        public void 文字列長が壊れたファイルは読まない()
        {
            // BinaryReader.ReadString の 7bit 長プレフィックスが終わらない (最上位ビットが立ち続ける)
            var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            TexFileData tex;
            Assert.False(TexFile.TryParse(bytes, out tex));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "MaterialTextureCatalogTests|TexFileTests"`
Expected: コンパイルエラー (`MaterialTextureCatalog` / `MaterialTextureOverride` / `TexFile` が無い)

- [ ] **Step 3: 実装する**

`MaterialTextureCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアルのテクスチャ差し替えで扱うプロパティと、差し替えに使えるファイルの決まり。
    /// ファイルは SceneEditor フォルダからの相対パス (/ 区切り) で持ち、
    /// プロパティごとの参照フォルダの外は指せない (他の環境から渡された XML でフォルダ外を読まないため)
    /// </summary>
    public static class MaterialTextureCatalog
    {
        public const string ToonFolder = "Toon";
        public const string TextureFolder = "Texture";

        /// <summary>行の並び順</summary>
        public static readonly string[] Properties =
        {
            "_MainTex", "_ShadowTex", "_ToonRamp", "_ShadowRateToon", "_OutlineToonRamp",
        };

        private static readonly string[] Labels =
        {
            "テクスチャ", "影テクスチャ", "トゥーン", "影の濃さ", "輪郭トゥーン",
        };

        private static readonly string[] Extensions = { ".png", ".tex" };

        public static bool IsToonProperty(string property)
        {
            return property == "_ToonRamp" || property == "_ShadowRateToon" || property == "_OutlineToonRamp";
        }

        /// <summary>プロパティの参照フォルダ名。対象外は null</summary>
        public static string GetFolder(string property)
        {
            if (IsToonProperty(property))
            {
                return ToonFolder;
            }
            if (property == "_MainTex" || property == "_ShadowTex")
            {
                return TextureFolder;
            }
            return null;
        }

        public static string GetLabel(string property)
        {
            var index = Array.IndexOf(Properties, property);
            return index >= 0 ? Labels[index] : property;
        }

        /// <summary>
        /// 相対パスを / 区切りに揃える。絶対パス・親への移動・ドライブ指定・対象外の拡張子は null
        /// </summary>
        public static string NormalizeFile(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return null;
            }
            var trimmed = file.Trim().Replace('\\', '/');
            if (trimmed.Length == 0 || trimmed[0] == '/' || trimmed.IndexOf(':') >= 0)
            {
                return null;
            }

            var parts = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == "." || part == "..")
                {
                    return null;
                }
            }
            var normalized = string.Join("/", parts);
            return HasImageExtension(normalized) ? normalized : null;
        }

        /// <summary>正規化したうえで、プロパティの参照フォルダ内 (サブフォルダ可) を指していれば true</summary>
        public static bool TryNormalize(string property, string file, out string normalized)
        {
            normalized = null;
            var folder = GetFolder(property);
            var candidate = NormalizeFile(file);
            if (folder == null || candidate == null
                || !candidate.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            normalized = candidate;
            return true;
        }

        public static bool IsTexFile(string file)
        {
            return file.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasImageExtension(string file)
        {
            foreach (var extension in Extensions)
            {
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    && file.Length > extension.Length
                    && file[file.Length - extension.Length - 1] != '/')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>root 配下の絶対パスを root からの相対パス (/ 区切り) にする。root の外は null</summary>
        public static string ToRelativePath(string root, string fullPath)
        {
            var prefix = root.TrimEnd('\\', '/') + "\\";
            var path = fullPath.Replace('/', '\\');
            if (!path.StartsWith(prefix.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return path.Substring(prefix.Length).Replace('\\', '/');
        }

        /// <summary>相対パスの一覧から、folder 内の画像だけを正規化して自然順に並べる</summary>
        public static List<string> FilterFiles(string folder, IEnumerable<string> relativePaths)
        {
            var result = new List<string>();
            foreach (var path in relativePaths)
            {
                var normalized = NormalizeFile(path);
                if (normalized != null
                    && normalized.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                    && !result.Exists(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(normalized);
                }
            }
            // 自然順の比較は MTEUtils/NaturalStringComparer.cs の既存クラス
            result.Sort(new NaturalStringComparer());
            return result;
        }

        /// <summary>一覧の表示名。参照フォルダ名は全行で同じなので除く</summary>
        public static string GetDisplayName(string file)
        {
            var slash = file.IndexOf('/');
            return slash >= 0 ? file.Substring(slash + 1) : file;
        }
    }
}
```

`MaterialTextureOverride.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// テクスチャ差し替え 1 件 (シェーダーのプロパティ名と、SceneEditor フォルダからの相対パス)。
    /// タイムライン・プリセット・Undo・クリップボードで共有するため不変にする
    /// </summary>
    public class MaterialTextureOverride
    {
        public readonly string property;
        public readonly string file;

        public MaterialTextureOverride(string property, string file)
        {
            this.property = property;
            this.file = file;
        }

        /// <summary>保存と比較の順を揃えるため、プロパティ名の昇順に並べる</summary>
        public static void Sort(List<MaterialTextureOverride> list)
        {
            list.Sort((a, b) => string.CompareOrdinal(a.property, b.property));
        }

        /// <summary>並びも含めて同じか。呼び出し側は Sort 済みの一覧を渡す</summary>
        public static bool ListEquals(List<MaterialTextureOverride> a, List<MaterialTextureOverride> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].property != b[i].property || a[i].file != b[i].file)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
```

`TexFile.cs`:

```csharp
using System;
using System.IO;
using System.Text;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>.tex の中身。format は UnityEngine.TextureFormat の数値</summary>
    public struct TexFileData
    {
        public int version;
        public int width;
        public int height;
        public int format;
        public byte[] data;
    }

    /// <summary>
    /// CM3D2 の .tex を解析する。ゲームの ImportCM.LoadTextureFile は arc 経由でしか読めないため、
    /// 任意のファイルを読むために同じ手順を持つ。Texture2D の生成は呼び出し側 (TextureResource) が行う
    /// </summary>
    public static class TexFile
    {
        private const string Header = "CM3D2_TEX";

        /// <summary>TextureFormat.ARGB32。版 1000 は形式を持たず、中身は PNG</summary>
        private const int FormatARGB32 = 5;

        public static bool TryParse(byte[] bytes, out TexFileData result)
        {
            result = new TexFileData();
            if (bytes == null)
            {
                return false;
            }

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                {
                    if (reader.ReadString() != Header)
                    {
                        return false;
                    }
                    result.version = reader.ReadInt32();
                    reader.ReadString(); // 元ファイルのパス

                    result.format = FormatARGB32;
                    if (result.version >= 1010)
                    {
                        if (result.version >= 1011)
                        {
                            var rectCount = reader.ReadInt32();
                            // UV 矩形 (x, y, w, h) は差し替えでは使わない
                            reader.ReadBytes(Math.Max(0, rectCount) * 16);
                        }
                        result.width = reader.ReadInt32();
                        result.height = reader.ReadInt32();
                        result.format = reader.ReadInt32();
                    }

                    var size = reader.ReadInt32();
                    if (size < 0)
                    {
                        return false;
                    }
                    result.data = reader.ReadBytes(size);
                    if (result.data.Length != size)
                    {
                        return false;
                    }

                    if (result.version == 1000)
                    {
                        if (size < 24)
                        {
                            return false;
                        }
                        var d = result.data;
                        result.width = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
                        result.height = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
                    }
                    return result.width > 0 && result.height > 0;
                }
            }
            // 途中で切れたファイル (EndOfStreamException) や壊れた文字列長 (FormatException) など、
            // 読めない .tex は呼び出し側に頼らずここで false にする
            catch (Exception)
            {
                return false;
            }
        }
    }
}
```

csproj の `<Compile Include="MaterialLookup.cs" />` の直後に足す:

```xml
    <Compile Include="MaterialTextureCatalog.cs" />
    <Compile Include="MaterialTextureOverride.cs" />
```

`<Compile Include="ShaderCatalog.cs" />` の直後に `<Compile Include="TexFile.cs" />` を足す。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "MaterialTextureCatalogTests|TexFileTests"` が PASS。`途中で切れたファイルは読まない` が落ちたら、`ReadBytes` が短く返る経路 (例外ではない) を先に疑う。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaterialTextureCatalog.cs source/COM3D2.SceneEditor.Plugin/MaterialTextureOverride.cs source/COM3D2.SceneEditor.Plugin/TexFile.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/MaterialTextureCatalogTests.cs source/COM3D2.SceneEditor.Plugin.Tests/TexFileTests.cs
git commit -m "feat(material): テクスチャ差し替えのパス検証と .tex の解析を追加する"
```

### Task 2: ファイルからの読み込みとサンプル toon

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialTextureFiles.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Create: `assets/toon/generate.js`
- Create: `UnityInjector/Config/SceneEditor/Toon/0_影なし.png` / `1_影薄め.png` / `2_影標準.png` / `3_影濃いめ.png` (生成物)

**Interfaces:**
- Consumes: `MaterialTextureCatalog.*`、`TexFile.TryParse` (Task 1)
- Produces:
  - `MaterialTextureFiles.GetChoices(string folder) : List<string>` (先頭は `""` = 元。キャッシュを返すので呼び出し側は書き換えない)
  - `MaterialTextureFiles.Refresh()` (一覧のキャッシュを捨てる)
  - `MaterialTextureFiles.Load(string property, string file, Texture original) : Texture2D` (不正・見つからない・読めないなら null。警告はファイルごとに 1 回)

- [ ] **Step 1: 読み込みを実装する**

Context7 で Unity の `Texture2D.LoadImage` (戻り値 bool、失敗時の挙動) を確認してから書く。

`MaterialTextureFiles.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// テクスチャ差し替えの参照フォルダ (Config\SceneEditor\Toon, Texture) の一覧と読み込み。
    /// 読み込んだ Texture2D の所有と破棄は呼び出し側 (ModelMaterialTextures) が持つ
    /// </summary>
    public static class MaterialTextureFiles
    {
        private static string rootPath => PluginUtils.PluginDataPath;

        // フォルダ名 → 選択肢 (先頭は元に戻す用の空文字)。表示のたびに走査しないよう控え、「更新」で作り直す
        private static readonly Dictionary<string, List<string>> _choices = new Dictionary<string, List<string>>();

        // 同じファイルの警告を Undo/Redo の再適用のたびに出さない
        private static readonly HashSet<string> _warnedFiles = new HashSet<string>();

        public static List<string> GetChoices(string folder)
        {
            List<string> choices;
            if (!_choices.TryGetValue(folder, out choices))
            {
                choices = Scan(folder);
                _choices[folder] = choices;
            }
            return choices;
        }

        public static void Refresh()
        {
            _choices.Clear();
            _warnedFiles.Clear();
        }

        private static List<string> Scan(string folder)
        {
            var choices = new List<string> { "" };
            var directory = Path.Combine(rootPath, folder);
            try
            {
                // 置き場所が分かるよう、無ければ作っておく
                Directory.CreateDirectory(directory);
                var relativePaths = new List<string>();
                foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = MaterialTextureCatalog.ToRelativePath(rootPath, path);
                    if (relative != null)
                    {
                        relativePaths.Add(relative);
                    }
                }
                choices.AddRange(MaterialTextureCatalog.FilterFiles(folder, relativePaths));
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            return choices;
        }

        /// <summary>
        /// 相対パスのファイルを Texture2D にする。toon は端の回り込みで境目の反対側に色が滲まないよう Clamp に固定し、
        /// それ以外は元テクスチャの wrap を引き継ぐ。読めなければ null
        /// </summary>
        public static Texture2D Load(string property, string file, Texture original)
        {
            string normalized;
            if (!MaterialTextureCatalog.TryNormalize(property, file, out normalized))
            {
                WarnOnce(file, "テクスチャの指定が不正です: {0} ({1})", file, property);
                return null;
            }

            var path = Path.Combine(rootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                WarnOnce(normalized, "テクスチャが見つかりません: {0}", path);
                return null;
            }

            try
            {
                var texture = MaterialTextureCatalog.IsTexFile(normalized)
                    ? LoadTex(File.ReadAllBytes(path), normalized)
                    : LoadPng(File.ReadAllBytes(path), normalized);
                if (texture == null)
                {
                    return null;
                }

                texture.name = normalized;
                texture.wrapMode = MaterialTextureCatalog.IsToonProperty(property)
                    ? TextureWrapMode.Clamp
                    : original != null ? original.wrapMode : TextureWrapMode.Repeat;
                // 元テクスチャが無いスロットは Texture2D の既定 (Bilinear) のまま
                if (original != null)
                {
                    texture.filterMode = original.filterMode;
                }
                return texture;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return null;
            }
        }

        private static Texture2D LoadPng(byte[] bytes, string file)
        {
            // ゲームの toon と同じくミップマップなし
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                WarnOnce(file, "画像を読み込めません: {0}", file);
                return null;
            }
            return texture;
        }

        private static Texture2D LoadTex(byte[] bytes, string file)
        {
            TexFileData tex;
            if (!TexFile.TryParse(bytes, out tex))
            {
                WarnOnce(file, "tex ファイルを読み込めません: {0}", file);
                return null;
            }
            return new TextureResource(tex.width, tex.height, (TextureFormat)tex.format, null, tex.data)
                .CreateTexture2D();
        }

        private static void WarnOnce(string key, string format, params object[] args)
        {
            if (_warnedFiles.Add(key ?? ""))
            {
                MTEUtils.LogWarning(format, args);
            }
        }
    }
}
```

csproj の `<Compile Include="MaterialTextureCatalog.cs" />` の直後に `<Compile Include="MaterialTextureFiles.cs" />` を足す。

- [ ] **Step 2: サンプル toon の生成スクリプトを書く**

`assets/toon/generate.js`:

```js
// サンプルの toon テクスチャ (影の濃さ) を生成し、配布用 Config の Toon フォルダへ書き出す。
//
//   node generate.js
//
// ゲームのテクスチャは再配布しないため、ここで数式から作る。
// 形はゲームの _ShadowRateToon (左 = 暗部が黒、右 = 明部が白、ToonSkin_Shadow は u≈0.33〜0.5 で立ち上がる) に合わせる。
// PNG のエンコードは icons/generate.js と同じく Node 標準の zlib で組み立てる
// (ライブラリが吐く PNG は Unity の Texture2D.LoadImage が読めないことがあるため)。

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const WIDTH = 256;
const HEIGHT = 16;
const OUT_DIR = path.join(__dirname, '../../UnityInjector/Config/SceneEditor/Toon');

// dark: 暗部の値 (0〜255)、from / to: 暗部から白へ立ち上がる u の範囲
const SAMPLES = [
    { name: '0_影なし', dark: 255, from: 0.33, to: 0.47 },
    { name: '1_影薄め', dark: 160, from: 0.33, to: 0.47 },
    { name: '2_影標準', dark: 0, from: 0.33, to: 0.47 },
    { name: '3_影濃いめ', dark: 0, from: 0.48, to: 0.62 },
];

const CRC_TABLE = (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c >>> 0;
    }
    return table;
})();

function crc32(buffer) {
    let crc = 0xffffffff;
    for (const byte of buffer) {
        crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
    }
    return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(data.length, 0);
    head.write(type, 4, 'ascii');
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
    return Buffer.concat([head, data, crc]);
}

function encodePng(rgba, width, height) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8; // ビット深度
    ihdr[9] = 6; // RGBA
    const raw = Buffer.alloc((width * 4 + 1) * height);
    for (let y = 0; y < height; y++) {
        raw[y * (width * 4 + 1)] = 0; // フィルタなし
        rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
    }
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', zlib.deflateSync(raw)),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

function smoothstep(from, to, x) {
    const t = Math.min(1, Math.max(0, (x - from) / (to - from)));
    return t * t * (3 - 2 * t);
}

function render(sample) {
    const rgba = Buffer.alloc(WIDTH * HEIGHT * 4);
    for (let x = 0; x < WIDTH; x++) {
        const u = x / (WIDTH - 1);
        const value = Math.round(sample.dark + (255 - sample.dark) * smoothstep(sample.from, sample.to, u));
        for (let y = 0; y < HEIGHT; y++) {
            const i = (y * WIDTH + x) * 4;
            rgba[i] = rgba[i + 1] = rgba[i + 2] = value;
            rgba[i + 3] = 255;
        }
    }
    return encodePng(rgba, WIDTH, HEIGHT);
}

fs.mkdirSync(OUT_DIR, { recursive: true });
for (const sample of SAMPLES) {
    const file = path.join(OUT_DIR, `${sample.name}.png`);
    fs.writeFileSync(file, render(sample));
    console.log(`生成しました: ${file}`);
}
```

- [ ] **Step 3: 生成して確認する**

Run: `node assets/toon/generate.js`
Expected: `UnityInjector/Config/SceneEditor/Toon/` に 4 枚。`node -e "const b=require('fs').readFileSync('UnityInjector/Config/SceneEditor/Toon/2_影標準.png');console.log(b.readUInt32BE(16),b.readUInt32BE(20))"` が `256 16` を出す。画像ビューアで左が黒 (影なしは全面白) → 右が白であること。

`git status` で 4 枚が追跡対象に出ること (`.gitignore` は `UnityInjector/*.dll` だけを除外している)。

- [ ] **Step 4: 両構成をビルドする**

MSBuild COM3D2 → COM3D25 がエラーなし。`TextureResource` が両構成の Assembly-CSharp で解決すること。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaterialTextureFiles.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj assets/toon/generate.js UnityInjector/Config/SceneEditor/Toon
git commit -m "feat(material): 差し替え用テクスチャの読み込みとサンプル toon を追加する"
```

### Task 3: `ModelMaterial` のテクスチャ差し替え

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterialTextures.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs` (`Init` :120-143、`ChangeShader` :255-274、レジストリ :99-104 / :305-346)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs` (改名の追随のみ)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `MaterialTextureFiles.Load` (Task 2)、`MaterialTextureOverride` (Task 1)
- Produces (namespace `COM3D2.MotionTimelineEditor.Plugin`):
  - `ModelMaterial.isTextureChanged : bool` / `isChanged : bool` (シェーダーかテクスチャ) / `isReleased : bool`
  - `ModelMaterial.GetTextureFile(string property) : string` (差し替えていなければ null)
  - `ModelMaterial.IsTextureMissing(string property) : bool`
  - `ModelMaterial.GetTextureOverrides(List<SE.MaterialTextureOverride> result)` (プロパティ順)
  - `ModelMaterial.ChangeTexture(string property, string file)` (null / 空文字は元へ戻す)
  - `ModelMaterial.ResetTextures()`
  - `ModelMaterial.SetTextureOverrides(List<SE.MaterialTextureOverride> overrides)` (内容に揃える)
  - `ModelMaterial.changedVersion` / `ModelMaterial.CollectChanged(List<ModelMaterial>)` (旧 `shaderChangedVersion` / `CollectShaderChanged` の改名)
  - `ModelMaterial.ResetAllTextures()` (static、シーン遷移用)

- [ ] **Step 1: `ModelMaterialTextures` を書く**

Context7 で `Material.GetTexture` / `SetTexture` に無いプロパティ名を渡したときの挙動を確認する (本実装は `HasProperty` で必ずガードする)。

`Timeline/ModelMaterialTextures.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// マテリアル 1 件のテクスチャ差し替え。差し替え前のテクスチャを控え、
    /// 読み込んだテクスチャはここが所有して、外すときに破棄する
    /// </summary>
    public class ModelMaterialTextures
    {
        private class Slot
        {
            public string property;
            public string file;
            public Texture original;
            /// <summary>読み込んだテクスチャ。ファイルが見つからなければ null (指定だけ残して保存からは消さない)</summary>
            public Texture2D loaded;
        }

        private readonly List<Slot> _slots = new List<Slot>();

        public int count => _slots.Count;

        private Slot Find(string property)
        {
            return _slots.Find(s => s.property == property);
        }

        public string GetFile(string property)
        {
            var slot = Find(property);
            return slot != null ? slot.file : null;
        }

        public bool IsMissing(string property)
        {
            var slot = Find(property);
            return slot != null && slot.loaded == null;
        }

        public void GetOverrides(List<SE.MaterialTextureOverride> result)
        {
            result.Clear();
            foreach (var slot in _slots)
            {
                result.Add(new SE.MaterialTextureOverride(slot.property, slot.file));
            }
            SE.MaterialTextureOverride.Sort(result);
        }

        /// <summary>
        /// 差し替える。同じファイルを読み込み済みなら何もしない (Undo/Redo の再適用で読み直さない)。
        /// 見つからないファイルは指定だけ残し、マテリアルは元のテクスチャに戻す
        /// </summary>
        public void Change(Material material, string property, string file)
        {
            var slot = Find(property);
            if (slot != null && slot.file == file && slot.loaded != null)
            {
                return;
            }
            if (slot == null)
            {
                slot = new Slot
                {
                    property = property,
                    original = material.HasProperty(property) ? material.GetTexture(property) : null,
                };
                _slots.Add(slot);
            }

            var previous = slot.loaded;
            slot.file = file;
            slot.loaded = SE.MaterialTextureFiles.Load(property, file, slot.original);
            if (material.HasProperty(property))
            {
                material.SetTexture(property, slot.loaded != null ? slot.loaded : slot.original);
            }
            // マテリアルが新しいテクスチャを指した後で破棄する
            Destroy(previous);
        }

        public void Reset(Material material, string property)
        {
            var slot = Find(property);
            if (slot == null)
            {
                return;
            }
            _slots.Remove(slot);
            if (material != null && material.HasProperty(property))
            {
                material.SetTexture(property, slot.original);
            }
            Destroy(slot.loaded);
        }

        public void ResetAll(Material material)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                Reset(material, _slots[i].property);
            }
        }

        /// <summary>overrides の内容に揃える。載っていないプロパティの差し替えは外す</summary>
        public void SetAll(Material material, List<SE.MaterialTextureOverride> overrides)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var property = _slots[i].property;
                if (!overrides.Exists(o => o.property == property))
                {
                    Reset(material, property);
                }
            }
            foreach (var entry in overrides)
            {
                Change(material, entry.property, entry.file);
            }
        }

        /// <summary>
        /// ゲーム側 (パーツ色の変更で _MainTex を作り直す等) が差し替えたテクスチャを取り込む。
        /// こちらの差し替えは外し、読み込んだテクスチャを破棄する。外したら true
        /// </summary>
        public bool DropExternallyReplaced(Material material)
        {
            var dropped = false;
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (slot.loaded == null || !material.HasProperty(slot.property)
                    || material.GetTexture(slot.property) == slot.loaded)
                {
                    continue;
                }
                _slots.RemoveAt(i);
                Destroy(slot.loaded);
                dropped = true;
            }
            return dropped;
        }

        /// <summary>シェーダー差し替え後に貼り直す。差し替え時にプロパティが無かった場合に備える</summary>
        public void Reapply(Material material)
        {
            foreach (var slot in _slots)
            {
                if (slot.loaded != null && material.HasProperty(slot.property))
                {
                    material.SetTexture(slot.property, slot.loaded);
                }
            }
        }

        /// <summary>Material が破棄・作り直されたとき。Material には触らず、読み込んだテクスチャだけ破棄する</summary>
        public void DestroyAll()
        {
            foreach (var slot in _slots)
            {
                Destroy(slot.loaded);
            }
            _slots.Clear();
        }

        private static void Destroy(Texture2D texture)
        {
            if (texture != null)
            {
                Object.Destroy(texture);
            }
        }
    }
}
```

csproj の `<Compile Include="Timeline\ModelMaterial.cs" />` の直後に `<Compile Include="Timeline\ModelMaterialTextures.cs" />` を足す。

- [ ] **Step 2: `ModelMaterial` にテクスチャ API を足し、レジストリを一般化する**

フィールド・プロパティ (`isShaderChanged` の下):

```csharp
        // テクスチャ差し替え。読み込んだテクスチャの所有と破棄もここが持つ
        private readonly ModelMaterialTextures _textures = new ModelMaterialTextures();

        public bool isTextureChanged => material != null && _textures.count > 0;

        /// <summary>シェーダーかテクスチャを差し替えているか。タイムラインへ保存する対象</summary>
        public bool isChanged => isShaderChanged || isTextureChanged;

        /// <summary>コントローラの一覧から外された。作り直しの検出 (MaterialShaderManager) に使う</summary>
        public bool isReleased { get; private set; }
```

レジストリの改名 (コメントも「シェーダーかテクスチャを変えたマテリアル」へ直す):

- `shaderChangedMaterials` → `changedMaterials`
- `shaderChangedVersion` → `changedVersion`
- `UpdateShaderChangedRegistry()` → `UpdateChangedRegistry()` (中身は `isShaderChanged` を `isChanged` に)。呼び出し元は `Init()` / `ChangeShader()` と、前計画で足した `AdoptExternalShader()` (ゲーム側がシェーダーを差し替えたのを取り込む処理) の 3 箇所。`AdoptExternalShader` の呼び出しも忘れずに新しい名前へ直す
- `CollectShaderChanged` → `CollectChanged`:

```csharp
        /// <summary>
        /// シェーダーかテクスチャを変えたマテリアルを result へ写す。
        /// 着替え・モデル削除で破棄されたものは読み込んだテクスチャを破棄して落とし、
        /// ゲーム側に上書きされて変更が残っていないものも落とす (落としたら version も進める)
        /// </summary>
        public static void CollectChanged(List<ModelMaterial> result)
        {
            result.Clear();
            var removed = changedMaterials.RemoveWhere(m =>
            {
                if (m.material == null || m.controller == null)
                {
                    m._textures.DestroyAll();
                    return true;
                }
                m._textures.DropExternallyReplaced(m.material);
                return !m.isChanged;
            });
            if (removed > 0)
            {
                changedVersion++;
            }
            result.AddRange(changedMaterials);
        }

        /// <summary>
        /// シーン遷移で全マテリアルのテクスチャ差し替えを元へ戻し、読み込んだテクスチャを破棄する。
        /// メイドはシーンをまたいで残るため、破棄したテクスチャを指したままにしない
        /// </summary>
        public static void ResetAllTextures()
        {
            foreach (var m in changedMaterials.ToList())
            {
                if (m.material != null)
                {
                    m._textures.ResetAll(m.material);
                }
                else
                {
                    m._textures.DestroyAll();
                }
            }
            if (changedMaterials.RemoveWhere(m => m.material == null || !m.isChanged) > 0)
            {
                changedVersion++;
            }
        }
```

`Init()` の先頭に:

```csharp
            // Material が作り直された。前の Material 用に読んだテクスチャは要らない
            // (再適用は MaterialShaderManager が保留から行う)
            _textures.DestroyAll();
```

`ChangeShader` の `RefreshProperties();` の直後に `_textures.Reapply(material);` を足す。

`Release()`:

```csharp
        public void Release()
        {
            isReleased = true;
            _textures.DestroyAll();
            if (changedMaterials.Remove(this))
            {
                changedVersion++;
            }
        }
```

テクスチャ API (`ResetShader` の下):

```csharp
        public string GetTextureFile(string property) => _textures.GetFile(property);

        public bool IsTextureMissing(string property) => _textures.IsMissing(property);

        public void GetTextureOverrides(List<SE.MaterialTextureOverride> result) => _textures.GetOverrides(result);

        /// <summary>テクスチャを差し替える。file が空なら差し替え前へ戻す</summary>
        public void ChangeTexture(string property, string file)
        {
            if (material == null)
            {
                return;
            }
            _textures.DropExternallyReplaced(material);
            if (string.IsNullOrEmpty(file))
            {
                _textures.Reset(material, property);
            }
            else
            {
                _textures.Change(material, property, file);
            }
            UpdateChangedRegistry();
        }

        /// <summary>全テクスチャを差し替え前へ戻す。値とシェーダーは戻さない</summary>
        public void ResetTextures()
        {
            if (material == null)
            {
                return;
            }
            _textures.ResetAll(material);
            UpdateChangedRegistry();
        }

        /// <summary>差し替えを overrides の内容に揃える (Undo・ペースト・タイムライン読込)</summary>
        public void SetTextureOverrides(List<SE.MaterialTextureOverride> overrides)
        {
            if (material == null)
            {
                return;
            }
            _textures.DropExternallyReplaced(material);
            _textures.SetAll(material, overrides);
            UpdateChangedRegistry();
        }
```

`Reset()` のコメントを「値を初期値へ戻す。シェーダーとテクスチャは戻さない (タイムラインのキー操作からも呼ばれるため)」に直す。

- [ ] **Step 3: `MaterialShaderManager` を改名に追随させる**

`ModelMaterial.CollectShaderChanged` → `ModelMaterial.CollectChanged`、`ModelMaterial.shaderChangedVersion` → `ModelMaterial.changedVersion` (中身の変更は Task 5)。`rg -n "CollectShaderChanged|shaderChangedVersion|UpdateShaderChangedRegistry|shaderChangedMaterials" source` が 0 件になること。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS (既存テストの退行なし)。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterialTextures.cs source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(material): ModelMaterial にテクスチャの差し替えと元への戻しを追加する"
```

### Task 4: タイムラインのデータと XML

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:78-94`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:830-840`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/MaterialTextureXmlTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderSyncTests.cs`

**Interfaces:**
- Consumes: `MaterialTextureOverride`、`MaterialTextureCatalog.TryNormalize` (Task 1)
- Produces:
  - `class TimelineMaterialTextureXml { [XmlAttribute("prop")] string property; [XmlAttribute("file")] string file; }`
  - `TimelineMaterialShaderXml.textures : List<TimelineMaterialTextureXml>` (`[XmlElement("Texture")]`)
  - `TimelineMaterialShaderData.textures : List<SE.MaterialTextureOverride>` (プロパティ順)、`hasChanges : bool`
  - `MaterialShaderSync.Requeue(List<TimelineMaterialShaderData> pending, List<TimelineMaterialShaderData> live, TimelineMaterialShaderData lost) : bool`

- [ ] **Step 1: 失敗するテストを書く**

`MaterialTextureXmlTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインのテクスチャ差し替え (&lt;MaterialShader&gt; の &lt;Texture&gt;) の保存と読込を固定する。
    /// テクスチャだけのエントリは &lt;Shader&gt; を書かない (旧版はシェーダーが空のエントリを読まない)
    /// </summary>
    public class MaterialTextureXmlTests
    {
        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        private static TimelineMaterialShaderData TextureOnly()
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = 0, owner = "body", material = "skin", index = 0,
                textures = new List<MaterialTextureOverride>
                {
                    new MaterialTextureOverride("_ShadowRateToon", "Toon/0_影なし.png"),
                    new MaterialTextureOverride("_ToonRamp", "Toon/2_影標準.png"),
                },
            };
        }

        [Fact]
        public void テクスチャだけのエントリを往復する()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(TextureOnly().ToXml());

            var text = Serialize(xml);
            Assert.DoesNotContain("<Shader", text);
            Assert.Contains("<Texture prop=\"_ShadowRateToon\" file=\"Toon/0_影なし.png\" />", text);

            var data = new TimelineData();
            data.FromXml(Deserialize(text));
            Assert.Single(data.materialShaders);
            Assert.True(TextureOnly().ContentEquals(data.materialShaders[0]));
        }

        [Fact]
        public void シェーダーとテクスチャを併せて往復する()
        {
            var entry = TextureOnly();
            entry.shader = "com3d2mod/Standard_NPRToonV2_Lit_";
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(entry.ToXml());

            var restored = new TimelineMaterialShaderData();
            restored.FromXml(Deserialize(Serialize(xml)).materialShaders[0]);

            Assert.True(entry.ContentEquals(restored));
        }

        [Fact]
        public void 要素の無い旧エントリはテクスチャなしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><TimelineData version=\"38\"><MaterialShaders>"
                + "<MaterialShader><MaidSlotNo>0</MaidSlotNo><Owner>wear</Owner><Material>a</Material>"
                + "<Index>0</Index><Shader>CM3D2/Lighted</Shader></MaterialShader></MaterialShaders></TimelineData>");

            var data = new TimelineMaterialShaderData();
            data.FromXml(restored.materialShaders[0]);
            Assert.Empty(data.textures);
            Assert.Equal("CM3D2/Lighted", data.shader);
        }

        [Fact]
        public void 不正なテクスチャ指定は読まない()
        {
            var xml = new TimelineMaterialShaderXml
            {
                maidSlotNo = 0, owner = "body", material = "skin",
                textures = new List<TimelineMaterialTextureXml>
                {
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "../../evil.png" },
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "Texture/a.png" },
                    new TimelineMaterialTextureXml { property = "_Color", file = "Toon/a.png" },
                    new TimelineMaterialTextureXml { property = "_ShadowRateToon", file = "Toon\\b.png" },
                    new TimelineMaterialTextureXml { property = "_ShadowRateToon", file = "Toon/c.png" },
                },
            };

            var data = new TimelineMaterialShaderData();
            data.FromXml(xml);

            // 区切りは揃え、同じプロパティは先勝ち
            Assert.Single(data.textures);
            Assert.Equal("_ShadowRateToon", data.textures[0].property);
            Assert.Equal("Toon/b.png", data.textures[0].file);
        }

        [Fact]
        public void シェーダーも有効なテクスチャも無いエントリはタイムラインに読まない()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(new TimelineMaterialShaderXml
            {
                maidSlotNo = 0, owner = "body", material = "skin",
                textures = new List<TimelineMaterialTextureXml>
                {
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "../x.png" },
                },
            });
            xml.materialShaders.Add(TextureOnly().ToXml());

            var data = new TimelineData();
            data.FromXml(xml);

            Assert.Single(data.materialShaders);
        }

        [Fact]
        public void テクスチャが違えば内容が違う()
        {
            var a = TextureOnly();
            var b = TextureOnly();
            b.textures[1] = new MaterialTextureOverride("_ToonRamp", "Toon/3_影濃いめ.png");

            Assert.True(a.IsSameTarget(b));
            Assert.False(a.ContentEquals(b));
        }

        [Fact]
        public void 複製はテクスチャ一覧を共有しない()
        {
            var original = TextureOnly();
            var clone = original.Clone();
            clone.textures.RemoveAt(0);

            Assert.Equal(2, original.textures.Count);
        }
    }
}
```

`MaterialShaderSyncTests.cs` の末尾 (クラス内) に足す:

```csharp
        [Fact]
        public void 作り直されたエントリは保留へ戻る()
        {
            var pending = new List<TimelineMaterialShaderData>();
            var live = new List<TimelineMaterialShaderData>();

            Assert.True(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "a", 0, "s")));
            Assert.Single(pending);
        }

        [Fact]
        public void 同じ対象が現在か保留にあれば保留へ戻さない()
        {
            var pending = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "old") };
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "b", 0, "s") };

            Assert.False(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "a", 0, "new")));
            Assert.False(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "b", 0, "new")));
            Assert.Single(pending);
            Assert.Equal("old", pending[0].shader);
        }

        [Fact]
        public void 併合でもテクスチャは現在の状態が勝つ()
        {
            var liveEntry = Entry(0, "body", "skin", 0, "");
            liveEntry.textures.Add(new MaterialTextureOverride("_ToonRamp", "Toon/a.png"));
            var pendingEntry = Entry(0, "body", "skin", 0, "");
            pendingEntry.textures.Add(new MaterialTextureOverride("_ToonRamp", "Toon/b.png"));

            var merged = MaterialShaderSync.Merge(
                new List<TimelineMaterialShaderData> { liveEntry },
                new List<TimelineMaterialShaderData> { pendingEntry });

            Assert.Single(merged);
            Assert.Equal("Toon/a.png", merged[0].textures[0].file);
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "MaterialTextureXmlTests|MaterialShaderSyncTests"`
Expected: コンパイルエラー (`textures` / `TimelineMaterialTextureXml` / `Requeue` が無い)

- [ ] **Step 3: XML を足す**

`TimelineXml.cs` の `TimelineMaterialShaderXml` を次へ置き換える (直前に `TimelineMaterialTextureXml` を置く):

```csharp
    /// <summary>テクスチャ差し替え 1 件 (SE 独自)。file は Config\SceneEditor からの相対パス</summary>
    public class TimelineMaterialTextureXml
    {
        [XmlAttribute("prop")]
        public string property;
        [XmlAttribute("file")]
        public string file;
    }

    /// <summary>
    /// マテリアルのシェーダー・テクスチャ変更 1 件 (SE 独自)。メイドは MaidSlotNo + スロット名、
    /// モデルは MaidSlotNo = -1 + タイムラインのモデル名で所有者を表す。
    /// シェーダーを変えていなければ Shader は書かない (旧版はシェーダーが空のエントリを読まない)
    /// </summary>
    public class TimelineMaterialShaderXml
    {
        [XmlElement("MaidSlotNo")]
        public int maidSlotNo = -1;
        [XmlElement("Owner")]
        public string owner;
        [XmlElement("Material")]
        public string material;
        [XmlElement("Index")]
        public int index;
        [XmlElement("Shader")]
        public string shader;
        // 旧版・MTE は未知の要素として読み飛ばす
        [XmlElement("Texture")]
        public List<TimelineMaterialTextureXml> textures = new List<TimelineMaterialTextureXml>();
    }
```

- [ ] **Step 4: データを足す**

`TimelineMaterialShaderData.cs` を次の内容にする (`using System.Collections.Generic;` と `using SE = COM3D2.SceneEditor.Plugin;` を足す):

```csharp
using System.Collections.Generic;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインに保存するマテリアルのシェーダー・テクスチャ変更 1 件。
    /// キーフレームではなく、読込時に 1 回だけ適用する定義の値
    /// </summary>
    public class TimelineMaterialShaderData
    {
        /// <summary>メイドならタイムラインのメイド番号、モデルなら -1</summary>
        public int maidSlotNo = -1;

        /// <summary>メイドはスロット名 (MaidSlotStat.name)、モデルはタイムラインのモデル名</summary>
        public string owner = "";

        /// <summary>Unity マテリアル名 (ModelMaterial.displayName)</summary>
        public string material = "";

        /// <summary>所有者内のマテリアル位置。同名マテリアルの同定に使う</summary>
        public int index;

        /// <summary>差し替えたシェーダー名。空ならシェーダーは元のまま</summary>
        public string shader = "";

        /// <summary>テクスチャ差し替え (プロパティ順)</summary>
        public List<SE.MaterialTextureOverride> textures = new List<SE.MaterialTextureOverride>();

        public bool hasChanges => shader.Length > 0 || textures.Count > 0;

        public bool IsSameTarget(TimelineMaterialShaderData other)
        {
            return other != null
                && maidSlotNo == other.maidSlotNo
                && owner == other.owner
                && material == other.material
                && index == other.index;
        }

        public bool ContentEquals(TimelineMaterialShaderData other)
        {
            return IsSameTarget(other) && shader == other.shader
                && SE.MaterialTextureOverride.ListEquals(textures, other.textures);
        }

        public TimelineMaterialShaderData Clone()
        {
            var clone = (TimelineMaterialShaderData)MemberwiseClone();
            // 要素は不変なので一覧だけ複製する
            clone.textures = new List<SE.MaterialTextureOverride>(textures);
            return clone;
        }

        public void FromXml(TimelineMaterialShaderXml xml)
        {
            maidSlotNo = xml.maidSlotNo;
            owner = xml.owner ?? "";
            material = xml.material ?? "";
            index = xml.index;
            shader = xml.shader ?? "";

            // フォルダ外を指す指定と、同じプロパティの 2 件目以降は読まない
            textures = new List<SE.MaterialTextureOverride>();
            if (xml.textures != null)
            {
                foreach (var texture in xml.textures)
                {
                    string file;
                    if (texture == null
                        || !SE.MaterialTextureCatalog.TryNormalize(texture.property, texture.file, out file)
                        || textures.Exists(t => t.property == texture.property))
                    {
                        continue;
                    }
                    textures.Add(new SE.MaterialTextureOverride(texture.property, file));
                }
            }
            SE.MaterialTextureOverride.Sort(textures);
        }

        public TimelineMaterialShaderXml ToXml()
        {
            var xml = new TimelineMaterialShaderXml
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material,
                index = index,
                // 空なら要素ごと書かない
                shader = shader.Length > 0 ? shader : null,
            };
            foreach (var texture in textures)
            {
                xml.textures.Add(new TimelineMaterialTextureXml { property = texture.property, file = texture.file });
            }
            return xml;
        }
    }
}
```

`TimelineData.cs:830-840` の絞り込みを直す:

```csharp
                // 対象が空か、シェーダーも有効なテクスチャも無いものは適用できないので読まない
                if (materialShader.material.Length > 0 && materialShader.hasChanges)
```

`MaterialShaderSync.cs` のクラス内に足す (クラスの summary を「タイムラインへ書くシェーダー・テクスチャ変更一覧の組み立て」に直す):

```csharp
        /// <summary>
        /// Material が作り直されて現在の状態から消えたエントリを保留へ戻す。
        /// 同じ対象が現在の状態か保留に既にあれば戻さない (そちらが新しい)。戻したら true
        /// </summary>
        public static bool Requeue(
            List<TimelineMaterialShaderData> pending,
            List<TimelineMaterialShaderData> live,
            TimelineMaterialShaderData lost)
        {
            if (live.Exists(e => e.IsSameTarget(lost)) || pending.Exists(e => e.IsSameTarget(lost)))
            {
                return false;
            }
            pending.Add(lost.Clone());
            return true;
        }
```

`MaterialShaderXmlTests.対象かシェーダーが空の項目はタイムラインに読まない` はテクスチャの無いエントリなので、そのまま通る。

- [ ] **Step 5: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "MaterialTextureXmlTests|MaterialShaderSyncTests|MaterialShaderXmlTests"` が PASS。`<Texture ... />` の直列化の文字列が合わないときは、属性の並び (`prop` → `file`) とフィールドの宣言順を先に見る。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs source/COM3D2.SceneEditor.Plugin.Tests/MaterialTextureXmlTests.cs source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderSyncTests.cs
git commit -m "feat(timeline): マテリアルのテクスチャ差し替えを MaterialShaders に保存する"
```

### Task 5: タイムラインとの同期・読込時の適用・作り直しの再適用

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:555`

**Interfaces:**
- Consumes: `ModelMaterial.isChanged` / `isReleased` / `GetTextureOverrides` / `SetTextureOverrides` / `ResetTextures` / `CollectChanged` / `changedVersion` / `ResetAllTextures` (Task 3)、`TimelineMaterialShaderData.textures` / `hasChanges`、`MaterialShaderSync.Requeue` (Task 4)
- Produces: `MaterialShaderManager.ResetChangesNotIn(List<TimelineMaterialShaderData>)` (旧 `ResetShadersNotIn`)

- [ ] **Step 1: 適用をエントリの内容に揃える**

`ApplyPending` のループ本体を置き換える:

```csharp
                var entry = _pending[i];
                var material = FindMaterial(entry);
                if (material == null)
                {
                    continue;
                }

                Shader shader = null;
                if (entry.shader.Length > 0)
                {
                    shader = ResolveShader(entry.shader);
                    if (shader == null)
                    {
                        // 保留に残して保存からは消さない (導入し直せば次の読込で効く)。
                        // テクスチャも新シェーダーのプロパティへ貼るものなので一緒に待つ
                        continue;
                    }
                }

                // エントリの内容に揃える (シェーダーが空なら元へ、載っていないテクスチャは外す)
                if (shader != null)
                {
                    material.ChangeShader(shader);
                }
                else
                {
                    material.ResetShader();
                }
                // 見つからないファイルは指定だけ残るので、ここで保留から外してよい
                material.SetTextureOverrides(entry.textures);
                _pending.RemoveAt(i);
                applied = true;
```

- [ ] **Step 2: エントリの作成を広げる**

`CreateEntry(int, string, List<ModelMaterial>, ModelMaterial)` を置き換える:

```csharp
        private static TimelineMaterialShaderData CreateEntry(
            int maidSlotNo, string owner, List<ModelMaterial> materials, ModelMaterial material)
        {
            var index = materials.IndexOf(material);
            if (index < 0)
            {
                return null;
            }
            var entry = new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material.displayName,
                index = index,
                shader = material.isShaderChanged ? material.material.shader.name : "",
            };
            material.GetTextureOverrides(entry.textures);
            return entry;
        }
```

- [ ] **Step 3: 作り直されたマテリアルのエントリを保留へ戻す**

フィールドを足す (`_names` の下):

```csharp
        /// <summary>前回の同期で保存対象だったマテリアルと、そのときの Material・エントリ</summary>
        private struct LiveRecord
        {
            public Material material;
            public TimelineMaterialShaderData entry;
        }

        // 着替え等で Material が作り直されたら、前回のエントリを保留へ戻して再適用を待つ (仕様 #7・13)
        private Dictionary<ModelMaterial, LiveRecord> _lastLive = new Dictionary<ModelMaterial, LiveRecord>();
        private Dictionary<ModelMaterial, LiveRecord> _nextLive = new Dictionary<ModelMaterial, LiveRecord>();
```

`SyncToTimeline` を置き換える:

```csharp
        private void SyncToTimeline()
        {
            ModelMaterial.CollectChanged(_changedMaterials);
            // Collect が破棄分を落とすと version が進むので、読むのは後
            _lastVersion = ModelMaterial.changedVersion;

            _live.Clear();
            _nextLive.Clear();
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null)
                {
                    _live.Add(entry);
                    _nextLive[material] = new LiveRecord { material = material.material, entry = entry };
                }
            }

            RequeueRebuilt();

            // 現在の状態が決まった対象の保留は、もう適用しない
            _pending.RemoveAll(p => _live.Exists(l => l.IsSameTarget(p)));

            var merged = MaterialShaderSync.Merge(_live, _pending);
            if (!MaterialShaderSync.ListEquals(merged, timeline.materialShaders))
            {
                timeline.materialShaders = merged;
            }
        }

        /// <summary>
        /// 前回は保存対象だったのに今回消えたマテリアルのうち、Material が破棄・差し替え・一覧から除去された
        /// (= 作り直された) ものは、前回のエントリを保留へ戻す。ユーザーの初期化やゲーム側の上書きは
        /// Material が同じまま変更だけ消えるので戻さない
        /// </summary>
        private void RequeueRebuilt()
        {
            foreach (var pair in _lastLive)
            {
                var material = pair.Key;
                if (_nextLive.ContainsKey(material))
                {
                    continue;
                }
                // 破棄済み同士は Unity の == で等しくなるため参照で比べる
                var rebuilt = material.material == null
                    || !ReferenceEquals(material.material, pair.Value.material)
                    || material.isReleased;
                if (rebuilt)
                {
                    MaterialShaderSync.Requeue(_pending, _live, pair.Value.entry);
                }
            }

            var swap = _lastLive;
            _lastLive = _nextLive;
            _nextLive = swap;
        }
```

`OnLoad` の `_pending.Clear();` の直後に `_lastLive.Clear();` を足す (前のタイムラインのエントリを戻さないため)。

- [ ] **Step 4: 読込前の戻しとシーン遷移を広げる**

`ResetShadersNotIn` を `ResetChangesNotIn` へ改名し、本体を次にする (summary の「シェーダー変更」を「シェーダー・テクスチャ変更」へ直す):

```csharp
        public void ResetChangesNotIn(List<TimelineMaterialShaderData> entries)
        {
            ModelMaterial.CollectChanged(_changedMaterials);
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null && !entries.Exists(e => e.IsSameTarget(entry)))
                {
                    material.ResetShader();
                    material.ResetTextures();
                }
            }
        }
```

`TimelineManager.cs:555` の呼び出しを `MaterialShaderManager.instance.ResetChangesNotIn(_timeline.materialShaders);` にし、直前のコメントを「前のタイムラインやプリセットのシェーダー・テクスチャ変更を、読んだタイムラインへ持ち込まない」にする。

`OnChangedSceneLevel` を置き換える:

```csharp
        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // 読み込んだテクスチャはシーン遷移で破棄する (タイムラインがあれば次の OnLoad が保留から再適用する)
            ModelMaterial.ResetAllTextures();
            // シーン遷移でメイド・モデルが入れ替わるため、保留も捨てる。
            // タイムラインを開いていないと Update が回らないので、破棄済みの登録もここで落とす
            ModelMaterial.CollectChanged(_changedMaterials);
            _changedMaterials.Clear();
            _pending.Clear();
            _lastLive.Clear();
            _lastTimeline = null;
            _lastVersion = -1;
        }
```

クラスの summary を「マテリアルのシェーダー・テクスチャ変更をタイムラインへ保存・復元する」に直し、「適用できないエントリ (モデル未ロード・シェーダー未導入) は保留」の後に「着替え等で作り直されたマテリアルのエントリも保留へ戻して再適用する」を足す。

- [ ] **Step 5: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。`rg -n "ResetShadersNotIn" source` が 0 件。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs
git commit -m "feat(timeline): テクスチャ差し替えを読込時に適用し、作り直されたマテリアルへ再適用する"
```

### Task 6: シーンプリセット・Undo・クリップボード

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:540-578` と v37 のコメント (:1145-1147)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:2356-2432`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialClipboard.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaterialTextureTests.cs`

**Interfaces:**
- Consumes: `ModelMaterial.GetTextureOverrides` / `ChangeTexture` / `SetTextureOverrides` (Task 3)、`MaterialTextureCatalog.TryNormalize`、`MaterialTextureOverride.ListEquals` (Task 1)
- Produces: `class ScenePresetMaterialTexture { [XmlAttribute] string prop; [XmlAttribute] string file; }`、`ScenePresetMaterial.textures`

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetMaterialTextureTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのマテリアルのテクスチャ差し替えを固定する</summary>
    public class ScenePresetMaterialTextureTests
    {
        private static string Serialize(ScenePresetMaterial material)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaterial));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, material);
                return writer.ToString();
            }
        }

        private static ScenePresetMaterial Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaterial));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaterial)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 差し替えが無ければ要素を書き出さない()
        {
            Assert.DoesNotContain("<texture", Serialize(new ScenePresetMaterial { owner = "body", material = "skin" }));
        }

        [Fact]
        public void テクスチャ差し替えを往復する()
        {
            var material = new ScenePresetMaterial { owner = "body", material = "skin" };
            material.textures.Add(new ScenePresetMaterialTexture { prop = "_ShadowRateToon", file = "Toon/0_影なし.png" });

            var text = Serialize(material);
            Assert.Contains("<texture prop=\"_ShadowRateToon\" file=\"Toon/0_影なし.png\" />", text);

            var restored = Deserialize(text);
            Assert.Single(restored.textures);
            Assert.Equal("Toon/0_影なし.png", restored.textures[0].file);
        }

        [Fact]
        public void テクスチャだけでも空ではない()
        {
            var material = new ScenePresetMaterial { owner = "body", material = "skin" };
            Assert.True(material.isEmpty);

            material.textures.Add(new ScenePresetMaterialTexture { prop = "_ToonRamp", file = "Toon/a.png" });
            Assert.False(material.isEmpty);
        }

        [Fact]
        public void 要素の無い旧プリセットはテクスチャなしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><ScenePresetMaterial owner=\"body\" material=\"skin\" index=\"0\" />");

            Assert.Empty(restored.textures);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetMaterialTextureTests`
Expected: コンパイルエラー (`ScenePresetMaterialTexture` / `textures` が無い)

- [ ] **Step 3: プリセットのデータを足す**

`ScenePresetData.cs` の `ScenePresetMaterialValue` の後に:

```csharp
    /// <summary>マテリアルのテクスチャ差し替え 1 件。file は Config\SceneEditor からの相対パス</summary>
    public class ScenePresetMaterialTexture
    {
        [XmlAttribute]
        public string prop;
        [XmlAttribute]
        public string file;
    }
```

`ScenePresetMaterial` の `values` の後に:

```csharp
        [XmlElement("texture")]
        public List<ScenePresetMaterialTexture> textures = new List<ScenePresetMaterialTexture>();
```

`isEmpty` に `&& (textures == null || textures.Count == 0)` を足す。

v37 のコメントの末尾に 1 行足す:

```csharp
        //      マテリアル差分にテクスチャ差し替え (texture 要素、prop / file 属性) を追加。無い場合はテクスチャを触らない
```

- [ ] **Step 4: 捕捉と適用**

`ScenePresetManager.CaptureMaterial` の `if (material.isShaderChanged) {...}` の後に:

```csharp
            var overrides = new List<MaterialTextureOverride>();
            material.GetTextureOverrides(overrides);
            foreach (var texture in overrides)
            {
                data.textures.Add(new ScenePresetMaterialTexture { prop = texture.property, file = texture.file });
            }
```

`ApplyMaterial` のシェーダーの処理の後 (色の前) に:

```csharp
            // テクスチャは記載分だけ差し替える (シェーダー属性と同じく、無い項目は触らない)
            if (state.textures != null)
            {
                foreach (var texture in state.textures)
                {
                    string file;
                    if (texture != null && MaterialTextureCatalog.TryNormalize(texture.prop, texture.file, out file))
                    {
                        material.ChangeTexture(texture.prop, file);
                    }
                }
            }
```

`ApplyMaterial` の summary を「保存されたシェーダー・テクスチャとプロパティだけを…」に直す。

- [ ] **Step 5: Undo のスナップショット**

`MaterialSnapshot.cs`: フィールド `private readonly List<MaterialTextureOverride> _textures = new List<MaterialTextureOverride>();` を足し、`Capture` の `_shader = ...` の後で `material.GetTextureOverrides(snapshot._textures);` を呼ぶ (オブジェクト初期化子の外、`snapshot` 生成直後)。

`Apply` のシェーダーを戻す処理の直後に:

```csharp
            // 見つからないファイルの指定も含めて記録時の状態へ揃える
            _material.SetTextureOverrides(_textures);
```

`Approximately` の最初の条件に `|| !MaterialTextureOverride.ListEquals(o._textures, _textures)` を足す。クラスの summary を「(シェーダー・テクスチャ・全色・数値プロパティと追跡チェック)」に直す。

- [ ] **Step 6: クリップボード**

`MaterialClipboard.cs`: フィールド `private static readonly List<MaterialTextureOverride> _textures = new List<MaterialTextureOverride>();`、`Copy` で `material.GetTextureOverrides(_textures);`。`Paste` のシェーダーを揃える処理の直後に:

```csharp
            // テクスチャもコピー元に揃える。シェーダーと同じく追跡の対象外なので applied には数えない
            if (material.material != null)
            {
                material.SetTextureOverrides(_textures);
            }
```

`GetTextureOverrides` は結果を `Clear` してから詰めるので、`Copy` の先頭で別途消さなくてよい。クラスの summary を「(シェーダー / テクスチャ / 色 / 数値プロパティ)」に直す。

- [ ] **Step 7: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs source/COM3D2.SceneEditor.Plugin/MaterialClipboard.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaterialTextureTests.cs
git commit -m "feat(material): テクスチャ差し替えをシーンプリセット・Undo・コピーに含める"
```

### Task 7: マテリアルウィンドウのテクスチャ行

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs` (フィールド :59-68、`DrawMaterialSelector` :300-321、`ApplyShader` :382-394)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs:155-166`

**Interfaces:**
- Consumes: `MaterialTextureCatalog.Properties` / `GetFolder` / `GetLabel` / `GetDisplayName`、`MaterialTextureFiles.GetChoices` / `Refresh`、`ModelMaterial.GetTextureFile` / `IsTextureMissing` / `ChangeTexture` / `ResetTextures`、`MaterialPropertyRowsDrawer.RecordEdit`

- [ ] **Step 1: 行の対象フィールドを一般化する**

`_shaderTarget` / `_shaderTrack` / `_shaderMaid` を `_editTarget` / `_editTrack` / `_editMaid` へ改名し、コメントを「シェーダー行・テクスチャ行の対象。コンボの選択確定はポップアップ側 (別フレーム) で起きるため、最後に描いた対象を控えて onSelected から引く」にする。代入は `DrawShaderRow` の先頭のまま (テクスチャ行はシェーダー行の後に描くので同じ対象)。

フィールドを足す:

```csharp
        // テクスチャ行のコンボ。行ごとに onSelected の対象プロパティが違うため、プロパティごとに持つ
        private readonly Dictionary<string, GUIComboBox<string>> _textureComboBoxes
            = new Dictionary<string, GUIComboBox<string>>();
```

- [ ] **Step 2: テクスチャ行を描く**

`DrawMaterialSelector` の `DrawShaderRow(material, track, maid);` の直後に `DrawTextureRows(material);` を足し、メソッドを足す (`ApplyShader` の後):

```csharp
        /// <summary>
        /// テクスチャの差し替え行。今のシェーダーが持つプロパティだけ出す。
        /// 候補は参照フォルダのファイルで、先頭の「(元)」で差し替え前へ戻る
        /// </summary>
        private void DrawTextureRows(MTEP.ModelMaterial material)
        {
            foreach (var property in MaterialTextureCatalog.Properties)
            {
                if (!material.material.HasProperty(property))
                {
                    continue;
                }

                var comboBox = GetTextureComboBox(property);
                var current = material.GetTextureFile(property) ?? "";
                var choices = MaterialTextureFiles.GetChoices(MaterialTextureCatalog.GetFolder(property));
                if (!choices.Contains(current))
                {
                    // 見つからない・フォルダから消えた指定も選択中として見せる (キャッシュは書き換えない)
                    choices = new List<string>(choices) { current };
                }
                comboBox.items = choices;
                comboBox.currentItem = current;

                DrawLabeledComboBox(MaterialTextureCatalog.GetLabel(property), comboBox, UpdateButtonWidth + view.margin, () =>
                {
                    if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                    {
                        MaterialTextureFiles.Refresh();
                    }
                });
            }
        }

        private GUIComboBox<string> GetTextureComboBox(string property)
        {
            GUIComboBox<string> comboBox;
            if (!_textureComboBoxes.TryGetValue(property, out comboBox))
            {
                comboBox = new GUIComboBox<string>
                {
                    getName = (file, _) => GetTextureDisplayName(property, file),
                    onSelected = (file, _) => ApplyTexture(property, file),
                };
                _textureComboBoxes[property] = comboBox;
            }
            return comboBox;
        }

        private string GetTextureDisplayName(string property, string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return "(元)";
            }
            var name = MaterialTextureCatalog.GetDisplayName(file);
            var target = _editTarget;
            if (target != null && target.GetTextureFile(property) == file && target.IsTextureMissing(property))
            {
                return name + " (見つかりません)";
            }
            return name;
        }

        private void ApplyTexture(string property, string file)
        {
            var material = _editTarget;
            if (material == null || material.material == null
                || (material.GetTextureFile(property) ?? "") == (file ?? ""))
            {
                return;
            }

            // テクスチャもキーではないので追跡チェックは付けない (タイムラインへは MaterialShaderManager が保存する)
            MaterialPropertyRowsDrawer.RecordEdit(material, _editTrack, _editMaid, "テクスチャ");
            material.ChangeTexture(property, file);
        }
```

- [ ] **Step 3: `初期化` でテクスチャも戻す**

`MaterialPropertyRowsDrawer.cs` の `初期化` の処理で `material.ResetShader();` の直後に `material.ResetTextures();` を足し、コメントを「シェーダーとテクスチャを先に戻す (値の初期値は元シェーダーのプロパティで控えている)」にする。

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs
git commit -m "feat(material): マテリアルウィンドウにテクスチャの差し替え行を追加する"
```

### Task 8: 実機確認

**Files:** なし (確認のみ。見つけた不具合は該当 Task のファイルを直して別コミット)

- [ ] **Step 1: restart-verify スキルで DLL を反映し、ゲームを起動してセーブをロードする** (再起動前にユーザーへ確認する)。配布用 `UnityInjector/Config/SceneEditor/Toon/` の 4 枚をゲームの `Sybaris\UnityInjector\Config\SceneEditor\Toon\` へコピーする。`Texture\` には手元の任意の `.png` と、Mod フォルダの `.tex` を 1 枚ずつ置く。デイリー画面でエディタを有効にして確認する (memory `verify-in-normal-scene`)
- [ ] **Step 2: devbridge で確認する** (プラグイン内部は memory `devbridge-plugin-reflection` の作法でリフレクション経由で触る)
  - マテリアルウィンドウのメイドタブで、肌 (`body` / `skin`) にテクスチャ・影テクスチャ・トゥーン・影の濃さの行が出て、ラベルが切れていないこと (`screenshot`。`影テクスチャ` / `輪郭トゥーン` が 70px に収まらなければ表示名を短くする)
  - `影の濃さ` に `0_影なし.png` を選ぶと肌の影が消え、`2_影標準.png` でゲームの元の見た目に近いこと、`(元)` で元のテクスチャ (`ToonSkin_Shadow`) へ戻ること。`eval_csharp` で `material.GetTexture("_ShadowRateToon").name` と `wrapMode == Clamp` を確認する
  - `Texture\` の `.tex` を `_MainTex` に選び、見た目が変わること (`.tex` の読み込み)
  - 同じファイルを選び直しても `GetTexture` の `GetInstanceID()` が変わらないこと。タイムラインを再生・シーク、キーの Undo/Redo をしても変わらないこと
  - Undo でテクスチャが戻り、Redo で再び差し替わること。マテリアルのコピー → 別マテリアルへのペーストでテクスチャも写ること。`初期化` で戻ること
  - シーンプリセットを保存 → `初期化` → 適用で差し替えが戻ること。XML に `<texture prop=... file=... />` が出ること
  - タイムラインを保存し、XML の `<MaterialShader>` に `<Texture prop=... file=... />` が出て、シェーダーを変えていなければ `<Shader>` が無いこと。`初期化` してからタイムラインを読み直すと差し替えが戻ること
  - `file` を存在しない名前に書き換えた XML を読み、例外にならず警告が 1 回だけ出て、行に `(見つかりません)` が出て、保存し直しても `<Texture>` が残ること
  - 同じアイテムの付け直し (`maid.SetProp(mpn, 同じ menu, 0, false, true)` → `AllProcPropSeqStart`、同名ファイルだと `boDut` が立たないので一度別アイテムを経由する) の後、30 フレーム以上待ってシェーダーとテクスチャが再適用され、タイムラインの `materialShaders` にエントリが残ること
  - 髪の色 (パーツ色) を変えるなどゲーム側が `_MainTex` を差し替えたら、こちらの差し替えが外れ (行が `(元)`)、読み込んだテクスチャが破棄されること (`UnityEngine.Object.FindObjectsOfType<Texture2D>()` から名前 `Texture/...` が消える)
  - シーン遷移 (デイリー → 別画面 → デイリー) で差し替えが元へ戻り、読み込んだテクスチャが破棄されること。タイムラインを開いていれば、戻った後に再適用されること
  - `tail_log` に SceneEditor 由来の例外が無いこと
- [ ] **Step 3: 後片付け**: 差し替えたマテリアルを `初期化` で戻す。ゲームフォルダの `Texture\` に置いた検証用ファイルを消す

### Task 9: ドキュメント

**Files:**
- Modify: `docs-site/guide/maid-editing.md` (`## マテリアルのシェーダー変更` の節の後)
- Modify: `docs-site/timeline/compatibility.md:21` の後
- Modify: `W:\COM3D2_5\work\CLAUDE.md` (リポジトリ外。「マテリアルのシェーダー変更」の行)

- [ ] **Step 1: ガイドに節を足す**

`maid-editing.md` の `## マテリアルのシェーダー変更` の節の後に:

```markdown
## マテリアルのテクスチャ差し替え

マテリアルウィンドウの `テクスチャ` / `影テクスチャ` / `トゥーン` / `影の濃さ` / `輪郭トゥーン` で、
選んだマテリアルのテクスチャを差し替えられます。行は今のシェーダーが持つものだけ出ます。

| 行 | 参照フォルダ |
|---|---|
| `テクスチャ` / `影テクスチャ` | `Sybaris\UnityInjector\Config\SceneEditor\Texture\` |
| `トゥーン` / `影の濃さ` / `輪郭トゥーン` | `Sybaris\UnityInjector\Config\SceneEditor\Toon\` |

- 候補はフォルダ (サブフォルダ含む) の `.png` と `.tex` です。ファイルを足したら `更新` を押してください。先頭の `(元)` か `初期化` で元のテクスチャに戻ります
- `Toon\` にはサンプルが入っています。`影の濃さ` に `0_影なし` を選ぶと影が消え、`1_影薄め` / `2_影標準` / `3_影濃いめ` で影の強さと範囲が変わります
- 保存されるのはフォルダからの相対パスです。フォルダごと渡せば、ほかの環境でも同じ見た目になります
- テクスチャの差し替えはシーンプリセットと Undo、マテリアルのコピー / ペーストに含まれます。タイムラインにはメイドと配置モデルの差し替えが保存され、読み込み時に反映されます。キーフレームでは切り替えられません
- ファイルが見つからないときは `(見つかりません)` と表示され、元のテクスチャのままになります。指定は保存し直しても残ります
- 髪や肌の色を変えるなど、ゲームがテクスチャを作り直すと差し替えは外れます
- シーンを移動すると差し替えは元に戻ります (タイムラインを開いていれば読み込み直されます)
```

同じ節の上の `## マテリアルのシェーダー変更` の「着替えるとマテリアルが作り直されるため、シェーダーは元に戻ります」を、次に置き換える:

```markdown
- 着替えるとマテリアルが作り直され、シェーダーはいったん元に戻ります。タイムラインを開いていれば、同じアイテムに戻したときにタイムラインの記録から再び反映されます
```

- [ ] **Step 2: `compatibility.md` に 1 行足す** (21 行目の後)

```markdown
- マテリアルの `テクスチャ` の差し替えはタイムラインの `MaterialShaders` に `Texture` 要素として保存されます。SceneEditor 独自の値で、MTE や以前の SceneEditor で読むと元のテクスチャで表示され、以前の SceneEditor で保存し直すと差し替えは消えます。参照ファイルが無い環境では適用されず、保存し直しても記録は残ります
```

- [ ] **Step 3: ワークスペースの `CLAUDE.md` の「マテリアルのシェーダー変更」の行を更新する**

行頭を「マテリアルのシェーダー・テクスチャ変更」にし、`/ `Shader`)` の後に次を挿入する:

```markdown
、テクスチャ差し替えは子要素 `<Texture prop=".." file=".."/>`（`file` は `Config\SceneEditor` からの相対パス、参照フォルダ外は読込時に捨てる）。シェーダーを変えていないエントリは `<Shader>` を書かない（旧 SE はシェーダーが空のエントリを読まない）。着替え等で Material が作り直されたエントリは保留へ戻して再適用する
```

末尾の「シーンプリセットは v37 で `ScenePresetMaterial` に `shader` 属性を追加」を「シーンプリセットは v37 で `ScenePresetMaterial` に `shader` 属性と `<texture prop file>` 要素を追加」にする。

- [ ] **Step 4: コミット** (CLAUDE.md はリポジトリ外なので含めない)

```bash
git add docs-site/guide/maid-editing.md docs-site/timeline/compatibility.md
git commit -m "docs(material): テクスチャ差し替えを説明する"
```

## レビュー却下メモ

- (plan-review の結果をここに追記する)

## レビュー却下メモ

- `TBody.ChangeTex` が一度 null を入れてから差し替える間の 1 フレームを UI が拾う懸念 — 見送り。検出は次フレーム以降の `MaterialShaderManager.Update` で行い、Task 8 の実機確認 (パーツ色の変更) で見る
