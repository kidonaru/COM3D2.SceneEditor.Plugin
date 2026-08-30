# タイムラインのモデル管理 ModItemExplorer 一本化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> （このリポジトリの運用ルールにより subagent-driven-development は使わない）

**Goal:** SE のタイムラインが持つモデル配置機能を廃し、配置のライフサイクル（生成・削除・表示・アタッチ）を ModItemExplorer（MIE）へ一本化する。

**Architecture:** SE 側に「属性の短名一致でゲストの public static クラスを発見し、デリゲートに束ねる」規約 `ModelPlacerProvider` を新設する（既存 `ScenePresetProviderRegistry` と同型）。SE の `ExternalModelHack : ModelHackBase` がその規約経由で MIE へ委譲し、`SceneEditorHack` のモデル機能は削除する。MIE 側は `SelfModelPlacer` に規約クラスと公式 BG / マイルームの生成経路を追加する。

**Tech Stack:** C# (net48 / Unity 2022, IMGUI), MSBuild, xUnit (SE のみ), リフレクション + `Delegate.CreateDelegate`

**Spec:** `docs/superpowers/specs/2026-08-26-timeline-model-placer-integration-design.md`

## Global Constraints

- 対象リポジトリは 2 つ。SE = `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`、MIE = `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`。**git worktree は使わない**（各リポのメイン作業ディレクトリで作業する）
- 両プラグインは互いのアセンブリを参照しない。境界を越える型は `UnityEngine.GameObject` / `Maid`（Assembly-CSharp）/ プリミティブのみ
- コードコメントとログメッセージは日本語で書く
- **`deploy.bat` / `deploy.ps1` は実行しない**。`debug.bat` も実行しない（ゲームフォルダへ DLL をコピーするため）。ビルド確認は MSBuild を直接叩く:
  - SE: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"`
  - MIE: 同じ MSBuild で `source\COM3D2.ModItemExplorer.Plugin\COM3D2.ModItemExplorer.Plugin.csproj` を指定する
- SE は新規 `.cs` を追加したら **`COM3D2.SceneEditor.Plugin.csproj` に `<Compile Include="..." />` を追記する**（ワイルドカード指定ではないため）。既存行のアルファベット順を保つ
- SE のテストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。実行前に上記 MSBuild で COM3D25 構成のプラグインをビルドしておくこと
- SE テストは Unity ランタイム無しで動く。**`GameObject` の生成や `UnityEngine.Debug.Log` を通る経路はテストしない**。テスト対象は純粋なリフレクション / 文字列ロジックに限る
- MIE にテストプロジェクトは無い。MIE 側タスクの検証はビルド成功と実機確認（最終タスク）で行う
- プロバイダ ID は `"ModItemExplorer"`（= `SelfModelPlacer.PluginName`）。旧タイムライン XML の `pluginName` は `"SceneEditor"`

---

### Task 1: SE — `ModelPlacerProvider` 規約の発見・束縛

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Manager\ModelPlacerProviderRegistry.cs" />` を `Manager\` 群のアルファベット順の位置に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProvider`（デリゲート束。フィールドは下記実装のとおり）
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderBinder.TryBind(Type type, out ModelPlacerProvider provider, out string error) → bool`
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderRegistry.providers → List<ModelPlacerProvider>`
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderRegistry.current → ModelPlacerProvider`（先頭 1 件、無ければ null）
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderRegistry.Refresh() → void`
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderRegistry.LEGACY_PLUGIN_NAME → "SceneEditor"`
  - `COM3D2.SceneEditor.Plugin.ModelPlacerProviderRegistry.MigratePluginName(string pluginName, string providerId) → string`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs` を新規作成する。

```csharp
using System;
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ゲスト側が自前定義する規約用属性（短名一致で判定されるため型の同一性は不要）</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ModelPlacerProviderAttribute : Attribute
    {
    }

    /// <summary>必須・任意メンバをすべて備えたプロバイダ</summary>
    [ModelPlacerProvider]
    public static class FullDummyProvider
    {
        public static string ModelPlacerId => "DummyPlacer";
        public static string ModelPlacerDisplayName => "ダミー配置";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "dummy.menu";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }

        public static string GetModelDisplayName(GameObject obj) => "ダミー";
        public static void BeginBatch() { }
        public static void EndBatch() { }
    }

    /// <summary>任意メンバを持たないプロバイダ</summary>
    [ModelPlacerProvider]
    public static class MinimalDummyProvider
    {
        public static string ModelPlacerId => "MinimalPlacer";
        public static string ModelPlacerDisplayName => "最小配置";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    /// <summary>必須メンバ GetModels を欠いたプロバイダ</summary>
    [ModelPlacerProvider]
    public static class BrokenDummyProvider
    {
        public static string ModelPlacerId => "BrokenPlacer";
        public static string ModelPlacerDisplayName => "壊れた配置";

        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    /// <summary>ID が空のプロバイダ</summary>
    [ModelPlacerProvider]
    public static class EmptyIdDummyProvider
    {
        public static string ModelPlacerId => "";
        public static string ModelPlacerDisplayName => "ID なし";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    public class ModelPlacerProviderTests
    {
        [Fact]
        public void 必須と任意メンバを備えた型をバインドできる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(FullDummyProvider), out var provider, out var error);

            Assert.True(bound, error);
            Assert.Equal("DummyPlacer", provider.id);
            Assert.Equal("ダミー配置", provider.displayName);
            Assert.NotNull(provider.getModels);
            Assert.NotNull(provider.createModel);
            Assert.NotNull(provider.attachModel);
            Assert.NotNull(provider.getModelDisplayName);
            Assert.NotNull(provider.beginBatch);
            Assert.NotNull(provider.endBatch);
        }

        [Fact]
        public void 任意メンバが無くてもバインドできる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(MinimalDummyProvider), out var provider, out var error);

            Assert.True(bound, error);
            Assert.Null(provider.getModelDisplayName);
            Assert.Null(provider.beginBatch);
            Assert.Null(provider.endBatch);
        }

        [Fact]
        public void 必須メンバが欠けた型はバインドされず欠落名が分かる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(BrokenDummyProvider), out var provider, out var error);

            Assert.False(bound);
            Assert.Null(provider);
            Assert.Contains("GetModels", error);
        }

        [Fact]
        public void IDが空の型はバインドされない()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(EmptyIdDummyProvider), out var provider, out var error);

            Assert.False(bound);
            Assert.Null(provider);
        }

        [Theory]
        // 旧タイムライン XML の pluginName はプロバイダ ID へ読み替える
        [InlineData("SceneEditor", "ModItemExplorer", "ModItemExplorer")]
        // それ以外はそのまま通す
        [InlineData("ModItemExplorer", "ModItemExplorer", "ModItemExplorer")]
        [InlineData("MultipleMaids", "ModItemExplorer", "MultipleMaids")]
        // 空・null は触らない
        [InlineData("", "ModItemExplorer", "")]
        [InlineData(null, "ModItemExplorer", null)]
        public void 旧プラグイン名を読み替えられる(string pluginName, string providerId, string expected)
        {
            Assert.Equal(expected, ModelPlacerProviderRegistry.MigratePluginName(pluginName, providerId));
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー（`ModelPlacerProviderBinder` / `ModelPlacerProviderRegistry` が存在しない）

- [ ] **Step 3: 実装を書く**

`source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs` を新規作成する。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 発見済みのモデル配置プロバイダ 1 件。メソッドはデリゲートで保持する。
    /// 任意メンバに対応するデリゲートは未実装なら null になる
    /// </summary>
    public class ModelPlacerProvider
    {
        public string id;
        public string displayName;

        public Func<List<GameObject>> getModels;
        public Func<GameObject, string> getModelFileName;

        /// <summary>(type, fileName, myRoomId, bgObjectId, group, visible) → 生成した GameObject</summary>
        public Func<string, string, int, long, int, bool, GameObject> createModel;

        public Action<GameObject> deleteModel;
        public Action deleteAllModels;
        public Action<GameObject, bool> setModelVisible;

        /// <summary>(obj, maid, attachPointName)。maid が null なら解除</summary>
        public Action<GameObject, Maid, string> attachModel;

        public Func<GameObject, string> getModelDisplayName;
        public Action beginBatch;
        public Action endBatch;
    }

    /// <summary>
    /// 規約メンバをリフレクションでデリゲートへ束ねる。
    /// ログを出さず純粋な結果だけを返すため、ユニットテストから直接呼べる
    /// </summary>
    public static class ModelPlacerProviderBinder
    {
        public static bool TryBind(Type type, out ModelPlacerProvider provider, out string error)
        {
            provider = null;
            error = null;

            var flags = BindingFlags.Public | BindingFlags.Static;
            var missing = new List<string>();

            var idProp = type.GetProperty("ModelPlacerId", flags);
            var nameProp = type.GetProperty("ModelPlacerDisplayName", flags);
            if (idProp == null || idProp.PropertyType != typeof(string))
            {
                missing.Add("ModelPlacerId");
            }
            if (nameProp == null || nameProp.PropertyType != typeof(string))
            {
                missing.Add("ModelPlacerDisplayName");
            }

            var getModels = FindMethod(type, flags, "GetModels", typeof(List<GameObject>), Type.EmptyTypes, missing);
            var getFileName = FindMethod(type, flags, "GetModelFileName", typeof(string), new[] { typeof(GameObject) }, missing);
            var createModel = FindMethod(type, flags, "CreateModel", typeof(GameObject),
                new[] { typeof(string), typeof(string), typeof(int), typeof(long), typeof(int), typeof(bool) }, missing);
            var deleteModel = FindMethod(type, flags, "DeleteModel", typeof(void), new[] { typeof(GameObject) }, missing);
            var deleteAll = FindMethod(type, flags, "DeleteAllModels", typeof(void), Type.EmptyTypes, missing);
            var setVisible = FindMethod(type, flags, "SetModelVisible", typeof(void),
                new[] { typeof(GameObject), typeof(bool) }, missing);
            var attachModel = FindMethod(type, flags, "AttachModel", typeof(void),
                new[] { typeof(GameObject), typeof(Maid), typeof(string) }, missing);

            if (missing.Count > 0)
            {
                error = string.Format(
                    "モデル配置プロバイダの契約メンバが不足しています: {0} ({1})",
                    string.Join(", ", missing.ToArray()), type.FullName);
                return false;
            }

            var id = idProp.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(id))
            {
                error = "モデル配置プロバイダの ID が空です: " + type.FullName;
                return false;
            }

            var displayName = nameProp.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = id;
            }

            provider = new ModelPlacerProvider
            {
                id = id,
                displayName = displayName,
                getModels = (Func<List<GameObject>>)Delegate.CreateDelegate(typeof(Func<List<GameObject>>), getModels),
                getModelFileName = (Func<GameObject, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string>), getFileName),
                createModel = (Func<string, string, int, long, int, bool, GameObject>)Delegate.CreateDelegate(
                    typeof(Func<string, string, int, long, int, bool, GameObject>), createModel),
                deleteModel = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), deleteModel),
                deleteAllModels = (Action)Delegate.CreateDelegate(typeof(Action), deleteAll),
                setModelVisible = (Action<GameObject, bool>)Delegate.CreateDelegate(typeof(Action<GameObject, bool>), setVisible),
                attachModel = (Action<GameObject, Maid, string>)Delegate.CreateDelegate(
                    typeof(Action<GameObject, Maid, string>), attachModel),
            };

            // 任意メンバ。シグネチャ不一致は契約不備とせず単に無視する
            var getDisplayName = type.GetMethod("GetModelDisplayName", flags, null, new[] { typeof(GameObject) }, null);
            if (getDisplayName != null && getDisplayName.ReturnType == typeof(string))
            {
                provider.getModelDisplayName = (Func<GameObject, string>)Delegate.CreateDelegate(
                    typeof(Func<GameObject, string>), getDisplayName);
            }

            var beginBatch = type.GetMethod("BeginBatch", flags, null, Type.EmptyTypes, null);
            var endBatch = type.GetMethod("EndBatch", flags, null, Type.EmptyTypes, null);
            if (beginBatch != null && beginBatch.ReturnType == typeof(void)
                && endBatch != null && endBatch.ReturnType == typeof(void))
            {
                provider.beginBatch = (Action)Delegate.CreateDelegate(typeof(Action), beginBatch);
                provider.endBatch = (Action)Delegate.CreateDelegate(typeof(Action), endBatch);
            }

            return true;
        }

        /// <summary>
        /// 戻り値の型まで一致するメソッドを探す。
        /// 型を見ずに通すと Delegate.CreateDelegate が例外で落ちるため、ここで弾く
        /// </summary>
        private static MethodInfo FindMethod(
            Type type, BindingFlags flags, string name, Type returnType, Type[] parameterTypes, List<string> missing)
        {
            var method = type.GetMethod(name, flags, null, parameterTypes, null);
            if (method == null || method.ReturnType != returnType)
            {
                missing.Add(name);
                return null;
            }
            return method;
        }
    }

    /// <summary>
    /// 外部プラグインのモデル配置プロバイダをリフレクションで発見・保持する。
    /// アセンブリ参照を不要にするため、属性は型の完全一致ではなく
    /// 短名 "ModelPlacerProviderAttribute" の一致で判定する（各プラグインが自前定義する規約）。
    /// 契約の詳細は docs-site/dev/model-placer-guest-guide.md を参照
    /// </summary>
    public static class ModelPlacerProviderRegistry
    {
        private const string ATTRIBUTE_NAME = "ModelPlacerProviderAttribute";

        /// <summary>旧タイムライン XML が持つ、SE 自前配置時代のプラグイン名</summary>
        public const string LEGACY_PLUGIN_NAME = "SceneEditor";

        private static List<ModelPlacerProvider> _providers;

        /// <summary>前回走査時のロード済みアセンブリ数。増えていなければ再走査を省く</summary>
        private static int _scannedAssemblyCount = -1;

        public static List<ModelPlacerProvider> providers
            => _providers ?? (_providers = FindProviders());

        /// <summary>現在使うプロバイダ。複数見つかった場合は先勝ち。無ければ null</summary>
        public static ModelPlacerProvider current
            => providers.Count > 0 ? providers[0] : null;

        /// <summary>
        /// 次回参照時に再走査させる。遅延ロードされたプラグインを取りこぼさないよう
        /// シーン切り替え時に呼ぶ。全型走査は重いため、アセンブリ数が増えていなければ維持する
        /// </summary>
        public static void Refresh()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Length == _scannedAssemblyCount)
            {
                return;
            }
            _providers = null;
        }

        /// <summary>
        /// 旧 XML の pluginName をプロバイダ ID へ読み替える。
        /// 空・null はタイムライン側で既定値扱いされるため触らない
        /// </summary>
        public static string MigratePluginName(string pluginName, string providerId)
        {
            if (pluginName == LEGACY_PLUGIN_NAME && !string.IsNullOrEmpty(providerId))
            {
                return providerId;
            }
            return pluginName;
        }

        private static List<ModelPlacerProvider> FindProviders()
        {
            var result = new List<ModelPlacerProvider>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            _scannedAssemblyCount = assemblies.Length;

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    // 型解決に失敗するアセンブリ（依存欠落等）は対象外として読み飛ばす
                    continue;
                }

                foreach (var type in types)
                {
                    try
                    {
                        if (!HasProviderAttribute(type))
                        {
                            continue;
                        }

                        if (!ModelPlacerProviderBinder.TryBind(type, out var provider, out var error))
                        {
                            MTEUtils.LogError(error);
                            continue;
                        }

                        // id 重複はサイレント上書きになるため、先勝ちで明示的に弾く
                        if (result.Any(p => p.id == provider.id))
                        {
                            MTEUtils.LogError("モデル配置プロバイダの ID が重複しています: {0} ({1})",
                                provider.id, type.FullName);
                            continue;
                        }

                        result.Add(provider);
                        MTEUtils.Log("モデル配置プロバイダを発見しました: {0} ({1})",
                            provider.id, type.FullName);
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogError("モデル配置プロバイダのバインドに失敗しました: " + type.FullName);
                        MTEUtils.LogException(e);
                    }
                }
            }

            return result;
        }

        private static bool HasProviderAttribute(Type type)
        {
            return type.GetCustomAttributes(false)
                .Any(attr => attr.GetType().Name == ATTRIBUTE_NAME);
        }
    }
}
```

- [ ] **Step 4: csproj に追加してビルドする**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `Manager\` 群に以下を追記する（アルファベット順の位置）。

```xml
<Compile Include="Manager\ModelPlacerProviderRegistry.cs" />
```

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"`
Expected: ビルド成功

- [ ] **Step 5: テストを実行して成功を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS（既存 70 件 + 新規 9 件）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs
git commit -m "feat(timeline): モデル配置プロバイダ規約を追加する"
```

---

### Task 2: MIE — 規約クラスを実装して SE から発見できるようにする

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`SetVisible` の隣に `FindModelByGameObject` を使う委譲用ヘルパを追加）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj`（`<Compile Include>` 形式なら追記。ワイルドカードなら不要 — 開く前に既存の記述形式を確認すること）

**Interfaces:**
- Consumes: なし（SE 側 Task 1 の規約メンバ名・シグネチャに合わせる）
- Produces: `COM3D2.ModItemExplorer.Plugin.ModelPlacerProvider`（`[ModelPlacerProvider]` 付き public static クラス）。この時点で対応する `type` は `Mod` と `Asset` のみ

- [ ] **Step 1: 規約クラスを作る**

`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor プラグインのモデル配置プロバイダ規約用の属性。
    /// アセンブリ参照を避けるため、各プラグインが同名（短名一致）で自前定義する
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ModelPlacerProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// 自前配置モデルを SceneEditor のタイムラインへ公開するプロバイダ
    /// （規約の詳細は SceneEditor の docs-site/dev/model-placer-guest-guide.md 参照）。
    /// タイムラインは配置の生成・削除・表示・アタッチをここへ委譲する
    /// </summary>
    [ModelPlacerProvider]
    public static class ModelPlacerProvider
    {
        private static SelfModelPlacer placer => SelfModelPlacer.instance;

        public static string ModelPlacerId => SelfModelPlacer.PluginName;

        public static string ModelPlacerDisplayName => "モデル配置 (ModItemExplorer)";

        public static List<GameObject> GetModels()
        {
            var result = new List<GameObject>();
            foreach (var model in placer.modelList)
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    result.Add(go);
                }
            }
            return result;
        }

        public static string GetModelFileName(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            return model?.infoWrapper?.fileName ?? "";
        }

        public static string GetModelDisplayName(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            return model?.displayName ?? "";
        }

        /// <summary>
        /// type は SceneEditor 側 StudioModelType の enum 名。
        /// 現時点で対応するのは Mod (.menu) と Asset (.asset_bg) のみで、
        /// Prefab / MyRoom は後続タスクで対応する
        /// </summary>
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible)
        {
            StudioModelStatWrapper model = null;
            switch (type)
            {
                case "Mod":
                    model = placer.CreateModel(fileName, group, visible);
                    break;
                case "Asset":
                    model = placer.CreateBgObject(TrimAssetBgExtension(fileName), group, visible);
                    break;
                default:
                    MTEUtils.LogWarning("未対応のモデル種別です。{0} ({1})", type, fileName);
                    break;
            }
            return model?.obj as GameObject;
        }

        public static void DeleteModel(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.DeleteModel(model);
            }
        }

        public static void DeleteAllModels()
        {
            placer.DeleteAll();
        }

        public static void SetModelVisible(GameObject obj, bool visible)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.SetVisible(model, visible);
            }
        }

        /// <summary>
        /// boneName は追従先ボーンの名前（SceneEditor 側で解決済み）。
        /// ボーン名でのアタッチは Task 6 で実装するため、現時点では解除のみ受け付ける
        /// </summary>
        public static void AttachModel(GameObject obj, Maid maid, string boneName)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model == null)
            {
                return;
            }
            placer.Attach(model, null, null);
        }
    }
}
```

`BgObjectAssetLoader` には拡張子定数 `AssetBgExtension`（`.asset_bg`）しか無く、
拡張子を落とすヘルパは存在しない。`CreateBgObject` は `assetBundleName + AssetBgExtension` で
`fileName` を組み立てるため、規約クラス側に逆変換のローカルヘルパを書く。

```csharp
/// <summary>fileName から .asset_bg 拡張子を落としてアセットバンドル名に戻す</summary>
private static string TrimAssetBgExtension(string fileName)
{
    if (!string.IsNullOrEmpty(fileName)
        && fileName.EndsWith(BgObjectAssetLoader.AssetBgExtension, StringComparison.OrdinalIgnoreCase))
    {
        return fileName.Substring(
            0, fileName.Length - BgObjectAssetLoader.AssetBgExtension.Length);
    }
    return fileName;
}
```

- [ ] **Step 2: ビルドする**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" source\COM3D2.ModItemExplorer.Plugin\COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"`（MIE リポジトリで実行）
Expected: ビルド成功。`FindModelByGameObject` / `Attach` / `SetVisible` / `DeleteAll` のシグネチャが合わない場合は `SelfModelPlacer` の実定義に合わせて修正する

- [ ] **Step 3: コミット（MIE リポジトリ）**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs
git commit -m "feat(model): SceneEditor へモデル配置プロバイダを公開する"
```

---

### Task 3: SE — `ExternalModelHack` でタイムラインの配置をプロバイダへ委譲する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs:170-386`（モデル管理ブロックを削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:152`（hack 登録）、`:353-386`（`RegisterModelProvider` 削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `ModelPlacerProviderRegistry.current`, `ModelPlacerProvider`（Task 1）
- Produces: `COM3D2.MotionTimelineEditor.Plugin.ExternalModelHack`（`ModelHackBase` 派生、コンストラクタ `ExternalModelHack(ModelPlacerProvider provider)`）

- [ ] **Step 1: `ExternalModelHack` を作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 外部プラグイン（ModelPlacerProvider 規約の実装者）へモデル配置を委譲する ModelHack。
    /// タイムラインは配置の所有者ではなく、プロバイダが列挙するモデルをキーフレーム対象として扱う
    /// </summary>
    public class ExternalModelHack : ModelHackBase
    {
        private readonly ModelPlacerProvider _provider;

        /// <summary>
        /// GameObject → StudioModelStat の対応。毎フレーム作り直すと
        /// BoneController / MaterialController が作り直されるためキャッシュする
        /// </summary>
        private readonly Dictionary<GameObject, StudioModelStat> _statMap
            = new Dictionary<GameObject, StudioModelStat>();

        private readonly List<StudioModelStat> _modelList = new List<StudioModelStat>();

        public override string pluginName => _provider.id;

        public ExternalModelHack(ModelPlacerProvider provider)
        {
            _provider = provider;
        }

        public override List<StudioModelStat> modelList
        {
            get
            {
                _modelList.Clear();

                var objects = SafeGetModels();
                CleanupDestroyed(objects);

                foreach (var obj in objects)
                {
                    var stat = GetOrCreateStat(obj);
                    if (stat != null)
                    {
                        _modelList.Add(stat);
                    }
                }
                return _modelList;
            }
        }

        private List<GameObject> SafeGetModels()
        {
            try
            {
                return _provider.getModels() ?? new List<GameObject>();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return new List<GameObject>();
            }
        }

        /// <summary>
        /// プロバイダの一覧から消えた GameObject のエントリを捨てる。
        /// Unity の null 判定が真でも Dictionary のキーとしては生きているため明示的に掃除する
        /// </summary>
        private void CleanupDestroyed(List<GameObject> aliveObjects)
        {
            List<GameObject> deadKeys = null;
            foreach (var pair in _statMap)
            {
                if (pair.Key == null || !aliveObjects.Contains(pair.Key))
                {
                    if (deadKeys == null)
                    {
                        deadKeys = new List<GameObject>();
                    }
                    deadKeys.Add(pair.Key);
                }
            }
            if (deadKeys != null)
            {
                foreach (var key in deadKeys)
                {
                    _statMap.Remove(key);
                }
            }
        }

        /// <summary>
        /// GameObject に対応する StudioModelStat を返す。
        /// group は ModelHackManager.modelList の FixGroup が列挙順で振り直すため、
        /// ここでは触らない（プロバイダ側の採番と突き合わせると毎回作り直しになる）。
        /// 作り直しの判定は fileName の変化だけで行う
        /// </summary>
        private StudioModelStat GetOrCreateStat(GameObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            var fileName = SafeGetFileName(obj);

            if (_statMap.TryGetValue(obj, out var cached))
            {
                if (cached.info != null && cached.info.fileName == fileName)
                {
                    cached.visible = obj.activeSelf;
                    return cached;
                }
                _statMap.Remove(obj);
            }

            var stat = modelManager.CreateModelStat(
                fileName,
                obj.transform,
                AttachPoint.Null,
                -1,
                obj,
                pluginName,
                obj.activeSelf);

            _statMap[obj] = stat;
            return stat;
        }

        private string SafeGetFileName(GameObject obj)
        {
            try
            {
                return _provider.getModelFileName(obj) ?? obj.name;
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return obj.name;
            }
        }

        public override void CreateModel(StudioModelStat model)
        {
            try
            {
                var info = model.info;
                var obj = _provider.createModel(
                    info.type.ToString(),
                    info.fileName,
                    info.myRoomId,
                    info.bgObjectId,
                    model.group,
                    model.visible);
                if (obj == null)
                {
                    MTEUtils.LogError("CreateModel: モデルの追加に失敗しました " + model.name);
                    return;
                }

                model.transform = obj.transform;
                model.obj = obj;
                _statMap[obj] = model;

                UpdateAttachPoint(model);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void DeleteModel(StudioModelStat model)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj != null)
                {
                    _statMap.Remove(obj);
                    _provider.deleteModel(obj);
                }
                model.transform = null;
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void DeleteAllModels()
        {
            try
            {
                _statMap.Clear();
                _provider.deleteAllModels();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void SetModelVisible(StudioModelStat model, bool visible)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj != null)
                {
                    _provider.setModelVisible(obj, visible);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void UpdateAttachPoint(StudioModelStat model)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj == null)
                {
                    return;
                }

                // アタッチ先ボーンの解決は SE 側が行い、プロバイダへはボーン名だけ渡す。
                // AttachPoint enum → IKManager.BoneType の対応表をゲスト側に持たせずに済む
                var maidCache = maidManager.GetMaidCache(model.attachMaidSlotNo);
                var boneTransform = maidCache?.GetAttachPointTransform(model.attachPoint);
                var maid = boneTransform != null ? maidCache.maid : null;
                _provider.attachModel(obj, maid, boneTransform != null ? boneTransform.name : "");
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>タイムライン読込のような一括操作をプロバイダへ伝える（任意メンバ）</summary>
        public void BeginBatch()
        {
            try
            {
                _provider.beginBatch?.Invoke();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void EndBatch()
        {
            try
            {
                _provider.endBatch?.Invoke();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }
}
```

`AttachPoint` は `SceneEditorHack.cs` と同じく `using AttachPoint = PhotoTransTargetObject.AttachPoint;` を
ファイル冒頭の名前空間内に置くこと。`MaidCache.maid` の実プロパティ名は `MaidCache.cs` で確認する。

- [ ] **Step 2: `SceneEditorHack` からモデル管理を削除する**

`source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs` の「モデル管理」ブロックのうち、
`_modelList` / `CreateModel` / `DeleteModel` / `DeleteAllModels` / `UpdateAttachPoint` / `AttachItem` /
`GetModelParent` / `LoadGameModel` / `LoadMyRoomObject` / `LoadModObject` を削除する。

**`modelList` の override は削除しないこと。** `SceneEditorHack` は `StudioHackBase` 派生で、
`StudioHackBase : IModelHack`（`Timeline/Hack/StudioHackBase.cs:17`）かつ `modelList` は
**abstract**（同 `:23`）なので、削除するとコンパイルエラーになる。
`ModelHackManager.modelList`（`Timeline/Manager/ModelHackManager.cs:19`）は無条件に
`studioHack.modelList` を連結するため、空リストを返す override として残す。
同じ理由で**クラス宣言から `IModelHack` を外すこともしない**（`StudioHackBase` 経由で実装している）。

```csharp
// モデル配置は ModelPlacerProvider 経由の ExternalModelHack が持つ。
// StudioHackBase.modelList が abstract のため、空リストを返す実装だけ残す
private static readonly List<StudioModelStat> _emptyModelList = new List<StudioModelStat>();
public override List<StudioModelStat> modelList => _emptyModelList;
```

`CreateModel` / `DeleteModel` / `DeleteAllModels` / `UpdateAttachPoint` は
`StudioHackBase` 側に no-op の virtual 実装があるため、override を消して問題ない。

`PhotoBGObjectData.Create()` の明示ロード（`:151-155`）は
`StudioModelManager` の `OfficialObjectLabelMap` / `BGObjectIdMap` が依存するため**残す**。

- [ ] **Step 3: 登録処理を差し替える**

`source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` の `Initialize`:

```csharp
MTEP.StudioHackManager.instance.Register(new MTEP.SceneEditorHack());

var modelPlacer = ModelPlacerProviderRegistry.current;
if (modelPlacer != null)
{
    MTEP.ModelHackManager.instance.Register(new MTEP.ExternalModelHack(modelPlacer));
}
else
{
    MTEUtils.LogWarning(
        "モデル配置プロバイダが見つかりません。ModItemExplorer を導入するとタイムラインのモデル機能が使えます");
}
```

同ファイル末尾の `RegisterModelProvider()` 呼び出しと `RegisterModelProvider` /
`ValidTimelineModels` メソッドを削除する。タイムラインのモデルは MIE 自身が
`ModelProviderHost` へ提供するため、残すとボーン編集・マテリアル編集の一覧に二重に並ぶ。
不要になった `using` があれば併せて整理する。

- [ ] **Step 4: csproj に追加してビルドする**

```xml
<Compile Include="Timeline\Hack\ExternalModelHack.cs" />
```

Run: SE の MSBuild コマンド（Global Constraints 参照）
Expected: ビルド成功

- [ ] **Step 5: テストを実行して回帰が無いことを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs source/COM3D2.SceneEditor.Plugin/Timeline/Hack/SceneEditorHack.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(timeline): モデル配置を外部プロバイダへ委譲する"
```

---

### Task 4: SE — 旧 pluginName の読み替えとモデル系レイヤーの条件登録

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs:380-437`（`SetupModels`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:176-186`（モデル系レイヤー登録）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/StudioModelStatNameTests.cs`

**Interfaces:**
- Consumes: `ModelPlacerProviderRegistry.MigratePluginName`（Task 1）、`ExternalModelHack.BeginBatch/EndBatch`（Task 3）
- Produces: なし（既存挙動の変更のみ）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/StudioModelStatNameTests.cs` を新規作成する。
プロバイダが希望と異なる group を返しても名前解決が壊れないことを、名前生成規則の側から固定する。

```csharp
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// StudioModelStat の名前生成を固定する。
    /// タイムライン XML はモデルを name で照合するため、
    /// group 採番がプロバイダ側で変わっても name の作られ方が変わってはいけない
    /// </summary>
    public class StudioModelStatNameTests
    {
        private static MTEP.StudioModelStat CreateStat(int group)
        {
            var info = new MTEP.OfficialObjectInfo
            {
                type = MTEP.StudioModelType.Mod,
                label = "テスト家具",
                fileName = "test_furniture.menu",
            };
            return new MTEP.StudioModelStat(
                info, group, null, PhotoTransTargetObject.AttachPoint.Null, -1, null, "ModItemExplorer", true);
        }

        [Fact]
        public void group0ならサフィックスが付かない()
        {
            var stat = CreateStat(0);
            Assert.Equal("test_furniture.menu", stat.name);
            Assert.Equal("テスト家具", stat.displayName);
        }

        [Fact]
        public void group指定でサフィックスが付く()
        {
            var stat = CreateStat(2);
            Assert.Equal("test_furniture.menu (2)", stat.name);
            Assert.Equal("テスト家具 (2)", stat.displayName);
        }

        [Fact]
        public void SetGroupで名前が付け直される()
        {
            var stat = CreateStat(0);
            stat.SetGroup(3);
            Assert.Equal("test_furniture.menu (3)", stat.name);
            Assert.Equal("テスト家具 (3)", stat.displayName);
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter StudioModelStatNameTests`
Expected: FAIL（ファイル新規追加のためコンパイルは通るが、`OfficialObjectInfo` のメンバ名が違えばここで判明する。
実定義に合わせてテストを直すこと。ロジックが正しければこの時点で PASS してもよい — その場合は既存挙動を固定した回帰テストとして扱う）

- [ ] **Step 3: `SetupModels` に読み替えとバッチを入れる**

`StudioModelManager.SetupModels` の冒頭で pluginName を読み替え、生成ループを
`BeginBatch` / `EndBatch` で挟む。

```csharp
public void SetupModels(List<TimelineModelData> modelDataList)
{
    MTEUtils.LogDebug("SetupModels: count={0}", modelDataList.Count);

    // プロバイダが無いとモデルの生成・削除が黙って no-op になる
    // (ModelHackManager.GetOrDefault が studioHack へフォールバックするため)。
    // 復元できない旨をここで明示して打ち切る
    var providerId = SceneEditor.Plugin.ModelPlacerProviderRegistry.current?.id;
    if (providerId == null)
    {
        if (modelDataList.Count > 0)
        {
            MTEUtils.LogWarning(
                "モデル配置プロバイダが無いためモデルを復元できません。ModItemExplorer を導入してください (対象 {0} 件)",
                modelDataList.Count);
        }
        return;
    }

    // 旧 XML は SE 自前配置時代の pluginName を持つ。現行のプロバイダ ID へ寄せる
    foreach (var modelData in modelDataList)
    {
        modelData.pluginName = SceneEditor.Plugin.ModelPlacerProviderRegistry
            .MigratePluginName(modelData.pluginName, providerId);
    }

    var externalHack = modelHackManager.GetOrDefault(providerId) as ExternalModelHack;
    externalHack?.BeginBatch();
    try
    {
        // （既存の生成・削除ループをそのまま）
    }
    finally
    {
        externalHack?.EndBatch();
    }

    LateUpdate(true);

    MTEUtils.ExecuteNextFrame(() =>
    {
        LateUpdate(true);
    });
}
```

名前空間解決は SE 側 `PluginUtils` との衝突を避けるため、
ファイル冒頭に `using SE = COM3D2.SceneEditor.Plugin;` を追加して `SE.ModelPlacerProviderRegistry` と書いてもよい。

- [ ] **Step 4: モデル系レイヤーを条件登録にする**

`TimelineIntegration.Initialize` のモデル系 4 レイヤー登録を、プロバイダが見つかったときだけ行う。

```csharp
if (modelPlacer != null)
{
    timelineManager.RegisterLayer(
        typeof(MTEP.ModelTimelineLayer), MTEP.ModelTimelineLayer.Create);
    timelineManager.RegisterLayer(
        typeof(MTEP.ModelBoneTimelineLayer), MTEP.ModelBoneTimelineLayer.Create);
    timelineManager.RegisterLayer(
        typeof(MTEP.ModelShapeKeyTimelineLayer), MTEP.ModelShapeKeyTimelineLayer.Create);
    timelineManager.RegisterLayer(
        typeof(MTEP.ModelMaterialTimelineLayer), MTEP.ModelMaterialTimelineLayer.Create);
}
```

`modelPlacer` は Task 3 で導入したローカル変数を使い回す。
警告ログは Task 3 の `else` 節に既にあるため増やさない。

- [ ] **Step 5: ビルドとテスト**

Run: SE の MSBuild コマンド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全件 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin.Tests/StudioModelStatNameTests.cs
git commit -m "feat(timeline): 旧プラグイン名の読み替えとモデルレイヤーの条件登録を行う"
```

---

### Task 5: MIE — 公式 BG プレハブ / マイルームオブジェクトの生成経路

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`CreateBgObject` の隣に 2 メソッド追加）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`（`CreateModel` の分岐に `Prefab` / `MyRoom` を追加）

**Interfaces:**
- Consumes: `SelfModelPlacer.RegisterCreatedModel`（private。既存の合流点）
- Produces:
  - `SelfModelPlacer.CreateGameModel(string assetName, int group, bool visible) → StudioModelStatWrapper`
  - `SelfModelPlacer.CreateMyRoomObject(int myRoomId, int group, bool visible) → StudioModelStatWrapper`

- [ ] **Step 1: 公式 BG プレハブのロードを移植する**

SE の `SceneEditorHack.LoadGameModel`（削除済み。git 履歴か本計画の記述を参照）を
`SelfModelPlacer` へ逐語移植する。ラッパー GameObject / ギズモ / 履歴の扱いは
既存 `RegisterCreatedModel` に合流させて揃える。

```csharp
/// <summary>
/// 公式 BG プレハブ（アセットバンドル / Resources）を配置する。
/// 見つからなければ .menu 経路へフォールバックする
/// </summary>
public StudioModelStatWrapper CreateGameModel(string assetName, int group, bool visible)
{
    GameObject modelGo = null;
    try
    {
        var sourceObj = GameMain.Instance.BgMgr.CreateAssetBundle(assetName);
        if (!sourceObj)
        {
            sourceObj = Resources.Load<GameObject>("Prefab/" + assetName);
        }
        if (!sourceObj)
        {
            sourceObj = Resources.Load<GameObject>("BG/" + assetName);
        }
        if (!sourceObj)
        {
            return CreateModel(assetName, group, visible);
        }

        modelGo = UnityEngine.Object.Instantiate(sourceObj);
        modelGo.name = assetName;
        SetLayerRecursively(modelGo, GetModelLayer());

        // 影・当たり判定・パーティクルの既定をスタジオ配置向けに整える
        foreach (var renderer in modelGo.GetComponentsInChildren<Renderer>())
        {
            if (renderer != null && renderer.gameObject.name.Contains("castshadow"))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        foreach (var collider in modelGo.GetComponentsInChildren<Collider>())
        {
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
        foreach (var particle in modelGo.GetComponentsInChildren<ParticleSystem>())
        {
            if (particle != null)
            {
                var main = particle.main;
                main.loop = true;
            }
        }

        // Mesh/Material はアセットバンドル / Resources 所有のため破棄対象に積まない
        return RegisterCreatedModel(
            modelGo, assetName, group, visible, new List<UnityEngine.Object>());
    }
    catch (Exception e)
    {
        MTEUtils.LogWarning("公式BGモデルの配置に失敗しました。{0}", assetName);
        MTEUtils.LogException(e);
        if (modelGo != null)
        {
            UnityEngine.Object.Destroy(modelGo);
        }
        return null;
    }
}
```

- [ ] **Step 2: マイルームオブジェクトのロードを移植する**

```csharp
/// <summary>
/// マイルームカスタムの配置オブジェクトを配置する。
/// fileName は "MYR_<id>" 形式で、SceneEditor 側の名前解決もこの形を前提にしている
/// </summary>
public StudioModelStatWrapper CreateMyRoomObject(int myRoomId, int group, bool visible)
{
    GameObject modelGo = null;
    try
    {
        var data = MyRoomCustom.PlacementData.GetData(myRoomId);
        if (data == null)
        {
            MTEUtils.LogWarning("マイルームオブジェクトが見つかりません。id={0}", myRoomId);
            return null;
        }

        var prefab = data.GetPrefab();
        modelGo = UnityEngine.Object.Instantiate(prefab);
        SetLayerRecursively(modelGo, GetModelLayer());

        return RegisterCreatedModel(
            modelGo, "MYR_" + myRoomId, group, visible, new List<UnityEngine.Object>());
    }
    catch (Exception e)
    {
        MTEUtils.LogWarning("マイルームオブジェクトの配置に失敗しました。id={0}", myRoomId);
        MTEUtils.LogException(e);
        if (modelGo != null)
        {
            UnityEngine.Object.Destroy(modelGo);
        }
        return null;
    }
}
```

- [ ] **Step 3: 規約側の分岐を埋める**

`ModelPlacerProvider.CreateModel` の `switch` に追加する。

```csharp
case "Prefab":
    model = placer.CreateGameModel(fileName, group, visible);
    break;
case "MyRoom":
    model = placer.CreateMyRoomObject(myRoomId, group, visible);
    break;
```

`default` の警告分岐は残す（未知の種別が来たときに黙って落ちないようにするため）。

- [ ] **Step 4: ビルドする**

Run: MIE の MSBuild コマンド
Expected: ビルド成功

- [ ] **Step 5: コミット（MIE リポジトリ）**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs
git commit -m "feat(model): 公式BGプレハブとマイルームオブジェクトを配置できるようにする"
```

---

### Task 6: MIE — アタッチポイント変換とバッチ抑止

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`AttachPoints` の近くに変換表、`RegisterCreatedModel` にバッチ判定）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`（`AttachModel` / `BeginBatch` / `EndBatch`）

**Interfaces:**
- Consumes: `SelfModelPlacer.AttachPoints`（`displayName` / `boneName` を持つ既存リスト）、`SelfModelPlacer.Attach(StudioModelStatWrapper, Maid, AttachPoint)`
- Produces:
  - `SelfModelPlacer.AttachByBoneName(StudioModelStatWrapper model, Maid maid, string boneName) → void`
  - `SelfModelPlacer.BeginBatch()` / `SelfModelPlacer.EndBatch()`
  - `SelfModelPlacer.isBatching → bool`

- [ ] **Step 1: ボーン名でアタッチできるようにする**

SE 側は `MaidCache.GetAttachPointTransform` でボーン Transform まで解決してから
その名前を渡してくるため、MIE は enum の対応表を持つ必要がない。
既存 `Attach` は `AttachPoints` に載っているポイントしか受け取れないので、
ボーン名から直接アタッチする入口を足す。

```csharp
/// <summary>
/// ボーン名を直接指定してアタッチする。
/// SceneEditor のタイムラインは AttachPoints に無いボーンも指定してくるため、
/// 一覧に無い場合は臨時のアタッチポイントを作って委譲する
/// （boneName が空なら解除）
/// </summary>
public void AttachByBoneName(StudioModelStatWrapper model, Maid maid, string boneName)
{
    if (maid == null || string.IsNullOrEmpty(boneName))
    {
        Attach(model, null, null);
        return;
    }

    var point = AttachPoints.Find(p => p.boneName == boneName)
        ?? new AttachPoint { displayName = boneName, boneName = boneName };
    Attach(model, maid, point);
}
```

`AttachPoint` のフィールド構成（`displayName` / `boneName` 以外に必須の値があるか）は
`SelfModelPlacer.AttachPoints` の初期化コードで確認し、臨時生成でも破綻しないようにすること。

併せて `RestoreAttachState` の `AttachPoints.Find` も、見つからないときに
臨時の `AttachPoint` へフォールバックするよう直す。
タイムライン経由でアタッチしたモデルを配置プリセットへ保存・復元できるようにするため。

- [ ] **Step 2: バッチ抑止を入れる**

```csharp
/// <summary>
/// タイムライン読込のような一括操作の最中か。
/// 真の間は配置履歴の登録と選択の切り替えを行わない
/// （読込のたびに Undo 履歴が大量に積まれるのを防ぐ）
/// </summary>
public bool isBatching { get; private set; }

public void BeginBatch()
{
    isBatching = true;
}

public void EndBatch()
{
    isBatching = false;
}
```

`RegisterCreatedModel` 内の `selectedModel = wrapper;` と `history.RegisterCreate(...)` を
`if (!isBatching) { ... }` で囲む。

**`Attach` 内の `history.RegisterAttach(model, historyState)` も同じガードで囲むこと。**
`ExternalModelHack.CreateModel` は生成直後に必ず `UpdateAttachPoint` を呼び、
`SetupModels` も既存モデルに対して `UpdateAttachPoint` を呼ぶため、
ここを抜かすとタイムライン読込のたびにアタッチ履歴が積まれ続け、
このタスクの目的（Undo 履歴を汚さない）を達成できない。

- [ ] **Step 3: 規約側を繋ぐ**

```csharp
public static void AttachModel(GameObject obj, Maid maid, string boneName)
{
    var model = placer.FindModelByGameObject(obj);
    if (model != null)
    {
        placer.AttachByBoneName(model, maid, boneName);
    }
}

public static void BeginBatch()
{
    placer.BeginBatch();
}

public static void EndBatch()
{
    placer.EndBatch();
}
```

`SelfModelPlacer.Attach` が `maid == null` / `point == null` を解除として扱わない実装なら、
解除経路を `Attach` 側に足すこと（既存の解除処理があればそれを呼ぶ）。

- [ ] **Step 4: ビルドする**

Run: MIE の MSBuild コマンド
Expected: ビルド成功

- [ ] **Step 5: コミット（MIE リポジトリ）**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs
git commit -m "feat(model): タイムラインのアタッチポイント変換と一括操作の抑止を追加する"
```

---

### Task 7: MIE — 配置プリセットに生成種別を保存する

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPreset.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`GetPlacementXml` / `ApplyPlacementXml`）

**Interfaces:**
- Consumes: `SelfModelPlacer.CreateGameModel` / `CreateMyRoomObject`（Task 5）
- Produces: なし（XML スキーマの後方互換拡張）

- [ ] **Step 1: プリセットの要素を増やす**

`ModelPlacementPreset` に生成種別を持たせる。既定値は旧 XML 互換のため `Mod`。

```csharp
/// <summary>
/// 生成種別。SceneEditor 側 StudioModelType の enum 名に合わせる
/// （Mod / Prefab / Asset / MyRoom）。要素の無い旧 XML は Mod として読む
/// </summary>
public string type = "Mod";

/// <summary>MyRoom 種別のときの配置データ ID</summary>
public int myRoomId;
```

- [ ] **Step 2: 保存側を直す**

`GetPlacementXml` でモデル 1 件を書き出すところに `type` / `myRoomId` を載せる。
種別は `fileName` から判定する（`.menu` → `Mod`、`.asset_bg` → `Asset`、`MYR_` 始まり → `MyRoom`、
それ以外 → `Prefab`）。判定は 1 か所のヘルパにまとめること。

- [ ] **Step 3: 読込側を直す**

`ApplyPlacementXml` の生成分岐を `type` で切り替える。

```csharp
switch (preset.type)
{
    case "Asset":
        CreateBgObject(/* .asset_bg 名 */, preset.group, preset.visible);
        break;
    case "Prefab":
        CreateGameModel(preset.fileName, preset.group, preset.visible);
        break;
    case "MyRoom":
        CreateMyRoomObject(preset.myRoomId, preset.group, preset.visible);
        break;
    default:
        CreateModel(preset.fileName, preset.group, preset.visible);
        break;
}
```

- [ ] **Step 4: 旧 XML で読めることを確かめる**

`type` 要素を持たない既存の配置プリセット XML（`SavePreset` で作った過去ファイル。無ければ手で
`type` 行を削った XML を作る）を `LoadPreset` で読み、モデルが配置されることを確認する。
確認はビルド後の実機で行うため、この段階ではビルド成功をもって次へ進み、
実機確認は Task 9 のチェックリストに含める。

Run: MIE の MSBuild コマンド
Expected: ビルド成功

- [ ] **Step 5: コミット（MIE リポジトリ）**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPreset.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(model): 配置プリセットに生成種別を保存する"
```

---

### Task 8: ドキュメントと CHANGELOG

**Files:**
- Create: `docs-site/dev/model-placer-guest-guide.md`（SE）
- Modify: `docs-site/dev/index.md`（SE）
- Modify: `docs-site/guide/timeline.md`（SE）
- Modify: `CHANGELOG.md`（SE / MIE 両方）

**Interfaces:**
- Consumes: Task 1〜7 で確定した規約メンバ名・シグネチャ
- Produces: なし

- [ ] **Step 1: ゲストガイドを書く**

`docs-site/dev/model-placer-guest-guide.md` を、既存の `scene-preset-provider-guide.md` の構成に倣って書く。
含める内容:

- 規約の目的（配置のライフサイクルをゲスト側が持ち、タイムラインはキーフレーム対象として扱う）
- `ModelPlacerProviderAttribute` を自前定義すること（短名一致で判定するため型の同一性は不要）
- 必須メンバの表（Task 1 の `TryBind` が探すシグネチャと完全に一致させる）
- 任意メンバの表（`GetModelDisplayName` / `BeginBatch` / `EndBatch`）
- `type` に渡る文字列は `Mod` / `Prefab` / `Asset` / `MyRoom`
- `AttachModel` の第 3 引数は追従先ボーンの名前。SE 側が `AttachPoint` からボーン Transform まで
  解決して渡すため、ゲスト側に enum の対応表は要らない。`maid == null` または空文字で解除
- 契約: `CreateModel` に渡る `group` はゲスト側への**ヒント**でしかなく、ゲストが別の値で採番してよい。
  タイムラインが使う group は `ModelHackManager.FixGroup` が列挙順で振り直すため、
  SE はゲストの採番を読み戻さない
- 既知の相互作用: ゲスト側がギズモを持つ場合、タイムライン再生中は毎フレーム Transform が
  上書きされるため、ギズモ操作は再生停止時のみ有効になる
- MIE の `ModelPlacerProvider.cs` を実装例として参照リンクする

- [ ] **Step 2: 索引とユーザーガイドを直す**

- `docs-site/dev/index.md` に新ガイドへのリンクを追加する（既存リンクの並び順に合わせる）
- `docs-site/guide/timeline.md` に「モデル系レイヤーの利用には ModItemExplorer が必要」旨を追記する

- [ ] **Step 3: CHANGELOG を書く**

SE の `CHANGELOG.md` 先頭の未リリース節に、非互換変更として記載する。

```markdown
- タイムラインのモデル配置を ModItemExplorer へ一本化しました。モデル系レイヤー
  （モデル / モデルボーン / モデルシェイプキー / モデルマテリアル）の利用には
  ModItemExplorer が必要になります
```

MIE の `CHANGELOG.md` にも対応する追加を記載する。

```markdown
- SceneEditor のタイムラインへモデル配置を提供するようになりました
- 公式 BG プレハブとマイルームオブジェクトを配置できるようになりました
```

- [ ] **Step 4: ドキュメントをレビューする**

docs-review スキルでドキュメントの整合性をレビューし、指摘を取り込む。

- [ ] **Step 5: コミット（両リポジトリ）**

```bash
# SE
git add docs-site/dev/model-placer-guest-guide.md docs-site/dev/index.md docs-site/guide/timeline.md CHANGELOG.md
git commit -m "docs(timeline): モデル配置プロバイダ規約のガイドを追加する"

# MIE
git add CHANGELOG.md
git commit -m "docs(model): SceneEditor 連携の変更履歴を追加する"
```

---

### Task 9: コードレビューと実機確認

**Files:** なし（レビューと確認のみ）

**Interfaces:**
- Consumes: Task 1〜8 の成果物
- Produces: なし

- [ ] **Step 1: コードレビュー**

code-review スキルで SE / MIE 双方の差分をレビューし、妥当な指摘を取り込む。
このリポジトリの運用ではユーザーへ提示する前のコードレビューが必須。

- [ ] **Step 2: テストを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`（SE）
Expected: 全件 PASS

- [ ] **Step 3: 実機確認**

ゲーム起動中は DLL がロックされて差し替えできないため、次回ゲーム起動時にまとめて確認する。
確認項目（spec の実機確認チェックリストと同一）:

1. MIE の一覧からモデルを配置 → タイムラインのモデルレイヤーに現れる
2. タイムラインでモデルを追加 → MIE の一覧に現れる
3. タイムライン XML を読み込み → 不足モデルが MIE 側に自動配置される / 余剰が削除される
4. 公式 BG プレハブ・マイルームオブジェクトが MIE 経由で配置できる
5. アタッチポイント指定でメイドのボーンへ追従する
6. MIE の配置プリセット保存 → 適用でモデルが総入れ替えされ、タイムラインが再バインドする
7. `type` 要素の無い旧配置プリセット XML が読める
8. MIE 未導入の環境でモデル系レイヤーが無効化され、警告が 1 回だけ出る
9. ボーン編集 / マテリアル編集のモデル一覧に同じモデルが二重に並んでいない
10. 同一 `fileName` のモデルを group 違いで複数配置し、削除と再配置を挟んでも
    ボーン / マテリアルの編集内容が保持される（`FixGroup` と噛み合わず
    `StudioModelStat` が作り直され続けていないことの確認）
11. アタッチ付きのタイムラインを読み込んでも MIE の Undo 履歴が積み上がらない

- [ ] **Step 4: 結果を memory に反映する**

`timeline-window-integration` メモリに、この統合の完了と実機確認の結果を追記する。

---

## 補足: 参照が必要になったときの調べ先

- ゲーム側実装: `W:\COM3D2_5\work\Assembly-CSharp\`（逆コンパイル済みソース）を grep する
- 既存の規約実装例: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetProviderRegistry.cs`（SE）、
  `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPresetProvider.cs`（MIE）
- 実機での挙動確認: ゲーム起動中なら MCP `com3d25-devbridge` の `eval_csharp` を使う。
  Unity 型は完全修飾名で書くこと（`UnityEngine.Time.frameCount`）

## レビュー却下メモ

- MIE 未導入環境で XML 読込時に個別モデルが復元できない場合の UI 可視エラーの有無（確信度: 中） — `SetupModels` の冒頭でプロバイダ不在を検出して警告ログを出す形で対応済み。UI 側への追加表示は要件外のため見送り
