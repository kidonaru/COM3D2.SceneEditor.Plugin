# 背景のみライトでキャラの影を落とす Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 照射対象を「背景のみ」にした追加ライトでも、キャラ（メイド・男）の影を背景へ落とせるようにする。キャラ自身はそのライトに照らされないままにする。

**Architecture:**
- 設定の真の値は、照射対象と同じくライトの `cullingMask` に持たせる。空きレイヤーを 1 つ「影用レイヤー」として選び、そのビット（影ビット）を立てるかどうかで「キャラの影」の ON/OFF を表す
  - 選ぶのは `CharacterShadowLayer`、マスクの組み立てと逆引きは `LightTarget` の役目
- 影は、元の `SkinnedMeshRenderer` とメッシュ・骨・マテリアルを共有する `ShadowsOnly` の複製（影用レイヤーに置く）で戻す。複製の作成・追従・破棄は `CharacterShadowProxyManager` が受け持つ
  - 条件を満たすライトが 1 灯でもある間だけ複製を持つ
  - カメラごとの表示判定は `Camera.onPreCull` で行う
- ライト定義（タイムライン XML・シーンプリセット・コピー/貼り付け・Undo）は、輪郭や影の種類と同じ流れで `characterShadow` を運ぶ

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Unity 5.6 / 2022 の API、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-05-background-light-character-shadow-design.md`

## Global Constraints

- 対象のライト: SceneEditor の追加ライト（index > 0）。メインライト（index 0）の `cullingMask` には触らない
- 影を落とすキャラ: Renderer のレイヤーが `LightTarget.CharacterMask`（Charactor / Face / Man）に入っているもの
- 影用レイヤーは名前の付いていないレイヤーから選ぶ。条件は、メインカメラの `cullingMask` に入っていて、`CharacterMask` に入っていないこと。番号は固定で埋め込まない
  - 該当するレイヤーが無いときは機能を無効にし、エラーログを 1 回だけ出す
- `cullingMask` の組み立て（spec の表のとおり）:

  | 照射対象 | キャラの影 OFF | キャラの影 ON |
  |---|---|---|
  | 全て | `~影ビット` | `-1` |
  | キャラのみ | `CharacterMask` | `CharacterMask \| 影ビット` |
  | 背景のみ | `~CharacterMask & ~影ビット` | `~CharacterMask` |

- 照射対象が「背景のみ」以外のときも影ビットは残す
- 新しく作る追加ライトの既定値は ON。既存データ（要素・属性が無い）は OFF として読む
- タイムライン XML: ライト定義に `<CharacterShadow>`（bool）を足す。true のときだけ書き出す。`TimelineData.CurrentVersion` は上げない
- シーンプリセット: `ScenePresetAdditionalLight` に属性 `characterShadow` を足す。true のときだけ書き出す。`ScenePresetData.CurrentVersion` を 38 から 39 へ上げる
- UI: トグルの表示名は「キャラの影」。影が「なし」以外で、照射対象が「背景のみ」のときだけ出す
- コードのコメントとエラーログは日本語で書く
- **2 構成のビルド**: `COM3D2`（.NET 3.5）と `COM3D25`（.NET 4.7.1）の両方を通す
  - .NET 3.5 では入力 5 個以上の `Func<>` / `Action<>` が使えない
- `debug.bat` / `deploy.bat` は実行しない。ビルドは MSBuild を直接叩く
- テストでは Unity のネイティブ呼び出し（`new GameObject`、`LayerMask.NameToLayer`、`LayerMask.LayerToName` など）を使わない。`LightTarget.CharacterMask` のような、内部でネイティブを呼ぶプロパティもテストから触らない（注入版を使う）
- プラグイン本体の csproj は `<Compile Include>` を明示列挙している。新しいファイルは必ず追記する（テスト側は SDK 形式なので不要）
- `MTEUtils/` はサブモジュール。今回は変更しない

## 実機で確かめた前提（2026-10-05、計画作成時）

spec の「要確認事項」のうち 3・4 は、計画作成時に devbridge で確かめた（SceneDaily、メイド 1 人）。

- **呼び出し順**: `Camera.onPreCull`（静的コールバック）は、カメラのコンポーネントの `OnPreCull` より**後**に呼ばれる
  - メインカメラの `ViewCullingFilter.hideMaid` を ON にすると、`Camera.onPreCull` の中ではメイドの Renderer がすでに `enabled == false` だった（OFF の対照では true）
  - PostEffects の `MaidHideEffect` を有効にすると、`Camera.onPreCull` の中で読んだカメラの `cullingMask` からは Charactor / Face（bit 10・11）がすでに外れていた
  - したがって複製の表示判定は `Camera.onPreCull` で「元の Renderer の enabled」と「カメラの今の `cullingMask`」を読めばよい
- **男のメッシュ構成**: 非アクティブの男 0（body ロード済み）は `SkinnedMeshRenderer` 3 個（`ManHead000` / `karada` / `moza`）。レイヤーは Charactor、`rootBone` は空、`shadowCastingMode` は On、`blendShapeCount` は 0。メイドと同じ扱いでよい
- **レイヤー**: 名前の無いレイヤーは 3・6・7 で、どれもメインカメラの `cullingMask` に入っている。SceneView カメラは 8（NGUI）以外の全レイヤーを描く
  - メインカメラは Man（12）を描いていなかった

spec の要確認事項 1・2（新規ライトの既定値、トグルの表示条件）は、spec 本文の推奨（既定 ON、「背景のみ」のときだけ表示）を採る。

## Review Focus

- **旧データの読込で見た目が変わらない**: 既存の XML・プリセットの「背景のみ」のライトは、要素・属性が無いので OFF として読まれ、キャラの影が出ないこと
  - Task 2・Task 3 の「要素の無い旧 XML は OFF」「属性の無い旧プリセットは OFF」のテストで固定する。読込経路で影ビットが落ちることは Task 7 の実機検証で確かめる
- **照射対象の切り替えで設定が消えない**: キーで「背景のみ → 全て → 背景のみ」と切り替えても、影ビットが保たれること
  - Task 1 の「モードを切り替えても影の設定を引き継ぐ」テストで固定する。書き込み箇所はすべて今の影ビットを引き継ぐ
- **影用レイヤーが無い環境**: 影ビットが 0 のとき、ON/OFF のマスクが同じになり、`HasCharacterShadow` は false、複製の条件も満たさないこと（例外や誤動作を出さない）
  - Task 1 と Task 4 の「影ビットが 0」のテストで固定する
- **非表示への追従**: GameView の「メイド非表示」、PostEffects の「メイド非表示」、非アクティブの男で、影が残らないこと
  - Task 4 の `ShouldHideForCamera` のテストで判定を固定する。順序は上の実機確認で確かめ済み。効き目は Task 7 で確かめる
- **影用レイヤーが未解決のときの読込**: メインカメラが取れる前に読み込んだり、空きレイヤーが無い環境で読み込んだりすると、影ビットを立てられない。そのとき保存済みの ON を、定義の同期で OFF に上書きしないこと
  - Task 2 の定義同期は、影用レイヤーが無い間は `characterShadow` を同期しない。ライト一覧の作り直し・Undo・プリセットの記録まではかばわない（空きレイヤーが無い環境ではこの機能自体が働かないため）
- **男が見えているシーン**: 計画作成時の確認は非アクティブの男だけ。男が表示されるシーンでも、元の Renderer のレイヤーをカメラが描いていて、影が落ちること
  - Task 7 の実機検証で、男が実際に見えるシーン（夜伽など）で確かめ、レイヤーとカメラの `cullingMask` を記録する
- **着替え中の破棄済み参照**: 着替え中に、破棄済みの骨やメッシュを指す複製を残さず、例外も出さないこと
  - 着替え中（`IsAllProcPropBusy`）はそのキャラの複製を持たない。Task 7 の実機検証で確かめる

## ファイル構成

以下、`source/COM3D2.SceneEditor.Plugin/` 配下のパスは `<P>/`、`source/COM3D2.SceneEditor.Plugin.Tests/` 配下は `<T>/` と略す。

| ファイル | 役割 |
|---|---|
| Create `<P>/CharacterShadowLayer.cs` | 影用レイヤーの選択（純粋ロジック）と、起動後の初回だけの解決・キャッシュ |
| Modify `<P>/LightTarget.cs` | 影ビット込みの `cullingMask` の組み立て・逆引き・影ビットの読み書き |
| Modify `<P>/Timeline/TimelineLayer/LightTimelineLayer.cs` | キーの適用で影ビットを引き継ぐ |
| Modify `<P>/LightRowDrawer.cs` | 照射対象のコンボで影ビットを引き継ぐ（Task 1）。「キャラの影」トグル（Task 6） |
| Modify `<P>/LightClipboard.cs` | 影ビットの引き継ぎ（Task 1）、`characterShadow` のコピー/貼り付け（Task 3） |
| Modify `<P>/Manager/History/LightSnapshot.cs` | 影ビットの引き継ぎ（Task 1）、`characterShadow` の記録・復元・比較（Task 3） |
| Modify `<P>/Timeline/TimelineXml.cs` | `TimelineLightXml.characterShadow`（`<CharacterShadow>`） |
| Modify `<P>/Timeline/TimelineData.cs` | `TimelineLightData.characterShadow` |
| Modify `<P>/Timeline/StudioLightStat.cs` | `characterShadow` の写し |
| Modify `<P>/Timeline/Manager/StudioLightManager.cs` | 定義の同期と、読込時の `cullingMask` への反映 |
| Modify `<P>/ScenePresetData.cs` | `ScenePresetAdditionalLight.characterShadow`、版 39 |
| Modify `<P>/Manager/StudioLightManager.cs` | 新規ライトの `cullingMask` を「全て・ON」に明示する |
| Create `<P>/Manager/CharacterShadowProxyRules.cs` | 複製の条件判定（純粋ロジック） |
| Create `<P>/Manager/CharacterShadowProxyManager.cs` | 複製の作成・追従・破棄、カメラごとの表示判定 |
| Modify `<P>/COM3D2.SceneEditor.Plugin.cs` | マネージャの登録 |
| Modify `<P>/COM3D2.SceneEditor.Plugin.csproj` | Compile 追記 |
| Modify `<T>/LightTargetTests.cs` | 新しいシグネチャへの追従と、影ビットのテスト |
| Create `<T>/CharacterShadowLayerTests.cs` | レイヤー選択のテスト |
| Create `<T>/LightCharacterShadowXmlTests.cs` | タイムライン XML のテスト |
| Create `<T>/ScenePresetLightCharacterShadowTests.cs` | シーンプリセットのテスト（版 39 を含む） |
| Modify `<T>/ScenePresetEffectsTests.cs`、`<T>/ScenePresetMaidScaleTests.cs` | 版の期待値 38 を外す・直す |
| Create `<T>/CharacterShadowProxyRulesTests.cs` | 複製の条件判定のテスト |
| Modify `docs-site/guide/staging.md`、`docs-site/timeline/compatibility.md` | 利用者向けの説明 |
| Modify `W:\COM3D2_5\work\CLAUDE.md` | 「タイムライン XML の互換方向」へ 1 項目追加（git 管理外なのでコミットしない） |

## ビルド・テストのコマンド

Git Bash から実行する。順番は「COM3D2 → COM3D25 → dotnet test」。COM3D2 構成のビルドが `bin/Debug/COM3D25/` を消すため、この順でないとテストが古い DLL を見る。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

以下では、これを「**ビルド＆テスト**」と呼ぶ。特定のテストだけ回すときは、末尾を `dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightTarget"` のように置き換える。

テストはビルド済みのプラグイン DLL を参照するため、「失敗するテストを書く」段階では、未定義のメンバーによるコンパイルエラーが「失敗」にあたる。

---

### Task 1: 影用レイヤーの選択と、影ビット込みの cullingMask

**Files:**
- Create: `<P>/CharacterShadowLayer.cs`
- Modify: `<P>/LightTarget.cs`（全面的に書き換え）
- Modify: `<P>/Timeline/TimelineLayer/LightTimelineLayer.cs:150`
- Modify: `<P>/LightRowDrawer.cs:407`
- Modify: `<P>/LightClipboard.cs:101`
- Modify: `<P>/Manager/History/LightSnapshot.cs:153`
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="LightTarget.cs" />` の次の行）
- Modify: `<T>/LightTargetTests.cs`
- Create: `<T>/CharacterShadowLayerTests.cs`

**Interfaces:**
- Produces:
  - `COM3D2.SceneEditor.Plugin.CharacterShadowLayer`（static）
    - `const int None = -1`
    - `static int layer`（未解決・該当なしなら `None`）
    - `static int mask`（`layer` が `None` なら 0、それ以外は `1 << layer`）
    - `static bool isAvailable`
    - `static int Select(Func<int, string> layerToName, int cameraMask, int characterMask)`（純粋。該当なしは `None`）
  - `COM3D2.SceneEditor.Plugin.LightTarget`（既存の static クラスを拡張）
    - `static int ToCullingMask(LightTargetMode mode, bool characterShadow)`
    - `static int ToCullingMask(LightTargetMode mode, bool characterShadow, int characterMask, int shadowMask)`（テスト用）
    - `static LightTargetMode FromCullingMask(int cullingMask)`
    - `static LightTargetMode FromCullingMask(int cullingMask, int characterMask, int shadowMask)`（テスト用）
    - `static bool HasCharacterShadow(int cullingMask)` / `HasCharacterShadow(int cullingMask, int shadowMask)`
    - `static int WithCharacterShadow(int cullingMask, bool characterShadow)` / `WithCharacterShadow(int cullingMask, bool characterShadow, int shadowMask)`
    - 削除: `ToCullingMask(LightTargetMode mode)`、`ToCullingMask(LightTargetMode mode, int characterMask)`、`FromCullingMask(int cullingMask, int characterMask)`。古い呼び出しをコンパイルエラーで洗い出すため、残さない

- [ ] **Step 1: 失敗するテストを書く（レイヤー選択）**

`<T>/CharacterShadowLayerTests.cs`:

```csharp
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>影用レイヤーの選択を固定する。名前の無い、カメラに映る、キャラ用でないレイヤーの最小番号を選ぶ</summary>
    public class CharacterShadowLayerTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        private static string[] CreateNames()
        {
            var names = new string[32];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = "Layer" + i;
            }
            return names;
        }

        private static int Select(string[] names, int cameraMask, int characterMask)
        {
            return CharacterShadowLayer.Select(layer => names[layer], cameraMask, characterMask);
        }

        [Fact]
        public void 名前の無いレイヤーのうち最小の番号を選ぶ()
        {
            var names = CreateNames();
            names[3] = "";
            names[6] = "";
            names[7] = null;

            Assert.Equal(3, Select(names, -1, CharacterMask));
        }

        [Fact]
        public void カメラに映らないレイヤーは選ばない()
        {
            var names = CreateNames();
            names[3] = "";
            names[6] = "";

            Assert.Equal(6, Select(names, ~(1 << 3), CharacterMask));
        }

        [Fact]
        public void キャラ用のレイヤーは選ばない()
        {
            var names = CreateNames();
            names[10] = "";
            names[20] = "";

            Assert.Equal(20, Select(names, -1, CharacterMask));
        }

        [Fact]
        public void 該当が無ければNone()
        {
            var names = CreateNames();
            names[5] = "";

            Assert.Equal(CharacterShadowLayer.None, Select(names, ~(1 << 5), CharacterMask));
            Assert.Equal(CharacterShadowLayer.None, Select(CreateNames(), -1, CharacterMask));
        }
    }
}
```

- [ ] **Step 2: 失敗するテストを書く（cullingMask）**

`<T>/LightTargetTests.cs` の `全て_は全レイヤー` から `想定外のマスクは全て扱い` までの 5 テストを、次で置き換える。クラス先頭の `CharacterMask` 定数はそのまま使う。`範囲外の整数は全てへ丸める` 以降（`ClampMode` と `TransformDataLight` のテスト）もそのまま残す。

```csharp
        // 影用レイヤー (実機では名前の無い 3 が選ばれる)
        private const int ShadowMask = 1 << 3;

        [Theory]
        [InlineData(LightTargetMode.All, false, ~ShadowMask)]
        [InlineData(LightTargetMode.All, true, -1)]
        [InlineData(LightTargetMode.Character, false, CharacterMask)]
        [InlineData(LightTargetMode.Character, true, CharacterMask | ShadowMask)]
        [InlineData(LightTargetMode.Background, false, ~CharacterMask & ~ShadowMask)]
        [InlineData(LightTargetMode.Background, true, ~CharacterMask)]
        public void モードと影の設定からマスクを組み立てる(LightTargetMode mode, bool characterShadow, int expected)
        {
            Assert.Equal(expected,
                LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false)]
        [InlineData(LightTargetMode.All, true)]
        [InlineData(LightTargetMode.Character, false)]
        [InlineData(LightTargetMode.Character, true)]
        [InlineData(LightTargetMode.Background, false)]
        [InlineData(LightTargetMode.Background, true)]
        public void 往復変換で元のモードと影の設定に戻る(LightTargetMode mode, bool characterShadow)
        {
            var mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);

            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask, ShadowMask));
            Assert.Equal(characterShadow, LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Fact]
        public void モードを切り替えても影の設定を引き継ぐ()
        {
            // 背景のみ (ON) → 全て → 背景のみ と、書き込みのたびに今の影ビットを引き継ぐ
            var mask = LightTarget.ToCullingMask(LightTargetMode.Background, true, CharacterMask, ShadowMask);
            foreach (var mode in new[] { LightTargetMode.All, LightTargetMode.Character, LightTargetMode.Background })
            {
                var characterShadow = LightTarget.HasCharacterShadow(mask, ShadowMask);
                mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);
            }

            Assert.Equal(LightTargetMode.Background, LightTarget.FromCullingMask(mask, CharacterMask, ShadowMask));
            Assert.True(LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットだけを書き換えてもモードは変わらない(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, ShadowMask);

            var on = LightTarget.WithCharacterShadow(off, true, ShadowMask);

            Assert.Equal(LightTarget.ToCullingMask(mode, true, CharacterMask, ShadowMask), on);
            Assert.Equal(off, LightTarget.WithCharacterShadow(on, false, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットが0なら影の設定は無視され従来と同じマスクになる(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, 0);
            var on = LightTarget.ToCullingMask(mode, true, CharacterMask, 0);

            Assert.Equal(off, on);
            Assert.False(LightTarget.HasCharacterShadow(on, 0));
            Assert.Equal(mode, LightTarget.FromCullingMask(on, CharacterMask, 0));
        }

        [Fact]
        public void 影ビットが0のマスクは従来の値と同じ()
        {
            Assert.Equal(-1, LightTarget.ToCullingMask(LightTargetMode.All, false, CharacterMask, 0));
            Assert.Equal(CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Character, false, CharacterMask, 0));
            Assert.Equal(~CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Background, false, CharacterMask, 0));
        }

        [Fact]
        public void 想定外のマスクは全て扱い()
        {
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(1 << 10, CharacterMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(0, CharacterMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(ShadowMask, CharacterMask, ShadowMask));
        }
```

- [ ] **Step 3: テストが失敗することを確かめる**

Run: ビルド＆テスト（`--filter "FullyQualifiedName~LightTarget|FullyQualifiedName~CharacterShadowLayer"`）
Expected: テストのコンパイルが `CharacterShadowLayer` と `ToCullingMask` の 4 引数版が無いことで失敗する

- [ ] **Step 4: `CharacterShadowLayer` を書く**

`<P>/CharacterShadowLayer.cs`:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「背景のみ」のライトでキャラの影を落とすための影用レイヤー。
    /// ライトの cullingMask から外したオブジェクトはそのライトの影も落とさないため、
    /// 影専用の複製をこのレイヤーに置き、ライトの cullingMask にこのビットを入れるかどうかで影の有無を切り替える。
    /// 複製の影はカメラの cullingMask にも入っていないと落ちないため、メインカメラが描くレイヤーから選ぶ
    /// </summary>
    public static class CharacterShadowLayer
    {
        public const int None = -1;

        private static bool _resolved = false;
        private static int _layer = None;

        /// <summary>影用レイヤーの番号。メインカメラが取れるまでは未解決のまま None を返し、次の呼び出しで再試行する</summary>
        public static int layer
        {
            get
            {
                Resolve();
                return _layer;
            }
        }

        public static int mask => layer == None ? 0 : 1 << layer;

        public static bool isAvailable => layer != None;

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            var gameMain = GameMain.Instance;
            var cameraMain = gameMain != null ? gameMain.MainCamera : null;
            var camera = cameraMain != null ? cameraMain.camera : null;
            if (camera == null)
            {
                return;
            }

            _resolved = true;
            _layer = Select(LayerMask.LayerToName, camera.cullingMask, LightTarget.CharacterMask);
            if (_layer == None)
            {
                MTEUtils.LogError("キャラの影に使える空きレイヤーが見つからないため、「背景のみ」のライトでキャラの影を落とす機能を無効にします");
            }
            else
            {
                MTEUtils.LogDebug("キャラの影の複製にレイヤー {0} を使います", _layer);
            }
        }

        /// <summary>
        /// 名前の無いレイヤーのうち、カメラに映り、キャラ用でないものの最小番号を返す。該当が無ければ None。
        /// SceneEditor・PostEffects ともに名前の無いレイヤーは使っていない
        /// </summary>
        public static int Select(Func<int, string> layerToName, int cameraMask, int characterMask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                var bit = 1 << layer;
                if ((cameraMask & bit) == 0 || (characterMask & bit) != 0)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(layerToName(layer)))
                {
                    return layer;
                }
            }
            return None;
        }
    }
}
```

- [ ] **Step 5: `LightTarget` を書き換える**

`<P>/LightTarget.cs` の `LightTarget` クラスのうち、`CharacterMask` プロパティより後ろ（`ToCullingMask` / `FromCullingMask` の 4 メソッド）を次で置き換える。`ClampMode` はそのまま残す。クラスの `<summary>` の末尾に「影用レイヤーのビット (CharacterShadowLayer) で、背景のみのライトでもキャラの影を落とすかを表す」の一文を足す。

```csharp
        /// <summary>照射対象とキャラの影の設定から cullingMask を組み立てる</summary>
        public static int ToCullingMask(LightTargetMode mode, bool characterShadow)
            => ToCullingMask(mode, characterShadow, CharacterMask, CharacterShadowLayer.mask);

        public static LightTargetMode FromCullingMask(int cullingMask)
            => FromCullingMask(cullingMask, CharacterMask, CharacterShadowLayer.mask);

        public static bool HasCharacterShadow(int cullingMask)
            => HasCharacterShadow(cullingMask, CharacterShadowLayer.mask);

        /// <summary>照射対象は変えずに、キャラの影の設定だけを書き換える</summary>
        public static int WithCharacterShadow(int cullingMask, bool characterShadow)
            => WithCharacterShadow(cullingMask, characterShadow, CharacterShadowLayer.mask);

        /// <summary>
        /// characterMask と影ビットを注入する版（テスト用）。
        /// 影ビットは照射対象と独立に持つ。照射対象はタイムラインのキーで切り替わるため、
        /// 「背景のみ」以外でも残しておかないと、背景のみへ戻したときに設定が失われる
        /// </summary>
        public static int ToCullingMask(LightTargetMode mode, bool characterShadow, int characterMask, int shadowMask)
        {
            int cullingMask;
            switch (mode)
            {
                case LightTargetMode.Character:
                    cullingMask = characterMask;
                    break;
                case LightTargetMode.Background:
                    cullingMask = ~characterMask;
                    break;
                default:
                    cullingMask = -1;
                    break;
            }
            return WithCharacterShadow(cullingMask, characterShadow, shadowMask);
        }

        /// <summary>
        /// 実マスクからモードへ逆引きする。影ビットを立てた状態にそろえてから比べる。
        /// 本プラグイン以外が書いたマスクは判別できないため「全て」として扱う
        /// </summary>
        public static LightTargetMode FromCullingMask(int cullingMask, int characterMask, int shadowMask)
        {
            var normalized = cullingMask | shadowMask;
            if (normalized == (characterMask | shadowMask))
            {
                return LightTargetMode.Character;
            }
            // 影用レイヤーはキャラ用でないレイヤーから選ぶので、~characterMask は影ビットを含む
            if (normalized == ~characterMask)
            {
                return LightTargetMode.Background;
            }
            return LightTargetMode.All;
        }

        /// <summary>影ビットが立っているか。影用レイヤーが無い (shadowMask == 0) ときは常に false</summary>
        public static bool HasCharacterShadow(int cullingMask, int shadowMask)
        {
            return shadowMask != 0 && (cullingMask & shadowMask) == shadowMask;
        }

        public static int WithCharacterShadow(int cullingMask, bool characterShadow, int shadowMask)
        {
            return characterShadow ? cullingMask | shadowMask : cullingMask & ~shadowMask;
        }
```

- [ ] **Step 6: 書き込み箇所で今の影ビットを引き継ぐ**

1 引数の `ToCullingMask` を消したので、4 箇所がコンパイルエラーになる。どれも、書き込む前の `light.cullingMask` から影ビットを引き継ぐ。

`<P>/Timeline/TimelineLayer/LightTimelineLayer.cs`（`ApplyMotionInit` の中）:

```csharp
            // 照射対象は補間しない。メインライト (index 0) はゲーム側の恒久オブジェクトのため照射対象・輪郭を触らない。
            // キャラの影はライト定義の値なので、キーの適用では今の設定を引き継ぐ
            if (stat.index > 0)
            {
                light.cullingMask = LightTarget.ToCullingMask(
                    LightTarget.ClampMode(start.lightTarget), LightTarget.HasCharacterShadow(light.cullingMask));
                SceneEditor.Plugin.LightCookie.SetHardness(light, start.cookieHardness);
            }
```

`<P>/LightRowDrawer.cs`（`DrawLightTargetRow` の `onSelected`）:

```csharp
                _lightTargetComboBox.onSelected = (mode, _) =>
                {
                    RecordLightEdit("対象");
                    light.cullingMask = LightTarget.ToCullingMask(
                        mode, LightTarget.HasCharacterShadow(light.cullingMask));
                };
```

`<P>/LightClipboard.cs`（`Paste` の中。Task 3 でクリップボードの値に置き換える）:

```csharp
            light.cullingMask = LightTarget.ToCullingMask(
                _data.target, LightTarget.HasCharacterShadow(light.cullingMask));
```

`<P>/Manager/History/LightSnapshot.cs`（`ApplyLightState` の中。Task 3 で記録値に置き換える）:

```csharp
            light.cullingMask = LightTarget.ToCullingMask(
                LightTarget.ClampMode(lightState.target), LightTarget.HasCharacterShadow(light.cullingMask));
```

- [ ] **Step 7: csproj に追記する**

`<P>/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="LightTarget.cs" />` の次の行へ:

```xml
    <Compile Include="CharacterShadowLayer.cs" />
```

- [ ] **Step 8: テストが通ることを確かめる**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS。`LightTargetTests` と `CharacterShadowLayerTests` も PASS

- [ ] **Step 9: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/CharacterShadowLayer.cs source/COM3D2.SceneEditor.Plugin/LightTarget.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs \
  source/COM3D2.SceneEditor.Plugin/LightClipboard.cs source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs source/COM3D2.SceneEditor.Plugin.Tests/CharacterShadowLayerTests.cs
git commit -m "feat(light): 照射対象のマスクにキャラの影のビットを持たせる"
```

---

### Task 2: タイムラインのライト定義に「キャラの影」を保存する

**Files:**
- Modify: `<P>/Timeline/TimelineXml.cs`（`TimelineLightXml` の `ShouldSerializeshadows` の後ろ）
- Modify: `<P>/Timeline/TimelineData.cs`（`TimelineLightData`）
- Modify: `<P>/Timeline/StudioLightStat.cs`（`shadows` フィールドの後ろ、コンストラクタ、`FromStat`）
- Modify: `<P>/Timeline/Manager/StudioLightManager.cs:150-157`（定義の同期）、`:252-277`（`SetupLights`）
- Create: `<T>/LightCharacterShadowXmlTests.cs`

**Interfaces:**
- Consumes: `LightTarget.HasCharacterShadow(int)`、`LightTarget.WithCharacterShadow(int, bool)`（Task 1）
- Produces:
  - `TimelineLightXml.characterShadow`（bool、`<CharacterShadow>`、true のときだけ書き出す）
  - `TimelineLightData.characterShadow`（bool、既定 false）
  - `StudioLightStat.characterShadow`（bool）

- [ ] **Step 1: 失敗するテストを書く**

`<T>/LightCharacterShadowXmlTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義のキャラの影 (&lt;CharacterShadow&gt;) の保存と読込を固定する。
    /// OFF は書き出さず、要素の無い旧 XML は OFF として読む (今までの見た目を変えない)
    /// </summary>
    public class LightCharacterShadowXmlTests
    {
        private static string Serialize(TimelineLightXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineLightData FromText(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var reader = new StringReader(text))
            {
                var restored = new TimelineLightData();
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
                return restored;
            }
        }

        [Fact]
        public void OFFは書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Directional };

            Assert.DoesNotContain("CharacterShadow", Serialize(data.ToXml()));
        }

        [Fact]
        public void ONを往復する()
        {
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Directional,
                shadows = LightShadows.Soft,
                characterShadow = true,
            };

            var text = Serialize(data.ToXml());

            Assert.Contains("<CharacterShadow>true</CharacterShadow>", text);
            var restored = FromText(text);
            Assert.True(restored.characterShadow);
            Assert.Equal(LightShadows.Soft, restored.shadows);
        }

        [Fact]
        public void 要素の無い旧XMLはOFFとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Directional</Type><Shadows>2</Shadows></TimelineLightXml>");

            Assert.False(restored.characterShadow);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルド＆テスト（`--filter "FullyQualifiedName~LightCharacterShadowXml"`）
Expected: テストのコンパイルが `characterShadow` が無いことで失敗する

- [ ] **Step 3: XML とデータに足す**

`<P>/Timeline/TimelineXml.cs` の `TimelineLightXml`、`ShouldSerializeshadows` の後ろへ:

```csharp

        // 「背景のみ」のライトでキャラの影を落とすか (SE 独自)。OFF では書き出さない。
        // 旧 XML は要素が無く OFF として読む (今までの背景のみのライトはキャラの影を落とさなかった)
        [XmlElement("CharacterShadow")]
        public bool characterShadow;

        public bool ShouldSerializecharacterShadow() { return characterShadow; }
```

`<P>/Timeline/TimelineData.cs` の `TimelineLightData`:

- フィールド `public LightShadows shadows = LightShadows.None;` の次の行に `public bool characterShadow = false;`
- `FromStat` の `shadows = light.shadows;` の次の行に `characterShadow = light.characterShadow;`
- `FromXml` の `shadows = SE.LightShadowValues.FromInt(xml.shadows);` の次の行に `characterShadow = xml.characterShadow;`
- `ToXml` のオブジェクト初期化子の `shadows = (int)shadows,` の次の行に `characterShadow = characterShadow,`

`<P>/Timeline/StudioLightStat.cs`:

- `public LightShadows shadows = LightShadows.None;` の後ろへ:

```csharp

        /// <summary>キャラの影 (cullingMask の影ビット) の写し。輪郭と同じく LateUpdate の比較でライト定義へ同期する</summary>
        public bool characterShadow = false;
```

- `StudioLightStat(Light light, Transform transform, object obj, int index)` の `this.shadows = ...;` の次の行へ:

```csharp
            // メインライト (index 0) の cullingMask はゲーム側の値なので定義に載せない
            this.characterShadow = index > 0 && SceneEditor.Plugin.LightTarget.HasCharacterShadow(light.cullingMask);
```

- `FromStat` の `shadows = stat.shadows;` の次の行に `characterShadow = stat.characterShadow;`

- [ ] **Step 4: 定義の同期と読込時の反映**

`<P>/Timeline/Manager/StudioLightManager.cs` の `LateUpdate(bool force)` の定義同期を次へ変える:

```csharp
                // 輪郭の種類・画像、影の種類、キャラの影はライト定義の値なので、一覧の作り直し (イベント発火) はせず定義だけ同期する。
                // 影用レイヤーが無い (未解決を含む) 間は cullingMask からキャラの影を読めないので、定義の値を上書きしない
                var characterShadowChanged = SceneEditor.Plugin.CharacterShadowLayer.isAvailable
                    && cachedLight.characterShadow != stat.characterShadow;
                if (!cachedLight.cookie.EqualsIgnoringHardness(stat.cookie)
                    || cachedLight.shadows != stat.shadows
                    || characterShadowChanged)
                {
                    cachedLight.cookie = stat.cookie;
                    cachedLight.shadows = stat.shadows;
                    if (characterShadowChanged)
                    {
                        cachedLight.characterShadow = stat.characterShadow;
                    }
                    definitionChanged = true;
                }
```

この防御は定義の同期だけに効く。ライトの一覧が作り直される経路（`FromStat`）や、影用レイヤーが無い環境での Undo・プリセットの記録では、ON は OFF として記録される。空きレイヤーが無い環境ではこの機能自体が働かないため、そこまでは守らない（Review Focus 参照）。

`SetupLights` の 2 箇所（新規作成の分岐と既存ライトの分岐）の `i > 0` ブロックの末尾に、それぞれ 1 行足す。照射対象はこの後のキーの適用で書かれ、そのとき影ビットは引き継がれる（Task 1）。

```csharp
                    // メインライト (index 0) は輪郭と影の種類を持たない (メインライトが取れないシーンでは index 0 もここを通る)
                    if (i > 0)
                    {
                        SceneEditor.Plugin.LightCookie.Set(newLight, lightData.cookie);
                        newLight.shadows = lightData.shadows;
                        newLight.cullingMask = SceneEditor.Plugin.LightTarget.WithCharacterShadow(
                            newLight.cullingMask, lightData.characterShadow);
                    }
```

```csharp
                    // メインライト (index 0) は輪郭と影の種類を持たない
                    if (i > 0)
                    {
                        SceneEditor.Plugin.LightCookie.Set(stat.light, lightData.cookie);
                        stat.light.shadows = lightData.shadows;
                        stat.light.cullingMask = SceneEditor.Plugin.LightTarget.WithCharacterShadow(
                            stat.light.cullingMask, lightData.characterShadow);
                    }
```

- [ ] **Step 5: テストが通ることを確かめる**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS

- [ ] **Step 6: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/LightCharacterShadowXmlTests.cs
git commit -m "feat(timeline): ライト定義にキャラの影を保存する"
```

---

### Task 3: シーンプリセット・Undo・コピーに含め、新規ライトを ON にする

**Files:**
- Modify: `<P>/ScenePresetData.cs`（`ScenePresetAdditionalLight` の `SetShadows` の後ろ、`CurrentVersion` とその上のコメント）
- Modify: `<P>/Manager/History/LightSnapshot.cs`（記録・`ApplyLightState`・比較）
- Modify: `<P>/LightClipboard.cs`（`Data`・`Copy`・`CopyMain`・`Paste`）
- Modify: `<P>/Manager/StudioLightManager.cs`（`AddLight`）
- Create: `<T>/ScenePresetLightCharacterShadowTests.cs`
- Modify: `<T>/ScenePresetEffectsTests.cs:149`
- Modify: `<T>/ScenePresetMaidScaleTests.cs:116-120`

**Interfaces:**
- Consumes: `LightTarget.ToCullingMask(LightTargetMode, bool)`、`LightTarget.HasCharacterShadow(int)`（Task 1）
- Produces:
  - `ScenePresetAdditionalLight.characterShadow`（bool、XML 属性、true のときだけ書き出す、既定 false）
  - `ScenePresetData.CurrentVersion == 39`

- [ ] **Step 1: 失敗するテストを書く**

`<T>/ScenePresetLightCharacterShadowTests.cs`:

```csharp
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトのキャラの影を固定する</summary>
    public class ScenePresetLightCharacterShadowTests
    {
        private static string Serialize(ScenePresetAdditionalLight light)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, light);
                return writer.ToString();
            }
        }

        private static ScenePresetAdditionalLight Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetAdditionalLight)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void OFFは属性を書き出さない()
        {
            Assert.DoesNotContain("characterShadow=", Serialize(new ScenePresetAdditionalLight()));
        }

        [Fact]
        public void ONを往復する()
        {
            var text = Serialize(new ScenePresetAdditionalLight { characterShadow = true });

            Assert.Contains("characterShadow=\"true\"", text);
            Assert.True(Deserialize(text).characterShadow);
        }

        [Fact]
        public void 属性の無い旧プリセットはOFFとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"1\" target=\"2\" shadows=\"2\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.False(Deserialize(text).characterShadow);
        }

        [Fact]
        public void シーンプリセットの版は39()
        {
            Assert.Equal(39, ScenePresetData.CurrentVersion);
        }
    }
}
```

既存テストの版の期待値を直す:

- `<T>/ScenePresetEffectsTests.cs:149` の `Assert.Equal(38, ScenePresetData.CurrentVersion);` を `Assert.Equal(39, ScenePresetData.CurrentVersion);` にする
- `<T>/ScenePresetMaidScaleTests.cs` の `シーンプリセットの版は38` テスト（`[Fact]` から閉じ括弧まで）を削除する。版の確認は新しいテストへ移す

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルド＆テスト（`--filter "FullyQualifiedName~ScenePreset"`）
Expected: テストのコンパイルが `characterShadow` が無いことで失敗する

- [ ] **Step 3: プリセットに足して版を上げる**

`<P>/ScenePresetData.cs` の `ScenePresetAdditionalLight`、`public void SetShadows(LightShadows value) { shadows = (int)value; }` の後ろへ:

```csharp

        /// <summary>
        /// 「背景のみ」のライトでキャラの影を落とすか。旧プリセットには無いので OFF で読み
        /// (今までの背景のみのライトはキャラの影を落とさなかった)、OFF では書き出さない
        /// </summary>
        [XmlAttribute]
        public bool characterShadow = false;

        public bool ShouldSerializecharacterShadow() { return characterShadow; }
```

`ScenePresetData` の版のコメントの `// v38: ...` の 2 行の後ろへ追記し、版を 39 にする:

```csharp
        // v39: 追加ライトにキャラの影 (characterShadow) を追加。OFF では書き出さない。
        //      旧形式は属性が無く OFF として読める
        public static readonly int CurrentVersion = 39;
```

- [ ] **Step 4: Undo（`LightSnapshot`）に含める**

`<P>/Manager/History/LightSnapshot.cs`:

- `CaptureState` の `new ScenePresetAdditionalLight { ... }` の初期化子で、`target = ...,` の次の行へ:

```csharp
                    characterShadow = LightTarget.HasCharacterShadow(light.cullingMask),
```

- `ApplyLightState` の `light.cullingMask = ...;`（Task 1 で書き換えた行）を記録値へ置き換える:

```csharp
            light.cullingMask = LightTarget.ToCullingMask(
                LightTarget.ClampMode(lightState.target), lightState.characterShadow);
```

- 差分の比較で `|| a.target != b.target` の次の行へ:

```csharp
                    || a.characterShadow != b.characterShadow
```

- `ApplyState` の末尾のコメント「輪郭と影の種類はタイムラインのライト定義にも載るので…」を「輪郭・影の種類・キャラの影はタイムラインのライト定義にも載るので…」に直す

- [ ] **Step 5: コピー/貼り付け（`LightClipboard`）に含める**

`<P>/LightClipboard.cs`:

- `Data` の `public LightShadows shadows;` の次の行へ `public bool characterShadow;`
- `Copy` の初期化子の `shadows = light.shadows,` の次の行へ `characterShadow = LightTarget.HasCharacterShadow(light.cullingMask),`
- `CopyMain` の初期化子の `shadows = light.shadows,` の次の行へ:

```csharp
                // メインライトは照射対象を持たないので、追加ライトの生成時の既定 (ON) にする
                characterShadow = true,
```

- `Paste` の `light.cullingMask = ...;`（Task 1 で書き換えた行）を次へ置き換える:

```csharp
            light.cullingMask = LightTarget.ToCullingMask(_data.target, _data.characterShadow);
```

- `Paste` の末尾のコメント「輪郭と影の種類はタイムラインのライト定義にも載るので…」を「輪郭・影の種類・キャラの影はタイムラインのライト定義にも載るので…」に直す

- [ ] **Step 6: 新規ライトを ON にする**

`<P>/Manager/StudioLightManager.cs` の `AddLight` の `light.color = Color.white;` の次の行へ:

```csharp
            // キャラの影は ON で始める。効くのは「背景のみ・影あり」のときだけなので、ほかの設定の見た目は変わらない。
            // 読込 (タイムライン・プリセット・Undo) では、この後に保存値で上書きされる
            light.cullingMask = LightTarget.ToCullingMask(LightTargetMode.All, true);
```

- [ ] **Step 7: テストが通ることを確かめる**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS

- [ ] **Step 8: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs \
  source/COM3D2.SceneEditor.Plugin/LightClipboard.cs source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightCharacterShadowTests.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs
git commit -m "feat(light): キャラの影をシーンプリセット・Undo・コピーに含め、新規ライトを ON にする"
```

---

### Task 4: 複製の条件判定

**Files:**
- Create: `<P>/Manager/CharacterShadowProxyRules.cs`
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Manager\CameraShakeManager.cs" />` の次の行）
- Create: `<T>/CharacterShadowProxyRulesTests.cs`

**Interfaces:**
- Consumes: `LightTarget.FromCullingMask(int, int, int)`、`LightTarget.HasCharacterShadow(int, int)`（Task 1）
- Produces: `COM3D2.SceneEditor.Plugin.CharacterShadowProxyRules`（static、純粋）
  - `static bool IsCasterLight(bool isLightActive, LightShadows shadows, int cullingMask, int characterMask, int shadowMask)`
  - `static bool ShouldProxy(int layer, ShadowCastingMode castingMode, int characterMask)`
  - `static bool ShouldUseCamera(int cameraMask, int shadowMask)`
  - `static bool ShouldHideForCamera(bool isSourceAlive, bool isSourceEnabled, bool isSourceActive, int sourceLayer, int cameraMask)`

- [ ] **Step 1: 失敗するテストを書く**

`<T>/CharacterShadowProxyRulesTests.cs`:

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CharacterShadowProxyRulesTests
    {
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);
        private const int ShadowMask = 1 << 3;

        private static int Mask(LightTargetMode mode, bool characterShadow)
        {
            return LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);
        }

        [Fact]
        public void 背景のみ_影あり_キャラの影ONのライトは複製を要する()
        {
            Assert.True(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(LightTargetMode.Background, true), CharacterMask, ShadowMask));
            Assert.True(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Hard, Mask(LightTargetMode.Background, true), CharacterMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        public void 背景のみ以外は複製を要さない(LightTargetMode mode)
        {
            // 影ビットは残っているが、キャラ本体が影を落とすので複製は要らない
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(mode, true), CharacterMask, ShadowMask));
        }

        [Fact]
        public void 無効_影なし_キャラの影OFF_影用レイヤーなしは複製を要さない()
        {
            var mask = Mask(LightTargetMode.Background, true);
            Assert.False(CharacterShadowProxyRules.IsCasterLight(false, LightShadows.Soft, mask, CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(true, LightShadows.None, mask, CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(LightTargetMode.Background, false), CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, LightTarget.ToCullingMask(LightTargetMode.Background, true, CharacterMask, 0),
                CharacterMask, 0));
        }

        [Theory]
        [InlineData(10, ShadowCastingMode.On, true)]
        [InlineData(11, ShadowCastingMode.TwoSided, true)]
        [InlineData(12, ShadowCastingMode.ShadowsOnly, true)]
        [InlineData(10, ShadowCastingMode.Off, false)]
        [InlineData(0, ShadowCastingMode.On, false)]
        [InlineData(20, ShadowCastingMode.On, false)]
        public void キャラ用レイヤーで影を落とす部位だけ複製する(int layer, ShadowCastingMode mode, bool expected)
        {
            Assert.Equal(expected, CharacterShadowProxyRules.ShouldProxy(layer, mode, CharacterMask));
        }

        [Fact]
        public void 影用レイヤーを描かないカメラでは判定しない()
        {
            Assert.True(CharacterShadowProxyRules.ShouldUseCamera(-1, ShadowMask));
            Assert.False(CharacterShadowProxyRules.ShouldUseCamera(1 << 8, ShadowMask));
            Assert.False(CharacterShadowProxyRules.ShouldUseCamera(-1, 0));
        }

        [Fact]
        public void 元が描かれるなら複製も影を落とす()
        {
            Assert.False(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 10, -1));
        }

        [Fact]
        public void 元が破棄_無効_非アクティブなら複製を止める()
        {
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(false, false, false, 0, -1));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, false, true, 10, -1));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, false, 10, -1));
        }

        [Fact]
        public void 元のレイヤーをカメラが描かないなら複製を止める()
        {
            // PostEffects のメイド非表示は Charactor / Face をカメラの cullingMask から外す
            var cameraMask = ~((1 << 10) | (1 << 11));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 10, cameraMask));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 11, cameraMask));
            Assert.False(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 12, cameraMask));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルド＆テスト（`--filter "FullyQualifiedName~CharacterShadowProxyRules"`）
Expected: テストのコンパイルが `CharacterShadowProxyRules` が無いことで失敗する

- [ ] **Step 3: 判定を書く**

`<P>/Manager/CharacterShadowProxyRules.cs`:

```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>キャラの影の複製 (CharacterShadowProxyManager) の条件判定</summary>
    public static class CharacterShadowProxyRules
    {
        /// <summary>
        /// 複製を必要とするライトか。「背景のみ」でキャラを照らさないライトだけが対象で、
        /// ほかの照射対象ではキャラ本体が影を落とすので複製は要らない
        /// </summary>
        public static bool IsCasterLight(
            bool isLightActive, LightShadows shadows, int cullingMask, int characterMask, int shadowMask)
        {
            return isLightActive
                && shadows != LightShadows.None
                && LightTarget.HasCharacterShadow(cullingMask, shadowMask)
                && LightTarget.FromCullingMask(cullingMask, characterMask, shadowMask) == LightTargetMode.Background;
        }

        /// <summary>複製する部位か。顔 (Face7) のように元から影を落とさない部位は複製しない</summary>
        public static bool ShouldProxy(int layer, ShadowCastingMode castingMode, int characterMask)
        {
            return (characterMask & (1 << layer)) != 0 && castingMode != ShadowCastingMode.Off;
        }

        /// <summary>
        /// このカメラの描画で表示判定をするか。複製の影はカメラが影用レイヤーを描くときにしか落ちないので、
        /// 描かないカメラ (オーバーレイ用など) は判定を省く
        /// </summary>
        public static bool ShouldUseCamera(int cameraMask, int shadowMask)
        {
            return shadowMask != 0 && (cameraMask & shadowMask) != 0;
        }

        /// <summary>
        /// このカメラの描画中、複製に影を落とさせないか。元がこのカメラに描かれないなら、その影も消す。
        /// ViewCullingFilter のメイド非表示は元の Renderer を無効にし、PostEffects のメイド非表示は
        /// カメラの cullingMask からキャラ用レイヤーを外すので、どちらもここで拾える
        /// </summary>
        public static bool ShouldHideForCamera(
            bool isSourceAlive, bool isSourceEnabled, bool isSourceActive, int sourceLayer, int cameraMask)
        {
            if (!isSourceAlive || !isSourceEnabled || !isSourceActive)
            {
                return true;
            }
            return (cameraMask & (1 << sourceLayer)) == 0;
        }
    }
}
```

`<P>/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Manager\CameraShakeManager.cs" />` の次の行へ:

```xml
    <Compile Include="Manager\CharacterShadowProxyRules.cs" />
```

- [ ] **Step 4: テストが通ることを確かめる**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS

- [ ] **Step 5: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyRules.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
  source/COM3D2.SceneEditor.Plugin.Tests/CharacterShadowProxyRulesTests.cs
git commit -m "feat(light): キャラの影の複製の条件判定を追加する"
```

---

### Task 5: 影専用の複製を管理する

**Files:**
- Create: `<P>/Manager/CharacterShadowProxyManager.cs`
- Modify: `<P>/COM3D2.SceneEditor.Plugin.cs`（`managerRegistry.RegisterManager(CameraShakeManager.instance);` の後ろ）
- Modify: `<P>/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="Manager\CharacterShadowProxyRules.cs" />` の次の行）

**Interfaces:**
- Consumes: `CharacterShadowProxyRules`（Task 4）、`CharacterShadowLayer.layer` / `mask`（Task 1）、`LightTarget.CharacterMask`、`StudioLightManager.instance.lights`（SE 側、`List<Light>`）
- Produces: `COM3D2.SceneEditor.Plugin.CharacterShadowProxyManager : ManagerBase`
  - `static CharacterShadowProxyManager instance`
  - `int proxyCount`（今ある複製の数。実機検証で使う）

このタスクは Unity の実体を扱うためゲーム外テストは書かない。確認は Task 7 の実機検証で行う。

- [ ] **Step 1: マネージャを書く**

`<P>/Manager/CharacterShadowProxyManager.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「背景のみ」のライトでキャラの影を背景へ落とすための、影専用の複製を管理する。
    /// ライトの cullingMask から外したキャラはそのライトの影も落とさない (Unity 標準パイプラインの挙動) ため、
    /// メッシュ・骨・マテリアルを共有する ShadowsOnly の複製を影用レイヤーに置いて、影だけを戻す。
    /// 骨を共有するのでモーションにそのまま追従し、体型や表情は TMorph がメッシュを直接書き換えるので共有で追従する。
    /// 複製はメイドの階層の外に置く。ゲームや他の機能が GetComponentsInChildren で集める対象に混ざらないようにするため
    /// </summary>
    public class CharacterShadowProxyManager : ManagerBase
    {
        private const string RootName = "SceneEditor CharacterShadowProxies";

        // 着替え以外で Renderer が増える変化 (部位の付け外し等) は参照の比較では拾えないため、
        // 一定フレームごとに複製元の顔ぶれを取り直して比べる
        private const int SourceCheckInterval = 60;

        private class Proxy
        {
            public Renderer source;
            public Renderer renderer;
            public SkinnedMeshRenderer sourceSkinned;
            public SkinnedMeshRenderer proxySkinned;
            public MeshFilter sourceFilter;
            public Mesh mesh;
            public Material[] materials;
            public int blendShapeCount;
        }

        private class Entry
        {
            public bool isDirty = true;
            public readonly List<Proxy> proxies = new List<Proxy>();
        }

        private static CharacterShadowProxyManager _instance;
        public static CharacterShadowProxyManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CharacterShadowProxyManager();
                }
                return _instance;
            }
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();
        private readonly List<Maid> _characters = new List<Maid>();
        private readonly List<Maid> _absentCharacters = new List<Maid>();
        private readonly List<Renderer> _sources = new List<Renderer>();
        // onPreCull で止めた複製 (onPostRender で戻す)
        private readonly List<Renderer> _hiddenForCamera = new List<Renderer>();
        private GameObject _root;
        private bool _isCameraHooked = false;
        private int _lastSourceCheckFrame = -1;

        /// <summary>今ある複製の数 (実機検証用)</summary>
        public int proxyCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries.Values)
                {
                    count += entry.proxies.Count;
                }
                return count;
            }
        }

        /// <summary>ライトの適用 (タイムライン・UI) が済んだ後に判定するため LateUpdate で行う</summary>
        public override void LateUpdate()
        {
            if (!HasCasterLight())
            {
                DestroyAll();
                return;
            }

            if (_root == null)
            {
                _root = new GameObject(RootName);
                _root.hideFlags = HideFlags.HideAndDontSave;
            }
            HookCamera();

            var frame = Time.frameCount;
            var checkSources = _lastSourceCheckFrame < 0 || frame - _lastSourceCheckFrame >= SourceCheckInterval;
            if (checkSources)
            {
                _lastSourceCheckFrame = frame;
            }

            CollectCharacters(_characters);
            RemoveAbsentEntries();

            foreach (var maid in _characters)
            {
                Entry entry;
                if (!_entries.TryGetValue(maid, out entry))
                {
                    entry = new Entry();
                    _entries.Add(maid, entry);
                }

                // 着替え中はスロットが破棄・再生成されるので、破棄済みの骨やメッシュを指す複製を残さない。
                // 完了 (IsAllProcPropBusy が false に戻る) 後に作り直す
                if (maid.IsAllProcPropBusy)
                {
                    DestroyProxies(entry);
                    entry.isDirty = true;
                    continue;
                }

                if (entry.isDirty || IsStale(entry)
                    || (checkSources && (HasMaterialChanged(entry) || HasSourceChanged(maid, entry))))
                {
                    Rebuild(maid, entry);
                }
                SyncProxies(entry);
            }
        }

        public override void OnPluginDisable()
        {
            DestroyAll();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            DestroyAll();
        }

        private static bool HasCasterLight()
        {
            var shadowMask = CharacterShadowLayer.mask;
            if (shadowMask == 0)
            {
                return false;
            }

            var characterMask = LightTarget.CharacterMask;
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null && CharacterShadowProxyRules.IsCasterLight(
                    light.enabled && light.gameObject.activeInHierarchy,
                    light.shadows, light.cullingMask, characterMask, shadowMask))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>影を落とすキャラ (メイドと男)。非アクティブのキャラ (呼んでいない男など) は含めない</summary>
        private static void CollectCharacters(List<Maid> results)
        {
            results.Clear();
            var gameMain = GameMain.Instance;
            var characterMgr = gameMain != null ? gameMain.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }

            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                AddIfPresent(results, characterMgr.GetMaid(i));
            }
            for (var i = 0; i < characterMgr.GetManCount(); i++)
            {
                AddIfPresent(results, characterMgr.GetMan(i));
            }
        }

        private static void AddIfPresent(List<Maid> results, Maid maid)
        {
            if (maid != null && maid.body0 != null && maid.gameObject.activeInHierarchy)
            {
                results.Add(maid);
            }
        }

        private void RemoveAbsentEntries()
        {
            _absentCharacters.Clear();
            foreach (var maid in _entries.Keys)
            {
                if (maid == null || !_characters.Contains(maid))
                {
                    _absentCharacters.Add(maid);
                }
            }

            foreach (var maid in _absentCharacters)
            {
                DestroyProxies(_entries[maid]);
                _entries.Remove(maid);
            }
        }

        /// <summary>複製元の破棄とメッシュの差し替えを毎フレーム拾う</summary>
        private static bool IsStale(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.source == null || proxy.renderer == null)
                {
                    return true;
                }

                var mesh = proxy.sourceSkinned != null
                    ? proxy.sourceSkinned.sharedMesh
                    : proxy.sourceFilter != null ? proxy.sourceFilter.sharedMesh : null;
                if (mesh != proxy.mesh)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// マテリアルの差し替え (MaterialShaderManager のシェーダー変更を含む) を拾う。
        /// sharedMaterials は呼ぶたびに配列を確保するので、毎フレームではなく複製元の顔ぶれの確認と同じ間隔で比べる
        /// </summary>
        private static bool HasMaterialChanged(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (!SameMaterials(proxy.source.sharedMaterials, proxy.materials))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasSourceChanged(Maid maid, Entry entry)
        {
            CollectSources(maid, _sources);
            if (_sources.Count != entry.proxies.Count)
            {
                return true;
            }
            for (var i = 0; i < _sources.Count; i++)
            {
                if (_sources[i] != entry.proxies[i].source)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 複製元の Renderer。非アクティブの部位も含める (表示の切り替えは描画ごとの判定で追従する)。
        /// MeshRenderer は調査時のメイドには無かったが、見つかった場合は Transform を毎フレーム写して扱う
        /// </summary>
        private static void CollectSources(Maid maid, List<Renderer> results)
        {
            results.Clear();
            var characterMask = LightTarget.CharacterMask;
            foreach (var renderer in maid.gameObject.GetComponentsInChildren<Renderer>(true))
            {
                if (!CharacterShadowProxyRules.ShouldProxy(
                    renderer.gameObject.layer, renderer.shadowCastingMode, characterMask))
                {
                    continue;
                }
                if (renderer is SkinnedMeshRenderer
                    || (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null))
                {
                    results.Add(renderer);
                }
            }
        }

        private void Rebuild(Maid maid, Entry entry)
        {
            DestroyProxies(entry);
            CollectSources(maid, _sources);
            foreach (var source in _sources)
            {
                entry.proxies.Add(CreateProxy(source));
            }
            entry.isDirty = false;
        }

        private Proxy CreateProxy(Renderer source)
        {
            var go = new GameObject(source.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = CharacterShadowLayer.layer;
            go.transform.SetParent(_root.transform, false);

            var proxy = new Proxy { source = source };
            var skinned = source as SkinnedMeshRenderer;
            if (skinned != null)
            {
                var proxySkinned = go.AddComponent<SkinnedMeshRenderer>();
                proxySkinned.sharedMesh = skinned.sharedMesh;
                proxySkinned.bones = skinned.bones;
                // メイドの rootBone は空 (描画範囲の基準が自身の Transform)。複製で空のままだと基準が複製のルートになってずれるため、
                // 元の基準の Transform を指定し、localBounds を写す
                proxySkinned.rootBone = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                proxySkinned.localBounds = skinned.localBounds;
                proxySkinned.quality = skinned.quality;
                proxySkinned.updateWhenOffscreen = skinned.updateWhenOffscreen;

                proxy.sourceSkinned = skinned;
                proxy.proxySkinned = proxySkinned;
                proxy.mesh = skinned.sharedMesh;
                proxy.blendShapeCount = skinned.sharedMesh != null ? skinned.sharedMesh.blendShapeCount : 0;
                proxy.renderer = proxySkinned;
            }
            else
            {
                var filter = source.GetComponent<MeshFilter>();
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                proxy.sourceFilter = filter;
                proxy.mesh = filter.sharedMesh;
                proxy.renderer = go.AddComponent<MeshRenderer>();
            }

            // アルファテストの切り抜きを影に反映させるため、マテリアルも共有する
            proxy.materials = source.sharedMaterials;
            proxy.renderer.sharedMaterials = proxy.materials;
            proxy.renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            proxy.renderer.receiveShadows = false;
            return proxy;
        }

        private static void SyncProxies(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.proxySkinned != null)
                {
                    proxy.proxySkinned.localBounds = proxy.sourceSkinned.localBounds;
                    // 調査時のメイドはブレンドシェイプを使っていなかったが、使うメッシュでは重みを写す
                    for (var i = 0; i < proxy.blendShapeCount; i++)
                    {
                        proxy.proxySkinned.SetBlendShapeWeight(i, proxy.sourceSkinned.GetBlendShapeWeight(i));
                    }
                }
                else
                {
                    var sourceTransform = proxy.source.transform;
                    var proxyTransform = proxy.renderer.transform;
                    proxyTransform.position = sourceTransform.position;
                    proxyTransform.rotation = sourceTransform.rotation;
                    // 複製のルートは原点・等倍なので、ワールドの拡縮をそのまま写せる
                    proxyTransform.localScale = sourceTransform.lossyScale;
                }
            }
        }

        private void HookCamera()
        {
            if (_isCameraHooked)
            {
                return;
            }
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
            _isCameraHooked = true;
        }

        private void UnhookCamera()
        {
            if (!_isCameraHooked)
            {
                return;
            }
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
            _isCameraHooked = false;
            RestoreHidden();
        }

        /// <summary>
        /// 元がこのカメラに描かれないなら、このカメラの描画中は複製の影も止める。影の計算はカメラごとに行われる。
        /// Camera.onPreCull はカメラのコンポーネントの OnPreCull (ViewCullingFilter・PostEffects の MaidHideEffect) より
        /// 後に呼ばれるため、それらが無効にした Renderer や書き換えた cullingMask をここで読める (実機で確認済み)
        /// </summary>
        private void OnPreCull(Camera camera)
        {
            // 前のカメラの onPostRender が来なかったときの保険
            RestoreHidden();

            var cameraMask = camera.cullingMask;
            if (!CharacterShadowProxyRules.ShouldUseCamera(cameraMask, CharacterShadowLayer.mask))
            {
                return;
            }

            foreach (var entry in _entries.Values)
            {
                foreach (var proxy in entry.proxies)
                {
                    var renderer = proxy.renderer;
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    var source = proxy.source;
                    var isAlive = source != null;
                    if (CharacterShadowProxyRules.ShouldHideForCamera(
                        isAlive,
                        isAlive && source.enabled,
                        isAlive && source.gameObject.activeInHierarchy,
                        isAlive ? source.gameObject.layer : 0,
                        cameraMask))
                    {
                        renderer.enabled = false;
                        _hiddenForCamera.Add(renderer);
                    }
                }
            }
        }

        private void OnPostRender(Camera camera)
        {
            RestoreHidden();
        }

        private void RestoreHidden()
        {
            for (var i = 0; i < _hiddenForCamera.Count; i++)
            {
                var renderer = _hiddenForCamera[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            _hiddenForCamera.Clear();
        }

        private static void DestroyProxies(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.renderer != null)
                {
                    Object.Destroy(proxy.renderer.gameObject);
                }
            }
            entry.proxies.Clear();
        }

        /// <summary>条件を満たすライトが無くなったら複製をすべて捨て、この機能を使わない間のコストを無くす</summary>
        private void DestroyAll()
        {
            UnhookCamera();
            foreach (var entry in _entries.Values)
            {
                DestroyProxies(entry);
            }
            _entries.Clear();
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
```

注意:
- `StudioLightManager` は SE 側（`COM3D2.SceneEditor.Plugin.StudioLightManager`、`List<Light> lights`）。同名の MTE 側クラスと取り違えない。このファイルは `COM3D2.SceneEditor.Plugin` 名前空間なので、修飾なしで SE 側に解決される
- `Object` は `UnityEngine.Object`。`System` を using していないので曖昧にならない
- `DestroyAll` は毎フレーム呼ばれうる。複製が無いときは、`_entries` が空で `_root` が null なので何もしない

- [ ] **Step 2: 登録と csproj**

`<P>/COM3D2.SceneEditor.Plugin.cs` の `managerRegistry.RegisterManager(CameraShakeManager.instance);` の後ろへ:

```csharp

                // ライトの照射対象・影はタイムラインと UI が書くため、それより後に登録して LateUpdate で判定する
                managerRegistry.RegisterManager(CharacterShadowProxyManager.instance);
```

`<P>/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Manager\CharacterShadowProxyRules.cs" />` の次の行へ:

```xml
    <Compile Include="Manager\CharacterShadowProxyManager.cs" />
```

- [ ] **Step 3: ビルドとテスト**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS（`ManagerPluginEnableTests` は `OnPluginEnable` を上書きしないので影響しない）

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyManager.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(light): 背景のみのライトへキャラの影を落とす影専用の複製を管理する"
```

---

### Task 6: 「キャラの影」トグル

**Files:**
- Modify: `<P>/LightRowDrawer.cs`（`DrawAdditionalLight` 相当の本体の「影の濃さ」ブロック、`SetShadows` の後ろ）

**Interfaces:**
- Consumes: `LightTarget.FromCullingMask(int)`、`LightTarget.HasCharacterShadow(int)`、`LightTarget.WithCharacterShadow(int, bool)`、`CharacterShadowLayer.isAvailable`（Task 1）
- Produces: なし

- [ ] **Step 1: トグルの行を足す**

`<P>/LightRowDrawer.cs` の `DrawShadowTypeRow(view, labelWidth, rowHeight, light);` の直後のブロックを次へ変える:

```csharp
            DrawShadowTypeRow(view, labelWidth, rowHeight, light);

            // 濃さと距離は影を落とすときだけ意味を持つ
            if (light.shadows != LightShadows.None)
            {
                // キャラの影はキャラを照らさない「背景のみ」のときだけ意味を持つ
                if (LightTarget.FromCullingMask(light.cullingMask) == LightTargetMode.Background)
                {
                    DrawCharacterShadowRow(view, rowHeight, light);
                }
                DrawAxisSlider(view, labelWidth, "影の濃さ", light.shadowStrength, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowStrength, value => light.shadowStrength = value);
                DrawAxisSlider(view, labelWidth, "影の距離", light.shadowBias, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowBias, value => light.shadowBias = value);
            }
```

`DrawShadowTypeButton` の後ろへ:

```csharp

        /// <summary>
        /// 「背景のみ」のライトでもキャラの影を背景へ落とすか。キャラ自身は照らさないまま、影専用の複製で影だけを戻す。
        /// 影用レイヤーが確保できなかった環境では操作できない
        /// </summary>
        private static void DrawCharacterShadowRow(GUIView view, float rowHeight, Light light)
        {
            var isAvailable = CharacterShadowLayer.isAvailable;
            // トグルはツールチップを持たないので、描く前に同じ矩形を取って登録する
            var rect = view.GetDrawRect(-1, rowHeight);
            view.DrawToggle("キャラの影", LightTarget.HasCharacterShadow(light.cullingMask), -1, rowHeight, isAvailable,
                value =>
                {
                    RecordLightEdit("キャラの影");
                    SetCharacterShadow(light, value);
                });
            if (!isAvailable)
            {
                TooltipDrawer.RegisterIfHovered(rect, "影の複製に使える空きレイヤーが無いため使えません");
            }
        }
```

`SetShadows` の後ろへ:

```csharp

        /// <summary>キャラの影を反映し、輪郭と同じくタイムラインのライト定義へ即時に同期させる</summary>
        private static void SetCharacterShadow(Light light, bool value)
        {
            light.cullingMask = LightTarget.WithCharacterShadow(light.cullingMask, value);
            MTEP.StudioLightManager.instance.LateUpdate(true);
        }
```

`TooltipDrawer` は `COM3D2.MotionTimelineEditor` 名前空間で、`LightRowDrawer.cs` はすでに `using COM3D2.MotionTimelineEditor;` を持つ。

- [ ] **Step 2: ビルドとテスト**

Run: ビルド＆テスト（全件）
Expected: 2 構成のビルドが通り、テストがすべて PASS

- [ ] **Step 3: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs
git commit -m "feat(light): 背景のみのライトにキャラの影のトグルを追加する"
```

---

### Task 7: 実機検証とドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md`（98 行目の影の説明の後ろ）
- Modify: `docs-site/timeline/compatibility.md`（21 行目の影の種類の項目の後ろ）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「タイムライン XML の互換方向」の、ライト定義の輪郭・影の項目の後ろ。git 管理外）

**Interfaces:**
- Consumes: `CharacterShadowProxyManager.instance.proxyCount`（Task 5）

- [ ] **Step 1: DLL を実機へ反映する**

`com3d25-devbridge:restart-verify` スキルに従い、ゲームを終了 → COM3D25 構成の DLL を反映 → 起動 → 通常シーン（デイリー画面）でエディタを有効にする。撮影モードでは検証しない。

- [ ] **Step 2: spec の検証手順 1〜7 を確かめる**

devbridge の `eval_csharp` と `screenshot` で確かめる。プラグインの型はリフレクションで辿る（`AppDomain.CurrentDomain.GetAssemblies()` から名前に `SceneEditor` を含むアセンブリを取り、`GetType("COM3D2.SceneEditor.Plugin.CharacterShadowProxyManager")` → `GetProperty("instance")` → `proxyCount`）。

1. 追加ライト（平行光源）を作り、照射対象を「背景のみ」、影を「ソフト」にする。「キャラの影」トグルが表示され、ON になっていること
2. キャラの影が床に落ち、キャラ自身はそのライトに照らされないこと（スクリーンショットで比較）。`proxyCount` が 0 より大きいこと
3. 「キャラの影」を OFF にすると影が消え、`proxyCount` が 0 になること。ON に戻すと影が出ること
4. 照射対象を「全て」→「背景のみ」とキーで切り替えても、トグルが ON のまま保たれること
5. 次で影が追従するか消えることを確かめる
   - 着替え: 完了後に影が新しい衣装の形になり、ログに例外が出ない（`tail_log`）
   - メイドの追加と削除
   - GameView の「メイド非表示」
   - PostEffects の「メイド非表示」
   - 男の呼び出しと非表示。男が実際に見えるシーン（夜伽など）で、男の Renderer のレイヤーとメインカメラの `cullingMask` を記録し、男の影が落ちることを確かめる。落ちなければ `ShouldHideForCamera` の判定を見直す
6. XML とシーンプリセットに保存して読み直し、設定が戻ること。既存の XML・プリセット（「背景のみ」で影ありのライトを含むもの）を読み込んで、キャラの影が出ない（今までどおり）こと
7. 条件を満たすライトを消したとき、`proxyCount` が 0 になり、`SceneEditor CharacterShadowProxies` の GameObject が無くなること

うまくいかない項目があれば、superpowers:systematic-debugging で原因を調べてから直す。

- [ ] **Step 3: 利用者向けドキュメントを書く**

`docs-site/guide/staging.md` の 98 行目（影の種類の説明の段落）の後ろへ:

```markdown
`対象` が `背景のみ` で影が `なし` 以外のときは、`キャラの影` を選べます。ON にすると、キャラはそのライトに照らされないまま、キャラの影だけが背景へ落ちます。新しく追加したライトは ON で始まり、以前の版で保存したシーンやプリセットのライトは OFF で読み込まれます。影を落とすキャラが多いほど重くなります。
```

`docs-site/timeline/compatibility.md` の 21 行目の後ろへ:

```markdown
- 追加ライトの `キャラの影` もライト定義として保存されます。SceneEditor 独自の値で、MTE や以前の SceneEditor で読むと OFF（`背景のみ` のライトはキャラの影を落とさない）になります
```

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」で、「ライトキーの輪郭の硬さ（index 19、値数 20）は SE 独自…」の項目の後ろへ:

```markdown
- ライト定義のキャラの影（`<CharacterShadow>`、bool）は SE 独自。OFF では書き出さない。要素の無い旧 XML は OFF で読む。実行時は追加ライトの `cullingMask` の影用レイヤーのビット（`CharacterShadowLayer`）で持ち、「背景のみ」で影ありのライトにだけ効く（影専用の複製 `CharacterShadowProxyManager` がキャラの影を落とす）。MTE・旧 SE は要素を読み飛ばし、キャラの影なしで表示する。シーンプリセットは v39 で追加ライトに `characterShadow` 属性を追加
```

- [ ] **Step 4: コミット**

`W:\COM3D2_5\work\CLAUDE.md` は git 管理外なのでコミットに含めない。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site/guide/staging.md docs-site/timeline/compatibility.md
git commit -m "docs(light): 背景のみのライトのキャラの影を説明する"
```

## レビュー却下メモ

- 影用レイヤーをシーンごとに解決し直す — 解決し直して別のレイヤーになると、既存ライトの `cullingMask` に立っている古い影ビットが読めなくなり、設定が消える。spec の「起動後の初回に 1 つ選ぶ」に従う
- 複製がメインライトなど他のライトの影にも加わる — 本体と同じ位置・形の影なので見た目は変わらない（spec の方式どおり）。コストが増えるのは複製がある間だけ
- `LightSnapshot` の比較や `StudioLightStat`・`SetupLights` の結線にゲーム外テストが無い — Unity の実体（`Light`）が要るため、ゲーム外テストは書けない。Task 7 の実機検証で確かめる
