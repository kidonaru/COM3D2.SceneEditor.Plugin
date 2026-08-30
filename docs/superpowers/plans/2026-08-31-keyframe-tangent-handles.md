# キーフレーム詳細タンジェントエディタの操作性改良 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inspector「補間曲線」タブのタンジェント編集を、① 選択キーフレームが持たない値種別をコンボから隠す ② OutTangent/InTangent をドラッグ可能な数値欄にする ③ 曲線プレビュー内のハンドルをドラッグして In/Out を直接調整できる、の 3 点で TimelineCurveEditor と同等の操作性へ引き上げる。

**Architecture:** 既存の `KeyFrameTangentDrawer`（`source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`）を拡張する。① は `TimelineCurveEditor.UpdateAvailableValueTypes`/`HasValueType`（L423-455）と同じく `ITransformData.GetValueDataList(valueType).Length > 0` で候補を絞り、`GUIComboBox.items` を毎フレーム差し替える。② は `GUIView.DrawDragLabel` + `DrawFloatField` を組み合わせ、ラベルドラッグを「差分適用」に割り当てて複数選択（混在=NaN）でも相対編集を保つ（`DrawDragFloatField` は絶対値しか渡さず NaN 時に編集不能になるため使わない）。③ は `TimelineCurveEditor.HandleInput`/`UpdateDrag`/`GetHandlePos`/`DrawChannelHandles`（L741-1005, L1650-1705）の移植。ただし本エディタは**正規化空間**（区間始点 (0,0)・終点 (1,1)、`normalizedValue` がそのまま画面勾配）なので、フレーム幅・値スケールの換算は不要で slope = dy/dx がそのまま `normalizedValue` になる。純粋計算（ハンドル座標・マウス座標→正規化タンジェント）は `KeyFrameTangentLogic` へ置き、xUnit でテストする。

**Tech Stack:** C# (net35/net48, 旧形式 csproj), Unity IMGUI (`GUIView`), Texture2D への CPU ピクセル描画, xUnit (net48)

**Spec:** 正式な spec なし。ユーザー依頼（原文）:
1. 「タイムラインウィンドウのカーブエディタ同様に持っていない値はドロップダウンに表示しないように」
2. 「OutTangentとInTangentはドラッグ可能なテキストエリアで表示」
3. 「カーブエディタ参考にグラフ中にハンドルを表示してIn/Out調整可能に」

**前提となる既存実装:** 直前の作業（コミット `bf565a1`〜`ca71fe8`）で `KeyFrameTangentDrawer` / `KeyFrameTangentLogic` と Inspector のタブ分けが入っている。本計画はその続き。

## Global Constraints

- `deploy.bat` / `deploy.ps1` / `debug.bat` / `release.bat` は実行禁止。ビルドは MSBuild 直叩きのみ
- **ビルド順が重要**: COM3D2 構成のビルドは COM3D25 の出力ディレクトリを消すため、**必ず COM3D2 → COM3D25 の順**でビルドする（テストは COM3D25 出力を参照する）
- MSBuild は Git Bash から呼ぶ場合 `/p:` ではなく `-p:` を使う（`/p:` は MSYS のパス変換で壊れる）
- コードのコメント・ログメッセージは日本語
- 新規 .cs は csproj（旧形式）へ `<Compile Include>` を手動追加
- 既存の失敗テスト 4 件（`EyesItemInspectorTests.ResolveRowKind` の `EyesPosL/R`・`EyesScaL/R`）は本作業以前からの既知失敗。**新規の失敗を出さないこと**が合格条件
- 履歴は `timelineManager.RequestHistory(説明)` を使う（`AddHistory` 直呼び禁止。ドラッグ中の連続変更が 1 件に集約される）
- `GUIView.GetFieldCache` は描画順インデックスで採番するため、同一フレーム内で描画要素数を変える操作は避ける。本計画で要素数が変わるのはコンボ候補の増減のみで、これは選択変更時にしか起きない

ビルドコマンド（以後「両構成ビルド」と呼ぶ。作業ディレクトリは `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`、`MSB` は `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`）:

```bash
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -v:m
"$MSB" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -v:m
```

テスト実行:

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

---

### Task 1: 値種別コンボを選択キーフレームが持つものだけに絞る

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`（`_valueTypeComboBox` の初期化部と `DrawTangentFields`）

**Interfaces:**
- Consumes: `MTEP.ITransformData.GetValueDataList(MTEP.TangentValueType)`（`Timeline/TransformData/ITransformData.cs`、非対応なら長さ 0 の配列を返す）
- Produces: `KeyFrameTangentDrawer` 内 private `void UpdateAvailableValueTypes()` — `_availableValueTypes`（`List<MTEP.TangentValueType>`）を作り直し、現在の `_tangentValueType` が候補から消えていたら `すべて` へ戻す

- [ ] **Step 1: 候補リストとフィルタ処理を追加**

`KeyFrameTangentDrawer` のフィールド宣言部（`_tangentValueType` の直後）へ追加:

```csharp
        /// <summary>選択キーフレームが実際に持つ値種別だけを入れたコンボ候補</summary>
        private readonly List<MTEP.TangentValueType> _availableValueTypes
            = new List<MTEP.TangentValueType>();
```

`_valueTypeComboBox` の初期化から `items = Enum.GetValues(...)` の行を削除し、`getName` と `buttonSize` だけ残す（候補は毎フレーム差し替えるため）:

```csharp
        private readonly GUIComboBox<MTEP.TangentValueType> _valueTypeComboBox =
            new GUIComboBox<MTEP.TangentValueType>
            {
                getName = (type, index) => type.ToString(),
                buttonSize = new Vector2(100, 20),
            };
```

`ForEachInTangent` の直前へメソッドを 2 つ追加（`TimelineCurveEditor.UpdateAvailableValueTypes` / `HasValueType` と同じ考え方）:

```csharp
        /// <summary>
        /// コンボ候補を選択キーフレームが実際に持つ値種別だけに絞る。
        /// 「すべて」は常に候補に残し、選択中の種別が消えたらそこへ戻す
        /// (TimelineCurveEditor.UpdateAvailableValueTypes と同じ流儀)
        /// </summary>
        private void UpdateAvailableValueTypes()
        {
            _availableValueTypes.Clear();
            _availableValueTypes.Add(MTEP.TangentValueType.すべて);

            foreach (MTEP.TangentValueType valueType in Enum.GetValues(typeof(MTEP.TangentValueType)))
            {
                if (valueType != MTEP.TangentValueType.すべて && HasValueType(valueType))
                {
                    _availableValueTypes.Add(valueType);
                }
            }

            if (!_availableValueTypes.Contains(_tangentValueType))
            {
                _tangentValueType = MTEP.TangentValueType.すべて;
            }
        }

        /// <summary>選択キーフレームのいずれかが指定種別の値を持つか</summary>
        private static bool HasValueType(MTEP.TangentValueType valueType)
        {
            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform != null && transform.GetValueDataList(valueType).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }
```

- [ ] **Step 2: 描画時に候補を差し替える**

`Draw` の先頭、`CollectTangents()` 呼び出しの**前**に 1 行入れる（候補が確定してからタンジェントを集めるため）:

```csharp
        public bool Draw(GUIView view)
        {
            UpdateAvailableValueTypes();

            if (!CollectTangents())
            {
                return false;
            }
```

`DrawTangentFields` のコンボ描画部を、候補リストとインデックスの対応へ直す（`_availableValueTypes` は `TangentValueType` の値と添字が一致しないため `(int)_tangentValueType` は使えない）:

```csharp
            _valueTypeComboBox.items = _availableValueTypes;
            _valueTypeComboBox.currentIndex =
                Mathf.Max(0, _availableValueTypes.IndexOf(_tangentValueType));
            _valueTypeComboBox.onSelected = (type, index) =>
            {
                _tangentValueType = type;
            };
            _valueTypeComboBox.DrawButton(subView);
```

- [ ] **Step 3: 両構成ビルド**

Run: 両構成ビルド（COM3D2 → COM3D25 の順）
Expected: 両方成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs
git commit -m "feat(timeline): タンジェントの値種別コンボを持っている値だけに絞る"
```

---

### Task 2: Out/In をドラッグ可能な数値欄にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`（`DrawTangentFields`）

**Interfaces:**
- Consumes: `GUIView.DrawDragLabel(string text, float width, float height, float sensitivity, Action<float> onDelta, GUIStyle style = null, Action onDragStart = null)`、`GUIView.DrawFloatField(GUIView.FloatFieldOption)`、`GUIView.GetFieldCache(string label, FloatFieldType fieldType = FloatFieldType.Float)`
- Produces: `KeyFrameTangentDrawer` 内 private `void DrawTangentRow(GUIView view, string label, float value, Action<float> onDiff, Action<float> onValue)` — ラベルドラッグで `onDiff`（差分）、数値入力で `onValue`（絶対値）を呼ぶ 1 行

**背景（なぜ `DrawDragFloatField` を使わないか）:** `GUIView.DrawDragFloatField` はラベルドラッグの結果を `value + delta` の**絶対値**で `onChanged` に渡す。複数選択で値が混在しているとき本エディタは `value` に `float.NaN` を入れるため、`NaN + delta = NaN` となり `DrawFloatField` 側の `!float.IsNaN` ガードで握り潰され、**混在時にドラッグ編集が一切できなくなる**。差分をそのまま受け取れる `DrawDragLabel` を直接使い、既存の「混在でも相対編集はできる」挙動（MTE 原典の `onDiffChanged` 相当）を保つ。

- [ ] **Step 1: 行描画メソッドを追加**

`DrawTangentFields` の直後へ追加:

```csharp
        /// <summary>
        /// タンジェント 1 行。ラベルを左右ドラッグすると差分編集、数値欄への入力で絶対値編集。
        /// value が NaN (複数選択で混在) でも差分編集を残したいため、
        /// 絶対値しか渡さない DrawDragFloatField ではなく DrawDragLabel を直接使う
        /// </summary>
        private void DrawTangentRow(
            GUIView view, string label, float value, Action<float> onDiff, Action<float> onValue)
        {
            view.BeginHorizontal();
            {
                view.DrawDragLabel(label, TangentLabelWidth, RowHeight, DragSensitivity, onDiff);

                var fieldCache = view.GetFieldCache(label);
                fieldCache.UpdateValue(value);
                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    value = value,
                    width = TangentFieldWidth,
                    height = RowHeight,
                    fieldCache = fieldCache,
                    onChanged = onValue,
                });
            }
            view.EndLayout();
        }
```

定数を他の定数宣言の並びへ追加:

```csharp
        /// <summary>タンジェント行のラベル幅 ("OutTangent" が収まる幅)</summary>
        private const float TangentLabelWidth = 80f;
        /// <summary>タンジェント行の数値入力欄の幅</summary>
        private const float TangentFieldWidth = 50f;
        /// <summary>ラベルドラッグ 1px あたりのタンジェント増減量</summary>
        private const float DragSensitivity = 0.01f;
```

- [ ] **Step 2: DrawTangentFields を差し替える**

`DrawTangentFields` の中の「OutTangent / InTangent の `DrawLabel` + `SetEnabled` + `DrawFloatSelect` の 2 セット」（コンボ描画の直後から `subView.SetEnabled(subView.focusedComboBox == null);` まで）を、以下へ置き換える。新値・差分の適用ブロック（`if (!float.IsNaN(newOutTangent) && ...)` 以降）はそのまま残す:

```csharp
            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            var newOutTangent = outTangent;
            var newInTangent = inTangent;
            var diffOutTangent = 0f;
            var diffInTangent = 0f;

            // コンボ展開中は下の行を触らせない (ポップアップが重なるため)
            subView.SetEnabled(subView.focusedComboBox == null);

            DrawTangentRow(subView, "OutTangent", outTangent,
                diff => diffOutTangent += diff,
                value => newOutTangent = value);

            DrawTangentRow(subView, "InTangent", inTangent,
                diff => diffInTangent += diff,
                value => newInTangent = value);

            subView.SetEnabled(true);
```

（`hasAnyTangent` による無効化は不要になるため削除する。`_workTangents` が空なら `Draw` 手前の `CollectTangents` が既に false を返しているか、値が NaN のままで適用ブロックが空振りするだけで実害がない）

- [ ] **Step 3: 両構成ビルド**

Run: 両構成ビルド（COM3D2 → COM3D25 の順）
Expected: 両方成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs
git commit -m "feat(timeline): Out/In タンジェントをドラッグ可能な数値欄にする"
```

---

### Task 3: ハンドル座標と入力→タンジェント変換の純粋ロジック（テスト付き）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentLogic.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameTangentLogicTests.cs`

**Interfaces:**
- Produces:
  - `public static Vector2 GetHandlePos(bool isOut, float normalizedValue, float size, float handleLength)` — 曲線プレビュー内（左上原点・Y 下向きの GUI 座標、一辺 `size` px）でのハンドル先端位置。out は始点 (0, size)、in は終点 (size, 0) から、正規化勾配 `normalizedValue` 方向へ `handleLength` px 伸ばした点
  - `public static bool TryGetNormalizedTangent(bool isOut, Vector2 mouse, float size, out float normalizedValue)` — プレビュー内のマウス位置（GUI 座標）から正規化タンジェントを求める。out は始点より右、in は終点より左でなければ `false`。求めた値が NaN / Infinity なら `false`

**座標系の前提（間違えやすいので明記）:**
- 曲線プレビューは**正規化空間**。区間の始点が値 0、終点が値 1、`normalizedValue` は「1 = 線形勾配」の無次元比なので、正規化空間では**勾配そのもの**（dy/dx）になる
- テクスチャは `UpdateTangentTexture` が `SetPixel(x, HermiteSimplified(...) * height)` で描くため値は上向き。GUI 座標は Y 下向きなので、値 v の画面 Y は `size - v * size`
- したがって始点 (値 0) は GUI 座標 `(0, size)`、終点 (値 1) は `(size, 0)`

- [ ] **Step 1: 失敗するテストを書く**

`KeyFrameTangentLogicTests.cs` の末尾（クラス内）へ追加:

```csharp
        [Fact]
        public void ハンドル位置_Out側は始点から右上へ伸びる()
        {
            // 勾配 1 (線形) なら右へ 1・上へ 1 の向き。長さ√2 の対角線ぶん進む
            var pos = KeyFrameTangentLogic.GetHandlePos(true, 1f, 100f, Mathf.Sqrt(2f));
            Assert.Equal(1f, pos.x, 3);
            Assert.Equal(99f, pos.y, 3);
        }

        [Fact]
        public void ハンドル位置_勾配0なら水平に伸びる()
        {
            var pos = KeyFrameTangentLogic.GetHandlePos(true, 0f, 100f, 10f);
            Assert.Equal(10f, pos.x, 3);
            Assert.Equal(100f, pos.y, 3);
        }

        [Fact]
        public void ハンドル位置_In側は終点から左下へ伸びる()
        {
            var pos = KeyFrameTangentLogic.GetHandlePos(false, 0f, 100f, 10f);
            Assert.Equal(90f, pos.x, 3);
            Assert.Equal(0f, pos.y, 3);
        }

        [Fact]
        public void マウス位置から正規化タンジェントを求める()
        {
            // 始点 (0,100) から右へ 50・上へ 50 の点は勾配 1
            Assert.True(KeyFrameTangentLogic.TryGetNormalizedTangent(
                true, new Vector2(50f, 50f), 100f, out var value));
            Assert.Equal(1f, value, 3);
        }

        [Fact]
        public void マウスが逆側なら求まらない()
        {
            // Out ハンドルは始点より右側だけが有効
            Assert.False(KeyFrameTangentLogic.TryGetNormalizedTangent(
                true, new Vector2(-5f, 50f), 100f, out _));
            // In ハンドルは終点より左側だけが有効
            Assert.False(KeyFrameTangentLogic.TryGetNormalizedTangent(
                false, new Vector2(150f, 50f), 100f, out _));
        }
```

ファイル先頭の using へ `using UnityEngine;` を追加する。

- [ ] **Step 2: テストが失敗することを確認**

Run: 両構成ビルド（COM3D2 → COM3D25 の順）→ `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: `GetHandlePos` / `TryGetNormalizedTangent` が未定義でテストプロジェクトがコンパイル失敗

- [ ] **Step 3: 実装**

`KeyFrameTangentLogic.cs` の using へ `using UnityEngine;` を足し、`GetUniformTangents` の後ろへ追加:

```csharp
        /// <summary>
        /// 曲線プレビュー内のハンドル先端位置 (左上原点・Y 下向きの GUI 座標)。
        /// プレビューは正規化空間なので、normalizedValue がそのまま勾配 (dy/dx) になる。
        /// out は始点 (0, size)、in は終点 (size, 0) から handleLength px 伸ばす
        /// </summary>
        public static Vector2 GetHandlePos(
            bool isOut, float normalizedValue, float size, float handleLength)
        {
            var origin = isOut ? new Vector2(0f, size) : new Vector2(size, 0f);
            if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue))
            {
                normalizedValue = 0f;
            }

            // 値は上向き、GUI の Y は下向きなので符号を反転する
            var dir = new Vector2(1f, -normalizedValue).normalized;
            if (!isOut)
            {
                dir = -dir;
            }
            return origin + dir * handleLength;
        }

        /// <summary>
        /// プレビュー内のマウス位置 (GUI 座標) から正規化タンジェントを求める。
        /// out ハンドルは始点より右、in ハンドルは終点より左でないと勾配が定まらない
        /// </summary>
        public static bool TryGetNormalizedTangent(
            bool isOut, Vector2 mouse, float size, out float normalizedValue)
        {
            normalizedValue = 0f;

            var origin = isOut ? new Vector2(0f, size) : new Vector2(size, 0f);
            var dx = (mouse.x - origin.x) / size;
            // GUI の Y は下向きなので、値の増加方向へ戻す
            var dy = (origin.y - mouse.y) / size;

            if (isOut ? dx <= 0f : dx >= 0f)
            {
                return false;
            }

            var value = dy / dx;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            normalizedValue = value;
            return true;
        }
```

- [ ] **Step 4: 両構成ビルド + テスト**

Run: 両構成ビルド（COM3D2 → COM3D25 の順）→ `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功。新規 5 テストが PASS。失敗は既知の `EyesItemInspectorTests` 4 件のみ

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentLogic.cs source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameTangentLogicTests.cs
git commit -m "feat(timeline): タンジェントハンドルの座標計算ロジックを追加する"
```

---

### Task 4: 曲線プレビューにハンドルを描いてドラッグ編集する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`（`Draw` のテクスチャ描画部）

**Interfaces:**
- Consumes: `KeyFrameTangentLogic.GetHandlePos` / `TryGetNormalizedTangent`（Task 3）、`GUIView.texWhite`（`public static Texture2D`）、`GUIView.currentPos`（代入で次の要素位置を動かせる）、`GUIView.GetDrawRect(float x, float y, float width, float height)`、`UnityEngine.Input.GetMouseButton(0)`
- Produces: `KeyFrameTangentDrawer` 内 private `void DrawCurveArea(GUIView view)` — 曲線テクスチャ・ハンドル・入力処理をまとめて描く

**設計メモ:**
- ハンドルの表示値は代表値（`GetUniformTangents` の結果）。混在（NaN）のときは `GetHandlePos` 側で勾配 0 として扱われ水平に出る。ドラッグすると全選択キーフレームへ同じ値が入る（数値欄の絶対値入力と同じ挙動）。混在を色で区別はしない（`curveLineSmoothColor` は「自動補間」を表す色なので流用すると意味が混ざる。混在は数値欄が空欄になることで分かる）
- ドラッグ中の履歴は `RequestHistory`（マウスを離すまで 1 件に集約される）。`TimelineCurveEditor` のような明示的な `EndDrag` は不要
- 押下の消費（`e.Use()`）はハンドルを掴んだときだけにする。プレビュー上の空クリックまで消費すると Inspector のスクロールドラッグを妨げるため
- **無変化ガード必須**: `Draw` は 1 フレームに複数回（Layout / Repaint 等）呼ばれる。マウスが動いていないイベントでも `Input.GetMouseButton(0)` は真のままなので、値が変わっていないなら `ApplyCurrentFrame` を呼ばない（`TimelineCurveEditor.UpdateDrag` L897 と同じガード）
- **コンボ展開中は入力を受けない**: 値種別コンボのポップアップは別ウィンドウとしてプレビューの上に重なりうる。`focusedComboBox != null` の間はハンドルの掴み・ドラッグを止める
- **ハンドル描画は `currentPos` を直接操作する**（`TimelineCurveEditor.DrawChannelHandles` と同じ方式）。`layoutMaxPos.y` は `Math.Max` 更新なので後退しないが、これは「ハンドルがテクスチャ矩形内に収まる」前提に暗黙依存している。`IsInCurveArea` でクリップしていることをコメントで明示し、描画後は `currentPos` をテクスチャ下端へ戻す

- [ ] **Step 1: 定数とドラッグ状態を追加**

定数の並びへ追加:

```csharp
        /// <summary>ハンドル線の長さ (px)</summary>
        private const float HandleLength = 30f;
        /// <summary>ハンドル先端のマーカー一辺 (px)</summary>
        private const float HandleMarkerSize = 6f;
        /// <summary>ハンドルを掴める距離 (px)</summary>
        private const float HandleHitRadius = 8f;
        /// <summary>ハンドル線を描く点の間隔 (px)</summary>
        private const float HandleSampleStep = 2f;
        /// <summary>ハンドルの色 (曲線より前に出したいので不透明寄りの白)</summary>
        private static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.8f);
```

フィールドの並びへ追加:

```csharp
        /// <summary>ドラッグ中のハンドル。null ならドラッグしていない</summary>
        private bool? _draggingIsOut = null;
```

- [ ] **Step 2: Draw からテクスチャ描画を切り出す**

`Draw` の `view.DrawTexture(_tangentTex);` を `DrawCurveArea(view);` へ置き換える。

- [ ] **Step 3: DrawCurveArea を実装**

`DrawPresets` の直前へ追加:

```csharp
        /// <summary>曲線テクスチャと In/Out ハンドルを描き、ハンドルのドラッグを処理する</summary>
        private void DrawCurveArea(GUIView view)
        {
            // テクスチャ左上のスクリーン座標。ハンドル位置とマウス位置の基準にする
            var texRect = view.GetDrawRect(
                view.currentPos.x, view.currentPos.y, CurveTexSize, CurveTexSize);
            var texPos = view.currentPos;

            view.DrawTexture(_tangentTex);

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            DrawHandle(view, texPos, true, outTangent);
            DrawHandle(view, texPos, false, inTangent);

            // ハンドルを描いたぶん currentPos が動いているので、テクスチャの下へ戻す
            view.currentPos = new Vector2(texPos.x, texPos.y + CurveTexSize + view.margin);

            HandleCurveInput(texRect, view.focusedComboBox != null);
        }

        /// <summary>ハンドル線と先端マーカーを小さな矩形の連続で描く (TimelineCurveEditor と同じ方式)</summary>
        private void DrawHandle(GUIView view, Vector2 texPos, bool isOut, float normalizedValue)
        {
            var origin = isOut
                ? new Vector2(0f, CurveTexSize)
                : new Vector2(CurveTexSize, 0f);
            var handlePos = KeyFrameTangentLogic.GetHandlePos(
                isOut, normalizedValue, CurveTexSize, HandleLength);
            var half = HandleMarkerSize * 0.5f;

            // 線分は小さな矩形の連続で描く (TimelineCurveEditor.DrawChannelHandles と同じ方式)。
            // currentPos を直接動かすので、IsInCurveArea でテクスチャ矩形の外へ出る点を
            // 落とし、レイアウトの縦幅がハンドルで伸びないようにする
            var steps = Mathf.CeilToInt(HandleLength / HandleSampleStep);
            for (var i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(origin, handlePos, i / (float)steps);
                if (!IsInCurveArea(p, 1f))
                {
                    continue;
                }
                view.currentPos = new Vector2(texPos.x + p.x - 1f, texPos.y + p.y - 1f);
                view.DrawTexture(GUIView.texWhite, HandleSampleStep, HandleSampleStep, HandleColor);
            }

            if (IsInCurveArea(handlePos, half))
            {
                view.currentPos = new Vector2(
                    texPos.x + handlePos.x - half, texPos.y + handlePos.y - half);
                view.DrawTexture(GUIView.texWhite, HandleMarkerSize, HandleMarkerSize, HandleColor);
            }
        }

        private static bool IsInCurveArea(Vector2 pos, float margin)
        {
            return pos.x >= margin && pos.x <= CurveTexSize - margin
                && pos.y >= margin && pos.y <= CurveTexSize - margin;
        }

        /// <summary>ハンドルの掴み・ドラッグ・離しを処理する</summary>
        private void HandleCurveInput(Rect texRect, bool comboBoxOpen)
        {
            var e = Event.current;
            var mouse = e.mousePosition - new Vector2(texRect.x, texRect.y);

            // 値種別コンボのポップアップは別ウィンドウとしてプレビューの上に重なりうるので、
            // 展開中はハンドル操作を受け付けない
            if (comboBoxOpen)
            {
                _draggingIsOut = null;
                return;
            }

            if (_draggingIsOut != null)
            {
                if (!Input.GetMouseButton(0))
                {
                    _draggingIsOut = null;
                    return;
                }

                var isOut = _draggingIsOut.Value;
                if (KeyFrameTangentLogic.TryGetNormalizedTangent(
                        isOut, mouse, CurveTexSize, out var newValue))
                {
                    ApplyTangent(isOut, newValue);
                }
                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                }
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 0)
            {
                return;
            }

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            for (var side = 0; side < 2; side++)
            {
                var isOut = side == 0;
                var handlePos = KeyFrameTangentLogic.GetHandlePos(
                    isOut, isOut ? outTangent : inTangent, CurveTexSize, HandleLength);
                if (Vector2.Distance(handlePos, mouse) > HandleHitRadius)
                {
                    continue;
                }

                _draggingIsOut = isOut;
                // 掴んだ押下だけ消費する。プレビュー上の空クリックまで消すと
                // Inspector のスクロールドラッグができなくなる
                e.Use();
                return;
            }
        }

        /// <summary>
        /// ハンドルドラッグの結果を選択キーフレーム全体へ適用する。
        /// Draw は 1 フレームに複数回呼ばれるので、値が変わっていないなら
        /// ApplyCurrentFrame を呼ばない (TimelineCurveEditor.UpdateDrag と同じガード)
        /// </summary>
        private void ApplyTangent(bool isOut, float normalizedValue)
        {
            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var currentOut, out var currentIn);
            var current = isOut ? currentOut : currentIn;
            if (normalizedValue == current)
            {
                return;
            }

            if (isOut)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue = normalizedValue;
                    data.isSmooth = false;
                });
            }
            else
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue = normalizedValue;
                    data.isSmooth = false;
                });
            }
            ApplyAndRecord(isOut ? "タンジェント: Out 変更" : "タンジェント: In 変更");
        }
```

- [ ] **Step 4: 両構成ビルド + テスト**

Run: 両構成ビルド（COM3D2 → COM3D25 の順）→ `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、新規の失敗なし（既知の 4 件のみ）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs
git commit -m "feat(timeline): 補間曲線にハンドルを表示しドラッグで In/Out を調整する"
```

---

### Task 5: コードレビューと実機確認の依頼

**Files:** なし（レビューでの指摘があれば該当ファイルを修正）

- [ ] **Step 1: code-review スキルを実行**

Run: `code-review` スキル（Task 1〜4 の差分を対象）
Expected: 🔴 HIGH が残らない状態にする。取り込んだ指摘は修正してコミット

- [ ] **Step 2: 実機確認の依頼をユーザーへ出す**

DLL の実機配置（`W:\COM3D2_5\Sybaris\UnityInjector\`）は規約により実行しない。以下の確認項目をユーザーへ提示する:
- 値種別コンボに、選択キーフレームが持たない種別（例: 位置しか持たないレイヤーでの「X回転」）が出ないこと
- OutTangent / InTangent のラベル左右ドラッグで値が動き、数値入力でも変えられること
- 曲線プレビュー内の 2 個のハンドルをドラッグすると曲線が追従し、離した時点で Undo 履歴が 1 件だけ積まれること
- ハンドル以外の場所をドラッグしても Inspector のスクロールが妨げられないこと
- 区間の先頭キーだけを選んだ状態（前キーが無く編集対象のタンジェントが 0 件）では、欄は操作できるが値が変わらない。無反応で気になるようなら無効化するか要相談
