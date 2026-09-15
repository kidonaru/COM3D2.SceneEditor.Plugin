using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// メイドカテゴリのレイヤーがメイド単位 (hasSlotNo) であることを固定する。
    /// TimelineLayerGateHost はカテゴリからスロット番号の要否を決めるため、
    /// この対応が崩れると外部プラグインのゲート判定が別メイドを見る
    /// </summary>
    public class MaidLayerSlotNoTests
    {
        // コンストラクタが UnityEngine.Debug.Log を呼ぶためゲーム外では new できない。
        // hasSlotNo は状態を読まないので未初期化インスタンスで判定できる
        private static ITimelineLayer CreateUninitialized(Type type)
        {
            return (ITimelineLayer)FormatterServices.GetUninitializedObject(type);
        }

        [Fact]
        public void メイドカテゴリのレイヤーはすべてメイド単位である()
        {
            var types = TimelineLayerTestUtils.GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            var maidTypes = types
                .Where(t => t.GetCustomAttribute<TimelineLayerDescAttribute>()?.Category
                            == TimelineLayerCategory.Maid)
                .ToList();
            Assert.NotEmpty(maidTypes);

            foreach (var type in maidTypes)
            {
                Assert.True(CreateUninitialized(type).hasSlotNo,
                    type.Name + " はメイドカテゴリなのに hasSlotNo が false");
            }
        }

        [Fact]
        public void メイド衣装レイヤーはメイド単位である()
        {
            Assert.True(CreateUninitialized(typeof(DressTimelineLayer)).hasSlotNo);
        }
    }
}
