# D-7 視線の向け先/注視先 概念統合 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (このリポジトリでは subagent-driven-development は使用禁止)

**Goal:** 視線の「どこを見るか」を `MaidLookMode` 1 本の語彙へ統合し、視線タブの「向け先」行をキー化 ON/OFF で入れ替わらない 1 行にする。保存形式(TimelineXml のキー)は変更しない。

**Architecture:** 統合列挙は SE の `MaidLookMode`(`メイド` を末尾追加)。キー化 ON の値は従来どおり `MaidCache.lookAtTarget*` に格納し、`MaidLookBridge` の双方向マップ(`ToLookMode` / `ToTargetType` / `GetSelectableModes`)が UI と格納表現を繋ぐ。メイドの注視ポイント解決は `MaidLookController.GetMaidPointTransform` へ一本化し、`MaidCache.GetPointTransform` はそこへ委譲する。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI 風 GUIView)、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** `docs/se-mte-state-duplication-survey.md`(「D-7 の設計メモ」節)

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree 禁止。作業はメイン作業ディレクトリの `feature/timeline-window` ブランチ
- ビルド確認(COM3D25 構成):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- ビルド確認(COM3D2 構成。対象フレームワークが違うため必ず両方通す):
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
  ```
- `debug.bat` はゲームフォルダへ DLL をコピーするため使わない。`deploy.bat` / `release.bat` / `deploy.ps1` は絶対に実行しない
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`(既存テストを退行させない)。テストは COM3D25 構成のビルド出力を参照するため、テスト実行前に必ず COM3D25 構成でビルドしておくこと
- 新規ファイルは作らない計画のため csproj の変更は不要
- テスト名前空間では `MTEP = COM3D2.MotionTimelineEditor.Plugin` の別名エイリアスを使う(既存テストと同じ)
- コミットフッター:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01AfYsdCoPDrgceZqdPfecRU
  ```

## データモデルの前提知識(全タスク共通)

- **統合後の対応表**(キー化 ON の格納先は変えない)

  | 統合列挙 `MaidLookMode` | キー化 OFF の実体 | キー化 ON の実体 (`MTEP.LookAtTargetType`) | ON の選択肢 |
  |---|---|---|---|
  | カメラ | `MaidLookController` | `Camera` | あり |
  | メイド(+ポイント) | 同上(本計画で新設) | `Maid` + `lookAtTargetIndex` + `lookAtMaidPointType` | あり |
  | 方向指定 | 同上(`lookX/Y`) | `None` + `lookDirection`(顔向きキー) | あり |
  | 無し | 同上 | `None`(そらし時に自動フォールバック) | **なし** |
  | マウス | 同上 | — | なし |
  | オブジェクト(任意 Transform) | 同上 | — | なし |

  「無し」をキー化 ON の選択肢に入れないのは、`None` が「方向指定」と衝突するため(そらし時のフォールバックは `ResolveLookMode` が自動で行う。現行どおり)。ON で選べない値(無し/マウス/オブジェクト)は ON 表示時に「方向指定」へ丸め、OFF に戻せば SE 側の設定がそのまま残る(現行挙動と同じ)。
- `MTEP.LookAtTargetType.Model` は `StudioModelManager` 未移植のため選択肢に出さない(現行どおり)。既存データに残っている場合の丸め先も「方向指定」
- 向け先(`TBody.trsLookTarget`)の書き手は `MaidLookController` のみ(A-1 で統合済み)。MTE 側は `Timeline/MaidLookBridge.cs` 経由で駆動する
- SE 側の呼出済みメイド一覧は `MTEUtils.GetReadyMaidList()`(`List<Maid>`、null 要素なし。`MaidWindowBase.DrawMaidSelector` と同じ)。MTE 側は `MTEP.MaidManager.instance.maidCaches`
- `GUIComboBox<T>.defaultName` は `currentIndex` が -1 のときのボタン文字列。未選択を先頭要素へ丸めない(`MaidWindowBase.cs:191-218` の既存パターン)
- `MTEP.MaidPointType`(`Timeline/MaidCache.cs:16`)は 顔/胸/股/尻/中心。表示名は `MTEP.MaidCache.GetMaidPointTypeName(type)`
- `GUIComboBox<T>.items` は `List<T>`(`MTEUtils/GUIComboBox.cs:39`)
- `MaidLookController` は既に MTE 型を参照していないが、`MaidManipulation/` の他ファイル(`MaidFingerBlendController` 等)は `MTEP` を参照している。本計画で `MTEP.MaidPointType` を使うのは既存の依存方針の範囲内

---

## Task 1: 統合列挙と双方向マップ(純粋ロジック)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs:7-21`(`MaidLookMode` に `メイド` を追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs`(マップ 3 つを追加)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs`

**Interfaces:**
- Consumes: `MTEP.LookAtTargetType`(None/Camera/Maid/Model)
- Produces:
  - `MaidLookMode.メイド`(列挙の末尾)
  - `MaidLookBridge.ToLookMode(MTEP.LookAtTargetType) : MaidLookMode`
  - `MaidLookBridge.ToTargetType(MaidLookMode) : MTEP.LookAtTargetType`
  - `MaidLookBridge.GetSelectableModes(bool useHeadKey) : List<MaidLookMode>`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs` のクラス末尾(最後の `}` の 2 つ手前)へ追記する:

```csharp
        [Theory]
        [InlineData(MTEP.LookAtTargetType.None, MaidLookMode.方向指定)]
        [InlineData(MTEP.LookAtTargetType.Camera, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Maid, MaidLookMode.メイド)]
        // モデル注視は選択肢に出さないため、表示上は方向指定へ丸める
        [InlineData(MTEP.LookAtTargetType.Model, MaidLookMode.方向指定)]
        public void ToLookMode_キーの注視先種別を統合列挙へ写す(
            MTEP.LookAtTargetType targetType, MaidLookMode expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToLookMode(targetType));
        }

        [Theory]
        [InlineData(MaidLookMode.カメラ, MTEP.LookAtTargetType.Camera)]
        [InlineData(MaidLookMode.メイド, MTEP.LookAtTargetType.Maid)]
        [InlineData(MaidLookMode.方向指定, MTEP.LookAtTargetType.None)]
        // キー化できない値は顔向きキーで駆動する None へ丸める
        [InlineData(MaidLookMode.無し, MTEP.LookAtTargetType.None)]
        [InlineData(MaidLookMode.マウス, MTEP.LookAtTargetType.None)]
        [InlineData(MaidLookMode.オブジェクト, MTEP.LookAtTargetType.None)]
        public void ToTargetType_統合列挙をキーの注視先種別へ写す(
            MaidLookMode mode, MTEP.LookAtTargetType expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToTargetType(mode));
        }

        [Fact]
        public void GetSelectableModes_キー化中はキー化できる値だけを出す()
        {
            Assert.Equal(
                new[] { MaidLookMode.カメラ, MaidLookMode.メイド, MaidLookMode.方向指定 },
                MaidLookBridge.GetSelectableModes(true));
        }

        [Fact]
        public void GetSelectableModes_キー化していなければ全ての向け先を出す()
        {
            Assert.Equal(
                new[]
                {
                    MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
                    MaidLookMode.メイド, MaidLookMode.オブジェクト, MaidLookMode.無し,
                },
                MaidLookBridge.GetSelectableModes(false));
        }

        [Fact]
        public void GetSelectableModes_返したリストを書き換えても次の呼び出しに影響しない()
        {
            var modes = MaidLookBridge.GetSelectableModes(true);
            modes.Clear();
            Assert.Equal(3, MaidLookBridge.GetSelectableModes(true).Count);
        }
```

- [ ] **Step 2: テストが落ちることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー(`MaidLookMode.メイド` / `ToLookMode` / `ToTargetType` / `GetSelectableModes` が存在しない)

- [ ] **Step 3: 列挙値を追加する**

`MaidManipulation/MaidLookController.cs` の `MaidLookMode` 末尾(`無し,` の直後)へ:

```csharp
        /// <summary>
        /// 他のメイドの部位 (顔・胸・股・尻・中心) を見る。
        /// タイムラインの注視先「メイド」と同じ概念で、キー化の有無に関わらず選べる
        /// (対象の Transform はボディ読み直しで破棄されるため、適用のたびに引き直す)。
        /// 既存プリセットの互換のため末尾に足す (列挙値のずれを避ける)
        /// </summary>
        メイド,
```

- [ ] **Step 4: 双方向マップを実装する**

`Timeline/MaidLookBridge.cs` の先頭に `using System.Collections.Generic;` を足し、`ResolveLookMode` の直前へ:

```csharp
        /// <summary>キー化中に選べる向け先。「無し」は None が方向指定と衝突するため出さない</summary>
        private static readonly MaidLookMode[] KeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.メイド, MaidLookMode.方向指定,
        };

        /// <summary>キー化していないときに選べる向け先 (SE の全モード)</summary>
        private static readonly MaidLookMode[] UnkeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
            MaidLookMode.メイド, MaidLookMode.オブジェクト, MaidLookMode.無し,
        };

        /// <summary>
        /// 向け先の選択肢。キー化の有無で選べる値だけが変わり、語彙は共通にする。
        /// コンボボックスへ渡す List を呼び出し側が持ち回るため、毎回複製して返す
        /// </summary>
        public static List<MaidLookMode> GetSelectableModes(bool useHeadKey)
        {
            return new List<MaidLookMode>(useHeadKey ? KeyedModes : UnkeyedModes);
        }

        /// <summary>
        /// キーの注視先種別を統合列挙へ写す (UI 表示用)。
        /// モデル注視は選択肢に出していないため方向指定へ丸める
        /// (StudioModelManager 未移植。TimelineLookRowDrawer の除外と揃える)
        /// </summary>
        public static MaidLookMode ToLookMode(MTEP.LookAtTargetType targetType)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Maid:
                    return MaidLookMode.メイド;
                default:
                    return MaidLookMode.方向指定;
            }
        }

        /// <summary>
        /// 統合列挙をキーの注視先種別へ写す。
        /// キー化できない値 (マウス・任意オブジェクト・無し) は、顔向きキーで駆動する
        /// None へ丸める (選択肢には出さないが、外部から渡っても壊れないようにする)
        /// </summary>
        public static MTEP.LookAtTargetType ToTargetType(MaidLookMode mode)
        {
            switch (mode)
            {
                case MaidLookMode.カメラ:
                    return MTEP.LookAtTargetType.Camera;
                case MaidLookMode.メイド:
                    return MTEP.LookAtTargetType.Maid;
                default:
                    return MTEP.LookAtTargetType.None;
            }
        }
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS(既存テストも含めて緑)

- [ ] **Step 6: 2 構成でビルドする**

Run: Global Constraints の COM3D25 / COM3D2 の MSBuild コマンド 2 本
Expected: どちらも成功

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs
git commit -m "refactor(look): 視線の向け先とタイムラインの注視先を 1 つの語彙へ写す"
```

---

## Task 2: メイド注視を SE のコントローラへ実装し、ポイント解決を一本化する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs`(Entry / セッター / `SetState` / `ResolveLookTarget` / 静的ヘルパー)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs:47-60`(`ResolveLookMode` の Maid 分岐)、`ApplyLookMode`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:546-566`(`UpdateLookAtTarget`)、`:803-830`(`GetPointTransform`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs`

**Interfaces:**
- Consumes: `MaidLookMode.メイド`(Task 1)
- Produces:
  - `MaidLookController.GetTargetMaid(Maid) : Maid`
  - `MaidLookController.GetMaidPointType(Maid) : MTEP.MaidPointType`
  - `MaidLookController.SetMaidTarget(Maid maid, Maid targetMaid, MTEP.MaidPointType pointType) : void`
  - `MaidLookController.SetState(Maid, MaidLookMode, float lookX, float lookY, Transform target, Maid targetMaid, MTEP.MaidPointType maidPointType) : void`(既存 5 引数版は残す)
  - `MaidLookController.GetMaidPointTransform(Maid, MTEP.MaidPointType) : Transform`(static)
  - `MaidLookBridge.ApplyLookMode(Maid, MaidLookMode, Transform, Vector2, Maid targetMaid, MTEP.MaidPointType maidPointType) : void`

- [ ] **Step 1: 失敗するテストを書く**

`MaidLookBridgeTests.cs` の `ResolveLookMode_キー化中は注視先種別を向け先モードへ写す` の `[InlineData]` を差し替える(メイドは統合列挙の `メイド` になる):

```csharp
        [Theory]
        [InlineData(MTEP.LookAtTargetType.Camera, true, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Camera, false, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Maid, true, MaidLookMode.メイド)]
        [InlineData(MTEP.LookAtTargetType.Model, true, MaidLookMode.オブジェクト)]
        [InlineData(MTEP.LookAtTargetType.None, false, MaidLookMode.方向指定)]
        public void ResolveLookMode_キー化中は注視先種別を向け先モードへ写す(
            MTEP.LookAtTargetType targetType, bool hasTarget, MaidLookMode expected)
        {
            var mode = MaidLookBridge.ResolveLookMode(
                true, targetType, hasTarget, isEyeSorashi: false);
            Assert.Equal(expected, mode);
        }
```

- [ ] **Step 2: テストが落ちることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ResolveLookMode_キー化中は注視先種別を向け先モードへ写す`
Expected: FAIL(`Maid` の期待値が `オブジェクト` のままのため `メイド` と一致しない)

- [ ] **Step 3: `ResolveLookMode` のメイド分岐を分ける**

`Timeline/MaidLookBridge.cs` の `switch (targetType)` を:

```csharp
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Maid:
                    // メイド注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.メイド;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                case MTEP.LookAtTargetType.Model:
                    // モデルは SE 側に対応する概念が無いため任意オブジェクトとして扱う
                    if (hasTarget)
                    {
                        return MaidLookMode.オブジェクト;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                default:
                    return ResolveNoTargetMode(isEyeSorashi);
            }
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ResolveLookMode_キー化中は注視先種別を向け先モードへ写す`
Expected: PASS

- [ ] **Step 5: コントローラにメイド注視の状態を持たせる**

`MaidManipulation/MaidLookController.cs`:

ファイル先頭の using へ `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` を追加。

`Entry` クラスへ:

```csharp
            /// <summary>メイドモードの注視対象。Transform はボディ読み直しで破棄されるため保持しない</summary>
            public Maid targetMaid;

            /// <summary>メイドモードで見る部位</summary>
            public MTEP.MaidPointType maidPointType = MTEP.MaidPointType.Head;
```

`SetTarget` の直後へアクセサを追加:

```csharp
        public Maid GetTargetMaid(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.targetMaid : null;
        }

        public MTEP.MaidPointType GetMaidPointType(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.maidPointType : MTEP.MaidPointType.Head;
        }

        /// <summary>メイドモードの注視対象と部位。対象と部位は必ず対で変わるためまとめて受ける</summary>
        public void SetMaidTarget(Maid maid, Maid targetMaid, MTEP.MaidPointType pointType)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.targetMaid = targetMaid;
            entry.maidPointType = pointType;
            Apply(maid);
        }
```

- [ ] **Step 6: `SetState` を拡張し、向け先の解決へメイドモードを足す**

同ファイルの既存 `SetState` を次の 2 つに置き換える(5 引数版は既存の呼び出し元のために残す):

```csharp
        /// <summary>メイド注視の指定を変えずに状態を差し替える (従来の呼び出し向け)</summary>
        public void SetState(Maid maid, MaidLookMode mode, float lookX, float lookY, Transform target)
        {
            SetState(maid, mode, lookX, lookY, target,
                GetTargetMaid(maid), GetMaidPointType(maid));
        }

        /// <summary>
        /// 状態をまとめて差し替える。個別セッターを重ねると Apply が状態ごとに走り、
        /// 途中の中途半端な組み合わせで注視点を計算してしまうため、
        /// Undo・プリセット復元のような一括復元はこちらを使う
        /// </summary>
        public void SetState(
            Maid maid, MaidLookMode mode, float lookX, float lookY, Transform target,
            Maid targetMaid, MTEP.MaidPointType maidPointType)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.mode = mode;
            entry.lookX = lookX;
            entry.lookY = lookY;
            entry.target = target;
            entry.targetMaid = targetMaid;
            entry.maidPointType = maidPointType;
            Apply(maid);
        }
```

`ResolveLookTarget` の `if (entry.mode == MaidLookMode.オブジェクト && entry.target != null)` の**直前**へ:

```csharp
            if (entry.mode == MaidLookMode.メイド)
            {
                // 対象が退去・未設定なら方向指定の注視点で代用する (オブジェクトモードと同じ扱い)
                var point = GetMaidPointTransform(entry.targetMaid, entry.maidPointType);
                if (point != null)
                {
                    return point;
                }
            }
```

同ファイルの末尾側(`GetHeadBone` の直前)へ静的ヘルパーを追加:

```csharp
        /// <summary>
        /// メイドの注視ポイントを引く。
        /// タイムラインの MaidCache.GetPointTransform もここへ委譲し、
        /// 「メイドのどこを見るか」の解決を SE 側の 1 か所に保つ
        /// </summary>
        public static Transform GetMaidPointTransform(Maid maid, MTEP.MaidPointType type)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }

            switch (type)
            {
                case MTEP.MaidPointType.Head:
                    return maid.body0.trsHead;
                case MTEP.MaidPointType.Chest:
                    return maid.body0.Spine1a;
                case MTEP.MaidPointType.Crotch:
                    return maid.body0.Pelvis;
                case MTEP.MaidPointType.Hip:
                    return maid.body0.Hip_R;
                case MTEP.MaidPointType.Bip01:
                    return maid.body0.trBip;
                default:
                    return null;
            }
        }
```

- [ ] **Step 7: ブリッジからメイド指定を流す**

`Timeline/MaidLookBridge.cs` の `ApplyLookMode` を差し替える:

```csharp
        /// <summary>
        /// 向け先モードを SE のコントローラへ反映する。
        /// モードに関係しない指定 (注視対象・メイド指定・顔向き) は
        /// SE が覚えている値を残す (タイムライン側の都合で消さないため)。
        /// 方向指定のときだけ顔向きキーの指定値で lookX/lookY を駆動する
        /// </summary>
        /// <param name="lookDirection">顔向きキーの指定値 (lookX/lookY、-1〜1)</param>
        /// <param name="targetMaid">注視先がメイドのときの対象</param>
        /// <param name="maidPointType">注視先がメイドのときの部位</param>
        public static void ApplyLookMode(
            Maid maid, MaidLookMode mode, Transform target, Vector2 lookDirection,
            Maid targetMaid, MTEP.MaidPointType maidPointType)
        {
            if (maid == null)
            {
                return;
            }

            var controller = lookController;
            var isDirection = mode == MaidLookMode.方向指定;
            var isMaid = mode == MaidLookMode.メイド;
            controller.SetState(
                maid,
                mode,
                isDirection ? lookDirection.x : controller.GetLookX(maid),
                isDirection ? lookDirection.y : controller.GetLookY(maid),
                mode == MaidLookMode.オブジェクト ? target : controller.GetTarget(maid),
                isMaid ? targetMaid : controller.GetTargetMaid(maid),
                isMaid ? maidPointType : controller.GetMaidPointType(maid));
        }
```

- [ ] **Step 8: MaidCache の適用経路とポイント解決を繋ぎ替える**

`Timeline/MaidCache.cs` の `UpdateLookAtTarget` の適用部分を差し替える:

```csharp
            var lookAtTarget = GetLookAtTarget();
            var lookMode = SEP.MaidLookBridge.ResolveLookMode(
                timeline.useHeadKey, lookAtTargetType, lookAtTarget != null,
                SEP.MaidLookBridge.IsEyeSorashi(timeline.eyeMoveType));
            if (lookMode == null)
            {
                return;
            }

            // メイド注視は Transform ではなく「対象メイド + 部位」で渡し、
            // 実際の Transform は SE のコントローラが適用のたびに引き直す
            var targetMaidCache = lookAtTargetType == LookAtTargetType.Maid
                ? maidManager.GetMaidCache(lookAtTargetIndex) : null;
            var targetMaid = targetMaidCache != null ? targetMaidCache.maid : null;

            SEP.MaidLookBridge.ApplyLookMode(
                maid, lookMode.Value, lookAtTarget, _lookDirection,
                targetMaid, lookAtMaidPointType);
```

同ファイルの `GetPointTransform` を委譲へ差し替える:

```csharp
        /// <summary>
        /// メイドの注視ポイント。解決の実装は SE の MaidLookController に一本化しているため、
        /// ここでは委譲だけを行う (キー化の有無で同じ部位を指すようにするため)
        /// </summary>
        public Transform GetPointTransform(MaidPointType type)
        {
            return SEP.MaidLookController.GetMaidPointTransform(maid, type);
        }
```

- [ ] **Step 9: テストと 2 構成ビルドを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS

Run: Global Constraints の MSBuild コマンド 2 本
Expected: どちらも成功

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs
git commit -m "feat(look): SE の向け先にメイド注視を足しポイント解決を一本化する"
```

---

## Task 3: 視線タブの「向け先」行を 1 本に畳む

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs`(注視先コンボを統合列挙へ)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs:80-88`(`LOOK_MODES` 撤去)、`:359-380`(`DrawLookTargetRows`)、`:421-440`(メイド行の追加)
- Test: なし(UI 描画のため実機確認項目で担保する。選択肢の決定ロジックは Task 1 でテスト済み)

**Interfaces:**
- Consumes: `MaidLookBridge.GetSelectableModes` / `ToLookMode` / `ToTargetType`、`MaidLookController.SetMaidTarget` / `GetTargetMaid` / `GetMaidPointType`、`MTEUtils.GetReadyMaidList()`
- Produces: `TimelineLookRowDrawer.DrawLookAtTargetRows(GUIView, MTEP.MaidCache, float labelWidth, float rowHeight)`(シグネチャ据え置き。中身をキー化中の統合行へ変更)

- [ ] **Step 1: キー化中の行を統合列挙のコンボに差し替える**

`TimelineLookRowDrawer.cs` の `LookAtTargetTypes` フィールドと `_targetTypeComboBox` を次で置き換える(`System` / `System.Linq` の using が未使用になる場合は削除する):

```csharp
        /// <summary>
        /// キー化中の向け先コンボ。SE の向け先と同じ語彙 (MaidLookMode) を使い、
        /// 書き込み先だけが MaidCache のキー指定値になる
        /// </summary>
        private readonly GUIComboBox<MaidLookMode> _lookModeComboBox =
            new GUIComboBox<MaidLookMode>
            {
                getName = (mode, _) => mode.ToString(),
            };
```

`DrawLookAtTargetRows` を差し替える:

```csharp
        /// <summary>
        /// キー化中の向け先の行 (対象がメイドのときはメイド・ポイントの行も続けて出す)。
        /// キー化していないときの SE の「向け先」行と同じ語彙・同じラベルにして、
        /// キー化の切り替えで行が入れ替わらないようにする
        /// </summary>
        public void DrawLookAtTargetRows(
            GUIView view, MTEP.MaidCache maidCache, float labelWidth, float rowHeight)
        {
            var modes = MaidLookBridge.GetSelectableModes(true);
            // 選択肢に無い値 (モデル注視等) は ToLookMode が方向指定へ丸める
            var mode = MaidLookBridge.ToLookMode(maidCache.lookAtTargetType);

            _lookModeComboBox.items = modes;
            _lookModeComboBox.currentIndex = modes.IndexOf(mode);
            _lookModeComboBox.onSelected =
                (newMode, _) => maidCache.lookAtTargetType = MaidLookBridge.ToTargetType(newMode);
            LabeledComboRow.Draw(view, "向け先", _lookModeComboBox, labelWidth, rowHeight);

            if (_lookModeComboBox.currentItem != MaidLookMode.メイド)
            {
                return;
            }

            _targetMaidComboBox.items = MTEP.MaidManager.instance.maidCaches;
            _targetMaidComboBox.currentIndex = maidCache.lookAtTargetIndex;
            _targetMaidComboBox.onSelected =
                (_, index) => maidCache.lookAtTargetIndex = index;
            LabeledComboRow.Draw(view, "メイド", _targetMaidComboBox, labelWidth, rowHeight);

            _targetMaidPointComboBox.currentIndex = (int) maidCache.lookAtMaidPointType;
            _targetMaidPointComboBox.onSelected =
                (type, _) => maidCache.lookAtMaidPointType = type;
            LabeledComboRow.Draw(view, "ポイント", _targetMaidPointComboBox, labelWidth, rowHeight);
        }
```

`DrawLookDirectionRows` の活性条件のコメントを「注視先が手動のとき」から「向け先が方向指定のとき」へ直す(判定式 `maidCache.lookAtTargetType == MTEP.LookAtTargetType.None` は変更しない)。

- [ ] **Step 2: 表情ウィンドウの向け先行を 1 本にする**

`MaidFaceWindow.cs` の静的リスト `LOOK_MODES`(`:81-88`)を削除し、`DrawLookTargetRows` を差し替える:

```csharp
        /// <summary>
        /// 向け先の行。キー化の有無で選択肢と書き込み先だけが変わり、
        /// 行の構成 (向け先 → 対象の指定) は変えない
        /// </summary>
        private void DrawLookTargetRows(
            GUIView view, Maid target, MaidLookMode mode, bool isKeyed)
        {
            if (isKeyed)
            {
                var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
                if (maidCache == null)
                {
                    view.DrawLabel("タイムライン側の対象メイドが見つかりません",
                        -1, ROW_HEIGHT, textColor: Color.yellow);
                    return;
                }
                _timelineLookRowDrawer.DrawLookAtTargetRows(
                    view, maidCache, LABEL_WIDTH, ROW_HEIGHT);
                return;
            }

            _lookModeComboBox.items = MaidLookBridge.GetSelectableModes(false);
            _lookModeComboBox.currentIndex = _lookModeComboBox.items.IndexOf(mode);
            _lookModeComboBox.onSelected = (newMode, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "視線の向け先");
                lookController.SetMode(target, newMode);
            };
            DrawLabeledComboBox("向け先", _lookModeComboBox);

            // 対象の指定はキー化中の行 (向け先の直下にメイド・ポイントが続く) と並びを揃える
            if (mode == MaidLookMode.メイド)
            {
                DrawLookMaidRows(view, target);
            }
            else if (mode == MaidLookMode.オブジェクト)
            {
                DrawLookObjectRows(view, target);
            }
        }
```

あわせて `DrawLookContent`(`:295-304`)から末尾の `オブジェクト` 分岐を削除する(対象指定行は `DrawLookTargetRows` 側へ寄せたため):

```csharp
            DrawLookTargetRows(view, target, mode, isKeyed);
            DrawEyeMoveTypeRow(view, timeline);
            DrawHeadToCamRow(view, target);
            DrawLookDirectionSliders(view, target, mode, isKeyed);

            if (isKeyed)
            {
                DrawKeyedLookResetRow(view, target);
            }
        }
```

`DrawLookObjectRows` の先頭にある `view.AddSpace(5);` は、向け先の直下へ移ったため削除する。

- [ ] **Step 3: キー化していないときのメイド指定行を足す**

`MaidFaceWindow.cs` の `DrawLookObjectRows` の直前へメソッドを追加し、コンボ 2 つのフィールドは `_lookModeComboBox` の宣言(`:49`)の直後へ置く:

```csharp
        /// <summary>メイドモードの注視対象。呼出済みメイドから選ぶ</summary>
        private readonly GUIComboBox<Maid> _lookMaidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid.status.fullNameJpStyle,
        };

        /// <summary>メイドモードで見る部位。タイムラインのキーと同じ列挙を使う</summary>
        private readonly GUIComboBox<MTEP.MaidPointType> _lookMaidPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = new List<MTEP.MaidPointType>((MTEP.MaidPointType[])
                    Enum.GetValues(typeof(MTEP.MaidPointType))),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
            };
```

メソッド本体:

```csharp
        /// <summary>メイドモードの注視対象と部位。キー化中は同じ行を TimelineLookRowDrawer が描く</summary>
        private void DrawLookMaidRows(GUIView view, Maid target)
        {
            var maids = MTEUtils.GetReadyMaidList();
            var targetMaid = lookController.GetTargetMaid(target);

            _lookMaidComboBox.items = maids;
            // 未選択のときは currentIndex が -1 になりボタン文字列が決まらないため、既定名で埋める
            // (先頭のメイドへ丸めると、選んでいないメイドが選択済みに見えてしまう)
            _lookMaidComboBox.defaultName = targetMaid == null ? "未選択" : null;
            _lookMaidComboBox.currentIndex = maids.IndexOf(targetMaid);
            _lookMaidComboBox.onSelected = (selected, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetMaidTarget(
                    target, selected, lookController.GetMaidPointType(target));
            };
            DrawLabeledComboBox("メイド", _lookMaidComboBox);

            _lookMaidPointComboBox.currentIndex = (int) lookController.GetMaidPointType(target);
            _lookMaidPointComboBox.onSelected = (pointType, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視ポイントの指定");
                lookController.SetMaidTarget(
                    target, lookController.GetTargetMaid(target), pointType);
            };
            DrawLabeledComboBox("ポイント", _lookMaidPointComboBox);
        }
```

`MTEUtils.GetReadyMaidList()` は `MaidWindowBase.DrawMaidSelector` と同じ呼出済みメイド一覧(`List<Maid>`、null 要素なし)。`using System;` / `using System.Collections.Generic;` が未追加なら足す。

- [ ] **Step 4: 2 構成でビルドする**

Run: Global Constraints の MSBuild コマンド 2 本
Expected: どちらも成功

- [ ] **Step 5: テストが退行していないことを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs
git commit -m "refactor(ui): 視線タブの向け先と注視先を 1 行へ統合する"
```

---

## Task 4: 履歴(Undo)とシーンプリセット v27 への対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs:19-22`(フィールド)、`:59-63`(記録)、`:194-195`(復元)、`:236-242`(差分判定)
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs:448-514`(`ScenePresetLook`)、`:700-714`(バージョン)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs:1195-1235`(`CaptureLook`)、`:1670-1700`(`ApplyLook`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs`

**Interfaces:**
- Consumes: `MaidLookController.GetTargetMaid` / `GetMaidPointType` / 7 引数 `SetState`(Task 2)
- Produces: `ScenePresetLook.maidPointType`(`MTEP.MaidPointType` の名前。空なら未記録)

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetLookXmlTests.cs` へ追記する:

```csharp
        [Fact]
        public void メイド注視の対象と部位が往復で保たれる()
        {
            var restored = RoundTrip(new ScenePresetLook
            {
                mode = "メイド",
                targetMaidGuid = "guid-1234",
                maidPointType = "Chest",
            });

            Assert.Equal("メイド", restored.mode);
            Assert.Equal("guid-1234", restored.targetMaidGuid);
            Assert.Equal("Chest", restored.maidPointType);
        }

        [Fact]
        public void メイド注視を持たない旧プリセットは部位が未記録になる()
        {
            const string oldPreset =
                "<?xml version=\"1.0\"?>"
                + "<ScenePresetLook mode=\"カメラ\">"
                + "<lookX>0</lookX><lookY>0</lookY>"
                + "</ScenePresetLook>";

            using (var reader = new StringReader(oldPreset))
            {
                var look = (ScenePresetLook) Serializer.Deserialize(reader);
                Assert.Null(look.maidPointType);
            }
        }
```

- [ ] **Step 2: テストが落ちることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetLookXmlTests`
Expected: コンパイルエラー(`ScenePresetLook.maidPointType` が存在しない)

- [ ] **Step 3: プリセットのスキーマを足す**

`ScenePresetData.cs` の `ScenePresetLook` の `targetPath` の直後へ:

```csharp
        /// <summary>
        /// 向け先がメイドのときの部位 (MaidPointType の名前)。
        /// 対象メイドは targetMaidGuid を使い回す。
        /// v26 以前や他モードでは null になり、適用時に部位へ触らない
        /// </summary>
        [XmlAttribute]
        public string maidPointType;
```

同ファイルのバージョン定義へ 1 行足し、`CurrentVersion` を 27 にする:

```csharp
        // v27: look に maidPointType（向け先「メイド」の部位）を追加。
        //      対象メイドは既存の targetMaidGuid を使い回す。
        //      旧形式は maidPointType が null = 未記録として読み飛ばす
        public static readonly int CurrentVersion = 27;
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter ScenePresetLookXmlTests`
Expected: PASS

- [ ] **Step 5: プリセットの記録・復元をメイドモードへ対応させる**

`Manager/ScenePresetManager.cs` の `CaptureLook` の注視対象を記録する箇所を差し替える:

```csharp
            var target = controller.GetTarget(maid);
            if (mode == MaidLookMode.オブジェクト && target != null)
            {
                var ownerMaid = FindOwnerMaid(target);
                if (ownerMaid != null)
                {
                    look.targetMaidGuid = GetGuid(ownerMaid);
                    look.targetBone = target.name;
                }
                else
                {
                    look.targetPath = GetScenePath(target);
                }
            }
            else if (mode == MaidLookMode.メイド)
            {
                // メイド注視は「対象メイド + 部位」で持つ。ボーン名は復元時に部位から引き直す
                var targetMaid = controller.GetTargetMaid(maid);
                if (targetMaid != null)
                {
                    look.targetMaidGuid = GetGuid(targetMaid);
                    look.maidPointType = controller.GetMaidPointType(maid).ToString();
                }
            }
```

`mode` は新設のローカル変数(`MaidLookMode` 型)なので、`CaptureLook` 冒頭のオブジェクト初期化子も次の形へ直し、`controller.GetMode(maid)` の呼び出しをこの変数へ寄せる(初期化子の `mode = ...` はプロパティ名なのでローカル変数と衝突しない):

```csharp
            var controller = maidManager.lookController;
            var mode = controller.GetMode(maid);
            var look = new ScenePresetLook
            {
                mode = mode.ToString(),
                lookX = controller.GetLookX(maid),
                lookY = controller.GetLookY(maid),
            };
```

その上で、注視対象を記録する既存ブロック(`var target = controller.GetTarget(maid);` 以降)を上記の分岐へ差し替える。

`ApplyLook` の復元部分を差し替える:

```csharp
            var target = mode == MaidLookMode.オブジェクト ? ResolveLookTarget(look) : null;
            if (mode == MaidLookMode.オブジェクト && target == null)
            {
                MTEUtils.LogWarning("注視対象が見つからないため方向指定で復元します: {0}",
                    look.targetPath ?? look.targetBone);
                mode = MaidLookMode.方向指定;
            }

            Maid targetMaid = null;
            var maidPointType = MTEP.MaidPointType.Head;
            if (mode == MaidLookMode.メイド)
            {
                targetMaid = !string.IsNullOrEmpty(look.targetMaidGuid)
                    ? FindMaidBySlotGuid(look.targetMaidGuid) : null;
                // XML は外部入力のため、未知の部位名は既定 (顔) のままにする
                if (!TryParseEnum(look.maidPointType, out maidPointType))
                {
                    maidPointType = MTEP.MaidPointType.Head;
                }
                if (targetMaid == null)
                {
                    MTEUtils.LogWarning("注視対象のメイドが見つからないため方向指定で復元します: {0}",
                        look.targetMaidGuid);
                    mode = MaidLookMode.方向指定;
                }
            }

            maidManager.lookController.SetState(
                maid, mode, look.lookX, look.lookY, target, targetMaid, maidPointType);
```

- [ ] **Step 6: Undo でメイド注視が戻るようにする**

`Manager/History/PoseSnapshot.cs` の `_lookTarget` フィールドの直後へ:

```csharp
        private Maid _lookTargetMaid;
        private MTEP.MaidPointType _lookMaidPointType;
```

(ファイル先頭に `using MTEP = COM3D2.MotionTimelineEditor.Plugin;` が無ければ足す)

`Capture` の視線記録へ:

```csharp
            snapshot._lookTargetMaid = lookController.GetTargetMaid(maid);
            snapshot._lookMaidPointType = lookController.GetMaidPointType(maid);
```

復元(`:194`)を 7 引数版へ:

```csharp
            MaidManipulateManager.instance.lookController.SetState(
                maid, _lookMode, _lookX, _lookY, _lookTarget,
                _lookTargetMaid, _lookMaidPointType);
```

差分判定(`:238-241` の条件列)へ 2 行足す:

```csharp
                || _lookTargetMaid != o._lookTargetMaid
                || _lookMaidPointType != o._lookMaidPointType
```

- [ ] **Step 7: テストと 2 構成ビルドを通す**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS

Run: Global Constraints の MSBuild コマンド 2 本
Expected: どちらも成功

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs
git commit -m "feat(preset): メイド注視を履歴とシーンプリセット (v27) へ記録する"
```

---

## Task 5: 調査ドキュメントの更新

**Files:**
- Modify: `docs/se-mte-state-duplication-survey.md`(D 分類の表 D-7 行、チェックリスト `:139`、「D-7 の設計メモ」節、「実機確認項目」節)

**Interfaces:**
- Consumes: Task 1〜4 の実装結果

- [ ] **Step 1: D-7 の状態を完了へ更新する**

- チェックリストの D-7 を `- [x]` にし、末尾を「**統合済み**(下記「D-7 の実装メモ」参照)」へ変更する
- 「### D-7 の設計メモ(…設計確定/実装待ち)」の見出しを「### D-7 の実装メモ(視線の向け先/注視先の概念統合)」へ変更し、冒頭の「実装は未着手」を実装結果の記述へ差し替える(統合列挙・双方向マップ・UI 1 行・ポイント解決の一本化・プリセット v27 の 5 点)
- D 分類の表(`:62`)の D-7 行の判定欄末尾へ「→ 統合済み(実装メモ参照)」を追記する
- 「優先度の所感」(`:66`)の D-7 に関する記述を、統合済みである旨へ更新する

- [ ] **Step 2: 実機確認項目を追記する**

「### 実機確認項目(loop 中に追記)」の先頭へ:

```markdown
- D-7: 視線タブでキー化 ON/OFF を切り替えても「向け先」行のラベルと語彙が変わらず、行数が飛ばないこと
- D-7: キー化 OFF で向け先「メイド」を選び、対象メイドと部位(顔/胸/股/尻/中心)を切り替えると視線が追従すること
- D-7: キー化 ON で向け先「メイド」を選んだときの見た目が、OFF で同じメイド・同じ部位を選んだときと一致すること
- D-7: キー化 OFF でマウス/オブジェクトを選んだ状態でキー化 ON にすると「方向指定」表示になり、OFF に戻すと元の選択が残っていること
- D-7: 向け先「メイド」で対象メイドを退去させると方向指定の注視点へ落ち、例外が出ないこと
- D-7: 向け先「メイド」のままシーンプリセットを保存 → ロードで対象メイドと部位が戻ること。v26 以前のプリセットをロードしても視線まわりで例外・値の飛びが出ないこと
- D-7: 向け先「メイド」で対象・部位を変えた直後に Ctrl+Z すると、変更前の対象・部位へ戻ること
- D-7: 瞳レイヤーの項目表示(EyesItemInspector)の注視先行が視線タブと同じ「向け先」語彙になっていること
```

- [ ] **Step 3: コミット**

```bash
git add docs/se-mte-state-duplication-survey.md
git commit -m "docs(survey): D-7 視線概念統合の実装結果を記録する"
```

---

## レビュー却下メモ

(plan-review で取り込まなかった指摘。今回は 3 件すべて取り込んだため該当なし)
