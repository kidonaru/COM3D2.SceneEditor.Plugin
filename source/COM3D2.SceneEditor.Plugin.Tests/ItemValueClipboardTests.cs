using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ItemValueClipboardTests
    {
        private static int Resolve(string[] names, TransformType[] types, string name, TransformType type)
        {
            return ItemValueClipboard.ResolveIndex(
                new List<string>(names), new List<TransformType>(types), name, type);
        }

        [Fact]
        public void 名前と型が一致する組を優先する()
        {
            var index = Resolve(
                new[] { "Bip01 Spine", "Bip01 Neck" },
                new[] { TransformType.Rotation, TransformType.Rotation },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(1, index);
        }

        [Fact]
        public void 名前が一致しても型が違えば使わない()
        {
            var index = Resolve(
                new[] { "Bip01", "Bip01 Spine" },
                new[] { TransformType.Root, TransformType.Rotation },
                "Bip01", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 一件だけなら型が同じ別名へ貼れる()
        {
            var index = Resolve(
                new[] { "Bip01 Spine" }, new[] { TransformType.Rotation },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(0, index);
        }

        [Fact]
        public void 複数件で名前が当たらなければ貼らない()
        {
            var index = Resolve(
                new[] { "Bip01 Spine", "Bip01 Neck" },
                new[] { TransformType.Rotation, TransformType.Rotation },
                "Bip01 Head", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 一件でも型が違えば貼らない()
        {
            var index = Resolve(
                new[] { "eyeclose" }, new[] { TransformType.Morph },
                "Bip01 Neck", TransformType.Rotation);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 衣装は一件でも別スロットへ貼らない()
        {
            // 衣装は項目名 (スロット) と値 (menu) が対応しており、別スロットへ写すと上衣がスカート枠に入る
            var index = Resolve(
                new[] { "wear" }, new[] { TransformType.Dress },
                "skirt", TransformType.Dress);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void 衣装も同じスロットへは貼れる()
        {
            var index = Resolve(
                new[] { "wear" }, new[] { TransformType.Dress },
                "wear", TransformType.Dress);
            Assert.Equal(0, index);
        }

        [Fact]
        public void 別名へ貼る値は貼り付け先の名前を持つ()
        {
            // 直接適用は値の name を当て先に使うため、写し元の名前のままだと写し元へ書き戻してしまう
            var source = new TransformDataMorph();
            source.Initialize("eyeclose");
            source.morphValue = 0.7f;
            ItemValueClipboard.Set(new[] { "eyeclose" }, new ITransformData[] { source });

            var pasted = ItemValueClipboard.CreateFor("mouthup", TransformType.Morph) as TransformDataMorph;

            Assert.NotNull(pasted);
            Assert.Equal("mouthup", pasted.name);
            Assert.Equal(0.7f, pasted.morphValue);
            // クリップボード側の写し元は書き換えない
            Assert.Equal("eyeclose", ItemValueClipboard.Resolve("mouthup", TransformType.Morph).name);
        }

        [Fact]
        public void 空なら貼らない()
        {
            var index = Resolve(new string[0], new TransformType[0], "a", TransformType.Morph);
            Assert.Equal(-1, index);
        }
    }
}
