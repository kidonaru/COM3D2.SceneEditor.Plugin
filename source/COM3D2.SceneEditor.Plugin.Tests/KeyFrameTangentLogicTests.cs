using COM3D2.SceneEditor.Plugin;
using System.Collections.Generic;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class KeyFrameTangentLogicTests
    {
        private static MTEP.TangentPair Pair(float outT, float inT)
        {
            return new MTEP.TangentPair { outTangent = outT, inTangent = inT };
        }

        [Fact]
        public void 全要素が一致すればその値を返す()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.5f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.Equal(0.5f, outT);
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 混在した側だけNaNになる()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.7f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 空ならどちらもNaN()
        {
            KeyFrameTangentLogic.GetUniformTangents(new List<MTEP.TangentPair>(), out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.True(float.IsNaN(inT));
        }

        [Fact]
        public void ハンドル位置_Out側は始点から右上へ伸びる()
        {
            // 勾配 1 (線形) なら右へ 1・上へ 1 の向き。長さ√2 で 1px ずつ進む
            var pos = KeyFrameTangentLogic.GetHandlePos(true, 1f, 100f, Mathf.Sqrt(2f));
            Assert.Equal(1f, pos.x, 3);
            Assert.Equal(99f, pos.y, 3);
        }

        [Fact]
        public void ハンドル位置_勾配0なら水平に伸びる()
        {
            var pos = KeyFrameTangentLogic.GetHandlePos(true, 0f, 100f, 10f);
            Assert.Equal(10f, pos.x, 3);
            Assert.Equal(100f, pos.y, 3);
        }

        [Fact]
        public void ハンドル位置_In側は終点から左下へ伸びる()
        {
            var pos = KeyFrameTangentLogic.GetHandlePos(false, 0f, 100f, 10f);
            Assert.Equal(90f, pos.x, 3);
            Assert.Equal(0f, pos.y, 3);
        }

        [Fact]
        public void マウス位置から正規化タンジェントを求める()
        {
            // 始点 (0,100) から右へ 50・上へ 50 の点は勾配 1
            Assert.True(KeyFrameTangentLogic.TryGetNormalizedTangent(
                true, new Vector2(50f, 50f), 100f, out var value));
            Assert.Equal(1f, value, 3);
        }

        [Fact]
        public void ハンドル位置_混在NaNは勾配0として水平に描く()
        {
            var pos = KeyFrameTangentLogic.GetHandlePos(true, float.NaN, 100f, 10f);
            Assert.Equal(10f, pos.x, 3);
            Assert.Equal(100f, pos.y, 3);
        }

        [Fact]
        public void マウスが原点の真上なら勾配が発散して求まらない()
        {
            // dx = 0 は勾配が定まらない (Infinity になる) ので false
            Assert.False(KeyFrameTangentLogic.TryGetNormalizedTangent(
                true, new Vector2(0f, 20f), 100f, out _));
        }

        [Fact]
        public void マウスが逆側なら求まらない()
        {
            // Out ハンドルは始点より右側だけが有効
            Assert.False(KeyFrameTangentLogic.TryGetNormalizedTangent(
                true, new Vector2(-5f, 50f), 100f, out _));
            // In ハンドルは終点より左側だけが有効
            Assert.False(KeyFrameTangentLogic.TryGetNormalizedTangent(
                false, new Vector2(150f, 50f), 100f, out _));
        }
    }
}
