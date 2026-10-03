# メイドスケールのウィンドウ編集とシーンプリセット対応 設計

前提: メイドスケール本体（`docs/superpowers/specs/2026-10-03-maid-scale-design.md`、ブランチ `feature/maid-scale`）。

## 目的

メイドスケール（腕 6 本の均一倍率）を、タイムラインの Inspector 以外からも編集でき、シーンプリセットで保存・復元できるようにする。

成功条件:

- ボーンウィンドウから、選んだメイドの腕 6 本の倍率を編集できる（タイムライン未読込でも使える）
- シーンプリセットに倍率が保存され、適用すると復元される
- 旧プリセット（v37 以前）を適用しても倍率は変わらない
- ボーンウィンドウで腕の骨を選んだとき、倍率を含まない素の値が表示・編集される

## 範囲

- 含めるもの
  - ボーンウィンドウの内部タブ「腕スケール」
  - Inspector と共有する行描画 `MaidScaleRowDrawer`
  - ウィンドウ描画の間だけ倍率を外す処理（ボーン編集との共存）
  - シーンプリセット v38 の `maidScale` 要素
- 含めないもの
  - ボーンウィンドウの PartsEdit 互換プリセットへの保存（形式が PartsEdit 互換のため）
  - 専用ウィンドウの新設
  - 対象骨・倍率の範囲の変更

## 構成

### 行描画: `MaidScaleRowDrawer`

- `MaidScaleItemInspector.DrawScaleSlider` の中身を `MaidScaleRowDrawer.Draw(GUIView view, Maid maid, MaidScaleBone bone, float rowHeight)` へ移す（`GravityRowDrawer` と同じ置き場所・作法）
- 倍率スライダー（`MaidScaleBones.MinScale`〜`MaxScale`、既定 1、リセットボタン付き）。変更時は `HistoryManager.instance.BeforeEdit(maid, HistoryScope.MaidScale, "メイドスケール: " + 表示名)` の後に `SetScale` する
- Inspector は見出し（`TimelineItemClipboardMenu.DrawHeading`）を出してからこの行を描く。ウィンドウは見出しの代わりにラベルとして表示名を出す

### ボーンウィンドウ: 「腕スケール」タブ

- `BoneEditWindow.BoneTabType` に `腕スケール` を追加する（`編集 / プリセット / 腕スケール`）
- 対象種別が「モデル」のときはタブを出さない。モデルへ切り替えた時点で「腕スケール」が選ばれていたら「編集」へ戻す
- 内容
  - ボディ未読込・アイテム処理中は、ボーンウィンドウの既存の案内（「ボディが読み込まれていません」「プロパティ適用中...」）に任せる
  - `MaidScaleBones.bones` の順に 6 行
  - 「すべて 1 に戻す」ボタン。押すと 1 件の履歴（`BeforeEdit` 1 回）で 6 本を 1 にする。拡縮していないメイドでは押せない（無変更の履歴を積まない）
- 骨ツリー用の骨格線・関節クリック選択は「編集」タブの機能のままとし、このタブでは変えない
- タイムラインにメイドスケールレイヤーがある場合は、重力ウィンドウと同じく、ポーズ編集中でなければ再生値が優先される。`BeforeEdit` がポーズ編集へ入るので、編集した値はそのまま見える

### ボーン編集との共存

- 問題: プラグインの OnGUI は描画の後に走るが、`TBody.LateUpdate` の直後に掛けた倍率は次の Update まで残る。ボーンウィンドウ（`BoneEditManager`）は OnGUI で腕の複製骨の `localScale` を読み書きするため、倍率込みの値を表示し、編集すると倍率が焼き込まれ、元値の記録（`EnsureOrigRecorded`）にも倍率込みの値が残る
- 対処: `SceneEditorPlugin.OnGUI` でウィンドウを描く間だけ倍率を外し（`SuspendApplied`）、描き終えたら掛け直す（`ResumeApplied`、例外でも `finally` で戻す）
  - 外したままにしないのは、OnGUI の後に描く撮影（`ScreenshotHotkeyPatch` が `WaitForEndOfFrame` の後に呼ぶ `ScreenshotManager.Capture` の `camera.Render()`、サムネイル）にも倍率を写すため
  - 掛け直しは GUI が書き換えた今の値を元にする。GUI の中で倍率を変えた場合は新しい倍率で掛ける
  - Update での復元はそのまま残す
- ボーンウィンドウのタイムラインのゲート（今はメイドアニメのレイヤーでヘッダーとタブバーより前に掛けている）はタブバーの後へ移し、タブごとのレイヤー（「腕スケール」はメイドスケール、他はメイドアニメ）で掛ける。ゲートが閉じていてもタブを切り替えられるようにするため

### シーンプリセット v38: `maidScale`

```xml
<maid ...>
  <maidScale>
    <bone name="Bip01 L Hand" scale="1.5" />
  </maidScale>
</maid>
```

- `ScenePresetMaid.maidScale`（`ScenePresetMaidScale`、子要素 `bone` のリスト。`bone` は属性 `name` / `scale`）
- 保存: 倍率が 1 でない骨だけを書く。要素自体は常に書き、全骨 1 なら空要素にする（「全部 1 に戻す」と「旧データ」を区別するため）
- 適用:
  - `maidScale` が null（v37 以前）なら倍率へ触らない
  - 一度も拡縮していないメイドで、記録が空なら何もしない（`ApplyGravity` と同じく常駐コストを増やさない）
  - それ以外は 6 本すべてへ、記録があればその値、無ければ 1 を `SetScale` する
  - 対象外の骨名は読み飛ばす。値は `SetScale` が範囲へ丸める
- 保存・適用は `ScenePresetManager` の `CaptureGravity` / `ApplyGravity` と同じ場所で呼ぶ
- `ScenePresetData.CurrentVersion` を 38 に上げ、版の履歴コメントへ追記する
- `W:\COM3D2_5\work\CLAUDE.md` の互換方向の節に、シーンプリセット v38 の 1 行を足す

## 既知の制約

- タイムラインでメイドスケールレイヤーを再生中は、ウィンドウやプリセットで変えた倍率が次のフレームで再生値に上書きされる（重力と同じ）

## テスト

**単体テスト**（Unity のネイティブ呼び出しは使わない）

- `ScenePresetMaid` の `maidScale` の XML 往復（値が保たれる）
- `maidScale` 要素の無い XML は null になる
- 空の `maidScale` 要素は空リストとして読める（null にならない）
- 保存用の変換で、倍率 1 の骨は書き出さず、対象外の骨名を読み飛ばす（変換は Maid を受けない純粋関数に切り出す）
- `ScenePresetData.CurrentVersion` が 38

**実機（devbridge、通常シーン）**

- ボーンウィンドウの「腕スケール」タブで倍率を変えると効き、Undo / Redo で戻る。「すべて 1 に戻す」が 1 回の Undo で戻る
- 倍率 1.5 の前腕をボーンウィンドウの「編集」タブで選ぶと、スケール欄が素の値を示す。スケールを編集しても倍率が焼き込まれない
- 倍率つきでシーンプリセットを保存し、倍率を 1 に戻してから適用すると復元される
- v37 のプリセットを適用しても倍率は変わらない
