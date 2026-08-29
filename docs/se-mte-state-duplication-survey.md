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
| A-3 | **スロット揺れボーンの ON/OFF** | `SlotYureUtil.SetYureState` ＋ InspectorWindow の揺れトグル（:507-538）、`BoneEditManager` のボーン掴み時の自動 OFF（:540-542） | `MotionTimelineLayer`（:612-622）が `config.isAutoYureBone` 有効時、anm 構築のたび全揺れスロットの状態を `_extendSlotNames` 基準で総入れ替え | タイムラインでキーを打つたびに Inspector で切った揺れ設定が復活／消滅する。設定は `TimelineSettingWindow`「自動揺れボーン」（:875）にのみ存在 |
| A-4 | **ボーン／IK の表示** | `MaidManipulateManager.isBoneVisible`（BoneEditWindow「ボーン表示」:363、MenuBarWindow:223） | `StudioHackManager.isPoseEditing` の setter（:31）が `isIKVisible = value && canIKVisible` を書き込み、`canIKVisible` は選択中レイヤー種別と `config.alwaysShowIK`（:37）で決まる | 編集モードを切り替えるたび、選択中のタイムラインレイヤーによって SE のボーン表示トグルが勝手に落ちる。`alwaysShowIK` は `TimelineSettingWindow`「常にIKを表示」（:881）にしか無い |
| A-5 | **表情モーフの読み書き** | `MaidFaceMorphController`（TMorph 直接操作。CRC 顔のサフィックス解決を独自実装） | `TimelineFaceManager`（:30-70。`CheckMorph` / `GetRatio` で別実装の名前解決・倍率換算）。`MorphTimelineLayer` が `SetMabatakiOff` + `SetMorphValue` を適用 | 同じ TMorph に対する名前解決・値スケールの実装が 2 本。片方だけ直すと表情ウィンドウとタイムライン再生で値がずれる |

## B. 設定・データが二重管理（整理対象）

| # | 対象 | 実態 |
|---|---|---|
| B-1 | **Config が 2 ファイル** | `Config.cs`（`SceneEditor.xml`）と `Timeline/Config.cs`（`Timeline.xml`）。**フィールド名が重複しているのは 13 個**: `pluginEnabled` / `historyLimit` / `keyRepeatTime` / `keyRepeatTimeFirst` / `useHSVColor` / `windowHoverColor` / `gridCountInWorld` / `gridAlphaInWorld` / `gridLineWidthInWorld` / `gridCellSize` / `gridColorInWorld` / `gridColorInDisplay` / `dirty`。うち grid 系・`windowHoverColor`・`keyRepeat*` の読み手は **すべて SE 側**（`GridRenderer` / `SettingWindow` / `COM3D2.SceneEditor.Plugin.cs`）で、MTE 側の同名フィールドは死んでいる |
| B-2 | **「色をHSVで指定」トグルが無効** | `TimelineSettingWindow`（:891）は `timelineConfig.useHSVColor` を書くが、実際に参照されるのは `GUIView.option.useHSVColor` ←`COM3D2.SceneEditor.Plugin.cs:14-19` 経由の **SE 側** `config.useHSVColor`。タイムライン設定側のトグルは押しても何も起きない |
| B-3 | **Undo/Redo が 2 系統** | SE の `HistoryManager`（Ctrl+Z、`HistoryWindow`、`historyLimit`）と MTE の `TimelineHistoryManager`（TimelineXml のスナップショット、`Timeline/Config.historyLimit`）。**MTE 側は UI に接続されておらず `Undo()` / `Redo()` の呼び出し元が無い** — 履歴を積むだけのメモリ消費になっている。`TimelineSettingWindow`「ポーズ履歴無効」（:865）だけがその存在を露出している |
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
- [ ] A-2: `MaidCache.UpdateMuneYure` を `MaidMuneYureController` 経由へ差し替え、`useMuneKeyL/R` を SE トグルの別名にする(`SceneEditorHack.useMuneKeyL/R` の空実装も解消)
- [ ] A-3: `MotionTimelineLayer` の `isAutoYureBone` 一括上書きをやめ、`SlotYureUtil` の状態を唯一の真実にする(`BoneEditManager` の自動 OFF と同じ経路へ)
- [ ] A-4: `StudioHackManager.isPoseEditing` setter の `canIKVisible` による `isIKVisible` / `isIkBoxVisibleRoot/Body` 上書きを廃し、SE 側のトグルを唯一の入口にする(`alwaysShowIK` の扱いも整理。`isBoneVisible` 自体はアダプタ化済みで対象外)
- [ ] A-5: `TimelineFaceManager` の名前解決・倍率換算を `MaidFaceMorphController` へ一本化する(表情プリセット・モーフ追跡への影響が広いため差分を丁寧に確認する)

B 分類(二重管理の整理):

- [ ] B-1: `Timeline/Config.cs` の読み手が無い重複フィールド(grid 系 / `windowHoverColor` / `keyRepeat*` 等 13 個のうち死んでいるもの)を削除する。あわせて `Timeline.xml` の後方互換(未知フィールドの読み飛ばし)を確認する
- [ ] B-2: `TimelineSettingWindow` の「色をHSVで指定」トグルを SE 側 `config.useHSVColor` へ接続するか、トグル自体を撤去する(SE の設定ウィンドウに同項目があるなら撤去を優先)
- [ ] B-3: `TimelineHistoryManager` を撤去する(`Timeline/IKHoldEntity.cs` 撤去と同じ扱い。`TimelineSettingWindow`「ポーズ履歴無効」も併せて整理)。調査の結果 SE の `HistoryManager` へ統合できる見込みが立つならそちらを優先し、判断理由を本書へ追記する

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

### 実機確認項目(loop 中に追記)

- A-1a: 表情ウィンドウの向け先「無し」を選ぶと正面(頭ボーンの `offsetLookTarget`)を向くこと
- A-1a: タイムライン設定「顔/瞳の固定化」を ON にして注視先(カメラ/メイド)を切り替えると、表情ウィンドウの「向け先」表示が追従すること
- A-1a: 「メイド目線」を「顔をそらす」「目だけそらす」にし、かつ表情ウィンドウの向け先を「無し」にしたとき、実際に視線そらしが動くこと(向け先が「無し」以外ならそらしは動かないのが仕様)
- A-1a: タイムライン再生(`PlayAnm`)の後も、SE 側で設定した向け先(マウス/方向指定/オブジェクト)が維持されること(固定化が無効の場合)
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
