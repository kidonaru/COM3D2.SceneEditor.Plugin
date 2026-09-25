# ModItemExplorer のアタッチ同期 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（プロジェクト規約で subagent-driven-development は使わない。最終レビューは code-review スキル）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ModItemExplorer（MIE）の UI で付けたアタッチを SceneEditor（SE）のモデルキーへ正しく記録し、両者の部位一覧を揃える。

**Architecture:** SE は部位一覧に独自値 19（胸）・20（骨盤）を足した `ModelAttachPoints` を持ち、名前でボーンを引く。MIE はプロバイダ任意メンバ `GetModelAttachBone` でアタッチ中の親ボーンを返し、SE の `ExternalModelHack` がそれを stat へ取り込む。stat の食い違いを `StudioModelManager` がイベントで知らせ、`TimelineWindow` が自動登録する。MIE の UI は付け替え前に `AutoEditModeClient.Enter` で SE の編集モードへ入る。

**Tech Stack:** C# (.NET Framework / Unity Mono、COM3D2 と COM3D25 の 2 構成), xUnit (net48)。2 リポジトリ（`W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin` と `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`）

**Spec:** `docs/superpowers/specs/2026-09-25-mie-attach-sync-design.md`

## Global Constraints

- コードのコメント・ログは日本語
- 両構成（`GameVersion=COM3D2` / `COM3D25`）でビルドが通ること。SE のテストは COM3D2 → COM3D25 の順にビルドしてから `dotnet test`（テストプロジェクトは MSBuild の出力 DLL を参照するため、`dotnet test` だけでは本体の変更が反映されない）
- ビルドは `debug.bat` ではなく MSBuild 直叩き（ゲームへの意図しない反映を避ける）。Git Bash では `export MSYS2_ARG_CONV_EXCL="*"` が必要
- テストで Unity ネイティブ呼び出し（`Quaternion.Euler`、`new GameObject` 等）をしない
- プラグイン間連携はリフレクション経由のみ（SE と MIE は互いをコンパイル時参照しない）
- 共有サブモジュール MTEUtils のソースは変更しない（MIE 側は既存コミットへ進めるだけ）
- `deploy.bat` / `deploy.ps1` は実行しない。push もしない
- 部位の値: ゲームの `PhotoTransTargetObject.AttachPoint` 0〜18、SE 独自 19 = 胸（`Bip01 Spine1a`）、20 = 骨盤（`Bip01 Pelvis`）
- MIE に足す部位: 原点（`Bip01`）・右胸（`Mune_R`）・左胸（`Mune_L`）。「固定」は足さない（ボディごとに名前が変わるため）

SE のビルドとテストのコマンド（以下「SE ビルド」）:
```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

MIE のビルドコマンド（以下「MIE ビルド」。MIE にテストプロジェクトは無い）:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
```

## Review Focus

- タイムラインを読み込んでいない状態で MIE の UI からアタッチする → stat は同期され、例外もキー登録も起きない（`AutoKeyFrameAfterEdit` は timeline が null なら何もしない。実機確認 Task 5 Step 1）
- 胸・骨盤のボーンが無いボディで、胸・骨盤のキーを再生する → 解決できずアタッチなし扱い、例外なし（`GetBone` が null → `GetAttachPointTransform` が null → `ApplyAttach` がアタッチなしへ正規化。Task 1 の実装で担保する。手元のセーブに該当ボディが無ければ実機では確認できないので、Task 5 の記録にその旨を残す）
- 同期が SE 自身の付け替えと食い違い、再生・シークのたびに stat が揺れる / `onModelAttachChanged` が誤発火してキーが増える（実機確認 Task 5 Step 6）
- `GetModelAttachBone` を持たない旧 MIE と組み合わせる → これまでどおり同期しないだけ（Task 2 のバインドテスト）
- SE が MaidCache を持たないメイドや一覧に無いボーンへ MIE が付ける → stat を変えず、例外なし（Task 2 の `TryResolveAttach` が false を返す経路。取り込めない場合キーは不正確なまま残るので、同じボーンにつき 1 度だけ警告ログで知らせる。実機確認 Task 5 Step 7）

---

### Task 1: SE の部位一覧に胸・骨盤を足す

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelAttachPoints.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:588-598`（`GetAttachPointTransform`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataModel.cs`（`attachPoint` の `max`）
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFollowCustomValueDrawer.cs`（`AttachPointItems`）
- Modify: `source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs:38`（部位コンボの `items`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachPointsTests.cs`（新規）、`TransformDataModelTests.cs`、`MaidFollowCustomValueDrawerTests.cs`

**Interfaces:**
- Produces:
  - `ModelAttachPoints.Chest`（`(AttachPoint)19`）、`ModelAttachPoints.Pelvis`（`(AttachPoint)20`）、`ModelAttachPoints.Max`（= `Pelvis`）
  - `List<string> ModelAttachPoints.Names`（添字 = 部位の値。0〜18 は `BoneUtils.AttachPointNames` と同じ）
  - `string ModelAttachPoints.GetExtraBoneName(AttachPoint)`（独自値ならボーン名、それ以外は null）
  - `bool ModelAttachPoints.TryFindAttachPoint(Func<AttachPoint, object> resolve, object bone, out AttachPoint point)`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachPointsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルのアタッチ部位の一覧。値はキーに保存されるため、並びがずれると保存済みのアタッチが別の部位になる
    /// </summary>
    public class ModelAttachPointsTests
    {
        [Fact]
        public void 名前はゲームの部位の後ろに胸と骨盤を足した並び()
        {
            Assert.Equal((int)ModelAttachPoints.Max + 1, ModelAttachPoints.Names.Count);
            Assert.Equal("設定なし", ModelAttachPoints.Names[(int)AttachPoint.Null]);
            Assert.Equal("左足首", ModelAttachPoints.Names[(int)AttachPoint.Foot_L]);
            Assert.Equal("胸", ModelAttachPoints.Names[(int)ModelAttachPoints.Chest]);
            Assert.Equal("骨盤", ModelAttachPoints.Names[(int)ModelAttachPoints.Pelvis]);
        }

        [Theory]
        [InlineData(19, "Bip01 Spine1a")]
        [InlineData(20, "Bip01 Pelvis")]
        [InlineData((int)AttachPoint.Hand_R, null)]
        [InlineData((int)AttachPoint.Null, null)]
        public void 独自値だけボーン名で引く(int point, string expected)
        {
            Assert.Equal(expected, ModelAttachPoints.GetExtraBoneName((AttachPoint)point));
        }

        [Fact]
        public void 親ボーンに一致する部位を探す()
        {
            var hand = new object();
            var pelvis = new object();
            var bones = new Dictionary<AttachPoint, object>
            {
                { AttachPoint.Hand_R, hand },
                { ModelAttachPoints.Pelvis, pelvis },
            };
            Func<AttachPoint, object> resolve = p =>
            {
                object bone;
                return bones.TryGetValue(p, out bone) ? bone : null;
            };

            AttachPoint found;
            Assert.True(ModelAttachPoints.TryFindAttachPoint(resolve, pelvis, out found));
            Assert.Equal(ModelAttachPoints.Pelvis, found);
            Assert.True(ModelAttachPoints.TryFindAttachPoint(resolve, hand, out found));
            Assert.Equal(AttachPoint.Hand_R, found);

            Assert.False(ModelAttachPoints.TryFindAttachPoint(resolve, new object(), out found));
            Assert.Equal(AttachPoint.Null, found);
        }
    }
}
```

`TransformDataModelTests.cs` の `アタッチ値は15値の末尾3つで既定はなし_Head_OFF` で、`Assert.Equal((float)AttachPoint.Fix, map["attachPoint"].min);` の次の行に足す:

```csharp
            Assert.Equal((float)ModelAttachPoints.Max, map["attachPoint"].max);
```

`MaidFollowCustomValueDrawerTests.cs` の `部位コンボはNullを除いた並びで値と添字を変換する` の `[InlineData(AttachPoint.Foot_L, (int)AttachPoint.Foot_L - 1)]` の次の行に足す:

```csharp
        [InlineData(ModelAttachPoints.Pelvis, (int)ModelAttachPoints.Pelvis - 1)]
```

- [ ] **Step 2: テストが失敗することを確認**

Run: SE ビルド
Expected: テストプロジェクトのコンパイルエラー（`ModelAttachPoints` 未定義）

- [ ] **Step 3: `ModelAttachPoints` を作る**

`source/COM3D2.SceneEditor.Plugin/Timeline/ModelAttachPoints.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// モデルのアタッチ部位。ゲームの PhotoTransTargetObject.AttachPoint (0〜18) の後ろに、
    /// ModItemExplorer が持つ胸・骨盤を SceneEditor 独自の値として足す。
    /// 値はモデルキーに保存されるので、既存の値は並べ替えず末尾に足すこと
    /// </summary>
    public static class ModelAttachPoints
    {
        public const AttachPoint Chest = (AttachPoint)19;
        public const AttachPoint Pelvis = (AttachPoint)20;
        public const AttachPoint Max = Pelvis;

        /// <summary>独自値のボーン名。IK 管理外のボーンなので名前で引く</summary>
        private static readonly Dictionary<AttachPoint, string> ExtraBoneNames = new Dictionary<AttachPoint, string>
        {
            { Chest, "Bip01 Spine1a" },
            { Pelvis, "Bip01 Pelvis" },
        };

        /// <summary>表示名。添字が部位の値</summary>
        public static readonly List<string> Names = CreateNames();

        private static List<string> CreateNames()
        {
            var names = new List<string>(BoneUtils.AttachPointNames);
            names.Add("胸");
            names.Add("骨盤");
            return names;
        }

        /// <summary>独自値ならボーン名、ゲームの値なら null</summary>
        public static string GetExtraBoneName(AttachPoint point)
        {
            string boneName;
            return ExtraBoneNames.TryGetValue(point, out boneName) ? boneName : null;
        }

        /// <summary>
        /// 親ボーンに一致する部位を探す。resolve は部位 → そのメイドでのボーン。
        /// テストで Unity のオブジェクトを作れないため object で受ける
        /// </summary>
        public static bool TryFindAttachPoint(Func<AttachPoint, object> resolve, object bone, out AttachPoint point)
        {
            for (var i = (int)AttachPoint.Fix; i <= (int)Max; i++)
            {
                var candidate = (AttachPoint)i;
                var resolved = resolve(candidate);
                if (resolved != null && Equals(resolved, bone))
                {
                    point = candidate;
                    return true;
                }
            }

            point = AttachPoint.Null;
            return false;
        }
    }
}
```

`BoneUtils` は `COM3D2.MotionTimelineEditor` 名前空間（MTEUtils）。同じファイルの namespace `COM3D2.MotionTimelineEditor.Plugin` の内側からは親名前空間として見えるので using は不要。見えなければ `using COM3D2.MotionTimelineEditor;` を足す。

- [ ] **Step 4: 使う側を拡張後の一覧へ切り替える**

`MaidCache.GetAttachPointTransform` を:

```csharp
        public Transform GetAttachPointTransform(AttachPoint point)
        {
            if (point == AttachPoint.Null)
            {
                return null;
            }

            var extraBoneName = ModelAttachPoints.GetExtraBoneName(point);
            if (extraBoneName != null)
            {
                return maid != null && maid.body0 != null ? maid.body0.GetBone(extraBoneName) : null;
            }

            if (ikManager == null)
            {
                return null;
            }

            var boneType = BoneUtils.GetBoneType(point);
            var bone = ikManager.GetBone(boneType);
            return bone != null ? bone.transform : null;
        }
```

`TransformDataModel.cs` の `attachPoint` の `CustomValueInfo` で `max = (float)AttachPoint.Foot_L,` を:

```csharp
                    max = (float)ModelAttachPoints.Max,
```

`MaidFollowCustomValueDrawer.cs` の `AttachPointItems` を:

```csharp
        private static readonly List<string> AttachPointItems = ModelAttachPoints.Names.GetRange(
            1, ModelAttachPoints.Names.Count - 1);
```

（`ModelAttachPoints` は `MTEP.` 修飾が要る。ファイル冒頭の `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` に合わせて `MTEP.ModelAttachPoints.Names` と書く）

`ModelManageRowDrawer.cs:38` の `items = BoneUtils.AttachPointNames,` を:

```csharp
            items = MTEP.ModelAttachPoints.Names,
```

（添字 = 部位の値のまま。`_attachPointComboBox.currentIndex = (int)model.attachPoint` と `(AttachPoint)index` は変えない）

- [ ] **Step 5: テストが通ることを確認**

Run: SE ビルド
Expected: 両構成ビルド成功、全件 PASS

- [ ] **Step 6: コミット（SE リポジトリ）**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Timeline/ModelAttachPoints.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataModel.cs source/COM3D2.SceneEditor.Plugin/MaidFollowCustomValueDrawer.cs source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelAttachPointsTests.cs source/COM3D2.SceneEditor.Plugin.Tests/TransformDataModelTests.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidFollowCustomValueDrawerTests.cs
git commit -m "feat(timeline): モデルのアタッチ部位に胸と骨盤を追加"
```

---

### Task 2: SE がプロバイダのアタッチを取り込み、自動登録する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs`（`ModelPlacerProvider.getModelAttachBone` と任意メンバのバインド）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs`（`SyncAttachFromProvider` / `TryResolveAttach`、`GetOrCreateStat` から呼ぶ）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs`（`onModelAttachChanged`、`LateUpdate`）
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs`（コンストラクタで購読）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs`

**Interfaces:**
- Consumes: `ModelAttachPoints.TryFindAttachPoint`（Task 1）、`MaidCache.GetAttachPointTransform`（Task 1 で独自値対応済み）、`TimelineWindow.AutoKeyFrameAfterEdit(Type, int)`（既存）
- Produces:
  - `Func<GameObject, Transform> ModelPlacerProvider.getModelAttachBone`（任意。未実装なら null）
  - プロバイダ規約の任意メンバ `Transform GetModelAttachBone(GameObject)`（Task 3 の MIE が実装する）
  - `static event UnityAction<StudioModelStat> StudioModelManager.onModelAttachChanged`

Unity のシーンが要る同期・イベントは単体テストを書かず、Task 5 の実機確認で担保する。

- [ ] **Step 1: 失敗するテストを書く**

`ModelPlacerProviderTests.cs` の `FullDummyProvider` の `EndBatch` の次の行に足す:

```csharp
        public static Transform GetModelAttachBone(GameObject obj) => null;
```

`必須と任意メンバを備えた型をバインドできる` の `Assert.NotNull(provider.endBatch);` の次の行に足す:

```csharp
            Assert.NotNull(provider.getModelAttachBone);
```

`任意メンバが無くてもバインドできる` の `Assert.Null(provider.endBatch);` の次の行に足す:

```csharp
            // 旧版のプロバイダはアタッチを返さない。SE は同期しないだけで、これまでどおり動く
            Assert.Null(provider.getModelAttachBone);
```

- [ ] **Step 2: テストが失敗することを確認**

Run: SE ビルド
Expected: テストプロジェクトのコンパイルエラー（`getModelAttachBone` 未定義）

- [ ] **Step 3: 任意メンバをバインドする**

`ModelPlacerProvider` の `public Action endBatch;` の次に:

```csharp

        /// <summary>
        /// アタッチ中の親ボーン。未アタッチ・不明なら null。
        /// プロバイダ側の UI で付け替えたアタッチを SE のキーへ取り込むのに使う
        /// </summary>
        public Func<GameObject, Transform> getModelAttachBone;
```

`TryBind` の `BeginBatch` / `EndBatch` のバインドの後（`return true;` の前）に:

```csharp

            var getAttachBone = type.GetMethod("GetModelAttachBone", flags, null, new[] { typeof(GameObject) }, null);
            if (getAttachBone != null && getAttachBone.ReturnType == typeof(Transform))
            {
                provider.getModelAttachBone = (Func<GameObject, Transform>)Delegate.CreateDelegate(
                    typeof(Func<GameObject, Transform>), getAttachBone);
            }
```

- [ ] **Step 4: テストが通ることを確認**

Run: SE ビルド
Expected: 全件 PASS

- [ ] **Step 5: `ExternalModelHack` で stat へ取り込む**

`GetOrCreateStat` のキャッシュ経路を:

```csharp
            if (_statMap.TryGetValue(obj, out var cached))
            {
                if (cached.info != null && cached.info.fileName == fileName)
                {
                    cached.visible = obj.activeSelf;
                    SyncAttachFromProvider(cached, obj);
                    return cached;
                }
                _statMap.Remove(obj);
            }
```

新規経路の `stat.SetGroup(StudioModelStat.UnassignedGroup);` の次の行に:

```csharp
            SyncAttachFromProvider(stat, obj);
```

`SafeGetFileName` の上に:

```csharp
        /// <summary>
        /// プロバイダ側の UI で付け替えたアタッチを stat へ取り込む。
        /// SE 自身の付け替えは stat を書いてからプロバイダを呼ぶので、ここで差分にはならない
        /// </summary>
        /// <summary>取り込めなかったアタッチ先の控え (警告の重複を避ける)</summary>
        private readonly Dictionary<GameObject, Transform> _unresolvedAttachBones = new Dictionary<GameObject, Transform>();

        private void SyncAttachFromProvider(StudioModelStat stat, GameObject obj)
        {
            if (_provider.getModelAttachBone == null)
            {
                return;
            }

            Transform bone;
            try
            {
                bone = _provider.getModelAttachBone(obj);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return;
            }

            AttachPoint point;
            int slotNo;
            if (!TryResolveAttach(bone, out point, out slotNo))
            {
                // 30 フレームごとに呼ばれるので、同じボーンについては 1 度だけ知らせる
                Transform warnedBone;
                if (!_unresolvedAttachBones.TryGetValue(obj, out warnedBone) || warnedBone != bone)
                {
                    _unresolvedAttachBones[obj] = bone;
                    MTEUtils.LogWarning(
                        "アタッチ先を SceneEditor の部位として表せないため、キーへ取り込みません: {0} → {1}",
                        stat.displayName, bone.name);
                }
                return;
            }

            _unresolvedAttachBones.Remove(obj);
            stat.attachPoint = point;
            stat.attachMaidSlotNo = slotNo;
        }

        /// <summary>
        /// 親ボーンを SE の部位とメイドのスロットへ戻す。未アタッチは true (Null / -1)。
        /// SE がキャッシュを持たないメイドや、部位一覧に無いボーンは false
        /// </summary>
        private static bool TryResolveAttach(Transform bone, out AttachPoint point, out int slotNo)
        {
            point = AttachPoint.Null;
            slotNo = -1;
            if (bone == null)
            {
                return true;
            }

            var maid = bone.GetComponentInParent<Maid>();
            var maidCache = maid != null ? maidManager.GetMaidCache(maid) : null;
            if (maidCache == null)
            {
                return false;
            }

            if (!ModelAttachPoints.TryFindAttachPoint(p => maidCache.GetAttachPointTransform(p), bone, out point))
            {
                return false;
            }

            slotNo = maidCache.slotNo;
            return true;
        }
```

- [ ] **Step 6: 食い違いをイベントで知らせる**

`StudioModelManager` の `public static event UnityAction<StudioModelStat> onModelUpdated;` の次に:

```csharp

        /// <summary>
        /// プロバイダ側 (ModItemExplorer の UI など) でアタッチが変わった。
        /// SE 自身の付け替えは両方の stat を同時に書くため、このイベントは発火しない
        /// </summary>
        public static event UnityAction<StudioModelStat> onModelAttachChanged;
```

`LateUpdate` の `var updatedModels = new List<StudioModelStat>();` の次に:

```csharp
            var attachChangedModels = new List<StudioModelStat>();
```

アタッチ・表示の差分を見る分岐を:

```csharp
                if (cachedModel.attachPoint != model.attachPoint ||
                    cachedModel.attachMaidSlotNo != model.attachMaidSlotNo ||
                    cachedModel.visible != model.visible)
                {
                    if (cachedModel.attachPoint != model.attachPoint ||
                        cachedModel.attachMaidSlotNo != model.attachMaidSlotNo)
                    {
                        attachChangedModels.Add(cachedModel);
                    }

                    cachedModel.FromModel(model);
                    updatedModels.Add(cachedModel);
                    continue;
                }
```

末尾の `onModelUpdated` のループの後に:

```csharp

            foreach (var model in attachChangedModels)
            {
                onModelAttachChanged?.Invoke(model);
            }
```

`StudioModelStat.FromModel` はアタッチを写す（`StudioModelStat.cs:170-171` で確認済み）。

- [ ] **Step 7: `TimelineWindow` で自動登録する**

コンストラクタの `HistoryManager.instance.onEditCommitted += OnEditCommitted;` の次の行に:

```csharp
            MTEP.StudioModelManager.onModelAttachChanged += OnModelAttachChanged;
```

`AutoKeyFrameAfterEdit` の下に:

```csharp
        private static int _lastAttachAutoKeyFrame = -1;

        /// <summary>
        /// プロバイダ (ModItemExplorer) の UI で付け替えたアタッチも、SE のコンボと同じく自動登録に載せる。
        /// 登録は全モデルの差分をまとめて取るので、同じフレームで複数モデルが変わっても 1 回だけ呼ぶ
        /// </summary>
        private static void OnModelAttachChanged(MTEP.StudioModelStat model)
        {
            if (_lastAttachAutoKeyFrame == Time.frameCount)
            {
                return;
            }
            _lastAttachAutoKeyFrame = Time.frameCount;

            AutoKeyFrameAfterEdit(typeof(MTEP.ModelTimelineLayer), slotNo: 0);
        }
```

- [ ] **Step 8: 両構成ビルドと全テスト**

Run: SE ビルド
Expected: 両構成ビルド成功、全件 PASS

- [ ] **Step 9: コミット（SE リポジトリ）**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/ModelPlacerProviderRegistry.cs source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioModelManager.cs source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs source/COM3D2.SceneEditor.Plugin.Tests/ModelPlacerProviderTests.cs
git commit -m "feat(timeline): プロバイダ側で付け替えたモデルのアタッチをキーへ取り込む"
```

---

### Task 3: MIE がアタッチを返し、UI から SE の編集モードへ入る

**Files（MIE リポジトリ `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`）:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils`（サブモジュールを `2da6959` へ進める）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj`（`MTEUtils\AutoEditModeClient.cs` の Compile 項目。csproj は MTEUtils のファイルを 1 件ずつ列挙しているため、サブモジュールを進めるだけではコンパイル対象に入らない）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs:176-194`（`AttachPoints`）と `Attach` の近く（`AttachFromUI`）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs`（`GetModelAttachBone`）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs:113-114`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs:572-573`

**Interfaces:**
- Consumes: プロバイダ任意メンバ `Transform GetModelAttachBone(GameObject)`（Task 2 がバインドする）、`AutoEditModeClient.Enter(string layerName)`（MTEUtils `7a1f307` で追加）、SE のレイヤークラス名 `"ModelTimelineLayer"`
- Produces: `SelfModelPlacer.AttachFromUI(StudioModelStatWrapper model, Maid maid, AttachPoint point)`

MIE にテストプロジェクトは無い。ビルドと Task 5 の実機確認で担保する。

- [ ] **Step 1: MTEUtils サブモジュールを進める**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git -C source/COM3D2.ModItemExplorer.Plugin/MTEUtils fetch origin
git -C source/COM3D2.ModItemExplorer.Plugin/MTEUtils checkout 2da6959
git -C source/COM3D2.ModItemExplorer.Plugin/MTEUtils log --oneline -1
```
Expected: `2da6959 feat(gui): スライダー行のラベルを常時ドラッグ可能にし < > ボタンを廃止`。`AutoEditModeClient.cs` が現れる（`3a6fdeb..2da6959` の差分は `A AutoEditModeClient.cs` / `M GUIView.cs` / `M HistoryClient.cs`）

- [ ] **Step 2: csproj に新規ファイルを足し、MIE ビルドで既存コードが新しい MTEUtils で通るか確認**

`COM3D2.ModItemExplorer.Plugin.csproj` の `<Compile Include="MTEUtils\DockingClient.cs" />` の直前に:

```xml
    <Compile Include="MTEUtils\AutoEditModeClient.cs" />
```


Run: MIE ビルド
Expected: 両構成ビルド成功。失敗したら、MTEUtils の 3 コミット（`7a1f307` / `05c2976` / `2da6959`）で変わった API に MIE を合わせる（修正内容は Ruling に記録）

- [ ] **Step 3: サブモジュール更新をコミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj
git commit -m "chore: MTEUtils を更新"
```

- [ ] **Step 4: 部位一覧に原点・右胸・左胸を足す**

`SelfModelPlacer.AttachPoints` を:

```csharp
        public static readonly List<AttachPoint> AttachPoints = new List<AttachPoint>
        {
            new AttachPoint { displayName = "なし", boneName = null },
            new AttachPoint { displayName = "原点", boneName = "Bip01" },
            new AttachPoint { displayName = "頭", boneName = "Bip01 Head" },
            new AttachPoint { displayName = "首", boneName = "Bip01 Neck" },
            new AttachPoint { displayName = "胸", boneName = "Bip01 Spine1a" },
            new AttachPoint { displayName = "右胸", boneName = "Mune_R" },
            new AttachPoint { displayName = "左胸", boneName = "Mune_L" },
            new AttachPoint { displayName = "骨盤", boneName = "Bip01 Pelvis" },
            new AttachPoint { displayName = "左肩", boneName = "Bip01 L UpperArm" },
            new AttachPoint { displayName = "右肩", boneName = "Bip01 R UpperArm" },
            new AttachPoint { displayName = "左肘", boneName = "Bip01 L Forearm" },
            new AttachPoint { displayName = "右肘", boneName = "Bip01 R Forearm" },
            new AttachPoint { displayName = "左手", boneName = "Bip01 L Hand" },
            new AttachPoint { displayName = "右手", boneName = "Bip01 R Hand" },
            new AttachPoint { displayName = "左腿", boneName = "Bip01 L Thigh" },
            new AttachPoint { displayName = "右腿", boneName = "Bip01 R Thigh" },
            new AttachPoint { displayName = "左膝", boneName = "Bip01 L Calf" },
            new AttachPoint { displayName = "右膝", boneName = "Bip01 R Calf" },
            new AttachPoint { displayName = "左足", boneName = "Bip01 L Foot" },
            new AttachPoint { displayName = "右足", boneName = "Bip01 R Foot" },
        };
```

プリセットと履歴はアタッチをボーン名で持つ（`attachBoneName`、`GetAttachPointIndex` もボーン名で引く）ので、並びを変えても保存済みデータには影響しない。この前提を `grep -n "attachBoneName\|GetAttachPointIndex\|AttachPoints\[" -r source/COM3D2.ModItemExplorer.Plugin --include=*.cs` で確かめ、添字で保存している箇所があれば末尾へ足す配置に切り替えて Ruling に記録する。

- [ ] **Step 5: UI 用の付け替えメソッドを足す**

`SelfModelPlacer.Attach` の直前に:

```csharp
        /// <summary>SceneEditor 側のモデルのタイムラインレイヤー名 (AutoEditModeClient へ渡す)</summary>
        private const string ModelTimelineLayerName = "ModelTimelineLayer";

        /// <summary>
        /// UI からの付け替え。SceneEditor の編集モードへ入ってから付け替える
        /// (編集モード外はタイムラインが毎フレーム再生値を書き戻し、付け替えが元に戻るため)。
        /// SceneEditor から来る付け替え (AttachByBoneName) では呼ばないこと
        /// </summary>
        public void AttachFromUI(StudioModelStatWrapper model, Maid maid, AttachPoint point)
        {
            AutoEditModeClient.Enter(ModelTimelineLayerName);
            Attach(model, maid, point);
        }
```

`ModelInspectorDrawer.cs` と `ModelOperationWindow.cs` の

```csharp
                    placer.Attach(model, modItemManager.currentMaid, point);
```

をそれぞれ

```csharp
                    placer.AttachFromUI(model, modItemManager.currentMaid, point);
```

にする。`AutoEditModeClient` は `COM3D2.MotionTimelineEditor` 名前空間。`SelfModelPlacer.cs` に `using COM3D2.MotionTimelineEditor;` が無ければ足す。

- [ ] **Step 6: アタッチ取得 API を足す**

`ModelPlacerProvider.AttachModel` の下に:

```csharp
        /// <summary>
        /// アタッチ中の親ボーン。未アタッチなら null（任意メンバ）。
        /// SceneEditor が UI での付け替えをモデルのキーへ取り込むのに使う
        /// </summary>
        public static Transform GetModelAttachBone(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model == null || placer.GetAttachState(model) == null)
            {
                return null;
            }
            return obj.transform.parent;
        }
```

- [ ] **Step 7: MIE ビルド**

Run: MIE ビルド
Expected: 両構成ビルド成功

- [ ] **Step 8: コミット（MIE リポジトリ）**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerProvider.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs
git commit -m "feat(model): アタッチ先を SceneEditor のタイムラインへ取り込めるようにする"
```

---

### Task 4: ドキュメント

**Files:**
- Modify: `docs-site/dev/model-placer-guest-guide.md`（SE。任意メンバの表と `AttachModel` の `boneName` の節）
- Modify: `docs-site/timeline/layers-model.md`（SE。アタッチの箇条書き）
- Modify: `W:\COM3D2_5\work\CLAUDE.md`「タイムライン XML の互換方向」（git 管理外、コミット対象外）

- [ ] **Step 1: ゲスト向けガイドに任意メンバを足す**

`model-placer-guest-guide.md` の任意メンバの表（`BeginBatch` / `EndBatch` の行の次）に:

```markdown
| `GetModelAttachBone` | `Transform GetModelAttachBone(GameObject)` | アタッチ中の親ボーン（未アタッチなら null）。実装すると、ゲスト側の UI で付け替えたアタッチが SceneEditor のモデルキーへ取り込まれる。UI での付け替えの前に `AutoEditModeClient.Enter("ModelTimelineLayer")` を呼ぶこと（呼ばないとタイムラインの再生値で元に戻る） |
```

`### AttachModel の boneName` の節の末尾に:

```markdown
SceneEditor が付ける部位は、ゲームの `PhotoTransTargetObject.AttachPoint` の部位に胸（`Bip01 Spine1a`）と骨盤（`Bip01 Pelvis`）を足したものです。`GetModelAttachBone` が返すボーンがこの一覧に無い場合、SceneEditor はそのアタッチをキーへ取り込みません。
```

- [ ] **Step 2: モデルレイヤーの説明を更新**

`layers-model.md` の「アタッチ先はキーごとに持てるので…」の行を:

```markdown
- アタッチ先はキーごとに持てるので、途中で持ち替えられます。Inspector のモデル行か ModItemExplorer のアタッチ欄でアタッチ先を変えてキーを登録するか、キーフレーム詳細で直接変えます（自動登録が ON なら、どちらで変えてもキーに入ります）
- アタッチ部位はゲームの部位に `胸` と `骨盤` を足したものです
```

- [ ] **Step 3: ワークスペース CLAUDE.md**

「タイムライン XML の互換方向」の version 37 の行の次に:

```markdown
- モデルキーのアタッチ部位（index 13）の値 19（胸 `Bip01 Spine1a`）・20（骨盤 `Bip01 Pelvis`）は SE 独自（`ModelAttachPoints`）。ゲームの `PhotoTransTargetObject.AttachPoint` は 18 まで。これより前の SE で読むと `BoneUtils.GetBoneType` の既定値により「固定」へ付く
```

- [ ] **Step 4: コミット（SE リポジトリ、docs-site のみ）**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site/dev/model-placer-guest-guide.md docs-site/timeline/layers-model.md
git commit -m "docs(timeline): ModItemExplorer のアタッチ取り込みと胸・骨盤を反映"
```

---

### Task 5: 実機確認

`com3d25-restart-verify` スキルの手順でゲームを再起動する。SE と MIE の両方の DLL を反映する（`debug.bat com3d25` を両リポジトリで実行し、それぞれ配置先とハッシュが一致することを確認）。タイトル画面（`SceneTitle`）に着いてからセーブをロードする。REPL では Unity 型を完全修飾名で書き、状態を変える eval と読む eval は分ける。

- [ ] **Step 1: タイムライン無しで MIE の UI からアタッチ**

セーブをロードした直後は `TimelineManager.instance.timeline` が null。この状態で `ModelPlacerProvider.CreateModel("Mod", "ac-cuteaudiop_i_.menu", 0, 0L, 5, true)`（MIE、リフレクション）で置き、`AttachFromUI(model, メイド0, 右手)` を呼ぶ。1 秒待ってからプロバイダ側 stat（`ModelHackManager.instance.GetOrDefault("ModItemExplorer").modelList` の該当 stat）を読む
Expected: 例外なし、プロバイダ側 stat が `0/Hand_R`、`tail_log` にエラーが無い。タイムライン無しで `StudioModelManager.LateUpdate` が動くかは未確認なので、マネージャ側 stat は参考として読むだけにする。読んだら `ModelPlacerProvider.DeleteModel` で消す

- [ ] **Step 2: 部位の照合を全件確かめる**

メイド 0 について、`SelfModelPlacer.AttachPoints` の全件（「なし」以外）のボーン名を `maid.body0.GetBone(name)` で引き、`ModelAttachPoints.TryFindAttachPoint(p => maidCache.GetAttachPointTransform(p), bone, out point)` の結果を一覧で出す。可能なら旧ボディと CRC ボディ（`maid.IsCrcBody`）の両方で行う（手元のセーブに片方しか無ければその旨を記録する）
Expected: 全件 true で、表示名に対応する部位（例: 右手 → `Hand_R`、胸 → 19、骨盤 → 20、原点 → `Root`）。false があれば、その部位は MIE から付けてもキーへ取り込まれないので、原因（IK 側のボーン実体の違いなど）を調べて Ruling に記録する

- [ ] **Step 3: 準備**

新規タイムライン → `ac-cuteaudiop_i_.menu` を `StudioModelManager.CreateModel` で配置 → モデルレイヤーをアクティブにする。`SelfModelPlacer` はリフレクションで取る（`SelfModelPlacer.instance`、`FindModelByGameObject`、`AttachPoints`、`AttachFromUI`）。SE の stat は `StudioModelManager.GetModel` で取り直す（配置時の stat とは別インスタンス）

- [ ] **Step 4: MIE の UI 経路 → SE の stat とキー**

自動登録 ON（`ConfigManager.instance.config.isAutoKeyFrame = true`）で 10F にシークし、`AttachFromUI(model, メイド0, 右手)` を呼ぶ。1 秒待ってから（`LateUpdate` の定期実行を待つ）SE の stat と 10F キーを読む
Expected: stat が `0/Hand_R`、10F キーが `0/Hand_R`、キーの位置・回転がボーン基準の値（MIE の Attach はローカルを 0 にリセットするので位置 (0,0,0)・回転 identity）。続けて胸・骨盤でも同じく、stat とキーが `Chest(19)` / `Pelvis(20)` になる。最後に別フレームへシークしてから 10F へ戻し、モデルが同じボーンの同じ位置に出ることを確かめる。検証後に自動登録を元の値へ戻す

- [ ] **Step 5: SE のキーで胸・骨盤へ付け替える**

10F キーを骨盤、20F キーを胸にして 10F・20F へシーク
Expected: 親がそれぞれ `Bip01 Pelvis` / `Bip01 Spine1a`

- [ ] **Step 6: 同期が SE の付け替えと喧嘩しない**

`onModelAttachChanged` に件数を数えるハンドラを足し、SE の履歴件数（`HistoryManager` の `_entries.Count`）を控える。自動登録 OFF で 0F→20F を数回再生・シークし、2 秒待ってから読む。読み終えたらハンドラを外す
Expected: `onModelAttachChanged` の発火 0 件、履歴件数が変わらない、キー数が変わらない

- [ ] **Step 7: 表せない親への付け替え**

MIE の `AttachByBoneName(model, メイド0, "Bip01 R Finger0")`（SE の一覧に無いボーン）を呼んで 1 秒待つ
Expected: 例外なし、SE の stat は直前の値のまま（取り込まない）、`tail_log` に「アタッチ先を SceneEditor の部位として表せないため」の警告が 1 回だけ出る（さらに 2 秒待って増えないこと）

- [ ] **Step 8: MIE の UI の目視**

MTEUtils の更新（`2da6959`）でスライダー行のラベルがドラッグ操作になり `<` `>` ボタンが無くなる。MIE の操作ウィンドウ（または Inspector）のスライダー行を `screenshot` で撮り、崩れていないことを確認する

- [ ] **Step 9: 後始末**

テスト用モデルを削除し、自動登録の設定を元へ戻す。結果（eval の戻り値・`tail_log` 抜粋・両 DLL のハッシュ一致）を記録する

## レビュー却下メモ

- 検出の遅れ（最大 30 フレーム）の間に編集モードを抜けると変更が巻き戻る → `AutoEditModeHost.Enter` 後に LateUpdate の間引きを外す案 — 30 フレーム（約 0.5 秒）以内に編集を確定する操作は現実的でなく、間引きの特例は `StudioModelManager` の更新周期の設計に手を入れることになるため見送り。spec に帰結を明記した
- 胸・骨盤の照合がボーン階層の再帰探索になる（`TBody.GetBone`）ので名前で先に絞る — 30 フレームごと・アタッチ中のモデルだけで実害が小さく、`TryFindAttachPoint` をテスト可能な汎用形のまま保つため見送り
