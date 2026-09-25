# モデルの表示レイヤーをタイムラインへ保存する Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ModItemExplorer のモデルの表示レイヤー（Unity レイヤー番号）を、タイムライン XML のモデル定義 `<ModelLayer>` として保存・復元する。

**Architecture:** 既存のアタッチ同期（`GetModelAttachBone`）と同じ形にする。
- プロバイダの任意メンバ `GetModelLayer` / `SetModelLayer` で読み書きする
- `StudioModelStat.layer` を経由して `TimelineModelData` へ流す
- 読込時は `SetupModels` がプロバイダへ適用する

**Tech Stack:** C#（COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1）、`XmlSerializer`、xUnit（SE テスト）

**Spec:** `docs/superpowers/specs/2026-09-25-model-layer-timeline-design.md`

## Global Constraints

- 両構成（`GameVersion=COM3D2` / `COM3D25`）でビルドが通ること
- SE のテストは、COM3D2 → COM3D25 の順にビルドしてから `dotnet test` する
- テストで Unity のネイティブ呼び出しはしない
- ビルドは MSBuild を直接叩く（`debug.bat` は使わない）。`deploy.bat` は実行しない。push はしない
- コメント・ログは日本語
- レイヤー未指定は `-1`。XML の要素名は `ModelLayer`。`TimelineData.CurrentVersion` は上げない
- プロバイダ連携はリフレクション経由（MIE から SE の型をコンパイル時参照しない）

SE ビルド + テスト（以下「SE ビルド」）:
```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
dotnet test source/COM3D2.SceneEditor.Plugin.Tests 2>&1 | tail -1
```

MIE ビルド（以下「MIE ビルド」）:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
```

## Review Focus

- 読込直後の `LateUpdate` がプロバイダの値を読み戻す。適用より前に読むと、既定レイヤーがタイムラインへ書き戻されて保存値が消える。`SetupModels` の中で、`LateUpdate(true)` より前に適用すること（Task 3 の実機確認）
- 旧 MIE（API 無し）と組み合わせる。stat は `-1` のまま、XML に要素が出ず、読込で例外が出ない（Task 2 のバインドテストと Task 3 の null ガード）
- 既存のモデルを流用するときも、レイヤーを適用する。同名モデルが残ったまま別のタイムラインを読むと、前のレイヤーが残る（Task 3）
- 範囲外のレイヤー番号（手編集した XML の 99 など）。MIE の `SetLayer` が 0〜31 以外を無視するので、例外にならない（Task 4）
- `SetModelLayer` で MIE の Undo 履歴が積まれない。読込は `BeginBatch` の区間内で、`SetLayer` は元から履歴を積まない（Task 4）

---

### Task 1: レイヤーの値を stat とタイムライン XML に持たせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:11-25`（`TimelineModelXml`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:17-62`（`TimelineModelData`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs`（`layer` フィールドと `FromModel`）
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ModelLayerXmlTests.cs`

**Interfaces:**
- Produces:
  - `public int StudioModelStat.layer`（既定 -1）
  - `public int TimelineModelData.layer`（既定 -1）
  - `public int TimelineModelXml.layer`（`[XmlElement("ModelLayer")]`、既定 -1、`-1` は書き出さない）
  - `public const int StudioModelStat.UnspecifiedLayer = -1`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelLayerXmlTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデル定義の表示レイヤー (&lt;ModelLayer&gt;) の保存と読込を固定する。
    /// 未指定 (-1) は書き出さず、要素の無い旧 XML は未指定として読む
    /// </summary>
    public class ModelLayerXmlTests
    {
        private static string Serialize(TimelineModelXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineModelXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineModelXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 指定したレイヤーはModelLayer要素として往復する()
        {
            var data = new TimelineModelData { name = "cup.menu", pluginName = "ModItemExplorer", layer = 10 };

            var text = Serialize(data.ToXml());
            var restored = new TimelineModelData();
            restored.FromXml(Deserialize(text));

            Assert.Contains("<ModelLayer>10</ModelLayer>", text);
            Assert.Equal(10, restored.layer);
        }

        [Fact]
        public void 未指定のレイヤーは書き出さない()
        {
            var data = new TimelineModelData { name = "cup.menu", pluginName = "ModItemExplorer" };

            var text = Serialize(data.ToXml());

            Assert.Equal(StudioModelStat.UnspecifiedLayer, data.layer);
            Assert.DoesNotContain("ModelLayer", text);
        }

        [Fact]
        public void 要素の無い旧XMLは未指定として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineModelXml><Name>cup.menu</Name><PluginName>ModItemExplorer</PluginName></TimelineModelXml>";

            var restored = new TimelineModelData();
            restored.FromXml(Deserialize(text));

            Assert.Equal(StudioModelStat.UnspecifiedLayer, restored.layer);
        }

        [Fact]
        public void レイヤー0も指定として書き出す()
        {
            // Unity の Default レイヤーは 0。未指定 (-1) と区別して保存する
            var data = new TimelineModelData { name = "cup.menu", layer = 0 };

            Assert.Contains("<ModelLayer>0</ModelLayer>", Serialize(data.ToXml()));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ModelLayerXmlTests`
Expected: `TimelineModelData` に `layer` が無く、`StudioModelStat` に `UnspecifiedLayer` が無いので、コンパイルエラー（CS0117 / CS1061）

- [ ] **Step 3: 実装する**

`StudioModelStat.cs`
- `UnassignedGroup` 定数の下に次を足す:

```csharp
        /// <summary>表示レイヤー未指定。プロバイダがレイヤーを扱えない、または旧 XML から読んだ</summary>
        public const int UnspecifiedLayer = -1;
```

- `public bool visible { get; set; }` の下に次を足す:

```csharp
        /// <summary>
        /// 表示レイヤー (Unity のレイヤー番号)。プロバイダの GetModelLayer から同期し、
        /// タイムラインのモデル定義として保存する。未指定は UnspecifiedLayer
        /// </summary>
        public int layer { get; set; } = UnspecifiedLayer;
```

- `FromModel` の `visible = model.visible;` の下に `layer = model.layer;` を足す

`TimelineXml.cs` の `TimelineModelXml` の `pluginName` の下に次を足す:

```csharp
        /// <summary>
        /// 表示レイヤー (Unity のレイヤー番号)。SE 独自で、未指定 (-1) は書き出さない。
        /// 要素名を Layer にしないのは、タイムラインレイヤーの &lt;Layer&gt; と紛れるため
        /// </summary>
        [XmlElement("ModelLayer")]
        public int layer = StudioModelStat.UnspecifiedLayer;
```

`ShouldSerializeattachMaidSlotNo` の下に次を足す:

```csharp
        public bool ShouldSerializelayer() { return layer != StudioModelStat.UnspecifiedLayer; }
```

`TimelineData.cs` の `TimelineModelData`
- `public string pluginName;` の下に次を足す:

```csharp
        /// <summary>表示レイヤー (Unity のレイヤー番号)。未指定は StudioModelStat.UnspecifiedLayer</summary>
        public int layer = StudioModelStat.UnspecifiedLayer;
```

- `FromModel` に `layer = model.layer;` を足す
- `FromXml` に `layer = xml.layer;` を足す
- `ToXml` の初期化子に `layer = layer,` を足す

- [ ] **Step 4: テストが通ることを確かめる**

Run: SE ビルド（`dotnet test` を `--filter ModelLayerXmlTests` 付きで 1 回、無しで 1 回）
Expected: 両構成のビルドがエラーなし。`ModelLayerXmlTests` の 4 件と既存テストが PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelLayerXmlTests.cs
git commit -m "feat(timeline): モデル定義に表示レイヤー (ModelLayer) を保存できるようにする"
```

---

### Task 2: プロバイダの任意メンバ GetModelLayer / SetModelLayer をバインドする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs`（`ModelPlacerProvider` のフィールド、`TryBind` の任意メンバ）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs`

**Interfaces:**
- Produces:
  - `public Func<GameObject, int> ModelPlacerProvider.getModelLayer`（未実装なら null）
  - `public Action<GameObject, int> ModelPlacerProvider.setModelLayer`（未実装なら null）

- [ ] **Step 1: 失敗するテストを書く**

`ModelPlacerProviderTests.cs`
- `FullDummyProvider` の `GetModelAttachBone` の下に次を足す:

```csharp
        public static int GetModelLayer(GameObject obj) => -1;
        public static void SetModelLayer(GameObject obj, int layer) { }
```

- `MinimalDummyProvider` の下に、片方だけを持つプロバイダを足す:

```csharp
    /// <summary>レイヤーの取得だけを持ち、設定を欠いたプロバイダ</summary>
    [ModelPlacerProvider]
    public static class GetLayerOnlyDummyProvider
    {
        public static string ModelPlacerId => "GetLayerOnlyPlacer";
        public static string ModelPlacerDisplayName => "取得のみ";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
        public static int GetModelLayer(GameObject obj) => 0;
    }
```

- `必須と任意メンバを備えた型をバインドできる` の末尾に次を足す:

```csharp
            Assert.NotNull(provider.getModelLayer);
            Assert.NotNull(provider.setModelLayer);
```

- `任意メンバが無くてもバインドできる` の末尾に次を足す:

```csharp
            Assert.Null(provider.getModelLayer);
            Assert.Null(provider.setModelLayer);
```

- 新しいテストを足す:

```csharp
        [Fact]
        public void レイヤーの取得と設定は片方だけならどちらも無効()
        {
            // 読めても書けないと、保存した値を読込で戻せず食い違うため
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(GetLayerOnlyDummyProvider), out var provider, out var error);

            Assert.True(bound, error);
            Assert.Null(provider.getModelLayer);
            Assert.Null(provider.setModelLayer);
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ModelPlacerProviderTests`
Expected: `ModelPlacerProvider` に `getModelLayer` が無く、コンパイルエラー（CS1061）

- [ ] **Step 3: 実装する**

`ModelPlacerProviderRegistry.cs` の `ModelPlacerProvider` で、`getModelAttachBone` の下に次を足す:

```csharp
        /// <summary>モデルの表示レイヤー (Unity のレイヤー番号)。管理外・不明なら -1 (任意メンバ)</summary>
        public Func<GameObject, int> getModelLayer;

        /// <summary>モデルの表示レイヤーを変える (任意メンバ。getModelLayer と対でだけ有効)</summary>
        public Action<GameObject, int> setModelLayer;
```

`TryBind` の `getAttachBone` ブロックの後、`return true;` の前に次を足す:

```csharp
            // 保存 (取得) と読込 (設定) は対で使うため、片方だけなら両方とも無効にする
            var getLayer = type.GetMethod("GetModelLayer", flags, null, new[] { typeof(GameObject) }, null);
            var setLayer = type.GetMethod("SetModelLayer", flags, null, new[] { typeof(GameObject), typeof(int) }, null);
            if (getLayer != null && getLayer.ReturnType == typeof(int)
                && setLayer != null && setLayer.ReturnType == typeof(void))
            {
                provider.getModelLayer = (Func<GameObject, int>)Delegate.CreateDelegate(
                    typeof(Func<GameObject, int>), getLayer);
                provider.setModelLayer = (Action<GameObject, int>)Delegate.CreateDelegate(
                    typeof(Action<GameObject, int>), setLayer);
            }
```

- [ ] **Step 4: テストが通ることを確かめる**

Run: SE ビルド
Expected: 両構成ともエラーなし、テストはすべて PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs
git commit -m "feat(timeline): モデル配置プロバイダの任意メンバ GetModelLayer / SetModelLayer をバインドする"
```

---

### Task 3: レイヤーをプロバイダと同期し、読込・複製で適用する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ModelHackBase.cs`（`IModelHack` / `ModelHackBase` に `UpdateLayer`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs`（同期・適用）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs`（`UpdateLayer`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs`（`LateUpdate` の変化判定、`SetupModels`）

**Interfaces:**
- Consumes: `StudioModelStat.layer` / `UnspecifiedLayer`、`TimelineModelData.layer`（Task 1）、`ModelPlacerProvider.getModelLayer` / `setModelLayer`（Task 2）
- Produces:
  - `void IModelHack.UpdateLayer(StudioModelStat model)`
  - `public void ModelHackManager.UpdateLayer(StudioModelStat model)`

Unity の GameObject とプロバイダの実体が要るため、単体テストは書けない。確認は Task 6 の実機で行う。

- [ ] **Step 1: ModelHackBase に UpdateLayer を足す**

`IModelHack` の `void SetModelVisible(...)` の下に次を足す:

```csharp
        /// <summary>stat の表示レイヤーを実体へ適用する。未指定なら何もしない</summary>
        void UpdateLayer(StudioModelStat model);
```

`ModelHackBase` の `SetModelVisible` の後に次を足す:

```csharp
        public virtual void UpdateLayer(StudioModelStat model)
        {
            // do nothing
        }
```

- [ ] **Step 2: ExternalModelHack で同期と適用を行う**

`GetOrCreateStat`
- キャッシュ経路の `SyncAttachFromProvider(cached, obj);` の下に `SyncLayerFromProvider(cached, obj);` を足す
- 新規経路の `SyncAttachFromProvider(stat, obj);` の下に `SyncLayerFromProvider(stat, obj);` を足す

`SyncAttachFromProvider` の前に次を足す:

```csharp
        /// <summary>
        /// プロバイダ側の UI で切り替えた表示レイヤーを stat へ取り込む。
        /// プロバイダが扱えない (任意メンバが無い) 場合は未指定のまま
        /// </summary>
        private void SyncLayerFromProvider(StudioModelStat stat, GameObject obj)
        {
            if (_provider.getModelLayer == null)
            {
                return;
            }

            try
            {
                stat.layer = _provider.getModelLayer(obj);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

`CreateModel` の末尾（`UpdateAttachPoint(model);` の後）に次を足す:

```csharp
            // 複製・読込で stat に載っているレイヤーを新しい実体へ移す
            UpdateLayer(model);
```

`SetModelVisible` の後に次を足す:

```csharp
        public override void UpdateLayer(StudioModelStat model)
        {
            var obj = model.obj as GameObject;
            if (obj == null || model.layer == StudioModelStat.UnspecifiedLayer
                || _provider.setModelLayer == null)
            {
                return;
            }

            // UI は StudioModelManager 側の複製 stat を渡してくるので、modelList が返す stat へも写す
            // (写さないと直後の LateUpdate で旧値との差分と誤判定される)
            if (_statMap.TryGetValue(obj, out var cached) && cached != model)
            {
                cached.layer = model.layer;
            }

            _provider.setModelLayer(obj, model.layer);
        }
```

- [ ] **Step 3: ModelHackManager.UpdateLayer**

`ModelHackManager.cs` の `UpdateAttachPoint` の後に次を足す:

```csharp
        public void UpdateLayer(StudioModelStat model)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack != null)
                {
                    modelHack.UpdateLayer(model);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

- [ ] **Step 4: StudioModelManager の変化判定と読込時の適用**

`LateUpdate` の `if (attachChanged || cachedModel.visible != model.visible)` を次に変える:

```csharp
                if (attachChanged || cachedModel.visible != model.visible
                    || cachedModel.layer != model.layer)
```

`SetupModels` のループを次にする。`model == null` の分岐の直後、`else if` の前に置いた新規生成ブロックはそのまま残す:

```csharp
                foreach (var modelData in modelDataList)
                {
                    var model = modelList.Find(m => m.name == modelData.name);
                    // アタッチは version 37 からモデルキーの値。ここでは付けず、レイヤーの適用に任せる
                    if (model == null)
                    {
                        model = CreateModelStat(
                            modelData.name,
                            null,
                            AttachPoint.Null,
                            -1,
                            null,
                            modelData.pluginName,
                            true);
                        // 生成時に ExternalModelHack.CreateModel が適用する
                        model.layer = modelData.layer;
                        modelHackManager.CreateModel(model);

                        MTEUtils.LogDebug(/* 既存のまま */);
                    }
                    else
                    {
                        // 同名モデルを流用するときも、前のタイムラインのレイヤーを持ち越さない。
                        // プラグインの付け替えは実体を作り直す (CreateModel が stat のレイヤーを適用する) ので、先に書いておく
                        var hasLayer = modelData.layer != StudioModelStat.UnspecifiedLayer;
                        if (hasLayer)
                        {
                            model.layer = modelData.layer;
                        }

                        if (model.pluginName != modelData.pluginName)
                        {
                            modelHackManager.ChangePluginName(model, modelData.pluginName);
                        }
                        else if (hasLayer)
                        {
                            modelHackManager.UpdateLayer(model);
                        }
                    }
                }
```

`MTEUtils.LogDebug("Create model: ...")` の呼び出しは、既存の引数のまま残す。

- [ ] **Step 5: ビルドとテスト**

Run: SE ビルド
Expected: 両構成ともエラーなし、テストはすべて PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ModelHackBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs
git commit -m "feat(timeline): モデルの表示レイヤーをプロバイダと同期し、読込・複製で復元する"
```

---

### Task 4: MIE のプロバイダに GetModelLayer / SetModelLayer を足す

**Files:**
- Modify: MIE `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`

**Interfaces:**
- Produces（リフレクションで SE が解決）:
  - `public static int ModelPlacerProvider.GetModelLayer(GameObject obj)`
  - `public static void ModelPlacerProvider.SetModelLayer(GameObject obj, int layer)`

- [ ] **Step 1: 実装する**

`GetModelAttachBone` の後に次を足す:

```csharp
        /// <summary>
        /// モデルの表示レイヤー (Unity のレイヤー番号)。自前配置でなければ -1（任意メンバ）。
        /// SceneEditor がタイムラインのモデル定義として保存するのに使う
        /// </summary>
        public static int GetModelLayer(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            return model != null ? obj.layer : -1;
        }

        /// <summary>
        /// 表示レイヤーを変える（任意メンバ）。SceneEditor のタイムライン読込・複製から呼ばれる。
        /// 0〜31 以外は SetLayer が無視する。Undo 履歴は積まない
        /// </summary>
        public static void SetModelLayer(GameObject obj, int layer)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.SetLayer(model, layer);
            }
        }
```

`SelfModelPlacer.SetLayer` は `internal`。同じアセンブリなので呼べる。

- [ ] **Step 2: ビルド**

Run: MIE ビルド
Expected: 両構成ともエラーなし

- [ ] **Step 3: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs
git commit -m "feat(model): SceneEditor のタイムラインへ表示レイヤーを保存できるようプロバイダに取得・設定を足す"
```

---

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/dev/model-placer-guest-guide.md`（任意メンバの表）
- Modify: MIE `docs/external-plugin-api.md`（プロバイダの節に該当があれば）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」。git 管理外なので編集だけ）
- Modify: `docs-site/timeline/compatibility.md`（XML 互換の一覧があれば）

- [ ] **Step 1: ゲストガイド**

`model-placer-guest-guide.md` の任意メンバの表で、`GetModelAttachBone` の行の下に次を足す:

```markdown
| `GetModelLayer` | `int GetModelLayer(GameObject)` | モデルの表示レイヤー（Unity のレイヤー番号）。管理外・不明なら -1。`SetModelLayer` と対で実装すると、SceneEditor がタイムラインのモデル定義（`<ModelLayer>`）として保存・復元する |
| `SetModelLayer` | `void SetModelLayer(GameObject, int)` | 表示レイヤーを変える。タイムライン読込・モデル複製から呼ばれる。0〜31 以外は無視すること。`GetModelLayer` と片方だけでは無効 |
```

- [ ] **Step 2: MIE の外部 API ドキュメント**

Run: `grep -n "GetModelAttachBone" /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/docs/external-plugin-api.md`
- 該当する行があれば、その下に Step 1 と同じ 2 行を足す（表の列構成はその文書に合わせる）
- 無ければ変更しない

- [ ] **Step 3: 互換メモ**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」の末尾に次を足す:

```markdown
- モデル定義の表示レイヤー（`<Models>` の `<ModelLayer>`、Unity のレイヤー番号）は SE 独自。未指定（-1）は書き出さない。旧 SE や MTE は要素を読み飛ばし、レイヤーはモデル配置プロバイダの既定になる
```

Run: `grep -n "ModelLayer\|AttachPoint" docs-site/timeline/compatibility.md`
- アタッチの互換の記述があれば、同じ粒度で上と同じ内容を 1 行足す
- 無ければ変更しない

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site/dev/model-placer-guest-guide.md docs-site/timeline/compatibility.md
git commit -m "docs(timeline): モデルの表示レイヤーの保存と GetModelLayer / SetModelLayer を反映"
# MIE 側を変えた場合
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && git add docs/external-plugin-api.md && git commit -m "docs: GetModelLayer / SetModelLayer を追記"
```

---

### Task 6: 実機で確認する

ゲームを停止し、両プラグインを `debug.bat com3d25` で反映してから起動する（com3d25-restart-verify の手順）。ユーザーのタイムラインは上書きしない。保存の確認は、テスト用の別名で行う。

- [ ] **Step 1: 保存**
  - テスト用に新規タイムラインを作る（`TimelineManager.CreateNewTimeline`）。ユーザーのタイムラインを読んでテスト用の別名で保存してもよい
  - MIE のモデルを 1 つ、レイヤー行で Charactor にする
  - 30 フレーム以上待ってから、別名（例: `_layer_test`）で保存する
  - XML に `<ModelLayer>10</ModelLayer>`（Charactor の番号）が出ていること
- [ ] **Step 2: 復元**: Default へ戻してから、保存した XML を読み直す。Charactor に戻ること
- [ ] **Step 3: 旧 XML**: `<ModelLayer>` を含まない既存のタイムラインを読むと、レイヤーは MIE の既定のままであること
- [ ] **Step 4: 複製**: Charactor のモデルを「複製」すると、複製先も Charactor になること
- [ ] **Step 5: 後始末**: テスト用 XML を削除する。`tail_log` に例外が無いこと

## レビュー却下メモ

- SetupModels の if / else if でプラグイン変更時にレイヤー適用が漏れる — 元の計画は独立した if で漏れは無かった（誤読）。ただし付け替え前に stat へ書いておく改善は取り込んだ
- ゲストガイドの表の挿入位置 — 計画どおり任意メンバ表の GetModelAttachBone 行の下で正しい（確認済み）
