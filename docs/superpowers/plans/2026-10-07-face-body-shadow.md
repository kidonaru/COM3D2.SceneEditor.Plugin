# 顔と体の影・照明の分離 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイドの顔（head スロット）だけを対象に、(A) 影を受けるか、(B) 影を落とすかをキーフレームで切り替えられるようにする。あわせて (C) 追加ライトの照射対象に「顔のみ」「体のみ」を足し、顔と体を別のライトで照らせるようにする。

**Architecture:**
- **照射対象 (C)**: `LightTargetMode` に `Face = 3` / `Body = 4` を足す。`Face` は Face レイヤーのビットだけ、`Body` はキャラ用マスクから Face を除いたもの。ゲームは通常、顔も `Charactor` レイヤーに置く（Face レイヤーへ移すのは VR の一人称モードで頭を VR カメラから隠すときだけ。`OvrIK.cs:930`）。そのため新しい `MaidFaceRendererManager` が、「顔のみ／体のみ」の追加ライトが 1 灯でも有効な間だけ、各メイドの head スロットの Renderer を `Charactor` → `Face` へ移し、無くなれば戻す。移している間は、`Charactor` を照らすのに `Face` を照らさないゲーム側のライト（`CharaDirectionalLight` など。SE の追加ライトは除く）に Face ビットを足し、顔が暗くならないようにする。
- **顔の影 (A/B)**: 同じ `MaidFaceRendererManager` がメイドごとの上書き（`receiveShadows` / `castShadows` の 2 項目、bool）を持ち、head スロットの Renderer の `receiveShadows` / `shadowCastingMode` に書く。元の値は Renderer ごとに退避し、上書きを外すと戻す。着替えで head スロットが作り直されたら、新しい Renderer へ書き直す。新レイヤー「メイド顔の影」（`FaceShadowTimelineLayer`、`TransformType.FaceShadow`）でキー化し、Undo とシーンプリセットにも載せる。UI は表情ウィンドウの新しいタブ「影」。
- **キャラの影の拡張**: 影専用の複製（`CharacterShadowProxyManager`）を、「背景のみ」に加えて「顔のみ」「体のみ」のライトでも使う。「顔のみ」のライトでも髪や体の影が顔に落ちるようにするため。
- **PostEffects**: PostEffects のキャラ用マスク（`CharacterMask`）と輪郭の色にじみ（`OutlineColorEffect`）は `Charactor` レイヤーしか見ていない。このままでは顔を Face へ移すと、AO の除外などで顔が背景扱いになる。両方を `Charactor | Face` に直す。メイド非表示は元から Face を含むので変えない。

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Unity 2022（COM3D2.5）、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）

**Spec:** この計画自体が仕様を兼ねる。ユーザーの決定（2026-10-07）:
- A/B/C をすべて入れる。PostEffects 側も対応する
- C で顔として扱う範囲: **head スロットだけ**（顔の肌・目・眉・口）。髪・帽子・メガネは体側
- A/B の持ち方: **キーフレームで切り替える**（新しいレイヤー）
- C のレイヤー移動: **必要なときだけ**（「顔のみ／体のみ」の追加ライトが有効な間だけ移す）
- 「キャラの影」: **「顔のみ／体のみ」にも広げる**

## 調査で確定した事実

- 実機（2026-10-07、通常モード、旧ボディ）: 顔 `Face7` は `Charactor` レイヤー、`shadowCastingMode = Off`、`receiveShadows = True`。体・髪・服は `Charactor` で `On` / `True`。旧計画 `2026-09-12-light-target-layer.md:17` の「顔 = Face(11)」は誤り（VR モードの状態）。**新ボディ（CRC）は未確認なので Task 8 で確かめる**
- 旧ボディでは瞳も `Face7` のマテリアル（`EyeL` / `EyeR`）で、`eye` スロットは空。head スロットだけで顔全体になる
- 顔のマテリアルのうち影を受けるのは描画順 2000 の肌（`Skin`）と口だけ。目・眉・ハイライト（描画順 3000、`*_Trans*`）は Unity の仕様で元から影を受けない
- `Face` レイヤーは 11。メインカメラ `CameraMain`・SceneView カメラ・`MainDirectionalLight` の cullingMask は Face を含む。`CharaDirectionalLight`（無効なことが多い）は `0x400` = `Charactor` だけ
- ゲーム本体で `Charactor` / `Face` を名前で参照するのは `OvrIK`（VR）、`TBodySkin.LayerCheck`（VR）、`DanceMain:921`、`PhotoBooth.Effect.EffectManager`（`{"Charactor","Face"}` の両方）だけ
- PostEffects（`W:\COM3D2_5\work\COM3D2.PostEffects.Plugin`、v2.5.0.0 は未リリース。最新タグ v2.2.0.0）:
  - `Effects\CharacterMask.cs:19-26` のマスクは `Charactor` だけ。これを使う AO（既定でキャラ除外）、分離ブルーム、Paraffin / Rimlight のマスクモード、`CharacterMaskableEffect` 派生 9 種で、顔が背景扱いになる
  - `Effects\OutlineColorEffect.cs:45,86` は `layer != _characterLayer` で除外している
  - メイド非表示（`MaidHideEffect.cs:14`）は `Charactor | Face`、リムライトの「顔にかけない」（`HeadMask.cs`）はスロットで判定するので影響なし
  - Renderer の影設定・Light.cullingMask には触らない

## Global Constraints

- **顔の範囲**: メイドの `head` スロット（`SlotBoneManager.GetSlotObject(maid, "head")`）配下の全 Renderer。男・髪・アクセサリは対象外
- **レイヤー番号**: ハードコードせず `LayerMask.NameToLayer("Charactor")` / `("Face")` で解決する
- **レイヤーを移すのは `Charactor` にある Renderer だけ**。ほかのレイヤー（VR で Face にいる、他プラグインが変えた等）は触らない
- **照射対象の値**: `0=全て / 1=キャラのみ / 2=背景のみ / 3=顔のみ / 4=体のみ`。タイムラインキー（`TransformDataLight` index 18）・プリセット属性 `target` にそのまま入る。範囲外は「全て」
- **「体のみ」**: キャラ用マスク（`Charactor | Face | Man`）から Face を除いたもの。男は全身が体側
- **顔の影の項目名**: `receiveShadows`（顔が影を受ける。ゲームの既定 ON）、`castShadows`（顔が影を落とす。ゲームの既定 OFF）。上書きが無い項目はゲームの値のまま
- **タイムライン XML**: 新しい `TransformType.FaceShadow` を使う。version は上げない（NodeVisibility と同じ扱い）。MTE・旧 SE では未知の enum 値で読込に失敗する
- **シーンプリセット**: v42 に上げる。追加ライトの `target` に 3 / 4 が入りうる（旧 SE は「全て」で読む）。maid に `faceShadow` 要素（上書きのある項目だけ `<item name enabled>`、上書きが無くても空要素を書く）。要素の無い旧プリセットは適用時に触らない
- **言語**: コードのコメント・ログは日本語
- **ビルド**: 両構成（COM3D2 → COM3D25 の順）でビルドする。`debug.bat` / `deploy.bat` は実行しない
- **.NET 3.5**: 入力 5 個以上の `Func` / `Action` は使えない。タプルも使えない

## 決定事項

| 論点 | 決定 | 理由 |
|---|---|---|
| A/B と C の実装の置き場所 | 1 つの `MaidFaceRendererManager`（ManagerBase）が head スロットの Renderer のレイヤー・影受け・影落としを全部扱う | どれも同じ Renderer の値を書き、元の値の退避・着替えへの追従・解放時の復元が共通 |
| 実行順 | `TimelineIntegration.Initialize` の後、`CharacterShadowProxyManager` の前に登録し、`LateUpdate` で書く | ライトの照射対象と顔の影のキーはタイムライン（LateUpdate）と UI が書く。影の複製は顔の `shadowCastingMode` を見て作る |
| 元の値の退避 | Renderer ごとに初めて書くときに退避する。head スロットの実体が入れ替わったら、破棄された Renderer の退避を捨てる | 着替え中に同じ Renderer の退避を捨てると、SE が書いた値を「元の値」として取り違える |
| 書き込みの頻度 | Renderer ごとに「前回書いた値」を持ち、決めた値がそれから変わったときだけ書く。戻すときも、今の値が前回書いた値のままのときだけ元の値へ戻す | 毎フレーム実体と比べて書くと、VR の `LayerCheck` や他プラグインが後から変えた値を取り消し続ける |
| head 配下の Renderer の増減 | 60 フレームごとに数を比べ、変わっていれば集め直す | 影の複製と同じ間隔。毎フレームの `GetComponentsInChildren` を避ける |
| 影の複製の更新 | `MaidFaceRendererManager.castVersion`（影落としを書くたびに増える）が変わったら、影の複製を作り直す | 複製元の取り直しは 60 フレームごとなので、そのままでは影落としの切り替えが最大約 1 秒遅れる |
| ゲーム側ライト補正の限界 | 補正の対象に入るまで最大 60 フレーム遅れること（その間、顔だけ暗い）は許容する。「Charactor を照らさず Face を照らす」ゲーム側ライトは補正しない（顔を移すとそのライトが顔を照らすようになる） | `CharaDirectionalLight` は普段無効で、有効にするコードも見つかっていない。逆向きのマスクのライトは実在を確認していない |
| 着替え中 | `maid.IsAllProcPropBusy` の間は書かない。明けたら Renderer を集め直して書く | 他のコントローラー（ノード表示・影の複製）と同じ作法 |
| ゲーム側ライトの補正 | 移している間、`FindObjectsOfType<Light>()` を 60 フレームごとに走査し、SE の追加ライト以外で `Charactor` ビットがあり `Face` ビットが無いライトに Face ビットを足す。足したライトは覚えておき、戻すときに外す | `CharaDirectionalLight` はシーンで有効になることがあり、顔を移すと顔だけ照らされなくなる。毎フレームの全走査は避ける |
| 影の複製の対象 | `IsCasterLight` を「背景のみ／顔のみ／体のみ」に広げる。複製する Renderer の条件（`ShouldProxy`）は変えない | 影用レイヤーは 1 つで、ライトごとに複製を分けられない。照らされている側の複製は本体と同じ形の影を重ねて落とすだけで、見た目は変わらない |
| 顔の影のキー | NodeVisibility と同じく「上書きのある項目」と「レイヤーにキーのある項目」だけをキーにする。最初のキーより前の区間では上書きを外す | 既存レイヤーと同じ挙動にそろえる。上書きの無いメイドの XML を変えない |
| 影落とし ON の値 | `ShadowCastingMode.On`。OFF は `Off` | ゲームの体と同じ |
| PostEffects の修正 | `CharacterMask` と `OutlineColorEffect` のマスクを `Charactor | Face` にする。v2.5.0.0 の変更履歴に足す | VR モードの Face（頭）もキャラとして扱うのが正しく、副作用が無い |
| 照射対象のラベル | `顔のみ` / `体のみ` | 既存の「キャラのみ」「背景のみ」と同じ語尾 |

## Review Focus

1. **着替え・顔の差し替え**: 顔の影を上書きした状態や顔を Face へ移した状態で着替え（head の差し替えを含む）をしても、新しい顔に設定が付き直し、外したときにゲームの値へ戻ること（Task 3 の `FaceRendererRules` のテストと Task 8 の実機確認）。
2. **ライトをすべて「全て」へ戻したとき**: 顔が `Charactor` へ戻り、ゲーム側ライトに足した Face ビットも外れること（Task 3 のテスト `補正したライトだけを戻す` と Task 8）。
3. **旧データ**: 照射対象 3 / 4 を含まない旧タイムライン・旧プリセットは今までどおりに見えること。5 以上や負の値は「全て」（Task 1 のテスト）。
4. **VR やほかのプラグインが顔を Face 以外へ移した状態**: `Charactor` にない Renderer のレイヤーには触らないこと（Task 3 のテスト `Charactor以外のレイヤーは移さない`）。
5. **メイドの入れ替え・呼出解除**: ストックへ戻ったメイドに上書きや Face レイヤーが残らないこと（Task 3 の Release と Task 8）。

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/LightTarget.cs` | `Face` / `Body` モード、`FaceMask`、`UsesCharacterShadow` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyRules.cs` | `IsCasterLight` を 3 モードへ広げる | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyManager.cs` | `IsCasterLight` の呼び出しに faceMask を渡す | 変更 |
| `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs` | ラベル・コンボの高さ・「キャラの影」行の表示条件 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs` | 対象の max を 4 に | 変更 |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `target` のコメント、v42、`ScenePresetFaceShadow`、`ScenePresetMaid.faceShadow` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceShadowItems.cs` | 顔の影の項目定義（名前・表示名・ゲームの既定値） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceRendererRules.cs` | レイヤー・影設定の決め方とライト補正（純粋ロジック） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Manager/MaidFaceRendererManager.cs` | head スロットへの書き込み・退避・着替え追従・ライト補正 | 新規 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs` | マネージャの登録 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` | Release / Destroy の呼び出し | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs` | `TransformType.FaceShadow` | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataFaceShadow.cs` | 顔の影キー（bool 1 値） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/FaceShadowTimelineLayer.cs` | レイヤー | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/FaceShadowItemInspector.cs` | レイヤー項目の Inspector | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` | レイヤー・Inspector・Transform の登録 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/FaceShadowRowDrawer.cs` | 項目 1 行（トグル + 解除）。表情ウィンドウと Inspector で共有 | 新規 |
| `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs` | タブ「影」 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs`、`SnapshotFactory.cs`、`FaceShadowSnapshot.cs` | Undo | 変更 / 新規 |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | プリセットの保存・適用 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規ファイルの `<Compile Include>` | 変更 |
| `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs`、`CharacterShadowProxyRulesTests.cs` | 既存テストの更新・追加 | 変更 |
| `source/COM3D2.SceneEditor.Plugin.Tests/FaceRendererRulesTests.cs`、`FaceShadowLayerTests.cs`、`ScenePresetFaceShadowTests.cs` | 新規テスト | 新規 |
| `source/COM3D2.SceneEditor.Plugin.Tests/LaneColorInfoTests.cs`、`TimelineLayerCategoryTests.cs` | 新レイヤーの登録 | 変更 |
| `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin\source\COM3D25.PostEffects.Plugin\Effects\CharacterMask.cs`、`OutlineColorEffect.cs`、`README.md` | PostEffects のキャラ用マスク | 変更（別リポジトリ） |
| `docs-site/guide/staging.md`、`docs-site/guide/maid-editing.md`、`docs-site/guide/scene-preset.md`、`docs-site/timeline/layers-maid.md`、`docs-site/timeline/layers-effect.md`、`docs-site/timeline/compatibility.md`、`W:\COM3D2_5\work\CLAUDE.md` | ドキュメント | 変更 |

### ビルド + テストのコマンド（全タスク共通）

Git Bash で実行する。`<Filter>` はタスクごとに指定する（全件のときは `--filter` ごと外す）。

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "<Filter>"
```

COM3D2 構成のビルドが `bin/Debug/COM3D25/` を消すため、この順番を守る。テストは COM3D25 の DLL を参照する。

### ブランチ

`main` から `feat/face-body-shadow` を切って作業する（worktree は使わない）。PostEffects は自リポジトリの `main` から同名のブランチを切る。

---

### Task 1: 照射対象に「顔のみ」「体のみ」を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightTarget.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyRules.cs:13-20`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyManager.cs:147-166`（`HasCasterLight`）
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs:67-73, 252-256, 605-616`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs:127-137`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:127-129, 1376-1377`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LightTargetTests.cs`、`CharacterShadowProxyRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum LightTargetMode { All = 0, Character = 1, Background = 2, Face = 3, Body = 4 }`
  - `static int LightTarget.FaceMask { get; }`（Face レイヤーのビット。無ければ 0）
  - `static int LightTarget.ToCullingMask(LightTargetMode mode, bool characterShadow, int characterMask, int faceMask, int shadowMask)`
  - `static LightTargetMode LightTarget.FromCullingMask(int cullingMask, int characterMask, int faceMask, int shadowMask)`
  - `static bool LightTarget.UsesCharacterShadow(LightTargetMode mode)`（Background / Face / Body で true）
  - `static bool LightTarget.UsesFaceLayer(LightTargetMode mode)`（Face / Body で true）
  - `static bool CharacterShadowProxyRules.IsCasterLight(bool isLightActive, LightShadows shadows, int cullingMask, int characterMask, int faceMask, int shadowMask)`
  - 実マスク版 `ToCullingMask(mode, characterShadow)` / `FromCullingMask(cullingMask)` / `HasCharacterShadow` / `WithCharacterShadow` / `ClampMode` の引数は変えない

- [ ] **Step 1: 失敗するテストを書く**

まず注入版の呼び出し元を洗い出す。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
grep -rn "ToCullingMask(\|FromCullingMask(\|IsCasterLight(" --include=*.cs COM3D2.SceneEditor.Plugin.Tests COM3D2.SceneEditor.Plugin
```

テスト側の注入版（引数 4 個の `ToCullingMask`、3 個の `FromCullingMask`、5 個の `IsCasterLight`）はすべて新しい引数の並びへ直す。本体側の実マスク版（引数 2 個 / 1 個）は変えない。

`LightTargetTests.cs` の先頭の定数に Face マスクを足し、注入版の呼び出しすべてに `FaceMask` を渡すよう書き換える（既存の Theory の `InlineData` はそのまま）。次のテストを追加する。

```csharp
        // Face レイヤー (11)
        private const int FaceMask = 1 << 11;

        [Theory]
        [InlineData(LightTargetMode.Face, false, FaceMask)]
        [InlineData(LightTargetMode.Face, true, FaceMask | ShadowMask)]
        [InlineData(LightTargetMode.Body, false, CharacterMask & ~FaceMask)]
        [InlineData(LightTargetMode.Body, true, (CharacterMask & ~FaceMask) | ShadowMask)]
        public void 顔のみと体のみのマスクを組み立てる(LightTargetMode mode, bool characterShadow, int expected)
        {
            Assert.Equal(expected,
                LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.Face, false)]
        [InlineData(LightTargetMode.Face, true)]
        [InlineData(LightTargetMode.Body, false)]
        [InlineData(LightTargetMode.Body, true)]
        public void 顔のみと体のみも往復変換で戻る(LightTargetMode mode, bool characterShadow)
        {
            var mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask);

            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask, FaceMask, ShadowMask));
            Assert.Equal(characterShadow, LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Fact]
        public void Faceレイヤーが無ければ体のみはキャラのみとして読む()
        {
            // 実機では起きないが、faceMask == 0 では体のみのマスクがキャラのみと一致する
            var mask = LightTarget.ToCullingMask(LightTargetMode.Body, false, CharacterMask, 0, ShadowMask);
            Assert.Equal(LightTargetMode.Character, LightTarget.FromCullingMask(mask, CharacterMask, 0, ShadowMask));
        }

        [Theory]
        [InlineData(3, LightTargetMode.Face)]
        [InlineData(4, LightTargetMode.Body)]
        [InlineData(5, LightTargetMode.All)]
        [InlineData(-1, LightTargetMode.All)]
        public void 顔のみと体のみは丸めで残し範囲外は全てにする(int value, LightTargetMode expected)
        {
            Assert.Equal(expected, LightTarget.ClampMode(value));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false, false)]
        [InlineData(LightTargetMode.Character, false, false)]
        [InlineData(LightTargetMode.Background, true, false)]
        [InlineData(LightTargetMode.Face, true, true)]
        [InlineData(LightTargetMode.Body, true, true)]
        public void キャラの影と顔レイヤーを使うモード(LightTargetMode mode, bool usesShadow, bool usesFace)
        {
            Assert.Equal(usesShadow, LightTarget.UsesCharacterShadow(mode));
            Assert.Equal(usesFace, LightTarget.UsesFaceLayer(mode));
        }
```

既存の `ClampMode` のテストに「3 → All」を期待する行（`LightTargetTests.cs:106` 付近）があれば削除する（上の Theory が置き換える）。`モードを切り替えても影の設定を引き継ぐ` の `foreach` の配列に `LightTargetMode.Face, LightTargetMode.Body` を足す（最後は Background のまま）。

`CharacterShadowProxyRulesTests.cs` の `IsCasterLight` の呼び出しすべてに `FaceMask`（同ファイルにも `private const int FaceMask = 1 << 11;` を足す）を `characterMask` の後ろへ渡し、次を追加する。

```csharp
        [Theory]
        [InlineData(LightTargetMode.Background, true)]
        [InlineData(LightTargetMode.Face, true)]
        [InlineData(LightTargetMode.Body, true)]
        [InlineData(LightTargetMode.Character, false)]
        [InlineData(LightTargetMode.All, false)]
        public void キャラの影を使うのは背景のみと顔のみと体のみ(LightTargetMode mode, bool expected)
        {
            var mask = LightTarget.ToCullingMask(mode, true, CharacterMask, FaceMask, ShadowMask);

            Assert.Equal(expected, CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, mask, CharacterMask, FaceMask, ShadowMask));
        }
```

（定数名 `CharacterMask` / `ShadowMask` が同ファイルで別名なら、既存の名前に合わせる。）

- [ ] **Step 2: テストが失敗することを確認する**

Filter: `FullyQualifiedName~LightTarget|FullyQualifiedName~CharacterShadowProxyRules`
Expected: コンパイルエラー（`LightTargetMode.Face` が無い、引数の数が合わない）

- [ ] **Step 3: `LightTarget.cs` を実装する**

enum とクラスを次のように変える（`CharacterMask` プロパティと `HasCharacterShadow` / `WithCharacterShadow` は変えない）。

```csharp
    /// <summary>追加ライトが照らす対象。値はタイムラインキー・プリセットにそのまま保存される</summary>
    public enum LightTargetMode
    {
        All = 0,
        Character = 1,
        Background = 2,
        /// <summary>メイドの顔 (head スロット) だけ。照らしている間は MaidFaceRendererManager が顔を Face レイヤーへ移す</summary>
        Face = 3,
        /// <summary>キャラのうち顔以外 (男は全身)</summary>
        Body = 4,
    }
```

クラスの summary に「顔のみ／体のみは Face レイヤーで顔と体を分ける。ゲームは通常顔も Charactor に置くため、
そのモードのライトがある間だけ MaidFaceRendererManager が顔を Face へ移す」を足す。`CharacterMask` の直後に追加する。

```csharp
        private static int _faceMask = -1;

        /// <summary>Face レイヤーのビット。レイヤーが無ければ 0</summary>
        public static int FaceMask
        {
            get
            {
                if (_faceMask < 0)
                {
                    var layer = LayerMask.NameToLayer("Face");
                    _faceMask = layer >= 0 ? 1 << layer : 0;
                }
                return _faceMask;
            }
        }
```

実マスク版を差し替える。

```csharp
        public static int ToCullingMask(LightTargetMode mode, bool characterShadow)
            => ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, CharacterShadowLayer.mask);

        public static LightTargetMode FromCullingMask(int cullingMask)
            => FromCullingMask(cullingMask, CharacterMask, FaceMask, CharacterShadowLayer.mask);
```

注入版を差し替える。

```csharp
        /// <summary>characterMask・faceMask・影ビットを注入する版（テスト用）</summary>
        public static int ToCullingMask(
            LightTargetMode mode, bool characterShadow, int characterMask, int faceMask, int shadowMask)
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
                case LightTargetMode.Face:
                    cullingMask = faceMask;
                    break;
                case LightTargetMode.Body:
                    cullingMask = characterMask & ~faceMask;
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
        public static LightTargetMode FromCullingMask(int cullingMask, int characterMask, int faceMask, int shadowMask)
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
            if (normalized == (faceMask | shadowMask))
            {
                return LightTargetMode.Face;
            }
            if (normalized == ((characterMask & ~faceMask) | shadowMask))
            {
                return LightTargetMode.Body;
            }
            return LightTargetMode.All;
        }

        /// <summary>
        /// 照らさないキャラの影を影専用の複製で落とせるモードか。
        /// 「キャラのみ」「全て」はキャラ全体を照らすので、本体がそのまま影を落とす
        /// </summary>
        public static bool UsesCharacterShadow(LightTargetMode mode)
        {
            return mode == LightTargetMode.Background || mode == LightTargetMode.Face || mode == LightTargetMode.Body;
        }

        /// <summary>顔を Face レイヤーへ移さないと照らし分けられないモードか</summary>
        public static bool UsesFaceLayer(LightTargetMode mode)
        {
            return mode == LightTargetMode.Face || mode == LightTargetMode.Body;
        }
```

`ClampMode` を差し替える。

```csharp
        /// <summary>XML 等から読んだ整数を有効なモードへ丸める。範囲外は「全て」</summary>
        public static LightTargetMode ClampMode(int value)
        {
            return value >= (int)LightTargetMode.All && value <= (int)LightTargetMode.Body
                ? (LightTargetMode)value
                : LightTargetMode.All;
        }
```

- [ ] **Step 4: 影の複製の条件を広げる**

`CharacterShadowProxyRules.IsCasterLight` を差し替える（summary も直す）。

```csharp
        /// <summary>
        /// 複製を必要とするライトか。キャラの一部または全部を照らさないモード (背景のみ・顔のみ・体のみ) だけが対象。
        /// 照らさない側は本体が影を落とさないので、複製に落とさせる。照らす側の複製は本体と同じ影を重ねるだけ
        /// </summary>
        public static bool IsCasterLight(
            bool isLightActive, LightShadows shadows, int cullingMask, int characterMask, int faceMask, int shadowMask)
        {
            return isLightActive
                && shadows != LightShadows.None
                && LightTarget.HasCharacterShadow(cullingMask, shadowMask)
                && LightTarget.UsesCharacterShadow(
                    LightTarget.FromCullingMask(cullingMask, characterMask, faceMask, shadowMask));
        }
```

`CharacterShadowProxyManager.HasCasterLight` の呼び出しに `LightTarget.FaceMask` を足す。

```csharp
            var characterMask = LightTarget.CharacterMask;
            var faceMask = LightTarget.FaceMask;
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null && CharacterShadowProxyRules.IsCasterLight(
                    light.enabled && light.gameObject.activeInHierarchy,
                    light.shadows, light.cullingMask, characterMask, faceMask, shadowMask))
```

- [ ] **Step 5: UI・キー・プリセットの値域を直す**

`LightRowDrawer.cs`:
- `_lightTargetComboBox` の `contentSize` を `new Vector2(120, 200)` にする。ポップアップの高さは `Mathf.Min(contentSize.y, GUIView.GetPopupHeight(項目数))`（`MTEUtils/GUIComboBox.cs:254`）なので、上限を広げておけば 5 項目ぶんの高さになる
- 「キャラの影」行の条件（:254）を次にする。

```csharp
                // キャラの影はキャラの一部か全部を照らさないモード (背景のみ・顔のみ・体のみ) のときだけ意味を持つ
                if (LightTarget.UsesCharacterShadow(LightTarget.FromCullingMask(light.cullingMask)))
```

- `GetLightTargetName` の switch に追加する。

```csharp
                case LightTargetMode.Face:
                    return "顔のみ";
                case LightTargetMode.Body:
                    return "体のみ";
```

`TransformDataLight.cs` の `lightTarget` の `CustomValueInfo`: コメントを `// 0=全て / 1=キャラのみ / 2=背景のみ / 3=顔のみ / 4=体のみ (LightTargetMode)。実体は int なので丸めて渡す` に、`max = 4f` にする。

`ScenePresetData.cs`:
- `target` の summary を `照射対象 (LightTargetMode の数値: 0=全て / 1=キャラのみ / 2=背景のみ / 3=顔のみ / 4=体のみ)。旧プリセットは 0` にする
- 版履歴の v41 の後に追記し、`CurrentVersion` を 42 にする。

```csharp
        // v42: 追加ライトの照射対象 (target) に 3=顔のみ / 4=体のみ を追加。旧 SE は「全て」として読む。
        //      maid に faceShadow (顔の影の上書き。上書きのある項目だけ item 要素に持つ) を追加。
        //      上書きが無くても空要素を書く。旧形式は null で読め、適用時に触らない
        public static readonly int CurrentVersion = 42;
```

- [ ] **Step 6: テストが通ることを確認する**

Filter: `FullyQualifiedName~LightTarget|FullyQualifiedName~CharacterShadowProxyRules|FullyQualifiedName~ScenePreset`
Expected: PASS（両構成のビルドも通る）。プリセットの版を固定しているテストがあれば 42 へ直す。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/LightTarget.cs source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyRules.cs source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyManager.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataLight.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/
git commit -m "feat(light-target): 追加ライトの照射対象に顔のみ・体のみを足す"
```

---

### Task 2: 顔の影の項目定義と決め方（純粋ロジック）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceShadowItems.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceRendererRules.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（2 ファイルの `<Compile Include>`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceRendererRulesTests.cs`

**Interfaces:**
- Consumes: `LightTarget.UsesFaceLayer`（Task 1）
- Produces:
  - `class FaceShadowItem { string name; string displayName; bool gameDefault; }`
  - `static class FaceShadowItems { const string Receive = "receiveShadows"; const string Cast = "castShadows"; static readonly List<FaceShadowItem> items; static FaceShadowItem Find(string name); }`
  - `static class FaceRendererRules`:
    - `int ResolveLayer(int originalLayer, bool moveToFace, int charactorLayer, int faceLayer)`
    - `bool ResolveReceive(bool original, IDictionary<string, bool> overrides)`
    - `ShadowCastingMode ResolveCast(ShadowCastingMode original, IDictionary<string, bool> overrides)`
    - `bool NeedsFaceLayer(bool isLightActive, LightTargetMode mode)`
    - `bool NeedsLightCompensation(int cullingMask, int charactorMask, int faceMask)`
    - `bool IsDefault(IDictionary<string, bool> overrides, bool moveToFace)`（書くものが無いか）
    - `bool ShouldWrite<T>(T desired, T lastWritten)`（決めた値が前回書いた値から変わったか）
    - `T ResolveRestore<T>(T current, T lastWritten, T original)`（戻す値。他者が書き換えていれば残す）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class FaceRendererRulesTests
    {
        private const int Charactor = 10;
        private const int Face = 11;

        private static Dictionary<string, bool> Overrides(params KeyValuePair<string, bool>[] pairs)
        {
            var result = new Dictionary<string, bool>();
            foreach (var pair in pairs)
            {
                result[pair.Key] = pair.Value;
            }
            return result;
        }

        [Fact]
        public void 移すときはCharactorの顔をFaceへ移す()
        {
            Assert.Equal(Face, FaceRendererRules.ResolveLayer(Charactor, true, Charactor, Face));
        }

        [Fact]
        public void 移さないときは元のレイヤーに戻す()
        {
            Assert.Equal(Charactor, FaceRendererRules.ResolveLayer(Charactor, false, Charactor, Face));
        }

        [Fact]
        public void Charactor以外のレイヤーは移さない()
        {
            // VR の一人称モードでゲームが Face へ移した顔や、他プラグインが変えたレイヤー
            Assert.Equal(Face, FaceRendererRules.ResolveLayer(Face, true, Charactor, Face));
            Assert.Equal(5, FaceRendererRules.ResolveLayer(5, true, Charactor, Face));
        }

        [Fact]
        public void 上書きが無ければ元の影設定のまま()
        {
            var overrides = Overrides();
            Assert.True(FaceRendererRules.ResolveReceive(true, overrides));
            Assert.Equal(ShadowCastingMode.Off, FaceRendererRules.ResolveCast(ShadowCastingMode.Off, overrides));
        }

        [Fact]
        public void 上書きで影受けと影落としを切り替える()
        {
            var overrides = Overrides(
                new KeyValuePair<string, bool>(FaceShadowItems.Receive, false),
                new KeyValuePair<string, bool>(FaceShadowItems.Cast, true));
            Assert.False(FaceRendererRules.ResolveReceive(true, overrides));
            Assert.Equal(ShadowCastingMode.On, FaceRendererRules.ResolveCast(ShadowCastingMode.Off, overrides));
        }

        [Fact]
        public void 影落としOFFの上書きはOffにする()
        {
            var overrides = Overrides(new KeyValuePair<string, bool>(FaceShadowItems.Cast, false));
            Assert.Equal(ShadowCastingMode.Off, FaceRendererRules.ResolveCast(ShadowCastingMode.TwoSided, overrides));
        }

        [Theory]
        [InlineData(true, LightTargetMode.Face, true)]
        [InlineData(true, LightTargetMode.Body, true)]
        [InlineData(false, LightTargetMode.Face, false)]
        [InlineData(true, LightTargetMode.Character, false)]
        [InlineData(true, LightTargetMode.Background, false)]
        [InlineData(true, LightTargetMode.All, false)]
        public void 顔を移すのは有効な顔のみか体のみのライトがあるとき(bool active, LightTargetMode mode, bool expected)
        {
            Assert.Equal(expected, FaceRendererRules.NeedsFaceLayer(active, mode));
        }

        [Theory]
        [InlineData(1 << Charactor, true)]                        // CharaDirectionalLight
        [InlineData((1 << Charactor) | (1 << Face), false)]       // MainDirectionalLight など元から照らす
        [InlineData(~(1 << Charactor), false)]                    // キャラを照らさないライト
        public void Charactorを照らしFaceを照らさないライトだけ補正する(int mask, bool expected)
        {
            Assert.Equal(expected, FaceRendererRules.NeedsLightCompensation(mask, 1 << Charactor, 1 << Face));
        }

        [Fact]
        public void 上書きが無く移さなければ書くものは無い()
        {
            Assert.True(FaceRendererRules.IsDefault(Overrides(), false));
            Assert.False(FaceRendererRules.IsDefault(Overrides(), true));
            Assert.False(FaceRendererRules.IsDefault(
                Overrides(new KeyValuePair<string, bool>(FaceShadowItems.Receive, true)), false));
        }

        [Fact]
        public void 戻すときは自分が書いた値のままなら元の値へ戻す()
        {
            // 顔のみのライトを消して Face → Charactor へ戻す遷移
            Assert.Equal(Charactor, FaceRendererRules.ResolveRestore(Face, Face, Charactor));
        }

        [Fact]
        public void 戻すときに他者が書き換えていたらその値を残す()
        {
            // SE が Face へ移した後、VR の LayerCheck が別の値を書いた等
            Assert.Equal(5, FaceRendererRules.ResolveRestore(5, Face, Charactor));
            Assert.True(FaceRendererRules.ResolveRestore(true, false, false));
        }

        [Fact]
        public void 前回書いた値と同じなら書かない()
        {
            // 他者が書き換えても、こちらの決めた値が変わらない限り取り消さない
            Assert.False(FaceRendererRules.ShouldWrite(Face, Face));
            Assert.True(FaceRendererRules.ShouldWrite(Charactor, Face));
        }

        [Fact]
        public void 項目はゲームの既定値を持つ()
        {
            Assert.True(FaceShadowItems.Find(FaceShadowItems.Receive).gameDefault);
            Assert.False(FaceShadowItems.Find(FaceShadowItems.Cast).gameDefault);
            Assert.Null(FaceShadowItems.Find("unknown"));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Filter: `FullyQualifiedName~FaceRendererRules`
Expected: コンパイルエラー（型が無い）

- [ ] **Step 3: `FaceShadowItems.cs` を書く**

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影の項目 1 件</summary>
    public class FaceShadowItem
    {
        /// <summary>タイムラインの項目名・プリセットの name 属性</summary>
        public string name;
        public string displayName;
        /// <summary>上書きが無いときのゲームの値 (実機の Face7 で確認)</summary>
        public bool gameDefault;
    }

    /// <summary>
    /// 顔 (head スロット) の影の項目。タイムライン・プリセット・UI で共有する。
    /// 並びは UI とレイヤーのメニューの並び
    /// </summary>
    public static class FaceShadowItems
    {
        public const string Receive = "receiveShadows";
        public const string Cast = "castShadows";

        public static readonly List<FaceShadowItem> items = new List<FaceShadowItem>
        {
            new FaceShadowItem { name = Receive, displayName = "影を受ける", gameDefault = true },
            new FaceShadowItem { name = Cast, displayName = "影を落とす", gameDefault = false },
        };

        public static FaceShadowItem Find(string name)
        {
            foreach (var item in items)
            {
                if (item.name == name)
                {
                    return item;
                }
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: `FaceRendererRules.cs` を書く**

```csharp
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔 (head スロット) の Renderer に書く値の決め方 (MaidFaceRendererManager の判定部分)</summary>
    public static class FaceRendererRules
    {
        /// <summary>
        /// 書くレイヤー。Charactor にある顔だけを Face へ移す。
        /// VR の一人称モードでゲームが Face へ移した顔や、他プラグインが変えたレイヤーは元のまま
        /// </summary>
        public static int ResolveLayer(int originalLayer, bool moveToFace, int charactorLayer, int faceLayer)
        {
            return moveToFace && originalLayer == charactorLayer ? faceLayer : originalLayer;
        }

        public static bool ResolveReceive(bool original, IDictionary<string, bool> overrides)
        {
            bool value;
            return overrides.TryGetValue(FaceShadowItems.Receive, out value) ? value : original;
        }

        public static ShadowCastingMode ResolveCast(ShadowCastingMode original, IDictionary<string, bool> overrides)
        {
            bool value;
            if (!overrides.TryGetValue(FaceShadowItems.Cast, out value))
            {
                return original;
            }
            return value ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        /// <summary>このライトのために顔を Face レイヤーへ移す必要があるか</summary>
        public static bool NeedsFaceLayer(bool isLightActive, LightTargetMode mode)
        {
            return isLightActive && LightTarget.UsesFaceLayer(mode);
        }

        /// <summary>
        /// 顔を移している間、Face ビットを足すライトか。Charactor を照らすのに Face を照らさないライト
        /// (ゲームの CharaDirectionalLight など) は、移した顔を照らさなくなるため
        /// </summary>
        public static bool NeedsLightCompensation(int cullingMask, int charactorMask, int faceMask)
        {
            return faceMask != 0 && (cullingMask & charactorMask) != 0 && (cullingMask & faceMask) == 0;
        }

        /// <summary>上書きも移動も無く、Renderer をゲームの値へ戻してよいか</summary>
        public static bool IsDefault(IDictionary<string, bool> overrides, bool moveToFace)
        {
            return overrides.Count == 0 && !moveToFace;
        }

        /// <summary>
        /// 書き込むか。決めた値が前回書いた値から変わったときだけ書く。
        /// 毎フレーム実体と比べて書くと、他者 (VR の LayerCheck・他プラグイン) が後から変えた値を取り消し続ける
        /// </summary>
        public static bool ShouldWrite<T>(T desired, T lastWritten)
        {
            return !EqualityComparer<T>.Default.Equals(desired, lastWritten);
        }

        /// <summary>戻す値。今の値が前回書いた値のままなら元の値、他者が書き換えていればその値を残す</summary>
        public static T ResolveRestore<T>(T current, T lastWritten, T original)
        {
            return EqualityComparer<T>.Default.Equals(current, lastWritten) ? original : current;
        }
    }
}
```

csproj の `<Compile Include="MaidManipulation\FaceRendererRules.cs" />` と `<Compile Include="MaidManipulation\FaceShadowItems.cs" />` を、既存の `MaidManipulation\` の並び（アルファベット順）に足す。

- [ ] **Step 5: テストが通ることを確認する**

Filter: `FullyQualifiedName~FaceRendererRules`
Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceShadowItems.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceRendererRules.cs \
  source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/FaceRendererRulesTests.cs
git commit -m "feat(face-shadow): 顔の Renderer に書く影設定とレイヤーの決め方を足す"
```

---

### Task 3: 顔の Renderer へ書くマネージャ

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/MaidFaceRendererManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:473-474`（登録）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:705, 751`（Release / Destroy）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/CharacterShadowProxyManager.cs:85-135`（顔の影落としの変化で作り直す）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `FaceRendererRules`、`FaceShadowItems`（Task 2）、`LightTarget.FromCullingMask(int)` / `FaceMask`（Task 1）、`StudioLightManager.instance.lights`、`SlotBoneManager.GetSlotObject(Maid, string)`、`MaidNodeVisibilityController.IsBodyReady(Maid)`
- Produces（`MaidFaceRendererManager.instance` のメソッド。Task 4〜6 が使う）:
  - `bool TryGetOverride(Maid maid, string item, out bool enabled)`
  - `void SetOverride(Maid maid, string item, bool enabled)`
  - `void ClearOverride(Maid maid, string item)`
  - `void ClearAll(Maid maid)`
  - `Dictionary<string, bool> GetOverrides(Maid maid)`（コピーを返す。無ければ空）
  - `void SetOverrides(Maid maid, IDictionary<string, bool> overrides)`（全置き換え。未知の項目は捨てる）
  - `bool IsEnabled(Maid maid, string item)`（上書きがあればその値、無ければ head の最初の Renderer の今の値。読めなければ `gameDefault`）
  - `bool isFaceLayerActive { get; }`
  - `int castVersion { get; }`（顔の shadowCastingMode を書くたびに増える。影の複製が見る）
  - `void Release(Maid maid)` / `void Destroy()`

- [ ] **Step 1: マネージャを書く**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドの顔 (head スロット) の Renderer に、影の上書き (受ける・落とす) と Face レイヤーへの移動を書く。
    /// ゲームは通常顔も Charactor に置くため、「顔のみ／体のみ」の追加ライトがある間だけ顔を Face へ移す。
    /// 元の値は Renderer ごとに初めて書くときに退避し、上書きも移動も無くなったら戻す。
    /// タイムライン (ライトの照射対象・顔の影のキー) と UI の後、影の複製の前に LateUpdate で書く
    /// </summary>
    public class MaidFaceRendererManager : ManagerBase
    {
        private const string HeadSlotName = "head";

        /// <summary>ゲーム側ライトを探し直す間隔 (フレーム)。毎フレームの全走査を避ける</summary>
        private const int LightScanInterval = 60;

        /// <summary>Renderer の 3 値。元の値と、前回書いた値の両方に使う</summary>
        private struct RendererValues
        {
            public int layer;
            public bool receiveShadows;
            public ShadowCastingMode castingMode;

            public static RendererValues From(Renderer renderer)
            {
                return new RendererValues
                {
                    layer = renderer.gameObject.layer,
                    receiveShadows = renderer.receiveShadows,
                    castingMode = renderer.shadowCastingMode,
                };
            }
        }

        private class Original
        {
            /// <summary>初めて触ったときのゲームの値</summary>
            public RendererValues original;
            /// <summary>前回書いた値 (書いていない値は元の値)。他者が変えた値を取り消さないために持つ</summary>
            public RendererValues written;
        }

        /// <summary>head 配下の Renderer の増減を確かめる間隔 (フレーム)。影の複製と同じ</summary>
        private const int RendererCheckInterval = 60;

        private class Entry
        {
            public readonly Dictionary<string, bool> overrides = new Dictionary<string, bool>();
            /// <summary>Renderer を集めたときの head スロットの実体。入れ替わったら集め直す</summary>
            public GameObject headObj;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly Dictionary<Renderer, Original> originals = new Dictionary<Renderer, Original>();
            public bool wasBusy;
            /// <summary>head 配下の Renderer の数を最後に確かめたフレーム</summary>
            public int lastRendererCheckFrame = -1;
        }

        /// <summary>HasRendererCountChanged の作業用。毎回の確保を避ける</summary>
        private static readonly List<Renderer> _rendererScratch = new List<Renderer>();

        private static MaidFaceRendererManager _instance;
        public static MaidFaceRendererManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidFaceRendererManager();
                }
                return _instance;
            }
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();
        private readonly List<Maid> _maids = new List<Maid>();
        private readonly List<Maid> _removeScratch = new List<Maid>();

        /// <summary>Face ビットを足したゲーム側ライトと元の cullingMask</summary>
        private readonly Dictionary<Light, int> _compensatedLights = new Dictionary<Light, int>();
        private int _lastLightScanFrame = -1;

        private int _charactorLayer = -1;
        private int _faceLayer = -1;

        public bool isFaceLayerActive { get; private set; }

        /// <summary>
        /// 顔の shadowCastingMode を書くたびに増える。影の複製 (CharacterShadowProxyManager) は 60 フレームごとにしか
        /// 顔ぶれを取り直さないため、これの変化で即座に作り直す
        /// </summary>
        public int castVersion { get; private set; }

        private MaidFaceRendererManager()
        {
        }

        public override void Init()
        {
            _charactorLayer = LayerMask.NameToLayer("Charactor");
            _faceLayer = LayerMask.NameToLayer("Face");
            if (_charactorLayer < 0 || _faceLayer < 0)
            {
                MTEUtils.LogError("Charactor / Face レイヤーが見つからないため、顔のみ・体のみのライトで顔を照らし分けられません");
            }
        }

        public bool TryGetOverride(Maid maid, string item, out bool enabled)
        {
            enabled = false;
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry) && entry.overrides.TryGetValue(item, out enabled);
        }

        public void SetOverride(Maid maid, string item, bool enabled)
        {
            if (maid == null || FaceShadowItems.Find(item) == null)
            {
                return;
            }
            GetOrCreateEntry(maid).overrides[item] = enabled;
        }

        public void ClearOverride(Maid maid, string item)
        {
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                entry.overrides.Remove(item);
            }
        }

        public void ClearAll(Maid maid)
        {
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                entry.overrides.Clear();
            }
        }

        public Dictionary<string, bool> GetOverrides(Maid maid)
        {
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry)
                ? new Dictionary<string, bool>(entry.overrides)
                : new Dictionary<string, bool>();
        }

        public void SetOverrides(Maid maid, IDictionary<string, bool> overrides)
        {
            if (maid == null)
            {
                return;
            }
            var entry = GetOrCreateEntry(maid);
            entry.overrides.Clear();
            foreach (var pair in overrides)
            {
                if (FaceShadowItems.Find(pair.Key) != null)
                {
                    entry.overrides[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>今の値。上書きがあればその値、無ければ顔の Renderer の値 (読めなければゲームの既定)</summary>
        public bool IsEnabled(Maid maid, string item)
        {
            bool value;
            if (TryGetOverride(maid, item, out value))
            {
                return value;
            }
            var definition = FaceShadowItems.Find(item);
            var fallback = definition != null && definition.gameDefault;
            var head = maid != null && MaidNodeVisibilityController.IsBodyReady(maid)
                ? SlotBoneManager.GetSlotObject(maid, HeadSlotName) : null;
            var renderer = head != null ? head.GetComponentInChildren<Renderer>(true) : null;
            if (renderer == null)
            {
                return fallback;
            }
            return item == FaceShadowItems.Cast
                ? renderer.shadowCastingMode != ShadowCastingMode.Off
                : renderer.receiveShadows;
        }

        public override void LateUpdate()
        {
            var moveToFace = _charactorLayer >= 0 && _faceLayer >= 0 && HasFaceLayerLight();
            isFaceLayerActive = moveToFace;
            UpdateLightCompensation(moveToFace);

            CollectMaids(_maids);
            foreach (var maid in _maids)
            {
                Entry entry;
                _entries.TryGetValue(maid, out entry);
                if (entry == null)
                {
                    if (!moveToFace)
                    {
                        continue;
                    }
                    entry = GetOrCreateEntry(maid);
                }
                Apply(maid, entry, moveToFace);
            }
            RemoveIdleEntries(moveToFace);
        }

        public override void OnPluginDisable()
        {
            Destroy();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Destroy();
        }

        /// <summary>メイドがストックへ戻るとき。上書きを外し、顔をゲームの値へ戻す</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }
            RestoreAll(entry);
            _entries.Remove(maid);
        }

        /// <summary>すべてのメイドの顔とゲーム側ライトを元へ戻す</summary>
        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                RestoreAll(entry);
            }
            _entries.Clear();
            UpdateLightCompensation(false);
            isFaceLayerActive = false;
        }

        private Entry GetOrCreateEntry(Maid maid)
        {
            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                entry = new Entry();
                _entries.Add(maid, entry);
            }
            return entry;
        }

        private static bool HasFaceLayerLight()
        {
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null && FaceRendererRules.NeedsFaceLayer(
                    light.enabled && light.gameObject.activeInHierarchy,
                    LightTarget.FromCullingMask(light.cullingMask)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectMaids(List<Maid> results)
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
                var maid = characterMgr.GetMaid(i);
                if (maid != null && maid.body0 != null && maid.gameObject.activeInHierarchy)
                {
                    results.Add(maid);
                }
            }
        }

        private void Apply(Maid maid, Entry entry, bool moveToFace)
        {
            // 着替え中はスロットが作り直されるので書かない。明けたら集め直して書き直す
            if (maid.IsAllProcPropBusy || !MaidNodeVisibilityController.IsBodyReady(maid))
            {
                entry.wasBusy = true;
                return;
            }

            var head = SlotBoneManager.GetSlotObject(maid, HeadSlotName);
            var frame = Time.frameCount;
            if (head != entry.headObj || entry.wasBusy
                || (frame - entry.lastRendererCheckFrame >= RendererCheckInterval && HasRendererCountChanged(entry)))
            {
                entry.headObj = head;
                entry.wasBusy = false;
                CollectRenderers(entry);
            }
            if (frame - entry.lastRendererCheckFrame >= RendererCheckInterval)
            {
                entry.lastRendererCheckFrame = frame;
            }

            foreach (var renderer in entry.renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                Original state;
                if (!entry.originals.TryGetValue(renderer, out state))
                {
                    var current = RendererValues.From(renderer);
                    state = new Original { original = current, written = current };
                    entry.originals.Add(renderer, state);
                }

                // 決めた値が前回書いた値から変わったときだけ書く。上書きの無い項目は元の値 = 前回の値なので書かない
                var layer = FaceRendererRules.ResolveLayer(state.original.layer, moveToFace, _charactorLayer, _faceLayer);
                if (FaceRendererRules.ShouldWrite(layer, state.written.layer))
                {
                    renderer.gameObject.layer = layer;
                    state.written.layer = layer;
                }
                var receive = FaceRendererRules.ResolveReceive(state.original.receiveShadows, entry.overrides);
                if (FaceRendererRules.ShouldWrite(receive, state.written.receiveShadows))
                {
                    renderer.receiveShadows = receive;
                    state.written.receiveShadows = receive;
                }
                var cast = FaceRendererRules.ResolveCast(state.original.castingMode, entry.overrides);
                if (FaceRendererRules.ShouldWrite(cast, state.written.castingMode))
                {
                    renderer.shadowCastingMode = cast;
                    state.written.castingMode = cast;
                    castVersion++;
                }
            }
        }

        private static bool HasRendererCountChanged(Entry entry)
        {
            if (entry.headObj == null)
            {
                return false;
            }
            entry.headObj.GetComponentsInChildren(true, _rendererScratch);
            return _rendererScratch.Count != entry.renderers.Count;
        }

        /// <summary>
        /// head スロットの Renderer を集め直す。破棄された Renderer の退避は捨てるが、残っている Renderer の退避は残す
        /// (着替え中に同じ Renderer の退避を捨てると、SE が書いた値を元の値と取り違える)
        /// </summary>
        private static void CollectRenderers(Entry entry)
        {
            entry.renderers.Clear();
            if (entry.headObj != null)
            {
                entry.headObj.GetComponentsInChildren(true, entry.renderers);
            }

            var dead = new List<Renderer>();
            foreach (var renderer in entry.originals.Keys)
            {
                if (renderer == null || !entry.renderers.Contains(renderer))
                {
                    dead.Add(renderer);
                }
            }
            foreach (var renderer in dead)
            {
                entry.originals.Remove(renderer);
            }
        }

        /// <summary>元の値へ戻す。前回書いた後に他者が書き換えた値は残す</summary>
        private void RestoreAll(Entry entry)
        {
            foreach (var pair in entry.originals)
            {
                var renderer = pair.Key;
                if (renderer == null)
                {
                    continue;
                }
                var state = pair.Value;
                renderer.gameObject.layer = FaceRendererRules.ResolveRestore(
                    renderer.gameObject.layer, state.written.layer, state.original.layer);
                renderer.receiveShadows = FaceRendererRules.ResolveRestore(
                    renderer.receiveShadows, state.written.receiveShadows, state.original.receiveShadows);
                var cast = FaceRendererRules.ResolveRestore(
                    renderer.shadowCastingMode, state.written.castingMode, state.original.castingMode);
                if (renderer.shadowCastingMode != cast)
                {
                    renderer.shadowCastingMode = cast;
                    castVersion++;
                }
            }
            entry.originals.Clear();
            entry.renderers.Clear();
            entry.headObj = null;
        }

        /// <summary>
        /// 上書きが無く移動もしないエントリは、ゲームの値へ戻して捨てる。
        /// いなくなったメイドのエントリも捨てる (Renderer は破棄済みか、Release 済み)
        /// </summary>
        private void RemoveIdleEntries(bool moveToFace)
        {
            _removeScratch.Clear();
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (_maids.Contains(pair.Key))
                {
                    if (FaceRendererRules.IsDefault(entry.overrides, moveToFace))
                    {
                        RestoreAll(entry);
                        _removeScratch.Add(pair.Key);
                    }
                    continue;
                }

                // 非表示などで一時的にいないメイドは、戻ってきたときのために上書きだけ残す。退避は戻して捨てる
                RestoreAll(entry);
                if (entry.overrides.Count == 0)
                {
                    _removeScratch.Add(pair.Key);
                }
            }
            foreach (var maid in _removeScratch)
            {
                _entries.Remove(maid);
            }
        }

        /// <summary>
        /// 顔を移している間、Charactor を照らすのに Face を照らさないゲーム側ライトへ Face ビットを足す。
        /// 移さなくなったら足したライトだけを戻す
        /// </summary>
        private void UpdateLightCompensation(bool moveToFace)
        {
            if (!moveToFace)
            {
                if (_compensatedLights.Count > 0)
                {
                    var face = 1 << _faceLayer;
                    foreach (var pair in _compensatedLights)
                    {
                        // 足した後にゲームが別のマスクへ変えていたら、その値を残す
                        if (pair.Key != null && pair.Key.cullingMask == (pair.Value | face))
                        {
                            pair.Key.cullingMask = pair.Value;
                        }
                    }
                    _compensatedLights.Clear();
                }
                _lastLightScanFrame = -1;
                return;
            }

            var charactorMask = 1 << _charactorLayer;
            var faceMask = 1 << _faceLayer;
            var frame = Time.frameCount;
            if (_lastLightScanFrame < 0 || frame - _lastLightScanFrame >= LightScanInterval)
            {
                _lastLightScanFrame = frame;
                RemoveDestroyedLights();
                // FindObjectsOfType は Light コンポーネントが無効でも GameObject がアクティブなら拾う
                // (CharaDirectionalLight は普段この状態。実機確認済み)
                var studioLights = StudioLightManager.instance.lights;
                foreach (var light in Object.FindObjectsOfType<Light>())
                {
                    if (studioLights.Contains(light) || _compensatedLights.ContainsKey(light))
                    {
                        continue;
                    }
                    if (FaceRendererRules.NeedsLightCompensation(light.cullingMask, charactorMask, faceMask))
                    {
                        _compensatedLights.Add(light, light.cullingMask);
                    }
                }
            }

            // ゲームが書き戻しても次のフレームで足し直す
            foreach (var light in _compensatedLights.Keys)
            {
                if (light != null && (light.cullingMask & faceMask) == 0)
                {
                    light.cullingMask |= faceMask;
                }
            }
        }

        private readonly List<Light> _lightScratch = new List<Light>();

        private void RemoveDestroyedLights()
        {
            _lightScratch.Clear();
            foreach (var light in _compensatedLights.Keys)
            {
                if (light == null)
                {
                    _lightScratch.Add(light);
                }
            }
            foreach (var light in _lightScratch)
            {
                _compensatedLights.Remove(light);
            }
        }
    }
}
```

`StudioLightManager` は `COM3D2.SceneEditor.Plugin` 側（`Manager/StudioLightManager.cs`）を使う。名前が Timeline 側と衝突する場合は名前空間で修飾する。`GetComponentsInChildren(bool, List<T>)` が 2.0 側（Unity 5.6）にもあることをビルドで確かめる。無ければ配列版に替える。

- [ ] **Step 2: 登録と解放をつなぐ**

`COM3D2.SceneEditor.Plugin.cs` の `CharacterShadowProxyManager` の登録の直前に足す。

```csharp
                // 顔のレイヤー・影はライトの照射対象と顔の影のキー (タイムライン・UI) で決まり、
                // 影の複製は顔の影落としを見て作るため、その間に登録する
                managerRegistry.RegisterManager(MaidFaceRendererManager.instance);
```

`MaidManipulateManager.cs` の `MaidNodeVisibilityController.Release(maid);` の直後と `MaidNodeVisibilityController.Destroy();` の直後に足す。

```csharp
            // 顔の影の上書きと Face レイヤーへの移動も持ち越さない（ストックの Maid は使い回される）
            MaidFaceRendererManager.instance.Release(maid);
```

```csharp
            MaidFaceRendererManager.instance.Destroy();
```

csproj に `<Compile Include="Manager\MaidFaceRendererManager.cs" />` を足す。

`CharacterShadowProxyManager` に、顔の影落としの変化で複製を作り直す経路を足す（複製元の取り直しは 60 フレームごとなので、そのままでは切り替えが最大約 1 秒遅れる）。フィールドに `private int _lastFaceCastVersion = -1;` を足し、`LateUpdate` の `CollectCharacters(_characters);` の直前に入れる。

```csharp
            // 顔の影落とし (MaidFaceRendererManager) が変わったら、顔を複製に含めるかが変わるので作り直す
            var faceCastVersion = MaidFaceRendererManager.instance.castVersion;
            var faceCastChanged = faceCastVersion != _lastFaceCastVersion;
            _lastFaceCastVersion = faceCastVersion;
```

ループ内の作り直し条件を `if (entry.isDirty || faceCastChanged || IsStale(entry) || ...)` にする。

- [ ] **Step 3: ビルドと全テスト**

Filter なし（全件）。Expected: 両構成のビルドとテストが通る。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/MaidFaceRendererManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs \
  source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(face-shadow): 顔の Renderer へ影の上書きと Face レイヤーへの移動を書く"
```

---

### Task 4: タイムラインの「メイド顔の影」レイヤー

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（`FaceSetting` の直後に `FaceShadow,`）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataFaceShadow.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/FaceShadowTimelineLayer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/FaceShadowItemInspector.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/FaceShadowRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:327-328, 371-372, 510-512`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs:55`、`SnapshotFactory.cs:37-38`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/FaceShadowSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceShadowLayerTests.cs`、`LaneColorInfoTests.cs:138`、`TimelineLayerCategoryTests.cs:22`、`TimelineLayerResetOnRemoveTests.cs:12-29`

**Interfaces:**
- Consumes: `MaidFaceRendererManager.instance` の上書き API（Task 3）、`FaceShadowItems`（Task 2）
- Produces:
  - `TransformType.FaceShadow`
  - `class TransformDataFaceShadow : TransformDataBase { enum Index { IsEnabled = 0 } bool isEnabled; ValueData isEnabledValue; }`
  - `class FaceShadowTimelineLayer : TimelineLayerBase { static FaceShadowTimelineLayer Create(int slotNo); static List<KeyValuePair<string, bool>> BuildKeys(IDictionary<string, bool> overrides, ICollection<string> keyedItems, Func<string, bool> isEnabled); }`
  - `static class FaceShadowRowDrawer { static void Draw(GUIView view, Maid target, FaceShadowItem item, float rowHeight); static void RecordEdit(Maid target, string label); }`
  - `HistoryScope.FaceShadow`

- [ ] **Step 1: 失敗するテストを書く**

`FaceShadowLayerTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>顔の影レイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class FaceShadowLayerTests
    {
        private static MTEP.TransformDataFaceShadow CreateTransform(string name)
        {
            var trans = new MTEP.TransformDataFaceShadow();
            trans.Initialize(name);
            return trans;
        }

        [Fact]
        public void キーは有効1値でBool型()
        {
            var trans = CreateTransform(FaceShadowItems.Receive);
            Assert.Equal(MTEP.TransformType.FaceShadow, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(0, (int)MTEP.TransformDataFaceShadow.Index.IsEnabled);
            Assert.Equal(MTEP.CustomValueType.BoolValue,
                trans.GetCustomValueInfoMap()["isEnabled"].type);
        }

        [Fact]
        public void 新しいキーの既定はゲームの既定値()
        {
            Assert.True(CreateTransform(FaceShadowItems.Receive).isEnabled);
            Assert.False(CreateTransform(FaceShadowItems.Cast).isEnabled);
        }

        [Fact]
        public void 上書きとキーのある項目だけを定義順でキーにする()
        {
            var overrides = new Dictionary<string, bool> { { FaceShadowItems.Cast, true } };
            var keyed = new List<string> { FaceShadowItems.Receive };

            var keys = MTEP.FaceShadowTimelineLayer.BuildKeys(overrides, keyed, item => false);

            Assert.Equal(2, keys.Count);
            Assert.Equal(FaceShadowItems.Receive, keys[0].Key);
            Assert.False(keys[0].Value);   // 上書きは無いが、キーがあるので今の値
            Assert.Equal(FaceShadowItems.Cast, keys[1].Key);
            Assert.True(keys[1].Value);
        }

        [Fact]
        public void 上書きもキーも無ければキーにしない()
        {
            var keys = MTEP.FaceShadowTimelineLayer.BuildKeys(
                new Dictionary<string, bool>(), new List<string>(), item => true);
            Assert.Empty(keys);
        }
    }
}
```

`LaneColorInfoTests.cs:138` の直後に足す。

```csharp
            yield return new object[] { new TransformDataFaceShadow(), (int)TransformDataFaceShadow.Index.IsEnabled, true };
```

`TimelineLayerCategoryTests.cs:22` の直後に足す。

```csharp
            { "FaceShadowTimelineLayer", TimelineLayerCategory.Maid },
```

`TimelineLayerResetOnRemoveTests.cs` の `EXPECTED_OVERRIDES` の `"NodeVisibilityTimelineLayer",` の直後に足す（`ResetOnRemove` を override するレイヤーはここに無いとテストが落ちる）。

```csharp
            // 上書きはこのレイヤーと表情ウィンドウの影タブからしか作れないため、断面の無い削除でも全解除する
            "FaceShadowTimelineLayer",
```

レイヤーの優先度（`TimelineLayerDesc` の第 2 引数）は全カテゴリの通し番号。Maid は 0, 1, 9〜19 を使い、20 はカメラ。空いている 8 を使う（ノード表示 9 の直前）。

- [ ] **Step 2: テストが失敗することを確認する**

Filter: `FullyQualifiedName~FaceShadowLayer|FullyQualifiedName~LaneColorInfo|FullyQualifiedName~TimelineLayerCategory|FullyQualifiedName~TimelineLayerResetOnRemove`
Expected: コンパイルエラー

- [ ] **Step 3: TransformType と TransformData を書く**

`ITransformData.cs` の `FaceSetting,` の直後に `FaceShadow,` を足す（XML は名前で保存されるので途中へ足してよい）。

`TransformDataFaceShadow.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 顔の影キー 1 件分。項目名は FaceShadowItems の名前 (receiveShadows / castShadows) で、有効フラグを持つ。
    /// 補間せず区間開始時に適用する
    /// </summary>
    public class TransformDataFaceShadow : TransformDataBase
    {
        public enum Index
        {
            IsEnabled = 0,
        }

        public override TransformType type => TransformType.FaceShadow;

        public override int valueCount => 1;

        public TransformDataFaceShadow()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るため、項目ごとのゲームの既定値にそろえる。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            var item = FaceShadowItems.Find(name);
            isEnabled = item != null && item.gameDefault;
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "isEnabled",
                new CustomValueInfo
                {
                    index = (int)Index.IsEnabled,
                    name = "有効",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    // 項目ごとに既定が違う (影を受ける = ON、影を落とす = OFF)。情報は型ごとに 1 つなので受ける側に合わせる
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        private static readonly LaneColorInfo LaneInfo =
            LaneColorInfo.FromBool(LaneColorInfo.DefaultColor, (int)Index.IsEnabled);

        public override LaneColorInfo GetLaneColorInfo()
        {
            return LaneInfo;
        }

        public ValueData isEnabledValue => values[(int)Index.IsEnabled];

        public bool isEnabled
        {
            get => isEnabledValue.boolValue;
            set => isEnabledValue.boolValue = value;
        }
    }
}
```

- [ ] **Step 4: レイヤーを書く**

`FaceShadowTimelineLayer.cs`（`NodeVisibilityTimelineLayer` と同じ作り。書き込みは `MaidFaceRendererManager` の LateUpdate がまとめて行うので Flush は無い）:

```csharp
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドの顔 (head スロット) の影の上書き (MaidFaceRendererManager) をキー化するレイヤー。
    /// 項目は FaceShadowItems で、有効フラグは区間開始時に適用する。
    /// キーにするのは、SE が上書きしている項目と、このレイヤーに既にキーのある項目だけ。
    /// 最初のキーより前の区間では上書きを解除する
    /// </summary>
    [TimelineLayerDesc("メイド顔の影", 8, TimelineLayerCategory.Maid)]
    public class FaceShadowTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(FaceShadowTimelineLayer);
        public override string layerName => nameof(FaceShadowTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var item in FaceShadowItems.items)
                    {
                        _allBoneNames.Add(item.name);
                    }
                }
                return _allBoneNames;
            }
        }

        /// <summary>最初のキーより前の区間にいる項目。区間に入ったときだけ解除するために持つ</summary>
        private readonly HashSet<string> _beforeFirstKeyItems = new HashSet<string>();

        private static MaidFaceRendererManager faceRenderer => MaidFaceRendererManager.instance;

        private FaceShadowTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static FaceShadowTimelineLayer Create(int slotNo)
        {
            return new FaceShadowTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();
            foreach (var item in FaceShadowItems.items)
            {
                _allMenuItems.Add(new BoneMenuItem(item.name, item.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        /// <summary>キーの打ち直しで区間が変わるので、「区間に入った」の記録も作り直す</summary>
        protected override void BuildPlayData()
        {
            base.BuildPlayData();
            _beforeFirstKeyItems.Clear();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        /// <summary>
        /// 基底は最初のキーより前 (current == null) を何もしないため、後ろの区間で付けた上書きが
        /// シークで戻しても残る。その区間に入ったときの 1 回だけ上書きを外す
        /// (毎フレーム外すと、その区間で UI から付けた上書きまで消える)
        /// </summary>
        protected override void ApplyPlayData()
        {
            base.ApplyPlayData();

            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            foreach (var pair in _playDataMap)
            {
                if (pair.Value.current != null)
                {
                    _beforeFirstKeyItems.Remove(pair.Key);
                    continue;
                }
                if (_beforeFirstKeyItems.Add(pair.Key))
                {
                    faceRenderer.ClearOverride(maid, pair.Key);
                }
            }
        }

        /// <summary>
        /// キーにする項目と値。上書きのある項目はその値、上書きは無いがレイヤーにキーのある項目は今の値。
        /// 後者が無いと、途中で「解除」した項目のキーが作られず、前のキーの上書きが続く。並びは定義順
        /// </summary>
        public static List<KeyValuePair<string, bool>> BuildKeys(
            IDictionary<string, bool> overrides, ICollection<string> keyedItems, Func<string, bool> isEnabled)
        {
            var result = new List<KeyValuePair<string, bool>>();
            foreach (var item in FaceShadowItems.items)
            {
                bool enabled;
                if (overrides.TryGetValue(item.name, out enabled))
                {
                    result.Add(new KeyValuePair<string, bool>(item.name, enabled));
                }
                else if (keyedItems.Contains(item.name))
                {
                    result.Add(new KeyValuePair<string, bool>(item.name, isEnabled(item.name)));
                }
            }
            return result;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || !indexUpdated || FaceShadowItems.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataFaceShadow;
            if (start == null)
            {
                return;
            }

            bool current;
            if (faceRenderer.TryGetOverride(maid, motion.name, out current) && current == start.isEnabled)
            {
                return;
            }
            faceRenderer.SetOverride(maid, motion.name, start.isEnabled);
        }

        /// <summary>上書きはこのレイヤーと表情ウィンドウの影タブからしか作れないので、削除・アンロード時は全解除する</summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid != null)
            {
                faceRenderer.ClearAll(maid);
            }
        }

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            var keys = BuildKeys(
                faceRenderer.GetOverrides(maid),
                _playDataMap.Keys,
                item => faceRenderer.IsEnabled(maid, item));
            foreach (var pair in keys)
            {
                var trans = CreateTransformData<TransformDataFaceShadow>(pair.Key);
                trans.isEnabled = pair.Value;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.FaceShadow;
        }
    }
}
```

- [ ] **Step 5: 行の描画・Inspector・Undo を書く**

`FaceShadowRowDrawer.cs`（`NodeVisibilityRowDrawer` と同じ形。上書きのある項目は名前に「*」）:

```csharp
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 顔の影の項目 1 件分の行 (トグル・解除ボタン)。表情ウィンドウの影タブと
    /// TimelineItemInspector (顔の影レイヤーの項目表示) で共有する。
    /// トグルには今の値を出し、SE が上書きしている項目は名前に「*」を付ける
    /// </summary>
    public static class FaceShadowRowDrawer
    {
        private const float ClearButtonWidth = 50f;

        private static MaidFaceRendererManager faceRenderer => MaidFaceRendererManager.instance;

        public static void Draw(GUIView view, Maid target, FaceShadowItem item, float rowHeight)
        {
            bool overridden;
            var hasOverride = faceRenderer.TryGetOverride(target, item.name, out overridden);
            var label = hasOverride ? item.displayName + " *" : item.displayName;

            view.BeginHorizontal();
            {
                // トグルは残り幅いっぱい (-1) にするため、解除ボタンを先に置く
                if (view.DrawButton("解除", ClearButtonWidth, rowHeight, hasOverride))
                {
                    RecordEdit(target, item.displayName + " 解除");
                    faceRenderer.ClearOverride(target, item.name);
                }

                view.DrawToggle(label, faceRenderer.IsEnabled(target, item.name), -1, rowHeight,
                    value =>
                    {
                        RecordEdit(target, item.displayName);
                        faceRenderer.SetOverride(target, item.name, value);
                    });
            }
            view.EndLayout();
        }

        public static void RecordEdit(Maid target, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.FaceShadow, "顔の影: " + label);
        }
    }
}
```

（`NodeVisibilityRowDrawer.Draw` の `BeginHorizontal` / `EndLayout` / `DrawButton` / `DrawToggle` の実際の引数と並びを開いて確かめ、同じ書き方にそろえる。）

`FaceShadowItemInspector.cs`（`NodeVisibilityItemInspector` と同じ形）:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影レイヤーのメニュー項目 → トグル。行の描画と履歴への記録は FaceShadowRowDrawer に任せる</summary>
    public class FaceShadowItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        public void DrawItems(GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight, textColor: Color.yellow);
                return;
            }

            foreach (var menuItem in items)
            {
                var item = FaceShadowItems.Find(menuItem.name);
                if (item == null)
                {
                    view.DrawLabel(menuItem.displayName + " (未対応)", -1, RowHeight, textColor: Color.gray);
                    continue;
                }
                FaceShadowRowDrawer.Draw(view, maid, item, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 顔の影に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
```

`HistoryScope.cs` の `NodeVisibility,` の直後に足す。

```csharp
        /// <summary>顔の影 (head スロットの影を受ける・落とすの上書き)</summary>
        FaceShadow,
```

`SnapshotFactory.cs` の `NodeVisibility` の case の直後に足す。

```csharp
                case HistoryScope.FaceShadow:
                    return FaceShadowSnapshot.Capture(maid);
```

`FaceShadowSnapshot.cs`（`NodeVisibilitySnapshot` と同じ形）:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影のスナップショット (SE の上書き一式)</summary>
    public class FaceShadowSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, bool> _overrides;

        public static FaceShadowSnapshot Capture(Maid maid)
        {
            return new FaceShadowSnapshot
            {
                _capturedMaid = maid,
                _overrides = MaidFaceRendererManager.instance.GetOverrides(maid),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            MaidFaceRendererManager.instance.SetOverrides(maid, _overrides);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as FaceShadowSnapshot;
            if (o == null || o._overrides.Count != _overrides.Count)
            {
                return false;
            }
            foreach (var pair in _overrides)
            {
                bool value;
                if (!o._overrides.TryGetValue(pair.Key, out value) || value != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
```

`HistoryScopeUtils` に「対象メイドが必須のスコープか」の判定（`HistoryScope.cs` の `HistoryScopeUtils` 内）がある。`NodeVisibility` が列挙されていれば、同じ場所に `FaceShadow` を足す。

- [ ] **Step 6: 登録する**

`TimelineIntegration.cs`:
- NodeVisibility の `RegisterLayer` の直後に `timelineManager.RegisterLayer(typeof(MTEP.FaceShadowTimelineLayer), MTEP.FaceShadowTimelineLayer.Create);`
- NodeVisibility の `TimelineItemInspectorRegistry.Register` の直後に `TimelineItemInspectorRegistry.Register(typeof(MTEP.FaceShadowTimelineLayer), new FaceShadowItemInspector());`
- NodeVisibility の `RegisterTransform` の直後に

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.FaceShadow,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataFaceShadow>);
```

csproj に 5 ファイル（`FaceShadowRowDrawer.cs`、`Manager\History\FaceShadowSnapshot.cs`、`Timeline\ItemInspector\FaceShadowItemInspector.cs`、`Timeline\TimelineLayer\FaceShadowTimelineLayer.cs`、`Timeline\TransformData\TransformDataFaceShadow.cs`）を足す。

- [ ] **Step 7: テストが通ることを確認する**

Filter なし（全件）。Expected: PASS。全 TransformType や全レイヤーを列挙するテスト（`TimelineLayerRestorePolicyTests` など）が落ちたら、その一覧に `FaceShadow` / `FaceShadowTimelineLayer` を足す。

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ source/COM3D2.SceneEditor.Plugin.Tests/
git commit -m "feat(face-shadow): 顔の影をキー化するメイド顔の影レイヤーを足す"
```

---

### Task 5: 表情ウィンドウの「影」タブ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:24-33, 140-158, 175-215`

**Interfaces:**
- Consumes: `FaceShadowRowDrawer.Draw`、`FaceShadowItems.items`（Task 4 / 2）、`MaidFaceRendererManager.instance.isFaceLayerActive`（Task 3）

- [ ] **Step 1: タブを足す**

`FaceTab` の `シェイプキー,` の直後に `影,` を足す。

`TryFocusTimelineLayer` の先頭に足す。

```csharp
            if (layerType == typeof(MTEP.FaceShadowTimelineLayer))
            {
                _tab = FaceTab.影;
                return true;
            }
```

同メソッドの `MorphTimelineLayer` の分岐の条件に影タブも足す（表情レイヤーを選んだときに影タブのまま残らないように）。

```csharp
                if (_tab == FaceTab.視線 || _tab == FaceTab.シェイプキー || _tab == FaceTab.影)
```

`DrawHeader`（表情の保存・強制上書き）は影タブでもそのまま出す。メイド選択と同じくウィンドウ共通の見出しで、タブごとに出し分けている前例が無いため。

`DrawMaidContent` の分岐に足す（`シェイプキー` の分岐の後）。

```csharp
            else if (_tab == FaceTab.影)
            {
                DrawShadowContent(view, target);
            }
```

`GetTimelineLayerType` に `case FaceTab.影: return typeof(MTEP.FaceShadowTimelineLayer);` を足し、summary に「影タブは顔の影レイヤー」を足す。

描画メソッドを足す。

```csharp
        /// <summary>
        /// 顔 (head スロット) の影の上書き。顔と体を別のライトで照らすのは、追加ライトの「対象」の顔のみ／体のみで行う
        /// </summary>
        private void DrawShadowContent(GUIView view, Maid target)
        {
            foreach (var item in FaceShadowItems.items)
            {
                FaceShadowRowDrawer.Draw(view, target, item, ROW_HEIGHT);
            }
            view.DrawLabel("顔と体を別のライトで照らすには、ライトの「対象」を顔のみ／体のみにします", -1, ROW_HEIGHT,
                textColor: Color.gray);
        }
```

`isMorphTab` は変えない（影タブはモーフを持たない）。タブ幅（`DrawInnerTabs(_tab, 78)`）で 8 タブが収まらなければ、ウィンドウ幅に応じて折り返されるか実機で確かめ（Task 8）、収まらないなら幅を詰める。

- [ ] **Step 2: ビルドと全テスト**

Filter なし。Expected: PASS

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs
git commit -m "feat(face-shadow): 表情ウィンドウに顔の影タブを足す"
```

---

### Task 6: シーンプリセットに顔の影を保存する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetNodeVisibility` の直後にクラス、`ScenePresetMaid.nodeVisibility` の直後にフィールド）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1137-1138, 2188-2195, 2915-2923`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetFaceShadowTests.cs`

**Interfaces:**
- Consumes: `FaceShadowItems`、`MaidFaceRendererManager.instance.GetOverrides / SetOverrides`
- Produces: `class ScenePresetFaceShadow { List<ScenePresetFaceShadowItem> items; static ScenePresetFaceShadow FromOverrides(IEnumerable<KeyValuePair<string, bool>>); Dictionary<string, bool> ToOverrides(); }`、`ScenePresetMaid.faceShadow`

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetNodeVisibilityTests.cs` の XML 往復の書き方（シリアライザの作り方）を開いて同じ作法で書く。最低限、次を確かめる。

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ScenePresetFaceShadowTests
    {
        [Fact]
        public void 上書きから作って戻すと同じになる()
        {
            var overrides = new Dictionary<string, bool>
            {
                { FaceShadowItems.Receive, false },
                { FaceShadowItems.Cast, true },
            };

            var restored = ScenePresetFaceShadow.FromOverrides(overrides).ToOverrides();

            Assert.Equal(2, restored.Count);
            Assert.False(restored[FaceShadowItems.Receive]);
            Assert.True(restored[FaceShadowItems.Cast]);
        }

        [Fact]
        public void 未知の項目と重複は捨てる()
        {
            var preset = new ScenePresetFaceShadow();
            preset.items.Add(new ScenePresetFaceShadowItem { name = "unknown", enabled = true });
            preset.items.Add(new ScenePresetFaceShadowItem { name = FaceShadowItems.Cast, enabled = true });
            preset.items.Add(new ScenePresetFaceShadowItem { name = FaceShadowItems.Cast, enabled = false });

            var overrides = preset.ToOverrides();

            Assert.Single(overrides);
            Assert.True(overrides[FaceShadowItems.Cast]);
        }

        [Fact]
        public void XMLで往復し要素の無い旧プリセットはnullで読む()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            var maid = new ScenePresetMaid
            {
                faceShadow = ScenePresetFaceShadow.FromOverrides(
                    new Dictionary<string, bool> { { FaceShadowItems.Cast, true } }),
            };

            var writer = new StringWriter();
            serializer.Serialize(writer, maid);
            var xml = writer.ToString();
            Assert.Contains("<faceShadow>", xml);
            Assert.Contains("name=\"castShadows\"", xml);

            var read = (ScenePresetMaid)serializer.Deserialize(new StringReader(xml));
            Assert.True(read.faceShadow.ToOverrides()[FaceShadowItems.Cast]);

            var legacy = (ScenePresetMaid)serializer.Deserialize(new StringReader(
                xml.Replace(xml.Substring(xml.IndexOf("<faceShadow>"),
                    xml.IndexOf("</faceShadow>") + "</faceShadow>".Length - xml.IndexOf("<faceShadow>")), "")));
            Assert.Null(legacy.faceShadow);
        }
    }
}
```

（`ScenePresetMaid` を単独でシリアライズできない場合は、`ScenePresetNodeVisibilityTests` と同じく上位の型で往復させる。）

- [ ] **Step 2: テストが失敗することを確認する**

Filter: `FullyQualifiedName~ScenePresetFaceShadow`
Expected: コンパイルエラー

- [ ] **Step 3: 型とフィールドを書く**

`ScenePresetData.cs` の `ScenePresetNodeVisibility` の直後に足す。

```csharp
    /// <summary>顔の影の上書き 1 件</summary>
    public class ScenePresetFaceShadowItem
    {
        /// <summary>FaceShadowItems の名前 (receiveShadows / castShadows)</summary>
        [XmlAttribute]
        public string name;

        [XmlAttribute]
        public bool enabled;
    }

    /// <summary>
    /// 顔の影。SE が上書きしている項目だけを持つ。
    /// 上書きが無くても要素自体は書き、旧プリセット (要素なし = null、適用時に触らない) と区別する
    /// </summary>
    public class ScenePresetFaceShadow
    {
        [XmlElement("item")]
        public List<ScenePresetFaceShadowItem> items = new List<ScenePresetFaceShadowItem>();

        /// <summary>上書きから作る。対象外の項目は書かない</summary>
        public static ScenePresetFaceShadow FromOverrides(IEnumerable<KeyValuePair<string, bool>> overrides)
        {
            var result = new ScenePresetFaceShadow();
            foreach (var pair in overrides)
            {
                if (FaceShadowItems.Find(pair.Key) == null)
                {
                    continue;
                }
                result.items.Add(new ScenePresetFaceShadowItem { name = pair.Key, enabled = pair.Value });
            }
            return result;
        }

        /// <summary>上書きへ戻す。手で書き換えた値に備え、対象外は捨て、同じ項目は先のものを使う</summary>
        public Dictionary<string, bool> ToOverrides()
        {
            var result = new Dictionary<string, bool>();
            foreach (var item in items)
            {
                if (item == null || FaceShadowItems.Find(item.name) == null || result.ContainsKey(item.name))
                {
                    continue;
                }
                result[item.name] = item.enabled;
            }
            return result;
        }
    }
```

`ScenePresetMaid.nodeVisibility` の直後に足す。

```csharp
        /// <summary>顔の影 (v42)。旧プリセットは null になり、適用時に触らない</summary>
        public ScenePresetFaceShadow faceShadow;
```

- [ ] **Step 4: 保存と適用をつなぐ**

`ScenePresetManager.cs` の `state.nodeVisibility = ...` の直後に足す。

```csharp
            state.faceShadow = ScenePresetFaceShadow.FromOverrides(
                MaidFaceRendererManager.instance.GetOverrides(maid));
```

`ApplyNodeVisibility` の try ブロックの直後に同じ形で `ApplyFaceShadow(maid, state);` の try ブロックを足し、メソッドを足す。

```csharp
        /// <summary>顔の影を復元する。旧プリセット (faceShadow 無し) では変更しない</summary>
        private static void ApplyFaceShadow(Maid maid, ScenePresetMaid state)
        {
            if (state.faceShadow == null)
            {
                return;
            }
            MaidFaceRendererManager.instance.SetOverrides(maid, state.faceShadow.ToOverrides());
        }
```

`ApplyNodeVisibility` の呼び出しが 2 か所（`:2190` 付近と、エージェントの報告にある別の適用経路）にあれば両方に足す。`grep -n "ApplyNodeVisibility" Manager/ScenePresetManager.cs` で確かめる。

- [ ] **Step 5: テストが通ることを確認する**

Filter なし（全件）。Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs \
  source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetFaceShadowTests.cs
git commit -m "feat(face-shadow): シーンプリセットに顔の影を保存する"
```

---

### Task 7: PostEffects のキャラ用マスクに Face を含める（別リポジトリ）

**Files（`W:\COM3D2_5\work\COM3D2.PostEffects.Plugin`）:**
- Modify: `source/COM3D25.PostEffects.Plugin/Effects/CharacterMask.cs:6, 19-26, 208`
- Modify: `source/COM3D25.PostEffects.Plugin/Effects/OutlineColorEffect.cs:30, 45, 63, 86`
- Modify: `source/COM3D25.PostEffects.Plugin/Effects/ObscuranceEffect.cs:47`（コメントだけ）
- Modify: `README.md`（v2.5.0.0 の変更履歴）

- [ ] **Step 1: ブランチを切る**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin && git switch -c feat/face-layer-mask
```

- [ ] **Step 2: `CharacterMask` のマスクを直す**

```csharp
        // メイドは Charactor レイヤーに置かれ、顔は VR の一人称モードや SceneEditor の「顔のみ／体のみ」ライトで
        // Face レイヤーへ移る。どちらもキャラとして扱う。名前解決できない場合は既知の 10 / 11 へ落とす
        private static readonly int characterLayerMask = ResolveCharacterLayerMask();

        private static int ResolveCharacterLayerMask()
        {
            return ToLayerMask("Charactor", 10) | ToLayerMask("Face", 11);
        }

        private static int ToLayerMask(string name, int fallback)
        {
            var layer = LayerMask.NameToLayer(name);
            return 1 << (layer >= 0 ? layer : fallback);
        }
```

（`MaidHideEffect.cs:14` に同名の `ToLayerMask` がある。可視性が合えばそちらを共有してよい。）クラス summary（:6）と :208 のコメントの「Charactor レイヤー」を「Charactor / Face レイヤー」にする。`characterLayerMask` を外から使えるよう `internal static int layerMask => characterLayerMask;` を足す（Step 3 で使う）。

- [ ] **Step 3: `OutlineColorEffect` を直す**

`_characterLayer`（int、:30）を `_characterLayerMask` に替え、`OnEnable`（:45）で `_characterLayerMask = CharacterMask.layerMask;` にする。:63 のガード `_characterLayer < 0` は `_characterLayerMask == 0` にする（`CharacterMask` は名前解決できなくても既知の番号へ落とすので実際には 0 にならないが、ガードの意味は残す）。:86 の条件を次にする。

```csharp
                        (_characterLayerMask & (1 << renderer.gameObject.layer)) == 0 ||
```

`grep -n "_characterLayer" Effects/OutlineColorEffect.cs` で残りが無いことを確かめる。

`ObscuranceEffect.cs:47` のコメントの「Charactor レイヤー」を「Charactor / Face レイヤー」にする。

- [ ] **Step 4: 両構成でビルドする**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
"$MSB" COM3D25.PostEffects.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" \
 && "$MSB" COM3D25.PostEffects.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D25_DIR=W:\COM3D2_5"
```

Expected: 両方成功（`build.bat` / `debug.bat` はゲームフォルダへコピーするので使わない）

- [ ] **Step 5: 変更履歴を書く**

`README.md` の v2.5.0.0 の節に 1 行足す: `- 顔が Face レイヤーにあるとき（VR の一人称モード、SceneEditor の顔のみ／体のみライト）も、キャラ除外・キャラのみのマスクと輪郭の色にじみで顔をキャラとして扱うようにした`

- [ ] **Step 6: コミット**

```bash
git add source/COM3D25.PostEffects.Plugin/Effects/CharacterMask.cs source/COM3D25.PostEffects.Plugin/Effects/OutlineColorEffect.cs \
  source/COM3D25.PostEffects.Plugin/Effects/ObscuranceEffect.cs README.md
git commit -m "fix(character-mask): Face レイヤーの顔もキャラとして扱う"
```

---

### Task 8: 実機検証

**前提:** ゲームが旧 DLL を握っているため、`com3d25-devbridge:restart-verify` スキルに従う。**DLL をゲームへ反映する前にユーザーへ確認する**（SceneEditor と PostEffects の両方）。検証は通常シーン（デイリー画面でエディタを有効化）で行う。

- [ ] **Step 1: 照射対象と顔のレイヤー**

追加ライトを 1 灯「顔のみ」にし、`eval_csharp` で確かめる:
- 全メイドの head スロットの Renderer が Face（11）、体が Charactor（10）
- `MaidFaceRendererManager.instance.isFaceLayerActive == true`
- `CharaDirectionalLight` の cullingMask に Face ビットが足されている
- 顔だけが照らされていることをスクリーンショットで見る

ライトを「全て」に戻し、顔が Charactor に戻り、`CharaDirectionalLight` が `0x400` に戻ることを確かめる。「体のみ」でも同様に確かめる。

- [ ] **Step 2: 新ボディ（CRC）の顔**

CRC ボディのメイドを呼び、通常時の顔のレイヤー・影設定を読む。旧ボディと同じ（Charactor / Off / True）でなければ、計画の前提と食い違うのでユーザーに報告する。あわせて `head` と `eye` スロットの Renderer を列挙する。旧ボディでは瞳は `Face7` のマテリアル（`EyeL` / `EyeR`）で `eye` スロットは空だった（2026-10-07 確認）。CRC で瞳が `eye` スロットに分かれていれば、顔のみ／体のみで瞳が体側になるのでユーザーに報告する。

- [ ] **Step 2b: シーンの違い**

日常（デイリー）以外にも、夜伽など別のシーンで「顔のみ」のライトを使い、顔の見え方がおかしくならないかを見る。シーンやプレハブに保存されたカメラ・ライトのマスクは、逆コンパイルしたソースの検索では見えないため。

- [ ] **Step 3: 顔の影 A/B**

表情ウィンドウの影タブで:
- 「影を受ける」OFF で、顔に髪の影が落ちなくなる（影ありの追加ライトを使う）
- 「影を落とす」ON で、顔の影が首や体に落ちる。目・眉（`*_Trans*`）が余計な影を落とさないか見る。落とすならユーザーに報告する
- 解除でゲームの値へ戻る
- 上書き中に着替え（顔の差し替えを含む）をし、新しい顔に付き直す
- Undo / Redo、タイムラインのキー（最初のキーより前のシークで解除される）、シーンプリセットの保存・読込

- [ ] **Step 4: キャラの影の拡張**

「顔のみ」のライトで影あり + キャラの影 ON にすると、髪の影が顔に落ちる。`CharacterShadowProxyManager.instance.proxyCount > 0` を確かめる。

- [ ] **Step 5: PostEffects**

顔を Face へ移した状態で、AO（キャラ除外）が顔に乗らないこと、メイド非表示で顔も消えることを確かめる。

- [ ] **Step 6: UI**

- 表情ウィンドウの 8 タブが欠けずに表示される
- ライトの「対象」のドロップダウンで 5 項目すべてが見える
- 「影を落とす」のキーを Inspector でリセットしたときの値を見る（`CustomValueInfo.defaultValue` は項目共通で 1 = ON）。ゲームの既定（OFF）と食い違って使いにくければ、ユーザーに報告する

検証で見つかった不具合は該当タスクへ戻って直し、コミットを分ける。

---

### Task 9: ドキュメント

**Files:**
- Modify: `docs-site/guide/staging.md:92, 113`（対象の表に顔のみ・体のみ、キャラの影の条件）
- Modify: `docs-site/guide/maid-editing.md`（表情ウィンドウの影タブ。ウィンドウ一覧 :7 も）
- Modify: `docs-site/guide/scene-preset.md:17`（保存対象に顔の影）
- Modify: `docs-site/timeline/layers-maid.md`（メイド顔の影レイヤー。ノード表示の節と同じ形）
- Modify: `docs-site/timeline/layers-effect.md:19`（ライトキーの対象の値域）
- Modify: `docs-site/timeline/compatibility.md`（FaceShadow と照射対象 3 / 4）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（タイムライン XML の互換方向に 2 項目）

- [ ] **Step 1: docs-site を書く**

staging.md の対象の説明に入れる内容:
- `顔のみ`: メイドの顔（head スロット。髪・帽子・メガネは含まない）だけを照らす。`体のみ`: キャラのうち顔以外を照らす（男は全身）
- 顔のみ／体のみのライトが 1 灯でも有効な間、SceneEditor が顔を Face レイヤーへ移す。PostEffects.Plugin で顔を正しくキャラとして扱うには v2.5.0.0 以降が必要
- キャラの影: 背景のみ・顔のみ・体のみのときに出る。顔のみでは、照らさない髪や体の影を顔に落とす

maid-editing.md の影タブ: 2 項目の意味とゲームの既定（受ける ON・落とす OFF）、`*` と解除、目・眉などの半透明部分は元から影を受けないこと、メイド顔の影レイヤーへのキー化。

layers-maid.md: レイヤー名・項目・値（bool、区間開始で適用、補間なし）・最初のキーより前は解除。

compatibility.md と CLAUDE.md に足す項目（CLAUDE.md の文体に合わせる）:
- 「メイド顔の影レイヤー（`FaceShadowTimelineLayer`、head スロットの影を受ける／落とすの bool、`TransformType.FaceShadow`）は SE 独自。version は上げていない。これを含む XML は MTE・旧 SE では未知の enum 値（`<Type>FaceShadow</Type>`）でデシリアライズに失敗する。キーにするのは SE が上書きしている項目だけ。シーンプリセットは v42 で maid に `faceShadow` 要素（上書きのある項目だけ `<item name enabled>`。上書きが無くても空要素を書く）を追加。要素の無い旧プリセットは適用時に触らない」
- 「ライトキーの照射対象（index 18）の値 3（顔のみ）・4（体のみ）は SE 独自。version は上げていない。旧 SE は `ClampMode` で「全て」として読む。シーンプリセットは v42 で追加ライトの `target` に同じ値を許す。顔のみ／体のみのライトがある間だけ、SE がメイドの顔（head スロット）を Charactor から Face レイヤーへ移す」

- [ ] **Step 2: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site/
git commit -m "docs(docs-site): 顔と体の影・照明の分離を書く"
```

`W:\COM3D2_5\work\CLAUDE.md` はどのリポジトリにも属さないか確かめ（`git -C /w/COM3D2_5/work status` が失敗すればリポジトリ外）、リポジトリ外ならコミットせず編集だけにする。

---

## 最後に

- code-review スキルで両リポジトリのブランチ全体をレビューする（`final-review-uses-code-review-policy` に従い、指摘ごとに取捨を判定する）
- コミットは commit スキルで行う。マージ・PR はユーザーの指示を待つ

## レビュー却下メモ

- head スロット外の瞳（`eye` スロット）が顔に含まれない — 誤検知。旧ボディでは瞳は `Face7` のマテリアルで `eye` スロットは空（実機で確認）。CRC だけ Task 8 Step 2 で確かめる
- SE を介さずストックへ戻ったメイドに上書きが持ち越される — 見送り。ノード表示・体型など既存のメイドごとの上書きと同じ解放経路（`MaidManipulateManager.Release`）にそろえる。別の経路を足すなら全コントローラーで一緒に直す
- ゲーム側ライト補正の 60 フレームの遅れ・逆向きマスクのライト — 補正は入れたまま、限界を決定事項に書いて許容する（`CharaDirectionalLight` は普段無効で、有効にするコードも見つかっていない）
- `isFaceLayerActive` / `FaceShadowRowDrawer.RecordEdit` の公開範囲 — 計画どおり（前者は検証用、後者は NodeVisibilityRowDrawer と同じ形）
