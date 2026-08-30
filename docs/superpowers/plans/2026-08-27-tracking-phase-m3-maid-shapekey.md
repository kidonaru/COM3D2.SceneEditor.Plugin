# 変更追跡 Phase M3: メイドシェイプキー 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイドの任意シェイプキーについて、「どのシェイプキーを編集対象として扱うか」のソース・オブ・トゥルースを per-Maid の `EditTargetStore` へ一本化し、`ShapeKeyEditWindow` のメイドタブから直接チェックできるようにする。タイムライン側の既存 opt-in（`TimelineData.maidShapeKeysMap`）へは片方向同期し、シーンプリセットの `shapeKeys` 保存もチェック済みへ絞る。

**Architecture:** 他フェーズと決定的に違う点として、**タイムライン側に既に完成した opt-in 機構がある**。`ShapeKeyTimelineLayer.allBoneNames` は `timeline.GetMaidShapeKeys(slotNo)` そのもので、`OnShapeKeyAdded` が 0F 自動キー（`AddFirstBones`）、`OnShapeKeyRemoved` が全フレームキー削除（`RemoveAllBones`）を既に行う。つまり M0 の追跡部品を接続する必要は無い（接続すると二重管理になる）。

やるべきは**主導権の移動**である。現在タグ登録はレイヤー編集ウィンドウのトグル（`ShapeKeyTimelineLayer.cs:219-233`）が `timeline.AddMaidShapeKey/RemoveMaidShapeKey` を直接叩いている。これを per-Maid `EditTargetStore` へ向け、ストアの変更を毎フレーム `timeline` へ片方向同期する。タイムライン側の 0F 自動キー・キー削除は同期の結果として従来どおり発火する。

**Tech Stack:** C#(プラグイン本体は旧形式 csproj / `<Compile Include>` 明示列挙)、xUnit(net48、COM3D25 構成のプラグイン DLL 参照)、MSBuild 2 構成(COM3D2 / COM3D25)

**Spec:** `docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の「Phase M3」節

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` / `deploy.bat` / `release.bat` は実行しない**。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
- テストは COM3D25 構成の DLL を参照するため、**COM3D25 構成ビルドを最後に流してから `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`** を実行する
- 新規 .cs は `source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ追加する
- xUnit は Unity ランタイム無しで動くため、`Maid` / `TBody` を触るコードはテストできない。同期の差分計算だけを純粋関数へ切り出してテストする
- **HistoryManager 対応はスコープ外**（M2 と同じ縮退判断。`ShapeKeyEditWindow` は SE の履歴に未対応）。チェックの ON/OFF は undo で戻らない
- **ShapeKey / Eyes レイヤーと MaidFaceWindow の相互排他はスコープ外**（`timeline-remaining-work.md` §4 の既知課題。このフェーズでは解決しない）
- 実機確認はユーザーに依頼する（このセッションでゲームを操作しない）

## 設計確定（このフェーズ最大の作業）

### D1. ソース・オブ・トゥルースと同期の向き

| 主体 | 役割 |
|---|---|
| `MaidShapeKeyEditManager` の per-Maid `EditTargetStore` | **唯一のソース**。チェック集合を持つ。タイムライン非ロードでも生きる |
| `TimelineData.maidShapeKeysMap` | ストアの投影。タイムライン XML への永続化とレイヤーの `allBoneNames` を担う |

同期は **ストア → タイムライン の片方向**。毎フレーム差分を取り、増えたタグは `timeline.AddMaidShapeKey`、減ったタグは `timeline.RemoveMaidShapeKey` を呼ぶ。この 2 つが既存の `OnShapeKeyAdded` / `OnShapeKeyRemoved` を発火し、0F 自動キーとキー削除が従来どおり行われる。**タイムライン側の API を直接叩く経路は残さない**（レイヤー編集ウィンドウのトグルも Task 4 でストア向きへ変える）。

逆方向はタイムラインのロード時の初期反映のみ（D2）。

これは `timeline-remaining-work.md` §3 の「キーフレーム対象タグの登録はレイヤー編集ウィンドウの責務」という過去決定を**更新する**（Task 7 でドキュメントを直す）。

### D2. タイムラインのロード時に、ストアとマップのどちらを勝たせるか

**取り込みの契機は `OnLoad` ではなく「タイムラインの参照が変わったこと」にする。**

`Timeline/Manager/TimelineManager.cs` が呼ぶ `mte.OnLoad()` は `MotionTimelineEditor.OnLoad()`（`Timeline/MotionTimelineEditor.cs:56-73`）で、**SE の `managerRegistry` とは別の専用リスト**を回す。そこに登録されているのは `TimelineIntegration.cs:398` の `TimelineUpdateManager` 1 件だけ。一方 SE の `managerRegistry.OnLoad()` は `SceneEditorPlugin.OnLoad()` からしか呼ばれず、それは `OnPluginEnable()`（UI をトグルで有効化した瞬間）でしか走らない。

つまり `ManagerBase.OnLoad()` を override しても**タイムラインの新規作成・読み込みでは発火しない**。両方のレジストリへ登録する手もあるが、依存する配線が増えるうえ `OnLoad` と `Update` の順序に暗黙の前提ができる。そこで同期処理の中で「タイムラインの参照が前回と変わったか」を見て取り込む。これなら新規作成・読み込み・差し替えのどの経路でも 1 か所で捉えられる。

取り込みの規則:

- タイムラインのマップ（`maidShapeKeysMap`）を走査し、**タグを持つ**スロットについては、そのメイドのストアをマップの内容で置き換える（`SetNames`）。読み込んだタイムラインが持つ opt-in を正とする
- **マップに載っていない / タグが空**のメイドはストアを保持する。直後の差分同期がストアの内容をタイムラインへ流し込む

これで「シェイプキーを編集 → タイムラインを新規作成」でチェックが消えず（新規タイムラインのマップは空）、「既存タイムラインを読み込む」ではそのタイムラインの opt-in が復元される。

マップ側の走査でストアを作るので、**そのメイドを一度も操作していなくても読み込み直後からチェックが表示される**（`FindStore` が null を返さなくなる）。

**副作用として明記すること:** タグを 1 つも持たない既存タイムラインを読み込んだ場合、直前のセッションのチェックが残ったまま流し込まれる。実害は「使わないタグに 0F キーが付く」程度で、チェックを外せば消える。

**やってはいけないこと:** `ManagerBase.OnLoad()` を override して取り込みを書くこと。上記のとおり発火しない。将来 `OnLoad` へ戻したくなったら、先に `MTEP.MotionTimelineEditor.instance.RegisterManager()` への登録が要る点を確認すること。

### D3. シーンプリセットの保存・適用の非対称

- **保存**: 「値が非 0」→「チェック済み」へ変更する。ただし**表情モーフ名の除外は維持する**（`ScenePresetManager.cs:1970-1977` の `faceMorphNames`）。表情と同じ `TMorph` を共有しており、両方に保存すると適用順で競合するため
- **適用**: 現状どおり**未保存タグのゼロ化は行わない**（`timeline-remaining-work.md` §4 に記録済みの意図的な決定。`ApplyFace` の結果を踏まないため）。適用した分は `Mark` で積み増す

したがって「保存集合 = チェック集合」の等式は保存側でのみ成り立ち、適用は加算的になる。表情（v22 の `SetNames` による完全同期）とは非対称であることを、コードコメントとロードマップの両方へ明記する。

### D4. 着替えで消えたタグの扱い

方針 3 に従い**チェックは保持する**。`ShapeKeyEditWindow` は現物のタグ一覧（`morph.GetTags()`）を順に描くので、現物に無いチェックは単に表示されない。タイムライン側も既存の挙動どおり（`maidShapeKeysMap` に残るがキーは書けない）。

---

### Task 1: 同期の差分計算を純粋関数として切り出す

Unity ランタイム無しでテストできる形にしてから、それを使う。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ShapeKeySyncDiff.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ShapeKeySyncDiffTests.cs`（新規）

**Interfaces:**
- Consumes: なし（`ICollection<string>` / `IEnumerable<string>` だけ）
- Produces: `static void ShapeKeySyncDiff.Compute(IEnumerable<string> desired, ICollection<string> current, List<string> toAdd, List<string> toRemove)` — Task 2 が使う。`toAdd` / `toRemove` はクリアしてから詰める

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ShapeKeySyncDiffTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// チェック集合 → タイムライン opt-in の片方向同期の差分計算を固定する。
    /// 差分が過不足なく出ないと、0F 自動キーやキー削除が漏れる/暴発する
    /// </summary>
    public class ShapeKeySyncDiffTests
    {
        private readonly List<string> _toAdd = new List<string>();
        private readonly List<string> _toRemove = new List<string>();

        private void Compute(string[] desired, string[] current)
        {
            ShapeKeySyncDiff.Compute(desired, new HashSet<string>(current), _toAdd, _toRemove);
        }

        [Fact]
        public void 増えた分だけ追加に出る()
        {
            Compute(new[] { "a", "b" }, new[] { "a" });

            Assert.Equal(new[] { "b" }, _toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 減った分だけ削除に出る()
        {
            Compute(new[] { "a" }, new[] { "a", "b" });

            Assert.Empty(_toAdd);
            Assert.Equal(new[] { "b" }, _toRemove);
        }

        [Fact]
        public void 同じなら差分は空()
        {
            Compute(new[] { "a", "b" }, new[] { "b", "a" });

            Assert.Empty(_toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 入れ替わりは追加と削除の両方に出る()
        {
            Compute(new[] { "a" }, new[] { "b" });

            Assert.Equal(new[] { "a" }, _toAdd);
            Assert.Equal(new[] { "b" }, _toRemove);
        }

        [Fact]
        public void 呼び出しごとに結果リストがクリアされる()
        {
            Compute(new[] { "a" }, new string[0]);
            Compute(new string[0], new string[0]);

            Assert.Empty(_toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 空文字とnullは無視する()
        {
            ShapeKeySyncDiff.Compute(
                new[] { "a", "", null }, new HashSet<string>(), _toAdd, _toRemove);

            Assert.Equal(new[] { "a" }, _toAdd);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests --filter ShapeKeySyncDiffTests`
Expected: コンパイルエラー（`ShapeKeySyncDiff` が存在しない）

- [ ] **Step 3: 実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/ShapeKeySyncDiff.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック集合 (EditTargetStore) からタイムラインの opt-in 集合
    /// (TimelineData.maidShapeKeysMap) への片方向同期の差分計算。
    /// タイムライン側の Add/RemoveMaidShapeKey は 0F 自動キーやキー削除を伴う重い操作なので、
    /// 実際に変わった分だけ呼べるようここで差分を出す
    /// </summary>
    public static class ShapeKeySyncDiff
    {
        /// <summary>
        /// desired (チェック集合) と current (タイムラインの現状) を突き合わせる。
        /// toAdd / toRemove は呼び出しごとにクリアしてから詰める
        /// </summary>
        public static void Compute(
            IEnumerable<string> desired,
            ICollection<string> current,
            List<string> toAdd,
            List<string> toRemove)
        {
            toAdd.Clear();
            toRemove.Clear();

            var desiredSet = new HashSet<string>();
            if (desired != null)
            {
                foreach (var name in desired)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    desiredSet.Add(name);
                }
            }

            foreach (var name in desiredSet)
            {
                if (!current.Contains(name))
                {
                    toAdd.Add(name);
                }
            }

            foreach (var name in current)
            {
                if (!desiredSet.Contains(name))
                {
                    toRemove.Add(name);
                }
            }
        }
    }
}
```

- [ ] **Step 4: csproj へ登録する**

`MaidManipulation\ModelTrackedNameStore.cs` の近くへ:

```xml
    <Compile Include="MaidManipulation\ShapeKeySyncDiff.cs" />
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: 全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ShapeKeySyncDiff.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ShapeKeySyncDiffTests.cs
git commit -m "feat(tracking): シェイプキー同期の差分計算を切り出す"
```

---

### Task 2: メイドシェイプキーの変更追跡ストアと片方向同期を置く

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidShapeKeyEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs`（`RegisterManager(ModelShapeKeyEditManager.instance);` の直後へ登録）

**Interfaces:**
- Consumes: `EditTargetStore`（M0）、`ShapeKeySyncDiff`（Task 1）、`MTEP.TimelineManager.instance.timeline`、`MTEP.MaidManager.instance.GetMaidCache(int).maid`
- Produces:
  - `EditTargetStore MaidShapeKeyEditManager.instance.GetStore(Maid maid)`
  - `EditTargetStore MaidShapeKeyEditManager.instance.FindStore(Maid maid)`

**注意（レビューで判明した落とし穴）:**

1. SE の `ManagerBase`（`source/COM3D2.SceneEditor.Plugin/ManagerBase.cs`）には `timeline` プロパティが**無い**。`MTEP.TimelineManager.instance.timeline` と完全修飾で書くこと（`config` や `windowManager` はあるが、タイムライン系は別名前空間の `ManagerBase` の持ち物）
2. `OnLoad()` は override しない（D2 のとおり発火しない）。取り込みは同期処理の中で「タイムラインの参照が変わったか」で判断する
3. ストア集合の増減は世代カウンタで検知する。`version` 合計だけだと、メイド破棄による減少と他メイドの増分が偶然釣り合ったときに見逃す（`ModelTrackedNameStore` と同じ対策）

- [ ] **Step 1: マネージャを作る**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidShapeKeyEditManager.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドの任意シェイプキーの変更追跡。
    /// チェック集合はここが唯一のソースで、タイムラインの opt-in
    /// (TimelineData.maidShapeKeysMap) へは片方向に流すだけ。
    /// 逆方向はタイムラインが差し替わったときの初期取り込みに限る。
    /// タグ名はメイド単位でフラットに持つ (スロットを跨いだ同名タグは同一視される。
    /// TimelineData.maidShapeKeysMap と MaidCache.GetBlendShape が元からこの前提)
    /// </summary>
    public class MaidShapeKeyEditManager : ManagerBase
    {
        private static MaidShapeKeyEditManager _instance;
        public static MaidShapeKeyEditManager instance
            => _instance ?? (_instance = new MaidShapeKeyEditManager());

        private MaidShapeKeyEditManager()
        {
        }

        private readonly Dictionary<Maid, EditTargetStore> _stores
            = new Dictionary<Maid, EditTargetStore>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        // 同期の作り直し判定。ストア集合の世代と version 合計、タイムラインの同一性を見る
        private int _generation;
        private int _lastGeneration = -1;
        private int _lastVersionSum = -1;
        private MTEP.TimelineData _lastTimeline;

        // 差分計算の結果。使い回してゴミを出さない
        private readonly List<string> _toAdd = new List<string>();
        private readonly List<string> _toRemove = new List<string>();

        private static MTEP.TimelineData currentTimeline
            => MTEP.TimelineManager.instance.timeline;

        /// <summary>
        /// メイドのチェック集合。無ければ作る。
        /// 新規作成時はタイムラインの既存 opt-in を取り込む
        /// (取り込まないと、直後の片方向同期が既存タグを全部消してしまう)
        /// </summary>
        public EditTargetStore GetStore(Maid maid)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new EditTargetStore();
                _stores[maid] = store;
                _generation++;

                var timelineData = currentTimeline;
                var slotNo = GetMaidSlotNo(maid);
                if (timelineData != null && slotNo >= 0)
                {
                    store.SetNames(timelineData.GetMaidShapeKeys(slotNo));
                }
            }
            return store;
        }

        /// <summary>既存のチェック集合を引くだけで新規生成はしない (表示用)</summary>
        public EditTargetStore FindStore(Maid maid)
        {
            EditTargetStore store;
            return maid != null && _stores.TryGetValue(maid, out store) ? store : null;
        }

        public override void Update()
        {
            CleanupStores();
            SyncToTimeline();
        }

        /// <summary>破棄済みメイドの記録を掃除する (FaceEditManager と同じ方式)</summary>
        private void CleanupStores()
        {
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
                _generation++;
            }
        }

        /// <summary>
        /// チェック集合をタイムラインの opt-in へ流す。
        /// Add/RemoveMaidShapeKey は 0F 自動キーと全フレームキー削除を伴うため、
        /// 実際に変わったタグだけ呼ぶ
        /// </summary>
        private void SyncToTimeline()
        {
            var timelineData = currentTimeline;
            if (timelineData == null)
            {
                // タイムライン未ロード。次にロードされたときへ判定を持ち越す
                _lastTimeline = null;
                _lastGeneration = -1;
                _lastVersionSum = -1;
                return;
            }

            // タイムラインが差し替わったら、まずそちらの opt-in を取り込む (D2)。
            // 判定状態を更新する前に行うことで、取り込みで動いた version も次の判定で拾える
            if (timelineData != _lastTimeline)
            {
                ImportFromTimeline(timelineData);
            }

            var versionSum = 0;
            foreach (var pair in _stores)
            {
                versionSum += pair.Value.version;
            }

            if (timelineData == _lastTimeline
                && _generation == _lastGeneration
                && versionSum == _lastVersionSum)
            {
                return;
            }
            _lastTimeline = timelineData;
            _lastGeneration = _generation;
            _lastVersionSum = versionSum;

            foreach (var pair in _stores)
            {
                var slotNo = GetMaidSlotNo(pair.Key);
                if (slotNo < 0)
                {
                    // タイムラインの管理外のメイド。次のフレームで解決するかもしれないので記録は残す
                    continue;
                }

                ShapeKeySyncDiff.Compute(
                    pair.Value.GetNames(),
                    timelineData.GetMaidShapeKeys(slotNo),
                    _toAdd, _toRemove);

                foreach (var shapeKey in _toAdd)
                {
                    timelineData.AddMaidShapeKey(slotNo, shapeKey);
                }
                foreach (var shapeKey in _toRemove)
                {
                    timelineData.RemoveMaidShapeKey(slotNo, shapeKey);
                }
            }
        }

        /// <summary>
        /// タイムラインが持つ opt-in をストアへ取り込む (D2)。
        /// タグを持つスロットだけを反映し、空のスロットはストアを保持する。
        /// こうしないとタイムラインの新規作成でチェックが全部消える
        /// (新規タイムラインのマップは空)
        /// </summary>
        private void ImportFromTimeline(MTEP.TimelineData timelineData)
        {
            var maidManager = MTEP.MaidManager.instance;

            foreach (var pair in timelineData.maidShapeKeysMap)
            {
                if (pair.Value == null || pair.Value.Count == 0)
                {
                    continue;
                }

                var maidCache = maidManager.GetMaidCache(pair.Key);
                var maid = maidCache != null ? maidCache.maid : null;
                if (maid == null)
                {
                    continue;
                }

                // GetStore の初回生成も同じ内容を取り込むため、ここは上書きになっても等価
                GetStore(maid).SetNames(pair.Value);
            }
        }

        /// <summary>タイムライン側のメイドスロット番号。管理外なら -1</summary>
        private static int GetMaidSlotNo(Maid maid)
        {
            if (maid == null)
            {
                return -1;
            }
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(maid);
            return maidCache != null ? maidCache.slotNo : -1;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる (FaceEditManager と同じ方式)
            _stores.Clear();
            _generation++;
            _lastTimeline = null;
            _lastGeneration = -1;
            _lastVersionSum = -1;
        }
    }
}
```

- [ ] **Step 2: csproj とマネージャ登録**

csproj の `MaidManipulation\ShapeKeySyncDiff.cs` の近くへ:

```xml
    <Compile Include="MaidManipulation\MaidShapeKeyEditManager.cs" />
```

`COM3D2.SceneEditor.Plugin.cs` の `RegisterManager(ModelShapeKeyEditManager.instance);` の直後へ:

```csharp
                managerRegistry.RegisterManager(MaidShapeKeyEditManager.instance);
```

- [ ] **Step 3: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS（この時点ではストアを触る UI が無いので挙動は変わらない）

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidShapeKeyEditManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs
git commit -m "feat(tracking): メイドシェイプキーの変更追跡ストアと片方向同期を追加する"
```

---

### Task 3: ShapeKeyEditWindow のメイドタブへチェックを出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs`
  - クラス冒頭のコメント
  - `DrawMaidShapeKeys()`（L146-206 付近）

**Interfaces:**
- Consumes: `MaidShapeKeyEditManager.instance.GetStore/FindStore`（Task 2）、`GUIView.DrawTrackedLabel`（M2）
- Produces: なし

- [ ] **Step 1: 描画ループを差し替える**

`DrawMaidShapeKeys()` の `view.BeginScrollView();` ブロックを次に置き換える:

```csharp
            // 表示判定用。まだ 1 つもチェックしていないメイドのストアを作らないよう FindStore を使う
            // (操作側のコールバックは GetStore で遅延生成する)
            var shapeKeyStore = MaidShapeKeyEditManager.instance.FindStore(target);

            view.BeginScrollView();
            {
                foreach (var tag in _tags)
                {
                    var blendShape = maidCache.GetBlendShape(tag);
                    if (blendShape == null || blendShape.entities.Count == 0)
                    {
                        continue;
                    }

                    var weight = blendShape.weight;
                    var tagName = tag;
                    var isModified = shapeKeyStore != null && shapeKeyStore.IsModified(tagName);

                    // 変更追跡チェック。ON=プリセット保存とタイムラインのキーフレーム対象。
                    // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
                    Action<bool> onCheckChanged = newChecked =>
                    {
                        if (newChecked)
                        {
                            MaidShapeKeyEditManager.instance.GetStore(target).Mark(tagName);
                        }
                        else
                        {
                            blendShape.weight = 0f;
                            maidCache.FixBlendValues(new string[] { tagName });
                            MaidShapeKeyEditManager.instance.GetStore(target).Unmark(tagName);
                        }
                    };

                    view.DrawTrackedLabel(isModified, onCheckChanged, tagName, -1, ROW_HEIGHT);

                    var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = 0f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = weight,
                        onChanged = x => weight = x,
                    });

                    if (updateTransform)
                    {
                        blendShape.weight = weight;
                        maidCache.FixBlendValues(new string[] { tagName });
                        // 編集したシェイプキーは自動で追跡対象にする
                        MaidShapeKeyEditManager.instance.GetStore(target).Mark(tagName);
                    }
                }
            }
            view.EndScrollView();
```

**注意:** ループ変数 `tag` をそのままクロージャで captured すると C# 5 以前の foreach 変数キャプチャ問題が出る環境があるため、`tagName` へ写してから使う（`blendShape` は毎回 `GetBlendShape` の戻り値で別インスタンス扱いになるためそのままでよいが、明示的に読みやすくするため上記のとおり書く）。

- [ ] **Step 2: クラスコメントを実態へ合わせる**

M2 で書いた `<summary>` の「メイドのシェイプキーについては…ここでは扱わない」を次に置き換える:

```csharp
    /// 行頭のチェックが変更追跡 (プリセット保存とタイムラインのキーフレーム対象) を兼ねる。
    /// チェック集合がソース・オブ・トゥルースで、タイムラインの opt-in
    /// (TimelineData.maidShapeKeysMap) へは MaidShapeKeyEditManager が片方向に流す。
    /// チェック集合は HistoryManager 未対応のため undo で戻らない
```

- [ ] **Step 3: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs
git commit -m "feat(ui): メイドシェイプキーへ変更追跡チェックを表示する"
```

---

### Task 4: レイヤー編集ウィンドウのタグトグルをストア向きへ変える

二重管理を作らないため、タイムライン API を直接叩く最後の経路を潰す。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs`（L219-233 付近）

**Interfaces:**
- Consumes: `MaidShapeKeyEditManager.instance.GetStore/FindStore`（Task 2）
- Produces: なし

- [ ] **Step 1: トグルの向き先を変える**

ファイル冒頭の `using` へ `using COM3D2.SceneEditor.Plugin;` を追加し、タグのトグルループを次に置き換える:

```csharp
            // タグ登録のソース・オブ・トゥルースは変更追跡ストア。
            // ここからタイムラインの Add/RemoveMaidShapeKey を直接叩くと二重管理になるため、
            // ストアだけを変え、タイムラインへは MaidShapeKeyEditManager が片方向に流す
            var shapeKeyStore = MaidShapeKeyEditManager.instance.FindStore(maid);

            foreach (string tag in tags)
            {
                var enable = shapeKeyStore != null && shapeKeyStore.IsModified(tag);
                var tagName = tag;

                view.DrawToggle(tagName, enable, -1, 20, newValue =>
                {
                    var store = MaidShapeKeyEditManager.instance.GetStore(maid);
                    if (newValue)
                    {
                        store.Mark(tagName);
                    }
                    else
                    {
                        store.Unmark(tagName);
                    }
                });
            }
```

**注意:** ここでは値のゼロ化を行わない（`ShapeKeyEditWindow` 側と挙動が違う）。このウィンドウは「キーフレーム対象の選別」の UI であって値の編集 UI ではなく、既存の挙動も値に触っていなかったため。この差はコメントで明記する。

- [ ] **Step 2: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ShapeKeyTimelineLayer.cs
git commit -m "refactor(timeline): シェイプキーのタグ登録を変更追跡ストア経由へ寄せる"
```

---

### Task 5: シーンプリセットの保存対象をチェック済みへ切り替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
  - `state.shapeKeys` を作る箇所（L1982-2005 付近）
  - `ApplyShapeKeys()`（L2028-2053 付近）
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（バージョン履歴と `CurrentVersion`、`shapeKeys` フィールドのコメント L550）

**Interfaces:**
- Consumes: `MaidShapeKeyEditManager.instance.GetStore/FindStore`（Task 2）
- Produces: なし

- [ ] **Step 1: 保存側をチェック済みフィルタへ変える**

タグ走査ループの値フィルタを差し替える:

```csharp
            // チェック集合が保存対象。表情モーフは同じ TMorph を共有していて
            // 適用順で競合するため、これまでどおり除外する
            var shapeKeyStore = MaidShapeKeyEditManager.instance.FindStore(maid);
            if (shapeKeyStore == null)
            {
                return;
            }

            ... (slot 走査は変更なし)

                foreach (var tag in slot.morph.GetTags())
                {
                    if (faceMorphNames.Contains(tag) || !seenTags.Add(tag))
                    {
                        continue;
                    }
                    // 保存対象はチェック済みのみ。値 0 でもユーザーが意図して選んだものは残す
                    if (!shapeKeyStore.IsModified(tag))
                    {
                        continue;
                    }
                    state.shapeKeys.Add(new ScenePresetMorph
                    {
                        name = tag,
                        value = maidCache.GetBlendShapeValue(tag),
                    });
                }
```

**注意:** `shapeKeyStore == null` での早期 return は `state.shapeKeys` を作った**後**に置くこと（null のまま返すと XML に要素ごと出なくなり、既存の「保存したが空」との区別が壊れる）。実装時は `state.shapeKeys = new List<ScenePresetMorph>();` の直後へ置く。スロットマテリアルの取得はこの後ろにあるため、`return` ではなくフラグで走査だけ飛ばすこと。

正しくは次の形にする:

```csharp
            state.shapeKeys = new List<ScenePresetMorph>();
            var shapeKeyStore = MaidShapeKeyEditManager.instance.FindStore(maid);
            var seenTags = new HashSet<string>();
            // チェックが 1 つも無ければ走査自体を省く (スロットマテリアルの取得は続ける)
            if (shapeKeyStore != null && !shapeKeyStore.isEmpty)
            {
                ... (既存のスロット走査。値フィルタをチェック済みフィルタへ差し替え)
            }
```

- [ ] **Step 2: 適用側でチェック集合を復元する**

`ApplyShapeKeys` の `maidCache.FixBlendValues(appliedTags);` の直前へ:

```csharp
            // 保存されていた = ユーザーがチェックしていた。旧バージョンのプリセットでも
            // 「記載分 = チェック済み」で辻褄が合う。
            // 表情の v22 と違い SetNames ではなく Mark の積み増しにするのは、
            // 未保存タグのゼロ化を行わない適用側の仕様と揃えるため
            // (ゼロ化しないのにチェックだけ消すと、値が残ったまま追跡から外れる)
            var shapeKeyStore = MaidShapeKeyEditManager.instance.GetStore(maid);
            foreach (var tag in appliedTags)
            {
                shapeKeyStore.Mark(tag);
            }
```

`ApplyShapeKeys` の `<summary>` にも「チェック集合は積み増しで復元する」旨を 1 行足す。

- [ ] **Step 3: スキーマバージョンを上げる**

`ScenePresetData.cs` の v23 行の直後へ足し、`CurrentVersion` を 24 にする:

```csharp
        // v24: メイドの任意シェイプキーの保存対象を「値が非 0」から「チェック済み (EditTargetStore)」へ変更。
        //      構造変更なし。旧データは記載タグ (=非 0 保存分) を適用時にチェック済みへ復元する
        public static readonly int CurrentVersion = 24;
```

`shapeKeys` フィールドのコメント（L550 付近「任意シェイプキー (v21)。公式表情モーフを除く非ゼロ値のみ持つ。」）を次に置き換える:

```csharp
        /// 任意シェイプキー (v21)。公式表情モーフを除く。v24 以降はチェック済みのみ。
```

- [ ] **Step 4: ビルドとテスト**

Run: `MSBuild ... /p:GameVersion=COM3D25` → `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功・全 PASS

- [ ] **Step 5: COM3D2 構成でもビルドする**

Run: `MSBuild ... /p:GameVersion=COM3D2` → その後 COM3D25 構成をもう一度ビルド
Expected: 成功

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs
git commit -m "feat(preset): メイドシェイプキーの保存対象をチェック済みへ絞る"
```

---

### Task 6: 実機確認

**Interfaces:**
- Consumes: Task 1〜5 の成果
- Produces: なし

- [ ] **Step 1: ユーザーへ実機確認を依頼する**

**シェイプキー編集ウィンドウ（メイドタブ）**
1. タグ名の行頭にチェックが出て、初期状態は全て未チェックであること（タイムライン未ロード時）
2. スライダーを動かすと自動でチェックが入ること
3. チェックを外すと値が 0 に戻ること
4. チェックを手動で入れると、値を変えずに追跡対象になること
5. モデルタブの表示・操作が M2 のときから変わっていないこと

**タイムライン（メイドシェイプレイヤー）との同期**
6. ウィンドウでチェックすると、メイドシェイプレイヤーのボーンメニューにそのタグが現れ、0F にキーが自動登録されること
7. チェックを外すと、そのタグのキーが全フレームから消えメニューからも消えること
8. レイヤー編集ウィンドウのタグトグル（従来の登録 UI）とシェイプキー編集ウィンドウのチェックが**常に一致**すること（どちらで操作しても他方へ反映される）
9. レイヤー編集ウィンドウでトグルを外しても値はゼロ化されないこと（シェイプキー編集ウィンドウのチェック解除とは意図的に挙動が違う）

**タイムラインのロード（設計 D2 の核心）**
10. タグを持つ既存タイムラインを読み込むと、**そのメイドを一度も操作していなくても**タグがチェック済みで表示されること
11. **同一メイドで別のタイムラインを続けて読み込む**と、後から読んだタイムラインのタグが正になり、前のタイムラインのタグでキーが消えたりしないこと
11. **チェックを付けた状態でタイムラインを新規作成**しても、チェックが消えないこと（新規タイムラインへ流し込まれる）
12. タイムラインを一度も開かずにチェック → その後タイムラインを作成 → タグが登録され 0F キーが付くこと

**着替え**
13. チェックしたスロットの衣装を着替えると、現物に無いタグは表示されなくなるが、元の衣装へ戻すとチェックが残っていること

**シーンプリセット**
14. タグをチェックして値を付けた状態でプリセット保存 → 読み込みで値が復元され、チェックも入った状態になること
15. **値 0 のままチェックしたタグ**もプリセットに保存され、読み込み後にチェックが復元されること
16. v23 以前の旧プリセット（非 0 のタグを持つもの）を読み込むと、記載分がチェック済みで復元されること
17. 表情モーフがシェイプキー側に二重保存されていないこと

**既知の制限（確認不要・仕様）**
- チェックの ON/OFF は undo で戻らない（HistoryManager 未対応。スコープ外）
- プリセット適用は未保存タグをゼロ化しない（既存の意図的な仕様）
- ShapeKey / Eyes レイヤーと MaidFaceWindow の相互排他は未解決（スコープ外）

- [ ] **Step 2: 指摘があれば修正し、再ビルド・再テストしてコミットする**

---

### Task 7: ドキュメント更新

**Files:**
- Modify: `docs/superpowers/specs/modified-tracking-rollout-roadmap.md`（Phase M3 節）
- Modify: `docs/superpowers/specs/timeline-remaining-work.md`（§3 の過去決定と §4 の表）

- [ ] **Step 1: ロードマップの Phase M3 を完了として更新する**

`### Phase M3: メイドシェイプキー(最大ペイオフ・最難関)` を `### Phase M3: メイドシェイプキー ✅ 完了 (YYYY-MM-DD)` に変え、「実装の要点」を追記する:

- タイムライン側は既に完全な opt-in 機構を持っていた（`allBoneNames` = `GetMaidShapeKeys`、`OnShapeKeyAdded` が 0F キー、`OnShapeKeyRemoved` がキー削除）ため、**M0 の追跡部品は接続していない**。やったのは主導権の移動だけ
- ソース・オブ・トゥルースは per-Maid `EditTargetStore`。タイムラインへは `MaidShapeKeyEditManager` が毎フレーム差分同期する。逆方向はロード時の初期反映のみ
- タイムラインのロード時、そのメイドのタグが**空でなければ**マップを正とし、**空なら**ストアを保持する（新規作成でチェックが消えるのを防ぐ）
- プリセットは v24。保存はチェック済みのみ（表情モーフは従来どおり除外）、適用は積み増しで未保存タグのゼロ化はしない（非対称であることを明記）
- チェック集合は履歴に含めない（M2 と同じ縮退判断）

- [ ] **Step 2: timeline-remaining-work.md の過去決定を更新する**

§3 の「キーフレーム対象タグの登録はレイヤー編集ウィンドウの責務」という記述を次に改める:

```
キーフレーム対象タグの登録は変更追跡チェック (ShapeKeyEditWindow のメイドタブ) が持つ。
レイヤー編集ウィンドウのタグトグルも同じストアを操作する (2026-08-27, Phase M3 で変更)
```

§4 の表の「シーンプリセットのシェイプキー適用は保存タグのみ設定（未保存タグはゼロ化しない…）」の行はそのまま残し、出典へ「Phase M3 でも維持」と追記する。

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/modified-tracking-rollout-roadmap.md docs/superpowers/specs/timeline-remaining-work.md
git commit -m "docs: 変更追跡ロードマップの M3 完了を記録する"
```

---

## Self-Review

**Spec coverage（ロードマップ Phase M3 の 6 項目）:**

| ロードマップの項目 | 対応タスク |
|---|---|
| 設計確定（片方向同期・ロード時の初期反映・過去決定の更新） | 設計確定 D1〜D4 + Task 7 Step 2 |
| ShapeKeyEditWindow メイドタブへチェック行、着替えは方針 3 | Task 3 + D4 |
| `ShapeKeyTimelineLayer` は既存イベント再構築を活用（ポーリング不要） | **追跡部品は接続しない**という判断そのもの（Architecture 節） |
| シーンプリセット `shapeKeys` をチェック済みフィルタへ、適用側の非対称を仕様決定 | Task 5 + D3 |
| 前提タスク: HistoryManager 対応 | **縮退**（Global Constraints。M2 と同じ判断） |
| ShapeKey/Eyes と MaidFaceWindow の相互排他はスコープ外 | Global Constraints に明記 |

**方針との整合:**
- 方針 1（行独立な領域は書き込みも絞る）: シェイプキーは行独立。書き込み絞り込みは既存の `allBoneNames` 機構がそのまま担う
- 方針 2（既存 opt-in がある領域は片方向同期）: D1 のとおり。逆方向はロード時のみ（D2）。タイムライン API を直接叩く経路は Task 4 で潰す
- 方針 3（現物に無い名前は捨てない）: D4
- 方針 4（ポーリングよりイベント）: `Add/RemoveMaidShapeKey` が既存のイベント再構築を発火する。ストア側は version 比較のみで、キーフレーム走査は伴わない
- 方針 5（Undo 前提の確認）: 未対応と判断し明記して縮退

**残リスク（計画外メモ）:**
- `MaidShapeKeyEditManager.Update` の同期は「ストアの version 合計 + タイムラインの参照」で判定する。タイムライン側のタグが**外部から**書き換わった場合（未知の経路）に追従できないが、Task 4 で直接経路を潰すため現時点では存在しないはず
- D2 の「マップが空ならストアを保持」は、タグを持たない既存タイムラインを読むと前のチェックが流れ込む。実害は不要な 0F キーで、チェックを外せば消える
- 表情モーフとシェイプキーが同じ `TMorph` を共有する構造そのものは変えていない。相互排他（`timeline-remaining-work.md` §4）は引き続き未解決

## レビュー却下メモ

- 「`mte.OnLoad()` は `managerRegistry.OnLoad()` と別物で D2 が機能しない」— **取り込み済み（設計を作り直し）**。`OnLoad` の override をやめ、同期処理内で「タイムラインの参照が変わったか」を見る方式へ変更（D2 / Task 2）。両レジストリへの二重登録は配線と順序依存が増えるため採らなかった
- 「コード例が未定義の `timeline` を参照している」— **取り込み済み**。SE の `ManagerBase` に `timeline` は無いので `MTEP.TimelineManager.instance.timeline` を使う（Task 2 の `currentTimeline`）
- 「`OnLoad` が既存ストアしか見ないため、未操作メイドのタグが復元されない」— **取り込み済み**。取り込みをマップ側の走査（`ImportFromTimeline`）に変え、ストアが無いメイドも作るようにした。実機確認 10 を「一度も操作していなくても」と明示し、11 に別タイムラインの続け読みを追加
- 「version 合計だけの変更検知はストア集合の増減で見逃しうる」— **取り込み済み**。`_generation` カウンタを追加（`ModelTrackedNameStore` と同型）
- 「スロットを跨いだ同名タグが同一視される点をコメントに残すべき」— **取り込み済み**。`MaidShapeKeyEditManager` のクラスコメントへ明記
