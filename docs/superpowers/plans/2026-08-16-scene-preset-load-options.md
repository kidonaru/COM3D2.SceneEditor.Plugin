# シーンプリセット 読込対象トグル + タイル幅調整 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** シーンプリセットウィンドウにカテゴリ別の読込トグル（カメラ / メイド / 背景 / 外部プロバイダごと）を追加してロード時に反映する内容を制御できるようにし、あわせてタイル横幅を PNG 配置ウィンドウと同じ 96 に揃える。

**Architecture:** 読込対象は `Config` に永続化し、`ScenePresetManager` の適用経路（`ApplyScenery` / `Apply` / `FinishApply` / `ApplyExternals` / `ApplySceneCapturePreset` / `ApplySceneCaptureExternals`）でカテゴリごとにゲートする。外部プロバイダはトグル行に並べるため、プロバイダ契約へ任意メンバ `PresetProviderShortDisplayName`（短縮表示名）を追加する。UI は `PresetWindow` のツール行直下にラベル幅に応じた折り返し付きトグル行を足す。

**Tech Stack:** C# (.NET 3.5 相当 / Unity IMGUI)、XmlSerializer ベースの Config、リフレクションによるプロバイダ発見

**Spec:** 別ファイルなし。本ファイルの「仕様詳細」がスペック（ユーザー指示:「カテゴリごとにトグルを用意して、ロード時に反映する内容を制御する(カメラ/メイド/背景/モデル/エフェクト)」「PNG 配置くらいにタイル横幅サイズを調整」＋ 対話で確定した下記の決定事項）

## 仕様詳細

### 決定事項（ユーザー確認済み）

- カテゴリ構成は **固定 3 トグル（カメラ / メイド / 背景）＋ 発見済み外部プロバイダごとに 1 トグル**。
  「モデル」「エフェクト」はプロバイダ（ModItemExplorer.ModelPlacement / PostEffects 等）であり、
  **プロバイダ id をハードコードしない**（CLAUDE.md のハードコーディング回避方針）。
- 「背景」トグルには **背景 (`background`) / ライト (`light`) / PNG 配置 (`pngPlacement`)** をまとめる。
- プロバイダのトグル表示名は長すぎるため（例: `"モデル配置 (ModItemExplorer)"`）、
  契約に **任意メンバ `PresetProviderShortDisplayName`** を追加する。未定義なら `displayName` にフォールバック。
- **SceneCapture プリセットにもトグルを効かせる**（カメラ / 背景 / プロバイダ）。
  SceneCapture はメイドを扱わないため、メイドトグルは無関係。
- 自動ロード（`PreloadAutoLoadScenery` / `UpdateAutoLoad`）も同じトグルに従う。
- タイル横幅は `PngPlacementWindow` と同じ `96`。高さは既存式（サムネ比率 + ラベル 20）のまま追従する。

### ゲート対象の対応表

| 適用対象 | ゲート |
|---|---|
| `CameraSnapshot.ApplyState` | カメラ |
| `BackgroundSnapshot.ApplyState` / `LightSnapshot.ApplyState` / `PngPlacementSnapshot.ApplyState` | 背景 |
| `AssignMaids` / `ReleaseUnassignedMaids` / `ApplyMaid` / `ApplyLooks` / `RequestFocusOnAppliedMaid` | メイド |
| `ApplyExternals` の各 external / `ApplySceneCaptureExternals` の各プロバイダ | 該当プロバイダ |

### 意図的な仕様

- メイドトグル OFF のとき、プリセットにメイドが含まれていても**呼出も解除もしない**
  （`AssignMaids` ごと飛ばすため `ReleaseUnassignedMaids` も走らない）。
- 読込トグルは**保存トグル（`scenePresetSaveCamera` 等）とは独立**。Config キーも別に持つ。
- 保存側 UI（`SavePresetPopupWindow`）は従来どおり `displayName`（長い方）を使う。変更しない。
- SceneCapture 適用時に `applySceneCaptureXml` を持つプロバイダが全部トグル OFF だった場合、
  「適用できる外部プラグインが見つかりません」の警告は**出さない**（ユーザーが意図的に切っているため）。

## Global Constraints

- ビルドコマンド: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
- 同じソースが COM3D2 (2.0) と COM3D2.5 の両方にビルドされる。`ビルドに成功しました` が出て `0 エラー` であること
  （ゲーム起動中は dll のコピーのみ失敗するが想定内）
- **テストフレームワークは存在しない**。検証は「ビルドが通ること」＋「実機での目視確認」
- コードコメント・ログメッセージは日本語
- コミットは Conventional Commits 形式の日本語メッセージ
- Config フィールドの追加は既定値を `true`（従来どおり全適用）にして、既存 Config の読み込み互換を保つ
- `ScenePresetProvider` の契約変更は**任意メンバの追加のみ**。既存プロバイダが未対応でも動くこと

---

### Task 1: Config に読込対象フィールドを追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs`（`scenePresetAutoLoadOnceOnly` 付近、248-256 行あたり）

**Interfaces:**
- Produces: `Config.scenePresetLoadCamera` / `Config.scenePresetLoadMaids` / `Config.scenePresetLoadBackground`（`bool`、既定 `true`）、
  `Config.scenePresetLoadDisabledProviders`（`string`、既定 `""`）。Task 2, 3, 4, 5 が使用

- [ ] **Step 1: フィールドを追加**

`scenePresetDisabledProviders` の直後（`scenePresetAutoLoadKey` の直前）に追加:

```csharp
        // シーンプリセット読込対象 (プリセットウィンドウのトグル状態。ロード時に反映する内容を絞る)。
        // 上の scenePresetSave* / scenePresetDisabledProviders は「保存時に記録する対象」の設定で、
        // こちらは「適用時に反映する対象」の設定。名前が似ているので取り違えに注意
        public bool scenePresetLoadCamera = true;
        public bool scenePresetLoadMaids = true;
        // 背景・ライト・PNG 配置をまとめた「背景」カテゴリ
        public bool scenePresetLoadBackground = true;
        // 読込を無効化した外部プロバイダ id のカンマ区切り (未指定は全有効)
        public string scenePresetLoadDisabledProviders = "";
```

- [ ] **Step 2: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Config.cs
git commit -m "feat(preset): シーンプリセットの読込対象カテゴリを Config に追加する"
```

---

### Task 2: プロバイダ契約に短縮表示名を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetProviderRegistry.cs`（`ScenePresetProvider` の 15-37 行 / `BindProvider` の 210-215 行あたり）
- Modify: `docs/scene-preset-provider-guide.md`（契約仕様の表 71-80 行、サンプルコード 42-62 行）

**Interfaces:**
- Produces: `ScenePresetProvider.shortDisplayName`（`string`。プロバイダ型の任意 public static プロパティ
  `PresetProviderShortDisplayName` から取得し、未定義・空なら `displayName` と同値）。Task 5 が使用

- [ ] **Step 1: ScenePresetProvider に shortDisplayName を追加**

`ScenePresetProvider` の `displayName` フィールド直後に追加:

```csharp
        /// <summary>
        /// トグル行など狭い場所で使う短縮表示名（例: "モデル"）。
        /// 任意メンバ PresetProviderShortDisplayName 未定義のプロバイダでは displayName と同値になる
        /// </summary>
        public string shortDisplayName;
```

- [ ] **Step 2: BindProvider で短縮表示名をバインド**

既存コードは表示名の空フォールバックを初期化子内で
`displayName = string.IsNullOrEmpty(displayName) ? id : displayName,` と書いている。
短縮名も同じフォールバック結果を引き継ぐ必要があるため、**分岐を初期化子の外へ出して先に確定させる**。

`BindProvider` 内の `var provider = new ScenePresetProvider { ... };` のブロックを、
次の 3 つをこの順で並べた形に丸ごと差し替える:

```csharp
            // 表示名が空のプロバイダは id をそのまま出す
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = id;
            }

            // 短縮表示名は任意メンバ。未定義・空なら通常の表示名を流用する
            var shortDisplayName = displayName;
            var shortNameProp = type.GetProperty("PresetProviderShortDisplayName", flags);
            if (shortNameProp != null)
            {
                var value = shortNameProp.GetValue(null, null) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    shortDisplayName = value;
                }
            }

            var provider = new ScenePresetProvider
            {
                id = id,
                displayName = displayName,
                shortDisplayName = shortDisplayName,
                extension = extension,
            };
```

順序が重要: 空フォールバックを先に済ませないと、`displayName` が空のプロバイダで
短縮名だけ空文字列になる。

- [ ] **Step 3: ドキュメントの契約仕様表へ 1 行追加**

`docs/scene-preset-provider-guide.md` の契約仕様表、`PresetProviderDisplayName` の行の直後に追加:

```markdown
| `PresetProviderShortDisplayName` | `string`（プロパティ） | 任意 | プリセットウィンドウの読込トグルに出す短縮名（例: `"モデル"`）。未定義・空なら `PresetProviderDisplayName` が使われる |
```

- [ ] **Step 4: ドキュメントのサンプルコードへ短縮名を追記**

同ファイルのサンプル（`### 2. 契約メンバを持つ型に属性を付ける`）の
`public static string PresetProviderDisplayName => "モデル配置 (ModItemExplorer)";` の直後に追加:

```csharp
    /// <summary>読込トグルなど狭い場所で使う短縮名（任意）</summary>
    public static string PresetProviderShortDisplayName => "モデル";
```

- [ ] **Step 5: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetProviderRegistry.cs docs/scene-preset-provider-guide.md
git commit -m "feat(preset): プロバイダ契約に短縮表示名を追加する"
```

---

### Task 3: ScenePresetManager にプロバイダ読込判定を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`isLoading` プロパティ付近 114-116 行の直後、`Reload()` 139-141 行）

**Interfaces:**
- Consumes: `Config.scenePresetLoadDisabledProviders`（Task 1）
- Produces:
  - `static bool ScenePresetManager.IsProviderLoadEnabled(string providerId)`（Task 4, 5 が使用）
  - `static void ScenePresetManager.SetProviderLoadEnabled(string providerId, bool enabled)`（Task 5 が使用）

- [ ] **Step 1: 判定・更新メソッドを追加**

`public static bool isLoading => ...;` の直後（`CreateRootItem()` の直前）に追加:

```csharp
        // 読込を無効化したプロバイダ id の集合。Config の文字列から作り直したキャッシュで、
        // 毎フレーム参照される UI からの Split 実行を避けるために持つ
        private static HashSet<string> _loadDisabledProviders;
        private static string _loadDisabledProvidersSource;

        /// <summary>Config の CSV からキャッシュを作り直す（内容が変わっていなければ何もしない）</summary>
        private static HashSet<string> GetLoadDisabledProviders()
        {
            var source = config.scenePresetLoadDisabledProviders ?? "";
            if (_loadDisabledProviders == null || _loadDisabledProvidersSource != source)
            {
                _loadDisabledProviders = new HashSet<string>(
                    source.Split(',').Where(id => !string.IsNullOrEmpty(id)));
                _loadDisabledProvidersSource = source;
            }
            return _loadDisabledProviders;
        }

        /// <summary>プロバイダの状態をロード時に反映するか</summary>
        public static bool IsProviderLoadEnabled(string providerId)
        {
            return !GetLoadDisabledProviders().Contains(providerId);
        }

        /// <summary>プロバイダの読込可否を記録する。UI のトグルから呼ばれる</summary>
        public static void SetProviderLoadEnabled(string providerId, bool enabled)
        {
            if (enabled == IsProviderLoadEnabled(providerId))
            {
                return;
            }

            var disabled = new HashSet<string>(GetLoadDisabledProviders());
            if (enabled)
            {
                disabled.Remove(providerId);
            }
            else
            {
                disabled.Add(providerId);
            }

            config.scenePresetLoadDisabledProviders = string.Join(",", disabled.ToArray());
            config.dirty = true;
        }
```

- [ ] **Step 2: Reload でプロバイダ一覧を再走査する**

`public static void Reload()` の先頭 `_loaded = true;` の**直前**に追加:

```csharp
            // 「更新」ボタンで、後からロードされたプラグインのトグルも出せるようにする
            ScenePresetProviderRegistry.Refresh();
```

- [ ] **Step 3: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): プロバイダごとの読込可否を管理できるようにする"
```

---

### Task 4: 適用経路を読込トグルでゲートする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
  - `ApplySceneCapturePreset`（512-530 行あたり）
  - `ApplySceneCaptureExternals`（536-570 行あたり）
  - `Apply`（922-971 行あたり）
  - `ApplyScenery`（977-982 行あたり）
  - `FinishApply`（1194-1199 行あたり）
  - `ApplyExternals`（1345-1393 行あたり）

**Interfaces:**
- Consumes: `Config.scenePresetLoadCamera` / `scenePresetLoadMaids` / `scenePresetLoadBackground`（Task 1）、
  `ScenePresetManager.IsProviderLoadEnabled(string)`（Task 3）

- [ ] **Step 1: ApplyScenery をゲートする**

`ApplyScenery` の本体を差し替える（XML コメントの説明も更新する）:

```csharp
        /// <summary>
        /// メイドを伴わない情景 (カメラ・背景・ライト) を適用する。
        /// 本適用と先出し適用で順序を揃えるため 1 箇所にまとめている。
        /// 読込トグルで OFF にされたカテゴリは触らない
        /// </summary>
        private static void ApplyScenery(ScenePresetData data)
        {
            if (config.scenePresetLoadCamera)
            {
                CameraSnapshot.ApplyState(data.camera);
            }
            // ライトは背景カテゴリに含めている (UI のトグルを 1 つにまとめているため)
            if (config.scenePresetLoadBackground)
            {
                BackgroundSnapshot.ApplyState(data.background);
                LightSnapshot.ApplyState(data.light);
            }
        }
```

- [ ] **Step 2: Apply の PNG 配置とメイド処理をゲートする**

`Apply` の `if (!skipScenery) { ApplyScenery(data); }` から
`FinishApply(data);` の閉じ括弧までを次のとおり差し替える:

```csharp
            if (!skipScenery)
            {
                ApplyScenery(data);
            }
            // PNG 配置も背景カテゴリに含める
            if (config.scenePresetLoadBackground)
            {
                PngPlacementSnapshot.ApplyState(data.pngPlacement);
            }

            // メイドの読込が OFF のときは呼出も解除も行わず、現在のメイドをそのまま残す
            if (config.scenePresetLoadMaids)
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

            // 外部プラグインはメイドを参照することがあるため、
            // 全メイドのロード完了を待ってから反映する
            if (_pendingApplies.Count > 0)
            {
                _pendingExternalsData = data;
            }
            else
            {
                FinishApply(data);
            }
```

- [ ] **Step 3: FinishApply をゲートする**

`FinishApply` を差し替える:

```csharp
        /// <summary>
        /// 全メイドのロード完了後にまとめて行う仕上げ。
        /// 視線は他メイドを参照しうるため、外部プロバイダと同じくここで反映する。
        /// メイドの読込が OFF のときは AssignMaids ごと飛ばしているため、
        /// 無関係な既存メイドへ視線・フォーカスを当てないよう合わせて飛ばす
        /// </summary>
        private static void FinishApply(ScenePresetData data)
        {
            var applyMaids = config.scenePresetLoadMaids;
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

- [ ] **Step 4: ApplyExternals をゲートする**

`ApplyExternals` の `foreach (var external in data.externals)` 直下、
`var provider = ScenePresetProviderRegistry.GetProvider(external.id);` の**直前**に追加:

```csharp
                // 読込トグルで OFF にされたプロバイダは、プラグイン未導入の警告も出さずに飛ばす
                if (!IsProviderLoadEnabled(external.id))
                {
                    continue;
                }

```

- [ ] **Step 5: ApplySceneCapturePreset をゲートする**

`ApplySceneCapturePreset` 内の 3 行

```csharp
            CameraSnapshot.ApplyState(converted.camera);
            BackgroundSnapshot.ApplyState(converted.background);
            LightSnapshot.ApplyState(converted.light);
```

を次に差し替える:

```csharp
            if (config.scenePresetLoadCamera)
            {
                CameraSnapshot.ApplyState(converted.camera);
            }
            if (config.scenePresetLoadBackground)
            {
                BackgroundSnapshot.ApplyState(converted.background);
                LightSnapshot.ApplyState(converted.light);
            }
```

- [ ] **Step 6: ApplySceneCaptureExternals をゲートする**

`ApplySceneCaptureExternals` の `var handled = false;` 以降を次のとおり差し替える:

```csharp
            var handled = false;
            // トグルで切られただけの場合に「見つかりません」と誤解させないための区別
            var skippedByOption = false;
            foreach (var provider in ScenePresetProviderRegistry.providers)
            {
                if (provider.applySceneCaptureXml == null)
                {
                    continue;
                }
                if (!IsProviderLoadEnabled(provider.id))
                {
                    skippedByOption = true;
                    continue;
                }
                handled = true;
                try
                {
                    if (!provider.applySceneCaptureXml(rawXml))
                    {
                        MTEUtils.LogWarning(
                            "SceneCapture プリセットの適用に失敗しました: {0}", provider.id);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("SceneCapture プリセットの適用に失敗しました: " + provider.id);
                    MTEUtils.LogException(e);
                }
            }

            if (!handled && !skippedByOption)
            {
                MTEUtils.LogWarning(
                    "SceneCapture のモデル・エフェクトを適用できる外部プラグインが見つかりません");
            }
```

- [ ] **Step 7: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(preset): 読込対象トグルに従ってプリセットを部分適用する"
```

---

### Task 5: プリセットウィンドウに読込トグル行を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs`（`using` 1-2 行、定数 17-25 行、`DrawContent` 106-137 行）

**Interfaces:**
- Consumes: `Config.scenePresetLoadCamera` / `scenePresetLoadMaids` / `scenePresetLoadBackground`（Task 1）、
  `ScenePresetProvider.shortDisplayName`（Task 2）、
  `ScenePresetManager.IsProviderLoadEnabled` / `SetProviderLoadEnabled`（Task 3）

- [ ] **Step 1: using と定数を追加**

ファイル先頭の using を次のとおりにする（`Action<bool>` のため `System` が要る）:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
```

`ROW_HEIGHT` の直後に追加:

```csharp
        // トグル 1 個分の余白。チェックボックス本体とラベル実寸の差を埋める
        private static readonly float TOGGLE_EXTRA_WIDTH = 20;

        /// <summary>読込トグル行の先頭ラベル幅</summary>
        private static readonly float LOAD_LABEL_WIDTH = 40;
```

- [ ] **Step 2: DrawContent へトグル行の描画を挟む**

`DrawContent` の try ブロックを次のとおりにする:

```csharp
            try
            {
                DrawToolRow(currentDirItem);
                DrawLoadOptionRow();

                _view.DrawHorizontalLine(Color.gray);
                _view.AddSpace(5);

                DrawPresetTiles(currentDirItem);
            }
```

- [ ] **Step 3: トグル行の描画メソッドを追加**

`DrawToolRow` の直後（`DrawPresetTiles` の直前）に追加:

```csharp
        /// <summary>
        /// ロード時に反映するカテゴリのトグル行。
        /// 固定 3 カテゴリ（カメラ・メイド・背景）に続けて、
        /// 発見済みの外部プロバイダを短縮表示名で並べる
        /// </summary>
        private void DrawLoadOptionRow()
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("読込:", LOAD_LABEL_WIDTH, ROW_HEIGHT);

                DrawLoadToggle("カメラ", config.scenePresetLoadCamera,
                    value => config.scenePresetLoadCamera = value);
                DrawLoadToggle("メイド", config.scenePresetLoadMaids,
                    value => config.scenePresetLoadMaids = value);
                // 背景トグルは背景・ライト・PNG 配置をまとめて制御する
                DrawLoadToggle("背景", config.scenePresetLoadBackground,
                    value => config.scenePresetLoadBackground = value);

                foreach (var provider in ScenePresetProviderRegistry.providers)
                {
                    // ラムダのキャプチャ対象をループ変数から切り離す
                    var providerId = provider.id;
                    DrawLoadToggle(
                        provider.shortDisplayName,
                        ScenePresetManager.IsProviderLoadEnabled(providerId),
                        value => ScenePresetManager.SetProviderLoadEnabled(providerId, value));
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// 読込トグル 1 個。ラベル実寸に合わせた幅で描き、
        /// 行に収まらなくなったら次の行へ折り返す
        /// </summary>
        private void DrawLoadToggle(string label, bool value, Action<bool> onChanged)
        {
            var width = GUIView.CalcWidth(GUIView.gsToggle, label) + TOGGLE_EXTRA_WIDTH;

            // 行頭 (x = 0) での折り返しは無限ループになるため、2 個目以降だけ判定する
            var rowWidth = _view.viewRect.width - _view.padding.x * 2;
            if (_view.currentPos.x > 0 && _view.currentPos.x + width > rowWidth)
            {
                _view.EndLayout();
                _view.BeginHorizontal();
            }

            _view.DrawToggle(label, value, width, ROW_HEIGHT, newValue =>
            {
                onChanged(newValue);
                config.dirty = true;
            });
        }
```

補足: `ScenePresetManager.SetProviderLoadEnabled` は内部で `config.dirty = true` を立てるため
プロバイダ分は二重になるが、`dirty` は単純なフラグなので害はない（固定 3 カテゴリ側で必須）。

- [ ] **Step 4: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 5: 実機で目視確認**

ゲームを起動し、メニューバー `Window` → `プリセット` でウィンドウを開いて確認する:

1. ツール行の下に「読込: [カメラ][メイド][背景]」＋プロバイダ分のトグルが出ること
2. 各トグルのラベルが枠内に収まり、隣のトグルと重なっていないこと
   （`TOGGLE_EXTRA_WIDTH = 20` はチェックボックス実描画幅との差を埋める概算値。
   プロバイダの短縮名が長い場合に詰まって見えるようなら値を調整する）
3. ウィンドウ幅を狭めるとトグルが次の行へ折り返し、タイル一覧が押し下げられること。
   トグル 1 個分の幅すら無いところまで狭めても、空行が挟まる程度で描画が壊れないこと
4. 「メイド」を OFF にしてプリセットを適用 → カメラ・背景だけ変わり、メイドの位置・ポーズが変わらないこと
5. 「背景」を OFF にして適用 → 背景・ライト・PNG 配置が変わらないこと
6. 「カメラ」を OFF にして適用 → カメラが動かないこと
7. プロバイダのトグルを OFF にして適用 → そのプラグインの状態が復元されないこと
8. SceneCapture フォルダのプリセットでも 5〜7 が同じように効くこと
9. ゲーム再起動後もトグル状態が保持されること（`Config` への永続化確認）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/PresetWindow.cs
git commit -m "feat(preset): プリセットウィンドウに読込対象トグルを追加する"
```

---

### Task 6: タイル横幅を PNG 配置ウィンドウに揃える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/PresetWindow.cs`（`TILE_WIDTH` 定義）

- [ ] **Step 1: TILE_WIDTH を 96 にする**

```csharp
        // タイルの表示サイズ。サムネの縦横比 + 名前ラベル分の高さ。
        // 幅は PNG 配置ウィンドウ (PngPlacementWindow.TILE_WIDTH) と揃えている
        private static readonly float TILE_WIDTH = 96;
```

`TILE_HEIGHT` の式は変更しない（`TILE_WIDTH` から自動的に追従する）。

- [ ] **Step 2: ビルドして通ることを確認**

Run: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\debug.bat"`
Expected: `ビルドに成功しました`（エラー 0 件）

- [ ] **Step 3: 実機で目視確認**

プリセットウィンドウを開き、タイルが PNG 配置ウィンドウと同程度の大きさで並び、
同じ幅で以前より 1 行あたりの件数が増えていること、サムネが潰れていないことを確認する。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/PresetWindow.cs
git commit -m "style(preset): プリセットタイルの横幅を PNG 配置に揃える"
```

---

### Task 7: 利用者向けドキュメントを更新する

**Files:**
- Modify: `docs-site/guide/scene-preset.md`（「保存と適用」節）
- Modify: `docs-site/guide/configuration.md`（「シーンプリセット」の設定表 95-100 行）

- [ ] **Step 1: scene-preset.md に読込トグルの説明を追加**

`## 保存と適用` 節の箇条書き（`- プリセットは ... に保存されます` の行）の**直後**に追加:

```markdown
- ウィンドウ上部の「読込:」トグルで、プリセット適用時に反映する要素を絞れます
  - `カメラ` / `メイド` / `背景`（背景・ライト・PNG 配置）の 3 つに加え、
    連携プラグインが登録した項目（モデル配置・ポストエフェクトなど）が並びます
  - `メイド` を OFF にすると、プリセットにメイドが含まれていても呼出も解除も行いません
  - このトグルは自動ロードと SceneCapture プリセットの適用にも効きます
```

- [ ] **Step 2: configuration.md の設定表へ追記**

`## シーンプリセット` の表、`scenePresetSaveMaids` の行の直後に追加:

```markdown
| `scenePresetLoadCamera` | `true` | 読込トグル「カメラ」の状態 |
| `scenePresetLoadMaids` | `true` | 読込トグル「メイド」の状態 |
| `scenePresetLoadBackground` | `true` | 読込トグル「背景」（背景・ライト・PNG 配置）の状態 |
| `scenePresetLoadDisabledProviders` | 空 | 読込を無効にした連携プラグイン id のカンマ区切り |
```

- [ ] **Step 3: コミット**

```bash
git add docs-site/guide/scene-preset.md docs-site/guide/configuration.md
git commit -m "docs(preset): 読込対象トグルの説明を追加する"
```

---

## 実装後の必須手順

- [ ] **code-review スキルでレビューする**（CLAUDE.md のワークフロー規約。実装完了後・ユーザー提示前に必須）
