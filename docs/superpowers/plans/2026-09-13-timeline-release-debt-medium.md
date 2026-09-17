# タイムライン リリース前 負債対応（優先度 中）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン XML version 34 を公開する前に、優先度 中の負債 10 件（登録対象の一本化・ライブ演出の履歴対応・Undo の穴・0 フレームキー必須の撤廃・レイヤー削除の副作用・補間のばらつき・回転カーブのタンジェント編集）をコードで解消し、残り 2 件を仕様書に明記する。

**Architecture:** XML の DTO（`TimelineXml` / `TransformXml`）と `TimelineData.CurrentVersion` は一切変えない。変更はすべて「保存の形はそのままに、登録対象・履歴記録・補間計算・編集可否の振る舞いを直す」もの。ライブ演出の履歴は、既存の `PresetDtoSnapshot<T>` と `GUIView.onBeforeValueChanged` フックに乗せて、行ドロワーを触らずに全値変更を拾う。

**Tech Stack:** C#（プラグイン本体は COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成）、xunit（テストは net48、COM3D25 構成のビルド成果物を参照）、MSBuild、VitePress（`docs-site/`）

**Spec:** `docs/superpowers/specs/2026-09-13-timeline-release-debt-medium-design.md`
（元となった負債一覧は `docs/timeline-release-debt-review.md`。優先度 高の計画は `docs/superpowers/plans/2026-09-13-timeline-release-debt.md`）

## Global Constraints

- **XML 形式と version は変えない。** `TimelineData.CurrentVersion` は 34 のまま。`TransformXml` の要素、`TransformData*` の `valueCount` と値の並びも変えない（`hasTangent` を true にすると既存要素 `InSmoothBit` / `OutSmoothBit` の中身が変わるだけで、要素自体は増えない）
- **コメントとログメッセージは日本語で書く。** 既存コードの記述密度に合わせる
- **ビルドは 2 構成とも通すこと。** COM3D2 構成は **.NET 3.5**（入力 5 個以上の `Func<>`/`Action<>` 不可、`Tuple` 一部不可）。片方だけ通して「ビルド成功」と報告しない
- **`debug.bat` / `deploy.bat` / `deploy.ps1` は実行しない。** `debug.bat` はゲームフォルダへ DLL をコピーする。ビルドは MSBuild を直接叩く
- **`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` は `<Compile Include=...>` の明示列挙。** 本体側にファイルを新規追加したら csproj にも行を足す（本計画では Task 7 で 1 ファイル追加する）。テストプロジェクトは SDK スタイルなので追加不要
- **git worktree は使わない。** メインの作業ディレクトリで作業する
- ビルド・テストは常に次の 1 本で回す（Git Bash）。COM3D2 構成のビルドは `bin/Debug/COM3D25/` を消すため、必ず COM3D2 → COM3D25 → `dotnet test` の順にする

  ```bash
  cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
  export MSYS2_ARG_CONV_EXCL="*"
  MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && dotnet test source/COM3D2.SceneEditor.Plugin.Tests
  ```

## 設計書からの逸脱（実装前に把握すること）

1. **項目 11 の配線は行ドロワーではなく `GUIView.onBeforeValueChanged` で行う。** 設計書は「3 つの行ドロワーと `LiveEffectWindow` の全経路で値を書く直前に `BeforeEdit` を呼ぶ」としているが、ライブ演出の値行はすべて `view.BeginAutoEditMode()` … `view.EndAutoEditMode()` の内側にあり（`LiveEffectWindow` 9 箇所 + 3 つの ItemInspector）、`GUIView` は値変更の直前に必ず `onBeforeValueChanged` を発火する（`MTEUtils/GUIView.cs:285-291`、21 箇所から呼ばれる）。フックに `BeforeEdit` を差し込めば行ドロワー（計 1330 行）を 1 行も触らずに全値変更を拾える。`BeforeEdit` は内部で `AutoEditMode.Enter()` を呼ぶので、今フックに入っている `AutoEditMode.Enter` は置き換えでよい。コールバックを経由しないボタン操作（追加・削除・コピー）だけは明示的に呼ぶ
2. **項目 17 の互換説明を訂正する。** 設計書は「既存キーはタンジェント未保存なので自動補間 (`isSmooth`) として読まれる」としているが、`TransformXml.inSmoothBit` の既定値は `0` で、`hasTangent` が false だった頃の `背景色` / `地面色` キーは `InSmoothBit=0` として保存されている（`TransformDataBase.cs:882-907`）。読み直すと `isSmooth=false` / `normalizedValue=0` = **両端が水平なタンジェント**になり、自動補間にはならない。挙動としては「スナップ → 補間」で設計意図どおりなので実装は設計書のままとし、`compatibility.md` の文言だけ「両端がゆるやかな補間になる」に直す
3. **項目 13 は「補修 + 削除・移動の禁止」ではなく「検証の撤廃」で解決する（ユーザー判断で設計書から変更）。** 設計書は 0 フレームキーを必須のまま守る方針だったが、必須である根拠をコードで追った結果、次が分かった。

   - この検証は `メイドアニメ` / `メイド瞳` / `メイド表情` / `メイド脱衣` の 4 レイヤーが MTE からの移植時にそのまま持ち込んだもので（コミット `9b5c130` / `8461b68` / `e7773b8` / `993a29b`）、本リポジトリに判断の記録は無い
   - 0 フレームキーが無くても再生は壊れない。`PlayDataBase.Update` は先頭キーより手前では `listIndex < 0` の「開始フレーム待ち」で何も適用しないだけで、`メイド衣装` / `拡張ボーン` / `IK` / `ライト` / `モデル` など検証を持たない他の全レイヤーは元からこの挙動
   - anm 出力も壊れない。`MotionTimelineLayer.GetAnmBinaryInternal` はキーを `frameTime - startSecond` で書くので、先頭キーが 5F なら clip が 0.16 秒から始まるだけで、Unity の `AnimationCurve` は先頭キー以前をその値で保持する
   - 一方で代償が大きい。`IsValidData` は `TimelineIntegration.UpdateGuards`（`:124-139`）と `LateUpdate`（`:157-162`）から呼ばれるので、1 レイヤーが落ちるとタイムライン機構全体（全レイヤー + 全マネージャ、ライブ演出含む）の更新が止まり、保存と `アニメ出力` も止まる

   よって 4 レイヤーの `IsValidData` から 0 フレーム検証を外し、他レイヤーと同列に扱う。補修・削除禁止・`requiresFirstFrame` / `FirstFrameGuard` の追加はすべて不要になる
4. **仕様書の更新は各項目のタスクに畳み込む。** 設計書の実装順では最後にまとめているが、記述の更新はその項目の成果物の一部なので同じタスクで行う。実装順そのものは設計書どおり

## File Structure

| ファイル | 役割 | 変更 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | タイムライン全体の管理 | トラック操作・`最終フレーム`の履歴記録、基準 XML の先取り、登録対象の一本化、0 フレームキーの削除・移動の抑止 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs` | SE 履歴への橋渡し | `SetBaseline` を追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs` | タイムライン設定ウィンドウ | トラック名・範囲変更で履歴を要求 |
| `.../TimelineLayer/{Motion,Eyes,Morph,Undress}TimelineLayer.cs` | 旧「0 フレーム必須」の 4 レイヤー | `IsValidData` から 0 フレーム検証を削除 |
| `.../TimelineLayer/SubCameraTimelineLayer.cs` | サブカメラレイヤー | `Dispose` の `DestroyAllCameras` を外す |
| `.../TimelineLayer/TextTimelineLayer.cs` | テキスト適用 | 位置・角度・拡縮を Hermite 補間へ |
| `.../TimelineLayer/BGColorTimelineLayer.cs` | 背景色・地面色適用 | 補間経路を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBGColor.cs` | 背景色のキー値 | `hasTangent` / `tangentValues` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBGGroundColor.cs` | 地面色のキー値 | `hasTangent` / `tangentValues` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPngObject.cs` | PNG 配置のキー値 | `tangentValues` に `ScaleZ` |
| `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs` | カーブエディタ | Euler 表示のタンジェント編集を解禁 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs` | 履歴スコープ | `LiveEffect` を追加 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs` | **新規** | ライブ演出 3 マネージャの全状態 DTO とスナップショット |
| `source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs` | 自動編集モード宣言 | フック差し替えのオーバーロード |
| `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs` | ライブ演出ウィンドウ | 履歴フックの適用と、ボタン操作の明示記録 |
| `.../Timeline/ItemInspector/{StageLight,StageLaser,Psyllium}ItemInspector.cs` | Inspector の項目表示 | 履歴フックの適用 |
| `docs-site/timeline/*.md` | 仕様書 | 各項目の記述を訂正 |
| `source/COM3D2.SceneEditor.Plugin.Tests/*.cs` | テスト | 3 ファイル新規 |

---

### Task 1: トラック操作と最終フレームを Undo でき、読込直後の 1 手目も戻せるようにする（項目 12）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs:56`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:346-373,375-449,925-931,1248-1325`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs:555-560,566-585`
- Modify: `docs-site/timeline/editing.md:114-121`
- Modify: `docs-site/timeline/compatibility.md:42`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineHistoryBaselineTests.cs`（新規）

**Interfaces:**
- Consumes: なし（先頭タスク）
- Produces: `TimelineHistoryManager.SetBaseline(TimelineData timeline)` — 現在のタイムラインを「変更前」の基準として据え、履歴には何も積まない。以降のタスクでは使わない

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineHistoryBaselineTests.cs` を新規作成する。

`TimelineHistoryManager` はシングルトンだが `lastCommittedXml` は public な自動プロパティなので、`SetBaseline` を通したあとに中身を読めば「基準が据わったか」を検証できる。`AddHistory` は `SceneEditor.Plugin.HistoryManager` を触るためゲーム外では呼べないので、テストは `SetBaseline` の効果だけを見る。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineHistoryBaselineTests
    {
        [Fact]
        public void SetBaselineは現在のタイムラインを基準として据える()
        {
            var manager = TimelineHistoryManager.instance;
            manager.ClearHistory();
            Assert.Null(manager.lastCommittedXml);

            var timeline = new TimelineData { anmName = "テスト" };
            manager.SetBaseline(timeline);

            Assert.NotNull(manager.lastCommittedXml);
            Assert.Equal("テスト", manager.lastCommittedXml.anmName);
        }

        [Fact]
        public void SetBaselineにnullを渡すと基準を消す()
        {
            var manager = TimelineHistoryManager.instance;
            manager.SetBaseline(new TimelineData { anmName = "テスト" });

            manager.SetBaseline(null);

            Assert.Null(manager.lastCommittedXml);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineHistoryBaselineTests`
Expected: コンパイルエラー `'TimelineHistoryManager' に 'SetBaseline' の定義が含まれていません`

- [ ] **Step 3: `SetBaseline` を実装する**

`Timeline/Manager/TimelineHistoryManager.cs` の `ClearHistory` の直前へ追加する。

```csharp
        /// <summary>
        /// 現在のタイムラインを「変更前」の基準として据える。履歴には積まない。
        /// 新規作成・読み込みの直後に呼ぶことで、その後の最初の操作から
        /// AddHistory がエントリを積めるようになる (基準作りに 1 手目を消費しない)
        /// </summary>
        public void SetBaseline(TimelineData timeline)
        {
            lastCommittedXml = timeline?.ToXml();
        }
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TimelineHistoryBaselineTests`
Expected: PASS（先に COM3D25 構成のビルドが要る。Global Constraints のコマンド 1 本で回す）

- [ ] **Step 5: 新規作成・読み込みの末尾で基準を先取りする**

`Timeline/Manager/TimelineManager.cs` の `CreateNewTimeline`（`:346`）末尾を次に置き換える。

```csharp
            CreateAndApplyAnmAll();
            Refresh();

            // 履歴の基準をここで据える。RequestHistory で積むと基準が無いぶん
            // 積まれず、直後の最初の操作が基準作りに消費されてしまう
            historyManager.SetBaseline(_timeline);
        }
```

同じく `LoadTimeline`（`:375`）末尾を次に置き換える。

```csharp
            SeekCurrentFrame(0);
            Refresh();

            // 読み込み直後の最初の操作から Undo できるよう、基準をここで据える
            historyManager.SetBaseline(_timeline);
            // Extensions.ShowDialog("タイムライン「" + anmName + "」を読み込みました");
        }
```

- [ ] **Step 6: トラック操作へ履歴要求を足す**

`Timeline/Manager/TimelineManager.cs:1248` の `AddTrack` 末尾へ 1 行足す。

```csharp
            timeline.tracks.Add(new TrackData
            {
                name = trackName,
                startFrameNo = 0,
                endFrameNo = timeline.maxFrameNo,
            });

            RequestHistory("トラック追加");
        }
```

`SetActiveTrack`（`:1273`）末尾へ 1 行足す。

```csharp
            if (timeline.activeTrackIndex >= 0)
            {
                SetPlayingFrameNoAll(track.startFrameNo);
            }

            RequestHistory("トラック選択");
        }
```

`MoveUpTrack`（`:1296`）と `MoveDownTrack`（`:1311`）の末尾へ、**`SetActiveTrack` の呼び出しより後**に 1 行足す（`RequestHistory` は単一スロットなので、後から呼んだ説明が残る）。

```csharp
            var activeTrack = timeline.activeTrack;
            timeline.tracks.RemoveAt(index);
            timeline.tracks.Insert(index - 1, track);

            SetActiveTrack(activeTrack, true);

            RequestHistory("トラック並べ替え");
        }
```

`MoveDownTrack` も同じ末尾 1 行（`RequestHistory("トラック並べ替え");`）を `SetActiveTrack(activeTrack, true);` の後に足す。

`RemoveTrack`（`:1286`）は既に `SetActiveTrack` の後で `RequestHistory("トラック削除")` を呼んでいるので変更不要。

- [ ] **Step 7: `最終フレーム` へ履歴要求を足す**

`Timeline/Manager/TimelineManager.cs:925` の `SetMaxFrameNo` を次に置き換える。

```csharp
        public void SetMaxFrameNo(int maxFrameNo)
        {
            timeline.maxFrameNo = maxFrameNo;

            ApplyCurrentFrame(true);
            Refresh();

            RequestHistory("最終フレーム変更");
        }
```

- [ ] **Step 8: トラック名・範囲の変更で履歴を要求する**

`TimelineSettingWindow.cs` の `DrawTrack` で、名前欄（`:556`）を次に置き換える。

```csharp
                // 並べ替えボタン (右端 30px) に被らない幅で名前欄を取る
                view.DrawTextField(track.name, width - 30 - view.currentPos.x, ROW_HEIGHT, newText =>
                {
                    track.name = newText;
                    timelineManager.RequestHistory("トラック変更");
                });
```

範囲欄（`:566-585`）の `updated` 判定ブロックを次に置き換える。`RequestHistory` はマウス解放時にまとめて確定するので、ドラッグ中の連続変更は 1 件になる。

```csharp
                // 再生中のトラックの範囲を変えたときだけ、その場で再生位置へ反映する
                if (updated)
                {
                    if (track == timeline.activeTrack)
                    {
                        timelineManager.ApplyCurrentFrame(true);
                    }
                    timelineManager.RequestHistory("トラック変更");
                }
```

- [ ] **Step 9: `RequestHistory("タイムライン新規作成")` の残骸が無いことを確認する**

Run: `grep -n 'タイムライン新規作成' source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`
Expected: 出力なし（Step 5 で置き換え済み）

- [ ] **Step 10: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、`dotnet test` 全 PASS

- [ ] **Step 11: 実機で確認する**

ゲームを起動した状態で MCP `com3d25-devbridge` を使い、次を手で確認する（UI 操作が要るので `eval_csharp` では代替できない）。

1. タイムラインを新規作成し、すぐキーを 1 つ打って `Ctrl+Z` → キーが消える
2. `タイムライン設定` → `トラック` タブで `追加` → `Ctrl+Z` → トラックが消える
3. 同タブでトラックの範囲欄をドラッグ → 1 回の `Ctrl+Z` で元の範囲へ戻る
4. `最終フレーム` を変更 → `Ctrl+Z` で戻る

`mcp__com3d25-devbridge__tail_log` で例外が出ていないことも見る。

- [ ] **Step 12: 仕様書を直す**

`docs-site/timeline/editing.md:114-121` の操作履歴節を次に置き換える。

```markdown
## 操作履歴（Undo / Redo）

タイムラインの操作は SceneEditor の操作履歴に統合されており、`Ctrl` + `Z` / `Ctrl` + `X` で戻す・進めるができます。

- キーフレームの登録・削除・移動・ペースト、レイヤーの追加・削除、フレーム挿入 / 削除 / 複製、トラックの追加・削除・並べ替え・名前 / 範囲の変更、`最終フレーム` の変更、キーフレーム詳細やカーブエディタでの編集、テンプレート適用などが記録されます
- ドラッグ操作はマウスを離した時点で 1 件として記録されます
- タイムライン読み込み中はシーンの操作（メイドの追加など）は履歴に載りません。各ウィンドウの値変更は自動登録でキーになるため、タイムラインの履歴で戻せます
```

`docs-site/timeline/compatibility.md:42` の行

```markdown
- 新規作成・読み込み直後の最初の操作は Undo できません
```

を削除する。同ファイル `:120` 付近（`editing.md` 側と重複していた記述）に該当行がある場合も同様に削除する。

Run: `grep -rn '最初の操作は' docs-site/timeline/`
Expected: 出力なし

- [ ] **Step 13: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs \
        source/COM3D2.SceneEditor.Plugin/TimelineSettingWindow.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/TimelineHistoryBaselineTests.cs \
        docs-site/timeline/editing.md docs-site/timeline/compatibility.md
git commit -m "$(cat <<'EOF'
fix(timeline): トラック操作と最終フレームを Undo できるようにする

読込・新規作成の直後に履歴の基準 XML を先取りし、1 手目が基準作りに
消費されないようにした。トラックの追加・選択・並べ替え・名前/範囲変更と
最終フレーム変更にも履歴要求を足した。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 2: 0 フレーム目のキー必須という制約を外す（項目 13）

`メイドアニメ` / `メイド瞳` / `メイド表情` / `メイド脱衣` の `IsValidData` が持つ 0 フレーム検証を削除し、他レイヤーと同じ「先頭キーより手前は何も適用しない」挙動へそろえる。**「設計書からの逸脱」3 に根拠を書いてあるので、着手前に必ず読むこと。**

このタスクは削除が主体で、新しい純粋ロジックが生まれないため単体テストを追加しない。検証はビルドと実機で行う。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MotionTimelineLayer.cs:249-261`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/EyesTimelineLayer.cs:87-99`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/MorphTimelineLayer.cs:163-175`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/UndressTimelineLayer.cs:50-62`
- Modify: `docs-site/timeline/layers.md:109-113`
- Modify: `docs-site/timeline/files.md:40`

**Interfaces:**
- Consumes: なし
- Produces: 「どのレイヤーも 0 フレーム目のキーを必須としない」という不変条件。以降のタスクでは使わない

- [ ] **Step 1: 検証を持つ 4 レイヤーを洗い出して、他に無いことを確かめる**

Run: `grep -rn '0フレーム目にキーフレームが必要です' source/COM3D2.SceneEditor.Plugin/`
Expected: `MotionTimelineLayer.cs` / `EyesTimelineLayer.cs` / `MorphTimelineLayer.cs` / `UndressTimelineLayer.cs` の 4 件だけ。5 件以上出た場合はここで止め、増えたファイルを報告する

- [ ] **Step 2: 4 レイヤーの `IsValidData` から 0 フレーム検証を削除する**

4 ファイルとも `IsValidData` は「0 フレーム検証だけを行い、通れば true」という同じ形をしている。それぞれ次の形へ置き換える（`errorMessage` のクリアは他所が読むので残す）。

`MotionTimelineLayer.cs:249`:

```csharp
        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }
```

`EyesTimelineLayer.cs:87` / `MorphTimelineLayer.cs:163` / `UndressTimelineLayer.cs:50` も、`override` 修飾子とメソッド名はそのままに同じ 4 行の本体へ置き換える。`var firstFrame = this.firstFrame;` の行ごと消えるので、それによって未使用になる `using` が出ないかコンパイラの警告で確認する。

置き換え後、`BGColorTimelineLayer.cs:68-72` と同じ「常に true を返す `IsValidData`」が 4 つ増える形になる。

- [ ] **Step 3: 検証が消えたことを確かめる**

Run: `grep -rn '0フレーム目にキーフレームが必要です' source/`
Expected: 出力なし（`bin/` `obj/` のビルド成果物は次のビルドで更新されるので、ここで出ても構わない。ソースに無ければよい）

Run: `grep -rn 'firstFrame' source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/`
Expected: `TimelineLayerBase.cs` の `firstFrame` プロパティ定義・`Init` の生成・`AddFirstBones`、`MorphTimelineLayer.cs:77` の `faceForceOverride` 用の 3 系統だけが残る。これらは 0 フレーム目にキーを**作る**側なので消さない

- [ ] **Step 4: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、既存テスト全 PASS（このタスクで追加するテストは無い）

- [ ] **Step 5: 実機で確認する**

`IsValidData` が false のときタイムライン機構全体が止まる（`TimelineIntegration.UpdateGuards`）ため、「止まらなくなったこと」が確認の主眼になる。

1. `メイドアニメ` の 0 フレーム目のキーを選んで `Backspace` → キーが消え、**タイムラインは止まらない**（再生・シーク・他レイヤーの編集が続けられる）。`mcp__com3d25-devbridge__tail_log` に `0フレーム目にキーフレームが必要です` が出ない
2. その状態で `保存` → エラーにならず保存できる。`アニメ出力` も通る
3. 保存した XML を読み直し、先頭キー（例: 10F）より手前へシーク → メイドは 10F のポーズを保ったままになる（`AnimationCurve` が先頭値を保持する）。フリーズやエラーは起きない
4. `メイド表情` の 0 フレームキーを消して読み直す → `MorphTimelineLayer.Init`（`:77`）が `faceForceOverride` のキーを 0 フレームへ作り直す。表情の強制上書きが ON として読まれる
5. `メイド瞳` / `メイド脱衣` でも 1 と同じ操作を行い、止まらないことを見る

- [ ] **Step 6: 仕様書を直す**

`docs-site/timeline/layers.md:109-113` の節

```markdown
## 0 フレーム目のキー

`メイドアニメ` / `メイド表情` / `メイド瞳` / `メイド脱衣` は 0 フレーム目にキーが必要です。
無いと `0フレーム目にキーフレームが必要です` と表示され、保存と `アニメ出力` ができなくなります。
```

を次に置き換える。

```markdown
## 0 フレーム目のキー

新しく作ったレイヤーには 0 フレーム目にキーが入りますが、これは必須ではありません。
0 フレーム目のキーを消すと、先頭のキーより手前のフレームではそのレイヤーは何もしません（`メイドアニメ` は先頭キーのポーズを保ちます）。
`メイド表情` だけは、タイムラインを読み込み直したときに表情の強制上書き設定が 0 フレーム目へ入り直します。
```

`docs-site/timeline/files.md:40` の行

```markdown
| `0フレーム目にキーフレームが必要です` | メイドアニメなどの 0 フレーム目にキーが無い |
```

を削除する。

Run: `grep -rn '0フレーム目にキーフレームが必要です' docs-site/`
Expected: 出力なし

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ \
        docs-site/timeline/layers.md docs-site/timeline/files.md
git commit -m "$(cat <<'EOF'
fix(timeline): 0 フレーム目のキー必須という制約を外す

メイドアニメ・瞳・表情・脱衣の IsValidData が持っていた 0 フレーム検証を
削除し、他レイヤーと同じ「先頭キーより手前は何も適用しない」挙動へそろえた。

この検証は MTE からの移植で持ち込まれたもので、再生も anm 出力も 0 フレーム
キーを必要としない。一方で IsValidData は TimelineIntegration.UpdateGuards
から呼ばれるため、キーを 1 つ消しただけでタイムライン機構全体の更新・保存・
アニメ出力が止まっていた。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---


### Task 3: サブカメラレイヤーの削除で実体を壊さない（項目 14）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SubCameraTimelineLayer.cs:46-55`
- Modify: `docs-site/timeline/layers.md:75`
- Modify: `docs-site/timeline/layers-camera.md:31`

**Interfaces:**
- Consumes: なし
- Produces: 「レイヤーの `Dispose` はシーン上の実体を変えない」という全レイヤー共通の不変条件

- [ ] **Step 1: `DestroyAllCameras` の呼び出しを外す**

`Timeline/TimelineLayer/SubCameraTimelineLayer.cs:46` の `Dispose` を次に置き換える。

```csharp
        public override void Dispose()
        {
            base.Dispose();

            SubCameraManager.onCameraAdded -= OnCameraAdded;
            SubCameraManager.onCameraRemoved -= OnCameraRemoved;

            // レイヤー削除はシーン上の実体を変えない (全レイヤー共通)。
            // サブカメラの削除は Camera ウィンドウの「サブカメラ数」で行う
        }
```

- [ ] **Step 2: 他レイヤーが実体を壊していないことを確認する**

Run: `grep -rn 'Destroy' source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/*.cs`
Expected: `Dispose` の中から実体を破棄している箇所が無いこと（`BGColorTimelineLayer.cs:50` のコメントどおり、地面は `BGGroundManager` が持つ）

- [ ] **Step 3: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、全テスト PASS

- [ ] **Step 4: 実機で確認する**

サブカメラを 2 台にした状態で `サブカメラ` レイヤーを削除し、`mcp__com3d25-devbridge__screenshot` で小窓が残っていることを見る。さらに Camera ウィンドウの `サブカメラ数` を 0 にすると消えることも確認する。

- [ ] **Step 5: 仕様書を直す**

`docs-site/timeline/layers.md:75` の行

```markdown
- `サブカメラ` レイヤーを削除するとサブカメラの実体も破棄されます
```

を次に置き換える。

```markdown
- レイヤーを削除してもシーン上の実体（サブカメラ、地面、モデルなど）は変わりません
```

`docs-site/timeline/layers-camera.md:31` の行

```markdown
- `サブカメラ` レイヤーを削除するとサブカメラの実体もすべて破棄されます
```

を次に置き換える。

```markdown
- `サブカメラ` レイヤーを削除してもサブカメラの実体は残ります。台数を減らすには Camera ウィンドウの `サブカメラ数` を使ってください
```

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SubCameraTimelineLayer.cs \
        docs-site/timeline/layers.md docs-site/timeline/layers-camera.md
git commit -m "$(cat <<'EOF'
fix(timeline): サブカメラレイヤーの削除で実体を破棄しないようにする

レイヤー削除はシーン上の実体を変えない、という他レイヤーと同じ規則に揃えた。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 4: 手動「登録」の対象を編集対象レイヤー全部に一本化する（項目 9 / 10）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:2050-2093,2106-2145`
- Modify: `docs-site/timeline/editing.md:41-50`
- Modify: `docs-site/timeline/control.md:92-94`
- Modify: `docs-site/timeline/layers-camera.md:21,42`

**Interfaces:**
- Consumes: なし
- Produces: 「`AddKeyFrameDiff` の対象は `isAuto` に関わらず `editTargetLayers`。カメラ系だけ `layer.isCurrent` で絞る」という規則

- [ ] **Step 1: 登録対象の切り替えを外す**

`Timeline/Manager/TimelineManager.cs:2144` の行

```csharp
            var targetLayers = isAuto ? editTargetLayers : BuildManualKeyFrameLayers();
```

とその直前のコメント 3 行（`// 手動登録は画面に出ているレイヤーだけを対象にする。` から `// (表示範囲外のモデル等を動かしたときに登録が漏れると気づけないため)` まで）を次に置き換える。

```csharp
            // 手動・自動とも編集対象レイヤー全てを見る。
            // 差分登録なので値の変わっていないレイヤーにキーは増えず、
            // 表示範囲外のモデル等を動かしたときの登録漏れだけを防げる
            var targetLayers = editTargetLayers;
```

- [ ] **Step 2: カメラ系の除外条件から `isCameraSync` を落とす**

同ファイル `:2106-2116` の `ShouldSkipCameraKeyFrame` を次に置き換える。

```csharp
        /// <summary>
        /// カメラレイヤーへの差分キーフレーム登録を見送るか。
        /// カメラはカメラ同期で常時動いており、他の操作のたびに意図しないキーフレームが
        /// 増えてしまうため自動登録の対象から外す。
        /// 手動の「登録」では、そのレイヤーがアクティブなときだけ記録する
        /// (カメラ同期の ON/OFF は再生への反映だけを決め、登録可否には関わらない)
        /// </summary>
        private bool ShouldSkipCameraKeyFrame(ITimelineLayer layer, bool isAuto)
        {
            return isAuto || !layer.isCurrent;
        }
```

- [ ] **Step 3: 使われなくなった絞り込みを消す**

同ファイル `:2050-2093` から次を削除する。

- `_editTargetLayerBuffer` と `_manualKeyFrameLayers` のフィールド宣言と、その上のコメント 3 行（`// 手動キーフレーム登録のスコープ計算バッファ。` から `// yield ベースの editTargetLayers を一度 _editTargetLayerBuffer へ移す` まで）
- `BuildManualKeyFrameLayers` メソッド全体（XML ドキュメントコメント含む）

`TimelineLayerViewFilter` 自体はグリッド表示用に残す（`TimelineWindow.cs:235,588` から使われている）。

- [ ] **Step 4: 残骸が無いことを確認する**

Run: `grep -rn 'BuildManualKeyFrameLayers\|_manualKeyFrameLayers\|_editTargetLayerBuffer' source/`
Expected: 出力なし

Run: `grep -rn 'TimelineLayerViewFilter' source/COM3D2.SceneEditor.Plugin/`
Expected: `Timeline/TimelineLayerViewFilter.cs` と `TimelineWindow.cs` の 2 ファイルだけ

- [ ] **Step 5: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、全テスト PASS

- [ ] **Step 6: 実機で確認する**

1. メイドカテゴリを表示した状態でライトを動かして `Enter` → ライトレイヤーにキーが増える（グリッドをライトカテゴリへ切り替えて確認）
2. カメラレイヤーが非アクティブな状態でカメラを動かして `Enter` → カメラにキーは増えない
3. カメラレイヤーをアクティブにしてカメラを動かして `Enter` → キーが増える。`タイムライン操作` の `カメラ同期` を OFF にしても同じ結果になる

- [ ] **Step 7: 仕様書を直す**

`docs-site/timeline/editing.md:41-50` を次に置き換える。

```markdown
### 登録の対象範囲

`登録` は、操作対象メイドのレイヤーとメイドに紐づかないレイヤーのうち、編集開始時から値が変わったものすべてに打たれます。表示モードやカテゴリには依存しません。
カメラとサブカメラだけは、そのレイヤーがアクティブなときにしか登録されません。
```

`docs-site/timeline/control.md:83` の `カメラ同期` の説明行を次に置き換える。

```markdown
| `カメラ同期` | カメラ系レイヤーの再生内容を実際のカメラへ反映します。既定 ON。キーの登録可否には影響しません |
```

同ファイル `:92-94` の info ブロック 3 行

```markdown
::: info カメラ同期 OFF のときのカメラレイヤー
`カメラ同期` が OFF で、かつカメラ系レイヤーがアクティブでないときは、カメラレイヤーは再生に反映されず、`登録` でもカメラのキーは打たれません。
:::
```

を次に置き換える。

```markdown
::: info カメラ同期 OFF のときのカメラレイヤー
`カメラ同期` が OFF で、かつカメラ系レイヤーがアクティブでないときは、カメラレイヤーは再生に反映されません。`登録` でキーを打てるかどうかは、カメラ系レイヤーがアクティブかどうかだけで決まります。
:::
```

`docs-site/timeline/layers-camera.md:21` の行

```markdown
- `タイムライン操作` の `カメラ同期`（既定 ON）が OFF で、かつカメラ系レイヤーがアクティブでないときは、カメラレイヤーは再生に反映されず `登録` でもキーが打たれません
```

を次に置き換える。

```markdown
- `タイムライン操作` の `カメラ同期`（既定 ON）が OFF で、かつカメラ系レイヤーがアクティブでないときは、カメラレイヤーは再生に反映されません
- `登録` でカメラのキーが打たれるのは、カメラ系レイヤーがアクティブなときだけです（`カメラ同期` の値には依りません）
```

同ファイル `:42` の行末を次に置き換える。

```markdown
`VP` は画面内の小窓の位置とサイズです。登録と自動登録の扱いはカメラレイヤーと同じです。
```

Run: `grep -rn '登録.*でもキーが打たれ\|登録. でもカメラのキー' docs-site/timeline/`
Expected: 出力なし

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs \
        docs-site/timeline/editing.md docs-site/timeline/control.md docs-site/timeline/layers-camera.md
git commit -m "$(cat <<'EOF'
fix(timeline): 手動「登録」の対象を編集対象レイヤー全部に一本化する

表示モードによる絞り込みをやめ、手動・自動とも editTargetLayers を対象にした。
カメラ系はアクティブなときだけ登録するようにし、カメラ同期は登録可否から外した。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 5: テキスト・サブカメラ VP・背景色・地面色・PNG の SZ を補間する（項目 17）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TextTimelineLayer.cs:127-157`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/SubCameraTimelineLayer.cs:158-165`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBGColor.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBGGroundColor.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BGColorTimelineLayer.cs:86-145`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPngObject.cs:75-90`
- Modify: `docs-site/timeline/layers-other.md:24-27`
- Modify: `docs-site/timeline/layers-camera.md:41`
- Modify: `docs-site/timeline/layers-background.md:14`
- Modify: `docs-site/timeline/compatibility.md`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TangentValueCoverageTests.cs`（新規）

**Interfaces:**
- Consumes: なし
- Produces: `TransformDataBGColor` / `TransformDataBGGroundColor` が `hasTangent == true` であること。`TransformDataPngObject.tangentValues` が `ScaleZ` を含むこと

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TangentValueCoverageTests.cs` を新規作成する。`TransformData*` は `Initialize(name)` を呼べば Unity ランタイム無しで値配列を作れる（`config` はシングルトン経由で既定値が返る）。既存の `ColorValueInfoTests` と同じ作法に倣う。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TangentValueCoverageTests
    {
        [Fact]
        public void 背景色はタンジェントを持ち色成分は対象外()
        {
            var trans = new TransformDataBGColor();
            trans.Initialize("BGColor");

            Assert.True(trans.hasTangent);
            // valueCount は色 RGB の 3 つだけなので、色を除くと対象は空になる
            Assert.Empty(trans.tangentValues);
        }

        [Fact]
        public void 地面色は位置と広さと表示にタンジェントを持つ()
        {
            var trans = new TransformDataBGGroundColor();
            trans.Initialize("BGGroundColor");

            Assert.True(trans.hasTangent);
            // valueCount 10 のうち色 RGB 3 つを除いた 7 つ (位置 3 + 広さ 3 + 表示 1)
            Assert.Equal(7, trans.tangentValues.Length);
        }

        [Fact]
        public void PNG配置のタンジェント対象にSZが含まれる()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("Png0");

            Assert.Contains(trans.scalezValue, trans.tangentValues);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter TangentValueCoverageTests`
Expected: 3 件とも FAIL（`hasTangent` が false、`tangentValues` が空、`scalezValue` 未収録）

- [ ] **Step 3: 背景色・地面色へタンジェントを足す**

`Timeline/TransformData/TransformDataBGColor.cs` の `valueCount` の次へ追加する。

```csharp
        public override int valueCount => 3;

        // 色は Color.Lerp で線形補間するため、タンジェントの対象からは外す。
        // (valuesWithoutColors はこの型では空配列になる)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;
```

`Timeline/TransformData/TransformDataBGGroundColor.cs` の `hasVisible` の次へ追加する。

```csharp
        public override bool hasVisible => true;

        // 位置・広さはタンジェント補間、色は Color.Lerp で線形補間する
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;
```

- [ ] **Step 4: PNG 配置の `tangentValues` へ `ScaleZ` を足す**

`Timeline/TransformData/TransformDataPngObject.cs:84-87` を次に置き換える。

```csharp
                    _tangentValues.AddRange(new ValueData[] { 
                        values[(int)Index.ScaleX], 
                        values[(int)Index.ScaleZ], 
                        values[(int)Index.PrimitiveReferenceX] 
                    });
```

- [ ] **Step 5: テストが通ることを確認する**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、`TangentValueCoverageTests` 3 件 PASS

- [ ] **Step 6: テキストの位置・角度・拡縮を Hermite 補間にする**

`Timeline/TimelineLayer/TextTimelineLayer.cs:127` の `ApplyMotionUpdate` を次に置き換える。

```csharp
        private void ApplyMotionUpdate(MotionData motion, float t)
        {
            var start = motion.start as TransformDataText;
            var end = motion.end as TransformDataText;

            if (!textManager.IsValidIndex(start.index))
            {
                return;
            }

            var freeTextSet = textManager.GetFreeTextSet(start.index);

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (start.position != end.position)
            {
                freeTextSet.rect.localPosition = PluginUtils.HermiteVector3(
                    t0, t1, start.positionValues, end.positionValues, t);
            }
            if (start.eulerAngles != end.eulerAngles)
            {
                freeTextSet.rect.eulerAngles = PluginUtils.HermiteVector3(
                    t0, t1, start.eulerAnglesValues, end.eulerAnglesValues, t);
            }
            if (start.scale != end.scale)
            {
                freeTextSet.rect.localScale = PluginUtils.HermiteVector3(
                    t0, t1, start.scaleValues, end.scaleValues, t);
            }
            // 色は他レイヤーと同じく線形補間で統一する
            if (start.color != end.color)
            {
                freeTextSet.text.color = Color.Lerp(start.color, end.color, t);
            }

            textManager.UpdateFreeTextSet(start.index, freeTextSet);
        }
```

- [ ] **Step 7: サブカメラの VP を Hermite 補間にする**

`Timeline/TimelineLayer/SubCameraTimelineLayer.cs:158-165` の viewport 計算を次に置き換える。

```csharp
            // VP もタンジェントを持つので、他の値と同じ補間にそろえる
            var viewportValues = PluginUtils.HermiteValues(
                t0, t1, start.viewportValues, end.viewportValues, t);
            var viewportRect = new Rect(
                viewportValues[0], viewportValues[1], viewportValues[2], viewportValues[3]);
```

- [ ] **Step 8: 背景色・地面色に補間経路を足す**

`Timeline/TimelineLayer/BGColorTimelineLayer.cs:86` の `ApplyMotion` を次に置き換える。

```csharp
        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.BGColor:
                    ApplyBGColorMotionUpdate(motion, t);
                    break;
                case TransformType.BGGroundColor:
                    if (indexUpdated)
                    {
                        ApplyBGGroundColorMotionInit(motion, t);
                    }
                    ApplyBGGroundColorMotionUpdate(motion, t);
                    break;
            }
        }

        /// <summary>背景色。色だけなので区間の開始・終了を線形補間する</summary>
        private void ApplyBGColorMotionUpdate(MotionData motion, float t)
        {
            try
            {
                var start = motion.start;
                var end = motion.end;
                camera.backgroundColor = Color.Lerp(start.color, end.color, t);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>地面色の補間されない値 (表示)。区間の開始で切り替える</summary>
        private void ApplyBGGroundColorMotionInit(MotionData motion, float t)
        {
            try
            {
                var start = motion.start;

                if (bgGround != null)
                {
                    var visible = start.visible;
                    if (timeline.isGroundLinkedToBackground && !timeline.isBackgroundVisible)
                    {
                        visible = false;
                    }

                    bgGround.visible = visible;
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>地面色の補間される値。位置・広さはタンジェント、色は線形</summary>
        private void ApplyBGGroundColorMotionUpdate(MotionData motion, float t)
        {
            try
            {
                if (bgGround == null)
                {
                    return;
                }

                var start = motion.start;
                var end = motion.end;
                var t0 = motion.stFrame * timeline.frameDuration;
                var t1 = motion.edFrame * timeline.frameDuration;

                bgGround.position = PluginUtils.HermiteVector3(
                    t0, t1, start.positionValues, end.positionValues, t);
                bgGround.scale = PluginUtils.HermiteVector3(
                    t0, t1, start.scaleValues, end.scaleValues, t);
                bgGround.color = Color.Lerp(start.color, end.color, t);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

- [ ] **Step 9: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、全テスト PASS

- [ ] **Step 10: 実機で確認する**

1. テキストの位置キーを 0 / 30 フレームに打ち、Inspector の `補間曲線` タブで片側を `EaseIn` にする → 15 フレーム付近の位置が線形の中点からずれる
2. `背景色` を 0 フレーム赤 / 30 フレーム青で打ち、15 フレームへシークして `mcp__com3d25-devbridge__screenshot` で中間色になっていることを見る
3. `地面色` の位置キーを 2 つ打ってシーク → 地面がなめらかに動く
4. `PNG配置` の `SZ` に 2 キー打ち、Inspector の `補間曲線` タブに `SZ` のタンジェント欄が出ることを確認する
5. サブカメラの `VP X` に 2 キー打ってタンジェントを付け、小窓が非線形に動くことを見る

- [ ] **Step 11: 仕様書を直す**

`docs-site/timeline/layers-other.md:24-27` の表を次に置き換える。

```markdown
| 値 | 補間 |
|---|---|
| 位置 / 回転 / 拡縮 | タンジェント補間 |
| 色 | 線形補間 |
| `本文` / `フォント` / `サイズ` / `行間` / `整列` / `幅` / `高さ` | なし（区間開始値） |
```

`docs-site/timeline/layers-camera.md:41` の行

```markdown
| `VP X` / `VP Y` / `VP 幅` / `VP 高` | — | 線形補間 |
```

を次に置き換える。

```markdown
| `VP X` / `VP Y` / `VP 幅` / `VP 高` | — | タンジェント補間 |
```

`docs-site/timeline/layers-background.md:14` を次に置き換える。

```markdown
項目は `背景色` と `地面色` の 2 つ固定です。`背景色` と `地面色` の色は線形補間、地面の位置と広さはタンジェント補間されます。`表示` は区間の開始で切り替わります。
```

`docs-site/timeline/compatibility.md` の「その他の制限」節の末尾へ 1 行足す。

```markdown
- `背景色` / `地面色` は v34 以前のタイムラインでは補間されず段差になっていました。読み込むと補間されるようになり、旧データのキーは両端がゆるやかな（タンジェント 0 の）補間になります。段差を意図していた区間は `1フレーム調整` で再現できます
```

Run: `grep -rn 'なめらかに変えたいときはキーを細かく' docs-site/timeline/`
Expected: 出力なし

- [ ] **Step 12: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ \
        source/COM3D2.SceneEditor.Plugin.Tests/TangentValueCoverageTests.cs \
        docs-site/timeline/layers-other.md docs-site/timeline/layers-camera.md \
        docs-site/timeline/layers-background.md docs-site/timeline/compatibility.md
git commit -m "$(cat <<'EOF'
feat(timeline): テキスト・サブカメラ VP・背景色・地面色・PNG の SZ を補間する

タンジェントを持ちながら Lerp やスナップで適用されていた値を、他レイヤーと
同じ補間へそろえた。XML 形式は変わらない。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 6: カーブエディタで回転の Euler 表示からタンジェントを編集できるようにする（項目 18）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs:167-180,483-500,736-780,818-875,1039-1058,1722-1731`
- Modify: `docs-site/timeline/interpolation.md:63`

**Interfaces:**
- Consumes: なし
- Produces: `CurveChannel.GetTangents(int keyIndex, bool isOut)` — そのキーの片側タンジェント。通常チャンネルは 1 本、Euler 表示チャンネルは回転 4 成分の 4 本を返す

- [ ] **Step 1: チャンネルからタンジェントを引く口を足す**

`TimelineCurveEditor.cs:236-239` の `GetKeyValue` の隣（`CurveChannel` クラス末尾）へ追加する。

```csharp
            /// <summary>キー位置の表示値。Euler 表示では連続化済みの角度 (度)</summary>
            public float GetKeyValue(int i)
            {
                return isEulerDisplay ? eulerKeyValues[i] : values[i].value;
            }

            /// <summary>
            /// キー i の片側タンジェント。通常チャンネルは代表値の 1 本。
            /// Euler 表示は表示値から 4 成分への逆変換ができないため、
            /// 同じ正規化値を回転 4 成分すべてへ反映する (4 本を返す)
            /// </summary>
            public MTEP.TangentData[] GetTangents(int i, bool isOut)
            {
                if (!isEulerDisplay)
                {
                    return new[] { isOut ? values[i].outTangent : values[i].inTangent };
                }

                var rotation = rotationValues[i];
                var result = new MTEP.TangentData[rotation.Length];
                for (var k = 0; k < rotation.Length; k++)
                {
                    result[k] = isOut ? rotation[k].outTangent : rotation[k].inTangent;
                }
                return result;
            }
```

- [ ] **Step 2: ドラッグ対象を 1 本から複数本へ広げる**

`TimelineCurveEditor.cs:174` のフィールド宣言

```csharp
        private MTEP.TangentData _dragTangent = null;
```

を次に置き換える。

```csharp
        /// <summary>ドラッグ中のタンジェント。Euler 表示では回転 4 成分がまとめて入る</summary>
        private MTEP.TangentData[] _dragTangents = null;
```

- [ ] **Step 3: ハンドルのヒットテストから Euler 除外を外す**

`TimelineCurveEditor.cs:736-744` のハンドルヒットテストのループ先頭から、次の 5 行を削除する。

```csharp
                // Euler 表示チャンネルは表示専用 (タンジェント編集も不可)
                if (channel.isEulerDisplay)
                {
                    continue;
                }
```

同ループ内の `_dragMode = DragMode.Tangent;` から `return;` まで（`:765-774`）を次に置き換える。

```csharp
                        _dragMode = DragMode.Tangent;
                        _dragValue = channel.values[i];
                        _dragTangents = channel.GetTangents(i, isOut);
                        _dragTangentIsOut = isOut;
                        _dragBaseSlopePerFrame = baseSlope;
                        _dragKeyFrameNo = channel.frameNos[i];
                        _dragKeyValue = channel.GetKeyValue(i);
                        _dragChanged = false;
                        e.Use();
                        return;
```

値ドラッグ側のループ（`:778-783`）の Euler 除外はそのまま残す。コメントを次に限定する。

```csharp
            foreach (var channel in _channels)
            {
                // 値ドラッグだけは不可。Euler 表示値から回転 4 成分への逆変換が無いため
                if (channel.isEulerDisplay)
                {
                    continue;
                }
```

- [ ] **Step 4: ドラッグ適用を複数本へ流す**

`TimelineCurveEditor.cs:850-858` を次に置き換える。

```csharp
            if (_dragTangents.Length == 0)
            {
                return;
            }
            var current = _dragTangents[0];
            if (normalized == current.normalizedValue && !current.isSmooth)
            {
                return;
            }

            foreach (var tangent in _dragTangents)
            {
                tangent.normalizedValue = normalized;
                tangent.isSmooth = false;
            }
            _dragChanged = true;
            currentLayer.ApplyCurrentFrame(true);
        }
```

`EndDrag`（`:861-874`）の `_dragTangent = null;` を `_dragTangents = null;` に置き換える。

- [ ] **Step 5: ハンドル描画の Euler 除外を外し、位置計算を正規化値ベースにする**

`TimelineCurveEditor.cs:1725-1730` の `DrawChannelHandles` 冒頭から、次の 5 行を削除する。

```csharp
            // Euler 表示チャンネルは表示専用のためハンドルを出さない
            if (channel.isEulerDisplay)
            {
                return;
            }
```

`GetHandlePos`（`:1039`）を次に置き換える。`TangentData.value` は成分の値域で計算されており、Euler 表示の角度スケールと合わない。`value = normalizedValue * baseTangent`（`TangentData.UpdateValue`）なので、表示値から求めた区間勾配へ正規化値を掛け直せば、どちらのチャンネルでも同じ式で描ける。

```csharp
        /// <summary>ハンドル先端のペイン内座標。
        /// 勾配は「そのチャンネルの表示値での区間勾配 × 正規化値」で求める。
        /// TangentData.value は成分の値域で計算されており Euler 表示の角度とは
        /// スケールが違うため、value ではなく normalizedValue から組み立てる</summary>
        private Vector2 GetHandlePos(
            CurveChannel channel, int keyIndex, bool isOut, float scrollX)
        {
            var keyX = _mapping.FrameToX(channel.frameNos[keyIndex]) - scrollX;
            var keyY = _mapping.ValueToY(channel.GetKeyValue(keyIndex));

            var tangents = channel.GetTangents(keyIndex, isOut);
            var normalized = tangents.Length > 0 ? tangents[0].normalizedValue : 0f;

            float baseSlopePerFrame;
            if (!TryGetBaseSlopePerFrame(channel, keyIndex, isOut, out baseSlopePerFrame))
            {
                baseSlopePerFrame = 0f;
            }

            var slopePerFrame = normalized * baseSlopePerFrame;
            var pxPerValue = _mapping.paneHeight / (_mapping.valueMax - _mapping.valueMin);

            var dx = isOut ? _mapping.frameWidth : -_mapping.frameWidth;
            var dy = -slopePerFrame * pxPerValue * (isOut ? 1f : -1f);

            var dir = new Vector2(dx, dy).normalized;
            return new Vector2(keyX, keyY) + dir * HANDLE_LEN;
        }
```

- [ ] **Step 6: ツールバーの入力欄を Euler 表示でも有効にする**

`TimelineCurveEditor.cs:483-489` の `HasEditableTangent` から、次の 5 行を削除する。

```csharp
                // Euler 表示は導出値のためタンジェント編集の対象外
                if (channel.isEulerDisplay)
                {
                    continue;
                }
```

`HasEditableTangent` は残りの走査で `channel.values[i]` と `TryGetBaseSlopePerFrame` を使う。Euler 表示チャンネルの `values` は空ではなく、`BuildQuaternionSourceChannel`（`:1336-1337`）が回転の代表成分 (x) を積んでいるのでキー数の境界として正しく働き、勾配は `GetKeyValue` 経由で Euler 表示値から求まる。除外を外しても添字ずれや null 参照は起きない。

`ForEachTangent`（`:563`）はレイヤーの `selectedBones` と `_targets.current` から直接タンジェントを引いており、`回転` / `X回転` などを選べば元の 4 成分（または該当軸）に届く。Euler 表示チャンネルの有無に依存しないので変更しない。

- [ ] **Step 7: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、全テスト PASS。特に既存の `CurveViewMappingTests` と `KeyFrameTangentLogicTests` が落ちていないこと

- [ ] **Step 8: 実機で確認する**

`メイド移動` の回転に 0 / 30 フレームでキーを打ち、30 フレーム側を選択してカーブエディタを開く。値種別を `回転` にして `EulerX` のハンドルを動かしたあと、MCP `com3d25-devbridge` の `eval_csharp` で 4 成分のタンジェントが同値になっているか見る。

```csharp
var layer = COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance.currentLayer;
var bone = layer.GetFrame(30).GetBone("Bip01");
var trans = bone.transform;
var outs = trans.GetInTangentDataList(COM3D2.MotionTimelineEditor.Plugin.TangentValueType.回転);
string.Join(", ", System.Linq.Enumerable.Select(outs, x => x.normalizedValue.ToString("F4")).ToArray())
```

Expected: 4 つとも同じ値（ボーン名は実際に選んだ項目に合わせる）。あわせて、値ドラッグ（キー点の上下ドラッグ）が Euler カーブでは効かないままであることも確認する。

- [ ] **Step 9: 仕様書を直す**

`docs-site/timeline/interpolation.md:63` の行

```markdown
- クォータニオンで保持される回転は、表示用のオイラー角 3 本に置き換えて描かれます。**これは表示専用で、値もタンジェントも編集できません**
```

を次に置き換える。

```markdown
- クォータニオンで保持される回転は、表示用のオイラー角 3 本に置き換えて描かれます。**値は編集できません**が、タンジェントは編集でき、回転の 4 成分すべてへ同じ形で反映されます
```

- [ ] **Step 10: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/TimelineCurveEditor.cs docs-site/timeline/interpolation.md
git commit -m "$(cat <<'EOF'
feat(timeline): 回転カーブの Euler 表示でタンジェントを編集できるようにする

Euler 表示チャンネルのハンドル操作を回転 4 成分すべてへ同じ正規化値で
反映するようにした。値ドラッグは逆変換が無いため従来どおり不可。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 7: ライブ演出のスナップショットを用意する（項目 11 前半）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs:39,70`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:259`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs`（新規）

**Interfaces:**
- Consumes: `PresetDtoSnapshot<T>`（`Manager/History/PresetDtoSnapshot.cs`）、`PresetDtoUtils.AreEqual<T>`
- Produces:
  - `HistoryScope.LiveEffect`
  - `LiveEffectState` — ステージライト・レーザー・サイリウムの全パラメータを持つ XML 直列化可能な DTO
  - `LiveEffectSnapshot.Capture()` → `LiveEffectSnapshot`（`Func<IStateSnapshot>` として `HistoryManager.BeforeEdit` へ渡す）
  - `LiveEffectSnapshot.TargetKey` — ウィンドウ単位で固定の対象キー（3 マネージャをまとめて 1 スナップショットにするため常に同じ値）

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs` を新規作成する。DTO の直列化可否と同値判定だけを見る（`Capture` / `Apply` は Unity のマネージャに触るのでゲーム外では呼べない）。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LiveEffectStateTests
    {
        private static LiveEffectState BuildSample()
        {
            var state = new LiveEffectState();

            var lightController = new LiveEffectStageLightControllerState
            {
                autoColor = true,
                colorMin = Color.red,
                colorMax = Color.blue,
                lightInfo = new StageLightInfo { spotAngle = 42f },
            };
            lightController.lights.Add(new LiveEffectStageLightState
            {
                visible = true,
                position = new Vector3(1f, 2f, 3f),
                color = Color.green,
            });
            state.stageLightControllers.Add(lightController);

            var laserController = new LiveEffectStageLaserControllerState
            {
                position = new Vector3(0f, 1f, 0f),
                laserInfo = new StageLaserInfo { laserRange = 7f },
            };
            laserController.lasers.Add(new LiveEffectStageLaserState { intensity = 0.5f });
            state.stageLaserControllers.Add(laserController);

            var psylliumController = new LiveEffectPsylliumControllerState
            {
                visible = true,
                position = new Vector3(0f, 0f, 11f),
            };
            psylliumController.barConfig.baseScale = 2f;
            psylliumController.areas.Add(new PsylliumAreaConfig());
            psylliumController.patterns.Add(new LiveEffectPsylliumPatternState());
            state.psylliumControllers.Add(psylliumController);

            return state;
        }

        [Fact]
        public void 同じ内容のStateは等価と判定される()
        {
            Assert.True(PresetDtoUtils.AreEqual(BuildSample(), BuildSample()));
        }

        [Fact]
        public void 値が違うStateは非等価と判定される()
        {
            var a = BuildSample();
            var b = BuildSample();
            b.stageLightControllers[0].lights[0].position = new Vector3(9f, 9f, 9f);

            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }

        [Fact]
        public void サイリウムのバー設定の違いも検出できる()
        {
            var a = BuildSample();
            var b = BuildSample();
            b.psylliumControllers[0].barConfig.baseScale = 3f;

            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }
    }
}
```

`PresetDtoUtils` は `COM3D2.SceneEditor.Plugin` 名前空間なのでテストの名前空間から修飾なしで使える。`LiveEffectState` も同じ名前空間に置く。

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter LiveEffectStateTests`
Expected: コンパイルエラー `型または名前空間名 'LiveEffectState' が見つかりません`

- [ ] **Step 3: `HistoryScope.LiveEffect` を足す**

`Manager/History/HistoryScope.cs` の `Video,` の次へ追加する。

```csharp
        /// <summary>動画全本 (本数含む)</summary>
        Video,
        /// <summary>ライブ演出 (ステージライト・レーザー・サイリウムの全体状態)</summary>
        LiveEffect,
```

同ファイル `RequiresMaid` の `case HistoryScope.Video:` の次へ追加する。

```csharp
                case HistoryScope.Video:
                case HistoryScope.LiveEffect:
                    return false;
```

`SnapshotFactory` は触らない。`LiveEffect` は `BeforeEdit` の `capture` 引数版だけを使うため、`SnapshotFactory.Capture` の分岐には載せない（`Material` / `ShapeKey` / `Text` / `SubCamera` / `Sound` / `Video` と同じ扱い）。

- [ ] **Step 4: `LiveEffectSnapshot.cs` を作る**

`source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs` を新規作成する。

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ステージライト 1 灯のパラメータ (StageLight の可変フィールド全部)</summary>
    public class LiveEffectStageLightState
    {
        public bool visible = true;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Color color = Color.white;
        public StageLightInfo lightInfo = new StageLightInfo();
    }

    /// <summary>ステージライトのコントローラー (一括設定) と配下のライト</summary>
    public class LiveEffectStageLightControllerState
    {
        public bool autoVisible;
        public bool visible = true;
        public bool autoPosition;
        public Vector3 positionMin;
        public Vector3 positionMax;
        public bool autoRotation;
        public Vector3 rotationMin;
        public Vector3 rotationMax;
        public bool autoColor;
        public Color colorMin = Color.white;
        public Color colorMax = Color.white;
        public bool autoLightInfo;
        public StageLightInfo lightInfo = new StageLightInfo();
        public StageLightController.PatternType patternType = StageLightController.PatternType.None;
        public float patternCycleTime = 5f;
        public List<LiveEffectStageLightState> lights = new List<LiveEffectStageLightState>();
    }

    /// <summary>ステージレーザー 1 本のパラメータ (StageLaser の可変フィールド全部)</summary>
    public class LiveEffectStageLaserState
    {
        public bool visible = true;
        public Vector3 eulerAngles;
        public Color color1 = Color.white;
        public Color color2 = Color.white;
        public float intensity = 1f;
        public float laserRange = 13f;
        public float laserWidth = 0.05f;
        public float falloffExp = 0.2f;
        public float noiseStrength = 0.2f;
        public float noiseScale = 5f;
        public float coreRadius;
        public float offsetRange;
        public float glowWidth = 0.1f;
        public int segmentRange = 10;
        public bool zTest = true;
    }

    /// <summary>ステージレーザーのコントローラー (一括設定) と配下のレーザー</summary>
    public class LiveEffectStageLaserControllerState
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        public bool autoVisible = true;
        public bool visible = true;
        public bool autoRotation = true;
        public Vector3 rotationMin;
        public Vector3 rotationMax;
        public bool autoColor = true;
        public Color color1 = Color.white;
        public Color color2 = Color.white;
        public bool autoLaserInfo = true;
        public StageLaserInfo laserInfo = new StageLaserInfo();
        public List<LiveEffectStageLaserState> lasers = new List<LiveEffectStageLaserState>();
    }

    /// <summary>サイリウムのパターン 1 件 (パターン設定 + 移動回転設定)</summary>
    public class LiveEffectPsylliumPatternState
    {
        public PsylliumPatternConfig patternConfig = new PsylliumPatternConfig();
        public PsylliumTransformConfig transformConfig = new PsylliumTransformConfig();
    }

    /// <summary>サイリウムのコントローラーと配下のエリア・パターン</summary>
    public class LiveEffectPsylliumControllerState
    {
        public bool visible = true;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Vector3 scale = Vector3.one;
        public PsylliumBarConfig barConfig = new PsylliumBarConfig();
        public PsylliumHandConfig handConfig = new PsylliumHandConfig();
        public List<PsylliumAreaConfig> areas = new List<PsylliumAreaConfig>();
        public List<LiveEffectPsylliumPatternState> patterns = new List<LiveEffectPsylliumPatternState>();
    }

    /// <summary>
    /// ライブ演出 3 マネージャ (ステージライト / レーザー / サイリウム) の全状態。
    /// 個数も含めて持つので、追加・削除も 1 件の履歴として戻せる
    /// </summary>
    public class LiveEffectState
    {
        public List<LiveEffectStageLightControllerState> stageLightControllers
            = new List<LiveEffectStageLightControllerState>();
        public List<LiveEffectStageLaserControllerState> stageLaserControllers
            = new List<LiveEffectStageLaserControllerState>();
        public List<LiveEffectPsylliumControllerState> psylliumControllers
            = new List<LiveEffectPsylliumControllerState>();
    }

    /// <summary>
    /// ライブ演出全体のスナップショット。
    /// 3 マネージャをまとめて 1 件で持つため、対象キーはウィンドウ単位で固定 (TargetKey)。
    /// 適用は個数を合わせてから各要素へ値を書き戻す
    /// </summary>
    public class LiveEffectSnapshot : PresetDtoSnapshot<LiveEffectState>
    {
        /// <summary>BeforeEdit へ渡す対象キー。全操作で同じものを使う</summary>
        public static readonly object TargetKey = new object();

        private static StageLightManager stageLightManager => StageLightManager.instance;
        private static StageLaserManager stageLaserManager => StageLaserManager.instance;
        private static PsylliumManager psylliumManager => PsylliumManager.instance;

        public static LiveEffectSnapshot Capture()
        {
            var snapshot = new LiveEffectSnapshot();
            snapshot.Init();
            return snapshot;
        }

        protected override PresetDtoSnapshot<LiveEffectState> CreateEmpty() => new LiveEffectSnapshot();

        protected override LiveEffectState CaptureState()
        {
            var state = new LiveEffectState();

            foreach (var controller in stageLightManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectStageLightControllerState
                {
                    autoVisible = controller.autoVisible,
                    visible = controller.visible,
                    autoPosition = controller.autoPosition,
                    positionMin = controller.positionMin,
                    positionMax = controller.positionMax,
                    autoRotation = controller.autoRotation,
                    rotationMin = controller.rotationMin,
                    rotationMax = controller.rotationMax,
                    autoColor = controller.autoColor,
                    colorMin = controller.colorMin,
                    colorMax = controller.colorMax,
                    autoLightInfo = controller.autoLightInfo,
                    patternType = controller.patternType,
                    patternCycleTime = controller.patternCycleTime,
                };
                CopyLightInfo(controller.lightInfo, dto.lightInfo);

                foreach (var light in controller.lights)
                {
                    if (light == null) continue;

                    var lightDto = new LiveEffectStageLightState
                    {
                        visible = light.visible,
                        position = light.position,
                        eulerAngles = light.eulerAngles,
                        color = light.color,
                    };
                    lightDto.lightInfo.spotAngle = light.spotAngle;
                    lightDto.lightInfo.spotRange = light.spotRange;
                    lightDto.lightInfo.rangeMultiplier = light.rangeMultiplier;
                    lightDto.lightInfo.falloffExp = light.falloffExp;
                    lightDto.lightInfo.noiseStrength = light.noiseStrength;
                    lightDto.lightInfo.noiseScale = light.noiseScale;
                    lightDto.lightInfo.coreRadius = light.coreRadius;
                    lightDto.lightInfo.offsetRange = light.offsetRange;
                    lightDto.lightInfo.segmentAngle = light.segmentAngle;
                    lightDto.lightInfo.segmentRange = light.segmentRange;
                    lightDto.lightInfo.zTest = light.zTest;
                    dto.lights.Add(lightDto);
                }

                state.stageLightControllers.Add(dto);
            }

            foreach (var controller in stageLaserManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectStageLaserControllerState
                {
                    position = controller.position,
                    eulerAngles = controller.eulerAngles,
                    autoVisible = controller.autoVisible,
                    visible = controller.visible,
                    autoRotation = controller.autoRotation,
                    rotationMin = controller.rotationMin,
                    rotationMax = controller.rotationMax,
                    autoColor = controller.autoColor,
                    color1 = controller.color1,
                    color2 = controller.color2,
                    autoLaserInfo = controller.autoLaserInfo,
                };
                dto.laserInfo.CopyFrom(controller.laserInfo);

                foreach (var laser in controller.lasers)
                {
                    if (laser == null) continue;

                    dto.lasers.Add(new LiveEffectStageLaserState
                    {
                        visible = laser.visible,
                        eulerAngles = laser.eulerAngles,
                        color1 = laser.color1,
                        color2 = laser.color2,
                        intensity = laser.intensity,
                        laserRange = laser.laserRange,
                        laserWidth = laser.laserWidth,
                        falloffExp = laser.falloffExp,
                        noiseStrength = laser.noiseStrength,
                        noiseScale = laser.noiseScale,
                        coreRadius = laser.coreRadius,
                        offsetRange = laser.offsetRange,
                        glowWidth = laser.glowWidth,
                        segmentRange = laser.segmentRange,
                        zTest = laser.zTest,
                    });
                }

                state.stageLaserControllers.Add(dto);
            }

            foreach (var controller in psylliumManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectPsylliumControllerState
                {
                    visible = controller.visible,
                    position = controller.position,
                    eulerAngles = controller.eulerAngles,
                    scale = controller.scale,
                };
                dto.barConfig.CopyFrom(controller.barConfig);
                dto.handConfig.CopyFrom(controller.handConfig);

                foreach (var area in controller.areas)
                {
                    if (area == null) continue;
                    dto.areas.Add(area.areaConfig.Clone());
                }

                foreach (var pattern in controller.patterns)
                {
                    if (pattern == null) continue;

                    var patternDto = new LiveEffectPsylliumPatternState();
                    patternDto.patternConfig.CopyFrom(pattern.patternConfig);
                    patternDto.transformConfig.CopyFrom(pattern.transformConfig);
                    dto.patterns.Add(patternDto);
                }

                state.psylliumControllers.Add(dto);
            }

            return state;
        }

        protected override void ApplyState(LiveEffectState state)
        {
            if (state == null) return;

            ApplyStageLights(state);
            ApplyStageLasers(state);
            ApplyPsylliums(state);
        }

        private static void ApplyStageLights(LiveEffectState state)
        {
            // 個数を先に合わせる (SetupLights が不足分の追加と余剰の削除をまとめて行う)
            var counts = new List<int>();
            foreach (var dto in state.stageLightControllers)
            {
                counts.Add(dto.lights.Count);
            }
            stageLightManager.SetupLights(counts);

            var controllers = stageLightManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.stageLightControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.stageLightControllers[i];
                if (controller == null) continue;

                controller.autoVisible = dto.autoVisible;
                controller.visible = dto.visible;
                controller.autoPosition = dto.autoPosition;
                controller.positionMin = dto.positionMin;
                controller.positionMax = dto.positionMax;
                controller.autoRotation = dto.autoRotation;
                controller.rotationMin = dto.rotationMin;
                controller.rotationMax = dto.rotationMax;
                controller.autoColor = dto.autoColor;
                controller.colorMin = dto.colorMin;
                controller.colorMax = dto.colorMax;
                controller.autoLightInfo = dto.autoLightInfo;
                controller.patternType = dto.patternType;
                controller.patternCycleTime = dto.patternCycleTime;
                CopyLightInfo(dto.lightInfo, controller.lightInfo);

                for (var k = 0; k < controller.lights.Count && k < dto.lights.Count; k++)
                {
                    var light = controller.lights[k];
                    var lightDto = dto.lights[k];
                    if (light == null) continue;

                    light.visible = lightDto.visible;
                    light.position = lightDto.position;
                    light.eulerAngles = lightDto.eulerAngles;
                    light.color = lightDto.color;
                    light.spotAngle = lightDto.lightInfo.spotAngle;
                    light.spotRange = lightDto.lightInfo.spotRange;
                    light.rangeMultiplier = lightDto.lightInfo.rangeMultiplier;
                    light.falloffExp = lightDto.lightInfo.falloffExp;
                    light.noiseStrength = lightDto.lightInfo.noiseStrength;
                    light.noiseScale = lightDto.lightInfo.noiseScale;
                    light.coreRadius = lightDto.lightInfo.coreRadius;
                    light.offsetRange = lightDto.lightInfo.offsetRange;
                    light.segmentAngle = lightDto.lightInfo.segmentAngle;
                    light.segmentRange = lightDto.lightInfo.segmentRange;
                    light.zTest = lightDto.lightInfo.zTest;
                }
            }

            stageLightManager.UpdateLights();
        }

        private static void ApplyStageLasers(LiveEffectState state)
        {
            var counts = new List<int>();
            foreach (var dto in state.stageLaserControllers)
            {
                counts.Add(dto.lasers.Count);
            }
            stageLaserManager.SetupLasers(counts);

            var controllers = stageLaserManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.stageLaserControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.stageLaserControllers[i];
                if (controller == null) continue;

                controller.position = dto.position;
                controller.eulerAngles = dto.eulerAngles;
                controller.autoVisible = dto.autoVisible;
                controller.visible = dto.visible;
                controller.autoRotation = dto.autoRotation;
                controller.rotationMin = dto.rotationMin;
                controller.rotationMax = dto.rotationMax;
                controller.autoColor = dto.autoColor;
                controller.color1 = dto.color1;
                controller.color2 = dto.color2;
                controller.autoLaserInfo = dto.autoLaserInfo;
                controller.laserInfo.CopyFrom(dto.laserInfo);

                for (var k = 0; k < controller.lasers.Count && k < dto.lasers.Count; k++)
                {
                    var laser = controller.lasers[k];
                    var laserDto = dto.lasers[k];
                    if (laser == null) continue;

                    laser.visible = laserDto.visible;
                    laser.eulerAngles = laserDto.eulerAngles;
                    laser.color1 = laserDto.color1;
                    laser.color2 = laserDto.color2;
                    laser.intensity = laserDto.intensity;
                    laser.laserRange = laserDto.laserRange;
                    laser.laserWidth = laserDto.laserWidth;
                    laser.falloffExp = laserDto.falloffExp;
                    laser.noiseStrength = laserDto.noiseStrength;
                    laser.noiseScale = laserDto.noiseScale;
                    laser.coreRadius = laserDto.coreRadius;
                    laser.offsetRange = laserDto.offsetRange;
                    laser.glowWidth = laserDto.glowWidth;
                    laser.segmentRange = laserDto.segmentRange;
                    laser.zTest = laserDto.zTest;
                }
            }

            stageLaserManager.UpdateLasers();
        }

        private static void ApplyPsylliums(LiveEffectState state)
        {
            var datas = new List<TimelinePsylliumData>();
            foreach (var dto in state.psylliumControllers)
            {
                datas.Add(new TimelinePsylliumData
                {
                    areaCount = dto.areas.Count,
                    patternCount = dto.patterns.Count,
                });
            }
            psylliumManager.Setup(datas);

            var controllers = psylliumManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.psylliumControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.psylliumControllers[i];
                if (controller == null) continue;

                controller.visible = dto.visible;
                controller.position = dto.position;
                controller.eulerAngles = dto.eulerAngles;
                controller.scale = dto.scale;
                controller.barConfig.CopyFrom(dto.barConfig);
                controller.handConfig.CopyFrom(dto.handConfig);

                for (var k = 0; k < controller.areas.Count && k < dto.areas.Count; k++)
                {
                    var area = controller.areas[k];
                    if (area == null) continue;
                    area.areaConfig.CopyFrom(dto.areas[k], false);
                    area.refreshRequired = true;
                }

                for (var k = 0; k < controller.patterns.Count && k < dto.patterns.Count; k++)
                {
                    var pattern = controller.patterns[k];
                    if (pattern == null) continue;
                    pattern.patternConfig.CopyFrom(dto.patterns[k].patternConfig);
                    pattern.transformConfig.CopyFrom(dto.patterns[k].transformConfig);
                }

                controller.refreshRequired = true;
            }
        }

        /// <summary>StageLightInfo は CopyFrom を持たないため、ここで値を移す</summary>
        private static void CopyLightInfo(StageLightInfo src, StageLightInfo dst)
        {
            dst.spotAngle = src.spotAngle;
            dst.spotRange = src.spotRange;
            dst.rangeMultiplier = src.rangeMultiplier;
            dst.falloffExp = src.falloffExp;
            dst.noiseStrength = src.noiseStrength;
            dst.noiseScale = src.noiseScale;
            dst.coreRadius = src.coreRadius;
            dst.offsetRange = src.offsetRange;
            dst.segmentAngle = src.segmentAngle;
            dst.segmentRange = src.segmentRange;
            dst.zTest = src.zTest;
        }
    }
}
```

- [ ] **Step 5: csproj へファイルを登録する**

`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:259` の `SnapshotFactory.cs` の行の直前へ追加する。

```xml
    <Compile Include="Manager\History\LiveEffectSnapshot.cs" />
```

- [ ] **Step 6: ビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、`LiveEffectStateTests` 3 件 PASS

ここで `PresetDtoUtils.AreEqual`（`XmlSerializer`）が `StageLightInfo` / `StageLaserInfo` / `Psyllium*Config` を直列化できるかが実地で確かめられる。もし `PsylliumAreaConfig` の読み取り専用配列プロパティ（`barCountWeights` / `colorWeights` / `patternWeights`）で `InvalidOperationException` が出る場合は、`LiveEffectPsylliumControllerState.areas` の要素型を `PsylliumAreaConfig` から、同じフィールドを平置きした専用 DTO へ差し替える（フィールドは `PsylliumAreaConfig.CopyFrom` が写している 20 個 + `position` / `rotation` / `size` / `randomSeed`）。判定はこのテストで行い、推測で先回りしない。

- [ ] **Step 7: `StageLaserManager` / `PsylliumManager` の反映メソッド名を確認する**

Run: `grep -n 'public void UpdateLasers\|public void SetupLasers' source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StageLaserManager.cs`
Expected: 両方が存在する。名前が違う場合は Step 4 の `ApplyStageLasers` をそれに合わせて直し、ビルドし直す

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/ \
        source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj \
        source/COM3D2.SceneEditor.Plugin.Tests/LiveEffectStateTests.cs
git commit -m "$(cat <<'EOF'
feat(history): ライブ演出のスナップショットを追加する

HistoryScope.LiveEffect と、ステージライト・レーザー・サイリウムの
全パラメータを個数ごと持つ LiveEffectSnapshot を用意した。配線は次の変更で行う。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 8: ライブ演出の値変更を履歴へ載せ、自動登録を効かせる（項目 11 後半）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs:349-430,469-560,632-720,739,982-1020`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/StageLightItemInspector.cs:25`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/StageLaserItemInspector.cs:25`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/PsylliumItemInspector.cs:28`
- Modify: `docs-site/timeline/editing.md:72`
- Modify: `docs-site/timeline/compatibility.md:37`
- Modify: `docs-site/timeline/layers-effect.md:73`

**Interfaces:**
- Consumes: `LiveEffectSnapshot.Capture` / `LiveEffectSnapshot.TargetKey` / `HistoryScope.LiveEffect`（Task 7）
- Produces: `LiveEffectSnapshot.RecordEdit(string label)` — 値を書く直前に呼ぶ履歴記録。`GUIView.BeginAutoEditMode(Action)` のフックとしても、ボタン操作からの直接呼び出しとしても使う

- [ ] **Step 1: 記録用のヘルパーを足す**

`Manager/History/LiveEffectSnapshot.cs` の `Capture()` の直後へ追加する。

```csharp
        /// <summary>
        /// ライブ演出の変更を履歴へ記録する。値を書く「前」に呼ぶこと。
        /// 3 マネージャをまとめて 1 スナップショットにするので対象キーは固定で、
        /// ドラッグ中の連続変更はマウス解放時に 1 件へ集約される。
        /// BeforeEdit が内部で AutoEditMode.Enter を呼ぶため、
        /// 従来の onBeforeValueChanged (AutoEditMode.Enter) の置き換えとして使える
        /// </summary>
        public static void RecordEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.LiveEffect, "ライブ演出: " + label, TargetKey, Capture);
        }
```

- [ ] **Step 2: `BeginAutoEditMode` にフック差し替えのオーバーロードを足す**

`GUIViewAutoEditModeExtensions.cs` を次に置き換える。

```csharp
using System;
using COM3D2.MotionTimelineEditor;   // GUIView の名前空間 (GUIView.cs:6)

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「この区間の値変更は編集モードへ自動移行する」を宣言する GUIView 拡張。
    /// 従来 view.SetEnabled(focusedComboBox == null &amp;&amp; isPoseEditing) で塞いでいた区間を
    /// 常時操作可にし、代わりに値を書く直前に AutoEditMode.Enter を呼ばせる
    /// </summary>
    public static class GUIViewAutoEditModeExtensions
    {
        public static void BeginAutoEditMode(this GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);
            view.onBeforeValueChanged = AutoEditMode.Enter;
        }

        /// <summary>
        /// 値変更の直前フックを差し替えるオーバーロード。
        /// 履歴を記録してから編集モードへ入りたい区間で使う
        /// (HistoryManager.BeforeEdit は内部で AutoEditMode.Enter を呼ぶため、
        /// onBeforeEdit が記録を行うなら Enter を重ねて呼ぶ必要はない)
        /// </summary>
        public static void BeginAutoEditMode(this GUIView view, Action onBeforeEdit)
        {
            view.SetEnabled(view.focusedComboBox == null);
            view.onBeforeValueChanged = onBeforeEdit;
        }

        public static void EndAutoEditMode(this GUIView view)
        {
            view.onBeforeValueChanged = null;
            view.SetEnabled(view.focusedComboBox == null);
        }
    }
}
```

- [ ] **Step 3: ライブ演出ウィンドウの 9 箇所でフックを差し替える**

`LiveEffectWindow.cs` の `view.BeginAutoEditMode();`（`:370, 407, 490, 546, 647, 686, 709, 739, 1011`）を、それぞれの描画対象に合わせて次のように置き換える。

| 行 | メソッド | 置き換え後 |
|---|---|---|
| 370 | `DrawStageLightControllEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("ライト一括"));` |
| 407 | `DrawStageLightEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("ライト"));` |
| 490 | `DrawStageLaserControllEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("レーザー一括"));` |
| 546 | `DrawStageLaserEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("レーザー"));` |
| 647 | `DrawPsylliumControllEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム"));` |
| 686 | `DrawPsylliumBarConfigEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム バー設定"));` |
| 709 | `DrawPsylliumHandConfigEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム 持ち手設定"));` |
| 739 | `DrawPsylliumPatternConfigEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム パターン"));` |
| 1011 | `DrawPsylliumAreaEdit` | `view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム エリア"));` |

`DrawPsylliumTransformConfigEdit`（`:847`）は `DrawPsylliumPatternConfigEdit` の内側から呼ばれ、同じビューのフックを引き継ぐので単独の対応は要らない。

- [ ] **Step 4: ボタン操作（追加・削除・コピー）を明示的に記録する**

`GUIView.DrawButton` はフックを通さないので、コールバックの先頭で直接呼ぶ。

まず `LiveEffectWindow.cs` の `DrawTargetTabs` へ渡している 12 個のラムダを、それぞれ次の形にする。これらは現状 `AutoEditMode.Enter()` すら呼んでいないが、`RecordEdit` → `BeforeEdit` → `BeforeEditCore` が内部で `AutoEditMode.Enter()` を呼ぶため、履歴記録と編集モード突入がこの 1 行で同時に片付く。

```csharp
            var controller = DrawTargetTabs(
                view, "コントローラー", stageLightManager.controllers, ref _lightControllerIndex,
                () => { LiveEffectSnapshot.RecordEdit("ライト追加"); stageLightManager.AddController(true); },
                () => { LiveEffectSnapshot.RecordEdit("ライト削除"); stageLightManager.RemoveController(true); });
```

対象は次のとおり。ラベルはそのまま使う。

| メソッド | 追加 / 削除の呼び先 | ラベル |
|---|---|---|
| `DrawStageLightControllEdit` / `DrawStageLightEdit` | `stageLightManager.AddController` / `RemoveController` | `ライトコントローラー追加` / `ライトコントローラー削除` |
| 同上 | `stageLightManager.AddLight` / `RemoveLight` | `ライト追加` / `ライト削除` |
| `DrawStageLaserControllEdit` / `DrawStageLaserEdit` | `stageLaserManager.AddController` / `RemoveController` | `レーザーコントローラー追加` / `レーザーコントローラー削除` |
| 同上 | `stageLaserManager.AddLaser` / `RemoveLaser` | `レーザー追加` / `レーザー削除` |
| `DrawPsyllium*Edit` | `psylliumManager.AddController` / `RemoveController` | `サイリウムコントローラー追加` / `サイリウムコントローラー削除` |
| 同上 | `psylliumManager.AddArea` / `RemoveArea` | `サイリウムエリア追加` / `サイリウムエリア削除` |
| 同上 | `psylliumManager.AddPattern` / `RemovePattern` | `サイリウムパターン追加` / `サイリウムパターン削除` |

次に、コピーボタンの中で直接呼んでいる `AutoEditMode.Enter();` を `LiveEffectSnapshot.RecordEdit(...)` へ置き換える。**コピーボタンは 8 箇所ある**（`grep -n 'AutoEditMode.Enter();' LiveEffectWindow.cs` で全件出る）。1 箇所も飛ばさないこと。

たとえば `DrawStageLightEdit`（`:416-423`）は次になる。

```csharp
                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLight != null && copyToLight != light)
                    {
                        LiveEffectSnapshot.RecordEdit("ライトのコピー");
                        copyToLight.CopyFrom(light);
                    }
                }
```

8 箇所と、それぞれで使うラベルは次のとおり。`AutoEditMode.Enter();` の行をそのまま `LiveEffectSnapshot.RecordEdit("<ラベル>");` に差し替えるだけで、前後の条件やコピー処理は変えない。

| 行 | メソッド | ボタン | ラベル |
|---|---|---|---|
| 420 | `DrawStageLightEdit` | `コピー` | `ライトのコピー` |
| 503 | `DrawStageLaserControllEdit` | `コピー` | `レーザーコントローラーのコピー` |
| 559 | `DrawStageLaserEdit` | `コピー` | `レーザーのコピー` |
| 660 | `DrawPsylliumControllEdit` | `コピー` | `サイリウムコントローラーのコピー` |
| 835 | `DrawPsylliumPatternConfigEdit` | `コピー` | `サイリウムパターンのコピー` |
| 970 | `DrawPsylliumTransformConfigEdit` | `コピー` | `サイリウム移動回転のコピー` |
| 1026 | `DrawPsylliumAreaEdit` | `コピー` | `サイリウムエリアのコピー` |
| 1033 | `DrawPsylliumAreaEdit` | `全エリアにコピー` | `サイリウムエリアの全コピー` |

`:1026` は元のコードのインデントが 1 段ずれている（`if` ブロックより深い）。置き換えるついでに周囲へそろえる。

Run: `grep -n 'AutoEditMode.Enter();' source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs`
Expected: 出力なし（8 箇所すべてが `LiveEffectSnapshot.RecordEdit` に置き換わっている）

- [ ] **Step 5: 3 つの ItemInspector でもフックを差し替える**

`Timeline/ItemInspector/StageLightItemInspector.cs:25` を次に置き換える。

```csharp
            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("ライト"));
```

`StageLaserItemInspector.cs:25`：

```csharp
            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("レーザー"));
```

`PsylliumItemInspector.cs:28`：

```csharp
            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム"));
```

3 ファイルとも `using COM3D2.SceneEditor.Plugin;` が無ければ足す（`Timeline/ItemInspector/` は `COM3D2.MotionTimelineEditor.Plugin` 名前空間のため）。

- [ ] **Step 6: 2 構成をビルドしてテストを回す**

Run: Global Constraints のコマンド 1 本
Expected: 両構成のビルド成功、全テスト PASS。`.NET 3.5` 側でラムダの引数が 5 個を超えていないことにも注意する（今回は最大 1 個）

- [ ] **Step 7: 実機で確認する**

1. ライブ演出ウィンドウでステージライトの色を変えて `Ctrl+Z` → 元の色に戻る。`履歴` ウィンドウに `ライブ演出: ライト` が 1 件だけ積まれている（スライダーのドラッグでも 1 件）
2. 自動登録を ON、編集モードに入った状態でサイリウムの `Seed` を変える → `サイリウム` レイヤーの現在フレームにキーが増える
3. `ライト` タブで `追加` → `Ctrl+Z` → ライトが 1 灯減る
4. `ライト` タブの `個別` で `コピー先` を選び `コピー` → `Ctrl+Z` → コピー先が元の値へ戻る。サイリウムの `エリア` タブの `全エリアにコピー` でも同じく 1 回の `Ctrl+Z` で戻る
4. `mcp__com3d25-devbridge__tail_log` に例外が出ていないこと。特に `LiveEffectSnapshot` の `XmlSerializer` 由来の例外（Task 7 Step 6 の懸念）が出ていないこと
5. Inspector でステージライトの項目を編集したときも履歴に載ること

- [ ] **Step 8: 仕様書を直す**

`docs-site/timeline/editing.md:72` の行

```markdown
操作履歴に対応していない操作（ライブ演出ウィンドウのステージライト・レーザー・サイリウム）も自動登録されません。`登録` か `Enter` を使ってください。
```

を削除する。

`docs-site/timeline/compatibility.md:37` の行

```markdown
- ステージライト・レーザー・サイリウムは操作履歴に対応していないため自動登録されません
```

を削除する。

`docs-site/timeline/layers-effect.md:73` の行

```markdown
ステージライト・レーザー・サイリウムは操作履歴に対応していないため、自動登録の対象外です。`登録` か `Enter` で登録してください。
```

を次に置き換える。

```markdown
ステージライト・レーザー・サイリウムの値変更は操作履歴に記録され、自動登録の対象になります。ライトやレーザーの増減も 1 件の履歴として戻せます。
```

Run: `grep -rn '操作履歴に対応していない' docs-site/`
Expected: 出力なし

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/GUIViewAutoEditModeExtensions.cs \
        source/COM3D2.SceneEditor.Plugin/Manager/History/LiveEffectSnapshot.cs \
        source/COM3D2.SceneEditor.Plugin/LiveEffectWindow.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ \
        docs-site/timeline/editing.md docs-site/timeline/compatibility.md docs-site/timeline/layers-effect.md
git commit -m "$(cat <<'EOF'
feat(history): ライブ演出を操作履歴と自動登録の対象にする

GUIView.onBeforeValueChanged のフックを差し替えて、ライブ演出の値変更を
LiveEffectSnapshot で記録するようにした。追加・削除・コピーは明示的に記録する。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

### Task 9: メイド目線の所有を仕様書に明記する（項目 16）

**Files:**
- Modify: `docs-site/timeline/files.md:16`

**Interfaces:**
- Consumes: なし
- Produces: なし（文書のみ）

- [ ] **Step 1: 所有関係をコードで裏取りする**

Run: `grep -n 'eyeMoveType' source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`
Expected: 保存側（タイムライン読込中だけ `TimelineData.eyeMoveType` を `look.eyeMoveType` へ写す）と適用側（`TimelineData` へ書き戻す）の 2 箇所が出る。行番号が設計書の `:1223-1229, 1791-1811` からずれていても内容が一致していればよい。一致しない場合はここで止め、実装者へ差異を報告する

- [ ] **Step 2: 仕様書を直す**

`docs-site/timeline/files.md:16` の行

```markdown
タイムラインはシーンプリセットには含まれません。メイド目線と注視先の設定だけがシーンプリセットに保存されます。
```

を次に置き換える。

```markdown
タイムラインはシーンプリセットには含まれません。
タイムラインを読み込んだ状態でプリセットを保存すると、メイド目線と注視先はタイムラインが持つ値の「写し」として保存され、プリセットを適用したときにタイムラインへ戻されます。値の本体はタイムライン側にあります。
```

- [ ] **Step 3: コミット**

```bash
git add docs-site/timeline/files.md
git commit -m "$(cat <<'EOF'
docs(timeline): メイド目線がタイムライン側の所有であることを明記する

シーンプリセットの look.eyeMoveType はタイムライン読込中に限って保存される
写しで、適用時に TimelineData へ書き戻される、という実装の形に合わせた。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01P7T1o3WNAHt8JensLxU9Qm
EOF
)"
```

---

## 完了条件

- [ ] Task 1〜9 のステップがすべて完了している
- [ ] 2 構成（COM3D2 / COM3D25）のビルドが通り、`dotnet test` が全 PASS
- [ ] 新規テスト 3 ファイル（`TimelineHistoryBaselineTests` / `TangentValueCoverageTests` / `LiveEffectStateTests`）が追加されている
- [ ] 実機確認（Task 1 / 2 / 3 / 4 / 5 / 6 / 8）が済んでいる
- [ ] `TimelineData.CurrentVersion` が 34 のままで、`TimelineXml` / `TransformXml` の要素が増減していない
- [ ] `grep -rn '操作履歴に対応していない\|0フレーム目にキーフレームが必要です\|最初の操作は' docs-site/` が空
- [ ] 実装後、**code-review** スキルでレビューを通してからユーザーへ提示する

## レビュー却下メモ

- `InSmoothBit` の既定値 0 が「両端水平タンジェント」になる根拠が未裏取り（確信度: 低） — 計画作成時に確認済み。`Timeline/TransformXml.cs` の `[XmlElement("InSmoothBit")] public long inSmoothBit = 0;` と `TransformDataBase.cs:882-907` の `_inSmoothBit` / `_outSmoothBit` の setter（ビットが 0 なら全成分 `isSmooth = false`）、`TangentData` の既定 `normalizedValue = 0` の 3 点で成立する。念のため Task 5 Step 10 の実機確認で最終判断する
