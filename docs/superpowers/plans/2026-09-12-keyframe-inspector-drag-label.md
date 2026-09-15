# キーフレーム詳細の数値行ラベルをドラッグ可能にする 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inspector のキーフレーム詳細（`KeyFrameInspector` / `KeyFrameBatchDrawer`）のカスタム値行で、ラベル部分を左右ドラッグして数値を増減できるようにする。

**Architecture:** GUIView（MTEUtils サブモジュール）の `DrawFloatField` と `DrawSliderValue` に `dragSensitivity` オプションを追加し、0 より大きいときだけラベルを既存の `DrawDragLabel` で描く（オプトイン。既存呼び出しは挙動不変）。プラグイン側は `Timeline/Extensions.cs` の `DrawCustomValueFloat / Int` から感度を渡すだけで、キーフレーム詳細と現在値 UI の両方が同時にドラッグ対応になる。

**Tech Stack:** C# (.NET 3.5 / net48 の 2 構成)、Unity IMGUI ラッパー `GUIView`（`source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs`、git submodule `kidonaru/COM3D2.MTEUtils` master）

**Spec:** 専用の spec ファイルは無い。本計画の「背景と決定事項」節が仕様。調査元は会話ログ（2026-09-12 の Inspector ドラッグ調査）。

## Global Constraints

- **git worktree を使わない**。メインの作業ディレクトリで作業する
- **COM3D2 / COM3D25 の両構成をビルドする**。MSBuild を直接叩き、`debug.bat` は使わない（ゲーム停止中に実行すると実機へ DLL がコピーされる）
- コメント・ログ文言は日本語
- `deploy.bat` / `deploy.ps1` / `release.bat` は実行しない
- コミットメッセージは Conventional Commits 形式の日本語。`commit` スキルで行う
- MTEUtils はサブモジュール。GUIView の変更は **先にサブモジュール内でコミット**し、親リポジトリでポインタ更新を含めてコミットする（既存例: `9618c3f fix(gizmo): 回転ギズモの掴み判定を広げた MTEUtils を取り込む`）
- GUIView は UnityEngine 依存のため xUnit テストは書けない。検証は両構成ビルドと実機確認（ゲーム起動中なら `com3d25-devbridge` の `screenshot`）で行う

---

## 背景と決定事項

### 現状

| 部品 | 場所 | ドラッグ |
|---|---|---|
| `DrawDragLabel` | `GUIView.cs:1174` | 左右ドラッグで差分を返すラベル。Shift で 0.1 倍。hotControl でウィンドウ移動を抑止 |
| `DrawVector3Row` | `GUIView.cs:1495` | X/Y/Z がドラッグラベル。キーフレーム詳細の位置/回転/拡縮は既に対応済み |
| `DrawFloatField` | `GUIView.cs:1387` | ラベルは `DrawTextField` 内の `GUI.Label` 直描き。**非対応** |
| `DrawSliderValue` | `GUIView.cs:2767` | ラベルは `DrawLabel`。**非対応** |
| `DrawDragFloatField` / `DrawDragIntField` | `GUIView.cs:1836` / `1899` | ドラッグラベル + 数値欄のセット。キーフレーム詳細では 2026-09-09 の行スタイル統一で不使用になった |

キーフレーム詳細のカスタム値行（`KeyFrameInspector.cs:494` / `KeyFrameBatchDrawer.cs:317` の `DrawCustomValueRow`）は型ごとに次の経路を通る。

- `FloatValue`（数値欄のみ）: `DrawCustomValueFloat` → `DrawFloatField`
- `FloatSlider`: `DrawCustomValueFloat` → `DrawSliderValue`
- `IntValue`: `DrawCustomValueInt` → `DrawSliderValue`（一括編集側は `DrawSliderValue` を直接呼ぶ）
- `BoolValue` / 文字列: 対象外

### 決定事項

1. **オプトイン方式**。`DrawFloatField` 全体を無条件でドラッグ化しない。ラベルが hotControl 持ちに変わると、既存の全呼び出しでラベルクリックの挙動が変わるため
2. **感度の既定**
   - `FloatValue`: `GUIView.DefaultFloatDragSensitivity`（0.01 / px）
   - `FloatSlider` / `IntValue`: レンジ基準。`(max - min) / SliderDragRangePixels`（200 px でレンジ端から端）。Int はさらに `DefaultIntDragSensitivity`（0.5）を下限にする
3. **Int はドラッグの端数を持ち越す**。`DrawDragIntField` と同じ `_intDragResidual` を使い、1 px 未満の移動を切り捨てない
4. **混在値（NaN）はドラッグしない**。一括編集で値が混在しているときは、数値入力かスライダー操作で明示的に揃えるまで変更なし（`DrawSliderValue` の混在ハンドル方針と同じ）
5. **クランプ**は `ClampValue`（min/max がともに 0 なら無制限）で `DrawFloatField` の流儀に揃える
6. 現在値 UI（各 RowDrawer / `LiveEffectWindow`）も `DrawCustomValue*` 経由なので同時にドラッグ対応になる。これは意図した副作用（Inspector 内で操作感を揃える）

---

### Task 1: `DrawFloatField` にドラッグラベルを追加する（MTEUtils）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs:1372-1456`（`FloatFieldOption` / `DrawFloatField`）

**Interfaces:**
- Consumes: `DrawDragLabel(string, float, float, float, Action<float>, GUIStyle, Action)`、`ClampValue(float, float, float)`、`FloatFieldCache.UpdateValue(float)`
- Produces: `FloatFieldOption.dragSensitivity`（float。0 以下でラベルは従来どおり）

- [ ] **Step 1: `FloatFieldOption` にフィールドを追加**

```csharp
        public struct FloatFieldOption
        {
            public string label;
            public float labelWidth;
            /// <summary>ラベルドラッグ 1px あたりの増減量。0 以下ならラベルはドラッグ不可</summary>
            public float dragSensitivity;
            public FloatFieldType fieldType;
            public float value;
            public float minValue;
            public float maxValue;
            public float width;
            public float height;
            public FloatFieldCache fieldCache;
            public Action<float> onChanged;
            public Action onReset;
        }
```

- [ ] **Step 2: `DrawFloatField` でドラッグラベルを先に描き、残りをラベル無しで描く**

`DrawFloatField` 本体を次に置き換える。変更点は `hasDragLabel` の分岐と、その場合に `label` を null にして `width` を詰める処理。

```csharp
        public bool DrawFloatField(FloatFieldOption option)
        {
            var fieldCache = option.fieldCache;
            if (fieldCache == null)
            {
                fieldCache = GetFieldCache(option.label, option.fieldType);
                fieldCache.UpdateValue(option.value);
            }

            var updated = false;

            Action<string> onChanged = null;
            if (option.onChanged != null)
            {
                onChanged = newText =>
                {
                    fieldCache.text = newText;

                    float newValue;
                    if (float.TryParse(newText, out newValue))
                    {
                        if (option.minValue != 0f || option.maxValue != 0f)
                        {
                            newValue = Mathf.Clamp(newValue, option.minValue, option.maxValue);
                        }
                        fieldCache.UpdateValue(newValue, false);
                        option.onChanged(newValue);
                        updated = true;
                    }
                };
            }

            var label = option.label;
            var width = option.width;

            // ドラッグラベルは DrawTextField のラベル描画の代わりに自前で描き、
            // 残り幅をラベル無しの入力欄へ渡す (幅の差し引きは DrawTextField と同じ式)
            var hasDragLabel = option.dragSensitivity > 0f
                && !string.IsNullOrEmpty(label)
                && option.onChanged != null;
            if (hasDragLabel)
            {
                var labelWidth = option.labelWidth > 0f ? option.labelWidth : this.labelWidth;
                var value = option.value;

                DrawDragLabel(label, labelWidth, option.height, option.dragSensitivity, delta =>
                {
                    // 混在 (NaN) はドラッグの起点が定まらないため変更しない
                    if (float.IsNaN(value)) return;

                    var newValue = ClampValue(value + delta, option.minValue, option.maxValue);
                    if (newValue == value) return;

                    value = newValue;
                    // ドラッグで変わった値を同じフレームの入力欄へ出すためキャッシュを更新する
                    fieldCache.UpdateValue(newValue);
                    option.onChanged(newValue);
                    updated = true;
                });

                label = null;
                width -= labelWidth + margin;
            }

            if (option.onReset != null)
            {
                var subViewRect = GetDrawRect(width, option.height);

                BeginSubView(subViewRect, LayoutDirection.Horizontal);
                {
                    var fieldWidth = subViewRect.width - 20;

                    subView.DrawTextField(
                        label,
                        option.labelWidth,
                        fieldCache.text,
                        fieldWidth,
                        option.height,
                        onChanged);

                    if (subView.DrawButton("R", 20, 20))
                    {
                        option.onReset();
                        updated = true;
                    }
                }
                EndSubView();
            }
            else
            {
                DrawTextField(
                    label,
                    option.labelWidth,
                    fieldCache.text,
                    width,
                    option.height,
                    onChanged);
            }

            return updated;
        }
```

注意: `ClampValue` は `DrawFloatField` より後ろ（`GUIView.cs:1957` 付近）で定義済みの private static メソッド。同一クラスなのでそのまま呼べる。

- [ ] **Step 3: 両構成ビルドが通ることを確認**

Run（リポジトリ直下で。`.env` の `COM3D2_DIR` / `COM3D25_DIR` を展開して渡す）:

```
cd source/COM3D2.SceneEditor.Plugin
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=<.env の値>" "/p:COM3D25_DIR=<.env の値>" /v:m
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=<.env の値>" "/p:COM3D25_DIR=<.env の値>" /v:m
```

Expected: 両方 `0 エラー`

- [ ] **Step 4: サブモジュール内でコミット**（Task 2 とまとめて 1 コミットにしてよい。Task 2 の Step 4 参照）

---

### Task 2: `DrawSliderValue` にドラッグラベルを追加する（MTEUtils）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs:2753-2850`（`SliderOption` / `DrawSliderValue`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUIView.cs:1893-1945`（`DrawDragIntField` の端数処理を helper へ切り出す）

**Interfaces:**
- Consumes: `DrawDragLabel`、`_intDragResidual`（既存の private static float）
- Produces: `SliderOption.dragSensitivity`（float。0 以下で従来どおり）、`private static bool TryTakeIntDragStep(float delta, out int step)`

- [ ] **Step 1: Int ドラッグの端数処理を helper に切り出す**

`DrawDragIntField` 内の `DrawDragLabel` 呼び出しを次に置き換え、helper を `_intDragResidual` の直後に追加する。

```csharp
        /// <summary>
        /// ドラッグ差分を端数込みで積み、整数 1 以上になった分だけ step として取り出す。
        /// 1px 未満の移動を切り捨てないための共通処理
        /// </summary>
        private static bool TryTakeIntDragStep(float delta, out int step)
        {
            _intDragResidual += delta;
            step = (int)_intDragResidual;
            if (step == 0) return false;

            _intDragResidual -= step;
            return true;
        }
```

```csharp
            DrawDragLabel(option.label, labelWidth, height, sensitivity, delta =>
            {
                int step;
                if (!TryTakeIntDragStep(delta, out step)) return;

                var newValue = (int)ClampValue(value + step, option.minValue, option.maxValue);
                if (newValue == value) return;

                value = newValue;
                option.onChanged?.Invoke(newValue);
                updated = true;
            },
            onDragStart: () => _intDragResidual = 0f);
```

- [ ] **Step 2: `SliderOption` にフィールドを追加**

```csharp
        public struct SliderOption
        {
            public string label;
            public float labelWidth;
            /// <summary>ラベルドラッグ 1px あたりの増減量。0 以下ならラベルはドラッグ不可</summary>
            public float dragSensitivity;
            public float width;
            public FloatFieldType fieldType;
            public float min;
            public float max;
            public float step;
            public float defaultValue;
            public float value;
            public bool hiddenResetButton;
            public Action<float> onChanged;
        }
```

- [ ] **Step 3: `DrawSliderValue` のラベル描画を差し替える**

`var label = fieldCache.label; if (!string.IsNullOrEmpty(label)) { subView.DrawLabel(...); sliderWidth -= option.labelWidth; }` の部分を次に置き換える。

```csharp
                var label = fieldCache.label;
                if (!string.IsNullOrEmpty(label))
                {
                    if (option.dragSensitivity > 0f)
                    {
                        var isInt = option.fieldType == FloatFieldType.Int;
                        subView.DrawDragLabel(label, option.labelWidth, 20, option.dragSensitivity, delta =>
                        {
                            // 混在 (NaN) はドラッグの起点が定まらないため変更しない
                            if (float.IsNaN(newValue)) return;

                            if (isInt)
                            {
                                int step;
                                if (!TryTakeIntDragStep(delta, out step)) return;
                                delta = step;
                            }
                            newValue = Mathf.Clamp(newValue + delta, option.min, option.max);
                        },
                        onDragStart: () => _intDragResidual = 0f);

                        // ドラッグで変わった値を同じフレームの入力欄へ出すためキャッシュを更新する
                        fieldCache.UpdateValue(newValue);
                    }
                    else
                    {
                        subView.DrawLabel(label, option.labelWidth, 20);
                    }
                    sliderWidth -= option.labelWidth;
                }
```

続く `subView.DrawFloatField(...)` は変更しない。`fieldCache` を渡している呼び出しでは `DrawFloatField` は `option.value` を参照せず、表示は上の `fieldCache.UpdateValue(newValue)` で反映されるため。

`_intDragResidual` の既存コメントを次に差し替え、共有が安全な根拠を残す。

```csharp
        /// <summary>
        /// int ドラッグの端数。IMGUI の hotControl は単一で、ドラッグは同時に 1 つしか成立しないため、
        /// DrawDragIntField と DrawSliderValue (Int) が同じ端数を共有しても汚染し合わない
        /// </summary>
        private static float _intDragResidual = 0f;
```

- [ ] **Step 4: 両構成ビルドが通ることを確認**

Task 1 Step 3 と同じ 2 コマンド。Expected: 両方 `0 エラー`

- [ ] **Step 5: サブモジュール内でコミット**

```bash
cd source/COM3D2.SceneEditor.Plugin/MTEUtils
git add GUIView.cs
# commit スキルで。メッセージ例:
# feat(gui): DrawFloatField / DrawSliderValue のラベルをオプトインでドラッグ可能にする
```

サブモジュールのブランチが `master` であることを `git branch --show-current` で確認してからコミットする（detached HEAD だとポインタ更新後に迷子になる）。

---

### Task 3: キーフレーム詳細と現在値 UI から感度を渡す（プラグイン側）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs:210-282`（`DrawCustomValueFloat` / `DrawCustomValueInt`）
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameBatchDrawer.cs:340-355`（Int 行の `DrawSliderValue` 直接呼び出し）

**Interfaces:**
- Consumes: `FloatFieldOption.dragSensitivity`、`SliderOption.dragSensitivity`、`GUIView.DefaultFloatDragSensitivity`、`GUIView.DefaultIntDragSensitivity`
- Produces: `public static float SliderDragSensitivity(this GUIView view, float min, float max, bool isInt)`（`Timeline/Extensions.cs`）

- [ ] **Step 1: 感度計算 helper を `Extensions.cs` に追加**

`CustomFloatFieldWidth` 定数の直後に追加する。

```csharp
        /// <summary>スライダーのラベルドラッグでレンジ端から端まで動かすのに要する px</summary>
        private const float SliderDragRangePixels = 200f;

        /// <summary>
        /// スライダー行のラベルドラッグ感度。レンジ幅基準で、Int は 1 段ずつ動かせるよう下限を設ける
        /// </summary>
        public static float SliderDragSensitivity(float min, float max, bool isInt)
        {
            var sensitivity = Mathf.Abs(max - min) / SliderDragRangePixels;
            if (isInt)
            {
                return Mathf.Max(sensitivity, GUIView.DefaultIntDragSensitivity);
            }
            return sensitivity > 0f ? sensitivity : GUIView.DefaultFloatDragSensitivity;
        }
```

（拡張メソッドにする必要は無いので `this GUIView` は付けない。`Extensions` クラスは static class なのでそのまま置ける）

- [ ] **Step 2: `DrawCustomValueFloat` / `DrawCustomValueInt` に感度を渡す**

```csharp
            if (info.type == CustomValueType.FloatSlider)
            {
                return view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = info.name,
                    labelWidth = labelWidth,
                    dragSensitivity = SliderDragSensitivity(info.min, info.max, false),
                    width = sliderWidth,
                    ...（以下既存どおり）
```

```csharp
                return view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = info.name,
                    labelWidth = fieldLabelWidth,
                    dragSensitivity = GUIView.DefaultFloatDragSensitivity,
                    minValue = info.min,
                    ...（以下既存どおり）
```

```csharp
        public static bool DrawCustomValueInt(...)
        {
            return view.DrawSliderValue(new GUIView.SliderOption
            {
                label = info.name,
                labelWidth = labelWidth,
                dragSensitivity = SliderDragSensitivity(info.min, info.max, true),
                width = sliderWidth,
                ...（以下既存どおり）
```

- [ ] **Step 3: 一括編集の Int 行にも感度を渡す**

`KeyFrameBatchDrawer.cs` の `case MTEP.CustomValueType.IntValue:` 内の `DrawSliderValue` に 1 行追加する。

```csharp
                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = info.name,
                        labelWidth = CustomLabelWidth,
                        dragSensitivity = MTEP.Extensions.SliderDragSensitivity(info.min, info.max, true),
                        width = CustomSliderWidth,
```

`Extensions` の名前空間と静的クラス名は `Timeline/Extensions.cs` の先頭で確認し、`KeyFrameBatchDrawer.cs` の既存 using / エイリアス（`MTEP`）に合わせて修飾する。

- [ ] **Step 4: 両構成ビルドが通ることを確認**

Task 1 Step 3 と同じ 2 コマンド。Expected: 両方 `0 エラー`

- [ ] **Step 5: 実機確認（ゲーム起動中の場合のみ）**

`com3d25-devbridge` の `ping` が通るなら `screenshot` で次を確認する。起動していなければこのステップは省略し、最終報告で「実機未確認」と明記する。

- Inspector のキーフレーム詳細: カスタム値行（例: ライトの「強さ」スライダー、FloatValue 型の数値欄）のラベルをドラッグして値が変わる。Shift で微調整になる。ウィンドウが引きずられない
- 一括編集モードで値が混在（NaN）している行: ラベルをドラッグしても値が変わらない
- `DrawCustomValueFloat / Int` は 30 以上の RowDrawer（`BoneSliderRowDrawer` / `MaterialPropertyRowsDrawer` / `LiveEffectWindow` 等）からも呼ばれる。ラベル幅が狭い、または行幅が狭いレイアウトの代表として **ボーンスライダー行とマテリアル行の 2 箇所**でも、ラベル・数値欄・スライダーの幅が崩れていないこと、ドラッグが効くことを確認する
- 感度の体感: スライダー行はレンジ端から端まで約 200 px、数値欄のみの行は 1 px = 0.01。速すぎる / 遅すぎるなら `SliderDragRangePixels` を調整し、最終報告に採用値を書く

- [ ] **Step 6: コードレビューと親リポジトリのコミット**

`code-review` スキルでレビューし、指摘を取り込んでから `commit` スキルでコミットする。

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs source/COM3D2.SceneEditor.Plugin/KeyFrameBatchDrawer.cs docs/superpowers/plans/2026-09-12-keyframe-inspector-drag-label.md
# メッセージ例:
# feat(inspector): キーフレーム詳細のカスタム値ラベルをドラッグで増減できるようにする
```

サブモジュールのポインタ更新（`source/COM3D2.SceneEditor.Plugin/MTEUtils`）を同じコミットに含める。

## レビュー却下メモ

- なし（plan-reviewer の指摘 4 件はすべて取り込み。`DrawSliderValue` の `value = newValue` 差し替えは無害だが意図が読めないため、変更自体を計画から外した）
