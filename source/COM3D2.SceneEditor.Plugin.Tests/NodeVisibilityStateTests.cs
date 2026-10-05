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

        [Fact]
        public void 作り直されたスロットへは退避値を戻さない()
        {
            // 強制表示の後、node消去 しない衣装へ着替えて menu の _ALL_ が true を書いた場合。
            // 値は SE の書いた true と同じでも、スロットが作り直されているので古い false を戻してはいけない
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var slots = new List<Dictionary<string, bool>> { body, onepiece };
            var bodyToken = new object();
            var state = new NodeVisibilityState();
            state.Set(Node, true, slots, new List<object> { bodyToken, new object() });

            onepiece[Node] = true;
            var remade = new List<object> { bodyToken, new object() };
            state.RestoreAll(slots, remade);
            Assert.True(onepiece[Node]);

            state.ApplyAll(slots, remade);
            state.Clear(Node, slots, remade);
            Assert.True(onepiece[Node]);
        }

        [Fact]
        public void 同じスロットなら退避値を戻す()
        {
            var body = Dict(P(Node, true));
            var onepiece = Dict(P(Node, false));
            var slots = new List<Dictionary<string, bool>> { body, onepiece };
            var tokens = new List<object> { new object(), new object() };
            var state = new NodeVisibilityState();
            state.Set(Node, true, slots, tokens);

            state.Clear(Node, slots, tokens);

            Assert.False(onepiece[Node]);
        }

        [Fact]
        public void Clearは上書きがあったときだけtrueを返す()
        {
            var slots = new List<Dictionary<string, bool>> { Dict(P(Node, true)) };
            var state = new NodeVisibilityState();

            Assert.False(state.Clear(Node, slots));

            state.Set(Node, false, slots);
            Assert.True(state.Clear(Node, slots));
        }

        [Fact]
        public void TokensChangedは参照が1つでも違えばtrue()
        {
            var a = new object();
            var b = new object();

            Assert.False(NodeVisibilityState.TokensChanged(new List<object> { a, null }, new List<object> { a, null }));
            Assert.True(NodeVisibilityState.TokensChanged(new List<object> { a, b }, new List<object> { a, new object() }));
            Assert.True(NodeVisibilityState.TokensChanged(new List<object> { a }, new List<object> { a, b }));
            Assert.True(NodeVisibilityState.TokensChanged(null, new List<object> { a }));
        }
    }
}
