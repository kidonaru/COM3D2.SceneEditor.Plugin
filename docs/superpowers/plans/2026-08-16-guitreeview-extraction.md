# GUITreeView 切り出し 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HierarchyWindow と BoneEditWindow に重複しているツリー行 UI（展開状態・検索フィルタ・行構築・表示範囲カリング・スクロール追従・矢印キー操作）を、MTEUtils submodule の汎用部品 `GUITreeView<T>` として 1 箇所に集約する。

**Architecture:** ノード型 `T` を型引数にとり、木のたどり方（ID・名前・生存判定・子の数・子の取得）と行の見た目（ラベル・色・クリック時の動作）をデリゲートで受け取る。ツリーの構造も選択状態も `GUITreeView` は保持せず、利用側が持つものを参照するだけにする。これにより GameObject ツリー（Hierarchy）と SlotBoneNode ツリー（BoneEdit）という異なるノード型を同じ部品で扱える。

**Tech Stack:** C# (Unity 5.x / 2019 両対応), IMGUI (`UnityEngine.GUI`), 既存の `GUIView` ラッパ, BepInEx/UnityInjector プラグイン

**Spec:** 本計画に内包（別途のスペック文書は無い）

## Global Constraints

- 対象 submodule: `source/COM3D2.SceneEditor.Plugin/MTEUtils`（リポジトリ `https://github.com/kidonaru/COM3D2.MTEUtils.git`）。**親リポとは別の git リポジトリ**であり、コミットは別々に行う。
- `GUITreeView` の namespace は `COM3D2.MotionTimelineEditor`（`GUIView` と同じ）。
- MTEUtils は MotionTimelineEditor 等の他プラグインからも使われる共有ライブラリ。**ゲーム固有の型（`Maid` / `SlotBoneNode` / `SelectionManager` 等）を GUITreeView から参照してはならない**。参照してよいのは `UnityEngine` と同 submodule 内の型のみ。
- 同じソースが COM3D2 (2.0) と COM3D2.5 の両方にビルドされる。両方のビルドが通ること。
- コードコメント・エラーメッセージは日本語で書く。
- ファイルは **BOM なし UTF-8 / CRLF** で保存する（既存ファイルの形式を変えないこと）。
- ビルドコマンド: `cmd /c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug"`（ゲーム起動中はデプロイのみ失敗するが、それは想定内。`ビルドに成功しました` が 2 回出て `0 エラー` であればよい）
- **テストフレームワークは存在しない**（Unity ランタイムとゲーム本体が必要なため導入もしない）。各タスクの検証は「ビルドが通ること」＋「実機での目視確認」で行う。実機確認は MCP `com3d25-devbridge` でゲームの生存を確認しつつ、ユーザーに画面操作を依頼する形で行う。

## 意図的な仕様変更

切り出しに伴い、次の 2 点は**退行ではなく意図した仕様変更**として扱う。実機確認でもこの前提で見ること。

1. **検索中の「←」キーで親へ移動しなくなる**（Hierarchy）
   旧実装は `go.transform.parent` の実オブジェクトと行を突き合わせていたため、検索フィルタ中（全行が depth 0）でも「親が偶然検索条件にヒットして行に出ている」場合は選択が移動しえた。新実装は「自分より浅い深さの直前の行」を親とみなすため、検索中は常に何も起きない。ノード型に親参照を要求しないための割り切りであり、検索中のツリー移動は元々意味を成さないので問題ないと判断する。

2. **`Clear()` はルート参照を保持する**
   レビュー指摘を受け、`Clear()` の責務を「展開状態・行・スクロール予約のリセット」に絞った。`SetRoots` の呼び直し忘れが「何も表示されない」という分かりにくい症状になるのを避けるため。

## 前提条件

このタスクに着手する前に、進行中の以下の変更がコミット済みであること:

- `HierarchyWindow.cs` / `BoneEditWindow.cs` のダーティフラグ化（親リポ）
- `MTEUtils/GUIView.cs` の `IsOutOfScrollView` 追加とカリング適用（**submodule**）

`git status` で確認する。現時点では submodule 側 `GUIView.cs` が `M`、親リポ側は `HierarchyWindow.cs` / `BoneEditWindow.cs` が `M`、`MTEUtils` が `m`（submodule 差分あり）の状態。

`GUIView.cs` は submodule 側の変更なので、**Task 1 と同じ 2 段階**でコミットする必要がある。

```bash
# 1. submodule 側をコミット
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils"
git add GUIView.cs
git commit -m "perf(guiview): スクロール範囲外の要素を描画から省く"

# 2. 親リポの変更 + submodule ポインタをコミット
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
git add source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "perf(window): ツリー行の組み立てを差分更新にする"
```

本計画はそれらが入った状態のコードを前提に書かれている。

## ファイル構成

| ファイル | 責務 |
|---|---|
| `MTEUtils/GUITreeView.cs` (新規) | 汎用ツリービュー部品。展開状態・検索語・行リスト・スクロール予約を保持し、行の構築と仮想化描画、矢印キー操作を行う |
| `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs` (改修) | ボーンツリーの行 UI を `GUITreeView<SlotBoneNode>` に委譲。残るのはスロット選択・リセット等のボーン固有 UI |
| `source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs` (改修) | シーン階層の行 UI を `GUITreeView<GameObject>` に委譲。残るのはルート収集・選択同期・ダブルクリックフォーカス |

---

## Task 1: MTEUtils に GUITreeView&lt;T&gt; を追加する

まだどのウィンドウからも使わない、単体で追加するだけのタスク。ビルドが通ることだけを確認する。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MTEUtils/GUITreeView.cs`

**Interfaces:**
- Consumes: `GUIView`（同 submodule。`BeginScrollView` / `EndScrollView` / `DrawButton` / `DrawEmpty` / `BeginHorizontal` / `EndLayout` / `GetDrawRect` / `currentPos` / `padding` / `scrollPosition` / `GUIView.gsLabel`）
- Produces: 後続タスクが使う公開 API
  - `class GUITreeView<T> where T : class`
  - アダプタ: `Func<T,int> getId` / `Func<T,string> getName` / `Func<T,bool> isAlive` / `Func<T,int> getChildCount` / `Func<T,int,T> getChild`
  - 行の見た目: `Func<T,string> getLabel` / `Func<T,Color> getLabelColor` / `Func<T,bool> isSelected` / `Action<T> onSelected`
  - 寸法: `float rowHeight` / `float indentWidth` / `float toggleWidth` / `float scrollBarWidth`
  - 状態: `string searchText { get; set; }`
  - 操作: `void SetRoots(IList<T>)` / `void SetDirty()` / `void Expand(int)` / `void Clear()` / `void Reveal(int)` / `void CancelReveal()` / `void EnsureRows()` / `void Draw(GUIView, Rect)` / `void HandleKeyboard()`

- [ ] **Step 1: `GUITreeView.cs` を新規作成する**

`source/COM3D2.SceneEditor.Plugin/MTEUtils/GUITreeView.cs` に以下を書く。

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// 展開/折りたたみ + 検索 + 行仮想化を備えた汎用ツリービュー。
    /// ノード型 T のたどり方と行の見た目をデリゲートで受け取るため、
    /// GameObject 階層でもボーン階層でも同じ部品で描ける。
    ///
    /// 行は GUILayout ではなく固定高で手動配置する。
    /// キー操作でのスクロール量を行番号から正確に計算できるようにするためで、
    /// 併せて表示範囲外の行を描画から省ける。
    ///
    /// ツリーの実体も選択状態も保持しない (利用側のものを参照するだけ)。
    /// 保持するのは展開状態・検索語・組み立て済みの行リストだけ
    /// </summary>
    public class GUITreeView<T> where T : class
    {
        // ---- 木のたどり方 (利用側が必ず設定する) ----

        /// <summary>ノードの一意な ID。展開状態の記録とスクロール予約の突き合わせに使う</summary>
        public Func<T, int> getId;
        /// <summary>検索フィルタに使う名前</summary>
        public Func<T, string> getName;
        /// <summary>ノードがまだ生きているか。false なら自身も子も行に出さない</summary>
        public Func<T, bool> isAlive;
        public Func<T, int> getChildCount;
        public Func<T, int, T> getChild;

        // ---- 行の見た目と操作 (利用側が必ず設定する) ----

        public Func<T, string> getLabel;
        public Func<T, Color> getLabelColor;
        /// <summary>矢印キー操作の起点を求めるための選択判定</summary>
        public Func<T, bool> isSelected;
        public Action<T> onSelected;

        // ---- 寸法 ----

        public float rowHeight = 20f;
        public float indentWidth = 14f;
        public float toggleWidth = 20f;
        public float scrollBarWidth = 16f;

        /// <summary>表示中の 1 行。矢印キーでの移動もこの並びをたどる</summary>
        private struct Row
        {
            public T node;
            public int depth;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _expanded = new HashSet<int>();
        private IList<T> _roots = null;
        // 行の組み直しが必要か。ルート・展開状態・検索語の変化で立てる
        private bool _rowsDirty = true;
        private string _searchText = "";
        // 選択行を画面内へ送るスクロール量。次の描画で反映する
        private int _scrollToRow = -1;
        // 表示したいノードの ID。行位置は行構築後でないと確定しないため予約だけしておく
        private int _pendingRevealId = 0;
        private bool _hasPendingReveal = false;

        /// <summary>検索語。変えると次の描画で行を組み直す</summary>
        public string searchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value ?? "";
                    _rowsDirty = true;
                }
            }
        }

        /// <summary>
        /// ルート一覧を差し替える。
        /// 同じリストを利用側が中身だけ入れ替えている場合は参照が変わらないため、
        /// そのときは別途 SetDirty() を呼ぶこと
        /// </summary>
        public void SetRoots(IList<T> roots)
        {
            if (!ReferenceEquals(_roots, roots))
            {
                _roots = roots;
                _rowsDirty = true;
            }
        }

        public void SetDirty()
        {
            _rowsDirty = true;
        }

        /// <summary>
        /// 展開状態・行・スクロール予約を捨てる (シーン切替時など)。
        /// ルート参照は保持したままにする。ここで捨てると SetRoots の呼び直しを
        /// 忘れたときに「何も表示されない」という分かりにくい形で症状が出るため
        /// </summary>
        public void Clear()
        {
            _rows.Clear();
            _expanded.Clear();
            _scrollToRow = -1;
            _hasPendingReveal = false;
            _rowsDirty = true;
        }

        /// <summary>指定 ID を展開する。祖先をまとめて開くときに使う</summary>
        public void Expand(int id)
        {
            if (_expanded.Add(id))
            {
                _rowsDirty = true;
            }
        }

        /// <summary>指定 ID の行を画面内へ送るよう予約する。行に出ていなければ何も起きない</summary>
        public void Reveal(int id)
        {
            _pendingRevealId = id;
            _hasPendingReveal = true;
        }

        /// <summary>
        /// 表示予約を取り消す。選択が外れたときに呼ぶと、直前に予約した行へ
        /// 意図せずスクロールしてしまうのを防げる
        /// </summary>
        public void CancelReveal()
        {
            _hasPendingReveal = false;
        }

        /// <summary>
        /// 行が古ければ組み直す。行番号に依存する処理の前に呼ぶ。
        /// Draw / HandleKeyboard は内部で呼ぶため、利用側から呼ぶ必要は通常ない
        /// </summary>
        public void EnsureRows()
        {
            ValidateDelegates();

            if (_rowsDirty)
            {
                BuildRows();
            }
            ResolvePendingReveal();
        }

        private bool _validated = false;

        /// <summary>
        /// 必須デリゲートの設定漏れを最初の 1 回だけ検査する。
        /// 未設定のまま描くと内部の奥で NullReferenceException になり原因が分かりにくいため、
        /// 何が足りないかを名指しで知らせる。
        /// OnGUI から毎フレーム投げるとログが溢れるので検査は 1 回きりにする
        /// </summary>
        private void ValidateDelegates()
        {
            if (_validated)
            {
                return;
            }
            _validated = true;

            var missing = new List<string>();
            if (getId == null) missing.Add("getId");
            if (getName == null) missing.Add("getName");
            if (isAlive == null) missing.Add("isAlive");
            if (getChildCount == null) missing.Add("getChildCount");
            if (getChild == null) missing.Add("getChild");
            if (getLabel == null) missing.Add("getLabel");
            if (getLabelColor == null) missing.Add("getLabelColor");
            if (isSelected == null) missing.Add("isSelected");
            if (onSelected == null) missing.Add("onSelected");

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "GUITreeView のデリゲートが設定されていません: " + string.Join(", ", missing.ToArray()));
            }
        }

        /// <summary>展開状態と検索条件から、実際に表示する行を組み立てる</summary>
        private void BuildRows()
        {
            _rowsDirty = false;
            _rows.Clear();

            if (_roots == null)
            {
                return;
            }

            var searching = !string.IsNullOrEmpty(_searchText);
            for (var i = 0; i < _roots.Count; i++)
            {
                AddRows(_roots[i], 0, searching);
            }
        }

        private void AddRows(T node, int depth, bool searching)
        {
            if (node == null || !isAlive(node))
            {
                return;
            }

            // 検索中は一致するものだけフラット表示
            var matched = !searching ||
                getName(node).IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            if (matched)
            {
                _rows.Add(new Row { node = node, depth = searching ? 0 : depth });
            }

            if (searching || _expanded.Contains(getId(node)))
            {
                var childCount = getChildCount(node);
                for (var i = 0; i < childCount; i++)
                {
                    AddRows(getChild(node, i), depth + 1, searching);
                }
            }
        }

        private void ResolvePendingReveal()
        {
            if (!_hasPendingReveal)
            {
                return;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (getId(_rows[i].node) == _pendingRevealId)
                {
                    _scrollToRow = i;
                    break;
                }
            }
            _hasPendingReveal = false;
        }

        /// <summary>
        /// listRect の領域にツリーを描く。
        /// 行位置とスクロール量をずらさないよう padding なしで描くため、
        /// 呼び出し前後で view.padding は保存・復元する
        /// </summary>
        public void Draw(GUIView view, Rect listRect)
        {
            if (listRect.height <= 0f)
            {
                return;
            }

            EnsureRows();
            ApplyScrollToRow(view, listRect.height);

            var savedPadding = view.padding;
            view.padding = Vector2.zero;

            var contentWidth = Mathf.Max(listRect.width - scrollBarWidth, 0f);
            var contentHeight = _rows.Count * rowHeight;
            // 内容矩形は毎フレーム行数から与える。EndScrollView が最後に描いた行の位置で
            // 高さを書き戻すが、次フレームのここで上書きされるためスクロール範囲は保たれる。
            // 縦バーは他ウィンドウと揃えて常時表示にする (幅は contentWidth で常に確保済み)
            view.BeginScrollView(
                listRect.width, listRect.height,
                new Rect(0f, 0f, contentWidth, contentHeight), false, true);
            {
                // 表示範囲に入っている行だけ描く。
                // 行内のボタン操作で行が組み直されて縮む場合があるため、毎回件数を見る
                var firstRow = Mathf.Max((int)(view.scrollPosition.y / rowHeight), 0);
                var lastRow = Mathf.Min(
                    (int)((view.scrollPosition.y + listRect.height) / rowHeight) + 1, _rows.Count - 1);
                for (var i = firstRow; i <= lastRow && i < _rows.Count; i++)
                {
                    DrawRow(view, _rows[i], i, contentWidth);
                }
            }
            view.EndScrollView();

            view.padding = savedPadding;
        }

        /// <summary>index 行目を描く。行の位置は行番号から直接決めるため currentPos を毎回置き直す</summary>
        private void DrawRow(GUIView view, Row row, int index, float contentWidth)
        {
            var node = row.node;
            // 行は組み直しまでキャッシュされるため、破棄済みが残りうる。
            // 放っておくと空白行が残り続けるので、見つけた時点で組み直しを予約する
            // (反復中なのでその場では組み直さない)
            if (!isAlive(node))
            {
                _rowsDirty = true;
                return;
            }

            view.currentPos = new Vector2(row.depth * indentWidth, index * rowHeight);
            view.BeginHorizontal();
            {
                if (getChildCount(node) > 0)
                {
                    var id = getId(node);
                    var isExpanded = _expanded.Contains(id);
                    if (view.DrawButton(isExpanded ? "-" : "+", toggleWidth, rowHeight))
                    {
                        ToggleExpanded(id);
                    }
                }
                else
                {
                    // 子がなくてもラベルの開始位置は揃える
                    view.DrawEmpty(toggleWidth, rowHeight);
                }

                var labelWidth = contentWidth - view.currentPos.x;
                if (view.DrawButton(
                    getLabel(node), labelWidth, rowHeight, true,
                    getLabelColor(node), GUIView.gsLabel))
                {
                    onSelected(node);
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 展開状態を切り替える。行の描画ループから呼ばれるため、ここでは行を組み直さない
        /// (組み直すと反復中のリストが縮んで添字が範囲外になる)。次フレームの BuildRows で反映される
        /// </summary>
        private void ToggleExpanded(int id)
        {
            if (!_expanded.Remove(id))
            {
                _expanded.Add(id);
            }
            _rowsDirty = true;
        }

        /// <summary>予約された行がスクロール範囲外なら、見える位置まで送る</summary>
        private void ApplyScrollToRow(GUIView view, float viewHeight)
        {
            if (_scrollToRow < 0)
            {
                return;
            }

            var top = _scrollToRow * rowHeight;
            var bottom = top + rowHeight;

            var scrollPosition = view.scrollPosition;
            if (scrollPosition.y > top)
            {
                scrollPosition.y = top;
            }
            else if (scrollPosition.y + viewHeight < bottom)
            {
                scrollPosition.y = bottom - viewHeight;
            }
            view.scrollPosition = scrollPosition;

            _scrollToRow = -1;
        }

        // ---- キーボード操作 ----

        /// <summary>
        /// 矢印キーで選択行を移動する (← 折りたたみ/親へ、→ 展開/子へ)。
        /// 使いたいウィンドウだけが描画前に呼ぶ。
        /// どこかの入力欄が編集中ならキャレット移動を優先して何もしない。
        /// 自窓の検索欄だけでなく他窓の数値入力も対象にする必要があるため、
        /// コントロール名ではなく「キーボードフォーカスを持つコントロールの有無」で判定する
        /// (GUIView の入力欄はコントロール名を設定しないため名前では判別できない)
        /// </summary>
        public void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
            {
                return;
            }

            EnsureRows();

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    break;
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    break;
                case KeyCode.RightArrow:
                    ExpandOrSelectChild();
                    break;
                case KeyCode.LeftArrow:
                    CollapseOrSelectParent();
                    break;
                default:
                    return;
            }

            e.Use();
        }

        /// <summary>現在の選択が表示行の何番目か。選択なし・非表示なら -1</summary>
        private int GetSelectedRowIndex()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (isSelected(_rows[i].node))
                {
                    return i;
                }
            }
            return -1;
        }

        private void MoveSelection(int delta)
        {
            if (_rows.Count == 0)
            {
                return;
            }

            var index = GetSelectedRowIndex();
            // 未選択・折りたたまれて見えない場合は端から始める
            var next = index < 0
                ? (delta > 0 ? 0 : _rows.Count - 1)
                : Mathf.Clamp(index + delta, 0, _rows.Count - 1);

            SelectRow(next);
        }

        /// <summary>→: 折りたたまれていれば展開し、展開済みなら最初の子へ移る</summary>
        private void ExpandOrSelectChild()
        {
            var index = GetSelectedRowIndex();
            if (index < 0)
            {
                MoveSelection(1);
                return;
            }

            var node = _rows[index].node;
            if (!isAlive(node) || getChildCount(node) == 0)
            {
                return;
            }

            if (_expanded.Add(getId(node)))
            {
                // 展開結果は次フレームの BuildRows で反映する (ToggleExpanded と同じ理由)
                _rowsDirty = true;
                return;
            }

            // 展開済みなら直後の行が最初の子になる
            if (index + 1 < _rows.Count)
            {
                SelectRow(index + 1);
            }
        }

        /// <summary>←: 展開済みなら折りたたみ、そうでなければ親へ移る</summary>
        private void CollapseOrSelectParent()
        {
            var index = GetSelectedRowIndex();
            if (index < 0)
            {
                return;
            }

            var node = _rows[index].node;
            if (!isAlive(node))
            {
                return;
            }

            if (_expanded.Remove(getId(node)))
            {
                _rowsDirty = true;
                return;
            }

            // 親は「自分より浅い深さで直前に現れる行」。
            // 親を型で辿らずに済むので、ノード型に親参照が無くても動く
            var depth = _rows[index].depth;
            for (var i = index - 1; i >= 0; i--)
            {
                if (_rows[i].depth < depth)
                {
                    SelectRow(i);
                    return;
                }
            }
        }

        private void SelectRow(int index)
        {
            onSelected(_rows[index].node);
            _scrollToRow = index;
        }
    }
}
```

- [ ] **Step 2: ファイルの形式を確認する**

BOM なし UTF-8 / CRLF であることを確認する。

Run:
```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils"
file GUITreeView.cs
```
Expected: `UTF-8 Unicode text, with CRLF line terminators`（`with BOM` が出たら BOM を除去する）

- [ ] **Step 3: ビルドして通ることを確認する**

Run:
```bash
cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug" > "$TEMP/t1.log" 2>&1
grep -cE "error CS" "$TEMP/t1.log"
grep -E "ビルドに成功|個の警告|エラー$" "$TEMP/t1.log"
```
Expected: `error CS` が 0 件、`ビルドに成功しました` が 2 回、`0 エラー` が 2 回

- [ ] **Step 4: submodule にコミットする**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils"
git add GUITreeView.cs
git commit -m "feat(guitreeview): 汎用ツリービュー部品を追加する"
```

- [ ] **Step 5: 親リポの submodule ポインタを更新してコミットする**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(mteutils): submodule を GUITreeView 追加版へ更新する"
```

---

## Task 2: BoneEditWindow を GUITreeView に載せ替える

キーボード操作を持たない分こちらが単純なので、先に載せ替えて API を検証する。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `GUITreeView<T>` 全公開 API
- Produces: なし（このウィンドウ内で閉じる）

- [ ] **Step 1: 行まわりのフィールドを GUITreeView 1 本に置き換える**

`BoneEditWindow.cs` の `Row` 構造体（`private struct Row { public SlotBoneNode node; public int depth; }`）と、その下のフィールド群

```csharp
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _expanded = new HashSet<int>(); // Transform の GetInstanceID
        // _rows の組み直しが必要か。行の内容はボーンツリー・展開状態・検索語だけで決まるため、
        // この 3 つが変わったときにだけ立てればよい (OnGUI は 1 フレームに複数回走る)
        private bool _rowsDirty = true;
        // 組み直し済みのボーンツリー。BoneEditManager.GetCurrentBoneTree() はスロット obj が
        // 変わらない限り同一インスタンスを返すため、参照比較でツリー変化を検出できる
        private List<SlotBoneNode> _lastTree = null;
        private string _searchText = "";
        // 選択行を画面内へ送るスクロール量。次の描画で反映する
        private int _scrollToRow = -1;
        // 外部経路 (ビュー窓のボーンピック等) の選択変更検出用
        private Transform _lastSelectedBone;
        // 選択変更で表示したいボーン。行構築後に行位置を求めてスクロールする
        private Transform _pendingReveal;
```

を、次に置き換える。

```csharp
        private readonly GUITreeView<SlotBoneNode> _treeView = new GUITreeView<SlotBoneNode>();
        // 外部経路 (ビュー窓のボーンピック等) の選択変更検出用
        private Transform _lastSelectedBone;
        // ラベル生成デリゲートから参照する、描画中のメイドと編集ストア。
        // GUITreeView にはゲーム固有の型を渡せないため、描画直前にここへ置いてから使う。
        //
        // 【前提】これが有効なのは _treeView.Draw() の実行中だけ。
        // 現状 onSelected は Draw() の中からしか発火しないため成立している。
        // このウィンドウに _treeView.HandleKeyboard() を足す場合は、描画外から
        // onSelected が飛んできて前フレームの値を掴む経路が生まれるため、
        // ここをフィールド経由ではなく引数渡しに変える必要がある
        private Maid _drawingTarget;
        private BoneEditStore _drawingStore;
```

- [ ] **Step 2: GUITreeView の初期化メソッドを追加する**

`private static BoneEditManager boneEditManager => BoneEditManager.instance;` の直後に、次のメソッドを追加する。

```csharp
        /// <summary>
        /// ツリービューにボーンツリーのたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupTreeView()
        {
            _treeView.rowHeight = ROW_HEIGHT;
            _treeView.indentWidth = IndentWidth;
            _treeView.toggleWidth = ToggleWidth;
            _treeView.scrollBarWidth = ScrollBarWidth;

            // ID は Transform のものを使う。ビュー窓のピックで飛んでくるのも Transform のため、
            // Reveal / Expand と突き合わせるにはこれで揃えておく必要がある
            _treeView.getId = node => node.transform.GetInstanceID();
            _treeView.getName = node => node.name;
            _treeView.isAlive = node => node.transform != null;
            _treeView.getChildCount = node => node.children.Count;
            _treeView.getChild = (node, i) => node.children[i];

            _treeView.getLabel = node =>
            {
                var isEdited = _drawingStore != null &&
                    _drawingStore.GetEntry(boneEditManager.targetSlotName, node.name) != null;
                return isEdited ? node.name + " *" : node.name;
            };
            _treeView.getLabelColor = node =>
                node.transform == boneEditManager.selectedBone ? Color.cyan : Color.white;
            _treeView.isSelected = node => node.transform == boneEditManager.selectedBone;
            _treeView.onSelected = node =>
            {
                // 編集 UI は Inspector 側に出すため、選択を Inspector にも反映する
                boneEditManager.SelectBone(_drawingTarget, node.transform);
                _lastSelectedBone = node.transform;
            };
        }
```

- [ ] **Step 3: コンストラクタから初期化を呼ぶ**

```csharp
        private BoneEditWindow()
        {
        }
```
を
```csharp
        private BoneEditWindow()
        {
            SetupTreeView();
        }
```
に変更する。

- [ ] **Step 4: `DrawBoneTree` を委譲に書き換える**

`DrawBoneTree` メソッド全体（`/// <summary>` コメントから `}` まで）を次に置き換える。

```csharp
        /// <summary>
        /// ボーンツリー。行まわりは GUITreeView に委譲し、
        /// ここではスロットのツリーを渡すことと検索欄の配置だけを行う
        /// </summary>
        private void DrawBoneTree(Maid target)
        {
            var tree = boneEditManager.GetCurrentBoneTree();
            if (tree.Count == 0)
            {
                view.DrawLabel("ボーンがありません", -1, ROW_HEIGHT);
                return;
            }

            // GetCurrentBoneTree() はスロット obj が変わらない限り同じインスタンスを返すため、
            // SetRoots の参照比較だけでツリーの作り直しを検出できる
            _treeView.SetRoots(tree);

            // 展開状態を確定させてから描く
            DetectExternalSelection();

            view.DrawTextField(_treeView.searchText, -1, ROW_HEIGHT,
                value => _treeView.searchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // ラベル生成から参照するため、描画に入る前に置いておく
            _drawingTarget = target;
            _drawingStore = boneEditManager.GetStore(target);

            _treeView.Draw(view, view.GetDrawRect(-1, -1));
        }
```

- [ ] **Step 5: `DetectExternalSelection` をツリービュー API に合わせる**

`DetectExternalSelection` メソッド全体を次に置き換える。

```csharp
        /// <summary>
        /// ビュー窓のボーンピック等、外部経路の選択変更を検出して祖先を展開する。
        /// 行位置は行構築後でないと確定しないため、ここでは表示予約だけしておく
        /// </summary>
        private void DetectExternalSelection()
        {
            var selected = boneEditManager.selectedBone;
            if (selected == _lastSelectedBone)
            {
                return;
            }
            _lastSelectedBone = selected;

            if (selected == null)
            {
                return;
            }

            _treeView.Reveal(selected.GetInstanceID());

            // ツリー外の Transform が混ざっても展開集合に余分な ID が入るだけで害はない
            for (var parent = selected.parent; parent != null; parent = parent.parent)
            {
                _treeView.Expand(parent.GetInstanceID());
            }
        }
```

- [ ] **Step 6: 不要になったメソッドを削除する**

次のメソッドを丸ごと削除する（すべて `GUITreeView` 側へ移った）。

- `ResolvePendingReveal()`
- `BuildRows(List<SlotBoneNode> tree)`
- `AddRows(SlotBoneNode node, int depth, bool searching)`
- `DrawRow(Maid target, BoneEditStore store, Row row, int index, float contentWidth)`
- `ToggleExpanded(Transform transform)`
- `ApplyScrollToRow(float viewHeight)`

- [ ] **Step 7: 未使用の using を整理する**

削除後に `System`（`StringComparison` 用）が未使用になっていれば `using System;` を削除する。`System.Collections.Generic`（`List<>`）が他で使われていなければそちらも削除する。ビルド警告は出ないため、ファイル内を検索して確認すること。

Run:
```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin"
grep -n "StringComparison\|List<\|Dictionary<\|IEnumerable<" BoneEditWindow.cs
```
Expected: ヒットが無い using は削除する

- [ ] **Step 8: ビルドして通ることを確認する**

Run:
```bash
cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug" > "$TEMP/t2.log" 2>&1
grep -cE "error CS" "$TEMP/t2.log"
grep -E "ビルドに成功|個の警告|エラー$" "$TEMP/t2.log"
```
Expected: `error CS` が 0 件、`ビルドに成功しました` が 2 回、`0 エラー` が 2 回

- [ ] **Step 9: 実機で確認する**

ゲームを再起動してプラグインを読み込ませたうえで、ボーンウィンドウを開き以下を確認する。デプロイはゲーム停止中に `deploy.bat` を実行する。MCP `com3d25-devbridge` の `ping` でゲームの生存を、`tail_log` で例外が出ていないことを確認する。

確認項目:
1. ボーン一覧が表示され、`+` / `-` で展開・折りたたみができる
2. 検索欄に文字を入れると一致するボーンだけがフラット表示される。消すと元の階層に戻る
3. ボーン名をクリックすると選択され、シアン色になり Inspector に編集 UI が出る
4. ビュー窓で骨格線の関節をクリックすると、対応する行が展開されて画面内へスクロールしてくる
5. 編集済みボーンに `*` が付く
6. スロットを切り替えるとツリーが差し替わる
7. 大量のボーンを展開してもフレームレートが落ちない

Run（例外が出ていないことの確認）:
```
mcp__com3d25-devbridge__tail_log
```
Expected: `GUITreeView` / `BoneEditWindow` 由来の例外が無いこと

- [ ] **Step 10: コミットする**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
git add source/COM3D2.SceneEditor.Plugin/BoneEditWindow.cs
git commit -m "refactor(bone): ボーン一覧の行 UI を GUITreeView へ委譲する"
```

---

## Task 3: HierarchyWindow を GUITreeView に載せ替える

矢印キー操作を含む分こちらが複雑。Task 2 で API が検証済みであることを前提にする。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `GUITreeView<T>` 全公開 API（`HandleKeyboard()` を含む）
- Produces: なし

- [ ] **Step 1: 行まわりのフィールドを GUITreeView 1 本に置き換える**

`Row` 構造体（`private struct Row { public GameObject go; public int depth; }`）と、次のフィールド群

```csharp
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _expanded = new HashSet<int>(); // GetInstanceID
        // _rows の組み直しが必要か。ルート再取得と、展開状態の変化
        // (検索語による絞り込みや、選択に伴う祖先の自動展開を含む) で立てる
        private bool _rowsDirty = true;
```

を、次に置き換える（`_roots` は GUITreeView に渡すため残す）。

```csharp
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly GUITreeView<GameObject> _treeView = new GUITreeView<GameObject>();
```

`_searchText` / `_scrollToRow` / `_pendingReveal` の 3 フィールドも削除する（すべて GUITreeView 側に移る）。`_lastClickedGo` / `_lastClickTime`（ダブルクリック判定）と `_lastRefreshTime`、DontDestroyOnLoad の probe 関連フィールドは残す。

- [ ] **Step 2: GUITreeView の初期化メソッドを追加する**

`private static SelectionManager selectionManager => SelectionManager.instance;` の直後に追加する。

```csharp
        /// <summary>
        /// ツリービューにシーン階層のたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupTreeView()
        {
            _treeView.rowHeight = RowHeight;
            _treeView.indentWidth = IndentWidth;
            _treeView.toggleWidth = ToggleWidth;
            _treeView.scrollBarWidth = ScrollBarWidth;

            _treeView.getId = go => go.GetInstanceID();
            _treeView.getName = go => go.name;
            _treeView.isAlive = go => go != null;
            _treeView.getChildCount = go => go.transform.childCount;
            _treeView.getChild = (go, i) => go.transform.GetChild(i).gameObject;

            _treeView.getLabel = go => go.activeInHierarchy ? go.name : go.name + " (無効)";
            _treeView.getLabelColor = go =>
                selectionManager.selectedObject == go ? ACCENT_COLOR : Color.white;
            _treeView.isSelected = go => selectionManager.selectedObject == go;
            _treeView.onSelected = go =>
            {
                selectionManager.Select(go);
                OnRowClicked(go);
            };

            _treeView.SetRoots(_roots);
        }
```

- [ ] **Step 3: コンストラクタから初期化を呼ぶ**

```csharp
        private HierarchyWindow()
        {
        }
```
を
```csharp
        private HierarchyWindow()
        {
            SetupTreeView();
        }
```
に変更する。

- [ ] **Step 4: `OnSelectionChanged` をツリービュー API に合わせる**

`OnSelectionChanged` メソッド全体を次に置き換える。

```csharp
        /// <summary>
        /// SceneView / Inspector 等どの経路の選択でも、祖先を展開して行を画面内へ送る。
        /// 行位置は行構築後でないと確定しないため、ここでは表示予約だけしておく
        /// </summary>
        private void OnSelectionChanged(GameObject go)
        {
            if (go == null)
            {
                // 選択が外れたら予約も取り消す。残しておくと直前に選ばれていた行へ
                // 意図せずスクロールしてしまう
                _treeView.CancelReveal();
                return;
            }

            _treeView.Reveal(go.GetInstanceID());

            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                _treeView.Expand(parent.gameObject.GetInstanceID());
            }
        }
```

- [ ] **Step 5: `OnChangedSceneLevel` を書き換える**

```csharp
        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _roots.Clear();
            _treeView.Clear();
        }
```

（`Clear()` はルート参照を保持したままなので、`SetRoots` の呼び直しは不要。）

- [ ] **Step 6: `RefreshRoots` の末尾を書き換える**

`RefreshRoots` の末尾の `_rowsDirty = true;` を次に置き換える。

```csharp
            // _roots は同じリストの中身を入れ替えているため、参照比較では検出されない
            _treeView.SetDirty();
```

- [ ] **Step 7: `DrawContent` を委譲に書き換える**

`DrawContent` メソッド全体を次に置き換える。

```csharp
        /// <summary>
        /// 行まわりは GUITreeView に委譲し、
        /// ここでは検索欄・更新ボタンの配置と矢印キー操作の有効化だけを行う
        /// </summary>
        protected override void DrawContent()
        {
            _treeView.HandleKeyboard();

            _view.Init(ToLocalRect(contentRect));

            // 検索欄 + 手動更新ボタン
            _view.BeginHorizontal();
            {
                var searchWidth = _view.viewRect.width - SearchButtonWidth - Spacing;
                _view.DrawTextField(_treeView.searchText, searchWidth, RowHeight,
                    value => _treeView.searchText = value);

                if (_view.DrawButton("更新", SearchButtonWidth, RowHeight))
                {
                    RefreshRoots();
                }
            }
            _view.EndLayout();

            // 残りの領域すべてをリストに使う
            _treeView.Draw(_view, _view.GetDrawRect(-1, -1));
        }
```

- [ ] **Step 8: 不要になったメソッドを削除する**

次のメソッドを丸ごと削除する（すべて `GUITreeView` 側へ移った）。

- `ResolvePendingReveal()`
- `BuildRows()`
- `AddRows(GameObject go, int depth, bool searching)`
- `DrawRow(Row row, int index, float contentWidth)`
- `HandleKeyboard()`
- `GetSelectedRowIndex()`
- `MoveSelection(int delta)`
- `ExpandOrSelectChild()`
- `CollapseOrSelectParent()`
- `ToggleExpanded(GameObject go)`
- `SelectRow(int index)`
- `ApplyScrollToRow(float viewHeight)`

`OnRowClicked(GameObject go)`（ダブルクリック判定）は**残す**。

- [ ] **Step 9: クラスの doc コメントを実態に合わせる**

クラス冒頭の `/// <summary>` を次に置き換える。

```csharp
    /// <summary>
    /// シーン上の GameObject ツリーを表示するウィンドウ。
    /// 行まわり (展開/折りたたみ・検索・行仮想化・矢印キー移動) は GUITreeView に委譲し、
    /// ここではルート一覧の収集と、SelectionManager 経由の選択同期を担う。
    /// ルート一覧は一定間隔で取り直す。OnChangedSceneLevel ではシーン切替しか拾えず、
    /// シーン内で動的に生成・破棄されるルートはポーリングでしか追えないため
    /// </summary>
```

- [ ] **Step 10: 未使用の using を整理する**

Run:
```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin"
grep -n "StringComparison\|HashSet<" HierarchyWindow.cs
```
Expected: ヒットしなければ `using System;` を削除する（`List<GameObject>` があるため `System.Collections.Generic` は残る）

- [ ] **Step 11: ビルドして通ることを確認する**

Run:
```bash
cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug" > "$TEMP/t3.log" 2>&1
grep -cE "error CS" "$TEMP/t3.log"
grep -E "ビルドに成功|個の警告|エラー$" "$TEMP/t3.log"
```
Expected: `error CS` が 0 件、`ビルドに成功しました` が 2 回、`0 エラー` が 2 回

- [ ] **Step 12: 実機で確認する**

ゲームを再起動してプラグインを読み込ませたうえで、Hierarchy ウィンドウを開き以下を確認する。

確認項目:
1. シーンのルートが一覧に出る。非アクティブなオブジェクトは `(無効)` 付きで出る
2. `+` / `-` で展開・折りたたみができる
3. 検索欄に文字を入れると一致するものだけフラット表示される。消すと元の階層に戻る
4. 行をクリックすると選択され、Inspector に内容が出る。同じ行をすばやく 2 回クリックすると SceneView のカメラがそのオブジェクトへ寄る
5. **矢印キー**: ↑↓ で行を移動、→ で展開して子へ、← で折りたたんで親へ。選択行が画面外なら自動でスクロールしてくる
5b. **検索中の矢印キー**: 検索語を入れた状態で ↑↓ は動くが、← では親へ移動しない（「意図的な仕様変更」1 のとおり。移動したら実装が計画と違う）
5c. **選択解除**: 行を選んだ直後に SceneView の何もない場所をクリックして選択を外し、リストが勝手にスクロールしないこと
6. Inspector や検索欄に文字を入力しているときは、矢印キーがキャレット移動として働き行移動しない
7. SceneView でオブジェクトをクリックすると、Hierarchy 側で祖先が展開されてその行までスクロールしてくる
8. メイドのボーン階層など大量ノードを展開してもフレームレートが落ちない
9. シーンを切り替えても一覧が壊れない

Run（例外が出ていないことの確認）:
```
mcp__com3d25-devbridge__tail_log
```
Expected: `GUITreeView` / `HierarchyWindow` 由来の例外が無いこと

- [ ] **Step 13: コミットする**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
git add source/COM3D2.SceneEditor.Plugin/HierarchyWindow.cs
git commit -m "refactor(hierarchy): シーン階層の行 UI を GUITreeView へ委譲する"
```

---

## Task 4: 仕上げのコードレビュー

**Files:**
- Review: `MTEUtils/GUITreeView.cs`, `BoneEditWindow.cs`, `HierarchyWindow.cs`

- [ ] **Step 1: code-review スキルでレビューする**

`code-review` スキルを起動し、Task 1〜3 の全変更（submodule と親リポの両方）を対象にレビューする。特に次を観点として渡す。

- デリゲート未設定のまま `Draw` を呼んだ場合の `NullReferenceException` 耐性
- `Clear()` が `_roots` 参照も捨てる仕様と、利用側の渡し直し漏れ
- `CollapseOrSelectParent` の「深さで親を探す」方式が、検索中（全 depth が 0）に意図通り何もしないこと
- Hierarchy / BoneEdit で挙動が変わっていないか（特にスクロール追従とダブルクリック）

- [ ] **Step 2: 妥当な指摘を反映し、再ビルドする**

Run:
```bash
cmd //c "W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\build.bat debug" > "$TEMP/t4.log" 2>&1
grep -cE "error CS" "$TEMP/t4.log"
```
Expected: 0

- [ ] **Step 3: コミットする**

```bash
cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
git status --short
```
変更が残っていれば `commit` スキルでコミットする。submodule 側に変更があれば submodule → 親リポのポインタ、の順でコミットする。

---

## レビュー却下メモ

plan-review（2026-08-16）で挙がったが、取り込まなかった指摘とその理由。

- （なし。全 7 件を取り込み済み）

## レビュー反映メモ

plan-review（2026-08-16、総合 🟡 軽微修正後承認・🔴 なし）で取り込んだ指摘:

1. `OnSelectionChanged(null)` で reveal 予約が取り消されない退行 → `CancelReveal()` を追加し呼ぶようにした
2. `CollapseOrSelectParent` の検索中の挙動変更が未明記 → 「意図的な仕様変更」節を新設し、実機確認項目 5b を追加
3. デリゲート未設定時の NRE → `ValidateDelegates()` を Task 1 に前倒しし、日本語メッセージで名指し通知
4. `Clear()` が `_roots` を捨てる危うさ → ルート参照を保持する仕様に変更し、Task 3 の呼び直しを削除
5. `_drawingTarget` / `_drawingStore` の stale リスク → 成立条件と、キーボード操作追加時に見直すべき旨をコメントに明記
6. 前提条件のコミット手順が submodule の 2 段階に触れていない → 具体的なコマンドを明記
7. `rowCount` が未使用（YAGNI） → 公開 API から削除
