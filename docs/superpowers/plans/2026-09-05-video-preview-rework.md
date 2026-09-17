# 動画ウィンドウ / プレビュー改修 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans でタスク単位に実行する（本リポジトリでは subagent-driven-development は使わない）。Steps は `- [ ]` チェックボックスで進捗管理する。

**Goal:** 動画の「GUI 表示」をゲーム画面への DisplayIMGUI 描画から動画ごとの独立プレビューウィンドウ描画へ置き換え、あわせて動画ウィンドウの開始位置・音量入力をドラッグ数値 1 行にまとめる。

**Architecture:** `VideoDisplayType.GUI` の意味を「プレビューウィンドウに表示」へ変更し、`MoviePlayerImpl` からは DisplayIMGUI と GUI 配置ロジックを撤去して MediaPlayer（テクスチャ供給）だけを残す。`VideoPreviewWindow` は単一シングルトンから添字付き 4 インスタンス（`MovieManager.MaxVideoCount` と同数）へ拡張し、配置・表示状態は `Config` のリストに index ごとに保存する。描画は現行の cover（ウィンドウいっぱいに拡大して見切れ）方式を維持し、`guiScale` をウィンドウサイズ比、`guiAlpha` を不透明度として反映する。

**Tech Stack:** C# 7.3 / .NET 3.5 相当（Unity 5.6 IMGUI）、AVProVideo、xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`、net48 SDK 形式）

**Spec:** 本ドキュメントの「仕様（ユーザー確定事項）」節（口頭仕様のため独立 spec ファイルは作らない）

## 仕様（ユーザー確定事項）

1. 動画の**開始位置と音量**をドラッグ可能な数値入力（`GUIView.DrawDragFloatField`）に変更し、**同じ 1 行**にまとめる。
2. **音量の既定値を 0** にする（`VideoSettings.volume` の初期値、リセットボタンの既定値の両方）。
3. 「無効状態でもプレビュー表示」（未コミットの作業ツリー変更）は**オミット**＝ HEAD の挙動（無効化で `UnloadMovie`）に戻す。
4. 代わりに **表示形式が GUI のときプレビューウィンドウに表示**する。従来の GUI 表示機能（`DisplayIMGUI` によるゲーム画面描画）は**削除**する。
5. GUI 用設定は **表示サイズ（ウィンドウサイズ比）と透過度のみ残す**。GUI 位置（`guiPosition`）は削除する。
6. 表示形式の選択肢名 `GUI` → **「プレビュー」**（表示名のみ。enum 名 `VideoDisplayType.GUI` は XML 互換のため据え置き）。
7. **動画ごとに独立したプレビューウィンドウ**（動画1〜4）。ウィンドウごとに位置・サイズ・表示 ON/OFF を保存する。

## Global Constraints

- コメント・ログメッセージは日本語で書く。
- 既存コードスタイル（`EditorSubWindow` 派生、`GUIView` 描画、`Config` の XML シリアライズ）に合わせる。ハードコーディングは避ける。
- ビルドは `debug.bat all`（COM3D2 / COM3D2.5 の 2 構成）で両方成功させる。`deploy.bat` / `release.bat` は実行しない。
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` で全 PASS を保つ。
- 動画本数の上限は `MovieManager.MaxVideoCount`（= 4）。プレビューウィンドウ数はこの定数から導出し、4 をリテラルで散らさない。
- `VideoDisplayType` の enum 名・順序は変更しない（プリセット / タイムライン XML が enum 名で保存されるため）。

## File Structure

| ファイル | 役割 / 変更内容 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs` | `volume` 既定値 0、`guiPosition` 削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | `VideoSettingsXml.guiPosition` 削除、旧形式マイグレーションの該当代入削除 |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | プリセット XML の `guiPosition` 削除 |
| `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs` | `guiPosition` の写し取り削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs` | `DisplayIMGUI` 生成・GUI 分岐を削除（GUI 形式ではメッシュも作らずテクスチャ供給のみ） |
| `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs` | 開始位置・音量を 1 行のドラッグ入力へ、GUI 設定行を表示サイズ/透過度のみに、表示形式名を「プレビュー」に、プレビュートグルを選択中 index のウィンドウへ |
| `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs` | 添字付き複数インスタンス化。GUI 形式のときのみ動画を描画し `guiScale` / `guiAlpha` を反映 |
| `source/COM3D2.SceneEditor.Plugin/Config.cs` | プレビューウィンドウ配置を index ごとのリストで保持 |
| `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs` | プレビューウィンドウを本数分登録 |
| `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs` | Window メニューに「動画プレビュー1〜4」を並べる |
| `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs` | `guiPosition` のアサーション削除 |

---

### Task 1: 「無効でもプレビュー」変更の巻き戻し

前回の作業ツリー変更のうち、無効時もプレイヤーを生かす部分だけを HEAD へ戻す。`VideoPreviewWindow.cs` の cover 描画（`CoverRect` + `GUI.BeginGroup`）は今回も使うため残す。

**Files:**
- Revert: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs`
- Revert: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`
- Revert: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`
- Revert: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`
- Keep: `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`（変更を維持）

**Interfaces:**
- Consumes: なし
- Produces: `MovieManager.IsEnabled(int)` が復活し、`SetupImpl` / `LoadMovie(int)` の読込判定が `IsEnabled` に戻る。`MoviePlayerImpl.UpdateVisible()` と `MovieManager.UpdateVisible()` / `UpdateVisible(int)` は存在しなくなる。

- [ ] **Step 1: 4 ファイルを HEAD に戻す**

```bash
git checkout HEAD -- \
  source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs \
  source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs \
  source/COM3D2.SceneEditor.Plugin/VideoWindow.cs \
  source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs
```

- [ ] **Step 2: 巻き戻し漏れの確認**

Run: `git diff --stat`
Expected: 変更が残るのは `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs` のみ（サブモジュール `MTEUtils` のポインタ差分は元からあるので無視）。

- [ ] **Step 3: プレビュー側に残った旧コメントを直す**

`VideoPreviewWindow.cs` のクラス doc から「有効・無効に関係なく」の記述を戻す（Task 4 で「プレビュー形式のときだけ表示」に書き換えるため、ここでは HEAD 相当の文面にする）。

```csharp
    /// <summary>
    /// 再生中の動画をウィンドウ内に表示するプレビュー。
    /// 表示形式 (GUI / 3D / 最背面 / 最前面) に関係なく、MediaPlayer のテクスチャを直接描くため
    /// ゲーム画面上で見えにくい配置でも内容を確認できる
    /// </summary>
```

- [ ] **Step 4: ビルド確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat" all`
Expected: 「0 エラー」で 2 構成とも成功。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs
git commit -m "feat(video): 動画プレビューをウィンドウいっぱいに拡大表示する"
```

---

### Task 2: 音量の既定値 0 と guiPosition の削除

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:780`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs:270,311`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`（`DrawGuiSetting` の位置行削除、音量スライダーの `defaultValue`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs:298-299`（`_displayIMGUI._x/_y` 代入を削除。DisplayIMGUI 自体の撤去は Task 4）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`

**Interfaces:**
- Consumes: Task 1 で戻した `VideoSettings`
- Produces: `VideoSettings` から `guiPosition` が消え、`volume` の初期値が `0f` になる。`VideoSettingsXml` からも `guiPosition` 要素が消える。

- [ ] **Step 1: テストを先に書き換えて落とす**

`BgmVideoSettingsXmlTests.cs` の `guiPosition` を使う 2 箇所（L51 の初期化、L75 のアサーション）を削除し、代わりに音量の既定値を検証するテストを追加する。

```csharp
        [Fact]
        public void VideoSettings_音量の既定値は0()
        {
            var settings = new VideoSettings();

            Assert.Equal(0f, settings.volume);
        }
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: `VideoSettings_音量の既定値は0` が `Assert.Equal() Failure: Expected 0, Actual 0.5` で FAIL（`guiPosition` を消したことでコンパイルエラーになる場合は、それが期待どおりの赤）。

- [ ] **Step 3: VideoSettings を変更する**

`source/COM3D2.SceneEditor.Plugin/Timeline/VideoSettings.cs`:

```csharp
        public float startTime = 0f;
        // 動画の音は既定で鳴らさない (BGM と重なって驚くのを避ける)
        public float volume = 0f;
        public float alpha = 1f;
        public float guiScale = 1f;
        public float guiAlpha = 1f;
```

`guiPosition` のフィールド宣言と、`CopyFrom` / `ReadFrom` / `ToXml` 内の `guiPosition` 行（VideoSettings.cs:20,41,63,87）をすべて削除する。

- [ ] **Step 4: XML / プリセット / スナップショットから guiPosition を消す**

- `Timeline/TimelineXml.cs:112` の `public Vector2 guiPosition = new Vector2(0, 0);` を削除。
- `Timeline/TimelineXml.cs:457` の旧形式マイグレーション `guiPosition = videoGUIPosition,` を削除（読み捨てになるため、直前に日本語コメント `// 旧形式の GUI 位置はプレビューウィンドウ配置に置き換わったため読み捨てる` を残す）。`videoGUIPosition` フィールド自体が他で使われていなければあわせて削除する（`grep -n videoGUIPosition source -r` で確認）。
- `ScenePresetData.cs:780` の `public Vector2 guiPosition;` を削除。
- `Manager/MteEffectsSnapshot.cs:270,311` の `guiPosition` 代入 2 行を削除。

- [ ] **Step 5: 音量スライダーの既定値を 0 にする**

`VideoWindow.cs` の音量 `DrawSliderValue` の `defaultValue = 0.5f` を `defaultValue = 0f` に変更する（この行は Task 3 でドラッグ入力へ置き換わるが、ここでは既定値だけ先に合わせる）。

- [ ] **Step 6: GUI 設定の位置行を削除する**

`VideoWindow.cs` の `DrawGuiSetting` から `DrawPositionRow(...)` 呼び出しブロック（VideoWindow.cs:317-321 相当）を削除し、表示サイズ・透過度の 2 行だけを残す。
`MoviePlayerImpl.cs:298-299` の `_displayIMGUI._x` / `_displayIMGUI._y` 代入も削除する。

- [ ] **Step 7: テストとビルドを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS。
Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat" all`
Expected: 「0 エラー」で 2 構成とも成功。

- [ ] **Step 8: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(video): 音量の既定値を 0 にし GUI 表示位置の設定を廃止する"
```

---

### Task 3: 開始位置・音量をドラッグ数値入力 1 行にまとめる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`（`DrawVideoSetting` の開始位置スライダー / 音量スライダー）

**Interfaces:**
- Consumes: `GUIView.DrawDragFloatField(GUIView.DragFloatFieldOption)`、`GUIView.BeginHorizontal()` / `EndLayout()`
- Produces: 開始位置と音量が 1 行に並ぶ。表示形式ごとの設定行（`DrawGuiSetting` 等）はこの行の後に続く。

- [ ] **Step 1: 開始位置と音量の 2 つの `DrawSliderValue` を 1 行のドラッグ入力へ置き換える**

`DrawVideoSetting` 内の開始位置スライダー（VideoWindow.cs:240-255 相当）と音量スライダー（VideoWindow.cs:272-287 相当）を削除し、表示形式ごとの `switch` の**手前**に次の 1 行を置く。

```csharp
            DrawStartTimeAndVolumeRow(view);
```

そのうえで同クラスに次のメソッドを追加する。

```csharp
        /// <summary>ラベル幅を詰めた 2 値横並び用のラベル幅</summary>
        private static readonly int SHORT_LABEL_WIDTH = 60;
        /// <summary>2 値を 1 行に並べるときの数値入力欄の幅</summary>
        private static readonly int PAIR_FIELD_WIDTH = 60;

        /// <summary>
        /// 開始位置と音量の 1 行。どちらも 1 値なのでスライダーで 2 行使うより
        /// ドラッグ数値入力を横に並べたほうが表示形式ごとの設定行を見渡しやすい
        /// </summary>
        private void DrawStartTimeAndVolumeRow(GUIView view)
        {
            var frameRate = movieManager.GetFrameRate(_videoIndex);
            var duration = movieManager.GetDuration(_videoIndex);

            view.BeginHorizontal();
            {
                view.DrawDragFloatField(new GUIView.DragFloatFieldOption
                {
                    label = "開始位置",
                    labelWidth = SHORT_LABEL_WIDTH,
                    fieldWidth = PAIR_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    // 1px で 1 フレーム動かす。メタデータ未確定時は秒単位の細かさで代替する
                    dragSensitivity = frameRate > 0f ? 1f / frameRate : 0.01f,
                    value = settings.startTime,
                    minValue = -1f,
                    maxValue = duration > 0f ? duration : float.MaxValue,
                    onChanged = newValue =>
                    {
                        settings.startTime = newValue;
                        movieManager.UpdateSeekTime(_videoIndex);
                    },
                    onReset = () =>
                    {
                        settings.startTime = 0f;
                        movieManager.UpdateSeekTime(_videoIndex);
                    },
                });

                view.DrawDragFloatField(new GUIView.DragFloatFieldOption
                {
                    label = "音量",
                    labelWidth = SHORT_LABEL_WIDTH,
                    fieldWidth = PAIR_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    value = settings.volume,
                    minValue = 0f,
                    maxValue = 1f,
                    onChanged = newValue =>
                    {
                        settings.volume = newValue;
                        movieManager.UpdateVolume(_videoIndex);
                    },
                    onReset = () =>
                    {
                        settings.volume = 0f;
                        movieManager.UpdateVolume(_videoIndex);
                    },
                });
            }
            view.EndLayout();
        }
```

- [ ] **Step 2: ビルド確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat" all`
Expected: 「0 エラー」で 2 構成とも成功。

- [ ] **Step 3: 実機確認（ゲーム起動中のみ）**

MCP `com3d25-devbridge` が応答するなら `screenshot` で動画ウィンドウを撮り、開始位置と音量が 1 行に並び、ラベルの左右ドラッグで値が動くことを確認する。ゲーム未起動ならスキップし、その旨を報告に残す。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/VideoWindow.cs
git commit -m "feat(video): 開始位置と音量をドラッグ数値入力の 1 行にまとめる"
```

---

### Task 4: GUI 表示形式をプレビュー描画へ置き換える

DisplayIMGUI によるゲーム画面描画をやめ、GUI 形式は「プレビューウィンドウにだけ映る」形式にする。プレビューウィンドウはこの時点ではまだ単一（添字は `VideoWindow.instance.selectedIndex` 追従）。複数化は Task 5。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs`（表示形式名）
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`（GUI 形式のみ描画、`guiScale` / `guiAlpha` 反映）

**Interfaces:**
- Consumes: `MovieManager.GetSettings(int)`, `MovieManager.GetTexture(int)`, `MovieManager.RequiresVerticalFlip(int)`
- Produces: `MoviePlayerImpl` から `_displayIMGUI` フィールド、`DisplayIMGUI` 生成、`UpdateTransform` / `UpdateColor` の GUI 分岐が消える。`VideoPreviewWindow` は `displayType == VideoDisplayType.GUI` のときだけ動画を描く。

- [ ] **Step 1: MoviePlayerImpl から DisplayIMGUI を撤去する**

`MoviePlayerImpl.cs` で次を行う。

1. フィールド `private DisplayIMGUI _displayIMGUI = null;` を削除し、`OnDestroy` 内の `_displayIMGUI = null;` も削除する。
2. `Setup` の分岐を、GUI 形式では描画コンポーネントを作らない形にする。

```csharp
            // GUI 形式はプレビューウィンドウがテクスチャを直接描くため、
            // ゲーム画面側の描画コンポーネントは作らない
            if (!isDisplayOnGUI)
            {
                gameObject.layer = layerMask;
                _meshRenderer = gameObject.AddComponent<MeshRenderer>();
                _meshFilter = gameObject.AddComponent<MeshFilter>();
                _meshFilter.mesh = CreateQuadMesh();

                Material material = new Material(Shader.Find(config.videoShaderName));
                _meshRenderer.material = material;

                _applyToMaterial = gameObject.AddComponent<ApplyToMaterial>();
                _applyToMaterial.Material = material;
                _applyToMaterial.Player = _mediaPlayer;
            }
```

3. `UpdateTransform` の `if (isDisplayOnGUI) { ... }` 分岐を削除し、GUI 形式では何もしないようにする（先頭で `if (isDisplayOnGUI) return;`）。
4. `UpdateColor` の `if (isDisplayOnGUI) { ... }` 分岐を削除し、`_meshRenderer != null` のときだけ色を更新する形にする。
5. `videoColor` の `isDisplayOnGUI` 分岐（`color.a = video.guiAlpha;`）は残す（プレビュー側で `guiAlpha` を直接読むため、ここは削除して差し支えないなら削除し、参照が残るならそのままにする。実装時に `videoColor` の呼び出し元を確認して判断する）。

- [ ] **Step 2: 表示形式の選択肢名を「プレビュー」にする**

`VideoWindow.cs` の `VideoDisplayTypeNames` を書き換える。

```csharp
        // enum 名 (GUI) は XML 互換のため据え置き、表示名だけ実態に合わせる
        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "プレビュー",
            "3Dビュー",
            "最背面",
            "最前面",
        };
```

- [ ] **Step 3: プレビュー描画を GUI 形式限定にし、表示サイズ・透過度を反映する**

`VideoPreviewWindow.DrawContent` を次の形にする（`localRect` の背景塗りは現状のまま先に行う）。

```csharp
            var settings = movieManager.GetSettings(videoIndex);
            if (settings.displayType != MTEP.VideoDisplayType.GUI)
            {
                _view.Init(localRect);
                _view.DrawLabel("表示形式が「プレビュー」ではありません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            // メタデータ確定前はサイズ 0 のダミーが返ることがあり、そのままだと CoverRect が NaN になる
            var texture = movieManager.GetTexture(videoIndex);
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                _view.Init(localRect);
                _view.DrawLabel("動画が読み込まれていません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            // MediaFoundation 等ではテクスチャが上下反転しているため UV 側で戻す
            var texCoords = movieManager.RequiresVerticalFlip(videoIndex)
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);

            // 領域いっぱいまで拡大するため、はみ出した分はグループでクリップする
            var drawRect = CoverRect(localRect, (float)texture.width / texture.height, settings.guiScale);
            drawRect.x -= localRect.x;
            drawRect.y -= localRect.y;

            var prevGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, settings.guiAlpha);

            GUI.BeginGroup(localRect);
            {
                GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords, true);

                if (config.isGridVisibleInVideo && GridRenderer.isGridEnabled)
                {
                    DrawGrid(drawRect);
                }
            }
            GUI.EndGroup();

            GUI.color = prevGuiColor;
```

`CoverRect` に表示サイズ比の引数を足す。

```csharp
        /// <summary>
        /// 領域をアスペクト比を保って覆う中央寄せ矩形を返す。短辺側は領域からはみ出す。
        /// scale はウィンドウサイズに対する表示倍率 (1 で領域いっぱい)
        /// </summary>
        private static Rect CoverRect(Rect area, float aspectRatio, float scale)
        {
            var width = area.width;
            var height = width / aspectRatio;
            if (height < area.height)
            {
                height = area.height;
                width = height * aspectRatio;
            }

            width *= scale;
            height *= scale;

            return new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);
        }
```

あわせてクラス doc を実態に合わせる。

```csharp
    /// <summary>
    /// 表示形式が「プレビュー」の動画をウィンドウ内に描画する。
    /// ゲーム画面には出さずここだけに映すための表示形式で、
    /// 表示サイズ (ウィンドウサイズ比) と透過度を反映する
    /// </summary>
```

- [ ] **Step 4: ビルド確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat" all`
Expected: 「0 エラー」で 2 構成とも成功。`DisplayIMGUI` への参照が残っていないことを `grep -rn "DisplayIMGUI" source --include=*.cs | grep -v /obj/` で確認（0 件）。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin
git commit -m "feat(video): GUI 表示形式をプレビューウィンドウ描画へ置き換える"
```

---

### Task 5: プレビューウィンドウを動画ごとに独立させる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/WindowManager.cs:66`
- Modify: `source/COM3D2.SceneEditor.Plugin/MenuBarWindow.cs:132`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs:191`（プレビュートグル）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/BgmVideoSettingsXmlTests.cs`（新規テストは Config 側なので追加先は後述）

**Interfaces:**
- Consumes: `MovieManager.MaxVideoCount`, `EditorSubWindow.LoadPlacement/StorePlacement/savedVisible`
- Produces:
  - `Config.VideoPreviewPlacement`（`posX` / `posY` / `width` / `height` / `visible`）
  - `Config.GetVideoPreview(int index) : VideoPreviewPlacement`（不足分を既定値で埋めて返す）
  - `VideoPreviewWindow.GetInstance(int index) : VideoPreviewWindow`
  - `VideoPreviewWindow.instances : IEnumerable<VideoPreviewWindow>`（登録・メニュー生成用、`MaxVideoCount` 個）

- [ ] **Step 1: Config のテストを書いて落とす**

`source/COM3D2.SceneEditor.Plugin.Tests/` に `VideoPreviewPlacementTests.cs` を新規作成する。

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class VideoPreviewPlacementTests
    {
        [Fact]
        public void 未設定の添字でも既定値の配置が返る()
        {
            var config = new Config();

            var placement = config.GetVideoPreview(2);

            Assert.Equal(-1, placement.posX);
            Assert.Equal(-1, placement.posY);
            Assert.Equal(480, placement.width);
            Assert.Equal(270, placement.height);
            Assert.False(placement.visible);
        }

        [Fact]
        public void 添字ごとに別の配置が保持される()
        {
            var config = new Config();

            config.GetVideoPreview(0).posX = 100;
            config.GetVideoPreview(1).posX = 200;

            Assert.Equal(100, config.GetVideoPreview(0).posX);
            Assert.Equal(200, config.GetVideoPreview(1).posX);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: `Config.GetVideoPreview` が存在せずコンパイルエラーで FAIL。

- [ ] **Step 3: Config にプレビュー配置リストを追加する**

`Config.cs` の既存フィールド `videoPreviewPosX` 〜 `videoPreviewVisible`（Config.cs:215-220）を削除し、次を追加する。

```csharp
        /// <summary>動画プレビューウィンドウ 1 本分の配置 (-1 は未初期化)</summary>
        public class VideoPreviewPlacement
        {
            public int posX = -1;
            public int posY = -1;
            public int width = 480;
            public int height = 270;
            public bool visible = false;
        }

        // 動画プレビューウィンドウ (動画の添字ごとに 1 件)
        [XmlElement("videoPreview")]
        public List<VideoPreviewPlacement> videoPreviews = new List<VideoPreviewPlacement>();

        /// <summary>
        /// 添字に対応するプレビュー配置。足りない分は既定値で埋めて返すため、
        /// 旧バージョンの Config を読んでも欠番で落ちない
        /// </summary>
        public VideoPreviewPlacement GetVideoPreview(int index)
        {
            while (videoPreviews.Count <= index)
            {
                videoPreviews.Add(new VideoPreviewPlacement());
            }
            return videoPreviews[index];
        }
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS。

- [ ] **Step 5: VideoPreviewWindow を添字付き複数インスタンスにする**

`VideoPreviewWindow.cs` のシングルトンを添字付きに置き換える。

```csharp
        /// <summary>添字ごとに固有の ID を振る (レイアウト保存・ロック状態のキー)</summary>
        public static readonly int WINDOW_ID = 8903397;

        protected override int windowId => WINDOW_ID + _videoIndex;
        /// <summary>タイトルの番号は VideoWindow の操作対象コンボと同じ 1 始まり</summary>
        protected override string windowTitle => "動画プレビュー (" + (_videoIndex + 1) + ")";

        /// <summary>このウィンドウが表示する動画の添字</summary>
        private readonly int _videoIndex;

        private int videoIndex => _videoIndex;

        private static VideoPreviewWindow[] _instances = null;

        /// <summary>動画本数の上限ぶんのウィンドウ。登録とメニュー生成で使う</summary>
        public static VideoPreviewWindow[] instances
        {
            get
            {
                if (_instances == null)
                {
                    _instances = new VideoPreviewWindow[MTEP.MovieManager.MaxVideoCount];
                    for (var i = 0; i < _instances.Length; i++)
                    {
                        _instances[i] = new VideoPreviewWindow(i);
                    }
                }
                return _instances;
            }
        }

        public static VideoPreviewWindow GetInstance(int index)
        {
            var list = instances;
            return list[Mathf.Clamp(index, 0, list.Length - 1)];
        }

        private VideoPreviewWindow(int videoIndex)
        {
            _videoIndex = videoIndex;
        }
```

配置の読み書きを Config のリスト経由にする。

```csharp
        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            var placement = config.GetVideoPreview(_videoIndex);
            x = placement.posX;
            y = placement.posY;
            width = placement.width;
            height = placement.height;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            var placement = config.GetVideoPreview(_videoIndex);
            placement.posX = x;
            placement.posY = y;
            placement.width = width;
            placement.height = height;
        }

        public override bool savedVisible
        {
            get => config.GetVideoPreview(_videoIndex).visible;
            set => config.GetVideoPreview(_videoIndex).visible = value;
        }
```

本数を超える添字のウィンドウは中身を出さない。`DrawContent` の先頭（背景塗りの直後）に次を挟む。

```csharp
            if (!movieManager.IsValidIndex(videoIndex))
            {
                _view.Init(localRect);
                _view.DrawLabel("この番号の動画はありません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }
```

- [ ] **Step 6: 登録とメニューを本数分にする**

`Manager/WindowManager.cs:66` の `AddWindow(VideoPreviewWindow.instance);` を置き換える。

```csharp
            foreach (var previewWindow in VideoPreviewWindow.instances)
            {
                AddWindow(previewWindow);
            }
```

`MenuBarWindow.cs:132` の `CreateWindowItem("動画プレビュー", VideoPreviewWindow.instance),` を、`items` 配列の組み立て前にプレビュー項目を作って差し込む形に変える。配列リテラル内で `foreach` は書けないため、Window メニューの `items` を `List<MenuItem>` で組み立てる形に直し、動画の直後に次を挿入する。

```csharp
                    // 動画プレビューは動画の本数分あるので番号付きで並べる
                    for (var i = 0; i < VideoPreviewWindow.instances.Length; i++)
                    {
                        windowItems.Add(CreateWindowItem("動画プレビュー" + (i + 1), VideoPreviewWindow.instances[i]));
                    }
```

`MenuDef.items` が `MenuItem[]` 型なら `windowItems.ToArray()` を渡す。

- [ ] **Step 7: VideoWindow のプレビュートグルを操作対象へ向ける**

`VideoWindow.cs:190-195` 相当のトグルを次にする。

```csharp
                // プレビューウィンドウの表示切替。操作対象の動画に対応する 1 枚を開閉する
                var previewWindow = VideoPreviewWindow.GetInstance(_videoIndex);
                view.DrawToggle("プレビュー", previewWindow.isShowWnd, 90, ROW_HEIGHT, _ =>
                {
                    WindowManager.ToggleWindowVisible(previewWindow);
                });
```

`VideoWindow.selectedIndex`（VideoWindow.cs:52 付近）はプレビュー側から参照されなくなるため削除する（`grep -rn "selectedIndex" source --include=*.cs | grep -v /obj/` で他参照がないことを確認してから）。

- [ ] **Step 8: テストとビルドを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS。
Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat" all`
Expected: 「0 エラー」で 2 構成とも成功。

- [ ] **Step 9: 実機確認（ゲーム起動中のみ）**

`com3d25-devbridge` が応答するなら、動画を 2 本にして両方を表示形式「プレビュー」にし、`screenshot` でプレビューウィンドウが 2 枚別々に出ることを確認する。未起動ならスキップし報告に残す。

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin source/COM3D2.SceneEditor.Plugin.Tests
git commit -m "feat(video): 動画ごとに独立した動画プレビューウィンドウを開けるようにする"
```

---

## 補足: 影響と非対象

- 旧 Config の `videoPreviewPosX` 等は削除するため、既存 Config を読み込むと未知要素として読み捨てられ、プレビューウィンドウの配置だけが初期値に戻る。プラグイン全体の設定は失われない。
- 旧プリセット / タイムライン XML の `guiPosition` も読み捨てになる。表示形式 GUI の動画は、位置指定ぶんの見え方だけが変わる。
- `videoShaderName` や 3D / 最背面 / 最前面の挙動は変更しない。
