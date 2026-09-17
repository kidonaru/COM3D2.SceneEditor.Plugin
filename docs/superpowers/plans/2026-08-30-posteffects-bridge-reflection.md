# PostEffects 連携のリフレクションブリッジ化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SceneEditor から PostEffects.Plugin への**コンパイル時アセンブリ参照を撤去**し、MTEUtils の共有 DTO + リフレクションのフィールドコピー方式へ置き換えて、UnityInjector のロード順に依存せずポストエフェクトのタイムラインレイヤーが動くようにする。

**Architecture:** ① MTEUtils（両リポジトリでソース複製する共有層）に名前空間 `COM3D2.MotionTimelineEditor.PostEffects` の DTO 5 種と、同名 public フィールドを写す汎用 `ReflectionFieldCopier` を追加する。② PostEffects.Plugin の `TimelineBridge` の公開シグネチャを DTO 型へ差し替え、内部で自前の実体型と相互コピーする。③ SceneEditor 側は `MTEUtils/PostEffectsClient.cs`（既存の `ModelProviderClient` / `DockingClient` と同じ「ホスト型が見つかるまで再試行する」リフレクションクライアント）から `TimelineBridge` を叩く。④ `PostEffectsBridge` は `PostEffectsClient.isAvailable` へ委譲するだけの薄い別名にする。⑤ csproj の `Reference` と全ての `extern alias` を撤去する。

**Tech Stack:** C#（Unity IMGUI / リフレクション）、2 リポジトリ（COM3D2.PostEffects.Plugin / COM3D2.SceneEditor.Plugin）、xUnit

**Spec:** `docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md`

## 背景（この計画が必要になった理由）

直前の実装（`docs/superpowers/plans/2026-08-30-posteffects-timeline-integration.md`）は SceneEditor から `COM3D25.PostEffects.Plugin.dll` を直接参照する設計だった。実機ログで以下が確認された:

```
6691  UnityInjector: loaded COM3D2.SceneEditor.Plugin 1.4.0.0
6692  Failed to load W:\...\COM3D2.SceneEditor.Plugin.dll. Stack trace:
6693  System.TypeLoadException: Could not load type of field
      'PostEffectRowDrawer+<>c__DisplayClass19_0:paraffin' (0) due to:
      Could not load file or assembly 'COM3D25.PostEffects.Plugin, Version=2.1.0.0...'
6700  UnityInjector: loaded COM3D25.PostEffects.Plugin 2.1.0.0
6730  SceneEditor: PostEffects.Plugin が見つからないため、…無効化します
```

- UnityInjector はファイル名順にロードするため、`COM3D2.SceneEditor.Plugin.dll`（`.` = 0x2E）が `COM3D25.PostEffects.Plugin.dll`（`5` = 0x35）より先に来る
- SceneEditor のロード時点で PostEffects は未ロードのため参照解決に失敗し、**Mono はこの束縛失敗をプロセス寿命の間キャッシュする**
- 実機検証: 後から `Assembly.Load("COM3D25.PostEffects.Plugin, Version=2.1.0.0, …")` は成功するが、`TimelineBridge` への参照は `FileNotFoundException` のまま。`AppDomain.AssemblyResolve` を後付けしても解決しない
- 型列挙の失敗は UnityInjector（6692）と COM3D2 API（6774）の双方で起きており、ポストエフェクト以外の連携にも影響する

したがって `AssemblyResolve` では対処できない（SceneEditor 自身のコードが動く前に型列挙が走るため、ハンドラを間に合わせられない）。**コンパイル時参照そのものをやめる**のが唯一の恒久策である。

## Global Constraints

- **両構成ビルド必須**: 両リポジトリとも COM3D2 (.NET 3.5) / COM3D25 (.NET 4.7.1) を MSBuild 直叩きで確認する。`debug.bat` / `build.bat` 経由は使わない（実機へ DLL をコピーするため）
- .NET 3.5 制約: 入力 5 個以上の `Func<>`/`Action<>` 禁止
- コメント・ログは日本語
- **MTEUtils は両リポジトリで同一ソースを複製する層**。`MTEUtils/` 配下に置くファイルは 2 リポジトリへ**バイト単位で同じ内容**をコピーすること
- **SceneEditor から `COM3D25.PostEffects.Plugin` の型名を直接書かない**。文字列によるリフレクション解決のみ（`PostEffectsClient` の内部に閉じる）
- リフレクションクライアントは既存 `MTEUtils/ModelProviderClient.cs` の作法に従う: ホスト型が見つからない間は `_initialized` を立てずに再試行し、型は見つかったがシグネチャが合わない場合のみ恒久無効化する
- 実機での動作確認は次回ゲーム起動時チェックリストに積む（本計画ではビルドとテストまで）

## ビルド・テストコマンド

MSBuild は Git Bash から叩くと `/p:` がパス変換されるため `-p:` 形式を使い、`MSYS2_ARG_CONV_EXCL='*'` を設定すること。

PostEffects.Plugin:

```bash
export MSYS2_ARG_CONV_EXCL='*'
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
P="W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj"
"$MSB" "$P" -nologo -v:m -p:Configuration=Debug -p:GameVersion=COM3D2  "-p:COM3D2_DIR=W:\\COM3D2" "-p:COM3D25_DIR=W:\\COM3D2_5"
"$MSB" "$P" -nologo -v:m -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\\COM3D2" "-p:COM3D25_DIR=W:\\COM3D2_5"
```

SceneEditor.Plugin:

```bash
P="W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj"
"$MSB" "$P" -nologo -v:m -p:Configuration=Debug -p:GameVersion=COM3D2  "-p:COM3D2_DIR=W:\\COM3D2" "-p:COM3D25_DIR=W:\\COM3D2_5"
"$MSB" "$P" -nologo -v:m -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\\COM3D2" "-p:COM3D25_DIR=W:\\COM3D2_5"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

既知の先行失敗: `EyesItemInspectorTests.ResolveRowKind_項目名から編集UI種別を求める` 4 件は本計画と無関係に失敗する（`EyesPosL` 等に `Unsupported` を期待しているが実装は `EyesPos` を返す）。**合格 340 / 失敗 4** が現状のベースラインで、これを悪化させないこと。

テストプロジェクトは `source/COM3D2.SceneEditor.Plugin/bin/Debug/COM3D25/` に `netstandard.dll` と `UnityEngine.SharedInternalsModule.dll` が存在することを前提にする。COM3D25 構成のビルドはこの 2 つを生成しないため、`-t:Rebuild` を実行すると消える。消えた場合は `W:\COM3D2_5\COM3D2x64_Data\Managed\` からコピーして戻すこと。**本計画では `-t:Rebuild` を使わない**。

---

### Task 1: MTEUtils に共有 DTO と汎用フィールドコピーを追加する

両リポジトリに同一ソースで置く。**この時点ではまだ誰も使わない**ため、ビルドが通ることだけを確認する。

**Files:**
- Create: `W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectData.cs`
- Create: `W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/ReflectionFieldCopier.cs`
- Create: `W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils/PostEffectData.cs`（上記と同一内容）
- Create: `W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils/ReflectionFieldCopier.cs`（上記と同一内容）
- Modify: 両リポジトリの csproj（`Compile` 追加）

**Interfaces:**
- Consumes: なし
- Produces（Task 2 以降が消費）:
  - 名前空間 `COM3D2.MotionTimelineEditor.PostEffects` の `ParaffinData` / `DistanceFogData` / `RimlightData` / `GTToneMapData` / `DepthOfFieldData`（いずれも public フィールドのみの class、既定コンストラクタあり）
  - `COM3D2.MotionTimelineEditor.ReflectionFieldCopier.Copy(object src, object dst)`

- [ ] **Step 1: `MTEUtils/PostEffectData.cs` を作成する**

DTO のフィールド名・型は **PostEffects.Plugin 側の実体型と 1:1 で一致させる**こと（フィールドコピーが名前一致で行われるため）。既定値も実体側に揃える。

```csharp
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.PostEffects
{
    /// <summary>
    /// プラグイン間で受け渡すポストエフェクトの値。
    /// PostEffects.Plugin と SceneEditor が同じソースを各々コンパイルするため
    /// CLR 上は別型になり、境界では ReflectionFieldCopier で同名フィールドを写す。
    /// **フィールド名は PostEffects.Plugin 側の実体型と 1:1 で一致させること**
    /// (名前が食い違うと、その値だけ黙って既定値のまま素通りする)
    /// </summary>
    public class ParaffinData
    {
        public bool enabled = false;
        public Color color1 = new Color(0.68f, 0.34f, 0f, 1f);
        public Color color2 = new Color(0.68f, 0.34f, 0f, 0f);

        public Vector2 centerPosition = new Vector2(0.5f, 1.0f);
        public float radiusFar = 1f;
        public float radiusNear = 0f;
        public Vector2 radiusScale = new Vector2(1f, 1f);
        // 0=マスクなし / 1=キャラ除外 / 2=キャラのみ
        public int maskMode = 0;

        public float useNormal = 0f;
        public float useAdd = 1f;
        public float useMultiply = 0f;
        public float useOverlay = 0f;
        public float useSubstruct = 0f;
    }

    public class DistanceFogData
    {
        public bool enabled = false;
        public Color color1 = new Color(1f, 1f, 1f, 1f);
        public Color color2 = new Color(1f, 1f, 1f, 0f);

        public float fogStart = 0f;
        public float fogEnd = 40f;
        public float fogExp = 1f;

        public float useNormal = 1f;
        public float useAdd = 0f;
        public float useMultiply = 0f;
        public float useOverlay = 0f;
        public float useSubstruct = 0f;
    }

    public class RimlightData
    {
        public bool enabled = false;
        public Color color1 = new Color(0.77f, 0.70f, 1f, 1f);
        public Color color2 = new Color(0.77f, 0.70f, 1f, 0f);

        public Vector3 rotation = new Vector3(10f, -40f, 0f);
        public float lightArea = 1f;
        public float fadeRange = 0.2f;
        public float fadeExp = 1f;

        public float useNormal = 0f;
        public float useAdd = 0.8f;
        public float useMultiply = 0f;
        public float useOverlay = 0f;
        public float useSubstruct = 0f;

        public bool isWorldSpace = false;
        // 頭部 (顔・髪・頭アクセ) にリムライトを乗せない
        public bool excludeFace = true;
        // excludeFace 時でも髪 (髪・帽子・髪アクセ) には適用する
        public bool applyHair = false;
        // 0=マスクなし / 1=キャラ除外 / 2=キャラのみ
        public int maskMode = 2;
    }

    public class GTToneMapData
    {
        public bool enabled = false;
        public float maxBrightness = 1f;
        public float contrast = 1f;
        public float linearStart = 0.22f;
        public float linearLength = 0.4f;
        public float blackTightness = 1.33f;
        public float blackOffset = 0f;
    }

    /// <summary>
    /// 被写界深度。タイムラインが駆動する項目だけを持ち、
    /// DX11 ボケ等の実体側だけの項目は持たない (触らない = PostEffects 側 UI の管轄)
    /// </summary>
    public class DepthOfFieldData
    {
        public bool enabled = false;
        public float focalLength = 10f;
        public float focalSize = 0.05f;
        public float aperture = 11.5f;
        public float maxBlurSize = 2f;
        // メイドの頭にフォーカスを追従させる
        public bool maidFocus = false;
        // 準備完了メイド一覧の中のインデックス
        public int maidIndex = 0;
    }
}
```

- [ ] **Step 2: `MTEUtils/ReflectionFieldCopier.cs` を作成する**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// 構造が同じだが CLR 上は別型になるクラス間で、同名 public フィールドを写す。
    /// プラグインごとに同じソースをコンパイルする DTO の受け渡しに使う。
    /// 型ペアごとにフィールド対応をキャッシュするため、毎フレーム呼んでも
    /// リフレクション探索は初回だけで済む
    /// </summary>
    public static class ReflectionFieldCopier
    {
        private struct TypePair : IEquatable<TypePair>
        {
            public readonly Type src;
            public readonly Type dst;

            public TypePair(Type src, Type dst)
            {
                this.src = src;
                this.dst = dst;
            }

            public bool Equals(TypePair other)
            {
                return src == other.src && dst == other.dst;
            }

            public override bool Equals(object obj)
            {
                return obj is TypePair && Equals((TypePair)obj);
            }

            public override int GetHashCode()
            {
                return src.GetHashCode() ^ (dst.GetHashCode() << 1);
            }
        }

        private class FieldPair
        {
            public FieldInfo src;
            public FieldInfo dst;
        }

        private static readonly Dictionary<TypePair, List<FieldPair>> _cache
            = new Dictionary<TypePair, List<FieldPair>>();

        /// <summary>
        /// src の public インスタンスフィールドを、dst の同名・代入可能なフィールドへ写す。
        /// 対応の無いフィールドは黙って無視する (dst 側は既定値のまま残る)
        /// </summary>
        public static void Copy(object src, object dst)
        {
            if (src == null || dst == null)
            {
                return;
            }

            foreach (var pair in GetFieldPairs(src.GetType(), dst.GetType()))
            {
                pair.dst.SetValue(dst, pair.src.GetValue(src));
            }
        }

        /// <summary>
        /// src にあって dst に写せなかった public インスタンスフィールド名を返す。
        /// 名前がずれた値は例外を出さず既定値のまま素通りするため、
        /// 接続時の自己診断でこれを出して気付けるようにする
        /// </summary>
        public static List<string> FindUnmappedFields(Type srcType, Type dstType)
        {
            var mapped = new List<string>();
            foreach (var pair in GetFieldPairs(srcType, dstType))
            {
                mapped.Add(pair.src.Name);
            }

            var unmapped = new List<string>();
            foreach (var field in srcType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!mapped.Contains(field.Name))
                {
                    unmapped.Add(field.Name);
                }
            }
            return unmapped;
        }

        private static List<FieldPair> GetFieldPairs(Type srcType, Type dstType)
        {
            var key = new TypePair(srcType, dstType);
            List<FieldPair> pairs;
            if (_cache.TryGetValue(key, out pairs))
            {
                return pairs;
            }

            pairs = new List<FieldPair>();
            var flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (var srcField in srcType.GetFields(flags))
            {
                var dstField = dstType.GetField(srcField.Name, flags);
                if (dstField == null)
                {
                    continue;
                }
                // 別アセンブリの同名 struct (Color / Vector3 等) は同一型に解決されるが、
                // 型が食い違う場合は写すと例外になるため弾く
                if (!dstField.FieldType.IsAssignableFrom(srcField.FieldType))
                {
                    continue;
                }
                pairs.Add(new FieldPair { src = srcField, dst = dstField });
            }

            _cache[key] = pairs;
            return pairs;
        }
    }
}
```

- [ ] **Step 3: 同一内容を PostEffects.Plugin 側へコピーする**

```bash
SE=W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
PE=W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils
cp "$SE/PostEffectData.cs" "$PE/PostEffectData.cs"
cp "$SE/ReflectionFieldCopier.cs" "$PE/ReflectionFieldCopier.cs"
diff "$SE/PostEffectData.cs" "$PE/PostEffectData.cs" && diff "$SE/ReflectionFieldCopier.cs" "$PE/ReflectionFieldCopier.cs" && echo "同一"
```

Expected: `同一` が出力される。

- [ ] **Step 4: 両 csproj に `Compile` を追加する**

SceneEditor 側 `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="MTEUtils\ParallelHelper.cs" />` の近くへ:

```xml
    <Compile Include="MTEUtils\PostEffectData.cs" />
    <Compile Include="MTEUtils\ReflectionFieldCopier.cs" />
```

PostEffects 側 `COM3D25.PostEffects.Plugin.csproj` の `MTEUtils\` の `Compile` が並ぶ箇所へ同じ 2 行を追加する。

- [ ] **Step 5: 両リポジトリ・両構成をビルドする**

「ビルド・テストコマンド」の 4 本を実行。Expected: すべて Build succeeded。

- [ ] **Step 6: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils/PostEffectData.cs \
        source/COM3D25.PostEffects.Plugin/MTEUtils/ReflectionFieldCopier.cs \
        source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj
git commit -m "feat(mteutils): プラグイン間で共有するポストエフェクト DTO とフィールドコピーを追加する"

cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectData.cs \
        source/COM3D2.SceneEditor.Plugin/MTEUtils/ReflectionFieldCopier.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(mteutils): プラグイン間で共有するポストエフェクト DTO とフィールドコピーを追加する"
```

---

### Task 2: PostEffects.Plugin の TimelineBridge を DTO シグネチャへ差し替える

現行 `TimelineBridge` は自前の実体型（`ColorParaffinData` 等）を公開している。これを Task 1 の DTO 型に差し替え、内部で実体型と相互コピーする。

**Files:**
- Modify: `W:/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/TimelineBridge.cs`（全面書き換え）

**Interfaces:**
- Consumes: Task 1 の DTO 5 種、`ReflectionFieldCopier.Copy`、既存の `EffectSettings.instance`、`ColorParaffinEffectModel.MAX_PARAFFIN_COUNT` / `DistanceFogEffectModel.MAX_FOG_COUNT` / `RimlightEffectModel.MAX_RIMLIGHT_COUNT`（いずれも `static readonly int`、値 4）
- Produces（Task 3 の `PostEffectsClient` がリフレクションで解決する。**メソッド名・引数の個数と型を厳密に維持すること**）:
  - `static int GetMaxParaffinCount()` / `GetMaxDistanceFogCount()` / `GetMaxRimlightCount()`
  - `static int GetParaffinCount()` / `static void SetParaffinCount(int)`（距離フォグ・リムライトも同形）
  - `static bool GetParaffinEnabled()` / `static void SetParaffinEnabled(bool)`（同上）
  - `static PostEffects.ParaffinData GetParaffinData(int index)` / `static void ApplyParaffin(int index, PostEffects.ParaffinData data)`（同上）
  - `static PostEffects.GTToneMapData GetGTToneMap()` / `static void ApplyGTToneMap(PostEffects.GTToneMapData data)`
  - `static PostEffects.DepthOfFieldData GetDepthOfField()` / `static void ApplyDepthOfField(PostEffects.DepthOfFieldData data)`

**プロパティではなくメソッドにする理由**: `Delegate.CreateDelegate` でプロパティを直接束縛できず、`GetGetMethod()` を挟む必要があって解決コードが煩雑になるため。リフレクション越しの契約はメソッドに統一する。

- [ ] **Step 1: `TimelineBridge.cs` を全面書き換えする**

```csharp
using System;
using UnityEngine;
using COM3D2.MotionTimelineEditor;
using PEData = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D25.PostEffects.Plugin
{
    /// <summary>
    /// SceneEditor のタイムラインからポストエフェクトを駆動するための公開 API。
    /// SceneEditor はこのクラスをリフレクションで解決するため、
    /// **メソッド名・引数の個数と型を変えると連携が黙って切れる**。
    /// 値の受け渡しは MTEUtils の共有 DTO で行い、実体型との相互コピーはここで閉じる。
    /// 再生中は毎フレーム呼ばれるため、XML を挟まず設定値を直接読み書きする。
    /// 対象はタイムライン対応 5 系統 (DoF / GTToneMap / パラフィン / 距離フォグ / リムライト) のみ
    /// </summary>
    public static class TimelineBridge
    {
        private static EffectSettings settings => EffectSettings.instance;

        // 各系統の上限。実体側のシェーダーバッファ上限と同値
        public static int GetMaxParaffinCount()
        {
            return ColorParaffinEffectModel.MAX_PARAFFIN_COUNT;
        }

        public static int GetMaxDistanceFogCount()
        {
            return DistanceFogEffectModel.MAX_FOG_COUNT;
        }

        public static int GetMaxRimlightCount()
        {
            return RimlightEffectModel.MAX_RIMLIGHT_COUNT;
        }

        public static int GetParaffinCount()
        {
            return settings.paraffin.GetDataCount();
        }

        /// <summary>パラフィンのデータ数。上限で丸めて 1 件ずつ増減する</summary>
        public static void SetParaffinCount(int value)
        {
            ResizeData(
                settings.paraffin.GetDataCount(),
                Mathf.Clamp(value, 0, GetMaxParaffinCount()),
                () => settings.paraffin.AddData(new ColorParaffinData()),
                () => settings.paraffin.RemoveDataLast());
        }

        public static bool GetParaffinEnabled()
        {
            return settings.paraffin.enabled;
        }

        public static void SetParaffinEnabled(bool value)
        {
            settings.paraffin.enabled = value;
            settings.dirty = true;
        }

        public static PEData.ParaffinData GetParaffinData(int index)
        {
            var dto = new PEData.ParaffinData();
            ReflectionFieldCopier.Copy(settings.paraffin.GetData(index), dto);
            return dto;
        }

        public static void ApplyParaffin(int index, PEData.ParaffinData data)
        {
            // 個別データが有効なら系統ごと有効化する (SceneEditor 旧実装と同じ規約)
            if (data.enabled)
            {
                settings.paraffin.enabled = true;
            }
            var native = new ColorParaffinData();
            ReflectionFieldCopier.Copy(data, native);
            settings.paraffin.SetData(index, native);
            settings.dirty = true;
        }

        public static int GetDistanceFogCount()
        {
            return settings.distanceFog.GetDataCount();
        }

        /// <summary>距離フォグのデータ数。上限で丸めて 1 件ずつ増減する</summary>
        public static void SetDistanceFogCount(int value)
        {
            ResizeData(
                settings.distanceFog.GetDataCount(),
                Mathf.Clamp(value, 0, GetMaxDistanceFogCount()),
                () => settings.distanceFog.AddData(new DistanceFogData()),
                () => settings.distanceFog.RemoveDataLast());
        }

        public static bool GetDistanceFogEnabled()
        {
            return settings.distanceFog.enabled;
        }

        public static void SetDistanceFogEnabled(bool value)
        {
            settings.distanceFog.enabled = value;
            settings.dirty = true;
        }

        public static PEData.DistanceFogData GetDistanceFogData(int index)
        {
            var dto = new PEData.DistanceFogData();
            ReflectionFieldCopier.Copy(settings.distanceFog.GetData(index), dto);
            return dto;
        }

        public static void ApplyDistanceFog(int index, PEData.DistanceFogData data)
        {
            if (data.enabled)
            {
                settings.distanceFog.enabled = true;
            }
            var native = new DistanceFogData();
            ReflectionFieldCopier.Copy(data, native);
            settings.distanceFog.SetData(index, native);
            settings.dirty = true;
        }

        public static int GetRimlightCount()
        {
            return settings.rimlight.GetDataCount();
        }

        /// <summary>リムライトのデータ数。上限で丸めて 1 件ずつ増減する</summary>
        public static void SetRimlightCount(int value)
        {
            ResizeData(
                settings.rimlight.GetDataCount(),
                Mathf.Clamp(value, 0, GetMaxRimlightCount()),
                () => settings.rimlight.AddData(new RimlightData()),
                () => settings.rimlight.RemoveDataLast());
        }

        public static bool GetRimlightEnabled()
        {
            return settings.rimlight.enabled;
        }

        public static void SetRimlightEnabled(bool value)
        {
            settings.rimlight.enabled = value;
            settings.dirty = true;
        }

        public static PEData.RimlightData GetRimlightData(int index)
        {
            var dto = new PEData.RimlightData();
            ReflectionFieldCopier.Copy(settings.rimlight.GetData(index), dto);
            return dto;
        }

        public static void ApplyRimlight(int index, PEData.RimlightData data)
        {
            if (data.enabled)
            {
                settings.rimlight.enabled = true;
            }
            var native = new RimlightData();
            ReflectionFieldCopier.Copy(data, native);
            settings.rimlight.SetData(index, native);
            settings.dirty = true;
        }

        public static PEData.GTToneMapData GetGTToneMap()
        {
            var dto = new PEData.GTToneMapData();
            ReflectionFieldCopier.Copy(settings.gtToneMap, dto);
            return dto;
        }

        public static void ApplyGTToneMap(PEData.GTToneMapData data)
        {
            // 実体は差し替えず、既存インスタンスへ写す
            // (他のコントローラが同じ参照を握っているため)
            ReflectionFieldCopier.Copy(data, settings.gtToneMap);
            settings.dirty = true;
        }

        public static PEData.DepthOfFieldData GetDepthOfField()
        {
            var dto = new PEData.DepthOfFieldData();
            ReflectionFieldCopier.Copy(settings.depthOfField, dto);
            return dto;
        }

        public static void ApplyDepthOfField(PEData.DepthOfFieldData data)
        {
            // DTO は DX11 ボケ等の項目を持たないため、写らない項目は実体側の値が残る
            ReflectionFieldCopier.Copy(data, settings.depthOfField);
            settings.dirty = true;
        }

        /// <summary>データ数を target へ寄せる。増減どちらも 1 件ずつで、書き込み後は dirty を立てる</summary>
        private static void ResizeData(
            int current, int target,
            Action addOne, Action removeLast)
        {
            if (current == target)
            {
                return;
            }
            while (current < target)
            {
                addOne();
                current++;
            }
            while (current > target)
            {
                removeLast();
                current--;
            }
            settings.dirty = true;
        }
    }
}
```

注意: `GetParaffinData(index)` は `settings.paraffin.GetData(index)` が範囲外で `null` を返す。`ReflectionFieldCopier.Copy` は `src == null` で何もせず戻るため、範囲外では既定値の DTO が返る（例外は出ない）。SceneEditor 側の呼び出しはインデックスが必ず範囲内である前提で書かれているため、これで従来挙動と差は出ない。

- [ ] **Step 2: 両構成をビルドする**

PostEffects 側 MSBuild 2 本。Expected: 両方 Build succeeded。

- [ ] **Step 3: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/TimelineBridge.cs
git commit -m "refactor(bridge): TimelineBridge の公開契約を共有 DTO とメソッドへ揃える"
```

---

### Task 3: SceneEditor に PostEffectsClient（リフレクションクライアント）を追加する

既存 `MTEUtils/ModelProviderClient.cs` と同じ作法で、`COM3D25.PostEffects.Plugin.TimelineBridge` へ接続する。**ホスト型が見つからない間は再試行し続ける**のがロード順対策の要。

**Files:**
- Create: `W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectsClient.cs`
- Modify: `W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Compile` 追加）

**Interfaces:**
- Consumes: Task 2 の `TimelineBridge` の全メソッド（文字列で解決）、Task 1 の DTO と `ReflectionFieldCopier`
- Produces（Task 4-6 が消費。すべて `COM3D2.MotionTimelineEditor.PostEffectsClient` の static メンバ）:
  - `bool isAvailable`
  - `int maxParaffinCount` / `maxDistanceFogCount` / `maxRimlightCount`
  - `int paraffinCount { get; set; }` / `distanceFogCount` / `rimlightCount`
  - `bool paraffinEnabled { get; set; }` / `distanceFogEnabled` / `rimlightEnabled`
  - `PostEffects.ParaffinData GetParaffinData(int)` / `void ApplyParaffin(int, PostEffects.ParaffinData)`（フォグ・リムライト同形）
  - `PostEffects.GTToneMapData GetGTToneMap()` / `void ApplyGTToneMap(PostEffects.GTToneMapData)`
  - `PostEffects.DepthOfFieldData GetDepthOfField()` / `void ApplyDepthOfField(PostEffects.DepthOfFieldData)`
  - `isAvailable == false` のとき、getter は既定値（数値 0 / bool false / 新規 DTO）を返し、setter と Apply は何もしない

- [ ] **Step 1: `MTEUtils/PostEffectsClient.cs` を作成する**

`Apply*` はホスト側の DTO 型（別アセンブリの同名型）を引数に取るため、`Delegate.CreateDelegate` では束縛できない（デリゲートの引数型 `object` はホストの引数型より派生していないため）。**Apply 系のみ `MethodInfo.Invoke` を使う**。Get 系と数値・bool 系は戻り値の参照変換が許されるため `Delegate.CreateDelegate` で束縛する。

```csharp
using System;
using System.Reflection;
using PEData = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// PostEffects.Plugin の TimelineBridge へのリフレクションブリッジ。
    ///
    /// UnityInjector はファイル名順にプラグインをロードするため
    /// SceneEditor が PostEffects より先に来る。コンパイル時参照にすると
    /// ロード時の型解決に失敗し、Mono はその束縛失敗をプロセス寿命の間
    /// キャッシュするので後から復帰できない。そのため参照は持たず、
    /// ホスト型が見つかるまで毎回探し直す (ModelProviderClient と同じ作法)。
    ///
    /// 値は MTEUtils の共有 DTO で受け渡し、ホスト側 DTO との相互変換は
    /// ReflectionFieldCopier が担う
    /// </summary>
    public static class PostEffectsClient
    {
        private const string HostAssemblyName = "COM3D25.PostEffects.Plugin";
        private const string HostTypeName = "COM3D25.PostEffects.Plugin.TimelineBridge";

        private static bool _initialized;

        private static Func<int> _getMaxParaffinCount;
        private static Func<int> _getMaxDistanceFogCount;
        private static Func<int> _getMaxRimlightCount;

        private static Func<int> _getParaffinCount;
        private static Action<int> _setParaffinCount;
        private static Func<bool> _getParaffinEnabled;
        private static Action<bool> _setParaffinEnabled;
        private static Func<int, object> _getParaffinData;
        private static MethodInfo _applyParaffin;

        private static Func<int> _getDistanceFogCount;
        private static Action<int> _setDistanceFogCount;
        private static Func<bool> _getDistanceFogEnabled;
        private static Action<bool> _setDistanceFogEnabled;
        private static Func<int, object> _getDistanceFogData;
        private static MethodInfo _applyDistanceFog;

        private static Func<int> _getRimlightCount;
        private static Action<int> _setRimlightCount;
        private static Func<bool> _getRimlightEnabled;
        private static Action<bool> _setRimlightEnabled;
        private static Func<int, object> _getRimlightData;
        private static MethodInfo _applyRimlight;

        private static Func<object> _getGTToneMap;
        private static MethodInfo _applyGTToneMap;
        private static Func<object> _getDepthOfField;
        private static MethodInfo _applyDepthOfField;

        // Apply 系へ渡すホスト側 DTO のインスタンスを使い回す (毎フレーム生成しない)
        private static object _paraffinArg;
        private static object _distanceFogArg;
        private static object _rimlightArg;
        private static object _gtToneMapArg;
        private static object _depthOfFieldArg;

        // MethodInfo.Invoke 用の引数配列も使い回す
        private static readonly object[] _args1 = new object[1];
        private static readonly object[] _args2 = new object[2];

        /// <summary>PostEffects.Plugin と接続できているか。未ロードの間は false を返し続ける</summary>
        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _getParaffinCount != null;
            }
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            // ロード順によってはホストのアセンブリが未登場のことがあるため、
            // 型が見つかるまでは _initialized を立てずに再試行を続ける
            var type = FindHostType();
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                _getMaxParaffinCount = CreateFuncInt(type, "GetMaxParaffinCount");
                _getMaxDistanceFogCount = CreateFuncInt(type, "GetMaxDistanceFogCount");
                _getMaxRimlightCount = CreateFuncInt(type, "GetMaxRimlightCount");

                _getParaffinCount = CreateFuncInt(type, "GetParaffinCount");
                _setParaffinCount = CreateActionInt(type, "SetParaffinCount");
                _getParaffinEnabled = CreateFuncBool(type, "GetParaffinEnabled");
                _setParaffinEnabled = CreateActionBool(type, "SetParaffinEnabled");
                _getParaffinData = CreateFuncIntObject(type, "GetParaffinData");
                _applyParaffin = type.GetMethod("ApplyParaffin", BindingFlags.Public | BindingFlags.Static);

                _getDistanceFogCount = CreateFuncInt(type, "GetDistanceFogCount");
                _setDistanceFogCount = CreateActionInt(type, "SetDistanceFogCount");
                _getDistanceFogEnabled = CreateFuncBool(type, "GetDistanceFogEnabled");
                _setDistanceFogEnabled = CreateActionBool(type, "SetDistanceFogEnabled");
                _getDistanceFogData = CreateFuncIntObject(type, "GetDistanceFogData");
                _applyDistanceFog = type.GetMethod("ApplyDistanceFog", BindingFlags.Public | BindingFlags.Static);

                _getRimlightCount = CreateFuncInt(type, "GetRimlightCount");
                _setRimlightCount = CreateActionInt(type, "SetRimlightCount");
                _getRimlightEnabled = CreateFuncBool(type, "GetRimlightEnabled");
                _setRimlightEnabled = CreateActionBool(type, "SetRimlightEnabled");
                _getRimlightData = CreateFuncIntObject(type, "GetRimlightData");
                _applyRimlight = type.GetMethod("ApplyRimlight", BindingFlags.Public | BindingFlags.Static);

                _getGTToneMap = CreateFuncObject(type, "GetGTToneMap");
                _applyGTToneMap = type.GetMethod("ApplyGTToneMap", BindingFlags.Public | BindingFlags.Static);
                _getDepthOfField = CreateFuncObject(type, "GetDepthOfField");
                _applyDepthOfField = type.GetMethod("ApplyDepthOfField", BindingFlags.Public | BindingFlags.Static);

                if (_getMaxParaffinCount == null || _getMaxDistanceFogCount == null ||
                    _getMaxRimlightCount == null ||
                    _getParaffinCount == null || _setParaffinCount == null ||
                    _getParaffinEnabled == null || _setParaffinEnabled == null ||
                    _getParaffinData == null || _applyParaffin == null ||
                    _getDistanceFogCount == null || _setDistanceFogCount == null ||
                    _getDistanceFogEnabled == null || _setDistanceFogEnabled == null ||
                    _getDistanceFogData == null || _applyDistanceFog == null ||
                    _getRimlightCount == null || _setRimlightCount == null ||
                    _getRimlightEnabled == null || _setRimlightEnabled == null ||
                    _getRimlightData == null || _applyRimlight == null ||
                    _getGTToneMap == null || _applyGTToneMap == null ||
                    _getDepthOfField == null || _applyDepthOfField == null)
                {
                    MTEUtils.LogWarning(
                        "PostEffectsClient: TimelineBridge にシグネチャの一致するメソッドが見つかりませんでした");
                    Disable();
                    return;
                }

                // Apply 系へ渡すホスト側 DTO を 1 個ずつ確保して使い回す
                _paraffinArg = Activator.CreateInstance(_applyParaffin.GetParameters()[1].ParameterType);
                _distanceFogArg = Activator.CreateInstance(_applyDistanceFog.GetParameters()[1].ParameterType);
                _rimlightArg = Activator.CreateInstance(_applyRimlight.GetParameters()[1].ParameterType);
                _gtToneMapArg = Activator.CreateInstance(_applyGTToneMap.GetParameters()[0].ParameterType);
                _depthOfFieldArg = Activator.CreateInstance(_applyDepthOfField.GetParameters()[0].ParameterType);

                WarnUnmappedFields("パラフィン", typeof(PEData.ParaffinData), _paraffinArg);
                WarnUnmappedFields("距離フォグ", typeof(PEData.DistanceFogData), _distanceFogArg);
                WarnUnmappedFields("リムライト", typeof(PEData.RimlightData), _rimlightArg);
                WarnUnmappedFields("GTトーンマップ", typeof(PEData.GTToneMapData), _gtToneMapArg);
                WarnUnmappedFields("被写界深度", typeof(PEData.DepthOfFieldData), _depthOfFieldArg);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning(
                    "PostEffectsClient: TimelineBridge との接続に失敗しました: " + e.Message);
                Disable();
            }
        }

        /// <summary>
        /// 共有 DTO とホスト側 DTO でフィールド名がずれていないか接続時に 1 回だけ検査する。
        /// ずれた値は例外を出さず既定値のまま素通りするため、ログでしか気付けない
        /// </summary>
        private static void WarnUnmappedFields(string label, Type shared, object hostArg)
        {
            var unmapped = ReflectionFieldCopier.FindUnmappedFields(shared, hostArg.GetType());
            if (unmapped.Count == 0)
            {
                return;
            }
            MTEUtils.LogWarning(
                "PostEffectsClient: {0} の共有 DTO に、PostEffects.Plugin 側と対応しないフィールドがあります " +
                "(この値は反映されません): {1}",
                label, string.Join(", ", unmapped.ToArray()));
        }

        private static void Disable()
        {
            // isAvailable の判定に使う 1 個を落とせば全体が無効になる
            _getParaffinCount = null;
        }

        /// <summary>
        /// PostEffects.Plugin 内の TimelineBridge を解決する。
        /// ロードが自分より後の場合は Type.GetType が null を返すため、
        /// AppDomain の読み込み済みアセンブリからも探す
        /// </summary>
        private static Type FindHostType()
        {
            var type = Type.GetType(HostTypeName + ", " + HostAssemblyName);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != HostAssemblyName)
                {
                    continue;
                }
                type = assembly.GetType(HostTypeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Func<int> CreateFuncInt(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), method);
        }

        private static Func<bool> CreateFuncBool(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
        }

        private static Action<int> CreateActionInt(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), method);
        }

        private static Action<bool> CreateActionBool(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), method);
        }

        // 戻り値は参照型なので object へのデリゲート束縛 (共変) が使える
        private static Func<object> CreateFuncObject(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), method);
        }

        private static Func<int, object> CreateFuncIntObject(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method == null
                ? null
                : (Func<int, object>)Delegate.CreateDelegate(typeof(Func<int, object>), method);
        }

        public static int maxParaffinCount => isAvailable ? _getMaxParaffinCount() : 0;
        public static int maxDistanceFogCount => isAvailable ? _getMaxDistanceFogCount() : 0;
        public static int maxRimlightCount => isAvailable ? _getMaxRimlightCount() : 0;

        public static int paraffinCount
        {
            get => isAvailable ? _getParaffinCount() : 0;
            set { if (isAvailable) _setParaffinCount(value); }
        }

        public static int distanceFogCount
        {
            get => isAvailable ? _getDistanceFogCount() : 0;
            set { if (isAvailable) _setDistanceFogCount(value); }
        }

        public static int rimlightCount
        {
            get => isAvailable ? _getRimlightCount() : 0;
            set { if (isAvailable) _setRimlightCount(value); }
        }

        public static bool paraffinEnabled
        {
            get => isAvailable && _getParaffinEnabled();
            set { if (isAvailable) _setParaffinEnabled(value); }
        }

        public static bool distanceFogEnabled
        {
            get => isAvailable && _getDistanceFogEnabled();
            set { if (isAvailable) _setDistanceFogEnabled(value); }
        }

        public static bool rimlightEnabled
        {
            get => isAvailable && _getRimlightEnabled();
            set { if (isAvailable) _setRimlightEnabled(value); }
        }

        public static PEData.ParaffinData GetParaffinData(int index)
        {
            var dto = new PEData.ParaffinData();
            if (isAvailable)
            {
                ReflectionFieldCopier.Copy(_getParaffinData(index), dto);
            }
            return dto;
        }

        public static void ApplyParaffin(int index, PEData.ParaffinData data)
        {
            if (!isAvailable)
            {
                return;
            }
            ReflectionFieldCopier.Copy(data, _paraffinArg);
            _args2[0] = index;
            _args2[1] = _paraffinArg;
            _applyParaffin.Invoke(null, _args2);
        }

        public static PEData.DistanceFogData GetDistanceFogData(int index)
        {
            var dto = new PEData.DistanceFogData();
            if (isAvailable)
            {
                ReflectionFieldCopier.Copy(_getDistanceFogData(index), dto);
            }
            return dto;
        }

        public static void ApplyDistanceFog(int index, PEData.DistanceFogData data)
        {
            if (!isAvailable)
            {
                return;
            }
            ReflectionFieldCopier.Copy(data, _distanceFogArg);
            _args2[0] = index;
            _args2[1] = _distanceFogArg;
            _applyDistanceFog.Invoke(null, _args2);
        }

        public static PEData.RimlightData GetRimlightData(int index)
        {
            var dto = new PEData.RimlightData();
            if (isAvailable)
            {
                ReflectionFieldCopier.Copy(_getRimlightData(index), dto);
            }
            return dto;
        }

        public static void ApplyRimlight(int index, PEData.RimlightData data)
        {
            if (!isAvailable)
            {
                return;
            }
            ReflectionFieldCopier.Copy(data, _rimlightArg);
            _args2[0] = index;
            _args2[1] = _rimlightArg;
            _applyRimlight.Invoke(null, _args2);
        }

        public static PEData.GTToneMapData GetGTToneMap()
        {
            var dto = new PEData.GTToneMapData();
            if (isAvailable)
            {
                ReflectionFieldCopier.Copy(_getGTToneMap(), dto);
            }
            return dto;
        }

        public static void ApplyGTToneMap(PEData.GTToneMapData data)
        {
            if (!isAvailable)
            {
                return;
            }
            ReflectionFieldCopier.Copy(data, _gtToneMapArg);
            _args1[0] = _gtToneMapArg;
            _applyGTToneMap.Invoke(null, _args1);
        }

        public static PEData.DepthOfFieldData GetDepthOfField()
        {
            var dto = new PEData.DepthOfFieldData();
            if (isAvailable)
            {
                ReflectionFieldCopier.Copy(_getDepthOfField(), dto);
            }
            return dto;
        }

        public static void ApplyDepthOfField(PEData.DepthOfFieldData data)
        {
            if (!isAvailable)
            {
                return;
            }
            ReflectionFieldCopier.Copy(data, _depthOfFieldArg);
            _args1[0] = _depthOfFieldArg;
            _applyDepthOfField.Invoke(null, _args1);
        }
    }
}
```

- [ ] **Step 2: ホスト呼び出しを try/catch で隔離する**

上の `Get*` / `Apply*` は接続成立後のホスト側実行時例外を素通しする。`TimelineUpdateManager.Update()` はサブマネージャの `foreach` に個別 `try/catch` を持たないため、1 つ投げると**そのフレームの他マネージャの更新まで巻き添えで止まる**（最終的には `ManagerRegistry` が握り潰す）。手本の `ModelProviderClient` も実呼び出しを `try/catch` で囲んでいるので、それに揃える。

10 個の公開メソッド（`GetParaffinData` / `ApplyParaffin` / `GetDistanceFogData` / `ApplyDistanceFog` / `GetRimlightData` / `ApplyRimlight` / `GetGTToneMap` / `ApplyGTToneMap` / `GetDepthOfField` / `ApplyDepthOfField`）と 6 個のプロパティ（`paraffinCount` / `distanceFogCount` / `rimlightCount` / `paraffinEnabled` / `distanceFogEnabled` / `rimlightEnabled`）の**ホストへ触れる部分**を、次のヘルパ経由にする。

```csharp
        // 毎フレーム走るパスなので、例外が出たときだけ 1 回ログして以後は黙らせる
        private static bool _errorLogged;

        private static void LogHostError(string member, Exception e)
        {
            if (_errorLogged)
            {
                return;
            }
            _errorLogged = true;
            MTEUtils.LogWarning(
                "PostEffectsClient: {0} の呼び出しで例外が発生しました (以後この警告は出しません): {1}",
                member, e.Message);
        }
```

適用例（他も同形。`catch` の中では DTO / 既定値をそのまま返し、呼び出し元へ例外を伝播させない）:

```csharp
        public static PEData.ParaffinData GetParaffinData(int index)
        {
            var dto = new PEData.ParaffinData();
            if (!isAvailable)
            {
                return dto;
            }
            try
            {
                ReflectionFieldCopier.Copy(_getParaffinData(index), dto);
            }
            catch (Exception e)
            {
                LogHostError("GetParaffinData", e);
            }
            return dto;
        }

        public static void ApplyParaffin(int index, PEData.ParaffinData data)
        {
            if (!isAvailable)
            {
                return;
            }
            try
            {
                ReflectionFieldCopier.Copy(data, _paraffinArg);
                _args2[0] = index;
                _args2[1] = _paraffinArg;
                _applyParaffin.Invoke(null, _args2);
            }
            catch (Exception e)
            {
                LogHostError("ApplyParaffin", e);
            }
        }

        public static int paraffinCount
        {
            get
            {
                if (!isAvailable)
                {
                    return 0;
                }
                try { return _getParaffinCount(); }
                catch (Exception e) { LogHostError("GetParaffinCount", e); return 0; }
            }
            set
            {
                if (!isAvailable)
                {
                    return;
                }
                try { _setParaffinCount(value); }
                catch (Exception e) { LogHostError("SetParaffinCount", e); }
            }
        }
```

`maxParaffinCount` / `maxDistanceFogCount` / `maxRimlightCount` も同形にする。

- [ ] **Step 3: csproj に `Compile` を追加する**

```xml
    <Compile Include="MTEUtils\PostEffectsClient.cs" />
```

- [ ] **Step 4: 両構成をビルドする**

SceneEditor 側 MSBuild 2 本。この時点ではまだ `extern alias` 版の `PostEffectsBridge` が残っており、csproj の `Reference` も残っているため、ビルドは通る。Expected: 両方 Build succeeded。

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectsClient.cs \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(mteutils): PostEffects.Plugin へのリフレクションクライアントを追加する"
```

---

### Task 4: SceneEditor のタイムライン層を共有 DTO へ切り替える

`PostEffectManager` を `PostEffectsClient` 委譲へ差し替え、`PEP.*` 型を使っている全ファイルを共有 DTO へ切り替える。`extern alias` と csproj の `Reference` を撤去する。**この Task の完了時点でコンパイル時参照がゼロになる**。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectsBridge.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataParaffin.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataDistanceFog.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataRimlight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer_Parrifin.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer_DistanceFog.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer_Rimlight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Reference` 撤去）

**Interfaces:**
- Consumes: Task 3 の `PostEffectsClient` 全メンバ、Task 1 の DTO
- Produces（既存呼び出し元が引き続き使う。**戻り値の型だけ `PEP.*` → `PostEffects.*` に変わる**）:
  - `PostEffectManager.instance`、`paraffinCount / distanceFogCount / rimlightCount { get; set; }`
  - `MaxParaffinCount / MaxDistanceFogCount / MaxRimlightCount`（static プロパティ）
  - `paraffinEnabled / distanceFogEnabled / rimlightEnabled { get; set; }`
  - `PostEffects.ParaffinData GetParaffinData(int)` / `void ApplyParaffin(int, PostEffects.ParaffinData)`（フォグ・リムライト同形）
  - `DepthOfFieldData GetDepthOfFieldData()` / `void ApplyDepthOfField(DepthOfFieldData)`（`MTEP.DepthOfFieldData` を維持）
  - `GTToneMapData GetGTToneMapData()` / `void ApplyGTToneMap(GTToneMapData)`（`MTEP.GTToneMapData` を維持）
  - `InitPostEffects()` / `DisableAllEffects()`
  - `PostEffectsBridge.isAvailable`

**`MTEP.GTToneMapData` / `MTEP.DepthOfFieldData` を残す理由**: `TransformDataGTToneMap` / `TransformDataDepthOfField` と補間コード（`Lerp` / `Equals` / `Create`）が既にこれらに依存しており、共有 DTO に寄せると補間 API まで MTEUtils へ持ち込むことになる。境界の変換は `PostEffectManager` 内に閉じる。

- [ ] **Step 1: `PostEffectManager.cs` を書き換える**

冒頭 3 行（`extern alias` とコメント、`using PEP = ...`）を削除し、`using PEData = COM3D2.MotionTimelineEditor.PostEffects;` を足す。`PEP.TimelineBridge.xxx` を `PostEffectsClient.xxx` へ、`PEP.ColorParaffinData` を `PEData.ParaffinData` へ、`PEP.DistanceFogData` を `PEData.DistanceFogData` へ、`PEP.RimlightData` を `PEData.RimlightData` へ置換する。

対応表（現行 → 新）:

| 現行 | 新 |
| --- | --- |
| `PEP.TimelineBridge.MaxParaffinCount` | `PostEffectsClient.maxParaffinCount` |
| `PEP.TimelineBridge.MaxDistanceFogCount` | `PostEffectsClient.maxDistanceFogCount` |
| `PEP.TimelineBridge.MaxRimlightCount` | `PostEffectsClient.maxRimlightCount` |
| `PEP.TimelineBridge.paraffinCount` | `PostEffectsClient.paraffinCount` |
| `PEP.TimelineBridge.distanceFogCount` | `PostEffectsClient.distanceFogCount` |
| `PEP.TimelineBridge.rimlightCount` | `PostEffectsClient.rimlightCount` |
| `PEP.TimelineBridge.paraffinEnabled` | `PostEffectsClient.paraffinEnabled` |
| `PEP.TimelineBridge.distanceFogEnabled` | `PostEffectsClient.distanceFogEnabled` |
| `PEP.TimelineBridge.rimlightEnabled` | `PostEffectsClient.rimlightEnabled` |
| `PEP.TimelineBridge.GetParaffinData(i)` | `PostEffectsClient.GetParaffinData(i)` |
| `PEP.TimelineBridge.ApplyParaffin(i, d)` | `PostEffectsClient.ApplyParaffin(i, d)` |
| `PEP.TimelineBridge.GetDistanceFogData(i)` | `PostEffectsClient.GetDistanceFogData(i)` |
| `PEP.TimelineBridge.ApplyDistanceFog(i, d)` | `PostEffectsClient.ApplyDistanceFog(i, d)` |
| `PEP.TimelineBridge.GetRimlightData(i)` | `PostEffectsClient.GetRimlightData(i)` |
| `PEP.TimelineBridge.ApplyRimlight(i, d)` | `PostEffectsClient.ApplyRimlight(i, d)` |
| `PEP.TimelineBridge.GetGTToneMap()` | `PostEffectsClient.GetGTToneMap()` |
| `PEP.TimelineBridge.ApplyGTToneMap(s)` | `PostEffectsClient.ApplyGTToneMap(s)` |
| `PEP.TimelineBridge.GetDepthOfField()` | `PostEffectsClient.GetDepthOfField()` |
| `PEP.TimelineBridge.ApplyDepthOfField(s)` | `PostEffectsClient.ApplyDepthOfField(s)` |

あわせて以下 3 点を直す:

1. 上限プロパティは `PostEffectsClient` 経由に変える:

```csharp
        // 各エフェクト数の上限。実体側 (PostEffects.Plugin) のシェーダーバッファ上限に従う。
        // 未接続時は 0 になるので、UI 側は isAvailable でゲートしてから参照すること
        public static int MaxParaffinCount => PostEffectsClient.maxParaffinCount;
        public static int MaxDistanceFogCount => PostEffectsClient.maxDistanceFogCount;
        public static int MaxRimlightCount => PostEffectsClient.maxRimlightCount;
```

2. `GetDepthOfFieldData` / `ApplyDepthOfField` は共有 DTO と `MTEP.DepthOfFieldData` の間で詰め替える:

```csharp
        public DepthOfFieldData GetDepthOfFieldData()
        {
            var setting = PostEffectsClient.GetDepthOfField();
            return new DepthOfFieldData
            {
                enabled = setting.enabled,
                focalLength = setting.focalLength,
                focalSize = setting.focalSize,
                aperture = setting.aperture,
                maxBlurSize = setting.maxBlurSize,
                // メイド追従は共有 DTO 側で maidFocus + maidIndex に分かれている
                maidSlotNo = setting.maidFocus ? setting.maidIndex : -1,
            };
        }

        public void ApplyDepthOfField(DepthOfFieldData data)
        {
            var setting = PostEffectsClient.GetDepthOfField();
            setting.enabled = data.enabled;
            setting.focalLength = data.focalLength;
            setting.focalSize = data.focalSize;
            setting.aperture = data.aperture;
            setting.maxBlurSize = data.maxBlurSize;
            setting.maidFocus = data.maidSlotNo >= 0;
            setting.maidIndex = data.maidSlotNo >= 0 ? data.maidSlotNo : 0;
            PostEffectsClient.ApplyDepthOfField(setting);
            depthOfFieldMaidSlotId = data.maidSlotNo;

            studioHack.OnUpdateDepthOfField();
        }
```

3. `GetGTToneMapData` / `ApplyGTToneMap` も同様に詰め替える（フィールド名は 1:1）:

```csharp
        public GTToneMapData GetGTToneMapData()
        {
            var setting = PostEffectsClient.GetGTToneMap();
            return new GTToneMapData
            {
                enabled = setting.enabled,
                maxBrightness = setting.maxBrightness,
                contrast = setting.contrast,
                linearStart = setting.linearStart,
                linearLength = setting.linearLength,
                blackTightness = setting.blackTightness,
                blackOffset = setting.blackOffset,
            };
        }

        public void ApplyGTToneMap(GTToneMapData data)
        {
            var setting = PostEffectsClient.GetGTToneMap();
            setting.enabled = data.enabled;
            setting.maxBrightness = data.maxBrightness;
            setting.contrast = data.contrast;
            setting.linearStart = data.linearStart;
            setting.linearLength = data.linearLength;
            setting.blackTightness = data.blackTightness;
            setting.blackOffset = data.blackOffset;
            PostEffectsClient.ApplyGTToneMap(setting);
        }
```

- [ ] **Step 2: `PostEffectsBridge.cs` を薄い別名へ置き換える**

型ロード隔離はもう不要（コンパイル時参照が無くなるため）。既存の呼び出し元（`TimelineIntegration` / `TimelineSettingWindow`）を変えずに済むよう、名前だけ残して `PostEffectsClient` へ委譲する。

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// PostEffects.Plugin の導入判定。実体は MTEUtils の PostEffectsClient にあり、
    /// ここはタイムライン層から呼びやすくするための別名。
    /// PostEffects.Plugin のロードが SceneEditor より後になるため、
    /// false のまま確定させず毎回問い合わせる (接続できた時点で true に変わる)
    /// </summary>
    public static class PostEffectsBridge
    {
        public static bool isAvailable => PostEffectsClient.isAvailable;
    }
}
```

`using COM3D2.MotionTimelineEditor;` が必要なら足す（同ファイル内で `PostEffectsClient` を解決するため）。

- [ ] **Step 3: TransformData / TimelineLayer の 6 ファイルを共有 DTO へ切り替える**

各ファイル冒頭の以下 2 行を削除する:

```csharp
// ポストエフェクトの値クラスは PostEffects.Plugin 側の実体を使う。alias の理由は PostEffectsBridge を参照
extern alias PostEffectsPlugin;
```

`using` 行の `using PEP = PostEffectsPlugin::COM3D25.PostEffects.Plugin;` を次へ差し替える:

```csharp
using PEP = COM3D2.MotionTimelineEditor.PostEffects;
```

型名の対応（エイリアス名 `PEP` を据え置くので、本文の書き換えはこの 3 つだけ）:

| 現行 | 新 |
| --- | --- |
| `PEP.ColorParaffinData` | `PEP.ParaffinData` |
| `PEP.DistanceFogData` | `PEP.DistanceFogData`（変更なし） |
| `PEP.RimlightData` | `PEP.RimlightData`（変更なし） |

対象ファイルと `PEP.ColorParaffinData` の出現箇所:

- `TransformDataParaffin.cs`: `public PEP.ColorParaffinData paraffin` の宣言と `get => new PEP.ColorParaffinData` の 2 箇所
- `PostEffectTimelineLayer_Parrifin.cs`: `PEP.ColorParaffinData.Lerp(...)` の 1 箇所 → **`Lerp` は共有 DTO に無いため Step 4 で対応する**
- 残り 4 ファイル（`TransformDataDistanceFog.cs` / `TransformDataRimlight.cs` / `PostEffectTimelineLayer_DistanceFog.cs` / `PostEffectTimelineLayer_Rimlight.cs`）は型名変更なし

- [ ] **Step 4: 補間 (`Lerp`) を共有 DTO へ追加する**

3 つの `PostEffectTimelineLayer_*.cs` が `PEP.ColorParaffinData.Lerp` / `PEP.DistanceFogData.Lerp` / `PEP.RimlightData.Lerp` を呼んでいる。共有 DTO は素のデータクラスなので `Lerp` を持たない。**MTEUtils の `PostEffectData.cs` に static メソッドとして追加する**（両リポジトリへ同一内容でコピーすること）。

`MTEUtils/PostEffectData.cs` の末尾（`namespace` の内側）へ追加:

```csharp
    /// <summary>ポストエフェクト DTO のキーフレーム間補間。enabled 等の非連続値は start 側を採る</summary>
    public static class PostEffectDataLerp
    {
        public static ParaffinData Lerp(ParaffinData a, ParaffinData b, float t)
        {
            return new ParaffinData
            {
                enabled = a.enabled,
                color1 = Color.Lerp(a.color1, b.color1, t),
                color2 = Color.Lerp(a.color2, b.color2, t),
                centerPosition = Vector2.Lerp(a.centerPosition, b.centerPosition, t),
                radiusFar = Mathf.Lerp(a.radiusFar, b.radiusFar, t),
                radiusNear = Mathf.Lerp(a.radiusNear, b.radiusNear, t),
                radiusScale = Vector2.Lerp(a.radiusScale, b.radiusScale, t),
                maskMode = a.maskMode,
                useNormal = Mathf.Lerp(a.useNormal, b.useNormal, t),
                useAdd = Mathf.Lerp(a.useAdd, b.useAdd, t),
                useMultiply = Mathf.Lerp(a.useMultiply, b.useMultiply, t),
                useOverlay = Mathf.Lerp(a.useOverlay, b.useOverlay, t),
                useSubstruct = Mathf.Lerp(a.useSubstruct, b.useSubstruct, t),
            };
        }

        public static DistanceFogData Lerp(DistanceFogData a, DistanceFogData b, float t)
        {
            return new DistanceFogData
            {
                enabled = a.enabled,
                color1 = Color.Lerp(a.color1, b.color1, t),
                color2 = Color.Lerp(a.color2, b.color2, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t),
                fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
                fogExp = Mathf.Lerp(a.fogExp, b.fogExp, t),
                useNormal = Mathf.Lerp(a.useNormal, b.useNormal, t),
                useAdd = Mathf.Lerp(a.useAdd, b.useAdd, t),
                useMultiply = Mathf.Lerp(a.useMultiply, b.useMultiply, t),
                useOverlay = Mathf.Lerp(a.useOverlay, b.useOverlay, t),
                useSubstruct = Mathf.Lerp(a.useSubstruct, b.useSubstruct, t),
            };
        }

        public static RimlightData Lerp(RimlightData a, RimlightData b, float t)
        {
            return new RimlightData
            {
                enabled = a.enabled,
                color1 = Color.Lerp(a.color1, b.color1, t),
                color2 = Color.Lerp(a.color2, b.color2, t),
                rotation = Vector3.Lerp(a.rotation, b.rotation, t),
                lightArea = Mathf.Lerp(a.lightArea, b.lightArea, t),
                fadeRange = Mathf.Lerp(a.fadeRange, b.fadeRange, t),
                fadeExp = Mathf.Lerp(a.fadeExp, b.fadeExp, t),
                useNormal = Mathf.Lerp(a.useNormal, b.useNormal, t),
                useAdd = Mathf.Lerp(a.useAdd, b.useAdd, t),
                useMultiply = Mathf.Lerp(a.useMultiply, b.useMultiply, t),
                useOverlay = Mathf.Lerp(a.useOverlay, b.useOverlay, t),
                useSubstruct = Mathf.Lerp(a.useSubstruct, b.useSubstruct, t),
                isWorldSpace = a.isWorldSpace,
                excludeFace = a.excludeFace,
                applyHair = a.applyHair,
                maskMode = a.maskMode,
            };
        }
    }
```

呼び出し側 3 ファイルの置換:

| 現行 | 新 |
| --- | --- |
| `PEP.ColorParaffinData.Lerp(start.paraffin, end.paraffin, lerpTime)` | `PEP.PostEffectDataLerp.Lerp(start.paraffin, end.paraffin, lerpTime)` |
| `PEP.DistanceFogData.Lerp(start.distanceFog, end.distanceFog, lerpTime)` | `PEP.PostEffectDataLerp.Lerp(start.distanceFog, end.distanceFog, lerpTime)` |
| `PEP.RimlightData.Lerp(start.rimlight, end.rimlight, lerpTime)` | `PEP.PostEffectDataLerp.Lerp(start.rimlight, end.rimlight, lerpTime)` |

追加後、Task 1 Step 3 と同じ手順で PostEffects 側へコピーし直し、`diff` で同一を確認すること。

- [ ] **Step 5: csproj から `Reference` を撤去する**

`COM3D2.SceneEditor.Plugin.csproj` から次のブロックを削除する（コメント 4 行を含む）:

```xml
    <!-- ポストエフェクトの実体を提供するプラグイン。未導入環境では PostEffectsBridge が
         型ロード失敗を握り潰すため、必要なのはビルド時だけ (実行時は任意)。
         向こうも MTEUtils 一式 (COM3D2.MotionTimelineEditor 名前空間) を同梱しており
         拡張メソッドが多重定義になるため、extern alias で名前空間ごと隔離する -->
    <Reference Include="COM3D25.PostEffects.Plugin">
      <HintPath>$(GameDir)\Sybaris\UnityInjector\COM3D25.PostEffects.Plugin.dll</HintPath>
      <Aliases>PostEffectsPlugin</Aliases>
      <Private>False</Private>
    </Reference>
```

- [ ] **Step 6: `extern alias` の残骸が無いことを確認する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -rn "extern alias\|PostEffectsPlugin::\|COM3D25.PostEffects" source/COM3D2.SceneEditor.Plugin --include='*.cs' --include='*.csproj'
```

Expected: `MTEUtils/PostEffectsClient.cs` の `HostAssemblyName` / `HostTypeName` の 2 行だけがヒットする。それ以外が残っていたら潰す。

- [ ] **Step 7: 両構成をビルド → テスト**

SceneEditor 側 MSBuild 2 本 → Build succeeded、`dotnet test` → 合格 340 / 失敗 4（ベースライン維持）。

- [ ] **Step 8: 「読んだら必ず Apply する」不変条件を目視で確認する**

**意味論が変わる点**: 変更前の `TimelineBridge.GetParaffinData(index)` は `settings.paraffin.GetData(index)` の**実体そのもの**を返していたため、返り値を書き換えるだけで `Apply*` を呼ばなくても描画へ反映された。本計画以降は**毎回新しい DTO のコピー**が返るので、`Apply*` を呼ばない限り変更が消える。

以下を読み、`Get*` の戻り値を書き換えている経路がすべて `Apply*` で締めていることを確認する。

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "GetParaffinData\|GetDistanceFogData\|GetRimlightData\|GetGTToneMapData\|GetDepthOfFieldData" \
  source/COM3D2.SceneEditor.Plugin/PostEffectRowDrawer.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PostEffectTimelineLayer.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs
```

確認済みの経路（レビュー時点）:
- `PostEffectRowDrawer.cs`: `updateTransform` フラグが立ったときだけ末尾で `Apply*` を呼ぶ形になっており問題なし
- `PostEffectTimelineLayer.cs` の 171 / 180 / 189 行: `trans.paraffin = postEffectManager.GetParaffinData(...)` のように **TransformData へ代入するだけ**で、返り値を書き換えていないため問題なし
- `PostEffectManager.ApplyDepthOfField` / `ApplyGTToneMap`: `Get` で受けた DTO を書き換えて同じ DTO を `Apply` へ渡す形になっており問題なし

**書き換えたのに `Apply*` していない経路が見つかった場合は、その場で `Apply*` を足すこと。** ビルドもテストも通ってしまい、実機で「値が戻る」形でしか露見しない。

- [ ] **Step 9: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils/PostEffectData.cs
git commit -m "feat(mteutils): 共有ポストエフェクト DTO にキーフレーム補間を追加する"

cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectData.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectManager.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PostEffectsBridge.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(posteffect): PostEffects.Plugin への直接参照をリフレクション経由へ置き換える"
```

---

### Task 5: レイヤー登録のゲートをロード順に耐える形へ直す

`TimelineIntegration` は `Initialize()` の 1 回だけ `isAvailable` を見てレイヤーを登録する。PostEffects のロードが後になる環境では、この時点でまだ false のことがある。**登録が漏れると「レイヤーが表示されない」症状がそのまま残るため、この Task が本計画の目的そのもの**。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`

**Interfaces:**
- Consumes: Task 4 の `PostEffectsBridge.isAvailable`
- Produces: なし（挙動のみ）

- [ ] **Step 1: 現在の登録タイミングを確認する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "PostEffectsBridge.isAvailable" -B 4 -A 22 source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
grep -n "public void Init()\|public void Update()\|TimelineUpdateManager()" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
```

Expected: `Initialize()` 内の 3 箇所（レイヤー登録・インスペクタ登録・Transform 登録）と `TimelineUpdateManager` のコンストラクタ、計 4 箇所がヒットする。

- [ ] **Step 2: 登録処理を 1 メソッドへ括り出し、接続できるまで再試行する**

`TimelineUpdateManager` に「まだ登録していなければ登録する」処理を持たせ、`Init()` と `Update()` の両方から呼ぶ。`Update()` は毎フレーム走るが、`_postEffectsRegistered` が立った後は bool 比較 1 回で抜ける。

`TimelineUpdateManager` のフィールドとコンストラクタを次の形にする（コンストラクタでの `_managers.Insert` は廃止し、遅延登録へ一本化する）:

```csharp
            // PostEffects.Plugin のロードは SceneEditor より後になることがあるため、
            // 接続できるまで毎フレーム試し、成功した 1 回だけ登録する
            private bool _postEffectsRegistered;

            private void TryRegisterPostEffects()
            {
                if (_postEffectsRegistered || !MTEP.PostEffectsBridge.isAvailable)
                {
                    return;
                }
                _postEffectsRegistered = true;

                // 破棄順に依存する並び (MovieManager → CameraManager 等) を崩さないよう、
                // 元の位置 (PsylliumManager の直後) へ差し込む
                var manager = MTEP.PostEffectManager.instance;
                _managers.Insert(
                    _managers.IndexOf(MTEP.PsylliumManager.instance) + 1, manager);
                manager.Init();

                timelineManager.RegisterLayer(
                    typeof(MTEP.PostEffectTimelineLayer), MTEP.PostEffectTimelineLayer.Create);
                TimelineItemInspectorRegistry.Register(
                    typeof(MTEP.PostEffectTimelineLayer), new PostEffectItemInspector());
                timelineManager.RegisterTransform(
                    MTEP.TransformType.DepthOfField,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDepthOfField>);
                timelineManager.RegisterTransform(
                    MTEP.TransformType.DistanceFog,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDistanceFog>);
                timelineManager.RegisterTransform(
                    MTEP.TransformType.GTToneMap,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataGTToneMap>);
                timelineManager.RegisterTransform(
                    MTEP.TransformType.Paraffin,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataParaffin>);
                timelineManager.RegisterTransform(
                    MTEP.TransformType.Rimlight,
                    MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRimlight>);

                // 後から差し込むため一括ループには乗らない。ライフサイクルを手で追いつかせる。
                // OnLoad は timeline 読込済みのときだけ (未読込で呼ぶと同期する値が無い)
                manager.Init();
                if (timelineManager.timeline != null)
                {
                    manager.OnLoad();
                }

                MTEUtils.LogDebug("PostEffects.Plugin と接続しました。ポストエフェクトのレイヤーを登録します");
            }
```

`Init()` の先頭と `Update()` の先頭で `TryRegisterPostEffects();` を呼ぶ。

**`Update()` では `UpdateGuards()` より前に置くこと。** `UpdateGuards()` は studioHack 未確定・メイド未解決・タイムライン未検証のとき早期 return するため、ガードの後ろに置くと接続の検出が遅れる（最悪、条件が揃うまで永久に登録されない）。

**`manager.Init()` / `manager.OnLoad()` を明示的に呼ぶ理由**: `_managers` へ後から差し込むため、`TimelineUpdateManager.Init()` / `OnLoad()` の一括ループには乗らない。`PostEffectManager.Init()` は現状空実装だが契約として揃える。`OnLoad()` は `DisableAllEffects()` と `InitPostEffects()` を実行し、`InitPostEffects()` が `timeline.paraffinCount` 等を実体側へ同期する。**シーンとタイムラインが既に読み込まれた後に接続が成立した場合、これを呼ばないとレイヤーは出るのに要素数が TimelineXml の値と食い違ったまま残る**（レビュー指摘 🔴）。`TimelineUpdateManager.OnLoad()` 自身も `timelineManager.timeline == null` で早期 return するため、ここでも同じ条件を使う。

- [ ] **Step 3: `Initialize()` 内の旧ゲートを撤去する**

`TimelineIntegration.Initialize()` にある 3 つの `if (MTEP.PostEffectsBridge.isAvailable) { ... }` ブロックを削除する（中身は Step 2 の `TryRegisterPostEffects` へ移した）。`TimelineUpdateManager` のコンストラクタも不要になるので削除する。

- [ ] **Step 4: 削除漏れが無いことを確認する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "PostEffectsBridge.isAvailable" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
```

Expected: `TryRegisterPostEffects` 内の 1 箇所だけ。

- [ ] **Step 5: 両構成をビルド → テスト**

SceneEditor 側 MSBuild 2 本 → Build succeeded、`dotnet test` → 合格 340 / 失敗 4。

- [ ] **Step 6: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs
git commit -m "fix(posteffect): PostEffects.Plugin と接続できた時点でレイヤーを登録する"
```

---

### Task 6: 要素数 UI のゲートを遅延接続に追随させる

`TimelineSettingWindow.DrawElementCountSection` は `isAvailable` が false のとき案内ラベルを出して return する。毎フレーム描画されるため、接続後は自動的に本来の UI へ切り替わる。**上限値が 0 のときに増減行が壊れないことだけ確認する。**

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs`

**Interfaces:**
- Consumes: Task 4 の `PostEffectsBridge.isAvailable` と `PostEffectManager.Max*Count`
- Produces: なし

- [ ] **Step 1: 現在のゲートを読む**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
grep -n "DrawElementCountSection" -A 26 source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
```

Expected: `isAvailable` が false のとき灰色ラベルを出して return し、true のとき `CountRowDrawer.Draw` を 3 本呼ぶ形になっている。

- [ ] **Step 2: 案内ラベルの文言をロード順の実態に合わせる**

現行は「見つかりません」と断定しているが、実際には未ロードなだけで後から繋がることがある。文言を変える:

```csharp
                view.DrawLabel("PostEffects.Plugin に接続していません。導入するとポストエフェクトを使用できます",
                    -1, ROW_HEIGHT, textColor: Color.gray);
```

- [ ] **Step 3: 両構成をビルド → テスト**

SceneEditor 側 MSBuild 2 本 → Build succeeded、`dotnet test` → 合格 340 / 失敗 4。

- [ ] **Step 4: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs
git commit -m "docs(setting): ポストエフェクト未接続時の案内文言を実態に合わせる"
```

---

### Task 7: ドキュメント更新

**Files:**
- Modify: `docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md`
- Modify: `W:/COM3D2_5/work/CLAUDE.md`

- [ ] **Step 1: スペックの連携方式を書き換える**

スペックの「B. SceneEditor 側: 委譲・実体削除・遅延バインド」の `PostEffectsBridge` の説明に、方式変更を追記する:

```markdown
**実装時の追記: 連携方式をリフレクションへ変更 (2026-08-30)**

当初は `COM3D25.PostEffects.Plugin.dll` へのコンパイル時参照で設計したが、
UnityInjector がファイル名順にロードするため SceneEditor が先に来てしまい、
ロード時の型解決に失敗する。Mono はこの束縛失敗をプロセス寿命の間キャッシュ
するため、後から PostEffects がロードされても復帰しない (`AssemblyResolve` の
後付けも効かない)。

そのため参照を撤去し、次の方式へ変更した:

- 値は MTEUtils の共有 DTO (`COM3D2.MotionTimelineEditor.PostEffects` 名前空間)
  で受け渡す。両プラグインが同じソースをコンパイルするため CLR 上は別型になり、
  境界では `ReflectionFieldCopier` が同名フィールドを写す
- ホストへの接続は `MTEUtils/PostEffectsClient.cs` が担う。既存の
  `ModelProviderClient` と同じく、ホスト型が見つかるまで再試行し続ける
- レイヤー登録は `TimelineIntegration.Initialize` の 1 回きりではなく、
  接続できたフレームで 1 回だけ行う (`TryRegisterPostEffects`)
```

- [ ] **Step 2: CLAUDE.md のビルド前提を削除する**

`W:/COM3D2_5/work/CLAUDE.md` から次の行を削除する（直接参照が無くなり、前提でなくなるため）:

```markdown
- **SceneEditor.Plugin のビルド前提**: `COM3D25.PostEffects.Plugin.dll` が両ゲームフォルダ（`W:\COM3D2\Sybaris\UnityInjector\` / `W:\COM3D2_5\Sybaris\UnityInjector\`）にデプロイ済みであること。SceneEditor はこの DLL を直接参照するため、未配置・古いままだと SceneEditor に無関係な変更でもビルドが通らない
```

代わりに、プラグイン間連携の作法として次を追記する:

```markdown
- **プラグイン間連携はリフレクション経由にすること**。UnityInjector はファイル名順にロードするため、後から来るプラグインをコンパイル時参照すると型解決に失敗し、Mono はその束縛失敗をプロセス寿命の間キャッシュする（`AssemblyResolve` の後付けも効かない）。`MTEUtils/*Client.cs` の作法（ホスト型が見つかるまで再試行、値は共有 DTO + `ReflectionFieldCopier`）に従う
```

`W:/COM3D2_5/work/CLAUDE.md` は git 管理外なのでコミット不要。

- [ ] **Step 3: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs/superpowers/specs/2026-08-30-posteffects-timeline-integration-design.md \
        docs/superpowers/plans/2026-08-30-posteffects-bridge-reflection.md
git commit -m "docs: PostEffects 連携をリフレクション方式へ変更した経緯を残す"
```

---

## 補足 (実装者向けメモ)

- **DTO のフィールド名が唯一の契約**: `ReflectionFieldCopier` は同名フィールドしか写さない。PostEffects 側の実体型でフィールド名を変えると、その値だけ黙って既定値のまま素通りする（例外は出ない）。実体側の名前を変えるときは MTEUtils の DTO も必ず追随させること
- **MTEUtils は 2 リポジトリで同一ソース**。片方だけ直すと DTO の形が食い違い、上記の「黙って素通り」を踏む。編集後は必ず `diff` で同一を確認する
- **`Delegate.CreateDelegate` の制約**: 戻り値の参照型は共変で束縛できる（`Func<int, object>` ← `ParaffinData GetParaffinData(int)`）が、引数は反変にできない（`Action<int, object>` ← `void ApplyParaffin(int, ParaffinData)` は不可）。そのため Apply 系だけ `MethodInfo.Invoke` を使う。毎フレーム 5 系統分の Invoke が走るが、1 回あたり ~1μs 程度で実測上の影響は無い想定
- **`_args1` / `_args2` の使い回しは単一スレッド前提**。Unity の IMGUI / Update はメインスレッドのみなので成立する。別スレッドから呼ぶ用途が出たら配列をローカルに変えること
- **上限値 0 の扱い**: 未接続時 `PostEffectsClient.max*Count` は 0 を返す。`TimelineSettingWindow` は `isAvailable` でゲートしてから参照するため到達しないが、他所から参照を足すときは同じくゲートすること
- **リフレクションの実行コスト**: `MethodInfo.Invoke` に加え、`ReflectionFieldCopier` も `FieldInfo.GetValue` / `SetValue` を使うため値型のボクス化が毎回走る。毎フレームの最大規模は「(パラフィン 4 + フォグ 4 + リムライト 4) × フィールド 11〜16 個 × (Get + Apply)」に GTToneMap / DoF を足した程度。実機確認時にタイムライン再生中の FPS を見ておくこと。問題になるなら、型ペアごとに `FieldInfo` を押さえているので `Delegate` 化 (ゲッタ・セッタを `Action<object, object>` へ展開) へ差し替える余地がある
- **実機確認チェックリスト**（次回ゲーム起動時）:
  - BepInEx ログに `Failed to load ... COM3D2.SceneEditor.Plugin.dll` の `TypeLoadException` が出ないこと
  - ログに `PostEffects.Plugin と接続しました` が 1 回だけ出ること
  - タイムラインにポストエフェクトのレイヤーが表示され、キーフレームを打てること
  - 再生でパラフィン / 距離フォグ / リムライト / DoF / GT トーンマップの値が動き、PostEffects.Plugin の「タイムライン」タブへ反映されること
  - リムライトの `maskMode` / `excludeFace` / `applyHair`、パラフィンの `maskMode` がタイムラインから駆動できること
  - DoF のメイド追従が `maidSlotNo` ↔ `maidFocus` + `maidIndex` で往復すること
  - PostEffects.Plugin の DLL を外して起動 → レイヤー・UI が出ず、例外も出ないこと
  - タイムライン設定の要素数行が上限 4 で頭打ちになること

## 本計画のスコープ外

- **`WINDOW_ID = 8903361` の三重衝突**: `MTEUtils/ComboBoxPopupWindow` を ModItemExplorer / SceneEditor / PostEffects の 3 プラグインが同じ ID で持っており、コンボボックスのポップアップが同時に 2 つ以上開くと `GUI.Window` の状態を奪い合う。実機で確認済みだが、今回の「レイヤーが表示されない」問題とは独立しているため別タスクとする
- **`EyesItemInspectorTests` の先行失敗 4 件**: 本計画と無関係
- **テストプロジェクトが依存する `netstandard.dll` / `UnityEngine.SharedInternalsModule.dll` の供給**: COM3D25 構成のビルドが生成しないため `-t:Rebuild` で消える。恒久対処は別途

## レビュー却下メモ

- (却下なし。plan-reviewer の指摘 🔴 1 件・🟡 2 件・🟢 1 件はすべて取り込み済み: `OnLoad()` の呼び落とし (Task 5)、ホスト呼び出しの try/catch 隔離 (Task 3 Step 2)、Get の意味論変更の検証ステップ (Task 4 Step 8)、リフレクションの実行コストを実機チェックへ追加)
- レビュー指摘外の自発的な追加 2 件: `Update()` で `TryRegisterPostEffects()` を `UpdateGuards()` より前に置く注意 (Task 5)、フィールド名ずれの自己診断 (Task 1 / Task 3)
