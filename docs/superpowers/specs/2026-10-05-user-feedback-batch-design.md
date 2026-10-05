# ユーザー FB 対応（3 件）の設計とループ運用

ユーザー FB で届いた要望のうち 3 件について、設計と、`/loop` で「計画 → 実装 → 実機確認」を回すための運用手順をまとめる。

作成日: 2026-10-05

| ID | 要望 | 方針 | 難易度 |
|---|---|---|---|
| A | シーンプリセットに検索バーを追加 | 全フォルダを名前で検索する。SceneCapture も含める | 小 |
| B | 重力設定にローカル座標を追加 | Bip01 基準のローカル軸フラグを追加する | 小〜中 |
| C | メイドのボーンに表示/非表示を追加 | ACCEx の「表示ノード選択」相当を、タイムライン・プリセット対応で追加する | 中 |

- 実装順は A → B → C。
- 4 件目の「ModsSlider の値をシーンプリセット・タイムラインに保存」（MaidScale 廃止・体型スライダー一本化）は仕様検討中のため対象外とした。調査結果と設計案は `2026-10-05-body-slider-draft.md` に分けてある。ループではこのファイルに触れない。

---

## ループ運用

`/loop` の 1 回の起動で、進捗表の「次の 1 段」だけを進める。進捗はすべて下の進捗表に書くので、コンパクションやセッションのやり直しがあっても、進捗表を読めば再開できる。

### 作業ブランチ

- `feat/user-feedback-2026-10` を main から 1 本切り、A・B・C をこの上に順に積む。worktree は使わない（CLAUDE.md）。
- ブランチが既にあれば、それを checkout して続ける。
- この spec（進捗表を含む）もこのブランチ上で更新し、コミットする。
- マージと push はしない。ユーザーが行う。

### 1 機能の段

各機能は次の段を順に進む。進捗表の「状態」列に、終わった段の名前を書く。

| 段 | やること | 終わりの判定 | 次の状態 |
|---|---|---|---|
| 1. 計画 | superpowers:writing-plans で、この spec の該当節から計画を作る。保存先は `docs/superpowers/plans/2026-10-05-<slug>.md`。続けて plan-review でレビューし、指摘を取り込む | 計画ファイルがあり、plan-review の指摘を処理し終えた | `計画済` |
| 2. 実装 | superpowers:executing-plans で計画を実行する。最終レビューは code-review スキルで行う（CLAUDE.md） | 計画の全タスクが終わり、「ビルド＆テスト」（下記）が通り、code-review の指摘を処理し終えた | `実装済` |
| 3. 実機確認 | restart-verify スキルに従い、ゲームを再起動して DLL を反映し、該当節の「実機確認」を devbridge で行う | 自動で確かめられる項目がすべて期待どおりで、`tail_log` に SE 由来の例外が無い | `実機確認済` |
| 4. コミット | commit スキルでコミットする。この spec の進捗表の更新も含める | コミットが作成された | `完了` |

- 段 1 の計画を作り終えたら、ユーザーに提示せずにそのまま段 2 へ進んでよい。
- 実機確認で不具合が見つかったら修正して段 3 をやり直す。修正が計画の範囲を超える場合は、計画に追記してから直す。
- 目視や IME 入力など自動で確かめられない項目は、進捗表の「ユーザー確認待ち」に書き残して先へ進む。これは止まる理由にならない。

### ビルド＆テスト

Git Bash から実行する。順番は「COM3D2 → COM3D25 → dotnet test」。COM3D2 構成のビルドが `bin/Debug/COM3D25/` を消すため、この順でないとテストが古い DLL を見る。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj /v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

- この MSBuild 直叩きはゲームフォルダへ DLL をコピーしない。段 2 ではこちらを使う。
- 実機へ反映するのは段 3 だけ。restart-verify の「2. DLL 更新」で、ゲームを終了させたあとに、リポジトリ直下の `debug.bat com3d25` を実行する。これでビルドと、`Sybaris\UnityInjector\` への DLL・Config のコピーが行われる。
- 反映の判定は、ビルド成果物とゲーム側の DLL のハッシュが一致すること。

### 実機確認の共通事項

- restart-verify では最新のセーブをロードする。SE の検証は、そのまま通常シーン（デイリー画面など）で行う。ギアメニューからエディタウィンドウモードを有効にする。撮影モード（スタジオ）では検証しない。
- SE の内部状態はリフレクションで読み書きする（memory `devbridge-plugin-reflection`）。状態を変える eval と読む eval は分け、1 フレーム以上空ける。
- 確認が終わったら、変えた状態を元に戻す。セーブはしない。

### 止まる条件

次のどれかに当たったら、進捗表の「メモ」に状況を書き、ループを止めて（ScheduleWakeup の `stop: true`）、ユーザーに報告する。

- spec にも計画にも答えが無く、どう進めても推測になる仕様判断が要る（「未決事項」で「ユーザー」が決めるもの）
- 同じ段で 3 回続けて失敗した（ビルド・テスト・実機確認が通らない）
- ゲームが起動しない、またはセーブをロードできない（restart-verify の段 4・5 が上限時間を超えた）
- 取り消せない操作が要る（`deploy.bat` / `release.bat` の実行、push、マージなど。`deploy.bat` はどんな場合も実行しない）

A・B・C がすべて `完了` になったときもループを止め、全体の結果と「ユーザー確認待ち」の一覧を報告する。

### 進捗表

| ID | 状態 | 計画ファイル | メモ |
|---|---|---|---|
| A | 完了 | `docs/superpowers/plans/2026-10-05-preset-search.md` | plan-review 🟡: 3 件取り込み・3 件却下（計画末尾に記録）。実装: 両構成ビルド OK、テスト 1536/1536。code-review: 両側 APPROVE WITH COMMENTS、4 件取り込み（タイル描画を `DrawTiles` に統合、件数ラベル、コメント圧縮、検索終了時に結果を手放す）、6 件却下（Trim・「開く」無効化・スクロール復元は spec どおりのため等）。実機（セーブ 42、SceneDaily、DLL ハッシュ一致）: test=6 / てすと=12 / PRESET=13 / 神社=7 件で実ファイルの部分一致数と一致、SC タグ付き、該当なし表示 OK、SC「TEST」を結果から適用 OK、一時プリセットの削除で結果が作り直される、相対パス `SceneCapture/稲荷神社夜`、検索中の「<」「保存」無効を screenshot で確認。SE 由来の例外なし。Task 3 は `docs/` しか探しておらず漏れていたため、段 3 で `docs-site/guide/scene-preset.md` に「検索」節を追記した |
| B | 完了 | `docs/superpowers/plans/2026-10-05-gravity-local.md` | 基準姿勢は計画時に実測（旧ボディ）。bindpose は標準立ち `maid_stand01` と約 10° ずれるため、`maid_stand01` の Bip01 を定数にした（ほかの立ちポーズでは 4〜19° ずれる旨を docs に書く）。CRC は未ロードで段 3 に回す。plan-review 🟡: 7 件取り込み・2 件却下（計画末尾に記録）。実装: 両構成ビルド OK、テスト 1551/1551。code-review: 両側 APPROVE WITH COMMENTS、取り込み 7 件（タイムライン経由の毎フレーム適用でも閾値が効くよう判定を `ApplyCategory` へ移動、ボディ未ロード時の `GetBone` NRE ガード、docs にメイドの回転にも追従する旨、コメント圧縮等）、見送り 2 件（ledger `.superpowers/sdd/2026-10-05-gravity-local/progress.md`）。段 3 で見る: 閾値・NRE ガードの実機挙動、ダンス中の fps、CRC の基準姿勢。実機（セーブ 42、SceneDaily、旧ボディのメイド 1 人、DLL ハッシュ一致）: スカート重力 ON・offset (0,-1,0) でワールド時 lp=(0,-1,0)、`maid_stand01` でローカル ON の lp=(0,-1,0) とワールドに一致、ルートを X 軸 90° 回すと lp=(0,0,-1)＝`R×offset`、ローカル OFF に戻すと回したままでも (0,-1,0)。着替え（boDut + `AllProcPropSeqStart`）完了後もローカルの値が書き戻る。`dance_lesson_sb_f` 再生中はローカル ON で 657 フレーム中 137 回だけ書き込み（閾値が効いている）、fps は ON 59.7 / OFF 59.3 で差なし。体が横倒しになる区間で力が体の下向き (-0.99,-0.09,-0.10) へ回ることも確認。`tail_log` の例外は YotogiUtil 由来のみで SE 由来なし。状態は元へ戻した |
| C | 完了 | `docs/superpowers/plans/2026-10-05-node-visibility.md` | UI は脱衣ウィンドウの内部タブ（重力ウィンドウのタブが先例、新ウィンドウは Config・メニュー・docs の追加が増える）。実機で 90 ノード名が旧ボディの `BoneNames` と一致、onepiece に node消去 14 件を確認。2.0 にも同名メンバーあり。plan-review: 自己申告 🔴（タイムラインで「上書きなし」を表せない）を、値は spec どおり bool のまま「最初のキーより前の区間に入ったら解除」「キー済みノードは今の表示で書き出す」で解消し 🟡。取り込み 7 件・却下 3 件（3 値化・`FixMaskFlag` 削除・SE 無効中の着替え。計画末尾に記録）。実装: 両構成ビルド OK、テスト 1581/1581。code-review: 機能面 REQUEST CHANGES（🔴 強制表示の退避値を作り直されたスロットへ戻す / 🟡 同期 AllProcProp で追従しない）→ 退避にスロットの実体（`TBodySkin.obj`）を持たせ、`Update` で実体の入れ替わりを検知して再適用する形で修正（テスト RED→GREEN）。ほか描画のキャッシュ・不要な FixVisibleFlag の抑止・可読性指摘を取り込み、見送り 3 件（ledger `.superpowers/sdd/2026-10-05-node-visibility/progress.md`）。段 3 で見る: 着替え・めくれでの再適用、強制表示の解除後の値、CRC の BoneNames。実機（セーブ 42、SceneDaily、旧ボディのメイド 1 人、DLL ハッシュ一致）: `Mune_L` 非表示で body の dict が false・全脱衣でも胸の左側が消えたまま（screenshot）、解除で True に戻る。onepiece が消す `Mune_L` の強制表示で onepiece の dict が true、onepiece の再処理（boDut + `AllProcPropSeqStart`、実体も作り直し）の後も true、解除で menu の false に戻る。非表示（左手）は着替え中に元へ戻り、完了後に再び false。めくれ（同期の読み直し）の前後でも強制表示が翌フレームに書き直され、解除で false に戻る。タイムライン: 10F に非表示キー → 0F へシークで上書き解除・20F で非表示。左手を 5F 非表示・15F で解除してキー → 8F 非表示・18F 表示。アンロードで全解除。シーンプリセット: `<nodeVisibility>` に 2 件書き出され、全解除後の読み直しで戻る（一時プリセットは削除）。ノード表示タブの描画を screenshot で確認（`*` と解除の有効化、衣装が消すノードはチェック OFF）。SE 由来の例外なし（ログの NRE は背景 MOD の AssetBundle 読込失敗でゲーム側由来）。状態は元へ戻した |

状態の値: `未着手` → `計画済` → `実装済` → `実機確認済` → `完了`（止まったときは `要対応`）

#### ユーザー確認待ち

（実機で自動確認できなかった項目を、機能 ID 付きでここへ追記する）

- A: 検索欄への日本語（IME）入力。あわせて、検索欄を空にすると元のフォルダ表示へ戻ること（結果の解放はテキスト欄の入力経由でしか通らないため、リフレクションでは確かめていない）
- A: 検索結果のタイルにマウスを乗せたとき、下部の相対パス表示の見た目
- A: プリセットのサブフォルダに入った同名プリセットを相対パスで見分けられること（検証環境のプリセットフォルダにサブフォルダが無かった）
- B: CRC ボディでの確認一式。とくに `maid_stand01` の Bip01 が旧ボディの基準姿勢 `(-0.5416, 0.5416, 0.4546, 0.4546)` と一致するか（検証環境に CRC ボディのメイドがいなかった。違えば `GravityLocalSpace` に CRC 用の定数が要る）
- B: KCES2 / MagicaCloth 系の衣装で、ローカル ON の力がワールド空間として正しく効くか（該当衣装を着たメイドがいなかった）
- B: 寝そべりポーズで髪・スカートが体の「下」側へ垂れる見た目
- B: ボディ再ロード中（未ロード）のメイドでローカル ON のまま例外が出ないこと（`isLoadedBody` ガードの経路は実機で通していない）
- C: CRC ボディでの確認一式（body の `morph.BoneNames` と dict に 90 ノードがあるか、非表示・強制表示・着替え追従）。検証環境に CRC ボディのメイドがいなかった
- C: 撮影（スクリーンショット・サムネ）にノードの表示/非表示が正しく写ること
- C: ノード表示タブの使い勝手（インデント・解除ボタンの位置、90 行のスクロール）と、Undo/Redo での戻り方

---

## 共通方針（設計）

- **XML の版**: 各機能で、タイムライン（`TimelineData.CurrentVersion`、現在 38）とシーンプリセット（`ScenePresetData.CurrentVersion`、現在 40）の版を、実装した順に 1 つずつ上げる。版を上げる必要が無い変更（要素の追加だけで、既定値が旧挙動と一致するもの）なら上げない。どちらにするかは計画で決める。
- **互換の向き**: 保証するのは MTE → SE の一方向だけ（CLAUDE.md「タイムライン XML の互換方向」）。各機能の実装時に、CLAUDE.md のこの節へ互換の注意を追記する。
- **Undo**: 新しい状態はすべて Undo に乗せる。手順は、`Manager/History/HistoryScope.cs` に値を足し、`SnapshotFactory.cs` に case を足して Snapshot 型を作る。UI 側は編集の直前に `HistoryManager.instance.BeforeEdit(maid, scope, label)` を呼ぶ。
- **ビルド**: COM3D2 と COM3D25 の両構成でビルドを確認する。

---

## A. シーンプリセットの検索バー

### 現状

- 一覧は `PresetWindow.cs`。フォルダ階層をタイルで表示し、クリックで即適用する。SceneCapture は読み込み専用の仮想フォルダで、サムネが無いので、その配下だけリスト表示にしている（:269）。
- `PngPlacementWindow.cs` に、同じ構成の検索がある。
  - `DrawSearchRow`（:252）、検索中は「<」を無効化（:272）、表示元の切り替え（:302）、`BuildSearchList`（:637）、`TempTileViewContent`（:114）。

### 仕様

- PngPlacementWindow のパターンを移植する。
  - `PresetWindow` に `_searchText` と `TempTileViewContent _searchRoot` を持たせ、ツール行の上に検索行を足す。
- **検索範囲**: 表示中のフォルダではなく全体。`ScenePresetManager.rootItem` から再帰的にファイルを集める。**SceneCapture 配下も含める**。
- **照合**: 名前の部分一致で、大文字小文字は区別しない（`OrdinalIgnoreCase`）。ほかの画面とそろえ、ひらがなとカタカナの同一視はしない。
- **表示**: 検索結果はタイルで出す。
  - SceneCapture の項目はサムネが無い。`DrawTile` はサムネが null なら描かず、名前ラベルとタグは出せる（`GUIView.cs:2665-2688`）。そこで名前を出したうえで、タグ「SC」を付けて区別する。
  - マウスを乗せたときは、名前ではなく、ルートからの相対パスを出す。同じ名前のプリセットが別フォルダにあっても区別できるようにするため。
  - 一致が無ければ、「該当するプリセットはありません」と出す。
- **検索中の操作**: 「<」と「保存」は無効にする（保存先のフォルダが決まらないため）。削除は通常のプリセットだけ許す。SceneCapture は今と同じく削除できない。
- **作り直し**: 一覧のツリーは、保存・削除・更新・自動ロードのたびに丸ごと作り直される。検索結果は、`rootItem` の参照と検索文字列が変わったときに作り直す。
- **スクロール**: 検索文字列が変わったら、スクロール位置を先頭へ戻す。
- XML への影響は無い。

### 実機確認

- `PresetWindow` の検索文字列をリフレクションで設定し、翌フレーム以降に検索結果の件数と名前を読む。期待値は、プリセットフォルダと SceneCapture の実ファイルから求めた部分一致の件数。
- SceneCapture のプリセットを含む語で検索し、結果に SC タグ付きで入ること。結果から 1 件をリフレクションで開いて、適用されること（`tail_log` に例外が無いこと）。
- 検索中は「<」と「保存」が無効であること。`screenshot` で確認する。
- ユーザー確認待ちに回す項目: 日本語（IME）入力、マウスオーバー時の相対パス表示の見た目。

---

## B. 重力のローカル座標（Bip01 基準）

### 現状

- SE はゲームの `GravityTransformControl` を、メイド × カテゴリ（hair / skirt）ごとに AddComponent している（`MaidManipulation/MaidGravityController.cs:343-361`）。
  - `ApplyCategory`（:415-445）で、`localPosition = offset` を書いている。
- ゲーム側は `localPosition * forceRate` を、DynamicSkirtBone / DynamicBone / DynamicYureBone / KCES2 の力にそのまま足す（`GravityTransformControl.cs:184-246`）。つまり今の重力はワールド方向。
- タイムラインは `GravityTimelineLayer` / `TransformDataGravity`（値は [0]Enabled、[1..3]XYZ）。シーンプリセットは `ScenePresetGravity`、Undo は `GravitySnapshot`。

### 仕様

- カテゴリごとに「ローカル」フラグを追加する。既定は OFF（今までどおりワールド）。
- **ON のときの解釈**: 入力した offset を、Bip01 の回転に追従する軸として解釈する。
  - 基準の回転 `R = Bip01.rotation × Inverse(Bip01 の基準姿勢の回転)`。基準姿勢は「メイドのルート回転が単位で、立ちポーズ」のときの Bip01 のワールド回転とする。
  - これにより、立ちポーズではローカルとワールドが一致し、寝そべるポーズでは重力も体に合わせて回る。
  - 書き込む値: `force = R × offset`。
  - 基準姿勢の回転の取り方は、計画の最初のタスクで devbridge を使って実測して決める。旧ボディと CRC で Bip01 の軸の向きが同じかも、このとき確かめる。
- **±1 クランプへの対処**: ゲーム側は各軸を ±1 に切り詰める（`GravityTransformControl.cs:188`）。回転後にいずれかの成分の絶対値が 1 を超えたら、最大成分が 1 になるようにベクトル全体を縮め、方向を保つ。
- **書き込みタイミング**:
  - ローカル ON のカテゴリは、`MaidGravityController.Update`（`MaidManipulateManager.Update` 経由）で毎フレーム書き直す。
  - Bip01 は前フレームの最終姿勢を読む。1 フレームの遅れは、物理の力としては許容範囲（既存のタイムラインも同じ遅れを持つ）。
  - 変化が `Approximately` 以下なら、ゲーム側の `UpdateParameters` は走らないので負荷は小さい。
  - 着替え中（wasBusy）は、既存どおり書き込みを止める。
- **UI**: `GravityRowDrawer` に「ローカル（Bip01）」トグルを足す。
- **タイムライン**: `TransformDataGravity` に index 4 = Local（bool）を足し、値数を 5 にする。
  - Local は補間しない。Enabled と同じく、区間の開始時に適用する。
  - 4 値の旧キーは Local=0（ワールド）として読む。`FromXml` で明示的に補正する。
- **シーンプリセット**: `ScenePresetGravity` に `[XmlAttribute] local` を足す。既定 false で、false のときは書き出さない。
- **Undo**: `GravitySnapshot` に local を足す。

### 互換

- 重力は SE 独自の機能で、MTE には無い。旧 SE は 5 値目を読み飛ばし、ワールドとして表示する。

### 実機確認

- メイド 1 人について、スカートのカテゴリの重力を ON、offset (0,-1,0)、ローカル ON にする。
  - 立ちポーズで、`GravityTransformControl` の localPosition がワールド指定のときと一致すること。
  - Bip01 を X 軸まわりに 90° 回した姿勢（eval で直接回すか、寝そべりポーズを適用する）で、localPosition が `R × offset` に一致すること（成分の誤差 0.01 以内）。
- ローカル OFF に戻すと、Bip01 を回したままでも localPosition が offset と一致すること。
- 着替え（`SetProp` + `AllProcPropSeqStart`）の最中と完了後に、`tail_log` に例外が無く、完了後に値が再び書かれること。
- 旧ボディと CRC ボディのメイドが両方ロードされていれば、両方で確認する。片方しかいなければ、いない方をユーザー確認待ちに回す。
- KCES2 / MagicaCloth 系の衣装で力の向きがワールド空間か（MagicaCloth2 の仕様からの推測にとどまっている）は、該当衣装を着たメイドがいれば `screenshot` で確認する。いなければユーザー確認待ちに回す。
- ユーザー確認待ちに回す項目: 寝そべりポーズで髪・スカートが体の「下」側へ垂れる見た目。

---

## C. ノード表示/非表示（表示ノード選択）

### 参考実装（ACCEx）の仕組み

- **ノード一覧**: `CM3D2.AlwaysColorChangeEx.Plugin/Data/ACConstants.cs:91-181` の `NodeNames` に 90 件ある。キーは body のボーン名（例: `Bip01 Pelvis_SCL_`、`Mune_L`、`Bip01 L Thigh_SCL_`、`Finger0..42`）、値は日本語の表示名と階層の深さ。
- **状態の実体**: 各スロットの `TBodySkin.m_dicDelNodeBody`（`Dictionary<string,bool>`、`TBodySkin.cs:356`）。false が非表示。
- **書き込み**: dict を書き換えたあと、`body0.FixMaskFlag(); body0.FixVisibleFlag();` を呼ぶ（`Util/MaidHolder.cs:200`、`:365`）。
- **消し方**: `TBody.FixVisibleFlag`（`TBody.cs:4143`）→ `TMorph.FixVisibleFlag`。false のボーンにウェイトを持つ三角形をメッシュから削る。localScale も骨の有効/無効も触らないので、IK や物理には影響しない。
- **公式 API との違い**: 公式の `TBody.SetVisibleNodeSlot` は名前の部分一致で、子孫にも伝播する。ACCEx は dict を直接書くので、ノード単位で効く。

### 仕様

- **単位と対象**: メイド単位。対象ノードは ACCEx と同じ 90 件で、ノードごとに個別に効く（子孫へは伝播しない）。ノードの定義（ボーン名・表示名・階層の深さ）は定義テーブル 1 か所にまとめる。
- **UI**: 階層の深さでインデントした一覧に、チェックボックスを並べる。
  - チェックボックスには、その時点で実際に表示されているかどうかを出す。
  - 一覧の上に「全表示」「全非表示」「リセット（SE の上書きを全解除）」を置く。
  - 置き場所は、脱衣ウィンドウ（`MaidUndressWindow`）のタブとして足すのを第一案とする。脱衣ウィンドウの構造上タブが不自然なら、新しいウィンドウにする。計画時に決め、計画に理由を書く。
- **状態の持ち方**: SE の上書きを、メイドごとに `Dictionary<string,bool>`（ノード名 → 表示/非表示）で持つ。キーが無いノードは上書きなしで、menu の `node消去` などゲームが決めた状態のままにする。
- **適用**:
  - 非表示: body スロット（`goSlot[0]`、常に可視）の dict に false を書く。服スロットに書くと、脱衣でそのスロットが不可視になったときに効果が消えるため。
  - 表示（menu が消したノードの強制表示）: 全スロットの dict のうち、そのキーが false のものを true にする。
  - 書き換える前の値は、ノード × スロットごとに退避する。上書きを解除するときは退避した値へ戻す。
  - 書き換えたら `FixMaskFlag()` → `FixVisibleFlag()` を 1 回呼ぶ。毎フレームの適用は不要。
- **着替え・ボディ再ロード後の再適用**: どちらでも dict は初期化される。CRC ボディは `DeleteObj` で Clear される。
  - そこで、`IsAllProcPropBusy` の立ち下がりで、退避値を取り直してから上書きを再適用する。`MaidGravityController.cs:228` の検知パターンを流用する。
- **タイムライン**:
  - `NodeVisibilityTimelineLayer` と `TransformType.NodeVisibility` を新しく作る。値は bool 1 個。
  - 作りは Undress と同じ（`UndressTimelineLayer.cs`、`TransformDataUndress.cs`）。`allBoneNames` はノード名の一覧にし、`indexUpdated` のときだけ適用する。
  - キーがあるノードだけを上書きする。
  - 登録は `TimelineIntegration.cs`（RegisterLayer / Inspector / RegisterTransform）。
- **シーンプリセット**: `ScenePresetMaid` に `nodeVisibility` 要素を足す。中身は上書きのあるノードだけの `<node name visible>`。
  - 要素が無い旧プリセットは「触らない」として扱う。空要素は「上書きを全解除」として扱う。
- **書き込み口の一本化**: 書き込みは静的クラス `MaidNodeVisibilityController` にまとめる（`MaidUndressController.SetSlotMask` と同じ形）。

### 互換

- MTE と旧 SE は、未知の enum `<Type>NodeVisibility</Type>` でデシリアライズに失敗する（MaidScale・ScreenOverlay と同じ扱い）。
- 旧 SE はプリセットの要素を読み飛ばす。

### 実機確認

- メイド 1 人について、`Mune_L` を非表示にする。
  - body スロットの `m_dicDelNodeBody["Mune_L"]` が false になること。
  - `screenshot` で胸の左側が消えていること。
- 上書きを解除すると、dict が退避した値に戻ること。
- menu の `node消去` で消えているノードがある衣装なら、そのノードを強制表示して、全スロットの dict が true になり、解除で元に戻ること。該当する衣装が無ければ、この項目はユーザー確認待ちに回す。
- 非表示のまま着替え（`SetProp` + `AllProcPropSeqStart`）を行い、`IsAllProcPropBusy` が false に戻った後に、上書きが再適用されていること。
- 非表示のまま脱衣（`SetMask`）を切り替えても、上書きが維持されること。
- CRC ボディのメイドがいれば、body の `morph.BoneNames` と dict に 90 ノードがあること、非表示が効くことを確かめる。いなければユーザー確認待ちに回す。
- タイムラインにキーを打って再生し、キーの切り替わりで表示が変わること。シーンプリセットを保存して読み直すと、状態が戻ること。
- ユーザー確認待ちに回す項目: 撮影（スクリーンショット・サムネ）に正しく写ること、ノード一覧 UI の使い勝手。

---

## 未決事項

| ID | 内容 | 決め方 |
|---|---|---|
| B | Bip01 の基準姿勢の回転の取り方 | 計画の最初のタスクで実測して決める |
| C | ノード表示 UI の置き場所 | 計画時に決める（第一案: 脱衣ウィンドウのタブ） |
