# Phase L0: タイムラインデータ層テスト基盤 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインのデータ層（XML シリアライズ・補間・Easing）をゲーム外で `dotnet test` により機械検証できるテストプロジェクトを新設する。

**Architecture:** ビルド済みプラグイン DLL（`bin/Debug/COM3D25/`、ゲーム DLL 同梱）を参照する SDK 形式の xUnit プロジェクト（net48）を `source/COM3D2.SceneEditor.Plugin.Tests/` に追加する。データ層ソースの再コンパイルはせず DLL 参照とする（データ層が `PhotoTransTargetObject.AttachPoint` 等ゲーム型に依存するため）。XML ゴールデンテストは「XmlSerializer で読み込み → 保存 → 正規化 XML 比較」のラウンドトリップで検証し、バージョン移行ロジック（`TimelineXml` の `version < N` 分岐）は実行しない。

**Tech Stack:** .NET Framework 4.8 / SDK スタイル csproj / xUnit / dotnet test（dotnet SDK 10 導入済み）

**Spec:** `docs/superpowers/specs/timeline-layers-roadmap.md` §Phase L0

## Global Constraints

- テストは実行前に COM3D25 構成のプラグインをビルドしておく必要がある（`bin/Debug/COM3D25/` を参照するため）。ゲーム起動不要
- **ビルドは MSBuild 直接実行を使うこと（`debug.bat` は実機フォルダへ DLL をコピーする副作用があるため使わない）**:
  `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
- データ層クラスの名前空間は `COM3D2.MotionTimelineEditor.Plugin`（`Timeline/TimelineXml.cs:6`）。`COM3D2.SceneEditor.Plugin` ではない
- 再生・適用ロジック（Maid / TBody 依存）はスコープ外。実機側は devbridge スモーク確認
- コミットメッセージは Conventional Commits 形式・日本語
- deploy.bat / release.bat は実行しない

---

### Task 1: テストプロジェクト新設 + スモークテスト

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: テストプロジェクト（`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` で実行可能）。プラグイン DLL・UnityEngine・Assembly-CSharp が参照でき、bin の全 DLL がテスト出力へコピーされるため後続タスクは追加設定不要

- [ ] **Step 1: csproj 作成**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>9</LangVersion>
    <IsPackable>false</IsPackable>
    <!-- COM3D25 構成でビルドしたプラグイン出力（ゲーム DLL 同梱）を参照する -->
    <PluginBin>..\COM3D2.SceneEditor.Plugin\bin\Debug\COM3D25</PluginBin>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="COM3D2.SceneEditor.Plugin">
      <HintPath>$(PluginBin)\COM3D2.SceneEditor.Plugin.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(PluginBin)\UnityEngine.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.CoreModule" Condition="Exists('$(PluginBin)\UnityEngine.CoreModule.dll')">
      <HintPath>$(PluginBin)\UnityEngine.CoreModule.dll</HintPath>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(PluginBin)\Assembly-CSharp.dll</HintPath>
    </Reference>
    <!-- 実行時の依存解決に必要な DLL のみコピーする。
         全 DLL のワイルドカードコピーは netstandard.dll / Newtonsoft.Json.dll 等が
         テストランナー自身の依存と衝突し FileLoadException を招くため行わない -->
    <None Include="$(PluginBin)\UnityEngine*.dll;$(PluginBin)\Assembly-CSharp*.dll;$(PluginBin)\COM3D2.SceneEditor.Plugin.dll" CopyToOutputDirectory="PreserveNewest" Visible="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: スモークテスト作成**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void TimelineXml_空データを往復シリアライズできる()
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            var src = new TimelineXml();
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                var restored = (TimelineXml)serializer.Deserialize(ms);
                Assert.NotNull(restored);
            }
        }
    }
}
```

（注: 名前空間は `COM3D2.MotionTimelineEditor.Plugin` で確認済み。ビルドエラー `CS0012`（間接参照）が出た場合は、本体 csproj の COM3D25 構成が参照する UnityEngine 分割モジュール（IMGUIModule / AssetBundleModule / AnimationModule / ImageConversionModule 等、`COM3D2.SceneEditor.Plugin.csproj:77-111` 参照）を同様に `<Reference>` 追加する）

- [ ] **Step 3: プラグインを COM3D25 構成で MSBuild 直接ビルド（Global Constraints のコマンド。`debug.bat` は使わない）してから `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` を実行し PASS を確認。FileLoadException 等の DLL 衝突が出た場合はコピー対象 DLL を減らす方向で切り分ける**

- [ ] **Step 4: コミット** `test(timeline): データ層テストプロジェクトを新設 (Phase L0)`

### Task 2: MTE XML ゴールデン（ラウンドトリップ）テスト

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/Fixtures/`（実 MTE プロジェクト XML を配置。候補: `W:\COM3D2_5\PhotoModeData\_Timeline\Works\テスト０.xml`（約 30KB、最小）。実装時に中身を確認する。TimelineLayerXml は className ベースの汎用コンテナのためレイヤー種別の多寡は往復リスクにならない。サイズが小さく version 属性が新しいものを優先）
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/XmlRoundTripTests.cs`

**Interfaces:**
- Produces: `XmlRoundTripTests.AssertRoundTrip(string path)` 相当のヘルパ。以後レイヤー移植のたびにフィクスチャ XML を 1 つ追加するだけで検証が増える

- [ ] **Step 1: フィクスチャ XML をコピーして配置**（csproj の `None Include` にワイルドカードで `Fixtures\**\*.xml` を CopyToOutputDirectory 追加）

- [ ] **Step 2: ラウンドトリップテスト作成**

```csharp
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class XmlRoundTripTests
    {
        // 読み込み → 保存 → 再読み込み → 保存 で XML が安定することを検証する。
        // バージョン移行（Migrate 相当）は通さず、シリアライザ純粋往復のみを対象とする。
        private static string SerializeToString(object xml, XmlSerializer serializer)
        {
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, xml);
                return sw.ToString();
            }
        }

        // カレントディレクトリ非依存でフィクスチャを解決する
        private static string FixtureDir =>
            Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Fixtures");

        public static TheoryData<string> FixtureFiles()
        {
            var data = new TheoryData<string>();
            foreach (var f in Directory.GetFiles(FixtureDir, "*.xml"))
                data.Add(f);
            return data;
        }

        [Fact]
        public void フィクスチャが1件以上存在する()
        {
            // フィクスチャ 0 件だと Theory がサイレントスキップになるのを防ぐ番兵
            Assert.NotEmpty(Directory.GetFiles(FixtureDir, "*.xml"));
        }

        [Theory]
        [MemberData(nameof(FixtureFiles))]
        public void フィクスチャXMLの往復が安定する(string path)
        {
            var serializer = new XmlSerializer(typeof(COM3D2.MotionTimelineEditor.Plugin.TimelineXml));
            object first;
            using (var fs = File.OpenRead(path))
                first = serializer.Deserialize(fs);
            var save1 = SerializeToString(first, serializer);
            object second;
            using (var sr = new StringReader(save1))
                second = serializer.Deserialize(sr);
            var save2 = SerializeToString(second, serializer);
            Assert.Equal(save1, save2); // 保存の安定性

            // 元 XML との情報保存性: 正規化した XDocument 比較（要素・属性の集合一致）
            var original = XDocument.Load(path);
            var saved = XDocument.Parse(save1);
            Assert.True(XNode.DeepEquals(Normalize(original.Root), Normalize(saved.Root)),
                $"正規化 XML が一致しません: {path}");
        }

        // 属性順を安定ソートし、シリアライザが出力しない既定値差異を吸収する正規化。
        // 完全一致が難しい場合は実装時に「安定性 (save1 == save2)」のみを完了条件とし、
        // DeepEquals 側は差異レポート付きの警告テストに緩和してよい。
        private static XElement Normalize(XElement e)
        {
            return new XElement(e.Name,
                e.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString()),
                e.Elements().Select(Normalize),
                e.HasElements ? null : (object)e.Value);
        }
    }
}
```

- [ ] **Step 3: `dotnet test` 実行。DeepEquals が不一致の場合はまず差異の原因を切り分ける（既定値の出力有無の差 → 緩和対象 / 要素・属性の欠落 → データ欠損バグとして修正対象）。既定値差異のみなら安定性検証（save1 == save2）を主、DeepEquals を差異ログ出力に緩和し、緩和理由をテストコメントに残す**

- [ ] **Step 4: コミット** `test(timeline): MTE XML ゴールデンラウンドトリップテストを追加`

### Task 3: 補間・Easing の数値テスト

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/InterpolationTests.cs`

- [ ] **Step 1: EasingFunctions / TangentData の公開 API を DLL から確認し（`Timeline/EasingFunctions.cs` / `Timeline/TangentData.cs` を Read）、少数の数値テストを書く**

検証観点（実 API 名に合わせて実装）:
- Easing: 各 Easing 関数が t=0 で 0、t=1 で 1 を返す（全 MoveEasingType を列挙して Theory 化）
- Linear の t=0.5 が 0.5
- Hermite/Tangent 補間: 始点値・終点値の一致、単調区間の中間値が範囲内

```csharp
// 例（実 API 確認後に調整）
[Theory]
[MemberData(nameof(AllEasingTypes))]
public void Easingは端点で0と1を返す(MoveEasingType type)
{
    Assert.Equal(0f, EasingFunctions.MoveEasing(0f, type), 3);
    Assert.Equal(1f, EasingFunctions.MoveEasing(1f, type), 3);
}
```

- [ ] **Step 2: `dotnet test` 実行、PASS 確認**
- [ ] **Step 3: コミット** `test(timeline): 補間・Easing の数値テストを追加`

### Task 4: test-runner 統合とドキュメント

**Files:**
- Modify: `README.md`（開発者向けセクションにテスト実行方法を追記）

- [ ] **Step 1: README にテスト実行方法を追記**

```markdown
## テスト

タイムラインデータ層（XML シリアライズ・補間）のゲーム外テスト。

前提: COM3D25 構成でプラグインをビルド済みであること（`debug.bat com3d25` または MSBuild 直接実行）。

    dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

- [ ] **Step 2: test-runner エージェントで `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が実行できることを確認**
- [ ] **Step 3: コミット** `docs: テスト実行方法を README に追記`
