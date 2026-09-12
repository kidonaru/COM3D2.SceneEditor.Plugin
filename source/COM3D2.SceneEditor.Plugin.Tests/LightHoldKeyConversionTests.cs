using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 旧ライト補間トグル OFF の段差を保持キーで再現する変換の単体テスト。
    /// レイヤー生成は Unity 依存のため、キー生成 (CreateHoldKey) と適用判定だけを固定する
    /// </summary>
    public class LightHoldKeyConversionTests
    {
        private static TransformDataLight NewLight(float intensity, Color color, Vector3 position)
        {
            var trans = TimelineManager.CreateTransform<TransformDataLight>("Light1");
            trans.intensity = intensity;
            trans.color = color;
            trans.position = position;
            return trans;
        }

        [Theory]
        [InlineData(33, false, true, true)]
        [InlineData(33, true, false, true)]
        [InlineData(33, true, true, false)]
        [InlineData(34, false, false, false)]
        public void 旧バージョンでトグルがOFFのときだけ変換する(
            int version, bool colorEasing, bool extraEasing, bool expected)
        {
            Assert.Equal(expected, LightHoldKeyConversion.IsRequired(version, colorEasing, extraEasing));
        }

        [Fact]
        public void 保持対象に変化が無い区間はキーを作らない()
        {
            var start = NewLight(1f, Color.red, Vector3.zero);
            var end = NewLight(1f, Color.red, new Vector3(10f, 0f, 0f));

            var hold = LightHoldKeyConversion.CreateHoldKey(start, end, 0f, 1f, 0.5f, true, true);

            Assert.Null(hold);
        }

        [Fact]
        public void 拡張補間OFFなら数値は開始キーの値を保持し色は補間する()
        {
            var start = NewLight(1f, Color.black, Vector3.zero);
            var end = NewLight(3f, Color.white, Vector3.zero);

            var hold = LightHoldKeyConversion.CreateHoldKey(start, end, 0f, 1f, 0.5f, false, true);

            Assert.NotNull(hold);
            Assert.Equal("Light1", hold.name);
            Assert.Equal(1f, hold.intensity);
            Assert.Equal(0.5f, hold.color.r, 3);
        }

        [Fact]
        public void 色補間OFFなら色は開始キーの値を保持する()
        {
            var start = NewLight(1f, Color.black, Vector3.zero);
            var end = NewLight(3f, Color.white, Vector3.zero);

            var hold = LightHoldKeyConversion.CreateHoldKey(start, end, 0f, 1f, 0.5f, true, false);

            Assert.NotNull(hold);
            Assert.Equal(0f, hold.color.r, 3);
            // 拡張補間は ON 扱いなので intensity は補間値 (フラットなタンジェント既定値では中点にならないが、開始値ではない)
            Assert.NotEqual(1f, hold.intensity);
        }

        [Fact]
        public void 補間対象外のチャンネルは開始キーの値を引き継ぐ()
        {
            var start = NewLight(1f, Color.red, Vector3.zero);
            start.visible = false;
            start.maidSlotNo = 2;
            var end = NewLight(2f, Color.red, Vector3.zero);
            end.visible = true;
            end.maidSlotNo = 0;

            var hold = LightHoldKeyConversion.CreateHoldKey(start, end, 0f, 1f, 0.5f, true, true);

            Assert.False(hold.visible);
            Assert.Equal(2, hold.maidSlotNo);
        }

        [Fact]
        public void 挿入キーのタンジェントは自動補間になる()
        {
            var start = NewLight(1f, Color.red, Vector3.zero);
            var end = NewLight(2f, Color.red, Vector3.zero);

            var hold = LightHoldKeyConversion.CreateHoldKey(start, end, 0f, 1f, 0.5f, true, true);

            foreach (var value in hold.values)
            {
                Assert.True(value.inTangent.isSmooth);
                Assert.True(value.outTangent.isSmooth);
            }
        }
    }
}
