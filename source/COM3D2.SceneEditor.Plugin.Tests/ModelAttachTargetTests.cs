using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデル → モデルのアタッチの純粋ロジック。
    /// 循環や適用順がずれると、Unity の親子が壊れる・ワールド補間が 1 フレーム遅れる
    /// </summary>
    public class ModelAttachTargetTests
    {
        private static System.Func<string, string> Parents(Dictionary<string, string> map)
        {
            return name => map.TryGetValue(name, out var parent) ? parent : null;
        }

        [Theory]
        [InlineData("cup.menu", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void モデル名があればモデルへのアタッチ(string reference, bool expected)
        {
            Assert.Equal(expected, ModelAttachTarget.IsModelTarget(reference));
        }

        [Fact]
        public void 参照がモデル名そのものなら原点を指す()
        {
            var models = new HashSet<string> { "cup.menu", "desk.menu (2)" };
            Assert.True(ModelAttachTarget.TryResolveReference("desk.menu (2)", models.Contains, out var model, out var bone));
            Assert.Equal("desk.menu (2)", model);
            Assert.Equal("", bone);
        }

        [Fact]
        public void 将来のボーン参照はモデル名とボーン名に分かれる()
        {
            var models = new HashSet<string> { "cup.menu" };
            Assert.True(ModelAttachTarget.TryResolveReference("cup.menu/Bone01/Tip", models.Contains, out var model, out var bone));
            Assert.Equal("cup.menu", model);
            Assert.Equal("Bone01/Tip", bone);
        }

        [Fact]
        public void 存在しないモデルの参照は解決できない()
        {
            var models = new HashSet<string> { "cup.menu" };
            Assert.False(ModelAttachTarget.TryResolveReference("desk.menu", models.Contains, out _, out _));
            Assert.False(ModelAttachTarget.TryResolveReference("", models.Contains, out _, out _));
        }

        [Fact]
        public void 自分自身と子孫へは付けられない()
        {
            // c → b → a の鎖
            var parents = Parents(new Dictionary<string, string> { { "b", "a" }, { "c", "b" } });
            Assert.True(ModelAttachTarget.WouldCreateCycle("a", "a", parents));
            Assert.True(ModelAttachTarget.WouldCreateCycle("a", "c", parents));
            Assert.False(ModelAttachTarget.WouldCreateCycle("c", "a", parents));
            Assert.False(ModelAttachTarget.WouldCreateCycle("d", "c", parents));
        }

        [Fact]
        public void 既に循環しているデータでも判定が止まる()
        {
            var parents = Parents(new Dictionary<string, string> { { "a", "b" }, { "b", "a" } });
            Assert.False(ModelAttachTarget.WouldCreateCycle("x", "a", parents));
        }

        [Fact]
        public void 親が先に並ぶ_関係の無いものは元の順を保つ()
        {
            var parents = Parents(new Dictionary<string, string> { { "child", "parent" }, { "grand", "child" } });
            var result = new List<string>();
            ModelAttachTarget.SortParentsFirst(new List<string> { "grand", "x", "child", "parent", "y" }, parents, result);
            Assert.Equal(new[] { "x", "parent", "y", "child", "grand" }, result);
        }

        [Fact]
        public void 終点側の親も子より先に並ぶ()
        {
            // ワールド補間は始点と終点の両方の親の今フレームの姿勢を読む
            var none = Parents(new Dictionary<string, string>());
            var endParents = Parents(new Dictionary<string, string> { { "x", "c" }, { "y", "x" } });
            var result = new List<string>();
            ModelAttachTarget.SortParentsFirst(new List<string> { "y", "x", "c" }, none, endParents, result);
            Assert.Equal(new[] { "c", "x", "y" }, result);
        }

        [Fact]
        public void 始点と終点の親のうち深い方に合わせる()
        {
            var startParents = Parents(new Dictionary<string, string> { { "child", "a" } });
            var endParents = Parents(new Dictionary<string, string> { { "child", "b" }, { "b", "a" } });
            var result = new List<string>();
            ModelAttachTarget.SortParentsFirst(new List<string> { "child", "b", "a" }, startParents, endParents, result);
            Assert.Equal(new[] { "a", "b", "child" }, result);
        }

        [Fact]
        public void 循環していても全員が一度ずつ並ぶ()
        {
            var parents = Parents(new Dictionary<string, string> { { "a", "b" }, { "b", "a" } });
            var result = new List<string>();
            ModelAttachTarget.SortParentsFirst(new List<string> { "a", "b" }, parents, result);
            Assert.Equal(2, result.Count);
            Assert.Contains("a", result);
            Assert.Contains("b", result);
        }

        [Fact]
        public void 親基準の値を配置ルート基準へ変換する()
        {
            // 親: (1,2,3)、Y 軸 +90°、拡縮 2。ルート: 原点・無回転・拡縮 1
            var s = Mathf.Sqrt(0.5f);
            var parent = new ModelAttachPose
            {
                position = new Vector3(1f, 2f, 3f),
                rotation = new Quaternion(0f, s, 0f, s),
                lossyScale = new Vector3(2f, 2f, 2f),
            };
            var root = new ModelAttachPose
            {
                position = Vector3.zero,
                rotation = new Quaternion(0f, 0f, 0f, 1f),
                lossyScale = Vector3.one,
            };

            var position = new Vector3(1f, 0f, 0f);
            var rotation = new Quaternion(0f, 0f, 0f, 1f);
            var scale = new Vector3(1f, 0.5f, 1f);
            ModelAttachTarget.ConvertToRoot(parent, root, ref position, ref rotation, ref scale);

            // Y +90° で +X は -Z へ向く: (1,2,3) + (0,0,-2)
            Assert.Equal(1f, position.x, 4);
            Assert.Equal(2f, position.y, 4);
            Assert.Equal(1f, position.z, 4);
            Assert.Equal(s, rotation.y, 4);
            Assert.Equal(s, rotation.w, 4);
            Assert.Equal(2f, scale.x, 4);
            Assert.Equal(1f, scale.y, 4);
        }

        [Fact]
        public void 配置ルートが動いていてもルート基準へ戻す()
        {
            var identity = new Quaternion(0f, 0f, 0f, 1f);
            var parent = new ModelAttachPose { position = new Vector3(5f, 0f, 0f), rotation = identity, lossyScale = Vector3.one };
            var root = new ModelAttachPose { position = new Vector3(2f, 0f, 0f), rotation = identity, lossyScale = new Vector3(2f, 2f, 2f) };

            var position = new Vector3(1f, 0f, 0f);
            var rotation = identity;
            var scale = Vector3.one;
            ModelAttachTarget.ConvertToRoot(parent, root, ref position, ref rotation, ref scale);

            // ワールド (6,0,0) をルート (2,0,0)・拡縮 2 で割り戻す
            Assert.Equal(2f, position.x, 4);
            Assert.Equal(0.5f, scale.x, 4);
        }
    }
}
