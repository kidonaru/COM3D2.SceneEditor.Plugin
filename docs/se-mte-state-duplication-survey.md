# SE 独自機能と MTE タイムライン機能の二重化調査

調査日: 2026-08-29
目的: 同じゲーム状態を **SceneEditor 側の実装（SE）と MotionTimelineEditor 移植側（MTE）が別々に持っている** 箇所を洗い出し、統合対象を決める。

`docs/layer-window-duplication-survey.md` は「レイヤー編集ウィンドウ vs 個別ウィンドウ」という **UI 配置** の軸で、そちらは委譲済み。本書は **状態の所有者** の軸で、UI を委譲しても残る二重化を対象とする。

## 判定の基準

MTE 移植で採用されている正しいパターンは **アダプタ化** — MTE 側は状態を持たず、SE のマネージャ／実体を読み書きする。

| 例 | 形 |
|---|---|
| `Timeline/Manager/StudioLightManager`（:10-14） | SE の `Manager/StudioLightManager` と `GameMain.MainLight` へ接続するアダプタ |
| `Timeline/Hack/SceneEditorHack`（:70-131） | `isPoseEditing` / `isAnmEnabled` / `isUIVisible` などを SE のマネージャへ委譲 |
| `Timeline/Manager/PngObjectTimelineManager`（:40-46 `TimelinePngObjectEntry`） | SE の `PngObjectData` との対応表だけを持つ |
| `Timeline/TimelineSelectionBridge`（:7-14） | SE `SelectionManager` と双方向同期 |

**二重化 = このアダプタ化がされておらず、MTE が独自の状態を持っている／SE の状態を勝手に上書きしている箇所。**

## A. 同じゲーム状態を両者が奪い合う（統合必須）

| # | 対象 | SE 側 | MTE 側 | 症状 |
|---|---|---|---|---|
| A-1 | **視線・注視先**（A-1a〜c で解消済み。現行仕様は「[視線の統合後仕様](#視線の統合後仕様a-1-まとめ)」参照） | `MaidLookController`（向け先モード / 顔向き XY / 注視オブジェクト）＋ `MaidFaceWindow` の `boHeadToCam` / `boEyeToCam` トグル（:276-288） | `MaidCache.lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `eyeEulerAngle`、`UpdateHeadLook`（MaidCache.cs:501）が毎回 `maid.EyeToCamera(timeline.eyeMoveType)` と `LockHeadAndEye` を実行 | 同じ `body0.trsLookTarget` を奪い合う。表情ウィンドウ視線タブに「視線」と「タイムライン視線」の 2 セクションが並ぶ（MaidFaceWindow.cs:330-335 に既知として明記）。さらに `TimelineSettingWindow` の「メイド目線」（`eyeMoveType`, :253）と「顔/瞳の固定化」（`useHeadKey`, :258）が 3 箇所目の入口になっていた |
| A-2 | **胸の揺れ物理** | `MaidMuneYureController`（`SetMuneYure*WithEnable` / `MuneYureL/R`）＋ InspectorWindow の胸揺れトグル（:304） | `MaidCache.UpdateMuneYure`（:490）が `timeline.useMuneKeyL/R` から `MuneYureL/R` と `jbMuneL/R.enabled` を直接上書き。入口は `TimelineSettingWindow`「胸(左/右)の物理無効」（:265-270） | `TimelineData.useMuneKeyL/R` の setter（TimelineData.cs:292/309）が発火するたび SE のトグル状態を無視して書き換える。`SceneEditorHack.useMuneKeyL/R` は空実装（:121-129）なので逆方向の同期も無い |
| A-3 | **スロット揺れボーンの ON/OFF** | `SlotYureUtil.SetYureState` ＋ InspectorWindow の揺れトグル（:507-538）、`BoneEditManager` のボーン掴み時の自動 OFF（:540-542） | `MotionTimelineLayer`（:612-622）が `config.isAutoYureBone` 有効時、anm 構築のたび全揺れスロットの状態を `_extendSlotNames` 基準で総入れ替え | **調査時の記述は誤り（A-3 で判明）**。この経路が呼ぶ `MaidCache.GetYureState` は常に false、`SetYureState` は空実装（PartsEdit 連携が未移植）で、上書きは実際には起きていなかった。設定 `TimelineSettingWindow`「自動揺れボーン」も効果を持っていなかった |
| A-4 | **ボーン／IK の表示** | `MaidManipulateManager.isBoneVisible`（BoneEditWindow「ボーン表示」:363、MenuBarWindow:223） | `StudioHackManager.isPoseEditing` の setter（:31）が `isIKVisible = value && canIKVisible` を書き込み、`canIKVisible` は選択中レイヤー種別と `config.alwaysShowIK`（:37）で決まる | 編集モードを切り替えるたび、選択中のタイムラインレイヤーによって SE のボーン表示トグルが勝手に落ちる。`alwaysShowIK` は `TimelineSettingWindow`「常にIKを表示」（:881）にしか無い |
| A-5 | **表情モーフの読み書き** | `MaidFaceMorphController`（TMorph 直接操作。CRC 顔のサフィックス解決を独自実装） | `TimelineFaceManager`（:30-70。`CheckMorph` / `GetRatio` で別実装の名前解決・倍率換算）。`MorphTimelineLayer` が `SetMabatakiOff` + `SetMorphValue` を適用 | 同じ TMorph に対する名前解決・値スケールの実装が 2 本。片方だけ直すと表情ウィンドウとタイムライン再生で値がずれる |

## B. 設定・データが二重管理（整理対象）

| # | 対象 | 実態 |
|---|---|---|
| B-1 | **Config が 2 ファイル** | `Config.cs`（`SceneEditor.xml`）と `Timeline/Config.cs`（`Timeline.xml`）。**フィールド名が重複しているのは 13 個**: `pluginEnabled` / `historyLimit` / `keyRepeatTime` / `keyRepeatTimeFirst` / `useHSVColor` / `windowHoverColor` / `gridCountInWorld` / `gridAlphaInWorld` / `gridLineWidthInWorld` / `gridCellSize` / `gridColorInWorld` / `gridColorInDisplay` / `dirty`。うち grid 系・`windowHoverColor`・`pluginEnabled` の読み手は **すべて SE 側**（`GridRenderer` / `SettingWindow` / `COM3D2.SceneEditor.Plugin.cs`）で、MTE 側の同名フィールドは死んでいる。**`keyRepeat*` は誤り（B-1 で判明）**: `Timeline/Config.GetKeyDownRepeat` が読んでおり、タイムラインのフレーム送りで現役だった |
| B-2 | **「色をHSVで指定」トグルが無効** | `TimelineSettingWindow`（:891）は `timelineConfig.useHSVColor` を書くが、実際に参照されるのは `GUIView.option.useHSVColor` ←`COM3D2.SceneEditor.Plugin.cs:14-19` 経由の **SE 側** `config.useHSVColor`。タイムライン設定側のトグルは押しても何も起きない |
| B-3 | **Undo/Redo が 2 系統** | SE の `HistoryManager`（Ctrl+Z、`HistoryWindow`、`historyLimit`）と MTE の `TimelineHistoryManager`（TimelineXml のスナップショット、`Timeline/Config.historyLimit`）。**調査時の記述は不正確（B-3 で判明）**。SE の `HistoryManager` への統合は `TimelineHistoryEntry` 経由で既に済んでおり、Ctrl+Z も履歴ウィンドウもそこを通る。死んでいたのは MTE が並行して積んでいた**もう一本の履歴スタック**（`historyList` に `TimelineXml` を溜めるが `Undo()` / `Redo()` に呼び出し元が無い）の方 |
| B-4 | **BGM の入口が 2 箇所** | `SoundWindow` の BGM タブ（`BgmUtils`、ゲーム内蔵 BGM の再生）と `TimelineSettingWindow` の BGM 設定（`BGMManager`、外部音声ファイルをタイムライン同期再生）。音源が違うので機能は別だが、UI 上は「BGM」が 2 箇所にあり同時再生の調停も無い |
| B-5 | **シーン状態の永続化が 2 系統** | `ScenePresetData`（camera / background / light / undress / gravity / png / IK / boneEdit / slotYure / morph / material / modelShapeKey / look / motion）と `TimelineXml`。ほぼ同じ状態空間を別スキーマで保存する。静的プリセット vs アニメーションという役割分担自体は妥当だが、**片方にしか無い項目が実害を生む**（B-6） |
| B-6 | **シーンプリセットがタイムライン視線を保存しない**（A-1c で解消済み） | `ScenePresetLook`（ScenePresetData.cs:448-484）は `MaidLookMode` / `lookX/Y` / `boHeadToCam` / `boEyeToCam` / 注視対象しか持たない。A-1 の MTE 側状態（`lookAtTargetType` / `eyeEulerAngle`）はプリセットに入らないため、保存→ロードで消える |

## C. 片側にしか無い（統合ではなく穴埋めの候補）

| 対象 | 状況 |
|---|---|
| **重力（髪/スカート）** | SE の `MaidGravityController` / `MaidGravityWindow` のみ。MTE 側に対応レイヤーが無く、キー化できない（`Timeline/` に gravity 参照ゼロ） |
| **瞳の位置/スケール** | MTE の `MaidCache.eyesPosL/R` / `eyesScaL/R` のみ。EyesTimelineLayer の「位置タブ」に残置され、SE の個別ウィンドウには無い |
| **モデル注視（`LookAtTargetType.Model`）** | `StudioModelManager` が未接続（`SceneEditorHack.modelList` は空リスト :172）のため、`TimelineLookRowDrawer` が選択肢から除外している（:26-30） |

## D. 追加調査で見つかった未対応の二重化(2026-08-30)

A/B 完了後の再調査で見つかった残り。A-1 追補(瞳回転→顔向き)と同型の**概念レベル二重化**(同じ目的を別の操作系・値表現で持つ)を含む。本 loop のスコープ外で、着手は別途計画する。

| # | 対象 | SE 側 | MTE 側 | 判定・症状 |
|---|---|---|---|---|
| D-1 | **脱衣/マスク切替** | `MaidUndressController`(:199-217 `SetUndressed`。`UndressCategory` 単位、`SetMaskMode(None)` → `SetMask`)＋ `UndressSnapshot` / `MaidUndressWindow` | `Timeline/DressUtils`(:260-334 `SetMask` / `GetMask`。`DressSlotID` 単位で `TBody` 直書き)、`UndressTimelineLayer` / `UndressItemInspector`(:14 に「SE を迂回して直接書く」と明記) | **奪い合い＋概念重複**。同じ `TBody` マスクに 2 本の書き込み経路・2 つの値表現。`MaskMode` リセットは SE 側にしか無く、MTE 経由では `MaskMode.None` が保証されない。片側のみの要素: SE=衣装タイプ、MTE=めくれ |
| D-2 | **指ブレンド** | `MaidFingerBlendController`(:99-240)。ゲームの `FingerBlend` を使わず自前実装(`valueOpen` / `valueFist` / digit 別 `isLock` を独自に持ちボーン回転を直書き) | `MotionTimelineLayer`(:387-412 / :540-570)がゲームの `FingerBlend.BaseFinger` を `MTEUtils/Extensions`(:227-334)のリフレクションで書く | **概念重複＋奪い合い**(瞳回転 vs 顔向きと同型)。同じ指ボーンを 2 つの操作系・2 つの値表現で持ち、SE のスライダーとタイムラインキーが別の値を指す |
| D-3 | **まばたき(`boMabataki`)** | `MaidFaceMorphController.SetMabataki`(:360-367)＋ `MaidFaceWindow` トグル / 表情プリセット / `FaceSnapshot` / シーンプリセット | `TimelineFaceManager.SetMabatakiOff`(:64-74)が `maid.boMabataki = false` を直書き。`MorphTimelineLayer`(:113)が毎 LateUpdate で呼ぶ | **奪い合い**(A-5 の取りこぼし)。表情レイヤーが有効な間、SE のまばたきトグル ON・プリセット/履歴の復元が即座に潰される。委譲だけでは足りず「表情レイヤー有効中は SE トグルを無効化」の親スイッチ設計(A-1b と同じ形)が要る |
| D-4 | **モーション再生状態** | `MaidMotionState`(:125-190 `StopMotion`。停止中の真実は `_resetClipNames` 辞書、:270-292 `SetPlaybackTime`) | `MaidCache.anmSpeed` / `motionSliderRate` / `isAnmEnabled` / `PlayAnm`(:69-180, :473-492)が `AnimationState` を直書き | **奪い合い＋概念重複**。`SceneEditorHack.isAnmEnabled` は委譲済みだが `MaidCache` 自身は未委譲。停止の真実が 2 つあり、タイムライン再生後に SE が「再生中」と誤認する。SE 経路にある `CaptureBasePose` の呼び直しも MTE 経路では走らない |
| D-5 | **メイド配置(Transform)** | `MaidPlacementPreset`(`SetPos` / `SetRot`)＋ `MaidVisibilityController`(:54-71 非表示 = `HiddenPosition(100,0,0)` へ退避)＋ `MaidManipulateManager.GetLogicalPosition` | `MoveTimelineLayer`(:73-111)が `maid.transform` を毎 LateUpdate 直書き(`localScale` 含む) | **奪い合い**。MTE は退避を知らないため、再生中に退避メイドを引き戻す/退避座標 `(100,0,0)` がキーに焼かれる。`SetRestorePosition` も呼ばれない |
| D-6 | **メインカメラ** | `CameraWindow`(:361-374, :463-493 `CameraMain.SetTargetPos` 等)＋ `CameraSnapshot` | `CameraTimelineLayer`(:118-124)が `UltimateOrbitCamera` を直叩き | **概念重複(軽度)**。実体は同じカメラだが操作 API が 2 系統、ロールの持ち方も別。優先度低。`Timeline/Manager/CameraManager` はオーバーレイ専用の `MTEFrontCamera` で競合しない |
| D-7 | **視線の向け先 vs 注視先** | `MaidLookController` の `MaidLookMode`(カメラ/マウス/方向指定/オブジェクト/無し)＋視線タブ「向け先」コンボ | `MaidCache.lookAtTargetType`(`LookAtTargetType`: None/Camera/Maid/Model)＋メイド・ポイント指定、視線タブ「注視先」行 | **概念重複**(A-1 の残り)。書き込みは `MaidLookBridge.ResolveLookMode` で一本化済みだが、同じ「どこを見るか」に 2 つの列挙・2 つの UI 行が残り、キー化 ON/OFF で操作する行が入れ替わる。MTE 側にしか無い値(メイドのポイント指定)と SE 側にしか無い値(マウス)があり単純な統合はできない。瞳回転→顔向きと同様に片方へ寄せるなら、キー化の有無で意味が変わらない共通の注視先表現の設計が要る → 統合済み(実装メモ参照) |

**確認済み・問題なし**: メイド/モデルのシェイプキー(`EditTargetStore` 追跡のみで値は一本化)、マテリアル 3 レイヤー、IK 接地(`MaidIKHoldController` 経由)、拡張ボーン/モデルボーン(`BoneEditManager` 経由)、モーフ名前解決・追跡(A-5 で統合済み)。**片側のみ**: 衣装差し替え(`DressTimelineLayer`、SE に書き手なし)、ボイス(`MaidCache`、SE に対応実装なし)。

**優先度の所感**: 最小コストは D-3(ただし親スイッチ設計が要る)。実害が出やすいのは D-4 / D-5。統合効果が大きいのは D-1 / D-2。D-7 は書き込み経路の統合(A-1)が済んでいるため実害は小さかったが、視線 UI を 1 系統へ畳むために統合済み。

## 統合方針の示唆

1. **A 分類は「MTE 側をアダプタ化する」で揃える。** 既に `StudioLightManager` / `SceneEditorHack` / `PngObjectTimelineManager` で確立したパターンがあるので、新方式の発明は不要。
   - A-1: `MaidCache` の視線フィールドを `MaidLookController` の状態へ委譲し、`UpdateHeadLook` / `UpdateLookAtTarget` を SE の適用経路に合流させる。表情ウィンドウは 1 セクションに統合し、`eyeMoveType` / `useHeadKey` もそこへ集約する。
   - A-2: `MaidCache.UpdateMuneYure` を `MaidMuneYureController` 経由へ差し替え、`useMuneKeyL/R` を SE のトグルの別名にする。
   - A-3: `isAutoYureBone` の一括上書きをやめ、`SlotYureUtil` の状態を唯一の真実にする（`BoneEditManager` の自動 OFF と同じ経路へ）。
   - A-4: `canIKVisible` による `isIKVisible`（および `isIkBoxVisibleRoot/Body`）の上書きを廃し、SE のトグルを唯一の入口にする。
   - A-5: `TimelineFaceManager` の名前解決・倍率換算を `MaidFaceMorphController` へ一本化する。
2. **B-1 / B-2 / B-3 は削除で解消できる。** MTE 側の重複フィールドは読み手が無いので落とせる。`TimelineHistoryManager` は「UI へ繋ぐ」か「撤去する」かの判断が要る（`Timeline/IKHoldEntity.cs` を撤去した前例と同じ扱い）。
3. **B-6 は A-1 の統合と同時に解決する。** 視線の所有者が 1 つになれば `ScenePresetLook` の既存スキーマで足りる。
4. **着手順は A-1 → A-2/A-3 → A-4 → A-5 → B。** A-1 が最も表面化しており、統合すると B-6 も落ちる。A-5 は影響範囲（表情プリセット・モーフ追跡）が広いので後段。

## Loop 実行プロトコル(統合作業の自走用)

上記 A/B 分類の統合を `/loop` で 1 反復ずつ進めるための手順。`docs/superpowers/specs/timeline-item-inspector-roadmap.md` §7 と同じ運用。

### 反復単位

**1 反復 = 下記チェックリストの未完了項目 1 つ**。上から順に消化する(A-1 は影響が大きいため 3 分割してある)。

### 1 反復の手順

1. チェックリストから最初の未完了項目を選ぶ
2. **調査**: 本書の該当行(A/B 表)に記載した SE 側・MTE 側の実装を読み、委譲の切り口を確定する。参照パターンは既存アダプタ(`Timeline/Hack/SceneEditorHack.cs`、`Timeline/Manager/StudioLightManager.cs`、`Timeline/Manager/PngObjectTimelineManager.cs`)
3. **実装**: 原則は「**MTE 側をアダプタ化し、SE のマネージャ/コントローラを唯一の真実にする**」。MTE レイヤー本体(逐語コピー)への変更は最小限にとどめ、委譲は MaidCache / Manager / Hack 層で行う
4. **テスト**: 委譲マッピング・名前解決などの純粋ロジックに単体テストを追加し、テストを実行して通す
5. **ビルド**: COM3D2 / COM3D25 両構成を MSBuild 直接実行で確認(ゲーム停止中に `debug.bat` を使わない)
6. **コードレビュー**: code-review スキルを実行し、妥当な指摘を取り込む
7. **本書更新**:
   - チェックリストの当該項目を `[x]` にする
   - 「実機確認項目」へ確認すべき観点を追記する(実機確認はユーザー操作待ちのため loop では行わない)
   - 調査で本書の記述と実装が食い違っていた場合は、該当表も修正する
8. **コミット**: commit スキルでコミット
9. 全項目完了なら loop を停止し、実機通し確認待ちであることを報告する

### 反復ごとの注意

- **仕様のデフォルトは「SE 側の挙動を正とする」**。MTE 独自の状態を残す判断をした場合は、その理由を本書に追記する
- ビルド・テストが通らない状態でチェックを付けない・コミットしない
- 実機でしか確認できない挙動はブロッカーにせず、実機確認項目に積んで先へ進む
- **待つ理由が無ければ次の項目へそのまま進む**。1 項目のコミットまで終わり、実際にブロックしている事象が無いなら、間隔を空けずに次の反復を始めてよい
- ただし、レビュー結果の取り込みが済んでいない項目を残したまま次の反復へ進んではならない
- タイムラインの保存データ(`TimelineXml`)の互換性を壊す変更(フィールド削除・意味変更)は行わない。読み込み側で SE 状態へ流し込む方向で吸収する
- C 分類(片側にしか無い機能)は本 loop のスコープ外。穴埋めは別途計画する

### 進行チェックリスト

A 分類(状態の奪い合い解消):

- [x] A-1a: `MaidCache` の視線フィールド(`lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `eyeEulerAngle`)を `MaidLookController` へ委譲し、`UpdateHeadLook` / `UpdateLookAtTarget` を SE の適用経路へ合流させる
- [x] A-1b: 表情ウィンドウ視線タブの「視線」「タイムライン視線」を 1 セクションへ統合し、`TimelineSettingWindow` の「メイド目線」(`eyeMoveType`)と「顔/瞳の固定化」(`useHeadKey`)もそこへ集約する
- [x] A-1c: B-6 の解消確認 — 視線の所有者統合後、`ScenePresetLook` の保存→ロードで視線状態が欠落しないことを確認し、不足があればスキーマへ追加する
- [x] A-2: `MaidCache.UpdateMuneYure` を `MaidMuneYureController` 経由へ差し替え、`useMuneKeyL/R` を SE トグルの別名にする(`SceneEditorHack.useMuneKeyL/R` の空実装も解消)
- [x] A-3: `MotionTimelineLayer` の `isAutoYureBone` 一括上書きをやめ、`SlotYureUtil` の状態を唯一の真実にする(`BoneEditManager` の自動 OFF と同じ経路へ)
- [x] A-4: `StudioHackManager.isPoseEditing` setter の `canIKVisible` による `isIKVisible` / `isIkBoxVisibleRoot/Body` 上書きを廃し、SE 側のトグルを唯一の入口にする(`alwaysShowIK` の扱いも整理。`isBoneVisible` 自体はアダプタ化済みで対象外)
- [x] A-5: `TimelineFaceManager` の名前解決・倍率換算を `MaidFaceMorphController` へ一本化する(表情プリセット・モーフ追跡への影響が広いため差分を丁寧に確認する)

B 分類(二重管理の整理):

- [x] B-1: `Timeline/Config.cs` の読み手が無い重複フィールド(grid 系 / `windowHoverColor` / `keyRepeat*` 等 13 個のうち死んでいるもの)を削除する。あわせて `Timeline.xml` の後方互換(未知フィールドの読み飛ばし)を確認する
- [x] B-2: `TimelineSettingWindow` の「色をHSVで指定」トグルを SE 側 `config.useHSVColor` へ接続するか、トグル自体を撤去する(SE の設定ウィンドウに同項目があるなら撤去を優先)
- [x] B-3: `TimelineHistoryManager` を撤去する(`Timeline/IKHoldEntity.cs` 撤去と同じ扱い。`TimelineSettingWindow`「ポーズ履歴無効」も併せて整理)。調査の結果 SE の `HistoryManager` へ統合できる見込みが立つならそちらを優先し、判断理由を本書へ追記する

D 分類(追加調査分。優先度の所感に沿い、最小コスト → 実害大 → 統合効果大 → 低優先の順):

- [x] D-3: `TimelineFaceManager.SetMabatakiOff` の `boMabataki` 直書きをやめ、`MaidFaceMorphController.SetMabataki` を唯一の書き手にする。表情レイヤー有効中は SE トグルを無効化する親スイッチ設計(A-1b と同じ形)を含む
- [x] D-5: `MoveTimelineLayer` の `maid.transform` 直書きを SE の配置系(`MaidVisibilityController` の退避・`SetRestorePosition`)と調停する。退避中メイドの引き戻し・退避座標 `(100,0,0)` のキー焼き込みを防ぐ
- [x] D-4: `MaidCache` のモーション再生系(`anmSpeed` / `motionSliderRate` / `PlayAnm`)を `MaidMotionState` と調停し、停止の真実を一本化する(`CaptureBasePose` の呼び直しを含む)
- [x] D-1: `Timeline/DressUtils` の `TBody` マスク直書きを `MaidUndressController` 経由へアダプタ化する(`MaskMode` リセットの保証、`DressSlotID` ↔ `UndressCategory` の対応設計を含む。片側のみの要素は残す)
- [x] D-2: `MotionTimelineLayer` の `FingerBlend.BaseFinger` 書き込みと `MaidFingerBlendController` の自前実装を調停し、指ボーンの書き手・値表現を一本化する
- [x] D-6: メインカメラ操作(`CameraTimelineLayer` の `UltimateOrbitCamera` 直叩き)と `CameraWindow` の API を調停する → **調査の結果、現状維持で確定**(下記実装メモ参照)
- [x] D-7: 視線の「向け先」「注視先」の概念統合(キー化の有無で意味が変わらない共通の注視先表現の設計)。**統合済み**(下記「D-7 の実装メモ」参照)

B-4(BGM 2 箇所)と B-5(永続化 2 系統)は現状維持で確定。B-4 は音源が別で機能が異なり、B-5 は静的プリセット vs アニメーションの役割分担が妥当なため、本 loop では扱わない。

### 視線の統合後仕様(A-1 まとめ)

A-1a〜c 完了後の現行仕様。経緯・実装差分は後続の各実装メモを参照。

**所有者と経路**

- 向け先(`body0.trsLookTarget`)の書き手は SE の `MaidLookController` の一本のみ。MTE は `Timeline/MaidLookBridge.cs` 経由でこのコントローラを駆動する側に降りた
- `MaidCache` の `lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `lookDirection`(顔向きキー)は MTE 側に残るが、これらは `TimelineXml` / `EyesTimelineLayer` が読み書きする**キーフレームの指定値**であって、ゲーム状態ではない
- **瞳回転という概念は撤去した**(A-1 追補)。瞳は「目を向ける」(`boEyeToCam`)+ 向け先にのみ従い、方向の指定は顔向き一本。旧 EyesRot キーは顔向きへ変換して読む

**UI と操作の親スイッチ**

- 入口は表情ウィンドウの視線タブ 1 箇所(旧「視線」「タイムライン視線」の 2 セクションと `TimelineSettingWindow` の 2 行を統合)
- 「視線をキー化」(旧称「顔/瞳の固定化」、`TimelineData.useHeadKey`)が親スイッチ:
  - **ON**: タイムラインの「注視先」行が向け先を駆動する。SE の「向け先」コンボは無効化される(2 つの入口を並べない)
  - **OFF**: SE の向け先・顔向き・注視対象を従来どおり操作する。タイムラインは向け先に触らない(SE の設定が正)

**目線種別(「メイド目線」`eyeMoveType`)**

- フラグ(`boHeadToCam` / `boEyeToCam` / `boEyeSorashi` / `HeadToCamFadeSpeed`)だけを設定する(`MaidLookBridge.ApplyEyeMoveType`)。向け先の判断材料にはしない
- `Maid.EyeToCamera` は使わない(フラグ設定と同時に `trsLookTarget` まで書き換えてしまうため)

**注視先が無いときの向け先(キー化 ON)**

- 注視先「なし」(または対象が未解決)のときは向け先を「方向指定」にし、**顔向きキー**(`MaidCache.lookDirection`、旧 EyesRot 行)が注視点を駆動する。顔も瞳も同じ点を向く
- ただし目線種別が「そらす」系なら「無し」へ倒す。`TBody` のそらし演出は `trsLookTarget == null` かつ `boLockHeadAndEye == false` が条件のため

**視線そらし(キー化 OFF)**

- 従来どおり SE で向け先を「無し」(`MaidLookMode.無し`、A-1a で新設)にしたときだけ効く。目線種別を「そらす」にしても向け先は自動で変わらない(`EyeToCamera` の無条件 null 化は SE 設定を壊すため踏襲していない)

**プリセット(B-6 の解消)**

- `ScenePresetLook`(v26)は SE 側の向け先一式に加え、キーフレーム指定値(`timelineTargetType` / `timelineTargetIndex` / `timelineMaidPointType` / `timelineLookX/Y`)も保存する
- 「視線をキー化」(`useHeadKey`)自体は保存しない。保存時と復元時でこの設定が違うと、指定値の復元が `mode` の復元を上書きしうる(既知の前提)

### A-1a の実装メモ(向け先の所有者統合)

`Timeline/MaidLookBridge.cs` を新設し、`trsLookTarget` の書き手を SE の `MaidLookController` に一本化した。仕様は上記まとめのとおり。実装上の補足:

- **`MaidLookMode.無し` は列挙の末尾へ追加**: 既存プリセット互換のため
- **そらし時の内部動作**: `UpdateLookAtTarget` の `LockHeadAndEye` と `UpdateEyeEulerAngle` がそらし指定時に手を引く形で実現した
- **`MaidCache.useHeadKey` プロパティを削除**: `trsLookTarget` を直接読み書きする所有権違反で、呼び出し元も無かった(`TimelineData.useHeadKey` とは別物)

### A-1b の実装メモ(UI 統合)

表情ウィンドウ視線タブへの UI 統合(上記まとめ参照)を実施した。

- `TimelineSettingWindow` からは「メイド目線」「顔/瞳の固定化」の 2 行と、未使用になった目線種別コンボを削除した
- `TimelineLookRowDrawer.HeadKeyDisabledMessage` の文言を更新し、`EyesItemInspector` の案内も新しい設定場所(視線タブ)を指すようにした
- UI の再配置のみで純粋ロジックの追加が無いため、単体テストは追加していない(判定に使う `ResolveLookMode` は A-1a で網羅済み)

### A-1c の実装メモ(プリセット対応・B-6 の解消)

向け先そのもの(`mode` / `lookX` / `lookY` / 注視対象 / `boHeadToCam` / `boEyeToCam`)は既存スキーマで往復できていたが、キーフレーム指定値が欠落していたため `ScenePresetLook` へ追加した(プリセット v26。項目は上記まとめ参照)。

- **旧プリセット互換**: `timelineTargetType` が空なら未記録として `MaidCache` へ触らない
- **番号で持つ理由**: `timelineTargetIndex` は `TimelineXml` のキー(`TransformDataLookAtTarget.targetIndex`)と同じ相対番号。並びが変わると別の対象を指すが、既存のデータモデルの慣習に合わせた
- **復元順**: 3 つのセッターがいずれも `UpdateLookAtTarget` を呼ぶため、番号・ポイントを先に入れて種別を最後にする
- **B-6 の扱い**: 上記により解消。B 表の B-6 は本項目で閉じる

### A-1 追補の実装メモ(瞳回転の撤去・顔向き一本化)

「瞳回転は顔向き+『目を向ける』とほぼ同義」というユーザー判断により、瞳回転という概念自体を撤去し、方向の指定を顔向き一本にした。

- **旧 EyesRot キーは顔向きへ変換して読む**: XML キー名は `EyesRot` のまま(TimelineXml スキーマ変更なし)、値(horizon/vertical、各 -1〜1)を `lookX/lookY` としてそのまま解釈する。レイヤー上の表示名は「視線」→「顔向き」。瞳ヨー ±90° と顔向き注視点は厳密には同角にならないため、既存タイムラインの見た目は変わりうる(割り切り)
- **適用経路**: `ApplyEyes(EyesRot)` → `MaidCache.lookDirection` → `UpdateLookAtTarget` → `MaidLookBridge.ApplyLookMode` が「方向指定」のとき lookX/lookY を `MaidLookController.SetState` へ渡す
- **`ResolveLookMode` の変更**: 注視先なし(None または対象未解決)は、そらし指定なら「無し」、それ以外は「方向指定」。キー化中の視線そらしは注視先「なし」で自動的に効くようになった(従来は `LockHeadAndEye` の解除条件のみ)
- **撤去したもの**: `MaidCache.eyeEulerAngle` / `UpdateEyeEulerAngle`(TBody への瞳直書きごと)、`UpdateHeadLook` の瞳リセットブロック、`LockHeadAndEye(true)` の経路(瞳の固定書き込みが無くなったため常に解除)、視線タブ・項目インスペクタの瞳回転スライダー(顔向き行 `DrawLookDirectionRows` に置換)、`ScenePresetLook.eyeAngleX/Y/Z`
- **視線タブの顔向きスライダーの二面性**: キー化 OFF は SE の `lookX/lookY`(向け先「方向指定」で活性・履歴あり)、キー化 ON はタイムラインの顔向きキー(注視先「なし」で活性・履歴なし)を編集する。ラベルは同じ「顔向き左右/上下」
- **プリセット**: `eyeAngleX/Y/Z` → `timelineLookX/Y` に置換(v26 のまま。v26 は未リリースのため同バージョン内で差し替え、`eyeAngle*` 付きで保存されたデータは同属性だけ読み飛ばされる)
- **符号の向きは実機確認**: EyesRot の vertical と顔向き `lookY` の上下方向が一致するかは静的解析で確定できないため、実機確認項目に積んだ

### A-2 の実装メモ

`MaidCache.UpdateMuneYure` の直接上書きをやめ、`MaidMuneYureController` を唯一の所有者にした。

- **`UpdateMuneYure` は `Reapply` へ委譲**: アニメーション再生でボディの揺れものが既定へ戻る分を SE の記録で塗り直すだけにした。タイムラインのフラグはここでは見ない
- **`SceneEditorHack.useMuneKeyL/R` の空実装を解消**: フラグの反映はこちらが担い、呼出済み全メイドへ `SetYure(maid, isLeft, !useMuneKey)` を書く(「物理無効」なので揺れとは反転する)
- **同期は一方向のみ**: SE のトグルはメイド別、タイムラインのフラグは全体設定。逆方向は 1 体の操作で全体設定が動くうえ、このフラグは胸ボーンのキー化可否判定(`TransformDataRotation`)も兼ねるため行わない
- **対象は `calledMaids`**: コントローラの記録は `MaidManipulateManager` の呼び出し管理と同じ寿命を持つため、管理外のメイドの分を作らない
- **削除したもの**: 意味を失った `TimelineData` セッター内の `maidManager.UpdateMuneYure()` 呼び出しと、呼び出し元が無くなった `MaidManager.UpdateMuneYure`
- **`!maid.boMAN` ガードの削除**: `Reapply` は記録の無いメイドへ何もせず、記録は胸の揺れトグルを操作したメイドにしか作られないため不要
- 新規の純粋ロジックは反転 1 つのみで、`Maid` / `TBody` 依存のため単体テストは追加していない

### A-3 の実装メモ

**調査時の症状の記述が実装と食い違っていた。** `MotionTimelineLayer` の一括上書きが呼ぶ `MaidCache.GetYureState` は常に false を返し、`SetYureState` は空実装だった(「PartsEdit 連携は未移植のため無効化している」とコメントあり)。つまりこのブロックは効果の無い死んだ経路で、Inspector の揺れ設定が潰される事象は起きていなかった。A 表の該当行を修正済み。

方針「一括上書きをやめ、`SlotYureUtil` を唯一の真実にする」は、死んだ経路の削除という形で達成した(挙動変更なし)。

- **削除したもの**: `MotionTimelineLayer` の `isAutoYureBone` ブロック、呼び出し元が無くなった `MaidCache.IsYureSlot` / `GetYureState` / `SetYureState`、効果の無い設定 `Config.isAutoYureBone` と「自動揺れボーン」トグル、ブロック専用だった `_extendSlotNames`
- **`Timeline.xml` の後方互換**: `XmlSerializer` は未知要素を読み飛ばすため、既存ファイルに残る `<isAutoYureBone>` は無害。`TimelineConfigXmlTests` で固定した(B-1 でフィールドを削除する際の前提にもなる)
- **残したもの**: `ExtendBoneCache.yureSlotNames` / `IsYureSlot`(拡張ボーンキャッシュのデータ)、`PartsEditHackBase` の同名メンバー(未移植の PartsEdit 連携側で別階層)

### A-4 の実装メモ

`StudioHackManager.isPoseEditing` の setter から表示の書き換えを外し、編集モードの委譲だけを行うようにした。ボーン/IK 表示の所有者は SE の `MaidManipulateManager.isBoneVisible` 一本になった。

- **編集モードへの追従は残す**: 編集モード ON でボーン表示 ON、OFF で OFF。MTE の挙動に合わせる(当初は追従ごと外したが、編集モードに入るたび手動でトグルを ON にする必要があり使いにくかったため戻した)。落としたのは `canIKVisible` によるレイヤー種別依存の部分だけ
- **値が変わらない再設定もしない**: `TimelineManager.SetCurrentLayer` 末尾にあった「IK表示反映のために再設定」は `canIKVisible` を再評価するための行で、追従を戻すとレイヤー切り替えのたびにボーン表示が強制 ON になるため撤去した
- **削除したもの**: `canIKVisible`、`StudioHackBase` の `hasIkBoxVisible` / `isIkBoxVisibleRoot` / `isIkBoxVisibleBody`(このリポジトリでは `hasIkBoxVisible` が常に false で当該分岐は死んでいた)、上書きの消滅で呼び出し元を失った `isIKVisible`(`SceneEditorHack` の実装ごと)、設定 `isIkBoxVisibleRoot` / `isIkBoxVisibleBody` / `alwaysShowIK`、「常にIKを表示」トグル
- **`Timeline.xml` の後方互換**: A-3 と同じく未知要素の読み飛ばしで吸収。削除した 3 項目を `TimelineConfigXmlTests` の XML へ追加した

### A-5 の実装メモ

モーフ名の解決と倍率換算を `MaidFaceMorphController` へ一本化し、`TimelineFaceManager` の独自実装(`CheckMorph` / `CheckMorphFB` / `IsFBFace` / `GetRatio`)を削除した。

ゲーム側(`WindowPartsFaceMorph.GetBlendIdx` / `TMorph`)で裏取りして直した点:

- **CRC 顔の判定を `PartsVersion >= 120` に統一**。SE 側の旧判定 `GetFaceTypeGP01FB() != MAX` は、`GetFaceTypeGP01FB` が MAX を返さないため常に真だった(旧顔でも無駄なサフィックス探索をしていた)
- **サフィックス探索は eyeclose 系に限定しない**。ゲーム側は eyeclose 系限定だが、`TMorph` の初期化を見るとサフィックス付きのキーを持つのは eyeclose 系と `itome` の 2 系統。名前を問わず試す方が `itome` まで扱えて広く、衝突するキーも無い
- **目型のインデックスを丸める**。`crcFaceTypesStr` は 3 要素しかないため、想定外の値でも配列外参照にならないようにした

**値スケールの扱い(重要)**: 倍率(CRC 顔のジト目 `eyeclose3` だけ 3 倍)は「UI 値 ↔ TMorph 値」の換算であって保存形式ではない。そこで API を 2 系統に分けた。

| 用途 | API | 単位 |
|---|---|---|
| 表情ウィンドウのスライダー・タイムライン | `GetMorphValue` / `SetMorphValue`(+`*ByName`) | UI 値 (0〜1)。倍率を適用 |
| プリセット・履歴スナップショット | `GetStoredMorphValue` / `SetStoredMorphValue` | TMorph の生値。倍率なし |

保存経路が従来どおり生値のままなので、**既存のマイ表情プリセット・シーンプリセットの解釈は変わらない**(当初は保存経路にも倍率を通す実装にしていたが、既存ファイルが 3 倍で読まれるためレビュー指摘を受けて分離した)。これで表情ウィンドウとタイムラインの見え方も揃う。

### B-1 の実装メモ

読み手を数え直した結果は次のとおり。**調査時の「`keyRepeat*` は死んでいる」は誤りだった**(A 表と同様に B 表も訂正済み)。

| フィールド | MTE 側の読み手 | 対応 |
|---|---|---|
| `pluginEnabled` / `windowHoverColor` / grid 系 6 個 | 0 | 削除 |
| `keyRepeatTimeFirst` / `keyRepeatTime` | `Timeline/Config.GetKeyDownRepeat`(タイムラインのフレーム送り) | SE の設定を読むよう変更してフィールドは削除 |
| `historyLimit` | `TimelineHistoryManager` | B-3 で扱う |
| `useHSVColor` | 0(書き手だけ `TimelineSettingWindow`) | B-2 で扱う |
| `dirty` | 23 箇所 | 現役。対象外 |

キーリピートは SE / MTE のどちらも UI が無く XML 直接編集のみで、SE 側を変えてもフレーム送りには効かない二重管理だった。`GetKeyDownRepeat` が SE の設定を参照するようにして解消した。

`Timeline.xml` の後方互換は A-3 / A-4 と同じく未知要素の読み飛ばしで吸収し、削除した項目を `TimelineConfigXmlTests` の XML へ追加した。

### B-2 の実装メモ

`TimelineSettingWindow` の「色をHSVで指定」トグルと `Timeline/Config.useHSVColor` を撤去した。

- **接続ではなく撤去を選んだ理由**: SE の設定ウィンドウには同項目が無いが、`MTEUtils/ColorPickerWindow` のカラーピッカー内に RGB/HSV を切り替えるボタンがあり、そこが `GUIView.option.useHSVColor`(= SE 側 `config.useHSVColor`)を読み書きしている。使う場所に生きた導線があるため、設定ウィンドウ側の入口は要らない
- `docs-site/guide/configuration.md` の `useHSVColor` は `SceneEditor.xml` の項目(SE 側)の説明なので、そのまま残す

### B-3 の実装メモ

**調査時の記述は不正確だった**。SE の `HistoryManager` への統合は `Timeline/TimelineHistoryEntry.cs`(before/after の `TimelineXml` を持つ `IHistoryEntry`)経由で既に済んでおり、`AddHistory` はそこへエントリを積んでいた。死んでいたのは MTE が並行して積んでいたもう一本の履歴スタックの方で、そこだけを撤去した。B 表の該当行も訂正済み。

- **撤去したもの**: `historyList` / `historyIndex` / `historyListInv` / `Undo` / `Redo` / `RestoreHistory` / `TimelineHistoryData`、および読み手が無くなった `Timeline/Config.historyLimit`
- **残したもの**: `lastCommittedXml`(積むエントリの before)と `AddHistory` / `ClearHistory`。クラスの責務は「SE 履歴への橋渡し」だけになった
- **仕様変更**: `Timeline.xml` の `historyLimit` を 0 にしてタイムライン操作だけ履歴から外す、という使い方はできなくなった。履歴の有効・無効は SE の設定(`SceneEditor.xml` の `historyLimit`、設定ウィンドウから編集可)に一本化される。この項目は UI に接続されておらず、XML を直接編集した場合にのみ効いていた
- **「ポーズ履歴無効」は対象外**: `Config.disablePoseHistory` は別フィールドで、ポーズ編集中の履歴登録を抑える現役の設定(`TimelineManager` が参照)

### D-3 の実装メモ(まばたきの抑止化)

`TimelineFaceManager.SetMabatakiOff` の `boMabataki` 直書きをやめ、SE の `MaidFaceMorphController` に「抑止(サプレス)」概念を追加して委譲した。所有者は SE 側の一本になった。

- **抑止の意味**: `SetMabatakiSuppressed(maid, true)` は現在のユーザー設定(`boMabataki`)を退避してから false を書き、解除時に退避値を復元する。抑止中の `SetMabataki` / `GetMabataki` は退避値の読み書きになるため、表情プリセット・`FaceSnapshot`・シーンプリセットの保存/復元は抑止中でもユーザー設定として正しく往復する(復元の実効は抑止解除時)
- **抑止のライフサイクルは `MorphTimelineLayer` が管理**: 適用中(`ApplyPlayData`)は毎フレーム抑止を主張(ゲーム側が `boMabataki` を立て直すため)。ポーズ編集中・メイド未ロード・`Dispose` / `OnPluginDisable` で解除する。対象メイドは `_mabatakiSuppressedMaid` で追跡し、差し替え時に旧メイドを解除する
- **親スイッチ(A-1b と同じ形)**: 表情ウィンドウの「強制上書き」トグルは抑止中 `SetEnabled(false)` で編集不可にする(値の表示はユーザー設定のまま)
- **`EyeMabataki = 0f`(進行中のまばたきの目閉じ量リセット)は MTE 側に残置**: タイムライン適用のフレームでだけ必要な後始末で、状態の所有ではないため
- **破棄済みメイドの後始末**: Unity の null 化で復元先が無い場合は退避エントリを捨てる(辞書リーク防止)。レイヤー側の解除判定は Unity の `==`(fake-null)ではなく `ReferenceEquals` で行い、破棄済みでも解除経路が必ず走るようにした(レビュー指摘の取り込み)
- 新規ロジックは `Maid` 依存のため単体テストは追加していない(A-2 と同じ判断)

### D-5 の実装メモ(メイド配置と退避契約の調停)

`MoveTimelineLayer` を SE の退避契約(`MaidVisibilityController`。非表示 = `(100,0,0)` へ退避、見かけの位置は戻り先で管理)に対応させた。

- **位置の適用は `ApplyPosition` に集約**: 退避中(`MaidManipulateManager.IsVisible == false`)は実座標を触らず `SetRestorePosition` で戻り先だけを更新する。再生が退避メイドを画面へ引き戻さず、再表示時にはタイムラインの最新位置へ戻る
- **キーの記録は見かけの位置**: `UpdateFrame` は `transform.localPosition` ではなく `GetLogicalPosition` を焼く。退避座標 `(100,0,0)` がキーに混入しない。座標系は `Maid.SetPos/GetPos` = `transform.localPosition` で一致することを逆コンパイル済みソースで確認済み
- **回転・スケールは従来どおり直接適用**: 退避は位置のみの契約で、回転/スケールは退避中に書いても無害(再表示で位置だけ戻る)
- **対象外**: `TimelineManager.OnPoseEditUpdated/End` と `MotionTimelineLayer` にある transform 退避・復元は「自分で保存した値を戻す」対称ペアで、SE 状態との奪い合いではない
- 新規ロジックは `Maid`/マネージャ依存のため単体テストは追加していない(A-2 と同じ判断)

### D-4 の実装メモ(モーション再生状態の整合)

「停止の真実」の食い違いを、SE 側の再生中判定の精緻化と、MTE の停止・シーク経路からの SE 後始末呼び出しで解消した。

- **`MaidMotionState.IsPlaying` を「実際に動いているか」に精緻化**: タイムラインの一時停止は `AnimationState.enabled` のまま `speed=0` にする方式で、`Animation.isPlaying` は true のままになる。再生中クリップの `speed > 0` を見ることで、タイムライン停止後に SE が「再生中」と誤認してポーズ編集(ボーンスライダー・停止ボタン)を塞ぐ症状を解消。SE 自身の停止(`anim.Stop()`)の判定は従来どおり
- **`CaptureBasePose` の呼び直しを MTE 経路へ追加**: `MaidCache.isAnmPlaying` の停止側(speed=0)と、`motionSliderRate` の停止・一時停止中シークの後で、SE のボーンスライダー基準を取り直す(SE の `StopMotion` / `SetPlaybackTime` と同じ後始末)。`CaptureBasePose` は毎フレーム呼び出し前提の設計(既存辞書を使い回す)で、シークドラッグ中の連続呼び出しも問題ない
- **`MaidCache` の `AnimationState` 直書き(速度・時刻・enabled)自体は撤去しない**: タイムライン再生機構の中核で、SE の `MaidMotionState` はゲーム側モーション(クリップ名ベース)の停止・再開・リセットを担う別レイヤー。共有する状態は「今動いているか」と「ボーンスライダーの基準」だけで、そこだけを接続した
- **停止記録(`_resetClipNames`)とタイムライン再生の相互作用は現状維持**: MTE は再生前に `SceneEditorHack.isAnmEnabled`(委譲済み)経由で SE の停止を解除して再生する既存経路があり、タイムラインの anm 差し替え(`PlayAnm`)が SE の記録を勝手に消すことはしない
- 新規ロジックは `Animation`/`Maid` 依存のため単体テストは追加していない(A-2 と同じ判断)

### D-1 の実装メモ(脱衣マスクの書き込み経路統合)

`TBody` マスクへの書き込みを SE の `MaidUndressController` に一本化した。

- **`SetSlotMask`(スロット単位)を SE 側に新設**: 脱衣ウィンドウ(カテゴリ単位の `SetUndressed`)もタイムラインの脱衣レイヤー(`DressUtils.SetMask` 経由のスロット単位)も、`MaskMode`(Nude 等)を個別制御へ解除する `EnsureIndividualMaskMode` を必ず通ってから `body.SetMask` する形に合流した。MTE 経由で `MaskMode.None` が保証されない問題はこれで解消
- **調査時の「奪い合い」認識の訂正**: `MaidUndressController` はステートレスで真実は `TBody` のマスクそのもの。厳密な状態二重化ではなく、欠けていたのは MTE 経路の `MaskMode` 解除だけだった。カテゴリ(`UndressCategory`)⇔スロット(`DressSlotID`)の値表現は「UI の集計単位」の違いで、書き込みの最小単位(スロット)が一致しているため対応表は不要
- **片側のみの要素は現状維持**: SE の衣装タイプ(めくれ等は `MaidCostumeChangeController` 経由で別経路)、MTE のずらし/めくれキー化(`DressUtils` の Shift 系。`mekureController` 経由で同じゲーム API に乗る)
- 新規ロジックは `TBody` 依存のため単体テストは追加していない(A-2 と同じ判断)

### D-2 の実装メモ(指ブレンドの書き手一本化)

タイムラインの指ブレンドキーの適用・記録を、ゲームの `FingerBlend.BaseFinger`(リフレクション経由)から SE の `MaidFingerBlendController` へ差し替えた。指ボーンの書き手と値表現は SE の一本になった。

- **値表現は元々同型だった**: どちらも「開き/握り + 指ごとのロック(固定 open/fist 対)」で、`TimelineXml` のキー(`LockValueN` の Vector2 = open/fist)は SE の `lockOpen/lockFist` にそのまま対応する。**保存データのスキーマ変更なし**
- **適用**: `TransformDataFingerBlend.ApplyUnit` が SE ユニットへロック・値を書き `Apply()`(テンプレート補間でボーン直書き)。旧経路のような「値だけ書いて適用はゲーム任せ(1 フレーム遅れ)」が無くなった。`timeline.fingerBlendEnabled` の gate は適用側の早期 return へ移動(旧実装はゲーム側 `enabled_` を折っていた)
- **記録**: `UpdateFromUnit` が SE ユニットの現在値を読む。SE のスライダーとタイムラインキーが同じ値を指す
- **適用対象は編集対象メイドのみ**: SE コントローラはアクティブメイドのユニットしか持たない。旧実装も `maidManager.ikManager`(現在のメイド)にしか適用していなかったため挙動は同等(むしろ別スロットのキーを現在のメイドへ適用しうる取り違えが直った)
- **削除・残置**: `MotionTimelineLayer` の `GetBaseFinger` 系 3 メソッドを撤去。`MTEUtils/Extensions` の `FingerBlend` 向けリフレクション拡張(`SetValueOpenOnly` 等)は呼び出し元が無くなったが、MTEUtils は逐語コピー方針のため残置
- **ゲームの IKManager 生成(`PoseEditWindow.GetMaidIKManager`)は残る**: `MaidCache.ikManager` は IK ドラッグ等で現役。指ブレンドだけが SE 経路へ移った
- 新規ロジックはボーン/ゲーム型依存のため単体テストは追加していない(A-2 と同じ判断)

### D-6 の判断メモ(メインカメラ・現状維持で確定)

調査の結果、**状態の二重化は存在しない**と確認し、現状維持で確定した。

- `CameraWindow` はステートレスで、`CameraMain.GetTargetPos/SetTargetPos` 等を読み書きするだけ。`CameraMain` は内部で `UltimateOrbitCamera` へ委譲しており、`CameraTimelineLayer` の `UltimateOrbitCamera` 直叩きと**同じ実体**へ到達する
- ロールも両者とも同じ `Camera` transform(`SetRotationZ` 拡張)を書き、SE 側にロールの持ち分は無い
- つまり「2 つの API 表面が 1 つのゲーム状態を指す」だけで、A-1〜D-5 のような影の状態・上書き経路は無い。奪い合いは「再生中にカメラウィンドウを操作すると再生値に負ける」だが、これはタイムラインの再生が値を駆動する以上避けられない仕様で、他レイヤー(移動等)と同じ振る舞い

### D-7 の実装メモ(視線の向け先/注視先の概念統合)

書き込み経路は A-1 で一本化済みだったため、残っていたのは概念重複のみ。2026-08-30 に
**統合列挙 + UI 1 行(保存形式は据え置き)** の方針で統合した。実装は次の 5 点。

1. **統合列挙**: `MaidLookMode` の末尾へ `メイド` を追加し、「どこを見るか」の語彙を 1 本にした
2. **双方向マップ**: `MaidLookBridge` の `ToLookMode` / `ToTargetType` / `GetSelectableModes` が
   統合列挙と `LookAtTargetType` を相互に写す。キー化中に選べない値(無し/マウス/オブジェクト)は
   表示時に「方向指定」へ丸め、キー化を戻せば SE 側の設定が残る
3. **UI 1 行**: キー化 ON/OFF のどちらでもラベルは「向け先」で、対象がメイドなら
   「メイド」「ポイント」の行が続く(`MaidFaceWindow.DrawLookTargetRows` /
   `TimelineLookRowDrawer.DrawLookAtTargetRows`)。行が入れ替わらなくなった
4. **ポイント解決の一本化**: メイドの注視ポイントは
   `MaidLookController.GetMaidPointTransform` に集約し、`MaidCache.GetPointTransform` は委譲だけを行う
5. **プリセット v27**: 向け先「メイド」の部位を `ScenePresetLook.maidPointType` へ記録する
   (対象メイドは既存の `targetMaidGuid` を使い回す)。履歴(`PoseSnapshot`)も対象・部位を持つ

**統合前の状態**(着手時のコード確認結果)

- 列挙が 2 本: `MaidLookMode`(カメラ/マウス/方向指定/オブジェクト/無し) と `LookAtTargetType`(None/Camera/Maid/Model)
- キー化で行が入れ替わる: OFF は「向け先」コンボ、ON は「注視先 + メイド + ポイント」の 3 行(`MaidFaceWindow.DrawLookTargetRows` → `TimelineLookRowDrawer.DrawLookAtTargetRows`)
- 片側にしか無い値: SE = マウス・任意 Transform(`SelectionManager` 選択)、MTE = メイドのポイント指定・モデル

**本質的な制約**: 任意 Transform はキーへ保存できず、マウスは再生に再現性が無い。よって「完全に 1 列挙」にはできず、**キー化可否で選択肢が変わる統合列挙**にした。

#### 決定した方針

1. **統合列挙**: `MaidLookMode` に `メイド` を末尾追加(既存プリセット互換のため末尾)。統合後の対応は下表。

   | 統合列挙 | キー化 OFF の実体 | キー化 ON の実体(`LookAtTargetType`) | ON の選択肢 |
   |---|---|---|---|
   | カメラ | `MaidLookController` | `Camera` | あり |
   | メイド(+ポイント) | 同上(新規) | `Maid` + `targetIndex` + `maidPointType` | あり |
   | 方向指定 | 同上(顔向き `lookX/Y`) | `None` + `lookDirection` | あり |
   | 無し | 同上 | `None`(そらし時に自動フォールバック。現行どおり) | なし |
   | マウス | 同上 | — | なし |
   | オブジェクト(任意 Transform) | 同上 | — | なし |

   「無し」をキー化 ON の選択肢に出さないのは、`None` が「方向指定」と衝突して往復で意味が変わるため。
   そらし時のフォールバックは `ResolveLookMode` が自動で行う(現行どおり)。

2. **格納先は変えない**: キー化 ON の値は従来どおり `MaidCache.lookAtTarget*` に入り、統合列挙は UI と `MaidLookBridge` の共通語彙としてだけ使う。`TransformDataLookAtTarget` は無変更で、**タイムラインの保存互換は完全維持**。
3. **メイドポイント解決を SE へ一本化**: `MaidCache.GetPointTransform`(`trsHead` / `Spine1a` / `Pelvis` / `Hip_R` / `trBip`)は `TBody` だけで解決でき MTE 依存が無いため SE 側ヘルパへ移し、`MaidCache` は委譲する(D-2 と同じ形)。`ScenePresetLook` が既に注視対象を「メイド guid + ボーン名」で持つため、SE 側のメイド注視は既存表現の昇格になる。
4. **UI は「向け先」コンボ 1 本**: 現行の「注視先」「メイド」「ポイント」3 行を畳む。選択肢だけをキー化状態で切り替え(ON = カメラ/メイド/方向指定、OFF = ＋マウス/オブジェクト/無し)、追随行(メイド+ポイント / 注視対象 / 顔向きスライダー)は共通。**キー化を切っても同じ行・同じ語彙のまま**になり、書き込み先だけが `MaidLookController` ↔ `MaidCache` で変わる。
5. **キー化不可の値の丸め**: キー化 ON へ切り替えた時点で SE 側がマウス/オブジェクトなら「方向指定」として表示する(`LookAtTargetType.None` 相当)。OFF に戻せば SE の設定はそのまま復帰(現行挙動を維持)。
6. **モデル注視は除外を継続**: `StudioModelManager` 未接続のため選択肢に出さない(C 分類の解消が前提)。統合列挙の `オブジェクト` はキー化 OFF 専用なので `Model` と衝突しない。
7. **プリセット v27**: SE 側の `メイド` モードを保存するため `ScenePresetLook` に `maidPointType` 属性を追加する。対象メイドは既存の `targetMaidGuid` を再利用。`timeline*` 属性(v26)は格納先を変えないため据え置き。旧プリセットは `mode` が「メイド」でない限り新属性を読まないので互換影響なし。

**変更ファイル**: `MaidManipulation/MaidLookController.cs` / `Timeline/MaidLookBridge.cs` / `MaidFaceWindow.cs` / `TimelineLookRowDrawer.cs` / `Timeline/MaidCache.cs` / `ScenePresetData.cs` / `Manager/ScenePresetManager.cs`

**実装計画**: `docs/superpowers/plans/2026-08-30-d7-look-target-unification.md`

**テスト**: 双方向マップ(統合列挙 ⇔ `LookAtTargetType`)は純粋ロジックのため `MaidLookBridgeTests.cs` へ追加。プリセット v27 の往復は `ScenePresetLookXmlTests.cs` へ追加。UI とボーン解決は実機確認項目で担保する。

### 実機確認項目(loop 中に追記)

- D-7: 視線タブでキー化 ON/OFF を切り替えても「向け先」行のラベルと語彙が変わらず、行数が飛ばないこと
- D-7: キー化 OFF で向け先「メイド」を選び、対象メイドと部位(顔/胸/股/尻/中心)を切り替えると視線が追従すること
- D-7: キー化 ON で向け先「メイド」を選んだときの見た目が、OFF で同じメイド・同じ部位を選んだときと一致すること
- D-7: キー化 OFF でマウス/オブジェクトを選んだ状態でキー化 ON にすると「方向指定」表示になり、OFF に戻すと元の選択が残っていること
- D-7: 向け先「メイド」で対象メイドを退去させると方向指定の注視点へ落ち、例外が出ないこと
- D-7: 向け先「メイド」のままシーンプリセットを保存 → ロードで対象メイドと部位が戻ること。v26 以前のプリセットをロードしても視線まわりで例外・値の飛びが出ないこと
- D-7: 向け先「メイド」で対象・部位を変えた直後に Ctrl+Z すると、変更前の対象・部位へ戻ること
- D-7: 瞳レイヤーの項目表示(EyesItemInspector)の注視先行が視線タブと同じ「向け先」語彙になっていること

- D-2: ポーズ編集中に指ブレンドキーを打ったフレームへシークすると、SE の指ウィンドウのスライダー・ロック表示がキーの値へ追従すること
- D-2: SE の指ウィンドウで開き/握り・ロックを操作してキーを打つと、その値がキーに記録され再シークで再現されること(旧タイムラインのキーも同じ見た目で再生されること)
- D-2: 「TL:ブレンド有効」を OFF にするとタイムラインの指キーが適用されなくなり、SE のスライダー操作は従来どおり効くこと
- D-2: 足指のキー(3 本ロック)も手指(5 本)と同様に適用・記録できること

- D-1: MaskMode が Nude 系の状態(スタジオ等から引き継いだ場合)で脱衣レイヤーのキーを打つ/再生すると、個別制御へ解除されてスロット表示が指定どおりになること。このとき操作対象外のスロットが一旦表示へ戻るのは公式(`SetMaskMode(None)`)と同じ仕様であること
- D-1: 脱衣ウィンドウのカテゴリトグルと脱衣レイヤーのスロットトグルを交互に操作しても、互いの表示状態が正しく追従すること
- D-1: めくれ/ずらしのキー化と脱衣ウィンドウのめくれ系チェックが従来どおり動くこと

- D-4: タイムラインを再生→一時停止した直後、SE のポーズウィンドウの再生ボタンが「■(再生中)」のままにならず、ボーンスライダーが操作できること。スライダー操作の起点が停止時のポーズになっている(ポーズが飛ばない)こと
- D-4: タイムライン停止中にフレームをシークした後、ボーンスライダーを操作してもシーク前のポーズへ飛ばないこと
- D-4: SE 側のモーション停止・再生・リセット(ポーズウィンドウ)が従来どおり動くこと

- D-5: メイドを非表示にしたままメイド移動レイヤー入りタイムラインを再生しても、メイドが画面に現れないこと。再表示すると再生位置(タイムラインの最新の位置)に出ること
- D-5: 非表示中にキーを打っても退避座標 `(100,0,0)` ではなく見かけの位置が記録されること
- D-5: 表示中の再生・ギズモ編集からのキー記録が従来どおり動くこと

- D-3: 表情レイヤーがあるタイムラインの再生中、表情ウィンドウの「強制上書き」トグルが編集不可になり、ポーズ編集モードへ入る(または表情レイヤーを削除する)と復帰すること
- D-3: 「強制上書き」OFF(=まばたき有効)の状態でタイムラインを再生→停止・ポーズ編集へ戻すと、まばたきが再開すること(従来は false のまま潰れていた)
- D-3: 再生中にシーンプリセット/履歴(Ctrl+Z)で表情を復元しても例外なく動き、抑止解除後にまばたき設定が復元値と一致すること

- A-1 追補: 旧タイムライン(EyesRot キー入り)を読み込むと「顔向き」行として再生され、キーの左右/上下の向きが従来の瞳の向きと一致すること(**符号が逆なら `EyesTimelineLayer.ApplyEyes` / `GetEyesValue` で反転を入れる**)
- A-1 追補: キー化 ON・注視先「なし」で顔向きスライダーを動かすと頭と瞳が同じ方向を向き、キーとして記録・再生されること
- A-1 追補: キー化 ON・注視先「なし」・目線種別「そらす」系で視線そらしが動くこと(顔向きキーは効かなくなるのが仕様)
- A-1 追補: キー化 OFF に戻すと SE の向け先・顔向きが保存時のまま残っていること
- A-1 追補: eyeAngle* 付きで保存したシーンプリセット(撤去前の v26)を読んでも例外が出ず、顔向きキーが 0 として復元されること

- A-1a: 表情ウィンドウの向け先「無し」を選ぶと正面(頭ボーンの `offsetLookTarget`)を向くこと
- A-1a: タイムライン設定「顔/瞳の固定化」を ON にして注視先(カメラ/メイド)を切り替えると、表情ウィンドウの「向け先」表示が追従すること
- A-1a: 「メイド目線」を「顔をそらす」「目だけそらす」にし、かつ表情ウィンドウの向け先を「無し」にしたとき、実際に視線そらしが動くこと(向け先が「無し」以外ならそらしは動かないのが仕様)
- A-1a: タイムライン再生(`PlayAnm`)の後も、SE 側で設定した向け先(マウス/方向指定/オブジェクト)が維持されること(固定化が無効の場合)
- B-3: タイムラインを編集して Ctrl+Z / 履歴ウィンドウで元に戻せること、カーブエディタの操作も履歴に乗ること
- B-3: `SceneEditor.xml` の `historyLimit` を 0 にすると、タイムライン操作も履歴に積まれなくなること
- B-2: タイムライン設定から「色をHSVで指定」が消えていること、カラーピッカーの RGB/HSV 切り替えボタンは従来どおり効き、`SceneEditor.xml` に保存されること
- B-1: `SceneEditor.xml` の `keyRepeatTime` / `keyRepeatTimeFirst` を変更すると、タイムラインのフレーム送り(←→ キー長押し)のリピート間隔にも反映されること
- B-1: 既存の `Timeline.xml` を読んでも他の設定が既定へ戻らないこと
- A-5: CRC 顔でジト目(`eyeclose3`)をスライダー最大にしたときの見た目が、タイムラインで同じ値をキーにしたときと一致すること
- A-5: 既存のマイ表情プリセット / シーンプリセットを読み込んで、表情が保存時と同じであること(特にジト目)
- A-5: タレ目・ツリ目の顔で目閉じ・ウィンクが従来どおり効くこと(サフィックス解決の変更点)
- A-4: 編集モードに入るとボーンが表示され、抜けると消えること
- A-4: 編集モード中にタイムラインのレイヤーを切り替えても、ボーン表示トグルが勝手に変わらないこと(手動で OFF にしたなら OFF のまま)
- A-4: タイムライン設定から「常にIKを表示」が消えていること、既存の `Timeline.xml` を読んでも他の設定が既定へ戻らないこと
- A-3: タイムライン設定から「自動揺れボーン」が消えていること、既存の `Timeline.xml` を読んでも他の設定が既定へ戻らないこと
- A-3: タイムラインでキーを打っても、Inspector で切ったスロット揺れの設定が変化しないこと
- A-2: Inspector で胸の揺れを止めた後にタイムラインを再生しても、揺れが復活しないこと
- A-2: タイムライン設定の「胸(左/右)の物理無効」を切り替えると、呼出済み全メイドの Inspector トグルが追従すること
- A-2: 「物理無効」が有効なタイムラインを読み込むと、その時点の SE 側トグルが一括で上書きされること(全体設定として意図した挙動)
- A-1c: 「視線をキー化」ON で注視先・瞳回転を設定 → シーンプリセット保存 → ロードで指定値が戻ること
- A-1c: v25 以前の既存プリセットをロードしても、視線まわりで例外・値の飛びが出ないこと
- A-1b: 視線タブで「視線をキー化」を切り替えると、同じ描画のうちに「向け先」コンボと顔向きスライダーの活性が切り替わること
- A-1b: キー化 ON のとき「注視先」を変えると向け先が追従し、OFF に戻すと SE の向け先設定がそのまま残ること
- A-1b: 瞳レイヤーの項目表示(`EyesItemInspector`)の案内文が新しい設定場所(視線タブ)を指していること
- A-1a: `UpdateHeadLook` のたびに「顔を向ける」「目を向ける」トグルが `eyeMoveType` 由来の値へ揃うこと(従来の `EyeToCamera` と同じ挙動だが、A-1b で UI を統合する際の前提になる)

## 参照

- UI 配置の軸の調査: `docs/layer-window-duplication-survey.md`
- アダプタ化の実例: `Timeline/Hack/SceneEditorHack.cs`、`Timeline/Manager/StudioLightManager.cs`
- 状態の実体: `Timeline/MaidCache.cs`、`MaidManipulation/*Controller.cs`
