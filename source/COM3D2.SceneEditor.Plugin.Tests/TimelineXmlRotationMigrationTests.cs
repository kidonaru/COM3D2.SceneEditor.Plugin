using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 35 でのオイラー角 → クォータニオン移行を固定する。
    /// 値・タンジェント・スムーズビットのいずれかだけがずれると添字が食い違い、
    /// 既存タイムラインが黙って壊れるため
    /// </summary>
    public class TimelineXmlRotationMigrationTests
    {
        /// <summary>添字 eulerIndex から 3 値をオイラー角とする、count 個の連番レコードを作る</summary>
        private static TransformXml CreateTransform(TransformType type, int count, int eulerIndex, Vector3 eulerAngles)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                // 移行後に「どの値がどこへ動いたか」を追えるよう連番を入れる
                values[i] = 100f + i;
            }
            values[eulerIndex] = eulerAngles.x;
            values[eulerIndex + 1] = eulerAngles.y;
            values[eulerIndex + 2] = eulerAngles.z;

            var inTangents = new float[count];
            var outTangents = new float[count];
            for (var i = 0; i < count; i++)
            {
                inTangents[i] = 200f + i;
                outTangents[i] = 300f + i;
            }

            return new TransformXml
            {
                name = "test",
                type = type,
                values = values,
                inTangents = inTangents,
                outTangents = outTangents,
                // 全ビット立てておくと挿入位置のビットが複製されたか判別できないため、
                // オイラー X のビットだけ立てる
                inSmoothBit = 1L << eulerIndex,
                outSmoothBit = 1L << eulerIndex,
            };
        }

        /// <summary>1 レコードだけを持つ version 指定の TimelineXml を作る</summary>
        private static TimelineXml CreateTimeline(int version, TransformXml transform)
        {
            // FrameXml.bones は既定が null なので明示的に作る
            var keyFrame = new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = transform } },
            };
            var layer = new TimelineLayerXml { className = "TestLayer" };
            layer.keyFrames.Add(keyFrame);

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        [Fact]
        public void ConvertEulerToRotation_オイラー3値がクォータニオン4値になる()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.PngObject, 31, 3, eulerAngles);

            TimelineXml.ConvertEulerToRotation(transform, 3);

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(32, transform.values.Length);
            Assert.Equal(expected.x, transform.values[3], 4);
            Assert.Equal(expected.y, transform.values[4], 4);
            Assert.Equal(expected.z, transform.values[5], 4);
            Assert.Equal(expected.w, transform.values[6], 4);
        }

        [Fact]
        public void ConvertEulerToRotation_挿入位置より後ろの値が1つずれる()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // 旧添字 6 (=106) が 7 へ、旧添字 30 (=130) が 31 へ動く
            Assert.Equal(106f, transform.values[7]);
            Assert.Equal(130f, transform.values[31]);
            // 挿入位置より前は動かない
            Assert.Equal(100f, transform.values[0]);
            Assert.Equal(102f, transform.values[2]);
        }

        [Fact]
        public void ConvertEulerToRotation_回転4成分のタンジェントは捨てて後続はずらす()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(32, transform.inTangents.Length);
            Assert.Equal(32, transform.outTangents.Length);
            // 回転 4 成分 (3〜6) はオイラー角空間の比なので持ち越さない
            for (var i = 3; i <= 6; i++)
            {
                Assert.Equal(0f, transform.inTangents[i]);
                Assert.Equal(0f, transform.outTangents[i]);
            }
            // 旧添字 6 は 7 へ
            Assert.Equal(206f, transform.inTangents[7]);
            Assert.Equal(306f, transform.outTangents[7]);
            // 挿入位置より前は動かない
            Assert.Equal(200f, transform.inTangents[0]);
            Assert.Equal(300f, transform.outTangents[0]);
        }

        [Fact]
        public void ConvertEulerToRotation_回転4成分のスムーズビットを立てて後続はずらす()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));
            // 旧添字 6 のビットも立てて、ずれることを見る
            transform.inSmoothBit |= 1L << 6;
            transform.outSmoothBit |= 1L << 6;

            TimelineXml.ConvertEulerToRotation(transform, 3);

            // 回転 4 成分 (3〜6) は自動補間へ倒す。旧添字 6 のビットは 7 へずれる
            Assert.Equal((1L << 3) | (1L << 4) | (1L << 5) | (1L << 6) | (1L << 7), transform.inSmoothBit);
            Assert.Equal((1L << 3) | (1L << 4) | (1L << 5) | (1L << 6) | (1L << 7), transform.outSmoothBit);
        }

        [Fact]
        public void ConvertEulerToRotation_値数の足りないレコードは変換しない()
        {
            var transform = new TransformXml
            {
                name = "test",
                type = TransformType.PngObject,
                values = new float[] { 1f, 2f },
            };

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(2, transform.values.Length);
        }

        [Fact]
        public void ConvertEulerToRotation_タンジェントが短くても値と長さが揃う()
        {
            var transform = CreateTransform(TransformType.PngObject, 31, 3, new Vector3(30f, 40f, 50f));
            // タンジェントだけ極端に短い壊れかけのデータ
            transform.inTangents = new float[] { 200f, 201f };
            transform.outTangents = null;

            TimelineXml.ConvertEulerToRotation(transform, 3);

            Assert.Equal(32, transform.values.Length);
            // 短いタンジェントは触らない。FromXml 側が不足分を 0 で埋める
            Assert.Equal(2, transform.inTangents.Length);
            Assert.Null(transform.outTangents);
        }

        [Fact]
        public void Initialize_据え置き型は変換しない()
        {
            // ステージライト一括制御の回転は min/max の範囲なので据え置く
            var transform = CreateTransform(TransformType.StageLightController, 37, 6, new Vector3(30f, 40f, 50f));
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            Assert.Equal(37, transform.values.Length);
            Assert.Equal(30f, transform.values[6]);
        }

        [Fact]
        public void Initialize_サイリウムの手は左右2スロットともクォータニオン化する()
        {
            var left = new Vector3(30f, 40f, 50f);
            var right = new Vector3(-30f, -40f, -50f);

            var transform = CreateTransform(TransformType.PsylliumTransform, 12, 6, left);
            transform.values[9] = right.x;
            transform.values[10] = right.y;
            transform.values[11] = right.z;

            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expectedLeft = QuaternionUtils.EulerToQuaternion(left);
            var expectedRight = QuaternionUtils.EulerToQuaternion(right);
            Assert.Equal(14, transform.values.Length);
            // 左手 6〜9
            Assert.Equal(expectedLeft.x, transform.values[6], 4);
            Assert.Equal(expectedLeft.w, transform.values[9], 4);
            // 右手 10〜13
            Assert.Equal(expectedRight.x, transform.values[10], 4);
            Assert.Equal(expectedRight.w, transform.values[13], 4);
            // 位置 (0〜5) は動かない
            Assert.Equal(100f, transform.values[0]);
            Assert.Equal(105f, transform.values[5]);
        }

        [Fact]
        public void Initialize_レーザー本体の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLaser, 24, 1, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(25, transform.values.Length);
            Assert.Equal(expected.w, transform.values[4], 4);
            // 旧添字 0 (PositionX = 100) は動かない
            Assert.Equal(100f, transform.values[0]);
            // 旧添字 4 (ColorR = 104) が 5 へ動く
            Assert.Equal(104f, transform.values[5]);
        }

        [Fact]
        public void Initialize_レーザー一括制御は本体姿勢だけをクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLaserController, 37, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(38, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // rotationMin (旧 31〜33 = 131〜133) は値のまま 32〜34 へずれる
            Assert.Equal(131f, transform.values[32]);
            Assert.Equal(133f, transform.values[34]);
        }

        [Fact]
        public void Initialize_ステージライト本体の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.StageLight, 23, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(24, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 7 (ColorR = 107) が 8 へ動く
            Assert.Equal(107f, transform.values[8]);
        }

        [Fact]
        public void Initialize_テキストの回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.Text, 20, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(21, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 6 (ScaleX = 106) が 7 へ動く
            Assert.Equal(106f, transform.values[7]);
        }

        [Fact]
        public void Initialize_PNG配置の回転をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.PngObject, 31, 3, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(32, transform.values.Length);
            Assert.Equal(expected.w, transform.values[6], 4);
            // 旧添字 30 (FixedPosZ = 130) が 31 へ動く
            Assert.Equal(130f, transform.values[31]);
        }

        [Fact]
        public void Initialize_リムライトの光源方向をクォータニオン化する()
        {
            var eulerAngles = new Vector3(30f, 40f, 50f);
            var transform = CreateTransform(TransformType.Rimlight, 25, 0, eulerAngles);
            var timeline = CreateTimeline(34, transform);

            timeline.Initialize();

            var expected = QuaternionUtils.EulerToQuaternion(eulerAngles);
            Assert.Equal(26, transform.values.Length);
            Assert.Equal(expected.w, transform.values[3], 4);
            // 旧添字 3 (ColorR = 103) が 4 へ動く
            Assert.Equal(103f, transform.values[4]);
        }

        [Fact]
        public void Initialize_旧28値リムライトはv32当時のレイアウトへ移行する()
        {
            var values = new float[28];
            for (var i = 0; i < 28; i++)
            {
                values[i] = 100f + i;
            }

            var transform = new TransformXml
            {
                name = "Rimlight",
                type = TransformType.Rimlight,
                values = values,
            };
            var timeline = CreateTimeline(31, transform);

            timeline.Initialize();

            // v32 当時の 25 値へ切り詰められ、その後 v35 で 26 値になる
            Assert.Equal(26, transform.values.Length);

            // MaskMode / ExcludeFace / ApplyHair は v32 当時の添字 16 / 17 / 18 へ
            // 既定値が書かれ、v35 の w 挿入で 17 / 18 / 19 へずれる
            var rimlight = TransformDataRimlight.defaultTrans;
            Assert.Equal(rimlight.maskModeInfo.defaultValue, transform.values[17], 4);
            Assert.Equal(rimlight.excludeFaceInfo.defaultValue, transform.values[18], 4);
            Assert.Equal(rimlight.applyHairInfo.defaultValue, transform.values[19], 4);
        }

        [Fact]
        public void Initialize_version35以降は再変換しない()
        {
            var transform = CreateTransform(TransformType.PngObject, 32, 3, new Vector3(30f, 40f, 50f));
            var timeline = CreateTimeline(35, transform);

            timeline.Initialize();

            Assert.Equal(32, transform.values.Length);
        }
    }
}
