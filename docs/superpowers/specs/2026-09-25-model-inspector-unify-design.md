# モデルの Inspector 表示の統一 設計

## 目的

同じモデルなのに、Inspector の表示が経路によって違う。これを 1 つのレイアウトに揃える。

| 経路 | 現状の表示 |
|---|---|
| タイムラインでモデルレイヤーの項目をタップ（SE `ModelItemInspector`） | 表示名ラベル / 表示トグル / プラグイン・複製・削除 / アタッチ先メイド・部位 / Transform |
| SE で配置したモデルを選択（SE `InspectorWindow` の既定描画） | ヘッダー行（アクティブ・名前・フォーカス）/ Transform |
| ModItemExplorer（MIE）のモデルを選択（MIE `ModelInspectorDrawer` へ全面委譲） | ヘッダー行 / Transform（常にローカル）/ MIE のアタッチ（編集中メイド固定）/ レイヤー |

背景モデルも同じく、タップすると「表示名ラベル・表示トグル・Transform」、選択すると「ヘッダー行・Transform」になっていて食い違っている。

## 確定した方針（ユーザー決定）

- 両方の良いところを取った共通レイアウトを作り、どの経路でもそれを使う
- MIE のモデルも SE が共通レイアウトを描く。MIE 固有の行（レイヤー）だけを MIE に描かせる

## 共通レイアウト

```
ギズモ行                                    ← 既存。InspectorWindow が描く
[表示トグル] 表示名                [フォーカス]  ← ヘッダー行
[< プラグイン >] [複製] [削除]                ← 配置モデルだけ（ModelManageRowDrawer）
[< メイド >] [< 部位 >]                        ← 配置モデルだけ（SE のアタッチ、キーに入る）
位置 / 回転 / 拡縮                             ← ObjectTransformRowDrawer（ギズモの Local/Global に従う）
（委譲先の固有行。MIE はレイヤー）              ← InspectorHost.DrawRows
```

- ヘッダー行のトグルは `model.visible` に結び付ける。以前タイムライン側にあった「表示」トグルと同じ意味で、独立した「表示」行は無くす
  - 配置モデルは `StudioModelManager.SetModelVisible` と `model.visible` を書く。値を書く直前に編集モードへ入る（既存の `BeginAutoEditMode` 区間に入れる）
  - 背景モデルは `model.visible` だけを書く（既存と同じ）
- 名前は `displayName`（例: キュートオーディオプレーヤー）
- フォーカスボタンはモデル本体（`model.transform.gameObject`）へ寄せる
- タイムラインで複数項目を選んだときは、ヘッダー行がモデルごとの見出しを兼ねる
- 背景モデルは「プラグイン・複製・削除」と「アタッチ」の行を持たない（配置数の増減は背景ウィンドウの担当。既存と同じ）

## 選択経路での判定

- 選択オブジェクトが配置モデルまたは背景モデルの**本体そのもの**（`model.transform.gameObject == selected`）のときだけ、共通レイアウトを使う
- 子オブジェクト（メッシュ・ボーン）を選んだときは、これまでどおり既定表示にする。その子の Transform を個別に触れる経路を残すため。MIE の `CanDraw` も本体の完全一致で判定しているので、それと揃う
- 判定の順序は次のとおり
  1. `InspectorHost.TryDraw`（全面委譲）
  2. 配置モデル
  3. 背景モデル
  4. 既定描画
- 全面委譲を先に見るのは、旧 MIE と組み合わせたときに今の表示を保つため。新 MIE は全面委譲を登録しない

## InspectorHost の拡張（後発 API）

```csharp
// 公開 API に追加（シグネチャは安定契約）
object RegisterRows(
    string name,
    Func<GameObject, bool> canDraw,
    Func<GameObject, Rect, float> drawRows);   // rect の左上から描き、使った高さ（末尾の余白を含まない）を返す
float DrawRows(GameObject go, Rect rect);      // ホスト内部用。該当者が居なければ 0
```

- 登録は既存の `_entries` へ入れる
  - `drawRows` を持つ行の登録は、`TryDraw` の対象にしない
  - 全面委譲の登録は、`DrawRows` の対象にしない
- 同名の再登録による置き換えは、同じ種類（全面 / 行）の登録どうしに限る
- 例外の隔離と、連続 5 回の失敗で打ち切る扱いは `TryDraw` と共有する
- `DrawRows` は、最初に `canDraw` が true を返した 1 者だけを呼ぶ
- 呼び出し元は共通レイアウトの末尾だけ。呼ぶ位置は `view.GetDrawRect(-1, 0f)` で、戻り値の高さ分を `DrawEmpty` で送る（`DrawHeader` と同じ作法）

## MTEUtils `InspectorHostClient`

- `isRowsDrawAvailable` と `RegisterRows(name, canDraw, drawRows)` を追加する
- 解決できない旧ホストでは `isRowsDrawAvailable` が false になり、`RegisterRows` は null を返す
- 解決の失敗は、既存 API（`Register` / `Unregister`）の有効性に波及させない（`InitializeHeaderDraw` と同じ作法）
- MTEUtils は共有サブモジュール。サブモジュール側でコミットし、SE と MIE の参照を進める

## MIE の変更

- `ModelInspectorDrawer.DrawRows(GameObject go, Rect rect)`: レイヤー行だけを自前の `GUIView`（余白なし）で描き、高さを返す
- `SelfModelPlacer.TryRegisterInspector` の登録先
  - `isRowsDrawAvailable` なら `RegisterRows` で登録する
  - 旧ホストなら従来どおり `Register(..., drawsHeader: true)` で全面委譲する
- MIE のアタッチ行は Inspector に出さない（SE のアタッチ行に一本化）。SE のアタッチ変更はプロバイダ経由で MIE へ届く（`2026-09-25-mie-attach-sync` で実装済み）
- `isInspectorRegistered` の意味は変えない（行の登録でもハンドルが立つ）。登録中はモデル操作ウィンドウが下部の Transform / アタッチ行を隠すが、同じ内容は SE の共通レイアウトに出る

## 互換

| SE | MIE | 結果 |
|---|---|---|
| 新 | 新 | 共通レイアウト + MIE のレイヤー行 |
| 新 | 旧 | MIE の全面委譲が先に取るので、今の表示のまま |
| 旧 | 新 | `RegisterRows` が無いので MIE は全面委譲へ倒す。今の表示のまま |

## テスト

単体テスト（SE。Unity のネイティブ呼び出しは不可）:
- `InspectorHost.RegisterRows` / `DrawRows`
  - 該当者の高さを返す
  - 該当なしなら 0
  - 行の登録が `TryDraw` に拾われない
  - 全面委譲の登録が `DrawRows` に拾われない
  - 同名でも種類が違えば置き換えない
  - 例外時は 0 を返し、連続 5 回で打ち切る
- `InspectorHost` の公開契約: `RegisterRows` のシグネチャ

実機（devbridge）:
- 配置モデル（SE 配置 / MIE 配置）と背景モデルで、タップ時と選択時の表示が一致する
- MIE のモデルでは末尾にレイヤー行が出て、切り替えが効く
- ヘッダー行のトグル・フォーカス、複製・削除、アタッチ、Transform 編集が動く
- 子オブジェクトの選択は既定表示のまま

## 範囲外

- MIE のアタッチ先メイド選択 UI（SE のアタッチ行で代替できる）
- SE と MIE で異なるラベル幅の統一（MIE のレイヤー行は MIE の幅のまま）
- 選択時に子オブジェクトからモデル本体へ寄せること
