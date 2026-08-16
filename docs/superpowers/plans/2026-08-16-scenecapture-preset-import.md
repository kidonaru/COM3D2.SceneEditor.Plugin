# SceneCapture プリセット Readonly 読み込み 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SceneEditor のシーンプリセット一覧から SceneCapture プリセット XML を Readonly で読み込み、カメラ・背景・ライトは本体で、モデル配置・ポスプロは外部プロバイダ経由で適用できるようにする。

**Architecture:** `ScenePresetManager` のツリーに SceneCapture の Presets フォルダを仮想フォルダとして表示し、読み込み時にルート要素 `<Preset>` を `SceneCapturePresetLoader` で SceneEditor 内部形式（`ScenePresetCamera` / `ScenePresetBackground` / `ScenePresetLight`）へ変換して既存 Apply 経路に流す。Models / Effects セクションは生 XML のまま、プロバイダ規約の新任意メソッド `ApplySceneCaptureXml(string)` を実装した外部プラグインへ委譲する。**外部プラグイン側（ModItemExplorer / PostEffects）の実装は本計画のスコープ外**で、実装者向け資料の整備までを行う。

**Tech Stack:** C# (.NET 3.5 相当 / Unity 5.6 系), System.Xml.Linq (XDocument), UnityInjector プラグイン

**Spec:** `docs/superpowers/specs/2026-08-16-scenecapture-preset-import-design.md`

## Global Constraints

- コードコメント・エラーログメッセージは日本語で書く
- テストプロジェクトは無い。各タスクの検証は `debug.bat com3d25` でのビルド成功と、最終タスクの devbridge 実機検証で行う
- git worktree は使わない。メイン作業ディレクトリで作業する
- SceneCapture プリセットへの書き込み（保存・削除・リネーム）は一切行わない
- 1 要素のパース・適用失敗で全体を止めない（警告ログを出して続行）

## 参照情報（実装時に前提となる事実）

- SceneCapture Presets 実体: `<UnityInjector>\Config\SceneCapture\Presets\*.xml`。
  SceneEditor から見ると `Path.Combine(Path.GetDirectoryName(PluginUtils.UserDataPath), "Config") ` ではなく、
  **`PluginUtils.UserDataPath`（= DLL 隣の `Config`）配下の `SceneCapture\Presets`** が同一フォルダになる
  （SceneCapture の `ConstantValues.ConfigDir` も同じ `Config` を基点にしているため）。
- SceneCapture XML 構造（実サンプル `TEST4.xml` より）:

```xml
<Preset>
  <Effects>
    <BloomDef><bloomIntensity>2.85</bloomIntensity>...</BloomDef>
    <!-- 有効なエフェクトのみ Def 要素が存在。値は「フィールド名=要素名」で文字列化 -->
  </Effects>
  <Lights>
    <Light>
      <Position>0,2,0</Position><EulerAngles>40,180,18</EulerAngles>
      <Intensity>0.9</Intensity><Range>10</Range><SpotAngle>30</SpotAngle>
      <Color>255,255,255,255</Color><Type>1</Type><Enabled>True</Enabled>
      <shadows>2</shadows><shadowStrength>0.098</shadowStrength>
      <shadowBias>0.01</shadowBias><shadowNormalBias>0.4</shadowNormalBias>
    </Light>
  </Lights>
  <LightShafts />   <!-- Light と同じ12要素 + シャフト固有要素 -->
  <Models />        <!-- Model: Position/Rotation(Quaternion x,y,z,w)/LocalScale/ModelType/MenuFileName 等 -->
  <Camera>
    <Position>0,1.5,0</Position>          <!-- MainCamera.GetTargetPos() -->
    <Rotation>354.9,186.9,0.2</Rotation>  <!-- transform euler (x=pitch, y=yaw, z=roll) -->
    <Distance>2</Distance><FieldOfView>25</FieldOfView>
  </Camera>
  <Misc><Background></Background><Version>0.3.1.27</Version></Misc>
</Preset>
```

- 数値は InvariantCulture、`Color` は Color32 の "r,g,b,a"（0-255）、bool は "True"/"False"。
- SceneCapture の `<Lights>` 先頭 1 灯はゲームのメインライト（Type=1 Directional）で、以降が追加ライト。
- SceneEditor 側の適用先 API（既存・変更しない）:
  - `CameraSnapshot.ApplyState(ScenePresetCamera)` — targetPos / yaw(=euler.y) / pitch(=euler.x) / roll(=euler.z) / distance / fov
  - `BackgroundSnapshot.ApplyState(ScenePresetBackground)` — bgId 優先、無ければ bgPrefabName。`BackgroundUtils.EnsureBgDataLoaded()` + `BackgroundUtils.GetBgId(bgName)` で逆引き
  - `LightSnapshot.ApplyState(ScenePresetLight)` — hasMain + additionalLights
- プロバイダ発見: `ScenePresetProviderRegistry`（`Manager/ScenePresetProviderRegistry.cs`）。
  `BindProvider` が public static メンバを `Delegate.CreateDelegate` でバインドする。

---

### Task 1: SceneCapture 仮想フォルダと Readonly アイテム

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs`

**Interfaces:**
- Produces: `ScenePresetItem.isSceneCapture`（bool フィールド）、`ScenePresetManager.sceneCapturePresetsPath`（string static プロパティ）、`ScenePresetItem.isReadonlyDir`（保存抑止判定に使う。仮想フォルダとその配下フォルダで true）

- [ ] **Step 1: ScenePresetItem にフラグを追加**

`ScenePresetManager.cs` 冒頭の `ScenePresetItem` クラスに追加する:

```csharp
/// <summary>SceneCapture プリセット由来の項目か。読み込み専用で、適用経路も専用になる</summary>
public bool isSceneCapture;

/// <summary>保存先にできないフォルダか（SceneCapture 仮想フォルダとその配下）</summary>
public bool isReadonlyDir;
```

- [ ] **Step 2: 仮想フォルダのパスとツリー構築を追加**

`ScenePresetManager` に追加する:

```csharp
/// <summary>SceneCapture プラグインのプリセットフォルダ。同じ Config 配下に同居している</summary>
public static string sceneCapturePresetsPath
    => Path.Combine(PluginUtils.UserDataPath, Path.Combine("SceneCapture", "Presets"));

/// <summary>
/// SceneCapture のプリセットを読み込み専用の仮想フォルダとしてツリーへ追加する。
/// フォルダが無い環境では何も足さない
/// </summary>
private static void AddSceneCaptureItems(ScenePresetItem rootItem, HashSet<string> visitedDirs)
{
    if (!Directory.Exists(sceneCapturePresetsPath))
    {
        return;
    }

    var dirItem = new ScenePresetItem
    {
        name = "SceneCapture",
        path = sceneCapturePresetsPath,
        isDir = true,
        isSceneCapture = true,
        isReadonlyDir = true,
        children = new List<ITileViewContent>(16),
    };
    rootItem.AddChild(dirItem);

    if (visitedDirs.Add(GetCanonicalPath(sceneCapturePresetsPath)))
    {
        SearchItems(dirItem, visitedDirs);
        MarkSceneCaptureItems(dirItem);
    }
}

/// <summary>仮想フォルダ配下の全項目へ SceneCapture / Readonly の属性を伝播する</summary>
private static void MarkSceneCaptureItems(ScenePresetItem dirItem)
{
    if (dirItem.children == null)
    {
        return;
    }
    foreach (var child in dirItem.children.OfType<ScenePresetItem>())
    {
        child.isSceneCapture = true;
        if (child.isDir)
        {
            child.isReadonlyDir = true;
            MarkSceneCaptureItems(child);
        }
        else
        {
            // 読み込み専用: 削除ボタンと自動ロード指定を出さない
            child.canDelete = false;
            child.canFavorite = false;
        }
    }
}
```

`Reload()` の `SearchItems(rootItem, visitedDirs);` の直後（`UpdateSelection(rootItem);` の前）に
`AddSceneCaptureItems(rootItem, visitedDirs);` を追加する。

注意: `SearchItemsCore` は `IsSidecarXmlPath`（ドット入りファイル名）を除外するが、
SceneCapture のプリセット名にはドットを含むもの（例 `HRK preset v2.0.xml`）がある。
そのため `SearchItemsCore` の除外判定を「SceneCapture 配下では行わない」ように、
`SearchItemsCore(ScenePresetItem dirItem, HashSet<string> visitedDirs)` の
サイドカー判定を `if (!IsUnderSceneCapture(dirItem.path) && IsSidecarXmlPath(xmlPath))` に変更し、
判定ヘルパーを追加する:

```csharp
/// <summary>
/// SceneCapture 仮想フォルダ配下のパスか。サイドカー除外と保存抑止の判定に使う。
/// 同名接頭辞の別フォルダ（例: Presets と PresetsOld）を誤判定しないよう、
/// ルート自身との一致か、区切り文字付きの前方一致で判定する
/// </summary>
private static bool IsUnderSceneCapture(string path)
{
    var root = GetCanonicalPath(sceneCapturePresetsPath);
    var target = GetCanonicalPath(path);
    return target.Equals(root, StringComparison.OrdinalIgnoreCase)
        || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
```

さらに、`Reload()` の表示中フォルダ復元は `GetRelativePath`（`presetFolderPath` 基準固定）
で行われるため、SceneCapture 仮想フォルダ配下を表示中に「更新」を押すと
相対パスが `""` になりルートへ追い出される。復元処理を仮想フォルダ対応にする:

```csharp
// Reload() 冒頭の控え（既存の currentRelativeDir と併用）
var wasSceneCapture = IsUnderSceneCapture(currentDirPath);
var sceneCaptureRelativeDir = wasSceneCapture
    ? GetRelativePathFrom(sceneCapturePresetsPath, currentDirPath)
    : null;
```

```csharp
// Reload() 末尾の復元（既存の FindDirItem 行を置き換え）
if (wasSceneCapture)
{
    var scRoot = rootItem.children.OfType<ScenePresetItem>()
        .FirstOrDefault(c => c.isDir && c.isSceneCapture);
    currentDirItem = (scRoot != null ? FindDirItem(scRoot, sceneCaptureRelativeDir) : null)
        ?? rootItem;
}
else
{
    currentDirItem = FindDirItem(rootItem, currentRelativeDir) ?? rootItem;
}
```

```csharp
/// <summary>基準フォルダからの相対パス。配下でなければ空文字を返す</summary>
private static string GetRelativePathFrom(string basePath, string fullPath)
{
    var root = GetCanonicalPath(basePath);
    var target = GetCanonicalPath(fullPath);
    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        return "";
    }
    return target.Substring(root.Length + 1);
}
```

（既存の `GetRelativePath` の実装を確認し、同等処理があれば共通化してよい。）

- [ ] **Step 3: 保存の抑止**

`PresetWindow.cs` の `DrawToolRow` にある保存ボタン描画（`SavePresetWithConfirm()` を呼ぶ箇所）を、
`DrawButton` の enabled 引数（既存の `_view.DrawButton("<", 20, ROW_HEIGHT, currentDirItem.parent != null)`
と同じ形式）で条件付き無効化する:

```csharp
// SceneCapture 仮想フォルダは読み込み専用のため保存させない
if (_view.DrawButton("保存", /* 既存の幅 */, ROW_HEIGHT, !currentDirItem.isReadonlyDir))
{
    SavePresetWithConfirm();
}
```

**`SetEnabled` は使わないこと。** `GUIView.SetEnabled` はスタックではなく単純代入のため、
`DrawContent` 冒頭の `SetEnabled(!isLoading)`（読み込み中の全操作抑止）を
ツールバー内の `SetEnabled(true)` が上書きして壊してしまう。
併せて `ScenePresetManager.SavePreset` の冒頭にも防御を入れる:

```csharp
// UI の抑止をすり抜けても SceneCapture 配下には書き込まない
if (IsUnderSceneCapture(currentDirPath))
{
    MTEUtils.LogWarning("SceneCapture フォルダは読み込み専用のため保存できません");
    return;
}
```

- [ ] **Step 4: ビルド確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat com3d25"`
Expected: `ビルドに成功しました`

- [ ] **Step 5: Commit**

```bash
git add -A source
git commit -m "feat(preset): SceneCapture プリセットを読み込み専用の仮想フォルダとして一覧表示する"
```

---

### Task 2: プロバイダ規約に ApplySceneCaptureXml を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetProviderRegistry.cs`

**Interfaces:**
- Produces: `ScenePresetProvider.applySceneCaptureXml`（`Func<string, bool>`、未実装プロバイダでは null）

- [ ] **Step 1: フィールドとバインドを追加**

`ScenePresetProvider` クラスに追加:

```csharp
/// <summary>
/// SceneCapture 形式のプリセット XML（&lt;Preset&gt; 全体）を適用する任意メソッド。
/// 未実装のプロバイダは null のままで、SceneCapture プリセット適用の対象外になる
/// </summary>
public Func<string, bool> applySceneCaptureXml;
```

`BindProvider` の `return provider;` 直前に追加:

```csharp
// SceneCapture 形式の適用は任意メンバ。シグネチャ不一致は契約不備として扱わず単に無視する
var applySceneCaptureMethod = type.GetMethod(
    "ApplySceneCaptureXml", flags, null, new[] { typeof(string) }, null);
if (applySceneCaptureMethod != null && applySceneCaptureMethod.ReturnType == typeof(bool))
{
    provider.applySceneCaptureXml = (Func<string, bool>)Delegate.CreateDelegate(
        typeof(Func<string, bool>), applySceneCaptureMethod);
}
```

クラス先頭の契約コメント（`/// 契約: ...`）にも任意メンバとして 1 行追記する。

- [ ] **Step 2: ビルド確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat com3d25"`
Expected: `ビルドに成功しました`

- [ ] **Step 3: Commit**

```bash
git add -A source
git commit -m "feat(preset): プロバイダ規約に任意メソッド ApplySceneCaptureXml を追加する"
```

---

### Task 3: SceneCapturePresetLoader（XML → 内部形式の変換）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/SceneCapturePresetLoader.cs`

**Interfaces:**
- Consumes: `ScenePresetCamera` / `ScenePresetBackground` / `ScenePresetLight` / `ScenePresetAdditionalLight`（`ScenePresetData.cs` の既存型）、`BackgroundUtils.EnsureBgDataLoaded()` / `BackgroundUtils.GetBgId(string)`
- Produces: `SceneCapturePresetLoader.Parse(string xmlText)` → `SceneCaptureConvertedPreset { camera, background, light, rawXml, hasModels, hasEffects }`、`SceneCapturePresetLoader.IsSceneCaptureXml(string path)`

- [ ] **Step 1: 変換クラスを実装**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>SceneCapture プリセットを SceneEditor 内部形式へ変換した結果</summary>
    public class SceneCaptureConvertedPreset
    {
        public ScenePresetCamera camera;
        public ScenePresetBackground background;
        public ScenePresetLight light;

        /// <summary>外部プロバイダへそのまま渡す &lt;Preset&gt; XML 全体</summary>
        public string rawXml;

        public bool hasModels;
        public bool hasEffects;
    }

    /// <summary>
    /// SceneCapture プラグインのプリセット XML（ルート &lt;Preset&gt;）を読み取り、
    /// カメラ・背景・ライトを SceneEditor の内部形式へ変換する。
    /// Models / Effects の中身は解釈せず、外部プロバイダへの委譲可否だけ判定する
    /// </summary>
    public static class SceneCapturePresetLoader
    {
        /// <summary>ルート要素だけ見て SceneCapture 形式かを判定する。読めないファイルは false</summary>
        public static bool IsSceneCaptureXml(string path)
        {
            try
            {
                using (var reader = System.Xml.XmlReader.Create(path))
                {
                    reader.MoveToContent();
                    return reader.Name == "Preset";
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static SceneCaptureConvertedPreset Parse(string xmlText)
        {
            var doc = XDocument.Parse(xmlText);
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "Preset")
            {
                throw new InvalidDataException("SceneCapture プリセットのルート要素が不正です");
            }

            var result = new SceneCaptureConvertedPreset { rawXml = xmlText };

            // 1 セクションの失敗で全体を止めない。変換できたものだけ適用する
            try { result.camera = ParseCamera(root.Element("Camera")); }
            catch (Exception e) { LogSectionError("Camera", e); }
            try { result.background = ParseBackground(root.Element("Misc")); }
            catch (Exception e) { LogSectionError("Misc", e); }
            try { result.light = ParseLights(root.Element("Lights"), root.Element("LightShafts")); }
            catch (Exception e) { LogSectionError("Lights", e); }

            result.hasModels = root.Element("Models") != null
                && root.Element("Models").Elements("Model").Any();
            result.hasEffects = root.Element("Effects") != null
                && root.Element("Effects").Elements().Any();

            return result;
        }

        private static void LogSectionError(string section, Exception e)
        {
            MTEUtils.LogWarning("SceneCapture プリセットの {0} を変換できませんでした", section);
            MTEUtils.LogException(e);
        }

        /// <summary>
        /// Camera: Position は注視点、Rotation は transform euler。
        /// SceneEditor のオービット表現へは yaw=euler.y / pitch=euler.x / roll=euler.z で写す
        /// </summary>
        private static ScenePresetCamera ParseCamera(XElement e)
        {
            if (e == null)
            {
                return null;
            }
            var rotation = ParseVector3(Value(e, "Rotation"));
            return new ScenePresetCamera
            {
                targetPos = ParseVector3(Value(e, "Position")),
                yaw = rotation.y,
                pitch = rotation.x,
                roll = rotation.z,
                distance = ParseFloat(Value(e, "Distance"), 2f),
                fov = ParseFloat(Value(e, "FieldOfView"), 35f),
            };
        }

        /// <summary>
        /// Misc/Background: 背景プレハブ名の文字列だけが入っている。
        /// 空なら背景は触らない (null)。id を逆引きできなければ prefab 名で復元させる。
        /// SceneCapture は背景の位置・回転・背景色を持たないため、位置回転は原点、色は触らない
        /// </summary>
        private static ScenePresetBackground ParseBackground(XElement misc)
        {
            var bgName = misc != null ? Value(misc, "Background") : null;
            if (string.IsNullOrEmpty(bgName))
            {
                return null;
            }

            BackgroundUtils.EnsureBgDataLoaded();
            var bgId = BackgroundUtils.GetBgId(bgName);
            return new ScenePresetBackground
            {
                bgId = bgId,
                bgPrefabName = bgId == null ? bgName : null,
                position = Vector3.zero,
                rotation = Vector3.zero,
                hasBgColor = false,
            };
        }

        /// <summary>
        /// Lights: 先頭 1 灯はゲームのメインライト、以降は追加ライト。
        /// LightShafts はシャフト固有要素を捨て、共通 12 要素だけ追加ライトとして写す
        /// </summary>
        private static ScenePresetLight ParseLights(XElement lights, XElement lightShafts)
        {
            var state = new ScenePresetLight();

            var entries = lights != null ? lights.Elements("Light").ToList() : new List<XElement>();
            if (entries.Count > 0)
            {
                var main = entries[0];
                state.hasMain = true;
                state.mainRotation = ParseVector3(Value(main, "EulerAngles"));
                state.mainColor = ParseColor32(Value(main, "Color"));
                state.mainIntensity = ParseFloat(Value(main, "Intensity"), 0.95f);
                state.mainShadowStrength = ParseFloat(Value(main, "shadowStrength"), 0.098f);
            }

            foreach (var e in entries.Skip(1))
            {
                state.additionalLights.Add(ParseAdditionalLight(e));
            }
            if (lightShafts != null)
            {
                foreach (var e in lightShafts.Elements("LightShaft"))
                {
                    state.additionalLights.Add(ParseAdditionalLight(e));
                }
            }

            // メインも追加も無いプリセットではライトを触らない
            return state.hasMain || state.additionalLights.Count > 0 ? state : null;
        }

        private static ScenePresetAdditionalLight ParseAdditionalLight(XElement e)
        {
            return new ScenePresetAdditionalLight
            {
                type = ParseInt(Value(e, "Type"), (int)LightType.Point),
                position = ParseVector3(Value(e, "Position")),
                rotation = ParseVector3(Value(e, "EulerAngles")),
                color = ParseColor32(Value(e, "Color")),
                intensity = ParseFloat(Value(e, "Intensity"), 0.95f),
                range = ParseFloat(Value(e, "Range"), 10f),
                spotAngle = ParseFloat(Value(e, "SpotAngle"), 30f),
                enabled = ParseBool(Value(e, "Enabled"), true),
            };
        }

        private static string Value(XElement parent, string name)
        {
            var child = parent.Element(name);
            return child != null ? child.Value : null;
        }

        // SceneCapture の書式: float は InvariantCulture、Vector3 "x,y,z"、Color32 "r,g,b,a" (0-255)

        private static float ParseFloat(string s, float fallback)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            bool v;
            return bool.TryParse(s, out v) ? v : fallback;
        }

        private static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Vector3.zero;
            }
            var parts = s.Split(',');
            if (parts.Length != 3)
            {
                throw new FormatException("Vector3 の書式が不正です: " + s);
            }
            return new Vector3(
                ParseFloat(parts[0], 0f), ParseFloat(parts[1], 0f), ParseFloat(parts[2], 0f));
        }

        private static Color ParseColor32(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Color.white;
            }
            var parts = s.Split(',');
            if (parts.Length != 4)
            {
                throw new FormatException("Color32 の書式が不正です: " + s);
            }
            return new Color32(
                (byte)ParseInt(parts[0], 255), (byte)ParseInt(parts[1], 255),
                (byte)ParseInt(parts[2], 255), (byte)ParseInt(parts[3], 255));
        }
    }
}
```

注意: `using System.Collections.Generic;` を先頭に追加すること（`List<XElement>` 使用のため）。
`System.Xml.Linq` への参照が csproj に無ければ追加する（SceneCapture プラグインが
実機で XDocument を使っているため、ゲーム側にアセンブリは存在する）。

- [ ] **Step 2: ビルド確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat com3d25"`
Expected: `ビルドに成功しました`

- [ ] **Step 3: 変換ロジックの静的検証**

実サンプルで最低限の変換を机上確認する（ゲーム外で動くロジックのため、
`TEST4.xml` の値が Parse 結果へ正しく写ることをコードレビューで確認）:
- Camera: targetPos=(0,1.5,0) / yaw=186.99 / pitch=354.96 / roll=0.21 / distance=2 / fov=25
- Lights: 1 灯のみ → hasMain=true, additionalLights 空, mainIntensity=0.9
- Background: 空文字 → null

- [ ] **Step 4: Commit**

```bash
git add -A source
git commit -m "feat(preset): SceneCapture プリセット XML の変換ローダーを追加する"
```

---

### Task 4: 読み込み分岐と適用フロー

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`

**Interfaces:**
- Consumes: `SceneCapturePresetLoader.Parse` / `IsSceneCaptureXml`（Task 3）、`ScenePresetProvider.applySceneCaptureXml`（Task 2）
- Produces: `ScenePresetManager.LoadPreset(ScenePresetItem)` が SceneCapture 項目でも動く

- [ ] **Step 1: LoadPreset に分岐を追加**

`LoadPreset(ScenePresetItem item, ScenePresetData preloaded)` の try 冒頭に追加:

```csharp
// SceneCapture プリセットはフォーマットも適用経路も別物のため専用処理へ
if (item.isSceneCapture)
{
    ApplySceneCapturePreset(item);
    currentPresetKey = "";
    UpdateSelection(rootItem);
    return;
}
```

（`currentPresetKey` は SceneEditor 形式のキー体系のため、SceneCapture 適用後は未選択へ戻す。）

- [ ] **Step 2: 適用処理を実装**

`ScenePresetManager` に追加:

```csharp
/// <summary>
/// SceneCapture プリセットを適用する。カメラ・背景・ライトは本体で、
/// Models / Effects は ApplySceneCaptureXml を実装した外部プロバイダへ委譲する。
/// メイドには一切触らない
/// </summary>
private static void ApplySceneCapturePreset(ScenePresetItem item)
{
    var converted = SceneCapturePresetLoader.Parse(File.ReadAllText(item.path));

    // シーンの見た目を書き換えるため、既存の履歴は復元先を失う
    HistoryManager.instance.ClearHistory();
    MTEUtils.Log("SceneCapture プリセット適用のため操作履歴をクリアしました");

    CameraSnapshot.ApplyState(converted.camera);
    BackgroundSnapshot.ApplyState(converted.background);
    LightSnapshot.ApplyState(converted.light);

    if (converted.hasModels || converted.hasEffects)
    {
        ApplySceneCaptureExternals(converted.rawXml);
    }

    MTEUtils.Log("SceneCapture プリセットを適用しました: {0}", item.name);
}

/// <summary>
/// ApplySceneCaptureXml を実装している全プロバイダへ生 XML を渡す。
/// どのセクションを読むかはプロバイダの責務。1 件の失敗で他を止めない
/// </summary>
private static void ApplySceneCaptureExternals(string rawXml)
{
    var handled = false;
    foreach (var provider in ScenePresetProviderRegistry.providers)
    {
        if (provider.applySceneCaptureXml == null)
        {
            continue;
        }
        handled = true;
        try
        {
            if (!provider.applySceneCaptureXml(rawXml))
            {
                MTEUtils.LogWarning("SceneCapture プリセットの適用に失敗しました: {0}", provider.id);
            }
        }
        catch (Exception e)
        {
            MTEUtils.LogError("SceneCapture プリセットの適用に失敗しました: " + provider.id);
            MTEUtils.LogException(e);
        }
    }

    if (!handled)
    {
        MTEUtils.LogWarning(
            "SceneCapture のモデル・エフェクトを適用できる外部プラグインが見つかりません");
    }
}
```

注意: `SavePresetPopupWindow` を開いた時にしか `ScenePresetProviderRegistry.Refresh()` が
呼ばれないため、ここでも適用前に `ScenePresetProviderRegistry.Refresh();` を
`ApplySceneCaptureExternals` の先頭で呼ぶこと（遅延ロードされたプラグインの取りこぼし防止）。

- [ ] **Step 3: 一覧走査の除外確認**

Task 1 で SceneCapture 配下のサイドカー除外を無効化したが、SceneCapture フォルダに
SceneEditor 形式でない壊れた XML があっても一覧には出る。読み込み失敗時は既存の
catch（`プリセットの読み込みに失敗しました` ダイアログ）で拾われることを確認する
（`ApplySceneCapturePreset` は `LoadPreset` の try 内で呼ばれるため追加処理は不要）。

- [ ] **Step 4: ビルド確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat com3d25"`
Expected: `ビルドに成功しました`

- [ ] **Step 5: Commit**

```bash
git add -A source
git commit -m "feat(preset): SceneCapture プリセットの読み込み分岐と適用フローを追加する"
```

---

### Task 5: 外部プラグイン実装者向け資料

**Files:**
- Modify: `docs/scene-preset-provider-guide.md`
- Create: `docs/scenecapture-import-guide.md`

**Interfaces:**
- Consumes: Task 2 の契約（`ApplySceneCaptureXml(string) : bool`、public static、任意）

- [ ] **Step 1: プロバイダガイドへ任意メンバを追記**

`docs/scene-preset-provider-guide.md` の契約仕様の表へ 1 行追加する:

| メンバ | 型 | 区分 | 説明 |
|---|---|---|---|
| `ApplySceneCaptureXml(string)` | `bool` を返す `string` 1 引数メソッド | 任意 | SceneCapture プリセット XML（`<Preset>` 全体）を適用し、成功可否を返す。実装すると SceneEditor の SceneCapture プリセット読み込み時に呼ばれる |

あわせて「適用」節に、SceneCapture プリセット読み込み時のフロー
（カメラ・背景・ライトは SceneEditor 本体 → `ApplySceneCaptureXml` 実装プロバイダへ生 XML）を追記する。

- [ ] **Step 2: SceneCapture インポートガイドを新規作成**

`docs/scenecapture-import-guide.md` に以下を書く（実装者 = ModItemExplorer / PostEffects の開発者向け）:

1. **概要**: SceneEditor は SceneCapture プリセット（`Config\SceneCapture\Presets\*.xml`）を
   読み込み専用で一覧表示し、読み込み時に `ApplySceneCaptureXml(string xml)` を実装した
   全プロバイダへ `<Preset>` XML 全体を渡す。プロバイダは自分の担当セクションだけを解釈する。
2. **契約**: public static `bool ApplySceneCaptureXml(string xml)`。
   担当セクションが無い/空なら何もせず true を返す。適用失敗は false（SceneEditor は警告ログのみ）。
   **前提**: プロバイダとして登録されるには従来どおり基本の対
   （`CapturePresetXml`+`ApplyPresetXml` またはバイナリ対）と id / displayName が必須。
   `ApplySceneCaptureXml` 単独では登録されない（この注意はプロバイダガイドの追記にも含める）。
3. **XML 全体構造**: 本計画の「参照情報」にあるサンプルと同じものを記載する。
4. **`<Models>` セクション仕様**（ModItemExplorer 向け）: `Model` 要素の全項目
   （Position "x,y,z" / Rotation クォータニオン "x,y,z,w" / LocalScale / ModelType
   0=MaidEquip 1=BGObject 2=Background 3=MyRoom 4=MyRoomObject / MenuFileName /
   ObjectLayer 欠落時 20 / BGObjectId / myRoomObjectId / ModelID / ModelName /
   ModelIconName / ModelCastShadow ※古いファイルには無い）と、
   参考実装として SceneCapture 本家 `CM3D2/SceneCapture/Plugin/Instances.cs` の
   `LoadModel`（`Instances.cs:708` 付近）を挙げる。
5. **`<Effects>` セクション仕様**（PostEffects 向け）: Def クラス名 = 要素名
   （例 `BloomDef`）、子要素はフィールド名 = 要素名の文字列値
   （float は InvariantCulture、Color32 は "r,g,b,a" 0-255、enum は int、
   Texture 系は Config 相対パス文字列）。**要素が存在しないエフェクトは無効**という意味論。
   全 34 種の Def 名一覧（MaidHideDef, AntialiasingDef, ColorCorrectionCurvesDef,
   ContrastDef, CreaseDef, EdgeDetectDef, EdgeDetect2Def, GrayscaleDef, MotionBlurDef,
   NoiseAndGrainDef, SepiaDef, SunShaftsDef, TiltShiftHdrDef, AnalogGlitchDef,
   DigitalGlitchDef, IsolineDef, ObscuranceDef, CinematicBloomDef, CinematicBloomLayerDef,
   FilmicBloomDef, BloomDef, StreakDef, DepthOfFieldDef, CinematicDepthOfFieldDef,
   BokehDef, FilmicBokehDef, CinematicLensAberrationsDef, FisheyeDef,
   FilmicMedianFilterDef, RampDef, ColorCorrectionLutDef, TonemappingColorGradingDef,
   FilmicLetterBoxDef, StylisticFogDef）を記載し、
   参考として SceneCapture 本家の `SerializeStatic.cs`（フィールド反射の読み書き）を挙げる。
6. **SceneEditor 側が処理する範囲**: Camera / Misc(Background) / Lights / LightShafts は
   SceneEditor 本体が適用するため、プロバイダは触らないこと。

- [ ] **Step 3: docs-review スキルでレビュー**

作成・更新した 2 ドキュメントを docs-review スキルでレビューし、指摘を反映する。

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs(preset): SceneCapture インポートの外部プラグイン実装者向け資料を追加する"
```

---

### Task 6: 実機検証

**Files:** （コード変更なし。問題が出た場合のみ該当タスクのファイルを修正）

- [ ] **Step 1: デプロイと起動確認**

`debug.bat com3d25` で COM3D2.5 へ配備し、ゲームを起動してもらう
（起動は自動化できないためユーザーへ依頼）。起動済みなら
MCP `com3d25-devbridge` の `ping` で確認する。

- [ ] **Step 2: 一覧表示の確認**

実機でシーンプリセットウィンドウを開き、以下をスクリーンショット
（`com3d25-devbridge` の `screenshot` / `capture`）で確認:
- ルートに「SceneCapture」フォルダが表示される
- 中に `Amber Preset` 等 18 件前後が表示される（`HRK preset v2.0 DOF①` などドット入り名も含む）
- サムネはプレースホルダ（無し）表示で、x 削除ボタンとホームアイコンが出ない
- SceneCapture フォルダ表示中は保存ボタンが無効

- [ ] **Step 3: 適用の確認**

`TEST4.xml`（カメラ + ライト + Bloom のみ）を読み込み:
- カメラが注視点 (0,1.5,0)・距離 2・FOV 25 付近へ移動する（**yaw/pitch の写像が
  合っているかを最重要で確認**。ズレる場合は `ParseCamera` の euler → yaw/pitch 対応を修正）
- メインライトの向き・強度が変わる
- ログに「SceneCapture のモデル・エフェクトを適用できる外部プラグインが見つかりません」
  警告が出る（外部側未実装のため期待どおり）
- 別プリセット（`Night City Preset.xml` 等、複数ライト入り）で追加ライトの生成も確認

- [ ] **Step 4: 異常系の確認**

`eval_csharp` で壊れた XML（`<Preset><Camera><Position>bad</Position></Camera></Preset>`）を
SceneCapture Presets フォルダへ一時ファイルとして書き、更新 → 読み込みで
ダイアログ「プリセットの読み込みに失敗しました」またはフォールバック値での適用となり、
ゲームが落ちないことを確認。確認後に一時ファイルを削除する。

- [ ] **Step 5: code-review スキルでレビュー → Commit**

code-review スキルを起動し、指摘反映後に修正があればコミットする。

```bash
git add -A
git commit -m "fix(preset): SceneCapture プリセット読み込みのレビュー指摘を反映する"
```
