using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
// 囲みの名前空間 COM3D2.SceneEditor.Plugin にも PluginUtils があるため明示的に別名を張る
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 35 の移行でタンジェントを持ち越さないことを固定する。
    ///
    /// タンジェントは <c>normalizedValue</c>（そのキーの傾きを近傍の割線の傾きで割った比）として
    /// 保存されるが、この比はオイラー角空間の近傍差分から求めたもので、クォータニオン空間では意味を持たない。
    /// さらに値が変化しないチャンネルでは <see cref="TransformDataBase.UpdateTangent"/> が
    /// 勾配の代わりに絶対値 0.01 を使うため、比がそのまま「値の単位での寄与」になる。
    /// 度（±360）からクォータニオン成分（±1）へ約 1/100 に縮んだ値へ同じ比を当てると、
    /// 寄与が約 100 倍に効いて区間の途中が大きく振れる（実データで約 67 度の暴れを観測）
    /// </summary>
    public class RotationTangentMigrationTests
    {
        /// <summary>
        /// 実データで問題になった形を再現する。
        /// レーザー一括制御の回転（オイラー X = 添字 3）に、手動タンジェント（非スムーズ）で
        /// 大きな normalizedValue が入っているレコード
        /// </summary>
        private static TransformXml CreateControllerTransform()
        {
            var values = new float[37];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = 100f + i;
            }
            // 実データの値: euler = (-30, -105, 0)
            values[3] = -30f;
            values[4] = -105f;
            values[5] = 0f;

            var inTangents = new float[37];
            var outTangents = new float[37];
            for (var i = 0; i < 37; i++)
            {
                inTangents[i] = 200f + i;
                outTangents[i] = 300f + i;
            }
            // 実データのタンジェント（オイラー角空間で求めた比）
            inTangents[3] = 256.666656f;
            inTangents[4] = 5.22400665f;
            inTangents[5] = 0f;
            outTangents[3] = 0.8679467f;
            outTangents[4] = 0.8679467f;
            outTangents[5] = 0f;

            return new TransformXml
            {
                name = "StageLaserController (2)",
                type = TransformType.StageLaserController,
                values = values,
                inTangents = inTangents,
                outTangents = outTangents,
                // 回転 3 スロットは手動タンジェント（スムーズビットが落ちている）
                inSmoothBit = ~((1L << 3) | (1L << 4) | (1L << 5)),
                outSmoothBit = ~((1L << 3) | (1L << 4) | (1L << 5)),
            };
        }

        [Fact]
        public void ConvertEulerToRotation_回転4成分のタンジェントは持ち越さない()
        {
            var transform = CreateControllerTransform();

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // オイラー角空間で求めた比はクォータニオン空間では意味を持たないので捨てる
            for (var i = 3; i <= 6; i++)
            {
                Assert.Equal(0f, transform.inTangents[i]);
                Assert.Equal(0f, transform.outTangents[i]);
            }
        }

        [Fact]
        public void ConvertEulerToRotation_回転4成分は自動補間へ倒す()
        {
            var transform = CreateControllerTransform();

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // スムーズ（自動補間）にしておけば、クォータニオン空間の近傍から比が計算し直される
            for (var i = 3; i <= 6; i++)
            {
                Assert.True(((transform.inSmoothBit >> i) & 1) != 0, "inSmoothBit[" + i + "]");
                Assert.True(((transform.outSmoothBit >> i) & 1) != 0, "outSmoothBit[" + i + "]");
            }
        }

        [Fact]
        public void ConvertEulerToRotation_回転以外のタンジェントとスムーズビットは保つ()
        {
            var transform = CreateControllerTransform();

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // 挿入位置より前
            Assert.Equal(200f, transform.inTangents[0]);
            Assert.Equal(300f, transform.outTangents[0]);
            Assert.True(((transform.inSmoothBit >> 0) & 1) != 0);

            // 挿入位置より後ろは 1 つずれる（旧添字 6 が 7 へ）
            Assert.Equal(206f, transform.inTangents[7]);
            Assert.Equal(306f, transform.outTangents[7]);
            Assert.True(((transform.inSmoothBit >> 7) & 1) != 0);
        }

        /// <summary>
        /// ユーザーが踏んだ症状そのもの。1412F と 1419F のレーザー一括制御は同じ姿勢なのに、
        /// 区間の途中で約 67 度振れていた。移行を通したうえで両端が同じなら途中も動かないこと
        /// </summary>
        [Fact]
        public void 移行後は同じ姿勢のキーに挟まれた区間で姿勢が動かない()
        {
            // 1412F と 1419F の実データ。姿勢は同じで、1419F 側だけ手動タンジェントを持つ
            var startXml = CreateControllerTransform();
            startXml.inTangents = null;
            startXml.outTangents = null;
            startXml.inSmoothBit = -1L;
            startXml.outSmoothBit = -1L;
            var endXml = CreateControllerTransform();

            var keyFrame = new FrameXml
            {
                frameNo = 0,
                bones = new System.Collections.Generic.List<BoneXml>
                {
                    new BoneXml { transform = startXml },
                    new BoneXml { transform = endXml },
                },
            };
            var layer = new TimelineLayerXml { className = "StageLaserTimelineLayer" };
            layer.keyFrames.Add(keyFrame);
            var timeline = new TimelineXml { version = 34 };
            timeline.layers.Add(layer);

            timeline.Initialize();

            var start = new TransformDataStageLaserController();
            start.Initialize("StageLaserController (2)");
            start.FromXml(startXml);
            var end = new TransformDataStageLaserController();
            end.Initialize("StageLaserController (2)");
            end.FromXml(endXml);

            // 値が変化しないチャンネルで UpdateTangent が使う勾配 (絶対値 0.01 / dt) を与える
            var dt = 7 * (1f / 30f);
            foreach (var v in end.rotationValues)
            {
                v.inTangent.UpdateValue(0.01f / dt);
                v.outTangent.UpdateValue(0.01f / dt);
            }
            foreach (var v in start.rotationValues)
            {
                v.inTangent.UpdateValue(0.01f / dt);
                v.outTangent.UpdateValue(0.01f / dt);
            }

            var expected = start.rotation;
            for (var i = 0; i <= 7; i++)
            {
                var q = MTEP.PluginUtils.HermiteQuaternion(
                    0f, dt, start.rotationValues, end.rotationValues, i / 7f);
                Assert.Equal(0f, Quaternion.Angle(expected, q), 1);
            }
        }
    }
}
