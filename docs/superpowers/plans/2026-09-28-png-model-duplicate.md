# PNG・モデルをタイムラインごと複製 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PNG 配置に「複製」を足し、タイムライン読込中は元のキーごと写す。モデルの既存「複製」で、マテリアルのキーが効かない不具合を直し、シェーダー変更も複製先へ写す。あわせて、PNG の番号振り直しで一部のキーが孤立する不具合と、PNG の追加・複製を Undo しても実体が残る不具合を直す。

**Architecture:**
- モデル: `TimelineManager.CopyModel` → 各レイヤーの `OnCopyModel` の既存経路はそのまま使う。マテリアルレイヤーの複製先の名前は、修飾名のモデル名部分だけを差し替える純関数 `ModelQualifiedNames.Requalify` で作る。シェーダー変更は `MaterialShaderSync.CopyForModel` (純関数) でエントリを丸ごと複製して `MaterialShaderManager` の保留へ積む。複製先の実体はプロバイダが遅れて作るので、既存の保留・再試行の仕組みで適用される
- PNG の実体: `PngPlacementManager.DuplicatePng` は、シーンプリセット・履歴と同じ 1 枚分の記録 (`PngPlacementSnapshot.CaptureObject` / `ApplyObject`) で全設定を写す。項目を足したときに複製だけ漏れることを防ぐ
- PNG のタイムライン: `PngObjectTimelineManager` に「実体 → 希望する番号」の予約 (`_requestedGroups`) を足す。読込では XML の番号を、複製では空いている最小の番号を予約してから `RebuildIfChanged` を回す。複製では予約した名前へ元のキーを写してから対応表へ載せるので、`onObjectAdded` → `AddFirstBones` (初期フレームの自動登録) は 0F に写したキーがあれば何もしない
- Undo/Redo の全再構築 (`TimelineManager.UpdateTimeline`) のときだけ、`Setup` が XML に無い実体を消す (`StudioModelManager.SetupModels` と同じ扱い)。新規作成・ファイル読込では従来どおり消さない
- 複製ボタンは Inspector (PNG 固有欄) と PNG配置ウィンドウの「配置済み」タブの操作行に置き、どちらも `PngDuplicator.Duplicate` を呼ぶ

**Tech Stack:** C# (Unity IMGUI / COM3D2 両ビルド), xunit (net48)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「8. PNG・モデルをタイムラインごと複製」

## 仕様

仕様書の #8 を実装の粒度に落としたもの。仕様書に無い判断には (判断) を付けた。

### モデル

- 既存の「複製」ボタン (`ModelManageRowDrawer`。タイムラインの項目 Inspector にしか出ない) を使う。タイムライン未読込では従来どおり何もしない
- マテリアルのキー名を `"{new}/{mat}"` にする (現状は `"{new}/{src}/{mat}"` になり、複製先のマテリアルに効かない)
- シェーダー変更 (`<MaterialShaders>` の `MaidSlotNo = -1`、`Owner` = 元のモデル名 のエントリ) を、`Owner` だけ複製先のモデル名に変えて写す。エントリは `Clone()` で丸ごと写すので、#7/#13 (テクスチャ差し替え) がエントリへ足す項目も一緒に写る。#7/#13 はテクスチャ指定をリストで持つ場合、`TimelineMaterialShaderData.Clone()` をリストまで複製する実装にすること (今の `MemberwiseClone` のままだと、元と複製先で同じリストを共有する)
- 履歴は既存の「モデルの追加: {表示名}」1 件のまま (判断: 仕様書は PNG だけ文言を指定している)

### PNG

- `PngPlacementManager.DuplicatePng(PngObjectData)` を足す。画像・位置・回転・拡縮・ビルボード・色・明るさ・彩度・表示順・表示・表示タイプ・ブレンド・デカール設定 (フェード角・メイドにも投影) を写す
- 複製先は一覧の末尾に置く (判断: 元の直後へ差し込むと、タイムライン未読込の Undo (`PngPlacementSnapshot.ApplyState` は並び順で照合する) が後ろの実体を作り直してしまう)。位置は元と同じ (重なる)。複製後は複製先を選択し、ギズモで動かせるようにする
- ボタン:
  - Inspector の PNG 固有欄 (`PngPlacementInspector.Draw`) の「表示 / ビルボード」行の右に「複製」。タイムラインの項目 Inspector (`PngPlacementItemInspector`) も同じ関数で描くので、そちらにも出る
  - PNG配置ウィンドウの「配置済み」タブ: タイル一覧の上に操作行を置き、「複製」(選択中の PNG が無いときは無効) と選択中の名前を出す (判断: タイルの角に描ける操作は `GUIView.DrawTileView` の削除 `x` だけで、`GUIView` は共有サブモジュール `MTEUtils` にある。タイルごとのボタンにするとサブモジュールの変更になるため、選択中の 1 枚への操作行にする)
- タイムライン読込中:
  - 元のキー (PNG配置レイヤーの全キーフレーム) を新しい名前へ写してから対応表へ載せる。初期フレームの自動登録より前なので、0F は元のキーの値になる
  - 新しい名前は `imageName` + 同じ画像内で空いている最小の番号 (`PluginUtils.GetGroupSuffix`)。画像が見つからず保留になっている定義 (`_unresolved`) の番号も使用中として扱う
  - 履歴は「PNGの複製: {新しい名前}」1 件。シーン履歴の `BeforeEdit` は通さない (タイムラインでは自動キーフレーム登録を誘発し、履歴が 2 件になりうるため)
  - PNG配置レイヤーを追加していないタイムラインでは、キーを写す先が無いので実体だけ複製する
- タイムライン未読込: シーン履歴に「PNGの複製: {元の名前}」1 件 (`HistoryScope.PngPlacement`)
- 既知の制限 (判断): タイムラインの Redo で複製先を作り直すと、ビルボードは既定 (ON) に戻る。ビルボードはキーにも実体定義にも無いため。PNG の削除を Undo したときと同じ既存の制限

### 既存の不具合

- 読込時の番号: `Setup` で作った実体は XML の `group` を予約して対応表へ載せる。欠番のある XML (`a`, `a (2)`) を読んでも `a (2)` のキーが効く
- Undo で実体が残る: `TimelineManager.UpdateTimeline` (Undo/Redo の全再構築) から呼ばれた `Setup` だけ、XML に無い実体を消す。消す実体が選択中なら選択を外す。新規作成 (`CreateNewTimeline`) とファイル読込 (`LoadTimeline`) では消さない (判断: 仕様書は Undo だけを挙げている。新規作成で消すと、シーンに置いた PNG がタイムラインを作っただけで消える)
- 保存形式は変わらない

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること。`Vector2Int` は 2.0 の Unity に無いので使わない
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`。COM3D25 を最後にビルドしてからテストする)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。Unity のネイティブ呼び出し (`Quaternion.Euler`, `GUI.*`, `SystemInfo.*` 等) はテストから呼べない。`Mathf` / `Rect` / `Vector2` の算術は可
- 新規 .cs は `COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include=...>` へ追加する (ワイルドカードではない)。リネームは `git mv` と csproj の書き換えの両方
- `MTEUtils/` はサブモジュール。本計画では変更しない (`GUIView` の公開 API だけを使う)
- 実機検証は通常シーン (撮影モード非対応)

## Review Focus

1. タイムライン読込中に PNG を複製して Undo すると複製先が消え、Redo で同じ名前・同じキーの複製先が戻る。Inspector に消えた PNG が残らない — Task 3 のテスト「余りの実体名」「予約した番号」、Task 4 の実機確認
2. 複数フレームにキーがある PNG を、0F 以外のフレームで複製しても、複製先が元と同じ動きで再生される (0F が複製した時点の値で上書きされない) — Task 4 の実機確認
3. 欠番のある XML (`a` と `a (2)` だけ) を読むと、`a (2)` のキーが実体に効く — Task 3 のテスト「空いていれば予約した番号」、Task 3 の実機確認
4. モデルを複製すると、複製先のマテリアルのキーが再生で効き、シェーダー変更も複製先に付く。複製先のモデルが数フレーム遅れて生成されても付く — Task 1・2 のテスト、Task 2 の実機確認
5. 新規タイムライン作成・ファイル読込では、シーンに置いた PNG が消えない (余りの削除は Undo/Redo だけ) — Task 3 の実機確認
6. タイムライン未読込で複製すると、シーン履歴に「PNGの複製」が 1 件積まれ、Undo で複製先だけが消える (元の実体は作り直されない) — Task 4 の実機確認
7. モデルを複製した直後 (実体の生成前) に複製先を削除しても、シェーダー変更のエントリが残らない (残ると同じ番号で次に置いたモデルに付く) — Task 2 Step 5 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelQualifiedNames.cs` | 修飾名のモデル名部分の差し替え (`Requalify`) |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs` | 複製先のマテリアルのキー名を直す |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs` | モデルのシェーダー変更エントリの複製 (`CopyForModel`) |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs` | 複製したエントリを保留と保存データへ積む |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | `CopyModel` でシェーダー変更を写す、`isRestoringHistory` |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs` | 番号の予約・XML の番号保持・余りの削除・複製の登録 |
| Modify `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs`、`TimelineLayerBase.cs`、`PngPlacementTimelineLayer.cs`、`Timeline/TimelineData.cs` | PNG のキーの複製 (`OnCopyPngObject`) |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs` | 1 枚分の記録・適用を切り出す |
| Modify `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` | `DuplicatePng` |
| Create `source/COM3D2.SceneEditor.Plugin/Manager/PngDuplicator.cs` | 複製ボタンの共通処理 (履歴・タイムライン登録・選択) |
| Modify `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs`、`PngPlacementWindow.cs` | 複製ボタン |
| Modify `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | `PngDuplicator.cs` の追加 |
| Modify `source/COM3D2.SceneEditor.Plugin.Tests/ModelQualifiedNamesTests.cs`、`MaterialShaderSyncTests.cs` | テスト追加 |
| Create `source/COM3D2.SceneEditor.Plugin.Tests/PngObjectGroupTests.cs` | PNG の番号・余りのテスト |
| Modify `docs-site/timeline/layers-background.md`、`docs-site/timeline/layers-model.md`、`docs-site/guide/staging.md` | 説明 |

---

### Task 1: モデル複製でマテリアルのキー名を直す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelQualifiedNames.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs:170-191`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelQualifiedNamesTests.cs`

**Interfaces:**
- Produces: `ModelQualifiedNames.Requalify(string sourceModelName, string newModelName, string qualifiedName) : string` (修飾されていなければ null)

- [ ] **Step 1: 失敗するテストを書く**

`ModelQualifiedNamesTests.cs` のクラス末尾に足す:

```csharp
        [Fact]
        public void Requalifyはモデル名の部分だけを差し替える()
        {
            Assert.Equal(
                "x.menu (2)/mat01",
                ModelQualifiedNames.Requalify("x.menu", "x.menu (2)", "x.menu/mat01"));
        }

        [Fact]
        public void Requalifyは生名にスラッシュがあってもそのまま残す()
        {
            Assert.Equal(
                "y.menu/a/b",
                ModelQualifiedNames.Requalify("x.menu", "y.menu", "x.menu/a/b"));
        }

        [Theory]
        [InlineData("x.menu2/mat01")]
        [InlineData("other.menu/mat01")]
        [InlineData("x.menu")]
        [InlineData("")]
        [InlineData(null)]
        public void Requalifyは元のモデル名で修飾されていなければnullを返す(string qualifiedName)
        {
            Assert.Null(ModelQualifiedNames.Requalify("x.menu", "x.menu (2)", qualifiedName));
        }
```

- [ ] **Step 2: 失敗を確認する**

MSBuild COM3D2 → COM3D25 (Global Constraints)。`Requalify` が無いのでテストプロジェクトのビルドが `CS0117` で失敗する。

- [ ] **Step 3: 実装する**

`ModelQualifiedNames.cs` の先頭に `using System;` を足し、`Qualify` の下へ:

```csharp
        /// <summary>
        /// 修飾名のモデル名部分を差し替える。モデル複製でキーを写す先の名前に使う。
        /// qualifiedName が sourceModelName で修飾されていなければ null
        /// </summary>
        public static string Requalify(string sourceModelName, string newModelName, string qualifiedName)
        {
            if (string.IsNullOrEmpty(sourceModelName) || string.IsNullOrEmpty(qualifiedName))
            {
                return null;
            }

            var prefix = sourceModelName + "/";
            if (!qualifiedName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }
            return Qualify(newModelName, qualifiedName.Substring(prefix.Length));
        }
```

`ModelMaterialTimelineLayer.OnCopyModel` を置き換える:

```csharp
        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var sourceModelName = sourceModel.name;
            var sourceModelMaterials = sourceModel.materials;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                foreach (var sourceModelMaterial in sourceModelMaterials)
                {
                    // ModelMaterial.name はモデル名で修飾済み。そのまま修飾すると "{new}/{src}/{mat}" になり効かない
                    var sourceMaterialName = sourceModelMaterial.name;
                    var sourceMaterial = keyFrame.GetBone(sourceMaterialName);
                    if (sourceMaterial == null)
                    {
                        continue;
                    }

                    var newMaterialName = ModelQualifiedNames.Requalify(
                        sourceModelName, newModelName, sourceMaterialName);
                    if (newMaterialName == null)
                    {
                        continue;
                    }

                    var newMaterial = keyFrame.GetOrCreateBone(sourceMaterial.transform.type, newMaterialName);
                    newMaterial.transform.FromTransformData(sourceMaterial.transform);
                }
            }

            // 複製先のキーを追跡集合へ即座に反映する (ボーン・シェイプキーのレイヤーと揃える)
            InvalidateTrackedBoneNames();
        }
```

(`ModelMaterialTimelineLayer.cs` は既に `using COM3D2.SceneEditor.Plugin;` を持つ)

- [ ] **Step 4: 両構成をビルドしてテストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ModelQualifiedNamesTests` が PASS。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/ModelQualifiedNames.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ModelMaterialTimelineLayer.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelQualifiedNamesTests.cs
git commit -m "fix(model): 複製したモデルのマテリアルキー名が二重に修飾される不具合を直す"
```

---

### Task 2: モデル複製でシェーダー変更を写す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs` (`ResetShadersNotIn` の下)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:2482-2501` (`CopyModel`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderSyncTests.cs`

**Interfaces:**
- Produces:
  - `MaterialShaderSync.CopyForModel(List<TimelineMaterialShaderData> entries, string sourceOwner, string newOwner) : List<TimelineMaterialShaderData>` (複製。元のリストは変えない)
  - `MaterialShaderManager.CopyModelEntries(string sourceOwner, string newOwner) : void`

- [ ] **Step 1: 失敗するテストを書く**

`MaterialShaderSyncTests.cs` のクラス末尾に足す (`Entry` ヘルパーは既存):

```csharp
        [Fact]
        public void CopyForModelは元モデルのエントリを複製先の名前で写す()
        {
            var entries = new List<TimelineMaterialShaderData>
            {
                Entry(-1, "x.menu", "m0", 0, "com3d2mod/X"),
                Entry(-1, "x.menu", "m1", 1, "com3d2mod/Y"),
                Entry(-1, "other.menu", "m0", 0, "com3d2mod/Z"),
            };

            var copies = MaterialShaderSync.CopyForModel(entries, "x.menu", "x.menu (2)");

            Assert.Equal(2, copies.Count);
            Assert.All(copies, c => Assert.Equal("x.menu (2)", c.owner));
            Assert.All(copies, c => Assert.Equal(-1, c.maidSlotNo));
            Assert.Equal("m0", copies[0].material);
            Assert.Equal(0, copies[0].index);
            Assert.Equal("com3d2mod/X", copies[0].shader);
            Assert.Equal("m1", copies[1].material);
            Assert.Equal("com3d2mod/Y", copies[1].shader);
        }

        [Fact]
        public void CopyForModelはメイドのエントリを写さない()
        {
            // メイドの所有者はスロット名。モデル名と同じ文字列でも別物
            var entries = new List<TimelineMaterialShaderData> { Entry(0, "x.menu", "m0", 0, "s") };

            Assert.Empty(MaterialShaderSync.CopyForModel(entries, "x.menu", "x.menu (2)"));
        }

        [Fact]
        public void CopyForModelの結果は元のエントリと別インスタンス()
        {
            var entries = new List<TimelineMaterialShaderData> { Entry(-1, "x.menu", "m0", 0, "s") };

            var copies = MaterialShaderSync.CopyForModel(entries, "x.menu", "x.menu (2)");
            copies[0].shader = "changed";

            Assert.Equal("x.menu", entries[0].owner);
            Assert.Equal("s", entries[0].shader);
        }
```

- [ ] **Step 2: 失敗を確認する**

MSBuild COM3D2 → COM3D25。`CopyForModel` が無いのでテストプロジェクトのビルドが失敗する。

- [ ] **Step 3: 実装する**

`MaterialShaderSync.cs` の `ListEquals` の下へ:

```csharp
        /// <summary>
        /// モデル複製用。元モデル (sourceOwner) のエントリを複製し、所有者だけ newOwner に変える。
        /// エントリは Clone で丸ごと写すので、後からエントリへ足した項目 (テクスチャ差し替え等) も一緒に写る。
        /// メイドのエントリ (maidSlotNo >= 0) は所有者がスロット名なので対象外
        /// </summary>
        public static List<TimelineMaterialShaderData> CopyForModel(
            List<TimelineMaterialShaderData> entries, string sourceOwner, string newOwner)
        {
            var result = new List<TimelineMaterialShaderData>();
            foreach (var entry in entries)
            {
                if (entry.maidSlotNo >= 0 || entry.owner != sourceOwner)
                {
                    continue;
                }

                var copy = entry.Clone();
                copy.owner = newOwner;
                result.Add(copy);
            }
            return result;
        }
```

`MaterialShaderManager.cs` の `ResetShadersNotIn` の下へ:

```csharp
        /// <summary>
        /// モデル複製で、元モデルのシェーダー変更を複製先の名前で写す。
        /// 複製先の実体はプロバイダが遅れて作るため保留へ積み、定期の再試行で適用させる。
        /// 保存が再試行より先に来ても載るよう、保存データへもすぐ併せる
        /// </summary>
        public void CopyModelEntries(string sourceOwner, string newOwner)
        {
            if (timeline == null)
            {
                return;
            }

            var copies = MaterialShaderSync.CopyForModel(timeline.materialShaders, sourceOwner, newOwner);
            if (copies.Count == 0)
            {
                return;
            }

            foreach (var copy in copies)
            {
                _pending.RemoveAll(p => p.IsSameTarget(copy));
                _pending.Add(copy);
            }

            // Undo で消えた前回の複製先の保留が同名で残っていることがあるため、今回の複製を勝たせる
            timeline.materialShaders = MaterialShaderSync.Merge(copies, timeline.materialShaders);
        }
```

`TimelineManager.CopyModel` の `timeline.OnCopyModel(model, newModel);` の直後へ:

```csharp
            MaterialShaderManager.instance.CopyModelEntries(model.name, newModel.name);
```

- [ ] **Step 4: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 5: 実機確認 (Review Focus 4)**

通常シーン (デイリー画面でエディタを有効化) で、タイムラインを新規作成し、ModItemExplorer でモデルを 1 体置く。マテリアルウィンドウでそのモデルのマテリアルの色を 0F と 30F で変えてキーを打ち、シェーダーを別のもの (NPR が無ければ `CM3D2/Lighted` 等) に変える。項目 Inspector の「複製」を押す。

- マテリアルレイヤーのボーンメニューに、複製先のモデルの見出しとマテリアルが出ること (名前に元のモデル名が二重に入っていないこと)
- 再生すると複製先の色も元と同じく変わること
- 30 フレーム程度待つと、複製先のマテリアルのシェーダーが元と同じになること。devbridge `eval_csharp` で確認する:

```csharp
return string.Join("\n", COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance.timeline.materialShaders
    .Select(e => e.maidSlotNo + " " + e.owner + " " + e.material + " " + e.shader).ToArray());
```

複製先のモデル名のエントリがあること。Undo で複製先のモデルが消えること。

続けて、複製した直後 (30 フレーム待たずに) 複製先のモデルを削除し、上のコードでエントリを見る。複製先の名前のエントリが残っている場合は、削除経路 (`StudioModelManager.DeleteModel`) で同じ owner の `timeline.materialShaders` と `MaterialShaderManager` の保留を消す処理を足す。残ったままだと、後で同じ番号のモデルを置いたときに無関係なシェーダー変更が付く。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/MaterialShaderSync.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaterialShaderManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs source/COM3D2.SceneEditor.Plugin.Tests/MaterialShaderSyncTests.cs
git commit -m "feat(model): モデル複製でシェーダー変更も複製先へ写す"
```

---

### Task 3: PNG の読込で XML の番号を保ち、Undo で余った実体を消す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs:76-260, 348-369`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:624-658` (`UpdateTimeline`)
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/PngObjectGroupTests.cs`

**Interfaces:**
- Produces:
  - `PngObjectTimelineManager.AssignGroup(ICollection<int> usedGroups, int requestedGroup) : int` (static。requestedGroup < 0 は希望なし)
  - `PngObjectTimelineManager.GetSurplusNames(IEnumerable<string> currentNames, IEnumerable<TimelinePngObjectData> sources) : List<string>` (static)
  - `PngObjectTimelineManager.Setup(List<TimelinePngObjectData> pngObjectDatas, bool removeSurplus)`
  - `PngObjectTimelineManager` の private `_requestedGroups : Dictionary<SE.PngObjectData, int>` と `GetUsedGroups(string imageName) : HashSet<int>` (Task 4 が使う)
  - `TimelineManager.isRestoringHistory : bool` (get のみ公開)

- [ ] **Step 1: 失敗するテストを書く**

`PngObjectGroupTests.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインの PNG 名 (画像名 + 番号) の採番と、Undo で消す余りの判定を固定する。
    /// キーフレームは名前で実体に結び付くため、番号がずれるとキーが孤立する
    /// </summary>
    public class PngObjectGroupTests
    {
        [Fact]
        public void 希望が無ければ空いている最小の番号()
        {
            Assert.Equal(0, PngObjectTimelineManager.AssignGroup(new HashSet<int>(), -1));
            Assert.Equal(2, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0, 1 }, -1));
        }

        [Fact]
        public void 欠番があれば欠番を使う()
        {
            Assert.Equal(1, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0, 2 }, -1));
        }

        [Fact]
        public void 空いていれば予約した番号を使う()
        {
            // XML に a と a (2) だけがあるとき、a (2) を 1 へ詰めない
            Assert.Equal(2, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0 }, 2));
        }

        [Fact]
        public void 予約した番号が使用中なら空いている最小の番号()
        {
            Assert.Equal(0, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 2 }, 2));
        }

        [Fact]
        public void 番号1以上は括弧付きの名前になる()
        {
            Assert.Equal("a", new TimelinePngObjectData { imageName = "a", group = 0 }.name);
            Assert.Equal("a (2)", new TimelinePngObjectData { imageName = "a", group = 2 }.name);
        }

        [Fact]
        public void 余りの実体名はXMLの定義に無い名前()
        {
            var sources = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "a", group = 0 },
                new TimelinePngObjectData { imageName = "b", group = 0 },
            };

            var surplus = PngObjectTimelineManager.GetSurplusNames(
                new[] { "a", "a (1)", "b" }, sources);

            Assert.Equal(new[] { "a (1)" }, surplus.ToArray());
        }

        [Fact]
        public void XMLが空なら全部余り()
        {
            var surplus = PngObjectTimelineManager.GetSurplusNames(
                new[] { "a", "b" }, new List<TimelinePngObjectData>());

            Assert.Equal(new[] { "a", "b" }, surplus.ToArray());
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

MSBuild COM3D2 → COM3D25。`AssignGroup` / `GetSurplusNames` が無いのでテストプロジェクトのビルドが失敗する。

- [ ] **Step 3: 番号の予約と純関数を実装する**

`PngObjectTimelineManager.cs` の `_unresolved` の宣言の下へ:

```csharp
        // 次の RebuildIfChanged で実体に割り当てる番号の希望。読込では XML の番号、複製では写したキーの番号。
        // 希望が無い実体は同名画像内で空いている最小の番号になる
        private readonly Dictionary<SE.PngObjectData, int> _requestedGroups
            = new Dictionary<SE.PngObjectData, int>();
```

`IsChanged` の上へ:

```csharp
        /// <summary>
        /// 同名画像内の番号を決める。requestedGroup (0 以上) が空いていればそれを、
        /// 希望が無いか使用中なら空いている最小の番号を返す
        /// </summary>
        public static int AssignGroup(ICollection<int> usedGroups, int requestedGroup)
        {
            if (requestedGroup >= 0 && !usedGroups.Contains(requestedGroup))
            {
                return requestedGroup;
            }

            var group = 0;
            while (usedGroups.Contains(group))
            {
                group++;
            }
            return group;
        }

        /// <summary>XML の定義に無い実体名。Undo/Redo の再構築で消す対象</summary>
        public static List<string> GetSurplusNames(
            IEnumerable<string> currentNames, IEnumerable<TimelinePngObjectData> sources)
        {
            var sourceNames = new HashSet<string>(sources.Select(d => d.name));
            return currentNames.Where(name => !sourceNames.Contains(name)).ToList();
        }

        /// <summary>
        /// 同名画像内で使用中の番号。画像が見つからず保留にした定義の番号も含める
        /// (実体に取られると、保留の定義のキーがその実体へ効いてしまうため)
        /// </summary>
        private HashSet<int> GetUsedGroups(string imageName)
        {
            var used = new HashSet<int>(
                _dataMap.Values.Where(e => e.imageName == imageName).Select(e => e.group));
            foreach (var data in _unresolved)
            {
                if (data.imageName == imageName)
                {
                    used.Add(data.group);
                }
            }
            return used;
        }
```

`RebuildIfChanged` の追加検出ループの番号決定 (現状 146-153 行) を置き換える:

```csharp
                var imageName = Path.GetFileNameWithoutExtension(data.relativePath ?? "");
                int requestedGroup;
                if (!_requestedGroups.TryGetValue(data, out requestedGroup))
                {
                    requestedGroup = -1;
                }
                var group = AssignGroup(GetUsedGroups(imageName), requestedGroup);
```

同じループの直後 (`pngObjects = seObjects.Select(...)` の直前) へ:

```csharp
            // 希望は割り当てた時点で役目を終える。消えた実体の希望も残さない
            _requestedGroups.Clear();
```

関数冒頭の summary を「既存の名前は維持し、新規実体には予約した番号か、同名画像内で空いている最小の番号を割り当てる」に直す (138 行のコメントも同様)。

- [ ] **Step 4: Setup を XML の番号保持と余りの削除に対応させる**

`Setup` を置き換える:

```csharp
        /// <summary>
        /// タイムライン読込時に PNG 実体を再生成する。
        /// 画像は SE の既知ソース (config → photo) からファイル名一致で探索し、
        /// 見つからない場合は警告してスキップする (キーフレームは XML に保持されたまま)。
        /// 作った実体には XML の番号を予約し、欠番のある XML でもキーとの対応を崩さない。
        /// removeSurplus なら XML に無い実体を消す (Undo/Redo の再構築用。追加・複製の Undo で実体を残さない)。
        /// 最後に XML の実体設定 (表示順・表示タイプ・ブレンド方式・デカール設定) を SE 実体へ適用する
        /// </summary>
        public void Setup(List<TimelinePngObjectData> pngObjectDatas, bool removeSurplus)
        {
            // 引数は timeline.pngObjects そのもので、途中の RebuildIfChanged → UpdateTimelineData が
            // 同じリストを消して SE 側の状態で書き直しうる。XML の値を失わないよう先に複製する
            var sources = new List<TimelinePngObjectData>(pngObjectDatas);
            _unresolved.Clear();

            RebuildIfChanged();

            if (removeSurplus)
            {
                RemoveSurplus(sources);
            }

            // ソースディレクトリの走査は 1 回にまとめ、画像名 → (source, relativePath) の辞書で解決する
            Dictionary<string, KeyValuePair<string, string>> imageIndex = null;

            foreach (var data in sources)
            {
                var name = data.name;
                if (_entryMap.ContainsKey(name))
                {
                    continue;
                }

                if (imageIndex == null)
                {
                    imageIndex = BuildImageIndex();
                }

                KeyValuePair<string, string> found;
                if (!imageIndex.TryGetValue(data.imageName, out found))
                {
                    MTEUtils.LogWarning(
                        "PNG 画像が見つかりません: {0} (UserData\\PngPlacement または PhotoModeData\\Texture へ配置してください)",
                        data.imageName);
                    _unresolved.Add(data);
                    continue;
                }

                var created = sePngManager.AddPng(found.Key, found.Value);
                if (created == null)
                {
                    MTEUtils.LogWarning("PNG の生成に失敗しました: {0}", data.imageName);
                    _unresolved.Add(data);
                    continue;
                }
                _requestedGroups[created] = data.group;
            }

            RebuildIfChanged();

            // XML の実体設定を、生成した実体と名前が一致した既存の実体へ戻す
            foreach (var data in sources)
            {
                var entry = GetPngObject(data.name);
                if (entry != null && entry.data != null)
                {
                    ApplyEntitySettings(entry.data, data);
                }
            }
        }

        /// <summary>XML に無い実体を消す。消す実体が選択中なら Inspector に残さない</summary>
        private void RemoveSurplus(List<TimelinePngObjectData> sources)
        {
            var surplusNames = GetSurplusNames(pngObjectNames, sources);
            if (surplusNames.Count == 0)
            {
                return;
            }

            var selection = SE.SelectionManager.instance;
            foreach (var name in surplusNames)
            {
                var entry = GetPngObject(name);
                if (entry == null || entry.data == null)
                {
                    continue;
                }
                if (entry.data.rootObject != null && selection.selectedObject == entry.data.rootObject)
                {
                    selection.Select(null);
                }
                sePngManager.RemovePng(entry.data);
            }

            // 消した実体の名前を空けてから XML の定義を作る (同じ名前を取り直せるように)
            RebuildIfChanged();
        }
```

`OnLoad` を置き換える:

```csharp
        public override void OnLoad()
        {
            if (timeline != null)
            {
                // 余りを消すのは Undo/Redo の再構築だけ。新規作成・ファイル読込ではシーンの PNG を残す
                Setup(timeline.pngObjects, timelineManager.isRestoringHistory);
            }
        }
```

`Reset` に `_requestedGroups.Clear();` を足す。

`Setup` の呼び出し元が `OnLoad` だけであることを `rg -n "\.Setup\(" source/COM3D2.SceneEditor.Plugin` で確認し、他にあれば `removeSurplus: false` を渡す。

- [ ] **Step 5: TimelineManager に再構築中の印を付ける**

`TimelineManager.cs` の `UpdateTimeline` の直前へ:

```csharp
        /// <summary>
        /// Undo/Redo の全再構築 (UpdateTimeline) で mte.OnLoad を回している最中か。
        /// 読み込み・新規作成と違い、履歴の XML に無い実体 (PNG) を消す判断に使う
        /// </summary>
        public bool isRestoringHistory { get; private set; }
```

`UpdateTimeline` 内の `mte.OnLoad();` を置き換える:

```csharp
            isRestoringHistory = true;
            try
            {
                mte.OnLoad();
            }
            finally
            {
                isRestoringHistory = false;
            }
```

- [ ] **Step 6: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 7: 実機確認 (Review Focus 1 の追加側・3・5)**

通常シーンでタイムラインを新規作成し、PNG配置レイヤーを追加する。

1. 同じ画像を 3 枚置き、それぞれ別の位置で 0F にキーを打つ。真ん中 (`a (1)`) を配置済みタブの `x` で消して保存し、タイムラインを読み直す。devbridge で名前を見る:

```csharp
return string.Join(",", COM3D2.MotionTimelineEditor.Plugin.PngObjectTimelineManager.instance.pngObjectNames.ToArray());
```

`a,a (2)` であること (修正前は `a,a (1)`)。`a (2)` が保存前の位置に出ること。
2. PNG を 1 枚追加 → Undo で追加した PNG が消え、配置済みタブと Inspector に残らないこと。Redo で戻ること
3. シーンに PNG を置いた状態でタイムラインを新規作成しても PNG が消えないこと。別のタイムラインを読み込んでも、シーンの PNG が消えないこと (従来どおり)

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs source/COM3D2.SceneEditor.Plugin.Tests/PngObjectGroupTests.cs
git commit -m "fix(png): 読込で PNG の番号を保ち、Undo で余った実体を消す"
```

---

### Task 4: PNG をタイムラインごと複製する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs` (`AddPng` の下)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs:67`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:316` 付近
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PngPlacementTimelineLayer.cs` (`OnPngObjectRemoved` の下)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:691` 付近 (`OnCopyModel` の下)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/PngDuplicator.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Modify: `source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs`、`source/COM3D2.SceneEditor.Plugin/PngPlacementWindow.cs`

**Interfaces:**
- Consumes (Task 3): `_requestedGroups`、`GetUsedGroups(string)`、`AssignGroup(ICollection<int>, int)`
- Produces:
  - `PngPlacementSnapshot.CaptureObject(PngObjectData) : ScenePresetPngObject` / `PngPlacementSnapshot.ApplyObject(PngPlacementManager, PngObjectData, ScenePresetPngObject)`
  - `PngPlacementManager.DuplicatePng(PngObjectData source) : PngObjectData` (失敗は null)
  - `ITimelineLayer.OnCopyPngObject(string sourceName, string newName)` / `TimelineData.OnCopyPngObject(string, string)`
  - `PngObjectTimelineManager.RegisterDuplicate(SE.PngObjectData source, SE.PngObjectData created) : string` (新しいタイムライン名。登録できなければ null)
  - `PngDuplicator.Duplicate(PngObjectData source) : PngObjectData`

このタスクの処理は Unity の実体とタイムラインの両方に触るためテストで固めにくい。番号の決め方は Task 3 の `AssignGroup` のテストで、動作は Step 9 の実機確認で見る。

- [ ] **Step 1: 1 枚分の記録・適用を切り出す**

`PngPlacementSnapshot.cs` の `CaptureState` のループ本体を切り出す:

```csharp
        public static ScenePresetPngPlacement CaptureState()
        {
            var state = new ScenePresetPngPlacement();
            foreach (var data in PngPlacementManager.instance.pngObjects)
            {
                if (data.rootObject == null)
                {
                    continue;
                }
                state.objects.Add(CaptureObject(data));
            }
            return state;
        }

        /// <summary>配置物 1 枚分の記録。複製 (PngPlacementManager.DuplicatePng) も同じ項目を写す</summary>
        public static ScenePresetPngObject CaptureObject(PngObjectData data)
        {
            return new ScenePresetPngObject
            {
                source = data.source,
                relativePath = data.relativePath,
                position = data.transform.position,
                rotation = data.transform.eulerAngles,
                scale = data.transform.localScale,
                billboard = data.billboard,
                brightness = data.brightness,
                saturation = data.saturation,
                color = data.color,
                renderQueue = data.renderQueue,
                visible = data.visible,
                displayType = data.displayType,
                blendMode = data.blendMode,
                decalFadeAngle = data.decalFadeAngle,
                decalProjectOnMaids = data.decalProjectOnMaids,
            };
        }

        /// <summary>配置物 1 枚へ記録を適用する。画像 (source / relativePath) は呼び出し側で合わせておくこと</summary>
        public static void ApplyObject(PngPlacementManager manager, PngObjectData data, ScenePresetPngObject objState)
        {
            data.transform.position = objState.position;
            data.transform.eulerAngles = objState.rotation;
            data.transform.localScale = objState.scale;
            manager.SetBillboard(data, objState.billboard);
            manager.SetColor(data, objState.color, objState.brightness);
            manager.SetSaturation(data, objState.saturation);
            manager.SetRenderQueue(data, objState.renderQueue);
            manager.SetVisible(data, objState.visible);
            manager.SetDisplayType(data, objState.displayType);
            manager.SetBlendMode(data, objState.blendMode);
            manager.SetDecalFadeAngle(data, objState.decalFadeAngle);
            manager.SetDecalProjectOnMaids(data, objState.decalProjectOnMaids);
        }
```

`ApplyState` の `data.transform.position = objState.position;` から `manager.SetDecalProjectOnMaids(...)` までの 12 行を `ApplyObject(manager, data, objState);` に置き換える (`index++;` は残す)。

- [ ] **Step 2: DuplicatePng を足す**

`PngPlacementManager.cs` の `AddPng` の下へ:

```csharp
        /// <summary>
        /// 配置物を 1 枚複製して一覧の末尾へ置く。写す項目はシーンプリセットと同じ
        /// (PngPlacementSnapshot の 1 枚分の記録・適用を使うので、項目を足しても複製だけ漏れない)。
        /// 末尾に置くのは、タイムライン未読込の Undo (並び順で照合する) で後ろの実体を作り直させないため。
        /// 画像が読めなければ null
        /// </summary>
        public PngObjectData DuplicatePng(PngObjectData source)
        {
            if (source == null || source.rootObject == null)
            {
                return null;
            }

            var state = PngPlacementSnapshot.CaptureObject(source);
            var data = AddPng(state.source, state.relativePath);
            if (data == null)
            {
                return null;
            }

            PngPlacementSnapshot.ApplyObject(this, data, state);
            return data;
        }
```

- [ ] **Step 3: レイヤーへ PNG のキーの複製を通す**

`ITimelineLayer.cs` の `void OnCopyLight(...)` の下へ:

```csharp
        void OnCopyPngObject(string sourceName, string newName);
```

`TimelineLayerBase.cs` の `OnCopyLight` の下へ:

```csharp
        public virtual void OnCopyPngObject(string sourceName, string newName)
        {
            // do nothing
        }
```

`TimelineData.cs` の `OnCopyModel` の下へ:

```csharp
        public void OnCopyPngObject(string sourceName, string newName)
        {
            foreach (var layer in layers)
            {
                layer.OnCopyPngObject(sourceName, newName);
            }
        }
```

`PngPlacementTimelineLayer.cs` の `OnPngObjectRemoved` の下へ:

```csharp
        /// <summary>
        /// 複製元のキーを全フレーム分、新しい名前へ写す。
        /// 対応表へ載せる (onObjectAdded → 0F の自動登録) より前に呼ばれるので、0F も元のキーの値になる
        /// </summary>
        public override void OnCopyPngObject(string sourceName, string newName)
        {
            foreach (var keyFrame in keyFrames)
            {
                var sourceBone = keyFrame.GetBone(sourceName);
                if (sourceBone == null)
                {
                    continue;
                }

                var newBone = keyFrame.GetOrCreateBone(sourceBone.transform.type, newName);
                newBone.transform.FromTransformData(sourceBone.transform);
            }
        }
```

- [ ] **Step 4: 複製の登録を PngObjectTimelineManager に足す**

`PngObjectTimelineManager.cs` の `GetUsedGroups` の下へ:

```csharp
        /// <summary>
        /// 複製した実体を、元のキーを写した名前で対応表へ載せる。
        /// 番号を予約してキーを写してから RebuildIfChanged を回すので、初期フレームの自動登録は
        /// 写した 0F のキーを上書きしない。履歴は「PNGの複製」1 件。
        /// 元の実体が対応表に無い (タイムライン未読込など) ときは何もせず null を返す
        /// </summary>
        public string RegisterDuplicate(SE.PngObjectData source, SE.PngObjectData created)
        {
            TimelinePngObjectEntry sourceEntry;
            if (timeline == null || source == null || created == null
                || !_dataMap.TryGetValue(source, out sourceEntry)
                || _dataMap.ContainsKey(created))
            {
                return null;
            }

            var group = AssignGroup(GetUsedGroups(sourceEntry.imageName), -1);
            _requestedGroups[created] = group;
            var newName = sourceEntry.imageName + PluginUtils.GetGroupSuffix(group);

            timeline.OnCopyPngObject(sourceEntry.name, newName);
            RebuildIfChanged();

            // 対応表へ載せたときの「初期フレーム登録」より後に呼び、履歴の文言をこちらにする
            timelineManager.RequestHistory("PNGの複製: " + newName);
            return newName;
        }
```

- [ ] **Step 5: 複製ボタンの共通処理を作る**

`Manager/PngDuplicator.cs`:

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置の複製ボタンの処理。Inspector と PNG配置ウィンドウの「配置済み」タブから呼ぶ。
    /// タイムライン読込中は元のキーを新しい名前へ写してから対応表へ載せ、履歴はタイムラインへ 1 件積む。
    /// 未読込ならシーン履歴へ 1 件積む
    /// </summary>
    public static class PngDuplicator
    {
        public static PngObjectData Duplicate(PngObjectData source)
        {
            if (source == null || source.rootObject == null)
            {
                return null;
            }

            var historyManager = HistoryManager.instance;
            var pngTimelineManager = MTEP.PngObjectTimelineManager.instance;
            var isTimelineMode = historyManager.isTimelineMode;

            if (isTimelineMode)
            {
                // シーン履歴の BeforeEdit は確定時に自動キーフレーム登録を誘発し、
                // 「PNGの複製」と別の履歴が積まれうるので通さない (モデルの複製ボタンと同じ)
                AutoEditMode.Enter();
                // 元の実体が対応表に載っていないと写し元のキーを引けない
                pngTimelineManager.RebuildIfChanged();
            }
            else
            {
                historyManager.BeforeEdit(null, HistoryScope.PngPlacement, "PNGの複製: " + source.name);
            }

            var created = PngPlacementManager.instance.DuplicatePng(source);
            if (created == null)
            {
                return null;
            }

            if (isTimelineMode)
            {
                pngTimelineManager.RegisterDuplicate(source, created);
            }

            // 元と同じ位置に重なるので、すぐギズモで動かせるよう選択する
            SelectionManager.instance.Select(created.rootObject, true, true);
            return created;
        }
    }
}
```

csproj の `<Compile Include="Manager\PngPlacementManager.cs" />` の直前に `<Compile Include="Manager\PngDuplicator.cs" />` を足す。

- [ ] **Step 6: Inspector に複製ボタンを置く**

`PngPlacementInspector.cs` の定数に足す:

```csharp
        private const float DUPLICATE_BUTTON_WIDTH = 45f;
```

「表示 / ビルボード」の `view.BeginHorizontal()` ブロックで、ビルボードの `if (!isDecal) { ... }` の後ろ (`view.EndLayout()` の前) へ:

```csharp
                if (view.DrawButton("複製", DUPLICATE_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    PngDuplicator.Duplicate(data);
                }
```

- [ ] **Step 7: 配置済みタブに操作行を置く**

`PngPlacementWindow.cs` の `DrawPlacedTiles` で、空のときの `return` の後ろ (配置済みが 0 枚なら複製対象も無いので、操作行ごと出さなくてよい。選択が無いときだけ無効表示にする)・`_view.DrawTileView(_placedRoot, ...)` の前へ `DrawPlacedActionRow();` を足し、メソッドを追加する:

```csharp
        /// <summary>
        /// 選択中の配置物への操作行。タイルの角に置ける操作は削除 (x) だけで、
        /// タイルの描画は共有サブモジュール (MTEUtils の GUIView) にあるため、複製はここに置く
        /// </summary>
        private void DrawPlacedActionRow()
        {
            var selected = pngManager.FindByRoot(SelectionManager.instance.selectedObject);

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("複製", FOLDER_BUTTON_WIDTH, ROW_HEIGHT, selected != null))
                {
                    PngDuplicator.Duplicate(selected);
                }
                _view.DrawLabel(selected != null ? selected.name : "複製する PNG を選択してください",
                    -1, ROW_HEIGHT, textColor: Color.gray);
            }
            _view.EndLayout();
        }
```

`DrawPlacedTiles` の summary を「クリックで選択し、パラメータの編集は Inspector で行う。x ボタンで削除し、上の操作行で選択中を複製する」に直す。

- [ ] **Step 8: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 9: 実機確認 (Review Focus 1・2・6)**

1. タイムライン未読込: PNG を置き、色・彩度・表示タイプ (デカール)・ブレンド・表示順を既定から変える。Inspector の「複製」で、同じ見た目の PNG が同じ位置に増え、選択が複製先へ移ること。履歴ウィンドウに「PNGの複製」が 1 件積まれ、Undo で複製先だけが消え、元の PNG の見た目・選択中のギズモが変わらないこと。配置済みタブの「複製」(選択中が無いと押せない) でも同じ
2. タイムライン読込中: PNG配置レイヤーで PNG `a` に 0F と 60F で位置と色のキーを打つ。30F に移動して「複製」を押す。devbridge で名前とキーを確認する:

```csharp
var tm = COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance;
var layer = tm.timeline.layers.First(l => l.layerName == "PngPlacementTimelineLayer") as COM3D2.MotionTimelineEditor.Plugin.TimelineLayerBase;
return string.Join("\n", layer.keyFrames.Select(f => f.frameNo + ": " + string.Join(",", f.boneNames.ToArray())).ToArray());
```

0F と 60F の両方に `a (1)` のキーがあり、再生すると複製先が元と同じ動きをすること (0F が 30F の値になっていないこと)。履歴の最新が「PNGの複製: a (1)」1 件であること。Undo で `a (1)` が消え、Redo で `a (1)` が同じキーで戻ること。
3. PNG配置レイヤーを追加していないタイムラインで複製しても例外が出ないこと (`tail_log` を確認)

`keyFrames` / `boneNames` が public でなければ、同等の情報を `eval_csharp` でリフレクション取得する (memory「devbridge からプラグイン内部を触る」)。

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/PngPlacementSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/PngPlacementManager.cs source/COM3D2.SceneEditor.Plugin/Manager/PngDuplicator.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/PngPlacementTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/PngObjectTimelineManager.cs source/COM3D2.SceneEditor.Plugin/PngPlacementInspector.cs source/COM3D2.SceneEditor.Plugin/PngPlacementWindow.cs
git commit -m "feat(png): PNG 配置をタイムラインのキーごと複製する"
```

---

### Task 5: ドキュメント

**Files:**
- Modify: `docs-site/timeline/layers-background.md` (「PNG配置」節)
- Modify: `docs-site/timeline/layers-model.md:28`
- Modify: `docs-site/guide/staging.md` (「背景 / ライト / サウンド / PNG 配置」節)

- [ ] **Step 1: PNG配置レイヤーの説明を足す**

`layers-background.md` の「PNG配置」節の箇条書きに足す:

```markdown
- Inspector または PNG配置ウィンドウの `配置済み` タブの `複製` で、選択中の PNG を複製できます。複製元のキーがすべて複製先へコピーされます（名前は `画像名 (番号)`）
```

- [ ] **Step 2: モデルの複製の説明を直す**

`layers-model.md` 28 行目の「モデルを複製すると、複製元のキーが複製先へコピーされます（モデルボーン・シェイプ・マテリアルも同様）」の後ろへ「マテリアルのシェーダー変更も複製先へ写ります」を足す。

- [ ] **Step 3: PNG 配置ウィンドウの説明を足す**

`staging.md` の PNG 配置の説明 (97 行目付近の画像フォルダの段落) の後ろへ:

```markdown
配置した PNG は `配置済み` タブに並びます。タイルのクリックで選択、`x` で削除、上の `複製` で選択中の PNG を複製します（Inspector の `複製` も同じ）。複製先は画像・位置・色・表示タイプなどの設定をすべて引き継ぎ、元と同じ位置に置かれます。
```

- [ ] **Step 4: コミット**

```bash
git add docs-site
git commit -m "docs(png): PNG とモデルの複製の説明を追加する"
```

## レビュー却下メモ

- なし (plan-review 前)

## レビュー却下メモ

- なし (指摘 2 件を取り込み: 複製直後に削除したときのシェーダー変更エントリの残りを実機確認して必要なら削除経路で消す、配置済み 0 枚のときに操作行を出さない意図を明記)
