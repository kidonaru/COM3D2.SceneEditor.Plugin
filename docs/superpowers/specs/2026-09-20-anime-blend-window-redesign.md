# アニメブレンド 再設計メモ (MTE 準拠)

> **2026-09-21: このメモの「別ウィンドウ化」「ピッカーモード」「停止中はブレンドを見せない」は
> 65f3019 / 67481b7 で撤回され、`2026-09-21-anime-blend-per-maid-bake-design.md` に置き換えられた。**

2026-09-19 に実装したアニメブレンド（`2026-09-19-maid-pose-anime-blend.md`）が
MTE (`W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin`) の前提と矛盾していたため、
MTE 側の実装を正として作り直す。

## MTE の実装（確認済みの事実）

### 1. サブレイヤー無効化の条件

```csharp
// TimelineManager.UpdateMotionEditing()
if (studioHackManager.isPoseEditing)
{
    if (currentLayer.layerType == typeof(MotionTimelineLayer) ||
        currentLayer.layerType == typeof(MoveTimelineLayer))
    {
        isMotionEditing = true;
        return;
    }
}
isMotionEditing = false;
```

条件は「編集モード中」ではなく **「ポーズ編集中 かつ アクティブレイヤーがモーション／移動レイヤー」**。
アクティブレイヤーがアニメブレンドレイヤーなら `isMotionEditing` は false になり、
ブレンドは生きたままになる。これが「ブレンド編集中はブレンドが見える／
モーション編集中はブレンドが死ぬ」を両立させている仕組み。

`AnimationTimelineLayer.OnPoseEditEnd()` は `isMotionEditing` のとき
`state.enabled = false` に加えて **`state = null`** まで落とし、ベースのみで再サンプルする。
`info.anmName` は残るため、タイムライン再生時に読み直せる。

### 2. MTE のブレンド UI

`AnimationTimelineLayer.DrawWindow` — レイヤー専用ペイン。

| 項目 | MTE | 2026-09-19 版の SceneEditor |
|---|---|---|
| anmName の指定 | 生のテキストフィールド | 一覧クリック + 適用先コンボ |
| ループ | あり | あり |
| **時間上書き** (`overrideTime`) | **あり** | **無し（未実装）** |
| 開始時間 | あり | 「再生時間」として同等 |
| 重み / 速度 | あり | あり |
| 層ごとの ▶ / ■ | 無し | あり |
| 削除 | 無し | あり |
| 操作可否 | `isPoseEditing` のときのみ | TimelineLayerGate |

MTE はアニメブレンドを **タイムライン再生の道具** として設計しており、
ポーズ編集の道具にはしていない。

### 3. 移植の差分

SceneEditor の `TimelineManager.IsMotionEditingState` は `MoveTimelineLayer` を条件から落としている
（`MoveTimelineLayer.cs` は存在する）。MTE と揃える。

## 2026-09-19 版が壊していた前提

### 汚染（本質的な問題）

SceneEditor の編集結果はボーンの Transform に直接乗る。サンプル後の Transform は
「ベース寄与 + ブレンド寄与 + 手編集」が混ざった 1 つの値になり、後から分離できない。

| 読み取り側 | 汚染の中身 |
|---|---|
| `MaidBoneSliderController.CaptureBasePose` | ブレンド込みが編集の基準になる |
| `PoseSnapshot.GetAllBodyBones` | 履歴にブレンド込みのポーズが載る |
| ポーズ保存（マイポーズ） | ブレンドが焼き込まれた `.anm` になる |
| `MotionTimelineLayer` のキー打ち | **二重適用**（キーにブレンド分が入り、再生時に層も乗る） |

ポーズ保存の焼き込みは用途によっては望ましいが、キーの二重適用は事故。

### 具体的な矛盾箇所

- `MaidMotionState.SampleWhileStopped` / `SetPlaybackTime` が停止中もブレンド層を乗せてサンプルする
  （`OnPoseEditEnd` の思想の真逆）
- `EnableStatesForSample` / `DisableStatesAfterSample` という「停止中もブレンドを見せる」ための
  フックが不要になる（`CaptureTimesBeforeStop` / `ResumeAfterPlay` は
  `anim.Stop()` への対処なので D の通り残す）
- `適用先` コンボが一覧クリックの意味を変えるモードになっており、消し忘れ事故を防ぐために
  メイド切替リセットを後付けした
- `MaidPoseWindow` 内で `TimelineLayerGate` を張り直しており、モーションレイヤー未登録時に
  注意ラベルが 2 回出る

## 新しい仕様

### 方針

アニメブレンドは **タイムライン再生の道具**（MTE と同じ位置づけ）。
ポーズ編集とは排他にし、ボーンを触った瞬間に解除する。

### A. 別ウィンドウ化

新規ウィンドウ `アニメブレンド`（`MaidAnimationBlendWindow`）。

- メニューバー `メイド` → `アニメブレンド` で開閉
- レイヤー 2〜8 のうち「載っている層」＋「空いている先頭の層 1 つ」を行として描く
  （`MaidPoseBlendRows.GetVisibleLayers` の判定を、適用先ではなく空き行 1 つを出す形へ変える）
- 行の項目: `選択` / `▶`■ / `削除` / `開始時間` / `重み` / `速度` / `ループ` / **`時間上書き`**
- ウィンドウ全体を `TimelineLayerGate.Begin(typeof(AnimationTimelineLayer))` で 1 回だけ囲む
  （モーションウィンドウ内での張り直しが不要になり、注意ラベルの二重表示も消える）

`時間上書き` は MTE にあって SceneEditor に無かった項目。`AnimationLayerInfo.overrideTime` は
既に存在し `TransformDataAnimation` でシリアライズされるため、UI から触れないと
タイムラインの挙動が MTE と食い違う。

### B. モーション選択はモーションウィンドウをピッカーとして使う

一覧描画（カテゴリ / 検索 / マイポーズのフォルダ移動 / ハイライト）を複製しないため、
既存の `MaidPoseWindow` をピッカーモードで再利用する。

1. ブレンドウィンドウの行の `選択` を押す
2. `MaidPoseWindow` がピッカーモードに入り、ウィンドウを表示・前面化する
3. モーションウィンドウのヘッダーに `▶ レイヤーN へ載せる [キャンセル]` が出る
4. 一覧のエントリを押すと、そのレイヤーへ載せてピッカーモードを抜ける
5. `キャンセル` / 対象メイドの変更 / ウィンドウを閉じる でピッカーモードを抜ける

ピッカーモード中は前後送り (`<` `>`)、`ポーズ保存`、`反転`、`リセット` を無効化する
（ベースを触る操作とピッカーが混ざらないようにする）。

`適用先` コンボは廃止する。一覧クリックの意味を恒常的に変えるモードを持たせない。

### C. 無効化のルール

#### C-1. タイムライン利用時（MTE 準拠）

`IsMotionEditingState` に `MoveTimelineLayer` を足して MTE と揃える。
`AnimationTimelineLayer.OnPoseEditEnd` の既存挙動はそのまま使う（移植済み）。

#### C-2. タイムライン未使用時（SceneEditor 独自）

ボーンを実際に書く操作が始まった時点で、そのメイドのブレンド層を **解除** する。

- トリガー:
  - ドラッグ点の掴み（`MaidDragBoneTracker.BeginDrag`。ボーン回転 / 顔 / 指 / IK の 4 種が集約される）
  - ボーンスライダーの変更（`MaidBoneSliderController.SetOffsetAxis`）
- 解除の内容: クリップごと破棄し、`AnimationLayerInfo` を `Reset()` する
- 解除の直前に `HistoryScope.Pose` で `BeforeEdit` を呼ぶ（誤って触っても Ctrl+Z で戻せるようにする）。
  ただし `HistoryManager.BeforeEditCore` は確定待ちがあると `description` を上書きしないため、
  履歴に出るラベルは元の操作名（「ボーン回転: …」等）のまま。
  ここでの `BeforeEdit` はラベル表示ではなくスナップショットを取らせるのが目的で、
  `PoseSnapshot.Capture` がブレンド層を含むため Ctrl+Z では層が戻る
- 解除は一方向。自動で再開はしない（MTE も再開しない）

トリガーに `MaidManipulateManager.isEditMode` を使ってはいけない。
`AutoEditMode.Enter` が全てのパラメータ変更で走るため、
ブレンドの重みを動かすだけで自分自身を解除してしまう。

### D. 停止中サンプルからブレンド層を外す

外すのは **停止中にブレンド層を乗せてサンプルする経路だけ**:

- `MaidMotionState.SampleWhileStopped` の `EnableStatesForSample` / `DisableStatesAfterSample` 呼び出し
  （`maid` 引数も不要になるので元のシグネチャへ戻す）
- `MaidMotionState.SetPlaybackTime` の同フック
- `MaidAnimationBlendController.EnableStatesForSample` / `DisableStatesAfterSample` 本体

**`CaptureTimesBeforeStop` と `ResumeAfterPlay` は残す。**
この 2 つは「`anim.Stop()` が全 `AnimationState` を無効化して巻き戻す」ことへの対処であり、
停止中サンプルとは責務が別。消すと `MaidMotionState.StopMotion` を呼ぶ 18 箇所
（指ウィンドウ / IK 固定 / マイポーズ読込 / 編集モード開始 / タイムラインのモーションレイヤーなど、
C-2 の解除トリガーに含まれない経路を多数含む）のいずれかを通っただけで、
`anmName` は残ったまま無効化されたブレンド層が再生を再開しても戻らなくなる。

停止中にブレンド層を乗せてサンプルしないため、`ApplyAnmName` / `RemoveLayer` /
`SetTime` / `SetWeight` の `SampleStopped` 呼び出しも「ベースのみで再サンプル」に変える。
結果として **ベース停止中はブレンドの見た目が出ない**（再生を再開すると混ざる）。
ウィンドウには停止中に「ベース再生中に反映されます」の注記を出す。

## 引き継ぐもの（2026-09-19 版から生きる部分）

- `AnimationBlendNameResolver`（名前解決、テスト込み）
- `MaidCache.LoadAnimationLayer` の切り出しと再ロード判定の修正
- `MaidAnimationBlendController` の中核（`SyncFromAnimation` / `ApplyMotion` / `ApplyMyPose` /
  `RemoveLayer` / 値操作 / `ReleaseDuplicatedLayers` / 履歴の `Capture` / `Restore`）
- `PoseSnapshot` への層の載せ込み
- `MaidPoseBlendRows.GetVisibleLayers`（テスト込み）

## スコープ外

- シーンプリセットへのブレンド層の保存
- ブレンド結果のベースへの焼き込み（ポーズ保存で代用できる）
- ブレンド層のクロスフェード時間の指定（MTE も持っていない）
