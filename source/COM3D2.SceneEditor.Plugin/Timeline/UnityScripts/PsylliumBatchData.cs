using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// サイリウムのバーをまとめて描くためのシェーダー配列。
    /// 1 バッチ = 1 Renderer で、_BarPos / _BarUp に Capacity 本ぶん詰める
    /// </summary>
    public class PsylliumBatchBuffer
    {
        /// <summary>
        /// シェーダー配列の要素数。Unity の配列プロパティの上限 (1023) に合わせる。
        /// PsylliumBatchVert.cginc の PSYLLIUM_BATCH_CAPACITY と必ず同じ値にすること。
        /// 配列長は最初の SetVectorArray で固定されるため、常にこの長さで渡す
        /// </summary>
        public const int Capacity = 1023;

        /// <summary>xyz = エリアローカルの根元位置、w = 1 なら有効なスロット</summary>
        public readonly List<Vector4[]> positions = new List<Vector4[]>();

        /// <summary>xyz = エリアローカルの上方向、w = 色番号</summary>
        public readonly List<Vector4[]> ups = new List<Vector4[]>();

        public int barCount { get; private set; }

        public int batchCount
        {
            get { return positions.Count; }
        }

        public static int GetBatchCount(int barCount)
        {
            return (barCount + Capacity - 1) / Capacity;
        }

        /// <summary>
        /// 本数を変える。count 以降のスロットは無効 (w = 0) にする。
        /// 前回の位置が残ると、減ったはずのバーが描かれ続けるため
        /// </summary>
        public void SetBarCount(int count)
        {
            if (count < 0)
            {
                count = 0;
            }

            var needed = GetBatchCount(count);
            while (positions.Count < needed)
            {
                positions.Add(new Vector4[Capacity]);
                ups.Add(new Vector4[Capacity]);
            }
            while (positions.Count > needed)
            {
                positions.RemoveAt(positions.Count - 1);
                ups.RemoveAt(ups.Count - 1);
            }

            var end = needed * Capacity;
            for (int i = count; i < end; i++)
            {
                positions[i / Capacity][i % Capacity] = Vector4.zero;
                ups[i / Capacity][i % Capacity] = Vector4.zero;
            }

            barCount = count;
        }

        /// <summary>別スレッドから呼ぶ。バーごとに書き込み先が分かれるのでロックは要らない</summary>
        public void Write(int barIndex, Vector4 position, Vector4 up)
        {
            positions[barIndex / Capacity][barIndex % Capacity] = position;
            ups[barIndex / Capacity][barIndex % Capacity] = up;
        }
    }

    public static class PsylliumBarMath
    {
        /// <summary>
        /// バー 1 本のエリアローカルの根元位置と上方向を求める。
        /// 旧来の「エリア → 手 → バー」の Transform 階層（手・バーのスケールは 1）と同じ合成をする。
        /// 別スレッドから呼ぶため Unity のネイティブ API を使わないこと
        /// </summary>
        public static void ComputeLocal(
            Vector3 handPosition,
            Quaternion handRotation,
            Vector3 barPosition,
            Quaternion barRotation,
            int colorIndex,
            out Vector4 position,
            out Vector4 up)
        {
            var p = handPosition + handRotation * barPosition;
            var u = handRotation * (barRotation * Vector3.up);
            position = new Vector4(p.x, p.y, p.z, 1f);
            up = new Vector4(u.x, u.y, u.z, colorIndex);
        }
    }

    public static class PsylliumBatchMesh
    {
        public const int VertexCountPerBar = 8;
        public const int IndexCountPerBar = 18;

        private static readonly int[] BarTriangles = new int[] {
            0, 1, 2,
            1, 3, 2,
            1, 4, 3,
            4, 5, 3,
            4, 6, 5,
            6, 7, 5,
        };

        /// <summary>
        /// バー capacity 本ぶんの形を重ねたメッシュデータを作る。実際の位置はシェーダーが配列から決める。
        /// uv2.x は太さ方向のオフセット、uv2.y はシェーダー配列のスロット番号
        /// </summary>
        public static void Build(
            float halfWidth,
            float positionY,
            float barHeight,
            float barRadius,
            float topThreshold,
            int capacity,
            out Vector3[] vertices,
            out Vector2[] uv,
            out Vector2[] uv2,
            out int[] triangles)
        {
            var top = positionY + barHeight;
            var baseVertices = new Vector3[] {
                new Vector3(-halfWidth, positionY, 0),
                new Vector3(-halfWidth, positionY, 0),
                new Vector3( halfWidth, positionY, 0),
                new Vector3( halfWidth, positionY, 0),
                new Vector3(-halfWidth, top, 0),
                new Vector3( halfWidth, top, 0),
                new Vector3(-halfWidth, top, 0),
                new Vector3( halfWidth, top, 0),
            };
            var baseUv = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(0, topThreshold),
                new Vector2(1, 0),
                new Vector2(1, topThreshold),
                new Vector2(0, 1 - topThreshold),
                new Vector2(1, 1 - topThreshold),
                new Vector2(0, 1),
                new Vector2(1, 1),
            };
            var baseRadius = new float[] {
                -barRadius, 0, -barRadius, 0, 0, 0, barRadius, barRadius,
            };

            vertices = new Vector3[capacity * VertexCountPerBar];
            uv = new Vector2[capacity * VertexCountPerBar];
            uv2 = new Vector2[capacity * VertexCountPerBar];
            triangles = new int[capacity * IndexCountPerBar];

            for (int slot = 0; slot < capacity; slot++)
            {
                var vertexOffset = slot * VertexCountPerBar;
                for (int i = 0; i < VertexCountPerBar; i++)
                {
                    vertices[vertexOffset + i] = baseVertices[i];
                    uv[vertexOffset + i] = baseUv[i];
                    uv2[vertexOffset + i] = new Vector2(baseRadius[i], slot);
                }

                var indexOffset = slot * IndexCountPerBar;
                for (int i = 0; i < IndexCountPerBar; i++)
                {
                    triangles[indexOffset + i] = vertexOffset + BarTriangles[i];
                }
            }
        }
    }
}
