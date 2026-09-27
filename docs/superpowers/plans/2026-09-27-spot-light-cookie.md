# スポットライトの輪郭 (cookie) 設定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 追加ライトのスポットに輪郭テクスチャ (cookie) を設定できるようにし、「硬さ」スライダーから自動生成した円形 cookie か、`Config/SceneEditor/LightCookie` の PNG 画像を選べるようにする。

**Architecture:** 輪郭の設定 (`LightCookieData`) を灯の GameObject に付けた `LightCookieHolder` コンポーネントに持たせ、`LightCookie.Set / Apply` が `Light.cookie` へ反映する。テクスチャ生成・画像読込・キャッシュは `LightCookieTextures` に、Unity に依存しないアルファ計算は `LightCookieAlpha` に分ける。保存はタイムラインのライト定義 (`TimelineLightXml`)、シーンプリセット / Undo (`ScenePresetAdditionalLight`)、クリップボードの 3 経路に載せる。

**Tech Stack:** C# (Unity 2022.3 / COM3D2 両ビルド), IMGUI (`GUIView`), XmlSerializer, xunit (net48)

**Spec:** 本計画の「仕様」節 (調査の経緯は PostEffects リポジトリでのセッション: 実機で 120° のスポットに Alpha8 の円形 cookie を当て、縁が硬くなることを確認済み)

## 仕様

- 背景: ビルトイン RP のスポットは内蔵 cookie の減衰で縁を描くため、角度を広げるとぼけ幅も比例して広がる。`innerSpotAngle` はビルトイン RP では効かない
- 対象は **追加ライトのスポットのみ**。メインライト・ポイント・平行には輪郭設定を出さない (種別がスポット以外なら `Light.cookie = null`)。設定値自体は種別を変えても保持し、スポットに戻すと再適用する
- 輪郭モードは 3 つ
  - `既定` (Default): `Light.cookie = null` で Unity 内蔵の減衰。既存データの見た目は変えない
  - `硬さ` (Generated): 硬さ 0〜1 (既定 0.8) から 256×256 の円形アルファを生成。0 で中心から滑らかに減衰、1 で 1px の縁だけぼかす
  - `画像` (Image): `Config/SceneEditor/LightCookie` 配下 (サブフォルダ可) の PNG を相対パスで選ぶ
- 画像の変換: 透過を持つ画像 (いずれかの画素の a < 255) はアルファを、持たない画像は輝度 (0.299R + 0.587G + 0.114B) をそのまま cookie のアルファにする。外周 1px は強制的に 0 (縁が黒くないと光が四角く漏れるため)
- 画像が見つからない・読めないときは警告ログを 1 回出して既定の輪郭で描く (設定値は残す)
- 保存
  - タイムライン: `<Light>` に SE 独自要素 `CookieMode` (int) / `CookieHardness` / `CookieImage`。既定の輪郭では何も書かない。`TimelineData.CurrentVersion` は上げない (ModelLayer と同じ扱い)
  - シーンプリセット / Undo: `ScenePresetAdditionalLight` に属性 `cookieMode` / `cookieHardness` / `cookieImage`。`ScenePresetData.CurrentVersion` を 37 へ
  - ライトのコピー / ペーストにも含める
- 輪郭はキーフレーム化しない (ライト定義の値)

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 (v3.5 / Unity 旧版) と COM3D25 の両構成でビルドが通ること。`TextureFormat.Alpha8` / `LoadRawTextureData` / `LoadImage` は両方で使える API のみ使う
- ビルド確認は MSBuild 直叩き (`debug.bat` はゲームフォルダへ DLL をコピーするので使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"` (COM3D2 構成は `/p:GameVersion=COM3D2`)
- テストは上記 COM3D25 ビルドの後に `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
- 新規 .cs は csproj の `<Compile Include>` へ追加する (SDK 形式ではない)
- タイムライン XML は MTE → SE の一方向互換。SE 独自要素は MTE で読み飛ばされる前提でよい

## Review Focus

1. 既定の輪郭のライトを含む旧タイムライン / 旧プリセットを読み、保存し直しても XML が変わらない (Task 3・4 のテストで固定)
2. 種別をスポット → ポイントへ変えたとき `Light.cookie` が残らない (ポイントに 2D cookie が付くと描画が壊れる)。`SetLightType` で必ず `Apply` する (Task 2)
3. `Config/SceneEditor/LightCookie` が無い・空のとき、UI が例外を出さず案内文を出す (Task 2 の `ListImageNames`、Task 5)
4. プリセット XML の `cookieImage` に `..\..\x.png` や絶対パスを書かれても、フォルダ外を読まない (`ResolveImagePath` を流用、Task 2)
5. 透過なし白黒画像・透過付き画像の両方が意図どおりのアルファになり、外周が 0 になる (Task 1 のテスト)

---

## File Structure

| ファイル | 責務 |
|---|---|
| Create `source/COM3D2.SceneEditor.Plugin/LightCookieData.cs` | `LightCookieMode` enum と値型 `LightCookieData` (既定値・正規化・等値) |
| Create `source/COM3D2.SceneEditor.Plugin/LightCookieAlpha.cs` | Unity 実行環境に依存しないアルファ計算 (円形生成・画素→アルファ・外周消去) |
| Create `source/COM3D2.SceneEditor.Plugin/LightCookieTextures.cs` | Texture2D の生成・画像読込・キャッシュ・フォルダ列挙・破棄 |
| Create `source/COM3D2.SceneEditor.Plugin/LightCookie.cs` | `LightCookieHolder` コンポーネントと `Get / Set / Apply` |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs` | 種別変更時の再適用、全灯の再適用、無効化時のテクスチャ破棄 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` | `ResolveImagePath` を public にして流用 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `TimelineLightXml` に SE 独自要素 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `TimelineLightData` に `cookie` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs` | stat に `cookie` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` | 変更検出と定義同期、ロード時の適用 |
| Modify `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetAdditionalLight` に属性、CurrentVersion 37 |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs` | 記録・復元・比較 |
| Modify `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs` | コピー / ペースト |
| Modify `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs` | 輪郭 UI |
| Create tests `LightCookieAlphaTests.cs` / `LightCookieDataTests.cs` / `LightCookieXmlTests.cs` / `ScenePresetLightCookieTests.cs` | |
| Modify `docs-site/guide/staging.md` / `docs-site/timeline/compatibility.md` / `W:\COM3D2_5\work\CLAUDE.md` | ドキュメント |

---

### Task 1: 輪郭データとアルファ計算

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LightCookieData.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/LightCookieAlpha.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` (`LightClipboard.cs` の行の後に 2 行)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightCookieAlphaTests.cs`, `LightCookieDataTests.cs`

**Interfaces:**
- Produces: `enum LightCookieMode { Default = 0, Generated = 1, Image = 2 }`、`struct LightCookieData { LightCookieMode mode; float hardness; string image; const float DefaultHardness = 0.8f; static LightCookieData Default { get; } LightCookieData Normalized(); bool Equals(LightCookieData) }`、`static class LightCookieAlpha { byte[] BuildRadial(int size, float hardness); byte[] FromPixels(Color32[] pixels, int width, int height); void ClearBorder(byte[] alpha, int width, int height) }` (すべて namespace `COM3D2.SceneEditor.Plugin`)

- [ ] **Step 1: 失敗するテストを書く**

`LightCookieAlphaTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>スポットライトの輪郭 (cookie) のアルファ計算を固定する</summary>
    public class LightCookieAlphaTests
    {
        private const int Size = 64;

        private static byte At(byte[] alpha, int x, int y) => alpha[y * Size + x];

        [Fact]
        public void 生成した円は中心が不透明で外周と四隅は0()
        {
            var alpha = LightCookieAlpha.BuildRadial(Size, 0.8f);

            Assert.Equal(Size * Size, alpha.Length);
            Assert.Equal(255, At(alpha, Size / 2, Size / 2));
            for (var i = 0; i < Size; i++)
            {
                Assert.Equal(0, At(alpha, i, 0));
                Assert.Equal(0, At(alpha, i, Size - 1));
                Assert.Equal(0, At(alpha, 0, i));
                Assert.Equal(0, At(alpha, Size - 1, i));
            }
        }

        [Fact]
        public void 硬いほど縁の手前まで明るい()
        {
            // 半径 0.75 付近 (中心から右へ 3/8 * Size) の明るさを比べる
            var x = Size / 2 + Size * 3 / 8;
            var soft = At(LightCookieAlpha.BuildRadial(Size, 0f), x, Size / 2);
            var hard = At(LightCookieAlpha.BuildRadial(Size, 1f), x, Size / 2);

            Assert.True(hard > soft, $"hard={hard} soft={soft}");
            Assert.Equal(255, hard);
        }

        [Fact]
        public void 硬さは0から1へ丸める()
        {
            Assert.Equal(LightCookieAlpha.BuildRadial(Size, 1f), LightCookieAlpha.BuildRadial(Size, 5f));
            Assert.Equal(LightCookieAlpha.BuildRadial(Size, 0f), LightCookieAlpha.BuildRadial(Size, -1f));
        }

        [Fact]
        public void 透過の無い画像は輝度をアルファにする()
        {
            var pixels = Fill(3, 3, new Color32(255, 255, 255, 255));
            pixels[4] = new Color32(128, 128, 128, 255);

            var alpha = LightCookieAlpha.FromPixels(pixels, 3, 3);

            // 中央以外は外周なので 0
            Assert.Equal(new byte[] { 0, 0, 0, 0, 128, 0, 0, 0, 0 }, alpha);
        }

        [Fact]
        public void 透過のある画像はアルファをそのまま使う()
        {
            var pixels = Fill(3, 3, new Color32(0, 0, 0, 0));
            pixels[4] = new Color32(0, 0, 0, 200);

            var alpha = LightCookieAlpha.FromPixels(pixels, 3, 3);

            Assert.Equal(200, alpha[4]);
        }

        [Fact]
        public void 外周1pxを0にする()
        {
            var alpha = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }; // 4x3

            LightCookieAlpha.ClearBorder(alpha, 4, 3);

            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 9, 9, 0, 0, 0, 0, 0 }, alpha);
        }

        private static Color32[] Fill(int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            return pixels;
        }
    }
}
```

`LightCookieDataTests.cs`:

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightCookieDataTests
    {
        [Fact]
        public void 既定値は既定の輪郭と既定の硬さ()
        {
            var data = LightCookieData.Default;

            Assert.Equal(LightCookieMode.Default, data.mode);
            Assert.Equal(LightCookieData.DefaultHardness, data.hardness);
            Assert.Equal("", data.image);
        }

        [Fact]
        public void 正規化は未知のモードを既定にし硬さを丸める()
        {
            var data = new LightCookieData { mode = (LightCookieMode)99, hardness = 3f, image = null }.Normalized();

            Assert.Equal(LightCookieMode.Default, data.mode);
            Assert.Equal(1f, data.hardness);
            Assert.Equal("", data.image);
        }

        [Fact]
        public void NaNの硬さは既定値にする()
        {
            var data = new LightCookieData { mode = LightCookieMode.Generated, hardness = float.NaN }.Normalized();

            Assert.Equal(LightCookieData.DefaultHardness, data.hardness);
        }

        [Fact]
        public void 画像名のnullと空文字は等しい()
        {
            var a = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = null };
            var b = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = "" };

            Assert.True(a.Equals(b));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (Global Constraints の COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightCookie"`
Expected: テストプロジェクトのコンパイルエラー (`LightCookieAlpha` / `LightCookieData` が無い)

- [ ] **Step 3: 実装する**

`LightCookieData.cs`:

```csharp
using System;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>スポットライトの輪郭 (cookie) の決め方。数値は XML に保存するので変えないこと</summary>
    public enum LightCookieMode
    {
        // Unity 内蔵の減衰 (cookie なし)
        Default = 0,
        // 硬さから円形の cookie を生成する
        Generated = 1,
        // Config/SceneEditor/LightCookie の PNG を使う
        Image = 2,
    }

    /// <summary>
    /// 追加ライト 1 灯分の輪郭設定。キーフレーム化しないライト定義の値で、
    /// タイムライン・シーンプリセット・クリップボードの間で受け渡す
    /// </summary>
    public struct LightCookieData : IEquatable<LightCookieData>
    {
        public const float DefaultHardness = 0.8f;

        public LightCookieMode mode;
        /// <summary>0 で中心から滑らかに減衰、1 で縁だけぼかす</summary>
        public float hardness;
        /// <summary>LightCookie フォルダからの相対パス</summary>
        public string image;

        public static LightCookieData Default => new LightCookieData
        {
            mode = LightCookieMode.Default,
            hardness = DefaultHardness,
            image = "",
        };

        /// <summary>XML など外部入力由来の値を扱える範囲へ丸める</summary>
        public LightCookieData Normalized()
        {
            return new LightCookieData
            {
                mode = Enum.IsDefined(typeof(LightCookieMode), mode) ? mode : LightCookieMode.Default,
                hardness = float.IsNaN(hardness) ? DefaultHardness : Math.Max(0f, Math.Min(1f, hardness)),
                image = image ?? "",
            };
        }

        public bool Equals(LightCookieData other)
        {
            return mode == other.mode
                && hardness == other.hardness
                && string.Equals(image ?? "", other.image ?? "", StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is LightCookieData other && Equals(other);

        public override int GetHashCode()
        {
            return ((int)mode * 397) ^ hardness.GetHashCode() ^ (image ?? "").GetHashCode();
        }
    }
}
```

`LightCookieAlpha.cs` (Mathf ではなく System.Math を使い、ゲーム外テストで Unity 実行環境に触れないようにする):

```csharp
using System;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スポットライトの輪郭 (cookie) のアルファ計算。
    /// ビルトイン RP のスポットは cookie のアルファだけを明るさに使い、
    /// cookie の形がそのまま光の形になるため、外周は必ず 0 にする (0 でないと光が四角く漏れる)
    /// </summary>
    public static class LightCookieAlpha
    {
        /// <summary>
        /// 円形のアルファを生成する。hardness は内側の明るい円の半径の比率で、
        /// 1 でも縁の 1px はぼかしてジャギを抑える。配列は下の行から並ぶ (Texture2D の生データと同じ)
        /// </summary>
        public static byte[] BuildRadial(int size, float hardness)
        {
            var alpha = new byte[size * size];
            var pixel = 2f / size;
            var outer = 1f - pixel;
            var clamped = Math.Max(0f, Math.Min(1f, hardness));
            var inner = Math.Min(clamped * outer, outer - pixel);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) * pixel - 1f;
                    var dy = (y + 0.5f) * pixel - 1f;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    var t = Math.Max(0f, Math.Min(1f, (r - inner) / (outer - inner)));
                    var a = 1f - t * t * (3f - 2f * t);
                    alpha[y * size + x] = (byte)Math.Round(a * 255f);
                }
            }

            ClearBorder(alpha, size, size);
            return alpha;
        }

        /// <summary>
        /// 画像の画素を cookie のアルファへ変換する。透過を持つ画像はアルファを、
        /// 持たない画像 (白黒の模様画像など) は輝度を使う
        /// </summary>
        public static byte[] FromPixels(Color32[] pixels, int width, int height)
        {
            var hasTransparency = false;
            foreach (var p in pixels)
            {
                if (p.a < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }

            var alpha = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                alpha[i] = hasTransparency
                    ? p.a
                    : (byte)Math.Round(0.299 * p.r + 0.587 * p.g + 0.114 * p.b);
            }

            ClearBorder(alpha, width, height);
            return alpha;
        }

        public static void ClearBorder(byte[] alpha, int width, int height)
        {
            for (var x = 0; x < width; x++)
            {
                alpha[x] = 0;
                alpha[(height - 1) * width + x] = 0;
            }
            for (var y = 0; y < height; y++)
            {
                alpha[y * width] = 0;
                alpha[y * width + width - 1] = 0;
            }
        }
    }
}
```

csproj (`<Compile Include="LightClipboard.cs" />` の直後):

```xml
    <Compile Include="LightCookieData.cs" />
    <Compile Include="LightCookieAlpha.cs" />
```

- [ ] **Step 4: テストが通ることを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightCookie"`
Expected: PASS (10 件)

- [ ] **Step 5: コミット** (commit スキル。例: `feat(light): スポットライトの輪郭データとアルファ計算を追加する`)

---

### Task 2: テクスチャ管理と灯への適用

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LightCookieTextures.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/LightCookie.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs:199` (`private static string ResolveImagePath` → `public static`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs` (`SetLightType`・`ReapplyCookies` 追加・`OnPluginDisable`)
- Modify: csproj (Task 1 の 2 行の後に 2 行)

**Interfaces:**
- Consumes: Task 1 の `LightCookieData` / `LightCookieAlpha`
- Produces: `static class LightCookieTextures { string directory; Texture2D GetGenerated(float hardness); Texture2D GetImage(string relativePath); List<string> GetImageNames(); void Reload(); void ReleaseAll(); }`、`class LightCookieHolder : MonoBehaviour { LightCookieData data; }`、`static class LightCookie { LightCookieData Get(Light); void Set(Light, LightCookieData); void Apply(Light); }`、`StudioLightManager.ReapplyCookies()`

Unity 実体に触るため単体テストは書かない (アルファ計算は Task 1 で固定済み)。確認はビルドと Task 7 の実機確認で行う。

- [ ] **Step 1: `LightCookieTextures.cs` を作る**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スポットライトの輪郭 (cookie) テクスチャの生成・読込・キャッシュ。
    /// 同じ硬さ・同じ画像の灯はテクスチャを共有する
    /// </summary>
    public static class LightCookieTextures
    {
        private const int GeneratedSize = 256;
        // 硬さはこの刻みで丸めてキャッシュする (スライダーのドラッグで生成が増え続けないように)
        private const int HardnessSteps = 100;

        private static readonly Dictionary<int, Texture2D> _generated = new Dictionary<int, Texture2D>();
        // 読めなかった画像も null で覚え、警告を毎回出さない
        private static readonly Dictionary<string, Texture2D> _images =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _imageNames = null;

        public static string directory => Path.Combine(PluginUtils.PluginDataPath, "LightCookie");

        public static Texture2D GetGenerated(float hardness)
        {
            var key = (int)Math.Round(Math.Max(0f, Math.Min(1f, hardness)) * HardnessSteps);
            Texture2D texture;
            if (_generated.TryGetValue(key, out texture) && texture != null)
            {
                return texture;
            }

            var alpha = LightCookieAlpha.BuildRadial(GeneratedSize, (float)key / HardnessSteps);
            texture = CreateAlphaTexture(alpha, GeneratedSize, GeneratedSize);
            _generated[key] = texture;
            return texture;
        }

        public static Texture2D GetImage(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            Texture2D texture;
            if (_images.TryGetValue(relativePath, out texture))
            {
                return texture;
            }

            texture = LoadImage(relativePath);
            _images[relativePath] = texture;
            return texture;
        }

        private static Texture2D LoadImage(string relativePath)
        {
            // relativePath はプリセット XML 由来の外部入力なのでフォルダ外への脱出を弾く
            var path = PngPlacementManager.ResolveImagePath(directory, relativePath);
            if (path == null)
            {
                MTEUtils.LogWarning("ライトの輪郭画像のパスが不正です: {0}", relativePath);
                return null;
            }
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("ライトの輪郭画像が見つかりません: {0}", path);
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ライトの輪郭画像を読み込めません: {0} ({1})", path, e.Message);
                return null;
            }

            // サイズは LoadImage が実画像で上書きするためダミー
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(bytes))
                {
                    MTEUtils.LogWarning("ライトの輪郭画像を読み込めません: {0}", path);
                    return null;
                }
                var alpha = LightCookieAlpha.FromPixels(source.GetPixels32(), source.width, source.height);
                return CreateAlphaTexture(alpha, source.width, source.height);
            }
            finally
            {
                Object.Destroy(source);
            }
        }

        private static Texture2D CreateAlphaTexture(byte[] alpha, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.Alpha8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.LoadRawTextureData(alpha);
            texture.Apply(false);
            return texture;
        }

        /// <summary>LightCookie フォルダ配下の PNG の相対パス一覧。フォルダが無ければ空</summary>
        public static List<string> GetImageNames()
        {
            if (_imageNames == null)
            {
                _imageNames = ListImageNames();
            }
            return _imageNames;
        }

        private static List<string> ListImageNames()
        {
            var result = new List<string>();
            var dir = directory;
            if (!Directory.Exists(dir))
            {
                return result;
            }

            var rootPath = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var path in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
            {
                result.Add(Path.GetFullPath(path).Substring(rootPath.Length));
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>画像の一覧と読込済み画像を捨てる。呼び出し側は灯へ再適用すること</summary>
        public static void Reload()
        {
            DestroyAll(_images.Values);
            _images.Clear();
            _imageNames = null;
        }

        public static void ReleaseAll()
        {
            Reload();
            DestroyAll(_generated.Values);
            _generated.Clear();
        }

        private static void DestroyAll(IEnumerable<Texture2D> textures)
        {
            foreach (var texture in textures)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
        }
    }
}
```

- [ ] **Step 2: `LightCookie.cs` を作る**

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 灯ごとの輪郭設定。Light.cookie だけでは生成元 (硬さ・画像名) を失うため、灯の GameObject に持たせる
    /// </summary>
    public class LightCookieHolder : MonoBehaviour
    {
        public LightCookieData data = LightCookieData.Default;
    }

    /// <summary>追加ライトの輪郭 (cookie) の読み書き</summary>
    public static class LightCookie
    {
        public static LightCookieData Get(Light light)
        {
            var holder = light != null ? light.GetComponent<LightCookieHolder>() : null;
            return holder != null ? holder.data : LightCookieData.Default;
        }

        public static void Set(Light light, LightCookieData data)
        {
            if (light == null)
            {
                return;
            }

            data = data.Normalized();
            var holder = light.GetComponent<LightCookieHolder>();
            if (holder == null)
            {
                // 既定の灯には部品を増やさない
                if (data.Equals(LightCookieData.Default))
                {
                    Apply(light);
                    return;
                }
                holder = light.gameObject.AddComponent<LightCookieHolder>();
            }
            holder.data = data;
            Apply(light);
        }

        /// <summary>
        /// 設定を Light.cookie へ反映する。2D の cookie はスポットにしか使えないため、
        /// それ以外の種別では外す (設定値は残し、スポットへ戻したときに再適用する)
        /// </summary>
        public static void Apply(Light light)
        {
            if (light == null)
            {
                return;
            }
            light.cookie = light.type == LightType.Spot ? GetTexture(Get(light)) : null;
        }

        private static Texture GetTexture(LightCookieData data)
        {
            switch (data.mode)
            {
                case LightCookieMode.Generated:
                    return LightCookieTextures.GetGenerated(data.hardness);
                case LightCookieMode.Image:
                    // 読めなければ null になり、Unity 内蔵の輪郭で描く
                    return LightCookieTextures.GetImage(data.image);
                default:
                    return null;
            }
        }
    }
}
```

- [ ] **Step 3: `PngPlacementManager.ResolveImagePath` を public にする**

`Manager/PngPlacementManager.cs` の `private static string ResolveImagePath(string dir, string relativePath)` を `public static string ResolveImagePath(string dir, string relativePath)` に変える (本体・コメントはそのまま)。

- [ ] **Step 4: SE 側 `StudioLightManager` を変更する**

`SetLightType` の `light.type = type;` の後に:

```csharp
            // 2D の cookie はスポット専用なので、種別に合わせて付け外しする
            LightCookie.Apply(light);
```

`RemoveLight` の後に追加:

```csharp
        /// <summary>輪郭テクスチャを読み直したあと、全灯の Light.cookie を付け直す</summary>
        public void ReapplyCookies()
        {
            foreach (var light in _lights)
            {
                LightCookie.Apply(light);
            }
        }
```

`OnPluginDisable` を:

```csharp
        public override void OnPluginDisable()
        {
            ReleaseAll();
            LightCookieTextures.ReleaseAll();
        }
```

- [ ] **Step 5: csproj に追加する** (`LightCookieAlpha.cs` の行の後)

```xml
    <Compile Include="LightCookieTextures.cs" />
    <Compile Include="LightCookie.cs" />
```

- [ ] **Step 6: 両構成でビルドする**

Run: MSBuild を `/p:GameVersion=COM3D25` と `/p:GameVersion=COM3D2` で 1 回ずつ
Expected: エラー 0

- [ ] **Step 7: コミット** (例: `feat(light): スポットライトへ輪郭テクスチャを適用する仕組みを追加する`)

---

### Task 3: タイムラインのライト定義へ保存する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:34-40` (`TimelineLightXml`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:70-105` (`TimelineLightData`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioLightStat.cs` (フィールド・コンストラクタ・`FromStat`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs` (`LateUpdate(bool)` / `SetupLights` / `CreateLightInternal`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightCookieXmlTests.cs`

**Interfaces:**
- Consumes: `LightCookieData`、`LightCookie.Get / Set`
- Produces: `TimelineLightXml.cookieMode (int) / cookieHardness / cookieImage`、`TimelineLightData.cookie`、`StudioLightStat.cookie`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義の輪郭 (&lt;CookieMode&gt; ほか) の保存と読込を固定する。
    /// 既定の輪郭は書き出さず、要素の無い旧 XML は既定として読む
    /// </summary>
    public class LightCookieXmlTests
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

        private static TimelineLightData RoundTrip(TimelineLightData data, out string text)
        {
            text = Serialize(data.ToXml());
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var reader = new StringReader(text))
            {
                var restored = new TimelineLightData();
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
                return restored;
            }
        }

        [Fact]
        public void 既定の輪郭は書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot };

            var text = Serialize(data.ToXml());

            Assert.DoesNotContain("Cookie", text);
        }

        [Fact]
        public void 硬さ指定は硬さだけを往復する()
        {
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Spot,
                cookie = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.95f, image = "a.png" },
            };

            string text;
            var restored = RoundTrip(data, out text);

            Assert.Contains("<CookieMode>1</CookieMode>", text);
            Assert.Contains("<CookieHardness>0.95</CookieHardness>", text);
            Assert.DoesNotContain("CookieImage", text);
            Assert.Equal(LightCookieMode.Generated, restored.cookie.mode);
            Assert.Equal(0.95f, restored.cookie.hardness);
        }

        [Fact]
        public void 画像指定は画像名だけを往復する()
        {
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Spot,
                cookie = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.3f, image = @"gobo\window.png" },
            };

            string text;
            var restored = RoundTrip(data, out text);

            Assert.Contains("<CookieImage>gobo\\window.png</CookieImage>", text);
            Assert.DoesNotContain("CookieHardness", text);
            Assert.Equal(LightCookieMode.Image, restored.cookie.mode);
            Assert.Equal(@"gobo\window.png", restored.cookie.image);
        }

        [Fact]
        public void 要素の無い旧XMLは既定の輪郭として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type></TimelineLightXml>";

            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            var restored = new TimelineLightData();
            using (var reader = new StringReader(text))
            {
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
            }

            Assert.True(restored.cookie.Equals(LightCookieData.Default));
        }

        [Fact]
        public void 未知のモードは既定として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type><CookieMode>9</CookieMode></TimelineLightXml>";

            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            var restored = new TimelineLightData();
            using (var reader = new StringReader(text))
            {
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
            }

            Assert.Equal(LightCookieMode.Default, restored.cookie.mode);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~LightCookieXml"`
Expected: コンパイルエラー (`TimelineLightData.cookie` が無い)

- [ ] **Step 3: `TimelineLightXml` に要素を足す**

```csharp
    public class TimelineLightXml
    {
        [XmlElement("Name")]
        public string name;
        [XmlElement("Type")]
        public LightType type;

        // 輪郭 (cookie) は SE 独自。既定の輪郭では書き出さず、MTE 産の XML を保存し直しても内容を変えない。
        // モードは未知の値でも読めるよう int で持ち、読込時に LightCookieData.Normalized で丸める
        [XmlElement("CookieMode")]
        public int cookieMode;
        [XmlElement("CookieHardness")]
        public float cookieHardness = LightCookieData.DefaultHardness;
        [XmlElement("CookieImage")]
        public string cookieImage;

        public bool ShouldSerializecookieMode() { return cookieMode != (int)LightCookieMode.Default; }
        public bool ShouldSerializecookieHardness() { return cookieMode == (int)LightCookieMode.Generated; }
        public bool ShouldSerializecookieImage()
        {
            return cookieMode == (int)LightCookieMode.Image && !string.IsNullOrEmpty(cookieImage);
        }
    }
```

- [ ] **Step 4: `TimelineLightData` に `cookie` を足す**

```csharp
    public class TimelineLightData
    {
        public string name;
        public LightType type;
        public LightCookieData cookie = LightCookieData.Default;

        public TimelineLightData()
        {
        }

        public TimelineLightData(StudioLightStat light)
        {
            FromStat(light);
        }

        public void FromStat(StudioLightStat light)
        {
            name = light.name;
            type = light.type;
            cookie = light.cookie;
        }

        public void FromXml(TimelineLightXml xml)
        {
            name = xml.name;
            type = xml.type;
            cookie = new LightCookieData
            {
                mode = (LightCookieMode)xml.cookieMode,
                hardness = xml.cookieHardness,
                image = xml.cookieImage,
            }.Normalized();
        }

        public TimelineLightXml ToXml()
        {
            var xml = new TimelineLightXml
            {
                name = name,
                type = type,
                cookieMode = (int)cookie.mode,
                cookieHardness = cookie.hardness,
                cookieImage = cookie.image,
            };
            return xml;
        }
    }
```

`TimelineData.cs` の先頭 using に `COM3D2.SceneEditor.Plugin` が無ければ追加する。

- [ ] **Step 5: `StudioLightStat` に `cookie` を足す**

`public string displayName = "通常";` の後に:

```csharp
        /// <summary>
        /// 輪郭設定の写し。実体の変化を LateUpdate の比較で拾い、タイムラインのライト定義へ同期するために持つ
        /// </summary>
        public SceneEditor.Plugin.LightCookieData cookie = SceneEditor.Plugin.LightCookieData.Default;
```

`StudioLightStat(Light light, Transform transform, object obj, int index)` の末尾に `this.cookie = SceneEditor.Plugin.LightCookie.Get(light);`、`FromStat` の `index = stat.index;` の後に `cookie = stat.cookie;` を足す。

- [ ] **Step 6: タイムライン側 `StudioLightManager` を変更する**

`LateUpdate(bool force)` の `var refresh = false;` の後に `var cookieChanged = false;` を足し、ループ内の種別比較の `if` ブロックの後 (ループ末尾) に:

```csharp
                // 輪郭はライト定義の値なので、一覧の作り直し (イベント発火) はせず定義だけ同期する
                if (!cachedLight.cookie.Equals(stat.cookie))
                {
                    cachedLight.cookie = stat.cookie;
                    cookieChanged = true;
                }
```

`if (refresh) { ... UpdateTimelineLights(); }` の直後に:

```csharp
            else if (cookieChanged)
            {
                UpdateTimelineLights();
            }
```

`CreateLightInternal` を生成した灯を返すように変える:

```csharp
        // 追加ライトを 1 灯生成し stat の種別を適用する。stat.light は次回 LateUpdate で再収集される
        private Light CreateLightInternal(StudioLightStat stat)
        {
            var newLight = seLightManager.AddLight();
            seLightManager.SetLightType(newLight, stat.type);
            return newLight;
        }
```

`SetupLights` のループを:

```csharp
                if (i >= lightList.Count)
                {
                    var stat = CreateLightStat(lightData.type, i);
                    var newLight = CreateLightInternal(stat);
                    // メインライトが取れないシーンでは index 0 もここを通る。メインライト相当の定義は輪郭を持たない
                    if (i > 0)
                    {
                        SceneEditor.Plugin.LightCookie.Set(newLight, lightData.cookie);
                    }

                    MTEUtils.LogDebug("Create light: type={0} displayName={1} name={2}",
                        stat.type, stat.displayName, stat.name);
                }
                else
                {
                    var stat = lightList[i];
                    var newStat = stat.Clone();
                    newStat.type = lightData.type;
                    newStat.index = i;
                    ChangeLight(newStat);

                    // メインライト (index 0) は輪郭を持たない
                    if (i > 0)
                    {
                        SceneEditor.Plugin.LightCookie.Set(stat.light, lightData.cookie);
                    }
                }
```

- [ ] **Step 7: テストが通ることを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS (既存の `XmlRoundTripTests` / `MteCompatibilityTests` も含む)

- [ ] **Step 8: コミット** (例: `feat(timeline): ライト定義にスポットライトの輪郭を保存する`)

---

### Task 4: シーンプリセット・Undo・クリップボード

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:113-137` (`ScenePresetAdditionalLight`)、`:1085-1094` (変更履歴コメントと CurrentVersion)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs` (`CaptureState` / `ApplyLightState` / `Approximately`)
- Modify: `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs:149` (36 → 37)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightCookieTests.cs`

**Interfaces:**
- Consumes: `LightCookieData`、`LightCookie.Get / Set`
- Produces: `ScenePresetAdditionalLight.cookieMode (LightCookieMode) / cookieHardness / cookieImage`、`ScenePresetAdditionalLight.GetCookie()` / `SetCookie(LightCookieData)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトの輪郭属性を固定する</summary>
    public class ScenePresetLightCookieTests
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
        public void 既定の輪郭は属性を書き出さない()
        {
            var text = Serialize(new ScenePresetAdditionalLight());

            Assert.DoesNotContain("cookie", text);
        }

        [Fact]
        public void 画像指定を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetCookie(new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = "window.png" });

            var text = Serialize(light);
            var restored = Deserialize(text).GetCookie();

            Assert.Contains("cookieImage=\"window.png\"", text);
            Assert.DoesNotContain("cookieHardness", text);
            Assert.Equal(LightCookieMode.Image, restored.mode);
            Assert.Equal("window.png", restored.image);
        }

        [Fact]
        public void 硬さ指定を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetCookie(new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.25f });

            var restored = Deserialize(Serialize(light)).GetCookie();

            Assert.Equal(LightCookieMode.Generated, restored.mode);
            Assert.Equal(0.25f, restored.hardness);
        }

        [Fact]
        public void 属性の無い旧プリセットは既定の輪郭として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.True(Deserialize(text).GetCookie().Equals(LightCookieData.Default));
        }
    }
}
```

`ScenePresetEffectsTests.cs:149` の `Assert.Equal(36, ScenePresetData.CurrentVersion);` を `Assert.Equal(37, ScenePresetData.CurrentVersion);` に変える。

- [ ] **Step 2: テストが失敗することを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~ScenePreset"`
Expected: コンパイルエラー (`SetCookie` が無い)

- [ ] **Step 3: `ScenePresetAdditionalLight` に属性を足す**

`public Vector3 followOffset = Vector3.zero;` の後に:

```csharp

        // 輪郭 (cookie)。旧プリセットには無いので既定の輪郭で読み、既定では書き出さない
        [XmlAttribute]
        public LightCookieMode cookieMode = LightCookieMode.Default;
        [XmlAttribute]
        public float cookieHardness = LightCookieData.DefaultHardness;
        [XmlAttribute]
        public string cookieImage;

        public bool ShouldSerializecookieMode() { return cookieMode != LightCookieMode.Default; }
        public bool ShouldSerializecookieHardness() { return cookieMode == LightCookieMode.Generated; }
        public bool ShouldSerializecookieImage()
        {
            return cookieMode == LightCookieMode.Image && !string.IsNullOrEmpty(cookieImage);
        }

        public LightCookieData GetCookie()
        {
            return new LightCookieData { mode = cookieMode, hardness = cookieHardness, image = cookieImage }.Normalized();
        }

        public void SetCookie(LightCookieData cookie)
        {
            cookieMode = cookie.mode;
            cookieHardness = cookie.hardness;
            cookieImage = cookie.image;
        }
```

`CurrentVersion` の直前の変更履歴コメントに 1 行足し、値を 37 にする:

```csharp
        // v37: 追加ライトに輪郭 (cookieMode / cookieHardness / cookieImage) を追加。既定の輪郭では書き出さない。
        //      旧形式は属性が無く既定の輪郭として読める
        public static readonly int CurrentVersion = 37;
```

- [ ] **Step 4: `LightSnapshot` を変更する**

`CaptureState` の `new ScenePresetAdditionalLight { ... }` を変数に受けてから輪郭を入れる:

```csharp
                var lightState = new ScenePresetAdditionalLight
                {
                    // (既存の初期化子はそのまま)
                };
                lightState.SetCookie(LightCookie.Get(light));
                state.additionalLights.Add(lightState);
```

`ApplyLightState` の `light.shadowBias = lightState.shadowBias;` の後に:

```csharp
            LightCookie.Set(light, lightState.GetCookie());
```

`Approximately` の追加ライト比較の条件末尾 (`|| a.followOffset != b.followOffset`) に `|| !a.GetCookie().Equals(b.GetCookie())` を足す。

- [ ] **Step 5: `LightClipboard` を変更する**

- `Data` に `public LightCookieData cookie;` を足す
- `Copy` の初期化子に `cookie = LightCookie.Get(light),`
- `CopyMain` の初期化子に `cookie = LightCookieData.Default,`
- `Paste` の `light.cullingMask = ...;` の後に `LightCookie.Set(light, _data.cookie);`

- [ ] **Step 6: テストが通ることを確認する**

Run: MSBuild (COM3D25) → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全件 PASS

- [ ] **Step 7: コミット** (例: `feat(light): 輪郭をシーンプリセット・Undo・コピーに含める`)

---

### Task 5: ライト設定の UI

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs`

**Interfaces:**
- Consumes: `LightCookie.Get / Set`、`LightCookieTextures.GetImageNames / Reload / directory`、`StudioLightManager.ReapplyCookies`、`MTEP.StudioLightManager.instance.LateUpdate(true)`

- [ ] **Step 1: 定数とコンボを足す**

`LightTargetComboWidth` の後に:

```csharp
        /// <summary>輪郭画像のコンボの幅</summary>
        private const float CookieImageComboWidth = 160f;

        /// <summary>輪郭画像の再読込ボタンの幅</summary>
        private const float CookieReloadButtonWidth = 60f;
```

`_followMaidComboBox` の後に:

```csharp
        /// <summary>輪郭画像のコンボ。LightCookie フォルダからの相対パスを並べる</summary>
        private readonly GUIComboBox<string> _cookieImageComboBox =
            new GUIComboBox<string>
            {
                getName = (name, _) => name,
                defaultName = "未選択",
                contentSize = new Vector2(220, 300),
            };
```

- [ ] **Step 2: スポットの角度スライダーの後に輪郭の行を出す**

```csharp
            if (light.type == LightType.Spot)
            {
                DrawAxisSlider(view, labelWidth, "角度", light.spotAngle, 1f, 179f, 0.1f,
                    StudioLightManager.DefaultSpotAngle, value => light.spotAngle = value);
                DrawCookieRows(view, labelWidth, rowHeight, light);
            }
```

- [ ] **Step 3: 描画メソッドを足す** (`DrawLightTargetRow` の後)

```csharp
        /// <summary>
        /// スポットの輪郭 (cookie)。角度を広げると内蔵の輪郭はぼけ幅も広がるため、
        /// 硬さの指定か画像で縁を決められるようにする
        /// </summary>
        private void DrawCookieRows(GUIView view, float labelWidth, float rowHeight, Light light)
        {
            var cookie = LightCookie.Get(light);

            view.BeginHorizontal();
            {
                view.DrawLabel("輪郭", labelWidth, rowHeight);
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Default, "既定");
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Generated, "硬さ");
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Image, "画像");
            }
            view.EndLayout();

            if (cookie.mode == LightCookieMode.Generated)
            {
                DrawAxisSlider(view, labelWidth, "硬さ", cookie.hardness, 0f, 1f, 0.01f,
                    LightCookieData.DefaultHardness,
                    value =>
                    {
                        cookie.hardness = value;
                        SetCookie(light, cookie);
                    });
            }
            else if (cookie.mode == LightCookieMode.Image)
            {
                DrawCookieImageRow(view, labelWidth, rowHeight, light, cookie);
            }
        }

        private static void DrawCookieModeButton(
            GUIView view, float rowHeight, Light light, LightCookieData cookie, LightCookieMode mode, string label)
        {
            var isCurrent = cookie.mode == mode;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("輪郭");
                cookie.mode = mode;
                SetCookie(light, cookie);
            }
        }

        private void DrawCookieImageRow(
            GUIView view, float labelWidth, float rowHeight, Light light, LightCookieData cookie)
        {
            var names = LightCookieTextures.GetImageNames();

            view.BeginHorizontal();
            {
                view.DrawLabel("画像", labelWidth, rowHeight);

                _cookieImageComboBox.items = names;
                _cookieImageComboBox.buttonSize = new Vector2(CookieImageComboWidth, rowHeight);
                // 履歴の復元等で外から変わるため、描画のたびに実体から選択位置を取り直す
                _cookieImageComboBox.currentItem = cookie.image;
                _cookieImageComboBox.onSelected = (name, _) =>
                {
                    RecordLightEdit("輪郭画像");
                    cookie.image = name;
                    SetCookie(light, cookie);
                };
                _cookieImageComboBox.DrawButton(view);

                if (view.DrawButton("再読込", CookieReloadButtonWidth, rowHeight))
                {
                    LightCookieTextures.Reload();
                    lightManager.ReapplyCookies();
                }
            }
            view.EndLayout();

            if (names.Count == 0)
            {
                view.DrawLabel("PNG を置いてください: " + LightCookieTextures.directory, -1, rowHeight);
            }
        }

        /// <summary>
        /// 輪郭を反映し、タイムラインのライト定義へ即時に同期させる
        /// (定期収集は 30 フレーム間隔なので、直後の保存で古い値が書かれないように)
        /// </summary>
        private static void SetCookie(Light light, LightCookieData cookie)
        {
            LightCookie.Set(light, cookie);
            MTEP.StudioLightManager.instance.LateUpdate(true);
        }
```

注: `DrawAxisSlider` は `onChanged` の前に `RecordLightEdit(label)` を呼ぶので、硬さスライダーで履歴を二重に積まないこと。

- [ ] **Step 4: 両構成でビルドする**

Run: MSBuild を COM3D25 / COM3D2 の両方で
Expected: エラー 0。`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` も全件 PASS

- [ ] **Step 5: コミット** (例: `feat(light): スポットライトの輪郭を設定する UI を追加する`)

---

### Task 6: ドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md` (ライトウィンドウの節、`:56-80` 付近)
- Modify: `docs-site/timeline/compatibility.md` (SE 独自項目の一覧、ModelLayer の行 `:18` 付近)
- Modify: `W:\COM3D2_5\work\CLAUDE.md` (「タイムライン XML の互換方向」の箇条書き。ワークスペース直下で git 管理外なのでコミット対象外)

- [ ] **Step 1: `staging.md` のライトの項目説明にスポットの輪郭を足す** (既存の書式に合わせる)

```markdown
- **輪郭**（スポットのみ）: 光の輪の縁の決め方。
  - 既定: ゲーム標準の縁。角度を広げるほど縁が大きくぼける
  - 硬さ: スライダーで縁の硬さを決める。1 に近いほどくっきりした円になる
  - 画像: `Sybaris\UnityInjector\Config\SceneEditor\LightCookie` の PNG を光の形として投影する（窓枠・模様など）。
    透過付きの画像はアルファを、透過の無い画像は明るさを使う。色は反映されない。画像の外周 1px は光らない。
    PNG を追加したら「再読込」を押す
```

(配置先のパスは `PluginUtils.PluginDataPath` の実体。COM3D2 の構成で `Sybaris` 以外になる場合は既存の記述に合わせる)

- [ ] **Step 2: `compatibility.md` に 1 行足す**

```markdown
| ライト定義の輪郭（`CookieMode` / `CookieHardness` / `CookieImage`） | SE 独自。既定の輪郭では書き出さない。MTE は要素を読み飛ばし、既定の輪郭で表示する |
```

(表の列構成は既存の行に合わせる)

- [ ] **Step 3: ワークスペースの `CLAUDE.md` に 1 行足す** (PNG 彩度の行の後)

```markdown
- ライト定義（`<Lights>` の `<Light>`）の輪郭（`CookieMode` / `CookieHardness` / `CookieImage`）は SE 独自。既定の輪郭（cookie なし）では書き出さない。MTE は要素を読み飛ばすため Unity 内蔵の輪郭で表示される。シーンプリセットは v37 で同名の属性を追加
```

- [ ] **Step 4: コミット** (例: `docs(light): スポットライトの輪郭設定を説明する`。CLAUDE.md は別リポジトリ外なので含めない)

---

### Task 7: 実機確認

**Files:** なし (確認のみ)

- [ ] **Step 1: restart-verify スキルで DLL を反映し、ゲームを起動してセーブをロードする** (ゲームを落とす前にユーザーへ確認する)
- [ ] **Step 2: 確認用の画像を置く**: `Config/SceneEditor/LightCookie/test_hard.png` (中央に白い円・外は黒、透過なし) を置く
- [ ] **Step 3: devbridge で確認する**
  - 追加ライトをスポット・角度 120° で床に当て、輪郭「既定」→「硬さ 0.8」→「硬さ 1」で `screenshot` を撮り、縁が硬くなることを確認する
  - 「画像」で `test_hard.png` を選び、円形に投影されることを確認する
  - 種別をポイントへ変え、`eval_csharp` で `light.cookie == null` を確認する。スポットへ戻すと cookie が戻ること
  - Undo / Redo で輪郭が戻ること、ライトのコピー → 別ライトへペーストで輪郭が写ること
  - タイムラインを保存 → 読み直しで輪郭が復元されること。保存した XML の `<Light>` に `CookieMode` が出ていること、既定のライトには出ていないこと
  - シーンプリセットを保存 → 読込で輪郭が復元されること
  - `tail_log` に例外が無いこと
- [ ] **Step 4: 後片付け**: 確認用の PNG とテスト用ライトを削除する

## レビュー却下メモ

- 「硬さ」スライダーのドラッグ中に毎フレーム `LateUpdate(true)` が走る — 意図どおり（輪郭はキー化しない定義値で、直後の保存に古い値が載らないよう即時同期する）。根拠は Task 5 の `SetCookie` の XML コメントに記載済み。追加ライトは数灯でコストは小さい
