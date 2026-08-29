using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class EyesItemInspectorTests
    {
        [Theory]
        [InlineData("EyesRot", EyesItemInspector.RowKind.EyeRotation)]
        [InlineData("LookAtTarget", EyesItemInspector.RowKind.LookAtTarget)]
        [InlineData("EyesPosL", EyesItemInspector.RowKind.Unsupported)]
        [InlineData("EyesPosR", EyesItemInspector.RowKind.Unsupported)]
        [InlineData("EyesScaL", EyesItemInspector.RowKind.Unsupported)]
        [InlineData("EyesScaR", EyesItemInspector.RowKind.Unsupported)]
        public void ResolveRowKind_項目名から編集UI種別を求める(
            string itemName, EyesItemInspector.RowKind expected)
        {
            Assert.Equal(expected, EyesItemInspector.ResolveRowKind(itemName));
        }

        [Fact]
        public void ResolveRowKind_未知の項目名は未対応扱い()
        {
            Assert.Equal(
                EyesItemInspector.RowKind.Unsupported,
                EyesItemInspector.ResolveRowKind("UnknownItem"));
        }

        /// <summary>
        /// レイヤーが項目を増減させたときに気づけるよう、
        /// 実際のメニュー項目名から編集UIを持つ項目が 1 つずつであることを固定する
        /// </summary>
        [Fact]
        public void 瞳レイヤーの項目のうち編集UIを持つのは瞳回転と注視先だけ()
        {
            var names = MTEP.EyesTimelineLayer.saveEyesNames;
            var rotationCount = 0;
            var lookAtCount = 0;
            foreach (var name in names)
            {
                switch (EyesItemInspector.ResolveRowKind(name))
                {
                    case EyesItemInspector.RowKind.EyeRotation:
                        rotationCount++;
                        break;
                    case EyesItemInspector.RowKind.LookAtTarget:
                        lookAtCount++;
                        break;
                }
            }

            Assert.Equal(1, rotationCount);
            Assert.Equal(1, lookAtCount);
        }
    }
}
