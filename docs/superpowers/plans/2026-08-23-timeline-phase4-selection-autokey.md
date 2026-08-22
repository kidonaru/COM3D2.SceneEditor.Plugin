# タイムライン Phase 4 前半（選択連携・自動キーフレーム）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ロードマップ Phase 4 のうち、(a) Hierarchy の選択とタイムラインのアクティブレイヤーを同期する SelectionManager 連携、(b) ポーズ編集（ドラッグ操作）完了時に現在フレームへ自動でキーフレーム登録するオプトイン機能、の 2 つを実装する。Undo/Redo 統合（HistoryManager ブリッジ）は次の計画に分離する。

**Architecture:** 既存イベントに乗る薄い配線のみで実現する。(a) は `SelectionManager.onSelectionChanged` を `TimelineWindow` が購読し、選択 GameObject をメイド/ライトに解決して `ChangeActiveLayer` を呼ぶ。(b) は全ドラッグ種別（IK/ボーン回転/指/顔）が経由する `MaidDragBoneTracker.EndDrag()` に static イベントを 1 つ追加し、TimelineWindow が購読して `AddKeyFrameDiff()` を呼ぶ。自動登録はトグル（既定 OFF）で Config に永続化する。

**Tech Stack:** C# (.NET 3.5), IMGUI, 移植済み Timeline コア

**Spec:** `docs/superpowers/specs/timeline-window-roadmap.md`（Phase 4「ポーズ編集 → キーフレーム化」「SelectionManager との連携」）

## Global Constraints

- SE ネイティブ側ファイル（MaidManipulation/ 等）への変更は最小限（イベント 1 つの追加のみ）
- Timeline/ 配下は namespace `COM3D2.MotionTimelineEditor.Plugin`、SE 側は `COM3D2.SceneEditor.Plugin`。TimelineWindow.cs は境界ファイルで `MTEP.` エイリアス使用中
- 新規ファイルなし（既存ファイルの変更のみ）→ csproj 変更不要
- ビルド検証: MSBuild 直接（COM3D25 で都度、最終 Task で COM3D2 も）。コマンドは既存計画と同一

---

### Task 1: SelectionManager 連携（選択→レイヤー・メイド同期）

**Files:**
- Modify: `TimelineWindow.cs`（ctor 付近 L102-105 と handler 追加）

**Interfaces:**
- Consumes: `SelectionManager.instance.onSelectionChanged : Action<GameObject>`（Manager/SelectionManager.cs:58。全オブジェクト種別で発火するためフィルタ必須）、`MTEP.MaidManager.instance.maidCaches`、`timelineManager.ChangeActiveLayer(Type, int slotNo)`（_layerComboBox.onSelected L488 と同じ呼び出し）
- Produces: なし（内部配線のみ）

- [ ] **Step 1: TimelineWindow に選択同期ハンドラを追加**

ctor（`MTEP.TimelineManager.onRefresh += ...` の並び）で購読し、`OnDestroy` 相当（既存の解除箇所があればそこ、なければ購読のみで可 — TimelineWindow はシングルトン常駐）に合わせる:

```csharp
            SelectionManager.instance.onSelectionChanged += OnSelectionChanged;
```

ハンドラ実装（再入ガード付き）:

```csharp
        private bool _syncingSelection = false;

        // Hierarchy 等での選択をタイムラインのアクティブメイド/レイヤーへ同期する
        private void OnSelectionChanged(GameObject go)
        {
            if (_syncingSelection || go == null)
            {
                return;
            }

            var timelineManager = MTEP.TimelineManager.instance;
            if (timelineManager.timeline == null || timelineManager.currentLayer == null)
            {
                return;
            }

            try
            {
                _syncingSelection = true;

                // メイド（配下ボーン含む）なら該当スロットのレイヤーへ切替
                var maid = go.GetComponentInParent<Maid>();
                if (maid != null)
                {
                    var maidCaches = MTEP.MaidManager.instance.maidCaches;
                    for (var i = 0; i < maidCaches.Count; i++)
                    {
                        if (maidCaches[i].maid == maid)
                        {
                            var currentLayer = timelineManager.currentLayer;
                            if (currentLayer.hasSlotNo && currentLayer.slotNo != i)
                            {
                                timelineManager.ChangeActiveLayer(currentLayer.layerType, i);
                            }
                            return;
                        }
                    }
                    return;
                }

                // 追加ライトならライトレイヤーへ切替
                var light = go.GetComponentInChildren<Light>();
                if (light != null && StudioLightManager.instance.lights.Contains(light))
                {
                    if (timelineManager.currentLayer.layerType != typeof(MTEP.LightTimelineLayer))
                    {
                        timelineManager.ChangeActiveLayer(typeof(MTEP.LightTimelineLayer), 0);
                    }
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }
```

注意: `ChangeActiveLayer` は `SetCurrentLayer` 経由で isPoseEditing のトグルを伴いカスケードし得るため、ガードとレイヤー未初期化時の早期 return を必ず入れる。`Maid` 解決は `GetComponentInParent<Maid>()` が効かない構造（メイドルートに Maid が付かない）の場合、`maidCaches[i].maid.gameObject` と `transform.IsChildOf` の突き合わせに切り替える（実装時に確認）。

- [ ] **Step 2: ビルド確認（COM3D25）**
- [ ] **Step 3: コミット** `feat(timeline): Hierarchy の選択をタイムラインのレイヤー/メイドへ同期`

### Task 2: ドラッグ完了イベントの追加（SE ネイティブ側の最小変更）

**Files:**
- Modify: `MaidManipulation/IMaidDragPoint.cs`（`MaidDragBoneTracker.EndDrag()` L53 付近）

**Interfaces:**
- Produces: `MaidDragBoneTracker.onDragEnd : static event Action` — IK / ボーン回転 / 指 / 顔の全ドラッグ種別の終了で発火（各 DragPoint の EndDrag/CancelDrag が Tracker.EndDrag を呼ぶ既存構造に乗る）

- [ ] **Step 1: static イベントを追加（キャンセル・破棄経路では発火しない）**

`MaidDragBoneTracker.EndDrag()` は各 DragPoint の正常終了だけでなく `CancelDrag()` / `OnDestroy()` からも呼ばれるため、シグネチャを `EndDrag(bool committed = true)` に拡張し、キャンセル・破棄側の呼び出しを `EndDrag(false)` に変更する:

```csharp
        // ドラッグ編集の完了通知。タイムラインの自動キーフレーム登録などが購読する。
        // キャンセルやコンポーネント破棄による終了 (committed=false) では発火しない
        public static event Action onDragEnd;

        public void EndDrag(bool committed = true)
        {
            ...既存処理...
            if (committed)
            {
                onDragEnd?.Invoke();
            }
        }
```

4 つの DragPoint（IK / ボーン回転 / 指 / 顔）の `CancelDrag` / `OnDestroy` 経由の Tracker.EndDrag 呼び出しを grep で全数確認し、`EndDrag(false)` へ変更する（正常終了経路はデフォルト引数のまま）。`using System;` が無ければ追加。

- [ ] **Step 2: ビルド確認（COM3D25）**
- [ ] **Step 3: コミット** `feat(manipulate): ドラッグ編集完了イベント onDragEnd を追加`

### Task 3: 自動キーフレーム登録（オプトイン）

**Files:**
- Modify: `TimelineWindow.cs`（購読 + トグル UI）
- Modify: `Timeline/Config.cs`（`isAutoKeyFrame` フラグ追加・永続化）

**Interfaces:**
- Consumes: `MaidDragBoneTracker.onDragEnd`（Task 2）、`currentLayer.AddKeyFrameDiff()`（TimelineLayerBase）、`timelineManager.initialEditFrame`、`studioHackManager.isPoseEditing`
- Produces: `Config.isAutoKeyFrame : bool`（既定 false）

- [ ] **Step 1: Config にフラグを追加**

`Timeline/Config.cs` の他の bool フラグ群の並びに追加:

```csharp
        // ドラッグ編集完了時に現在フレームへ自動でキーフレーム登録する (SE 独自機能)
        public bool isAutoKeyFrame = false;
```

- [ ] **Step 2: TimelineWindow で購読して登録を実行**

ctor で `MaidDragBoneTracker.onDragEnd += OnDragEnd;` を購読し、ハンドラ:

```csharp
        // ドラッグ編集完了時の自動キーフレーム登録 (SE 独自機能、既定 OFF)
        private void OnDragEnd()
        {
            var config = MTEP.ConfigManager.instance.config;
            if (!config.isAutoKeyFrame)
            {
                return;
            }

            var timelineManager = MTEP.TimelineManager.instance;
            var currentLayer = timelineManager.currentLayer;
            if (currentLayer == null || timelineManager.initialEditFrame == null)
            {
                return;
            }

            currentLayer.AddKeyFrameDiff();
        }
```

注意: `AddKeyFrameDiff` 内の `RequestHistory` はマウスアップ後のフレームで flush されるため（`!Input.GetMouseButton(0)` 判定）、EndDrag と同フレームで競合しないか実装後に確認する。競合する場合は `MTEUtils.EnqueueAction` で 1 フレーム遅延させる。

- [ ] **Step 3: 編集モードトグルの隣に「自動登録」トグルを追加**

`TimelineWindow.cs` の `編集モード` トグル（L838-841 付近）の隣に:

```csharp
            view.DrawToggle("自動登録", config.isAutoKeyFrame, 80, 20, newValue =>
            {
                config.isAutoKeyFrame = newValue;
                config.dirty = true;
            });
```

（`config.dirty` の永続化パターンは既存トグルの実装に合わせる。実装時に既存トグルの保存方法を確認して同じ流儀にする）

- [ ] **Step 4: ビルド確認（COM3D25 / COM3D2 両方）**
- [ ] **Step 5: コミット** `feat(timeline): ドラッグ編集完了時の自動キーフレーム登録を追加 (既定 OFF)`

### Task 4: ロードマップ進捗の記録

- [ ] **Step 1:** Phase 4 の「ポーズ編集 → キーフレーム化」に ` — **実装完了（2026-08-23）**。ドラッグ編集（IK/ボーン回転/指/顔）完了時の自動登録をオプトインで追加（既定 OFF、スライダー編集は対象外）`、「SelectionManager との連携」に ` — **実装完了（2026-08-23）**。選択メイド/追加ライトへのレイヤー・スロット切替に対応` を追記
- [ ] **Step 2: コミット** `docs(timeline): Phase 4 前半 (選択連携・自動キーフレーム) の完了を記録`

## 主要リスク

| リスク | 対応 |
|---|---|
| ChangeActiveLayer のカスケード（isPoseEditing トグル）による再入・チラつき | `_syncingSelection` ガード + レイヤー同一時の早期 return |
| AddKeyFrameDiff の ApplyCurrentFrame(true) がドラッグ直後に見た目のスナップを起こす | 自動登録は既定 OFF のオプトイン。問題があれば EnqueueAction で遅延 |
| RequestHistory のマウスアップ debounce との競合 | 実装後にログで flush を確認。競合時は 1 フレーム遅延 |
| スライダー編集は自動登録対象外（イベントが無い） | v1 はドラッグのみと明記（ロードマップに記録）。必要なら HistoryManager.onChanged 購読を後続検討 |

## レビュー却下メモ

- ライト選択時の `ChangeActiveLayer(LightTimelineLayer, 0)` の slotNo=0 固定 — 却下。LightTimelineLayer は MTE 原本から slotNo を持たない単一レイヤー（`Create(int slotNo)` は常に `base(0)`、全ライトが 1 レイヤー内のボーンとして扱われる）ため 0 が正しい。選択ライトのトラック（ボーン行）へのジャンプは将来の改善候補
- RequestHistory の同フレーム競合 — レビュー指摘どおり「実害の可能性は低い」に補正（EndDrag 時点でマウスは離れているため通常どおり flush される）

## スコープ外（明示）

- Undo/Redo 統合（TimelineHistoryEntry ブリッジ）: 次の計画（Phase 4 後半）で実施
- BoneEditManager（スロットボーン編集）の自動キーフレーム化
- スライダー編集の自動登録
