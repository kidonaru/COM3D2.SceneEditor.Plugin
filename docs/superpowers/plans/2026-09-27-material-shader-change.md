# マテリアルのシェーダー変更 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** マテリアルウィンドウでシェーダーを差し替えられるようにする（バニラ `CM3D2/*` と NPRShader の `com3d2mod/*`）。変更はシーンプリセット・Undo・マテリアルのコピーに含め、タイムラインにも保存して読込時に反映する（キーフレーム化はしない）。

**Architecture:** 差し替えは `material.shader` の代入だけで行い、`ModelMaterial` が元シェーダー・元 renderQueue・初期値を保持して「元へ戻す」を保証する。変更済みマテリアルは `ModelMaterial` の静的レジストリに載せ、MTE 側の新マネージャ `MaterialShaderManager` がそこからタイムラインの `<MaterialShaders>` を片方向同期する。読込時は同マネージャの `OnLoad` が適用し、解決できない（モデル未ロード・シェーダー未導入）エントリは保留として保持し、定期的に再試行する。

**Tech Stack:** C# (Unity 2022.3 / COM3D2 両ビルド), IMGUI (`GUIView` / `GUIComboBox`), XmlSerializer, xunit (net48)

**Spec:** 本計画の「仕様」節

## 仕様

- 背景 (実機で確認済み、COM3D2.5 / URP 無効 / Unity 2022.3.62f2)
  - `material.shader = shader` で差し替えれば描画は破綻しない（メイドの 16 マテリアルを NPR シェーダーへ切り替えて確認）。同名プロパティ（`_Color` / `_MainTex` / `_ToonRamp` 等）の値は引き継がれる
  - NPR シェーダー（`com3d2mod/Standard_NPRToon*`、40 種）は `Shader.Find` では見つからない。`Resources.FindObjectsOfTypeAll<Shader>()` には載る（NPRShader が Awake で AssetBundle の Material を読み込むため）
  - **シェーダーを代入すると `material.renderQueue` は新シェーダーの既定値に戻る**（例: `nip_02` の 3010 → NPR で 2000 → 元シェーダーへ戻しても 3000）
  - COM3D2.5 の `.model` は接線を必ず持つ（`ImportCM` が無ければ計算する）ので、NPRShader がやっている `RecalculateTangents` は不要
- 対象はマテリアルウィンドウの 3 タブ（メイド / モデル / 背景）すべて。Inspector（`TimelineItemInspector` のマテリアル行）にはシェーダー行を出さない
- シェーダー候補は `Resources.FindObjectsOfTypeAll<Shader>()` のうち名前が `CM3D2/` または `com3d2mod/` で始まり `isSupported` のもの。並びは `CM3D2/` → `com3d2mod/` の順で、各グループ内は名前の昇順。同名は先勝ち。対象マテリアルの元シェーダーと現在のシェーダーは候補に無くても必ず載せる。候補は初回表示時に作り、`更新` ボタンで作り直す（NPRShader を後から導入した場合など）
- 元シェーダーはコンボ上で ` (元)` を付けて表示する。元シェーダーを選べば元に戻る
- renderQueue: 差し替え前の値が差し替え前シェーダーの既定と違う（= マテリアルで明示指定されている）なら引き継ぎ、既定のままなら新シェーダーの既定に従う。元シェーダーへ戻すときは元の値を戻す
- 初期値: 元シェーダーで存在したプロパティの初期値は保持する。差し替えで初めて現れたプロパティ（NPR の `_MatcapValue` 等）は、最初に現れた時点の値を初期値にする（NPR → 別の NPR と続けて替えても更新しない）
- `初期化` ボタンはシェーダーも元に戻す。追跡チェックの OFF とタイムラインの `ModelMaterial.Reset()` は値だけを戻し、シェーダーは戻さない
- シェーダー変更は変更追跡（チェック）の対象にしない（キーではないため）
- 保存
  - タイムライン: ルートに SE 独自要素 `<MaterialShaders>`（`<MaterialShader>` の並び）。変更が無ければ書かない。`TimelineData.CurrentVersion` は上げない（v38 は未リリース）
    - メイド: `MaidSlotNo`（タイムラインのメイド番号）+ `Owner`（スロット名 = `MaidSlotStat.name`）
    - モデル: `MaidSlotNo` = -1 + `Owner`（タイムラインのモデル名 = `StudioModelStat.name`）
    - 共通: `Material`（Unity マテリアル名 = `ModelMaterial.displayName`）、`Index`（所有者内のマテリアル位置）、`Shader`（シェーダー名）
    - 背景タブの変更とタイムライン管理外のモデル・メイドは書かない（背景タブはもともとタイムラインの `背景モデルマテリアル` と対象集合が違うため）
    - 読込時（`mte.OnLoad`: 読込・新規作成・Undo/Redo の全再構築）に適用する。書かれていないマテリアルを元へ戻すことはしない
    - 適用できないエントリは保留として保持し、保存時にも書き出す（消さない）。30 フレームごとに再試行し、遅れて適用できたらマテリアル系レイヤーの現在フレームを適用し直す。シェーダーが見つからないエントリは警告を 1 回だけ出す
    - タイムラインの内容は「現在の変更済みマテリアル ∪ 保留」。同じ対象では現在の状態が勝ち、保留は捨てる
  - シーンプリセット: `ScenePresetMaterial` に属性 `shader`。変更が無ければ書かない。`ScenePresetData.CurrentVersion` は未リリースの 37 のまま、v37 のコメントに追記する。属性が無ければシェーダーは触らない
  - Undo: `MaterialSnapshot` にシェーダーを含める
  - マテリアルのコピー / ペースト: シェーダーも写す（ペースト先と違えば先に差し替えてから値を写す）
- シェーダー変更前に打ったマテリアルキーには、NPR 固有プロパティの値が 0 として入っている。変更後に再生すると 0 が適用されるため、キーは変更後に登録し直す（ドキュメントに書く。自動補正はしない）

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること（COM3D2 構成は Unity 5.6 の API に限る。`Shader.renderQueue` / `Material.renderQueue` / `Resources.FindObjectsOfTypeAll` / `Shader.isSupported` は 5.6 にもある）
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"` (COM3D2 構成は `/p:GameVersion=COM3D2`)。COM3D2 構成のビルドで COM3D25 の出力 DLL が消えるため、テスト前は COM3D25 を最後にビルドする。Git Bash から叩く場合は memory `msbuild-from-bash` の引数変換の注意に従う
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。テストで Unity ネイティブ呼び出し（`Shader.Find`、`new Material` 等）はできないので、純粋関数と XML だけをテストする
- 新規 .cs は csproj の `<Compile Include>` へ追加する
- NPRShader はコンパイル時参照もリフレクションもしない（シェーダーは `Resources.FindObjectsOfTypeAll` で拾う）
- タイムライン XML は MTE → SE の一方向互換。SE 独自要素は MTE で読み飛ばされる前提でよい
- 作業ブランチは `feat/material-shader`（main から切る。worktree は使わない）

## Review Focus

1. `<MaterialShaders>` を持たない旧タイムラインを読んで保存し直しても、XML に要素が増えない（Task 3 の `空なら書き出さない` テスト）
2. NPRShader 未導入の環境で NPR シェーダー名を含むタイムラインを読んでも例外にならず、エントリは保存し直しても消えない（Task 3 の `保留は現在の状態と重ならなければ残る` テスト + Task 8 の実機確認）
3. renderQueue を明示指定したマテリアル（例: 3010）が、別シェーダーへの往復で元の値に戻る（Task 1 の `MaterialRenderQueue` テスト + Task 8 の実機確認）
4. 着替えでマテリアルが破棄されたら、そのエントリはタイムラインから消える（破棄済みはレジストリから落とす。Task 2 の `CollectShaderChanged`、Task 8 の実機確認）
5. タイムラインのキー再生（`ModelMaterial.Reset()` 経由の値戻し含む）でシェーダーが戻らない（Task 2 で `Reset()` を値のみに保つ。Task 8 の実機確認）

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/ShaderCatalog.cs` | シェーダー候補の列挙・名前での検索 |
| Create `source/COM3D2.SceneEditor.Plugin/MaterialRenderQueue.cs` | 差し替え時の renderQueue の決定 |
| Create `source/COM3D2.SceneEditor.Plugin/MaterialLookup.cs` | 名前 + 位置でのマテリアル同定（プリセットとタイムラインで共用） |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs` | 元シェーダー保持・差し替え・変更済みレジストリ |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineMaterialShaderXml` と `<MaterialShaders>` |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs` | タイムライン上のシェーダー変更 1 件 |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs` | 現在の状態と保留の併合・比較 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `materialShaders` と XML 変換 |
| Create `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs` | 読込時の適用・保留の再試行・タイムラインへの同期 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` | マネージャの登録 |
| Modify `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetMaterial.shader`、v37 コメント |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 捕捉・適用、`FindMaterial` を `MaterialLookup` へ |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs` | シェーダーの記録・復元・比較 |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialClipboard.cs` | シェーダーのコピー / ペースト |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs` | `RecordEdit` の公開、`初期化` でシェーダーも戻す |
| Modify `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs` | シェーダー行 |
| Tests `ShaderCatalogTests.cs` / `MaterialRenderQueueTests.cs` / `MaterialLookupTests.cs` / `MaterialShaderXmlTests.cs` / `MaterialShaderSyncTests.cs` / `ScenePresetMaterialShaderTests.cs` | |
| Modify `docs-site/guide/maid-editing.md` / `docs-site/timeline/compatibility.md` / `W:\COM3D2_5\work\CLAUDE.md` | ドキュメント |

---

### Task 0: ブランチを切る

- [ ] **Step 1:** `git switch -c feat/material-shader`（`main` がクリーンであることを確認してから）

---

### Task 1: シェーダー候補・renderQueue・マテリアル同定の純粋関数

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/ShaderCatalog.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialRenderQueue.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaterialLookup.cs`
- Modify: csproj（`<Compile Include="LightShadowValues.cs" />` の直後に 3 行）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`FindMaterial`）
- Test: `ShaderCatalogTests.cs` / `MaterialRenderQueueTests.cs` / `MaterialLookupTests.cs`

**Interfaces:**
- Produces（namespace `COM3D2.SceneEditor.Plugin`）:
  - `static class ShaderCatalog { List<string> FilterNames(IEnumerable<string> names); List<Shader> GetShaders(); Shader Find(string name); }`
  - `static class MaterialRenderQueue { int Resolve(int currentQueue, int currentShaderQueue, int newShaderQueue); }`
  - `static class MaterialLookup { int FindIndex(IList<string> names, string name, int index); }`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ShaderCatalogTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェーダー候補の絞り込みと並びを固定する</summary>
    public class ShaderCatalogTests
    {
        [Fact]
        public void 対象の接頭辞だけを残す()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "CM3D2/Toony_Lighted", "com3d2mod/Standard_NPRToon_", "Standard", "Skybox/Cubemap", "Hidden/Foo",
            });

            Assert.Equal(new[] { "CM3D2/Toony_Lighted", "com3d2mod/Standard_NPRToon_" }, result);
        }

        [Fact]
        public void バニラを先にしてグループ内は名前順に並べる()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "com3d2mod/Standard_NPRToonV2_Lit_", "CM3D2/Toony_Lighted_Outline",
                "com3d2mod/Standard_NPRToon_", "CM3D2/Lighted",
            });

            Assert.Equal(new[]
            {
                "CM3D2/Lighted", "CM3D2/Toony_Lighted_Outline",
                "com3d2mod/Standard_NPRToonV2_Lit_", "com3d2mod/Standard_NPRToon_",
            }, result);
        }

        [Fact]
        public void 同名は1件にまとめる()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "CM3D2/Toony_Lighted_Outline_Tex", "CM3D2/Toony_Lighted_Outline_Tex",
            });

            Assert.Equal(new[] { "CM3D2/Toony_Lighted_Outline_Tex" }, result);
        }

        [Fact]
        public void 空やnullの名前は捨てる()
        {
            Assert.Empty(ShaderCatalog.FilterNames(new string[] { null, "" }));
        }
    }
}
```

注: 並びは `string.CompareOrdinal`。`"com3d2mod/Standard_NPRToonV2_Lit_"` と `"com3d2mod/Standard_NPRToon_"` は `'V'`(0x56) < `'_'`(0x5F) で V2 が先になる。

`source/COM3D2.SceneEditor.Plugin.Tests/MaterialRenderQueueTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェーダー差し替え時の renderQueue の決め方を固定する</summary>
    public class MaterialRenderQueueTests
    {
        [Fact]
        public void 明示指定のキューは引き継ぐ()
        {
            Assert.Equal(3010, MaterialRenderQueue.Resolve(3010, 3000, 2000));
        }

        [Fact]
        public void シェーダー既定のままなら新シェーダーの既定に従う()
        {
            Assert.Equal(2000, MaterialRenderQueue.Resolve(3000, 3000, 2000));
            Assert.Equal(3000, MaterialRenderQueue.Resolve(2000, 2000, 3000));
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin.Tests/MaterialLookupTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>保存された名前 + 位置からのマテリアル同定を固定する</summary>
    public class MaterialLookupTests
    {
        private static readonly string[] Names = { "skin", "cloth", "skin" };

        [Fact]
        public void 位置と名前が一致すればその位置を返す()
        {
            Assert.Equal(2, MaterialLookup.FindIndex(Names, "skin", 2));
        }

        [Fact]
        public void 位置の名前が違えば先頭の名前一致を返す()
        {
            Assert.Equal(0, MaterialLookup.FindIndex(Names, "skin", 1));
        }

        [Fact]
        public void 位置が範囲外なら先頭の名前一致を返す()
        {
            Assert.Equal(1, MaterialLookup.FindIndex(Names, "cloth", 9));
            Assert.Equal(0, MaterialLookup.FindIndex(Names, "skin", -1));
        }

        [Fact]
        public void 名前が無ければマイナス1()
        {
            Assert.Equal(-1, MaterialLookup.FindIndex(Names, "hair", 0));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ShaderCatalog|FullyQualifiedName~MaterialRenderQueue|FullyQualifiedName~MaterialLookup"`
Expected: コンパイルエラー（3 クラスが無い）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/ShaderCatalog.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアルのシェーダー変更で選べるシェーダーの一覧。
    /// NPRShader のシェーダーは AssetBundle 由来で Shader.Find に載らないため、
    /// 読み込み済みの Shader 全体から名前の接頭辞で拾う (NPRShader への参照は持たない)
    /// </summary>
    public static class ShaderCatalog
    {
        /// <summary>候補にする接頭辞。並びがそのまま一覧のグループ順になる (バニラ → NPRShader)</summary>
        private static readonly string[] Prefixes = { "CM3D2/", "com3d2mod/" };

        /// <summary>接頭辞で絞り、重複を除き、接頭辞の順 → 名前順に並べる</summary>
        public static List<string> FilterNames(IEnumerable<string> names)
        {
            var groups = new List<string>[Prefixes.Length];
            for (var i = 0; i < groups.Length; i++)
            {
                groups[i] = new List<string>();
            }

            var seen = new HashSet<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue;
                }
                for (var i = 0; i < Prefixes.Length; i++)
                {
                    if (name.StartsWith(Prefixes[i], System.StringComparison.Ordinal))
                    {
                        groups[i].Add(name);
                        break;
                    }
                }
            }

            var result = new List<string>();
            foreach (var group in groups)
            {
                group.Sort(string.CompareOrdinal);
                result.AddRange(group);
            }
            return result;
        }

        /// <summary>読み込み済みで使えるシェーダーを一覧順に返す。同名は先勝ち</summary>
        public static List<Shader> GetShaders()
        {
            var byName = new Dictionary<string, Shader>();
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (shader != null && shader.isSupported && !byName.ContainsKey(shader.name))
                {
                    byName[shader.name] = shader;
                }
            }

            var result = new List<Shader>();
            foreach (var name in FilterNames(byName.Keys))
            {
                result.Add(byName[name]);
            }
            return result;
        }

        /// <summary>保存されたシェーダー名から引く。見つからなければ null</summary>
        public static Shader Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var shader = Shader.Find(name);
            if (shader != null)
            {
                return shader;
            }

            // NPRShader のシェーダーは Shader.Find では見つからない
            foreach (var loaded in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (loaded != null && loaded.name == name)
                {
                    return loaded;
                }
            }
            return null;
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin/MaterialRenderQueue.cs`:

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シェーダー差し替え時の renderQueue。Unity はシェーダーを代入すると
    /// renderQueue を新シェーダーの既定へ戻すため、差し替え後に決め直す
    /// </summary>
    public static class MaterialRenderQueue
    {
        /// <summary>
        /// 差し替え前の値が差し替え前シェーダーの既定と違えばマテリアルで明示指定されたものとして引き継ぎ、
        /// 既定のままなら新シェーダーの既定に従う (不透明 ↔ 半透明の切り替えで描画順も追従させる)
        /// </summary>
        public static int Resolve(int currentQueue, int currentShaderQueue, int newShaderQueue)
        {
            return currentQueue != currentShaderQueue ? currentQueue : newShaderQueue;
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin/MaterialLookup.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>保存されたマテリアル名 + 位置から、現在の一覧の位置を引く</summary>
    public static class MaterialLookup
    {
        /// <summary>
        /// 同名マテリアルが複数ある場合に備え、保存時の位置の名前一致を優先し、無ければ先頭の名前一致。
        /// 見つからなければ -1
        /// </summary>
        public static int FindIndex(IList<string> names, string name, int index)
        {
            if (index >= 0 && index < names.Count && names[index] == name)
            {
                return index;
            }
            return names.IndexOf(name);
        }
    }
}
```

csproj（`<Compile Include="LightShadowValues.cs" />` の直後）:

```xml
    <Compile Include="MaterialLookup.cs" />
    <Compile Include="MaterialRenderQueue.cs" />
    <Compile Include="ShaderCatalog.cs" />
```

- [ ] **Step 4: `ScenePresetManager.FindMaterial` を `MaterialLookup` へ寄せる**

`ScenePresetManager.cs` の `FindMaterial` を次に置き換える（振る舞いは同じ）:

```csharp
        /// <summary>
        /// 保存されたマテリアルを一覧から同定する。
        /// 同名マテリアルが複数ある場合に備え、保存時インデックスの名前一致を優先し、無ければ先頭の名前一致
        /// </summary>
        private static MTEP.ModelMaterial FindMaterial(List<MTEP.ModelMaterial> materials, ScenePresetMaterial state)
        {
            var names = new List<string>(materials.Count);
            foreach (var material in materials)
            {
                names.Add(material.displayName);
            }
            var index = MaterialLookup.FindIndex(names, state.material, state.index);
            return index >= 0 ? materials[index] : null;
        }
```

- [ ] **Step 5: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS（新規 10 件を含む）

- [ ] **Step 6: コミット**（例: `feat(material): シェーダー候補の列挙と差し替え用の補助関数を追加する`）

---

### Task 2: `ModelMaterial` のシェーダー差し替え

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterialController.cs`（`materials` ゲッターの切り詰め）

**Interfaces:**
- Consumes: `MaterialRenderQueue.Resolve`
- Produces（`COM3D2.MotionTimelineEditor.Plugin.ModelMaterial`）:
  - `Shader originalShader { get; }` / `bool isShaderChanged { get; }`
  - `void ChangeShader(Shader shader)` / `void ResetShader()` / `void Release()`
  - `static int shaderChangedVersion { get; }` / `static void CollectShaderChanged(List<ModelMaterial> result)`
  - `Reset()` は従来どおり値だけを戻す（シェーダーは戻さない）

単体テストは Unity ネイティブを呼ぶので書けない。Task 8 で実機確認する。

- [ ] **Step 1: using とフィールドを足す**

ファイル先頭の using に `using SE = COM3D2.SceneEditor.Plugin;` を足す（`TimelineData.cs` と同じ別名）。

`private List<float> initialValues = new List<float>();` の後に:

```csharp

        // 初期値を控え済みのプロパティ。シェーダー差し替えで初めて現れたプロパティは
        // その時点の値を初期値にし、元シェーダーから引き継いだものは元の初期値を保つ
        private HashSet<ColorPropertyType> capturedColors = new HashSet<ColorPropertyType>();
        private HashSet<ValuePropertyType> capturedValues = new HashSet<ValuePropertyType>();

        /// <summary>元のシェーダー。Init で掴み、ChangeShader では変えない</summary>
        public Shader originalShader { get; private set; }

        // 元シェーダーへ戻すときの renderQueue。シェーダーを代入すると既定値へ戻るため控える
        private int originalRenderQueue;

        public bool isShaderChanged
            => material != null && originalShader != null && material.shader != originalShader;

        // シェーダーを変えたマテリアル。タイムラインへの同期 (MaterialShaderManager) が全メイド・全モデルを
        // 走査せずに済むよう、変更と戻しのたびに出し入れする
        private static readonly HashSet<ModelMaterial> shaderChangedMaterials = new HashSet<ModelMaterial>();

        /// <summary>shaderChangedMaterials の出し入れで増える。同期側の変更検出に使う</summary>
        public static int shaderChangedVersion { get; private set; }
```

- [ ] **Step 2: `Init` を書き換える**

`Init()` 全体を次に置き換える:

```csharp
        public void Init()
        {
            originalShader = material.shader;
            originalRenderQueue = material.renderQueue;

            capturedColors.Clear();
            capturedValues.Clear();
            initialColors.Clear();
            initialValues.Clear();
            for (int i = 0; i < ColorPropertyNameIds.Count; i++)
            {
                initialColors.Add(Color.black);
            }
            for (int i = 0; i < ValuePropertyNameIds.Count; i++)
            {
                initialValues.Add(0f);
            }

            RefreshProperties();
            UpdateShaderChangedRegistry();

            material.name = material.name.Replace(" (Instance)", "");
        }

        /// <summary>
        /// 現在のシェーダーが持つプロパティを数え直す。
        /// 初期値は初めて現れたプロパティだけ現在値で控え、既知のものは元の初期値を保つ
        /// </summary>
        private void RefreshProperties()
        {
            hasColorProperties.Clear();
            hasValueProperties.Clear();

            for (int i = 0; i < ColorPropertyNameIds.Count; i++)
            {
                var nameId = ColorPropertyNameIds[i];
                if (!material.HasProperty(nameId))
                {
                    continue;
                }
                var type = (ColorPropertyType)i;
                hasColorProperties.Add(type);
                if (capturedColors.Add(type))
                {
                    initialColors[i] = material.GetColor(nameId);
                }
            }

            for (int i = 0; i < ValuePropertyNameIds.Count; i++)
            {
                var nameId = ValuePropertyNameIds[i];
                if (!material.HasProperty(nameId))
                {
                    continue;
                }
                var type = (ValuePropertyType)i;
                hasValueProperties.Add(type);
                if (capturedValues.Add(type))
                {
                    initialValues[i] = material.GetFloat(nameId);
                }
            }
        }
```

- [ ] **Step 3: 差し替え・戻し・レジストリを足す**（`Reset()` の直前）

```csharp
        /// <summary>
        /// シェーダーを差し替える。同名プロパティの値は Unity が引き継ぐ。
        /// renderQueue は代入で既定値へ戻るため、元シェーダーへ戻すなら元の値、
        /// それ以外は MaterialRenderQueue の規則で決め直す
        /// </summary>
        public void ChangeShader(Shader shader)
        {
            if (material == null || shader == null || material.shader == shader)
            {
                return;
            }

            var currentQueue = material.renderQueue;
            var currentShaderQueue = material.shader.renderQueue;
            material.shader = shader;
            material.renderQueue = shader == originalShader
                ? originalRenderQueue
                : SE.MaterialRenderQueue.Resolve(currentQueue, currentShaderQueue, shader.renderQueue);

            RefreshProperties();
            UpdateShaderChangedRegistry();
        }

        /// <summary>元のシェーダーへ戻す。値は戻さない (値は Reset が戻す)</summary>
        public void ResetShader()
        {
            ChangeShader(originalShader);
        }

        private void UpdateShaderChangedRegistry()
        {
            var changed = isShaderChanged
                ? shaderChangedMaterials.Add(this)
                : shaderChangedMaterials.Remove(this);
            if (changed)
            {
                shaderChangedVersion++;
            }
        }

        /// <summary>
        /// シェーダーを変えたマテリアルを result へ写す。
        /// 着替え・モデル削除で破棄されたものはここで落とす (落としたら version も進める)
        /// </summary>
        public static void CollectShaderChanged(List<ModelMaterial> result)
        {
            result.Clear();
            var removed = shaderChangedMaterials.RemoveWhere(
                m => m.material == null || m.controller == null);
            if (removed > 0)
            {
                shaderChangedVersion++;
            }
            result.AddRange(shaderChangedMaterials);
        }
```

一覧から外されたマテリアル用に、レジストリから抜く口を足す（`CollectShaderChanged` の後）:

```csharp

        /// <summary>
        /// コントローラの一覧から外されたときに呼ぶ。Material も controller も生きたままなので
        /// CollectShaderChanged の破棄検出では落ちず、ここで抜かないとレジストリに残り続ける
        /// </summary>
        public void Release()
        {
            if (shaderChangedMaterials.Remove(this))
            {
                shaderChangedVersion++;
            }
        }
```

`Reset()` には手を入れない（doc コメントを 1 行足すだけ）:

```csharp
        /// <summary>値を初期値へ戻す。シェーダーは戻さない (タイムラインのキー操作からも呼ばれるため)</summary>
        public void Reset()
```

- [ ] **Step 4: 一覧から外れたマテリアルをレジストリから抜く**（`Timeline/ModelMaterialController.cs`）

`materials` ゲッターの末尾の切り詰めを次に置き換える（着替えでマテリアル数が減ったとき、外れた側がシェーダー変更済みだとレジストリに残り続けるため）:

```csharp
                    if (_materials.Count > baseMaterials.Length)
                    {
                        for (int i = baseMaterials.Length; i < _materials.Count; i++)
                        {
                            _materials[i].Release();
                        }
                        _materials.RemoveRange(baseMaterials.Length, _materials.Count - baseMaterials.Length);
                    }
```

- [ ] **Step 5: 両構成でビルドし、テストを流す**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: エラー 0、全件 PASS

- [ ] **Step 6: コミット**（例: `feat(material): ModelMaterial にシェーダーの差し替えと元への戻しを追加する`）

---

### Task 3: タイムラインのデータと XML

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs`
- Modify: csproj（`<Compile Include="Timeline\ModelMaterial.cs" />` の前後の Timeline 直下の並びに 2 行）
- Test: `MaterialShaderXmlTests.cs` / `MaterialShaderSyncTests.cs`

**Interfaces:**
- Produces（namespace `COM3D2.MotionTimelineEditor.Plugin`）:
  - `class TimelineMaterialShaderXml { int maidSlotNo; string owner; string material; int index; string shader; }`
  - `TimelineXml.materialShaders (List<TimelineMaterialShaderXml>)`、空なら書き出さない
  - `class TimelineMaterialShaderData { int maidSlotNo = -1; string owner; string material; int index; string shader; bool IsSameTarget(TimelineMaterialShaderData); bool ContentEquals(TimelineMaterialShaderData); TimelineMaterialShaderData Clone(); void FromXml(TimelineMaterialShaderXml); TimelineMaterialShaderXml ToXml(); }`
  - `static class MaterialShaderSync { List<TimelineMaterialShaderData> Merge(List<TimelineMaterialShaderData> live, List<TimelineMaterialShaderData> pending); bool ListEquals(List<TimelineMaterialShaderData> a, List<TimelineMaterialShaderData> b); }`
  - `TimelineData.materialShaders (List<TimelineMaterialShaderData>)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderXmlTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインのシェーダー変更 (&lt;MaterialShaders&gt;) の保存と読込を固定する。
    /// 変更が無ければ書き出さず、要素の無い旧 XML は変更なしとして読む
    /// </summary>
    public class MaterialShaderXmlTests
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

        private static TimelineMaterialShaderData Maid()
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = 0, owner = "wear", material = "Dress590_onep", index = 1,
                shader = "com3d2mod/Standard_NPRToonV2_Lit_",
            };
        }

        [Fact]
        public void 変更が無ければ書き出さない()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };

            Assert.DoesNotContain("MaterialShaders", Serialize(xml));
        }

        [Fact]
        public void 要素の無い旧XMLは変更なしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><TimelineData version=\"38\" />");

            Assert.Empty(restored.materialShaders);
        }

        [Fact]
        public void シェーダー変更を往復する()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(Maid().ToXml());

            var text = Serialize(xml);
            Assert.Contains("<Shader>com3d2mod/Standard_NPRToonV2_Lit_</Shader>", text);

            var restored = new TimelineMaterialShaderData();
            restored.FromXml(Deserialize(text).materialShaders[0]);
            Assert.True(Maid().ContentEquals(restored));
        }

        [Fact]
        public void 読込時に名前の欠けた項目は空文字で読む()
        {
            var restored = new TimelineMaterialShaderData();
            restored.FromXml(new TimelineMaterialShaderXml { maidSlotNo = -1 });

            Assert.Equal("", restored.owner);
            Assert.Equal("", restored.material);
            Assert.Equal("", restored.shader);
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderSyncTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>現在のシェーダー変更と未適用の保留の併合を固定する</summary>
    public class MaterialShaderSyncTests
    {
        private static TimelineMaterialShaderData Entry(
            int maidSlotNo, string owner, string material, int index, string shader)
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo, owner = owner, material = material, index = index, shader = shader,
            };
        }

        [Fact]
        public void 同じ対象では現在の状態が勝つ()
        {
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "CM3D2/Lighted") };
            var pending = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "com3d2mod/X") };

            var merged = MaterialShaderSync.Merge(live, pending);

            Assert.Single(merged);
            Assert.Equal("CM3D2/Lighted", merged[0].shader);
        }

        [Fact]
        public void 保留は現在の状態と重ならなければ残る()
        {
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "CM3D2/Lighted") };
            var pending = new List<TimelineMaterialShaderData> { Entry(-1, "Model1", "m", 0, "com3d2mod/X") };

            var merged = MaterialShaderSync.Merge(live, pending);

            Assert.Equal(2, merged.Count);
        }

        [Fact]
        public void メイド番号_所有者_マテリアル_位置の順に並べる()
        {
            var live = new List<TimelineMaterialShaderData>
            {
                Entry(1, "wear", "a", 0, "s"),
                Entry(0, "wear", "b", 0, "s"),
                Entry(0, "wear", "a", 1, "s"),
                Entry(-1, "Model1", "m", 0, "s"),
                Entry(0, "body", "z", 0, "s"),
            };

            var merged = MaterialShaderSync.Merge(live, new List<TimelineMaterialShaderData>());

            Assert.Equal(-1, merged[0].maidSlotNo);
            Assert.Equal("body", merged[1].owner);
            Assert.Equal("a", merged[2].material);
            Assert.Equal("b", merged[3].material);
            Assert.Equal(1, merged[4].maidSlotNo);
        }

        [Fact]
        public void 並びと中身が同じなら等しい()
        {
            var a = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "s") };
            var b = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "s") };
            var c = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "t") };

            Assert.True(MaterialShaderSync.ListEquals(a, b));
            Assert.False(MaterialShaderSync.ListEquals(a, c));
            Assert.False(MaterialShaderSync.ListEquals(a, new List<TimelineMaterialShaderData>()));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~MaterialShader"`
Expected: コンパイルエラー（`TimelineMaterialShaderData` が無い）

- [ ] **Step 3: XML 型を足す**（`TimelineXml.cs` の `TimelineExtendBoneXml` の後）

```csharp

    /// <summary>
    /// マテリアルのシェーダー変更 1 件 (SE 独自)。メイドは MaidSlotNo + スロット名、
    /// モデルは MaidSlotNo = -1 + タイムラインのモデル名で所有者を表す
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
    }
```

`TimelineXml` の `extendBones` の後に:

```csharp

        // シェーダー変更は SE 独自。変更が無いタイムラインの XML を変えないよう、空なら書き出さない
        [XmlArray("MaterialShaders")]
        [XmlArrayItem("MaterialShader")]
        public List<TimelineMaterialShaderXml> materialShaders = new List<TimelineMaterialShaderXml>();

        public bool ShouldSerializematerialShaders() { return materialShaders != null && materialShaders.Count > 0; }
```

- [ ] **Step 4: データ型を作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineMaterialShaderData.cs`:

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインに保存するマテリアルのシェーダー変更 1 件。
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

        public string shader = "";

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
            return IsSameTarget(other) && shader == other.shader;
        }

        public TimelineMaterialShaderData Clone()
        {
            return (TimelineMaterialShaderData)MemberwiseClone();
        }

        public void FromXml(TimelineMaterialShaderXml xml)
        {
            maidSlotNo = xml.maidSlotNo;
            owner = xml.owner ?? "";
            material = xml.material ?? "";
            index = xml.index;
            shader = xml.shader ?? "";
        }

        public TimelineMaterialShaderXml ToXml()
        {
            return new TimelineMaterialShaderXml
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material,
                index = index,
                shader = shader,
            };
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>タイムラインへ書くシェーダー変更一覧の組み立て</summary>
    public static class MaterialShaderSync
    {
        /// <summary>
        /// 現在の変更済みマテリアルと、まだ適用できていない保留を併せる。
        /// 同じ対象では現在の状態が勝つ。並びは保存の度に揺れないよう
        /// メイド番号 → 所有者 → マテリアル → 位置で固定する
        /// </summary>
        public static List<TimelineMaterialShaderData> Merge(
            List<TimelineMaterialShaderData> live, List<TimelineMaterialShaderData> pending)
        {
            var result = new List<TimelineMaterialShaderData>(live.Count + pending.Count);
            result.AddRange(live);
            foreach (var entry in pending)
            {
                if (!result.Exists(e => e.IsSameTarget(entry)))
                {
                    result.Add(entry);
                }
            }

            result.Sort((a, b) =>
            {
                var compare = a.maidSlotNo.CompareTo(b.maidSlotNo);
                if (compare == 0)
                {
                    compare = string.CompareOrdinal(a.owner, b.owner);
                }
                if (compare == 0)
                {
                    compare = string.CompareOrdinal(a.material, b.material);
                }
                if (compare == 0)
                {
                    compare = a.index.CompareTo(b.index);
                }
                return compare;
            });
            return result;
        }

        public static bool ListEquals(List<TimelineMaterialShaderData> a, List<TimelineMaterialShaderData> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].ContentEquals(b[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
```

csproj（Timeline 直下の並び、`<Compile Include="Timeline\ModelMaterial.cs" />` の直後）:

```xml
    <Compile Include="Timeline\MaterialShaderSync.cs" />
    <Compile Include="Timeline\TimelineMaterialShaderData.cs" />
```

- [ ] **Step 5: `TimelineData` に保持と変換を足す**

- フィールド: `extendBoneNamesMap` の宣言の後に

```csharp

        /// <summary>マテリアルのシェーダー変更。MaterialShaderManager が現在の状態から同期する</summary>
        public List<TimelineMaterialShaderData> materialShaders = new List<TimelineMaterialShaderData>();
```

- `FromXml`: `extendBoneNamesMap` を埋めるループの後に

```csharp

            materialShaders = new List<TimelineMaterialShaderData>(xml.materialShaders.Count);
            foreach (var materialShaderXml in xml.materialShaders)
            {
                var materialShader = new TimelineMaterialShaderData();
                materialShader.FromXml(materialShaderXml);
                // 対象かシェーダーが空のものは適用できないので読まない
                if (materialShader.material.Length > 0 && materialShader.shader.Length > 0)
                {
                    materialShaders.Add(materialShader);
                }
            }
```

- `ToXml`: `xml.extendBones` を埋めるループの後に

```csharp

            xml.materialShaders = new List<TimelineMaterialShaderXml>(materialShaders.Count);
            foreach (var materialShader in materialShaders)
            {
                xml.materialShaders.Add(materialShader.ToXml());
            }
```

- [ ] **Step 6: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS（新規 8 件を含む）

- [ ] **Step 7: コミット**（例: `feat(timeline): マテリアルのシェーダー変更を保存するデータと XML を追加する`）

---

### Task 4: タイムラインとの同期と読込時の適用

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（`TimelineUpdateManager._managers`）
- Modify: csproj（`<Compile Include="Timeline\Manager\StudioLightManager.cs" />` の前に 1 行。Timeline\Manager 内の並びに合わせる）

**Interfaces:**
- Consumes: Task 2 の `ModelMaterial` API、Task 3 の `TimelineMaterialShaderData` / `MaterialShaderSync` / `TimelineData.materialShaders`、Task 1 の `ShaderCatalog.Find` / `MaterialLookup.FindIndex`
- Produces: `MTEP.MaterialShaderManager.instance`（`ManagerBase`）

- [ ] **Step 1: マネージャを作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// マテリアルのシェーダー変更をタイムラインへ保存・復元する。
    /// 保存は変更済みマテリアル (ModelMaterial のレジストリ) からの片方向同期で、
    /// 復元は読込時 (mte.OnLoad) の 1 回だけ行う。キーフレームは持たない。
    /// 適用できないエントリ (モデル未ロード・シェーダー未導入) は保留として持ち、
    /// 保存にも残しつつ定期的に再試行する
    /// </summary>
    public class MaterialShaderManager : ManagerBase
    {
        private static MaterialShaderManager _instance;
        public static MaterialShaderManager instance
            => _instance ?? (_instance = new MaterialShaderManager());

        private MaterialShaderManager()
        {
        }

        /// <summary>保留の再試行と、破棄されたマテリアルの掃除の間隔 (フレーム)</summary>
        private const int RetryInterval = 30;

        /// <summary>遅れて適用できたときに現在フレームを適用し直すレイヤー</summary>
        private static readonly Type[] MaterialLayerTypes =
        {
            typeof(MaidMaterialTimelineLayer),
            typeof(ModelMaterialTimelineLayer),
        };

        private readonly List<TimelineMaterialShaderData> _pending = new List<TimelineMaterialShaderData>();

        // 同期の作業用。使い回してゴミを出さない
        private readonly List<ModelMaterial> _changedMaterials = new List<ModelMaterial>();
        private readonly List<TimelineMaterialShaderData> _live = new List<TimelineMaterialShaderData>();
        private readonly List<string> _names = new List<string>();

        // 見つからないシェーダーの警告は名前ごとに 1 回
        private readonly HashSet<string> _warnedShaders = new HashSet<string>();

        private TimelineData _lastTimeline;
        private int _lastVersion = -1;
        private int _frameCount;

        public override void OnLoad()
        {
            _lastTimeline = timeline;
            _pending.Clear();
            foreach (var entry in timeline.materialShaders)
            {
                _pending.Add(entry.Clone());
            }

            // この後の LayerInit / CreateAndApplyAnmAll より前に差し替えるので、
            // NPR 固有プロパティのキーも最初の適用から効く
            ApplyPending();
            _lastVersion = -1;
        }

        public override void Update()
        {
            if (timeline == null)
            {
                return;
            }
            if (timeline != _lastTimeline)
            {
                OnLoad();
            }

            _frameCount++;
            var isRetryFrame = _frameCount >= RetryInterval;
            if (isRetryFrame)
            {
                _frameCount = 0;
                if (_pending.Count > 0 && ApplyPending())
                {
                    ReapplyMaterialLayers();
                }
            }

            // 破棄されたマテリアルは version を動かさないので、再試行と同じ間隔で拾いに行く
            if (!isRetryFrame && ModelMaterial.shaderChangedVersion == _lastVersion)
            {
                return;
            }
            SyncToTimeline();
        }

        /// <summary>保留を適用できたものから外す。1 件でも適用したら true</summary>
        private bool ApplyPending()
        {
            var applied = false;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                var material = FindMaterial(entry);
                if (material == null)
                {
                    continue;
                }

                var shader = SE.ShaderCatalog.Find(entry.shader);
                if (shader == null)
                {
                    // 保留に残して保存からは消さない (導入し直せば次の読込で効く)
                    if (_warnedShaders.Add(entry.shader))
                    {
                        MTEUtils.LogWarning("シェーダーが見つかりません: {0}", entry.shader);
                    }
                    continue;
                }

                material.ChangeShader(shader);
                _pending.RemoveAt(i);
                applied = true;
            }
            return applied;
        }

        private void ReapplyMaterialLayers()
        {
            foreach (var type in MaterialLayerTypes)
            {
                foreach (var layer in timeline.FindLayers(type))
                {
                    layer.ApplyCurrentFrame(false);
                }
            }
        }

        private void SyncToTimeline()
        {
            ModelMaterial.CollectShaderChanged(_changedMaterials);
            // Collect が破棄分を落とすと version が進むので、読むのは後
            _lastVersion = ModelMaterial.shaderChangedVersion;

            _live.Clear();
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null)
                {
                    _live.Add(entry);
                }
            }

            // 現在の状態が決まった対象の保留は、もう適用しない
            _pending.RemoveAll(p => _live.Exists(l => l.IsSameTarget(p)));

            var merged = MaterialShaderSync.Merge(_live, _pending);
            if (!MaterialShaderSync.ListEquals(merged, timeline.materialShaders))
            {
                timeline.materialShaders = merged;
            }
        }

        /// <summary>
        /// 変更済みマテリアルをタイムラインのエントリにする。
        /// 背景とタイムライン管理外のメイド・モデルは null (保存しない)
        /// </summary>
        private TimelineMaterialShaderData CreateEntry(ModelMaterial material)
        {
            var controller = material.controller;

            // メイドスロットの所有者は MaidSlotStat (借り手は所有権を奪わない。ModelMaterialController.GetOrCreate)
            var slot = controller.model as MaidSlotStat;
            if (slot != null)
            {
                var body = slot.bodySkin != null ? slot.bodySkin.body : null;
                var maidCache = body != null && body.maid != null ? maidManager.GetMaidCache(body.maid) : null;
                if (maidCache == null)
                {
                    return null;
                }
                return CreateEntry(maidCache.slotNo, slot.name, slot.materials, material);
            }

            // モデルは借り手 (ProviderModelStat) に束縛されうるため、GameObject でタイムラインのモデルを引く
            foreach (var model in modelManager.models)
            {
                if (model != null && model.transform != null
                    && model.transform.gameObject == controller.gameObject)
                {
                    return CreateEntry(-1, model.name, model.materials, material);
                }
            }
            return null;
        }

        private static TimelineMaterialShaderData CreateEntry(
            int maidSlotNo, string owner, List<ModelMaterial> materials, ModelMaterial material)
        {
            var index = materials.IndexOf(material);
            if (index < 0)
            {
                return null;
            }
            return new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material.displayName,
                index = index,
                shader = material.material.shader.name,
            };
        }

        /// <summary>エントリの対象マテリアル。まだ無ければ null</summary>
        private ModelMaterial FindMaterial(TimelineMaterialShaderData entry)
        {
            List<ModelMaterial> materials = null;
            if (entry.maidSlotNo >= 0)
            {
                var maidCache = maidManager.GetMaidCache(entry.maidSlotNo);
                if (maidCache == null || maidCache.maid == null)
                {
                    return null;
                }
                var slot = maidCache.slotStats.Find(s => s.name == entry.owner);
                materials = slot != null ? slot.materials : null;
            }
            else
            {
                var model = modelManager.models.Find(m => m != null && m.name == entry.owner);
                materials = model != null ? model.materials : null;
            }
            if (materials == null)
            {
                return null;
            }

            _names.Clear();
            foreach (var material in materials)
            {
                _names.Add(material.material != null ? material.displayName : "");
            }
            var index = SE.MaterialLookup.FindIndex(_names, entry.material, entry.index);
            return index >= 0 ? materials[index] : null;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でメイド・モデルが入れ替わるため、保留も捨てる
            _pending.Clear();
            _lastTimeline = null;
            _lastVersion = -1;
        }
    }
}
```

注:
- `ModelMaterialController` は `MonoBehaviour` なので `controller.gameObject` が使える。`CollectShaderChanged` が `controller == null`（破棄済み）を落とした後に呼ぶので `controller` は生きている
- `ModelMaterialController.model` が `null` のケースは `as` で `null` になり、モデル側のループでも一致しなければ `null` を返す

- [ ] **Step 2: 登録する**

`TimelineIntegration.cs` の `TimelineUpdateManager._managers` で、`MTEP.BGModelManager.instance,` の直後に:

```csharp
                // モデルの一覧 (StudioModelManager.OnLoad) が埋まった後にシェーダーを適用する
                MTEP.MaterialShaderManager.instance,
```

csproj（`<Compile Include="Timeline\Manager\StudioLightManager.cs" />` の直前）:

```xml
    <Compile Include="Timeline\Manager\MaterialShaderManager.cs" />
```

- [ ] **Step 3: 両構成でビルドし、テストを流す**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: エラー 0、全件 PASS

- [ ] **Step 4: コミット**（例: `feat(timeline): シェーダー変更をタイムラインへ同期し読込時に適用する`）

---

### Task 5: シーンプリセット・Undo・クリップボード

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetMaterial`、v37 コメント）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`CaptureMaterial` / `ApplyMaterial`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialClipboard.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaterialShaderTests.cs`

**Interfaces:**
- Consumes: `ShaderCatalog.Find`、`ModelMaterial.isShaderChanged` / `ChangeShader`
- Produces: `ScenePresetMaterial.shader (string)`、`isEmpty` がシェーダーも見る

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのマテリアルのシェーダー変更を固定する</summary>
    public class ScenePresetMaterialShaderTests
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
        public void 変更が無ければ属性を書き出さない()
        {
            Assert.DoesNotContain("shader=", Serialize(new ScenePresetMaterial { owner = "wear", material = "a" }));
        }

        [Fact]
        public void シェーダーを往復する()
        {
            var text = Serialize(new ScenePresetMaterial
            {
                owner = "wear", material = "a", shader = "com3d2mod/Standard_NPRToonV2_Lit_",
            });

            Assert.Contains("shader=\"com3d2mod/Standard_NPRToonV2_Lit_\"", text);
            Assert.Equal("com3d2mod/Standard_NPRToonV2_Lit_", Deserialize(text).shader);
        }

        [Fact]
        public void 属性の無い旧プリセットはシェーダーを持たない()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaterial owner=\"wear\" material=\"a\" index=\"0\" />";

            Assert.Null(Deserialize(text).shader);
        }

        [Fact]
        public void シェーダーだけの変更も空ではない()
        {
            Assert.True(new ScenePresetMaterial().isEmpty);
            Assert.False(new ScenePresetMaterial { shader = "CM3D2/Lighted" }.isEmpty);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePresetMaterialShader"`
Expected: コンパイルエラー（`shader` が無い）

- [ ] **Step 3: `ScenePresetMaterial` に属性を足す**（`index` の後）

```csharp

        /// <summary>差し替えたシェーダー名。変更していなければ null で、書き出さない (null の属性は出ない)</summary>
        [XmlAttribute]
        public string shader;
```

`isEmpty` を次にする:

```csharp
        [XmlIgnore]
        public bool isEmpty => string.IsNullOrEmpty(shader)
            && (colors == null || colors.Count == 0) && (values == null || values.Count == 0);
```

v37 の変更履歴コメントを次に置き換える（CurrentVersion は 37 のまま）:

```csharp
        // v37: 追加ライトに輪郭 (cookieMode / cookieHardness / cookieImage) と影の種類 (shadows) を追加。
        //      既定の輪郭・影なしでは書き出さない。旧形式は属性が無く既定の輪郭・影なしとして読める。
        //      マテリアル差分にシェーダー (shader) を追加。変更が無ければ書き出さず、無い場合はシェーダーを触らない
```

- [ ] **Step 4: `ScenePresetManager` を変更する**

`CaptureMaterial` の `data` 初期化の後（色のループの前）に:

```csharp

            if (material.isShaderChanged)
            {
                data.shader = material.material.shader.name;
            }
```

`ApplyMaterial` の先頭に（色・値より先に差し替えないと、新シェーダーにしか無いプロパティへ書けない）:

```csharp
            if (!string.IsNullOrEmpty(state.shader))
            {
                var shader = ShaderCatalog.Find(state.shader);
                if (shader == null)
                {
                    MTEUtils.LogWarning("シェーダーが見つかりません: {0}", state.shader);
                }
                else
                {
                    material.ChangeShader(shader);
                }
            }

```

`ApplyMaterial` の doc コメントを「保存されたシェーダーとプロパティだけをマテリアルへ書き戻す。未知のプロパティ名は無視して互換を保つ」にする。

- [ ] **Step 5: `MaterialSnapshot` を変更する**

- フィールド: `private bool _isTracked;` の後に `private Shader _shader;`
- `Capture`: `snapshot` の初期化子に `_shader = material.material != null ? material.material.shader : null,`
- `Apply`: 先頭（色のループの前）に

```csharp
            // 値は記録時のシェーダーのプロパティで控えているので、先にシェーダーを戻す
            if (_shader != null)
            {
                _material.ChangeShader(_shader);
            }
```

- `Approximately`: 最初の条件に `|| o._shader != _shader` を足す
- クラスの doc コメントの「(全色・数値プロパティと追跡チェック)」を「(シェーダー・全色・数値プロパティと追跡チェック)」にする

- [ ] **Step 6: `MaterialClipboard` を変更する**

- フィールド: `_values` の後に `private static Shader _shader;`
- `Copy`: 先頭の `Clear` の後に `_shader = material.material != null ? material.material.shader : null;`
- `Paste`: `var applied = false;` の後に

```csharp

            // 貼り付け元のシェーダーにしか無いプロパティへ書けるよう、先にシェーダーを揃える。
            // シェーダーは追跡の対象外なので applied (= 呼び出し側の追跡チェック) には数えない
            if (_shader != null && material.material != null && material.material.shader != _shader)
            {
                material.ChangeShader(_shader);
            }
```

`Paste` の `<returns>` を「色・数値を 1 つでも適用したら true (シェーダーの差し替えは数えない)」にする。

- クラスの doc コメントを「マテリアル設定 (シェーダー / 色 / 数値プロパティ) の … 貼り付け先のシェーダーを先に揃え、それでも持たないプロパティは読み飛ばす」にする

- [ ] **Step 7: テストが通ることを確認する**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS（`ScenePresetEffectsTests` の CurrentVersion == 37 も通る）

- [ ] **Step 8: コミット**（例: `feat(material): シェーダー変更をシーンプリセット・Undo・コピーに含める`）

---

### Task 6: マテリアルウィンドウの UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs`

**Interfaces:**
- Consumes: `ShaderCatalog.GetShaders`、`ModelMaterial.originalShader` / `ChangeShader` / `ResetShader`
- Produces: `MaterialPropertyRowsDrawer.RecordEdit(MTEP.ModelMaterial material, MaterialTrackTarget track, Maid maid, string label)`

- [ ] **Step 1: Undo 記録を公開メソッドにする**（`MaterialPropertyRowsDrawer`）

`Draw` 内の `recordEdit` ラムダを次に置き換える:

```csharp
            Action<string> recordEdit = label => RecordEdit(material, track, maid, label);
```

クラスに足す（`DrawNameRow` の前）:

```csharp
        /// <summary>
        /// 値を書き込む直前に呼ぶ。同じマテリアルへの連続変更はマウス解放まで 1 件に集約される。
        /// maid はメイドタブでだけ渡り、自動キーフレーム登録の対象判定に使う
        /// </summary>
        public static void RecordEdit(MTEP.ModelMaterial material, MaterialTrackTarget track, Maid maid, string label)
        {
            var trackKey = track.isEnabled ? track.getKey(material) : null;
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Material,
                "マテリアル: " + material.displayName + " " + label,
                material, () => MaterialSnapshot.Capture(material, track, trackKey));
        }
```

（`Draw` 内の元の 2 行コメント「値を書き込む直前に呼ぶ…」は `RecordEdit` の doc へ移したので消す）

- [ ] **Step 2: `初期化` でシェーダーも戻す**

`初期化` ボタンの処理を次にする:

```csharp
            if (view.DrawButton("初期化", 80, rowHeight))
            {
                recordEdit("初期化");
                // シェーダーを先に戻す (値の初期値は元シェーダーのプロパティで控えている)
                material.ResetShader();
                material.Reset();
                // 初期値へ戻したのだから追跡からも外す (チェック OFF と同じ意味)
                if (trackKey != null)
                {
                    track.getStore().Unmark(trackKey);
                }
            }
```

- [ ] **Step 3: ウィンドウにシェーダー行を足す**（`MaterialEditWindow`）

フィールド（`_materialComboBox` の後）:

```csharp

        private readonly GUIComboBox<Shader> _shaderComboBox = new GUIComboBox<Shader>();

        /// <summary>シェーダー候補。表示のたびに全 Shader を走査しないよう控え、「更新」で作り直す</summary>
        private static List<Shader> _shaderCatalog;

        // シェーダー行の対象。コンボの選択確定はポップアップ側 (別フレーム) で起きるため、
        // 最後に描いた対象を控えて onSelected から引く
        private MTEP.ModelMaterial _shaderTarget;
        private MaterialTrackTarget _shaderTrack;
        private Maid _shaderMaid;
        private MTEP.ModelMaterial _shaderItemsTarget;
```

コンストラクタ:

```csharp
        private MaterialEditWindow()
        {
            _shaderComboBox.getName = (shader, _) => GetShaderDisplayName(shader);
            _shaderComboBox.onSelected = (shader, _) => ApplyShader(shader);
        }
```

`DrawMaterialSelector` の `var material = _materialComboBox.currentItem;` の null チェックの後、`DrawMaterialProperties` の前に:

```csharp
            DrawShaderRow(material, track, maid);
```

メソッド（`DrawMaterialProperties` の前）:

```csharp
        /// <summary>
        /// シェーダーの選択行。元シェーダーは「(元)」付きで並び、選べば元に戻る。
        /// 候補は NPRShader などを後から読んだときのために「更新」で作り直せる
        /// </summary>
        private void DrawShaderRow(MTEP.ModelMaterial material, MaterialTrackTarget track, Maid maid)
        {
            _shaderTarget = material;
            _shaderTrack = track;
            _shaderMaid = maid;

            var currentShader = material.material.shader;
            if (_shaderItemsTarget != material || !_shaderComboBox.items.Contains(currentShader))
            {
                RefreshShaderItems(material, false);
            }
            _shaderComboBox.currentItem = currentShader;

            DrawLabeledComboBox("シェーダー", _shaderComboBox, UpdateButtonWidth + view.margin, () =>
            {
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    RefreshShaderItems(material, true);
                }
            });
        }

        private void RefreshShaderItems(MTEP.ModelMaterial material, bool reloadCatalog)
        {
            if (_shaderCatalog == null || reloadCatalog)
            {
                _shaderCatalog = ShaderCatalog.GetShaders();
            }

            var items = new List<Shader>(_shaderCatalog.Count + 2);
            // 元と現在のシェーダーは候補の接頭辞に合わなくても選べるようにする
            foreach (var shader in new[] { material.originalShader, material.material.shader })
            {
                if (shader != null && !_shaderCatalog.Contains(shader) && !items.Contains(shader))
                {
                    items.Add(shader);
                }
            }
            items.AddRange(_shaderCatalog);

            _shaderComboBox.items = items;
            _shaderItemsTarget = material;
        }

        private string GetShaderDisplayName(Shader shader)
        {
            if (shader == null)
            {
                return "";
            }
            return _shaderTarget != null && shader == _shaderTarget.originalShader
                ? shader.name + " (元)"
                : shader.name;
        }

        private void ApplyShader(Shader shader)
        {
            var material = _shaderTarget;
            if (material == null || material.material == null || shader == null
                || material.material.shader == shader)
            {
                return;
            }

            // シェーダーはキーではないので追跡チェックは付けない (タイムラインへは MaterialShaderManager が保存する)
            MaterialPropertyRowsDrawer.RecordEdit(material, _shaderTrack, _shaderMaid, "シェーダー");
            material.ChangeShader(shader);
        }
```

注: `MaterialEditWindow.cs` は `System.Collections.Generic` と `UnityEngine` を using 済み。

- [ ] **Step 4: 両構成でビルドし、テストを流す**

Run: MSBuild (COM3D2 → COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: エラー 0、全件 PASS

- [ ] **Step 5: コミット**（例: `feat(material): マテリアルウィンドウでシェーダーを選べるようにする`）

---

### Task 7: ドキュメント

**Files:**
- Modify: `docs-site/guide/maid-editing.md`（末尾に節を追加）
- Modify: `docs-site/timeline/compatibility.md`（影の行の後）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（タイムライン XML の互換方向の節の末尾。git 管理外）

- [ ] **Step 1: `maid-editing.md` の末尾に追記する**

```markdown

## マテリアルのシェーダー変更

マテリアルウィンドウの `シェーダー` で、選んだマテリアルのシェーダーを差し替えられます。
候補はゲーム標準の `CM3D2/` と、NPRShader を導入していればその `com3d2mod/` のシェーダーです。
元のシェーダーには `(元)` が付き、選ぶと元に戻ります。`初期化` でもシェーダーは元に戻ります。
NPRShader を後から読み込んだときは `更新` で候補を作り直してください。

- 色や数値は、同じ名前のプロパティなら差し替え後も引き継がれます。NPR 固有の値（Matcap・Emission など）は差し替え後に現れます
- シェーダーの変更はシーンプリセットと Undo、マテリアルのコピー / ペーストに含まれます
- タイムラインにはメイドと配置モデルのシェーダー変更が保存され、読み込み時に反映されます。キーフレームでは切り替えられません。背景タブの変更はタイムラインには保存されません
- シェーダーを変える前に打ったマテリアルのキーには、NPR 固有の値が 0 で入っています。シェーダーを変えた後にキーを登録し直してください
- 着替えるとマテリアルが作り直されるため、シェーダーは元に戻ります
```

- [ ] **Step 2: `compatibility.md` に 1 行足す**（影の行の後）

```markdown
- マテリアルの `シェーダー` の変更はタイムラインに保存されます（`MaterialShaders`）。SceneEditor 独自の値で、MTE や以前の SceneEditor で読むと元のシェーダーで表示されます。NPRShader のシェーダーは NPRShader が無い環境では適用されず、保存し直しても記録は残ります
```

- [ ] **Step 3: ワークスペースの `CLAUDE.md` に 1 行足す**（「ライトキーの輪郭の硬さ」の行の後）

```markdown
- マテリアルのシェーダー変更（ルートの `<MaterialShaders>`、`MaidSlotNo`（モデルは -1）/ `Owner`（スロット名かモデル名）/ `Material` / `Index` / `Shader`）は SE 独自。変更が無ければ書き出さない。読込時に 1 回適用するだけでキーフレームは持たない。適用できないエントリ（モデル未ロード・NPRShader 未導入）は保留として保持し、保存し直しても消さない。MTE は要素を読み飛ばすため元のシェーダーで表示される。シーンプリセットは v37 で `ScenePresetMaterial` に `shader` 属性を追加
```

- [ ] **Step 4: コミット**（例: `docs(material): シェーダー変更を説明する`。CLAUDE.md はリポジトリ外なので含めない）

---

### Task 8: 実機確認

**Files:** なし（確認のみ）

- [ ] **Step 1: restart-verify スキルで DLL を反映し、ゲームを起動してセーブをロードする**（再起動前にユーザーへ確認する）。デイリー画面でエディタを有効にして確認する（memory `verify-in-normal-scene`）
- [ ] **Step 2: devbridge で確認する**
  - マテリアルウィンドウのメイドタブで `シェーダー` 行が出て、候補に `CM3D2/*` と `com3d2mod/*` が並び、元シェーダーに `(元)` が付くこと（`screenshot` で確認）
  - `ModelMaterial.ChangeShader` で髪のマテリアルを `com3d2mod/Standard_NPRToonV2_Hair_OutlineTex_` へ変え、NPR のプロパティ（`_MatcapValue` 等）の行が出ること。`ResetShader` で元に戻り、NPR の行が消えること
  - renderQueue を明示指定したマテリアル（例: `nip_02` の 3010）を NPR → 元へ往復し、3010 に戻ること。既定のままの半透明（例: 3000 の `Toony_Lighted_Trans`）を `Standard_NPRToonV2_Trans` へ変えて 3000 のままになること
  - Undo（`HistoryManager`）でシェーダーが戻ること。マテリアルのコピー → 別マテリアルへのペーストでシェーダーも写ること
  - シーンプリセットを保存 → マテリアルを元に戻す → 適用で、シェーダーと値が戻ること。XML に `shader=` が出ること
  - タイムラインを保存し、XML に `<MaterialShaders>` が出ること。マテリアルを元に戻してからタイムラインを読み直し、シェーダーが反映されること。変更が無いタイムラインでは要素が出ないこと
  - `<Shader>` を存在しない名前に書き換えた XML を読み、例外にならず警告が 1 回だけ出て、保存し直しても要素が残ること
  - 着替え（`SetProp` → `AllProcPropSeqStart`）でマテリアルが作り直された後、30 フレーム以上待ってタイムラインの `materialShaders` からそのエントリが消えること
  - タイムラインを再生・シークしてもシェーダーが戻らないこと（`ModelMaterialTimelineLayer` の `Reset` 経路を含む）
  - `tail_log` に SceneEditor 由来の例外が無いこと
- [ ] **Step 3: 後片付け**: 変更したマテリアルを元のシェーダーへ戻す（着替え直しでもよい）

## レビュー却下メモ

- 着替え検知が `.materials` への外部アクセスに間接依存する — 誤検知。着替えで破棄された Material / コントローラは Unity の null 判定で `CollectShaderChanged` の `RemoveWhere` が落とす（`.materials` を読まなくてよい）。30 フレームごとの同期で拾う。Task 8 でも確認する
- 多段切替で初期値が最初の出現時点で固定される — 意図どおり。仕様の文言を「最初に現れた時点」に明確化した
- `ModelMaterial` の中核ロジックに単体テストが無い — プロジェクト制約（テストで Unity ネイティブ呼び出し不可）。純粋関数へ切り出せる部分（renderQueue・同定・併合）はテスト済みで、残りは Task 8 の実機確認で担保する
