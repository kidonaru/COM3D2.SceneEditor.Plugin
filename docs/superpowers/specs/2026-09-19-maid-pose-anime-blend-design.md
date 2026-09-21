# モーションウィンドウのアニメブレンド対応 設計メモ

## 目的

モーションウィンドウ (`MaidPoseWindow`) から、ベースモーション (レイヤー 0) の上に
別のモーション / マイポーズをアニメレイヤー 2〜8 へ重ねて再生 (ブレンド) できるようにする。
ModItemExplorer のモーションウィンドウ「拡張」表示と同じ操作感を、SceneEditor の
編集モデル (停止編集・履歴・タイムライン連携) に合わせて提供する。

## 参考にした ModItemExplorer の仕組み

- 適用先レイヤーを選んでからアイテムをクリックすると `TBody.CrossFadeLayer(..., layer, ..., weight)` で
  そのレイヤーへ載せる (`ModItemManager.ApplyAnmItem`)
- レイヤーごとの状態は MTE の `MaidCache.animationLayerInfos` (`AnimationLayerInfo`) を共有し、
  毎フレーム `Animation` の実 `AnimationState` から `anmName` / `state` を同期する (`UpdateAnimationLayerInfos`)
- 行 UI はレイヤーごとに「再生時間スライダー + ▶/■」「重み」「速度」「削除」

SceneEditor では MTE 相当のコードが同一アセンブリにあるため、リフレクション無しで
`MTEP.MaidManager.instance.GetMaidCache(maid).animationLayerInfos` を直接使う。

## 設計

### 状態の置き場

- 真の状態は `MaidCache.animationLayerInfos` (レイヤー 2〜8)。タイムラインの
  `AnimationTimelineLayer` が同じリストを読む (`UpdateFrame`) ため、ウィンドウで編集した値が
  そのままキー登録の元になる
- `MaidCache` は呼出中メイド全員ぶん作られる (`MaidManager.maidCaches`) ので、タイムライン未読込でも使える
- ベースアニメが変わると `MaidCache.OnAnmChanged` → `ResetAnm` で全 `AnimationLayerInfo` が
  `Reset()` されるが、`Animation` 上の `AnimationState` は残る。ModItemExplorer と同じく
  毎フレームの同期 (`SyncFromAnimation`) で `state.layer > 0 && enabled` な状態を info へ拾い直す

### 新規クラス `MaidAnimationBlendController` (static, `MaidManipulation/`)

`MaidMotionState` と同じ static スタイル。責務:

| 操作 | 内容 |
|---|---|
| `SyncFromAnimation(maid)` | 実 `AnimationState` から info を同期。info が指すクリップが消えていれば `Reset()` |
| `ApplyMotion(maid, layer, PhotoMotionData)` / `ApplyMyPose(maid, layer, relativePath)` | `anmName` を決めて `MaidCache.LoadAnimationLayer` でロード。ベース停止中なら `speed=0` でサンプル、再生中なら `speed=info.speed` で流す |
| `RemoveLayer(maid, layer)` | `TBody.StopAndDestroy(anmTag)` → `info.Reset()` → 停止中は再サンプル |
| `SetTime / SetWeight / SetSpeed / SetLoop / Play / Stop (layer)` | info と state を同時に更新。停止中は `Animation.Sample()` で即反映 |
| `EnableStatesForSample(maid)` / `DisableStatesAfterSample(maid)` | `MaidMotionState` の停止サンプル時にブレンド層も乗せるためのフック |
| `CaptureTimesBeforeStop(maid)` | `Animation.Stop()` は全 state を巻き戻すため、直前の再生位置を `info.startTime` に控える |
| `ResumeAfterPlay(maid)` | ベース再生再開後にブレンド層の state を `enabled / weight / speed` で流し直す |
| `Capture(maid)` / `Restore(maid, list)` | 履歴 (`PoseSnapshot`) 用のスナップショット |

`anmName` の決め方 (タイムラインの `MaidCache.ApplyAnimationLayerInfo` が再ロードできる名前に揃える):

| 元 | anmName | ロード経路 |
|---|---|---|
| 公式モーション (`!is_mod`) | `direct_file` (新ボディ男は `crc_` 前置。`PhotoMotionData.Apply` と同じ) | `CrossFadeLayer(name, GameUty.FileSystem)` |
| Mod モーション (`is_mod`) | `direct_file` (絶対パス) | `CrossFadeLayerByFullPath` (絶対パス fallback を `MaidCache` に追加) |
| マイポーズ | 保存フォルダからの相対パス + `.anm` | `CrossFadeLayerByFullPath(PhotoModePoseSave.folder_path + name)` (既存経路) |
| スクリプト経由 (`call_script_fil` のみ) | 不可。ダイアログで案内 | - |

### `MaidCache` の変更 (タイムライン側)

- `ApplyAnimationLayerInfo` のロード部分を `public AnimationState LoadAnimationLayer(AnimationLayerInfo info)` に切り出し、
  ウィンドウとタイムラインで共用する
- 絶対パスの `anmName` (`File.Exists(anmName)`) を `CrossFadeLayerByFullPath` で読めるようにする (Mod モーション対応)
- 再ロード判定 `info.state.name != info.anmTag` を `Path.GetFileName(anmTag)` との比較にする。
  `CrossFadeLayerByFullPath` はファイル名を state 名にするため、サブフォルダ入りマイポーズや
  絶対パスでは毎フレーム再ロードしてしまう (既存の不具合。名前解決は pure な `AnimationLayerNameUtils.GetStateTag` に切り出してテストする)

### `MaidMotionState` の変更

停止編集モデルとブレンド層の両立:

- `StopMotion`: `anim.Stop()` の前に `CaptureTimesBeforeStop`、`SampleWhileStopped` で層を乗せる
- `SampleWhileStopped` / `SetPlaybackTime` / `ResetPoseWhileStopped`: `Sample()` の前後で
  `EnableStatesForSample` / `DisableStatesAfterSample` を呼ぶ (ベース state と同じ扱い)
- `PlayMotion` / `PlayClip` / `ResetPose` (再生中分岐): `anim.Play(...)` の後に `ResumeAfterPlay`

### 履歴

`PoseSnapshot` に `List<MaidAnimationBlendController.LayerState>` を追加し、
`Capture` / `Apply(RestoreMotion の直後)` / `Approximately` で扱う。ウィンドウの各操作は
`HistoryScope.Pose` で `BeforeEdit` を積む (再生位置スライダーと同じ)。
`LayerState` は `layer / anmName / time / weight / speed / loop / playing` を持つ pure クラスで、
`Approximately` はテストで固定する。

### UI (`MaidPoseWindow`)

再生行の下に「アニメブレンド」トグル行 (`config.maidPoseBlendVisible`)。ON のとき:

```
[適用先 ▾ 通常 / レイヤー2 … レイヤー8]
── レイヤー2: xxx.anm ───────── [▶] [削除]
再生時間 [=====o=====] 1.23
重み [==o==] 0.50   速度 [===o=] 1.00  [x]ループ
── (レイヤー3 以降は anmName があるものだけ。適用先が空なら「(未設定)」行)
```

- 一覧 (モーション / マイポーズ) のクリックは、適用先が「通常」なら従来経路、
  レイヤー 2〜8 なら `MaidAnimationBlendController.ApplyMotion / ApplyMyPose`
- ブレンド区間は `TimelineLayerGate` を `AnimationTimelineLayer` で張り直す
  (`End` → `Begin(AnimationTimelineLayer)` → 描画 → `End` → `Begin(MotionTimelineLayer)`)。
  値変更は `BeginAutoEditMode` / `EndAutoEditMode` で囲み、タイムライン読込中はキー登録の対象になる
- 適用先の選択はランタイムのみ (設定に保存しない)

### スコープ外

- シーンプリセットへのブレンド層の保存 / 復元 (`docs/scene-preset-timeline-coverage-gap.md` と同じ扱いで別途)
- 一覧ボタンのハイライトはベース (レイヤー 0) のみ
- ModItemExplorer 側との状態共有 (向こうは MTE 本体の `MaidCache` を見る)

## テスト方針

Unity ネイティブ呼び出しは不可 (`tests-no-unity-native-calls`)。pure なロジックだけを切り出して xunit で固定する:

- `AnimationLayerNameUtils.GetStateTag` (anmTag → state 名)
- `AnimationBlendNameResolver.Resolve` (公式 / Mod / マイポーズ / スクリプトの anmName 決定)
- `MaidAnimationBlendController.LayerState.Approximately`
- `MaidPoseBlendRows.GetVisibleLayers` (表示するレイヤー番号の決定)

実機確認は COM3D2.5 で devbridge を使い、停止編集 → ブレンド追加 → 再生 → リセット → undo の順で行う。
