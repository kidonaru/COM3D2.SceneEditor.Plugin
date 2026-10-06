# 体型スライダー（MaidScale 廃止・ModsSlider 一本化） Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ModsSlider（AddModsSlider + MaidVoicePitch）の体型スライダー相当の値を SE 自身が骨に掛ける。値はメイドごとに持ち、タイムライン・シーンプリセット・Undo に載せる。CRC ボディでも MVP なしでも効くようにする。腕（上腕・前腕・手）は左右別に調整できるようにする。既存の「メイドスケール」（腕 6 本の均一倍率）はこれに吸収して廃止する。

**Architecture:**
- **定義と純粋ロジック**: `BodySliderDefs` に全項目の定義を 1 か所でまとめる。定義に持つのは、prop 名、表示名、グループ、種類（スケール / 位置）、3 成分のラベル・範囲・表示有無、対象骨と軸の変換。`BodySliderState` はメイド 1 体分の値を持つ。骨ごとの合成（スケールは積、位置は和）を `BuildBoneOps` で計算する。どちらもゲーム外テストで固定する。
- **適用**: `BodySliderController` は `MaidScaleController` を汎用化したもの。`TBody.LateUpdate` の postfix で、全スロットの複製骨に「今の値 × 倍率」「今の値 + 差分」を書く。次の prefix / Update で、自分が書いた値のまま残っている骨だけを戻す。
- **周辺**:
  - タイムライン: `BodySliderTimelineLayer`、`TransformType.BodySlider`（値 3 個、タンジェント補間）
  - Undo: `HistoryScope.BodySlider`、`BodySliderSnapshot`
  - シーンプリセット: `ScenePresetMaid.bodySlider`（v41）
  - UI: ボーンウィンドウの「体型」タブと Inspector
- **移行**: 旧 MaidScale のタイムライン（v39 の変換段）とシーンプリセット（`maidScale` 要素）は、腕の左右別項目へ移す。

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、Harmony、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-05-body-slider-draft.md`（2026-10-06 の決定事項を含む）

## Global Constraints

- **値の重ね方**: ExSaveData（MVP の設定）は読まない・書かない。各フレームの骨の現在値（MVP の結果を含む）に対し、スケールは乗算、位置は加算する。
- **書き込み先**: 複製骨だけに書く。複製骨は、各スロットの `SkinnedMeshRenderer.bones` と、その祖先のうちスロット obj 配下にあるもの。本体骨には書かない（IK・アタッチ・ギズモの基準を崩さないため）。存在しない骨は黙って飛ばす。
- **書くタイミング**: `TBody.LateUpdate` の postfix。戻すのは次の prefix と、プラグインの `Update`。戻すのは、今の値が自分の書いた値のままのとき（`ShouldRestore`）だけにする。
- **撮影・GUI・着替え**: `MaidScaleController` の作法をそのまま引き継ぐ。
  - GUI 描画中は外す（`SuspendApplied` / `ResumeApplied`）。
  - OnGUI 内の撮影の間だけ掛け直す（`BeginCapture` / `EndCapture`）。
  - `isLoadedBody == false` か `IsAllProcPropBusy` の間はキャッシュを捨てて書かない。
  - `Release` / `Destroy` で戻す。
- **変換式**: MVP v0.2.17.6 の `WideSlider` に合わせる（下の「変換表」）。
- **腕の左右**: UPARMSCL / FARMSCL / HANDSCL は、項目 `<PROP>_L` / `<PROP>_R` に分ける。肩（KATASCL / CLVSCL / CLVPOS）を含め、他の prop は左右共通。
- **対象外**: S1ABASESCL（spec の決定事項）と、体型以外の MVP 項目（WIDESLIDER、FARMFIX、EYE_ANG など）。
- **値の範囲**: ModsParam.xml の min / max に丸める。NaN は既定値にする（スケール 1、位置 0）。
- **タイムラインの版**: `TimelineData.CurrentVersion` を 38 → 39 に上げる。v39 未満では `MaidScaleTimelineLayer` を `BodySliderTimelineLayer` へ変換する。`TransformType.MaidScale` は読込互換のため残す。
- **プリセットの版**: `ScenePresetData.CurrentVersion` を 40 → 41 に上げる。`maidScale` は読込専用で残す。
- **言語**: コードのコメント・ログは日本語。
- **ビルド**: 両構成（COM3D2 / COM3D25）でビルドする。`deploy.bat` は実行しない。ビルド確認は MSBuild を直接叩く。
- **テスト**: Unity のネイティブ呼び出し（`Quaternion.Euler` など）は使えない。`Vector3` の生成・四則・`Mathf.Clamp` / `Mathf.Approximately` は使える。

## 変換表（MVP v0.2.17.6 から抽出）

値は ModsParam のサブキーの並びで 3 個持つ。スケールは `(width, depth, height)`、位置は `(x, y, z)`。

**スケール**: 全項目共通で、骨の `localScale` へ掛ける倍率は `(x: height, y: depth, z: width)`。係数はかけない。L / R で符号は変わらない。

| 項目 | 表示名 | 対象骨 | 範囲 | 非表示の成分 |
|---|---|---|---|---|
| THISCL | 足全体のスケーリング | Bip01 L/R Thigh | 0.1〜2 | height |
| THISCL2 | 膝のスケーリング | Bip01 L/R Thigh_SCL_ | 0.1〜2 | height |
| MTWSCL | ももねじれ肉のスケーリング | momotwist_L/R | 0.1〜2 | |
| MMNSCL | もも肉のスケーリング | momoniku_L/R | 0.1〜2 | |
| CALFSCL | 膝下のスケーリング | Bip01 L/R Calf | 0.1〜2 | height |
| FOOTSCL | 足首より下のスケーリング | Bip01 L/R Foot | 0.1〜2 | |
| PELSCL | 骨盤スケーリング | Bip01 Pelvis_SCL_、Hip_L/R | 0.1〜2 | |
| HIPSCL | 尻スケーリング | Hip_L/R | 0.1〜2 | |
| SKTSCL | スカート周辺スケーリング | Skirt | 0.1〜3 | |
| SPISCL | 胴(下腹部)スケーリング | Bip01 Spine_SCL_ | 0.1〜3 | |
| S0ASCL | 胴(腹部)スケーリング | Bip01 Spine0a_SCL_ | 0.1〜3 | |
| S1_SCL | 胴(みぞおち)スケーリング | Bip01 Spine1_SCL_ | 0.1〜3 | |
| S1ASCL | 胴(肋骨)スケーリング | Bip01 Spine1a_SCL_ | 0.1〜3 | |
| NECKSCL | 首スケーリング | Bip01 Neck_SCL_ | 0.1〜3 | |
| MUNESCL | 胸スケーリング | Mune_L/R | 0.1〜3 | |
| MUNESUBSCL | 胸サブスケーリング | Mune_L_sub / Mune_R_sub | 0.1〜3 | |
| CLVSCL | 鎖骨スケーリング | Bip01 L/R Clavicle | 0.1〜3 | |
| KATASCL | 肩スケーリング | Kata_L/R | 0.1〜3 | |
| UPARMSCL_L / _R | 上腕スケーリング(左/右) | Bip01 L(R) UpperArm | 0.1〜3 | |
| FARMSCL_L / _R | 前腕スケーリング(左/右) | Bip01 L(R) Forearm | 0.1〜3 | |
| HANDSCL_L / _R | 手スケーリング(左/右) | Bip01 L(R) Hand | 0.1〜3 | |

- 成分のラベルは ModsParam のとおり（width=横幅、depth=奥行、height=高さ）。例外が 3 つある。
  - MUNESCL / MUNESUBSCL は depth=縦幅、height=奥行。
  - CLVSCL は width=高さ、height=横幅。ModsParam の誤記だが、ModsSlider と同じ表示にそろえるためそのまま使う。
- PELSCL と HIPSCL が両方掛かる Hip_L/R は、成分ごとの積になる（MVP と同じ）。

**位置**: `localPosition += 差分`。下の表の記法では、差分の (x, y, z) を `+X` などの並びで書く。X / Y / Z はサブキー `.x` / `.y` / `.z` の値、`0` は常に 0。

| 項目 | 表示名 | 係数 | 対象骨 | L（または単独）の差分 | R の差分 | 範囲 | 非表示 |
|---|---|---|---|---|---|---|---|
| THIPOS | 足の位置 | /1000 | Bip01 L/R Thigh、Hip_L/R | (0, +Z, -X) | (0, +Z, +X) | -100〜200 | y |
| THI2POS | 膝の位置 | /1000 | Bip01 L/R Thigh_SCL_ | (+Y, +Z, -X) | (+Y, +Z, +X) | -100〜200 | |
| HIPPOS | 尻の位置 | /1000 | Hip_L/R | (+Y, +Z, -X) | (+Y, +Z, +X) | -100〜200 | |
| MTWPOS | ももねじれ肉の位置 | /10 | momotwist_L/R | (+X, +Y, +Z) | (+X, +Y, -Z) | -1〜1 | |
| MMNPOS | もも肉の位置 | /10 | momoniku_L/R | (+X, +Y, +Z) | (+X, -Y, +Z) | -1〜1 | |
| SKTPOS | スカート位置 | /10 | Skirt | (-Z, -Y, +X) | — | -1〜1 | x, y |
| SPIPOS | 胴(下腹部)ポジション | /10 | Bip01 Spine | (-X, +Y, +Z) | — | -1〜1 | y |
| S0APOS | 胴(腹部)ポジション | /10 | Bip01 Spine0a | (-X, +Y, +Z) | — | -1〜1 | z |
| S1POS | 胴(みぞおち)ポジション | /10 | Bip01 Spine1 | (-X, +Y, +Z) | — | -1〜1 | z |
| S1APOS | 胴(肋骨)ポジション | /10 | Bip01 Spine1a | (-X, +Y, +Z) | — | -1〜1 | z |
| NECKPOS | 首ポジション | /10 | Bip01 Neck | (-X, +Y, +Z) | — | -1〜1 | z |
| CLVPOS | 鎖骨の位置 | /10 | Bip01 L/R Clavicle | (-X, +Y, +Z) | (-X, +Y, -Z) | -1〜1 | |
| MUNEPOS | 胸ポジション | /10 | Mune_L/R | (+Z, -Y, +X) | (+Z, -Y, -X) | -1〜1 | |
| MUNESUBPOS | 胸サブポジション | /10 | Mune_L_sub / Mune_R_sub | (-Y, +Z, -X) | (-Y, -Z, -X) | -1〜1 | |

- 成分のラベルは ModsParam のとおり。THIPOS / THI2POS / HIPPOS は x=左右、y=高さ、z=前後。MTWPOS / MMNPOS は x=高さ、y=前後、z=左右。SKTPOS は x=左右、y=前後、z=高さ。SPIPOS は x=前後、y=高さ、z=上下。S0APOS / S1POS / S1APOS / NECKPOS / CLVPOS は x=上下、y=前後、z=左右。MUNEPOS / MUNESUBPOS は x=左右、y=前後、z=高さ。
- Hip_L/R の位置は THIPOS + HIPPOS の和になる（MVP と同じ）。

## 決定事項

| 論点 | 決定 | 理由 |
|---|---|---|
| 値の単位 | ModsParam のサブキーそのまま（スケール = width/depth/height、位置 = x/y/z の生値）で持つ。骨の軸への変換は適用時に行う | UI・タイムライン・プリセットの数値を ModsSlider と一致させ、ユーザーが同じ値を打ち込めるようにする |
| 腕の左右 | 項目キーは `UPARMSCL_L` / `UPARMSCL_R` のように `_L` / `_R` を付ける | ユーザーの決定（2026-10-06）。ModsParam の prop 名と衝突しない |
| タイムラインのキー | 既定でない項目と、レイヤーに既にキーのある項目だけをキーにする（`NodeVisibilityTimelineLayer.BuildKeys` と同じ考え方） | 38 項目を毎回全部キーにすると、XML とキー一覧が膨らむ。既にキーのある項目を含めるのは、途中で既定へ戻した値をキーに残すため |
| キーより前の区間・キーの無い項目 | 何もしない（基底どおり、UI で入れた値を保つ）。spec の「キーが無い prop は既定値として扱う」からは変えた | MaidScale と同じ。キーの無い項目を毎フレーム既定値へ戻すと、UI で入れた値がすぐ消える |
| 項目ごとのリセット | 専用ボタンは置かない。成分ごとのスライダーのリセット（`SliderOption.defaultValue`）と「すべて既定に戻す」で足りる | spec の「項目ごとにリセットボタン」は、成分のリセットで代替する |
| 範囲外・NaN | 成分ごとに min / max へ丸め、NaN は既定値にする | MaidScale と同じ方針。手で書き換えたプリセットへの備え |
| 非表示の成分 | UI には出さないが、値は持ち、適用もする | MVP も非表示の成分を適用する。キーやプリセットに入っていれば効かせる |
| 旧 MaidScale のプリセット | `bodySlider` が無く `maidScale` がある場合だけ、腕の左右別項目へ変換して適用する。その他の項目は既定値へ戻す | 旧プリセットの時点では体型スライダーが存在しない。既定値に戻すのが「保存した時点の状態」になる |
| 旧 MaidScale のキーフレームテンプレート | 変換しない | MaidScale は未リリース（v2.5.0.0 より後の追加）。テンプレートは今のレイヤーの `layerName` で引く（`TimelineTemplateManager.cs:348,416`）ため、`MaidScaleTimelineLayer` のテンプレートは、そのレイヤーが無くなった後は読まれも適用もされない |
| キーフレームインスペクタの非表示成分 | 3 成分すべてを出す（ボーンウィンドウ・Inspector の行描画では隠す） | `GetCustomValueInfoMap` は `Reset` と補間種別の判定にも使われるため、非表示の成分だけ外すと既定値へ戻らなくなる |
| UI のグループ | 「脚」「腰」「胴・首」「胸」「肩・腕」の内部タブで切り替える | 全 38 項目（脚 10・腰 5・胴首 10・胸 4・肩腕 9）を 1 画面に並べると 100 行近くになる |
| 骨ごとの量の作り直し | 値を変えたら dirty にし、次に掛ける直前に 1 回だけ `BuildBoneOps` する | タイムライン再生中は、キーのある項目ごとに毎フレーム `SetValues` が呼ばれる。そのたびに全項目を走査しないため |

## 版の判断

- **タイムライン（38 → 39）**: `MaidScaleTimelineLayer` のクラスを消すため、旧 XML のレイヤーを読込時に変換する必要がある。版で変換段を区切る。v39 の XML は MTE と旧 SE では未知の enum `<Type>BodySlider</Type>` によりデシリアライズに失敗する（NodeVisibility と同じ扱い）。
- **シーンプリセット（40 → 41）**: `bodySlider` の追加と、`maidScale` の読込専用化を版の履歴に残す。要素が無い旧プリセットは「触らない」で読めるので、版で挙動は分けない。

## Review Focus

1. **腕の左右の独立**: 左の上腕を 2 にしても、右の上腕の骨は 1 のままであること。`BuildBoneOps` のテストで固定する（Task 1）。
2. **1 本の骨に複数の項目が掛かるとき**: Hip_L は PELSCL × HIPSCL、THIPOS + HIPPOS で合成されること。片方だけ既定のときも正しいこと（Task 1）。
3. **旧タイムラインのタンジェント**: MaidScale のキーにあった in/out タンジェントと smooth ビットが、3 成分すべてへ写ること。値が欠けた（0 個の）キーは倍率 1 として扱われること（Task 4）。
4. **旧プリセットの優先順位**: `bodySlider` と `maidScale` の両方を持つプリセットでは `bodySlider` を使うこと。`maidScale` だけなら腕へ変換されること（Task 4）。
5. **手で壊した値**: プリセットやキーの NaN・範囲外の値が、丸められてから骨に掛かること（Task 1 / Task 4）。

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderDefs.cs` | 項目定義（`BodySliderItem` / `BodySliderComponent` / `BodySliderTarget` / `BodySliderAxis`）、グループ、旧 MaidScale の骨名 → 項目キー | 新規 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderState.cs` | メイド 1 体分の値、骨ごとの合成（`BuildBoneOps`）、`ShouldRestore` | 新規 |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderController.cs` | 複製骨のキャッシュ、書き込みと戻し、GUI・撮影の一時解除 | 新規（`MaidScaleController.cs` を置き換え） |
| `source/COM3D2.SceneEditor.Plugin/Manager/BodySliderLateUpdatePatch.cs` | `TBody.LateUpdate` の prefix / postfix | 新規（`MaidScaleLateUpdatePatch.cs` を置き換え） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBodySlider.cs` | キー 1 件（値 3 個） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BodySliderTimelineLayer.cs` | タイムラインレイヤー | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BodySliderItemInspector.cs` | Inspector | 新規 |
| `source/COM3D2.SceneEditor.Plugin/BodySliderRowDrawer.cs` | 項目 1 件分のスライダー（ボーンウィンドウと Inspector で共有） | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/BodySliderSnapshot.cs` | Undo のスナップショット | 新規 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | v39 の変換段 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `CurrentVersion` 39 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | `ScenePresetBodySlider`、v41、`ScenePresetMaidScale` の読込専用化 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 記録・適用 | 変更 |
| `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs` | 「腕スケール」タブ → 「体型」タブ | 変更 |
| `COM3D2.SceneEditor.Plugin.cs` / `MaidManipulateManager.cs` / `ScreenshotManager.cs` / `ThumbnailCapture.cs` / `TimelineIntegration.cs` / `HistoryScope.cs` / `SnapshotFactory.cs` / `ITransformData.cs` / csproj | 登録・呼び出しの差し替え | 変更 |
| MaidScale 系 9 ファイルとテスト 2 ファイル | 廃止 | 削除（Task 5） |
| `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderDefsTests.cs` / `BodySliderStateTests.cs` / `BodySliderLayerTests.cs` / `BodySliderMigrationTests.cs` / `ScenePresetBodySliderTests.cs` | テスト | 新規 |
| `docs-site/guide/maid-editing.md` / `docs-site/timeline/layers-maid.md` / `docs-site/timeline/compatibility.md` / `docs-site/guide/scene-preset.md` / `W:\COM3D2_5\work\CLAUDE.md` | ドキュメント | 変更 |

テストプロジェクトは SDK 形式なので、テストファイルは置くだけでビルド対象になる。プラグイン本体の csproj は `<Compile Include>` を明示する必要がある。

### ビルド + テストのコマンド（全タスク共通）

Git Bash で実行する。`<Filter>` はタスクごとに指定する。

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests --filter "<Filter>"
```

COM3D2 → COM3D25 の順でビルドする（逆だとテストが参照する COM3D25 の DLL が消える）。MSBuild の DLL コピー警告（ゲーム起動中）は無視してよい。

---

### Task 1: 項目定義と純粋ロジック（`BodySliderDefs` / `BodySliderState`）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderDefs.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`MaidManipulation\MaidScaleState.cs` の行の直後に 2 行追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderDefsTests.cs`、`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderStateTests.cs`

**Interfaces:**
- Produces:
  - `enum BodySliderKind { Scale, Position }`
  - `class BodySliderComponent { string name; string label; float min; float max; bool visible; }`
  - `struct BodySliderAxis { int source; float sign; }`（`source` < 0 は常に 0）
  - `class BodySliderTarget { string boneName; BodySliderAxis[] axes; }`（`axes` は位置のときだけ。スケールでは null）
  - `class BodySliderItem`:
    - フィールド: `string key`, `string displayName`, `string group`, `BodySliderKind kind`, `BodySliderComponent[] components`（3 個）, `float positionDivisor`, `List<BodySliderTarget> targets`
    - `float defaultValue`、`Vector3 defaultValues`
    - `Vector3 Clamp(Vector3 values)`、`bool IsDefault(Vector3 values)`
    - `Vector3 ToScale(Vector3 values)`、`Vector3 ToOffset(Vector3 values, BodySliderTarget target)`
  - `static class BodySliderDefs`:
    - `List<BodySliderItem> items`、`string[] groupNames`
    - `BodySliderItem Find(string key)`、`bool IsTargetBone(string boneName)`、`IEnumerable<BodySliderItem> ItemsInGroup(string group)`
    - `Dictionary<string, string> legacyMaidScaleKeys`（旧 MaidScale の骨名 → 項目キー）
  - `struct BodySliderBoneOp { Vector3 scale; Vector3 offset; bool hasScale; bool hasOffset; }`
  - `class BodySliderState`:
    - `bool isDefault`、`IEnumerable<KeyValuePair<string, Vector3>> nonDefaultValues`
    - `Vector3 Get(string key)`、`void Set(string key, Vector3 values)`、`void Clear()`
    - `void BuildBoneOps(Dictionary<string, BodySliderBoneOp> result)`
    - `static bool ShouldRestore(Vector3 current, Vector3 written)`

- [ ] **Step 1: 定義のテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderDefsTests.cs`:

```csharp
using System.Linq;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーの項目定義を固定する。変換は MaidVoicePitch v0.2.17.6 の WideSlider に合わせている</summary>
    public class BodySliderDefsTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
            Assert.Equal(expected.z, actual.z, 5);
        }

        [Fact]
        public void 項目キーは重複しない()
        {
            var keys = BodySliderDefs.items.Select(i => i.key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void 腕は左右別の項目で肩は左右共通()
        {
            Assert.NotNull(BodySliderDefs.Find("UPARMSCL_L"));
            Assert.NotNull(BodySliderDefs.Find("UPARMSCL_R"));
            Assert.NotNull(BodySliderDefs.Find("FARMSCL_L"));
            Assert.NotNull(BodySliderDefs.Find("HANDSCL_R"));
            Assert.Null(BodySliderDefs.Find("UPARMSCL"));
            Assert.NotNull(BodySliderDefs.Find("KATASCL"));
            Assert.NotNull(BodySliderDefs.Find("CLVPOS"));

            var left = BodySliderDefs.Find("UPARMSCL_L");
            Assert.Single(left.targets);
            Assert.Equal("Bip01 L UpperArm", left.targets[0].boneName);
            Assert.Equal("上腕スケーリング(左)", left.displayName);
        }

        [Fact]
        public void 対象外の項目は定義に無い()
        {
            Assert.Null(BodySliderDefs.Find("S1ABASESCL"));
            Assert.Null(BodySliderDefs.Find("FARMFIX"));
            Assert.Null(BodySliderDefs.Find(null));
            Assert.Null(BodySliderDefs.Find(""));
        }

        [Fact]
        public void 全項目がいずれかのグループに属する()
        {
            foreach (var item in BodySliderDefs.items)
            {
                Assert.Contains(item.group, BodySliderDefs.groupNames);
            }
            var total = BodySliderDefs.groupNames.Sum(g => BodySliderDefs.ItemsInGroup(g).Count());
            Assert.Equal(BodySliderDefs.items.Count, total);
        }

        [Fact]
        public void スケールは幅奥行高さを骨のzyxへ割り当てる()
        {
            var item = BodySliderDefs.Find("THISCL");
            // values = (width, depth, height)
            AssertVector(new Vector3(3f, 2f, 1f), item.ToScale(new Vector3(1f, 2f, 3f)));
        }

        [Fact]
        public void 位置は係数で割り左右で符号が変わる()
        {
            var item = BodySliderDefs.Find("THIPOS");
            var left = item.targets.First(t => t.boneName == "Bip01 L Thigh");
            var right = item.targets.First(t => t.boneName == "Bip01 R Thigh");
            var values = new Vector3(10f, 999f, 20f); // y は使わない

            AssertVector(new Vector3(0f, 0.02f, -0.01f), item.ToOffset(values, left));
            AssertVector(new Vector3(0f, 0.02f, 0.01f), item.ToOffset(values, right));
        }

        [Fact]
        public void 胸の位置は軸を入れ替える()
        {
            var item = BodySliderDefs.Find("MUNEPOS");
            var left = item.targets.First(t => t.boneName == "Mune_L");
            var right = item.targets.First(t => t.boneName == "Mune_R");
            var values = new Vector3(0.1f, 0.2f, 0.3f);

            AssertVector(new Vector3(0.03f, -0.02f, 0.01f), item.ToOffset(values, left));
            AssertVector(new Vector3(0.03f, -0.02f, -0.01f), item.ToOffset(values, right));
        }

        [Fact]
        public void 足の位置は尻の骨にも掛かる()
        {
            var item = BodySliderDefs.Find("THIPOS");
            Assert.Contains(item.targets, t => t.boneName == "Hip_L");
            Assert.Contains(item.targets, t => t.boneName == "Hip_R");
        }

        [Fact]
        public void 範囲はModsParamに合わせる()
        {
            var thi = BodySliderDefs.Find("THISCL").components[0];
            Assert.Equal(0.1f, thi.min);
            Assert.Equal(2f, thi.max);
            var arm = BodySliderDefs.Find("UPARMSCL_L").components[0];
            Assert.Equal(3f, arm.max);
            var pos = BodySliderDefs.Find("THIPOS").components[0];
            Assert.Equal(-100f, pos.min);
            Assert.Equal(200f, pos.max);
        }

        [Fact]
        public void 非表示の成分はModsParamに合わせる()
        {
            Assert.False(BodySliderDefs.Find("THISCL").components[2].visible);  // height
            Assert.True(BodySliderDefs.Find("THISCL").components[0].visible);
            Assert.False(BodySliderDefs.Find("THIPOS").components[1].visible);  // y
            Assert.False(BodySliderDefs.Find("SKTPOS").components[0].visible);  // x
            Assert.False(BodySliderDefs.Find("SKTPOS").components[1].visible);  // y
            Assert.True(BodySliderDefs.Find("SKTPOS").components[2].visible);   // z
        }

        [Theory]
        [InlineData(float.NaN, 1f)]
        [InlineData(0f, 0.1f)]
        [InlineData(9f, 2f)]
        [InlineData(1.5f, 1.5f)]
        public void スケールは範囲へ丸めNaNは1にする(float input, float expected)
        {
            var item = BodySliderDefs.Find("THISCL");
            Assert.Equal(expected, item.Clamp(new Vector3(input, 1f, 1f)).x);
        }

        [Fact]
        public void 位置のNaNは0にする()
        {
            var item = BodySliderDefs.Find("SPIPOS");
            Assert.Equal(0f, item.Clamp(new Vector3(float.NaN, 0f, 0f)).x);
            Assert.True(item.IsDefault(new Vector3(float.NaN, 0f, 0f)));
        }

        [Fact]
        public void 既定値はスケール1と位置0()
        {
            Assert.Equal(Vector3.one, BodySliderDefs.Find("THISCL").defaultValues);
            Assert.Equal(Vector3.zero, BodySliderDefs.Find("THIPOS").defaultValues);
            Assert.True(BodySliderDefs.Find("THISCL").IsDefault(new Vector3(1.000001f, 1f, 1f)));
            Assert.False(BodySliderDefs.Find("THISCL").IsDefault(new Vector3(1.5f, 1f, 1f)));
        }

        [Fact]
        public void 対象骨の判定はスケールと位置の両方を含む()
        {
            Assert.True(BodySliderDefs.IsTargetBone("Bip01 L UpperArm"));
            Assert.True(BodySliderDefs.IsTargetBone("Bip01 Spine"));
            Assert.True(BodySliderDefs.IsTargetBone("Hip_R"));
            Assert.False(BodySliderDefs.IsTargetBone("Bip01 Head"));
            Assert.False(BodySliderDefs.IsTargetBone(null));
        }

        [Fact]
        public void 旧メイドスケールの骨名は腕の左右別項目へ対応する()
        {
            Assert.Equal("UPARMSCL_L", BodySliderDefs.legacyMaidScaleKeys["Bip01 L UpperArm"]);
            Assert.Equal("FARMSCL_R", BodySliderDefs.legacyMaidScaleKeys["Bip01 R Forearm"]);
            Assert.Equal("HANDSCL_L", BodySliderDefs.legacyMaidScaleKeys["Bip01 L Hand"]);
            Assert.Equal(6, BodySliderDefs.legacyMaidScaleKeys.Count);
        }
    }
}
```

- [ ] **Step 2: 状態のテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderStateTests.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーの値の保持と、骨ごとの合成を固定する</summary>
    public class BodySliderStateTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
            Assert.Equal(expected.z, actual.z, 5);
        }

        private static Dictionary<string, BodySliderBoneOp> BuildOps(BodySliderState state)
        {
            var ops = new Dictionary<string, BodySliderBoneOp>();
            state.BuildBoneOps(ops);
            return ops;
        }

        [Fact]
        public void 未設定の項目は既定値で状態なし()
        {
            var state = new BodySliderState();
            Assert.True(state.isDefault);
            Assert.Equal(Vector3.one, state.Get("THISCL"));
            Assert.Equal(Vector3.zero, state.Get("THIPOS"));
        }

        [Fact]
        public void 設定すると丸めて保持し既定へ戻すと状態なしになる()
        {
            var state = new BodySliderState();
            state.Set("THISCL", new Vector3(9f, 1f, 1f));
            Assert.False(state.isDefault);
            Assert.Equal(2f, state.Get("THISCL").x);

            state.Set("THISCL", Vector3.one);
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 定義に無いキーへの設定は無視する()
        {
            var state = new BodySliderState();
            state.Set("UNKNOWN", new Vector3(2f, 2f, 2f));
            state.Set(null, new Vector3(2f, 2f, 2f));
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 左の上腕は左の骨にだけ掛かる()
        {
            var state = new BodySliderState();
            state.Set("UPARMSCL_L", new Vector3(2f, 2f, 2f));

            var ops = BuildOps(state);
            Assert.True(ops.ContainsKey("Bip01 L UpperArm"));
            Assert.False(ops.ContainsKey("Bip01 R UpperArm"));
            AssertVector(new Vector3(2f, 2f, 2f), ops["Bip01 L UpperArm"].scale);
            Assert.True(ops["Bip01 L UpperArm"].hasScale);
            Assert.False(ops["Bip01 L UpperArm"].hasOffset);
        }

        [Fact]
        public void 尻の骨は骨盤と尻のスケールの積になる()
        {
            var state = new BodySliderState();
            state.Set("PELSCL", new Vector3(2f, 1f, 1f));   // width → z
            state.Set("HIPSCL", new Vector3(1.5f, 1f, 1f));

            var ops = BuildOps(state);
            AssertVector(new Vector3(1f, 1f, 3f), ops["Hip_L"].scale);
            AssertVector(new Vector3(1f, 1f, 2f), ops["Bip01 Pelvis_SCL_"].scale);
        }

        [Fact]
        public void 尻の骨は足の位置と尻の位置の和になる()
        {
            var state = new BodySliderState();
            state.Set("THIPOS", new Vector3(10f, 0f, 0f));   // L: z = -X/1000
            state.Set("HIPPOS", new Vector3(0f, 20f, 0f));   // L: x = +Y/1000

            var ops = BuildOps(state);
            AssertVector(new Vector3(0.02f, 0f, -0.01f), ops["Hip_L"].offset);
            Assert.True(ops["Hip_L"].hasOffset);
            Assert.False(ops["Hip_L"].hasScale);
        }

        [Fact]
        public void 片方だけ既定でも合成できる()
        {
            var state = new BodySliderState();
            state.Set("HIPSCL", new Vector3(1f, 1f, 2f));   // height → x

            var ops = BuildOps(state);
            AssertVector(new Vector3(2f, 1f, 1f), ops["Hip_R"].scale);
            Assert.False(ops.ContainsKey("Bip01 Pelvis_SCL_"));
        }

        [Fact]
        public void 合成の前に結果を空にする()
        {
            var state = new BodySliderState();
            var ops = new Dictionary<string, BodySliderBoneOp>
            {
                { "Bip01 Head", new BodySliderBoneOp() },
            };
            state.BuildBoneOps(ops);
            Assert.Empty(ops);
        }

        [Fact]
        public void 自分が書いた値のままなら戻し他から書き換えられていたら戻さない()
        {
            Assert.True(BodySliderState.ShouldRestore(new Vector3(2f, 2f, 2f), new Vector3(2f, 2f, 2f)));
            Assert.False(BodySliderState.ShouldRestore(new Vector3(1f, 2f, 2f), new Vector3(2f, 2f, 2f)));
        }

        [Fact]
        public void Clearで全項目を既定へ戻す()
        {
            var state = new BodySliderState();
            state.Set("THISCL", new Vector3(1.5f, 1f, 1f));
            state.Clear();
            Assert.True(state.isDefault);
        }
    }
}
```

- [ ] **Step 3: テストが失敗することを確かめる**

上の「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySlider` で実行する。
Expected: テストプロジェクトのコンパイルエラー（`BodySliderDefs` / `BodySliderState` が無い）

- [ ] **Step 4: `BodySliderDefs.cs` を書く**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderDefs.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    public enum BodySliderKind
    {
        /// <summary>骨の localScale へ掛ける倍率。値は (width, depth, height)</summary>
        Scale,
        /// <summary>骨の localPosition へ足す差分。値は (x, y, z)</summary>
        Position,
    }

    /// <summary>項目の成分 1 つ分 (ModsParam の value 1 行)</summary>
    public class BodySliderComponent
    {
        /// <summary>ModsParam のサブキー名 (width / x など)。カスタム値のキーにも使う</summary>
        public readonly string name;
        public readonly string label;
        public readonly float min;
        public readonly float max;
        /// <summary>ModsParam で visible="false" の成分は UI に出さない (値は持ち、適用もする)</summary>
        public readonly bool visible;

        public BodySliderComponent(string name, string label, float min, float max, bool visible)
        {
            this.name = name;
            this.label = label;
            this.min = min;
            this.max = max;
            this.visible = visible;
        }
    }

    /// <summary>位置の差分の軸 1 つ分。source は値の添字 (負なら常に 0)、sign は符号</summary>
    public struct BodySliderAxis
    {
        public int source;
        public float sign;
    }

    /// <summary>項目が掛かる骨 1 本分。axes は位置の項目だけが持つ (x, y, z の 3 軸)</summary>
    public class BodySliderTarget
    {
        public readonly string boneName;
        public readonly BodySliderAxis[] axes;

        public BodySliderTarget(string boneName, BodySliderAxis[] axes)
        {
            this.boneName = boneName;
            this.axes = axes;
        }
    }

    /// <summary>
    /// 体型スライダーの項目 1 件分。値は ModsParam のサブキーの並びで 3 個持ち、
    /// 骨の軸への変換は適用時に行う (UI とデータの数値を ModsSlider とそろえるため)
    /// </summary>
    public class BodySliderItem
    {
        public readonly string key;
        public readonly string displayName;
        public readonly string group;
        public readonly BodySliderKind kind;
        public readonly BodySliderComponent[] components;
        /// <summary>位置の差分を割る係数。スケールでは使わない</summary>
        public readonly float positionDivisor;
        public readonly List<BodySliderTarget> targets;

        public BodySliderItem(
            string key, string displayName, string group, BodySliderKind kind,
            BodySliderComponent[] components, float positionDivisor, List<BodySliderTarget> targets)
        {
            this.key = key;
            this.displayName = displayName;
            this.group = group;
            this.kind = kind;
            this.components = components;
            this.positionDivisor = positionDivisor;
            this.targets = targets;
        }

        public float defaultValue => kind == BodySliderKind.Scale ? 1f : 0f;

        public Vector3 defaultValues => new Vector3(defaultValue, defaultValue, defaultValue);

        /// <summary>成分ごとに範囲へ丸める。NaN (手で書き換えたデータなど) は既定値にする</summary>
        public Vector3 Clamp(Vector3 values)
        {
            var result = values;
            for (var i = 0; i < 3; i++)
            {
                var value = values[i];
                result[i] = float.IsNaN(value)
                    ? defaultValue
                    : Mathf.Clamp(value, components[i].min, components[i].max);
            }
            return result;
        }

        /// <summary>丸めた値が既定値か。状態を持つか・キーを適用するかの判定をそろえる</summary>
        public bool IsDefault(Vector3 values)
        {
            var clamped = Clamp(values);
            for (var i = 0; i < 3; i++)
            {
                if (!Mathf.Approximately(clamped[i], defaultValue))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>(width, depth, height) を localScale の倍率 (height, depth, width) へ変える。MVP の SetBoneScale と同じ割り当て</summary>
        public Vector3 ToScale(Vector3 values)
        {
            return new Vector3(values.z, values.y, values.x);
        }

        /// <summary>(x, y, z) を骨の localPosition の差分へ変える。軸の入れ替えと符号は骨ごとの定義に従う</summary>
        public Vector3 ToOffset(Vector3 values, BodySliderTarget target)
        {
            var result = Vector3.zero;
            for (var i = 0; i < 3; i++)
            {
                var axis = target.axes[i];
                if (axis.source < 0)
                {
                    continue;
                }
                result[i] = axis.sign * values[axis.source] / positionDivisor;
            }
            return result;
        }
    }

    /// <summary>
    /// 体型スライダーの全項目。骨・軸・係数は MaidVoicePitch v0.2.17.6 の WideSlider に合わせる。
    /// 並びは UI・タイムラインのメニュー・キーの並びを兼ねる
    /// </summary>
    public static class BodySliderDefs
    {
        public const string GroupLeg = "脚";
        public const string GroupHip = "腰";
        public const string GroupBody = "胴・首";
        public const string GroupMune = "胸";
        public const string GroupArm = "肩・腕";

        public static readonly string[] groupNames = { GroupLeg, GroupHip, GroupBody, GroupMune, GroupArm };

        private const float LegScaleMax = 2f;
        private const float BodyScaleMax = 3f;
        private const float ScaleMin = 0.1f;
        private const float HipPositionMin = -100f;
        private const float HipPositionMax = 200f;
        private const float PositionMin = -1f;
        private const float PositionMax = 1f;
        private const float HipPositionDivisor = 1000f;
        private const float PositionDivisor = 10f;

        /// <summary>旧メイドスケール (腕 6 本の均一倍率) の骨名 → 腕の左右別項目。タイムラインとプリセットの移行で使う</summary>
        public static readonly Dictionary<string, string> legacyMaidScaleKeys = new Dictionary<string, string>
        {
            { "Bip01 L UpperArm", "UPARMSCL_L" },
            { "Bip01 L Forearm", "FARMSCL_L" },
            { "Bip01 L Hand", "HANDSCL_L" },
            { "Bip01 R UpperArm", "UPARMSCL_R" },
            { "Bip01 R Forearm", "FARMSCL_R" },
            { "Bip01 R Hand", "HANDSCL_R" },
        };

        public static readonly List<BodySliderItem> items = BuildItems();

        private static Dictionary<string, BodySliderItem> _itemMap;
        private static HashSet<string> _targetBones;

        public static BodySliderItem Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (_itemMap == null)
            {
                _itemMap = new Dictionary<string, BodySliderItem>();
                foreach (var item in items)
                {
                    _itemMap[item.key] = item;
                }
            }
            BodySliderItem result;
            return _itemMap.TryGetValue(key, out result) ? result : null;
        }

        /// <summary>いずれかの項目が掛かる骨か。複製骨のキャッシュを作るときに使う</summary>
        public static bool IsTargetBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            if (_targetBones == null)
            {
                _targetBones = new HashSet<string>();
                foreach (var item in items)
                {
                    foreach (var target in item.targets)
                    {
                        _targetBones.Add(target.boneName);
                    }
                }
            }
            return _targetBones.Contains(boneName);
        }

        public static IEnumerable<BodySliderItem> ItemsInGroup(string group)
        {
            foreach (var item in items)
            {
                if (item.group == group)
                {
                    yield return item;
                }
            }
        }

        private static List<BodySliderItem> BuildItems()
        {
            return new List<BodySliderItem>
            {
                // 脚
                Scale("THISCL", "足全体のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Thigh", "Bip01 R Thigh"),
                Scale("THISCL2", "膝のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Thigh_SCL_", "Bip01 R Thigh_SCL_"),
                Position("THIPOS", "足の位置", GroupLeg, HipPositionComponents(hiddenY: true), HipPositionDivisor,
                    Target("Bip01 L Thigh", "0", "+Z", "-X"), Target("Bip01 R Thigh", "0", "+Z", "+X"),
                    Target("Hip_L", "0", "+Z", "-X"), Target("Hip_R", "0", "+Z", "+X")),
                Position("THI2POS", "膝の位置", GroupLeg, HipPositionComponents(hiddenY: false), HipPositionDivisor,
                    Target("Bip01 L Thigh_SCL_", "+Y", "+Z", "-X"), Target("Bip01 R Thigh_SCL_", "+Y", "+Z", "+X")),
                Scale("MTWSCL", "ももねじれ肉のスケーリング", GroupLeg, LegScaleMax, false, "momotwist_L", "momotwist_R"),
                Position("MTWPOS", "ももねじれ肉の位置", GroupLeg, Positions("高さ", "前後", "左右", -1), PositionDivisor,
                    Target("momotwist_L", "+X", "+Y", "+Z"), Target("momotwist_R", "+X", "+Y", "-Z")),
                Scale("MMNSCL", "もも肉のスケーリング", GroupLeg, LegScaleMax, false, "momoniku_L", "momoniku_R"),
                Position("MMNPOS", "もも肉の位置", GroupLeg, Positions("高さ", "前後", "左右", -1), PositionDivisor,
                    Target("momoniku_L", "+X", "+Y", "+Z"), Target("momoniku_R", "+X", "-Y", "+Z")),
                Scale("CALFSCL", "膝下のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Calf", "Bip01 R Calf"),
                Scale("FOOTSCL", "足首より下のスケーリング", GroupLeg, LegScaleMax, false, "Bip01 L Foot", "Bip01 R Foot"),

                // 腰
                Scale("PELSCL", "骨盤スケーリング", GroupHip, LegScaleMax, false, "Bip01 Pelvis_SCL_", "Hip_L", "Hip_R"),
                Scale("HIPSCL", "尻スケーリング", GroupHip, LegScaleMax, false, "Hip_L", "Hip_R"),
                Position("HIPPOS", "尻の位置", GroupHip, HipPositionComponents(hiddenY: false), HipPositionDivisor,
                    Target("Hip_L", "+Y", "+Z", "-X"), Target("Hip_R", "+Y", "+Z", "+X")),
                Scale("SKTSCL", "スカート周辺スケーリング", GroupHip, BodyScaleMax, false, "Skirt"),
                Position("SKTPOS", "スカート位置", GroupHip,
                    new[]
                    {
                        new BodySliderComponent("x", "左右", PositionMin, PositionMax, false),
                        new BodySliderComponent("y", "前後", PositionMin, PositionMax, false),
                        new BodySliderComponent("z", "高さ", PositionMin, PositionMax, true),
                    },
                    PositionDivisor,
                    Target("Skirt", "-Z", "-Y", "+X")),

                // 胴・首
                Scale("SPISCL", "胴(下腹部)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine_SCL_"),
                Position("SPIPOS", "胴(下腹部)ポジション", GroupBody,
                    new[]
                    {
                        new BodySliderComponent("x", "前後", PositionMin, PositionMax, true),
                        new BodySliderComponent("y", "高さ", PositionMin, PositionMax, false),
                        new BodySliderComponent("z", "上下", PositionMin, PositionMax, true),
                    },
                    PositionDivisor,
                    Target("Bip01 Spine", "-X", "+Y", "+Z")),
                Scale("S0ASCL", "胴(腹部)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine0a_SCL_"),
                Position("S0APOS", "胴(腹部)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine0a", "-X", "+Y", "+Z")),
                Scale("S1_SCL", "胴(みぞおち)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine1_SCL_"),
                Position("S1POS", "胴(みぞおち)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine1", "-X", "+Y", "+Z")),
                Scale("S1ASCL", "胴(肋骨)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine1a_SCL_"),
                Position("S1APOS", "胴(肋骨)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine1a", "-X", "+Y", "+Z")),
                Scale("NECKSCL", "首スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Neck_SCL_"),
                Position("NECKPOS", "首ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Neck", "-X", "+Y", "+Z")),

                // 胸
                Item("MUNESCL", "胸スケーリング", GroupMune, BodySliderKind.Scale,
                    Scales("横幅", "縦幅", "奥行", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Mune_L"), Target("Mune_R")),
                Position("MUNEPOS", "胸ポジション", GroupMune, Positions("左右", "前後", "高さ", -1), PositionDivisor,
                    Target("Mune_L", "+Z", "-Y", "+X"), Target("Mune_R", "+Z", "-Y", "-X")),
                Item("MUNESUBSCL", "胸サブスケーリング", GroupMune, BodySliderKind.Scale,
                    Scales("横幅", "縦幅", "奥行", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Mune_L_sub"), Target("Mune_R_sub")),
                Position("MUNESUBPOS", "胸サブポジション", GroupMune, Positions("左右", "前後", "高さ", -1), PositionDivisor,
                    Target("Mune_L_sub", "-Y", "+Z", "-X"), Target("Mune_R_sub", "-Y", "-Z", "-X")),

                // 肩・腕
                // ModsParam のラベルは width=高さ / height=横幅 と逆に付いているが、ModsSlider と同じ表示にそろえる
                Item("CLVSCL", "鎖骨スケーリング", GroupArm, BodySliderKind.Scale,
                    Scales("高さ", "奥行", "横幅", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Bip01 L Clavicle"), Target("Bip01 R Clavicle")),
                Position("CLVPOS", "鎖骨の位置", GroupArm, Positions("上下", "前後", "左右", -1), PositionDivisor,
                    Target("Bip01 L Clavicle", "-X", "+Y", "+Z"), Target("Bip01 R Clavicle", "-X", "+Y", "-Z")),
                Scale("KATASCL", "肩スケーリング", GroupArm, BodyScaleMax, false, "Kata_L", "Kata_R"),
                // 腕は ModsParam では左右共通だが、SE では左右別に調整できるようにする
                Scale("UPARMSCL_L", "上腕スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L UpperArm"),
                Scale("UPARMSCL_R", "上腕スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R UpperArm"),
                Scale("FARMSCL_L", "前腕スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L Forearm"),
                Scale("FARMSCL_R", "前腕スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R Forearm"),
                Scale("HANDSCL_L", "手スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L Hand"),
                Scale("HANDSCL_R", "手スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R Hand"),
            };
        }

        private static BodySliderItem Item(
            string key, string displayName, string group, BodySliderKind kind,
            BodySliderComponent[] components, float positionDivisor, params BodySliderTarget[] targets)
        {
            return new BodySliderItem(key, displayName, group, kind, components, positionDivisor,
                new List<BodySliderTarget>(targets));
        }

        private static BodySliderItem Scale(
            string key, string displayName, string group, float max, bool hiddenHeight, params string[] boneNames)
        {
            var targets = new BodySliderTarget[boneNames.Length];
            for (var i = 0; i < boneNames.Length; i++)
            {
                targets[i] = Target(boneNames[i]);
            }
            return Item(key, displayName, group, BodySliderKind.Scale,
                Scales("横幅", "奥行", "高さ", max, hiddenHeight), 1f, targets);
        }

        private static BodySliderItem Position(
            string key, string displayName, string group, BodySliderComponent[] components,
            float divisor, params BodySliderTarget[] targets)
        {
            return Item(key, displayName, group, BodySliderKind.Position, components, divisor, targets);
        }

        private static BodySliderComponent[] Scales(
            string widthLabel, string depthLabel, string heightLabel, float max, bool hiddenHeight)
        {
            return new[]
            {
                new BodySliderComponent("width", widthLabel, ScaleMin, max, true),
                new BodySliderComponent("depth", depthLabel, ScaleMin, max, true),
                new BodySliderComponent("height", heightLabel, ScaleMin, max, !hiddenHeight),
            };
        }

        /// <param name="hiddenIndex">ModsParam で非表示の成分の添字。無ければ -1</param>
        private static BodySliderComponent[] Positions(string xLabel, string yLabel, string zLabel, int hiddenIndex)
        {
            return new[]
            {
                new BodySliderComponent("x", xLabel, PositionMin, PositionMax, hiddenIndex != 0),
                new BodySliderComponent("y", yLabel, PositionMin, PositionMax, hiddenIndex != 1),
                new BodySliderComponent("z", zLabel, PositionMin, PositionMax, hiddenIndex != 2),
            };
        }

        /// <summary>足・膝・尻の位置 (THIPOS / THI2POS / HIPPOS) の成分。範囲が広く /1000 で効く</summary>
        private static BodySliderComponent[] HipPositionComponents(bool hiddenY)
        {
            return new[]
            {
                new BodySliderComponent("x", "左右", HipPositionMin, HipPositionMax, true),
                new BodySliderComponent("y", "高さ", HipPositionMin, HipPositionMax, !hiddenY),
                new BodySliderComponent("z", "前後", HipPositionMin, HipPositionMax, true),
            };
        }

        private static BodySliderTarget Target(string boneName)
        {
            return new BodySliderTarget(boneName, null);
        }

        /// <summary>差分の x, y, z 軸を "+X" / "-Z" / "0" の記法で受け取る (MVP の式をそのまま写すため)</summary>
        private static BodySliderTarget Target(string boneName, string x, string y, string z)
        {
            return new BodySliderTarget(boneName, new[] { ParseAxis(x), ParseAxis(y), ParseAxis(z) });
        }

        private static BodySliderAxis ParseAxis(string spec)
        {
            if (spec == "0")
            {
                return new BodySliderAxis { source = -1, sign = 0f };
            }
            return new BodySliderAxis
            {
                source = spec[1] - 'X',
                sign = spec[0] == '-' ? -1f : 1f,
            };
        }
    }
}
```

- [ ] **Step 5: `BodySliderState.cs` を書く**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderState.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>骨 1 本に掛ける量。複数の項目が同じ骨に掛かる場合は合成済み</summary>
    public struct BodySliderBoneOp
    {
        public Vector3 scale;
        public Vector3 offset;
        public bool hasScale;
        public bool hasOffset;
    }

    /// <summary>
    /// メイド 1 体分の体型スライダーの値。未設定の項目は既定値 (スケール 1、位置 0)。
    /// 既定値は保持しないので、全項目が既定なら isDefault になる
    /// </summary>
    public class BodySliderState
    {
        private readonly Dictionary<string, Vector3> _values = new Dictionary<string, Vector3>();

        public bool isDefault => _values.Count == 0;

        /// <summary>既定値でない項目だけ</summary>
        public IEnumerable<KeyValuePair<string, Vector3>> nonDefaultValues => _values;

        public Vector3 Get(string key)
        {
            var item = BodySliderDefs.Find(key);
            if (item == null)
            {
                return Vector3.zero;
            }
            Vector3 values;
            return _values.TryGetValue(key, out values) ? values : item.defaultValues;
        }

        /// <summary>範囲へ丸めて保持する。定義に無いキーは無視する</summary>
        public void Set(string key, Vector3 values)
        {
            var item = BodySliderDefs.Find(key);
            if (item == null)
            {
                return;
            }
            if (item.IsDefault(values))
            {
                _values.Remove(key);
                return;
            }
            _values[key] = item.Clamp(values);
        }

        public void Clear()
        {
            _values.Clear();
        }

        /// <summary>
        /// 骨ごとに掛ける量を求める。1 本の骨に複数の項目が掛かる場合 (Hip_? など) は、
        /// MVP と同じくスケールを成分ごとの積、位置を和で合成する
        /// </summary>
        public void BuildBoneOps(Dictionary<string, BodySliderBoneOp> result)
        {
            result.Clear();
            foreach (var pair in _values)
            {
                var item = BodySliderDefs.Find(pair.Key);
                if (item == null)
                {
                    continue;
                }
                foreach (var target in item.targets)
                {
                    BodySliderBoneOp op;
                    if (!result.TryGetValue(target.boneName, out op))
                    {
                        op = new BodySliderBoneOp { scale = Vector3.one, offset = Vector3.zero };
                    }

                    if (item.kind == BodySliderKind.Scale)
                    {
                        op.scale = Vector3.Scale(op.scale, item.ToScale(pair.Value));
                        op.hasScale = true;
                    }
                    else
                    {
                        op.offset += item.ToOffset(pair.Value, target);
                        op.hasOffset = true;
                    }
                    result[target.boneName] = op;
                }
            }
        }

        /// <summary>
        /// 退避値へ戻してよいか。今の値が自分の書いた値のままのときだけ戻す。
        /// 体型スライダー (BoneMorph_.Blend) や CopyTrans などが途中で書き直した値を古い退避値で潰さないため。
        /// Vector3 の == は近似比較 (差の 2 乗が 1e-10 未満) なので、それより小さな書き直しは区別しない
        /// </summary>
        public static bool ShouldRestore(Vector3 current, Vector3 written)
        {
            return current == written;
        }
    }
}
```

csproj（`MaidManipulation\MaidScaleState.cs` の行の直後）:

```xml
    <Compile Include="MaidManipulation\BodySliderDefs.cs" />
    <Compile Include="MaidManipulation\BodySliderState.cs" />
```

- [ ] **Step 6: テストが通ることを確かめる**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySlider` で実行する。
Expected: 両ビルド成功、BodySliderDefsTests / BodySliderStateTests 全件 PASS

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderDefs.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderState.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/BodySliderDefsTests.cs source/COM3D2.SceneEditor.Plugin.Tests/BodySliderStateTests.cs
git commit -m "feat(body-slider): 体型スライダーの項目定義と骨ごとの合成を追加する"
```

---

### Task 2: 適用器（`BodySliderController` / `BodySliderLateUpdatePatch`）

このタスクでは MaidScale の適用器と並べて置く。MaidScale の削除は Task 5 で行う。両方とも状態が無ければ何もしないので、通常は干渉しない。ただし同じ腕の骨に両方の値を入れると、Harmony の postfix の順によっては戻しの一致判定が外れ、戻りきらないことがある。Task 2〜4 の途中で実機を触るときは、メイドスケールと体型の腕を同時に使わない（Task 5 で MaidScale が消えれば解消する）。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BodySliderController.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/BodySliderLateUpdatePatch.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs:230`（フィールド追加）、`:692`（Release）、`:739`（Destroy）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:134`（Update の戻し）、`:439`（パッチ初期化）、`:528-537`（OnGUI の一時解除）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScreenshotManager.cs:116,142`、`source/COM3D2.SceneEditor.Plugin/Manager/ThumbnailCapture.cs:69,83`
- Modify: csproj（`MaidManipulation\BodySliderState.cs` の直後と `Manager\MaidScaleLateUpdatePatch.cs` の直後）

**Interfaces:**
- Consumes: `BodySliderDefs.Find / IsTargetBone`、`BodySliderState`（Task 1）
- Produces（`BodySliderController`）:
  - `bool HasState(Maid maid)`
  - `Vector3 GetValues(Maid maid, string key)`
  - `void SetValues(Maid maid, string key, Vector3 values)`
  - `void ResetAll(Maid maid)`
  - `List<KeyValuePair<string, Vector3>> GetNonDefaultValues(Maid maid)`（コピーを返す）
  - `void RestoreApplied()` / `SuspendApplied()` / `ResumeApplied()` / `BeginCapture()` / `EndCapture()` / `Release(Maid)` / `Destroy()`
  - `void OnBodyLateUpdateBegin(TBody body)` / `void OnBodyLateUpdateEnd(TBody body)`
- Produces: `MaidManipulateManager.instance.bodySliderController`

- [ ] **Step 1: `BodySliderController.cs` を書く**

`MaidScaleController` を下敷きにする。変更点は 3 つ。

1. 状態を `BodySliderState` にする。値が変わるたびに骨ごとの量（`ops`）を作り直す。
2. スケールに加えて位置も書き、戻す。
3. 複製骨の収集は `BodySliderDefs.IsTargetBone` で判定する。

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 体型スライダーの状態を持ち、スキニングへ反映する。
    /// メッシュは TBodySkin ごとの複製骨にスキニングされ、本体の骨のスケールはコピーされないため、
    /// 全スロットの複製骨 (SkinnedMeshRenderer.bones とその祖先) へ直接掛ける。
    /// 掛けるのは TBody.LateUpdate の直後 (BodySliderLateUpdatePatch)。
    /// それより前だと MOD (MaidVoicePitch の ForeArmFix など) が書き戻し、
    /// 描画直前 (Camera.onPreCull) ではスキニングの計算に間に合わない。
    /// スケールは今の値への乗算、位置は今の値への加算なので、MaidVoicePitch の体型スライダーの結果に重なる。
    /// 次フレームの Update と TBody.LateUpdate の直前で元へ戻す。
    /// GUI を描く間だけは外して掛け直す (SuspendApplied / ResumeApplied)。
    /// 本体の骨には触らないので、IK・ハンドル・アタッチ位置は影響を受けない
    /// </summary>
    public class BodySliderController
    {
        /// <summary>1 回分の書き込み記録。戻すときに使う。書いた成分だけ has* が立つ</summary>
        private struct AppliedBone
        {
            public Transform bone;
            public bool hasScale;
            public Vector3 originalScale;
            public Vector3 writtenScale;
            public bool hasPosition;
            public Vector3 originalPosition;
            public Vector3 writtenPosition;
        }

        /// <summary>メイド 1 体分の状態と複製骨のキャッシュ</summary>
        private class Entry
        {
            public readonly BodySliderState state = new BodySliderState();

            /// <summary>骨名 → 掛ける量。isOpsDirty のとき、掛ける直前に作り直す</summary>
            public readonly Dictionary<string, BodySliderBoneOp> ops = new Dictionary<string, BodySliderBoneOp>();

            /// <summary>
            /// state を変えてから ops を作り直していない。再生中は項目ごとに毎フレーム SetValues が来るので、
            /// 作り直しは掛ける直前の 1 回にまとめる
            /// </summary>
            public bool isOpsDirty;

            /// <summary>骨名 → 全スロットの複製骨</summary>
            public readonly Dictionary<string, List<Transform>> bones
                = new Dictionary<string, List<Transform>>();

            /// <summary>キャッシュを作った時点のスロット obj の並び。変わったら作り直す</summary>
            public readonly List<GameObject> slotObjects = new List<GameObject>();

            public bool isCacheValid;

            /// <summary>前回掛けた分。次に掛ける前か Update で戻す</summary>
            public readonly List<AppliedBone> applied = new List<AppliedBone>();

            /// <summary>GUI の間だけ外している。ResumeApplied で掛け直す</summary>
            public bool isSuspended;
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>キャッシュの検証で毎回作らないよう使い回す</summary>
        private readonly List<GameObject> _slotObjectBuffer = new List<GameObject>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        /// <summary>SuspendApplied から ResumeApplied までの間 (GUI の描画中) か</summary>
        private bool _isGuiSuspended;

        /// <summary>GUI の中の撮影のために BeginCapture で掛け直したか (EndCapture の外し直し判定)</summary>
        private bool _isResumedForCapture;

        public bool HasState(Maid maid)
        {
            return maid != null && _entries.ContainsKey(maid);
        }

        /// <summary>記録が無いメイドは既定値として扱う</summary>
        public Vector3 GetValues(Maid maid, string key)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                var item = BodySliderDefs.Find(key);
                return item != null ? item.defaultValues : Vector3.zero;
            }
            return entry.state.Get(key);
        }

        public void SetValues(Maid maid, string key, Vector3 values)
        {
            var item = BodySliderDefs.Find(key);
            if (maid == null || item == null)
            {
                return;
            }

            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                // 既定値を書くだけなら状態を作らない (常駐コストを増やさない)
                if (item.IsDefault(values))
                {
                    return;
                }
                entry = new Entry();
                _entries[maid] = entry;
            }

            entry.state.Set(key, values);
            entry.isOpsDirty = true;

            // 全項目が既定値に戻ったら状態ごと捨てる (HasState を「変更中か」に揃える)
            if (entry.state.isDefault)
            {
                Restore(entry);
                _entries.Remove(maid);
            }
        }

        /// <summary>全項目を既定値へ戻す</summary>
        public void ResetAll(Maid maid)
        {
            Release(maid);
        }

        /// <summary>既定値でない項目の一覧 (プリセットとスナップショット用のコピー)</summary>
        public List<KeyValuePair<string, Vector3>> GetNonDefaultValues(Maid maid)
        {
            var result = new List<KeyValuePair<string, Vector3>>();
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                result.AddRange(entry.state.nonDefaultValues);
            }
            return result;
        }

        // RestoreApplied / SuspendApplied / ResumeApplied / BeginCapture / EndCapture /
        // SuspendEntries / ResumeEntries / Release / Destroy / OnBodyLateUpdateBegin /
        // FindEntry / RefreshBonesIfNeeded / CollectSlotObjects / IsSameSlotObjects / AddBone は
        // MaidScaleController.cs の同名メソッドを、コメントの「メイドスケール」「倍率」を「体型スライダー」「体型」へ
        // 読み替えてそのまま写す。ResumeEntries と OnBodyLateUpdateEnd の ApplyScales 呼び出しは Apply に変える

        /// <summary>キャッシュ済みの複製骨へ、今の値 × 倍率 / 今の値 + 差分を書き、戻すための記録を残す</summary>
        private static void Apply(Entry entry)
        {
            if (entry.isOpsDirty)
            {
                entry.state.BuildBoneOps(entry.ops);
                entry.isOpsDirty = false;
            }

            foreach (var opPair in entry.ops)
            {
                List<Transform> bones;
                if (!entry.bones.TryGetValue(opPair.Key, out bones))
                {
                    continue;
                }
                var op = opPair.Value;
                foreach (var bone in bones)
                {
                    if (bone == null)
                    {
                        continue;
                    }
                    var record = new AppliedBone { bone = bone };
                    if (op.hasScale)
                    {
                        record.hasScale = true;
                        record.originalScale = bone.localScale;
                        record.writtenScale = Vector3.Scale(record.originalScale, op.scale);
                        bone.localScale = record.writtenScale;
                    }
                    if (op.hasOffset)
                    {
                        record.hasPosition = true;
                        record.originalPosition = bone.localPosition;
                        record.writtenPosition = record.originalPosition + op.offset;
                        bone.localPosition = record.writtenPosition;
                    }
                    entry.applied.Add(record);
                }
            }
        }

        /// <summary>自分が書いた値のまま残っている成分だけを戻し、破棄済みの骨は飛ばす</summary>
        private static void Restore(Entry entry)
        {
            var applied = entry.applied;
            for (var i = 0; i < applied.Count; i++)
            {
                var record = applied[i];
                if (record.bone == null)
                {
                    continue;
                }
                if (record.hasScale
                    && BodySliderState.ShouldRestore(record.bone.localScale, record.writtenScale))
                {
                    record.bone.localScale = record.originalScale;
                }
                if (record.hasPosition
                    && BodySliderState.ShouldRestore(record.bone.localPosition, record.writtenPosition))
                {
                    record.bone.localPosition = record.originalPosition;
                }
            }
            applied.Clear();
        }

        /// <summary>
        /// 各スロットの SkinnedMeshRenderer が実際に参照している骨と、そのスロット obj までの祖先から対象骨を集める。
        /// CRC ボディは上腕・前腕を直接参照せず子のツイスト骨だけを参照し、
        /// 位置の項目の骨 (Bip01 Spine など) も多くは祖先にしか現れないため、祖先までたどる。
        /// 名前で子孫を探すと、別スロットの骨や持ち物の中の同名ノードを拾うおそれがある。
        /// スロット obj 配下にない骨 (本体の骨を直接参照している場合) は書かない
        /// </summary>
        private static void RebuildBones(Entry entry)
        {
            entry.bones.Clear();

            // 一度たどった骨から上は同じ経路になるので、ここで打ち切る
            var visited = new HashSet<Transform>();

            foreach (var slotObject in entry.slotObjects)
            {
                if (slotObject == null)
                {
                    continue;
                }
                var slotRoot = slotObject.transform;
                foreach (var renderer in slotObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = renderer.bones;
                    if (bones == null)
                    {
                        continue;
                    }
                    foreach (var bone in bones)
                    {
                        if (bone == null || !bone.IsChildOf(slotRoot))
                        {
                            continue;
                        }

                        for (var t = bone; t != null && t != slotRoot && visited.Add(t); t = t.parent)
                        {
                            if (BodySliderDefs.IsTargetBone(t.name))
                            {
                                AddBone(entry, t);
                            }
                        }
                    }
                }
            }
        }
    }
}
```

「そのまま写す」と書いたメソッドは、`MaidScaleController.cs:111-262` と `:293-301`、`:319-369`、`:418-427` にある。写すときの置き換えは次のとおり（ほかは変えない）。

| 元 | 置き換え |
|---|---|
| `ApplyScales(entry)`（`ResumeEntries` と `OnBodyLateUpdateEnd` の 2 か所） | `Apply(entry)` |
| コメントの「倍率を写す」「メイドスケール」 | 「体型スライダーを写す」「体型スライダー」 |
| `// Update が回らないフレームでも積み上げないよう、…` | そのまま |

- [ ] **Step 2: `BodySliderLateUpdatePatch.cs` を書く**

`Manager/MaidScaleLateUpdatePatch.cs` を写して、次の置き換えだけを行う。

| 元 | 置き換え |
|---|---|
| クラス名 `MaidScaleLateUpdatePatch` | `BodySliderLateUpdatePatch` |
| `PluginInfo.PluginFullName + ".MaidScale"` | `PluginInfo.PluginFullName + ".BodySlider"` |
| `MaidScaleController controller` / `manager.maidScaleController` | `BodySliderController controller` / `manager.bodySliderController` |
| ログ「メイドスケールは無効です」 | 「体型スライダーは無効です」 |
| summary「メイドスケールを TBody.LateUpdate の前後で戻す・掛ける。掛ける位置の理由は MaidScaleController 参照。」 | 「体型スライダーを TBody.LateUpdate の前後で戻す・掛ける。掛ける位置の理由は BodySliderController 参照。」 |

- [ ] **Step 3: 呼び出し箇所を足す**

MaidScale の呼び出しの隣に、体型スライダーの呼び出しを足す（MaidScale 側は Task 5 で消す）。

`MaidManipulateManager.cs:230` の直後:

```csharp
        /// <summary>体型スライダー。TBody.LateUpdate の直後に複製骨へ掛けるため常駐させる</summary>
        public BodySliderController bodySliderController = new BodySliderController();
```

`MaidManipulateManager.cs:692` の直後:

```csharp
            // 体型スライダーも持ち越さない（ストックの Maid は使い回される）
            bodySliderController.Release(maid);
```

`MaidManipulateManager.cs:739` の直後:

```csharp
            bodySliderController.Destroy();
```

`COM3D2.SceneEditor.Plugin.cs:134` の直後:

```csharp
                MaidManipulateManager.instance.bodySliderController.RestoreApplied();
```

`COM3D2.SceneEditor.Plugin.cs:439` の直後:

```csharp
                // 体型スライダーを TBody.LateUpdate の直後に掛ける。UI の有効状態に関係なく常時効かせる
                BodySliderLateUpdatePatch.Init();
```

`COM3D2.SceneEditor.Plugin.cs` の `OnGUI`（528〜537 行）を次に変える:

```csharp
                if (isEnable)
                {
                    var scaleController = MaidManipulateManager.instance.maidScaleController;
                    var bodySliderController = MaidManipulateManager.instance.bodySliderController;
                    scaleController.SuspendApplied();
                    bodySliderController.SuspendApplied();
                    try
                    {
                        windowManager.OnGUI();
                    }
                    finally
                    {
                        bodySliderController.ResumeApplied();
                        scaleController.ResumeApplied();
                    }
                }
```

`ScreenshotManager.cs:116` と `ThumbnailCapture.cs:69` の `maidScaleController.BeginCapture();` の直後に:

```csharp
                MaidManipulateManager.instance.bodySliderController.BeginCapture();
```

`ScreenshotManager.cs:142` と `ThumbnailCapture.cs:83` の `maidScaleController.EndCapture();` の直後に:

```csharp
                MaidManipulateManager.instance.bodySliderController.EndCapture();
```

csproj（`MaidManipulation\BodySliderState.cs` の直後と `Manager\MaidScaleLateUpdatePatch.cs` の直後）:

```xml
    <Compile Include="MaidManipulation\BodySliderController.cs" />
```

```xml
    <Compile Include="Manager\BodySliderLateUpdatePatch.cs" />
```

- [ ] **Step 4: ビルドとテスト**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySlider` で実行する。
Expected: 両ビルド成功、テスト全件 PASS（このタスクで増えるテストは無い。Unity の骨を使うため実機検証は Task 6 で行う）

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin
git commit -m "feat(body-slider): TBody.LateUpdate の直後に複製骨へ体型スライダーを掛ける適用器を追加する"
```

---

### Task 3: タイムライン・Inspector・Undo

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBodySlider.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BodySliderTimelineLayer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BodySliderItemInspector.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/BodySliderRowDrawer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/BodySliderSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:8-10`（enum に `BodySlider` を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs:53`（`BodySlider` を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/SnapshotFactory.cs:35`（case を追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs:326,370,508`（登録を追加）
- Modify: csproj（各ファイルを MaidScale の対応ファイルの直後に追加）
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs:21`、`source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerResetOnRemoveTests.cs:26`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderLayerTests.cs`

**Interfaces:**
- Consumes: `BodySliderDefs`、`BodySliderController`（Task 1・2）
- Produces:
  - `TransformType.BodySlider`
  - `TransformDataBodySlider`: `Vector3 vector { get; set; }`、`bool isDefault`、`ValueData[] values`（3 個）
  - `BodySliderTimelineLayer.BuildKeyNames(ICollection<string> nonDefaultKeys, ICollection<string> keyedNames) : List<string>`
  - `BodySliderItemInspector.ResolveItem(string itemName) : BodySliderItem`
  - `BodySliderRowDrawer.DrawComponents(GUIView view, Maid maid, BodySliderItem item, float labelWidth)`
  - `BodySliderRowDrawer.DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)`
  - `HistoryScope.BodySlider`、`BodySliderSnapshot.Capture(Maid maid)`

- [ ] **Step 1: テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderLayerTests.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーレイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class BodySliderLayerTests
    {
        private static MTEP.TransformDataBodySlider CreateKey(string key)
        {
            var trans = new MTEP.TransformDataBodySlider();
            trans.Initialize(key);
            return trans;
        }

        [Fact]
        public void キーは3値でタンジェント補間する()
        {
            var trans = CreateKey("THISCL");
            Assert.Equal(MTEP.TransformType.BodySlider, trans.type);
            Assert.Equal(3, trans.valueCount);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Fact]
        public void スケールの既定は1で位置の既定は0()
        {
            Assert.Equal(Vector3.one, CreateKey("THISCL").vector);
            Assert.True(CreateKey("THISCL").isDefault);
            Assert.Equal(Vector3.zero, CreateKey("THIPOS").vector);
        }

        [Fact]
        public void カスタム値は項目の成分名と範囲を持つ()
        {
            var map = CreateKey("THIPOS").GetCustomValueInfoMap();
            Assert.Equal(3, map.Count);
            Assert.Equal(0, map["x"].index);
            Assert.Equal(-100f, map["x"].min);
            Assert.Equal(200f, map["x"].max);
            Assert.Equal(0f, map["x"].defaultValue);
            Assert.Equal(2, CreateKey("THISCL").GetCustomValueInfoMap()["height"].index);
        }

        [Fact]
        public void 未知の項目名はカスタム値を持たない()
        {
            Assert.Empty(CreateKey("UNKNOWN").GetCustomValueInfoMap());
        }

        [Fact]
        public void リセットしたキーは項目の既定値に戻る()
        {
            var trans = CreateKey("UPARMSCL_L");
            trans.vector = new Vector3(2f, 2f, 2f);
            Assert.False(trans.isDefault);

            trans.Reset();
            Assert.Equal(Vector3.one, trans.vector);
        }

        [Fact]
        public void XMLの往復で値を保つ()
        {
            var trans = CreateKey("MUNEPOS");
            trans.vector = new Vector3(0.1f, -0.2f, 0.3f);

            var loaded = CreateKey("MUNEPOS");
            loaded.FromXml(trans.ToXml());

            Assert.Equal(new Vector3(0.1f, -0.2f, 0.3f), loaded.vector);
            Assert.Equal("MUNEPOS", loaded.name);
        }

        [Fact]
        public void キーは既定でない項目とキー済み項目だけを定義順で作る()
        {
            var names = MTEP.BodySliderTimelineLayer.BuildKeyNames(
                new List<string> { "HANDSCL_R", "THISCL" },
                new List<string> { "THIPOS" });

            Assert.Equal(new[] { "THISCL", "THIPOS", "HANDSCL_R" }, names.ToArray());
        }

        [Fact]
        public void 値もキーも無ければキーにしない()
        {
            Assert.Empty(MTEP.BodySliderTimelineLayer.BuildKeyNames(new List<string>(), new List<string>()));
        }

        [Fact]
        public void 履歴スコープはメイド必須()
        {
            Assert.True(HistoryScopeUtils.RequiresMaid(HistoryScope.BodySlider));
        }

        [Fact]
        public void Inspectorは項目名から定義を引き未知の名前ではnullを返す()
        {
            Assert.Equal("上腕スケーリング(左)", BodySliderItemInspector.ResolveItem("UPARMSCL_L").displayName);
            Assert.Null(BodySliderItemInspector.ResolveItem("unknown"));
        }
    }
}
```

`TimelineLayerCategoryTests.cs:21` の次の行に追加:

```csharp
            { "BodySliderTimelineLayer", TimelineLayerCategory.Maid },
```

`TimelineLayerResetOnRemoveTests.cs:26` の次の行に追加:

```csharp
            // 値はこのレイヤーと体型タブからしか編集できないため、断面の無い削除でも既定値へ戻す
            "BodySliderTimelineLayer",
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySlider|FullyQualifiedName~TimelineLayerCategoryTests|FullyQualifiedName~TimelineLayerResetOnRemoveTests` で実行する。
Expected: テストプロジェクトのコンパイルエラー（`TransformDataBodySlider` などが無い）

- [ ] **Step 3: enum・履歴スコープを足す**

`ITransformData.cs` の `TransformType` で、`BGModel,` の直後に追加（名前順）:

```csharp
        BodySlider,
```

`HistoryScope.cs:53`（`MaidScale,` の行）の直後に追加:

```csharp
        /// <summary>体型スライダー (ModsSlider 相当の骨のスケール・位置)</summary>
        BodySlider,
```

`SnapshotFactory.cs:36`（`return MaidScaleSnapshot.Capture(maid);`）の直後に追加:

```csharp
                case HistoryScope.BodySlider:
                    return BodySliderSnapshot.Capture(maid);
```

- [ ] **Step 4: `TransformDataBodySlider.cs` を書く**

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 体型スライダーキー 1 件分。項目名は BodySliderDefs のキーで、値は ModsParam のサブキーの並びの 3 個
    /// (スケールは width / depth / height、位置は x / y / z)。Tangent 補間する。
    /// 既定値と成分の範囲は項目ごとに違うため、カスタム値の定義は項目名から引く
    /// </summary>
    public class TransformDataBodySlider : TransformDataBase
    {
        public override TransformType type => TransformType.BodySlider;

        public override int valueCount => 3;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        private static readonly Dictionary<string, CustomValueInfo> EmptyInfoMap
            = new Dictionary<string, CustomValueInfo>();

        /// <summary>項目キー → カスタム値の定義。定義は不変なので作ったものを使い回す</summary>
        private static readonly Dictionary<string, Dictionary<string, CustomValueInfo>> InfoMapCache
            = new Dictionary<string, Dictionary<string, CustomValueInfo>>();

        public TransformDataBodySlider()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るだけなので、項目の既定値 (スケールは 1) にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            var item = BodySliderDefs.Find(name);
            if (item != null)
            {
                vector = item.defaultValues;
            }
        }

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            var item = BodySliderDefs.Find(name);
            if (item == null)
            {
                return EmptyInfoMap;
            }

            Dictionary<string, CustomValueInfo> map;
            if (!InfoMapCache.TryGetValue(item.key, out map))
            {
                map = new Dictionary<string, CustomValueInfo>();
                for (var i = 0; i < item.components.Length; i++)
                {
                    var component = item.components[i];
                    map[component.name] = new CustomValueInfo
                    {
                        index = i,
                        name = component.label,
                        min = component.min,
                        max = component.max,
                        step = 0.01f,
                        defaultValue = item.defaultValue,
                    };
                }
                InfoMapCache[item.key] = map;
            }
            return map;
        }

        public Vector3 vector
        {
            get => new Vector3(values[0].value, values[1].value, values[2].value);
            set
            {
                values[0].value = value.x;
                values[1].value = value.y;
                values[2].value = value.z;
            }
        }

        /// <summary>既定値か。適用を省く判定に使う。未知の項目は適用しないので既定扱い</summary>
        public bool isDefault
        {
            get
            {
                var item = BodySliderDefs.Find(name);
                return item == null || item.IsDefault(vector);
            }
        }
    }
}
```

基底の `Reset()`（`TransformDataBase.cs:1497` 付近）は、カスタム値を `GetCustomValueInfoMap()` の既定値へ戻す。この map は項目名から引くので、テストの「リセットしたキーは項目の既定値に戻る」は項目ごとの既定値になる。

- [ ] **Step 5: `BodySliderTimelineLayer.cs` を書く**

```csharp
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 体型スライダー (BodySliderController) をキー化するレイヤー。
    /// 項目名は BodySliderDefs のキーで、値 3 個を Tangent 補間する。
    /// キーにするのは既定値でない項目と、このレイヤーに既にキーのある項目だけ
    /// (全 38 項目を毎回キーにすると XML とキー一覧が膨らむため)。
    /// 骨への書き込みはコントローラーが TBody.LateUpdate の直後に行い、このレイヤーは値を渡すだけ
    /// </summary>
    [TimelineLayerDesc("体型", 19, TimelineLayerCategory.Maid)]
    public class BodySliderTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BodySliderTimelineLayer);
        public override string layerName => nameof(BodySliderTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var item in BodySliderDefs.items)
                    {
                        _allBoneNames.Add(item.key);
                    }
                }
                return _allBoneNames;
            }
        }

        private static BodySliderController bodySliderController
            => MaidManipulateManager.instance.bodySliderController;

        private BodySliderTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static BodySliderTimelineLayer Create(int slotNo)
        {
            return new BodySliderTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var item in BodySliderDefs.items)
            {
                _allMenuItems.Add(new BoneMenuItem(item.key, item.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
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
        /// キーにする項目名。既定値でない項目と、レイヤーに既にキーのある項目。並びは定義順。
        /// 後者が無いと、途中で既定値へ戻した項目のキーが作られず、前のキーの値が続く
        /// </summary>
        public static List<string> BuildKeyNames(ICollection<string> nonDefaultKeys, ICollection<string> keyedNames)
        {
            var result = new List<string>();
            foreach (var item in BodySliderDefs.items)
            {
                if (nonDefaultKeys.Contains(item.key) || keyedNames.Contains(item.key))
                {
                    result.Add(item.key);
                }
            }
            return result;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || BodySliderDefs.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataBodySlider;
            var end = motion.end as TransformDataBodySlider;
            if (start == null || end == null)
            {
                return;
            }

            // 一度も変えていないメイドへ既定値を書いても何も変わらない
            if (!bodySliderController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            var values = Vector3.zero;
            for (var i = 0; i < 3; i++)
            {
                values[i] = PluginUtils.HermiteValue(t0, t1, start.values[i], end.values[i], t);
            }
            bodySliderController.SetValues(maid, motion.name, values);
        }

        /// <summary>
        /// 値はこのレイヤーと体型タブからしか編集できないので、削除・アンロード時は既定値へ戻す。
        /// 誕生時の断面がある場合は、この後の断面の復元が上書きする
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            bodySliderController.ResetAll(maid);
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

            var nonDefaultKeys = new HashSet<string>();
            foreach (var pair in bodySliderController.GetNonDefaultValues(maid))
            {
                nonDefaultKeys.Add(pair.Key);
            }

            foreach (var key in BuildKeyNames(nonDefaultKeys, _playDataMap.Keys))
            {
                var trans = CreateTransformData<TransformDataBodySlider>(key);
                trans.vector = bodySliderController.GetValues(maid, key);

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
            return TransformType.BodySlider;
        }
    }
}
```

`TimelineLayerDesc` の並び順 19 は、MaidScale と同じ値にする。MaidScale は Task 5 で消すので重複は一時的。

- [ ] **Step 6: `BodySliderRowDrawer.cs` を書く**

```csharp
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 体型スライダーの項目 1 件分のスライダー (表示する成分だけ) と、全項目を既定値へ戻すボタン。
    /// ボーンウィンドウの「体型」タブと TimelineItemInspector (体型レイヤーの項目表示) で共有する。
    /// 値の読み書きは BodySliderController を通し、操作は履歴に記録する
    /// </summary>
    public static class BodySliderRowDrawer
    {
        private const string ResetAllLabel = "すべて既定に戻す";

        private static BodySliderController controller
            => MaidManipulateManager.instance.bodySliderController;

        public static void DrawComponents(GUIView view, Maid maid, BodySliderItem item, float labelWidth)
        {
            var values = controller.GetValues(maid, item.key);
            for (var i = 0; i < item.components.Length; i++)
            {
                var component = item.components[i];
                if (!component.visible)
                {
                    continue;
                }

                var index = i;
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = component.label,
                    labelWidth = labelWidth,
                    width = -1,
                    min = component.min,
                    max = component.max,
                    defaultValue = item.defaultValue,
                    value = values[index],
                    onChanged = newValue =>
                    {
                        RecordEdit(maid, item.displayName);
                        // ドラッグ中に他の成分が変わっていることはないが、書き込み直前の値から作り直す
                        var current = controller.GetValues(maid, item.key);
                        current[index] = newValue;
                        controller.SetValues(maid, item.key, current);
                    },
                });
            }
        }

        /// <summary>
        /// 全項目を既定値へ戻す。履歴は 1 件にまとめる。
        /// 変更していないメイドでは無変更の履歴を積まないよう押せなくする
        /// </summary>
        public static void DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)
        {
            if (!view.DrawButton(ResetAllLabel, width, rowHeight, controller.HasState(maid)))
            {
                return;
            }

            RecordEdit(maid, ResetAllLabel);
            controller.ResetAll(maid);
        }

        /// <summary>体型スライダーの操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid maid, string label)
        {
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.BodySlider, "体型: " + label);
        }
    }
}
```

- [ ] **Step 7: `BodySliderItemInspector.cs` を書く**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 体型レイヤーのメニュー項目 → 項目のスライダー。
    /// 行の描画と履歴への記録は BodySliderRowDrawer に任せる
    /// </summary>
    public class BodySliderItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        private const float ComponentLabelWidth = 40f;

        /// <summary>メニュー項目名 (項目キー) から定義を求める。対象外なら null</summary>
        public static BodySliderItem ResolveItem(string itemName)
        {
            return BodySliderDefs.Find(itemName);
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var menuItem in items)
            {
                var item = ResolveItem(menuItem.name);
                if (item == null)
                {
                    view.DrawLabel(menuItem.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの項目の行か分かるよう見出しを出す
                TimelineItemClipboardMenu.DrawHeading(view, item.displayName, layer, menuItem.name);
                BodySliderRowDrawer.DrawComponents(view, maid, item, ComponentLabelWidth);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 体型スライダーに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
```

- [ ] **Step 8: `BodySliderSnapshot.cs` を書く**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>体型スライダーのスナップショット (既定値でない項目の値)</summary>
    public class BodySliderSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, Vector3> _values;

        private static BodySliderController controller
            => MaidManipulateManager.instance.bodySliderController;

        public static BodySliderSnapshot Capture(Maid maid)
        {
            var values = new Dictionary<string, Vector3>();
            foreach (var pair in controller.GetNonDefaultValues(maid))
            {
                values[pair.Key] = pair.Value;
            }

            return new BodySliderSnapshot
            {
                _capturedMaid = maid,
                _values = values,
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        /// <summary>記録の無い項目は既定値へ戻す</summary>
        public void Apply(Maid maid)
        {
            foreach (var item in BodySliderDefs.items)
            {
                Vector3 values;
                controller.SetValues(maid, item.key,
                    _values.TryGetValue(item.key, out values) ? values : item.defaultValues);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as BodySliderSnapshot;
            if (o == null || o._values.Count != _values.Count)
            {
                return false;
            }
            foreach (var pair in _values)
            {
                Vector3 otherValues;
                if (!o._values.TryGetValue(pair.Key, out otherValues) || pair.Value != otherValues)
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

`Vector3 !=` は近似比較（差の 2 乗が 1e-10 未満）なので、MaidScale の `Mathf.Approximately` と同程度の判定になる。

- [ ] **Step 9: 登録と csproj**

`TimelineIntegration.cs:326` の直後:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.BodySliderTimelineLayer), MTEP.BodySliderTimelineLayer.Create);
```

`TimelineIntegration.cs:370` の直後:

```csharp
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.BodySliderTimelineLayer), new BodySliderItemInspector());
```

`TimelineIntegration.cs:509` の直後:

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.BodySlider,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBodySlider>);
```

csproj（MaidScale の対応ファイルの直後）:

```xml
    <Compile Include="Manager\History\BodySliderSnapshot.cs" />
```

```xml
    <Compile Include="BodySliderRowDrawer.cs" />
```

```xml
    <Compile Include="Timeline\ItemInspector\BodySliderItemInspector.cs" />
```

```xml
    <Compile Include="Timeline\TimelineLayer\BodySliderTimelineLayer.cs" />
```

```xml
    <Compile Include="Timeline\TransformData\TransformDataBodySlider.cs" />
```

- [ ] **Step 10: テストが通ることを確かめる**

Step 2 と同じフィルタで実行する。
Expected: 両ビルド成功、BodySliderLayerTests・TimelineLayerCategoryTests・TimelineLayerResetOnRemoveTests 全件 PASS

- [ ] **Step 11: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(body-slider): 体型レイヤー・Inspector・Undo を追加する"
```

---

### Task 4: 旧 MaidScale の移行とシーンプリセット

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:1452-1460`（変換段を追加）、`:1566` 付近（定数）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:267`（`CurrentVersion = 39`）
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:246-299`（`ScenePresetBodySlider` 追加、`ScenePresetMaidScale` の読込専用化）、`:965-966`（`bodySlider` フィールド）、`:1299-1305`（v41）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1135`、`:1238-1247`、`:2192`、`:2900-2918`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BodySliderMigrationTests.cs`、`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetBodySliderTests.cs`
- Modify（版数を固定している既存テスト）: `source/COM3D2.SceneEditor.Plugin.Tests/PngBrightnessMigrationTests.cs:181-185`、`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetEffectsTests.cs:149`、`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLightCharacterShadowTests.cs:55-58`

**Interfaces:**
- Consumes: `BodySliderDefs.legacyMaidScaleKeys`、`BodySliderController`（Task 1〜3）
- Produces:
  - `TimelineXml.BodySliderVersion`（= 39）、`static void TimelineXml.ConvertMaidScaleLayer(TimelineLayerXml layer)`
  - `ScenePresetBodySlider`: `List<ScenePresetBodySliderParam> parameters`、`static FromValues(IEnumerable<KeyValuePair<string, Vector3>>)`、`Vector3 GetValues(string key)`、`static FromLegacyMaidScale(ScenePresetMaidScale)`
  - `ScenePresetMaid.bodySlider`

- [ ] **Step 1: タイムライン移行のテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/BodySliderMigrationTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 39 でのメイドスケールレイヤー → 体型レイヤーの移行を固定する。
    /// 腕 6 本の均一倍率 s を、腕の左右別項目の (s, s, s) へ移す
    /// </summary>
    public class BodySliderMigrationTests
    {
        private static TransformXml CreateMaidScale(string boneName, float[] values)
        {
            return new TransformXml
            {
                name = boneName,
                type = TransformType.MaidScale,
                values = values,
                inTangents = values.Length > 0 ? new[] { 0.5f } : new float[0],
                outTangents = values.Length > 0 ? new[] { -0.5f } : new float[0],
                inSmoothBit = 1,
                outSmoothBit = 0,
            };
        }

        private static TimelineXml CreateTimeline(int version, string className, params TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml { transform = transform });
            }

            // FrameXml.bones は既定が null なので明示的に作る
            var keyFrame = new FrameXml { frameNo = 0, bones = bones };
            var layer = new TimelineLayerXml { className = className };
            layer.keyFrames.Add(keyFrame);

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        [Fact]
        public void Initialize_v38のメイドスケールは体型レイヤーの腕の項目になる()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Forearm", new[] { 1.5f }));

            timeline.Initialize();

            var layer = timeline.layers[0];
            Assert.Equal("BodySliderTimelineLayer", layer.className);
            var transform = layer.keyFrames[0].bones[0].transform;
            Assert.Equal("FARMSCL_L", transform.name);
            Assert.Equal(TransformType.BodySlider, transform.type);
            Assert.Equal(new[] { 1.5f, 1.5f, 1.5f }, transform.values);
        }

        [Fact]
        public void Initialize_タンジェントとsmoothビットは3成分へ写る()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 R Hand", new[] { 2f }));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal("HANDSCL_R", transform.name);
            Assert.Equal(new[] { 0.5f, 0.5f, 0.5f }, transform.inTangents);
            Assert.Equal(new[] { -0.5f, -0.5f, -0.5f }, transform.outTangents);
            Assert.Equal(7L, transform.inSmoothBit);
            Assert.Equal(0L, transform.outSmoothBit);
        }

        [Fact]
        public void Initialize_値の無いキーは倍率1として移す()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new float[0]));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal(new[] { 1f, 1f, 1f }, transform.values);
        }

        [Fact]
        public void Initialize_対象外の骨名のキーは捨てる()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 Head", new[] { 2f }),
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));

            timeline.Initialize();

            var bones = timeline.layers[0].keyFrames[0].bones;
            Assert.Single(bones);
            Assert.Equal("HANDSCL_L", bones[0].transform.name);
        }

        [Fact]
        public void Initialize_v39以降と他のレイヤーは変えない()
        {
            var current = CreateTimeline(39, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));
            current.Initialize();
            Assert.Equal("MaidScaleTimelineLayer", current.layers[0].className);

            var other = CreateTimeline(38, "GravityTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));
            other.Initialize();
            Assert.Equal(TransformType.MaidScale, other.layers[0].keyFrames[0].bones[0].transform.type);
        }

        [Fact]
        public void 現行版は39()
        {
            Assert.Equal(39, TimelineData.CurrentVersion);
            Assert.Equal(39, TimelineXml.BodySliderVersion);
        }
    }
}
```

- [ ] **Step 2: プリセットのテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetBodySliderTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの体型スライダー (v41) と、旧メイドスケール (v38) からの変換を固定する</summary>
    public class ScenePresetBodySliderTests
    {
        private static string Serialize(ScenePresetMaid maid)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, maid);
                return writer.ToString();
            }
        }

        private static ScenePresetMaid Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaid)serializer.Deserialize(reader);
            }
        }

        private static KeyValuePair<string, Vector3> Pair(string key, Vector3 values)
        {
            return new KeyValuePair<string, Vector3>(key, values);
        }

        [Fact]
        public void 既定値と定義に無い項目は書き出さない()
        {
            var preset = ScenePresetBodySlider.FromValues(new[]
            {
                Pair("THISCL", new Vector3(1.5f, 1f, 1f)),
                Pair("THIPOS", Vector3.zero),
                Pair("UNKNOWN", new Vector3(2f, 2f, 2f)),
            });

            Assert.Single(preset.parameters);
            Assert.Equal("THISCL", preset.parameters[0].name);
        }

        [Fact]
        public void XMLの往復で値を保ち記録の無い項目は既定値になる()
        {
            var maid = new ScenePresetMaid
            {
                bodySlider = ScenePresetBodySlider.FromValues(new[] { Pair("THIPOS", new Vector3(10f, 0f, -5f)) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<param name=\"THIPOS\" x=\"10\" y=\"0\" z=\"-5\" />", text);

            var loaded = Deserialize(text);
            Assert.Equal(new Vector3(10f, 0f, -5f), loaded.bodySlider.GetValues("THIPOS"));
            Assert.Equal(Vector3.one, loaded.bodySlider.GetValues("THISCL"));
        }

        [Fact]
        public void 全項目が既定でも空要素を書く()
        {
            var maid = new ScenePresetMaid { bodySlider = ScenePresetBodySlider.FromValues(new KeyValuePair<string, Vector3>[0]) };
            var loaded = Deserialize(Serialize(maid));
            Assert.NotNull(loaded.bodySlider);
            Assert.Empty(loaded.bodySlider.parameters);
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            var loaded = Deserialize(Serialize(new ScenePresetMaid()));
            Assert.Null(loaded.bodySlider);
            Assert.Null(loaded.maidScale);
        }

        [Fact]
        public void 手で書き換えた範囲外とNaNの値は丸める()
        {
            var preset = new ScenePresetBodySlider();
            preset.parameters.Add(new ScenePresetBodySliderParam { name = "THISCL", x = 99f, y = float.NaN, z = 1f });

            Assert.Equal(new Vector3(2f, 1f, 1f), preset.GetValues("THISCL"));
        }

        [Fact]
        public void 旧メイドスケールは腕の左右別項目の均一倍率になる()
        {
            var legacy = new ScenePresetMaidScale();
            legacy.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L UpperArm", scale = 1.5f });
            legacy.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 Head", scale = 2f });

            var converted = ScenePresetBodySlider.FromLegacyMaidScale(legacy);

            Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), converted.GetValues("UPARMSCL_L"));
            Assert.Equal(Vector3.one, converted.GetValues("UPARMSCL_R"));
            Assert.Single(converted.parameters);
        }

        [Fact]
        public void 旧メイドスケールが無ければnull()
        {
            Assert.Null(ScenePresetBodySlider.FromLegacyMaidScale(null));
        }

        [Fact]
        public void 旧プリセットのmaidScale要素を読める()
        {
            const string text =
                "<ScenePresetMaid><maidScale><bone name=\"Bip01 R Hand\" scale=\"2\" /></maidScale></ScenePresetMaid>";
            var loaded = Deserialize(text);

            Assert.Null(loaded.bodySlider);
            var converted = ScenePresetBodySlider.FromLegacyMaidScale(loaded.maidScale);
            Assert.Equal(new Vector3(2f, 2f, 2f), converted.GetValues("HANDSCL_R"));
        }

        [Fact]
        public void 体型スライダーと旧メイドスケールの両方があれば体型スライダーを使う()
        {
            var state = new ScenePresetMaid
            {
                bodySlider = ScenePresetBodySlider.FromValues(new[] { Pair("HANDSCL_R", new Vector3(1.2f, 1.2f, 1.2f)) }),
                maidScale = new ScenePresetMaidScale(),
            };
            state.maidScale.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 R Hand", scale = 2f });

            Assert.Equal(new Vector3(1.2f, 1.2f, 1.2f), ScenePresetBodySlider.Resolve(state).GetValues("HANDSCL_R"));
        }

        [Fact]
        public void 現行版は41()
        {
            Assert.Equal(41, ScenePresetData.CurrentVersion);
        }
    }
}
```

版数を固定している既存テストを、版を上げても壊れない形に変える。現行版そのものは新しいテスト（`現行版は39` / `現行版は41`）が固定する。

`PngBrightnessMigrationTests.cs:181-185` を次に置き換える:

```csharp
        [Fact]
        public void 現行バージョンは明るさの換算後()
        {
            Assert.True(TimelineData.CurrentVersion >= TimelineXml.PngBrightnessScaleVersion);
        }
```

`ScenePresetEffectsTests.cs:149` を次に置き換える:

```csharp
            Assert.True(ScenePresetData.CurrentVersion >= 40);
```

`ScenePresetLightCharacterShadowTests.cs:55-58` のテストを次に置き換える:

```csharp
        public void シーンプリセットの版はキャラの影の追加後()
        {
            Assert.True(ScenePresetData.CurrentVersion >= 39);
        }
```

v39 はキャラの影を追加した版（`ScenePresetData.cs` の版の履歴）。テスト名とメソッド本体の行番号は、実装時に開いて確かめる。

- [ ] **Step 3: テストが失敗することを確かめる**

「ビルド + テストのコマンド」を `<Filter>` = `FullyQualifiedName~BodySliderMigrationTests|FullyQualifiedName~ScenePresetBodySliderTests` で実行する。
Expected: テストプロジェクトのコンパイルエラー（`ScenePresetBodySlider` などが無い）

- [ ] **Step 4: タイムラインの変換段を書く**

`TimelineData.cs:267`: `public static readonly int CurrentVersion = 39;`

`TimelineXml.cs` の `Initialize` で、`ConvertPngBrightnessToScale` の段（1452〜1458 行）の直後、`ConvertPlugin();` の前に追加:

```csharp
            if (version < BodySliderVersion)
            {
                foreach (var layer in layers)
                {
                    ConvertMaidScaleLayer(layer);
                }
            }
```

`ConvertPngBrightnessToScale` の直後にメソッドを追加:

```csharp
        /// <summary>
        /// メイドスケールレイヤー (腕 6 本の均一倍率) を体型レイヤーへ変える (version 39)。
        /// 倍率 s は腕の左右別項目の (s, s, s) にし、タンジェントと smooth ビットは 3 成分へ写す。
        /// 対象外の骨名のキーは捨てる
        /// </summary>
        public static void ConvertMaidScaleLayer(TimelineLayerXml layer)
        {
            if (layer.className != MaidScaleLayerNameAtV38)
            {
                return;
            }
            layer.className = BodySliderLayerNameAtV39;

            var convertedCount = 0;
            foreach (var keyFrame in layer.keyFrames)
            {
                if (keyFrame.bones == null)
                {
                    continue;
                }

                keyFrame.bones.RemoveAll(bone =>
                {
                    var transform = bone.transform;
                    if (transform == null || transform.type != TransformType.MaidScale)
                    {
                        return false;
                    }

                    string key;
                    if (!BodySliderDefs.legacyMaidScaleKeys.TryGetValue(transform.name, out key))
                    {
                        return true;
                    }

                    transform.name = key;
                    transform.type = TransformType.BodySlider;
                    transform.values = Triple(transform.values, MaidScaleDefaultAtV38);
                    transform.inTangents = Triple(transform.inTangents, 0f);
                    transform.outTangents = Triple(transform.outTangents, 0f);
                    transform.inSmoothBit = (transform.inSmoothBit & 1L) != 0 ? 7L : 0L;
                    transform.outSmoothBit = (transform.outSmoothBit & 1L) != 0 ? 7L : 0L;
                    convertedCount++;
                    return false;
                });
            }

            if (convertedCount > 0)
            {
                MTEUtils.LogDebug("Convert maid scale to body slider count={0}", convertedCount);
            }
        }

        /// <summary>1 値の配列を同じ値の 3 値へ広げる。値が無ければ fallback で埋める</summary>
        private static float[] Triple(float[] source, float fallback)
        {
            var value = source != null && source.Length > 0 ? source[0] : fallback;
            return new[] { value, value, value };
        }
```

`MTEUtils.LogDebug` はテストから呼ばれる。既存の `ConvertPngBrightnessToScale` もテスト経由で `LogDebug` を呼んでいるので、ゲーム外でも落ちない。落ちる場合は `ConvertPostEffectMaskValues` と同じく件数を返してログを呼び出し側へ移す。

定数（`PngBrightnessScaleVersion` の直後）:

```csharp
        // version 39 で変換する前 (v38) のメイドスケールレイヤーの名前と倍率の既定値。
        // クラスは削除済みなので、型名ではなく文字列で持つ
        private const string MaidScaleLayerNameAtV38 = "MaidScaleTimelineLayer";
        private const string BodySliderLayerNameAtV39 = "BodySliderTimelineLayer";
        private const float MaidScaleDefaultAtV38 = 1f;
        /// <summary>メイドスケールレイヤーを体型レイヤーへ変えたバージョン</summary>
        public const int BodySliderVersion = 39;
```

- [ ] **Step 5: プリセットのデータを書く**

`ScenePresetData.cs:246-299` の `ScenePresetMaidScaleBone` / `ScenePresetMaidScale` を次に置き換える:

```csharp
    /// <summary>旧メイドスケールの骨 1 本分 (v38〜40)。読込専用</summary>
    public class ScenePresetMaidScaleBone
    {
        [XmlAttribute]
        public string name;

        [XmlAttribute]
        public float scale = 1f;
    }

    /// <summary>
    /// 旧メイドスケール (v38〜40)。v41 で体型スライダー (bodySlider) に置き換わったため読込専用。
    /// bodySlider の無いプリセットだけ、適用時に ScenePresetBodySlider.FromLegacyMaidScale で変換する
    /// </summary>
    public class ScenePresetMaidScale
    {
        [XmlElement("bone")]
        public List<ScenePresetMaidScaleBone> bones = new List<ScenePresetMaidScaleBone>();
    }

    /// <summary>体型スライダーの項目 1 件分 (v41)。x / y / z は項目の定義順の 3 成分 (スケールは width / depth / height)</summary>
    public class ScenePresetBodySliderParam
    {
        /// <summary>BodySliderDefs の項目キー</summary>
        [XmlAttribute]
        public string name;

        [XmlAttribute]
        public float x;

        [XmlAttribute]
        public float y;

        [XmlAttribute]
        public float z;
    }

    /// <summary>
    /// 体型スライダー (v41)。既定値でない項目だけを持つ。
    /// 全項目が既定でも要素自体は書き、旧プリセット (要素なし = null) と区別する
    /// </summary>
    public class ScenePresetBodySlider
    {
        [XmlElement("param")]
        public List<ScenePresetBodySliderParam> parameters = new List<ScenePresetBodySliderParam>();

        /// <summary>項目キーと値の組から作る。既定値と定義に無い項目は書かない</summary>
        public static ScenePresetBodySlider FromValues(IEnumerable<KeyValuePair<string, Vector3>> values)
        {
            var result = new ScenePresetBodySlider();
            foreach (var pair in values)
            {
                var item = BodySliderDefs.Find(pair.Key);
                if (item == null || item.IsDefault(pair.Value))
                {
                    continue;
                }
                var clamped = item.Clamp(pair.Value);
                result.parameters.Add(new ScenePresetBodySliderParam
                {
                    name = pair.Key,
                    x = clamped.x,
                    y = clamped.y,
                    z = clamped.z,
                });
            }
            return result;
        }

        /// <summary>
        /// 項目の値。記録の無い項目は既定値。同じ項目が複数あれば先のものを使う。
        /// 手で書き換えた値に備えて範囲へ丸める
        /// </summary>
        public Vector3 GetValues(string key)
        {
            var item = BodySliderDefs.Find(key);
            if (item == null)
            {
                return Vector3.zero;
            }
            foreach (var param in parameters)
            {
                if (param != null && param.name == key)
                {
                    return item.Clamp(new Vector3(param.x, param.y, param.z));
                }
            }
            return item.defaultValues;
        }

        /// <summary>旧メイドスケールを腕の左右別項目の均一倍率へ変える。旧データが無ければ null</summary>
        public static ScenePresetBodySlider FromLegacyMaidScale(ScenePresetMaidScale legacy)
        {
            if (legacy == null)
            {
                return null;
            }
            var values = new List<KeyValuePair<string, Vector3>>();
            foreach (var bone in legacy.bones)
            {
                string key;
                if (bone == null || bone.name == null
                    || !BodySliderDefs.legacyMaidScaleKeys.TryGetValue(bone.name, out key))
                {
                    continue;
                }
                values.Add(new KeyValuePair<string, Vector3>(
                    key, new Vector3(bone.scale, bone.scale, bone.scale)));
            }
            return FromValues(values);
        }

        /// <summary>適用に使う体型スライダー。bodySlider を優先し、無ければ旧メイドスケールを変換する。どちらも無ければ null</summary>
        public static ScenePresetBodySlider Resolve(ScenePresetMaid state)
        {
            return state.bodySlider ?? FromLegacyMaidScale(state.maidScale);
        }
    }
```

`ScenePresetData.cs` の先頭の using に `UnityEngine` が無ければ追加する（`ScenePresetGravity.offset` が `Vector3` なので既にあるはず）。

`ScenePresetData.cs:965-966` を次に置き換える:

```csharp
        /// <summary>
        /// 旧メイドスケール (v38〜40)。読込専用で、新規保存では書かない。
        /// bodySlider の無いプリセットだけ、適用時に体型スライダーへ変換する
        /// </summary>
        public ScenePresetMaidScale maidScale;

        /// <summary>体型スライダー (v41)。旧プリセットは null になり、maidScale も無ければ適用時に触らない</summary>
        public ScenePresetBodySlider bodySlider;
```

`ScenePresetData.cs` の版の履歴（`// v40: …` の 2 行の直後）に追加し、`CurrentVersion` を 41 にする:

```csharp
        // v41: maid に bodySlider (体型スライダー。既定値でない項目だけを param 要素に持つ) を追加し、
        //      maidScale は読込専用にした。全項目が既定でも空要素を書く。
        //      bodySlider が無く maidScale がある旧形式は、腕の左右別項目の均一倍率へ変換して適用する
        public static readonly int CurrentVersion = 41;
```

- [ ] **Step 6: プリセットの記録と適用を差し替える**

`ScenePresetManager.cs:1135` を次に置き換える:

```csharp
            state.bodySlider = ScenePresetBodySlider.FromValues(
                maidManager.bodySliderController.GetNonDefaultValues(maid));
```

`ScenePresetManager.cs:1238-1247`（`CaptureMaidScale`）を削除する。

`ScenePresetManager.cs:2192` の `ApplyMaidScale(maid, state);` を `ApplyBodySlider(maid, state);` に変える。

`ScenePresetManager.cs:2900-2918`（`ApplyMaidScale`）を次に置き換える:

```csharp
        /// <summary>
        /// 体型スライダーを復元する。bodySlider も旧 maidScale も無いプリセットでは変更しない。
        /// 記録の無い項目は既定値へ戻す
        /// </summary>
        private static void ApplyBodySlider(Maid maid, ScenePresetMaid state)
        {
            var preset = ScenePresetBodySlider.Resolve(state);
            if (preset == null)
            {
                return;
            }

            // 状態の無いメイドへ既定値を書いても SetValues は状態を作らないので、
            // ApplyGravity のような既定値だけのプリセットの判定は要らない
            var controller = maidManager.bodySliderController;
            foreach (var item in BodySliderDefs.items)
            {
                controller.SetValues(maid, item.key, preset.GetValues(item.key));
            }
        }
```

`ScenePresetManager.cs` に `using UnityEngine;` が無ければ追加する。

既存の `ScenePresetMaidScaleTests.cs` は `ScenePresetMaidScale.FromScales` / `GetScale` を使っていてコンパイルできなくなるので、このステップで削除する（新しいテストが同じ範囲を覆う）。

```bash
git rm source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetMaidScaleTests.cs
```

- [ ] **Step 7: テストが通ることを確かめる**

「ビルド + テストのコマンド」を `<Filter>` なし（全件）で実行する。`--filter` 引数ごと外す。
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 8: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(body-slider): メイドスケールのタイムラインとプリセットを体型スライダーへ移行する"
```

---

### Task 5: 体型タブと MaidScale の削除

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs:30-49,224-229,318-346,359-362,378-390`
- Delete: `MaidManipulation/MaidScaleBones.cs`、`MaidManipulation/MaidScaleController.cs`、`MaidManipulation/MaidScaleState.cs`、`MaidScaleRowDrawer.cs`、`Manager/History/MaidScaleSnapshot.cs`、`Manager/MaidScaleLateUpdatePatch.cs`、`Timeline/ItemInspector/MaidScaleItemInspector.cs`、`Timeline/TimelineLayer/MaidScaleTimelineLayer.cs`、`Timeline/TransformData/TransformDataMaidScale.cs`、`source/COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs`
- Modify: csproj（MaidScale の 9 行を削除）、`COM3D2.SceneEditor.Plugin.cs`、`MaidManipulateManager.cs`、`ScreenshotManager.cs`、`ThumbnailCapture.cs`、`TimelineIntegration.cs`、`HistoryScope.cs`、`SnapshotFactory.cs`、`ITransformData.cs`（コメントだけ）、`TimelineLayerCategoryTests.cs`、`TimelineLayerResetOnRemoveTests.cs`

**Interfaces:**
- Consumes: `BodySliderRowDrawer`、`BodySliderDefs.groupNames / ItemsInGroup`、`BodySliderTimelineLayer`（Task 1〜3）

- [ ] **Step 1: ボーンウィンドウのタブを差し替える**

`BoneEditWindow.cs:30-36`:

```csharp
        /// <summary>ウィンドウ内の内部タブ。体型はメイドだけで出す</summary>
        private enum BoneTabType
        {
            編集,
            プリセット,
            体型,
        }

        /// <summary>モデルを対象にしているときのタブ。体型はメイド専用なので出さない</summary>
```

`BoneEditWindow.cs:48` の `MaidScaleLabelWidth` を次に置き換える:

```csharp
        private const float BodySliderLabelWidth = 50f;

        /// <summary>体型タブで開いているグループ (BodySliderDefs.groupNames の添字)</summary>
        private int _bodySliderGroupIndex = 0;
```

`TryFocusTimelineLayer`（224〜229 行）の MaidScale の分岐を次に置き換える:

```csharp
            if (layerType == typeof(MTEP.BodySliderTimelineLayer))
            {
                SwitchTargetType(BoneEditTargetType.Maid);
                _tabType = BoneTabType.体型;
                return true;
            }
```

`DrawContentTabs` の summary（318 行）を「編集 / プリセット / 体型タブとその中身。…」に変え、337〜340 行を次に置き換える:

```csharp
            else if (_tabType == BoneTabType.体型)
            {
                DrawBodySliderContent(target);
            }
```

`BeginMaidTabGate`（360〜362 行）:

```csharp
            var layerType = _tabType == BoneTabType.体型
                ? typeof(MTEP.BodySliderTimelineLayer)
                : typeof(MTEP.MotionTimelineLayer);
```

`DrawMaidScaleContent`（378〜390 行）を次に置き換える:

```csharp
        /// <summary>
        /// 体型スライダー (ModsSlider 相当)。項目が多いのでグループの内部タブで切り替える。
        /// 値は MaidVoicePitch の体型スライダーの結果に掛け合わせる (スケールは乗算、位置は加算)
        /// </summary>
        private void DrawBodySliderContent(Maid target)
        {
            _bodySliderGroupIndex = DrawInnerTabs(BodySliderDefs.groupNames, _bodySliderGroupIndex, TAB_WIDTH);
            var group = BodySliderDefs.groupNames[_bodySliderGroupIndex];

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var item in BodySliderDefs.ItemsInGroup(group))
            {
                view.DrawLabel(item.displayName, -1, ROW_HEIGHT);
                BodySliderRowDrawer.DrawComponents(view, target, item, BodySliderLabelWidth);
            }
            BodySliderRowDrawer.DrawResetAll(view, target, ResetButtonWidth, ROW_HEIGHT);

            view.EndScrollView();
        }
```

`TAB_WIDTH` が 5 つのグループのタブに対して広すぎる・狭すぎる場合は、実機（Task 6）で見て調整する。

- [ ] **Step 2: MaidScale の呼び出しを消す**

| ファイル | 消すもの |
|---|---|
| `COM3D2.SceneEditor.Plugin.cs` | `maidScaleController.RestoreApplied();` とそのコメント 2 行、`MaidScaleLateUpdatePatch.Init();` とそのコメント、OnGUI の `scaleController` の 3 行（宣言・Suspend・Resume） |
| `MaidManipulateManager.cs` | `maidScaleController` のフィールドと summary、`Release` / `Destroy` の呼び出し（Release はコメント行も） |
| `ScreenshotManager.cs` / `ThumbnailCapture.cs` | `maidScaleController.BeginCapture()` / `EndCapture()` の行。直前のコメント「GUI の間だけ外したメイドスケールを掛け直す」は「GUI の間だけ外した体型スライダーを掛け直す」に変えて、`bodySliderController` の行の上へ残す |
| `TimelineIntegration.cs` | `MaidScaleTimelineLayer` の RegisterLayer / Inspector 登録、`TransformType.MaidScale` の RegisterTransform |
| `HistoryScope.cs` | `MaidScale,` とその summary |
| `SnapshotFactory.cs` | `case HistoryScope.MaidScale:` の 2 行 |
| `TimelineLayerCategoryTests.cs` | `{ "MaidScaleTimelineLayer", TimelineLayerCategory.Maid },` |
| `TimelineLayerResetOnRemoveTests.cs` | `"MaidScaleTimelineLayer",` とそのコメント行 |
| csproj | MaidScale 系 9 ファイルの `<Compile Include>` 行 |

`ITransformData.cs` の `MaidScale,` は残す。消すと旧タイムラインの読込が XmlSerializer の例外で失敗するため。次のコメントを付ける:

```csharp
        /// <summary>旧メイドスケール。version 39 で BodySlider へ変換する。読込互換のためだけに残す</summary>
        MaidScale,
```

- [ ] **Step 3: MaidScale のファイルを消す**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
git rm COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleBones.cs \
  COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleController.cs \
  COM3D2.SceneEditor.Plugin/MaidManipulation/MaidScaleState.cs \
  COM3D2.SceneEditor.Plugin/MaidScaleRowDrawer.cs \
  COM3D2.SceneEditor.Plugin/Manager/History/MaidScaleSnapshot.cs \
  COM3D2.SceneEditor.Plugin/Manager/MaidScaleLateUpdatePatch.cs \
  COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/MaidScaleItemInspector.cs \
  COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidScaleTimelineLayer.cs \
  COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataMaidScale.cs \
  COM3D2.SceneEditor.Plugin.Tests/MaidScaleTests.cs
```

- [ ] **Step 4: 取り残しが無いことを確かめる**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
grep -rn "MaidScale\|maidScale\|メイドスケール\|BoneTabType.腕スケール" --include=*.cs --include=*.csproj . | grep -v "/obj/"
```

（「腕スケール」単体で探すと、ゲーム側の前腕スケールを指す無関係なコメント `MaidIKHoldController.cs:505,613`、`TimelineManager.cs:198` に当たるので、パターンから外している）

Expected: 次の箇所だけが残る（いずれも読込互換か移行のため）。
- `ITransformData.cs` の `MaidScale,`
- `TimelineXml.cs` の変換段（`ConvertMaidScaleLayer` / `MaidScaleLayerNameAtV38` / `MaidScaleDefaultAtV38` / `TransformType.MaidScale`）
- `ScenePresetData.cs` の `ScenePresetMaidScale` / `ScenePresetMaidScaleBone` / `maidScale` フィールド / 版の履歴コメント
- `BodySliderDefs.cs` の `legacyMaidScaleKeys`
- テストの `BodySliderMigrationTests.cs` / `ScenePresetBodySliderTests.cs`

- [ ] **Step 5: ビルドとテスト**

「ビルド + テストのコマンド」を `<Filter>` なし（全件）で実行する。
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 6: Commit**

```bash
git add -A source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(body-slider): ボーンウィンドウに体型タブを追加し、メイドスケールを廃止する"
```

---

### Task 6: ドキュメントと実機検証

**Files:**
- Modify: `docs-site/guide/maid-editing.md`（「ボーン編集」の節に「体型」を追加）
- Modify: `docs-site/timeline/layers-maid.md`（体型レイヤー）
- Modify: `docs-site/timeline/compatibility.md`（MTE・旧 SE との互換）
- Modify: `docs-site/guide/scene-preset.md:17`（保存対象の列挙）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（タイムライン XML の互換方向の節）

- [ ] **Step 1: ユーザー向けドキュメントを書く**

`docs-site/guide/maid-editing.md` の `## ボーン編集` の節で、`### ボーンプリセット` の直後（`### 注意` の前）に追加:

```markdown
### 体型

ボーン編集ウィンドウの `体型` タブ（メイドを対象にしているときだけ出ます）で、ModsSlider（AddModsSlider）の体型スライダーと同じ項目を調整できます。足・腰・胴・首・胸・肩・腕のスケールと位置を、`脚` `腰` `胴・首` `胸` `肩・腕` の内部タブで切り替えて編集します。

- 数値の意味と範囲は ModsSlider と同じです。ModsSlider で使っていた値をそのまま入力できます
- 上腕・前腕・手は、左右を別々に調整できます
- MaidVoicePitch の体型スライダーの結果に重ねて効きます（スケールは掛け算、位置は足し算）。ModsSlider の設定は書き換えません
- MaidVoicePitch が効かない新ボディや、MaidVoicePitch を入れていない環境でも効きます
- `すべて既定に戻す` で全項目を元に戻します
- タイムラインの「体型」レイヤーとシーンプリセットにも保存されます
```

`docs-site/timeline/layers-maid.md` に、既存のレイヤーの書式に合わせて「体型」レイヤーを追加する。中身は次のとおり。
- 項目は体型スライダーの各項目
- 値は 3 成分で、補間はタンジェント
- キーを打つと、既定値でない項目と既にキーのある項目がキーになる
- まだキーの無い項目を「既定値から始めて変化させる」ときは、先に後ろのフレームで値を変えてキーを打ち、その後で前のフレームに戻って既定値のキーを打つ（既定値のままの項目は、キーが無いとキーにならないため）

`docs-site/timeline/compatibility.md` の `メイドノード表示` の行の直後に追加:

```markdown
- `体型` レイヤーは SceneEditor 独自です。これを含む XML は MTE や以前の SceneEditor で読み込みに失敗します
```

`docs-site/guide/scene-preset.md:17` の列挙で「ノード表示」の後ろに「体型」を足す。

- [ ] **Step 2: CLAUDE.md の互換の節を直す**

`W:\COM3D2_5\work\CLAUDE.md` の「メイドスケールレイヤー（`MaidScaleTimelineLayer`、…）」の項目を、次に置き換える:

```markdown
- version 39 で体型レイヤー（`BodySliderTimelineLayer`、ModsSlider 相当の骨のスケール・位置、`TransformType.BodySlider`、値数 3）を追加し、メイドスケールレイヤー（`MaidScaleTimelineLayer`）を廃止した。旧 XML は `TimelineXml.ConvertMaidScaleLayer` が腕の左右別項目（`UPARMSCL_L` など）の (s,s,s) へ変換する。`TransformType.MaidScale` は読込互換のためだけに残っている。これを含む XML は MTE・旧 SE では未知の enum 値（`<Type>BodySlider</Type>`）でデシリアライズに失敗する。シーンプリセットは v41 で maid に `bodySlider` 要素（既定値でない項目だけを `<param name x y z>` で持つ。全項目既定でも空要素を書く）を追加し、`maidScale` は読込専用。`bodySlider` が無く `maidScale` だけある旧プリセットは腕の項目へ変換して適用する
```

- [ ] **Step 3: 実機検証（restart-verify スキル）**

ゲームが旧 DLL を握っているので、`com3d25-devbridge:restart-verify` スキルの手順で DLL を反映してから検証する。検証はデイリー画面でエディタを有効にした通常シーンで行う（撮影モードは非対応）。devbridge の `eval_csharp` で確認する項目は次のとおり。

| # | 確認すること | 方法 | 期待 |
|---|---|---|---|
| 1 | CRC ボディで、各項目の対象骨が複製骨のキャッシュに入るか | `BodySliderController` の `_entries[maid].bones.Keys` をリフレクションで列挙する（memory `devbridge-plugin-reflection`） | `_SCL_` 系、momotwist / momoniku、Kata_、Skirt、Hip_、Mune_ が入っている。無い骨は一覧にして報告する（黙って飛ばす仕様なので不具合ではないが、ユーザーに伝える） |
| 2 | 旧ボディ + MVP（ModsSlider で THISCL を 1.3 にしたメイド）で、SE の THISCL 1.2 が上に乗るか | `onPreCull` で `Bip01 L Thigh` の複製骨の localScale を読む（memory `capture-runs-inside-ongui`） | MVP 単独のときの値 × (1, 1.2, 1.2) 相当 |
| 3 | ModsSlider で値を変えた後（Blend の再実行後）に、SE の倍率が積み上がらないか | 2 の後に ModsSlider の値を変え、数フレーム後に同じ骨を読む | MVP の新しい値 × SE の倍率のまま |
| 4 | FARMFIX ON（旧ボディ）で FARMSCL_L が効くか | 前腕の複製骨を読む | ForeArmFix の結果 × SE の倍率 |
| 5 | 胸（MUNESCL / MUNEPOS）と揺れ骨の干渉 | 旧ボディ（jiggleBone）と CRC（dbMune）で胸を揺らしながらスクリーンショット | 破綻しない。破綻したらスクショを添えて報告する |
| 6 | 位置の変換式が MVP と一致するか | 旧ボディで、ModsSlider に THIPOS (x=50, z=30) を入れたメイドと、SE に同じ値を入れたメイドを並べて `capture` | 足の付け根の位置が一致する |
| 7 | 左右別の腕 | UPARMSCL_L だけ 2 にする | 左上腕だけ太く長くなる |
| 8 | 旧タイムラインの移行 | MaidScale のキーを持つ開発中の XML を読む | 体型レイヤーの腕の項目として再生される |
| 9 | 体型タブの見た目 | `screenshot` でボーンウィンドウを撮る | グループのタブが収まり、スライダーのラベルが切れない |
| 10 | CRC ボディで軸の向きが合っているか | CRC のメイドで、グループ（脚・腰・胴・胸・肩腕）ごとに代表の項目（THISCL の横幅、THIPOS の前後、SPIPOS の上下、MUNEPOS の高さ、UPARMSCL_L の高さ）を動かし、`capture` で撮る | ラベルどおりの向きに動く。変換表は旧ボディの局所軸が前提なので、ずれた項目は一覧にして報告する |
| 11 | スカートと髪の揺れ物 | SKTSCL / SKTPOS を変えて、スカートと髪を揺らしながら撮る | 揺れ物が暴れない。MVP は DynamicSkirtBone の更新中だけ親スケールを 1 に戻しているので、暴れたらその対処を別途検討する |
| 12 | 性能 | 3 メイドに全項目を既定値以外で入れ、`profile_add` で `BodySliderController.OnBodyLateUpdateEnd` を計測する（高頻度メソッドへの patch は計測を汚すので、計測後に外す） | 1 メイドあたり 0.1 ms 程度に収まる。大きければ値と時間を報告する |

確認できなかった項目（ゲームの状態が整わないなど）は、理由を添えてユーザーへ報告する。

- [ ] **Step 4: Commit**

```bash
git add docs-site
git commit -m "docs(body-slider): 体型タブと体型レイヤーの説明を追加する"
```

`W:\COM3D2_5\work\CLAUDE.md` はこのリポジトリの外なので、このコミットには含まれない。変更したことをユーザーへ伝える。

---

## レビュー却下メモ

- 旧 MaidScale テンプレートの適用時に NRE になりうる — 誤検知。テンプレートは今のレイヤーの `layerName` で引く（`TimelineTemplateManager.cs:348,416`）ので、`MaidScaleTimelineLayer` が無くなった後はそのテンプレートは読まれない。決定事項の理由欄を正確に書き直した
- Undo の全再構築で `ResetOnRemove` が走り、キーの無い UI の値が消える — 誤検知。`CleanupLayerOnRemove` を呼ぶのは `RemoveLayer`（`TimelineManager.cs:2170`）と、アンロード・読込・新規作成時の `CleanupAllLayersOnRemove`（`:2048`）だけで、Undo の再構築経路は通らない
