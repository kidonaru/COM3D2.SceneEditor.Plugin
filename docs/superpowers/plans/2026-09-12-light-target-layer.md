# 追加ライトの照射対象（キャラ / 背景）切替 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 追加ライトごとに「全て / キャラのみ / 背景のみ」を選べるようにし、プリセット・履歴・タイムラインキーにも保存する。

**Architecture:** Unity `Light.cullingMask` を「照射対象モード」（int 0/1/2）で抽象化する静的ヘルパー `LightTarget` を追加し、UI・プリセット・履歴・タイムラインは全てこのモード値だけを扱う。実レイヤー番号はゲーム側の `LayerMask.NameToLayer` から実行時に引く。タイムラインは `TransformDataLight` に値を 1 つ追加し、旧 XML（18 値）は `_valuesForXml` の 0 埋めで「全て」になる。

**Tech Stack:** C# / Unity (COM3D2.5)、xunit（`source/COM3D2.SceneEditor.Plugin.Tests`）

**Spec:** 本計画の「仕様」節（別 spec なし）

## 仕様

- 対象は**追加ライトのみ**。メインライト（`GameMain.Instance.MainLight`）はゲーム側の恒久オブジェクトなので触らない（メイド追従と同じ方針）
- モード値 `LightTargetMode`: `All = 0`, `Character = 1`, `Background = 2`
- 実機確認済みのレイヤー: メイド本体・衣装 = `Charactor`(10)、顔 = `Face`(11)、男 = `Man`(12)。キャラ用マスクはこの 3 つの OR。背景用はその補集合
- `All` は `-1`（Unity 新規 Light の既定値。`StudioLightManager.AddLight` の現状と同じ）
- `cullingMask` → モードの逆引きは「キャラ用マスクと完全一致 → Character、補集合と完全一致 → Background、それ以外 → All」
- タイムライン: `TransformDataLight` の末尾に `lightTarget`（index 18）を追加。補間せず開始キーの値を適用。メインライト（`stat.index == 0`）には適用しない
- プリセット XML: `ScenePresetAdditionalLight.target`（XmlAttribute、既定 0）。旧プリセットは要素が無いので 0 = All
- 履歴（`LightSnapshot`）: `target` を比較・復元に含める
- UI（`LightRowDrawer.DrawAdditionalLightParams`）: 「種別」行の直下に「対象」行を置き、種別ボタンと同じ 3 ボタン（全て / キャラ / 背景）で切替。履歴ラベルは「対象」
- キーフレーム詳細（Inspector）は `CustomValueInfo`（0〜2 の int スライダー、名前「対象」）で編集できる。ポストエフェクトの `maskMode` と同じ扱い

## Global Constraints

- コメント・ログ文言は日本語
- レイヤー番号（10/11/12）はハードコードせず `LayerMask.NameToLayer` で解決する
- 2 構成（COM3D2 / COM3D25）とも MSBuild でビルドを通す。`debug.bat` は使わない（実機へコピーされるため）
- git worktree は使わない

## File Structure

| ファイル | 責務 |
|---|---|
| Create: `source/COM3D2.SceneEditor.Plugin/LightTarget.cs` | モード enum と `cullingMask` 相互変換 |
| Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:72-84` | `target` フィールド追加 |
| Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs` | 記録・復元・比較に `target` を追加 |
| Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs:120-134` | 「対象」行の追加 |
| Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs` | `Index.LightTarget = 18`、`valueCount = 19`、カスタム値 `lightTarget` |
| Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs:106-128, 237-265` | キー適用・キー書き込み |
| Create: `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs` | 変換ロジックとタイムライン値のテスト |
| Modify: `CHANGELOG.md` | 未リリース節へ追記 |

---

### Task 1: `LightTarget` ヘルパーとテスト

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LightTarget.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs`

**Interfaces:**
- Produces:
  - `enum LightTargetMode { All = 0, Character = 1, Background = 2 }`（namespace `COM3D2.SceneEditor.Plugin`）
  - `static class LightTarget` に `int ToCullingMask(LightTargetMode mode)`, `LightTargetMode FromCullingMask(int cullingMask)`, `LightTargetMode ClampMode(int value)`, `int ToCullingMask(LightTargetMode mode, int characterMask)`, `LightTargetMode FromCullingMask(int cullingMask, int characterMask)`
  - 2 引数版はテスト用に `characterMask` を注入する。1 引数版は `CharacterMask`（実行時に `LayerMask.NameToLayer` で解決）を渡すだけ

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs`:

```csharp
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightTargetTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        [Fact]
        public void 全て_は全レイヤー()
        {
            Assert.Equal(-1, LightTarget.ToCullingMask(LightTargetMode.All, CharacterMask));
        }

        [Fact]
        public void キャラのみ_はキャラ用マスク()
        {
            Assert.Equal(CharacterMask,
                LightTarget.ToCullingMask(LightTargetMode.Character, CharacterMask));
        }

        [Fact]
        public void 背景のみ_はキャラ用マスクの補集合()
        {
            Assert.Equal(~CharacterMask,
                LightTarget.ToCullingMask(LightTargetMode.Background, CharacterMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 往復変換で元のモードに戻る(LightTargetMode mode)
        {
            var mask = LightTarget.ToCullingMask(mode, CharacterMask);
            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask));
        }

        [Fact]
        public void 想定外のマスクは全て扱い()
        {
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(1 << 10, CharacterMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(0, CharacterMask));
        }

        [Theory]
        [InlineData(-1, LightTargetMode.All)]
        [InlineData(0, LightTargetMode.All)]
        [InlineData(1, LightTargetMode.Character)]
        [InlineData(2, LightTargetMode.Background)]
        [InlineData(3, LightTargetMode.All)]
        public void 範囲外の整数は全てへ丸める(int value, LightTargetMode expected)
        {
            Assert.Equal(expected, LightTarget.ClampMode(value));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

先にプラグインを COM3D25 構成でビルドする（テストは DLL 参照）:

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
MSBuild COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test COM3D2.SceneEditor.Plugin.Tests --filter LightTargetTests
```

Expected: ビルドエラー（`LightTarget` が未定義）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/LightTarget.cs`:

```csharp
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>追加ライトが照らす対象。値はタイムラインキー・プリセットにそのまま保存される</summary>
    public enum LightTargetMode
    {
        All = 0,
        Character = 1,
        Background = 2,
    }

    /// <summary>
    /// 照射対象モードと Light.cullingMask の相互変換。
    /// ゲーム側はメイドを Charactor / Face、男を Man レイヤーに置くため、この 3 つをキャラ用とみなす
    /// （CharaDirectionalLight が Charactor だけを照らすのと同じ仕組み）
    /// </summary>
    public static class LightTarget
    {
        private static readonly string[] CharacterLayerNames = { "Charactor", "Face", "Man" };

        private static int _characterMask = 0;

        /// <summary>キャラ用レイヤーのマスク。レイヤー名の解決は初回だけ行う</summary>
        public static int CharacterMask
        {
            get
            {
                if (_characterMask == 0)
                {
                    foreach (var layerName in CharacterLayerNames)
                    {
                        var layer = LayerMask.NameToLayer(layerName);
                        if (layer >= 0)
                        {
                            _characterMask |= 1 << layer;
                        }
                    }
                }
                return _characterMask;
            }
        }

        public static int ToCullingMask(LightTargetMode mode) => ToCullingMask(mode, CharacterMask);

        public static LightTargetMode FromCullingMask(int cullingMask)
            => FromCullingMask(cullingMask, CharacterMask);

        /// <summary>characterMask を注入する版（テスト用）</summary>
        public static int ToCullingMask(LightTargetMode mode, int characterMask)
        {
            switch (mode)
            {
                case LightTargetMode.Character:
                    return characterMask;
                case LightTargetMode.Background:
                    return ~characterMask;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 実マスクからモードへ逆引きする。
        /// 本プラグイン以外が書いたマスクは判別できないため「全て」として扱う
        /// </summary>
        public static LightTargetMode FromCullingMask(int cullingMask, int characterMask)
        {
            if (cullingMask == characterMask)
            {
                return LightTargetMode.Character;
            }
            if (cullingMask == ~characterMask)
            {
                return LightTargetMode.Background;
            }
            return LightTargetMode.All;
        }

        /// <summary>XML 等から読んだ整数を有効なモードへ丸める。範囲外は「全て」</summary>
        public static LightTargetMode ClampMode(int value)
        {
            return value == (int)LightTargetMode.Character || value == (int)LightTargetMode.Background
                ? (LightTargetMode)value
                : LightTargetMode.All;
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Step 2 と同じコマンド。Expected: 全件 PASS

- [ ] **Step 5: コミット**

```
git add source/COM3D2.SceneEditor.Plugin/LightTarget.cs source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs
git commit -m "feat(light): 照射対象モードと cullingMask の変換ヘルパーを追加する"
```

---

### Task 2: プリセット・履歴に照射対象を保存する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:72-84`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs:45-56, 112-127, 155-170`

**Interfaces:**
- Consumes: `LightTarget.FromCullingMask(int)`, `LightTarget.ToCullingMask(LightTargetMode)`, `LightTarget.ClampMode(int)`
- Produces: `ScenePresetAdditionalLight.target`（int、XmlAttribute、既定 0）

- [ ] **Step 1: `ScenePresetAdditionalLight` にフィールドを追加する**

`ScenePresetData.cs` の `public bool enabled = true;` の直後に追加:

```csharp
        /// <summary>照射対象 (LightTargetMode の数値: 0=全て / 1=キャラのみ / 2=背景のみ)。旧プリセットは 0</summary>
        [XmlAttribute]
        public int target = (int)LightTargetMode.All;
```

- [ ] **Step 2: `LightSnapshot` の記録・復元・比較に追加する**

`CaptureState` の `enabled = light.enabled,` の直後:

```csharp
                    // 本プラグインが書いたマスク以外は判別できないため「全て」として記録する
                    target = (int)LightTarget.FromCullingMask(light.cullingMask),
```

`ApplyLightState` の `light.enabled = lightState.enabled;` の直後:

```csharp
            light.cullingMask = LightTarget.ToCullingMask(LightTarget.ClampMode(lightState.target));
```

`Approximately` の `|| !Mathf.Approximately(a.spotAngle, b.spotAngle))` を:

```csharp
                    || !Mathf.Approximately(a.spotAngle, b.spotAngle)
                    || a.target != b.target)
```

- [ ] **Step 3: 両構成でビルドする**

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
MSBuild COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
MSBuild COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
```

Expected: どちらも警告のみでエラー 0

- [ ] **Step 4: コミット**

```
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs
git commit -m "feat(light): 照射対象をプリセットと履歴に保存する"
```

---

### Task 3: ライト行 UI に「対象」行を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs:120-134, 286-297`

**Interfaces:**
- Consumes: `LightTarget.ToCullingMask` / `FromCullingMask`, 既存 `RecordLightEdit(string)`, `TypeButtonWidth`

- [ ] **Step 1: 「種別」行の直下に「対象」行を描く**

`DrawAdditionalLightParams` 内、種別の `view.EndLayout();` と `view.DrawToggle("有効", ...)` の間に追加:

```csharp
            // 照射対象。CharaDirectionalLight と同じ cullingMask の切替で、キャラ用/背景用のライトを分ける
            view.BeginHorizontal();
            {
                view.DrawLabel("対象", labelWidth, rowHeight);
                DrawLightTargetButton(view, rowHeight, light, LightTargetMode.All, "全て");
                DrawLightTargetButton(view, rowHeight, light, LightTargetMode.Character, "キャラ");
                DrawLightTargetButton(view, rowHeight, light, LightTargetMode.Background, "背景");
            }
            view.EndLayout();
```

- [ ] **Step 2: ボタン描画メソッドを追加する**

`DrawLightTypeButton` の直後に追加:

```csharp
        /// <summary>照射対象切替ボタン 1 つ。種別ボタンと同じ見た目で選択中を示す</summary>
        private static void DrawLightTargetButton(
            GUIView view, float rowHeight, Light light, LightTargetMode mode, string label)
        {
            var isCurrent = LightTarget.FromCullingMask(light.cullingMask) == mode;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("対象");
                light.cullingMask = LightTarget.ToCullingMask(mode);
            }
        }
```

- [ ] **Step 3: 両構成でビルドする**

Task 2 Step 3 と同じ 2 コマンド。Expected: エラー 0

- [ ] **Step 4: 実機確認（ゲーム起動中のみ）**

MCP `com3d25-devbridge` の `eval_csharp` で、ライトウィンドウから追加ライトを 1 灯作り「キャラ」を押したあと:

```csharp
var l = UnityEngine.GameObject.Find("SceneEditorLightRoot").GetComponentInChildren<UnityEngine.Light>();
l.cullingMask
```

Expected: `7168`（= 1<<10 | 1<<11 | 1<<12）。「背景」で `-7169`、「全て」で `-1`。
Undo でボタンの選択状態が戻ることも確認する

- [ ] **Step 5: コミット**

```
git add source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs
git commit -m "feat(light): ライト行に照射対象（全て/キャラ/背景）の切替を追加する"
```

---

### Task 4: タイムラインのライトキーに照射対象を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs:106-128, 237-265`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs`

**Interfaces:**
- Consumes: `LightTarget.*`, `TransformDataBase._valuesForXml`（0 埋め仕様）
- Produces: `TransformDataLight.Index.LightTarget = 18`、`TransformDataLight.lightTarget`（int プロパティ）、`TransformDataLight.lightTargetValue`

- [ ] **Step 1: 失敗するテストを書く**

`LightTargetTests.cs` の末尾（クラス内）に追加。`using COM3D2.MotionTimelineEditor.Plugin;` をファイル先頭に足す:

```csharp
        [Fact]
        public void ライトキーの値数は19で末尾が対象()
        {
            var trans = new TransformDataLight();
            Assert.Equal(19, trans.valueCount);
            Assert.Equal(18, (int)TransformDataLight.Index.LightTarget);
            Assert.True(trans.GetCustomValueInfoMap().ContainsKey("lightTarget"));
        }

        [Fact]
        public void 旧XMLの18値を読むと対象は全て()
        {
            var trans = new TransformDataLight();
            trans.Initialize("Light");
            var xml = new TransformXml
            {
                name = "Light",
                type = TransformType.Light,
                values = new float[18],
                inTangents = new float[18],
                outTangents = new float[18],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
            trans.FromXml(xml);
            Assert.Equal((int)LightTargetMode.All, trans.lightTarget);
        }
```

> `Initialize` / `FromXml` / `TransformXml` のフィールド名は `TransformDataBase.cs:790-811` の `FromXml(TransformXml xml)` と `TransformXml` クラスを開いて実際の名前に合わせる。`Initialize` が無ければ `values` を確保する既存の初期化メソッド（`TransformDataBase` 内で `values = new ValueData[valueCount]` している箇所）を使う

- [ ] **Step 2: テストが失敗することを確認する**

Task 1 Step 2 と同じコマンド。Expected: `Index.LightTarget` 未定義でビルドエラー

- [ ] **Step 3: `TransformDataLight` を拡張する**

`Index` enum の `Visible = 17` の後に `LightTarget = 18` を追加し、`valueCount => 19` にする。

`CustomValueInfoMap` の `maidSlotNo` エントリの後に追加:

```csharp
            {
                "lightTarget", new CustomValueInfo
                {
                    index = (int)Index.LightTarget,
                    name = "対象",
                    // 0=全て / 1=キャラのみ / 2=背景のみ (LightTargetMode)。実体は int なので丸めて渡す
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
```

値アクセサに追加:

```csharp
        public ValueData lightTargetValue => values[(int)Index.LightTarget];
```

プロパティアクセサに追加:

```csharp
        public int lightTarget
        {
            get => lightTargetValue.intValue;
            set => lightTargetValue.intValue = value;
        }
```

- [ ] **Step 4: `LightTimelineLayer` でキー適用・書き込みする**

`ApplyMotionInit` の `light.shadowBias = start.shadowBias;` の直後:

```csharp
            // 照射対象は補間しない。メインライト (index 0) はゲーム側の恒久オブジェクトのため触らない
            if (stat.index > 0)
            {
                light.cullingMask = LightTarget.ToCullingMask(LightTarget.ClampMode(start.lightTarget));
            }
```

`UpdateFrame` の `trans.maidSlotNo = followLight.maidSlotNo;` の直後:

```csharp
                // 本プラグインが書いたマスク以外は判別できないため「全て」としてキー化する
                trans.lightTarget = (int)LightTarget.FromCullingMask(light.cullingMask);
```

`LightTimelineLayer.cs` の namespace は `COM3D2.MotionTimelineEditor.Plugin` なので、ファイル先頭に `using COM3D2.SceneEditor.Plugin;` を追加する（同ファイル内で SE 側の型をまだ参照していない場合）。

- [ ] **Step 5: テストが通り、両構成でビルドできることを確認する**

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
MSBuild COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
MSBuild COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
dotnet test COM3D2.SceneEditor.Plugin.Tests
```

Expected: ビルドエラー 0、テスト全件 PASS（既存の `TangentUnificationTests` の `TransformType.Light` ケースも含む）

- [ ] **Step 6: 実機確認（ゲーム起動中のみ）**

タイムラインウィンドウでライトレイヤーを開き、追加ライトを「キャラ」にしてキーを打ち、別フレームで「全て」にしてキーを打つ。再生してフレームをまたぐと `cullingMask` が切り替わることを Task 3 Step 4 の式で確認する。

- [ ] **Step 7: コミット**

```
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/LightTimelineLayer.cs source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs
git commit -m "feat(timeline): ライトキーに照射対象を追加する"
```

---

### Task 5: CHANGELOG

**Files:**
- Modify: `CHANGELOG.md`（`## 未リリース` 節の先頭）

- [ ] **Step 1: 追記する**

```markdown
- 追加ライトごとに照らす対象を「全て / キャラ / 背景」から選べるようになりました
  - ライトウィンドウと Inspector のライト行に「対象」ボタンが並びます
  - シーンプリセット・Undo 履歴・タイムラインのライトキーにも保存されます
  - メインライトは対象外です
```

- [ ] **Step 2: コミット**

```
git add CHANGELOG.md
git commit -m "docs: 追加ライトの照射対象切替を変更履歴に追記する"
```

## Self-Review

- 仕様の各項目 → Task 1（変換）、Task 2（プリセット・履歴）、Task 3（UI）、Task 4（タイムライン・旧 XML 互換）、Task 5（CHANGELOG）で網羅
- 型名: `LightTargetMode` / `LightTarget.ToCullingMask` / `FromCullingMask` / `ClampMode` / `TransformDataLight.lightTarget` / `Index.LightTarget` / `ScenePresetAdditionalLight.target` を全 Task で統一
- `SceneCapturePresetLoader`（外部プリセット取り込み）は `target` を持たないため既定 0 のまま。変更不要
- 既知の制約: `lightTarget` は `tangentValues`（= 色以外の全値）に含まれるため、カーブエディタにチャンネルが出るが適用は開始キー値のみ。既存の `maidSlotNo` と同じ扱いで前例に従う

## レビュー却下メモ

- コピー直後（`OnCopyLight` 後、次の `ApplyMotion` まで）に新ライトが一瞬「全て」になる — 次フレームの `ApplyMotionInit` で補正される既存の `maidSlotNo` 等と同じ挙動で実害なし。未確認のまま見送り
- `stat.index == 0` で `lightTarget` を無視する単体テストが無い — `ApplyMotionInit` は private かつ Unity 実体依存で単体テスト不可。Task 4 Step 6 の実機確認で担保する
- `lightTarget` をタンジェント補間対象から外すべき — `maidSlotNo` も同様に含まれており、切り分けは本機能の範囲外（Self-Review に既知の制約として記載）
