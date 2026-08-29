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
| A-1 | **視線・注視先** | `MaidLookController`（向け先モード / 顔向き XY / 注視オブジェクト）＋ `MaidFaceWindow` の `boHeadToCam` / `boEyeToCam` トグル（:276-288） | `MaidCache.lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `eyeEulerAngle`、`UpdateHeadLook`（MaidCache.cs:501）が毎回 `maid.EyeToCamera(timeline.eyeMoveType)` と `LockHeadAndEye` を実行 | 同じ `body0.trsLookTarget` を奪い合う。表情ウィンドウ視線タブに「視線」と「タイムライン視線」の 2 セクションが並ぶ（MaidFaceWindow.cs:330-335 に既知として明記）。さらに `TimelineSettingWindow` の「メイド目線」（`eyeMoveType`, :253）と「顔/瞳の固定化」（`useHeadKey`, :258）が 3 箇所目の入口になっている |
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

B-4(BGM 2 箇所)と B-5(永続化 2 系統)は現状維持で確定。B-4 は音源が別で機能が異なり、B-5 は静的プリセット vs アニメーションの役割分担が妥当なため、本 loop では扱わない。

### A-1a の実装メモ

`Timeline/MaidLookBridge.cs` を新設し、`trsLookTarget` の書き手を SE の `MaidLookController` に一本化した。

- **MTE 側に残した状態**: `lookAtTargetType` / `lookAtTargetIndex` / `lookAtMaidPointType` / `eyeEulerAngle` は `MaidCache` に残す。これらは `TimelineXml` と `EyesTimelineLayer` が読み書きするキーフレームの指定値であり、ゲーム状態そのものではないため。移したのは「向け先という**ゲーム状態の所有**」だけで、MTE は `MaidLookBridge` 経由で SE のコントローラを駆動する側に降りた
- **`MaidLookMode.無し` を追加**: MTE の `LookAtTargetType.None`(向け先なし)に対応する状態が SE 側に無かったため。`trsLookTarget = null` は視線そらしと瞳回転が効く唯一の条件でもある。既存プリセット互換のため列挙の末尾へ追加した
- **`Maid.EyeToCamera` の呼び出しを廃止**: 目線種別のフラグ設定と同時に `trsLookTarget` をカメラへ書き換えてしまうため。`MaidLookBridge.ApplyEyeMoveType` がフラグ(`boHeadToCam` / `boEyeToCam` / `boEyeSorashi` / `HeadToCamFadeSpeed`)だけを同じ組み合わせで再現する
- **「顔/瞳の固定化」が無効なら SE の向け先を変更しない**: タイムラインが向け先を持たない状態なので、SE 側の設定を正とする。目線種別(「メイド目線」)は向け先の判断材料にしない
- **視線そらしは向け先「無し」のときだけ効く**: `TBody` はそらし演出を `trsLookTarget == null` かつ `boLockHeadAndEye == false` のときだけ動かす。従来の `EyeToCamera` は「そらす」指定で無条件に向け先を null にしていたが、それは SE の設定を一方向に壊して戻せない(戻す経路が無い)ため踏襲しない。代わりに `UpdateLookAtTarget` の `LockHeadAndEye` と `UpdateEyeEulerAngle` がそらし指定時に手を引き、SE で向け先を「無し」にすればそらしが動くようにした
- **`MaidCache.useHeadKey` プロパティを削除**: `trsLookTarget` を直接読み書きする所有権違反で、呼び出し元も無かった(`TimelineData.useHeadKey` とは別物)

### A-1b の実装メモ

表情ウィンドウ視線タブの「視線」「タイムライン視線」の 2 セクションを 1 セクションへ統合し、`TimelineSettingWindow` の「メイド目線」(`eyeMoveType`)と「顔/瞳の固定化」(`useHeadKey`)も同タブへ移した。

- **「顔/瞳の固定化」は UI 上「視線をキー化」へ改称**: A-1a で向け先の所有者が SE に一本化されたため、このトグルの意味は「タイムラインが視線を持つか」に絞られた
- **キー化中は SE の「向け先」コンボを無効化する**: 向け先はタイムラインの「注視先」行が駆動するため、2 つの入口を並べず操作を譲る。キー化していないときは従来どおり SE の向け先・顔向き・注視対象を操作する
- **`TimelineSettingWindow` からは 2 行を削除**: 未使用になった目線種別コンボも撤去した
- **`TimelineLookRowDrawer.HeadKeyDisabledMessage` の文言を更新**: 設定の所在が変わったため、`EyesItemInspector` の案内も新しい場所を指す
- A-1b は UI の再配置のみで純粋ロジックの追加が無いため、単体テストは追加していない(判定に使う `ResolveLookMode` は A-1a で網羅済み)

### A-1c の実装メモ (B-6 の解消)

確認の結果、向け先そのもの(`mode` / `lookX` / `lookY` / 注視対象 / `boHeadToCam` / `boEyeToCam`)は既存スキーマで往復できていたが、`MaidCache` に残したキーフレームの指定値が欠落していたため `ScenePresetLook` へ追加した(プリセット v26)。

- **追加した項目**: `timelineTargetType` / `timelineTargetIndex` / `timelineMaidPointType` / `eyeAngleX/Y/Z`
- **旧プリセット互換**: `timelineTargetType` が空なら未記録として `MaidCache` へ触らない
- **番号で持つ理由**: `timelineTargetIndex` は `TimelineXml` のキー(`TransformDataLookAtTarget.targetIndex`)と同じ相対番号。並びが変わると別の対象を指すが、既存のデータモデルの慣習に合わせた
- **復元順**: 3 つのセッターがいずれも `UpdateLookAtTarget` を呼ぶため、番号・ポイントを先に入れて種別を最後にする
- **既知の前提**: プリセットは「視線をキー化」(`useHeadKey`)を保存しない。保存時と復元時でこの設定が違うと、指定値の復元が `mode` の復元を上書きしうる。所在をコードのコメントに明記した
- **B-6 の扱い**: 上記により解消。B 表の B-6 は本項目で閉じる

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

- **仕様変更**: 編集モードに入っても、ボーン表示が自動で ON にならなくなった。SE の「ボーン表示」トグルが唯一の入口になるため、OFF のまま編集モードへ入るとボーンは出ない(意図した変更。以前は選択中のレイヤー種別でトグルが勝手に落ちる副作用の方が大きかった)
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

### 実機確認項目(loop 中に追記)

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
- A-4: 編集モードを何度か出入りしても、SE の「ボーン表示」トグルが勝手に落ちないこと
- A-4: 編集モードに入ってもボーンが自動表示されないこと自体は仕様。手動でトグルを ON にすれば従来どおり編集できること
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
