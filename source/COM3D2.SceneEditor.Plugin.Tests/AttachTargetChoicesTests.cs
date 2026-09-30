using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// アタッチ先コンボの選択位置。キーの (スロット, モデル名) の組と選択肢の対応がずれると、
    /// 別のアタッチ先が選ばれて見える
    /// </summary>
    public class AttachTargetChoicesTests
    {
        private static List<AttachTargetChoice> Items()
        {
            return new List<AttachTargetChoice>
            {
                new AttachTargetChoice { label = "未選択", slotNo = -1, modelName = "" },
                new AttachTargetChoice { label = "メイド0", slotNo = 0, modelName = "" },
                new AttachTargetChoice { label = "モデル: 机", slotNo = -1, modelName = "desk.menu" },
            };
        }

        [Theory]
        [InlineData(-1, "", 0)]
        [InlineData(0, "", 1)]
        [InlineData(-1, "desk.menu", 2)]
        [InlineData(-1, "missing.menu", 0)]
        [InlineData(3, "", 0)]
        public void キーのアタッチ先に当たる選択肢を引く(int slotNo, string modelName, int expected)
        {
            Assert.Equal(expected, AttachTargetChoices.IndexOf(Items(), slotNo, modelName));
        }
    }
}
