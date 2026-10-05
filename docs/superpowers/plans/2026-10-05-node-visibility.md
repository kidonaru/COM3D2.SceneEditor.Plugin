# メイドのノード表示/非表示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task（このプロジェクトでは subagent-driven-development は使わない）. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ACCEx の「表示ノード選択」相当の機能を追加する。メイドの体の部位（body のボーン 90 件）を、ノードごとに非表示にしたり、menu が消したノードを強制表示したりできるようにし、タイムライン・シーンプリセット・Undo にも載せる。

**Architecture:**
- **状態の純粋ロジック**: `NodeVisibilityState` を作る。上書きの記録、上書き前の値の退避と復元、実際の表示判定を、スロットごとの `Dictionary<string,bool>`（`TBodySkin.m_dicDelNodeBody`）のリストだけを相手に行い、ゲーム外テストで固定する。
- **ゲームとの接続**: 静的クラス `MaidNodeVisibilityController` が担う。
  - メイドごとに `NodeVisibilityState` を持つ。
  - スロットの dict を集めて書き込み、`FixMaskFlag` → `FixVisibleFlag` をまとめて 1 回呼ぶ（`Flush`）。
  - 着替えの立ち上がりで退避値へ戻し、立ち下がりで取り直して再適用する。
- **周辺の対応**:
  - タイムライン: `NodeVisibilityTimelineLayer`、`TransformType.NodeVisibility`
  - シーンプリセット: `ScenePresetMaid.nodeVisibility`
  - Undo: `NodeVisibilitySnapshot`
  - UI: 脱衣ウィンドウの「ノード表示」タブ

**Tech Stack:** C#（.NET 3.5 / 4.7.1 の 2 構成）、IMGUI（MTEUtils の `GUIView`）、xUnit（ゲーム外テスト）、devbridge（実機検証）

**Spec:** `docs/superpowers/specs/2026-10-05-user-feedback-batch-design.md` の「C. ノード表示/非表示（表示ノード選択）」

## Global Constraints

- **対象ノード**: ACCEx と同じ 90 件。ノードごとに個別に効き、子孫へは伝播しない。定義（ボーン名・表示名・階層の深さ）は定義テーブル 1 か所（`MaidNodeVisibilityNodes`）にまとめる。
- **状態の持ち方**: メイドごとに `Dictionary<string,bool>`（ノード名 → 表示/非表示）で持つ。キーが無いノードは上書きなしとし、ゲームが決めた状態のままにする。
- **非表示**: body スロット（`goSlot[0]`）の dict に false を書く。
- **表示**: 全スロットの dict のうち、そのキーが false のものを true にする。
- **退避と解除**: 書き換える前の値は、ノード × スロットごとに退避する。上書きを解除するときは退避した値へ戻す（もとはキーが無かったならキーを消す）。
- **反映**: 書き換えたら `FixMaskFlag()` → `FixVisibleFlag()` を 1 回呼ぶ。毎フレームの適用はしない。
- **着替え・ボディ再ロード**: `IsAllProcPropBusy` の立ち下がりで、退避値を取り直してから上書きを再適用する。
- **タイムライン**:
  - `NodeVisibilityTimelineLayer` / `TransformType.NodeVisibility` を作る。値は bool 1 個。
  - `indexUpdated` のときだけ適用し、キーがあるノードだけを上書きする。
  - 登録は `TimelineIntegration.cs`（RegisterLayer / Inspector / RegisterTransform）で行う。
- **シーンプリセット**:
  - `ScenePresetMaid` に `nodeVisibility` 要素を足す。中身は上書きのあるノードだけで、`<node name visible>` の形にする。
  - 要素が無い旧プリセットは「触らない」、空要素は「上書きを全解除」として扱う。
- **書き込み口**: 静的クラス `MaidNodeVisibilityController` に一本化する。
- **UI**:
  - 階層の深さでインデントした一覧に、その時点で実際に表示されているかを示すチェックボックスを並べる。
  - 一覧の上に「全表示」「全非表示」「リセット（SE の上書きを全解除）」を置く。
- **Undo**: `HistoryScope` に値を足し、`SnapshotFactory` に case を足す。UI は編集の直前に `HistoryManager.instance.BeforeEdit(maid, scope, label)` を呼ぶ。
- **版**: タイムライン（38）とシーンプリセット（40）の版は上げない（後述「版の判断」）。
- **言語**: コードのコメント・ログは日本語。
- **ビルド**: 両構成（COM3D2 / COM3D25）でビルドする。
- **テスト**: Unity のネイティブ呼び出しは使えない。dict と文字列だけで組む。

## 実機での事前確認（計画作成時、2026-10-06、devbridge）

| 確認した値 | 結果 |
|---|---|
| ACCEx の 90 ノード名が旧ボディの `goSlot[0].morph.BoneNames` にあるか | 全件ある（`BoneNames.Count` は 90） |
| 各スロットの `m_dicDelNodeBody` の件数 | 129 件（ボーンの Transform 全部）。body は全部 true |
| onepiece の dict | false が 14 件（menu の `node消去`）。強制表示の検証に使える |

`TBody.FixVisibleFlag`（`Assembly-CSharp/TBody.cs:4143`）の判定は次の手順で進む。

1. 全スロットの morph の表示フラグを true に戻す。
2. `boVisible && obj != null` の各スロットについて、`m_dicDelNodeBody[name] == false` のノードがあれば、body の morph でそのノードを非表示にする。
3. 最後に各 morph の `FixVisibleFlag` を呼ぶ。

ここから次のことが言える。

- body スロットは常に可視なので、body の dict へ書いた false は脱衣（`SetMask`）の影響を受けない。
- 実際に表示されているかの判定も、この手順をそのまま写せば求められる（`NodeVisibilityState.IsVisible`）。

## 決定事項

**UI の置き場所**: 脱衣ウィンドウ（`MaidUndressWindow`）に内部タブ「脱衣」「ノード表示」を足す。理由は次の 3 つ。

- 「着ている物・体の一部を見せる/隠す」という同じ目的の操作で、ユーザーが探す場所が一致する。
- 重力ウィンドウ（`MaidGravityWindow`）に、内部タブ（`DrawInnerTabs`）とタブごとの `TimelineLayerGate.Begin` という先例がある。
- 新しいウィンドウにすると、Config の位置・サイズ・表示フラグ、`WindowManager` への登録、メニューバーの項目、`configuration.md` の表が増える。

タブはどちらも `TryFocusTimelineLayer` から切り替えられるようにする。

**レイヤーの並び（Priority）**: メイド系の優先度は 0・1・10〜19 が埋まっていて、20 からはカメラ系になる。そのため空いている 9 を使う。並びはソート（`TimelineManager.cs:2410`）にしか使われない。メニュー上は「メイドアニメブレンド」の次、「メイド表情」の前に並ぶ。

**キーに載せるノード**: `UpdateFrame` がキーにするのは次の 2 種類だけ。

- 上書きのあるノード: その値でキーにする。
- 上書きは無いが、レイヤーのどこかにキーのあるノード: 今の実際の表示でキーにする。これが無いと、途中で「解除」したノードのキーが作られず、前のキーの上書きが続いてしまう。

全 90 件を今の表示でキーにすると、menu が消したノードまで上書きになり、別の衣装に替えても消えたまま残る。そのため全件はキーにしない。

**キーの無い区間**: 基底の `ApplyPlayData` は、最初のキーより前（`current == null`）では何もしない。そのため、後ろの区間で付けた上書きが、シークで戻しても残ってしまう。

- そこで、レイヤーの `ApplyPlayData` で、その区間に入ったときに 1 回だけ、そのノードの上書きを解除する。毎フレーム解除すると、その区間で UI から付けた上書きまで消えるため。
  - `PlayDataBase.Update`（`Timeline/MotionPlayData.cs`）は、前へシークすると `ResetIndex` で `current` を null に戻す。`current` が null になるのは、最初のキーより前にいる間だけ。
- spec の「値は bool 1 個」を守るため、キーで「上書きなし」を直接は表さない。途中で解除したノードは「今の表示」の値でキーになる。衣装が消していないノードなら見た目は同じ。衣装が消しているノードは、その時点の見た目のまま上書きになる。

**反映のまとめ方**: 次の書き込みは dirty を立てるだけにする。

- `SetOverride` / `ClearOverride`
- 状態を丸ごと入れ替える `SetOverrides`

`FixMaskFlag` → `FixVisibleFlag` は `Flush(maid)` が 1 回だけ呼ぶ。呼ぶ場所は次のとおり。

- UI・Undo・プリセットは、書き込みの直後に `Flush` を呼ぶ。
- タイムラインのレイヤーは、`LateUpdate` の `ApplyPlayData` の直後に `Flush` を呼ぶ。キーの切り替わりで 90 件が同時に変わっても、メッシュの作り直しは 1 回で済む。
- `Update()` も、残った dirty を最後に `Flush` する（保険）。

**着替え中の書き込み**: `IsAllProcPropBusy` の間は、上書きを記録するだけで dict は触らない。立ち下がりで `ApplyAll` が書く。重力（`MaidGravityController.ApplyCategory` の `wasBusy`）と同じ方針。

- 立ち上がりの処理（`SyncBusy`: 退避値へ戻す）は、`Update` に加えて書き込み口からも呼ぶ。立ち上がり直後、`Update` より先に `Release` や `ClearAll` が来ても退避を取り残さないため。
- 着替えでの dict の扱いはボディで違う。
  - CRC ボディは `DeleteObj` で `Clear` する（`TBodySkin.cs:768` は `IsCRC` の中だけ）。
  - 旧ボディは menu の処理で `SetVisibleNodeSlot(..., true, "_ALL_")` が全キーを上書きする（`Menu.cs:834`）。
- 退避値を戻すのは、今の値が SE の書いた値のときだけ。Clear された dict にキーは作らず、ゲームが書き直した値も残す（`MaidScaleState.ShouldRestore` と同じ考え方）。

## 版の判断

- **タイムライン**: 新しい enum 値 `NodeVisibility` を足すだけなので、版は上げない（MaidScale・ScreenOverlay と同じ扱い）。MTE と旧 SE は未知の enum でデシリアライズに失敗する。
- **シーンプリセット**: 要素の追加だけで、要素が無い旧プリセットは「触らない」なので旧挙動と一致する。spec「共通方針」に従い版は上げない。経緯はフィールドのコメントに書く。

## Review Focus

1. **着替えの立ち上がりより先に dict が作り直される**: 立ち上がりで退避値を戻す前に、dict が作り直されていることがある（CRC は Clear、旧ボディは menu が `_ALL_` を true に上書き）。その場合も、ゲームの値を退避値で上書きしないこと。立ち下がりの `ApplyAll` が、退避を取り直して書き直す → Task 1 のテスト `RestoreAllはClearされたdictにキーを作らない` / `解除はゲームが書き直した値を戻さない`、Task 2 の `SyncBusy`
2. **非表示中の脱衣（`SetMask`）・着衣**: 非表示が維持されること。body に書くため `FixVisibleFlag` の再計算でも残る → 実機確認。Task 1 のテスト `IsVisible` で body の false が効くことを固定する
3. **表示を強制したノードを、さらに非表示へ切り替える**: 先に強制表示の退避値を戻してから body に false を書くこと。衣装側の true が残らない → Task 1 のテスト `表示から非表示へ切り替えると強制表示の退避を先に戻す`
4. **上書きの無いメイドへの Undo・プリセット適用**: 状態を作らず、dict にも触らないこと（空の上書きで `Flush` を呼んでも `FixVisibleFlag` を呼ばない）→ Task 2 の `SetOverrides` と `Flush`、Task 4 の `NodeVisibilitySnapshot.Apply`
5. **タイムラインで最初のキーより前へシーク・途中で解除**: 前者は上書きを解除してゲームの表示へ戻ること。後者は解除したノードも次のキー登録でキーになり、前のキーの上書きが続かないこと → Task 3 の `ApplyPlayData` と `BuildKeys`（テスト `キーは上書きのあるノードとキー済みノードだけを定義順で作る`）。レイヤー削除時の全解除は `ResetOnRemove` と `TimelineLayerResetOnRemoveTests` の期待表で固定する

---

## File Structure

| ファイル | 変更 | 責務 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidNodeVisibilityNodes.cs` | 新規 | 90 ノードの定義テーブル |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/NodeVisibilityState.cs` | 新規 | 上書き・退避・復元・表示判定（純粋ロジック） |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidNodeVisibilityController.cs` | 新規 | ゲームとの接続・着替え追従・`Flush` |
| `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs` | 変更 | `Update` / `ReleaseMaid` / `Destroy` から呼ぶ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs` | 変更 | `TransformType.NodeVisibility` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataNodeVisibility.cs` | 新規 | キー 1 件（表示 bool） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/NodeVisibilityTimelineLayer.cs` | 新規 | レイヤー |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/NodeVisibilityItemInspector.cs` | 新規 | Inspector の項目表示 |
| `source/COM3D2.SceneEditor.Plugin/NodeVisibilityRowDrawer.cs` | 新規 | 1 ノード分の行（ウィンドウと Inspector で共有） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs` | 変更 | 登録 3 か所 |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs` | 変更 | `NodeVisibility` |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/NodeVisibilitySnapshot.cs` | 新規 | Undo |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/SnapshotFactory.cs` | 変更 | case |
| `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 変更 | `ScenePresetNode` / `ScenePresetNodeVisibility` / `ScenePresetMaid.nodeVisibility` |
| `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs` | 変更 | Capture / Apply |
| `source/COM3D2.SceneEditor.Plugin/MaidUndressWindow.cs` | 変更 | 内部タブとノード一覧 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 変更 | 新規 8 ファイルの Compile |
| `source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityStateTests.cs` | 新規 | Task 1 |
| `source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityLayerTests.cs` | 新規 | Task 3 |
| `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNodeVisibilityTests.cs` | 新規 | Task 4 |
| `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerCategoryTests.cs` / `TimelineLayerResetOnRemoveTests.cs` / `LaneColorInfoTests.cs` | 変更 | 期待表に追加 |
| `docs-site/guide/maid-editing.md` / `docs-site/timeline/layers-maid.md` / `docs-site/timeline/compatibility.md` / `docs-site/guide/scene-preset.md` | 変更 | ドキュメント |
| `W:\COM3D2_5\work\CLAUDE.md` | 変更 | 互換の注意（リポジトリ外。コミット対象外） |

テストプロジェクトは、ビルド済みのプラグイン DLL を参照する。そのため、ソースの追加は本体 csproj への登録だけで足りる。

---

### Task 1: ノード定義と純粋ロジック `NodeVisibilityState`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidNodeVisibilityNodes.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/NodeVisibilityState.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`MaidManipulation\MaidGravityController.cs` の Compile 行の直後に 2 行）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityStateTests.cs`

**Interfaces:**
- Produces:
  - `public class MaidNodeVisibilityNode { public readonly string boneName; public readonly string displayName; public readonly int depth; }`
  - `public static class MaidNodeVisibilityNodes { public static readonly IList<MaidNodeVisibilityNode> nodes; public static MaidNodeVisibilityNode Find(string boneName); }`
  - `public class NodeVisibilityState`
    - `int count`
    - `bool TryGetOverride(string node, out bool visible)`
    - `Dictionary<string, bool> CopyOverrides()`
    - `void Set(string node, bool visible, IList<Dictionary<string, bool>> slots)`（slots が null なら記録だけ）
    - `void Clear(string node, IList<Dictionary<string, bool>> slots)`
    - `void ClearAll(IList<Dictionary<string, bool>> slots)`
    - `void RestoreAll(IList<Dictionary<string, bool>> slots)`（退避値へ戻す。上書きは残す）
    - `void ApplyAll(IList<Dictionary<string, bool>> slots)`（退避を取り直して全上書きを書く）
    - `static bool IsVisible(IEnumerable<Dictionary<string, bool>> activeSlots, string node)`
  - slots の index 0 は body。要素が null のスロット（未装着）は読み飛ばす

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityStateTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ノード表示の上書き・退避・復元を固定する。
    /// スロットは TBodySkin.m_dicDelNodeBody 相当の dict のリストで、index 0 が body
    /// </summary>
    public class NodeVisibilityStateTests
    {
        private const string Node = "Mune_L";

        private static Dictionary<string, bool> Dict(params KeyValuePair<string, bool>[] pairs)
        {
            return pairs.ToDictionary(p => p.Key, p => p.Value);
        }

        private static KeyValuePair<string, bool> P(string key, bool value)
        {
            return new KeyValuePair<string, bool>(key, value);
        }

        [Fact]
        public void 定義は90件で名前が重複せず深さは0以上()
        {
            var nodes = MaidNodeVisibilityNodes.nodes;
            Assert.Equal(90, nodes.Count);
            Assert.Equal(90, nodes.Select(n => n.boneName).Distinct().Count());
            Assert.All(nodes, n => Assert.True(n.depth >= 0));
            Assert.Equal("左胸下", MaidNodeVisibilityNodes.Find("Mune_L").displayName);
            Assert.Null(MaidNodeVisibilityNodes.Find("Unknown"));
        }

        [Fact]
        public void 非表示はbodyのdictにfalseを書き解除で元の値へ戻す()
        {
            var body = Dict(P(Node, true));
            var wear = Dict(P(Node, true));
            var slots = new List<Dictionary<string, bool>> { body, wear };
            var state = new NodeVisibilityState();

            state.Set(Node, false, slots);

            Assert.False(body[Node]);
            Assert.True(wear[Node]);
            bool visible;
            Assert.True(state.TryGetOverride(Node, out visible));
            Assert.False(visible);

            state.Clear(Node, slots);

            Assert.True(body[Node]);
            Assert.Equal(0, state.count);
        }

        [Fact]
        public void bodyにキーが無ければ解除でキーを消す()
        {
            var body = new Dictionary<string, bool>();
            var slots = new List<Dictionary<string, bool>> { body };
            var state = new NodeVisibilityState();

            state.Set(Node, false, slots);
            Assert.False(body[Node]);

            state.Clear(Node, slots);
            Assert.False(body.ContainsKey(Node));
        }

        [Fact]
        public void 表示は全スロットのfalseをtrueにし解除で戻す()
        {
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var shoes = Dict(P(Node, true));
            var slots = new List<Dictionary<string, bool>> { body, null, onepiece, shoes };
            var state = new NodeVisibilityState();

            state.Set(Node, true, slots);

            Assert.True(body[Node]);
            Assert.True(onepiece[Node]);
            Assert.True(shoes[Node]);

            state.Clear(Node, slots);

            Assert.False(onepiece[Node]);
            Assert.True(shoes[Node]);
        }

        [Fact]
        public void 表示から非表示へ切り替えると強制表示の退避を先に戻す()
        {
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var slots = new List<Dictionary<string, bool>> { body, onepiece };
            var state = new NodeVisibilityState();

            state.Set(Node, true, slots);
            state.Set(Node, false, slots);

            Assert.False(body[Node]);
            Assert.False(onepiece[Node]);

            state.Clear(Node, slots);

            Assert.True(body[Node]);
            Assert.False(onepiece[Node]);
        }

        [Fact]
        public void slotsがnullなら記録だけでApplyAllで書く()
        {
            var body = Dict(P(Node, true));
            var slots = new List<Dictionary<string, bool>> { body };
            var state = new NodeVisibilityState();

            state.Set(Node, false, null);
            Assert.True(body[Node]);

            state.ApplyAll(slots);
            Assert.False(body[Node]);

            state.Clear(Node, slots);
            Assert.True(body[Node]);
        }

        [Fact]
        public void RestoreAllは上書きを残してdictを戻しApplyAllで書き直す()
        {
            var body = Dict(P(Node, true), P("Mune_R", true));
            var slots = new List<Dictionary<string, bool>> { body };
            var state = new NodeVisibilityState();
            state.Set(Node, false, slots);
            state.Set("Mune_R", false, slots);

            state.RestoreAll(slots);

            Assert.True(body[Node]);
            Assert.True(body["Mune_R"]);
            Assert.Equal(2, state.count);

            state.ApplyAll(slots);

            Assert.False(body[Node]);
            Assert.False(body["Mune_R"]);
        }

        [Fact]
        public void RestoreAllはClearされたdictにキーを作らない()
        {
            // CRC ボディは着替えでスロットの dict を Clear する
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var slots = new List<Dictionary<string, bool>> { body, onepiece };
            var state = new NodeVisibilityState();
            state.Set(Node, true, slots);

            onepiece.Clear();
            state.RestoreAll(slots);

            Assert.False(onepiece.ContainsKey(Node));
        }

        [Fact]
        public void 解除はゲームが書き直した値を戻さない()
        {
            // 旧ボディは menu の処理で _ALL_ を true に書き直す
            var body = Dict(P(Node, true));
            var slots = new List<Dictionary<string, bool>> { body };
            var state = new NodeVisibilityState();
            state.Set(Node, false, slots);

            body[Node] = true;
            state.Clear(Node, slots);

            Assert.True(body[Node]);
        }

        [Fact]
        public void ApplyAllは着替え後の値から退避を取り直す()
        {
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var slots = new List<Dictionary<string, bool>> { body, onepiece };
            var state = new NodeVisibilityState();
            state.Set(Node, true, slots);
            state.RestoreAll(slots);

            // 着替えで別の衣装になり、そのノードは消さなくなった
            onepiece[Node] = true;
            state.ApplyAll(slots);
            state.Clear(Node, slots);

            Assert.True(onepiece[Node]);
        }

        [Fact]
        public void ClearAllは全ノードを戻して上書きを空にする()
        {
            var body = Dict(P(Node, true), P("Mune_R", true));
            var slots = new List<Dictionary<string, bool>> { body };
            var state = new NodeVisibilityState();
            state.Set(Node, false, slots);
            state.Set("Mune_R", false, slots);

            state.ClearAll(slots);

            Assert.True(body[Node]);
            Assert.True(body["Mune_R"]);
            Assert.Equal(0, state.count);
        }

        [Fact]
        public void CopyOverridesは内部の辞書と切り離される()
        {
            var state = new NodeVisibilityState();
            state.Set(Node, false, null);

            var copy = state.CopyOverrides();
            copy[Node] = true;

            bool visible;
            state.TryGetOverride(Node, out visible);
            Assert.False(visible);
        }

        [Fact]
        public void IsVisibleは有効なスロットのどれかがfalseなら非表示()
        {
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));

            Assert.True(NodeVisibilityState.IsVisible(new[] { body }, Node));
            Assert.False(NodeVisibilityState.IsVisible(new[] { body, onepiece }, Node));
            Assert.True(NodeVisibilityState.IsVisible(new[] { new Dictionary<string, bool>() }, Node));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」（spec「ループ運用」）を実行する。Expected: `MaidNodeVisibilityNodes` / `NodeVisibilityState` が無く、テストのコンパイルが失敗する。

- [ ] **Step 3: 定義テーブルを実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidNodeVisibilityNodes.cs`。並びと名前は ACCEx `ACConstants.NodeNames` のまま。

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表示/非表示を切り替えられる body のノード 1 件</summary>
    public class MaidNodeVisibilityNode
    {
        /// <summary>body のボーン名。m_dicDelNodeBody のキー</summary>
        public readonly string boneName;

        public readonly string displayName;

        /// <summary>一覧のインデント段数</summary>
        public readonly int depth;

        public MaidNodeVisibilityNode(string boneName, string displayName, int depth)
        {
            this.boneName = boneName;
            this.displayName = displayName;
            this.depth = depth;
        }
    }

    /// <summary>
    /// ノード表示の対象一覧。ACCEx (AlwaysColorChangeEx) の「表示ノード選択」と同じ 90 件・同じ並び。
    /// body の morph.BoneNames と一致する（旧ボディで実測）
    /// </summary>
    public static class MaidNodeVisibilityNodes
    {
        public static readonly IList<MaidNodeVisibilityNode> nodes =
            new List<MaidNodeVisibilityNode>
            {
                new MaidNodeVisibilityNode("Bip01 Pelvis_SCL_", "骨盤", 0),
                new MaidNodeVisibilityNode("Bip01 Spine_SCL_", "脊椎", 0),
                new MaidNodeVisibilityNode("Bip01 Spine0a_SCL_", "腹部", 1),
                new MaidNodeVisibilityNode("Bip01 Spine1_SCL_", "腰中", 2),
                new MaidNodeVisibilityNode("Bip01 Spine1a_SCL_", "胸部", 3),
                new MaidNodeVisibilityNode("Bip01 Neck_SCL_", "首", 4),
                new MaidNodeVisibilityNode("Bip01 Head", "頭", 5),
                new MaidNodeVisibilityNode("Mune_L", "左胸下", 4),
                new MaidNodeVisibilityNode("Mune_L_sub", "左胸上", 5),
                new MaidNodeVisibilityNode("Mune_R", "右胸下", 4),
                new MaidNodeVisibilityNode("Mune_R_sub", "右胸上", 5),
                new MaidNodeVisibilityNode("Bip01", "股間", 0),
                new MaidNodeVisibilityNode("Hip_L", "左尻", 1),
                new MaidNodeVisibilityNode("Hip_L_nub", "股間左部", 2),
                new MaidNodeVisibilityNode("Hip_R", "右尻", 1),
                new MaidNodeVisibilityNode("Hip_R_nub", "股間右部", 2),
                new MaidNodeVisibilityNode("momotwist_L", "左前腿", 2),
                new MaidNodeVisibilityNode("momoniku_L", "左後腿", 3),
                new MaidNodeVisibilityNode("momotwist2_L", "左前腿下部", 3),
                new MaidNodeVisibilityNode("Bip01 L Thigh_SCL_", "左ふくらはぎ", 1),
                new MaidNodeVisibilityNode("Bip01 L Calf_SCL_", "左足下腿", 2),
                new MaidNodeVisibilityNode("Bip01 L Foot", "左足首", 3),
                new MaidNodeVisibilityNode("Bip01 L Toe0", "左足小指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe01", "左足小指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Toe1", "左足中指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe11", "左足中指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Toe2", "左足親指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe21", "左足親指先", 5),
                new MaidNodeVisibilityNode("momotwist_R", "右前腿", 2),
                new MaidNodeVisibilityNode("momoniku_R", "右後腿", 3),
                new MaidNodeVisibilityNode("momotwist2_R", "右前腿下部", 3),
                new MaidNodeVisibilityNode("Bip01 R Thigh_SCL_", "右ふくらはぎ", 1),
                new MaidNodeVisibilityNode("Bip01 R Calf_SCL_", "右足下腿", 2),
                new MaidNodeVisibilityNode("Bip01 R Foot", "右足首", 3),
                new MaidNodeVisibilityNode("Bip01 R Toe0", "右足小指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe01", "右足小指先", 5),
                new MaidNodeVisibilityNode("Bip01 R Toe1", "右足中指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe11", "右足中指先", 5),
                new MaidNodeVisibilityNode("Bip01 R Toe2", "右足親指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe21", "右足親指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Clavicle_SCL_", "左鎖骨", 4),
                new MaidNodeVisibilityNode("Kata_L", "左肩", 5),
                new MaidNodeVisibilityNode("Kata_L_nub", "左肩上腕", 6),
                new MaidNodeVisibilityNode("Uppertwist_L", "左上腕A", 6),
                new MaidNodeVisibilityNode("Uppertwist1_L", "左上腕B", 6),
                new MaidNodeVisibilityNode("Bip01 L UpperArm", "左上腕", 5),
                new MaidNodeVisibilityNode("Bip01 L Forearm", "左肘", 6),
                new MaidNodeVisibilityNode("Foretwist1_L", "左前腕", 7),
                new MaidNodeVisibilityNode("Foretwist_L", "左手首", 7),
                new MaidNodeVisibilityNode("Bip01 L Hand", "左手", 7),
                new MaidNodeVisibilityNode("Bip01 L Finger0", "左親指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger01", "左親指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger02", "左親指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger1", "左人指し指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger11", "左人指し指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger12", "左人指し指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger2", "左中指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger21", "左中指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger22", "左中指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger3", "左薬指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger31", "左薬指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger32", "左薬指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger4", "左小指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger41", "左小指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger42", "左小指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Clavicle_SCL_", "右鎖骨", 4),
                new MaidNodeVisibilityNode("Kata_R", "右肩", 5),
                new MaidNodeVisibilityNode("Kata_R_nub", "右肩上腕", 6),
                new MaidNodeVisibilityNode("Uppertwist_R", "右上腕A", 6),
                new MaidNodeVisibilityNode("Uppertwist1_R", "右上腕B", 6),
                new MaidNodeVisibilityNode("Bip01 R UpperArm", "右上腕", 5),
                new MaidNodeVisibilityNode("Bip01 R Forearm", "右肘", 6),
                new MaidNodeVisibilityNode("Foretwist1_R", "右前腕", 7),
                new MaidNodeVisibilityNode("Foretwist_R", "右手首", 7),
                new MaidNodeVisibilityNode("Bip01 R Hand", "右手", 7),
                new MaidNodeVisibilityNode("Bip01 R Finger0", "右親指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger01", "右親指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger02", "右親指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger1", "右人指し指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger11", "右人指し指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger12", "右人指し指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger2", "右中指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger21", "右中指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger22", "右中指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger3", "右薬指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger31", "右薬指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger32", "右薬指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger4", "右小指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger41", "右小指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger42", "右小指先", 10),
            }.AsReadOnly();

        private static Dictionary<string, MaidNodeVisibilityNode> _byName = null;

        /// <summary>ボーン名から定義を引く。対象外なら null</summary>
        public static MaidNodeVisibilityNode Find(string boneName)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, MaidNodeVisibilityNode>();
                foreach (var node in nodes)
                {
                    _byName[node.boneName] = node;
                }
            }
            MaidNodeVisibilityNode result;
            return boneName != null && _byName.TryGetValue(boneName, out result) ? result : null;
        }
    }
}
```

- [ ] **Step 4: `NodeVisibilityState` を実装する**

`source/COM3D2.SceneEditor.Plugin/MaidManipulation/NodeVisibilityState.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 人分のノード表示の上書きと、上書き前の値の退避。
    /// ゲーム外テストのため、スロットごとの TBodySkin.m_dicDelNodeBody だけを相手にする
    /// （slots の index 0 が body、null は未装着のスロット）。
    /// 非表示は常に可視の body へ false を書き、脱衣で効果が消えないようにする。
    /// 表示（menu の node消去 の打ち消し）は false になっている全スロットを true にする
    /// </summary>
    public class NodeVisibilityState
    {
        private struct Backup
        {
            /// <summary>書き換え前にキーがあったか。無かったなら解除時にキーを消す</summary>
            public bool existed;

            public bool value;

            /// <summary>SE が書いた値。戻すのは今の値がこれのときだけ（ゲームが書き直した値は残す）</summary>
            public bool written;
        }

        private readonly Dictionary<string, bool> _overrides = new Dictionary<string, bool>();

        /// <summary>ノード → スロット index → 書き換え前の値</summary>
        private readonly Dictionary<string, Dictionary<int, Backup>> _backups
            = new Dictionary<string, Dictionary<int, Backup>>();

        public int count => _overrides.Count;

        public bool TryGetOverride(string node, out bool visible)
        {
            return _overrides.TryGetValue(node, out visible);
        }

        public Dictionary<string, bool> CopyOverrides()
        {
            return new Dictionary<string, bool>(_overrides);
        }

        /// <summary>上書きを記録して dict へ書く。slots が null（着替え中）なら記録だけで、ApplyAll が後で書く</summary>
        public void Set(string node, bool visible, IList<Dictionary<string, bool>> slots)
        {
            if (slots != null)
            {
                // 表示 → 非表示のように向きが変わるとき、前の書き込みを残さない
                RestoreNode(node, slots);
            }
            _overrides[node] = visible;
            if (slots != null)
            {
                ApplyNode(node, visible, slots);
            }
        }

        public void Clear(string node, IList<Dictionary<string, bool>> slots)
        {
            if (slots != null)
            {
                RestoreNode(node, slots);
            }
            _overrides.Remove(node);
        }

        public void ClearAll(IList<Dictionary<string, bool>> slots)
        {
            foreach (var node in new List<string>(_overrides.Keys))
            {
                Clear(node, slots);
            }
        }

        /// <summary>全ノードを書き換え前の値へ戻す。上書き自体は残す。着替えの開始時に呼ぶ</summary>
        public void RestoreAll(IList<Dictionary<string, bool>> slots)
        {
            foreach (var node in new List<string>(_backups.Keys))
            {
                RestoreNode(node, slots);
            }
        }

        /// <summary>今の dict から退避を取り直して全上書きを書く。着替えの完了時に呼ぶ</summary>
        public void ApplyAll(IList<Dictionary<string, bool>> slots)
        {
            foreach (var pair in _overrides)
            {
                RestoreNode(pair.Key, slots);
                ApplyNode(pair.Key, pair.Value, slots);
            }
        }

        /// <summary>
        /// 実際に表示されているか。TBody.FixVisibleFlag と同じく、
        /// 有効なスロット（boVisible かつ obj あり）のどれかが false なら非表示
        /// </summary>
        public static bool IsVisible(IEnumerable<Dictionary<string, bool>> activeSlots, string node)
        {
            foreach (var dict in activeSlots)
            {
                bool value;
                if (dict != null && dict.TryGetValue(node, out value) && !value)
                {
                    return false;
                }
            }
            return true;
        }

        private void ApplyNode(string node, bool visible, IList<Dictionary<string, bool>> slots)
        {
            var backups = new Dictionary<int, Backup>();
            if (visible)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var dict = slots[i];
                    bool value;
                    if (dict != null && dict.TryGetValue(node, out value) && !value)
                    {
                        backups[i] = new Backup { existed = true, value = false, written = true };
                        dict[node] = true;
                    }
                }
            }
            else if (slots.Count > 0 && slots[0] != null)
            {
                var body = slots[0];
                bool value;
                var existed = body.TryGetValue(node, out value);
                backups[0] = new Backup { existed = existed, value = value, written = false };
                body[node] = false;
            }
            _backups[node] = backups;
        }

        private void RestoreNode(string node, IList<Dictionary<string, bool>> slots)
        {
            Dictionary<int, Backup> backups;
            if (!_backups.TryGetValue(node, out backups))
            {
                return;
            }
            foreach (var pair in backups)
            {
                if (pair.Key >= slots.Count || slots[pair.Key] == null)
                {
                    continue;
                }
                var dict = slots[pair.Key];
                bool current;
                // 着替えで Clear された dict（CRC）や、menu が書き直した値（旧ボディの _ALL_ 上書き）には触らない
                if (!dict.TryGetValue(node, out current) || current != pair.Value.written)
                {
                    continue;
                }
                if (pair.Value.existed)
                {
                    dict[node] = pair.Value.value;
                }
                else
                {
                    dict.Remove(node);
                }
            }
            _backups.Remove(node);
        }
    }
}
```

csproj（`<Compile Include="MaidManipulation\MaidGravityController.cs" />` の直後）:

```xml
    <Compile Include="MaidManipulation\MaidNodeVisibilityNodes.cs" />
    <Compile Include="MaidManipulation\NodeVisibilityState.cs" />
```

- [ ] **Step 5: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 両構成のビルドが成功し、`NodeVisibilityStateTests` が全件 PASS、既存テストも PASS。

---

### Task 2: ゲームとの接続 `MaidNodeVisibilityController`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidNodeVisibilityController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidManipulateManager.cs`（`gravityController.Update();` の直後、`gravityController.Release(maid);` の近く、`gravityController.Destroy();` の近く）
- Modify: csproj（Task 1 の 2 行の直後）

**Interfaces:**
- Consumes: `NodeVisibilityState`、`MaidNodeVisibilityNodes`（Task 1）
- Produces（すべて `public static`。namespace `COM3D2.SceneEditor.Plugin`）:
  - `bool HasOverrides(Maid maid)`
  - `bool TryGetOverride(Maid maid, string node, out bool visible)`
  - `bool IsVisible(Maid maid, string node)`（実際の表示）
  - `void SetOverride(Maid maid, string node, bool visible)` / `void ClearOverride(Maid maid, string node)` / `void ClearAll(Maid maid)` / `void SetAll(Maid maid, bool visible)`
  - `Dictionary<string, bool> GetOverrides(Maid maid)`（コピー。無ければ空）
  - `void SetOverrides(Maid maid, IDictionary<string, bool> overrides)`（丸ごと入れ替え）
  - `void Flush(Maid maid)`
  - `void Update()` / `void Release(Maid maid)` / `void Destroy()`

ゲーム外テストは書けない（`Maid` / `TBody` がゲーム実体）。実機確認（段 3）で確かめる。

- [ ] **Step 1: 実装する**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのノード表示/非表示（ACCEx の「表示ノード選択」相当）の書き込み口。
    /// 上書きの記録と退避は NodeVisibilityState に任せ、ここは dict の収集・反映 (FixVisibleFlag)・着替え追従を担う。
    /// 書き込みは dirty を立てるだけで、FixMaskFlag → FixVisibleFlag は Flush がまとめて 1 回呼ぶ
    /// </summary>
    public static class MaidNodeVisibilityController
    {
        private class Entry
        {
            public readonly NodeVisibilityState state = new NodeVisibilityState();

            /// <summary>前フレームの着替え中フラグ。立ち上がりで退避値へ戻し、立ち下がりで書き直す</summary>
            public bool wasBusy;

            /// <summary>dict を書き換えて FixVisibleFlag がまだのとき</summary>
            public bool dirty;
        }

        private static readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        public static bool HasOverrides(Maid maid)
        {
            var entry = GetEntry(maid);
            return entry != null && entry.state.count > 0;
        }

        public static bool TryGetOverride(Maid maid, string node, out bool visible)
        {
            visible = true;
            var entry = GetEntry(maid);
            return entry != null && entry.state.TryGetOverride(node, out visible);
        }

        /// <summary>実際に表示されているか。ボディ未ロードなら true</summary>
        public static bool IsVisible(Maid maid, string node)
        {
            if (!IsBodyReady(maid))
            {
                return true;
            }
            return NodeVisibilityState.IsVisible(CollectActiveDicts(maid.body0), node);
        }

        public static void SetOverride(Maid maid, string node, bool visible)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.state.Set(node, visible, GetWritableDicts(maid, entry));
            entry.dirty = true;
        }

        public static void ClearOverride(Maid maid, string node)
        {
            var entry = GetEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.state.Clear(node, GetWritableDicts(maid, entry));
            entry.dirty = true;
        }

        /// <summary>SE の上書きを全解除して、ゲームが決めた表示へ戻す</summary>
        public static void ClearAll(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null || entry.state.count == 0)
            {
                return;
            }
            entry.state.ClearAll(GetWritableDicts(maid, entry));
            entry.dirty = true;
        }

        /// <summary>全ノードを表示か非表示で上書きする。表示は衣装の node消去 も打ち消す</summary>
        public static void SetAll(Maid maid, bool visible)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            // 90 件分の dict の収集を 1 回で済ませる
            var dicts = GetWritableDicts(maid, entry);
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                entry.state.Set(node.boneName, visible, dicts);
            }
            entry.dirty = true;
        }

        public static Dictionary<string, bool> GetOverrides(Maid maid)
        {
            var entry = GetEntry(maid);
            return entry != null ? entry.state.CopyOverrides() : new Dictionary<string, bool>();
        }

        /// <summary>
        /// 上書きを丸ごと入れ替える（Undo・プリセット用）。対象外のノード名は捨てる。
        /// 上書きの無いメイドへ空を渡しても状態を作らない
        /// </summary>
        public static void SetOverrides(Maid maid, IDictionary<string, bool> overrides)
        {
            if (overrides.Count == 0 && GetEntry(maid) == null)
            {
                return;
            }
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            var dicts = GetWritableDicts(maid, entry);
            foreach (var pair in entry.state.CopyOverrides())
            {
                if (!overrides.ContainsKey(pair.Key))
                {
                    entry.state.Clear(pair.Key, dicts);
                    entry.dirty = true;
                }
            }
            foreach (var pair in overrides)
            {
                if (MaidNodeVisibilityNodes.Find(pair.Key) == null)
                {
                    continue;
                }
                bool current;
                if (entry.state.TryGetOverride(pair.Key, out current) && current == pair.Value)
                {
                    continue;
                }
                entry.state.Set(pair.Key, pair.Value, dicts);
                entry.dirty = true;
            }
        }

        /// <summary>書き換えた dict をメッシュへ反映する。着替え中は完了時の ApplyAll に任せる</summary>
        public static void Flush(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null || !entry.dirty || entry.wasBusy || !IsBodyReady(maid))
            {
                return;
            }
            entry.dirty = false;
            maid.body0.FixMaskFlag();
            maid.body0.FixVisibleFlag();
        }

        /// <summary>着替え・ボディ再ロードへの追従と、取り残した反映。MaidManipulateManager.Update から毎フレーム呼ぶ</summary>
        public static void Update()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<Maid> deadMaids = null;
            foreach (var pair in _entries)
            {
                var maid = pair.Key;
                var entry = pair.Value;
                if (maid == null || maid.body0 == null)
                {
                    if (deadMaids == null)
                    {
                        deadMaids = new List<Maid>();
                    }
                    deadMaids.Add(maid);
                    continue;
                }

                SyncBusy(maid, entry);
                if (entry.wasBusy && !maid.IsAllProcPropBusy && maid.body0.isLoadedBody)
                {
                    entry.wasBusy = false;
                    entry.state.ApplyAll(CollectDicts(maid.body0));
                    entry.dirty = true;
                }
            }

            if (deadMaids != null)
            {
                foreach (var maid in deadMaids)
                {
                    _entries.Remove(maid);
                }
            }

            foreach (var maid in new List<Maid>(_entries.Keys))
            {
                Flush(maid);
            }
        }

        /// <summary>メイド解除時。上書きを戻して、使い回されるストックの Maid へ持ち越さない</summary>
        public static void Release(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null)
            {
                return;
            }
            ClearAll(maid);
            Flush(maid);
            _entries.Remove(maid);
        }

        public static void Destroy()
        {
            foreach (var maid in new List<Maid>(_entries.Keys))
            {
                if (maid != null)
                {
                    Release(maid);
                }
            }
            _entries.Clear();
        }

        private static Entry GetEntry(Maid maid)
        {
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry) ? entry : null;
        }

        private static Entry GetOrCreateEntry(Maid maid)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }
            var entry = GetEntry(maid);
            if (entry == null)
            {
                entry = new Entry { wasBusy = maid.IsAllProcPropBusy || !maid.body0.isLoadedBody };
                _entries[maid] = entry;
            }
            return entry;
        }

        private static bool IsBodyReady(Maid maid)
        {
            return maid != null && maid.body0 != null && maid.body0.isLoadedBody;
        }

        /// <summary>
        /// 着替えの立ち上がりを処理する。着替えで dict が作り直される前に、SE が書いた値をゲームの値へ戻しておく
        /// （作り直されなかったスロットに SE の値が残り、次の退避に紛れ込むのを防ぐ）。
        /// Update を待たずに書き込み口からも呼び、立ち上がり直後の Release・ClearAll が退避を取り残さないようにする
        /// </summary>
        private static void SyncBusy(Maid maid, Entry entry)
        {
            if (entry.wasBusy || !maid.IsAllProcPropBusy)
            {
                return;
            }
            var dicts = CollectDicts(maid.body0);
            if (dicts != null)
            {
                entry.state.RestoreAll(dicts);
            }
            entry.wasBusy = true;
        }

        /// <summary>着替え中・未ロードなら null（記録だけにして、完了時の ApplyAll に書かせる）</summary>
        private static IList<Dictionary<string, bool>> GetWritableDicts(Maid maid, Entry entry)
        {
            SyncBusy(maid, entry);
            if (entry.wasBusy || !IsBodyReady(maid))
            {
                return null;
            }
            return CollectDicts(maid.body0);
        }

        /// <summary>
        /// スロットごとの m_dicDelNodeBody（index 0 が body）。未装着のスロットは null。
        /// FixVisibleFlag と同じく goSlot[i] だけを見る（2.5 のサブスロットは対象外）
        /// </summary>
        private static IList<Dictionary<string, bool>> CollectDicts(TBody body)
        {
            if (body == null || body.goSlot == null)
            {
                return null;
            }
            // SlotID.end は番兵で goSlot に実体が無い。goSlot[int] は例外を握らないため上限を切る
            var count = Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
            var list = new List<Dictionary<string, bool>>(count);
            for (var i = 0; i < count; i++)
            {
                var slot = body.goSlot[i];
                list.Add(slot != null ? slot.m_dicDelNodeBody : null);
            }
            return list;
        }

        /// <summary>FixVisibleFlag が見るスロット（boVisible かつ obj あり）の dict</summary>
        private static IEnumerable<Dictionary<string, bool>> CollectActiveDicts(TBody body)
        {
            var count = Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
            for (var i = 0; i < count; i++)
            {
                var slot = body.goSlot[i];
                if (slot != null && slot.boVisible && slot.obj != null)
                {
                    yield return slot.m_dicDelNodeBody;
                }
            }
        }
    }
}
```


- [ ] **Step 2: `MaidManipulateManager` から呼ぶ**

`gravityController.Update();` の直後:

```csharp

            // 着替え・ボディ再ロードで初期化されたノード表示を書き直す
            MaidNodeVisibilityController.Update();
```

`maidScaleController.Release(maid);` の直後:

```csharp
            // ノード表示の上書きも持ち越さない（ストックの Maid は使い回される）
            MaidNodeVisibilityController.Release(maid);
```

`maidScaleController.Destroy();` の直後:

```csharp
            MaidNodeVisibilityController.Destroy();
```

csproj（Task 1 の 2 行の直後）:

```xml
    <Compile Include="MaidManipulation\MaidNodeVisibilityController.cs" />
```

- [ ] **Step 3: ビルドする**

「ビルド＆テスト」を実行する。Expected: 両構成のビルドが成功し、全テスト PASS。COM3D2 構成の 2.0 にも同名のメンバーがある（ilspycmd で確認済み）。`TBody.goSlot` は `List<TBodySkin>`、`TBodySkin` に `boVisible` / `obj` / `morph` / `m_dicDelNodeBody`、`TBody` に `FixMaskFlag` / `FixVisibleFlag`。

---

### Task 3: タイムライン（`TransformDataNodeVisibility` / `NodeVisibilityTimelineLayer` / Inspector）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs`（`Move,` の直後に `NodeVisibility,`）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataNodeVisibility.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/NodeVisibilityTimelineLayer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/NodeVisibilityRowDrawer.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/NodeVisibilityItemInspector.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineIntegration.cs`（MaidScale の登録 3 か所の直後）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs`（行の描画が履歴を記録するため、`NodeVisibility` の値をここで足す）
- Modify: csproj（新規 4 ファイル）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityLayerTests.cs`、`TimelineLayerCategoryTests.cs`、`TimelineLayerResetOnRemoveTests.cs`、`LaneColorInfoTests.cs`

**Interfaces:**
- Consumes: `MaidNodeVisibilityController.*`（Task 2）、`MaidNodeVisibilityNodes`（Task 1）
- Produces:
  - `TransformType.NodeVisibility`
  - `TransformDataNodeVisibility`（`Index.IsVisible = 0`、`valueCount 1`、`bool isVisible`、`ValueData isVisibleValue`）
  - `NodeVisibilityTimelineLayer.Create(int slotNo)`
  - `HistoryScope.NodeVisibility`
  - `NodeVisibilityRowDrawer.Draw(GUIView view, Maid target, MaidNodeVisibilityNode node, float rowHeight, float indentWidth)`
  - `NodeVisibilityItemInspector.ResolveNode(string itemName)` → `MaidNodeVisibilityNode` または null

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/NodeVisibilityLayerTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ノード表示レイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class NodeVisibilityLayerTests
    {
        private static MTEP.TransformDataNodeVisibility CreateTransform()
        {
            var trans = new MTEP.TransformDataNodeVisibility();
            trans.Initialize("Mune_L");
            return trans;
        }

        [Fact]
        public void キーは表示1値でBool型()
        {
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.NodeVisibility, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(0, (int)MTEP.TransformDataNodeVisibility.Index.IsVisible);
            Assert.Equal(MTEP.CustomValueType.BoolValue,
                trans.GetCustomValueInfoMap()["isVisible"].type);
        }

        [Fact]
        public void 新しいキーの既定は表示()
        {
            Assert.True(CreateTransform().isVisible);
        }

        [Fact]
        public void 表示フラグは値配列へ書き戻される()
        {
            var trans = CreateTransform();
            trans.isVisible = false;

            Assert.False(trans.isVisibleValue.boolValue);
            Assert.Equal(0f, trans.values[0].value);
        }

        [Fact]
        public void キーは上書きのあるノードとキー済みノードだけを定義順で作る()
        {
            var overrides = new Dictionary<string, bool> { { "Mune_R", true } };
            var keyed = new List<string> { "Mune_L", "Mune_R" };

            var keys = MTEP.NodeVisibilityTimelineLayer.BuildKeys(
                overrides, keyed, node => node != "Mune_L");

            Assert.Equal(2, keys.Count);
            Assert.Equal("Mune_L", keys[0].Key);
            Assert.False(keys[0].Value);
            Assert.Equal("Mune_R", keys[1].Key);
            Assert.True(keys[1].Value);
        }

        [Fact]
        public void 上書きもキーも無いノードはキーにしない()
        {
            var keys = MTEP.NodeVisibilityTimelineLayer.BuildKeys(
                new Dictionary<string, bool>(), new List<string>(), _ => false);

            Assert.Empty(keys);
        }

        [Fact]
        public void 履歴スコープはメイド必須()
        {
            Assert.True(HistoryScopeUtils.RequiresMaid(HistoryScope.NodeVisibility));
        }

        [Fact]
        public void Inspectorは項目名からノードを引き未知の名前ではnullを返す()
        {
            Assert.Equal("左胸下", NodeVisibilityItemInspector.ResolveNode("Mune_L").displayName);
            Assert.Null(NodeVisibilityItemInspector.ResolveNode("unknown"));
        }
    }
}
```

`TimelineLayerCategoryTests.cs` の `{ "MaidScaleTimelineLayer", TimelineLayerCategory.Maid },` の次に足す:

```csharp
            { "NodeVisibilityTimelineLayer", TimelineLayerCategory.Maid },
```

`TimelineLayerResetOnRemoveTests.cs` の `"MaidScaleTimelineLayer",` の次に足す:

```csharp
            // 上書きはこのレイヤーとノード表示タブからしか作れないため、断面の無い削除でも全解除する
            "NodeVisibilityTimelineLayer",
```

`LaneColorInfoTests.cs` の `TransformDataUndress` の行の次に足す:

```csharp
            yield return new object[] { new TransformDataNodeVisibility(), (int)TransformDataNodeVisibility.Index.IsVisible, true };
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を実行する。Expected: `TransformDataNodeVisibility` などが無く、テストのコンパイルが失敗する。

- [ ] **Step 3: `TransformType` と `TransformDataNodeVisibility`**

`ITransformData.cs` の `Move,` の次の行に `NodeVisibility,` を足す。

`Timeline/TransformData/TransformDataNodeVisibility.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ノード表示キー 1 件分。項目名は body のボーン名 (MaidNodeVisibilityNodes) で、表示フラグを持つ。
    /// 補間せず区間開始時に適用する
    /// </summary>
    public class TransformDataNodeVisibility : TransformDataBase
    {
        public enum Index
        {
            IsVisible = 0,
        }

        public override TransformType type => TransformType.NodeVisibility;

        public override int valueCount => 1;

        public TransformDataNodeVisibility()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0（非表示）で作るため、表示を既定にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            isVisible = true;
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "isVisible",
                new CustomValueInfo
                {
                    index = (int)Index.IsVisible,
                    name = "表示",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        private static readonly LaneColorInfo LaneInfo =
            LaneColorInfo.FromBool(Color.white, (int)Index.IsVisible);

        public override LaneColorInfo GetLaneColorInfo()
        {
            return LaneInfo;
        }

        public ValueData isVisibleValue => values[(int)Index.IsVisible];

        public bool isVisible
        {
            get => isVisibleValue.boolValue;
            set => isVisibleValue.boolValue = value;
        }
    }
}
```

- [ ] **Step 4: `HistoryScope.NodeVisibility`**

`HistoryScope.cs` の `MaidScale,` の後に足す（`RequiresMaid` は default の true のまま）:

```csharp
        /// <summary>ノード表示 (体の部位ごとの表示/非表示の上書き)</summary>
        NodeVisibility,
```

- [ ] **Step 5: 行の描画 `NodeVisibilityRowDrawer`**

`source/COM3D2.SceneEditor.Plugin/NodeVisibilityRowDrawer.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ノード 1 件分の行 (表示トグル・解除ボタン)。脱衣ウィンドウのノード表示タブと
    /// TimelineItemInspector (ノード表示レイヤーの項目表示) で共有する。
    /// トグルには実際の表示を出し、SE が上書きしているノードは名前に「*」を付ける
    /// </summary>
    public static class NodeVisibilityRowDrawer
    {
        private const float ClearButtonWidth = 50f;

        public static void Draw(GUIView view, Maid target, MaidNodeVisibilityNode node,
            float rowHeight, float indentWidth)
        {
            bool overridden;
            var hasOverride = MaidNodeVisibilityController.TryGetOverride(target, node.boneName, out overridden);
            var label = hasOverride ? node.displayName + " *" : node.displayName;

            view.BeginHorizontal();
            {
                if (indentWidth > 0f)
                {
                    view.AddSpace(indentWidth, rowHeight);
                }

                // トグルは残り幅いっぱい (-1) にするため、解除ボタンを先に置く
                if (view.DrawButton("解除", ClearButtonWidth, rowHeight, hasOverride))
                {
                    RecordEdit(target, node.displayName + " 解除");
                    MaidNodeVisibilityController.ClearOverride(target, node.boneName);
                    MaidNodeVisibilityController.Flush(target);
                }

                view.DrawToggle(label, MaidNodeVisibilityController.IsVisible(target, node.boneName),
                    -1, rowHeight,
                    value =>
                    {
                        RecordEdit(target, node.displayName);
                        MaidNodeVisibilityController.SetOverride(target, node.boneName, value);
                        MaidNodeVisibilityController.Flush(target);
                    });
            }
            view.EndLayout();
        }

        public static void RecordEdit(Maid target, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.NodeVisibility, "ノード表示: " + label);
        }
    }
}
```

（`GUIView.AddSpace(width, height)` は描画領域を 1 要素分進める。横並びの中では横へ空く。`DrawButton` の 4 番目の引数は enabled。どちらも `MTEUtils/GUIView.cs` で確認済み）

- [ ] **Step 6: `NodeVisibilityTimelineLayer`**

`Timeline/TimelineLayer/NodeVisibilityTimelineLayer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドのノード表示 (MaidNodeVisibilityController) をキー化するレイヤー。
    /// 項目名は body のボーン名 (MaidNodeVisibilityNodes) で、表示フラグは区間開始時に適用する。
    /// キーにするのは、SE が上書きしているノードと、このレイヤーに既にキーのあるノードだけ
    /// （menu が消したノードまで上書きにしないため）。最初のキーより前の区間では上書きを解除する
    /// </summary>
    [TimelineLayerDesc("メイドノード表示", 9, TimelineLayerCategory.Maid)]
    public class NodeVisibilityTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(NodeVisibilityTimelineLayer);
        public override string layerName => nameof(NodeVisibilityTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var node in MaidNodeVisibilityNodes.nodes)
                    {
                        _allBoneNames.Add(node.boneName);
                    }
                }
                return _allBoneNames;
            }
        }

        private NodeVisibilityTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static NodeVisibilityTimelineLayer Create(int slotNo)
        {
            return new NodeVisibilityTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                _allMenuItems.Add(new BoneMenuItem(node.boneName, node.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
                // キーの切り替わりで複数ノードが同時に変わってもメッシュの作り直しは 1 回にする
                MaidNodeVisibilityController.Flush(maid);
            }
        }

        /// <summary>最初のキーより前の区間にいるノード。区間に入ったときだけ解除するために持つ</summary>
        private readonly HashSet<string> _beforeFirstKeyNodes = new HashSet<string>();

        /// <summary>
        /// 基底は最初のキーより前 (current == null) を何もしないため、
        /// 後ろの区間で付けた上書きがシークで戻しても残る。その区間に入ったときにゲームが決めた表示へ戻す。
        /// 毎フレーム戻すと、その区間で UI から付けた上書きまで消えるので、入ったときの 1 回だけにする
        /// </summary>
        protected override void ApplyPlayData()
        {
            base.ApplyPlayData();

            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }
            foreach (var pair in _playDataMap)
            {
                if (pair.Value.current != null)
                {
                    _beforeFirstKeyNodes.Remove(pair.Key);
                    continue;
                }
                if (_beforeFirstKeyNodes.Add(pair.Key))
                {
                    MaidNodeVisibilityController.ClearOverride(maid, pair.Key);
                }
            }
        }

        /// <summary>
        /// キーにするノードと値。上書きのあるノードはその値、上書きは無いがレイヤーにキーのあるノードは今の表示。
        /// 後者が無いと、途中で「解除」したノードのキーが作られず、前のキーの上書きが続く。並びは定義順
        /// </summary>
        public static List<KeyValuePair<string, bool>> BuildKeys(
            IDictionary<string, bool> overrides, ICollection<string> keyedNodes, Func<string, bool> isVisible)
        {
            var result = new List<KeyValuePair<string, bool>>();
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                bool visible;
                if (overrides.TryGetValue(node.boneName, out visible))
                {
                    result.Add(new KeyValuePair<string, bool>(node.boneName, visible));
                }
                else if (keyedNodes.Contains(node.boneName))
                {
                    result.Add(new KeyValuePair<string, bool>(node.boneName, isVisible(node.boneName)));
                }
            }
            return result;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || !indexUpdated || MaidNodeVisibilityNodes.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataNodeVisibility;
            if (start == null)
            {
                return;
            }

            bool current;
            if (MaidNodeVisibilityController.TryGetOverride(maid, motion.name, out current)
                && current == start.isVisible)
            {
                return;
            }
            MaidNodeVisibilityController.SetOverride(maid, motion.name, start.isVisible);
        }

        /// <summary>
        /// 上書きはこのレイヤーとノード表示タブからしか作れないので、削除・アンロード時は全解除する。
        /// 誕生時の断面がある場合は、この後の断面の復元が上書きする
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            MaidNodeVisibilityController.ClearAll(maid);
            MaidNodeVisibilityController.Flush(maid);
        }

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
            MaidNodeVisibilityController.Flush(maid);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            var keys = BuildKeys(
                MaidNodeVisibilityController.GetOverrides(maid),
                _playDataMap.Keys,
                node => MaidNodeVisibilityController.IsVisible(maid, node));
            foreach (var pair in keys)
            {
                var trans = CreateTransformData<TransformDataNodeVisibility>(pair.Key);
                trans.isVisible = pair.Value;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.NodeVisibility;
        }
    }
}
```

- [ ] **Step 7: Inspector**

`Timeline/ItemInspector/NodeVisibilityItemInspector.cs`:

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ノード表示レイヤーのメニュー項目 → 表示トグル。
    /// 行の描画と履歴への記録は NodeVisibilityRowDrawer に任せる
    /// </summary>
    public class NodeVisibilityItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名 (ボーン名) からノードを求める。対象外なら null</summary>
        public static MaidNodeVisibilityNode ResolveNode(string itemName)
        {
            return MaidNodeVisibilityNodes.Find(itemName);
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var item in items)
            {
                var node = ResolveNode(item.name);
                if (node == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }
                NodeVisibilityRowDrawer.Draw(view, maid, node, RowHeight, 0f);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // ノード表示に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
```

- [ ] **Step 8: 登録**

`TimelineIntegration.cs` の、MaidScale のそれぞれの登録の直後に足す:

```csharp
            timelineManager.RegisterLayer(
                typeof(MTEP.NodeVisibilityTimelineLayer), MTEP.NodeVisibilityTimelineLayer.Create);
```

```csharp
            TimelineItemInspectorRegistry.Register(
                typeof(MTEP.NodeVisibilityTimelineLayer), new NodeVisibilityItemInspector());
```

```csharp
            timelineManager.RegisterTransform(
                MTEP.TransformType.NodeVisibility,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataNodeVisibility>);
```

csproj に、既存の同種ファイルの行の隣へ 4 行を足す:

```xml
    <Compile Include="Timeline\TransformData\TransformDataNodeVisibility.cs" />
    <Compile Include="Timeline\TimelineLayer\NodeVisibilityTimelineLayer.cs" />
    <Compile Include="Timeline\ItemInspector\NodeVisibilityItemInspector.cs" />
    <Compile Include="NodeVisibilityRowDrawer.cs" />
```

- [ ] **Step 9: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 両構成のビルドが成功し、全件 PASS（追加したテストと期待表の 3 ファイルを含む）。

---

### Task 4: Undo とシーンプリセット

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/NodeVisibilitySnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/SnapshotFactory.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetMaidScale` の後にクラス 2 つ、`ScenePresetMaid.maidScale` の後にフィールド）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`state.maidScale = CaptureMaidScale(maid);` の次、`ApplyMaidScale` の try ブロックの次、`ApplyMaidScale` メソッドの次）
- Modify: csproj（1 ファイル）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNodeVisibilityTests.cs`

**Interfaces:**
- Consumes: `MaidNodeVisibilityController.GetOverrides` / `SetOverrides` / `Flush` / `HasOverrides`（Task 2）、`HistoryScope.NodeVisibility`（Task 3）
- Produces:
  - `ScenePresetNode`（`[XmlAttribute] name`、`[XmlAttribute] visible`）
  - `ScenePresetNodeVisibility`（`[XmlElement("node")] List<ScenePresetNode> nodes`、`static FromOverrides(IEnumerable<KeyValuePair<string,bool>>)`、`Dictionary<string,bool> ToOverrides()`）
  - `ScenePresetMaid.nodeVisibility`

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ScenePresetNodeVisibilityTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットのノード表示を固定する。
    /// 要素が無い旧プリセットは「触らない」(null)、空要素は「上書きを全解除」
    /// </summary>
    public class ScenePresetNodeVisibilityTests
    {
        private static string Serialize(ScenePresetMaid maid)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, maid);
                return writer.ToString();
            }
        }

        private static ScenePresetMaid Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaid)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 対象外のノードは書き出さない()
        {
            var preset = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>
            {
                { "Mune_L", false },
                { "Unknown", false },
            });

            Assert.Single(preset.nodes);
            Assert.Equal("Mune_L", preset.nodes[0].name);
            Assert.False(preset.nodes[0].visible);
        }

        [Fact]
        public void 上書きを往復する()
        {
            var maid = new ScenePresetMaid
            {
                nodeVisibility = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>
                {
                    { "Mune_L", false },
                    { "Bip01 Head", true },
                }),
            };

            var text = Serialize(maid);

            Assert.Contains("<node name=\"Mune_L\" visible=\"false\" />", text);
            var overrides = Deserialize(text).nodeVisibility.ToOverrides();
            Assert.Equal(2, overrides.Count);
            Assert.False(overrides["Mune_L"]);
            Assert.True(overrides["Bip01 Head"]);
        }

        [Fact]
        public void 上書きが無くても空要素を書き全解除として読める()
        {
            var maid = new ScenePresetMaid
            {
                nodeVisibility = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>()),
            };

            var restored = Deserialize(Serialize(maid));

            Assert.NotNull(restored.nodeVisibility);
            Assert.Empty(restored.nodeVisibility.ToOverrides());
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            var restored = Deserialize(Serialize(new ScenePresetMaid()));

            Assert.Null(restored.nodeVisibility);
        }

        [Fact]
        public void 手で書き換えた重複と対象外のノードは読込時に捨てる()
        {
            var preset = new ScenePresetNodeVisibility();
            preset.nodes.Add(new ScenePresetNode { name = "Mune_L", visible = false });
            preset.nodes.Add(new ScenePresetNode { name = "Mune_L", visible = true });
            preset.nodes.Add(new ScenePresetNode { name = "Unknown", visible = false });
            preset.nodes.Add(null);

            var overrides = preset.ToOverrides();

            Assert.Single(overrides);
            Assert.False(overrides["Mune_L"]);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

「ビルド＆テスト」を実行する。Expected: `ScenePresetNodeVisibility` が無く、コンパイルが失敗する。

- [ ] **Step 3: プリセットのデータ**

`ScenePresetData.cs` の `ScenePresetMaidScale` クラスの後に足す:

```csharp
    /// <summary>ノード表示の上書き 1 件</summary>
    public class ScenePresetNode
    {
        /// <summary>body のボーン名 (MaidNodeVisibilityNodes)</summary>
        [XmlAttribute]
        public string name;

        [XmlAttribute]
        public bool visible;
    }

    /// <summary>
    /// ノード表示。SE が上書きしているノードだけを持つ。
    /// 上書きが無くても要素自体は書き、旧プリセット (要素なし = null、適用時に触らない) と区別する
    /// </summary>
    public class ScenePresetNodeVisibility
    {
        [XmlElement("node")]
        public List<ScenePresetNode> nodes = new List<ScenePresetNode>();

        /// <summary>上書きから作る。対象外のノードは書かない</summary>
        public static ScenePresetNodeVisibility FromOverrides(IEnumerable<KeyValuePair<string, bool>> overrides)
        {
            var result = new ScenePresetNodeVisibility();
            foreach (var pair in overrides)
            {
                if (MaidNodeVisibilityNodes.Find(pair.Key) == null)
                {
                    continue;
                }
                result.nodes.Add(new ScenePresetNode { name = pair.Key, visible = pair.Value });
            }
            return result;
        }

        /// <summary>上書きへ戻す。手で書き換えた値に備え、対象外は捨て、同じノードは先のものを使う</summary>
        public Dictionary<string, bool> ToOverrides()
        {
            var result = new Dictionary<string, bool>();
            foreach (var node in nodes)
            {
                if (node == null || MaidNodeVisibilityNodes.Find(node.name) == null
                    || result.ContainsKey(node.name))
                {
                    continue;
                }
                result[node.name] = node.visible;
            }
            return result;
        }
    }
```

`ScenePresetMaid` の `public ScenePresetMaidScale maidScale;` の後に足す:

```csharp

        /// <summary>
        /// ノード表示。旧プリセットは null になり、適用時に触らない。
        /// 要素の追加だけで旧挙動と一致するため版は上げていない
        /// </summary>
        public ScenePresetNodeVisibility nodeVisibility;
```

- [ ] **Step 4: Capture / Apply**

`ScenePresetManager.cs` の `state.maidScale = CaptureMaidScale(maid);` の次に足す:

```csharp
            state.nodeVisibility = ScenePresetNodeVisibility.FromOverrides(
                MaidNodeVisibilityController.GetOverrides(maid));
```

`ApplyMaidScale(maid, state);` の try/catch ブロックの次に足す:

```csharp
            try
            {
                ApplyNodeVisibility(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
```

`ApplyMaidScale` メソッドの次に足す:

```csharp
        /// <summary>
        /// ノード表示を復元する。旧プリセット (nodeVisibility 無し) では変更しない。
        /// 着替えの途中なら上書きを記録だけして、完了時に書かせる
        /// </summary>
        private static void ApplyNodeVisibility(Maid maid, ScenePresetMaid state)
        {
            if (state.nodeVisibility == null)
            {
                return;
            }
            MaidNodeVisibilityController.SetOverrides(maid, state.nodeVisibility.ToOverrides());
            MaidNodeVisibilityController.Flush(maid);
        }
```

- [ ] **Step 5: Undo**

`Manager/History/NodeVisibilitySnapshot.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ノード表示のスナップショット (SE の上書き一式)</summary>
    public class NodeVisibilitySnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, bool> _overrides;

        public static NodeVisibilitySnapshot Capture(Maid maid)
        {
            return new NodeVisibilitySnapshot
            {
                _capturedMaid = maid,
                _overrides = MaidNodeVisibilityController.GetOverrides(maid),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            MaidNodeVisibilityController.SetOverrides(maid, _overrides);
            MaidNodeVisibilityController.Flush(maid);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as NodeVisibilitySnapshot;
            if (o == null || o._overrides.Count != _overrides.Count)
            {
                return false;
            }
            foreach (var pair in _overrides)
            {
                bool value;
                if (!o._overrides.TryGetValue(pair.Key, out value) || value != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
```

`SnapshotFactory.cs` の `case HistoryScope.MaidScale:` の 2 行の次に足す:

```csharp
                case HistoryScope.NodeVisibility:
                    return NodeVisibilitySnapshot.Capture(maid);
```

csproj に、`Manager\History\MaidScaleSnapshot.cs` の行の隣へ足す:

```xml
    <Compile Include="Manager\History\NodeVisibilitySnapshot.cs" />
```

- [ ] **Step 6: テストが通ることを確かめる**

「ビルド＆テスト」を実行する。Expected: 全件 PASS。

---

### Task 5: 脱衣ウィンドウのタブとドキュメント

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidUndressWindow.cs`
- Modify: `docs-site/guide/maid-editing.md`、`docs-site/timeline/layers-maid.md`、`docs-site/timeline/compatibility.md`、`docs-site/guide/scene-preset.md`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（リポジトリ外。コミット対象外）

**Interfaces:**
- Consumes: `NodeVisibilityRowDrawer.Draw` / `RecordEdit`（Task 3）、`MaidNodeVisibilityController.SetAll` / `ClearAll` / `Flush`（Task 2）、`MaidNodeVisibilityNodes.nodes`（Task 1）

- [ ] **Step 1: 内部タブ**

`MaidUndressWindow` のフィールドに足す（`WINDOW_ID` の後）:

```csharp
        private static readonly int TAB_WIDTH = 100;

        /// <summary>ノード一覧の 1 段あたりのインデント幅</summary>
        private const float NODE_INDENT_WIDTH = 12f;

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum UndressTabType
        {
            脱衣,
            ノード表示,
        }

        private UndressTabType _tabType = UndressTabType.脱衣;
```

クラス summary の末尾に「ノード表示タブでは体の部位 (body のボーン) ごとの表示/非表示を上書きする」を足す。

`TryFocusTimelineLayer` を次にする:

```csharp
        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.UndressTimelineLayer))
            {
                _tabType = UndressTabType.脱衣;
                return true;
            }
            if (layerType == typeof(MTEP.NodeVisibilityTimelineLayer))
            {
                _tabType = UndressTabType.ノード表示;
                return true;
            }
            return false;
        }
```

`DrawMaidContent` の `TimelineLayerGate.Begin(...)` から末尾までを次にする:

```csharp
            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // タブ切替はゲートの対象外にするため、タブを描いた後で判定する
            if (_tabType == UndressTabType.ノード表示)
            {
                TimelineLayerGate.Begin(view, typeof(MTEP.NodeVisibilityTimelineLayer), target, ROW_HEIGHT);
                DrawNodeVisibility(view, target);
                return;
            }

            TimelineLayerGate.Begin(view, typeof(MTEP.UndressTimelineLayer), target, ROW_HEIGHT);

            DrawAllButtons(view, target);
            DrawCategoryList(view, target);
```

`DrawCostumeChangeList` の後に足す:

```csharp
        /// <summary>ノード表示タブ。一括操作行と、階層の深さでインデントしたノード一覧</summary>
        private void DrawNodeVisibility(GUIView view, Maid target)
        {
            if (!target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            view.BeginHorizontal();
            {
                if (view.DrawButton("全表示", 90, ROW_HEIGHT))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "全表示");
                    MaidNodeVisibilityController.SetAll(target, true);
                    MaidNodeVisibilityController.Flush(target);
                }
                if (view.DrawButton("全非表示", 90, ROW_HEIGHT))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "全非表示");
                    MaidNodeVisibilityController.SetAll(target, false);
                    MaidNodeVisibilityController.Flush(target);
                }
                if (view.DrawButton("リセット", 90, ROW_HEIGHT,
                    MaidNodeVisibilityController.HasOverrides(target)))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "リセット");
                    MaidNodeVisibilityController.ClearAll(target);
                    MaidNodeVisibilityController.Flush(target);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                NodeVisibilityRowDrawer.Draw(view, target, node, ROW_HEIGHT, node.depth * NODE_INDENT_WIDTH);
            }
            view.EndScrollView();
        }
```


- [ ] **Step 2: ユーザー向けドキュメント**

`docs-site/guide/maid-editing.md` に、「## 重力」の直前（無ければ「## ボーン編集」の直前）へ足す:

```markdown
## ノード表示

`メイド` → `脱衣` ウィンドウの `ノード表示` タブで、体の部位（胸・指・足首など 90 か所）を部位ごとに消したり、衣装が消している部位を表示し直したりできます。AlwaysColorChangeEx の「表示ノード選択」と同じ部位です。

- チェックは、その部位が今実際に表示されているかを表します。切り替えると SceneEditor の上書きになり、名前の後ろに `*` が付きます
- `解除` は、その部位の上書きを外して、衣装が決めた表示へ戻します。`リセット` は全部位の上書きを外します
- `全表示` / `全非表示` は、全部位を上書きします。`全表示` は衣装が消している部位も表示し直します
- 今と同じ状態へチェックを切り替えても上書きになります（別の衣装に替えても、その部位は表示/非表示のまま保たれます）
- 部位はそれぞれ単独で効きます（親の部位を消しても子は消えません）
- 消すのは体のメッシュだけで、ボーンや物理には影響しません
- 非表示の上書きは、脱衣や着替えをしても保たれます。表示の上書きは、着替えの完了時に新しい衣装へ書き直されます
- タイムラインの「メイドノード表示」レイヤーとシーンプリセットにも保存されます

```

`docs-site/timeline/layers-maid.md` の「## メイド重力」の直前に足す:

```markdown
## メイドノード表示

体の部位ごとの表示/非表示です。項目は body のボーン 90 か所で、脱衣ウィンドウの `ノード表示` タブで編集します（[メイド編集の「ノード表示」](/guide/maid-editing#ノード表示)）。

| 値 | 既定 | 補間 |
|---|---|---|
| `表示` | ON | なし（区間開始値） |

キーフレームに登録されるのは、SceneEditor が上書きしている部位と、このレイヤーに既にキーのある部位だけです。キーの無い部位は衣装が決めた表示のままです。最初のキーより前の区間では、その部位の上書きを外します。
途中で `解除` した部位は、次にキーを登録したとき、その時点の見た目（表示/非表示）でキーになります。
レイヤーを削除すると、全部位の上書きを外します。

::: warning
`メイドノード表示` レイヤーは SceneEditor 独自です。これを含むタイムラインは MTE や以前の SceneEditor では読み込めません。
:::

```

`docs-site/timeline/compatibility.md` の「`メイド重力` の `ローカル` は…」の行の直後に足す:

```markdown
- `メイドノード表示` レイヤーは SceneEditor 独自です。これを含む XML は MTE や以前の SceneEditor で読み込みに失敗します
```

`docs-site/guide/scene-preset.md` の 17 行目の列挙「…脱衣・重力・揺れ物理・ボーン編集の差分」を「…脱衣・ノード表示・重力・揺れ物理・ボーン編集の差分」にする。

- [ ] **Step 3: CLAUDE.md の互換の注意**

`W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」節で、「重力キーのローカル」の行の直後に足す:

```markdown
- メイドノード表示レイヤー（`NodeVisibilityTimelineLayer`、body のボーン 90 件の表示 bool、`TransformType.NodeVisibility`）は SE 独自。version は上げていない。これを含む XML は MTE・旧 SE では未知の enum 値（`<Type>NodeVisibility</Type>`）でデシリアライズに失敗する。キーにするのは SE が上書きしているノードだけ。シーンプリセットは版を上げずに maid へ `nodeVisibility` 要素（上書きのあるノードだけ `<node name visible>`。上書きが無くても空要素を書く）を追加。要素の無い旧プリセットは適用時に触らない
```

- [ ] **Step 4: ビルド＆テスト**

「ビルド＆テスト」を実行する。Expected: 両構成のビルドが成功し、全テスト PASS。

---

## 実機確認（ループの段 3 で行う。計画の実装範囲外）

spec「C. 実機確認」の項目を devbridge で確かめる。補足は次のとおり。

- **型の参照**: `COM3D2.SceneEditor.Plugin.MaidNodeVisibilityController` は REPL から完全修飾名で直接呼べる（B の段 3 で `MaidManipulateManager` を直接参照できた）。
- **状態の変更と読み取り**: `SetOverride` → `Flush` を呼んだら、翌フレーム以降に次の 2 つを読む。
  - `goSlot[0].m_dicDelNodeBody["Mune_L"]`
  - `screenshot`（エディタウィンドウモードの GameView）
- **強制表示**: 検証環境では onepiece に false のノードが 14 件ある。その 1 件を強制表示して全スロットの dict が true になること、解除で false に戻ることを見る。
- **着替え**: `GetProp(MPN.onepiece).boDut = true` → `AllProcPropSeqStart()` で再処理させる。完了後に、非表示の上書きが dict に再び書かれていることを見る。旧ボディ（menu が `_ALL_` を上書き）として確かめる。CRC（dict を Clear）はメイドがいれば別に確かめる。
- **強制表示中の着替え**: 強制表示したまま onepiece を再処理し、完了後に onepiece の dict が true になっていることを見る。解除すると、menu が書いた false に戻ること。
- **脱衣**: 脱衣（`SetMask`）は `MaidUndressController.SetAllUndressed(maid, true/false)` で切り替える。切り替えのあとも、body の dict の false が残ることを見る。
- **タイムライン**: 次の手順で確かめる。
  1. エディタを有効化する。
  2. `TimelineManager` でノード表示レイヤーを追加し、2 フレームにキーを打つ（例: 0 で表示、10 で非表示）。
  3. `SeekCurrentFrame` で表示が切り替わること。
  4. 最初のキーより前（そのノードのキーが 10 からだけなら 5）へシークすると、上書きが外れること。
- **シーンプリセット**: 一時プリセットに保存して読み直すと上書きが戻ることを見る。確認後に一時プリセットを削除する。
- **CRC ボディ**: CRC ボディのメイドがいれば、`morph.BoneNames` と dict に 90 ノードがあることを見る。いなければユーザー確認待ちに回す。

## レビュー却下メモ

plan-review（自己申告 🔴、取り込み後 🟡。取り込み 7 件・却下 3 件）:

- 取り込み:
  - 🔴 タイムラインで「上書きなし」を表せない問題: 値は bool のまま（spec どおり）にした。最初のキーより前の区間で解除する `ApplyPlayData` と、キー済みノードを今の表示で書き出す `BuildKeys` で対処した。
  - 退避値は SE の書いた値のときだけ戻す。
  - 立ち上がり直後の Release・ClearAll のための `SyncBusy`。
  - 旧ボディと CRC で着替えの dict の扱いが違うことを計画と実機確認に反映した。
  - `SetAll` の dict 収集を 1 回にした。
  - 全表示が node消去 を打ち消す旨を docs に追記した。
  - 最初のキーより前へのシークを実機確認に追加した。
- キー値を 3 値（解除 / 表示 / 非表示）にする案 — spec が「値は bool 1 個」と決めている。上の 2 点で、bool のまま実害の大きい「シークで戻しても上書きが残る」「解除がキーにならない」は解消できるため、見送った。
- `FixMaskFlag` を外す案 — spec と ACCEx の手順（`FixMaskFlag` → `FixVisibleFlag`）どおりに残す。SE は `boVisible` を直書きしていないので、巻き戻す対象が無い。
- SE 無効中の着替えで再同期されない — 重力（`MaidGravityController`）も同じ前提で、SE 無効中は `MaidManipulateManager.Update` 自体が回らない。次の着替えで `ApplyAll` が走れば戻るので、今回は扱わない（ユーザー確認待ちにも載せない）。
