# タイムライン リリース前 負債対応 設計（優先度 中）

作成日: 2026-09-13
前提: `docs/timeline-release-debt-review.md` の優先度 中 9〜18 項目を検討した結果。優先度 高の設計は `2026-09-13-timeline-release-debt-design.md`。XML version は 34 のまま変えない。

## 決定一覧

| # | 項目 | 決定 | 実装 |
|---|---|---|---|
| 9 | `登録` の対象が表示中レイヤー依存 | 手動 `登録` も編集対象レイヤー全部を対象にする。カメラ系はアクティブ時のみ | あり |
| 10 | `カメラ同期` が登録可否に絡む | 9 の規則で条件が消える。同期トグルは再生反映のみ | あり（9 に含む） |
| 11 | ライブ演出が履歴非対応 | `BeforeEdit` を入れて Undo と自動登録を有効にする | あり |
| 12 | Undo の穴 | トラック操作・`最終フレーム`・読込直後の基準を直す。シーン操作が載らない件は仕様書に明記のみ | あり（一部文書） |
| 13 | 0 フレームキー必須 | 読込時に 0 フレームへ補修し、必須レイヤーでは 0 フレームキーの削除・移動を禁止 | あり |
| 14 | レイヤー削除時の実体の扱いが不統一 | 「削除は実体を壊さない」に統一。サブカメラの破棄をやめる | あり |
| 15 | メイド切替時の暗黙レイヤー作成 | 優先度 高の項目 6 で対応済み | なし |
| 16 | メイド目線の所有が二重 | 構造は維持し、プリセット側は「写し」と仕様書に明記 | 文書のみ |
| 17 | 補間のばらつき | テキスト・サブカメラ VP・背景色・地面色・PNG の SZ を直す。背景 Transform はスナップのまま | あり |
| 18 | 回転カーブが表示専用 | Euler 表示でのタンジェント編集を許可（値編集は不可のまま） | あり |

## 9 / 10. 登録対象の一本化

### 現状
- `TimelineManager.AddKeyFrameDiff(isAuto)`（`TimelineManager.cs:2121-2190`）は `isAuto ? editTargetLayers : BuildManualKeyFrameLayers()` で対象を切り替える。手動側は `TimelineLayerViewFilter.Filter` でグリッド表示と同じ絞り込み（カテゴリモード / レイヤーモード）をかける
- 差分登録なので、対象を広げても値の変わっていないレイヤーにキーは増えない
- カメラ系は `ShouldSkipCameraKeyFrame`（`:2113-2115`）で `isAuto || (!layer.isCurrent && !config.isCameraSync)` のとき除外。ポストエフェクトには登録側のガードが無い

### 変更
- `BuildManualKeyFrameLayers` と `TimelineLayerViewFilter` の登録用途を廃止し、手動・自動とも `editTargetLayers` を対象にする（`TimelineLayerViewFilter` はグリッド表示用として残す）
- `ShouldSkipCameraKeyFrame` を `isAuto || !layer.isCurrent` にする。カメラ系は「アクティブレイヤーのときだけ `登録` で記録」。`isCameraSync` は参照しない
- 状態メッセージ `[Enter]キーでキーフレームを登録します` はそのまま

### 仕様（文書）
- `editing.md` の「登録の対象範囲」を次に置き換える
  > `登録` は、操作対象メイドのレイヤーとメイドに紐づかないレイヤーのうち、編集開始時から値が変わったものすべてに打たれます。表示モードやカテゴリには依存しません。カメラとサブカメラだけは、そのレイヤーがアクティブなときにしか登録されません
- `control.md` / `layers-camera.md` の「`カメラ同期` OFF だと `登録` でもキーが打たれない」を削除。`カメラ同期` は再生反映のみの説明にする

### テスト
- メイドカテゴリ表示中にライトを動かして `登録` → ライトレイヤーにキーが増える
- カメラレイヤー非アクティブでカメラを動かして `登録` → カメラにキーは増えない。アクティブなら増える（`isCameraSync` の値に依らない）

## 11. ライブ演出の履歴対応

### 現状
- `LiveEffectWindow.cs` と `StageLightRowDrawer` / `StageLaserRowDrawer` / `PsylliumRowDrawer` は `HistoryManager.BeforeEdit` を一度も呼ばない。値変更は `AutoEditMode.Enter()` だけで直接書き込む
- 他ウィンドウは `BeforeEdit(maid, scope, description)` または `BeforeEdit(maid, scope, description, targetKey, capture)` を呼び、`CommitPending` がマウス解放時に確定して `onEditCommitted` を発火。`TimelineWindow.OnEditCommitted` がそれを受けて自動登録する
- スナップショットは `PresetDtoSnapshot<T>`（`Manager/History/PresetDtoSnapshot.cs`）が「マネージャの状態を DTO で丸ごと持つ」基底を提供している（`SoundSnapshot` / `SubCameraSnapshot` / `TextSnapshot` が利用）。ライブ演出用の DTO はシーンプリセットにも無い

### 変更
- `HistoryScope` に `LiveEffect` を追加
- `Manager/History/LiveEffectSnapshot.cs` を新設。`PresetDtoSnapshot<LiveEffectState>` を継承し、`LiveEffectState` はステージライト・レーザー・サイリウムの各マネージャが持つコントローラーと個別要素の全パラメータを保持する DTO。キャプチャは各データクラスの既存 `CopyFrom` を使い、適用は個数を合わせてから各要素へ `CopyFrom` する
- `LiveEffectWindow` の値変更・追加・削除・コピーの全経路（`DrawStageLightControllEdit` / `DrawStageLightEdit` / `DrawStageLaserControllEdit` / `DrawStageLaserEdit` / `DrawPsyllium*Edit` / `DrawTargetTabs` の追加削除コールバック）と 3 つの行ドロワーで、値を書く直前に `HistoryManager.instance.BeforeEdit(null, HistoryScope.LiveEffect, "ライブ演出: <項目>", targetKey, LiveEffectSnapshot.Capture)` を呼ぶ。`targetKey` はウィンドウ単位で固定でよい（3 マネージャをまとめて 1 スナップショットにするため）
- 上記により自動登録は既存経路（`onEditCommitted` → `TryAutoKeyFrame`）で自動的に効く

### 仕様（文書）
- `editing.md` / `compatibility.md` / `layers-effect.md` の「ライブ演出は操作履歴に対応していないため自動登録されない」を削除

### テスト
- ステージライトの色を変えて `Ctrl+Z` → 元の色に戻る
- 自動登録 ON でサイリウムの Seed を変える → 現在フレームにキーが増える

## 12. Undo の穴

### 現状
- `TimelineSettingWindow.cs` のトラックタブ: `AddTrack`（`TimelineManager.cs:1248`）、名前変更、範囲変更（`onChanged` で直接代入）、`MoveUpTrack` / `MoveDownTrack`、`SetActiveTrack` に `RequestHistory` が無い。`RemoveTrack` だけ記録している
- `最終フレーム`: `SetMaxFrameNo`（`TimelineManager.cs:925-931`）に `RequestHistory` が無い
- 読込直後: `TimelineHistoryManager.AddHistory` は `lastCommittedXml`（基準）が null のとき積まずに基準だけ更新する。`ResetTimelineState` が基準を消すため、新規作成・読込後の最初の操作は基準作りに消費される
- シーン操作: `HistoryManager.isTimelineMode` が true の間、`CommitPending` は `AddEntry` せず `onEditCommitted` だけ発火する（2 モード構造）

### 変更
- `AddTrack` / `SetActiveTrack` / `MoveUpTrack` / `MoveDownTrack` に `RequestHistory` を追加。名前変更と範囲変更は `TimelineSettingWindow` 側の `onChanged` の後で `RequestHistory("トラック変更")` を呼ぶ（`RequestHistory` はマウス解放時にまとめて確定するため、ドラッグ中の連続変更は 1 件になる）
- `SetMaxFrameNo` に `RequestHistory("最終フレーム変更")` を追加
- `CreateNewTimeline` と `LoadTimeline` の末尾で `historyManager.SetBaseline(timeline)` を呼び、基準 XML を先に取る。`RequestHistory("タイムライン新規作成")` は不要になるので削除
- シーン操作の件は変更しない。`editing.md` の操作履歴節に「タイムライン読込中はシーンの操作（メイド追加など）は履歴に載らない。各ウィンドウの値変更は自動登録でキーになるためタイムライン履歴で戻せる」と明記

### テスト
- トラック追加 → `Ctrl+Z` で消える。範囲ドラッグ → 1 回の `Ctrl+Z` で戻る
- 新規作成直後にキーを 1 つ打って `Ctrl+Z` → キーが消える

## 13. 0 フレームキー必須の扱い

### 現状
- `TimelineLayerBase.Init`（`:213-219`）は `firstFrame == null` のとき 0 フレームへキーを作る。新規レイヤーは必ず 0 フレームキーを持つ
- `Motion` / `Eyes` / `Morph` / `Undress` の 4 レイヤーが `IsValidData` で `firstFrame.frameNo != 0` を検証し、失敗すると `IsValidData` が false になり保存・出力に加えてタイムラインの更新全体が止まる（`TimelineIntegration.UpdateGuards`）
- エラーに到達するのは、利用者が 0 フレームのキーを削除・移動した場合と、読み込んだ XML の先頭キーが 0 フレームでない場合

### 変更
- `ITimelineLayer` に `bool requiresFirstFrame`（既定 false）を追加し、4 レイヤーで true にする
- `TimelineLayerBase.Init` の補修条件を `requiresFirstFrame` のレイヤーでは `firstFrame == null || firstFrame.frameNo != 0` にする。0 フレームキーが無い XML を読んだときは現在値で 0 フレームキーを作る（ログに補修した旨を出す）
- キーの削除・移動・範囲削除・範囲挿入で、`requiresFirstFrame` のレイヤーの 0 フレームキーは対象から外す。削除しようとしたときは状態メッセージに `0フレーム目のキーは削除できません` を出す
- `IsValidData` の検証は安全網として残す

### 仕様（文書）
- `layers.md` の「0 フレーム目のキー」を「4 レイヤーは 0 フレーム目のキーを常に持ち、削除・移動できない。0 フレーム目にキーが無いタイムラインを読み込むと現在値で自動補修される」に書き換え。`files.md` の保存エラー表からも該当行を落とす

### テスト
- 0 フレームキーだけの XML から frameNo を 5 に書き換えて読む → 0 フレームにキーが補修される
- `Motion` の 0 フレームキーを選択して Backspace → 消えずメッセージが出る

## 14. レイヤー削除は実体を壊さない

### 現状
- `SubCameraTimelineLayer.Dispose`（`:46-55`）だけが `subCameraManager.DestroyAllCameras()` を呼ぶ。他の全レイヤーはイベント解除のみ

### 変更
- `SubCameraTimelineLayer.Dispose` から `DestroyAllCameras()` を外す。サブカメラの削除は Camera ウィンドウの `サブカメラ数` で行う
- `layers.md` / `layers-camera.md` の「レイヤーを削除するとサブカメラの実体も破棄されます」を「レイヤー削除はシーン上の実体を変えません（全レイヤー共通）」に置き換える

## 16. メイド目線の所有（文書のみ）

- `TimelineData.eyeMoveType` が本体で、タイムライン XML に保存される。シーンプリセットの `look.eyeMoveType` はタイムライン読込中に限って保存される写しで、適用時に `TimelineData` へ書き戻す（`ScenePresetManager.cs:1223-1229, 1791-1811`）
- `files.md` の「メイド目線と注視先の設定だけがシーンプリセットに保存されます」を「タイムライン読込中にプリセットを保存すると、メイド目線と注視先はタイムラインの値の写しとして保存され、適用時にタイムラインへ戻されます」に書き換える

## 17. 補間の統一

### 現状と変更
| レイヤー | 現状 | 変更 |
|---|---|---|
| `テキスト` | `TransformDataText` は `hasTangent` でタンジェントを保存しているが、`ApplyMotionUpdate` は位置・角度・拡縮を `Vector3.Lerp` | 位置・角度・拡縮を `HermiteVector3` に変更。色は線形のまま |
| `サブカメラ` | `ViewportX/Y/W/H` にタンジェントがあるのに `Mathf.Lerp` | 4 値を `HermiteValue` に変更 |
| `背景色` / `地面色` | `hasTangent` 無し。`indexUpdated` 時のみ開始値を適用 | `TransformDataBGColor` / `BGGroundColor` に `hasTangent => true`、`tangentValues => valuesWithoutColors` を追加。色は `Color.Lerp`、地面の位置・広さは `HermiteVector3`。`表示` は開始値 |
| `PNG配置` の `SZ` | `HermiteValue` に掛かっているが `tangentValues` に含まれない | `tangentValues` に `ScaleZ` を追加 |
| `背景` | `hasTangent` 無し、切替・Transform ともスナップ | 変更しない |

- `interpolation.md` / `layers-other.md` / `layers-camera.md` / `layers-background.md` の補間欄を上表に合わせて更新。`背景色` の「なめらかに変えたいときはキーを細かく打ってください」を削除

### 互換
- XML 形式は不変。`背景色` / `地面色` の既存キーはタンジェント未保存なので自動補間（`isSmooth`）として読まれ、これまでのスナップから補間へ挙動が変わる。段差を意図していた旧データは `1フレーム調整` で再現できる旨を `compatibility.md` に追記

### テスト
- テキストの位置キー 2 つに異なるタンジェントを与え、中間フレームの値が `HermiteVector3` と一致する
- 背景色を赤→青で 2 キー打ち、中間フレームで中間色になる

## 18. 回転カーブのタンジェント編集

### 現状
- `TimelineCurveEditor` はクォータニオン格納の回転（`hasRotation`）を `EulerDisplay` チャンネルに置き換えて描画し、値ドラッグ・タンジェント編集の両方を拒否している（`:483, :739, :781`）。理由は Euler 表示値から 4 成分への逆変換が無いこと
- `TransformDataRotation` は 4 成分それぞれにタンジェントを持ち、`UpdateTangent` も成分ごとに計算する

### 変更
- `EulerDisplay` チャンネルに元の 4 成分 `ValueData` への参照を持たせ、タンジェント操作（ハンドルドラッグ、`In` / `Out` 数値欄、プリセット適用、`自動補間` トグル）を **4 成分すべてへ同じ正規化値で反映**する。表示カーブはその後 `RefreshEulerKeyValues` で再計算する
- 値ドラッグは引き続き不可。`:781` のガードは残し、理由コメントを「値の逆変換が無いため」に限定する
- `interpolation.md` の「これは表示専用で、値もタンジェントも編集できません」を「値は編集できません。タンジェントは編集でき、回転の 4 成分すべてに同じ形で反映されます」に書き換える

### テスト
- `メイド移動` の回転キーで Euler 表示のハンドルを動かす → `rotationValues[0..3]` の該当側タンジェントが同じ正規化値になる

## 実装順

1. 項目 12（履歴の穴。小さく独立）
2. 項目 13（0 フレーム補修と禁止）
3. 項目 14（サブカメラ破棄の撤去）
4. 項目 9 / 10（登録対象の一本化）
5. 項目 17（補間統一）
6. 項目 18（回転タンジェント）
7. 項目 11（ライブ演出履歴。DTO 新設で最も大きい）
8. 文書更新（9〜18 の記述）
