# タイムライン テンプレート機能 (Phase W5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE の TimelineTemplateManager + TimelineTemplateUI を SE へ移植し、選択ボーンのキーフレームをテンプレとして保存・適用できるようにする（唯一の未移植機能を消化する）。

**Architecture:** データ層（TemplateLayerXml / TemplateCategoryXml / TemplateXml + TimelineTemplateManager）は MTE からほぼ逐語移植し、`Timeline/Manager/` に置く。UI は MTE の SubWindowUIBase ではなく SE の `EditorSubWindow` 派生の新規 `TimelineTemplateWindow` として再構成する（TimelineSettingWindow / TimelineLayerWindow と同じホスト様式）。テンプレ XML の格納先は SE 設定ディレクトリ配下 `Config\SceneEditor\Template\<レイヤー名>.xml` とし、XML スキーマは MTE と完全互換。初回ロード時に SE 側が空で MTE の `UserData\MotionTimelineEditor\Template` が存在すれば XML をコピーして資産を引き継ぐ。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI), XmlSerializer, xunit（既存 Tests プロジェクト）

**Spec:** `docs/superpowers/specs/timeline-remaining-work.md` §1（テンプレート機能）および `docs/superpowers/specs/timeline-editing-roadmap.md` の Phase W5 相当

## Global Constraints

- コードコメント・ログメッセージは日本語で書く
- namespace はタイムライン移植コードの慣例に従い `COM3D2.MotionTimelineEditor.Plugin`（SE 固有ウィンドウは `COM3D2.SceneEditor.Plugin`）
- ビルド確認は MSBuild 直叩き（ゲーム起動中に debug.bat の DLL コピーで警告が出るのは無視してよい）:
  `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj`
- deploy.bat / release.bat は実行しない
- ウィンドウ ID は既存最大 8903387 の次番 **8903388** を使う

## 参照（移植元）

- `W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin\source\COM3D2.MotionTimelineEditor.Plugin\Manager\TimelineTemplateManager.cs`（411 行）
- 同 `TimelineTemplateUI.cs`（318 行）

SE 側で確認済みの前提 API（すべて存在を確認済み）:

- `TimelineManager.instance.selectedBones : HashSet<BoneData>`
- `ITimelineLayer.CreateFrame(int)` / `CreateFrame(FrameXml)` / `UpdateBones(int, IEnumerable<BoneData>)` / `ApplyCurrentFrame(bool)`
- `FrameData.UpdateBone(BoneData)` / `FrameData.ToXml() : FrameXml`
- `TimelineData.AdjustMaxFrameNo()`、`TimelineManager.RequestHistory(string)`
- `MTEUtils.ShowDialog / ShowConfirmDialog / Log / LogDebug / LogException`、`NaturalStringComparer`（namespace `COM3D2.MotionTimelineEditor`）
- SE の `PluginUtils.PluginDataPath`（= `Sybaris\UnityInjector\Config\SceneEditor`）

---

### Task 1: テンプレ格納パスの追加（PluginUtils）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs`（`GetTimelinePath` の直後、56 行目付近）

**Interfaces:**
- Produces: `PluginUtils.TemplateDirPath : string`（SE テンプレディレクトリ。無ければ作成）、`PluginUtils.GetTemplatePath(string layerName) : string`、`PluginUtils.MteTemplateDirPath : string`（MTE 資産の探索元。存在チェックは呼び出し側）

- [ ] **Step 1: プロパティとメソッドを追加**

```csharp
        /// <summary>テンプレート XML の格納先 (SE 設定ディレクトリ配下、MTE 互換スキーマ)</summary>
        public static string TemplateDirPath
        {
            get
            {
                var path = MTEUtils.CombinePaths(
                    SceneEditor.Plugin.PluginUtils.PluginDataPath, "Template");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }

        /// <summary>MTE がテンプレートを保存するディレクトリ (資産の引き継ぎ元。存在しないこともある)</summary>
        public static string MteTemplateDirPath
        {
            get => MTEUtils.CombinePaths(
                Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                "UserData", "MotionTimelineEditor", "Template");
        }

        public static string GetTemplatePath(string layerName)
        {
            return MTEUtils.CombinePaths(TemplateDirPath, layerName + ".xml");
        }
```

- [ ] **Step 2: ビルド確認**

Run: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
Expected: ビルド成功

- [ ] **Step 3: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/PluginUtils.cs
git commit -m "feat(timeline): テンプレート XML の格納パスを追加"
```

---

### Task 2: TimelineTemplateManager の移植（データ層）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTemplateManager.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineTemplateXmlTests.cs`

**Interfaces:**
- Consumes: Task 1 の `PluginUtils.TemplateDirPath` / `GetTemplatePath` / `MteTemplateDirPath`
- Produces: `TemplateLayerXml`（`OnLoad()` / `GetCategory` / `AddCategory` / `RemoveCategory` / `HasCategory` / `MoveCategory` / `CanMoveCategory` / `IsDirtyRecursive` / `ClearDirtyRecursive`）、`TemplateCategoryXml`（`GetTemplate` / `AddTemplate` / `RemoveTemplate` / `HasTemplate` / `SortTemplates` / `ApplyTemplate`）、`TemplateXml`、`TimelineTemplateManager.instance`（`LoadXml` / `SaveXml` / `GetTemplateLayer` / `AddTemplateCategory`）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineTemplateXmlTests.cs` を新規作成（既存 `TimelineSettingXmlTests.cs` と同じ流儀）:

```csharp
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineTemplateXmlTests
    {
        private static TemplateLayerXml RoundTrip(TemplateLayerXml src)
        {
            var serializer = new XmlSerializer(typeof(TemplateLayerXml));
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                return (TemplateLayerXml)serializer.Deserialize(ms);
            }
        }

        [Fact]
        public void Xmlラウンドトリップでレイヤー名とカテゴリ構造が保持される()
        {
            var src = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            var category = new TemplateCategoryXml { categoryName = "Default" };
            category.templates.Add(new TemplateXml
            {
                templateName = "手を振る",
                frames = { new FrameXml { frameNo = 5 } },
            });
            src.categories.Add(category);

            var dst = RoundTrip(src);

            Assert.Equal("MotionTimelineLayer", dst.layerName);
            var dstCategory = Assert.Single(dst.categories);
            Assert.Equal("Default", dstCategory.categoryName);
            var dstTemplate = Assert.Single(dstCategory.templates);
            Assert.Equal("手を振る", dstTemplate.templateName);
            Assert.Equal(5, Assert.Single(dstTemplate.frames).frameNo);
        }

        [Fact]
        public void OnLoadでDefaultカテゴリが先頭に補われる()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.categories.Add(new TemplateCategoryXml { categoryName = "その他" });

            layer.OnLoad();

            Assert.Equal("Default", layer.categories[0].categoryName);
            Assert.Equal(new[] { "Default", "その他" }, layer.categoryNames);
        }

        [Fact]
        public void カテゴリの追加と移動と削除が一覧へ反映される()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.OnLoad();

            Assert.NotNull(layer.AddCategory("A"));
            Assert.NotNull(layer.AddCategory("B"));
            Assert.True(layer.HasCategory("A"));

            // Default, A, B → A を下へ
            Assert.True(layer.CanMoveCategory("A", 1));
            Assert.True(layer.MoveCategory("A", 1));
            Assert.Equal(new[] { "Default", "B", "A" },
                layer.categories.Select(c => c.categoryName).ToArray());

            Assert.True(layer.RemoveCategory("B"));
            Assert.False(layer.HasCategory("B"));
        }

        [Fact]
        public void 先頭カテゴリはそれ以上上へ移動できない()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.OnLoad();

            Assert.False(layer.CanMoveCategory("Default", -1));
        }

        [Fact]
        public void ダーティ判定はカテゴリの変更にも反応する()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.OnLoad();
            layer.ClearDirtyRecursive();
            Assert.False(layer.IsDirtyRecursive());

            layer.categories[0].dirty = true;
            Assert.True(layer.IsDirtyRecursive());

            layer.ClearDirtyRecursive();
            Assert.False(layer.IsDirtyRecursive());
        }
    }
}
```

- [ ] **Step 2: テストが失敗（コンパイルエラー）することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj`
Expected: `TemplateLayerXml` 未定義でコンパイル失敗

- [ ] **Step 3: TimelineTemplateManager.cs を作成**

MTE 版の逐語移植 + SE 差分は 3 点のみ: (1) `using COM3D2.MotionTimelineEditor;`（NaturalStringComparer 用）、(2) `LoadXml` 冒頭で MTE 資産の初回インポート、(3) クラス先頭のコメント。

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [XmlRoot("TemplateLayer")]
    public class TemplateLayerXml
    {
        [XmlElement("LayerName")]
        public string layerName;

        [XmlElement("Category")]
        public List<TemplateCategoryXml> categories = new List<TemplateCategoryXml>();

        [XmlIgnore]
        public List<string> categoryNames = new List<string>();

        [XmlIgnore]
        private bool _dirty = false;

        [XmlIgnore]
        public bool dirty
        {
            get => _dirty;
            set => _dirty = value;
        }

        public void OnLoad()
        {
            // デフォルトカテゴリが無い場合は追加
            if (categories.FindIndex(c => c.categoryName == "Default") < 0)
            {
                categories.Insert(0, new TemplateCategoryXml { categoryName = "Default" });
            }

            categoryNames.Clear();
            foreach (var category in categories)
            {
                categoryNames.Add(category.categoryName);
            }
        }

        public TemplateCategoryXml GetCategory(string categoryName)
        {
            return categories.FirstOrDefault(c => c.categoryName == categoryName);
        }

        public TemplateCategoryXml AddCategory(string categoryName)
        {
            if (HasCategory(categoryName))
            {
                MTEUtils.ShowDialog("カテゴリ名が重複しています: " + categoryName);
                return null;
            }

            var category = new TemplateCategoryXml
            {
                categoryName = categoryName,
            };

            categories.Add(category);
            categoryNames.Add(categoryName);
            dirty = true;
            MTEUtils.Log("新しいカテゴリが追加されました: " + categoryName);
            return category;
        }

        public bool RemoveCategory(string categoryName)
        {
            if (categoryName == "Default")
            {
                MTEUtils.ShowDialog("デフォルトカテゴリは削除できません。");
                return false;
            }

            var category = GetCategory(categoryName);
            if (category == null)
            {
                MTEUtils.ShowDialog($"カテゴリが見つかりません: {categoryName}");
                return false;
            }

            categories.Remove(category);
            categoryNames.Remove(categoryName);
            dirty = true;
            MTEUtils.Log("カテゴリが削除されました: " + categoryName);
            return true;
        }

        public bool HasCategory(string categoryName)
        {
            return categories.Any(c => c.categoryName == categoryName);
        }

        public bool MoveCategory(string categoryName, int offset)
        {
            if (CanMoveCategory(categoryName, offset) == false)
            {
                MTEUtils.ShowDialog("カテゴリの移動に失敗しました: " + categoryName);
                return false;
            }

            var index = categories.FindIndex(c => c.categoryName == categoryName);
            var category = categories[index];
            categories.RemoveAt(index);
            categories.Insert(index + offset, category);
            dirty = true;
            MTEUtils.Log($"カテゴリを移動しました: {categoryName} (新しい位置: {index + offset})");
            return true;
        }

        public bool CanMoveCategory(string categoryName, int offset)
        {
            var index = categories.FindIndex(c => c.categoryName == categoryName);
            return index >= 0 && index + offset >= 0 && index + offset < categories.Count;
        }

        public bool IsDirtyRecursive()
        {
            if (dirty) return true;

            foreach (var category in categories)
            {
                if (category.dirty) return true;
            }

            return false;
        }

        public void ClearDirtyRecursive()
        {
            dirty = false;

            foreach (var category in categories)
            {
                category.dirty = false;
            }
        }
    }

    public class TemplateCategoryXml
    {
        [XmlElement("CategoryName")]
        public string categoryName;

        [XmlElement("Template")]
        public List<TemplateXml> templates = new List<TemplateXml>();

        [XmlIgnore]
        public bool dirty = false;

        [XmlIgnore]
        private static TimelineManager timelineManager => TimelineManager.instance;

        [XmlIgnore]
        private static TimelineData timeline => timelineManager.timeline;

        [XmlIgnore]
        private static ITimelineLayer currentLayer => timelineManager.currentLayer;

        public TemplateXml GetTemplate(string templateName)
        {
            return templates.FirstOrDefault(t => t.templateName == templateName);
        }

        public bool AddTemplate(string templateName)
        {
            var selectedBones = timelineManager.selectedBones;
            if (selectedBones == null || selectedBones.Count == 0)
            {
                MTEUtils.ShowDialog("ボーンを選択してください。");
                return false;
            }

            var tmpFrames = new Dictionary<int, FrameData>();
            foreach (var bone in selectedBones)
            {
                FrameData tmpFrame;
                if (!tmpFrames.TryGetValue(bone.frameNo, out tmpFrame))
                {
                    tmpFrame = currentLayer.CreateFrame(bone.frameNo);
                    tmpFrames[bone.frameNo] = tmpFrame;
                }

                tmpFrame.UpdateBone(bone);
            }

            var newTemplate = new TemplateXml
            {
                templateName = templateName,
                frames = tmpFrames.Values.Select(frame => frame.ToXml()).ToList(),
            };

            if (HasTemplate(templateName))
            {
                // MTE 版はここで GameMain.Instance.SysDlg.Close() を呼ぶが、
                // SE のダイアログは DialogPopupWindow (応答時に自前でクローズ) のため呼ばない
                MTEUtils.ShowConfirmDialog($"同名のテンプレートがあります\n上書きしますか?\n「{templateName}」", () =>
                {
                    RemoveTemplate(templateName);
                    templates.Add(newTemplate);
                    SortTemplates();
                    dirty = true;
                    MTEUtils.Log("テンプレートが上書きされました: " + templateName);
                });
                return true;
            }

            templates.Add(newTemplate);
            SortTemplates();
            dirty = true;
            MTEUtils.Log("新しいテンプレートが追加されました: " + templateName);
            return true;
        }

        public bool RemoveTemplate(string templateName)
        {
            var template = GetTemplate(templateName);
            if (template == null)
            {
                MTEUtils.ShowDialog($"テンプレートが見つかりません: {templateName}");
                return false;
            }

            templates.Remove(template);
            dirty = true;
            MTEUtils.Log("テンプレートが削除されました: " + templateName);
            return true;
        }

        public bool HasTemplate(string templateName)
        {
            return templates.Any(t => t.templateName == templateName);
        }

        public void SortTemplates()
        {
            var compare = new NaturalStringComparer();
            templates.Sort((a, b) => compare.Compare(a.templateName, b.templateName));
            dirty = true;
        }

        public void ApplyTemplate(string templateName)
        {
            try
            {
                var template = GetTemplate(templateName);
                if (template == null)
                {
                    MTEUtils.ShowDialog("指定されたテンプレートが存在しません: " + templateName);
                    return;
                }

                if (template.frames.Count == 0)
                {
                    MTEUtils.ShowDialog("空のテンプレートです: " + templateName);
                    return;
                }

                var framesXml = template.frames;
                var minFrameNo = framesXml.Min(frame => frame.frameNo);
                foreach (var frameXml in framesXml)
                {
                    var tmpFrame = currentLayer.CreateFrame(frameXml);
                    var frameNo = timelineManager.currentFrameNo + tmpFrame.frameNo - minFrameNo;
                    currentLayer.UpdateBones(frameNo, tmpFrame.bones);
                }

                timeline.AdjustMaxFrameNo();
                timelineManager.RequestHistory("テンプレ適用: " + templateName);
                MTEUtils.LogDebug("テンプレートが適用されました: " + templateName);

                currentLayer.ApplyCurrentFrame(true);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }

    public class TemplateXml
    {
        [XmlElement("TemplateName")]
        public string templateName;
        [XmlElement("Frame")]
        public List<FrameXml> frames = new List<FrameXml>();

        [XmlIgnore]
        public float nameWidth = -1f;
    }

    /// <summary>
    /// キーフレームテンプレートの管理。MTE から移植。
    /// XML はレイヤー名単位のファイルで、スキーマは MTE と互換
    /// </summary>
    public class TimelineTemplateManager : ManagerBase
    {
        public Dictionary<string, TemplateLayerXml> templateLayerMap = new Dictionary<string, TemplateLayerXml>();

        private static TimelineTemplateManager _instance = null;
        public static TimelineTemplateManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineTemplateManager();
                }
                return _instance;
            }
        }

        public string layerName => currentLayer?.layerName ?? "MotionTimelineLayer";

        private TimelineTemplateManager()
        {
        }

        public override void Init()
        {
            LoadXml();
        }

        public override void Update()
        {
            SaveXml();
        }

        public void LoadXml()
        {
            templateLayerMap.Clear();

            ImportMteTemplatesIfEmpty();

            var templateDirPath = PluginUtils.TemplateDirPath;
            var filePaths = Directory.GetFiles(templateDirPath, "*.xml");

            foreach (var path in filePaths)
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(TemplateLayerXml));
                    using (var stream = new FileStream(path, FileMode.Open))
                    {
                        var templateLayer = (TemplateLayerXml)serializer.Deserialize(stream);
                        templateLayer.OnLoad();
                        templateLayerMap[templateLayer.layerName] = templateLayer;
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// SE 側にテンプレが 1 件も無い初回だけ、MTE のテンプレ資産をコピーして引き継ぐ。
        /// 以後は SE 側のファイルを正とし、MTE 側の変更は追従しない。
        /// SE 側のテンプレを意図的に全削除した場合も次回起動時に再インポートされる点に注意
        /// </summary>
        private void ImportMteTemplatesIfEmpty()
        {
            try
            {
                var templateDirPath = PluginUtils.TemplateDirPath;
                if (Directory.GetFiles(templateDirPath, "*.xml").Length > 0)
                {
                    return;
                }

                var mteDirPath = PluginUtils.MteTemplateDirPath;
                if (!Directory.Exists(mteDirPath))
                {
                    return;
                }

                foreach (var srcPath in Directory.GetFiles(mteDirPath, "*.xml"))
                {
                    var dstPath = MTEUtils.CombinePaths(templateDirPath, Path.GetFileName(srcPath));
                    File.Copy(srcPath, dstPath, false);
                }

                MTEUtils.Log("MTE のテンプレートを取り込みました: " + mteDirPath);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void SaveXml()
        {
            foreach (var templateLayer in templateLayerMap.Values)
            {
                if (!templateLayer.IsDirtyRecursive()) continue;

                try
                {
                    var path = PluginUtils.GetTemplatePath(templateLayer.layerName);
                    var serializer = new XmlSerializer(typeof(TemplateLayerXml));
                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        serializer.Serialize(stream, templateLayer);
                    }
                    templateLayer.ClearDirtyRecursive();
                    MTEUtils.LogDebug($"テンプレートレイヤーを保存しました: {templateLayer.layerName} ({path})");
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public TemplateLayerXml GetTemplateLayer()
        {
            if (templateLayerMap.TryGetValue(layerName, out var templateLayer))
            {
                return templateLayer;
            }

            // 無い場合は作成する
            templateLayer = new TemplateLayerXml
            {
                layerName = layerName,
                dirty = true,
            };

            templateLayerMap[layerName] = templateLayer;
            templateLayer.OnLoad();
            return templateLayer;
        }

        public bool AddTemplateCategory(string categoryName)
        {
            var templateLayer = GetTemplateLayer();
            if (templateLayer == null)
            {
                MTEUtils.ShowDialog("テンプレートレイヤーが見つかりません。");
                return false;
            }

            // 新しいカテゴリを追加
            templateLayer.AddCategory(categoryName);
            return true;
        }
    }
}
```

注意: `MTEUtils.ShowDialog` はテスト実行時に Unity GUI へ触れない実装（DialogPopupWindow のフィールド設定のみ）だが、テストでは重複追加や Default 削除などダイアログ経路を踏まないケースだけを検証する（Step 1 のテストはその方針で書いてある）。

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj`
Expected: 追加した 5 テストを含め全件 PASS

- [ ] **Step 5: プラグイン本体のビルド確認**

Run: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
Expected: ビルド成功（csproj がワイルドカード参照でない場合は新規ファイルの `<Compile Include>` 追加）

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTemplateManager.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineTemplateXmlTests.cs
git commit -m "feat(timeline): TimelineTemplateManager を移植して MTE 資産の初回取り込みに対応"
```

---

### Task 3: TimelineTemplateWindow（UI）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineTemplateWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:220` 付近（placement フィールド追加）

**Interfaces:**
- Consumes: Task 2 の `TimelineTemplateManager.instance`（`GetTemplateLayer` / `AddTemplateCategory`）と各 Xml クラスの操作 API
- Produces: `TimelineTemplateWindow.instance : TimelineTemplateWindow`（EditorSubWindow 派生・WINDOW_ID 8903388）

- [ ] **Step 1: Config.cs に placement フィールドを追加**

`timelineLayerVisible` の直後に追記:

```csharp
        [Header("タイムラインテンプレートウィンドウ")]
        public int timelineTemplatePosX = -1;
        public int timelineTemplatePosY = -1;
        public int timelineTemplateWidth = 400;
        public int timelineTemplateHeight = 480;
        public bool timelineTemplateVisible = false;
```

（既存フィールドに `[Header]` が付いていなければ属性行は省き、周辺の書式に合わせる）

- [ ] **Step 2: TimelineTemplateWindow.cs を作成**

MTE `TimelineTemplateUI` の 3 タブ（操作 / カテゴリ編集 / テンプレ編集）を EditorSubWindow へ移植。タブ描画・スクロールは TimelineSettingWindow と同じ `GUIView` API を使う:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレームテンプレートの操作・編集ウィンドウ。
    /// MTE の TimelineTemplateUI (操作 / カテゴリ編集 / テンプレ編集の 3 タブ) を移植。
    /// TimelineWindow のコントロールパネルの「テンプレ」ボタンから開く
    /// </summary>
    public class TimelineTemplateWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903388;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "テンプレート";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int TAB_WIDTH = 70;

        private enum TabType
        {
            操作,
            ｶﾃｺﾞﾘ編集,
            ﾃﾝﾌﾟﾚ編集,
        }

        private TabType _tabType = TabType.操作;

        private readonly GUIView _view = new GUIView();

        /// <summary>カテゴリごとの新規テンプレ名の入力途中の値</summary>
        private readonly Dictionary<string, string> _newTemplateNames = new Dictionary<string, string>();
        private string _newCategoryName = "";

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.TimelineTemplateManager templateManager => MTEP.TimelineTemplateManager.instance;

        private static TimelineTemplateWindow _instance = null;
        public static TimelineTemplateWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineTemplateWindow();
                }
                return _instance;
            }
        }

        private TimelineTemplateWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineTemplatePosX;
            y = config.timelineTemplatePosY;
            width = config.timelineTemplateWidth;
            height = config.timelineTemplateHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineTemplatePosX = x;
            config.timelineTemplatePosY = y;
            config.timelineTemplateWidth = width;
            config.timelineTemplateHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineTemplateVisible;
            set => config.timelineTemplateVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _tabType = _view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);

            _view.DrawHorizontalLine(Color.gray);

            _view.AddSpace(5);

            _view.BeginScrollView();

            switch (_tabType)
            {
                case TabType.操作:
                    DrawControl(_view);
                    break;
                case TabType.ｶﾃｺﾞﾘ編集:
                    DrawCategory(_view);
                    break;
                case TabType.ﾃﾝﾌﾟﾚ編集:
                    DrawTemplate(_view);
                    break;
            }

            _view.EndScrollView();
        }

        /// <summary>「操作」タブ: テンプレ一覧の適用と新規保存</summary>
        private void DrawControl(GUIView view)
        {
            var info = timelineManager.GetLayerInfo(currentLayer?.layerType);
            view.DrawLabel($"{info?.displayName ?? ""} テンプレ", -1, ROW_HEIGHT);

            view.DrawHorizontalLine();

            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            var categories = templateLayer.categories.ToArray();
            foreach (var category in categories)
            {
                view.DrawLabel($"[{category.categoryName}]", 200, ROW_HEIGHT);

                view.BeginHorizontal();

                foreach (var template in category.templates)
                {
                    if (template.nameWidth < 0f)
                    {
                        template.nameWidth = GUIView.CalcWidth(GUIView.gsButton, template.templateName);
                    }

                    // 折り返し: ボタンが右端を超える場合は次の行へ
                    if (view.currentPos.x + template.nameWidth > view.viewRect.width)
                    {
                        view.EndLayout();
                        view.BeginHorizontal();
                    }

                    if (view.DrawButton(template.templateName, template.nameWidth, ROW_HEIGHT))
                    {
                        category.ApplyTemplate(template.templateName);
                    }
                }

                view.EndLayout();

                DrawNewTemplateField(view, category);

                view.DrawHorizontalLine();
            }
        }

        /// <summary>「カテゴリ編集」タブ: カテゴリの追加・改名・並べ替え・削除</summary>
        private void DrawCategory(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawTextField(new GUIView.TextFieldOption
                {
                    label = "カテゴリ名",
                    labelWidth = 70,
                    width = 200,
                    value = _newCategoryName,
                    onChanged = value => _newCategoryName = value,
                    hiddenButton = true,
                });

                if (view.DrawButton("追加", 45, ROW_HEIGHT, !string.IsNullOrEmpty(_newCategoryName)))
                {
                    if (templateManager.AddTemplateCategory(_newCategoryName))
                    {
                        _newCategoryName = "";
                    }
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine();

            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            var categories = templateLayer.categories.ToArray();
            foreach (var category in categories)
            {
                view.BeginHorizontal();
                {
                    view.DrawTextField(new GUIView.TextFieldOption
                    {
                        width = 150,
                        value = category.categoryName,
                        onChanged = value =>
                        {
                            category.categoryName = value;
                            category.dirty = true;
                        },
                        disabled = category.categoryName == "Default",
                        hiddenButton = true,
                    });

                    if (view.DrawButton("削除", 45, ROW_HEIGHT, category.categoryName != "Default"))
                    {
                        templateLayer.RemoveCategory(category.categoryName);
                    }

                    if (view.DrawButton("↑", 20, ROW_HEIGHT, templateLayer.CanMoveCategory(category.categoryName, -1)))
                    {
                        templateLayer.MoveCategory(category.categoryName, -1);
                    }

                    if (view.DrawButton("↓", 20, ROW_HEIGHT, templateLayer.CanMoveCategory(category.categoryName, 1)))
                    {
                        templateLayer.MoveCategory(category.categoryName, 1);
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>「テンプレ編集」タブ: テンプレの改名・削除・ソート・新規保存</summary>
        private void DrawTemplate(GUIView view)
        {
            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            foreach (var category in templateLayer.categories)
            {
                view.BeginHorizontal();
                {
                    view.DrawLabel($"[{category.categoryName}]", 180, ROW_HEIGHT);

                    if (view.DrawButton("ソート", 60, ROW_HEIGHT))
                    {
                        category.SortTemplates();
                    }
                }
                view.EndLayout();

                var templates = category.templates.ToArray();
                foreach (var template in templates)
                {
                    view.BeginHorizontal();
                    {
                        view.DrawTextField(new GUIView.TextFieldOption
                        {
                            width = 150,
                            value = template.templateName,
                            onChanged = value =>
                            {
                                template.templateName = value;
                                template.nameWidth = -1f;
                                category.dirty = true;
                            },
                            hiddenButton = true,
                        });

                        if (view.DrawButton("削除", 45, ROW_HEIGHT))
                        {
                            category.RemoveTemplate(template.templateName);
                        }
                    }
                    view.EndLayout();
                }

                DrawNewTemplateField(view, category);

                view.DrawHorizontalLine();
            }
        }

        /// <summary>新規テンプレ名の入力欄と「追加」ボタン (操作タブとテンプレ編集タブで共通)</summary>
        private void DrawNewTemplateField(GUIView view, MTEP.TemplateCategoryXml category)
        {
            if (!_newTemplateNames.ContainsKey(category.categoryName))
            {
                _newTemplateNames[category.categoryName] = "";
            }
            var newTemplateName = _newTemplateNames[category.categoryName];

            view.BeginHorizontal();
            {
                view.DrawTextField(new GUIView.TextFieldOption
                {
                    label = "テンプレ名",
                    labelWidth = 70,
                    width = 200,
                    value = newTemplateName,
                    onChanged = value => _newTemplateNames[category.categoryName] = value,
                    hiddenButton = true,
                });

                if (view.DrawButton("追加", 45, ROW_HEIGHT, !string.IsNullOrEmpty(newTemplateName)))
                {
                    if (category.AddTemplate(newTemplateName))
                    {
                        _newTemplateNames[category.categoryName] = "";
                    }
                }
            }
            view.EndLayout();

            if (newTemplateName.Length > 0)
            {
                var selectedBones = timelineManager.selectedBones;
                if (selectedBones == null || selectedBones.Count == 0)
                {
                    view.DrawLabel("ボーンが選択されていません。", -1, ROW_HEIGHT, Color.green);
                }
                else if (category.HasTemplate(newTemplateName))
                {
                    view.DrawLabel("同名のテンプレートが既に存在します。", -1, ROW_HEIGHT, Color.green);
                }
            }
        }
    }
}
```

実装時の適合ポイント（TimelineSettingWindow / TimelineLayerWindow を正とする）:
- `view.DrawTabs(enum, width, height)` のシグネチャは TimelineSettingWindow の使用箇所に合わせる（enum ジェネリクスの有無）
- `GUIView.CalcWidth(GUIView.gsButton, string)` が SE の GUIView に無い場合は `GUIView.gsButton.CalcSize(new GUIContent(name)).x` 相当へ置き換える
- `System.Linq` が必要になったら using へ追加（`ToArray()` は `List<T>.ToArray()` なので不要のはず）

- [ ] **Step 3: ビルド確認**

Run: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
Expected: ビルド成功

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineTemplateWindow.cs source/COM3D2.SceneEditor.Plugin/Config.cs
git commit -m "feat(timeline): テンプレートウィンドウを追加"
```

---

### Task 4: 配線（マネージャ登録・ウィンドウ登録・導線）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:45` 付近（マネージャ配列）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:68` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:135` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:650` 付近（「編集」ボタンの後）

**Interfaces:**
- Consumes: Task 2 `TimelineTemplateManager.instance`、Task 3 `TimelineTemplateWindow.instance`

- [ ] **Step 1: TimelineIntegration のマネージャ配列へ追加**

`MTEP.TimelineHistoryManager.instance,` の行の直後に追加:

```csharp
                MTEP.TimelineTemplateManager.instance,
```

（この配列のマネージャは Init / Update が呼ばれる。Init → LoadXml、Update → dirty 時 SaveXml の駆動が成立する）

- [ ] **Step 2: WindowManager へ登録**

`AddWindow(TimelineLayerWindow.instance);` の直後:

```csharp
            AddWindow(TimelineTemplateWindow.instance);
```

- [ ] **Step 3: MenuBarWindow へ項目追加**

`CreateWindowItem("レイヤー編集", TimelineLayerWindow.instance),` の直後:

```csharp
                        CreateWindowItem("テンプレート", TimelineTemplateWindow.instance),
```

- [ ] **Step 4: TimelineWindow へ「テンプレ」ボタン追加**

「編集」ボタンのブロック（`WindowManager.ToggleWindowVisible(TimelineLayerWindow.instance);` を含む if）の直後:

```csharp
                if (view.DrawButton("テンプレ", 60, 20))
                {
                    WindowManager.ToggleWindowVisible(TimelineTemplateWindow.instance);
                }
```

ボタン行の幅が不足して折り返す場合は、既存ボタンの並びを崩さない範囲で `AddSpace` を詰めて調整する。

- [ ] **Step 5: ビルドとテストの最終確認**

Run: `MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj`
Expected: 両方成功

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs
git commit -m "feat(timeline): テンプレートウィンドウとマネージャを配線"
```

---

### Task 5: 実機確認（devbridge）

**Files:** なし（検証のみ）

- [ ] **Step 1: 新 DLL の反映**

ゲーム停止中なら `debug.bat com3d25`。起動中なら CLAUDE.md 記載の Harmony ホットリロード手順（バイト配列ロード）か、ユーザーへ再起動を依頼する。

- [ ] **Step 2: devbridge で確認する項目**

1. `TimelineTemplateManager.instance.LoadXml()` 後に `templateLayerMap` が空でも例外が出ない
2. TimelineWindow の「テンプレ」ボタン → ウィンドウが開く（`isShowWnd` を reflection で立てても可）
3. Motion レイヤーでボーンを選択（`timelineManager.selectedBones` に投入）→ カテゴリ Default へ `AddTemplate("検証")` → `Config\SceneEditor\Template\MotionTimelineLayer.xml` が生成される（保存は `Update()` が毎フレーム呼ぶ `SaveXml()` の dirty チェックで自動実行される。タイムライン編集中のガード条件下でのみ Update が回る点に注意。reflection なら `SaveXml()` 直呼びで可）
4. 別フレームへ移動して `ApplyTemplate("検証")` → currentFrameNo 起点でキーフレームが増える
5. 同名テンプレへ再度 `AddTemplate("検証")` → DialogPopupWindow の上書き確認が表示され、「はい」で上書きされる
6. カテゴリ追加・改名・並べ替え・削除、テンプレ改名・削除・ソートの各操作でエラーが出ない
7. 検証で作ったテンプレ XML を削除して後始末する

- [ ] **Step 3: 結果をドキュメントへ記録**

`docs/superpowers/specs/timeline-remaining-work.md` §1 のテンプレート機能を「移植済み」に更新し、実機確認結果を追記して commit:

```bash
git add docs/superpowers/specs/timeline-remaining-work.md
git commit -m "docs(timeline): テンプレート機能 (W5) の移植完了を記録"
```

---

## Self-Review 済みメモ

- Spec カバレッジ: テンプレ保存（Task 2 AddTemplate）・適用（ApplyTemplate）・3 タブ UI（Task 3）・MTE 互換 XML 資産（スキーマ互換 + 初回インポート、Task 1/2）を網羅
- MTE との差分は「格納ディレクトリ」「初回インポート」「UI ホスト形式」の 3 点のみで、データスキーマと操作ロジックは逐語移植
- 型整合: `TemplateCategoryXml.AddTemplate(string) : bool` / `TimelineTemplateManager.GetTemplateLayer() : TemplateLayerXml` を UI 側でそのまま使用
- `FrameXml.frames` のコレクション初期化子（Step 1 テスト）は `frames` が `List<FrameXml>` フィールド初期化済みのため有効
