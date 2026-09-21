# アニメブレンド: メイド単位の状態化と編集ポーズの anm 化

> 実装後にレビュー・実機検証で見つかった問題への対応と、追加で決めた仕様は
> `2026-09-22-anime-blend-followup.md` にまとめてある。

2026-09-21 策定。`2026-09-20-anime-blend-window-redesign.md` 以降のコミット
(65f3019 〜 3c68cf5) で「モーションウィンドウ内の `適用先` タブ」「レイヤー選択中は停止しても層を残す」
へ変わった現行実装を前提に、次の問題と要望を解決する。

## 背景: 現行実装の問題

| 問題 | 原因 |
|---|---|
| メイド切替で前のメイドの層とベースが有効 (速度 0) のまま取り残される | `isBlendLayerSelected` が静的フラグ 1 つで、メイドを持たない。切替後の解除は新しいメイドに向く |
| ウィンドウ非表示時の解除が別のメイドへ向くことがある | 同上。`MaidManipulateManager.Update` は `activeMaid` へ解除を送る |
| 取り残されたメイドのポーズ保存・タイムラインのキーにブレンドが焼き込まれる | 上 2 つの帰結 |
| `時間上書き` がタイムライン未使用時にも操作できる | 値はタイムライン再生でしか効かない |
| タイムライン側の層解除が履歴に残らない | `AnimationTimelineLayer.OnPoseEditEnd` が `BeforeEdit` を呼ばない |
| 停止中のブレンドはレイヤータブでしか見えない | 「ベースタブ = ベースのみ」の設計 |

## 決定事項

ユーザーとの合意 (2026-09-21):

1. 状態はメイド単位。メイド A のレイヤー選択は B へ切り替えても保持し、ロックも A だけに効く
2. `ポーズ保存` はブレンド込み (マージ後) で良い
3. シーンプリセットはベースのみのポーズを保存し、ブレンド層は別項目で保存して復元時に再現する
4. `時間上書き` はタイムラインが無いときは操作不可
5. タイムライン側の層解除も履歴に残す (シーン側と同じ形)
6. 停止中の層はベースタブでも見せる。編集モード中だけベースのみ
7. 層は破棄しない。手編集は編集モードを抜けるときに anm 化してベースへ差し替える (`▶` / Space / メニュー OFF すべて)

## 仕様

### 1. メイド単位の状態

`MaidAnimationBlendController` に `Dictionary<Maid, BlendEditState>` を持たせる。

```csharp
sealed class BlendEditState
{
    public int selectedLayer = MaidPoseBlendRows.BaseLayer; // 適用先タブ
    public bool hasBoneEdit;                                 // この編集セッションでボーンを書いたか
}
```

- 静的 `isBlendLayerSelected` を廃止し `IsLayerSelected(Maid)` に置き換える。
  参照箇所 (`MaidManipulateManager.isBoneEditing` / `isDragPointActive`、各ドラッグ点の `BeginDrag`、
  `MaidBoneSliderController.SetOffsetAxis`、`InspectorWindow`、`MaidIKWindow`、`MaidWindowBase`) は
  対象メイドを渡して判定する。`MaidManipulateManager` の `isBoneEditing` / `isDragPointActive` は
  `activeMaid` 基準のまま、`SceneView` で他メイドの白丸を作るかは各メイドの状態で決める
- `_blendTargetLayer` / `_blendTargetMaid` は `MaidPoseWindow` から `BlendEditState.selectedLayer` へ移す。
  タブのクリック時に `SetSelectedLayer(maid, layer)` を呼び、描画同期 (`SetBlendLayerSelected` 毎フレーム呼び) は廃止する
- ウィンドウを閉じても選択は保持する。3c68cf5 の「見えていない間は解除」は削除する
  (固着ではなく意図した状態。ゲート文言 `BlendLayerGateMessage` が案内する)
- 状態を捨てるタイミング: メイドの退避・削除 (`calledMaids` から外れる)、ボディ再ロード。
  `SyncFromAnimation` が層を空へ戻すときは `selectedLayer` も `BaseLayer` へ戻す
  (空の段を選び続ける意味が無い)
- エディット系をベースに当てたときの `BaseLayer` 強制も `BlendEditState` に対して行う

### 2. 停止中の層の見せ方

停止中の層の有効状態は次の 1 本のルールで決める。

```
層を有効 (速度 0) のまま残す  ⇔  IsLayerSelected(maid) || !isEditMode
```

- 層を残す間はベースも有効 / 速度 0 で残す (2648482 の方式。層だけ有効だと Unity の自動サンプルが
  ベース抜きで走る)。`RestoreBaseAfterSample` / `SampleStopped` の `isBlendLayerSelected` 参照をこのルールへ差し替える
- 編集モードに入る (`MaidManipulateManager.isEditMode` setter, value=true) とき: ベースタブのメイドは
  層を一時無効化してベースのみでサンプルする。`info.anmName` / 重み等は保持する (破棄しない)
- 編集モードを抜ける (value=false) とき: 3 節の anm 化を先に行い、そのあと層を有効へ戻して再サンプルする
- 編集モード中にレイヤータブへ切り替えるとき: 層を有効へ戻す前に 3 節の anm 化を行う
  (戻すと再サンプルで手編集が消えるため)
- `ReleaseForBoneEdit` (ドラッグ点・ボーンスライダーでの層破棄) は廃止する。
  代わりに同じトリガーで `hasBoneEdit = true` を立てる:
  - `MaidDragBoneTracker.BeginDrag` (ボーン回転 / 顔 / 指 / IK)
  - `MaidBoneSliderController.SetOffsetAxis`
  - `反転`
- `hasBoneEdit` を落とす: anm 化したとき、ベースを差し替えたとき (一覧クリック / マイポーズ読込 / プリセット復元 / `PlayMotion`)
- `ReleaseAll` は残す (`アニメブレンド` チェック OFF の一括解除で使う)

タイムライン読込中で対象メイドにモーションレイヤーがあるときは、この節の編集モード連動は行わない。
タイムライン側は `isMotionEditing` のときだけ `OnPoseEditEnd` で層を外す MTE 準拠の動きを保つ (6 節)。

### 3. 編集ポーズの anm 化

`hasBoneEdit` が立っているメイドは、次のタイミングで現在ポーズを anm 化してベースへ差し替える。

- 編集モードを抜けるとき (`▶` / Space / メニューの編集モード OFF)
- 編集モード中にレイヤータブへ切り替えるとき

処理はシーンプリセット復元と同じ経路:

1. `HistoryManager.BeforeEdit(maid, HistoryScope.Pose, ...)` — 直前の編集に確定待ちがあればそこへマージされる
2. `MaidPoseFileManager.CapturePoseBinary(maid)` (層は無効化済みなのでベース + 手編集だけが乗る)
3. `MaidPoseFileManager.ApplyPoseBinary(maid, binary, startPlaying: false)`
4. `MaidMotionState.SetAppliedMotion(maid, new AppliedMotionInfo { displayName = "編集ポーズ" })`
   (motionId 0 / myPosePath null。どのボタンもハイライトしない)
5. `MaidPoseFileManager.MarkPoseAsResetTarget(maid)` (`リセット` はこの anm へ戻る)
6. `hasBoneEdit = false`

結果:

- `再生中` の表示名は `編集ポーズ`。`<` `>` は送り先が無いので無効 (プリセット復元と同じ)
- `▶` は編集ポーズ (静止) を再生し、層だけが動く。元のモーションへ戻すには一覧から選び直す
- `▶` で抜ける経路は `AutoEditMode.Exit()` → `PlayMotion()` の順なので、`Exit` 内の anm 化が先に走り
  `PlayMotion` は `POSE_CLIP_TAG` を再生する
- 対象メイドにタイムラインのモーションレイヤーがあるときは行わない (タイムラインがキーから anm を作る)

履歴:

- `PoseSnapshot` はクリップ名 + 適用記録 + ボーン Transform を戻す。元のモーションクリップは
  `Animation` に残るので `PlayClip` で戻せる
- anm 化の前のベースが常駐ポーズクリップ (マイポーズ / プリセット / 以前の編集ポーズ) だった場合、
  `POSE_CLIP_TAG` の 1 枠が上書きされ中身が戻らない。`MaidPoseFileManager` に「最後に適用したバイナリ」を
  メイドごとに控え、`PoseSnapshot` がクリップ名 `POSE_CLIP_TAG` のときはそのバイナリも控えて
  `Restore` で `ApplyPoseBinary` し直す

### 4. シーンプリセット

`ScenePresetMaid` に追加 (`CurrentVersion` 34 → 35):

```csharp
public List<ScenePresetAnimationLayer> animationLayers; // 載っている層だけ

public class ScenePresetAnimationLayer
{
    [XmlAttribute] public int layer;        // 2〜8
    [XmlAttribute] public string anmName;   // AnimationLayerInfo.anmName (タイムライン XML と同じ。Mod は絶対パス)
    [XmlAttribute] public float startTime;
    [XmlAttribute] public float weight;
    [XmlAttribute] public float speed;
    [XmlAttribute] public bool loop;
}
```

保存 (`CaptureMaid`):

- 停止中: 層を一時無効化してベースのみでサンプルし `poseAnmBinary` を取り、層を戻して再サンプルする
- 再生中: 従来どおり `motion` を記録 (ポーズは固めない)
- 層は `HasAnyLayer` のとき `animationLayers` に記録

復元 (`ApplyMaid`):

- ポーズ / モーション適用後に各層を `MaidCache.LoadAnimationLayer` で読み直し、値を反映する
- 停止中 (`poseAnmBinary` 復元): 層を有効 / 速度 0 で残す (2 節のルールで編集モード外なら見える)
- 再生中 (`motion` 復元): `ResumeAfterPlay` 相当で層も流す
- 読み込めない `anmName` (Mod の絶対パスが無い等) は警告ログを出してその段だけ飛ばす
- 適用先タブは保存しない

### 5. `時間上書き`

`MaidPoseBlendRows` の `時間上書き` トグルは、`TimelineLayerGate` の判定が Ready でない
(タイムライン未読込または対象メイドのアニメブレンドレイヤーが無い) とき操作不可にする。
値は表示する。

### 6. タイムライン側の解除を履歴に残す

`AnimationTimelineLayer.OnPoseEditEnd` で `isMotionEditing` により層を外すとき、
実際に外す層 (`info.state != null && info.state.enabled && info.layer > 0`) があれば、
外す前に `HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose, "ブレンド解除", () => PoseSnapshot.GetAllBodyBones(maid))` を呼ぶ。

- 確定待ちの編集があればそこへマージされ、ラベルは元の操作名のまま (シーン側の従来挙動と同じ)
- Ctrl+Z で層とポーズが戻る。モーションレイヤーで編集を続けると次のフレーム移動でまた解除される (許容)
- `OnPoseEditEnd` はフレーム移動・レイヤー切替・ポーズ更新でも呼ばれるため、
  層が既に外れているときは何も積まない

## 検証項目 (実機)

- メイド A でレイヤーを選び停止 → B へ切替 → A のポーズにブレンドが残り、B はベースのみ。A へ戻すと同じタブ
- ベースタブ・編集モード外・停止中にブレンドが見える。編集モードに入るとベースのみになる
- 手編集 → `▶`: 編集ポーズが静止したまま層だけ動く。Ctrl+Z で元のモーションと手編集前のポーズへ戻る
- マイポーズをベースに読込 → 手編集 → `▶` → Ctrl+Z でマイポーズのクリップへ戻る (常駐枠の復元)
- 編集モード外で層を残している間、IK 固定が LateUpdate でボーンを書いてポーズがブレないか
  (`SceneEditorHack.isAnmEnabled` のコメントにある衝突。ブレるなら層を残す間は IK 固定の書き込みを止める)
- シーンプリセット保存 → 復元でブレンド込みの見た目が戻り、`poseAnmBinary` にはブレンドが乗っていない
- タイムライン読込中にモーションレイヤーで編集 → 層が外れる → Ctrl+Z で戻る

## ドキュメント

- `docs-site/guide/maid-editing.md` アニメブレンド節: 停止中の見え方、編集ポーズ、メイド単位の選択保持、
  `時間上書き` の無効化、ボーン編集との関係 (破棄しない) を書き直す
- `docs-site/guide/scene-preset.md`: ブレンド層の保存を追記
- `docs-site/timeline/layers-maid.md`: 解除が履歴に残ることを追記
- `docs/superpowers/specs/2026-09-20-anime-blend-window-redesign.md`: 冒頭に本メモで置き換えた旨を追記

## スコープ外

- ブレンド結果をベースへ焼き込んで 1 本の anm にする
- 層のクロスフェード時間
- 適用先タブのプリセット保存
