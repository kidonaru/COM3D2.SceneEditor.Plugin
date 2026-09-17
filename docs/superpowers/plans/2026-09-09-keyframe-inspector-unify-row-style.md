# キーフレーム詳細の行スタイル統一 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inspector のキーフレーム詳細（`KeyFrameInspector`）で、カスタム値・色・表示トグルの描画部品を現在値 UI（`TimelineItemInspector` 配下の RowDrawer）と同じものに差し替え、行単位の見た目を揃える。

**Architecture:** キーフレーム詳細は `ITransformData` を汎用に列挙して描く構造を維持する。変更は描画関数の差し替えのみで、カスタム値は `GUIView.DrawCustomValueFloat / Int / Bool`（現在値 UI と同じ拡張メソッド）、色は `GUIView.DrawColor` + `GetColorFieldCache`（カラーピッカー付き）に置き換える。これに伴いドラッグ数値欄のフロー配置（`KeyFrameFlowLayout`）は不要になるため削除する。

**Tech Stack:** C# (.NET 3.5 / net48 の 2 構成)、Unity IMGUI ラッパー `GUIView`（`source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs`）、xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`）

**Spec:** 専用の spec ファイルは無い。本計画の「背景と決定事項」節が仕様。調査元は会話ログ（2026-09-09 の Inspector 調査）。

## Global Constraints

- **git worktree を使わない**。メインの作業ディレクトリで作業する
- **COM3D2 / COM3D25 の両構成をビルドする**（対象フレームワークが異なる）。MSBuild を直接叩き、`debug.bat` は使わない（ゲーム停止中に実行すると実機へ DLL がコピーされる）
- コメント・ログ文言は日本語
- `deploy.bat` / `deploy.ps1` / `release.bat` は実行しない
- コミットメッセージは Conventional Commits 形式の日本語。`commit` スキルで行う

---

## 背景と決定事項

### 現状の差分

| 項目 | キーフレーム詳細（変更前） | 現在値 UI（揃える先） |
|---|---|---|
| 位置/回転/拡縮 | `view.DrawVector3Row` | 同じ。**変更なし** |
| 色 | `DrawVector3Row` で RGB 数値入力 | `view.DrawColor(fieldCache, color, resetColor, onChanged)`（ラベル 90 + 色見本 + 編集ボタンでカラーピッカー） |
| float 値 | `DrawDragFloatField` をフロー配置（ラベル 70 + 欄 60 + リセット） | `view.DrawCustomValueFloat(info, value, onChanged)`（`FloatSlider` 型はラベル 40 + 数値欄 50 + `<` `>` + スライダー + `R`、それ以外は `DrawFloatField` ラベル 40 幅 90） |
| int 値 | `DrawDragIntField` フロー配置 | `view.DrawCustomValueInt(info, value, onChanged)`（スライダー行） |
| bool 値 | `DrawToggle`（要素幅固定） | `view.DrawCustomValueBool(info, value, onChanged)`（全幅トグル） |
| 表示トグル | `DrawToggle("表示", …, itemWidth)` フロー配置 | 現在値 UI に直接の対応は無い。全幅トグル 1 行にする |
| 文字列値 | `DrawTextField` を 8px インデント | インデント無し（現在値 UI は行をインデントしない） |

### 決定事項

1. **1 値 = 1 行**にする。フロー配置（横に詰めて折り返す）は廃止する。ドラッグ増減は失われるが、現在値 UI に合わせることを優先する（ユーザー了承済み）
2. **リセット既定値はそのまま**。`DrawSliderValue` の `R` ボタンは `info.defaultValue` を書くが、これは `ITransformData.GetDefaultCustomValue` と同じ値（`TransformDataBase.cs:834`）
3. **色欄のラベルはキーフレームごとに一意にする**。`ColorPickerWindow` はラベル文字列で編集対象を同定する（`GUIView.cs:2895` 付近 `picker.IsEditing(label)`）ため、`"{bone.name} (F{frameNo})/色"` とする。`ColorFieldCache` は `view.GetColorFieldCache(label, hasAlpha)` が描画順インデックスで採番するので、専用のキャッシュ辞書は持たない（`BackgroundRowDrawer.DrawBgColorRow` と同じ流儀）
4. **色はアルファ無し**（`hasAlpha = false`）。変更前も `Vector3` 経由で RGB のみ編集していたため意味を変えない
5. **ブロック内容のインデント（`BlockIndent = 8f`）を廃止**する。現在値 UI はインデントしないため
6. `FloatFieldCache` / `ColorFieldCache` は描画順インデックスで採番されるため、**同一フレーム内で描画要素数を変えない**制約は従来どおり。開閉・削除を描画ループ外へ遅延する既存の仕組み（`ProcessPendingFold` / `ProcessPendingDelete`）はそのまま使う
7. スライダー行の幅は `DrawCustomValueFloat` の既定（250px）に任せる。Inspector の既定幅 280 に収まり、現在値 UI と同じ寸法になる
8. 色ラベルの一意性は **表示中のブロック**（`config.detailTransformCount` 件まで）の範囲で成立すればよい。表示件数を超えて非表示になったキーフレームは色行自体が描かれないため、ピッカーの対象にならない（既存の表示上限の挙動をそのまま引き継ぐ）

## レビュー却下メモ

- HDR 色（RGB > 1.0）を `DrawColor` では編集できない点を計画・CHANGELOG に明記すべき — 現在値 UI（`LightRowDrawer` 等）と同じ制約を引き継ぐだけで、案 A「現在値 UI に揃える」の要件どおり。新規の劣化ではないため記載しない

### 変更対象ファイル

- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`（描画関数の差し替え、不要な定数・メソッドの削除、クラス summary の更新）
- Delete: `source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs`（フロー配置の列数計算。唯一の利用者が `KeyFrameInspector` なので不要になる）
- Delete: `source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs`
- Modify: `CHANGELOG.md`（「未リリース」節へ 1 項目）

### テスト方針

描画コードは Unity の `GUI` に依存し単体テストできない。検証は (1) 両構成の MSBuild 成功、(2) `dotnet test` 全 PASS（削除したテスト以外に退行がないこと）、(3) ゲーム起動中なら MCP `com3d25-devbridge` の `screenshot` で見た目確認、の 3 点で行う。

---

### Task 1: カスタム値・色・表示トグルを現在値 UI と同じ部品で描く

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs`

**Interfaces:**
- Consumes（すべて既存）:
  - `Extensions.DrawCustomValueFloat(this GUIView view, CustomValueInfo info, float value, Action<float> onChanged)`（`Timeline/Extensions.cs:207`、namespace `COM3D2.MotionTimelineEditor.Plugin`）
  - `Extensions.DrawCustomValueInt(this GUIView view, CustomValueInfo info, int value, Action<int> onChanged)`（同 `:244`）
  - `Extensions.DrawCustomValueBool(this GUIView view, CustomValueInfo info, bool value, Action<bool> onChanged)`（同 `:284`）
  - `GUIView.GetColorFieldCache(string label, bool hasAlpha)`（`MTEUtils/GUIView.cs:3149`）
  - `GUIView.DrawColor(ColorFieldCache fieldCache, Color color, Color resetColor, Action<Color> onColorChanged)`（同 `:2895`）
  - `GUIView.DrawToggle(string label, bool value, float width, float height, Action<bool> onChanged)`（`width = -1` で残り幅）
  - `GUIView.DrawTextField(string label, float labelWidth, string text, float width, float height, Action<string> onChanged, ...)`（同 `:1221`）
- Produces: なし（公開 API は変えない。`KeyFrameInspector.instance.Draw(GUIView)` / `ShouldDraw()` はそのまま）

- [ ] **Step 1: using を追加する**

`KeyFrameInspector.cs` 先頭の using を以下にする（`DrawCustomValue*` 拡張メソッドは `COM3D2.MotionTimelineEditor.Plugin` 名前空間の `Extensions` に定義されているため、エイリアスだけでは解決できない）。

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
```

`MTEP.` 付きの既存参照はそのまま残す（両方あっても曖昧参照は起きない。`Config` など同名型が `COM3D2.SceneEditor.Plugin` 側にもあるが、本ファイルでは `MTEP.Config` と明示しているため衝突しない）。ビルド時に CS0104（曖昧参照）が出た場合は、その型だけ `MTEP.` を付ける。

- [ ] **Step 2: 定数を整理する**

`KeyFrameInspector.cs:33-46` の定数群を次のように変える。

削除する定数:
```csharp
/// <summary>フロー要素内のラベル幅</summary>
private const float FlowLabelWidth = 70f;
/// <summary>フロー要素内の数値入力欄の幅 (リセットボタンは含まない)</summary>
private const float FlowFieldWidth = 60f;
/// <summary>ブロック内容の左インデント</summary>
private const float BlockIndent = 8f;
private const float ColorSensitivity = 0.01f;
```

追加する定数（`MinHeaderLabelWidth` の直後）:
```csharp
/// <summary>文字列値のラベル幅 (「ﾎﾟｰｽﾞ名」等が収まる幅)</summary>
private const float StrLabelWidth = 70f;
```

`PositionSensitivity` / `RotationSensitivity` / `ScaleSensitivity` は Transform 行で引き続き使うので残す。

- [ ] **Step 3: クラス summary を更新する**

`KeyFrameInspector.cs:9-16` の summary を以下に置き換える。

```csharp
/// <summary>
/// 選択中キーフレームの詳細表示・編集。
/// 選択キーフレームごとに折りたたみ可能なブロックを縦に並べ、
/// Transform は標準 Inspector と同じ横並び行、色・カスタム値・表示・文字列値は
/// タイムライン項目の現在値 UI (TimelineItemInspector) と同じ部品で 1 値 1 行に描く
/// (「補間曲線」タブに KeyFrameTangentDrawer のタンジェント曲線エディタを表示する。
/// 区間ごとの実値カーブ編集は TimelineCurveEditor が担当)
/// </summary>
```

- [ ] **Step 4: DrawBoneBlock の呼び出し順を変える**

`KeyFrameInspector.cs:229-244` の `DrawBoneBlock` 末尾を以下にする。

```csharp
var transform = bone.transform;
DrawTransform(view, bone, transform);
DrawColorRow(view, bone, transform);
DrawCustomValues(view, bone, transform);
DrawStrValues(view, bone, transform);
```

- [ ] **Step 5: DrawTransform から色の分岐を外す**

`KeyFrameInspector.cs:371-377` の `if (transform.hasColor) { DrawVector3Row(... "色" ...) }` ブロックを削除する。summary も「位置 / 回転 / 拡縮 を標準 Inspector と同じ横並び行で描く」に直す。

- [ ] **Step 6: 色の行を追加する**

`DrawVector3Row`（`:380-409`）の直後に追加する。

```csharp
/// <summary>
/// 色をカラーピッカー付きの行で描く (現在値 UI と同じ部品)。
/// ColorPickerWindow はラベル文字列で編集対象を同定するため、
/// 同名ボーンの別フレームと混ざらないようフレーム番号を含めて一意にする。
/// 変更前も RGB のみ編集していたのでアルファは扱わない。
/// コールバックは ColorPickerWindow の描画中に呼ばれるが、bone.transform は
/// PostEffectManager.GetXxxData() のような使い捨てではなく永続参照なので直接書いてよい
/// (PostEffectRowDrawer.ApplyColorImmediate の回避策は不要)
/// </summary>
private void DrawColorRow(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
{
    if (!transform.hasColor)
    {
        return;
    }

    var fieldCache = view.GetColorFieldCache(
        string.Format("{0} (F{1})/色", bone.name, bone.frameNo), false);
    view.DrawColor(fieldCache, transform.color, transform.initialColor, newValue =>
    {
        transform.color = newValue;
        Apply(bone);
    });
}
```

- [ ] **Step 7: フロー配置を 1 値 1 行へ置き換える**

`KeyFrameInspector.cs:411-567`（`GetFlowItemWidth` / `DrawFlowValues` / `BeginFlowItem` / `DrawCustomValueItem` / `DrawVisibleItem`）を丸ごと削除し、以下に置き換える。

```csharp
/// <summary>カスタム値と表示トグルを 1 値 1 行で描く</summary>
private void DrawCustomValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
{
    foreach (var pair in transform.GetCustomValueInfoMap())
    {
        if (!transform.HasCustomValue(pair.Key))
        {
            continue;
        }
        DrawCustomValueRow(view, bone, transform, pair.Key, pair.Value);
    }

    if (transform.hasVisible)
    {
        view.DrawToggle("表示", transform.visible, -1, RowHeight, newValue =>
        {
            transform.visible = newValue;
            Apply(bone);
        });
    }
}

/// <summary>
/// カスタム値 1 個。現在値 UI (各 RowDrawer) と同じ DrawCustomValue* を使い、
/// bool 相当はトグル、整数相当は int スライダー、それ以外は float スライダー / 数値欄にする。
/// リセット (R) は info.defaultValue へ戻し、GetDefaultCustomValue と同じ値になる
/// </summary>
private void DrawCustomValueRow(
    GUIView view,
    MTEP.BoneData bone,
    MTEP.ITransformData transform,
    string customKey,
    MTEP.CustomValueInfo info)
{
    var value = transform.GetCustomValue(customKey).value;

    switch (info.type)
    {
        case MTEP.CustomValueType.BoolValue:
            view.DrawCustomValueBool(info, value != 0f, newValue =>
            {
                transform.GetCustomValue(customKey).value = newValue ? 1f : 0f;
                Apply(bone);
            });
            break;

        case MTEP.CustomValueType.IntValue:
            view.DrawCustomValueInt(info, Mathf.RoundToInt(value), newValue =>
            {
                transform.GetCustomValue(customKey).value = newValue;
                Apply(bone);
            });
            break;

        default:
            view.DrawCustomValueFloat(info, value, newValue =>
            {
                transform.GetCustomValue(customKey).value = newValue;
                Apply(bone);
            });
            break;
    }
}
```

- [ ] **Step 8: 文字列値のインデントを外す**

`DrawStrValues`（`:569-603`）を以下にする。

```csharp
/// <summary>文字列値は幅が読めないので 1 行ずつ全幅で描く</summary>
private void DrawStrValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
{
    foreach (var pair in transform.GetStrValueInfoMap())
    {
        var strKey = pair.Key;
        if (!transform.HasStrValue(strKey))
        {
            continue;
        }

        var value = transform.GetStrValue(strKey);

        view.DrawTextField(
            transform.GetStrValueName(strKey),
            StrLabelWidth,
            value,
            -1,
            RowHeight,
            newValue =>
            {
                if (newValue == value)
                {
                    return;
                }
                transform.SetStrValue(strKey, newValue);
                Apply(bone);
            });
    }
}
```

- [ ] **Step 9: KeyFrameFlowLayout とそのテストを削除する**

```bash
git rm source/COM3D2.SceneEditor.Plugin/KeyFrameFlowLayout.cs source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameFlowLayoutTests.cs
```

プラグイン本体の csproj は旧形式（`ToolsVersion="15.0"`）で `Compile Include` を明示しているため、`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:285` の次の行を削除する（`GameVersion` で分岐しない単一の ItemGroup なので 1 箇所のみ）。

```xml
    <Compile Include="KeyFrameFlowLayout.cs" />
```

テスト側の csproj は SDK 形式（ワイルドカード収集）なので編集不要。最後に `grep -rn KeyFrameFlowLayout source` で参照が残っていないことを確認する（0 件であること）。

- [ ] **Step 10: 未使用の残骸が無いか確認する**

```bash
grep -n "FlowLabelWidth\|FlowFieldWidth\|BlockIndent\|ColorSensitivity\|GetFlowItemWidth\|BeginFlowItem\|DrawCustomValueItem\|DrawVisibleItem\|DrawFlowValues\|ToVector3\|ToColor" source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs
```

Expected: 0 件。

- [ ] **Step 11: 両構成をビルドする**

`W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\.env` の `COM3D2_DIR` / `COM3D25_DIR` を読んで MSBuild を直接叩く（`debug.bat` は使わない）。

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSB="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=<.env の値>" "/p:COM3D25_DIR=<.env の値>" /v:m
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=<.env の値>" "/p:COM3D25_DIR=<.env の値>" /v:m
```

Expected: 両方とも `0 エラー`。CS0104（曖昧参照）が出たら該当型に `MTEP.` を付ける。

- [ ] **Step 12: 単体テストを実行する**

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

Expected: 全 PASS（`KeyFrameFlowLayoutTests` が消えた以外の増減が無いこと）。

- [ ] **Step 13: 実機で見た目を確認する（ゲーム起動中のみ）**

MCP `com3d25-devbridge` の `ping` が通るなら、タイムラインでライトまたはポストエフェクトのキーフレームを選択した状態で `screenshot` を撮り、以下を確認する。

- カスタム値がスライダー行（ラベル 40 + 数値欄 + `<` `>` + スライダー + `R`）で 1 行ずつ並ぶ
- 色が「ラベル + 色見本 + 編集」の行になり、編集ボタンでカラーピッカーが開く。2 つのキーフレームを同時選択したとき、ピッカーの対象が混ざらない
- ピッカーを開いたまま別のキーフレームを選択に追加・削除して描画順が変わっても、ピッカーが別のキーフレームへ張り付かない（`ColorPickerWindow` はラベル不一致で自動的に閉じる想定）
- Transform 行（位置 / 回転 / 拡縮）は変更前と同じ
- ゲームが起動していなければこの Step は省略し、最終報告で「実機未確認」と明記する

- [ ] **Step 14: コミットする**

`commit` スキルを使う。メッセージ例:

```
refactor(inspector): キーフレーム詳細の値行を現在値 UI と同じ部品に揃える
```

---

### Task 2: 変更履歴を追記する

**Files:**
- Modify: `CHANGELOG.md`（「## 未リリース」節）

**Interfaces:** なし

- [ ] **Step 1: 未リリース節へ項目を追加する**

`CHANGELOG.md` の `## 未リリース` 直下の箇条書き末尾に追加する。

```markdown
- Inspector のキーフレーム詳細で、数値・色・表示の行がタイムライン項目の現在値編集と同じ見た目になりました
  - 数値はスライダー行、色はカラーピッカー付きの行で 1 値 1 行に並びます
  - 横に詰めて折り返す配置と、数値欄のドラッグ増減は廃止しました
```

- [ ] **Step 2: コミットする**

`commit` スキルを使う。メッセージ例:

```
docs(changelog): キーフレーム詳細の行スタイル統一を追記する
```

---

## Self-Review

- **Spec coverage:** 決定事項 1（1 値 1 行）→ Task 1 Step 7。決定事項 3・4（色ラベル一意・アルファ無し）→ Step 6。決定事項 5（インデント廃止）→ Step 7・8。`KeyFrameFlowLayout` 削除 → Step 9。変更履歴 → Task 2。
- **Placeholder scan:** コード断片はすべて実コード。MSBuild の `<.env の値>` は環境依存値で実行者が `.env` から読む（CLAUDE.md の手順どおり）。
- **Type consistency:** `DrawColorRow` / `DrawCustomValues` / `DrawCustomValueRow` / `DrawStrValues` の名前は Step 4 の呼び出しと Step 6〜8 の定義で一致。`StrLabelWidth` は Step 2 で定義し Step 8 で使用。`Apply(MTEP.BoneData)` は既存メソッド。
