using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェーダー候補の絞り込みと並びを固定する</summary>
    public class ShaderCatalogTests
    {
        [Fact]
        public void 対象の接頭辞だけを残す()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "CM3D2/Toony_Lighted", "com3d2mod/Standard_NPRToon_", "Standard", "Skybox/Cubemap", "Hidden/Foo",
            });

            Assert.Equal(new[] { "CM3D2/Toony_Lighted", "com3d2mod/Standard_NPRToon_" }, result);
        }

        [Fact]
        public void バニラを先にしてグループ内は名前順に並べる()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "com3d2mod/Standard_NPRToonV2_Lit_", "CM3D2/Toony_Lighted_Outline",
                "com3d2mod/Standard_NPRToon_", "CM3D2/Lighted",
            });

            Assert.Equal(new[]
            {
                "CM3D2/Lighted", "CM3D2/Toony_Lighted_Outline",
                "com3d2mod/Standard_NPRToonV2_Lit_", "com3d2mod/Standard_NPRToon_",
            }, result);
        }

        [Fact]
        public void 同名は1件にまとめる()
        {
            var result = ShaderCatalog.FilterNames(new[]
            {
                "CM3D2/Toony_Lighted_Outline_Tex", "CM3D2/Toony_Lighted_Outline_Tex",
            });

            Assert.Equal(new[] { "CM3D2/Toony_Lighted_Outline_Tex" }, result);
        }

        [Fact]
        public void 空やnullの名前は捨てる()
        {
            Assert.Empty(ShaderCatalog.FilterNames(new string[] { null, "" }));
        }
    }
}
