# メイドアニメレイヤーの編集 UI 委譲 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `MotionTimelineLayer`（レイヤー名「メイドアニメ」）の編集 UI をすべて SceneEditor の個別ウィンドウ（ボーンウィンドウ / Inspector / IK ウィンドウ / 指ウィンドウ）へ委譲し、レイヤー編集ウィンドウ側は案内ラベルのみにする。

**Architecture:** IK 固定・接地は MTE の `IKHoldEntity`（FinalIK `LimbControl`）を廃止して SE の `MaidIKHoldController`（`MaidIKChain`）へ一本化し、キー書き込み・再生をそちら経由にする。拡張ボーンの対象選択は `timeline.extendBoneNamesMap` 主導をやめ、`ModelBoneTimelineLayer` と同じ `EditTargetStore` ベースの追跡フレームワーク（`TimelineLayerBaseTracking`）へ載せ替える。体ボーンは既に Inspector・ギズモが同じ Transform を編集しており、`UpdateFrame` がライブ値を読むため追加作業は不要。

**Tech Stack:** C#（プラグイン本体は .NET 3.5 相当・旧形式 csproj）、IMGUI（GUIView）、xunit（`source/COM3D2.SceneEditor.Plugin.Tests`、net48 SDK 形式）

**Spec:** `docs/superpowers/specs/2026-08-27-maid-motion-layer-delegation-design.md`

## Global Constraints

- コメント・ログメッセージは日本語で書く
- プラグイン本体の csproj は旧形式・手動管理。ファイルの追加・削除時は `<Compile Include>` をパス昇順の位置で追加／削除する
- ビルド確認は MSBuild 直叩きで行う（`debug.bat` はゲーム停止中に実機へ DLL をコピーしてしまうため使わない）:
  - COM3D25: `MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"`
  - COM3D2: 同じコマンドの `/p:GameVersion=COM3D2`
  - **2 構成とも必ず通すこと**（対象フレームワークが違う）
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`。テストは COM3D25 構成のビルド成果物を参照するため、**先に COM3D25 構成をビルドしてから**実行する
- `deploy.bat` / `deploy.ps1` は実行しない
- git worktree は使わない。作業はメインの作業ディレクトリで行う

## 検証済みの前提（設計書の調査結果）

- MTE の拡張ボーン名は `slotName + "/" + boneName`。`slotName` は `TBodySkin.Category` で、これは `TBody.m_strDefSlotName` 由来。`TBody.SlotID` と同順・同名のため SE 側の `((TBody.SlotID)i).ToString()` と一致する（変換不要）
- `IKHoldType`（MTE）と `MaidIKHoldType`（SE）はメンバ名が同一。キーフレームのボーン名は enum メンバ名そのもの
- `IKHoldEntity.targetPosition` と SE の `HoldEntity.targetPosition` はどちらも固定点ボーンのワールド座標
- `MaidCache.GetIkFabrik / GetDragPoint / GetAxisObj / IsIkDragging` の外部呼び出しは `MaidManager.GetIkPosition` の 1 本のみ。`StudioHackBase.IsIKDragging` は常に false の既定実装だけ
- `IKHoldType` enum 自体は `TimelineXml.cs:135`（`isHoldList`）・`TimelineXml.cs:458`（旧データ移行）・`MotionTimelineLayer.GetTransformTypeInternal` が参照するため残す
- SE の `MaidFingerBlendController` は `FingerBlend.BaseFinger.enabled` に触れていない。`enabled` を操作するのは `MotionTimelineLayer.GetBaseFinger` だけ

---

### Task 1: IK 固定に isAnime を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/IKSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`

**Interfaces:**
- Produces:
  - `bool MaidIKHoldController.GetAnime(Maid maid, MaidIKHoldType type)`
  - `void MaidIKHoldController.SetAnime(Maid maid, MaidIKHoldType type, bool anime)`
  - `List<string> ScenePresetMaid.ikAnimes`

- [ ] **Step 1: `HoldEntity` に `isAnime` を足し、アクセサを追加する**

`MaidIKHoldController.cs` の `HoldEntity`（`:45-51`）へフィールドを足す:

```csharp
        /// <summary>固定 1 箇所ぶんの状態</summary>
        private class HoldEntity
        {
            public bool isHold;
            /// <summary>モーション再生中も固定を効かせる（MTE の IK アニメーション相当）</summary>
            public bool isAnime;
            public bool resetRequested;
            public Vector3 targetPosition;
        }
```

`GetHold` / `SetHold`（`:206-236`）の直後にアクセサを追加する:

```csharp
        public bool GetAnime(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry) && entry.entities[(int)type].isAnime;
        }

        public void SetAnime(Maid maid, MaidIKHoldType type, bool anime)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.entities[(int)type].isAnime = anime;
        }
```

- [ ] **Step 2: 編集モード・モーション停止のゲートを isAnime で抜けるようにする**

`LateUpdate`（`:349-353`）の編集モードゲートを、アニメ指定が 1 つでもあるメイドは通すよう書き換える:

```csharp
            // 固定はポーズ編集用の機能なので、編集モード中だけ効かせる
            // （UI の「※編集モードで有効」表記と揃える）。
            // ただしアニメ指定（タイムライン再生中の固定）は編集モード外でも効かせる
            var isEditMode = MaidManipulateManager.instance.isEditMode;

            foreach (var pair in _entries)
            {
                if (!isEditMode && !HasAnime(pair.Value))
                {
                    continue;
                }
                UpdateMaid(pair.Key, pair.Value);
            }
        }

        /// <summary>アニメ指定の固定を 1 箇所でも持つか</summary>
        private static bool HasAnime(MaidEntry entry)
        {
            foreach (var entity in entry.entities)
            {
                if (entity.isHold && entity.isAnime)
                {
                    return true;
                }
            }
            return false;
        }
```

`UpdateMaid`（`:389-393`）のモーション停止ゲートを書き換える:

```csharp
                // モーション再生中の固定はアニメ指定のときだけ行う。
                // 指定が無ければ従来どおり停止中のポーズ編集時のみ固定する
                if (!isMotionStopped && !entity.isAnime)
                {
                    continue;
                }
```

さらに `UpdateMaid` の先頭で編集モード外のときはアニメ指定の箇所だけ処理するよう、ループ内へガードを足す（`entity.isHold` 判定の直後）:

```csharp
                if (!MaidManipulateManager.instance.isEditMode && !entity.isAnime)
                {
                    continue;
                }
```

- [ ] **Step 3: IK ウィンドウにアニメトグルを追加する**

`MaidIKWindow.DrawHoldToggles` のペア行（`:99-129`）で、各固定トグルの右に「アニメ」トグルを置く。固定 OFF の箇所では意味を持たないため無効化する:

```csharp
                    foreach (var type in pair)
                    {
                        var holdType = type;
                        var isHold = holdController.GetHold(target, holdType);

                        view.DrawToggle(MaidIKHoldController.GetHoldTypeName(holdType),
                            isHold, ToggleWidth, ROW_HEIGHT,
                            newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                                    "IK固定: " + MaidIKHoldController.GetHoldTypeName(holdType));
                                holdController.SetHold(target, holdType, newValue);
                            });

                        // 再生中も固定を効かせる指定。固定 OFF では効かないので押させない
                        view.DrawToggle("アニメ", holdController.GetAnime(target, holdType),
                            AnimeToggleWidth, ROW_HEIGHT, isHold,
                            newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                                    "IKアニメ: " + MaidIKHoldController.GetHoldTypeName(holdType));
                                holdController.SetAnime(target, holdType, newValue);
                            });
                    }
```

`ToggleWidth` の隣に定数を足す:

```csharp
        private static readonly int AnimeToggleWidth = 60;
```

使うオーバーロードは `GUIView.DrawToggle(string label, bool value, float width, float height, bool enabled, Action<bool> onChanged)`（`MTEUtils/GUIView.cs:1007`）。`enabled` が false のときは灰色表示で押せなくなる。

- [ ] **Step 4: 履歴スナップショットに isAnime を含める**

`IKSnapshot.cs` に `_animes` を足す。`_holds` と同じ扱いにする:

```csharp
        private readonly bool[] _holds = new bool[(int)MaidIKHoldType.Max];
        private readonly bool[] _animes = new bool[(int)MaidIKHoldType.Max];
```

`Capture` のループへ追加:

```csharp
            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                snapshot._holds[i] = controller.GetHold(maid, (MaidIKHoldType)i);
                snapshot._animes[i] = controller.GetAnime(maid, (MaidIKHoldType)i);
            }
```

`Apply` のループへ追加（固定より先にアニメ指定を入れる。`SetHold` は ON でモーションを止めるため、順序を変えても結果は同じだが指定漏れを避ける）:

```csharp
            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                controller.SetAnime(maid, (MaidIKHoldType)i, _animes[i]);
                controller.SetHold(maid, (MaidIKHoldType)i, _holds[i]);
            }
```

`Approximately` の固定比較ループへ追加:

```csharp
            for (var i = 0; i < _holds.Length; i++)
            {
                if (_holds[i] != o._holds[i] || _animes[i] != o._animes[i])
                {
                    return false;
                }
            }
```

- [ ] **Step 5: シーンプリセットへ isAnime を保存する**

`ScenePresetData.cs` の `ScenePresetMaid`（`:570` 付近の `ikParams` / `ikHolds` の並び）へ追加:

```csharp
        /// <summary>アニメ指定（再生中も固定）の箇所。MaidIKHoldType 名の一覧</summary>
        public List<string> ikAnimes = new List<string>();
```

`ScenePresetManager.CaptureIKHold`（`:1173-1188`）のループへ追加:

```csharp
                if (maidManager.ikHoldController.GetAnime(maid, type))
                {
                    state.ikAnimes.Add(type.ToString());
                }
```

`ScenePresetManager.ApplyIKHold`（`:2563-2584`）のループへ追加:

```csharp
                ik.SetAnime(maid, type, state.ikAnimes.Contains(type.ToString()));
                ik.SetHold(maid, type, state.ikHolds.Contains(type.ToString()));
```

`ScenePresetData.cs:671` の `CurrentVersion` を 24 から 25 へ上げ、その直前に並ぶバージョン履歴コメントへ次の行を足す:

```csharp
        // v25: maid に ikAnimes（IK 固定のアニメ指定）を追加。
        //      旧形式は一覧が空 = 全 OFF として読める
        public static readonly int CurrentVersion = 25;
```

- [ ] **Step 6: ビルドとテスト**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```
Expected: 2 構成ともビルド成功、テスト全 PASS（既存テストへの退行がないこと）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs source/COM3D2.SceneEditor.Plugin/MaidIKWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/History/IKSnapshot.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs
git commit -m "feat(ik): IK固定にアニメ指定を追加し再生中も効くようにする"
```

---

### Task 2: IK 固定・接地のキー読み書きを SE コントローラへ差し替える

MTE 側の `IKHoldEntity` はまだ残したまま、読み書き先だけを切り替える（撤去は Task 3）。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:289-340,479-510`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidIKHoldTypeNameTests.cs`（新規）

**Interfaces:**
- Consumes: `MaidIKHoldController.GetAnime / SetAnime`（Task 1）
- Produces:
  - `bool MaidIKHoldController.TryParseHoldType(string name, out MaidIKHoldType type)`
  - `Vector3 MaidIKHoldController.GetTargetPosition(Maid maid, MaidIKHoldType type)`
  - `void MaidIKHoldController.SetTargetPosition(Maid maid, MaidIKHoldType type, Vector3 position)`

- [ ] **Step 1: enum メンバ名の一致を固定する失敗テストを書く**

Create `source/COM3D2.SceneEditor.Plugin.Tests/MaidIKHoldTypeNameTests.cs`:

```csharp
using System;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// キーフレームの IK ボーン名は MTE の IKHoldType メンバ名そのもの。
    /// SE の MaidIKHoldType と名前がずれると、既存タイムラインの IK キーが
    /// 一切引けなくなるため、対応表をテストで固定する
    /// </summary>
    public class MaidIKHoldTypeNameTests
    {
        [Fact]
        public void MTEのIKHoldType名がすべてSE側へ解決できる()
        {
            var names = Enum.GetValues(typeof(IKHoldType))
                .Cast<IKHoldType>()
                .Where(t => t != IKHoldType.Max)
                .Select(t => t.ToString());

            foreach (var name in names)
            {
                MaidIKHoldType type;
                Assert.True(
                    MaidIKHoldController.TryParseHoldType(name, out type),
                    "解決できませんでした: " + name);
                Assert.Equal(name, type.ToString());
            }
        }

        [Fact]
        public void 未知の名前は解決できない()
        {
            MaidIKHoldType type;
            Assert.False(MaidIKHoldController.TryParseHoldType("Head_Tip", out type));
        }

        [Fact]
        public void nullや空文字でも例外にならない()
        {
            MaidIKHoldType type;
            Assert.False(MaidIKHoldController.TryParseHoldType(null, out type));
            Assert.False(MaidIKHoldController.TryParseHoldType("", out type));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter MaidIKHoldTypeNameTests`
Expected: コンパイルエラー（`TryParseHoldType` が存在しない）で FAIL

- [ ] **Step 3: `TryParseHoldType` とターゲット位置アクセサを実装する**

`MaidIKHoldController.cs` の `HoldTypeByBoneName` の下（`:118` 付近）へ追加:

```csharp
        /// <summary>enum メンバ名 → 固定タイプ。タイムラインのキーは enum 名でボーンを指す</summary>
        private static readonly Dictionary<string, MaidIKHoldType> HoldTypeByEnumName =
            Enum.GetValues(typeof(MaidIKHoldType))
                .Cast<MaidIKHoldType>()
                .Where(t => t != MaidIKHoldType.Max)
                .ToDictionary(t => t.ToString(), t => t);

        /// <summary>enum メンバ名から固定タイプを引く。未知の名前なら false</summary>
        public static bool TryParseHoldType(string name, out MaidIKHoldType type)
        {
            type = MaidIKHoldType.Max;
            return !string.IsNullOrEmpty(name) && HoldTypeByEnumName.TryGetValue(name, out type);
        }
```

`GetAnime` / `SetAnime` の隣へターゲット位置のアクセサを追加:

```csharp
        /// <summary>固定点の目標ワールド座標。エントリ未作成なら Vector3.zero</summary>
        public Vector3 GetTargetPosition(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry)
                ? entry.entities[(int)type].targetPosition
                : Vector3.zero;
        }

        /// <summary>固定点の目標ワールド座標を差し替える（タイムライン再生からの書き戻し用）</summary>
        public void SetTargetPosition(Maid maid, MaidIKHoldType type, Vector3 position)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }

            var entity = entry.entities[(int)type];
            entity.targetPosition = position;
            // 外から位置を指定した以上、現在のボーン位置で取り直させてはいけない
            entity.resetRequested = false;
        }
```

`System.Linq` の using が無ければ足す。

- [ ] **Step 4: テストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter MaidIKHoldTypeNameTests
```
Expected: PASS

- [ ] **Step 5: `UpdateFrame` の IK・接地ブロックを差し替える**

`MotionTimelineLayer.cs` の先頭へ `using COM3D2.SceneEditor.Plugin;` を足す（`ModelBoneTimelineLayer.cs:8` と同じ）。これで `MaidIKHoldController` / `MaidIKHoldType` / `EditTargetStore` / `BoneEditManager` が修飾なしで書ける。ただしレイヤー内の `maidManager` は MTE 側の `MaidManager` を指すため、SE のマネージャは名前で取り違えないようクラス先頭付近へショートカットを置く:

```csharp
        /// <summary>IK 固定・接地の実体は SE 側が持つ (maidManager は MTE 側なので混同しないこと)</summary>
        private static MaidIKHoldController ikHoldController
            => MaidManipulateManager.instance.ikHoldController;
```

`UpdateFrame` の IK ブロック（`:479-494`）を置き換える:

```csharp
            foreach (var name in MaidCache.ikHoldTypeMap.Keys)
            {
                MaidIKHoldType holdType;
                if (!MaidIKHoldController.TryParseHoldType(name, out holdType))
                {
                    continue;
                }

                var trans = CreateTransformData<TransformDataIKHold>(name);
                trans.position = ikHoldController.GetTargetPosition(maid, holdType);
                trans.isHold = ikHoldController.GetHold(maid, holdType);
                trans.isAnime = ikHoldController.GetAnime(maid, holdType);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
```

接地ブロック（`:496-510`）を置き換える:

```csharp
            {
                var name = GroundingBoneName;
                var holdParams = ikHoldController.GetParams(maid);

                var trans = CreateTransformData<TransformDataGrounding>(name);
                trans.isGroundingFootL = holdParams.isGroundingFootL;
                trans.isGroundingFootR = holdParams.isGroundingFootR;
                trans.floorHeight = holdParams.floorHeight;
                trans.footBaseOffset = holdParams.footBaseOffset;
                trans.footStretchHeight = holdParams.footStretchHeight;
                trans.footStretchAngle = holdParams.footStretchAngle;
                trans.footGroundAngle = holdParams.footGroundAngle;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
```

- [ ] **Step 6: 再生側（ApplyMotion）を差し替える**

`ApplyIKHoldMotion`（`:289-322`）を置き換える:

```csharp
        private void ApplyIKHoldMotion(MotionData motion, float t)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            MaidIKHoldType holdType;
            if (!MaidIKHoldController.TryParseHoldType(motion.name, out holdType))
            {
                return;
            }

            var start = motion.start as TransformDataIKHold;
            var end = motion.end as TransformDataIKHold;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            ikHoldController.SetAnime(maid, holdType, start.isAnime);
            ikHoldController.SetHold(maid, holdType, start.isHold);
            ikHoldController.SetTargetPosition(maid, holdType, PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t));
        }
```

`ApplyGroundingMotion`（`:326-341`）を置き換える:

```csharp
        private void ApplyGroundingMotion(MotionData motion)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var start = motion.start as TransformDataGrounding;
            var holdParams = ikHoldController.GetParams(maid);

            holdParams.isGroundingFootL = start.isGroundingFootL;
            holdParams.isGroundingFootR = start.isGroundingFootR;
            holdParams.floorHeight = start.floorHeight;
            holdParams.footBaseOffset = start.footBaseOffset;
            holdParams.footStretchHeight = start.footStretchHeight;
            holdParams.footStretchAngle = start.footStretchAngle;
            holdParams.footGroundAngle = start.footGroundAngle;
        }
```

- [ ] **Step 7: Init の IK リセットを外す**

`MotionTimelineLayer.Init`（`:71-72`）の 2 行を削除する。IK 状態はタイムライン所有ではなくなり、リセットするとユーザーが IK ウィンドウで設定した内容を消してしまうため。

```csharp
            maidCache.ResetIkHoldEntities();
            maidCache.ResetGrounding();
```

削除した理由をコメントで残す:

```csharp
            // IK 固定・接地の状態は SE の MaidIKHoldController が持つ (タイムライン所有ではない)。
            // 読み込み時のリセットは IK ウィンドウの設定を消すことになるため行わない
```

- [ ] **Step 8: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKHoldController.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidIKHoldTypeNameTests.cs
git commit -m "refactor(timeline): IK固定・接地のキー読み書きをSEのIKコントローラ経由にする"
```

---

### Task 3: MTE 側の IK 実体を撤去する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/IKHoldType.cs`
- Delete: `source/COM3D2.SceneEditor.Plugin/Timeline/IKHoldEntity.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Extensions.cs:137-160`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MaidManager.cs:279-286`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/StudioHackBase.cs:290-293`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `enum COM3D2.MotionTimelineEditor.Plugin.IKHoldType`（メンバは従来どおり、`Max` を含む）。`MaidCache.ikHoldTypeMap` / `MaidCache.GetIKHoldType(string)` は据え置き

- [ ] **Step 1: `IKHoldType` を独立ファイルへ切り出す**

Create `source/COM3D2.SceneEditor.Plugin/Timeline/IKHoldType.cs`:

```csharp
namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// IK 固定の対象箇所。
    /// 固定の実体は SE の MaidIKHoldController が持ち、この enum は
    /// キーフレームのボーン名テーブル (TimelineXml の isHoldList・旧データ移行、
    /// MotionTimelineLayer の TransformType 判定) としてのみ使う。
    /// メンバ名は MaidIKHoldType と一致させること (キーのボーン名がメンバ名そのもののため)
    /// </summary>
    public enum IKHoldType
    {
        Arm_R_Joint,
        Arm_R_Tip,
        Arm_L_Joint,
        Arm_L_Tip,
        Foot_R_Joint,
        Foot_R_Tip,
        Foot_L_Joint,
        Foot_L_Tip,
        Max,
    }
}
```

- [ ] **Step 2: `IKHoldEntity.cs` を削除し csproj を更新する**

```bash
git rm source/COM3D2.SceneEditor.Plugin/Timeline/IKHoldEntity.cs
```

`COM3D2.SceneEditor.Plugin.csproj:355` の `<Compile Include="Timeline\IKHoldEntity.cs" />` を削除し、パス昇順の位置へ `<Compile Include="Timeline\IKHoldType.cs" />` を追加する。

- [ ] **Step 3: `MaidCache` から IK 実体を撤去する**

`Timeline/MaidCache.cs` から次を削除する:

- フィールド `ikHoldEntities`（`:45`）と接地 7 フィールド（`:55-61`）
- `ResetIkHoldEntities`（`:370`）/ `ResetGrounding`（`:375`）
- `GetIKHoldEntity(IKHoldType)`（`:381`）/ `GetIKHoldEntity(string)`（`:407`）/ `GetIkFabrik` / `GetDragPoint` / `GetAxisObj` / `GetIkPosition` / `IsIkDragging`
- `OnPoseEditUpdated`（`:1228`）
- `LateUpdate`（`:537`）。IK 更新ループを消すと本体が `maid == null` の早期 return だけになるためメソッドごと削除する。呼び出し元は `Timeline/Manager/MaidManager.cs:251-262` の `LateUpdate` override 1 箇所だけで、そのループを消すと override も空になるため併せて削除する
- `:456` の `ikHoldEntities.Clear()`
- `GetBoneTransform`（`:807` 付近）の IK 分岐 3 行:

```csharp
            var ikHoldEntity = GetIKHoldEntity(boneName);
            if (ikHoldEntity != null)
            {
                return ikHoldEntity.transform;
            }
```

を削除して `return null;` に落とす。`GetInitialPosition` にも IK 分岐があれば同様に外す。

`ikHoldTypeMap`（`:66`）と `GetIKHoldType(string)`（`:396`）は残す。

`OnPoseEditUpdated` の呼び出し元があれば、そこも削除する（`grep -rn "OnPoseEditUpdated" --include=*.cs source/` で確認）。

- [ ] **Step 4: 使われなくなった周辺 API を撤去する**

- `Timeline/Extensions.cs` の `ConvertBoneType`（`:137-160`。`IKHoldType` → `IKManager.BoneType`）を削除
- `Timeline/Manager/MaidManager.cs` の `GetIkPosition`（`:279-286`）を削除
- `Timeline/Hack/StudioHackBase.cs` の `IsIKDragging`（`:290-293`）を削除。派生クラスに override があれば併せて削除する（`grep -rn "IsIKDragging" --include=*.cs source/`）

- [ ] **Step 5: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS。未使用参照のコンパイルエラーが出た場合は、その参照も本タスクの撤去対象として削除する

- [ ] **Step 6: コミット**

```bash
git add -A source/COM3D2.SceneEditor.Plugin
git commit -m "refactor(timeline): MTE側のIK固定実体を撤去しSEへ一本化する"
```

---

### Task 4: メイド拡張ボーンの追跡集約ストアを作る

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/SlotQualifiedNames.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/SlotQualifiedNamesTests.cs`（新規）

**Interfaces:**
- Produces:
  - `static string SlotQualifiedNames.Qualify(string slotName, string boneName)`
  - `static void SlotQualifiedNames.Collect(List<BoneEditEntry> entries, List<string> result)`
  - `EditTargetStore BoneEditManager.FindMaidBoneTrackedStore(Maid maid)`（記録の無いメイドは null）

- [ ] **Step 1: 名前修飾の失敗テストを書く**

Create `source/COM3D2.SceneEditor.Plugin.Tests/SlotQualifiedNamesTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// メイド拡張ボーン名 ("{slotName}/{boneName}") の生成を固定する。
    /// MTE 側 (ExtendBoneCache.AddEntity) と同じ規則でなければ
    /// 追跡集合がタイムラインの候補名と一切マッチしなくなる
    /// </summary>
    public class SlotQualifiedNamesTests
    {
        [Fact]
        public void スロット名とボーン名をスラッシュで連結する()
        {
            Assert.Equal("skirt/Skirt_01", SlotQualifiedNames.Qualify("skirt", "Skirt_01"));
        }

        [Theory]
        [InlineData(null, "Skirt_01")]
        [InlineData("skirt", null)]
        [InlineData("", "Skirt_01")]
        [InlineData("skirt", "")]
        public void 名前が欠けていればnullを返す(string slotName, string boneName)
        {
            Assert.Null(SlotQualifiedNames.Qualify(slotName, boneName));
        }

        [Fact]
        public void Collectはエントリを修飾名にして積む()
        {
            var entries = new List<BoneEditEntry>
            {
                new BoneEditEntry { slotName = "skirt", boneName = "Skirt_01" },
                new BoneEditEntry { slotName = "wear", boneName = "Wear_02" },
            };

            var result = new List<string>();
            SlotQualifiedNames.Collect(entries, result);

            Assert.Equal(new[] { "skirt/Skirt_01", "wear/Wear_02" }, result);
        }

        [Fact]
        public void Collectはresultをクリアしない()
        {
            var result = new List<string> { "既存" };
            SlotQualifiedNames.Collect(
                new List<BoneEditEntry> { new BoneEditEntry { slotName = "skirt", boneName = "S" } },
                result);

            Assert.Equal(new[] { "既存", "skirt/S" }, result);
        }

        [Fact]
        public void Collectはnullを渡しても例外にならない()
        {
            SlotQualifiedNames.Collect(null, new List<string>());
            SlotQualifiedNames.Collect(new List<BoneEditEntry>(), null);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter SlotQualifiedNamesTests`
Expected: コンパイルエラー（`SlotQualifiedNames` が存在しない）で FAIL

- [ ] **Step 3: `SlotQualifiedNames` を実装する**

Create `source/COM3D2.SceneEditor.Plugin/MaidManipulation/SlotQualifiedNames.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド拡張ボーン名 ("{slotName}/{boneName}") の組み立て。
    /// 編集側はスロット名とボーン名を別々に持ち、タイムライン側は連結名で扱うため、
    /// その橋渡しをここ 1 箇所に閉じ込める。
    /// 修飾規則は MTE の ExtendBoneCache.AddEntity (Timeline/ExtendBoneCache.cs) と
    /// 必ず一致させること。スロット名は双方とも TBody.SlotID 名 (= TBodySkin.Category)
    /// </summary>
    public static class SlotQualifiedNames
    {
        /// <summary>タイムライン側の拡張ボーン名を作る。名前が欠けていれば null</summary>
        public static string Qualify(string slotName, string boneName)
        {
            if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            return slotName + "/" + boneName;
        }

        /// <summary>ボーン編集済みエントリを拡張ボーン名にして result へ積む (result はクリアしない)</summary>
        public static void Collect(List<BoneEditEntry> entries, List<string> result)
        {
            if (entries == null || result == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var name = Qualify(entry.slotName, entry.boneName);
                if (name != null)
                {
                    result.Add(name);
                }
            }
        }
    }
}
```

csproj へ `<Compile Include="MaidManipulation\SlotQualifiedNames.cs" />` をパス昇順の位置で追加する。

- [ ] **Step 4: テストが通ることを確認する**

Run:
```
MSBuild source\COM3D2.SceneEditor.Plugin\COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter SlotQualifiedNamesTests
```
Expected: PASS

- [ ] **Step 5: `BoneEditManager` にメイド単位の集約ストアを足す**

`_modelBoneTracked` の宣言（`:100-103`）の下へ追加する。**集約はメイドごとに別インスタンスにする**。名前はスロット名までしか修飾されないため、全メイドを 1 つに集約すると他メイドの編集が混ざる:

```csharp
        /// <summary>
        /// メイドごとの編集済みボーンを拡張ボーン名 ("slot/bone") で集約した読み取り専用ビュー。
        /// MotionTimelineLayer の追跡ストアとして使う。
        /// 名前にメイドの識別子が入らないため、メイドごとに別インスタンスへ集約する。
        /// ソース・オブ・トゥルースは _stores 側で、ここへ直接 Mark/Unmark してはならない
        /// </summary>
        private readonly Dictionary<Maid, ModelTrackedNameStore<Maid>> _maidBoneTracked
            = new Dictionary<Maid, ModelTrackedNameStore<Maid>>();

        // Sync へ毎フレーム渡すキー列。1 要素だけ入れて使い回す
        private readonly List<Maid> _maidTrackedKeys = new List<Maid>(1);
```

参照用 API を `modelBoneTrackedStore` の隣へ追加:

```csharp
        /// <summary>メイドの拡張ボーン集約ストア。記録が無ければ null</summary>
        public EditTargetStore FindMaidBoneTrackedStore(Maid maid)
        {
            ModelTrackedNameStore<Maid> tracked;
            return maid != null && _maidBoneTracked.TryGetValue(maid, out tracked)
                ? tracked.store
                : null;
        }
```

- [ ] **Step 6: 毎フレームの同期と後片付けを足す**

`SyncModelBoneTrackedStore`（`:600-622`）の下へメイド版を追加する:

```csharp
        /// <summary>メイドの編集済みボーンを集約ストアへ片方向同期する</summary>
        private void SyncMaidBoneTrackedStores()
        {
            foreach (var pair in _stores)
            {
                var maid = pair.Key;
                if (maid == null)
                {
                    continue;
                }

                ModelTrackedNameStore<Maid> tracked;
                if (!_maidBoneTracked.TryGetValue(maid, out tracked))
                {
                    tracked = new ModelTrackedNameStore<Maid>();
                    _maidBoneTracked[maid] = tracked;
                }

                _maidTrackedKeys.Clear();
                _maidTrackedKeys.Add(maid);

                tracked.Sync(
                    _maidTrackedKeys,
                    m => _stores[m].version,
                    (m, result) =>
                    {
                        SlotQualifiedNames.Collect(_stores[m].GetAllEntries(), result);
                        return true;
                    });
            }
        }
```

`Update`（`:553-564`）の `SyncModelBoneTrackedStore()` の後ろへ `SyncMaidBoneTrackedStores();` を足す。

メイドの後片付けは既存の死んだメイド掃除（`_deadMaids` を使う箇所。`UpdateStores` 内）で `_stores.Remove(maid)` と並べて `_maidBoneTracked.Remove(maid);` を行う。

- [ ] **Step 7: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/SlotQualifiedNames.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/BoneEditManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/SlotQualifiedNamesTests.cs
git commit -m "feat(bone): メイド拡張ボーンの追跡集約ストアを追加する"
```

---

### Task 5: 拡張ボーンの対象集合を追跡ストア方式へ載せ替える

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:34-50,64-92,126-158,461-477`

**Interfaces:**
- Consumes: `BoneEditManager.FindMaidBoneTrackedStore(Maid)`（Task 4）、`TimelineLayerBase` の `trackedStore` / `trackedCandidateNames` / `trackedHistoryPrefix` / `trackedBoneNames` / `InvalidateTrackedBoneNames()`

- [ ] **Step 1: 追跡フレームワークの口を実装する**

`MotionTimelineLayer` のクラス先頭（`allBoneNames` の周辺）へ追加する。`MorphTimelineLayer:17-30` と同じ形:

```csharp
        protected override EditTargetStore trackedStore
        {
            get
            {
                var maid = this.maid;
                return maid != null
                    ? BoneEditManager.instance.FindMaidBoneTrackedStore(maid)
                    : null;
            }
        }

        private List<string> _trackedCandidateNames = new List<string>();

        /// <summary>拡張ボーンの候補。MTE の ExtendBoneCache が拾えたボーンだけが対象になる</summary>
        protected override List<string> trackedCandidateNames
        {
            get
            {
                _trackedCandidateNames.Clear();
                var cache = maidCache != null ? maidCache.extendBoneCache : null;
                if (cache != null)
                {
                    _trackedCandidateNames.AddRange(cache.entities.Keys);
                }
                return _trackedCandidateNames;
            }
        }

        protected override string trackedHistoryPrefix => "拡張ボーン";
```

- [ ] **Step 2: `allBoneNames` を追跡集合ベースに変える**

`allBoneNames`（`:35-50`）を置き換える。体ボーン・IK・接地・指ブレンドは固定で、拡張ボーンだけ追跡集合から取る。キャッシュは追跡集合が変わるたびに作り直す必要があるため、毎回組み立てる:

```csharp
        private readonly List<string> _allBoneNamesCache = new List<string>();

        public override List<string> allBoneNames
        {
            get
            {
                _allBoneNamesCache.Clear();
                _allBoneNamesCache.AddRange(BoneUtils.saveBoneNames);
                // 拡張ボーンはボーンウィンドウのチェック (∪ 既存キーフレーム記載) が対象集合
                _allBoneNamesCache.AddRange(trackedBoneNames);
                _allBoneNamesCache.AddRange(MaidCache.ikHoldTypeMap.Keys);
                _allBoneNamesCache.Add(GroundingBoneName);
                _allBoneNamesCache.AddRange(FingerBlendBoneNames);
                return _allBoneNamesCache;
            }
        }
```

`_allBoneNames` フィールドと、`InitMenuItems` 冒頭の `_allBoneNames = null;` を削除する。

- [ ] **Step 3: `InitMenuItems` と `Init` / `UpdateFrame` の参照を差し替える**

`InitMenuItems`（`:126-158`）の拡張ボーンのループを `timeline.GetExtendBoneNames(slotNo)` から `trackedBoneNames` に変える。ループ本体（`extendBoneCache.GetEntity` で引いてスロットごとの `BoneSetMenuItem` に積む処理）はそのまま:

```csharp
            foreach (var extendBoneName in trackedBoneNames)
```

同じ関数の末尾（メニュー構築が終わった後）で、保存互換のため `extendBoneNamesMap` を追跡集合で置き換える:

```csharp
            // 保存互換のため、タイムライン側の拡張ボーン一覧は追跡集合から作り直す。
            // ソース・オブ・トゥルースはボーンウィンドウのチェック側
            var extendBoneNames = timeline.GetExtendBoneNames(slotNo);
            extendBoneNames.Clear();
            foreach (var extendBoneName in trackedBoneNames)
            {
                extendBoneNames.Add(extendBoneName);
            }
```

`Init`（`:76`）と `UpdateFrame`（`:461`）の `timeline.GetExtendBoneNames(slotNo)` も `trackedBoneNames` に差し替える。

- [ ] **Step 4: 候補が入れ替わる契機で追跡キャッシュを捨てる**

`OnMaidChanged`（`:369-372`）で `InitMenuItems()` の前に `InvalidateTrackedBoneNames();` を呼ぶ。メイドが変われば `extendBoneCache` の中身ごと入れ替わるため:

```csharp
        public override void OnMaidChanged(Maid maid)
        {
            // 候補 (extendBoneCache) ごと入れ替わるため、追跡集合のキャッシュを捨てる
            InvalidateTrackedBoneNames();
            InitMenuItems();
        }
```

- [ ] **Step 5: 旧 API がまだ生きていることを確認する**

`TimelineData.AddExtendBoneName` / `RemoveExtendBoneName` / `HasExtendBoneName` は「追加」タブの `DrawExtendBone`（`MotionTimelineLayer.cs:1149,1159,1183,1189,1193`）から**まだ呼ばれている**ため、このタスクでは消さない。撤去は UI を消す Task 7 で行う。

この時点の「追加」タブは、チェックしても `InitMenuItems` が `extendBoneNamesMap` を追跡集合で上書きするため実質無効になる。Task 7 まで残る過渡状態として許容する。

Run: `grep -n "HasExtendBoneName\|AddExtendBoneName\|RemoveExtendBoneName" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs`
Expected: 5 件ヒットする（撤去は Task 7 で行うため、ここでは消さない）

- [ ] **Step 6: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS（`XmlRoundTripTests` が拡張ボーンを含むフィクスチャで落ちないこと）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline
git commit -m "refactor(timeline): 拡張ボーンの対象集合をボーン編集の追跡ストアから引く"
```

---

### Task 6: 指ブレンド有効トグルを指ウィンドウへ移す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFingerWindow.cs:143-170`

- [ ] **Step 1: ヘッダー行へトグルを追加する**

`MaidFingerWindow.DrawHeader` の「個別編集」トグルの後ろへ追加する。`timeline` はタイムライン未ロード時に null になるため、そのときは描かない:

```csharp
                // タイムライン全体の設定 (FingerBlend.BaseFinger.enabled を握る唯一の経路)。
                // メイドごとの設定ではないためラベルで明示する
                var timeline = MTEP.TimelineManager.instance.timeline;
                if (timeline != null)
                {
                    view.DrawToggle("TL:ブレンド有効", timeline.fingerBlendEnabled, 120, ROW_HEIGHT,
                        value => timeline.fingerBlendEnabled = value);
                }
```

`MTEP` は SE 側ウィンドウが使っている MTE 名前空間のエイリアス（`MaidFaceWindow.cs:430` の `MTEP.TimelineManager.instance.timeline` と同じ書き方）。`MaidFingerWindow.cs` に `MTEP` エイリアスの using が無ければ、`MaidFaceWindow.cs` 先頭の using 行をそのまま持ってくる。

ヘッダー行は「指を保存 / 個別編集 / TL:ブレンド有効 / （右端）リセット」の並びになる。右端揃えのリセットボタンの計算（`view.currentPos.x = ...`）はトグル追加後に行うこと。

- [ ] **Step 2: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidFingerWindow.cs
git commit -m "feat(finger): 指ブレンド有効トグルを指ウィンドウへ移す"
```

---

### Task 7: レイヤー UI を案内ラベルだけにする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:853-1110`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:738-782`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ITimelineLayer.cs:52-53`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:291-299`

- [ ] **Step 1: `DrawWindow` を案内ラベルへ置き換える**

`DrawWindow`（`:853-886`）を置き換える。`ModelBoneTimelineLayer.DrawWindow:228-235` と同じ方針:

```csharp
        public override void DrawWindow(GUIView view)
        {
            // ボーン・IK・指の編集 UI は SE の各ウィンドウに委譲する (レイヤー UI 非接続方針)。
            // キー書き込み (UpdateFrame) はいずれもライブ状態を読むため、
            // 個別ウィンドウでの編集がそのままキー化される
            view.DrawLabel("ボーンの編集はボーンウィンドウ・Inspectorで行ってください", -1, 20);
            view.DrawLabel("IK固定・接地はIKウィンドウで行ってください", -1, 20);
            view.DrawLabel("指の編集は指ウィンドウで行ってください", -1, 20);
        }
```

- [ ] **Step 2: 使われなくなった UI コードを削除する**

`MotionTimelineLayer` から次を削除する:

- `TabType` enum と `_tabType` フィールド
- `DrawTransformEdit`（`:888`）/ `DrawMenuItem`（`:935`）/ `DrawIKMenuItem`（`:992`）/ `DrawExtendBone`（`:1111`）
- `_menuItemComboBox` / `_transComboBox` / `_slotNameComboBox` / `_isExtendBoneAllEnabled` / `_extendSlotNames` など、削除した UI 専用のフィールド
- 上記でしか使っていない定数（`DrawMaskPositonAndRotation` などの描画マスク）

`GetBaseFinger` / `GetArmFinger` / `GetLegFinger` / `FingerBlendBoneNames` / `FingerBlendBoneTypeMap` / `ConvertToFingerBlendType` / `IsFingerBlendBone` は `UpdateFrame`・`ApplyFingerBlendMotion`・`GetTransformTypeInternal` が使うため**残す**。

削除後、`grep -n "_extendSlotNames\|_tabType" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs` で残骸が無いことを確認する。

- [ ] **Step 3: 旧 API（Add/RemoveExtendBoneName と通知）を撤去する**

Step 2 で `DrawExtendBone` が消え、ここで初めて呼び出し元がゼロになる。

- `TimelineData.cs` の `AddExtendBoneName`（`:756`）/ `RemoveExtendBoneName`（`:770`）/ `HasExtendBoneName`（`:738`）を削除する
- `GetExtendBoneNames`（`:744`）は保存（`ToXml`）と Task 5 Step 3 の同期で使うため**残す**
- `ITimelineLayer.cs:52-53` の `OnBoneNameAdded` / `OnBoneNameRemoved` 宣言と、`TimelineLayerBase.cs:291-299` の既定実装、`MotionTimelineLayer` の override（`:376-390`）を削除する

Run: `grep -rn "HasExtendBoneName\|AddExtendBoneName\|RemoveExtendBoneName\|OnBoneNameAdded\|OnBoneNameRemoved" --include=*.cs source/`
Expected: ヒット 0 件

- [ ] **Step 4: ビルドとテスト**

Run: Task 1 Step 6 と同じ 3 コマンド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline
git commit -m "refactor(timeline): メイドアニメレイヤーの編集UIを各ウィンドウへ委譲する"
```

---

### Task 8: 実機検証と調査ドキュメントの更新

**Files:**
- Modify: `docs/layer-window-duplication-survey.md`

- [ ] **Step 1: 実機で動作を確認する**

ゲームを起動した状態で MCP `com3d25-devbridge` を使い、ホットリロード（CLAUDE.md の手順）または再起動で新ビルドを反映してから、次を順に確認する。確認できない項目があれば、その旨を記録して次のステップへ進まずユーザーへ報告する。

1. 体ボーン: Inspector・ギズモでポーズを変え、キー登録でその姿勢が入る
2. 拡張ボーン: ボーンウィンドウでスロットボーンにチェック → 0F へ自動キーが入り、タイムラインに項目が出る。チェックを外すとキーごと消える
3. IK: IK ウィンドウで固定 → 手足を動かしてキー登録 → 再生で固定位置が再現される
4. 接地: IK ウィンドウの接地パラメータを変えてキー登録 → 再生で反映される
5. isAnime: アニメトグル ON で、モーション再生中も固定が効く
6. isAnime ON のまま編集モードを抜けた直後: 固定が暴れない（`UpdateMaid` のドラッグ判定は編集モード中しか成立しない前提のコードなので、モード外を通すようになった影響を見る）
7. 再生開始 1 フレーム目の IK: 固定位置が現在のボーン位置へ飛ばない。`SetHold` は ON 化時に `resetRequested` を立て、直後の `SetTargetPosition` がそれを畳む前提のため、`ApplyMotion` が `MaidIKHoldController.LateUpdate` より先に走る必要がある。グリッチが出るならこの順序が崩れていないか（両者が別マネージャの Update / LateUpdate に属する）を疑う
8. 指: 指ウィンドウの「TL:ブレンド有効」がタイムライン保存へ載る（保存 → 読み込みで復帰する）
9. 既存タイムライン: 旧 `extendBoneNames` を持つファイルを開き、チェックなしでも項目が出て編集できる。IK キーを含むタイムラインを再生し、見た目が壊れていない

- [ ] **Step 2: 調査ドキュメントを更新する**

`docs/layer-window-duplication-survey.md` の B 分類の表で、`MotionTimelineLayer`「メイドアニメ」の編集タブ行を「**対象外**」から「対応済み」へ変える。対応内容は次の要点を含める。

- IK 固定・接地を SE の `MaidIKHoldController` へ一本化し、MTE の `IKHoldEntity` を撤去したこと
- 再生中の固定（旧 `isAnime`）は IK ウィンドウのアニメトグルへ移したこと
- 拡張ボーンの対象選択はボーンウィンドウのチェック（追跡ストア）へ移したこと

A 分類の「MotionTimelineLayer の手指/足指タブ」行の対応欄にある「ブレンド有効トグルは残置」の記述を、指ウィンドウへ移設済みに直す。

「削除方針の示唆」の 4 番（B 分類の残り 3 件が対象外）を、残り 2 件（サブカメラ / モデル系管理タブ）に直す。

- [ ] **Step 3: コミット**

```bash
git add docs/layer-window-duplication-survey.md
git commit -m "docs: メイドアニメレイヤーの委譲対応状況を調査ドキュメントへ反映する"
```

- [ ] **Step 4: コードレビュー**

`code-review` スキルでレビューし、指摘があれば対応する。

---

## レビュー却下メモ

- 調査ドキュメント更新時に現行文面と突き合わせる一手間を明記すべき（確信度: 低） — 却下。Task 8 Step 2 で書き換える行（B 分類の編集タブ行 / A 分類の「ブレンド有効トグルは残置」/ 削除方針の 4 番）を既に特定して指示済みのため
