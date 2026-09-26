# PNG配置のオーバーレイ合成と彩度調整 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このワークスペースでは subagent-driven-development を使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PNG配置（板・デカールの両方）にブレンド方式「オーバーレイ」と、キーフレームで補間できる「彩度」を追加する。

**Architecture:**
- **ブレンド方式を板・デカール共通の実体設定にする**
  - これまでデカール専用だった `PngDecalBlendMode` を `PngBlendMode` へ改名し、`Overlay = 3` を足す
  - 板もゲーム組込みシェーダーから SE 独自シェーダー `SE/PngBoard` に置き換え、通常・乗算・加算・オーバーレイを選べるようにする
- **オーバーレイは下地の色を読む必要がある**
  - 固定機能のブレンドでは作れないので、GrabPass を持つ別シェーダー（`SE/PngBoardOverlay` / `SE/DecalOverlay`）を用意する
  - C# はオーバーレイを選んだときだけマテリアルのシェーダーを差し替える。GrabPass はシェーダーに書いた時点で必ず走るため、通常のシェーダーには入れない
- **彩度はキーの値**
  - 明るさと同じくタイムラインのキーで線形補間する
  - `TransformDataPngObject` の値を 32 → 33 個に増やす（index 32 = 彩度）
  - 旧データ（32 値以下）は `FromXml` で既定値 1 に補正する（モデルのアタッチ値と同じ方式）
- **保存**
  - Undo・シーンプリセットは、PNG 配置の既存経路（`PngPlacementSnapshot` / `ScenePresetPngObject`）に項目を足す
  - タイムラインでは、ブレンド方式は実体設定（`<PngObjects>`）、彩度はキーに保存する

**Tech Stack:**
- C#（プラグイン: COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1、テスト: xUnit net48）
- シェーダー: Unity 5.6.4f1 でビルドする `se_bundle`（Built-in RP の GrabPass）
- 実機検証: com3d25-devbridge

**Spec:** 独立した spec 文書は無い。ユーザーとの合意事項は次の 3 点:
- 「オーバーレイ」はブレンド方式の選択肢として追加する（通常・乗算・加算の並び）
- ブレンド方式と彩度は、板とデカールの両方に効かせる
- 彩度はタイムラインのキーで補間する

## Global Constraints

**コードの規則**
- 思考は英語、コメントとエラーログは日本語で書く
- 数値やシェーダー名の直書きは避け、名前付き定数にする
- ブレンドの値: `PngBlendMode.Normal = 0` / `Multiply = 1` / `Additive = 2` / `Overlay = 3`
- 彩度: 範囲 0〜2、既定値 1（1 で元画像のまま、0 でグレースケール）、刻み 0.01
- 彩度はキーの index 32。`TransformDataPngObject.LegacyValueCount = 32`、`valueCount = 33`
- 彩度は輝度 `Luminance()`（UnityCG）との `lerp` で計算する。乗算・加算・オーバーレイでも、合成の前に画像の色へかける
- シェーダー名: `SE/PngBoard`、`SE/PngBoardOverlay`、`SE/Decal`（既存）、`SE/DecalOverlay`。ファイルは `UnityProject/Assets/Shaders/` 配下に置く（フォルダーの `.meta` に `assetBundleName: se_bundle` があるので、置くだけでバンドルに入る）
- 板のオーバーレイは名前なしの `GrabPass {}`（板ごとに取得）。デカールのオーバーレイは名前付きの `GrabPass { "_SEDecalOverlayGrab" }`（1 フレームに 1 回だけ取得）
  - デカールは Projector が受け側のメッシュごとに描くので、名前なしにすると画面コピーが受け側の数だけ走る
  - 名前付き GrabPass は「最初にそのバッチを描いた時点で 1 フレーム 1 回だけ取得する」（Unity マニュアル SL-GrabPass）。そのため、オーバーレイのデカール同士を重ねても互いは合成されない。仕様上の制約として扱う
- SE シェーダーがバンドルから読めないとき、板は従来のゲーム組込みシェーダー（`CM3D2/Unlit_Texture_Photo_MyObject`）で描く。このときブレンド方式と彩度は効かないが、設定値は保持して保存する
- オーバーレイ用シェーダーだけが読めないときは、通常のブレンドで描く（設定値はオーバーレイのまま保持する）
- 名前の変更（`PngDecalBlendMode` → `PngBlendMode`、`decalBlendMode` → `blendMode`、XML 要素 `DecalBlendMode` → `BlendMode`）は、デカール機能が未リリース（v2.3.0.0 より後に追加）なので互換処理なしで行う
- ブレンド方式は、表示タイプに関係なく **通常以外のときだけ** 書き出す。彩度はプリセットでは **1 以外のときだけ** 書き出す。板・通常・彩度 1 だけのシーンでは、保存内容が変更前と同じになること
- `ScenePresetData.CurrentVersion`（36）と `TimelineData.CurrentVersion`（37）は上げない
  - プリセット v36 はデカールと一緒に未リリースなので、v36 の定義を書き換える
  - タイムラインは、値の不足を `FromXml` で補正するので移行処理が要らない
- COM3D2 構成は .NET 3.5 なので、入力 5 個以上の `Func<>` / `Action<>` は使わない
- テストでは Unity のネイティブ呼び出し（`Shader.Find`、`new Material` など）を使わない

**実行禁止のコマンド**
- `deploy.bat` / `deploy.ps1`
- `debug.bat`（実機へ DLL をコピーしてしまうため）
- git worktree（使わない）

**ビルド手順（BUILD）** — 各タスクで「BUILD を実行」と書いた箇所では、次をそのまま実行する（Git Bash）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m -nologo /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" && \
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m -nologo /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

- 期待する結果: 両構成とも `0 エラー`
- 順番は COM3D2 → COM3D25 にする（テストが参照する COM3D25 の DLL を最後に作るため）

**テスト手順（TEST <filter>）** — BUILD の後に実行する:

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin.Tests
dotnet test -nologo -v q --filter "FullyQualifiedName~<filter>"
```

- フィルターを外せば、全テストを実行する

**コミット** は commit スキルで行う（Conventional Commits、日本語）。

## Review Focus

1. **旧タイムラインの PNG キーが灰色になる**: 32 値のキーは不足分が 0 で埋まるので、補正しないと彩度 0（グレースケール）で読まれる。32 値以下のキーは彩度 1 になること（Task 4 のテスト）
2. **板の見た目が変わる回帰**: 板のシェーダーを差し替えるので、既定（通常・彩度 1）の板がゲーム組込みシェーダーのときと同じに見えること。透過・両面・表示順・ZWrite を含む（Task 2 の実機比較、Task 5 の項目 1）
3. **板の保存内容が変わる回帰**: 板・通常・彩度 1 だけのシーンでは、プリセットにもタイムラインの実体設定にも新しい属性や要素が出ないこと（Task 1・Task 4 のテスト）
4. **シェーダー差し替えで表示順が戻る**: オーバーレイとの切り替えでマテリアルのシェーダーを変えても、ユーザーが設定した表示順（renderQueue）と ZWrite・Cull が保たれること（Task 3 の実装で毎回再適用し、Task 5 の項目 4 で確認する）
5. **範囲外の値**: タイムライン XML の `BlendMode=9` などは、例外にせず通常として読むこと。彩度にスライダー範囲外の値が来ても、シェーダーの出力は `saturate` で 0〜1 に収まること（Task 1 のテスト、Task 2 のシェーダー）

---

## File Structure

| ファイル | 区分 | 役割 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/PngBlendModes.cs` | 新規 | ブレンド方式 → ブレンド係数・GrabPass 要否の純粋な対応表 |
| `source/COM3D2.SceneEditor.Plugin.Tests/PngBlendModesTests.cs` | 新規 | 上記のテスト |
| `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` | 変更 | 列挙型の改名、板の SE シェーダー、ブレンドの適用（板・デカール）、彩度 |
| `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs` | 変更 | ブレンドのドロップダウン（共通欄へ移動）と彩度スライダー |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 変更 | `blendMode` / `saturation` 属性と v36 のコメント |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs` | 変更 | 捕捉・適用・比較 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | 変更 | `TimelinePngObjectXml` の `BlendMode` 要素 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | 変更 | `TimelinePngObjectData.blendMode` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs` | 変更 | 書き戻しと読込時の適用 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPngObject.cs` | 変更 | 彩度の値（index 32）と旧データの補正 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PngPlacementTimelineLayer.cs` | 変更 | 彩度の適用・補間・キー登録 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs` | 変更 | 改名と、ブレンド・彩度の書き出し条件 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelinePngDecalXmlTests.cs` | 変更 | 改名と、`BlendMode` 要素の書き出し条件 |
| `source/COM3D2.SceneEditor.Plugin.Tests/PngSaturationKeyTests.cs` | 新規 | 彩度キーの旧データ補正と既定値 |
| `UnityProject/Assets/Shaders/PngCommon.cginc` | 新規 | 彩度とオーバーレイの共通関数 |
| `UnityProject/Assets/Shaders/PngBoard.shader` / `PngBoardOverlay.shader` / `DecalOverlay.shader`（+ `.meta`） | 新規 | 板と、オーバーレイ用のシェーダー |
| `UnityProject/Assets/Shaders/Decal.shader` | 変更 | 彩度と共通関数の利用 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle` | 更新 | 再ビルドしたバンドル |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | `PngBlendModes.cs` の `<Compile>` |
| `W:\COM3D2_5\work\CLAUDE.md` | 変更 | 互換リストの更新（リポジトリ外） |

---

### Task 1: ブレンド方式を板・デカール共通の設定にし、オーバーレイを足す（保存層）

見た目はまだ変えない（板のシェーダー差し替えは Task 3）。このタスクでは、列挙型・データ・保存・Undo・Inspector の名前と書き出し条件を揃える。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/PngBlendModes.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/PngBlendModesTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Manager\PngDecalProjection.cs" />` の直前に `<Compile Include="Manager\PngBlendModes.cs" />`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs:19-28, 57, 428-456, 690-698`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:214-229, 1083-1085`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:81-97`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:182-244`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs:83-85, 262-274, 322-328`
- Modify: `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs`（`SetDecalBlendMode` の呼び出しを追随させるだけ。UI の移動は Task 3）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs`、`TimelinePngDecalXmlTests.cs`

**Interfaces:**
- Produces（名前空間 `COM3D2.SceneEditor.Plugin`）:
  - `public enum PngBlendMode { Normal = 0, Multiply = 1, Additive = 2, Overlay = 3 }`
  - `public static class PngBlendModes`
    - `public static void GetBlendFactors(PngBlendMode mode, out UnityEngine.Rendering.BlendMode src, out UnityEngine.Rendering.BlendMode dst)`
    - `public static bool UsesGrab(PngBlendMode mode)`（Overlay だけ true）
    - `public static PngBlendMode ResolveRenderMode(PngBlendMode mode, bool hasGrabShader)`（オーバーレイ用シェーダーが無いときだけ Normal）
  - `PngObjectData.blendMode`（`PngBlendMode`、既定 `Normal`）
  - `PngPlacementManager.SetBlendMode(PngObjectData data, PngBlendMode blendMode)`（変化したら `entitySettingsRevision++`）
  - `ScenePresetPngObject.blendMode`（属性 `blendMode`）
  - `TimelinePngObjectXml.blendMode`（要素 `BlendMode`）、`TimelinePngObjectData.blendMode`（int）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PngBlendModesTests.cs` を新規作成する:

```csharp
using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド方式とブレンド係数・GrabPass 要否の対応を固定する。
    /// 係数はシェーダー側の合成式 (乗算は透明部を白へ寄せる等) と対になっているため、ずれると見た目が壊れる
    /// </summary>
    public class PngBlendModesTests
    {
        [Theory]
        [InlineData(PngBlendMode.Normal, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha)]
        [InlineData(PngBlendMode.Multiply, BlendMode.DstColor, BlendMode.Zero)]
        [InlineData(PngBlendMode.Additive, BlendMode.SrcAlpha, BlendMode.One)]
        // オーバーレイはシェーダー内で下地と合成した色を、通常の半透明合成で重ねる
        [InlineData(PngBlendMode.Overlay, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha)]
        public void ブレンド方式ごとの係数(PngBlendMode mode, BlendMode expectedSrc, BlendMode expectedDst)
        {
            BlendMode src;
            BlendMode dst;
            PngBlendModes.GetBlendFactors(mode, out src, out dst);

            Assert.Equal(expectedSrc, src);
            Assert.Equal(expectedDst, dst);
        }

        [Theory]
        [InlineData(PngBlendMode.Normal, false)]
        [InlineData(PngBlendMode.Multiply, false)]
        [InlineData(PngBlendMode.Additive, false)]
        [InlineData(PngBlendMode.Overlay, true)]
        public void 下地を読むのはオーバーレイだけ(PngBlendMode mode, bool expected)
        {
            Assert.Equal(expected, PngBlendModes.UsesGrab(mode));
        }

        [Theory]
        // オーバーレイ用シェーダーが読めないときだけ通常へ落とす
        [InlineData(PngBlendMode.Overlay, false, PngBlendMode.Normal)]
        [InlineData(PngBlendMode.Overlay, true, PngBlendMode.Overlay)]
        [InlineData(PngBlendMode.Multiply, false, PngBlendMode.Multiply)]
        [InlineData(PngBlendMode.Additive, true, PngBlendMode.Additive)]
        public void 描画するブレンド方式の解決(PngBlendMode mode, bool hasGrabShader, PngBlendMode expected)
        {
            Assert.Equal(expected, PngBlendModes.ResolveRenderMode(mode, hasGrabShader));
        }

        [Fact]
        public void 列挙値は保存形式と一致する()
        {
            // 値はプリセットとタイムライン XML に整数で保存され、シェーダーの _BlendMode 判定とも対応する
            Assert.Equal(0, (int)PngBlendMode.Normal);
            Assert.Equal(1, (int)PngBlendMode.Multiply);
            Assert.Equal(2, (int)PngBlendMode.Additive);
            Assert.Equal(3, (int)PngBlendMode.Overlay);
        }
    }
}
```

`TimelinePngDecalXmlTests.cs` を次のとおり直す:
- 全体の `PngDecalBlendMode` → `PngBlendMode`、`decalBlendMode` → `blendMode`
- `範囲外の値は板と通常として読む` の XML 文字列の `<DecalBlendMode>-1</DecalBlendMode>` → `<BlendMode>9</BlendMode>`
- `板ではデカールの要素を書き出さない` の `data` から `decalBlendMode = ...` 行を消す（ブレンドは板でも書き出すようになったため）
- 次の 2 テストを末尾に足す:

```csharp
        [Fact]
        public void 板でもブレンドが通常以外なら書き出す()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                blendMode = (int)PngBlendMode.Overlay,
            };

            var text = Serialize(data.ToXml());
            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Contains("<BlendMode>3</BlendMode>", text);
            Assert.DoesNotContain("DisplayType", text);
            Assert.Equal((int)PngBlendMode.Overlay, restored.blendMode);
        }

        [Fact]
        public void 通常のブレンドは書き出さない()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                displayType = (int)PngDisplayType.Decal,
            };

            var text = Serialize(data.ToXml());

            Assert.DoesNotContain("BlendMode", text);
        }
```

`ScenePresetPngDecalTests.cs` を次のとおり直す:
- 全体の `PngDecalBlendMode` → `PngBlendMode`、`decalBlendMode` → `blendMode`
- `板では表示タイプとデカール設定を書き出さない` の `png` から `blendMode = ...` 行を消す
- 次のテストを末尾に足す:

```csharp
        [Fact]
        public void 板でもブレンドが通常以外なら書き出す()
        {
            var png = new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                blendMode = PngBlendMode.Overlay,
            };

            var xml = Serialize(png);
            var restored = Deserialize(Serialize(WithPng(png)));

            Assert.Contains("blendMode=\"Overlay\"", xml);
            Assert.DoesNotContain("displayType", xml);
            Assert.Equal(PngBlendMode.Overlay, Assert.Single(restored.pngPlacement.objects).blendMode);
        }
```

- [ ] **Step 2: ビルドが失敗することを確認する**

BUILD を実行する。期待する結果: 本体は成功する。続けて `TEST PngBlend` を実行すると、テストプロジェクトが `PngBlendMode` / `PngBlendModes` 未定義でコンパイルエラーになる。

- [ ] **Step 3: `PngBlendModes` を作る**

`source/COM3D2.SceneEditor.Plugin/Manager/PngBlendModes.cs`:

```csharp
using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置 (板・デカール共通) のブレンド方式。値はシーンプリセットとタイムライン XML に保存され、
    /// シェーダー SE/PngBoard・SE/Decal の _BlendMode 判定とも対応する
    /// </summary>
    public enum PngBlendMode
    {
        Normal = 0,
        Multiply = 1,
        Additive = 2,
        /// <summary>下地の色を読んで合成する。GrabPass を持つ専用シェーダーへ差し替えて描く</summary>
        Overlay = 3,
    }

    /// <summary>ブレンド方式からマテリアルへ渡す値を決める純粋関数群</summary>
    public static class PngBlendModes
    {
        public static void GetBlendFactors(PngBlendMode mode, out BlendMode src, out BlendMode dst)
        {
            switch (mode)
            {
                case PngBlendMode.Multiply:
                    // シェーダーが透明部を白 (変化なし) へ寄せた色を出す
                    src = BlendMode.DstColor;
                    dst = BlendMode.Zero;
                    break;
                case PngBlendMode.Additive:
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.One;
                    break;
                default:
                    // 通常と、シェーダー内で下地と合成済みのオーバーレイ
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.OneMinusSrcAlpha;
                    break;
            }
        }

        /// <summary>下地を GrabPass で読むシェーダーが要るか</summary>
        public static bool UsesGrab(PngBlendMode mode)
        {
            return mode == PngBlendMode.Overlay;
        }

        /// <summary>
        /// 実際に描くブレンド方式。オーバーレイ用シェーダーが無いときは通常で描く
        /// (設定値そのものは変えず、保存もオーバーレイのまま)
        /// </summary>
        public static PngBlendMode ResolveRenderMode(PngBlendMode mode, bool hasGrabShader)
        {
            return UsesGrab(mode) && !hasGrabShader ? PngBlendMode.Normal : mode;
        }
    }
}
```

csproj に `<Compile Include="Manager\PngBlendModes.cs" />` を `<Compile Include="Manager\PngDecalProjection.cs" />` の直前へ足す。

- [ ] **Step 4: `PngPlacementManager` を改名に追随させる**

- ファイル先頭の `PngDecalBlendMode` 列挙型（19〜28 行）を削除する（`PngBlendModes.cs` へ移した）
- `PngObjectData` の `public PngDecalBlendMode decalBlendMode = PngDecalBlendMode.Normal;` を、デカール設定の塊から出して `renderQueue` の直後へ移し、次にする:

```csharp
        /// <summary>板・デカール共通のブレンド方式</summary>
        public PngBlendMode blendMode = PngBlendMode.Normal;
```

- `ApplyDecalBlendMode` の switch を `PngBlendModes.GetBlendFactors` に置き換える（板への適用とシェーダー差し替えは Task 3 で足す）:

```csharp
        private static void ApplyDecalBlendMode(PngObjectData data)
        {
            var material = data.decalMaterial;
            if (material == null)
            {
                return;
            }

            UnityEngine.Rendering.BlendMode src;
            UnityEngine.Rendering.BlendMode dst;
            PngBlendModes.GetBlendFactors(data.blendMode, out src, out dst);
            material.SetInt(SrcBlendId, (int)src);
            material.SetInt(DstBlendId, (int)dst);
            material.SetFloat(BlendModeId, (int)data.blendMode);
        }
```

- `SetDecalBlendMode` を `SetBlendMode` に改名する:

```csharp
        public void SetBlendMode(PngObjectData data, PngBlendMode blendMode)
        {
            if (data.blendMode != blendMode)
            {
                entitySettingsRevision++;
            }
            data.blendMode = blendMode;
            ApplyDecalBlendMode(data);
        }
```

- `entitySettingsRevision` の doc コメントの「(表示順・表示タイプ・デカール設定)」を「(表示順・表示タイプ・ブレンド方式・デカール設定)」へ直す

- [ ] **Step 5: プリセット DTO を直す**

`ScenePresetData.cs` の `ScenePresetPngObject`:

```csharp
        [XmlAttribute]
        public PngDisplayType displayType = PngDisplayType.Board;

        /// <summary>板・デカール共通のブレンド方式</summary>
        [XmlAttribute]
        public PngBlendMode blendMode = PngBlendMode.Normal;

        [XmlAttribute]
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;

        [XmlAttribute]
        public bool decalProjectOnMaids;

        // 板のプリセットを v35 以前と同じ内容に保つため、表示タイプとデカール設定は板では書き出さない。
        // ブレンド方式は板にも効くため、表示タイプに関係なく既定値 (通常) 以外のときだけ書き出す
        public bool ShouldSerializedisplayType() { return IsDecal(); }
        public bool ShouldSerializeblendMode() { return blendMode != PngBlendMode.Normal; }
        public bool ShouldSerializedecalFadeAngle() { return IsDecal(); }
        public bool ShouldSerializedecalProjectOnMaids() { return IsDecal(); }
```

v36 のコメント（1083〜1085 行）を次にする:

```csharp
        // v36: pngPlacement の png に表示タイプ (displayType)、ブレンド方式 (blendMode)、
        //      デカール設定 (decalFadeAngle / decalProjectOnMaids) を追加。表示タイプとデカール設定は板では、
        //      ブレンド方式は通常のときは書き出さない。旧形式は属性が無く板・通常として読める
```

- [ ] **Step 6: Undo スナップショットを直す**

`PngPlacementSnapshot.cs` の 3 か所:
- `CaptureState`: `decalBlendMode = data.decalBlendMode,` → `blendMode = data.blendMode,`
- `ApplyState`: `manager.SetDecalBlendMode(data, objState.decalBlendMode);` → `manager.SetBlendMode(data, objState.blendMode);`
- `Approximately`: `|| a.decalBlendMode != b.decalBlendMode` → `|| a.blendMode != b.blendMode`

- [ ] **Step 7: タイムライン XML とデータを直す**

`TimelineXml.cs` の `TimelinePngObjectXml`:

```csharp
        // 表示タイプとデカール設定は SE 独自。板では書き出さず、
        // MTE 産の XML を保存し直しても内容を変えない (MTE は未知の要素を読み飛ばし板として表示する)
        [XmlElement("DisplayType")]
        public int displayType;
        // ブレンド方式も SE 独自。板にも効くため、表示タイプに関係なく通常以外のときだけ書き出す
        [XmlElement("BlendMode")]
        public int blendMode;
        [XmlElement("DecalFadeAngle")]
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;
        [XmlElement("DecalProjectOnMaids")]
        public bool decalProjectOnMaids;

        public bool ShouldSerializedisplayType() { return IsDecal(); }
        public bool ShouldSerializeblendMode() { return blendMode != (int)PngBlendMode.Normal; }
        public bool ShouldSerializedecalFadeAngle() { return IsDecal(); }
        public bool ShouldSerializedecalProjectOnMaids() { return IsDecal(); }
```

`TimelineData.cs` の `TimelinePngObjectData`:
- フィールド `public int decalBlendMode;` → `public int blendMode;`
- `FromXml` の該当部分:

```csharp
            blendMode = System.Enum.IsDefined(typeof(SE.PngBlendMode), xml.blendMode)
                ? xml.blendMode
                : (int)SE.PngBlendMode.Normal;
```

- `ToXml` の `decalBlendMode = decalBlendMode,` → `blendMode = blendMode,`

`PngObjectTimelineManager.cs`:
- `ApplyEntitySettings`: `sePngManager.SetDecalBlendMode(target, (SE.PngDecalBlendMode)source.decalBlendMode);` → `sePngManager.SetBlendMode(target, (SE.PngBlendMode)source.blendMode);`
- `UpdateTimelineData`: `data.decalBlendMode = (int)se.decalBlendMode;` → `data.blendMode = (int)se.blendMode;`
- 改訂番号とSetupのコメントの「表示順・表示タイプ・デカール設定」を「表示順・表示タイプ・ブレンド方式・デカール設定」へ直す

- [ ] **Step 8: Inspector を追随させる**

`PngPlacementInspector.cs` の `DrawDecalRows` 内:
- `(int)data.decalBlendMode` → `(int)data.blendMode`（2 か所）
- `pngManager.SetDecalBlendMode(data, (PngDecalBlendMode)blendIndex);` → `pngManager.SetBlendMode(data, (PngBlendMode)blendIndex);`
- コメント `// PngDisplayType / PngDecalBlendMode の値順に並べる` → `// PngDisplayType / PngBlendMode の値順に並べる`

（`BlendModeLabels` はまだ 3 要素のまま。オーバーレイの選択肢は Task 3 でドロップダウンにするときに足す）

- [ ] **Step 9: 取り残しが無いことを確認する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
grep -rn "PngDecalBlendMode\|decalBlendMode\|DecalBlendMode\|SetDecalBlendMode" --include=*.cs COM3D2.SceneEditor.Plugin COM3D2.SceneEditor.Plugin.Tests
```

期待する結果: 出力なし。

- [ ] **Step 10: ビルドとテスト**

BUILD を実行し、続けて `TEST Png` を実行する。期待する結果: 両構成 0 エラー、テストはすべて PASS。

- [ ] **Step 11: コミット**

commit スキルでコミットする（例: `refactor(png): ブレンド方式を板・デカール共通の設定にしオーバーレイを追加する`）。

---

### Task 2: 板とオーバーレイのシェーダー、彩度、バンドル

**Files:**
- Create: `UnityProject/Assets/Shaders/PngCommon.cginc`
- Create: `UnityProject/Assets/Shaders/PngBoard.shader`
- Create: `UnityProject/Assets/Shaders/PngBoardOverlay.shader`
- Create: `UnityProject/Assets/Shaders/DecalOverlay.shader`
- Modify: `UnityProject/Assets/Shaders/Decal.shader`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle`（再ビルドした成果物）
- `.meta` は Unity が生成する

**Interfaces:**
- Consumes: `PngBlendMode` の値（Task 1）。`_BlendMode` 1 = 乗算
- Produces（シェーダーのプロパティ。Task 3 の C# が設定する）:
  - 4 つのシェーダー共通: `_MainTex`、`_Color`、`_Saturation`（既定 1）、`_BlendMode`、`_SrcBlend`、`_DstBlend`
  - 板（`SE/PngBoard` / `SE/PngBoardOverlay`）のみ: `_ZWrite`、`_Cull`（ゲーム組込みシェーダーと同じ名前にし、既存の `SetInt("_ZWrite")` / `SetInt("_Cull")` をそのまま使えるようにする）
  - デカール（`SE/Decal` / `SE/DecalOverlay`）のみ: 既存の `_DecalMatrix`、`_DecalNormal`、`_FadeCosMin`、`_FadeCosMax`
  - バンドル内のパス: `Assets/Shaders/PngBoard.shader`、`Assets/Shaders/PngBoardOverlay.shader`、`Assets/Shaders/DecalOverlay.shader`（`TimelineBundleManager.LoadShader("PngBoard")` などで読める）

- [ ] **Step 1: 共通関数を書く**

`UnityProject/Assets/Shaders/PngCommon.cginc`:

```hlsl
#ifndef SE_PNG_COMMON_INCLUDED
#define SE_PNG_COMMON_INCLUDED

#include "UnityCG.cginc"

// PNG 配置 (板・デカール) の共通処理。
// 値の意味は C# の PngPlacementManager / PngBlendMode と対応する

// 彩度。0 でグレースケール、1 で元の色、1 より大きいと強調する。
// 範囲外の値でも出力を 0〜1 に収める
inline fixed3 PngApplySaturation(fixed3 rgb, float saturation)
{
    fixed luma = Luminance(rgb);
    return saturate(lerp(fixed3(luma, luma, luma), rgb, saturation));
}

// _BlendMode 1 = PngBlendMode.Multiply (DstColor Zero): 透明な所は白 (変化なし) へ寄せる
inline fixed4 PngApplyMultiply(fixed4 col, float blendMode)
{
    if (blendMode > 0.5 && blendMode < 1.5)
    {
        return fixed4(lerp(fixed3(1, 1, 1), col.rgb, col.a), 1);
    }
    return col;
}

// オーバーレイ合成 (Photoshop と同じ式)。base が下地、blend が画像の色
inline fixed3 PngOverlay(fixed3 base, fixed3 blend)
{
    fixed3 low = 2 * base * blend;
    fixed3 high = 1 - 2 * (1 - base) * (1 - blend);
    return lerp(low, high, step(0.5, base));
}

#endif
```

- [ ] **Step 2: 板のシェーダーを書く**

`UnityProject/Assets/Shaders/PngBoard.shader`:

```hlsl
Shader "SE/PngBoard"
{
    // ゲーム組込みの CM3D2/Unlit_Texture_Photo_MyObject (マイオブジェクト) と同じ見た目の
    // 無照明・半透明の板に、ブレンド方式と彩度を足したもの。実行時の値は C# が上書きする
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        // PngBlendMode の値
        _BlendMode ("Blend Mode", Float) = 0
        // SrcAlpha / OneMinusSrcAlpha
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        // ゲーム組込みシェーダーと同じ名前。C# が透過画像なら 0 にする
        _ZWrite ("ZWrite", Float) = 1
        // UnityEngine.Rendering.CullMode。C# が両面描画 (0) にする
        _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Saturation;
            float _BlendMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                return PngApplyMultiply(col, _BlendMode);
            }
            ENDCG
        }
    }
}
```

`"IgnoreProjector"="True"` は、デカールが板へ投影されるのを防ぐため（ゲーム組込みシェーダーの板にデカールが載るかは Step 6 で確認し、載っていたならこのタグを外して挙動を合わせる）。

- [ ] **Step 3: 板のオーバーレイシェーダーを書く**

`UnityProject/Assets/Shaders/PngBoardOverlay.shader`:

```hlsl
Shader "SE/PngBoardOverlay"
{
    // SE/PngBoard のオーバーレイ版。板ごとに下地を取得して合成する
    // (板は 1 枚 1 描画なので、名前なし GrabPass でも取得回数は板の枚数で済む)
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        _BlendMode ("Blend Mode", Float) = 3
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        _ZWrite ("ZWrite", Float) = 1
        _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        GrabPass { }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _GrabTexture;
            fixed4 _Color;
            float _Saturation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                fixed3 base = tex2Dproj(_GrabTexture, i.grabPos).rgb;
                // 不透明度は通常の半透明合成 (SrcAlpha / OneMinusSrcAlpha) で効かせる
                return fixed4(PngOverlay(base, col.rgb), col.a);
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 4: デカールに彩度を足し、オーバーレイ版を書く**

`UnityProject/Assets/Shaders/Decal.shader` を直す:
- Properties の `_Color` の次に `_Saturation ("Saturation", Float) = 1` を足す
- `_BlendMode` のコメント `// PngDecalBlendMode の値` → `// PngBlendMode の値`
- `#include "UnityCG.cginc"` → `#include "PngCommon.cginc"`
- 変数宣言に `float _Saturation;` を足す
- `frag` の末尾（`fixed4 col = tex2D(...) * _Color;` 以降）を次に置き換える:

```hlsl
                fixed4 col = tex2D(_MainTex, uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                col.a *= fade;
                return PngApplyMultiply(col, _BlendMode);
```

`UnityProject/Assets/Shaders/DecalOverlay.shader` を新規作成する。`Decal.shader`（上の修正後）を丸ごと複製し、次だけ変える:
- 1 行目を `Shader "SE/DecalOverlay"` にする
- 冒頭コメントを次にする:

```hlsl
    // SE/Decal のオーバーレイ版。Projector は受け側のメッシュごとに描くため、
    // 名前付き GrabPass で下地の取得を 1 フレーム 1 回に抑える。
    // そのためオーバーレイのデカール同士を重ねても互いは合成されない
```

- `_BlendMode` の既定値を `3` にする
- `SubShader` の `Tags` の直後（`Pass` の前）に `GrabPass { "_SEDecalOverlayGrab" }` を足す
- `v2f` に `float4 grabPos : TEXCOORD2;` を足し、`vert` の `return o;` の前に `o.grabPos = ComputeGrabScreenPos(o.pos);` を足す
- 変数宣言に `sampler2D _SEDecalOverlayGrab;` を足す
- `frag` の末尾を次にする:

```hlsl
                fixed4 col = tex2D(_MainTex, uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                col.a *= fade;
                fixed3 base = tex2Dproj(_SEDecalOverlayGrab, i.grabPos).rgb;
                return fixed4(PngOverlay(base, col.rgb), col.a);
```

- [ ] **Step 5: バンドルをビルドしてプラグインへコピーする**

Unity エディタで UnityProject を開いていないことを確認してから実行する（Git Bash）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
ROOT='W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin'
mkdir -p /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Logs
"/c/Program Files/Unity/Hub/Editor/5.6.4f1/Editor/Unity.exe" -batchmode -nographics -quit \
  -projectPath "$ROOT\UnityProject" \
  -executeMethod CreateAssetBundles.BuildAllAssetBundlesBatch \
  -logFile "$ROOT\UnityProject\Logs\build-bundle.log"
echo "exit=$?"
grep -n "error\|Png\|Decal" /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Logs/build-bundle.log | head -20
cp /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/UnityProject/Assets/Bundles/se_bundle \
   /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin && git status --short UnityProject source/COM3D2.SceneEditor.Plugin/Timeline/se_bundle
```

期待する結果:
- `exit=0`
- ログにシェーダーのコンパイルエラーが無い
- `git status` に新規の `.shader` / `.cginc` とその `.meta`、変更の `Decal.shader`、`se_bundle`、`Bundles/*` が出る

- [ ] **Step 6: 実機（COM3D2.5）でシェーダーのスモークテストを行う**

devbridge の `ping` でゲームの起動を確かめる。起動していなければこのステップは飛ばし、Task 5 でまとめて確認する（飛ばしたことを報告に書く）。

ロード済みの `se_bundle` を新しいバンドルへ差し替え、4 つのシェーダーが読めることを確かめる（`eval_csharp`）:

```csharp
var asm = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "COM3D2.SceneEditor.Plugin");
var bmType = asm.GetType("COM3D2.MotionTimelineEditor.Plugin.TimelineBundleManager");
var bm = bmType.GetProperty("instance").GetValue(null, null);
var field = bmType.GetField("_assetBundle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var old = (UnityEngine.AssetBundle)field.GetValue(bm);
if (old != null) old.Unload(false);
var nb = UnityEngine.AssetBundle.LoadFromFile(@"W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\UnityProject\Assets\Bundles\se_bundle");
field.SetValue(bm, nb);
string.Join(" ", new[] { "PngBoard", "PngBoardOverlay", "Decal", "DecalOverlay" }.Select(n => {
    var s = nb.LoadAsset<UnityEngine.Shader>("Assets/Shaders/" + n + ".shader");
    return n + "=" + (s == null ? "null" : s.isSupported.ToString());
}).ToArray())
```

期待する結果: `PngBoard=True PngBoardOverlay=True Decal=True DecalOverlay=True`

続けて、カメラの前にゲーム組込みシェーダーの板と `SE/PngBoard` の板を左右に並べ、同じ半透明のグラデーション画像を貼る:

```csharp
var cam = UnityEngine.Camera.main.transform;
var tex = new UnityEngine.Texture2D(64, 64);
for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
    tex.SetPixel(x, y, UnityEngine.Color.HSVToRGB(x / 64f, 1f, 1f) * new UnityEngine.Color(1, 1, 1, 0) + new UnityEngine.Color(0, 0, 0, y / 63f));
tex.Apply();
System.Func<string, UnityEngine.Shader, float, UnityEngine.Material> place = (name, shader, offset) => {
    var q = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
    q.name = name;
    q.transform.position = cam.position + cam.forward * 2f + cam.right * offset;
    q.transform.rotation = UnityEngine.Quaternion.LookRotation(cam.forward, cam.up);
    var m = new UnityEngine.Material(shader);
    m.mainTexture = tex; m.renderQueue = 3000;
    m.SetInt("_ZWrite", 0); m.SetInt("_Cull", 0);
    q.GetComponent<UnityEngine.MeshRenderer>().material = m;
    return m;
};
var gameMat = place("__png_game", UnityEngine.Shader.Find("CM3D2/Unlit_Texture_Photo_MyObject"), -0.6f);
var seMat = place("__png_se", nb.LoadAsset<UnityEngine.Shader>("Assets/Shaders/PngBoard.shader"), 0.6f);
"placed"
```

devbridge の `screenshot` で撮る。期待する結果: 左右の板が同じ色・同じ透け方に見える（下が透明、上が不透明の虹色）。

続けて彩度とオーバーレイを確かめる:

```csharp
seMat.SetFloat("_Saturation", 0f);
"saturation 0"
```

`screenshot` で右の板がグレースケールになることを確かめる。

```csharp
seMat.SetFloat("_Saturation", 1f);
seMat.shader = nb.LoadAsset<UnityEngine.Shader>("Assets/Shaders/PngBoardOverlay.shader");
seMat.renderQueue = 3000;
"overlay"
```

`screenshot` で右の板が背景と重なった部分で、背景の明暗を残したまま色付くこと（背景の暗い所は暗く、明るい所は明るいまま）を確かめる。

片付け:

```csharp
foreach (var n in new[] { "__png_game", "__png_se" }) { var g = UnityEngine.GameObject.Find(n); if (g != null) UnityEngine.Object.Destroy(g); }
"cleaned"
```

左右の見た目が違う場合（色の濃さ・透け方）は、ゲーム組込みシェーダーのマテリアルで `_Color` の既定値と `Blend` を調べ（`gameMat.GetColor("_Color")` 等）、`PngBoard.shader` を合わせてから Step 5 に戻る。

- [ ] **Step 7: コミット**

新規・変更したシェーダーと `.meta`、`Bundles/*` の生成物、`Timeline/se_bundle` を commit スキルでコミットする（例: `feat(png): 板とオーバーレイ合成のシェーダーを追加し彩度に対応する`）。

---

### Task 3: マネージャーで板の SE シェーダー・ブレンド・彩度を適用し、Inspector に出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs`

**Interfaces:**
- Consumes: `PngBlendMode`、`PngBlendModes.GetBlendFactors` / `UsesGrab`、`SetBlendMode`（Task 1）。シェーダー名とプロパティ（Task 2）
- Produces:
  - `PngObjectData.saturation`（float、既定 `PngPlacementManager.DefaultSaturation`）
  - `PngPlacementManager.DefaultSaturation = 1f`、`MinSaturation = 0f`、`MaxSaturation = 2f`
  - `PngPlacementManager.SetSaturation(PngObjectData data, float saturation)`（`entitySettingsRevision` は増やさない。キーの値のため）

このタスクはゲームのランタイムに依存するので新しい単体テストは無い（純粋な判定 `ResolveRenderMode` は Task 1 でテスト済み）。ビルドと Task 5 の実機検証で確かめる。

- [ ] **Step 1: 定数とフィールドを足す**

`PngPlacementManager.cs`:

`PngObjectData` の `blendMode` の直後に:

```csharp
        /// <summary>彩度。0 でグレースケール、1 で元の色。タイムラインではキーの値</summary>
        public float saturation = PngPlacementManager.DefaultSaturation;
```

`PngPlacementManager` の定数群（`DefaultRenderQueue` の後）に:

```csharp
        public const float DefaultSaturation = 1f;
        public const float MinSaturation = 0f;
        public const float MaxSaturation = 2f;
```

シェーダー名の定数（`DECAL_SHADER_NAME` の隣）を次にする:

```csharp
        // se_bundle 内のシェーダー (Assets/Shaders/<名前>.shader)
        private const string BOARD_SHADER_NAME = "PngBoard";
        private const string BOARD_OVERLAY_SHADER_NAME = "PngBoardOverlay";
        private const string DECAL_SHADER_NAME = "Decal";
        private const string DECAL_OVERLAY_SHADER_NAME = "DecalOverlay";
```

プロパティ ID に `private static readonly int SaturationId = Shader.PropertyToID("_Saturation");` を足す。

`SHADER_NAME` / `FALLBACK_SHADER_NAME` のコメントを「SE のシェーダーが読めないときの代替 (マイオブジェクトと同じゲーム組込みシェーダー)」へ直す。

- [ ] **Step 2: バンドルのシェーダー読込を 1 か所にまとめる**

`_decalShader` / `_isDecalShaderMissing` と `GetDecalShader()` を削除し、次に置き換える:

```csharp
        /// <summary>
        /// se_bundle から読んだシェーダー。読めなかった名前は null を入れ、
        /// 失敗を毎回ログへ出さないよう再試行しない
        /// </summary>
        private readonly Dictionary<string, Shader> _bundleShaders = new Dictionary<string, Shader>();

        private Shader GetBundleShader(string shaderName)
        {
            Shader shader;
            if (!_bundleShaders.TryGetValue(shaderName, out shader))
            {
                shader = MTEP.TimelineBundleManager.instance.LoadShader(shaderName);
                _bundleShaders[shaderName] = shader;
            }
            return shader;
        }
```

`CreateDecal` の `var shader = GetDecalShader();` → `var shader = GetBundleShader(DECAL_SHADER_NAME);`

`ReleaseAll` の末尾に `_bundleShaders.Clear();` を足す（シーン切替後にバンドルが読み直されても追従するため）。

- [ ] **Step 3: 板のマテリアルを SE シェーダーで作る**

`GetShader()` を次に置き換える:

```csharp
        /// <summary>
        /// 板のシェーダー。SE/PngBoard が読めなければゲーム組込みシェーダーで描く
        /// (このときブレンド方式と彩度は効かないが、設定値は保持して保存する)
        /// </summary>
        private Shader GetBoardShader()
        {
            var shader = GetBundleShader(BOARD_SHADER_NAME);
            if (shader != null)
            {
                return shader;
            }

            if (_shader == null)
            {
                _shader = Shader.Find(SHADER_NAME);
                if (_shader == null)
                {
                    MTEUtils.LogWarning("シェーダーが見つからないため代替を使います: {0}",
                        SHADER_NAME);
                    _shader = Shader.Find(FALLBACK_SHADER_NAME);
                }
            }
            return _shader;
        }
```

`AddPng` の `var shader = GetShader();` → `var shader = GetBoardShader();`。`quad.GetComponent<MeshRenderer>().material = material;` の直後に次を足す（SE シェーダーの既定値を実体の既定値に揃える）:

```csharp
            material.SetFloat(SaturationId, DefaultSaturation);
```

`data` の生成後（`_pngObjects.Add(data);` の前）に `ApplyBlendMode(data);` を足す。

- [ ] **Step 4: ブレンドの適用を板・デカール共通にする**

`ApplyDecalBlendMode` を削除し、次に置き換える:

```csharp
        /// <summary>
        /// ブレンド方式を板とデカールのマテリアルへ適用する。
        /// オーバーレイは GrabPass を持つ専用シェーダーへ差し替える
        /// (GrabPass はシェーダーに書くと常に走るため、通常のシェーダーには入れていない)
        /// </summary>
        private void ApplyBlendMode(PngObjectData data)
        {
            var useGrab = PngBlendModes.UsesGrab(data.blendMode);

            // ゲーム組込みシェーダーで描いている板 (SE シェーダーが読めない) には適用しない
            var boardShader = GetBundleShader(BOARD_SHADER_NAME);
            if (data.material != null && boardShader != null)
            {
                var overlay = useGrab ? GetBundleShader(BOARD_OVERLAY_SHADER_NAME) : null;
                SetBlendMaterial(data.material, overlay ?? boardShader,
                    PngBlendModes.ResolveRenderMode(data.blendMode, overlay != null));
                // シェーダーの差し替えで表示順がシェーダー既定へ戻らないよう、毎回設定し直す
                data.material.renderQueue = data.renderQueue;
            }

            if (data.decalMaterial != null)
            {
                var decalShader = GetBundleShader(DECAL_SHADER_NAME);
                var overlay = useGrab ? GetBundleShader(DECAL_OVERLAY_SHADER_NAME) : null;
                SetBlendMaterial(data.decalMaterial, overlay ?? decalShader,
                    PngBlendModes.ResolveRenderMode(data.blendMode, overlay != null));
            }
        }

        private static void SetBlendMaterial(Material material, Shader shader, PngBlendMode mode)
        {
            // 同じシェーダーの再代入でもキーワード等の再構築が走るため、変わるときだけ差し替える
            if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            UnityEngine.Rendering.BlendMode src;
            UnityEngine.Rendering.BlendMode dst;
            PngBlendModes.GetBlendFactors(mode, out src, out dst);
            material.SetInt(SrcBlendId, (int)src);
            material.SetInt(DstBlendId, (int)dst);
            material.SetFloat(BlendModeId, (int)mode);
        }
```

`ApplyBlendMode` はインスタンスメソッドになるので、呼び出し元を直す:
- `CreateDecal` の `ApplyDecalBlendMode(data);` → `ApplyBlendMode(data);`。その直前に `material.SetFloat(SaturationId, data.saturation);` を足す
- `SetBlendMode` の `ApplyDecalBlendMode(data);` → `ApplyBlendMode(data);`

`SetRenderQueue` はそのまま（`material.renderQueue` を直接設定している）。`_ZWrite` / `_Cull` はマテリアルのプロパティとしてシェーダー差し替え後も残るので、設定し直さない（Task 5 の項目 4 で確認する）。

- [ ] **Step 5: 彩度のセッターを足す**

`SetColor` の後に:

```csharp
        /// <summary>
        /// 彩度を設定する。キーの値で再生中に毎フレーム変わりうるため改訂番号は増やさない
        /// (色・明るさと同じ扱い)
        /// </summary>
        public void SetSaturation(PngObjectData data, float saturation)
        {
            data.saturation = Mathf.Clamp(saturation, MinSaturation, MaxSaturation);
            if (data.material != null)
            {
                data.material.SetFloat(SaturationId, data.saturation);
            }
            if (data.decalMaterial != null)
            {
                data.decalMaterial.SetFloat(SaturationId, data.saturation);
            }
        }
```

クラス冒頭の doc コメント（「描画はマイオブジェクトと同じ Unlit シェーダーを使い、」）を「描画は SE 独自の無照明シェーダー (読めなければマイオブジェクトと同じ組込みシェーダー) を使い、」へ直す。`entitySettingsRevision` の doc の「色・表示は」を「色・明るさ・彩度・表示は」へ直す。

- [ ] **Step 6: Inspector にブレンドのドロップダウンと彩度を出す**

`PngPlacementInspector.cs`:

定数とラベルを次にする（`BLEND_TAB_WIDTH` と `TAB_MARGIN`、`BlendModeLabels` の 3 要素版を置き換える）:

```csharp
        private const float COMBO_WIDTH = 100f;
        private const float FADE_ANGLE_STEP = 1f;
        private const float SATURATION_STEP = 0.01f;

        // PngDisplayType / PngBlendMode の値順に並べる
        private static readonly List<PngDisplayType> DisplayTypes =
            new List<PngDisplayType> { PngDisplayType.Board, PngDisplayType.Decal };
        private static readonly string[] DisplayTypeLabels = { "板", "デカール" };
        private static readonly List<PngBlendMode> BlendModes = new List<PngBlendMode>
        {
            PngBlendMode.Normal, PngBlendMode.Multiply, PngBlendMode.Additive, PngBlendMode.Overlay,
        };
        private static readonly string[] BlendModeLabels = { "通常", "乗算", "加算", "オーバーレイ" };
```

`DISPLAY_TYPE_COMBO_WIDTH` の参照は `COMBO_WIDTH` へ置き換える。

コンボの辞書を配置物ごとの組にまとめる（表示タイプと同じく、複数 PNG を並べる項目 Inspector に備える）:

```csharp
        /// <summary>配置物 1 枚分のコンボ。開閉状態と選択時の対象を持つ</summary>
        private class ComboBoxes
        {
            public GUIComboBox<PngDisplayType> displayType;
            public GUIComboBox<PngBlendMode> blendMode;
        }

        /// <summary>
        /// 複数の PNG を並べる呼び出し側 (タイムラインの項目 Inspector) に備えて配置物ごとに持つ
        /// </summary>
        private static readonly Dictionary<PngObjectData, ComboBoxes> ComboBoxesMap =
            new Dictionary<PngObjectData, ComboBoxes>();
```

`GetDisplayTypeComboBox` を `GetComboBoxes` に置き換える:

```csharp
        private static ComboBoxes GetComboBoxes(PngObjectData data)
        {
            ComboBoxes comboBoxes;
            if (ComboBoxesMap.TryGetValue(data, out comboBoxes))
            {
                return comboBoxes;
            }

            PruneComboBoxes();
            comboBoxes = new ComboBoxes
            {
                displayType = new GUIComboBox<PngDisplayType>
                {
                    items = DisplayTypes,
                    getName = (type, _) => DisplayTypeLabels[(int)type],
                    labelWidth = LABEL_WIDTH,
                    buttonSize = new Vector2(COMBO_WIDTH, ROW_HEIGHT),
                    contentSize = new Vector2(COMBO_WIDTH, GUIView.GetPopupHeight(DisplayTypes.Count)),
                    onSelected = (type, _) =>
                    {
                        // ポップアップを開いたまま配置物が消えた場合、削除済みの配置物へ書き込まない
                        if (type == data.displayType || !IsAlive(data))
                        {
                            return;
                        }
                        RecordPngEdit("表示タイプ");
                        pngManager.SetDisplayType(data, type);
                    },
                },
                blendMode = new GUIComboBox<PngBlendMode>
                {
                    items = BlendModes,
                    getName = (mode, _) => BlendModeLabels[(int)mode],
                    labelWidth = LABEL_WIDTH,
                    buttonSize = new Vector2(COMBO_WIDTH, ROW_HEIGHT),
                    contentSize = new Vector2(COMBO_WIDTH, GUIView.GetPopupHeight(BlendModes.Count)),
                    onSelected = (mode, _) =>
                    {
                        if (mode == data.blendMode || !IsAlive(data))
                        {
                            return;
                        }
                        RecordPngEdit("ブレンド");
                        pngManager.SetBlendMode(data, mode);
                    },
                },
            };
            ComboBoxesMap.Add(data, comboBoxes);
            return comboBoxes;
        }

        private static bool IsAlive(PngObjectData data)
        {
            return pngManager.FindByRoot(data.rootObject) == data;
        }
```

`PruneDisplayTypeComboBoxes` を `PruneComboBoxes` に改名し、辞書を `ComboBoxesMap` に替える。

`Draw` の冒頭を次にする:

```csharp
            var comboBoxes = GetComboBoxes(data);
            comboBoxes.displayType.currentIndex = (int)data.displayType;
            comboBoxes.displayType.DrawButton("表示タイプ", view);
```

`Draw` の明るさスライダーの後、`if (isDecal)` の前に、ブレンドと彩度の共通欄を足す:

```csharp
            comboBoxes.blendMode.currentIndex = (int)data.blendMode;
            comboBoxes.blendMode.DrawButton("ブレンド", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "彩度",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = PngPlacementManager.MinSaturation,
                max = PngPlacementManager.MaxSaturation,
                step = SATURATION_STEP,
                defaultValue = PngPlacementManager.DefaultSaturation,
                value = data.saturation,
                onChanged = value =>
                {
                    RecordPngEdit("彩度");
                    pngManager.SetSaturation(data, value);
                },
            });
```

`DrawDecalRows` からブレンドのタブ（`view.BeginHorizontal()` 〜 `view.EndLayout()` の塊）を削除する。クラスの doc コメントに「ブレンド方式と彩度は板・デカール共通」を足す。

- [ ] **Step 7: ビルドとテスト**

BUILD を実行し、続けて `TEST Png` を実行する。期待する結果: 両構成 0 エラー、PASS。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
grep -rn "GetShader()\|GetDecalShader\|ApplyDecalBlendMode\|DISPLAY_TYPE_COMBO_WIDTH\|BLEND_TAB_WIDTH\|TAB_MARGIN\|DisplayTypeComboBoxes" --include=*.cs COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs
```

期待する結果: 出力なし。

- [ ] **Step 8: コミット**

commit スキルでコミットする（例: `feat(png): PNG配置にオーバーレイ合成と彩度を追加する`）。

---

### Task 4: 彩度をプリセット・Undo・タイムラインのキーへ保存する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPngObject.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PngPlacementTimelineLayer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/PngSaturationKeyTests.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetPngDecalTests.cs`

**Interfaces:**
- Consumes: `PngObjectData.saturation`、`SetSaturation`、`DefaultSaturation` / `MinSaturation` / `MaxSaturation`（Task 3）
- Produces:
  - `ScenePresetPngObject.saturation`（属性 `saturation`、既定 1。1 のときは書き出さない）
  - `TransformDataPngObject.Index.Saturation = 32`、`LegacyValueCount = 32`、`valueCount = 33`、`saturationValue` / `saturationInfo` / `saturation`（float）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/PngSaturationKeyTests.cs`:

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// PNG 配置のキーに追加した彩度 (index 32) を固定する。
    /// 旧データ (32 値) は不足分が 0 で埋まるため、補正しないとグレースケールで読まれる
    /// </summary>
    public class PngSaturationKeyTests
    {
        private static TransformDataPngObject CreateKey()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("logo");
            return trans;
        }

        [Fact]
        public void 新しいキーの彩度は1()
        {
            Assert.Equal(1f, CreateKey().saturation);
        }

        [Fact]
        public void 彩度はindex32で値数は33()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            Assert.Equal(32, (int)TransformDataPngObject.Index.Saturation);
            Assert.Equal(33, CreateKey().valueCount);
            Assert.Equal(32, TransformDataPngObject.LegacyValueCount);
        }

        [Fact]
        public void 彩度を持たない旧キーは1で読む()
        {
            var trans = CreateKey();
            trans.FromXml(new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = new float[32],
            });

            Assert.Equal(1f, trans.saturation);
        }

        [Fact]
        public void 彩度を持つキーは保存値で読む()
        {
            var values = new float[33];
            values[32] = 0.25f;
            var trans = CreateKey();
            trans.FromXml(new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = values,
            });

            Assert.Equal(0.25f, trans.saturation);
        }

        [Fact]
        public void 彩度は往復で保たれる()
        {
            var trans = CreateKey();
            trans.saturation = 1.5f;

            var restored = CreateKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(1.5f, restored.saturation);
        }
    }
}
```

`ScenePresetPngDecalTests.cs` の末尾に足す:

```csharp
        [Fact]
        public void 彩度は1以外のときだけ書き出す()
        {
            var plain = Serialize(new ScenePresetPngObject { source = "config", relativePath = "logo.png" });
            var tinted = new ScenePresetPngObject { source = "config", relativePath = "logo.png", saturation = 0.5f };

            var restored = Deserialize(Serialize(WithPng(tinted)));

            Assert.DoesNotContain("saturation", plain);
            Assert.Contains("saturation=\"0.5\"", Serialize(tinted));
            Assert.Equal(0.5f, Assert.Single(restored.pngPlacement.objects).saturation);
        }

        [Fact]
        public void 彩度の属性が無い旧データは1で読む()
        {
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetData><pngPlacement>" +
                "<png source=\"config\" relativePath=\"logo.png\" />" +
                "</pngPlacement></ScenePresetData>";

            var png = Assert.Single(Deserialize(xml).pngPlacement.objects);

            Assert.Equal(1f, png.saturation);
        }
```

- [ ] **Step 2: 失敗を確認する**

BUILD を実行してから `TEST PngSaturationKeyTests` と `TEST ScenePresetPngDecalTests` を実行する。期待する結果: `Index.Saturation` / `saturation` 未定義でコンパイルエラー。

- [ ] **Step 3: キーに彩度を足す**

`TransformDataPngObject.cs`:
- `Index` の末尾を `FixedPosZ = 31,` にし、`Saturation = 32` を足す。直前にコメント `// 以降は SE 独自。MTE は値数 32 までしか読まない` を置く
- `valueCount` を次にする:

```csharp
        /// <summary>彩度 (SE 独自) を持たない MTE・旧 SE の値数</summary>
        public const int LegacyValueCount = 32;

        public override int valueCount => (int)Index.Saturation + 1;
```

- `CustomValueInfoMap` の末尾（`fixedposz` の後）に:

```csharp
            {
                "saturation", new CustomValueInfo
                {
                    index = (int)Index.Saturation,
                    name = "彩度",
                    min = SE.PngPlacementManager.MinSaturation,
                    max = SE.PngPlacementManager.MaxSaturation,
                    step = 0.01f,
                    defaultValue = SE.PngPlacementManager.DefaultSaturation,
                }
            },
```

  `namespace COM3D2.MotionTimelineEditor.Plugin {` の直後に `using SE = SceneEditor.Plugin;` を足す（`PngPlacementTimelineLayer.cs` と同じく namespace 内に置く相対の別名）。ファイル冒頭コメントを「MTE_PngPlacement からの逐語移植。namespace を SE の Timeline 共通名前空間へ変更し、SE 独自の彩度を末尾に足している」へ直す

- アクセサを足す（`apngisfixedspeedValue` / `apngisfixedspeedInfo` / `apngisfixedspeed` の後にそれぞれ）:

```csharp
        public ValueData saturationValue => values[(int)Index.Saturation];
```

```csharp
        public CustomValueInfo saturationInfo => CustomValueInfoMap["saturation"];
```

```csharp
        public float saturation
        {
            get => saturationValue.value;
            set => saturationValue.value = value;
        }
```

- クラス末尾に `FromXml` の補正を足す:

```csharp
        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            // 彩度を持たない旧データ (MTE・旧 SE) は不足分が 0 で埋まり、グレースケールになる。既定値へ補正する
            if (xml.values == null || xml.values.Length <= LegacyValueCount)
            {
                saturation = SE.PngPlacementManager.DefaultSaturation;
            }
        }
```

- [ ] **Step 4: タイムラインレイヤーで適用・補間・登録する**

`PngPlacementTimelineLayer.cs`:
- `ApplyPngObjectInit` の `SetColor` の後に `sePngManager.SetSaturation(data, start.saturation);`
- `ApplyPngObjectUpdate` の色の `if` の後に:

```csharp
            if (start.saturation != end.saturation)
            {
                // 色・明るさと同じく線形補間する
                sePngManager.SetSaturation(data,
                    Mathf.Lerp(start.saturation, end.saturation, t));
            }
```

- `UpdateFrame` の `trans.brightness = ...` の後に `trans.saturation = data.saturation;`
- クラスの doc コメントに「彩度は SE 独自の値 (index 32)」を足す

- [ ] **Step 5: プリセットと Undo に足す**

`ScenePresetData.cs` の `ScenePresetPngObject` の `brightness` の後に:

```csharp
        [XmlAttribute]
        public float saturation = PngPlacementManager.DefaultSaturation;

        // 彩度は板・デカール共通。既定値 (1) のときは書き出さず、旧プリセットと同じ内容に保つ
        public bool ShouldSerializesaturation()
        {
            return !Mathf.Approximately(saturation, PngPlacementManager.DefaultSaturation);
        }
```

（`ScenePresetData.cs` に `using UnityEngine;` が無ければ足す）

v36 のコメントを「表示タイプ (displayType)、ブレンド方式 (blendMode)、彩度 (saturation)、デカール設定 (...) を追加。表示タイプとデカール設定は板では、ブレンド方式は通常、彩度は 1 のときは書き出さない」へ直す。

`PngPlacementSnapshot.cs`:
- `CaptureState`: `brightness = data.brightness,` の後に `saturation = data.saturation,`
- `ApplyState`: `manager.SetColor(...)` の後に `manager.SetSaturation(data, objState.saturation);`
- `Approximately`: `|| !Mathf.Approximately(a.brightness, b.brightness)` の後に `|| !Mathf.Approximately(a.saturation, b.saturation)`

- [ ] **Step 6: ビルドとテスト**

BUILD を実行し、続けて `TEST` をフィルターなしで実行する。期待する結果: 両構成 0 エラー、全テスト PASS（`RotationMigrationFixtureTests` の PngObject 32 値は XML レベルの検証なので影響しない）。

- [ ] **Step 7: コミット**

commit スキルでコミットする（例: `feat(png): 彩度をプリセットとタイムラインのキーへ保存する`）。

---

### Task 5: 実機検証と互換ドキュメント

**Files:**
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の PNG 実体の行）

**Interfaces:**
- Consumes: Task 1〜4 のすべて
- Produces: なし

- [ ] **Step 1: 互換リストを直す**

`W:\COM3D2_5\work\CLAUDE.md` の PNG 実体の行を次に置き換える:

```markdown
- PNG 実体（`<PngObjects>`）の表示タイプとデカール設定（`DisplayType` / `DecalFadeAngle` / `DecalProjectOnMaids`）は SE 独自。板では書き出さない。ブレンド方式（`BlendMode`、3 = オーバーレイ）も SE 独自で、板・デカール共通のため通常以外なら板でも書き出す。MTE は要素を読み飛ばすため、デカールは板・通常ブレンドとして表示される。読込時は `RenderQueue` も実体へ戻す（0 以下は適用しない）
- PNG 配置キーの彩度（index 32、値数 33）は SE 独自。32 値以下の旧キーは `TransformDataPngObject.FromXml` が彩度 1 に補正する。MTE は 32 値までしか読まないため彩度は失われる
```

- [ ] **Step 2: 実機へ反映する**

restart-verify スキルの手順に従う。

1. ゲームを終了する
2. BUILD の成果物（COM3D25 構成の DLL）と `se_bundle` の反映先を確認し、`W:\COM3D2_5\Sybaris\UnityInjector\` へ反映する
3. ゲームを再起動し、セーブをロードする
4. デイリー画面でギアメニューから SceneEditor を有効にし、床と壁のある背景を選ぶ（撮影モードは使わない）

- [ ] **Step 3: 動作確認（devbridge の `screenshot` / `eval_csharp`）**

UI 操作が必要な項目はユーザーに依頼し、画面は `screenshot` で確認する。各項目の結果（OK / NG と画像）を記録する。

1. 変更前に置いていた板（通常・彩度 1）の見た目が変わらない。透過画像の縁、裏から見たとき、表示順の前後関係を含む
2. 板で「ブレンド」を乗算・加算・オーバーレイに切り替えると、それぞれ見た目が変わる。オーバーレイでは背景の明暗が残る
3. 「彩度」を 0 にすると灰色、2 にすると色が濃くなる。板・デカールの両方で確かめる
4. オーバーレイと通常を行き来しても、表示順（`eval_csharp` で `material.renderQueue`）、透過画像の ZWrite、両面描画が保たれる
5. デカールでオーバーレイ・乗算・加算・通常と彩度が効く。オーバーレイのデカールが床の明暗を残して色付く
6. Undo / Redo でブレンドと彩度が戻り、また進む
7. シーンプリセットを保存し、PNG を消してから読み込むと、ブレンドと彩度が復元される
8. タイムラインで彩度の違う 2 キーを打ち、再生すると彩度が滑らかに変わる。保存・再読込後も同じ
9. 変更前に保存した PNG 配置入りのタイムラインを読み込むと、彩度 1（灰色にならない）で表示される
10. 板・通常・彩度 1 だけのシーンでプリセットを保存すると、XML に `blendMode` も `saturation` も出ない（`eval_csharp` でファイルを読み、`Contains` で確かめる）
11. 性能: 次の 3 状態で `UnityEngine.Time.smoothDeltaTime` を比べ、目立つ差が無いこと。差が大きい場合は結果をユーザーに報告し、板も名前付き GrabPass にするか相談する
    - オーバーレイの PNG なし
    - オーバーレイのデカール 1 枚
    - オーバーレイの板 5 枚（名前なし GrabPass で板の枚数だけ画面コピーが走る）

NG があった場合は、superpowers:systematic-debugging で原因を調べて直す。

- [ ] **Step 4: ドキュメントの更新を確認する**

`W:\COM3D2_5\work\CLAUDE.md` はリポジトリの外なので、このリポジトリのコミットには含めない。追記したことを最終報告に書く。

---

## 完了後

1. code-review スキルでレビューし、指摘を取り込む（CLAUDE.md の必須工程）
2. commit スキルで残りの変更をコミットする
3. 利用者向けドキュメント（docs-site）と CHANGELOG の更新は、release-prep で行う（本計画の範囲外）

## レビュー却下メモ

- **「プリセットの `blendMode` は enum 型の XmlAttribute で、未知の文字列だと XmlSerializer が例外を投げる。int 化とガードに揃えるべき」** — 却下
  - 未知の値が入るのは手編集のときだけ。`displayType` など既存の enum 属性と同じ扱いで、本計画の範囲外
  - 属性名が新しいので、旧 SE が新しいプリセットを読んでも属性ごと無視され、例外にはならない
- **「作業中にリリースが割り込むと、未リリース前提の改名が崩れる。コミット前に CHANGELOG とタグを再確認すべき」** — 却下。運用上の話で、計画の不備ではない
- **「`ComboBoxes` クラスのコメントが将来の呼び出し側を根拠にしている」** — 却下。項目 Inspector は既に複数 PNG を並べて描いており（`PngPlacementItemInspector`）、将来の話ではない
