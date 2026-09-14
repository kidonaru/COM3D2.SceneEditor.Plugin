# ショートカット

タイムライン用のキーは、プラグインを表示している間はウィンドウの表示状態に関わらず有効です。テキスト入力中と、連番画像出力の実行中は無効になります。

| キー | 内容 | 設定名 |
|---|---|---|
| `Enter` | キーフレーム登録（差分） | `AddKeyFrame` |
| `Shift` + `Enter` | 全登録 | `AddKeyFrameAll` |
| `Backspace` | 選択キーの削除 | `RemoveKeyFrame` |
| `Space` | 再生 / 停止 | `Play` |
| `A` / `D` | 前 / 次のフレーム（押しっぱなしで連続） | `PrevFrame` / `NextFrame` |
| `Ctrl` + `A` / `Ctrl` + `D` | 前 / 次のキーフレーム | `PrevKeyFrame` / `NextKeyFrame` |
| `Ctrl` + `C` / `Ctrl` + `V` | キーのコピー / ペースト | `Copy` / `Paste` |
| `Ctrl` + `Shift` + `V` | 反転ペースト | `FlipPaste` |
| `Ctrl` + `Alt` + `C` / `Ctrl` + `Alt` + `V` | ポーズのコピー / ペースト | `PoseCopy` / `PosePaste` |
| `Shift`（押している間） | グリッド・ボーンメニューの複数選択 | `MultiSelect` |

## SceneEditor 本体のキー

タイムライン操作でよく使う本体側のキーです。設定は `SceneEditor.xml` にあります。

| キー | 内容 |
|---|---|
| `F1` | 編集モードの ON / OFF |
| `Tab` | ウィンドウの一括非表示 |
| `Ctrl` + `Z` / `Ctrl` + `X` | 操作を戻す / 進める |
| `Z` / `X` / `C` | ギズモを移動 / 回転 / 拡縮モードにする |

## キーの変更

タイムライン用のキーは `Sybaris\UnityInjector\Config\SceneEditor\SceneEditor.xml` の `keyBind` 要素で変更します。GUI はありません。本体側のキー（編集モード、Undo など）と同じファイルです（[設定リファレンス](/guide/configuration#キー設定)）。
