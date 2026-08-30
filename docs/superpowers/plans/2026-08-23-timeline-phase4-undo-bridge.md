# タイムライン Phase 4 後半（Undo/Redo 統合）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン操作の履歴を SE の `HistoryManager`（Ctrl+Z / HistoryWindow）へ統合し、キーフレーム操作もシーン編集操作と同じ単一の Undo/Redo スタックで戻せるようにする（ロードマップ Phase 4「Undo/Redo」）。

**Architecture:** マージではなくブリッジ。タイムラインの状態は SE の `IStateSnapshot` では表現できないため、既存の `TimelineHistoryManager.AddHistory`（全 21 箇所の `RequestHistory` が集約される単一入口）で生成される `TimelineXml` スナップショットの前後ペアを `IHistoryEntry` 実装（`TimelineHistoryEntry`）として SE `HistoryManager.AddEntry` に積む。Undo/Redo の適用は `TimelineManager.UpdateTimeline(xml)`（MTE の RestoreHistory と同じ復元経路）で行う。MTE 側スタック（TimelineHistoryManager）はスナップショットの生成元として残すが、その Undo/Redo キーバインドは SE では元々未配線であり二重 Undo は発生しない。

**Tech Stack:** C# (.NET 3.5), XmlSerializer（TimelineXml）, SE HistoryManager

**Spec:** `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 4「Undo/Redo: キーフレーム操作を HistoryManager のスナップショット方式に統合」）

## Global Constraints

- Timeline/ 配下は namespace `COM3D2.MotionTimelineEditor.Plugin`。新規のブリッジ用エントリは SE 側の型として `COM3D2.SceneEditor.Plugin` 名前空間で `Timeline/` 配下に置く（TimelineIntegration.cs / TimelineWindow.cs と同じ境界ファイル扱い）
- 移植済みファイル（TimelineHistoryManager.cs）への変更は最小限とし、日本語理由コメントを付す
- csproj に `<Compile Include>` をアルファベット順で追加
- ビルド検証: MSBuild 直接（COM3D25 で都度、最終 Task で COM3D2 も）。コマンドは既存計画と同一

---

### Task 1: TimelineHistoryEntry の新設

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineHistoryEntry.cs`
- Modify: csproj（`Timeline\TimelineData.cs` の後、`Timeline\TimelineIntegration.cs` の前）

**Interfaces:**
- Consumes: `IHistoryEntry`（Manager/HistoryManager.cs: description/canApply/ApplyBefore/ApplyAfter）、`MTEP.TimelineXml`、`MTEP.TimelineManager.instance.UpdateTimeline(TimelineXml)`（TimelineManager.cs:432。MTE RestoreHistory と同一の復元経路）
- Produces: `TimelineHistoryEntry(TimelineXml before, TimelineXml after, string description)`。Task 2 のブリッジが生成する

- [ ] **Step 1: ファイル作成**

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作 1 件の履歴エントリ。
    /// タイムラインの状態は IStateSnapshot で表現できないため、
    /// TimelineXml の前後スナップショットを対で持ち SE の履歴スタックへ参加する
    /// </summary>
    public class TimelineHistoryEntry : IHistoryEntry
    {
        public string description { get; set; }

        private readonly MTEP.TimelineXml _before;
        private readonly MTEP.TimelineXml _after;

        public TimelineHistoryEntry(MTEP.TimelineXml before, MTEP.TimelineXml after, string description)
        {
            _before = before;
            _after = after;
            this.description = description;
        }

        // タイムラインが閉じられている間は適用できない (エントリはスキップされる)
        public bool canApply => MTEP.TimelineManager.instance.timeline != null;

        public void ApplyBefore()
        {
            Restore(_before);
        }

        public void ApplyAfter()
        {
            Restore(_after);
        }

        private static void Restore(MTEP.TimelineXml xml)
        {
            MTEP.TimelineManager.instance.UpdateTimeline(xml);
            // 次の編集の before がこの復元後状態を指すよう、確定済みスナップショットを更新する
            MTEP.TimelineHistoryManager.instance.lastCommittedXml = xml;
        }
    }
}
```

- [ ] **Step 2: csproj 追加、ビルド確認（COM3D25）**
- [ ] **Step 3: コミット** `feat(timeline): TimelineXml 前後対を保持する TimelineHistoryEntry を新設`

### Task 2: AddHistory から SE HistoryManager へのブリッジ

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineHistoryManager.cs`（`AddHistory` 末尾）

**Interfaces:**
- Consumes: `SceneEditor.Plugin.HistoryManager.instance.AddEntry(IHistoryEntry)`（履歴適用中 `_isApplying` は内部で拒否されるため再入安全）、`TimelineHistoryEntry`（Task 1）
- Produces: なし（配線のみ）

- [ ] **Step 1: 現在状態スナップショットの独立追跡と before 捕捉**

**設計（plan-review 🔴 反映）**: `beforeXml` を MTE スタックの `historyList[historyIndex]` から取ると、SE 側 Undo/Redo（`TimelineHistoryEntry.Apply*` → `UpdateTimeline`）が MTE 側 `historyIndex` を動かさないため、次の編集で before が実状態とズレて履歴が壊れる。そこで「現在のタイムライン状態に対応する確定済みスナップショット」を独立プロパティで追跡し、AddHistory と SE 側適用の双方で更新する。

`TimelineHistoryManager` にプロパティを追加:

```csharp
        // 現在のタイムライン状態に対応する確定済みスナップショット。
        // SE 履歴ブリッジの before として使う。AddHistory と
        // TimelineHistoryEntry (SE 側 Undo/Redo 適用) の双方で更新する
        public TimelineXml lastCommittedXml { get; set; }
```

`AddHistory` の冒頭で捕捉:

```csharp
            var beforeXml = lastCommittedXml;
```

`ClearHistory` に `lastCommittedXml = null;` を追加（タイムライン破棄時に古い状態を持ち越さない）。

末尾（`historyIndex = historyList.Count - 1;` の後）に追加:

```csharp
            // SE の履歴スタックへも同じ操作を積み、Ctrl+Z を一本化する。
            // 直前スナップショットが無い場合 (新規作成・読み込み直後) は
            // それ以前へ戻る意味がないため積まない
            if (beforeXml != null)
            {
                SceneEditor.Plugin.HistoryManager.instance.AddEntry(
                    new SceneEditor.Plugin.TimelineHistoryEntry(beforeXml, history.xml, description));
            }
            lastCommittedXml = history.xml;
```

注意: `historyLimit <= 0` の早期 return（AddHistory 中間）がある場合はブリッジも実行されない（同じ設定で両者無効になる、意図どおり）。

- [ ] **Step 2: ビルド確認（COM3D25）**
- [ ] **Step 3: コミット** `feat(timeline): タイムライン履歴を SE HistoryManager へブリッジし Ctrl+Z を一本化`

### Task 3: 動作整合の確認と記録

- [ ] **Step 1: 復元経路の再入確認（静的）**

`UpdateTimeline` → `mte.OnLoad` → `StudioLightManager.OnLoad/SetupLights` の経路で `RequestHistory` / `AddHistory` が呼ばれないことを grep で確認する（SetupLights は内部メソッド経由で履歴を積まない実装であること）。もし積む経路が見つかった場合は、`HistoryManager._isApplying` により AddEntry は拒否されるが警告ログが出るため、該当呼び出しに適用中ガードを追加する。

- [ ] **Step 2: 両 GameVersion ビルド確認**
- [ ] **Step 3: ロードマップ更新**

Phase 4 の「Undo/Redo」行に ` — **実装完了（2026-08-23）**。TimelineXml 前後対を IHistoryEntry として SE HistoryManager へブリッジ（AddHistory 単一入口で全タイムライン操作を網羅）。MTE 側の Undo キーバインドは SE では未配線のため二重 Undo なし` を追記。

- [ ] **Step 4: コミット** `docs(timeline): Undo/Redo 統合の完了を記録`

## 主要リスク

| リスク | 対応 |
|---|---|
| Undo 適用（UpdateTimeline）が重い（全タイムライン再構築 + CreateAndApplyAnmAll） | MTE の RestoreHistory と同一経路であり、MTE で実績のある挙動。頻度は Undo 操作時のみで許容 |
| 適用中の再入で履歴が壊れる | HistoryManager が `_isApplying` で AddEntry を拒否。Task 3 Step 1 で警告ログが出る経路がないか静的確認 |
| SE 履歴の historyLimit による混在トリム | エントリは自己完結（before/after を保持）のためトリムされても他エントリに影響しない |
| TimelineXml の保持によるメモリ増 | MTE 側 TimelineHistoryManager が既に同量を保持しており、参照共有（同一インスタンス）のため実質増分は小さい。ToXml は毎回新規生成・FromXml は読み取り専用のため後方破壊なし（plan-review で確認済み） |
| タイムラインを閉じている間の Undo で canApply=false エントリが恒久スキップされる | SE HistoryManager の既存仕様（対象消滅時の他スナップショットと同じ挙動）として許容。頻度も低い |
| 新規作成・読み込み直後の最初の操作は SE Undo 対象外 | before が存在しないため意図どおり（読み込み前へは戻れない）。ロードマップに明記 |

## スコープ外（明示）

- MTE 側 TimelineHistoryManager の削除・縮小（スナップショット生成元として存続）
- HistoryWindow へのタイムライン専用 UI 追加
