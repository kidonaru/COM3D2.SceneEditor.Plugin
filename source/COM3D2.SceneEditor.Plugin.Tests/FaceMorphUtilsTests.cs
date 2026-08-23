using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // FaceMorphUtils は DCM MyConst のモーフ名表を値ごと持ち込んだもの。
    // キー文字列は XML の Bone 名そのものなので、カテゴリ分割と合算の整合が崩れると
    // 保存済みタイムラインのモーフが読めなくなる
    public class FaceMorphUtilsTests
    {
        private static readonly Dictionary<string, string>[] CategoryMaps =
        {
            FaceMorphUtils.eyeMorphJp,
            FaceMorphUtils.mayuMorphJp,
            FaceMorphUtils.mouthMorphJp,
            FaceMorphUtils.faceOptionMorphJp,
        };

        [Fact]
        public void カテゴリ辞書のキーは重複しない()
        {
            var allKeys = CategoryMaps.SelectMany(map => map.Keys).ToList();
            Assert.Equal(allKeys.Count, allKeys.Distinct().Count());
        }

        [Fact]
        public void saveMorphNamesはカテゴリ辞書のキー合算と一致する()
        {
            var expected = CategoryMaps.SelectMany(map => map.Keys).OrderBy(k => k);
            var actual = FaceMorphUtils.saveMorphNames.OrderBy(k => k);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void モーフ名からグループ名が全件解決できる()
        {
            foreach (var morphName in FaceMorphUtils.saveMorphNames)
            {
                Assert.True(FaceMorphUtils.morphNameToSetNameMap.ContainsKey(morphName), morphName);
                var setName = FaceMorphUtils.morphNameToSetNameMap[morphName];
                Assert.True(FaceMorphUtils.morphSetNameJp.ContainsKey(setName), setName);
            }
        }

        [Fact]
        public void ステップ適用対象はオプションモーフのみ()
        {
            foreach (var morphName in FaceMorphUtils.saveMorphNames)
            {
                var expected = FaceMorphUtils.faceOptionMorphJp.ContainsKey(morphName);
                Assert.Equal(expected, FaceMorphUtils.IsStepMorph(morphName));
            }
        }
    }
}
