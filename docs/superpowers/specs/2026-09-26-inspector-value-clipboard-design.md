# Inspector のキーフレーム / レイヤー項目コピー＆ペースト 設計

作成日: 2026-09-26

## 1. 目的と確定仕様

Inspector に表示しているタイムラインの値を、**キーフレーム（ボーン）単位**または**レイヤー項目単位**でコピーし、同じパラメータを持つ別の対象へ貼り付けられるようにする。

ブレインストーミングで確定した仕様:

1. 操作はメニューアイコンボタン（☰）から開くポップアップの「コピー」「貼り付け」で行う
2. 対象は次の 2 つの表示
   - **キーフレーム詳細**（KeyFrameInspector）: 個別ブロックと、一括モードの型グループ
   - **レイヤー項目の現在値**（TimelineItemInspector。キーフレーム未選択時にメニュー項目を選択した表示）
3. レイヤー項目への貼り付けは**キーフレームを作らず、シーンの現在値へ直接当てる**（B 案）。Motion レイヤーも含める
4. レイヤー項目のメニューは**選択全体に 1 個 + 項目ごとに 1 個**の両方を置く。表情モーフのように 1 行で完結する項目は、行（スライダー）の右端に置く
5. 通常オブジェクトの Transform（タイムラインと無関係な位置・回転・拡縮）は対象外

## 2. 貼り付けの互換規則

クリップボードは `(名前, ITransformData)` の組を 1 件以上持つ（`ItemValueClipboard`）。貼り付け先 1 件ごとに、次の順で値を決める。

1. **名前一致**: クリップボードに貼り付け先と同じ名前の組があり、TransformType も同じならそれを使う
2. **単一型一致**: クリップボードが 1 件だけで、TransformType が貼り付け先と同じならそれを使う（例: Spine の回転キーを Neck へ、あるモーフの値を別のモーフへ）
3. どちらにも当たらない貼り付け先は変更しない

メニューの「貼り付け」は、選択中の貼り付け先のうち 1 件以上に当たるときだけ有効にする。TransformType が同じなら値の並び（`values` / `strValues`）が同じなので、`FromTransformData` で安全に写せる。

キーフレームとレイヤー項目は同じクリップボードを共有する。キーフレームからコピーして現在値へ貼る、現在値からコピーしてキーフレームへ貼る、のどちらもできる。

クリップボードはプロセス内だけで保持する（`LightClipboard` / `MaterialClipboard` と同じ流儀）。タイムラインの「フレームコピー」はシステムクリップボードに XML を書くので、互いに干渉しない。

## 3. コピー

| 表示 | コピー元 |
|---|---|
| キーフレームブロック | `bone.transform.Clone()` の 1 件 |
| 一括モードの型グループ | 対象外（どのキーを写すか決まらないため「コピー」は無効表示） |
| レイヤー項目（項目ごと） | 現在値の 1 件 |
| レイヤー項目（選択全体） | 選択中の全葉項目の現在値 |

レイヤー項目の現在値は、キーフレーム登録（`AddKeyFrames`）と同じく `layer.CreateFrame(currentFrameNo)` → `layer.UpdateFrame(tmpFrame, force: true)` → `tmpFrame.GetBone(item.name)` で取り出す。葉の `IBoneMenuItem.name` は BoneData の名前と一致し、セット行は `TimelineItemInspectorRegistry.CollectLeafItems` で子へ展開済みなので、名前はずれない。

注意: MotionTimelineLayer の `UpdateFrame` は、編集モード中かつ Move レイヤーが無いとき `maid.transform` を編集開始位置へ戻す（MotionTimelineLayer.cs の `initialEditFrame != null && !HasMoveLayer()` 分岐）。キーフレーム登録と同じ副作用なので許容し、実機で確認する。

## 4. 貼り付け

### 4.1 キーフレーム

`target.transform.FromTransformData(source)` で値・文字列値・タンジェントを丸ごと写す。そのあと `bone.parentLayer.ApplyCurrentFrame(true)` と `timelineManager.RequestHistory("キーフレーム貼り付け: <名前>")` を呼ぶ（既存の「初期化」ボタンと同じ流れ）。一括モードのグループでは、グループ内の全キーへ 2 章の規則で当て、履歴は 1 回にまとめる。

### 4.2 レイヤー項目（シーンへ直接適用）

`TimelineLayerBase` に直接適用の入口を追加する。

```csharp
/// 1 項目の値をキーフレームを介さずシーンへ当てる
public void ApplyTransformDirect(ITransformData transform)
{
    var frameNo = timelineManager.currentFrameNo;
    var motion = new MotionData(transform, transform, frameNo, frameNo);
    ApplyTransformDirectCore(motion);
}

/// 既定は ApplyMotion を t=0, indexUpdated=true, playData=null で 1 回呼ぶ。
/// ApplyMotion だけでは反映しきれないレイヤーが上書きする
protected virtual void ApplyTransformDirectCore(MotionData motion)
{
    ApplyMotion(motion, 0f, true, null);
}

/// 貼り付け不可のレイヤー (イベント型) は false
public virtual bool canApplyTransformDirect => true;
```

調査で確認した前提:

- start == end なので `MotionData.isConstant` が true になり、`LerpScratch` は start をそのまま返す。`PluginUtils.Hermite` も dt == 0 で v0 を返す
- `playData` を参照するのは AnimationTimelineLayer だけで、null ガードがある（null なら `info.startTime`）
- `indexUpdated = true` を渡さないと、BGModel / Light / Move / BG などの Init 系の書き込みが走らない

レイヤー別の対応:

| 区分 | レイヤー | 対応 |
|---|---|---|
| 既定のままで可 | Animation, BG, BGColor, BGModel, Light, Move, BGModelMaterial, MaidMaterial, ModelMaterial, Camera, SubCamera, Undress, Eyes, Gravity, Model, ModelBone, PostEffect 各種, Text, PngPlacement | なし |
| 後処理を足す | ShapeKey | `maidCache.FixBlendValues(keys)` |
| | ModelShapeKey | `model.FixBlendValues()` |
| | Dress | `AllProcPropSeqStart`（ApplyPlayData の `_propUpdated` 後処理と同じ） |
| | StageLaser / StageLight | `controller.UpdateLasers()` / `UpdateLights()` |
| | Psyllium | `controller.ManualUpdate(playingTime)` |
| 個別実装 | Morph | `ApplyMotion` は `_applyMorphMap` / `_isForceOverride` に貯めるだけなので、貯めたあと ApplyPlayData と同じ書き込み（`SetMorphValue` / `UpdateMabatakiOverride`）を対象項目だけ行う |
| | Motion | 下記 |
| 対象外 | Voice, Se | `canApplyTransformDirect = false`。indexUpdated で即再生するだけのイベントなので、「貼り付け」を無効表示にする |

Motion レイヤーの個別実装:

- **体ボーン（`TransformDataRotation` / `TransformDataRoot`）**: anm バイナリで動いていて `ApplyMotion` を通らない。`UpdateFrame` の読み取りと対称に、`maidCache.GetBoneTransform(name)` の `localRotation`（Root は `localPosition` も）へ直接書く。書く前に `MaidMotionState.StopMotion(maid)` を呼ぶ（Inspector のボーン位置行と同じ）。ボーンスライダーは毎フレーム localRotation から基準回転とのオフセットを分解するので、直接書いても表示は追従する
- **ExtendBone**: `ApplyExtendBoneMotion` は位置と拡縮だけを書くので、`localRotation` も書く
- **IKHold / Grounding / FingerBlend**: 既定の `ApplyMotion` を使う。IKHold は `isAnime = false` のとき目標を取り直す仕様なので、そのまま受け入れる

### 4.3 履歴・編集モード・自動キー

貼り付けは `HistoryManager.BeforeEdit(maid, HistoryScope.TimelineItem, "貼り付け: <名前>", targetKey, capture)`（呼び出し側がスナップショットを組み立てるオーバーロード）を通してから適用する。

- `BeforeEditCore` が `AutoEditMode.Enter()` を呼ぶので、編集モードに入って再生データ（ApplyPlayData）による上書きが止まる
- 確定時に `onEditCommitted` が通知されるので、自動キーフレーム登録（`TryAutoKeyFrame`）の対象になる
- タイムラインモード中はシーン操作が履歴に積まれない（既存仕様）。Undo は自動キー登録で積まれるタイムライン履歴に従う

スナップショットは新規の `TimelineItemSnapshot : IStateSnapshot` とする。

- 捕捉: 3 章の方法で、貼り付け先の各項目の現在値を `ITransformData` として取る
- `Apply`: 各項目へ `layer.ApplyTransformDirect` を呼ぶ
- `Approximately`: 項目ごとに `TransformDataDiff.IsApproximatelyEqual`
- `CanApply`: レイヤーがまだタイムラインに居るか
- `HistoryScope` に `TimelineItem` を追加する。メイドに紐付かないレイヤーもあるので、`RequiresMaid` は false

## 5. UI

### 5.1 共通部品

- `ToolbarIcons.Kind.Menu`（横線 3 本、32x32 PNG）を追加する
- `ItemClipboardMenu`: アイコンボタン 1 個を描き、押すと「コピー」「貼り付け」のポップアップを出す部品。`GUIComboBox<T>` を `defaultTexture = Menu アイコン`、`showArrow = false`、`getEnabled` で無効表示、という設定で使う
  - ポップアップはボタン位置（`buttonPos`）を基準に出るので、描画する行ごとに別インスタンスを持つ。キーは BoneData / (レイヤー, 項目名)。描かれなくなったインスタンスはフレーム末に捨てる（`MaidFollowCustomValueDrawer.EndFrame` と同じ流儀）
  - ポップアップは既存の `ComboBoxPopupWindow.ProcessFocus(_rootView, this)` で出る。KeyFrameInspector / TimelineItemInspector は `_view`（`_rootView` の子）に描くので、追加の配線は要らない

### 5.2 配置

| 表示 | 位置 |
|---|---|
| キーフレームブロック | ヘッダー行の「削除」の右 |
| 一括モードの型グループ | ヘッダー行の「削除」の右 |
| レイヤー項目（選択全体） | TimelineItemInspector の先頭に「選択中の項目 (N件)」＋右端に ☰ の 1 行 |
| レイヤー項目（項目ごと） | 見出し行がある項目は見出しの右端。表情モーフ・シェイプキーなど 1 行で完結する項目は行の右端（スライダーを 1 アイコンぶん縮める）。見出しも 1 行完結の行も無い項目は、`displayName` の見出し行を足してその右端 |

項目ごとの配置は各 `*ItemInspector` / 行ドロワーに手を入れる。作業は計画で Inspector ごとのタスクへ分ける。

## 6. 影響範囲

- 新規: `ItemValueClipboard.cs`、`ItemClipboardMenu.cs`、`Manager/History/TimelineItemSnapshot.cs`
- 変更: `ToolbarIcons.cs`、`KeyFrameInspector.cs`、`KeyFrameBatchDrawer.cs`、`TimelineItemInspector.cs`、`Timeline/ItemInspector/*ItemInspector.cs`（約 30）、一部の行ドロワー（`FaceMorphRowDrawer` など）、`HistoryScope.cs`（と `HistoryScopeUtils`）、`TimelineLayerBase.cs`、`MorphTimelineLayer.cs`、`MotionTimelineLayer.cs`、`ShapeKeyTimelineLayer.cs`、`ModelShapeKeyTimelineLayer.cs`、`DressTimelineLayer.cs`、`StageLaserTimelineLayer.cs`、`StageLightTimelineLayer.cs`、`PsylliumTimelineLayer.cs`、`VoiceTimelineLayer.cs`、`SeTimelineLayer.cs`
- MTEUtils サブモジュールは変更しない（`GUIComboBox` を既存の API の範囲で使う）
- タイムライン XML の形式は変えない

## 7. 検証

- ビルド: COM3D2 / COM3D25 の両構成
- 単体テスト: 2 章の互換規則（名前一致・単一型一致・不一致）を `ItemValueClipboard` のテストで確認する。Unity ネイティブ呼び出しを避けるため、TransformData の生成に依存しない形（名前と型だけで判定する関数）に切り出す
- 実機（通常シーン、デイリー画面でエディタを有効にする）:
  - キーフレーム: ブロック間の貼り付け、型違いで無効表示、一括グループへの貼り付け、Undo
  - レイヤー項目: 既定のレイヤー（Camera, Light, Model など）と、後処理・個別実装のレイヤー（Morph, ShapeKey, Dress, StageLight, Psyllium, Motion の体ボーン / Root / ExtendBone）で、貼った値が次のフレームで戻らないこと
  - 自動キー登録 ON で貼り付けるとキーが登録され、Undo で戻ること
  - Voice / Se で「貼り付け」が無効表示になること
  - Motion のコピーで、Move レイヤーなしの編集モード中にメイドの位置がずれないこと
