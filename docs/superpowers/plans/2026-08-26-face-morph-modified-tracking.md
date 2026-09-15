# 表情モーフ変更追跡(チェックボックス)実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 表情モーフに「ユーザーが変更した」チェックを付け、チェック済み項目のみプリセット保存・タイムライン表示/書き込み対象にする。

**Architecture:** 変更追跡はメイド単位の `FaceEditStore`(モーフ生名の HashSet + version カウンタ)に集約し、`FaceEditManager`(ManagerBase 準拠シングルトン)が Maid → Store を管理する。UI(MaidFaceWindow)・履歴(FaceSnapshot)・プリセット(ScenePresetManager / MaidFacePresetManager)・タイムライン(MorphTimelineLayer)はすべてこのストアを参照する。

**Tech Stack:** C# (net48 / LangVersion 9 はテストのみ、プラグイン本体は旧形式 csproj)、xUnit、MSBuild(COM3D2 / COM3D25 の 2 構成)

**Spec:** `docs/superpowers/specs/2026-08-26-face-morph-modified-tracking-design.md`

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` は使わない**(ゲームフォルダへ DLL をコピーするため)。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
  - 環境変数 `COM3D2_DIR` / `COM3D25_DIR` が未設定でエラーになる場合のみ `/p:COM3D2_DIR="W:\COM3D2"` `/p:COM3D25_DIR="W:\COM3D2_5"` を付ける
- テストは COM3D25 構成のプラグイン DLL を参照する。**テスト実行前に必ず COM3D25 構成をビルド**してから `dotnet test source\COM3D2.SceneEditor.Plugin.Tests` を実行する
- プラグイン本体の csproj は旧形式で `<Compile Include="...">` を明示列挙している。**新規 .cs は必ず csproj へ追加する**(`source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の L198 付近、`MaidManipulation\BoneEditStore.cs` の近く)
- `deploy.bat` / `release.bat` は実行しない
- 実機での動作確認(表示崩れ等)はユーザーに依頼する。エージェントはビルドとテストまで

---

### Task 1: FaceEditStore(変更追跡ストア)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(Compile 追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/FaceEditStoreTests.cs`

**Interfaces:**
- Consumes: なし(純粋なコレクションクラス)
- Produces: `class FaceEditStore` — `bool IsModified(string name)` / `void Mark(string name)` / `void Unmark(string name)` / `void SetNames(IEnumerable<string> names)` / `List<string> GetNames()` / `void Clear()` / `int version { get; }` / `bool isEmpty { get; }`。後続タスク全てがこの API を使う

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/FaceEditStoreTests.cs`:

```csharp
using System.Linq;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // FaceEditStore はプリセット保存とタイムライン絞り込みの共通ソース。
    // version はタイムライン側の再構築検知に使うため「実際に集合が変わったときだけ」増える
    public class FaceEditStoreTests
    {
        [Fact]
        public void Markでチェック済みになる()
        {
            var store = new FaceEditStore();
            store.Mark("eyeclose");
            Assert.True(store.IsModified("eyeclose"));
            Assert.False(store.IsModified("eyeclose2"));
            Assert.False(store.isEmpty);
        }

        [Fact]
        public void Unmarkで解除される()
        {
            var store = new FaceEditStore();
            store.Mark("eyeclose");
            store.Unmark("eyeclose");
            Assert.False(store.IsModified("eyeclose"));
            Assert.True(store.isEmpty);
        }

        [Fact]
        public void SetNamesで丸ごと置き換わる()
        {
            var store = new FaceEditStore();
            store.Mark("eyeclose");
            store.SetNames(new[] { "mayuup", "mouthup" });
            Assert.False(store.IsModified("eyeclose"));
            Assert.True(store.IsModified("mayuup"));
            Assert.True(store.IsModified("mouthup"));
        }

        [Fact]
        public void 集合が変わったときだけversionが増える()
        {
            var store = new FaceEditStore();
            var v0 = store.version;

            store.Mark("eyeclose");
            var v1 = store.version;
            Assert.True(v1 > v0);

            // 既にチェック済みの Mark では増えない
            store.Mark("eyeclose");
            Assert.Equal(v1, store.version);

            // 未チェックの Unmark でも増えない
            store.Unmark("mayuup");
            Assert.Equal(v1, store.version);

            store.Unmark("eyeclose");
            Assert.True(store.version > v1);
        }

        [Fact]
        public void null名と空文字は無視される()
        {
            var store = new FaceEditStore();
            store.Mark(null);
            store.Mark("");
            Assert.True(store.isEmpty);
            Assert.False(store.IsModified(null));
            store.SetNames(new[] { "eyeclose", null, "" });
            Assert.Single(store.GetNames());
        }

        [Fact]
        public void GetNamesはコピーを返す()
        {
            var store = new FaceEditStore();
            store.Mark("eyeclose");
            var names = store.GetNames();
            names.Clear();
            Assert.True(store.IsModified("eyeclose"));
        }

        [Fact]
        public void Clearで空になる()
        {
            var store = new FaceEditStore();
            store.Mark("eyeclose");
            var v1 = store.version;
            store.Clear();
            Assert.True(store.isEmpty);
            Assert.True(store.version > v1);

            // 空のときの Clear では増えない
            var v2 = store.version;
            store.Clear();
            Assert.Equal(v2, store.version);
        }
    }
}
```

- [ ] **Step 2: テストが失敗(コンパイルエラー)することを確認する**

Run: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter FaceEditStoreTests`
Expected: `FaceEditStore` 未定義のコンパイルエラーで FAIL

- [ ] **Step 3: FaceEditStore を実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 人分の表情モーフ変更追跡。ユーザーが明示的に編集した (=チェック済みの) モーフ生名を持つ。
    /// プリセット保存の対象選別とタイムラインのボーンメニュー絞り込みの共通ソース。
    /// 編集側 (FaceMorphDef) とタイムライン側 (FaceMorphUtils) でモーフ名テーブルが別物のため、
    /// 両者が共通に扱える生名文字列をキーにする
    /// </summary>
    public class FaceEditStore
    {
        private readonly HashSet<string> _modifiedNames = new HashSet<string>();

        /// <summary>変更検知用。集合が実際に変わったときだけ増える (タイムラインのメニュー再構築判定に使う)</summary>
        public int version { get; private set; }

        public bool isEmpty => _modifiedNames.Count == 0;

        public bool IsModified(string name)
        {
            return name != null && _modifiedNames.Contains(name);
        }

        public void Mark(string name)
        {
            if (!string.IsNullOrEmpty(name) && _modifiedNames.Add(name))
            {
                version++;
            }
        }

        public void Unmark(string name)
        {
            if (name != null && _modifiedNames.Remove(name))
            {
                version++;
            }
        }

        /// <summary>集合を丸ごと置き換える。プリセット適用と履歴復元に使う</summary>
        public void SetNames(IEnumerable<string> names)
        {
            _modifiedNames.Clear();
            if (names != null)
            {
                foreach (var name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        _modifiedNames.Add(name);
                    }
                }
            }
            version++;
        }

        /// <summary>列挙中の変更に耐えるようコピーを返す</summary>
        public List<string> GetNames()
        {
            return _modifiedNames.ToList();
        }

        public void Clear()
        {
            if (_modifiedNames.Count > 0)
            {
                _modifiedNames.Clear();
                version++;
            }
        }
    }
}
```

csproj へ追加(`<Compile Include="MaidManipulation\BoneEditStore.cs" />` の隣):

```xml
    <Compile Include="MaidManipulation\FaceEditStore.cs" />
```

- [ ] **Step 4: テストが通ることを確認する**

Run: COM3D25 構成をビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter FaceEditStoreTests`
Expected: 全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditStore.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/FaceEditStoreTests.cs
git commit -m "feat(face): 表情モーフの変更追跡ストアを追加する"
```

---

### Task 2: FaceEditManager(Maid 別レジストリ)とフォトプリセット適用時のマーク

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`(Compile 追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:373` 付近(RegisterManager)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs:200-230` 付近(`ApplyPhotoFacePreset`)

**Interfaces:**
- Consumes: Task 1 の `FaceEditStore`
- Produces: `FaceEditManager.instance`(シングルトン)、`FaceEditStore GetStore(Maid maid)`(無ければ作る)、`FaceEditStore FindStore(Maid maid)`(無ければ null)。後続タスクはこの 2 メソッドでストアへアクセスする

- [ ] **Step 1: FaceEditManager を実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditManager.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドごとの FaceEditStore を管理する。消えたメイドの記録は毎フレームの掃除で捨てる</summary>
    public class FaceEditManager : ManagerBase
    {
        private static FaceEditManager _instance;
        public static FaceEditManager instance => _instance ?? (_instance = new FaceEditManager());

        private FaceEditManager()
        {
        }

        private readonly Dictionary<Maid, FaceEditStore> _stores = new Dictionary<Maid, FaceEditStore>();
        private readonly List<Maid> _deadMaids = new List<Maid>();

        public FaceEditStore GetStore(Maid maid)
        {
            FaceEditStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new FaceEditStore();
                _stores[maid] = store;
            }
            return store;
        }

        public FaceEditStore FindStore(Maid maid)
        {
            FaceEditStore store;
            return maid != null && _stores.TryGetValue(maid, out store) ? store : null;
        }

        public override void Update()
        {
            // 破棄済みメイドの記録を掃除する (BoneEditManager.UpdateStores と同じ方式)
            _deadMaids.Clear();
            foreach (var pair in _stores)
            {
                if (pair.Key == null)
                {
                    _deadMaids.Add(pair.Key);
                }
            }
            foreach (var maid in _deadMaids)
            {
                _stores.Remove(maid);
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる (BoneEditManager と同じ方式)
            _stores.Clear();
        }
    }
}
```

ファイル先頭の using に `UnityEngine.SceneManagement`(`Scene` / `LoadSceneMode` 用)を追加する:

```csharp
using UnityEngine.SceneManagement;
```

csproj へ追加(FaceEditStore.cs の隣):

```xml
    <Compile Include="MaidManipulation\FaceEditManager.cs" />
```

- [ ] **Step 2: マネージャーを登録する**

`COM3D2.SceneEditor.Plugin.cs` の `managerRegistry.RegisterManager(BoneEditManager.instance);`(L373)の直後に追加:

```csharp
                managerRegistry.RegisterManager(FaceEditManager.instance);
```

- [ ] **Step 3: フォトプリセット適用時にチェック集合を置き換える**

`MaidFaceMorphController.ApplyPhotoFacePreset` のメソッド末尾(`morph.MulBlendValues(settingName, 1f);` 実行後)に追加。プリセット適用は表情の総入れ替えなので、非 0 になったモーフをチェック済みとして丸ごと置き換える(シーンプリセット保存でプリセット由来の表情が欠落しないようにするため):

```csharp
            // プリセット適用は表情の総入れ替え。非 0 のモーフをチェック済みへ置き換え、
            // シーンプリセット保存 (チェック済みのみ保存) で表情が欠落しないようにする
            var modifiedNames = new List<string>();
            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in GetAvailableMorphs(maid, category))
                {
                    if (GetMorphValue(maid, def) != 0f)
                    {
                        modifiedNames.Add(def.name);
                    }
                }
            }
            FaceEditManager.instance.GetStore(maid).SetNames(modifiedNames);
```

ファイル先頭の using に `System`(`Enum` 用)と `System.Collections.Generic`(`List<>` 用)が無ければ追加する。

- [ ] **Step 4: 両構成でビルドが通ることを確認する**

Run: COM3D25 構成 → COM3D2 構成の順で MSBuild(Global Constraints のコマンド)
Expected: 両方ビルド成功

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/FaceEditManager.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceMorphController.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(face): メイド別の表情変更追跡マネージャーを追加する"
```

---

### Task 3: 表情ウィンドウにチェックボックス列を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:194-261`(ヘッダーのリセットと `DrawMorphList`)

**Interfaces:**
- Consumes: `FaceEditManager.instance.GetStore/FindStore`、`FaceEditStore.IsModified/Mark/Unmark`
- Produces: なし(UI のみ)

- [ ] **Step 1: DrawMorphList にチェックボックス列を追加する**

`MaidFaceWindow.DrawMorphList`(L213-261)の foreach 本体を次の形に書き換える。各行の先頭に幅 20px のラベル無しチェックボックスを置き、既存のトグル/スライダーを `BeginHorizontal` で横に並べる。トグル幅 150→130、スライダーの labelWidth は `LABEL_WIDTH - 20` に詰めて右端を揃える:

```csharp
            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(target, currentMorphCategory))
            {
                var value = MaidFaceMorphController.GetMorphValue(target, def);
                var store = FaceEditManager.instance.FindStore(target);
                var isModified = store != null && store.IsModified(def.name);

                view.BeginHorizontal();
                {
                    // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
                    // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
                    view.DrawToggle(isModified, 20, ROW_HEIGHT, newChecked =>
                    {
                        if (newChecked)
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情変更マーク: " + def.displayName);
                            FaceEditManager.instance.GetStore(target).Mark(def.name);
                        }
                        else
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情変更解除: " + def.displayName);
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, 0f);
                            FaceEditManager.instance.GetStore(target).Unmark(def.name);
                        }
                    });

                    if (def.isToggle)
                    {
                        view.DrawToggle(def.displayName, value >= 0.5f, 130, ROW_HEIGHT, newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情: " + def.displayName);
                            // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                            FaceEditManager.instance.GetStore(target).Mark(def.name);
                        });
                    }
                    else
                    {
                        view.DrawSliderValue(new GUIView.SliderOption
                        {
                            label = def.displayName,
                            labelWidth = LABEL_WIDTH - 20,
                            width = -1,
                            min = 0f,
                            max = 1f,
                            step = 0.01f,
                            defaultValue = 0f,
                            value = value,
                            onChanged = newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                    "表情: " + def.displayName);
                                // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                                MaidFaceMorphController.SetMabataki(target, false);
                                MaidFaceMorphController.SetMorphValue(target, def, newValue);
                                FaceEditManager.instance.GetStore(target).Mark(def.name);
                            },
                        });
                    }
                }
                view.EndLayout();
            }
```

補足: `GUIView.GetDrawRect` は `width < 0` のとき `currentPos.x` を差し引いた残り幅を自動計算するため、チェックボックス(20px)描画後の `width = -1` はそのまま残り幅に縮む(レビューで確認済み)。実機確認で万一崩れていた場合のみ `width = view.viewRect.width - view.padding.x * 2 - 20` の明示指定に切り替える。

- [ ] **Step 2: カテゴリリセットでチェックも解除する**

`DrawHeader` のリセットボタン処理(L200-205)の `ResetCategory` 呼び出し直後に追加:

```csharp
                        // リセットは未編集状態へ戻す操作なので、カテゴリ内のチェックも外す
                        var store = FaceEditManager.instance.FindStore(target);
                        if (store != null)
                        {
                            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(
                                target, currentMorphCategory))
                            {
                                store.Unmark(def.name);
                            }
                        }
```

- [ ] **Step 3: 両構成でビルドが通ることを確認する**

Run: COM3D25 構成 → COM3D2 構成で MSBuild
Expected: 両方ビルド成功

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs
git commit -m "feat(face): 表情ウィンドウに変更追跡チェックボックスを追加する"
```

---

### Task 4: 履歴(FaceSnapshot)へのチェック集合の組み込み

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/FaceSnapshot.cs`

**Interfaces:**
- Consumes: `FaceEditManager.instance.GetStore/FindStore`、`FaceEditStore.GetNames/SetNames`
- Produces: なし(既存の `IStateSnapshot` 経由で HistoryManager が使う)

- [ ] **Step 1: FaceSnapshot にチェック集合を追加する**

`FaceSnapshot.cs` を次のように変更する。

フィールド追加(`_mabataki` の下):

```csharp
        private List<string> _modifiedNames;
```

`Capture` の `snapshot._mabataki = ...` 行の直後に追加:

```csharp
            var store = FaceEditManager.instance.FindStore(maid);
            snapshot._modifiedNames = store != null ? store.GetNames() : new List<string>();
```

`Apply` の末尾(モーフ書き戻しループの後)に追加:

```csharp
            FaceEditManager.instance.GetStore(maid).SetNames(_modifiedNames);
```

`Approximately` の morph 比較の後(`return true;` の前)にチェック集合の比較を追加し、集合が違えば false を返す:

```csharp
            // チェック集合の差も履歴対象。集合だけ変えた操作 (手動チェック ON) も undo できるようにする
            if (_modifiedNames.Count != o._modifiedNames.Count)
            {
                return false;
            }
            foreach (var name in _modifiedNames)
            {
                if (!o._modifiedNames.Contains(name))
                {
                    return false;
                }
            }
```

- [ ] **Step 2: 両構成でビルドが通ることを確認する**

Run: COM3D25 構成 → COM3D2 構成で MSBuild
Expected: 両方ビルド成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/FaceSnapshot.cs
git commit -m "feat(face): 表情履歴に変更追跡チェックを含める"
```

---

### Task 5: プリセット保存/適用をチェック済みベースへ変更

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1026-1036`(Capture)、`:2539-2568`(ApplyFace)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFacePresetManager.cs:155-203`(Capture / Apply)
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:663-668`(バージョン)

**Interfaces:**
- Consumes: `FaceEditManager.instance.GetStore/FindStore`、`FaceEditStore.IsModified/SetNames`
- Produces: なし(XML 構造は不変。保存対象の選別ルールだけ変わる)

- [ ] **Step 1: ScenePresetManager の表情 Capture をチェック済みフィルタへ変更する**

L1026-1036 のループを次に置き換える。値 0 でもチェック済みなら保存する(「明示的に 0」の意図を保持):

```csharp
            // チェック済み (=ユーザーが編集した) モーフだけ保存する。値 0 も明示編集なら保存する
            var faceStore = FaceEditManager.instance.FindStore(maid);
            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    if (faceStore != null && faceStore.IsModified(def.name))
                    {
                        state.morphs.Add(new ScenePresetMorph
                        {
                            name = def.name,
                            value = MaidFaceMorphController.GetMorphValue(maid, def),
                        });
                    }
                }
            }
```

- [ ] **Step 2: ApplyFace で保存項目をチェック済みへ復元する**

`ApplyFace`(L2539-2568)の `MaidFaceMorphController.SetMabataki(maid, state.mabataki);` の直前に追加:

```csharp
            // 保存されているモーフ=保存時のチェック済み集合。ロード後すぐ編集を継続できるよう復元する
            FaceEditManager.instance.GetStore(maid).SetNames(savedValues.Keys);
```

- [ ] **Step 3: MaidFacePresetManager も同じルールへ変更する**

`Capture`(L156-176)のループを次に置き換え、doc コメントを「チェック済みのモーフだけ持つ」へ更新する:

```csharp
        /// <summary>現在の表情から保存データを組み立てる。チェック済み (=ユーザーが編集した) モーフだけ持つ</summary>
        private static FacePresetData Capture(Maid maid)
        {
            var data = new FacePresetData
            {
                mabataki = MaidFaceMorphController.GetMabataki(maid),
            };

            var store = FaceEditManager.instance.FindStore(maid);
            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    if (store != null && store.IsModified(def.name))
                    {
                        data.morphs.Add(new FacePresetMorph
                        {
                            name = def.name,
                            value = MaidFaceMorphController.GetMorphValue(maid, def),
                        });
                    }
                }
            }

            return data;
        }
```

`Apply`(L181-203)の `SetMabataki` 直前に追加:

```csharp
            // 保存されているモーフ=保存時のチェック済み集合として復元する
            FaceEditManager.instance.GetStore(maid).SetNames(savedValues.Keys);
```

- [ ] **Step 4: シーンプリセットのバージョンを上げる**

`ScenePresetData.cs` L665 の `CurrentVersion` を 22 へ変更し、L650-664 のバージョン履歴コメント末尾に追記:

```csharp
        // v22: 表情モーフの保存対象を「値が非 0」から「チェック済み (FaceEditStore)」へ変更。
        //      構造変更なし。旧データは記載モーフ (=非 0 保存分) を適用時にチェック済みへ復元する
```

```csharp
        public static readonly int CurrentVersion = 22;
```

- [ ] **Step 5: ビルドと既存テストの確認**

Run: COM3D25 構成をビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`(XmlRoundTripTests 等の既存テストが通ること)→ COM3D2 構成もビルド
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFacePresetManager.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs
git commit -m "feat(face): プリセット保存対象をチェック済みモーフに変更する"
```

---

### Task 6: タイムライン(MorphTimelineLayer)の絞り込み

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs`

**Interfaces:**
- Consumes: `FaceEditManager.instance.FindStore/GetStore`、`FaceEditStore.IsModified/Mark/version`、`FrameData.boneNames`(既存)、`TimelineLayerBase.keyFrames / Update() / InitMenuItems()`(既存)
- Produces: なし

- [ ] **Step 1: allBoneNames を「チェック済み ∪ 既存キーフレーム記載」へ変更する**

`MorphTimelineLayer.cs` の `public override List<string> allBoneNames => FaceMorphUtils.saveMorphNames;`(L16)を削除し、以下のフィールドとメソッドを追加する。namespace が `COM3D2.MotionTimelineEditor.Plugin` のため、`FaceEditManager` / `FaceEditStore` の参照にはファイル先頭へ `using COM3D2.SceneEditor.Plugin;` を追加する(既存 using と衝突する型名があれば完全修飾に切り替える):

```csharp
        private List<string> _cachedBoneNames;

        public override List<string> allBoneNames
            => _cachedBoneNames ?? (_cachedBoneNames = BuildBoneNames());

        private FaceEditStore FindFaceStore()
        {
            var maid = this.maid;
            return maid != null ? FaceEditManager.instance.FindStore(maid) : null;
        }

        /// <summary>
        /// チェック済みモーフ ∪ 既存キーフレーム記載モーフ。表示順は saveMorphNames に揃える。
        /// 既存キーフレーム分を含めるのは、チェックを外しても保存済みアニメの編集を可能なままにするため
        /// </summary>
        private List<string> BuildBoneNames()
        {
            var store = FindFaceStore();

            var keyFrameNames = new HashSet<string>();
            foreach (var frame in keyFrames)
            {
                foreach (var name in frame.boneNames)
                {
                    keyFrameNames.Add(name);
                }
            }

            var result = new List<string>();
            foreach (var name in FaceMorphUtils.saveMorphNames)
            {
                if ((store != null && store.IsModified(name)) || keyFrameNames.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }
```

- [ ] **Step 2: 対象集合の変化でボーンメニューを再構築する**

`Update` override を追加する(既存に `Update` は無い。`LateUpdate` はあるので混同しないこと)。

注意: `TimelineManager.Update` は**全レイヤーの Update を毎フレーム無条件に呼ぶ**うえ、`TimelineLayerBase.keyFrames` プロパティは呼ぶたびにリストをフルコピーする。毎フレーム `BuildBoneNames()` を回すとキーフレーム数×メイド数に比例したコストが常時かかるため、チェック変更(store.version)は毎フレームの軽量チェックで即時反映し、キー削除など store 以外由来の変化は 30 フレームごとの間引き再計算で拾う:

```csharp
        private int _lastStoreVersion = -1;
        private FaceEditStore _lastStore;
        private int _rebuildCheckFrameCount;

        public override void Update()
        {
            base.Update();

            // チェック変更 (store.version) は毎フレームの整数比較だけで検知する。
            // キー削除など store 以外由来の集合変化は 30 フレームごとの間引き再計算で拾う
            // (BuildBoneNames は keyFrames のフルコピーを伴うため毎フレームは回さない)
            var store = FindFaceStore();
            var version = store != null ? store.version : -1;
            var storeChanged = store != _lastStore || version != _lastStoreVersion;

            _rebuildCheckFrameCount++;
            if (!storeChanged && _rebuildCheckFrameCount < 30)
            {
                return;
            }
            _rebuildCheckFrameCount = 0;
            _lastStore = store;
            _lastStoreVersion = version;

            var newNames = BuildBoneNames();
            if (_cachedBoneNames == null || !newNames.SequenceEqual(_cachedBoneNames))
            {
                _cachedBoneNames = newNames;
                InitMenuItems();
            }
        }
```

`SequenceEqual` 用に `using System.Linq;` が無ければ追加する。

- [ ] **Step 3: InitMenuItems を対象集合で絞る**

`InitMenuItems`(L34-57)の foreach 先頭に絞り込みを追加する:

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var targetNames = new HashSet<string>(allBoneNames);
            var setMenuItemMap = new Dictionary<string, BoneSetMenuItem>(10);

            foreach (var pair in FaceMorphUtils.morphNameToSetNameMap)
            {
                var morphName = pair.Key;
                var morphSetName = pair.Value;

                // 対象外 (未チェックかつキーフレーム未記載) のモーフは行を出さない
                if (!targetNames.Contains(morphName))
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem;
                if (!setMenuItemMap.TryGetValue(morphSetName, out setMenuItem))
                {
                    var displaySetName = FaceMorphUtils.GetMorphSetJpName(morphSetName);
                    setMenuItem = new BoneSetMenuItem(morphSetName, displaySetName);
                    setMenuItemMap[morphSetName] = setMenuItem;
                    allMenuItems.Add(setMenuItem);
                }

                var displayName = FaceMorphUtils.GetMorphJpName(morphName);
                setMenuItem.AddChild(new BoneMenuItem(morphName, displayName));
            }
        }
```

`UpdateFrame`(L169-183)は `allBoneNames` を回しているため変更不要(自動的にチェック済み ∪ 既存記載のみ書き込まれる)。

既知の制約(仕様として許容): 編集側の `nosefook`(鼻フック)はタイムライン側テーブル(`FaceMorphUtils.saveMorphNames`)に存在しないため、チェックしてもタイムラインには現れない。これは本機能導入前からの制約(従来も `allBoneNames` は `saveMorphNames` 固定)で、今回はテーブル追加を行わない(タイムラインのデータ互換に関わるためスコープ外)。

- [ ] **Step 4: タイムライン側スライダー編集でもマークする**

`SetMorphValue`(L158-167)の先頭に追加(タイムラインのレイヤーウィンドウで編集したモーフが即座にメニューへ現れるようにする):

```csharp
            // レイヤーウィンドウからの編集もユーザーの明示編集なのでチェックを付ける。
            // 強制上書き (_isForceUpdate) のプレビュー書き込み中も、
            // そのモーフを編集する意図は同じなのでマークする (仕様)
            var maid = this.maid;
            if (maid != null)
            {
                FaceEditManager.instance.GetStore(maid).Mark(morphName);
            }
```

- [ ] **Step 5: ビルドとテストの確認**

Run: COM3D25 構成をビルド → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests` → COM3D2 構成もビルド
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs
git commit -m "feat(timeline): 表情レイヤーの表示と書き込みをチェック済みモーフに絞る"
```

---

### Task 7: 最終確認

**Files:**
- なし(検証のみ)

**Interfaces:**
- Consumes: 全タスクの成果物
- Produces: なし

- [ ] **Step 1: クリーンな全体ビルド確認**

Run: COM3D25 構成 → COM3D2 構成で MSBuild、`dotnet test source\COM3D2.SceneEditor.Plugin.Tests`(フィルタ無しで全テスト)
Expected: 両ビルド成功、全テスト PASS

- [ ] **Step 2: code-review スキルでレビューする**

実装完了後、ユーザーへ提示する前に code-review スキルを起動し、指摘を取り込む(プロジェクトの必須工程)。

- [ ] **Step 3: 実機確認の依頼事項をまとめてユーザーへ報告する**

以下をユーザーの実機確認項目として提示する:
1. 表情タブの各行にチェックボックスが出る。スライダー/トグル操作で自動チェック、手動 OFF で値が 0 に戻る
2. チェックボックス追加でスライダー行のレイアウトが崩れていない(崩れていたら Task 3 Step 1 の注意書きに従い幅を明示指定して修正)
3. シーンプリセット保存 → ロードでチェック済みモーフだけが復元され、チェック状態も戻る
4. タイムラインのメイド表情レイヤーで、ボーンメニューにチェック済みモーフ(+既存キーフレーム分)だけが表示される
5. チェック 0 件・キーフレーム無しのときボーンメニューが空になる(仕様通り)

## レビュー却下メモ

- 指摘なし(plan-reviewer の指摘 5 件はすべて取り込み: Update の間引き化、nosefook 制約の明記、OnChangedSceneLevel でのストアクリア、強制上書き中マークの仕様明記、width=-1 注記の是正)
