# モデルの Inspector 表示の統一 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムラインのモデルレイヤー項目タップ時とモデル選択時の Inspector 表示を、1 つの共通レイアウト（ヘッダー行 + 管理行 + アタッチ + Transform + 委譲先の固有行）に揃える。

**Architecture:** SE の `ModelTransformItemInspectorBase<TModel>` にモデル 1 件分の共通描画 `DrawModel` を置き、タイムライン経路（`DrawItems`）と選択経路（新設 `TryDrawSelected`、`InspectorWindow` から呼ぶ）の両方で使う。ヘッダー行は `InspectorHeaderRowDrawer` へ切り出して通常オブジェクトと共有する。MIE 固有のレイヤー行は、`InspectorHost` の新 API `RegisterRows` / `DrawRows` で末尾へ足させる。

**Tech Stack:** C# (COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1)、Unity IMGUI（MTEUtils `GUIView`）、xUnit（SE テスト）

**Spec:** `docs/superpowers/specs/2026-09-25-model-inspector-unify-design.md`

## Global Constraints

- 両構成（`GameVersion=COM3D2` / `COM3D25`）でビルドが通ること。.NET 3.5 の `Func<>` / `Action<>` は入力 4 個まで
- SE のテストは、COM3D2 → COM3D25 の順にビルドしてから `dotnet test` する（テストは MSBuild の出力 DLL を参照する）
- テストで Unity のネイティブ呼び出しはしない（`Mathf` 以外の UnityEngine API、`GameObject` の生成など）。`Rect` の生成と `null` の `GameObject` は可
- ビルドは `debug.bat` ではなく MSBuild を直接叩く。Git Bash では `export MSYS2_ARG_CONV_EXCL="*"` が必要
- `deploy.bat` / `deploy.ps1` は実行しない。push もしない（MTEUtils を含む）
- コードのコメントとログメッセージは日本語
- `InspectorHost` の既存公開 API（`Register` / `Register2` / `Unregister` / `GetWindowRect` / `IsWindowVisible` / `DrawHeader`）のシグネチャは変えない。追加のみ
- プラグイン間連携はリフレクション経由（MTEUtils `InspectorHostClient`）。MIE から SE の型をコンパイル時に参照しない

SE ビルド + テスト（以下「SE ビルド」）:
```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

MIE ビルド（以下「MIE ビルド」。MIE にテストプロジェクトは無い）:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
export MSYS2_ARG_CONV_EXCL="*"; M="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$M" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo | grep -E " error |->"; done
```

## Review Focus

- 同名（`"ModItemExplorer"`）で全面委譲と行の両方が登録されうる（プラグインのリロード中など）。同名の再登録で、もう一方の種類の登録を消してはならない（Task 1 のテスト）
- 行の登録者が例外を投げ続ける。そのフレームは 0 高さで共通レイアウトを描き続け、5 回で打ち切って警告を出す。全面委譲の登録は巻き込まない（Task 1 のテスト）
- モデルの子オブジェクト（メッシュ・ボーン）を Hierarchy や SceneView で選ぶ。今までどおり、その子の既定表示（ヘッダー + Transform）になり、モデル本体の表示に化けない（Task 6 の実機確認）
- 選択中のモデルを共通レイアウトの「削除」で消す。次のフレームで `go == null` の表示へ移り、例外が出ない（Task 6 の実機確認）
- タイムライン未読込（`timeline == null`）の状態でモデルを選ぶ。共通レイアウトは出て、複製・アタッチを押しても例外にならない（`CopyModel` / `AutoKeyFrameAfterEdit` は timeline null で何もしない。Task 6 の実機確認）

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Manager/InspectorHost.cs` | 変更 | `RegisterRows` / `DrawRows` を追加。登録の種類ごとの置き換えと失敗の記録を共通化する |
| `source/COM3D2.SceneEditor.Plugin.Tests/InspectorHostRowsTests.cs` | 新規 | 行の委譲の単体テストと公開契約 |
| `source/COM3D2.SceneEditor.Plugin/InspectorHeaderRowDrawer.cs` | 新規 | ヘッダー行（トグル + 名前 + フォーカス）の描画 |
| `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs` | 変更 | ヘッダー行の委譲、選択経路でのモデル表示 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelTransformItemInspectorBase.cs` | 変更 | 共通レイアウト `DrawModel` と `TryDrawSelected` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelItemInspector.cs` | 変更 | 配置モデルのヘッダー行 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BGModelItemInspector.cs` | 変更 | 背景モデルのヘッダー行 |
| `source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs` | 変更 | 表示トグルをヘッダー行へ移す |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | `InspectorHeaderRowDrawer.cs` を登録 |
| `source/COM3D2.SceneEditor.Plugin/MTEUtils/InspectorHostClient.cs`（サブモジュール） | 変更 | `isRowsDrawAvailable` / `RegisterRows` |
| MIE `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs` | 変更 | `DrawRows`（レイヤー行だけ） |
| MIE `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs` | 変更 | 登録先の切り替え |
| `docs-site/dev/inspector-guest-guide.md` / `docs-site/timeline/inspector.md` | 変更 | ドキュメント |

---

### Task 1: InspectorHost に行の委譲 API を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/InspectorHost.cs`
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/InspectorHostRowsTests.cs`

**Interfaces:**
- Produces:
  - `public static object InspectorHost.RegisterRows(string name, Func<GameObject, bool> canDraw, Func<GameObject, Rect, float> drawRows)`
  - `public static float InspectorHost.DrawRows(GameObject go, Rect rect)`（ホスト内部用。テストから呼ぶため public）

テストプロジェクトは SDK 形式の既定 glob なので、csproj への登録は不要（確認済み）。

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/InspectorHostRowsTests.cs`:

```csharp
using System;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// InspectorHost の行の委譲 (RegisterRows / DrawRows) を固定する。
    /// 登録表は static なので、テストごとに一意な名前で登録し finally で解除する
    /// </summary>
    public class InspectorHostRowsTests
    {
        private static readonly Rect AnyRect = new Rect(0f, 0f, 100f, 0f);

        private static string UniqueName()
        {
            return "Test_" + Guid.NewGuid().ToString("N");
        }

        [Fact]
        public void 該当する行の登録者の高さを返す()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => 42f);
            try
            {
                Assert.Equal(42f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 該当者が居なければ0()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => false, (go, rect) => 42f);
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 負の高さは0に丸める()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => -5f);
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 行の登録はTryDrawに拾われない()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => 42f);
            try
            {
                bool headerDelegated;
                Assert.False(InspectorHost.TryDraw(null, AnyRect, AnyRect, out headerDelegated));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 全面委譲の登録はDrawRowsに拾われない()
        {
            var handle = InspectorHost.Register(UniqueName(), go => true, (go, rect) => { });
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 同名でも種類が違えば置き換えない()
        {
            var name = UniqueName();
            var drawCalled = false;
            var full = InspectorHost.Register(name, go => true, (go, rect) => drawCalled = true);
            var rows = InspectorHost.RegisterRows(name, go => true, (go, rect) => 7f);
            try
            {
                bool headerDelegated;
                Assert.True(InspectorHost.TryDraw(null, AnyRect, AnyRect, out headerDelegated));
                Assert.True(drawCalled);
                Assert.Equal(7f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(full);
                InspectorHost.Unregister(rows);
            }
        }

        [Fact]
        public void 同名同種の再登録は置き換える()
        {
            var name = UniqueName();
            var first = InspectorHost.RegisterRows(name, go => true, (go, rect) => 1f);
            var second = InspectorHost.RegisterRows(name, go => true, (go, rect) => 2f);
            try
            {
                Assert.Equal(2f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(first);
                InspectorHost.Unregister(second);
            }
        }

        [Fact]
        public void 例外は0を返し連続5回で打ち切る()
        {
            var calls = 0;
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) =>
            {
                calls++;
                throw new InvalidOperationException("テスト用の例外");
            });
            try
            {
                for (var i = 0; i < 7; i++)
                {
                    Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
                }
                Assert.Equal(5, calls);
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void RegisterRows_の公開シグネチャ()
        {
            // MTEUtils の InspectorHostClient は名前とシグネチャだけでリフレクション解決する
            var method = typeof(InspectorHost).GetMethod("RegisterRows",
                BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, "RegisterRows が public static で見つかりません");
            Assert.Equal(typeof(object), method.ReturnType);
            var parameters = method.GetParameters();
            Assert.Equal(3, parameters.Length);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
            Assert.Equal(typeof(Func<GameObject, bool>), parameters[1].ParameterType);
            Assert.Equal(typeof(Func<GameObject, Rect, float>), parameters[2].ParameterType);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter InspectorHostRowsTests`
Expected: `InspectorHost` に `RegisterRows` / `DrawRows` が無く、コンパイルエラー（CS0117）

- [ ] **Step 3: 実装する**

`Manager/InspectorHost.cs` の `Entry` に行の描画者を足す:

```csharp
        private class Entry
        {
            public string name;
            public Func<GameObject, bool> canDraw;
            /// <summary>内容を丸ごと描く委譲先。行の登録では null</summary>
            public Action<GameObject, Rect> draw;
            /// <summary>ホストが描く内容の末尾へ行を足す委譲先。全面委譲の登録では null</summary>
            public Func<GameObject, Rect, float> drawRows;
            /// <summary>委譲先が自前のスクロールビュー内で DrawHeader を呼ぶか</summary>
            public bool drawsHeader;
            /// <summary>連続で例外になった回数。成功したら 0 に戻す</summary>
            public int failureCount;

            public bool isRows => drawRows != null;
        }
```

`Register2` の本体を、共通の `AddEntry` 経由に直す（null チェックはそのまま）:

```csharp
            return AddEntry(new Entry
            {
                name = name ?? "",
                canDraw = canDraw,
                draw = draw,
                drawsHeader = drawsHeader,
            });
        }

        /// <summary>
        /// ホストが自前で描く内容 (現状は配置モデル・背景モデルの共通表示) の末尾へ、
        /// 委譲先に固有の行だけを足す登録 (後発 API)。
        /// drawRows は rect の左上から描き、使った高さ (末尾の余白を含まない) を返す。
        /// 全面委譲 (Register / Register2) とは別枠で、同名でも互いを置き換えない
        /// </summary>
        public static object RegisterRows(
            string name,
            Func<GameObject, bool> canDraw,
            Func<GameObject, Rect, float> drawRows)
        {
            if (canDraw == null || drawRows == null)
            {
                MTEUtils.LogError("InspectorHost.RegisterRows: デリゲートに null は指定できません");
                return null;
            }

            return AddEntry(new Entry
            {
                name = name ?? "",
                canDraw = canDraw,
                drawRows = drawRows,
            });
        }

        /// <summary>
        /// 同名・同種の既存登録はプラグインのリロードとみなして置き換える。
        /// 種類が違えば残す (同じプラグインが全面委譲と行を併用しても消し合わない)
        /// </summary>
        private static object AddEntry(Entry entry)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].name == entry.name && _entries[i].isRows == entry.isRows)
                {
                    Unregister(_entries[i]);
                }
            }

            _entries.Add(entry);
            return entry;
        }
```

（`Register2` にあった置き換えループと `_entries.Add` は `AddEntry` へ移したので消す。）

`TryDraw` のループ先頭で行の登録を飛ばし、例外処理を `RecordFailure` へ寄せる:

```csharp
            foreach (var entry in _entries)
            {
                if (entry.isRows || entry.failureCount >= MaxConsecutiveFailures)
                {
                    continue;
                }

                try
                {
                    if (!entry.canDraw(go))
                    {
                        continue;
                    }
                    entry.draw(go, entry.drawsHeader ? fullContentRect : contentRect);
                    entry.failureCount = 0;
                    headerDelegated = entry.drawsHeader;
                    return true;
                }
                catch (Exception e)
                {
                    RecordFailure(entry, e);
                }
            }
            return false;
        }

        /// <summary>
        /// 選択オブジェクトを管理下に持つ行の登録者が居れば、rect の位置へ行を描かせて
        /// 使った高さを返す。居なければ 0。最初に canDraw が true を返した 1 者だけを呼ぶ。
        /// 呼び出し元は戻り値の高さぶんレイアウトを送る (DrawHeader と同じ作法)。
        /// 例外の扱いは TryDraw と同じ
        /// </summary>
        public static float DrawRows(GameObject go, Rect rect)
        {
            foreach (var entry in _entries)
            {
                if (!entry.isRows || entry.failureCount >= MaxConsecutiveFailures)
                {
                    continue;
                }

                try
                {
                    if (!entry.canDraw(go))
                    {
                        continue;
                    }
                    var height = entry.drawRows(go, rect);
                    entry.failureCount = 0;
                    return Math.Max(0f, height);
                }
                catch (Exception e)
                {
                    RecordFailure(entry, e);
                }
            }
            return 0f;
        }

        /// <summary>外部プラグインの例外でホストの描画を止めない。連続失敗が続く登録者は打ち切る</summary>
        private static void RecordFailure(Entry entry, Exception e)
        {
            MTEUtils.LogException(e);
            if (++entry.failureCount >= MaxConsecutiveFailures)
            {
                MTEUtils.LogWarning("InspectorHost: {0} の描画が連続で失敗したため委譲を停止します", entry.name);
            }
        }
```

クラス冒頭の `/// <summary>` にある「契約はプリミティブ + UnityEngine 型 + デリゲートのみ」はそのまま残す。

- [ ] **Step 4: テストが通ることを確かめる**

Run: SE ビルド（最後の `dotnet test` を `--filter InspectorHostRowsTests` 付きで 1 回、無しで 1 回）
Expected: 両構成のビルドがエラーなし。`InspectorHostRowsTests` の 9 件と既存テストが PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/InspectorHost.cs source/COM3D2.SceneEditor.Plugin.Tests/InspectorHostRowsTests.cs
git commit -m "feat(inspector): 委譲先に固有の行だけを足す InspectorHost.RegisterRows を追加する"
```

---

### Task 2: ヘッダー行を InspectorHeaderRowDrawer へ切り出す

振る舞いを変えないリファクタリング。Task 3 でモデルのヘッダー行に使う。

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/InspectorHeaderRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs:22-25`（定数）, `:644-671`（`DrawHeader`）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `public static void InspectorHeaderRowDrawer.Draw(GUIView view, bool active, string label, float rowHeight, Action<bool> onActiveChanged, GameObject focusTarget)`

- [ ] **Step 1: ドロワーを作る**

`source/COM3D2.SceneEditor.Plugin/InspectorHeaderRowDrawer.cs`:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector のヘッダー行 (表示トグル + 名前 + 右端のフォーカスボタン)。
    /// 通常オブジェクトとモデルで同じ見た目にするため、トグルの意味と名前は呼び出し側が決める
    /// </summary>
    public static class InspectorHeaderRowDrawer
    {
        // トグルとフォーカスボタンの幅 (どちらも正方形)
        private const float ToggleWidth = 20f;
        private const float FocusButtonWidth = 20f;

        public static void Draw(
            GUIView view,
            bool active,
            string label,
            float rowHeight,
            Action<bool> onActiveChanged,
            GameObject focusTarget)
        {
            // 名前ラベルを自動幅にするとフォーカスボタンが右端からはみ出すため、
            // 残り幅を明示計算して割り当てる。
            // margin は NextElement が要素ごとに加算するため、要素数ぶん引く
            var labelWidth = view.viewRect.width - view.padding.x * 2
                - (ToggleWidth + view.margin)
                - (FocusButtonWidth + view.margin)
                - view.margin;

            view.BeginHorizontal();
            {
                view.DrawToggle(active, ToggleWidth, rowHeight, onActiveChanged);
                view.DrawLabel(label, labelWidth, rowHeight);

                var focusIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Focus);
                if (view.DrawTextureButton(focusIcon, FocusButtonWidth, rowHeight, 4f, tooltip: "フォーカス"))
                {
                    // 明示的なフォーカス要求なのでオートフォーカス設定に関わらず寄せる
                    SceneViewWindow.instance.FocusOn(focusTarget, true);
                }
            }
            view.EndLayout();
        }
    }
}
```

（`GUIView.DrawToggle(bool value, float width, float height, Action<bool> onChanged)` のオーバーロードが存在することは確認済み。）

- [ ] **Step 2: csproj へ登録する**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="InspectorWindow.cs" />` の直前に次を足す:
```xml
    <Compile Include="InspectorHeaderRowDrawer.cs" />
```

- [ ] **Step 3: InspectorWindow.DrawHeader を置き換える**

`InspectorWindow.cs` の `DrawHeader` 全体を次にする:

```csharp
        private void DrawHeader(GUIView view, GameObject go)
        {
            InspectorHeaderRowDrawer.Draw(view, go.activeSelf, go.name, RowHeight, value =>
            {
                ObjectTransformRowDrawer.RecordEdit(go);
                go.SetActive(value);
            }, go);
        }
```

`HeaderToggleWidth` は使われなくなるので、定数宣言を消す。`HeaderFocusButtonWidth` はボーン行（`DrawBoneContent` / `DrawSlotBoneHeader` / `DrawBoneFocusButton`）で使い続けるので残す。コメント「ヘッダー行のアクティブトグルとフォーカスボタンの幅」は「ボーン行のフォーカスボタンの幅 (正方形。InspectorHeaderRowDrawer と同じ)」に直す。

- [ ] **Step 4: ビルドとテスト**

Run: SE ビルド
Expected: 両構成ともエラーなし、テストはすべて PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/InspectorHeaderRowDrawer.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "refactor(inspector): ヘッダー行の描画を InspectorHeaderRowDrawer へ切り出す"
```

---

### Task 3: モデルの共通レイアウトを作り、タップ時と選択時の両方で使う

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelTransformItemInspectorBase.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BGModelItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs:44-60`
- Modify: `source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs`（選択経路の既定描画ブロック）

**Interfaces:**
- Consumes: `InspectorHeaderRowDrawer.Draw(...)`（Task 2）、`InspectorHost.DrawRows(GameObject, Rect)`（Task 1）
- Produces:
  - `public bool ModelTransformItemInspectorBase<TModel>.TryDrawSelected(GUIView view, GameObject selected)`
  - `protected abstract void ModelTransformItemInspectorBase<TModel>.DrawModelHeaderRow(GUIView view, TModel model)`
  - `protected virtual void ModelTransformItemInspectorBase<TModel>.PruneCaches(IList<string> names)`（引数型を `IList<IBoneMenuItem>` から変更）
  - `public static void ModelManageRowDrawer.DrawHeaderRow(GUIView view, MTEP.StudioModelStat model)`

IMGUI の描画なので単体テストは書けない（`GUIView` がネイティブ呼び出しを含む）。確認は Task 6 の実機で行う。

- [ ] **Step 1: 基底クラスに共通レイアウトを置く**

`ModelTransformItemInspectorBase.cs` のクラス冒頭の `<summary>` を次に直す:

```csharp
    /// <summary>
    /// モデルの Transform をキー化するレイヤー共通のプロバイダ。
    /// モデル 1 件分の表示 (ヘッダー行・管理行・Transform・委譲先の固有行) を持ち、
    /// タイムラインのメニュー項目選択と、Inspector でのモデル本体の選択の両方で同じものを描く。
    /// モデル一覧の持ち主 (配置モデル / 背景モデル) だけが派生先で変わる。
    ///
    /// 型引数を取るのは COM3D2 構成 (.NET 3.5) に IEnumerable&lt;T&gt; の共変性が無く、
    /// List&lt;StudioModelStat&gt; を List&lt;IModelStat&gt; として受け取れないため
    /// </summary>
```

フィールドに描画済みの名前の一覧を足す:

```csharp
        /// <summary>このフレームに描いたモデル名。キャッシュの掃除に使う</summary>
        private readonly List<string> _drawnNames = new List<string>();
```

`DrawModelManageRows` の前に抽象メソッドを足し、`PruneCaches` の引数を名前の一覧にする:

```csharp
        /// <summary>
        /// 表示トグル + 名前 + フォーカスのヘッダー行。トグルが書く先 (表示の持ち方) が
        /// モデルの種類ごとに違うため派生先が描く。複数選択時はモデルごとの見出しを兼ねる
        /// </summary>
        protected abstract void DrawModelHeaderRow(GUIView view, TModel model);
```

```csharp
        /// <summary>描かなかったモデルのキャッシュを捨てる (派生先が持つ分)</summary>
        protected virtual void PruneCaches(IList<string> names)
        {
        }
```

`DrawItems` を次に置き換え、`TryDrawSelected` と私用メソッドを足す:

```csharp
        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            _drawnNames.Clear();
            foreach (var item in items)
            {
                var model = FindModel(item.name);
                if (model == null || model.transform == null)
                {
                    // 一覧から消えた直後のメニュー項目 (削除・シーン切替) はここに来る
                    view.DrawLabel(item.displayName + " (モデルが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                DrawModel(view, model);
            }

            PruneAllCaches();
        }

        /// <summary>
        /// 選択中のオブジェクトがモデル本体なら、タイムラインのメニュー項目選択と
        /// 同じ表示を描いて true を返す。子オブジェクト (メッシュ・ボーン) は
        /// その子の Transform を個別に触れるよう、呼び出し側の既定表示に任せる
        /// </summary>
        public bool TryDrawSelected(GUIView view, GameObject selected)
        {
            var model = FindModelByObject(selected);
            if (model == null)
            {
                return false;
            }

            _drawnNames.Clear();
            DrawModel(view, model);
            PruneAllCaches();
            return true;
        }

        private void DrawModel(GUIView view, TModel model)
        {
            _drawnNames.Add(model.name);
            var go = model.transform.gameObject;

            DrawModelHeaderRow(view, model);
            DrawModelManageRows(view, model);
            _transformRowDrawers.Get(model.name).Draw(
                view, go, LabelWidth, ScaleLabelWidth, RowHeight);

            // 委譲先 (ModItemExplorer 等) に固有の行。ホスト側の別ビューで描くため、
            // こちらのレイアウトは返ってきた高さぶん自分で送る
            var rowsHeight = InspectorHost.DrawRows(go, view.GetDrawRect(-1, 0f));
            if (rowsHeight > 0f)
            {
                view.DrawEmpty(-1, rowsHeight);
            }
        }

        private TModel FindModelByObject(GameObject go)
        {
            if (go == null)
            {
                return default(TModel);
            }
            foreach (var model in models)
            {
                if (model.transform != null && model.transform.gameObject == go)
                {
                    return model;
                }
            }
            return default(TModel);
        }

        private void PruneAllCaches()
        {
            _transformRowDrawers.PruneExcept(_drawnNames);
            PruneCaches(_drawnNames);
        }
```

`FindItemName` はそのまま残す（祖先も辿る逆引きはメニュー同期の用途で、表示の判定とは別）。

`using UnityEngine;` は既にある。`GameObject` を使うので、追加の using は要らない。

- [ ] **Step 2: 配置モデルのヘッダー行と管理行**

`ModelManageRowDrawer.cs`
- 冒頭 `<summary>` の「(表示切替・プラグイン・複製・削除・アタッチ先)」を「(プラグイン・複製・削除・アタッチ先。表示切替はヘッダー行の DrawHeaderRow)」に直す
- `Draw` の中の「表示」トグル（`view.DrawToggle("表示", ...)` の 5 行）を消す
- `Draw` の前に次を足す:

```csharp
        /// <summary>
        /// 表示トグル + 表示名 + フォーカスのヘッダー行。
        /// 表示切替はレイヤーの書き戻し対象なので、値を書く直前に編集モードへ入る
        /// </summary>
        public static void DrawHeaderRow(GUIView view, MTEP.StudioModelStat model)
        {
            view.BeginAutoEditMode();
            InspectorHeaderRowDrawer.Draw(view, model.visible, model.displayName, RowHeight,
                newValue =>
                {
                    modelManager.SetModelVisible(model, newValue);
                    model.visible = newValue;
                },
                model.transform.gameObject);
            view.EndAutoEditMode();
        }
```

`Draw` 冒頭のコメント「複製・削除・表示切替はレイヤーの書き戻し対象なので」は「複製・削除・アタッチはレイヤーの書き戻し対象なので」に直す。

`ModelItemInspector.cs`:
- `<summary>` の 1 行目を「配置モデルレイヤー (ModelTimelineLayer) のメニュー項目・選択中のモデル本体 → モデルの管理操作と Transform 編集UI。」に直す
- 次を足し、`PruneCaches` の引数型を変える:

```csharp
        protected override void DrawModelHeaderRow(GUIView view, MTEP.StudioModelStat model)
        {
            ModelManageRowDrawer.DrawHeaderRow(view, model);
        }
```

```csharp
        protected override void PruneCaches(IList<string> names)
        {
            _manageRowDrawers.PruneExcept(names);
        }
```

- [ ] **Step 3: 背景モデルのヘッダー行**

`BGModelItemInspector.cs` の `DrawModelManageRows` の override を丸ごと、次に置き換える（管理行は基底の空実装に任せる）:

```csharp
        /// <summary>
        /// 背景モデルは配置数の増減を背景ウィンドウが持つため、管理行は出さずヘッダー行の表示切替だけにする
        /// </summary>
        protected override void DrawModelHeaderRow(GUIView view, MTEP.BGModelStat model)
        {
            InspectorHeaderRowDrawer.Draw(view, model.visible, model.displayName, RowHeight,
                newValue => { model.visible = newValue; },
                model.transform.gameObject);
        }
```

`<summary>` の 1 行目を「背景モデルレイヤー (BGModelTimelineLayer) のメニュー項目・選択中の背景モデル本体 → モデルの Transform 編集UI。」に直す。

- [ ] **Step 4: 選択経路でモデル表示を使う**

`InspectorWindow.cs` のフィールドに、`_objectTransformRowDrawer` の下へ次を足す:

```csharp
        /// <summary>
        /// モデル本体の選択時に、タイムラインのモデルレイヤー選択と同じ表示を描く。
        /// 行ドロワーの状態 (コンボの開閉・オイラー角キャッシュ) をタイムライン側と
        /// 混ぜないよう、登録済みのものとは別インスタンスを持つ
        /// </summary>
        private readonly ModelItemInspector _modelInspector = new ModelItemInspector();
        private readonly BGModelItemInspector _bgModelInspector = new BGModelItemInspector();
```

既定描画ブロック（`DrawMaidContent` の後）を次に置き換える:

```csharp
                var maid = go.GetComponent<Maid>();
                if (maid != null)
                {
                    DrawMaidContent(maid);
                }

                if (!TryDrawModel(go))
                {
                    DrawHeader(_view, go);

                    _objectTransformRowDrawer.Draw(
                        _view, go, LabelWidth, ScaleLabelWidth, RowHeight);

                    // PNG 配置は Transform に続けて固有パラメータも編集させる
                    PngPlacementInspector.Draw(_view, go);
                }

                _view.EndScrollView();
```

`DrawHeader` の前にメソッドを足す:

```csharp
        /// <summary>
        /// 配置モデル・背景モデルの本体なら、タイムラインのモデルレイヤー選択と同じ表示を描く。
        /// 全面委譲 (InspectorHost.TryDraw) より後に判定するので、旧版の委譲先はそちらが優先される
        /// </summary>
        private bool TryDrawModel(GameObject go)
        {
            return _modelInspector.TryDrawSelected(_view, go)
                || _bgModelInspector.TryDrawSelected(_view, go);
        }
```

クラス冒頭の `<summary>` の「通常オブジェクトは Transform を表示・編集する。」の後に「配置モデル・背景モデルの本体はタイムラインのモデルレイヤー選択と同じ表示にする。」を足す。

- [ ] **Step 5: ビルドとテスト**

Run: SE ビルド
Expected: 両構成ともエラーなし、テストはすべて PASS（`PruneCaches` の override 漏れがあれば CS0115 で落ちる）

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelTransformItemInspectorBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/ModelItemInspector.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BGModelItemInspector.cs source/COM3D2.SceneEditor.Plugin/ModelManageRowDrawer.cs source/COM3D2.SceneEditor.Plugin/InspectorWindow.cs
git commit -m "feat(inspector): モデルの選択時とタイムライン項目選択時の表示を共通レイアウトに揃える"
```

---

### Task 4: MTEUtils の InspectorHostClient に RegisterRows を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MTEUtils/InspectorHostClient.cs`（サブモジュール）

**Interfaces:**
- Consumes: `InspectorHost.RegisterRows`（Task 1。リフレクションで解決）
- Produces:
  - `public static bool InspectorHostClient.isRowsDrawAvailable`
  - `public static object InspectorHostClient.RegisterRows(string name, Func<GameObject, bool> canDraw, Func<GameObject, Rect, float> drawRows)`

- [ ] **Step 1: 実装する**

`InspectorHostClient.cs`
- クラス `<summary>` の 2 行目「登録すると、SceneEditor Inspector で対象オブジェクト選択時に内容描画が丸ごと委譲される。」の後に「RegisterRows で登録すると、ホストが描く内容の末尾へ固有の行だけを足せる。」を足す
- 以下を足す

デリゲート型とフィールド（`Register2Delegate` の後）:

```csharp
        private delegate object RegisterRowsDelegate(
            string name,
            Func<GameObject, bool> canDraw,
            Func<GameObject, Rect, float> drawRows);

        private static RegisterRowsDelegate _registerRows;
```

プロパティ（`isHeaderDrawAvailable` の後）:

```csharp
        /// <summary>
        /// ホストが描く内容の末尾へ固有の行だけを足せるか。
        /// 足せない旧バージョンの SceneEditor では、従来どおり Register で内容を丸ごと描く
        /// </summary>
        public static bool isRowsDrawAvailable
        {
            get
            {
                Initialize();
                return _registerRows != null;
            }
        }
```

`Initialize` の末尾の `InitializeHeaderDraw(type);` の後に `InitializeRowsDraw(type);` を足し、メソッドを追加する:

```csharp
        /// <summary>
        /// 行の委譲も後から足した API なので、無くても既存の登録は成立させる
        /// </summary>
        private static void InitializeRowsDraw(Type type)
        {
            try
            {
                var registerRows = type.GetMethod("RegisterRows", BindingFlags.Public | BindingFlags.Static);
                if (registerRows == null)
                {
                    return;
                }

                _registerRows = (RegisterRowsDelegate)Delegate.CreateDelegate(
                    typeof(RegisterRowsDelegate), registerRows);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("InspectorHostClient: 行の委譲 API の解決に失敗しました: " + e.Message);
                _registerRows = null;
            }
        }
```

`DrawHeader` の前に登録メソッドを足す:

```csharp
        /// <summary>
        /// ホストが自前で描く内容 (現状は配置モデル・背景モデル) の末尾へ、固有の行だけを足す登録。
        /// drawRows は rect の左上から描き、使った高さ (末尾の余白を含まない) を返す。
        /// 戻り値はハンドル (非対応の旧ホスト・失敗時は null)。解除は Unregister を使う
        /// </summary>
        public static object RegisterRows(
            string name,
            Func<GameObject, bool> canDraw,
            Func<GameObject, Rect, float> drawRows)
        {
            if (!isAvailable || !isRowsDrawAvailable)
            {
                return null;
            }

            try
            {
                return _registerRows(name, canDraw, drawRows);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("InspectorHostClient: InspectorHost への行の登録に失敗しました: " + e.Message);
                return null;
            }
        }
```

- [ ] **Step 2: ビルド**

Run: SE ビルド
Expected: 両構成ともエラーなし（SE は InspectorHostClient を呼ばないがコンパイル対象に入っている）

- [ ] **Step 3: サブモジュールでコミットし、SE の参照を進める**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git status --short   # InspectorHostClient.cs だけが変更されていること
git add InspectorHostClient.cs
git commit -m "feat(inspector): InspectorHostClient に固有行だけを足す RegisterRows を追加する"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore: MTEUtils を更新"
```

サブモジュールの push はしない（ユーザー判断）。

---

### Task 5: MIE を行の登録へ切り替える

**Files:**
- Modify（サブモジュール同期）: MIE `source/COM3D2.ModItemExplorer.Plugin/MTEUtils`
- Modify: MIE `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs`
- Modify: MIE `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs:531-556`

**Interfaces:**
- Consumes: `InspectorHostClient.isRowsDrawAvailable` / `RegisterRows(...)`（Task 4）
- Produces: `public float ModelInspectorDrawer.DrawRows(GameObject go, Rect rect)`

- [ ] **Step 1: MIE のサブモジュールを SE と同じコミットへ進める**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git status --short   # 未コミットの変更が無いこと
git fetch /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils HEAD
git merge --ff-only FETCH_HEAD
git log --oneline -1   # Task 4 のコミットになっていること
```

- [ ] **Step 2: DrawRows を足す**

`ModelInspectorDrawer.cs`
- クラス `<summary>` を次に直す:

```csharp
    /// <summary>
    /// SceneEditor Inspector へ委譲描画する MTE 管理モデルの内容。
    /// 行の委譲に対応したホストでは、共通のモデル表示 (ヘッダー・管理行・アタッチ・Transform) を
    /// ホストが描き、こちらは末尾のレイヤー行だけを足す (DrawRows)。
    /// 旧ホストでは内容を丸ごと描く (Draw)。Transform 行・アタッチ行は ModelOperationWindow と同じ部品で描く。
    /// アタッチのドロップダウンは MTE 側の ComboBoxPopupWindow が独立ウィンドウとして
    /// 出すため、ボタン座標をスクリーン座標へ直す基準として SceneEditor のウィンドウ矩形を借りる
    /// </summary>
```

- フィールドに、行専用のビューを足す:

```csharp
        /// <summary>
        /// DrawRows 用。ホストのスクロールビュー内の座標だけを共有する別ビューで、
        /// Draw 側の _view とは初期化のタイミングが違うため分ける
        /// </summary>
        private readonly GUIView _rowsView = new GUIView();
```

- `DrawLayerRow` をビュー引数付きに変え、`Draw` からの呼び出しを `DrawLayerRow(_view, model);` にする:

```csharp
        private void DrawLayerRow(GUIView view, StudioModelStatWrapper model)
        {
            ModelLayerRowDrawer.Draw(view, new ModelLayerRowOption
            {
                labelWidth = LabelWidth + 20,
                height = RowHeight,
                getLayerType = () => placer.GetLayerType(model),
                setLayerType = value => placer.SetLayerType(model, value),
            });
        }
```

- `Draw` の後に次を足す:

```csharp
        /// <summary>
        /// InspectorHost の drawRows。ホストが描く共通のモデル表示の末尾へ、
        /// ModItemExplorer 固有のレイヤー行だけを足す。戻り値は使った高さ (末尾の余白を含まない)
        /// </summary>
        public float DrawRows(GameObject go, Rect rect)
        {
            var model = placer.FindModelByGameObject(go);
            if (model == null)
            {
                return 0f;
            }

            // ホストが確保した矩形をそのまま使う (内側で二重に余白を取らない)
            _rowsView.padding = Vector2.zero;
            _rowsView.Init(rect);

            DrawLayerRow(_rowsView, model);

            // EndLayout 後の currentPos.y は最後の要素の下端 + margin なので、
            // ホストが余白を重ねないよう 1 個ぶん差し引いて返す
            return Mathf.Max(0f, _rowsView.currentPos.y - _rowsView.margin);
        }
```

`DrawAttachRow` は旧ホスト向けの `Draw` で使い続けるので残す。

- [ ] **Step 3: 登録先を切り替える**

`SelfModelPlacer.TryRegisterInspector` の `_inspectorHandle = InspectorHostClient.Register(...)` を次に置き換える:

```csharp
            if (InspectorHostClient.isRowsDrawAvailable)
            {
                // 共通のモデル表示 (ヘッダー・管理行・アタッチ・Transform) はホストが描く。
                // こちらは固有のレイヤー行だけを末尾へ足す
                _inspectorHandle = InspectorHostClient.RegisterRows(
                    "ModItemExplorer",
                    _inspectorDrawer.CanDraw,
                    _inspectorDrawer.DrawRows);
            }
            else
            {
                // 行の委譲に対応していない旧ホストへは、内容を丸ごと描く従来の登録にする
                _inspectorHandle = InspectorHostClient.Register(
                    "ModItemExplorer",
                    _inspectorDrawer.CanDraw,
                    _inspectorDrawer.Draw,
                    // ヘッダー行を自前のスクロールビュー内へ描き、内容と一緒にスクロールさせる。
                    // 対応していない旧ホストへは Register 側が従来どおりの登録へ倒す
                    drawsHeader: true);
            }
```

`isInspectorRegistered` の `<summary>` の末尾に「行の登録 (RegisterRows) でも true になる。その場合 Transform・アタッチはホストの共通表示に出る」を足す。

- [ ] **Step 4: ビルド**

Run: MIE ビルド
Expected: 両構成ともエラーなし

- [ ] **Step 5: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git commit -m "chore: MTEUtils を更新"
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(model): SceneEditor Inspector ではレイヤー行だけを足し、共通のモデル表示に揃える"
```

---

### Task 6: 実機で確認する

ゲームが起動していて devbridge が応答することが前提（`ping`）。SE と MIE の DLL は、ユーザーにゲームを再起動してもらって反映する。
- `debug.bat` はゲーム停止中だと実機へコピーする。コピーの実行はユーザーに任せる
- MonoBehaviour を含むためホットリロードはしない

操作は devbridge の `eval_csharp` でリフレクションを使う（`asm.GetType("COM3D2.SceneEditor.Plugin.SelectionManager")` の `Select(GameObject)` と、`TimelineManager.currentLayer.allMenuItems[i].SelectMenu(false)`）。表示の確認は `screenshot`（`window: "#8903352"` = Inspector）。

- [ ] **Step 1: MIE 配置モデル**
  - タイムラインでモデル項目をタップした画面と、モデル本体を選んだ画面を比べる。どちらも「ヘッダー行（トグル・表示名・フォーカス）/ プラグイン・複製・削除 / メイド・部位 / Transform / レイヤー」の順になること
  - レイヤーの Default ⇔ Charactor の切り替えが効くこと
- [ ] **Step 2: SE 配置モデル**: 同じ比較。レイヤー行は出ないこと
- [ ] **Step 3: 背景モデル**: 同じ比較。「ヘッダー行 / Transform」だけであること
- [ ] **Step 4: 操作**: ヘッダー行のトグルで表示が切り替わる（タイムライン読込中は編集モードへ入る）こと、フォーカスでモデルへ寄ること、アタッチの変更でモデルが付け替わること
- [ ] **Step 5: Review Focus の実機項目**
  - モデルの子オブジェクトを選ぶと既定表示になること
  - 共通レイアウトの「削除」で選択中のモデルを消しても例外が出ないこと（`tail_log`）
  - タイムライン未読込の状態でモデルを選んでも例外が出ないこと
- [ ] **Step 6: 複数選択と高さ**
  - タイムラインでモデル項目を 2 つ以上選ぶ。ヘッダー行が項目ごとに分かれ、各トグルが対応するモデルにだけ効くこと
  - Inspector を縮めてスクロールさせる。MIE のレイヤー行が末尾に収まり、Transform 行と重ならず、スクロールで最後まで見えること
- [ ] **Step 7: 結果を記録する**: 実機で確認できなかった項目があれば、その理由を最終報告に書く

---

### Task 7: ドキュメント

**Files:**
- Modify: `docs-site/dev/inspector-guest-guide.md`
- Modify: `docs-site/timeline/inspector.md:90`

- [ ] **Step 1: ゲストガイドに行の委譲を足す**

`inspector-guest-guide.md`
- 「できること / できないこと」の表で、「複数登録者による同一オブジェクトの分担描画」の行の前に次を足す:

```markdown
| ホストが描くモデル表示（配置モデル・背景モデル）の末尾へ固有の行だけを足す | ✅ `RegisterRows` で登録し、`drawRows` が使った高さを返す |
```

- 「A. MTEUtils を使う」節の、コンボのドロップダウンの段落の前に次を足す:

````markdown
配置モデル・背景モデルは、ホストが共通のモデル表示（ヘッダー・管理行・アタッチ・Transform）を描く。
その末尾へ固有の行だけを足したい場合は `RegisterRows` を使う:

```csharp
if (InspectorHostClient.isRowsDrawAvailable)
{
    _inspectorHandle = InspectorHostClient.RegisterRows(
        "MyPlugin",
        canDraw:  go => IsMyModel(go),
        drawRows: (go, rect) => DrawMyRows(go, rect));   // rect の左上から描き、使った高さを返す
}
else
{
    // 旧ホスト: 従来どおり Register で内容を丸ごと描く
}
```

`drawRows` は `DrawHeader` と同じく、ホストのスクロールビュー内の座標で自前の `GUIView`
（`padding = Vector2.zero` で `Init(rect)`）に描く。返す高さに末尾の余白は含めない。
全面委譲（`Register`）の登録者が居るオブジェクトではホストが共通表示を描かないため、`drawRows` は呼ばれない。
````

- 「B. 自前実装」節の公開 API の末尾に次を足す:

```csharp
object RegisterRows(                    // ホストの共通表示の末尾へ行を足す登録（後発 API）
    string name,                        // 同名・同種の再登録は置き換え。全面委譲の登録とは別枠
    Func<GameObject, bool> canDraw,
    Func<GameObject, Rect, float> drawRows);  // 戻り値は使った高さ（末尾の余白を含まない）
```

- 「挙動の詳細・注意点」に次を足す:

```markdown
- `RegisterRows` も後発 API。A の `isRowsDrawAvailable` で有無を判定する。
  判定の順は「全面委譲 → ホストの共通表示（+ 行の委譲）→ 既定描画」
```

- [ ] **Step 2: タイムラインの Inspector ドキュメントを直す**

`docs-site/timeline/inspector.md:90` の行を次にする:

```markdown
| モデル / モデルボーン / 背景 / 背景モデル | Transform（モデル・背景モデルはヘッダー行も付き、モデル本体を選んだときと同じ表示。モデルは管理操作行も） |
```

- [ ] **Step 3: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add docs-site/dev/inspector-guest-guide.md docs-site/timeline/inspector.md
git commit -m "docs(inspector): モデル表示の統一と RegisterRows を反映"
```
