# シーンプリセット 保存カテゴリの統一（背景の追加 + v16 保存フラグ）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** プリセット保存ポップアップに読込トグルと同じ「背景」カテゴリ（背景・ライト・PNG 配置）を追加し、どのカテゴリを保存したかをプリセット XML（v16）に明記して、未保存カテゴリを適用時に確実に飛ばせるようにする。

**Architecture:** `ScenePresetData` に `savedCamera` / `savedMaids` / `savedBackground` の 3 属性を追加する（既定 `true`）。既定値により v15 以前のプリセットは「全カテゴリ保存済み」として読まれ、既存の適用挙動が一切変わらない。保存側は `ScenePresetSaveOptions` に `saveBackground` を足して `Capture` でゲートしつつ、同じ値を上記フラグへ記録する。適用側は既存の読込トグル（`config.scenePresetLoad*`）と保存フラグの **AND** で判定し、判定は `ScenePresetManager` 内の 3 つのヘルパーに集約する。

**Tech Stack:** C# 7.3（.NET 3.5 相当 / 4.7.1 の両ターゲット）、Unity IMGUI、XmlSerializer ベースの Config とプリセット XML

**Spec:** 別ファイルなし。本ファイルの「仕様詳細」がスペック（ユーザー指示:「保存時も同じカテゴリを使用したい。(背景など)」＋ 対話で確定した下記の決定事項）

## 仕様詳細

### 背景（なぜこの変更が必要か）

読込側には `カメラ / メイド / 背景（背景・ライト・PNG 配置）` の 3 カテゴリ＋プロバイダ別トグルがあるが、
保存側は `カメラ / メイド` ＋プロバイダ別しか無く、背景・ライト・PNG 配置は**常に保存**されていた。
保存側にも「背景」を足すだけだと、保存しなかったカテゴリが `null` になったとき

- `BackgroundSnapshot.ApplyState(null)` / `LightSnapshot.ApplyState(null)` … 即 return（無害）
- `PngPlacementSnapshot.ApplyState(null)` … **PNG 配置を全消去**（`PngPlacementSnapshot.cs:60-70`）

と扱いが割れる。`null` だけでは「保存していない」と「保存した結果 0 枚」を区別できないため、
保存カテゴリを XML に明記する方式を採る（ユーザー選択）。

### 決定事項（ユーザー確認済み）

- 保存側のカテゴリ構成は読込側と揃える: **カメラ / メイド / 背景**（背景・ライト・PNG 配置をまとめる）＋ 発見済み外部プロバイダごとに 1 つ。
- どのカテゴリを保存したかは **プリセット XML（v16）の属性**として記録する。
- v15 以前のプリセットは 3 フラグとも既定 `true`（= 全カテゴリ保存済み）として読む。
  これにより旧プリセットの適用挙動は現状から一切変わらない
  （v14 以前の `pngPlacement` 無しプリセットが PNG 配置を全消去する挙動も維持される）。
- 適用可否は **保存フラグ AND 読込トグル**。どちらかが OFF なら適用しない。
- 保存ポップアップの表示名は既存どおり長い方（`displayName`）を使う。読込トグルの短縮名とは別。

### ゲート対象の対応表（適用時）

| 適用対象 | 判定 |
|---|---|
| `CameraSnapshot.ApplyState` | `data.savedCamera && config.scenePresetLoadCamera` |
| `BackgroundSnapshot.ApplyState` / `LightSnapshot.ApplyState` / `PngPlacementSnapshot.ApplyState` | `data.savedBackground && config.scenePresetLoadBackground` |
| `AssignMaids` / `ReleaseUnassignedMaids` / `ApplyMaid` / `ApplyLooks` / `RequestFocusOnAppliedMaid` | `data.savedMaids && config.scenePresetLoadMaids` |
| `ApplyExternals` の各 external | `IsProviderLoadEnabled(external.id)`（変更なし。未保存プロバイダはそもそも external が無い） |

### 意図的な仕様（今回変更しないもの）

- `PngPlacementSnapshot.ApplyState(null)` の全消去挙動は**そのまま残す**。
  v16 以降は `savedBackground = false` の時点で呼び出し自体が行われないため、
  この分岐に来るのは v14 以前の旧プリセットだけになる。
- `ReleaseUnassignedMaids` の「1 体以上保存されている場合だけ解除する」ヒューリスティックは**変更しない**。
  v16 では `savedMaids` で未保存を判別できるようになるが、旧プリセットとの二重判定が要るうえ、
  現行挙動で困っていないため YAGNI とする。
- SceneCapture プリセットは `SceneCapturePresetLoader.SceneCapturePresetResult`（`ScenePresetData` ではない）を
  経由するため、保存フラグとは無関係。**変更しない**（読込トグルによるゲートは既存のまま効く）。
- 読込トグル（`scenePresetLoad*`）と保存トグル（`scenePresetSave*`）は引き続き独立した Config キー。

## Global Constraints

- ビルドコマンド: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`（Bash ツールからは `//c`。`/c` はパス変換で壊れる）
- 同じソースが COM3D2 (2.0) と COM3D2.5 の両方にビルドされる。両方で `ビルドに成功しました` かつ `0 エラー` であること
  （ゲーム起動中は dll のコピーのみ失敗するが想定内）
- **テストフレームワークは存在しない**。検証は「ビルドが通ること」＋「実機での目視確認」
- コードコメント・ログメッセージは日本語
- コミットは Conventional Commits 形式の日本語メッセージ
- 新規 Config フィールドの既定値は `true`（従来どおり全保存）にして既存 Config の読み込み互換を保つ
- `ScenePresetData` へのフィールド追加は `[XmlAttribute]` + 既定値 `true` とし、旧プリセットが同じ意味で読めること

---

### Task 1: プリセット XML に保存カテゴリのフラグを追加する（v16）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（バージョン注記 384-410 行、`ScenePresetData` のフィールド 412-424 行）

**Interfaces:**
- Produces: `ScenePresetData.savedCamera` / `savedMaids` / `savedBackground`（`bool`、既定 `true`、`[XmlAttribute]`）。Task 3, 4 が使用

- [ ] **Step 1: バージョン注記を追加してバージョンを上げる**

`// v15: ...` の 2 行の直後、`public static readonly int CurrentVersion = 15;` の直前に追加:

```csharp
        // v16: savedCamera / savedMaids / savedBackground（保存時に選んだカテゴリ）を追加。
        //      旧形式は属性が無く既定の true で読めるため、全カテゴリ保存済みとして扱う
```

同時に `CurrentVersion` を 16 にする:

```csharp
        public static readonly int CurrentVersion = 16;
```

- [ ] **Step 2: 保存カテゴリのフラグを追加**

`public int version = CurrentVersion;` の直後、`public ScenePresetCamera camera;` の直前に追加:

```csharp
        // 保存時に選んだカテゴリ。「保存していない」と「保存した結果が空」を区別するために持つ。
        // 旧プリセット (v15 以前) は属性が無く既定の true で読まれ、全カテゴリ保存済みとして扱われる
        [XmlAttribute]
        public bool savedCamera = true;

        [XmlAttribute]
        public bool savedMaids = true;

        /// <summary>背景・ライト・PNG 配置をまとめた「背景」カテゴリを保存したか</summary>
        [XmlAttribute]
        public bool savedBackground = true;
```

- [ ] **Step 3: ビルドして通ることを確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: 両ターゲットとも `ビルドに成功しました` / `0 エラー`

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs
git commit -m "feat(preset): 保存したカテゴリをプリセット XML に記録できるようにする"
```

---

### Task 2: 保存側に「背景」カテゴリを追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`scenePresetSaveMaids` の直後、250 行あたり）
- Modify: `source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs`
  （`ScenePresetSaveOptions` 11-17 行、フィールド 44-45 行、`Show` 80-81 行、`ConfirmSave` 101-113 行、
  行数算出 155-156 行、`DrawWindow` 203-206 行）

**Interfaces:**
- Produces:
  - `Config.scenePresetSaveBackground`（`bool`、既定 `true`）。Task 5 のドキュメントが参照
  - `ScenePresetSaveOptions.saveBackground`（`bool`、既定 `true`）。Task 3 が使用

- [ ] **Step 1: Config にフィールドを追加**

`public bool scenePresetSaveMaids = true;` の直後に追加:

```csharp
        // 背景・ライト・PNG 配置をまとめた「背景」カテゴリ
        public bool scenePresetSaveBackground = true;
```

- [ ] **Step 2: ScenePresetSaveOptions に保存対象を追加**

`public bool saveMaids = true;` の直後に追加:

```csharp
        /// <summary>背景・ライト・PNG 配置をまとめた「背景」カテゴリ</summary>
        public bool saveBackground = true;
```

- [ ] **Step 3: ポップアップの状態フィールドを追加**

`private bool _saveMaids;` の直後に追加:

```csharp
        private bool _saveBackground;
```

- [ ] **Step 4: Show で Config から初期値を読む**

`window._saveMaids = config.scenePresetSaveMaids;` の直後に追加:

```csharp
            window._saveBackground = config.scenePresetSaveBackground;
```

- [ ] **Step 5: ConfirmSave で書き戻して保存対象に載せる**

`config.scenePresetSaveMaids = _saveMaids;` の直後に追加:

```csharp
            config.scenePresetSaveBackground = _saveBackground;
```

`saveMaids = _saveMaids,` の直後（`var options = new ScenePresetSaveOptions { ... }` の初期化子内）に追加:

```csharp
                saveBackground = _saveBackground,
```

- [ ] **Step 6: ウィンドウ高さの行数を 1 増やす**

`OnGUI` の行数算出コメントと式を差し替える:

```csharp
            // 行数（名前入力 + タイトル + 固定 3 カテゴリ + プロバイダ数 + エラー表示）に合わせて高さを算出する
            var rowCount = 2 + 3 + _providerChecks.Count + (_errorMessage != null ? 1 : 0);
```

- [ ] **Step 7: トグルを描画する**

`DrawWindow` の「メイド (位置・ポーズ・表情)」トグルの直後に追加:

```csharp
            _view.DrawToggle("背景 (背景・ライト・PNG 配置)", _saveBackground, contentWidth, ROW_HEIGHT,
                value => _saveBackground = value);
```

- [ ] **Step 8: ビルドして通ることを確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: 両ターゲットとも `ビルドに成功しました` / `0 エラー`

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Config.cs source/COM3D2.SceneEditor.Plugin/SavePresetPopupWindow.cs
git commit -m "feat(preset): 保存ポップアップに背景カテゴリを追加する"
```

---

### Task 3: Capture で背景をゲートし、保存カテゴリを記録する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`Capture` 682-712 行あたり）

**Interfaces:**
- Consumes: `ScenePresetSaveOptions.saveBackground`（Task 2）、
  `ScenePresetData.savedCamera` / `savedMaids` / `savedBackground`（Task 1）

- [ ] **Step 1: Capture を差し替える**

`Capture` の本体を次のとおりにする（`CaptureExternals` の呼び出しと `return` はそのまま）:

```csharp
        /// <summary>現在のシーン状態から、選択されたカテゴリだけプリセットデータを組み立てる</summary>
        private static ScenePresetData Capture(ScenePresetSaveOptions options)
        {
            var data = new ScenePresetData();

            // 適用時に「保存していない」と「保存した結果が空」を区別できるよう、選択内容を残す
            data.savedCamera = options.saveCamera;
            data.savedMaids = options.saveMaids;
            data.savedBackground = options.saveBackground;

            if (options.saveCamera)
            {
                data.camera = CameraSnapshot.CaptureState();
            }

            if (options.saveMaids)
            {
                foreach (var maid in maidManager.calledMaids)
                {
                    data.maids.Add(CaptureMaid(maid));
                }
            }

            // ライトと PNG 配置は UI 上「背景」カテゴリにまとめている
            if (options.saveBackground)
            {
                data.background = BackgroundSnapshot.CaptureState();
                data.light = LightSnapshot.CaptureState();
                data.pngPlacement = PngPlacementSnapshot.CaptureState();
            }

            CaptureExternals(data, options.enabledProviderIds);

            return data;
        }
```

- [ ] **Step 2: ビルドして通ることを確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: 両ターゲットとも `ビルドに成功しました` / `0 エラー`

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): 背景カテゴリの選択に従って保存内容を絞る"
```

---

### Task 4: 適用側で保存フラグと読込トグルを AND する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
  - 判定ヘルパーの追加（`IsProviderLoadEnabled` / `SetProviderLoadEnabled` の直後、`CreateRootItem()` の直前）
  - `Apply`（PNG 配置とメイド処理のゲート）
  - `ApplyScenery`
  - `FinishApply`

**Interfaces:**
- Consumes: `ScenePresetData.savedCamera` / `savedMaids` / `savedBackground`（Task 1）、
  `Config.scenePresetLoadCamera` / `scenePresetLoadMaids` / `scenePresetLoadBackground`（実装済み）
- Produces:
  - `static bool ShouldApplyCamera(ScenePresetData data)`
  - `static bool ShouldApplyMaids(ScenePresetData data)`
  - `static bool ShouldApplyBackground(ScenePresetData data)`
  （いずれも private。同ファイル内の適用経路のみが使う）

- [ ] **Step 1: 判定ヘルパーを追加**

`SetProviderLoadEnabled` の閉じ括弧の直後（`private static ScenePresetItem CreateRootItem()` の直前）に追加:

```csharp
        // 適用可否は「保存されているか (data.saved*)」と「読み込む設定か (config.scenePresetLoad*)」の AND。
        // v15 以前のプリセットは saved* が既定 true で読まれるため、従来どおり読込トグルだけで決まる

        /// <summary>カメラをこのプリセットから適用するか</summary>
        private static bool ShouldApplyCamera(ScenePresetData data)
        {
            return data.savedCamera && config.scenePresetLoadCamera;
        }

        /// <summary>メイド (呼出・解除・ポーズ・視線・フォーカス) を適用するか</summary>
        private static bool ShouldApplyMaids(ScenePresetData data)
        {
            return data.savedMaids && config.scenePresetLoadMaids;
        }

        /// <summary>背景カテゴリ (背景・ライト・PNG 配置) を適用するか</summary>
        private static bool ShouldApplyBackground(ScenePresetData data)
        {
            return data.savedBackground && config.scenePresetLoadBackground;
        }
```

- [ ] **Step 2: ApplyScenery のゲートをヘルパーに置き換える**

`ApplyScenery` の本体を次のとおりにする（XML コメントも差し替える）:

```csharp
        /// <summary>
        /// メイドを伴わない情景 (カメラ・背景・ライト) を適用する。
        /// 本適用と先出し適用で順序を揃えるため 1 箇所にまとめている。
        /// 保存されていない、または読込トグルで OFF にされたカテゴリは触らない
        /// </summary>
        private static void ApplyScenery(ScenePresetData data)
        {
            if (ShouldApplyCamera(data))
            {
                CameraSnapshot.ApplyState(data.camera);
            }
            // ライトは背景カテゴリに含めている (UI のトグルを 1 つにまとめているため)
            if (ShouldApplyBackground(data))
            {
                BackgroundSnapshot.ApplyState(data.background);
                LightSnapshot.ApplyState(data.light);
            }
        }
```

- [ ] **Step 3: Apply の PNG 配置とメイド処理をヘルパーに置き換える**

`Apply` 内の該当箇所を次のとおりにする（前後の `if (!skipScenery) { ApplyScenery(data); }` と
`if (_pendingApplies.Count > 0)` ブロックは変更しない）:

```csharp
            // PNG 配置も背景カテゴリに含める。
            // 未保存のプリセットで ApplyState(null) を呼ぶと既存の配置を全消去してしまうため、
            // ここで確実に弾く
            if (ShouldApplyBackground(data))
            {
                PngPlacementSnapshot.ApplyState(data.pngPlacement);
            }

            // メイドを読み込まないときは呼出も解除も行わず、現在のメイドをそのまま残す
            if (ShouldApplyMaids(data))
            {
                var assignments = AssignMaids(data.maids);

                // メイド未保存（カメラ・背景のみ等）のプリセットと「保存時に 0 体」は
                // XML 上区別できないため、1 体以上保存されている場合だけ解除まで行う
                if (data.maids != null && data.maids.Count > 0)
                {
                    ReleaseUnassignedMaids(assignments);
                }

                foreach (var pair in assignments)
                {
                    // 新規呼出はロード完了までポーズ・位置を適用できないため保留に積む
                    if (maidManager.IsLoading(pair.Key))
                    {
                        _pendingApplies.Add(pair);
                    }
                    else
                    {
                        ApplyMaid(pair.Key, pair.Value);
                    }
                }
            }
```

- [ ] **Step 4: FinishApply のゲートをヘルパーに置き換える**

`FinishApply` を差し替える:

```csharp
        /// <summary>
        /// 全メイドのロード完了後にまとめて行う仕上げ。
        /// 視線は他メイドを参照しうるため、外部プロバイダと同じくここで反映する。
        /// メイドを読み込まないときは AssignMaids ごと飛ばしているため、
        /// 無関係な既存メイドへ視線・フォーカスを当てないよう合わせて飛ばす
        /// </summary>
        private static void FinishApply(ScenePresetData data)
        {
            var applyMaids = ShouldApplyMaids(data);
            if (applyMaids)
            {
                ApplyLooks(data);
            }
            ApplyExternals(data);
            if (applyMaids)
            {
                RequestFocusOnAppliedMaid(data);
            }
        }
```

- [ ] **Step 5: 直接参照が残っていないことを確認**

Run: `grep -n "scenePresetLoadCamera\|scenePresetLoadMaids\|scenePresetLoadBackground" source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
Expected: ヒットするのは Step 1 で追加した 3 つのヘルパー内だけ（各 1 行、計 3 行）。
`ApplyScenery` / `Apply` / `FinishApply` に直接参照が残っていたらヘルパー呼び出しに置き換える。

なお `ApplySceneCapturePreset` は `ScenePresetData` を持たない別経路のため、
`config.scenePresetLoadCamera` / `scenePresetLoadBackground` を直接見たままでよい
（`SceneCapturePresetLoader` の結果型には保存フラグが無い）。上記 grep の対象ファイルには
`ApplySceneCapturePreset` も含まれるので、その 2 行はヒットして正常。合計 5 行になる。

- [ ] **Step 6: ビルドして通ることを確認**

Run: `cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: 両ターゲットとも `ビルドに成功しました` / `0 エラー`

- [ ] **Step 7: 実機で目視確認**

ゲームを起動し（dll 反映のため再起動が必要）、以下を確認する:

1. 保存ポップアップに「カメラ」「メイド (位置・ポーズ・表情)」「背景 (背景・ライト・PNG 配置)」＋プロバイダが並び、
   ウィンドウ高さがボタンと重ならずに収まっていること
2. 「背景」を OFF で保存 → 保存された XML に `savedBackground="false"` が入り、
   `<background>` / `<light>` / `<pngPlacement>` 要素が出ないこと
   （`Sybaris\UnityInjector\Config\SceneEditor\ScenePreset\<名前>.xml` を開いて確認）
3. **PNG 配置を数枚置いた状態で** 2 のプリセットを適用 → 背景・ライトが変わらず、
   **PNG 配置も消えない**こと（これが今回の中心的な修正点）
4. 「メイド」を OFF で保存したプリセットを適用 → 読込トグルが全 ON でもメイドが呼出・解除されないこと
5. 「カメラ」を OFF で保存したプリセットを適用 → カメラが動かないこと
6. 全カテゴリ ON で保存したプリセットは従来どおり全部復元されること
7. **この変更より前に保存した既存プリセット**を適用 → 従来どおりの挙動（全カテゴリ復元）であること
8. 保存側 OFF と読込側 OFF の組み合わせでも二重に問題が出ないこと（どちらか OFF なら適用されない）

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): 未保存カテゴリを適用時に確実に飛ばす"
```

---

### Task 5: ドキュメントを更新する

**Files:**
- Modify: `docs-site/guide/scene-preset.md`（「保存と適用」節）
- Modify: `docs-site/guide/configuration.md`（「シーンプリセット」の設定表）

- [ ] **Step 1: scene-preset.md の保存側の説明を更新**

`- 保存ダイアログでは「カメラ」「メイド」など、保存する要素を選べます` の行とその続きの
`（既定状態は設定項目 ...）` の行を、次の 3 行に差し替える:

```markdown
- 保存ダイアログでは「カメラ」「メイド」「背景」（背景・ライト・PNG 配置）と、
  連携プラグインが登録した項目から保存する要素を選べます
  （既定状態は設定項目 `scenePresetSaveCamera` / `scenePresetSaveMaids` / `scenePresetSaveBackground` で変更できます）
- 保存しなかった要素はプリセットに記録されず、適用時にも変更されません
```

- [ ] **Step 2: configuration.md の設定表へ追記**

`| scenePresetSaveMaids | true | 保存ダイアログの「メイド」の既定状態 |` の行の直後に追加:

```markdown
| `scenePresetSaveBackground` | `true` | 保存ダイアログの「背景」（背景・ライト・PNG 配置）の既定状態 |
```

- [ ] **Step 3: コミット**

```bash
git add docs-site/guide/scene-preset.md docs-site/guide/configuration.md
git commit -m "docs(preset): 保存側の背景カテゴリを説明に追加する"
```

---

## 実装後の必須手順

- [ ] **code-review スキルでレビューする**（CLAUDE.md のワークフロー規約。実装完了後・ユーザー提示前に必須）

---

## レビュー却下メモ

plan-review（総合評価 🟢 承認 / 🔴 なし）で挙がった 🟡 改善提案のうち、以下は本計画に取り込まない。

- **読込トグル側に「そのプリセットが何を保存しているか」を示すインジケータが無い** — 新規 UI の追加であり、
  今回の要望（保存側にも同じカテゴリを持たせる）のスコープ外。保存フラグは XML に載るので、
  必要になった時点で別途デザインすればよい（YAGNI）。
- **未保存カテゴリを飛ばしたときにログが出ない** — 「保存しなかった」はユーザー自身の選択であり、
  適用のたびに正常系のログを増やすとノイズになる。レビュー側も「好みの範囲」（確信度: 低）としている。
- **ドキュメントに旧プリセット（v14 以前）の PNG 全消去という例外挙動を書き添えるべき** —
  追記予定の文面「保存しなかった要素は……適用時にも変更されません」は v16 以降の保存内容についての説明で、
  旧プリセットは「全カテゴリ保存済み」として読まれるためこの文の対象外。文面自体は誤りではなく、
  例外の但し書きを足す方が利用者には分かりにくい。
