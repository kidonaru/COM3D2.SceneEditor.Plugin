# タイムライン リリース前 負債対応（優先度 低）設計メモ

作成日: 2026-09-14
元一覧: `docs/timeline-release-debt-review.md` の優先度 低（19〜25）

優先度 高（1〜8）・中（9〜18）は対応済み。残る低 19〜25 について、ユーザー判断を受けて対応可否を確定した。

## 決定一覧

| # | 項目 | 決定 | 実装 |
|---|---|---|---|
| 19 | 破壊的操作の確認が不統一 | **セーブの上書き**と**連番画像出力の既存フォルダ削除**にだけ確認を足す。新規作成・ロード・トラック削除・テンプレ削除は現状維持 | あり |
| 20 | 設定が二系統（`Timeline.xml` の死に項目とキー割り当て） | **キー割り当てを `SceneEditor.xml` へ統合**し、`Timeline.xml` からキー関連を撤去する。リリース前なので**移行処理は作らない**（旧 `keyBind` 要素は読み飛ばし） | あり |
| 21 | ショートカットが `タイムライン操作` ウィンドウ表示中のみ有効 | キー処理をウィンドウから外し、プラグイン本体の `Update` で常時処理する | あり |
| 22 | グリッドのズームが設定ファイル編集のみ | 現状維持（`settings.md` に設定ファイル項目として記載済み） | なし |
| 23 | タイムラインの削除・リネームが無い。既定名 `テスト` | 現状維持（`files.md` に記載済み） | なし |
| 24 | `個別設定を初期化` で `最終フレーム` だけ戻らない | 現状維持（`settings.md` に記載済み） | なし |
| 25 | `ボイス最大秒数` / `移動範囲` が値域を決める | 現状維持（`settings.md` に記載済み） | なし |

## 19: 確認ダイアログ

### セーブの上書き

現状 `TimelineControlWindow.OnSaveClicked` → `TimelineManager.SaveTimeline()` が `FileMode.Create` で無条件に上書きする。同名の別タイムラインを気付かず潰す事故があり得る。

- 確認は UI 層（`OnSaveClicked`）に置く。`MTEUtils.ShowConfirmDialog` はコールバック方式なので、`SaveTimeline()` の中に入れると後続の `TimelineLoadManager.Reload()` が保存前に走る
- 既存ファイルがあるときだけ確認する。初回保存は今までどおり即保存
- 対象ファイルは `timeline.timelinePath`（`PluginUtils.GetTimelinePath(anmName, directoryName)`）

### 連番画像出力

`OutputImageInternal` が `Directory.Delete(outputDir, true)` で出力先を中身ごと消す。開始前の確認文にこの事実が無い。

- 確認ダイアログを増やさず、`OutputImage()` の既存の確認文を**出力先が既に存在するときだけ**削除警告入りに差し替える
- 出力先は `PluginUtils.GetImageOutputDirPath(timeline.anmName)`。`timeline` が null のときは従来文言

## 20: キー割り当ての統合

### 現状

| 持ち主 | 中身 |
|---|---|
| `SceneEditor.xml`（`Config`） | `PluginToggle` / `GizmoMove` / `GizmoRotate` / `GizmoScale` / `Undo` / `Redo` / `EditModeToggle` / `WindowsHiddenToggle`。キーリピート間隔（`keyRepeatTimeFirst` / `keyRepeatTime`）もここが唯一の持ち主 |
| `Timeline.xml`（`MTEP.Config`） | 上記と重複する `PluginToggle` / `Visible` / `EditMode` / `Undo` / `Redo`（いずれも未使用）と、実際に使われる 14 種 |

`Timeline.xml` 側で実際に読まれているのは次の 14 種だけ:
`AddKeyFrame` / `AddKeyFrameAll` / `RemoveKeyFrame` / `Play` / `Copy` / `Paste` / `FlipPaste` / `PoseCopy` / `PosePaste` / `PrevFrame` / `NextFrame` / `PrevKeyFrame` / `NextKeyFrame` / `MultiSelect`

### 変更

- SE 本体の `KeyBindType` へ上記 14 種を追加し、既定値も `SceneEditor.xml` 側へ移す（既定キーは現行のまま）
- SE 本体の `Config` へ `GetKeyDownRepeat` と `isKeyInputEnabled` を移設する。`GetKeyDownRepeat` は既に SE の `keyRepeatTimeFirst` / `keyRepeatTime` を見ているので、移設で参照の往復が消える
- `MTEP.Config` から `KeyBindType` / `keyBinds` / `keyBindsXml` / `GetKey*` / `isKeyInputEnabled` を削除する。ついでに「MTE 互換のため残す。SceneEditor では効果なし」と自ら書いてある `disablePoseHistory` も消す
- 既存 `Timeline.xml` の `<keyBind>` 要素は未知要素として読み飛ばされる（`TimelineConfigXmlTests` と同じ性質）。カスタムキーは既定へ戻るが、リリース前なので移行はしない
- GUI は作らない。変更先が `SceneEditor.xml` 一本になった旨を仕様書へ書く

### 名前の衝突

追加する 14 種は SE 既存の 8 種と名前が重ならないので、接頭辞は付けない。`Timeline.xml` 側にあった `PluginToggle` / `Visible` / `EditMode` / `Undo` / `Redo` / `GC` は読み手が無いので移さず捨てる。

## 21: ショートカットのウィンドウ依存

現状 `TimelineControlWindow.Update()` が `isWndVisible` を見てから `UpdateKeyInput()` を呼ぶため、ウィンドウを閉じるとタイムライン系キーが全滅する。

- キー処理を `TimelineKeyInput`（新規の静的クラス）へ移し、プラグイン本体の `Update()` から `UpdateGizmoToolKey` 等と並べて呼ぶ
- 残す条件: テキスト入力中（`GUIUtility.keyboardControl != 0`）は無視、`studioHack` / `maid` が無い、または `IsValidData()` が false なら何もしない。プラグイン無効中（`isEnable == false`）は本体 `Update` 側で既に弾かれる
- `MultiSelect` は `TimelineWindow` 側の判定なのでそのまま（参照先が `config` に変わるだけ）
- 連番画像出力中の `isKeyInputEnabled = false` は移設先でも同じ意味で効く

## 影響範囲

| ファイル | 変更 |
|---|---|
| `Config.cs` | `KeyBindType` に 14 種追加、既定値追加、`GetKeyDownRepeat` / `isKeyInputEnabled` 追加 |
| `Timeline/Config.cs` | キー関連と `disablePoseHistory` を削除 |
| `TimelineKeyInput.cs`（新規） | タイムライン系キー処理 |
| `COM3D2.SceneEditor.Plugin.cs` | `UpdateTimelineKey()` の呼び出し追加 |
| `TimelineControlWindow.cs` | `Update` / `UpdateKeyInput` 削除、セーブ上書き確認、`GetKeyName` の参照先変更 |
| `TimelineWindow.cs` | `isMultiSelect` の参照先変更 |
| `Timeline/Manager/TimelineManager.cs` | `isKeyInputEnabled` の参照先変更、`OutputImage` の確認文言 |
| `docs-site/timeline/shortcuts.md` / `files.md` / `compatibility.md` | 仕様反映 |

## 非対応にした理由

- 22 / 23 / 24 / 25 はいずれもデータ互換に影響せず、既に仕様書へ記載がある。リリース後でも安全に足せる
- 新規作成・ロード・トラック削除・テンプレ削除の確認は、頻繁な操作へダイアログを挟む不便のほうが大きい。`ロード` で未保存の変更が破棄される点は `files.md` に記載済み
