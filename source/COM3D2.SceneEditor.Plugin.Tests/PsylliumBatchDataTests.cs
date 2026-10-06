using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PsylliumBatchDataTests
    {
        private static void AssertVector(Vector4 expected, Vector4 actual)
        {
            Assert.Equal(expected.x, actual.x, 4);
            Assert.Equal(expected.y, actual.y, 4);
            Assert.Equal(expected.z, actual.z, 4);
            Assert.Equal(expected.w, actual.w, 4);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(1023, 1)]
        [InlineData(1024, 2)]
        [InlineData(2046, 2)]
        public void GetBatchCountRoundsUp(int barCount, int expected)
        {
            Assert.Equal(expected, PsylliumBatchBuffer.GetBatchCount(barCount));
        }

        [Fact]
        public void SetBarCountAllocatesFullCapacityArrays()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1030);
            Assert.Equal(2, buffer.batchCount);
            Assert.Equal(1030, buffer.barCount);
            Assert.Equal(PsylliumBatchBuffer.Capacity, buffer.positions[1].Length);
            Assert.Equal(PsylliumBatchBuffer.Capacity, buffer.ups[1].Length);
        }

        [Fact]
        public void SetBarCountZeroRemovesAllBatches()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(2046);
            buffer.SetBarCount(0);
            Assert.Equal(0, buffer.batchCount);
            Assert.Empty(buffer.ups);
        }

        [Fact]
        public void ShrinkClearsStaleSlots()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(5);
            buffer.Write(4, new Vector4(1, 2, 3, 1), new Vector4(0, 1, 0, 1));
            buffer.SetBarCount(3);
            AssertVector(Vector4.zero, buffer.positions[0][4]);
            AssertVector(Vector4.zero, buffer.ups[0][4]);
        }

        [Fact]
        public void ShrinkAcrossBatchClearsTailOfLastBatch()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1023);
            buffer.Write(1022, new Vector4(1, 2, 3, 1), new Vector4(0, 1, 0, 0));
            buffer.SetBarCount(1000);
            Assert.Equal(1, buffer.batchCount);
            AssertVector(Vector4.zero, buffer.positions[0][1022]);
        }

        [Fact]
        public void WriteGoesToBatchAndSlot()
        {
            var buffer = new PsylliumBatchBuffer();
            buffer.SetBarCount(1030);
            var position = new Vector4(1, 2, 3, 1);
            var up = new Vector4(0, 1, 0, 1);
            buffer.Write(1025, position, up);
            AssertVector(position, buffer.positions[1][2]);
            AssertVector(up, buffer.ups[1][2]);
        }

        [Fact]
        public void ComputeLocalWithIdentityAddsOffsets()
        {
            Vector4 position, up;
            PsylliumBarMath.ComputeLocal(
                new Vector3(1, 2, 3), Quaternion.identity,
                new Vector3(0.5f, 0, 0), Quaternion.identity, 1,
                out position, out up);
            AssertVector(new Vector4(1.5f, 2, 3, 1), position);
            AssertVector(new Vector4(0, 1, 0, 1), up);
        }

        [Fact]
        public void ComputeLocalComposesHandAndBarRotation()
        {
            const float h = 0.70710678f;
            var handRotation = new Quaternion(0, h, 0, h); // Y 軸 +90°
            var barRotation = new Quaternion(0, 0, h, h);  // Z 軸 +90°
            Vector4 position, up;
            PsylliumBarMath.ComputeLocal(
                new Vector3(10, 0, 0), handRotation,
                new Vector3(1, 0, 0), barRotation, 0,
                out position, out up);
            // (1,0,0) を Y +90° 回すと (0,0,-1)
            AssertVector(new Vector4(10, 0, -1, 1), position);
            // 上 (0,1,0) を Z +90° で (-1,0,0)、さらに Y +90° で (0,0,1)
            AssertVector(new Vector4(0, 0, 1, 0), up);
        }

        [Fact]
        public void BuildRepeatsBarShapeWithSlotIndex()
        {
            Vector3[] vertices;
            Vector2[] uv;
            Vector2[] uv2;
            int[] triangles;
            PsylliumBatchMesh.Build(0.5f, 0.1f, 2f, 0.3f, 0.2f, 3,
                out vertices, out uv, out uv2, out triangles);

            Assert.Equal(3 * PsylliumBatchMesh.VertexCountPerBar, vertices.Length);
            Assert.Equal(3 * PsylliumBatchMesh.IndexCountPerBar, triangles.Length);

            // 形は全バー共通
            Assert.Equal(vertices[4], vertices[2 * 8 + 4]);
            Assert.Equal(uv[7], uv[2 * 8 + 7]);
            // 先端 (index 4..7) は positionY + barHeight
            Assert.Equal(2.1f, vertices[4].y, 4);
            Assert.Equal(-0.5f, vertices[4].x, 4);
            // uv2.x は太さ方向のオフセット、uv2.y はスロット番号
            Assert.Equal(0.3f, uv2[2 * 8 + 6].x, 4);
            Assert.Equal(-0.3f, uv2[2 * 8 + 0].x, 4);
            Assert.Equal(2f, uv2[2 * 8 + 6].y, 4);
            // 三角形はスロットごとに頂点番号をずらす
            Assert.Equal(new[] { 16, 17, 18 }, new[] { triangles[36], triangles[37], triangles[38] });
            Assert.Equal(2 * 8 + 5, triangles[3 * 18 - 1]);
        }
    }
}
