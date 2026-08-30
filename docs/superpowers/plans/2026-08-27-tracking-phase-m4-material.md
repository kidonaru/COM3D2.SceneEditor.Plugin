# 変更追跡 Phase M4: マテリアル系 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイドスロットマテリアルと配置モデルマテリアルについて、「どのマテリアルを編集対象として扱うか」を `EditTargetStore` で追跡し、`MaterialEditWindow` からチェックできるようにする。`MaidMaterialTimelineLayer` / `ModelMaterialTimelineLayer` のボーンメニュー表示とキー書き込みをチェック済みマテリアルへ絞る。

**Architecture:** M0 で抽出した `TimelineLayerBase` の追跡部品（`trackedStore` / `trackedCandidateNames` / `trackedHistoryPrefix`）へ接続するだけで、メニュー絞り込み・0F 自動キー・解除時キー削除が付いてくる。ストアはメイド用（`Maid` キー、FaceEditManager と同型）とモデル用（`GameObject` キー + `ModelTrackedNameStore` 集約、M2 の `ModelShapeKeyEditManager` と同型）の 2 本立て。マテリアル系に固有の追加作業は 3 つ:

1. **追跡部品の解除判定の修正（Task 0、前提）** — メイドスロットは「同じ対象のまま候補一覧だけが伸縮する」M0 以来はじめてのケース（着替え）で、現状の判定は候補から消えたチェック済み項目を「チェック解除」と誤認してキーを全消去してしまう
2. **書き込み絞り込みを手で付ける** — `UpdateFrame` が `materialMap.Values` を回していて `allBoneNames` を見ていないため、他フェーズのように自動では付かない
3. **既存の `AddFirstBones(全マテリアル)` を絞り込み後の集合へ差し替える**

**Tech Stack:** C#（プラグイン本体は旧形式 csproj / `<Compile Include>` 明示列挙）、xUnit（net48、COM3D25 構成のプラグイン DLL 参照）、MSBuild 2 構成（COM3D2 / COM3D25）

**Spec:** `docs/superpowers/specs/modified-tracking-rollout-roadmap.md` の「Phase M4」節

## Global Constraints

- コメント・ログメッセージは日本語で書く
- **`debug.bat` / `deploy.bat` / `release.bat` は実行しない**。ビルドは MSBuild 直叩き:
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25`
  - `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2`
- テストは COM3D25 構成の DLL を参照するため、**COM3D25 構成ビルドを最後に流してから `dotnet test source\COM3D2.SceneEditor.Plugin.Tests`** を実行する
- 新規 .cs は `source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ追加する
- xUnit は Unity ランタイム無しで動くため、`Maid` / `GameObject` / `Material` を触るコードはテストできない。本 Phase で追加する部品は全て Unity 依存のため**新規ユニットテストは無い**（既存の `EditTargetStoreTests` / `ModelTrackedNameStoreTests` / `TrackedDirtyGateTests` が土台を担保する）。検証はビルド + 実機確認で行う
- **HistoryManager 対応はスコープ外**（M2 / M3 と同じ縮退判断。`MaterialEditWindow` は SE の履歴に未対応）。チェックの ON/OFF は undo で戻らない
- 実機確認はユーザーに依頼する（このセッションでゲームを操作しない）

---

## 設計確定

### D1. スコープの縮退（ロードマップ記載から 2 点減らす）

ロードマップの Phase M4 は 3 レイヤー + プリセットフィルタを挙げているが、コードを読んだ結果 2 点を**本 Phase の対象外**とする。

#### D1-a. 背景モデルマテリアル（`BGModelMaterialTimelineLayer`）は対象外

編集ウィンドウとタイムラインレイヤーが**別の対象集合を見ている**ため、チェックを橋渡しできない。

| | 対象 |
|---|---|
| `MaterialEditWindow.DrawBGModelMaterial`（`MaterialEditWindow.cs:216-254`） | `GameMain.Instance.BgMgr.BgObject` 配下の `Renderer` を `ProviderModelStat` で包んだもの（= 現在の背景シーンそのもの） |
| `BGModelMaterialTimelineLayer.allBoneNames` | `bgModelManager.materialNames`（= `BGModelManager.AddModel` でタイムラインが配置した背景モデル） |

編集ウィンドウ側のコメント（`MaterialEditWindow.cs:212-214`）が明言しているとおり、`BGModelManager` はタイムラインデータと双方向同期するため編集ウィンドウは意図的に使っていない。背景に追跡チェックを載せるには「編集ウィンドウへ `BGModelManager` の配置モデルタブを足す」という別の機能追加が要る。**M4 完了後の別 Phase（または需要が出たとき）へ回す。**

#### D1-b. シーンプリセットのマテリアル保存フィルタは変更しない

`ScenePresetManager.CaptureMaterial`（`Manager/ScenePresetManager.cs:2116-2162`）は既に**初期値と異なるプロパティだけ**を控え、差分が無ければ `null` を返して保存対象から落としている。ここへチェック集合フィルタを重ねると:

- **退行になる**: シーン遷移や再起動でストアが空になった後に保存すると、値としては編集済みのマテリアルが保存から落ちる。現状の初期値差分フィルタにはこの弱点が無い
- **得るものが無い**: 「チェック済みだが初期値のまま」のマテリアルを足しても `data.isEmpty` で捨てられるため、保存内容は 1 件も増えない

シェイプキーで「値 0 でもチェック済みなら残す」としたのは、シェイプキーには初期値差分の概念を持つ土台が無く、チェックが唯一の意図表明だったため。マテリアルには `GetInitialColor` / `GetInitialValue` があるので事情が違う。**プリセット側は現状維持**とし、ロードマップへ理由を書き戻す（Task 7）。

### D2. ストアのキーと記録する名前

| 対象 | ストアのキー | 記録する生名 | タイムライン候補名との関係 |
|---|---|---|---|
| メイドスロット | `Maid` | `ModelMaterial.name`（= `"{スロット名}/{Unity マテリアル名}"`） | **そのまま一致**。橋渡し不要 |
| 配置モデル | ルート `GameObject` | `ModelMaterial.displayName`（= Unity マテリアル名） | `"{StudioModelStat.name}/{生名}"` へ集約時に修飾 |

メイド側が一致するのは、`MaterialEditWindow.DrawMaidMaterial` が使う `slot.materials`（`MTEP.MaidSlotStat`）と `MaidCache.UpdateMaterials`（`Timeline/MaidCache.cs:871-883`）が同じ `ModelMaterial` インスタンスを回しており、`ModelMaterial.name`（`Timeline/ModelMaterial.cs:62-65`）が `model.name + "/" + material.name` = スロット名修飾になっているため。**メイドは `EditTargetStore` をそのまま `trackedStore` へ渡せる。**

モデル側は編集ウィンドウが `ProviderModelStat`（`name = obj.name`）、タイムラインが `StudioModelStat`（`name` は group 込みの修飾名で `ModelHackManager.FixGroup` により変わりうる）と別物なので、M2 と同じ「生名で記録 → `ModelTrackedNameStore` で毎回修飾名を組み直す」方式を採る。

### D3. モデル名の解決は `StudioModelManager` から引く（`controller.model` を使わない）

M2 の `ModelShapeKeyEditManager.GetModelName` は `BlendShapeController.model.name` を使ったが、マテリアルで同じことをしてはいけない。

`ModelMaterialController.GetOrCreate`（`Timeline/ModelMaterialController.cs:60-72`）は呼ばれるたび `controller.model` を上書きする。`ModelMaterialController` は GameObject 単位で冪等なので、`MaterialEditWindow` がモデルタブを開いて `ProviderModelStat.materials` に触れた瞬間、同じ GameObject を指す `StudioModelStat` が入れた `controller.model` が `ProviderModelStat` へ差し替わる（= **最終書き込み勝ち**）。したがって `controller.model.name` は編集ウィンドウの操作で化ける。

そこで解決は `StudioModelManager.instance.models` を走査し、`model.transform.gameObject` が一致するものの `name` を採る。見つからなければ `null` を返して `ModelTrackedNameStore` の再試行に任せる（タイムライン未ロード時の扱いは M2 と同じ）。

**なお `controller.model` 最終書き込み勝ちは既存の別課題**（`StudioModelManager.materialMap` のキーが編集ウィンドウ操作でずれうる）。本 Phase は記録側が `controller.model` に依存しない設計なので影響を受けないが、既知課題として `timeline-remaining-work.md` へ書き残す（Task 7）。

### D4. 書き込み絞り込み（方針 1 の適用）

マテリアルは行ごとに独立なので**キー書き込みも絞る**。ただしマテリアルレイヤーの `UpdateFrame` は `allBoneNames` ではなく `materialMap.Values` を直接回している（`MaidMaterialTimelineLayer.cs:131-142` / `ModelMaterialTimelineLayer.cs:152-161`）ため、`allBoneNames` を絞っただけでは書き込みが絞られない。`allBoneNames` を走査して `GetMaterial(name)` で引く形へ変える。

### D5. 既存の `AddFirstBones(全マテリアル)` を絞り込み後の集合へ差し替える

各レイヤーは初期化やモデル追加のたび全マテリアルへ 0F キーを打っている（`MaidMaterialTimelineLayer.cs:127` / `ModelMaterialTimelineLayer.cs:32,116`）。ここを絞らないと**全マテリアルにキーが入り、`BuildTrackedBoneNames` の「既存キーフレーム記載」経由で全部がメニューへ復活する**（絞り込みが実質無効になる）。呼び出しを `AddFirstBones(allBoneNames)`（= `trackedBoneNames`）へ変える。

`RemoveAllBones`（モデル削除時、`ModelMaterialTimelineLayer.cs:125`）は削除されたモデルのキーを消すだけなので、絞り込みとは無関係にそのまま残す。

### D6. 着替えでチェック済みマテリアルのキーフレームを消さない（M0 部品の修正）

**メイドスロットは「同じ対象（同じストア）のまま候補一覧だけが伸縮する」M0 以来はじめてのケース**である。現状の `UpdateTrackedBoneFilter`（`Timeline/TimelineLayer/TimelineLayerBaseTracking.cs:106-151`）はこれを扱えない。

- `BuildTrackedCheckedNames`（同 75-91 行）は `trackedCandidateNames` を回してチェック済みか判定する。つまり**候補一覧に無いチェック済み名は `checkedNames` に入らない**
- 着替えでスロットが消えると `maidCache.materialNames` からそのマテリアルが落ちる → 次のティックで `checkedNames` から消える
- `storeChanged == false`（ストアは何も変わっていない）でも、`InvalidateTrackedBoneNames()` が間引きを解除するため判定は即座に走り、`RemoveTrackedKeys(_lastTrackedCheckedNames - checkedNames)`（133 行）が発火して**そのマテリアルのキーが全フレームから消える**

D2 で「着替えで消えたマテリアルの記録は捨てない」としてもチェックマークが残るだけで、ユーザーが作ったマテリアルアニメーションは失われる。元の衣装へ戻しても `AddTrackedFirstFrameKeys` が現在値で 0F キーを打ち直すだけで、フレーム間の変化は戻らない。

M1 / M2（モデルボーン・モデルシェイプキー）で顕在化しないのは、候補一覧が縮むのがモデル削除時だけで、そのときは全消去が正しい挙動だから。

**対処: 解除の判定をストアの直参照へ変える。** キーを消してよいのは「ユーザーが実際にチェックを外したとき」だけであり、それは `store.IsModified(name) == false` で判定できる。候補一覧に居るかどうかは関係ない。

M1 / M2 への影響は無い。モデル削除時は集約ストア（`ModelTrackedNameStore`）側からもその修飾名が消える（`CleanupStores` → `Invalidate` → 再集約）ため、`store.IsModified` も false になり従来どおりキーが消える。加えて各レイヤーの `OnModelRemoved` が `RemoveAllBones` で消す経路も残る。

### D7. タイムライン管理外のモデルではチェックが効かない（既知の制約・M2 と同じ）

`MaterialEditWindow.DrawModelMaterial` は `ModelProviderHost.GetModels()` で ModItemExplorer 等の外部プラグイン提供モデルも列挙する。これらの `GameObject` は `StudioModelManager.instance.models` に載らないため D3 の `GetModelName` が常に `null` を返し、チェックを ON にしても集約ストアへ反映されない（= タイムラインには出ない）。

タイムラインが管理していないモデルにキーフレームを打てない以上これは正しい挙動だが、**UI 上はチェックが押せて ON 表示される**ためユーザーには効いていないことが伝わらない。M2（モデルシェイプキー）が既に同じ制約を持っているため本 Phase 固有の新規リスクではない。**本 Phase では UI 的な手当てをせず、既知の制約として文書化するに留める**（Task 7）。

### D8. チェック操作の意味づけ（UI）

`MaterialEditWindow` はコンボボックスで 1 マテリアルずつ表示するため、チェック行はマテリアル選択コンボの直下・プロパティ一覧の直前に 1 行だけ置く。

| 操作 | 挙動 |
|---|---|
| プロパティ（色 / 数値）を変更 | 自動で `Mark` |
| チェック ON | `Mark` のみ（値は触らない） |
| チェック OFF | `material.Reset()` してから `Unmark`（M2 の「重みを 0 に戻す」と対称） |
| 「初期化」ボタン | `material.Reset()` + `Unmark`（値を初期へ戻すのだから追跡からも外す） |

背景タブは D1-a によりチェック行を出さない。

---

## File Structure

| ファイル | 責務 |
|---|---|
| `Timeline/TimelineLayer/TimelineLayerBaseTracking.cs`（変更） | 追跡部品。チェック解除の判定を候補一覧経由からストア直参照へ直す（D6） |
| `MaidManipulation/MaidMaterialEditManager.cs`（新規） | メイド別マテリアル追跡ストアの保持。`FaceEditManager` と同型（死亡メイド掃除 + シーン遷移クリア） |
| `MaidManipulation/ModelMaterialEditManager.cs`（新規） | モデル別マテリアル追跡ストア + `ModelTrackedNameStore<GameObject>` によるモデル修飾名集約。`ModelShapeKeyEditManager` と同型 |
| `MaterialEditWindow.cs`（変更） | チェック行の描画と自動チェック。3 タブ共用の `DrawMaterialSelector` へ追跡対象の引き当てを渡す |
| `Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs`（変更） | 追跡部品への opt-in、`UpdateFrame` / `AddFirstBones` の絞り込み |
| `Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs`（変更） | 同上（モデル側） |
| `COM3D2.SceneEditor.Plugin.cs`（変更） | 新規マネージャ 2 件の登録 |
| `COM3D2.SceneEditor.Plugin.csproj`（変更） | 新規 .cs の `<Compile Include>` 追加 |

---

### Task 0: 追跡部品のチェック解除判定をストア直参照へ直す（前提修正）

D6 の修正。**Task 4 より先に入れないと、着替えでチェック済みマテリアルのキーが全消去される。**

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs:106-151`

**Interfaces:**
- Consumes: `EditTargetStore.IsModified(string)`
- Produces: 挙動の変更のみ（シグネチャは不変）。`trackedStore` を持つ全レイヤー（M1 / M2 / 表情 / M4）へ効く

- [ ] **Step 1: 解除判定を書き換える**

`UpdateTrackedBoneFilter` の `RemoveTrackedKeys` 呼び出し（133 行付近）を、候補一覧で絞った `checkedNames` との差分ではなく、**ストアに問い合わせた結果**で判定するよう変える。

```csharp
            var checkedNames = BuildTrackedCheckedNames(store);
            if (!targetChanged)
            {
                // チェックを外した項目はキーごと消す。0F 目の自動登録と対称にしないと、
                // 自動登録されたキーが残り続けてボーンメニューから消えなくなる。
                //
                // 判定は候補一覧 (checkedNames) ではなくストアへ直接問い合わせる。
                // 候補一覧はメイドの着替えなどで縮むことがあり、そのとき checkedNames から
                // 落ちただけの項目まで「チェック解除」とみなすと、ユーザーが作った
                // キーフレームが着替えのたびに黙って消える
                RemoveTrackedKeys(
                    _lastTrackedCheckedNames.Where(name => store == null || !store.IsModified(name)).ToList());
            }
            _lastTrackedCheckedNames = checkedNames;
```

`targetChanged == false` の枝では `store == _lastTrackedStore` が保証されるため、`store` が null なら前回も null で `_lastTrackedCheckedNames` は空になる。`store == null` の項は防御的な記述。

- [ ] **Step 2: `BuildTrackedCheckedNames` のコメントを実態へ合わせる**

「候補テーブルに存在するチェック済み項目」という説明は変わらないが、解除判定に使わなくなったことを書き添える。

```csharp
        /// <summary>
        /// 候補テーブルに存在するチェック済み項目。表示対象の算出に使う。
        /// チェック解除の判定には使わない (候補テーブルが縮んだだけの項目を解除と誤認するため)
        /// </summary>
        private HashSet<string> BuildTrackedCheckedNames(EditTargetStore store)
```

- [ ] **Step 3: ビルドとテストで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source\COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド成功、テスト全 PASS

- [ ] **Step 4: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBaseTracking.cs
git commit -m "fix(tracking): 候補一覧が縮んだだけの項目をチェック解除と誤認しない"
```

---

### Task 1: メイドマテリアルの変更追跡ストアを置く

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidMaterialEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include>` 追加）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:374-375` 付近（マネージャ登録）

**Interfaces:**
- Produces:
  - `MaidMaterialEditManager.instance` : `MaidMaterialEditManager`
  - `EditTargetStore GetStore(Maid maid)` — 無ければ作る
  - `EditTargetStore FindStore(Maid maid)` — 無ければ `null`（表示用）

- [ ] **Step 1: マネージャを新規作成する**

`FaceEditManager.cs` をそのまま横に置いた形。マテリアル名はスロット名込み（D2）なので、着替えで消えたマテリアルの記録は**捨てずに残す**（方針 3）。表示側の候補は `maidCache.materialNames` なので、現物に無い記録は単に表示されない。

```csharp
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスロットマテリアルの変更追跡。メイドごとに EditTargetStore を持つ。
    /// 記録する名前は ModelMaterial.name (= "{スロット名}/{Unity マテリアル名}") で、
    /// タイムライン側の候補名 (MaidCache.materialNames) と同じ文字列になるため修飾の変換は不要。
    /// 着替えで消えたマテリアルの記録は捨てない (現物に無ければ表示されないだけ)
    /// </summary>
    public class MaidMaterialEditManager : ManagerBase
    {
        private static MaidMaterialEditManager _instance;
        public static MaidMaterialEditManager instance
            => _instance ?? (_instance = new MaidMaterialEditManager());

        private MaidMaterialEditManager()
        {
        }

        private readonly Dictionary<Maid, EditTargetStore> _stores = new Dictionary<Maid, EditTargetStore>();
        private readonly List<Maid> _deadMaids = new List<Maid>();

        public EditTargetStore GetStore(Maid maid)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new EditTargetStore();
                _stores[maid] = store;
            }
            return store;
        }

        public EditTargetStore FindStore(Maid maid)
        {
            EditTargetStore store;
            return maid != null && _stores.TryGetValue(maid, out store) ? store : null;
        }

        public override void Update()
        {
            // 破棄済みメイドの記録を掃除する (FaceEditManager と同じ方式)
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
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる
            _stores.Clear();
        }
    }
}
```

- [ ] **Step 2: csproj へ登録する**

`COM3D2.SceneEditor.Plugin.csproj` の `MaidManipulation\ModelShapeKeyEditManager.cs` の行（201 行目付近）の並びへ、アルファベット順の位置に追加する。

```xml
    <Compile Include="MaidManipulation\MaidMaterialEditManager.cs" />
```

- [ ] **Step 3: マネージャレジストリへ登録する**

`COM3D2.SceneEditor.Plugin.cs:374-375` の並びへ追加する。

```csharp
                managerRegistry.RegisterManager(MaidMaterialEditManager.instance);
```

- [ ] **Step 4: ビルドで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
```
Expected: どちらも成功（0 Error）

- [ ] **Step 5: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidMaterialEditManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs
git commit -m "feat(tracking): メイドマテリアルの変更追跡ストアを追加する"
```

---

### Task 2: モデルマテリアルの変更追跡ストアと修飾名集約を置く

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelMaterialEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:374-375` 付近

**Interfaces:**
- Consumes: `EditTargetStore`、`ModelTrackedNameStore<TKey>`、`ModelQualifiedNames.Qualify(string, string)`
- Produces:
  - `ModelMaterialEditManager.instance` : `ModelMaterialEditManager`
  - `EditTargetStore trackedStore` — モデル修飾名の集約（レイヤーの `trackedStore` へ渡す）
  - `EditTargetStore GetStore(GameObject model)` / `EditTargetStore FindStore(GameObject model)`

- [ ] **Step 1: マネージャを新規作成する**

`ModelShapeKeyEditManager.cs` と同じ骨格。相違点は名前解決だけ（D3）。

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 配置モデルマテリアルの変更追跡。
    /// 記録はモデル (GameObject) ごとに Unity マテリアル名 (ModelMaterial.displayName) で持ち、
    /// タイムラインへ渡すモデル修飾名は集約側で毎回組み直す。
    /// 修飾名を直接記録に使わないのは、group 振り直しで名前が変わりうるため
    /// (ModelHackManager.FixGroup)
    /// </summary>
    public class ModelMaterialEditManager : ManagerBase
    {
        private static ModelMaterialEditManager _instance;
        public static ModelMaterialEditManager instance
            => _instance ?? (_instance = new ModelMaterialEditManager());

        private ModelMaterialEditManager()
        {
        }

        private readonly Dictionary<GameObject, EditTargetStore> _stores
            = new Dictionary<GameObject, EditTargetStore>();

        private readonly List<GameObject> _deadModels = new List<GameObject>();

        private readonly ModelTrackedNameStore<GameObject> _tracked
            = new ModelTrackedNameStore<GameObject>();

        // Sync へ毎フレーム渡すキー列。使い回してゴミを出さない
        private readonly List<GameObject> _keys = new List<GameObject>();

        /// <summary>モデル修飾名の集約。ModelMaterialTimelineLayer の追跡ストアとして使う</summary>
        public EditTargetStore trackedStore => _tracked.store;

        /// <summary>モデルのチェック集合 (Unity マテリアル名)。無ければ作る</summary>
        public EditTargetStore GetStore(GameObject model)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(model, out store))
            {
                store = new EditTargetStore();
                _stores[model] = store;
                _tracked.Invalidate();
            }
            return store;
        }

        /// <summary>既存のチェック集合を引くだけで新規生成はしない (表示用)</summary>
        public EditTargetStore FindStore(GameObject model)
        {
            EditTargetStore store;
            return model != null && _stores.TryGetValue(model, out store) ? store : null;
        }

        public override void Init()
        {
            // モデルの増減で group が振り直され、モデル修飾名が変わるため、
            // 一覧が変わった契機で集約を作り直させる。
            // UI トグルで購読が切れないよう、解除はしない (ModelShapeKeyEditManager と同じ理由)
            MTEP.StudioModelManager.onModelAdded += OnTimelineModelChanged;
            MTEP.StudioModelManager.onModelRemoved += OnTimelineModelChanged;
        }

        private void OnTimelineModelChanged(MTEP.StudioModelStat model)
        {
            _tracked.Invalidate();
        }

        public override void Update()
        {
            CleanupStores();
            SyncTrackedStore();
        }

        /// <summary>削除されたモデルの記録を捨てる</summary>
        private void CleanupStores()
        {
            _deadModels.Clear();
            foreach (var pair in _stores)
            {
                if (pair.Key == null)
                {
                    _deadModels.Add(pair.Key);
                }
            }
            foreach (var model in _deadModels)
            {
                _stores.Remove(model);
                _tracked.Invalidate();
            }
        }

        private void SyncTrackedStore()
        {
            _keys.Clear();
            foreach (var pair in _stores)
            {
                _keys.Add(pair.Key);
            }

            _tracked.Sync(
                _keys,
                model => _stores[model].version,
                (model, result) =>
                {
                    var modelName = GetModelName(model);
                    if (modelName == null)
                    {
                        return false;
                    }
                    foreach (var materialName in _stores[model].GetNames())
                    {
                        var name = ModelQualifiedNames.Qualify(modelName, materialName);
                        if (name != null)
                        {
                            result.Add(name);
                        }
                    }
                    return true;
                });
        }

        /// <summary>
        /// モデルルートからタイムライン側のモデル名を引く。取れなければ null。
        /// ModelMaterialController.model は GetOrCreate のたび上書きされ、
        /// MaterialEditWindow が触ると ProviderModelStat へ差し替わるため参照しない。
        /// StudioModelManager の一覧はタイムラインのロード後にしか埋まらないので、
        /// 未ロードのうちは解決できず集約側の再試行に任せる
        /// </summary>
        private static string GetModelName(GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            foreach (var stat in MTEP.StudioModelManager.instance.models)
            {
                if (stat != null && stat.transform != null && stat.transform.gameObject == model)
                {
                    return stat.name;
                }
            }
            return null;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でモデルが全て入れ替わるため記録を丸ごと捨てる
            _stores.Clear();
            _tracked.Clear();
        }
    }
}
```

- [ ] **Step 2: csproj とマネージャ登録へ追加する**

csproj:
```xml
    <Compile Include="MaidManipulation\ModelMaterialEditManager.cs" />
```

`COM3D2.SceneEditor.Plugin.cs`:
```csharp
                managerRegistry.RegisterManager(ModelMaterialEditManager.instance);
```

- [ ] **Step 3: ビルドとテストで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source\COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド成功、テスト全 PASS（既存テストの退行が無いこと）

- [ ] **Step 4: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelMaterialEditManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs
git commit -m "feat(tracking): モデルマテリアルの変更追跡ストアを追加する"
```

---

### Task 3: MaterialEditWindow へチェック行を出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs:138-349`

**Interfaces:**
- Consumes: `MaidMaterialEditManager.GetStore/FindStore`、`ModelMaterialEditManager.GetStore/FindStore`、`GUIView.DrawTrackedLabel(bool, Action<bool>, string, float, float)`
- Produces: なし（ウィンドウ内で閉じる）

- [ ] **Step 1: 追跡対象の引き当てを表す入れ物を足す**

3 タブが `DrawMaterialSelector` を共用しているため、対象ごとに違う「ストアの引き当て」と「記録する生名」を持ち回す。背景タブは既定値（全メンバ null）を渡してチェック行を出さない。

`MaterialEditWindow` クラスの先頭付近（`_materialComboBox` の宣言の後）へ:

```csharp
        /// <summary>
        /// チェック行の描画に必要な、対象ごとの引き当て。
        /// 背景タブは対象のタイムラインレイヤーと対象集合が食い違うため既定値 (追跡なし) を渡す
        /// </summary>
        private struct MaterialTrackTarget
        {
            /// <summary>表示判定用。まだ 1 つもチェックしていない対象のストアを作らないため FindStore を使う</summary>
            public Func<EditTargetStore> findStore;

            /// <summary>操作時のストア。遅延生成する</summary>
            public Func<EditTargetStore> getStore;

            /// <summary>記録する生名。メイドは ModelMaterial.name、モデルは displayName</summary>
            public Func<MTEP.ModelMaterial, string> getKey;

            public bool isEnabled => findStore != null && getStore != null && getKey != null;
        }
```

`using System;` を先頭へ追加する（`Func` のため）。

- [ ] **Step 2: `DrawMaterialSelector` / `DrawMaterialProperties` が対象を受け取るようにする**

`MaterialEditWindow.cs:256` の `DrawMaterialSelector` シグネチャと、`281` の `DrawMaterialProperties` シグネチャを変える。

```csharp
        private void DrawMaterialSelector(List<MTEP.ModelMaterial> materials, MaterialTrackTarget track)
```
本体末尾の呼び出しを:
```csharp
            DrawMaterialProperties(material, track);
```
へ変える。

```csharp
        private void DrawMaterialProperties(MTEP.ModelMaterial material, MaterialTrackTarget track)
```

- [ ] **Step 3: チェック行と自動チェックを描く**

`DrawMaterialProperties` の中身を書き換える。変更点は 3 つだけで、既存のプロパティ描画ループはそのまま残す。

1. スクロール開始直後にチェック行を置く
2. 「初期化」ボタンで `Unmark` する
3. 色 / 数値の `onChanged` で `Mark` する

```csharp
        private void DrawMaterialProperties(MTEP.ModelMaterial material, MaterialTrackTarget track)
        {
            var defaultTrans = MTEP.TransformDataModelMaterial.defaultTrans;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.SetEnabled(view.focusedComboBox == null);

            var trackKey = track.isEnabled ? track.getKey(material) : null;

            // 編集されたマテリアルは自動で追跡対象にする
            Action markTracked = () =>
            {
                if (trackKey != null)
                {
                    track.getStore().Mark(trackKey);
                }
            };

            view.BeginScrollView();
            {
                if (trackKey != null)
                {
                    var store = track.findStore();
                    var isModified = store != null && store.IsModified(trackKey);

                    // 変更追跡チェック。ON=タイムラインの表示とキー書き込みの対象。
                    // 手動 OFF は「未編集へ戻す」操作なので値も初期値へ戻す
                    Action<bool> onCheckChanged = newChecked =>
                    {
                        if (newChecked)
                        {
                            track.getStore().Mark(trackKey);
                        }
                        else
                        {
                            material.Reset();
                            track.getStore().Unmark(trackKey);
                        }
                    };

                    view.DrawTrackedLabel(isModified, onCheckChanged, material.displayName, -1, ROW_HEIGHT);
                }

                if (view.DrawButton("初期化", 80, ROW_HEIGHT))
                {
                    material.Reset();
                    // 初期値へ戻したのだから追跡からも外す (チェック OFF と同じ意味)
                    if (trackKey != null)
                    {
                        track.getStore().Unmark(trackKey);
                    }
                }

                foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
                {
                    if (!material.HasColor(propertyType))
                    {
                        continue;
                    }

                    var color = material.GetColor(propertyType);
                    var initialColor = material.GetInitialColor(propertyType);

                    // ColorPickerWindow はラベル文字列で編集対象を同定するため、
                    // 行ごとに一意なプロパティ名を渡す (空文字だと全行が「編集中」扱いになり、
                    // ピッカーの反映先も最後の行へ化ける)。ラベル描画は DrawColor 内で行われる
                    var cache = view.GetColorFieldCache(propertyType.ToString(), true);

                    view.DrawColor(cache, color, initialColor,
                        newColor =>
                        {
                            material.SetColor(propertyType, newColor);
                            markTracked();
                        });
                }

                foreach (var propertyType in MTEP.ModelMaterial.ValuePropertyTypes)
                {
                    if (!material.HasValue(propertyType))
                    {
                        continue;
                    }

                    var value = material.GetValue(propertyType);
                    var initialValue = material.GetInitialValue(propertyType);
                    var info = defaultTrans.GetCustomValueInfo(propertyType);

                    // _OutlineWidth は 0.001 前後の極小値のため桁数を増やす
                    var fieldType = propertyType == MTEP.ModelMaterial.ValuePropertyType._OutlineWidth
                        ? FloatFieldType.F4
                        : FloatFieldType.Float;

                    view.DrawLabel($"{propertyType} ({info.name})", -1, ROW_HEIGHT);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        fieldType = fieldType,
                        min = info.min,
                        max = info.max,
                        step = info.step,
                        defaultValue = initialValue,
                        value = value,
                        onChanged = newValue =>
                        {
                            material.SetValue(propertyType, newValue);
                            markTracked();
                        },
                    });
                }
            }
            view.EndScrollView();
        }
```

- [ ] **Step 4: 3 つの呼び出し元を書き換える**

`DrawMaidMaterial`（`MaterialEditWindow.cs:171`）:
```csharp
            DrawMaterialSelector(slot.materials, new MaterialTrackTarget
            {
                findStore = () => MaidMaterialEditManager.instance.FindStore(target),
                getStore = () => MaidMaterialEditManager.instance.GetStore(target),
                // タイムライン側の候補名 (MaidCache.materialNames) と同じ文字列
                getKey = material => material.name,
            });
```

`DrawModelMaterial`（`MaterialEditWindow.cs:209`）:
```csharp
            var modelObject = model.transform.gameObject;
            DrawMaterialSelector(model.materials, new MaterialTrackTarget
            {
                findStore = () => ModelMaterialEditManager.instance.FindStore(modelObject),
                getStore = () => ModelMaterialEditManager.instance.GetStore(modelObject),
                // 修飾名は group 振り直しで変わるため、記録は Unity マテリアル名で持つ
                getKey = material => material.displayName,
            });
```

`DrawBGModelMaterial`（`MaterialEditWindow.cs:253`）— 追跡なし:
```csharp
            // 背景タブは BGModelMaterialTimelineLayer と対象集合が違うため追跡チェックを出さない
            // (レイヤーは BGModelManager の配置モデル、こちらは現在の背景の Renderer)
            DrawMaterialSelector(model.materials, new MaterialTrackTarget());
```

- [ ] **Step 5: ビルドで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
```
Expected: どちらも成功

- [ ] **Step 6: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs
git commit -m "feat(ui): マテリアル編集へ変更追跡チェックを表示する"
```

---

### Task 4: メイドマテリアルレイヤーを絞り込む

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs:16-142`

**Interfaces:**
- Consumes: `MaidMaterialEditManager.FindStore(Maid)`、`TimelineLayerBase.trackedBoneNames` / `trackedStore` / `trackedCandidateNames` / `trackedHistoryPrefix` / `InvalidateTrackedBoneNames()`
- Produces: なし

- [ ] **Step 1: 追跡部品へ opt-in する**

`allBoneNames`（18-19 行目）を差し替え、override を足す。`isTrackedTargetReady` は既定（`maid != null`）で正しいので触らない。

```csharp
        // MTE 原本は三項演算子の条件が反転しており (null 時に参照 / 非 null 時に空リスト)、
        // マテリアル一覧が常に空になるため SE 側で修正している。
        // さらに SE では変更追跡チェック済みのマテリアルだけへ絞り込む
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => MaidMaterialEditManager.instance.FindStore(maid);

        protected override List<string> trackedCandidateNames
            => maidCache == null ? new List<string>() : maidCache.materialNames;

        protected override string trackedHistoryPrefix => "メイドマテリアル";
```

`using COM3D2.SceneEditor.Plugin;` をファイル先頭へ追加する（`EditTargetStore` / `MaidMaterialEditManager` のため。`TimelineLayerBaseTracking.cs` と同じ）。

- [ ] **Step 2: `UpdateMaterials` の 0F 自動キーを絞り込み後の集合にする**

`MaidMaterialTimelineLayer.cs:115-129` を書き換える。マテリアル一覧が作り直された（着替え・更新ボタン）ので、追跡集合のキャッシュも捨ててから使う。

```csharp
        private void UpdateMaterials()
        {
            if (maidCache == null)
            {
                return;
            }

            maidCache.UpdateMaterials();

            // 候補一覧が入れ替わったので追跡集合を作り直させる
            InvalidateTrackedBoneNames();

            InitMenuItems();

            // 0F 目の自動キーは絞り込み後の対象だけへ打つ。
            // 全マテリアルへ打つと「既存キーフレーム記載」経由で全部がメニューへ復活して絞り込みが効かなくなる
            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }
```

- [ ] **Step 3: `InitMenuItems` を絞り込み後の集合で組む**

`MaidMaterialTimelineLayer.cs:36-62` のマテリアル列挙を、絞り込み後の名前だけ通すようにする。子が 1 つも無いスロットは見出しごと出さない。

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            if (maidCache == null)
            {
                return;
            }

            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var stat in maidCache.slotStats)
            {
                if (stat == null || stat.materials.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var material in stat.materials)
                {
                    if (!targetNames.Contains(material.name))
                    {
                        continue;
                    }

                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(stat.name, stat.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    var menuItem = new BoneMenuItem(material.name, material.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }
```

- [ ] **Step 4: `UpdateFrame` の書き込みを絞り込む**

`MaidMaterialTimelineLayer.cs:131-142` を書き換える。方針 1 のとおりマテリアルは行独立なのでキー書き込みも絞る。

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // materialMap を直接回すと絞り込みを素通りするため、対象集合を回して引き当てる
            foreach (var materialName in allBoneNames)
            {
                var sourceMaterial = maidCache.GetMaterial(materialName);
                if (sourceMaterial == null)
                {
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataModelMaterial>(materialName);
                trans.Apply(sourceMaterial);
            }
        }
```

- [ ] **Step 5: `OnMaidChanged` で追跡集合を捨てる**

`MaidMaterialTimelineLayer.cs:107-110` の `OnMaidChanged` は `UpdateMaterials()` を呼ぶだけなので、Step 2 の `InvalidateTrackedBoneNames()` により自動で満たされる。**追加変更は不要**。この Step は確認のみ（`OnCurrentLayer` も同じ経路）。

- [ ] **Step 6: ビルドとテストで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source\COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド成功、テスト全 PASS（`XmlRoundTripTests` / `MteCompatibilityTests` に退行が無いこと）

- [ ] **Step 7: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MaidMaterialTimelineLayer.cs
git commit -m "feat(timeline): メイドマテリアルレイヤーをチェック済みマテリアルで絞り込む"
```

---

### Task 5: モデルマテリアルレイヤーを絞り込む

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs:16-161`

**Interfaces:**
- Consumes: `ModelMaterialEditManager.trackedStore`、`TimelineLayerBase` の追跡部品
- Produces: なし

- [ ] **Step 1: 追跡部品へ opt-in する**

`ModelMaterialTimelineLayer.cs:18` を差し替え、override を足す。モデルレイヤーは `maid` を持たないので `isTrackedTargetReady` を override する（M2 の `ModelShapeKeyTimelineLayer` と同じ）。

```csharp
        // 変更追跡チェック済みのマテリアルだけへ絞り込む
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => ModelMaterialEditManager.instance.trackedStore;

        protected override List<string> trackedCandidateNames => modelManager.materialNames;

        protected override string trackedHistoryPrefix => "モデルマテリアル";

        // モデルレイヤーは maid を持たないため、モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;
```

`using COM3D2.SceneEditor.Plugin;` をファイル先頭へ追加する。

- [ ] **Step 2: `InitMenuItems` を絞り込み後の集合で組む**

`ModelMaterialTimelineLayer.cs:38-57` を書き換える。

```csharp
        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.materials.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var material in model.materials)
                {
                    if (!targetNames.Contains(material.name))
                    {
                        continue;
                    }

                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    var menuItem = new BoneMenuItem(material.name, material.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }
```

- [ ] **Step 3: `Init` / モデル増減の 0F 自動キーを絞り込み後の集合にする**

`ModelMaterialTimelineLayer.cs:29-36`:
```csharp
        public override void Init()
        {
            base.Init();
            // 0F 目の自動キーは絞り込み後の対象だけへ打つ
            AddFirstBones(allBoneNames);

            StudioModelManager.onModelAdded += OnModelAdded;
            StudioModelManager.onModelRemoved += OnModelRemoved;
        }
```
（`allBoneNames` が `trackedBoneNames` になったので記述は変わらないが、コメントを足して意図を残す）

`ModelMaterialTimelineLayer.cs:106-113` の `OnModelAdded`:
```csharp
        public void OnModelAdded(StudioModelStat model)
        {
            // 候補一覧が入れ替わったので追跡集合を作り直させる
            InvalidateTrackedBoneNames();

            InitMenuItems();

            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }
```

`ModelMaterialTimelineLayer.cs:115-122` の `OnModelRemoved` — 削除されたモデルのキーを消すのは絞り込みと無関係なので `RemoveAllBones` の引数は変えない。追跡集合の作り直しだけ足す:
```csharp
        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var materialNames = model.materials.Select(x => x.name).ToList();
            RemoveAllBones(materialNames);
            ApplyCurrentFrame(true);
        }
```

- [ ] **Step 4: `UpdateFrame` の書き込みを絞り込む**

`ModelMaterialTimelineLayer.cs:152-161`:
```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // materialMap を直接回すと絞り込みを素通りするため、対象集合を回して引き当てる
            foreach (var materialName in allBoneNames)
            {
                var sourceMaterial = modelManager.GetMaterial(materialName);
                if (sourceMaterial == null)
                {
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataModelMaterial>(materialName);
                trans.Apply(sourceMaterial);
            }
        }
```

- [ ] **Step 5: ビルドとテストで検証する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25
dotnet test source\COM3D2.SceneEditor.Plugin.Tests
```
Expected: ビルド成功、テスト全 PASS

- [ ] **Step 6: コミットする**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs
git commit -m "feat(timeline): モデルマテリアルレイヤーをチェック済みマテリアルで絞り込む"
```

---

### Task 6: 実機確認

**Files:** なし（ユーザーへ依頼する確認項目）

- [ ] **Step 1: 確認項目をユーザーへ提示する**

このセッションではゲームを操作しないため、以下をユーザーに実施してもらう。ゲーム再起動のタイミングでまとめて行う。

メイドマテリアル:
1. マテリアルウィンドウのメイドタブでスロットとマテリアルを選び、`_Color` を変更 → チェックが自動で ON になる
2. タイムラインの「メイドマテリアル」レイヤーを開く → **そのマテリアルだけ**がボーンメニューに出る（従来は全マテリアル）
3. 0F 目にキーが自動で入っている
4. チェックを外す → 値が初期値へ戻り、ボーンメニューから消え、全フレームのキーも消える
5. 「初期化」ボタンでも同じく追跡から外れる
6. 着替えて「更新」を押す → 消えたスロットのマテリアルは表示されず、残ったスロットのチェックは維持される
7. **（Task 0 の裏付け／最重要）** チェック済みマテリアルに 0F 以外のフレームでもキーを打つ → 着替えて「更新」→ **元の衣装へ戻して「更新」** → 打ったキーが全フレームぶん残っていること。ここでキーが消えていれば Task 0 が効いていない

モデルマテリアル:
8. モデルを 2 体配置し、片方のマテリアルを編集 → チェックが ON になる
9. タイムラインの「モデルマテリアル」レイヤーに**編集した側のマテリアルだけ**が出る
10. モデルをもう 1 体追加して group が振り直されても、チェックしたマテリアルがメニューから消えない（`ModelTrackedNameStore` の再集約が効いている）
11. モデルを削除すると、そのモデルのマテリアルがメニューとキーから消える
12. マテリアルウィンドウのモデルタブを開いてからタイムラインへ戻っても、ボーンメニューの名前が化けない（D3 の `controller.model` 上書き対策）

背景（対象外の確認）:
13. 背景タブにチェック行が出ないこと。背景モデルマテリアルレイヤーの挙動が従来どおりであること

プリセット（変更していないことの確認）:
14. マテリアルを編集してシーンプリセットを保存 → 読み込みで従来どおり復元される（保存内容が減っていない）

- [ ] **Step 2: 報告された不具合を修正する**

修正が要る場合は該当 Task の Step へ戻る。修正後は Task 4 / 5 の Step でビルドとテストを再実行する。

---

### Task 7: ドキュメントを更新する

**Files:**
- Modify: `docs/superpowers/specs/modified-tracking-rollout-roadmap.md`
- Modify: `docs/superpowers/specs/timeline-remaining-work.md`

- [ ] **Step 1: ロードマップの Phase M4 を完了として書き直す**

`### Phase M4: マテリアル系` の見出しへ `✅ 完了 (YYYY-MM-DD)` を付け、既存の箇条書きの下へ「実装の要点」節を足す。書く内容:

- 対象はメイドスロットと配置モデルの 2 系統。**背景モデルマテリアルは対象外**（D1-a の理由 — 編集ウィンドウの背景タブは `BgMgr.BgObject` 配下の Renderer、レイヤーは `BGModelManager` の配置モデルで対象集合が違う）
- **シーンプリセットの保存フィルタは変更していない**（D1-b の理由 — `CaptureMaterial` が初期値差分で既に絞っており、チェック集合を重ねるとストアが空のときに保存が落ちる退行になる）
- メイドは編集側の生名（`ModelMaterial.name`）がタイムライン候補名とそのまま一致するため修飾の橋渡しが不要。モデルは M2 と同じ `ModelTrackedNameStore` 集約
- モデル名の解決に `ModelMaterialController.model` を使わず `StudioModelManager.models` から引く（`GetOrCreate` が最終書き込み勝ちで、編集ウィンドウを開くと `ProviderModelStat` へ差し替わるため）
- マテリアルレイヤーの `UpdateFrame` は `materialMap.Values` を回していて `allBoneNames` を見ていなかったため、書き込み絞り込みは**手で `allBoneNames` 走査へ変えた**（他フェーズのように自動では付かない）
- 既存の `AddFirstBones(全マテリアル)` を `AddFirstBones(allBoneNames)` へ変えないと、全マテリアルに 0F キーが入って「既存キーフレーム記載」経由で絞り込みが無効化される
- チェック集合は履歴に含めない（M2 / M3 と同じ縮退判断）
- **M0 部品の修正を伴った**（D6）: メイドスロットは「同じ対象のまま候補一覧だけが伸縮する」M0 以来はじめてのケース（着替え）で、チェック解除の判定を候補一覧経由からストア直参照へ変えないと、着替えのたびにチェック済みマテリアルのキーが全フレームから消えていた。M1 / M2 / 表情にも効く修正
- タイムライン管理外のモデル（`ModelProviderHost` 経由の外部プラグイン提供モデル）はチェックしても効かない（D7。M2 と同じ既知の制約）

さらに `## 2. 設計方針(全 Phase 共通ルール)` の方針 3 へ、**「現物に無いチェック名は捨てずに保持する」だけでは足りず、タイムライン側のキーフレームも守る必要がある**（D6 の教訓）ことを 1 行追記する。

- [ ] **Step 2: 残タスクへ背景マテリアルと `controller.model` の課題を書き足す**

`docs/superpowers/specs/timeline-remaining-work.md` へ 2 件追記する。

1. **背景モデルマテリアルの変更追跡が未対応** — 編集ウィンドウの背景タブとレイヤーの対象集合が違う。載せるには編集ウィンドウへ `BGModelManager` 配置モデル向けのタブを足す必要がある
2. **タイムライン管理外のモデルは変更追跡が効かない** — `ModelProviderHost` 経由の外部プラグイン提供モデルは `StudioModelManager.models` に載らないため、マテリアル / シェイプキーのチェックを ON にしてもタイムラインへ反映されない。UI 上は押せてしまうため、無効であることが伝わらない（M2 / M4 共通）
3. **`ModelMaterialController.model` が最終書き込み勝ち** — `GetOrCreate` が呼ばれるたび `controller.model` を上書きするため、`MaterialEditWindow` のモデルタブを開くと `ModelMaterial.name` が `ProviderModelStat` 由来（group 無し）へ化け、`StudioModelManager.materialMap` のキーと食い違いうる。M4 の追跡側は `controller.model` に依存しない設計なので影響を受けないが、既存のマテリアルキーフレーム経路には残っている

- [ ] **Step 3: docs-review スキルでレビューする**

Run: `/docs-review`（対象: 上記 2 ファイルの差分）
Expected: 整合性・可読性の指摘を取り込んで書き直す

- [ ] **Step 4: コミットする**

```bash
git add docs/superpowers/specs/modified-tracking-rollout-roadmap.md docs/superpowers/specs/timeline-remaining-work.md
git commit -m "docs: 変更追跡ロードマップの M4 完了を記録する"
```

---

## Self-Review

**1. Spec coverage（ロードマップ Phase M4 の 4 項目）**

| スペックの項目 | 対応 |
|---|---|
| マテリアル用ストア（メイドは Maid、モデルは M2 と同じモデルキー） | Task 1 / Task 2 |
| MaterialEditWindow へチェック行 | Task 3 |
| `MaidMaterialTimelineLayer` / `ModelMaterialTimelineLayer` へ M0 部品接続 | Task 4 / Task 5 |
| `BGModelMaterialTimelineLayer` へ M0 部品接続 | **未対応（D1-a で対象外と決定、Task 7 で理由を文書化）** |
| プリセットのマテリアル保存をチェック済みフィルタへ | **未対応（D1-b で変更しないと決定、Task 7 で理由を文書化）** |
| 前提タスク: マテリアル編集の HistoryManager 対応 | **未対応（Global Constraints で M2 / M3 と同じ縮退判断と明記）** |

**2. Placeholder scan:** 全ステップに実コードまたは実行コマンドがある。「適切に処理する」類の記述なし。

**3. Type consistency:**
- `MaterialTrackTarget`（Task 3 Step 1 で定義）のメンバ名 `findStore` / `getStore` / `getKey` / `isEnabled` は Step 3・Step 4 の使用箇所と一致
- `MaidMaterialEditManager.GetStore/FindStore(Maid)`（Task 1）→ Task 3 Step 4 と Task 4 Step 1 で同じ引数型
- `ModelMaterialEditManager.trackedStore`（Task 2）→ Task 5 Step 1 で使用
- `ModelMaterialEditManager.GetStore/FindStore(GameObject)`（Task 2）→ Task 3 Step 4 で `model.transform.gameObject` を渡す
- `TimelineLayerBase` 側の override 名（`trackedStore` / `trackedCandidateNames` / `trackedHistoryPrefix` / `isTrackedTargetReady` / `InvalidateTrackedBoneNames()` / `trackedBoneNames`）は `Timeline/TimelineLayer/TimelineLayerBaseTracking.cs` の実体と一致

**4. レビュー反映後の追記:** Task 0（M0 部品の解除判定修正）を追加し、D6 / D7 を新設した。Task 6 の実機確認へ「チェック → 着替え → 元の衣装へ戻す」でキーフレームが残ることの確認（項目 7）を追加した。

---

## レビュー却下メモ

- **Task 4/5 の `AddFirstBones(allBoneNames)` 手動呼び出しを M2 に倣って削除すべき（🟡）** — 却下。M2 が呼んでいないのは実装の都合であり、意図的な設計ではない。`AddFirstBones` は 0F に既にボーンがある名前を飛ばす冪等な実装（`TimelineLayerBase.cs:1151-1181`）で、引数を絞り込み後の集合にした時点で「チェック済みマテリアルにだけ 0F の起点を保証する」正しい動きになる。削除すると、チェック済みの状態でタイムラインを読み込んだ場合に 0F の起点が作られない（`UpdateTrackedBoneFilter` の初回構築は追加扱いにしないため）。指摘が挙げた「🔴 と絡み合う」という懸念は Task 0 の修正で解消済み。
- **`firstFrame.SetBone(tmpBone)` に null が渡りうる（レビュー外の自己確認）** — 対処不要。`UpdateFrame` を `allBoneNames` 走査へ変えると、着替えで消えたマテリアルは `tmpFrame` に載らず `tmpBone` が null になりうるが、`FrameData.SetBone` は null を早期 return する（`Timeline/FrameData.cs:103-108`）。
