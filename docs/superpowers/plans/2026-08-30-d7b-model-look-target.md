# D-7 追補: 視線の向け先へモデル注視を足す Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (このリポジトリでは subagent-driven-development は使用禁止)

**Goal:** 視線の「向け先」にモデル注視を足し、キー化 ON/OFF のどちらでもスタジオモデルを注視先に選べるようにする。あわせて「`StudioModelManager` 未移植」という事実と違う根拠を取り除く。

**Architecture:** D-7 で作った統合列挙 `MaidLookMode` の末尾へ `モデル` を足し、`MaidLookBridge` の双方向マップを `LookAtTargetType.Model` へ繋ぐ。SE 側は `StudioModelStat` の実体ではなく**モデル名 (`StudioModelStat.name`)** を保持し、`MaidLookController` が適用のたびに `StudioModelManager.GetModel(name).transform` を引き直す(モデルはシーン再構築で作り直されるため、名前が唯一の安定した同定手段)。保存形式(TimelineXml のキー)は変更しない。

**Tech Stack:** C# (.NET Framework 4.8 / Unity IMGUI 風 GUIView)、xUnit (`source/COM3D2.SceneEditor.Plugin.Tests`)

**Spec:** `docs/se-mte-state-duplication-survey.md`(「D-7 の実装メモ」節)

## Global Constraints

- コードコメント・ログメッセージは日本語
- git worktree 禁止。作業はメイン作業ディレクトリの `feature/timeline-window` ブランチ
- ビルド確認は **COM3D2 → COM3D25 の順**で 2 本流す。COM3D2 構成の出力先が `bin/Debug` で COM3D25 構成の出力(`bin/Debug/COM3D25`)を巻き込むため、**COM3D25 を後に流さないとテストが参照する DLL が消える**:
  ```
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"
  MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"
  ```
  MSBuild の実体は `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`。Git Bash から呼ぶときは `/p:` がパスへ変換されるため `-p:` 形式を使う
- `debug.bat` はゲームフォルダへ DLL をコピーするため使わない。`deploy.bat` / `release.bat` / `deploy.ps1` は絶対に実行しない
- テスト: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`(既存 305 件を退行させない)。テストは COM3D25 構成のビルド出力を参照するため、**テスト実行前に必ず上記 2 本のビルドを済ませておくこと**
- 新規ファイルは作らない計画のため csproj の変更は不要
- テスト名前空間では `MTEP = COM3D2.MotionTimelineEditor.Plugin` の別名エイリアスを使う(既存テストと同じ)
- コミットフッター:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01JfyMyqPyfMpXSARp2D9Xsx
  ```

## データモデルの前提知識(全タスク共通)

- **この追補で変わる対応表**(キー化 ON の格納先は変えない)

  | 統合列挙 `MaidLookMode` | キー化 OFF の実体 | キー化 ON の実体 (`MTEP.LookAtTargetType`) | ON の選択肢 |
  |---|---|---|---|
  | カメラ | `MaidLookController` | `Camera` | あり |
  | メイド(+ポイント) | 同上 | `Maid` + `lookAtTargetIndex` + `lookAtMaidPointType` | あり |
  | **モデル** | **同上(本計画で新設。モデル名で保持)** | **`Model` + `lookAtTargetIndex`** | **あり(本計画で追加)** |
  | 方向指定 | 同上(`lookX/Y`) | `None` + `lookDirection`(顔向きキー) | あり |
  | 無し | 同上 | `None`(そらし時に自動フォールバック) | なし |
  | マウス | 同上 | — | なし |
  | オブジェクト(任意 Transform) | 同上 | — | なし |

- **今回の変更で `オブジェクト` の意味が狭まる**: これまで `LookAtTargetType.Model` は SE 側で `オブジェクト`(任意 Transform)として表示されていた。本計画以降は `モデル` へ写る。`オブジェクト` はキー化 OFF 専用の「Hierarchy で選んだ任意 Transform」に戻る
- **モデルの同定はモデル名**: `StudioModelStat.name` は `StudioModelManager.modelMap` のキーで、`GetModel(string)` / `GetModel(int index)`(`modelNames[index]` 経由)の両方から引ける。`StudioModelStat` の実体はシーン再構築で作り直されるため保持しない。表示名は `StudioModelStat.displayName`(`ShapeKeyEditWindow.cs:46` と同じ規約)
- **モデル一覧**: `MTEP.StudioModelManager.instance.models`(`List<StudioModelStat>`)。キー化 ON 側の番号 `lookAtTargetIndex` は `modelNames` の添字で、`models` と同順(`StudioModelManager.cs:294-316` で同時に構築される)
- `MaidWindowBase.DrawModelComboBox` は**使わない**。あれは「編集対象モデル」をシーン選択と同期させるヘルパで、注視先の指定とは意味が違う。`DrawLookMaidRows` と同じく `DrawLabeledComboBox` を使う
- `GUIComboBox<T>.defaultName` は `currentIndex` が -1 のときのボタン文字列。未選択を先頭要素へ丸めない
- `MaidCache.GetLookAtTarget` の `Model` 分岐(`Timeline/MaidCache.cs:861-869`)は**既に `StudioModelManager.GetModel(lookAtTargetIndex)` でモデルを解決できている**。壊れていたのは UI の選択肢と `MaidLookBridge` の写像だけ

---

## Task 1: 統合列挙へモデルを足し、双方向マップを繋ぐ(純粋ロジック)

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs`(`MaidLookMode` に `モデル` を追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs`(`KeyedModes` / `UnkeyedModes` / `ToLookMode` / `ToTargetType` / `ResolveLookMode`)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs`

**Interfaces:**
- Consumes: `MTEP.LookAtTargetType`(None/Camera/Maid/Model)
- Produces:
  - `MaidLookMode.モデル`(列挙の末尾。`メイド` の後)
  - `MaidLookBridge.ToLookMode(MTEP.LookAtTargetType.Model)` が `MaidLookMode.モデル` を返す
  - `MaidLookBridge.ToTargetType(MaidLookMode.モデル)` が `MTEP.LookAtTargetType.Model` を返す
  - `MaidLookBridge.GetSelectableModes(true)` に `モデル` が入る

- [ ] **Step 1: 既存テストの期待値を差し替える**

`MaidLookBridgeTests.cs` の既存 3 箇所を書き換える。

1 つ目 — `ResolveLookMode_キー化中は注視先種別を向け先モードへ写す` の `Model` 行:

```csharp
        [InlineData(MTEP.LookAtTargetType.Model, true, MaidLookMode.モデル)]
```

2 つ目 — `ToLookMode_キーの注視先種別を統合列挙へ写す` の `Model` 行(コメント行も差し替える):

```csharp
        [InlineData(MTEP.LookAtTargetType.Model, MaidLookMode.モデル)]
```

3 つ目 — `GetSelectableModes` の 3 テストを次で置き換える:

```csharp
        [Fact]
        public void GetSelectableModes_キー化中はキー化できる値だけを出す()
        {
            Assert.Equal(
                new[]
                {
                    MaidLookMode.カメラ, MaidLookMode.メイド,
                    MaidLookMode.モデル, MaidLookMode.方向指定,
                },
                MaidLookBridge.GetSelectableModes(true));
        }

        [Fact]
        public void GetSelectableModes_キー化していなければ全ての向け先を出す()
        {
            Assert.Equal(
                new[]
                {
                    MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
                    MaidLookMode.メイド, MaidLookMode.モデル,
                    MaidLookMode.オブジェクト, MaidLookMode.無し,
                },
                MaidLookBridge.GetSelectableModes(false));
        }

        [Fact]
        public void GetSelectableModes_返したリストを書き換えても次の呼び出しに影響しない()
        {
            var modes = MaidLookBridge.GetSelectableModes(true);
            modes.Clear();
            Assert.Equal(4, MaidLookBridge.GetSelectableModes(true).Count);
        }
```

- [ ] **Step 2: 追加の失敗するテストを書く**

`MaidLookBridgeTests.cs` の `ToTargetType_統合列挙をキーの注視先種別へ写す` の `[InlineData]` 群の先頭(`カメラ` の行の直後)へ 1 行足す:

```csharp
        [InlineData(MaidLookMode.モデル, MTEP.LookAtTargetType.Model)]
```

- [ ] **Step 3: テストが落ちることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー(`MaidLookMode` に `モデル` の定義がありません)

- [ ] **Step 4: 列挙値を追加する**

`MaidManipulation/MaidLookController.cs` の `MaidLookMode` の `メイド,` の直後(列挙の末尾)へ:

```csharp
        /// <summary>
        /// スタジオモデル (StudioModelManager が持つ配置物) を見る。
        /// タイムラインの注視先「モデル」と同じ概念で、キー化の有無に関わらず選べる。
        /// モデルの実体はシーン再構築で作り直されるため名前で保持し、適用のたびに引き直す。
        /// 既存プリセットの互換のため末尾に足す (列挙値のずれを避ける)
        /// </summary>
        モデル,
```

- [ ] **Step 5: 選択肢と双方向マップを繋ぐ**

`Timeline/MaidLookBridge.cs` の `KeyedModes` / `UnkeyedModes` を差し替える:

```csharp
        /// <summary>キー化中に選べる向け先。「無し」は None が方向指定と衝突するため出さない</summary>
        private static readonly MaidLookMode[] KeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.メイド,
            MaidLookMode.モデル, MaidLookMode.方向指定,
        };

        /// <summary>キー化していないときに選べる向け先 (SE の全モード)</summary>
        private static readonly MaidLookMode[] UnkeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
            MaidLookMode.メイド, MaidLookMode.モデル,
            MaidLookMode.オブジェクト, MaidLookMode.無し,
        };
```

`ToLookMode` を差し替える(古い「未移植」の根拠をここで消す):

```csharp
        /// <summary>キーの注視先種別を統合列挙へ写す (UI 表示用)</summary>
        public static MaidLookMode ToLookMode(MTEP.LookAtTargetType targetType)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Maid:
                    return MaidLookMode.メイド;
                case MTEP.LookAtTargetType.Model:
                    return MaidLookMode.モデル;
                default:
                    return MaidLookMode.方向指定;
            }
        }
```

`ToTargetType` の `メイド` の分岐の直後へ 2 行足す:

```csharp
                case MaidLookMode.モデル:
                    return MTEP.LookAtTargetType.Model;
```

- [ ] **Step 6: `ResolveLookMode` のモデル分岐を直す**

同ファイルの `switch (targetType)` の `Model` の case を差し替える(これまでは任意オブジェクト扱いだった):

```csharp
                case MTEP.LookAtTargetType.Model:
                    // モデル注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.モデル;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
```

- [ ] **Step 7: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS(既存テストも含めて緑)

- [ ] **Step 8: 2 構成でビルドする**

Run: Global Constraints の MSBuild コマンド 2 本(COM3D2 → COM3D25 の順)
Expected: どちらも成功

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs source/COM3D2.SceneEditor.Plugin.Tests/MaidLookBridgeTests.cs
git commit -m "refactor(look): 視線の向け先の語彙へモデル注視を足す"
```

---

## Task 2: コントローラにモデル注視を実装し、ブリッジから流す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs`(`Entry` / アクセサ / `SetState` / `ResolveLookTarget` / 静的ヘルパー)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs`(`ApplyLookMode`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs`(`UpdateLookAtTarget` / `GetLookAtModelName` の新設)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs`(呼び出し元の無い `GetLookAtTarget` を削除)

**Interfaces:**
- Consumes: `MaidLookMode.モデル`(Task 1)
- Produces:
  - `MaidLookController.GetTargetModelName(Maid) : string`
  - `MaidLookController.SetModelTarget(Maid maid, string modelName) : void`
  - `MaidLookController.SetState(Maid, MaidLookMode, float lookX, float lookY, Transform target, Maid targetMaid, MTEP.MaidPointType maidPointType, string targetModelName) : void`(8 引数。既存 7 引数版は**置き換える**)
  - `MaidLookController.GetModelTransform(string modelName) : Transform`(static)
  - `MaidLookBridge.ApplyLookMode(Maid, MaidLookMode, Transform, Vector2, Maid targetMaid, MTEP.MaidPointType maidPointType, string targetModelName) : void`

- [ ] **Step 1: コントローラにモデル注視の状態を持たせる**

`MaidManipulation/MaidLookController.cs` の `Entry` クラスの `maidPointType` の直後へ:

```csharp
            /// <summary>
            /// モデルモードの注視対象のモデル名。
            /// StudioModelStat はシーン再構築で作り直されるため実体は保持しない
            /// </summary>
            public string targetModelName;
```

`SetMaidTarget` の直後へアクセサを追加:

```csharp
        public string GetTargetModelName(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.targetModelName : null;
        }

        /// <summary>モデルモードの注視対象。モデル名で持ち、Transform は適用のたびに引き直す</summary>
        public void SetModelTarget(Maid maid, string modelName)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.targetModelName = modelName;
            Apply(maid);
        }
```

- [ ] **Step 2: `SetState` へモデル名を足す**

同ファイルの `SetState`(7 引数版)を次で置き換える:

```csharp
        /// <summary>
        /// 状態をまとめて差し替える。個別セッターを重ねると Apply が状態ごとに走り、
        /// 途中の中途半端な組み合わせで注視点を計算してしまうため、
        /// Undo・プリセット復元のような一括復元はこちらを使う
        /// </summary>
        public void SetState(
            Maid maid, MaidLookMode mode, float lookX, float lookY, Transform target,
            Maid targetMaid, MTEP.MaidPointType maidPointType, string targetModelName)
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
            entry.targetModelName = targetModelName;
            Apply(maid);
        }
```

呼び出し元は Task 2 Step 4(ブリッジ)と Task 4(履歴・プリセット)で 8 引数へ直す。この時点ではコンパイルが通らないが、Step 5 のビルドまで確認しない。

- [ ] **Step 3: 向け先の解決へモデルモードを足す**

同ファイルの `ResolveLookTarget` の `if (entry.mode == MaidLookMode.メイド)` ブロックの**直後**へ:

```csharp
            if (entry.mode == MaidLookMode.モデル)
            {
                // モデルが消えている・未設定なら方向指定の注視点で代用する (メイドモードと同じ扱い)
                var modelTransform = GetModelTransform(entry.targetModelName);
                if (modelTransform != null)
                {
                    return modelTransform;
                }
            }
```

同ファイルの `GetMaidPointTransform` の直後へ静的ヘルパーを追加:

```csharp
        /// <summary>
        /// モデル名からモデルの Transform を引く。
        /// モデルはシーン再構築で作り直されるため、適用のたびにここで引き直す
        /// </summary>
        public static Transform GetModelTransform(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                return null;
            }

            var model = MTEP.StudioModelManager.instance.GetModel(modelName);
            return model != null ? model.transform : null;
        }
```

- [ ] **Step 4: ブリッジからモデル指定を流す**

`Timeline/MaidLookBridge.cs` の `ApplyLookMode` を差し替える:

```csharp
        /// <summary>
        /// 向け先モードを SE のコントローラへ反映する。
        /// モードに関係しない指定 (注視対象・メイド指定・モデル指定・顔向き) は
        /// SE が覚えている値を残す (タイムライン側の都合で消さないため)。
        /// 方向指定のときだけ顔向きキーの指定値で lookX/lookY を駆動する
        /// </summary>
        /// <param name="lookDirection">顔向きキーの指定値 (lookX/lookY、-1〜1)</param>
        /// <param name="targetMaid">注視先がメイドのときの対象</param>
        /// <param name="maidPointType">注視先がメイドのときの部位</param>
        /// <param name="targetModelName">注視先がモデルのときの対象のモデル名</param>
        public static void ApplyLookMode(
            Maid maid, MaidLookMode mode, Transform target, Vector2 lookDirection,
            Maid targetMaid, MTEP.MaidPointType maidPointType, string targetModelName)
        {
            if (maid == null)
            {
                return;
            }

            var controller = lookController;
            var isDirection = mode == MaidLookMode.方向指定;
            var isMaid = mode == MaidLookMode.メイド;
            var isModel = mode == MaidLookMode.モデル;
            controller.SetState(
                maid,
                mode,
                isDirection ? lookDirection.x : controller.GetLookX(maid),
                isDirection ? lookDirection.y : controller.GetLookY(maid),
                mode == MaidLookMode.オブジェクト ? target : controller.GetTarget(maid),
                isMaid ? targetMaid : controller.GetTargetMaid(maid),
                isMaid ? maidPointType : controller.GetMaidPointType(maid),
                isModel ? targetModelName : controller.GetTargetModelName(maid));
        }
```

- [ ] **Step 5: MaidCache の適用経路を繋ぎ替える**

`Timeline/MaidCache.cs` の `GetLookAtMaid` の直後へ、同じ形のヘルパーを足す:

```csharp
        /// <summary>
        /// 注視先がモデルのときの対象のモデル名。注視先が別種別なら null。
        /// SE 側は名前でモデルを保持するため、番号ではなく名前を渡す。
        /// GetLookAtTarget と SE 側の解決で GetModel を二度引くが、
        /// メイド注視と同じ形で、辞書引き 1 回のため許容する
        /// </summary>
        private string GetLookAtModelName()
        {
            if (lookAtTargetType != LookAtTargetType.Model)
            {
                return null;
            }

            var model = modelManager.GetModel(lookAtTargetIndex);
            return model != null ? model.name : null;
        }
```

同ファイルの `UpdateLookAtTarget` の `ApplyLookMode` 呼び出しを差し替える:

```csharp
            // メイド注視・モデル注視は Transform ではなく対象の同定情報で渡し、
            // 実際の Transform は SE のコントローラが適用のたびに引き直す
            SEP.MaidLookBridge.ApplyLookMode(
                maid, lookMode.Value, lookAtTarget, _lookDirection,
                GetLookAtMaid(), lookAtMaidPointType, GetLookAtModelName());
```

- [ ] **Step 6: 瞳レイヤーの死んだ注視先解決を削除する**

`Timeline/TimelineLayer/EyesTimelineLayer.cs` の `private Transform GetLookAtTarget(...)` メソッド全体(`ApplyLookAtTarget` の直後にある、`LookAtTargetType` で分岐して `Transform` を返すメソッド)を削除する。

理由: このメソッドは呼び出し元が無く(`grep -n "GetLookAtTarget(" source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs` で定義行しか出ない)、実際の注視先解決は `MaidCache.GetLookAtTarget` が行っている。残っているコメント「`StudioModelManager` は未移植のため対象を解決しない」は事実と違う(移植済み)ため、古い根拠を残さないよう削除する。

削除後、`EyesTimelineLayer.cs` で未使用になる using があればあわせて削除する(`dotnet build` の警告ではなく、`MaidPointType` 等の参照が他に残っているかを目視で確認すること)。

- [ ] **Step 7: 2 構成でビルドする**

Run: Global Constraints の MSBuild コマンド 2 本(COM3D2 → COM3D25 の順)
Expected: `PoseSnapshot.cs` と `ScenePresetManager.cs` の `SetState` 呼び出しが 7 引数のままのため **CS1501(引数の数が違う)で失敗する**。ここでは失敗が正しい。Task 4 で 8 引数へ直す

> **注意**: Task 3(UI)は `SetState` を直接呼ばないため、Task 4 まではビルドが通らない状態が続く。Task 3 のビルド確認も同じ 2 件のエラーだけが出ることを確認して先へ進む

- [ ] **Step 8: コミットしない**

このタスクの変更はビルドが通らないため単独ではコミットしない。Task 4 Step 6 でまとめてコミットする。

---

## Task 3: 視線タブへモデル選択行を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs`(キー化 ON のモデル行)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs`(キー化 OFF のモデル行)
- Test: なし(UI 描画のため実機確認項目で担保する。選択肢の決定ロジックは Task 1 でテスト済み)

**Interfaces:**
- Consumes: `MaidLookController.GetTargetModelName` / `SetModelTarget`(Task 2)、`MTEP.StudioModelManager.instance.models`
- Produces: UI のみ。他タスクが参照するシグネチャは増やさない

- [ ] **Step 1: キー化 ON のモデル行を足す**

`TimelineLookRowDrawer.cs` の `_targetMaidPointComboBox` の宣言の直後へコンボを追加:

```csharp
        /// <summary>キー化中のモデル注視の対象。番号 (modelNames の添字) で書き込む</summary>
        private readonly GUIComboBox<MTEP.StudioModelStat> _targetModelComboBox =
            new GUIComboBox<MTEP.StudioModelStat>
            {
                getName = (model, _) => model.displayName,
            };
```

`DrawLookAtTargetRows` の末尾(`ポイント` の行を描いた後)を差し替える。現在の

```csharp
            if (_lookModeComboBox.currentItem != MaidLookMode.メイド)
            {
                return;
            }
```

以降のブロック全体を次で置き換える:

```csharp
            if (_lookModeComboBox.currentItem == MaidLookMode.モデル)
            {
                var models = MTEP.StudioModelManager.instance.models;
                _targetModelComboBox.items = models;
                // 番号は modelNames の添字。models と同順で構築されるため添字をそのまま使う
                var modelIndex = maidCache.lookAtTargetIndex >= 0
                    && maidCache.lookAtTargetIndex < models.Count
                    ? maidCache.lookAtTargetIndex : -1;
                _targetModelComboBox.defaultName = modelIndex >= 0 ? null : "未選択";
                _targetModelComboBox.currentIndex = modelIndex;
                _targetModelComboBox.onSelected =
                    (_, index) => maidCache.lookAtTargetIndex = index;
                LabeledComboRow.Draw(view, "モデル", _targetModelComboBox, labelWidth, rowHeight);
                return;
            }

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
```

`lookAtTargetIndex` はメイドとモデルで共用の番号のため、向け先を切り替えた直後は前の種別の番号が残る。`models.Count` を超えていたら未選択として扱う(上の `defaultName` / `currentIndex` の分岐)。

- [ ] **Step 2: キー化 OFF のモデル行を足す**

`MaidFaceWindow.cs` の `_lookMaidPointComboBox` の宣言の直後へコンボを追加:

```csharp
        /// <summary>モデルモードの注視対象。スタジオモデルから選ぶ</summary>
        private readonly GUIComboBox<MTEP.StudioModelStat> _lookModelComboBox =
            new GUIComboBox<MTEP.StudioModelStat>
            {
                getName = (model, _) => model.displayName,
            };
```

`DrawLookTargetRows` の末尾の分岐へ `モデル` を足す:

```csharp
            // 対象の指定はキー化中の行 (向け先の直下に対象が続く) と並びを揃える
            if (mode == MaidLookMode.メイド)
            {
                DrawLookMaidRows(view, target);
            }
            else if (mode == MaidLookMode.モデル)
            {
                DrawLookModelRow(view, target);
            }
            else if (mode == MaidLookMode.オブジェクト)
            {
                DrawLookObjectRows(view, target);
            }
```

`DrawLookMaidRows` の直後へメソッドを追加:

```csharp
        /// <summary>モデルモードの注視対象。キー化中は同じ行を TimelineLookRowDrawer が描く</summary>
        private void DrawLookModelRow(GUIView view, Maid target)
        {
            var models = MTEP.StudioModelManager.instance.models;
            var modelName = lookController.GetTargetModelName(target);

            _lookModelComboBox.items = models;
            // 未選択・モデルが消えたときは currentIndex が -1 になりボタン文字列が決まらないため既定名で埋める
            var index = models.FindIndex(model => model.name == modelName);
            _lookModelComboBox.defaultName = index >= 0 ? null : "未選択";
            _lookModelComboBox.currentIndex = index;
            _lookModelComboBox.onSelected = (selected, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetModelTarget(target, selected.name);
            };
            DrawLabeledComboBox("モデル", _lookModelComboBox);
        }
```

- [ ] **Step 3: 2 構成でビルドする**

Run: Global Constraints の MSBuild コマンド 2 本(COM3D2 → COM3D25 の順)
Expected: Task 2 Step 7 と同じ `SetState` の CS1501 が 2 件だけ出る(`PoseSnapshot.cs` / `ScenePresetManager.cs`)。**新しいエラーが増えていないこと**を確認する

- [ ] **Step 4: コミットしない**

Task 4 Step 6 でまとめてコミットする。

---

## Task 4: 履歴(Undo)とシーンプリセット v28 への対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs`(フィールド / 記録 / 復元 / 差分判定)
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`(`ScenePresetLook` / バージョン)
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`(`CaptureLook` / `ApplyLook` / `FinishApply` の順序 / `ApplyLooks` のモデル一覧強制更新)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs`

**Interfaces:**
- Consumes: `MaidLookController.GetTargetModelName` / 8 引数 `SetState`(Task 2)
- Produces: `ScenePresetLook.targetModelName`(モデル名。空なら未記録)

- [ ] **Step 1: 失敗するテストを書く**

`ScenePresetLookXmlTests.cs` のクラス末尾(最後の `}` の 2 つ手前)へ追記する:

```csharp
        [Fact]
        public void モデル注視の対象が往復で保たれる()
        {
            var restored = RoundTrip(new ScenePresetLook
            {
                mode = "モデル",
                targetModelName = "Cube_1",
            });

            Assert.Equal("モデル", restored.mode);
            Assert.Equal("Cube_1", restored.targetModelName);
        }

        [Fact]
        public void モデル注視を持たない旧プリセットは対象が未記録になる()
        {
            const string oldPreset =
                "<?xml version=\"1.0\"?>"
                + "<ScenePresetLook mode=\"カメラ\">"
                + "<lookX>0</lookX><lookY>0</lookY>"
                + "</ScenePresetLook>";

            using (var reader = new StringReader(oldPreset))
            {
                var look = (ScenePresetLook) Serializer.Deserialize(reader);
                Assert.Null(look.targetModelName);
            }
        }
```

- [ ] **Step 2: テストが落ちることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: コンパイルエラー(`ScenePresetLook` に `targetModelName` の定義がありません。加えて Task 2 由来の `SetState` の CS1501 も残っている)

- [ ] **Step 3: プリセットのスキーマを足す**

`ScenePresetData.cs` の `ScenePresetLook` の `maidPointType` の直後へ:

```csharp
        /// <summary>
        /// 向け先がモデルのときの対象のモデル名 (StudioModelStat.name)。
        /// v27 以前や他モードでは null になり、適用時にモデル指定へ触らない
        /// </summary>
        [XmlAttribute]
        public string targetModelName;
```

同ファイルのバージョン定義へ 1 行足し、`CurrentVersion` を 28 にする(`// v27:` のコメントブロックの直後):

```csharp
        // v28: look に targetModelName（向け先「モデル」の対象モデル名）を追加。
        //      旧形式は targetModelName が null = 未記録として読み飛ばす
        public static readonly int CurrentVersion = 28;
```

- [ ] **Step 4: プリセットの記録・復元をモデルモードへ対応させる**

`Manager/ScenePresetManager.cs` の `CaptureLook` の `else if (mode == MaidLookMode.メイド)` ブロックの直後へ:

```csharp
            else if (mode == MaidLookMode.モデル)
            {
                // モデルはシーン再構築で作り直されるため、名前で持つ
                look.targetModelName = controller.GetTargetModelName(maid);
            }
```

`ApplyLook` の既存の `maidManager.lookController.SetState(maid, mode, look.lookX, look.lookY, target, targetMaid, maidPointType);` の呼び出しを、次のブロックで置き換える(モデルの解決を足し、`SetState` を 8 引数へ直す)。

> **前提**: このモデル存在チェックが正しく働くには Step 4b・4c の順序修正が要る。Step 4 単独では、モデルがまだ復元されていないため常に方向指定へ落ちる

```csharp
            string targetModelName = null;
            if (mode == MaidLookMode.モデル)
            {
                targetModelName = look.targetModelName;
                if (MaidLookController.GetModelTransform(targetModelName) == null)
                {
                    MTEUtils.LogWarning("注視対象のモデルが見つからないため方向指定で復元します: {0}",
                        look.targetModelName);
                    mode = MaidLookMode.方向指定;
                }
            }

            maidManager.lookController.SetState(
                maid, mode, look.lookX, look.lookY, target,
                targetMaid, maidPointType, targetModelName);
```

`MaidLookController.GetModelTransform` は Task 2 Step 3 で足した static ヘルパー。`ScenePresetManager` は `COM3D2.SceneEditor.Plugin` 名前空間にあるため修飾なしで呼べる。

- [ ] **Step 4b: 視線の復元を外部プロバイダのモデル復元より後へ動かす**

`Manager/ScenePresetManager.cs` の `FinishApply` を差し替える。**現状は `ApplyLooks()` が `ApplyExternals(data)` より先に走るため、モデルはまだ存在しない**(モデルを作るのは外部プロバイダ):

```csharp
        private static void FinishApply(ScenePresetData data)
        {
            var applyMaids = ShouldApplyMaids(data);
            ApplyExternals(data);
            // 外部プロバイダのモデル復元 (同期) の後でないと GameObject が存在しない
            ApplyModelBoneEdits(data);
            ApplyModelAppearances(data);
            // 視線はモデルを注視先にできるため、モデルが揃った後に反映する
            if (applyMaids)
            {
                ApplyLooks();
                RequestFocusOnAppliedMaid(data);
            }
            // Maid 参照を適用の間だけ持つ。以降の解除・シーン遷移で寿命が切れるため残さない
            _resolvedAssignments.Clear();
        }
```

あわせて `FinishApply` の XML コメントの「視線は他メイドを参照しうるため、外部プロバイダと同じくここで反映する。」を次へ直す:

```csharp
        /// 全メイドのロード完了後にまとめて行う仕上げ。
        /// 視線は他メイドとモデルを注視先にできるため、外部プロバイダのモデル復元より後に反映する。
```

- [ ] **Step 4c: モデル一覧を強制的に取り直してから視線を戻す**

`StudioModelManager.LateUpdate()` は 30 フレームに 1 回しか `modelMap` を更新しない(`Timeline/Manager/StudioModelManager.cs:219-228` の `Time.frameCount < _prevUpdateFrame + 30` ガード)。Step 4b で順序を直しても、復元直後のモデルはまだ登録されていない。

さらに `MaidLookController.Apply` はセッター呼び出しのときだけ走る(マウスモード以外は毎フレーム再解決しない)ため、後から現れたモデルを拾い直す機会が無い。復元の時点で引けるようにする。

`ApplyLooks` の `foreach` の直前へ:

```csharp
            // モデル注視を戻すメイドがいるなら、モデル一覧を取り直してから解決する。
            // StudioModelManager は 30 フレームに 1 回しか一覧を更新しないため、
            // 外部プロバイダが今作ったモデルは強制更新しないと引けない
            var needsModel = _resolvedAssignments.Values.Any(
                assignment => assignment.look != null
                    && !string.IsNullOrEmpty(assignment.look.targetModelName));
            if (needsModel)
            {
                MTEP.StudioModelManager.instance.LateUpdate(true);
            }
```

`System.Linq` と `MTEP` の using がファイルに無ければ足す(`MTEP = COM3D2.MotionTimelineEditor.Plugin` は追加済みのはず)。`_resolvedAssignments` の値の型に `look` フィールドがあることは既存の `ApplyLooks` が `pair.Value.look` で使っている。

- [ ] **Step 5: Undo でモデル注視が戻るようにする**

`Manager/History/PoseSnapshot.cs` の `_lookMaidPointType` フィールドの直後へ:

```csharp
        private string _lookTargetModelName;
```

`Capture` の `snapshot._lookMaidPointType = ...` の直後へ:

```csharp
            snapshot._lookTargetModelName = lookController.GetTargetModelName(maid);
```

復元の `SetState` を 8 引数へ:

```csharp
            MaidManipulateManager.instance.lookController.SetState(
                maid, _lookMode, _lookX, _lookY, _lookTarget,
                _lookTargetMaid, _lookMaidPointType, _lookTargetModelName);
```

差分判定(`Approximately` の条件列)の `|| _lookMaidPointType != o._lookMaidPointType` の直後へ:

```csharp
                || _lookTargetModelName != o._lookTargetModelName
```

- [ ] **Step 6: テストと 2 構成ビルドを通してコミットする**

Run: Global Constraints の MSBuild コマンド 2 本(COM3D2 → COM3D25 の順)
Expected: どちらも成功(Task 2 から続いていた CS1501 が解消する)

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: PASS

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidLookController.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidLookBridge.cs source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/TimelineLookRowDrawer.cs source/COM3D2.SceneEditor.Plugin/MaidFaceWindow.cs source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetLookXmlTests.cs
git commit -m "feat(look): 視線の向け先にモデル注視を足す"
```

---

## Task 5: 調査ドキュメントの更新

**Files:**
- Modify: `docs/se-mte-state-duplication-survey.md`(「D-7 の実装メモ」節、「実機確認項目」節)

**Interfaces:**
- Consumes: Task 1〜4 の実装結果

- [ ] **Step 1: 実装メモをモデル注視込みへ更新する**

「### D-7 の実装メモ(視線の向け先/注視先の概念統合)」節を次のとおり直す:

- 冒頭の箇条書き 1 に「`メイド`」だけでなく「`メイド` / `モデル`」を足す
- 箇条書き 5(プリセット v27)を「プリセット v27 / v28」にし、v28 で `targetModelName` を足した旨を書く
- 「**統合前の状態**」の「片側にしか無い値」の行から `モデル` を外す(統合済みのため)
- 「#### 決定した方針」の対応表へ `モデル` の行を足し、`ON の選択肢` を「あり」にする
- 「#### 決定した方針」の 6.「**モデル注視は除外を継続**」を次で置き換える:

  ```markdown
  6. **モデル注視も統合列挙へ入れる**(2026-08-30 追補): 当初は「`StudioModelManager` 未移植」を根拠に除外していたが、**この根拠は誤り**だった。`StudioModelManager` は移植済みで `MaidCache.GetLookAtTarget` も `Model` を解決できていたため、UI から選べないだけの状態だった。`MaidLookMode.モデル` を末尾へ足して統合列挙へ入れ、キー化 ON/OFF のどちらでも選べるようにした。SE 側はモデル名で保持し、適用のたびに `StudioModelManager.GetModel(name)` で引き直す。あわせて `EyesTimelineLayer` にあった同じ誤った根拠のコメントごと、呼び出し元の無い `GetLookAtTarget` を削除した。統合列挙の `オブジェクト` はキー化 OFF 専用の「Hierarchy で選んだ任意 Transform」に意味が狭まった
  ```

- 「**実装計画**」の行へ本計画のパスを追記する:

  ```markdown
  **実装計画**: `docs/superpowers/plans/2026-08-30-d7-look-target-unification.md`、追補は `docs/superpowers/plans/2026-08-30-d7b-model-look-target.md`
  ```

- [ ] **Step 2: 実機確認項目を追記する**

「### 実機確認項目(loop 中に追記)」の D-7 の項目群の末尾へ:

```markdown
- D-7: キー化 OFF で向け先「モデル」を選び、配置したスタジオモデルを選ぶと視線が追従すること。モデルを動かすと視線も追従すること
- D-7: キー化 ON で向け先「モデル」を選んだときの見た目が、OFF で同じモデルを選んだときと一致すること
- D-7: 向け先「モデル」で対象モデルを削除すると方向指定の注視点へ落ち、例外が出ないこと。コンボの表示が「未選択」になること
- D-7: 向け先「メイド」→「モデル」と切り替えたとき、キー化 ON でメイドの番号がモデル一覧の範囲外なら「未選択」表示になること
- D-7: 向け先「モデル」のままシーンプリセットを保存 → ロードで対象モデルが戻ること。v27 以前のプリセットをロードしても視線まわりで例外が出ないこと
- D-7: 向け先「モデル」で対象を変えた直後に Ctrl+Z すると、変更前の対象へ戻ること
- D-7: v27 以前のプリセットで注視先がモデルだったデータをロードすると、「オブジェクト」ではなく「モデル」として復元されること(または従来どおり注視できていること)
- D-7: モデル配置を含むシーンプリセットをロードしたとき、視線が「モデルが見つからない」の警告を出さずに対象モデルへ戻ること(視線の復元が外部プロバイダのモデル復元より後に走っていること)
- D-7: プリセットロード後に他のメイドの視線・フォーカス・モデルボーン編集が従来どおり復元されること(FinishApply の順序変更の巻き添えが無いこと)
```

- [ ] **Step 3: コミット**

```bash
git add docs/se-mte-state-duplication-survey.md docs/superpowers/plans/2026-08-30-d7b-model-look-target.md
git commit -m "docs(survey): D-7 追補としてモデル注視の統合を記録する"
```

---

## レビュー却下メモ

plan-review(2026-08-30)の指摘 3 件はすべて取り込んだため、却下は無し。

取り込んだ内容:

- 🔴 プリセット復元時のモデル存在チェックが `ApplyExternals` より前に走り、常に方向指定へ落ちる → Task 4 Step 4b(`FinishApply` の順序修正)を追加。**加えて計画側の追加調査で、`StudioModelManager.LateUpdate` の 30 フレームガードにより順序修正だけでは足りず、`MaidLookController.Apply` もセッター時のみで後追い解決しないことが判明したため、Step 4c(モデル一覧の強制更新)も追加した**
- 🟡 `lookAtTargetIndex` の下限チェック漏れ → Task 3 Step 1 で `>= 0` も見るよう修正
- 🟡 `GetModel` の二重解決が意図的である旨のコメントが無い → Task 2 Step 5 の `GetLookAtModelName` のコメントへ明記
